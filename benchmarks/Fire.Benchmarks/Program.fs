open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Columns
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Exporters
open BenchmarkDotNet.Jobs
open BenchmarkDotNet.Loggers
open BenchmarkDotNet.Running
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Fire

[<Sealed>]
type BenchmarkConfig() as this =
    inherit ManualConfig()

    do
        let job =
            Job.ShortRun
                .AsDefault()
                .WithMsBuildArguments([| "/p:NuGetAudit=false" |])
        this.BuildTimeout <- TimeSpan.FromMinutes(5.0)
        this.AddJob(job) |> ignore
        // ManualConfig starts empty: without these, the summary renders with
        // "no columns to show" and results are never persisted to a report.
        this.AddColumnProvider(DefaultColumnProviders.Instance) |> ignore
        this.AddLogger(ConsoleLogger.Default) |> ignore
        this.AddExporter(MarkdownExporter.GitHub) |> ignore
        this.WithOption(ConfigOptions.JoinSummary, true) |> ignore
        this.WithOption(ConfigOptions.DisableParallelBuild, true) |> ignore

let configureAspNetBuilder (builder: WebApplicationBuilder) =
    builder.Logging.ClearProviders() |> ignore
    builder.Logging.SetMinimumLevel(LogLevel.None) |> ignore
    builder

// Fire's runTest builds its host with default logging providers, which write
// request logs to stdout during measurement. That corrupts BenchmarkDotNet's
// result protocol (reported as NA) and bloats the run log. Clear them.
let silenceFireLogging : ServiceRegistration =
    RawConfigure(fun services ->
        services.AddLogging(fun logging ->
            logging.ClearProviders().SetMinimumLevel(LogLevel.None) |> ignore)
        |> ignore)

[<MemoryDiagnoser>]
type PlainTextBenchmark() =
    let mutable firePort = 0
    let mutable aspnetPort = 0
    let mutable fireStop : (unit -> Task) = fun () -> Task.CompletedTask
    let mutable aspnetApp : WebApplication = null
    let mutable client = Unchecked.defaultof<HttpClient>

    [<GlobalSetup>]
    member _.Setup() = task {
        client <- new HttpClient()

        // Fire server
        let routes =
            Route.start
            |> Route.get "/plaintext" (fun _ -> task { return Response.text "Hello, World!" })
        let config = App.defaults |> App.port 0 |> App.services [silenceFireLogging]
        let! (p, stop) = App.runTest routes config CancellationToken.None
        firePort <- p
        fireStop <- stop

        // ASP.NET Core minimal API server
        let builder = WebApplication.CreateBuilder() |> configureAspNetBuilder
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(IPAddress.Loopback, 0)
        ) |> ignore
        let app = builder.Build()
        app.MapGet("/plaintext", Func<string>(fun () -> "Hello, World!")) |> ignore
        do! app.StartAsync()
        let server = app.Services.GetRequiredService<IServer>()
        let addresses = server.Features.Get<IServerAddressesFeature>()
        let uri = Uri(addresses.Addresses |> Seq.head)
        aspnetPort <- uri.Port
        aspnetApp <- app
    }

    [<GlobalCleanup>]
    member _.Cleanup() = task {
        do! fireStop()
        do! aspnetApp.StopAsync()
        client.Dispose()
    }

    [<Benchmark(Description = "Fire: plaintext")>]
    member _.FirePlaintext() = task {
        let! response = client.GetStringAsync($"http://127.0.0.1:{firePort}/plaintext")
        return response
    }

    [<Benchmark(Description = "ASP.NET Core: plaintext", Baseline = true)>]
    member _.AspNetPlaintext() = task {
        let! response = client.GetStringAsync($"http://127.0.0.1:{aspnetPort}/plaintext")
        return response
    }

