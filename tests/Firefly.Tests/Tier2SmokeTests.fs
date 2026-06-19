module Firefly.Tests.Tier2SmokeTests

open System.IO
open System.Net
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``Tier 2 integration smoke test`` () = task {
    let dir = Path.Combine(Path.GetTempPath(), "fire-tier2-" + System.Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    File.WriteAllText(Path.Combine(dir, "hello.txt"), "world")

    try
        let mutable logEntries = []
        let logMw = Log.withOutput (fun e -> logEntries <- e :: logEntries)

        let routes =
            Route.start
            |> Route.get "/api/data" (fun (req: Request) -> task {
                if req.Accepts "application/json" then
                    return
                        Response.json {| items = [1;2;3] |}
                        |> Response.etag "\"v1\""
                        |> Response.cacheControl "public, max-age=60"
                else
                    return Response.text "items: 1, 2, 3"
            })
            |> Route.get "/go" (fun _ -> task {
                return Response.ok |> Response.redirect "/api/data" 302
            })
            |> Route.get "/static/*path" (Static.serve dir)

        let config =
            App.defaults
            |> App.port 0
            |> App.middleware logMw

        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient(new HttpClientHandler(AllowAutoRedirect = false))
        let base' = $"http://127.0.0.1:{port}"

        // Content negotiation + caching
        let req1 = new HttpRequestMessage(HttpMethod.Get, $"{base'}/api/data")
        req1.Headers.Add("Accept", "application/json")
        let! r1 = client.SendAsync(req1)
        r1.StatusCode |> should equal HttpStatusCode.OK
        r1.Headers.GetValues("ETag") |> Seq.head |> should equal "\"v1\""
        r1.Headers.GetValues("Cache-Control") |> Seq.head |> should equal "public, max-age=60"

        // Redirect
        let! r2 = client.GetAsync($"{base'}/go")
        r2.StatusCode |> should equal HttpStatusCode.Redirect
        r2.Headers.GetValues("Location") |> Seq.head |> should equal "/api/data"

        // Static files
        let! r3 = client.GetAsync($"{base'}/static/hello.txt")
        let! b3 = r3.Content.ReadAsStringAsync()
        b3 |> should equal "world"

        // Logging captured all requests
        logEntries |> List.length |> should be (greaterThanOrEqualTo 3)

        do! stop()
    finally
        Directory.Delete(dir, true)
}
