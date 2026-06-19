namespace Firefly

type Node =
    | Element of tag: string * attrs: Attr list * children: Node list
    | Text of string
    | Raw of string
    | Fragment of Node list
    | Empty

and Attr =
    | Class of string
    | Id of string
    | Href of string
    | Src of string
    | Type of string
    | Name of string
    | Value of string
    | Placeholder of string
    | Style of string
    | Disabled
    | Checked
    | Required
    | Readonly
    | Data of string * string
    | Custom of string * string

type Html =
    // Container / structural
    static member div (children: Node list) = Element("div", [], children)
    static member div (attrs: Attr list, children: Node list) = Element("div", attrs, children)
    static member span (children: Node list) = Element("span", [], children)
    static member span (attrs: Attr list, children: Node list) = Element("span", attrs, children)
    static member section (children: Node list) = Element("section", [], children)
    static member section (attrs: Attr list, children: Node list) = Element("section", attrs, children)
    static member nav (children: Node list) = Element("nav", [], children)
    static member nav (attrs: Attr list, children: Node list) = Element("nav", attrs, children)
    static member main (children: Node list) = Element("main", [], children)
    static member main (attrs: Attr list, children: Node list) = Element("main", attrs, children)
    static member header (children: Node list) = Element("header", [], children)
    static member header (attrs: Attr list, children: Node list) = Element("header", attrs, children)
    static member footer (children: Node list) = Element("footer", [], children)
    static member footer (attrs: Attr list, children: Node list) = Element("footer", attrs, children)
    static member article (children: Node list) = Element("article", [], children)
    static member article (attrs: Attr list, children: Node list) = Element("article", attrs, children)
    static member aside (children: Node list) = Element("aside", [], children)
    static member aside (attrs: Attr list, children: Node list) = Element("aside", attrs, children)

    // Headings
    static member h1 (children: Node list) = Element("h1", [], children)
    static member h1 (attrs: Attr list, children: Node list) = Element("h1", attrs, children)
    static member h2 (children: Node list) = Element("h2", [], children)
    static member h2 (attrs: Attr list, children: Node list) = Element("h2", attrs, children)
    static member h3 (children: Node list) = Element("h3", [], children)
    static member h3 (attrs: Attr list, children: Node list) = Element("h3", attrs, children)
    static member h4 (children: Node list) = Element("h4", [], children)
    static member h4 (attrs: Attr list, children: Node list) = Element("h4", attrs, children)
    static member h5 (children: Node list) = Element("h5", [], children)
    static member h5 (attrs: Attr list, children: Node list) = Element("h5", attrs, children)
    static member h6 (children: Node list) = Element("h6", [], children)
    static member h6 (attrs: Attr list, children: Node list) = Element("h6", attrs, children)

    // Text
    static member p (children: Node list) = Element("p", [], children)
    static member p (attrs: Attr list, children: Node list) = Element("p", attrs, children)
    static member a (children: Node list) = Element("a", [], children)
    static member a (attrs: Attr list, children: Node list) = Element("a", attrs, children)
    static member strong (children: Node list) = Element("strong", [], children)
    static member strong (attrs: Attr list, children: Node list) = Element("strong", attrs, children)
    static member em (children: Node list) = Element("em", [], children)
    static member em (attrs: Attr list, children: Node list) = Element("em", attrs, children)
    static member small (children: Node list) = Element("small", [], children)
    static member small (attrs: Attr list, children: Node list) = Element("small", attrs, children)
    static member code (children: Node list) = Element("code", [], children)
    static member code (attrs: Attr list, children: Node list) = Element("code", attrs, children)
    static member pre (children: Node list) = Element("pre", [], children)
    static member pre (attrs: Attr list, children: Node list) = Element("pre", attrs, children)
    static member blockquote (children: Node list) = Element("blockquote", [], children)
    static member blockquote (attrs: Attr list, children: Node list) = Element("blockquote", attrs, children)

    // Forms
    static member form (children: Node list) = Element("form", [], children)
    static member form (attrs: Attr list, children: Node list) = Element("form", attrs, children)
    static member label (children: Node list) = Element("label", [], children)
    static member label (attrs: Attr list, children: Node list) = Element("label", attrs, children)
    static member button (children: Node list) = Element("button", [], children)
    static member button (attrs: Attr list, children: Node list) = Element("button", attrs, children)
    static member select (children: Node list) = Element("select", [], children)
    static member select (attrs: Attr list, children: Node list) = Element("select", attrs, children)
    static member option (children: Node list) = Element("option", [], children)
    static member option (attrs: Attr list, children: Node list) = Element("option", attrs, children)
    static member textarea (children: Node list) = Element("textarea", [], children)
    static member textarea (attrs: Attr list, children: Node list) = Element("textarea", attrs, children)

    // Lists
    static member ul (children: Node list) = Element("ul", [], children)
    static member ul (attrs: Attr list, children: Node list) = Element("ul", attrs, children)
    static member ol (children: Node list) = Element("ol", [], children)
    static member ol (attrs: Attr list, children: Node list) = Element("ol", attrs, children)
    static member li (children: Node list) = Element("li", [], children)
    static member li (attrs: Attr list, children: Node list) = Element("li", attrs, children)

    // Tables
    static member table (children: Node list) = Element("table", [], children)
    static member table (attrs: Attr list, children: Node list) = Element("table", attrs, children)
    static member thead (children: Node list) = Element("thead", [], children)
    static member thead (attrs: Attr list, children: Node list) = Element("thead", attrs, children)
    static member tbody (children: Node list) = Element("tbody", [], children)
    static member tbody (attrs: Attr list, children: Node list) = Element("tbody", attrs, children)
    static member tr (children: Node list) = Element("tr", [], children)
    static member tr (attrs: Attr list, children: Node list) = Element("tr", attrs, children)
    static member td (children: Node list) = Element("td", [], children)
    static member td (attrs: Attr list, children: Node list) = Element("td", attrs, children)
    static member th (children: Node list) = Element("th", [], children)
    static member th (attrs: Attr list, children: Node list) = Element("th", attrs, children)

    // Void elements (no children)
    static member br () = Element("br", [], [])
    static member br (attrs: Attr list) = Element("br", attrs, [])
    static member hr () = Element("hr", [], [])
    static member hr (attrs: Attr list) = Element("hr", attrs, [])
    static member img (attrs: Attr list) = Element("img", attrs, [])
    static member input (attrs: Attr list) = Element("input", attrs, [])
    static member meta (attrs: Attr list) = Element("meta", attrs, [])
    static member link (attrs: Attr list) = Element("link", attrs, [])

    // Escape hatch
    static member element (tag: string) (children: Node list) = Element(tag, [], children)
    static member elementWithAttrs (tag: string) (attrs: Attr list, children: Node list) = Element(tag, attrs, children)

module Component =
    let client (name: string) (props: 'T) : Node =
        let json = System.Text.Json.JsonSerializer.Serialize(props)
        Element("div", [ Data("fire-component", name); Data("fire-props", json) ], [])

module Meta =
    let description (text: string) : Node =
        Element("meta", [ Custom("name", "description"); Custom("content", text) ], [])

    let ogTitle (text: string) : Node =
        Element("meta", [ Custom("property", "og:title"); Custom("content", text) ], [])

    let ogDescription (text: string) : Node =
        Element("meta", [ Custom("property", "og:description"); Custom("content", text) ], [])

    let ogImage (url: string) : Node =
        Element("meta", [ Custom("property", "og:image"); Custom("content", url) ], [])

    let robots (content: string) : Node =
        Element("meta", [ Custom("name", "robots"); Custom("content", content) ], [])

    let canonical (url: string) : Node =
        Element("link", [ Custom("rel", "canonical"); Href url ], [])
