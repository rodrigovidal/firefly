module Fire.Tests.SecureHeadersTests

open Xunit
open FsUnit.Xunit
open Fire

let private findHeader name (headers: (string * string) list) =
    headers |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

[<Fact>]
let ``SecureHeaders middleware adds all default headers`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.middleware SecureHeaders.middleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    findHeader "X-Content-Type-Options" response.Headers |> should equal (Some "nosniff")
    findHeader "X-Frame-Options" response.Headers |> should equal (Some "DENY")
    findHeader "X-XSS-Protection" response.Headers |> should equal (Some "0")
    findHeader "Referrer-Policy" response.Headers |> should equal (Some "strict-origin-when-cross-origin")
    findHeader "Content-Security-Policy" response.Headers |> should equal (Some "default-src 'self'")
    findHeader "Strict-Transport-Security" response.Headers |> should equal (Some "max-age=31536000; includeSubDomains")
    findHeader "Permissions-Policy" response.Headers |> should equal (Some "camera=(), microphone=(), geolocation=()")
}

[<Fact>]
let ``SecureHeaders configurable builder allows customizing headers`` () = task {
    let customMiddleware =
        SecureHeaders.defaults
        |> SecureHeaders.frameOptions "SAMEORIGIN"
        |> SecureHeaders.referrerPolicy "no-referrer"
        |> SecureHeaders.build
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.middleware customMiddleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    findHeader "X-Frame-Options" response.Headers |> should equal (Some "SAMEORIGIN")
    findHeader "Referrer-Policy" response.Headers |> should equal (Some "no-referrer")
    findHeader "X-Content-Type-Options" response.Headers |> should equal (Some "nosniff")
}

[<Fact>]
let ``SecureHeaders noHsts removes HSTS header`` () = task {
    let customMiddleware =
        SecureHeaders.defaults
        |> SecureHeaders.noHsts
        |> SecureHeaders.build
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.middleware customMiddleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    findHeader "Strict-Transport-Security" response.Headers |> should equal None
    // Other headers should still be present
    findHeader "X-Content-Type-Options" response.Headers |> should equal (Some "nosniff")
}

[<Fact>]
let ``SecureHeaders custom CSP`` () = task {
    let customMiddleware =
        SecureHeaders.defaults
        |> SecureHeaders.contentSecurityPolicy "default-src 'self'; script-src 'unsafe-inline'"
        |> SecureHeaders.build
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.middleware customMiddleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    findHeader "Content-Security-Policy" response.Headers |> should equal (Some "default-src 'self'; script-src 'unsafe-inline'")
}
