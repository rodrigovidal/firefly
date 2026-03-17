# Tier 4 Features Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add NuGet packaging setup, testing helpers, composable validation, and JWT auth to Fire.

**Architecture:** TestClient uses either direct handler invocation or HTTP. Validation is composable functions. JWT uses Microsoft.IdentityModel.JsonWebTokens.

**Tech Stack:** F# 10, .NET 10, Microsoft.IdentityModel.JsonWebTokens for JWT.

---

### Task 1: NuGet Packaging Setup + LICENSE + README

**Files:**
- Modify: `src/Fire/Fire.fsproj`
- Create: `LICENSE`
- Create: `README.md`

**Step 1: Add package metadata to Fire.fsproj**

Add to the existing `<PropertyGroup>` in `src/Fire/Fire.fsproj`:

```xml
<PackageId>Fire</PackageId>
<Version>0.1.0</Version>
<Authors>Rodrigo Vidal</Authors>
<Description>A minimal F# web framework built on Kestrel</Description>
<PackageTags>fsharp;web;framework;kestrel;api</PackageTags>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<RepositoryUrl>https://github.com/rodrigovidal/fire</RepositoryUrl>
<PackageReadmeFile>README.md</PackageReadmeFile>
```

Add a new `<ItemGroup>` to include README in the package:

```xml
<ItemGroup>
    <None Include="../../README.md" Pack="true" PackagePath="/" />
</ItemGroup>
```

**Step 2: Create LICENSE**

Create `LICENSE` at repo root with MIT license, copyright Rodrigo Vidal 2026.

**Step 3: Create README.md**

Create `README.md` at repo root with:
- Project name and one-line description
- Install command: `dotnet add package Fire`
- Hello world example showing Route.start, Route.get, App.defaults, App.run
- Feature list (routing, middleware, JSON, cookies, CORS, logging, static files, rate limiting, OpenAPI, etc.)
- License

**Step 4: Verify package builds**

Run: `dotnet pack src/Fire -c Release --no-build` (after a build)
Expected: produces `Fire.0.1.0.nupkg`

**Step 5: Commit**

```bash
git commit -m "chore: add NuGet package metadata, LICENSE, and README"
```

---

### Task 2: Testing Helpers

**Files:**
- Create: `src/Fire/TestClient.fs`
- Create: `tests/Fire.Tests/TestClientTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/TestClientTests.fs`:

```fsharp
module Fire.Tests.TestClientTests

open Xunit
open FsUnit.Xunit
open Fire

let routes =
    Route.start
    |> Route.get "/hello" (fun _ -> task { return Response.text "world" })
    |> Route.get "/users/:id" (fun req -> task {
        return Response.json {| id = req.Params.["id"] |}
    })
    |> Route.post "/echo" (fun req -> task {
        let! body = req.Text()
        return Response.text body
    })
    |> Route.get "/header-check" (fun req -> task {
        let v = req.Header "X-Custom" |> Option.defaultValue "none"
        return Response.text v
    })

// --- Direct mode tests ---

[<Fact>]
let ``Direct: GET returns correct status and body`` () = task {
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/hello"
    r.Status |> should equal 200
    r.Body |> should equal "world"
}

[<Fact>]
let ``Direct: GET with route params`` () = task {
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/users/42"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "42"
}

[<Fact>]
let ``Direct: POST with body`` () = task {
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/echo" "hello fire"
    r.Status |> should equal 200
    r.Body |> should equal "hello fire"
}

[<Fact>]
let ``Direct: returns 404 for unknown route`` () = task {
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/nope"
    r.Status |> should equal 404
}

[<Fact>]
let ``Direct: withHeader adds header to request`` () = task {
    let client = TestClient.create routes |> TestClient.withHeader "X-Custom" "test-val"
    let! r = client |> TestClient.get "/header-check"
    r.Body |> should equal "test-val"
}

[<Fact>]
let ``Direct: createWith applies global middleware`` () = task {
    let mw : Middleware = fun next req -> task {
        let! response = next req
        return response |> Response.header "X-MW" "applied"
    }
    let config = App.defaults |> App.middleware mw
    let client = TestClient.createWith routes config
    let! r = client |> TestClient.get "/hello"
    r.Headers |> List.exists (fun (k, _) -> k = "X-MW") |> should be True
}

// --- Integration mode tests ---

[<Fact>]
let ``Integration: GET returns correct status and body`` () = task {
    let! client = TestClient.start routes (App.defaults |> App.port 0)
    let! r = client |> TestClient.get "/hello"
    r.Status |> should equal 200
    r.Body |> should equal "world"
    do! TestClient.stop client
}

[<Fact>]
let ``Integration: POST with body`` () = task {
    let! client = TestClient.start routes (App.defaults |> App.port 0)
    let! r = client |> TestClient.post "/echo" "hello fire"
    r.Status |> should equal 200
    r.Body |> should equal "hello fire"
    do! TestClient.stop client
}
```

