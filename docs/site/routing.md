# Routing

Fire uses a trie-based router with type-safe format strings for route parameters.

## Basic Routes

Register routes with HTTP method helpers on `Route`:

```fsharp
let routes =
    Route.start
    |> Route.get    "/users"     getUsers
    |> Route.post   "/users"     createUser
    |> Route.put    "/users/%i"  updateUser
    |> Route.patch  "/users/%i"  patchUser
    |> Route.delete "/users/%i"  deleteUser
    |> Route.head   "/ping"      pingHandler
    |> Route.options "/cors"     corsHandler
```

For custom HTTP methods use `Route.method'`:

```fsharp
Route.method' "PURGE" "/cache" purgeHandler
```

## Format String Parameters

Routes use printf-style format specifiers that are parsed and type-checked at startup:

| Specifier | Type     | Example           |
|-----------|----------|-------------------|
| `%i`      | `int`    | `/users/%i`       |
| `%s`      | `string` | `/users/%s`       |
| `%b`      | `bool`   | `/active/%b`      |
| `%f`      | `float`  | `/price/%f`       |

Parameters are automatically extracted and passed to the handler as function arguments:

```fsharp
// Single parameter — int is injected directly
let getUser (id: int) (req: Request) = task {
    return Response.json {| id = id |}
}

Route.get "/users/%i" getUser
```

```fsharp
// Multiple parameters
let getComment (userId: int) (commentId: int) (req: Request) = task {
    return Response.json {| userId = userId; commentId = commentId |}
}

Route.get "/users/%i/comments/%i" getComment
```

```fsharp
// String parameter
let getBySlug (slug: string) (req: Request) = task {
    return Response.json {| slug = slug |}
}

Route.get "/articles/%s" getBySlug
```

## Handler Signatures

Fire's `HandlerFactory` inspects the handler function signature at registration time and builds a compiled invoker using expression trees. No reflection at request time.

Supported parameter binding:

| Parameter Type | Binding Source |
|---------------|---------------|
| `Request`     | The full request object |
| `int`, `string`, `bool`, `float` | Route parameters (format specifiers) |
| Interface / abstract type | Dependency injection (from `IServiceProvider`) |
| Record / class (on POST/PUT/PATCH) | JSON body (auto-deserialized) |
| Record / class (on GET/DELETE) | Query string (auto-bound) |

Examples:

```fsharp
// No parameters — unit handler
let health () = task {
    return Response.text "ok"
}
Route.get "/health" health

// Request only
let list (req: Request) = task {
    return Response.json {| path = req.Path |}
}
Route.get "/list" list

// Route param + Request
let show (id: int) (req: Request) = task {
    return Response.json {| id = id |}
}
Route.get "/items/%i" show

// Route param + JSON body
type UpdateItem = { Name: string; Price: float }
let update (id: int) (body: UpdateItem) (req: Request) = task {
    return Response.json {| id = id; name = body.Name |}
}
Route.put "/items/%i" update

// Auto-DI — interfaces are resolved from the service container
let listWithService (repo: IItemRepository) (req: Request) = task {
    let! items = repo.GetAll()
    return Response.json items
}
Route.get "/items" listWithService

// Query auto-binding — record types on GET are populated from query string
type SearchQuery = { Q: string; Page: int }
let search (query: SearchQuery) (req: Request) = task {
    return Response.json {| query = query.Q; page = query.Page |}
}
Route.get "/search" search
// GET /search?q=fire&page=2 => { query: "fire", page: 2 }
```

## Route Groups

Group related routes under a shared prefix:

```fsharp
let routes =
    Route.start
    |> Route.group "/api" (fun t ->
        t
        |> Route.get "/users" listUsers
        |> Route.post "/users" createUser
        |> Route.group "/admin" (fun t ->
            t
            |> Route.get "/stats" adminStats
        )
    )
// Registers: GET /api/users, POST /api/users, GET /api/admin/stats
```

## Route-Level Middleware

Apply middleware to specific routes or groups:

```fsharp
let routes =
    Route.start
    |> Route.get "/public" publicHandler  // no middleware
    |> Route.group "/api" (fun t ->
        t
        |> Route.middleware (Jwt.validate jwtConfig)
        |> Route.get "/profile" profileHandler  // JWT required
        |> Route.get "/settings" settingsHandler  // JWT required
    )
```

## Pipelines

Pipelines are named collections of middleware that can be applied to route groups:

```fsharp
let authPipeline =
    Pipeline.create "auth"
    |> Pipeline.plug (Jwt.validate jwtConfig)
    |> Pipeline.plug RequestId.middleware

let adminPipeline =
    Pipeline.create "admin"
    |> Pipeline.plug (Jwt.validate jwtConfig)
    |> Pipeline.plug (RateLimit.fixedWindow 10 (TimeSpan.FromMinutes 1.0) RateLimit.byIp)

let routes =
    Route.start
    |> Route.pipe "/api" authPipeline (fun t ->
        t
        |> Route.get "/me" meHandler
    )
    |> Route.pipe "/admin" adminPipeline (fun t ->
        t
        |> Route.get "/dashboard" dashHandler
    )
```

## Declarative Redirects

Register permanent or temporary redirects directly in the route table:

```fsharp
let routes =
    Route.start
    |> Redirect.permanent "/old-path" "/new-path"   // 301
    |> Redirect.temporary "/beta" "/v2"              // 302
```

## How It Works

At startup, Fire converts format strings to trie-compatible patterns (`/users/%i` becomes `/users/:__p0`) and builds a trie for O(path-length) dispatch. The `HandlerFactory` compiles expression trees for each handler so parameter extraction and invocation have zero reflection overhead at request time.
