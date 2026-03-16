# Tier 2 Features Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add logging middleware, static file serving, content negotiation, redirect, and caching helpers to Fire.

**Architecture:** Two new modules (Log, Static), extensions to Request and Response. All follow existing Fire patterns.

**Tech Stack:** F# 10, .NET 10, xUnit + FsUnit. Microsoft.Extensions.Logging for ILogger bridge.

---

### Task 1: Response helpers (redirect, etag, cacheControl)

**Files:**
- Modify: `src/Fire/Response.fs`
- Create: `tests/Fire.Tests/ResponseHelpersTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/ResponseHelpersTests.fs`:

```fsharp
module Fire.Tests.ResponseHelpersTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Response.redirect sets Location header and status`` () =
    let r = Response.ok |> Response.redirect "/login" 302
    r.Status |> should equal 302
    r.Headers |> should contain ("Location", "/login")

[<Fact>]
let ``Response.redirect 301 for permanent`` () =
    let r = Response.ok |> Response.redirect "/new" 301
    r.Status |> should equal 301
    r.Headers |> should contain ("Location", "/new")

[<Fact>]
let ``Response.etag sets ETag header`` () =
    let r = Response.ok |> Response.etag "\"abc123\""
    r.Headers |> should contain ("ETag", "\"abc123\"")

[<Fact>]
let ``Response.cacheControl sets Cache-Control header`` () =
    let r = Response.ok |> Response.cacheControl "public, max-age=3600"
    r.Headers |> should contain ("Cache-Control", "public, max-age=3600")

[<Fact>]
let ``Caching headers compose with other builders`` () =
    let r =
        Response.json {| data = 1 |}
        |> Response.etag "\"v1\""
        |> Response.cacheControl "no-cache"
        |> Response.status 200
    r.Headers |> should contain ("ETag", "\"v1\"")
    r.Headers |> should contain ("Cache-Control", "no-cache")
```

Add `ResponseHelpersTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj` after `CookieTests.fs`.

**Step 2: Implement in Response.fs**

Add after `unauthorized` in the Response module:

```fsharp
    let redirect url code r =
        { r with Status = code; Headers = ("Location", url) :: r.Headers }

    let etag tag r = r |> header "ETag" tag

    let cacheControl value r = r |> header "Cache-Control" value
```

**Step 3: Run tests, verify all pass**

Run: `dotnet test tests/Fire.Tests`

**Step 4: Commit**

```bash
git add src/Fire/Response.fs tests/Fire.Tests/ResponseHelpersTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add redirect, etag, and cacheControl response helpers"
```

---

### Task 2: Request.Accepts and ContentType

**Files:**
- Modify: `src/Fire/Request.fs`
- Create: `tests/Fire.Tests/ContentNegotiationTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/ContentNegotiationTests.fs`:

```fsharp
module Fire.Tests.ContentNegotiationTests

open System.Collections.Generic
open System.IO
open System.Text
open Microsoft.AspNetCore.Http
open Xunit
open FsUnit.Xunit
open Fire

let makeCtx (method': string) (path: string) (headers: (string * string) list) (contentType: string option) =
    let ctx = DefaultHttpContext()
    ctx.Request.Method <- method'
    ctx.Request.Path <- PathString(path)
    for (k, v) in headers do
        ctx.Request.Headers.[k] <- Microsoft.Extensions.Primitives.StringValues(v)
    match contentType with
    | Some ct -> ctx.Request.ContentType <- ct
    | None -> ()
    ctx :> HttpContext

let emptyParams = Dictionary<string, string>() :> IReadOnlyDictionary<_, _>

[<Fact>]
let ``Accepts returns true when Accept header contains media type`` () =
    let ctx = makeCtx "GET" "/" ["Accept", "text/html, application/json"] None
    let req = Request(ctx, emptyParams)
    req.Accepts "application/json" |> should be True

[<Fact>]
let ``Accepts returns false when Accept header does not contain media type`` () =
    let ctx = makeCtx "GET" "/" ["Accept", "text/html"] None
    let req = Request(ctx, emptyParams)
    req.Accepts "application/json" |> should be False

[<Fact>]
let ``Accepts returns false when no Accept header`` () =
    let ctx = makeCtx "GET" "/" [] None
    let req = Request(ctx, emptyParams)
    req.Accepts "application/json" |> should be False

[<Fact>]
let ``ContentType returns Some for present content type`` () =
    let ctx = makeCtx "POST" "/" [] (Some "application/json")
    let req = Request(ctx, emptyParams)
    req.ContentType |> should equal (Some "application/json")

[<Fact>]
let ``ContentType returns None when not set`` () =
    let ctx = makeCtx "GET" "/" [] None
    let req = Request(ctx, emptyParams)
    req.ContentType |> should equal None
```