Add `TestClientTests.fs` to test fsproj after `Tier3SmokeTests.fs`.

**Step 2: Implement TestClient.fs**

Create `src/Fire/TestClient.fs`:

```fsharp
namespace Fire

open System
open System.Collections.Generic
open System.IO
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type TestResponse = {
    Status: int
    Headers: (string * string) list
    Body: string
}

type TestClient = {
    Mode: TestClientMode
    DefaultHeaders: (string * string) list
}

and TestClientMode =
    | Direct of trie: TrieNode * config: FireConfig
    | Integration of port: int * stop: (unit -> Task) * client: HttpClient

[<RequireQualifiedAccess>]
module TestClient =

    let private buildTrie (routes: RouteTable) : TrieNode =
        let mutable trie = Trie.empty
        for entry in routes.Routes do
            trie <- Trie.add entry.Method entry.Pattern entry.Middlewares entry.Handler trie
        trie

    let create (routes: RouteTable) : TestClient =
        { Mode = Direct (buildTrie routes, App.defaults); DefaultHeaders = [] }

    let createWith (routes: RouteTable) (config: FireConfig) : TestClient =
        { Mode = Direct (buildTrie routes, config); DefaultHeaders = [] }

    let start (routes: RouteTable) (config: FireConfig) : Task<TestClient> = task {
        let! (port, stop) = App.runTest routes config CancellationToken.None
        let client = new HttpClient()
        return { Mode = Integration (port, stop, client); DefaultHeaders = [] }
    }

    let withHeader (key: string) (value: string) (client: TestClient) : TestClient =
        { client with DefaultHeaders = (key, value) :: client.DefaultHeaders }

    let private directRequest (method': string) (path: string) (body: string option)
                              (headers: (string * string) list)
                              (trie: TrieNode) (config: FireConfig) : Task<TestResponse> = task {
        let ctx = DefaultHttpContext()
        // Parse path and query string
        let parts = path.Split('?', 2)
        ctx.Request.Method <- method'
        ctx.Request.Path <- PathString(parts.[0])
        if parts.Length > 1 then
            ctx.Request.QueryString <- QueryString("?" + parts.[1])
        for (k, v) in headers do
            ctx.Request.Headers.[k] <- Microsoft.Extensions.Primitives.StringValues(v)
        match body with
        | Some b ->
            let bytes = Encoding.UTF8.GetBytes(b)
            ctx.Request.Body <- new MemoryStream(bytes)
            ctx.Request.ContentType <- "text/plain"
        | None -> ()

        // Build response capture stream
        let responseBody = new MemoryStream()
        ctx.Response.Body <- responseBody

        // Dispatch through trie + global middleware
        let emptyParams = Dictionary<string, string>() :> IReadOnlyDictionary<_, _>

        let baseHandler : Handler = fun _req -> task {
            let lookupPath = ctx.Request.Path.Value
            let lookupMethod = ctx.Request.Method
            match Trie.lookup lookupMethod lookupPath trie with
            | Some (handler, ps) ->
                let req = Request(ctx, ps)
                return! handler req
            | None ->
                match config.NotFound with
                | Some nfHandler ->
                    let req = Request(ctx, emptyParams)
                    return! nfHandler req
                | None ->
                    return { Status = 404; Headers = []; Body = Empty }
        }

        let composed =
            List.foldBack (fun (mw: Middleware) (h: Handler) -> mw h) config.Middlewares baseHandler

        let req = Request(ctx, emptyParams)
        let! response = composed req

        // Write response to capture stream
        ctx.Response.StatusCode <- response.Status
        for (key, value) in response.Headers do
            ctx.Response.Headers.Append(key, value)
        match response.Body with
        | Empty -> ()
        | Text s ->
            let bytes = Encoding.UTF8.GetBytes(s)
            do! responseBody.WriteAsync(bytes, 0, bytes.Length)
        | Json bytes ->
            do! responseBody.WriteAsync(bytes, 0, bytes.Length)
        | Stream stream ->
            do! stream.CopyToAsync(responseBody)

        responseBody.Position <- 0L
        use reader = new StreamReader(responseBody)
        let! bodyStr = reader.ReadToEndAsync()

        let responseHeaders =
            [ for kvp in ctx.Response.Headers do
                for v in kvp.Value do
                    yield (kvp.Key, v) ]
            @ (response.Headers |> List.rev)

        return {
            Status = response.Status
            Headers = responseHeaders
            Body = bodyStr
        }
    }

    let private httpRequest (method': string) (path: string) (body: string option)
                            (headers: (string * string) list)
                            (port: int) (httpClient: HttpClient) : Task<TestResponse> = task {
        let url = $"http://127.0.0.1:{port}{path}"
        let msg = new HttpRequestMessage(HttpMethod(method'), url)
        for (k, v) in headers do
            msg.Headers.TryAddWithoutValidation(k, v) |> ignore
        match body with
        | Some b -> msg.Content <- new StringContent(b, Encoding.UTF8)
        | None -> ()
        let! response = httpClient.SendAsync(msg)
        let! bodyStr = response.Content.ReadAsStringAsync()
        let responseHeaders =
            [ for h in response.Headers do
                for v in h.Value do
                    yield (h.Key, v)
              for h in response.Content.Headers do
                for v in h.Value do
                    yield (h.Key, v) ]
        return {
            Status = int response.StatusCode
            Headers = responseHeaders
            Body = bodyStr
        }
    }

    let private request (method': string) (path: string) (body: string option) (client: TestClient) : Task<TestResponse> =
        match client.Mode with
        | Direct (trie, config) ->
            directRequest method' path body client.DefaultHeaders trie config
        | Integration (port, _, httpClient) ->
            httpRequest method' path body client.DefaultHeaders port httpClient

    let get (path: string) (client: TestClient) : Task<TestResponse> =
        request "GET" path None client

    let post (path: string) (body: string) (client: TestClient) : Task<TestResponse> =
        request "POST" path (Some body) client

    let put (path: string) (body: string) (client: TestClient) : Task<TestResponse> =
        request "PUT" path (Some body) client

    let delete (path: string) (client: TestClient) : Task<TestResponse> =
        request "DELETE" path None client

    let stop (client: TestClient) : Task =
        match client.Mode with
        | Direct _ -> Task.CompletedTask
        | Integration (_, stopFn, httpClient) ->
            httpClient.Dispose()
            stopFn()
```

