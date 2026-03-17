namespace Fire

open System

[<RequireQualifiedAccess>]
module RequestId =

    [<Literal>]
    let HeaderName = "X-Request-Id"

    /// Middleware that adds X-Request-Id header to every response.
    /// If the request already has X-Request-Id, it's forwarded. Otherwise a new GUID is generated.
    let middleware : Middleware =
        fun next req -> task {
            let requestId =
                req.Header HeaderName
                |> Option.defaultWith (fun () -> Guid.NewGuid().ToString("N"))
            req.Raw.Items.[RequestKeys.RequestIdItemKey] <- requestId
            let! response = next req
            return response |> Response.header HeaderName requestId
        }
