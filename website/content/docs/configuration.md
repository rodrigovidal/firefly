---
title: "Configuration"
description: "Configure the app, ports, and environment."
group: "Core"
order: 5
---

# Configuration

Firefly provides typed configuration from `.env` files and environment variables via `Env.load`.

## .env Files

Create a `.env` file in your project root:

```env
# .env
DATABASE_URL=postgres://localhost:5432/myapp
PORT=3000
JWT_SECRET=my-secret-key
DEBUG=true
API_BASE_URL=https://api.example.com
```

Supported formats:

```env
# Comments
KEY=value
KEY="quoted value"
KEY='single quoted'
EMPTY=
```

## Typed Configuration

Define an F# record and load it with `Env.load`:

```fsharp
type AppConfig = {
    DatabaseUrl: string
    Port: int
    JwtSecret: string
    Debug: bool
    ApiBaseUrl: System.Uri
}

let config = Env.load<AppConfig>()
// config.DatabaseUrl = "postgres://localhost:5432/myapp"
// config.Port = 3000
// config.Debug = true
```

### Naming Convention

Record field names are converted to `SCREAMING_SNAKE_CASE`:

| Field Name | Environment Variable |
|-----------|---------------------|
| `DatabaseUrl` | `DATABASE_URL` |
| `JwtSecret` | `JWT_SECRET` |
| `ApiBaseUrl` | `API_BASE_URL` |
| `Port` | `PORT` |

### Supported Types

| F# Type | Accepted Values |
|---------|----------------|
| `string` | Any string |
| `int` | Integer (e.g., `3000`) |
| `float` | Number (e.g., `3.14`) |
| `bool` | `true`, `1`, `yes` / `false`, `0`, `no` |
| `System.Uri` | Valid URI string |
| `System.TimeSpan` | TimeSpan string (e.g., `00:30:00`) |

### Optional Fields

Use `option` types for fields that may not be present:

```fsharp
type AppConfig = {
    DatabaseUrl: string       // required — throws if missing
    RedisUrl: string option   // optional — None if missing
    Port: int                 // required
    Debug: bool option        // optional
}
```

Missing required fields produce a clear error listing all missing variable names:

```
Missing required environment variables: DATABASE_URL, PORT
```

## Environment Priority

Real environment variables take precedence over `.env` file values. The `.env` file is only used to fill in variables that are not already set in the process environment. This is useful for local development without affecting deployed environments.

## Usage with DI

Register your config as a singleton service:

```fsharp
type AppConfig = { DatabaseUrl: string; Port: int }

let appConfig = Env.load<AppConfig>()

let config =
    App.defaults
    |> App.port appConfig.Port
    |> App.services [
        Service.instance appConfig
    ]
```

Then access it in handlers via auto-DI (if it is an interface) or manually:

```fsharp
let handler (req: Request) = task {
    let config = req.Raw.RequestServices.GetRequiredService<AppConfig>()
    return Response.json {| db = config.DatabaseUrl |}
}
```

## Per-Environment Configuration

Use the CLI template pattern with separate config modules:

```fsharp
// Config/Dev.fs
module MyApp.Config.Dev

let config =
    App.defaults
    |> App.port 3000
    |> App.middleware Cors.allowAll

// Config/Prod.fs
module MyApp.Config.Prod

let config =
    App.defaults
    |> App.port 8080
    |> App.middleware SecureHeaders.middleware
    |> App.middleware Compress.auto
```

