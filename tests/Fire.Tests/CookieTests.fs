module Fire.Tests.CookieTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Request.Cookie reads cookie from request`` () = task {
    let routes =
        Route.start
        |> Route.get("/me", fun (req: Request) -> task {
            let session = req.Cookie "session" |> Option.defaultValue "none"
            return Response.text session
        })
    let client =
        TestClient.create routes
        |> TestClient.withHeader "Cookie" "session=abc123"
    let! r = client |> TestClient.get "/me"
    r.Status |> should equal 200
    r.Body |> should equal "abc123"
}

[<Fact>]
let ``Request.Cookie returns None when cookie missing`` () = task {
    let routes =
        Route.start
        |> Route.get("/me", fun (req: Request) -> task {
            let session = req.Cookie "session"
            match session with
            | Some _ -> return Response.text "found"
            | None -> return Response.text "missing"
        })
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/me"
    r.Status |> should equal 200
    r.Body |> should equal "missing"
}

[<Fact>]
let ``Response.cookie sets bare Set-Cookie header`` () =
    let r = Response.ok |> Response.cookie "session" "abc123"
    r.Headers |> should contain ("Set-Cookie", "session=abc123")

[<Fact>]
let ``Cookie.set sets full Set-Cookie header with options`` () =
    let r =
        Response.ok
        |> Cookie.set "token" "xyz" (
            Cookie.defaults
            |> Cookie.httpOnly
            |> Cookie.secure
            |> Cookie.maxAge 3600
            |> Cookie.path "/"
            |> Cookie.sameSite "Strict"
        )
    let cookieHeader = r.Headers |> List.find (fun (k, _) -> k = "Set-Cookie") |> snd
    cookieHeader |> should haveSubstring "token=xyz"
    cookieHeader |> should haveSubstring "Max-Age=3600"
    cookieHeader |> should haveSubstring "Path=/"
    cookieHeader |> should haveSubstring "HttpOnly"
    cookieHeader |> should haveSubstring "Secure"
    cookieHeader |> should haveSubstring "SameSite=Strict"

[<Fact>]
let ``Cookie.set with defaults sets bare cookie`` () =
    let r = Response.ok |> Cookie.set "name" "val" Cookie.defaults
    let cookieHeader = r.Headers |> List.find (fun (k, _) -> k = "Set-Cookie") |> snd
    cookieHeader |> should equal "name=val"

[<Fact>]
let ``Multiple cookies produce multiple Set-Cookie headers`` () =
    let r =
        Response.ok
        |> Response.cookie "a" "1"
        |> Response.cookie "b" "2"
    let cookies = r.Headers |> List.filter (fun (k, _) -> k = "Set-Cookie")
    cookies |> List.length |> should equal 2

[<Fact>]
let ``Cookie.domain sets Domain attribute`` () =
    let r =
        Response.ok
        |> Cookie.set "x" "y" (Cookie.defaults |> Cookie.domain "example.com")
    let cookieHeader = r.Headers |> List.find (fun (k, _) -> k = "Set-Cookie") |> snd
    cookieHeader |> should haveSubstring "Domain=example.com"
