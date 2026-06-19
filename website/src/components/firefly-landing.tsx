"use client";

import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from "react";
import SiteHeader from "@/components/site-header";
import { useTheme } from "@/components/use-theme";

/* ----------------------------------------------------------------
   Firefly landing page
   Ported from the Claude design (Firefly.dc.html). The original was a
   .dc.html component (sc-if / sc-for / {{ }} bindings + a DCLogic class);
   this is a faithful React/Next port. Visual tokens live in globals.css
   under [data-fire-root].
   ---------------------------------------------------------------- */

type Theme = "dark" | "light";

interface FireflyLandingProps {
  defaultTheme?: Theme;
  githubStars?: number;
  showComparison?: boolean;
  showTestimonials?: boolean;
}

const INSTALL_CMD = "dotnet add package Firefly.Server";
const REPO_URL = "https://github.com/rodrigovidal/firefly";

/* ---- syntax-highlight token helpers (colors are CSS vars) ---- */
const tok = (name: string, bold = false): CSSProperties => ({
  color: `var(--${name})`,
  ...(bold ? { fontWeight: 600 } : {}),
});
const K = ({ children }: { children: ReactNode }) => <span style={tok("ck")}>{children}</span>;
const S = ({ children }: { children: ReactNode }) => <span style={tok("cs")}>{children}</span>;
const Fm = ({ children }: { children: ReactNode }) => <span style={tok("cf")}>{children}</span>;
const Ty = ({ children }: { children: ReactNode }) => <span style={tok("ct")}>{children}</span>;
const Op = ({ children }: { children: ReactNode }) => <span style={tok("co", true)}>{children}</span>;
const Nm = ({ children }: { children: ReactNode }) => <span style={tok("cn")}>{children}</span>;
const Pn = ({ children }: { children: ReactNode }) => <span style={tok("cp")}>{children}</span>;
const Line = ({ children }: { children?: ReactNode }) => (
  <div style={{ whiteSpace: "pre" }}>{children ?? " "}</div>
);

/* ---- static content (from the design's DCLogic) ---- */
const CHIPS = [
  "Typed route params",
  "Task-based handlers",
  "Built-in JSON",
  "HTTP + gRPC",
  "Flame validation",
  "JWT middleware",
  "DI via Service",
  "Console logging",
  "Hot reload",
];

// Real numbers from benchmarks/Firefly.Benchmarks (BenchmarkDotNet, Apple M4
// Pro, .NET 10). Mean response time (µs) and allocations (KB); lower is better.
const BENCH_ROWS = [
  { name: "Firefly", jsonUs: "45.5", jsonKb: "3.55", textUs: "44.0", textKb: "3.46", self: true },
  { name: "ASP.NET Core", jsonUs: "46.3", jsonKb: "3.58", textUs: "41.9", textKb: "3.14", self: false },
  { name: "Giraffe", jsonUs: "45.5", jsonKb: "3.82", textUs: "42.1", textKb: "3.47", self: false },
  { name: "Saturn", jsonUs: "46.3", jsonKb: "3.82", textUs: "41.3", textKb: "3.47", self: false },
  { name: "Falco", jsonUs: "45.5", jsonKb: "3.83", textUs: "42.0", textKb: "3.22", self: false },
  { name: "Oxpecker", jsonUs: "47.9", jsonKb: "3.70", textUs: "42.0", textKb: "3.25", self: false },
];

const ROWS = [
  { label: "Built directly on", fire: "Kestrel", giraffe: "ASP.NET", saturn: "ASP.NET", falco: "ASP.NET", oxpecker: "ASP.NET" },
  { label: "Hello world", fire: "8 lines", giraffe: "~15", saturn: "~12", falco: "~10", oxpecker: "~10" },
  { label: "API style", fire: "Pipeline", giraffe: "Handlers", saturn: "CE / DSL", falco: "Handlers", oxpecker: "Pipeline" },
  { label: "Typed params", fire: "/%i", giraffe: "routef", saturn: "scan", falco: "manual", oxpecker: "routef" },
  { label: "Startup", fire: "Instant", giraffe: "Fast", saturn: "Heavier", falco: "Fast", oxpecker: "Fast" },
];

const QUOTES = [
  {
    text: "“We swapped a Giraffe service over in an afternoon and shaved real latency off p99. The pipeline API just clicks if you already think in F#.”",
    initials: "RC",
    name: "Rodrigo Couto",
    role: "Staff Engineer @ Stone",
  },
  {
    text: "“Eight lines and I had a JSON API with JWT auth. No magic, no reflection — I can read the whole request path. That’s rare.”",
    initials: "AG",
    name: "Allan Garcez",
    role: "Senior Software Engineer @ Deel",
  },
  {
    text: "“Firefly is the first .NET framework that feels as light as the F# it’s written in. Startup is instant and the surface area fits in my head.”",
    initials: "RA",
    name: "Rodrigo Andrade",
    role: "Principal Engineer @ VTEX",
  },
];

const TAB_FILES = ["Routes.fs", "Todos.fs", "Program.fs"];

