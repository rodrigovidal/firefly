namespace Firefly

[<RequireQualifiedAccess>]
module MiddlewareHelpers =

    /// Apply the given middleware only when the predicate returns true for the request.
    /// When false, the request passes straight through to the next handler.
    let when' (predicate: Request -> bool) (mw: Middleware) : Middleware =
        fun next req ->
            if predicate req then mw next req
            else next req
