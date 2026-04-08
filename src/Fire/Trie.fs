namespace Fire

open System.Collections.Generic

type TrieNode = {
    StaticChildren: Map<string, TrieNode>
    ParamChild: TrieNode option
    WildcardChild: Map<string, Handler * string list> option  // method -> (handler, paramNames for wildcard)
    Handlers: Map<string, Handler * string list>              // method -> (handler, paramNames collected during add)
}

[<RequireQualifiedAccess>]
module Trie =

    let private emptyNode () = {
        StaticChildren = Map.empty
        ParamChild = None
        WildcardChild = None
        Handlers = Map.empty
    }

    let empty = emptyNode ()

    let private splitPath (path: string) =
        path.Split('/', System.StringSplitOptions.RemoveEmptyEntries)

    let private composeMiddleware (middlewares: Middleware list) (handler: Handler) : Handler =
        List.foldBack (fun (mw: Middleware) (h: Handler) -> mw h) middlewares handler

    let add (method': string) (pattern: string) (middlewares: Middleware list) (handler: Handler) (root: TrieNode) : TrieNode =
        let segments = splitPath pattern
        let composed = composeMiddleware middlewares handler

        // Collect param names encountered on the path to the handler
        let rec insert (node: TrieNode) (idx: int) (paramNames: string list) =
            if idx >= segments.Length then
                { node with Handlers = node.Handlers |> Map.add method' (composed, List.rev paramNames) }
            else
                let seg = segments.[idx]
                if seg.[0] = '*' then
                    let paramName = seg.Substring(1)
                    let handlers =
                        match node.WildcardChild with
                        | Some existing -> existing
                        | None -> Map.empty
                    { node with WildcardChild = Some (handlers |> Map.add method' (composed, List.rev (paramName :: paramNames))) }
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
                        match node.StaticChildren |> Map.tryFind seg with
                        | Some existing -> existing
                        | None -> emptyNode ()
                    let updated = insert child (idx + 1) paramNames
                    { node with StaticChildren = node.StaticChildren |> Map.add seg updated }

        insert root 0 []

    let lookup (method': string) (path: string) (root: TrieNode) : (Handler * IReadOnlyDictionary<string, string>) option =
        let segments = splitPath path

        // During lookup, collect param segment values (positional) on the way down
        let rec search (node: TrieNode) (idx: int) (paramValues: string list) =
            if idx >= segments.Length then
                match node.Handlers |> Map.tryFind method' with
                | Some (h, paramNames) ->
                    let dict = Dictionary<string, string>()
                    let values = List.rev paramValues
                    List.iter2 (fun name value -> dict.[name] <- value) paramNames values
                    Some (h, dict :> IReadOnlyDictionary<_, _>)
                | None -> None
            else
                let seg = segments.[idx]
                match node.StaticChildren |> Map.tryFind seg with
                | Some child ->
                    match search child (idx + 1) paramValues with
                    | Some _ as result -> result
                    | None -> tryParam node seg idx paramValues
                | None -> tryParam node seg idx paramValues

        and tryParam (node: TrieNode) (seg: string) (idx: int) (paramValues: string list) =
            match node.ParamChild with
            | Some child ->
                match search child (idx + 1) (seg :: paramValues) with
                | Some _ as result -> result
                | None -> tryWildcard node idx paramValues
            | None -> tryWildcard node idx paramValues

        and tryWildcard (node: TrieNode) (idx: int) (paramValues: string list) =
            match node.WildcardChild with
            | Some handlers ->
                match handlers |> Map.tryFind method' with
                | Some (h, paramNames) ->
                    let captured = System.String.Join("/", segments, idx, segments.Length - idx)
                    let dict = Dictionary<string, string>()
                    let values = List.rev (captured :: paramValues)
                    List.iter2 (fun name value -> dict.[name] <- value) paramNames values
                    Some (h, dict :> IReadOnlyDictionary<_, _>)
                | None -> None
            | None -> None

        if segments.Length = 0 then
            match root.Handlers |> Map.tryFind method' with
            | Some (h, _) ->
                let dict = Dictionary<string, string>() :> IReadOnlyDictionary<_, _>
                Some (h, dict)
            | None -> None
        else
            search root 0 []
