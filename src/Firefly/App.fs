namespace Firefly

open System
open System.Collections.Generic
open System.Net
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

[<RequireQualifiedAccess>]
module App =

    let defaults = {
        Port = 3000
        Host = "localhost"
        OnError = None
        NotFound = None
        Middlewares = []
        ShutdownTimeout = None
        Services = []
        Configure = None
        GrpcServices = []
    }

    let port p config = { config with Port = p }
    let host h config = { config with Host = h }
    let onError handler config = { config with OnError = Some handler }
    let notFound handler config = { config with NotFound = Some handler }
    let middleware mw (config: FireConfig) = { config with Middlewares = config.Middlewares @ [mw] }
    let shutdownTimeout ts config = { config with ShutdownTimeout = Some ts }
    let services (registrations: ServiceRegistration list) config = { config with Services = config.Services @ registrations }
    let configure fn config = { config with Configure = Some fn }
    let grpc (service: GrpcServiceConfig) config = { config with GrpcServices = config.GrpcServices @ [service] }

    let private buildTrie (routes: RouteTable) : TrieNode =
        let mutable trie = Trie.empty
        for entry in routes.Routes |> List.rev do
            trie <- Trie.add entry.Method entry.Pattern entry.Middlewares entry.Handler trie
        trie

    let private handleRequest (trie: TrieNode) (config: FireConfig) (ctx: HttpContext) = task {
        let baseHandler : Handler = fun _req -> Internal.dispatchRequest trie config ctx

        let composed = List.foldBack (fun (mw: Middleware) (h: Handler) -> mw h) config.Middlewares baseHandler

        let req = Request(ctx, Internal.emptyParams)
        try
            let! response = composed req
            do! Internal.writeResponse ctx response
        with ex ->
            match config.OnError with
            | Some errorHandler ->
                try
                    let errorReq =
                        match ctx.Items.TryGetValue("fire.current.request") with
                        | true, r -> r :?> Request
                        | false, _ -> req
                    let! response = errorHandler ex errorReq
                    do! Internal.writeResponse ctx response
                with _ ->
                    ctx.Response.StatusCode <- 500
            | None ->
                ctx.Response.StatusCode <- 500
    }

    let private resolveHost (host: string) =
        if host = "localhost" then IPAddress.Loopback
        else IPAddress.Parse(host)

    let private applyConfig (builder: WebApplicationBuilder) (config: FireConfig) =
        match config.ShutdownTimeout with
        | Some ts ->
            builder.Services.Configure<HostOptions>(fun (opts: HostOptions) ->
                opts.ShutdownTimeout <- ts
            ) |> ignore
        | None -> ()
        for reg in config.Services do
            match reg with
            | Singleton (svc, impl) -> builder.Services.AddSingleton(svc, impl) |> ignore
            | SingletonFactory (svc, factory) -> builder.Services.AddSingleton(svc, factory) |> ignore
            | SingletonInstance (svc, inst) -> builder.Services.AddSingleton(svc, inst) |> ignore
            | Transient (svc, impl) -> builder.Services.AddTransient(svc, impl) |> ignore
            | TransientFactory (svc, factory) -> builder.Services.AddTransient(svc, factory) |> ignore
            | Scoped (svc, impl) -> builder.Services.AddScoped(svc, impl) |> ignore
            | ScopedFactory (svc, factory) -> builder.Services.AddScoped(svc, factory) |> ignore
            | RawConfigure configure -> configure builder.Services
        if config.GrpcServices.Length > 0 then
            builder.Services.AddGrpc() |> ignore

    let private isDevelopment () =
        let env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        String.Equals(env, "Development", StringComparison.OrdinalIgnoreCase)
        || String.Equals(env, "dev", StringComparison.OrdinalIgnoreCase)

    /// Starts the server. Pass CancellationToken to stop.
    /// In Development mode, automatically enables live reload (SSE + script injection).
    let run (routes: RouteTable) (config: FireConfig) (ct: CancellationToken) : Task =
        let devMode = isDevelopment ()
        let config =
            if devMode then config |> middleware LiveReload.middleware
            else config
        let trie = buildTrie routes
        let builder = WebApplication.CreateBuilder()
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(resolveHost config.Host, config.Port)
        ) |> ignore
        applyConfig builder config
        let app = builder.Build()
        app.UseWebSockets() |> ignore
        // In dev mode, register the SSE live reload endpoint
        if devMode then
            app.Map("/__fire/livereload", RequestDelegate(LiveReload.endpoint)) |> ignore
        if config.GrpcServices.Length > 0 then
            app.UseRouting() |> ignore
            for svc in config.GrpcServices do
                GrpcRuntime.mapEndpoints app svc
        match config.Configure with
        | Some configure -> configure (app :> IApplicationBuilder)
        | None -> ()
        (app :> IApplicationBuilder).Run(RequestDelegate(fun ctx -> handleRequest trie config ctx))
        (app :> IHost).RunAsync(ct)

    /// Test helper: starts on port 0, returns (actualPort, stopTask).
    /// The caller should cancel the CancellationTokenSource when done, then await the stop task.
    let runTest (routes: RouteTable) (config: FireConfig) (ct: CancellationToken) : Task<int * (unit -> Task)> = task {
        let trie = buildTrie routes
        let builder = WebApplication.CreateBuilder()
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(System.Net.IPAddress.Loopback, 0)
        ) |> ignore
        applyConfig builder config
        let app = builder.Build()
        app.UseWebSockets() |> ignore
        if config.GrpcServices.Length > 0 then
            app.UseRouting() |> ignore
            for svc in config.GrpcServices do
                GrpcRuntime.mapEndpoints app svc
        match config.Configure with
        | Some configure -> configure (app :> IApplicationBuilder)
        | None -> ()
        (app :> IApplicationBuilder).Run(RequestDelegate(fun ctx -> handleRequest trie config ctx))
        do! app.StartAsync(ct)
        let server = app.Services.GetRequiredService<IServer>()
        let serverAddresses = server.Features.Get<IServerAddressesFeature>()
        let address = serverAddresses.Addresses |> Seq.head
        let uri = System.Uri(address)
        return (uri.Port, fun () -> app.StopAsync(CancellationToken.None))
    }
