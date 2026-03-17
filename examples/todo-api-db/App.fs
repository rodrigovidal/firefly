module TodoApiDb.App

open System.Data
open Microsoft.Extensions.DependencyInjection
open Fire

let createTodoSchema = schema {
    let! title = Schema.required "title" Schema.string [ Schema.minLength 1; Schema.maxLength 200 ]
    return {| Title = title |}
}

let updateTodoSchema = schema {
    let! title = Schema.required "title" Schema.string [ Schema.minLength 1 ]
    let! completed = Schema.optional "completed" Schema.bool false []
    return {| Title = title; Completed = completed |}
}

// Helper to get a new DB connection from DI and ensure it's disposed via `use`.
// We resolve manually (instead of auto-DI) because IDbConnection needs deterministic
// disposal — the `use` binding ensures the connection is closed after each request.
let private openDb (req: Request) =
    req.Raw.RequestServices.GetRequiredService<IDbConnection>()

let routes =
    Route.start
    |> Route.get "/api/todos" (fun (req: Request) -> task {
        use conn = openDb req
        let todos = Db.getAll conn
        return Response.json todos
    })
    |> Route.get "/api/todos/%i" (fun (id: int) (req: Request) -> task {
        use conn = openDb req
        match Db.getById conn id with
        | Some todo -> return Response.json todo
        | None -> return Response.json {| error = "not found" |} |> Response.status 404
    })
    |> Route.post "/api/todos" (fun (req: Request) -> task {
        match! Schema.parseRequest createTodoSchema req with
        | Ok input ->
            use conn = openDb req
            match Db.create conn input.Title with
            | Some todo -> return Response.json todo |> Response.status 201
            | None -> return Response.json {| error = "failed to create" |} |> Response.status 500
        | Error errors ->
            return Response.json {| errors = errors |} |> Response.status 400
    })
    |> Route.put "/api/todos/%i" (fun (id: int) (req: Request) -> task {
        match! Schema.parseRequest updateTodoSchema req with
        | Ok input ->
            use conn = openDb req
            match Db.update conn id input.Title input.Completed with
            | Some todo -> return Response.json todo
            | None -> return Response.json {| error = "not found" |} |> Response.status 404
        | Error errors ->
            return Response.json {| errors = errors |} |> Response.status 400
    })
    |> Route.delete "/api/todos/%i" (fun (id: int) (req: Request) -> task {
        use conn = openDb req
        if Db.delete conn id then return Response.noContent
        else return Response.json {| error = "not found" |} |> Response.status 404
    })

let create (dbPath: string) =
    let connectionString = $"Data Source={dbPath}"
    // Ensure table exists on startup
    use initConn = Db.connect connectionString
    Db.ensureTable initConn

    let config =
        App.defaults
        |> App.port 0
        |> App.middleware Cors.allowAll
        |> App.middleware Log.toConsole
        |> App.dependencyInjection (fun services ->
            // Transient: each resolution gets a fresh, opened connection
            services.AddTransient<IDbConnection>(fun _ ->
                Db.connect connectionString
            ) |> ignore
        )
        |> App.notFound (fun _ -> task {
            return Response.json {| error = "not found" |} |> Response.status 404
        })
    (routes, config)
