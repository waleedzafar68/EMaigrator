# Handoff: EMaigrator — Email Migration Web App

## Overview
EMaigrator is a client-rendered SPA for migrating email mailboxes between providers (Google Workspace, Microsoft 365, Exchange, AWS WorkMail, IMAP, Yahoo, Zoho). It serves **two personas from one design system**:

- **Individual ("just me")** — a non-technical user migrating their own mailbox. Rendered at **low density**: bigger type, more whitespace, one primary action per screen, plain reassuring language. Must never look like a monitoring dashboard.
- **Admin / MSP** — a managed-service provider running **batch** migrations across many mailboxes. Rendered at **full data density**: compact tables, dense grids, live monitoring.

"Modern technical" is the fixed visual language; **information density flexes by persona**. Default to low density and let power users opt into density.

The prototype covers six surfaces: **Dashboard, Migration Wizard, Live Run, Batches, Results & Audit, Settings.**

## About the Design Files
The files in this bundle are **design references created in HTML/React-via-Babel** — prototypes that demonstrate the intended look, layout, and behavior. **They are not production code to copy directly.** They use in-browser Babel, inline styles, and mocked data for fast iteration.

Your task is to **recreate these designs in the target codebase's environment** using its established patterns and libraries. The intended production stack (per the source design doc) is **Vite + React 19 + TypeScript, Tailwind CSS, shadcn/ui (Radix primitives), Lucide icons, and Recharts** — but if the codebase already has conventions, follow those. Data arrives over a **REST API + SignalR (WebSocket)** for live progress; design every data surface with loading/skeleton and "reconnecting…" states accordingly.

## Fidelity
**High-fidelity (hifi).** Final colors, typography, spacing, component states, and interactions are all specified. Recreate the UI faithfully using the codebase's component library, mapping the tokens below to its theme system. All exact values are in `styles.css` (the single source of truth for tokens).

---

## Design Tokens
All tokens live in `styles.css` as CSS custom properties, themed via `[data-theme="light"|"dark"]`, with density via `[data-density="comfortable"|"compact"]` and surface "vibe" via `[data-vibe="calm"|"vivid"]`. Port these into the codebase's token/Tailwind config.

### Color — Accent (teal, the brand-meaningful color)
| Token | Light | Dark |
|---|---|---|
| `--accent` | `#0d9488` | `#2dd4bf` |
| `--accent-hover` | `#0f766e` | `#5eead4` |
| `--accent-fg` (text on accent) | `#ffffff` | `#042f2e` |
| `--accent-subtle` (tint bg) | `#f0fdfa` | `#134e4a` |
| `--accent-line` (tint border) | `#99f6e4` | `#115e59` |

An optional **emerald** accent preset also ships (light `#059669` / dark `#34d399`) — see `ACCENTS` in `app.jsx`.

### Color — Neutrals / surfaces (slate)
| Token | Light | Dark |
|---|---|---|
| `--bg` (page) | `#ffffff` | `#0b1120` |
| `--surface` (sidebar, sunken) | `#f8fafc` | `#111827` |
| `--surface-2` (chips, tracks) | `#f1f5f9` | `#161f31` |
| `--surface-raised` (cards) | `#ffffff` | `#131c2e` |
| `--border` | `#e2e8f0` | `#1e293b` |
| `--border-strong` (inputs) | `#cbd5e1` | `#334155` |
| `--fg` | `#0f172a` | `#e2e8f0` |
| `--fg-muted` | `#64748b` | `#94a3b8` |
| `--fg-subtle` | `#94a3b8` | `#64748b` |

### Color — Status (semantic; ALWAYS paired with an icon + label, never color alone)
| Status | Meaning | Color light/dark | bg light/dark | Icon (Lucide) |
|---|---|---|---|---|
| `done` | Migrated / complete | `#16a34a` / `#4ade80` | `#f0fdf4` / `#0d2417` | check |
| `running` | In progress | `--accent` | `--accent-subtle` | play |
| `throttled` | Rate-limited (backing off) | `#d97706` / `#fbbf24` | `#fffbeb` / `#281c08` | rotate-ccw / "slow" |
| `warning` | Needs decision | `#ca8a04` / `#facc15` | `#fefce8` / `#2a2408` | alert-triangle |
| `error` | Failed | `#dc2626` / `#f87171` | `#fef2f2` / `#2a1212` | x |
| `queued` / idle | Waiting | `#64748b` / `#94a3b8` | `#f1f5f9` / `#1a2335` | circle |

