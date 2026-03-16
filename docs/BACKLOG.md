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

- [ ] Graceful shutdown — drain in-flight requests on SIGTERM
- [ ] Request timeout middleware — per-route or global timeout
- [ ] Rate limiting middleware
- [ ] WebSocket support — ergonomic helpers over the `Raw` escape hatch
- [ ] OpenAPI generation — auto-generate spec from route definitions

## Tier 4: Ecosystem

- [ ] NuGet packaging — publish as `Fire` on NuGet
- [ ] `dotnet new fire` template — project template for quick starts
- [ ] Validation middleware — composable request validation
- [ ] JWT auth middleware
- [ ] Testing helpers — `TestClient` that calls handlers directly without HTTP (like Hono's `app.request()`)
