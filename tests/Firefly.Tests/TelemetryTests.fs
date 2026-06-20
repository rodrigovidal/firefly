module Firefly.Tests.TelemetryTests

open Xunit
open FsUnit.Xunit
open Firefly
open Microsoft.Extensions.DependencyInjection
open OpenTelemetry.Trace
open OpenTelemetry.Metrics

[<Fact>]
let ``Telemetry source and meter names are Firefly`` () =
    Telemetry.sourceName |> should equal "Firefly"
    Telemetry.meterName |> should equal "Firefly"

[<Fact>]
let ``Telemetry.otlp returns a raw service registration`` () =
    match Telemetry.otlp "svc" with
    | RawConfigure _ -> ()
    | other -> failwithf "expected RawConfigure, got %A" other

[<Fact>]
let ``Telemetry.otlp registers tracer and meter providers`` () =
    let services = ServiceCollection()
    match Telemetry.otlp "test-service" with
    | RawConfigure configure -> configure services
    | _ -> failwith "expected RawConfigure"
    use provider = services.BuildServiceProvider()
    provider.GetService<TracerProvider>() |> should not' (be Null)
    provider.GetService<MeterProvider>() |> should not' (be Null)