Add `TestClient.fs` to `src/Fire/Fire.fsproj` after `OpenApi.fs` (before `Cors.fs`).

**Step 3: Run tests, commit**

```bash
git commit -m "feat: add TestClient with direct and HTTP integration modes"
```

---

### Task 3: Composable Validation

**Files:**
- Create: `src/Fire/Validate.fs`
- Create: `tests/Fire.Tests/ValidateTests.fs`

**Step 1: Write failing tests**

Create `tests/Fire.Tests/ValidateTests.fs`:

```fsharp
module Fire.Tests.ValidateTests

open Xunit
open FsUnit.Xunit
open Fire

type CreateUser = { Name: string; Email: string }

[<Fact>]
let ``Validate.required fails on empty string`` () =
    let v = Validate.required "name" (fun (u: CreateUser) -> u.Name)
    let result = v { Name = ""; Email = "a@b.com" }
    result |> should equal (Error ["name is required"])

[<Fact>]
let ``Validate.required passes on non-empty string`` () =
    let v = Validate.required "name" (fun (u: CreateUser) -> u.Name)
    let result = v { Name = "Alice"; Email = "a@b.com" }
    result |> should equal (Ok { Name = "Alice"; Email = "a@b.com" })

[<Fact>]
let ``Validate.minLength fails when too short`` () =
    let v = Validate.minLength "name" 3 (fun (u: CreateUser) -> u.Name)
    let result = v { Name = "Al"; Email = "a@b.com" }
    result |> should equal (Error ["name must be at least 3 characters"])

[<Fact>]
let ``Validate.maxLength fails when too long`` () =
    let v = Validate.maxLength "name" 5 (fun (u: CreateUser) -> u.Name)
    let result = v { Name = "Alexander"; Email = "a@b.com" }
    result |> should equal (Error ["name must be at most 5 characters"])

[<Fact>]
let ``Validate.pattern fails on non-match`` () =
    let v = Validate.pattern "email" @"^.+@.+\..+$" (fun (u: CreateUser) -> u.Email)
    let result = v { Name = "Alice"; Email = "not-an-email" }
    result |> should equal (Error ["email has invalid format"])

[<Fact>]
let ``Validate.combine collects all errors`` () =
    let v = Validate.combine [
        Validate.required "name" (fun (u: CreateUser) -> u.Name)
        Validate.minLength "email" 5 (fun (u: CreateUser) -> u.Email)
    ]
    let result = v { Name = ""; Email = "a@b" }
    match result with
    | Error errs -> errs |> List.length |> should equal 2
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``Validate.combine passes when all valid`` () =
    let v = Validate.combine [
        Validate.required "name" (fun (u: CreateUser) -> u.Name)
        Validate.required "email" (fun (u: CreateUser) -> u.Email)
    ]
    let result = v { Name = "Alice"; Email = "a@b.com" }
    result |> should equal (Ok { Name = "Alice"; Email = "a@b.com" })

[<Fact>]
let ``Validate.body returns 400 with errors on invalid JSON body`` () = task {
    let routes =
        Route.start
        |> Route.post "/users" (
            Validate.body
                (Validate.combine [
                    Validate.required "name" (fun (u: CreateUser) -> u.Name)
                ])
                (fun user -> task {
                    return Response.json {| name = user.Name |} |> Response.status 201
                })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/users" """{"Name":"","Email":"a@b.com"}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "name is required"
}

[<Fact>]
let ``Validate.body calls handler on valid body`` () = task {
    let routes =
        Route.start
        |> Route.post "/users" (
            Validate.body
                (Validate.required "name" (fun (u: CreateUser) -> u.Name))
                (fun user -> task {
                    return Response.json {| name = user.Name |} |> Response.status 201
                })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/users" """{"Name":"Alice","Email":"a@b.com"}"""
    r.Status |> should equal 201
    r.Body |> should haveSubstring "Alice"
}
```

