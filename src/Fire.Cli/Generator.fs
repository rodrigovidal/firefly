module Fire.Cli.Generator

open System
open System.IO

type Field = { Name: string; Type: string }

type GeneratorOptions = {
    Kind: string          // "html" or "json"
    Resource: string      // e.g. "Users"
    Fields: Field list
    ProjectDir: string    // src/<AppName>/
    Namespace: string     // e.g. "MyApp"
}

let capitalize (s: string) =
    if String.IsNullOrEmpty(s) then s
    else s.Substring(0, 1).ToUpper() + s.Substring(1)

let singular (name: string) =
    if name.Length < 2 then name
    elif name.EndsWith("ies", StringComparison.OrdinalIgnoreCase) then
        name.Substring(0, name.Length - 3) + "y"
    elif name.EndsWith("ses", StringComparison.OrdinalIgnoreCase)
         || name.EndsWith("xes", StringComparison.OrdinalIgnoreCase)
         || name.EndsWith("zes", StringComparison.OrdinalIgnoreCase)
         || name.EndsWith("shes", StringComparison.OrdinalIgnoreCase)
         || name.EndsWith("ches", StringComparison.OrdinalIgnoreCase) then
        name.Substring(0, name.Length - 2)
    elif name.EndsWith("s", StringComparison.OrdinalIgnoreCase) then
        name.Substring(0, name.Length - 1)
    else name

let private lower (s: string) =
    if String.IsNullOrEmpty(s) then s
    else s.Substring(0, 1).ToLower() + s.Substring(1)

let private fsharpType (t: string) =
    match t.ToLower() with
    | "string" -> "string"
    | "int" -> "int"
    | "float" -> "float"
    | "bool" -> "bool"
    | other -> failwith $"Unknown field type: {other}. Supported: string, int, float, bool"

let private schemaParser (t: string) =
    match t.ToLower() with
    | "string" -> "Schema.string"
    | "int" -> "Schema.int"
    | "float" -> "Schema.float"
    | "bool" -> "Schema.bool"
    | other -> failwith $"Unknown field type: {other}"

let private schemaRules (t: string) =
    match t.ToLower() with
    | "string" -> "[ Schema.minLength 1 ]"
    | _ -> "[]"

let parseFields (args: string list) : Field list =
    args |> List.map (fun arg ->
        match arg.Split(':') with
        | [| name; typ |] -> { Name = name; Type = typ }
        | _ -> failwith $"Invalid field format: {arg}. Expected name:type")

// --- Domain file ---

let private generateDomain (opts: GeneratorOptions) =
    let entity = singular opts.Resource
    let fields =
        opts.Fields
        |> List.map (fun f -> $"    {capitalize f.Name}: {fsharpType f.Type}")
        |> String.concat "\n"
    let inputFields =
        opts.Fields
        |> List.map (fun f ->
            let cap = capitalize f.Name
            $"{cap}: {fsharpType f.Type}")
        |> String.concat "; "
    let fieldCaps =
        opts.Fields |> List.map (fun f -> capitalize f.Name)
    let createFields =
        fieldCaps
        |> List.map (fun n -> $"{n} = input.{n}")
        |> String.concat "; "
    let updateFields =
        fieldCaps
        |> List.map (fun n -> $"{n} = input.{n}")
        |> String.concat "; "

    $"""namespace {opts.Namespace}.Domain

open System
open System.Threading.Tasks

type {entity} = {{
    Id: Guid
{fields}
}}

type I{entity}Repository =
    abstract List : cursor: Guid option -> limit: int -> Task<{{| Items: {entity} list; NextCursor: Guid option |}}>
    abstract Get : id: Guid -> Task<{entity} option>
    abstract Create : input: {{| {inputFields} |}} -> Task<{entity}>
    abstract Update : id: Guid -> input: {{| {inputFields} |}} -> Task<{entity} option>
    abstract Delete : id: Guid -> Task<bool>

type InMemory{entity}Repository() =
    let items = System.Collections.Generic.List<{entity}>()

    interface I{entity}Repository with
        member _.List cursor limit = task {{
            let filtered =
                match cursor with
                | Some c -> items |> Seq.filter (fun x -> x.Id > c)
                | None -> items |> Seq.cast
            let taken = filtered |> Seq.truncate (limit + 1) |> Seq.toList
            let hasMore = taken.Length > limit
            let page = taken |> List.truncate limit
            let nextCursor = if hasMore then Some (page |> List.last).Id else None
            return {{| Items = page; NextCursor = nextCursor |}}
        }}

        member _.Get id = task {{
            return items |> Seq.tryFind (fun x -> x.Id = id)
        }}

        member _.Create input = task {{
            let item = {{ Id = Guid.CreateVersion7(); {createFields} }}
            items.Add(item)
            return item
        }}

        member _.Update id input = task {{
            match items |> Seq.tryFindIndex (fun x -> x.Id = id) with
            | Some idx ->
                items.[idx] <- {{ items.[idx] with {updateFields} }}
                return Some items.[idx]
            | None -> return None
        }}

        member _.Delete id = task {{
            match items |> Seq.tryFindIndex (fun x -> x.Id = id) with
            | Some idx -> items.RemoveAt(idx); return true
            | None -> return false
        }}
"""

