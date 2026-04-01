# Fireproof — Authentication for Fire

## Overview

Auth library for the Fire ecosystem. Handles cookie-based sessions, password hashing, OAuth providers, and route protection middleware. Does not own user storage — the app implements Fireproof's interfaces with whatever database it uses.

## Interfaces

Fireproof defines three interfaces. The app implements them with Rhinox, Dapper, EF, Redis, in-memory — whatever it has.

### IUserStore

```fsharp
type AuthUser = {
    Id: string
    Email: string
    PasswordHash: string option  // None for OAuth-only users
    Roles: string list
    Claims: (string * string) list
}

type NewUser = {
    Email: string
    PasswordHash: string option
    Roles: string list
    Claims: (string * string) list
}

type IUserStore =
    abstract FindByEmail: email: string -> Task<AuthUser option>
    abstract FindById: id: string -> Task<AuthUser option>
    abstract Create: user: NewUser -> Task<AuthUser>
    abstract UpdatePassword: userId: string * passwordHash: string -> Task<unit>
```

### ITokenStore

For email verification and password reset tokens.

```fsharp
type ITokenStore =
    abstract Save: key: string * token: string * expiry: TimeSpan -> Task<unit>
    abstract Get: key: string -> Task<string option>
    abstract Delete: key: string -> Task<unit>
```

### ISessionStore

For server-side sessions with revocation support.

```fsharp
type ISessionStore =
    abstract Create: sessionId: string * identity: Identity * expiry: TimeSpan -> Task<unit>
    abstract Get: sessionId: string -> Task<Identity option>
    abstract Delete: sessionId: string -> Task<unit>
    abstract DeleteAllForUser: userId: string -> Task<unit>
```

### Registration

```fsharp
App.services [
    Service.scoped<IUserStore, RhinoxUserStore>
    Service.scoped<ISessionStore, RhinoxSessionStore>
    Service.singleton<ITokenStore, RedisTokenStore>
]
```

## Modules

### Auth — session management and middleware

#### Identity

The identity stored in the session:

```fsharp
type Identity = {
    Id: string
    Roles: string list
    Claims: (string * string) list
}
```

#### Configuration

```fsharp
let auth = Auth.config "my-secret-key-at-least-32-chars" {
    cookieName "_session"
    expiry (TimeSpan.FromDays 7.0)
    loginPath "/login"
    secureCookie true
    github { clientId "abc123"; clientSecret "xyz789" }
    google { clientId "abc123"; clientSecret "xyz789" }
}
```

#### Login / Logout

```fsharp
// Login — creates session, sets cookie
Auth.login auth identity req  // Response with Set-Cookie

// Logout — deletes session, clears cookie
Auth.logout auth req  // Response clearing cookie

// Logout everywhere — deletes all sessions for user
Auth.logoutAll auth userId req

// Get current user from request
Auth.currentUser req  // Identity option
```

#### Middleware

```fsharp
// Require any authenticated user — 401 if not
Route.middleware (Auth.requireLogin auth)

// Require specific role — 403 if missing
Route.middleware (Auth.requireRole auth "admin")

// Require specific claim — 403 if missing
Route.middleware (Auth.requireClaim auth "org" "acme")

// Custom predicate — 403 if false
Route.middleware (Auth.require auth (fun identity ->
    identity.Roles |> List.contains "editor"))
```

#### Full example

```fsharp
Route.post "/signup" (fun (req: Request) (users: IUserStore) -> task {
    let! body = req.Json<{| email: string; password: string |}>()
    let hash = Password.hash body.password
    let! user = users.Create { Email = body.email; PasswordHash = Some hash; Roles = []; Claims = [] }
    return Auth.login auth { Id = user.Id; Roles = []; Claims = [] } req
})

Route.post "/login" (fun (req: Request) (users: IUserStore) -> task {
    let! body = req.Json<{| email: string; password: string |}>()
    match! users.FindByEmail(body.email) with
    | Some user when user.PasswordHash.IsSome && Password.verify body.password user.PasswordHash.Value ->
        return Auth.login auth { Id = user.Id; Roles = user.Roles; Claims = user.Claims } req
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

Uses bcrypt via `BCrypt.Net-Next`. Constant-time comparison.

### OAuth — provider flows

#### Configuration

Providers are added in the auth config:

```fsharp
let auth = Auth.config "secret" {
    cookieName "_session"
    expiry (TimeSpan.FromDays 7.0)
    github { clientId "abc"; clientSecret "xyz" }
    google { clientId "abc"; clientSecret "xyz" }
}
```

#### OAuth profile

```fsharp
type OAuthProfile = {
    Provider: string
    ProviderId: string
    Email: string option
    Name: string option
    AvatarUrl: string option
    Raw: Map<string, string>
}
```

#### Routes

```fsharp
Route.group "/auth" (Auth.oauthRoutes auth (fun profile (users: IUserStore) -> task {
    match! users.FindByEmail(profile.Email |> Option.defaultValue "") with
    | Some user -> return { Id = user.Id; Roles = user.Roles; Claims = user.Claims }
    | None ->
        let! user = users.Create {
            Email = profile.Email |> Option.defaultValue ""
            PasswordHash = None
            Roles = []
            Claims = [("provider", profile.Provider); ("provider_id", profile.ProviderId)]
        }
        return { Id = user.Id; Roles = []; Claims = [] }
}))