Add `ContentNegotiationTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj` after `RequestExtensionsTests.fs`.

**Step 2: Implement on Request**

Add to `src/Fire/Request.fs` after the `Form()` member:

```fsharp
    member _.Accepts (mediaType: string) : bool =
        match ctx.Request.Headers.TryGetValue("Accept") with
        | true, values -> values.ToString().Contains(mediaType)
        | false, _ -> false

    member _.ContentType : string option =
        match ctx.Request.ContentType with
        | null | "" -> None
        | ct -> Some ct
```

**Step 3: Run tests, verify all pass**

Run: `dotnet test tests/Fire.Tests`

**Step 4: Commit**

```bash
git add src/Fire/Request.fs tests/Fire.Tests/ContentNegotiationTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add Accepts and ContentType to Request"
```

---

### Task 3: Logging Middleware

**Files:**
- Create: `src/Fire/Log.fs`
- Create: `tests/Fire.Tests/LogTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/LogTests.fs`:

```fsharp
module Fire.Tests.LogTests

open System
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Log.withOutput calls output function with correct entry`` () = task {
    let mutable captured = None
    let logMw = Log.withOutput (fun entry -> captured <- Some entry)

    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.json {| ok = true |} })

    let config = App.defaults |> App.port 0 |> App.middleware logMw
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    let! _ = client.GetAsync($"http://127.0.0.1:{port}/test")

    captured |> Option.isSome |> should be True
    let entry = captured.Value
    entry.Method |> should equal "GET"
    entry.Path |> should equal "/test"
    entry.Status |> should equal 200
    entry.Duration.TotalMilliseconds |> should be (greaterThan 0.0)

    do! stop()
}

[<Fact>]
let ``Log.withOutput captures 404 status`` () = task {
    let mutable captured = None
    let logMw = Log.withOutput (fun entry -> captured <- Some entry)

    let routes = Route.start
    let config = App.defaults |> App.port 0 |> App.middleware logMw
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    let! _ = client.GetAsync($"http://127.0.0.1:{port}/missing")

    captured |> Option.isSome |> should be True
    captured.Value.Status |> should equal 404

    do! stop()
}

[<Fact>]
let ``Log.toConsole does not throw`` () = task {
    let routes =
        Route.start
        |> Route.get "/ok" (fun _ -> task { return Response.ok })

    let config = App.defaults |> App.port 0 |> App.middleware Log.toConsole
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    let! response = client.GetAsync($"http://127.0.0.1:{port}/ok")
    response.StatusCode |> should equal System.Net.HttpStatusCode.OK

    do! stop()
}
```

Add `LogTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj` after `CorsTests.fs`.

**Step 2: Implement Log.fs**

Create `src/Fire/Log.fs`:

```fsharp
namespace Fire

open System
open System.Diagnostics

type LogEntry = {
    Method: string
    Path: string
    Status: int
    Duration: TimeSpan
}

[<RequireQualifiedAccess>]
module Log =

    let withOutput (output: LogEntry -> unit) : Middleware =
        fun next req -> task {
            let sw = Stopwatch.StartNew()
            let! response = next req
            sw.Stop()
            output {
                Method = req.Method
                Path = req.Path
                Status = response.Status
                Duration = sw.Elapsed
            }
            return response
        }

    let toConsole : Middleware =
        withOutput (fun e ->
            Console.WriteLine($"{e.Method} {e.Path} -> {e.Status} ({e.Duration.TotalMilliseconds:F1}ms)"))

    let toLogger (logger: Microsoft.Extensions.Logging.ILogger) : Middleware =
        withOutput (fun e ->
            logger.LogInformation(
                "{Method} {Path} -> {Status} ({Duration:F1}ms)",
                e.Method, e.Path, e.Status, e.Duration.TotalMilliseconds))
```

Add `Log.fs` to `src/Fire/Fire.fsproj` after `Route.fs` (before `Cors.fs`):

```xml
<Compile Include="Log.fs" />
```

**Step 3: Run tests, verify all pass**

Run: `dotnet test tests/Fire.Tests`

**Step 4: Commit**

```bash
git add src/Fire/Log.fs src/Fire/Fire.fsproj tests/Fire.Tests/LogTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add logging middleware with withOutput, toConsole, and toLogger"
```

---

### Task 4: Static File Serving

**Files:**
- Create: `src/Fire/Static.fs`
- Create: `tests/Fire.Tests/StaticTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/StaticTests.fs`:

