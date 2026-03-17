namespace Fire

open System
open System.Security.Cryptography

[<RequireQualifiedAccess>]
module Csrf =

    let private tokenKey = "fire.csrf.token"
    let private cookieName = "_fire_csrf"
    let private headerName = "X-CSRF-Token"
    let private formFieldName = "_csrf"

    /// Generate a random CSRF token
    let private generateToken () =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))

    /// Get or create a CSRF token for the current request.
    /// Stores it in a cookie and makes it available to views.
    let token (req: Request) : string =
        match req.Cookie cookieName with
        | Some existing -> existing
        | None ->
            let t = generateToken()
            // Store in HttpContext.Items so the response can set the cookie
            req.Raw.Items.[tokenKey] <- t
            t

    /// Middleware that validates CSRF tokens on state-changing methods (POST, PUT, PATCH, DELETE).
    /// The token must be present as X-CSRF-Token header or _csrf form field.
    let middleware : Middleware =
        fun next req -> task {
            let method = req.Method.ToUpperInvariant()
            if method = "GET" || method = "HEAD" || method = "OPTIONS" then
                let! response = next req
                // Set CSRF cookie if a new token was generated
                match req.Raw.Items.TryGetValue(tokenKey) with
                | true, token ->
                    return response |> Response.cookie cookieName (token :?> string)
                | false, _ -> return response
            else
                // Validate token
                let cookieToken = req.Cookie cookieName
                let submittedToken =
                    match req.Header headerName with
                    | Some t -> Some t
                    | None ->
                        // Check form field (for traditional form submissions)
                        // Read from query as fallback
                        req.QueryParam formFieldName

                match cookieToken, submittedToken with
                | Some ct, Some st when ct = st ->
                    let! response = next req
                    return response
                | _ ->
                    return Response.json {| error = "CSRF token mismatch" |} |> Response.status 403
        }
