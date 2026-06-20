# Firefly — Product Backlog

## Ecosystem

Firefly is part of a cohesive F# ecosystem:

| Library | Purpose | Status |
|---------|---------|--------|
| **Firefly** | Web framework | Active |
| **Flame** | Schema validation | Published (NuGet) |
| **Flare** | HTTP client | In progress |
| **Evlog** | Logging | Published |
| **Rhinox** | Database | Active |

## Completed

### Core web framework
- [x] Routing — type-safe format strings (`%i`, `%s`, `%b`, `%f`), wildcards, groups
- [x] Body parsing — `Request.json<'T>`, `req.Form()`, `req.Text()`
- [x] Query / route param helpers
- [x] Response builders — text, html, json, stream, noContent, redirect, `badRequest`, `forbidden`, `unauthorized`, `notFound`
- [x] Cookies — `Cookie.set`, signed cookies
- [x] Static file serving — `Static.serve` with MIME detection
- [x] Content negotiation — `req.Accepts`, `req.ContentType`
- [x] ETag / caching — `Response.etag`, `Response.cacheControl`, auto-ETag
- [x] gRPC — `grpcService { unary; serverStream }` + `App.grpc`, served alongside HTTP

### Middleware
- [x] CORS, JWT (JWS + JWE), rate limiting (fixed window), request timeout
- [x] Compression (gzip/brotli/auto), Request ID, Correlation ID, Secure headers
- [x] CSRF, Session, Idempotency

### Validation (Flame)
- [x] `schema { }` CE, 30+ validators, `Schema.fromType<'T>()`
- [x] Type-safe `Rule<'T>` (no boxing), zero-alloc buffer path, `Validator`/`Validated` CEs
- [x] JSON Schema generation

### View engine
- [x] Server-side DSL (`Html` / `Node` / `Render`), layout + error-boundary middleware
- [x] Vite dev proxy + asset helpers, Live reload

### Real-time
- [x] WebSocket — basic send/receive (`WS.handler` / `WsConn`)
- [x] WebSocket rooms / broadcast — typed `WsHub<'T>` + `WS.hub` (room/channel/broadcast)
- [x] SSE — server-sent events with broadcast

### API patterns
- [x] Pagination (cursor + offset, `Pagination.parse`)
- [x] HATEOAS link generation
- [x] Bulk operations (batch endpoints, partial success)
- [x] API versioning — `Version.url` (URL prefix) + `Version.header` / `Version.headerRequired`
- [x] Router pipelines — `Route.pipe` (prefix + pipeline + nested routes)

### File handling
- [x] Multipart parsing — `req.Files()` returning `UploadedFile list`
- [x] File downloads — `Response.file "path"` with Content-Disposition
- [x] Upload size limits — `Upload.maxSize` middleware (apply globally or per route group)

### Real-time (responses)
- [x] Streaming responses — `Response.streamJson` / `streamJsonAsync` for NDJSON / large datasets

### Caching
- [x] Response-caching middleware (`Cache.maxAge`, `Cache.varyBy`)
- [x] Auto ETag generation (304 on match)

### DX & tooling
- [x] DI — `App.services [ Service.singleton<I, T>; ... ]`
- [x] Env config — `Env.load<AppConfig>()`, with `.env.{environment}` profile layering
- [x] Testing — `TestClient.create` (direct) + `TestClient.start` (HTTP)
- [x] OpenAPI — `OpenApi.handler` + `firefly openapi`
- [x] Dev error page, graceful shutdown, health checks, Evlog integration
- [x] CLI `firefly` — `new`, `dev`, `gen html|json|controller|schema|docker`, `openapi`
- [x] Generators in `firefly dev` — `firefly.json` manifest, regenerated on change
- [x] Vite dev proxy with auto-detected port — `App.vite` / `Vite.autoPort`
- [x] Docker template via `firefly gen docker`

### Docs & perf
- [x] Documentation site + guides (fireflyframework.dev)
- [x] Route-matching + framework-comparison benchmarks; JSON-validation benchmark vs FluentValidation
- [x] Response pooling

## Next Up

> Auth lives in the separate **Fireproof** repo/project and is tracked there.

### Ecosystem integrations — optional, not batteries-included
> Flare and Rhinox are separate opt-in packages, not bundled with the core framework. Firefly stays minimal; these are integrations users add only if they want them.
- [ ] Flare — `Flare.get/post` with Flame schemas + an example
- [ ] Rhinox — connection/transaction middleware maturity, `firefly gen migration`, full Firefly+Rhinox CRUD example

### Smaller items
- [ ] Distributed cache / session backend (Redis abstraction); session store is in-memory only

### Performance (future)
- [x] AOT/trim-safe DI guidance — documented composition-root + factory pattern (F# has no Roslyn source generators; the hand-written graph gives the same compile-time validation)
- [ ] Source-generated DI proper — Myriad plugin or `firefly gen di` (build → reflect constructors → emit factories), only if demand appears
