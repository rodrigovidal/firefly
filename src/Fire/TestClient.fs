namespace Fire

open System
open System.Collections.Generic
open System.IO
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type TestResponse = {
    Status: int
    Headers: (string * string) list
    Body: string
}

type TestClientMode =
    | Direct of trie: TrieNode * config: FireConfig
    | Integration of port: int * stop: (unit -> Task) * client: HttpClient

type TestClient = {
    Mode: TestClientMode
    DefaultHeaders: (string * string) list
}

[<RequireQualifiedAccess>]
module TestClient =

    let private buildTrie (routes: RouteTable) : TrieNode =
        let mutable trie = Trie.empty
        for entry in routes.Routes do
            trie <- Trie.add entry.Method entry.Pattern entry.Middlewares entry.Handler trie
        trie

    let create (routes: RouteTable) : TestClient =
        let trie = buildTrie routes
        { Mode = Direct (trie, App.defaults); DefaultHeaders = [] }

    let createWith (routes: RouteTable) (config: FireConfig) : TestClient =
        let trie = buildTrie routes
        { Mode = Direct (trie, config); DefaultHeaders = [] }

    let start (routes: RouteTable) (config: FireConfig) : Task<TestClient> = task {
        let cts = new CancellationTokenSource()
        let! (port, stopFn) = App.runTest routes config cts.Token
        let client = new HttpClient()
        client.BaseAddress <- Uri($"http://127.0.0.1:{port}")
        let stopAll () : Task =
            task {
                cts.Cancel()
                try do! stopFn() with _ -> ()
                client.Dispose()
                cts.Dispose()
            } :> Task
        return { Mode = Integration (port, stopAll, client); DefaultHeaders = [] }
    }

    let withHeader (key: string) (value: string) (client: TestClient) : TestClient =
        { client with DefaultHeaders = (key, value) :: client.DefaultHeaders }

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
            do! ctx.Response.Body.WriteAsync(ReadOnlyMemory(bytes))
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

    let private executeDirect (trie: TrieNode) (config: FireConfig) (method': string) (path: string) (headers: (string * string) list) (bodyOpt: string option) : Task<TestResponse> = task {
        let ctx = DefaultHttpContext()
        ctx.Request.Method <- method'

        // Parse path and query string
        let pathIdx = path.IndexOf('?')
        if pathIdx >= 0 then
            ctx.Request.Path <- PathString(path.Substring(0, pathIdx))
            ctx.Request.QueryString <- QueryString("?" + path.Substring(pathIdx + 1))
        else
            ctx.Request.Path <- PathString(path)

        for (key, value) in headers do
            ctx.Request.Headers.Append(key, value)

        match bodyOpt with
        | Some body ->
            let bytes = Encoding.UTF8.GetBytes(body)
            ctx.Request.Body <- new MemoryStream(bytes)
        | None -> ()

        let captureStream = new MemoryStream()
        ctx.Response.Body <- captureStream

        let baseHandler : Handler = fun _req -> dispatchRequest trie config ctx

        let composed =
            List.foldBack (fun (mw: Middleware) (h: Handler) -> mw h) config.Middlewares baseHandler

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

        captureStream.Position <- 0L
        use reader = new StreamReader(captureStream, Encoding.UTF8)
        let! bodyStr = reader.ReadToEndAsync()

        let responseHeaders =
            ctx.Response.Headers
            |> Seq.collect (fun kvp ->
                kvp.Value |> Seq.map (fun v -> (kvp.Key, v)))
            |> Seq.toList

        return {
            Status = ctx.Response.StatusCode
            Headers = responseHeaders
            Body = bodyStr
        }
    }

    let private executeIntegration (client: HttpClient) (method': string) (path: string) (headers: (string * string) list) (bodyOpt: string option) : Task<TestResponse> = task {
        let msg = new HttpRequestMessage(HttpMethod(method'), path)
        for (key, value) in headers do
            msg.Headers.TryAddWithoutValidation(key, value) |> ignore
        match bodyOpt with
        | Some body ->
            msg.Content <- new StringContent(body, Encoding.UTF8, "text/plain")
        | None -> ()

        let! resp = client.SendAsync(msg)
        let! bodyStr = resp.Content.ReadAsStringAsync()

        let responseHeaders =
            resp.Headers
            |> Seq.collect (fun kvp ->
                kvp.Value |> Seq.map (fun v -> (kvp.Key, v)))
            |> Seq.toList

        let contentHeaders =
            resp.Content.Headers
            |> Seq.collect (fun kvp ->
                kvp.Value |> Seq.map (fun v -> (kvp.Key, v)))
            |> Seq.toList

        return {
            Status = int resp.StatusCode
            Headers = responseHeaders @ contentHeaders
            Body = bodyStr
        }
    }

    let private send (method': string) (path: string) (bodyOpt: string option) (client: TestClient) : Task<TestResponse> =
        match client.Mode with
        | Direct (trie, config) ->
            executeDirect trie config method' path client.DefaultHeaders bodyOpt
        | Integration (_, _, httpClient) ->
            executeIntegration httpClient method' path client.DefaultHeaders bodyOpt

    let get (path: string) (client: TestClient) : Task<TestResponse> =
        send "GET" path None client

    let post (path: string) (body: string) (client: TestClient) : Task<TestResponse> =
        send "POST" path (Some body) client

    let put (path: string) (body: string) (client: TestClient) : Task<TestResponse> =
        send "PUT" path (Some body) client

    let delete (path: string) (client: TestClient) : Task<TestResponse> =
        send "DELETE" path None client

    let stop (client: TestClient) : Task =
        match client.Mode with
        | Integration (_, stopFn, _) -> stopFn()
        | Direct _ -> Task.CompletedTask
