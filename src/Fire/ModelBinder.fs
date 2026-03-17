namespace Fire

open System
open System.Collections.Generic
open System.Text.Json

[<RequireQualifiedAccess>]
module ModelBinder =

    type BindResult = Result<obj, string list>

    let private tryConvert (targetType: Type) (name: string) (value: string option) : Result<obj, string> =
        let innerType, isOption =
            if targetType.IsGenericType && targetType.GetGenericTypeDefinition() = typedefof<option<_>> then
                targetType.GetGenericArguments().[0], true
            else
                targetType, false

        match value with
        | None ->
            if isOption then Ok null  // None
            else Error $"{name} is required"
        | Some "" ->
            if isOption then Ok null  // None
            elif targetType = typeof<string> then Ok (box "")
            else Error $"{name} is required"
        | Some v ->
            if innerType = typeof<string> then
                if isOption then Ok (box (Some v)) else Ok (box v)
            elif innerType = typeof<int> then
                match Int32.TryParse(v) with
                | true, n -> if isOption then Ok (box (Some n)) else Ok (box n)
                | false, _ -> Error $"{name}: expected integer"
            elif innerType = typeof<bool> then
                match Boolean.TryParse(v) with
                | true, b -> if isOption then Ok (box (Some b)) else Ok (box b)
                | false, _ -> Error $"{name}: expected boolean"
            elif innerType = typeof<float> then
                match Double.TryParse(v) with
                | true, f -> if isOption then Ok (box (Some f)) else Ok (box f)
                | false, _ -> Error $"{name}: expected number"
            else
                Error $"{name}: unsupported type {innerType.Name}"

    type ResolvedBinder = {
        ParamNames: string[]
        ParamTypes: Type[]
        Constructor: obj[] -> obj
        IsBodyMethod: bool
    }

    let create (inputType: Type) (httpMethod: string) : ResolvedBinder =
        let ctor = inputType.GetConstructors().[0]
        let ctorParams = ctor.GetParameters()
        let isBodyMethod = match httpMethod.ToUpperInvariant() with "POST" | "PUT" | "PATCH" -> true | _ -> false

        {
            ParamNames = ctorParams |> Array.map (fun p -> p.Name)
            ParamTypes = ctorParams |> Array.map (fun p -> p.ParameterType)
            Constructor = fun values -> ctor.Invoke(values)
            IsBodyMethod = isBodyMethod
        }

    let bind (binder: ResolvedBinder) (routeParams: IReadOnlyDictionary<string, string>) (queryParams: IReadOnlyDictionary<string, string>) (bodyJson: string option) : BindResult =
        let bodyFields =
            if binder.IsBodyMethod then
                match bodyJson with
                | Some json when not (String.IsNullOrWhiteSpace json) ->
                    try
                        let doc = JsonDocument.Parse(json)
                        let dict = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        for prop in doc.RootElement.EnumerateObject() do
                            dict.[prop.Name] <- prop.Value.ToString()
                        Ok (dict :> IReadOnlyDictionary<_, _>)
                    with _ ->
                        Error ["invalid request body"]
                | _ -> Ok (Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
            else
                Ok (Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)

        match bodyFields with
        | Error errs -> Error errs
        | Ok body ->
            let errors = ResizeArray<string>()
            let values = Array.zeroCreate binder.ParamNames.Length

            for i in 0 .. binder.ParamNames.Length - 1 do
                let name = binder.ParamNames.[i]
                let typ = binder.ParamTypes.[i]

                // Case-insensitive lookup across all sources
                let tryFind (dict: IReadOnlyDictionary<string, string>) (key: string) =
                    match dict.TryGetValue(key) with
                    | true, v -> Some v
                    | false, _ ->
                        // Fallback: case-insensitive search
                        dict |> Seq.tryFind (fun kvp ->
                            String.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                        |> Option.map (fun kvp -> kvp.Value)

                let value =
                    match tryFind routeParams name with
                    | Some _ as v -> v
                    | None ->
                        if binder.IsBodyMethod then
                            match tryFind body name with
                            | Some _ as v -> v
                            | None -> None
                        else
                            match tryFind queryParams name with
                            | Some _ as v -> v
                            | None -> None

                match tryConvert typ name value with
                | Ok v -> values.[i] <- v
                | Error e -> errors.Add(e)

            if errors.Count > 0 then
                Error (errors |> Seq.toList)
            else
                Ok (binder.Constructor values)
