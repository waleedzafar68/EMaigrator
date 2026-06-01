# EMaigrator — Frontend Design System

> **Status:** Locked. Owns *visual identity* — color, type, spacing, components, motion, theming.
> **Audience:** Claude / agents using the `frontend-design` skill, and frontend engineers.
> **Pairs with:** `UX-Guide.md` (flows & interactions — this doc does NOT repeat them) and `DESIGN.md §13` (Vite + React + TS + SignalR).
> **Direction (chosen):** **Modern technical · teal/emerald accent · light + dark.**

---

## 1. Design Direction & The Persona Reconciliation

**Aesthetic = Modern technical:** sharp, data-first, confident. Tight-but-legible spacing, hairline borders, small radii, monospace for all data/numerics/IDs, dark-mode-native.

**The deliberate reconciliation (critical — do not skip):** "Modern technical" is the *visual language*, but **information density flexes by persona**:

- **Single-mailbox happy path** (the "old man") → the *same* design system rendered at **low density**: larger type, more whitespace, one primary action per screen, plain language, reassurance visible. It must **never look like a monitoring dashboard.**
- **Batch / admin / logs** (the MSP) → **full data density**: compact tables, monospace numerics, dense grids, more on screen.

The density toggles in `UX-Guide.md` (dashboard cards⇄list, run-view light⇄dense) are the runtime expression of this. **Default low-density; let power users opt into density.** This is how a technical look stays non-intimidating.

---

## 2. Foundation: Library & Tooling

