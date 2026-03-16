# Fire — Product Backlog

## Tier 1: Essential for any real API

- [ ] POST/PUT body parsing — `req.Form()`, `req.Text()` beyond just `req.Json<'T>()`
- [ ] Query param helpers — `req.QueryParam "key"` returning `string option` (single value access)
- [ ] Wildcard routes — `/static/*path` for catch-all segments
- [ ] Response cookies — `Response.cookie "name" "value"` builder
- [ ] CORS middleware — ships as built-in middleware

## Tier 2: Developer experience

- [ ] Logging middleware — request method, path, status, duration
- [ ] Static file serving — `Route.staticFiles "/public" "./wwwroot"`
- [ ] Content negotiation — `req.Accepts "application/json"`, auto content-type detection
- [ ] Redirect helper — `Response.redirect "/somewhere" 302`
- [ ] ETag / caching helpers — `Response.etag`, `Response.cacheControl`

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
