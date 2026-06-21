module FireApp.Tests.ApiTests

open Xunit
open FsUnit.Xunit
open Firefly
open FireApp

[<Fact>]
let ``create todo with valid input returns 201`` () = task {
    let client = FireApp.Testing.createClient ()
    let! r = client |> TestClient.post "/api/todos" """{"title":"Write docs"}"""
    r.Status |> should equal 201
    r.Body |> should haveSubstring "Write docs"
}

[<Fact>]
let ``create todo with invalid input returns 400`` () = task {
    let client = FireApp.Testing.createClient ()
    let! r = client |> TestClient.post "/api/todos" """{"title":""}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "errors"
}

[<Fact>]
let ``list todos returns the created todo`` () = task {
    let client = FireApp.Testing.createClient ()
    let! _ = client |> TestClient.post "/api/todos" """{"title":"Ship it"}"""
    let! r = client |> FireApp.Testing.get "/api/todos"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "Ship it"
}
