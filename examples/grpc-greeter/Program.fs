open System.Threading
open Firefly
open GrpcGreeter.App

let (routes, config) = create ()
App.run routes config CancellationToken.None |> fun t -> t.Wait()
