# Tier 3 Features Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add graceful shutdown, request timeout, rate limiting, and OpenAPI generation to Fire.

**Architecture:** ShutdownTimeout on FireConfig, three new middleware/utility modules. All follow existing Fire patterns.

**Tech Stack:** F# 10, .NET 10, xUnit + FsUnit, System.Text.Json for OpenAPI JSON output.

---

### Task 1: Graceful Shutdown

**Files:**
- Modify: `src/Fire/App.fs`
- Create: `tests/Fire.Tests/ShutdownTests.fs`

**Step 1: Write failing test**

Create `tests/Fire.Tests/ShutdownTests.fs`:

```fsharp
module Fire.Tests.ShutdownTests

open System
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``App.shutdownTimeout sets config`` () =
    let config =
        App.defaults
        |> App.shutdownTimeout (TimeSpan.FromSeconds 10.0)
    config.ShutdownTimeout |> should equal (Some (TimeSpan.FromSeconds 10.0))

[<Fact>]
let ``Server stops gracefully after stop is called`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.port 0 |> App.shutdownTimeout (TimeSpan.FromSeconds 5.0)
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    let! response = client.GetAsync($"http://127.0.0.1:{port}/test")
    let! body = response.Content.ReadAsStringAsync()
    body |> should equal "ok"

    do! stop()
    // After stop, server should not accept connections
    let! ex = Assert.ThrowsAnyAsync<Exception>(fun () -> task {
        let! _ = client.GetAsync($"http://127.0.0.1:{port}/test")
        return ()
    })
    ex |> should not' (be Null)
}
```

Add `ShutdownTests.fs` to test fsproj after `AppTests.fs`.

**Step 2: Implement**

In `src/Fire/App.fs`:

1. Add `ShutdownTimeout: TimeSpan option` to FireConfig
2. Add `ShutdownTimeout = None` to defaults
3. Add `let shutdownTimeout ts config = { config with ShutdownTimeout = Some ts }`
4. In both `run` and `runTest`, configure HostOptions:

```fsharp
match config.ShutdownTimeout with
| Some ts ->
    builder.Services.Configure<Microsoft.Extensions.Hosting.HostOptions>(fun (opts: Microsoft.Extensions.Hosting.HostOptions) ->
        opts.ShutdownTimeout <- ts
    ) |> ignore
| None -> ()
```

**Step 3: Run tests, commit**

```bash
git commit -m "feat: add graceful shutdown with App.shutdownTimeout"
```

---

### Task 2: Request Timeout Middleware

**Files:**
- Create: `src/Fire/Timeout.fs`
- Create: `tests/Fire.Tests/TimeoutTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/TimeoutTests.fs`:

```fsharp
module Fire.Tests.TimeoutTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Timeout.after returns 504 when handler exceeds timeout`` () = task {
    let routes =
        Route.start
        |> Route.middleware (Timeout.after (TimeSpan.FromMilliseconds 100.0))
        |> Route.get "/slow" (fun _ -> task {
            do! Task.Delay(5000)
            return Response.text "done"
        })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    let! response = client.GetAsync($"http://127.0.0.1:{port}/slow")
    response.StatusCode |> should equal HttpStatusCode.GatewayTimeout

    do! stop()
}

[<Fact>]
let ``Timeout.after passes through when handler completes in time`` () = task {
    let routes =
        Route.start
        |> Route.middleware (Timeout.after (TimeSpan.FromSeconds 5.0))
        |> Route.get "/fast" (fun _ -> task { return Response.text "quick" })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    let! response = client.GetAsync($"http://127.0.0.1:{port}/fast")
    let! body = response.Content.ReadAsStringAsync()
    response.StatusCode |> should equal HttpStatusCode.OK
    body |> should equal "quick"

    do! stop()
}
```

Add `TimeoutTests.fs` to test fsproj after `ShutdownTests.fs`.

**Step 2: Implement Timeout.fs**

Create `src/Fire/Timeout.fs`:

```fsharp
namespace Fire

open System
open System.Threading
open System.Threading.Tasks

[<RequireQualifiedAccess>]
module Timeout =

    let after (timeout: TimeSpan) : Middleware =
        fun next req -> task {
            use cts = new CancellationTokenSource(timeout)
            let handlerTask = next req
            let delayTask = Task.Delay(Timeout.Infinite, cts.Token)

            let! completed = Task.WhenAny(handlerTask, Task.Delay(int timeout.TotalMilliseconds))
            if Object.ReferenceEquals(completed, handlerTask) then
                return! handlerTask
            else
                cts.Cancel()
                return { Status = 504; Headers = []; Body = Empty }
        }
