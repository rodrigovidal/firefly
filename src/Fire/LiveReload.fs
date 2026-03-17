namespace Fire

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

[<RequireQualifiedAccess>]
module LiveReload =

    /// JS snippet injected before </body> in dev mode.
    /// Opens SSE connection; on disconnect (server restart), waits and reloads.
    let private script = """<script>(function(){var r=0,s=new EventSource("/__fire/livereload");s.onerror=function(){s.close();if(r<20){r++;setTimeout(function(){location.reload()},500);}};})();</script>"""

    /// SSE endpoint handler — holds connection open, sends pings.
    /// When dotnet watch restarts the server, the connection drops
    /// and the browser script reloads the page.
    let endpoint : HttpContext -> Task = fun ctx -> task {
        ctx.Response.ContentType <- "text/event-stream"
        ctx.Response.Headers.["Cache-Control"] <- "no-cache"
        ctx.Response.Headers.["Connection"] <- "keep-alive"
        let ct = ctx.RequestAborted
        try
            while not ct.IsCancellationRequested do
                do! ctx.Response.WriteAsync(": ping\n\n", ct)
                do! ctx.Response.Body.FlushAsync(ct)
                do! Task.Delay(5000, ct)
        with :? OperationCanceledException -> ()
    }

    /// Inject the live reload script into an HTML string before </body>.
    let injectScript (html: string) : string =
        let idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase)
        if idx >= 0 then html.Insert(idx, script)
        else html + script

    /// Check if a response is HTML by looking at its headers.
    let private isHtmlResponse (response: Response) =
        response.Headers |> List.exists (fun (k, v) ->
            k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
            && v.Contains("text/html"))

    /// Middleware that injects the live reload script into HTML responses.
    let middleware : Middleware =
        fun next req -> task {
            let! response = next req
            match response.Body with
            | Text s when isHtmlResponse response ->
                return { response with Body = Text (injectScript s) }
            | _ -> return response
        }
