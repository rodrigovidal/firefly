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

    /// Per-thread reusable Utf8JsonWriter for the deferred-JSON write path, so
    /// serializing a response doesn't allocate a writer per request. Reset onto
    /// the target buffer on each rent. Mirrors the pooling spirit of Trie's
    /// ArrayPool usage. Only used by writeResponse (single-threaded per request).
    [<Sealed; AbstractClass>]
    type private JsonWriterPool =
        [<System.ThreadStatic; DefaultValue>]
        static val mutable private writer : System.Text.Json.Utf8JsonWriter

        static member Rent (bw: System.Buffers.IBufferWriter<byte>) : System.Text.Json.Utf8JsonWriter =
            match JsonWriterPool.writer with
            | null ->
                let w = new System.Text.Json.Utf8JsonWriter(bw)
                JsonWriterPool.writer <- w
                w
            | w ->
                w.Reset(bw)
                w

    /// Runs a deferred-JSON closure into a byte[] so body-reading middleware
    /// (compression, ETag, idempotency) can treat it as `Json bytes`. Uses the
    /// same JsonSerializer.Serialize(writer, value) overload as the write path,
    /// so the bytes are identical to what would be written to the wire.
    let materializeJson (response: Response) : Response =
        match response.Body with
        | JsonDeferred serialize ->
            let abw = System.Buffers.ArrayBufferWriter<byte>()
            use w = new System.Text.Json.Utf8JsonWriter(abw)
            serialize w
            w.Flush()
            { response with Body = Json (abw.WrittenSpan.ToArray()) }
        | _ -> response

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
        | JsonDeferred serialize ->
            // Serialize straight into the response PipeWriter via a pooled writer,
            // avoiding a payload-sized byte[]. Content-Length is left to Kestrel
            // (buffered + set for small responses, chunked for large) like ASP.NET.
            if not hasContentType then
                ctx.Response.ContentType <- "application/json; charset=utf-8"
            let bw = ctx.Response.BodyWriter
            let w = JsonWriterPool.Rent bw
            serialize w
            w.Flush()
            let! _ = bw.FlushAsync()
            ()
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
