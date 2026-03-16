# Tier 1 Features Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add body parsing, query helpers, wildcard routes, response cookies, and CORS middleware to Fire.

**Architecture:** Extends existing Request/Response/Trie types with new members and modules. Cookie and Cors are new files. Wildcard is a new leaf type in the trie.

**Tech Stack:** F# 10, .NET 10, xUnit + FsUnit, same as existing.

---

### Task 1: Request.QueryParam, Text(), Form()

**Files:**
- Modify: `src/Fire/Request.fs`
- Create: `tests/Fire.Tests/RequestExtensionsTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/RequestExtensionsTests.fs`:

```fsharp
module Fire.Tests.RequestExtensionsTests

open System.Collections.Generic
open System.IO
open System.Text
open Microsoft.AspNetCore.Http
open Xunit
open FsUnit.Xunit
open Fire

let makeHttpContext (method': string) (path: string) (query: string) (headers: (string * string) list) (body: string option) (contentType: string option) =
    let ctx = DefaultHttpContext()
    ctx.Request.Method <- method'
    ctx.Request.Path <- PathString(path)
    ctx.Request.QueryString <- QueryString(query)
    for (k, v) in headers do
        ctx.Request.Headers.[k] <- Microsoft.Extensions.Primitives.StringValues(v)
    match body with
    | Some b ->
        let bytes = Encoding.UTF8.GetBytes(b)
        ctx.Request.Body <- new MemoryStream(bytes)
    | None -> ()
    match contentType with
    | Some ct -> ctx.Request.ContentType <- ct
    | None -> ()
    ctx :> HttpContext

let emptyParams = Dictionary<string, string>() :> IReadOnlyDictionary<_, _>

[<Fact>]
let ``QueryParam returns Some for existing key`` () =
    let ctx = makeHttpContext "GET" "/search" "?q=fire" [] None None
    let req = Request(ctx, emptyParams)
    req.QueryParam "q" |> should equal (Some "fire")

[<Fact>]
let ``QueryParam returns None for missing key`` () =
    let ctx = makeHttpContext "GET" "/search" "" [] None None
    let req = Request(ctx, emptyParams)
    req.QueryParam "q" |> should equal None

[<Fact>]
let ``Text reads body as string`` () = task {
    let ctx = makeHttpContext "POST" "/" "" [] (Some "hello world") None
    let req = Request(ctx, emptyParams)
    let! text = req.Text()
    text |> should equal "hello world"
}

[<Fact>]
let ``Text returns empty string for empty body`` () = task {
    let ctx = makeHttpContext "POST" "/" "" [] None None
    let req = Request(ctx, emptyParams)
    let! text = req.Text()
    text |> should equal ""
}

[<Fact>]
let ``Form parses url-encoded body`` () = task {
    let body = "name=fire&version=1"
    let ctx = makeHttpContext "POST" "/" "" [] (Some body) (Some "application/x-www-form-urlencoded")
    let req = Request(ctx, emptyParams)
    let! form = req.Form()
    form.["name"] |> should equal "fire"
    form.["version"] |> should equal "1"
}
```

Add `RequestExtensionsTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj` after `RequestTests.fs`:

```xml
<Compile Include="RequestExtensionsTests.fs" />
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests`
Expected: FAIL — `QueryParam`, `Text`, `Form` not found on Request.

**Step 3: Implement on Request**

Add to `src/Fire/Request.fs` after the `Json<'T>()` member:

```fsharp
    member _.QueryParam (name: string) : string option =
        match ctx.Request.Query.TryGetValue(name) with
        | true, values -> Some (values.ToString())
        | false, _ -> None

    member _.Text() : Task<string> =
        let body = ctx.Request.Body
        task {
            use reader = new StreamReader(body, Encoding.UTF8, leaveOpen = true)
            return! reader.ReadToEndAsync()
        }

    member _.Form() : Task<IReadOnlyDictionary<string, string>> =
        let request = ctx.Request
        task {
            let! form = request.ReadFormAsync()
            let d = Dictionary<string, string>(form.Count)
            for kvp in form do
                d.[kvp.Key] <- kvp.Value.ToString()
            return d :> IReadOnlyDictionary<_, _>
        }
```

