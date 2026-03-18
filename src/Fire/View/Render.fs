namespace Fire

open System.Net
open System.Text

[<RequireQualifiedAccess>]
module Render =

    let private voidElements =
        Set.ofList [ "area"; "base"; "br"; "col"; "embed"; "hr"; "img"; "input"; "link"; "meta"; "param"; "source"; "track"; "wbr" ]

    let private renderAttr (sb: StringBuilder) (attr: Attr) =
        match attr with
        | Class v -> sb.Append($""" class="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Id v -> sb.Append($""" id="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Href v -> sb.Append($""" href="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Src v -> sb.Append($""" src="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Type v -> sb.Append($""" type="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Name v -> sb.Append($""" name="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Value v -> sb.Append($""" value="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Placeholder v -> sb.Append($""" placeholder="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Style v -> sb.Append($""" style="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Disabled -> sb.Append(" disabled") |> ignore
        | Checked -> sb.Append(" checked") |> ignore
        | Required -> sb.Append(" required") |> ignore
        | Readonly -> sb.Append(" readonly") |> ignore
        | Data(k, v) -> sb.Append($""" data-{WebUtility.HtmlEncode k}="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore
        | Custom(k, v) -> sb.Append($""" {WebUtility.HtmlEncode k}="{WebUtility.HtmlEncode v}" """.TrimEnd()) |> ignore

    let rec private render (sb: StringBuilder) (node: Node) =
        match node with
        | Text s -> sb.Append(WebUtility.HtmlEncode s) |> ignore
        | Raw s -> sb.Append(s) |> ignore
        | Empty -> ()
        | Fragment nodes -> for n in nodes do render sb n
        | Element(tag, attrs, children) ->
            sb.Append('<').Append(tag) |> ignore
            for attr in attrs do renderAttr sb attr
            sb.Append('>') |> ignore
            if not (voidElements.Contains tag) then
                for child in children do render sb child
                sb.Append("</").Append(tag).Append('>') |> ignore

    let toHtml (node: Node) : string =
        let sb = StringBuilder()
        render sb node
        sb.ToString()
