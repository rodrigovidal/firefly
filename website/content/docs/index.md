---
title: "Introduction"
description: "A minimal, fast F# web framework built straight on Kestrel."
group: "Introduction"
order: 0
---

# Firefly

A minimal F# web framework built on Kestrel.

Firefly is part of a cohesive F# ecosystem:

- **Firefly** — Web framework (routing, middleware, DI, gRPC)
- **Flame** — Schema validation (parsing, rules, JSON Schema)
- **Flare** — HTTP client (typed requests, resilience)
- **Evlog** — Structured logging
- **Rhinox** — Database conventions

## Quick Start

```bash
dotnet new fire -n MyApp
cd MyApp
firefly dev
```

## Features

- Type-safe routing with format strings (%i, %s, %b, %f)
- Auto dependency injection
- Schema validation via Flame
- 15+ built-in middleware (CORS, JWT, rate limiting, compression, etc.)
- WebSocket and SSE support
- gRPC server with computation expressions
- File uploads and downloads
- Pagination, versioning, HATEOAS, bulk operations
- OpenTelemetry traces and metrics
- Response caching with ETag auto-generation
- Typed configuration from .env files
- Testing helpers (direct + integration modes)
- CLI generators (controllers, schemas, Docker)

