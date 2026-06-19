"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { CSSProperties } from "react";
import type { Theme } from "@/components/use-theme";

const REPO_URL = "https://github.com/rodrigovidal/firefly";
const headingFont = "var(--font-bricolage), sans-serif";

interface SiteHeaderProps {
  theme: Theme;
  onToggleTheme: () => void;
  /** When provided, shows the mobile sidebar toggle (docs/guides only). */
  onMenuToggle?: () => void;
  /** Aligns the bar with the page content width. */
  maxWidth?: number;
}

/**
 * The single top navigation bar used on every page (landing, docs, guides) so
 * the header never changes as you move around the site.
 */
export default function SiteHeader({ theme, onToggleTheme, onMenuToggle, maxWidth = 1180 }: SiteHeaderProps) {
  const pathname = usePathname();
  const navLink = (active: boolean): CSSProperties => ({
    color: active ? "var(--fg)" : "var(--fg-2)",
    textDecoration: "none",
    fontSize: 14,
    fontWeight: 500,
    padding: "7px 12px",
    borderRadius: 8,
  });

  return (
    <nav style={{ position: "sticky", top: 0, zIndex: 50, backdropFilter: "blur(12px)", background: "color-mix(in srgb, var(--bg) 80%, transparent)", borderBottom: "1px solid var(--border)" }}>
      <div style={{ maxWidth, margin: "0 auto", padding: "14px 28px", display: "flex", alignItems: "center", gap: 28 }}>
        <Link href="/" style={{ display: "flex", alignItems: "center", gap: 10, flex: "none", textDecoration: "none", color: "var(--fg)" }}>
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src="/firefly-logo.png" alt="Firefly" style={{ height: 30, width: "auto", display: "block", filter: "drop-shadow(0 0 12px var(--glow-soft))" }} />
          <span style={{ fontFamily: headingFont, fontWeight: 700, fontSize: 19, letterSpacing: "-0.02em" }}>Firefly</span>
        </Link>
        <div style={{ display: "flex", alignItems: "center", gap: 6, marginLeft: 8 }}>
          <Link href="/docs" className="ff-navlink" style={navLink(pathname.startsWith("/docs"))}>Docs</Link>
          <Link href="/guides" className="ff-navlink" style={navLink(pathname.startsWith("/guides"))}>Guides</Link>
          <a href="/#benchmarks" className="ff-navlink" style={navLink(false)}>Benchmarks</a>
          <a href={REPO_URL} target="_blank" rel="noreferrer" className="ff-navlink" style={navLink(false)}>GitHub</a>
        </div>
        <div style={{ flex: 1 }} />
        <button onClick={onToggleTheme} aria-label="Toggle theme" className="ff-iconbtn" style={{ display: "flex", alignItems: "center", justifyContent: "center", width: 36, height: 36, borderRadius: 9, border: "1px solid var(--border)", background: "var(--surface)", color: "var(--fg-2)", cursor: "pointer" }}>
          <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></svg>
        </button>
        <Link href="/docs/getting-started" className="ff-btn-primary" style={{ display: "flex", alignItems: "center", gap: 8, flex: "none", background: "var(--glow)", color: "#1a1305", textDecoration: "none", fontWeight: 700, fontSize: 14, padding: "9px 16px", borderRadius: 9, boxShadow: "0 0 0 1px rgba(255,255,255,0.12) inset, 0 8px 24px -8px var(--glow)" }}>Get started</Link>
        {onMenuToggle && (
          <button onClick={onMenuToggle} aria-label="Toggle navigation" className="ff-doc-menu-btn ff-iconbtn" style={{ display: "none", alignItems: "center", justifyContent: "center", width: 36, height: 36, borderRadius: 9, border: "1px solid var(--border)", background: "var(--surface)", color: "var(--fg-2)", cursor: "pointer" }}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M3 6h18M3 12h18M3 18h18" /></svg>
          </button>
        )}
      </div>
    </nav>
  );
}
