namespace Fire

type ViewConfig = {
    Title: string
    Content: Node
    Scripts: string list
    Styles: string list
    Head: Node list
    Layout: (string -> string -> string) option
    QueryCache: QueryCache option
}

[<RequireQualifiedAccess>]
module View =

    let page (title: string) (content: Node) : ViewConfig =
        { Title = title
          Content = content
          Scripts = []
          Styles = []
          Head = []
          Layout = None
          QueryCache = None }

    let withScript (src: string) (config: ViewConfig) : ViewConfig =
        { config with Scripts = config.Scripts @ [ src ] }

    let withStyle (href: string) (config: ViewConfig) : ViewConfig =
        { config with Styles = config.Styles @ [ href ] }

    let withHead (node: Node) (config: ViewConfig) : ViewConfig =
        { config with Head = config.Head @ [ node ] }

    let withLayout (layout: string -> string -> string) (config: ViewConfig) : ViewConfig =
        { config with Layout = Some layout }

    let withQueryCache (cache: QueryCache) (config: ViewConfig) : ViewConfig =
        { config with QueryCache = Some cache }

    let render (config: ViewConfig) : Response =
        let content = Render.toHtml config.Content
        let dehydrationScript =
            match config.QueryCache with
            | Some cache ->
                let script = cache.DehydrateScript()
                match script with
                | Node.Empty -> ""
                | _ -> Render.toHtml script
            | None -> ""
        let html =
            match config.Layout with
            | Some layout -> layout config.Title (content + dehydrationScript)
            | None ->
                let sb = System.Text.StringBuilder()
                sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">") |> ignore
                sb.Append($"<title>{System.Net.WebUtility.HtmlEncode config.Title}</title>") |> ignore
                for href in config.Styles do
                    sb.Append($"""<link rel="stylesheet" href="{System.Net.WebUtility.HtmlEncode href}">""") |> ignore
                for node in config.Head do
                    sb.Append(Render.toHtml node) |> ignore
                sb.Append("</head><body>") |> ignore
                sb.Append(content) |> ignore
                sb.Append(dehydrationScript) |> ignore
                for src in config.Scripts do
                    sb.Append($"""<script src="{System.Net.WebUtility.HtmlEncode src}"></script>""") |> ignore
                sb.Append("</body></html>") |> ignore
                sb.ToString()
        Response.html html

    /// Middleware that wraps the body content of HTML responses in a layout.
    /// Use in pipelines for nested layouts: inner views render content,
    /// this middleware wraps it in a section layout (e.g., admin sidebar),
    /// and the outermost View.withLayout renders the full document shell.
    ///
    /// The wrap function receives (title, body-inner-html) and returns new body-inner-html.
    /// It extracts content between <body> and </body>, wraps it, and puts it back.
    let layout (wrap: string -> string -> string) : Middleware =
        fun next req -> task {
            let! response = next req
            match response.Body with
            | ResponseBody.Text html when
                response.Headers |> List.exists (fun (k, v) ->
                    k.Equals("Content-Type", System.StringComparison.OrdinalIgnoreCase)
                    && v.Contains("text/html")) ->
                let bodyStart = html.IndexOf("<body>")
                let bodyEnd = html.IndexOf("</body>")
                if bodyStart >= 0 && bodyEnd > bodyStart then
                    let contentStart = bodyStart + 6
                    let content = html.Substring(contentStart, bodyEnd - contentStart)
                    let title =
                        let idx = html.IndexOf("<title>")
                        if idx >= 0 then
                            let endIdx = html.IndexOf("</title>", idx)
                            if endIdx >= 0 then html.Substring(idx + 7, endIdx - idx - 7)
                            else ""
                        else ""
                    let wrapped = wrap title content
                    let newHtml = html.Substring(0, contentStart) + wrapped + html.Substring(bodyEnd)
                    return { response with Body = ResponseBody.Text newHtml }
                else
                    return response
            | _ -> return response
        }
