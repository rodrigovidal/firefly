module Firefly.Cli.DevManifest

open System
open System.IO
open System.Text.Json

/// One generator invocation declared in firefly.json.
/// `fields` is "name:type" pairs (empty for controller/docker).
[<CLIMutable>]
type GenSpec = { kind: string; name: string; fields: string list }

[<CLIMutable>]
type FireflyManifest = { generators: GenSpec list }

let manifestFileName = "firefly.json"

let private jsonOptions = JsonSerializerOptions(PropertyNameCaseInsensitive = true)

/// Parse firefly.json content into a typed manifest.
let parse (json: string) : FireflyManifest =
    JsonSerializer.Deserialize<FireflyManifest>(json, jsonOptions)

/// Locate the app's (non-test) .fsproj under `dir`, mirroring the CLI's
/// project discovery: prefer src/**/*.fsproj, else a top-level .fsproj.
let findProjectIn (dir: string) : string option =
    let srcDir = Path.Combine(dir, "src")
    if Directory.Exists(srcDir) then
        Directory.GetFiles(srcDir, "*.fsproj", SearchOption.AllDirectories)
        |> Array.tryFind (fun path -> not (path.EndsWith(".Tests.fsproj", StringComparison.OrdinalIgnoreCase)))
    else
        Directory.GetFiles(dir, "*.fsproj", SearchOption.TopDirectoryOnly)
        |> Array.tryHead

/// Run a single generator by kind, the same dispatch used by `firefly gen`.
let runGen (workingDir: string) (kind: string) (name: string) (fields: string list) =
    match kind.ToLowerInvariant() with
    | "controller" ->
        SimpleGenerator.generateController (Generator.capitalize name) workingDir
    | "schema" ->
        SimpleGenerator.generateSchema (Generator.capitalize name) (SimpleGenerator.parseFields fields) workingDir
    | "docker" ->
        SimpleGenerator.generateDocker workingDir
    | "html" | "json" ->
        match findProjectIn workingDir with
        | Some projectPath ->
            Generator.generate {
                Kind = kind.ToLowerInvariant()
                Resource = Generator.capitalize name
                Fields = Generator.parseFields fields
                ProjectDir = Path.GetDirectoryName(projectPath)
                Namespace = Path.GetFileNameWithoutExtension(projectPath)
            }
        | None -> failwith "No F# project found. Run from the project root."
    | other ->
        failwith $"Unknown generator kind: {other}. Use html, json, controller, schema, or docker."

/// Run every generator declared in firefly.json (if present in `workingDir`).
/// Returns the number of generators run.
let runGenerators (workingDir: string) : int =
    let path = Path.Combine(workingDir, manifestFileName)
    if not (File.Exists path) then 0
    else
        let manifest = parse (File.ReadAllText path)
        let specs = if obj.ReferenceEquals(manifest.generators, null) then [] else manifest.generators
        for spec in specs do
            let fields = if obj.ReferenceEquals(spec.fields, null) then [] else spec.fields
            runGen workingDir spec.kind spec.name fields
        List.length specs
