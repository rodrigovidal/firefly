# Fire gRPC Server — Design

## Overview

Proto-first gRPC server support for Fire. Message types come from `.proto` files via `Grpc.Tools`. Services are defined as plain F# functions using a CE — no base class inheritance, no code generation beyond the standard protobuf messages.

Uses `ServerServiceDefinition` directly instead of generated service base classes.

## API

### Defining a service

```fsharp
open Fire
open Greet // generated message types from .proto

let greeter = grpcService "greet.Greeter" {
    unary "SayHello" (fun (req: HelloRequest) ctx -> task {
        return HelloReply(Message = $"Hello, {req.Name}!")
    })
    serverStream "SayHelloStream" (fun (req: HelloRequest) (writer: IServerStreamWriter<HelloReply>) ctx -> task {
        for i in 1..5 do
            do! writer.WriteAsync(HelloReply(Message = $"#{i}, {req.Name}!"))
            do! Task.Delay(500)
    })
}
```

No classes, no inheritance, just functions.

### Registering with Fire

```fsharp
App.defaults
|> App.grpc greeter
|> App.grpc orderService
|> App.run routes
```

gRPC and REST coexist on the same port.

### Consuming with Flare

```fsharp
// Same .proto, same wire format
match! grpc greeterClient {
    call (_.SayHelloAsync) (HelloRequest(Name = "Alice"))
} with
| GrpcResponse.Ok reply -> printfn $"{reply.Message}"
| _ -> ...
```

## Implementation

### Core types — `src/Fire/Grpc.fs`

```fsharp
namespace Fire

open System
open System.Threading.Tasks
open Grpc.Core
open Google.Protobuf

type GrpcMethod =
    | Unary of name: string * handler: (obj -> ServerCallContext -> Task<obj>) * requestType: Type * responseType: Type
    | ServerStream of name: string * handler: (obj -> obj -> ServerCallContext -> Task) * requestType: Type * responseType: Type

type GrpcServiceConfig = {
    ServiceName: string
    Methods: GrpcMethod list
}
```

### Marshaller helper

Protobuf messages implement `IMessage<T>`. We build marshallers from the type:

```fsharp
let private marshallerFor<'T when 'T :> IMessage<'T> and 'T: (new: unit -> 'T)> () =
    Marshallers.Create(
        (fun msg -> msg.ToByteArray()),
        (fun bytes ->
            let msg = Activator.CreateInstance<'T>()
            msg.MergeFrom(bytes)
            msg))
```

### CE builder

```fsharp
type GrpcServiceBuilder(serviceName: string) =
    member _.Yield(_) = { ServiceName = serviceName; Methods = [] }

    [<CustomOperation("unary")>]
    member _.Unary(state, name: string, handler: 'TReq -> ServerCallContext -> Task<'TResp>) =
        let wrapped = fun (req: obj) (ctx: ServerCallContext) -> task {
            let! result = handler (req :?> 'TReq) ctx
            return box result
        }
        { state with Methods = Unary(name, wrapped, typeof<'TReq>, typeof<'TResp>) :: state.Methods }

    [<CustomOperation("serverStream")>]
    member _.ServerStream(state, name: string, handler: 'TReq -> IServerStreamWriter<'TResp> -> ServerCallContext -> Task) =
        let wrapped = fun (req: obj) (writer: obj) (ctx: ServerCallContext) -> task {
            do! handler (req :?> 'TReq) (writer :?> IServerStreamWriter<'TResp>) ctx
        }
        { state with Methods = ServerStream(name, wrapped, typeof<'TReq>, typeof<'TResp>) :: state.Methods }

    member _.Run(state) = state

[<AutoOpen>]
module GrpcCE =
    let grpcService (serviceName: string) = GrpcServiceBuilder(serviceName)
```

### Building ServerServiceDefinition

```fsharp
module GrpcRuntime =

    let private buildMarshaller (t: Type) =
        // Use reflection to call Marshallers.Create with the correct protobuf type
        let toBytes = t.GetMethod("ToByteArray")
        let mergeFrom = t.GetMethod("MergeFrom", [| typeof<byte[]> |])
        Marshallers.Create(
            (fun (msg: obj) -> toBytes.Invoke(msg, [||]) :?> byte[]),
            (fun (bytes: byte[]) ->
                let msg = Activator.CreateInstance(t)
                mergeFrom.Invoke(msg, [| box bytes |]) |> ignore
                msg))

    let build (config: GrpcServiceConfig) : ServerServiceDefinition =
        let mutable builder = ServerServiceDefinition.CreateBuilder()
        for method in config.Methods do
            match method with
            | Unary(name, handler, reqType, respType) ->
                let fullName = $"/{config.ServiceName}/{name}"
                let m = Method<obj, obj>(MethodType.Unary, config.ServiceName, name, buildMarshaller reqType, buildMarshaller respType)
                builder <- builder.AddMethod(m, UnaryServerMethod<obj, obj>(fun req ctx -> handler req ctx))
            | ServerStream(name, handler, reqType, respType) ->
                let fullName = $"/{config.ServiceName}/{name}"
                let m = Method<obj, obj>(MethodType.ServerStreaming, config.ServiceName, name, buildMarshaller reqType, buildMarshaller respType)
                builder <- builder.AddMethod(m, ServerStreamingServerMethod<obj, obj>(fun req writer ctx -> handler req (box writer) ctx))
        builder.Build()
```

### App integration

```fsharp
// FireConfig gets a new field:
GrpcServices: GrpcServiceConfig list

// App module:
let grpc (service: GrpcServiceConfig) config =
    { config with GrpcServices = config.GrpcServices @ [service] }

// In App.run, after builder.Build():
if config.GrpcServices.Length > 0 then
    builder.Services.AddGrpc() |> ignore
    builder.Services.AddGrpcReflection() |> ignore

let app = builder.Build()

if config.GrpcServices.Length > 0 then
    for service in config.GrpcServices do
        let definition = GrpcRuntime.build service
        // Register via UseRouting + endpoint mapping
        app.UseRouting() |> ignore
        app.UseEndpoints(fun endpoints ->
            endpoints.MapGrpcService(definition) // needs custom extension
        ) |> ignore
    app.MapGrpcReflectionService() |> ignore
```

Note: `MapGrpcService` normally takes a type parameter for the service class. Since we're using `ServerServiceDefinition` directly, we need to register it differently — via `ServiceBinderBase` or a custom endpoint. The exact wiring depends on `Grpc.AspNetCore`'s internal API for definition-based registration.

Alternative: use Kestrel's raw HTTP/2 pipeline to serve gRPC without `Grpc.AspNetCore`'s service registration. `ServerServiceDefinition` can be served directly by handling the HTTP/2 request and dispatching to the right method handler.

## Dependencies

```xml
<PackageReference Include="Grpc.AspNetCore" Version="2.71.0" />
<PackageReference Include="Grpc.AspNetCore.Server.Reflection" Version="2.71.0" />
<PackageReference Include="Google.Protobuf" Version="3.29.3" />
```

These are added to Fire.fsproj. Users also need `Grpc.Tools` in their app project for proto compilation.

## What stays the same

- All REST routing, middleware, DI
- gRPC and REST coexist on the same port (HTTP/2 multiplexing)
- Existing tests unaffected

## Future

- Client streaming and bidirectional in the CE
- DI injection in gRPC handlers (resolve from `ServerCallContext.GetHttpContext().RequestServices`)
- Interceptor support in the CE
- `fire gen proto` CLI command to export `.proto` from service definitions
