# Zero-Allocation Schema Parser Design

Rewrite Schema internals to use `Utf8JsonReader` on `ReadOnlySequence<byte>` from Kestrel's pipe. No `JsonDocument` allocation. Same CE API surface.

## Current vs New

**Current (allocates):**
```
Request body → Stream → JsonDocument.Parse (ALLOCATES document + metadata)
→ JsonElement → Schema field parsers → Result<'T, errors>
```

**New (zero-copy):**
```
Request body → PipeReader.ReadAsync → ReadOnlySequence<byte> (Kestrel's buffer, no copy)
→ Utf8JsonReader (ref struct, stack-only) → compiled field matcher → Result<'T, errors>
```

Only allocations: string field values (unavoidable) + the output record.

## Architecture

The CE collects field definitions at build time. `Run` compiles them into a flat array of `CompiledField`. At parse time, a monolithic `Utf8JsonReader` loop walks the JSON and populates a pre-allocated value array.

```fsharp
type FieldType = String | Int | Bool | Float | StringList | Nested of ICompiledSchema

type CompiledField = {
    Name: string
    Type: FieldType
    Required: bool
    DefaultValue: obj
    Rules: Rule list
}

type Schema<'T> = {
    Fields: CompiledField[]
    Construct: obj[] -> 'T           // maps value array → output record
    ParseBuffer: ReadOnlySequence<byte> -> Result<'T, string list>
    ParseElement: JsonElement -> Result<'T, string list>  // fallback for tests
    FieldSpecs: FieldSpec list       // for JSON Schema generation
}
```

## Parse Loop (sync, zero-alloc except strings)

```fsharp
let parseBuffer (schema: Schema<'T>) (buffer: ReadOnlySequence<byte>) =
    let values = Array.zeroCreate schema.Fields.Length  // one array per parse
    let found = Array.zeroCreate<bool> schema.Fields.Length
    let mutable reader = Utf8JsonReader(buffer)

    // Expect start object
    reader.Read() |> ignore
    if reader.TokenType <> JsonTokenType.StartObject then
        Error ["expected JSON object"]
    else
        // Walk properties
        while reader.Read() && reader.TokenType <> JsonTokenType.EndObject do
            if reader.TokenType = JsonTokenType.PropertyName then
                let propName = reader.GetString()
                match findFieldIndex schema.Fields propName with
                | Some idx ->
                    reader.Read() |> ignore
                    values.[idx] <- readValue schema.Fields.[idx].Type &reader
                    found.[idx] <- true
                | None ->
                    reader.Skip()  // skip unknown property, no allocation

        // Check required + defaults + validate
        let errors = collectErrors schema.Fields values found
        if errors.IsEmpty then Ok (schema.Construct values)
        else Error errors
```

## PipeReader Integration

```fsharp
let parseFromPipe (schema: Schema<'T>) (bodyReader: PipeReader) = task {
    let! readResult = bodyReader.ReadAsync()
    let buffer = readResult.Buffer
    let result = schema.ParseBuffer buffer
    bodyReader.AdvanceTo(buffer.End)
    return result
}
```

`PipeReader` gives us the bytes Kestrel already read. No copy. `Utf8JsonReader` reads directly from that `ReadOnlySequence<byte>`.

## CE API (unchanged)

```fsharp
let createTodo = schema {
    let! title     = Schema.required "title" Schema.string [ Schema.minLength 3 ]
    let! completed = Schema.optional "completed" Schema.bool false []
    return {| Title = title; Completed = completed |}
}
```

The CE still works the same way. `Run` now compiles field definitions into the fast `ParseBuffer` function.

## Field Value Reading (per type)

```fsharp
let readValue (fieldType: FieldType) (reader: byref<Utf8JsonReader>) : obj =
    match fieldType with
    | String -> box (reader.GetString())           // one string allocation
    | Int -> box (reader.GetInt32())               // no allocation (boxed int)
    | Bool -> box (reader.GetBoolean())            // no allocation (boxed bool)
    | Float -> box (reader.GetDouble())            // no allocation (boxed float)
    | StringList ->
        let items = ResizeArray<string>()
        while reader.Read() && reader.TokenType <> JsonTokenType.EndArray do
            items.Add(reader.GetString())
        box (items |> Seq.toList)
    | Nested schema ->
        // Read nested object — recursive, still zero-copy from buffer
        schema.ParseCurrent(&reader)
```

## Allocation Comparison

| Operation | Current | New |
|-----------|---------|-----|
| JsonDocument.Parse | ~2KB+ per request | 0 |
| JsonElement metadata | proportional to JSON size | 0 |
| Field lookup | dictionary per field | array index |
| String values | allocated | allocated (unavoidable) |
| Output record | allocated | allocated (unavoidable) |
| Value array | N/A | one `obj[]` per parse (poolable) |
| PipeReader buffer | N/A | Kestrel-managed, no user alloc |

## Backward Compatibility

- `Schema.parseString` still works — wraps string in `ReadOnlySequence<byte>`
- `Schema.parseJson` (JsonElement) kept as fallback for tests
- `Schema.parseStream` uses PipeReader when available, falls back to reading stream
- CE API unchanged
- `Schema.toJsonSchema` unchanged