Also add `open System.Text` at the top of Request.fs (for `Encoding`).

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests`
Expected: All tests PASS.

**Step 5: Commit**

```bash
git add src/Fire/Request.fs tests/Fire.Tests/RequestExtensionsTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add QueryParam, Text(), and Form() to Request"
```

---

### Task 2: Wildcard Routes in Trie

**Files:**
- Modify: `src/Fire/Trie.fs`
- Create: `tests/Fire.Tests/WildcardTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/WildcardTests.fs`:

```fsharp
module Fire.Tests.WildcardTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Wildcard captures remaining path segments`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/static/*path" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/static/css/app.css" trie
    result |> Option.isSome |> should be True
    let (_, ps) = result.Value
    ps.["path"] |> should equal "css/app.css"

[<Fact>]
let ``Wildcard captures single segment`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/files/*name" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/files/readme.txt" trie
    result |> Option.isSome |> should be True
    let (_, ps) = result.Value
    ps.["name"] |> should equal "readme.txt"

[<Fact>]
let ``Wildcard captures deeply nested path`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/assets/*path" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/assets/js/lib/vue/dist/vue.min.js" trie
    result |> Option.isSome |> should be True
    let (_, ps) = result.Value
    ps.["path"] |> should equal "js/lib/vue/dist/vue.min.js"

[<Fact>]
let ``Static takes priority over wildcard`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/files/special" [] (fun _ -> task { return Response.text "static" })
        |> Trie.add "GET" "/files/*path" [] (fun _ -> task { return Response.text "wildcard" })
    let (h, _) = (Trie.lookup "GET" "/files/special" trie).Value
    let r = h (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    r.Body |> should equal (Text "static")

[<Fact>]
let ``Param takes priority over wildcard`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/users/:id" [] (fun _ -> task { return Response.text "param" })
        |> Trie.add "GET" "/users/*rest" [] (fun _ -> task { return Response.text "wildcard" })
    let (h, _) = (Trie.lookup "GET" "/users/42" trie).Value
    let r = h (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    r.Body |> should equal (Text "param")

[<Fact>]
let ``Wildcard returns None when no segments to capture`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/static/*path" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/static" trie
    result |> Option.isNone |> should be True

[<Fact>]
let ``Wildcard distinguishes methods`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/api/*rest" [] (fun _ -> task { return Response.text "get" })
        |> Trie.add "POST" "/api/*rest" [] (fun _ -> task { return Response.text "post" })
    let (hGet, _) = (Trie.lookup "GET" "/api/foo/bar" trie).Value
    let (hPost, _) = (Trie.lookup "POST" "/api/foo/bar" trie).Value
    let rGet = hGet (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    let rPost = hPost (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    rGet.Body |> should equal (Text "get")
    rPost.Body |> should equal (Text "post")
```

Add `WildcardTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj` after `TrieTests.fs`:

```xml
<Compile Include="WildcardTests.fs" />
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests`
Expected: FAIL — wildcard segments not handled.

**Step 3: Implement wildcard in Trie**

Modify `src/Fire/Trie.fs`:

1. Add `WildcardChild` to TrieNode:

```fsharp
type TrieNode = {
    StaticChildren: Map<string, TrieNode>
    ParamChild: (string * TrieNode) option
    WildcardChild: (string * Map<string, Handler>) option
    Handlers: Map<string, Handler>
}
```

2. Update `emptyNode`:

```fsharp
let private emptyNode () = {
    StaticChildren = Map.empty
    ParamChild = None
    WildcardChild = None
    Handlers = Map.empty
}
```

3. In `add`, handle `*` segments. When `seg.[0] = '*'`, store the composed handler in `WildcardChild`:

```fsharp
let rec insert (node: TrieNode) (idx: int) =
    if idx >= segments.Length then
        { node with Handlers = node.Handlers |> Map.add method' composed }
    else
        let seg = segments.[idx]
        if seg.[0] = '*' then
            let paramName = seg.Substring(1)
            let handlers =
                match node.WildcardChild with
                | Some (_, existing) -> existing
                | None -> Map.empty
            { node with WildcardChild = Some (paramName, handlers |> Map.add method' composed) }
        elif seg.[0] = ':' then
            // ... existing param logic
        else
            // ... existing static logic
```

4. In `lookup`, after static and param fail, try wildcard. The wildcard captures all remaining segments joined with `/`:

```fsharp
and tryWildcard (node: TrieNode) (idx: int) (segments: string[]) (ps: (string * string) list) =
    match node.WildcardChild with
    | Some (paramName, handlers) ->
        match handlers |> Map.tryFind method' with
        | Some h ->
            let captured = System.String.Join("/", segments, idx, segments.Length - idx)
            let dict = Dictionary<string, string>()
            for (k, v) in ((paramName, captured) :: ps) do dict.[k] <- v
            Some (h, dict :> IReadOnlyDictionary<_, _>)
        | None -> None
    | None -> None
```

Update the `search` function to call `tryWildcard` when both static and param fail:

```fsharp
| None ->
    match tryParam node seg idx ps with
    | Some _ as result -> result
    | None -> tryWildcard node idx segments ps
```

And similarly when static succeeds but deeper search fails.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests`
Expected: All tests PASS (existing + 7 new).

**Step 5: Commit**

```bash
git add src/Fire/Trie.fs tests/Fire.Tests/WildcardTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add wildcard route support (*path) to trie"
```

---

### Task 3: Response Cookies

**Files:**
- Create: `src/Fire/Cookie.fs`
- Modify: `src/Fire/Response.fs`
- Create: `tests/Fire.Tests/CookieTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/CookieTests.fs`:

```fsharp
module Fire.Tests.CookieTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Response.cookie sets bare Set-Cookie header`` () =
    let r = Response.ok |> Response.cookie "session" "abc123"
    r.Headers |> should contain ("Set-Cookie", "session=abc123")

[<Fact>]
let ``Response.cookieWith sets full Set-Cookie header`` () =
    let r =
        Response.ok
        |> Response.cookieWith "token" "xyz" (
            Cookie.defaults
            |> Cookie.httpOnly
            |> Cookie.secure
            |> Cookie.maxAge 3600
            |> Cookie.path "/"
            |> Cookie.sameSite "Strict"
        )
    let cookieHeader = r.Headers |> List.find (fun (k, _) -> k = "Set-Cookie") |> snd
    cookieHeader |> should haveSubstring "token=xyz"
    cookieHeader |> should haveSubstring "Max-Age=3600"
    cookieHeader |> should haveSubstring "Path=/"
    cookieHeader |> should haveSubstring "HttpOnly"
    cookieHeader |> should haveSubstring "Secure"
    cookieHeader |> should haveSubstring "SameSite=Strict"

[<Fact>]
let ``Response.cookieWith with defaults sets bare cookie`` () =
    let r = Response.ok |> Response.cookieWith "name" "val" Cookie.defaults
    let cookieHeader = r.Headers |> List.find (fun (k, _) -> k = "Set-Cookie") |> snd
    cookieHeader |> should equal "name=val"

[<Fact>]
let ``Multiple cookies produce multiple Set-Cookie headers`` () =
    let r =
        Response.ok
        |> Response.cookie "a" "1"
        |> Response.cookie "b" "2"
    let cookies = r.Headers |> List.filter (fun (k, _) -> k = "Set-Cookie")
    cookies |> List.length |> should equal 2

[<Fact>]
let ``Cookie.domain sets Domain attribute`` () =
    let r =
        Response.ok
        |> Response.cookieWith "x" "y" (Cookie.defaults |> Cookie.domain "example.com")
    let cookieHeader = r.Headers |> List.find (fun (k, _) -> k = "Set-Cookie") |> snd
    cookieHeader |> should haveSubstring "Domain=example.com"
```

Add `CookieTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj` after `ResponseTests.fs`:

```xml
<Compile Include="CookieTests.fs" />
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests`
Expected: FAIL — `Cookie` module not found.

**Step 3: Create Cookie.fs**

Create `src/Fire/Cookie.fs`:

```fsharp
namespace Fire

type CookieOptions = {
    MaxAge: int option
    Path: string option
    Domain: string option
    Secure: bool
    HttpOnly: bool
    SameSite: string option
}

[<RequireQualifiedAccess>]
module Cookie =
    let defaults = {
        MaxAge = None
        Path = None
        Domain = None
        Secure = false
        HttpOnly = false
        SameSite = None
    }

    let maxAge seconds opts = { opts with MaxAge = Some seconds }
    let path p opts = { opts with Path = Some p }
    let domain d opts = { opts with Domain = Some d }
    let secure opts = { opts with Secure = true }
    let httpOnly opts = { opts with HttpOnly = true }
    let sameSite s opts = { opts with SameSite = Some s }

    let internal buildHeaderValue (name: string) (value: string) (opts: CookieOptions) =
        let parts = System.Collections.Generic.List<string>()
        parts.Add($"{name}={value}")
        match opts.MaxAge with Some s -> parts.Add($"Max-Age={s}") | None -> ()
        match opts.Path with Some p -> parts.Add($"Path={p}") | None -> ()
        match opts.Domain with Some d -> parts.Add($"Domain={d}") | None -> ()
        if opts.Secure then parts.Add("Secure")
        if opts.HttpOnly then parts.Add("HttpOnly")
        match opts.SameSite with Some s -> parts.Add($"SameSite={s}") | None -> ()
        System.String.Join("; ", parts)
```

Add `Cookie.fs` to `src/Fire/Fire.fsproj` after `Response.fs`:

```xml
<Compile Include="Cookie.fs" />
```

**Step 4: Add cookie and cookieWith to Response module**

Add to `src/Fire/Response.fs` after the `header` function:

```fsharp
    let cookie name value r =
        r |> header "Set-Cookie" $"{name}={value}"

    let cookieWith name value (opts: CookieOptions) r =
        r |> header "Set-Cookie" (Cookie.buildHeaderValue name value opts)
```

NOTE: Response.fs must come before Cookie.fs in the fsproj, but `cookieWith` references `Cookie.buildHeaderValue` which is in Cookie.fs. This creates a circular dependency.

**Fix:** Move the `buildHeaderValue` logic into Response.fs as a private helper, or move `cookie`/`cookieWith` into a separate file after Cookie.fs. The simplest approach: put `cookie` and `cookieWith` as module functions in Cookie.fs instead of Response module:

Actually, the cleanest fix: keep `cookie` in Response.fs (it's just a header call, no Cookie dependency) and put `cookieWith` in Cookie.fs as `Cookie.set`:

Revised approach — add to `src/Fire/Cookie.fs` at the end of the Cookie module:

```fsharp
    let set name value (opts: CookieOptions) (r: Response) =
        { r with Headers = ("Set-Cookie", buildHeaderValue name value opts) :: r.Headers }
```

And in `src/Fire/Response.fs`, only add the bare `cookie`:

```fsharp
    let cookie name value r =
        r |> header "Set-Cookie" $"{name}={value}"
```

Update the tests to use `Cookie.set` instead of `Response.cookieWith`:

```fsharp
// Change Response.cookieWith to Cookie.set in tests
|> Cookie.set "token" "xyz" (Cookie.defaults |> Cookie.httpOnly |> ...)
```

Wait — this breaks the pipe-friendly pattern where everything goes through `Response`. Let me reconsider.

**Final approach:** Cookie.fs comes after Response.fs. Cookie.fs can reference Response types. Add `cookieWith` as a function in the Cookie module that returns a modified Response:

In Cookie.fs:

```fsharp
    let set name value (opts: CookieOptions) (r: Response) : Response =
        let headerValue = buildHeaderValue name value opts
        { r with Headers = ("Set-Cookie", headerValue) :: r.Headers }
```

Usage:
```fsharp
Response.ok
|> Response.cookie "simple" "val"          // bare cookie via Response module
|> Cookie.set "token" "xyz" (              // configured cookie via Cookie module
    Cookie.defaults |> Cookie.httpOnly |> Cookie.secure
)
```

Update test expectations to use `Cookie.set` instead of `Response.cookieWith`.

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests`
Expected: All tests PASS.

**Step 6: Commit**

```bash
git add src/Fire/Cookie.fs src/Fire/Response.fs src/Fire/Fire.fsproj tests/Fire.Tests/CookieTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add response cookies with Cookie.set builder"
```

---

### Task 4: CORS Middleware

**Files:**
- Create: `src/Fire/Cors.fs`
- Create: `tests/Fire.Tests/CorsTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/CorsTests.fs`:

```fsharp
module Fire.Tests.CorsTests

open System.Net
open System.Net.Http
open System.Threading
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Cors.allowAll adds wildcard origin header`` () = task {
    let routes =
        Route.start
        |> Route.middleware Cors.allowAll
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    let! response = client.GetAsync($"http://127.0.0.1:{port}/test")
    response.Headers.GetValues("Access-Control-Allow-Origin") |> Seq.head |> should equal "*"

    do! stop()
}

[<Fact>]
let ``Cors.allowAll handles preflight OPTIONS`` () = task {
    let routes =
        Route.start
        |> Route.middleware Cors.allowAll
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    let request = new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{port}/test")
    request.Headers.Add("Origin", "http://example.com")
    request.Headers.Add("Access-Control-Request-Method", "POST")
    let! response = client.SendAsync(request)

    response.StatusCode |> should equal HttpStatusCode.NoContent
    response.Headers.GetValues("Access-Control-Allow-Origin") |> Seq.head |> should equal "*"
    response.Headers.GetValues("Access-Control-Allow-Methods") |> Seq.isEmpty |> should be False

    do! stop()
}

[<Fact>]
let ``Cors.build with specific origins echoes matching origin`` () = task {
    let cors =
        Cors.defaults
        |> Cors.origins ["http://example.com"; "http://other.com"]
        |> Cors.build
    let routes =
        Route.start
        |> Route.middleware cors
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    let request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/test")
    request.Headers.Add("Origin", "http://example.com")
    let! response = client.SendAsync(request)

    response.Headers.GetValues("Access-Control-Allow-Origin") |> Seq.head |> should equal "http://example.com"

    do! stop()
}

[<Fact>]
let ``Cors.build rejects non-matching origin`` () = task {
    let cors =
        Cors.defaults
        |> Cors.origins ["http://allowed.com"]
        |> Cors.build
    let routes =
        Route.start
        |> Route.middleware cors
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    let request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/test")
    request.Headers.Add("Origin", "http://evil.com")
    let! response = client.SendAsync(request)

    response.Headers.Contains("Access-Control-Allow-Origin") |> should be False

    do! stop()
}

[<Fact>]
let ``Cors.build with maxAge sets Max-Age on preflight`` () = task {
    let cors =
        Cors.defaults
        |> Cors.maxAge 3600
        |> Cors.build
    let routes =
        Route.start
        |> Route.middleware cors
        |> Route.get "/test" (fun _ -> task { return Response.ok })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()

    let request = new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{port}/test")
    request.Headers.Add("Origin", "http://example.com")
    request.Headers.Add("Access-Control-Request-Method", "GET")
    let! response = client.SendAsync(request)

    response.Headers.GetValues("Access-Control-Max-Age") |> Seq.head |> should equal "3600"

    do! stop()
}
```

Add `CorsTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj` after `AppTests.fs`:

```xml
<Compile Include="CorsTests.fs" />
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests`
Expected: FAIL — `Cors` module not found.

**Step 3: Create Cors.fs**

Create `src/Fire/Cors.fs`:

```fsharp
namespace Fire

type CorsConfig = {
    Origins: string list
    Methods: string list
    Headers: string list
    MaxAge: int option
}

[<RequireQualifiedAccess>]
module Cors =

    let defaults = { Origins = []; Methods = []; Headers = []; MaxAge = None }

    let origins o config = { config with Origins = o }
    let methods m config = { config with Methods = m }
    let headers h config = { config with Headers = h }
    let maxAge s config = { config with MaxAge = Some s }

    let private defaultMethods = "GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS"

    let build (config: CorsConfig) : Middleware =
        fun next req ->
            let origin = req.Header "Origin"
            let isPreflight = req.Method = "OPTIONS"

            let allowedOrigin =
                match config.Origins with
                | [] -> Some "*"
                | origins ->
                    match origin with
                    | Some o when origins |> List.contains o -> Some o
                    | _ -> None

            match allowedOrigin with
            | None ->
                // Origin not allowed — call next without CORS headers
                next req
            | Some allowOrigin ->
                if isPreflight then
                    // Preflight: return 204 with CORS headers, don't call next
                    task {
                        let methodsValue =
                            match config.Methods with
                            | [] -> defaultMethods
                            | m -> System.String.Join(", ", m)
                        let headersValue =
                            match config.Headers with
                            | [] -> "*"
                            | h -> System.String.Join(", ", h)
                        let mutable r =
                            { Status = 204; Headers = []; Body = Empty }
                            |> Response.header "Access-Control-Allow-Origin" allowOrigin
                            |> Response.header "Access-Control-Allow-Methods" methodsValue
                            |> Response.header "Access-Control-Allow-Headers" headersValue
                        match config.MaxAge with
                        | Some age ->
                            r <- r |> Response.header "Access-Control-Max-Age" (string age)
                        | None -> ()
                        return r
                    }
                else
                    // Normal request: call next, add origin header
                    task {
                        let! response = next req
                        return response |> Response.header "Access-Control-Allow-Origin" allowOrigin
                    }

    let allowAll : Middleware = defaults |> build
```

Add `Cors.fs` to `src/Fire/Fire.fsproj` after `Route.fs` (before `App.fs`):

```xml
<Compile Include="Cors.fs" />
```

IMPORTANT: Cors.fs needs the `Middleware`, `Handler` types from Types.fs and `Response` from Response.fs. The fsproj order must be: Request.fs, Response.fs, Cookie.fs, Types.fs, Trie.fs, Route.fs, Cors.fs, App.fs.

**Step 4: Handle CORS preflight in App.fs**

The CORS middleware returns a Response for OPTIONS preflight requests, but the trie won't have an OPTIONS handler registered. The middleware intercepts before the handler is called — but only if the trie matches. We need to make sure OPTIONS requests reach the middleware.

**Fix:** In `App.fs` `handleRequest`, when `Trie.lookup` returns `None` for an OPTIONS request, still run the middleware chain with a fallback 404 handler. Actually, the simpler fix: the CORS middleware is applied per-route via `Route.middleware`. If the GET route is `/test`, an OPTIONS to `/test` won't match because there's no OPTIONS handler.

**The real fix:** Add a special case in `handleRequest` — for OPTIONS requests, if the trie has no OPTIONS handler but does have other handlers for that path, still run the middleware chain. Or simpler: make CORS middleware registration also register OPTIONS on the same paths.

**Simplest fix:** In the trie lookup, when method is OPTIONS and no match, try matching with GET (or any method) to find the node and run its middleware. Actually this is getting complex.

**Pragmatic fix:** Change `Trie.lookup` to also accept a `matchAnyMethod` flag. When OPTIONS doesn't match, try to find any handler at that path. OR: the CORS middleware can be applied at the App level (before trie routing) as a request interceptor.

**Best approach for now:** Add an `App.middleware` function that runs before routing. This is a global middleware that wraps the entire request pipeline:

In `src/Fire/App.fs`, add to FireConfig:

```fsharp
type FireConfig = {
    Port: int
    Host: string
    OnError: (exn -> Request -> Task<Response>) option
    NotFound: (Request -> Task<Response>) option
    Middlewares: Middleware list
}
```

Add `let middleware mw config = { config with Middlewares = config.Middlewares @ [mw] }` to App module. Defaults: `Middlewares = []`.

In `handleRequest`, wrap the trie dispatch with the global middleware chain.

Actually, let's keep it even simpler. The CORS middleware is meant to be used with `Route.middleware`. For it to handle OPTIONS preflight, we just need to also register an OPTIONS route wherever CORS is applied.

**Simplest real fix:** In `Cors.build`, make the middleware check `req.Method = "OPTIONS"` and return immediately with CORS headers — it never needs to hit the trie for OPTIONS. But the middleware only runs if the trie matches a route.

OK, the cleanest solution: **Add global middleware support to App.** This is a small, useful addition that solves CORS and other cross-cutting concerns:

Add to `FireConfig`:
```fsharp
Middlewares: Middleware list
```

Add to App module:
```fsharp
let middleware mw config = { config with Middlewares = config.Middlewares @ [mw] }
```

In `handleRequest`, before trie routing, run global middlewares. For OPTIONS preflight, the CORS middleware will intercept and return 204 before the trie is consulted.

Wait — the Handler type is `Request -> Task<Response>`, and middlewares wrap handlers. We need a "base handler" that does the trie routing, then wrap it with global middlewares.

In `App.run` / `App.runTest`, compose the global middleware chain around the trie dispatch:

```fsharp
let private makeHandler (trie: TrieNode) (config: FireConfig) : HttpContext -> Task =
    let dispatch : Handler = fun req ->
        // trie lookup + response writing happens here
        ...

    let composed =
        config.Middlewares
        |> List.foldBack (fun mw h -> mw h) <| dispatch

    fun ctx -> task {
        let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
        let! response = composed req
        do! writeResponse ctx response
    }
```

Hmm, this changes the architecture more than I'd like. The issue is `handleRequest` currently both dispatches AND writes the response. We'd need to refactor so the global middleware wraps the dispatch-only part, and writing happens at the very end.

Let me simplify: **For Task 4, use CORS at the App config level. Add `App.middleware` that registers a global handler wrapping the entire pipeline.** The implementation:

In `handleRequest`, refactor to separate "dispatch to get Response" from "write Response":

```fsharp
let private dispatchRequest (trie: TrieNode) (config: FireConfig) (ctx: HttpContext) : Task<Response> = task {
    let path = ctx.Request.Path.Value
    let method' = ctx.Request.Method
    match Trie.lookup method' path trie with
    | Some (handler, ps) ->
        let req = Request(ctx, ps)
        return! handler req
    | None ->
        match config.NotFound with
        | Some nfHandler ->
            let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
            return! nfHandler req
        | None ->
            return { Status = 404; Headers = []; Body = Empty }
}

let private handleRequest (trie: TrieNode) (config: FireConfig) (ctx: HttpContext) = task {
    let baseHandler : Handler = fun req -> dispatchRequest trie config req.Raw
    let composed = List.foldBack (fun (mw: Middleware) (h: Handler) -> mw h) config.Middlewares baseHandler
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    try
        let! response = composed req
        do! writeResponse ctx response
    with ex ->
        match config.OnError with
        | Some errorHandler ->
            try
                let! response = errorHandler ex req
                do! writeResponse ctx response
            with _ ->
                ctx.Response.StatusCode <- 500
        | None ->
            ctx.Response.StatusCode <- 500
}
```

Wait — this changes how route-level middleware and params work. The `baseHandler` creates a Request with empty params, but the trie dispatch inside creates a new Request with actual params. The route-level middlewares already have the correct Request with params (they're pre-composed in the trie). Only the global middleware sees the "no params" request.

This is actually fine for CORS — it only looks at headers, not params.

But there's a subtlety: the `dispatchRequest` calls `handler req`, but `handler` is the pre-composed route handler (with route-level middleware). The `req` passed to it has empty params. The route handler will then see empty params.

**Fix:** `dispatchRequest` must create the Request with the actual params from the trie lookup and pass that to the handler:

```fsharp
let private dispatchRequest (trie: TrieNode) (config: FireConfig) (ctx: HttpContext) : Task<Response> = task {
    let path = ctx.Request.Path.Value
    let method' = ctx.Request.Method
    match Trie.lookup method' path trie with
    | Some (handler, ps) ->
        let req = Request(ctx, ps)
        return! handler req
    | None ->
        match config.NotFound with
        | Some nfHandler ->
            let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
            return! nfHandler req
        | None ->
            return { Status = 404; Headers = []; Body = Empty }
}
```

And the global middleware wrapper:

```fsharp
let private handleRequest (trie: TrieNode) (config: FireConfig) (ctx: HttpContext) = task {
    let baseHandler : Handler = fun _req ->
        dispatchRequest trie config ctx  // ignores _req, uses ctx directly

    let composed = List.foldBack (fun (mw: Middleware) (h: Handler) -> mw h) config.Middlewares baseHandler

    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    try
        let! response = composed req
        do! writeResponse ctx response
    with ex ->
        match config.OnError with
        | Some errorHandler ->
            try
                let! response = errorHandler ex req
                do! writeResponse ctx response
            with _ ->
                ctx.Response.StatusCode <- 500
        | None ->
            ctx.Response.StatusCode <- 500
}
```

This works because:
- Global middleware sees a Request with empty params (fine for CORS, logging, etc.)
- `baseHandler` ignores the Request passed by global middleware and dispatches via trie using the raw HttpContext
- The trie dispatch creates a proper Request with params for route-level middleware and handlers

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests`
Expected: All tests PASS.

**Step 6: Commit**

```bash
git add src/Fire/Cors.fs src/Fire/App.fs src/Fire/Fire.fsproj tests/Fire.Tests/CorsTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add CORS middleware with Cors.allowAll and Cors.build"
```

---

### Task 5: Tier 1 Integration Smoke Test

**Files:**
- Create: `tests/Fire.Tests/Tier1SmokeTests.fs`

**Step 1: Write smoke test exercising all Tier 1 features**

Create `tests/Fire.Tests/Tier1SmokeTests.fs`:

```fsharp
module Fire.Tests.Tier1SmokeTests

open System.Net
open System.Net.Http
open System.Text
open System.Threading
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Tier 1 integration smoke test`` () = task {
    let routes =
        Route.start
        |> Route.get "/" (fun _ -> task { return Response.text "Fire" })
        |> Route.get "/search" (fun req -> task {
            let q = req.QueryParam "q" |> Option.defaultValue "none"
            return Response.text q
        })
        |> Route.post "/echo" (fun req -> task {
            let! body = req.Text()
            return Response.text body
        })
        |> Route.get "/static/*path" (fun req -> task {
            return Response.text req.Params.["path"]
        })
        |> Route.get "/cookie" (fun _ -> task {
            return
                Response.ok
                |> Response.cookie "simple" "val"
                |> Cookie.set "secure" "tok" (
                    Cookie.defaults |> Cookie.httpOnly |> Cookie.secure |> Cookie.path "/"
                )
        })

    let config =
        App.defaults
        |> App.port 0
        |> App.middleware Cors.allowAll

    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let base' = $"http://127.0.0.1:{port}"

    // QueryParam
    let! r1 = client.GetAsync($"{base'}/search?q=fire")
    let! b1 = r1.Content.ReadAsStringAsync()
    b1 |> should equal "fire"

    // Text body
    let! r2 = client.PostAsync($"{base'}/echo", new StringContent("hello", Encoding.UTF8))
    let! b2 = r2.Content.ReadAsStringAsync()
    b2 |> should equal "hello"

    // Wildcard route
    let! r3 = client.GetAsync($"{base'}/static/css/app.css")
    let! b3 = r3.Content.ReadAsStringAsync()
    b3 |> should equal "css/app.css"

    // Cookies
    let! r4 = client.GetAsync($"{base'}/cookie")
    let cookies = r4.Headers.GetValues("Set-Cookie") |> Seq.toList
    cookies |> List.length |> should equal 2

    // CORS
    r1.Headers.GetValues("Access-Control-Allow-Origin") |> Seq.head |> should equal "*"

    // CORS preflight
    let preflight = new HttpRequestMessage(HttpMethod.Options, $"{base'}/test")
    preflight.Headers.Add("Origin", "http://example.com")
    preflight.Headers.Add("Access-Control-Request-Method", "GET")
    let! r5 = client.SendAsync(preflight)
    r5.StatusCode |> should equal HttpStatusCode.NoContent

    do! stop()
}
```

Add `Tier1SmokeTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj` after `SmokeTests.fs`:

```xml
<Compile Include="Tier1SmokeTests.fs" />
```

**Step 2: Run all tests**

Run: `dotnet test tests/Fire.Tests`
Expected: All tests PASS.

**Step 3: Commit**

```bash
git add tests/Fire.Tests/Tier1SmokeTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "test: add Tier 1 integration smoke test"
```

---

### F# fsproj Compile Order (Final)

`src/Fire/Fire.fsproj`:
```xml
<Compile Include="Request.fs" />
<Compile Include="Response.fs" />
<Compile Include="Cookie.fs" />
<Compile Include="Types.fs" />
<Compile Include="Trie.fs" />
<Compile Include="Route.fs" />
<Compile Include="Cors.fs" />
<Compile Include="App.fs" />
```

`tests/Fire.Tests/Fire.Tests.fsproj`:
```xml
<Compile Include="RequestTests.fs" />
<Compile Include="RequestExtensionsTests.fs" />
<Compile Include="ResponseTests.fs" />
<Compile Include="CookieTests.fs" />
<Compile Include="TrieTests.fs" />
<Compile Include="WildcardTests.fs" />
<Compile Include="RouteTests.fs" />
<Compile Include="AppTests.fs" />
<Compile Include="CorsTests.fs" />
<Compile Include="SmokeTests.fs" />
<Compile Include="Tier1SmokeTests.fs" />
```
