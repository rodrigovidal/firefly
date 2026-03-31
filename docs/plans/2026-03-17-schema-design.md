# Fire Schema — Typed JSON Schema Design

A built-in schema library that combines parsing, validation, and type inference in one declaration. Like Zod/JSON Schema but statically typed in F#.

## Core Concept

A schema defines shape + types + rules. The CE binds fields and produces a typed output. Parsing happens directly from raw JSON (stream/JsonElement) — no separate deserialization step.

```fsharp
let createTodo = schema {
    let! title     = Schema.required "title" Schema.string [ Schema.minLength 3 ]
    let! completed = Schema.optional "completed" Schema.bool false
    return {| Title = title; Completed = completed |}
}
// Type: Schema<{| Title: string; Completed: bool |}>
```

## Types

```fsharp
// A parsed field result
type FieldResult<'T> = {
    Name: string
    Value: 'T
}

// A schema that parses JsonElement into 'T
type Schema<'T> = {
    Parse: JsonElement -> Result<'T, string list>
    Fields: FieldSpec list  // for JSON Schema generation
}

// Field spec for JSON Schema output
type FieldSpec = {
    Name: string
    Type: string        // "string", "integer", "boolean", "number", "array", "object"
    Required: bool
    Rules: RuleSpec list
    Children: FieldSpec list  // for nested objects
}

type RuleSpec =
    | MinLength of int
    | MaxLength of int
    | ExactLength of int
    | Pattern of string
    | Min of float
    | Max of float
    | ExclusiveMin of float
    | ExclusiveMax of float
    | MultipleOf of float
    | MinItems of int
    | MaxItems of int
    | Format of string  // "email", "uri", "uuid", "ip", "ipv4", "ipv6", "date-time", etc.
    | Enum of string list
```

## Schema Module — Type Builders

```fsharp
[<RequireQualifiedAccess>]
module Schema =

    // Primitive types — each returns a parser for that type from JsonElement
    let string : JsonElement -> Result<string, string>
    let int : JsonElement -> Result<int, string>
    let bool : JsonElement -> Result<bool, string>
    let float : JsonElement -> Result<float, string>

    // Composite types
    let list (itemParser: JsonElement -> Result<'T, string>) : JsonElement -> Result<'T list, string>
    let nullable (parser: JsonElement -> Result<'T, string>) : JsonElement -> Result<'T option, string>
    let nest (schema: Schema<'T>) : JsonElement -> Result<'T, string>

    // String rules
    let minLength (len: int) : Rule
    let maxLength (len: int) : Rule
    let length (len: int) : Rule       // exact length
    let nonempty : Rule                // non-empty string
    let pattern (regex: string) : Rule
    let email : Rule
    let url : Rule
    let uuid : Rule
    let ip : Rule
    let ipv4 : Rule
    let ipv6 : Rule
    let datetime : Rule                // ISO 8601 date/time string
    let startsWith (prefix: string) : Rule
    let endsWith (suffix: string) : Rule
    let includes (substring: string) : Rule
    let enum' (values: string list) : Rule

    // String transforms
    let trim : Rule
    let lowercase : Rule
    let uppercase : Rule

    // Number rules
    let min (n: float) : Rule          // >= n (inclusive)
    let max (n: float) : Rule          // <= n (inclusive)
    let gt (n: float) : Rule           // > n (exclusive)
    let lt (n: float) : Rule           // < n (exclusive)
    let positive : Rule                // > 0
    let negative : Rule                // < 0
    let nonnegative : Rule             // >= 0
    let nonpositive : Rule             // <= 0
    let int' : Rule                    // must be an integer
    let multipleOf (n: float) : Rule   // divisible by n

    // Array rules
    let minItems (n: int) : Rule
    let maxItems (n: int) : Rule
    let nonEmpty : Rule                // at least 1 item

    // Field builders — used inside schema CE
    let required (name: string) (parser) (rules: Rule list) : SchemaField<'T>
    let optional (name: string) (parser) (defaultValue: 'T) (rules: Rule list) : SchemaField<'T>
    let req (name: string) (parser) : SchemaField<'T>   // required, no rules
    let opt (name: string) (parser) (defaultValue: 'T) : SchemaField<'T>  // optional, no rules

    // Cross-field validation
    let check (validate: unit -> Result<unit, string>) : SchemaCheck

    // Auto-generate schema from F# record type
    let fromType<'T> () : Schema<'T>  // supports nested records, option, typed lists

    // Parse from various sources
    let parseJson (schema: Schema<'T>) (json: JsonElement) : Result<'T, string list>
    let parseString (schema: Schema<'T>) (jsonString: string) : Result<'T, string list>
    let parseBuffer (schema: Schema<'T>) (buffer: ReadOnlySequence<byte>) : Result<'T, string list>
    let parsePipe (schema: Schema<'T>) (pipeReader: PipeReader) : Task<Result<'T, string list>>
    let parseStream (schema: Schema<'T>) (stream: Stream) : Task<Result<'T, string list>>
    let parseLookup (schema: Schema<'T>) (lookup: string -> string option) : Result<'T, string list>
    let parseMap (schema: Schema<'T>) (data: IReadOnlyDictionary<string, string>) : Result<'T, string list>

    // Generate JSON Schema
    let toJsonSchema (schema: Schema<'T>) : string
```

