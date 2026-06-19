module Firefly.Tests.OpenApiTests

open System.Text.Json
open Xunit
open FsUnit.Xunit
open Firefly

let dummyHandler : Handler = fun _ -> task { return Response.ok }

[<Fact>]
let ``OpenApi.generate produces valid JSON with correct metadata`` () =
    let routes = Route.start |> Route.get "/health" dummyHandler
    let json = OpenApi.generate "Test API" "1.0" routes
    let doc = JsonDocument.Parse(json)
    doc.RootElement.GetProperty("openapi").GetString() |> should equal "3.0.0"
    doc.RootElement.GetProperty("info").GetProperty("title").GetString() |> should equal "Test API"
    doc.RootElement.GetProperty("info").GetProperty("version").GetString() |> should equal "1.0"

[<Fact>]
let ``OpenApi.generate includes paths and methods`` () =
    let routes =
        Route.start
        |> Route.get "/users" dummyHandler
        |> Route.post "/users" dummyHandler
        |> Route.get "/users/:id" dummyHandler
    let json = OpenApi.generate "API" "1.0" routes
    let doc = JsonDocument.Parse(json)
    let paths = doc.RootElement.GetProperty("paths")
    paths.TryGetProperty("/users") |> fst |> should be True
    paths.TryGetProperty("/users/{id}") |> fst |> should be True
    let users = paths.GetProperty("/users")
    users.TryGetProperty("get") |> fst |> should be True
    users.TryGetProperty("post") |> fst |> should be True

[<Fact>]
let ``OpenApi.generate extracts path parameters`` () =
    let routes =
        Route.start
        |> Route.get "/users/:userId/posts/:postId" dummyHandler
    let json = OpenApi.generate "API" "1.0" routes
    let doc = JsonDocument.Parse(json)
    let op = doc.RootElement.GetProperty("paths").GetProperty("/users/{userId}/posts/{postId}").GetProperty("get")
    let parameters = op.GetProperty("parameters")
    parameters.GetArrayLength() |> should equal 2

[<Fact>]
let ``OpenApi.generate converts wildcard to parameter`` () =
    let routes = Route.start |> Route.get "/static/*path" dummyHandler
    let json = OpenApi.generate "API" "1.0" routes
    let doc = JsonDocument.Parse(json)
    let paths = doc.RootElement.GetProperty("paths")
    paths.TryGetProperty("/static/{path}") |> fst |> should be True

[<Fact>]
let ``OpenApi.handler serves spec as JSON`` () = task {
    let routes = Route.start |> Route.get "/users" dummyHandler
    let h = OpenApi.handler "API" "1.0" routes
    let! response = h (Unchecked.defaultof<Request>)
    response.Status |> should equal 200
    match response.Body with
    | Json bytes ->
        let json = System.Text.Encoding.UTF8.GetString(bytes)
        json |> should haveSubstring "openapi"
    | _ -> failwith "expected JSON body"
}
