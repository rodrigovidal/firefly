# Fire Framework Design

Fire is a minimal F# web framework built on Kestrel that provides Hono-like ergonomics through plain functions and pipe-friendly APIs. It depends on ASP.NET Core only for the HTTP server — application code never touches `HttpContext` unless opting in via escape hatch.

**Targets F# 10 / .NET 10.** Leverages `and!` in task expressions for concurrent awaiting, `[<Struct>]` ValueOption optional parameters, typed CE bindings without parentheses, and parallel compilation.

## Project Structure

```
fire/
├── src/
│   └── Fire/
│       ├── Fire.fsproj
│       ├── Request.fs
│       ├── Response.fs
│       ├── Route.fs
│       └── App.fs
├── tests/
│   └── Fire.Tests/
│       └── Fire.Tests.fsproj
├── Fire.sln
```

## Core Types

```fsharp
[<Struct>]
type Request =
    member Path: string
    member Method: string
    member Params: IReadOnlyDictionary<string, string>  // route params, backed by small array
    member Query: IReadOnlyDictionary<string, string>   // query params, backed by small array
    member Header: string -> string option
    member Headers: string -> string list   // multi-value headers
    member Body: Stream
    member Json<'T> : unit -> Task<'T>     // built-in JSON
    member Raw: HttpContext                 // escape hatch

type Handler = Request -> Task<Response>

type Middleware = Handler -> Handler
```

`Request` is a `[<Struct>]` wrapper around `HttpContext` — no heap allocation per request. It exposes a clean API while the `Raw` property provides an escape hatch for advanced scenarios (WebSockets, streaming, Kestrel-specific features). Params and Query use `IReadOnlyDictionary` backed by small arrays, avoiding immutable tree overhead.

`Handler` is a plain function. Users can build the returned `Task` with any computation expression — built-in `task {}`, Ply, or anything that produces `Task<Response>`.

`Middleware` is function composition: `Handler -> Handler`. No interfaces, no DI, no pipeline abstraction.

## Response

Response is an immutable record. Handlers return data — Fire handles writing to the HTTP response at the edge.

```fsharp
type Response = {
    Status: int
    Headers: (string * string) list
    Body: ResponseBody
}

and ResponseBody =
    | Empty
    | Text of string
    | Json of byte[]
    | Stream of Stream
```

`Json` holds pre-serialized UTF-8 bytes. Serialization happens eagerly in `Response.json` so the `Response` record is truly inert data with no runtime type dependencies.

Headers use `(string * string) list` — a flat list of key-value pairs. Duplicate keys are legal in HTTP (e.g., `Set-Cookie`), so this maps directly to the wire format. Writing to `HttpResponse` just iterates the list with no lookup needed. No map rebuilds when adding headers.

### Builder Functions

```fsharp
module Response =
    let ok = { Status = 200; Headers = []; Body = Empty }
    let text s = { ok with Body = Text s }
    let json<'T> (value: 'T) = { ok with Body = Json (JsonSerializer.SerializeToUtf8Bytes(value)) }
    let stream s = { ok with Body = Stream s }
    let status code r = { r with Status = code }
    let header key value r = { r with Headers = (key, value) :: r.Headers }

    let notFound = { ok with Status = 404 }
    let unauthorized = { ok with Status = 401 }

    let ofResult (onOk: 'T -> Response) (onError: 'E -> Response) (result: Result<'T, 'E>) =
        match result with
        | Ok value -> onOk value
        | Error err -> onError err
```

### Usage

```fsharp
fun req -> task {
    let! body = req.Json<CreateUser>()
    return
        {| id = 1; name = body.Name |}
        |> Response.json
        |> Response.header "X-Powered-By" "fire"
}
```

## Routing & Groups

Routing is built around a `RouteTable` — an immutable list of route definitions compiled into matchers at startup.

```fsharp
type RouteEntry = {
    Method: string
    Pattern: string
    Middlewares: Middleware list
    Handler: Handler
}

type RouteTable = {
    Prefix: string
    Middlewares: Middleware list
    Routes: RouteEntry list
}
```

### Route Module

```fsharp
module Route =
    let start = { Prefix = ""; Middlewares = []; Routes = [] }

    // HTTP methods
    let get pattern handler table = ...
    let post pattern handler table = ...
    let put pattern handler table = ...
    let patch pattern handler table = ...
    let delete pattern handler table = ...
    let head pattern handler table = ...
    let options pattern handler table = ...
    let method verb pattern handler table = ...   // custom methods

    // Scoped groups: prefix + middleware apply only within the function
    let group prefix (configure: RouteTable -> RouteTable) (parent: RouteTable) =
        let scoped = { Prefix = parent.Prefix + prefix; Middlewares = parent.Middlewares; Routes = [] }
        let result = configure scoped
        { parent with Routes = parent.Routes @ result.Routes }

    // Middleware (applies within current scope)
    let middleware mw table = { table with Middlewares = table.Middlewares @ [mw] }

// At startup, when building the trie, the middleware chain for each route is
// pre-composed into a single Handler -> Handler function. This means request
// dispatch calls one composed function — no list traversal or per-request composition.
```

