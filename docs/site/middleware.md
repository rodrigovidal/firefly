# Middleware

Fire includes 15+ built-in middleware modules. A middleware is a function that wraps a handler:

```fsharp
type Middleware = Handler -> Handler
// Which expands to:
// (Request -> Task<Response>) -> (Request -> Task<Response>)
```

Apply middleware globally via `App.middleware` or per-route via `Route.middleware`.

## CORS

Cross-Origin Resource Sharing support with configurable origins, methods, and headers.

```fsharp
// Allow all origins
App.defaults |> App.middleware Cors.allowAll

// Configured
let cors =
    Cors.defaults
    |> Cors.origins ["https://myapp.com"; "https://staging.myapp.com"]
    |> Cors.methods ["GET"; "POST"; "PUT"; "DELETE"]
    |> Cors.headers ["Authorization"; "Content-Type"]
    |> Cors.maxAge 3600
    |> Cors.build

App.defaults |> App.middleware cors
```

The middleware handles preflight `OPTIONS` requests automatically, returning 204 with the appropriate `Access-Control-*` headers. Non-preflight requests get `Access-Control-Allow-Origin` added to the response.

## JWT Authentication

Validates JWT tokens from the `Authorization: Bearer <token>` header.

```fsharp
let jwtConfig =
    Jwt.defaults "your-256-bit-secret"
    |> Jwt.issuer "my-app"
    |> Jwt.audience "my-api"

// Apply to protected routes
Route.start
|> Route.group "/api" (fun t ->
    t
    |> Route.middleware (Jwt.validate jwtConfig)
    |> Route.get "/profile" (fun (req: Request) -> task {
        let claims = Jwt.claims req
        match claims with
        | Some c -> return Response.json {| sub = c.["sub"] |}
        | None -> return Response.unauthorized
    })
)
```

Supports optional encryption key for encrypted JWTs:

```fsharp
Jwt.defaults "signing-key"
|> Jwt.encryptionKey "encryption-key"
```

Returns 401 with `{ "error": "invalid token" }` on validation failure and `{ "error": "missing or invalid authorization header" }` when the header is absent.

## Rate Limiting

Fixed-window rate limiting with configurable key function:

```fsharp
// 100 requests per minute, keyed by IP address
let rateLimiter =
    RateLimit.fixedWindow 100 (TimeSpan.FromMinutes 1.0) RateLimit.byIp

App.defaults |> App.middleware rateLimiter
```

Custom key function:

```fsharp
// Rate limit by API key header
let byApiKey (req: Request) =
    req.Header "X-Api-Key" |> Option.defaultValue "anonymous"

RateLimit.fixedWindow 1000 (TimeSpan.FromHours 1.0) byApiKey
```

Returns 429 with a `Retry-After` header when the limit is exceeded.

## Timeout

Abort requests that exceed a time limit:

```fsharp
Timeout.after (TimeSpan.FromSeconds 30.0)
```

Returns 504 Gateway Timeout if the handler does not complete in time.

## Compression

Response body compression with gzip, brotli, or auto-detection:

```fsharp
// Auto-select: brotli > gzip > none (based on Accept-Encoding)
App.defaults |> App.middleware Compress.auto

// Or choose specifically
App.defaults |> App.middleware Compress.gzip
App.defaults |> App.middleware Compress.brotli
```

The middleware inspects the `Accept-Encoding` header (with quality values) and compresses `Text` and `Json` response bodies. Stream bodies are passed through uncompressed.

## Request ID

Generates or forwards a unique request identifier:

```fsharp
App.defaults |> App.middleware RequestId.middleware
```

If the incoming request has an `X-Request-Id` header, it is forwarded. Otherwise a new GUID is generated. The ID is available via `req.RequestId` and added to the response as `X-Request-Id`.

## Correlation ID

Tracks requests across service boundaries:

```fsharp
App.defaults |> App.middleware CorrelationId.middleware
```

Works like Request ID but uses the `X-Correlation-Id` header. Available via `req.CorrelationId`.

## Secure Headers

Adds security headers similar to Helmet.js:

```fsharp
// Default secure headers
App.defaults |> App.middleware SecureHeaders.middleware
```

Default headers added:

| Header | Default Value |
|--------|--------------|
| X-Content-Type-Options | nosniff |
| X-Frame-Options | DENY |
| X-XSS-Protection | 0 |
| Referrer-Policy | strict-origin-when-cross-origin |
| Content-Security-Policy | default-src 'self' |
| Strict-Transport-Security | max-age=31536000; includeSubDomains |
| Permissions-Policy | camera=(), microphone=(), geolocation=() |

Configurable:

```fsharp
let headers =
    SecureHeaders.defaults
    |> SecureHeaders.contentSecurityPolicy "default-src 'self'; script-src 'self' https://cdn.example.com"
    |> SecureHeaders.frameOptions "SAMEORIGIN"
    |> SecureHeaders.referrerPolicy "no-referrer"
    |> SecureHeaders.noHsts  // disable HSTS (e.g. for dev)
    |> SecureHeaders.build

App.defaults |> App.middleware headers
```

