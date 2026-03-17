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
- [x] OpenAPI generation — `OpenApi.generate` / `OpenApi.handler` from RouteTable

## Tier 4: Ecosystem

- [x] NuGet packaging — package metadata, LICENSE, README (not published yet)
- [x] JWT auth middleware — `Jwt.validate` with JWS + JWE support
- [x] Testing helpers — `TestClient.create` (direct) + `TestClient.start` (HTTP integration)
- [x] Schema validation — Zod-like CE with zero-alloc Utf8JsonReader parser
- [x] Auto DI — HandlerFactory resolves interfaces from IServiceProvider
- [x] Format string params — `%i`, `%s`, `%b`, `%f` for typed route params
- [x] CI/CD — GitHub Actions build + test + coverage threshold

## Tier 5: Real-time & Rendering

- [ ] WebSockets — ergonomic helpers over the `Raw` escape hatch
- [ ] Server-Sent Events (SSE) — one-way streaming for live updates
- [ ] View engine — server HTML rendering + React client hydration + TanStack Query
- [ ] Streaming responses — `Response.streamJson` for large datasets, NDJSON

## Tier 6: Developer Experience

- [ ] `dotnet new fire` template — scaffold a new Fire project
- [ ] Hot reload / watch mode — `dotnet watch` integration with auto-restart
- [ ] Dev error page — show stack traces in dev, hide in prod
- [ ] Content negotiation middleware — auto-select JSON/XML/text based on Accept header
- [ ] Response compression — gzip/brotli middleware
- [ ] Request ID middleware — `X-Request-Id` for tracing
- [ ] Health checks — `/health` with customizable checks (db, disk, etc.)

## Tier 7: Schema Enhancements

- [ ] `Schema.fromType<'T>` — auto-generate schema from an F# record type
- [ ] Schema coercion — `"42"` → `42` when field expects int
- [ ] Schema transforms — `Schema.trim`, `Schema.lowercase` applied during parsing

## Tier 8: Performance

- [ ] Object pooling — pool the `obj[]` arrays in schema parser
- [ ] Source-generated DI — eliminate runtime reflection in HandlerFactory
