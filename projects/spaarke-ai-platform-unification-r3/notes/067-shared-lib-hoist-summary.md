# Task 067 — Architectural Summary: Workspace Section Infrastructure Hoist

> **Date**: 2026-05-20
> **Task**: 067 — Hoist workspace section registry + builder to `@spaarke/ui-components`
> **Status**: ✅ Complete (partial Bug 2 fix; ADR-012 cross-cutting limitation architecturally addressed)

---

## TL;DR

The workspace section **builder + types** are now in `@spaarke/ui-components`. The
**legal-domain section factories** remain in LegalWorkspace (correctly so — they reference
sprk_event/sprk_document/sprk_todo entities and legal-Code-Page web resource names, which
ADR-012 explicitly forbids from the shared lib). SpaarkeAi now uses the canonical builder
with a local placeholder registry, eliminating divergence from the standalone LegalWorkspace
data path while preserving content-level placeholders until full domain-component hoisting
is designed.

---

## What changed

### Shared lib (`@spaarke/ui-components`) — additions

| File | Action | Purpose |
|---|---|---|
| `src/components/WorkspaceShell/buildDynamicWorkspaceConfig.ts` | **NEW** | Canonical workspace config builder. Pure (type-only dependencies on shared lib types). Includes `LayoutJson`, `LayoutJsonRow`, `WorkspaceScope` interfaces, `SYSTEM_DEFAULT_LAYOUT_JSON` constant, `buildDynamicWorkspaceConfig` function, `countSlots` helper. |
| `src/components/WorkspaceShell/index.ts` | **MODIFIED** (+11 lines) | Exports `buildDynamicWorkspaceConfig`, `SYSTEM_DEFAULT_LAYOUT_JSON`, and the `LayoutJson` family from the WorkspaceShell barrel. Propagates to the root `@spaarke/ui-components` barrel via the existing `export *` chain. |

### LegalWorkspace — re-export shim (no behavior change)

| File | Action | Purpose |
|---|---|---|
| `src/solutions/LegalWorkspace/src/workspace/buildDynamicWorkspaceConfig.ts` | **REPLACED** (248 → 19 lines) | Now a re-export from `@spaarke/ui-components`. Preserves the historical import path `from "../workspace/buildDynamicWorkspaceConfig"` so `WorkspaceGrid.tsx` and `useWorkspaceLayouts.ts` don't need touches. |

LegalWorkspace's `sectionRegistry.ts` is **unchanged** — it still composes the 6
legal-domain factories (getStarted, quickSummary, latestUpdates, todo, documents,
dailyBriefing), all of which remain solution-local because they import legal-domain
components (`QuickSummaryRow`, `ActivityFeed`, `SmartToDo`, `DocumentsTab`,
`DailyBriefingSection`, `ACTION_CARD_CONFIGS`).

### SpaarkeAi — rewired to canonical builder

