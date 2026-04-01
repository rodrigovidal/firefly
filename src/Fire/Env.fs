namespace Fire

open System
open System.IO

[<RequireQualifiedAccess>]
module Env =

    let private toScreamingSnake (name: string) =
        let mutable result = System.Text.StringBuilder()
        for i in 0..name.Length-1 do
            let c = name.[i]
            if i > 0 && Char.IsUpper(c) then
                let prev = name.[i-1]
                if Char.IsLower(prev) then
                    result.Append('_').Append(c) |> ignore
                elif i + 1 < name.Length && Char.IsLower(name.[i+1]) then
                    result.Append('_').Append(c) |> ignore
                else
                    result.Append(Char.ToUpperInvariant(c)) |> ignore
            else
                result.Append(Char.ToUpperInvariant(c)) |> ignore
        result.ToString()

    let mutable private envFileLoaded = false
    let private envFileLock = obj()

    let private ensureEnvFileLoaded () =
        if not envFileLoaded then
            lock envFileLock (fun () ->
                if not envFileLoaded then
                    let path = Path.Combine(Directory.GetCurrentDirectory(), ".env")
                    if File.Exists(path) then
                        for line in File.ReadAllLines(path) do
                            let trimmed = line.Trim()
                            if trimmed.Length > 0 && not (trimmed.StartsWith("#")) then
                                match trimmed.IndexOf('=') with
                                | -1 -> ()
                                | i ->
                                    let key = trimmed.Substring(0, i).Trim()
                                    let value =
                                        let raw = trimmed.Substring(i + 1).Trim()
                                        if (raw.StartsWith("\"") && raw.EndsWith("\"")) || (raw.StartsWith("'") && raw.EndsWith("'")) then
                                            raw.Substring(1, raw.Length - 2)
                                        else raw
                                    // Only set if not already in environment (env vars win)
                                    if Environment.GetEnvironmentVariable(key) = null then
                                        Environment.SetEnvironmentVariable(key, value)
                    envFileLoaded <- true
            )

    let private parseValue (targetType: Type) (value: string) : obj =
        if targetType = typeof<string> then box value
        elif targetType = typeof<int> then
            match Int32.TryParse(value) with
            | true, n -> box n
            | _ -> failwith $"expected integer, got '{value}'"
        elif targetType = typeof<float> then
            match Double.TryParse(value, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture) with
            | true, n -> box n
            | _ -> failwith $"expected number, got '{value}'"
        elif targetType = typeof<bool> then
            match value.ToLowerInvariant() with
            | "true" | "1" | "yes" -> box true
            | "false" | "0" | "no" -> box false
            | _ -> failwith $"expected boolean, got '{value}'"
        elif targetType = typeof<Uri> then box (Uri(value))
        elif targetType = typeof<TimeSpan> then
            match TimeSpan.TryParse(value) with
            | true, ts -> box ts
            | _ -> failwith $"expected timespan, got '{value}'"
        else
            box value

    let private buildOptionValue (optionType: Type) (value: obj) =
        let someCase = FSharp.Reflection.FSharpType.GetUnionCases(optionType) |> Array.find (fun c -> c.Name = "Some")
        FSharp.Reflection.FSharpValue.MakeUnion(someCase, [| value |])

    /// Load typed configuration from .env file + environment variables.
    /// PascalCase fields map to SCREAMING_SNAKE_CASE env vars.
    /// Option fields are optional; missing required vars throw with all missing names listed.
    let load<'T> () : 'T =
        ensureEnvFileLoaded ()
        let t = typeof<'T>
        let fields = FSharp.Reflection.FSharpType.GetRecordFields(t)
        let ctor = FSharp.Reflection.FSharpValue.PreComputeRecordConstructor(t)
        let ctorParams = t.GetConstructors() |> Array.find (fun c -> c.GetParameters().Length = fields.Length) |> fun c -> c.GetParameters()

        let values = Array.zeroCreate fields.Length
        let missing = Collections.Generic.List<string>()

        for i in 0..fields.Length-1 do
            let prop = fields.[i]
            let envName = toScreamingSnake prop.Name
            let propType = prop.PropertyType
            let isOptional = propType.IsGenericType && propType.GetGenericTypeDefinition() = typedefof<_ option>
            let rawValue = Environment.GetEnvironmentVariable(envName)

            if rawValue = null then
                if isOptional then
                    values.[i] <- box None
                else
                    missing.Add(envName)
            else
                try
                    if isOptional then
                        let innerType = propType.GetGenericArguments().[0]
                        let parsed = parseValue innerType rawValue
                        values.[i] <- buildOptionValue propType parsed
                    else
                        values.[i] <- parseValue propType rawValue
                with ex ->
                    failwith $"Environment variable {envName}: {ex.Message}"

        if missing.Count > 0 then
            let vars = String.Join(", ", missing)
            failwith $"Missing required environment variables: {vars}"

        let paramMapping =
            ctorParams |> Array.map (fun p ->
                fields |> Array.findIndex (fun f -> String.Equals(f.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
        let args = paramMapping |> Array.map (fun idx -> values.[idx])
        ctor args :?> 'T
