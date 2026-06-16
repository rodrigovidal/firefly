namespace Fire

open System
open System.Globalization
open System.Linq.Expressions
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.FSharp.Reflection

[<RequireQualifiedAccess>]
module HandlerFactory =

    let private queryJsonOptions =
        let opts = JsonSerializerOptions()
        opts.NumberHandling <- System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        opts.PropertyNameCaseInsensitive <- true
        opts

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

    /// Build a compiled invoker using Expression trees.
    /// At registration: builds and compiles an expression that chains Invoke calls.
    /// At request time: direct delegate call — no reflection, no boxing overhead from MethodInfo.Invoke.
    let private buildInvoker (handler: obj) (paramCount: int) : (obj[] -> obj) =
        // Walk the FSharpFunc chain to collect MethodInfo for each Invoke
        let methods = Array.zeroCreate paramCount
        let mutable currentType = handler.GetType()
        for i in 0..paramCount-1 do
            methods.[i] <-
                currentType.GetMethods()
                |> Array.find (fun m -> m.Name = "Invoke" && m.GetParameters().Length = 1)
            currentType <- methods.[i].ReturnType

        // Build expression tree: (args) => handler.Invoke(args[0]).Invoke(args[1])...
        let argsParam = Expression.Parameter(typeof<obj[]>, "args")
        let handlerConst = Expression.Constant(handler)

        let mutable expr : Expression = Expression.Convert(handlerConst, handler.GetType()) :> Expression
        for i in 0..paramCount-1 do
            let method = methods.[i]
            let paramType = method.GetParameters().[0].ParameterType
            let argAccess = Expression.ArrayIndex(argsParam, Expression.Constant(i))
            let convertedArg =
                if paramType.IsValueType then
                    Expression.Unbox(argAccess, paramType) :> Expression
                else
                    Expression.Convert(argAccess, paramType) :> Expression
            expr <- Expression.Call(expr, method, convertedArg) :> Expression

        let boxedResult = Expression.Convert(expr, typeof<obj>) :> Expression
        let lambda = Expression.Lambda<Func<obj[], obj>>(boxedResult, argsParam)
        let compiled = lambda.Compile()

        fun args -> compiled.Invoke(args)

    /// Single-argument variant: compiles `(arg) => handler.Invoke(arg)` so the
    /// common one-param handler path doesn't allocate an obj[] per request.
    let private buildInvoker1 (handler: obj) : Func<obj, obj> =
        let invokeMethod =
            handler.GetType().GetMethods()
            |> Array.find (fun m -> m.Name = "Invoke" && m.GetParameters().Length = 1)
        let argParam = Expression.Parameter(typeof<obj>, "arg")
        let paramType = invokeMethod.GetParameters().[0].ParameterType
        let convertedArg =
            if paramType.IsValueType then Expression.Unbox(argParam, paramType) :> Expression
            else Expression.Convert(argParam, paramType) :> Expression
        let handlerConst = Expression.Convert(Expression.Constant(handler), handler.GetType())
        let call = Expression.Call(handlerConst, invokeMethod, convertedArg)
        let boxedResult = Expression.Convert(call, typeof<obj>) :> Expression
        Expression.Lambda<Func<obj, obj>>(boxedResult, argParam).Compile()

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
        if targetType = typeof<int> then box (Int32.Parse(value, CultureInfo.InvariantCulture))
        elif targetType = typeof<string> then box value
        elif targetType = typeof<bool> then box (Boolean.Parse(value))
        elif targetType = typeof<float> then box (Double.Parse(value, CultureInfo.InvariantCulture))
        else failwith $"cannot convert to {targetType.Name}"

    let private isSupportedRouteParamType (t: Type) =
        t = typeof<int> || t = typeof<string> || t = typeof<bool> || t = typeof<float>

    /// Check if a type is assignable to Handler (FSharpFunc<Request, Task<Response>>)
    let private isHandler (t: Type) : bool =
        typeof<Handler>.IsAssignableFrom(t)

    /// Build a Handler from any F# function
    let create (httpMethod: string) (pattern: string) (handler: obj) : string * Handler =
        let triePattern, formatSpecs = convertPattern pattern
        let routeParamNames =
            triePattern.Split('/', StringSplitOptions.RemoveEmptyEntries)
            |> Array.choose (fun segment ->
                if segment.StartsWith(":", StringComparison.Ordinal) then
                    Some (segment.Substring(1))
                else
                    None)
        let handlerType = handler.GetType()

        // Fast path: if the handler IS already a Handler (Request -> Task<Response>),
        // use it directly without reflection overhead.
        if isHandler handlerType then
            let h = handler :?> Handler
            (triePattern, h)
        else
            if findFSharpFuncType handlerType = None && not (isHandler handlerType) then
                failwith $"Route handler must be a function (Request -> Task<Response> or similar), got {handlerType.Name}"

            let paramTypes = getParamTypes handlerType
            let isBodyMethod = match httpMethod.ToUpperInvariant() with "POST" | "PUT" | "PATCH" -> true | _ -> false

            if paramTypes.IsEmpty || (paramTypes.Length = 1 && paramTypes.[0] = typeof<unit>) then
                // fun () -> task { ... }
                let invoker = buildInvoker handler 1
                let h : Handler = fun _req ->
                    let result = invoker [| box () |]
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
                        elif specIdx < formatSpecs.Length && isSupportedRouteParamType t then
                            specIdx <- specIdx + 1
                            (t, "route")
                        elif isBodyMethod && (FSharpType.IsRecord t || (t.IsClass && t <> typeof<string> && not (t.FullName.StartsWith("Microsoft.FSharp.Core.FSharpFunc")))) then
                            (t, "body")
                        elif (not isBodyMethod) && (FSharpType.IsRecord t || (t.IsClass && t <> typeof<string> && not (t.FullName.StartsWith("Microsoft.FSharp.Core.FSharpFunc")))) then
                            (t, "query")
                        else
                            if specIdx < formatSpecs.Length then
                                failwith $"Route format parameters support int, string, bool, and float, got {t.Name}"
                            (t, "route")
                    )

                let mutable routeParamIdx = 0
                let paramBindings = classified |> List.map (fun (t, kind) ->
                    match kind with
                    | "route" ->
                        let name = routeParamNames.[routeParamIdx]
                        routeParamIdx <- routeParamIdx + 1
                        (t, "route", name)
                    | _ -> (t, kind, ""))

                match paramBindings with
                | [ (_, ("request" | "request-obj"), _) ] ->
                    // Single Request param (incl. erased-generic `fun _ -> ...`).
                    // Skip the ResizeArray/args-array/extra-task machinery and the
                    // per-request conversion loop — invoke directly with no obj[].
                    let invoke1 = buildInvoker1 handler
                    let h : Handler = fun req -> awaitResponse (invoke1.Invoke(box req))
                    (triePattern, h)
                | _ ->

                let paramCount = paramBindings.Length
                let invoker = buildInvoker handler paramCount

                let h : Handler = fun req -> task {
                    let args = ResizeArray<obj>()
                    let mutable conversionError = false
                    for (paramType, kind, name) in paramBindings do
                        if not conversionError then
                            match kind with
                            | "di" ->
                                args.Add(req.Raw.RequestServices.GetRequiredService(paramType))
                            | "route" ->
                                let value = req.Params.[name]
                                try
                                    args.Add(convertValue paramType value)
                                with :? FormatException ->
                                    conversionError <- true
                            | "body" ->
                                let body = req.Raw.Request.Body
                                let! deserialized = JsonSerializer.DeserializeAsync(body, paramType)
                                args.Add(deserialized)
                            | "query" ->
                                try
                                    let q = req.Raw.Request.Query
                                    let d = System.Collections.Generic.Dictionary<string, string>(q.Count)
                                    for kvp in q do
                                        d.[kvp.Key] <- kvp.Value.ToString()
                                    let json = JsonSerializer.SerializeToUtf8Bytes(d :> System.Collections.Generic.IDictionary<string, string>)
                                    let deserialized = JsonSerializer.Deserialize(ReadOnlySpan(json), paramType, queryJsonOptions)
                                    args.Add(deserialized)
                                with
                                | :? JsonException | :? NotSupportedException ->
                                    conversionError <- true
                            | "request" ->
                                args.Add(box req)
                            | "request-obj" ->
                                // Erased generic — pass Request boxed as obj
                                args.Add(box req)
                            | _ -> ()
                    if conversionError then
                        return { Status = 400; Headers = []; Body = Empty }
                    else
                        let result = invoker (args.ToArray())
                        return! awaitResponse result
                }

                (triePattern, h)
