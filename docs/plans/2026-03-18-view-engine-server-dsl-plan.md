# View Engine Server-Side DSL — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add type-safe, composable server-side HTML rendering to Fire via an F# DSL (`Node`, `Attr`, `Html`, `Render`, `View`).

**Architecture:** Three new files under `src/Fire/View/` — `Node.fs` (types + Html static class), `Render.fs` (StringBuilder-based HTML rendering), `View.fs` (ViewConfig pipeline that produces a `Response`). TDD with xUnit + FsUnit.

**Tech Stack:** F#, xUnit, FsUnit.Xunit, System.Text.StringBuilder, System.Net.WebUtility

**Design doc:** `docs/plans/2026-03-17-view-engine-server-dsl-design.md`

---

### Task 1: Node and Attr Types

**Files:**
- Create: `src/Fire/View/Node.fs`
- Create: `tests/Fire.Tests/NodeTests.fs`
- Modify: `src/Fire/Fire.fsproj` (add compile entry)
- Modify: `tests/Fire.Tests/Fire.Tests.fsproj` (add compile entry)

**Step 1: Add Node.fs to Fire.fsproj**

Insert after line 15 (before `Request.fs`), since `Node.fs` has no Fire dependencies:

```xml
    <Compile Include="View/Node.fs" />
```

Add `NodeTests.fs` to the test project — insert before the closing `</ItemGroup>` at line 57:

```xml
    <Compile Include="NodeTests.fs" />
```

**Step 2: Write the failing tests**

File: `tests/Fire.Tests/NodeTests.fs`

```fsharp
module Fire.Tests.NodeTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Text node holds string`` () =
    let node = Text "hello"
    match node with
    | Text s -> s |> should equal "hello"
    | _ -> failwith "expected Text"

[<Fact>]
let ``Element node holds tag, attrs, children`` () =
    let node = Element("div", [ Class "box" ], [ Text "hi" ])
    match node with
    | Element(tag, attrs, children) ->
        tag |> should equal "div"
        attrs |> should equal [ Class "box" ]
        children |> should equal [ Text "hi" ]
    | _ -> failwith "expected Element"

[<Fact>]
let ``Fragment holds list of nodes`` () =
    let node = Fragment [ Text "a"; Text "b" ]
    match node with
    | Fragment nodes -> nodes |> should haveLength 2
    | _ -> failwith "expected Fragment"

[<Fact>]
let ``Empty is Empty`` () =
    let node = Empty
    node |> should equal Empty

[<Fact>]
let ``Raw holds unescaped string`` () =
    let node = Raw "<b>bold</b>"
    match node with
    | Raw s -> s |> should equal "<b>bold</b>"
    | _ -> failwith "expected Raw"

[<Fact>]
let ``Boolean attrs have no value`` () =
    let attr = Disabled
    match attr with
    | Disabled -> ()
    | _ -> failwith "expected Disabled"

[<Fact>]
let ``Data attr holds key-value pair`` () =
    let attr = Data("toggle", "modal")
    match attr with
    | Data(k, v) ->
        k |> should equal "toggle"
        v |> should equal "modal"
    | _ -> failwith "expected Data"

[<Fact>]
let ``Html.div with children creates Element`` () =
    let node = Html.div [ Text "hello" ]
    match node with
    | Element("div", [], [ Text "hello" ]) -> ()
    | _ -> failwith "expected div element"

[<Fact>]
let ``Html.div with attrs and children creates Element`` () =
    let node = Html.div ([ Class "box" ], [ Text "hello" ])
    match node with
    | Element("div", [ Class "box" ], [ Text "hello" ]) -> ()
    | _ -> failwith "expected div element with attrs"

[<Fact>]
let ``Html.input is void element with attrs`` () =
    let node = Html.input [ Type "text"; Name "email" ]
    match node with
    | Element("input", [ Type "text"; Name "email" ], []) -> ()
    | _ -> failwith "expected input element"

[<Fact>]
let ``Html.br with no args creates empty br`` () =
    let node = Html.br ()
    match node with
    | Element("br", [], []) -> ()
    | _ -> failwith "expected br element"

[<Fact>]
let ``Html.element escape hatch works`` () =
    let node = Html.element "dialog" [ Text "content" ]
    match node with
    | Element("dialog", [], [ Text "content" ]) -> ()
    | _ -> failwith "expected dialog element"

[<Fact>]
let ``Nested elements compose`` () =
    let node =
        Html.div [
            Html.h1 [ Text "Title" ]
            Html.p [ Text "Body" ]
        ]
    match node with
    | Element("div", [], [ Element("h1", _, _); Element("p", _, _) ]) -> ()
    | _ -> failwith "expected nested structure"
```

**Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~NodeTests" --no-restore`
Expected: Build failure — `Node`, `Attr`, `Html` not defined.

**Step 4: Write Node.fs implementation**

File: `src/Fire/View/Node.fs`

```fsharp
namespace Fire

type Node =
    | Element of tag: string * attrs: Attr list * children: Node list
    | Text of string
    | Raw of string
    | Fragment of Node list
    | Empty

and Attr =
    | Class of string
    | Id of string
    | Href of string
    | Src of string
    | Type of string
    | Name of string
    | Value of string
    | Placeholder of string
    | Style of string
    | Disabled
    | Checked
    | Required
    | Readonly
    | Data of string * string
    | Custom of string * string

type Html =
    // Container / structural
    static member div (children: Node list) = Element("div", [], children)
    static member div (attrs: Attr list, children: Node list) = Element("div", attrs, children)
    static member span (children: Node list) = Element("span", [], children)
    static member span (attrs: Attr list, children: Node list) = Element("span", attrs, children)
    static member section (children: Node list) = Element("section", [], children)
    static member section (attrs: Attr list, children: Node list) = Element("section", attrs, children)
    static member nav (children: Node list) = Element("nav", [], children)
    static member nav (attrs: Attr list, children: Node list) = Element("nav", attrs, children)
    static member main (children: Node list) = Element("main", [], children)
    static member main (attrs: Attr list, children: Node list) = Element("main", attrs, children)
    static member header (children: Node list) = Element("header", [], children)
    static member header (attrs: Attr list, children: Node list) = Element("header", attrs, children)
    static member footer (children: Node list) = Element("footer", [], children)
    static member footer (attrs: Attr list, children: Node list) = Element("footer", attrs, children)
    static member article (children: Node list) = Element("article", [], children)
    static member article (attrs: Attr list, children: Node list) = Element("article", attrs, children)
    static member aside (children: Node list) = Element("aside", [], children)
    static member aside (attrs: Attr list, children: Node list) = Element("aside", attrs, children)

    // Headings
    static member h1 (children: Node list) = Element("h1", [], children)
    static member h1 (attrs: Attr list, children: Node list) = Element("h1", attrs, children)
    static member h2 (children: Node list) = Element("h2", [], children)
    static member h2 (attrs: Attr list, children: Node list) = Element("h2", attrs, children)
    static member h3 (children: Node list) = Element("h3", [], children)
    static member h3 (attrs: Attr list, children: Node list) = Element("h3", attrs, children)
    static member h4 (children: Node list) = Element("h4", [], children)
    static member h4 (attrs: Attr list, children: Node list) = Element("h4", attrs, children)
    static member h5 (children: Node list) = Element("h5", [], children)
    static member h5 (attrs: Attr list, children: Node list) = Element("h5", attrs, children)
    static member h6 (children: Node list) = Element("h6", [], children)
    static member h6 (attrs: Attr list, children: Node list) = Element("h6", attrs, children)

    // Text
    static member p (children: Node list) = Element("p", [], children)
    static member p (attrs: Attr list, children: Node list) = Element("p", attrs, children)
    static member a (children: Node list) = Element("a", [], children)
    static member a (attrs: Attr list, children: Node list) = Element("a", attrs, children)
    static member strong (children: Node list) = Element("strong", [], children)
    static member strong (attrs: Attr list, children: Node list) = Element("strong", attrs, children)
    static member em (children: Node list) = Element("em", [], children)
    static member em (attrs: Attr list, children: Node list) = Element("em", attrs, children)
    static member small (children: Node list) = Element("small", [], children)
    static member small (attrs: Attr list, children: Node list) = Element("small", attrs, children)
    static member code (children: Node list) = Element("code", [], children)
    static member code (attrs: Attr list, children: Node list) = Element("code", attrs, children)
    static member pre (children: Node list) = Element("pre", [], children)
    static member pre (attrs: Attr list, children: Node list) = Element("pre", attrs, children)
    static member blockquote (children: Node list) = Element("blockquote", [], children)
    static member blockquote (attrs: Attr list, children: Node list) = Element("blockquote", attrs, children)

    // Forms
    static member form (children: Node list) = Element("form", [], children)
    static member form (attrs: Attr list, children: Node list) = Element("form", attrs, children)
    static member label (children: Node list) = Element("label", [], children)
    static member label (attrs: Attr list, children: Node list) = Element("label", attrs, children)
    static member button (children: Node list) = Element("button", [], children)
    static member button (attrs: Attr list, children: Node list) = Element("button", attrs, children)
    static member select (children: Node list) = Element("select", [], children)
    static member select (attrs: Attr list, children: Node list) = Element("select", attrs, children)
    static member option (children: Node list) = Element("option", [], children)
    static member option (attrs: Attr list, children: Node list) = Element("option", attrs, children)
    static member textarea (children: Node list) = Element("textarea", [], children)
    static member textarea (attrs: Attr list, children: Node list) = Element("textarea", attrs, children)

    // Lists
    static member ul (children: Node list) = Element("ul", [], children)
    static member ul (attrs: Attr list, children: Node list) = Element("ul", attrs, children)
    static member ol (children: Node list) = Element("ol", [], children)
    static member ol (attrs: Attr list, children: Node list) = Element("ol", attrs, children)
    static member li (children: Node list) = Element("li", [], children)
    static member li (attrs: Attr list, children: Node list) = Element("li", attrs, children)

    // Tables
    static member table (children: Node list) = Element("table", [], children)
    static member table (attrs: Attr list, children: Node list) = Element("table", attrs, children)
    static member thead (children: Node list) = Element("thead", [], children)
    static member thead (attrs: Attr list, children: Node list) = Element("thead", attrs, children)
    static member tbody (children: Node list) = Element("tbody", [], children)
    static member tbody (attrs: Attr list, children: Node list) = Element("tbody", attrs, children)
    static member tr (children: Node list) = Element("tr", [], children)
    static member tr (attrs: Attr list, children: Node list) = Element("tr", attrs, children)
    static member td (children: Node list) = Element("td", [], children)
    static member td (attrs: Attr list, children: Node list) = Element("td", attrs, children)
    static member th (children: Node list) = Element("th", [], children)
    static member th (attrs: Attr list, children: Node list) = Element("th", attrs, children)

    // Void elements (no children)
    static member br () = Element("br", [], [])
    static member br (attrs: Attr list) = Element("br", attrs, [])
    static member hr () = Element("hr", [], [])
    static member hr (attrs: Attr list) = Element("hr", attrs, [])
    static member img (attrs: Attr list) = Element("img", attrs, [])
    static member input (attrs: Attr list) = Element("input", attrs, [])
    static member meta (attrs: Attr list) = Element("meta", attrs, [])
    static member link (attrs: Attr list) = Element("link", attrs, [])

    // Escape hatch
    static member element (tag: string) (children: Node list) = Element(tag, [], children)
    static member element (tag: string) (attrs: Attr list, children: Node list) = Element(tag, attrs, children)
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~NodeTests" --no-restore`
Expected: All 13 tests PASS.

