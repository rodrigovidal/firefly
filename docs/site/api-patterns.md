# API Patterns

Fire includes modules for common REST API patterns: pagination, versioning, HATEOAS, and bulk operations.

## Pagination

Parse pagination parameters from the query string and build standardized responses.

### Offset-Based Pagination

```fsharp
let listUsers (req: Request) = task {
    match Pagination.parse req with
    | PageParams.Offset (offset, limit) ->
        let! users = db.GetUsers(offset, limit)
        let! total = db.CountUsers()
        let meta = Pagination.offsetMeta "/api/users" offset limit total
        return Pagination.respond meta users
    | PageParams.Cursor _ ->
        return Response.json {| error = "Use offset pagination" |} |> Response.status 400
}
// GET /api/users?offset=20&limit=10
```

Response format:

```json
{
  "data": [...],
  "meta": {
    "limit": 10,
    "hasMore": true,
    "next": "/api/users?offset=30&limit=10",
    "previous": "/api/users?offset=10&limit=10",
    "total": 150
  }
}
```

### Cursor-Based Pagination

```fsharp
let listEvents (req: Request) = task {
    match Pagination.parse req with
    | PageParams.Cursor (cursor, limit) ->
        let! events = db.GetEventsAfter(cursor, limit + 1)
        let hasMore = events.Length > limit
        let items = events |> List.truncate limit
        let nextCursor = if hasMore then Some (items |> List.last |> fun e -> e.Id) else None
        let meta = Pagination.cursorMeta "/api/events" limit nextCursor
        return Pagination.respond meta items
    | PageParams.Offset (offset, limit) ->
        // Also works with offset
        let! events = db.GetEvents(offset, limit)
        return Pagination.respond (Pagination.offsetMeta "/api/events" offset limit 0) events
}
// GET /api/events?cursor=abc123&limit=25
```

Defaults: `limit = 20`, `maxLimit = 100`. The `limit` is clamped to `[1, 100]`.

## API Versioning

### URL-Based Versioning

```fsharp
Route.start
|> Version.url "v1" (fun t ->
    t
    |> Route.get "/users" listUsersV1
)
|> Version.url "v2" (fun t ->
    t
    |> Route.get "/users" listUsersV2
)
// GET /v1/users, GET /v2/users
```

### Header-Based Versioning

```fsharp
Route.start
|> Route.group "/api" (fun t ->
    t
    |> Route.middleware (Version.header "X-Api-Version" "2024-01-01")
    |> Route.get "/users" listUsers
)
```

`Version.header` passes through if the header is absent (optional) but returns 400 if the header is present with a non-matching value.

Use `Version.headerRequired` to require the header:

```fsharp
Version.headerRequired "X-Api-Version" "2024-01-01"
// Missing header => 400 { "error": "Missing X-Api-Version header" }
```

## HATEOAS

Add hypermedia links to responses:

```fsharp
let getUser (id: int) (req: Request) = task {
    let! user = db.GetUser(id)
    let links = [
        Hateoas.self $"/api/users/{id}"
        Hateoas.link "orders" "GET" $"/api/users/{id}/orders"
        Hateoas.link "update" "PUT" $"/api/users/{id}"
        Hateoas.link "delete" "DELETE" $"/api/users/{id}"
    ]
    return Hateoas.respond links user
}
```

Response format:

```json
{
  "data": { "id": 1, "name": "Alice" },
  "_links": [
    { "rel": "self", "href": "/api/users/1", "httpMethod": "GET" },
    { "rel": "orders", "href": "/api/users/1/orders", "httpMethod": "GET" },
    { "rel": "update", "href": "/api/users/1", "httpMethod": "PUT" },
    { "rel": "delete", "href": "/api/users/1", "httpMethod": "DELETE" }
  ]
}
```

### Template Resolution

Use `Hateoas.resolve` to fill in link templates:

```fsharp
let linkTemplate = Hateoas.link "user" "GET" "/api/users/:id"
let resolved = linkTemplate |> Hateoas.resolve [("id", "42")]
// resolved.Href = "/api/users/42"
```

## Bulk Operations

Process multiple items in a single request:

```fsharp
// Define the operation
let createUser (input: CreateUserRequest) : Task<Result<User, string>> = task {
    try
        let! user = db.CreateUser(input)
        return Ok user
    with ex ->
        return Error ex.Message
}

// Register as a bulk endpoint
Route.start
|> Route.post "/api/users/bulk" (Bulk.handler createUser)
```

Request: `POST /api/users/bulk` with a JSON array body:

```json
[
  { "name": "Alice", "email": "alice@example.com" },
  { "name": "Bob", "email": "invalid" }
]
```

Response:

```json
{
  "succeeded": 1,
  "failed": 1,
  "total": 2,
  "results": [
    { "index": 0, "status": "success", "data": { "id": 1, "name": "Alice" } },
    { "index": 1, "status": "error", "data": { "error": "Invalid email" } }
  ]
}
```

Status codes:
- **200** — all succeeded
- **207** — partial success (multi-status)
- **422** — all failed

You can also use `Bulk.execute` directly for more control:

```fsharp
let handler (req: Request) = task {
    let! items = req.Json<CreateUserRequest list>()
    return! Bulk.execute createUser items
}
```
