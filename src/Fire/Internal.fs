namespace Fire

open System
open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

[<RequireQualifiedAccess>]
module Internal =

    let writeResponse (ctx: HttpContext) (response: Response) = task {
        ctx.Response.StatusCode <- response.Status
        for (key, value) in response.Headers do
            ctx.Response.Headers.Append(key, value)
        let hasContentType = ctx.Response.Headers.ContainsKey("Content-Type")
        match response.Body with
        | Empty -> ()
        | Text s ->
            let bytes = System.Text.Encoding.UTF8.GetBytes(s)
            if not hasContentType then
                ctx.Response.ContentType <- "text/plain; charset=utf-8"
            ctx.Response.ContentLength <- Nullable(int64 bytes.Length)
            do! ctx.Response.Body.WriteAsync(ReadOnlyMemory(bytes))
        | Json bytes ->
            if not hasContentType then
                ctx.Response.ContentType <- "application/json; charset=utf-8"
            ctx.Response.ContentLength <- Nullable(int64 bytes.Length)
            do! ctx.Response.Body.WriteAsync(ReadOnlyMemory(bytes))
        | Stream stream ->
            use stream = stream
            do! stream.CopyToAsync(ctx.Response.Body)
        | StreamCallback callback ->
            do! callback ctx
    }

    let dispatchRequest (trie: TrieNode) (config: FireConfig) (ctx: HttpContext) : Task<Response> = task {
        let path = ctx.Request.Path.Value
        let method' = ctx.Request.Method
        match Trie.lookup method' path trie with
        | Some (handler, ps) ->
            let req = Request(ctx, ps)
            ctx.Items.["fire.current.request"] <- box req
            return! handler req
        | None ->
            match config.NotFound with
            | Some nfHandler ->
                let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
                return! nfHandler req
            | None ->
                return { Status = 404; Headers = []; Body = Empty }
    }
