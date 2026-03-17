module Fire.Tests.SchemaTests

open System
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

// --- Additional buffer parser edge cases ---

[<Fact>]
let ``Schema.parseBuffer handles missing optional with default`` () =
    let json = """{"title":"test"}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createTodoSchema buffer with
    | Ok todo ->
        todo.Title |> should equal "test"
        todo.Completed |> should equal false
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.parseBuffer rejects empty object with required fields`` () =
    let json = """{}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createTodoSchema buffer with
    | Error errs -> errs |> should contain "title is required"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.parseBuffer validates rules on buffer path`` () =
    let json = """{"title":"ab"}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createTodoSchema buffer with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("at least 3")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.parseBuffer handles nested schema via buffer`` () =
    let json = """{"name":"Alice","email":"alice@test.com","address":{"street":"Main","city":"NY","zip":"12345"}}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createUserSchema buffer with
    | Ok user ->
        user.Name |> should equal "Alice"
        user.Address.Zip |> should equal "12345"
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema.parseBuffer skips unknown properties`` () =
    let json = """{"title":"test","unknown_field":"ignored","completed":true}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createTodoSchema buffer with
    | Ok todo -> todo.Title |> should equal "test"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.parseBuffer handles invalid JSON gracefully`` () =
    let json = """not json{{{"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createTodoSchema buffer with
    | Error _ -> () // expected
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.nullable parses null value via JsonElement`` () =
    let nullableSchema = schema {
        let! title = Schema.required "title" Schema.string []
        let! note = Schema.optional "note" (Schema.nullable Schema.string) None []
        return {| Title = title; Note = note |}
    }
    let json = """{"title":"test","note":null}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson nullableSchema doc.RootElement with
    | Ok r ->
        r.Title |> should equal "test"
        r.Note |> should equal None
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema.nullable parses present value via JsonElement`` () =
    let nullableSchema = schema {
        let! title = Schema.required "title" Schema.string []
        let! note = Schema.optional "note" (Schema.nullable Schema.string) None []
        return {| Title = title; Note = note |}
    }
    let json = """{"title":"test","note":"hello"}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson nullableSchema doc.RootElement with
    | Ok r ->
        r.Title |> should equal "test"
        r.Note |> should equal (Some "hello")
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema parsePipe rejects invalid JSON`` () = task {
    let json = """not valid json"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let pipe = System.IO.Pipelines.Pipe()
    let! _ = pipe.Writer.WriteAsync(System.ReadOnlyMemory<byte>(bytes))
    do! pipe.Writer.CompleteAsync()
    let! result = Schema.parsePipe createTodoSchema pipe.Reader
    match result with
    | Error _ -> () // expected
    | Ok _ -> failwith "expected Error"
}

// --- Coverage: Schema.min and Schema.max rules ---

[<Fact>]
let ``Schema.min rule rejects value below minimum`` () =
    let s = schema {
        let! age = Schema.required "age" Schema.int [ Schema.min 18.0 ]
        return {| Age = age |}
    }
    let json = """{"age":10}"""
    match Schema.parseString s json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("at least 18")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.min rule allows value at minimum`` () =
    let s = schema {
        let! age = Schema.required "age" Schema.int [ Schema.min 18.0 ]
        return {| Age = age |}
    }
    let json = """{"age":18}"""
    match Schema.parseString s json with
    | Ok r -> r.Age |> should equal 18
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.max rule rejects value above maximum`` () =
    let s = schema {
        let! score = Schema.required "score" Schema.float [ Schema.max 100.0 ]
        return {| Score = score |}
    }
    let json = """{"score":150.5}"""
    match Schema.parseString s json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("at most 100")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.max rule allows value at maximum`` () =
    let s = schema {
        let! score = Schema.required "score" Schema.float [ Schema.max 100.0 ]
        return {| Score = score |}
    }
    let json = """{"score":100.0}"""
    match Schema.parseString s json with
    | Ok r -> r.Score |> should (equalWithin 0.01) 100.0
    | Error _ -> failwith "expected Ok"

// --- Coverage: Schema.url rule ---

[<Fact>]
let ``Schema.url rule rejects invalid URL`` () =
    let s = schema {
        let! link = Schema.required "link" Schema.string [ Schema.url ]
        return {| Link = link |}
    }
    let json = """{"link":"not-a-url"}"""
    match Schema.parseString s json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("URL")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.url rule accepts valid http URL`` () =
    let s = schema {
        let! link = Schema.required "link" Schema.string [ Schema.url ]
        return {| Link = link |}
    }
    let json = """{"link":"https://example.com"}"""
    match Schema.parseString s json with
    | Ok r -> r.Link |> should equal "https://example.com"
    | Error _ -> failwith "expected Ok"

// --- Coverage: Schema.int and Schema.float type parsers with wrong type ---

[<Fact>]
let ``Schema.int rejects non-integer JSON`` () =
    let s = schema {
        let! age = Schema.required "age" Schema.int []
        return {| Age = age |}
    }
    let json = """{"age":"not a number"}"""
    match Schema.parseString s json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("age")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.float rejects non-number JSON`` () =
    let s = schema {
        let! score = Schema.required "score" Schema.float []
        return {| Score = score |}
    }
    let json = """{"score":"not a number"}"""
    match Schema.parseString s json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("score")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.bool rejects non-boolean JSON`` () =
    let s = schema {
        let! flag = Schema.required "flag" Schema.bool []
        return {| Flag = flag |}
    }
    let json = """{"flag":"not a bool"}"""
    match Schema.parseString s json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("flag")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: Schema.list with invalid items (via JsonElement path) ---

[<Fact>]
let ``Schema.list rejects array with invalid items via JsonElement`` () =
    let s = schema {
        let! numbers = Schema.required "numbers" (Schema.list Schema.int) []
        return {| Numbers = numbers |}
    }
    let json = """{"numbers":[1, "two", 3]}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("[1]")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.list rejects non-array via JsonElement`` () =
    let s = schema {
        let! numbers = Schema.required "numbers" (Schema.list Schema.int) []
        return {| Numbers = numbers |}
    }
    let json = """{"numbers":"not an array"}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("array")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: Schema.nest parser function (line 343-346) ---

[<Fact>]
let ``Schema.nest returns error on invalid nested object via JsonElement`` () =
    let json = """{"name":"Alice","email":"alice@test.com","address":{"street":"Main","city":"NY","zip":"bad"}}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson createUserSchema doc.RootElement with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("pattern")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: Schema.parseStream with invalid JSON (lines 467-468) ---

[<Fact>]
let ``Schema.parseStream rejects invalid JSON`` () = task {
    let stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("not json{{{"))
    let! result = Schema.parseStream createTodoSchema stream
    match result with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("invalid JSON")) |> should be True
    | Ok _ -> failwith "expected Error"
}

// --- Coverage: Schema.validated (lines 475-486) ---

[<Fact>]
let ``Schema.validated accepts valid body`` () = task {
    let handler = Schema.validated createTodoSchema (fun todo -> task {
        return Response.text todo.Title
    })
    let routes = Route.start |> Route.post "/todos" handler
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/todos" """{"title":"Buy milk"}"""
    r.Status |> should equal 200
    r.Body |> should equal "Buy milk"
}

[<Fact>]
let ``Schema.validated rejects invalid body with 400`` () = task {
    let handler = Schema.validated createTodoSchema (fun todo -> task {
        return Response.text todo.Title
    })
    let routes = Route.start |> Route.post "/todos" handler
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/todos" """{"title":"ab"}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "at least 3"
}

[<Fact>]
let ``Schema.validated rejects completely invalid JSON with 400`` () = task {
    let handler = Schema.validated createTodoSchema (fun todo -> task {
        return Response.text todo.Title
    })
    let routes = Route.start |> Route.post "/todos" handler
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/todos" "not json at all{{{"
    r.Status |> should equal 400
    r.Body |> should haveSubstring "invalid JSON"
}

// --- Coverage: Schema.toJsonSchema with rules (Pattern, Min, Max, Format, Enum, nested children) ---

[<Fact>]
let ``Schema.toJsonSchema includes pattern rule`` () =
    let jsonSchema = Schema.toJsonSchema addressSchema
    jsonSchema |> should haveSubstring "pattern"

[<Fact>]
let ``Schema.toJsonSchema includes min and max rules`` () =
    let s = schema {
        let! age = Schema.required "age" Schema.float [ Schema.min 0.0; Schema.max 150.0 ]
        return {| Age = age |}
    }
    let jsonSchema = Schema.toJsonSchema s
    jsonSchema |> should haveSubstring "minimum"
    jsonSchema |> should haveSubstring "maximum"

[<Fact>]
let ``Schema.toJsonSchema includes format rule`` () =
    let s = schema {
        let! email = Schema.required "email" Schema.string [ Schema.email ]
        return {| Email = email |}
    }
    let jsonSchema = Schema.toJsonSchema s
    jsonSchema |> should haveSubstring "\"format\""
    jsonSchema |> should haveSubstring "email"

[<Fact>]
let ``Schema.toJsonSchema includes enum rule`` () =
    let jsonSchema = Schema.toJsonSchema taskSchema
    jsonSchema |> should haveSubstring "enum"

[<Fact>]
let ``Schema.toJsonSchema includes url format`` () =
    let s = schema {
        let! link = Schema.required "link" Schema.string [ Schema.url ]
        return {| Link = link |}
    }
    let jsonSchema = Schema.toJsonSchema s
    jsonSchema |> should haveSubstring "\"format\""
    jsonSchema |> should haveSubstring "uri"

// --- Coverage: Schema.nullable via JsonElement (lines 301-303) ---

[<Fact>]
let ``Schema.nullable with null via JsonElement`` () =
    let s = schema {
        let! title = Schema.required "title" Schema.string []
        let! note = Schema.optional "note" (Schema.nullable Schema.string) None []
        return {| Title = title; Note = note |}
    }
    let json = """{"title":"test","note":null}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Ok r ->
        r.Title |> should equal "test"
        r.Note |> should equal None
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema.nullable with present value via JsonElement`` () =
    let s = schema {
        let! title = Schema.required "title" Schema.string []
        let! note = Schema.optional "note" (Schema.nullable Schema.string) None []
        return {| Title = title; Note = note |}
    }
    let json = """{"title":"test","note":"hello"}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Ok r ->
        r.Title |> should equal "test"
        r.Note |> should equal (Some "hello")
    | Error errs -> failwith $"expected Ok, got {errs}"

// --- Coverage: SchemaCompiler FInt via buffer (line 63) ---

[<Fact>]
let ``Schema.parseBuffer handles integer field`` () =
    let s = schema {
        let! name = Schema.required "name" Schema.string []
        let! age = Schema.required "age" Schema.int []
        return {| Name = name; Age = age |}
    }
    let json = """{"name":"Alice","age":30}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer s buffer with
    | Ok r ->
        r.Name |> should equal "Alice"
        r.Age |> should equal 30
    | Error errs -> failwith $"expected Ok, got {errs}"

// --- Coverage: SchemaCompiler FFloat via buffer (line 65) ---

[<Fact>]
let ``Schema.parseBuffer handles float field`` () =
    let s = schema {
        let! name = Schema.required "name" Schema.string []
        let! score = Schema.required "score" Schema.float []
        return {| Name = name; Score = score |}
    }
    let json = """{"name":"Alice","score":99.5}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer s buffer with
    | Ok r ->
        r.Name |> should equal "Alice"
        r.Score |> should (equalWithin 0.01) 99.5
    | Error errs -> failwith $"expected Ok, got {errs}"

// --- Coverage: SchemaCompiler parseAndValidate with type mismatch error (lines 144-145) ---

[<Fact>]
let ``Schema.parseBuffer reports error on type mismatch`` () =
    let s = schema {
        let! age = Schema.required "age" Schema.int []
        return {| Age = age |}
    }
    let json = """{"age":"not-a-number"}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer s buffer with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("age")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: SchemaCompiler parseAndValidate non-object input (line 129) ---

[<Fact>]
let ``Schema.parseBuffer reports error on non-object JSON`` () =
    let json = """42"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createTodoSchema buffer with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("expected JSON object")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: SchemaCompiler FStringList via buffer (lines 66-72) ---

[<Fact>]
let ``Schema.parseBuffer handles string list field`` () =
    let s = schema {
        let! tags = Schema.required "tags" (Schema.list Schema.string) []
        return {| Tags = tags |}
    }
    let json = """{"tags":["a","b","c"]}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer s buffer with
    | Ok r -> r.Tags |> should equal ["a"; "b"; "c"]
    | Error errs -> failwith $"expected Ok, got {errs}"

// --- Coverage: SchemaCompiler unknown field skip (lines 111-112) ---

[<Fact>]
let ``Schema.parseBuffer with parseAndValidate skips unknown properties`` () =
    let json = """{"title":"test","unknownField":{"nested":true},"completed":true}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer createTodoSchema buffer with
    | Ok todo -> todo.Title |> should equal "test"
    | Error _ -> failwith "expected Ok"

// --- Coverage: SchemaBuilder.Bind error collection (lines 550-554) ---

[<Fact>]
let ``Schema collects errors from multiple fields`` () =
    let json = """{"title":"ab","completed":"not-a-bool"}"""
    match Schema.parseString createTodoSchema json with
    | Error errs ->
        // Should have at least the minLength error for title
        errs |> List.length |> should be (greaterThanOrEqualTo 1)
        errs |> List.exists (fun e -> e.Contains("at least 3")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema collects errors from all invalid fields`` () =
    let s = schema {
        let! name = Schema.required "name" Schema.string [ Schema.minLength 3 ]
        let! age = Schema.required "age" Schema.int [ Schema.min 18.0 ]
        return {| Name = name; Age = age |}
    }
    let json = """{"name":"ab","age":5}"""
    match Schema.parseString s json with
    | Error errs ->
        errs |> List.length |> should be (greaterThanOrEqualTo 2)
    | Ok _ -> failwith "expected Error"

// --- Coverage: required field rule failure returning Error errs (lines 397-398) ---

[<Fact>]
let ``Schema.required field with failing rule returns rule errors`` () =
    let s = schema {
        let! name = Schema.required "name" Schema.string [ Schema.minLength 5; Schema.maxLength 3 ]
        return {| Name = name |}
    }
    let json = """{"name":"abcd"}"""
    match Schema.parseString s json with
    | Error errs ->
        // "abcd" has length 4: fails minLength 5 AND maxLength 3
        errs |> List.length |> should be (greaterThanOrEqualTo 2)
    | Ok _ -> failwith "expected Error"

// --- Coverage: optional field parse error (lines 433-434) ---

[<Fact>]
let ``Schema.optional field with parse error returns error`` () =
    let s = schema {
        let! title = Schema.required "title" Schema.string []
        let! count = Schema.optional "count" Schema.int 0 []
        return {| Title = title; Count = count |}
    }
    let json = """{"title":"test","count":"not-a-number"}"""
    match Schema.parseString s json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("count")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: optional field rule failure (lines 433) ---

[<Fact>]
let ``Schema.optional field with failing rule returns errors`` () =
    let s = schema {
        let! title = Schema.required "title" Schema.string []
        let! priority = Schema.optional "priority" Schema.string "medium" [ Schema.enum' ["low"; "medium"; "high"] ]
        return {| Title = title; Priority = priority |}
    }
    let json = """{"title":"test","priority":"urgent"}"""
    match Schema.parseString s json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("must be one of")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: SchemaCompiler optional default value via buffer (line 116) ---

[<Fact>]
let ``Schema.parseBuffer uses default for missing optional field with rules validation`` () =
    let s = schema {
        let! title = Schema.required "title" Schema.string [ Schema.minLength 3 ]
        let! priority = Schema.optional "priority" Schema.string "medium" []
        return {| Title = title; Priority = priority |}
    }
    let json = """{"title":"test"}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer s buffer with
    | Ok r ->
        r.Title |> should equal "test"
        r.Priority |> should equal "medium"
    | Error errs -> failwith $"expected Ok, got {errs}"

// --- Coverage: SchemaCompiler parseAndValidate rule validation on buffer path (lines 97) ---

[<Fact>]
let ``Schema.parseBuffer validates rules on parsed values`` () =
    let s = schema {
        let! title = Schema.required "title" Schema.string [ Schema.minLength 5 ]
        return {| Title = title |}
    }
    let json = """{"title":"ab"}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = ReadOnlySequence<byte>(bytes)
    match Schema.parseBuffer s buffer with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("at least 5")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: Schema.parseJson directly ---

[<Fact>]
let ``Schema.parseJson parses valid JsonElement`` () =
    let json = """{"title":"Buy milk","completed":true}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson createTodoSchema doc.RootElement with
    | Ok todo ->
        todo.Title |> should equal "Buy milk"
        todo.Completed |> should equal true
    | Error _ -> failwith "expected Ok"

// --- Coverage: Schema.float parser via JsonElement (line 285) ---

[<Fact>]
let ``Schema.float parses via JsonElement`` () =
    let s = schema {
        let! score = Schema.required "score" Schema.float []
        return {| Score = score |}
    }
    let json = """{"score":42.5}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.Score |> should (equalWithin 0.01) 42.5
    | Error _ -> failwith "expected Ok"

// --- Coverage: Schema.list success path via JsonElement (lines 289-296) ---

[<Fact>]
let ``Schema.list parses valid items via JsonElement`` () =
    let s = schema {
        let! tags = Schema.required "tags" (Schema.list Schema.string) []
        return {| Tags = tags |}
    }
    let json = """{"tags":["a","b","c"]}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.Tags |> should equal ["a"; "b"; "c"]
    | Error _ -> failwith "expected Ok"

// --- Coverage: Schema.int parser via JsonElement (line 279) ---

[<Fact>]
let ``Schema.int parses via JsonElement`` () =
    let s = schema {
        let! age = Schema.required "age" Schema.int []
        return {| Age = age |}
    }
    let json = """{"age":25}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.Age |> should equal 25
    | Error _ -> failwith "expected Ok"

// --- Coverage: Schema.bool parser via JsonElement (line 282) ---

[<Fact>]
let ``Schema.bool parses via JsonElement`` () =
    let s = schema {
        let! flag = Schema.required "flag" Schema.bool []
        return {| Flag = flag |}
    }
    let json = """{"flag":true}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.Flag |> should equal true
    | Error _ -> failwith "expected Ok"

// --- Coverage: required field rule failure via JsonElement (lines 397-398) ---

[<Fact>]
let ``Schema.required rule failure via JsonElement`` () =
    let s = schema {
        let! name = Schema.required "name" Schema.string [ Schema.minLength 5 ]
        return {| Name = name |}
    }
    let json = """{"name":"ab"}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("at least 5")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: required field parse failure via JsonElement (line 399) ---

[<Fact>]
let ``Schema.required parse error via JsonElement`` () =
    let s = schema {
        let! age = Schema.required "age" Schema.int []
        return {| Age = age |}
    }
    let json = """{"age":"not-int"}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("age")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: optional field rule failure via JsonElement (lines 433-434) ---

[<Fact>]
let ``Schema.optional rule failure via JsonElement`` () =
    let s = schema {
        let! title = Schema.required "title" Schema.string []
        let! priority = Schema.optional "priority" Schema.string "medium" [ Schema.enum' ["low"; "medium"; "high"] ]
        return {| Title = title; Priority = priority |}
    }
    let json = """{"title":"test","priority":"urgent"}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("must be one of")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: optional field parse error via JsonElement ---

[<Fact>]
let ``Schema.optional parse error via JsonElement`` () =
    let s = schema {
        let! title = Schema.required "title" Schema.string []
        let! count = Schema.optional "count" Schema.int 0 []
        return {| Title = title; Count = count |}
    }
    let json = """{"title":"test","count":"not-int"}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("count")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: Schema.parseString with valid JSON (line 448-449) ---

[<Fact>]
let ``Schema.parseString with invalid JSON returns error`` () =
    match Schema.parseString createTodoSchema "{{invalid" with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("invalid JSON")) |> should be True
    | Ok _ -> failwith "expected Error"

// --- Coverage: SchemaBuilder.Bind second field error collection (lines 550-554) ---

[<Fact>]
let ``Schema.Bind collects errors from both fields via JsonElement`` () =
    let s = schema {
        let! name = Schema.required "name" Schema.string [ Schema.minLength 5 ]
        let! email = Schema.required "email" Schema.string [ Schema.email ]
        return {| Name = name; Email = email |}
    }
    let json = """{"name":"ab","email":"invalid"}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson s doc.RootElement with
    | Error errs ->
        // Both fields should have errors
        errs |> List.length |> should be (greaterThanOrEqualTo 2)
    | Ok _ -> failwith "expected Error"

// --- Coverage: Schema.nest parser error path (line 345) via JsonElement ---

[<Fact>]
let ``Schema.nest parser returns error on missing nested fields via JsonElement`` () =
    let json = """{"name":"Alice","email":"alice@test.com","address":{}}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson createUserSchema doc.RootElement with
    | Error errs -> errs |> List.length |> should be (greaterThanOrEqualTo 1)
    | Ok _ -> failwith "expected Error"

// --- Coverage: Schema.validated exception path (lines 484-485) ---

[<Fact>]
let ``Schema.validated handles empty body gracefully`` () = task {
    let handler = Schema.validated createTodoSchema (fun todo -> task {
        return Response.text todo.Title
    })
    let routes = Route.start |> Route.post "/todos" handler
    let client = TestClient.create routes
    // Empty body - will fail to read BodyReader
    let! r = client |> TestClient.post "/todos" ""
    r.Status |> should equal 400
}

// --- Coverage: toJsonSchema with nested children (lines 509-518) ---

[<Fact>]
let ``Schema.toJsonSchema includes nested object properties`` () =
    let nestedSchema = schema {
        let! name = Schema.required "name" Schema.string []
        let! address = Schema.required "address" (Schema.nest addressSchema) []
        return {| Name = name; Address = address |}
    }
    let jsonSchema = Schema.toJsonSchema nestedSchema
    // The address field should have type "object" with children
    let doc = JsonDocument.Parse(jsonSchema)
    let props = doc.RootElement.GetProperty("properties")
    let addressProp = props.GetProperty("address")
    addressProp.GetProperty("type").GetString() |> should equal "object"

// --- Coverage: buildNestedFieldType with int/bool/float fields (Schema.fs lines 317-320) ---

let nestedWithTypes = schema {
    let! count = Schema.required "count" Schema.int []
    let! active = Schema.required "active" Schema.bool []
    let! score = Schema.optional "score" Schema.float 0.0 []
    return {| Count = count; Active = active; Score = score |}
}

let parentWithTypedNested = schema {
    let! name = Schema.required "name" Schema.string []
    let! data = Schema.required "data" (Schema.nest nestedWithTypes) []
    return {| Name = name; Data = data |}
}

[<Fact>]
let ``Buffer parses nested schema with int/bool/float fields`` () =
    let json = """{"name":"test","data":{"count":5,"active":true,"score":9.5}}"""
    match Schema.parseString parentWithTypedNested json with
    | Ok r ->
        r.Name |> should equal "test"
        r.Data.Count |> should equal 5
        r.Data.Active |> should equal true
        r.Data.Score |> should (equalWithin 0.01) 9.5
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Buffer parses nested schema with default optional float`` () =
    let json = """{"name":"test","data":{"count":5,"active":true}}"""
    match Schema.parseString parentWithTypedNested json with
    | Ok r ->
        r.Data.Count |> should equal 5
        r.Data.Active |> should equal true
        r.Data.Score |> should (equalWithin 0.01) 0.0
    | Error e -> failwith $"expected Ok, got {e}"

// --- Coverage: nest parser error path (Schema.fs line 345) ---

[<Fact>]
let ``Nested schema returns error for invalid nested data`` () =
    let json = """{"name":"test","data":{"count":"notint","active":true}}"""
    match Schema.parseString parentWithTypedNested json with
    | Error _ -> ()
    | Ok _ -> failwith "expected Error"

// --- Coverage: Nullable via buffer (SchemaCompiler lines 74-88) ---

let nullableBufferSchema = schema {
    let! name = Schema.required "name" Schema.string []
    let! nickname = Schema.optional "nickname" (Schema.nullable Schema.string) None []
    return {| Name = name; Nickname = nickname |}
}

[<Fact>]
let ``Buffer parses nullable field with value`` () =
    let json = """{"name":"Alice","nickname":"Ali"}"""
    match Schema.parseString nullableBufferSchema json with
    | Ok r ->
        r.Name |> should equal "Alice"
        r.Nickname |> should equal (Some "Ali")
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Buffer parses nullable field with null`` () =
    let json = """{"name":"Alice","nickname":null}"""
    match Schema.parseString nullableBufferSchema json with
    | Ok r ->
        r.Name |> should equal "Alice"
        r.Nickname |> should equal None
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Buffer parses nullable field missing`` () =
    let json = """{"name":"Alice"}"""
    match Schema.parseString nullableBufferSchema json with
    | Ok r ->
        r.Name |> should equal "Alice"
        r.Nickname |> should equal None
    | Error e -> failwith $"expected Ok, got {e}"

// --- Coverage: toJsonSchema with nested children populated (lines 510-518) ---

[<Fact>]
let ``toJsonSchema includes nested object children with properties`` () =
    let jsonSchema = Schema.toJsonSchema parentWithTypedNested
    let doc = System.Text.Json.JsonDocument.Parse(jsonSchema)
    let props = doc.RootElement.GetProperty("properties")
    let data = props.GetProperty("data")
    data.GetProperty("type").GetString() |> should equal "object"
    // Should have nested properties from children
    let nestedProps = data.GetProperty("properties")
    nestedProps.TryGetProperty("count") |> fst |> should be True
    nestedProps.TryGetProperty("active") |> fst |> should be True
    // Should have nested required array
    let nestedRequired = data.GetProperty("required")
    nestedRequired.GetArrayLength() |> should be (greaterThanOrEqualTo 2)

// --- Coverage: SchemaCompiler nested object via buffer (line 97), skip unknown in nested (line 111-112), default optional nested (line 116) ---

[<Fact>]
let ``Buffer parses nested schema skipping unknown nested properties`` () =
    let json = """{"name":"test","data":{"count":1,"active":false,"score":2.5,"extraField":"ignored"}}"""
    match Schema.parseString parentWithTypedNested json with
    | Ok r ->
        r.Data.Count |> should equal 1
        r.Data.Active |> should equal false
    | Error e -> failwith $"expected Ok, got {e}"

// --- Coverage: Schema.nest Ok path via JsonElement (line 348) ---

[<Fact>]
let ``Schema.nest Ok path via JsonElement`` () =
    let json = """{"name":"Alice","email":"alice@test.com","address":{"street":"Main","city":"NY","zip":"12345"}}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson createUserSchema doc.RootElement with
    | Ok user ->
        user.Name |> should equal "Alice"
        user.Address.Street |> should equal "Main"
    | Error errs -> failwith $"expected Ok, got {errs}"

// --- Coverage: buildNestedFieldType fallback for nested-nested (line 323) ---

let innerSchema = schema {
    let! tag = Schema.required "tag" Schema.string []
    return {| Tag = tag |}
}

let middleSchema = schema {
    let! label = Schema.required "label" Schema.string []
    let! inner = Schema.required "inner" (Schema.nest innerSchema) []
    return {| Label = label; Inner = inner |}
}

let outerSchema = schema {
    let! title = Schema.required "title" Schema.string []
    let! middle = Schema.required "middle" (Schema.nest middleSchema) []
    return {| Title = title; Middle = middle |}
}

[<Fact>]
let ``Nested-nested schema hits buildNestedFieldType fallback for object type`` () =
    let json = """{"title":"top","middle":{"label":"mid","inner":{"tag":"deep"}}}"""
    use doc = System.Text.Json.JsonDocument.Parse(json)
    match Schema.parseJson outerSchema doc.RootElement with
    | Ok r ->
        r.Title |> should equal "top"
        r.Middle.Label |> should equal "mid"
        r.Middle.Inner.Tag |> should equal "deep"
    | Error e -> failwith $"expected Ok, got {e}"

// --- Coverage: optional field children lookup (line 422) ---

let optionalNestedSchema = schema {
    let! title = Schema.required "title" Schema.string []
    let! extra = Schema.optional "extra" (Schema.nest innerSchema) {| Tag = "default" |} []
    return {| Title = title; Extra = extra |}
}

[<Fact>]
let ``Optional nested field uses default when missing`` () =
    let json = """{"title":"test"}"""
    match Schema.parseString optionalNestedSchema json with
    | Ok r ->
        r.Title |> should equal "test"
        r.Extra.Tag |> should equal "default"
    | Error e -> failwith $"expected Ok, got {e}"

// --- Coverage: FNullable with int/bool/float inner types via buffer (lines 84-88) ---

let nullableIntSchema = schema {
    let! name = Schema.required "name" Schema.string []
    let! count = Schema.optional "count" (Schema.nullable Schema.int) None []
    return {| Name = name; Count = count |}
}

[<Fact>]
let ``Buffer parses nullable int field with value`` () =
    let json = """{"name":"test","count":42}"""
    match Schema.parseString nullableIntSchema json with
    | Ok r ->
        r.Name |> should equal "test"
        r.Count |> should equal (Some 42)
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Buffer parses nullable int field with null`` () =
    let json = """{"name":"test","count":null}"""
    match Schema.parseString nullableIntSchema json with
    | Ok r -> r.Count |> should equal None
    | Error e -> failwith $"expected Ok, got {e}"

let nullableBoolSchema = schema {
    let! name = Schema.required "name" Schema.string []
    let! active = Schema.optional "active" (Schema.nullable Schema.bool) None []
    return {| Name = name; Active = active |}
}

[<Fact>]
let ``Buffer parses nullable bool field with value`` () =
    let json = """{"name":"test","active":true}"""
    match Schema.parseString nullableBoolSchema json with
    | Ok r -> r.Active |> should equal (Some true)
    | Error e -> failwith $"expected Ok, got {e}"

let nullableFloatSchema = schema {
    let! name = Schema.required "name" Schema.string []
    let! score = Schema.optional "score" (Schema.nullable Schema.float) None []
    return {| Name = name; Score = score |}
}

[<Fact>]
let ``Buffer parses nullable float field with value`` () =
    let json = """{"name":"test","score":3.14}"""
    match Schema.parseString nullableFloatSchema json with
    | Ok r -> r.Score |> should equal (Some 3.14)
    | Error e -> failwith $"expected Ok, got {e}"

// --- Cover Schema:373 — inferFieldType fallback for nullable with string list inner type ---

let nullableStringListSchema = schema {
    let! name = Schema.required "name" Schema.string []
    let! tags = Schema.optional "tags" (Schema.nullable (Schema.list Schema.string)) None []
    return {| Name = name; Tags = tags |}
}

[<Fact>]
let ``Nullable string list field via buffer`` () =
    let json = """{"name":"test","tags":["a","b"]}"""
    match Schema.parseString nullableStringListSchema json with
    | Ok r ->
        r.Name |> should equal "test"
        r.Tags |> Option.isSome |> should be True
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Nullable string list field with null via buffer`` () =
    let json = """{"name":"test","tags":null}"""
    match Schema.parseString nullableStringListSchema json with
    | Ok r -> r.Tags |> should equal None
    | Error e -> failwith $"expected Ok, got {e}"

// --- Cover Schema:469-470 — parseString exception when ParseBuffer throws ---

[<Fact>]
let ``parseString handles corrupted UTF8 gracefully`` () =
    // Empty string triggers error in buffer parser
    match Schema.parseString createTodoSchema "" with
    | Error errs -> errs.Length |> should be (greaterThan 0)
    | Ok _ -> failwith "expected Error"

// --- Cover Schema:505-506 — validated exception path ---

[<Fact>]
let ``Schema.validated returns 400 on PipeReader exception`` () = task {
    // Test with a request that has an empty/broken body
    let testSchema = schema {
        let! name = Schema.required "name" Schema.string []
        return {| Name = name |}
    }
    let handler = Schema.validated testSchema (fun v -> task {
        return Response.json {| name = v.Name |}
    })
    let routes = Route.start |> Route.post "/test" handler
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config System.Threading.CancellationToken.None
    use client = new System.Net.Http.HttpClient()
    // Send completely empty body — PipeReader should get empty buffer, parser fails
    let content = new System.Net.Http.StringContent("", System.Text.Encoding.UTF8, "application/json")
    let! r = client.PostAsync($"http://127.0.0.1:{port}/test", content)
    int r.StatusCode |> should be (greaterThanOrEqualTo 400)
    do! stop()
}

// --- Cover SchemaCompiler:100 — parseObject when reader not at StartObject ---
// This is hit when the nested object is embedded in a parent and the reader
// has already advanced past the property name. The test for nested schemas
// with int/bool/float fields should cover this path already.
// Adding an explicit test with nested object that has an extra preceding property.

[<Fact>]
let ``Buffer parses nested object with preceding unknown fields`` () =
    let json = """{"name":"test","data":{"unknown":"skip","count":3,"active":true,"score":1.5}}"""
    match Schema.parseString parentWithTypedNested json with
    | Ok r ->
        r.Data.Count |> should equal 3
        r.Data.Active |> should equal true
    | Error e -> failwith $"expected Ok, got {e}"

// --- Cover Schema:374 — inferFieldType fallback for truly unknown nullable inner type ---
// This is structurally hard to hit since all common types are covered.
// Skipping — it's a defensive fallback.

// --- Cover Schema:470-471 — parseString catch block ---
// parseString catches exceptions from ParseBuffer. ParseBuffer fails on truly malformed data.

[<Fact>]
let ``parseString returns error on null input`` () =
    match Schema.parseString createTodoSchema (null: string) with
    | Error errs -> errs.Length |> should be (greaterThan 0)
    | Ok _ -> failwith "expected Error"

// --- Cover Schema:506-507 — validated catch on broken body ---

[<Fact>]
let ``Schema.validated returns 400 on truly broken request`` () = task {
    let testSchema = schema {
        let! x = Schema.required "x" Schema.string []
        return {| X = x |}
    }
    let routes =
        Route.start
        |> Route.post "/test" (Schema.validated testSchema (fun v -> task {
            return Response.text v.X
        }))
    let config = App.defaults |> App.port 0
    let! (port, stop) = App.runTest routes config System.Threading.CancellationToken.None
    use client = new System.Net.Http.HttpClient()
    // Send non-JSON content type with garbage
    let content = new System.Net.Http.ByteArrayContent([| 0xFFuy; 0xFEuy |])
    let! r = client.PostAsync($"http://127.0.0.1:{port}/test", content)
    int r.StatusCode |> should be (greaterThanOrEqualTo 400)
    do! stop()
}

// --- Schema.fromType tests ---

type SimpleTodo = { Title: string; Completed: bool }
type OptionalTodo = { Title: string; Notes: string option }

[<Fact>]
let ``fromType creates schema from record`` () =
    let s = Schema.fromType<SimpleTodo> ()
    let json = """{"Title":"test","Completed":true}"""
    match Schema.parseString s json with
    | Ok todo ->
        todo.Title |> should equal "test"
        todo.Completed |> should equal true
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``fromType handles optional fields`` () =
    let s = Schema.fromType<OptionalTodo> ()
    let json = """{"Title":"test"}"""
    match Schema.parseString s json with
    | Ok todo ->
        todo.Title |> should equal "test"
        todo.Notes |> should equal None
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``fromType rejects missing required fields`` () =
    let s = Schema.fromType<SimpleTodo> ()
    match Schema.parseString s """{}""" with
    | Error errs -> errs.Length |> should be (greaterThan 0)
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType generates JSON Schema`` () =
    let s = Schema.fromType<SimpleTodo> ()
    let jsonSchema = Schema.toJsonSchema s
    jsonSchema |> should haveSubstring "Title"
    jsonSchema |> should haveSubstring "Completed"

// --- Schema coercion tests ---

[<Fact>]
let ``Schema coerces string to int`` () =
    let s = schema {
        let! age = Schema.required "age" Schema.int []
        return {| Age = age |}
    }
    let json = """{"age":"42"}"""
    match Schema.parseString s json with
    | Ok r -> r.Age |> should equal 42
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Schema coerces string to bool`` () =
    let s = schema {
        let! active = Schema.required "active" Schema.bool []
        return {| Active = active |}
    }
    let json = """{"active":"true"}"""
    match Schema.parseString s json with
    | Ok r -> r.Active |> should equal true
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Schema coerces string to float`` () =
    let s = schema {
        let! score = Schema.required "score" Schema.float []
        return {| Score = score |}
    }
    let json = """{"score":"3.14"}"""
    match Schema.parseString s json with
    | Ok r -> r.Score |> should be (greaterThan 3.0)
    | Error e -> failwith $"expected Ok, got {e}"

// --- Schema transform tests ---

[<Fact>]
let ``Schema.trim removes whitespace`` () =
    let s = schema {
        let! name = Schema.required "name" Schema.string [ Schema.trim ]
        return {| Name = name |}
    }
    let json = """{"name":"  Alice  "}"""
    match Schema.parseString s json with
    | Ok r -> r.Name |> should equal "Alice"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Schema.lowercase lowercases strings`` () =
    let s = schema {
        let! email = Schema.required "email" Schema.string [ Schema.lowercase ]
        return {| Email = email |}
    }
    let json = """{"email":"ALICE@TEST.COM"}"""
    match Schema.parseString s json with
    | Ok r -> r.Email |> should equal "alice@test.com"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Schema.uppercase uppercases strings`` () =
    let s = schema {
        let! code = Schema.required "code" Schema.string [ Schema.uppercase ]
        return {| Code = code |}
    }
    let json = """{"code":"abc"}"""
    match Schema.parseString s json with
    | Ok r -> r.Code |> should equal "ABC"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Schema transforms + validation compose`` () =
    let s = schema {
        let! name = Schema.required "name" Schema.string [ Schema.trim; Schema.minLength 3 ]
        return {| Name = name |}
    }
    // "  Al  " trims to "Al" which is < 3 chars
    let json = """{"name":"  Al  "}"""
    match Schema.parseString s json with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("at least 3")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema transforms run before validation`` () =
    let s = schema {
        let! name = Schema.required "name" Schema.string [ Schema.trim; Schema.minLength 5 ]
        return {| Name = name |}
    }
    // "  Hello  " trims to "Hello" which is exactly 5 chars
    let json = """{"name":"  Hello  "}"""
    match Schema.parseString s json with
    | Ok r -> r.Name |> should equal "Hello"
    | Error e -> failwith $"expected Ok, got {e}"

type NullableScalarRecord = {
    Count: int option
    Active: bool option
    Score: float option
}

type GeneratedCollectionRecord = {
    Tags: string list
    Notes: string option
}

type GeneratedObjectRecord = {
    Metadata: obj
}

type GeneratedScalarRecord = {
    Name: string
    Count: int
    Active: bool
    Score: float
    Tags: string list
}

type UnsupportedGeneratedRecord = {
    Value: Guid
}

type ThrowingReadStream() =
    inherit System.IO.Stream()

    override _.CanRead = true
    override _.CanSeek = false
    override _.CanWrite = false
    override _.Length = 0L
    override _.Position with get () = 0L and set _ = ()
    override _.Flush() = ()
    override _.Read(_, _, _) = raise (System.InvalidOperationException("stream exploded"))
    override _.Seek(_, _) = raise (System.NotSupportedException())
    override _.SetLength(_) = raise (System.NotSupportedException())
    override _.Write(_, _, _) = raise (System.NotSupportedException())
    override _.ReadAsync(buffer: Memory<byte>, _cancellationToken) =
        System.Threading.Tasks.ValueTask<int>(System.Threading.Tasks.Task.FromException<int>(System.InvalidOperationException("stream exploded")))

[<Fact>]
let ``Schema.int rejects non-scalar JsonElement`` () =
    let s = schema {
        let! age = Schema.required "age" Schema.int []
        return {| Age = age |}
    }
    use doc = JsonDocument.Parse("""{"age":{}}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> should contain "age: expected integer"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.int coerces string JsonElement`` () =
    let s = schema {
        let! age = Schema.required "age" Schema.int []
        return {| Age = age |}
    }
    use doc = JsonDocument.Parse("""{"age":"42"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.Age |> should equal 42
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema.int rejects overflowing JsonElement number`` () =
    let s = schema {
        let! age = Schema.required "age" Schema.int []
        return {| Age = age |}
    }
    use doc = JsonDocument.Parse("""{"age":999999999999}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> should contain "age: expected integer"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.bool coerces string via JsonElement`` () =
    let s = schema {
        let! flag = Schema.required "flag" Schema.bool []
        return {| Flag = flag |}
    }
    use doc = JsonDocument.Parse("""{"flag":"true"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.Flag |> should equal true
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema.bool rejects invalid string JsonElement`` () =
    let s = schema {
        let! flag = Schema.required "flag" Schema.bool []
        return {| Flag = flag |}
    }
    use doc = JsonDocument.Parse("""{"flag":"maybe"}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> should contain "flag: expected boolean"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.bool rejects object JsonElement`` () =
    let s = schema {
        let! flag = Schema.required "flag" Schema.bool []
        return {| Flag = flag |}
    }
    use doc = JsonDocument.Parse("""{"flag":{}}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> should contain "flag: expected boolean"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.float coerces string via JsonElement`` () =
    let s = schema {
        let! score = Schema.required "score" Schema.float []
        return {| Score = score |}
    }
    use doc = JsonDocument.Parse("""{"score":"3.14"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.Score |> should (equalWithin 0.01) 3.14
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema.float rejects invalid string JsonElement`` () =
    let s = schema {
        let! score = Schema.required "score" Schema.float []
        return {| Score = score |}
    }
    use doc = JsonDocument.Parse("""{"score":"nan?"}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> should contain "score: expected number"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.float rejects object JsonElement`` () =
    let s = schema {
        let! score = Schema.required "score" Schema.float []
        return {| Score = score |}
    }
    use doc = JsonDocument.Parse("""{"score":{}}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> should contain "score: expected number"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema buffer parser rejects numeric booleans`` () =
    let s = schema {
        let! flag = Schema.required "flag" Schema.bool []
        return {| Flag = flag |}
    }
    match Schema.parseString s """{"flag":1}""" with
    | Error errs -> errs |> should contain "flag: expected boolean"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema buffer parser rejects boolean integers`` () =
    let s = schema {
        let! age = Schema.required "age" Schema.int []
        return {| Age = age |}
    }
    match Schema.parseString s """{"age":true}""" with
    | Error errs -> errs |> should contain "age: expected integer"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema buffer parser rejects boolean floats`` () =
    let s = schema {
        let! score = Schema.required "score" Schema.float []
        return {| Score = score |}
    }
    match Schema.parseString s """{"score":true}""" with
    | Error errs -> errs |> should contain "score: expected number"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema nullable custom parser uses fallback field type`` () =
    let rawObject (el: JsonElement) : Result<obj, string> = Ok (box (el.GetRawText()))
    let s = schema {
        let! payload = Schema.optional "payload" (Schema.nullable rawObject) None []
        return {| Payload = payload |}
    }
    use doc = JsonDocument.Parse("""{"payload":{"a":1}}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r ->
        r.Payload |> should equal (Some (box """{"a":1}"""))
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``Schema.parseRequest returns invalid JSON when body reader throws`` () = task {
    let ctx = Microsoft.AspNetCore.Http.DefaultHttpContext()
    ctx.Request.Body <- new ThrowingReadStream()
    let req = Request(ctx, System.Collections.Generic.Dictionary<string, string>() :> System.Collections.Generic.IReadOnlyDictionary<_, _>)
    let! result = Schema.parseRequest createTodoSchema req
    match result with
    | Error errs -> errs |> should contain "invalid JSON: stream exploded"
    | Ok _ -> failwith "expected Error"
}

[<Fact>]
let ``Schema.validated returns 400 when body reader throws`` () = task {
    let ctx = Microsoft.AspNetCore.Http.DefaultHttpContext()
    ctx.Request.Body <- new ThrowingReadStream()
    let req = Request(ctx, System.Collections.Generic.Dictionary<string, string>() :> System.Collections.Generic.IReadOnlyDictionary<_, _>)
    let handler = Schema.validated createTodoSchema (fun _ -> task { return Response.ok })
    let! response = handler req
    response.Status |> should equal 400
    match response.Body with
    | Json body -> System.Text.Encoding.UTF8.GetString(body) |> should haveSubstring "stream exploded"
    | _ -> failwith "expected JSON response"
}

[<Fact>]
let ``fromType coerces nullable scalar strings via parseJson`` () =
    let s = Schema.fromType<NullableScalarRecord> ()
    use doc = JsonDocument.Parse("""{"Count":"42","Active":"true","Score":"3.5"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok value ->
        value.Count |> should equal (Some 42)
        value.Active |> should equal (Some true)
        value.Score |> should equal (Some 3.5)
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``fromType parses collections and optional fields`` () =
    let s = Schema.fromType<GeneratedCollectionRecord> ()
    match Schema.parseString s """{"Tags":["a","b"],"Notes":"hi"}""" with
    | Ok value ->
        value.Tags |> should equal ["a"; "b"]
        value.Notes |> should equal (Some "hi")
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``fromType parses object fallback values`` () =
    let s = Schema.fromType<GeneratedObjectRecord> ()
    use doc = JsonDocument.Parse("""{"Metadata":123}""")
    match Schema.parseJson s doc.RootElement with
    | Ok value -> value.Metadata |> should equal (box 123.0)
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``fromType parses object string values`` () =
    let s = Schema.fromType<GeneratedObjectRecord> ()
    use doc = JsonDocument.Parse("""{"Metadata":"hello"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok value -> value.Metadata |> should equal (box "hello")
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``fromType parses object boolean values`` () =
    let s = Schema.fromType<GeneratedObjectRecord> ()
    use doc = JsonDocument.Parse("""{"Metadata":true}""")
    match Schema.parseJson s doc.RootElement with
    | Ok value -> value.Metadata |> should equal (box true)
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``fromType parses object null values`` () =
    let s = Schema.fromType<GeneratedObjectRecord> ()
    use doc = JsonDocument.Parse("""{"Metadata":null}""")
    match Schema.parseJson s doc.RootElement with
    | Ok value -> value.Metadata |> should equal null
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``fromType parses object raw JSON values`` () =
    let s = Schema.fromType<GeneratedObjectRecord> ()
    use doc = JsonDocument.Parse("""{"Metadata":{"nested":1}}""")
    match Schema.parseJson s doc.RootElement with
    | Ok value -> value.Metadata |> should equal (box """{"nested":1}""")
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``fromType parses required scalar values via JsonElement`` () =
    let s = Schema.fromType<GeneratedScalarRecord> ()
    use doc = JsonDocument.Parse("""{"Name":"test","Count":1,"Active":true,"Score":2.5,"Tags":["a","b"]}""")
    match Schema.parseJson s doc.RootElement with
    | Ok value ->
        value.Name |> should equal "test"
        value.Count |> should equal 1
        value.Active |> should equal true
        value.Score |> should equal 2.5
        value.Tags |> should equal ["a"; "b"]
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``fromType reports parse errors for invalid scalar values`` () =
    let s = Schema.fromType<GeneratedScalarRecord> ()
    use doc = JsonDocument.Parse("""{"Name":"test","Count":"oops","Active":"maybe","Score":"nan?","Tags":"bad"}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs ->
        errs |> List.exists (fun e -> e.Contains("Count")) |> should be True
        errs |> List.exists (fun e -> e.Contains("Active")) |> should be True
        errs |> List.exists (fun e -> e.Contains("Score")) |> should be True
        errs |> List.exists (fun e -> e.Contains("Tags")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType rejects non-scalar numeric and boolean values`` () =
    let s = Schema.fromType<GeneratedScalarRecord> ()
    use doc = JsonDocument.Parse("""{"Name":"test","Count":{},"Active":{},"Score":{},"Tags":["a"]}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs ->
        errs |> List.exists (fun e -> e.Contains("Count")) |> should be True
        errs |> List.exists (fun e -> e.Contains("Active")) |> should be True
        errs |> List.exists (fun e -> e.Contains("Score")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType uses defaults for missing optional fields via JsonElement`` () =
    let s = Schema.fromType<GeneratedCollectionRecord> ()
    use doc = JsonDocument.Parse("""{"Tags":["a"]}""")
    match Schema.parseJson s doc.RootElement with
    | Ok value -> value.Notes |> should equal None
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``fromType treats explicit null option fields as None`` () =
    let s = Schema.fromType<GeneratedCollectionRecord> ()
    use doc = JsonDocument.Parse("""{"Tags":["a"],"Notes":null}""")
    match Schema.parseJson s doc.RootElement with
    | Ok value -> value.Notes |> should equal None
    | Error errs -> failwith $"expected Ok, got {errs}"

[<Fact>]
let ``fromType fallback types still generate JSON schema`` () =
    let s = Schema.fromType<UnsupportedGeneratedRecord> ()
    let jsonSchema = Schema.toJsonSchema s
    jsonSchema |> should haveSubstring "\"Value\""
    jsonSchema |> should haveSubstring "\"string\""

[<Fact>]
let ``fromType reports unsupported type parse errors`` () =
    let s = Schema.fromType<UnsupportedGeneratedRecord> ()
    use doc = JsonDocument.Parse("""{"Value":"nope"}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("unsupported type Guid")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.bool handles disposed JsonElement`` () =
    let element =
        let doc = JsonDocument.Parse("""true""")
        let value = doc.RootElement
        doc.Dispose()
        value
    match Schema.bool element with
    | Error err -> err |> should equal "expected boolean"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.float handles disposed JsonElement`` () =
    let element =
        let doc = JsonDocument.Parse("""1.5""")
        let value = doc.RootElement
        doc.Dispose()
        value
    match Schema.float element with
    | Error err -> err |> should equal "expected number"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``SchemaCompiler handles nullable nested values`` () =
    let ctor (_: obj[]) = box "nested"
    let json = """{"child":{}}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let mutable reader = Utf8JsonReader(ReadOnlySequence<byte>(bytes))
    reader.Read() |> ignore
    reader.Read() |> ignore
    reader.Read() |> ignore
    let value = SchemaCompiler.readValue (SchemaCompiler.FNullable (SchemaCompiler.FNested([||], [||], ctor))) &reader
    (value :?> string option) |> should equal (Some "nested")
