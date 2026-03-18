namespace Flame

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
        | FDateTime
        | FDateTimeOffset
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
        | FDateTime ->
            if reader.TokenType = JsonTokenType.String then
                match DateTime.TryParse(reader.GetString()) with
                | true, dt -> box dt
                | false, _ -> failwith "expected date/time"
            else failwith "expected date/time"
        | FDateTimeOffset ->
            if reader.TokenType = JsonTokenType.String then
                match DateTimeOffset.TryParse(reader.GetString()) with
                | true, dto -> box dto
                | false, _ -> failwith "expected date/time with offset"
            else failwith "expected date/time with offset"
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
                    | FDateTime -> typeof<DateTime>
                    | FDateTimeOffset -> typeof<DateTimeOffset>
                    | FStringList -> typeof<string list>
                    | _ -> v.GetType()
                let optionType = typedefof<_ option>.MakeGenericType(innerType)
                let someCase = FSharp.Reflection.FSharpType.GetUnionCases(optionType).[1]
                FSharp.Reflection.FSharpValue.MakeUnion(someCase, [| v |])
        | FNested (nestedFields, nestedParamMapping, nestedCtor) ->
            parseObject nestedFields nestedParamMapping nestedCtor &reader

    and parseObject (fields: CompiledField[]) (paramMapping: int[]) (construct: obj[] -> obj) (reader: byref<Utf8JsonReader>) : obj =
        let fieldIndex = buildFieldIndex fields
        let values = ArrayPool<obj>.Shared.Rent(fields.Length)
        let found = Array.zeroCreate<bool> fields.Length
        try
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
        finally
            ArrayPool<obj>.Shared.Return(values, clearArray = true)

    let parseAndValidate (fields: CompiledField[]) (paramMapping: int[]) (construct: obj[] -> 'T) (fieldIndex: Dictionary<string, int>) (reader: byref<Utf8JsonReader>) : Result<'T, string list> =
        let values = ArrayPool<obj>.Shared.Rent(fields.Length)
        let found = Array.zeroCreate<bool> fields.Length
        let errors = ResizeArray<string>()
        try
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
                                match fields.[fieldIdx].Type with
                                | FNested _ -> errors.Add($"{fields.[fieldIdx].Name}.{ex.Message}")
                                | _ -> errors.Add($"{fields.[fieldIdx].Name}: {ex.Message}")
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
        finally
            ArrayPool<obj>.Shared.Return(values, clearArray = true)

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
    Refinements: ('T -> Result<'T, string list>) list
}

// The public schema type
type Schema<'T> = {
    Parse: JsonElement -> Result<'T, string list>
    ParseBuffer: ReadOnlySequence<byte> -> Result<'T, string list>
    Fields: FieldSpec list
    CompiledFields: SchemaCompiler.CompiledField[]
    ParamMapping: int[]
    Refinements: ('T -> Result<'T, string list>) list
}

/// Cross-field validation check. Use with `do!` inside a schema CE.
type SchemaCheck = {
    Validate: unit -> Result<unit, string>
}

