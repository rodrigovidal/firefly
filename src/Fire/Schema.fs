namespace Fire

open System.Buffers
open System.IO
open System.IO.Pipelines
open Flame

[<AutoOpen>]
module FireSchemaExtensions =
    /// Re-export the Flame schema CE so Fire users can use it without opening Flame
    let schema = Flame.SchemaExtensions.schema

[<RequireQualifiedAccess>]
module Schema =
    // Re-export all Flame.Schema functions so Fire users don't need to open Flame
    let required name parser rules = Flame.Schema.required name parser rules
    let optional name parser defaultValue rules = Flame.Schema.optional name parser defaultValue rules
    let string = Flame.Schema.string
    let int = Flame.Schema.int
    let bool = Flame.Schema.bool
    let float = Flame.Schema.float
    let list parser = Flame.Schema.list parser
    let nullable parser = Flame.Schema.nullable parser
    let nest schema = Flame.Schema.nest schema
    let minLength len = Flame.Schema.minLength len
    let maxLength len = Flame.Schema.maxLength len
    let pattern regex = Flame.Schema.pattern regex
    let min n = Flame.Schema.min n
    let max n = Flame.Schema.max n
    let email = Flame.Schema.email
    let url = Flame.Schema.url
    let enum' values = Flame.Schema.enum' values
    let trim = Flame.Schema.trim
    let lowercase = Flame.Schema.lowercase
    let uppercase = Flame.Schema.uppercase
    let parseJson schema el = Flame.Schema.parseJson schema el
    let parseString schema str = Flame.Schema.parseString schema str
    let parseBuffer schema buffer = Flame.Schema.parseBuffer schema buffer
    let parsePipe schema reader = Flame.Schema.parsePipe schema reader
    let parseStream schema stream = Flame.Schema.parseStream schema stream
    let toJsonSchema schema = Flame.Schema.toJsonSchema schema
    let fromType<'T> () = Flame.Schema.fromType<'T> ()

    let private readPipeToBuffer (pipeReader: PipeReader) : System.Threading.Tasks.Task<System.Buffers.ReadOnlySequence<byte>> = task {
        use stream = new MemoryStream()
        let mutable isDone = false
        while not isDone do
            let! readResult = pipeReader.ReadAsync()
            let chunk = readResult.Buffer.ToArray()
            stream.Write(chunk, 0, chunk.Length)
            pipeReader.AdvanceTo(readResult.Buffer.End)
            isDone <- readResult.IsCompleted
        return System.Buffers.ReadOnlySequence<byte>(stream.ToArray())
    }

    /// Wraps a handler with schema validation. Validates body via PipeReader (zero-alloc path).
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
