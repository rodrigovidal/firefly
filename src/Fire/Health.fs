namespace Fire

open System
open System.Threading.Tasks

[<RequireQualifiedAccess>]
module Health =

    type CheckResult = { Name: string; Status: string; Duration: TimeSpan; Error: string option }

    type HealthResult = { Status: string; Checks: CheckResult list; TotalDuration: TimeSpan }

    /// A named health check function
    type Check = string * (unit -> Task<Result<unit, string>>)

    /// Creates a /health handler with customizable checks
    let handler (checks: Check list) : Handler =
        fun _req -> task {
            let sw = System.Diagnostics.Stopwatch.StartNew()
            let! results =
                checks
                |> List.map (fun (name, check) -> task {
                    let csw = System.Diagnostics.Stopwatch.StartNew()
                    try
                        let! result = check ()
                        csw.Stop()
                        match result with
                        | Ok () ->
                            return { Name = name; Status = "healthy"; Duration = csw.Elapsed; Error = None }
                        | Error msg ->
                            return { Name = name; Status = "unhealthy"; Duration = csw.Elapsed; Error = Some msg }
                    with ex ->
                        csw.Stop()
                        return { Name = name; Status = "unhealthy"; Duration = csw.Elapsed; Error = Some ex.Message }
                })
                |> Task.WhenAll
            sw.Stop()

            let resultList = results |> Array.toList
            let overallStatus =
                if resultList |> List.forall (fun r -> r.Status = "healthy") then "healthy"
                else "unhealthy"

            let healthResult = { Status = overallStatus; Checks = resultList; TotalDuration = sw.Elapsed }

            let statusCode = if overallStatus = "healthy" then 200 else 503
            return Response.json healthResult |> Response.status statusCode
        }

    /// Simple health check that always returns healthy
    let ping : Check = ("ping", fun () -> task { return Ok () })

    /// Health check that verifies a function doesn't throw
    let check (name: string) (fn: unit -> Task<unit>) : Check =
        (name, fun () -> task {
            try
                do! fn ()
                return Ok ()
            with ex ->
                return Error ex.Message
        })
