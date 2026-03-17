module Fire.IntegrationTests.UrlShortenerTests

open System
open System.Collections.Concurrent
open System.Threading
open Xunit
open FsUnit.Xunit
open Fire

type ShortUrl = { Code: string; Url: string; Clicks: int; CreatedAt: DateTime }
type CreateUrl = { Url: string }

let buildShortenerApp () =
    let store = ConcurrentDictionary<string, ShortUrl>()
    let mutable counter = 0

    let generateCode () =
        counter <- counter + 1
        $"test{counter:D3}"  // deterministic codes for testing

    let routes =
        Route.start
        |> Route.get "/" (fun _ -> task {
            return
                Response.text "<html><body>URL Shortener</body></html>"
                |> Response.header "Content-Type" "text/html; charset=utf-8"
        })
        |> Route.group "/api" (fun api ->
            api
            |> Route.post "/shorten" (fun req -> task {
                let! body = req.Json<CreateUrl>()
                if String.IsNullOrWhiteSpace(body.Url) then
                    return Response.json {| error = "url is required" |} |> Response.status 400
                elif not (body.Url.StartsWith("http://") || body.Url.StartsWith("https://")) then
                    return Response.json {| error = "url must start with http:// or https://" |} |> Response.status 400
                else
                    let code = generateCode ()
                    let entry = { Code = code; Url = body.Url; Clicks = 0; CreatedAt = DateTime.UtcNow }
                    store.[code] <- entry
                    return Response.json {| code = code; shortUrl = $"/{code}"; originalUrl = body.Url |} |> Response.status 201
            })
            |> Route.get "/stats" (fun _ -> task {
                return Response.json {| count = store.Count; urls = store.Values |> Seq.toList |}
            })
            |> Route.get "/stats/:code" (fun req -> task {
                let code = req.Params.["code"]
                match store.TryGetValue(code) with
                | true, entry -> return Response.json entry
                | false, _ -> return Response.json {| error = "not found" |} |> Response.status 404
            })
        )
        |> Route.get "/:code" (fun req -> task {
            let code = req.Params.["code"]
            match store.TryGetValue(code) with
            | true, entry ->
                store.[code] <- { entry with Clicks = entry.Clicks + 1 }
                return Response.ok |> Response.redirect entry.Url 302
            | false, _ ->
                return Response.json {| error = "not found" |} |> Response.status 404
        })

    let config =
        App.defaults
        |> App.port 0
        |> App.notFound (fun _ -> task {
            return Response.text "not found" |> Response.status 404
        })

    (routes, config)

// --- Tests ---

[<Fact>]
let ``Shortener: landing page returns HTML`` () = task {
    let (routes, config) = buildShortenerApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "URL Shortener"
    do! TestClient.stop client
}

[<Fact>]
let ``Shortener: create short URL`` () = task {
    let (routes, config) = buildShortenerApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/api/shorten" """{"Url":"https://example.com"}"""
    r.Status |> should equal 201
    r.Body |> should haveSubstring "example.com"
    r.Body |> should haveSubstring "code"
    do! TestClient.stop client
}

[<Fact>]
let ``Shortener: rejects empty URL`` () = task {
    let (routes, config) = buildShortenerApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/api/shorten" """{"Url":""}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "url is required"
    do! TestClient.stop client
}

[<Fact>]
let ``Shortener: rejects non-HTTP URL`` () = task {
    let (routes, config) = buildShortenerApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/api/shorten" """{"Url":"ftp://bad.com"}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "http"
    do! TestClient.stop client
}

[<Fact>]
let ``Shortener: redirect increments clicks`` () = task {
    let (routes, config) = buildShortenerApp ()
    let! client = TestClient.start routes config

    // Create
    let! r1 = client |> TestClient.post "/api/shorten" """{"Url":"https://example.com"}"""
    r1.Status |> should equal 201

    // Verify initial clicks are 0
    let! stats1 = client |> TestClient.get "/api/stats/test001"
    stats1.Body |> should haveSubstring "\"Clicks\":0"

    // Visit the short URL - HttpClient follows the redirect to example.com
    // The handler increments clicks before redirecting
    let! r2 = client |> TestClient.get "/test001"
    // After following the redirect, we get the final response from example.com (200)
    // The important thing is that the click was counted
    r2.Status |> should equal 200

    // Check clicks incremented
    let! stats2 = client |> TestClient.get "/api/stats/test001"
    stats2.Body |> should haveSubstring "\"Clicks\":1"

    // Visit again
    let! _ = client |> TestClient.get "/test001"

    // Check clicks incremented again
    let! stats3 = client |> TestClient.get "/api/stats/test001"
    stats3.Body |> should haveSubstring "\"Clicks\":2"

    do! TestClient.stop client
}

[<Fact>]
let ``Shortener: stats lists all URLs`` () = task {
    let (routes, config) = buildShortenerApp ()
    let! client = TestClient.start routes config

    let! _ = client |> TestClient.post "/api/shorten" """{"Url":"https://a.com"}"""
    let! _ = client |> TestClient.post "/api/shorten" """{"Url":"https://b.com"}"""

    let! r = client |> TestClient.get "/api/stats"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "\"count\":2"
    do! TestClient.stop client
}

[<Fact>]
let ``Shortener: unknown code returns 404`` () = task {
    let (routes, config) = buildShortenerApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/nonexistent"
    r.Status |> should equal 404
    do! TestClient.stop client
}

[<Fact>]
let ``Shortener: stats for unknown code returns 404`` () = task {
    let (routes, config) = buildShortenerApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/stats/nope"
    r.Status |> should equal 404
    do! TestClient.stop client
}
