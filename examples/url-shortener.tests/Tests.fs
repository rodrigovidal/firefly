module UrlShortener.Tests

open Xunit
open FsUnit.Xunit
open Fire
open UrlShortener

let mutable codeCounter = 0

let deterministicCode () =
    codeCounter <- codeCounter + 1
    $"code{codeCounter}"

[<Fact>]
let ``GET / returns landing page`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "Fire URL Shortener"
    r.Body |> should haveSubstring "<html"
    do! TestClient.stop client
}

[<Fact>]
let ``POST /api/shorten creates a short URL`` () = task {
    codeCounter <- 0
    let (routes, config) = App.createWith deterministicCode
    let! client = TestClient.start routes config
    let jsonClient = client |> TestClient.withHeader "Content-Type" "application/json"
    let! r = jsonClient |> TestClient.post "/api/shorten" """{"Url":"https://example.com"}"""
    r.Status |> should equal 201
    r.Body |> should haveSubstring "code1"
    r.Body |> should haveSubstring "https://example.com"
    do! TestClient.stop client
}

[<Fact>]
let ``POST /api/shorten validates empty url`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let jsonClient = client |> TestClient.withHeader "Content-Type" "application/json"
    let! r = jsonClient |> TestClient.post "/api/shorten" """{"Url":""}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "url is required"
    do! TestClient.stop client
}

[<Fact>]
let ``POST /api/shorten validates url scheme`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let jsonClient = client |> TestClient.withHeader "Content-Type" "application/json"
    let! r = jsonClient |> TestClient.post "/api/shorten" """{"Url":"ftp://bad.com"}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "url must start with http"
    do! TestClient.stop client
}

[<Fact>]
let ``Redirect increments click count`` () = task {
    codeCounter <- 100
    let (routes, config) = App.createWith deterministicCode
    let! client = TestClient.start routes config
    let jsonClient = client |> TestClient.withHeader "Content-Type" "application/json"

    // Create a short URL
    let! r1 = jsonClient |> TestClient.post "/api/shorten" """{"Url":"https://example.com/page"}"""
    r1.Status |> should equal 201

    // Redirect (click)
    let! r2 = client |> TestClient.get "/code101"
    r2.Status |> should equal 302
    r2.Headers |> List.exists (fun (k, v) -> k = "Location" && v = "https://example.com/page") |> should be True

    // Check stats - should have 1 click
    let! r3 = client |> TestClient.get "/api/stats/code101"
    r3.Status |> should equal 200
    r3.Body |> should haveSubstring "\"Clicks\":1"

    do! TestClient.stop client
}

[<Fact>]
let ``GET /api/stats returns all URLs`` () = task {
    codeCounter <- 200
    let (routes, config) = App.createWith deterministicCode
    let! client = TestClient.start routes config
    let jsonClient = client |> TestClient.withHeader "Content-Type" "application/json"

    let! _ = jsonClient |> TestClient.post "/api/shorten" """{"Url":"https://a.com"}"""
    let! _ = jsonClient |> TestClient.post "/api/shorten" """{"Url":"https://b.com"}"""

    let! r = client |> TestClient.get "/api/stats"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "\"count\":2"

    do! TestClient.stop client
}

[<Fact>]
let ``GET /api/stats/:code returns 404 for unknown code`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/stats/nonexistent"
    r.Status |> should equal 404
    r.Body |> should haveSubstring "not found"
    do! TestClient.stop client
}

[<Fact>]
let ``GET /:code returns 404 for unknown code`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/unknown"
    r.Status |> should equal 404
    do! TestClient.stop client
}