[<RequireQualifiedAccess>]
module Schema =

    // --- Rules ---

    let private pluralize n word = if n = 1 then word else $"{word}s"

    let minLength (len: int) : Rule =
        let unit = pluralize len "character"
        {
            Apply = fun name v ->
                let s = v :?> string
                if s.Length >= len then Ok (box s)
                else Error $"{name}: must be at least {len} {unit}"
            Spec = MinLength len
        }

    let maxLength (len: int) : Rule =
        let unit = pluralize len "character"
        {
            Apply = fun name v ->
                let s = v :?> string
                if s.Length <= len then Ok (box s)
                else Error $"{name}: must be at most {len} {unit}"
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

    let dateTime (el: JsonElement) : Result<DateTime, string> =
        try
            if el.ValueKind = JsonValueKind.String then
                match DateTime.TryParse(el.GetString()) with
                | true, dt -> Ok dt
                | false, _ -> Error "expected date/time"
            else
                Ok (el.GetDateTime())
        with _ -> Error "expected date/time"

    let dateTimeOffset (el: JsonElement) : Result<DateTimeOffset, string> =
        try
            if el.ValueKind = JsonValueKind.String then
                match DateTimeOffset.TryParse(el.GetString()) with
                | true, dto -> Ok dto
                | false, _ -> Error "expected date/time with offset"
            else
                Ok (el.GetDateTimeOffset())
        with _ -> Error "expected date/time with offset"

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

    /// Sentinel prefix used to identify errors from nested schemas
    let internal nestedErrorSeparator = "\x00NESTED\x00"

    /// Creates a nested parser and registers its compiled FieldType for the buffer path.
    /// Usage: Schema.required "address" (Schema.nest addressSchema) []
    let nest (nestedSchema: Schema<'T>) : (JsonElement -> Result<'T, string>) =
        let fieldType = buildNestedFieldType nestedSchema
        let parser = fun (el: JsonElement) ->
            match nestedSchema.Parse el with
            | Ok v -> Ok v
            | Error errs -> Error (nestedErrorSeparator + (errs |> String.concat nestedErrorSeparator))
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
            elif typeof<'T> = typeof<DateTime> then SchemaCompiler.FDateTime
            elif typeof<'T> = typeof<DateTimeOffset> then SchemaCompiler.FDateTimeOffset
            elif typeof<'T> = typeof<string list> then SchemaCompiler.FStringList
            elif typeof<'T>.IsGenericType && typeof<'T>.GetGenericTypeDefinition() = typedefof<_ option> then
                let innerType = typeof<'T>.GetGenericArguments().[0]
                let inner =
                    if innerType = typeof<string> then SchemaCompiler.FString
                    elif innerType = typeof<int> then SchemaCompiler.FInt
                    elif innerType = typeof<bool> then SchemaCompiler.FBool
                    elif innerType = typeof<float> then SchemaCompiler.FFloat
                    elif innerType = typeof<DateTime> then SchemaCompiler.FDateTime
                    elif innerType = typeof<DateTimeOffset> then SchemaCompiler.FDateTimeOffset
                    elif innerType = typeof<string list> then SchemaCompiler.FStringList
                    else SchemaCompiler.FString
                SchemaCompiler.FNullable inner
            else SchemaCompiler.FString // fallback

    // --- Field builders ---

    /// Converts a parser error string into a list of errors with proper dotted paths for nested schemas.
    let private formatParserErrors (name: string) (e: string) : string list =
        if e.StartsWith(nestedErrorSeparator) then
            // Nested schema errors — split and prefix each with parent name + "."
            e.Split(nestedErrorSeparator, StringSplitOptions.RemoveEmptyEntries)
            |> Array.toList
            |> List.map (fun err -> $"{name}.{err}")
        else
            [$"{name}: {e}"]

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
                    | Error e -> Error (formatParserErrors name e)
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
                    | Error e -> Error (formatParserErrors name e)
                | _ -> Ok defaultValue
        }

    // --- Shorthand field builders (no rules) ---

    let req (name: string) (parser: JsonElement -> Result<'T, string>) : SchemaField<'T> =
        required name parser []

    let opt (name: string) (parser: JsonElement -> Result<'T, string>) (defaultValue: 'T) : SchemaField<'T> =
        optional name parser defaultValue []

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

    let private readPipeToBuffer (pipeReader: PipeReader) : System.Threading.Tasks.Task<ReadOnlySequence<byte>> = task {
        use stream = new MemoryStream()
        let mutable isDone = false
        while not isDone do
            let! readResult = pipeReader.ReadAsync()
            let chunk = readResult.Buffer.ToArray()
            stream.Write(chunk, 0, chunk.Length)
            pipeReader.AdvanceTo(readResult.Buffer.End)
            isDone <- readResult.IsCompleted
        return ReadOnlySequence<byte>(stream.ToArray())
    }

    let parsePipe (schema: Schema<'T>) (pipeReader: PipeReader) : System.Threading.Tasks.Task<Result<'T, string list>> = task {
        let! buffer = readPipeToBuffer pipeReader
        return schema.ParseBuffer buffer
    }

    let parseStream (schema: Schema<'T>) (stream: Stream) : System.Threading.Tasks.Task<Result<'T, string list>> =
        task {
            try
                use! doc = JsonDocument.ParseAsync(stream)
                return schema.Parse doc.RootElement
            with ex ->
                return Error [$"invalid JSON: {ex.Message}"]
        }

    // --- Parse from key-value map (form data, query params, merged sources) ---

    let parseMap (schema: Schema<'T>) (data: IReadOnlyDictionary<string, string>) : Result<'T, string list> =
        let fields = schema.CompiledFields
        let errors = ResizeArray<string>()
        let values = Array.zeroCreate fields.Length

        for i in 0..fields.Length-1 do
            let field = fields.[i]
            // Case-insensitive lookup
            let rawValue =
                match data |> Seq.tryFind (fun kvp ->
                    String.Equals(kvp.Key, field.Name, StringComparison.OrdinalIgnoreCase)) with
                | Some kvp -> Some kvp.Value
                | None -> None

            match rawValue with
            | None | Some "" ->
                if field.Required then
                    errors.Add($"{field.Name} is required")
                else
                    values.[i] <- field.DefaultValue
            | Some v ->
                try
                    // Convert string to target type (coercion)
                    let converted =
                        match field.Type with
                        | SchemaCompiler.FString -> box v
                        | SchemaCompiler.FInt ->
                            match Int32.TryParse(v, Globalization.CultureInfo.InvariantCulture) with
                            | true, n -> box n
                            | false, _ -> failwith $"{field.Name}: expected integer"
                        | SchemaCompiler.FBool ->
                            match Boolean.TryParse(v) with
                            | true, b -> box b
                            | false, _ -> failwith $"{field.Name}: expected boolean"
                        | SchemaCompiler.FFloat ->
                            match Double.TryParse(v, Globalization.CultureInfo.InvariantCulture) with
                            | true, f -> box f
                            | false, _ -> failwith $"{field.Name}: expected number"
                        | SchemaCompiler.FDateTime ->
                            match DateTime.TryParse(v) with
                            | true, dt -> box dt
                            | false, _ -> failwith $"{field.Name}: expected date/time"
                        | SchemaCompiler.FDateTimeOffset ->
                            match DateTimeOffset.TryParse(v) with
                            | true, dto -> box dto
                            | false, _ -> failwith $"{field.Name}: expected date/time"
                        | _ -> box v  // fallback: treat as string

                    // Apply rules (transforms + validation)
                    match applyRules field.Name field.Rules converted with
                    | Ok transformed -> values.[i] <- transformed
                    | Error errs -> errors.AddRange(errs)
                with ex ->
                    errors.Add(ex.Message)

        if errors.Count > 0 then
            Error (errors |> Seq.toList)
        else
            let args = schema.ParamMapping |> Array.map (fun idx -> values.[idx])
            let ctor = typeof<'T>.GetConstructors().[0]
            Ok (ctor.Invoke(args) :?> 'T)

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

    let private buildOptionValue (optionType: Type) (value: obj) =
        let someCase = FSharp.Reflection.FSharpType.GetUnionCases(optionType) |> Array.find (fun c -> c.Name = "Some")
        FSharp.Reflection.FSharpValue.MakeUnion(someCase, [| value |])

    let rec private parseElementValue (targetType: Type) (prop: JsonElement) : obj =
        if targetType = typeof<string> then
            box (prop.GetString())
        elif targetType = typeof<int> then
            if prop.ValueKind = JsonValueKind.Number then
                box (prop.GetInt32())
            elif prop.ValueKind = JsonValueKind.String then
                match System.Int32.TryParse(prop.GetString(), Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture) with
                | true, n -> box n
                | false, _ -> failwith "expected integer"
            else
                failwith "expected integer"
        elif targetType = typeof<bool> then
            if prop.ValueKind = JsonValueKind.True || prop.ValueKind = JsonValueKind.False then
                box (prop.GetBoolean())
            elif prop.ValueKind = JsonValueKind.String then
                match System.Boolean.TryParse(prop.GetString()) with
                | true, b -> box b
                | false, _ -> failwith "expected boolean"
            else
                failwith "expected boolean"
        elif targetType = typeof<float> then
            if prop.ValueKind = JsonValueKind.Number then
                box (prop.GetDouble())
            elif prop.ValueKind = JsonValueKind.String then
                match System.Double.TryParse(prop.GetString(), Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture) with
                | true, n -> box n
                | false, _ -> failwith "expected number"
            else
                failwith "expected number"
        elif targetType = typeof<string list> then
            if prop.ValueKind <> JsonValueKind.Array then
                failwith "expected array"
            box (prop.EnumerateArray() |> Seq.map (fun item -> item.GetString()) |> Seq.toList)
        elif targetType.IsGenericType && targetType.GetGenericTypeDefinition() = typedefof<_ option> then
            if prop.ValueKind = JsonValueKind.Null then
                box None
            else
                let innerType = targetType.GetGenericArguments().[0]
                let innerValue = parseElementValue innerType prop
                buildOptionValue targetType innerValue
        elif targetType = typeof<obj> then
            match prop.ValueKind with
            | JsonValueKind.String -> box (prop.GetString())
            | JsonValueKind.Number -> box (prop.GetDouble())
            | JsonValueKind.True
            | JsonValueKind.False -> box (prop.GetBoolean())
            | JsonValueKind.Null -> null
            | _ -> box (prop.GetRawText())
        else
            failwith $"unsupported type {targetType.Name}"

    let rec private compiledFieldTypeOf (propType: Type) =
        if propType.IsGenericType && propType.GetGenericTypeDefinition() = typedefof<_ option> then
            let innerType = propType.GetGenericArguments().[0]
            SchemaCompiler.FNullable (compiledFieldTypeOf innerType)
        elif propType = typeof<string> then
            SchemaCompiler.FString
        elif propType = typeof<int> then
            SchemaCompiler.FInt
        elif propType = typeof<bool> then
            SchemaCompiler.FBool
        elif propType = typeof<float> then
            SchemaCompiler.FFloat
        elif propType = typeof<DateTime> then
            SchemaCompiler.FDateTime
        elif propType = typeof<DateTimeOffset> then
            SchemaCompiler.FDateTimeOffset
        elif propType = typeof<string list> then
            SchemaCompiler.FStringList
        else
            SchemaCompiler.FString

    let rec private schemaTypeNameOf (propType: Type) =
        if propType.IsGenericType && propType.GetGenericTypeDefinition() = typedefof<_ option> then
            schemaTypeNameOf (propType.GetGenericArguments().[0])
        elif propType = typeof<string> || propType = typeof<obj> then
            "string"
        elif propType = typeof<int> then
            "integer"
        elif propType = typeof<bool> then
            "boolean"
        elif propType = typeof<float> then
            "number"
        elif propType = typeof<DateTime> || propType = typeof<DateTimeOffset> then
            "string"
        elif propType = typeof<string list> then
            "array"
        else
            "string"

    let fromType<'T> () : Schema<'T> =
        let t = typeof<'T>
        let fields = FSharp.Reflection.FSharpType.GetRecordFields(t)
        let ctor = FSharp.Reflection.FSharpValue.PreComputeRecordConstructor(t)

        let compiledFields =
            fields
            |> Array.map (fun prop ->
                let propType = prop.PropertyType
                let isOptional = propType.IsGenericType && propType.GetGenericTypeDefinition() = typedefof<_ option>
                {
                    SchemaCompiler.CompiledField.Name = prop.Name
                    SchemaCompiler.CompiledField.Type = compiledFieldTypeOf propType
                    SchemaCompiler.CompiledField.Required = not isOptional
                    SchemaCompiler.CompiledField.DefaultValue = if isOptional then box None else null
                    SchemaCompiler.CompiledField.Rules = []
                })

        let fieldIndex = Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        for i in 0..compiledFields.Length-1 do
            fieldIndex.[compiledFields.[i].Name] <- i

        let ctorParams = t.GetConstructors().[0].GetParameters()
        let paramMapping =
            ctorParams
            |> Array.map (fun p ->
                compiledFields
                |> Array.findIndex (fun f -> String.Equals(f.Name, p.Name, StringComparison.OrdinalIgnoreCase)))

        let construct (values: obj[]) =
            let args = paramMapping |> Array.map (fun idx -> values.[idx])
            ctor args :?> 'T

        let parseElement (el: JsonElement) : Result<'T, string list> =
            let errors = ResizeArray<string>()
            let values = Array.zeroCreate compiledFields.Length
            for i in 0..compiledFields.Length-1 do
                let field = compiledFields.[i]
                match el.TryGetProperty(field.Name) with
                | true, prop ->
                    try
                        values.[i] <- parseElementValue fields.[i].PropertyType prop
                    with ex ->
                        errors.Add($"{field.Name}: {ex.Message}")
                | _ ->
                    if field.Required then errors.Add($"{field.Name} is required")
                    else values.[i] <- field.DefaultValue
            if errors.Count > 0 then Error (errors |> Seq.toList)
            else Ok (construct values)

        let fieldSpecs =
            fields |> Array.map (fun prop ->
                let isOptional = prop.PropertyType.IsGenericType && prop.PropertyType.GetGenericTypeDefinition() = typedefof<_ option>
                {
                    FieldSpec.Name = prop.Name
                    FieldSpec.Type = schemaTypeNameOf prop.PropertyType
                    FieldSpec.Required = not isOptional
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
            Refinements = []
        }

    /// Cross-field validation. Use with `do!` inside a schema CE:
    /// do! Schema.check (fun () -> if a = b then Ok () else Error "a must equal b")
    let check (validate: unit -> Result<unit, string>) : SchemaCheck =
        { SchemaCheck.Validate = validate }

type SchemaBuilder() =
    member _.Bind(field: SchemaField<'T>, f: 'T -> SchemaParser<'U>) : SchemaParser<'U> =
        let restDefault = f Unchecked.defaultof<'T>
        {
            Specs = field.Spec :: restDefault.Specs
            CompiledFields = field.Compiled :: restDefault.CompiledFields
            Refinements = restDefault.Refinements
            Parse = fun json ->
                match field.Parse json with
                | Ok value ->
                    match (f value).Parse json with
                    | Ok result -> Ok result
                    | Error errs -> Error errs
                | Error errs1 ->
                    match restDefault.Parse json with
                    | Ok _ -> Error errs1
                    | Error errs2 -> Error (errs1 @ errs2)
        }

    /// Bind for cross-field validation via `do! Schema.check (fun () -> ...)`
    member _.Bind(check: SchemaCheck, f: unit -> SchemaParser<'U>) : SchemaParser<'U> =
        let restForSpecs = f ()
        // Mark that this schema has checks — forces buffer path to use JsonElement fallback
        let marker : 'U -> Result<'U, string list> = fun v -> Ok v
        {
            Specs = restForSpecs.Specs
            CompiledFields = restForSpecs.CompiledFields
            Refinements = marker :: restForSpecs.Refinements  // non-empty = hasChecks
            Parse = fun json ->
                match check.Validate() with
                | Ok () -> (f ()).Parse json
                | Error msg -> Error [msg]
        }

    member _.Return(value: 'T) : SchemaParser<'T> =
        {
            Specs = []
            CompiledFields = []
            Refinements = []
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

        let hasChecks = not (List.isEmpty parser.Refinements)

        // Fall back to JsonElement Parse path when schema has cross-field checks
        // (closures needed for check validation).
        let bufferParse =
            if hasChecks then
                fun (buffer: ReadOnlySequence<byte>) ->
                    try
                        use doc = JsonDocument.Parse(buffer)
                        parser.Parse doc.RootElement
                    with ex ->
                        Error [$"invalid JSON: {ex.Message}"]
            else
                SchemaCompiler.parseFromBuffer compiledFields paramMapping construct fieldIndex

        {
            Parse = parser.Parse
            ParseBuffer = bufferParse
            Fields = parser.Specs |> List.rev
            CompiledFields = compiledFields
            ParamMapping = paramMapping
            Refinements = parser.Refinements
        }

[<AutoOpen>]
module SchemaExtensions =
    let schema = SchemaBuilder()
