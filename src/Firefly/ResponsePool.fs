namespace Firefly

/// Pre-built common responses. These are allocated once and reused.
/// F# records are immutable so `ResponsePool.ok |> Response.header "X" "Y"` creates
/// a new record without mutating the original.
[<RequireQualifiedAccess>]
module ResponsePool =

    let ok = { Status = 200; Headers = []; Body = Empty }
    let notFound = { Status = 404; Headers = []; Body = Empty }
    let noContent = { Status = 204; Headers = []; Body = Empty }
    let unauthorized = { Status = 401; Headers = []; Body = Empty }
    let forbidden = { Status = 403; Headers = []; Body = Empty }
    let badRequest = { Status = 400; Headers = []; Body = Empty }
    let serverError = { Status = 500; Headers = []; Body = Empty }
