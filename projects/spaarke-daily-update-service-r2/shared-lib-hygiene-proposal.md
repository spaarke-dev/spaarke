# Proposal — Spaarke Shared Library Hygiene (Follow-On Project)

> **Status**: Proposal — not approved, not scheduled
> **Created**: 2026-06-18
> **Origin**: Duplication audit conducted as part of [`spaarke-daily-update-service-r2`](./spec.md) scoping. 4 high-proximity findings folded into R2's DD workstream; the 10 lower-proximity findings catalogued here for evaluation as a separate project.
> **Recommended project name**: `spaarke-shared-lib-hygiene-r1` (rename freely)
> **Audience**: Owner — to evaluate, prioritize, and decide whether/when to schedule

---

## Why this document exists

During R2 scoping, a focused duplication audit across `src/solutions/*` found 14 cross-solution duplications where code that should live in a shared library was copied across multiple solutions or implemented inconsistently. The owner directed: *"do not defer the icon fix — let's get this resolved so that it is not buried as technical debt; also assess if other similar duplication or not in shared library issues."*

Of the 14 findings:
- **4 high-proximity** items were folded into R2 (FR-19, FR-20, FR-21, and 1 subsumed by P2's Pattern D hoist) — because R2 already touches those files for Daily Briefing.
- **10 lower-proximity** items (this document) are not touched by R2 and would balloon R2's scope from "Daily Briefing migration" into "repo-wide cleanup." They are real tech debt worth addressing, but in their own project.

This proposal preserves the findings so they're evaluated, not buried.

---

## Audit methodology

Audit target: `src/solutions/` (excluding wizards and PCFs, which have different lifecycle constraints).

Solutions inspected: `DailyBriefing/`, `LegalWorkspace/`, `SmartTodo/`, `EventsPage/`, `FindSimilarCodePage/`, `SpaarkeAi/`.

Existing shared libraries that should host shared code:
- `@spaarke/ui-components` → `src/client/shared/Spaarke.UI.Components/`
- `@spaarke/events-components` → `src/client/shared/Spaarke.Events.Components/`
- `@spaarke/smart-todo-components` → `src/client/shared/Spaarke.SmartTodo.Components/`
- `@spaarke/ai-widgets` → `src/client/shared/Spaarke.AI.Widgets/`
- `@spaarke/auth` → `src/client/shared/Spaarke.Auth/`
- `@spaarke/sdap-client` → `src/client/shared/Spaarke.SdapClient/`

Categories assessed:
- **Category 1** — Exact-name file duplicates across solutions
- **Category 2** — Semantic duplicates (different names, same concept)
- **Category 3** — Solution-local services bypassing shared libs

Fix-size scale:
- **TINY** — Single file × 2-3 copies, identical, mechanical move + import updates
- **SMALL** — 2-5 files, identical or near-identical, mechanical hoist
- **MEDIUM** — Files differ meaningfully — needs design discussion before hoisting (which variant becomes canonical? what's the API?)
- **LARGE** — Architecturally entangled — duplicates reveal a missing abstraction or layering issue

---

## Findings carried into R2 (NOT in scope for this follow-on project)

For reference. These are addressed in R2 and are documented here so the picture is complete:

| Finding | R2 FR | Notes |
|---|---|---|
| `MicrosoftToDoIcon.tsx` × 3 (LegalWorkspace, SmartTodo, DailyBriefing) | FR-19 | Hoist to `@spaarke/ui-components/src/icons/` |
| `authInit.ts` × 3 (DailyBriefing, LegalWorkspace, SpaarkeAi) | FR-20 | Consolidate into `@spaarke/auth` as `createCodePageAuthInitializer` |
| `runtimeConfig.ts` × 3 (DailyBriefing, LegalWorkspace, SpaarkeAi) | FR-21 | Consolidate into `@spaarke/auth` (or new `@spaarke/runtime-config`) |
| `notificationContextLoader.ts` semantic dup (SpaarkeAi mirrors DailyBriefing's `notificationService.ts`) | Subsumed by R2 P2 | Resolved automatically by Pattern D hoist — new package's hooks become the canonical implementation |

---

## Proposed scope — 10 deferred findings

### Category 1 — Exact-name file duplicates

#### Finding 1.1 — `xrmProvider.ts` × 2

- **Copies at**: `src/solutions/LegalWorkspace/src/services/xrmProvider.ts`, `src/solutions/SmartTodo/src/services/xrmProvider.ts`
- **Diff**: Near-identical (only logging labels differ: "LegalWorkspace" vs. "SmartTodo")
- **Shared-lib canonical exists**: No
- **Fix size**: TINY
- **Recommended target**: Hoist to `@spaarke/sdap-client` as `xrmProvider.ts` export, OR create new lightweight shared lib `@spaarke/xrm-provider` if `@spaarke/sdap-client` should remain narrowly scoped to OData/Dataverse API. Decide during this project's design phase.

#### Finding 1.2 — `ThemeProvider.ts` × 2

- **Copies at**: `src/solutions/LegalWorkspace/src/providers/ThemeProvider.ts`, `src/solutions/EventsPage/src/providers/ThemeProvider.ts`
- **Diff**: Identical except comments (both are thin wrappers around `@spaarke/ui-components`)
- **Shared-lib canonical exists**: Yes — the actual implementation is already in `@spaarke/ui-components`
- **Fix size**: TINY
- **Recommended target**: **Delete both copies**. Import the theme provider directly from `@spaarke/ui-components` in each solution's `App.tsx`. The wrapper layer adds no value.

#### Finding 1.3 — Smart Todo 8-component cluster

- **Copies at**:
  - `src/solutions/SmartTodo/src/components/{AddTodoBar, DismissedSection, EffortScoreCard, KanbanCard, KanbanHeader, PriorityScoreCard, ThresholdSettings, TodoAISummaryDialog}.tsx`
  - `src/solutions/LegalWorkspace/src/components/SmartToDo/{AddTodoBar, DismissedSection, EffortScoreCard, KanbanCard, KanbanHeader, PriorityScoreCard, ThresholdSettings, TodoAISummaryDialog}.tsx`
- **Diff**: Byte-for-byte identical except for the import line (LegalWorkspace imports from `./todoScoringTypes`, SmartTodo imports from `../hooks/useTodoScoring`)
- **Shared-lib canonical exists**: No (but `@spaarke/smart-todo-components` exists and is the right target)
- **Fix size**: SMALL (mechanical hoist of 8 files)
- **Recommended target**: Hoist all 8 components to `@spaarke/smart-todo-components`; both solutions consume from the shared lib.

#### Finding 1.4 — Smart Todo 3-util cluster

- **Copies at**:
  - `src/solutions/SmartTodo/src/utils/{dueLabelUtils.ts, navigation.ts, todoScoreUtils.ts}`
  - `src/solutions/LegalWorkspace/src/utils/{dueLabelUtils.ts, navigation.ts, todoScoreUtils.ts}`
- **Diff**: Byte-for-byte identical
- **Shared-lib canonical exists**: No
- **Fix size**: TINY
- **Recommended target**: Hoist to `@spaarke/smart-todo-components` (or top-level `@spaarke/todo-utils` if the utils belong in a more generic location).

#### Finding 1.5 — `queryHelpers.ts` (divergent)

- **Copies at**: `src/solutions/LegalWorkspace/src/services/queryHelpers.ts` (~560 LOC), `src/solutions/SmartTodo/src/services/queryHelpers.ts` (~551 LOC)
- **Diff**: Substantial overlap with divergence. LegalWorkspace includes `buildOwnerFilter()`, `IOwnershipContext`, portfolio-tab query builders (matters/projects/documents/invoices). SmartTodo is a minimal subset (todo-only).
- **Shared-lib canonical exists**: No
- **Fix size**: MEDIUM (requires design decision: is this a workspace-specific concern, or can it be genericized into an OData-builder library?)
- **Recommended target**: Split into `@spaarke/odata-builders` (generic FetchXML / OData query construction primitives) + solution-local extensions for portfolio-specific builders. Decide canonical API in design phase.

#### Finding 1.6 — `DataverseService.ts` (divergent)

- **Copies at**: `src/solutions/LegalWorkspace/src/services/DataverseService.ts`, `src/solutions/SmartTodo/src/services/DataverseService.ts`
- **Diff**: Substantial. Both are ~400+ lines of typed OData wrappers. LegalWorkspace includes extra queries for documents, invoices, matters, projects, and uses `buildOwnerFilter` (which SmartTodo doesn't). SmartTodo is todo-only.
- **Shared-lib canonical exists**: No
- **Fix size**: MEDIUM (requires separation of concerns: generic Xrm.WebApi wrapper vs. solution-specific queries)
- **Recommended target**: Hoist generic `WebApi` wrapper (request/response handling, retry, error normalization) to shared lib; keep solution-specific query methods local. This intersects with Finding 1.5 (queryHelpers) — design together.

### Category 2 — Semantic duplicates (different names, same concept)

#### Finding 2.1 — `useUserPreferences`

- **Locations**: `src/solutions/SmartTodo/src/hooks/useUserPreferences.ts`, `src/solutions/LegalWorkspace/src/hooks/useUserPreferences.ts`
- **Concept**: Fetches user preferences from Dataverse (`sprk_userpreference` entity)
- **Diff**: Likely near-identical (both fetch the same entity)
- **Fix size**: SMALL
- **Recommended target**: Hoist to `@spaarke/smart-todo-components` (if scope is todo-specific) or new `@spaarke/user-preferences` shared module (if generally applicable).

#### Finding 2.2 — `useTodoItems`

- **Locations**: `src/solutions/SmartTodo/src/hooks/useTodoItems.ts`, `src/solutions/LegalWorkspace/src/hooks/useTodoItems.ts`
- **Concept**: Fetches `sprk_todo` records via `Xrm.WebApi`, applies filtering/sorting
- **Diff**: Likely identical (core todo-fetch pattern)
- **Fix size**: TINY
- **Recommended target**: Hoist to `@spaarke/smart-todo-components`.

#### Finding 2.3 — `useFeedTodoSync`

- **Locations**: `src/solutions/SmartTodo/src/hooks/useFeedTodoSync.ts`, `src/solutions/LegalWorkspace/src/hooks/useFeedTodoSync.ts`
- **Concept**: Synchronizes todo state between activity feed and todo kanban
- **Diff**: Likely identical
- **Fix size**: TINY
- **Recommended target**: Hoist to `@spaarke/smart-todo-components`.

#### Finding 2.4 — `useWorkspaceLayouts`

- **Locations**: `src/solutions/SmartTodo/src/hooks/useWorkspaceLayouts.ts`, `src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts`, `src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts`
- **Concept**: Manages saved layout preferences for workspace panes
- **Diff**: Likely near-identical across all three
- **Fix size**: TINY
- **Recommended target**: Hoist to a shared infrastructure lib — this is cross-solution workspace infrastructure, candidate for `@spaarke/ui-components` (under a `WorkspaceLayouts/` folder) or `@spaarke/ai-widgets` (since SpaarkeAi consumes it heavily).

### Category 3 — Solution-local services bypassing shared

#### Finding 3.1 — Per-solution `DataverseService` wrappers bypass `@spaarke/sdap-client`

- **Observation**: LegalWorkspace + SmartTodo each implement their own typed `Xrm.WebApi` wrappers instead of consuming `@spaarke/sdap-client`.
- **Root cause**: `@spaarke/sdap-client` likely doesn't provide pre-built queries for matter/project/todo entities. Solutions extend it with custom typed wrappers because there's no generic way to express "give me a typed query for entity X."
- **Fix size**: MEDIUM-LARGE (architectural)
- **Recommended target**: Extend `@spaarke/sdap-client` with a query-builder pattern (entity schema → typed query) that solutions can use without re-implementing. Intersects with Findings 1.5, 1.6 — design together.

#### Finding 3.2 — Solution-local telemetry modules

- **Observation**: `src/solutions/SpaarkeAi/src/telemetry/errorTelemetry.ts` implements custom error tracking. `src/solutions/LegalWorkspace/src/services/telemetry.ts` is a separate module with a different contract.
- **Root cause**: No shared telemetry surface; each solution rolls its own.
- **Fix size**: MEDIUM (need to standardize a contract first)
- **Recommended target**: Standardize a `@spaarke/telemetry` module (or extend `@spaarke/auth` since it already does some App Insights integration) with a single `trackEvent`/`trackError`/`trackMetric` surface. Document via ADR if the contract is consequential.

---

## Summary

| # | Finding | Category | Fix size | Affected solutions |
|---|---|---|---|---|
| 1.1 | `xrmProvider.ts` | Exact-name dup | TINY | LegalWorkspace, SmartTodo |
| 1.2 | `ThemeProvider.ts` | Exact-name dup | TINY | LegalWorkspace, EventsPage |
| 1.3 | Smart Todo 8 components | Exact-name dup | SMALL | SmartTodo, LegalWorkspace |
| 1.4 | Smart Todo 3 utils | Exact-name dup | TINY | SmartTodo, LegalWorkspace |
| 1.5 | `queryHelpers.ts` (divergent) | Exact-name dup | MEDIUM | LegalWorkspace, SmartTodo |
| 1.6 | `DataverseService.ts` (divergent) | Exact-name dup | MEDIUM | LegalWorkspace, SmartTodo |
| 2.1 | `useUserPreferences` | Semantic dup | SMALL | SmartTodo, LegalWorkspace |
| 2.2 | `useTodoItems` | Semantic dup | TINY | SmartTodo, LegalWorkspace |
| 2.3 | `useFeedTodoSync` | Semantic dup | TINY | SmartTodo, LegalWorkspace |
| 2.4 | `useWorkspaceLayouts` | Semantic dup | TINY | SmartTodo, LegalWorkspace, SpaarkeAi |
| 3.1 | `DataverseService` bypass `@spaarke/sdap-client` | Local bypass | MEDIUM-LARGE | LegalWorkspace, SmartTodo |
| 3.2 | Local telemetry modules | Local bypass | MEDIUM | LegalWorkspace, SpaarkeAi |

- **Total deferred findings**: 12 line-items spanning 10 conceptual issues (some findings cluster together, e.g., 1.5 + 1.6 + 3.1 all converge on the Dataverse-access layer)
- **TINY**: 6 findings — ~300-500 LOC mechanical hoist work; quick standalone PRs
- **SMALL**: 2 findings — ~500-800 LOC mechanical hoist
- **MEDIUM / MEDIUM-LARGE**: 4 findings — require design phase (canonical API decisions) before hoist

---

## Suggested phasing for the follow-on project

If the owner approves this proposal as a separate project, a reasonable phasing:

| Phase | Workstream | Findings | Rationale |
|---|---|---|---|
| 1 | Quick wins — mechanical hoists | 1.1, 1.2, 1.4, 2.2, 2.3, 2.4 | Pure copy-and-update-imports; no design controversy; ~1-2 days each |
| 2 | Smart Todo consolidation | 1.3, 2.1 | All targets are `@spaarke/smart-todo-components`; consolidate in one PR |
| 3 | Telemetry standardization | 3.2 | Define a contract first; then hoist consumers |
| 4 | Dataverse-access layer redesign | 1.5, 1.6, 3.1 | The hardest; needs an ADR or design doc before code changes. Could be its own sub-project. |

---

## Risks

- **Sequencing collision with R2**: R2 (DailyBriefing Pattern D migration) is the immediate active project. If this follow-on project starts before R2 ships, the `@spaarke/smart-todo-components` package will see heavy concurrent activity (R2 doesn't touch it, but timing matters for review bandwidth). Recommend: schedule this follow-on AFTER R2 ships.
- **Phase 4 (Dataverse-access layer) is architecturally consequential** — touching `DataverseService` across LegalWorkspace and SmartTodo at the same time as `queryHelpers` could create merge-conflict churn. Plan a freeze window or coordinate with concurrent SmartTodo / LegalWorkspace projects.
- **Each MEDIUM finding (1.5, 1.6, 3.2) needs its own design discussion** — don't lump them into one big project plan; treat them as sub-projects with their own design.md / spec.md if needed.

---

## Recommendation

1. **Approve in principle** — the 10 findings are real, surfaced via systematic audit, and have clear ownership boundaries.
2. **Defer scheduling** until R2 ships (avoids sequencing collisions, especially around `@spaarke/smart-todo-components`).
3. **Treat the MEDIUM findings as design-discussion items** — don't approve the architectural changes (1.5, 1.6, 3.1, 3.2) without their own design phase.
4. **Quick wins first** — Phase 1's TINY findings could be a Week-1 cleanup PR sequence with very low risk and high signal value.

---

*This proposal originated from the audit conducted during R2 scoping on 2026-06-18. See [`spec.md`](spec.md) for the R2 project that addresses the 4 high-proximity findings.*
