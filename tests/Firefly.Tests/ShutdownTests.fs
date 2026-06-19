module Firefly.Tests.ShutdownTests

open System
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``App.shutdownTimeout sets config`` () =
    let config =
        App.defaults
        |> App.shutdownTimeout (TimeSpan.FromSeconds 10.0)
    config.ShutdownTimeout |> should equal (Some (TimeSpan.FromSeconds 10.0))

[<Fact>]
let ``Server stops gracefully after stop is called`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.port 0 |> App.shutdownTimeout (TimeSpan.FromSeconds 5.0)
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let! response = client.GetAsync($"http://127.0.0.1:{port}/test")
    let! body = response.Content.ReadAsStringAsync()
    body |> should equal "ok"
    do! stop()
}
