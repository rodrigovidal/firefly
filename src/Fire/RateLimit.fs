namespace Firefly

open System
open System.Collections.Concurrent

[<RequireQualifiedAccess>]
module RateLimit =

    let fixedWindow (maxRequests: int) (window: TimeSpan) (keyFunc: Request -> string) : Middleware =
        let counters = ConcurrentDictionary<string, int * DateTime>()
        fun next req -> task {
            let key = keyFunc req
            let now = DateTime.UtcNow
            let (count, windowStart) =
                counters.AddOrUpdate(key,
                    (fun _ -> (1, now)),
                    (fun _ (c, ws) ->
                        if now - ws >= window then (1, now)
                        else (c + 1, ws)))
            if count > maxRequests then
                let retryAfter = int (window - (now - windowStart)).TotalSeconds
                return
                    { Status = 429; Headers = []; Body = Empty }
                    |> Response.header "Retry-After" (string (max 1 retryAfter))
            else
                return! next req
        }

    let byIp : Request -> string =
        fun req ->
            match req.Raw.Connection.RemoteIpAddress with
            | null -> "unknown"
            | ip -> ip.ToString()
