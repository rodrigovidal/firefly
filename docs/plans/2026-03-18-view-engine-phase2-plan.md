# View Engine Phase 2: React Hydration — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add React client component hydration and TanStack Query integration to Fire's view engine.

**Architecture:** `Component.client` helper returns a standard Element with data attrs (no Node DU changes). `QueryCache` collects prefetched data per-request and injects a dehydration script. `@fire/fire-react` npm package hydrates components client-side. TDD with xUnit + FsUnit.

**Tech Stack:** F#, TypeScript, React, TanStack Query, xUnit, FsUnit.Xunit

**Design doc:** `docs/plans/2026-03-18-view-engine-phase2-design.md`

---

### Task 1: Component.client Helper

**Files:**
- Modify: `src/Fire/View/Node.fs` (add Component module at the end)
- Create: `tests/Fire.Tests/ComponentTests.fs`
- Modify: `tests/Fire.Tests/Fire.Tests.fsproj` (add compile entry)

**Step 1: Add ComponentTests.fs to test project**

Insert after `ViewTests.fs` (line 59) in `tests/Fire.Tests/Fire.Tests.fsproj`:

```xml
    <Compile Include="ComponentTests.fs" />
```

**Step 2: Write the failing tests**

File: `tests/Fire.Tests/ComponentTests.fs`

```fsharp
module Fire.Tests.ComponentTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Component.client creates Element with data-fire-component attr`` () =
    let node = Component.client "LikeButton" {| userId = 1 |}
    match node with
    | Element("div", attrs, []) ->
        attrs |> List.exists (fun a ->
            match a with Data("fire-component", "LikeButton") -> true | _ -> false)
        |> should be True
    | _ -> failwith "expected div element with no children"

[<Fact>]
let ``Component.client serializes props as JSON in data-fire-props`` () =
    let node = Component.client "Counter" {| count = 42 |}
    match node with
    | Element("div", attrs, []) ->
        let propsAttr =
            attrs |> List.tryPick (fun a ->
                match a with Data("fire-props", v) -> Some v | _ -> None)
        propsAttr |> Option.isSome |> should be True
        propsAttr.Value |> should haveSubstring "42"
    | _ -> failwith "expected div element"

[<Fact>]
let ``Component.client renders correct HTML via Render.toHtml`` () =
    let html = Component.client "Btn" {| label = "Click" |} |> Render.toHtml
    html |> should haveSubstring "data-fire-component"
    html |> should haveSubstring "Btn"
    html |> should haveSubstring "Click"

[<Fact>]
let ``Component.client composes inside Html tree`` () =
    let html =
        Html.div [
            Html.h1 [ Text "Hello" ]
            Component.client "Widget" {| id = 5 |}
        ]
        |> Render.toHtml
    html |> should haveSubstring "<h1>Hello</h1>"
    html |> should haveSubstring "data-fire-component"
    html |> should haveSubstring "Widget"

[<Fact>]
let ``Component.client with empty props`` () =
    let node = Component.client "Empty" {||}
    match node with
    | Element("div", attrs, []) ->
        attrs |> List.exists (fun a ->
            match a with Data("fire-props", _) -> true | _ -> false)
        |> should be True
    | _ -> failwith "expected div element"
```

**Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~ComponentTests" --no-restore`
Expected: Build failure — `Component` module not defined.

**Step 4: Write Component module**

Append to the end of `src/Fire/View/Node.fs` (after the `Html` type):

```fsharp
module Component =
    let client (name: string) (props: 'T) : Node =
        let json = System.Text.Json.JsonSerializer.Serialize(props)
        Element("div", [ Data("fire-component", name); Data("fire-props", json) ], [])
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~ComponentTests" --no-restore`
Expected: All 5 tests PASS.

**Step 6: Commit**

IMPORTANT: Do NOT add Co-Authored-By lines mentioning Claude. Do NOT mention Claude or AI in commit messages.

```bash
git add src/Fire/View/Node.fs tests/Fire.Tests/ComponentTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add Component.client for React hydration markers"
```

---

### Task 2: QueryCache & Query.prefetch

**Files:**
- Create: `src/Fire/View/Query.fs`
- Create: `tests/Fire.Tests/QueryTests.fs`
- Modify: `src/Fire/Fire.fsproj` (add compile entry after View/Render.fs)
- Modify: `tests/Fire.Tests/Fire.Tests.fsproj` (add compile entry)

**Step 1: Add Query.fs to Fire.fsproj**

Insert after `View/Render.fs` (line 17) in `src/Fire/Fire.fsproj`:

```xml
    <Compile Include="View/Query.fs" />
```

Add `QueryTests.fs` after `ComponentTests.fs` in test project:

```xml
    <Compile Include="QueryTests.fs" />
```

