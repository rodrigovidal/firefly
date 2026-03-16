namespace Fire

open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

[<Struct>]
type Request(ctx: HttpContext, routeParams: IReadOnlyDictionary<string, string>) =

    member _.Path = ctx.Request.Path.Value
    member _.Method = ctx.Request.Method
    member _.Params = routeParams

    member _.Query : IReadOnlyDictionary<string, string> =
        let q = ctx.Request.Query
        let d = Dictionary<string, string>(q.Count)
        for kvp in q do
            d.[kvp.Key] <- kvp.Value.ToString()
        d :> IReadOnlyDictionary<_, _>

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

    member _.Raw = ctx
