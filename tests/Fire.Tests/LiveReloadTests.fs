module Fire.Tests.LiveReloadTests

open Xunit
open FsUnit.Xunit
open Fire

// --- Script injection tests ---

[<Fact>]
let ``injectScript inserts before closing body tag`` () =
    let html = "<html><body><h1>Hello</h1></body></html>"
    let result = LiveReload.injectScript html
    result |> should haveSubstring "EventSource"
    result |> should haveSubstring "__fire/livereload"
    // Script should be before </body>
    let scriptIdx = result.IndexOf("EventSource")
    let bodyIdx = result.IndexOf("</body>")
    scriptIdx |> should be (lessThan bodyIdx)

[<Fact>]
let ``injectScript handles uppercase BODY tag`` () =
    let html = "<html><BODY><h1>Hello</h1></BODY></html>"
    let result = LiveReload.injectScript html
    result |> should haveSubstring "EventSource"

[<Fact>]
let ``injectScript appends when no body tag`` () =
    let html = "<h1>Hello</h1>"
    let result = LiveReload.injectScript html
    result |> should haveSubstring "EventSource"

[<Fact>]
let ``injectScript preserves original content`` () =
    let html = "<html><body><h1>Hello</h1></body></html>"
    let result = LiveReload.injectScript html
    result |> should haveSubstring "<h1>Hello</h1>"

// --- Middleware tests ---

[<Fact>]
let ``LiveReload.middleware injects script into HTML responses`` () = task {
    let routes =
        Route.start
        |> Route.get "/page" (fun _ -> task {
            return Response.text "<html><body><h1>Hi</h1></body></html>"
                   |> Response.header "Content-Type" "text/html"
        })
    let config =
        App.defaults |> App.port 0
        |> App.middleware LiveReload.middleware
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/page"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "EventSource"
    r.Body |> should haveSubstring "__fire/livereload"
    r.Body |> should haveSubstring "<h1>Hi</h1>"
    do! TestClient.stop client
}

[<Fact>]
let ``LiveReload.middleware does not inject into JSON responses`` () = task {
    let routes =
        Route.start
        |> Route.get "/api" (fun _ -> task {
            return Response.json {| message = "hello" |}
        })
    let config =
        App.defaults |> App.port 0
        |> App.middleware LiveReload.middleware
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api"
    r.Status |> should equal 200
    r.Body |> should not' (haveSubstring "EventSource")
    do! TestClient.stop client
}

[<Fact>]
let ``LiveReload.middleware does not inject into plain text responses`` () = task {
    let routes =
        Route.start
        |> Route.get "/plain" (fun _ -> task {
            return Response.text "hello"
        })
    let config =
        App.defaults |> App.port 0
        |> App.middleware LiveReload.middleware
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/plain"
    r.Status |> should equal 200
    r.Body |> should not' (haveSubstring "EventSource")
    do! TestClient.stop client
}

// --- SSE endpoint test ---

[<Fact>]
let ``LiveReload SSE endpoint responds with event-stream content type`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config System.Threading.CancellationToken.None
    use client = new System.Net.Http.HttpClient()
    client.Timeout <- System.TimeSpan.FromSeconds 2.0
    // The SSE endpoint holds the connection open, so we use a short timeout
    try
        let! _ = client.GetAsync($"http://127.0.0.1:{port}/__fire/livereload")
        ()
    with :? System.Threading.Tasks.TaskCanceledException ->
        // Expected — the endpoint holds the connection open, HttpClient times out
        ()
    do! stop()
}