```

Note: The `Task.WhenAny` approach races the handler against a delay. If the delay wins, return 504. This works even if the handler doesn't respect cancellation.

Add `Timeout.fs` to `src/Fire/Fire.fsproj` after `Static.fs`.

**Step 3: Run tests, commit**

```bash
git commit -m "feat: add request timeout middleware"
```

---

### Task 3: Rate Limiting

**Files:**
- Create: `src/Fire/RateLimit.fs`
- Create: `tests/Fire.Tests/RateLimitTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/RateLimitTests.fs`:

```fsharp
module Fire.Tests.RateLimitTests

open System
open System.Net
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``RateLimit allows requests within limit`` () = task {
    let routes =
        Route.start
        |> Route.middleware (RateLimit.fixedWindow 5 (TimeSpan.FromMinutes 1.0) (fun _ -> "test-key"))
        |> Route.get "/api" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    for _ in 1..5 do
        let! response = client.GetAsync($"http://127.0.0.1:{port}/api")
        response.StatusCode |> should equal HttpStatusCode.OK

    do! stop()
}

[<Fact>]
let ``RateLimit returns 429 when limit exceeded`` () = task {
    let routes =
        Route.start
        |> Route.middleware (RateLimit.fixedWindow 3 (TimeSpan.FromMinutes 1.0) (fun _ -> "test-key-2"))
        |> Route.get "/api" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    for _ in 1..3 do
        let! response = client.GetAsync($"http://127.0.0.1:{port}/api")
        response.StatusCode |> should equal HttpStatusCode.OK

    let! response = client.GetAsync($"http://127.0.0.1:{port}/api")
    response.StatusCode |> should equal HttpStatusCode.TooManyRequests

    do! stop()
}

[<Fact>]
let ``RateLimit returns Retry-After header on 429`` () = task {
    let routes =
        Route.start
        |> Route.middleware (RateLimit.fixedWindow 1 (TimeSpan.FromSeconds 60.0) (fun _ -> "test-key-3"))
        |> Route.get "/api" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    let! _ = client.GetAsync($"http://127.0.0.1:{port}/api")
    let! response = client.GetAsync($"http://127.0.0.1:{port}/api")

    response.StatusCode |> should equal HttpStatusCode.TooManyRequests
    response.Headers.Contains("Retry-After") |> should be True

    do! stop()
}

[<Fact>]
let ``RateLimit isolates keys`` () = task {
    let routes =
        Route.start
        |> Route.middleware (RateLimit.fixedWindow 1 (TimeSpan.FromMinutes 1.0)
            (fun req -> req.Header "X-Key" |> Option.defaultValue "default"))
        |> Route.get "/api" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    // First key exhausts limit
    let req1 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api")
    req1.Headers.Add("X-Key", "user-a")
    let! r1 = client.SendAsync(req1)
    r1.StatusCode |> should equal HttpStatusCode.OK

    let req2 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api")
    req2.Headers.Add("X-Key", "user-a")
    let! r2 = client.SendAsync(req2)
    r2.StatusCode |> should equal HttpStatusCode.TooManyRequests

    // Second key still has quota
    let req3 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api")
    req3.Headers.Add("X-Key", "user-b")
    let! r3 = client.SendAsync(req3)
    r3.StatusCode |> should equal HttpStatusCode.OK

    do! stop()
}
```

Add `RateLimitTests.fs` to test fsproj after `TimeoutTests.fs`.

**Step 2: Implement RateLimit.fs**

Create `src/Fire/RateLimit.fs`:

```fsharp
namespace Fire

open System
open System.Collections.Concurrent

[<RequireQualifiedAccess>]
module RateLimit =

    let private counters = ConcurrentDictionary<string, int * DateTime>()

    let fixedWindow (maxRequests: int) (window: TimeSpan) (keyFunc: Request -> string) : Middleware =
        fun next req -> task {
            let key = keyFunc req
            let now = DateTime.UtcNow
            let mutable blocked = false
            let mutable retryAfter = 0

            counters.AddOrUpdate(key,
                (fun _ -> (1, now)),
                (fun _ (c, ws) ->
                    if now - ws >= window then (1, now)
                    else
                        if c >= maxRequests then
                            blocked <- true
                            retryAfter <- int (window - (now - ws)).TotalSeconds
                            (c + 1, ws)
                        else
                            (c + 1, ws)))
            |> ignore

            if blocked then
                return
                    { Status = 429; Headers = []; Body = Empty }
                    |> Response.header "Retry-After" (string retryAfter)
            else
                return! next req
        }

    let byIp : Request -> string =
        fun req ->
            match req.Raw.Connection.RemoteIpAddress with
            | null -> "unknown"
            | ip -> ip.ToString()
