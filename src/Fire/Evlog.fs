namespace Fire

open Evlog

[<AutoOpen>]
module EvlogExtensions =

    type Request with
        /// Get the Evlog request logger for the current request.
        /// Requires App.configure (fun app -> app.UseEvlog() |> ignore)
        /// and App.services [ Service.raw (fun s -> s.AddEvlog(...) |> ignore) ].
        member this.Evlog : RequestLogger =
            this.Raw.GetEvlogLogger()
