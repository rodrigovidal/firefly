namespace FireApp.Views

open Fire
open FireApp.Components
open FireApp.Layouts

module PageView =

    let home () =
        let content =
            Html.section ([ Class "hero" ], [
                Html.p ([ Class "eyebrow" ], [ Text "Fire starter" ])
                Html.h1 [ Text "Opinionated by default." ]
                Html.p [ Text "Controllers, views, layouts, assets, config, and tests already have a home." ]
                Html.div ([ Class "actions" ], [
                    Html.a ([ Class "button primary"; Href "/health" ], [ Text "Health JSON" ])
                    Html.a ([ Class "button secondary"; Href "/assets/css/app.css" ], [ Text "CSS asset" ])
                ])
                CoreComponents.featureGrid ()
            ])

        View.page "FireApp" content
        |> View.withLayout RootLayout.render
        |> View.render
