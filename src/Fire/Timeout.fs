namespace Firefly

open System
open System.Threading
open System.Threading.Tasks

[<RequireQualifiedAccess>]
module Timeout =

    let after (timeout: TimeSpan) : Middleware =
        fun next req -> task {
            use cts = new CancellationTokenSource()
            cts.CancelAfter(timeout)
            let handlerTask = next req
            let delayTask = Task.Delay(Timeout.Infinite, cts.Token) |> fun t -> t.ContinueWith(fun _ -> ())
            let! completed = Task.WhenAny(handlerTask, Task.Delay(int timeout.TotalMilliseconds))
            if Object.ReferenceEquals(completed, handlerTask) then
                return! handlerTask
            else
                cts.Cancel()
                return { Status = 504; Headers = []; Body = Empty }
        }
