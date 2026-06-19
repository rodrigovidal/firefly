module Firefly.Tests.RequestIdTests

open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``RequestId adds X-Request-Id to response`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.middleware RequestId.middleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    response.Headers |> List.exists (fun (k, _) -> k = "X-Request-Id") |> should equal true
    let requestId = response.Headers |> List.find (fun (k, _) -> k = "X-Request-Id") |> snd
    requestId.Length |> should be (greaterThan 0)
}

[<Fact>]
let ``RequestId forwards existing X-Request-Id`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.middleware RequestId.middleware
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "X-Request-Id" "my-custom-id-123"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    let requestId = response.Headers |> List.find (fun (k, _) -> k = "X-Request-Id") |> snd
    requestId |> should equal "my-custom-id-123"
}

[<Fact>]
let ``RequestId generates unique IDs for different requests`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.middleware RequestId.middleware
    let client = TestClient.createWith routes config
    let! response1 = client |> TestClient.get "/test"
    let! response2 = client |> TestClient.get "/test"
    let id1 = response1.Headers |> List.find (fun (k, _) -> k = "X-Request-Id") |> snd
    let id2 = response2.Headers |> List.find (fun (k, _) -> k = "X-Request-Id") |> snd
    id1 |> should not' (equal id2)
}

[<Fact>]
let ``RequestId makes generated id available to handlers`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun (req: Request) -> task {
            return Response.text (req.RequestId |> Option.defaultValue "missing")
        })
    let config = App.defaults |> App.middleware RequestId.middleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    let requestIdHeader = response.Headers |> List.find (fun (k, _) -> k = "X-Request-Id") |> snd
    response.Body |> should equal requestIdHeader
}
