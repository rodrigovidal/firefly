module Fire.Tests.SignedCookieTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``sign produces value.signature format`` () =
    let signed = SignedCookie.sign "my-secret" "hello"
    signed |> should haveSubstring "hello."
    let parts = signed.Split('.')
    parts.Length |> should equal 2
    parts.[0] |> should equal "hello"
    parts.[1].Length |> should be (greaterThan 0)

[<Fact>]
let ``verify with correct secret returns Some value`` () =
    let signed = SignedCookie.sign "my-secret" "hello"
    let result = SignedCookie.verify "my-secret" signed
    result |> should equal (Some "hello")

[<Fact>]
let ``verify with wrong secret returns None`` () =
    let signed = SignedCookie.sign "my-secret" "hello"
    let result = SignedCookie.verify "wrong-secret" signed
    result |> should equal None

[<Fact>]
let ``verify with tampered value returns None`` () =
    let signed = SignedCookie.sign "my-secret" "hello"
    let tampered = "tampered" + signed.Substring(5)
    let result = SignedCookie.verify "my-secret" tampered
    result |> should equal None

[<Fact>]
let ``set and get roundtrip through Response and Request`` () = task {
    let secret = "super-secret-key"
    let routes =
        Route.start
        |> Route.get "/set" (fun _ -> task {
            return Response.ok
                |> SignedCookie.set secret "token" "myvalue" Cookie.defaults
        })
        |> Route.get "/get" (fun (req: Request) -> task {
            let value = SignedCookie.get secret "token" req |> Option.defaultValue "missing"
            return Response.text value
        })
    let client = TestClient.create routes

    // Set the signed cookie
    let! setResponse = client |> TestClient.get "/set"
    setResponse.Status |> should equal 200

    // Extract cookie value from Set-Cookie header
    let setCookie =
        setResponse.Headers
        |> List.find (fun (k, _) -> k = "Set-Cookie")
        |> snd
    // Format is "token=<signed-value>"
    let cookieValue = setCookie.Substring(setCookie.IndexOf('=') + 1)

    // Send the cookie back
    let clientWithCookie =
        TestClient.create routes
        |> TestClient.withHeader "Cookie" $"token={cookieValue}"
    let! getResponse = clientWithCookie |> TestClient.get "/get"
    getResponse.Status |> should equal 200
    getResponse.Body |> should equal "myvalue"
}