```fsharp
module Fire.Tests.StaticTests

open System.IO
open System.Net
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Fire

let setupTestDir () =
    let dir = Path.Combine(Path.GetTempPath(), "fire-static-test-" + System.Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    Directory.CreateDirectory(Path.Combine(dir, "css")) |> ignore
    File.WriteAllText(Path.Combine(dir, "index.html"), "<h1>Hello</h1>")
    File.WriteAllText(Path.Combine(dir, "css", "app.css"), "body { color: red; }")
    File.WriteAllText(Path.Combine(dir, "data.json"), """{"ok":true}""")
    dir

[<Fact>]
let ``Static.serve returns file content`` () = task {
    let dir = setupTestDir ()
    try
        let routes =
            Route.start
            |> Route.get "/static/*path" (Static.serve dir)
        let config = App.defaults |> App.port 0
        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient()

        let! response = client.GetAsync($"http://127.0.0.1:{port}/static/index.html")
        let! body = response.Content.ReadAsStringAsync()

        response.StatusCode |> should equal HttpStatusCode.OK
        body |> should haveSubstring "<h1>Hello</h1>"

        do! stop()
    finally
        Directory.Delete(dir, true)
}

[<Fact>]
let ``Static.serve sets correct content type`` () = task {
    let dir = setupTestDir ()
    try
        let routes =
            Route.start
            |> Route.get "/static/*path" (Static.serve dir)
        let config = App.defaults |> App.port 0
        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient()

        let! htmlResp = client.GetAsync($"http://127.0.0.1:{port}/static/index.html")
        htmlResp.Content.Headers.ContentType.MediaType |> should equal "text/html"

        let! cssResp = client.GetAsync($"http://127.0.0.1:{port}/static/css/app.css")
        cssResp.Content.Headers.ContentType.MediaType |> should equal "text/css"

        let! jsonResp = client.GetAsync($"http://127.0.0.1:{port}/static/data.json")
        jsonResp.Content.Headers.ContentType.MediaType |> should equal "application/json"

        do! stop()
    finally
        Directory.Delete(dir, true)
}

[<Fact>]
let ``Static.serve returns 404 for missing file`` () = task {
    let dir = setupTestDir ()
    try
        let routes =
            Route.start
            |> Route.get "/static/*path" (Static.serve dir)
        let config = App.defaults |> App.port 0
        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient()

        let! response = client.GetAsync($"http://127.0.0.1:{port}/static/nope.txt")
        response.StatusCode |> should equal HttpStatusCode.NotFound

        do! stop()
    finally
        Directory.Delete(dir, true)
}

[<Fact>]
let ``Static.serve prevents directory traversal`` () = task {
    let dir = setupTestDir ()
    try
        let routes =
            Route.start
            |> Route.get "/static/*path" (Static.serve dir)
        let config = App.defaults |> App.port 0
        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient()

        let! response = client.GetAsync($"http://127.0.0.1:{port}/static/../../../etc/passwd")
        response.StatusCode |> should equal HttpStatusCode.NotFound

        do! stop()
    finally
        Directory.Delete(dir, true)
}

[<Fact>]
let ``Static.serve handles nested directories`` () = task {
    let dir = setupTestDir ()
    try
        let routes =
            Route.start
            |> Route.get "/static/*path" (Static.serve dir)
        let config = App.defaults |> App.port 0
        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient()

        let! response = client.GetAsync($"http://127.0.0.1:{port}/static/css/app.css")
        let! body = response.Content.ReadAsStringAsync()
        body |> should equal "body { color: red; }"

        do! stop()
    finally
        Directory.Delete(dir, true)
}
```

Add `StaticTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj` after `LogTests.fs`.

**Step 2: Implement Static.fs**

Create `src/Fire/Static.fs`:

```fsharp
namespace Fire

open System.IO

[<RequireQualifiedAccess>]
module Static =

    let private mimeTypes = dict [
        ".html", "text/html"
        ".htm", "text/html"
        ".css", "text/css"
        ".js", "application/javascript"
        ".json", "application/json"
        ".png", "image/png"
        ".jpg", "image/jpeg"
        ".jpeg", "image/jpeg"
        ".gif", "image/gif"
        ".svg", "image/svg+xml"
        ".ico", "image/x-icon"
        ".woff", "font/woff"
        ".woff2", "font/woff2"
        ".ttf", "font/ttf"
        ".txt", "text/plain"
        ".xml", "application/xml"
        ".pdf", "application/pdf"
    ]

    let private getContentType (path: string) =
        let ext = Path.GetExtension(path).ToLowerInvariant()
        match mimeTypes.TryGetValue(ext) with
        | true, ct -> ct
        | false, _ -> "application/octet-stream"

    let serve (rootDir: string) : Handler =
        let absRoot = Path.GetFullPath(rootDir)
        fun req -> task {
            let filePath = req.Params.["path"]
            let fullPath = Path.GetFullPath(Path.Combine(absRoot, filePath))
            if not (fullPath.StartsWith(absRoot)) then
                return Response.notFound
            elif File.Exists(fullPath) then
                let stream = File.OpenRead(fullPath)
                return
                    Response.stream stream
                    |> Response.header "Content-Type" (getContentType filePath)
            else
                return Response.notFound
        }
```

