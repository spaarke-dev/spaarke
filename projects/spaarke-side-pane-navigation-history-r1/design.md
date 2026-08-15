# Design — Side-Pane Navigation & Quick-Access (Navigation History) — r1

> **Status**: DESIGN COMPLETE — ready for `/design-to-spec` (owner-approved 2026-08-12)
> **Architecture direction**: **PATH B — global always-available docked pane with
> continuous capture** (owner decision 2026-08-12), building on the recovered
> `SidePaneManager` reliable Code Page-injection bootstrap. Path A (contextual
> launch) is the documented rejected alternative.
> **Author**: owner + Claude, 2026-08-12
> **Project slug**: `spaarke-side-pane-navigation-history-r1`
> **Naming convention established here**: future side-pane features follow
> `spaarke-side-pane-{feature}-r{n}` and register as contributors to the SAME
> side-pane host framework this project builds (see §4).

---

## 1. Problem / User Need

A frequent Spaarke user request: **"let me get back to what I was just looking
at — quickly, from anywhere, without hunting."** Today users lose their place
navigating between matters, projects, documents, and views inside the
model-driven app (MDA) shell, and there is no cross-device memory of where they
have been or what they care about.

Four distinct jobs hide inside this request, split by **who creates the entry**:

| Need | Capture | Lifecycle | Curated by user? |
|---|---|---|---|
| **Browse history** ("what did I look at / change") | Automatic (passive) | Ephemeral, self-pruning | No — never hand-edited |
| **Pinned / bookmarked** (records + pages) | Explicit | Persistent | Yes |
| **Monitored / tracked records** | Explicit | Persistent | Yes — **= Pinned** (see §6) |
| **Saved views / queries** | Already exist in Dataverse | Persistent | Yes (aggregated, not created) |

**Core UX constraint (the whole point):** the feature **cannot be buried below
clicks.** It must be one gesture away from anywhere in the app.

---

## 2. Goals

- G1 — A single, **always-available** surface to reach Recent, Pinned, and Views
  from anywhere in the MDA, without leaving the current page.
- G2 — **Cross-device / cross-session** memory (server-side, not browser-local).
- G3 — **Zero form-authoring burden**: capture history without adding OnLoad/
  OnSave handlers to each form, and **no Dataverse plugins** (both are standing
  Spaarke constraints).
- G4 — **Effortless, non-intrusive pinning** — pin/unpin in one gesture, reusing
  the existing `monitor` flag (`TrackingFieldTrio`), not a new UI paradigm.
- G5 — Built as a **reusable side-pane host + registry** so future "quick-access"
  features (AI quick-chat, clipboard, notifications tray, Cmd-K switcher, …) are
  a new registry entry + component, not a new pane.
- G6 — **Security-trimmed**: never surface a record's name/link the user can no
  longer access (critical in a legal context — ethical walls / need-to-know).

## 2a. Non-Goals (explicit scope fence — resist over-extension)

- N1 — **NOT** "notify me when this record changes." Tracked = *keep handy* only.
  Change-notification is a separate, valuable feature that fuses with the
  notification spine later — do **not** couple this nav utility to a
  subscription engine now.
- N2 — **NOT** a field-level audit viewer ("who changed what field on this
  record"). The `audit` entity route is explicitly rejected (§7). "Recently
  edited by **me**" is derived cheaply from `modifiedon`/`modifiedby`, not audit.
- N3 — **NOT** a team activity feed ("everything everyone changed"). Scope is
  **me and my activity**.
- N4 — **NOT** rebuilding saved views. We *aggregate* existing `userquery`
  records; we do not create a new query builder.
- N5 — **NOT** a browser add-in, and **NOT** integration with the *browser's*
  bookmark system (IT blocking + weak Dataverse context — see §3). We DO provide
  manual URL bookmarks **inside our own cross-device store** (§6b) — that is the
  point, just not riding the browser's bookmarks.
- N6 — Standalone external SPA (external-access) surfaces are **out of scope** for
  capture in r1 (they run outside the MDA shell — see §8 scope boundary).

---

## 3. Prior Art / "What are we missing?" (competitive scan)

The single most important finding: **model-driven apps already ship a
"Recently viewed" + "Pinned" flyout** in the site map (the clock icon; pin a
record to keep it in the recents flyout). Per Spaarke §11 (default to reuse),
this project MUST justify itself against that native feature rather than ignore
it. **We have tried the native feature in production and it does not meet user
expectations — that failure is the reason this project exists.** Our
differentiation (all real gaps in the native feature):

| Native MDA Recent/Pinned | This project |
|---|---|
| Per-app, **client-side MRU**, not reliably cross-device | Server-side (`sprk_navitem`), cross-device (G2) |
| **Records only** | Records **+ pages + views** in one surface |
| No "edited by me" lens | Viewed **and** edited toggle (§7) |
| No saved-view aggregation | Views tab aggregates `userquery` (§6) |
| Buried in a **flyout** that closes on navigate | **Docked** side pane, persists across navigation (§4) |
| Not extensible | A reusable quick-access **framework** (G5) |

**Other analogues worth borrowing from (and what each teaches):**

- **Salesforce Lightning — Favorites star + Recent Items + docked Utility Bar.**
  Near-exact analog and strong validation of the whole shape: a star in the
  header favorites the *current* record; a docked utility bar hosts persistent
  quick tools. Confirms "docked, always-available, star-the-current-thing."
- **VS Code — back/forward navigation *stack*** (Alt+←/→ through cursor
  locations). Teaches a distinction we should name: a **history *list*** (scan &
  jump to any prior spot) vs. a **navigation *stack*** (pop the immediately
  previous location). r1 builds the list; a reliable in-app "back to previous
  record" stack is a candidate follow-on (browser Back is unreliable inside the
  MDA SPA shell).
