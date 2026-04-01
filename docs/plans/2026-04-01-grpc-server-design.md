# Fire gRPC Server — Design

## Overview

Proto-first gRPC server support for Fire. Services are defined via `.proto` files (code-generated with `Grpc.Tools`), wrapped in an F# computation expression for ergonomic handler definition. Wire-compatible with Flare's gRPC client.

## Approach

- `.proto` file is the source of truth
- `Grpc.Tools` generates C# base class + message types
- Fire's `grpc { }` CE wraps the generated base class — no manual inheritance
- `App.grpc` registers the service with Kestrel's gRPC pipeline
- Reflection enabled by default for `grpcurl` / service discovery

## Dependencies

```xml
<PackageReference Include="Grpc.AspNetCore" Version="2.71.0" />
<PackageReference Include="Grpc.AspNetCore.Server.Reflection" Version="2.71.0" />
```

## API

### Defining a service

```fsharp
// greeter.proto generates: GreeterBase, HelloRequest, HelloReply

let greeterService = grpcService<Greeter.GreeterBase> {
    unary "SayHello" (fun (req: HelloRequest) (ctx: ServerCallContext) -> task {
        return HelloReply(Message = $"Hello, {req.Name}!")
    })
    serverStream "ListUsers" (fun (req: ListRequest) (writer: IServerStreamWriter<UserReply>) (ctx: ServerCallContext) -> task {
        for user in users do
            do! writer.WriteAsync(user)
    })
}
```

### Registering with Fire

```fsharp
App.defaults
|> App.grpc greeterService
|> App.grpc orderService
|> App.services [ Service.singleton<IUserStore, UserStore> ]
|> App.run routes
```

`App.grpc` adds the service to the gRPC pipeline. Multiple services can be registered. gRPC and REST routes coexist on the same port.

### Full example

```fsharp
// Proto file: protos/greeter.proto
// syntax = "proto3";
// package greet;
// service Greeter {
//   rpc SayHello (HelloRequest) returns (HelloReply);
//   rpc SayHelloStream (HelloRequest) returns (stream HelloReply);
// }

open Fire
open Greet // generated namespace

let greeter = grpcService<Greeter.GreeterBase> {
    unary "SayHello" (fun (req: HelloRequest) ctx -> task {
        return HelloReply(Message = $"Hello, {req.Name}!")
    })
    serverStream "SayHelloStream" (fun (req: HelloRequest) (writer: IServerStreamWriter<HelloReply>) ctx -> task {
        for i in 1..5 do
            do! writer.WriteAsync(HelloReply(Message = $"Hello #{i}, {req.Name}!"))
            do! System.Threading.Tasks.Task.Delay(500)
    })
}

[<EntryPoint>]
let main _ =
    let routes = Route.start |> Route.get "/health" (fun _ -> task { return Response.text "ok" })

    App.defaults
    |> App.port 5000
    |> App.grpc greeter
    |> App.services [ Service.singleton<IUserStore, InMemoryUserStore> ]
    |> App.run routes (System.Threading.CancellationToken.None)
    |> fun t -> t.Wait()
    0
```

### Consuming with Flare

```fsharp
match! grpc greeterClient {
    call (_.SayHelloAsync) (HelloRequest(Name = "Alice"))
    deadline 5000
} with
| GrpcResponse.Ok reply -> printfn $"{reply.Message}"
| GrpcResponse.Unavailable err -> printfn $"Down: {err.Detail}"
| _ -> ...
```

Same `.proto`, same wire format, both sides idiomatic F#.

## Implementation

### Types — `src/Fire/Grpc.fs`

```fsharp
namespace Fire

open System
open System.Threading.Tasks
open Grpc.Core

type GrpcMethodHandler =
    | UnaryHandler of name: string * handler: (obj -> ServerCallContext -> Task<obj>)
    | ServerStreamHandler of name: string * handler: (obj -> obj -> ServerCallContext -> Task)

type GrpcServiceDefinition = {
    ServiceType: Type
    Methods: GrpcMethodHandler list
}
```

### CE — `GrpcServiceBuilder`

