# Frontend (Vite React SPA) Implementation Plan

> Part of the EMaigrator v1 plan set — see 00-INDEX.md. Binds to CONTRACTS.md.

**Goal:** Build the operator-facing SPA per `UX-Guide.md` + `FRONTEND-DESIGN.md` — a Vite + React 19 + TypeScript client of the ASP.NET Core REST API + SignalR hub (CONTRACTS §6). It delivers: design tokens (teal/slate light+dark, Geist fonts) + shadcn/ui, a typed API client, a reconnecting SignalR client, the dashboard (cards⇄list, live progress, usage widget, welcome state), the gated 6-step wizard (From&To, Connect 2a/2b, Scope, Review&plan, Run, Results) with draft autosave, global states (skeleton/empty/error/reconnecting), the plain+technical error pattern, theme toggle, WCAG AA, a Playwright happy-path E2E, and a user-gated security verification (no secrets persisted, XSS-safe rendering, cookie auth, CSP). Developed against a mock server; live integration with Plan 08 is a documented follow-up.

**Architecture:** A pure client-rendered SPA (no SSR). React Router shell (dashboard ↔ wizard). All DTOs mirror CONTRACTS §6 verbatim in `web/src/api/types.ts`; SignalR event names match the hub method names exactly (`Progress`, `StatusChanged`, `NeedsDecision`). Auth uses the API's **httpOnly auth cookie** (`fetch` with `credentials: "include"`) — the JWT access token is never stored in `localStorage`. Live progress arrives over SignalR with a reconnecting state. Density flexes by persona (low-density default; opt-in compact). Theming is a CSS-variable token swap. Tests are TDD with Vitest + Testing Library; one Playwright happy-path E2E drives the wizard against a mock API.

**Tech Stack:** Vite 6 + React 19 + TypeScript 5.7; Tailwind CSS v4 (CSS-variable tokens); shadcn/ui (Radix primitives, copied-in); `lucide-react` icons; `recharts` for throughput; `react-router-dom` v7; `@microsoft/signalr`; Vitest + `@testing-library/react` + `@testing-library/user-event` + `jsdom`; `msw` (Mock Service Worker) for the mock API; Playwright for the E2E. Builds on the `/web` stub from Plan 01 (Tasks 4–6).

---

### Task 0: Tailwind theme tokens (teal/slate light+dark) + Geist fonts + shadcn/ui setup

**Goal:** Port the `design_handoff_emaigrator/styles.css` token system into the Tailwind v4 SPA as CSS custom properties themed by `[data-theme]` and `[data-density]`, self-host the Geist Sans / Geist Mono fonts, and install the shadcn/ui primitives the later tasks consume (`button`, `card`, `badge`, `input`, `select`, `progress`, `tabs`, `alert`, `accordion`, `collapsible`, `dropdown-menu`, `skeleton`, `sonner`).

**Files:**
- Modify: `web/src/index.css`
- Create: `web/src/styles/tokens.css`
- Create: `web/src/lib/theme.ts`
- Create: `web/src/components/ui/button.tsx`, `web/src/components/ui/card.tsx`, `web/src/components/ui/badge.tsx`, `web/src/components/ui/input.tsx`, `web/src/components/ui/select.tsx`, `web/src/components/ui/progress.tsx`, `web/src/components/ui/tabs.tsx`, `web/src/components/ui/alert.tsx`, `web/src/components/ui/accordion.tsx`, `web/src/components/ui/collapsible.tsx`, `web/src/components/ui/dropdown-menu.tsx`, `web/src/components/ui/skeleton.tsx`, `web/src/components/ui/sonner.tsx` (added via `npx shadcn add`)
- Modify: `web/package.json` (adds `@radix-ui/*`, `sonner`, `tw-animate-css`, `recharts`, `react-router-dom`, `@microsoft/signalr`, `msw` as needed)
- Test: `web/src/styles/tokens.test.ts`, `web/src/lib/theme.test.ts`

**Acceptance Criteria:**
- [ ] `web/src/styles/tokens.css` defines all token CSS variables from `styles.css` for the **light default** (`--accent: #0d9488`, `--bg: #ffffff`, `--fg: #0f172a`, `--success: #16a34a`, `--throttled: #d97706`, `--warning: #ca8a04`, `--error: #dc2626`, …) and the **dark** override under `[data-theme="dark"]` (`--accent: #2dd4bf`, `--bg: #0b1120`, …) plus the `[data-density]` variables (`--card-pad`, `--control-h`, `--hit`, `--body-scale`).
- [ ] `index.css` imports Tailwind, the tokens, and maps tokens to Tailwind `@theme` color utilities (`bg-bg`, `text-fg`, `text-fg-muted`, `bg-accent`, `text-accent`, `border-border`) so components reference semantic names, not raw hex.
- [ ] Geist Sans + Geist Mono are self-hosted (`@font-face`, not a Google Fonts `@import`) and bound to `--font-sans` / `--font-mono`.
- [ ] `applyTheme("dark")` sets `document.documentElement.dataset.theme = "dark"` and persists `localStorage["em-theme"] = "dark"`; `applyTheme("system")` resolves via `matchMedia("(prefers-color-scheme: dark)")`.
- [ ] `resolveTheme("system")` returns `"light"` or `"dark"` based on the media query; `loadTheme()` returns the persisted value or `"system"` default.
- [ ] shadcn `Button` renders and `npm --prefix web run build` succeeds.

**Verify:** `npm --prefix web run test -- --run src/styles/tokens.test.ts src/lib/theme.test.ts` → `Tests` all passed; and `npm --prefix web run build` → `dist/index.html` written, exit 0.

**Steps:**

1. - [ ] Write the failing tests. Create `web/src/styles/tokens.test.ts`:
```ts
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

const tokens = readFileSync(resolve(__dirname, "tokens.css"), "utf8");

describe("design tokens", () => {
  it("defines light accent + surfaces", () => {
    expect(tokens).toContain("--accent: #0d9488");
    expect(tokens).toContain("--bg: #ffffff");
    expect(tokens).toContain("--fg: #0f172a");
    expect(tokens).toContain("--fg-muted: #64748b");
  });
  it("defines distinct semantic status colors (success != accent)", () => {
    expect(tokens).toContain("--success: #16a34a");
    expect(tokens).toContain("--throttled: #d97706");
    expect(tokens).toContain("--warning: #ca8a04");
    expect(tokens).toContain("--error: #dc2626");
  });
  it("overrides accent + bg under dark theme", () => {
    expect(tokens).toMatch(/\[data-theme="dark"\][\s\S]*--accent: #2dd4bf/);
    expect(tokens).toMatch(/\[data-theme="dark"\][\s\S]*--bg: #0b1120/);
  });
  it("defines density-driven sizing vars and AA hit target", () => {
    expect(tokens).toMatch(/\[data-density="comfortable"\][\s\S]*--hit: 44px/);
    expect(tokens).toContain("--control-h");
    expect(tokens).toContain("--body-scale");
  });
});
```
Create `web/src/lib/theme.test.ts`:
```ts
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { applyTheme, loadTheme, resolveTheme } from "./theme";

describe("theme", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute("data-theme");
    vi.stubGlobal("matchMedia", (q: string) => ({
      matches: q.includes("dark"),
      media: q,
      addEventListener: () => {},
      removeEventListener: () => {},
    }));
  });
  afterEach(() => vi.unstubAllGlobals());

  it("defaults to system when nothing persisted", () => {
    expect(loadTheme()).toBe("system");
  });
  it("applies and persists an explicit theme", () => {
    applyTheme("dark");
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(localStorage.getItem("em-theme")).toBe("dark");
    expect(loadTheme()).toBe("dark");
  });
  it("resolves system to the media-query result", () => {
    expect(resolveTheme("system")).toBe("dark");
    expect(resolveTheme("light")).toBe("light");
  });
});
```

2. - [ ] Run them, expect FAIL: `npm --prefix web run test -- --run src/styles/tokens.test.ts src/lib/theme.test.ts` → fails — `tokens.css` and `theme.ts` do not exist (`Cannot find module './theme'`, `ENOENT tokens.css`).

3. - [ ] Minimal implementation. First add the runtime deps the plan needs (run from project root): `npm --prefix web install react-router-dom@^7.1.0 @microsoft/signalr@^8.0.7 recharts@^2.15.0 sonner@^1.7.1 @radix-ui/react-slot@^1.1.1 @radix-ui/react-select@^2.1.4 @radix-ui/react-progress@^1.1.1 @radix-ui/react-tabs@^1.1.2 @radix-ui/react-accordion@^1.2.2 @radix-ui/react-collapsible@^1.1.2 @radix-ui/react-dropdown-menu@^2.1.4 class-variance-authority@^0.7.1` and `npm --prefix web install -D msw@^2.7.0 @playwright/test@^1.49.1 @testing-library/user-event@^14.5.2 tw-animate-css@^1.0.0`.
   Create `web/src/styles/tokens.css` (ports `design_handoff_emaigrator/styles.css` token blocks; full values):
```css
/* EMaigrator design tokens — ported from design_handoff_emaigrator/styles.css */
:root {
  --font-sans: "Geist", "Inter", system-ui, -apple-system, sans-serif;
  --font-mono: "Geist Mono", "JetBrains Mono", ui-monospace, SFMono-Regular, Menlo, monospace;

  --fs-display: 1.875rem; --lh-display: 2.25rem;
  --fs-h1: 1.5rem; --lh-h1: 2rem;
  --fs-h2: 1.25rem; --lh-h2: 1.75rem;
  --fs-body: 0.9375rem; --lh-body: 1.5rem;
  --fs-sm: 0.8125rem; --lh-sm: 1.25rem;
  --fs-xs: 0.75rem; --lh-xs: 1rem;
  --fs-mono: 0.875rem;

  --radius: 6px; --radius-sm: 4px; --radius-lg: 8px; --radius-full: 999px;
  --s1: 4px; --s2: 8px; --s3: 12px; --s4: 16px; --s6: 24px; --s8: 32px; --s12: 48px;

  --card-pad: 20px; --row-pad-y: 11px; --row-pad-x: 16px;
  --section-gap: 24px; --grid-gap: 16px;
  --control-h: 38px; --hit: 40px; --body-scale: 1;
}

:root, [data-theme="light"] {
  --accent: #0d9488; --accent-hover: #0f766e; --accent-fg: #ffffff;
  --accent-subtle: #f0fdfa; --accent-line: #99f6e4;
  --bg: #ffffff; --surface: #f8fafc; --surface-2: #f1f5f9; --surface-raised: #ffffff;
  --border: #e2e8f0; --border-strong: #cbd5e1;
  --fg: #0f172a; --fg-muted: #64748b; --fg-subtle: #94a3b8;
  --success: #16a34a; --success-bg: #f0fdf4; --success-line: #bbf7d0;
  --throttled: #d97706; --throttled-bg: #fffbeb; --throttled-line: #fde68a;
  --warning: #ca8a04; --warning-bg: #fefce8; --warning-line: #fef08a;
  --error: #dc2626; --error-bg: #fef2f2; --error-line: #fecaca;
  --idle: #64748b; --idle-bg: #f1f5f9; --idle-line: #e2e8f0;
  --shadow-sm: 0 1px 2px rgba(15,23,42,0.06), 0 1px 1px rgba(15,23,42,0.04);
  --shadow-md: 0 8px 24px -6px rgba(15,23,42,0.14), 0 2px 6px rgba(15,23,42,0.06);
  --chart-grid: #eef2f7;
}

[data-theme="dark"] {
  --accent: #2dd4bf; --accent-hover: #5eead4; --accent-fg: #042f2e;
  --accent-subtle: #134e4a; --accent-line: #115e59;
  --bg: #0b1120; --surface: #111827; --surface-2: #161f31; --surface-raised: #131c2e;
  --border: #1e293b; --border-strong: #334155;
  --fg: #e2e8f0; --fg-muted: #94a3b8; --fg-subtle: #64748b;
  --success: #4ade80; --success-bg: #0d2417; --success-line: #14532d;
  --throttled: #fbbf24; --throttled-bg: #281c08; --throttled-line: #78350f;
  --warning: #facc15; --warning-bg: #2a2408; --warning-line: #713f12;
  --error: #f87171; --error-bg: #2a1212; --error-line: #7f1d1d;
  --idle: #94a3b8; --idle-bg: #1a2335; --idle-line: #334155;
  --shadow-sm: 0 1px 2px rgba(0,0,0,0.4);
  --shadow-md: 0 12px 32px -8px rgba(0,0,0,0.6), 0 2px 8px rgba(0,0,0,0.4);
  --chart-grid: #1b2536;
}

[data-density="comfortable"] {
  --card-pad: 22px; --row-pad-y: 13px; --row-pad-x: 18px;
  --section-gap: 28px; --grid-gap: 18px; --control-h: 42px; --hit: 44px; --body-scale: 1.06;
}
[data-density="compact"] {
  --card-pad: 14px; --row-pad-y: 6px; --row-pad-x: 12px;
  --section-gap: 16px; --grid-gap: 12px; --control-h: 32px; --hit: 32px; --body-scale: 0.96;
}

@keyframes em-pulse { 0%,100% { opacity: 1; } 50% { opacity: 0.45; } }
@keyframes em-skeleton { 0%,100% { opacity: 0.5; } 50% { opacity: 0.85; } }
.theme-anim-off * { transition: none !important; }
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { animation-duration: 0.001ms !important; transition-duration: 0.001ms !important; }
}
```
Replace `web/src/index.css` with the Tailwind v4 entry that maps tokens to utilities and self-hosts fonts:
```css
@import "tailwindcss";
@import "tw-animate-css";
@import "./styles/tokens.css";

@font-face { font-family: "Geist"; font-weight: 300 700; font-display: swap; src: local("Geist"); }
@font-face { font-family: "Geist Mono"; font-weight: 400 600; font-display: swap; src: local("Geist Mono"); }

@theme inline {
  --color-bg: var(--bg);
  --color-surface: var(--surface);
  --color-surface-2: var(--surface-2);
  --color-surface-raised: var(--surface-raised);
  --color-border: var(--border);
  --color-border-strong: var(--border-strong);
  --color-fg: var(--fg);
  --color-fg-muted: var(--fg-muted);
  --color-fg-subtle: var(--fg-subtle);
  --color-accent: var(--accent);
  --color-accent-fg: var(--accent-fg);
  --color-accent-subtle: var(--accent-subtle);
  --color-success: var(--success);
  --color-throttled: var(--throttled);
  --color-warning: var(--warning);
  --color-error: var(--error);
  --color-idle: var(--idle);
  --font-sans: var(--font-sans);
  --font-mono: var(--font-mono);
  --radius: var(--radius);
}

html, body { background: var(--bg); color: var(--fg); font-family: var(--font-sans); }
body { font-size: calc(16px * var(--body-scale)); }
.mono { font-family: var(--font-mono); font-variant-numeric: tabular-nums; font-feature-settings: "tnum" 1; }
:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; border-radius: var(--radius-sm); }
```
Create `web/src/lib/theme.ts`:
```ts
export type Theme = "light" | "dark" | "system";
const KEY = "em-theme";

export function resolveTheme(theme: Theme): "light" | "dark" {
  if (theme === "system") {
    return typeof matchMedia !== "undefined" &&
      matchMedia("(prefers-color-scheme: dark)").matches
      ? "dark"
      : "light";
  }
  return theme;
}

export function applyTheme(theme: Theme): void {
  const resolved = resolveTheme(theme);
  const root = document.documentElement;
  root.classList.add("theme-anim-off");
  root.dataset.theme = resolved;
  localStorage.setItem(KEY, theme);
  requestAnimationFrame(() => root.classList.remove("theme-anim-off"));
}

export function loadTheme(): Theme {
  const v = localStorage.getItem(KEY);
  return v === "light" || v === "dark" || v === "system" ? v : "system";
}
```
Add the shadcn primitives (run from project root; `components.json` from Plan 01 exists): `npx --prefix web shadcn@latest add button card badge input select progress tabs alert accordion collapsible dropdown-menu skeleton sonner --yes` (if the CLI cannot run non-interactively in this environment, hand-author the listed `web/src/components/ui/*.tsx` files from the shadcn "new-york" registry — each is a thin Radix wrapper using the `cn` helper). Ensure each generated `ui` component uses the semantic Tailwind classes (`bg-accent text-accent-fg`, etc.).

4. - [ ] Run them, expect PASS: `npm --prefix web run test -- --run src/styles/tokens.test.ts src/lib/theme.test.ts` → `Tests` all passed; `npm --prefix web run build` → `dist/index.html` written.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
feat(web): design tokens (teal/slate light+dark), Geist fonts, shadcn/ui setup

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 1: Typed API client mirroring CONTRACTS §6 DTOs (fetch wrapper + error mapping)

**Goal:** Mirror the CONTRACTS §6 REST DTOs verbatim in `web/src/api/types.ts`, and ship a typed `fetch` wrapper (`web/src/api/client.ts`) that sends `credentials: "include"` (httpOnly cookie auth — no token in `localStorage`), serializes/deserializes camelCase JSON, and maps non-2xx responses to a structured `ApiError` carrying the plain message + technical detail + trace id for the §8.2 error pattern.

**Files:**
- Create: `web/src/api/types.ts`
- Create: `web/src/api/client.ts`
- Create: `web/src/api/migrations.ts`
- Test: `web/src/api/client.test.ts`, `web/src/api/migrations.test.ts`

**Acceptance Criteria:**
- [ ] `types.ts` defines `ProviderId` (`"imap" | "graph" | "gmail"`), `JobStatus` (`Draft | Queued | PreFlight | AwaitingApproval | Running | Paused | Completed | Partial | Failed | Cancelled`), `MigrationDto` (`{ id, status, wizardStep, from, to, isBatch, scopeSummary, mailboxCount, progress, createdAt }`), `ConnectionTestResult` (`{ ok, folderCount, messageCount, errorCode?, rawDetail? }`), `SetEndpointsRequest`, `ConnectionRequest`, `ScopeRequest`, `PreflightPlanDto`, `PreflightIssueDto`, `MigrationEstimateDto`, `ApproveRequest`, `ResultsDto`, `AuditEntryDto`, `NeedsDecisionDto`, `MigrationProgressDto`, `RemediationAction` — all camelCase, matching CONTRACTS §3/§4/§6 field names.
- [ ] `apiFetch<T>(path, init)` issues `fetch` with `credentials: "include"`, `Content-Type: application/json` when a body is sent, base path `/api/v1`, and returns parsed JSON typed as `T` on 2xx.
- [ ] On a non-2xx response, `apiFetch` throws an `ApiError` with `{ status, code, message, technicalDetail, traceId }` — `traceId` read from the `X-Trace-Id`/`traceparent` response header or the JSON body's `traceId`/`errorCode`; `message` is the body's plain message or a sensible fallback.
- [ ] No request ever reads or writes an auth token in `localStorage`/`sessionStorage` (asserted by a test that spies on storage).
- [ ] `migrations.ts` exposes typed functions: `createMigration()`, `listMigrations(query?)`, `getMigration(id)`, `deleteMigration(id)`, `setEndpoints(id, body)`, `putConnection(id, side, body)`, `testConnection(id, side)`, `putScope(id, body)`, `startPreflight(id)`, `getPreflight(id)`, `approve(id, body)`, `pause/resume/cancel(id)`, `getResults(id)`, `getAudit(id, query?)`, `rerun(id)`, `reportUrl(id, format)` — each hitting the exact CONTRACTS §6 route + method.

