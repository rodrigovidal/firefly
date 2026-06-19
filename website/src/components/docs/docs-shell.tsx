"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState, type CSSProperties, type ReactNode } from "react";
import type { NavGroup, TocItem } from "@/lib/content";
import SiteHeader from "@/components/site-header";
import { useTheme } from "@/components/use-theme";

const monoFont = "var(--font-fira), monospace";

interface DocsShellProps {
  section: "docs" | "guides";
  nav: NavGroup[];
  toc: TocItem[];
  children: ReactNode;
  /** Render children directly (no .ff-prose wrapper) — used for the guides index card grid. */
  bare?: boolean;
}

export default function DocsShell({ section, nav, toc, children, bare = false }: DocsShellProps) {
  const pathname = usePathname();
  const [theme, toggleTheme] = useTheme();
  const [menuOpen, setMenuOpen] = useState(false);
  const [activeId, setActiveId] = useState<string | null>(null);

  // close the mobile sidebar on navigation
  useEffect(() => {
    setMenuOpen(false);
  }, [pathname]);

  // scroll-spy for the on-page TOC
  useEffect(() => {
    if (toc.length === 0) return;
    const headings = toc
      .map((t) => document.getElementById(t.id))
      .filter((el): el is HTMLElement => el !== null);
    if (headings.length === 0) return;
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries.filter((e) => e.isIntersecting);
        if (visible.length > 0) {
          setActiveId(visible.sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0].target.id);
        }
      },
      { rootMargin: "0px 0px -70% 0px", threshold: 0.1 },
    );
    headings.forEach((h) => observer.observe(h));
    return () => observer.disconnect();
  }, [toc, pathname]);

  // flatten nav for prev/next
  const flat = nav.flatMap((g) => g.items);
  const currentSlug = pathname.replace(`/${section}`, "").replace(/^\//, "") || "index";
  const idx = flat.findIndex((i) => i.slug === currentSlug);
  const prev = idx > 0 ? flat[idx - 1] : null;
  const next = idx >= 0 && idx < flat.length - 1 ? flat[idx + 1] : null;

  const linkBase: CSSProperties = {
    display: "block",
    padding: "5px 10px",
    borderRadius: 7,
    fontSize: 13.5,
    textDecoration: "none",
    lineHeight: 1.4,
  };

  const sidebar = (
    <nav aria-label={`${section} navigation`} style={{ display: "flex", flexDirection: "column", gap: 22 }}>
      {nav.map((g) => (
        <div key={g.group} style={{ display: "flex", flexDirection: "column", gap: 3 }}>
          <div style={{ fontSize: 11, fontWeight: 700, letterSpacing: "0.06em", textTransform: "uppercase", color: "var(--fg-3)", padding: "0 10px", marginBottom: 5 }}>
            {g.group}
          </div>
          {g.items.map((item) => {
            const href = `/${section}/${item.slug}`;
            const active = pathname === href;
            return (
              <Link
                key={item.slug}
                href={href}
                className="ff-doclink"
                aria-current={active ? "page" : undefined}
                style={{
                  ...linkBase,
                  color: active ? "var(--glow)" : "var(--fg-2)",
                  background: active ? "var(--glow-soft)" : "transparent",
                  fontWeight: active ? 600 : 500,
                }}
              >
                {item.title}
              </Link>
            );
          })}
        </div>
      ))}
    </nav>
  );

  return (
    <div
      data-fire-root
      data-theme={theme}
      style={{
        minHeight: "100vh",
        background: "var(--bg)",
        backgroundImage: "var(--bg-grad)",
        color: "var(--fg)",
        fontFamily: "var(--font-hanken), system-ui, sans-serif",
        WebkitFontSmoothing: "antialiased",
      }}
    >
      <SiteHeader theme={theme} onToggleTheme={toggleTheme} onMenuToggle={() => setMenuOpen((v) => !v)} maxWidth={1320} />

      <div className="ff-docs-grid" style={{ maxWidth: 1320, margin: "0 auto", display: "grid", gridTemplateColumns: "232px minmax(0, 1fr) 220px", gap: 40, padding: "0 24px", alignItems: "start" }}>
        {/* left sidebar */}
        <aside className={`ff-docs-sidebar${menuOpen ? " ff-open" : ""}`} style={{ position: "sticky", top: 57, alignSelf: "start", maxHeight: "calc(100vh - 57px)", overflowY: "auto", padding: "28px 0 40px" }}>
          {sidebar}
        </aside>

        {/* content */}
        <main style={{ minWidth: 0, padding: "34px 0 64px" }}>
          {bare ? children : <article className="ff-prose">{children}</article>}

          {(prev || next) && (
            <nav style={{ display: "flex", justifyContent: "space-between", gap: 16, marginTop: 48, paddingTop: 24, borderTop: "1px solid var(--border)" }}>
              {prev ? (
                <Link href={`/${section}/${prev.slug}`} className="ff-btn-surface" style={{ display: "flex", flexDirection: "column", gap: 2, padding: "12px 16px", borderRadius: 11, border: "1px solid var(--border)", textDecoration: "none", maxWidth: "48%" }}>
                  <span style={{ fontSize: 12, color: "var(--fg-3)" }}>← Previous</span>
                  <span style={{ fontSize: 14.5, fontWeight: 600, color: "var(--fg)" }}>{prev.title}</span>
                </Link>
              ) : <span />}
              {next ? (
                <Link href={`/${section}/${next.slug}`} className="ff-btn-surface" style={{ display: "flex", flexDirection: "column", gap: 2, padding: "12px 16px", borderRadius: 11, border: "1px solid var(--border)", textDecoration: "none", textAlign: "right", maxWidth: "48%", marginLeft: "auto" }}>
                  <span style={{ fontSize: 12, color: "var(--fg-3)" }}>Next →</span>
                  <span style={{ fontSize: 14.5, fontWeight: 600, color: "var(--fg)" }}>{next.title}</span>
                </Link>
              ) : <span />}
            </nav>
          )}
        </main>

        {/* right TOC */}
        <aside className="ff-docs-toc" style={{ position: "sticky", top: 57, alignSelf: "start", maxHeight: "calc(100vh - 57px)", overflowY: "auto", padding: "34px 0 40px" }}>
          {toc.length > 0 && (
            <>
              <div style={{ fontSize: 11, fontWeight: 700, letterSpacing: "0.06em", textTransform: "uppercase", color: "var(--fg-3)", marginBottom: 12 }}>On this page</div>
              <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "flex", flexDirection: "column", gap: 7, fontFamily: monoFont }}>
                {toc.map((t) => (
                  <li key={t.id} style={{ paddingLeft: t.depth === 3 ? 14 : 0 }}>
                    <a
                      href={`#${t.id}`}
                      style={{
                        fontSize: 12.5,
                        textDecoration: "none",
                        lineHeight: 1.45,
                        color: activeId === t.id ? "var(--glow)" : "var(--fg-3)",
                        fontWeight: activeId === t.id ? 600 : 400,
                      }}
                    >
                      {t.text}
                    </a>
                  </li>
                ))}
              </ul>
            </>
          )}
        </aside>
      </div>
    </div>
  );
}
