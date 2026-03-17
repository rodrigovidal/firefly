module BlogApi.Tests

open Xunit
open FsUnit.Xunit
open Fire
open BlogApi

[<Fact>]
let ``GET /api/posts returns seeded posts`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/posts"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "Getting Started with F#"
    r.Body |> should haveSubstring "Building APIs with Fire"
    r.Body |> should haveSubstring "Functional Patterns"
    do! TestClient.stop client
}

[<Fact>]
let ``GET /api/posts filters by tag`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/posts?tag=fire"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "Building APIs with Fire"
    r.Body |> should not' (haveSubstring "Functional Patterns")
    do! TestClient.stop client
}

[<Fact>]
let ``GET /api/posts content negotiation returns text/plain`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let textClient = client |> TestClient.withHeader "Accept" "text/plain"
    let! r = textClient |> TestClient.get "/api/posts"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "[1] Getting Started with F#"
    do! TestClient.stop client
}

[<Fact>]
let ``GET /api/posts/:id returns ETag and Cache-Control`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/posts/1"
    r.Status |> should equal 200
    r.Headers |> List.exists (fun (k, _) -> k = "ETag") |> should be True
    r.Headers |> List.exists (fun (k, v) -> k = "Cache-Control" && v.Contains("max-age")) |> should be True
    do! TestClient.stop client
}

[<Fact>]
let ``GET /api/posts/:id sets cookie`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/posts/1"
    r.Status |> should equal 200
    r.Headers |> List.exists (fun (k, v) -> k = "Set-Cookie" && v.Contains("last-visited")) |> should be True
    do! TestClient.stop client
}

[<Fact>]
let ``POST /api/posts creates a new post`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/api/posts" """{"Title":"New Post","Body":"Content here","Tags":["test"]}"""
    r.Status |> should equal 201
    r.Body |> should haveSubstring "New Post"
    r.Headers |> List.exists (fun (k, v) -> k = "Location" && v.Contains("/api/posts/")) |> should be True
    do! TestClient.stop client
}

[<Fact>]
let ``POST /api/posts validates title`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/api/posts" """{"Title":"","Body":"Content","Tags":[]}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "Title is required"
    do! TestClient.stop client
}

[<Fact>]
let ``POST /api/posts validates body`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/api/posts" """{"Title":"Valid Title","Body":"","Tags":[]}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "Body is required"
    do! TestClient.stop client
}

[<Fact>]
let ``GET /api/posts/:postId/comments returns comments`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/posts/1/comments"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "Great intro!"
    do! TestClient.stop client
}

[<Fact>]
let ``POST /api/posts/:postId/comments creates a comment`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/api/posts/1/comments" """{"Author":"Charlie","Body":"Nice post!"}"""
    r.Status |> should equal 201
    r.Body |> should haveSubstring "Charlie"
    r.Body |> should haveSubstring "Nice post!"
    do! TestClient.stop client
}

[<Fact>]
let ``POST /api/posts/:postId/comments validates author`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/api/posts/1/comments" """{"Author":"","Body":"text"}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "Author is required"
    do! TestClient.stop client
}

[<Fact>]
let ``GET /api/tags returns unique sorted tags`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/tags"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "fsharp"
    r.Body |> should haveSubstring "fire"
    do! TestClient.stop client
}

[<Fact>]
let ``GET /feed redirects to /api/posts`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/feed"
    r.Status |> should equal 302
    r.Headers |> List.exists (fun (k, v) -> k = "Location" && v = "/api/posts") |> should be True
    do! TestClient.stop client
}

[<Fact>]
let ``GET /api/posts/999 returns 404`` () = task {
    let (routes, config) = App.create ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/posts/999"
    r.Status |> should equal 404
    r.Body |> should haveSubstring "Post not found"
    do! TestClient.stop client
}