**Step 6: Commit**

```bash
git add src/Fire/View/Node.fs src/Fire/Fire.fsproj tests/Fire.Tests/NodeTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add Node, Attr, Html types for view engine DSL"
```

---

### Task 2: Render.toHtml

**Files:**
- Create: `src/Fire/View/Render.fs`
- Create: `tests/Fire.Tests/RenderTests.fs`
- Modify: `src/Fire/Fire.fsproj` (add compile entry after Node.fs)
- Modify: `tests/Fire.Tests/Fire.Tests.fsproj` (add compile entry)

**Step 1: Add Render.fs to Fire.fsproj**

Insert immediately after the `View/Node.fs` entry:

```xml
    <Compile Include="View/Render.fs" />
```

Add `RenderTests.fs` to test project after `NodeTests.fs`:

```xml
    <Compile Include="RenderTests.fs" />
```

**Step 2: Write the failing tests**

File: `tests/Fire.Tests/RenderTests.fs`

```fsharp
module Fire.Tests.RenderTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Render Text HTML-encodes content`` () =
    let html = Render.toHtml (Text "<script>alert('xss')</script>")
    html |> should equal "&lt;script&gt;alert(&#x27;xss&#x27;)&lt;/script&gt;"

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
```

**Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~RenderTests" --no-restore`
Expected: Build failure — `Render` module not defined.

