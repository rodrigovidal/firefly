module Fire.Tests.AppTests

open System.Net.Http
open System.Threading
open Microsoft.Extensions.DependencyInjection
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``App serves a simple GET route`` () = task {
    let routes =
        Route.start
        |> Route.get "/hello" (fun _ -> task { return Response.text "world" })

    let config = App.defaults |> App.port 0
    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/hello")
    let! body = resp.Content.ReadAsStringAsync()

    resp.StatusCode |> should equal System.Net.HttpStatusCode.OK
    body |> should equal "world"

    do! stop ()
}

[<Fact>]
let ``App returns 404 for unmatched route`` () = task {
    let routes =
        Route.start
        |> Route.get "/hello" (fun _ -> task { return Response.text "world" })

    let config = App.defaults |> App.port 0
    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/nope")

    resp.StatusCode |> should equal System.Net.HttpStatusCode.NotFound

    do! stop ()
}

[<Fact>]
let ``App serves JSON response`` () = task {
    let routes =
        Route.start
        |> Route.get "/data" (fun _ -> task {
            return Response.json {| name = "fire"; version = 1 |}
        })

    let config = App.defaults |> App.port 0
    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/data")
    let! body = resp.Content.ReadAsStringAsync()

    resp.StatusCode |> should equal System.Net.HttpStatusCode.OK
    let ct = resp.Content.Headers.ContentType.MediaType
    ct |> should equal "application/json"
    body |> should haveSubstring "\"name\""
    body |> should haveSubstring "fire"

    do! stop ()
}

[<Fact>]
let ``App captures route params`` () = task {
    let routes =
        Route.start
        |> Route.get "/users/:id" (fun (req: Request) -> task {
            let id = req.Params.["id"]
            return Response.text id
        })

    let config = App.defaults |> App.port 0
    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/users/42")
    let! body = resp.Content.ReadAsStringAsync()

    resp.StatusCode |> should equal System.Net.HttpStatusCode.OK
    body |> should equal "42"

    do! stop ()
}

[<Fact>]
let ``App applies middleware`` () = task {
    let addHeader : Middleware = fun next req -> task {
        let! response = next req
        return response |> Response.header "X-Middleware" "applied"
    }

    let routes =
        Route.start
        |> Route.middleware addHeader
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })

    let config = App.defaults |> App.port 0
    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/test")
    let! body = resp.Content.ReadAsStringAsync()

    resp.StatusCode |> should equal System.Net.HttpStatusCode.OK
    body |> should equal "ok"
    let hasHeader = resp.Headers.Contains("X-Middleware")
    hasHeader |> should equal true
    let headerValue = resp.Headers.GetValues("X-Middleware") |> Seq.head
    headerValue |> should equal "applied"

    do! stop ()
}

[<Fact>]
let ``App calls custom error handler`` () = task {
    let routes =
        Route.start
        |> Route.get "/boom" (fun _ -> task {
            return failwith "kaboom"
        })

    let config =
        App.defaults
        |> App.port 0
        |> App.onError (fun ex _req -> task {
            return Response.text ex.Message |> Response.status 500
        })

    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/boom")
    let! body = resp.Content.ReadAsStringAsync()

    resp.StatusCode |> should equal System.Net.HttpStatusCode.InternalServerError
    body |> should equal "kaboom"

    do! stop ()
}

[<Fact>]
let ``App calls custom not-found handler`` () = task {
    let routes =
        Route.start
        |> Route.get "/exists" (fun _ -> task { return Response.text "yes" })

    let config =
        App.defaults
        |> App.port 0
        |> App.notFound (fun _req -> task {
            return Response.text "custom 404" |> Response.status 404
        })

    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/missing")
    let! body = resp.Content.ReadAsStringAsync()

    resp.StatusCode |> should equal System.Net.HttpStatusCode.NotFound
    body |> should equal "custom 404"

    do! stop ()
}

// --- Dependency Injection ---

type IGreeter =
    abstract Greet: string -> string

type Greeter() =
    interface IGreeter with
        member _.Greet name = $"Hello, {name}!"

// --- Coverage: writeResponse Stream body (line 68-70 in App.fs) ---