```fsharp
type GrpcServiceBuilder<'TBase>() =
    member _.Yield(_) = { ServiceType = typeof<'TBase>; Methods = [] }

    [<CustomOperation("unary")>]
    member _.Unary(state, name, handler: 'TReq -> ServerCallContext -> Task<'TResp>) =
        let wrapped = fun (req: obj) (ctx: ServerCallContext) -> task {
            let! result = handler (req :?> 'TReq) ctx
            return box result
        }
        { state with Methods = UnaryHandler(name, wrapped) :: state.Methods }

    [<CustomOperation("serverStream")>]
    member _.ServerStream(state, name, handler: 'TReq -> IServerStreamWriter<'TResp> -> ServerCallContext -> Task) =
        let wrapped = fun (req: obj) (writer: obj) (ctx: ServerCallContext) -> task {
            do! handler (req :?> 'TReq) (writer :?> IServerStreamWriter<'TResp>) ctx
        }
        { state with Methods = ServerStreamHandler(name, wrapped) :: state.Methods }

    member _.Run(state) = state

[<AutoOpen>]
module GrpcExtensions =
    let grpcService<'TBase> = GrpcServiceBuilder<'TBase>()
```

### App integration

```fsharp
// In Types.fs — add to FireConfig:
type FireConfig = {
    // ... existing fields ...
    GrpcServices: GrpcServiceDefinition list
}

// In App module:
let grpc (service: GrpcServiceDefinition) config =
    { config with GrpcServices = config.GrpcServices @ [service] }
```

### Server wiring — in `App.run`

```fsharp
// Add gRPC services to the builder
if config.GrpcServices.Length > 0 then
    builder.Services.AddGrpc() |> ignore

// After app.Build(), map each service
for service in config.GrpcServices do
    // Use reflection to call app.MapGrpcService<TBase>()
    let mapMethod = typeof<GrpcEndpointRouteBuilderExtensions>.GetMethod("MapGrpcService")
    let generic = mapMethod.MakeGenericMethod(service.ServiceType)
    generic.Invoke(null, [| app |]) |> ignore

// Enable reflection for grpcurl
if config.GrpcServices.Length > 0 then
    app.MapGrpcReflectionService() |> ignore
```

The tricky part: generating a concrete subclass of `GreeterBase` at runtime that delegates to the CE handlers. This requires either:
- Runtime type generation (Reflection.Emit) — complex but works
- A source generator — cleaner but requires build tooling
- A simple code-gen step via `fire gen grpc` — pragmatic

### Pragmatic approach: `fire gen grpc`

Rather than runtime type generation, `fire gen grpc` reads the `.proto` and generates an F# file with the base class override that delegates to the handler map:

```fsharp
// Generated: GreeterServiceImpl.fs
type GreeterServiceImpl(handlers: Map<string, obj>) =
    inherit Greeter.GreeterBase()

    override _.SayHello(request, context) =
        let handler = handlers.["SayHello"] :?> (HelloRequest -> ServerCallContext -> Task<HelloReply>)
        handler request context

    override _.SayHelloStream(request, responseStream, context) =
        let handler = handlers.["SayHelloStream"] :?> (HelloRequest -> IServerStreamWriter<HelloReply> -> ServerCallContext -> Task)
        handler request responseStream context
```

The `grpcService { }` CE builds the handler map. The generated class delegates to it. No runtime codegen needed.

## Implementation Order

1. Add `Grpc.AspNetCore` dependency to Fire.fsproj
2. Create `src/Fire/Grpc.fs` with types and CE
3. Add `GrpcServices` to `FireConfig`
4. Add `App.grpc` function
5. Wire gRPC in `App.run` and `App.runTest`
6. Add `fire gen grpc` to CLI
7. Create example: greeter service with unary + server stream
8. Test with `grpcurl`

## What stays the same

- All existing REST routing, middleware, DI
- gRPC and REST coexist on the same port
- Existing tests unaffected

## Future

- Client streaming and bidirectional in the CE
- DI injection in handlers (like Fire's auto-DI for REST handlers)
- `fire gen proto` to export .proto from service definition
- Interceptor support in the CE