- **Command palette / quick-switcher (Cmd-K / Ctrl-K)** — ServiceNow, Jira,
  Notion, Linear; also the browser address bar and VS Code Ctrl-P. A keyboard
  shortcut opens a search box; type a few letters → fuzzy-match across recent,
  pinned, records, and views → Enter jumps there. *Type-to-jump* is usually
  faster than scanning a list. **DECIDED IN r1 (OQ-4 resolved):** a persistent
  **search box at the top of the Navigator pane** (searches Recent + Pinned +
  Views, and can reach live Dataverse records/views), with a keyboard shortcut
  that focuses that same box as the accelerator. See §5a.
- **ServiceNow / Jira / SharePoint / Notion — Favorites + Recent + Following.**
  Confirm the flat-list-with-rename baseline; grouping/tags/folders are a
  common *phase-2* enhancement, not MVP (note under §6 extensibility).

**Conceptual gaps this scan surfaced (fed into the design below):**

1. **Justify vs native MDA recent/pinned** — done above; belongs in §11 too.
2. **Quick-switch (Cmd-K)** — likely the highest-leverage thing we're "missing";
   captured as OQ-4 rather than silently dropped.
3. **Security trimming + ethical walls** — legal context makes "records I viewed"
   potentially sensitive **and** makes stale links a leak risk → G6 + §9.
4. **List vs stack** — name the distinction; r1 = list, stack = follow-on.
5. **Scope boundary** across MDA / custom pages / external SPA → §8.

---

## 3a. Prior art IN OUR OWN CODEBASE (critical — read before building)

Spaarke has **already built a global, always-available, context-aware side pane**
and **deliberately retired the global version**. This is the most important input
to the design and must be reckoned with, not rediscovered.

**Documentation:**
- **`docs/architecture/SIDE-PANE-PLATFORM-ARCHITECTURE.md`** — a full side-pane
  platform doc. Status: **SUPERSEDED**. It describes a global `SidePaneManager`
  that auto-registered an always-available SprkChat pane via **Code Page
  `<script>` injection into the parent shell `<head>`** (after the hidden
  `Mscrm.GlobalTab` **ribbon enable-rule proved unreliable in current UCI**). That
  whole global auto-injection model was **removed (March 2026)** in favor of
  **contextual launch** (SprkChat embedded in `AnalysisWorkspace`, opened via
  `src/client/code-pages/SprkChatPane/launcher/openSprkChatPane.ts`).
- **`docs/adr/ADR-030-pane-event-bus.md`** — PaneEventBus (cross-pane/widget
  events; used by the SpaarkeAi workspace `widget_load` mechanism).

**Validated mechanics we inherit (de-risks §7):**
- **Polling, not events** — the superseded doc's "Why Polling (Not Events)"
  confirms Dataverse exposes **no navigation event**; `Xrm.App.sidePanes` has no
  `onNavigate`/`onContextChange`. Polling `Xrm.Page.data.entity` (~2s) is the
  sanctioned pattern. **Our §7 capture is a proven mechanism, not a bet.**
- **Persistence knobs** — `canClose: false` (non-dismissable, always in the rail)
  + `alwaysRender: true` (keep React mounted/state when hidden → **this is what
  keeps capture running while the pane is collapsed**).
- **Frame-walk `getXrm()`** — a side-pane/custom-page iframe reaches the shell via
  `window.parent`/`window.top` `.Xrm`.

