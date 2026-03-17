module Fire.Tests.SchemaTests

open System.Buffers
open System.Text.Json
open Xunit
open FsUnit.Xunit
open Fire

// --- Schema definitions ---

let createTodoSchema = schema {
    let! title = Schema.required "title" Schema.string [ Schema.minLength 3; Schema.maxLength 100 ]
    let! completed = Schema.optional "completed" Schema.bool false []
    return {| Title = title; Completed = completed |}
}

let addressSchema = schema {
    let! street = Schema.required "street" Schema.string []
    let! city = Schema.required "city" Schema.string []
    let! zip = Schema.required "zip" Schema.string [ Schema.pattern @"^\d{5}$" ]
    return {| Street = street; City = city; Zip = zip |}
}

let createUserSchema = schema {
    let! name = Schema.required "name" Schema.string [ Schema.minLength 1 ]
    let! email = Schema.required "email" Schema.string [ Schema.email ]
    let! address = Schema.required "address" (Schema.nest addressSchema) []
    return {| Name = name; Email = email; Address = address |}
}

let taskSchema = schema {
    let! title = Schema.required "title" Schema.string []
    let! priority = Schema.optional "priority" Schema.string "medium" [ Schema.enum' ["low"; "medium"; "high"] ]
    return {| Title = title; Priority = priority |}
}

// --- Tests ---

[<Fact>]
let ``Schema parses valid JSON`` () =
    let json = """{"title":"Buy milk","completed":true}"""
    match Schema.parseString createTodoSchema json with
    | Ok todo ->
        todo.Title |> should equal "Buy milk"
        todo.Completed |> should equal true
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema uses default for optional fields`` () =
    let json = """{"title":"Buy milk"}"""
    match Schema.parseString createTodoSchema json with
    | Ok todo ->
        todo.Title |> should equal "Buy milk"
        todo.Completed |> should equal false
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema rejects missing required field`` () =
    let json = """{"completed":true}"""
    match Schema.parseString createTodoSchema json with
    | Error errs -> errs |> should contain "title is required"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema validates minLength`` () =
    let json = """{"title":"ab"}"""
    match Schema.parseString createTodoSchema json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("at least 3")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema validates maxLength`` () =
    let longTitle = System.String('a', 101)
    let json = $"""{{"title":"{longTitle}"}}"""
    match Schema.parseString createTodoSchema json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("at most 100")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema collects all errors`` () =
    let json = """{}"""
    match Schema.parseString createTodoSchema json with
    | Error errs -> errs |> List.length |> should be (greaterThanOrEqualTo 1)
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema validates pattern`` () =
    let json = """{"street":"Main St","city":"NY","zip":"abc"}"""
    match Schema.parseString addressSchema json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("pattern")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema parses nested objects`` () =
    let json = """{"name":"Alice","email":"alice@test.com","address":{"street":"Main","city":"NY","zip":"12345"}}"""
    match Schema.parseString createUserSchema json with
    | Ok user ->
        user.Name |> should equal "Alice"
        user.Address.Street |> should equal "Main"
        user.Address.Zip |> should equal "12345"
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema validates email`` () =
    let json = """{"name":"Alice","email":"notanemail","address":{"street":"Main","city":"NY","zip":"12345"}}"""
    match Schema.parseString createUserSchema json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("email")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema validates enum`` () =
    let json = """{"title":"test","priority":"urgent"}"""
    match Schema.parseString taskSchema json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("must be one of")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema enum with valid value`` () =
    let json = """{"title":"test","priority":"high"}"""
    match Schema.parseString taskSchema json with
    | Ok t -> t.Priority |> should equal "high"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema rejects invalid JSON`` () =
    match Schema.parseString createTodoSchema "not json{{{" with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("invalid JSON")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.toJsonSchema produces valid JSON`` () =
    let jsonSchema = Schema.toJsonSchema createTodoSchema
    let doc = JsonDocument.Parse(jsonSchema)
    doc.RootElement.GetProperty("type").GetString() |> should equal "object"
    let props = doc.RootElement.GetProperty("properties")
    props.TryGetProperty("title") |> fst |> should be True
    props.TryGetProperty("completed") |> fst |> should be True
    let req = doc.RootElement.GetProperty("required")
    req.GetArrayLength() |> should equal 1
    req.[0].GetString() |> should equal "title"

[<Fact>]
let ``Schema validates list items`` () =
    let listSchema = schema {
        let! tags = Schema.required "tags" (Schema.list Schema.string) []
        return {| Tags = tags |}
    }
    let json = """{"tags":["a","b","c"]}"""
    match Schema.parseString listSchema json with
    | Ok r -> r.Tags |> should equal ["a"; "b"; "c"]
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema parseStream works`` () = task {
    let json = """{"title":"test"}"""
    let stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))
    let! result = Schema.parseStream createTodoSchema stream
    match result with
    | Ok todo -> todo.Title |> should equal "test"
    | Error _ -> failwith "expected Ok"
}

// --- parseBuffer tests ---

[<Fact>]
let ``Schema parseBuffer parses valid JSON`` () =
    let json = """{"title":"Buy milk","completed":true}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createTodoSchema buffer with
    | Ok todo ->
        todo.Title |> should equal "Buy milk"
        todo.Completed |> should equal true
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema parseBuffer uses defaults for optional fields`` () =
    let json = """{"title":"Buy milk"}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createTodoSchema buffer with
    | Ok todo ->
        todo.Title |> should equal "Buy milk"
        todo.Completed |> should equal false
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema parseBuffer rejects missing required fields`` () =
    let json = """{"completed":true}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createTodoSchema buffer with
    | Error errs -> errs |> should contain "title is required"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema parseBuffer validates rules`` () =
    let json = """{"title":"ab"}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createTodoSchema buffer with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("at least 3")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema parseBuffer parses nested objects`` () =
    let json = """{"name":"Alice","email":"alice@test.com","address":{"street":"Main","city":"NY","zip":"12345"}}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createUserSchema buffer with
    | Ok user ->
        user.Name |> should equal "Alice"
        user.Address.Street |> should equal "Main"
        user.Address.Zip |> should equal "12345"
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema parseBuffer handles invalid JSON`` () =
    let json = "not json{{{"
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createTodoSchema buffer with
    | Error _ -> () // expected
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema parseBuffer parses list fields`` () =
    let listSchema = schema {
        let! tags = Schema.required "tags" (Schema.list Schema.string) []
        return {| Tags = tags |}
    }
    let json = """{"tags":["a","b","c"]}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer listSchema buffer with
    | Ok r -> r.Tags |> should equal ["a"; "b"; "c"]
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema parsePipe works`` () = task {
    let json = """{"title":"test via pipe"}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let pipe = System.IO.Pipelines.Pipe()
    let! _ = pipe.Writer.WriteAsync(System.ReadOnlyMemory<byte>(bytes))
    do! pipe.Writer.CompleteAsync()
    let! result = Schema.parsePipe createTodoSchema pipe.Reader
    match result with
    | Ok todo -> todo.Title |> should equal "test via pipe"
    | Error errs -> failwith $"expected Ok, got {errs}"
}
