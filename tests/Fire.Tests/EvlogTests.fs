module Fire.Tests.EvlogTests

open System.Threading
open Xunit
open FsUnit.Xunit
open Fire
open Evlog

[<Fact>]
let ``App.configure hook is applied`` () = task {
    let mutable hookCalled = false
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config =
        App.defaults
        |> App.port 0
        |> App.configure (fun _app -> hookCalled <- true)
    use cts = new CancellationTokenSource()
    let! (_, stop) = App.runTest routes config cts.Token
    hookCalled |> should equal true
    do! stop()
}

[<Fact>]
let ``Evlog integration with App.configure and DI`` () = task {
    let mutable loggerAccessed = false
    let routes =
        Route.start
        |> Route.get "/test" (fun (req: Request) -> task {
            let log = req.Evlog
            log.Set("action", "test")
            loggerAccessed <- true
            return Response.text "ok"
        })
    let config =
        App.defaults
        |> App.port 0
        |> App.dependencyInjection (fun services ->
            services.AddEvlog(fun opts ->
                opts.Service <- "test-app"
                opts.Pretty <- false
            ) |> ignore
        )
        |> App.configure (fun app -> app.UseEvlog() |> ignore)
    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token
    use client = new System.Net.Http.HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/test")
    resp.StatusCode |> should equal System.Net.HttpStatusCode.OK
    loggerAccessed |> should equal true
    do! stop()
}

[<Fact>]
let ``Evlog captures request status`` () = task {
    let routes =
        Route.start
        |> Route.get "/ok" (fun (req: Request) -> task {
            req.Evlog.Set("page", "home")
            return Response.text "ok"
        })
        |> Route.get "/fail" (fun (req: Request) -> task {
            req.Evlog.Set("page", "error")
            return Response.notFound
        })
    let config =
        App.defaults
        |> App.port 0
        |> App.dependencyInjection (fun services ->
            services.AddEvlog(fun opts ->
                opts.Service <- "test-app"
                opts.Pretty <- false
            ) |> ignore
        )
        |> App.configure (fun app -> app.UseEvlog() |> ignore)
    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token
    use client = new System.Net.Http.HttpClient()
    let! r1 = client.GetAsync($"http://127.0.0.1:{port}/ok")
    r1.StatusCode |> should equal System.Net.HttpStatusCode.OK
    let! r2 = client.GetAsync($"http://127.0.0.1:{port}/fail")
    r2.StatusCode |> should equal System.Net.HttpStatusCode.NotFound
    do! stop()
}

[<Fact>]
let ``Evlog emits wide event with accumulated context via Drain`` () = task {
    let mutable drainedJson = System.ReadOnlyMemory<byte>.Empty
    let routes =
        Route.start
        |> Route.get "/checkout" (fun (req: Request) -> task {
            req.Evlog.Set("user", "usr_42")
            req.Evlog.Set("cart.items", 3)
            return Response.text "done"
        })
    let config =
        App.defaults
        |> App.port 0
        |> App.dependencyInjection (fun services ->
            services.AddEvlog(fun opts ->
                opts.Service <- "test-drain"
                opts.Pretty <- false
                opts.Drain <- fun ctx ->
                    drainedJson <- ctx.EventJson
                    System.Threading.Tasks.Task.CompletedTask
            ) |> ignore
        )
        |> App.configure (fun app -> app.UseEvlog() |> ignore)
    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token
    use client = new System.Net.Http.HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/checkout")
    resp.StatusCode |> should equal System.Net.HttpStatusCode.OK
    // Give Evlog a moment to drain (it may be async)
    do! System.Threading.Tasks.Task.Delay(100)
    let json = System.Text.Encoding.UTF8.GetString(drainedJson.Span)
    json |> should not' (equal "")
    json |> should haveSubstring "usr_42"
    json |> should haveSubstring "test-drain"
    do! stop()
}