/* firefly glow particles in the hero */
const FIREFLIES: Array<{
  top: string;
  left: string;
  size: number;
  bg: string;
  shadow: string;
  drift: string;
  flick: string;
  delay?: string;
}> = [
  { top: "18%", left: "8%", size: 6, bg: "radial-gradient(circle, var(--glow-2), var(--glow))", shadow: "0 0 12px 3px var(--glow)", drift: "11s", flick: "3.4s" },
  { top: "62%", left: "14%", size: 5, bg: "radial-gradient(circle, var(--spark-2), var(--spark))", shadow: "0 0 10px 3px var(--spark)", drift: "13s", flick: "4.2s", delay: "-2s, -1s" },
  { top: "30%", left: "30%", size: 4, bg: "var(--glow-2)", shadow: "0 0 9px 2px var(--glow)", drift: "9s", flick: "2.8s", delay: "-4s, -2s" },
  { top: "80%", left: "40%", size: 6, bg: "radial-gradient(circle, var(--glow-2), var(--glow))", shadow: "0 0 12px 3px var(--glow)", drift: "12.5s", flick: "3.9s", delay: "-1s, -3s" },
  { top: "12%", left: "54%", size: 5, bg: "var(--glow-2)", shadow: "0 0 11px 3px var(--glow)", drift: "10.5s", flick: "3.1s", delay: "-5s, -1.5s" },
  { top: "46%", left: "62%", size: 4, bg: "radial-gradient(circle, var(--spark-2), var(--spark))", shadow: "0 0 9px 2px var(--spark)", drift: "14s", flick: "4.6s", delay: "-3s, -2.5s" },
  { top: "72%", left: "72%", size: 6, bg: "radial-gradient(circle, var(--glow-2), var(--glow))", shadow: "0 0 13px 3px var(--glow)", drift: "11.5s", flick: "3.6s", delay: "-6s, -0.5s" },
  { top: "24%", left: "84%", size: 5, bg: "var(--glow-2)", shadow: "0 0 11px 3px var(--glow)", drift: "12s", flick: "3.3s", delay: "-2.5s, -3.5s" },
  { top: "54%", left: "92%", size: 4, bg: "radial-gradient(circle, var(--spark-2), var(--spark))", shadow: "0 0 9px 2px var(--spark)", drift: "13.5s", flick: "4s", delay: "-4.5s, -2s" },
  { top: "88%", left: "22%", size: 4, bg: "var(--glow-2)", shadow: "0 0 9px 2px var(--glow)", drift: "10s", flick: "2.9s", delay: "-1.5s, -4s" },
];

function fmtStars(n: number): string {
  if (n >= 1000) return (n / 1000).toFixed(n % 1000 === 0 ? 0 : 1) + "k";
  return String(n);
}

/* shared bits ---------------------------------------------------- */
const headingFont = "var(--font-bricolage), sans-serif";
const monoFont = "var(--font-fira), monospace";

function WindowDots({ file }: { file: string }) {
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 8, padding: "13px 16px", borderBottom: "1px solid var(--border)" }}>
      <span style={{ width: 11, height: 11, borderRadius: "50%", background: "#ff6057" }} />
      <span style={{ width: 11, height: 11, borderRadius: "50%", background: "#ffbd2e" }} />
      <span style={{ width: 11, height: 11, borderRadius: "50%", background: "var(--spark)" }} />
      <span style={{ marginLeft: 8, fontFamily: monoFont, fontSize: 12.5, color: "var(--fg-3)" }}>{file}</span>
    </div>
  );
}

function CopyButton({ copied, onCopy }: { copied: boolean; onCopy: () => void }) {
  return (
    <button
      onClick={onCopy}
      aria-label="Copy install command"
      className="ff-copybtn"
      style={{ display: "flex", alignItems: "center", gap: 6, marginLeft: 4, background: "transparent", border: "none", color: "var(--fg-2)", cursor: "pointer", fontFamily: "inherit", fontSize: 12 }}
    >
      {copied ? (
        <span style={{ color: "var(--spark)", display: "flex", alignItems: "center", gap: 5 }}>
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round"><path d="M20 6 9 17l-5-5" /></svg>
          Copied
        </span>
      ) : (
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><rect x="9" y="9" width="11" height="11" rx="2" /><path d="M5 15V5a2 2 0 0 1 2-2h10" /></svg>
      )}
    </button>
  );
}

const codePane: CSSProperties = {
  margin: 0,
  padding: 22,
  fontFamily: monoFont,
  fontSize: 13.5,
  lineHeight: 1.8,
  color: "var(--ctxt)",
  overflowX: "auto",
};

