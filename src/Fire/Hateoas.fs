namespace Firefly

type HateoasLink = { Rel: string; Href: string; Method: string }

[<RequireQualifiedAccess>]
module Hateoas =
    let link (rel: string) (method': string) (href: string) : HateoasLink =
        { Rel = rel; Href = href; Method = method' }

    let self (href: string) = link "self" "GET" href

    let resolve (paramMap: (string * string) list) (l: HateoasLink) : HateoasLink =
        let href = paramMap |> List.fold (fun (acc: string) (k, v) -> acc.Replace($":{k}", v)) l.Href
        { l with Href = href }

    let respond (links: HateoasLink list) (data: 'T) : Response =
        Response.json {| data = data; _links = links |> List.map (fun l -> {| rel = l.Rel; href = l.Href; httpMethod = l.Method |}) |}
