namespace Firefly

[<RequireQualifiedAccess>]
type PageParams =
    | Offset of offset: int * limit: int
    | Cursor of cursor: string * limit: int

type PageMeta = {
    Limit: int
    HasMore: bool
    Next: string option
    Previous: string option
    Total: int option
}

[<RequireQualifiedAccess>]
module Pagination =
    let defaultLimit = 20
    let maxLimit = 100

    let parse (req: Request) : PageParams =
        let limit =
            req.QueryParam "limit"
            |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
            |> Option.defaultValue defaultLimit
            |> min maxLimit |> max 1
        match req.QueryParam "cursor" with
        | Some c -> PageParams.Cursor(c, limit)
        | None ->
            let offset =
                req.QueryParam "offset"
                |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                |> Option.defaultValue 0 |> max 0
            PageParams.Offset(offset, limit)

    let offsetMeta (basePath: string) (offset: int) (limit: int) (total: int) : PageMeta =
        let hasMore = offset + limit < total
        { Limit = limit; HasMore = hasMore
          Next = if hasMore then Some $"{basePath}?offset={offset + limit}&limit={limit}" else None
          Previous = if offset > 0 then Some $"{basePath}?offset={max 0 (offset - limit)}&limit={limit}" else None
          Total = Some total }

    let cursorMeta (basePath: string) (limit: int) (nextCursor: string option) : PageMeta =
        { Limit = limit; HasMore = nextCursor.IsSome
          Next = nextCursor |> Option.map (fun c -> $"{basePath}?cursor={System.Net.WebUtility.UrlEncode(c)}&limit={limit}")
          Previous = None; Total = None }

    let respond (meta: PageMeta) (items: 'T list) : Response =
        Response.json {| data = items; meta = {| limit = meta.Limit; hasMore = meta.HasMore; next = meta.Next; previous = meta.Previous; total = meta.Total |} |}
