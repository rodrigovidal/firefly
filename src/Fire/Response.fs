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
    let ok = { Status = 200; Headers = []; Body = Empty }
    let text s = { ok with Body = Text s }

    let json<'T> (value: 'T) =
        { ok with Body = Json (JsonSerializer.SerializeToUtf8Bytes(value)) }

    let stream s = { ok with Body = Stream s }
    let status code r = { r with Status = code }
    let header key value r = { r with Headers = (key, value) :: r.Headers }

    let cookie name value r =
        r |> header "Set-Cookie" $"{name}={value}"

    let notFound = { ok with Status = 404 }
    let unauthorized = { ok with Status = 401 }

    let ofResult (onOk: 'T -> Response) (onError: 'E -> Response) (result: Result<'T, 'E>) =
        match result with
        | Ok value -> onOk value
        | Error err -> onError err
