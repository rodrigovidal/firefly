module Firefly.Tests.HealthTests

open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``Health.handler returns 200 when all checks pass`` () = task {
    let routes =
        Route.start
        |> Route.get "/health" (Health.handler [
            Health.ping
            Health.check "custom" (fun () -> task { () })
        ])
    let client = TestClient.create routes
    let! response = client |> TestClient.get "/health"
    response.Status |> should equal 200
    response.Body |> should haveSubstring "healthy"
}

[<Fact>]
let ``Health.handler returns 503 when a check fails`` () = task {
    let routes =
        Route.start
        |> Route.get "/health" (Health.handler [
            Health.ping
            ("db", fun () -> task { return Error "connection refused" })
        ])
    let client = TestClient.create routes
    let! response = client |> TestClient.get "/health"
    response.Status |> should equal 503
    response.Body |> should haveSubstring "unhealthy"
}

[<Fact>]
let ``Health.ping always returns healthy`` () = task {
    let (name, checkFn) = Health.ping
    name |> should equal "ping"
    let! result = checkFn ()
    match result with
    | Ok () -> ()
    | Error e -> failwith $"Expected Ok but got Error: {e}"
}

[<Fact>]
let ``Health.handler returns 503 when a check throws`` () = task {
    let routes =
        Route.start
        |> Route.get "/health" (Health.handler [
            Health.check "failing" (fun () -> task { return failwith "boom" })
        ])
    let client = TestClient.create routes
    let! response = client |> TestClient.get "/health"
    response.Status |> should equal 503
    response.Body |> should haveSubstring "unhealthy"
    response.Body |> should haveSubstring "boom"
}

[<Fact>]
let ``Health.handler returns check names in response`` () = task {
    let routes =
        Route.start
        |> Route.get "/health" (Health.handler [
            Health.ping
            Health.check "redis" (fun () -> task { () })
        ])
    let client = TestClient.create routes
    let! response = client |> TestClient.get "/health"
    response.Body |> should haveSubstring "ping"
    response.Body |> should haveSubstring "redis"
}

[<Fact>]
let ``Health.handler catches raw check exceptions`` () = task {
    let routes =
        Route.start
        |> Route.get "/health" (Health.handler [
            ("explodes", fun () -> task { return failwith "raw boom" })
        ])
    let client = TestClient.create routes
    let! response = client |> TestClient.get "/health"
    response.Status |> should equal 503
    response.Body |> should haveSubstring "raw boom"
}
