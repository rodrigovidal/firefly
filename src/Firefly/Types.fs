namespace Firefly

open System
open System.Threading.Tasks
open Grpc.Core
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

type Handler = Request -> Task<Response>
type Middleware = Handler -> Handler

type Pipeline = {
    Name: string
    Middlewares: Middleware list
}

[<RequireQualifiedAccess>]
module Pipeline =
    let create (name: string) : Pipeline =
        { Name = name; Middlewares = [] }

    let plug (mw: Middleware) (pipeline: Pipeline) : Pipeline =
        { pipeline with Middlewares = pipeline.Middlewares @ [ mw ] }

    let empty : Pipeline =
        { Name = "empty"; Middlewares = [] }

type ServiceRegistration =
    | Singleton of serviceType: Type * implType: Type
    | SingletonFactory of serviceType: Type * factory: (IServiceProvider -> obj)
    | SingletonInstance of serviceType: Type * instance: obj
    | Transient of serviceType: Type * implType: Type
    | TransientFactory of serviceType: Type * factory: (IServiceProvider -> obj)
    | Scoped of serviceType: Type * implType: Type
    | ScopedFactory of serviceType: Type * factory: (IServiceProvider -> obj)
    | RawConfigure of configure: (IServiceCollection -> unit)

[<RequireQualifiedAccess>]
module Service =
    /// Singleton registered by type. The container constructs 'TImpl by
    /// reflection (ActivatorUtilities). For NativeAOT/trim, prefer singletonFactory.
    let singleton<'TService, 'TImpl> =
        Singleton(typeof<'TService>, typeof<'TImpl>)

    /// Singleton built by an explicit factory — reflection-free, AOT/trim-safe.
    let singletonFactory (factory: IServiceProvider -> 'T) =
        SingletonFactory(typeof<'T>, fun sp -> box (factory sp))

    /// Register a pre-built instance — reflection-free, AOT/trim-safe.
    let instance (value: 'T) =
        SingletonInstance(typeof<'T>, box value)

    /// Transient registered by type. The container constructs 'TImpl by
    /// reflection (ActivatorUtilities). For NativeAOT/trim, prefer transientFactory.
    let transient<'TService, 'TImpl> =
        Transient(typeof<'TService>, typeof<'TImpl>)

    /// Transient built by an explicit factory — reflection-free, AOT/trim-safe.
    let transientFactory (factory: IServiceProvider -> 'T) =
        TransientFactory(typeof<'T>, fun sp -> box (factory sp))

    /// Scoped registered by type. The container constructs 'TImpl by
    /// reflection (ActivatorUtilities). For NativeAOT/trim, prefer scopedFactory.
    let scoped<'TService, 'TImpl> =
        Scoped(typeof<'TService>, typeof<'TImpl>)

    /// Scoped built by an explicit factory — reflection-free, AOT/trim-safe.
    let scopedFactory (factory: IServiceProvider -> 'T) =
        ScopedFactory(typeof<'T>, fun sp -> box (factory sp))

    let raw (fn: IServiceCollection -> unit) =
        RawConfigure fn

type GrpcMethod =
    | GrpcUnary of name: string * handler: (obj -> ServerCallContext -> Task<obj>) * requestType: Type * responseType: Type
    | GrpcServerStream of name: string * handler: (obj -> obj -> ServerCallContext -> Task) * requestType: Type * responseType: Type

type GrpcServiceConfig = {
    ServiceName: string
    Methods: GrpcMethod list
}

type FireConfig = {
    Port: int
    Host: string
    OnError: (exn -> Request -> Task<Response>) option
    NotFound: (Request -> Task<Response>) option
    Middlewares: Middleware list
    ShutdownTimeout: TimeSpan option
    Services: ServiceRegistration list
    Configure: (IApplicationBuilder -> unit) option
    GrpcServices: GrpcServiceConfig list
}