export default function FireflyLanding({
  githubStars = 1280,
  showComparison = true,
  showTestimonials = true,
}: FireflyLandingProps) {
  const [theme, toggleTheme] = useTheme();
  const [copied, setCopied] = useState(false);
  const [tab, setTab] = useState(0);
  const copyTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => () => { if (copyTimer.current) clearTimeout(copyTimer.current); }, []);

  const copy = () => {
    try { navigator.clipboard?.writeText(INSTALL_CMD); } catch { /* ignore */ }
    setCopied(true);
    if (copyTimer.current) clearTimeout(copyTimer.current);
    copyTimer.current = setTimeout(() => setCopied(false), 1600);
  };

  const tabBase: CSSProperties = { padding: "9px 16px", borderRadius: 9, fontFamily: monoFont, fontSize: 13, cursor: "pointer", border: "1px solid transparent", background: "transparent", color: "var(--fg-2)", transition: "all .15s", fontWeight: 500 };
  const tabActive: CSSProperties = { ...tabBase, background: "var(--surface-2)", color: "var(--fg)", border: "1px solid var(--border-strong)", fontWeight: 600 };

  const footLink: CSSProperties = { fontSize: 14, color: "var(--fg-2)", textDecoration: "none" };

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
        overflowX: "hidden",
      }}
    >
      <SiteHeader theme={theme} onToggleTheme={toggleTheme} />

      {/* HERO */}
      <header style={{ position: "relative", maxWidth: 1180, margin: "0 auto", padding: "84px 28px 70px" }}>
        <div style={{ position: "absolute", inset: 0, pointerEvents: "none", overflow: "hidden" }}>
          {FIREFLIES.map((f, i) => (
            <span key={i} style={{ position: "absolute", top: f.top, left: f.left, width: f.size, height: f.size, borderRadius: "50%", background: f.bg, boxShadow: f.shadow, animation: `ffDrift ${f.drift} ease-in-out infinite, ffFlicker ${f.flick} ease-in-out infinite`, animationDelay: f.delay }} />
          ))}
        </div>

        <div className="ff-hero-grid" style={{ position: "relative", display: "grid", gridTemplateColumns: "1.05fr 0.95fr", gap: 56, alignItems: "center" }}>
          <div style={{ minWidth: 0 }}>
            <div style={{ display: "inline-flex", alignItems: "center", gap: 9, padding: "6px 13px 6px 11px", borderRadius: 999, border: "1px solid var(--border)", background: "var(--surface)", fontSize: 13, color: "var(--fg-2)", fontWeight: 600 }}>
              <span style={{ width: 7, height: 7, borderRadius: "50%", background: "var(--glow)", boxShadow: "0 0 8px 2px var(--glow)", animation: "ffPulse 2.4s ease-in-out infinite" }} />
              Minimal F# web framework
            </div>
            <h1 style={{ fontFamily: headingFont, fontWeight: 700, fontSize: "clamp(40px, 5.4vw, 66px)", lineHeight: 1.02, letterSpacing: "-0.035em", margin: "22px 0 0", textWrap: "balance" }}>
              Web apps in F#,<br /><span style={{ color: "var(--glow)", textShadow: "0 0 38px var(--glow-soft)" }}>light</span> as a firefly.
            </h1>
            <p style={{ fontSize: 18, lineHeight: 1.6, color: "var(--fg-2)", margin: "22px 0 0", maxWidth: "30em", textWrap: "pretty" }}>
              Firefly is a tiny web framework built straight on Kestrel — responses go right to the{" "}
              <span style={{ fontFamily: monoFont, fontSize: "0.92em", color: "var(--fg)" }}>PipeWriter</span>. Idiomatic, composable, fast. A full app in about eight lines.
            </p>

            <div style={{ display: "flex", alignItems: "center", gap: 12, marginTop: 30, flexWrap: "wrap" }}>
              <a href="/docs/getting-started" className="ff-btn-primary" style={{ display: "inline-flex", alignItems: "center", gap: 8, background: "var(--glow)", color: "#1a1305", textDecoration: "none", fontWeight: 700, fontSize: 15, padding: "13px 22px", borderRadius: 11, boxShadow: "0 0 0 1px rgba(255,255,255,0.14) inset, 0 12px 30px -10px var(--glow)" }}>
                Get started
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><path d="M5 12h14M13 6l6 6-6 6" /></svg>
              </a>
              <a href={REPO_URL} target="_blank" rel="noreferrer" className="ff-btn-surface" style={{ display: "inline-flex", alignItems: "center", gap: 9, background: "var(--surface)", color: "var(--fg)", textDecoration: "none", fontWeight: 600, fontSize: 15, padding: "13px 18px", borderRadius: 11, border: "1px solid var(--border-strong)" }}>
                <svg width="17" height="17" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C6.48 2 2 6.58 2 12.25c0 4.53 2.87 8.37 6.84 9.73.5.1.68-.22.68-.49l-.01-1.7c-2.78.62-3.37-1.22-3.37-1.22-.46-1.18-1.11-1.49-1.11-1.49-.91-.64.07-.62.07-.62 1 .07 1.53 1.06 1.53 1.06.89 1.57 2.34 1.12 2.91.85.09-.66.35-1.12.63-1.37-2.22-.26-4.56-1.14-4.56-5.06 0-1.12.39-2.03 1.03-2.75-.1-.26-.45-1.3.1-2.71 0 0 .84-.28 2.75 1.05a9.4 9.4 0 0 1 5 0c1.91-1.33 2.75-1.05 2.75-1.05.55 1.41.2 2.45.1 2.71.64.72 1.03 1.63 1.03 2.75 0 3.93-2.35 4.79-4.58 5.05.36.32.68.94.68 1.9l-.01 2.82c0 .27.18.6.69.49A10.02 10.02 0 0 0 22 12.25C22 6.58 17.52 2 12 2Z" /></svg>
                Star on GitHub
                <span style={{ fontFamily: monoFont, fontSize: 13, color: "var(--fg-2)" }}>{fmtStars(githubStars)}</span>
              </a>
            </div>

            <div style={{ display: "flex", alignItems: "center", gap: 10, marginTop: 24 }}>
              <div style={{ display: "flex", alignItems: "center", gap: 12, fontFamily: monoFont, fontSize: 14, background: "var(--code-bg)", border: "1px solid var(--border)", borderRadius: 10, padding: "11px 14px" }}>
                <span style={{ color: "var(--spark)" }}>$</span>
                <span style={{ color: "var(--ctxt)" }}>{INSTALL_CMD}</span>
                <CopyButton copied={copied} onCopy={copy} />
              </div>
              <span style={{ fontSize: 12.5, color: "var(--fg-3)" }}>the <span style={{ fontFamily: monoFont }}>Firefly.Server</span> package</span>
            </div>
          </div>

          {/* hero code window */}
          <div style={{ animation: "ffFloaty 7s ease-in-out infinite" }}>
            <div style={{ borderRadius: 16, overflow: "hidden", border: "1px solid var(--border-strong)", background: "var(--code-bg)", boxShadow: "var(--card-shadow)", position: "relative" }}>
              <div style={{ position: "absolute", inset: 0, borderRadius: 16, pointerEvents: "none", boxShadow: "0 0 60px -10px var(--glow-soft) inset" }} />
              <WindowDots file="Program.fs" />
              <div style={{ ...codePane, padding: "22px 22px 24px", fontSize: 14, lineHeight: 1.75 }}>
                <Line><K>open</K> <Ty>Firefly</Ty></Line>
                <Line><Fm>Route</Fm>.start</Line>
                <Line><Op>{"|>"}</Op> <Fm>Route</Fm>.get <S>{'"/"'}</S> (<K>fun</K> _ <Op>{"->"}</Op> <K>task</K> {"{"}</Line>
                <Line>{"       "}<K>return</K> <Fm>Response</Fm>.text <S>{'"Hello, firefly!"'}</S> {"})"}</Line>
                <Line><Op>{"|>"}</Op> <Fm>Route</Fm>.get <S>{'"/todos/'}<Nm>%i</Nm>{'"'}</S> getTodo</Line>
                <Line><Op>{"|>"}</Op> <Fm>App</Fm>.run</Line>
              </div>
            </div>
          </div>
        </div>
      </header>

      {/* FEATURES */}
      <section style={{ maxWidth: 1180, margin: "0 auto", padding: "40px 28px 20px" }}>
        <div className="ff-features-grid" style={{ display: "grid", gridTemplateColumns: "repeat(3, 1fr)", gap: 20 }}>
          <FeatureCard
            iconBg="var(--glow-soft)" iconColor="var(--glow)"
            icon={<path d="M13 2 4 14h7l-1 8 9-12h-7l1-8Z" />}
            title="Built on raw Kestrel"
          >
            Text responses write straight to the <span style={{ fontFamily: monoFont, fontSize: "0.9em", color: "var(--fg)" }}>PipeWriter</span> — no middleware tax, no reflection. The shortest path from request to bytes.
          </FeatureCard>
          <FeatureCard
            iconBg="rgba(127,178,255,0.12)" iconColor="var(--cf)"
            icon={<path d="M8 4H6a2 2 0 0 0-2 2v3a2 2 0 0 1-2 2 2 2 0 0 1 2 2v3a2 2 0 0 0 2 2h2M16 4h2a2 2 0 0 1 2 2v3a2 2 0 0 0 2 2 2 2 0 0 0-2 2v3a2 2 0 0 1-2 2h-2" />}
            title="Reads like F#"
          >
            Compose routes with <span style={{ fontFamily: monoFont, fontSize: "0.9em", color: "var(--co)" }}>{"|>"}</span>. Pattern-match params, group and nest, drop in middleware inline. Idiomatic, declarative, no ceremony.
          </FeatureCard>
          <FeatureCard
            iconBg="rgba(198,242,78,0.13)" iconColor="var(--spark)"
            icon={<><path d="M20.24 12.24a6 6 0 0 0-8.49-8.49L5 10.5V19h8.5l6.74-6.76Z" /><path d="M16 8 2 22" /></>}
            title="Minimal by design"
          >
            One package, a tiny surface area, instant startup. Learn it in an afternoon, keep it for years. Only what you need — nothing you don&apos;t.
          </FeatureCard>
        </div>
        <div style={{ display: "flex", flexWrap: "wrap", gap: 10, marginTop: 20 }}>
          {CHIPS.map((c) => (
            <span key={c} style={{ display: "inline-flex", alignItems: "center", gap: 8, padding: "8px 14px", borderRadius: 999, border: "1px solid var(--border)", background: "var(--surface)", fontSize: 13.5, color: "var(--fg-2)", fontWeight: 500 }}>
              <span style={{ width: 5, height: 5, borderRadius: "50%", background: "var(--glow)" }} />{c}
            </span>
          ))}
        </div>
      </section>

      {/* CODE SHOWCASE */}
      <section style={{ maxWidth: 1180, margin: "0 auto", padding: "64px 28px" }}>
        <div className="ff-showcase-grid" style={{ display: "grid", gridTemplateColumns: "0.9fr 1.1fr", gap: 48, alignItems: "center" }}>
          <div>
            <div style={{ fontFamily: monoFont, fontSize: 13, color: "var(--glow)", fontWeight: 600, marginBottom: 14 }}>// the whole app</div>
            <h2 style={{ fontFamily: headingFont, fontWeight: 700, fontSize: "clamp(30px,3.6vw,44px)", lineHeight: 1.06, letterSpacing: "-0.03em", margin: "0 0 16px", textWrap: "balance" }}>Eight lines to a running service.</h2>
            <p style={{ fontSize: 16.5, lineHeight: 1.6, color: "var(--fg-2)", margin: "0 0 26px", maxWidth: "30em", textWrap: "pretty" }}>Typed route params, task-based handlers, JSON in and out, JWT middleware, and DI — all from the same composable pipeline. Here&apos;s a real todo API.</p>
            <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
              {["Routing", "JSON handlers", "Wire it up"].map((label, i) => (
                <button key={label} onClick={() => setTab(i)} style={tab === i ? tabActive : tabBase}>{label}</button>
              ))}
            </div>
          </div>

          <div style={{ borderRadius: 16, overflow: "hidden", border: "1px solid var(--border-strong)", background: "var(--code-bg)", boxShadow: "var(--card-shadow)" }}>
            <WindowDots file={TAB_FILES[tab]} />
            <div style={{ position: "relative" }}>
              {tab === 0 && (
                <div style={codePane}>
                  <Line><K>let</K> routes <Pn>=</Pn></Line>
                  <Line>{"    "}<Fm>Route</Fm>.start</Line>
                  <Line>{"    "}<Op>{"|>"}</Op> <Fm>Route</Fm>.get  <S>{'"/todos/'}<Nm>%i</Nm>{'"'}</S> getTodo</Line>
                  <Line>{"    "}<Op>{"|>"}</Op> <Fm>Route</Fm>.post <S>{'"/todos"'}</S>    createTodo</Line>
                  <Line>{"    "}<Op>{"|>"}</Op> <Fm>Route</Fm>.group <S>{'"/api"'}</S> (<K>fun</K> api <Op>{"->"}</Op></Line>
                  <Line>{"        "}api</Line>
                  <Line>{"        "}<Op>{"|>"}</Op> <Fm>Route</Fm>.middleware (<Fm>Jwt</Fm>.defaults key <Op>{"|>"}</Op> <Fm>Jwt</Fm>.validate)</Line>
                  <Line>{"        "}<Op>{"|>"}</Op> <Fm>Route</Fm>.get <S>{'"/health"'}</S> (<K>fun</K> _ <Op>{"->"}</Op> <K>task</K> {"{"} <K>return</K> <Fm>Response</Fm>.text <S>{'"ok"'}</S> {"}))"}</Line>
                </div>
              )}
              {tab === 1 && (
                <div style={codePane}>
                  <Line><K>open</K> <Ty>Firefly</Ty></Line>
                  <Line />
                  <Line><K>let</K> getTodo (store<Pn>:</Pn> <Ty>ITodoStore</Ty>) (id<Pn>:</Pn> <Ty>int</Ty>) <Pn>=</Pn> <K>task</K> {"{"}</Line>
                  <Line>{"    "}<K>let!</K> todo <Pn>=</Pn> store.GetById id</Line>
                  <Line>{"    "}<K>return</K> <Fm>Response</Fm>.json todo</Line>
                  <Line>{"}"}</Line>
                  <Line />
                  <Line><K>let</K> createTodo (store<Pn>:</Pn> <Ty>ITodoStore</Ty>) <Pn>=</Pn> <K>task</K> {"{"}</Line>
                  <Line>{"    "}<K>let!</K> dto  <Pn>=</Pn> <Fm>Request</Fm>.json<Pn>{"<"}</Pn><Ty>TodoDto</Ty><Pn>{">"}</Pn></Line>
                  <Line>{"    "}<K>let!</K> todo <Pn>=</Pn> store.Add dto</Line>
                  <Line>{"    "}<K>return</K> <Fm>Response</Fm>.json todo <Op>{"|>"}</Op> <Fm>Response</Fm>.status <Nm>201</Nm></Line>
                  <Line>{"}"}</Line>
                </div>
              )}
              {tab === 2 && (
                <div style={codePane}>
                  <Line><Fm>App</Fm>.defaults</Line>
                  <Line><Op>{"|>"}</Op> <Fm>App</Fm>.services [ <Fm>Service</Fm>.singleton<Pn>{"<"}</Pn><Ty>ITodoStore</Ty>, <Ty>InMemoryTodoStore</Ty><Pn>{">"}</Pn> ]</Line>
                  <Line><Op>{"|>"}</Op> <Fm>App</Fm>.middleware <Fm>Log</Fm>.toConsole</Line>
                  <Line><Op>{"|>"}</Op> <Fm>App</Fm>.run routes</Line>
                </div>
              )}
            </div>
          </div>
        </div>
      </section>

      {/* GRPC */}
      <section style={{ maxWidth: 1180, margin: "0 auto", padding: "24px 28px 56px" }}>
        <div className="ff-showcase-grid" style={{ display: "grid", gridTemplateColumns: "1.1fr 0.9fr", gap: 48, alignItems: "center" }}>
          {/* code window */}
          <div style={{ borderRadius: 16, overflow: "hidden", border: "1px solid var(--border-strong)", background: "var(--code-bg)", boxShadow: "var(--card-shadow)" }}>
            <WindowDots file="Greeter.fs" />
            <div style={codePane}>
              <Line><K>open</K> <Ty>Firefly</Ty></Line>
              <Line><K>open</K> <Ty>Grpc.Core</Ty></Line>
              <Line />
              <Line><span style={tok("cc")}>{"// a service generated from greet.proto"}</span></Line>
              <Line><K>let</K> greeter <Pn>=</Pn> <Fm>grpcService</Fm> <S>{'"greet.Greeter"'}</S> {"{"}</Line>
              <Line>{"    "}<Fm>unary</Fm> <S>{'"SayHello"'}</S> (<K>fun</K> (req<Pn>:</Pn> <Ty>HelloRequest</Ty>) _ctx <Op>{"->"}</Op> <K>task</K> {"{"}</Line>
              <Line>{"        "}<K>return</K> <Ty>HelloReply</Ty>(Message <Pn>=</Pn> <S>{'$"Hello, {req.Name}!"'}</S>)</Line>
              <Line>{"    }})"}</Line>
              <Line>{"    "}<Fm>serverStream</Fm> <S>{'"SayHelloStream"'}</S> (<K>fun</K> req writer _ctx <Op>{"->"}</Op> <K>task</K> {"{"}</Line>
              <Line>{"        "}<K>for</K> i <K>in</K> <Nm>1</Nm>..<Nm>5</Nm> <K>do</K> <K>do!</K> writer.WriteAsync(reply i)</Line>
              <Line>{"    }})"}</Line>
              <Line>{"}"}</Line>
              <Line />
              <Line><Fm>App</Fm>.defaults</Line>
              <Line><Op>{"|>"}</Op> <Fm>App</Fm>.grpc greeter      <span style={tok("cc")}>{"// gRPC"}</span></Line>
              <Line><Op>{"|>"}</Op> <Fm>App</Fm>.run routes        <span style={tok("cc")}>{"// + the HTTP routes"}</span></Line>
            </div>
          </div>

          {/* copy */}
          <div>
            <div style={{ fontFamily: monoFont, fontSize: 13, color: "var(--glow)", fontWeight: 600, marginBottom: 14 }}>// http + grpc</div>
            <h2 style={{ fontFamily: headingFont, fontWeight: 700, fontSize: "clamp(30px,3.6vw,44px)", lineHeight: 1.06, letterSpacing: "-0.03em", margin: "0 0 16px", textWrap: "balance" }}>Not just HTTP — gRPC too.</h2>
            <p style={{ fontSize: 16.5, lineHeight: 1.6, color: "var(--fg-2)", margin: "0 0 24px", maxWidth: "30em", textWrap: "pretty" }}>
              Define a service with the <span style={{ fontFamily: monoFont, fontSize: "0.9em", color: "var(--cf)" }}>grpcService</span> builder — unary and server-streaming methods, typed from your <span style={{ fontFamily: monoFont, fontSize: "0.9em", color: "var(--fg)" }}>.proto</span>. Register it on the same pipeline as your routes: <strong style={{ color: "var(--fg)", fontWeight: 600 }}>one Kestrel server speaks both protocols</strong>.
            </p>
            <ul style={{ listStyle: "none", margin: "0 0 26px", padding: 0, display: "flex", flexDirection: "column", gap: 12 }}>
              {[
                "Unary and server-streaming methods",
                "Shares DI, config, and the same port as HTTP",
                "Strongly-typed messages generated from .proto",
              ].map((item) => (
                <li key={item} style={{ display: "flex", alignItems: "flex-start", gap: 11, fontSize: 15, color: "var(--fg-2)", lineHeight: 1.5 }}>
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="var(--spark)" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" style={{ flex: "none", marginTop: 2 }}><path d="M20 6 9 17l-5-5" /></svg>
                  {item}
                </li>
              ))}
            </ul>
            <a href="/guides/grpc-greeter" className="ff-btn-surface" style={{ display: "inline-flex", alignItems: "center", gap: 8, background: "var(--surface)", color: "var(--fg)", textDecoration: "none", fontWeight: 600, fontSize: 14.5, padding: "11px 18px", borderRadius: 11, border: "1px solid var(--border-strong)" }}>
              Read the gRPC guide
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><path d="M5 12h14M13 6l6 6-6 6" /></svg>
            </a>
          </div>
        </div>
      </section>

      {/* BENCHMARKS */}
      <section id="benchmarks" style={{ maxWidth: 1180, margin: "0 auto", padding: "40px 28px" }}>
        <div style={{ background: "var(--surface)", border: "1px solid var(--border)", borderRadius: 22, padding: 44, boxShadow: "var(--card-shadow)" }}>
          <div style={{ display: "flex", alignItems: "flex-end", justifyContent: "space-between", gap: 24, flexWrap: "wrap", marginBottom: 34 }}>
            <div>
              <div style={{ fontFamily: monoFont, fontSize: 13, color: "var(--glow)", fontWeight: 600, marginBottom: 12 }}>// benchmarks</div>
              <h2 style={{ fontFamily: headingFont, fontWeight: 700, fontSize: "clamp(28px,3.4vw,40px)", lineHeight: 1.06, letterSpacing: "-0.03em", margin: 0, textWrap: "balance" }}>Fast where it counts.</h2>
            </div>
            <p style={{ fontSize: 14, color: "var(--fg-3)", margin: 0, maxWidth: "28em", lineHeight: 1.5 }}>Response time and allocations measured in-process with BenchmarkDotNet — Apple M4 Pro, .NET 10. Lower is better.</p>
          </div>
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 560, fontFamily: monoFont }}>
              <thead>
                <tr>
                  <th style={{ textAlign: "left", padding: "0 12px 12px", fontSize: 12, fontWeight: 600, color: "var(--fg-3)", fontFamily: "var(--font-hanken), sans-serif" }} />
                  <th colSpan={2} style={{ padding: "0 12px 6px", fontSize: 12, fontWeight: 700, letterSpacing: "0.04em", textTransform: "uppercase", color: "var(--fg-3)", textAlign: "center" }}>JSON</th>
                  <th colSpan={2} style={{ padding: "0 12px 6px", fontSize: 12, fontWeight: 700, letterSpacing: "0.04em", textTransform: "uppercase", color: "var(--fg-3)", textAlign: "center" }}>Plaintext</th>
                </tr>
                <tr>
                  <th style={{ borderBottom: "1px solid var(--border)" }} />
                  {["µs", "KB", "µs", "KB"].map((h, i) => (
                    <th key={i} style={{ padding: "0 12px 10px", fontSize: 12, fontWeight: 500, color: "var(--fg-3)", textAlign: "right", borderBottom: "1px solid var(--border)" }}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {BENCH_ROWS.map((r) => (
                  <tr key={r.name} style={{ background: r.self ? "var(--glow-soft)" : "transparent" }}>
                    <td style={{ padding: "11px 12px", fontSize: 14, borderBottom: "1px solid var(--border)", color: r.self ? "var(--fg)" : "var(--fg-2)", fontWeight: r.self ? 700 : 500, fontFamily: "var(--font-hanken), sans-serif", whiteSpace: "nowrap" }}>
                      {r.self && <span style={{ display: "inline-block", width: 7, height: 7, borderRadius: "50%", background: "var(--glow)", boxShadow: "0 0 8px 2px var(--glow)", marginRight: 8 }} />}
                      {r.name}
                    </td>
                    {[r.jsonUs, r.jsonKb, r.textUs, r.textKb].map((v, i) => (
                      <td key={i} style={{ padding: "11px 12px", fontSize: 13.5, textAlign: "right", borderBottom: "1px solid var(--border)", color: r.self ? "var(--fg)" : "var(--fg-2)", fontWeight: r.self ? 700 : 400 }}>{v}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p style={{ fontSize: 12.5, color: "var(--fg-3)", margin: "18px 0 0", lineHeight: 1.55 }}>
            On par with hand-written ASP.NET Core and the established F# frameworks — with the lowest JSON allocations of the F# options. Reproduce it:{" "}
            <span style={{ fontFamily: monoFont, color: "var(--fg-2)" }}>dotnet run -c Release --project benchmarks/Firefly.Benchmarks</span>.
          </p>
        </div>
      </section>

      {/* COMPARISON */}
      {showComparison && (
        <section style={{ maxWidth: 1180, margin: "0 auto", padding: "48px 28px" }}>
          <div style={{ textAlign: "center", marginBottom: 34 }}>
            <div style={{ fontFamily: monoFont, fontSize: 13, color: "var(--glow)", fontWeight: 600, marginBottom: 12 }}>// the landscape</div>
            <h2 style={{ fontFamily: headingFont, fontWeight: 700, fontSize: "clamp(28px,3.4vw,40px)", lineHeight: 1.06, letterSpacing: "-0.03em", margin: 0, textWrap: "balance" }}>How Firefly compares.</h2>
          </div>
          <div style={{ overflowX: "auto", border: "1px solid var(--border)", borderRadius: 18, background: "var(--surface)", boxShadow: "var(--card-shadow)" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 720 }}>
              <thead>
                <tr>
                  <th style={{ textAlign: "left", padding: "18px 22px", fontSize: 13, fontWeight: 600, color: "var(--fg-3)", borderBottom: "1px solid var(--border)" }} />
                  <th style={{ padding: "18px 16px", borderBottom: "1px solid var(--border)", background: "var(--glow-soft)" }}>
                    <span style={{ display: "inline-flex", alignItems: "center", gap: 8, fontFamily: headingFont, fontWeight: 700, fontSize: 16, color: "var(--fg)" }}>
                      <span style={{ width: 8, height: 8, borderRadius: "50%", background: "var(--glow)", boxShadow: "0 0 8px 2px var(--glow)" }} />Firefly
                    </span>
                  </th>
                  {["Giraffe", "Saturn", "Falco", "Oxpecker"].map((h) => (
                    <th key={h} style={{ padding: "18px 16px", fontSize: 15, fontWeight: 600, color: "var(--fg-2)", borderBottom: "1px solid var(--border)" }}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {ROWS.map((r) => (
                  <tr key={r.label}>
                    <td style={{ padding: "16px 22px", fontSize: 14, fontWeight: 600, color: "var(--fg)", borderBottom: "1px solid var(--border)" }}>{r.label}</td>
                    <td style={{ padding: 16, fontSize: 13.5, textAlign: "center", color: "var(--fg)", fontWeight: 600, borderBottom: "1px solid var(--border)", background: "var(--glow-soft)", fontFamily: monoFont }}>{r.fire}</td>
                    <td style={{ padding: 16, fontSize: 13.5, textAlign: "center", color: "var(--fg-2)", borderBottom: "1px solid var(--border)", fontFamily: monoFont }}>{r.giraffe}</td>
                    <td style={{ padding: 16, fontSize: 13.5, textAlign: "center", color: "var(--fg-2)", borderBottom: "1px solid var(--border)", fontFamily: monoFont }}>{r.saturn}</td>
                    <td style={{ padding: 16, fontSize: 13.5, textAlign: "center", color: "var(--fg-2)", borderBottom: "1px solid var(--border)", fontFamily: monoFont }}>{r.falco}</td>
                    <td style={{ padding: 16, fontSize: 13.5, textAlign: "center", color: "var(--fg-2)", borderBottom: "1px solid var(--border)", fontFamily: monoFont }}>{r.oxpecker}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p style={{ textAlign: "center", fontSize: 12.5, color: "var(--fg-3)", margin: "16px 0 0" }}>A rough orientation, not a scorecard — every framework here is excellent. Numbers are illustrative.</p>
        </section>
      )}

      {/* TESTIMONIALS */}
      {showTestimonials && (
        <section style={{ maxWidth: 1180, margin: "0 auto", padding: "48px 28px" }}>
          <div style={{ textAlign: "center", marginBottom: 34 }}>
            <div style={{ fontFamily: monoFont, fontSize: 13, color: "var(--glow)", fontWeight: 600, marginBottom: 12 }}>// community</div>
            <h2 style={{ fontFamily: headingFont, fontWeight: 700, fontSize: "clamp(28px,3.4vw,40px)", lineHeight: 1.06, letterSpacing: "-0.03em", margin: 0, textWrap: "balance" }}>Loved by F# developers.</h2>
          </div>
          <div className="ff-quotes-grid" style={{ display: "grid", gridTemplateColumns: "repeat(3,1fr)", gap: 20 }}>
            {QUOTES.map((q) => (
              <figure key={q.initials} style={{ margin: 0, background: "var(--surface)", border: "1px solid var(--border)", borderRadius: 16, padding: 26, boxShadow: "var(--card-shadow)", display: "flex", flexDirection: "column", gap: 18 }}>
                <blockquote style={{ margin: 0, fontSize: 15.5, lineHeight: 1.6, color: "var(--fg)", textWrap: "pretty" }}>{q.text}</blockquote>
                <figcaption style={{ display: "flex", alignItems: "center", gap: 12, marginTop: "auto" }}>
                  <span style={{ width: 38, height: 38, borderRadius: "50%", flex: "none", display: "flex", alignItems: "center", justifyContent: "center", fontWeight: 700, fontSize: 14, color: "#1a1305", background: "linear-gradient(135deg, var(--glow-2), var(--glow))" }}>{q.initials}</span>
                  <span style={{ display: "flex", flexDirection: "column" }}>
                    <span style={{ fontSize: 14, fontWeight: 600, color: "var(--fg)" }}>{q.name}</span>
                    <span style={{ fontSize: 12.5, color: "var(--fg-3)" }}>{q.role}</span>
                  </span>
                </figcaption>
              </figure>
            ))}
          </div>
        </section>
      )}

      {/* CTA BAND */}
      <section style={{ maxWidth: 1180, margin: "0 auto", padding: "44px 28px 80px" }}>
        <div style={{ position: "relative", overflow: "hidden", borderRadius: 24, border: "1px solid var(--border-strong)", background: "var(--surface)", padding: "56px 40px", textAlign: "center", boxShadow: "var(--card-shadow)" }}>
          <div style={{ position: "absolute", inset: 0, pointerEvents: "none", background: "radial-gradient(600px 300px at 50% -20%, var(--glow-soft), transparent 70%)" }} />
          <div style={{ position: "relative" }}>
            <div style={{ display: "inline-flex", marginBottom: 22 }}>
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src="/firefly-logo.png" alt="Firefly" style={{ height: 62, width: "auto", display: "block", filter: "drop-shadow(0 0 22px var(--glow))" }} />
            </div>
            <h2 style={{ fontFamily: headingFont, fontWeight: 700, fontSize: "clamp(30px,4vw,48px)", lineHeight: 1.04, letterSpacing: "-0.03em", margin: "0 0 14px", textWrap: "balance" }}>Ship your next F# app on Firefly.</h2>
            <p style={{ fontSize: 17, color: "var(--fg-2)", margin: "0 auto 30px", maxWidth: "30em", lineHeight: 1.55 }}>One package. Eight lines. Kestrel speed. Start now and have something running before your coffee&apos;s cold.</p>
            <div style={{ display: "inline-flex", alignItems: "center", gap: 12, fontFamily: monoFont, fontSize: 15, background: "var(--code-bg)", border: "1px solid var(--border-strong)", borderRadius: 12, padding: "13px 18px", marginBottom: 26 }}>
              <span style={{ color: "var(--spark)" }}>$</span>
              <span style={{ color: "var(--ctxt)" }}>{INSTALL_CMD}</span>
              <CopyButton copied={copied} onCopy={copy} />
            </div>
            <div style={{ display: "flex", alignItems: "center", justifyContent: "center", gap: 12, flexWrap: "wrap" }}>
              <a href="/docs" className="ff-btn-primary" style={{ display: "inline-flex", alignItems: "center", gap: 8, background: "var(--glow)", color: "#1a1305", textDecoration: "none", fontWeight: 700, fontSize: 15, padding: "13px 24px", borderRadius: 11, boxShadow: "0 0 0 1px rgba(255,255,255,0.14) inset, 0 12px 30px -10px var(--glow)" }}>Read the docs</a>
              <a href={REPO_URL} target="_blank" rel="noreferrer" className="ff-btn-surface" style={{ display: "inline-flex", alignItems: "center", gap: 9, background: "transparent", color: "var(--fg)", textDecoration: "none", fontWeight: 600, fontSize: 15, padding: "13px 20px", borderRadius: 11, border: "1px solid var(--border-strong)" }}>View on GitHub</a>
            </div>
          </div>
        </div>
      </section>

      {/* FOOTER */}
      <footer style={{ borderTop: "1px solid var(--border)" }}>
        <div className="ff-footer-grid" style={{ maxWidth: 1180, margin: "0 auto", padding: "44px 28px", display: "grid", gridTemplateColumns: "1.6fr 1fr 1fr 1fr", gap: 32 }}>
          <div>
            <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 14 }}>
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src="/firefly-logo.png" alt="Firefly" style={{ height: 26, width: "auto", display: "block" }} />
              <span style={{ fontFamily: headingFont, fontWeight: 700, fontSize: 17 }}>Firefly</span>
            </div>
            <p style={{ fontSize: 13.5, color: "var(--fg-3)", margin: 0, maxWidth: "24em", lineHeight: 1.6 }}>A minimal F# web framework built straight on Kestrel. Open source, MIT licensed.</p>
          </div>
          <FooterCol
            title="Docs"
            links={[
              { label: "Getting started", href: "/docs/getting-started" },
              { label: "Routing", href: "/docs/routing" },
              { label: "Middleware", href: "/docs/middleware" },
              { label: "Validation (Flame)", href: "/docs/flame" },
            ]}
            linkStyle={footLink}
          />
          <FooterCol
            title="Community"
            links={[
              { label: "GitHub", href: REPO_URL },
              { label: "Guides", href: "/guides" },
              { label: "Discussions", href: `${REPO_URL}/discussions` },
              { label: "Changelog", href: `${REPO_URL}/releases` },
            ]}
            linkStyle={footLink}
          />
          <FooterCol
            title="Project"
            links={[
              { label: "Benchmarks", href: "#benchmarks" },
              { label: "Ecosystem", href: "/docs/ecosystem" },
              { label: "Contributing", href: REPO_URL },
              { label: "License", href: `${REPO_URL}/blob/main/LICENSE` },
            ]}
            linkStyle={footLink}
          />
        </div>
        <div style={{ borderTop: "1px solid var(--border)" }}>
          <div style={{ maxWidth: 1180, margin: "0 auto", padding: "18px 28px", display: "flex", alignItems: "center", justifyContent: "space-between", gap: 16, flexWrap: "wrap" }}>
            <span style={{ fontSize: 12.5, color: "var(--fg-3)" }}>© 2026 Firefly. Built with F# and Kestrel.</span>
            <span style={{ fontFamily: monoFont, fontSize: 12.5, color: "var(--fg-3)" }}>{INSTALL_CMD}</span>
          </div>
        </div>
      </footer>
    </div>
  );
}

function FeatureCard({ icon, iconBg, iconColor, title, children }: { icon: ReactNode; iconBg: string; iconColor: string; title: string; children: ReactNode }) {
  return (
    <div style={{ background: "var(--surface)", border: "1px solid var(--border)", borderRadius: 16, padding: 26, boxShadow: "var(--card-shadow)" }}>
      <div style={{ width: 42, height: 42, borderRadius: 11, display: "flex", alignItems: "center", justifyContent: "center", background: iconBg, color: iconColor, marginBottom: 18 }}>
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">{icon}</svg>
      </div>
      <h3 style={{ fontFamily: headingFont, fontWeight: 700, fontSize: 19, letterSpacing: "-0.02em", margin: "0 0 8px" }}>{title}</h3>
      <p style={{ fontSize: 14.5, lineHeight: 1.6, color: "var(--fg-2)", margin: 0 }}>{children}</p>
    </div>
  );
}

function FooterCol({ title, links, linkStyle }: { title: string; links: { label: string; href: string }[]; linkStyle: CSSProperties }) {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 11 }}>
      <span style={{ fontSize: 12, fontWeight: 700, letterSpacing: "0.04em", textTransform: "uppercase", color: "var(--fg-3)", marginBottom: 3 }}>{title}</span>
      {links.map((l) => {
        const external = l.href.startsWith("http");
        return (
          <a
            key={l.label}
            href={l.href}
            className="ff-footlink"
            style={linkStyle}
            {...(external ? { target: "_blank", rel: "noreferrer" } : {})}
          >
            {l.label}
          </a>
        );
      })}
    </div>
  );
}
