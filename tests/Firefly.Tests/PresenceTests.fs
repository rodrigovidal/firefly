module Firefly.Tests.PresenceTests

open Xunit
open FsUnit.Xunit
open Firefly

type Meta = { Name: string }

[<Fact>]
let ``Track and list on a single node`` () =
    let p = Presence()
    p.Track("room-a", "u1", { Name = "Ada" })
    p.Track("room-a", "u2", { Name = "Linus" })
    let people = p.List<Meta>("room-a") |> List.sortBy fst
    people |> List.map fst |> should equal [ "u1"; "u2" ]
    (people |> List.map (snd >> fun m -> m.Name)) |> should equal [ "Ada"; "Linus" ]
    p.Count("room-a") |> should equal 2

[<Fact>]
let ``Untrack removes an entry`` () =
    let p = Presence()
    p.Track("room-a", "u1", { Name = "Ada" })
    p.Untrack("room-a", "u1")
    p.Count("room-a") |> should equal 0
    p.List<Meta>("room-a") |> List.isEmpty |> should be True

[<Fact>]
let ``OnChange fires for join and leave`` () =
    let p = Presence()
    let events = System.Collections.Generic.List<bool>()
    use _sub = p.OnChange(fun d -> events.Add(d.Joined))
    p.Track("room-a", "u1", { Name = "Ada" })
    p.Untrack("room-a", "u1")
    events |> List.ofSeq |> should equal [ true; false ]

[<Fact>]
let ``Presence replicates across nodes over a shared backplane`` () =
    let bus = PubSub.inProcess ()
    let nodeA = Presence("chat", bus)
    let nodeB = Presence("chat", bus)

    nodeA.Track("room-a", "u1", { Name = "Ada" })
    // u1 tracked on node A is visible from node B
    nodeB.List<Meta>("room-a") |> List.map fst |> should equal [ "u1" ]
    (nodeB.List<Meta>("room-a") |> List.head |> snd).Name |> should equal "Ada"

    nodeA.Untrack("room-a", "u1")
    nodeB.Count("room-a") |> should equal 0

[<Fact>]
let ``Presence under different names does not cross-talk`` () =
    let bus = PubSub.inProcess ()
    let chat = Presence("chat", bus)
    let game = Presence("game", bus)
    chat.Track("room-a", "u1", { Name = "Ada" })
    game.Count("room-a") |> should equal 0
