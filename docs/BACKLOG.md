# Fire — Product Backlog

## Tier 1: Essential for any real API

- [x] POST/PUT body parsing — `req.Form()`, `req.Text()` beyond just `req.Json<'T>()`
- [x] Query param helpers — `req.QueryParam "key"` returning `string option` (single value access)
- [x] Wildcard routes — `/static/*path` for catch-all segments
- [x] Response cookies — `Response.cookie "name" "value"` builder
- [x] CORS middleware — ships as built-in middleware

## Tier 2: Developer experience

- [x] Logging middleware — `Log.withOutput`, `Log.toConsole`, `Log.toLogger`
- [x] Static file serving — `Static.serve "./wwwroot"` with wildcard routes
- [x] Content negotiation — `req.Accepts`, `req.ContentType`
- [x] Redirect helper — `Response.redirect "/somewhere" 302`
- [x] ETag / caching helpers — `Response.etag`, `Response.cacheControl`

## Tier 3: Production readiness

- [x] Graceful shutdown — `App.shutdownTimeout` with Kestrel drain
- [x] Request timeout middleware — `Timeout.after`, returns 504
- [x] Rate limiting middleware — `RateLimit.fixedWindow` with `byIp` helper
- [ ] WebSocket support — deferred
- [x] OpenAPI generation — `OpenApi.generate` / `OpenApi.handler` from RouteTable

## Tier 4: Ecosystem

- [x] NuGet packaging — package metadata, LICENSE, README (not published yet)
- [ ] `dotnet new fire` template — deferred
- [x] Validation middleware — `Validate.body`, `Validate.query`, `Validate.param`, `Validate.headerValues` with composable rules
- [x] JWT auth middleware — `Jwt.validate` with JWS + JWE support via `Microsoft.IdentityModel.JsonWebTokens`
- [x] Testing helpers — `TestClient.create` (direct) + `TestClient.start` (HTTP integration)
