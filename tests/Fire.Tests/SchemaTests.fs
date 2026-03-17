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
