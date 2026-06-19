import type { Metadata } from "next";
import { notFound } from "next/navigation";
import DocsShell from "@/components/docs/docs-shell";
import { getNav, getPage, getPageSlugs } from "@/lib/content";
import { renderMarkdown } from "@/lib/render-mdx";

export function generateStaticParams() {
  return getPageSlugs("docs").map((slug) => ({ slug }));
}

export const dynamicParams = false;

export async function generateMetadata({ params }: { params: Promise<{ slug: string }> }): Promise<Metadata> {
  const { slug } = await params;
  const page = getPage("docs", slug);
  if (!page) return {};
  return {
    title: `${page.meta.title} — Firefly Docs`,
    description: page.meta.description,
  };
}

export default async function DocPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const page = getPage("docs", slug);
  if (!page) notFound();
  const content = await renderMarkdown(page.body);
  return (
    <DocsShell section="docs" nav={getNav("docs")} toc={page.toc}>
      {content}
    </DocsShell>
  );
}
