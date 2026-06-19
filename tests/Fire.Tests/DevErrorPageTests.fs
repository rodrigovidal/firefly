module Fire.Tests.DevErrorPageTests

open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``DevErrorPage returns HTML response with request context`` () = task {
    let routes =
        Route.start
        |> Route.get "/users/:id" (fun _ -> task {
            return failwith "boom"
        })

    let config =
        App.defaults
        |> App.onError DevErrorPage.handler

    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "X-Correlation-Id" "corr-123"
        |> TestClient.withHeader "X-Request-Id" "req-456"

    let! response = client |> TestClient.get "/users/42"

    response.Status |> should equal 500
    response.Headers |> should contain ("Content-Type", "text/html; charset=utf-8")
    response.Body |> should haveSubstring "boom"
    response.Body |> should haveSubstring "/users/42"
    response.Body |> should haveSubstring "corr-123"
    response.Body |> should haveSubstring "req-456"
    response.Body |> should haveSubstring "users"
    response.Body |> should haveSubstring "42"
}
