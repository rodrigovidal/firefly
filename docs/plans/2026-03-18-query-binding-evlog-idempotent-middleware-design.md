# Query Auto-Binding, Evlog, Idempotency & Conditional Middleware — Design

Four independent features: query parameter auto-binding in HandlerFactory, Evlog structured logging integration, idempotency key middleware with pluggable storage, and a conditional middleware combinator.

## 1. Query Param Auto-Binding

### File: Modify `src/Fire/HandlerFactory.fs`

HandlerFactory already classifies handler parameters as `"request"`, `"di"`, `"route"`, or `"body"`. For GET/HEAD/DELETE requests, record/class parameters are currently ignored. The change: classify them as `"query"` and deserialize from query string.

### Classification logic change

For non-body methods (GET, HEAD, DELETE, OPTIONS), record and class types (non-string, non-interface) are classified as `"query"` instead of being unrecognized.

### Query binding at request time

Build a dictionary from `IQueryCollection` and deserialize via `JsonSerializer`. Returns 400 if query params can't be deserialized into the record type.

### Usage

```fsharp
type UserFilters = { search: string; limit: int; offset: int }

// GET /users?search=alice&limit=10&offset=0
Route.get "/users" (fun (req: Request) (filters: UserFilters) -> task {
    return Response.json (findUsers filters)
})
```

## 2. Evlog Integration

### File: Create `src/Fire/Evlog.fs`

Fire wraps Evlog's ASP.NET Core middleware into Fire's middleware system and exposes the logger via a Request extension.

### Middleware

```fsharp
module Evlog =
    val middleware : Middleware
```

Gets the `RequestLogger` from `HttpContext` via `context.GetEvlogLogger()` and calls next.

### Request extension

```fsharp
type Request with
    member _.Evlog : RequestLogger
```

### Setup

```fsharp
let config =
    App.defaults
    |> App.dependencyInjection (fun services ->
        services.AddEvlog(fun opts ->
            opts.Service <- "my-app"
            opts.Pretty <- true
        ) |> ignore
    )
    |> App.middleware Evlog.middleware
```

### Dependency

Add `Evlog` NuGet package to `Fire.fsproj`. Not optional — part of core.

## 3. Idempotency Key Middleware

### File: Create `src/Fire/Idempotent.fs`

### Types

```fsharp
type IdempotencyStore =
    abstract TryGet : key:string -> Task<TestResponse option>
    abstract Set : key:string -> response:TestResponse -> ttl:TimeSpan -> Task<unit>

module Idempotent =
    val inMemory : unit -> IdempotencyStore
    val middleware : store:IdempotencyStore -> ttl:TimeSpan -> Middleware
```

### Behavior

1. On state-changing requests (POST/PUT/PATCH), checks for `Idempotency-Key` header
2. If key exists in store, returns cached response immediately (replay)
3. If key is new, calls next handler, caches response, returns it
4. GET/DELETE/HEAD pass through unchanged
5. Missing `Idempotency-Key` on POST passes through (opt-in per client)
6. Returns `Idempotency-Replayed: true` header when serving cached response

### In-memory store

`ConcurrentDictionary<string, (TestResponse * DateTime)>` with lazy cleanup on access.

### Usage

```fsharp
let store = Idempotent.inMemory()

let config =
    App.defaults
    |> App.middleware (Idempotent.middleware store (TimeSpan.FromMinutes 5.0))
```

## 4. Conditional Middleware

### File: Create `src/Fire/Middleware.fs`

A single combinator:

```fsharp
[<RequireQualifiedAccess>]
module Middleware =
    val when' : predicate:(Request -> bool) -> mw:Middleware -> Middleware
```

When predicate returns true, the middleware is applied. Otherwise, request passes straight through.

### Usage

```fsharp
Route.start
|> Route.middleware (Middleware.when' (fun req -> req.Path.StartsWith("/api")) rateLimitMw)
```

## Tests

### Query Auto-Binding (6 tests)

1. `GET with record param binds from query string`
2. `GET with record param returns 400 on invalid query`
3. `GET with record and Request params both work`
4. `GET with optional fields handles missing query params`
5. `POST with record param still binds from body (not query)`
6. `query binding works with Route.group`

### Evlog (4 tests)

1. `Evlog.middleware sets event logger on request`
2. `req.Evlog returns the request logger`
3. `Evlog.middleware emits event on request completion`
4. `Evlog.middleware captures status code`

### Idempotent (7 tests)

1. `POST with Idempotency-Key caches response`
2. `POST replaying cached response returns same body and status`
3. `replayed response includes Idempotency-Replayed header`
4. `POST without Idempotency-Key passes through`
5. `GET requests pass through without caching`
6. `different keys return different responses`
7. `custom store is called for get and set`

### Middleware.when' (4 tests)

1. `applies middleware when predicate is true`
2. `skips middleware when predicate is false`
3. `composes with other middleware`
4. `works with Route.middleware`

## Files

### Create
- `src/Fire/Evlog.fs` — Evlog middleware + Request extension
- `src/Fire/Idempotent.fs` — IdempotencyStore interface, in-memory store, middleware
- `src/Fire/Middleware.fs` — Middleware.when' combinator
- `tests/Fire.Tests/EvlogTests.fs`
- `tests/Fire.Tests/IdempotentTests.fs`
- `tests/Fire.Tests/MiddlewareTests.fs`
- `tests/Fire.Tests/QueryBindingTests.fs`

### Modify
- `src/Fire/HandlerFactory.fs` — add "query" param classification
- `src/Fire/Fire.fsproj` — add Evlog NuGet dependency, add compile entries
- `tests/Fire.Tests/Fire.Tests.fsproj` — add test file compile entries

### Compile order

```xml
<Compile Include="Middleware.fs" />
<Compile Include="Evlog.fs" />
<Compile Include="Idempotent.fs" />
<Compile Include="Redirect.fs" />
```

## Implementation Order

Middleware.when' first (smallest), then query auto-binding (HandlerFactory change), then Idempotent, then Evlog (needs NuGet dep).
