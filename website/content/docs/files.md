---
title: "Files"
description: "Multipart uploads and streaming file downloads."
group: "Features"
order: 1
---

# File Uploads and Downloads

## Uploading Files

Access uploaded files from multipart form requests:

```fsharp
// Get all uploaded files
let uploadAll (req: Request) = task {
    let! files = req.Files()
    for file in files do
        do! UploadedFile.saveTo $"/uploads/{file.FileName}" file
    return Response.json {| count = files.Length |}
}

// Get a specific file by form field name
let uploadAvatar (req: Request) = task {
    match! req.File("avatar") with
    | Some file ->
        let! bytes = UploadedFile.readAllBytes file
        // process bytes...
        return Response.json {| size = file.Length |}
    | None ->
        return Response.json {| error = "No avatar file uploaded" |} |> Response.status 400
}
```

### UploadedFile Properties

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Form field name |
| `FileName` | `string` | Original file name |
| `ContentType` | `string` | MIME type |
| `Length` | `int64` | File size in bytes |
| `Stream` | `Stream` | File content stream |

### UploadedFile Helpers

```fsharp
// Save to disk
do! UploadedFile.saveTo "/path/to/output.png" file

// Read entire file into memory
let! bytes = UploadedFile.readAllBytes file
```

## Limiting Upload Size

Use the `Upload.maxSize` middleware to reject oversized requests:

```fsharp
Route.start
|> Route.group "/upload" (fun t ->
    t
    |> Route.middleware (Upload.maxSize (5L * 1024L * 1024L))  // 5 MB
    |> Route.post "/avatar" uploadAvatar
    |> Route.post "/documents" uploadAll
)
```

Returns 413 if `Content-Length` exceeds the limit.

## Downloading Files

Send files as responses:

```fsharp
// Serve a file with auto-detected content type
let downloadReport (req: Request) = task {
    return Response.file "reports/annual-2024.pdf"
}

// Force download with a specific filename
let exportData (req: Request) = task {
    return
        Response.file "data/export.csv"
        |> Response.download "export-2024.csv"
}

// Inline display (e.g., for images/PDFs in browser)
let viewImage (req: Request) = task {
    return
        Response.file "images/photo.jpg"
        |> Response.inline'
}
```

`Response.file` auto-detects the content type from the file extension (HTML, CSS, JS, JSON, PNG, JPEG, GIF, SVG, PDF, fonts, and more).

## Streaming Responses

For dynamically generated content:

```fsharp
let generateCsv (req: Request) = task {
    let stream = new System.IO.MemoryStream()
    // write CSV data to stream...
    stream.Position <- 0L
    return
        Response.stream stream
        |> Response.header "Content-Type" "text/csv"
        |> Response.download "data.csv"
}
```