[<MemoryDiagnoser>]
type JsonBenchmark() =
    let mutable firePort = 0
    let mutable aspnetPort = 0
    let mutable fireStop : (unit -> Task) = fun () -> Task.CompletedTask
    let mutable aspnetApp : WebApplication = null
    let mutable client = Unchecked.defaultof<HttpClient>

    [<GlobalSetup>]
    member _.Setup() = task {
        client <- new HttpClient()

        // Fire server
        let routes =
            Route.start
            |> Route.get "/json" (fun _ -> task {
                return Response.json {| message = "Hello, World!"; count = 42 |}
            })
        let config = App.defaults |> App.port 0 |> App.services [silenceFireLogging]
        let! (p, stop) = App.runTest routes config CancellationToken.None
        firePort <- p
        fireStop <- stop

        // ASP.NET Core minimal API server
        let builder = WebApplication.CreateBuilder() |> configureAspNetBuilder
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(IPAddress.Loopback, 0)
        ) |> ignore
        let app = builder.Build()
        app.MapGet("/json", Func<IResult>(fun () ->
            Results.Json({| message = "Hello, World!"; count = 42 |}))
        ) |> ignore
        do! app.StartAsync()
        let server = app.Services.GetRequiredService<IServer>()
        let addresses = server.Features.Get<IServerAddressesFeature>()
        let uri = Uri(addresses.Addresses |> Seq.head)
        aspnetPort <- uri.Port
        aspnetApp <- app
    }

    [<GlobalCleanup>]
    member _.Cleanup() = task {
        do! fireStop()
        do! aspnetApp.StopAsync()
        client.Dispose()
    }

    [<Benchmark(Description = "Fire: JSON")>]
    member _.FireJson() = task {
        let! response = client.GetStringAsync($"http://127.0.0.1:{firePort}/json")
        return response
    }

    [<Benchmark(Description = "ASP.NET Core: JSON", Baseline = true)>]
    member _.AspNetJson() = task {
        let! response = client.GetStringAsync($"http://127.0.0.1:{aspnetPort}/json")
        return response
    }

[<MemoryDiagnoser>]
type RouteParamBenchmark() =
    let mutable firePort = 0
    let mutable aspnetPort = 0
    let mutable fireStop : (unit -> Task) = fun () -> Task.CompletedTask
    let mutable aspnetApp : WebApplication = null
    let mutable client = Unchecked.defaultof<HttpClient>

    [<GlobalSetup>]
    member _.Setup() = task {
        client <- new HttpClient()

        // Fire server
        let routes =
            Route.start
            |> Route.get "/users/:id" (fun (req: Request) -> task {
                return Response.json {| id = req.Params.["id"] |}
            })
        let config = App.defaults |> App.port 0 |> App.services [silenceFireLogging]
        let! (p, stop) = App.runTest routes config CancellationToken.None
        firePort <- p
        fireStop <- stop

        // ASP.NET Core minimal API server
        let builder = WebApplication.CreateBuilder() |> configureAspNetBuilder
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(IPAddress.Loopback, 0)
        ) |> ignore
        let app = builder.Build()
        app.MapGet("/users/{id}", Func<string, IResult>(fun id ->
            Results.Json({| id = id |}))
        ) |> ignore
        do! app.StartAsync()
        let server = app.Services.GetRequiredService<IServer>()
        let addresses = server.Features.Get<IServerAddressesFeature>()
        let uri = Uri(addresses.Addresses |> Seq.head)
        aspnetPort <- uri.Port
        aspnetApp <- app
    }

    [<GlobalCleanup>]
    member _.Cleanup() = task {
        do! fireStop()
        do! aspnetApp.StopAsync()
        client.Dispose()
    }

    [<Benchmark(Description = "Fire: route params")>]
    member _.FireRouteParam() = task {
        let! response = client.GetStringAsync($"http://127.0.0.1:{firePort}/users/42")
        return response
    }

    [<Benchmark(Description = "ASP.NET Core: route params", Baseline = true)>]
    member _.AspNetRouteParam() = task {
        let! response = client.GetStringAsync($"http://127.0.0.1:{aspnetPort}/users/42")
        return response
    }

