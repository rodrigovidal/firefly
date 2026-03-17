namespace FireApp.Views

open System
open FireApp.Components
open FireApp.Layouts

module PageView =

    let home () =
        let content =
            [
                "<section class=\"hero\">"
                "  <p class=\"eyebrow\">Fire starter</p>"
                "  <h1>Opinionated by default.</h1>"
                "  <p>Controllers, views, layouts, assets, config, and tests already have a home.</p>"
                "  <div class=\"actions\">"
                "    <a class=\"button primary\" href=\"/health\">Health JSON</a>"
                "    <a class=\"button secondary\" href=\"/assets/css/app.css\">CSS asset</a>"
                "  </div>"
                CoreComponents.featureGrid()
                "</section>"
            ]
            |> String.concat Environment.NewLine

        RootLayout.render "FireApp" content
