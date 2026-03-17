namespace Fire

open System

[<RequireQualifiedAccess>]
module CorrelationId =

    [<Literal>]
    let HeaderName = "X-Correlation-Id"

    let private getOrCreate (req: Request) =
        req.Header HeaderName
        |> Option.defaultWith (fun () -> Guid.NewGuid().ToString("N"))

    /// Middleware that adds X-Correlation-Id header to every response.
    /// If the request already has X-Correlation-Id, it's forwarded. Otherwise a new GUID is generated.
    let middleware : Middleware =
        fun next req -> task {
            let correlationId = getOrCreate req
            req.Raw.Items.[RequestKeys.CorrelationIdItemKey] <- correlationId
            let! response = next req
            return response |> Response.header HeaderName correlationId
        }
