module Fire.Tests.ViewTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``View.page creates ViewConfig with title and content`` () =
    let config = View.page "Home" (Html.h1 [ Text "Hello" ])
    config.Title |> should equal "Home"
    config.Scripts |> should equal List.empty<string>
    config.Styles |> should equal List.empty<string>
    config.Head |> should equal List.empty<Node>
    config.Layout |> should equal None

[<Fact>]
let ``View.withScript adds script`` () =
    let config =
        View.page "Home" (Text "hi")
        |> View.withScript "/app.js"
    config.Scripts |> should equal [ "/app.js" ]

[<Fact>]
let ``View.withStyle adds style`` () =
    let config =
        View.page "Home" (Text "hi")
        |> View.withStyle "/app.css"
    config.Styles |> should equal [ "/app.css" ]

[<Fact>]
let ``View.withHead adds head node`` () =
    let meta = Html.meta [ Custom("name", "description"); Custom("content", "A page") ]
    let config =
        View.page "Home" (Text "hi")
        |> View.withHead meta
    config.Head |> should haveLength 1

[<Fact>]
let ``View.render without layout produces default HTML document`` () =
    let response =
        View.page "Home" (Html.h1 [ Text "Hello" ])
        |> View.withStyle "/app.css"
        |> View.withScript "/app.js"
        |> View.render
    response.Status |> should equal 200
    response.Headers |> should contain ("Content-Type", "text/html; charset=utf-8")
    match response.Body with
    | ResponseBody.Text body ->
        body |> should haveSubstring "<!DOCTYPE html>"
        body |> should haveSubstring "<title>Home</title>"
        body |> should haveSubstring "<h1>Hello</h1>"
        body |> should haveSubstring """<link rel="stylesheet" href="/app.css">"""
        body |> should haveSubstring """<script src="/app.js"></script>"""
    | _ -> failwith "expected Text body"

[<Fact>]
let ``View.render with layout delegates to layout function`` () =
    let myLayout (title: string) (content: string) =
        $"<html><head><title>{title}</title></head><body>{content}</body></html>"
    let response =
        View.page "About" (Html.p [ Text "Info" ])
        |> View.withLayout myLayout
        |> View.render
    match response.Body with
    | ResponseBody.Text body ->
        body |> should haveSubstring "<title>About</title>"
        body |> should haveSubstring "<p>Info</p>"
    | _ -> failwith "expected Text body"

[<Fact>]
let ``View.render with head nodes includes them`` () =
    let meta = Html.meta [ Custom("name", "robots"); Custom("content", "noindex") ]
    let response =
        View.page "Home" (Text "hi")
        |> View.withHead meta
        |> View.render
    match response.Body with
    | ResponseBody.Text body ->
        body |> should haveSubstring """<meta name="robots" content="noindex">"""
    | _ -> failwith "expected Text body"

[<Fact>]
let ``View.render multiple scripts appear in order`` () =
    let response =
        View.page "Home" (Text "hi")
        |> View.withScript "/a.js"
        |> View.withScript "/b.js"
        |> View.render
    match response.Body with
    | ResponseBody.Text body ->
        let idxA = body.IndexOf("/a.js")
        let idxB = body.IndexOf("/b.js")
        idxA |> should be (lessThan idxB)
    | _ -> failwith "expected Text body"
