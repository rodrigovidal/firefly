namespace Fire

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
}

[<RequireQualifiedAccess>]
module App =

    let defaults = {
        Port = 3000
        Host = "localhost"
        OnError = None
        NotFound = None
    }

    let port p config = { config with Port = p }
    let host h config = { config with Host = h }
    let onError handler config = { config with OnError = Some handler }
    let notFound handler config = { config with NotFound = Some handler }

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
            ctx.Response.ContentType <- "text/plain; charset=utf-8"
            do! ctx.Response.WriteAsync(s)
        | Json bytes ->
            ctx.Response.ContentType <- "application/json; charset=utf-8"
            do! ctx.Response.Body.WriteAsync(System.ReadOnlyMemory(bytes))
        | Stream stream ->
            use stream = stream
            do! stream.CopyToAsync(ctx.Response.Body)
    }

    let private handleRequest (trie: TrieNode) (config: FireConfig) (ctx: HttpContext) = task {
        let path = ctx.Request.Path.Value
        let method' = ctx.Request.Method

        match Trie.lookup method' path trie with
        | Some (handler, ps) ->
            let req = Request(ctx, ps)
            try
                let! response = handler req
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
        | None ->
            match config.NotFound with
            | Some nfHandler ->
                let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
                let! response = nfHandler req
                do! writeResponse ctx response
            | None ->
                ctx.Response.StatusCode <- 404
    }

    let private resolveHost (host: string) =
        if host = "localhost" then IPAddress.Loopback
        else IPAddress.Parse(host)

    /// Starts the server. Pass CancellationToken to stop.
    let run (routes: RouteTable) (config: FireConfig) (ct: CancellationToken) : Task =
        let trie = buildTrie routes
        let builder = WebApplication.CreateBuilder()
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(resolveHost config.Host, config.Port)
        ) |> ignore
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
        let app = builder.Build()
        (app :> IApplicationBuilder).Run(RequestDelegate(fun ctx -> handleRequest trie config ctx))
        do! app.StartAsync(ct)
        let server = app.Services.GetRequiredService<IServer>()
        let serverAddresses = server.Features.Get<IServerAddressesFeature>()
        let address = serverAddresses.Addresses |> Seq.head
        let uri = System.Uri(address)
        return (uri.Port, fun () -> app.StopAsync(CancellationToken.None))
    }
