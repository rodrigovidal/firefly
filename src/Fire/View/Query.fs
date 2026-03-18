namespace Fire

open System.Text.Json

type QueryEntry = { Key: string; Data: obj }

type QueryCache() =
    let entries = System.Collections.Concurrent.ConcurrentQueue<QueryEntry>()

    member _.Add(key: string, data: obj) =
        entries.Enqueue({ Key = key; Data = data })

    member _.Entries = entries |> Seq.toList

    member _.DehydrateScript() : Node =
        if entries.IsEmpty then Empty
        else
            let now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            let json =
                entries
                |> Seq.map (fun e ->
                    let data = JsonSerializer.Serialize(e.Data)
                    $"""{{"queryKey":["{e.Key}"],"state":{{"data":{data},"dataUpdateCount":1,"dataUpdatedAt":{now},"status":"success","fetchStatus":"idle"}}}}""")
                |> String.concat ","
            Raw $"""<script>window.__FIRE_QUERY_STATE__={{"mutations":[],"queries":[{json}]}}</script>"""

[<RequireQualifiedAccess>]
module Query =
    let prefetch (key: string) (fetch: unit -> System.Threading.Tasks.Task<'T>) (cache: QueryCache) = task {
        let! result = fetch ()
        cache.Add(key, result :> obj)
        return result
    }
