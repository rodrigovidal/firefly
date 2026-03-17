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
        |> Route.get "/me" (fun (req: Request) -> task {
            let claims = Jwt.claims req
            let sub = claims.Value.["sub"]
            return Response.text sub
        })
    let client = TestClient.create routes |> TestClient.withHeader "Authorization" $"Bearer {token}"
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
    let client = TestClient.create routes |> TestClient.withHeader "Authorization" $"Bearer {token}"
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
    let client = TestClient.create routes |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r = client |> TestClient.get "/me"
    r.Status |> should equal 401
}

// --- Coverage: Jwt.audience (line 26) and Jwt.encryptionKey (line 24, 42) ---

[<Fact>]
let ``Jwt.validate with audience rejects wrong audience`` () = task {
    let handler = JsonWebTokenHandler()
    let key = SymmetricSecurityKey(Encoding.UTF8.GetBytes(testSecret))
    let descriptor = SecurityTokenDescriptor(
        SigningCredentials = SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        Audience = "wrong-audience",
        Expires = DateTime.UtcNow.AddHours(1.0)
    )
    let token = handler.CreateToken(descriptor)
    let jwtMw = Jwt.defaults testSecret |> Jwt.audience "my-app" |> Jwt.validate
    let routes =
        Route.start
        |> Route.middleware jwtMw
        |> Route.get "/me" (fun _ -> task { return Response.ok })
    let client = TestClient.create routes |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r = client |> TestClient.get "/me"
    r.Status |> should equal 401
}

[<Fact>]
let ``Jwt.validate with audience accepts correct audience`` () = task {
    let handler = JsonWebTokenHandler()
    let key = SymmetricSecurityKey(Encoding.UTF8.GetBytes(testSecret))
    let descriptor = SecurityTokenDescriptor(
        SigningCredentials = SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        Audience = "my-app",
        Expires = DateTime.UtcNow.AddHours(1.0)
    )
    let identity = System.Security.Claims.ClaimsIdentity()
    identity.AddClaim(System.Security.Claims.Claim("sub", "user-1"))
    descriptor.Subject <- identity
    let token = handler.CreateToken(descriptor)
    let jwtMw = Jwt.defaults testSecret |> Jwt.audience "my-app" |> Jwt.validate
    let routes =
        Route.start
        |> Route.middleware jwtMw
        |> Route.get "/me" (fun (req: Request) -> task {
            let claims = Jwt.claims req
            return Response.text (claims.Value.["sub"])
        })
    let client = TestClient.create routes |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r = client |> TestClient.get "/me"
    r.Status |> should equal 200
    r.Body |> should equal "user-1"
}

[<Fact>]
let ``Jwt.validate with encryptionKey config still validates signed tokens`` () = task {
    // Tests that Jwt.encryptionKey setter works (line 24, 42 coverage)
    // A normal signed token should still validate even when encryption key is configured
    let encKey = "12345678901234567890123456789012" // exactly 32 chars
    let token = generateToken testSecret ["sub", "user-enc"]
    let jwtMw = Jwt.defaults testSecret |> Jwt.encryptionKey encKey |> Jwt.validate
    let routes =
        Route.start
        |> Route.middleware jwtMw
        |> Route.get "/me" (fun (req: Request) -> task {
            let claims = Jwt.claims req
            return Response.text (claims.Value.["sub"])
        })
    let client = TestClient.create routes |> TestClient.withHeader "Authorization" $"Bearer {token}"
    let! r = client |> TestClient.get "/me"
    r.Status |> should equal 200
    r.Body |> should equal "user-enc"
}

[<Fact>]
let ``Jwt.claims returns None when no JWT validated`` () = task {
    let routes =
        Route.start
        |> Route.get "/public" (fun (req: Request) -> task {
            let c = Jwt.claims req
            return Response.text (if c.IsNone then "no-claims" else "has-claims")
        })
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/public"
    r.Body |> should equal "no-claims"
}
