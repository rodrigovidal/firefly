module Fire.Tests.LogTests

open System
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Log.withOutput calls output function with correct entry`` () = task {
    let mutable captured = None
    let logMw = Log.withOutput (fun entry -> captured <- Some entry)
    let routes =
        Route.start
        |> Route.get("/test", fun _ -> task { return Response.json {| ok = true |} })
    let config = App.defaults |> App.port 0 |> App.middleware logMw
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let! _ = client.GetAsync($"http://127.0.0.1:{port}/test")
    captured |> Option.isSome |> should be True
    let entry = captured.Value
    entry.Method |> should equal "GET"
    entry.Path |> should equal "/test"
    entry.Status |> should equal 200
    entry.Duration.TotalMilliseconds |> should be (greaterThan 0.0)
    do! stop()
}

[<Fact>]
let ``Log.withOutput captures 404 status`` () = task {
    let mutable captured = None
    let logMw = Log.withOutput (fun entry -> captured <- Some entry)
    let routes = Route.start
    let config = App.defaults |> App.port 0 |> App.middleware logMw
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let! _ = client.GetAsync($"http://127.0.0.1:{port}/missing")
    captured |> Option.isSome |> should be True
    captured.Value.Status |> should equal 404
    do! stop()
}

[<Fact>]
let ``Log.toConsole does not throw`` () = task {
    let routes =
        Route.start
        |> Route.get("/ok", fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0 |> App.middleware Log.toConsole
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let! response = client.GetAsync($"http://127.0.0.1:{port}/ok")
    response.StatusCode |> should equal System.Net.HttpStatusCode.OK
    do! stop()
}