[<Fact>]
let ``App serves Stream response`` () = task {
    let routes =
        Route.start
        |> Route.get "/stream" (fun _ -> task {
            let bytes = System.Text.Encoding.UTF8.GetBytes("streamed")
            let ms = new System.IO.MemoryStream(bytes) :> System.IO.Stream
            return Response.stream ms
        })

    let config = App.defaults |> App.port 0
    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/stream")
    let! body = resp.Content.ReadAsStringAsync()
    body |> should equal "streamed"

    do! stop ()
}

// --- Coverage: App error handler that throws (lines 104-105 in App.fs) ---

[<Fact>]
let ``App error handler that throws returns 500`` () = task {
    let routes =
        Route.start
        |> Route.get "/boom" (fun _ -> task {
            return failwith "kaboom"
        })

    let config =
        App.defaults
        |> App.port 0
        |> App.onError (fun _ex _req -> task {
            return failwith "error handler also fails"
        })

    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/boom")
    resp.StatusCode |> should equal System.Net.HttpStatusCode.InternalServerError

    do! stop ()
}

// --- Coverage: App no error handler returns 500 (line 107 in App.fs) ---

[<Fact>]
let ``App unhandled exception without error handler returns 500`` () = task {
    let routes =
        Route.start
        |> Route.get "/boom" (fun _ -> task {
            return failwith "kaboom"
        })

    let config = App.defaults |> App.port 0
    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/boom")
    resp.StatusCode |> should equal System.Net.HttpStatusCode.InternalServerError

    do! stop ()
}

// --- Coverage: App.host with IP address (line 112 in App.fs) ---

[<Fact>]
let ``App.host with 127.0.0.1 starts correctly`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })

    let config = App.defaults |> App.port 0 |> App.host "127.0.0.1"
    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/test")
    let! body = resp.Content.ReadAsStringAsync()
    body |> should equal "ok"

    do! stop ()
}

// --- Coverage: App.shutdownTimeout (lines 116-119 in App.fs) ---

[<Fact>]
let ``App.shutdownTimeout configures shutdown`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })

    let config =
        App.defaults
        |> App.port 0
        |> App.shutdownTimeout (System.TimeSpan.FromSeconds 5.0)

    use cts = new CancellationTokenSource()
    let! (port, stop) = App.runTest routes config cts.Token

    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/test")
    let! body = resp.Content.ReadAsStringAsync()
    body |> should equal "ok"

    do! stop ()
}

[<Fact>]
let ``App.dependencyInjection registers and resolves services`` () = task {
    let routes =
        Route.start
        |> Route.get "/greet/:name" (fun (req: Request) -> task {
            let greeter = req.Raw.RequestServices.GetRequiredService<IGreeter>()
            return Response.text (greeter.Greet(req.Params.["name"]))
        })

    let config =
        App.defaults
        |> App.port 0
        |> App.dependencyInjection (fun services ->
            services.AddSingleton<IGreeter, Greeter>() |> ignore
        )

    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let! resp = client.GetAsync($"http://127.0.0.1:{port}/greet/Fire")
    let! body = resp.Content.ReadAsStringAsync()

    resp.StatusCode |> should equal System.Net.HttpStatusCode.OK
    body |> should equal "Hello, Fire!"

    do! stop ()
}

// --- Coverage: App.run starts and stops server (lines 127-135) ---

[<Fact>]
let ``App.run starts and stops server`` () = task {
    let routes = Route.start |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.port 0 |> App.host "127.0.0.1"
    use cts = new CancellationTokenSource()
    let serverTask = App.run routes config cts.Token
    do! System.Threading.Tasks.Task.Delay(500)
    cts.Cancel()
    try do! serverTask with :? System.OperationCanceledException -> ()
}

// --- Coverage: resolveHost else branch with IP "0.0.0.0" (line 111-112) ---

[<Fact>]
let ``App.run with host 0.0.0.0 starts correctly`` () = task {
    let routes = Route.start |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.port 0 |> App.host "0.0.0.0"
    use cts = new CancellationTokenSource()
    let serverTask = App.run routes config cts.Token
    do! System.Threading.Tasks.Task.Delay(500)
    cts.Cancel()
    try do! serverTask with :? System.OperationCanceledException -> ()
}
