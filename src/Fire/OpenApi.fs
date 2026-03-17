namespace Fire

open System.Text.Json

[<RequireQualifiedAccess>]
module OpenApi =

    let private convertPattern (pattern: string) =
        pattern.Split('/')
        |> Array.map (fun seg ->
            if seg.Length > 0 && seg.[0] = ':' then "{" + seg.Substring(1) + "}"
            elif seg.Length > 0 && seg.[0] = '*' then "{" + seg.Substring(1) + "}"
            else seg)
        |> fun parts -> System.String.Join("/", parts)

    let private extractParams (pattern: string) =
        pattern.Split('/')
        |> Array.choose (fun seg ->
            if seg.Length > 0 && (seg.[0] = ':' || seg.[0] = '*') then Some (seg.Substring(1))
            else None)
        |> Array.toList

    let generate (title: string) (version: string) (routes: RouteTable) : string =
        let grouped =
            routes.Routes
            |> List.groupBy (fun r -> convertPattern r.Pattern)

        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writer.WriteString("openapi", "3.0.0")

        writer.WriteStartObject("info")
        writer.WriteString("title", title)
        writer.WriteString("version", version)
        writer.WriteEndObject()

        writer.WriteStartObject("paths")
        for (path, entries) in grouped do
            writer.WriteStartObject(path)
            for entry in entries do
                let method' = entry.Method.ToLowerInvariant()
                writer.WriteStartObject(method')
                let paramNames = extractParams entry.Pattern
                if paramNames.Length > 0 then
                    writer.WriteStartArray("parameters")
                    for name in paramNames do
                        writer.WriteStartObject()
                        writer.WriteString("name", name)
                        writer.WriteString("in", "path")
                        writer.WriteBoolean("required", true)
                        writer.WriteStartObject("schema")
                        writer.WriteString("type", "string")
                        writer.WriteEndObject()
                        writer.WriteEndObject()
                    writer.WriteEndArray()
                writer.WriteEndObject()
            writer.WriteEndObject()
        writer.WriteEndObject()

        writer.WriteEndObject()
        writer.Flush()

        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    let handler (title: string) (version: string) (routes: RouteTable) : Handler =
        let specBytes = System.Text.Encoding.UTF8.GetBytes(generate title version routes)
        fun _ -> task {
            return { Status = 200; Headers = [("Content-Type", "application/json")]; Body = Json specBytes }
        }
