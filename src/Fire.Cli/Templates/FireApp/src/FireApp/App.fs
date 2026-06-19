namespace FireApp

open System.Threading
open Firefly

module App =

    [<EntryPoint>]
    let main _ =
        Firefly.App.run Router.routes Endpoint.config CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously
        0
