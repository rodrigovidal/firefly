module TodoApi.App

open System
open System.Collections.Concurrent
open System.Security.Claims
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens
open Fire

// --- Types ---

type Todo =
    { Id: int
      Title: string
      Completed: bool }

type CreateTodo = { Title: string }

type UpdateTodo =
    { Title: string
      Completed: bool }

// --- Store interface ---

type ITodoStore =
    abstract GetAll: unit -> Task<Todo list>
    abstract GetById: int -> Task<Todo option>
    abstract Create: string -> Task<Todo>
    abstract Update: int * UpdateTodo -> Task<Todo option>
    abstract Delete: int -> Task<bool>

// --- In-memory implementation ---

type InMemoryTodoStore() =
    let todos = ConcurrentDictionary<int, Todo>()
    let mutable nextId = 0

    interface ITodoStore with
        member _.GetAll() = task { return todos.Values |> Seq.toList }

        member _.GetById(id) = task {
            match todos.TryGetValue(id) with
            | true, todo -> return Some todo
            | false, _ -> return None
        }

        member _.Create(title) = task {
            let id = Interlocked.Increment(&nextId)
            let todo = { Id = id; Title = title; Completed = false }
            todos.[id] <- todo
            return todo
        }

        member _.Update(id, update) = task {
            match todos.TryGetValue(id) with
            | true, _ ->
                let updated = { Id = id; Title = update.Title; Completed = update.Completed }
                todos.[id] <- updated
                return Some updated
            | false, _ -> return None
        }

        member _.Delete(id) = task {
            match todos.TryRemove(id) with
            | true, _ -> return true
            | false, _ -> return false
        }

// --- JWT helpers ---

let jwtSecret =
    "todo-api-secret-key-must-be-at-least-32-characters!!"

let generateToken (userId: string) =
    let handler = JsonWebTokenHandler()
    let key = SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
    let descriptor =
        SecurityTokenDescriptor(
            SigningCredentials = SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Subject = ClaimsIdentity([| Claim("sub", userId) |]),
            Expires = DateTime.UtcNow.AddHours(24.0))
    handler.CreateToken(descriptor)

// --- Routes using auto-DI + model binding ---

let routes =
    let jwtAuth = Jwt.defaults jwtSecret |> Jwt.validate

    Route.start
    |> Route.post("/auth/token", fun (req: Request) -> task {
        let! body = req.Json<{| userId: string |}>()
        let token = generateToken body.userId
        return Response.json {| token = token |}
    })
    |> Route.group("/api/todos", fun group ->
        group
        // List all todos — deps only
        |> Route.get("", Func<{| Store: ITodoStore |}, Task<Response>>(fun deps -> task {
            let! items = deps.Store.GetAll()
            return Response.json {| todos = items |}
        }))
        // Get by id — deps + model binding (id from route param)
        |> Route.get("/:id", Func<{| Store: ITodoStore |}, {| Id: int |}, Task<Response>>(fun deps input -> task {
            let! todo = deps.Store.GetById(input.Id)
            match todo with
            | Some t -> return Response.json t
            | None -> return Response.json {| error = "todo not found" |} |> Response.status 404
        }))
        // Protected routes
        |> Route.middleware(jwtAuth)
        // Create — deps + model binding (title from body)
        |> Route.post("", Func<{| Store: ITodoStore |}, {| Title: string |}, Task<Response>>(fun deps input -> task {
            let! todo = deps.Store.Create(input.Title)
            return Response.json todo |> Response.status 201
        }))
        // Update — deps + model binding (id from route, title+completed from body)
        |> Route.put("/:id", Func<{| Store: ITodoStore |}, {| Id: int; Title: string; Completed: bool |}, Task<Response>>(fun deps input -> task {
            let! result = deps.Store.Update(input.Id, { Title = input.Title; Completed = input.Completed })
            match result with
            | Some updated -> return Response.json updated
            | None -> return Response.json {| error = "todo not found" |} |> Response.status 404
        }))
        // Delete — deps + model binding (id from route)
        |> Route.delete("/:id", Func<{| Store: ITodoStore |}, {| Id: int |}, Task<Response>>(fun deps input -> task {
            let! deleted = deps.Store.Delete(input.Id)
            if deleted then return Response.noContent
            else return Response.json {| error = "todo not found" |} |> Response.status 404
        }))
    )

let allRoutes =
    routes
    |> Route.get("/openapi.json", OpenApi.handler "Todo API" "1.0" routes)

// --- App factory ---

let create () =
    let config =
        App.defaults
        |> App.port 0
        |> App.middleware Cors.allowAll
        |> App.middleware (RateLimit.fixedWindow 100 (TimeSpan.FromMinutes 1.0) RateLimit.byIp)
        |> App.dependencyInjection (fun services ->
            services.AddSingleton<ITodoStore, InMemoryTodoStore>() |> ignore
        )
        |> App.notFound (fun (req: Request) -> task {
            return Response.json {| error = "not found"; path = req.Path |} |> Response.status 404
        })
    (allRoutes, config)

let createWith (store: ITodoStore) =
    let config =
        App.defaults
        |> App.port 0
        |> App.middleware Cors.allowAll
        |> App.middleware (RateLimit.fixedWindow 100 (TimeSpan.FromMinutes 1.0) RateLimit.byIp)
        |> App.dependencyInjection (fun services ->
            services.AddSingleton<ITodoStore>(store) |> ignore
        )
        |> App.notFound (fun (req: Request) -> task {
            return Response.json {| error = "not found"; path = req.Path |} |> Response.status 404
        })
    (allRoutes, config)
