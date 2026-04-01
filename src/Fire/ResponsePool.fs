namespace Fire

open System.Buffers
open System.Text.Json

[<RequireQualifiedAccess>]
module ResponsePool =

    let private jsonPool = ArrayPool<byte>.Shared

    /// Serialize to JSON using a pooled buffer. Returns a Response with Json body.
    /// The buffer is copied to a right-sized array for the response (pool buffers are oversized).
    let json (value: 'T) : Response =
        let buffer = jsonPool.Rent(4096)
        try
            use stream = new System.IO.MemoryStream(buffer, 0, buffer.Length, true, true)
            use writer = new Utf8JsonWriter(stream)
            JsonSerializer.Serialize(writer, value)
            writer.Flush()
            let length = int stream.Position
            let result = Array.zeroCreate<byte> length
            System.Buffer.BlockCopy(buffer, 0, result, 0, length)
            { Status = 200; Headers = [("Content-Type", "application/json; charset=utf-8")]; Body = Json result }
        finally
            jsonPool.Return(buffer)

    /// Pre-built common responses (allocated once, reused).
    let ok = { Status = 200; Headers = []; Body = Empty }
    let notFound = { Status = 404; Headers = []; Body = Empty }
    let noContent = { Status = 204; Headers = []; Body = Empty }
    let unauthorized = { Status = 401; Headers = []; Body = Empty }
    let forbidden = { Status = 403; Headers = []; Body = Empty }
    let badRequest = { Status = 400; Headers = []; Body = Empty }
    let serverError = { Status = 500; Headers = []; Body = Empty }
