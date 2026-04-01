# Fireproof — Authentication for Fire

## Overview

Auth library for the Fire ecosystem. Handles cookie-based sessions, password hashing, OAuth providers, and route protection middleware. Does not own user storage — the app provides user lookup and Fireproof handles the auth protocol.

## Modules

### Auth — cookie-based sessions

```fsharp
open Fireproof

// Configure
let auth = Auth.config "my-secret-key-at-least-32-chars" {
    cookieName "_session"
    expiry (TimeSpan.FromDays 7.0)
    loginPath "/login"
    secureCookie true
}
```

#### Identity

The identity stored in the cookie:

```fsharp
type Identity = {
    Id: string
    Roles: string list
    Claims: (string * string) list
}
```

#### Login / Logout

```fsharp
// Login — sets encrypted cookie, returns redirect or JSON
Auth.login auth identity req  // returns Response with Set-Cookie

// Logout — clears the cookie
Auth.logout auth req  // returns Response clearing the cookie

// Get current user from request (set by requireLogin middleware)
Auth.currentUser req  // Identity option
```

#### Middleware

```fsharp
// Require any authenticated user
Route.middleware (Auth.requireLogin auth)

// Require specific role
Route.middleware (Auth.requireRole auth "admin")

// Require specific claim
Route.middleware (Auth.requireClaim auth "org" "acme")

// Custom predicate
Route.middleware (Auth.require auth (fun identity -> identity.Roles |> List.contains "editor"))
```

All auth middleware return 401 if not authenticated, 403 if authenticated but unauthorized.

#### Full example

```fsharp
Route.post "/login" (fun (req: Request) -> task {
    let! body = req.Json<{| email: string; password: string |}>()
    match! store.FindByEmail(body.email) with
    | Some user when Password.verify body.password user.PasswordHash ->
        return Auth.login auth { Id = string user.Id; Roles = user.Roles; Claims = [] } req
    | _ ->
        return Response.json {| error = "invalid credentials" |} |> Response.status 401
})

Route.post "/logout" (fun (req: Request) -> task {
    return Auth.logout auth req
})

Route.group "/api" (fun api ->
    api
    |> Route.middleware (Auth.requireLogin auth)
    |> Route.get "/me" (fun (req: Request) -> task {
        let user = Auth.currentUser req |> Option.get
        return Response.json {| id = user.Id; roles = user.Roles |}
    })
    |> Route.group "/admin" (fun admin ->
        admin
        |> Route.middleware (Auth.requireRole auth "admin")
        |> Route.get "/users" listAllUsers
    )
)
```

### Password — hashing and verification

```fsharp
// Hash a password (bcrypt, cost factor 12)
let hash = Password.hash "my-password"
// "$2a$12$..."

// Verify a password against a hash
Password.verify "my-password" hash  // true
Password.verify "wrong" hash        // false
```

Uses bcrypt via `BCrypt.Net-Next`. Constant-time comparison to prevent timing attacks.

### OAuth — provider flows

#### Configuration

```fsharp
let auth = Auth.config "my-secret-key" {
    cookieName "_session"
    expiry (TimeSpan.FromDays 7.0)
    loginPath "/login"
    github { clientId "abc123"; clientSecret "xyz789" }
    google { clientId "abc123"; clientSecret "xyz789" }
}
```

#### OAuth profile

```fsharp
type OAuthProfile = {
    Provider: string        // "github", "google", etc.
    ProviderId: string      // user ID from the provider
    Email: string option
    Name: string option
    AvatarUrl: string option
    Raw: Map<string, string>  // all fields from the provider
}
```

#### Routes

```fsharp
// Register OAuth routes — the callback receives the profile
Route.group "/auth" (Auth.oauthRoutes auth (fun profile -> task {
    // Find or create user from OAuth profile
    let! user = store.FindOrCreateByOAuth(profile.Provider, profile.ProviderId, profile.Email)
    return { Id = string user.Id; Roles = user.Roles; Claims = [] }
}))

// Auto-generates:
// GET /auth/github          → redirect to GitHub authorization
// GET /auth/github/callback → exchange code, fetch profile, call your function, set cookie
// GET /auth/google          → redirect to Google authorization
// GET /auth/google/callback → exchange code, fetch profile, call your function, set cookie
```

OAuth token exchange and profile fetching uses Flare internally.

#### Custom providers

```fsharp
let auth = Auth.config "secret" {
    oauth "gitlab" {
        clientId "abc"
        clientSecret "xyz"
        authorizeUrl "https://gitlab.com/oauth/authorize"
        tokenUrl "https://gitlab.com/oauth/token"
        profileUrl "https://gitlab.com/api/v4/user"
        scopes ["read_user"]
        mapProfile (fun json -> {
            ProviderId = json.["id"]
            Email = json.TryFind "email"
            Name = json.TryFind "name"
            AvatarUrl = json.TryFind "avatar_url"
        })
    }
}
```

## Implementation

### Cookie encryption

Identity is serialized to JSON, then encrypted with AES-256-GCM using the secret key. The encrypted blob is base64url-encoded and stored in the cookie. On each request, the middleware decrypts and deserializes.

```fsharp
// Encrypt: Identity → JSON → AES-256-GCM → base64url → cookie value
// Decrypt: cookie value → base64url → AES-256-GCM → JSON → Identity
```

### OAuth flow

1. User visits `/auth/github`
2. Fireproof generates a random `state` parameter, stores in a short-lived cookie
3. Redirects to `https://github.com/login/oauth/authorize?client_id=...&state=...&redirect_uri=...`
4. GitHub redirects back to `/auth/github/callback?code=...&state=...`
5. Fireproof verifies `state`, exchanges `code` for access token via Flare
6. Fetches user profile from GitHub API via Flare
7. Calls the app's callback with `OAuthProfile`
8. App returns `Identity`, Fireproof sets the encrypted session cookie
9. Redirects to `/` (or a configured post-login URL)

### Dependencies

```
BCrypt.Net-Next  — password hashing
Flare            — OAuth HTTP calls (token exchange, profile fetch)
Fire             — middleware types, Request, Response, Cookie
```

### Project structure

```
src/Fireproof/
  Auth.fs          — config, login, logout, currentUser, middleware
  Password.fs      — hash, verify
  OAuth.fs         — provider configs, route generation, flow handling
  Crypto.fs        — AES-256-GCM encrypt/decrypt for cookie
  Providers/
    GitHub.fs      — GitHub-specific config and profile mapping
    Google.fs      — Google-specific config and profile mapping
```

### What Fireproof does NOT do

- User storage / user records (app's responsibility)
- Email verification / password reset flows (app's responsibility)
- JWT generation (Fire.Jwt already handles this)
- Rate limiting on login (Fire.RateLimit already handles this)
- CSRF protection (Fire.Csrf already handles this)
