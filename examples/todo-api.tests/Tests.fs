module TodoApi.Tests

open System
open System.Security.Claims
open System.Text
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens
open Xunit
open FsUnit.Xunit
open Fire
open TodoApi

let makeToken () =
    let handler = JsonWebTokenHandler()
    let key = SymmetricSecurityKey(Encoding.UTF8.GetBytes(App.jwtSecret))
    let descriptor = SecurityTokenDescriptor(
        SigningCredentials = SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        Subject = ClaimsIdentity([| Claim("sub", "test-user") |]),
        Expires = DateTime.UtcNow.AddHours(1.0))
    handler.CreateToken(descriptor)

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
let ``Validation rejects empty title`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let token = makeToken ()
    let authed = client |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r = authed |> TestClient.post "/api/todos" """{"Title":""}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "title is required"
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
