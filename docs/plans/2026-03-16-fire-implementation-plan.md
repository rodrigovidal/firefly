# Fire Framework Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build Fire, a minimal F# web framework on Kestrel with Hono-like ergonomics.

**Architecture:** Struct Request wrapper over HttpContext, immutable Response record, trie-based routing, pipe-friendly API. ASP.NET Core used only at the Kestrel edge.

**Tech Stack:** F# 10, .NET 10, ASP.NET Core (Kestrel only), System.Text.Json, xUnit + FsUnit for tests. Leverages `and!` in task CEs, `[<Struct>]` ValueOption, parallel compilation.

---

### Task 1: Solution & Project Setup

**Files:**
- Create: `Fire.sln`
- Create: `src/Fire/Fire.fsproj`
- Create: `tests/Fire.Tests/Fire.Tests.fsproj`

**Step 1: Create the solution and projects**

```bash
dotnet new sln -n Fire -o .
mkdir -p src/Fire
dotnet new classlib -lang F# -o src/Fire --framework net10.0
rm src/Fire/Library.fs
mkdir -p tests/Fire.Tests
dotnet new xunit -lang F# -o tests/Fire.Tests --framework net10.0
rm tests/Fire.Tests/Tests.fs
dotnet sln add src/Fire/Fire.fsproj
dotnet sln add tests/Fire.Tests/Fire.Tests.fsproj
dotnet add tests/Fire.Tests reference src/Fire
dotnet add tests/Fire.Tests package FsUnit.xUnit
```

**Step 2: Add ASP.NET Core dependency to Fire.fsproj**

Update `src/Fire/Fire.fsproj` to reference the ASP.NET Core shared framework:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <ParallelCompilation>true</ParallelCompilation>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
```

**Step 3: Verify everything compiles**

Run: `dotnet build`
Expected: Build succeeded with 0 errors.

**Step 4: Commit**

```bash
git init
git add -A
git commit -m "chore: scaffold Fire solution with src and test projects"
```

---

### Task 2: Response Types & Builders

**Files:**
- Create: `src/Fire/Response.fs`
- Create: `tests/Fire.Tests/ResponseTests.fs`

**Step 1: Write failing tests for Response builders**

Create `tests/Fire.Tests/ResponseTests.fs`:

```fsharp
module Fire.Tests.ResponseTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Response.ok has status 200 and empty body`` () =
    let r = Response.ok
    r.Status |> should equal 200
    r.Headers |> should be Empty
    r.Body |> should equal Empty

[<Fact>]
let ``Response.text sets Text body`` () =
    let r = Response.text "hello"
    r.Status |> should equal 200
    r.Body |> should equal (Text "hello")

[<Fact>]
let ``Response.json serializes to UTF-8 bytes`` () =
    let r = Response.json {| name = "fire" |}
    r.Status |> should equal 200
    match r.Body with
    | Json bytes -> System.Text.Encoding.UTF8.GetString(bytes) |> should contain "fire"
    | _ -> failwith "expected Json body"

[<Fact>]
let ``Response.status overrides status code`` () =
    let r = Response.ok |> Response.status 201
    r.Status |> should equal 201

[<Fact>]
let ``Response.header prepends header pair`` () =
    let r = Response.ok |> Response.header "X-Foo" "bar" |> Response.header "X-Baz" "qux"
    r.Headers |> should contain ("X-Foo", "bar")
    r.Headers |> should contain ("X-Baz", "qux")

[<Fact>]
let ``Response.header allows duplicate keys`` () =
    let r = Response.ok |> Response.header "Set-Cookie" "a=1" |> Response.header "Set-Cookie" "b=2"
    r.Headers |> List.filter (fun (k, _) -> k = "Set-Cookie") |> List.length |> should equal 2

[<Fact>]
let ``Response.notFound has status 404`` () =
    Response.notFound.Status |> should equal 404

[<Fact>]
let ``Response.unauthorized has status 401`` () =
    Response.unauthorized.Status |> should equal 401

[<Fact>]
let ``Response.ofResult maps Ok`` () =
    let r = Ok "hello" |> Response.ofResult Response.text (fun _ -> Response.notFound)
    r.Body |> should equal (Text "hello")

[<Fact>]
let ``Response.ofResult maps Error`` () =
    let r = Error "bad" |> Response.ofResult (fun _ -> Response.ok) (fun e -> Response.text e |> Response.status 400)
    r.Status |> should equal 400
    r.Body |> should equal (Text "bad")
```

Add `ResponseTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj` `<ItemGroup><Compile>`.

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests`
Expected: FAIL — `Fire` module not found.

**Step 3: Implement Response types and builders**

Create `src/Fire/Response.fs`:

```fsharp
namespace Fire

open System.IO
open System.Text.Json

type ResponseBody =
    | Empty
    | Text of string
    | Json of byte[]
    | Stream of Stream

type Response = {
    Status: int
    Headers: (string * string) list
    Body: ResponseBody
}

[<RequireQualifiedAccess>]
module Response =
    let ok = { Status = 200; Headers = []; Body = Empty }
    let text s = { ok with Body = Text s }

    let json<'T> (value: 'T) =
        { ok with Body = Json (JsonSerializer.SerializeToUtf8Bytes(value)) }

    let stream s = { ok with Body = Stream s }
    let status code r = { r with Status = code }
    let header key value r = { r with Headers = (key, value) :: r.Headers }

    let notFound = { ok with Status = 404; Headers = []; Body = Empty }
    let unauthorized = { ok with Status = 401; Headers = []; Body = Empty }

    let ofResult (onOk: 'T -> Response) (onError: 'E -> Response) (result: Result<'T, 'E>) =
        match result with
        | Ok value -> onOk value
        | Error err -> onError err
```

Add `Response.fs` to `src/Fire/Fire.fsproj` `<ItemGroup><Compile>`.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests`
Expected: All 10 tests PASS.

**Step 5: Commit**

```bash
git add src/Fire/Response.fs src/Fire/Fire.fsproj tests/Fire.Tests/ResponseTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add Response types and builder functions"
```

---

### Task 3: Request Struct Wrapper

**Files:**
- Create: `src/Fire/Request.fs`
- Create: `tests/Fire.Tests/RequestTests.fs`

**Step 1: Write failing tests for Request**

Create `tests/Fire.Tests/RequestTests.fs`:

```fsharp
module Fire.Tests.RequestTests

open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Xunit
open FsUnit.Xunit
open Fire

