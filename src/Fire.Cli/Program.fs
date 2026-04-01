module Fire.Cli.Program

open System
open System.Diagnostics
open System.IO

type NewOptions = {
    Name: string
    OutputDir: string
    Force: bool
}

let private cliRoot =
    AppContext.BaseDirectory

let private templateRoot =
    Path.Combine(cliRoot, "Templates", "FireApp")

let private sourceToken = "FireApp"

let private templateConfigDirName = ".template.config"

let private usage () =
    String.concat Environment.NewLine [
        "Fire CLI"
        ""
        "Commands:"
        "  fire new <Name> [--output <path>] [--force]"
        "  fire dev [--project <path>]"
        "  fire gen html <Resource> field:type [field:type ...]"
        "  fire gen json <Resource> field:type [field:type ...]"
        "  fire gen controller <Name>"
        "  fire gen schema <Name> field:type [field:type ...]"
        "  fire gen docker"
        "  fire openapi [--project <path>] [--output <path>] [--title <title>] [--version <version>] [--routes <name>]"
    ]

let private ensureDirectory path =
    Directory.CreateDirectory(path) |> ignore

let rec copyTemplate (sourceDir: string) (targetDir: string) (name: string) =
    ensureDirectory targetDir

    for file in Directory.GetFiles(sourceDir) do
        let fileName = Path.GetFileName(file)
        if fileName <> "template.json" then
            let targetFile = Path.Combine(targetDir, fileName.Replace(sourceToken, name))
            let contents = File.ReadAllText(file).Replace(sourceToken, name)
            File.WriteAllText(targetFile, contents)

    for dir in Directory.GetDirectories(sourceDir) do
        let dirName = Path.GetFileName(dir)
        if dirName <> templateConfigDirName then
            let targetSubDir = Path.Combine(targetDir, dirName.Replace(sourceToken, name))
            copyTemplate dir targetSubDir name

let private tryFindRepoFireProject (startingDir: string) =
    let rec loop (current: DirectoryInfo) =
        if isNull current then
            None
        else
            let candidate = Path.Combine(current.FullName, "src", "Fire", "Fire.fsproj")
            if File.Exists(candidate) then Some candidate
            else loop current.Parent

    loop (DirectoryInfo(startingDir))

let private fireProjectPath () =
    tryFindRepoFireProject(Environment.CurrentDirectory)

let private fireReferenceItems () =
    match fireProjectPath () with
    | Some projectPath ->
        let normalized = projectPath.Replace("\\", "/")
        $"""  <ItemGroup>
    <ProjectReference Include="{normalized}" />
  </ItemGroup>"""
    | None ->
        """  <ItemGroup>
    <PackageReference Include="Fire" Version="0.1.0" />
  </ItemGroup>"""

let private fireTestReferenceItems () =
    match fireProjectPath () with
    | Some projectPath ->
        let projectPath = projectPath.Replace("\\", "/")
        $"""  <ItemGroup>
    <ProjectReference Include="{projectPath}" />
  </ItemGroup>"""
    | None ->
        """  <ItemGroup>
    <PackageReference Include="Fire" Version="0.1.0" />
  </ItemGroup>"""

let private writeFile (path: string) (contents: string) =
    ensureDirectory (Path.GetDirectoryName(path))
    File.WriteAllText(path, contents)

let private solutionTemplate name =
    $"""Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project("{{F2A71F9B-5D33-465A-A702-920D77279786}}") = "{name}", "src\{name}\{name}.fsproj", "{{A1111111-1111-1111-1111-111111111111}}"
EndProject
Project("{{F2A71F9B-5D33-465A-A702-920D77279786}}") = "{name}.Tests", "tests\{name}.Tests\{name}.Tests.fsproj", "{{B2222222-2222-2222-2222-222222222222}}"
EndProject
Global
    GlobalSection(SolutionConfigurationPlatforms) = preSolution
        Debug|Any CPU = Debug|Any CPU
        Release|Any CPU = Release|Any CPU
    EndGlobalSection
    GlobalSection(ProjectConfigurationPlatforms) = postSolution
        {{A1111111-1111-1111-1111-111111111111}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        {{A1111111-1111-1111-1111-111111111111}}.Debug|Any CPU.Build.0 = Debug|Any CPU
        {{A1111111-1111-1111-1111-111111111111}}.Release|Any CPU.ActiveCfg = Release|Any CPU
        {{A1111111-1111-1111-1111-111111111111}}.Release|Any CPU.Build.0 = Release|Any CPU
        {{B2222222-2222-2222-2222-222222222222}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        {{B2222222-2222-2222-2222-222222222222}}.Debug|Any CPU.Build.0 = Debug|Any CPU
        {{B2222222-2222-2222-2222-222222222222}}.Release|Any CPU.ActiveCfg = Release|Any CPU
        {{B2222222-2222-2222-2222-222222222222}}.Release|Any CPU.Build.0 = Release|Any CPU
    EndGlobalSection
    GlobalSection(SolutionProperties) = preSolution
        HideSolutionNode = FALSE
    EndGlobalSection
EndGlobal
"""

let private substituteFireReference (projectFile: string) =
    let contents = File.ReadAllText(projectFile)
    let updated =
        contents.Replace("__FIRE_REFERENCE_ITEMS__", fireReferenceItems ())
                .Replace("__FIRE_TEST_REFERENCE_ITEMS__", fireTestReferenceItems ())
    File.WriteAllText(projectFile, updated)

