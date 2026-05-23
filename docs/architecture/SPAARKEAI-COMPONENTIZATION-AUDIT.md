# SpaarkeAi Componentization Audit

> **Purpose**: Honest assessment of whether the SpaarkeAi workspace pipeline (as it stands after Round 13 / Calendar widget + R10–R13 polish) is componentized and reusable. Catalogs every coupling and gap that affects maintenance + extendability, with prioritized remediation guidance.
>
> **Last reviewed**: 2026-05-22 (Task 123, Round 13). Refreshed from task 113 (Round 9) to incorporate the Calendar widget result (task 115) as the proven canonical "shared-lib widget + thin LW shim" pattern. Periodic review required — coupling lands as the codebase grows.

---

## Executive summary

**Verdict**: The pipeline is structurally sound and works end-to-end, but it is NOT yet fully componentized. Six concrete gaps need addressing if Spaarke wants the workspace surface to be reusable across non-SpaarkeAi hosts (e.g. Outlook side pane, Teams app, MDA form embed) or to allow new section authors to work WITHOUT touching LegalWorkspace.

**Top 5 issues** (full detail in §1-6):

1. **Duplicate `useWorkspaceLayouts` hook** in SpaarkeAi and LegalWorkspace — different fallback strategies + drift risk.
2. **Section factories are LegalWorkspace-internal**, tightly coupled to LegalWorkspace's local context providers + DataverseService.
3. **`WorkspaceLayoutWidget` hard-wires `LegalWorkspaceApp`** as the only renderer — no abstraction for an alternative "workspace renderer".
4. **`Xrm.WebApi` vs BFF decision criteria are undocumented** — Wave 3a's QuickSummary chose Xrm; some other code uses BFF; no rule.
5. **Embedded-mode contract is informal** — `embedded={true}` is a boolean prop with implicit behavior; there is no documented contract a future host must implement.

**Effort to address top 3**: ~16-24 hours total. None block the current shipping behavior; all are maintenance-debt items.

---

## 1. The dual `useWorkspaceLayouts` hooks

**What's coupled today**: Two separate files implement essentially the same BFF fetch:

- `src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts` — canonical implementation. Returns `activeLayoutJson: LayoutJson` (parses sectionsJson). Has `SYSTEM_DEFAULT_LAYOUT` fallback so the workspace always renders something. Uses module-level `authenticatedFetch` + `getBffBaseUrl()`.
- `src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts` — "faithful adaptation" (per its own docblock). Sources auth from `useAiSession()` (hook-based instead of module-level — fixes a config-race bug from Task 081). Drops the LayoutJson parsing. Drops the SYSTEM_DEFAULT_LAYOUT fallback (degrades to "no workspace" empty state instead).

Both hit the same BFF endpoints (`GET /api/workspace/layouts` + `GET /api/workspace/layouts/default`). The LegalWorkspace hook runs INSIDE the embedded `LegalWorkspaceApp` to drive `WorkspaceGrid`. The SpaarkeAi hook runs inside `WorkspacePane` to populate the Workspaces dropdown + drive the auto-install default tab.

**Why it matters**:
- **Drift risk**: any BFF schema change requires editing two files in lockstep. The docblock on the SpaarkeAi hook explicitly says "KEEP THIS FILE IN SYNC if the LegalWorkspace hook changes its fetch shape, cache strategy, or selection cascade" — that's a maintenance smell.
- **Subtly different cache shapes**: LegalWorkspace stores `LayoutJson` in sessionStorage; SpaarkeAi stores just the list. A future "share cache across both" optimization would require explicit reconciliation.
- **Auth-surface inconsistency**: one uses module-level auth, the other uses hook-deps auth. This is itself a coupling that masked the Task 081 race condition — the bug was specifically introduced by the module-level pattern.

