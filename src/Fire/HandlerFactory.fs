namespace Fire

open System
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.FSharp.Reflection

[<RequireQualifiedAccess>]
module HandlerFactory =

    /// Parse %i, %s, %b, %f from pattern and convert to :__p0, :__p1, etc.
    let convertPattern (pattern: string) : string * Type list =
        let specs = ResizeArray<Type>()
        let result = Text.StringBuilder()
        let mutable i = 0
        let mutable pIdx = 0
        while i < pattern.Length do
            if i + 1 < pattern.Length && pattern.[i] = '%' then
                match pattern.[i+1] with
                | 'i' | 'd' ->
                    specs.Add(typeof<int>)
                    result.Append($":__p{pIdx}") |> ignore
                    pIdx <- pIdx + 1
                    i <- i + 2
                | 's' ->
                    specs.Add(typeof<string>)
                    result.Append($":__p{pIdx}") |> ignore
                    pIdx <- pIdx + 1
                    i <- i + 2
                | 'b' ->
                    specs.Add(typeof<bool>)
                    result.Append($":__p{pIdx}") |> ignore
                    pIdx <- pIdx + 1
                    i <- i + 2
                | 'f' ->
                    specs.Add(typeof<float>)
                    result.Append($":__p{pIdx}") |> ignore
                    pIdx <- pIdx + 1
                    i <- i + 2
                | _ ->
                    result.Append(pattern.[i]) |> ignore
                    i <- i + 1
            else
                result.Append(pattern.[i]) |> ignore
                i <- i + 1
        (result.ToString(), specs |> Seq.toList)

    /// Find the FSharpFunc<,> base type in the inheritance chain
    let private findFSharpFuncType (t: Type) : Type option =
        let mutable current = t
        let mutable result = None
        while current <> null && result.IsNone do
            if current.IsGenericType then
                let genDef = current.GetGenericTypeDefinition()
                if genDef.FullName <> null && genDef.FullName.StartsWith("Microsoft.FSharp.Core.FSharpFunc") then
                    result <- Some current
            current <- current.BaseType
        result

    /// Walk FSharpFunc chain to extract parameter types.
    /// Handles both direct FSharpFunc types and closure subclasses.
    let getParamTypes (t: Type) : Type list =
        let rec walk (t: Type) =
            let funcType =
                if t.IsGenericType then
                    let genDef = t.GetGenericTypeDefinition()
                    if genDef.FullName <> null && genDef.FullName.StartsWith("Microsoft.FSharp.Core.FSharpFunc") then
                        Some t
                    else None
                else
                    findFSharpFuncType t
            match funcType with
            | Some ft ->
                let args = ft.GetGenericArguments()
                args.[0] :: walk args.[1]
            | None -> []
        walk t

    /// Invoke a curried FSharpFunc with arguments
    let invokeCurried (fn: obj) (args: obj[]) : obj =
        let mutable current = fn
        for arg in args do
            let t = current.GetType()
            // F# closures may have multiple Invoke overloads (curried + tuple).
            // Pick the one with exactly one parameter.
            let invokeMethod =
                t.GetMethods()
                |> Array.find (fun m ->
                    m.Name = "Invoke" && m.GetParameters().Length = 1)
            current <- invokeMethod.Invoke(current, [| arg |])
        current

    /// Await a Task-like object and extract the Response.
    /// Handles both Task<Response> (concrete) and Task<obj> (erased generic).
    let awaitResponse (taskObj: obj) : Task<Response> =
        match taskObj with
        | :? Task<Response> as t -> t
        | _ ->
            // The result is Task<obj> or some other Task<T>
            let taskType = taskObj.GetType()
            task {
                let t = taskObj :?> Task
                do! t
                let resultProp = taskType.GetProperty("Result")
                let result = resultProp.GetValue(taskObj)
                return result :?> Response
            }

    /// Convert string to target type
    let convertValue (targetType: Type) (value: string) : obj =
        if targetType = typeof<int> then box (Int32.Parse value)
        elif targetType = typeof<string> then box value
        elif targetType = typeof<bool> then box (Boolean.Parse value)
        elif targetType = typeof<float> then box (Double.Parse value)
        else failwith $"cannot convert to {targetType.Name}"

    /// Check if a type is assignable to Handler (FSharpFunc<Request, Task<Response>>)
    let private isHandler (t: Type) : bool =
        typeof<Handler>.IsAssignableFrom(t)

    /// Build a Handler from any F# function
    let create (httpMethod: string) (pattern: string) (handler: obj) : string * Handler =
        let triePattern, formatSpecs = convertPattern pattern
        let handlerType = handler.GetType()

        // Fast path: if the handler IS already a Handler (Request -> Task<Response>),
        // use it directly without reflection overhead.
        if isHandler handlerType then
            let h = handler :?> Handler
            (triePattern, h)
        else
            let paramTypes = getParamTypes handlerType
            let isBodyMethod = match httpMethod.ToUpperInvariant() with "POST" | "PUT" | "PATCH" -> true | _ -> false

            if paramTypes.IsEmpty || (paramTypes.Length = 1 && paramTypes.[0] = typeof<unit>) then
                // fun () -> task { ... }
                let h : Handler = fun _req ->
                    let result = invokeCurried handler [| box () |]
                    awaitResponse result
                (triePattern, h)
            else
                // Classify params
                let mutable specIdx = 0
                let classified =
                    paramTypes |> List.map (fun t ->
                        if t = typeof<Request> then
                            (t, "request")
                        elif t = typeof<obj> || t = typeof<Object> then
                            // Erased generic (fun _ -> ...) — treat as Request for backward compat
                            (t, "request-obj")
                        elif t.IsInterface || t.IsAbstract then
                            (t, "di")
                        elif specIdx < formatSpecs.Length && (t = typeof<int> || t = typeof<string> || t = typeof<bool> || t = typeof<float>) then
                            specIdx <- specIdx + 1
                            (t, "route")
                        elif isBodyMethod && (FSharpType.IsRecord t || (t.IsClass && t <> typeof<string> && not (t.FullName.StartsWith("Microsoft.FSharp.Core.FSharpFunc")))) then
                            (t, "body")
                        else
                            // Primitive without format spec: still treat as route param if we have specs remaining
                            if specIdx < formatSpecs.Length then
                                specIdx <- specIdx + 1
                            (t, "route")
                    )

                let mutable routeParamIdx = 0
                let paramBindings = classified |> List.map (fun (t, kind) ->
                    match kind with
                    | "route" ->
                        let name = $"__p{routeParamIdx}"
                        routeParamIdx <- routeParamIdx + 1
                        (t, "route", name)
                    | _ -> (t, kind, ""))

                let h : Handler = fun req -> task {
                    let args = ResizeArray<obj>()
                    for (paramType, kind, name) in paramBindings do
                        match kind with
                        | "di" ->
                            args.Add(req.Raw.RequestServices.GetRequiredService(paramType))
                        | "route" ->
                            let value = req.Params.[name]
                            args.Add(convertValue paramType value)
                        | "body" ->
                            let body = req.Raw.Request.Body
                            let! deserialized = JsonSerializer.DeserializeAsync(body, paramType)
                            args.Add(deserialized)
                        | "request" ->
                            args.Add(box req)
                        | "request-obj" ->
                            // Erased generic — pass Request boxed as obj
                            args.Add(box req)
                        | _ -> ()
                    let result = invokeCurried handler (args.ToArray())
                    return! awaitResponse result
                }

                (triePattern, h)