[<MemoryDiagnoser>]
type MiddlewareBenchmark() =
    let mutable firePort = 0
    let mutable aspnetPort = 0
    let mutable fireStop : (unit -> Task) = fun () -> Task.CompletedTask
    let mutable aspnetApp : WebApplication = null
    let mutable client = Unchecked.defaultof<HttpClient>

    [<GlobalSetup>]
    member _.Setup() = task {
        client <- new HttpClient()

        // Fire server with middleware
        let withHeader : Middleware = fun next req -> task {
            let! response = next req
            return response |> Response.header "X-Request-Id" "bench-123"
        }
        let routes =
            Route.start
            |> Route.group "/api" (fun api ->
                api
                |> Route.middleware withHeader
                |> Route.get "/data" (fun _ -> task {
                    return Response.json {| value = 1 |}
                })
            )
        let config = App.defaults |> App.port 0 |> App.services [silenceFireLogging]
        let! (p, stop) = App.runTest routes config CancellationToken.None
        firePort <- p
        fireStop <- stop

        // ASP.NET Core minimal API with middleware
        let builder = WebApplication.CreateBuilder() |> configureAspNetBuilder
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(IPAddress.Loopback, 0)
        ) |> ignore
        let app = builder.Build()
        app.Use(Func<HttpContext, RequestDelegate, Task>(fun ctx next ->
            ctx.Response.OnStarting(fun () ->
                ctx.Response.Headers.["X-Request-Id"] <- "bench-123"
                Task.CompletedTask)
            next.Invoke(ctx)
        )) |> ignore
        app.MapGet("/api/data", Func<IResult>(fun () ->
            Results.Json({| value = 1 |}))
        ) |> ignore
        do! app.StartAsync()
        let server = app.Services.GetRequiredService<IServer>()
        let addresses = server.Features.Get<IServerAddressesFeature>()
        let uri = Uri(addresses.Addresses |> Seq.head)
        aspnetPort <- uri.Port
        aspnetApp <- app
    }

    [<GlobalCleanup>]
    member _.Cleanup() = task {
        do! fireStop()
        do! aspnetApp.StopAsync()
        client.Dispose()
    }

    [<Benchmark(Description = "Fire: middleware + JSON")>]
    member _.FireMiddleware() = task {
        use! response = client.GetAsync($"http://127.0.0.1:{firePort}/api/data")
        if not (response.Headers.Contains("X-Request-Id")) then
            invalidOp "Fire middleware benchmark expected X-Request-Id header"
        return! response.Content.ReadAsStringAsync()
    }

    [<Benchmark(Description = "ASP.NET Core: middleware + JSON", Baseline = true)>]
    member _.AspNetMiddleware() = task {
        use! response = client.GetAsync($"http://127.0.0.1:{aspnetPort}/api/data")
        if not (response.Headers.Contains("X-Request-Id")) then
            invalidOp "ASP.NET Core middleware benchmark expected X-Request-Id header"
        return! response.Content.ReadAsStringAsync()
    }

// --- Route Matching Benchmarks ---

