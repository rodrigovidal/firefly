namespace Fire

open System.Threading.Tasks

type Handler = Request -> Task<Response>
type Middleware = Handler -> Handler