**Step 2: Write the failing tests**

File: `tests/Fire.Tests/QueryTests.fs`

```fsharp
module Fire.Tests.QueryTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``QueryCache starts empty`` () =
    let cache = QueryCache()
    cache.Entries |> should equal List.empty<QueryEntry>

[<Fact>]
let ``QueryCache.Add stores entry`` () =
    let cache = QueryCache()
    cache.Add("user-1", {| name = "Alice" |})
    cache.Entries |> should haveLength 1
    cache.Entries.[0].Key |> should equal "user-1"

[<Fact>]
let ``QueryCache.Add stores multiple entries`` () =
    let cache = QueryCache()
    cache.Add("user-1", {| name = "Alice" |})
    cache.Add("user-2", {| name = "Bob" |})
    cache.Entries |> should haveLength 2

[<Fact>]
let ``DehydrateScript returns Empty when no entries`` () =
    let cache = QueryCache()
    cache.DehydrateScript() |> should equal Empty

[<Fact>]
let ``DehydrateScript returns Raw script with JSON`` () =
    let cache = QueryCache()
    cache.Add("user-1", {| name = "Alice" |})
    let node = cache.DehydrateScript()
    match node with
    | Raw s ->
        s |> should haveSubstring "<script>"
        s |> should haveSubstring "__FIRE_QUERY_STATE__"
        s |> should haveSubstring "user-1"
        s |> should haveSubstring "Alice"
    | _ -> failwith "expected Raw node"

[<Fact>]
let ``DehydrateScript produces valid TanStack Query format`` () =
    let cache = QueryCache()
    cache.Add("key-1", {| x = 1 |})
    match cache.DehydrateScript() with
    | Raw s ->
        s |> should haveSubstring "\"queryKey\""
        s |> should haveSubstring "\"state\""
        s |> should haveSubstring "\"data\""
    | _ -> failwith "expected Raw node"

[<Fact>]
let ``DehydrateScript with multiple entries separates with comma`` () =
    let cache = QueryCache()
    cache.Add("a", {| v = 1 |})
    cache.Add("b", {| v = 2 |})
    match cache.DehydrateScript() with
    | Raw s ->
        s |> should haveSubstring "\"a\""
        s |> should haveSubstring "\"b\""
        // Should be a JSON array with two elements
        s |> should haveSubstring "},{"
    | _ -> failwith "expected Raw node"

[<Fact>]
let ``Query.prefetch executes fetch and stores in cache`` () = task {
    let cache = QueryCache()
    let! result = Query.prefetch "user-1" (fun () -> task { return {| name = "Alice" |} }) cache
    result.name |> should equal "Alice"
    cache.Entries |> should haveLength 1
    cache.Entries.[0].Key |> should equal "user-1"
}

[<Fact>]
let ``Query.prefetch returns the fetched value`` () = task {
    let cache = QueryCache()
    let! user = Query.prefetch "u" (fun () -> task { return {| id = 42 |} }) cache
    user.id |> should equal 42
}

[<Fact>]
let ``Query.prefetch multiple calls accumulate in cache`` () = task {
    let cache = QueryCache()
    let! _ = Query.prefetch "a" (fun () -> task { return 1 }) cache
    let! _ = Query.prefetch "b" (fun () -> task { return 2 }) cache
    cache.Entries |> should haveLength 2
}
```

**Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~QueryTests" --no-restore`
Expected: Build failure — `QueryCache`, `QueryEntry`, `Query` not defined.

**Step 4: Write Query.fs implementation**

File: `src/Fire/View/Query.fs`

```fsharp
namespace Fire

open System.Text.Json

type QueryEntry = { Key: string; Data: obj }

type QueryCache() =
    let entries = System.Collections.Generic.List<QueryEntry>()

    member _.Add(key: string, data: obj) =
        entries.Add({ Key = key; Data = data })

    member _.Entries = entries |> Seq.toList

    member _.DehydrateScript() : Node =
        if entries.Count = 0 then Empty
        else
            let json =
                entries
                |> Seq.map (fun e ->
                    let data = JsonSerializer.Serialize(e.Data)
                    $"""{{ "queryKey": ["{e.Key}"], "state": {{ "data": {data} }} }}""")
                |> String.concat ","
            Raw $"""<script>window.__FIRE_QUERY_STATE__=[{json}]</script>"""

[<RequireQualifiedAccess>]
module Query =
    let prefetch (key: string) (fetch: unit -> System.Threading.Tasks.Task<'T>) (cache: QueryCache) = task {
        let! result = fetch ()
        cache.Add(key, result :> obj)
        return result
    }
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~QueryTests" --no-restore`
Expected: All 10 tests PASS.

**Step 6: Commit**