let makeHttpContext (method': string) (path: string) (query: string) (headers: (string * string) list) (body: string option) =
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
    ctx :> HttpContext

[<Fact>]
let ``Request exposes Path from HttpContext`` () =
    let ctx = makeHttpContext "GET" "/hello" "" [] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Path |> should equal "/hello"

[<Fact>]
let ``Request exposes Method from HttpContext`` () =
    let ctx = makeHttpContext "POST" "/" "" [] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Method |> should equal "POST"

[<Fact>]
let ``Request.Params returns route params`` () =
    let ps = dict ["id", "42"] :> IReadOnlyDictionary<_, _>
    let ctx = makeHttpContext "GET" "/users/42" "" [] None
    let req = Request(ctx, ps)
    req.Params.["id"] |> should equal "42"

[<Fact>]
let ``Request.Query parses query string`` () =
    let ctx = makeHttpContext "GET" "/search" "?q=fire&limit=10" [] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Query.["q"] |> should equal "fire"
    req.Query.["limit"] |> should equal "10"

[<Fact>]
let ``Request.Header returns header value`` () =
    let ctx = makeHttpContext "GET" "/" "" ["X-Custom", "test"] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Header "X-Custom" |> should equal (Some "test")

[<Fact>]
let ``Request.Header returns None for missing header`` () =
    let ctx = makeHttpContext "GET" "/" "" [] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Header "X-Missing" |> should equal None

[<Fact>]
let ``Request.Json deserializes body`` () = task {
    let ctx = makeHttpContext "POST" "/" "" [] (Some """{"name":"fire"}""")
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    let! result = req.Json<{| name: string |}>()
    result.name |> should equal "fire"
}

[<Fact>]
let ``Request.Raw returns underlying HttpContext`` () =
    let ctx = makeHttpContext "GET" "/" "" [] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Raw |> should be (sameAs ctx)
```

Add `RequestTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj`.

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests`
Expected: FAIL — `Request` type not found.

**Step 3: Implement Request struct**

Create `src/Fire/Request.fs`:

```fsharp
namespace Fire

open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

[<Struct>]
type Request(ctx: HttpContext, routeParams: IReadOnlyDictionary<string, string>) =

    member _.Path = ctx.Request.Path.Value
    member _.Method = ctx.Request.Method
    member _.Params = routeParams

    member _.Query : IReadOnlyDictionary<string, string> =
        let q = ctx.Request.Query
        let d = Dictionary<string, string>(q.Count)
        for kvp in q do
            d.[kvp.Key] <- kvp.Value.ToString()
        d :> IReadOnlyDictionary<_, _>

    member _.Header (name: string) : string option =
        match ctx.Request.Headers.TryGetValue(name) with
        | true, values -> Some (values.ToString())
        | false, _ -> None

    member _.Headers (name: string) : string list =
        match ctx.Request.Headers.TryGetValue(name) with
        | true, values -> values.ToArray() |> Array.toList
        | false, _ -> []

    member _.Body : Stream = ctx.Request.Body

    member _.Json<'T>() : Task<'T> = task {
        let! result = JsonSerializer.DeserializeAsync<'T>(ctx.Request.Body)
        return result
    }

    member _.Raw = ctx
```

Add `Request.fs` to `src/Fire/Fire.fsproj` **before** `Response.fs` (F# file ordering: Request doesn't depend on Response).

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests`
Expected: All tests PASS.

**Step 5: Commit**

```bash
git add src/Fire/Request.fs src/Fire/Fire.fsproj tests/Fire.Tests/RequestTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add Request struct wrapper over HttpContext"
```

---

### Task 4: Route Trie

**Files:**
- Create: `src/Fire/Trie.fs`
- Create: `tests/Fire.Tests/TrieTests.fs`

**Step 1: Write failing tests for the trie**

Create `tests/Fire.Tests/TrieTests.fs`:

```fsharp
module Fire.Tests.TrieTests

open Xunit
open FsUnit.Xunit
open Fire

// We test the trie with simple string handlers for isolation.
// The trie stores a compiled handler per (method, path).

[<Fact>]
let ``Trie matches static route`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/hello" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/hello" trie
    result |> Option.isSome |> should be True

[<Fact>]
let ``Trie returns None for unmatched path`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/hello" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/world" trie
    result |> Option.isNone |> should be True

[<Fact>]
let ``Trie returns None for unmatched method`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/hello" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "POST" "/hello" trie
    result |> Option.isNone |> should be True

[<Fact>]
let ``Trie captures route params`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/users/:id" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/users/42" trie
    result |> Option.isSome |> should be True
    let (_, ps) = result.Value
    ps.["id"] |> should equal "42"

[<Fact>]
let ``Trie captures multiple route params`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/users/:userId/posts/:postId" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/users/7/posts/99" trie
    result |> Option.isSome |> should be True
    let (_, ps) = result.Value
    ps.["userId"] |> should equal "7"
    ps.["postId"] |> should equal "99"

[<Fact>]
let ``Trie distinguishes between methods on same path`` () =
    let handlerGet = fun _ -> task { return Response.text "get" }
    let handlerPost = fun _ -> task { return Response.text "post" }
    let trie =
        Trie.empty
        |> Trie.add "GET" "/items" [] handlerGet
        |> Trie.add "POST" "/items" [] handlerPost
    let (hGet, _) = (Trie.lookup "GET" "/items" trie).Value
    let (hPost, _) = (Trie.lookup "POST" "/items" trie).Value
    // Verify they are different handlers
    let rGet = hGet (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    let rPost = hPost (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    rGet.Body |> should equal (Text "get")
    rPost.Body |> should equal (Text "post")

[<Fact>]
let ``Trie matches root path`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/" trie
    result |> Option.isSome |> should be True

[<Fact>]
let ``Trie static segment takes priority over param`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/users/me" [] (fun _ -> task { return Response.text "me" })
        |> Trie.add "GET" "/users/:id" [] (fun _ -> task { return Response.text "param" })
    let (hMe, _) = (Trie.lookup "GET" "/users/me" trie).Value
    let (hParam, _) = (Trie.lookup "GET" "/users/42" trie).Value
    let rMe = hMe (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    let rParam = hParam (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    rMe.Body |> should equal (Text "me")
    rParam.Body |> should equal (Text "param")

[<Fact>]
let ``Trie pre-composes middleware chain`` () =
    let mutable callOrder = []
    let mw1 : Middleware = fun next req -> task {
        callOrder <- callOrder @ ["mw1"]
        return! next req
    }
    let mw2 : Middleware = fun next req -> task {
        callOrder <- callOrder @ ["mw2"]
        return! next req
    }
    let handler : Handler = fun _ -> task {
        callOrder <- callOrder @ ["handler"]
        return Response.ok
    }
    let trie =
        Trie.empty
        |> Trie.add "GET" "/test" [mw1; mw2] handler
    let (h, _) = (Trie.lookup "GET" "/test" trie).Value
    h (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    callOrder |> should equal ["mw1"; "mw2"; "handler"]
```

Add `TrieTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj`.

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests`
Expected: FAIL — `Trie` module not found.

**Step 3: Implement the Trie**

Create `src/Fire/Trie.fs`:

```fsharp
namespace Fire

open System.Collections.Generic

type TrieNode = {
    StaticChildren: Dictionary<string, TrieNode>
    ParamChild: (string * TrieNode) option  // (paramName, node)
    Handlers: Dictionary<string, Handler>    // method -> pre-composed handler
}

[<RequireQualifiedAccess>]
module Trie =

    let private emptyNode () = {
        StaticChildren = Dictionary<string, TrieNode>()
        ParamChild = None
        Handlers = Dictionary<string, Handler>()
    }

    let empty = emptyNode ()

    let private splitPath (path: string) =
        path.Split('/', System.StringSplitOptions.RemoveEmptyEntries)

    let private composeMiddleware (middlewares: Middleware list) (handler: Handler) : Handler =
        List.foldBack (fun (mw: Middleware) (h: Handler) -> mw h) middlewares handler

    let add (method': string) (pattern: string) (middlewares: Middleware list) (handler: Handler) (root: TrieNode) : TrieNode =
        let segments = splitPath pattern
        let composed = composeMiddleware middlewares handler

        let rec insert (node: TrieNode) (idx: int) =
            if idx >= segments.Length then
                node.Handlers.[method'] <- composed
                node
            else
                let seg = segments.[idx]
                if seg.StartsWith(":") then
                    let paramName = seg.Substring(1)
                    let child =
                        match node.ParamChild with
                        | Some (_, existing) -> existing
                        | None -> emptyNode ()
                    let updated = insert child (idx + 1)
                    { node with ParamChild = Some (paramName, updated) }
                else
                    let child =
                        match node.StaticChildren.TryGetValue(seg) with
                        | true, existing -> existing
                        | false, _ -> emptyNode ()
                    let updated = insert child (idx + 1)
                    node.StaticChildren.[seg] <- updated
                    node

        insert root 0

    let lookup (method': string) (path: string) (root: TrieNode) : (Handler * IReadOnlyDictionary<string, string>) option =
        let segments = splitPath path
        let ps = Dictionary<string, string>()

        let rec search (node: TrieNode) (idx: int) =
            if idx >= segments.Length then
                match node.Handlers.TryGetValue(method') with
                | true, h -> Some (h, ps :> IReadOnlyDictionary<_, _>)
                | false, _ -> None
            else
                let seg = segments.[idx]
                // Static match takes priority
                match node.StaticChildren.TryGetValue(seg) with
                | true, child ->
                    match search child (idx + 1) with
                    | Some _ as result -> result
                    | None -> tryParam node seg idx
                | false, _ -> tryParam node seg idx

        and tryParam (node: TrieNode) (seg: string) (idx: int) =
            match node.ParamChild with
            | Some (paramName, child) ->
                ps.[paramName] <- seg
                search child (idx + 1)
            | None -> None

        // Handle root path "/"
        if segments.Length = 0 then
            match root.Handlers.TryGetValue(method') with
            | true, h -> Some (h, ps :> IReadOnlyDictionary<_, _>)
            | false, _ -> None
        else
            search root 0
```

Add `Trie.fs` to `src/Fire/Fire.fsproj` after `Response.fs`.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests`
Expected: All tests PASS.

**Step 5: Commit**

```bash
git add src/Fire/Trie.fs src/Fire/Fire.fsproj tests/Fire.Tests/TrieTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add trie-based route matcher with param capture and middleware pre-composition"
```

---

### Task 5: Route Module

**Files:**
- Create: `src/Fire/Route.fs`
- Create: `tests/Fire.Tests/RouteTests.fs`

**Step 1: Write failing tests for the Route module**

Create `tests/Fire.Tests/RouteTests.fs`:

```fsharp
module Fire.Tests.RouteTests

open Xunit
open FsUnit.Xunit
open Fire

let dummyHandler : Handler = fun _ -> task { return Response.ok }
let textHandler (t: string) : Handler = fun _ -> task { return Response.text t }

[<Fact>]
let ``Route.start creates empty table`` () =
    let table = Route.start
    table.Prefix |> should equal ""
    table.Middlewares |> should be Empty
    table.Routes |> should be Empty

[<Fact>]
let ``Route.get adds a GET route with prefix`` () =
    let table =
        Route.start
        |> Route.get "/hello" dummyHandler
    table.Routes |> List.length |> should equal 1
    table.Routes.[0].Method |> should equal "GET"
    table.Routes.[0].Pattern |> should equal "/hello"

[<Fact>]
let ``Route.group scopes prefix`` () =
    let table =
        Route.start
        |> Route.group "/api" (fun api ->
            api |> Route.get "/health" dummyHandler
        )
    table.Routes.[0].Pattern |> should equal "/api/health"

[<Fact>]
let ``Route.group nests prefixes`` () =
    let table =
        Route.start
        |> Route.group "/api" (fun api ->
            api
            |> Route.group "/v1" (fun v1 ->
                v1 |> Route.get "/users" dummyHandler
            )
        )
    table.Routes.[0].Pattern |> should equal "/api/v1/users"

[<Fact>]
let ``Route.middleware is scoped to group`` () =
    let mw : Middleware = fun next req -> next req
    let table =
        Route.start
        |> Route.group "/api" (fun api ->
            api
            |> Route.middleware mw
            |> Route.get "/inner" dummyHandler
        )
        |> Route.get "/outer" dummyHandler
    // Inner route has middleware, outer does not
    table.Routes.[0].Middlewares |> List.length |> should equal 1
    table.Routes.[1].Middlewares |> should be Empty

[<Fact>]
let ``Route registers all HTTP methods`` () =
    let table =
        Route.start
        |> Route.get "/a" dummyHandler
        |> Route.post "/b" dummyHandler
        |> Route.put "/c" dummyHandler
        |> Route.patch "/d" dummyHandler
        |> Route.delete "/e" dummyHandler
        |> Route.head "/f" dummyHandler
        |> Route.options "/g" dummyHandler
    let methods = table.Routes |> List.map (fun r -> r.Method)
    methods |> should equal ["GET"; "POST"; "PUT"; "PATCH"; "DELETE"; "HEAD"; "OPTIONS"]

[<Fact>]
let ``Route.method registers custom HTTP method`` () =
    let table =
        Route.start
        |> Route.method "PURGE" "/cache" dummyHandler
    table.Routes.[0].Method |> should equal "PURGE"

[<Fact>]
let ``Sibling groups have independent middleware`` () =
    let mw1 : Middleware = fun next req -> next req
    let mw2 : Middleware = fun next req -> next req
    let table =
        Route.start
        |> Route.group "/a" (fun a ->
            a |> Route.middleware mw1 |> Route.get "" dummyHandler
        )
        |> Route.group "/b" (fun b ->
            b |> Route.middleware mw2 |> Route.get "" dummyHandler
        )
    table.Routes.[0].Middlewares |> List.length |> should equal 1
    table.Routes.[1].Middlewares |> List.length |> should equal 1
    // They should have different middleware
    table.Routes.[0].Middlewares.[0] |> should not' (be sameAs table.Routes.[1].Middlewares.[0])
```

Add `RouteTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj`.

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests`
Expected: FAIL — `Route` module not found.

**Step 3: Implement Route module**

Create `src/Fire/Route.fs`:

```fsharp
namespace Fire

type RouteEntry = {
    Method: string
    Pattern: string
    Middlewares: Middleware list
    Handler: Handler
}

type RouteTable = {
    Prefix: string
    Middlewares: Middleware list
    Routes: RouteEntry list
}

[<RequireQualifiedAccess>]
module Route =

    let start = { Prefix = ""; Middlewares = []; Routes = [] }

    let private addRoute (verb: string) (pattern: string) (handler: Handler) (table: RouteTable) =
        let entry = {
            Method = verb
            Pattern = table.Prefix + pattern
            Middlewares = table.Middlewares
            Handler = handler
        }
        { table with Routes = table.Routes @ [entry] }

    let get pattern handler table = addRoute "GET" pattern handler table
    let post pattern handler table = addRoute "POST" pattern handler table
    let put pattern handler table = addRoute "PUT" pattern handler table
    let patch pattern handler table = addRoute "PATCH" pattern handler table
    let delete pattern handler table = addRoute "DELETE" pattern handler table
    let head pattern handler table = addRoute "HEAD" pattern handler table
    let options pattern handler table = addRoute "OPTIONS" pattern handler table
    let method verb pattern handler table = addRoute verb pattern handler table

    let group (prefix: string) (configure: RouteTable -> RouteTable) (parent: RouteTable) =
        let scoped = { Prefix = parent.Prefix + prefix; Middlewares = parent.Middlewares; Routes = [] }
        let result = configure scoped
        { parent with Routes = parent.Routes @ result.Routes }

    let middleware (mw: Middleware) (table: RouteTable) =
        { table with Middlewares = table.Middlewares @ [mw] }
```

Add `Route.fs` to `src/Fire/Fire.fsproj` after `Trie.fs`.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests`
Expected: All tests PASS.

**Step 5: Commit**

```bash
git add src/Fire/Route.fs src/Fire/Fire.fsproj tests/Fire.Tests/RouteTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add Route module with scoped groups and middleware"
```

---

### Task 6: App Module — Kestrel Integration

**Files:**
- Create: `src/Fire/App.fs`
- Create: `tests/Fire.Tests/AppTests.fs`

**Step 1: Write failing tests for App**

Create `tests/Fire.Tests/AppTests.fs`:

```fsharp
module Fire.Tests.AppTests

open System.Net
open System.Net.Http
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Fire

let runTestServer (routes: RouteTable) (config: FireConfig) = task {
    let cts = new System.Threading.CancellationTokenSource()
    let serverTask = App.run routes config cts.Token
    // Give server time to start
    do! Task.Delay(200)
    return (cts, serverTask)
}

[<Fact>]
let ``App serves a simple GET route`` () = task {
    let routes =
        Route.start
        |> Route.get "/hello" (fun _ -> task { return Response.text "world" })

    let config = App.defaults |> App.port 0  // random port

    use cts = new System.Threading.CancellationTokenSource()
    let! (port, serverTask) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! response = client.GetAsync($"http://localhost:{port}/hello")
    let! body = response.Content.ReadAsStringAsync()

    response.StatusCode |> should equal HttpStatusCode.OK
    body |> should equal "world"

    cts.Cancel()
}

[<Fact>]
let ``App returns 404 for unmatched route`` () = task {
    let routes =
        Route.start
        |> Route.get "/exists" (fun _ -> task { return Response.ok })

    let config = App.defaults |> App.port 0

    use cts = new System.Threading.CancellationTokenSource()
    let! (port, serverTask) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! response = client.GetAsync($"http://localhost:{port}/nope")

    response.StatusCode |> should equal HttpStatusCode.NotFound

    cts.Cancel()
}

[<Fact>]
let ``App serves JSON response`` () = task {
    let routes =
        Route.start
        |> Route.get "/data" (fun _ -> task {
            return Response.json {| name = "fire" |}
        })

    let config = App.defaults |> App.port 0

    use cts = new System.Threading.CancellationTokenSource()
    let! (port, serverTask) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! response = client.GetAsync($"http://localhost:{port}/data")
    let! body = response.Content.ReadAsStringAsync()

    response.StatusCode |> should equal HttpStatusCode.OK
    body |> should contain "fire"
    response.Content.Headers.ContentType.MediaType |> should equal "application/json"

    cts.Cancel()
}

[<Fact>]
let ``App captures route params`` () = task {
    let routes =
        Route.start
        |> Route.get "/users/:id" (fun req -> task {
            return Response.text (req.Params.["id"])
        })

    let config = App.defaults |> App.port 0

    use cts = new System.Threading.CancellationTokenSource()
    let! (port, serverTask) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! response = client.GetAsync($"http://localhost:{port}/users/42")
    let! body = response.Content.ReadAsStringAsync()

    body |> should equal "42"

    cts.Cancel()
}

[<Fact>]
let ``App applies middleware`` () = task {
    let addHeader : Middleware = fun next req -> task {
        let! response = next req
        return response |> Response.header "X-Middleware" "applied"
    }

    let routes =
        Route.start
        |> Route.group "/api" (fun api ->
            api
            |> Route.middleware addHeader
            |> Route.get "/test" (fun _ -> task { return Response.ok })
        )

    let config = App.defaults |> App.port 0

    use cts = new System.Threading.CancellationTokenSource()
    let! (port, serverTask) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! response = client.GetAsync($"http://localhost:{port}/api/test")

    response.Headers.GetValues("X-Middleware") |> Seq.head |> should equal "applied"

    cts.Cancel()
}

[<Fact>]
let ``App calls custom error handler`` () = task {
    let routes =
        Route.start
        |> Route.get "/boom" (fun _ -> task {
            return failwith "oops"
            return Response.ok
        })

    let config =
        App.defaults
        |> App.port 0
        |> App.onError (fun ex _ -> task {
            return Response.text ex.Message |> Response.status 500
        })

    use cts = new System.Threading.CancellationTokenSource()
    let! (port, serverTask) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! response = client.GetAsync($"http://localhost:{port}/boom")
    let! body = response.Content.ReadAsStringAsync()

    response.StatusCode |> should equal HttpStatusCode.InternalServerError
    body |> should equal "oops"

    cts.Cancel()
}

[<Fact>]
let ``App calls custom not-found handler`` () = task {
    let routes = Route.start

    let config =
        App.defaults
        |> App.port 0
        |> App.notFound (fun req -> task {
            return Response.json {| error = "not found"; path = req.Path |} |> Response.status 404
        })

    use cts = new System.Threading.CancellationTokenSource()
    let! (port, serverTask) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! response = client.GetAsync($"http://localhost:{port}/missing")
    let! body = response.Content.ReadAsStringAsync()

    response.StatusCode |> should equal HttpStatusCode.NotFound
    body |> should contain "not found"

    cts.Cancel()
}
```

Add `AppTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj`.

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fire.Tests`
Expected: FAIL — `App` module and `FireConfig` not found.

**Step 3: Implement App module**

Create `src/Fire/App.fs`:

```fsharp
namespace Fire

open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http

type FireConfig = {
    Port: int
    Host: string
    JsonOptions: JsonSerializerOptions option
    OnError: (exn -> Request -> Task<Response>) option
    NotFound: (Request -> Task<Response>) option
}

[<RequireQualifiedAccess>]
module App =

    let defaults = {
        Port = 3000
        Host = "localhost"
        JsonOptions = None
        OnError = None
        NotFound = None
    }

    let port p config = { config with Port = p }
    let host h config = { config with Host = h }
    let jsonOptions opts config = { config with JsonOptions = Some opts }
    let onError handler config = { config with OnError = Some handler }
    let notFound handler config = { config with NotFound = Some handler }

    let private buildTrie (routes: RouteTable) : TrieNode =
        let mutable trie = Trie.empty
        for entry in routes.Routes do
            trie <- Trie.add entry.Method entry.Pattern entry.Middlewares entry.Handler trie
        trie

    let private writeResponse (ctx: HttpContext) (response: Response) = task {
        ctx.Response.StatusCode <- response.Status
        for (key, value) in response.Headers do
            ctx.Response.Headers.Append(key, value)
        match response.Body with
        | Empty -> ()
        | Text s ->
            ctx.Response.ContentType <- "text/plain; charset=utf-8"
            do! ctx.Response.WriteAsync(s)
        | Json bytes ->
            ctx.Response.ContentType <- "application/json; charset=utf-8"
            do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
        | Stream stream ->
            do! stream.CopyToAsync(ctx.Response.Body)
    }

    /// Starts the server and returns a Task. Pass CancellationToken to stop.
    let run (routes: RouteTable) (config: FireConfig) (ct: CancellationToken) : Task =
        let trie = buildTrie routes
        let builder = WebApplication.CreateBuilder()
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.ListenLocalhost(config.Port)
        ) |> ignore

        let app = builder.Build()

        app.Run(fun ctx -> task {
            let path = ctx.Request.Path.Value
            let method' = ctx.Request.Method

            match Trie.lookup method' path trie with
            | Some (handler, ps) ->
                let req = Request(ctx, ps)
                try
                    let! response = handler req
                    do! writeResponse ctx response
                with ex ->
                    match config.OnError with
                    | Some errorHandler ->
                        let! response = errorHandler ex req
                        do! writeResponse ctx response
                    | None ->
                        ctx.Response.StatusCode <- 500
            | None ->
                match config.NotFound with
                | Some nfHandler ->
                    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
                    let! response = nfHandler req
                    do! writeResponse ctx response
                | None ->
                    ctx.Response.StatusCode <- 404
        })

        app.RunAsync(ct)

    /// Test helper: starts on port 0, returns (actualPort, serverTask).
    let runTest (routes: RouteTable) (config: FireConfig) (ct: CancellationToken) : Task<int * Task> = task {
        let trie = buildTrie routes
        let builder = WebApplication.CreateBuilder()
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.ListenLocalhost(0)  // OS-assigned port
        ) |> ignore

        let app = builder.Build()

        app.Run(fun ctx -> task {
            let path = ctx.Request.Path.Value
            let method' = ctx.Request.Method

            match Trie.lookup method' path trie with
            | Some (handler, ps) ->
                let req = Request(ctx, ps)
                try
                    let! response = handler req
                    do! writeResponse ctx response
                with ex ->
                    match config.OnError with
                    | Some errorHandler ->
                        let! response = errorHandler ex req
                        do! writeResponse ctx response
                    | None ->
                        ctx.Response.StatusCode <- 500
            | None ->
                match config.NotFound with
                | Some nfHandler ->
                    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
                    let! response = nfHandler req
                    do! writeResponse ctx response
                | None ->
                    ctx.Response.StatusCode <- 404
        })

        do! app.StartAsync(ct)
        let address = app.Urls |> Seq.head
        let uri = System.Uri(address)
        return (uri.Port, app.StopAsync(ct))
    }
```

Add `App.fs` to `src/Fire/Fire.fsproj` after `Route.fs`.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Fire.Tests`
Expected: All tests PASS.

**Step 5: Commit**

```bash
git add src/Fire/App.fs src/Fire/Fire.fsproj tests/Fire.Tests/AppTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "feat: add App module with Kestrel integration, error handling, and test helper"
```

---

### Task 7: Handler & Middleware Type Aliases

**Files:**
- Create: `src/Fire/Types.fs`

**Step 1: Create a shared types file for Handler and Middleware aliases**

These are currently defined implicitly. Create `src/Fire/Types.fs` so that all modules can reference them:

```fsharp
namespace Fire

open System.Threading.Tasks

type Handler = Request -> Task<Response>

type Middleware = Handler -> Handler
```

Add `Types.fs` to `src/Fire/Fire.fsproj` after `Response.fs` (after Request and Response are defined, before Trie/Route/App).

**Step 2: Remove any duplicate type definitions from other files**

Ensure `Handler` and `Middleware` are defined only in `Types.fs`. The other files (`Trie.fs`, `Route.fs`, `App.fs`) use them via the `Fire` namespace.

**Step 3: Verify everything compiles and tests pass**

Run: `dotnet test tests/Fire.Tests`
Expected: All tests PASS.

**Step 4: Commit**

```bash
git add src/Fire/Types.fs src/Fire/Fire.fsproj
git commit -m "refactor: extract Handler and Middleware type aliases to shared Types.fs"
```

---

### Task 8: End-to-End Smoke Test

**Files:**
- Create: `tests/Fire.Tests/SmokeTests.fs`

**Step 1: Write an end-to-end test that exercises the full API surface**

Create `tests/Fire.Tests/SmokeTests.fs`:

```fsharp
module Fire.Tests.SmokeTests

open System.Net
open System.Net.Http
open System.Text
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Full API smoke test`` () = task {
    let withCors : Middleware = fun next req -> task {
        let! response = next req
        return response |> Response.header "Access-Control-Allow-Origin" "*"
    }

    let withAuth : Middleware = fun next req -> task {
        match req.Header "Authorization" with
        | Some _ -> return! next req
        | None -> return Response.unauthorized |> Response.json {| error = "no token" |}
    }

    let routes =
        Route.start
        |> Route.get "/" (fun _ -> task { return Response.text "Fire" })
        |> Route.group "/api" (fun api ->
            api
            |> Route.middleware withCors
            |> Route.get "/health" (fun _ -> task { return Response.ok })
            |> Route.group "/users" (fun users ->
                users
                |> Route.get "/:id" (fun req -> task {
                    let id = req.Params.["id"]
                    return Response.json {| id = id |}
                })
            )
        )

    let config =
        App.defaults
        |> App.port 0
        |> App.notFound (fun req -> task {
            return Response.json {| error = "not found" |} |> Response.status 404
        })

    use cts = new System.Threading.CancellationTokenSource()
    let! (port, _) = App.runTest routes config cts.Token
    use client = new HttpClient()
    let base' = $"http://localhost:{port}"

    // GET /
    let! r1 = client.GetAsync($"{base'}/")
    let! b1 = r1.Content.ReadAsStringAsync()
    r1.StatusCode |> should equal HttpStatusCode.OK
    b1 |> should equal "Fire"

    // GET /api/health (has CORS header)
    let! r2 = client.GetAsync($"{base'}/api/health")
    r2.StatusCode |> should equal HttpStatusCode.OK
    r2.Headers.GetValues("Access-Control-Allow-Origin") |> Seq.head |> should equal "*"

    // GET /api/users/42 (route param + CORS) — using and! for concurrent awaiting
    let! r3 = client.GetAsync($"{base'}/api/users/42")
    and! r4nf = client.GetAsync($"{base'}/nope")
    let! b3 = r3.Content.ReadAsStringAsync()
    r3.StatusCode |> should equal HttpStatusCode.OK
    b3 |> should contain "42"
    r3.Headers.GetValues("Access-Control-Allow-Origin") |> Seq.head |> should equal "*"

    // GET /nope (custom 404) — already fetched concurrently via and! above
    let! b4 = r4nf.Content.ReadAsStringAsync()
    r4nf.StatusCode |> should equal HttpStatusCode.NotFound
    b4 |> should contain "not found"

    cts.Cancel()
}
```

Add `SmokeTests.fs` to `tests/Fire.Tests/Fire.Tests.fsproj`.

**Step 2: Run all tests**

Run: `dotnet test tests/Fire.Tests`
Expected: All tests PASS.

**Step 3: Commit**

```bash
git add tests/Fire.Tests/SmokeTests.fs tests/Fire.Tests/Fire.Tests.fsproj
git commit -m "test: add end-to-end smoke test covering routing, params, middleware, and 404"
```

---

### File ordering in Fire.fsproj

F# requires files in dependency order. The final `<ItemGroup>` in `src/Fire/Fire.fsproj`:

```xml
<ItemGroup>
    <Compile Include="Response.fs" />
    <Compile Include="Request.fs" />
    <Compile Include="Types.fs" />
    <Compile Include="Trie.fs" />
    <Compile Include="Route.fs" />
    <Compile Include="App.fs" />
</ItemGroup>
```

### File ordering in Fire.Tests.fsproj

```xml
<ItemGroup>
    <Compile Include="ResponseTests.fs" />
    <Compile Include="RequestTests.fs" />
    <Compile Include="TrieTests.fs" />
    <Compile Include="RouteTests.fs" />
    <Compile Include="AppTests.fs" />
    <Compile Include="SmokeTests.fs" />
</ItemGroup>
```
