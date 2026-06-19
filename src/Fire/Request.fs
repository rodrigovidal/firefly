namespace Firefly

open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type UploadedFile = {
    Name: string
    FileName: string
    ContentType: string
    Length: int64
    Stream: Stream
}

[<RequireQualifiedAccess>]
module UploadedFile =
    let saveTo (path: string) (file: UploadedFile) : Task = task {
        use output = File.Create(path)
        do! file.Stream.CopyToAsync(output)
    }

    let readAllBytes (file: UploadedFile) : Task<byte[]> = task {
        use ms = new MemoryStream(int file.Length)
        do! file.Stream.CopyToAsync(ms)
        return ms.ToArray()
    }

module internal RequestKeys =
    [<Literal>]
    let QueryCacheItemKey = "fire.query.cache"

    [<Literal>]
    let RequestIdItemKey = "fire.request-id"

    [<Literal>]
    let CorrelationIdItemKey = "fire.correlation-id"

[<Struct>]
type Request(ctx: HttpContext, routeParams: IReadOnlyDictionary<string, string>) =

    member _.Path = ctx.Request.Path.Value
    member _.Method = ctx.Request.Method
    member _.Params = routeParams

    member _.Query : IReadOnlyDictionary<string, string> =
        match ctx.Items.TryGetValue(RequestKeys.QueryCacheItemKey) with
        | true, cached -> cached :?> IReadOnlyDictionary<string, string>
        | false, _ ->
            let q = ctx.Request.Query
            let d = Dictionary<string, string>(q.Count)
            for kvp in q do
                d.[kvp.Key] <- kvp.Value.ToString()
            let result = d :> IReadOnlyDictionary<_, _>
            ctx.Items.[RequestKeys.QueryCacheItemKey] <- result
            result

    member _.Header (name: string) : string option =
        match ctx.Request.Headers.TryGetValue(name) with
        | true, values -> Some (values.ToString())
        | false, _ -> None

    member _.Headers (name: string) : string list =
        match ctx.Request.Headers.TryGetValue(name) with
        | true, values -> values.ToArray() |> Array.toList
        | false, _ -> []

    member _.Body : Stream = ctx.Request.Body

    member _.Json<'T>() : Task<'T> =
        let body = ctx.Request.Body
        task {
            let! result = JsonSerializer.DeserializeAsync<'T>(body)
            return result
        }

    member _.QueryParam (name: string) : string option =
        match ctx.Request.Query.TryGetValue(name) with
        | true, values -> Some (values.ToString())
        | false, _ -> None

    member _.Text() : Task<string> =
        let body = ctx.Request.Body
        task {
            use reader = new StreamReader(body, Encoding.UTF8, leaveOpen = true)
            return! reader.ReadToEndAsync()
        }

    member _.Form() : Task<IReadOnlyDictionary<string, string>> =
        let request = ctx.Request
        task {
            let! form = request.ReadFormAsync()
            let d = Dictionary<string, string>(form.Count)
            for kvp in form do
                d.[kvp.Key] <- kvp.Value.ToString()
            return d :> IReadOnlyDictionary<_, _>
        }

    member _.Accepts (mediaType: string) : bool =
        match ctx.Request.Headers.TryGetValue("Accept") with
        | true, values -> values.ToString().Contains(mediaType)
        | false, _ -> false

    member _.ContentType : string option =
        match ctx.Request.ContentType with
        | null | "" -> None
        | ct -> Some ct

    member _.Cookie (name: string) : string option =
        match ctx.Request.Cookies.TryGetValue(name) with
        | true, value -> Some value
        | false, _ -> None

    member _.RequestId : string option =
        match ctx.Items.TryGetValue(RequestKeys.RequestIdItemKey) with
        | true, requestId -> Some (requestId :?> string)
        | false, _ ->
            match ctx.Request.Headers.TryGetValue("X-Request-Id") with
            | true, values -> Some (values.ToString())
            | false, _ -> None

    member _.CorrelationId : string option =
        match ctx.Items.TryGetValue(RequestKeys.CorrelationIdItemKey) with
        | true, correlationId -> Some (correlationId :?> string)
        | false, _ ->
            match ctx.Request.Headers.TryGetValue("X-Correlation-Id") with
            | true, values -> Some (values.ToString())
            | false, _ -> None

    member _.Files() : Task<UploadedFile list> =
        let request = ctx.Request
        task {
            let! form = request.ReadFormAsync()
            return [ for f in form.Files -> { Name = f.Name; FileName = f.FileName; ContentType = f.ContentType; Length = f.Length; Stream = f.OpenReadStream() } ]
        }

    member _.File(name: string) : Task<UploadedFile option> =
        let request = ctx.Request
        task {
            let! form = request.ReadFormAsync()
            match form.Files.GetFile(name) with
            | null -> return None
            | f -> return Some { Name = f.Name; FileName = f.FileName; ContentType = f.ContentType; Length = f.Length; Stream = f.OpenReadStream() }
        }

    member _.Raw = ctx
