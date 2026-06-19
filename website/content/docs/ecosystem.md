---
title: "Ecosystem"
description: "Related packages and integrations."
group: "Reference"
order: 1
---

# Ecosystem

Firefly is part of a cohesive set of F# libraries designed to work together.

## Overview

| Library | Purpose | Key Feature |
|---------|---------|-------------|
| **Firefly** | Web framework | Routing, middleware, DI, gRPC |
| **Flame** | Schema validation | Typed parsing, rules, JSON Schema |
| **Flare** | HTTP client | Typed requests, resilience |
| **Evlog** | Structured logging | Wide events, drains |
| **Rhinox** | Database conventions | Query building, migrations |

## Firefly + Flame

Firefly integrates directly with Flame for request validation. The `Schema` module in Firefly bridges the two:

```fsharp
open Firefly
open Flame

type CreateUser = { Name: string; Email: string; Age: int }

let createUserSchema = schema<CreateUser> {
    required "name"  Schema.string [ Rules.minLength 1; Rules.trim ]
    required "email" Schema.string [ Rules.email ]
    required "age"   Schema.int   [ Rules.min 0; Rules.max 150 ]
}

// Auto-validated handler
let createUser =
    Schema.validated createUserSchema (fun user -> task {
        return Response.json user |> Response.status 201
    })

Route.start
|> Route.post "/users" createUser
```

Flame schemas work with multiple input sources:

```fsharp
// JSON body
Schema.parse schema req

// Form data
Schema.parseFormRequest schema req

// Query parameters
Schema.parseQuery schema req

// Route parameters
Schema.parseParams schema req
```

## Firefly + Flare

Use Flare as an HTTP client inside Firefly handlers, with DI for service registration:

```fsharp
// Register Flare client as a service
App.defaults
|> App.services [
    Service.singletonFactory (fun _ ->
        Flare.client "https://api.example.com"
    )
]

// Use in handlers via auto-DI
let proxyHandler (client: IFlareClient) (req: Request) = task {
    let! result = client.Get("/external-data")
    return Response.json result
}
```

## Firefly + Evlog

Evlog provides structured logging that integrates with Firefly's request lifecycle:

```fsharp
// Firefly's Evlog module is available for logging within handlers
let handler (req: Request) = task {
    // Use structured logging
    Evlog.info "Processing request" [
        "path", req.Path
        "method", req.Method
        "requestId", req.RequestId |> Option.defaultValue "none"
    ]
    return Response.ok
}
```

Combine with the Telemetry middleware for full observability:

```fsharp
App.defaults
|> App.middleware RequestId.middleware
|> App.middleware CorrelationId.middleware
|> App.middleware Telemetry.middleware
```

## Firefly + Rhinox

Rhinox provides database conventions and query building. Register your database context as a service:

```fsharp
App.defaults
|> App.services [
    Service.scoped<IDbContext, AppDbContext>
]

// Auto-injected in handlers
let listUsers (db: IDbContext) (req: Request) = task {
    let! users = db.Query<User>("SELECT * FROM users")
    return Response.json users
}
```

## Full Stack Example

A complete application using the entire ecosystem:

```fsharp
open Firefly
open Flame

// --- Schema (Flame) ---
type CreateOrder = { ProductId: int; Quantity: int }

let orderSchema = schema<CreateOrder> {
    required "productId" Schema.int [ Rules.positive ]
    required "quantity"  Schema.int [ Rules.min 1; Rules.max 100 ]
}

// --- Routes (Firefly) ---
let routes =
    Route.start
    |> Route.get "/health" (Health.handler [ Health.ping ])
    |> Route.group "/api/v1" (fun t ->
        t
        |> Route.middleware (Jwt.validate (Jwt.defaults "secret"))
        |> Route.get "/orders" (fun (db: IDbContext) (req: Request) -> task {
            match Pagination.parse req with
            | PageParams.Offset (offset, limit) ->
                let! orders = db.Query("SELECT * FROM orders LIMIT @limit OFFSET @offset")
                return Pagination.respond (Pagination.offsetMeta "/api/v1/orders" offset limit 100) orders
            | _ -> return Response.json {| error = "Use offset pagination" |} |> Response.status 400
        })
        |> Route.post "/orders" (Schema.validated orderSchema (fun order -> task {
            return Response.json {| id = 1; productId = order.ProductId; quantity = order.Quantity |}
                   |> Response.status 201
        }))
    )

// --- App Configuration ---
let appConfig = Env.load<AppConfig>()

let config =
    App.defaults
    |> App.port appConfig.Port
    |> App.middleware RequestId.middleware
    |> App.middleware CorrelationId.middleware
    |> App.middleware Telemetry.middleware
    |> App.middleware SecureHeaders.middleware
    |> App.middleware Compress.auto
    |> App.services [
        Service.instance appConfig
        Service.scoped<IDbContext, AppDbContext>
    ]
    |> App.onError (fun ex req -> task {
        return Response.json {| error = "Internal server error" |} |> Response.status 500
    })
    |> App.shutdownTimeout (System.TimeSpan.FromSeconds 30.0)

// --- Start ---
App.run routes config System.Threading.CancellationToken.None
```

## Package Dependencies

Each library is independently versioned and published as a NuGet package. Only add what you need:

```xml
<ItemGroup>
    <PackageReference Include="Firefly" Version="0.1.0" />
    <PackageReference Include="Flame" Version="0.1.0" />
    <!-- Optional -->
    <PackageReference Include="Flare" Version="0.1.0" />
    <PackageReference Include="Evlog" Version="0.1.0" />
    <PackageReference Include="Rhinox" Version="0.1.0" />
</ItemGroup>
```

Firefly has a direct dependency on Flame for the `Schema` module. The others are optional and integrated via DI.

