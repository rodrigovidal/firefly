module TodoApi.App

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

// --- JWT helpers ---

let jwtSecret =
    "todo-api-secret-key-must-be-at-least-32-characters!!"

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

// --- App factory ---

let create () =
    let todos = ConcurrentDictionary<int, Todo>()
    let mutable nextId = 0

    let jwtAuth = Jwt.defaults jwtSecret |> Jwt.validate

    let routes =
        Route.start
        |> Route.post "/auth/token" (fun req ->
            task {
                let! body = req.Json<{| userId: string |}>()
                let token = generateToken body.userId
                return Response.json {| token = token |}
            })
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
            |> Route.middleware jwtAuth
            |> Route.post
                ""
                (Validate.body
                    (Validate.required "title" (fun (t: CreateTodo) -> t.Title))
                    (fun body ->
                        task {
                            let id =
                                System.Threading.Interlocked.Increment(&nextId)

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

    let allRoutes =
        routes
        |> Route.get "/openapi.json" (OpenApi.handler "Todo API" "1.0" routes)

    let config =
        App.defaults
        |> App.port 3000
        |> App.middleware Cors.allowAll
        |> App.middleware (RateLimit.fixedWindow 100 (TimeSpan.FromMinutes 1.0) RateLimit.byIp)
        |> App.notFound (fun req ->
            task {
                return
                    Response.json {| error = "not found"; path = req.Path |}
                    |> Response.status 404
            })

    (allRoutes, config)
