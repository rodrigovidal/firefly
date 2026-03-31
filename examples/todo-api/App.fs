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
open Flame
open Fire

// --- Types ---

type Todo =
    { Id: int
      Title: string
      Completed: bool }

type LoginRequest = { UserId: string }

type UpdateTodoInput = { Title: string; Completed: bool option }

// --- Schemas ---

// fromType: auto-generates schema from record type (cached, zero reflection after first call)
let loginSchema = Schema.fromType<LoginRequest>()

// fromType with option field: Completed becomes optional automatically
let updateTodoSchema = Schema.fromType<UpdateTodoInput>()

// Manual schema: when you need validation rules (nonempty, maxLength, trim, etc.)
let createTodoSchema = schema {
    let! title = Schema.required "Title" Schema.string [ Schema.nonempty; Schema.maxLength 200; Schema.trim ]
    return {| Title = title |}
}

// --- Store interface ---

type ITodoStore =
    abstract GetAll: unit -> Task<Todo list>
    abstract GetById: int -> Task<Todo option>
    abstract Create: string -> Task<Todo>
    abstract Update: int * string * bool -> Task<Todo option>
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

        member _.Update(id, title, completed) = task {
            match todos.TryGetValue(id) with
            | true, _ ->
                let updated = { Id = id; Title = title; Completed = completed }
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

// --- Routes using new handler system ---

let routes =
    let jwtAuth = Jwt.defaults jwtSecret |> Jwt.validate

    Route.start
    |> Route.post "/auth/token" (fun (req: Request) -> task {
        match! Schema.parseRequest loginSchema req with
        | Ok login ->
            let token = generateToken login.UserId
            return Response.json {| token = token |}
        | Error errors ->
            return Response.json {| errors = errors |} |> Response.status 400
    })
    |> Route.group "/api/todos" (fun group ->
        group
        // List all todos
        |> Route.get "" (fun (store: ITodoStore) -> task {
            let! items = store.GetAll()
            return Response.json {| todos = items |}
        })
        // Get by id
        |> Route.get "/%i" (fun (store: ITodoStore) (id: int) -> task {
            let! todo = store.GetById(id)
            match todo with
            | Some t -> return Response.json t
            | None -> return Response.json {| error = "todo not found" |} |> Response.status 404
        })
        // Protected routes
        |> Route.middleware jwtAuth
        // Create
        |> Route.post "" (fun (store: ITodoStore) (req: Request) -> task {
            match! Schema.parseRequest createTodoSchema req with
            | Ok input ->
                let! todo = store.Create(input.Title)
                return Response.json todo |> Response.status 201
            | Error errors ->
                return Response.json {| errors = errors |} |> Response.status 400
        })
        // Update (fromType schema: Completed is option — omit to keep current value)
        |> Route.put "/%i" (fun (store: ITodoStore) (id: int) (req: Request) -> task {
            match! Schema.parseRequest updateTodoSchema req with
            | Ok input ->
                let completed = input.Completed |> Option.defaultValue false
                let! result = store.Update(id, input.Title, completed)
                match result with
                | Some updated -> return Response.json updated
                | None -> return Response.json {| error = "todo not found" |} |> Response.status 404
            | Error errors ->
                return Response.json {| errors = errors |} |> Response.status 400
        })
        // Delete
        |> Route.delete "/%i" (fun (store: ITodoStore) (id: int) -> task {
            let! deleted = store.Delete(id)
            if deleted then return Response.noContent
            else return Response.json {| error = "todo not found" |} |> Response.status 404
        })
    )

let allRoutes =
    routes
    |> Route.get "/openapi.json" (OpenApi.handler "Todo API" "1.0" routes)

// --- App factory ---

let create () =
    let config =
        App.defaults
        |> App.port 0
        |> App.middleware Cors.allowAll
        |> App.middleware (RateLimit.fixedWindow 100 (TimeSpan.FromMinutes 1.0) RateLimit.byIp)
        |> App.services [ Service.singleton<ITodoStore, InMemoryTodoStore> ]
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
        |> App.services [ Service.instance store ]
        |> App.notFound (fun (req: Request) -> task {
            return Response.json {| error = "not found"; path = req.Path |} |> Response.status 404
        })
    (allRoutes, config)
