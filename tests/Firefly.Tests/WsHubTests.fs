module Firefly.Tests.WsHubTests

open System.Threading.Channels
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Firefly

type Msg = { Text: string }

let private readOne (reader: ChannelReader<'T>) : Task<'T> = task {
    let! ok = reader.WaitToReadAsync()
    ok |> should be True
    let mutable item = Unchecked.defaultof<'T>
    reader.TryRead(&item) |> should be True
    return item
}

[<Fact>]
let ``Broadcast reaches every member of the room only`` () = task {
    let hub = WsHub<Msg>()
    let (_, r1) = hub.Subscribe("room-a")
    let (_, r2) = hub.Subscribe("room-a")
    let (_, r3) = hub.Subscribe("room-b")
    hub.Broadcast("room-a", { Text = "hi" })
    let! m1 = readOne r1
    let! m2 = readOne r2
    m1.Text |> should equal "hi"
    m2.Text |> should equal "hi"
    r3.Count |> should equal 0
}

[<Fact>]
let ``BroadcastAll reaches every room`` () = task {
    let hub = WsHub<Msg>()
    let (_, r1) = hub.Subscribe("room-a")
    let (_, r2) = hub.Subscribe("room-b")
    hub.BroadcastAll({ Text = "all" })
    let! m1 = readOne r1
    let! m2 = readOne r2
    m1.Text |> should equal "all"
    m2.Text |> should equal "all"
}

[<Fact>]
let ``Send targets a single connection`` () = task {
    let hub = WsHub<Msg>()
    let (id1, r1) = hub.Subscribe("room-a")
    let (_, r2) = hub.Subscribe("room-a")
    hub.Send(id1, { Text = "just you" })
    let! m1 = readOne r1
    m1.Text |> should equal "just you"
    r2.Count |> should equal 0
}

[<Fact>]
let ``Unsubscribe removes membership and completes the reader`` () = task {
    let hub = WsHub<Msg>()
    let (id, r) = hub.Subscribe("x")
    hub.RoomCount("x") |> should equal 1
    hub.Count |> should equal 1
    hub.Unsubscribe(id)
    hub.RoomCount("x") |> should equal 0
    hub.Count |> should equal 0
    // A later broadcast is not delivered, and the reader is completed.
    hub.Broadcast("x", { Text = "late" })
    let! canRead = r.WaitToReadAsync()
    canRead |> should be False
}

[<Fact>]
let ``RoomCount and Count track multiple rooms`` () =
    let hub = WsHub<Msg>()
    hub.Subscribe("a") |> ignore
    hub.Subscribe("a") |> ignore
    hub.Subscribe("b") |> ignore
    hub.RoomCount("a") |> should equal 2
    hub.RoomCount("b") |> should equal 1
    hub.RoomCount("missing") |> should equal 0
    hub.Count |> should equal 3

[<Fact>]
let ``Default room is empty string`` () =
    let hub = WsHub<Msg>()
    hub.Subscribe() |> ignore
    hub.RoomCount("") |> should equal 1
    hub.Count |> should equal 1

// --- Distributed fan-out over a shared backplane (two hubs = two nodes) ---

[<Fact>]
let ``Broadcast fans out to another node over the backplane`` () = task {
    let bus = PubSub.inProcess ()
    let nodeA = WsHub<Msg>("chat", bus)
    let nodeB = WsHub<Msg>("chat", bus)
    // A member connected on node B, in room "room-a"
    let (_, rB) = nodeB.Subscribe("room-a")
    // Broadcast originates on node A
    nodeA.Broadcast("room-a", { Text = "cross-node" })
    let! m = readOne rB
    m.Text |> should equal "cross-node"
}

[<Fact>]
let ``BroadcastAll fans out to another node`` () = task {
    let bus = PubSub.inProcess ()
    let nodeA = WsHub<Msg>("chat", bus)
    let nodeB = WsHub<Msg>("chat", bus)
    let (_, rB) = nodeB.Subscribe("any-room")
    nodeA.BroadcastAll({ Text = "everyone" })
    let! m = readOne rB
    m.Text |> should equal "everyone"
}

[<Fact>]
let ``Broadcast delivers locally exactly once with a backplane`` () = task {
    let bus = PubSub.inProcess ()
    let node = WsHub<Msg>("chat", bus)
    let (_, r) = node.Subscribe("room-a")
    // The publisher's own local member must not get a duplicate from the echo.
    node.Broadcast("room-a", { Text = "once" })
    let! m = readOne r
    m.Text |> should equal "once"
    r.Count |> should equal 0 // no second copy queued
}

[<Fact>]
let ``Hubs with different names do not cross-talk`` () =
    let bus = PubSub.inProcess ()
    let chat = WsHub<Msg>("chat", bus)
    let alerts = WsHub<Msg>("alerts", bus)
    let (_, rAlerts) = alerts.Subscribe("room-a")
    chat.Broadcast("room-a", { Text = "chat-only" })
    rAlerts.Count |> should equal 0
