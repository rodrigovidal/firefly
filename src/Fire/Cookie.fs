namespace Fire

type CookieOptions = {
    MaxAge: int option
    Path: string option
    Domain: string option
    Secure: bool
    HttpOnly: bool
    SameSite: string option
}

[<RequireQualifiedAccess>]
module Cookie =
    let defaults = {
        MaxAge = None
        Path = None
        Domain = None
        Secure = false
        HttpOnly = false
        SameSite = None
    }

    let maxAge seconds opts = { opts with MaxAge = Some seconds }
    let path p opts = { opts with Path = Some p }
    let domain d opts = { opts with Domain = Some d }
    let secure opts = { opts with Secure = true }
    let httpOnly opts = { opts with HttpOnly = true }
    let sameSite s opts = { opts with SameSite = Some s }

    let internal buildHeaderValue (name: string) (value: string) (opts: CookieOptions) =
        let parts = System.Collections.Generic.List<string>()
        parts.Add($"{name}={value}")
        match opts.MaxAge with Some s -> parts.Add($"Max-Age={s}") | None -> ()
        match opts.Path with Some p -> parts.Add($"Path={p}") | None -> ()
        match opts.Domain with Some d -> parts.Add($"Domain={d}") | None -> ()
        if opts.Secure then parts.Add("Secure")
        if opts.HttpOnly then parts.Add("HttpOnly")
        match opts.SameSite with Some s -> parts.Add($"SameSite={s}") | None -> ()
        System.String.Join("; ", parts)

    /// Set a cookie with options on a Response. Pipe-friendly.
    let set name value (opts: CookieOptions) (r: Response) : Response =
        let headerValue = buildHeaderValue name value opts
        { r with Headers = ("Set-Cookie", headerValue) :: r.Headers }
