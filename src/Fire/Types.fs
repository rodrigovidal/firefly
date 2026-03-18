namespace Fire

open System
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection

type Handler = Request -> Task<Response>
type Middleware = Handler -> Handler

type Pipeline = {
    Name: string
    Middlewares: Middleware list
}

[<RequireQualifiedAccess>]
module Pipeline =
    let create (name: string) : Pipeline =
        { Name = name; Middlewares = [] }

    let plug (mw: Middleware) (pipeline: Pipeline) : Pipeline =
        { pipeline with Middlewares = pipeline.Middlewares @ [ mw ] }

    let empty : Pipeline =
        { Name = "empty"; Middlewares = [] }

type FireConfig = {
    Port: int
    Host: string
    OnError: (exn -> Request -> Task<Response>) option
    NotFound: (Request -> Task<Response>) option
    Middlewares: Middleware list
    ShutdownTimeout: TimeSpan option
    DependencyInjection: (IServiceCollection -> unit) option
}
