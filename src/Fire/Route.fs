namespace Fire

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

[<RequireQualifiedAccess>]
module Route =

    let start = { Prefix = ""; Middlewares = []; Routes = [] }

    let private addRoute (verb: string) (pattern: string) (handler: Handler) (table: RouteTable) =
        let entry = {
            Method = verb
            Pattern = table.Prefix + pattern
            Middlewares = table.Middlewares
            Handler = handler
        }
        { table with Routes = table.Routes @ [entry] }

    let get (pattern: string) (handler: 'H) (table: RouteTable) : RouteTable =
        let triePattern, wrappedHandler = HandlerFactory.create "GET" pattern (box handler)
        addRoute "GET" triePattern wrappedHandler table

    let post (pattern: string) (handler: 'H) (table: RouteTable) : RouteTable =
        let triePattern, wrappedHandler = HandlerFactory.create "POST" pattern (box handler)
        addRoute "POST" triePattern wrappedHandler table

    let put (pattern: string) (handler: 'H) (table: RouteTable) : RouteTable =
        let triePattern, wrappedHandler = HandlerFactory.create "PUT" pattern (box handler)
        addRoute "PUT" triePattern wrappedHandler table

    let patch (pattern: string) (handler: 'H) (table: RouteTable) : RouteTable =
        let triePattern, wrappedHandler = HandlerFactory.create "PATCH" pattern (box handler)
        addRoute "PATCH" triePattern wrappedHandler table

    let delete (pattern: string) (handler: 'H) (table: RouteTable) : RouteTable =
        let triePattern, wrappedHandler = HandlerFactory.create "DELETE" pattern (box handler)
        addRoute "DELETE" triePattern wrappedHandler table

    let head (pattern: string) (handler: 'H) (table: RouteTable) : RouteTable =
        let triePattern, wrappedHandler = HandlerFactory.create "HEAD" pattern (box handler)
        addRoute "HEAD" triePattern wrappedHandler table

    let options (pattern: string) (handler: 'H) (table: RouteTable) : RouteTable =
        let triePattern, wrappedHandler = HandlerFactory.create "OPTIONS" pattern (box handler)
        addRoute "OPTIONS" triePattern wrappedHandler table

    let group (prefix: string) (configure: RouteTable -> RouteTable) (parent: RouteTable) =
        let scoped = { Prefix = parent.Prefix + prefix; Middlewares = parent.Middlewares; Routes = [] }
        let result = configure scoped
        { parent with Routes = parent.Routes @ result.Routes }

    let middleware (mw: Middleware) (table: RouteTable) =
        { table with Middlewares = table.Middlewares @ [mw] }

    /// Register a route with a custom HTTP method and a plain Handler
    let method' (verb: string) (pattern: string) (handler: Handler) (table: RouteTable) =
        addRoute verb pattern handler table
