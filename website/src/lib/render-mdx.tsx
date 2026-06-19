import { compileMDX } from "next-mdx-remote/rsc";
import remarkGfm from "remark-gfm";
import rehypeSlug from "rehype-slug";
import rehypeAutolinkHeadings from "rehype-autolink-headings";
import rehypePrettyCode, { type Options as PrettyCodeOptions } from "rehype-pretty-code";

const prettyCodeOptions: PrettyCodeOptions = {
  theme: "github-dark-default",
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