**Remediation direction**: Hoist the canonical hook to `@spaarke/ai-widgets` (or a new `@spaarke/workspace-layouts` package) as `useWorkspaceLayouts(opts)` that:
- Accepts `bffBaseUrl + authenticatedFetch + isAuthenticated` from caller (hook-deps style — works for both LegalWorkspace standalone via local `useAuth()` and SpaarkeAi via `useAiSession()`).
- Has an optional `parseLayoutJson: boolean` flag (default true) so LegalWorkspace gets the parsed `LayoutJson`, SpaarkeAi opts out.
- Has an optional `fallbackLayout: WorkspaceLayoutDto | null` (default null) so LegalWorkspace can pass `SYSTEM_DEFAULT_LAYOUT` and SpaarkeAi opts out.
- Owns the sessionStorage cache contract centrally.

Estimated effort: ~8 hours (extract + adapt both consumers + verify embedded + standalone parity).

---

## 2. Section factories are LegalWorkspace-internal

**What's coupled today**: All 6 section factories live in `src/solutions/LegalWorkspace/src/sections/*.registration.ts`. Each factory's `renderContent()` returns a React tree that imports from `src/solutions/LegalWorkspace/src/components/<Section>/`.

The implementations REACH into LegalWorkspace-local context providers and services:
- `documents` + `todo` + `latestUpdates` use LegalWorkspace's `DataverseService` and `FeedTodoSyncContext`.
- `quick-summary` uses `useQuickSummaryCounts` (LegalWorkspace-local hook calling `webApi.retrieveMultipleRecords`).
- `daily-briefing` IS hoisted to `@spaarke/ui-components/components/WorkspaceShell/sections/dailyBriefing/` (Task 069) — but LegalWorkspace's `sections/dailyBriefing/dailyBriefing.registration.ts` is now a SHIM that re-exports from there to preserve the static export shape. The actual factory contains LegalWorkspace-local wiring for `authenticatedFetch` + `trackEvent`.

**Can a non-LegalWorkspace Code Page use just one section today?** No.

To use `quick-summary` from a new host:
1. You'd have to import `quickSummaryRegistration` from LegalWorkspace's `src/sections/` — but those files are NOT exported from the `@spaarke/legal-workspace` package barrel.
2. Even if exported, you'd pull in `QuickSummaryRow` → `useQuickSummaryCounts` → LegalWorkspace's data shape.
3. You'd also need a `WorkspaceShell` mount with a `SectionFactoryContext` populated by your new host (no SpaarkeAi-internal shortcut exists for "just render this one section").

**Why it matters**:
- **Reuse is binary today**: you either embed all of LegalWorkspaceApp (the WorkspaceLayoutWidget pattern) or you reproduce each section's full dependency closure. There's no middle ground for "embed just this one section".
- **Section authoring requires LegalWorkspace knowledge**: a section developer must understand `FeedTodoSyncContext`, `DataverseService`, and the cross-section badge-count/refetch contracts — none of which are described outside LegalWorkspace's CLAUDE.md.
- **The dailyBriefing hoist precedent shows the right shape**: factory + hook + component all in `@spaarke/ui-components`; LegalWorkspace re-exports a thin shim. The other 5 sections are good candidates for the same treatment.

**Remediation direction**: Hoist each section's `*.registration.ts` to `@spaarke/ui-components/components/WorkspaceShell/sections/<sectionName>/` over multiple rounds, following the daily-briefing precedent:
1. Identify each section's dependency closure.
2. Hoist the section component + hook to the shared package.
3. Make context providers like `FeedTodoSyncContext` either (a) part of the section's own component tree, or (b) hoisted into `WorkspaceShell` so any host that mounts WorkspaceShell gets them.
4. LegalWorkspace's `sections/*.registration.ts` become re-export shims.

This is a multi-round effort. Estimated effort: ~6-10 hours per section × 5 sections = 30-50 hours total. The dailyBriefing hoist alone took 12 hours (Task 067).

### 2A. Calendar section: the proven canonical pattern (task 115, Round 9)

The Calendar section (task 115, 2026-05-22) ships the **first true "shared-lib widget + thin LW section shim" implementation** in SpaarkeAi. It is the model future widgets should follow.

**What shipped**:

- The widget proper — `CalendarWorkspaceWidget` (~1100 LOC) — lives in `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/`. It composes shared `CalendarSection` + `GridSection` + `ViewSelectorDropdown` + Events filter components, all from `@spaarke/events-components` (task 114).
- The LegalWorkspace section registration — `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` — is a **62-line shim** that delegates rendering entirely to the shared widget. No LW-internal coupling is introduced (no `DataverseService`, no `FeedTodoSyncContext`, no LW hooks). The factory accepts `SectionFactoryContext` but does not forward any of it — the widget is self-contained via `Xrm.WebApi` + shared services.

**Why this matters for the §2 prediction**:

The original §2 said section factories are LegalWorkspace-internal and the only path to a workspace section was either embed all of LegalWorkspaceApp or reproduce each section's full dependency closure. **Calendar disproves the strong form of that claim**: it shows you CAN build a workspace section that reuses the LegalWorkspaceApp pipeline (sectionsJson, WorkspaceShell, embedded mode) WITHOUT becoming LW-internal — you just have to put the implementation in a shared lib and let the LW-side registration be a one-screen shim.

**Implication for the 5 original sections**:

The original §2 remediation backlog said hoist each of the 5 existing sections (get-started, quick-summary, latest-updates, todo, documents) at ~6–10 hours each (30–50 hours total). **Calendar's existence reduces the urgency of that backlog** — there is now a proven path to reuse without hoisting, *as long as the new functionality is in a shared lib from day one*. The 5 existing sections only NEED to be hoisted when a non-LegalWorkspace host needs them (e.g. a future Outlook side pane, Teams app, MDA form embed). Until that day, the 5 can stay LW-local; the audit's prediction has been narrowed but not falsified.

