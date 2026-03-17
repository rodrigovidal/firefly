open System.Threading
open TodoApiDb

let dbPath = "todos.db"
let (routes, config) = App.create dbPath

printfn "Todo API (SQLite + Dapper) running on http://localhost:3000"
printfn "  GET    /api/todos       - List todos"
printfn "  GET    /api/todos/:id   - Get todo"
printfn "  POST   /api/todos       - Create todo (schema validated)"
printfn "  PUT    /api/todos/:id   - Update todo (schema validated)"
printfn "  DELETE /api/todos/:id   - Delete todo"
printfn "  Database: %s" dbPath

let config' = { config with Port = 3000 }
Fire.App.run routes config' CancellationToken.None
|> fun t -> t.GetAwaiter().GetResult()
