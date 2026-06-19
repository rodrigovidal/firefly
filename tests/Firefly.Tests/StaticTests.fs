module Firefly.Tests.StaticTests

open System.IO
open System.Net
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Firefly

let setupTestDir () =
    let dir = Path.Combine(Path.GetTempPath(), "fire-static-test-" + System.Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    Directory.CreateDirectory(Path.Combine(dir, "css")) |> ignore
    File.WriteAllText(Path.Combine(dir, "index.html"), "<h1>Hello</h1>")
    File.WriteAllText(Path.Combine(dir, "css", "app.css"), "body { color: red; }")
    File.WriteAllText(Path.Combine(dir, "data.json"), """{"ok":true}""")
    dir

[<Fact>]
let ``Static.serve returns file content`` () = task {
    let dir = setupTestDir ()
    try
        let routes = Route.start |> Route.get "/static/*path" (Static.serve dir)
        let config = App.defaults |> App.port 0
        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient()
        let! response = client.GetAsync($"http://127.0.0.1:{port}/static/index.html")
        let! body = response.Content.ReadAsStringAsync()
        response.StatusCode |> should equal HttpStatusCode.OK
        body |> should haveSubstring "<h1>Hello</h1>"
        do! stop()
    finally
        Directory.Delete(dir, true)
}

[<Fact>]
let ``Static.serve sets correct content type`` () = task {
    let dir = setupTestDir ()
    try
        let routes = Route.start |> Route.get "/static/*path" (Static.serve dir)
        let config = App.defaults |> App.port 0
        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient()
        let! htmlResp = client.GetAsync($"http://127.0.0.1:{port}/static/index.html")
        htmlResp.Content.Headers.ContentType.MediaType |> should equal "text/html"
        let! cssResp = client.GetAsync($"http://127.0.0.1:{port}/static/css/app.css")
        cssResp.Content.Headers.ContentType.MediaType |> should equal "text/css"
        let! jsonResp = client.GetAsync($"http://127.0.0.1:{port}/static/data.json")
        jsonResp.Content.Headers.ContentType.MediaType |> should equal "application/json"
        do! stop()
    finally
        Directory.Delete(dir, true)
}

[<Fact>]
let ``Static.serve returns 404 for missing file`` () = task {
    let dir = setupTestDir ()
    try
        let routes = Route.start |> Route.get "/static/*path" (Static.serve dir)
        let config = App.defaults |> App.port 0
        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient()
        let! response = client.GetAsync($"http://127.0.0.1:{port}/static/nope.txt")
        response.StatusCode |> should equal HttpStatusCode.NotFound
        do! stop()
    finally
        Directory.Delete(dir, true)
}

[<Fact>]
let ``Static.serve prevents directory traversal`` () = task {
    let dir = setupTestDir ()
    try
        let routes = Route.start |> Route.get "/static/*path" (Static.serve dir)
        let config = App.defaults |> App.port 0
        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient()
        let! response = client.GetAsync($"http://127.0.0.1:{port}/static/../../../etc/passwd")
        response.StatusCode |> should equal HttpStatusCode.NotFound
        do! stop()
    finally
        Directory.Delete(dir, true)
}

// --- Coverage: getContentType unknown extension (line 28) ---

[<Fact>]
let ``Static.serve returns octet-stream for unknown extension`` () = task {
    let dir = setupTestDir ()
    try
        // Create a file with unknown extension
        File.WriteAllText(Path.Combine(dir, "data.xyz"), "binary content")
        let routes = Route.start |> Route.get "/static/*path" (Static.serve dir)
        let config = App.defaults |> App.port 0
        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient()
        let! response = client.GetAsync($"http://127.0.0.1:{port}/static/data.xyz")
        response.StatusCode |> should equal HttpStatusCode.OK
        response.Content.Headers.ContentType.MediaType |> should equal "application/octet-stream"
        do! stop()
    finally
        Directory.Delete(dir, true)
}

// --- Coverage: Static.fs line 39 — traversal via direct TestClient ---

[<Fact>]
let ``Static.serve prevents traversal via TestClient`` () = task {
    let dir = setupTestDir ()
    try
        let routes = Route.start |> Route.get "/static/*path" (Static.serve dir)
        let client = TestClient.create routes
        // Send a traversal path directly — TestClient doesn't normalize URLs
        let! response = client |> TestClient.get "/static/../../../etc/passwd"
        response.Status |> should equal 404
    finally
        Directory.Delete(dir, true)
}

[<Fact>]
let ``Static.serve handles nested directories`` () = task {
    let dir = setupTestDir ()
    try
        let routes = Route.start |> Route.get "/static/*path" (Static.serve dir)
        let config = App.defaults |> App.port 0
        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient()
        let! response = client.GetAsync($"http://127.0.0.1:{port}/static/css/app.css")
        let! body = response.Content.ReadAsStringAsync()
        body |> should equal "body { color: red; }"
        do! stop()
    finally
        Directory.Delete(dir, true)
}
