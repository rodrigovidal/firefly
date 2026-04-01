module GrpcGreeter.App

open System.Threading.Tasks
open Grpc.Core
open Greet
open Fire

let greeter = grpcService "greet.Greeter" {
    unary "SayHello" (fun (req: HelloRequest) (_ctx: ServerCallContext) -> task {
        return HelloReply(Message = $"Hello, {req.Name}!")
    })
    serverStream "SayHelloStream" (fun (req: HelloRequest) (writer: IServerStreamWriter<HelloReply>) (_ctx: ServerCallContext) -> task {
        for i in 1..5 do
            do! writer.WriteAsync(HelloReply(Message = $"Hello #{i}, {req.Name}!"))
            do! Task.Delay(200)
    })
}

let routes =
    Route.start
    |> Route.get "/health" (fun _ -> task { return Response.text "ok" })

let create () =
    let config =
        App.defaults
        |> App.port 5000
        |> App.grpc greeter
    (routes, config)
