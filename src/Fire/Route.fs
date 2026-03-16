namespace Fire

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

    let get pattern handler table = addRoute "GET" pattern handler table
    let post pattern handler table = addRoute "POST" pattern handler table
    let put pattern handler table = addRoute "PUT" pattern handler table
    let patch pattern handler table = addRoute "PATCH" pattern handler table
    let delete pattern handler table = addRoute "DELETE" pattern handler table
    let head pattern handler table = addRoute "HEAD" pattern handler table
    let options pattern handler table = addRoute "OPTIONS" pattern handler table
    let method verb pattern handler table = addRoute verb pattern handler table

    let group (prefix: string) (configure: RouteTable -> RouteTable) (parent: RouteTable) =
        let scoped = { Prefix = parent.Prefix + prefix; Middlewares = parent.Middlewares; Routes = [] }
        let result = configure scoped
        { parent with Routes = parent.Routes @ result.Routes }

    let middleware (mw: Middleware) (table: RouteTable) =
        { table with Middlewares = table.Middlewares @ [mw] }