## CSRF Protection

Double-submit cookie pattern for CSRF protection:

```fsharp
App.defaults |> App.middleware Csrf.middleware
```

Safe methods (GET, HEAD, OPTIONS) pass through and may set the CSRF cookie. State-changing methods (POST, PUT, PATCH, DELETE) require the token via:

- `X-CSRF-Token` header, or
- `_csrf` form field

Generate tokens in your views:

```fsharp
// Hidden form input
let formView (req: Request) =
    form [] [
        Csrf.hiddenInput req  // <input type="hidden" name="_csrf" value="...">
        input [ Attr.Type "text"; Name "email" ]
        button [] [ str "Submit" ]
    ]

// Meta tag for AJAX
let layoutView (req: Request) =
    head [] [
        Csrf.metaTag req  // <meta name="csrf-token" content="...">
    ]
```

## Session

In-memory session management using cookies:

```fsharp
App.defaults |> App.middleware Session.middleware
```

Read and write session data:

```fsharp
let handler (req: Request) = task {
    // Read
    let username = Session.get<string> "username" req

    // Write
    Session.set "username" "alice" req

    // Remove a key
    Session.remove "username" req

    // Clear entire session
    Session.clear req

    return Response.ok
}
```

For testing, use a custom store:

```fsharp
let testStore = Session.SessionStore()
App.defaults |> App.middleware (Session.withStore testStore)
```

## Idempotency

Cache responses for requests with an `Idempotency-Key` header to ensure safe retries:

```fsharp
let store = Idempotent.inMemory ()
let ttl = TimeSpan.FromMinutes 60.0

App.defaults |> App.middleware (Idempotent.middleware store ttl)
```

Only applies to POST, PUT, and PATCH. GET and DELETE pass through. When a duplicate key is detected, the cached response is returned with an `Idempotency-Replayed: true` header.

The `IdempotencyStore` is an interface you can implement for Redis or database backing:

```fsharp
type IdempotencyStore =
    abstract TryGet : key:string -> Task<CachedResponse option>
    abstract Set : key:string * response:CachedResponse * ttl:TimeSpan -> Task<unit>
```

## Health Checks

Register health check endpoints with customizable checks:

```fsharp
let healthHandler =
    Health.handler [
        Health.ping  // always healthy
        Health.check "database" (fun () -> task {
            // Check DB connectivity
            do! db.PingAsync()
        })
        Health.check "redis" (fun () -> task {
            do! redis.PingAsync()
        })
    ]

Route.start
|> Route.get "/health" healthHandler
```

Returns 200 when all checks are healthy, 503 when any check fails. Response body includes per-check status, duration, and error details.

## Response Caching

Multiple caching strategies:

```fsharp
// Public cache with max-age
Route.middleware (Cache.maxAge 3600)

// Private (user-specific) cache
Route.middleware (Cache.privateMaxAge 60)

// Disable caching
Route.middleware Cache.noStore

// Vary by headers
Route.middleware (Cache.varyBy ["Accept"; "Accept-Encoding"])

// Auto ETag with 304 Not Modified
Route.middleware Cache.etag

// Combined: max-age + ETag + Vary
Route.middleware (Cache.standard 3600 ["Accept"])
```

The `Cache.etag` middleware computes a SHA-256 hash of the response body, sets an `ETag` header, and returns 304 Not Modified when the client sends a matching `If-None-Match` header. Only applies to GET and HEAD requests with 2xx status codes.

`AutoETag.middleware` is an alias for `Cache.etag`.

## Telemetry

OpenTelemetry-compatible tracing and metrics:

```fsharp
App.defaults |> App.middleware Telemetry.middleware
```

Creates an `Activity` (span) per request with tags:

- `http.request.method`
- `url.path`
- `url.scheme`
- `http.request_id` (if present)
- `http.response.status_code`

Records metrics:

| Metric | Type | Description |
|--------|------|-------------|
| `fire.http.requests` | Counter | Total HTTP requests |
| `fire.http.duration` | Histogram | Request duration in ms |
| `fire.http.active_requests` | UpDownCounter | Currently active requests |

Configure exporters using the source name `"Fire"` and meter name `"Fire"`:

```fsharp
Telemetry.sourceName  // "Fire"
Telemetry.meterName   // "Fire"
```

## Upload Size Limit

Restrict request body size:

```fsharp
// 10 MB limit
Route.middleware (Upload.maxSize (10L * 1024L * 1024L))
```

Returns 413 Payload Too Large if the `Content-Length` exceeds the limit.

## Writing Custom Middleware

A middleware is any function matching `Handler -> Handler`:

```fsharp
let timing : Middleware =
    fun next req -> task {
        let sw = System.Diagnostics.Stopwatch.StartNew()
        let! response = next req
        sw.Stop()
        return response |> Response.header "X-Response-Time" $"{sw.ElapsedMilliseconds}ms"
    }
```

Apply it like any built-in middleware:

```fsharp
App.defaults |> App.middleware timing
// or
Route.middleware timing
```