// --- Schema ---

let private generateSchema (opts: GeneratorOptions) =
    let entity = singular opts.Resource
    let bindings =
        opts.Fields
        |> List.map (fun f ->
            let cap = capitalize f.Name
            $"""    let! {lower f.Name} = Schema.required "{lower f.Name}" {schemaParser f.Type} {schemaRules f.Type}""")
        |> String.concat "\n"
    let returnFields =
        opts.Fields
        |> List.map (fun f ->
            let cap = capitalize f.Name
            $"{cap} = {lower f.Name}")
        |> String.concat "; "
    $"""let {lower entity}Schema = schema {{
{bindings}
    return {{| {returnFields} |}}
}}"""

// --- HTML Controller ---

let private generateHtmlController (opts: GeneratorOptions) =
    let entity = singular opts.Resource
    let resource = lower opts.Resource
    let schemaCode = generateSchema opts
    let fieldCaps = opts.Fields |> List.map (fun f -> capitalize f.Name)
    let formValues =
        opts.Fields
        |> List.map (fun f ->
            $"\"{lower f.Name}\", tryGet \"{lower f.Name}\"")
    let formValuesStr = formValues |> String.concat "; "
    let editValues =
        opts.Fields
        |> List.map (fun f ->
            let cap = capitalize f.Name
            $"\"{lower f.Name}\", item.{cap}")
    let editValuesStr = editValues |> String.concat "; "

    $"""namespace {opts.Namespace}.Controllers

open System
open Fire
open Flame
open {opts.Namespace}.Domain
open {opts.Namespace}.Views

module {entity}Controller =

    {schemaCode}

    let list (repo: I{entity}Repository) (req: Request) = task {{
        let limit = req.QueryParam "limit" |> Option.map int |> Option.defaultValue 20
        let cursor = req.QueryParam "cursor" |> Option.bind (fun s ->
            match Guid.TryParse(s) with true, g -> Some g | _ -> None)
        let! result = repo.List cursor limit
        return {entity}View.list result.Items result.NextCursor limit
    }}

    let get (id: string) (repo: I{entity}Repository) (_req: Request) = task {{
        match Guid.TryParse(id) with
        | true, guid ->
            match! repo.Get guid with
            | Some item -> return {entity}View.show item
            | None -> return Response.notFound
        | _ -> return Response.notFound
    }}

    let newForm (_req: Request) = task {{
        return {entity}View.form "New {entity}" "/{resource}" Map.empty Map.empty
    }}

    let create (repo: I{entity}Repository) (req: Request) = task {{
        match! Schema.parse {lower entity}Schema req with
        | Ok input ->
            let! item = repo.Create input
            return Response.ok |> Response.redirect $"/{resource}/{{item.Id}}" 303
        | Error errors ->
            let errorMap = errors |> List.choose (fun e ->
                match e.IndexOf(':') with
                | -1 -> None
                | i -> Some(e.Substring(0, i).Trim().ToLowerInvariant(), e.Substring(i + 1).Trim()))
                |> Map.ofList
            let! form = req.Form()
            let tryGet key = match form.TryGetValue(key) with true, v -> v | _ -> ""
            let values = Map.ofList [ {formValuesStr} ]
            return {entity}View.form "New {entity}" "/{resource}" values errorMap
    }}

    let editForm (id: string) (repo: I{entity}Repository) (_req: Request) = task {{
        match Guid.TryParse(id) with
        | true, guid ->
            match! repo.Get guid with
            | Some item ->
                let values = Map.ofList [ {editValuesStr} ]
                return {entity}View.form "Edit {entity}" $"/{resource}/{{item.Id}}/edit" values Map.empty
            | None -> return Response.notFound
        | _ -> return Response.notFound
    }}

    let update (id: string) (repo: I{entity}Repository) (req: Request) = task {{
        match Guid.TryParse(id) with
        | true, guid ->
            match! Schema.parse {lower entity}Schema req with
            | Ok input ->
                match! repo.Update guid input with
                | Some _ -> return Response.ok |> Response.redirect $"/{resource}/{{guid}}" 303
                | None -> return Response.notFound
            | Error errors ->
                let errorMap = errors |> List.choose (fun e ->
                    match e.IndexOf(':') with
                    | -1 -> None
                    | i -> Some(e.Substring(0, i).Trim().ToLowerInvariant(), e.Substring(i + 1).Trim()))
                    |> Map.ofList
                let! form = req.Form()
                let tryGet key = match form.TryGetValue(key) with true, v -> v | _ -> ""
                let values = Map.ofList [ {formValuesStr} ]
                return {entity}View.form "Edit {entity}" $"/{resource}/{{guid}}/edit" values errorMap
        | _ -> return Response.notFound
    }}

    let delete (id: string) (repo: I{entity}Repository) (_req: Request) = task {{
        match Guid.TryParse(id) with
        | true, guid ->
            let! _ = repo.Delete guid
            return Response.ok |> Response.redirect "/{resource}" 303
        | _ -> return Response.notFound
    }}
"""

