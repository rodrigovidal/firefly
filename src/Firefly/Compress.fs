namespace Firefly

open System.IO
open System.IO.Compression

[<RequireQualifiedAccess>]
module Compress =

    let private parseWeightedTokens (header: string) =
        header.Split(',', System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList
        |> List.choose (fun part ->
            let segments = part.Split(';', System.StringSplitOptions.RemoveEmptyEntries ||| System.StringSplitOptions.TrimEntries)
            let token = segments.[0].Trim().ToLowerInvariant()
            let quality =
                segments
                |> Array.skip 1
                |> Array.tryPick (fun segment ->
                    if segment.StartsWith("q=", System.StringComparison.OrdinalIgnoreCase) then
                        match System.Double.TryParse(segment.Substring(2), System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture) with
                        | true, value -> Some value
                        | false, _ -> Some 0.0
                    else
                        None)
                |> Option.defaultValue 1.0
            Some (token, quality))

    let private acceptsEncoding (header: string) (encoding: string) =
        let normalized = encoding.ToLowerInvariant()
        parseWeightedTokens header
        |> List.exists (fun (token, quality) ->
            quality > 0.0 && (token = normalized || token = "*"))

    /// Middleware that compresses response bodies using gzip if client accepts it.
    let gzip : Middleware =
        fun next req -> task {
            let! response = next req
            let response = Internal.materializeJson response
            let acceptEncoding = req.Header "Accept-Encoding" |> Option.defaultValue ""
            if acceptsEncoding acceptEncoding "gzip" then
                match response.Body with
                | Text s ->
                    use ms = new MemoryStream()
                    use gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen = true)
                    let bytes = System.Text.Encoding.UTF8.GetBytes(s)
                    gz.Write(bytes, 0, bytes.Length)
                    gz.Flush()
                    gz.Close()
                    let compressed = ms.ToArray()
                    let hasContentType = response.Headers |> List.exists (fun (k, _) ->
                        k.Equals("Content-Type", System.StringComparison.OrdinalIgnoreCase))
                    let result =
                        { response with Body = Json compressed }
                        |> Response.header "Content-Encoding" "gzip"
                    return
                        if not hasContentType then
                            result |> Response.header "Content-Type" "text/plain; charset=utf-8"
                        else
                            result
                | Json bytes ->
                    use ms = new MemoryStream()
                    use gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen = true)
                    gz.Write(bytes, 0, bytes.Length)
                    gz.Flush()
                    gz.Close()
                    let compressed = ms.ToArray()
                    return
                        { response with Body = Json compressed }
                        |> Response.header "Content-Encoding" "gzip"
                | _ -> return response
            else
                return response
        }

    /// Middleware that compresses response bodies using brotli if client accepts it.
    let brotli : Middleware =
        fun next req -> task {
            let! response = next req
            let response = Internal.materializeJson response
            let acceptEncoding = req.Header "Accept-Encoding" |> Option.defaultValue ""
            if acceptsEncoding acceptEncoding "br" then
                match response.Body with
                | Text s ->
                    use ms = new MemoryStream()
                    use br = new BrotliStream(ms, CompressionLevel.Fastest, leaveOpen = true)
                    let bytes = System.Text.Encoding.UTF8.GetBytes(s)
                    br.Write(bytes, 0, bytes.Length)
                    br.Flush()
                    br.Close()
                    let compressed = ms.ToArray()
                    let hasContentType = response.Headers |> List.exists (fun (k, _) ->
                        k.Equals("Content-Type", System.StringComparison.OrdinalIgnoreCase))
                    let result =
                        { response with Body = Json compressed }
                        |> Response.header "Content-Encoding" "br"
                    return
                        if not hasContentType then
                            result |> Response.header "Content-Type" "text/plain; charset=utf-8"
                        else
                            result
                | Json bytes ->
                    use ms = new MemoryStream()
                    use br = new BrotliStream(ms, CompressionLevel.Fastest, leaveOpen = true)
                    br.Write(bytes, 0, bytes.Length)
                    br.Flush()
                    br.Close()
                    let compressed = ms.ToArray()
                    return
                        { response with Body = Json compressed }
                        |> Response.header "Content-Encoding" "br"
                | _ -> return response
            else
                return response
        }

    /// Auto-selects best compression: brotli > gzip > none
    let auto : Middleware =
        fun next req -> task {
            let acceptEncoding = req.Header "Accept-Encoding" |> Option.defaultValue ""
            if acceptsEncoding acceptEncoding "br" then
                return! brotli next req
            elif acceptsEncoding acceptEncoding "gzip" then
                return! gzip next req
            else
                return! next req
        }
