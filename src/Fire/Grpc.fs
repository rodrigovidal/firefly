namespace Fire

open System
open System.Buffers.Binary
open System.Threading.Tasks
open Grpc.Core
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing

[<RequireQualifiedAccess>]
module GrpcRuntime =

    let buildMarshaller (t: Type) : Marshaller<obj> =
        let toBytes (msg: obj) : byte[] =
            let m = t.GetMethod("ToByteArray")
            m.Invoke(msg, [||]) :?> byte[]
        let fromBytes (bytes: byte[]) : obj =
            let msg = Activator.CreateInstance(t)
            let mergeFrom = t.GetMethod("MergeFrom", [| typeof<byte[]> |])
            mergeFrom.Invoke(msg, [| box bytes |]) |> ignore
            msg
        Marshallers.Create(Func<obj, byte[]>(toBytes), Func<byte[], obj>(fromBytes))

    let build (config: GrpcServiceConfig) : ServerServiceDefinition =
        let mutable builder = ServerServiceDefinition.CreateBuilder()
        for m in config.Methods do
            match m with
            | GrpcUnary(name, handler, reqType, respType) ->
                let method = Method<obj, obj>(MethodType.Unary, config.ServiceName, name, buildMarshaller reqType, buildMarshaller respType)
                builder <- builder.AddMethod(method, UnaryServerMethod<obj, obj>(fun req ctx -> handler req ctx))
            | GrpcServerStream(name, handler, reqType, respType) ->
                let method = Method<obj, obj>(MethodType.ServerStreaming, config.ServiceName, name, buildMarshaller reqType, buildMarshaller respType)
                builder <- builder.AddMethod(method, ServerStreamingServerMethod<obj, obj>(fun req writer ctx -> handler req (box writer) ctx))
        builder.Build()

    let private readGrpcMessage (ctx: HttpContext) (deserialize: byte[] -> obj) = task {
        let body = ctx.Request.Body
        let header = Array.zeroCreate<byte> 5
        do! body.ReadExactlyAsync(header, 0, 5)
        let length = BinaryPrimitives.ReadInt32BigEndian(ReadOnlySpan(header, 1, 4))
        let payload = Array.zeroCreate<byte> length
        do! body.ReadExactlyAsync(payload, 0, length)
        return deserialize payload
    }

    let private writeGrpcMessage (ctx: HttpContext) (serialize: obj -> byte[]) (msg: obj) = task {
        let payload = serialize msg
        let header = Array.zeroCreate<byte> 5
        header.[0] <- 0uy
        BinaryPrimitives.WriteInt32BigEndian(Span(header, 1, 4), payload.Length)
        do! ctx.Response.Body.WriteAsync(header)
        do! ctx.Response.Body.WriteAsync(payload)
    }

    let mapEndpoints (app: IEndpointRouteBuilder) (config: GrpcServiceConfig) =
        let reqMarshaller = buildMarshaller
        for m in config.Methods do
            match m with
            | GrpcUnary(name, handler, reqType, _respType) ->
                let path = $"/{config.ServiceName}/{name}"
                let toBytes (msg: obj) : byte[] =
                    let meth = msg.GetType().GetMethod("ToByteArray")
                    meth.Invoke(msg, [||]) :?> byte[]
                let fromBytes (bytes: byte[]) : obj =
                    let msg = Activator.CreateInstance(reqType)
                    let mergeFrom = reqType.GetMethod("MergeFrom", [| typeof<byte[]> |])
                    mergeFrom.Invoke(msg, [| box bytes |]) |> ignore
                    msg
                app.MapPost(path, RequestDelegate(fun ctx -> task {
                    ctx.Response.ContentType <- "application/grpc"
                    ctx.Response.AppendTrailer("grpc-status", "0")
                    let! reqMsg = readGrpcMessage ctx fromBytes
                    let callContext = Unchecked.defaultof<ServerCallContext>
                    let! respMsg = handler reqMsg callContext
                    do! writeGrpcMessage ctx toBytes respMsg
                })) |> ignore
            | GrpcServerStream(name, handler, reqType, _respType) ->
                let path = $"/{config.ServiceName}/{name}"
                let toBytes (msg: obj) : byte[] =
                    let meth = msg.GetType().GetMethod("ToByteArray")
                    meth.Invoke(msg, [||]) :?> byte[]
                let fromBytes (bytes: byte[]) : obj =
                    let msg = Activator.CreateInstance(reqType)
                    let mergeFrom = reqType.GetMethod("MergeFrom", [| typeof<byte[]> |])
                    mergeFrom.Invoke(msg, [| box bytes |]) |> ignore
                    msg
                app.MapPost(path, RequestDelegate(fun ctx -> task {
                    ctx.Response.ContentType <- "application/grpc"
                    ctx.Response.AppendTrailer("grpc-status", "0")
                    let! reqMsg = readGrpcMessage ctx fromBytes
                    let callContext = Unchecked.defaultof<ServerCallContext>
                    let streamWriter = { new IServerStreamWriter<obj> with
                        member _.WriteAsync(msg) =
                            writeGrpcMessage ctx toBytes msg
                        member _.WriteOptions
                            with get() = Unchecked.defaultof<WriteOptions>
                            and set(_) = ()
                    }
                    do! handler reqMsg (box streamWriter) callContext
                })) |> ignore

type GrpcServiceBuilder(serviceName: string) =
    member _.Yield(_) = { ServiceName = serviceName; Methods = [] }

    [<CustomOperation("unary")>]
    member _.Unary(state: GrpcServiceConfig, name: string, handler: 'TReq -> ServerCallContext -> Task<'TResp>) : GrpcServiceConfig =
        let wrapped = fun (req: obj) (ctx: ServerCallContext) -> task {
            let! result = handler (req :?> 'TReq) ctx
            return box result
        }
        { state with Methods = GrpcUnary(name, wrapped, typeof<'TReq>, typeof<'TResp>) :: state.Methods }

    [<CustomOperation("serverStream")>]
    member _.ServerStream(state: GrpcServiceConfig, name: string, handler: 'TReq -> IServerStreamWriter<'TResp> -> ServerCallContext -> Task) : GrpcServiceConfig =
        let wrapped = fun (req: obj) (writer: obj) (ctx: ServerCallContext) ->
            handler (req :?> 'TReq) (writer :?> IServerStreamWriter<'TResp>) ctx
        { state with Methods = GrpcServerStream(name, wrapped, typeof<'TReq>, typeof<'TResp>) :: state.Methods }

    member _.Run(state) = state

[<AutoOpen>]
module GrpcCE =
    let grpcService (serviceName: string) = GrpcServiceBuilder(serviceName)
