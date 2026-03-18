namespace Fire

open System.Buffers
open System.IO
open System.IO.Pipelines
open Flame

/// Fire-specific schema integration (web layer).
/// For schema definitions, use `open Flame` directly.
[<RequireQualifiedAccess>]
module Schema =

    let private readPipeToBuffer (pipeReader: PipeReader) : System.Threading.Tasks.Task<ReadOnlySequence<byte>> = task {
        use stream = new MemoryStream()
        let mutable isDone = false
        while not isDone do
            let! readResult = pipeReader.ReadAsync()
            let chunk = readResult.Buffer.ToArray()
            stream.Write(chunk, 0, chunk.Length)
            pipeReader.AdvanceTo(readResult.Buffer.End)
            isDone <- readResult.IsCompleted
        return ReadOnlySequence<byte>(stream.ToArray())
    }

    /// Parse JSON body via PipeReader (zero-alloc buffer path).
    let parseRequest (schema: Flame.Schema<'T>) (req: Request) : System.Threading.Tasks.Task<Result<'T, string list>> = task {
        try
            let! buffer = readPipeToBuffer req.Raw.Request.BodyReader
            return schema.ParseBuffer buffer
        with ex ->
            return Error [$"invalid JSON: {ex.Message}"]
    }

    /// Parse form body directly from IFormCollection. Zero dictionary allocation.
    let parseFormRequest (schema: Flame.Schema<'T>) (req: Request) : System.Threading.Tasks.Task<Result<'T, string list>> =
        let httpRequest = req.Raw.Request
        task {
            try
                let! form = httpRequest.ReadFormAsync()
                return Flame.Schema.parseLookup schema (fun name ->
                    match form.TryGetValue(name) with
                    | true, v -> Some (v.ToString())
                    | false, _ -> None)
            with ex ->
                return Error [$"invalid form data: {ex.Message}"]
        }

    /// Auto-detect content type and parse accordingly.
    /// JSON → zero-alloc buffer path. Form → form path.
    let parse (schema: Flame.Schema<'T>) (req: Request) : System.Threading.Tasks.Task<Result<'T, string list>> =
        let ct = req.Raw.Request.ContentType
        if ct <> null && (ct.Contains("application/x-www-form-urlencoded") || ct.Contains("multipart/form-data")) then
            parseFormRequest schema req
        else
            parseRequest schema req

    /// Parse route params into a typed record. Zero allocation — reads directly from params.
    let parseParams (schema: Flame.Schema<'T>) (req: Request) : Result<'T, string list> =
        let params' = req.Params
        Flame.Schema.parseLookup schema (fun name ->
            // Case-insensitive lookup in route params
            params' |> Seq.tryFind (fun kvp ->
                System.String.Equals(kvp.Key, name, System.StringComparison.OrdinalIgnoreCase))
            |> Option.map (fun kvp -> kvp.Value))

    /// Parse query string into a typed record. Zero allocation — reads directly from IQueryCollection.
    let parseQuery (schema: Flame.Schema<'T>) (req: Request) : Result<'T, string list> =
        let query = req.Raw.Request.Query
        Flame.Schema.parseLookup schema (fun name ->
            match query.TryGetValue(name) with
            | true, v -> Some (v.ToString())
            | false, _ -> None)

    /// Wraps a handler with schema validation. Auto-detects content type.
    let validated (schema: Flame.Schema<'T>) (handler: 'T -> System.Threading.Tasks.Task<Response>) : Handler =
        fun req -> task {
            try
                match! parse schema req with
                | Ok value -> return! handler value
                | Error errors -> return Response.json {| errors = errors |} |> Response.status 400
            with ex ->
                return Response.json {| errors = [$"invalid request: {ex.Message}"] |} |> Response.status 400
        }
