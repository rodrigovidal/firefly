namespace Fire

open System
open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

[<RequireQualifiedAccess>]
module Internal =

    /// Shared read-only empty route-params dictionary. Used for the throwaway
    /// pre-routing Request and the not-found path so they don't each allocate
    /// a fresh Dictionary. Safe: exposed only as IReadOnlyDictionary, never mutated.
    let emptyParams : IReadOnlyDictionary<string, string> =
        Dictionary<string, string>() :> IReadOnlyDictionary<_, _>

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

    // Not a task{} computation expression: the only async work is the handler's
    // own Task, which we return directly. Avoids an extra per-request state machine.
    let dispatchRequest (trie: TrieNode) (config: FireConfig) (ctx: HttpContext) : Task<Response> =
        let path = ctx.Request.Path.Value
        let method' = ctx.Request.Method
        match Trie.lookup method' path trie with
        | ValueSome (handler, ps) ->
            let req = Request(ctx, ps)
            // Only stashed for the OnError handler to recover the current request.
            // Writing it lazily allocates ctx.Items, so skip when no handler reads it.
            if config.OnError.IsSome then
                ctx.Items.["fire.current.request"] <- box req
            handler req
        | ValueNone ->
            match config.NotFound with
            | Some nfHandler ->
                let req = Request(ctx, emptyParams)
                nfHandler req
            | None ->
                Task.FromResult { Status = 404; Headers = []; Body = Empty }