// --- HTML View ---

let private generateHtmlView (opts: GeneratorOptions) =
    let entity = singular opts.Resource
    let resource = lower opts.Resource
    let fieldCaps = opts.Fields |> List.map (fun f -> capitalize f.Name)
    let showFields =
        fieldCaps
        |> List.map (fun n -> $"""                    Html.p [ Html.strong [ Text "{n}: " ]; Text (string item.{n}) ]""")
        |> String.concat "\n"
    let formFields =
        opts.Fields
        |> List.map (fun f ->
            let cap = capitalize f.Name
            match f.Type.ToLower() with
            | "bool" ->
                $"""                    Html.label [ Text "{cap}" ]
                    Html.input ([ Type "checkbox"; Name "{lower f.Name}" ] @ (if values |> Map.tryFind "{lower f.Name}" = Some "true" then [ Checked ] else []))
                    match errors |> Map.tryFind "{lower f.Name}" with
                    | Some msg -> Html.p ([ Class "error" ], [ Text msg ])
                    | None -> Empty"""
            | typ ->
                let inputType = match typ with "int" | "float" -> "number" | _ -> "text"
                $"""                    Html.label [ Text "{cap}" ]
                    Html.input [ Type "{inputType}"; Name "{lower f.Name}"; Value (values |> Map.tryFind "{lower f.Name}" |> Option.defaultValue "") ]
                    match errors |> Map.tryFind "{lower f.Name}" with
                    | Some msg -> Html.p ([ Class "error" ], [ Text msg ])
                    | None -> Empty""")
        |> String.concat "\n"

    $"""namespace {opts.Namespace}.Views

open System
open Fire
open {opts.Namespace}.Domain

module {entity}View =

    let list (items: {entity} list) (nextCursor: Guid option) (limit: int) =
        View.page "{opts.Resource}" (
            Html.div [
                Html.h1 [ Text "{opts.Resource}" ]
                Html.a ([ Href "/{resource}/new" ], [ Text "New {entity}" ])
                Html.ul [
                    for item in items do
                        Html.li [ Html.a ([ Href $"/{resource}/{{item.Id}}" ], [ Text (string item.{fieldCaps.[0]}) ]) ]
                ]
                Html.nav ([ Class "pagination" ], [
                    match nextCursor with
                    | Some cursor ->
                        Html.a ([ Href $"/{resource}?cursor={{cursor}}&limit={{limit}}" ], [ Text "Next" ])
                    | None -> Empty
                ])
            ]
        ) |> View.render

    let show (item: {entity}) =
        View.page (string item.{fieldCaps.[0]}) (
            Html.div [
                Html.h1 [ Text (string item.{fieldCaps.[0]}) ]
                Html.div ([ Class "card" ], [
{showFields}
                ])
                Html.div ([ Class "actions" ], [
                    Html.a ([ Href $"/{resource}/{{item.Id}}/edit" ], [ Html.button [ Text "Edit" ] ])
                    Html.form ([ Custom("method", "POST"); Custom("action", $"/{resource}/{{item.Id}}/delete") ], [
                        Html.button ([ Class "btn-danger" ], [ Text "Delete" ])
                    ])
                ])
            ]
        ) |> View.render

    let form (title: string) (action: string) (values: Map<string, string>) (errors: Map<string, string>) =
        View.page title (
            Html.div [
                Html.h1 [ Text title ]
                Html.form ([ Custom("method", "POST"); Custom("action", action) ], [
{formFields}
                    Html.button [ Text "Save" ]
                ])
            ]
        ) |> View.render
"""

