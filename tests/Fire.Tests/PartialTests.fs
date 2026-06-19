module Fire.Tests.PartialTests

open Xunit
open FsUnit.Xunit
open Firefly

let findHeader (name: string) (headers: (string * string) list) =
    headers |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

let htmlPage title body =
    $"<!DOCTYPE html><html><head><title>{title}</title></head><body>{body}</body></html>"

[<Fact>]
let ``middleware strips HTML shell when X-Fire-Navigation is present`` () = task {
    let routes =
        Route.start
        |> Route.get "/page" (fun _ -> task {
            return Response.html (htmlPage "My Page" "<h1>Hello</h1>")
        })
    let config = App.defaults |> App.middleware Partial.middleware
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "X-Fire-Navigation" "true"
    let! r = client |> TestClient.get "/page"
    r.Status |> should equal 200
    r.Body |> should equal "<h1>Hello</h1>"
    r.Body |> should not' (haveSubstring "<!DOCTYPE")
    r.Body |> should not' (haveSubstring "<head>")
}

[<Fact>]
let ``middleware sets X-Fire-Title header`` () = task {
    let routes =
        Route.start
        |> Route.get "/page" (fun _ -> task {
            return Response.html (htmlPage "Dashboard" "<p>content</p>")
        })
    let config = App.defaults |> App.middleware Partial.middleware
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "X-Fire-Navigation" "true"
    let! r = client |> TestClient.get "/page"
    r.Headers |> findHeader "X-Fire-Title" |> should equal (Some "Dashboard")
}

[<Fact>]
let ``middleware passes through non-HTML responses unchanged`` () = task {
    let routes =
        Route.start
        |> Route.get "/api" (fun _ -> task {
            return Response.json {| ok = true |}
        })
    let config = App.defaults |> App.middleware Partial.middleware
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "X-Fire-Navigation" "true"
    let! r = client |> TestClient.get "/api"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "ok"
    r.Headers |> findHeader "X-Fire-Title" |> should equal None
}

[<Fact>]
let ``middleware passes through when header is absent`` () = task {
    let routes =
        Route.start
        |> Route.get "/page" (fun _ -> task {
            return Response.html (htmlPage "Full" "<p>body</p>")
        })
    let config = App.defaults |> App.middleware Partial.middleware
    let client = TestClient.createWith routes config
    let! r = client |> TestClient.get "/page"
    r.Body |> should haveSubstring "<!DOCTYPE"
    r.Body |> should haveSubstring "<head>"
}

[<Fact>]
let ``middleware handles missing title gracefully`` () = task {
    let routes =
        Route.start
        |> Route.get "/page" (fun _ -> task {
            return Response.html "<html><head></head><body><p>no title</p></body></html>"
        })
    let config = App.defaults |> App.middleware Partial.middleware
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "X-Fire-Navigation" "true"
    let! r = client |> TestClient.get "/page"
    r.Body |> should equal "<p>no title</p>"
    // Empty title value may be dropped by ASP.NET Core headers
    let title = r.Headers |> findHeader "X-Fire-Title" |> Option.defaultValue ""
    title |> should equal ""
}

[<Fact>]
let ``middleware handles response without body tags`` () = task {
    let routes =
        Route.start
        |> Route.get "/partial" (fun _ -> task {
            return Response.html "<p>fragment</p>"
        })
    let config = App.defaults |> App.middleware Partial.middleware
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "X-Fire-Navigation" "true"
    let! r = client |> TestClient.get "/partial"
    // No body tags to extract from — pass through unchanged
    r.Body |> should equal "<p>fragment</p>"
}

[<Fact>]
let ``middleware handles body tag with attributes`` () = task {
    let routes =
        Route.start
        |> Route.get "/page" (fun _ -> task {
            return Response.html """<!DOCTYPE html><html><head><title>Dark</title></head><body class="dark"><p>content</p></body></html>"""
        })
    let config = App.defaults |> App.middleware Partial.middleware
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "X-Fire-Navigation" "true"
    let! r = client |> TestClient.get "/page"
    r.Body |> should equal "<p>content</p>"
    r.Headers |> findHeader "X-Fire-Title" |> should equal (Some "Dark")
}
