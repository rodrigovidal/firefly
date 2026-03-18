namespace FireApp.Layouts

open Fire

module RootLayout =

    let render (title: string) (content: string) =
        let head = Render.toHtml (Fragment [
            Vite.reactRefresh ()
            Vite.styles "src/FireApp/Assets/js/main.tsx"
        ])
        let scripts = Render.toHtml (Vite.script "src/FireApp/Assets/js/main.tsx")
        $"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{System.Net.WebUtility.HtmlEncode title}</title>
  <link rel="stylesheet" href="/assets/css/app.css">
  {head}
</head>
<body>
  <main class="shell">
    {content}
  </main>
  {scripts}
</body>
</html>"""