Add `ValidateTests.fs` to test fsproj after `TestClientTests.fs`.

**Step 2: Implement Validate.fs**

Create `src/Fire/Validate.fs`:

```fsharp
namespace Fire

open System.Text.Json
open System.Text.RegularExpressions

type Validator<'T> = 'T -> Result<'T, string list>

[<RequireQualifiedAccess>]
module Validate =

    let required (field: string) (getter: 'T -> string) : Validator<'T> =
        fun value ->
            if System.String.IsNullOrWhiteSpace(getter value) then
                Error [$"{field} is required"]
            else
                Ok value

    let minLength (field: string) (len: int) (getter: 'T -> string) : Validator<'T> =
        fun value ->
            if (getter value).Length < len then
                Error [$"{field} must be at least {len} characters"]
            else
                Ok value

    let maxLength (field: string) (len: int) (getter: 'T -> string) : Validator<'T> =
        fun value ->
            if (getter value).Length > len then
                Error [$"{field} must be at most {len} characters"]
            else
                Ok value

    let pattern (field: string) (regex: string) (getter: 'T -> string) : Validator<'T> =
        fun value ->
            if Regex.IsMatch(getter value, regex) then
                Ok value
            else
                Error [$"{field} has invalid format"]

    let combine (validators: Validator<'T> list) : Validator<'T> =
        fun value ->
            let errors =
                validators
                |> List.collect (fun v ->
                    match v value with
                    | Error errs -> errs
                    | Ok _ -> [])
            if errors.IsEmpty then Ok value
            else Error errors

    let body<'T> (validator: Validator<'T>) (handler: 'T -> System.Threading.Tasks.Task<Response>) : Handler =
        fun req -> task {
            let! value = req.Json<'T>()
            match validator value with
            | Ok validated ->
                return! handler validated
            | Error errors ->
                return
                    Response.json {| errors = errors |}
                    |> Response.status 400
        }

    // --- String-level rule helpers (for query/params/headers validation) ---

    type Rule = string -> string option -> string list  // fieldName -> value option -> errors

    let isRequired : Rule =
        fun field value ->
            match value with
            | None | Some "" -> [$"{field} is required"]
            | _ -> []

    let isInt : Rule =
        fun field value ->
            match value with
            | None -> []
            | Some v ->
                match System.Int32.TryParse(v) with
                | true, _ -> []
                | false, _ -> [$"{field} must be an integer"]

    let isMinLength (len: int) : Rule =
        fun field value ->
            match value with
            | None -> []
            | Some v when v.Length < len -> [$"{field} must be at least {len} characters"]
            | _ -> []

    let isMaxLength (len: int) : Rule =
        fun field value ->
            match value with
            | None -> []
            | Some v when v.Length > len -> [$"{field} must be at most {len} characters"]
            | _ -> []

    // --- Source-specific validators ---

    let query (rules: (string * Rule) list) (handler: Request -> System.Threading.Tasks.Task<Response>) : Handler =
        fun req -> task {
            let errors =
                rules |> List.collect (fun (field, rule) ->
                    rule field (req.QueryParam field))
            if errors.IsEmpty then
                return! handler req
            else
                return Response.json {| errors = errors |} |> Response.status 400
        }

    let param (rules: (string * Rule) list) (handler: Request -> System.Threading.Tasks.Task<Response>) : Handler =
        fun req -> task {
            let errors =
                rules |> List.collect (fun (field, rule) ->
                    let value =
                        match req.Params.TryGetValue(field) with
                        | true, v -> Some v
                        | false, _ -> None
                    rule field value)
            if errors.IsEmpty then
                return! handler req
            else
                return Response.json {| errors = errors |} |> Response.status 400
        }

    let headerValues (rules: (string * Rule) list) (handler: Request -> System.Threading.Tasks.Task<Response>) : Handler =
        fun req -> task {
            let errors =
                rules |> List.collect (fun (field, rule) ->
                    rule field (req.Header field))
            if errors.IsEmpty then
                return! handler req
            else
                return Response.json {| errors = errors |} |> Response.status 400
        }
```

