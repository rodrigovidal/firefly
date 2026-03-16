open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Fire

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
        let builder = WebApplication.CreateBuilder()
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
        let builder = WebApplication.CreateBuilder()
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
            |> Route.get "/users/:id" (fun req -> task {
                return Response.json {| id = req.Params.["id"] |}
            })
        let config = App.defaults |> App.port 0
        let! (p, stop) = App.runTest routes config CancellationToken.None
        firePort <- p
        fireStop <- stop

        // ASP.NET Core minimal API server
        let builder = WebApplication.CreateBuilder()
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
        let builder = WebApplication.CreateBuilder()
        builder.WebHost.ConfigureKestrel(fun opts ->
            opts.Listen(IPAddress.Loopback, 0)
        ) |> ignore
        let app = builder.Build()
        app.Use(Func<HttpContext, RequestDelegate, Task>(fun ctx next -> task {
            do! next.Invoke(ctx)
            ctx.Response.Headers.["X-Request-Id"] <- "bench-123"
        })) |> ignore
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
        let! response = client.GetStringAsync($"http://127.0.0.1:{firePort}/api/data")
        return response
    }

    [<Benchmark(Description = "ASP.NET Core: middleware + JSON", Baseline = true)>]
    member _.AspNetMiddleware() = task {
        let! response = client.GetStringAsync($"http://127.0.0.1:{aspnetPort}/api/data")
        return response
    }

[<EntryPoint>]
let main args =
    BenchmarkRunner.Run<PlainTextBenchmark>() |> ignore
    BenchmarkRunner.Run<JsonBenchmark>() |> ignore
    BenchmarkRunner.Run<RouteParamBenchmark>() |> ignore
    BenchmarkRunner.Run<MiddlewareBenchmark>() |> ignore
    0
