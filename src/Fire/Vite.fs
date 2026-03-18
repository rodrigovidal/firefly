namespace Fire

open System
open System.Text.Json

[<RequireQualifiedAccess>]
module Vite =

    type ManifestEntry = { File: string; Css: string list }

    let loadManifest (json: string) : Map<string, ManifestEntry> =
        let doc = JsonDocument.Parse(json)
        doc.RootElement.EnumerateObject()
        |> Seq.map (fun prop ->
            let file = prop.Value.GetProperty("file").GetString()
            let css =
                match prop.Value.TryGetProperty("css") with
                | true, arr -> arr.EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> Seq.toList
                | false, _ -> []
            prop.Name, { File = file; Css = css })
        |> Map.ofSeq

    let scriptFromManifest (manifest: Map<string, ManifestEntry>) (entry: string) : Node =
        let e = manifest.[entry]
        Element("script", [ Custom("type", "module"); Src $"/{e.File}" ], [])

    let stylesFromManifest (manifest: Map<string, ManifestEntry>) (entry: string) : Node =
        let e = manifest.[entry]
        match e.Css with
        | [] -> Node.Empty
        | cssFiles ->
            Fragment [
                for href in cssFiles do
                    Element("link", [ Custom("rel", "stylesheet"); Href $"/{href}" ], [])
            ]

    let reactRefreshProd () : Node = Node.Empty

    let scriptDev (port: int) (entry: string) : Node =
        Element("script", [ Custom("type", "module"); Src $"http://localhost:{port}/{entry}" ], [])

    let stylesDev () : Node = Node.Empty

    let reactRefreshDev (port: int) : Node =
        Raw $"""<script type="module">
import RefreshRuntime from 'http://localhost:{port}/@react-refresh'
RefreshRuntime.injectIntoGlobalHook(window)
window.$RefreshReg$ = () => {{}}
window.$RefreshSig$ = () => (type) => type
window.__vite_plugin_react_preamble_installed__ = true
</script>"""

    let shouldProxy (path: string) : bool =
        path.StartsWith("/@vite/", StringComparison.Ordinal) ||
        path.StartsWith("/@react-refresh", StringComparison.Ordinal) ||
        path.StartsWith("/src/", StringComparison.Ordinal) ||
        path.StartsWith("/node_modules/.vite/", StringComparison.Ordinal)

    let private isDevelopment () =
        let env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        String.Equals(env, "Development", StringComparison.OrdinalIgnoreCase)
        || String.Equals(env, "dev", StringComparison.OrdinalIgnoreCase)

    let mutable private cachedManifest : Map<string, ManifestEntry> option = None
    let private defaultPort = 5173

    let private getManifest (manifestPath: string) =
        match cachedManifest with
        | Some m -> m
        | None ->
            if not (IO.File.Exists manifestPath) then
                failwith $"Vite manifest not found at {manifestPath}. Run 'npm run build' first."
            let json = IO.File.ReadAllText manifestPath
            let m = loadManifest json
            cachedManifest <- Some m
            m

    let script (entry: string) : Node =
        if isDevelopment () then scriptDev defaultPort entry
        else scriptFromManifest (getManifest "wwwroot/.vite/manifest.json") entry

    let styles (entry: string) : Node =
        if isDevelopment () then stylesDev ()
        else stylesFromManifest (getManifest "wwwroot/.vite/manifest.json") entry

    let reactRefresh () : Node =
        if isDevelopment () then reactRefreshDev defaultPort
        else reactRefreshProd ()
