# Implementation Plan — Side-Pane Navigation & Quick-Access (Navigation History) — r1

> **Source**: [`spec.md`](spec.md) · **Design**: [`design.md`](design.md)
> **Generated**: 2026-08-13 by `/project-pipeline` Step 2/3
> **Execution model**: Sonnet 5 @ `high` default (spike = Opus @ `xhigh`); INITIALIZE-ONLY — execution owner-gated wave-by-wave.

---

## 1. Approach

Build the reusable side-pane **framework first**, make Navigation History its first tenant, and prove extensibility with a stub contributor. Everything is host-context `Xrm.WebApi` — **no BFF, no plugin, no per-form web resource**. The one honest cost is a single app-startup bootstrap (Path B), gated by a spike (Task 001) before any framework code is committed.

**Reuse is high** (see Discovered Resources): the pane host is a generalization of an existing orchestrator, the capture loop is recovered code, and the data layer mirrors an existing `Xrm.WebApi` CRUD hook.

## 2. Architecture Context — Discovered Resources (pipeline Step 2)

### Applicable ADRs
| ADR | Relevance | Resolution |
|---|---|---|
| **ADR-006** (PCF over web resources) | Pane body ships as a Vite code page hosted as a `pageType:'webresource'` pane (CalendarSidePane pattern) | Path **C** (comply) — not a classic HTML web resource, not a per-form handler; flag for `adr-check` |
| **ADR-021** (Fluent v9 / dark mode) | All pane UI: tokens only, portal `FluentProvider` re-wrap, code-page theme detection, `--sprk-ui-scale` | Comply |
| **ADR-022** (PCF platform libs React 16/17) | `@spaarke/ui-components` code must stay React-16/17-safe (no `createRoot`, `JSXElement` not `JSX.Element`); code page itself is React 19 | Path **C** |
| **ADR-030** (PaneEventBus) | Not needed r1 (dashboard widget deferred); noted for phase-2 reuse | N/A r1 |
| Superseded side-pane platform | Reviving the retired global bootstrap (Path B) | Path **A** — owner-approved 2026-08-12 (retirement was product-driven, not technical) |
| **ADR-038** (testing) | .NET-only by charter; the *philosophy* (behavior not scaffolding) applies to Jest tests; `/test-diet` at close | Comply |

### Canonical implementations to reuse (do NOT rebuild — §11)
| Asset | Path | Reuse as |
|---|---|---|
| `DataGridSidePaneOrchestrator` | `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/sidePane/DataGridSidePaneOrchestrator.ts` | **Generalize into `SprkSidePaneHost`** — already does createPane, `mutuallyExclusiveWith`, `attachVisibilityLifecycle` (IntersectionObserver close-on-navigate), unload cleanup |
| `SidePaneShell` | `src/client/shared/Spaarke.UI.Components/src/components/SidePane/SidePaneShell.tsx` | **Wrap** for pane chrome (presentational-only) |
| `xrmContext.ts` | `src/client/shared/Spaarke.UI.Components/src/utils/xrmContext.ts` | Reuse + **widen** (`getPane`/`pane.select`, `webresourceName`, 3-frame `getXrm`) |
| `WorkspaceWidgetRegistry` | `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts` | Registry ergonomics precedent (Map + lazy factory + metadata) |
| `surfaceLaunchRegistry` | `src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/surfaceLaunchRegistry.ts` | Code-owned static-table precedent |
| `useSprkMemoRepository` | `src/solutions/Notepad/src/hooks/useSprkMemoRepository.ts` | Closest `Xrm.WebApi` CRUD analog for `sprk_navitem` |
| `eventService.ts` | `src/solutions/EventDetailSidePane/src/services/eventService.ts` | Side-pane CRUD (scalar vs `@odata.bind` PATCH split) |
| `ViewService.ts` | `src/client/shared/Spaarke.UI.Components/src/services/ViewService.ts` | Views tab — `userquery`/`savedquery` (FR-10) |
| `CalendarSidePane` / `EventDetailSidePane` | `src/solutions/*` | Vite code-page-as-`webresource`-pane reference |
| `scaledTheme.ts` + `useUiScale.ts` | `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/` + `src/hooks/` | `--sprk-ui-scale` (NFR-07) |
| Recovered code | `notes/retired-sidepane-code/` | `SidePaneManager.ts` (PANE_REGISTRY + bootstrap), `contextService.ts` (2s poll + sessionStorage capture), `ContextSwitchDialog.tsx` (reference only) |
| `sprk_todo` schema | `src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md` + `scripts/Deploy-SprkTodoEntity.ps1` | Entity-schema doc + deploy-script exemplar for `sprk_navitem` (override to **UserOwned**) |

