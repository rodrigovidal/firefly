module Firefly.Cli.SimpleGenerator

open System
open System.IO

let private capitalize (s: string) =
    if String.IsNullOrEmpty(s) then s
    else s.Substring(0, 1).ToUpper() + s.Substring(1)

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

type Field = { Name: string; Type: string }

let parseFields (args: string list) : Field list =
    args |> List.map (fun arg ->
        match arg.Split(':') with
        | [| name; typ |] -> { Name = name; Type = typ }
        | _ -> failwith $"Invalid field format: {arg}. Expected name:type")

// --- Controller generator ---

let generateController (name: string) (projectRoot: string) =
    let controllerDir = Path.Combine(projectRoot, "Controllers")
    Directory.CreateDirectory(controllerDir) |> ignore

    let nameLower = lower name
    let content = $"""module MyApp.Controllers.{name}Controller

open Firefly
open Flame

let list (req: Request) = task {{
    return Response.json {{| items = [] |}}
}}

let getById (id: int) (req: Request) = task {{
    return Response.json {{| id = id |}}
}}

let create (req: Request) = task {{
    return Response.json {{| created = true |}} |> Response.status 201
}}

let update (id: int) (req: Request) = task {{
    return Response.json {{| id = id; updated = true |}}
}}

let delete (id: int) (req: Request) = task {{
    if true then return Response.noContent
    else return Response.json {{| error = "not found" |}} |> Response.status 404
}}

let routes =
    Route.group "/api/{nameLower}s" (fun group ->
        group
        |> Route.get "" list
        |> Route.get "/%%i" getById
        |> Route.post "" create
        |> Route.put "/%%i" update
        |> Route.delete "/%%i" delete
    )
"""

    let filePath = Path.Combine(controllerDir, $"{name}Controller.fs")
    File.WriteAllText(filePath, content)
    printfn $"Created Controllers/{name}Controller.fs"

// --- Schema generator ---

let generateSchema (name: string) (fields: Field list) (projectRoot: string) =
    let schemaDir = Path.Combine(projectRoot, "Schemas")
    Directory.CreateDirectory(schemaDir) |> ignore

    let recordFields =
        fields
        |> List.map (fun f -> $"    {capitalize f.Name}: {fsharpType f.Type}")
        |> String.concat "\n"

    let content = $"""module MyApp.Schemas.{name}Schema

open Flame

type {name}Input = {{
{recordFields}
}}

let schema = Schema.fromType<{name}Input>()
"""

    let filePath = Path.Combine(schemaDir, $"{name}Schema.fs")
    File.WriteAllText(filePath, content)
    printfn $"Created Schemas/{name}Schema.fs"

// --- Docker generator ---

let generateDocker (projectRoot: string) =
    let dockerfile = """FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MyApp.dll"]
"""

    let dockerCompose = """services:
  app:
    build: .
    ports:
      - "8080:8080"
    env_file:
      - .env
"""

    File.WriteAllText(Path.Combine(projectRoot, "Dockerfile"), dockerfile)
    printfn "Created Dockerfile"

    File.WriteAllText(Path.Combine(projectRoot, "docker-compose.yml"), dockerCompose)
    printfn "Created docker-compose.yml"
