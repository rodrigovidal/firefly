module Fire.Tests.CorrelationIdTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``CorrelationId adds X-Correlation-Id to response`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.middleware CorrelationId.middleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    response.Headers |> List.exists (fun (k, _) -> k = "X-Correlation-Id") |> should equal true
    let correlationId = response.Headers |> List.find (fun (k, _) -> k = "X-Correlation-Id") |> snd
    correlationId.Length |> should be (greaterThan 0)
}

[<Fact>]
let ``CorrelationId forwards existing X-Correlation-Id`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.middleware CorrelationId.middleware
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "X-Correlation-Id" "my-correlation-id-123"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    let correlationId = response.Headers |> List.find (fun (k, _) -> k = "X-Correlation-Id") |> snd
    correlationId |> should equal "my-correlation-id-123"
}

[<Fact>]
let ``CorrelationId generates unique IDs for different requests`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.middleware CorrelationId.middleware
    let client = TestClient.createWith routes config
    let! response1 = client |> TestClient.get "/test"
    let! response2 = client |> TestClient.get "/test"
    let id1 = response1.Headers |> List.find (fun (k, _) -> k = "X-Correlation-Id") |> snd
    let id2 = response2.Headers |> List.find (fun (k, _) -> k = "X-Correlation-Id") |> snd
    id1 |> should not' (equal id2)
}

[<Fact>]
let ``CorrelationId makes correlation id available to handlers`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun (req: Request) -> task {
            return Response.text (req.CorrelationId |> Option.defaultValue "missing")
        })
    let config = App.defaults |> App.middleware CorrelationId.middleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    let correlationIdHeader = response.Headers |> List.find (fun (k, _) -> k = "X-Correlation-Id") |> snd
    response.Body |> should equal correlationIdHeader
}
