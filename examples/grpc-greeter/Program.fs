open System.Threading
open Fire
open GrpcGreeter.App

let (routes, config) = create ()
App.run routes config CancellationToken.None |> fun t -> t.Wait()