[<MemoryDiagnoser>]
type RouteMatchBenchmark() =
    // Build a realistic route table
    let routes =
        Route.start
        |> Route.get "/" (fun _ -> task { return Response.text "home" })
        |> Route.get "/api/users" (fun _ -> task { return Response.text "users" })
        |> Route.get "/api/users/%i" (fun (_id: int) -> task { return Response.text "user" })
        |> Route.post "/api/users" (fun _ -> task { return Response.text "create" })
        |> Route.get "/api/users/%i/posts" (fun (_id: int) -> task { return Response.text "posts" })
        |> Route.get "/api/users/%i/posts/%i" (fun (_uid: int) (_pid: int) -> task { return Response.text "post" })
        |> Route.get "/api/products" (fun _ -> task { return Response.text "products" })
        |> Route.get "/api/products/%s" (fun (_slug: string) -> task { return Response.text "product" })
        |> Route.get "/api/categories" (fun _ -> task { return Response.text "categories" })
        |> Route.get "/api/categories/%i/products" (fun (_id: int) -> task { return Response.text "cat-products" })
        |> Route.get "/static/*path" (fun _ -> task { return Response.text "static" })

    let mutable trie = Trie.empty

    [<GlobalSetup>]
    member _.Setup() =
        for entry in routes.Routes |> List.rev do
            trie <- Trie.add entry.Method entry.Pattern entry.Middlewares entry.Handler trie

    [<Benchmark(Description = "Static route: /")>]
    member _.StaticRoot() =
        Trie.lookup "GET" "/" trie

    [<Benchmark(Description = "Static route: /api/users")>]
    member _.StaticNested() =
        Trie.lookup "GET" "/api/users" trie

    [<Benchmark(Description = "Param route: /api/users/42")>]
    member _.SingleParam() =
        Trie.lookup "GET" "/api/users/42" trie

    [<Benchmark(Description = "Two params: /api/users/42/posts/7")>]
    member _.TwoParams() =
        Trie.lookup "GET" "/api/users/42/posts/7" trie

    [<Benchmark(Description = "String param: /api/products/widget-pro")>]
    member _.StringParam() =
        Trie.lookup "GET" "/api/products/widget-pro" trie

    [<Benchmark(Description = "Wildcard: /static/css/style.css")>]
    member _.Wildcard() =
        Trie.lookup "GET" "/static/css/style.css" trie

    [<Benchmark(Description = "Miss: /api/nonexistent")>]
    member _.Miss() =
        Trie.lookup "GET" "/api/nonexistent" trie

    [<Benchmark(Description = "Wrong method: POST /api/users/42")>]
    member _.WrongMethod() =
        Trie.lookup "POST" "/api/users/42" trie

// --- In-process dispatch allocation micro-benchmark ---
// Isolates Fire's per-request overhead (routing + handler wrapping + Response
// build + write) from Kestrel/HTTP transport, so allocation deltas are precise
// and iterate in seconds. 'Raw serialize' is the floor: the minimal work any
// JSON endpoint must do. The gap between the two is Fire's framework overhead.

[<MemoryDiagnoser>]
type JsonDispatchBenchmark() =
    let mutable trie = Trie.empty
    let mutable trieTyped = Trie.empty

    [<GlobalSetup>]
    member _.Setup() =
        // Erased-generic handler: `fun _ -> ...` infers the param as obj, forcing
        // Fire's reflection-invoker + struct boxing path.
        let routes =
            Route.start
            |> Route.get "/json" (fun _ -> task {
                return Response.json {| message = "Hello, World!"; count = 42 |}
            })
        for entry in routes.Routes |> List.rev do
            trie <- Trie.add entry.Method entry.Pattern entry.Middlewares entry.Handler trie
        // Typed handler: `Request -> Task<Response>` IS a Handler, so it hits the
        // fast path — no boxing, no invoker indirection.
        let routesTyped =
            Route.start
            |> Route.get "/json" (fun (_req: Request) -> task {
                return Response.json {| message = "Hello, World!"; count = 42 |}
            })
        for entry in routesTyped.Routes |> List.rev do
            trieTyped <- Trie.add entry.Method entry.Pattern entry.Middlewares entry.Handler trieTyped

    [<Benchmark(Description = "Raw serialize (floor)", Baseline = true)>]
    member _.RawSerialize() = task {
        let ctx = DefaultHttpContext()
        ctx.Response.Body <- System.IO.Stream.Null
        let bytes =
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes({| message = "Hello, World!"; count = 42 |})
        ctx.Response.ContentType <- "application/json; charset=utf-8"
        ctx.Response.ContentLength <- Nullable(int64 bytes.Length)
        do! ctx.Response.Body.WriteAsync(ReadOnlyMemory(bytes))
    }

    [<Benchmark(Description = "Fire dispatch")>]
    member _.FireDispatch() = task {
        let ctx = DefaultHttpContext()
        ctx.Request.Path <- PathString("/json")
        ctx.Request.Method <- "GET"
        ctx.Response.Body <- System.IO.Stream.Null
        // Mirror App.handleRequest: throwaway pre-routing Request threads through
        // (empty) middleware, then dispatch builds the real Request and runs the handler.
        let baseHandler : Handler = fun _req -> Internal.dispatchRequest trie App.defaults ctx
        let req = Request(ctx, Internal.emptyParams)
        let! resp = baseHandler req
        do! Internal.writeResponse ctx resp
    }

    [<Benchmark(Description = "Fire dispatch (typed handler)")>]
    member _.FireDispatchTyped() = task {
        let ctx = DefaultHttpContext()
        ctx.Request.Path <- PathString("/json")
        ctx.Request.Method <- "GET"
        ctx.Response.Body <- System.IO.Stream.Null
        let baseHandler : Handler = fun _req -> Internal.dispatchRequest trieTyped App.defaults ctx
        let req = Request(ctx, Internal.emptyParams)
        let! resp = baseHandler req
        do! Internal.writeResponse ctx resp
    }

