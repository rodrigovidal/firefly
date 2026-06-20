---
title: "CLI"
description: "Scaffold projects, generate code, and run the dev server."
group: "Introduction"
order: 2
---

# CLI

The Firefly CLI (`firefly`) provides project scaffolding, code generation, development server, and OpenAPI spec generation.

## Installation

```bash
dotnet tool install --global Firefly.Cli
```

## Commands

### firefly new

Scaffold a new Firefly project with routing, configuration, tests, and a solution file:

```bash
firefly new MyApp
```

Options:

```bash
firefly new MyApp --output /path/to/dir    # custom output directory
firefly new MyApp --force                   # overwrite existing directory
```

Generated structure:

```
MyApp/
  MyApp.sln
  src/MyApp/
    App.fs
    Router.fs
    Endpoint.fs
    Config/
      Dev.fs
      Prod.fs
    Controllers/
      PageController.fs
    Views/
      PageView.fs
    Layouts/
      RootLayout.fs
    Components/
      CoreComponents.fs
    MyApp.fsproj
  tests/MyApp.Tests/
    Fixtures.fs
    IntegrationTests.fs
    ControllerTests.fs
    MyApp.Tests.fsproj
```

### firefly dev

Start the development server with hot reload:

```bash
firefly dev
```

This runs `dotnet watch run` with `ASPNETCORE_ENVIRONMENT=Development` and `DOTNET_ENVIRONMENT=Development`. In development mode, Firefly automatically enables live reload (SSE-based script injection for browser auto-refresh).

Options:

```bash
firefly dev --project src/MyApp/MyApp.fsproj
```

#### Generators in dev (`firefly.json`)

Drop a `firefly.json` manifest in the project root to declare generators that should run as part of `firefly dev`. Each entry is the equivalent of a `firefly gen` invocation:

```json
{
  "generators": [
    { "kind": "schema",     "name": "User",    "fields": ["name:string", "age:int"] },
    { "kind": "controller", "name": "Health" },
    { "kind": "html",       "name": "Contact", "fields": ["email:string"] }
  ]
}
```

`kind` is one of `schema`, `controller`, `html`, `json`, or `docker`; `fields` is omitted for `controller` and `docker`. When `firefly dev` starts it runs every declared generator once, then watches `firefly.json` and regenerates whenever you edit it — no restart required. Without a `firefly.json`, `firefly dev` behaves exactly as before.

#### Vite dev proxy

If your app has a Vite frontend, `App.vite` wires the dev proxy with an auto-detected port and is a no-op outside Development, so the same pipeline is safe to ship:

```fsharp
App.defaults
|> App.vite   // proxies Vite assets in dev only; production untouched
```

The port is detected from `vite.config.{ts,js,mjs}` (`server.port`), falling back to the `VITE_PORT` env var, then Vite's default `5173`.

### firefly gen controller

Generate a controller file:

```bash
firefly gen controller Users
```

Creates `Controllers/UsersController.fs` with GET/POST/PUT/DELETE handlers.

### firefly gen schema

Generate a Flame schema:

```bash
firefly gen schema CreateUser name:string email:string age:int
```

Creates `Schemas/CreateUserSchema.fs` with a typed record and schema definition.

Supported field types: `string`, `int`, `float`, `bool`, `datetime`.

### firefly gen html / firefly gen json

Generate a full resource with controller, views, and routes:

```bash
firefly gen html Product name:string price:float description:string
firefly gen json Product name:string price:float description:string
```

- `html` generates server-rendered views with forms
- `json` generates JSON API endpoints

### firefly gen docker

Generate Docker deployment files:

```bash
firefly gen docker
```

Creates a `Dockerfile` and related configuration for containerized deployment.

### firefly openapi

Generate a static OpenAPI specification from your route definitions:

```bash
firefly openapi
firefly openapi --output openapi.json
firefly openapi --title "My API" --version "1.0.0"
firefly openapi --project src/MyApp/MyApp.fsproj
firefly openapi --routes "apiRoutes"
```

Options:

| Option | Description |
|--------|-------------|
| `--project <path>` | Path to the F# project (auto-detected if omitted) |
| `--output <path>` | Output file path |
| `--title <title>` | API title in the spec |
| `--version <version>` | API version in the spec |
| `--routes <name>` | Name of the routes binding to analyze |

## Full Usage Reference

```
Firefly CLI

Commands:
  firefly new <Name> [--output <path>] [--force]
  firefly dev [--project <path>]
  firefly gen html <Resource> field:type [field:type ...]
  firefly gen json <Resource> field:type [field:type ...]
  firefly gen controller <Name>
  firefly gen schema <Name> field:type [field:type ...]
  firefly gen docker
  firefly openapi [--project <path>] [--output <path>] [--title <title>] [--version <version>] [--routes <name>]
```

