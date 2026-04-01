open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Jobs
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
        this.WithOption(ConfigOptions.JoinSummary, true) |> ignore
        this.WithOption(ConfigOptions.DisableParallelBuild, true) |> ignore

let configureAspNetBuilder (builder: WebApplicationBuilder) =
    builder.Logging.ClearProviders() |> ignore
    builder.Logging.SetMinimumLevel(LogLevel.None) |> ignore
    builder

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
        let config = App.defaults |> App.port 0
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
        let config = App.defaults |> App.port 0
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
        let config = App.defaults |> App.port 0
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
        let config = App.defaults |> App.port 0
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

[<EntryPoint>]
let main args =
    let config = BenchmarkConfig()
    BenchmarkSwitcher
        .FromAssembly(typeof<PlainTextBenchmark>.Assembly)
        .RunAllJoined(config, args)
    |> ignore
    0