// --- Large-payload JSON benchmarks ---
// Response.json serializes eagerly into a payload-sized byte[]; ASP.NET serializes
// through a pooled writer and stays flat. A tiny payload hides that, so measure a
// realistic one (50 records) to see whether the byte[] scaling is worth optimizing.

type LargeItem = { Id: int; Name: string; Email: string; Active: bool; Score: float }

let largePayload =
    [| for i in 1..50 ->
        { Id = i; Name = $"user{i}"; Email = $"u{i}@example.io"; Active = (i % 2 = 0); Score = float i * 1.5 } |]

[<MemoryDiagnoser>]
type JsonLargeBenchmark() =
    let mutable firePort = 0
    let mutable aspnetPort = 0
    let mutable fireStop : (unit -> Task) = fun () -> Task.CompletedTask
    let mutable aspnetApp : WebApplication = null
    let mutable client = Unchecked.defaultof<HttpClient>

    [<GlobalSetup>]
    member _.Setup() = task {
        client <- new HttpClient()

        // Fire server
        let routes =
            Route.start
            |> Route.get "/json-large" (fun _ -> task { return Response.json largePayload })
        let config = App.defaults |> App.port 0 |> App.services [silenceFireLogging]
        let! (p, stop) = App.runTest routes config CancellationToken.None
        firePort <- p
        fireStop <- stop

        // ASP.NET Core minimal API server
        let builder = WebApplication.CreateBuilder() |> configureAspNetBuilder
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(IPAddress.Loopback, 0)
        ) |> ignore
        let app = builder.Build()
        app.MapGet("/json-large", Func<IResult>(fun () -> Results.Json(largePayload))) |> ignore
        do! app.StartAsync()
        let server = app.Services.GetRequiredService<IServer>()
        let addresses = server.Features.Get<IServerAddressesFeature>()
        let uri = Uri(addresses.Addresses |> Seq.head)
        aspnetPort <- uri.Port
        aspnetApp <- app
    }

    [<GlobalCleanup>]
    member _.Cleanup() = task {
        do! fireStop()
        do! aspnetApp.StopAsync()
        client.Dispose()
    }

    [<Benchmark(Description = "Fire: large JSON")>]
    member _.FireJsonLarge() = task {
        let! response = client.GetStringAsync($"http://127.0.0.1:{firePort}/json-large")
        return response
    }

    [<Benchmark(Description = "ASP.NET Core: large JSON", Baseline = true)>]
    member _.AspNetJsonLarge() = task {
        let! response = client.GetStringAsync($"http://127.0.0.1:{aspnetPort}/json-large")
        return response
    }

