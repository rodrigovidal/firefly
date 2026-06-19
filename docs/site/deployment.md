# Deployment

## Docker

Generate Docker files with the CLI:

```bash
firefly gen docker
```

Or create a `Dockerfile` manually:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/MyApp/MyApp.fsproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "MyApp.dll"]
```

Build and run:

```bash
docker build -t myapp .
docker run -p 8080:8080 --env-file .env.production myapp
```

## Production Configuration

### Bind to All Interfaces

```fsharp
let config =
    App.defaults
    |> App.host "0.0.0.0"
    |> App.port 8080
```

### Graceful Shutdown

Configure a shutdown timeout so in-flight requests can complete:

```fsharp
App.defaults
|> App.shutdownTimeout (TimeSpan.FromSeconds 30.0)
```

The server waits up to the specified duration for active requests to finish before forcing shutdown.

### Error Handling

Always configure a global error handler in production:

```fsharp
App.defaults
|> App.onError (fun ex req -> task {
    // Log the error (use your logging library)
    printfn $"Error: {ex.Message}"
    return Response.json {| error = "Internal server error" |} |> Response.status 500
})
|> App.notFound (fun req -> task {
    return Response.json {| error = "Not found" |} |> Response.status 404
})
```

### Recommended Middleware Stack

```fsharp
let config =
    App.defaults
    |> App.port 8080
    |> App.host "0.0.0.0"
    |> App.middleware RequestId.middleware
    |> App.middleware CorrelationId.middleware
    |> App.middleware Telemetry.middleware
    |> App.middleware SecureHeaders.middleware
    |> App.middleware Compress.auto
    |> App.onError errorHandler
    |> App.notFound notFoundHandler
    |> App.shutdownTimeout (TimeSpan.FromSeconds 30.0)
```

## Health Checks

Register a health endpoint for container orchestrators and load balancers:

```fsharp
let healthHandler =
    Health.handler [
        Health.ping
        Health.check "database" (fun () -> task {
            do! db.PingAsync()
        })
    ]

Route.start
|> Route.get "/health" healthHandler
|> Route.get "/healthz" healthHandler  // common k8s convention
```

Response when healthy (200):

```json
{
  "status": "healthy",
  "checks": [
    { "name": "ping", "status": "healthy", "duration": "00:00:00.001", "error": null },
    { "name": "database", "status": "healthy", "duration": "00:00:00.015", "error": null }
  ],
  "totalDuration": "00:00:00.016"
}
```

Response when unhealthy (503):

```json
{
  "status": "unhealthy",
  "checks": [
    { "name": "ping", "status": "healthy", "duration": "00:00:00.001", "error": null },
    { "name": "database", "status": "unhealthy", "duration": "00:00:05.000", "error": "Connection refused" }
  ],
  "totalDuration": "00:00:05.001"
}
```

## Environment Variables

Use `Env.load` for typed configuration. Environment variables always override `.env` file values, so you can use `.env` for local development and real env vars in production:

```fsharp
type ProdConfig = {
    DatabaseUrl: string
    Port: int
    JwtSecret: string
    CorsOrigins: string option
}

let config = Env.load<ProdConfig>()
```

Set in your container orchestrator:

```yaml
# docker-compose.yml
services:
  app:
    image: myapp
    environment:
      DATABASE_URL: postgres://db:5432/myapp
      PORT: "8080"
      JWT_SECRET: ${JWT_SECRET}
    ports:
      - "8080:8080"
```

## Observability

### OpenTelemetry

Enable tracing and metrics:

```fsharp
App.defaults
|> App.middleware Telemetry.middleware
|> App.services [
    Service.raw (fun services ->
        services.AddOpenTelemetry()
            .WithTracing(fun builder ->
                builder
                    .AddSource(Telemetry.sourceName)
                    .AddOtlpExporter()
                |> ignore
            )
            .WithMetrics(fun builder ->
                builder
                    .AddMeter(Telemetry.meterName)
                    .AddOtlpExporter()
                |> ignore
            )
        |> ignore
    )
]
```

### Request Tracing

Use Request ID and Correlation ID middleware for distributed tracing:

```fsharp
App.defaults
|> App.middleware RequestId.middleware
|> App.middleware CorrelationId.middleware
```

Access in handlers:

```fsharp
let handler (req: Request) = task {
    let requestId = req.RequestId      // string option
    let correlationId = req.CorrelationId  // string option
    return Response.ok
}
```
