module TodoApiDb.Tests

open System
open System.IO
open Xunit
open FsUnit.Xunit
open Fire
open TodoApiDb

let createTestApp () =
    let dbPath = Path.Combine(Path.GetTempPath(), $"fire-db-test-{Guid.NewGuid():N}.db")
    let (routes, config) = App.create dbPath
    (routes, config, dbPath)

[<Fact>]
let ``CRUD lifecycle with SQLite`` () = task {
    let (routes, config, dbPath) = createTestApp ()
    try
        let! client = TestClient.start routes config

        // Initially empty
        let! r0 = client |> TestClient.get "/api/todos"
        r0.Status |> should equal 200
        r0.Body |> should haveSubstring "[]"

        // Create
        let! r1 = client |> TestClient.post "/api/todos" """{"title":"Buy milk"}"""
        r1.Status |> should equal 201
        r1.Body |> should haveSubstring "Buy milk"

        // List
        let! r2 = client |> TestClient.get "/api/todos"
        r2.Body |> should haveSubstring "Buy milk"

        // Get by id
        let! r3 = client |> TestClient.get "/api/todos/1"
        r3.Status |> should equal 200
        r3.Body |> should haveSubstring "Buy milk"

        // Update
        let! r4 = client |> TestClient.put "/api/todos/1" """{"title":"Buy oat milk","completed":true}"""
        r4.Status |> should equal 200
        r4.Body |> should haveSubstring "oat milk"

        // Delete
        let! r5 = client |> TestClient.delete "/api/todos/1"
        r5.Status |> should equal 204

        // Verify deleted
        let! r6 = client |> TestClient.get "/api/todos/1"
        r6.Status |> should equal 404

        do! TestClient.stop client
    finally
        if File.Exists(dbPath) then File.Delete(dbPath)
}

[<Fact>]
let ``Schema validation rejects invalid input`` () = task {
    let (routes, config, dbPath) = createTestApp ()
    try
        let! client = TestClient.start routes config

        // Empty title
        let! r = client |> TestClient.post "/api/todos" """{"title":""}"""
        r.Status |> should equal 400

        // Missing title
        let! r2 = client |> TestClient.post "/api/todos" """{}"""
        r2.Status |> should equal 400

        do! TestClient.stop client
    finally
        if File.Exists(dbPath) then File.Delete(dbPath)
}

[<Fact>]
let ``404 for non-existent todo`` () = task {
    let (routes, config, dbPath) = createTestApp ()
    try
        let! client = TestClient.start routes config
        let! r = client |> TestClient.get "/api/todos/999"
        r.Status |> should equal 404
        do! TestClient.stop client
    finally
        if File.Exists(dbPath) then File.Delete(dbPath)
}

[<Fact>]
let ``Title max length validation`` () = task {
    let (routes, config, dbPath) = createTestApp ()
    try
        let! client = TestClient.start routes config
        let longTitle = String('a', 201)
        let! r = client |> TestClient.post "/api/todos" $"""{{"title":"{longTitle}"}}"""
        r.Status |> should equal 400
        r.Body |> should haveSubstring "200"
        do! TestClient.stop client
    finally
        if File.Exists(dbPath) then File.Delete(dbPath)
}
