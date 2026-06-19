---
title: "Todo API with a Database"
description: "Build a CRUD todo API that persists data to SQLite with Dapper, using DI, schema-validated handlers and JSON responses."
group: "Guides"
order: 4
---

# Todo API with a Database

This guide builds a fully persistent CRUD todo API on top of Firefly. Instead of keeping todos in memory, it stores them in a SQLite database accessed through [Dapper](https://github.com/DapperLib/Dapper) and `Microsoft.Data.Sqlite`, with a fresh connection resolved per request from Firefly's dependency injection container. Along the way you'll see schema-validated request parsing, typed JSON responses, and CORS + logging middleware.

## What you'll learn

- Registering a per-request `IDbConnection` with Firefly's DI (`Service.transientFactory`)
- Persisting and querying data with Dapper against SQLite
- A small, plain-function data store (no heavyweight repository abstraction)
- Schema-validated `POST`/`PUT` bodies and typed JSON handlers
- Returning the right status codes (`201`, `204`, `404`, `400`)

## The data store

`Db.fs` holds everything database-related: the `Todo` record, a connection factory, table creation, and the CRUD query functions. Dapper maps query results straight onto the `[<CLIMutable>]` record, and parameterized queries use anonymous records as the parameter object.

```fsharp
module TodoApiDb.Db

open System.Data
open Dapper
open Microsoft.Data.Sqlite

[<CLIMutable>]
type Todo = {
    Id: int
    Title: string
    Completed: bool
    CreatedAt: string
}

let connect (connectionString: string) : IDbConnection =
    let conn = new SqliteConnection(connectionString)
    conn.Open()
    conn :> IDbConnection

let ensureTable (conn: IDbConnection) =
    conn.Execute("""
        CREATE TABLE IF NOT EXISTS Todos (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Title TEXT NOT NULL,
            Completed INTEGER NOT NULL DEFAULT 0,
            CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
        )
    """) |> ignore
```

The CRUD functions are ordinary functions that take a connection and return plain values, so handlers can call them directly. `create` inserts then reads the row back so callers always get the full record:

```fsharp
let getAll (conn: IDbConnection) =
    conn.Query<Todo>("SELECT * FROM Todos ORDER BY Id") |> Seq.toList

let getById (conn: IDbConnection) (id: int) =
    conn.Query<Todo>("SELECT * FROM Todos WHERE Id = @Id", {| Id = id |}) |> Seq.tryHead

let create (conn: IDbConnection) (title: string) =
    let id = conn.ExecuteScalar<int64>(
        "INSERT INTO Todos (Title) VALUES (@Title); SELECT last_insert_rowid()",
        {| Title = title |})
    getById conn (int id) |> Option.get

let update (conn: IDbConnection) (id: int) (title: string) (completed: bool) =
    let rows = conn.Execute(
        "UPDATE Todos SET Title = @Title, Completed = @Completed WHERE Id = @Id",
        {| Id = id; Title = title; Completed = if completed then 1 else 0 |})
    if rows > 0 then getById conn id
    else None

let delete (conn: IDbConnection) (id: int) =
    conn.Execute("DELETE FROM Todos WHERE Id = @Id", {| Id = id |}) > 0
```

## Validation schemas

`App.fs` declares two schemas with the `schema { ... }` computation expression. These describe the shape of incoming JSON and run the same validation rules (`nonempty`, `maxLength`, `trim`) before any database work happens.

```fsharp
open System.Data
open Microsoft.Extensions.DependencyInjection
open Flame
open Firefly

let createTodoSchema = schema {
    let! title = Schema.required "title" Schema.string [ Schema.nonempty; Schema.maxLength 200; Schema.trim ]
    return {| Title = title |}
}

let updateTodoSchema = schema {
    let! title = Schema.required "title" Schema.string [ Schema.nonempty; Schema.trim ]
    let! completed = Schema.optional "completed" Schema.bool false []
    return {| Title = title; Completed = completed |}
}
```

## Resolving the connection per request

Connections need deterministic disposal, so the app resolves an `IDbConnection` from the request's service provider manually and binds it with `use`. That closes the connection at the end of each handler.

```fsharp
// Resolve a fresh DB connection from DI; `use` ensures it's closed after each request.
let private openDb (req: Request) =
    req.Raw.RequestServices.GetRequiredService<IDbConnection>()
```

## The handlers

Routes are built with the `Route` pipeline. Path parameters like `%i` are passed to the handler as typed arguments. Each handler opens a connection, calls into `Db`, and returns a JSON response (with an explicit status where it matters).

```fsharp
let routes =
    Route.start
    |> Route.get "/api/todos" (fun (req: Request) -> task {
        use conn = openDb req
        let todos = Db.getAll conn
        return Response.json todos
    })
    |> Route.get "/api/todos/%i" (fun (id: int) (req: Request) -> task {
        use conn = openDb req
        match Db.getById conn id with
        | Some todo -> return Response.json todo
        | None -> return Response.json {| error = "not found" |} |> Response.status 404
    })
    |> Route.post "/api/todos" (fun (req: Request) -> task {
        match! Schema.parseRequest createTodoSchema req with
        | Ok input ->
            use conn = openDb req
            let todo = Db.create conn input.Title
            return Response.json todo |> Response.status 201
        | Error errors ->
            return Response.json {| errors = errors |} |> Response.status 400
    })
    |> Route.put "/api/todos/%i" (fun (id: int) (req: Request) -> task {
        match! Schema.parseRequest updateTodoSchema req with
        | Ok input ->
            use conn = openDb req
            match Db.update conn id input.Title input.Completed with
            | Some todo -> return Response.json todo
            | None -> return Response.json {| error = "not found" |} |> Response.status 404
        | Error errors ->
            return Response.json {| errors = errors |} |> Response.status 400
    })
    |> Route.delete "/api/todos/%i" (fun (id: int) (req: Request) -> task {
        use conn = openDb req
        if Db.delete conn id then return Response.noContent
        else return Response.json {| error = "not found" |} |> Response.status 404
    })
```

## App startup and services wiring

`create` ensures the table exists, then assembles the app config: CORS, request logging, a `404` fallback, and — crucially — the connection factory registered as a transient service so every request gets its own connection.

```fsharp
let create (dbPath: string) =
    let connectionString = $"Data Source={dbPath}"
    // Ensure table exists on startup
    use initConn = Db.connect connectionString
    Db.ensureTable initConn

    let config =
        App.defaults
        |> App.port 0
        |> App.middleware Cors.allowAll
        |> App.middleware Log.toConsole
        |> App.services [ Service.transientFactory (fun _ -> Db.connect connectionString) ]
        |> App.notFound (fun _ -> task {
            return Response.json {| error = "not found" |} |> Response.status 404
        })
    (routes, config)
```

`Program.fs` wires it together, overrides the port to `3000`, and runs the app:

```fsharp
open System.Threading
open TodoApiDb

let dbPath = "todos.db"
let (routes, config) = App.create dbPath

let config' = { config with Port = 3000 }
Firefly.App.run routes config' CancellationToken.None
|> fun t -> t.GetAwaiter().GetResult()
```

## Running it

```bash
dotnet run --project examples/todo-api-db
# Todo API (SQLite + Dapper) running on http://localhost:3000

# Create a todo (schema validated)
curl -X POST http://localhost:3000/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title":"Buy milk"}'

# List all todos
curl http://localhost:3000/api/todos

# Mark it completed
curl -X PUT http://localhost:3000/api/todos/1 \
  -H "Content-Type: application/json" \
  -d '{"title":"Buy milk","completed":true}'

# Delete it (returns 204 No Content)
curl -X DELETE http://localhost:3000/api/todos/1
```

A created todo comes back as JSON:

```json
{ "id": 1, "title": "Buy milk", "completed": false, "createdAt": "2026-06-19 12:00:00" }
```

## Source

The full example lives in [`examples/todo-api-db/`](https://github.com/) — see `Db.fs` (data store), `App.fs` (schemas, routes, services) and `Program.fs` (startup).