> **Cost/progress semantics are inverted from typical dashboards: a rising line is normal/good; throttling (slowdown) is the warning state, shown amber — distinct from error red.**

### Typography
- **UI sans:** **Geist** (fallback Inter, system-ui). `--font-sans`.
- **Mono:** **Geist Mono** (fallback JetBrains Mono, ui-monospace). `--font-mono`. Used for **all data**: message counts, msg/min, sizes, dates, trace IDs, folder paths, server hosts, technical error detail. Always `font-variant-numeric: tabular-nums` so streaming counts don't jitter.
- Loaded via Google Fonts in `styles.css` (`@import`). In production, self-host or use the codebase's font pipeline.

Scale (rem):
| Token | size / line-height | Use |
|---|---|---|
| `--fs-display` | 1.875 / 2.25 | Big % numbers, welcome |
| `--fs-h1` | 1.5 / 2 | Wizard step titles |
| `--fs-h2` | 1.25 / 1.75 | Page + card titles |
| `--fs-body` | 0.9375 / 1.5 | Default body |
| `--fs-sm` | 0.8125 / 1.25 | Secondary, table cells, inputs |
| `--fs-xs` | 0.75 / 1 | Helper text, captions |
| `--fs-mono` | 0.875 | Numeric/data mono |

KPI numbers: ~28px / weight 600 / tight tracking. KPI/nav-group labels: 10–11px / **UPPERCASE** / weight 700 / letter-spacing 0.07–0.1em.

### Spacing (4px base)
`--s1:4 --s2:8 --s3:12 --s4:16 --s6:24 --s8:32 --s12:48`. Density-driven vars that components consume:
| Var | comfortable | (default) | compact |
|---|---|---|---|
| `--card-pad` | 22 | 20 | 14 |
| `--row-pad-y` / `--row-pad-x` | 13 / 18 | 11 / 16 | 6 / 12 |
| `--section-gap` | 28 | 24 | 16 |
| `--grid-gap` | 18 | 16 | 12 |
| `--control-h` (inputs/buttons) | 42 | 38 | 32 |
| `--hit` (min hit target) | 44 | 40 | 32 |
| `--body-scale` (root font ×) | 1.06 | 1 | 0.96 |

> In **comfortable** mode hit targets are ≥40px (aging-eyes / shaky-hands friendly per the individual persona). Compact is opt-in for admins.

### Radius, borders, shadows
- `--radius` 6px (cards/inputs), `--radius-sm` 4px (chips/badges/icon-buttons), `--radius-lg` 8px (buttons, popovers), `--radius-full` (pills/avatars). **No sharp 0-radius, nothing pillowy.**
- **1px hairline borders** everywhere (`--border`). No colored left-accent stripes. No double borders.
- Restrained elevation: `--shadow-sm` for cards, `--shadow-md` only for overlays/popovers/toasts.
- Focus: `outline: 2px solid var(--accent); outline-offset: 2px` (keep shadcn/Radix focus-visible rings).

### Surface "vibe" (optional decorative toggle)
- `calm` (default): flat, no halo. `vivid`: two 520px blurred accent halos at top-left / bottom-right at ~16% opacity + a faint accent tint on cards (`--surface-tint`). Never a gradient fill or image as a background.

---

## Screens / Views

### 1. App Shell (`shell.jsx`, assembled in `app.jsx`)
- **Layout:** fixed left **sidebar 230px** (`--surface` bg, 1px right border) + main column (sticky **top bar ~58px** with blur backdrop, then scrollable content with 24px padding). Two fixed blurred halo orbs sit behind everything (only visible in `vivid`).
- **Sidebar:** brand mark + "EMaigrator / Email migration"; nav grouped under uppercase labels **OVERVIEW** (Dashboard), **MIGRATE** (New migration, Live run — Live run shows a pulsing accent dot), **MANAGE** (Batches w/ count badge "3", Results & audit). Active item = `--accent-subtle` bg + `--accent` text. Hover = `--surface-2` bg. Below nav: a **"VIEWING AS" persona segmented control** (Just me / Admin), then Settings, then a user footer (avatar + name + org) that swaps with persona (Harold Conway / Conway Law ↔ Tom Mercer / BrightStack MSP).
- **Top bar:** page title (`--fs-h2`) + subtitle (`--fs-xs` muted), right-aligned page actions, then a divider, a **density segmented** (Comfortable/Compact icons), and a **theme segmented** (Light / Dark / System icons).
- **Brand mark:** inline SVG in `shell.jsx` (`Logo`) — a rounded teal square with an envelope outline; favicon/thumbnail uses the same. No external logo asset.