### Skills invoked during execution
`dataverse-create-schema` → `dataverse-deploy` → `code-page-deploy` → `fluent-v9-component` → `ui-test` → `adr-check` + `code-review` → `test-diet`. (Explicitly NOT needed: `bff-deploy`, AI/playbook skills.)

### Data-access decision
Host-context `Xrm.WebApi` (single-entity CRUD, per-user, no cross-system/AI/streaming/bulk) — the textbook `Xrm.WebApi` case per `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`. No MSAL token; ADR-028 invariants don't apply.

## 3. Phase Breakdown (WBS)

### Phase 0 — Spike / Gate (FR-00)  🔒 go/no-go
- **001** — SPIKE: re-validate + productionize the Path B Code Page-injection bootstrap (`notes/retired-sidepane-code/SidePaneManager.ts`) on **current UCI**: pane auto-creates at app load, persists across ≥5 navigations + collapse/expand (`alwaysRender:true`), `getPageContext()` polling observes visits. **Opus / xhigh.** Escalation trigger: bootstrap unreliable → fall back to Path A and re-baseline. Deliverable: spike report in `notes/` + the productionized bootstrap approach.

### Phase 1 — Framework  (depends 001)
- **010** — Widen `xrmContext.ts`: add `getPane(paneId)` + `pane.select()` typings, fix `PageInput.webresource` → `webresourceName`, extend `getXrm()` to 3-frame walk (window→parent→top). Add unit tests.
- **011** — `SprkSidePaneHost` + `sidePaneRegistry` (NEW): wrap `SidePaneShell`, generalize `DataGridSidePaneOrchestrator` lifecycle, registry `{ id, icon, title, order, component }`. Right-rail icons. `<justification>` required. (depends 010)

### Phase 2 — Entity  (parallel with Phase 1 — different surface)
- **020** — `sprk_navitem` schema authoring: `src/solutions/SpaarkeCore/entities/sprk_navitem/entity-schema.md` (mirror `sprk_todo`) + `scripts/Deploy-SprkNavItemEntity.ps1` (**UserOwned**; global optionsets `sprk_type`/`sprk_source`/`sprk_pagetype` first; fields per spec data model). `<justification>` required. Prescriptive (schema order).
- **021** — Deploy `sprk_navitem` to `SpaarkeCore` + security roles (`dataverse-deploy`). `deploy` tag; **human/environment gate**. (depends 020)

### Phase 3 — Capture engine  (depends 011, 021)
- **030** — Recent (Viewed) capture: re-adopt `contextService.ts` polling loop (~0.5–2s) → upsert `history` `sprk_navitem` via `Xrm.WebApi` (`useSprkMemoRepository` pattern); derived current-page recomputed each poll, null on leaving records. Display-name resolve on first sighting.
- **031** — Retention prune-on-write, 30-day age: delete the user's `history` rows older than 30 days on each capture upsert. (depends 030)

### Phase 4 — Navigator core  (depends 011, 021)
- **040** — `NavigatorPane` code page (NEW): Vite solution hosted as `webresource` pane (CalendarSidePane pattern); tab scaffold (Recent/Pinned/Views); theme detection + `scaledTheme`/`useUiScale`; renders via shared Navigator body component. `<justification>` + `<ui-tests>`.
- **041** — Recent tab (Viewed): render history rows from capture (030); type chips; click-navigate; promote-to-pin. (depends 030, 040)
- **042** — Recent (Edited): N per-entity `modifiedby=me order by modifiedon desc` queries over the core set (matter/project/document/todo/event/communication), merged client-side; Viewed/Edited toggle. (depends 040)

