namespace Fire

open System
open System.Security.Cryptography

[<RequireQualifiedAccess>]
module Cache =

    /// Middleware that sets Cache-Control: public, max-age=N header on successful responses.
    let maxAge (seconds: int) : Middleware =
        fun next req -> task {
            let! response = next req
            if response.Status >= 200 && response.Status < 300 then
                return response |> Response.cacheControl $"public, max-age={seconds}"
            else
                return response
        }

    /// Middleware that sets Cache-Control: private, max-age=N (for user-specific content).
    let privateMaxAge (seconds: int) : Middleware =
        fun next req -> task {
            let! response = next req
            if response.Status >= 200 && response.Status < 300 then
                return response |> Response.cacheControl $"private, max-age={seconds}"
            else
                return response
        }

    /// Middleware that sets Cache-Control: no-store (disable caching).
    let noStore : Middleware =
        fun next req -> task {
            let! response = next req
            return response |> Response.cacheControl "no-store"
        }

    /// Middleware that adds Vary header for content negotiation caching.
    let varyBy (headers: string list) : Middleware =
        fun next req -> task {
            let! response = next req
            return response |> Response.header "Vary" (String.Join(", ", headers))
        }

    /// Middleware that auto-generates an ETag from the response body hash.
    /// Returns 304 Not Modified if the client sends a matching If-None-Match header.
    let etag : Middleware =
        fun next req -> task {
            let! response = next req
            if response.Status >= 200 && response.Status < 300 then
                let bodyBytes =
                    match response.Body with
                    | Text s -> Some (System.Text.Encoding.UTF8.GetBytes(s))
                    | Json bytes -> Some bytes
                    | _ -> None

                match bodyBytes with
                | Some bytes ->
                    let hash = SHA256.HashData(bytes)
                    let tag = $"\"{Convert.ToHexStringLower(hash.AsSpan(0, 8))}\""

                    match req.Header "If-None-Match" with
                    | Some clientTag when clientTag = tag ->
                        return { Status = 304; Headers = [("ETag", tag)]; Body = Empty }
                    | _ ->
                        return response |> Response.etag tag
                | None -> return response
            else
                return response
        }

    /// Middleware that combines max-age + ETag + Vary for common caching patterns.
    let standard (seconds: int) (varyHeaders: string list) : Middleware =
        fun next req -> task {
            let pipeline = etag (maxAge seconds (varyBy varyHeaders next))
            return! pipeline req
        }