```

Add `RateLimit.fs` to `src/Fire/Fire.fsproj` after `Timeout.fs`.

**Step 3: Run tests, commit**

```bash
git commit -m "feat: add fixed-window rate limiting middleware"
```

---

### Task 4: OpenAPI Generation

**Files:**
- Create: `src/Fire/OpenApi.fs`
- Create: `tests/Fire.Tests/OpenApiTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/OpenApiTests.fs`:

```fsharp
module Fire.Tests.OpenApiTests

open System.Text.Json
open Xunit
open FsUnit.Xunit
open Fire

let dummyHandler : Handler = fun _ -> task { return Response.ok }

[<Fact>]
let ``OpenApi.generate produces valid JSON`` () =
    let routes =
        Route.start
        |> Route.get "/health" dummyHandler
    let json = OpenApi.generate "Test API" "1.0" routes
    let doc = JsonDocument.Parse(json)
    doc.RootElement.GetProperty("openapi").GetString() |> should equal "3.0.0"
    doc.RootElement.GetProperty("info").GetProperty("title").GetString() |> should equal "Test API"
    doc.RootElement.GetProperty("info").GetProperty("version").GetString() |> should equal "1.0"

[<Fact>]
let ``OpenApi.generate includes paths and methods`` () =
    let routes =
        Route.start
        |> Route.get "/users" dummyHandler
        |> Route.post "/users" dummyHandler
        |> Route.get "/users/:id" dummyHandler
    let json = OpenApi.generate "API" "1.0" routes
    let doc = JsonDocument.Parse(json)
    let paths = doc.RootElement.GetProperty("paths")
    paths.TryGetProperty("/users") |> fst |> should be True
    paths.TryGetProperty("/users/{id}") |> fst |> should be True
    // /users should have get and post
    let users = paths.GetProperty("/users")
    users.TryGetProperty("get") |> fst |> should be True
    users.TryGetProperty("post") |> fst |> should be True

[<Fact>]
let ``OpenApi.generate extracts path parameters`` () =
    let routes =
        Route.start
        |> Route.get "/users/:userId/posts/:postId" dummyHandler
    let json = OpenApi.generate "API" "1.0" routes
    let doc = JsonDocument.Parse(json)
    let op = doc.RootElement.GetProperty("paths").GetProperty("/users/{userId}/posts/{postId}").GetProperty("get")
    let parameters = op.GetProperty("parameters")
    parameters.GetArrayLength() |> should equal 2

[<Fact>]
let ``OpenApi.generate converts wildcard to parameter`` () =
    let routes =
        Route.start
        |> Route.get "/static/*path" dummyHandler
    let json = OpenApi.generate "API" "1.0" routes
    let doc = JsonDocument.Parse(json)
    let paths = doc.RootElement.GetProperty("paths")
    paths.TryGetProperty("/static/{path}") |> fst |> should be True

[<Fact>]
let ``OpenApi.handler serves spec as JSON`` () = task {
    let routes =
        Route.start
        |> Route.get "/users" dummyHandler
    let handler = OpenApi.handler "API" "1.0" routes
    let! response = handler (Unchecked.defaultof<Request>)
    match response.Body with
    | Json bytes ->
        let json = System.Text.Encoding.UTF8.GetString(bytes)
        json |> should haveSubstring "openapi"
    | _ -> failwith "expected JSON body"
}
```

Add `OpenApiTests.fs` to test fsproj after `RateLimitTests.fs`.

**Step 2: Implement OpenApi.fs**

Create `src/Fire/OpenApi.fs`:

```fsharp
namespace Fire

open System.Text.Json