### Phase 5 — Pinned  (depends 040, 021)
- **050** — Pin gesture (star): create/remove per-user `sprk_type=pin` `sprk_navitem`; **never writes `sprk_monitor`**; Pinned→Records group. (depends 040)
- **051** — Bookmarks: "Pin this page" (captured, one click) + "+ Add bookmark" (manual; parse MDA record + entitylist/view URLs → logical target; else raw `sprk_url` weblink opening new tab). (depends 050)
- **052** — Monitored lens: distinct Pinned group from shared `sprk_monitor` (`monitor=true AND owned/assigned to me`); shared-flag semantics surfaced in UI; never merged with personal pins. (depends 040)

### Phase 6 — Views  (depends 040)
- **060** — Views tab: reuse `ViewService.ts` (`userquery` grouped by `returnedtypecode`); click → `navigateTo({pageType:'entitylist', entityName, viewId})`; system `savedquery` opt-in/pin-only.

### Phase 7 — Search / quick-switcher  (depends 041, 050, 060)
- **070** — Persistent top-of-pane search box: local fuzzy-match across Recent/Pinned/Views; escalate to live Dataverse record/view lookup on no local hit; keyboard-focus accelerator; Enter → navigate top result. `<ui-tests>`.

### Phase 8 — Security & retention verification  (depends 041, 050, 031)
- **080** — Read-time security trimming: re-validate access to each history/pin target via lightweight retrieve; drop/blank rows that 403/404 (never render a cached name for an inaccessible record). **FULL rigor** (legal-context sensitive).
- **081** — Retention verification: seed a >30-day history row → trigger capture → confirm pruned; confirm pins never expire.

### Phase 9 — Framework-proof, deploy, UI test
- **085** — Stub contributor (FR-13): register a second contributor with only `{ id, icon, title, component }` → own rail icon, renders, no host changes. Proves G5. (depends 011)
- **086** — Deploy: Vite build (cache-clear + recompile `@spaarke/ui-components` first) + package + deploy `NavigatorPane` as `webresource`; wire the Path B bootstrap. `deploy` tag; **human/environment gate**. (depends 040–070)
- **087** — UI test pass (`ui-test`): docked pane light + dark, capture-on-navigate, pin/unpin, bookmarks, search, Views. (depends 086)

### Wrap-up
- **090** — Project wrap-up: README status → Complete; `notes/lessons-learned.md`; `/test-diet` reconciliation; success-criteria verification; archive. (depends all)

## 4. Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| Gate | 001 | — | Serial go/no-go spike (Opus/xhigh); Path A fallback on failure |
| A | 010, 020 | 001 | Different surfaces (xrmContext.ts vs Dataverse schema) — parallel-safe |
| B | 011, 021 | A | Host build (needs 010 typings) ‖ entity deploy (human-gated) |
| C | 030, 040 | B | Capture service ‖ Navigator code-page shell |
| D | 041, 042, 050, 052, 060 | C | Tab/feature build on the shell — coordinate shared `NavigatorPane` files (see §5) |
| E | 031, 051 | 030 / 050 | Retention ‖ bookmarks |
| F | 070, 080, 081, 085 | D/E | Search ‖ security trim ‖ retention-verify ‖ stub |
| Deploy | 086 → 087 | F | Human/env gate, then UI test |
| Wrap | 090 | all | Serial |

## 5. Parallel-safety notes

- **`NavigatorPane` tab files** (Phase 4/5/6): tasks 041/042/050/051/052/060 all extend the Navigator body. Where they edit the *same* file they are `parallel-safe: false` and sequenced; where each owns a distinct tab/section file they may parallelize. Task authoring assigns `parallel-group`/`parallel-safe` per POML.
- **Deploy tasks (021, 086)** and the **spike (001)** and **wrap-up (090)** are `parallel-safe: false`.
- **No `.claude/` writes** in any task → no main-session-only sequencing needed for this project.
- Run `/conflict-check` before any `@spaarke/ui-components` PR (shared lib touched by other worktrees, though not the `SidePane/` subfolder).

## 6. Risks

- **R1 — Path B bootstrap on current UCI** (Task 001). Mitigation: spike gate + documented Path A fallback.
- **R2 — `getPageContext()` current-user OData literal** (`@me` binding) for FR-04/capture may vary by build — validate during Task 001/030.
- **R3 — Custom-page name resolution** (`sprk_pagetype=custom`, OQ-6) is best-effort — resolve during 030/041.
- **R4 — Fluent v9 portal dark-mode regression** — enforce portal `FluentProvider` re-wrap (ADR-021); covered by 087 UI test.
