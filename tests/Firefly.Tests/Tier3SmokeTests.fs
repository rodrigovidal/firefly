module Firefly.Tests.Tier3SmokeTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``Tier 3 integration smoke test`` () = task {
    let routes =
        Route.start
        |> Route.get "/fast" (fun _ -> task { return Response.text "ok" })
        |> Route.get "/slow" (fun _ -> task {
            do! Task.Delay(5000)
            return Response.text "done"
        })
        |> Route.get "/users/:id" (fun (req: Request) -> task {
            return Response.json {| id = req.Params.["id"] |}
        })

    let config =
        App.defaults
        |> App.port 0
        |> App.shutdownTimeout (TimeSpan.FromSeconds 5.0)
        |> App.middleware (Timeout.after (TimeSpan.FromMilliseconds 200.0))
        |> App.middleware (RateLimit.fixedWindow 10 (TimeSpan.FromMinutes 1.0) (fun _ -> "tier3-smoke"))

    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let base' = $"http://127.0.0.1:{port}"

    // Fast request succeeds
    let! r1 = client.GetAsync($"{base'}/fast")
    r1.StatusCode |> should equal HttpStatusCode.OK

    // Slow request times out with 504
    let! r2 = client.GetAsync($"{base'}/slow")
    r2.StatusCode |> should equal HttpStatusCode.GatewayTimeout

    // OpenAPI spec
    let spec = OpenApi.generate "Smoke API" "1.0" routes
    let doc = JsonDocument.Parse(spec)
    doc.RootElement.GetProperty("paths").EnumerateObject() |> Seq.length |> should be (greaterThanOrEqualTo 3)

    do! stop()
}