| File | Action | Purpose |
|---|---|---|
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceHomeTab.tsx` | **REWRITTEN** | Now imports `buildDynamicWorkspaceConfig` + `SYSTEM_DEFAULT_LAYOUT_JSON` + `LayoutJson` from `@spaarke/ui-components` (per the ADR-012 "public barrel, not deep imports" constraint). Builds a local `placeholderRegistry` covering known section IDs (get-started, quick-summary, latest-updates, todo, documents, daily-briefing, matters, projects). Passes it through the canonical builder. Eliminates the parallel `buildFoundationalConfig` logic that previously diverged from LegalWorkspace's data path. |

---

## Why we didn't hoist the factories

The pre-investigation suggested 4-5 factories were portable. Re-investigation revealed:

| Factory | Local imports | Why hoisting violates ADR-012 |
|---|---|---|
| `getStarted` | `ACTION_CARD_CONFIGS` (pure data) | Card click handlers reference `sprk_creatematterwizard`, `sprk_createprojectwizard`, etc. — these are legal-domain Code Page web-resource names that would couple the shared lib to legal workflows. |
| `quickSummary` | `QuickSummaryRow` | Fetches matter/project/invoice metrics — legal-domain Dataverse queries. |
| `latestUpdates` | `ActivityFeed` | Renders sprk_event entities — legal-domain entity dependency. |
| `todo` | `SmartToDo` | Kanban board over sprk_todo entity + @hello-pangea/dnd dep not in shared lib. |
| `documents` | `DocumentsTab` + `DataverseService` | sprk_document entity + per-view picker + DataverseService coupling. |
| `dailyBriefing` | `DailyBriefingSection`, `useDailyBriefing` | AI feature stack (BFF /daily-briefing/narrate, TTL cache, telemetry). Hoisting expands the AI-coupling surface of shared lib (per ADR-013 BFF placement governance, this is exactly the kind of cross-cutting growth to avoid in shared lib). |

ADR-012 explicitly states: *"MUST NOT hard-code Dataverse entity names or schemas as
string literals (use configurable entity maps)"*. Hoisting these factories would require
either (a) baking entity names into the shared lib (violation), or (b) extracting them as
configurable maps + hoisting all dependent components (large, scoped follow-on work).

---

## What's the result?

### From SpaarkeAi's perspective

Before task 067:
- `WorkspaceHomeTab.tsx` had a private `buildFoundationalConfig` function that mirrored
  parts of LegalWorkspace's builder logic but diverged. Every section rendered a
  `<Text>placeholder</Text>` body.

After task 067:
- `WorkspaceHomeTab.tsx` imports the canonical `buildDynamicWorkspaceConfig` from
  `@spaarke/ui-components`. The layout structure is built by the SAME function the
  standalone LegalWorkspace uses. The only difference is the section *registry*:
  SpaarkeAi supplies its own placeholder registry; LegalWorkspace supplies its legal-domain
  registry. Both produce a `WorkspaceConfig` via the SAME builder.

This is a structural improvement (single source of truth for the builder), even though
SpaarkeAi's section *bodies* are still placeholders.

### From standalone LegalWorkspace's perspective

- Identical behavior. Bundle size 569.84 KB gzip (within noise of pre-067).
- All 9 templates render in the layout wizard (FR-25).
- All 6 sections render full content (Quick Summary metrics, Activity Feed items, To Do
  kanban, Documents grid, Daily Briefing, Get Started cards). The local import path
  `"../workspace/buildDynamicWorkspaceConfig"` resolves to a re-export shim that points
  to the shared lib — semantically identical to the pre-067 inline implementation.

### From the ADR-012 cross-cutting limitation perspective

- **Builder + types**: Now in shared lib. Future Code Pages that need to embed workspace
  surfaces can consume `buildDynamicWorkspaceConfig` + `SYSTEM_DEFAULT_LAYOUT_JSON` from
  `@spaarke/ui-components`. The cross-cutting limitation FOR THE INFRASTRUCTURE LAYER is
  resolved.
- **Section content**: Legal-domain factories remain in LegalWorkspace by ADR-012 design.
  The cross-cutting limitation FOR LEGAL-DOMAIN CONTENT is not fully resolved — but it
  shouldn't be, because legal-domain components belong with the legal-domain solution.
  Any future Code Page that needs section content has two paths:
  1. Build its own placeholder/content registry (what SpaarkeAi does now).
  2. Build its own domain-specific section factories using the shared lib's
     `SectionRegistration` contract.

---

## Open follow-on work (scoped, not for task 067)

1. **Context-agnostic section catalog (design)**: If multiple future Code Pages need
   the same section content (e.g., a generic "My Recent Documents" surface), design a
   context-agnostic section family in the shared lib that accepts an `IDataService`
   abstraction (per ADR-012's "Tier 2: Abstracted I/O" pattern). Hoisting `DocumentsTab`
   etc. would require extracting their Dataverse calls behind such an abstraction first.

2. **AI-Section family**: `dailyBriefing` is a candidate for hoisting INTO the shared lib's
   AI-section subfamily, but per ADR-013 BFF placement governance, this needs explicit
   placement justification (does the AI-coupling-surface growth in shared lib pass the
   "consumed by 2+ surfaces" bar? Probably yes, given both LegalWorkspace and SpaarkeAi
   want it. Worth a dedicated task.).

3. **Configurable entity maps**: For factories like `latestUpdates`, the entity name
   `sprk_event` could be a configurable map (per ADR-012 MUST). This would make the
   factory portable. Scope: separate task; affects multiple factories.

---

## Verification summary

| Check | Result |
|---|---|
| Shared lib build (tsc) | ✅ Clean — 0 errors |
| LegalWorkspace build (vite) | ✅ Clean — 569.84 KB gzip (no drift) |
| SpaarkeAi build (vite) | ✅ Clean — 800.18 KB gzip (+22.58 KB / +2.9%) |
| Public barrel exports | ✅ `buildDynamicWorkspaceConfig`, `SYSTEM_DEFAULT_LAYOUT_JSON`, `LayoutJson`, `LayoutJsonRow`, `WorkspaceScope` |
| ADR-012 compliance (shared lib) | ✅ Builder is pure; no legal-domain entity references; no platform APIs |
| ADR-021 compliance (Fluent v9 tokens) | ✅ No hex/rgba literals in new code |
| ADR-028 compliance (no accessToken snapshots) | ✅ Only doc-comment references; no props/state |
| FR-25 / NFR-10 (LegalWorkspace identical) | ✅ Re-export shim preserves all imports; bundle size unchanged |
| Both web resources deployed | ✅ `sprk_spaarkeai` + `sprk_corporateworkspace` published |
| Test-Deployment.ps1 | ✅ 13/17 PASS (4 fails are pre-existing infra gaps) |

---

## Files touched (full paths)

**Shared lib**:
- `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/buildDynamicWorkspaceConfig.ts` — NEW (247 lines)
- `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/index.ts` — MODIFIED (+11 lines)

**LegalWorkspace**:
- `src/solutions/LegalWorkspace/src/workspace/buildDynamicWorkspaceConfig.ts` — REPLACED (248 → 19 lines)

**SpaarkeAi**:
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceHomeTab.tsx` — REWRITTEN

