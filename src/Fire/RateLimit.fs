namespace Fire

open System
open System.Collections.Concurrent

[<RequireQualifiedAccess>]
module RateLimit =

    let private counters = ConcurrentDictionary<string, int * DateTime>()

    let fixedWindow (maxRequests: int) (window: TimeSpan) (keyFunc: Request -> string) : Middleware =
        fun next req -> task {
            let key = keyFunc req
            let now = DateTime.UtcNow
            let mutable blocked = false
            let mutable retryAfter = 0

            counters.AddOrUpdate(key,
                (fun _ -> (1, now)),
                (fun _ (c, ws) ->
                    if now - ws >= window then (1, now)
                    else
                        if c >= maxRequests then
                            blocked <- true
                            retryAfter <- int (window - (now - ws)).TotalSeconds
                            (c + 1, ws)
                        else
                            (c + 1, ws)))
            |> ignore

            if blocked then
                return
                    { Status = 429; Headers = []; Body = Empty }
                    |> Response.header "Retry-After" (string retryAfter)
            else
                return! next req
        }

    let byIp : Request -> string =
        fun req ->
            match req.Raw.Connection.RemoteIpAddress with
            | null -> "unknown"
            | ip -> ip.ToString()