Add `Static.fs` to `src/Fire/Fire.fsproj` after `Log.fs` (before `Cors.fs`):

```xml
<Compile Include="Static.fs" />
```

**Step 3: Run tests, verify all pass**

Run: `dotnet test tests/Fire.Tests`

**Step 4: Commit**

```bash
git add src/Fire/Static.fs src/Fire/Fire.fsproj tests/Fire.Tests/StaticTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add static file serving with MIME types and traversal protection"
```

---

### Task 5: Tier 2 Integration Smoke Test

**Files:**
- Create: `tests/Fire.Tests/Tier2SmokeTests.fs`

**Step 1: Write smoke test**

Create `tests/Fire.Tests/Tier2SmokeTests.fs`:

```fsharp
module Fire.Tests.Tier2SmokeTests

open System.IO
open System.Net
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Tier 2 integration smoke test`` () = task {
    // Set up static files
    let dir = Path.Combine(Path.GetTempPath(), "fire-tier2-" + System.Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    File.WriteAllText(Path.Combine(dir, "hello.txt"), "world")

    try
        let mutable logEntries = []
        let logMw = Log.withOutput (fun e -> logEntries <- e :: logEntries)

        let routes =
            Route.start
            |> Route.get "/api/data" (fun req -> task {
                if req.Accepts "application/json" then
                    return
                        Response.json {| items = [1;2;3] |}
                        |> Response.etag "\"v1\""
                        |> Response.cacheControl "public, max-age=60"
                else
                    return Response.text "items: 1, 2, 3"
            })
            |> Route.get "/go" (fun _ -> task {
                return Response.ok |> Response.redirect "/api/data" 302
            })
            |> Route.get "/static/*path" (Static.serve dir)

        let config =
            App.defaults
            |> App.port 0
            |> App.middleware logMw

        let! (port, stop) = App.runTest routes config CancellationToken.None
        use client = new HttpClient(new HttpClientHandler(AllowAutoRedirect = false))
        let base' = $"http://127.0.0.1:{port}"

        // Content negotiation + caching
        let req1 = new HttpRequestMessage(HttpMethod.Get, $"{base'}/api/data")
        req1.Headers.Add("Accept", "application/json")
        let! r1 = client.SendAsync(req1)
        r1.StatusCode |> should equal HttpStatusCode.OK
        r1.Headers.GetValues("ETag") |> Seq.head |> should equal "\"v1\""
        r1.Headers.GetValues("Cache-Control") |> Seq.head |> should equal "public, max-age=60"

        // Redirect
        let! r2 = client.GetAsync($"{base'}/go")
        r2.StatusCode |> should equal HttpStatusCode.Redirect
        r2.Headers.GetValues("Location") |> Seq.head |> should equal "/api/data"

        // Static files
        let! r3 = client.GetAsync($"{base'}/static/hello.txt")
        let! b3 = r3.Content.ReadAsStringAsync()
        b3 |> should equal "world"

        // Logging captured all requests
        logEntries |> List.length |> should be (greaterThanOrEqualTo 3)

        do! stop()
    finally
        Directory.Delete(dir, true)
}
```

Add `Tier2SmokeTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj` after `Tier1SmokeTests.fs`.

**Step 2: Run all tests, commit**

```bash
git add tests/Fire.Tests/Tier2SmokeTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "test: add Tier 2 integration smoke test"
```

---

### Final fsproj Compile Orders

**src/Fire/Fire.fsproj:**
```xml
<Compile Include="Request.fs" />
<Compile Include="Response.fs" />
<Compile Include="Cookie.fs" />
<Compile Include="Types.fs" />
<Compile Include="Trie.fs" />
<Compile Include="Route.fs" />
<Compile Include="Log.fs" />
<Compile Include="Static.fs" />
<Compile Include="Cors.fs" />
<Compile Include="App.fs" />
```

**tests/Fire.Tests/Fire.Tests.fsproj:**
```xml
<Compile Include="RequestTests.fs" />
<Compile Include="RequestExtensionsTests.fs" />
<Compile Include="ContentNegotiationTests.fs" />
<Compile Include="ResponseTests.fs" />
<Compile Include="ResponseHelpersTests.fs" />
<Compile Include="CookieTests.fs" />
<Compile Include="TrieTests.fs" />
<Compile Include="WildcardTests.fs" />
<Compile Include="RouteTests.fs" />
<Compile Include="LogTests.fs" />
<Compile Include="StaticTests.fs" />
<Compile Include="AppTests.fs" />
<Compile Include="CorsTests.fs" />
<Compile Include="SmokeTests.fs" />
<Compile Include="Tier1SmokeTests.fs" />
<Compile Include="Tier2SmokeTests.fs" />
```
