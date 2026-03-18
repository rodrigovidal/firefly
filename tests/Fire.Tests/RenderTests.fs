module Fire.Tests.RenderTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Render Text HTML-encodes content`` () =
    let html = Render.toHtml (Text "<script>alert('xss')</script>")
    html |> should equal "&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;"

[<Fact>]
let ``Render Raw passes through unescaped`` () =
    let html = Render.toHtml (Raw "<b>bold</b>")
    html |> should equal "<b>bold</b>"

[<Fact>]
let ``Render Empty produces empty string`` () =
    let html = Render.toHtml Empty
    html |> should equal ""

[<Fact>]
let ``Render Fragment concatenates children`` () =
    let html = Render.toHtml (Fragment [ Text "a"; Text "b" ])
    html |> should equal "ab"

[<Fact>]
let ``Render Element with no attrs`` () =
    let html = Render.toHtml (Html.div [ Text "hello" ])
    html |> should equal "<div>hello</div>"

[<Fact>]
let ``Render Element with attrs`` () =
    let html = Render.toHtml (Html.div ([ Class "box"; Id "main" ], [ Text "hi" ]))
    html |> should equal """<div class="box" id="main">hi</div>"""

[<Fact>]
let ``Render nested elements`` () =
    let html = Render.toHtml (Html.ul [ Html.li [ Text "one" ]; Html.li [ Text "two" ] ])
    html |> should equal "<ul><li>one</li><li>two</li></ul>"

[<Fact>]
let ``Render void element self-closes`` () =
    let html = Render.toHtml (Html.br ())
    html |> should equal "<br>"

[<Fact>]
let ``Render void element with attrs`` () =
    let html = Render.toHtml (Html.input [ Type "text"; Name "email" ])
    html |> should equal """<input type="text" name="email">"""

[<Fact>]
let ``Render img with src`` () =
    let html = Render.toHtml (Html.img [ Src "/logo.png" ])
    html |> should equal """<img src="/logo.png">"""

[<Fact>]
let ``Render boolean attr has no value`` () =
    let html = Render.toHtml (Html.input [ Type "checkbox"; Checked; Disabled ])
    html |> should equal """<input type="checkbox" checked disabled>"""

[<Fact>]
let ``Render Data attr`` () =
    let html = Render.toHtml (Html.div ([ Data("toggle", "modal") ], [ Text "click" ]))
    html |> should equal """<div data-toggle="modal">click</div>"""

[<Fact>]
let ``Render Custom attr`` () =
    let html = Render.toHtml (Html.div ([ Custom("aria-label", "Close") ], [ Text "x" ]))
    html |> should equal """<div aria-label="Close">x</div>"""

[<Fact>]
let ``Render attr values are HTML-encoded`` () =
    let html = Render.toHtml (Html.div ([ Class "a\"b" ], [ Text "hi" ]))
    html |> should equal """<div class="a&quot;b">hi</div>"""

[<Fact>]
let ``Render complex nested structure`` () =
    let html =
        Html.div ([ Class "page" ], [
            Html.h1 [ Text "Title" ]
            Html.p ([ Class "lead" ], [ Text "Intro" ])
            Html.ul [
                Html.li [ Html.a ([ Href "/one" ], [ Text "One" ]) ]
                Html.li [ Html.a ([ Href "/two" ], [ Text "Two" ]) ]
            ]
        ])
        |> Render.toHtml
    html |> should equal """<div class="page"><h1>Title</h1><p class="lead">Intro</p><ul><li><a href="/one">One</a></li><li><a href="/two">Two</a></li></ul></div>"""
