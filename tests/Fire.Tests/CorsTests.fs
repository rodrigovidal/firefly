module Fire.Tests.CorsTests

open System.Net
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Fire

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
