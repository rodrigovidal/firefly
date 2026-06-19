# Firefly website

Marketing site, docs, and guides for the [Firefly](https://github.com/rodrigovidal/fire) F# web framework. Built with Next.js (App Router), Tailwind, and a custom MDX docs pipeline. Deployed on Vercel at [fireflyframework.dev](https://www.fireflyframework.dev).

## Develop

```bash
pnpm install
pnpm dev          # http://localhost:3000
pnpm build        # production build
```

## Structure

```
content/
  docs/*.md       # reference docs (sidebar grouped by frontmatter `group`/`order`)
  guides/*.md     # example-driven walkthroughs
src/
  app/            # routes: /, /docs, /docs/[slug], /guides, /guides/[slug]
  components/     # firefly-landing, docs shell (sidebar + TOC)
  lib/            # content.ts (frontmatter/nav/TOC), render-mdx.tsx (MDX pipeline)
```

Docs and guides are plain markdown with frontmatter (`title`, `description`, `group`, `order`). They're compiled through `next-mdx-remote` (markdown mode) with `remark-gfm`, heading slugs/anchors, and `rehype-pretty-code` (shiki) for F# syntax highlighting.

## Deploy

Pushes to `main` deploy automatically via the Vercel Git integration (the project's **Root Directory** is set to `website`). To deploy manually:

```bash
vercel --prod --cwd website
```
