import { compileMDX } from "next-mdx-remote/rsc";
import remarkGfm from "remark-gfm";
import rehypeSlug from "rehype-slug";
import rehypeAutolinkHeadings from "rehype-autolink-headings";
import rehypePrettyCode, { type Options as PrettyCodeOptions } from "rehype-pretty-code";

// Shiki theme that mirrors the landing page's code window palette
// (the --ck/--cs/--cf/--ct/--co/--cn/--cp/--ctxt tokens in globals.css).
const fireflyShikiTheme = {
  name: "firefly",
  type: "dark" as const,
  colors: { "editor.background": "#0b0d14", "editor.foreground": "#d8d4c8" },
  tokenColors: [
    { settings: { background: "#0b0d14", foreground: "#d8d4c8" } },
    { scope: ["comment", "punctuation.definition.comment"], settings: { foreground: "#6b7280" } },
    { scope: ["keyword", "keyword.control", "storage", "storage.type", "storage.modifier", "keyword.other", "variable.language"], settings: { foreground: "#ff9d5c" } },
    { scope: ["string", "string.quoted", "string.template", "punctuation.definition.string"], settings: { foreground: "#c6ef72" } },
    { scope: ["constant.numeric", "constant.language", "constant.language.boolean"], settings: { foreground: "#e89bff" } },
    { scope: ["keyword.operator"], settings: { foreground: "#ffd36b" } },
    { scope: ["entity.name.type", "support.type", "entity.name.class", "support.class", "entity.other.inherited-class"], settings: { foreground: "#5fd6c4" } },
    { scope: ["entity.name.function", "support.function", "entity.name.namespace", "variable.other.member", "meta.namespace"], settings: { foreground: "#7fb2ff" } },
    { scope: ["punctuation", "meta.brace", "punctuation.separator", "punctuation.section"], settings: { foreground: "#c9c5ba" } },
    { scope: ["variable", "variable.other", "entity.name"], settings: { foreground: "#d8d4c8" } },
  ],
};

const prettyCodeOptions: PrettyCodeOptions = {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  theme: fireflyShikiTheme as any,
  keepBackground: false,
  defaultLang: "text",
};

/**
 * Render a markdown body through the MDX pipeline (markdown mode, so the docs'
 * angle brackets/braces are literal) with GFM, heading slugs/anchors, and
 * shiki syntax highlighting. Returns a React node to drop into `.ff-prose`.
 */
export async function renderMarkdown(body: string) {
  const { content } = await compileMDX({
    source: body,
    options: {
      parseFrontmatter: false,
      mdxOptions: {
        format: "md",
        remarkPlugins: [remarkGfm],
        rehypePlugins: [
          rehypeSlug,
          [rehypePrettyCode, prettyCodeOptions],
          [rehypeAutolinkHeadings, { behavior: "wrap" }],
        ],
      },
    },
  });
  return content;
}
