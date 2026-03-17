namespace Fire

open System
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection

type Handler = Request -> Task<Response>
type Middleware = Handler -> Handler

type FireConfig = {
    Port: int
    Host: string
    OnError: (exn -> Request -> Task<Response>) option
    NotFound: (Request -> Task<Response>) option
    Middlewares: Middleware list
    ShutdownTimeout: TimeSpan option
    DependencyInjection: (IServiceCollection -> unit) option
}