**Verify:** `npm --prefix web run test -- --run src/api/client.test.ts src/api/migrations.test.ts` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing tests. Create `web/src/api/client.test.ts`:
```ts
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiFetch } from "./client";

describe("apiFetch", () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it("calls /api/v1 with credentials include and parses JSON", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: "m1", status: "Draft" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);
    const out = await apiFetch<{ id: string; status: string }>("/migrations/m1");
    expect(out.id).toBe("m1");
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/v1/migrations/m1");
    expect(init.credentials).toBe("include");
  });

  it("maps a non-2xx response to ApiError with trace id", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({ message: "We couldn't sign in.", errorCode: "AUTH_FAILED", traceId: "4f9c-21a8" }),
          { status: 401, headers: { "Content-Type": "application/json", "X-Trace-Id": "4f9c-21a8" } },
        ),
      ),
    );
    await expect(apiFetch("/migrations/m1")).rejects.toMatchObject({
      status: 401,
      code: "AUTH_FAILED",
      traceId: "4f9c-21a8",
    } satisfies Partial<ApiError>);
  });

  it("never touches localStorage for auth", async () => {
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    const getItem = vi.spyOn(Storage.prototype, "getItem");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("{}", { status: 200 })));
    await apiFetch("/migrations");
    expect(setItem).not.toHaveBeenCalledWith(expect.stringMatching(/token|auth|jwt/i), expect.anything());
    expect(getItem).not.toHaveBeenCalledWith(expect.stringMatching(/token|auth|jwt/i));
  });
});
```
Create `web/src/api/migrations.test.ts`:
```ts
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createMigration, listMigrations, testConnection } from "./migrations";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("migrations api", () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
  });
  afterEach(() => vi.restoreAllMocks());

  it("POSTs to /migrations to create a draft", async () => {
    fetchMock.mockResolvedValue(jsonResponse({ id: "m1", status: "Draft", wizardStep: 0 }));
    const dto = await createMigration();
    expect(dto.status).toBe("Draft");
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/v1/migrations");
    expect(init.method).toBe("POST");
  });

  it("lists migrations with status + q query", async () => {
    fetchMock.mockResolvedValue(jsonResponse([{ id: "m1", status: "Running" }]));
    await listMigrations({ status: "Running", q: "work" });
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/migrations?status=Running&q=work");
  });

  it("POSTs the test-connection route for a side", async () => {
    fetchMock.mockResolvedValue(jsonResponse({ ok: true, folderCount: 14, messageCount: 3201 }));
    const r = await testConnection("m1", "from");
    expect(r.ok).toBe(true);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/migrations/m1/connection/from/test");
    expect(fetchMock.mock.calls[0][1].method).toBe("POST");
  });
});
```

2. - [ ] Run them, expect FAIL: `npm --prefix web run test -- --run src/api/client.test.ts src/api/migrations.test.ts` → fails — `./client` and `./migrations` do not exist.

3. - [ ] Minimal implementation. Create `web/src/api/types.ts`:
```ts
// Mirrors EMaigrator v1 CONTRACTS §3/§4/§6 — camelCase JSON. Do not invent fields.
export type ProviderId = "imap" | "graph" | "gmail";
export type ConnectionSide = "from" | "to";

export type JobStatus =
  | "Draft" | "Queued" | "PreFlight" | "AwaitingApproval" | "Running"
  | "Paused" | "Completed" | "Partial" | "Failed" | "Cancelled";

export type AuthMethod =
  | "ImapBasic" | "ImapOAuthXoauth2" | "GraphAppOAuth" | "GraphDelegatedOAuth"
  | "GmailServiceAccountDwd" | "GmailDelegatedOAuth";

export type Severity = "Info" | "Warning" | "Blocker";

export type RemediationAction =
  | "None" | "RetryWithBackoff" | "FlattenFolder" | "SanitizeFolderName"
  | "RenameFolder" | "MergeFolder" | "SkipMessage";

export interface MigrationProgressDto {
  migratedCount: number;       // CONTRACTS §4 MigrationProgressEvent.Migrated
  total: number;
  currentFolder: string | null;
  msgPerMin: number;
  status: JobStatus;           // ∈ JobStatus — never a throttling sentinel
  // API view-model projection: throttling is NOT a JobStatus (CONTRACTS freezes Status ∈ JobStatus),
  // so the rate-limited signal rides a dedicated optional flag the Api (Plan 08) sets from the
  // rate-limiter. Absent/false ⇒ not throttled.
  throttled?: boolean;
}

export interface MigrationDto {
  id: string;
  status: JobStatus;
  wizardStep: number;
  from: ProviderId | null;
  to: ProviderId | null;
  isBatch: boolean;
  scopeSummary: string | null;
  mailboxCount: number;
  progress: MigrationProgressDto | null;
  createdAt: string;
}

export interface ConnectionTestResult {
  ok: boolean;
  folderCount: number;
  messageCount: number;
  errorCode?: string | null;
  rawDetail?: string | null;
}

export interface SetEndpointsRequest { from: ProviderId; to: ProviderId; }
export interface ConnectionRequest {
  auth: AuthMethod;
  settings: Record<string, string>;
  secret: string;
}
export interface MailboxPairDto { sourceMailbox: string; destMailbox: string; }
export interface ScopeRequest {
  isBatch: boolean;
  pairs: MailboxPairDto[];
  includeFolders?: string[] | null;
  excludeFolders?: string[] | null;
  since?: string | null;
  before?: string | null;
}
export interface PreflightIssueDto {
  issueType: string;
  affectedPaths: string[];
  recommendedAction: RemediationAction;
  options: RemediationAction[];
  severity: Severity;
  description: string;
}
export interface MigrationEstimateDto {
  mailboxCount: number;
  folderCount: number;
  messageCount: number;
  totalBytes: number;
  estimatedDurationSeconds: number;
}
// UsageDto and the `usage`/`scanning` fields below are API view-model projections (hosted billing
// §14 + async-preflight §6) layered on top of the frozen Core `PreflightPlan(Issues, Estimate)`
// record. They are owned and shaped by EMaigrator.Api (Plan 08); the frontend mirrors that wire
// shape here. Field names stay camelCase and must track Plan 08's serializer.
export interface UsageDto {
  used: number;
  quota: number;
  overCapMailboxes: number;
  capGb: number;
}
export interface PreflightPlanDto {
  issues: PreflightIssueDto[];
  estimate: MigrationEstimateDto;
  usage: UsageDto | null;
  scanning: boolean;
}
export interface ApproveRequest { resolutions: Record<string, RemediationAction>; }
export interface NeedsDecisionDto {
  migrationId: string;
  issueType: string;
  detail: string;
  options: RemediationAction[];
}
export interface ResultsDto {
  status: JobStatus;
  migratedCount: number;
  skippedCount: number;
  failedCount: number;
  needsDecision: NeedsDecisionDto[];
  sourceCount: number;
  destCount: number;
  durationSeconds: number;
  logDeletesAt: string;
}
export interface AuditEntryDto {
  subject: string | null;
  messageDate: string;
  sourceFolder: string;
  destFolder: string;
  status: "migrated" | "skipped" | "failed";
  errorCode?: string | null;
}
```
Create `web/src/api/client.ts`:
```ts
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | null,
    message: string,
    readonly technicalDetail: string | null,
    readonly traceId: string | null,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

const BASE = "/api/v1";

export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const hasBody = init.body !== undefined && init.body !== null;
  const res = await fetch(`${BASE}${path}`, {
    ...init,
    credentials: "include", // httpOnly auth cookie; no token in localStorage
    headers: {
      ...(hasBody ? { "Content-Type": "application/json" } : {}),
      ...(init.headers ?? {}),
    },
  });
  if (!res.ok) throw await toApiError(res);
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

async function toApiError(res: Response): Promise<ApiError> {
  let body: Record<string, unknown> = {};
  try {
    const t = await res.text();
    if (t) body = JSON.parse(t) as Record<string, unknown>;
  } catch {
    /* non-JSON error body */
  }
  const traceId =
    res.headers.get("X-Trace-Id") ??
    res.headers.get("traceparent") ??
    (typeof body.traceId === "string" ? body.traceId : null);
  const code =
    typeof body.errorCode === "string"
      ? body.errorCode
      : typeof body.code === "string"
        ? body.code
        : null;
  const message =
    typeof body.message === "string" ? body.message : `Request failed (${res.status})`;
  const technicalDetail =
    typeof body.rawDetail === "string"
      ? body.rawDetail
      : typeof body.detail === "string"
        ? body.detail
        : null;
  return new ApiError(res.status, code, message, technicalDetail, traceId);
}
```
Create `web/src/api/migrations.ts`:
```ts
import { apiFetch } from "./client";
import type {
  ApproveRequest, AuditEntryDto, ConnectionRequest, ConnectionSide, ConnectionTestResult,
  MigrationDto, PreflightPlanDto, ResultsDto, ScopeRequest, SetEndpointsRequest,
} from "./types";

export const createMigration = () =>
  apiFetch<MigrationDto>("/migrations", { method: "POST", body: "{}" });

export const listMigrations = (q?: { status?: string; q?: string }) => {
  const params = new URLSearchParams();
  if (q?.status) params.set("status", q.status);
  if (q?.q) params.set("q", q.q);
  const qs = params.toString();
  return apiFetch<MigrationDto[]>(`/migrations${qs ? `?${qs}` : ""}`);
};

export const getMigration = (id: string) => apiFetch<MigrationDto>(`/migrations/${id}`);
export const deleteMigration = (id: string) =>
  apiFetch<void>(`/migrations/${id}`, { method: "DELETE" });

export const setEndpoints = (id: string, body: SetEndpointsRequest) =>
  apiFetch<MigrationDto>(`/migrations/${id}/endpoints`, { method: "PATCH", body: JSON.stringify(body) });

export const putConnection = (id: string, side: ConnectionSide, body: ConnectionRequest) =>
  apiFetch<MigrationDto>(`/migrations/${id}/connection/${side}`, { method: "PUT", body: JSON.stringify(body) });

export const testConnection = (id: string, side: ConnectionSide) =>
  apiFetch<ConnectionTestResult>(`/migrations/${id}/connection/${side}/test`, { method: "POST" });

export const putScope = (id: string, body: ScopeRequest) =>
  apiFetch<MigrationDto>(`/migrations/${id}/scope`, { method: "PUT", body: JSON.stringify(body) });

export const startPreflight = (id: string) =>
  apiFetch<void>(`/migrations/${id}/preflight`, { method: "POST" });
export const getPreflight = (id: string) => apiFetch<PreflightPlanDto>(`/migrations/${id}/preflight`);

export const approve = (id: string, body: ApproveRequest) =>
  apiFetch<MigrationDto>(`/migrations/${id}/approve`, { method: "POST", body: JSON.stringify(body) });

export const pause = (id: string) => apiFetch<MigrationDto>(`/migrations/${id}/pause`, { method: "POST" });
export const resume = (id: string) => apiFetch<MigrationDto>(`/migrations/${id}/resume`, { method: "POST" });
export const cancel = (id: string) => apiFetch<MigrationDto>(`/migrations/${id}/cancel`, { method: "POST" });

export const getResults = (id: string) => apiFetch<ResultsDto>(`/migrations/${id}/results`);
export const getAudit = (id: string, q?: { q?: string; failuresOnly?: boolean }) => {
  const params = new URLSearchParams();
  if (q?.q) params.set("q", q.q);
  if (q?.failuresOnly) params.set("failuresOnly", "true");
  const qs = params.toString();
  return apiFetch<AuditEntryDto[]>(`/migrations/${id}/audit${qs ? `?${qs}` : ""}`);
};
export const rerun = (id: string) => apiFetch<MigrationDto>(`/migrations/${id}/rerun`, { method: "POST" });
export const reportUrl = (id: string, format: "csv" | "pdf") =>
  `/api/v1/migrations/${id}/report?format=${format}`;
```

4. - [ ] Run them, expect PASS: `npm --prefix web run test -- --run src/api/client.test.ts src/api/migrations.test.ts` → `Tests` all passed.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
feat(web): typed API client mirroring CONTRACTS DTOs with error mapping

Cookie auth (credentials: include); no token persisted. ApiError carries
plain message + technical detail + trace id for the error pattern.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 2: SignalR client with reconnecting state (tested against a mock connection)

**Goal:** Wrap `@microsoft/signalr` in a `MigrationsHubClient` that connects to `/hubs/migrations`, exposes a connection-state callback (`connected | reconnecting | disconnected`), subscribes/unsubscribes to a migration group, and dispatches the three server→client events whose names match the hub methods exactly (`Progress`, `StatusChanged`, `NeedsDecision`). Tested against an injectable fake `HubConnection` so no real socket is needed; plus a `useMigrationStream` React hook.

**Files:**
- Create: `web/src/api/signalr.ts`
- Create: `web/src/api/useMigrationStream.ts`
- Test: `web/src/api/signalr.test.ts`

**Acceptance Criteria:**
- [ ] `createHub()` builds a `HubConnectionBuilder().withUrl("/hubs/migrations").withAutomaticReconnect()` connection; `MigrationsHubClient` accepts an injected factory for tests.
- [ ] `MigrationsHubClient` registers handlers for `Progress(dto)`, `StatusChanged(migrationId, status)`, `NeedsDecision(migrationId, dto)` — names byte-identical to CONTRACTS §6 `IMigrationProgressClient`.
- [ ] `client.subscribe(id)` invokes hub `Subscribe` with the id; `unsubscribe(id)` invokes `Unsubscribe`.
- [ ] `client.onStateChange(cb)` fires `"reconnecting"` on the connection's `onreconnecting`, `"connected"` on `onreconnected`/`start`, `"disconnected"` on `onclose`/`stop`.
- [ ] Incoming `Progress`/`StatusChanged`/`NeedsDecision` invocations are forwarded to registered listeners with parsed args.
- [ ] No auth token is read from storage to authenticate the socket (cookie carries auth) — asserted by a storage spy.

**Verify:** `npm --prefix web run test -- --run src/api/signalr.test.ts` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing test. Create `web/src/api/signalr.test.ts`:
```ts
import { describe, expect, it, vi } from "vitest";
import { MigrationsHubClient } from "./signalr";

function makeFakeHub() {
  const handlers = new Map<string, (...args: unknown[]) => void>();
  const lifecycle: Record<string, () => void> = {};
  return {
    handlers,
    lifecycle,
    state: "Disconnected",
    on: vi.fn((name: string, cb: (...a: unknown[]) => void) => handlers.set(name, cb)),
    invoke: vi.fn().mockResolvedValue(undefined),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    onreconnecting: vi.fn((cb: () => void) => (lifecycle.reconnecting = cb)),
    onreconnected: vi.fn((cb: () => void) => (lifecycle.reconnected = cb)),
    onclose: vi.fn((cb: () => void) => (lifecycle.close = cb)),
    fire(name: string, ...args: unknown[]) { handlers.get(name)?.(...args); },
  };
}

describe("MigrationsHubClient", () => {
  it("registers the three contract event handlers by exact name", async () => {
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    await client.start();
    expect(hub.on).toHaveBeenCalledWith("Progress", expect.any(Function));
    expect(hub.on).toHaveBeenCalledWith("StatusChanged", expect.any(Function));
    expect(hub.on).toHaveBeenCalledWith("NeedsDecision", expect.any(Function));
  });

  it("invokes Subscribe/Unsubscribe with the migration id", async () => {
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    await client.start();
    await client.subscribe("m1");
    await client.unsubscribe("m1");
    expect(hub.invoke).toHaveBeenCalledWith("Subscribe", "m1");
    expect(hub.invoke).toHaveBeenCalledWith("Unsubscribe", "m1");
  });

  it("reflects reconnecting state transitions", async () => {
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    const states: string[] = [];
    client.onStateChange((s) => states.push(s));
    await client.start();
    hub.lifecycle.reconnecting!();
    hub.lifecycle.reconnected!();
    hub.lifecycle.close!();
    expect(states).toContain("connected");
    expect(states).toContain("reconnecting");
    expect(states).toContain("disconnected");
  });

  it("forwards Progress events to listeners", async () => {
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    const seen: unknown[] = [];
    client.onProgress((dto) => seen.push(dto));
    await client.start();
    hub.fire("Progress", { migratedCount: 5, total: 10, status: "Running" });
    expect(seen).toEqual([{ migratedCount: 5, total: 10, status: "Running" }]);
  });

  it("does not read auth tokens from storage", async () => {
    const getItem = vi.spyOn(Storage.prototype, "getItem");
    const hub = makeFakeHub();
    const client = new MigrationsHubClient(() => hub as never);
    await client.start();
    expect(getItem).not.toHaveBeenCalledWith(expect.stringMatching(/token|auth|jwt/i));
  });
});
```

2. - [ ] Run it, expect FAIL: `npm --prefix web run test -- --run src/api/signalr.test.ts` → fails — `./signalr` does not exist.

3. - [ ] Minimal implementation. Create `web/src/api/signalr.ts`:
```ts
import {
  HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel,
} from "@microsoft/signalr";
import type { MigrationProgressDto, NeedsDecisionDto } from "./types";

export type ConnectionState = "connected" | "reconnecting" | "disconnected";

export function createHub(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl("/hubs/migrations") // auth via httpOnly cookie the browser sends automatically
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}

type ProgressFn = (dto: MigrationProgressDto) => void;
type StatusFn = (migrationId: string, status: string) => void;
type NeedsFn = (migrationId: string, dto: NeedsDecisionDto) => void;
type StateFn = (state: ConnectionState) => void;

export class MigrationsHubClient {
  private readonly hub: HubConnection;
  private progress: ProgressFn[] = [];
  private status: StatusFn[] = [];
  private needs: NeedsFn[] = [];
  private stateCbs: StateFn[] = [];

  constructor(factory: () => HubConnection = createHub) {
    this.hub = factory();
    this.hub.on("Progress", (dto: MigrationProgressDto) => this.progress.forEach((f) => f(dto)));
    this.hub.on("StatusChanged", (id: string, s: string) => this.status.forEach((f) => f(id, s)));
    this.hub.on("NeedsDecision", (id: string, dto: NeedsDecisionDto) => this.needs.forEach((f) => f(id, dto)));
    this.hub.onreconnecting(() => this.emit("reconnecting"));
    this.hub.onreconnected(() => this.emit("connected"));
    this.hub.onclose(() => this.emit("disconnected"));
  }

  private emit(s: ConnectionState) { this.stateCbs.forEach((f) => f(s)); }

  onProgress(cb: ProgressFn) { this.progress.push(cb); return () => { this.progress = this.progress.filter((f) => f !== cb); }; }
  onStatusChanged(cb: StatusFn) { this.status.push(cb); return () => { this.status = this.status.filter((f) => f !== cb); }; }
  onNeedsDecision(cb: NeedsFn) { this.needs.push(cb); return () => { this.needs = this.needs.filter((f) => f !== cb); }; }
  onStateChange(cb: StateFn) { this.stateCbs.push(cb); return () => { this.stateCbs = this.stateCbs.filter((f) => f !== cb); }; }

  async start() {
    if (this.hub.state === HubConnectionState.Connected) return;
    await this.hub.start();
    this.emit("connected");
  }
  async stop() { await this.hub.stop(); this.emit("disconnected"); }
  subscribe(id: string) { return this.hub.invoke("Subscribe", id); }
  unsubscribe(id: string) { return this.hub.invoke("Unsubscribe", id); }
}
```
Create `web/src/api/useMigrationStream.ts`:
```ts
import { useEffect, useRef, useState } from "react";
import { MigrationsHubClient, type ConnectionState } from "./signalr";
import type { MigrationProgressDto, NeedsDecisionDto } from "./types";

export interface MigrationStream {
  connectionState: ConnectionState;
  progress: MigrationProgressDto | null;
  status: string | null;
  needsDecision: NeedsDecisionDto[];
}

export function useMigrationStream(migrationId: string | null): MigrationStream {
  const clientRef = useRef<MigrationsHubClient | null>(null);
  const [connectionState, setConnectionState] = useState<ConnectionState>("disconnected");
  const [progress, setProgress] = useState<MigrationProgressDto | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [needsDecision, setNeeds] = useState<NeedsDecisionDto[]>([]);

  useEffect(() => {
    if (!migrationId) return;
    const client = new MigrationsHubClient();
    clientRef.current = client;
    const offs = [
      client.onStateChange(setConnectionState),
      client.onProgress(setProgress),
      client.onStatusChanged((_, s) => setStatus(s)),
      client.onNeedsDecision((_, dto) => setNeeds((prev) => [...prev, dto])),
    ];
    let cancelled = false;
    void (async () => {
      await client.start();
      if (!cancelled) await client.subscribe(migrationId);
    })();
    return () => {
      cancelled = true;
      offs.forEach((off) => off());
      void client.unsubscribe(migrationId).catch(() => {});
      void client.stop().catch(() => {});
    };
  }, [migrationId]);

  return { connectionState, progress, status, needsDecision };
}
```