**Step 4: Write Render.fs implementation**

File: `src/Fire/View/Render.fs`

```fsharp
namespace Fire

open System.Net
open System.Text

[<RequireQualifiedAccess>]
module Render =

    let private voidElements =
        Set.ofList [ "br"; "hr"; "img"; "input"; "meta"; "link" ]

    let private renderAttr (sb: StringBuilder) (attr: Attr) =
        match attr with
        | Class v -> sb.Append($""" class="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Id v -> sb.Append($""" id="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Href v -> sb.Append($""" href="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Src v -> sb.Append($""" src="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Type v -> sb.Append($""" type="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Name v -> sb.Append($""" name="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Value v -> sb.Append($""" value="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Placeholder v -> sb.Append($""" placeholder="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Style v -> sb.Append($""" style="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Disabled -> sb.Append(" disabled") |> ignore
        | Checked -> sb.Append(" checked") |> ignore
        | Required -> sb.Append(" required") |> ignore
        | Readonly -> sb.Append(" readonly") |> ignore
        | Data(k, v) -> sb.Append($""" data-{k}="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Custom(k, v) -> sb.Append($""" {k}="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore

    let rec private render (sb: StringBuilder) (node: Node) =
        match node with
        | Text s -> sb.Append(WebUtility.HtmlEncode s) |> ignore
        | Raw s -> sb.Append(s) |> ignore
        | Empty -> ()
        | Fragment nodes -> for n in nodes do render sb n
        | Element(tag, attrs, children) ->
            sb.Append('<').Append(tag) |> ignore
            for attr in attrs do renderAttr sb attr
            sb.Append('>') |> ignore
            if not (voidElements.Contains tag) then
                for child in children do render sb child
                sb.Append("</").Append(tag).Append('>') |> ignore

    let toHtml (node: Node) : string =
        let sb = StringBuilder()
        render sb node
        sb.ToString()
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~RenderTests" --no-restore`
Expected: All 15 tests PASS.

**Step 6: Commit**

```bash
git add src/Fire/View/Render.fs src/Fire/Fire.fsproj tests/Fire.Tests/RenderTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add Render.toHtml — StringBuilder-based HTML rendering"
```

---

### Task 3: View Module

