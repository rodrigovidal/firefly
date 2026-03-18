# Fire Pipelines — Design

Named, reusable middleware stacks. `Route.pipe` applies a pipeline to a route group. Non-breaking — `Route.group` + `Route.middleware` remain for simple cases.

## 1. Pipeline Type

```fsharp
type Pipeline = {
    Name: string
    Middlewares: Middleware list
}

module Pipeline =
    let create (name: string) : Pipeline =
        { Name = name; Middlewares = [] }

    let plug (mw: Middleware) (pipeline: Pipeline) : Pipeline =
        { pipeline with Middlewares = pipeline.Middlewares @ [ mw ] }

    let empty : Pipeline =
        { Name = "empty"; Middlewares = [] }
```

## 2. Route.pipe

```fsharp
module Route =
    let pipe (prefix: string) (pipeline: Pipeline) (builder: RouteTable -> RouteTable) (table: RouteTable) : RouteTable
```

Behaves like `Route.group` but applies the pipeline's middlewares to all routes in the builder.

## Usage

```fsharp
let browser =
    Pipeline.create "browser"
    |> Pipeline.plug Csrf.protect
    |> Pipeline.plug Session.middleware

let api =
    Pipeline.create "api"
    |> Pipeline.plug Jwt.protect
    |> Pipeline.plug (RateLimit.perMinute 60)

Route.start
|> Route.pipe "/" browser (fun web ->
    web
    |> Route.get "/" homePage
    |> Route.get "/about" aboutPage)
|> Route.pipe "/api" api (fun api ->
    api
    |> Route.get "/users" listUsers
    |> Route.post "/users" createUser)
|> Route.pipe "/health" Pipeline.empty (fun h ->
    h
    |> Route.get "" Health.handler)
```

Simple cases still use `Route.group` + `Route.middleware`:

```fsharp
Route.start
|> Route.group "/admin" (fun admin ->
    admin
    |> Route.middleware authCheck
    |> Route.get "/" dashboard)
```

## File Structure

```
src/Fire/
  Pipeline.fs    -- Pipeline type + module (new)
  Route.fs       -- add Route.pipe (modify)
```

## Scope

**In scope:** Pipeline type, Pipeline.create/plug/empty, Route.pipe.

**Out of scope:** Layout-per-pipeline (use a layout middleware instead), pipeline introspection, named route lookup.
