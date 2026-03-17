module Fire.Tests.TimeoutTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Timeout.after returns 504 when handler exceeds timeout`` () = task {
    let routes =
        Route.start
        |> Route.middleware(Timeout.after (TimeSpan.FromMilliseconds 100.0))
        |> Route.get("/slow", fun _ -> task {
            do! Task.Delay(5000)
            return Response.text "done"
        })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let! response = client.GetAsync($"http://127.0.0.1:{port}/slow")
    response.StatusCode |> should equal HttpStatusCode.GatewayTimeout
    do! stop()
}

[<Fact>]
let ``Timeout.after passes through when handler completes in time`` () = task {
    let routes =
        Route.start
        |> Route.middleware(Timeout.after (TimeSpan.FromSeconds 5.0))
        |> Route.get("/fast", fun _ -> task { return Response.text "quick" })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let! response = client.GetAsync($"http://127.0.0.1:{port}/fast")
    let! body = response.Content.ReadAsStringAsync()
    response.StatusCode |> should equal HttpStatusCode.OK
    body |> should equal "quick"
    do! stop()
}
