namespace Fire

type ViewConfig = {
    Title: string
    Content: Node
    Scripts: string list
    Styles: string list
    Head: Node list
    Layout: (string -> string -> string) option
}

[<RequireQualifiedAccess>]
module View =

    let page (title: string) (content: Node) : ViewConfig =
        { Title = title
          Content = content
          Scripts = []
          Styles = []
          Head = []
          Layout = None }

    let withScript (src: string) (config: ViewConfig) : ViewConfig =
        { config with Scripts = config.Scripts @ [ src ] }

    let withStyle (href: string) (config: ViewConfig) : ViewConfig =
        { config with Styles = config.Styles @ [ href ] }

    let withHead (node: Node) (config: ViewConfig) : ViewConfig =
        { config with Head = config.Head @ [ node ] }

    let withLayout (layout: string -> string -> string) (config: ViewConfig) : ViewConfig =
        { config with Layout = Some layout }

    let render (config: ViewConfig) : Response =
        let content = Render.toHtml config.Content
        let html =
            match config.Layout with
            | Some layout -> layout config.Title content
            | None ->
                let sb = System.Text.StringBuilder()
                sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">") |> ignore
                sb.Append($"<title>{System.Net.WebUtility.HtmlEncode config.Title}</title>") |> ignore
                for href in config.Styles do
                    sb.Append($"""<link rel="stylesheet" href="{System.Net.WebUtility.HtmlEncode href}">""") |> ignore
                for node in config.Head do
                    sb.Append(Render.toHtml node) |> ignore
                sb.Append("</head><body>") |> ignore
                sb.Append(content) |> ignore
                for src in config.Scripts do
                    sb.Append($"""<script src="{System.Net.WebUtility.HtmlEncode src}"></script>""") |> ignore
                sb.Append("</body></html>") |> ignore
                sb.ToString()
        Response.html html
