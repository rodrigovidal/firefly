module Fire.Tests.InjectTests

open System.Threading
open Microsoft.Extensions.DependencyInjection
open Xunit
open FsUnit.Xunit
open Fire

// --- Test services ---

type IGreeter =
    abstract Greet: string -> string

type Greeter() =
    interface IGreeter with
        member _.Greet name = $"Hello, {name}!"

type ICounter =
    abstract Next: unit -> int

type Counter() =
    let mutable n = 0
    interface ICounter with
        member _.Next() = n <- n + 1; n

// --- Tests ---

[<Fact>]
let ``Inject.services resolves one service`` () = task {
    let routes =
        Route.start
        |> Route.get "/greet" (Inject.services (fun (greeter: IGreeter) -> task {
            return Response.text (greeter.Greet("Fire"))
        }))
    let config =
        App.defaults |> App.port 0
        |> App.dependencyInjection (fun s -> s.AddSingleton<IGreeter, Greeter>() |> ignore)
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new System.Net.Http.HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/greet")
    let! body = resp.Content.ReadAsStringAsync()
    body |> should equal "Hello, Fire!"
    do! stop()
}

[<Fact>]
let ``Inject.handle resolves service + Request`` () = task {
    let routes =
        Route.start
        |> Route.get "/greet/:name" (Inject.handle (fun (greeter: IGreeter) (req: Request) -> task {
            let name = req.Params.["name"]
            return Response.text (greeter.Greet(name))
        }))
    let config =
        App.defaults |> App.port 0
        |> App.dependencyInjection (fun s -> s.AddSingleton<IGreeter, Greeter>() |> ignore)
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new System.Net.Http.HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/greet/World")
    let! body = resp.Content.ReadAsStringAsync()
    body |> should equal "Hello, World!"
    do! stop()
}

[<Fact>]
let ``Inject.services resolves two services`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (Inject.services (fun (greeter: IGreeter) (counter: ICounter) -> task {
            let n = counter.Next()
            let greeting = greeter.Greet("Fire")
            return Response.text $"{greeting} #{n}"
        }))
    let config =
        App.defaults |> App.port 0
        |> App.dependencyInjection (fun s ->
            s.AddSingleton<IGreeter, Greeter>() |> ignore
            s.AddSingleton<ICounter, Counter>() |> ignore)
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new System.Net.Http.HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/test")
    let! body = resp.Content.ReadAsStringAsync()
    body |> should equal "Hello, Fire! #1"
    do! stop()
}

[<Fact>]
let ``Inject.handle resolves two services + Request`` () = task {
    let routes =
        Route.start
        |> Route.get "/test/:name" (Inject.handle (fun (greeter: IGreeter) (counter: ICounter) (req: Request) -> task {
            let n = counter.Next()
            let name = req.Params.["name"]
            let greeting = greeter.Greet(name)
            return Response.text $"{greeting} #{n}"
        }))
    let config =
        App.defaults |> App.port 0
        |> App.dependencyInjection (fun s ->
            s.AddSingleton<IGreeter, Greeter>() |> ignore
            s.AddSingleton<ICounter, Counter>() |> ignore)
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new System.Net.Http.HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/test/World")
    let! body = resp.Content.ReadAsStringAsync()
    body |> should equal "Hello, World! #1"
    do! stop()
}

[<Fact>]
let ``Inject.handle with Request only (no services)`` () = task {
    let routes =
        Route.start
        |> Route.get "/echo/:msg" (fun req -> task {
            return Response.text req.Params.["msg"]
        })
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new System.Net.Http.HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/echo/hello")
    let! body = resp.Content.ReadAsStringAsync()
    body |> should equal "hello"
    do! stop()
}
