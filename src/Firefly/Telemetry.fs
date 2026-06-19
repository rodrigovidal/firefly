namespace Firefly

open System.Collections.Generic
open System.Diagnostics
open System.Diagnostics.Metrics

[<RequireQualifiedAccess>]
module Telemetry =

    let private source = new ActivitySource("Fire")
    let private meter = new Meter("Fire")
    let private requestCount = meter.CreateCounter<int64>("fire.http.requests", "requests", "Total HTTP requests")
    let private requestDuration = meter.CreateHistogram<float>("fire.http.duration", "ms", "HTTP request duration")
    let private activeRequests = meter.CreateUpDownCounter<int64>("fire.http.active_requests", "requests", "Active HTTP requests")

    let inline private tag (k: string) (v: obj) = KeyValuePair<string, obj>(k, v)

    /// Middleware that creates an OpenTelemetry span per request and records metrics.
    let middleware : Middleware =
        fun next req -> task {
            let method = req.Raw.Request.Method
            let path = req.Raw.Request.Path.Value
            use activity = source.StartActivity($"{method} {path}", ActivityKind.Server)
            if activity <> null then
                activity.SetTag("http.request.method", method) |> ignore
                activity.SetTag("url.path", path) |> ignore
                activity.SetTag("url.scheme", req.Raw.Request.Scheme) |> ignore
                match req.Header "X-Request-Id" with
                | Some id -> activity.SetTag("http.request_id", id) |> ignore
                | None -> ()

            activeRequests.Add(1L)
            let sw = Stopwatch.StartNew()
            try
                let! response = next req
                sw.Stop()

                if activity <> null then
                    activity.SetTag("http.response.status_code", response.Status) |> ignore
                    if response.Status >= 400 then
                        activity.SetStatus(ActivityStatusCode.Error) |> ignore

                requestCount.Add(1L, tag "method" method, tag "status" (box response.Status), tag "path" path)
                requestDuration.Record(float sw.ElapsedMilliseconds, tag "method" method, tag "path" path)
                activeRequests.Add(-1L)
                return response
            with
            | :? System.OperationCanceledException ->
                sw.Stop()
                activeRequests.Add(-1L)
                return { Status = 200; Headers = []; Body = Empty }
            | ex ->
                sw.Stop()
                activeRequests.Add(-1L)
                if activity <> null then
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message) |> ignore
                    activity.SetTag("error.type", ex.GetType().Name) |> ignore

                requestCount.Add(1L, tag "method" method, tag "status" (box 500), tag "path" path)
                requestDuration.Record(float sw.ElapsedMilliseconds, tag "method" method, tag "path" path)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw()
                return Unchecked.defaultof<_>
        }

    /// The ActivitySource name for configuring OpenTelemetry exporters.
    let sourceName = "Fire"

    /// The Meter name for configuring OpenTelemetry metrics exporters.
    let meterName = "Fire"
