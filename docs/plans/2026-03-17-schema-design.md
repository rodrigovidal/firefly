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
    | Pattern of string
    | Min of float
    | Max of float
    | Format of string  // "email", "uri", etc.
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

    // Rules — transform a parser to add validation
    let minLength (len: int) : Rule
    let maxLength (len: int) : Rule
    let pattern (regex: string) : Rule
    let min (n: float) : Rule
    let max (n: float) : Rule
    let email : Rule
    let url : Rule
    let enum' (values: string list) : Rule

    // Field builders — used inside schema CE
    let required (name: string) (parser) (rules: Rule list) : SchemaField<'T>
    let optional (name: string) (parser) (defaultValue: 'T) (rules: Rule list) : SchemaField<'T>

    // Parse from various sources
    let parseJson (schema: Schema<'T>) (json: JsonElement) : Result<'T, string list>
    let parseString (schema: Schema<'T>) (jsonString: string) : Result<'T, string list>
    let parseStream (schema: Schema<'T>) (stream: Stream) : Task<Result<'T, string list>>

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

- `src/Fire/Schema.fs` — Schema<'T>, SchemaBuilder CE, type parsers, rules, parseJson/parseStream, toJsonSchema

## Implementation Notes

- SchemaParser internally is `JsonElement -> Result<'T, string list> * FieldSpec list`
- The CE accumulates FieldSpecs for JSON Schema generation alongside the parser
- Rules are applied after type parsing — parse first, validate second
- Schema.parseStream reads the full body into a JsonDocument, then delegates to parseJson
- Error collection is non-short-circuiting — all field errors returned at once
