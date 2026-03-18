namespace Flame.Benchmarks

open BenchmarkDotNet.Attributes
open Flame

// --- 1. Simple parse ---

type SimpleTodo = { Title: string; Completed: bool }

[<MemoryDiagnoser>]
type SimpleParserBenchmark() =
    let json = """{"Title":"Buy milk","Completed":false}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)

    let flameSchema = schema {
        let! title = Schema.req "Title" Schema.string
        let! completed = Schema.req "Completed" Schema.bool
        return {| Title = title; Completed = completed |}
    }

    [<Benchmark(Description = "Flame: simple parse")>]
    member _.FlameSimple() =
        Schema.parseString flameSchema json

    [<Benchmark(Description = "System.Text.Json: deserialize", Baseline = true)>]
    member _.StjSimple() =
        System.Text.Json.JsonSerializer.Deserialize<SimpleTodo>(json)

    [<Benchmark(Description = "Flame: buffer parse (zero-alloc)")>]
    member _.FlameBuffer() =
        let buffer = System.Buffers.ReadOnlySequence<byte>(bytes)
        Schema.parseBuffer flameSchema buffer

// --- 2. Validation ---

[<MemoryDiagnoser>]
type ValidationBenchmark() =
    let validJson = """{"title":"Buy milk","email":"alice@test.com","age":30}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(validJson)

    let flameSchema = schema {
        let! title = Schema.required "title" Schema.string [ Schema.minLength 1; Schema.maxLength 200 ]
        let! email = Schema.required "email" Schema.string [ Schema.email ]
        let! age = Schema.required "age" Schema.int [ Schema.min 0.0; Schema.max 150.0 ]
        return {| Title = title; Email = email; Age = age |}
    }

    [<Benchmark(Description = "Flame: parse + validate")>]
    member _.FlameValidated() =
        Schema.parseString flameSchema validJson

    [<Benchmark(Description = "Flame: buffer parse + validate")>]
    member _.FlameBufferValidated() =
        let buffer = System.Buffers.ReadOnlySequence<byte>(bytes)
        Schema.parseBuffer flameSchema buffer

    [<Benchmark(Description = "STJ + manual validation", Baseline = true)>]
    member _.StjManual() =
        let obj = System.Text.Json.JsonSerializer.Deserialize<{| title: string; email: string; age: int |}>(validJson)
        let mutable valid = true
        if System.String.IsNullOrEmpty(obj.title) || obj.title.Length > 200 then valid <- false
        if not (obj.email.Contains("@")) then valid <- false
        if obj.age < 0 || obj.age > 150 then valid <- false
        obj

// --- 3. Nested objects ---

[<MemoryDiagnoser>]
type NestedBenchmark() =
    let json = """{"name":"Alice","address":{"street":"123 Main","city":"NY","zip":"10001"}}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)

    let addressSchema = schema {
        let! street = Schema.req "street" Schema.string
        let! city = Schema.req "city" Schema.string
        let! zip = Schema.req "zip" Schema.string
        return {| Street = street; City = city; Zip = zip |}
    }
    let userSchema = schema {
        let! name = Schema.req "name" Schema.string
        let! address = Schema.required "address" (Schema.nest addressSchema) []
        return {| Name = name; Address = address |}
    }

    [<Benchmark(Description = "Flame: nested parse")>]
    member _.FlameNested() =
        Schema.parseString userSchema json

    [<Benchmark(Description = "Flame: nested buffer")>]
    member _.FlameNestedBuffer() =
        let buffer = System.Buffers.ReadOnlySequence<byte>(bytes)
        Schema.parseBuffer userSchema buffer

    [<Benchmark(Description = "STJ: nested deserialize", Baseline = true)>]
    member _.StjNested() =
        System.Text.Json.JsonSerializer.Deserialize<{| name: string; address: {| street: string; city: string; zip: string |} |}>(json)

// --- 4. Schema.fromType vs STJ ---

[<CLIMutable>]
type UserRecord = { Name: string; Email: string; Age: int }

[<MemoryDiagnoser>]
type FromTypeBenchmark() =
    let json = """{"Name":"Alice","Email":"alice@test.com","Age":30}"""
    let flameSchema = Schema.fromType<UserRecord>()

    [<Benchmark(Description = "Flame: fromType parse")>]
    member _.FlameFromType() =
        Schema.parseString flameSchema json

    [<Benchmark(Description = "STJ: deserialize", Baseline = true)>]
    member _.StjDeserialize() =
        System.Text.Json.JsonSerializer.Deserialize<UserRecord>(json)

// --- 5. Transform benchmark ---

[<MemoryDiagnoser>]
type TransformBenchmark() =
    let json = """{"name":"  ALICE  ","email":"  ALICE@TEST.COM  "}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)

    let flameSchema = schema {
        let! name = Schema.required "name" Schema.string [ Schema.trim ]
        let! email = Schema.required "email" Schema.string [ Schema.trim; Schema.lowercase ]
        return {| Name = name; Email = email |}
    }

    [<Benchmark(Description = "Flame: parse + transform")>]
    member _.FlameTransform() =
        Schema.parseString flameSchema json

    [<Benchmark(Description = "Flame: buffer + transform")>]
    member _.FlameBufferTransform() =
        let buffer = System.Buffers.ReadOnlySequence<byte>(bytes)
        Schema.parseBuffer flameSchema buffer

    [<Benchmark(Description = "STJ + manual transform", Baseline = true)>]
    member _.StjManualTransform() =
        let obj = System.Text.Json.JsonSerializer.Deserialize<{| name: string; email: string |}>(json)
        {| Name = obj.name.Trim(); Email = obj.email.Trim().ToLowerInvariant() |}

// --- Entry point ---

module Program =

    [<EntryPoint>]
    let main args =
        BenchmarkDotNet.Running.BenchmarkSwitcher
            .FromAssembly(System.Reflection.Assembly.GetExecutingAssembly())
            .RunAllJoined(BenchmarkDotNet.Configs.ManualConfig
                .CreateEmpty()
                .AddJob(BenchmarkDotNet.Jobs.Job.ShortRun)
                .AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance)
                .AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default)
                .AddExporter(BenchmarkDotNet.Exporters.MarkdownExporter.GitHub)
                .WithOption(BenchmarkDotNet.Configs.ConfigOptions.DisableLogFile, true), args)
        |> ignore
        0
