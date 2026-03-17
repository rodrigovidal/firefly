module Fire.IntegrationTests.TodoApiTests

open System
open System.Collections.Concurrent
open System.Security.Claims
open System.Text
open System.Threading
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens
open Xunit
open FsUnit.Xunit
open Fire

// --- Setup (mirrors todo-api example) ---

type Todo = { Id: int; Title: string; Completed: bool }
type CreateTodo = { Title: string }
type UpdateTodo = { Title: string; Completed: bool }

let jwtSecret = "integration-test-secret-key-must-be-32-chars!!"

let generateToken (userId: string) =
    let handler = JsonWebTokenHandler()
    let key = SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
    let descriptor = SecurityTokenDescriptor(
        SigningCredentials = SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        Subject = ClaimsIdentity([| Claim("sub", userId) |]),
        Expires = DateTime.UtcNow.AddHours(1.0))
    handler.CreateToken(descriptor)

let buildTodoApp () =
    let todos = ConcurrentDictionary<int, Todo>()
    let mutable nextId = 1
    let jwtAuth = Jwt.defaults jwtSecret |> Jwt.validate

    let routes =
        Route.start
        |> Route.group "/api/todos" (fun group ->
            group
            |> Route.get "" (fun _ -> task {
                return Response.json {| todos = todos.Values |> Seq.toList |}
            })
            |> Route.get "/:id" (
                Validate.param ["id", Validate.isInt] (fun req -> task {
                    let id = int req.Params.["id"]
                    match todos.TryGetValue(id) with
                    | true, todo -> return Response.json todo
                    | false, _ -> return Response.json {| error = "not found" |} |> Response.status 404
                }))
            |> Route.middleware jwtAuth
            |> Route.post "" (
                Validate.body
                    (Validate.required "title" (fun (t: CreateTodo) -> t.Title))
                    (fun body -> task {
                        let id = Interlocked.Increment(&nextId) - 1
                        let todo = { Id = id; Title = body.Title; Completed = false }
                        todos.[id] <- todo
                        return Response.json todo |> Response.status 201
                    }))
            |> Route.put "/:id" (fun req -> task {
                let id = int req.Params.["id"]
                let! body = req.Json<UpdateTodo>()
                match todos.TryGetValue(id) with
                | true, _ ->
                    let updated = { Id = id; Title = body.Title; Completed = body.Completed }
                    todos.[id] <- updated
                    return Response.json updated
                | false, _ -> return Response.json {| error = "not found" |} |> Response.status 404
            })
            |> Route.delete "/:id" (fun req -> task {
                let id = int req.Params.["id"]
                match todos.TryRemove(id) with
                | true, _ -> return Response.noContent
                | false, _ -> return Response.json {| error = "not found" |} |> Response.status 404
            })
        )

    let config =
        App.defaults
        |> App.port 0
        |> App.middleware Cors.allowAll
        |> App.notFound (fun _ -> task {
            return Response.json {| error = "not found" |} |> Response.status 404
        })

    (routes, config)

// --- Tests ---

[<Fact>]
let ``Todo: list empty todos`` () = task {
    let (routes, config) = buildTodoApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/todos"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "\"todos\":[]"
    do! TestClient.stop client
}

[<Fact>]
let ``Todo: create requires auth`` () = task {
    let (routes, config) = buildTodoApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/api/todos" """{"Title":"test"}"""
    r.Status |> should equal 401
    do! TestClient.stop client
}

[<Fact>]
let ``Todo: full CRUD lifecycle`` () = task {
    let (routes, config) = buildTodoApp ()
    let! client = TestClient.start routes config
    let token = generateToken "user-1"
    let authed = client |> TestClient.withHeader "Authorization" $"Bearer {token}"

    // Create
    let! r1 = authed |> TestClient.post "/api/todos" """{"Title":"Buy milk"}"""
    r1.Status |> should equal 201
    r1.Body |> should haveSubstring "Buy milk"

    // Read
    let! r2 = client |> TestClient.get "/api/todos"
    r2.Status |> should equal 200
    r2.Body |> should haveSubstring "Buy milk"

    // Update (need to extract id from create response)
    let! r3 = authed |> TestClient.put "/api/todos/1" """{"Title":"Buy oat milk","Completed":true}"""
    r3.Status |> should equal 200
    r3.Body |> should haveSubstring "oat milk"
    r3.Body |> should haveSubstring "true"

    // Delete
    let! r4 = authed |> TestClient.delete "/api/todos/1"
    r4.Status |> should equal 204

    // Verify deleted
    let! r5 = client |> TestClient.get "/api/todos/1"
    r5.Status |> should equal 404

    do! TestClient.stop client
}

[<Fact>]
let ``Todo: validation rejects empty title`` () = task {
    let (routes, config) = buildTodoApp ()
    let! client = TestClient.start routes config
    let token = generateToken "user-1"
    let authed = client |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r = authed |> TestClient.post "/api/todos" """{"Title":""}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "title is required"
    do! TestClient.stop client
}

[<Fact>]
let ``Todo: param validation rejects non-integer id`` () = task {
    let (routes, config) = buildTodoApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/todos/abc"
    r.Status |> should equal 400
    r.Body |> should haveSubstring "integer"
    do! TestClient.stop client
}

[<Fact>]
let ``Todo: CORS headers present`` () = task {
    let (routes, config) = buildTodoApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/todos"
    r.Headers |> List.exists (fun (k, _) -> k = "Access-Control-Allow-Origin") |> should be True
    do! TestClient.stop client
}

[<Fact>]
let ``Todo: 404 for unknown route`` () = task {
    let (routes, config) = buildTodoApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/nonexistent"
    r.Status |> should equal 404
    do! TestClient.stop client
}