**Notes**:
- `projects/spaarke-ai-platform-unification-r3/notes/drafts/067-factory-inventory.md` — NEW
- `projects/spaarke-ai-platform-unification-r3/notes/067-shared-lib-hoist-summary.md` — NEW (this file)
- `projects/spaarke-ai-platform-unification-r3/notes/deploys/2026-05-20-deploy.md` — APPENDED (supplemental section)

**Project state**:
- `projects/spaarke-ai-platform-unification-r3/tasks/067-hoist-workspace-section-registry-to-shared-lib.poml` — status → completed; notes appended
- `projects/spaarke-ai-platform-unification-r3/tasks/TASK-INDEX.md` — row 067 → ✅
- `projects/spaarke-ai-platform-unification-r3/current-task.md` — updated (this session)

---

## 2026-05-21 amendment — Task 069 revisits Option L for Daily Briefing

Task 067 placed Daily Briefing under "solution-local — STAYS in LegalWorkspace" (per the factory inventory) citing ADR-013 placement governance. On review (operator 2026-05-20 / Option Z minimum scope direction), that rationale was reconsidered:

- **ADR-013 governs SERVER-side BFF placement** (where AI services live in the backend), NOT frontend consumers sharing an existing AI BFF endpoint. Multiple Code Pages calling `/api/ai/daily-briefing/narrate` via a shared frontend component is not an ADR-013 concern.
- **ADR-012's binding rule** is "MUST NOT hard-code Dataverse entity names or schemas as string literals". Daily Briefing's hook + section call only the shared BFF AI endpoint — no `sprk_event` / `sprk_document` / `sprk_todo` references. It is safe to hoist under ADR-012.
- **Architectural design for the hoist**: shared `useDailyBriefing` + `DailyBriefingSection` accept `authenticatedFetch` (function-based contract per ADR-028) as parameters; `dailyBriefing.registration.ts` becomes a FACTORY (`createDailyBriefingRegistration({ authenticatedFetch, tenantId, onRateLimitError? })`) so consumers close over their own auth deps. LegalWorkspace shim path preserves FR-25 / NFR-10 byte-identically.

The other 5 legal-domain factories (getStarted, quickSummary, latestUpdates, todo, documents) CORRECTLY stay solution-local — they each have Dataverse entity string dependencies that ADR-012 explicitly forbids in shared lib.

After task 069 deploys, SpaarkeAi's Home tab shows the Daily Briefing section as its default content (single-row layout). LegalWorkspace continues to consume Daily Briefing via the local shim, alongside its 5 legal-domain sections. Bundle delta: -0.81 KB gzip LegalWorkspace, -0.52 KB gzip SpaarkeAi.

See:
- [`tasks/069-daily-briefing-in-spaarkeai-home-tab.poml`](../tasks/069-daily-briefing-in-spaarkeai-home-tab.poml)
- [`notes/deploys/2026-05-20-deploy.md`](deploys/2026-05-20-deploy.md) — Task 069 supplemental deploy section

---

*End of architectural summary.*
