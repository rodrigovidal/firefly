namespace Firefly

[<RequireQualifiedAccess>]
module Negotiate =

    let private parseAcceptHeader (header: string) =
        header.Split(',', System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList
        |> List.choose (fun part ->
            let segments = part.Split(';', System.StringSplitOptions.RemoveEmptyEntries ||| System.StringSplitOptions.TrimEntries)
            let mediaRange = segments.[0].Trim().ToLowerInvariant()
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
            Some (mediaRange, quality))

    let private mediaRangeMatches (supportedType: string) (mediaRange: string) =
        let normalizedSupported = supportedType.ToLowerInvariant()
        mediaRange = "*/*" ||
        mediaRange = normalizedSupported ||
        (mediaRange.EndsWith("/*", System.StringComparison.Ordinal) &&
         normalizedSupported.StartsWith(mediaRange.Substring(0, mediaRange.Length - 1), System.StringComparison.Ordinal))

    /// Returns 406 Not Acceptable if Accept header doesn't match any supported type.
    /// Supported types are provided as a list. Wildcard (*/*) is always accepted.
    let middleware (supportedTypes: string list) : Middleware =
        fun next req -> task {
            let accept = req.Header "Accept" |> Option.defaultValue "*/*"
            let isAcceptable =
                parseAcceptHeader accept
                |> List.exists (fun (mediaRange, quality) ->
                    quality > 0.0 &&
                    supportedTypes |> List.exists (fun supportedType -> mediaRangeMatches supportedType mediaRange))
            if not isAcceptable then
                return Response.json {| error = "Not Acceptable" |} |> Response.status 406
            else
                return! next req
        }
