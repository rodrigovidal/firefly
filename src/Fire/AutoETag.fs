namespace Fire

[<RequireQualifiedAccess>]
module AutoETag =

    /// Middleware that automatically generates ETag headers from response body hash.
    /// Returns 304 Not Modified if the client's If-None-Match matches.
    /// Delegates to Cache.etag which handles GET/HEAD method guard and 304 with preserved headers.
    let middleware : Middleware = Cache.etag