Add `Validate.fs` to `src/Fire/Fire.fsproj` after `TestClient.fs` (before `Cors.fs`).

**Step 3: Run tests, commit**

Note: also add these tests to ValidateTests.fs:

```fsharp
[<Fact>]
let ``Validate.query returns 400 when required query param missing`` () = task {
    let routes =
        Route.start
        |> Route.get "/search" (
            Validate.query [
                "q", Validate.isRequired
            ] (fun req -> task {
                return Response.text (req.QueryParam "q" |> Option.defaultValue "")
            })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/search"
    r.Status |> should equal 400
    r.Body |> should haveSubstring "q is required"
}

[<Fact>]
let ``Validate.query passes with valid params`` () = task {
    let routes =
        Route.start
        |> Route.get "/search" (
            Validate.query [
                "q", Validate.isRequired
            ] (fun req -> task {
                return Response.text (req.QueryParam "q" |> Option.defaultValue "")
            })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/search?q=fire"
    r.Status |> should equal 200
    r.Body |> should equal "fire"
}

[<Fact>]
let ``Validate.param validates route params`` () = task {
    let routes =
        Route.start
        |> Route.get "/users/:id" (
            Validate.param [
                "id", Validate.isInt
            ] (fun req -> task {
                return Response.text req.Params.["id"]
            })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/users/abc"
    r.Status |> should equal 400
    r.Body |> should haveSubstring "id must be an integer"
}

[<Fact>]
let ``Validate.headerValues validates headers`` () = task {
    let routes =
        Route.start
        |> Route.get "/api" (
            Validate.headerValues [
                "X-API-Key", Validate.isRequired
            ] (fun req -> task {
                return Response.ok
            })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/api"
    r.Status |> should equal 400
    r.Body |> should haveSubstring "X-API-Key is required"
}
```