// In-process variant: isolates the byte[] vs pooled-writer allocation from HTTP noise.
[<MemoryDiagnoser>]
type JsonLargeDispatchBenchmark() =
    let mutable trie = Trie.empty

    [<GlobalSetup>]
    member _.Setup() =
        let routes =
            Route.start
            |> Route.get "/json-large" (fun _ -> task { return Response.json largePayload })
        for entry in routes.Routes |> List.rev do
            trie <- Trie.add entry.Method entry.Pattern entry.Middlewares entry.Handler trie

    [<Benchmark(Description = "Raw serialize large (floor)", Baseline = true)>]
    member _.RawSerializeLarge() = task {
        let ctx = DefaultHttpContext()
        ctx.Response.Body <- System.IO.Stream.Null
        let bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(largePayload)
        ctx.Response.ContentType <- "application/json; charset=utf-8"
        ctx.Response.ContentLength <- Nullable(int64 bytes.Length)
        do! ctx.Response.Body.WriteAsync(ReadOnlyMemory(bytes))
    }

    [<Benchmark(Description = "Fire dispatch large")>]
    member _.FireDispatchLarge() = task {
        let ctx = DefaultHttpContext()
        ctx.Request.Path <- PathString("/json-large")
        ctx.Request.Method <- "GET"
        ctx.Response.Body <- System.IO.Stream.Null
        let baseHandler : Handler = fun _req -> Internal.dispatchRequest trie App.defaults ctx
        let req = Request(ctx, Internal.emptyParams)
        let! resp = baseHandler req
        do! Internal.writeResponse ctx resp
    }

// --- Competing F# frameworks: head-to-head HTTP comparison ---
// Each framework hosts the same two endpoints (/plaintext, /json) on Kestrel
// (port 0) with logging silenced, so the only difference measured is the
// framework's own request pipeline. JSON uses each framework's default serializer.

let private serverPort (app: WebApplication) =
    let addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
    Uri(addresses.Addresses |> Seq.head).Port

let private kestrelListen (port: int) (builder: WebApplicationBuilder) =
    builder.WebHost.ConfigureKestrel(fun o -> o.Listen(IPAddress.Loopback, port)) |> ignore

module private GiraffeServer =
    open Giraffe
    let private webApp : HttpHandler =
        choose [
            route "/plaintext" >=> text "Hello, World!"
            route "/json" >=> json {| message = "Hello, World!"; count = 42 |}
        ]
    let start (port: int) = task {
        let builder = WebApplication.CreateBuilder() |> configureAspNetBuilder
        builder.Services.AddGiraffe() |> ignore
        kestrelListen port builder
        let app = builder.Build()
        app.UseGiraffe(webApp) |> ignore
        do! app.StartAsync()
        return app
    }

module private SaturnServer =
    open Giraffe
    open Saturn
    // Saturn's router produces a Giraffe HttpHandler — host it on the same pipeline.
    let private webApp =
        router {
            get "/plaintext" (text "Hello, World!")
            get "/json" (json {| message = "Hello, World!"; count = 42 |})
        }
    let start (port: int) = task {
        let builder = WebApplication.CreateBuilder() |> configureAspNetBuilder
        builder.Services.AddGiraffe() |> ignore
        kestrelListen port builder
        let app = builder.Build()
        app.UseGiraffe(webApp) |> ignore
        do! app.StartAsync()
        return app
    }

module private FalcoServer =
    open Falco
    open Falco.Routing
    let private endpoints = [
        get "/plaintext" (Response.ofPlainText "Hello, World!")
        get "/json" (Response.ofJson {| message = "Hello, World!"; count = 42 |})
    ]
    let start (port: int) = task {
        let builder = WebApplication.CreateBuilder() |> configureAspNetBuilder
        builder.Services.AddRouting() |> ignore
        kestrelListen port builder
        let app = builder.Build()
        app.UseRouting().UseFalco(endpoints) |> ignore
        do! app.StartAsync()
        return app
    }

