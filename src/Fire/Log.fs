namespace Fire

open System
open System.Diagnostics
open System.Text.Json
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

    /// Like structuredWith, but writes to a custom output function instead of stdout.
    let structuredWith (output: string -> unit) : Middleware =
        fun next req -> task {
            let sw = Stopwatch.StartNew()
            let! response = next req
            sw.Stop()
            let requestId = req.Header "X-Request-Id" |> Option.defaultValue "-"
            let log = JsonSerializer.Serialize({|
                timestamp = DateTime.UtcNow.ToString("o")
                method = req.Method
                path = req.Path
                status = response.Status
                duration_ms = sw.ElapsedMilliseconds
                request_id = requestId
                content_length = req.Raw.Request.ContentLength |> Option.ofNullable |> Option.defaultValue 0L
            |})
            output log
            return response
        }

    /// Structured JSON request logging. Outputs one JSON line per request to stdout.
    /// Includes: method, path, status, duration_ms, request_id, content_length
    let structured : Middleware =
        structuredWith Console.WriteLine