### 2. Dashboard (`dashboard.jsx`)
**Individual variant** (`IndividualDashboard`): centered max-width 720px. One large **hero card**: source→destination provider route (colored icon chips + arrow), a `running` status chip, a big `XX% done` number, a striped `Progress` bar, mono "2,310 / 3,201 messages" + "about 18 min left", a **reassurance panel** (accent-subtle bg, shield icon: "You can keep using your old inbox…"), and two actions ("See live progress" primary, "Pause" outline). Below: two cards — "What we've moved so far" (folder list with status icons) and "Need a hand?" (plain copy + "How migration works").

**Admin/MSP variant** (`MspDashboard`): full-width grid.
- KPI row (4 cards): Active migrations, Mailboxes done (+delta), Throughput (msg/min, −delta when throttling), Needs attention.
- Row: **Throughput area chart** (with shaded "throttle window") + **donut breakdown** (Migrated/Running/Throttled/Queued/Failed) with a legend.
- "Mailboxes in flight" card: compact rows (name/email, progress bar, rate, status chip).
- "Needs your decision" card: error/warning mailboxes with Resolve buttons.
- **`dashLayout` tweak:** `cards` (above) vs `list` (KPIs collapse to one inline stat strip; throughput goes full-width; donut hidden).

### 3. Migration Wizard (`wizard.jsx`)
Gated 5-step flow: **Source → Destination → Test → Review → Start.** Back always allowed; Continue disabled until the step is valid (Test requires a successful test). Three interchangeable chrome patterns via the `wizardPattern` tweak:
- `stepped` (default): horizontal numbered stepper across the top, content in a card, Back/Continue footer.
- `siderail`: vertical step list (label + description) on a 220px left rail, content on the right.
- `single`: all five sections stacked on one scroll with a sticky progress bar.

Step bodies:
- **Source / Destination:** provider picker grid (colored icon chip + short name + protocol; selected = accent ring) → credential form that adapts to the provider's auth: OAuth ("Sign in with…" button), basic (password), or app-password (mono `xxxx-xxxx` field + "How to create one →"). IMAP/EWS providers also show server host + port (mono).
- **Test:** "Test connection" → spinner → **success Alert** (mono bullet list of what was found) or **failure Alert** (amber, plain-language cause + "How to" link + a **Collapsible "Technical details"** revealing mono raw IMAP error + `trace:` id). "Simulate failure" button demonstrates the error path.
- **Review:** accordion of grouped issues (Folder mapping / Large mailboxes / Authentication), each with a severity chip, count badge, mono item list, and a **resolution `Select`** (default option is the safe one).
- **Start:** recap card (source, destination, what to migrate, est. time, "source left untouched") + info Alert. Primary button starts the migration → toast + navigates to Live Run.

### 4. Live Run (`runprogress.jsx`)
Simulated **SignalR streaming**: counts ascend on an interval, throughput series rolls, a periodic **throttle window** flips the run to amber. A **connection pill** shows `Live` (pulsing green dot) or `Reconnecting…` (amber, wifi-off); the top-bar "Simulate drop" action triggers a reconnect cycle. Pausing stops the stream.
- **Individual:** centered hero (route + connection pill, big %, striped progress, throttle chip when active, ≈msg/min + ETA, Pause/Resume) + a **Folders** card (per-folder progress with done/total mono and status icons; spinner on the in-flight folder).
- **Admin:** a throttle banner when rate-limited; a batch summary card (name, route, started; stat row Complete/Messages/of total/Rate; overall progress); a row with a **live throughput area chart** + a **status breakdown** (per-status mini bars); and a "Mailboxes in flight" table (name/email, progress, rate, size, status chip).

### 5. Batches (`batches.jsx`)
Admin-only. Batch summary card (name, route, mailbox count, Done/Running/Issues counts, "Open live view"). Toolbar: search input + **status filter pills** (each with a count) + Export CSV. Then a full **mailbox table**: Mailbox (name + mono email; inline mono error in red for failed/warning rows), Progress bar, Messages, Size, Rate, Status chip, row actions (⋯). Empty state when a filter matches nothing. Row hover tint; respects density vars.

### 6. Results & Audit (`audit.jsx`)
Admin-only. Three summary KPI cards (Mailboxes completed, Messages migrated, Errors logged). **Tabs** (All / Completed / Decisions / Errors, each with counts). **Export dropdown** (CSV / JSON / PDF / Email to client). Event list: each row = mono timestamp, level icon (info/success/warning/error), event title + mono mailbox id, detail line, and for errors a **Collapsible "Technical details"** with mono raw error + host + `trace:` id.

