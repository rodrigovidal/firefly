module Firefly.Tests.EnvTests

open System
open Xunit
open FsUnit.Xunit
open Firefly

type SimpleConfig = { DatabaseUrl: string; Port: int; Debug: bool }
type OptionalConfig = { Host: string; ApiKey: string option; Timeout: int option }
type CoercionConfig = { Enabled: bool; Rate: float }

[<Fact>]
let ``Env.load reads env vars with screaming snake case`` () =
    Environment.SetEnvironmentVariable("DATABASE_URL", "postgres://localhost/test")
    Environment.SetEnvironmentVariable("PORT", "5432")
    Environment.SetEnvironmentVariable("DEBUG", "true")
    try
        let config = Env.load<SimpleConfig>()
        config.DatabaseUrl |> should equal "postgres://localhost/test"
        config.Port |> should equal 5432
        config.Debug |> should equal true
    finally
        Environment.SetEnvironmentVariable("DATABASE_URL", null)
        Environment.SetEnvironmentVariable("PORT", null)
        Environment.SetEnvironmentVariable("DEBUG", null)

[<Fact>]
let ``Env.load handles optional fields`` () =
    Environment.SetEnvironmentVariable("HOST", "localhost")
    Environment.SetEnvironmentVariable("API_KEY", null)
    Environment.SetEnvironmentVariable("TIMEOUT", null)
    try
        let config = Env.load<OptionalConfig>()
        config.Host |> should equal "localhost"
        config.ApiKey |> should equal None
        config.Timeout |> should equal None
    finally
        Environment.SetEnvironmentVariable("HOST", null)

[<Fact>]
let ``Env.load optional Some when present`` () =
    Environment.SetEnvironmentVariable("HOST", "localhost")
    Environment.SetEnvironmentVariable("API_KEY", "secret123")
    Environment.SetEnvironmentVariable("TIMEOUT", "30")
    try
        let config = Env.load<OptionalConfig>()
        config.ApiKey |> should equal (Some "secret123")
        config.Timeout |> should equal (Some 30)
    finally
        Environment.SetEnvironmentVariable("HOST", null)
        Environment.SetEnvironmentVariable("API_KEY", null)
        Environment.SetEnvironmentVariable("TIMEOUT", null)

[<Fact>]
let ``Env.load fails with all missing vars`` () =
    Environment.SetEnvironmentVariable("DATABASE_URL", null)
    Environment.SetEnvironmentVariable("PORT", null)
    Environment.SetEnvironmentVariable("DEBUG", null)
    let ex = Assert.Throws<Exception>(fun () -> Env.load<SimpleConfig>() |> ignore)
    ex.Message |> should haveSubstring "DATABASE_URL"
    ex.Message |> should haveSubstring "PORT"
    ex.Message |> should haveSubstring "DEBUG"

[<Fact>]
let ``Env.load bool coercion`` () =
    Environment.SetEnvironmentVariable("ENABLED", "yes")
    Environment.SetEnvironmentVariable("RATE", "3.14")
    try
        let config = Env.load<CoercionConfig>()
        config.Enabled |> should equal true
        config.Rate |> should equal 3.14
    finally
        Environment.SetEnvironmentVariable("ENABLED", null)
        Environment.SetEnvironmentVariable("RATE", null)

[<Fact>]
let ``Env.load bool accepts 1 and 0`` () =
    Environment.SetEnvironmentVariable("ENABLED", "0")
    Environment.SetEnvironmentVariable("RATE", "1.0")
    try
        let config = Env.load<CoercionConfig>()
        config.Enabled |> should equal false
    finally
        Environment.SetEnvironmentVariable("ENABLED", null)
        Environment.SetEnvironmentVariable("RATE", null)

[<Fact>]
let ``Env.load fails with parse error`` () =
    Environment.SetEnvironmentVariable("DATABASE_URL", "ok")
    Environment.SetEnvironmentVariable("PORT", "not-a-number")
    Environment.SetEnvironmentVariable("DEBUG", "true")
    try
        let ex = Assert.Throws<Exception>(fun () -> Env.load<SimpleConfig>() |> ignore)
        ex.Message |> should haveSubstring "PORT"
        ex.Message |> should haveSubstring "expected integer"
    finally
        Environment.SetEnvironmentVariable("DATABASE_URL", null)
        Environment.SetEnvironmentVariable("PORT", null)
        Environment.SetEnvironmentVariable("DEBUG", null)

[<Fact>]
let ``parseEnvLines skips comments and blanks, strips quotes, splits on first =`` () =
    let entries =
        Env.parseEnvLines [ "# a comment"; "   "; "FOO=bar"; "QUOTED=\"hello world\""
                            "SINGLE='x'"; "CONN=Host=localhost;Port=5432" ]
    entries
    |> should equal [ ("FOO", "bar"); ("QUOTED", "hello world"); ("SINGLE", "x"); ("CONN", "Host=localhost;Port=5432") ]

[<Fact>]
let ``mergeLayers gives precedence to the high layer`` () =
    let high = [ ("PORT", "5005"); ("ONLY_DEV", "1") ]
    let low = [ ("PORT", "8080"); ("ONLY_BASE", "2") ]
    let merged = Env.mergeLayers high low |> Map.ofList
    merged.["PORT"] |> should equal "5005" // env-specific overrides base
    merged.["ONLY_DEV"] |> should equal "1"
    merged.["ONLY_BASE"] |> should equal "2"

[<Fact>]
let ``toScreamingSnake converts PascalCase correctly`` () =
    // Test via round-trip: set env var, load config
    Environment.SetEnvironmentVariable("DATABASE_URL", "test")
    Environment.SetEnvironmentVariable("PORT", "1")
    Environment.SetEnvironmentVariable("DEBUG", "false")
    try
        let config = Env.load<SimpleConfig>()
        config.DatabaseUrl |> should equal "test"
    finally
        Environment.SetEnvironmentVariable("DATABASE_URL", null)
        Environment.SetEnvironmentVariable("PORT", null)
        Environment.SetEnvironmentVariable("DEBUG", null)
