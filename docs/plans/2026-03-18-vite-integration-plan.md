# Vite Integration — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add Vite dev proxy middleware and manifest-based asset resolution to Fire, enabling client JS bundling with React HMR.

**Architecture:** Single `Vite.fs` module with: dev proxy middleware (forwards /@vite/*, /src/* to Vite dev server), asset helpers (`Vite.script`, `Vite.styles`, `Vite.reactRefresh`) that emit dev URLs or read `.vite/manifest.json` in production. Dev/prod detected via `ASPNETCORE_ENVIRONMENT`. TDD with xUnit + FsUnit.

**Tech Stack:** F#, System.Net.Http.HttpClient, System.Text.Json, xUnit, FsUnit.Xunit

**Design doc:** `docs/plans/2026-03-18-vite-integration-design.md`

---

### Task 1: Manifest Reader & Asset Helpers (script, styles)

The manifest reader and asset helpers are pure functions — no HTTP, no middleware. They can be fully unit tested.

**Files:**
- Create: `src/Fire/Vite.fs`
- Create: `tests/Fire.Tests/ViteTests.fs`
- Modify: `src/Fire/Fire.fsproj` (add compile entry before App.fs)
- Modify: `tests/Fire.Tests/Fire.Tests.fsproj` (add compile entry)

**Step 1: Add Vite.fs to Fire.fsproj**

Insert before `App.fs` (currently line 48) in `src/Fire/Fire.fsproj`:

```xml
    <Compile Include="Vite.fs" />
```

Add `ViteTests.fs` after `QueryTests.fs` in test project:

```xml
    <Compile Include="ViteTests.fs" />
```

**Step 2: Write the failing tests**

File: `tests/Fire.Tests/ViteTests.fs`

```fsharp
module Fire.Tests.ViteTests

open Xunit
open FsUnit.Xunit
open Fire

// --- Manifest parsing ---

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

// --- Script helper (production mode) ---

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

// --- Styles helper (production mode) ---

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

// --- Dev mode helpers ---

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

// --- shouldProxy ---

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
```

**Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~ViteTests" --no-restore`
Expected: Build failure — `Vite` module not defined.

**Step 4: Write Vite.fs implementation**

File: `src/Fire/Vite.fs`

```fsharp
namespace Fire

open System
open System.Text.Json

[<RequireQualifiedAccess>]
module Vite =

    // --- Manifest types and parsing ---

    type ManifestEntry = { File: string; Css: string list }

    let loadManifest (json: string) : Map<string, ManifestEntry> =
        let doc = JsonDocument.Parse(json)
        doc.RootElement.EnumerateObject()
        |> Seq.map (fun prop ->
            let file = prop.Value.GetProperty("file").GetString()
            let css =
                match prop.Value.TryGetProperty("css") with
                | true, arr -> arr.EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> Seq.toList
                | false, _ -> []
            prop.Name, { File = file; Css = css })
        |> Map.ofSeq

    // --- Production helpers ---

    let scriptFromManifest (manifest: Map<string, ManifestEntry>) (entry: string) : Node =
        let e = manifest.[entry]
        Element("script", [ Custom("type", "module"); Src $"/{e.File}" ], [])

    let stylesFromManifest (manifest: Map<string, ManifestEntry>) (entry: string) : Node =
        let e = manifest.[entry]
        match e.Css with
        | [] -> Empty
        | cssFiles ->
            Fragment [
                for href in cssFiles do
                    Element("link", [ Custom("rel", "stylesheet"); Href $"/{href}" ], [])
            ]

    let reactRefreshProd () : Node = Empty

    // --- Dev helpers ---

    let scriptDev (port: int) (entry: string) : Node =
        Element("script", [ Custom("type", "module"); Src $"http://localhost:{port}/{entry}" ], [])

    let stylesDev () : Node = Empty

    let reactRefreshDev (port: int) : Node =
        Raw $"""<script type="module">
import RefreshRuntime from 'http://localhost:{port}/@react-refresh'
RefreshRuntime.injectIntoGlobalHook(window)
window.$RefreshReg$ = () => {{}}
window.$RefreshSig$ = () => (type) => type
window.__vite_plugin_react_preamble_installed__ = true
</script>"""

    // --- Proxy path matching ---

    let shouldProxy (path: string) : bool =
        path.StartsWith("/@vite/", StringComparison.Ordinal) ||
        path.StartsWith("/@react-refresh", StringComparison.Ordinal) ||
        path.StartsWith("/src/", StringComparison.Ordinal) ||
        path.StartsWith("/node_modules/.vite/", StringComparison.Ordinal)

    // --- High-level API (wired by dev/prod mode) ---

    let private isDevelopment () =
        let env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        String.Equals(env, "Development", StringComparison.OrdinalIgnoreCase)
        || String.Equals(env, "dev", StringComparison.OrdinalIgnoreCase)

    let private mutable cachedManifest : Map<string, ManifestEntry> option = None
    let private defaultPort = 5173

    let private getManifest (manifestPath: string) =
        match cachedManifest with
        | Some m -> m
        | None ->
            if not (IO.File.Exists manifestPath) then
                failwith $"Vite manifest not found at {manifestPath}. Run 'npm run build' first."
            let json = IO.File.ReadAllText manifestPath
            let m = loadManifest json
            cachedManifest <- Some m
            m

    /// Returns a <script type="module"> tag for the given Vite entry.
    /// Dev: points to Vite dev server. Prod: hashed path from manifest.
    let script (entry: string) : Node =
        if isDevelopment () then scriptDev defaultPort entry
        else scriptFromManifest (getManifest "wwwroot/.vite/manifest.json") entry

    /// Returns <link rel="stylesheet"> tags for CSS associated with the entry.
    /// Dev: Empty (Vite injects CSS via HMR). Prod: from manifest.
    let styles (entry: string) : Node =
        if isDevelopment () then stylesDev ()
        else stylesFromManifest (getManifest "wwwroot/.vite/manifest.json") entry

    /// Returns React refresh preamble script.
    /// Dev: injects refresh runtime. Prod: Empty.
    let reactRefresh () : Node =
        if isDevelopment () then reactRefreshDev defaultPort
        else reactRefreshProd ()
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~ViteTests" --no-restore`
Expected: All 13 tests PASS.

**Step 6: Commit**

IMPORTANT: Do NOT add Co-Authored-By lines mentioning Claude. Do NOT mention Claude or AI in commit messages.

```bash
git add src/Fire/Vite.fs src/Fire/Fire.fsproj tests/Fire.Tests/ViteTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add Vite manifest reader, asset helpers, and path matching"
```

---

### Task 2: Dev Proxy Middleware

**Files:**
- Modify: `src/Fire/Vite.fs` (add dev middleware)
- Modify: `tests/Fire.Tests/ViteTests.fs` (add middleware tests)

**Step 1: Write the failing tests**

Append to `tests/Fire.Tests/ViteTests.fs`:

```fsharp
// --- Dev middleware ---

[<Fact>]
let ``Vite.devMiddleware passes non-Vite requests through`` () = task {
    let inner : Handler = fun _ -> task { return Response.text "from fire" }
    let mw = Vite.devMiddleware 5173
    let handler = mw inner
    let! response = handler (Unchecked.defaultof<Request>)
    response.Body |> should equal (ResponseBody.Text "from fire")
}
```

Note: We cannot easily test the proxy behavior in unit tests (it requires a real Vite server). The middleware test verifies that non-Vite paths pass through correctly. The proxy path is covered by the `shouldProxy` tests above plus integration testing.

**Step 2: Write the middleware**

Add to `src/Fire/Vite.fs` before the high-level API section:

```fsharp
    // --- Dev proxy middleware ---

    let private httpClient = lazy (new Net.Http.HttpClient())

    let devMiddleware (port: int) : Middleware =
        if not (isDevelopment ()) then
            fun next _req -> next _req  // no-op in production
        else
            fun next req ->
                if shouldProxy req.Path then
                    task {
                        try
                            let url = $"http://localhost:{port}{req.Path}"
                            let! proxyResponse = httpClient.Value.GetAsync(url)
                            let! body = proxyResponse.Content.ReadAsStringAsync()
                            let contentType =
                                match proxyResponse.Content.Headers.ContentType with
                                | null -> "application/octet-stream"
                                | ct -> ct.ToString()
                            return
                                Response.text body
                                |> Response.status (int proxyResponse.StatusCode)
                                |> Response.header "Content-Type" contentType
                        with
                        | :? Net.Http.HttpRequestException ->
                            return
                                Response.text "Vite dev server not running. Start it with 'npm run dev'."
                                |> Response.status 502
                    }
                else
                    next req

    /// Dev proxy middleware. Forwards /@vite/*, /src/* etc to Vite dev server.
    /// In production, this is a no-op.
    let dev () : Middleware = devMiddleware defaultPort
    let devWithPort (port: int) : Middleware = devMiddleware port
```

**Step 3: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~ViteTests" --no-restore`
Expected: All 14 tests PASS.

**Step 4: Run full test suite**

Run: `dotnet test tests/Fire.Tests --no-restore`
Expected: All tests PASS.

**Step 5: Commit**

```bash
git add src/Fire/Vite.fs tests/Fire.Tests/ViteTests.fs
git commit -m "feat: add Vite dev proxy middleware"
```

---

### Task 3: Full Suite Verification

**Files:**
- No new files

**Step 1: Run complete F# test suite**

Run: `dotnet test tests/Fire.Tests --no-restore && dotnet test tests/Flame.Tests --no-restore`
Expected: All tests PASS.

**Step 2: Build full solution**

Run: `dotnet build --no-restore`
Expected: Build succeeds with no errors.
