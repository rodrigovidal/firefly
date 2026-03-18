module TodoApi.Tests

open System
open System.Security.Claims
open System.Text
open System.Threading.Tasks
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens
open Xunit
open FsUnit.Xunit
open Fire
open TodoApi

// --- Helpers ---

let makeToken () =
    let handler = JsonWebTokenHandler()
    let key = SymmetricSecurityKey(Encoding.UTF8.GetBytes(App.jwtSecret))
    let descriptor = SecurityTokenDescriptor(
        SigningCredentials = SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        Subject = ClaimsIdentity([| Claim("sub", "test-user") |]),
        Expires = DateTime.UtcNow.AddHours(1.0))
    handler.CreateToken(descriptor)

// ==========================================================================
// Tests using the default InMemoryTodoStore (App.create)
// ==========================================================================

[<Fact>]
let ``GET /api/todos returns empty list initially`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/todos"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "\"todos\":[]"
    do! TestClient.stop client
}

[<Fact>]
let ``POST /api/todos requires authentication`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/api/todos" """{"Title":"test"}"""
    r.Status |> should equal 401
    do! TestClient.stop client
}

[<Fact>]
let ``POST /auth/token issues a JWT`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/auth/token" """{"userId":"tester"}"""
    r.Status |> should equal 200
    r.Body |> should haveSubstring "\"token\""
    do! TestClient.stop client
}

[<Fact>]
let ``Full CRUD lifecycle`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let token = makeToken ()
    let authed = client |> TestClient.withHeader "Authorization" $"Bearer {token}"

    // Create
    let! r1 = authed |> TestClient.post "/api/todos" """{"Title":"Buy milk"}"""
    r1.Status |> should equal 201
    r1.Body |> should haveSubstring "Buy milk"

    // List
    let! r2 = client |> TestClient.get "/api/todos"
    r2.Body |> should haveSubstring "Buy milk"

    // Update
    let! r3 = authed |> TestClient.put "/api/todos/1" """{"Title":"Buy oat milk","Completed":true}"""
    r3.Status |> should equal 200
    r3.Body |> should haveSubstring "oat milk"

    // Delete
    let! r4 = authed |> TestClient.delete "/api/todos/1"
    r4.Status |> should equal 204

    // Verify gone
    let! r5 = client |> TestClient.get "/api/todos/1"
    r5.Status |> should equal 404

    do! TestClient.stop client
}

[<Fact>]
let ``Validation rejects missing title`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let token = makeToken ()
    let authed = client |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r = authed |> TestClient.post "/api/todos" """{}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "Title"
    do! TestClient.stop client
}

[<Fact>]
let ``Invalid id returns 400`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/todos/abc"
    r.Status |> should equal 400
    do! TestClient.stop client
}

[<Fact>]
let ``Unknown route returns 404`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/nope"
    r.Status |> should equal 404
    r.Body |> should haveSubstring "\"path\":\"/nope\""
    do! TestClient.stop client
}

[<Fact>]
let ``CORS headers are present`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/todos"
    r.Headers |> List.exists (fun (k, _) -> k = "Access-Control-Allow-Origin") |> should be True
    do! TestClient.stop client
}

// ==========================================================================
// Tests with a FAKE store — swap via DI, no function records needed
//
// Pattern:
//   1. Define ITodoStore interface (in App.fs)
//   2. App.createWith(store) registers the instance in DI
//   3. Handlers resolve via req.Service<ITodoStore>()
//   4. Tests pass any ITodoStore implementation
// ==========================================================================

/// A fake store pre-loaded with data
type PreloadedStore(items: App.Todo list) =
    let mutable data = items

    interface App.ITodoStore with
        member _.GetAll() = task { return data }
        member _.GetById(id) = task { return data |> List.tryFind (fun t -> t.Id = id) }
        member _.Create(title) = task {
            let todo : App.Todo = { Id = data.Length + 1; Title = title; Completed = false }
            data <- data @ [todo]
            return todo
        }
        member _.Update(id, title, completed) = task {
            match data |> List.tryFind (fun t -> t.Id = id) with
            | Some _ ->
                let updated : App.Todo = { Id = id; Title = title; Completed = completed }
                data <- data |> List.map (fun t -> if t.Id = id then updated else t)
                return Some updated
            | None -> return None
        }
        member _.Delete(id) = task {
            let before = data.Length
            data <- data |> List.filter (fun t -> t.Id <> id)
            return data.Length < before
        }

/// A fake store that always fails — simulates database errors
type FailingStore() =
    interface App.ITodoStore with
        member _.GetAll() = task { return failwith "database connection lost" }
        member _.GetById(_) = task { return failwith "database connection lost" }
        member _.Create(_) = task { return failwith "database connection lost" }
        member _.Update(_, _, _) = task { return failwith "database connection lost" }
        member _.Delete(_) = task { return failwith "database connection lost" }

[<Fact>]
let ``InMemoryTodoStore supports direct CRUD`` () = task {
    let store = App.InMemoryTodoStore() :> App.ITodoStore
    let! created = store.Create("hello")
    created.Title |> should equal "hello"
    let! loaded = store.GetById(created.Id)
    loaded.IsSome |> should equal true
    let! deleted = store.Delete(created.Id)
    deleted |> should equal true
}

[<Fact>]
let ``With preloaded store: returns seeded todos`` () = task {
    let store = PreloadedStore([
        { Id = 1; Title = "Already here"; Completed = true }
        { Id = 2; Title = "Also here"; Completed = false }
    ])
    let (routes, config) = App.createWith store
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/todos"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "Already here"
    r.Body |> should haveSubstring "Also here"
    do! TestClient.stop client
}

[<Fact>]
let ``With preloaded store: get by id`` () = task {
    let store = PreloadedStore([
        { Id = 42; Title = "Specific todo"; Completed = false }
    ])
    let (routes, config) = App.createWith store
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/todos/42"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "Specific todo"
    do! TestClient.stop client
}

[<Fact>]
let ``Missing update returns 404`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let token = App.generateToken "test-user"
    let authed = client |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r = authed |> TestClient.put "/api/todos/999" """{"Title":"Missing","Completed":false}"""
    r.Status |> should equal 404
    r.Body |> should haveSubstring "todo not found"
    do! TestClient.stop client
}

[<Fact>]
let ``Missing delete returns 404`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let token = App.generateToken "test-user"
    let authed = client |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r = authed |> TestClient.delete "/api/todos/999"
    r.Status |> should equal 404
    r.Body |> should haveSubstring "todo not found"
    do! TestClient.stop client
}

[<Fact>]
let ``With failing store: GET returns 500`` () = task {
    let (routes, config) = App.createWith (FailingStore())
    let config = config |> App.onError (fun ex _ -> task {
        return Response.json {| error = ex.Message |} |> Response.status 500
    })
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/todos"
    r.Status |> should equal 500
    r.Body |> should haveSubstring "database connection lost"
    do! TestClient.stop client
}

[<Fact>]
let ``createWith custom not found handler includes path`` () = task {
    let store = PreloadedStore([])
    let (routes, config) = App.createWith store
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/missing"
    r.Status |> should equal 404
    r.Body |> should haveSubstring "\"path\":\"/missing\""
    do! TestClient.stop client
}
