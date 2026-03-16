namespace Fire

open System
open System.Diagnostics
open Microsoft.Extensions.Logging

type LogEntry = {
    Method: string
    Path: string
    Status: int
    Duration: TimeSpan
}

[<RequireQualifiedAccess>]
module Log =

    let withOutput (output: LogEntry -> unit) : Middleware =
        fun next req -> task {
            let sw = Stopwatch.StartNew()
            let! response = next req
            sw.Stop()
            output {
                Method = req.Method
                Path = req.Path
                Status = response.Status
                Duration = sw.Elapsed
            }
            return response
        }

    let toConsole : Middleware =
        withOutput (fun e ->
            Console.WriteLine($"{e.Method} {e.Path} -> {e.Status} ({e.Duration.TotalMilliseconds:F1}ms)"))

    let toLogger (logger: Microsoft.Extensions.Logging.ILogger) : Middleware =
        withOutput (fun e ->
            logger.LogInformation(
                "{Method} {Path} -> {Status} ({Duration:F1}ms)",
                e.Method, e.Path, e.Status, e.Duration.TotalMilliseconds))
