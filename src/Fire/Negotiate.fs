namespace Fire

[<RequireQualifiedAccess>]
module Negotiate =

    /// Returns 406 Not Acceptable if Accept header doesn't match any supported type.
    /// Supported types are provided as a list. Wildcard (*/*) is always accepted.
    let middleware (supportedTypes: string list) : Middleware =
        fun next req -> task {
            let accept = req.Header "Accept" |> Option.defaultValue "*/*"
            let isAcceptable =
                accept = "*/*" ||
                supportedTypes |> List.exists (fun t -> accept.Contains(t))
            if not isAcceptable then
                return Response.json {| error = "Not Acceptable" |} |> Response.status 406
            else
                return! next req
        }
