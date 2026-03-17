open Fire
open TodoApi

let (routes, config) = App.create ()

printfn "Todo API running on http://localhost:3000"
printfn "  GET    /api/todos       - List todos"
printfn "  GET    /api/todos/:id   - Get todo"
printfn "  POST   /api/todos       - Create todo (auth required)"
printfn "  PUT    /api/todos/:id   - Update todo (auth required)"
printfn "  DELETE /api/todos/:id   - Delete todo (auth required)"
printfn "  POST   /auth/token      - Get JWT token"
printfn "  GET    /openapi.json    - OpenAPI spec"

let config' = config |> App.middleware Log.toConsole

App.run routes config' System.Threading.CancellationToken.None
|> fun t -> t.Wait()
