module Fire.Tests.CorsTests

open System.Net
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``Cors.allowAll adds wildcard origin header`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0 |> App.middleware Cors.allowAll
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let! response = client.GetAsync($"http://127.0.0.1:{port}/test")
    response.Headers.GetValues("Access-Control-Allow-Origin") |> Seq.head |> should equal "*"
    do! stop()
}

[<Fact>]
let ``Cors.allowAll handles preflight OPTIONS`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0 |> App.middleware Cors.allowAll
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let request = new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{port}/test")
    request.Headers.Add("Origin", "http://example.com")
    request.Headers.Add("Access-Control-Request-Method", "POST")
    let! response = client.SendAsync(request)
    response.StatusCode |> should equal HttpStatusCode.NoContent
    response.Headers.GetValues("Access-Control-Allow-Origin") |> Seq.head |> should equal "*"
    response.Headers.GetValues("Access-Control-Allow-Methods") |> Seq.isEmpty |> should be False
    do! stop()
}

[<Fact>]
let ``Cors.build with specific origins echoes matching origin`` () = task {
    let cors = Cors.defaults |> Cors.origins ["http://example.com"; "http://other.com"] |> Cors.build
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0 |> App.middleware cors
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/test")
    request.Headers.Add("Origin", "http://example.com")
    let! response = client.SendAsync(request)
    response.Headers.GetValues("Access-Control-Allow-Origin") |> Seq.head |> should equal "http://example.com"
    do! stop()
}

[<Fact>]
let ``Cors.build rejects non-matching origin`` () = task {
    let cors = Cors.defaults |> Cors.origins ["http://allowed.com"] |> Cors.build
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0 |> App.middleware cors
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/test")
    request.Headers.Add("Origin", "http://evil.com")
    let! response = client.SendAsync(request)
    response.Headers.Contains("Access-Control-Allow-Origin") |> should be False
    do! stop()
}

// --- Coverage: Cors.build with custom methods (line 41) ---

[<Fact>]
let ``Cors.build with custom methods on preflight`` () = task {
    let cors = Cors.defaults |> Cors.methods ["GET"; "POST"] |> Cors.build
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0 |> App.middleware cors
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let request = new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{port}/test")
    request.Headers.Add("Origin", "http://example.com")
    request.Headers.Add("Access-Control-Request-Method", "POST")
    let! response = client.SendAsync(request)
    response.StatusCode |> should equal HttpStatusCode.NoContent
    let methodsHeader = response.Headers.GetValues("Access-Control-Allow-Methods") |> Seq.head
    methodsHeader |> should haveSubstring "GET"
    methodsHeader |> should haveSubstring "POST"
    do! stop()
}

// --- Coverage: Cors.build with custom headers (line 45) ---

[<Fact>]
let ``Cors.build with custom headers on preflight`` () = task {
    let cors = Cors.defaults |> Cors.headers ["X-Custom"; "Authorization"] |> Cors.build
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0 |> App.middleware cors
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let request = new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{port}/test")
    request.Headers.Add("Origin", "http://example.com")
    request.Headers.Add("Access-Control-Request-Headers", "X-Custom")
    let! response = client.SendAsync(request)
    response.StatusCode |> should equal HttpStatusCode.NoContent
    let headersVal = response.Headers.GetValues("Access-Control-Allow-Headers") |> Seq.head
    headersVal |> should haveSubstring "X-Custom"
    headersVal |> should haveSubstring "Authorization"
    do! stop()
}

// --- Coverage: Cors with specific origins and no Origin header (line 14-15) ---

[<Fact>]
let ``Cors.build with specific origins passes through when no Origin header`` () = task {
    let cors = Cors.defaults |> Cors.origins ["http://allowed.com"] |> Cors.build
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.port 0 |> App.middleware cors
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    // Request without Origin header
    let! response = client.GetAsync($"http://127.0.0.1:{port}/test")
    let! body = response.Content.ReadAsStringAsync()
    // Should still get the response (non-CORS request passes through)
    response.StatusCode |> should equal HttpStatusCode.OK
    body |> should equal "ok"
    // But no CORS header added
    response.Headers.Contains("Access-Control-Allow-Origin") |> should be False
    do! stop()
}

[<Fact>]
let ``Cors.build with maxAge sets Max-Age on preflight`` () = task {
    let cors = Cors.defaults |> Cors.maxAge 3600 |> Cors.build
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0 |> App.middleware cors
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let request = new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{port}/test")
    request.Headers.Add("Origin", "http://example.com")
    request.Headers.Add("Access-Control-Request-Method", "GET")
    let! response = client.SendAsync(request)
    response.Headers.GetValues("Access-Control-Max-Age") |> Seq.head |> should equal "3600"
    do! stop()
}
