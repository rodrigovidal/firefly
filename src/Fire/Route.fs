namespace Fire

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Threading.Tasks

type RouteEntry = {
    Method: string
    Pattern: string
    Middlewares: Middleware list
    Handler: Handler
}

type RouteTable = {
    Prefix: string
    Middlewares: Middleware list
    Routes: RouteEntry list
}

type Route private () =

    static member start = { Prefix = ""; Middlewares = []; Routes = [] }

    static member private addRoute (verb: string) (pattern: string) (handler: Handler) (table: RouteTable) =
        let entry = {
            Method = verb
            Pattern = table.Prefix + pattern
            Middlewares = table.Middlewares
            Handler = handler
        }
        { table with Routes = table.Routes @ [entry] }

    // --- Private helpers to reduce duplication ---

    static member private withDeps<'Deps> (verb: string) (pattern: string) (handler: Func<'Deps, Task<Response>>) =
        let resolver = DepsResolver.create typeof<'Deps>
        fun table ->
            let h : Handler = fun req ->
                let deps = resolver req.Raw.RequestServices :?> 'Deps
                handler.Invoke(deps)
            Route.addRoute verb pattern h table

    static member private withDepsAndInput<'Deps, 'Input> (verb: string) (pattern: string) (handler: Func<'Deps, 'Input, Task<Response>>) =
        let resolver = DepsResolver.create typeof<'Deps>
        let binder = ModelBinder.create typeof<'Input> verb
        fun table ->
            let h : Handler = fun req -> task {
                let deps = resolver req.Raw.RequestServices :?> 'Deps
                let queryDict =
                    let q = req.Raw.Request.Query
                    let d = Dictionary<string, string>(q.Count, StringComparer.OrdinalIgnoreCase)
                    for kvp in q do d.[kvp.Key] <- kvp.Value.ToString()
                    d :> IReadOnlyDictionary<_, _>
                let! bodyJson =
                    if binder.IsBodyMethod then
                        task {
                            use reader = new StreamReader(req.Raw.Request.Body, Encoding.UTF8, leaveOpen = true)
                            let! text = reader.ReadToEndAsync()
                            return Some text
                        }
                    else
                        task { return None }
                match ModelBinder.bind binder req.Params queryDict bodyJson with
                | Ok input ->
                    return! handler.Invoke(deps, input :?> 'Input)
                | Error errors ->
                    return Response.json {| errors = errors |} |> Response.status 400
            }
            Route.addRoute verb pattern h table

    static member private withDepsInputAndReq<'Deps, 'Input> (verb: string) (pattern: string) (handler: Func<'Deps, 'Input, Request, Task<Response>>) =
        let resolver = DepsResolver.create typeof<'Deps>
        let binder = ModelBinder.create typeof<'Input> verb
        fun table ->
            let h : Handler = fun req -> task {
                let deps = resolver req.Raw.RequestServices :?> 'Deps
                let queryDict =
                    let q = req.Raw.Request.Query
                    let d = Dictionary<string, string>(q.Count, StringComparer.OrdinalIgnoreCase)
                    for kvp in q do d.[kvp.Key] <- kvp.Value.ToString()
                    d :> IReadOnlyDictionary<_, _>
                let! bodyJson =
                    if binder.IsBodyMethod then
                        task {
                            use reader = new StreamReader(req.Raw.Request.Body, Encoding.UTF8, leaveOpen = true)
                            let! text = reader.ReadToEndAsync()
                            return Some text
                        }
                    else
                        task { return None }
                match ModelBinder.bind binder req.Params queryDict bodyJson with
                | Ok input ->
                    return! handler.Invoke(deps, input :?> 'Input, req)
                | Error errors ->
                    return Response.json {| errors = errors |} |> Response.status 400
            }
            Route.addRoute verb pattern h table

    // --- Plain handler: fun req -> task { ... } ---

    static member get(pattern: string, handler: Request -> Task<Response>) =
        fun table -> Route.addRoute "GET" pattern handler table

    static member post(pattern: string, handler: Request -> Task<Response>) =
        fun table -> Route.addRoute "POST" pattern handler table

    static member put(pattern: string, handler: Request -> Task<Response>) =
        fun table -> Route.addRoute "PUT" pattern handler table

    static member patch(pattern: string, handler: Request -> Task<Response>) =
        fun table -> Route.addRoute "PATCH" pattern handler table

    static member delete(pattern: string, handler: Request -> Task<Response>) =
        fun table -> Route.addRoute "DELETE" pattern handler table

    static member head(pattern: string, handler: Request -> Task<Response>) =
        fun table -> Route.addRoute "HEAD" pattern handler table

    static member options(pattern: string, handler: Request -> Task<Response>) =
        fun table -> Route.addRoute "OPTIONS" pattern handler table

    // --- Deps only: fun deps -> task { ... } ---

    static member get(pattern: string, handler: Func<'Deps, Task<Response>>) =
        Route.withDeps<'Deps> "GET" pattern handler

    static member post(pattern: string, handler: Func<'Deps, Task<Response>>) =
        Route.withDeps<'Deps> "POST" pattern handler

    static member put(pattern: string, handler: Func<'Deps, Task<Response>>) =
        Route.withDeps<'Deps> "PUT" pattern handler

    static member patch(pattern: string, handler: Func<'Deps, Task<Response>>) =
        Route.withDeps<'Deps> "PATCH" pattern handler

    static member delete(pattern: string, handler: Func<'Deps, Task<Response>>) =
        Route.withDeps<'Deps> "DELETE" pattern handler

    static member head(pattern: string, handler: Func<'Deps, Task<Response>>) =
        Route.withDeps<'Deps> "HEAD" pattern handler

    static member options(pattern: string, handler: Func<'Deps, Task<Response>>) =
        Route.withDeps<'Deps> "OPTIONS" pattern handler

    // --- Deps + input: fun deps input -> task { ... } ---

    static member get(pattern: string, handler: Func<'Deps, 'Input, Task<Response>>) =
        Route.withDepsAndInput<'Deps, 'Input> "GET" pattern handler

    static member post(pattern: string, handler: Func<'Deps, 'Input, Task<Response>>) =
        Route.withDepsAndInput<'Deps, 'Input> "POST" pattern handler

    static member put(pattern: string, handler: Func<'Deps, 'Input, Task<Response>>) =
        Route.withDepsAndInput<'Deps, 'Input> "PUT" pattern handler

    static member patch(pattern: string, handler: Func<'Deps, 'Input, Task<Response>>) =
        Route.withDepsAndInput<'Deps, 'Input> "PATCH" pattern handler

    static member delete(pattern: string, handler: Func<'Deps, 'Input, Task<Response>>) =
        Route.withDepsAndInput<'Deps, 'Input> "DELETE" pattern handler

    // --- Deps + input + Request: fun deps input req -> task { ... } ---

    static member get(pattern: string, handler: Func<'Deps, 'Input, Request, Task<Response>>) =
        Route.withDepsInputAndReq<'Deps, 'Input> "GET" pattern handler

    static member post(pattern: string, handler: Func<'Deps, 'Input, Request, Task<Response>>) =
        Route.withDepsInputAndReq<'Deps, 'Input> "POST" pattern handler

    static member put(pattern: string, handler: Func<'Deps, 'Input, Request, Task<Response>>) =
        Route.withDepsInputAndReq<'Deps, 'Input> "PUT" pattern handler

    static member patch(pattern: string, handler: Func<'Deps, 'Input, Request, Task<Response>>) =
        Route.withDepsInputAndReq<'Deps, 'Input> "PATCH" pattern handler

    static member delete(pattern: string, handler: Func<'Deps, 'Input, Request, Task<Response>>) =
        Route.withDepsInputAndReq<'Deps, 'Input> "DELETE" pattern handler

    // --- Group and middleware ---

    static member group(prefix: string, configure: RouteTable -> RouteTable) =
        fun (parent: RouteTable) ->
            let scoped = { Prefix = parent.Prefix + prefix; Middlewares = parent.Middlewares; Routes = [] }
            let result = configure scoped
            { parent with Routes = parent.Routes @ result.Routes }

    static member middleware(mw: Middleware) =
        fun (table: RouteTable) ->
            { table with Middlewares = table.Middlewares @ [mw] }

    // --- Custom method ---
    static member method'(verb: string, pattern: string, handler: Request -> Task<Response>) =
        fun table -> Route.addRoute verb pattern handler table
