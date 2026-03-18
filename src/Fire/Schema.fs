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

    /// Wraps a handler with schema validation. Validates body via PipeReader.
    let validated (schema: Flame.Schema<'T>) (handler: 'T -> System.Threading.Tasks.Task<Response>) : Handler =
        fun req -> task {
            try
                let! buffer = readPipeToBuffer req.Raw.Request.BodyReader
                let result = schema.ParseBuffer buffer
                match result with
                | Ok value -> return! handler value
                | Error errors -> return Response.json {| errors = errors |} |> Response.status 400
            with ex ->
                return Response.json {| errors = [$"invalid JSON: {ex.Message}"] |} |> Response.status 400
        }

    /// Parse and validate the request body using a schema. Returns Result.
    let parseRequest (schema: Flame.Schema<'T>) (req: Request) : System.Threading.Tasks.Task<Result<'T, string list>> = task {
        try
            let! buffer = readPipeToBuffer req.Raw.Request.BodyReader
            return schema.ParseBuffer buffer
        with ex ->
            return Error [$"invalid JSON: {ex.Message}"]
    }