### Runtime context (shapes every design choice)
- **This is a client-rendered SPA: Vite + React 19 + TypeScript.** It is a **pure client** of a separate **ASP.NET Core REST API** (C# backend) — **no Next.js, no SSR, no server components.**
- **Implications for design:**
  - **All data is fetched client-side** → every data surface needs a **loading/skeleton state** (`UX-Guide.md §8.1`); there is no server-rendered first paint.
  - **Live data (progress, status) arrives over SignalR** (WebSocket) → design for streaming updates + a **"reconnecting…"** state, not request/response refreshes.
  - **Client-side routing** (e.g., React Router) for the dashboard ↔ wizard navigation.
  - Served as **static assets** (the API or a tiny container serves the bundle) → keep bundle size reasonable; lazy-load heavy/dense views.

### Stack
- **Tailwind CSS** — utility styling + design tokens (CSS variables for theming).
- **shadcn/ui** (Radix primitives) — accessible-by-default components (focus management, ARIA, keyboard nav) → directly satisfies the **WCAG AA** requirement (`UX-Guide.md §8.4`). Components are copied in and themeable, no black-box dependency.
- **Lucide** — line-icon set (pairs with shadcn; clean, technical).
- **Recharts** (or similar) for the throughput/progress visualizations on dense run views.

All theming via **CSS custom properties** so light/dark is a token swap, not a component fork.

---

## 3. Color System

Accent is **teal/emerald**; everything else is a **neutral slate** scale so data dominates and the accent means something.

### Brand / accent (teal)
| Token | Light | Dark |
|---|---|---|
| `--accent` (primary) | `#0d9488` (teal-600) | `#2dd4bf` (teal-400) |
| `--accent-hover` | `#0f766e` (teal-700) | `#5eead4` (teal-300) |
| `--accent-fg` (on accent) | `#ffffff` | `#042f2e` |
| `--accent-subtle` (bg) | `#f0fdfa` (teal-50) | `#134e4a` (teal-900) |

### Neutrals (slate)
Use a full slate ramp (`50→950`). Surfaces:
| Token | Light | Dark |
|---|---|---|
| `--bg` | `#ffffff` | `#0b1120` (slate-950-ish) |
| `--surface` | `#f8fafc` (slate-50) | `#111827` |
| `--border` | `#e2e8f0` (slate-200) | `#1e293b` (slate-800) |
| `--fg` | `#0f172a` (slate-900) | `#e2e8f0` (slate-200) |
| `--fg-muted` | `#64748b` (slate-500) | `#94a3b8` (slate-400) |

### Semantic / status
Accent is teal, so **success uses a distinct green** to avoid teal/green confusion — and **status is never color-alone** (always icon + label, per WCAG):
| Status | Color (light / dark) | Icon |
|---|---|---|
| Success / migrated / done | `#16a34a` / `#4ade80` | ✓ check |
| Running | `--accent` (teal) | ▸ / spinner |
| Throttled | `#d97706` / `#fbbf24` (amber) | ⟳ slow |
| Warning / needs-decision | `#ca8a04` / `#facc15` | ⚠ |
| Error / failed | `#dc2626` / `#f87171` (red) | ✕ |
| Queued / idle | `--fg-muted` (slate) | ○ |

---

## 4. Typography

A modern-technical pairing: a clean geometric sans for UI, a monospace for **all data** (counts, sizes, dates, IDs, throughput, technical error detail) — the mono is what delivers the "data-first" feel.

- **UI sans:** **Geist Sans** (fallback: Inter, system-ui).
- **Mono:** **Geist Mono** (fallback: JetBrains Mono, ui-monospace). Used for: message counts, msg/min, sizes (`250 MB`), dates, trace IDs, folder paths, and the expandable "Technical details."

### Scale (rem)
| Token | Size / line | Use |
|---|---|---|
| `display` | 1.875 / 2.25 | Page titles, welcome |
| `h1` | 1.5 / 2 | Section headers |
| `h2` | 1.25 / 1.75 | Card titles |
| `body` | 0.9375 / 1.5 | Default |
| `sm` | 0.8125 / 1.25 | Secondary, table cells |
| `mono-data` | 0.875 / 1.25 | Numerics, IDs (Geist Mono) |

**Low-density (individual) screens bump body up** (1rem+) and increase line-height for comfort — same tokens, persona-scaled.

---

## 5. Spacing, Layout & Shape

- **Spacing scale:** 4px base (`4, 8, 12, 16, 24, 32, 48`). Dense surfaces use the low end; happy-path screens use the high end.
- **Radius:** small/sharp — `--radius: 6px` (cards/inputs), `4px` (chips/badges). Technical, not pillowy.
- **Borders:** hairline `1px` `--border`; prefer borders + subtle elevation over heavy shadows.
- **Elevation:** restrained — `shadow-sm` for cards, `shadow-md` only for overlays/popovers.
- **Layout:** app shell = left nav (or top bar) + content; max content width on forms (readability) but full-bleed on dense tables/grids.

---

## 6. Core Components (shadcn mapping)

| EMaigrator element | shadcn / pattern | Notes |
|---|---|---|
| Wizard stepper | custom + `Progress` | Gated, back-allowed (`UX-Guide.md §4`) |
| Migration card / row | `Card` / `Table` | Card⇄list density toggle |
| Status chip | `Badge` | **Icon + label**, semantic color |
| Connect forms | `Form` + `Input` + `Select` | Provider/region selects; inline guide |
| Test-connection result | `Alert` (success/error) | Concrete success; expandable error (§7) |
| Review issues | `Accordion` (grouped) + `Select` (bulk resolution) | Per-item under "[details]" |
| Run progress | `Progress` + `Recharts` | + `throttled` chip |
| Batch list | `Table` + filter/search | Density toggle → compact rows |
| Results / audit log | `Table` + `Tabs` | Export menu (`DropdownMenu`) |
| Errors | `Alert` + `Collapsible` | Plain + "Technical details" (§7) |
| Notifications surface | `Toast` (in-app) | + email out-of-band |
| Theme toggle | `DropdownMenu` | Light / Dark / System |

---

## 7. Key Visual Patterns

**Error pattern (visual form of `UX-Guide.md §8.2`):** plain-language `Alert` (semantic color + icon + "what to do" link), with a `Collapsible` **"Technical details"** revealing **mono-formatted** raw error + trace ID.

```
⚠  We couldn't sign in to WorkMail.            ← Alert, amber, body sans
   WorkMail needs an app password.  → How to     ← link in --accent
   ▸ Technical details                            ← Collapsible trigger
       IMAP NO [AUTHENTICATIONFAILED] …           ← Geist Mono, --surface bg
       trace: 4f9c-21a8                           ← Geist Mono, --fg-muted
```

**Numerics are always mono + tabular-nums** so progress counts don't jitter as they update (`2,310 / 3,201`, `412 msg/min`).

**Throttling chip:** amber `Badge` with `⟳` + "Slowing to respect limits" — visually distinct from error red, never a silent slow bar.

---

## 8. Motion

- **Purposeful, subtle.** Progress bars animate smoothly; step transitions slide; toasts fade. ~150–250ms, ease-out.
- **No decorative motion.** This is a technical tool; movement signals *state change*, not delight.
- **Respect `prefers-reduced-motion`** — disable non-essential transitions.

---

## 9. Theming & Accessibility

- **Light + dark** via CSS-variable token swap; default to **system preference**, with a manual toggle (persisted per user).
- **WCAG AA:** all text/icon pairs meet contrast in *both* themes (verify teal accent on both `--bg`s — `teal-600` on white and `teal-400` on slate-950 both pass AA for UI text/large text).
- **Status never by color alone** — icon + label always (color-blind safe).
- **Full keyboard nav + focus-visible rings** (shadcn/Radix default; keep them).
- **Generous hit targets** (≥40px) on the low-density individual path — aging-eyes/shaky-hands friendly (`UX-Guide.md §8.4`).

---

## 10. Decision Log

| Decision | Why |
|---|---|
| Modern-technical language, density flexed by persona | Honors the chosen aesthetic without intimidating the non-tech individual — density toggles make it real |
| shadcn/ui + Tailwind | Accessible Radix primitives satisfy WCAG AA out of the box; themeable; no black-box dependency |
| Teal/emerald accent on a slate neutral base | Distinctive-but-calm; neutrals let data dominate and make the accent meaningful |
| Success = green ≠ teal accent + icons always | Avoids teal/green status confusion; color-blind safe |
| Geist Sans + Geist Mono; mono for all data | Mono numerics/IDs deliver the "data-first" feel; tabular-nums prevent jitter |
| Small radii, hairline borders, restrained elevation | Sharp/technical, not pillowy |
| Light + dark, system-default | Expected by MSPs; better for long monitoring sessions |
| Subtle, state-signaling motion only | It's a tool, not a toy; reduced-motion respected |
