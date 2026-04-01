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
        let buffer = Array.zeroCreate<byte> 4096
        let! result = ws.ReceiveAsync(Memory(buffer), ct)
        match result.MessageType with
        | WebSocketMessageType.Text ->
            return WsText(Encoding.UTF8.GetString(buffer, 0, result.Count))
        | WebSocketMessageType.Binary ->
            let data = Array.zeroCreate<byte> result.Count
            Buffer.BlockCopy(buffer, 0, data, 0, result.Count)
            return WsBinary data
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
                if conn.IsOpen then
                    do! conn.Close()
                return { Status = 200; Headers = []; Body = Empty }
            else
                return
                    Response.text "WebSocket connection expected"
                    |> Response.status 400
        }
