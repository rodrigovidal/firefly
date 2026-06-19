---
title: "Todo API"
description: "A CRUD todo REST API with JWT-protected writes, schema validation, DI, rate limiting, CORS and OpenAPI."
group: "Guides"
order: 1
---

# Todo API

This guide walks through a complete todo REST API built with Firefly. It exposes public read endpoints and JWT-protected create/update/delete endpoints backed by an in-memory store, and demonstrates how Firefly wires together routing, dependency injection, schema validation, middleware and an auto-generated OpenAPI spec.

## What you'll learn

- Routing with `Route.start`, route groups, and typed path parameters
- Dependency injection of a service into handlers via `App.services`
- JSON request validation with `Schema.fromType` and the `schema { }` builder
- JWT issuing and validation as route middleware
- App-wide middleware: CORS, fixed-window rate limiting, and request logging
- An auto-generated `/openapi.json` endpoint

## Types and store

The domain is a single `Todo` record plus the request DTOs. State lives behind an `ITodoStore` interface so handlers depend on an abstraction rather than a concrete implementation.

```fsharp
open System.Collections.Concurrent
open System.Threading
open Firefly

type Todo = { Id: int; Title: string; Completed: bool }
type LoginRequest = { UserId: string }
type UpdateTodoInput = { Title: string; Completed: bool option }

type ITodoStore =
    abstract GetAll: unit -> Task<Todo list>
    abstract GetById: int -> Task<Todo option>
    abstract Create: string -> Task<Todo>
    abstract Update: int * string * bool -> Task<Todo option>
    abstract Delete: int -> Task<bool>
```

The `InMemoryTodoStore` implements that interface with a `ConcurrentDictionary` and an `Interlocked`-incremented id counter, so it is safe under concurrent requests.

```fsharp
type InMemoryTodoStore() =
    let todos = ConcurrentDictionary<int, Todo>()
    let mutable nextId = 0

    interface ITodoStore with
        member _.GetAll() = task { return todos.Values |> Seq.toList }
        member _.Create(title) = task {
            let id = Interlocked.Increment(&nextId)
            let todo = { Id = id; Title = title; Completed = false }
            todos.[id] <- todo
            return todo
        }
        // GetById / Update / Delete omitted for brevity
```

## Schemas

Firefly can derive a schema straight from a record type. Option fields become optional automatically, so `Completed` may be omitted on update.

```fsharp
// fromType: auto-generates schema from record type (cached, zero reflection after first call)
let loginSchema = Schema.fromType<LoginRequest>()
let updateTodoSchema = Schema.fromType<UpdateTodoInput>()
```

When you need validation rules, use the `schema { }` computation expression with combinators like `nonempty`, `maxLength` and `trim`.

```fsharp
let createTodoSchema = schema {
    let! title = Schema.required "Title" Schema.string [ Schema.nonempty; Schema.maxLength 200; Schema.trim ]
    return {| Title = title |}
}
```

## Issuing JWTs

The `/auth/token` route validates the login body against `loginSchema` and mints a signed JWT. `Schema.parseRequest` returns an `Ok`/`Error` result, so validation failures map cleanly to a `400`.

```fsharp
Route.start
|> Route.post "/auth/token" (fun (req: Request) -> task {
    match! Schema.parseRequest loginSchema req with
    | Ok login ->
        let token = generateToken login.UserId
        return Response.json {| token = token |}
    | Error errors ->
        return Response.json {| errors = errors |} |> Response.status 400
})
```

`generateToken` uses `JsonWebTokenHandler` with an HMAC-SHA256 key derived from a shared secret and a 24-hour expiry.

## Routes and the protected group

Reads are public. `Route.group` scopes a path prefix, and `Route.middleware jwtAuth` applies JWT validation to every route declared *after* it in the group — so `POST`, `PUT` and `DELETE` require a token while the two `GET` routes do not. Handlers receive injected dependencies (`store`) and typed path params (`id: int` from `/%i`) as plain function arguments.

```fsharp
let jwtAuth = Jwt.defaults jwtSecret |> Jwt.validate

|> Route.group "/api/todos" (fun group ->
    group
    |> Route.get "" (fun (store: ITodoStore) -> task {
        let! items = store.GetAll()
        return Response.json {| todos = items |}
    })
    |> Route.get "/%i" (fun (store: ITodoStore) (id: int) -> task {
        match! store.GetById(id) with
        | Some t -> return Response.json t
        | None -> return Response.json {| error = "todo not found" |} |> Response.status 404
    })
    // Everything below requires a valid JWT
    |> Route.middleware jwtAuth
    |> Route.post "" (fun (store: ITodoStore) (req: Request) -> task {
        match! Schema.parseRequest createTodoSchema req with
        | Ok input ->
            let! todo = store.Create(input.Title)
            return Response.json todo |> Response.status 201
        | Error errors ->
            return Response.json {| errors = errors |} |> Response.status 400
    })
    |> Route.put "/%i" (fun (store: ITodoStore) (id: int) (req: Request) -> task {
        match! Schema.parseRequest updateTodoSchema req with
        | Ok input ->
            let completed = input.Completed |> Option.defaultValue false
            match! store.Update(id, input.Title, completed) with
            | Some updated -> return Response.json updated
            | None -> return Response.json {| error = "todo not found" |} |> Response.status 404
        | Error errors ->
            return Response.json {| errors = errors |} |> Response.status 400
    })
    |> Route.delete "/%i" (fun (store: ITodoStore) (id: int) -> task {
        let! deleted = store.Delete(id)
        if deleted then return Response.noContent
        else return Response.json {| error = "todo not found" |} |> Response.status 404
    })
)
```

## OpenAPI

A single line adds a self-describing spec endpoint generated from the route table.

```fsharp
let allRoutes =
    routes
    |> Route.get "/openapi.json" (OpenApi.handler "Todo API" "1.0" routes)
```

## App startup

The app factory composes defaults with app-wide middleware (CORS, a 100-request-per-minute fixed-window rate limit keyed by IP), registers `ITodoStore` as a singleton, and supplies a JSON `notFound` handler.

```fsharp
let create () =
    let config =
        App.defaults
        |> App.port 0
        |> App.middleware Cors.allowAll
        |> App.middleware (RateLimit.fixedWindow 100 (TimeSpan.FromMinutes 1.0) RateLimit.byIp)
        |> App.services [ Service.singleton<ITodoStore, InMemoryTodoStore> ]
        |> App.notFound (fun (req: Request) -> task {
            return Response.json {| error = "not found"; path = req.Path |} |> Response.status 404
        })
    (allRoutes, config)
```

`Program.fs` builds the app, adds console logging, and runs it.

```fsharp
open Firefly
open TodoApi

let (routes, config) = App.create ()
let config' = config |> App.middleware Log.toConsole
App.run routes config' System.Threading.CancellationToken.None
|> fun t -> t.Wait()
```

## Running it

```bash
dotnet run --project examples/todo-api
```

```bash
# 1. Get a token
curl -s -X POST http://localhost:3000/auth/token \
  -H 'Content-Type: application/json' \
  -d '{"UserId":"alice"}'

# 2. List todos (public)
curl -s http://localhost:3000/api/todos

# 3. Create a todo (auth required)
curl -s -X POST http://localhost:3000/api/todos \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"Title":"Write the docs"}'

# 4. Fetch the OpenAPI spec
curl -s http://localhost:3000/openapi.json
```

```json
{ "token": "<jwt>" }
```

## Source

The full example lives at [`examples/todo-api/`](examples/todo-api/) in the repository.
