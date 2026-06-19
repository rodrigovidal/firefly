module Fire.Cli.OpenApiGenerator

open System
open System.Diagnostics
open System.IO
open System.Reflection
open System.Runtime.Loader
open Firefly

type private ProjectLoadContext(assemblyPath: string) =
    inherit AssemblyLoadContext(isCollectible = true)
    let resolver = AssemblyDependencyResolver(assemblyPath)

    override _.Load(name) =
        // Share types already loaded by the host (Fire, system assemblies, etc.)
        let existing =
            AssemblyLoadContext.Default.Assemblies
            |> Seq.tryFind (fun a -> a.GetName().Name = name.Name)
        match existing with
        | Some asm -> asm
        | None ->
            match resolver.ResolveAssemblyToPath(name) with
            | null -> null
            | path -> base.LoadFromAssemblyPath(path)

let private buildProject (projectPath: string) =
    let psi = ProcessStartInfo("dotnet", $"build \"{projectPath}\" -c Release --nologo -v q")
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false

    let proc = Process.Start(psi)
    proc.WaitForExit()

    if proc.ExitCode <> 0 then
        let err = proc.StandardError.ReadToEnd()
        failwith $"Build failed:\n{err}"

let private findOutputAssembly (projectPath: string) =
    let projectDir = Path.GetDirectoryName(projectPath)
    let projectName = Path.GetFileNameWithoutExtension(projectPath)

    // Look in bin/Release for any target framework
    let releaseDir = Path.Combine(projectDir, "bin", "Release")
    if not (Directory.Exists(releaseDir)) then
        failwith $"Build output not found: {releaseDir}"

    let candidates =
        Directory.GetFiles(releaseDir, $"{projectName}.dll", SearchOption.AllDirectories)
        |> Array.filter (fun p -> not (p.Contains("ref")))

    match candidates |> Array.tryHead with
    | Some path -> path
    | None -> failwith $"Could not find {projectName}.dll in {releaseDir}"

let private findRouteTables (assembly: Assembly) : (string * RouteTable) list =
    let routeTableType = typeof<RouteTable>
    assembly.GetTypes()
    |> Array.collect (fun t ->
        t.GetProperties(BindingFlags.Public ||| BindingFlags.Static)
        |> Array.choose (fun p ->
            if p.PropertyType = routeTableType then
                try
                    let value = p.GetValue(null) :?> RouteTable
                    let name = $"{t.Name}.{p.Name}"
                    Some (name, value)
                with _ -> None
            else None))
    |> Array.toList

type OpenApiOptions = {
    ProjectPath: string
    Title: string option
    Version: string option
    Output: string option
    RouteName: string option
}

let generate (opts: OpenApiOptions) =
    eprintfn "Building project..."
    buildProject opts.ProjectPath

    let assemblyPath = findOutputAssembly opts.ProjectPath
    eprintfn $"Loading assembly: {Path.GetFileName(assemblyPath)}"

    let loadContext = new ProjectLoadContext(assemblyPath)
    let assembly = loadContext.LoadFromAssemblyPath(assemblyPath)

    let routeTables = findRouteTables assembly

    if routeTables.IsEmpty then
        failwith "No RouteTable values found in the assembly. Expose routes as a module-level let binding of type RouteTable."

    let (name, routes) =
        match opts.RouteName with
        | Some target ->
            match routeTables |> List.tryFind (fun (n, _) -> n.EndsWith(target, StringComparison.OrdinalIgnoreCase)) with
            | Some found -> found
            | None ->
                let available = routeTables |> List.map fst |> String.concat ", "
                failwith $"Route table '{target}' not found. Available: {available}"
        | None ->
            if routeTables.Length > 1 then
                let available = routeTables |> List.map fst |> String.concat ", "
                eprintfn $"Multiple route tables found: {available}. Using first one. Use --routes <name> to pick."
            routeTables.[0]

    let title = opts.Title |> Option.defaultValue "API"
    let version = opts.Version |> Option.defaultValue "1.0"

    eprintfn $"Generating OpenAPI spec from {name} ({routes.Routes.Length} routes)"
    let spec = OpenApi.generate title version routes

    match opts.Output with
    | Some path ->
        File.WriteAllText(path, spec)
        eprintfn $"Written to {path}"
    | None ->
        printfn "%s" spec
