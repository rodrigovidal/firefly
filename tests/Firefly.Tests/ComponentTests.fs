module Firefly.Tests.ComponentTests

open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``Component.client creates Element with data-fire-component attr`` () =
    let node = Component.client "LikeButton" {| userId = 1 |}
    match node with
    | Element("div", attrs, []) ->
        attrs |> List.exists (fun a ->
            match a with Data("fire-component", "LikeButton") -> true | _ -> false)
        |> should be True
    | _ -> failwith "expected div element with no children"

[<Fact>]
let ``Component.client serializes props as JSON in data-fire-props`` () =
    let node = Component.client "Counter" {| count = 42 |}
    match node with
    | Element("div", attrs, []) ->
        let propsAttr =
            attrs |> List.tryPick (fun a ->
                match a with Data("fire-props", v) -> Some v | _ -> None)
        propsAttr |> Option.isSome |> should be True
        propsAttr.Value |> should haveSubstring "42"
    | _ -> failwith "expected div element"

[<Fact>]
let ``Component.client renders correct HTML via Render.toHtml`` () =
    let html = Component.client "Btn" {| label = "Click" |} |> Render.toHtml
    html |> should haveSubstring "data-fire-component"
    html |> should haveSubstring "Btn"
    html |> should haveSubstring "Click"

[<Fact>]
let ``Component.client composes inside Html tree`` () =
    let html =
        Html.div [
            Html.h1 [ Text "Hello" ]
            Component.client "Widget" {| id = 5 |}
        ]
        |> Render.toHtml
    html |> should haveSubstring "<h1>Hello</h1>"
    html |> should haveSubstring "data-fire-component"
    html |> should haveSubstring "Widget"

[<Fact>]
let ``Component.client with empty props`` () =
    let node = Component.client "Empty" {||}
    match node with
    | Element("div", attrs, []) ->
        attrs |> List.exists (fun a ->
            match a with Data("fire-props", _) -> true | _ -> false)
        |> should be True
    | _ -> failwith "expected div element"