module private OxpeckerServer =
    open Oxpecker
    let private endpoints : Endpoint list = [
        route "/plaintext" (text "Hello, World!")
        route "/json" (json {| message = "Hello, World!"; count = 42 |})
    ]
    let start (port: int) = task {
        let builder = WebApplication.CreateBuilder() |> configureAspNetBuilder
        builder.Services.AddRouting().AddOxpecker() |> ignore
        kestrelListen port builder
        let app = builder.Build()
        app.UseRouting().UseOxpecker(endpoints) |> ignore
        do! app.StartAsync()
        return app
    }

[<MemoryDiagnoser>]
type FrameworkComparisonBenchmark() =
    let mutable client = Unchecked.defaultof<HttpClient>
    let mutable firePort = 0
    let mutable fireStop : (unit -> Task) = fun () -> Task.CompletedTask
    let mutable aspnet : WebApplication = null
    let mutable giraffe : WebApplication = null
    let mutable saturn : WebApplication = null
    let mutable falco : WebApplication = null
    let mutable oxpecker : WebApplication = null
    let mutable aspnetPort = 0
    let mutable giraffePort = 0
    let mutable saturnPort = 0
    let mutable falcoPort = 0
    let mutable oxpeckerPort = 0

    [<GlobalSetup>]
    member _.Setup() = task {
        client <- new HttpClient()

        // Fire
        let routes =
            Route.start
            |> Route.get "/plaintext" (fun _ -> task { return Response.text "Hello, World!" })
            |> Route.get "/json" (fun _ -> task { return Response.json {| message = "Hello, World!"; count = 42 |} })
        let config = App.defaults |> App.port 0 |> App.services [silenceFireLogging]
        let! (p, stop) = App.runTest routes config CancellationToken.None
        firePort <- p
        fireStop <- stop

        // ASP.NET Core minimal API
        let builder = WebApplication.CreateBuilder() |> configureAspNetBuilder
        kestrelListen 0 builder
        let app = builder.Build()
        app.MapGet("/plaintext", Func<string>(fun () -> "Hello, World!")) |> ignore
        app.MapGet("/json", Func<IResult>(fun () -> Results.Json({| message = "Hello, World!"; count = 42 |}))) |> ignore
        do! app.StartAsync()
        aspnet <- app
        aspnetPort <- serverPort app

        let! g = GiraffeServer.start 0
        giraffe <- g
        giraffePort <- serverPort g
        let! s = SaturnServer.start 0
        saturn <- s
        saturnPort <- serverPort s
        let! f = FalcoServer.start 0
        falco <- f
        falcoPort <- serverPort f
        let! o = OxpeckerServer.start 0
        oxpecker <- o
        oxpeckerPort <- serverPort o
    }

    [<GlobalCleanup>]
    member _.Cleanup() = task {
        do! fireStop ()
        do! aspnet.StopAsync()
        do! giraffe.StopAsync()
        do! saturn.StopAsync()
        do! falco.StopAsync()
        do! oxpecker.StopAsync()
        client.Dispose()
    }

    [<Benchmark(Description = "Fire: JSON", Baseline = true)>]
    member _.FireJson() = client.GetStringAsync($"http://127.0.0.1:{firePort}/json")

    [<Benchmark(Description = "ASP.NET Core: JSON")>]
    member _.AspNetJson() = client.GetStringAsync($"http://127.0.0.1:{aspnetPort}/json")

    [<Benchmark(Description = "Giraffe: JSON")>]
    member _.GiraffeJson() = client.GetStringAsync($"http://127.0.0.1:{giraffePort}/json")

    [<Benchmark(Description = "Saturn: JSON")>]
    member _.SaturnJson() = client.GetStringAsync($"http://127.0.0.1:{saturnPort}/json")

    [<Benchmark(Description = "Falco: JSON")>]
    member _.FalcoJson() = client.GetStringAsync($"http://127.0.0.1:{falcoPort}/json")

    [<Benchmark(Description = "Oxpecker: JSON")>]
    member _.OxpeckerJson() = client.GetStringAsync($"http://127.0.0.1:{oxpeckerPort}/json")

    [<Benchmark(Description = "Fire: plaintext")>]
    member _.FirePlain() = client.GetStringAsync($"http://127.0.0.1:{firePort}/plaintext")

    [<Benchmark(Description = "ASP.NET Core: plaintext")>]
    member _.AspNetPlain() = client.GetStringAsync($"http://127.0.0.1:{aspnetPort}/plaintext")

    [<Benchmark(Description = "Giraffe: plaintext")>]
    member _.GiraffePlain() = client.GetStringAsync($"http://127.0.0.1:{giraffePort}/plaintext")

    [<Benchmark(Description = "Saturn: plaintext")>]
    member _.SaturnPlain() = client.GetStringAsync($"http://127.0.0.1:{saturnPort}/plaintext")

    [<Benchmark(Description = "Falco: plaintext")>]
    member _.FalcoPlain() = client.GetStringAsync($"http://127.0.0.1:{falcoPort}/plaintext")

    [<Benchmark(Description = "Oxpecker: plaintext")>]
    member _.OxpeckerPlain() = client.GetStringAsync($"http://127.0.0.1:{oxpeckerPort}/plaintext")

