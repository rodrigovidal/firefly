open System
open System.Collections.Concurrent
open System.Security.Claims
open System.Text
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

// --- In-memory storage ---

let todos = ConcurrentDictionary<int, Todo>()
let mutable nextId = 3

todos.[1] <- { Id = 1; Title = "Learn Fire"; Completed = false }
todos.[2] <- { Id = 2; Title = "Build something"; Completed = false }

// --- JWT helpers ---

let jwtSecret =
    "todo-api-secret-key-must-be-at-least-32-characters!!"

let jwtAuth = Jwt.defaults jwtSecret |> Jwt.validate

let generateToken (userId: string) =
    let handler = JsonWebTokenHandler()

    let key =
        SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))

    let descriptor =
        SecurityTokenDescriptor(
            SigningCredentials = SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Subject = ClaimsIdentity([| Claim("sub", userId) |]),
            Expires = DateTime.UtcNow.AddHours(24.0)
        )

    handler.CreateToken(descriptor)

// --- Routes ---

let routes =
    Route.start
    // Auth: get a token (simplified for demo purposes)
    |> Route.post "/auth/token" (fun req ->
        task {
            let! body = req.Json<{| userId: string |}>()
            let token = generateToken body.userId
            return Response.json {| token = token |}
        })
    // Public: list and get todos
    |> Route.group "/api/todos" (fun group ->
        group
        |> Route.get "" (fun _ ->
            task {
                let items = todos.Values |> Seq.toList
                return Response.json {| todos = items |}
            })
        |> Route.get
            "/:id"
            (Validate.param
                [ "id", Validate.isInt ]
                (fun req ->
                    task {
                        let id = int req.Params.["id"]

                        match todos.TryGetValue(id) with
                        | true, todo -> return Response.json todo
                        | false, _ -> return Response.json {| error = "todo not found" |} |> Response.status 404
                    }))
        // Protected: create, update, delete (JWT required)
        |> Route.middleware jwtAuth
        |> Route.post
            ""
            (Validate.body
                (Validate.required "title" (fun (t: CreateTodo) -> t.Title))
                (fun body ->
                    task {
                        let id =
                            System.Threading.Interlocked.Increment(&nextId) - 1

                        let todo =
                            { Id = id
                              Title = body.Title
                              Completed = false }

                        todos.[id] <- todo
                        return Response.json todo |> Response.status 201
                    }))
        |> Route.put "/:id" (fun req ->
            task {
                let id = int req.Params.["id"]
                let! body = req.Json<UpdateTodo>()

                match todos.TryGetValue(id) with
                | true, _ ->
                    let updated =
                        { Id = id
                          Title = body.Title
                          Completed = body.Completed }

                    todos.[id] <- updated
                    return Response.json updated
                | false, _ -> return Response.json {| error = "todo not found" |} |> Response.status 404
            })
        |> Route.delete "/:id" (fun req ->
            task {
                let id = int req.Params.["id"]

                match todos.TryRemove(id) with
                | true, _ -> return Response.noContent
                | false, _ -> return Response.json {| error = "todo not found" |} |> Response.status 404
            }))

// Append OpenAPI endpoint (routes are already built, so no circular reference)
let allRoutes =
    routes
    |> Route.get "/openapi.json" (OpenApi.handler "Todo API" "1.0" routes)

// --- App configuration ---

printfn "Todo API running on http://localhost:3000"
printfn "  GET    /api/todos       - List todos"
printfn "  GET    /api/todos/:id   - Get todo"
printfn "  POST   /api/todos       - Create todo (auth required)"
printfn "  PUT    /api/todos/:id   - Update todo (auth required)"
printfn "  DELETE /api/todos/:id   - Delete todo (auth required)"
printfn "  POST   /auth/token      - Get JWT token"
printfn "  GET    /openapi.json    - OpenAPI spec"

let config =
    App.defaults
    |> App.port 3000
    |> App.middleware Cors.allowAll
    |> App.middleware Log.toConsole
    |> App.middleware (RateLimit.fixedWindow 100 (TimeSpan.FromMinutes 1.0) RateLimit.byIp)
    |> App.notFound (fun req ->
        task {
            return
                Response.json {| error = "not found"; path = req.Path |}
                |> Response.status 404
        })

App.run allRoutes config System.Threading.CancellationToken.None
|> fun t -> t.Wait()
