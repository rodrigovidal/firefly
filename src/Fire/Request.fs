namespace Fire

open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

module internal RequestKeys =
    [<Literal>]
    let QueryCacheItemKey = "fire.query.cache"

    [<Literal>]
    let RequestIdItemKey = "fire.request-id"

    [<Literal>]
    let CorrelationIdItemKey = "fire.correlation-id"

[<Struct>]
type Request(ctx: HttpContext, routeParams: IReadOnlyDictionary<string, string>) =

    member _.Path = ctx.Request.Path.Value
    member _.Method = ctx.Request.Method
    member _.Params = routeParams

    member _.Query : IReadOnlyDictionary<string, string> =
        match ctx.Items.TryGetValue(RequestKeys.QueryCacheItemKey) with
        | true, cached -> cached :?> IReadOnlyDictionary<string, string>
        | false, _ ->
            let q = ctx.Request.Query
            let d = Dictionary<string, string>(q.Count)
            for kvp in q do
                d.[kvp.Key] <- kvp.Value.ToString()
            let result = d :> IReadOnlyDictionary<_, _>
            ctx.Items.[RequestKeys.QueryCacheItemKey] <- result
            result

    member _.Header (name: string) : string option =
        match ctx.Request.Headers.TryGetValue(name) with
        | true, values -> Some (values.ToString())
        | false, _ -> None

    member _.Headers (name: string) : string list =
        match ctx.Request.Headers.TryGetValue(name) with
        | true, values -> values.ToArray() |> Array.toList
        | false, _ -> []

    member _.Body : Stream = ctx.Request.Body

    member _.Json<'T>() : Task<'T> =
        let body = ctx.Request.Body
        task {
            let! result = JsonSerializer.DeserializeAsync<'T>(body)
            return result
        }

    member _.QueryParam (name: string) : string option =
        match ctx.Request.Query.TryGetValue(name) with
        | true, values -> Some (values.ToString())
        | false, _ -> None

    member _.Text() : Task<string> =
        let body = ctx.Request.Body
        task {
            use reader = new StreamReader(body, Encoding.UTF8, leaveOpen = true)
            return! reader.ReadToEndAsync()
        }

    member _.Form() : Task<IReadOnlyDictionary<string, string>> =
        let request = ctx.Request
        task {
            let! form = request.ReadFormAsync()
            let d = Dictionary<string, string>(form.Count)
            for kvp in form do
                d.[kvp.Key] <- kvp.Value.ToString()
            return d :> IReadOnlyDictionary<_, _>
        }

    member _.Accepts (mediaType: string) : bool =
        match ctx.Request.Headers.TryGetValue("Accept") with
        | true, values -> values.ToString().Contains(mediaType)
        | false, _ -> false

    member _.ContentType : string option =
        match ctx.Request.ContentType with
        | null | "" -> None
        | ct -> Some ct

    member _.Cookie (name: string) : string option =
        match ctx.Request.Cookies.TryGetValue(name) with
        | true, value -> Some value
        | false, _ -> None

    member _.RequestId : string option =
        match ctx.Items.TryGetValue(RequestKeys.RequestIdItemKey) with
        | true, requestId -> Some (requestId :?> string)
        | false, _ ->
            match ctx.Request.Headers.TryGetValue("X-Request-Id") with
            | true, values -> Some (values.ToString())
            | false, _ -> None

    member _.CorrelationId : string option =
        match ctx.Items.TryGetValue(RequestKeys.CorrelationIdItemKey) with
        | true, correlationId -> Some (correlationId :?> string)
        | false, _ ->
            match ctx.Request.Headers.TryGetValue("X-Correlation-Id") with
            | true, values -> Some (values.ToString())
            | false, _ -> None

    /// Merge all input sources into a single dictionary: route params + query string + body (form or JSON).
    /// Priority: route params > body > query (later sources don't overwrite earlier ones).
    member _.All() : Task<IReadOnlyDictionary<string, string>> =
        let merged = Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        // 1. Query string (lowest priority)
        let query = ctx.Request.Query
        for kvp in query do
            merged.[kvp.Key] <- kvp.Value.ToString()
        // Capture before task (struct can't be captured in closures)
        let httpRequest = ctx.Request
        let contentType = ctx.Request.ContentType
        let params' = routeParams
        task {
            // 2. Body (form or JSON) — overwrites query
            let ct = if contentType = null then "" else contentType
            if ct.Contains("application/x-www-form-urlencoded") || ct.Contains("multipart/form-data") then
                let! form = httpRequest.ReadFormAsync()
                for kvp in form do
                    merged.[kvp.Key] <- kvp.Value.ToString()
            elif ct.Contains("application/json") then
                try
                    use reader = new StreamReader(httpRequest.Body, Encoding.UTF8, leaveOpen = true)
                    let! text = reader.ReadToEndAsync()
                    if not (System.String.IsNullOrWhiteSpace text) then
                        use doc = System.Text.Json.JsonDocument.Parse(text)
                        for prop in doc.RootElement.EnumerateObject() do
                            merged.[prop.Name] <- prop.Value.ToString()
                with _ -> ()
            // 3. Route params (highest priority) — overwrites body and query
            for kvp in params' do
                merged.[kvp.Key] <- kvp.Value
            return merged :> IReadOnlyDictionary<string, string>
        }

    member _.Raw = ctx
