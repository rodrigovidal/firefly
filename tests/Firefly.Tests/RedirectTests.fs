module Firefly.Tests.RedirectTests

open Xunit
open FsUnit.Xunit
open Firefly

let findHeader (name: string) (headers: (string * string) list) =
    headers |> List.find (fun (k, _) -> k = name) |> snd

[<Fact>]
let ``permanent returns 301 with Location header`` () = task {
    let routes =
        Route.start
        |> Redirect.permanent "/old-blog" "/blog"

    let client = TestClient.create routes
    let! r = client |> TestClient.get "/old-blog"
    r.Status |> should equal 301
    r.Headers |> findHeader "Location" |> should equal "/blog"
}

[<Fact>]
let ``permanent preserves query string in target`` () = task {
    let routes =
        Route.start
        |> Redirect.permanent "/old" "/new?ref=1"

    let client = TestClient.create routes
    let! r = client |> TestClient.get "/old"
    r.Status |> should equal 301
    r.Headers |> findHeader "Location" |> should equal "/new?ref=1"
}

[<Fact>]
let ``temporary returns 302 with Location header`` () = task {
    let routes =
        Route.start
        |> Redirect.temporary "/docs" "/documentation"

    let client = TestClient.create routes
    let! r = client |> TestClient.get "/docs"
    r.Status |> should equal 302
    r.Headers |> findHeader "Location" |> should equal "/documentation"
}

[<Fact>]
let ``temporary preserves query string in target`` () = task {
    let routes =
        Route.start
        |> Redirect.temporary "/old" "/new?page=2"

    let client = TestClient.create routes
    let! r = client |> TestClient.get "/old"
    r.Status |> should equal 302
    r.Headers |> findHeader "Location" |> should equal "/new?page=2"
}

[<Fact>]
let ``permanent composes with other routes`` () = task {
    let routes =
        Route.start
        |> Redirect.permanent "/old-blog" "/blog"
        |> Route.get "/blog" (fun _ -> task { return Response.text "blog" })

    let client = TestClient.create routes
    let! redirect = client |> TestClient.get "/old-blog"
    redirect.Status |> should equal 301

    let! blog = client |> TestClient.get "/blog"
    blog.Status |> should equal 200
    blog.Body |> should equal "blog"
}

[<Fact>]
let ``multiple redirects on same table`` () = task {
    let routes =
        Route.start
        |> Redirect.permanent "/a" "/b"
        |> Redirect.temporary "/c" "/d"

    let client = TestClient.create routes
    let! r1 = client |> TestClient.get "/a"
    r1.Status |> should equal 301
    r1.Headers |> findHeader "Location" |> should equal "/b"

    let! r2 = client |> TestClient.get "/c"
    r2.Status |> should equal 302
    r2.Headers |> findHeader "Location" |> should equal "/d"
}

[<Fact>]
let ``permanent works inside Route.group`` () = task {
    let routes =
        Route.start
        |> Route.group "/api" (fun api ->
            api |> Redirect.permanent "/v1" "/api/v2"
        )

    let client = TestClient.create routes
    let! r = client |> TestClient.get "/api/v1"
    r.Status |> should equal 301
    r.Headers |> findHeader "Location" |> should equal "/api/v2"
}

[<Fact>]
let ``redirect does not match POST requests`` () = task {
    let routes =
        Route.start
        |> Redirect.permanent "/old" "/new"

    let client = TestClient.create routes
    let! r = client |> TestClient.post "/old" ""
    r.Status |> should equal 404
}
