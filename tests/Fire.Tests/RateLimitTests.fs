module Fire.Tests.RateLimitTests

open System
open System.Net
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``RateLimit allows requests within limit`` () = task {
    let routes =
        Route.start
        |> Route.middleware(RateLimit.fixedWindow 5 (TimeSpan.FromMinutes 1.0) (fun _ -> "rtest-1"))
        |> Route.get("/api", fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    for _ in 1..5 do
        let! response = client.GetAsync($"http://127.0.0.1:{port}/api")
        response.StatusCode |> should equal HttpStatusCode.OK
    do! stop()
}

[<Fact>]
let ``RateLimit returns 429 when limit exceeded`` () = task {
    let routes =
        Route.start
        |> Route.middleware(RateLimit.fixedWindow 3 (TimeSpan.FromMinutes 1.0) (fun _ -> "rtest-2"))
        |> Route.get("/api", fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    for _ in 1..3 do
        let! response = client.GetAsync($"http://127.0.0.1:{port}/api")
        response.StatusCode |> should equal HttpStatusCode.OK
    let! response = client.GetAsync($"http://127.0.0.1:{port}/api")
    response.StatusCode |> should equal HttpStatusCode.TooManyRequests
    do! stop()
}

[<Fact>]
let ``RateLimit returns Retry-After header on 429`` () = task {
    let routes =
        Route.start
        |> Route.middleware(RateLimit.fixedWindow 1 (TimeSpan.FromSeconds 60.0) (fun _ -> "rtest-3"))
        |> Route.get("/api", fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let! _ = client.GetAsync($"http://127.0.0.1:{port}/api")
    let! response = client.GetAsync($"http://127.0.0.1:{port}/api")
    response.StatusCode |> should equal HttpStatusCode.TooManyRequests
    response.Headers.Contains("Retry-After") |> should be True
    do! stop()
}

[<Fact>]
let ``RateLimit isolates keys`` () = task {
    let routes =
        Route.start
        |> Route.middleware(RateLimit.fixedWindow 1 (TimeSpan.FromMinutes 1.0)
            (fun req -> req.Header "X-Key" |> Option.defaultValue "default"))
        |> Route.get("/api", fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    // First key exhausts limit
    let req1 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api")
    req1.Headers.Add("X-Key", "user-a")
    let! r1 = client.SendAsync(req1)
    r1.StatusCode |> should equal HttpStatusCode.OK
    let req2 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api")
    req2.Headers.Add("X-Key", "user-a")
    let! r2 = client.SendAsync(req2)
    r2.StatusCode |> should equal HttpStatusCode.TooManyRequests
    // Second key still has quota
    let req3 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api")
    req3.Headers.Add("X-Key", "user-b")
    let! r3 = client.SendAsync(req3)
    r3.StatusCode |> should equal HttpStatusCode.OK
    do! stop()
}
