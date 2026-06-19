---
title: "gRPC"
description: "Serve gRPC endpoints alongside HTTP."
group: "Features"
order: 3
---

# gRPC

Firefly includes a gRPC server with a computation expression for defining services.

## Setup

Add Protobuf references to your `.fsproj`:

```xml
<ItemGroup>
    <PackageReference Include="Google.Protobuf" Version="3.*" />
    <PackageReference Include="Grpc.Core.Api" Version="2.*" />
    <PackageReference Include="Grpc.Tools" Version="2.*" PrivateAssets="All" />
</ItemGroup>

<ItemGroup>
    <Protobuf Include="Protos/*.proto" GrpcServices="Server" />
</ItemGroup>
```

Define your service in a `.proto` file:

```protobuf
syntax = "proto3";

package greeter;

service Greeter {
    rpc SayHello (HelloRequest) returns (HelloReply);
    rpc StreamGreetings (HelloRequest) returns (stream HelloReply);
}

message HelloRequest {
    string name = 1;
}

message HelloReply {
    string message = 1;
}
```

## Defining a gRPC Service

Use the `grpcService` computation expression:

```fsharp
open Firefly
open Grpc.Core
open Greeter  // generated from .proto

let greeterService =
    grpcService "greeter.Greeter" {
        unary "SayHello" (fun (req: HelloRequest) (ctx: ServerCallContext) -> task {
            let reply = HelloReply()
            reply.Message <- $"Hello, {req.Name}!"
            return reply
        })

        serverStream "StreamGreetings" (fun (req: HelloRequest) (writer: IServerStreamWriter<HelloReply>) (ctx: ServerCallContext) -> task {
            for i in 1..5 do
                let reply = HelloReply()
                reply.Message <- $"Hello #{i}, {req.Name}!"
                do! writer.WriteAsync(reply)
                do! System.Threading.Tasks.Task.Delay(500)
        })
    }
```

## Registering the Service

Register gRPC services with `App.grpc`:

```fsharp
let config =
    App.defaults
    |> App.port 5000
    |> App.grpc greeterService

App.run routes config CancellationToken.None
```

Multiple gRPC services can be registered:

```fsharp
App.defaults
|> App.grpc greeterService
|> App.grpc orderService
|> App.grpc userService
```

## Method Types

| CE Operation | gRPC Type | Signature |
|-------------|-----------|-----------|
| `unary` | Unary | `'TReq -> ServerCallContext -> Task<'TResp>` |
| `serverStream` | Server streaming | `'TReq -> IServerStreamWriter<'TResp> -> ServerCallContext -> Task` |

The service name in `grpcService "package.ServiceName"` must match the fully-qualified service name from the `.proto` file.

## How It Works

Firefly maps gRPC methods to HTTP/2 POST endpoints following the gRPC-over-HTTP/2 protocol. For `greeter.Greeter/SayHello`, a POST route is created at `/greeter.Greeter/SayHello` that handles the length-prefixed Protobuf wire format. The framework uses `MergeFrom`/`ToByteArray` on the generated message types for serialization.

