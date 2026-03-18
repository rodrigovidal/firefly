namespace FireApp.Components

open Fire

module CoreComponents =

    let featureGrid () =
        Html.section ([ Class "features" ], [
            Html.article [
                Html.h2 [ Text "Router" ]
                Html.p [ Text "Keep public pages, browser flows, and JSON endpoints in named modules." ]
            ]
            Html.article [
                Html.h2 [ Text "Endpoint" ]
                Html.p [ Text "Centralize middleware, runtime config, static files, and development tooling." ]
            ]
            Html.article [
                Html.h2 [ Text "Tests" ]
                Html.p [ Text "Start with fixtures, route tests, and integration coverage from day one." ]
            ]
        ])