let private createNewProject (options: NewOptions) =
    let targetDir = Path.GetFullPath(options.OutputDir)

    if Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir) |> Seq.isEmpty |> not && not options.Force then
        failwith $"Target directory already exists and is not empty: {targetDir}"

    if not (Directory.Exists(templateRoot)) then
        failwith $"Template directory not found: {templateRoot}"

    copyTemplate templateRoot targetDir options.Name
    writeFile (Path.Combine(targetDir, $"{options.Name}.sln")) (solutionTemplate options.Name)
    substituteFireReference (Path.Combine(targetDir, "src", options.Name, $"{options.Name}.fsproj"))
    substituteFireReference (Path.Combine(targetDir, "tests", $"{options.Name}.Tests", $"{options.Name}.Tests.fsproj"))
    printfn $"Created Fire app at {targetDir}"

let private findProjectFromCurrentDirectory () =
    let srcDir = Path.Combine(Environment.CurrentDirectory, "src")
    if Directory.Exists(srcDir) then
        Directory.GetFiles(srcDir, "*.fsproj", SearchOption.AllDirectories)
        |> Array.tryFind (fun path -> not (path.EndsWith(".Tests.fsproj", StringComparison.OrdinalIgnoreCase)))
    else
        Directory.GetFiles(Environment.CurrentDirectory, "*.fsproj", SearchOption.TopDirectoryOnly)
        |> Array.tryHead

let private startProcess (fileName: string) (arguments: string) (workingDir: string) =
    let psi = ProcessStartInfo(fileName, arguments)
    psi.WorkingDirectory <- workingDir
    psi.UseShellExecute <- false
    psi.EnvironmentVariables["DOTNET_ENVIRONMENT"] <- "Development"
    psi.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] <- "Development"
    let proc = Process.Start(psi)

    if isNull proc then
        failwith $"Failed to start process: {fileName} {arguments}"

    proc.WaitForExit()
    proc.ExitCode

let private parseOpenApiArgs (args: string list) =
    let rec loop args (opts: OpenApiGenerator.OpenApiOptions) =
        match args with
        | [] -> opts
        | "--project" :: path :: rest -> loop rest { opts with ProjectPath = Path.GetFullPath(path) }
        | "--output" :: path :: rest -> loop rest { opts with Output = Some path }
        | "--title" :: title :: rest -> loop rest { opts with Title = Some title }
        | "--version" :: version :: rest -> loop rest { opts with Version = Some version }
        | "--routes" :: name :: rest -> loop rest { opts with RouteName = Some name }
        | unknown :: _ -> failwith $"Unknown option: {unknown}"

    let projectPath =
        match findProjectFromCurrentDirectory () with
        | Some path -> path
        | None -> failwith "No F# project found. Pass --project <path>."

    loop args {
        ProjectPath = projectPath
        Title = None
        Version = None
        Output = None
        RouteName = None
    }

let private runDev (projectPath: string option) =
    let resolvedProject =
        match projectPath with
        | Some path -> Path.GetFullPath(path)
        | None ->
            match findProjectFromCurrentDirectory () with
            | Some path -> path
            | None -> failwith "No F# project found. Pass --project <path>."

    let args = $"watch run --project \"{resolvedProject}\""
    startProcess "dotnet" args Environment.CurrentDirectory

[<EntryPoint>]
let main argv =
    try
        match argv |> Array.toList with
        | ["new"; name] ->
            createNewProject {
                Name = name
                OutputDir = Path.Combine(Environment.CurrentDirectory, name)
                Force = false
            }
            0
        | ["new"; name; "--output"; outputDir] ->
            createNewProject { Name = name; OutputDir = outputDir; Force = false }
            0
        | ["new"; name; "--force"] ->
            createNewProject {
                Name = name
                OutputDir = Path.Combine(Environment.CurrentDirectory, name)
                Force = true
            }
            0
        | ["new"; name; "--output"; outputDir; "--force"]
        | ["new"; name; "--force"; "--output"; outputDir] ->
            createNewProject { Name = name; OutputDir = outputDir; Force = true }
            0
        | ["gen"; "controller"; name] ->
            SimpleGenerator.generateController (Generator.capitalize name) Environment.CurrentDirectory
            0
        | ["gen"; "schema"; name] ->
            eprintfn "Usage: fire gen schema <Name> field:type [field:type ...]"
            1
        | "gen" :: "schema" :: name :: fields when fields.Length > 0 ->
            SimpleGenerator.generateSchema (Generator.capitalize name) (SimpleGenerator.parseFields fields) Environment.CurrentDirectory
            0
        | ["gen"; "docker"] ->
            SimpleGenerator.generateDocker Environment.CurrentDirectory
            0
        | "gen" :: kind :: resource :: fields when fields.Length > 0 ->
            let projectPath =
                match findProjectFromCurrentDirectory () with
                | Some path -> path
                | None -> failwith "No F# project found. Run from project root."
            Generator.generate {
                Kind = kind
                Resource = Generator.capitalize resource
                Fields = Generator.parseFields fields
                ProjectDir = Path.GetDirectoryName(projectPath)
                Namespace = Path.GetFileNameWithoutExtension(projectPath)
            }
            0
        | ["gen"] | ["gen"; _] ->
            eprintfn "Usage: fire gen html|json <Resource> field:type [field:type ...]"
            eprintfn "       fire gen controller <Name>"
            eprintfn "       fire gen schema <Name> field:type [field:type ...]"
            eprintfn "       fire gen docker"
            1
        | ["dev"] ->
            runDev None
        | ["dev"; "--project"; projectPath] ->
            runDev (Some projectPath)
        | "openapi" :: rest ->
            let opts = parseOpenApiArgs rest
            OpenApiGenerator.generate opts
            0
        | ["help"]
        | ["--help"]
        | ["-h"] ->
            printfn "%s" (usage ())
            0
        | _ ->
            printfn "%s" (usage ())
            1
    with ex ->
        eprintfn "%s" ex.Message
        1