4. - [ ] Run it, expect PASS: `npm --prefix web run test -- --run src/api/signalr.test.ts` → `Tests` all passed.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
feat(web): SignalR client with reconnecting state + migration stream hook

Event names match the hub contract (Progress/StatusChanged/NeedsDecision);
auth via cookie, no token read from storage.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 3: Router shell + Dashboard (cards⇄list density toggle, live progress, usage widget, welcome state)

**Goal:** Build the React Router app shell (sidebar + top bar per the handoff `shell.jsx`) and the Dashboard route: a migrations list with a cards⇄list density toggle, status chips with live SignalR progress, the hosted usage widget, and a dedicated empty/welcome first-run state.

**Files:**
- Create: `web/src/components/StatusChip.tsx`, `web/src/components/AppShell.tsx`, `web/src/components/ProviderRoute.tsx`
- Create: `web/src/routes/Dashboard.tsx`, `web/src/routes/Dashboard.empty.tsx` (Welcome sub-component) — Welcome lives inside `Dashboard.tsx`
- Create: `web/src/app/router.tsx`, `web/src/app/ThemeProvider.tsx`
- Modify: `web/src/App.tsx`, `web/src/main.tsx`
- Test: `web/src/components/StatusChip.test.tsx`, `web/src/routes/Dashboard.test.tsx`

**Acceptance Criteria:**
- [ ] `StatusChip` renders **icon + text label** for each status (`done`, `running`, `throttled`, `warning`, `error`, `queued`) — never color alone; has `role="status"` and an accessible name including the label text.
- [ ] `Dashboard` lists `MigrationDto[]` from `listMigrations()`; each row shows From→To, scope summary, status chip, and a context action (Resume for Draft / View for Running / Results for Completed/Partial).
- [ ] A cards⇄list density toggle switches the layout; the choice persists to `localStorage["em-dash-layout"]`.
- [ ] When the list is empty, a dedicated **Welcome** state renders (one line of what EMaigrator does + a single primary "Start your first migration" button), not an empty table.
- [ ] The usage widget (`UsageWidget`) renders `used / quota mailboxes this month` with a progress bar and an Upgrade link when a `UsageDto` is passed, and renders **nothing** when `usage` is `null` (no fabricated demo data); the Dashboard holds `usage` as state (null until the hosted usage endpoint is wired in Wave F).
- [ ] Running rows render their progress percentage from `MigrationDto.progress` (e.g. a row with `progress.migratedCount=126, total=218` shows `58%`). Live per-row SignalR updates on the dashboard are out of scope for this task — the streaming hook is exercised on the Run view (Task 9).
- [ ] Routes resolve: `/` → Dashboard; `/migrations/:id/*` → Wizard (placeholder route registered, fleshed out in later tasks).

**Verify:** `npm --prefix web run test -- --run src/components/StatusChip.test.tsx src/routes/Dashboard.test.tsx` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing tests. Create `web/src/components/StatusChip.test.tsx`:
```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatusChip } from "./StatusChip";

describe("StatusChip", () => {
  it("shows an icon and a text label (never color alone)", () => {
    render(<StatusChip status="throttled" />);
    const chip = screen.getByRole("status");
    expect(chip).toHaveAccessibleName(/slowing to respect/i);
    expect(chip.querySelector("svg")).not.toBeNull();
  });

  it("labels success as Migrated and error as Failed", () => {
    const { rerender } = render(<StatusChip status="done" />);
    expect(screen.getByText(/migrated|done/i)).toBeInTheDocument();
    rerender(<StatusChip status="error" />);
    expect(screen.getByText(/failed/i)).toBeInTheDocument();
  });
});
```
Create `web/src/routes/Dashboard.test.tsx`:
```tsx
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Dashboard, UsageWidget } from "./Dashboard";
import * as api from "../api/migrations";

const draft = { id: "d1", status: "Draft", wizardStep: 1, from: "imap", to: "graph", isBatch: false, scopeSummary: "1 mailbox", mailboxCount: 1, progress: null, createdAt: "2026-06-01T00:00:00Z" };
const running = { id: "r1", status: "Running", wizardStep: 5, from: "imap", to: "graph", isBatch: true, scopeSummary: "218 mailboxes", mailboxCount: 218, progress: { migratedCount: 126, total: 218, currentFolder: null, msgPerMin: 1402, status: "Running" }, createdAt: "2026-06-01T00:00:00Z" };

function renderDash() {
  return render(<MemoryRouter><Dashboard /></MemoryRouter>);
}

describe("Dashboard", () => {
  beforeEach(() => vi.spyOn(api, "listMigrations"));
  afterEach(() => vi.restoreAllMocks());

  it("shows the welcome state when there are no migrations", async () => {
    (api.listMigrations as ReturnType<typeof vi.fn>).mockResolvedValue([]);
    renderDash();
    expect(await screen.findByRole("button", { name: /start your first migration/i })).toBeInTheDocument();
  });

  it("lists migrations with status chips, context actions, and per-row progress", async () => {
    (api.listMigrations as ReturnType<typeof vi.fn>).mockResolvedValue([draft, running]);
    renderDash();
    expect(await screen.findByText(/218 mailboxes/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /resume/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /view/i })).toBeInTheDocument();
    // 126 / 218 -> 58% rendered from MigrationDto.progress (no SignalR needed)
    expect(screen.getByText("58%")).toBeInTheDocument();
  });

  it("toggles cards/list density and persists the choice", async () => {
    (api.listMigrations as ReturnType<typeof vi.fn>).mockResolvedValue([running]);
    renderDash();
    await screen.findByText(/218 mailboxes/i);
    await userEvent.click(screen.getByRole("button", { name: /list view/i }));
    await waitFor(() => expect(localStorage.getItem("em-dash-layout")).toBe("list"));
  });
});

describe("UsageWidget", () => {
  it("renders used/quota with a bar and Upgrade link when usage is present", () => {
    render(
      <MemoryRouter>
        <UsageWidget usage={{ used: 128, quota: 200, overCapMailboxes: 0, capGb: 50 }} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/128 \/ 200 mailboxes this month/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /upgrade/i })).toBeInTheDocument();
  });

  it("renders nothing when there is no usage (never a fabricated demo bar)", () => {
    const { container } = render(
      <MemoryRouter>
        <UsageWidget usage={null} />
      </MemoryRouter>,
    );
    expect(container).toBeEmptyDOMElement();
  });
});
```

2. - [ ] Run them, expect FAIL: `npm --prefix web run test -- --run src/components/StatusChip.test.tsx src/routes/Dashboard.test.tsx` → fails — `StatusChip`, `Dashboard` do not exist.

3. - [ ] Minimal implementation. Create `web/src/components/StatusChip.tsx`:
```tsx
import { AlertTriangle, Check, Circle, Play, RotateCcw, X } from "lucide-react";
import type { JSX } from "react";

export type ChipStatus = "done" | "running" | "throttled" | "warning" | "error" | "queued";

const MAP: Record<ChipStatus, { label: string; icon: JSX.Element; cls: string }> = {
  done: { label: "Migrated", icon: <Check size={14} aria-hidden />, cls: "text-success" },
  running: { label: "Running", icon: <Play size={14} aria-hidden />, cls: "text-accent" },
  throttled: { label: "Slowing to respect limits", icon: <RotateCcw size={14} aria-hidden />, cls: "text-throttled" },
  warning: { label: "Needs decision", icon: <AlertTriangle size={14} aria-hidden />, cls: "text-warning" },
  error: { label: "Failed", icon: <X size={14} aria-hidden />, cls: "text-error" },
  queued: { label: "Queued", icon: <Circle size={14} aria-hidden />, cls: "text-idle" },
};

export function StatusChip({ status }: { status: ChipStatus }) {
  const { label, icon, cls } = MAP[status];
  return (
    <span role="status" aria-label={label}
      className={`inline-flex items-center gap-1.5 rounded-[4px] px-2 py-0.5 text-sm ${cls}`}>
      {icon}
      <span>{label}</span>
    </span>
  );
}

export function jobStatusToChip(status: string): ChipStatus {
  switch (status) {
    case "Completed": return "done";
    case "Running": case "PreFlight": return "running";
    case "Paused": case "Queued": case "AwaitingApproval": case "Draft": return "queued";
    case "Partial": return "warning";
    case "Failed": return "error";
    case "Cancelled": return "error";
    default: return "queued";
  }
}
```
Create `web/src/components/ProviderRoute.tsx`:
```tsx
import type { ProviderId } from "../api/types";

const NAME: Record<ProviderId, string> = { imap: "WorkMail", graph: "Microsoft 365", gmail: "Google" };

export function providerName(p: ProviderId | null): string {
  return p ? NAME[p] : "—";
}

export function ProviderRoute({ from, to }: { from: ProviderId | null; to: ProviderId | null }) {
  return (
    <span className="inline-flex items-center gap-2 font-medium">
      <span>{providerName(from)}</span>
      <span aria-hidden>→</span>
      <span>{providerName(to)}</span>
    </span>
  );
}
```
Create `web/src/app/ThemeProvider.tsx`:
```tsx
import { useEffect } from "react";
import { applyTheme, loadTheme } from "../lib/theme";

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  useEffect(() => {
    applyTheme(loadTheme());
    const density = localStorage.getItem("em-density") ?? "comfortable";
    document.documentElement.dataset.density = density;
  }, []);
  return <>{children}</>;
}
```
Create `web/src/components/AppShell.tsx`:
```tsx
import { Link, Outlet, useLocation } from "react-router-dom";
import { LayoutDashboard, Plus } from "lucide-react";
import { ThemeToggle } from "./ThemeToggle";

export function AppShell() {
  const { pathname } = useLocation();
  return (
    <div className="flex min-h-screen bg-bg text-fg">
      <aside className="w-[230px] shrink-0 border-r border-border bg-surface p-4" aria-label="Primary">
        <div className="mb-6 font-semibold">EMaigrator</div>
        <nav className="space-y-1">
          <Link to="/" aria-current={pathname === "/" ? "page" : undefined}
            className="flex items-center gap-2 rounded-[6px] px-3 py-2 hover:bg-surface-2">
            <LayoutDashboard size={16} aria-hidden /> Dashboard
          </Link>
        </nav>
      </aside>
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-10 flex h-[58px] items-center justify-between border-b border-border bg-bg/80 px-6 backdrop-blur">
          <h1 className="text-[length:var(--fs-h2)] font-semibold">Migrations</h1>
          <div className="flex items-center gap-3">
            <Link to="/migrations/new"
              className="inline-flex items-center gap-1.5 rounded-[8px] bg-accent px-3 py-1.5 text-accent-fg">
              <Plus size={16} aria-hidden /> New Migration
            </Link>
            <ThemeToggle />
          </div>
        </header>
        <main className="flex-1 overflow-auto p-6"><Outlet /></main>
      </div>
    </div>
  );
}
```
Create `web/src/components/ThemeToggle.tsx`:
```tsx
import { Monitor, Moon, Sun } from "lucide-react";
import { applyTheme, loadTheme, type Theme } from "../lib/theme";
import { useState } from "react";

const OPTS: { value: Theme; icon: React.ReactNode; label: string }[] = [
  { value: "light", icon: <Sun size={15} aria-hidden />, label: "Light" },
  { value: "dark", icon: <Moon size={15} aria-hidden />, label: "Dark" },
  { value: "system", icon: <Monitor size={15} aria-hidden />, label: "System" },
];

export function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>(loadTheme());
  return (
    <div role="group" aria-label="Theme" className="flex rounded-[8px] border border-border">
      {OPTS.map((o) => (
        <button key={o.value} type="button" aria-label={o.label} aria-pressed={theme === o.value}
          onClick={() => { applyTheme(o.value); setTheme(o.value); }}
          className={`flex h-8 w-9 items-center justify-center ${theme === o.value ? "text-accent" : "text-fg-muted"}`}>
          {o.icon}
        </button>
      ))}
    </div>
  );
}
```
Create `web/src/routes/Dashboard.tsx`:
```tsx
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { LayoutGrid, List } from "lucide-react";
import { listMigrations } from "../api/migrations";
import type { MigrationDto, UsageDto } from "../api/types";
import { jobStatusToChip, StatusChip } from "../components/StatusChip";
import { ProviderRoute } from "../components/ProviderRoute";

type Layout = "cards" | "list";

function actionFor(m: MigrationDto): { to: string; label: string } {
  if (m.status === "Draft") return { to: `/migrations/${m.id}`, label: "Resume" };
  if (m.status === "Completed" || m.status === "Partial" || m.status === "Failed" || m.status === "Cancelled")
    return { to: `/migrations/${m.id}/results`, label: "Results" };
  return { to: `/migrations/${m.id}/run`, label: "View" };
}

function pct(m: MigrationDto): number {
  const p = m.progress;
  return p && p.total > 0 ? Math.round((p.migratedCount / p.total) * 100) : 0;
}

function Welcome() {
  return (
    <div className="mx-auto max-w-[560px] py-20 text-center">
      <h2 className="text-[length:var(--fs-display)] font-semibold">Move your email, safely.</h2>
      <p className="mt-3 text-fg-muted">
        EMaigrator copies a mailbox from one provider to another — your source is never changed.
      </p>
      <Link to="/migrations/new"
        className="mt-8 inline-flex rounded-[8px] bg-accent px-5 py-3 text-accent-fg">
        Start your first migration
      </Link>
    </div>
  );
}

export function Dashboard() {
  const [items, setItems] = useState<MigrationDto[] | null>(null);
  // Usage is hosted-only and arrives from the API in Wave F (see follow-up note at the
  // end of this plan). Until that endpoint is wired it stays null, so the widget is
  // simply not rendered — we never fabricate a fake usage bar.
  const [usage] = useState<UsageDto | null>(null);
  const [layout, setLayout] = useState<Layout>(
    (localStorage.getItem("em-dash-layout") as Layout) ?? "cards",
  );

  useEffect(() => { void listMigrations().then(setItems); }, []);

  function setAndPersist(l: Layout) {
    setLayout(l);
    localStorage.setItem("em-dash-layout", l);
  }

  if (items === null) {
    return <div role="status" aria-label="Loading migrations" className="h-24 animate-pulse rounded bg-surface-2" />;
  }
  if (items.length === 0) return <Welcome />;

  return (
    <div className="space-y-6">
      <UsageWidget usage={usage} />
      <div className="flex justify-end gap-1" role="group" aria-label="Layout density">
        <button type="button" aria-label="Cards view" aria-pressed={layout === "cards"}
          onClick={() => setAndPersist("cards")} className={layout === "cards" ? "text-accent" : "text-fg-muted"}>
          <LayoutGrid size={18} aria-hidden />
        </button>
        <button type="button" aria-label="List view" aria-pressed={layout === "list"}
          onClick={() => setAndPersist("list")} className={layout === "list" ? "text-accent" : "text-fg-muted"}>
          <List size={18} aria-hidden />
        </button>
      </div>
      <ul className={layout === "cards" ? "grid gap-[var(--grid-gap)] md:grid-cols-2" : "divide-y divide-border"}>
        {items.map((m) => {
          const a = actionFor(m);
          return (
            <li key={m.id} className="flex items-center justify-between gap-4 rounded-[6px] border border-border p-[var(--card-pad)]">
              <div className="min-w-0">
                <ProviderRoute from={m.from} to={m.to} />
                <div className="mt-1 text-sm text-fg-muted">{m.scopeSummary ?? `${m.mailboxCount} mailboxes`}</div>
              </div>
              <div className="flex items-center gap-4">
                {m.progress ? <span className="mono text-sm">{pct(m)}%</span> : null}
                <StatusChip status={jobStatusToChip(m.status)} />
                <Link to={a.to} className="text-accent">{a.label}</Link>
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

export function UsageWidget({ usage }: { usage: UsageDto | null }) {
  // Hosted-only. Renders nothing when there is no usage info (self-host / not yet loaded) —
  // never a fabricated demo bar.
  if (!usage) return null;
  const pct = usage.quota > 0 ? Math.min(100, Math.round((usage.used / usage.quota) * 100)) : 0;
  return (
    <div className="flex items-center gap-3 rounded-[6px] border border-border p-3 text-sm">
      <span className="text-fg-muted">Usage</span>
      <div className="h-2 w-40 overflow-hidden rounded-full bg-surface-2">
        <div className="h-full bg-accent" style={{ width: `${pct}%` }} />
      </div>
      <span className="mono">{usage.used} / {usage.quota} mailboxes this month</span>
      <Link to="/upgrade" className="ml-auto text-accent">Upgrade</Link>
    </div>
  );
}
```
Create `web/src/app/router.tsx`:
```tsx
import { createBrowserRouter } from "react-router-dom";
import { AppShell } from "../components/AppShell";
import { Dashboard } from "../routes/Dashboard";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <AppShell />,
    children: [
      { index: true, element: <Dashboard /> },
      // wizard + run + results routes registered in later tasks
    ],
  },
]);
```
Replace `web/src/App.tsx`:
```tsx
import { RouterProvider } from "react-router-dom";
import { ThemeProvider } from "./app/ThemeProvider";
import { router } from "./app/router";

export default function App() {
  return (
    <ThemeProvider>
      <RouterProvider router={router} />
    </ThemeProvider>
  );
}
```
(The Plan 01 `App.test.tsx` heading assertion is superseded; update it to assert the dashboard/welcome renders — or move it to `Dashboard.test.tsx`. Keep a single passing smoke for `App` that renders inside a `MemoryRouter`-free `RouterProvider`.)

4. - [ ] Run them, expect PASS: `npm --prefix web run test -- --run src/components/StatusChip.test.tsx src/routes/Dashboard.test.tsx` → `Tests` all passed.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
feat(web): router shell + dashboard (cards/list, live progress, usage, welcome)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 4: Wizard shell — gated stepper, draft autosave, reset

**Goal:** Build the wizard shell: a forward-gated, back-allowed stepper across the six stops (From&To, Connect From, Connect To, Scope, Review&plan, Run/Results), backed by a draft created server-side at entry and **autosaved per step**, with a "Reset / Start over" action that discards the draft.

**Files:**
- Create: `web/src/wizard/WizardShell.tsx`
- Create: `web/src/wizard/steps.ts`
- Create: `web/src/wizard/useDraft.ts`
- Create: `web/src/wizard/Stepper.tsx`
- Modify: `web/src/app/router.tsx`
- Test: `web/src/wizard/Stepper.test.tsx`, `web/src/wizard/useDraft.test.tsx`

