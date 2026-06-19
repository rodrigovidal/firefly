namespace Firefly

open System.Threading.Tasks

[<RequireQualifiedAccess>]
module Bulk =
    let execute (op: 'TInput -> Task<Result<'TOutput, string>>) (items: 'TInput list) : Task<Response> = task {
        let results = System.Collections.Generic.List<{| index: int; status: string; data: obj |}>()
        let mutable succeeded = 0
        let mutable failed = 0
        for i in 0..items.Length-1 do
            match! op items.[i] with
            | Ok value ->
                succeeded <- succeeded + 1
                results.Add({| index = i; status = "success"; data = box value |})
            | Error err ->
                failed <- failed + 1
                results.Add({| index = i; status = "error"; data = box {| error = err |} |})
        let status = if succeeded = 0 && failed > 0 then 422 elif failed > 0 then 207 else 200
        return Response.json {| succeeded = succeeded; failed = failed; total = items.Length; results = results |> Seq.toList |} |> Response.status status
    }

    let handler (op: 'TInput -> Task<Result<'TOutput, string>>) : Handler =
        fun req -> task {
            try
                let! items = req.Json<'TInput list>()
                return! execute op items
            with ex ->
                return Response.json {| error = $"Invalid request body: {ex.Message}" |} |> Response.status 400
        }