```bash
git commit -m "feat: add composable validation for body, query, params, and headers"
```

---

### Task 4: JWT Authentication

**Files:**
- Modify: `src/Fire/Fire.fsproj` (add NuGet dep)
- Create: `src/Fire/Jwt.fs`
- Create: `tests/Fire.Tests/JwtTests.fs`

**Step 1: Add NuGet dependency**

```bash
dotnet add src/Fire package Microsoft.IdentityModel.JsonWebTokens
```

**Step 2: Write failing tests**

Create `tests/Fire.Tests/JwtTests.fs`:

```fsharp
module Fire.Tests.JwtTests

open System
open System.Collections.Generic
open System.Security.Claims
open System.Text
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens
open Xunit
open FsUnit.Xunit
open Fire

let testSecret = "this-is-a-test-secret-key-at-least-32-chars!!"

let generateToken (secret: string) (claims: (string * string) list) =
    let handler = JsonWebTokenHandler()
    let key = SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
    let descriptor = SecurityTokenDescriptor(
        SigningCredentials = SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        Expires = DateTime.UtcNow.AddHours(1.0)
    )
    let identity = ClaimsIdentity()
    for (k, v) in claims do
        identity.AddClaim(Claim(k, v))
    descriptor.Subject <- identity
    handler.CreateToken(descriptor)

[<Fact>]
let ``Jwt.validate allows request with valid token`` () = task {
    let token = generateToken testSecret ["sub", "user-1"]
    let jwtMw = Jwt.defaults testSecret |> Jwt.validate
    let routes =
        Route.start
        |> Route.middleware jwtMw
        |> Route.get "/me" (fun req -> task {
            let claims = Jwt.claims req
            let sub = claims.Value.["sub"]
            return Response.text sub
        })
    let client =
        TestClient.create routes
        |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r = client |> TestClient.get "/me"
    r.Status |> should equal 200
    r.Body |> should equal "user-1"
}

[<Fact>]
let ``Jwt.validate rejects request without token`` () = task {
    let jwtMw = Jwt.defaults testSecret |> Jwt.validate
    let routes =
        Route.start
        |> Route.middleware jwtMw
        |> Route.get "/me" (fun _ -> task { return Response.ok })
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/me"
    r.Status |> should equal 401
}

[<Fact>]
let ``Jwt.validate rejects request with invalid token`` () = task {
    let token = generateToken "wrong-secret-key-that-is-at-least-32-chars!!" ["sub", "hacker"]
    let jwtMw = Jwt.defaults testSecret |> Jwt.validate
    let routes =
        Route.start
        |> Route.middleware jwtMw
        |> Route.get "/me" (fun _ -> task { return Response.ok })
    let client =
        TestClient.create routes
        |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r = client |> TestClient.get "/me"
    r.Status |> should equal 401
}

[<Fact>]
let ``Jwt.validate with issuer rejects wrong issuer`` () = task {
    let handler = JsonWebTokenHandler()
    let key = SymmetricSecurityKey(Encoding.UTF8.GetBytes(testSecret))
    let descriptor = SecurityTokenDescriptor(
        SigningCredentials = SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        Issuer = "wrong-issuer",
        Expires = DateTime.UtcNow.AddHours(1.0)
    )
    let token = handler.CreateToken(descriptor)

    let jwtMw = Jwt.defaults testSecret |> Jwt.issuer "my-app" |> Jwt.validate
    let routes =
        Route.start
        |> Route.middleware jwtMw
        |> Route.get "/me" (fun _ -> task { return Response.ok })
    let client =
        TestClient.create routes
        |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r = client |> TestClient.get "/me"
    r.Status |> should equal 401
}

[<Fact>]
let ``Jwt.claims returns None when no JWT validated`` () = task {
    let routes =
        Route.start
        |> Route.get "/public" (fun req -> task {
            let c = Jwt.claims req
            return Response.text (if c.IsNone then "no-claims" else "has-claims")
        })
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/public"
    r.Body |> should equal "no-claims"
}
```

