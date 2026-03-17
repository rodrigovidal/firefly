# Tier 4 Features Design

Tier 4 covers ecosystem features: NuGet packaging setup, testing helpers, composable validation, and JWT authentication. Template deferred.

## 1. NuGet Packaging (setup only, no publish)

Add package metadata to `src/Fire/Fire.fsproj`. Create LICENSE (MIT) and README.md at repo root.

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

## 2. Testing Helpers

Two modes with shared API surface:

```fsharp
module TestClient =
    let create (routes: RouteTable) : TestClient                           // direct, no HTTP
    let createWith (routes: RouteTable) (config: FireConfig) : TestClient  // direct + global middleware
    let start (routes: RouteTable) (config: FireConfig) : Task<TestClient> // real HTTP

    let get (path: string) (client: TestClient) : Task<TestResponse>
    let post (path: string) (body: string) (client: TestClient) : Task<TestResponse>
    let put (path: string) (body: string) (client: TestClient) : Task<TestResponse>
    let delete (path: string) (client: TestClient) : Task<TestResponse>
    let withHeader (key: string) (value: string) (client: TestClient) : TestClient
    let stop (client: TestClient) : Task
```

Direct mode builds `DefaultHttpContext`, runs through trie + middleware, captures Response. `TestResponse` has Status, Headers, Body (string).

## 3. Composable Validation

```fsharp
type Validator<'T> = 'T -> Result<'T, string list>

module Validate =
    let required (field: string) (getter: 'T -> string) : Validator<'T>
    let minLength (field: string) (len: int) (getter: 'T -> string) : Validator<'T>
    let maxLength (field: string) (len: int) (getter: 'T -> string) : Validator<'T>
    let pattern (field: string) (regex: string) (getter: 'T -> string) : Validator<'T>
    let combine (validators: Validator<'T> list) : Validator<'T>
    let body (validator: Validator<'T>) (handler: 'T -> Task<Response>) : Handler
```

`combine` runs all validators, collects all errors. `body` deserializes JSON, validates, returns 400 with `{"errors":[...]}` or calls handler.

## 4. JWT Authentication (JWS + JWE)

Uses `Microsoft.IdentityModel.JsonWebTokens` for both signed and encrypted tokens.

```fsharp
type JwtConfig = {
    SigningKey: string
    EncryptionKey: string option
    Issuer: string option
    Audience: string option
}

module Jwt =
    let defaults (signingKey: string) : JwtConfig
    let encryptionKey key config : JwtConfig
    let issuer iss config : JwtConfig
    let audience aud config : JwtConfig
    let validate (config: JwtConfig) : Middleware
    let claims (req: Request) : IReadOnlyDictionary<string, string> option
```

Claims stored in `HttpContext.Items["fire.jwt.claims"]`. Returns 401 on invalid/missing token.

## File Changes

**New files:** `src/Fire/TestClient.fs`, `src/Fire/Validate.fs`, `src/Fire/Jwt.fs`, `LICENSE`, `README.md`
**Modified:** `src/Fire/Fire.fsproj` (package metadata + JWT NuGet dep)
**NuGet dependency:** `Microsoft.IdentityModel.JsonWebTokens`