**Reusable code (extend, don't rebuild — §11):**
| Asset | Location | Reuse |
|---|---|---|
| `SidePaneShell` | `@spaarke/ui-components/src/components/SidePane/` | Head start on `SprkSidePaneHost` (§4) |
| Typed `Xrm.App.sidePanes` iface + `getXrm()` | `@spaarke/ui-components/src/utils/xrmContext.ts` | Pane API + frame-walk |
| `getXrm()` (App.sidePanes-first walk) | `EventsPage/src/xrmHelpers.ts` | Reference |
| `closeSidePane()` / navigate helpers | `EventDetailSidePane/src/services/sidePaneService.ts` | Close + parent-nav |
| `SprkChatBridge` (BroadcastChannel) | `@spaarke/ui-components/src/services/` | Cross-frame events |
| `CalendarSidePane`, `EventDetailSidePane` | `src/solutions/*` | Reference impls (bootstrap + close-on-navigate proven) |

> **Recovered from git (2026-03-26 deletion `7d80565a6`), extracted to
> [`notes/retired-sidepane-code/`](notes/retired-sidepane-code/):**
> - `SidePaneManager.ts` (261 lines) — the global bootstrap **+ a static
>   `PANE_REGISTRY` that IS our `sidePaneRegistry` (§4)**. Idempotent
>   `initialize()`, frame-walk, singleton guard, `createPane().navigate({pageType:
>   'webresource', ...})`. Supports **two load paths**: ribbon enable-rule AND
>   **auto-init-on-load for Code Page injection**.
> - `contextService.ts` (671) — the continuous context-detection **polling loop**
>   (the §7 capture engine, ready to re-adopt).
> - `ContextSwitchDialog.tsx` (182) — "you navigated, switch context?" UX
>   (reference only; Navigator captures silently, no prompt).
>
> **Reliability reality (refines the "fragile" headline):** per the arch doc, the
> **ribbon enable-rule path was the unreliable one; the Code Page injection path
> was reliable** — and both are in the recovered code. The global platform was
> **retired as a PRODUCT decision** (no global embedded AI agent; Calendar went a
> different way), **not** because the bootstrap couldn't be made to work. That
> materially **de-risks Path B.**

### 🔔 Architecture decision this forces — global-docked vs. contextual-launch

Our core premise (always-available docked pane + continuous capture from login)
**IS the pattern the team retired.** The retirement was partly chat-specific
(chat wants document context), and Navigation History is global in a way chat is
not — but the **bootstrap fragility is real and applies to us regardless.**

- **Path A — Contextual/launched pane** (aligns with current direction). User
  opens Navigator from an icon/command (like SprkChat now). **Cost:** capture runs
  only while open, not from login → less-complete history.
- **Path B — Revive the global always-available pane** (original premise).
  Requires the robust startup bootstrap the team found fragile and removed.
  **Higher risk;** the superseded doc is a warning label. Viable only if the spike
  (§13 Task 0) proves a reliable global bootstrap in *current* UCI.

**✅ DECIDED (owner, 2026-08-12): PATH B.** The recovered `SidePaneManager.ts`
already implements a reliable Code Page-injection bootstrap, and the platform's
retirement was product-driven (no global embedded AI agent), not a technical
failure — so Path B is viable. **Task 0 is therefore "re-validate + productionize
the recovered injection bootstrap on the current UCI build," not an A/B decision.**
Path A remains documented as the rejected alternative (fallback only if Task 0
unexpectedly fails). OQ-8 resolved.

---

## 4. Proposed Architecture — a Side-Pane *Framework*, not a pane

Build the reusable surface first; make Navigation History its first tenant.
This mirrors proven Spaarke patterns (`WorkspaceWidgetRegistry`,
`surfaceLaunchRegistry`, the `SprkModal` shell + presets): **shell + registry +
thin contributions.**

- **`SprkSidePaneHost`** (new shared component, `@spaarke/ui-components`) — owns
  the `Xrm.App.sidePanes` plumbing (create pane, icon, badge, open/close, header
  chrome, theming via the scaled Fluent theme, light/dark).
- **`sidePaneRegistry`** — each quick-access feature contributes
  `{ id, icon, title, order, component }`. **One registry entry = one right-rail
  icon = one job-to-be-done.** Splitting one job across icons is an anti-pattern;
  use tabs *within* a contributor for facets (Navigator's 3 tabs), separate
  registry entries for *different* jobs.
- **`NavigatorPane`** (this project) — the FIRST contributor. Internally a
  3-tab surface: **Recent** (Viewed / Edited toggle) · **Pinned** · **Views**.

**Host mechanism:** an **app-level side pane** created via
`Xrm.App.sidePanes.createPane(...)`, which **persists across record navigation**
(its React app is not torn down as the user moves between records). That
persistence is the enabling fact for capture (§8) and for "docked, always
available" (G1). It renders a **custom page** (Power Apps custom page hosting the
code-page bundle), **not a classic HTML web resource** — see ADR Tension §12.

```
Right rail (icons)         Pane body (active contributor)
┌──┐                       ┌───────────────────────────────┐
│★ │  ← Navigator          │ [Recent] [Pinned] [Views]     │
│⚡│  ← (future: Cmd-K)     │  ─ Viewed | Edited ─           │
│💬│  ← (future: AI chat)   │  • Acme Merger — Matter  ↩ ★  │
└──┘                       │  • NDA_v3.docx — Document ↩ ★  │
                           │  • My Open Matters — View  ↩   │
                           └───────────────────────────────┘
```

- **Reuse as a workspace widget:** the Navigator body renders from a shared
  component, so the SpaarkeAi dashboard can host a "Recent & Pinned" widget from
  the *same* component + *same* data (no duplication — §11).

---

## 5. Tabs (3, deliberately trimmed from 4) + a persistent search bar

Pinned and Tracked are **not distinct to the user** — both mean "I said keep this
handy." Folded into one **Pinned**. Final tabs:

1. **Recent** — passive stream, with a **Viewed / Edited** segmented toggle.
2. **Pinned** — user-curated **and per-user** (all from `sprk_navitem`), two
   visual groups: **Records** and **Bookmarks** (pages / views / links), with a
   **"+ Add bookmark"** affordance (§6b). Optionally a third **Monitored** group
   surfaces the shared `sprk_monitor` flag scoped to the user (§6c) — clearly
   distinct from personal pins.
3. **Views** — live aggregation of the user's saved views (`userquery`).

Each row: display name · type chip (Matter / Document / View / Page / Link) ·
click = navigate · inline **star** (pin/unpin) · (Recent only) promote-to-pin.

### 5a. Search bar (the quick-switcher, in r1)

A **persistent search box pinned to the top of the pane**, above the tabs. As the
user types it fuzzy-matches across their Recent + Pinned + Views entries first
(instant, local), and can escalate to a **live Dataverse lookup** (records +
views) when the local set has no hit — Enter navigates to the top result. A
keyboard shortcut focuses this box from anywhere (the "Cmd-K" accelerator). This
is the *type-to-jump* path that complements the scan-a-list tabs.

```
┌───────────────────────────────────────────┐
│ 🔍  Search records, pins, views…          │  ← 5a: persistent, keyboard-focusable
├───────────────────────────────────────────┤
│  [ Recent ]  [ Pinned ]  [ Views ]        │
│  ── Viewed | Edited ──                     │  ← Recent toggle
├───────────────────────────────────────────┤
│  • Acme Merger        Matter      ↩  ★     │
│  • NDA_v3.docx        Document    ↩  ★     │
│  • Kickoff notes      Custom page ↩  ★     │
├───────────────────────────────────────────┤   (Pinned tab shown below)
│  RECORDS                                   │
│  • Acme Merger        Matter      ↩  ★     │
│  BOOKMARKS                        + Add    │  ← 6b: Pin this page / paste URL
│  • My Open Matters    View        ↩  ✕     │
│  • Filing checklist   Link ↗      ↩  ✕     │
└───────────────────────────────────────────┘
```

---

## 6. Data Model & the "Pin" gesture (per-user pins)

> **OQ-1 RESOLVED (2026-08-12): personal pins are per-user, NOT `monitor`.**
> `sprk_monitor` is a single `TwoOptions` field *on the record* — one shared
> value the whole team sees (a "records where monitor=true" query returns the
> same set for every user, trimmed only by each user's access). A boolean on the
> record therefore **cannot** represent "*my* pins." So: **all personal pins live
> in the per-user `sprk_navitem` store** (records + pages + links); `sprk_monitor`
> stays the **shared record-level flag** it already is, surfaced only as an
> optional secondary "Monitored" lens (§6c). The pane star / "Pin this page"
> **always** write `sprk_navitem` and never touch `monitor`.

**(a) All personal pins + the browse stream → per-user `sprk_navitem` entity.**
This one entity (`ownerid`-scoped, private) covers records, pages, views, links,
and history — chosen precisely because pins must be per-user and monitor can't be:

| Field | Purpose |
|---|---|
| `sprk_navitemid` | PK |
| `ownerid` | user scope (per-user, private) |
| `sprk_type` (choice) | `history` / `pin` |
| `sprk_source` (choice) | `captured` / `manual` (manual = user-entered bookmark) |
| `sprk_targetlogicalname` | e.g. `sprk_matter`, or `custompage` (nullable for raw-URL links) |
| `sprk_targetid` | record id (nullable for pages / links) |
| `sprk_pagetype` | `entityrecord` / `entitylist` / `custom` / `weblink` |
| `sprk_url` | raw URL — for manual bookmarks that don't parse to a Dataverse target (§6b) |
| `sprk_displayname` | resolved (or user-supplied) label |
| `sprk_lastvisited` | for ordering + retention |
| `sprk_visitcount` | optional (dedupe/rank) |

- **History rows** are `sprk_type = history`, upserted on visit (§8), self-pruned
  (retention: keep last ~50 per user or 30 days — prune on write or nightly via
  an existing scheduled mechanism; **no plugin**).
- **Page/view pins** are `sprk_type = pin`.
- Read/written **directly via host-context `Xrm.WebApi`** from the pane — **no BFF
  endpoint** (personal, single-entity CRUD; per `DATA-ACCESS-DECISION-CRITERIA.md`
  this is exactly the host-context case). ⇒ **BFF untouched** (see §10/§11).

**Views tab** = live `Xrm.WebApi.retrieveMultipleRecords('userquery', owner=me)`,
grouped by `returnedtypecode`. Click → `Xrm.Navigation.navigateTo({ pageType:
'entitylist', entityName, viewId })`. Pin favorites (system `savedquery` views
are opt-in / pin-only to avoid noise). **No new query storage** for this tab.

### 6b. Bookmarks — for pages/links a `monitor` flag can't cover

`sprk_monitor` only reaches records that carry the flag. For "just get me back to
this page/view/link," bookmarks fill the gap, with **two gestures** (both write a
`sprk_type = pin` `sprk_navitem`):

1. **"Pin this page" (primary, one click)** — captures the *current* page from
   `Xrm.Utility.getPageContext()`; no typing. `sprk_source = captured`, stored as
   a logical target (etn / id / pagetype) so it gets a clean label, survives
   app-id changes, and can be security-trimmed (§9).
2. **"+ Add bookmark" (manual — matches the bookmark-bar habit)** — the user
   pastes, drags, or types a URL. `sprk_source = manual`. We **parse it when
   possible**: an MDA URL yields `etn` / `id` / `pagetype` / `viewid` → stored as
   a logical target (resilient, labeled, security-trimmed). If it **doesn't
   parse** (external site, SharePoint doc, odd form), we store the **raw
   `sprk_url` verbatim** with a user-supplied label and open it in a **new tab**
   (`sprk_pagetype = weblink`). This deliberately lets users keep **non-Dataverse
   links** in the same quick-access surface.

Clicking a bookmark replays parsed navigation (`Xrm.Navigation.navigateTo`) when
it has a logical target, else opens `sprk_url`. This is the ADR-compliant
alternative to browser bookmarks (§2a N5): the store is *ours* (cross-device),
not the browser's.

### 6c. Monitored lens (shared flag — OPTIONAL, kept distinct from pins)

`sprk_monitor` stays what it already is: a **shared, record-level** flag. If we
surface it in the Navigator at all, it is a **separate "Monitored" group**,
never merged into personal pins, and scoped to the user (`monitor = true AND
owned-by / assigned-to me`) so one person's list isn't the firm-wide monitored
set.

**Shared-flag semantics the user must accept for this lens (by design, not a
bug):**
- Setting `monitor` marks the record **for everyone** — it is not "my" pin.
- **Another user unchecking `monitor` clears it for everyone**, so it can
  disappear from your Monitored group even though you never touched it
  (last-writer-wins; no per-user intent). Anyone with write access can toggle it
  for the whole team.

Because of these semantics, Monitored is explicitly **not** a substitute for
personal pins (§6a) — it answers "is this record flagged for the team?", a
different question. **Open (OQ-1b): include the Monitored lens in r1 at all, or
drop it** and let per-user pins cover the "get back to it" need entirely? Lean:
ship personal pins first; add Monitored only if users still want the shared
signal in this surface.

> **Extensibility (phase 2, not MVP):** folders/tags on pins; a
> `sprk_querydefinition` JSON field only if genuinely cross-entity/workspace
> queries are needed (none today → field omitted for now).

---

## 7. Capture WITHOUT form web resources or plugins (the key technical move)

Two moves remove the per-form OnLoad/OnSave burden entirely (G3):

**Recent (Viewed) — captured by the persistent pane, not by forms.**
Because the app-level side pane persists and keeps running across navigation, the
pane itself observes the current page via `Xrm.Utility.getPageContext()`
(returns `entityName` / `entityId` / `pageType`). On change → upsert a
`history` `sprk_navitem`. This is *our* code in *our* pane — **zero form
handlers, works across every entity automatically, no per-form registration.**
Display-name for a newly-seen record resolved with one cached
`Xrm.WebApi.retrieveRecord`.

**Edited — derived, not captured.** Instead of an OnSave hook, query
`modifiedby eq me & order by modifiedon desc` across the core business entity set
(matter, project, document, todo, event, communication, …), merged in the pane.
Read-only, no hooks, no audit entity, no plugin. **More** accurate than a save
hook (survives edits made via flows / bulk / other apps). "Recently created" is
naturally included (create sets `modifiedon`).

**Context model — pull-based, nothing to "release."** The pane is not *pushed* a
navigation event; it *pulls* the current page via `getPageContext()` on a poll
(~0.5–1s) and treats "current page" as **derived state recomputed each poll**,
never cached as authoritative. Navigating from Matter 1234 → Project 5678 just
means the next poll overwrites the pane's notion of current page; there is no
lock/handle on 1234 to release. Leaving records entirely (dashboard/list) sets
current-record to null. This guarantees no "stuck on the previous record" leak.

**Pane lifecycle — persists across navigation (by design).** As an *app-level*
pane (`Xrm.App.sidePanes.createPane`), it does **NOT** close when the user
navigates between records — it stays alive (that's why we chose app-level over
form-level). **Collapsing** it (rail-icon toggle) hides the body but keeps the
iframe running, so **capture continues even while the pane is collapsed**. It is
destroyed only on explicit close, `pane.close()`, or a full app reload / session
end.

**Bootstrap cost (the one honest caveat to "no web resources").** Because a full
app reload destroys panes, the pane must be **(re)created once at app startup**
for capture to run from login. There is no per-form handler and no plugin (both
stay zero), but there is **no fully hook-free way to auto-create an app-level
pane** — the realistic mechanism is **one global app-startup bootstrap (a single
app-scoped web resource or equivalent)**, distinct in kind and footprint from the
per-form handlers we avoid. Alternative (zero web resources): create the pane
only when the user first opens it, accepting that capture starts at first-open
each session and misses earlier visits. **Lean: one global bootstrap for
continuous capture.** See OQ-8.

> **De-risk first (see §13):** confirm on the target MDA version that the
> app-level side pane keeps its JS alive across navigation (and while collapsed),
> that `getPageContext()` polling reliably observes record visits, and the
> cleanest way to auto-create the pane at app startup (OQ-8). This is the one
> genuine unknown; everything else is well-trodden.

**Explicitly rejected:** the `audit` entity (heavy, per-entity enablement,
permission-gated, storage cost, drifts toward N2/N3). Not used.

---

## 8. Surfaces & Scope Boundary

- **Primary:** app-level right side-pane (docked icon), the `NavigatorPane`
  contributor. Solves G1.
- **Secondary (reuse):** SpaarkeAi dashboard "Recent & Pinned" widget from the
  same shared component.
- **Optional (phase 2):** a left-nav link to a full-page management view (bulk
  organize pins, purge history) — the pane is intentionally small.

**Scope boundary (what capture covers):**
- ✅ MDA record pages, entity lists, and **custom pages** opened inside the MDA
  shell (`getPageContext` reports these).
- ⚠️ Code pages hosted as custom pages inside MDA → captured as
  `pagetype = custom` (name resolution best-effort).
- ❌ Standalone external SPA (external-access) → **out of scope r1** (N6): runs
  outside the MDA shell, no `Xrm.App`. A future contributor could post nav
  events from that SPA to `sprk_navitem` via BFF if ever needed.

---

## 9. Security, Privacy, Retention (legal-context sensitive)

- **Read-time security trimming (G6):** history/pins store ids + a cached name,
  but on render the pane MUST re-validate access (the click navigates through the
  platform, which enforces security; the *label* is the leak risk). If the user
  has lost access (ethical wall, role change), the row is hidden or shown as
  "(no longer available)" without the cached name. Re-check via a lightweight
  retrieve; drop rows that 404/403.
- **Per-user isolation:** `sprk_navitem` is `ownerid`-scoped; users see only
  their own history/pins (standard Dataverse ownership + security roles).
- **Ethical walls / conflicts (documented, not a blocker):** "records I viewed"
  can in principle be sensitive in legal ops. History is per-user and never a
  team feed (N3), and read-time trimming (above) already prevents stale-access
  leaks — so the owner is **not treating this as a gating concern for r1**. Noted
  here for completeness; revisit only if a specific compliance requirement
  surfaces.
- **Retention:** cap per-user history (last ~50 or 30 days); prune on write or a
  scheduled job — **no plugin**. Pins never auto-expire.
- **No secrets, no new auth surface** — all reads/writes are host-context
  `Xrm.WebApi` under the signed-in user (no BFF token, no new scope).

---

## 10. Placement Justification (§10 BFF Hygiene)

**This project adds NO code to `Sprk.Bff.Api`.** All data access is personal,
single-entity CRUD executed **host-context via `Xrm.WebApi`** from the side-pane
code page (`sprk_navitem`, `userquery`, `sprk_monitor` flips, security-trim
retrieves) — the textbook host-context case in
`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`. No BFF endpoint, service, DI
registration, package, or background job is introduced. ⇒ Publish-size,
CVE, and BFF-test obligations are **N/A**.

```xml
<hot-path-declaration>
  <bff>N</bff>                <!-- no Sprk.Bff.Api changes -->
  <spaarkeai>N</spaarkeai>    <!-- new solution/code page; not src/solutions/SpaarkeAi -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

> If de-risking (§13) forces a BFF path (e.g., external-SPA capture, or a
> server-side retention job that can't be scheduled otherwise), this declaration
> and §10 obligations must be revisited before that code lands.

---

## 11. Component Justification (§11 Default to Reuse)

**New surfaces this project introduces**, each run through the three-question
template:

- **`SprkSidePaneHost` + `sidePaneRegistry` (extend, likely not net-new)**
  1. *Existing overlap?* **Yes — `SidePaneShell`** (`@spaarke/ui-components/
     src/components/SidePane/`) already exists, plus the typed `Xrm.App.sidePanes`
     wrapper + `getXrm()` frame-walk. `SprkModal` covers modals, not docked panes.
  2. *Extend instead?* **Yes — extend `SidePaneShell`** with the registry/host
     role rather than build fresh (OQ-9 confirms depth). The registry seam is the
     genuinely new part future `spaarke-side-pane-*` projects extend.
  3. *Cost of doing nothing?* Every future quick-access feature re-implements
     pane plumbing → divergent panes, the exact failure §10/§11 exist to prevent.
     (The retired `SidePaneManager` is the cautionary example — §3a.)
- **`sprk_navitem` (new entity)**
  1. *Overlap?* Native MDA MRU (client-side, not queryable/cross-device) and
     `sprk_monitor` (record-scoped, records-only). Neither stores a per-user,
     cross-device browse stream or page/view pins.
  2. *Extend instead?* `sprk_monitor` can't hold pages/views/history and isn't
     per-user; native MRU isn't server-side. Genuine gap.
  3. *Cost of doing nothing?* No cross-device history, no page/view pins — the
     core user need (§1) fails.
- **`NavigatorPane` contributor** — the feature itself; extends the host.

**Reused, NOT rebuilt:** `sprk_monitor` / `TrackingFieldTrio` (pin state for
records), `userquery` (saved views), `Xrm.WebApi` (data access), `SprkModal`
patterns (registry shape precedent), the SpaarkeAi widget framework (secondary
surface). **Native MDA Recent/Pinned** is explicitly justified-against in §3.

---

## 12. ADR Tensions

- **ADR-006 (PCF over web resources) + "minimize form web resources" standing
  rule.** We deliberately do NOT add per-*form* OnLoad/OnSave handlers (that's the
  win of §7). The pane **body** is a **code-page bundle hosted as a `webresource`
  pane** (exactly like the shipping `CalendarSidePane` / the recovered
  `SidePaneManager` — `pageType: 'webresource'`), which is standard and ADR-fine —
  a code page shipped as a web resource is not a "classic HTML web resource," and
  is not a per-form handler. The only global JS is the **one bootstrap** (Path B)
  injected once at app load. Likely **Path C (comply)**; flag for `adr-check`.
- **ADR-022 (PCF platform libraries React 16/17).** If any part ships as a PCF
  (e.g., an on-form entry point), honor React-16 APIs; but the primary surface is
  a code page (React 19). Watch the shared-lib React-version drift
  (`@spaarke/ui-components`) called out in memory
  `feedback_shared-lib-react-version-tension`.
- **Capture-by-polling is NOT novel here** — it was the sanctioned mechanism in
  the retired `SidePaneManager` platform (§3a; "Why Polling (Not Events)"). Re-adopt
  and document it as the standard so future side-pane features reuse it.
- **ADR-030 (PaneEventBus)** — reuse for any cross-pane / pane↔widget events
  (e.g., the SpaarkeAi dashboard-widget secondary surface), rather than a new bus.
- **Superseded-platform lesson (§3a)** — the global auto-injection bootstrap was
  retired as fragile. Reviving it (Path B) is a conscious, escalated decision
  (OQ-8 / root §6.5), not a default. Path A stays ADR-clean with no bootstrap.

---

## 13. Phasing / De-risking

- **Task 0 (SPIKE — do FIRST):** re-validate + productionize the recovered
  **Code Page-injection bootstrap** (`notes/retired-sidepane-code/SidePaneManager.ts`)
  on the *current* UCI build — confirm the pane auto-creates at app load reliably.
  Adopt its static `PANE_REGISTRY` shape as `sidePaneRegistry` (§4). (Ribbon
  enable-rule path is NOT used — it was the unreliable variant.) Only if this
  unexpectedly fails on current UCI do we fall back to Path A.
- **Task 1 (SPIKE):** confirm the pane persists across navigation (with
  `alwaysRender: true`, incl. while collapsed) and that polling
  (`Xrm.Page.data.entity` / `getPageContext()`, ~1–2s) reliably captures record
  visits on the target MDA build. (Mechanism validated in the retired platform —
  re-confirm on current build.)
- **Task 2:** `SprkSidePaneHost` + `sidePaneRegistry` (framework).
- **Task 3:** `sprk_navitem` entity + retention.
- **Task 4:** `NavigatorPane` — Recent (Viewed via capture).
- **Task 5:** Recent (Edited via `modifiedby=me` derivation) + Pinned (record
  `monitor` reuse + page/view pins).
- **Task 6:** Views tab (`userquery` aggregation).
- **Task 7:** Security trimming (G6) + retention verification.
- **Task 8 (reuse):** SpaarkeAi dashboard widget from the same component.
- **Task 5b:** Bookmarks (§6b) — "Pin this page" + "+ Add bookmark" (URL
  parse-when-possible / raw-`weblink` fallback).
- **Task 6b:** Search bar / quick-switcher (§5a) — local fuzzy-match across
  Recent/Pinned/Views + escalation to live Dataverse lookup + keyboard focus.
- **Phase 2 (separate):** full-page management view, folders/tags on bookmarks,
  "back to previous record" navigation **stack** (§5 list-vs-stack), external-SPA
  capture, change-notification fusion (N1).

---

## 14. Open Questions (resolve during `/design-to-spec`)

- **OQ-1** — *(RESOLVED — per-user `sprk_navitem`; §6.)* Personal pins are
  per-user, not the shared `sprk_monitor` flag (a boolean on the record is shared
  and any user can clear it).
- **OQ-1b** — Include the optional shared **Monitored** lens (§6c) in r1, or drop
  it and let per-user pins cover "get back to it" entirely? (Lean: defer.)
- **OQ-2** — Retention policy exact numbers (count vs age; prune-on-write vs
  scheduled) and where the scheduled prune runs given "no plugin."
- **OQ-3** — *(Resolved — documented, not a blocker; §9.)* Ethical-wall/retention
  is noted for completeness; not gating r1 unless a specific requirement surfaces.
- **OQ-4** — *(Resolved — YES, in r1; §5a.)* Search/quick-switcher ships as a
  persistent top-of-pane search box with a keyboard-focus accelerator.
- **OQ-5** — Core entity set for the "Edited by me" derivation (which entities;
  is a Dataverse relevance-search call better than N per-entity queries?).
- **OQ-6** — Custom-page name resolution for `pagetype = custom` history rows.
- **OQ-7** — URL-parse coverage for "+ Add bookmark": which MDA URL shapes do we
  parse to a logical target vs. store raw as `weblink`? (§6b)
- **OQ-8 (RESOLVED — Path B, owner 2026-08-12)** — Global-docked, continuous
  capture, via the recovered injection bootstrap. Path A rejected (kept as
  fallback only). Task 0 now re-validates the bootstrap, not the decision. (§3a)
- **OQ-9** — Reuse depth of `SidePaneShell` vs. a new `SprkSidePaneHost` — does
  the existing shell already cover the registry/host needs (§4), or is it
  presentational-only? (Verify in spec.)

---

## 15. Acceptance (draft — closed set to be finalized in spec)

- A user, from any MDA page, opens the docked Navigator in one click and sees
  their Recent (Viewed) list populated by navigation with no form code deployed.
- Toggling Recent → Edited shows records the user recently modified, derived from
  `modifiedon`, with no audit entity involved.
- Starring a record in the pane creates a **per-user** `sprk_navitem` pin; it
  appears under the user's Pinned list and is **unaffected by any other user**
  (another user toggling `sprk_monitor` does not add/remove it). The shared
  `sprk_monitor` flag and its on-form toggle are independent of personal pins.
- Pinning a page/view stores a per-user `sprk_navitem`; it survives sign-out and
  appears on another device.
- "Pin this page" bookmarks the current page in one click (no typing); "+ Add
  bookmark" accepts a pasted URL — an MDA URL is parsed to a labeled logical
  target, a non-Dataverse URL is stored raw and opens in a new tab.
- The top-of-pane search box fuzzy-matches Recent/Pinned/Views as the user types
  and, on no local hit, finds a live Dataverse record/view; a keyboard shortcut
  focuses the box from anywhere; Enter navigates to the top result.
- The Views tab lists the user's saved views grouped by entity; clicking opens
  the entity grid with that view selected.
- A record the user has lost access to does not display its cached name in
  history/pins.
- A second (stub) contributor can be registered against `SprkSidePaneHost` with
  only `{ id, icon, title, component }` — proving the framework (G5).
- No changes to `Sprk.Bff.Api`; no Dataverse plugin; no per-form web resource.
```
