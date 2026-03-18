namespace Flame.Benchmarks

open BenchmarkDotNet.Attributes
open Flame
open FluentValidation

// --- Named types for STJ deserialization ---

type SimpleTodo = { Title: string; Completed: bool }

[<CLIMutable>] type ValidationInput = { title: string; email: string; age: int }

[<CLIMutable>] type NestedAddress = { street: string; city: string; zip: string }
[<CLIMutable>] type NestedUser = { name: string; address: NestedAddress }

[<CLIMutable>] type TransformInput = { name: string; email: string }

// --- 1. Simple parse ---

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
    member _.FlameSimple() : obj =
        Schema.parseString flameSchema json |> box

    [<Benchmark(Description = "System.Text.Json: deserialize", Baseline = true)>]
    member _.StjSimple() : obj =
        System.Text.Json.JsonSerializer.Deserialize<SimpleTodo>(json) |> box

    [<Benchmark(Description = "Flame: buffer parse (zero-alloc)")>]
    member _.FlameBuffer() : obj =
        let buffer = System.Buffers.ReadOnlySequence<byte>(bytes)
        Schema.parseBuffer flameSchema buffer |> box

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
    member _.FlameValidated() : obj =
        Schema.parseString flameSchema validJson |> box

    [<Benchmark(Description = "Flame: buffer parse + validate")>]
    member _.FlameBufferValidated() : obj =
        let buffer = System.Buffers.ReadOnlySequence<byte>(bytes)
        Schema.parseBuffer flameSchema buffer |> box

    [<Benchmark(Description = "STJ + manual validation", Baseline = true)>]
    member _.StjManual() : obj =
        let obj = System.Text.Json.JsonSerializer.Deserialize<ValidationInput>(validJson)
        let mutable valid = true
        if System.String.IsNullOrEmpty(obj.title) || obj.title.Length > 200 then valid <- false
        if not (obj.email.Contains("@")) then valid <- false
        if obj.age < 0 || obj.age > 150 then valid <- false
        obj |> box

// --- FluentValidation validator ---

type ValidationInputValidator() =
    inherit AbstractValidator<ValidationInput>()
    do
        base.RuleFor(fun x -> x.title).NotEmpty().MinimumLength(1).MaximumLength(200) |> ignore
        base.RuleFor(fun x -> x.email).NotEmpty().EmailAddress() |> ignore
        base.RuleFor(fun x -> x.age).InclusiveBetween(0, 150) |> ignore

// --- Realistic model: 10 fields, 5 nested ---

[<CLIMutable>]
type BillingAddress = {
    Street: string
    City: string
    State: string
    Zip: string
    Country: string
}

[<CLIMutable>]
type CreateOrder = {
    CustomerName: string
    Email: string
    Phone: string
    Amount: float
    Currency: string
    Billing: BillingAddress
}

type BillingAddressValidator() =
    inherit AbstractValidator<BillingAddress>()
    do
        base.RuleFor(fun x -> x.Street).NotEmpty().MaximumLength(200) |> ignore
        base.RuleFor(fun x -> x.City).NotEmpty().MaximumLength(100) |> ignore
        base.RuleFor(fun x -> x.State).NotEmpty().Length(2, 2) |> ignore
        base.RuleFor(fun x -> x.Zip).NotEmpty().Matches(@"^\d{5}$") |> ignore
        base.RuleFor(fun x -> x.Country).NotEmpty().MaximumLength(2) |> ignore

type CreateOrderValidator() =
    inherit AbstractValidator<CreateOrder>()
    do
        base.RuleFor(fun x -> x.CustomerName).NotEmpty().MinimumLength(1).MaximumLength(100) |> ignore
        base.RuleFor(fun x -> x.Email).NotEmpty().EmailAddress() |> ignore
        base.RuleFor(fun x -> x.Phone).NotEmpty().MaximumLength(20) |> ignore
        base.RuleFor(fun x -> x.Amount).GreaterThan(0.0).LessThanOrEqualTo(1000000.0) |> ignore
        base.RuleFor(fun x -> x.Currency).NotEmpty().Length(3, 3) |> ignore
        base.RuleFor(fun x -> x.Billing).SetValidator(BillingAddressValidator()) |> ignore

// --- Realistic benchmark: 10 fields + nested ---