// Auto-generates:
// GET /auth/github          → redirect to GitHub authorization
// GET /auth/github/callback → exchange code, fetch profile, call your function, set session
// GET /auth/google          → redirect to Google authorization
// GET /auth/google/callback → exchange code, fetch profile, call your function, set session
```

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

### Email verification

Requires ITokenStore and IUserStore.

```fsharp
// On signup — generate token, send email
Route.post "/signup" (fun (req: Request) (users: IUserStore) (tokens: ITokenStore) -> task {
    let! body = req.Json<{| email: string; password: string |}>()
    let! user = users.Create { Email = body.email; PasswordHash = Some (Password.hash body.password); Roles = []; Claims = [] }
    let! token = Auth.createVerificationToken tokens user.Id
    // Send email with link: /verify?token={token}
    do! sendVerificationEmail body.email token
    return Response.json {| message = "Check your email" |} |> Response.status 201
})

// On click — verify token
Route.get "/verify" (fun (req: Request) (users: IUserStore) (tokens: ITokenStore) -> task {
    match req.QueryParam "token" with
    | Some token ->
        match! Auth.verifyToken tokens token with
        | Some userId ->
            // Mark user as verified in your store
            return Response.json {| verified = true |}
        | None ->
            return Response.json {| error = "invalid or expired token" |} |> Response.status 400
    | None ->
        return Response.json {| error = "missing token" |} |> Response.status 400
})
```

### Password reset

Requires ITokenStore and IUserStore.

```fsharp
// Request reset
Route.post "/forgot-password" (fun (req: Request) (users: IUserStore) (tokens: ITokenStore) -> task {
    let! body = req.Json<{| email: string |}>()
    match! users.FindByEmail(body.email) with
    | Some user ->
        let! token = Auth.createResetToken tokens user.Id
        do! sendResetEmail body.email token
    | None -> ()  // don't reveal if email exists
    return Response.json {| message = "If that email exists, we sent a reset link" |}
})

// Execute reset
Route.post "/reset-password" (fun (req: Request) (users: IUserStore) (tokens: ITokenStore) -> task {
    let! body = req.Json<{| token: string; password: string |}>()
    match! Auth.verifyToken tokens body.token with
    | Some userId ->
        do! users.UpdatePassword(userId, Password.hash body.password)
        return Response.json {| reset = true |}
    | None ->
        return Response.json {| error = "invalid or expired token" |} |> Response.status 400
})
```

## Implementation

### Cookie encryption

Session ID stored in cookie. The session ID maps to an Identity via ISessionStore.

For stateless mode (no ISessionStore), Identity is encrypted directly in the cookie with AES-256-GCM.

### OAuth flow

1. User visits `/auth/github`
2. Fireproof generates random `state`, stores in short-lived cookie
3. Redirects to GitHub authorization URL
4. GitHub redirects to `/auth/github/callback?code=...&state=...`
5. Fireproof verifies `state`, exchanges `code` for token via Flare
6. Fetches profile from GitHub API via Flare
7. Calls app's callback with OAuthProfile and IUserStore (via DI)
8. App returns Identity
9. Fireproof creates session (via ISessionStore), sets cookie
10. Redirects to `/`

### Dependencies

```
BCrypt.Net-Next  — password hashing
Flare            — OAuth HTTP calls
Fire             — middleware, Request, Response, Cookie
```

### Project structure

```
src/Fireproof/
  Types.fs         — Identity, AuthUser, NewUser, OAuthProfile
  Interfaces.fs    — IUserStore, ITokenStore, ISessionStore
  Password.fs      — hash, verify
  Crypto.fs        — AES-256-GCM encrypt/decrypt
  Auth.fs          — config, login, logout, currentUser, middleware, token helpers
  OAuth.fs         — provider configs, route generation, flow
  Providers/
    GitHub.fs
    Google.fs
```

### What Fireproof does NOT do

- Own user tables or migrate databases — the app implements IUserStore
- Send emails — the app calls its own email service
- Rate limit login attempts — use Fire.RateLimit
- CSRF protection — use Fire.Csrf
- JWT — use Fire.Jwt (Fireproof is cookie-based)