```bash
git add src/Fire/View/Query.fs src/Fire/Fire.fsproj tests/Fire.Tests/QueryTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add QueryCache and Query.prefetch for TanStack Query dehydration"
```

---

### Task 3: View.withQueryCache

**Files:**
- Modify: `src/Fire/View/View.fs` (add QueryCache field, withQueryCache function, update render)
- Modify: `tests/Fire.Tests/ViewTests.fs` (add new tests, update existing ViewConfig assertions)

**Step 1: Write the failing tests**

Append to `tests/Fire.Tests/ViewTests.fs`:

```fsharp
[<Fact>]
let ``View.page creates ViewConfig with QueryCache None`` () =
    let config = View.page "Home" (Text "hi")
    config.QueryCache |> should equal None

[<Fact>]
let ``View.withQueryCache sets cache`` () =
    let cache = QueryCache()
    let config =
        View.page "Home" (Text "hi")
        |> View.withQueryCache cache
    config.QueryCache |> Option.isSome |> should be True

[<Fact>]
let ``View.render with QueryCache injects dehydration script`` () =
    let cache = QueryCache()
    cache.Add("user-1", {| name = "Alice" |})
    let response =
        View.page "Home" (Html.p [ Text "hi" ])
        |> View.withQueryCache cache
        |> View.withScript "/app.js"
        |> View.render
    match response.Body with
    | ResponseBody.Text body ->
        body |> should haveSubstring "__FIRE_QUERY_STATE__"
        body |> should haveSubstring "Alice"
        // Dehydration script should appear before user scripts
        let idxState = body.IndexOf("__FIRE_QUERY_STATE__")
        let idxApp = body.IndexOf("/app.js")
        idxState |> should be (lessThan idxApp)
    | _ -> failwith "expected Text body"

[<Fact>]
let ``View.render with empty QueryCache does not inject script`` () =
    let cache = QueryCache()
    let response =
        View.page "Home" (Text "hi")
        |> View.withQueryCache cache
        |> View.render
    match response.Body with
    | ResponseBody.Text body ->
        body |> should not' (haveSubstring "__FIRE_QUERY_STATE__")
    | _ -> failwith "expected Text body"

[<Fact>]
let ``View.render with layout and QueryCache appends dehydration to content`` () =
    let cache = QueryCache()
    cache.Add("k", {| v = 1 |})
    let myLayout (title: string) (content: string) =
        $"<html><body>{content}</body></html>"
    let response =
        View.page "Home" (Html.p [ Text "hi" ])
        |> View.withQueryCache cache
        |> View.withLayout myLayout
        |> View.render
    match response.Body with
    | ResponseBody.Text body ->
        body |> should haveSubstring "__FIRE_QUERY_STATE__"
        body |> should haveSubstring "<p>hi</p>"
    | _ -> failwith "expected Text body"
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~ViewTests" --no-restore`
Expected: Build failure — `ViewConfig` does not have `QueryCache` field.

**Step 3: Update View.fs**

Replace the full contents of `src/Fire/View/View.fs`:

```fsharp
namespace Fire

type ViewConfig = {
    Title: string
    Content: Node
    Scripts: string list
    Styles: string list
    Head: Node list
    Layout: (string -> string -> string) option
    QueryCache: QueryCache option
}

[<RequireQualifiedAccess>]
module View =

    let page (title: string) (content: Node) : ViewConfig =
        { Title = title
          Content = content
          Scripts = []
          Styles = []
          Head = []
          Layout = None
          QueryCache = None }

    let withScript (src: string) (config: ViewConfig) : ViewConfig =
        { config with Scripts = config.Scripts @ [ src ] }

    let withStyle (href: string) (config: ViewConfig) : ViewConfig =
        { config with Styles = config.Styles @ [ href ] }

    let withHead (node: Node) (config: ViewConfig) : ViewConfig =
        { config with Head = config.Head @ [ node ] }

    let withLayout (layout: string -> string -> string) (config: ViewConfig) : ViewConfig =
        { config with Layout = Some layout }

    let withQueryCache (cache: QueryCache) (config: ViewConfig) : ViewConfig =
        { config with QueryCache = Some cache }

    let render (config: ViewConfig) : Response =
        let content = Render.toHtml config.Content
        let dehydrationScript =
            match config.QueryCache with
            | Some cache ->
                let script = cache.DehydrateScript()
                if script = Empty then "" else Render.toHtml script
            | None -> ""
        let html =
            match config.Layout with
            | Some layout -> layout config.Title (content + dehydrationScript)
            | None ->
                let sb = System.Text.StringBuilder()
                sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">") |> ignore
                sb.Append($"<title>{System.Net.WebUtility.HtmlEncode config.Title}</title>") |> ignore
                for href in config.Styles do
                    sb.Append($"""<link rel="stylesheet" href="{System.Net.WebUtility.HtmlEncode href}">""") |> ignore
                for node in config.Head do
                    sb.Append(Render.toHtml node) |> ignore
                sb.Append("</head><body>") |> ignore
                sb.Append(content) |> ignore
                sb.Append(dehydrationScript) |> ignore
                for src in config.Scripts do
                    sb.Append($"""<script src="{System.Net.WebUtility.HtmlEncode src}"></script>""") |> ignore
                sb.Append("</body></html>") |> ignore
                sb.ToString()
        Response.html html
```