**Acceptance Criteria:**
- [ ] `steps.ts` exports the ordered step list with `key`, `label` (plain language: "From & To", "Connect From", "Connect To", "Scope", "Review & plan", "Run") and a `wizardStep` index mapping to `MigrationDto.wizardStep`.
- [ ] `Stepper` renders all stops; completed stops are clickable (back allowed), future stops past the gate are **disabled** (`aria-disabled`), the current stop has `aria-current="step"`.
- [ ] `useDraft(id)` loads the migration; `useDraft.save(patch)` calls the appropriate endpoint and optimistically advances `wizardStep`; navigating `/migrations/new` creates a draft via `createMigration()` then redirects to `/migrations/:id`.
- [ ] A "Reset / Start over" button calls `deleteMigration(id)` and navigates back to the dashboard.
- [ ] The stepper cannot advance past an incomplete step: `canAdvanceTo(target, maxReached)` returns false for `target > maxReached + 1`.
- [ ] `WizardShell` passes the `Outlet` context `{ migration, canBatch }`, where `canBatchFor(migration)` is `true` only when the destination provider is `graph`/`gmail` (admin/app auth proxy for v1) — consumed by the Scope step.

**Verify:** `npm --prefix web run test -- --run src/wizard/Stepper.test.tsx src/wizard/useDraft.test.tsx` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing tests. Create `web/src/wizard/Stepper.test.tsx`:
```tsx
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { Stepper, canAdvanceTo } from "./Stepper";

describe("Stepper", () => {
  it("marks the current step and disables steps past the gate", () => {
    render(<MemoryRouter><Stepper current={1} maxReached={1} migrationId="m1" /></MemoryRouter>);
    expect(screen.getByText("Connect From").closest("[aria-current]")).toHaveAttribute("aria-current", "step");
    const future = screen.getByText("Review & plan").closest("a,button,div")!;
    expect(future).toHaveAttribute("aria-disabled", "true");
  });

  it("gates forward navigation", () => {
    expect(canAdvanceTo(2, 1)).toBe(true);
    expect(canAdvanceTo(3, 1)).toBe(false);
    expect(canAdvanceTo(0, 1)).toBe(true);
  });
});
```
Create `web/src/wizard/useDraft.test.tsx`:
```tsx
import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useDraft } from "./useDraft";
import * as api from "../api/migrations";

const dto = { id: "m1", status: "Draft", wizardStep: 0, from: null, to: null, isBatch: false, scopeSummary: null, mailboxCount: 0, progress: null, createdAt: "2026-06-01T00:00:00Z" };

describe("useDraft", () => {
  beforeEach(() => {
    vi.spyOn(api, "getMigration").mockResolvedValue(dto as never);
    vi.spyOn(api, "setEndpoints").mockResolvedValue({ ...dto, from: "imap", to: "graph", wizardStep: 1 } as never);
  });
  afterEach(() => vi.restoreAllMocks());

  it("loads the migration by id", async () => {
    const { result } = renderHook(() => useDraft("m1"));
    await waitFor(() => expect(result.current.migration?.id).toBe("m1"));
  });

  it("saves endpoints and advances the step", async () => {
    const { result } = renderHook(() => useDraft("m1"));
    await waitFor(() => expect(result.current.migration).not.toBeNull());
    await act(async () => { await result.current.saveEndpoints("imap", "graph"); });
    expect(api.setEndpoints).toHaveBeenCalledWith("m1", { from: "imap", to: "graph" });
    expect(result.current.migration?.wizardStep).toBe(1);
  });
});
```

2. - [ ] Run them, expect FAIL: `npm --prefix web run test -- --run src/wizard/Stepper.test.tsx src/wizard/useDraft.test.tsx` → fails — modules do not exist.

3. - [ ] Minimal implementation. Create `web/src/wizard/steps.ts`:
```ts
export interface WizardStepDef { key: string; label: string; path: string; }

export const STEPS: WizardStepDef[] = [
  { key: "from-to", label: "From & To", path: "from-to" },
  { key: "connect-from", label: "Connect From", path: "connect/from" },
  { key: "connect-to", label: "Connect To", path: "connect/to" },
  { key: "scope", label: "Scope", path: "scope" },
  { key: "review", label: "Review & plan", path: "review" },
  { key: "run", label: "Run", path: "run" },
];
```
Create `web/src/wizard/Stepper.tsx`:
```tsx
import { Link } from "react-router-dom";
import { STEPS } from "./steps";

export function canAdvanceTo(target: number, maxReached: number): boolean {
  return target <= maxReached + 1;
}

export function Stepper({ current, maxReached, migrationId }: { current: number; maxReached: number; migrationId: string }) {
  return (
    <ol className="mb-8 flex items-center gap-2" aria-label="Migration steps">
      {STEPS.map((s, i) => {
        const reachable = i <= maxReached;
        const isCurrent = i === current;
        const content = (
          <span className="flex items-center gap-2 text-sm">
            <span className={`flex h-6 w-6 items-center justify-center rounded-full text-xs ${isCurrent ? "bg-accent text-accent-fg" : reachable ? "bg-surface-2 text-fg" : "bg-surface-2 text-fg-subtle"}`}>{i + 1}</span>
            {s.label}
          </span>
        );
        if (reachable && !isCurrent) {
          return <li key={s.key}><Link to={`/migrations/${migrationId}/${s.path}`}>{content}</Link></li>;
        }
        return (
          <li key={s.key} aria-current={isCurrent ? "step" : undefined} aria-disabled={!reachable ? "true" : undefined}>
            {content}
          </li>
        );
      })}
    </ol>
  );
}
```
Create `web/src/wizard/useDraft.ts`:
```ts
import { useCallback, useEffect, useState } from "react";
import { getMigration, putScope, setEndpoints } from "../api/migrations";
import type { MigrationDto, ProviderId, ScopeRequest } from "../api/types";

export function useDraft(id: string) {
  const [migration, setMigration] = useState<MigrationDto | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => { void getMigration(id).then(setMigration).catch(setError); }, [id]);

  const saveEndpoints = useCallback(async (from: ProviderId, to: ProviderId) => {
    const next = await setEndpoints(id, { from, to });
    setMigration(next);
    return next;
  }, [id]);

  const saveScope = useCallback(async (scope: ScopeRequest) => {
    const next = await putScope(id, scope);
    setMigration(next);
    return next;
  }, [id]);

  return { migration, error, saveEndpoints, saveScope, setMigration };
}
```
Create `web/src/wizard/WizardShell.tsx`:
```tsx
import { useEffect } from "react";
import { Outlet, useNavigate, useParams } from "react-router-dom";
import { createMigration, deleteMigration } from "../api/migrations";
import type { MigrationDto } from "../api/types";
import { useDraft } from "./useDraft";
import { Stepper } from "./Stepper";

export function NewMigrationRedirect() {
  const navigate = useNavigate();
  useEffect(() => {
    void createMigration().then((m) => navigate(`/migrations/${m.id}/from-to`, { replace: true }));
  }, [navigate]);
  return <div role="status" aria-label="Creating migration" className="h-24 animate-pulse rounded bg-surface-2" />;
}

// Batch is only possible with admin/app-scoped destination credentials (domain-wide delegation /
// application permissions). The v1 triangle proxy: pure IMAP basic (WorkMail) is single-only, while
// graph/gmail destinations are connected via app/DWD auth and can batch. The API will set this
// authoritatively from the persisted connection auth method once live (Wave F); until then this
// destination-provider heuristic drives the Scope step's Batch toggle.
export function canBatchFor(migration: MigrationDto): boolean {
  return migration.to === "graph" || migration.to === "gmail";
}

export function WizardShell() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const { migration } = useDraft(id);
  if (!migration) return <div role="status" aria-label="Loading" className="h-24 animate-pulse rounded bg-surface-2" />;
  return (
    <div className="mx-auto max-w-[760px]">
      <Stepper current={migration.wizardStep} maxReached={migration.wizardStep} migrationId={id} />
      <Outlet context={{ migration, canBatch: canBatchFor(migration) }} />
      <div className="mt-8 border-t border-border pt-4">
        <button type="button" className="text-sm text-fg-muted"
          onClick={() => { void deleteMigration(id).then(() => navigate("/")); }}>
          Reset / Start over
        </button>
      </div>
    </div>
  );
}
```
Update `web/src/app/router.tsx` to register the wizard routes (placeholders for the step bodies built in Tasks 5–10):
```tsx
import { createBrowserRouter } from "react-router-dom";
import { AppShell } from "../components/AppShell";
import { Dashboard } from "../routes/Dashboard";
import { NewMigrationRedirect, WizardShell } from "../wizard/WizardShell";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <AppShell />,
    children: [
      { index: true, element: <Dashboard /> },
      { path: "migrations/new", element: <NewMigrationRedirect /> },
      {
        path: "migrations/:id",
        element: <WizardShell />,
        children: [
          // step routes added in Tasks 5-10
        ],
      },
    ],
  },
]);
```

4. - [ ] Run them, expect PASS: `npm --prefix web run test -- --run src/wizard/Stepper.test.tsx src/wizard/useDraft.test.tsx` → `Tests` all passed.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
feat(web): wizard shell — gated stepper, draft autosave, reset

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 5: Step 1 — From & To

**Goal:** Build the trivial first step: two provider pickers (the v1 triangle — WorkMail/MS365/Google) and a plain-language summary ("You're moving mail from WorkMail to Microsoft 365"), with Next gated on both being chosen. Saving calls `setEndpoints` and advances.

**Files:**
- Create: `web/src/wizard/StepFromTo.tsx`
- Modify: `web/src/app/router.tsx`
- Test: `web/src/wizard/StepFromTo.test.tsx`

**Acceptance Criteria:**
- [ ] Two labeled groups ("From" / "To") each offer the three providers (WorkMail→`imap`, Microsoft 365→`graph`, Google→`gmail`) as selectable cards with accessible names.
- [ ] A plain-language summary updates as selections change: "You're moving mail from WorkMail to Microsoft 365." (uses `providerName`).
- [ ] The Next/Continue button is disabled until both From and To are selected.
- [ ] Clicking Continue calls `saveEndpoints(from, to)` and navigates to `connect/from`.

**Verify:** `npm --prefix web run test -- --run src/wizard/StepFromTo.test.tsx` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing test. Create `web/src/wizard/StepFromTo.test.tsx`:
```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { StepFromTo } from "./StepFromTo";

const save = vi.fn().mockResolvedValue(undefined);
const nav = vi.fn();
vi.mock("react-router-dom", () => ({ useNavigate: () => nav, useOutletContext: () => ({ migration: { id: "m1" } }) }));
vi.mock("./useDraft", () => ({ useDraft: () => ({ saveEndpoints: save, migration: { id: "m1" } }) }));

describe("StepFromTo", () => {
  it("gates Continue until both providers chosen and updates the summary", async () => {
    render(<StepFromTo />);
    const cont = screen.getByRole("button", { name: /continue/i });
    expect(cont).toBeDisabled();
    await userEvent.click(screen.getByRole("radio", { name: /from amazon workmail|from workmail/i }));
    await userEvent.click(screen.getByRole("radio", { name: /to microsoft 365/i }));
    expect(screen.getByText(/from workmail to microsoft 365/i)).toBeInTheDocument();
    expect(cont).toBeEnabled();
  });

  it("saves endpoints and advances on Continue", async () => {
    render(<StepFromTo />);
    await userEvent.click(screen.getByRole("radio", { name: /from workmail/i }));
    await userEvent.click(screen.getByRole("radio", { name: /to google/i }));
    await userEvent.click(screen.getByRole("button", { name: /continue/i }));
    expect(save).toHaveBeenCalledWith("imap", "gmail");
    expect(nav).toHaveBeenCalledWith("/migrations/m1/connect/from");
  });
});
```

2. - [ ] Run it, expect FAIL: `npm --prefix web run test -- --run src/wizard/StepFromTo.test.tsx` → fails — `StepFromTo` does not exist.

3. - [ ] Minimal implementation. Create `web/src/wizard/StepFromTo.tsx`:
```tsx
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ProviderId } from "../api/types";
import { providerName } from "../components/ProviderRoute";
import { useDraft } from "./useDraft";
import { useOutletContext } from "react-router-dom";

const PROVIDERS: { id: ProviderId; name: string }[] = [
  { id: "imap", name: "WorkMail" },
  { id: "graph", name: "Microsoft 365" },
  { id: "gmail", name: "Google" },
];

function Picker({ side, value, onChange }: { side: "From" | "To"; value: ProviderId | null; onChange: (p: ProviderId) => void }) {
  return (
    <fieldset className="space-y-2">
      <legend className="text-sm font-medium">{side}</legend>
      <div role="radiogroup" className="grid grid-cols-3 gap-2">
        {PROVIDERS.map((p) => (
          <button key={p.id} type="button" role="radio" aria-checked={value === p.id}
            aria-label={`${side} ${p.name}`} onClick={() => onChange(p.id)}
            className={`rounded-[6px] border p-3 text-sm ${value === p.id ? "border-accent ring-1 ring-accent" : "border-border"}`}>
            {p.name}
          </button>
        ))}
      </div>
    </fieldset>
  );
}

export function StepFromTo() {
  const { migration } = useOutletContext<{ migration: { id: string } }>();
  const { saveEndpoints } = useDraft(migration.id);
  const navigate = useNavigate();
  const [from, setFrom] = useState<ProviderId | null>(null);
  const [to, setTo] = useState<ProviderId | null>(null);

  async function onContinue() {
    if (!from || !to) return;
    await saveEndpoints(from, to);
    navigate(`/migrations/${migration.id}/connect/from`);
  }

  return (
    <div className="space-y-6">
      <h2 className="text-[length:var(--fs-h1)] font-semibold">Where are we moving mail?</h2>
      <div className="grid gap-6 md:grid-cols-2">
        <Picker side="From" value={from} onChange={setFrom} />
        <Picker side="To" value={to} onChange={setTo} />
      </div>
      {from && to ? (
        <p className="text-fg-muted">
          You're moving mail from {providerName(from)} to {providerName(to)}.
        </p>
      ) : null}
      <button type="button" disabled={!from || !to} onClick={() => void onContinue()}
        className="rounded-[8px] bg-accent px-4 py-2 text-accent-fg disabled:opacity-40">
        Continue
      </button>
    </div>
  );
}
```
Register the step route in `web/src/app/router.tsx` under `migrations/:id` children: `{ path: "from-to", element: <StepFromTo /> }` (import added).

4. - [ ] Run it, expect PASS: `npm --prefix web run test -- --run src/wizard/StepFromTo.test.tsx` → `Tests` all passed.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
feat(web): wizard step 1 — From & To provider pickers + summary

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 6: Step 2 — Connect (2a/2b: provider presets + WorkMail region, inline OAuth guide, mandatory test-connection gate)

**Goal:** Build the test-gated Connect sub-steps. For IMAP: provider presets with the **WorkMail region dropdown** (host template `imap.mail.{region}.awsapps.com`) + "How do I find my region?" helper + an "Advanced / custom server" escape hatch. For BYO OAuth (Graph/Gmail): an inline numbered guide with paste-back fields and an "I already have an app" toggle. All paths: a **mandatory Test connection gate** — green required to advance; failure shows the catalog-driven error with expandable technical details.

**Files:**
- Create: `web/src/wizard/StepConnect.tsx`
- Create: `web/src/wizard/connectPresets.ts`
- Create: `web/src/components/ErrorAlert.tsx`
- Modify: `web/src/app/router.tsx`
- Test: `web/src/wizard/connectPresets.test.ts`, `web/src/wizard/StepConnect.test.tsx`

**Acceptance Criteria:**
- [ ] `connectPresets.ts` exports `workmailHost(region)` returning `imap.mail.{region}.awsapps.com` for the three regions (`us-east-1`, `us-west-2`, `eu-west-1`) and `imapDefaults` (port 993, SSL on).
- [ ] For an `imap` endpoint, a region `Select` is shown and the server host preview updates with the chosen region; an "Advanced / custom server" toggle reveals manual host/port fields.
- [ ] For a `graph`/`gmail` endpoint, an inline numbered guide is shown with paste-back fields (Tenant ID / Client ID / Secret, or service-account JSON), plus an "I already have an app — just paste credentials" toggle that collapses the guide.
- [ ] A reassurance line is shown ("We read mail to migrate it. We never store contents.").
- [ ] The Continue button is **disabled until a Test connection succeeds**; a successful test shows a concrete success message ("Connected — found 14 folders, 3,201 messages"); a failed test renders an `ErrorAlert` with the plain message + a Collapsible "Technical details" containing the raw `errorCode`/`rawDetail` and any trace id.
- [ ] `connect/from` advances to `connect/to`; `connect/to` advances to `scope`.

**Verify:** `npm --prefix web run test -- --run src/wizard/connectPresets.test.ts src/wizard/StepConnect.test.tsx` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing tests. Create `web/src/wizard/connectPresets.test.ts`:
```ts
import { describe, expect, it } from "vitest";
import { imapDefaults, workmailHost, WORKMAIL_REGIONS } from "./connectPresets";

describe("connect presets", () => {
  it("builds the WorkMail host from region", () => {
    expect(workmailHost("us-east-1")).toBe("imap.mail.us-east-1.awsapps.com");
    expect(workmailHost("eu-west-1")).toBe("imap.mail.eu-west-1.awsapps.com");
  });
  it("exposes exactly the three supported regions", () => {
    expect(WORKMAIL_REGIONS).toEqual(["us-east-1", "us-west-2", "eu-west-1"]);
  });
  it("defaults to secure IMAP on 993", () => {
    expect(imapDefaults.port).toBe(993);
    expect(imapDefaults.ssl).toBe(true);
  });
});
```
Create `web/src/wizard/StepConnect.test.tsx`:
```tsx
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StepConnect } from "./StepConnect";
import * as api from "../api/migrations";

const nav = vi.fn();
vi.mock("react-router-dom", () => ({
  useNavigate: () => nav,
  useParams: () => ({ side: "from" }),
  useOutletContext: () => ({ migration: { id: "m1", from: "imap", to: "graph" } }),
}));

describe("StepConnect (IMAP from / WorkMail)", () => {
  afterEach(() => vi.restoreAllMocks());

  it("shows the region dropdown and a server host preview that tracks the region", async () => {
    render(<StepConnect />);
    expect(screen.getByLabelText(/region/i)).toBeInTheDocument();
    expect(screen.getByText(/imap\.mail\.us-east-1\.awsapps\.com/i)).toBeInTheDocument();
  });

  it("disables Continue until a test connection succeeds", async () => {
    vi.spyOn(api, "putConnection").mockResolvedValue({} as never);
    vi.spyOn(api, "testConnection").mockResolvedValue({ ok: true, folderCount: 14, messageCount: 3201 } as never);
    render(<StepConnect />);
    expect(screen.getByRole("button", { name: /continue/i })).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/username/i), "old@biz.com");
    await userEvent.type(screen.getByLabelText(/password/i), "app-pw");
    await userEvent.click(screen.getByRole("button", { name: /test connection/i }));
    expect(await screen.findByText(/found 14 folders, 3,?201 messages/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /continue/i })).toBeEnabled();
  });

  it("renders a catalog-driven error with expandable technical details on failure", async () => {
    vi.spyOn(api, "putConnection").mockResolvedValue({} as never);
    vi.spyOn(api, "testConnection").mockResolvedValue({
      ok: false, folderCount: 0, messageCount: 0,
      errorCode: "AUTH_FAILED", rawDetail: "IMAP NO [AUTHENTICATIONFAILED]",
    } as never);
    render(<StepConnect />);
    await userEvent.type(screen.getByLabelText(/username/i), "old@biz.com");
    await userEvent.type(screen.getByLabelText(/password/i), "wrong");
    await userEvent.click(screen.getByRole("button", { name: /test connection/i }));
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    await userEvent.click(screen.getByText(/technical details/i));
    expect(screen.getByText(/AUTHENTICATIONFAILED/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /continue/i })).toBeDisabled();
  });
});
```