## Schema CE Builder

```fsharp
type SchemaBuilder() =
    member _.Bind(field: SchemaField<'T>, f: 'T -> SchemaParser<'U>) : SchemaParser<'U>
    member _.Return(value: 'T) : SchemaParser<'T>
    member _.Run(parser: SchemaParser<'T>) : Schema<'T>

let schema = SchemaBuilder()
```

The CE accumulates field parsers. Each `let!` binds a field from the JSON. `Return` assembles the final typed value. `Run` produces a `Schema<'T>`.

Internally, `SchemaParser<'T>` is `JsonElement -> Result<'T, string list>` plus a list of `FieldSpec` for JSON Schema generation.

## Rules

Rules are functions that validate a parsed value:

```fsharp
type Rule = {
    Validate: obj -> Result<unit, string>
    Spec: RuleSpec  // for JSON Schema generation
}

let minLength (len: int) = {
    Validate = fun v ->
        let s = v :?> string
        if s.Length >= len then Ok ()
        else Error $"must be at least {len} characters"
    Spec = MinLength len
}
```

Rules are applied after parsing, before returning. Errors include the field name as prefix.

## Examples

### Simple

```fsharp
let createTodo = schema {
    let! title     = Schema.required "title" Schema.string [ Schema.minLength 3; Schema.maxLength 100 ]
    let! tags      = Schema.optional "tags" (Schema.list Schema.string) []
    let! completed = Schema.optional "completed" Schema.bool false
    return {| Title = title; Tags = tags; Completed = completed |}
}
```

### Nested

```fsharp
let address = schema {
    let! street = Schema.required "street" Schema.string []
    let! city   = Schema.required "city" Schema.string []
    let! zip    = Schema.required "zip" Schema.string [ Schema.pattern @"^\d{5}$" ]
    return {| Street = street; City = city; Zip = zip |}
}

let createUser = schema {
    let! name    = Schema.required "name" Schema.string [ Schema.minLength 1 ]
    let! email   = Schema.required "email" Schema.string [ Schema.email ]
    let! address = Schema.required "address" (Schema.nest address) []
    return {| Name = name; Email = email; Address = address |}
}
```

### With enums

```fsharp
let createTask = schema {
    let! title    = Schema.required "title" Schema.string [ Schema.minLength 1 ]
    let! priority = Schema.optional "priority" Schema.string "medium" [ Schema.enum' ["low"; "medium"; "high"] ]
    return {| Title = title; Priority = priority |}
}
```

