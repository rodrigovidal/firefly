# CLI

The Fire CLI (`fire`) provides project scaffolding, code generation, development server, and OpenAPI spec generation.

## Installation

```bash
dotnet tool install --global Fire.Cli
```

## Commands

### fire new

Scaffold a new Fire project with routing, configuration, tests, and a solution file:

```bash
fire new MyApp
```

Options:

```bash
fire new MyApp --output /path/to/dir    # custom output directory
fire new MyApp --force                   # overwrite existing directory
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

### fire dev

Start the development server with hot reload:

```bash
fire dev
```

This runs `dotnet watch run` with `ASPNETCORE_ENVIRONMENT=Development` and `DOTNET_ENVIRONMENT=Development`. In development mode, Fire automatically enables live reload (SSE-based script injection for browser auto-refresh).

Options:

```bash
fire dev --project src/MyApp/MyApp.fsproj
```

### fire gen controller

Generate a controller file:

```bash
fire gen controller Users
```

Creates `Controllers/UsersController.fs` with GET/POST/PUT/DELETE handlers.

### fire gen schema

Generate a Flame schema:

```bash
fire gen schema CreateUser name:string email:string age:int
```

Creates `Schemas/CreateUserSchema.fs` with a typed record and schema definition.

Supported field types: `string`, `int`, `float`, `bool`, `datetime`.

### fire gen html / fire gen json

Generate a full resource with controller, views, and routes:

```bash
fire gen html Product name:string price:float description:string
fire gen json Product name:string price:float description:string
```

- `html` generates server-rendered views with forms
- `json` generates JSON API endpoints

### fire gen docker

Generate Docker deployment files:

```bash
fire gen docker
```

Creates a `Dockerfile` and related configuration for containerized deployment.

### fire openapi

Generate a static OpenAPI specification from your route definitions:

```bash
fire openapi
fire openapi --output openapi.json
fire openapi --title "My API" --version "1.0.0"
fire openapi --project src/MyApp/MyApp.fsproj
fire openapi --routes "apiRoutes"
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
Fire CLI

Commands:
  fire new <Name> [--output <path>] [--force]
  fire dev [--project <path>]
  fire gen html <Resource> field:type [field:type ...]
  fire gen json <Resource> field:type [field:type ...]
  fire gen controller <Name>
  fire gen schema <Name> field:type [field:type ...]
  fire gen docker
  fire openapi [--project <path>] [--output <path>] [--title <title>] [--version <version>] [--routes <name>]
```