### 7. Settings (`app.jsx` → `Settings`)
Centered. "Appearance" card mirrors the Tweaks (Theme / Density / Surface segmenteds). "Notifications" card with toggle rows (Migration completed, Decision required, Throttling started, Weekly summary).

---

## Interactions & Behavior
- **Navigation:** client-side route state (`route`), persisted to `localStorage` (`em-route`). Map to the codebase's router.
- **Persona / theme / density / vibe / accent / layouts** are global state. Theme `system` resolves via `matchMedia('(prefers-color-scheme: dark)')` and reacts to changes. Applying a theme toggles `data-*` attributes on `<html>` and sets accent CSS vars.
- **Theme-swap flash guard:** a `.theme-anim-off` class is added to `<html>` for one frame on theme/density/accent change to suppress transition interpolation of color tokens. Segmented controls deliberately do **not** transition `background` (only color + box-shadow) for the same reason — preserve this when porting.
- **Animations:** purposeful/subtle, ~120–250ms ease-out. Striped progress bars while running; pulsing dots for live/running; bounce-in toasts; collapsible chevron rotation; skeleton pulse for loading. **Respect `prefers-reduced-motion`** (the stylesheet zeroes durations) — keep this.
- **Streaming:** Live Run uses `setInterval` to mock SignalR. In production, subscribe to the SignalR hub; design for out-of-order/late events and reconnect.
- **Validation:** wizard gates Continue on per-step validity (email present, test passed).

## State Management
Global (lift to context / store): `route`, `persona` (individual|msp), `theme` (light|dark|system) + resolved theme, `density` (comfortable|compact), `vibe` (calm|vivid), `accent` (teal|emerald), `dashLayout` (cards|list), `wizardPattern` (stepped|siderail|single), live-run `conn` (live|reconnecting) and `running`.
Wizard-local: selected providers + addresses + credentials, `test` (null|testing|ok|error), open accordion issue, per-issue resolution map, current step index + max-reached step (for gating).
Data fetching (production): summary/KPIs, batches + mailbox lists (paginated/filterable), audit events (filter by level, exportable), connection test, issue detection. All need skeleton + error states.

## Assets
- **No external image assets.** The brand mark is an inline SVG in `shell.jsx`; provider "logos" are colored rounded squares with a Lucide mail glyph (swap for real provider logomarks in production).
- **Icons:** a Lucide-style inline set in `icons.jsx`. In production use `lucide-react` directly at size 14–17, `currentColor`, stroke width 2.
- **Charts:** lightweight inline-SVG `ThroughputChart` (area + hover tooltip + throttle shading) and `Donut` in `charts.jsx`. Replace with **Recharts** (or the codebase's chart lib) in production.

## Files
All under this handoff folder (mirrors the prototype):
- `EMaigrator.html` — entry; loads scripts in dependency order.
- `styles.css` — **all design tokens** (themes, density, vibe), base styles, keyframes. Start here.
- `app.jsx` — root: routing, global state, theme/density/accent application, Tweaks panel wiring, Settings.
- `shell.jsx` — sidebar, top bar, brand `Logo`, theme/density toggles.
- `ui.jsx` — primitives: `Button`, `Card`/`CardTitle`, `StatusChip` (+ `STATUS` map), `Badge`, `Field`/`Input`/`Select`, `Segmented`, `Progress`, `Spinner`, `Skeleton`, `Tabs`, `Collapsible`, `Toast`. **Maps almost 1:1 to shadcn/ui** (Button, Card, Badge, Input, Select, ToggleGroup, Progress, Tabs, Collapsible, Toast).
- `icons.jsx` — icon set (replace with lucide-react).
- `charts.jsx` — throughput + donut (replace with Recharts).
- `data.jsx` — mocked domain data (providers, the individual live run, the MSP batch + mailboxes, review issues, audit events, throughput series). Use as fixture shapes for the real API.
- `dashboard.jsx`, `wizard.jsx`, `runprogress.jsx`, `batches.jsx`, `audit.jsx` — the surfaces.
- `tweaks-panel.jsx` — prototype-only tweak panel; **do not port** (it exists to toggle design variations during review).

> **Tweaks are a prototyping affordance, not a product feature.** The product should expose Theme (and likely Density) in Settings; Persona is determined by the authenticated account type; Dashboard layout and Wizard pattern are design decisions to lock, not user toggles — pick one of each per the product direction.
