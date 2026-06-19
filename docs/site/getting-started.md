# Getting Started

This guide walks you through creating your first Fire application from scratch.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- A terminal / shell

## Create a New Project

The Fire CLI scaffolds a complete project with routing, configuration, and tests:

```bash
fire new MyApp
cd MyApp
```

This generates:

```
MyApp/
  MyApp.sln
  src/MyApp/
    App.fs          # Entry point
    Router.fs       # Route definitions
    Endpoint.fs     # Handler functions
    Config/
      Dev.fs        # Development config
      Prod.fs       # Production config
    MyApp.fsproj
  tests/MyApp.Tests/
    Fixtures.fs
    IntegrationTests.fs
    ControllerTests.fs
    MyApp.Tests.fsproj
```

## Run in Development Mode

```bash
fire dev
```

This starts the server with `dotnet watch run`, enabling live reload and auto-restart on file changes. The environment is set to `Development` automatically.

## Your First App from Scratch

If you prefer to start from an empty project:

```fsharp
open Firefly
open System.Threading

[<EntryPoint>]
let main _ =
    let routes =
        Route.start
        |> Route.get "/" (fun _ -> task {
            return Response.text "Hello, Fire!"
        })

    App.run routes App.defaults CancellationToken.None
    |> Async.AwaitTask
    |> Async.RunSynchronously
    0
```

## Core Concepts

### Routes

Routes are built by piping through `Route.start`:

```fsharp
let routes =
    Route.start
    |> Route.get "/hello" (fun _ -> task { return Response.text "Hello" })
    |> Route.post "/users" (fun (req: Request) -> task {
        let! body = req.Json<CreateUser>()
        return Response.json body |> Response.status 201
    })
```

### Request and Response

Every handler is a function that takes a `Request` and returns a `Task<Response>`:

```fsharp
type Handler = Request -> Task<Response>
```

The `Request` gives you access to:

```fsharp
req.Path          // string — URL path
req.Method        // string — HTTP method
req.Params        // IReadOnlyDictionary — route parameters
req.Query         // IReadOnlyDictionary — query string
req.Header "name" // string option
req.Cookie "name" // string option
req.Json<'T>()    // Task<'T> — parse JSON body
req.Text()        // Task<string> — raw body text
req.Form()        // Task<IReadOnlyDictionary> — form data
req.Files()       // Task<UploadedFile list> — uploaded files
req.RequestId     // string option
req.CorrelationId // string option
req.Accepts "type" // bool — content negotiation
req.ContentType   // string option
req.Raw           // HttpContext — escape hatch
```

Build responses with the `Response` module:

```fsharp
Response.text "plain text"
Response.json {| name = "Fire" |}
Response.html "<h1>Hello</h1>"
Response.ok                        // 200 empty
Response.created                   // 201 empty
Response.noContent                 // 204 empty
Response.notFound                  // 404 empty
Response.unauthorized              // 401 empty
Response.file "path/to/file.pdf"
Response.stream someStream

// Chainable modifiers
Response.json data
|> Response.status 201
|> Response.header "X-Custom" "value"
|> Response.cookie "session" "abc123"
|> Response.etag "\"v1\""
|> Response.cacheControl "public, max-age=3600"
```

### Configuration

Configure the server via the `App` module:

```fsharp
let config =
    App.defaults
    |> App.port 8080
    |> App.host "0.0.0.0"
    |> App.onError (fun ex req -> task {
        return Response.json {| error = ex.Message |} |> Response.status 500
    })
    |> App.notFound (fun req -> task {
        return Response.json {| error = "Not found" |} |> Response.status 404
    })
    |> App.shutdownTimeout (System.TimeSpan.FromSeconds 30.0)

App.run routes config CancellationToken.None
```

### Middleware

Apply middleware globally or per-route:

```fsharp
// Global — applies to all routes
let config =
    App.defaults
    |> App.middleware Cors.allowAll
    |> App.middleware SecureHeaders.middleware

// Per-route group
Route.start
|> Route.group "/api" (fun t ->
    t
    |> Route.middleware (Jwt.validate jwtConfig)
    |> Route.get "/profile" profileHandler
)
```

## Next Steps

- [Routing](routing.md) — format strings, groups, wildcards
- [Middleware](middleware.md) — all 15+ built-in middleware
- [Validation](validation.md) — schema validation with Flame
- [Dependency Injection](di.md) — services and auto-DI
- [Testing](testing.md) — direct and integration test helpers
