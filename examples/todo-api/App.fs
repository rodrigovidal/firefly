module TodoApi.App

open System
open System.Collections.Concurrent
open System.Security.Claims
open System.Text
open System.Threading
open System.Threading.Tasks
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

// --- Store interface (function record — no DI container needed) ---

type TodoStore = {
    GetAll: unit -> Task<Todo list>
    GetById: int -> Task<Todo option>
    Create: string -> Task<Todo>
    Update: int -> UpdateTodo -> Task<Todo option>
    Delete: int -> Task<bool>
}

// --- In-memory store (production default + tests) ---

let inMemoryStore () : TodoStore =
    let todos = ConcurrentDictionary<int, Todo>()
    let mutable nextId = 0

    { GetAll = fun () -> task {
        return todos.Values |> Seq.toList
      }
      GetById = fun id -> task {
        match todos.TryGetValue(id) with
        | true, todo -> return Some todo
        | false, _ -> return None
      }
      Create = fun title -> task {
        let id = Interlocked.Increment(&nextId)
        let todo = { Id = id; Title = title; Completed = false }
        todos.[id] <- todo
        return todo
      }
      Update = fun id update -> task {
        match todos.TryGetValue(id) with
        | true, _ ->
            let updated = { Id = id; Title = update.Title; Completed = update.Completed }
            todos.[id] <- updated
            return Some updated
        | false, _ -> return None
      }
      Delete = fun id -> task {
        match todos.TryRemove(id) with
        | true, _ -> return true
        | false, _ -> return false
      }
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

// --- App factory: pass dependencies as arguments ---

let createWith (store: TodoStore) =
    let jwtAuth = Jwt.defaults jwtSecret |> Jwt.validate

    let routes =
        Route.start
        |> Route.post "/auth/token" (fun req -> task {
            let! body = req.Json<{| userId: string |}>()
            let token = generateToken body.userId
            return Response.json {| token = token |}
        })
        |> Route.group "/api/todos" (fun group ->
            group
            |> Route.get "" (fun _ -> task {
                let! items = store.GetAll()
                return Response.json {| todos = items |}
            })
            |> Route.get "/:id" (
                Validate.param ["id", Validate.isInt] (fun req -> task {
                    let id = int req.Params.["id"]
                    let! todo = store.GetById(id)
                    match todo with
                    | Some t -> return Response.json t
                    | None -> return Response.json {| error = "todo not found" |} |> Response.status 404
                }))
            |> Route.middleware jwtAuth
            |> Route.post "" (
                Validate.body
                    (Validate.required "title" (fun (t: CreateTodo) -> t.Title))
                    (fun body -> task {
                        let! todo = store.Create(body.Title)
                        return Response.json todo |> Response.status 201
                    }))
            |> Route.put "/:id" (fun req -> task {
                let id = int req.Params.["id"]
                let! body = req.Json<UpdateTodo>()
                let! result = store.Update id body
                match result with
                | Some updated -> return Response.json updated
                | None -> return Response.json {| error = "todo not found" |} |> Response.status 404
            })
            |> Route.delete "/:id" (fun req -> task {
                let id = int req.Params.["id"]
                let! deleted = store.Delete(id)
                if deleted then return Response.noContent
                else return Response.json {| error = "todo not found" |} |> Response.status 404
            })
        )

    let allRoutes =
        routes
        |> Route.get "/openapi.json" (OpenApi.handler "Todo API" "1.0" routes)

    let config =
        App.defaults
        |> App.port 0
        |> App.middleware Cors.allowAll
        |> App.middleware (RateLimit.fixedWindow 100 (TimeSpan.FromMinutes 1.0) RateLimit.byIp)
        |> App.notFound (fun req -> task {
            return
                Response.json {| error = "not found"; path = req.Path |}
                |> Response.status 404
        })

    (allRoutes, config)

let create () = createWith (inMemoryStore ())