Add `JwtTests.fs` to test fsproj after `ValidateTests.fs`.

**Step 3: Implement Jwt.fs**

Create `src/Fire/Jwt.fs`:

```fsharp
namespace Fire

open System
open System.Collections.Generic
open System.Text
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens

type JwtConfig = {
    SigningKey: string
    EncryptionKey: string option
    Issuer: string option
    Audience: string option
}

[<RequireQualifiedAccess>]
module Jwt =

    let private claimsKey = "fire.jwt.claims"

    let defaults (signingKey: string) : JwtConfig =
        { SigningKey = signingKey; EncryptionKey = None; Issuer = None; Audience = None }

    let encryptionKey key (config: JwtConfig) = { config with EncryptionKey = Some key }
    let issuer iss (config: JwtConfig) = { config with Issuer = Some iss }
    let audience aud (config: JwtConfig) = { config with Audience = Some aud }

    let validate (config: JwtConfig) : Middleware =
        let handler = JsonWebTokenHandler()
        let signingKey = SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.SigningKey))
        let validationParams = TokenValidationParameters(
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = config.Issuer.IsSome,
            ValidateAudience = config.Audience.IsSome,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1.0)
        )
        match config.Issuer with
        | Some iss -> validationParams.ValidIssuer <- iss
        | None -> ()
        match config.Audience with
        | Some aud -> validationParams.ValidAudience <- aud
        | None -> ()
        match config.EncryptionKey with
        | Some ek ->
            validationParams.TokenDecryptionKey <- SymmetricSecurityKey(Encoding.UTF8.GetBytes(ek))
        | None -> ()

        fun next req ->
            let authHeader = req.Header "Authorization"
            match authHeader with
            | Some h when h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ->
                let token = h.Substring(7).Trim()
                task {
                    let! result = handler.ValidateTokenAsync(token, validationParams)
                    if result.IsValid then
                        // Extract claims into dictionary
                        let claims = Dictionary<string, string>()
                        for claim in result.Claims do
                            claims.[claim.Key] <-
                                match claim.Value with
                                | :? string as s -> s
                                | v -> string v
                        req.Raw.Items.[claimsKey] <- claims :> IReadOnlyDictionary<string, string>
                        return! next req
                    else
                        return
                            Response.json {| error = "invalid token" |}
                            |> Response.status 401
                }
            | _ ->
                task {
                    return
                        Response.json {| error = "missing or invalid authorization header" |}
                        |> Response.status 401
                }

    let claims (req: Request) : IReadOnlyDictionary<string, string> option =
        match req.Raw.Items.TryGetValue(claimsKey) with
        | true, value -> Some (value :?> IReadOnlyDictionary<string, string>)
        | false, _ -> None
```

