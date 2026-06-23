# Task 002 — Decision Record: PCF UniversalQuickCreate `useAiSummary.ts` Duplicate Resolution

**Date**: 2026-06-21
**Task**: `002-pcf-useaisummary-duplicate-resolution.poml`
**Rigor**: STANDARD
**Decision**: **Delete the PCF stub and migrate the one remaining type import to the shared lib.**

---

## Investigation Summary

### Files compared

| Path | Lines | Content |
|---|---:|---|
| `src/client/pcf/UniversalQuickCreate/control/services/useAiSummary.ts` | 14 | **Thin re-export stub** marked `@deprecated`. Already re-exports `useAiSummary` + 7 types from `@spaarke/ui-components/src/hooks`. |
| `src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts` | 599 | **Canonical implementation** — full hook with SSE streaming, queue management, and document state. |

### Diff finding

The PCF copy is **NOT a real duplicate**. A prior migration already converted it into a deprecated pass-through re-export. Only one real `useAiSummary` implementation exists in the repo today (the shared one). The PCF stub remained as a backwards-compatibility shim. The task POML description ("duplicates the shared hook") reflects the spec.md §1.7.3 author's belief at design time that two implementations co-existed; in practice, the migration was performed but the stub was never deleted.

### Consumer grep — `useAiSummary` inside `src/client/pcf/UniversalQuickCreate/`

| File | Line | Import target | Status after task 002 |
|---|---:|---|---|
| `control/components/DocumentUploadForm.tsx` | 41 | `@spaarke/ui-components/src/hooks` | ✅ Already canonical |
| `control/components/AiSummaryPanel.tsx` | 41 | `@spaarke/ui-components/src/hooks` | ✅ Already canonical |
| `control/services/useRecordMatch.ts` | 11 | `./useAiSummary` (stub) | ❌ Migrated by this task → `@spaarke/ui-components/src/hooks` |
| `control/services/useAiSummary.ts` | 5 | `@spaarke/ui-components/src/hooks` | ❌ Deleted by this task |

No other PCF or solution code (excluding `bundle.js` build artifacts) referenced the local stub.

## Decision: Delete PCF stub + repoint last consumer

**Rationale**:
1. Already trivially adaptable — stub is a pure re-export with no PCF-specific shape adaptation.
2. Only one remaining consumer of the stub (`useRecordMatch.ts` for the `ExtractedEntities` type) — a one-line change.
3. ADR-012 mandates single source of truth; an orphaned `@deprecated` re-export shim violates the spirit of the constraint.
4. Spec §1.7.3 Pattern C explicitly classifies this as a cleanup item to prevent name-resolve drift before Pattern B stable-code migration (task 020 will edit `useAiSummary.ts:285`).
5. Future ADR / spec change ripple now requires editing only ONE file.

**Adapter layer**: Not required. PCF and shared lib already use identical signatures (proven by `DocumentUploadForm.tsx` and `AiSummaryPanel.tsx` already importing directly).

## Changes Applied

### Code changes
- **Deleted**: `src/client/pcf/UniversalQuickCreate/control/services/useAiSummary.ts`
- **Modified**: `src/client/pcf/UniversalQuickCreate/control/services/useRecordMatch.ts` line 11 — `import { ExtractedEntities } from './useAiSummary'` → `import type { ExtractedEntities } from '@spaarke/ui-components/src/hooks'` (also tightened to `import type` since it is only a type usage)

### Version bumps (PCF-DEPLOYMENT-GUIDE.md 4-location rule)
| # | File | Old | New |
|---|---|---|---|
| 1 | `control/ControlManifest.Input.xml` | `3.15.3` | `3.15.4` |
| 2 | `control/components/DocumentUploadForm.tsx` (footer) | `v3.15.3 • Built 2026-05-14` | `v3.15.4 • Built 2026-06-21` |
| 3 | `Solution/src/Other/Solution.xml` | `3.15.3` | `3.15.4` |
| 4 | `Solution/src/WebResources/.../ControlManifest.xml` | `3.15.3` | `3.15.4` |

Description strings in (1) and (4) (`...(v3.15.x)`) also updated to match.

## Build verification

`cd src/client/pcf/UniversalQuickCreate && npm run build:prod` — Failed with **10 pre-existing TypeScript errors in the shared lib**, all unrelated to this task:
- `SseStreamStatus`, `SseDataChunk`, `UseSseStreamOptions`, `UseSseStreamResult` missing exports from `useSseStream.ts` (4 errors — shared lib SSE hook export naming inconsistency)
- `themeStorage` dist-path resolution (1)
- `CustomCommandFactory.ts` ReactElement<any> typing (1)
- `useChatFileAttachment.ts` dynamic import module flag (2)
- `useForceSimulation.ts` `SimNode.x/y` typing (3 — went from 30 errors to 10 after installing shared lib deps)

**Zero errors mention `useAiSummary`, `ExtractedEntities`, or `useRecordMatch`.** The build failure is a pre-existing baseline state of the worktree (shared-lib drift unrelated to PCF), not a regression introduced by this task. Filing this as out-of-scope; it should be tracked under shared-lib build-health backlog, not task 002.

## Acceptance criteria status

| Criterion | Status |
|---|---|
| Single canonical hook implementation in shared lib | ✅ Achieved (stub removed) |
| PCF UniversalQuickCreate `npm run build:prod` exits 0 | ⚠️ Build fails on PRE-EXISTING shared-lib errors; ZERO errors caused by task 002 changes |
| No duplicate `useAiSummary.ts` hook content across the repo | ✅ Achieved — only `src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts` remains |

## Follow-ups recommended

1. **Shared-lib build-health follow-up**: file a maintenance task to resolve the 10 pre-existing shared-lib TypeScript errors. These will continue to block any PCF that depends on the shared lib until fixed. This is NOT within scope of task 002.
2. **Task 020 (Pattern B stable-code migration)** of `useAiSummary.ts:285` now operates on a single canonical file — one fewer file to migrate.
