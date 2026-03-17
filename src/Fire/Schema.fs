namespace Fire

open System
open System.Buffers
open System.IO
open System.IO.Pipelines
open System.Text.Json
open System.Collections.Concurrent

// Rule specification for JSON Schema output
type RuleSpec =
    | MinLength of int
    | MaxLength of int
    | Pattern of string
    | Min of float
    | Max of float
    | Format of string
    | Enum of string list

// Field specification for JSON Schema output
type FieldSpec = {
    Name: string
    Type: string
    Required: bool
    Rules: RuleSpec list
    Items: FieldSpec option      // for arrays
    Children: FieldSpec list     // for nested objects
}

// A rule validates a parsed value
type Rule = {
    Check: string -> obj -> Result<unit, string>  // fieldName -> value -> result
    Spec: RuleSpec
}

// --- Compiled parser types (zero-alloc via Utf8JsonReader) ---

module SchemaCompiler =

    type FieldType =
        | FString
        | FInt
        | FBool
        | FFloat
        | FStringList
        | FNullable of FieldType
        | FNested of CompiledField[] * int[] * (obj[] -> obj)  // fields, paramMapping, constructor

    and CompiledField = {
        Name: string
        Type: FieldType
        Required: bool
        DefaultValue: obj
        Rules: Rule list
    }

    /// Registry for nested schema compiled info, keyed by parser delegate identity
    let internal nestedRegistry = ConcurrentDictionary<obj, FieldType>()

    /// Registry for nested schema field specs, keyed by parser delegate identity
    let internal nestedSpecRegistry = ConcurrentDictionary<obj, FieldSpec list>()

    let rec readValue (fieldType: FieldType) (reader: byref<Utf8JsonReader>) : obj =
        match fieldType with
        | FString -> box (reader.GetString())
        | FInt -> box (reader.GetInt32())
        | FBool -> box (reader.GetBoolean())
        | FFloat -> box (reader.GetDouble())
        | FStringList ->
            let items = ResizeArray<string>()
            // reader should be at StartArray
            while reader.Read() && reader.TokenType <> JsonTokenType.EndArray do
                if reader.TokenType = JsonTokenType.String then
                    items.Add(reader.GetString())
            box (items |> Seq.toList)
        | FNullable inner ->
            if reader.TokenType = JsonTokenType.Null then
                box None
            else
                let v = readValue inner &reader
                let innerType =
                    match inner with
                    | FString -> typeof<string>
                    | FInt -> typeof<int>
                    | FBool -> typeof<bool>
                    | FFloat -> typeof<float>
                    | FStringList -> typeof<string list>
                    | _ -> v.GetType()
                let optionType = typedefof<_ option>.MakeGenericType(innerType)
                let someCase = FSharp.Reflection.FSharpType.GetUnionCases(optionType).[1]
                FSharp.Reflection.FSharpValue.MakeUnion(someCase, [| v |])
        | FNested (nestedFields, nestedParamMapping, nestedCtor) ->
            parseObject nestedFields nestedParamMapping nestedCtor &reader

    and parseObject (fields: CompiledField[]) (paramMapping: int[]) (construct: obj[] -> obj) (reader: byref<Utf8JsonReader>) : obj =
        let values = Array.zeroCreate fields.Length
        let found = Array.zeroCreate<bool> fields.Length

        if reader.TokenType <> JsonTokenType.StartObject then
            reader.Read() |> ignore

        while reader.Read() && reader.TokenType <> JsonTokenType.EndObject do
            if reader.TokenType = JsonTokenType.PropertyName then
                let propName = reader.GetString()
                let mutable fieldIdx = -1
                for i in 0..fields.Length-1 do
                    if String.Equals(fields.[i].Name, propName, StringComparison.OrdinalIgnoreCase) then
                        fieldIdx <- i
                if fieldIdx >= 0 then
                    reader.Read() |> ignore
                    values.[fieldIdx] <- readValue fields.[fieldIdx].Type &reader
                    found.[fieldIdx] <- true
                else
                    reader.Read() |> ignore
                    reader.Skip()

        for i in 0..fields.Length-1 do
            if not found.[i] && not fields.[i].Required then
                values.[i] <- fields.[i].DefaultValue

        let args = paramMapping |> Array.map (fun idx -> values.[idx])
        construct args

    let parseAndValidate (fields: CompiledField[]) (paramMapping: int[]) (construct: obj[] -> 'T) (reader: byref<Utf8JsonReader>) : Result<'T, string list> =
        let values = Array.zeroCreate fields.Length
        let found = Array.zeroCreate<bool> fields.Length
        let errors = ResizeArray<string>()

        // Advance to StartObject if needed
        if reader.TokenType <> JsonTokenType.StartObject then
            if not (reader.Read()) || reader.TokenType <> JsonTokenType.StartObject then
                errors.Add("expected JSON object")

        if errors.Count = 0 then
            while reader.Read() && reader.TokenType <> JsonTokenType.EndObject do
                if reader.TokenType = JsonTokenType.PropertyName then
                    let propName = reader.GetString()
                    let mutable fieldIdx = -1
                    for i in 0..fields.Length-1 do
                        if String.Equals(fields.[i].Name, propName, StringComparison.OrdinalIgnoreCase) then
                            fieldIdx <- i
                    if fieldIdx >= 0 then
                        reader.Read() |> ignore
                        try
                            values.[fieldIdx] <- readValue fields.[fieldIdx].Type &reader
                            found.[fieldIdx] <- true
                        with ex ->
                            errors.Add($"{fields.[fieldIdx].Name}: {ex.Message}")
                    else
                        reader.Read() |> ignore
                        reader.Skip()

        // Check required + defaults + validate rules
        for i in 0..fields.Length-1 do
            if not found.[i] then
                if fields.[i].Required then
                    errors.Add($"{fields.[i].Name} is required")
                else
                    values.[i] <- fields.[i].DefaultValue
            else
                for rule in fields.[i].Rules do
                    match rule.Check fields.[i].Name values.[i] with
                    | Ok () -> ()
                    | Error e -> errors.Add(e)

        if errors.Count > 0 then
            Error (errors |> Seq.toList)
        else
            let args = paramMapping |> Array.map (fun idx -> values.[idx])
            Ok (construct args)

    let parseFromBuffer (fields: CompiledField[]) (paramMapping: int[]) (construct: obj[] -> 'T) (buffer: ReadOnlySequence<byte>) : Result<'T, string list> =
        try
            let mutable reader = Utf8JsonReader(buffer)
            parseAndValidate fields paramMapping construct &reader
        with ex ->
            Error [$"invalid JSON: {ex.Message}"]

// A schema field: parses one field from JsonElement
type SchemaField<'T> = {
    Name: string
    Parse: JsonElement -> Result<'T, string list>
    Spec: FieldSpec
    Compiled: SchemaCompiler.CompiledField
}

// Internal: parser + accumulated specs + compiled fields
type SchemaParser<'T> = {
    Parse: JsonElement -> Result<'T, string list>
    Specs: FieldSpec list
    CompiledFields: SchemaCompiler.CompiledField list
}

