module Fire.Tests.CsrfTests

open Xunit
open FsUnit.Xunit
open Fire

let private findHeader name (headers: (string * string) list) =
    headers |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

let private extractCookieValue (setCookie: string) =
    let eqIdx = setCookie.IndexOf('=')
    if eqIdx < 0 then setCookie
    else setCookie.Substring(eqIdx + 1)

[<Fact>]
let ``CSRF GET requests pass through and set cookie when token is generated`` () = task {
    let routes =
        Route.start
        |> Route.get "/form" (fun (req: Request) -> task {
            let _token = Csrf.token req
            return Response.text "form"
        })
    let config = App.defaults |> App.middleware Csrf.middleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/form"
    response.Status |> should equal 200
    response.Body |> should equal "form"
    // Should have a CSRF cookie set
    let setCookie = findHeader "Set-Cookie" response.Headers
    setCookie.IsSome |> should equal true
    setCookie.Value |> should haveSubstring "_fire_csrf="
}

[<Fact>]
let ``CSRF POST without token returns 403`` () = task {
    let routes =
        Route.start
        |> Route.post "/submit" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.middleware Csrf.middleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.post "/submit" "{}"
    response.Status |> should equal 403
}

[<Fact>]
let ``CSRF POST with matching X-CSRF-Token header succeeds`` () = task {
    let token = "test-csrf-token-123"
    let routes =
        Route.start
        |> Route.post "/submit" (fun _ -> task { return Response.text "submitted" })
    let config = App.defaults |> App.middleware Csrf.middleware
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "Cookie" $"_fire_csrf={token}"
        |> TestClient.withHeader "X-CSRF-Token" token
    let! response = client |> TestClient.post "/submit" "{}"
    response.Status |> should equal 200
    response.Body |> should equal "submitted"
}

[<Fact>]
let ``CSRF POST with mismatched token returns 403`` () = task {
    let routes =
        Route.start
        |> Route.post "/submit" (fun _ -> task { return Response.text "submitted" })
    let config = App.defaults |> App.middleware Csrf.middleware
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "Cookie" "_fire_csrf=token-a"
        |> TestClient.withHeader "X-CSRF-Token" "token-b"
    let! response = client |> TestClient.post "/submit" "{}"
    response.Status |> should equal 403
}

[<Fact>]
let ``CSRF POST with matching _csrf form field succeeds`` () = task {
    let token = "test-csrf-form-token"
    let routes =
        Route.start
        |> Route.post "/submit" (fun _ -> task { return Response.text "submitted" })
    let config = App.defaults |> App.middleware Csrf.middleware
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "Cookie" $"_fire_csrf={token}"
        |> TestClient.withHeader "Content-Type" "application/x-www-form-urlencoded"
    let! response = client |> TestClient.post "/submit" $"_csrf={token}"
    response.Status |> should equal 200
    response.Body |> should equal "submitted"
}

[<Fact>]
let ``CSRF POST with mismatched _csrf form field returns 403`` () = task {
    let routes =
        Route.start
        |> Route.post "/submit" (fun _ -> task { return Response.text "submitted" })
    let config = App.defaults |> App.middleware Csrf.middleware
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "Cookie" "_fire_csrf=real-token"
        |> TestClient.withHeader "Content-Type" "application/x-www-form-urlencoded"
    let! response = client |> TestClient.post "/submit" "_csrf=wrong-token"
    response.Status |> should equal 403
}
