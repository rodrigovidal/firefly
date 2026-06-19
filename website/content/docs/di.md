---
title: "Dependency Injection"
description: "Automatic service resolution inside handlers."
group: "Core"
order: 4
---

# Dependency Injection

Firefly integrates with the built-in .NET dependency injection container. Services registered via `App.services` are available for auto-injection into route handlers.

## Registering Services

Use the `Service` module to create registrations and pass them to `App.services`:

```fsharp
let config =
    App.defaults
    |> App.services [
        Service.singleton<IUserRepository, UserRepository>
        Service.singleton<IEmailService, EmailService>
        Service.transient<IOrderService, OrderService>
        Service.scoped<IDbContext, AppDbContext>
    ]
```

## Service Lifetimes

| Registration | Lifetime | Description |
|-------------|----------|-------------|
| `Service.singleton<'S, 'I>` | Singleton | One instance for the app lifetime |
| `Service.singletonFactory fn` | Singleton | Created once via factory function |
| `Service.instance value` | Singleton | A pre-built instance |
| `Service.transient<'S, 'I>` | Transient | New instance per resolution |
| `Service.transientFactory fn` | Transient | New instance per resolution via factory |
| `Service.scoped<'S, 'I>` | Scoped | One instance per request |
| `Service.scopedFactory fn` | Scoped | One per request via factory |
| `Service.raw fn` | N/A | Direct access to `IServiceCollection` |

### Factory Registrations

When you need custom initialization:

```fsharp
App.defaults
|> App.services [
    Service.singletonFactory (fun sp ->
        let config = sp.GetRequiredService<AppConfig>()
        new PostgresUserRepository(config.ConnectionString) :> IUserRepository
    )
]
```

### Instance Registration

Register a pre-existing value:

```fsharp
let appConfig = Env.load<AppConfig>()

App.defaults
|> App.services [
    Service.instance appConfig
]
```

### Raw Configuration

For advanced scenarios or third-party library integrations:

```fsharp
App.defaults
|> App.services [
    Service.raw (fun services ->
        services.AddHttpClient() |> ignore
        services.AddMemoryCache() |> ignore
    )
]
```

## Auto-Injection in Handlers

When a handler parameter is an interface or abstract type, Firefly automatically resolves it from the DI container. No attributes or special syntax needed:

```fsharp
type IUserRepository =
    abstract GetAll : unit -> Task<User list>
    abstract GetById : int -> Task<User option>

// IUserRepository is injected automatically
let listUsers (repo: IUserRepository) (req: Request) = task {
    let! users = repo.GetAll()
    return Response.json users
}

let getUser (id: int) (repo: IUserRepository) (req: Request) = task {
    match! repo.GetById id with
    | Some user -> return Response.json user
    | None -> return Response.notFound
}

Route.start
|> Route.get "/users" listUsers
|> Route.get "/users/%i" getUser
```

The order of parameters in the function signature does not matter for DI vs route params -- Firefly classifies each parameter by its type:

- Concrete value types matching format specifiers (`int`, `string`, `bool`, `float`) are route parameters
- `Request` is the request object
- Interfaces and abstract types are resolved from DI
- Records and classes on POST/PUT/PATCH are deserialized from the JSON body
- Records and classes on GET/DELETE are bound from the query string

### Multiple Injected Services

```fsharp
let createOrder
    (users: IUserRepository)
    (orders: IOrderService)
    (email: IEmailService)
    (body: CreateOrderRequest)
    (req: Request) = task {
        let! user = users.GetById body.UserId
        match user with
        | None -> return Response.notFound
        | Some u ->
            let! order = orders.Create body
            do! email.SendConfirmation u.Email order.Id
            return Response.json order |> Response.status 201
    }

Route.post "/orders" createOrder
```

## Accessing Services Manually

If you need to resolve services outside of auto-injection:

```fsharp
let handler (req: Request) = task {
    let repo = req.Raw.RequestServices.GetRequiredService<IUserRepository>()
    let! users = repo.GetAll()
    return Response.json users
}
```

## Configure Callback

For additional WebApplication configuration (e.g., adding ASP.NET middleware):

```fsharp
App.defaults
|> App.configure (fun app ->
    app.UseStaticFiles() |> ignore
)
```

