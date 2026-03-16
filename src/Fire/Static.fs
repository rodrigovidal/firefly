namespace Fire

open System.IO

[<RequireQualifiedAccess>]
module Static =

    let private mimeTypes = dict [
        ".html", "text/html"; ".htm", "text/html"
        ".css", "text/css"
        ".js", "application/javascript"
        ".json", "application/json"
        ".png", "image/png"
        ".jpg", "image/jpeg"; ".jpeg", "image/jpeg"
        ".gif", "image/gif"
        ".svg", "image/svg+xml"
        ".ico", "image/x-icon"
        ".woff", "font/woff"; ".woff2", "font/woff2"; ".ttf", "font/ttf"
        ".txt", "text/plain"
        ".xml", "application/xml"
        ".pdf", "application/pdf"
    ]

    let private getContentType (path: string) =
        let ext = Path.GetExtension(path).ToLowerInvariant()
        match mimeTypes.TryGetValue(ext) with
        | true, ct -> ct
        | false, _ -> "application/octet-stream"

    let serve (rootDir: string) : Handler =
        let absRoot = Path.GetFullPath(rootDir)
        fun req -> task {
            let filePath = req.Params.["path"]
            let fullPath = Path.GetFullPath(Path.Combine(absRoot, filePath))
            if not (fullPath.StartsWith(absRoot)) then
                return Response.notFound
            elif File.Exists(fullPath) then
                let stream = File.OpenRead(fullPath)
                return
                    Response.stream stream
                    |> Response.header "Content-Type" (getContentType filePath)
            else
                return Response.notFound
        }
