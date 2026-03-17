namespace Fire

open System
open System.Security.Cryptography

[<RequireQualifiedAccess>]
module AutoETag =

    /// Middleware that automatically generates ETag headers from response body hash.
    /// Returns 304 Not Modified if the client's If-None-Match matches.
    let middleware : Middleware =
        fun next req -> task {
            let! response = next req
            // Only apply to successful GET responses with body content
            if req.Method <> "GET" || response.Status <> 200 then
                return response
            else
                let bodyBytes =
                    match response.Body with
                    | Text s -> Some (System.Text.Encoding.UTF8.GetBytes(s))
                    | Json bytes -> Some bytes
                    | _ -> None

                match bodyBytes with
                | Some bytes ->
                    let hash = SHA256.HashData(bytes)
                    let etag = $"\"{Convert.ToHexStringLower(hash.AsSpan(0, 8))}\""

                    match req.Header "If-None-Match" with
                    | Some clientEtag when clientEtag = etag ->
                        return { Status = 304; Headers = [("ETag", etag)]; Body = Empty }
                    | _ ->
                        return response |> Response.etag etag
                | None -> return response
        }
