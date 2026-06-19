namespace FireApp.Controllers

open Firefly
open FireApp.Views

module PageController =

    let home (_req: Request) = task {
        return PageView.home ()
    }
