namespace Fire

open System.Collections.Generic

type TrieNode = {
    StaticChildren: Map<string, TrieNode>
    ParamChild: (string * TrieNode) option
    WildcardChild: (string * Map<string, Handler>) option
    Handlers: Map<string, Handler>
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

        let rec insert (node: TrieNode) (idx: int) =
            if idx >= segments.Length then
                { node with Handlers = node.Handlers |> Map.add method' composed }
            else
                let seg = segments.[idx]
                if seg.[0] = '*' then
                    let paramName = seg.Substring(1)
                    let handlers =
                        match node.WildcardChild with
                        | Some (_, existing) -> existing
                        | None -> Map.empty
                    { node with WildcardChild = Some (paramName, handlers |> Map.add method' composed) }
                elif seg.[0] = ':' then
                    let paramName = seg.Substring(1)
                    let child =
                        match node.ParamChild with
                        | Some (_, existing) -> existing
                        | None -> emptyNode ()
                    let updated = insert child (idx + 1)
                    { node with ParamChild = Some (paramName, updated) }
                else
                    let child =
                        match node.StaticChildren |> Map.tryFind seg with
                        | Some existing -> existing
                        | None -> emptyNode ()
                    let updated = insert child (idx + 1)
                    { node with StaticChildren = node.StaticChildren |> Map.add seg updated }

        insert root 0

    let lookup (method': string) (path: string) (root: TrieNode) : (Handler * IReadOnlyDictionary<string, string>) option =
        let segments = splitPath path

        let rec search (node: TrieNode) (idx: int) (ps: (string * string) list) =
            if idx >= segments.Length then
                match node.Handlers |> Map.tryFind method' with
                | Some h ->
                    let dict = Dictionary<string, string>()
                    for (k, v) in ps do dict.[k] <- v
                    Some (h, dict :> IReadOnlyDictionary<_, _>)
                | None -> None
            else
                let seg = segments.[idx]
                match node.StaticChildren |> Map.tryFind seg with
                | Some child ->
                    match search child (idx + 1) ps with
                    | Some _ as result -> result
                    | None -> tryParam node seg idx ps
                | None -> tryParam node seg idx ps

        and tryParam (node: TrieNode) (seg: string) (idx: int) (ps: (string * string) list) =
            match node.ParamChild with
            | Some (paramName, child) ->
                match search child (idx + 1) ((paramName, seg) :: ps) with
                | Some _ as result -> result
                | None -> tryWildcard node idx ps
            | None -> tryWildcard node idx ps

        and tryWildcard (node: TrieNode) (idx: int) (ps: (string * string) list) =
            match node.WildcardChild with
            | Some (paramName, handlers) ->
                match handlers |> Map.tryFind method' with
                | Some h ->
                    let captured = System.String.Join("/", segments, idx, segments.Length - idx)
                    let dict = Dictionary<string, string>()
                    for (k, v) in ((paramName, captured) :: ps) do dict.[k] <- v
                    Some (h, dict :> IReadOnlyDictionary<_, _>)
                | None -> None
            | None -> None

        if segments.Length = 0 then
            match root.Handlers |> Map.tryFind method' with
            | Some h ->
                let dict = Dictionary<string, string>() :> IReadOnlyDictionary<_, _>
                Some (h, dict)
            | None -> None
        else
            search root 0 []