**Future widgets should follow the Calendar pattern** (codified in the build-a-widget guide as Pattern D — see [`../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) §1).

**Remediation backlog update** (see §8): item 5 ("Hoist remaining 5 section factories") priority is now **DEFER until a non-LegalWorkspace host enters the roadmap**. The other 5 sections are not at risk of growing more LW-internal coupling — they're stable. Hoist incrementally only on a forcing function.

---

## 3. `WorkspaceLayoutWidget` is hard-wired to `LegalWorkspaceApp`

**What's coupled today**: `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/WorkspaceLayoutWidget.tsx` does exactly one thing — `<LegalWorkspaceApp version="embedded" embedded initialWorkspaceId={data.layoutId} />`. There is no abstraction layer, no "workspace renderer" interface.

**Why it matters**:
- **Future hosts cannot supply a different renderer**: if Spaarke ever wants a "lightweight workspace" with different chrome (say, a stripped-down view for Outlook), today's path is to either (a) duplicate `WorkspaceLayoutWidget` with a fork, or (b) add a feature flag inside `LegalWorkspaceApp` to control which chrome bits are rendered. Neither is clean.
- **The widget IS PRESERVED for the operator's reuse principle** ("when we have working components reuse them") — embedding the working LegalWorkspaceApp avoided a 30-file / 10K-LOC duplication. So this gap is the COST of that pragmatic choice. It is documented as such in `WorkspaceLayoutWidget.tsx`'s own docblock (lines 7-15).

**Remediation direction**: Introduce a `WorkspaceRenderer` interface in `@spaarke/ui-components`:

```ts
export interface WorkspaceRenderer {
  /** Render a workspace by its layout id; the renderer fetches/parses its own sectionsJson. */
  render(props: { layoutId: string; webApi: unknown; userId: string }): React.ReactNode;
}
```

`LegalWorkspaceApp` becomes one implementation. `WorkspaceLayoutWidget` accepts an injected `renderer` prop (or uses a registry like the widget registry). Default registration points to `LegalWorkspaceApp`. Future renderers (e.g. "OutlookLiteWorkspace") register their own.

This is a small refactor IF we make `LegalWorkspaceApp` an implementation of the interface. Estimated effort: ~4 hours.

---

## 4. Xrm.WebApi vs BFF — decision criteria undocumented

**What's coupled today**: The codebase mixes Xrm.WebApi calls and BFF calls without a documented rule. Examples:

| Code path | Channel | Why |
|---|---|---|
| `useQuickSummaryCounts.ts:53,65` | `webApi.retrieveMultipleRecords` | Aggregate counts across N entities |
| `useDocumentsTabList` | LegalWorkspace `DataverseService` (wraps Xrm.WebApi) | Document list per record |
| `useDailyBriefing` | BFF (`/api/ai/dailybriefing`) | AI-curated content (must run server-side) |
| `useWorkspaceLayouts` (both copies) | BFF (`/api/workspace/layouts`) | Cross-user system layout merge logic lives server-side |
| `useChatFileAttachment` | BFF (chat endpoints) | AI session state |
| Tab persistence (`PATCH /api/ai/chat/sessions/{id}/tabs`) | BFF | Server-side persistence |

The Wave 3a operator brief on My Work card counts explicitly said "use Xrm.WebApi like the existing 4 cards" — but the rationale is NOT in any guide today. Task 114 (Events components hoist) reinforced the unwritten norm: every service in `@spaarke/events-components/services/` uses Xrm.WebApi exclusively. The pattern is consistent in practice; it remains undocumented as a decision rule.

**Why it matters**:
- **New section authors guess wrong**: a developer writing a new section may default to BFF because it feels more "modern", adding load to the BFF without need. Or they default to Xrm and miss cases that need server-side aggregation.
- **Embed portability**: Xrm.WebApi is unavailable outside MDA / model-driven contexts (no Xrm in Outlook, Teams, mobile shell). If we ever ship the workspace pipeline outside Dataverse hosts, every Xrm dependency becomes a port blocker.
- **Auth surface inconsistency** (see §6): Xrm.WebApi uses Xrm's auth (Dataverse cookie / OBO via host) — `authenticatedFetch` uses Bearer via @spaarke/auth. They are NOT interchangeable.

**Remediation direction**: Publish a decision-criteria addendum in `docs/standards/INTEGRATION-CONTRACTS.md` (or a new `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`):

- **Use Xrm.WebApi when**: data is in Dataverse, the call is read-only OR a simple CRUD, no server-side merge / cross-tenant / AI grounding required, callsite already has Xrm context, and there's no plan to embed in non-MDA hosts.
- **Use BFF when**: server-side merge logic (e.g. system + user layouts), AI-curated content, cross-tenant aggregation, NFR-09 session persistence, future-host portability anticipated, or the data needs SharePoint Embedded access.

Estimated effort: ~2 hours to write the addendum + cross-link from CLAUDE.md §11 + relevant docs. **Recommended next round** — this is the highest-priority doc-only item; every subsequent section author will hit the question.

---

## 5. Embedded-mode contract is informal

**What's coupled today**: `LegalWorkspaceApp` accepts an `embedded?: boolean` prop. When `true`, it skips:
1. The internal `<PageHeader>` (Task 087).
2. The footer.
3. The outer `<FluentProvider>`.
4. The cross-device theme sync side effects (Task 087 + 105).
5. `useDailyDigestAutoPopup` indirectly (via the `spaarke_dailyDigestShown` sessionStorage sentinel set by SpaarkeAi's main.tsx — Task 105).

The contract is encoded in the file's docblock (lines 36-48) but is not formal — there's no `EmbeddedModeContract` interface, no TypeScript exhaustiveness, no test that asserts the contract.

**Why it matters**:
- **A new host doesn't know what to pre-arrange**: SpaarkeAi knows to set `sessionStorage["spaarke_dailyDigestShown"]` BEFORE mounting LegalWorkspaceApp — but only because Task 105 told us. A new host (Outlook side pane, Teams app) would have to reverse-engineer this from the source.
- **Implicit `setLegalWorkspaceRuntimeConfig` requirement**: SpaarkeAi's main.tsx calls this with the SAME config the SpaarkeAi singleton uses (lines 222-235). A new host would have to do the same — there's no fallback that would surface "you forgot to init my config" until a downstream call throws.
- **Cross-device theme sync is a side-effect-free assumption**: SpaarkeAi assumes the host owns theme. A future host that DOESN'T own theme would need its own arrangement.

**Remediation direction**: Document a formal "embedded-mode host contract" inside `LegalWorkspaceApp.tsx` (or alongside it as `EMBEDDED-MODE-CONTRACT.md`):

- Host MUST call `setLegalWorkspaceRuntimeConfig(config)` BEFORE mounting.
- Host MUST own theme (`FluentProvider` + theme sync).
- Host MUST set `sessionStorage["spaarke_dailyDigestShown"]` before mount if it wants to suppress the auto-popup.
- Host MUST provide a stable `webApi` reference (or a non-Xrm shim implementing the `IWebApi` interface).
- Host MUST not unmount-remount on every layout change (LegalWorkspaceApp's internal hooks would re-fire).

A future hardening would convert these from prose into TypeScript types. Estimated effort: ~3 hours for the prose + cross-link, ~8 hours for the typed version.

---

## 6. Auth path: mostly unified, two gaps

**What works (Spaarke Auth v2)**:
- `@spaarke/auth` is the single source of MSAL truth.
- `authenticatedFetch` + `useAuth()` are function-based, no token snapshots cross component boundaries (INV-3).
- `buildBffApiUrl` normalizes BFF URLs (INV-7).
- Tab persistence, layouts list, Daily Briefing, chat — all go through `authenticatedFetch`.
- Verified clean by Task 060 (auth audit).

**Two gaps**:

### 6a. Xrm.WebApi paths bypass `@spaarke/auth`

This is BY DESIGN for the Xrm.WebApi cases (see §4). But it means:
- The `WorkspaceLayoutWidget` empty state ("Workspace requires the Dataverse host (Xrm.WebApi unavailable)") is what makes the embed work-or-degrade. A future non-Xrm host that wants the workspace pipeline would have to either:
  - Provide an Xrm.WebApi shim that calls Dataverse via Bearer-auth → requires the shim to mint Dataverse tokens via OBO.
  - OR rewrite the affected sections to use BFF endpoints.

### 6b. Dual runtime-config singletons

`main.tsx` calls BOTH `setRuntimeConfig(config)` (SpaarkeAi) AND `setLegalWorkspaceRuntimeConfig(config)` (LegalWorkspace) with the same config. Two singletons hold equivalent values; the embedded code paths call `getBffBaseUrl()` from LegalWorkspace's singleton. The docblock on main.tsx lines 50-60 acknowledges this. It's not a bug — it's a coupling.

**Why it matters**: any new host embedding LegalWorkspaceApp must remember step 4 from §5. This is in the embedded-mode contract above.

**Remediation direction**: Long-term, hoist `runtimeConfig` to `@spaarke/auth` as a single global singleton. Risk: cross-Code-Page coupling — if a new Code Page somehow loaded both packages with different config (unlikely), the singleton would be wrong. Mitigate with init guard. Estimated effort: ~4 hours.

---

## 7. Extensibility audit — concrete future scenarios

### 7.1 "Add a new section type to LegalWorkspace" — score: CLEAN (Pattern A is well-trodden)

See [`../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md). Five new files + two edits + two commands. NO SpaarkeAi changes. NO BFF changes. NO new packages. The dailyBriefing hoist precedent makes it incrementally cleaner if the new section is hoisted to `@spaarke/ui-components` from the start.

### 7.2 "Add a new BFF endpoint for a section" — score: GATED by CLAUDE.md §10

Per CLAUDE.md §10 (binding rule), any BFF additions require loading `.claude/constraints/bff-extensions.md` and stating the Placement Justification in the design doc. Not blocked; just bureaucratic — which is the intent.

### 7.3 "Embed the workspace in a non-MDA host (Outlook, Teams, mobile shell)" — score: BLOCKED until §3, §5, §6a addressed

Today's blockers:
- No abstraction layer over `LegalWorkspaceApp` (§3).
- No formal embedded-mode contract for the new host to comply with (§5).
- Section factories depend on Xrm.WebApi which is unavailable outside MDA (§6a).

Effort to unblock: ~32 hours (the §3 + §5 + §6a remediations together).

### 7.4 "Replace the section factory layer with a plug-in registry" — score: PARTIALLY BLOCKED

`SectionRegistration` already IS a plug-in shape, but the registration map is hardcoded in `sectionRegistry.ts`. To allow third-party registration (e.g. a customer-authored section pack), we'd need:
- A registry instance with `registerSection(reg)` + `getAllSections()` (mirror of `WorkspaceWidgetRegistry`).
- A discovery mechanism (side-effect import) so customer code can register before SpaarkeAi cold-load.

Estimated effort: ~6 hours.

### 7.5 "Build a user-customizable workspace from new sections" — score: CLEAN

Already works today. Users build via WorkspaceLayoutWizard; sectionsJson references registered section IDs; BFF stores `sprk_workspacelayout` records; cold-load resolves via the same pipeline. Confirmed by Tasks 091 + 092 + 102.

### 7.6 "Add a new workspace widget for an AI tool output" — score: CLEAN

Pattern C from the build guide — single registration in `register-workspace-widgets.ts` + a `WorkspaceWidgetComponent` impl. NO LegalWorkspace involvement. The 7 R1 wrapped widgets + the 4 wizard dispatchers + redline-viewer + the workspace embed all follow this pattern.

### 7.7 "Add a NEW shared-lib widget (Calendar-style)" — score: CLEAN (proven by task 115)

The Calendar widget shipped this pattern end-to-end:

1. **Hoist the components to a shared lib** (or reuse an existing one). Task 114 hoisted the Events components from `src/solutions/EventsPage/` to a new `@spaarke/events-components` package (~12 hours including barrel + import surgery + tsc + vite).
2. **Build the widget component in the shared lib**, composing the lib's primitives. Task 115's `CalendarWorkspaceWidget` (~1100 LOC) lives in `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/`.
3. **Write the LegalWorkspace section registration shim** — a 60-line file in `src/solutions/LegalWorkspace/src/sections/<name>.registration.ts` that imports the widget from the shared lib and returns a `ContentSectionConfig` with `renderContent: () => React.createElement(<Widget>)`.
4. **Register the shim** in `src/solutions/LegalWorkspace/src/sectionRegistry.ts`.
5. **Add a Dataverse-system layout entry** in `scripts/system-layouts.json`.
6. **Run the seed script** `pwsh scripts/Deploy-SystemWorkspaceLayouts.ps1 -EnvironmentUrl <url>`.
7. **Deploy LegalWorkspace** via `code-page-deploy`.

Effort breakdown for a new widget that reuses existing shared-lib components: ~1–2 days. Longer if the widget needs new shared components.

Effort breakdown for a Calendar-equivalent new widget that needs a NEW shared lib (hoist included): ~3–5 days.

No SpaarkeAi source changes. No BFF changes. No new `@spaarke/ai-widgets` registrations. The `workspace` widget type (registration #16 in `register-workspace-widgets.ts`) is the universal entry point for every Dataverse-defined layout.

---

## 8. Prioritized remediation backlog

| # | Item | Effort | Impact | Should we do it now? |
|---|---|---|---|---|
| 1 | Document Xrm.WebApi vs BFF decision criteria (§4) | 2h | HIGH (prevents future divergence) | YES — write it before the next round adds new sections |
| 2 | Document embedded-mode contract formally (§5) | 3h | MEDIUM (prevents host-impl drift) | YES — write it alongside the architecture doc |
| 3 | Consolidate the dual `useWorkspaceLayouts` (§1) | 8h | MEDIUM (eliminates drift risk) | Next round — bundle with the section-hoist roadmap |
| 4 | Introduce `WorkspaceRenderer` interface (§3) | 4h | LOW today / HIGH if multi-host needed | Defer until a non-MDA host is on the roadmap |
| 5 | Hoist remaining 5 section factories (§2) | 30-50h | LOW today (Calendar §2A proves the pattern works without hoisting the 5) / HIGH if multi-host on roadmap | **DEFER** — hoist incrementally only when a non-LegalWorkspace host actually needs them. Calendar's "shared-lib widget + thin LW shim" pattern shows you can grow NEW reuse-safe widgets without back-fitting the existing 5. |
| 6 | Hoist runtimeConfig to `@spaarke/auth` (§6b) | 4h | LOW (works today, but cleaner) | Defer until next auth-package round |
| 7 | Make section registry plug-in style (§7.4) | 6h | LOW today, HIGH if third-party sections wanted | Defer |

**Total recommended near-term (items 1 + 2)**: ~5 hours. Both are doc-only — no code changes, no deployment, no risk.

**Total recommended for next round (items 1 + 2 + 3)**: ~13 hours. Item 3 is the only code change, and it's a one-time consolidation that pays back every future BFF-schema change.

---

## 9. What IS unambiguously clean

The audit wouldn't be honest if it only listed gaps. The following ARE well-componentized today:

1. **`PaneEventBus`** — typed, multi-subscriber, DOM-free. Every channel has a documented type union. Subscribe/dispatch hooks are stable APIs. NOT coupled to SpaarkeAi.
2. **`WorkspaceWidgetRegistry` + `ContextWidgetRegistry`** — lazy-factory pattern, side-effect registration, lazy resolution. Adding a new widget type is one registration call. Clean.
3. **`@spaarke/auth`** — single MSAL instance, function-based contract (`useAuth`, `authenticatedFetch`), URL normalization (`buildBffApiUrl`). Task 060 verified zero token snapshots. INV-1..INV-8 enforced via skill checks.
4. **`SectionFactoryContext`** — every section factory receives the same context object. No bespoke parent wiring required at the factory level.
5. **`WorkspaceLayoutService.GetDefaultLayoutAsync`** — clean 4-step cascade with explicit `null` return for "no default" case; frontend handles null cleanly (Wave 2b / Task 109).
6. **The `workspace` widget-type registration** — single registration covers every Dataverse-defined workspace. New layouts require ZERO frontend code changes once the section factory exists.
7. **The 4-stage shell lifecycle** — `determineStage(SessionState)` is a pure function; ShellStageManager maintains a single SessionState ref. All panes read the same stage. No divergence.
8. **NFR-09 tab persistence** — debounced write-through, restore on mount, 404 = benign. Clean.
9. **MAX_WORKSPACE_TABS FIFO** — bounded tab count with deterministic eviction. Bounded memory.
10. **The Vite single-file build** — every Code Page bundles its own React 19 + Fluent v9 (per ADR-022 / ADR-026). No host-provided framework collision.
11. **`@spaarke/events-components`** (task 114) — clean componentization: one components inventory, two consumers (standalone EventsPage code page + the SpaarkeAi Calendar workspace widget). No BFF dependency. Auth via `Xrm.WebApi` only. Pure data + UI primitives + a single composing widget. The package mirrors the architectural unity goal of Round 8 Option B (one workspace concept) — now extended to events components.
12. **The Calendar widget itself** (task 115) — the section registration in LegalWorkspace is a 62-line shim with zero LW-internal coupling. All rendering, data access, and UX logic lives in the shared lib. This proves the "shared-lib widget + thin LW shim" pattern works and is the canonical model for future widgets.

---

## 10. Closing observations

The Round 8 Option B decision (every workspace flows through the same `widget_load → WorkspaceLayoutWidget → LegalWorkspaceApp(embedded) → section factories` pipeline) is GOOD architecture. It's the right unification. The remaining coupling is mostly inside LegalWorkspace itself, not in the SpaarkeAi side of the embed — which is the correct place for it to live, given LegalWorkspace is the only host we ship today.

The §3 (WorkspaceRenderer interface) and §6a (Xrm.WebApi portability) items become urgent only when a NEW non-MDA host enters the roadmap. Today the codebase is correctly optimized for the "embed LegalWorkspace in SpaarkeAi" use case; over-abstracting now would be premature.

The §1 + §4 + §5 items are doc + small-refactor wins that pay back every future round and should be the next maintenance investment.

---

## 11. Related docs

- [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](./SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — pipeline reference
- [`SPAARKEAI-COMPONENT-MODEL.md`](./SPAARKEAI-COMPONENT-MODEL.md) — inventory
- [`../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — tutorial; Calendar worked example is now a real shipped implementation (task 115), not a forward projection
- [`../assessments/bff-ai-extraction-assessment-2026-05-20.md`](../assessments/bff-ai-extraction-assessment-2026-05-20.md) — adjacent BFF audit; provides the model for this audit
- [`../../.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — binding rule before any BFF addition
