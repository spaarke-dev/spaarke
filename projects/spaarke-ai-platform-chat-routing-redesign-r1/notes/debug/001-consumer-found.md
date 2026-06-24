# Task 001 — Consumer-Found Finding

**Date**: 2026-06-21
**Task**: `001-delete-legalworkspace-creatematter-deadcode.poml`
**Status**: HALTED at Step 1 per task constraint and parent-agent instruction
**Rigor**: STANDARD
**PR #406 overlap**: NONE (verified via `gh pr view 406 --json files`)

## Summary

Task 001 was halted at Step 1 (grep for consumers) because:

1. **`CreateMatter/CreateRecordStep.tsx` HAS production consumers** within the LegalWorkspace solution.
2. **`CreateProject/CreateRecordStep.tsx` and `CreateWorkAssignment/CreateRecordStep.tsx` DO NOT EXIST** in the current tree (already removed by prior cleanup, presumably during R4 OC-R4-05 retirement work).

No file was deleted. Build was not run.

## Detailed findings

### File existence audit

| Target path | Exists? | Size |
|---|---|---|
| `src/solutions/LegalWorkspace/src/components/CreateMatter/CreateRecordStep.tsx` | ✅ YES | 23,976 bytes |
| `src/solutions/LegalWorkspace/src/components/CreateProject/CreateRecordStep.tsx` | ❌ NO | n/a — already removed |
| `src/solutions/LegalWorkspace/src/components/CreateWorkAssignment/CreateRecordStep.tsx` | ❌ NO | n/a — already removed |

The `CreateProject/` folder retains `CreateProjectStep.tsx` (different name); the `CreateWorkAssignment/` folder retains `EnterInfoStep.tsx` / `SelectWorkStep.tsx` (different names). The task POML's filename expectation for Project + WorkAssignment is stale.

### Consumer grep result for `CreateMatter/CreateRecordStep.tsx`

```
src\solutions\LegalWorkspace\src\components\CreateMatter\WizardDialog.tsx:27: import { CreateRecordStep } from './CreateRecordStep';
src\solutions\LegalWorkspace\src\components\CreateMatter\index.ts:17: export { CreateRecordStep } from './CreateRecordStep';
```

Plus barrel re-export at `src\client\shared\Spaarke.UI.Components\src\components\CreateMatterWizard\index.ts:19` — but that is a DIFFERENT file in the shared lib package (`@spaarke/ui-components`), not the LegalWorkspace one. The shared lib `CreateMatterWizard/CreateRecordStep` is alive and consumed by `src/solutions/CreateMatterWizard/src/main.tsx` (the standalone wizard solution) and `CreateMatterWizardWidget.tsx` (the widget). The shared-lib version is NOT in scope for this deletion.

### PR #406 overlap

`gh pr view 406 --json files --jq '.files[].path'` — filtered for CreateRecordStep/CreateMatter/CreateProject/CreateWorkAssignment: **NO_OVERLAP**. PR #406 does not touch any of these paths.

### Why deletion would break the build

`CreateMatter/CreateRecordStep.tsx` is imported by:
- `CreateMatter/WizardDialog.tsx` (line 27)
- `CreateMatter/index.ts` (line 17 — barrel re-export)

These two consumers ARE part of the same dead-code island per OC-R4-05 (LegalWorkspace standalone code-page retired; entry points `main.tsx`/`App.tsx` do NOT reference `WizardDialog` or `CreateMatter/index.ts`). External grep across `src/` confirms no consumer outside the `CreateMatter/` folder references either `WizardDialog` or the `CreateMatter` barrel.

However, `npm run build` (Vite + TypeScript) WILL compile every `.tsx` file in `src/` regardless of reachability. Deleting only `CreateRecordStep.tsx` while leaving `WizardDialog.tsx` (which imports it) and `index.ts` (which re-exports it) in place WILL produce a TypeScript compile error and break the build.

The task POML's scope is too narrow to safely complete: it instructs deleting 3 files (only 1 of which exists), but a complete Pattern C cleanup of the `CreateMatter/` dead-code island requires deleting (or modifying) at least:
- `CreateMatter/CreateRecordStep.tsx`
- `CreateMatter/WizardDialog.tsx`
- `CreateMatter/index.ts` (or removing the `CreateRecordStep`/`WizardDialog` exports from it)

…and likely the surrounding step components (`FileUploadZone.tsx`, `LookupField.tsx`, `AiFieldTag.tsx`, `NextStepsStep.tsx`, `AssignCounselStep.tsx`, `AssignResourcesStep.tsx`, `DraftSummaryStep.tsx`, `SendEmailStep.tsx`, `matterService.ts`, etc.) which are part of the same retired wizard.

**Caveat for follow-up**: `CreateMatter/matterService.ts` IS still consumed externally by `CreateEvent/EventWizardDialog.tsx`, `CreateProject/ProjectWizardDialog.tsx`, `CreateWorkAssignment/WorkAssignmentWizardDialog.tsx`, `CreateWorkAssignment/workAssignmentService.ts`, `FilePreview/FilePreviewDialog.tsx` (via `searchUsersAsLookup`, `searchOrganizationsAsLookup`, etc.). The matterService and the lookup-search exports MUST be preserved or extracted to a neutral location BEFORE the rest of `CreateMatter/` can be deleted.

## Recommended next steps

Pivot task 001 from "narrow file deletion" to "Pattern C cleanup of the CreateMatter dead-code island." Re-scope as:

1. **Verify with project owner** that the entire `CreateMatter/` wizard (the WizardDialog standalone path) is in fact retired and not still wired to any host that survived OC-R4-05.
2. **Inventory the dead-code island boundary**: walk imports from `WizardDialog.tsx` to determine all transitively-only-internal files that can be safely deleted. Files reused outside the island (notably `matterService.ts` lookups, `wizardTypes.ts` if it leaks types) must be extracted/preserved.
3. **Refile a revised task** that either:
   - Deletes the full island in one atomic commit (preferred — keeps build green), OR
   - Punts Pattern C cleanup for `CreateMatter/` to a later wave once consumer migration (Pattern A/B) has reduced cross-references to `matterService.ts`.
4. **Update task 001 status** to 🚧 BLOCKED in TASK-INDEX.md (main session owns this) pending owner re-scope decision.

## Recommended status for TASK-INDEX

**🚧 BLOCKED** — narrow file deletion as specified would break `npm run build`; cannot complete without re-scope. Two of three target files don't exist; the third has in-island consumers that also need handling.

## Files NOT modified

No files were deleted, edited, or created (other than this finding). `npm install` and `npm run build` were NOT run.