// --- JSON API ---

let private generateJsonApi (opts: GeneratorOptions) =
    let entity = singular opts.Resource
    let resource = lower opts.Resource
    let schemaCode = generateSchema opts

    $"""namespace {opts.Namespace}.Api

open System
open Fire
open Flame
open {opts.Namespace}.Domain

module {entity}Api =

    {schemaCode}

    let list (repo: I{entity}Repository) (req: Request) = task {{
        let limit = req.QueryParam "limit" |> Option.map int |> Option.defaultValue 20
        let cursor = req.QueryParam "cursor" |> Option.bind (fun s ->
            match Guid.TryParse(s) with true, g -> Some g | _ -> None)
        let! result = repo.List cursor limit
        return Response.json result
    }}

    let get (id: string) (repo: I{entity}Repository) (_req: Request) = task {{
        match Guid.TryParse(id) with
        | true, guid ->
            match! repo.Get guid with
            | Some item -> return Response.json item
            | None -> return Response.notFound
        | _ -> return Response.notFound
    }}

    let create (repo: I{entity}Repository) (req: Request) = task {{
        match! Schema.parseRequest {lower entity}Schema req with
        | Ok input ->
            let! item = repo.Create input
            return Response.json item |> Response.status 201
        | Error errors ->
            return Response.json {{| errors = errors |}} |> Response.status 400
    }}

    let update (id: string) (repo: I{entity}Repository) (req: Request) = task {{
        match Guid.TryParse(id) with
        | true, guid ->
            match! Schema.parseRequest {lower entity}Schema req with
            | Ok input ->
                match! repo.Update guid input with
                | Some item -> return Response.json item
                | None -> return Response.notFound
            | Error errors ->
                return Response.json {{| errors = errors |}} |> Response.status 400
        | _ -> return Response.notFound
    }}

    let delete (id: string) (repo: I{entity}Repository) (_req: Request) = task {{
        match Guid.TryParse(id) with
        | true, guid ->
            match! repo.Delete guid with
            | true -> return Response.noContent
            | false -> return Response.notFound
        | _ -> return Response.notFound
    }}
"""

