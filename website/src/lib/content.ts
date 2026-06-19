import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import matter from "gray-matter";
import GithubSlugger from "github-slugger";

export type Section = "docs" | "guides";

export interface PageMeta {
  slug: string;
  title: string;
  description: string;
  group: string;
  order: number;
}

export interface NavGroup {
  group: string;
  items: PageMeta[];
}

export interface TocItem {
  depth: number;
  text: string;
  id: string;
}

const CONTENT_ROOT = join(process.cwd(), "content");

// Sidebar group ordering for docs. Guides use a single implicit group.
const GROUP_ORDER: Record<string, number> = {
  Introduction: 0,
  Core: 1,
  Features: 2,
  Production: 3,
  Reference: 4,
  Guides: 0,
};

function sectionDir(section: Section): string {
  return join(CONTENT_ROOT, section);
}

export function getAllMeta(section: Section): PageMeta[] {
  const dir = sectionDir(section);
  let files: string[];
  try {
    files = readdirSync(dir);
  } catch {
    return [];
  }
  return files
    .filter((f) => f.endsWith(".md") || f.endsWith(".mdx"))
    .map((f) => {
      const slug = f.replace(/\.mdx?$/, "");
      const { data } = matter(readFileSync(join(dir, f), "utf8"));
      return {
        slug,
        title: String(data.title ?? slug),
        description: String(data.description ?? ""),
        group: String(data.group ?? "Guides"),
        order: Number(data.order ?? 99),
      } satisfies PageMeta;
    });
}

/** Slugs that get their own page under /docs/[slug] or /guides/[slug] (everything except the index). */
export function getPageSlugs(section: Section): string[] {
  return getAllMeta(section)
    .map((m) => m.slug)
    .filter((s) => s !== "index");
}

export function getNav(section: Section): NavGroup[] {
  const metas = getAllMeta(section).filter((m) => m.slug !== "index");
  const groups = new Map<string, PageMeta[]>();
  for (const m of metas) {
    if (!groups.has(m.group)) groups.set(m.group, []);
    groups.get(m.group)!.push(m);
  }
  return [...groups.entries()]
    .map(([group, items]) => ({
      group,
      items: items.sort((a, b) => a.order - b.order || a.title.localeCompare(b.title)),
    }))
    .sort((a, b) => (GROUP_ORDER[a.group] ?? 99) - (GROUP_ORDER[b.group] ?? 99) || a.group.localeCompare(b.group));
}

export interface LoadedPage {
  meta: PageMeta;
  body: string;
  toc: TocItem[];
}

export function getPage(section: Section, slug: string): LoadedPage | null {
  const dir = sectionDir(section);
  for (const ext of [".md", ".mdx"]) {
    try {
      const raw = readFileSync(join(dir, slug + ext), "utf8");
      const { data, content } = matter(raw);
      const meta: PageMeta = {
        slug,
        title: String(data.title ?? slug),
        description: String(data.description ?? ""),
        group: String(data.group ?? "Guides"),
        order: Number(data.order ?? 99),
      };
      return { meta, body: content, toc: buildToc(content) };
    } catch {
      /* try next extension */
    }
  }
  return null;
}

/** Strip inline markdown so heading text + slug match what rehype-slug produces. */
function headingText(raw: string): string {
  return raw
    .replace(/`([^`]+)`/g, "$1")
    .replace(/\[([^\]]+)\]\([^)]*\)/g, "$1")
    .replace(/[*_]/g, "")
    .trim();
}

/**
 * Build the on-page table of contents. The slugger must visit every heading in
 * document order (so dedupe counters match rehype-slug), but we only surface h2/h3.
 */
export function buildToc(body: string): TocItem[] {
  const slugger = new GithubSlugger();
  const toc: TocItem[] = [];
  let inFence = false;
  for (const line of body.split("\n")) {
    if (/^\s*(```|~~~)/.test(line)) {
      inFence = !inFence;
      continue;
    }
    if (inFence) continue;
    const m = /^(#{1,6})\s+(.+?)\s*#*$/.exec(line);
    if (!m) continue;
    const depth = m[1].length;
    const text = headingText(m[2]);
    const id = slugger.slug(text);
    if (depth >= 2 && depth <= 3) toc.push({ depth, text, id });
  }
  return toc;
}
