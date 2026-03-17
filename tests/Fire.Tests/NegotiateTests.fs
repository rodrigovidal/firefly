module Fire.Tests.NegotiateTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Negotiate returns 406 for unsupported Accept type`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello" })
    let config = App.defaults |> App.middleware (Negotiate.middleware ["application/json"; "text/plain"])
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept" "text/xml"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 406
}

[<Fact>]
let ``Negotiate allows supported Accept type`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello" })
    let config = App.defaults |> App.middleware (Negotiate.middleware ["application/json"; "text/plain"])
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept" "text/plain"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
}

[<Fact>]
let ``Negotiate allows wildcard Accept`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello" })
    let config = App.defaults |> App.middleware (Negotiate.middleware ["application/json"])
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept" "*/*"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
}

[<Fact>]
let ``Negotiate allows request with no Accept header`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello" })
    let config = App.defaults |> App.middleware (Negotiate.middleware ["application/json"])
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
}

[<Fact>]
let ``Negotiate rejects disabled supported media type`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello" })
    let config = App.defaults |> App.middleware (Negotiate.middleware ["application/json"; "text/plain"])
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept" "text/plain;q=0, application/json;q=0"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 406
}

[<Fact>]
let ``Negotiate allows subtype wildcard`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello" })
    let config = App.defaults |> App.middleware (Negotiate.middleware ["text/plain"])
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept" "text/*"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
}

[<Fact>]
let ``Negotiate rejects invalid quality values`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello" })
    let config = App.defaults |> App.middleware (Negotiate.middleware ["text/plain"])
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept" "text/plain;q=oops"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 406
}
