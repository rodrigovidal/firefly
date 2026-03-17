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

    let validateUser = Validate.combine [
        Validate.required "name" (fun (u: NewUser) -> u.Name)
        Validate.minLength "email" 5 (fun (u: NewUser) -> u.Email)
    ]

    let routes =
        Route.start
        |> Route.get "/public" (fun _ -> task { return Response.text "open" })
        |> Route.group "/api" (fun api ->
            api
            |> Route.middleware jwtMw
            |> Route.get "/me" (fun (req: Request) -> task {
                let claims = Jwt.claims req
                return Response.json {| sub = claims.Value.["sub"] |}
            })
            |> Route.post "/users" (fun (req: Request) -> task {
                let! body = req.Json<NewUser>()
                match validateUser body with
                | Ok user ->
                    return Response.json {| name = user.Name |} |> Response.status 201
                | Error errors ->
                    return Response.json {| errors = errors |} |> Response.status 400
            })
        )

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