[<RequireQualifiedAccess>]
module OpenApi =

    let private convertPattern (pattern: string) =
        // Convert :param to {param} and *wildcard to {wildcard}
        pattern.Split('/')
        |> Array.map (fun seg ->
            if seg.Length > 0 && seg.[0] = ':' then "{" + seg.Substring(1) + "}"
            elif seg.Length > 0 && seg.[0] = '*' then "{" + seg.Substring(1) + "}"
            else seg)
        |> fun parts -> System.String.Join("/", parts)

    let private extractParams (pattern: string) =
        pattern.Split('/')
        |> Array.choose (fun seg ->
            if seg.Length > 0 && (seg.[0] = ':' || seg.[0] = '*') then
                Some (seg.Substring(1))
            else None)
        |> Array.toList

    let generate (title: string) (version: string) (routes: RouteTable) : string =
        // Group routes by pattern
        let grouped =
            routes.Routes
            |> List.groupBy (fun r -> convertPattern r.Pattern)

        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writer.WriteString("openapi", "3.0.0")

        writer.WriteStartObject("info")
        writer.WriteString("title", title)
        writer.WriteString("version", version)
        writer.WriteEndObject()

        writer.WriteStartObject("paths")
        for (path, entries) in grouped do
            writer.WriteStartObject(path)
            for entry in entries do
                let method' = entry.Method.ToLowerInvariant()
                writer.WriteStartObject(method')

                let paramNames = extractParams entry.Pattern
                if paramNames.Length > 0 then
                    writer.WriteStartArray("parameters")
                    for name in paramNames do
                        writer.WriteStartObject()
                        writer.WriteString("name", name)
                        writer.WriteString("in", "path")
                        writer.WriteBoolean("required", true)
                        writer.WriteStartObject("schema")
                        writer.WriteString("type", "string")
                        writer.WriteEndObject()
                        writer.WriteEndObject()
                    writer.WriteEndArray()

                writer.WriteEndObject()
            writer.WriteEndObject()
        writer.WriteEndObject()

        writer.WriteEndObject()
        writer.Flush()

        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    let handler (title: string) (version: string) (routes: RouteTable) : Handler =
        let spec = generate title version routes
        fun _ -> task {
            return Response.json spec
        }
```

Wait — `Response.json spec` would double-serialize since `spec` is already a JSON string. Instead:

```fsharp
    let handler (title: string) (version: string) (routes: RouteTable) : Handler =
        let specBytes = System.Text.Encoding.UTF8.GetBytes(generate title version routes)
        fun _ -> task {
            return { Status = 200; Headers = [("Content-Type", "application/json")]; Body = Json specBytes }
        }
```

Add `OpenApi.fs` to `src/Fire/Fire.fsproj` after `RateLimit.fs`.

**Step 3: Run tests, commit**

```bash
git commit -m "feat: add OpenAPI spec generation from RouteTable"
```

---

### Task 5: Tier 3 Integration Smoke Test

**Files:**
- Create: `tests/Fire.Tests/Tier3SmokeTests.fs`

**Step 1: Write smoke test**

Create `tests/Fire.Tests/Tier3SmokeTests.fs`:

```fsharp
module Fire.Tests.Tier3SmokeTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Tier 3 integration smoke test`` () = task {
    let routes =
        Route.start
        |> Route.get "/fast" (fun _ -> task { return Response.text "ok" })
        |> Route.get "/slow" (fun _ -> task {
            do! Task.Delay(5000)
            return Response.text "done"
        })
        |> Route.get "/users/:id" (fun req -> task {
            return Response.json {| id = req.Params.["id"] |}
        })

    let config =
        App.defaults
        |> App.port 0
        |> App.shutdownTimeout (TimeSpan.FromSeconds 5.0)
        |> App.middleware (Timeout.after (TimeSpan.FromMilliseconds 200.0))
        |> App.middleware (RateLimit.fixedWindow 10 (TimeSpan.FromMinutes 1.0) (fun _ -> "smoke-test"))

    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let base' = $"http://127.0.0.1:{port}"

    // Fast request succeeds
    let! r1 = client.GetAsync($"{base'}/fast")
    r1.StatusCode |> should equal HttpStatusCode.OK

    // Slow request times out
    let! r2 = client.GetAsync($"{base'}/slow")
    r2.StatusCode |> should equal HttpStatusCode.GatewayTimeout

    // OpenAPI spec
    let spec = OpenApi.generate "Smoke API" "1.0" routes
    let doc = JsonDocument.Parse(spec)
    doc.RootElement.GetProperty("paths").EnumerateObject() |> Seq.length |> should be (greaterThanOrEqualTo 3)

    do! stop()
}
```

Add `Tier3SmokeTests.fs` to test fsproj after `Tier2SmokeTests.fs`.

**Step 2: Run all tests, commit**

```bash
git commit -m "test: add Tier 3 integration smoke test"
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
<Compile Include="Timeout.fs" />
<Compile Include="RateLimit.fs" />
<Compile Include="OpenApi.fs" />
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
<Compile Include="AppTests.fs" />
<Compile Include="ShutdownTests.fs" />
<Compile Include="TimeoutTests.fs" />
<Compile Include="RateLimitTests.fs" />
<Compile Include="OpenApiTests.fs" />
<Compile Include="LogTests.fs" />
<Compile Include="StaticTests.fs" />
<Compile Include="CorsTests.fs" />
<Compile Include="SmokeTests.fs" />
<Compile Include="Tier1SmokeTests.fs" />
<Compile Include="Tier2SmokeTests.fs" />
<Compile Include="Tier3SmokeTests.fs" />
```
