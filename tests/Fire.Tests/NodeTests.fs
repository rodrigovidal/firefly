module Fire.Tests.NodeTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Text node holds string`` () =
    let node = Text "hello"
    match node with
    | Text s -> s |> should equal "hello"
    | _ -> failwith "expected Text"

[<Fact>]
let ``Element node holds tag, attrs, children`` () =
    let node = Element("div", [ Class "box" ], [ Text "hi" ])
    match node with
    | Element(tag, attrs, children) ->
        tag |> should equal "div"
        attrs |> should equal [ Class "box" ]
        children |> should equal [ Text "hi" ]
    | _ -> failwith "expected Element"

[<Fact>]
let ``Fragment holds list of nodes`` () =
    let node = Fragment [ Text "a"; Text "b" ]
    match node with
    | Fragment nodes -> nodes |> should haveLength 2
    | _ -> failwith "expected Fragment"

[<Fact>]
let ``Empty is Empty`` () =
    let node = Empty
    node |> should equal Empty

[<Fact>]
let ``Raw holds unescaped string`` () =
    let node = Raw "<b>bold</b>"
    match node with
    | Raw s -> s |> should equal "<b>bold</b>"
    | _ -> failwith "expected Raw"

[<Fact>]
let ``Boolean attrs have no value`` () =
    let attr = Disabled
    match attr with
    | Disabled -> ()
    | _ -> failwith "expected Disabled"

[<Fact>]
let ``Data attr holds key-value pair`` () =
    let attr = Data("toggle", "modal")
    match attr with
    | Data(k, v) ->
        k |> should equal "toggle"
        v |> should equal "modal"
    | _ -> failwith "expected Data"

[<Fact>]
let ``Html.div with children creates Element`` () =
    let node = Html.div [ Text "hello" ]
    match node with
    | Element("div", [], [ Text "hello" ]) -> ()
    | _ -> failwith "expected div element"

[<Fact>]
let ``Html.div with attrs and children creates Element`` () =
    let node = Html.div ([ Class "box" ], [ Text "hello" ])
    match node with
    | Element("div", [ Class "box" ], [ Text "hello" ]) -> ()
    | _ -> failwith "expected div element with attrs"

[<Fact>]
let ``Html.input is void element with attrs`` () =
    let node = Html.input [ Type "text"; Name "email" ]
    match node with
    | Element("input", [ Type "text"; Name "email" ], []) -> ()
    | _ -> failwith "expected input element"

[<Fact>]
let ``Html.br with no args creates empty br`` () =
    let node = Html.br ()
    match node with
    | Element("br", [], []) -> ()
    | _ -> failwith "expected br element"

[<Fact>]
let ``Html.element escape hatch works`` () =
    let node = Html.element "dialog" [ Text "content" ]
    match node with
    | Element("dialog", [], [ Text "content" ]) -> ()
    | _ -> failwith "expected dialog element"

[<Fact>]
let ``Nested elements compose`` () =
    let node =
        Html.div [
            Html.h1 [ Text "Title" ]
            Html.p [ Text "Body" ]
        ]
    match node with
    | Element("div", [], [ Element("h1", _, _); Element("p", _, _) ]) -> ()
    | _ -> failwith "expected nested structure"
