namespace Fire

open System

[<RequireQualifiedAccess>]
module RequestId =

    /// Middleware that adds X-Request-Id header to every response.
    /// If the request already has X-Request-Id, it's forwarded. Otherwise a new GUID is generated.
    let middleware : Middleware =
        fun next req -> task {
            let requestId =
                req.Header "X-Request-Id"
                |> Option.defaultWith (fun () -> Guid.NewGuid().ToString("N"))
            let! response = next req
            return response |> Response.header "X-Request-Id" requestId
        }
