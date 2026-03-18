namespace Fire

open System

[<RequireQualifiedAccess>]
module Partial =

    let private isHtmlResponse (response: Response) =
        response.Headers |> List.exists (fun (k, v) ->
            k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
            && v.Contains("text/html"))

    let private extractTitle (html: string) : string =
        let idx = html.IndexOf("<title>")
        if idx >= 0 then
            let endIdx = html.IndexOf("</title>", idx)
            if endIdx >= 0 then html.Substring(idx + 7, endIdx - idx - 7)
            else ""
        else ""

    let private extractBody (html: string) : string option =
        let bodyIdx = html.IndexOf("<body")
        if bodyIdx < 0 then None
        else
            let closeTag = html.IndexOf('>', bodyIdx)
            let bodyEnd = html.IndexOf("</body>")
            if closeTag >= 0 && bodyEnd > closeTag then
                let contentStart = closeTag + 1
                Some (html.Substring(contentStart, bodyEnd - contentStart))
            else None

    /// Middleware that strips the HTML shell for client-side navigation requests.
    /// When X-Fire-Navigation header is present, returns only the body content
    /// and sets X-Fire-Title header with the page title.
    let middleware : Middleware =
        fun next req -> task {
            let! response = next req
            match req.Header "X-Fire-Navigation" with
            | Some "true" ->
                match response.Body with
                | Text html when isHtmlResponse response ->
                    match extractBody html with
                    | Some body ->
                        let title = extractTitle html
                        return
                            { response with Body = Text body }
                            |> Response.header "X-Fire-Title" title
                    | None -> return response
                | _ -> return response
            | _ -> return response
        }
