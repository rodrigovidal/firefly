namespace Fire

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

type FireConfig = {
    Port: int
    Host: string
    OnError: (exn -> Request -> Task<Response>) option
    NotFound: (Request -> Task<Response>) option
    Middlewares: Middleware list
    ShutdownTimeout: TimeSpan option
    DependencyInjection: (IServiceCollection -> unit) option
}

[<RequireQualifiedAccess>]
module App =

    let defaults = {
        Port = 3000
        Host = "localhost"
        OnError = None
        NotFound = None
        Middlewares = []
        ShutdownTimeout = None
        DependencyInjection = None
    }

    let port p config = { config with Port = p }
    let host h config = { config with Host = h }
    let onError handler config = { config with OnError = Some handler }
    let notFound handler config = { config with NotFound = Some handler }
    let middleware mw (config: FireConfig) = { config with Middlewares = config.Middlewares @ [mw] }
    let shutdownTimeout ts config = { config with ShutdownTimeout = Some ts }
    let dependencyInjection fn config = { config with DependencyInjection = Some fn }

    let private buildTrie (routes: RouteTable) : TrieNode =
        let mutable trie = Trie.empty
        for entry in routes.Routes do
            trie <- Trie.add entry.Method entry.Pattern entry.Middlewares entry.Handler trie
        trie

    let private writeResponse (ctx: HttpContext) (response: Response) = task {
        ctx.Response.StatusCode <- response.Status
        for (key, value) in response.Headers do
            ctx.Response.Headers.Append(key, value)
        match response.Body with
        | Empty -> ()
        | Text s ->
            let bytes = System.Text.Encoding.UTF8.GetBytes(s)
            ctx.Response.ContentType <- "text/plain; charset=utf-8"
            ctx.Response.ContentLength <- System.Nullable(int64 bytes.Length)
            do! ctx.Response.Body.WriteAsync(System.ReadOnlyMemory(bytes))
        | Json bytes ->
            ctx.Response.ContentType <- "application/json; charset=utf-8"
            ctx.Response.ContentLength <- System.Nullable(int64 bytes.Length)
            do! ctx.Response.Body.WriteAsync(System.ReadOnlyMemory(bytes))
        | Stream stream ->
            use stream = stream
            do! stream.CopyToAsync(ctx.Response.Body)
    }

    let private dispatchRequest (trie: TrieNode) (config: FireConfig) (ctx: HttpContext) : Task<Response> = task {
        let path = ctx.Request.Path.Value
        let method' = ctx.Request.Method
        match Trie.lookup method' path trie with
        | Some (handler, ps) ->
            let req = Request(ctx, ps)
            return! handler req
        | None ->
            match config.NotFound with
            | Some nfHandler ->
                let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
                return! nfHandler req
            | None ->
                return { Status = 404; Headers = []; Body = Empty }
    }

    let private handleRequest (trie: TrieNode) (config: FireConfig) (ctx: HttpContext) = task {
        let baseHandler : Handler = fun _req -> dispatchRequest trie config ctx

        let composed = List.foldBack (fun (mw: Middleware) (h: Handler) -> mw h) config.Middlewares baseHandler

        let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
        try
            let! response = composed req
            do! writeResponse ctx response
        with ex ->
            match config.OnError with
            | Some errorHandler ->
                try
                    let! response = errorHandler ex req
                    do! writeResponse ctx response
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
        match config.DependencyInjection with
        | Some fn -> fn builder.Services
        | None -> ()

    /// Starts the server. Pass CancellationToken to stop.
    let run (routes: RouteTable) (config: FireConfig) (ct: CancellationToken) : Task =
        let trie = buildTrie routes
        let builder = WebApplication.CreateBuilder()
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(resolveHost config.Host, config.Port)
        ) |> ignore
        applyConfig builder config
        let app = builder.Build()
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
        (app :> IApplicationBuilder).Run(RequestDelegate(fun ctx -> handleRequest trie config ctx))
        do! app.StartAsync(ct)
        let server = app.Services.GetRequiredService<IServer>()
        let serverAddresses = server.Features.Get<IServerAddressesFeature>()
        let address = serverAddresses.Addresses |> Seq.head
        let uri = System.Uri(address)
        return (uri.Port, fun () -> app.StopAsync(CancellationToken.None))
    }
