namespace FireApp

open System.Threading
open Fire

module App =

    [<EntryPoint>]
    let main _ =
        Fire.App.run Router.routes Endpoint.config CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously
        0