[<MemoryDiagnoser>]
type RealisticBenchmark() =
    let json = """{"CustomerName":"Alice Johnson","Email":"alice@example.com","Phone":"+1-555-0123","Amount":299.99,"Currency":"USD","Billing":{"Street":"123 Main St","City":"Springfield","State":"IL","Zip":"62701","Country":"US"}}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)

    let billingSchema = schema {
        let! street  = Schema.required "Street" Schema.string [ Schema.minLength 1; Schema.maxLength 200 ]
        let! city    = Schema.required "City" Schema.string [ Schema.minLength 1; Schema.maxLength 100 ]
        let! state   = Schema.required "State" Schema.string [ Schema.minLength 2; Schema.maxLength 2 ]
        let! zip     = Schema.required "Zip" Schema.string [ Schema.pattern @"^\d{5}$" ]
        let! country = Schema.required "Country" Schema.string [ Schema.minLength 1; Schema.maxLength 2 ]
        return {| Street = street; City = city; State = state; Zip = zip; Country = country |}
    }

    let orderSchema = schema {
        let! customerName = Schema.required "CustomerName" Schema.string [ Schema.minLength 1; Schema.maxLength 100 ]
        let! email    = Schema.required "Email" Schema.string [ Schema.email ]
        let! phone    = Schema.required "Phone" Schema.string [ Schema.maxLength 20 ]
        let! amount   = Schema.required "Amount" Schema.float [ Schema.min 0.01; Schema.max 1000000.0 ]
        let! currency = Schema.required "Currency" Schema.string [ Schema.minLength 3; Schema.maxLength 3 ]
        let! billing  = Schema.required "Billing" (Schema.nest billingSchema) []
        return {| CustomerName = customerName; Email = email; Phone = phone; Amount = amount; Currency = currency; Billing = billing |}
    }

    let fluentValidator = CreateOrderValidator()

    [<Benchmark(Description = "Flame: parse + validate (string)")>]
    member _.FlameString() : obj =
        Schema.parseString orderSchema json |> box

    [<Benchmark(Description = "Flame: parse + validate (buffer)")>]
    member _.FlameBuffer() : obj =
        let buffer = System.Buffers.ReadOnlySequence<byte>(bytes)
        Schema.parseBuffer orderSchema buffer |> box

    [<Benchmark(Description = "FluentValidation: STJ + validate", Baseline = true)>]
    member _.FluentValidation() : obj =
        let order = System.Text.Json.JsonSerializer.Deserialize<CreateOrder>(json)
        let result = fluentValidator.Validate(order)
        box (order, result.IsValid)

    [<Benchmark(Description = "STJ only (no validation)")>]
    member _.StjOnly() : obj =
        System.Text.Json.JsonSerializer.Deserialize<CreateOrder>(json) |> box

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
    member _.FlameNested() : obj =
        Schema.parseString userSchema json |> box

    [<Benchmark(Description = "Flame: nested buffer")>]
    member _.FlameNestedBuffer() : obj =
        let buffer = System.Buffers.ReadOnlySequence<byte>(bytes)
        Schema.parseBuffer userSchema buffer |> box

    [<Benchmark(Description = "STJ: nested deserialize", Baseline = true)>]
    member _.StjNested() : obj =
        System.Text.Json.JsonSerializer.Deserialize<NestedUser>(json) |> box

// --- 4. Schema.fromType vs STJ ---

[<CLIMutable>]
type UserRecord = { Name: string; Email: string; Age: int }

[<MemoryDiagnoser>]
type FromTypeBenchmark() =
    let json = """{"Name":"Alice","Email":"alice@test.com","Age":30}"""
    let flameSchema = Schema.fromType<UserRecord>()

    [<Benchmark(Description = "Flame: fromType parse")>]
    member _.FlameFromType() : obj =
        Schema.parseString flameSchema json |> box

    [<Benchmark(Description = "STJ: deserialize", Baseline = true)>]
    member _.StjDeserialize() : obj =
        System.Text.Json.JsonSerializer.Deserialize<UserRecord>(json) |> box

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
    member _.FlameTransform() : obj =
        Schema.parseString flameSchema json |> box

    [<Benchmark(Description = "Flame: buffer + transform")>]
    member _.FlameBufferTransform() : obj =
        let buffer = System.Buffers.ReadOnlySequence<byte>(bytes)
        Schema.parseBuffer flameSchema buffer |> box

    [<Benchmark(Description = "STJ + manual transform", Baseline = true)>]
    member _.StjManualTransform() : obj =
        let obj = System.Text.Json.JsonSerializer.Deserialize<TransformInput>(json)
        {| Name = obj.name.Trim(); Email = obj.email.Trim().ToLowerInvariant() |} |> box

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