### Usage

```fsharp
let routes =
    Route.start
    |> Route.get "/" (fun _ -> task { return Response.text "hello" })
    |> Route.group "/api" (fun api ->
        api
        |> Route.middleware withCors
        |> Route.get "/health" (fun _ -> task { return Response.ok })
        |> Route.group "/users" (fun users ->
            users
            |> Route.middleware withAuth
            |> Route.get "" (fun _ -> task {
                return Response.json {| users = [] |}
            })
            |> Route.post "" (fun req -> task {
                let! body = req.Json<CreateUser>()
                return Response.json {| id = 1 |} |> Response.status 201
            })
        )
        |> Route.get "/public" (fun _ -> task {
            return Response.text "no auth needed"
        })
    )
```

Groups nest by concatenation: `/api` + `/users` = `/api/users`. Middleware and prefix are scoped — they apply only within the `group` function, so sibling routes are unaffected. In the example above, `withAuth` applies to `/api/users` routes but not to `/api/public`.

Route params use `:param` syntax: `/users/:id/posts/:postId`. Patterns are compiled into a trie at startup. Each segment of the path is a node — static segments match exactly, `:param` segments match any value and capture it. Lookup is O(path-segment-count) regardless of how many routes are registered.

## App Startup & Kestrel Integration

The `App` module is the thin bridge to Kestrel. This is the only place ASP.NET Core appears.

```fsharp
module App =
    type FireConfig = {
        Port: int
        Host: string
        JsonOptions: JsonSerializerOptions option
        OnError: (exn -> Request -> Task<Response>) option
        NotFound: (Request -> Task<Response>) option
    }

    let defaults = {
        Port = 3000
        Host = "localhost"
        JsonOptions = None
        OnError = None
        NotFound = None
    }

    let port p config = { config with Port = p }
    let host h config = { config with Host = h }
    let jsonOptions opts config = { config with JsonOptions = Some opts }
    let onError handler config = { config with OnError = Some handler }
    let notFound handler config = { config with NotFound = Some handler }

    let run (routes: RouteTable) (config: FireConfig) : Task = ...
```

### Usage

```fsharp
let routes =
    Route.start
    |> Route.get "/" (fun _ -> task { return Response.text "hello" })
    |> Route.group "/api" (fun api ->
        api
        |> Route.middleware withCors
        |> Route.get "/users" (fun _ -> task {
            return Response.json {| users = [] |}
        })
    )

App.defaults
|> App.port 8080
|> App.onError (fun ex req -> task {
    return Response.json {| error = ex.Message |} |> Response.status 500
})
|> App.notFound (fun req -> task {
    return Response.json {| error = "not found"; path = req.Path |}
           |> Response.status 404
})
|> App.run routes
|> _.Wait()
```

Internally, `App.run` creates a `WebApplication` with a single catch-all middleware that does its own routing — bypassing ASP.NET Core's endpoint routing entirely.

## Error Handling

Two mechanisms:

1. **App-level catch-all**: `App.onError` catches unhandled exceptions. Default returns a plain 500 with no body (no stack trace leaks).

2. **Explicit Result control**: `Response.ofResult` for handlers that want fine-grained error handling. Both `Ok` and `Error` branches are configurable.

```fsharp
fun req -> task {
    let! user = req.Json<CreateUser>()
    return
        validate user
        |> Response.ofResult
            (fun u -> Response.json {| id = u.Id |} |> Response.status 201)
            (fun errs -> Response.json {| errors = errs |} |> Response.status 400)
}
```

## Out of Scope

Intentionally excluded from the initial release. Can be added later as separate packages:

- Dependency injection
- Static files
- Templating / views
- Validation
- WebSockets
- OpenAPI / Swagger
- Authentication (provided as middleware examples, not built-in)

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| ASP.NET Core dependency | Yes, minimal | Kestrel is tightly coupled; fighting it wastes effort. Only used at the edge. |
| Request type | `[<Struct>]` wrapper with escape hatch | Zero heap allocation per request, clean API for 95% of cases, `Raw` for advanced needs |
| Response type | Immutable record | Pure data, no side effects in handlers |
| Response.Json | Pre-serialized `byte[]` | Avoids boxing, no runtime type dependency in the record |
| Headers | `(string * string) list` | Flat pairs, no map rebuilds, duplicate keys legal in HTTP |
| Handler signature | `Request -> Task<Response>` | Plain functions, no framework types in signatures |
| Middleware | `Handler -> Handler`, pre-composed at startup | No per-request list traversal, single composed function per route |
| Group scoping | Function-based (`group prefix fn table`) | Prefix and middleware are scoped to the group function, preventing leaks to sibling routes |
| Task CE | User's choice | Fire returns `Task<Response>`, doesn't care how it's built |
| Routing | Own trie-based implementation | O(segment-count) lookup, natural param capture, no regex edge cases |
| JSON | Built-in via System.Text.Json | Every API needs it, but no pluggable serializer abstraction |
| ofResult | Both branches configurable | `onOk` and `onError` are both functions, no assumption about serialization format |