2. - [ ] Run them, expect FAIL: `npm --prefix web run test -- --run src/wizard/connectPresets.test.ts src/wizard/StepConnect.test.tsx` → fails — modules do not exist.

3. - [ ] Minimal implementation. Create `web/src/wizard/connectPresets.ts`:
```ts
export const WORKMAIL_REGIONS = ["us-east-1", "us-west-2", "eu-west-1"] as const;
export type WorkmailRegion = (typeof WORKMAIL_REGIONS)[number];

export function workmailHost(region: WorkmailRegion): string {
  return `imap.mail.${region}.awsapps.com`;
}

export const imapDefaults = { port: 993, ssl: true } as const;
```
Create `web/src/components/ErrorAlert.tsx`:
```tsx
import { AlertTriangle } from "lucide-react";
import { useState } from "react";

export interface ErrorAlertProps {
  message: string;
  helpLabel?: string;
  helpHref?: string;
  technicalDetail?: string | null;
  traceId?: string | null;
}

export function ErrorAlert({ message, helpLabel, helpHref, technicalDetail, traceId }: ErrorAlertProps) {
  const [open, setOpen] = useState(false);
  const hasTech = Boolean(technicalDetail || traceId);
  return (
    <div role="alert" className="rounded-[6px] border border-[color:var(--throttled-line)] bg-[color:var(--throttled-bg)] p-3 text-sm">
      <div className="flex items-start gap-2">
        <AlertTriangle size={16} className="mt-0.5 text-throttled" aria-hidden />
        <div className="space-y-1">
          <p className="text-fg">{message}</p>
          {helpHref ? <a href={helpHref} className="text-accent">{helpLabel ?? "Learn more"}</a> : null}
          {hasTech ? (
            <div>
              <button type="button" onClick={() => setOpen((o) => !o)} className="text-fg-muted" aria-expanded={open}>
                ▸ Technical details
              </button>
              {open ? (
                <pre className="mono mt-1 whitespace-pre-wrap rounded bg-surface-2 p-2 text-fg-muted">
                  {technicalDetail}
                  {traceId ? `\ntrace: ${traceId}` : ""}
                </pre>
              ) : null}
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}
```
Create `web/src/wizard/StepConnect.tsx`:
```tsx
import { useMemo, useState } from "react";
import { useNavigate, useOutletContext, useParams } from "react-router-dom";
import type { AuthMethod, ConnectionSide, ConnectionTestResult, MigrationDto, ProviderId } from "../api/types";
import { putConnection, testConnection } from "../api/migrations";
import { ErrorAlert } from "../components/ErrorAlert";
import { imapDefaults, workmailHost, WORKMAIL_REGIONS, type WorkmailRegion } from "./connectPresets";

function OAuthGuide({ provider }: { provider: ProviderId }) {
  const [skip, setSkip] = useState(false);
  return (
    <div className="space-y-3">
      <button type="button" className="text-sm text-accent" onClick={() => setSkip((s) => !s)}>
        {skip ? "Show me the setup guide" : "I already have an app — just let me paste credentials"}
      </button>
      {!skip ? (
        <ol className="list-decimal space-y-1 pl-5 text-sm text-fg-muted">
          <li>Open the {provider === "graph" ? "Azure portal" : "Google Cloud console"} and create an app registration.</li>
          <li>Grant the least-privilege mail permission and admin consent.</li>
          <li>Copy the values below back into EMaigrator.</li>
        </ol>
      ) : null}
    </div>
  );
}

export function StepConnect() {
  const { side = "from" } = useParams<{ side: ConnectionSide }>();
  const { migration } = useOutletContext<{ migration: MigrationDto }>();
  const navigate = useNavigate();
  const provider = (side === "from" ? migration.from : migration.to) as ProviderId;

  const [region, setRegion] = useState<WorkmailRegion>("us-east-1");
  const [advanced, setAdvanced] = useState(false);
  const [host, setHost] = useState("");
  const [username, setUsername] = useState("");
  const [secret, setSecret] = useState("");
  const [result, setResult] = useState<ConnectionTestResult | null>(null);
  const [testing, setTesting] = useState(false);

  const effectiveHost = useMemo(
    () => (advanced ? host : provider === "imap" ? workmailHost(region) : ""),
    [advanced, host, provider, region],
  );

  const auth: AuthMethod = provider === "imap" ? "ImapBasic" : provider === "graph" ? "GraphAppOAuth" : "GmailServiceAccountDwd";

  async function onTest() {
    setTesting(true);
    setResult(null);
    try {
      await putConnection(migration.id, side, {
        auth,
        settings: { host: effectiveHost, port: String(imapDefaults.port), region, accountEmail: username },
        secret,
      });
      setResult(await testConnection(migration.id, side));
    } finally {
      setTesting(false);
    }
  }

  function onContinue() {
    navigate(side === "from" ? `/migrations/${migration.id}/connect/to` : `/migrations/${migration.id}/scope`);
  }

  return (
    <div className="space-y-5">
      <h2 className="text-[length:var(--fs-h1)] font-semibold">Connect {side === "from" ? "From" : "To"}</h2>

      {provider === "imap" ? (
        <div className="space-y-3">
          {!advanced ? (
            <label className="block text-sm">
              Region
              <select aria-label="Region" value={region} onChange={(e) => setRegion(e.target.value as WorkmailRegion)}
                className="mt-1 block h-[var(--control-h)] rounded-[6px] border border-border-strong px-2">
                {WORKMAIL_REGIONS.map((r) => <option key={r} value={r}>{r}</option>)}
              </select>
              <a href="/help/workmail-region" className="ml-2 text-accent">How do I find my region?</a>
            </label>
          ) : null}
          <p className="mono text-sm text-fg-muted">Server: {effectiveHost || "—"} Port: {imapDefaults.port} 🔒</p>
          <button type="button" className="text-sm text-accent" onClick={() => setAdvanced((a) => !a)}>
            {advanced ? "Use a provider preset" : "Advanced / custom server"}
          </button>
          {advanced ? (
            <label className="block text-sm">Server host
              <input value={host} onChange={(e) => setHost(e.target.value)}
                className="mt-1 block h-[var(--control-h)] w-full rounded-[6px] border border-border-strong px-2" />
            </label>
          ) : null}
          <label className="block text-sm">Username
            <input aria-label="Username" value={username} onChange={(e) => setUsername(e.target.value)}
              className="mt-1 block h-[var(--control-h)] w-full rounded-[6px] border border-border-strong px-2" />
          </label>
          <label className="block text-sm">Password
            <input aria-label="Password" type="password" value={secret} onChange={(e) => setSecret(e.target.value)}
              className="mt-1 block h-[var(--control-h)] w-full rounded-[6px] border border-border-strong px-2" />
          </label>
        </div>
      ) : (
        <OAuthGuide provider={provider} />
      )}

      <p className="text-sm text-fg-muted">🔒 We read mail to migrate it. We never store contents.</p>

      <button type="button" onClick={() => void onTest()} disabled={testing}
        className="rounded-[8px] border border-border px-4 py-2">
        {testing ? "Testing…" : "Test connection"}
      </button>

      {result?.ok ? (
        <p role="status" className="text-success">
          Connected — found {result.folderCount} folders, {result.messageCount.toLocaleString()} messages.
        </p>
      ) : null}
      {result && !result.ok ? (
        <ErrorAlert
          message="We couldn't connect. WorkMail needs an app password, not your normal password."
          helpLabel="How to create one" helpHref="/help/workmail-app-password"
          technicalDetail={`${result.errorCode ?? ""} ${result.rawDetail ?? ""}`.trim()}
        />
      ) : null}

      <button type="button" disabled={!result?.ok} onClick={onContinue}
        className="block rounded-[8px] bg-accent px-4 py-2 text-accent-fg disabled:opacity-40">
        Continue
      </button>
    </div>
  );
}
```
Register routes in `web/src/app/router.tsx`: `{ path: "connect/:side", element: <StepConnect /> }`.

4. - [ ] Run them, expect PASS: `npm --prefix web run test -- --run src/wizard/connectPresets.test.ts src/wizard/StepConnect.test.tsx` → `Tests` all passed.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
feat(web): wizard step 2 — Connect (presets + WorkMail region, OAuth guide, test gate)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 7: Step 3 — Scope (single⇄batch, CSV import + in-app builder, advanced collapsed)

**Goal:** Build the Scope step adapting to the connection type: single (pre-determined, Batch disabled with explanation) vs admin/app (Single⇄Batch live). Batch supports **CSV import as primary** (`source_mailbox,destination_mailbox`) with an **in-app pair builder fallback** and per-row validation; folder/date filters live under a collapsed "Advanced".

**Files:**
- Create: `web/src/wizard/StepScope.tsx`
- Create: `web/src/wizard/csv.ts`
- Modify: `web/src/app/router.tsx`
- Test: `web/src/wizard/csv.test.ts`, `web/src/wizard/StepScope.test.tsx`

**Acceptance Criteria:**
- [ ] `parsePairsCsv(text)` parses a `source_mailbox,destination_mailbox` CSV (with or without a header row) into `MailboxPairDto[]`, trims whitespace, skips blank lines, and reports row-level errors for malformed rows.
- [ ] When connected with single-mailbox creds (`canBatch=false`), the Batch toggle is rendered **disabled** with the explanation copy ("To migrate multiple mailboxes, reconnect using admin access.").
- [ ] When `canBatch=true`, a Single⇄Batch toggle is live; Batch shows a CSV file input (primary) and an in-app "Add pair" builder (fallback); imported/added pairs render in a table with per-row valid/invalid status.
- [ ] An "Advanced" disclosure (collapsed by default) reveals folder include/exclude and date-range inputs.
- [ ] Continue calls `saveScope(scopeRequest)` and navigates to `review`.

**Verify:** `npm --prefix web run test -- --run src/wizard/csv.test.ts src/wizard/StepScope.test.tsx` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing tests. Create `web/src/wizard/csv.test.ts`:
```ts
import { describe, expect, it } from "vitest";
import { parsePairsCsv } from "./csv";

describe("parsePairsCsv", () => {
  it("parses pairs with a header row", () => {
    const { pairs, errors } = parsePairsCsv("source_mailbox,destination_mailbox\na@x.com,a@y.com\nb@x.com,b@y.com");
    expect(pairs).toEqual([
      { sourceMailbox: "a@x.com", destMailbox: "a@y.com" },
      { sourceMailbox: "b@x.com", destMailbox: "b@y.com" },
    ]);
    expect(errors).toHaveLength(0);
  });
  it("parses without a header and trims", () => {
    const { pairs } = parsePairsCsv(" a@x.com , a@y.com ");
    expect(pairs[0]).toEqual({ sourceMailbox: "a@x.com", destMailbox: "a@y.com" });
  });
  it("reports malformed rows", () => {
    const { errors } = parsePairsCsv("a@x.com\nb@x.com,b@y.com");
    expect(errors[0]).toMatch(/line 1/i);
  });
});
```
Create `web/src/wizard/StepScope.test.tsx`:
```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { StepScope } from "./StepScope";

const save = vi.fn().mockResolvedValue(undefined);
const nav = vi.fn();
let ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: true };
vi.mock("react-router-dom", () => ({ useNavigate: () => nav, useOutletContext: () => ctx }));
vi.mock("./useDraft", () => ({ useDraft: () => ({ saveScope: save, migration: ctx.migration }) }));

describe("StepScope", () => {
  it("disables Batch with an explanation when single-only creds", () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: false };
    render(<StepScope />);
    const batch = screen.getByRole("button", { name: /batch/i });
    expect(batch).toBeDisabled();
    expect(screen.getByText(/reconnect using admin access/i)).toBeInTheDocument();
  });

  it("imports a CSV into the pair table in batch mode", async () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: true }, canBatch: true };
    render(<StepScope />);
    await userEvent.click(screen.getByRole("button", { name: /batch/i }));
    const file = new File(["a@x.com,a@y.com\nb@x.com,b@y.com"], "pairs.csv", { type: "text/csv" });
    await userEvent.upload(screen.getByLabelText(/import csv/i), file);
    expect(await screen.findByText("a@x.com")).toBeInTheDocument();
    expect(screen.getByText("b@y.com")).toBeInTheDocument();
  });

  it("keeps Advanced collapsed by default", () => {
    ctx = { migration: { id: "m1", from: "imap", to: "graph", isBatch: false }, canBatch: true };
    render(<StepScope />);
    expect(screen.queryByLabelText(/include folders/i)).not.toBeInTheDocument();
  });
});
```

2. - [ ] Run them, expect FAIL: `npm --prefix web run test -- --run src/wizard/csv.test.ts src/wizard/StepScope.test.tsx` → fails — modules do not exist.

3. - [ ] Minimal implementation. Create `web/src/wizard/csv.ts`:
```ts
import type { MailboxPairDto } from "../api/types";

export function parsePairsCsv(text: string): { pairs: MailboxPairDto[]; errors: string[] } {
  const pairs: MailboxPairDto[] = [];
  const errors: string[] = [];
  const lines = text.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  lines.forEach((line, i) => {
    if (i === 0 && /source_mailbox/i.test(line)) return; // header
    const cols = line.split(",").map((c) => c.trim());
    if (cols.length !== 2 || !cols[0] || !cols[1]) {
      errors.push(`Line ${i + 1}: expected "source,destination"`);
      return;
    }
    pairs.push({ sourceMailbox: cols[0], destMailbox: cols[1] });
  });
  return { pairs, errors };
}
```
Create `web/src/wizard/StepScope.tsx`:
```tsx
import { useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import type { MailboxPairDto, MigrationDto, ScopeRequest } from "../api/types";
import { useDraft } from "./useDraft";
import { parsePairsCsv } from "./csv";

interface ScopeCtx { migration: MigrationDto; canBatch: boolean; }

export function StepScope() {
  const { migration, canBatch } = useOutletContext<ScopeCtx>();
  const { saveScope } = useDraft(migration.id);
  const navigate = useNavigate();
  const [isBatch, setIsBatch] = useState(false);
  const [pairs, setPairs] = useState<MailboxPairDto[]>([]);
  const [csvErrors, setCsvErrors] = useState<string[]>([]);
  const [showAdvanced, setShowAdvanced] = useState(false);

  async function onCsv(file: File) {
    const text = await file.text();
    const { pairs: p, errors } = parsePairsCsv(text);
    setPairs(p);
    setCsvErrors(errors);
  }

  async function onContinue() {
    const scope: ScopeRequest = { isBatch, pairs };
    await saveScope(scope);
    navigate(`/migrations/${migration.id}/review`);
  }

  return (
    <div className="space-y-5">
      <h2 className="text-[length:var(--fs-h1)] font-semibold">What should we migrate?</h2>

      <div role="group" aria-label="Scope mode" className="flex gap-2">
        <button type="button" aria-pressed={!isBatch} onClick={() => setIsBatch(false)}
          className={`rounded-[6px] border px-3 py-1.5 ${!isBatch ? "border-accent" : "border-border"}`}>Single</button>
        <button type="button" aria-pressed={isBatch} disabled={!canBatch} onClick={() => setIsBatch(true)}
          className={`rounded-[6px] border px-3 py-1.5 disabled:opacity-40 ${isBatch ? "border-accent" : "border-border"}`}>Batch</button>
      </div>
      {!canBatch ? (
        <p className="text-sm text-fg-muted">To migrate multiple mailboxes, reconnect using admin access.</p>
      ) : null}

      {isBatch ? (
        <div className="space-y-3">
          <label className="block text-sm">Import CSV (source_mailbox, destination_mailbox)
            <input aria-label="Import CSV" type="file" accept=".csv,text/csv"
              onChange={(e) => e.target.files?.[0] && void onCsv(e.target.files[0])} className="mt-1 block" />
          </label>
          <button type="button" className="text-sm text-accent"
            onClick={() => setPairs((p) => [...p, { sourceMailbox: "", destMailbox: "" }])}>+ Add pair</button>
          {csvErrors.length ? <ul className="text-sm text-error">{csvErrors.map((e) => <li key={e}>{e}</li>)}</ul> : null}
          {pairs.length ? (
            <table className="w-full text-sm"><thead><tr className="text-left text-fg-muted"><th>From</th><th>To</th><th>Status</th></tr></thead>
              <tbody>{pairs.map((p, i) => (
                <tr key={i} className="border-t border-border">
                  <td className="mono">{p.sourceMailbox}</td>
                  <td className="mono">{p.destMailbox}</td>
                  <td>{p.sourceMailbox && p.destMailbox ? "✓ valid" : "⚠ incomplete"}</td>
                </tr>
              ))}</tbody>
            </table>
          ) : null}
        </div>
      ) : (
        <p className="text-fg-muted">Migrating one mailbox. Confirm and continue.</p>
      )}

      <button type="button" className="text-sm text-fg-muted" aria-expanded={showAdvanced}
        onClick={() => setShowAdvanced((s) => !s)}>▸ Advanced</button>
      {showAdvanced ? (
        <div className="space-y-2">
          <label className="block text-sm">Include folders<input aria-label="Include folders" className="mt-1 block w-full rounded-[6px] border border-border-strong px-2 py-1" /></label>
          <label className="block text-sm">Exclude folders<input aria-label="Exclude folders" className="mt-1 block w-full rounded-[6px] border border-border-strong px-2 py-1" /></label>
        </div>
      ) : null}

      <button type="button" onClick={() => void onContinue()} className="rounded-[8px] bg-accent px-4 py-2 text-accent-fg">Continue</button>
    </div>
  );
}
```
Register route in `web/src/app/router.tsx`: `{ path: "scope", element: <StepScope /> }`. The `WizardShell` `Outlet context` already exposes `canBatch` via `canBatchFor(migration)` (added in Task 4) — `StepScope` reads it straight from `useOutletContext<ScopeCtx>()`.

4. - [ ] Run them, expect PASS: `npm --prefix web run test -- --run src/wizard/csv.test.ts src/wizard/StepScope.test.tsx` → `Tests` all passed.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
feat(web): wizard step 3 — Scope (single/batch, CSV import + builder, advanced)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 8: Step 4 — Review & plan (adaptive layout, bulk resolution dropdowns, usage block)

**Goal:** Build Review & plan: a scanning state, then an **adaptive layout** — a clean "Ready to migrate" card when there are no issues, or an issues panel (grouped by type, each with a **bulk resolution dropdown**) when there are. Show the estimate (with conservative ETA) and a usage block that **blocks Start** when quota/fair-use cap is exceeded. Approve calls `approve(resolutions)`.

**Files:**
- Create: `web/src/wizard/StepReview.tsx`
- Create: `web/src/wizard/format.ts`
- Modify: `web/src/app/router.tsx`
- Test: `web/src/wizard/format.test.ts`, `web/src/wizard/StepReview.test.tsx`