**Files:**
- Create: `src/Fire/View/View.fs`
- Create: `tests/Fire.Tests/ViewTests.fs`
- Modify: `src/Fire/Fire.fsproj` (add compile entry after Render.fs, before App.fs)
- Modify: `tests/Fire.Tests/Fire.Tests.fsproj` (add compile entry)

**Step 1: Add View.fs to Fire.fsproj**

Insert after `View/Render.fs` and before `App.fs` (View.fs depends on Response.fs and Render.fs):

```xml
    <Compile Include="View/View.fs" />
```

Add `ViewTests.fs` to test project after `RenderTests.fs`:

```xml
    <Compile Include="ViewTests.fs" />
```

**Step 2: Write the failing tests**

File: `tests/Fire.Tests/ViewTests.fs`

```fsharp
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
    | Text body ->
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
    | Text body ->
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
    | Text body ->
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
    | Text body ->
        let idxA = body.IndexOf("/a.js")
        let idxB = body.IndexOf("/b.js")
        idxA |> should be (lessThan idxB)
    | _ -> failwith "expected Text body"
```

**Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~ViewTests" --no-restore`
Expected: Build failure — `View`, `ViewConfig` not defined.

**Step 4: Write View.fs implementation**

File: `src/Fire/View/View.fs`

```fsharp
namespace Fire

type ViewConfig = {
    Title: string
    Content: Node
    Scripts: string list
    Styles: string list
    Head: Node list
    Layout: (string -> string -> string) option
}

[<RequireQualifiedAccess>]
module View =

    let page (title: string) (content: Node) : ViewConfig =
        { Title = title
          Content = content
          Scripts = []
          Styles = []
          Head = []
          Layout = None }

    let withScript (src: string) (config: ViewConfig) : ViewConfig =
        { config with Scripts = config.Scripts @ [ src ] }

    let withStyle (href: string) (config: ViewConfig) : ViewConfig =
        { config with Styles = config.Styles @ [ href ] }

    let withHead (node: Node) (config: ViewConfig) : ViewConfig =
        { config with Head = config.Head @ [ node ] }

    let withLayout (layout: string -> string -> string) (config: ViewConfig) : ViewConfig =
        { config with Layout = Some layout }

    let render (config: ViewConfig) : Response =
        let content = Render.toHtml config.Content
        let html =
            match config.Layout with
            | Some layout -> layout config.Title content
            | None ->
                let sb = System.Text.StringBuilder()
                sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">") |> ignore
                sb.Append($"<title>{System.Net.WebUtility.HtmlEncode config.Title}</title>") |> ignore
                for href in config.Styles do
                    sb.Append($"""<link rel="stylesheet" href="{System.Net.WebUtility.HtmlEncode href}">""") |> ignore
                for node in config.Head do
                    sb.Append(Render.toHtml node) |> ignore
                sb.Append("</head><body>") |> ignore
                sb.Append(content) |> ignore
                for src in config.Scripts do
                    sb.Append($"""<script src="{System.Net.WebUtility.HtmlEncode src}"></script>""") |> ignore
                sb.Append("</body></html>") |> ignore
                sb.ToString()
        Response.html html
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~ViewTests" --no-restore`
Expected: All 8 tests PASS.

**Step 6: Run full test suite**

Run: `dotnet test tests/Fire.Tests --no-restore`
Expected: All tests PASS (existing + new).

**Step 7: Commit**

```bash
git add src/Fire/View/View.fs src/Fire/Fire.fsproj tests/Fire.Tests/ViewTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add View module — ViewConfig pipeline with layout support"
```

---

### Task 4: Full Suite Verification & Cleanup

**Files:**
- No new files

**Step 1: Run the complete test suite**

Run: `dotnet test tests/ --no-restore`
Expected: All tests in Fire.Tests and Flame.Tests PASS.

**Step 2: Build the full solution**

Run: `dotnet build --no-restore`
Expected: Build succeeds with no warnings from View files.

**Step 3: Commit if any cleanup was needed**

Only commit if changes were made during cleanup.
