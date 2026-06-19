open System.Threading
open Firefly
open BlogApi

let (routes, config) = App.create ()

let config' = config |> App.middleware Log.toConsole

printfn "Blog API running on http://localhost:3000"
printfn "  GET  /api/posts          - list posts (?tag=fsharp)"
printfn "  GET  /api/posts/:id      - get post"
printfn "  POST /api/posts          - create post"
printfn "  GET  /api/posts/:id/comments - list comments"
printfn "  POST /api/posts/:id/comments - add comment"
printfn "  GET  /api/tags           - list unique tags"
printfn "  GET  /feed               - redirect to /api/posts"

App.run routes config' CancellationToken.None
|> fun t -> t.GetAwaiter().GetResult()
