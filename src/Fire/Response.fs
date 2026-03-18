namespace Fire

open System.IO
open System.Text.Json

type ResponseBody =
    | Empty
    | Text of string
    | Json of byte[]
    | Stream of Stream

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

    let ofResult (onOk: 'T -> Response) (onError: 'E -> Response) (result: Result<'T, 'E>) =
        match result with
        | Ok value -> onOk value
        | Error err -> onError err
