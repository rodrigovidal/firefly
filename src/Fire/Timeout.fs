namespace Fire

open System
open System.Threading.Tasks

[<RequireQualifiedAccess>]
module Timeout =

    let after (timeout: TimeSpan) : Middleware =
        fun next req -> task {
            let handlerTask = next req
            let delayTask = Task.Delay(int timeout.TotalMilliseconds)
            let! completed = Task.WhenAny(handlerTask, delayTask)
            if Object.ReferenceEquals(completed, handlerTask) then
                return! handlerTask
            else
                return { Status = 504; Headers = []; Body = Empty }
        }
