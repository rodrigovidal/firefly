# Route DI + Model Binding Design

Breaking change: Route becomes a type with overloaded static methods. Handlers receive auto-injected dependencies and auto-bound input from route params, query strings, and request body.

## Handler Shapes

```fsharp
// Plain handler
Route.get("/health", fun req -> task { return Response.ok })

// Deps only (resolved from DI)
Route.get("/todos", fun (deps: {| Store: ITodoStore |}) -> task {
    let! todos = deps.Store.GetAll()
    return Response.json todos
})

// Deps + input (model binding)
Route.get("/todos/:id", fun (deps: {| Store: ITodoStore |}) (input: {| Id: int |}) -> task {
    let! todo = deps.Store.GetById(input.Id)
    return Response.json todo
})

// Deps + input + Request (when you need raw access)
Route.post("/upload", fun (deps: {| Store: IFileStore |}) (input: {| Name: string |}) req -> task {
    let! bytes = req.Text()
    ...
})
```

Both named and anonymous records supported for deps and input.

## Route Type

```fsharp
type Route =
    static member start : RouteTable
    static member get(pattern, handler) : RouteTable -> RouteTable
    static member post(pattern, handler) : RouteTable -> RouteTable
    static member put(pattern, handler) : RouteTable -> RouteTable
    static member patch(pattern, handler) : RouteTable -> RouteTable
    static member delete(pattern, handler) : RouteTable -> RouteTable
    static member head(pattern, handler) : RouteTable -> RouteTable
    static member options(pattern, handler) : RouteTable -> RouteTable
    static member group(prefix, configure) : RouteTable -> RouteTable
    static member middleware(mw) : RouteTable -> RouteTable
```

Each HTTP method has overloads:
- `Func<Request, Task<Response>>` — plain
- `Func<'Deps, Task<Response>>` — deps only
- `Func<'Deps, 'Input, Task<Response>>` — deps + input
- `Func<'Deps, 'Input, Request, Task<Response>>` — deps + input + Request

## DI Resolution (DepsResolver)

At registration time:
1. Reflect on `'Deps` — get constructor parameters (works for named + anonymous records)
2. Store parameter types
3. Per-request: resolve each from `IServiceProvider`, construct record

Uses `PreComputeRecordConstructor` for named records, standard constructor reflection for anonymous records.

DI errors (missing service) → exception propagates to `App.onError` → 500.

## Model Binding (ModelBinder)

At registration time:
1. Reflect on `'Input` — get constructor parameters (name + type)
2. Build binder functions per field

At request time:
1. Collect sources:
   - GET/DELETE/HEAD: route params + query string
   - POST/PUT/PATCH: route params + JSON body (merged)
2. Match fields by name (case-insensitive)
3. Convert types
4. Construct record

Supported types:
- `string` — direct
- `int` — Int32.TryParse
- `bool` — Boolean.TryParse
- `float` — Double.TryParse
- `string option` — Some if present, None if missing
- `int option` — parse + option
- `bool option` — parse + option
- `float option` — parse + option

Binding errors → 400 with `{ "errors": [...] }`. All errors collected (no short-circuit).

Missing required field (non-option) → 400 `"fieldname is required"`.
Failed type conversion → 400 `"fieldname: expected integer"`.
Malformed JSON body → 400 `"invalid request body"`.

## Detection Logic

How Fire determines handler shape from `Func<>` type arguments:

1. `Func<Request, Task<Response>>` → plain handler (Request is first param)
2. `Func<'D, Task<Response>>` where 'D ≠ Request → deps only
3. `Func<'D, 'I, Task<Response>>` → deps + input
4. `Func<'D, 'I, Request, Task<Response>>` → deps + input + Request

## Removed

- `Inject` module (replaced by auto-injection in Route)
- `req.Service<'T>()` (replaced by deps record)
- `Validate.body`, `Validate.query`, `Validate.param`, `Validate.headerValues` (replaced by model binding)
- Plain `Handler` type alias stays but Route overloads accept broader signatures

## Files

**New:**
- `src/Fire/ModelBinder.fs`
- `src/Fire/DepsResolver.fs`

**Changed:**
- `src/Fire/Route.fs` — module → type
- All tests and examples — parens syntax

**Removed:**
- `src/Fire/Inject.fs`
- `src/Fire/Validate.fs` (pure validator functions stay, handler wrappers removed)

## Syntax Migration

```fsharp
// Before:
Route.start |> Route.get "/path" (fun req -> task { ... })
Route.start |> Route.group "/api" (fun api -> api |> Route.get "/x" handler)

// After:
Route.start |> Route.get("/path", fun req -> task { ... })
Route.start |> Route.group("/api", fun api -> api |> Route.get("/x", handler))
```
