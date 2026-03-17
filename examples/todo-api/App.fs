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

// --- Routes (handlers resolve ITodoStore via DI) ---

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
        |> Route.get("", fun (req: Request) -> task {
            let store = req.Raw.RequestServices.GetRequiredService<ITodoStore>()
            let! items = store.GetAll()
            return Response.json {| todos = items |}
        })
        |> Route.get("/:id",
            Validate.param ["id", Validate.isInt] (fun (req: Request) -> task {
                let store = req.Raw.RequestServices.GetRequiredService<ITodoStore>()
                let id = int req.Params.["id"]
                let! todo = store.GetById(id)
                match todo with
                | Some t -> return Response.json t
                | None -> return Response.json {| error = "todo not found" |} |> Response.status 404
            }))
        |> Route.middleware(jwtAuth)
        |> Route.post("", fun (req: Request) -> task {
            let store = req.Raw.RequestServices.GetRequiredService<ITodoStore>()
            let! body = req.Json<CreateTodo>()
            if String.IsNullOrWhiteSpace body.Title then
                return Response.json {| errors = ["title is required"] |} |> Response.status 400
            else
                let! todo = store.Create(body.Title)
                return Response.json todo |> Response.status 201
        })
        |> Route.put("/:id", fun (req: Request) -> task {
            let store = req.Raw.RequestServices.GetRequiredService<ITodoStore>()
            let id = int req.Params.["id"]
            let! body = req.Json<UpdateTodo>()
            let! result = store.Update(id, body)
            match result with
            | Some updated -> return Response.json updated
            | None -> return Response.json {| error = "todo not found" |} |> Response.status 404
        })
        |> Route.delete("/:id", fun (req: Request) -> task {
            let store = req.Raw.RequestServices.GetRequiredService<ITodoStore>()
            let id = int req.Params.["id"]
            let! deleted = store.Delete(id)
            if deleted then return Response.noContent
            else return Response.json {| error = "todo not found" |} |> Response.status 404
        })
    )

let allRoutes =
    routes
    |> Route.get("/openapi.json", OpenApi.handler "Todo API" "1.0" routes)

// --- App factory ---

/// Create app with default InMemoryTodoStore
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

/// Create app with a custom ITodoStore — for testing
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