// Serve mode for external throughput load tests: `serve <framework> <port>`
// starts one framework's server on a fixed port and blocks. Used by a load
// generator (oha) since BenchmarkDotNet's HTTP latency is transport-bound.
let private blockForever () = System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite)

let private serve (framework: string) (port: int) =
    match framework with
    | "fire" ->
        let routes =
            Route.start
            |> Route.get "/plaintext" (fun _ -> task { return Response.text "Hello, World!" })
            |> Route.get "/json" (fun _ -> task { return Response.json {| message = "Hello, World!"; count = 42 |} })
        let config = App.defaults |> App.host "127.0.0.1" |> App.port port |> App.services [silenceFireLogging]
        (App.run routes config CancellationToken.None).GetAwaiter().GetResult()
    | "aspnet" ->
        let builder = WebApplication.CreateBuilder() |> configureAspNetBuilder
        kestrelListen port builder
        let app = builder.Build()
        app.MapGet("/plaintext", Func<string>(fun () -> "Hello, World!")) |> ignore
        app.MapGet("/json", Func<IResult>(fun () -> Results.Json({| message = "Hello, World!"; count = 42 |}))) |> ignore
        app.Run()
    | "giraffe"  -> (GiraffeServer.start port).GetAwaiter().GetResult() |> ignore; blockForever ()
    | "saturn"   -> (SaturnServer.start port).GetAwaiter().GetResult() |> ignore; blockForever ()
    | "falco"    -> (FalcoServer.start port).GetAwaiter().GetResult() |> ignore; blockForever ()
    | "oxpecker" -> (OxpeckerServer.start port).GetAwaiter().GetResult() |> ignore; blockForever ()
    | other -> eprintfn "unknown framework: %s" other

[<EntryPoint>]
let main args =
    match args with
    | [| "serve"; framework; port |] ->
        serve framework (int port)
        0
    | _ ->
        let config = BenchmarkConfig()
        let switcher = BenchmarkSwitcher.FromAssembly(typeof<PlainTextBenchmark>.Assembly)
        // Bare `dotnet run` runs the full joined suite; passing args (e.g. --filter)
        // runs just the matching benchmarks (RunAllJoined ignores --filter).
        if Array.isEmpty args then
            switcher.RunAllJoined(config) |> ignore
        else
            switcher.Run(args, config) |> ignore
        0
