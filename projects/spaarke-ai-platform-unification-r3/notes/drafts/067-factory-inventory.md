# Task 067 — Factory Portability Inventory

> **Date**: 2026-05-20
> **Task**: 067 — Hoist workspace section registry + builder to `@spaarke/ui-components`
> **Author**: Claude Code (task-execute, FULL rigor)

---

## Summary

Re-investigated all 6 LegalWorkspace section factories. **None are factory-pure** — every
factory imports at least one solution-local React component or hook. The diagnostic
agent's pre-investigation claim that "4-5 factories are PORTABLE" was incomplete — what is
portable is the **shape of the registry, the builder, and the data constants**, not the
factory render bodies.

This inventory rewrites the task plan accordingly:

| Factory | Local imports | Portability verdict | Action |
|---------|---------------|---------------------|--------|
| `getStarted` | `ACTION_CARD_CONFIGS` (pure data — icons + strings) | **PARTIALLY portable** — data is movable, but the cards reference Code Page wizard webresources (`sprk_creatematterwizard`, etc.) which are LegalWorkspace-domain. | **Leave solution-local.** Hoisting would couple shared lib to legal-domain web resource names. |
| `quickSummary` | `QuickSummaryRow` (data-bound, Xrm.WebApi + LegalWorkspace queries) | **Not portable** — pulls legal-domain queries (matters/projects/invoices). | **Leave solution-local.** |
| `latestUpdates` | `ActivityFeed` (Xrm.WebApi + sprk_event queries) | **Not portable** — legal-domain feed wiring. | **Leave solution-local.** |
| `todo` | `SmartToDo` (kanban + sprk_todo entity + DnD) | **Not portable** — legal-domain to-do entity + @hello-pangea/dnd dependency not in shared lib. | **Leave solution-local.** |
| `documents` | `DocumentsTab` + `DataverseService` + `IViewDefinition` | **Not portable** — legal-domain document entity + per-view picker logic + DataverseService dep. | **Leave solution-local.** |
| `dailyBriefing` | `DailyBriefingSection`, `useDailyBriefing` (BFF AI call + telemetry) | **Not portable** — AI feature stack (per ADR-013 BFF placement governance, hoisting AI dependencies into shared lib expands the AI-coupling surface). | **Leave solution-local.** Option L from POML step 5. |

## What IS portable (and SHOULD be hoisted)

1. **`buildDynamicWorkspaceConfig`** — pure function, type-only deps on `@spaarke/ui-components`.
2. **`SYSTEM_DEFAULT_LAYOUT_JSON`** — pure data constant.
3. **`LayoutJson` / `LayoutJsonRow` / `WorkspaceScope`** — type definitions.
4. **`countSlots` helper** — pure regex util.

## What SpaarkeAi gains from this refactor

After hoist:
- `buildDynamicWorkspaceConfig` + `SYSTEM_DEFAULT_LAYOUT_JSON` available from `@spaarke/ui-components`.
- SpaarkeAi can build its own MINIMAL registry of placeholder sections (matching the user's
  layout JSON section IDs) and pass them to `buildDynamicWorkspaceConfig`. Result: layout
  STRUCTURE renders correctly (rows + columns + section IDs), content remains placeholders.
- This is a strict improvement over the current state: SpaarkeAi now reuses the SAME builder
  logic the standalone LegalWorkspace uses, eliminating the parallel `buildFoundationalConfig`
  divergence in `WorkspaceHomeTab.tsx`.

## What this refactor does NOT solve

- **Bug 2 in the original sense ("no data showing for workspace widgets")** — NOT fully fixed.
  SpaarkeAi still cannot render legal-domain section CONTENT (Quick Summary metrics, Activity
  Feed items, Document cards) without consuming legal-domain components.
- **ADR-012 cross-cutting limitation** — PARTIALLY addressed: the registry SHAPE + BUILDER
  are now in the shared lib; legal-domain factories remain in LegalWorkspace. Full resolution
  requires either (a) hoisting legal-domain components to the shared lib (high blast radius —
  brings sprk_event/sprk_document/sprk_todo entity contracts into shared lib, violates
  ADR-012 "MUST NOT hard-code Dataverse entity names"), or (b) building a thinner
  "context-agnostic" section catalog parallel to the legal-domain one.

## Recommended path

**This task delivers the foundation; full section-content render for SpaarkeAi requires
follow-on work** (likely a new task or task family). The deliverables here are:

1. Hoist `buildDynamicWorkspaceConfig` + `SYSTEM_DEFAULT_LAYOUT_JSON` + types to shared lib.
2. Export from `@spaarke/ui-components` barrel.
3. LegalWorkspace's `buildDynamicWorkspaceConfig.ts` becomes a re-export from shared lib
   (zero behavior change — FR-25/NFR-10 safe).
4. Rewrite `WorkspaceHomeTab.tsx` to use the hoisted builder with a SpaarkeAi-local
   "context-agnostic placeholder registry" so the layout structure renders via the canonical
   builder (eliminating the parallel `buildFoundationalConfig` code path).
5. Document this in a wrap-up memo explaining the partial vs full ADR-012 resolution.

The cross-cutting limitation is now **architecturally addressed** (shared lib owns the
canonical builder + types); the residual "no section content in SpaarkeAi" is **scoped as
follow-on work** because resolving it requires either component hoisting (large blast radius)
or a parallel context-agnostic section catalog (new design work).

## Decision

Proceed with hoisting the PURE infrastructure (builder, system default layout, types).
Leave all 6 factories solution-local. Rewrite SpaarkeAi `WorkspaceHomeTab` to use the
canonical builder with a local placeholder registry. Document the partial resolution.
