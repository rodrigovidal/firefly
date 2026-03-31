# Fire

A minimal F# web framework built on Kestrel.

## Install

```bash
dotnet add package Fire
```

## Quick Start

```fsharp
open Fire

let routes =
    Route.start
    |> Route.get "/" (fun _ -> task { return Response.text "Hello, World!" })

App.defaults
|> App.port 3000
|> App.run routes
|> fun t -> t.Wait()
```

## Features

### Routing

Type-safe format strings (`%i`, `%s`, `%b`, `%f`), named params, wildcards, and groups:

```fsharp
let routes =
    Route.start
    |> Route.get "/users/%i" (fun (id: int) -> task {
        return Response.json {| id = id |}
    })
    |> Route.get "/files/*path" (fun (req: Request) -> task {
        return Response.text req.Params.["path"]
    })
    |> Route.group "/api" (fun api ->
        api
        |> Route.get "/health" (fun _ -> task { return Response.text "ok" })
        |> Route.post "/items" (fun (item: CreateItem) -> task {
            return Response.json item |> Response.status 201
        })
    )
```

All HTTP methods: `Route.get`, `Route.post`, `Route.put`, `Route.patch`, `Route.delete`, `Route.head`, `Route.options`.

### Auto Dependency Injection

Handlers receive DI services automatically. Interface parameters are resolved from `IServiceProvider`:

```fsharp
// The handler receives ITodoStore from DI and the id from the URL
Route.get "/todos/%i" (fun (store: ITodoStore) (id: int) -> task {
    let! todo = store.GetById(id)
    return Response.json todo
})

// Register services at startup
App.defaults
|> App.services [ Service.singleton<ITodoStore, InMemoryTodoStore> ]
|> App.run routes
```

POST/PUT/PATCH bodies are deserialized automatically when the parameter is a record or class:

```fsharp
Route.post "/todos" (fun (store: ITodoStore) (body: CreateTodo) -> task {
    let! todo = store.Create(body.Title)
    return Response.json todo |> Response.status 201
})
```

### Schema Validation

Zod-like typed schemas with a computation expression, zero-allocation parsing via `Utf8JsonReader`, and JSON Schema generation:

```fsharp
let createUserSchema = schema {
    let! name  = Schema.required "name"  Schema.string [Schema.minLength 1; Schema.maxLength 100]
    let! email = Schema.required "email" Schema.string [Schema.email; Schema.trim; Schema.lowercase]
    let! role  = Schema.required "role"  Schema.string [Schema.oneOf ["admin"; "user"; "viewer"]]
    let! age   = Schema.optional "age"   Schema.int 0  [Schema.min 0; Schema.max 150]
    return {| Name = name; Email = email; Role = role; Age = age |}
}

// Use as a validating handler wrapper
Route.post "/users" (Schema.validated createUserSchema (fun user -> task {
    return Response.json {| created = user.Name |}
}))

// Generate JSON Schema
let jsonSchema = Schema.toJsonSchema createUserSchema
```

Auto-generate schemas from F# types — option fields become optional, nested records and typed lists are handled recursively:

```fsharp
type Address = { Street: string; Zip: string }
type User = { Name: string; Age: int; Address: Address; Tags: string list; Nickname: string option }

let userSchema = Schema.fromType<User>()
match Schema.parseString userSchema jsonBody with
| Ok user -> // fully typed User record
| Error errors -> // list of validation errors with dotted paths
```

**Built-in rules:**

| Category | Rules |
|---|---|
| String length | `minLength`, `maxLength`, `length`, `nonempty` |
| String format | `email`, `url`, `uuid`, `ip`, `ipv4`, `ipv6`, `datetime`, `pattern` |
| String content | `startsWith`, `endsWith`, `includes`, `enum'` |
| String transforms | `trim`, `lowercase`, `uppercase` |
| Number bounds | `min`, `max`, `gt`, `lt` |
| Number checks | `positive`, `negative`, `nonnegative`, `nonpositive`, `int'`, `multipleOf` |
| Array bounds | `minItems`, `maxItems`, `nonEmpty` |

Supports nested schemas with `Schema.nest`, nullable fields with `Schema.nullable`, lists with `Schema.list`, and cross-field validation with `Schema.check`.

Parse from multiple sources: `parseString`, `parseJson`, `parseBuffer`, `parsePipe`, `parseStream`, `parseLookup`, `parseMap`.

### Middleware

Middleware composes as `Handler -> Handler`. Apply per-route or globally:

