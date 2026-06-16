namespace Fire

open System
open System.Buffers
open System.Collections.Generic

type TrieNode = {
    StaticChildren: Dictionary<string, TrieNode>
    ParamChild: TrieNode option
    WildcardChild: Dictionary<string, Handler * string list> option  // method -> (handler, paramNames for wildcard)
    Handlers: Dictionary<string, Handler * string list>              // method -> (handler, paramNames collected during add)
}

[<RequireQualifiedAccess>]
module Trie =

    // Shared read-only empty dictionary so zero-param routes don't allocate a
    // fresh Dictionary on every lookup. Safe because it is exposed only as
    // IReadOnlyDictionary and never mutated.
    let private emptyParams : IReadOnlyDictionary<string, string> =
        Dictionary<string, string>() :> IReadOnlyDictionary<_, _>

    // All keyed dictionaries use ordinal comparison, which also enables the
    // zero-allocation ReadOnlySpan<char> alternate lookups used during routing.
    let private emptyNode () = {
        StaticChildren = Dictionary<string, TrieNode>(StringComparer.Ordinal)
        ParamChild = None
        WildcardChild = None
        Handlers = Dictionary<string, Handler * string list>(StringComparer.Ordinal)
    }

    let empty = emptyNode ()

    let private composeMiddleware (middlewares: Middleware list) (handler: Handler) : Handler =
        List.foldBack (fun (mw: Middleware) (h: Handler) -> mw h) middlewares handler

    // `add` stays purely functional even though nodes hold mutable dictionaries:
    // every update produces a fresh copy, so the shared `empty` node and any
    // previously-built tree are never mutated. Cost is paid once, at startup.
    let add (method': string) (pattern: string) (middlewares: Middleware list) (handler: Handler) (root: TrieNode) : TrieNode =
        let segments = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries)
        let composed = composeMiddleware middlewares handler

        // Collect param names encountered on the path to the handler
        let rec insert (node: TrieNode) (idx: int) (paramNames: string list) =
            if idx >= segments.Length then
                let handlers = Dictionary<string, Handler * string list>(node.Handlers, StringComparer.Ordinal)
                handlers.[method'] <- (composed, List.rev paramNames)
                { node with Handlers = handlers }
            else
                let seg = segments.[idx]
                if seg.[0] = '*' then
                    let paramName = seg.Substring(1)
                    let handlers =
                        match node.WildcardChild with
                        | Some existing -> Dictionary<string, Handler * string list>(existing, StringComparer.Ordinal)
                        | None -> Dictionary<string, Handler * string list>(StringComparer.Ordinal)
                    handlers.[method'] <- (composed, List.rev (paramName :: paramNames))
                    { node with WildcardChild = Some handlers }
                elif seg.[0] = ':' then
                    let paramName = seg.Substring(1)
                    let child =
                        match node.ParamChild with
                        | Some existing -> existing
                        | None -> emptyNode ()
                    let updated = insert child (idx + 1) (paramName :: paramNames)
                    { node with ParamChild = Some updated }
                else
                    let child =
                        match node.StaticChildren.TryGetValue seg with
                        | true, existing -> existing
                        | _ -> emptyNode ()
                    let updated = insert child (idx + 1) paramNames
                    let children = Dictionary<string, TrieNode>(node.StaticChildren, StringComparer.Ordinal)
                    children.[seg] <- updated
                    { node with StaticChildren = children }

        insert root 0 []

    // Returns a struct ValueOption of a struct tuple so a successful lookup
    // allocates nothing for the result itself — static routes and misses become
    // fully allocation-free; only param/wildcard captures allocate their values.
    let lookup (method': string) (path: string) (root: TrieNode) : (struct (Handler * IReadOnlyDictionary<string, string>)) voption =
        let pathLen = path.Length
        // Tokenize the path into segment ranges without allocating a string[] or
        // per-segment substrings. Static segments are matched against the trie via
        // a span alternate-lookup, so only param/wildcard captures allocate.
        let ranges = ArrayPool<Range>.Shared.Rent(64)
        try
            let count = MemoryExtensions.Split(path.AsSpan(), ranges.AsSpan(), '/', StringSplitOptions.RemoveEmptyEntries)

            let buildParams (paramNames: string list) (paramValues: string list) : IReadOnlyDictionary<string, string> =
                if List.isEmpty paramNames then emptyParams
                else
                    let dict = Dictionary<string, string>()
                    List.iter2 (fun name value -> dict.[name] <- value) paramNames (List.rev paramValues)
                    dict :> IReadOnlyDictionary<_, _>

            // During lookup, collect param segment values (positional) on the way down
            let rec search (node: TrieNode) (idx: int) (paramValues: string list) =
                if idx >= count then
                    match node.Handlers.TryGetValue method' with
                    | true, (h, paramNames) -> ValueSome (struct (h, buildParams paramNames paramValues))
                    | _ -> ValueNone
                else
                    let struct (off, len) = ranges.[idx].GetOffsetAndLength(pathLen)
                    let mutable child = Unchecked.defaultof<TrieNode>
                    let staticHit =
                        node.StaticChildren.Count > 0
                        && node.StaticChildren.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(path.AsSpan(off, len), &child)
                    if staticHit then
                        match search child (idx + 1) paramValues with
                        | ValueSome _ as result -> result
                        | ValueNone -> tryParam node off len idx paramValues
                    else
                        tryParam node off len idx paramValues

            and tryParam (node: TrieNode) (off: int) (len: int) (idx: int) (paramValues: string list) =
                match node.ParamChild with
                | Some child ->
                    let value = path.Substring(off, len)
                    match search child (idx + 1) (value :: paramValues) with
                    | ValueSome _ as result -> result
                    | ValueNone -> tryWildcard node idx paramValues
                | None -> tryWildcard node idx paramValues

            and tryWildcard (node: TrieNode) (idx: int) (paramValues: string list) =
                match node.WildcardChild with
                | Some handlers ->
                    match handlers.TryGetValue method' with
                    | true, (h, paramNames) ->
                        let sb = System.Text.StringBuilder()
                        for j in idx .. count - 1 do
                            if j > idx then sb.Append('/') |> ignore
                            let struct (o, l) = ranges.[j].GetOffsetAndLength(pathLen)
                            sb.Append(path.AsSpan(o, l)) |> ignore
                        let captured = sb.ToString()
                        ValueSome (struct (h, buildParams paramNames (captured :: paramValues)))
                    | _ -> ValueNone
                | None -> ValueNone

            if count = 0 then
                match root.Handlers.TryGetValue method' with
                | true, (h, _) -> ValueSome (struct (h, emptyParams))
                | _ -> ValueNone
            else
                search root 0 []
        finally
            ArrayPool<Range>.Shared.Return(ranges)
