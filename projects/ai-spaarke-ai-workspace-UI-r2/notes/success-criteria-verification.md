# Success criteria verification — ai-spaarke-ai-workspace-UI-r2

**Date**: 2026-07-01
**Scope**: 14 success criteria per [`spec.md § Success Criteria`](../spec.md#success-criteria)

## Legend

- ✅ **Static-verified** (grep / static analysis / code inspection)
- 🔄 **Pending** (requires post-merge / post-deploy manual QA)
- ⚪ **N/A** (does not apply given operator decision)

## Table

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | Every workspace widget row-click uses Layout 1 | ✅ static / 🔄 manual QA | Grep of `DataGrid.tsx` for `window.open` returns 1 result — line 921, a docstring citation of the RETIRED code path (not code). Phase 1 `phase1-verification.md` § 3 checklist covers post-deploy manual verification. |
| 2 | `formId` capability honored when present | ✅ static / 🔄 manual QA | `DataGrid.recordOpen.test.ts` "forwards rowOpen.formId as pageInput.formId when set" passes. Post-deploy: create a `sprk_gridconfiguration` record with `formId` GUID; click a row; confirm form variant. No such config record exists yet (out of R2 scope per audit). |
| 3 | Absent `formId` falls back to user default | ✅ static | `buildRecordOpenNavArgs` unit test "omits formId from pageInput when rowOpen.formId is absent" passes. All 5 existing config records have `formId` absent (per Task 001 audit); post-Phase-1 behavior identical to pre-Phase-1 for these records. |
| 4 | Communications widget renders correctly | 🔄 manual QA | Static: config record exists in Dataverse (GUID `e1826c4c-9575-f111-ab0e-7ced8ddc4a05`); direct widget `communications-list` registered; savedquery `2bf1c5a5-…` is the OOB `Active Communications` view. Manual QA: mount `communications-list` in a Workspace tab, verify columns and row-click. |
| 5 | Communications section usable in `WorkspaceLayoutWizard` | ✅ static | `sectionMetadataCatalog.ts` entry added (id `communications`, `MailRegular` icon); `communicationsRegistration` added to the factory array in `sectionRegistry.ts`; drift guard in `sectionRegistry.ts:141-179` will verify at dev-mode load. Manual QA: open wizard, verify Communications appears in picker. |
| 6 | SmartTodoModal retired | ✅ static | `git rm -r src/solutions/SmartTodo/src/components/Modal/` executed in task 022. Grep for `SmartTodoModal` returns only comment/docstring citations of the retirement (no code imports or JSX). |
| 7 | Documents preview unchanged (Layout 2) | ✅ static | `RichFilePreviewDialog.tsx` untouched by R2 (`git status` on the file shows clean). Dimensions preserved at `max-width: 1280px`, `height: 85vh` (grep-verified). |
| 8 | `RecordNavigationModalShell` retained + unchanged | ✅ static | `git status` on `src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/` shows no changes. Barrel export intact at `components/index.ts:146`. Public prop contract unchanged. |
| 9 | All 5 doc surfaces updated | ✅ static | Task 023 delivered all 5 changes to `MODAL-DECISION-CRITERIA.md`, `.claude/patterns/ui/record-modal-selection.md`, `BUILD-A-NEW-WORKSPACE-WIDGET.md § 6.6`, `SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md § 6.5`, `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md § 6.5`. MS Learn 2025-05-07 quote present verbatim; two-layout language consistent. |
| 10 | No iframe of `main.aspx` in production code | ✅ static | Grep for `main.aspx` in `src/solutions/`: 4 files found. `SmartTodoApp.tsx` — comments only (citing the retirement). `filePreviewService.ts`, `SuccessScreen.tsx`, `NextStepsStep.tsx` — unrelated to R2 scope (LegalWorkspace file preview + DocumentUploadWizard success screens); these are separate concerns, not iframe-embedded `main.aspx` records. R2 scope clean. |
| 11 | CI green after each PR | 🔄 pending | PR #530 has 13 CI checks running at time of commit push; not blocked on wait per operator directive. Reviewer verifies before merge. |
| 12 | Each PR independently mergeable | ⚪ N/A (operator decision) | Per operator direction 2026-07-01 ("don't wait for CI — proceed"), Phase 1 + 2 + 3 folded into PR #530. Original plan was 3 independent PRs; combining was the operator's mid-session choice. If reviewer prefers split, I can create stacked PRs. |
| 13 | Layout 1 modal size is 85% × 85% everywhere | ✅ static | Grep for `value: 85, unit: '%'` finds 12 usages across `DataGrid.tsx`, `SmartTodoApp.tsx`, `todo.registration.ts` (all R2-touched), plus 9 pre-existing usages in other consumers. Grep for `value: 80, unit: '%'` on R2-touched files → 0 results. FR-20 grep-verified. |
| 14 | Layout 2 dimensions preserved | ✅ static | `RichFilePreviewDialog.tsx` lines 149, 166-167: `DIALOG_MAX_WIDTH = '1280px'`, `height: '85vh'`, `maxHeight: '85vh'`. FR-21 grep-verified. |

## Summary

| Class | Count |
|---|---|
| ✅ Static-verified | 10 (criteria 3, 5, 6, 7, 8, 9, 10, 13, 14, part of 1 + 2) |
| ✅ + 🔄 manual QA required for full pass | 4 (criteria 1, 2, 4, 5 partial) — post-deploy actions in [`phase1-verification.md`](phase1-verification.md) checklist |
| 🔄 Pending (post-merge / CI) | 1 (criterion 11) |
| ⚪ N/A (operator decision) | 1 (criterion 12) |

**Overall**: R2 code-side complete. **Static verification: PASS**. Remaining criteria are gated on PR #530 merge + post-deploy manual QA per [`phase1-verification.md § 3 checklist`](phase1-verification.md#3-manual-qa-checklist-post-deploy).