```fsharp
// Per-route group middleware
Route.start
|> Route.group "/api" (fun api ->
    api
    |> Route.middleware (Jwt.defaults "secret-key-32-chars-minimum!!" |> Jwt.validate)
    |> Route.post "/items" createHandler
)

// Global middleware
App.defaults
|> App.middleware Cors.allowAll
|> App.middleware Log.toConsole
|> App.middleware (Timeout.after (TimeSpan.FromSeconds 30.0))
|> App.middleware (RateLimit.fixedWindow 100 (TimeSpan.FromMinutes 1.0) RateLimit.byIp)
```

**Built-in middleware:**

| Module | Function |
|---|---|
| `Cors` | `Cors.allowAll`, `Cors.defaults \|> Cors.origins [...] \|> Cors.build` |
| `Log` | `Log.toConsole`, `Log.toLogger logger`, `Log.withOutput fn` |
| `Timeout` | `Timeout.after timespan` |
| `RateLimit` | `RateLimit.fixedWindow maxReqs window keyFn` |
| `Jwt` | `Jwt.defaults key \|> Jwt.issuer "..." \|> Jwt.validate` |
| `RequestId` | `RequestId.middleware` |
| `CorrelationId` | `CorrelationId.middleware` |

**Custom middleware:**

```fsharp
let timing : Middleware =
    fun next req -> task {
        let sw = System.Diagnostics.Stopwatch.StartNew()
        let! response = next req
        sw.Stop()
        return response |> Response.header "X-Time-Ms" (string sw.ElapsedMilliseconds)
    }
```

### Response Builders

```fsharp
Response.text "hello"
Response.html "<h1>hello</h1>"
Response.json {| name = "fire" |}
Response.stream fileStream
Response.ok |> Response.status 201
Response.noContent
Response.notFound
Response.unauthorized

// Headers, caching, redirects
Response.json data
|> Response.header "X-Custom" "value"
|> Response.etag "\"abc123\""
|> Response.cacheControl "public, max-age=60"
|> Response.redirect "/new-path" 302
|> Cookie.set "session" "token" (Cookie.defaults |> Cookie.httpOnly |> Cookie.secure)
```

### Static Files

```fsharp
Route.start
|> Route.get "/static/*path" (Static.serve "./public")
```

Serves files with automatic MIME type detection (html, css, js, json, images, fonts, pdf, etc.) and path traversal protection.

### OpenAPI

Auto-generated OpenAPI 3.0 spec from your route table:

```fsharp
let routes =
    Route.start
    |> Route.get "/users/:id" getUser
    |> Route.post "/users" createUser

// Serve the spec as JSON
let allRoutes =
    routes
    |> Route.get "/openapi.json" (OpenApi.handler "My API" "1.0" routes)
```

### Testing

Two modes: **direct** (in-process, no HTTP overhead) and **integration** (real HTTP server on a random port):

```fsharp
open Fire

// Direct mode — fast unit tests
let client = TestClient.create routes

let! res = client |> TestClient.get "/users/1"
assert (res.Status = 200)

let! res = client |> TestClient.post "/users" """{"name":"Alice"}"""
assert (res.Status = 201)

// Integration mode — full HTTP stack
let! client = TestClient.start routes config
let! res = client |> TestClient.get "/health"
do! TestClient.stop client
```

`TestClient.withHeader` adds default headers (e.g., auth tokens) to all requests.

### Developer Experience

Fire now ships the first pieces of an opinionated Phoenix-style dev loop:

```fsharp
let config =
    App.defaults
    |> App.middleware RequestId.middleware
    |> App.middleware CorrelationId.middleware
    |> App.onError DevErrorPage.handler
```

`DevErrorPage.handler` returns a structured HTML error page in development with request metadata, route params, request ID, correlation ID, and stack trace.

The repo also includes `Fire.Cli` with two workflow commands:

```bash
dotnet run --project src/Fire.Cli/Fire.Cli.fsproj -- new MyApp
fire dev --project src/MyApp/MyApp.fsproj
```

`fire new` generates an opinionated app layout with:

- `App.fs`, `Endpoint.fs`, `Router.fs`
- `Controllers/`, `Views/`, `Components/`, `Layouts/`
- `Assets/`, `Static/`, `Config/`
- `tests/<App>.Tests` with fixtures and smoke tests

`fire dev` wraps `dotnet watch run` and the scaffold includes watch items for source, assets, and tests.

## Examples

- [**todo-api**](examples/todo-api) — CRUD with JWT auth, DI, rate limiting, OpenAPI
- [**blog-api**](examples/blog-api) — Nested routes, ETags, content negotiation, cookies
- [**url-shortener**](examples/url-shortener) — Form handling, redirects, custom 404

## License

MIT