Note: `View/View.fs` must be compiled AFTER `View/Query.fs` in the fsproj. Currently `View/View.fs` is at line 47 (before App.fs). `View/Query.fs` was added at line 18 (after Render.fs). This is correct — Query.fs compiles before View.fs.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests --filter "FullyQualifiedName~ViewTests" --no-restore`
Expected: All 14 tests PASS (9 existing + 5 new).

**Step 5: Run full test suite**

Run: `dotnet test tests/Fire.Tests --no-restore`
Expected: All tests PASS.

**Step 6: Commit**

```bash
git add src/Fire/View/View.fs tests/Fire.Tests/ViewTests.fs
git commit -m "feat: add View.withQueryCache — dehydration script injection"
```

---

### Task 4: @fire/fire-react npm Package

**Files:**
- Create: `packages/fire-react/package.json`
- Create: `packages/fire-react/tsconfig.json`
- Create: `packages/fire-react/src/index.tsx`

**Step 1: Create package.json**

File: `packages/fire-react/package.json`

```json
{
  "name": "@fire/fire-react",
  "version": "0.1.0",
  "description": "React hydration for Fire server-rendered components",
  "main": "dist/index.js",
  "types": "dist/index.d.ts",
  "files": ["dist"],
  "scripts": {
    "build": "tsc"
  },
  "peerDependencies": {
    "react": ">=18.0.0",
    "react-dom": ">=18.0.0",
    "@tanstack/react-query": ">=5.0.0"
  },
  "devDependencies": {
    "typescript": "^5.0.0",
    "@types/react": "^18.0.0",
    "@types/react-dom": "^18.0.0",
    "react": "^18.0.0",
    "react-dom": "^18.0.0",
    "@tanstack/react-query": "^5.0.0"
  },
  "license": "MIT",
  "repository": {
    "type": "git",
    "url": "https://github.com/rodrigovidal/fire",
    "directory": "packages/fire-react"
  }
}
```

**Step 2: Create tsconfig.json**

File: `packages/fire-react/tsconfig.json`

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "moduleResolution": "node",
    "jsx": "react-jsx",
    "declaration": true,
    "outDir": "dist",
    "rootDir": "src",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true
  },
  "include": ["src"]
}
```

**Step 3: Create src/index.tsx**

File: `packages/fire-react/src/index.tsx`

```tsx
import { hydrateRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider, HydrationBoundary } from '@tanstack/react-query'

type ComponentMap = Record<string, React.ComponentType<any>>

export function hydrateFireApp(components: ComponentMap) {
    const queryClient = new QueryClient()
    const dehydratedState = (window as any).__FIRE_QUERY_STATE__
    const markers = document.querySelectorAll('[data-fire-component]')

    markers.forEach(marker => {
        const name = marker.getAttribute('data-fire-component')!
        const props = JSON.parse(marker.getAttribute('data-fire-props') || '{}')
        const Component = components[name]
        if (!Component) {
            console.warn(`Fire: unknown component "${name}"`)
            return
        }

        hydrateRoot(marker,
            <QueryClientProvider client={queryClient}>
                <HydrationBoundary state={dehydratedState}>
                    <Component {...props} />
                </HydrationBoundary>
            </QueryClientProvider>
        )
    })
}
```

**Step 4: Install deps and verify build**

Run: `cd packages/fire-react && npm install && npm run build`
Expected: TypeScript compiles to `dist/index.js` and `dist/index.d.ts`.

**Step 5: Commit**

```bash
git add packages/fire-react/
git commit -m "feat: add @fire/fire-react npm package for client-side hydration"
```

Note: Add `packages/fire-react/node_modules/` to `.gitignore` if not already covered.

---

### Task 5: Full Suite Verification

**Files:**
- No new files

**Step 1: Run the complete F# test suite**

Run: `dotnet test tests/Fire.Tests --no-restore && dotnet test tests/Flame.Tests --no-restore`
Expected: All tests PASS.

**Step 2: Build the full solution**

Run: `dotnet build --no-restore`
Expected: Build succeeds with no errors.

**Step 3: Verify npm package builds**

Run: `cd packages/fire-react && npm run build`
Expected: Compiles without errors.
