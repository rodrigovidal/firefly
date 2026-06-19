import type { Metadata } from "next";
import { notFound } from "next/navigation";
import DocsShell from "@/components/docs/docs-shell";
import { getNav, getPage } from "@/lib/content";
import { renderMarkdown } from "@/lib/render-mdx";

export const metadata: Metadata = {
  title: "Documentation — Firefly",
  description: "Guides and reference for the Firefly F# web framework.",
};

export default async function DocsIndexPage() {
  const page = getPage("docs", "index");
  if (!page) notFound();
  const content = await renderMarkdown(page.body);
  return (
    <DocsShell section="docs" nav={getNav("docs")} toc={page.toc}>
      {content}
    </DocsShell>
  );
}