### Cross-field validation

```fsharp
let passwordSchema = schema {
    let! password = Schema.required "password" Schema.string []
    let! confirm  = Schema.required "confirm" Schema.string []
    do! Schema.check (fun () ->
        if password = confirm then Ok ()
        else Error "confirm: must match password"
    )
    return {| Password = password |}
}
```

### Auto-generated from types

`Schema.fromType<'T>()` introspects F# record fields. Option fields become optional, nested records and typed lists are handled recursively:

```fsharp
type Address = { Street: string; Zip: string }
type User = { Name: string; Age: int; Address: Address; Tags: string list; Nickname: string option }

let userSchema = Schema.fromType<User>()
// Name: required string, Age: required int, Address: required nested object,
// Tags: required array of strings, Nickname: optional string
```

### Parse from lookup (zero-alloc for query strings)

```fsharp
let querySchema = schema {
    let! page = Schema.optional "page" Schema.int 1 []
    let! limit = Schema.optional "limit" Schema.int 20 []
    return {| Page = page; Limit = limit |}
}

// Parse from query string without JSON allocation
Schema.parseLookup querySchema (fun name -> req.QueryParam name)
```

## Fire Route Integration

Schemas slot into Route.post/put/patch as a parameter before the handler:

```fsharp
Route.post "/todos" createTodo (fun (store: ITodoStore) todo -> task {
    let! created = store.Create(todo.Title)
    return Response.json created |> Response.status 201
})

Route.put "/todos/%i" updateTodo (fun (store: ITodoStore) id todo -> task {
    let! updated = store.Update(id, todo)
    return Response.json updated
})
```

Fire reads the body stream, runs `Schema.parseStream`, returns 400 with errors if invalid, calls handler with typed value if valid.

## OpenAPI Integration

Since schemas carry field specs (names, types, rules), OpenAPI generation becomes automatic:

```fsharp
// Schema.toJsonSchema produces standard JSON Schema
let jsonSchema = Schema.toJsonSchema createTodo
// { "type": "object", "required": ["title"], "properties": { "title": { "type": "string", "minLength": 3 }, ... } }

// OpenApi.generate can use schemas for request body specs
```

## Error Format

Validation errors include field paths:

```json
{
    "errors": [
        "title: must be at least 3 characters",
        "email: invalid email format",
        "address.zip: must match pattern ^\\d{5}$"
    ]
}
```

Nested field errors use dot notation.

## Files

- `src/Flame/Schema.fs` — standalone Flame library: SchemaCompiler, Schema<'T>, SchemaBuilder CE, type parsers, rules, all parse functions, toJsonSchema, fromType
- `src/Fire/Schema.fs` — Fire web integration: `Schema.validated` handler wrapper, `parseQuery`, `parseParams`, `parseFormRequest`

## Implementation Notes

- **Two parsing paths**: fast zero-alloc `Utf8JsonReader` path (default for `parseString`/`parseBuffer`) and `JsonElement` fallback (used when cross-field checks are present, or via `parseJson`)
- **SchemaCompiler module**: compiled `FieldType` discriminated union (`FString`, `FInt`, `FBool`, `FFloat`, `FDateTime`, `FDateTimeOffset`, `FStringList`, `FNullable`, `FNested`, `FList`) with `ArrayPool<obj>` for zero-alloc value storage
- The CE accumulates `FieldSpecs` for JSON Schema generation alongside the parser
- Rules are applied after type parsing — parse first, validate/transform second
- Error collection is non-short-circuiting — all field errors returned at once
- `fromType<'T>()` uses F# reflection (`FSharpType.GetRecordFields`, `PreComputeRecordConstructor`) to recursively build schemas for records, including nested records, option types, and typed lists
- `parseLookup` is zero-allocation — converts directly from string values without JSON intermediate
- Nested errors use dot notation (`address.zip: ...`), list errors use bracket notation (`items.[0]: ...`)
