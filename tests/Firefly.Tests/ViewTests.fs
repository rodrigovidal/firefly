module Firefly.Tests.ViewTests

open Xunit
open FsUnit.Xunit
open Firefly

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
        body |> should haveSubstring """<!DOCTYPE html><html lang="en">"""
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

[<Fact>]
let ``View.render with layout ignores scripts and styles`` () =
    let myLayout (title: string) (content: string) =
        $"<html><head><title>{title}</title></head><body>{content}</body></html>"
    let response =
        View.page "Test" (Html.p [ Text "hi" ])
        |> View.withScript "/app.js"
        |> View.withStyle "/app.css"
        |> View.withLayout myLayout
        |> View.render
    match response.Body with
    | ResponseBody.Text body ->
        body |> should not' (haveSubstring "/app.js")
        body |> should not' (haveSubstring "/app.css")
        body |> should haveSubstring "<p>hi</p>"
    | _ -> failwith "expected Text body"

[<Fact>]
let ``View.layout middleware wraps body content`` () = task {
    let adminWrap (_title: string) (content: string) =
        $"""<div class="admin"><nav>Sidebar</nav><div class="main">{content}</div></div>"""
    let inner : Handler = fun _ -> task {
        return
            View.page "Dashboard" (Html.h1 [ Text "Hello" ])
            |> View.render
    }
    let handler = (View.layout adminWrap) inner
    let! response = handler (Unchecked.defaultof<Request>)
    match response.Body with
    | ResponseBody.Text body ->
        body |> should haveSubstring "<nav>Sidebar</nav>"
        body |> should haveSubstring "<h1>Hello</h1>"
        body |> should haveSubstring "class=\"admin\""
    | _ -> failwith "expected Text body layout"
}

[<Fact>]
let ``View.layout middleware passes non-HTML through`` () = task {
    let wrap (_t: string) (c: string) = $"<div>{c}</div>"
    let inner : Handler = fun _ -> task { return Response.json {| x = 1 |} }
    let handler = (View.layout wrap) inner
    let! response = handler (Unchecked.defaultof<Request>)
    response.Headers |> should not' (contain ("Content-Type", "text/html; charset=utf-8"))
}

[<Fact>]
let ``View.layout middleware extracts title`` () = task {
    let mutable receivedTitle = ""
    let wrap (title: string) (content: string) =
        receivedTitle <- title
        $"<div>{content}</div>"
    let inner : Handler = fun _ -> task {
        return View.page "MyTitle" (Text "hi") |> View.render
    }
    let handler = (View.layout wrap) inner
    let! _ = handler (Unchecked.defaultof<Request>)
    receivedTitle |> should equal "MyTitle"
}

[<Fact>]
let ``View.layout composes for nested layouts`` () = task {
    let outerWrap (_title: string) (content: string) =
        $"""<div class="outer">{content}</div>"""
    let innerWrap (_title: string) (content: string) =
        $"""<div class="inner">{content}</div>"""
    let inner : Handler = fun _ -> task {
        return View.page "Test" (Html.p [ Text "content" ]) |> View.render
    }
    let handler = (View.layout outerWrap) ((View.layout innerWrap) inner)
    let! response = handler (Unchecked.defaultof<Request>)
    match response.Body with
    | ResponseBody.Text body ->
        body |> should haveSubstring "class=\"outer\""
        body |> should haveSubstring "class=\"inner\""
        body |> should haveSubstring "<p>content</p>"
        let outerIdx = body.IndexOf("outer")
        let innerIdx = body.IndexOf("inner")
        outerIdx |> should be (lessThan innerIdx)
    | _ -> failwith "expected Text body nested"
}

// --- Meta helpers ---

[<Fact>]
let ``Meta.description renders meta tag`` () =
    let html = Meta.description "A great page" |> Render.toHtml
    html |> should haveSubstring "name=\"description\""
    html |> should haveSubstring "content=\"A great page\""

[<Fact>]
let ``Meta.ogTitle renders og:title meta tag`` () =
    let html = Meta.ogTitle "My Page" |> Render.toHtml
    html |> should haveSubstring "property=\"og:title\""
    html |> should haveSubstring "content=\"My Page\""

[<Fact>]
let ``Meta.ogImage renders og:image meta tag`` () =
    let html = Meta.ogImage "/img/hero.png" |> Render.toHtml
    html |> should haveSubstring "property=\"og:image\""
    html |> should haveSubstring "/img/hero.png"

[<Fact>]
let ``Meta.robots renders robots meta tag`` () =
    let html = Meta.robots "noindex, nofollow" |> Render.toHtml
    html |> should haveSubstring "name=\"robots\""
    html |> should haveSubstring "noindex, nofollow"

[<Fact>]
let ``Meta.canonical renders link tag`` () =
    let html = Meta.canonical "https://example.com/page" |> Render.toHtml
    html |> should haveSubstring "rel=\"canonical\""
    html |> should haveSubstring "href=\"https://example.com/page\""

[<Fact>]
let ``Meta tags compose with View.withHead`` () =
    let response =
        View.page "Test" (Text "hi")
        |> View.withHead (Meta.description "Test page")
        |> View.withHead (Meta.robots "index")
        |> View.render
    match response.Body with
    | ResponseBody.Text body ->
        body |> should haveSubstring "name=\"description\""
        body |> should haveSubstring "name=\"robots\""
    | _ -> failwith "expected Text body meta"

// --- Error boundary ---

[<Fact>]
let ``View.errorBoundary catches exception and renders fallback`` () = task {
    let fallback (ex: exn) (_title: string) =
        Html.div [ Html.h1 [ Text "Oops" ]; Html.p [ Text ex.Message ] ]
    let inner : Handler = fun _ -> task {
        return failwith "broken"
    }
    let handler = (View.errorBoundary fallback) inner
    let! response = handler (Unchecked.defaultof<Request>)
    response.Status |> should equal 500
    match response.Body with
    | ResponseBody.Text body ->
        body |> should haveSubstring "Oops"
        body |> should haveSubstring "broken"
    | _ -> failwith "expected Text body error"
}

[<Fact>]
let ``View.errorBoundary passes successful responses through`` () = task {
    let fallback (_ex: exn) (_title: string) = Html.p [ Text "error" ]
    let inner : Handler = fun _ -> task { return Response.text "ok" }
    let handler = (View.errorBoundary fallback) inner
    let! response = handler (Unchecked.defaultof<Request>)
    response.Status |> should equal 200
    match response.Body with
    | ResponseBody.Text s -> s |> should equal "ok"
    | _ -> failwith "expected Text body"
}
