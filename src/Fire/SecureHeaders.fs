namespace Fire

[<RequireQualifiedAccess>]
module SecureHeaders =

    /// Middleware that adds security headers to every response.
    /// Similar to Helmet.js for Express.
    let middleware : Middleware =
        fun next req -> task {
            let! response = next req
            return response
                |> Response.header "X-Content-Type-Options" "nosniff"
                |> Response.header "X-Frame-Options" "DENY"
                |> Response.header "X-XSS-Protection" "0"
                |> Response.header "Referrer-Policy" "strict-origin-when-cross-origin"
                |> Response.header "Content-Security-Policy" "default-src 'self'"
                |> Response.header "Strict-Transport-Security" "max-age=31536000; includeSubDomains"
                |> Response.header "Permissions-Policy" "camera=(), microphone=(), geolocation=()"
        }

    /// Configurable secure headers
    type SecureHeadersConfig = {
        ContentTypeOptions: string option
        FrameOptions: string option
        XssProtection: string option
        ReferrerPolicy: string option
        ContentSecurityPolicy: string option
        StrictTransportSecurity: string option
        PermissionsPolicy: string option
    }

    let defaults = {
        ContentTypeOptions = Some "nosniff"
        FrameOptions = Some "DENY"
        XssProtection = Some "0"
        ReferrerPolicy = Some "strict-origin-when-cross-origin"
        ContentSecurityPolicy = Some "default-src 'self'"
        StrictTransportSecurity = Some "max-age=31536000; includeSubDomains"
        PermissionsPolicy = Some "camera=(), microphone=(), geolocation=()"
    }

    let contentSecurityPolicy csp config = { config with ContentSecurityPolicy = Some csp }
    let frameOptions fo config = { config with FrameOptions = Some fo }
    let referrerPolicy rp config = { config with ReferrerPolicy = Some rp }
    let noHsts config = { config with StrictTransportSecurity = None }

    let build (config: SecureHeadersConfig) : Middleware =
        fun next req -> task {
            let! response = next req
            let mutable r = response
            match config.ContentTypeOptions with Some v -> r <- r |> Response.header "X-Content-Type-Options" v | None -> ()
            match config.FrameOptions with Some v -> r <- r |> Response.header "X-Frame-Options" v | None -> ()
            match config.XssProtection with Some v -> r <- r |> Response.header "X-XSS-Protection" v | None -> ()
            match config.ReferrerPolicy with Some v -> r <- r |> Response.header "Referrer-Policy" v | None -> ()
            match config.ContentSecurityPolicy with Some v -> r <- r |> Response.header "Content-Security-Policy" v | None -> ()
            match config.StrictTransportSecurity with Some v -> r <- r |> Response.header "Strict-Transport-Security" v | None -> ()
            match config.PermissionsPolicy with Some v -> r <- r |> Response.header "Permissions-Policy" v | None -> ()
            return r
        }
