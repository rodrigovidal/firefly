# Fire — Product Backlog

## Current Priority: Phoenix-Style DX

Fire should move from "minimal API toolkit" toward an opinionated web framework with a golden path. The next milestone after the view layer is a cohesive dev loop that makes local development feel fast, structured, and guided.

Detailed plan: `docs/plans/2026-03-17-phoenix-dev-loop-plan.md`

- [ ] View engine — first-party server-rendered HTML with layouts, views, components, forms, and optional client hydration
- [ ] App scaffold + generators — ``dotnet new fire`` plus `fire new`, `fire gen html`, `fire gen json`, and shared conventions
- [ ] Hot reload / live reload — code reload plus browser refresh for templates, static assets, and route changes
- [x] Dev error page — rich diagnostics in development, safe fallback in production
- [x] Opinionated app structure — `App.fs`, router scopes/pipelines, `Controllers`, `Views`, `Components`, `Layouts`, `Static`, `Assets`, `Config`, and test fixtures

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
- [ ] View engine — first-party SSR HTML with layouts, views, components, forms, and optional client hydration
- [ ] Streaming responses — `Response.streamJson` for large datasets, NDJSON

## Tier 6: Developer Experience

- [x] `dotnet new fire` template — scaffold a new Fire project with the opinionated Fire structure
- [ ] Generators — `fire new`, `fire gen html`, `fire gen json`, shared naming conventions
- [x] Hot reload / watch mode — `fire dev` wraps `dotnet watch run` with Fire’s scaffolded watch layout
- [x] Dev error page — show stack traces in dev, hide in prod
- [x] Opinionated project structure — `App.fs`, router scopes/pipelines, `Controllers`, `Views`, `Components`, `Layouts`, `Static`, `Assets`, `Config`, tests/fixtures
- [x] Content negotiation middleware — `Negotiate.middleware` returns 406 for unsupported types
- [x] Response compression — `Compress.gzip` / `Compress.brotli` / `Compress.auto`
- [x] Request ID middleware — `RequestId.middleware` adds X-Request-Id
- [x] Correlation ID middleware — `CorrelationId.middleware` adds X-Correlation-Id
- [x] Health checks — `Health.handler` with customizable checks, 200/503

## Tier 7: Schema Enhancements

- [x] `Schema.fromType<'T>()` — auto-generate schema from F# record type
- [x] Schema coercion — `"42"` → `42` when field expects int
- [x] Schema transforms — `Schema.trim`, `Schema.lowercase`, `Schema.uppercase`
- [x] Complex types in `fromType` — nested records, `option` (auto-required/optional), typed lists (`int list`, `Tag list`)
- [x] Zod-parity string validators — `uuid`, `ip`, `ipv4`, `ipv6`, `datetime`, `startsWith`, `endsWith`, `includes`, `length`, `nonempty`
- [x] Zod-parity number validators — `gt`, `lt`, `positive`, `negative`, `nonnegative`, `nonpositive`, `int'`, `multipleOf`
- [x] Array validators — `minItems`, `maxItems`, `nonEmpty`
- [x] JSON Schema output for all new validators — `exclusiveMinimum`, `exclusiveMaximum`, `multipleOf`, `minItems`, `maxItems`, `format`

## Tier 8: Performance

- [x] Object pooling — `ArrayPool<obj>` in schema parser
- [ ] Source-generated DI — requires F# source generator / type provider (future)
