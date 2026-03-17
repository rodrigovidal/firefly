namespace Fire

open System
open System.Buffers
open System.Collections.Generic
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

// A rule validates (and optionally transforms) a parsed value
type Rule = {
    Apply: string -> obj -> Result<obj, string>  // fieldName -> value -> Result<transformedValue, error>
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

    let buildFieldIndex (fields: CompiledField[]) : Dictionary<string, int> =
        let d = Dictionary<string, int>(fields.Length, StringComparer.OrdinalIgnoreCase)
        for i in 0..fields.Length-1 do
            d.[fields.[i].Name] <- i
        d

    let rec readValue (fieldType: FieldType) (reader: byref<Utf8JsonReader>) : obj =
        match fieldType with
        | FString -> box (reader.GetString())
        | FInt ->
            if reader.TokenType = JsonTokenType.Number then
                box (reader.GetInt32())
            elif reader.TokenType = JsonTokenType.String then
                match System.Int32.TryParse(reader.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture) with
                | true, n -> box n
                | false, _ -> failwith "expected integer"
            else failwith "expected integer"
        | FBool ->
            if reader.TokenType = JsonTokenType.True || reader.TokenType = JsonTokenType.False then
                box (reader.GetBoolean())
            elif reader.TokenType = JsonTokenType.String then
                match System.Boolean.TryParse(reader.GetString()) with
                | true, b -> box b
                | false, _ -> failwith "expected boolean"
            else failwith "expected boolean"
        | FFloat ->
            if reader.TokenType = JsonTokenType.Number then
                box (reader.GetDouble())
            elif reader.TokenType = JsonTokenType.String then
                match System.Double.TryParse(reader.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture) with
                | true, n -> box n
                | false, _ -> failwith "expected number"
            else failwith "expected number"
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
        let fieldIndex = buildFieldIndex fields
        let values = Array.zeroCreate fields.Length
        let found = Array.zeroCreate<bool> fields.Length

        if reader.TokenType <> JsonTokenType.StartObject then
            reader.Read() |> ignore

        while reader.Read() && reader.TokenType <> JsonTokenType.EndObject do
            if reader.TokenType = JsonTokenType.PropertyName then
                let propName = reader.GetString()
                match fieldIndex.TryGetValue(propName) with
                | true, fieldIdx ->
                    reader.Read() |> ignore
                    values.[fieldIdx] <- readValue fields.[fieldIdx].Type &reader
                    found.[fieldIdx] <- true
                | false, _ ->
                    reader.Read() |> ignore
                    reader.Skip()

        for i in 0..fields.Length-1 do
            if not found.[i] && not fields.[i].Required then
                values.[i] <- fields.[i].DefaultValue

        let args = paramMapping |> Array.map (fun idx -> values.[idx])
        construct args

    let parseAndValidate (fields: CompiledField[]) (paramMapping: int[]) (construct: obj[] -> 'T) (fieldIndex: Dictionary<string, int>) (reader: byref<Utf8JsonReader>) : Result<'T, string list> =
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
                    match fieldIndex.TryGetValue(propName) with
                    | true, fieldIdx ->
                        reader.Read() |> ignore
                        try
                            values.[fieldIdx] <- readValue fields.[fieldIdx].Type &reader
                            found.[fieldIdx] <- true
                        with ex ->
                            errors.Add($"{fields.[fieldIdx].Name}: {ex.Message}")
                    | false, _ ->
                        reader.Read() |> ignore
                        reader.Skip()

        // Check required + defaults + validate/transform rules
        for i in 0..fields.Length-1 do
            if not found.[i] then
                if fields.[i].Required then
                    errors.Add($"{fields.[i].Name} is required")
                else
                    values.[i] <- fields.[i].DefaultValue
            else
                let mutable current = values.[i]
                for rule in fields.[i].Rules do
                    match rule.Apply fields.[i].Name current with
                    | Ok transformed -> current <- transformed
                    | Error e -> errors.Add(e)
                values.[i] <- current

        if errors.Count > 0 then
            Error (errors |> Seq.toList)
        else
            let args = paramMapping |> Array.map (fun idx -> values.[idx])
            Ok (construct args)

    let parseFromBuffer (fields: CompiledField[]) (paramMapping: int[]) (construct: obj[] -> 'T) (fieldIndex: Dictionary<string, int>) (buffer: ReadOnlySequence<byte>) : Result<'T, string list> =
        try
            let mutable reader = Utf8JsonReader(buffer)
            parseAndValidate fields paramMapping construct fieldIndex &reader
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
    CompiledFields: SchemaCompiler.CompiledField[]
    ParamMapping: int[]
}

[<RequireQualifiedAccess>]
module Schema =

    // --- Rules ---

    let minLength (len: int) : Rule = {
        Apply = fun name v ->
            let s = v :?> string
            if s.Length >= len then Ok (box s)
            else Error $"{name}: must be at least {len} characters"
        Spec = MinLength len
    }

    let maxLength (len: int) : Rule = {
        Apply = fun name v ->
            let s = v :?> string
            if s.Length <= len then Ok (box s)
            else Error $"{name}: must be at most {len} characters"
        Spec = MaxLength len
    }

    let pattern (regex: string) : Rule = {
        Apply = fun name v ->
            let s = v :?> string
            if Text.RegularExpressions.Regex.IsMatch(s, regex) then Ok (box s)
            else Error $"{name}: must match pattern {regex}"
        Spec = Pattern regex
    }

    let min (n: float) : Rule = {
        Apply = fun name v ->
            let d = Convert.ToDouble(v)
            if d >= n then Ok v else Error $"{name}: must be at least {n}"
        Spec = Min n
    }

    let max (n: float) : Rule = {
        Apply = fun name v ->
            let d = Convert.ToDouble(v)
            if d <= n then Ok v else Error $"{name}: must be at most {n}"
        Spec = Max n
    }

    let email : Rule = {
        Apply = fun name v ->
            let s = v :?> string
            if s.Contains("@") && s.Contains(".") then Ok (box s)
            else Error $"{name}: invalid email format"
        Spec = Format "email"
    }

    let url : Rule = {
        Apply = fun name v ->
            let s = v :?> string
            if s.StartsWith("http://") || s.StartsWith("https://") then Ok (box s)
            else Error $"{name}: invalid URL format"
        Spec = Format "uri"
    }

    let enum' (values: string list) : Rule = {
        Apply = fun name v ->
            let s = v :?> string
            if values |> List.contains s then Ok (box s)
            else
                let joined = String.Join(", ", values)
                Error $"{name}: must be one of {joined}"
        Spec = Enum values
    }

    // --- Transform rules ---

    let trim : Rule = {
        Apply = fun _name v -> Ok (box ((v :?> string).Trim()))
        Spec = Format "trimmed"
    }

    let lowercase : Rule = {
        Apply = fun _name v -> Ok (box ((v :?> string).ToLowerInvariant()))
        Spec = Format "lowercase"
    }

    let uppercase : Rule = {
        Apply = fun _name v -> Ok (box ((v :?> string).ToUpperInvariant()))
        Spec = Format "uppercase"
    }

    // --- Apply rules to a parsed value ---

    let private applyRules (name: string) (rules: Rule list) (value: obj) : Result<obj, string list> =
        let mutable current = value
        let errors = ResizeArray()
        for rule in rules do
            match rule.Apply name current with
            | Ok transformed -> current <- transformed
            | Error e -> errors.Add(e)
        if errors.Count > 0 then Error (errors |> Seq.toList)
        else Ok current

    // --- Type parsers ---

    let string (el: JsonElement) : Result<string, string> =
        try Ok (el.GetString()) with _ -> Error "expected string"

    let int (el: JsonElement) : Result<int, string> =
        try
            if el.ValueKind = JsonValueKind.Number then Ok (el.GetInt32())
            elif el.ValueKind = JsonValueKind.String then
                match System.Int32.TryParse(el.GetString(), Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture) with
                | true, n -> Ok n
                | false, _ -> Error "expected integer"
            else Error "expected integer"
        with _ -> Error "expected integer"

    let bool (el: JsonElement) : Result<bool, string> =
        try
            if el.ValueKind = JsonValueKind.True || el.ValueKind = JsonValueKind.False then Ok (el.GetBoolean())
            elif el.ValueKind = JsonValueKind.String then
                match System.Boolean.TryParse(el.GetString()) with
                | true, b -> Ok b
                | false, _ -> Error "expected boolean"
            else Error "expected boolean"
        with _ -> Error "expected boolean"

    let float (el: JsonElement) : Result<float, string> =
        try
            if el.ValueKind = JsonValueKind.Number then Ok (el.GetDouble())
            elif el.ValueKind = JsonValueKind.String then
                match System.Double.TryParse(el.GetString(), Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture) with
                | true, n -> Ok n
                | false, _ -> Error "expected number"
            else Error "expected number"
        with _ -> Error "expected number"

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

    /// Build a compiled FieldType for a nested schema, preserving compiled fields and rules
    let private buildNestedFieldType<'T> (nestedSchema: Schema<'T>) : SchemaCompiler.FieldType =
        let ctor = typeof<'T>.GetConstructors().[0]
        let construct (args: obj[]) = ctor.Invoke(args) |> box
        SchemaCompiler.FNested (nestedSchema.CompiledFields, nestedSchema.ParamMapping, construct)

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
                    elif innerType = typeof<string list> then SchemaCompiler.FStringList
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
                        | Ok transformed -> Ok (transformed :?> 'T)
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
                        | Ok transformed -> Ok (transformed :?> 'T)
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
        let mutable buffer = ReadOnlySequence<byte>.Empty
        let mutable isDone = false
        while not isDone do
            let! readResult = pipeReader.ReadAsync()
            buffer <- readResult.Buffer
            if readResult.IsCompleted then
                isDone <- true
            else
                // Need more data - examine what we have but don't consume
                pipeReader.AdvanceTo(buffer.Start, buffer.End)
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
                let reader = req.Raw.Request.BodyReader
                let mutable buffer = ReadOnlySequence<byte>.Empty
                let mutable isDone = false
                while not isDone do
                    let! readResult = reader.ReadAsync()
                    buffer <- readResult.Buffer
                    if readResult.IsCompleted then
                        isDone <- true
                    else
                        reader.AdvanceTo(buffer.Start, buffer.End)
                let result = schema.ParseBuffer buffer
                reader.AdvanceTo(buffer.End)
                match result with
                | Ok value -> return! handler value
                | Error errors -> return Response.json {| errors = errors |} |> Response.status 400
            with ex ->
                return Response.json {| errors = [$"invalid JSON: {ex.Message}"] |} |> Response.status 400
        }

    /// Parse and validate the request body using a schema. Returns Result.
    /// Use inside handlers with auto-DI:
    /// Route.post "/todos" (fun (store: ITodoStore) (req: Request) -> task {
    ///     match! Schema.parseRequest createTodoSchema req with
    ///     | Ok todo -> ...
    ///     | Error errors -> ...
    /// })
    let parseRequest (schema: Schema<'T>) (req: Request) : System.Threading.Tasks.Task<Result<'T, string list>> = task {
        try
            let reader = req.Raw.Request.BodyReader
            let mutable buffer = Buffers.ReadOnlySequence<byte>.Empty
            let mutable isDone = false
            while not isDone do
                let! readResult = reader.ReadAsync()
                buffer <- readResult.Buffer
                if readResult.IsCompleted then
                    isDone <- true
                else
                    reader.AdvanceTo(buffer.Start, buffer.End)
            let result = schema.ParseBuffer buffer
            reader.AdvanceTo(buffer.End)
            return result
        with ex ->
            return Error [$"invalid JSON: {ex.Message}"]
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

    // --- Auto-generate schema from F# record type ---

    let inline fromType<'T> () : Schema<'T> =
        let t = typeof<'T>
        let fields = FSharp.Reflection.FSharpType.GetRecordFields(t)
        let ctor = FSharp.Reflection.FSharpValue.PreComputeRecordConstructor(t)

        // Build compiled fields
        let compiledFields = fields |> Array.map (fun prop ->
            let fieldType, isRequired, defaultVal =
                let propType = prop.PropertyType
                if propType.IsGenericType && propType.GetGenericTypeDefinition() = typedefof<_ option> then
                    let innerType = propType.GetGenericArguments().[0]
                    let ft =
                        if innerType = typeof<string> then SchemaCompiler.FString
                        elif innerType = typeof<int> then SchemaCompiler.FInt
                        elif innerType = typeof<bool> then SchemaCompiler.FBool
                        elif innerType = typeof<float> then SchemaCompiler.FFloat
                        else SchemaCompiler.FString
                    SchemaCompiler.FNullable ft, false, (box None)
                else
                    let ft =
                        if propType = typeof<string> then SchemaCompiler.FString
                        elif propType = typeof<int> then SchemaCompiler.FInt
                        elif propType = typeof<bool> then SchemaCompiler.FBool
                        elif propType = typeof<float> then SchemaCompiler.FFloat
                        elif propType = typeof<string list> then SchemaCompiler.FStringList
                        else SchemaCompiler.FString
                    ft, true, null
            {
                SchemaCompiler.CompiledField.Name = prop.Name
                SchemaCompiler.CompiledField.Type = fieldType
                SchemaCompiler.CompiledField.Required = isRequired
                SchemaCompiler.CompiledField.DefaultValue = defaultVal
                SchemaCompiler.CompiledField.Rules = []
            }
        )

        // Build field index for O(1) lookup
        let fieldIndex = Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        for i in 0..compiledFields.Length-1 do
            fieldIndex.[compiledFields.[i].Name] <- i

        // Build param mapping (record constructor param order)
        let ctorParams = t.GetConstructors().[0].GetParameters()
        let paramMapping = ctorParams |> Array.map (fun p ->
            compiledFields |> Array.findIndex (fun f ->
                String.Equals(f.Name, p.Name, StringComparison.OrdinalIgnoreCase)))

        let construct (values: obj[]) =
            let args = paramMapping |> Array.map (fun idx -> values.[idx])
            ctor args :?> 'T

        // Build JsonElement parser for compat
        let parseElement (el: JsonElement) : Result<'T, string list> =
            let errors = ResizeArray<string>()
            let values = Array.zeroCreate compiledFields.Length
            for i in 0..compiledFields.Length-1 do
                let field = compiledFields.[i]
                match el.TryGetProperty(field.Name) with
                | true, prop when prop.ValueKind <> JsonValueKind.Null ->
                    try
                        values.[i] <-
                            match field.Type with
                            | SchemaCompiler.FString -> box (prop.GetString())
                            | SchemaCompiler.FInt ->
                                if prop.ValueKind = JsonValueKind.Number then box (prop.GetInt32())
                                elif prop.ValueKind = JsonValueKind.String then
                                    match System.Int32.TryParse(prop.GetString(), Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture) with
                                    | true, n -> box n
                                    | false, _ -> failwith "expected integer"
                                else failwith "expected integer"
                            | SchemaCompiler.FBool ->
                                if prop.ValueKind = JsonValueKind.True || prop.ValueKind = JsonValueKind.False then box (prop.GetBoolean())
                                elif prop.ValueKind = JsonValueKind.String then
                                    match System.Boolean.TryParse(prop.GetString()) with
                                    | true, b -> box b
                                    | false, _ -> failwith "expected boolean"
                                else failwith "expected boolean"
                            | SchemaCompiler.FFloat ->
                                if prop.ValueKind = JsonValueKind.Number then box (prop.GetDouble())
                                elif prop.ValueKind = JsonValueKind.String then
                                    match System.Double.TryParse(prop.GetString(), Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture) with
                                    | true, n -> box n
                                    | false, _ -> failwith "expected number"
                                else failwith "expected number"
                            | SchemaCompiler.FStringList ->
                                box (prop.EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> Seq.toList)
                            | SchemaCompiler.FNullable inner ->
                                let v = match inner with
                                        | SchemaCompiler.FString -> box (prop.GetString())
                                        | SchemaCompiler.FInt -> box (prop.GetInt32())
                                        | SchemaCompiler.FBool -> box (prop.GetBoolean())
                                        | SchemaCompiler.FFloat -> box (prop.GetDouble())
                                        | _ -> box (prop.GetString())
                                let innerType = match inner with
                                                | SchemaCompiler.FString -> typeof<string>
                                                | SchemaCompiler.FInt -> typeof<int>
                                                | SchemaCompiler.FBool -> typeof<bool>
                                                | SchemaCompiler.FFloat -> typeof<float>
                                                | _ -> typeof<string>
                                let optionType = typedefof<_ option>.MakeGenericType(innerType)
                                let someCase = FSharp.Reflection.FSharpType.GetUnionCases(optionType).[1]
                                FSharp.Reflection.FSharpValue.MakeUnion(someCase, [| v |])
                            | _ -> box (prop.GetString())
                    with ex ->
                        errors.Add($"{field.Name}: {ex.Message}")
                | _ ->
                    if field.Required then errors.Add($"{field.Name} is required")
                    else values.[i] <- field.DefaultValue
            if errors.Count > 0 then Error (errors |> Seq.toList)
            else Ok (construct values)

        // Build FieldSpecs for JSON Schema generation
        let fieldTypeToString (ft: SchemaCompiler.FieldType) =
            match ft with
            | SchemaCompiler.FString -> "string"
            | SchemaCompiler.FInt -> "integer"
            | SchemaCompiler.FBool -> "boolean"
            | SchemaCompiler.FFloat -> "number"
            | SchemaCompiler.FStringList -> "array"
            | SchemaCompiler.FNullable inner ->
                match inner with
                | SchemaCompiler.FString -> "string"
                | SchemaCompiler.FInt -> "integer"
                | SchemaCompiler.FBool -> "boolean"
                | SchemaCompiler.FFloat -> "number"
                | _ -> "string"
            | _ -> "string"

        let fieldSpecs =
            compiledFields |> Array.map (fun f ->
                {
                    FieldSpec.Name = f.Name
                    FieldSpec.Type = fieldTypeToString f.Type
                    FieldSpec.Required = f.Required
                    FieldSpec.Rules = []
                    FieldSpec.Items = None
                    FieldSpec.Children = []
                }
            ) |> Array.toList

        {
            Parse = parseElement
            ParseBuffer = SchemaCompiler.parseFromBuffer compiledFields paramMapping construct fieldIndex
            Fields = fieldSpecs
            CompiledFields = compiledFields
            ParamMapping = paramMapping
        }


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

        let fieldIndex = SchemaCompiler.buildFieldIndex compiledFields

        let construct (args: obj[]) =
            ctor.Invoke(args) :?> 'T

        {
            Parse = parser.Parse
            ParseBuffer = SchemaCompiler.parseFromBuffer compiledFields paramMapping construct fieldIndex
            Fields = parser.Specs |> List.rev
            CompiledFields = compiledFields
            ParamMapping = paramMapping
        }

[<AutoOpen>]
module SchemaBuilderModule =
    let schema = SchemaBuilder()