**Acceptance Criteria:**
- [ ] On mount the step calls `startPreflight(id)` (`POST …/preflight` → 202 per CONTRACTS §6) then polls `getPreflight(id)` (`GET …/preflight`); the POST kicking off the scan is asserted by a test spy.
- [ ] While `getPreflight` reports `scanning: true`, a scanning/skeleton state is shown ("Reviewing your mailboxes…"), polled until `scanning: false`.
- [ ] When `issues.length === 0`, the clean "Ready to migrate" card shows mailbox/folder/message/size counts (mono) + estimated duration + a single "Start migration" button.
- [ ] When issues exist, each issue group renders its description, affected count, a **bulk resolution `Select`** defaulting to `recommendedAction` with the issue's `options`, and a "[details]" disclosure; Blockers are visually distinct from warnings.
- [ ] `formatBytes`/`formatDuration` produce the displayed mono strings (e.g. `250 MB`, `~12 min`).
- [ ] When `usage` indicates over quota or over the GB cap, a usage block renders the blocking message and the Start/Approve button is **disabled**.
- [ ] Approve sends a `resolutions` map (`issueType → action`) reflecting the chosen dropdown values and navigates to `run`.

**Verify:** `npm --prefix web run test -- --run src/wizard/format.test.ts src/wizard/StepReview.test.tsx` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing tests. Create `web/src/wizard/format.test.ts`:
```ts
import { describe, expect, it } from "vitest";
import { formatBytes, formatDuration } from "./format";

describe("format", () => {
  it("formats bytes to human units", () => {
    expect(formatBytes(262144000)).toBe("250 MB");
    expect(formatBytes(1073741824)).toBe("1.0 GB");
  });
  it("formats duration conservatively in minutes/hours", () => {
    expect(formatDuration(720)).toBe("~12 min");
    expect(formatDuration(7800)).toBe("~2h 10m");
  });
});
```
Create `web/src/wizard/StepReview.test.tsx`:
```tsx
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StepReview } from "./StepReview";
import * as api from "../api/migrations";

const nav = vi.fn();
vi.mock("react-router-dom", () => ({ useNavigate: () => nav, useOutletContext: () => ({ migration: { id: "m1" } }) }));

const cleanPlan = { scanning: false, issues: [], estimate: { mailboxCount: 1, folderCount: 14, messageCount: 3201, totalBytes: 262144000, estimatedDurationSeconds: 720 }, usage: null };
const issuePlan = {
  scanning: false,
  issues: [{ issueType: "FolderDepth", affectedPaths: ["/a/b/c/d/e"], recommendedAction: "FlattenFolder", options: ["FlattenFolder", "RenameFolder", "SkipMessage"], severity: "Warning", description: "12 folders exceed Outlook's depth" }],
  estimate: { mailboxCount: 218, folderCount: 900, messageCount: 1200000, totalBytes: 0, estimatedDurationSeconds: 7800 },
  usage: { used: 188, quota: 200, overCapMailboxes: 2, capGb: 50 },
};

describe("StepReview", () => {
  beforeEach(() => vi.spyOn(api, "startPreflight").mockResolvedValue(undefined as never));
  afterEach(() => vi.restoreAllMocks());

  it("starts the preflight scan then polls for the plan", async () => {
    vi.spyOn(api, "getPreflight").mockResolvedValue(cleanPlan as never);
    render(<StepReview />);
    await screen.findByText(/ready to migrate/i);
    expect(api.startPreflight).toHaveBeenCalledWith("m1");
    expect(api.getPreflight).toHaveBeenCalledWith("m1");
  });

  it("shows a clean Ready card when there are no issues", async () => {
    vi.spyOn(api, "getPreflight").mockResolvedValue(cleanPlan as never);
    render(<StepReview />);
    expect(await screen.findByText(/ready to migrate/i)).toBeInTheDocument();
    expect(screen.getByText(/~12 min/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /start migration/i })).toBeEnabled();
  });

  it("shows bulk resolution dropdowns and blocks Start when over the cap", async () => {
    vi.spyOn(api, "getPreflight").mockResolvedValue(issuePlan as never);
    render(<StepReview />);
    expect(await screen.findByText(/exceed outlook's depth/i)).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: /resolution for folderdepth/i })).toBeInTheDocument();
    expect(screen.getByText(/exceed the 50 GB/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /approve plan & start/i })).toBeDisabled();
  });

  it("approves with the chosen resolutions when within quota", async () => {
    // quota must exceed used + estimate.mailboxCount (10 + 218 = 228) and overCapMailboxes must be 0,
    // otherwise the Approve button is blocked and the click is a no-op.
    vi.spyOn(api, "getPreflight").mockResolvedValue({ ...issuePlan, usage: { used: 10, quota: 500, overCapMailboxes: 0, capGb: 50 } } as never);
    const approve = vi.spyOn(api, "approve").mockResolvedValue({} as never);
    render(<StepReview />);
    await screen.findByText(/exceed outlook's depth/i);
    await userEvent.click(screen.getByRole("button", { name: /approve plan & start/i }));
    await waitFor(() => expect(approve).toHaveBeenCalledWith("m1", { resolutions: { FolderDepth: "FlattenFolder" } }));
    expect(nav).toHaveBeenCalledWith("/migrations/m1/run");
  });
});
```

2. - [ ] Run them, expect FAIL: `npm --prefix web run test -- --run src/wizard/format.test.ts src/wizard/StepReview.test.tsx` → fails — modules do not exist.

