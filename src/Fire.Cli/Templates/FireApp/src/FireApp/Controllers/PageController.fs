namespace FireApp.Controllers

open Fire
open FireApp.Views

module PageController =

    let home (_req: Request) = task {
        return PageView.home () |> Response.html
    }
