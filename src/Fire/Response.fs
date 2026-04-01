namespace Fire

open System.IO
open System.Text.Json

type ResponseBody =
    | Empty
    | Text of string
    | Json of byte[]
    | Stream of Stream
    | StreamCallback of (Microsoft.AspNetCore.Http.HttpContext -> System.Threading.Tasks.Task<unit>)

type Response = {
    Status: int
    Headers: (string * string) list
    Body: ResponseBody
}

[<RequireQualifiedAccess>]
module Response =
    let ok = { Status = 200; Headers = []; Body = ResponseBody.Empty }
    let text s = { ok with Body = ResponseBody.Text s }

    let json<'T> (value: 'T) =
        { ok with Body = ResponseBody.Json (JsonSerializer.SerializeToUtf8Bytes(value)) }

    let stream s = { ok with Body = ResponseBody.Stream s }
    let status code r = { r with Status = code }
    let header key value r = { r with Headers = (key, value) :: r.Headers }
    let html s = text s |> header "Content-Type" "text/html; charset=utf-8"

    let cookie name value r =
        r |> header "Set-Cookie" $"{name}={value}"

    let notFound = { ok with Status = 404 }
    let unauthorized = { ok with Status = 401 }
    let created = { ok with Status = 201 }
    let noContent = { ok with Status = 204 }

    let redirect url code r =
        { r with Status = code; Headers = ("Location", url) :: r.Headers }

    let etag tag r = r |> header "ETag" tag

    let cacheControl value r = r |> header "Cache-Control" value

    let private getContentType (path: string) =
        let ext = Path.GetExtension(path).ToLowerInvariant()
        match ext with
        | ".html" | ".htm" -> "text/html"
        | ".css" -> "text/css"
        | ".js" -> "application/javascript"
        | ".json" -> "application/json"
        | ".png" -> "image/png"
        | ".jpg" | ".jpeg" -> "image/jpeg"
        | ".gif" -> "image/gif"
        | ".svg" -> "image/svg+xml"
        | ".ico" -> "image/x-icon"
        | ".woff" -> "font/woff"
        | ".woff2" -> "font/woff2"
        | ".ttf" -> "font/ttf"
        | ".txt" -> "text/plain"
        | ".xml" -> "application/xml"
        | ".pdf" -> "application/pdf"
        | _ -> "application/octet-stream"

    let file (path: string) =
        let fullPath = Path.GetFullPath(path)
        { ok with Body = ResponseBody.Stream(File.OpenRead(fullPath)) }
        |> header "Content-Type" (getContentType fullPath)

    let download (filename: string) (r: Response) =
        r |> header "Content-Disposition" $"attachment; filename=\"{filename}\""

    let inline' (r: Response) =
        r |> header "Content-Disposition" "inline"

    let streamJson (items: seq<'T>) : Response =
        let callback (ctx: Microsoft.AspNetCore.Http.HttpContext) = task {
            ctx.Response.ContentType <- "application/x-ndjson"
            for item in items do
                let line = JsonSerializer.Serialize(item)
                let bytes = System.Text.Encoding.UTF8.GetBytes(line + "\n")
                do! ctx.Response.Body.WriteAsync(System.ReadOnlyMemory(bytes), ctx.RequestAborted)
                do! ctx.Response.Body.FlushAsync(ctx.RequestAborted)
        }
        { Status = 200; Headers = []; Body = StreamCallback callback }

    let streamJsonAsync (items: System.Collections.Generic.IAsyncEnumerable<'T>) : Response =
        let callback (ctx: Microsoft.AspNetCore.Http.HttpContext) = task {
            ctx.Response.ContentType <- "application/x-ndjson"
            let enumerator = items.GetAsyncEnumerator(ctx.RequestAborted)
            try
                let mutable hasNext = true
                while hasNext do
                    let! next = enumerator.MoveNextAsync()
                    hasNext <- next
                    if hasNext then
                        let line = JsonSerializer.Serialize(enumerator.Current)
                        let bytes = System.Text.Encoding.UTF8.GetBytes(line + "\n")
                        do! ctx.Response.Body.WriteAsync(System.ReadOnlyMemory(bytes), ctx.RequestAborted)
                        do! ctx.Response.Body.FlushAsync(ctx.RequestAborted)
            finally
                enumerator.DisposeAsync().AsTask().Wait()
        }
        { Status = 200; Headers = []; Body = StreamCallback callback }

    let streamCallback (callback: Microsoft.AspNetCore.Http.HttpContext -> System.Threading.Tasks.Task<unit>) : Response =
        { Status = 200; Headers = []; Body = StreamCallback callback }

    let ofResult (onOk: 'T -> Response) (onError: 'E -> Response) (result: Result<'T, 'E>) =
        match result with
        | Ok value -> onOk value
        | Error err -> onError err
