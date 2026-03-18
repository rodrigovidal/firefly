namespace Fire

open System.Text.Json

type QueryEntry = { Key: string; Data: obj }

type QueryCache() =
    let entries = System.Collections.Generic.List<QueryEntry>()

    member _.Add(key: string, data: obj) =
        entries.Add({ Key = key; Data = data })

    member _.Entries = entries |> Seq.toList

    member _.DehydrateScript() : Node =
        if entries.Count = 0 then Empty
        else
            let json =
                entries
                |> Seq.map (fun e ->
                    let data = JsonSerializer.Serialize(e.Data)
                    $"""{{"queryKey":["{e.Key}"],"state":{{"data":{data}}}}}""")
                |> String.concat ","
            Raw $"""<script>window.__FIRE_QUERY_STATE__=[{json}]</script>"""

[<RequireQualifiedAccess>]
module Query =
    let prefetch (key: string) (fetch: unit -> System.Threading.Tasks.Task<'T>) (cache: QueryCache) = task {
        let! result = fetch ()
        cache.Add(key, result :> obj)
        return result
    }
