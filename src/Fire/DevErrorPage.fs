namespace Fire

open System
open System.Text
open System.Threading.Tasks
open System.Net

[<RequireQualifiedAccess>]
module DevErrorPage =

    let private encode (value: string) =
        WebUtility.HtmlEncode(value)

    let private renderPairs (pairs: (string * string) list) =
        if List.isEmpty pairs then
            "<li><code>(none)</code></li>"
        else
            pairs
            |> List.map (fun (key, value) ->
                $"<li><strong>{encode key}</strong>: <code>{encode value}</code></li>")
            |> String.concat ""

    let private renderStackTrace (stackTrace: string option) =
        match stackTrace with
        | Some value when not (String.IsNullOrWhiteSpace value) ->
            $"<pre>{encode value}</pre>"
        | _ ->
            "<p>No stack trace available.</p>"

    let private page (ex: exn) (req: Request) =
        let routeParams = req.Params |> Seq.map (fun kvp -> kvp.Key, kvp.Value) |> Seq.toList
        let headers =
            req.Raw.Request.Headers
            |> Seq.map (fun header -> header.Key, header.Value.ToString())
            |> Seq.toList

        let requestId = req.RequestId |> Option.defaultValue "(missing)"
        let correlationId = req.CorrelationId |> Option.defaultValue "(missing)"
        let requestPath = req.Path |> Option.ofObj |> Option.defaultValue "/"

        let builder = StringBuilder()
        builder.AppendLine("<!DOCTYPE html>") |> ignore
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\" />") |> ignore
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />") |> ignore
        builder.AppendLine("<title>Fire Development Error</title>") |> ignore
        builder.AppendLine("<style>") |> ignore
        builder.AppendLine("body{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;background:#111827;color:#f9fafb;margin:0;padding:32px;}") |> ignore
        builder.AppendLine(".shell{max-width:1080px;margin:0 auto;}") |> ignore
        builder.AppendLine(".banner{background:#7f1d1d;border:1px solid #ef4444;border-radius:16px;padding:20px 24px;margin-bottom:24px;}") |> ignore
        builder.AppendLine(".grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:16px;margin-bottom:24px;}") |> ignore
        builder.AppendLine(".card{background:#1f2937;border:1px solid #374151;border-radius:16px;padding:18px;}") |> ignore
        builder.AppendLine("h1,h2{margin:0 0 12px 0;} h1{font-size:28px;} h2{font-size:18px;}") |> ignore
        builder.AppendLine("p,li{line-height:1.5;} ul{margin:0;padding-left:20px;} code,pre{background:#030712;color:#e5e7eb;border-radius:10px;} code{padding:2px 6px;} pre{padding:16px;overflow:auto;}") |> ignore
        builder.AppendLine(".meta{display:grid;grid-template-columns:max-content 1fr;gap:8px 12px;align-items:start;}") |> ignore
        builder.AppendLine(".meta strong{color:#fca5a5;}") |> ignore
        builder.AppendLine("</style></head><body><main class=\"shell\">") |> ignore
        builder.AppendLine($"<section class=\"banner\"><h1>{encode ex.Message}</h1><p>{encode (ex.GetType().FullName)}</p></section>") |> ignore
        builder.AppendLine("<section class=\"grid\">") |> ignore
        builder.AppendLine("<article class=\"card\"><h2>Request</h2><div class=\"meta\">") |> ignore
        builder.AppendLine($"<strong>Method</strong><span>{encode req.Method}</span>") |> ignore
        builder.AppendLine($"<strong>Path</strong><span>{encode requestPath}</span>") |> ignore
        builder.AppendLine($"<strong>Request ID</strong><span><code>{encode requestId}</code></span>") |> ignore
        builder.AppendLine($"<strong>Correlation ID</strong><span><code>{encode correlationId}</code></span>") |> ignore
        builder.AppendLine("</div></article>") |> ignore
        builder.AppendLine($"<article class=\"card\"><h2>Route Params</h2><ul>{renderPairs routeParams}</ul></article>") |> ignore
        builder.AppendLine($"<article class=\"card\"><h2>Headers</h2><ul>{renderPairs headers}</ul></article>") |> ignore
        builder.AppendLine("</section>") |> ignore
        builder.AppendLine("<section class=\"card\"><h2>Stack Trace</h2>") |> ignore
        builder.AppendLine(renderStackTrace (ex.StackTrace |> Option.ofObj)) |> ignore
        builder.AppendLine("</section>") |> ignore
        builder.AppendLine("</main></body></html>") |> ignore
        builder.ToString()

    let handler (ex: exn) (req: Request) : Task<Response> =
        task {
            return
                page ex req
                |> Response.html
                |> Response.status 500
        }
