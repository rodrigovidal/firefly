# Tier 3 Features Design

Tier 3 covers four production readiness features: graceful shutdown, request timeout, rate limiting, and OpenAPI generation. WebSockets deferred to a later tier.

## 1. Graceful Shutdown

Add `ShutdownTimeout: TimeSpan option` to `FireConfig`. In `App.run`, configure `HostOptions.ShutdownTimeout`. Kestrel handles draining in-flight requests natively.

```fsharp
App.defaults
|> App.shutdownTimeout (TimeSpan.FromSeconds 30.0)
|> App.run routes
```

Default: .NET's 30 seconds if not set.

## 2. Request Timeout Middleware

Creates a `CancellationTokenSource` with timeout. Returns 504 if handler is cancelled.

```fsharp
module Timeout =
    let after (timeout: TimeSpan) : Middleware
```

Only works if handlers respect cancellation. Standard .NET behavior.

## 3. Rate Limiting

Fixed window algorithm with `ConcurrentDictionary`. User-provided key function with `byIp` convenience. Returns 429 with `Retry-After` header.

```fsharp
module RateLimit =
    let fixedWindow (maxRequests: int) (window: TimeSpan) (keyFunc: Request -> string) : Middleware
    let byIp : Request -> string
```

## 4. OpenAPI Generation

Auto-generates OpenAPI 3.0 JSON from RouteTable. Extracts paths, methods, `:param` and `*wildcard` as parameters. No annotations or type reflection.

```fsharp
module OpenApi =
    let generate (title: string) (version: string) (routes: RouteTable) : string
    let handler (title: string) (version: string) (routes: RouteTable) : Handler
```

Converts `:id` to `{id}` in paths.

## File Changes

**New:** `src/Fire/Timeout.fs`, `src/Fire/RateLimit.fs`, `src/Fire/OpenApi.fs`
**Modified:** `src/Fire/App.fs` (ShutdownTimeout)

**Compile order:** Request.fs, Response.fs, Cookie.fs, Types.fs, Trie.fs, Route.fs, Log.fs, Static.fs, Timeout.fs, RateLimit.fs, OpenApi.fs, Cors.fs, App.fs