// The public schema type
type Schema<'T> = {
    Parse: JsonElement -> Result<'T, string list>
    ParseBuffer: ReadOnlySequence<byte> -> Result<'T, string list>
    Fields: FieldSpec list
}

[<RequireQualifiedAccess>]
module Schema =

    // --- Rules ---

    let minLength (len: int) : Rule = {
        Check = fun name v ->
            if (v :?> string).Length >= len then Ok ()
            else Error $"{name}: must be at least {len} characters"
        Spec = MinLength len
    }

    let maxLength (len: int) : Rule = {
        Check = fun name v ->
            if (v :?> string).Length <= len then Ok ()
            else Error $"{name}: must be at most {len} characters"
        Spec = MaxLength len
    }

    let pattern (regex: string) : Rule = {
        Check = fun name v ->
            if Text.RegularExpressions.Regex.IsMatch(v :?> string, regex) then Ok ()
            else Error $"{name}: must match pattern {regex}"
        Spec = Pattern regex
    }

    let min (n: float) : Rule = {
        Check = fun name v ->
            let d = Convert.ToDouble(v)
            if d >= n then Ok () else Error $"{name}: must be at least {n}"
        Spec = Min n
    }

    let max (n: float) : Rule = {
        Check = fun name v ->
            let d = Convert.ToDouble(v)
            if d <= n then Ok () else Error $"{name}: must be at most {n}"
        Spec = Max n
    }

    let email : Rule = {
        Check = fun name v ->
            let s = v :?> string
            if s.Contains("@") && s.Contains(".") then Ok ()
            else Error $"{name}: invalid email format"
        Spec = Format "email"
    }

    let url : Rule = {
        Check = fun name v ->
            let s = v :?> string
            if s.StartsWith("http://") || s.StartsWith("https://") then Ok ()
            else Error $"{name}: invalid URL format"
        Spec = Format "uri"
    }

    let enum' (values: string list) : Rule = {
        Check = fun name v ->
            let s = v :?> string
            if values |> List.contains s then Ok ()
            else
                let joined = String.Join(", ", values)
                Error $"{name}: must be one of {joined}"
        Spec = Enum values
    }

    // --- Apply rules to a parsed value ---

    let private applyRules (name: string) (rules: Rule list) (value: obj) : Result<unit, string list> =
        let errors = rules |> List.choose (fun r ->
            match r.Check name value with
            | Ok () -> None
            | Error e -> Some e)
        if errors.IsEmpty then Ok () else Error errors

    // --- Type parsers ---

    let string (el: JsonElement) : Result<string, string> =
        try Ok (el.GetString()) with _ -> Error "expected string"

    let int (el: JsonElement) : Result<int, string> =
        try Ok (el.GetInt32()) with _ -> Error "expected integer"

    let bool (el: JsonElement) : Result<bool, string> =
        try Ok (el.GetBoolean()) with _ -> Error "expected boolean"

    let float (el: JsonElement) : Result<float, string> =
        try Ok (el.GetDouble()) with _ -> Error "expected number"

    let list (itemParser: JsonElement -> Result<'T, string>) (el: JsonElement) : Result<'T list, string> =
        try
            let items = el.EnumerateArray() |> Seq.toList
            let results = items |> List.mapi (fun i item ->
                match itemParser item with
                | Ok v -> Ok v
                | Error e -> Error $"[{i}]: {e}")
            let errors = results |> List.choose (function Error e -> Some e | _ -> None)
            if errors.IsEmpty then
                Ok (results |> List.choose (function Ok v -> Some v | _ -> None))
            else
                Error (errors |> String.concat "; ")
        with _ -> Error "expected array"

    let nullable (parser: JsonElement -> Result<'T, string>) (el: JsonElement) : Result<'T option, string> =
        if el.ValueKind = JsonValueKind.Null then Ok None
        else parser el |> Result.map Some

    /// Build a compiled FieldType for a nested schema
    let private buildNestedFieldType<'T> (nestedSchema: Schema<'T>) : SchemaCompiler.FieldType =
        let ctor = typeof<'T>.GetConstructors().[0]
        let ctorParams = ctor.GetParameters()

        // Reconstruct compiled fields from the nested schema's FieldSpecs
        // For nested schemas built via the CE, the fields carry enough type info
        let compiledFields =
            nestedSchema.Fields |> List.map (fun spec ->
                let fieldType =
                    match spec.Type with
                    | "string" -> SchemaCompiler.FString
                    | "integer" -> SchemaCompiler.FInt
                    | "boolean" -> SchemaCompiler.FBool
                    | "number" -> SchemaCompiler.FFloat
                    | _ -> SchemaCompiler.FString
                {
                    SchemaCompiler.CompiledField.Name = spec.Name
                    SchemaCompiler.CompiledField.Type = fieldType
                    SchemaCompiler.CompiledField.Required = spec.Required
                    SchemaCompiler.CompiledField.DefaultValue = null
                    SchemaCompiler.CompiledField.Rules = []
                }
            ) |> Array.ofList

        let paramMapping =
            ctorParams |> Array.map (fun p ->
                compiledFields |> Array.findIndex (fun f ->
                    System.String.Equals(f.Name, p.Name, StringComparison.OrdinalIgnoreCase)))

        let construct (args: obj[]) = ctor.Invoke(args) |> box

        SchemaCompiler.FNested (compiledFields, paramMapping, construct)

    /// Creates a nested parser and registers its compiled FieldType for the buffer path.
    /// Usage: Schema.required "address" (Schema.nest addressSchema) []
    let nest (nestedSchema: Schema<'T>) : (JsonElement -> Result<'T, string>) =
        let fieldType = buildNestedFieldType nestedSchema
        let parser = fun (el: JsonElement) ->
            match nestedSchema.Parse el with
            | Ok v -> Ok v
            | Error errs -> Error (errs |> String.concat "; ")
        // Register the compiled field type for this specific parser closure
        SchemaCompiler.nestedRegistry.TryAdd(box parser, fieldType) |> ignore
        SchemaCompiler.nestedSpecRegistry.TryAdd(box parser, nestedSchema.Fields) |> ignore
        parser

    // --- Infer compiled FieldType from CLR type, checking nested registry ---

    let private inferFieldType<'T> (parser: JsonElement -> Result<'T, string>) : SchemaCompiler.FieldType =
        match SchemaCompiler.nestedRegistry.TryGetValue(box parser) with
        | true, fieldType -> fieldType
        | _ ->
            if typeof<'T> = typeof<string> then SchemaCompiler.FString
            elif typeof<'T> = typeof<int> then SchemaCompiler.FInt
            elif typeof<'T> = typeof<bool> then SchemaCompiler.FBool
            elif typeof<'T> = typeof<float> then SchemaCompiler.FFloat
            elif typeof<'T> = typeof<string list> then SchemaCompiler.FStringList
            elif typeof<'T>.IsGenericType && typeof<'T>.GetGenericTypeDefinition() = typedefof<_ option> then
                let innerType = typeof<'T>.GetGenericArguments().[0]
                let inner =
                    if innerType = typeof<string> then SchemaCompiler.FString
                    elif innerType = typeof<int> then SchemaCompiler.FInt
                    elif innerType = typeof<bool> then SchemaCompiler.FBool
                    elif innerType = typeof<float> then SchemaCompiler.FFloat
                    else SchemaCompiler.FString
                SchemaCompiler.FNullable inner
            else SchemaCompiler.FString // fallback

    // --- Field builders ---

    let required (name: string) (parser: JsonElement -> Result<'T, string>) (rules: Rule list) : SchemaField<'T> =
        let children =
            match SchemaCompiler.nestedSpecRegistry.TryGetValue(box parser) with
            | true, specs -> specs
            | _ -> []
        let spec = {
            Name = name
            Type =
                if typeof<'T> = typeof<string> then "string"
                elif typeof<'T> = typeof<int> then "integer"
                elif typeof<'T> = typeof<bool> then "boolean"
                elif typeof<'T> = typeof<float> then "number"
                else "object"
            Required = true
            Rules = rules |> List.map (fun r -> r.Spec)
            Items = None
            Children = children
        }
        {
            Name = name
            Spec = spec
            Compiled = {
                Name = name
                Type = inferFieldType parser
                Required = true
                DefaultValue = null
                Rules = rules
            }
            Parse = fun json ->
                match json.TryGetProperty(name) with
                | true, el when el.ValueKind <> JsonValueKind.Null ->
                    match parser el with
                    | Ok value ->
                        match applyRules name rules (box value) with
                        | Ok () -> Ok value
                        | Error errs -> Error errs
                    | Error e -> Error [$"{name}: {e}"]
                | _ -> Error [$"{name} is required"]
        }

    let optional (name: string) (parser: JsonElement -> Result<'T, string>) (defaultValue: 'T) (rules: Rule list) : SchemaField<'T> =
        let children =
            match SchemaCompiler.nestedSpecRegistry.TryGetValue(box parser) with
            | true, specs -> specs
            | _ -> []
        let spec = {
            Name = name
            Type =
                if typeof<'T> = typeof<string> then "string"
                elif typeof<'T> = typeof<int> then "integer"
                elif typeof<'T> = typeof<bool> then "boolean"
                elif typeof<'T> = typeof<float> then "number"
                else "object"
            Required = false
            Rules = rules |> List.map (fun r -> r.Spec)
            Items = None
            Children = children
        }
        {
            Name = name
            Spec = spec
            Compiled = {
                Name = name
                Type = inferFieldType parser
                Required = false
                DefaultValue = box defaultValue
                Rules = rules
            }
            Parse = fun json ->
                match json.TryGetProperty(name) with
                | true, el when el.ValueKind <> JsonValueKind.Null ->
                    match parser el with
                    | Ok value ->
                        match applyRules name rules (box value) with
                        | Ok () -> Ok value
                        | Error errs -> Error errs
                    | Error e -> Error [$"{name}: {e}"]
                | _ -> Ok defaultValue
        }

    // --- Parse from various sources ---

    let parseJson (schema: Schema<'T>) (el: JsonElement) : Result<'T, string list> =
        schema.Parse el

    let parseString (schema: Schema<'T>) (jsonString: string) : Result<'T, string list> =
        try
            let bytes = System.Text.Encoding.UTF8.GetBytes(jsonString)
            let buffer = ReadOnlySequence<byte>(bytes)
            schema.ParseBuffer buffer
        with ex ->
            Error [$"invalid JSON: {ex.Message}"]

    let parseBuffer (schema: Schema<'T>) (buffer: ReadOnlySequence<byte>) : Result<'T, string list> =
        schema.ParseBuffer buffer

    let parsePipe (schema: Schema<'T>) (pipeReader: PipeReader) : System.Threading.Tasks.Task<Result<'T, string list>> = task {
        let! readResult = pipeReader.ReadAsync()
        let buffer = readResult.Buffer
        let result = schema.ParseBuffer buffer
        pipeReader.AdvanceTo(buffer.End)
        return result
    }

    let parseStream (schema: Schema<'T>) (stream: Stream) : System.Threading.Tasks.Task<Result<'T, string list>> =
        task {
            try
                use! doc = JsonDocument.ParseAsync(stream)
                return schema.Parse doc.RootElement
            with ex ->
                return Error [$"invalid JSON: {ex.Message}"]
        }

    // --- Handler integration ---

    /// Wraps a handler with schema validation. Validates body via PipeReader (zero-alloc path).
    let validated (schema: Schema<'T>) (handler: 'T -> System.Threading.Tasks.Task<Response>) : Handler =
        fun req -> task {
            try
                let! readResult = req.Raw.Request.BodyReader.ReadAsync()
                let buffer = readResult.Buffer
                let result = schema.ParseBuffer buffer
                req.Raw.Request.BodyReader.AdvanceTo(buffer.End)
                match result with
                | Ok value -> return! handler value
                | Error errors -> return Response.json {| errors = errors |} |> Response.status 400
            with ex ->
                return Response.json {| errors = [$"invalid JSON: {ex.Message}"] |} |> Response.status 400
        }

    // --- JSON Schema generation ---

    let toJsonSchema (schema: Schema<'T>) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        let rec writeFieldSchema (writer: Utf8JsonWriter) (spec: FieldSpec) =
            writer.WriteStartObject(spec.Name)
            writer.WriteString("type", spec.Type)
            for rule in spec.Rules do
                match rule with
                | MinLength n -> writer.WriteNumber("minLength", n)
                | MaxLength n -> writer.WriteNumber("maxLength", n)
                | Pattern p -> writer.WriteString("pattern", p)
                | Min n -> writer.WriteNumber("minimum", n)
                | Max n -> writer.WriteNumber("maximum", n)
                | Format f -> writer.WriteString("format", f)
                | Enum values ->
                    writer.WriteStartArray("enum")
                    for v in values do writer.WriteStringValue(v)
                    writer.WriteEndArray()
            if spec.Children.Length > 0 then
                writer.WriteStartObject("properties")
                for child in spec.Children do
                    writeFieldSchema writer child
                writer.WriteEndObject()
                let required = spec.Children |> List.filter (fun c -> c.Required) |> List.map (fun c -> c.Name)
                if required.Length > 0 then
                    writer.WriteStartArray("required")
                    for r in required do writer.WriteStringValue(r)
                    writer.WriteEndArray()
            writer.WriteEndObject()

        writer.WriteStartObject()
        writer.WriteString("type", "object")

        writer.WriteStartObject("properties")
        for field in schema.Fields do
            writeFieldSchema writer field
        writer.WriteEndObject()

        let required = schema.Fields |> List.filter (fun f -> f.Required) |> List.map (fun f -> f.Name)
        if required.Length > 0 then
            writer.WriteStartArray("required")
            for r in required do writer.WriteStringValue(r)
            writer.WriteEndArray()

        writer.WriteEndObject()
        writer.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())


type SchemaBuilder() =
    member _.Bind(field: SchemaField<'T>, f: 'T -> SchemaParser<'U>) : SchemaParser<'U> =
        {
            Specs = field.Spec :: (f Unchecked.defaultof<'T>).Specs
            CompiledFields = field.Compiled :: (f Unchecked.defaultof<'T>).CompiledFields
            Parse = fun json ->
                match field.Parse json with
                | Ok value ->
                    match (f value).Parse json with
                    | Ok result -> Ok result
                    | Error errs -> Error errs
                | Error errs1 ->
                    match (f Unchecked.defaultof<'T>).Parse json with
                    | Ok _ -> Error errs1
                    | Error errs2 -> Error (errs1 @ errs2)
        }

    member _.Return(value: 'T) : SchemaParser<'T> =
        {
            Specs = []
            CompiledFields = []
            Parse = fun _ -> Ok value
        }

    member _.Run(parser: SchemaParser<'T>) : Schema<'T> =
        let compiledFields = parser.CompiledFields |> List.rev |> Array.ofList

        // Build parameter mapping: anonymous record constructors order params alphabetically
        let ctor = typeof<'T>.GetConstructors().[0]
        let ctorParams = ctor.GetParameters()
        let paramMapping =
            ctorParams |> Array.map (fun p ->
                compiledFields |> Array.findIndex (fun f ->
                    String.Equals(f.Name, p.Name, StringComparison.OrdinalIgnoreCase)))

        let construct (args: obj[]) =
            ctor.Invoke(args) :?> 'T

        {
            Parse = parser.Parse
            ParseBuffer = SchemaCompiler.parseFromBuffer compiledFields paramMapping construct
            Fields = parser.Specs |> List.rev
        }

[<AutoOpen>]
module SchemaBuilderModule =
    let schema = SchemaBuilder()