// --- Route printing ---

let private printHtmlRoutes (opts: GeneratorOptions) =
    let entity = singular opts.Resource
    let resource = lower opts.Resource
    $"""
Add these routes to Router.fs:

    |> Route.group "/{resource}" (fun routes ->
        routes
        |> Route.get "" {entity}Controller.list
        |> Route.get "/new" {entity}Controller.newForm
        |> Route.post "" {entity}Controller.create
        |> Route.get "/%%s" {entity}Controller.get
        |> Route.get "/%%s/edit" {entity}Controller.editForm
        |> Route.post "/%%s/edit" {entity}Controller.update
        |> Route.post "/%%s/delete" {entity}Controller.delete)
"""

let private printJsonRoutes (opts: GeneratorOptions) =
    let entity = singular opts.Resource
    let resource = lower opts.Resource
    $"""
Add these routes to Router.fs:

    |> Route.group "/{resource}" (fun routes ->
        routes
        |> Route.get "" {entity}Api.list
        |> Route.post "" {entity}Api.create
        |> Route.get "/%%s" {entity}Api.get
        |> Route.put "/%%s" {entity}Api.update
        |> Route.delete "/%%s" {entity}Api.delete)
"""

let private printDiRegistration (opts: GeneratorOptions) =
    let entity = singular opts.Resource
    $"""
Register the repository in Endpoint.fs:

    |> Fire.App.di (fun services ->
        services.AddSingleton<I{entity}Repository, InMemory{entity}Repository>() |> ignore)
"""

let private printFsprojEntries (opts: GeneratorOptions) =
    let entity = singular opts.Resource
    match opts.Kind with
    | "html" ->
        $"""
Add to your .fsproj compile list (before Router.fs):

    <Compile Include="Domain\\{entity}.fs" />
    <Compile Include="Views\\{entity}View.fs" />
    <Compile Include="Controllers\\{entity}Controller.fs" />
"""
    | "json" ->
        $"""
Add to your .fsproj compile list (before Router.fs):

    <Compile Include="Domain\\{entity}.fs" />
    <Compile Include="Api\\{entity}Api.fs" />
"""
    | _ -> ""

// --- Main entry point ---

let generate (opts: GeneratorOptions) =
    let entity = singular opts.Resource

    // Write domain file
    let domainDir = Path.Combine(opts.ProjectDir, "Domain")
    Directory.CreateDirectory(domainDir) |> ignore
    File.WriteAllText(Path.Combine(domainDir, $"{entity}.fs"), generateDomain opts)
    printfn $"Created Domain/{entity}.fs"

    match opts.Kind with
    | "html" ->
        let controllerDir = Path.Combine(opts.ProjectDir, "Controllers")
        Directory.CreateDirectory(controllerDir) |> ignore
        File.WriteAllText(Path.Combine(controllerDir, $"{entity}Controller.fs"), generateHtmlController opts)
        printfn $"Created Controllers/{entity}Controller.fs"

        let viewDir = Path.Combine(opts.ProjectDir, "Views")
        Directory.CreateDirectory(viewDir) |> ignore
        File.WriteAllText(Path.Combine(viewDir, $"{entity}View.fs"), generateHtmlView opts)
        printfn $"Created Views/{entity}View.fs"

        printfn "%s" (printHtmlRoutes opts)
    | "json" ->
        let apiDir = Path.Combine(opts.ProjectDir, "Api")
        Directory.CreateDirectory(apiDir) |> ignore
        File.WriteAllText(Path.Combine(apiDir, $"{entity}Api.fs"), generateJsonApi opts)
        printfn $"Created Api/{entity}Api.fs"

        printfn "%s" (printJsonRoutes opts)
    | kind ->
        failwith $"Unknown generator kind: {kind}. Use 'html' or 'json'."

    printfn "%s" (printDiRegistration opts)
    printfn "%s" (printFsprojEntries opts)
