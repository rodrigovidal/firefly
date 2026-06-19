import type { Metadata } from "next";
import Link from "next/link";
import DocsShell from "@/components/docs/docs-shell";
import { getAllMeta, getNav } from "@/lib/content";

export const metadata: Metadata = {
  title: "Guides — Firefly",
  description: "Hands-on walkthroughs built from real Firefly example apps.",
};

export default function GuidesIndexPage() {
  const guides = getAllMeta("guides").sort((a, b) => a.order - b.order);
  return (
    <DocsShell section="guides" nav={getNav("guides")} toc={[]} bare>
      <div style={{ maxWidth: "48rem" }}>
        <h1 style={{ fontFamily: "'Bricolage Grotesque', sans-serif", fontWeight: 700, fontSize: "2.1rem", letterSpacing: "-0.03em", color: "var(--fg)", margin: "0 0 10px" }}>
          Guides
        </h1>
        <p style={{ fontSize: 16.5, lineHeight: 1.6, color: "var(--fg-2)", margin: "0 0 30px" }}>
          Hands-on walkthroughs built from real example apps in the repo. Each one ships as runnable code under{" "}
          <span style={{ fontFamily: "'Fira Code', monospace", fontSize: "0.9em", color: "var(--glow-2)" }}>examples/</span>.
        </p>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(240px, 1fr))", gap: 16 }}>
          {guides.map((g) => (
            <Link
              key={g.slug}
              href={`/guides/${g.slug}`}
              className="ff-btn-surface"
              style={{ display: "flex", flexDirection: "column", gap: 8, padding: 20, borderRadius: 14, border: "1px solid var(--border)", background: "var(--surface)", textDecoration: "none", boxShadow: "var(--card-shadow)" }}
            >
              <span style={{ fontFamily: "'Bricolage Grotesque', sans-serif", fontWeight: 700, fontSize: 17, color: "var(--fg)", letterSpacing: "-0.02em" }}>
                {g.title}
              </span>
              <span style={{ fontSize: 13.5, lineHeight: 1.55, color: "var(--fg-2)" }}>{g.description}</span>
            </Link>
          ))}
        </div>
      </div>
    </DocsShell>
  );
}
