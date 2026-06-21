module Firefly.Tests.DashboardTests

open Xunit
open FsUnit.Xunit
open Firefly

// --- Collector unit tests ---

[<Fact>]
let ``Collector records totals, average, and percentiles`` () =
    let c = DashboardCollector()
    for i in 1..100 do
        c.Record(200, float i)
    let s = c.Snapshot()
    s.TotalRequests |> should equal 100L
    s.ErrorCount |> should equal 0L
    s.AvgMs |> should (equalWithin 0.001) 50.5
    s.P50Ms |> should be (greaterThanOrEqualTo 45.0)
    s.P50Ms |> should be (lessThanOrEqualTo 56.0)
    s.P99Ms |> should be (greaterThanOrEqualTo 95.0)

[<Fact>]
let ``Collector counts 5xx as errors`` () =
    let c = DashboardCollector()
    c.Record(200, 1.0)
    c.Record(503, 1.0)
    c.Record(500, 1.0)
    let s = c.Snapshot()
    s.TotalRequests |> should equal 3L
    s.ErrorCount |> should equal 2L
    s.ErrorRate |> should (equalWithin 0.001) (2.0 / 3.0)

[<Fact>]
let ``Collector tracks in-flight gauge`` () =
    let c = DashboardCollector()
    c.IncInFlight()
    c.IncInFlight()
    c.Snapshot().InFlight |> should equal 2
    c.DecInFlight()
    c.Snapshot().InFlight |> should equal 1

// --- Middleware tests ---

let private routes =
    Route.start
    |> Route.get "/ping" (fun _ -> task { return Response.text "ok" })

[<Fact>]
let ``Dashboard middleware counts app requests but not its own page`` () = task {
    let collector = DashboardCollector()
    let config = App.defaults |> App.middleware (Dashboard.middlewareWith collector "/dashboard")
    let client = TestClient.createWith routes config

    let! _ = client |> TestClient.get "/ping"
    let! _ = client |> TestClient.get "/ping"
    let! _ = client |> TestClient.get "/ping"

    // The dashboard page itself is served, not counted.
    let! page = client |> TestClient.get "/dashboard"
    page.Status |> should equal 200
    page.Body |> should haveSubstring "Firefly"

    collector.Snapshot().TotalRequests |> should equal 3L
}

[<Fact>]
let ``Dashboard middleware passes non-dashboard requests through`` () = task {
    let config = App.defaults |> App.dashboard "/dashboard"
    let client = TestClient.createWith routes config
    let! r = client |> TestClient.get "/ping"
    r.Status |> should equal 200
    r.Body |> should equal "ok"
}
