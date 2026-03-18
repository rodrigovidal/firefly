module Fire.Tests.ViteTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Vite.loadManifest parses manifest JSON`` () =
    let json = """{"src/main.tsx":{"file":"assets/main-abc123.js","css":["assets/main-xyz789.css"]}}"""
    let manifest = Vite.loadManifest json
    manifest.ContainsKey("src/main.tsx") |> should be True
    manifest.["src/main.tsx"].File |> should equal "assets/main-abc123.js"
    manifest.["src/main.tsx"].Css |> should equal [ "assets/main-xyz789.css" ]

[<Fact>]
let ``Vite.loadManifest handles entry with no CSS`` () =
    let json = """{"src/main.tsx":{"file":"assets/main-abc123.js"}}"""
    let manifest = Vite.loadManifest json
    manifest.["src/main.tsx"].Css |> should equal List.empty<string>

[<Fact>]
let ``Vite.loadManifest handles multiple entries`` () =
    let json = """{"src/a.tsx":{"file":"assets/a.js"},"src/b.tsx":{"file":"assets/b.js","css":["assets/b.css"]}}"""
    let manifest = Vite.loadManifest json
    manifest.Count |> should equal 2

[<Fact>]
let ``Vite.scriptFromManifest returns script tag with hashed path`` () =
    let json = """{"src/main.tsx":{"file":"assets/main-abc123.js"}}"""
    let manifest = Vite.loadManifest json
    let node = Vite.scriptFromManifest manifest "src/main.tsx"
    let html = Render.toHtml node
    html |> should equal """<script type="module" src="/assets/main-abc123.js"></script>"""

[<Fact>]
let ``Vite.scriptFromManifest throws for missing entry`` () =
    let manifest = Vite.loadManifest """{}"""
    (fun () -> Vite.scriptFromManifest manifest "src/missing.tsx" |> ignore)
    |> should throw typeof<System.Collections.Generic.KeyNotFoundException>

[<Fact>]
let ``Vite.stylesFromManifest returns link tags`` () =
    let json = """{"src/main.tsx":{"file":"assets/main.js","css":["assets/main-a.css","assets/main-b.css"]}}"""
    let manifest = Vite.loadManifest json
    let html = Vite.stylesFromManifest manifest "src/main.tsx" |> Render.toHtml
    html |> should haveSubstring """<link rel="stylesheet" href="/assets/main-a.css">"""
    html |> should haveSubstring """<link rel="stylesheet" href="/assets/main-b.css">"""

[<Fact>]
let ``Vite.stylesFromManifest returns Empty when no CSS`` () =
    let json = """{"src/main.tsx":{"file":"assets/main.js"}}"""
    let manifest = Vite.loadManifest json
    Vite.stylesFromManifest manifest "src/main.tsx" |> should equal Empty

[<Fact>]
let ``Vite.scriptDev returns script pointing to Vite dev server`` () =
    let html = Vite.scriptDev 5173 "src/main.tsx" |> Render.toHtml
    html |> should equal """<script type="module" src="http://localhost:5173/src/main.tsx"></script>"""

[<Fact>]
let ``Vite.stylesDev returns Empty`` () =
    Vite.stylesDev () |> should equal Empty

[<Fact>]
let ``Vite.reactRefreshDev returns script with refresh runtime`` () =
    let html = Vite.reactRefreshDev 5173 |> Render.toHtml
    html |> should haveSubstring "@react-refresh"
    html |> should haveSubstring "http://localhost:5173"

[<Fact>]
let ``Vite.reactRefreshProd returns Empty`` () =
    Vite.reactRefreshProd () |> should equal Empty

[<Fact>]
let ``Vite.shouldProxy matches Vite paths`` () =
    Vite.shouldProxy "/@vite/client" |> should be True
    Vite.shouldProxy "/@react-refresh" |> should be True
    Vite.shouldProxy "/src/main.tsx" |> should be True
    Vite.shouldProxy "/node_modules/.vite/deps/react.js" |> should be True

[<Fact>]
let ``Vite.shouldProxy does not match Fire routes`` () =
    Vite.shouldProxy "/" |> should be False
    Vite.shouldProxy "/contacts" |> should be False
    Vite.shouldProxy "/api/users" |> should be False
    Vite.shouldProxy "/static/logo.png" |> should be False

[<Fact>]
let ``Vite.devMiddleware passes non-Vite requests through`` () = task {
    let inner : Handler = fun _ -> task { return Response.text "from fire" }
    let mw = Vite.devMiddleware 5173
    let handler = mw inner
    let! response = handler (Unchecked.defaultof<Request>)
    response.Body |> should equal (ResponseBody.Text "from fire")
}