Add `Jwt.fs` to `src/Fire/Fire.fsproj` after `Validate.fs` (before `Cors.fs`).

**Step 4: Run tests, commit**

```bash
git commit -m "feat: add JWT authentication middleware with JWS and JWE support"
```

---

### Task 5: Tier 4 Smoke Test

**Files:**
- Create: `tests/Fire.Tests/Tier4SmokeTests.fs`

**Step 1: Write smoke test**

Create `tests/Fire.Tests/Tier4SmokeTests.fs`:

```fsharp
module Fire.Tests.Tier4SmokeTests

open System
open System.Security.Claims
open System.Text
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens
open Xunit
open FsUnit.Xunit
open Fire

type NewUser = { Name: string; Email: string }

let secret = "tier4-smoke-test-secret-at-least-32-chars!!"

let makeToken (sub: string) =
    let handler = JsonWebTokenHandler()
    let key = SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
    let descriptor = SecurityTokenDescriptor(
        SigningCredentials = SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        Subject = ClaimsIdentity([| Claim("sub", sub) |]),
        Expires = DateTime.UtcNow.AddHours(1.0)
    )
    handler.CreateToken(descriptor)

[<Fact>]
let ``Tier 4 integration smoke test`` () = task {
    let jwtMw = Jwt.defaults secret |> Jwt.validate

    let routes =
        Route.start
        |> Route.get "/public" (fun _ -> task { return Response.text "open" })
        |> Route.group "/api" (fun api ->
            api
            |> Route.middleware jwtMw
            |> Route.get "/me" (fun req -> task {
                let claims = Jwt.claims req
                return Response.json {| sub = claims.Value.["sub"] |}
            })
            |> Route.post "/users" (
                Validate.body
                    (Validate.combine [
                        Validate.required "name" (fun (u: NewUser) -> u.Name)
                        Validate.minLength "email" 5 (fun (u: NewUser) -> u.Email)
                    ])
                    (fun user -> task {
                        return Response.json {| name = user.Name |} |> Response.status 201
                    })
            )
        )

    // Direct test client
    let client = TestClient.create routes

    // Public route — no auth needed
    let! r1 = client |> TestClient.get "/public"
    r1.Status |> should equal 200
    r1.Body |> should equal "open"

    // Protected route — no token
    let! r2 = client |> TestClient.get "/api/me"
    r2.Status |> should equal 401

    // Protected route — valid token
    let token = makeToken "user-42"
    let authed = client |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r3 = authed |> TestClient.get "/api/me"
    r3.Status |> should equal 200
    r3.Body |> should haveSubstring "user-42"

    // Validation — invalid body
    let! r4 = authed |> TestClient.post "/api/users" """{"Name":"","Email":"ab"}"""
    r4.Status |> should equal 400
    r4.Body |> should haveSubstring "name is required"

    // Validation — valid body
    let! r5 = authed |> TestClient.post "/api/users" """{"Name":"Alice","Email":"alice@example.com"}"""
    r5.Status |> should equal 201
    r5.Body |> should haveSubstring "Alice"
}
```

Add `Tier4SmokeTests.fs` to test fsproj after `Tier3SmokeTests.fs`.

**Step 2: Run all tests, commit**

```bash
git commit -m "test: add Tier 4 integration smoke test"
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
<Compile Include="TestClient.fs" />
<Compile Include="Validate.fs" />
<Compile Include="Jwt.fs" />
<Compile Include="Cors.fs" />
<Compile Include="App.fs" />
```

**tests/Fire.Tests/Fire.Tests.fsproj — add after existing entries:**
```xml
<Compile Include="TestClientTests.fs" />
<Compile Include="ValidateTests.fs" />
<Compile Include="JwtTests.fs" />
<Compile Include="Tier4SmokeTests.fs" />
```
