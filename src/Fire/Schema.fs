namespace Fire

open System
open System.IO
open System.Text.Json

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

// A schema field: parses one field from JsonElement
type SchemaField<'T> = {
    Name: string
    Parse: JsonElement -> Result<'T, string list>
    Spec: FieldSpec
}

// Internal: parser + accumulated specs
type SchemaParser<'T> = {
    Parse: JsonElement -> Result<'T, string list>
    Specs: FieldSpec list
}

// The public schema type
type Schema<'T> = {
    Parse: JsonElement -> Result<'T, string list>
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

    let nest (schema: Schema<'T>) (el: JsonElement) : Result<'T, string> =
        match schema.Parse el with
        | Ok v -> Ok v
        | Error errs -> Error (errs |> String.concat "; ")

    // --- Field builders ---

    let required (name: string) (parser: JsonElement -> Result<'T, string>) (rules: Rule list) : SchemaField<'T> =
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
            Children = []
        }
        {
            Name = name
            Spec = spec
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
            Children = []
        }
        {
            Name = name
            Spec = spec
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
            use doc = JsonDocument.Parse(jsonString)
            schema.Parse doc.RootElement
        with ex ->
            Error [$"invalid JSON: {ex.Message}"]

    let parseStream (schema: Schema<'T>) (stream: Stream) : System.Threading.Tasks.Task<Result<'T, string list>> =
        task {
            try
                use! doc = JsonDocument.ParseAsync(stream)
                return schema.Parse doc.RootElement
            with ex ->
                return Error [$"invalid JSON: {ex.Message}"]
        }

    // --- Handler integration ---

    /// Wraps a handler with schema validation. Validates body, passes parsed value to handler.
    let validated (schema: Schema<'T>) (handler: 'T -> System.Threading.Tasks.Task<Response>) : Handler =
        fun req -> task {
            try
                use! doc = JsonDocument.ParseAsync(req.Raw.Request.Body)
                match schema.Parse doc.RootElement with
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
            Parse = fun _ -> Ok value
        }

    member _.Run(parser: SchemaParser<'T>) : Schema<'T> =
        {
            Parse = parser.Parse
            Fields = parser.Specs |> List.rev
        }

[<AutoOpen>]
module SchemaBuilderModule =
    let schema = SchemaBuilder()
