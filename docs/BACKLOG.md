# Fire — Product Backlog

## Ecosystem

Fire is part of a cohesive F# ecosystem:

| Library | Purpose | Status |
|---------|---------|--------|
| **Fire** | Web framework | Active |
| **Flame** | Schema validation | Published (NuGet) |
| **Flare** | HTTP client | In progress |
| **Evlog** | Logging | Published |
| **Rhinox** | Database | Active |

## Completed

### Core Web Framework
- [x] Routing — type-safe format strings (`%i`, `%s`, `%b`, `%f`), wildcards, groups
- [x] POST/PUT body parsing — `req.Json<'T>()`, `req.Form()`, `req.Text()`
- [x] Query param helpers — `req.QueryParam "key"`
- [x] Response builders — text, html, json, stream, noContent, redirect
- [x] Cookies — `Cookie.set`, signed cookies
- [x] Static file serving — `Static.serve` with MIME detection
- [x] Content negotiation — `req.Accepts`, `req.ContentType`
- [x] ETag / caching — `Response.etag`, `Response.cacheControl`

### Middleware
- [x] CORS — `Cors.allowAll`, `Cors.defaults |> Cors.build`
- [x] JWT auth — `Jwt.validate` with JWS + JWE
- [x] Rate limiting — `RateLimit.fixedWindow`
- [x] Request timeout — `Timeout.after`
- [x] Compression — `Compress.gzip`, `Compress.brotli`, `Compress.auto`
- [x] Request ID — `RequestId.middleware`
- [x] Correlation ID — `CorrelationId.middleware`
- [x] Secure headers — `SecureHeaders.defaults`
- [x] CSRF — `Csrf.middleware`
- [x] Session — `Session.middleware`
- [x] Idempotency — `Idempotent.middleware`

### Validation (Flame)
- [x] Schema CE — `schema { }` with typed parsing from JSON
- [x] 30+ validators — string, number, array rules
- [x] `Schema.fromType<'T>()` — auto-generate from records
- [x] `Rule<'T>` — type-safe rules, no boxing
- [x] `FieldValue` struct union — zero-alloc buffer path
- [x] Validator CE — `validator { }` for existing values
- [x] Validated CE — `validated { }` for transform A → B
- [x] JSON Schema generation — `Schema.toJsonSchema`

### DX & Tooling
- [x] DI — `App.services [ Service.singleton<I, T>; ... ]`
- [x] Env config — `Env.load<AppConfig>()` from .env + env vars
- [x] Testing — `TestClient.create` (direct) + `TestClient.start` (HTTP)
- [x] OpenAPI — `OpenApi.handler` from route table
- [x] Dev error page — rich diagnostics in dev
- [x] Live reload — browser refresh on code changes
- [x] `dotnet new fire` — scaffolded project template
- [x] `fire dev` — watch mode
- [x] Health checks — `Health.handler`
- [x] Graceful shutdown — `App.shutdownTimeout`
- [x] Evlog integration — event logging middleware

## Next Up

### Real-time
- [ ] WebSockets — ergonomic helpers, room/channel patterns
- [ ] SSE improvements — `Sse.stream` for push notifications
- [ ] Streaming responses — `Response.streamJson` for NDJSON / large datasets

### Auth
- [ ] Cookie auth — login/logout with encrypted cookies, no JWT
- [ ] OAuth helpers — GitHub, Google, generic OAuth2 provider
- [ ] Auth middleware — `Auth.requireRole "admin"`, `Auth.requireClaim`

### File Handling
- [ ] File uploads — multipart parsing, `req.Files()`
- [ ] File download helpers — `Response.file "path"` with Content-Disposition
- [ ] Upload size limits — configurable per route

### Database (Rhinox Integration)
- [ ] Connection-per-request — middleware that opens/closes per request
- [ ] Transaction middleware — auto-commit/rollback per request
- [ ] Fire + Rhinox example — full CRUD app with real database

### HTTP Client (Flare Integration)
- [ ] Service-to-service calls — `Flare.get`, `Flare.post` with Flame schemas
- [ ] Fire + Flare example — API gateway or aggregation pattern

### API Patterns
- [ ] Pagination — cursor and offset helpers, `Pagination.parse req`
- [ ] API versioning — URL or header based, `Route.version "v1"`
- [ ] HATEOAS helpers — link generation from route table
- [ ] Bulk operations — batch endpoints with partial success

### Observability
- [ ] Structured request logging — JSON log per request with method, path, status, duration
- [ ] OpenTelemetry — traces, spans, baggage propagation
- [ ] Metrics — request count, latency histograms, error rates

### Caching
- [ ] Response caching middleware — `Cache.maxAge 60`, `Cache.varyBy "Accept"`
- [ ] ETag auto-generation — hash response body, return 304 on match
- [ ] Distributed cache helpers — Redis/memory abstraction

### CLI & Scaffolding
- [ ] `fire gen controller` — generate handler + routes + tests
- [ ] `fire gen schema` — generate Flame schema from type
- [ ] `fire gen migration` — generate Rhinox migration
- [ ] Docker template — `fire new` includes Dockerfile + docker-compose

### Documentation
- [ ] Docs site — dedicated documentation beyond README
- [ ] API reference — auto-generated from XML docs
- [ ] Tutorials — step-by-step guides for common patterns
- [ ] Fire + Flame + Rhinox + Evlog integration guide

### Performance
- [ ] Source-generated DI — F# source generator (future)
- [ ] Response pooling — reuse response objects
- [ ] Route matching benchmarks — compare against ASP.NET minimal API