3. - [ ] Minimal implementation. Create `web/src/wizard/format.ts`:
```ts
export function formatBytes(bytes: number): string {
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(1)} GB`;
  if (bytes >= 1024 ** 2) return `${Math.round(bytes / 1024 ** 2)} MB`;
  if (bytes >= 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${bytes} B`;
}

export function formatDuration(seconds: number): string {
  const mins = Math.round(seconds / 60);
  if (mins < 60) return `~${mins} min`;
  const h = Math.floor(mins / 60);
  const m = mins % 60;
  return `~${h}h ${m}m`;
}
```
Create `web/src/wizard/StepReview.tsx`:
```tsx
import { useEffect, useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import type { MigrationDto, PreflightPlanDto, RemediationAction } from "../api/types";
import { approve, getPreflight, startPreflight } from "../api/migrations";
import { formatBytes, formatDuration } from "./format";

const ACTION_LABEL: Record<RemediationAction, string> = {
  None: "Keep as-is", RetryWithBackoff: "Retry", FlattenFolder: "Flatten",
  SanitizeFolderName: "Sanitize", RenameFolder: "Rename", MergeFolder: "Merge", SkipMessage: "Skip & log",
};

export function StepReview() {
  const { migration } = useOutletContext<{ migration: MigrationDto }>();
  const navigate = useNavigate();
  const [plan, setPlan] = useState<PreflightPlanDto | null>(null);
  const [resolutions, setResolutions] = useState<Record<string, RemediationAction>>({});

  useEffect(() => {
    let active = true;
    const poll = async () => {
      const p = await getPreflight(migration.id);
      if (!active) return;
      setPlan(p);
      if (p.scanning) setTimeout(() => void poll(), 1500);
      else setResolutions(Object.fromEntries(p.issues.map((i) => [i.issueType, i.recommendedAction])));
    };
    // CONTRACTS §6: POST /preflight kicks off the async scan (202), then poll GET /preflight.
    // startPreflight is idempotent server-side; if the scan was already started (e.g. on a
    // resumed draft) the 202/2xx is harmless and polling picks up the in-progress plan.
    void startPreflight(migration.id).catch(() => {}).finally(() => { if (active) void poll(); });
    return () => { active = false; };
  }, [migration.id]);

  if (!plan || plan.scanning) {
    return <div role="status" aria-label="Reviewing your mailboxes">Reviewing your mailboxes…</div>;
  }

  const overQuota = plan.usage ? plan.usage.used + plan.estimate.mailboxCount > plan.usage.quota : false;
  const overCap = plan.usage ? plan.usage.overCapMailboxes > 0 : false;
  const blocked = overQuota || overCap || plan.issues.some((i) => i.severity === "Blocker");
  const e = plan.estimate;

  async function onApprove() {
    await approve(migration.id, { resolutions });
    navigate(`/migrations/${migration.id}/run`);
  }

  if (plan.issues.length === 0 && !plan.usage) {
    return (
      <div className="space-y-3 rounded-[6px] border border-border p-[var(--card-pad)]">
        <h2 className="flex items-center gap-2 text-[length:var(--fs-h1)] font-semibold">✓ Ready to migrate</h2>
        <p className="mono text-sm">{e.mailboxCount} mailbox · {e.folderCount} folders</p>
        <p className="mono text-sm">{e.messageCount.toLocaleString()} messages · {formatBytes(e.totalBytes)}</p>
        <p className="mono text-sm">Estimated: {formatDuration(e.estimatedDurationSeconds)}</p>
        <button type="button" onClick={() => void onApprove()} className="rounded-[8px] bg-accent px-4 py-2 text-accent-fg">Start migration</button>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <h2 className="text-[length:var(--fs-h1)] font-semibold">{plan.issues.length} things to resolve before we start</h2>
      <ul className="space-y-3">
        {plan.issues.map((i) => (
          <li key={i.issueType} className={`rounded-[6px] border p-3 ${i.severity === "Blocker" ? "border-error" : "border-border"}`}>
            <p>{i.description} {i.severity === "Blocker" ? <span className="text-error">(must fix)</span> : null}</p>
            <label className="mt-2 block text-sm">Resolution
              <select aria-label={`Resolution for ${i.issueType}`} value={resolutions[i.issueType] ?? i.recommendedAction}
                onChange={(ev) => setResolutions((r) => ({ ...r, [i.issueType]: ev.target.value as RemediationAction }))}
                className="ml-2 h-[var(--control-h)] rounded-[6px] border border-border-strong px-2">
                {i.options.map((o) => <option key={o} value={o}>{ACTION_LABEL[o]}</option>)}
              </select>
            </label>
          </li>
        ))}
      </ul>
      <p className="mono text-sm">Summary: {e.mailboxCount} mailboxes · {e.messageCount.toLocaleString()} msgs · {formatDuration(e.estimatedDurationSeconds)}</p>
      {plan.usage ? (
        <p className={overQuota || overCap ? "text-error" : "text-fg-muted"}>
          Needs {e.mailboxCount} mailboxes (you have {plan.usage.quota - plan.usage.used} left)
          {overCap ? ` · ${plan.usage.overCapMailboxes} mailboxes exceed the ${plan.usage.capGb} GB cap → upgrade to proceed` : ""}
        </p>
      ) : null}
      <button type="button" disabled={blocked} onClick={() => void onApprove()}
        className="rounded-[8px] bg-accent px-4 py-2 text-accent-fg disabled:opacity-40">
        Approve plan &amp; start
      </button>
    </div>
  );
}
```
Register route: `{ path: "review", element: <StepReview /> }`.

4. - [ ] Run them, expect PASS: `npm --prefix web run test -- --run src/wizard/format.test.ts src/wizard/StepReview.test.tsx` → `Tests` all passed.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
feat(web): wizard step 4 — Review & plan (adaptive layout, bulk resolutions, usage)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 9: Step 5 — Run (live progress, throttling chip, pause/resume, batch density toggle)

**Goal:** Build the Run step driven by the `useMigrationStream` hook: a message-level progress bar, current-folder label, throughput, and buffered ETA; a **throttling chip** when rate-limited; Pause/Resume/Cancel controls; and a batch density toggle (light default ⇄ dense per-mailbox list) with a "Safe to close — runs in the background" reassurance.

**Files:**
- Create: `web/src/wizard/StepRun.tsx`
- Create: `web/src/components/ProgressBar.tsx`
- Modify: `web/src/app/router.tsx`
- Test: `web/src/wizard/StepRun.test.tsx`

**Acceptance Criteria:**
- [ ] Live progress renders from the stream: percentage, `migrated / total` (mono, tabular-nums), current-folder label, and `msg/min`.
- [ ] When `progress.throttled === true` (a dedicated flag — throttling is **not** a `JobStatus` value, per CONTRACTS §4), a `StatusChip status="throttled"` with "Slowing to respect limits" renders — never a silently stalled bar.
- [ ] Pause calls `pause(id)`, Resume calls `resume(id)`, Cancel calls `cancel(id)`.
- [ ] A "Safe to close — runs in the background" line is always visible.
- [ ] A batch density toggle switches between a light single summary and a dense per-mailbox view; the per-mailbox list shows status chips (done/running/throttled/failed/needs-decision/queued).
- [ ] When `connectionState === "reconnecting"`, a small "Reconnecting…" indicator is shown (never presented as failure).

**Verify:** `npm --prefix web run test -- --run src/wizard/StepRun.test.tsx` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing test. Create `web/src/wizard/StepRun.test.tsx`:
```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StepRun } from "./StepRun";
import * as api from "../api/migrations";
import * as stream from "../api/useMigrationStream";

vi.mock("react-router-dom", () => ({ useOutletContext: () => ({ migration: { id: "m1", isBatch: false } }) }));

function mockStream(over: Partial<stream.MigrationStream>) {
  vi.spyOn(stream, "useMigrationStream").mockReturnValue({
    connectionState: "connected",
    progress: { migratedCount: 2310, total: 3201, currentFolder: "/Archive/2023", msgPerMin: 412, status: "Running" },
    status: "Running",
    needsDecision: [],
    ...over,
  });
}

describe("StepRun", () => {
  afterEach(() => vi.restoreAllMocks());

  it("renders live progress with current folder and throughput", () => {
    mockStream({});
    render(<StepRun />);
    expect(screen.getByText(/2,310 \/ 3,201/)).toBeInTheDocument();
    expect(screen.getByText(/\/Archive\/2023/)).toBeInTheDocument();
    expect(screen.getByText(/412/)).toBeInTheDocument();
    expect(screen.getByText(/safe to close/i)).toBeInTheDocument();
  });

  it("shows a throttling chip when the throttled flag is set (not via a bogus status)", () => {
    vi.spyOn(stream, "useMigrationStream").mockReturnValue({
      connectionState: "connected", status: "Running", needsDecision: [],
      progress: { migratedCount: 5, total: 10, currentFolder: null, msgPerMin: 0, status: "Running", throttled: true },
    });
    render(<StepRun />);
    expect(screen.getByText(/slowing to respect limits/i)).toBeInTheDocument();
  });

  it("shows a reconnecting indicator without looking like failure", () => {
    mockStream({ connectionState: "reconnecting" });
    render(<StepRun />);
    expect(screen.getByText(/reconnecting/i)).toBeInTheDocument();
  });

  it("wires pause/resume/cancel controls", async () => {
    mockStream({});
    const pause = vi.spyOn(api, "pause").mockResolvedValue({} as never);
    render(<StepRun />);
    await userEvent.click(screen.getByRole("button", { name: /pause/i }));
    expect(pause).toHaveBeenCalledWith("m1");
  });
});
```

2. - [ ] Run it, expect FAIL: `npm --prefix web run test -- --run src/wizard/StepRun.test.tsx` → fails — modules do not exist.

3. - [ ] Minimal implementation. Create `web/src/components/ProgressBar.tsx`:
```tsx
export function ProgressBar({ value, label }: { value: number; label?: string }) {
  return (
    <div role="progressbar" aria-valuenow={value} aria-valuemin={0} aria-valuemax={100} aria-label={label ?? "Progress"}
      className="h-2 w-full overflow-hidden rounded-full bg-surface-2">
      <div className="h-full bg-accent transition-[width] duration-200" style={{ width: `${value}%` }} />
    </div>
  );
}
```
Create `web/src/wizard/StepRun.tsx`:
```tsx
import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import type { MigrationDto } from "../api/types";
import { cancel, pause, resume } from "../api/migrations";
import { useMigrationStream } from "../api/useMigrationStream";
import { ProgressBar } from "../components/ProgressBar";
import { StatusChip } from "../components/StatusChip";

export function StepRun() {
  const { migration } = useOutletContext<{ migration: MigrationDto }>();
  const { progress, connectionState } = useMigrationStream(migration.id);
  const [dense, setDense] = useState(false);

  const pct = progress && progress.total > 0 ? Math.round((progress.migratedCount / progress.total) * 100) : 0;
  // Throttling is a dedicated flag, not a JobStatus value (see MigrationProgressDto).
  const throttled = progress?.throttled === true;

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-[length:var(--fs-h1)] font-semibold">Migrating</h2>
        {connectionState === "reconnecting" ? (
          <span role="status" className="text-sm text-throttled">Reconnecting…</span>
        ) : null}
      </div>

      <ProgressBar value={pct} label="Migration progress" />
      <p className="mono text-sm">
        {(progress?.migratedCount ?? 0).toLocaleString()} / {(progress?.total ?? 0).toLocaleString()}
      </p>
      {progress?.currentFolder ? <p className="text-sm text-fg-muted">Current: {progress.currentFolder}</p> : null}
      <p className="mono text-sm">{progress?.msgPerMin ?? 0} msg/min</p>
      {throttled ? <StatusChip status="throttled" /> : null}

      <div className="flex gap-2">
        <button type="button" onClick={() => void pause(migration.id)} className="rounded-[8px] border border-border px-3 py-1.5">⏸ Pause</button>
        <button type="button" onClick={() => void resume(migration.id)} className="rounded-[8px] border border-border px-3 py-1.5">Resume</button>
        <button type="button" onClick={() => void cancel(migration.id)} className="rounded-[8px] border border-border px-3 py-1.5">✕ Cancel</button>
      </div>

      {migration.isBatch ? (
        <button type="button" className="text-sm text-accent" aria-pressed={dense} onClick={() => setDense((d) => !d)}>
          {dense ? "Simple view" : "Detailed view"}
        </button>
      ) : null}

      <p className="text-sm text-fg-muted">🔒 Safe to close — runs in the background.</p>
    </div>
  );
}
```
Register route: `{ path: "run", element: <StepRun /> }`.

4. - [ ] Run it, expect PASS: `npm --prefix web run test -- --run src/wizard/StepRun.test.tsx` → `Tests` all passed.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
feat(web): wizard step 5 — Run (live progress, throttling chip, pause/resume)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 10: Step 6 — Results (4 outcomes, needs-decision resolve+rerun, audit table, CSV/PDF export)

**Goal:** Build Results: four outcome headers (Success/Partial/Failed/Cancelled), a completeness summary with source↔destination reconciliation, a needs-decision queue with "Re-run unfinished items", a searchable/failures-filtered audit table (subject/date/folder/status), CSV/PDF export, and the 30-day purge transparency line.

**Files:**
- Create: `web/src/routes/Results.tsx`
- Create: `web/src/routes/AuditTable.tsx`
- Modify: `web/src/app/router.tsx`
- Test: `web/src/routes/Results.test.tsx`, `web/src/routes/AuditTable.test.tsx`

**Acceptance Criteria:**
- [ ] The header reflects the outcome: `Completed` → "Migration complete", `Partial` → "Migration complete — Partial", `Failed` → "Migration failed", `Cancelled` → "Migration cancelled".
- [ ] A summary shows migrated/skipped/needs-decision counts and a reconciliation line ("3,201 in source, 3,201 in destination ✓") from `sourceCount`/`destCount`.
- [ ] The needs-decision queue lists each item with a Resolve control; a "Re-run unfinished items" button calls `rerun(id)` and is labeled idempotent/free.
- [ ] The audit table renders rows from `getAudit`, supports a search box and a failures-only filter (re-queries with `failuresOnly`), and renders subject/date/folder/status; the subject cell renders text content **escaped** (no HTML injection).
- [ ] An Export menu provides CSV and PDF links pointing at `reportUrl(id, "csv"|"pdf")`.
- [ ] A "This log auto-deletes in 30 days" line + a "Delete now" action are shown.

**Verify:** `npm --prefix web run test -- --run src/routes/Results.test.tsx src/routes/AuditTable.test.tsx` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing tests. Create `web/src/routes/Results.test.tsx`:
```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Results } from "./Results";
import * as api from "../api/migrations";

vi.mock("react-router-dom", () => ({ useParams: () => ({ id: "m1" }) }));

const results = {
  status: "Partial", migratedCount: 3180, skippedCount: 18,
  failedCount: 0, sourceCount: 3201, destCount: 3201, durationSeconds: 702,
  logDeletesAt: "2026-07-01T00:00:00Z",
  needsDecision: [{ migrationId: "m1", issueType: "FolderCollision", detail: "/Projects collision", options: ["RenameFolder", "MergeFolder"] }],
};

describe("Results", () => {
  afterEach(() => vi.restoreAllMocks());

  it("shows the Partial outcome header and reconciliation", async () => {
    vi.spyOn(api, "getResults").mockResolvedValue(results as never);
    vi.spyOn(api, "getAudit").mockResolvedValue([] as never);
    render(<Results />);
    expect(await screen.findByText(/migration complete — partial/i)).toBeInTheDocument();
    expect(screen.getByText(/3,201 in source, 3,201 in destination/i)).toBeInTheDocument();
  });

  it("re-runs unfinished items", async () => {
    vi.spyOn(api, "getResults").mockResolvedValue(results as never);
    vi.spyOn(api, "getAudit").mockResolvedValue([] as never);
    const rerun = vi.spyOn(api, "rerun").mockResolvedValue({} as never);
    render(<Results />);
    await screen.findByText(/migration complete — partial/i);
    await userEvent.click(screen.getByRole("button", { name: /re-run unfinished/i }));
    expect(rerun).toHaveBeenCalledWith("m1");
  });

  it("offers CSV and PDF export links", async () => {
    vi.spyOn(api, "getResults").mockResolvedValue(results as never);
    vi.spyOn(api, "getAudit").mockResolvedValue([] as never);
    render(<Results />);
    await screen.findByText(/migration complete — partial/i);
    expect(screen.getByRole("link", { name: /csv/i })).toHaveAttribute("href", "/api/v1/migrations/m1/report?format=csv");
    expect(screen.getByRole("link", { name: /pdf/i })).toHaveAttribute("href", "/api/v1/migrations/m1/report?format=pdf");
  });
});
```
Create `web/src/routes/AuditTable.test.tsx`:
```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AuditTable } from "./AuditTable";

const entries = [
  { subject: "Re: invoice 4521", messageDate: "2024-03-12T00:00:00Z", sourceFolder: "/Archive", destFolder: "/Archive", status: "migrated" as const },
  { subject: "<script>alert('xss')</script>", messageDate: "2024-01-08T00:00:00Z", sourceFolder: "/Sent", destFolder: "/Sent", status: "skipped" as const },
];

describe("AuditTable", () => {
  it("renders subjects as escaped text, never as HTML", () => {
    const { container } = render(<AuditTable entries={entries} />);
    expect(screen.getByText("Re: invoice 4521")).toBeInTheDocument();
    // The script payload appears as literal text, and no <script> element is created.
    expect(screen.getByText("<script>alert('xss')</script>")).toBeInTheDocument();
    expect(container.querySelector("script")).toBeNull();
  });
});
```

2. - [ ] Run them, expect FAIL: `npm --prefix web run test -- --run src/routes/Results.test.tsx src/routes/AuditTable.test.tsx` → fails — modules do not exist.

3. - [ ] Minimal implementation. Create `web/src/routes/AuditTable.tsx`:
```tsx
import type { AuditEntryDto } from "../api/types";

const STATUS_LABEL: Record<AuditEntryDto["status"], string> = {
  migrated: "✓ migrated", skipped: "⤫ skipped", failed: "✕ failed",
};

export function AuditTable({ entries }: { entries: AuditEntryDto[] }) {
  if (entries.length === 0) {
    return <p className="text-sm text-fg-muted">No audit entries yet.</p>;
  }
  return (
    <table className="w-full text-sm">
      <thead>
        <tr className="text-left text-fg-muted"><th>Subject</th><th>Date</th><th>Folder</th><th>Status</th></tr>
      </thead>
      <tbody>
        {entries.map((e, i) => (
          <tr key={i} className="border-t border-border">
            {/* React escapes text children by default — no dangerouslySetInnerHTML anywhere */}
            <td>{e.subject ?? "(hidden)"}</td>
            <td className="mono">{e.messageDate.slice(0, 10)}</td>
            <td className="mono">{e.sourceFolder}</td>
            <td>{STATUS_LABEL[e.status]}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
```
Create `web/src/routes/Results.tsx`:
```tsx
import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import type { AuditEntryDto, ResultsDto } from "../api/types";
import { getAudit, getResults, rerun, reportUrl } from "../api/migrations";
import { AuditTable } from "./AuditTable";

const HEADER: Record<string, string> = {
  Completed: "Migration complete", Partial: "Migration complete — Partial",
  Failed: "Migration failed", Cancelled: "Migration cancelled",
};

export function Results() {
  const { id = "" } = useParams();
  const [data, setData] = useState<ResultsDto | null>(null);
  const [audit, setAudit] = useState<AuditEntryDto[]>([]);
  const [failuresOnly, setFailuresOnly] = useState(false);
  const [q, setQ] = useState("");

  useEffect(() => { void getResults(id).then(setData); }, [id]);
  useEffect(() => { void getAudit(id, { q, failuresOnly }).then(setAudit); }, [id, q, failuresOnly]);

  if (!data) return <div role="status" aria-label="Loading results" className="h-24 animate-pulse rounded bg-surface-2" />;

  return (
    <div className="space-y-5">
      <h2 className="text-[length:var(--fs-h1)] font-semibold">{HEADER[data.status] ?? "Migration"}</h2>
      <p className="mono text-sm">
        ✓ {data.migratedCount.toLocaleString()} migrated · ⚠ {data.needsDecision.length} need your decision · ⤫ {data.skippedCount} skipped
      </p>
      <p className="text-sm text-fg-muted">
        {data.sourceCount.toLocaleString()} in source, {data.destCount.toLocaleString()} in destination
        {data.sourceCount === data.destCount ? " ✓" : ""}
      </p>

      {data.needsDecision.length ? (
        <div className="space-y-2 rounded-[6px] border border-warning p-3">
          <h3 className="font-medium">Needs your decision ({data.needsDecision.length})</h3>
          <ul className="space-y-1 text-sm">
            {data.needsDecision.map((n, i) => (
              <li key={i} className="flex items-center justify-between">
                <span>{n.detail}</span>
                <button type="button" className="text-accent">Resolve</button>
              </li>
            ))}
          </ul>
          <button type="button" onClick={() => void rerun(id)} className="rounded-[8px] bg-accent px-3 py-1.5 text-accent-fg">
            Re-run unfinished items
          </button>
          <span className="ml-2 text-sm text-fg-muted">idempotent · free</span>
        </div>
      ) : null}

      <div className="flex items-center gap-3">
        <input aria-label="Search audit" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search"
          className="h-[var(--control-h)] rounded-[6px] border border-border-strong px-2 text-sm" />
        <label className="flex items-center gap-1 text-sm">
          <input type="checkbox" checked={failuresOnly} onChange={(e) => setFailuresOnly(e.target.checked)} /> Failures only
        </label>
        <a href={reportUrl(id, "csv")} className="text-accent">Export CSV</a>
        <a href={reportUrl(id, "pdf")} className="text-accent">Export PDF</a>
      </div>

      <AuditTable entries={audit} />

      <p className="text-sm text-fg-muted">
        🔒 This log auto-deletes in 30 days. <button type="button" className="text-accent">Delete now</button>
      </p>
    </div>
  );
}
```
Register a top-level results route in `web/src/app/router.tsx` (outside the wizard shell so it is reachable from the dashboard): `{ path: "migrations/:id/results", element: <Results /> }`.

4. - [ ] Run them, expect PASS: `npm --prefix web run test -- --run src/routes/Results.test.tsx src/routes/AuditTable.test.tsx` → `Tests` all passed.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
feat(web): wizard step 6 — Results (4 outcomes, rerun, audit table, CSV/PDF)

XSS-safe subject rendering via React text escaping; no dangerouslySetInnerHTML.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 11: Global states (skeleton/empty/error/reconnecting) + error pattern (plain + expandable technical details w/ trace id)

**Goal:** Ship the four reusable global-state components from `UX-Guide.md §8.1` — `Skeleton`, `EmptyState`, `ErrorState`, `ReconnectingIndicator` — and finalize the §8.2 error pattern via the `ErrorAlert` built in Task 6, mapping an `ApiError` to a plain message + expandable mono technical detail + trace id.

**Files:**
- Create: `web/src/components/states/Skeleton.tsx`, `web/src/components/states/EmptyState.tsx`, `web/src/components/states/ErrorState.tsx`, `web/src/components/states/ReconnectingIndicator.tsx`
- Create: `web/src/components/states/fromApiError.ts`
- Test: `web/src/components/states/states.test.tsx`, `web/src/components/states/fromApiError.test.ts`

**Acceptance Criteria:**
- [ ] `Skeleton` renders an element with `role="status"`, `aria-busy="true"`, and an accessible loading label (no blank page).
- [ ] `EmptyState` renders a single-focus friendly message + one primary action (never a bare empty table).
- [ ] `ErrorState` renders the plain message + a retry button that calls the provided `onRetry`.
- [ ] `ReconnectingIndicator` renders only when `state === "reconnecting"` and shows a non-alarming "Reconnecting…" label (not styled as an error).
- [ ] `errorAlertProps(error)` maps an `ApiError` to `{ message, technicalDetail, traceId }`; for a non-`ApiError` it returns a generic plain message and null technical detail.
- [ ] Expanding "Technical details" on the resulting `ErrorAlert` reveals the mono raw detail and the `trace:` id.

**Verify:** `npm --prefix web run test -- --run src/components/states/states.test.tsx src/components/states/fromApiError.test.ts` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing tests. Create `web/src/components/states/fromApiError.test.ts`:
```ts
import { describe, expect, it } from "vitest";
import { ApiError } from "../../api/client";
import { errorAlertProps } from "./fromApiError";

describe("errorAlertProps", () => {
  it("maps an ApiError to plain message + technical detail + trace id", () => {
    const e = new ApiError(401, "AUTH_FAILED", "We couldn't sign in to WorkMail.", "IMAP NO [AUTHENTICATIONFAILED]", "4f9c-21a8");
    const props = errorAlertProps(e);
    expect(props.message).toBe("We couldn't sign in to WorkMail.");
    expect(props.technicalDetail).toContain("AUTHENTICATIONFAILED");
    expect(props.traceId).toBe("4f9c-21a8");
  });
  it("falls back to a generic message for unknown errors", () => {
    const props = errorAlertProps(new Error("boom"));
    expect(props.message).toMatch(/something went wrong/i);
    expect(props.technicalDetail).toBeNull();
  });
});
```
Create `web/src/components/states/states.test.tsx`:
```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { EmptyState } from "./EmptyState";
import { ErrorState } from "./ErrorState";
import { ReconnectingIndicator } from "./ReconnectingIndicator";
import { Skeleton } from "./Skeleton";

describe("global states", () => {
  it("Skeleton announces loading and is never blank", () => {
    render(<Skeleton label="Loading migrations" />);
    const el = screen.getByRole("status");
    expect(el).toHaveAttribute("aria-busy", "true");
    expect(el).toHaveAccessibleName(/loading migrations/i);
  });

  it("EmptyState shows a single primary action", () => {
    render(<EmptyState title="No migrations yet" actionLabel="Start" onAction={() => {}} />);
    expect(screen.getByRole("button", { name: /start/i })).toBeInTheDocument();
  });

  it("ErrorState retries", async () => {
    const onRetry = vi.fn();
    render(<ErrorState message="It broke" onRetry={onRetry} />);
    await userEvent.click(screen.getByRole("button", { name: /retry/i }));
    expect(onRetry).toHaveBeenCalled();
  });

  it("ReconnectingIndicator only renders while reconnecting", () => {
    const { rerender, queryByText } = render(<ReconnectingIndicator state="connected" />);
    expect(queryByText(/reconnecting/i)).toBeNull();
    rerender(<ReconnectingIndicator state="reconnecting" />);
    expect(queryByText(/reconnecting/i)).not.toBeNull();
  });
});
```

2. - [ ] Run them, expect FAIL: `npm --prefix web run test -- --run src/components/states/states.test.tsx src/components/states/fromApiError.test.ts` → fails — modules do not exist.

3. - [ ] Minimal implementation. Create `web/src/components/states/Skeleton.tsx`:
```tsx
export function Skeleton({ label = "Loading", className = "h-24 w-full" }: { label?: string; className?: string }) {
  return (
    <div role="status" aria-busy="true" aria-label={label}
      className={`animate-[em-skeleton_1.2s_ease-in-out_infinite] rounded bg-surface-2 ${className}`} />
  );
}
```
Create `web/src/components/states/EmptyState.tsx`:
```tsx
export function EmptyState({ title, description, actionLabel, onAction }: {
  title: string; description?: string; actionLabel: string; onAction: () => void;
}) {
  return (
    <div className="mx-auto max-w-[480px] py-16 text-center">
      <h2 className="text-[length:var(--fs-h1)] font-semibold">{title}</h2>
      {description ? <p className="mt-2 text-fg-muted">{description}</p> : null}
      <button type="button" onClick={onAction} className="mt-6 rounded-[8px] bg-accent px-5 py-3 text-accent-fg">
        {actionLabel}
      </button>
    </div>
  );
}
```
Create `web/src/components/states/ErrorState.tsx`:
```tsx
export function ErrorState({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div role="alert" className="rounded-[6px] border border-[color:var(--error-line)] bg-[color:var(--error-bg)] p-4">
      <p className="text-fg">{message}</p>
      <button type="button" onClick={onRetry} className="mt-3 rounded-[8px] border border-border px-3 py-1.5">Retry</button>
    </div>
  );
}
```
Create `web/src/components/states/ReconnectingIndicator.tsx`:
```tsx
import { WifiOff } from "lucide-react";
import type { ConnectionState } from "../../api/signalr";

export function ReconnectingIndicator({ state }: { state: ConnectionState }) {
  if (state !== "reconnecting") return null;
  return (
    <span role="status" className="inline-flex items-center gap-1.5 text-sm text-throttled">
      <WifiOff size={14} aria-hidden /> Reconnecting…
    </span>
  );
}
```
Create `web/src/components/states/fromApiError.ts`:
```ts
import { ApiError } from "../../api/client";
import type { ErrorAlertProps } from "../ErrorAlert";

export function errorAlertProps(error: unknown): ErrorAlertProps {
  if (error instanceof ApiError) {
    return {
      message: error.message,
      technicalDetail: [error.code, error.technicalDetail].filter(Boolean).join(" ") || null,
      traceId: error.traceId,
    };
  }
  return { message: "Something went wrong. Please try again.", technicalDetail: null, traceId: null };
}
```

4. - [ ] Run them, expect PASS: `npm --prefix web run test -- --run src/components/states/states.test.tsx src/components/states/fromApiError.test.ts` → `Tests` all passed.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
feat(web): global states (skeleton/empty/error/reconnecting) + error mapping

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 12: Theme toggle + WCAG AA (keyboard, focus, status icon+label) tests

**Goal:** Lock in accessibility: assert keyboard operability and visible focus on the primary interactive paths, assert status is never conveyed by color alone (icon + text), and assert the theme toggle persists and applies light/dark/system. Add `vitest-axe` for an automated a11y assertion on the dashboard and a wizard step.

**Files:**
- Modify: `web/package.json` (adds `vitest-axe` devDependency)
- Modify: `web/vitest.setup.ts` (registers axe matchers)
- Test: `web/src/a11y/keyboard.test.tsx`, `web/src/a11y/statusLabels.test.tsx`, `web/src/a11y/axe.test.tsx`, `web/src/components/ThemeToggle.test.tsx`

**Acceptance Criteria:**
- [ ] `ThemeToggle` is keyboard-operable; activating "Dark" sets `document.documentElement.dataset.theme = "dark"` and persists `localStorage["em-theme"] = "dark"`; the active option exposes `aria-pressed="true"`.
- [ ] A keyboard test tabs through the From&To step and reaches the provider radios and the Continue button (focus order is sensible; `:focus-visible` present via shadcn/Radix defaults).
- [ ] A status-label test asserts every `StatusChip` variant exposes a non-empty text label alongside its icon (`getByRole("status")` has an accessible name for all six statuses).
- [ ] A `vitest-axe` test renders the Dashboard (with a mocked list) and a wizard step and asserts **no violations** (`expect(results).toHaveNoViolations()`).

**Verify:** `npm --prefix web run test -- --run src/a11y src/components/ThemeToggle.test.tsx` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing tests. Create `web/src/components/ThemeToggle.test.tsx`:
```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ThemeToggle } from "./ThemeToggle";

describe("ThemeToggle", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute("data-theme");
    vi.stubGlobal("matchMedia", (q: string) => ({ matches: false, media: q, addEventListener: () => {}, removeEventListener: () => {} }));
  });
  afterEach(() => vi.unstubAllGlobals());

  it("applies and persists dark via keyboard activation", async () => {
    render(<ThemeToggle />);
    const dark = screen.getByRole("button", { name: /dark/i });
    dark.focus();
    await userEvent.keyboard("{Enter}");
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(localStorage.getItem("em-theme")).toBe("dark");
    expect(dark).toHaveAttribute("aria-pressed", "true");
  });
});
```
Create `web/src/a11y/statusLabels.test.tsx`:
```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatusChip, type ChipStatus } from "../components/StatusChip";

describe("status is never color alone", () => {
  it("every status chip exposes a text label", () => {
    const statuses: ChipStatus[] = ["done", "running", "throttled", "warning", "error", "queued"];
    for (const s of statuses) {
      const { unmount } = render(<StatusChip status={s} />);
      expect(screen.getByRole("status").textContent?.trim().length ?? 0).toBeGreaterThan(0);
      unmount();
    }
  });
});
```
Create `web/src/a11y/keyboard.test.tsx`:
```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { StepFromTo } from "../wizard/StepFromTo";

vi.mock("react-router-dom", () => ({ useNavigate: () => vi.fn(), useOutletContext: () => ({ migration: { id: "m1" } }) }));
vi.mock("../wizard/useDraft", () => ({ useDraft: () => ({ saveEndpoints: vi.fn(), migration: { id: "m1" } }) }));

describe("keyboard nav on From & To", () => {
  it("tabs to the provider radios", async () => {
    render(<StepFromTo />);
    await userEvent.tab();
    expect(document.activeElement?.getAttribute("role")).toBe("radio");
  });
});
```
Create `web/src/a11y/axe.test.tsx`:
```tsx
import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { axe } from "vitest-axe";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Dashboard } from "../routes/Dashboard";
import * as api from "../api/migrations";

describe("axe a11y", () => {
  afterEach(() => vi.restoreAllMocks());
  it("dashboard has no detectable violations", async () => {
    vi.spyOn(api, "listMigrations").mockResolvedValue([
      { id: "r1", status: "Running", wizardStep: 5, from: "imap", to: "graph", isBatch: true, scopeSummary: "218 mailboxes", mailboxCount: 218, progress: { migratedCount: 1, total: 2, currentFolder: null, msgPerMin: 1, status: "Running" }, createdAt: "2026-06-01T00:00:00Z" },
    ] as never);
    const { container, findByText } = render(<MemoryRouter><Dashboard /></MemoryRouter>);
    await findByText(/218 mailboxes/i);
    const results = await axe(container);
    expect(results).toHaveNoViolations();
  });
});
```

2. - [ ] Run them, expect FAIL: `npm --prefix web run test -- --run src/a11y src/components/ThemeToggle.test.tsx` → fails — `vitest-axe` not installed (`Cannot find module 'vitest-axe'`) and matcher not registered.

3. - [ ] Minimal implementation. Install the dev dep (from project root): `npm --prefix web install -D vitest-axe@^0.1.0`. Register the matcher in `web/vitest.setup.ts`:
```ts
import "@testing-library/jest-dom/vitest";
import * as matchers from "vitest-axe/matchers";
import { expect } from "vitest";

expect.extend(matchers);
```
The `ThemeToggle`, `StepFromTo`, and `StatusChip` already satisfy the behavioral assertions (built in Tasks 3/5). If the axe run reports a violation (e.g., a missing label or contrast issue surfaced by jsdom-evaluable rules), fix the offending component (add the missing `aria-label`, associate the control with its `<label>`, or adjust the token class) — never disable the rule.

4. - [ ] Run them, expect PASS: `npm --prefix web run test -- --run src/a11y src/components/ThemeToggle.test.tsx` → `Tests` all passed.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
test(web): theme toggle + WCAG AA (keyboard, focus, status icon+label, axe)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 13: Functional Verification — Playwright E2E driving the wizard happy-path against a mock API

**Goal:** Prove the subsystem's headline behavior end-to-end: a Playwright browser test drives the wizard happy-path (Dashboard → New → From&To → Connect From (test passes) → Connect To (test passes) → Scope → Review (Approve) → Run shows live progress) against a **mock API + mock SignalR** served by MSW + a dev fixture, with no real backend.

**Files:**
- Create: `web/src/mocks/handlers.ts`
- Create: `web/src/mocks/browser.ts`
- Create: `web/src/mocks/enable.ts`
- Create: `web/playwright.config.ts`
- Create: `web/e2e/wizard-happy-path.spec.ts`
- Modify: `web/src/main.tsx` (start MSW when `VITE_USE_MOCKS` is set)
- Modify: `web/package.json` (adds `e2e` script)

**Acceptance Criteria:**
- [ ] MSW handlers implement the CONTRACTS §6 routes used by the happy path: `POST /migrations` (Draft), `GET /migrations` (list), `GET /migrations/:id`, `PATCH …/endpoints`, `PUT …/connection/:side`, `POST …/connection/:side/test` (returns `ok:true, folderCount:14, messageCount:3201`), `PUT …/scope`, `POST …/preflight`, `GET …/preflight` (a clean plan), `POST …/approve` (→Running).
- [ ] `playwright.config.ts` runs against `vite preview`/`dev` with `VITE_USE_MOCKS=1` on the configured base URL; one Chromium project.
- [ ] `wizard-happy-path.spec.ts` walks the full flow and asserts: the dashboard welcome → reaching Connect, the test-connection success text ("found 14 folders, 3,201 messages"), the Review "Ready to migrate" card, and that clicking "Start migration" lands on the Run view showing a progress element.
- [ ] `npm --prefix web run e2e` passes (1 passed) headless.

**Verify:** `npm --prefix web run e2e` → Playwright reports `1 passed`.

**Steps:**

1. - [ ] Write the failing E2E test. Create `web/e2e/wizard-happy-path.spec.ts`:
```ts
import { expect, test } from "@playwright/test";

test("operator drives the wizard happy path", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: /start your first migration/i }).click();

  // From & To
  await page.getByRole("radio", { name: /from workmail/i }).click();
  await page.getByRole("radio", { name: /to microsoft 365/i }).click();
  await page.getByRole("button", { name: /continue/i }).click();

  // Connect From — test must pass to advance
  await page.getByLabel(/username/i).fill("old@biz.com");
  await page.getByLabel(/password/i).fill("app-pw");
  await page.getByRole("button", { name: /test connection/i }).click();
  await expect(page.getByText(/found 14 folders, 3,201 messages/i)).toBeVisible();
  await page.getByRole("button", { name: /continue/i }).click();

  // Connect To
  await expect(page.getByRole("heading", { name: /connect to/i })).toBeVisible();
  await page.getByRole("button", { name: /test connection/i }).click();
  await expect(page.getByText(/found 14 folders/i)).toBeVisible();
  await page.getByRole("button", { name: /continue/i }).click();

  // Scope (single) → Review
  await page.getByRole("button", { name: /continue/i }).click();
  await expect(page.getByText(/ready to migrate/i)).toBeVisible();
  await page.getByRole("button", { name: /start migration/i }).click();

  // Run
  await expect(page.getByRole("progressbar")).toBeVisible();
});
```

2. - [ ] Run it, expect FAIL: `npm --prefix web run e2e` → fails — no `e2e` script, no Playwright config, no MSW handlers; the dev server has no mock backend so the flow cannot complete.

3. - [ ] Minimal implementation. Create `web/src/mocks/handlers.ts`:
```ts
import { http, HttpResponse } from "msw";

let draft = {
  id: "e2e-1", status: "Draft", wizardStep: 0, from: null as string | null, to: null as string | null,
  isBatch: false, scopeSummary: null as string | null, mailboxCount: 1, progress: null, createdAt: "2026-06-01T00:00:00Z",
};

const okTest = { ok: true, folderCount: 14, messageCount: 3201 };
const cleanPlan = {
  scanning: false, issues: [],
  estimate: { mailboxCount: 1, folderCount: 14, messageCount: 3201, totalBytes: 262144000, estimatedDurationSeconds: 720 },
  usage: null,
};

export const handlers = [
  http.post("/api/v1/migrations", () => { draft = { ...draft, status: "Draft", wizardStep: 0 }; return HttpResponse.json(draft); }),
  http.get("/api/v1/migrations", () => HttpResponse.json([])),
  http.get("/api/v1/migrations/:id", () => HttpResponse.json(draft)),
  http.patch("/api/v1/migrations/:id/endpoints", async ({ request }) => {
    const body = (await request.json()) as { from: string; to: string };
    draft = { ...draft, from: body.from, to: body.to, wizardStep: 1 };
    return HttpResponse.json(draft);
  }),
  http.put("/api/v1/migrations/:id/connection/:side", () => HttpResponse.json(draft)),
  http.post("/api/v1/migrations/:id/connection/:side/test", () => HttpResponse.json(okTest)),
  http.put("/api/v1/migrations/:id/scope", () => { draft = { ...draft, wizardStep: 4 }; return HttpResponse.json(draft); }),
  http.post("/api/v1/migrations/:id/preflight", () => new HttpResponse(null, { status: 202 })),
  http.get("/api/v1/migrations/:id/preflight", () => HttpResponse.json(cleanPlan)),
  http.post("/api/v1/migrations/:id/approve", () => { draft = { ...draft, status: "Running", wizardStep: 5 }; return HttpResponse.json(draft); }),
];
```
Create `web/src/mocks/browser.ts`:
```ts
import { setupWorker } from "msw/browser";
import { handlers } from "./handlers";

export const worker = setupWorker(...handlers);
```
Create `web/src/mocks/enable.ts`:
```ts
export async function enableMocksIfRequested(): Promise<void> {
  if (import.meta.env.VITE_USE_MOCKS !== "1") return;
  const { worker } = await import("./browser");
  await worker.start({ onUnhandledRequest: "bypass" });
}
```
Modify `web/src/main.tsx` to await mocks before rendering:
```tsx
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./index.css";
import { enableMocksIfRequested } from "./mocks/enable";

void enableMocksIfRequested().then(() => {
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
});
```
Create `web/playwright.config.ts`:
```ts
import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  use: { baseURL: "http://localhost:4173", trace: "on-first-retry" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    // cross-env makes the VITE_USE_MOCKS env var portable across PowerShell/cmd (Windows) and POSIX shells.
    // Vite bakes import.meta.env at BUILD time, so the flag must be set for the build that `preview` serves.
    command: "cross-env VITE_USE_MOCKS=1 npm run build && cross-env VITE_USE_MOCKS=1 npm run preview -- --port 4173",
    url: "http://localhost:4173",
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});
```
Add the `e2e` script to `web/package.json` scripts: `"e2e": "playwright test"`. Install `cross-env` as a dev dependency (from project root): `npm --prefix web install -D cross-env@^7.0.3`. Initialize the MSW service worker asset once (from project root): `npx --prefix web msw init web/public --save`.

4. - [ ] Run it, expect PASS: `npm --prefix web run e2e` → Playwright `1 passed`. (Run `npx --prefix web playwright install chromium` first if the browser binary is missing.)

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
test(web): Playwright E2E — wizard happy path against MSW mock API

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 14: Security Verification — no secrets persisted, XSS-safe rendering, cookie auth, CSP

**Goal:** Prove this plan's security focus (INDEX per-plan table for Plan 10): no secrets in `localStorage` or embedded in the bundle, mail subjects + folder names rendered XSS-safe, the auth token handled per the API scheme (httpOnly cookie — never stored client-side), and a CSP configured.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in acceptanceCriteria has been re-validated independently, with output captured.

**Files:**
- Modify: `web/index.html` (add the CSP meta tag)
- Create: `web/src/security/xss.test.tsx`
- Create: `web/src/security/noSecrets.test.ts`
- Create: `web/src/security/auth.test.ts`
- Create: `web/scripts/scan-bundle.mjs`
- Create: `web/src/security/csp.test.ts`
- Modify: `web/package.json` (adds `scan:bundle` script)

**Acceptance Criteria:**
- [ ] **XSS-safe rendering:** a component test renders the audit table with a subject of `<script>alert('xss')</script>` and a folder name containing `<img src=x onerror=alert(1)>` and asserts they appear as **escaped literal text** (`getByText` finds the raw string) and that **no `<script>`/`<img onerror>` element is created** in the DOM and **no `dangerouslySetInnerHTML`** is used anywhere in `web/src` (grep-style source assertion in the test).
- [ ] **No secrets persisted:** a test exercises the connect flow's `putConnection` path and asserts the secret value is **never** written to `localStorage` or `sessionStorage` (storage `setItem` spy sees no value equal to the secret); and asserts the codebase contains no `localStorage.setItem(... token/secret/password ...)` call (source assertion).
- [ ] **Auth per API scheme:** a test asserts `apiFetch` sends `credentials: "include"` and never sets an `Authorization` header from a stored token; `signalr.ts` `withUrl` is called without an `accessTokenFactory` reading storage (source assertion that no `accessTokenFactory` references `localStorage`/`sessionStorage`).
- [ ] **No secrets embedded in the bundle:** `web/scripts/scan-bundle.mjs` builds the app and scans `web/dist/assets/*.js` for secret-shaped patterns (`SigningKey`, `client_secret`, `BEGIN PRIVATE KEY`, `AKIA[0-9A-Z]{16}`, `password=`) and exits non-zero if any is found; the live scan over the real build exits **0** — captured.
- [ ] **CSP configured:** `index.html` contains a `<meta http-equiv="Content-Security-Policy">` with at least `default-src 'self'`, `connect-src 'self'` (covers REST + same-origin WS), `img-src 'self' data:`, `object-src 'none'`, `base-uri 'self'`, `frame-ancestors 'none'`; a test asserts these directives are present.

**Verify:** `npm --prefix web run test -- --run src/security` → all pass; and `npm --prefix web run scan:bundle` → prints `bundle-scan OK`, exit 0.

**Steps:**

1. - [ ] Write the failing tests. Create `web/src/security/xss.test.tsx`:
```tsx
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, resolve } from "node:path";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AuditTable } from "../routes/AuditTable";

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const p = join(dir, name);
    return statSync(p).isDirectory() ? walk(p) : [p];
  });
}

describe("XSS-safe rendering", () => {
  it("renders a script-payload subject and onerror folder as escaped text", () => {
    const { container } = render(
      <AuditTable entries={[
        { subject: "<script>alert('xss')</script>", messageDate: "2024-01-08T00:00:00Z", sourceFolder: "<img src=x onerror=alert(1)>", destFolder: "/Sent", status: "skipped" },
      ]} />,
    );
    expect(screen.getByText("<script>alert('xss')</script>")).toBeInTheDocument();
    expect(screen.getByText("<img src=x onerror=alert(1)>")).toBeInTheDocument();
    expect(container.querySelector("script")).toBeNull();
    expect(container.querySelector("img")).toBeNull();
  });

  it("uses no dangerouslySetInnerHTML anywhere in src", () => {
    const srcRoot = resolve(__dirname, "..");
    const offenders = walk(srcRoot)
      .filter((f) => /\.(tsx?|jsx?)$/.test(f) && !f.includes(`${"security"}`))
      .filter((f) => readFileSync(f, "utf8").includes("dangerouslySetInnerHTML"));
    expect(offenders).toEqual([]);
  });
});
```
Create `web/src/security/noSecrets.test.ts`:
```ts
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, resolve } from "node:path";
import { afterEach, describe, expect, it, vi } from "vitest";
import { putConnection } from "../api/migrations";

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const p = join(dir, name);
    return statSync(p).isDirectory() ? walk(p) : [p];
  });
}

describe("no secrets persisted", () => {
  afterEach(() => vi.restoreAllMocks());

  it("never writes the connection secret to storage", async () => {
    const setLocal = vi.spyOn(Storage.prototype, "setItem");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("{}", { status: 200 })));
    const secret = "super-secret-app-password-123";
    await putConnection("m1", "from", { auth: "ImapBasic", settings: { host: "h" }, secret });
    const wroteSecret = setLocal.mock.calls.some(([, v]) => typeof v === "string" && v.includes(secret));
    expect(wroteSecret).toBe(false);
    vi.unstubAllGlobals();
  });

  it("contains no storage write of a token/secret/password", () => {
    const srcRoot = resolve(__dirname, "..");
    const re = /(local|session)Storage\.setItem\([^)]*(token|secret|password|jwt|credential)/i;
    const offenders = walk(srcRoot)
      .filter((f) => /\.(tsx?|jsx?)$/.test(f) && !f.includes("security"))
      .filter((f) => re.test(readFileSync(f, "utf8")));
    expect(offenders).toEqual([]);
  });
});
```
Create `web/src/security/auth.test.ts`:
```ts
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiFetch } from "../api/client";

describe("auth per API scheme (httpOnly cookie)", () => {
  afterEach(() => vi.restoreAllMocks());

  it("sends credentials include and no token-derived Authorization header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("{}", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    await apiFetch("/migrations");
    const init = fetchMock.mock.calls[0][1];
    expect(init.credentials).toBe("include");
    const headers = (init.headers ?? {}) as Record<string, string>;
    expect(Object.keys(headers).map((k) => k.toLowerCase())).not.toContain("authorization");
    vi.unstubAllGlobals();
  });

  it("signalr withUrl uses no storage-reading accessTokenFactory", () => {
    const src = readFileSync(resolve(__dirname, "../api/signalr.ts"), "utf8");
    // No accessTokenFactory at all (cookie carries auth); and certainly none reading storage.
    expect(src).not.toMatch(/accessTokenFactory[\s\S]*?(local|session)Storage/);
  });
});
```
Create `web/src/security/csp.test.ts`:
```ts
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

describe("CSP", () => {
  it("index.html ships a Content-Security-Policy meta with safe directives", () => {
    const html = readFileSync(resolve(__dirname, "../../index.html"), "utf8");
    expect(html).toMatch(/http-equiv=["']Content-Security-Policy["']/i);
    for (const directive of [
      "default-src 'self'", "connect-src 'self'", "img-src 'self' data:",
      "object-src 'none'", "base-uri 'self'", "frame-ancestors 'none'",
    ]) {
      expect(html).toContain(directive);
    }
  });
});
```

2. - [ ] Run them, expect FAIL: `npm --prefix web run test -- --run src/security` → fails — `csp.test.ts` fails (no CSP meta yet); the others compile but `noSecrets`/`auth`/`xss` should already pass given Tasks 1/2/10 — confirm and fix any real gap they surface (if a source-scan offender is found, remove it). `scan:bundle` script does not exist yet.

3. - [ ] Minimal implementation. Add the CSP meta to `web/index.html` `<head>`:
```html
    <meta http-equiv="Content-Security-Policy" content="default-src 'self'; connect-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; font-src 'self'; script-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'" />
```
Create `web/scripts/scan-bundle.mjs`:
```js
import { execSync } from "node:child_process";
import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join } from "node:path";

// fileURLToPath is the only correct way to turn import.meta.url into a path on Windows
// (URL.pathname yields a broken "/D:/..." string that fs/exec choke on).
const webRoot = fileURLToPath(new URL("..", import.meta.url));
execSync("npm run build", { cwd: webRoot, stdio: "inherit" });

const assetsDir = join(webRoot, "dist", "assets");
const patterns = [
  /SigningKey/,
  /client_secret/i,
  /-----BEGIN PRIVATE KEY-----/,
  /AKIA[0-9A-Z]{16}/,
  /password\s*=\s*['"][^'"]+['"]/i,
];

let found = false;
for (const f of readdirSync(assetsDir).filter((n) => n.endsWith(".js"))) {
  const text = readFileSync(join(assetsDir, f), "utf8");
  for (const p of patterns) {
    if (p.test(text)) {
      console.error(`Secret-shaped pattern ${p} found in dist/assets/${f}`);
      found = true;
    }
  }
}
if (found) {
  console.error("bundle-scan FAILED — secret-shaped content in the bundle.");
  process.exit(1);
}
console.log("bundle-scan OK");
```
Add the script to `web/package.json` scripts: `"scan:bundle": "node scripts/scan-bundle.mjs"`.

4. - [ ] Run them, expect PASS: `npm --prefix web run test -- --run src/security` → `Tests` all passed; `npm --prefix web run scan:bundle` → prints `bundle-scan OK`, exit 0. Capture the `bundle-scan OK` output and the passing security test summary (XSS-escaped assertion, storage-write-zero assertion, `credentials: "include"` assertion, CSP-directives assertion) into the task close-out notes. Independently re-validate each acceptance item: (a) re-run the XSS test and read the asserted escaped text; (b) re-run `noSecrets`/`auth` and confirm zero offenders; (c) re-run `scan:bundle` and read `bundle-scan OK`; (d) re-read `index.html` to confirm the CSP directives are present.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
test(web,security): no secrets persisted/bundled; XSS-safe; cookie auth; CSP

USER-ORDERED GATE: subject/folder render escaped (no script/img injection);
connection secret never written to storage; apiFetch uses credentials:include
with no token Authorization header; SignalR uses no storage-reading token
factory; dist bundle scanned for secret-shaped content; CSP meta enforced.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

## Live integration follow-up (Wave F — not in this plan's task list)

This plan is built and verified entirely against the **frozen CONTRACTS §6 wire shapes** with an MSW mock API and an injectable fake SignalR hub, so it parallelizes with the backend (INDEX Wave C). The following are explicit, documented follow-ups that land when Plan 08's live API + SignalR hub are available (INDEX Wave F) — they are *not* hidden gaps in the tasks above:

- **Hosted usage widget data.** `Dashboard` holds `usage` as `null` and `UsageWidget` renders nothing until a hosted usage endpoint exists. Wiring that fetch (and surfacing `UsageDto` on the dashboard) is a Wave-F change; until then no usage bar is fabricated.
- **`canBatch` authority.** `canBatchFor(migration)` uses the destination-provider proxy (graph/gmail ⇒ batchable). Once Plan 08 persists the connection auth method on the migration, the API sets batch-eligibility authoritatively and the client reads it instead of the heuristic.
- **SignalR origin + cookie.** `createHub().withUrl("/hubs/migrations")` assumes the API is same-origin (so the httpOnly auth cookie is sent and CSP `connect-src 'self'` covers the WS). A cross-origin deployment would require a `withCredentials` transport option + a CSP `connect-src` entry — revisit at deploy time.
- **Dashboard live progress.** Per-row live updates via SignalR can be layered on later; v1 renders each row's progress from `MigrationDto.progress` returned by `GET /migrations`.
