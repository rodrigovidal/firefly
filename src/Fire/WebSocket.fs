namespace Fire

open System
open System.Net.WebSockets
open System.Text
open System.Threading
open System.Threading.Tasks

type WsMessage =
    | WsText of string
    | WsBinary of byte[]
    | WsClose

type WsConn(ws: WebSocket, ct: CancellationToken) =
    member _.Send(text: string) : Task = task {
        let bytes = Encoding.UTF8.GetBytes(text)
        do! ws.SendAsync(ReadOnlyMemory(bytes), WebSocketMessageType.Text, true, ct)
    }

    member _.SendBytes(data: byte[]) : Task = task {
        do! ws.SendAsync(ReadOnlyMemory(data), WebSocketMessageType.Binary, true, ct)
    }

    member _.Receive() : Task<WsMessage> = task {
        use ms = new System.IO.MemoryStream()
        let buffer = Array.zeroCreate<byte> 4096
        let mutable result = Unchecked.defaultof<ValueWebSocketReceiveResult>
        let mutable cont = true
        while cont do
            let! r = ws.ReceiveAsync(Memory(buffer), ct)
            result <- r
            if r.MessageType = WebSocketMessageType.Close then
                cont <- false
            else
                ms.Write(buffer, 0, r.Count)
                if r.EndOfMessage then cont <- false
        match result.MessageType with
        | WebSocketMessageType.Text ->
            return WsText(Encoding.UTF8.GetString(ms.ToArray()))
        | WebSocketMessageType.Binary ->
            return WsBinary(ms.ToArray())
        | _ ->
            return WsClose
    }

    member _.Close(?status: WebSocketCloseStatus, ?reason: string) : Task = task {
        let s = defaultArg status WebSocketCloseStatus.NormalClosure
        let r = defaultArg reason ""
        if ws.State = WebSocketState.Open then
            do! ws.CloseAsync(s, r, ct)
    }

    member _.IsOpen = ws.State = WebSocketState.Open
    member _.CancellationToken = ct

[<RequireQualifiedAccess>]
module WS =

    let handler (fn: WsConn -> Request -> Task<unit>) : Handler =
        fun (req: Request) -> task {
            let ctx = req.Raw
            if ctx.WebSockets.IsWebSocketRequest then
                let! ws = ctx.WebSockets.AcceptWebSocketAsync()
                let conn = WsConn(ws, ctx.RequestAborted)
                try
                    do! fn conn req
                with
                | :? OperationCanceledException -> ()
                | :? WebSocketException -> ()
                try
                    if conn.IsOpen then do! conn.Close()
                with _ -> ()
                return { Status = 200; Headers = []; Body = Empty }
            else
                return
                    Response.text "WebSocket connection expected"
                    |> Response.status 400
        }
