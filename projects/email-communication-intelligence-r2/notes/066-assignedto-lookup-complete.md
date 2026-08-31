# Task 066 — E3b New-task Assigned-to OOB advanced-lookup (complete)

> **Completed**: 2026-08-11 · FULL rigor · sonnet · Step 9.5 clean · plan §9 (build FIRST of 064/065/066).

## What changed
`TaskReconcileTab.tsx` (shared lib only — no host prop, no BFF):
- Assigned-to field: raw text `Input` → OOB advanced-lookup + editable text fallback. New `openAssignedToLookup(key)` reuses the shipped `getXrmForPicker().Utility.lookupObjects({ entityTypes:['systemuser','team'], defaultEntityType:'systemuser', allowMultiSelect:false })` pattern (exact mirror of `EmailConnectionsReview.tsx:157-176`), with the same non-MDA guard + id normalization (`.replace(/[{}]/g,'').toLowerCase()`).
- On pick: `patchForm(key,{assignedTo:id})` + store the display name in UI-only `assignedNames` state → shows the name + a "Change" affordance. Payload unchanged (`buildApplyBody`/`buildAdHocBody` still send the id).
- Applies to BOTH proposal cards and the ad-hoc "+ New task" form (shared `renderFields`).
- Imports: `getXrmForPicker` from `@spaarke/ui-components`, `SearchRegular` icon.

## Verify
- tsc 0-err (via the new prebuild chain — auth+ui-components rebuilt first).
- jest **15/15** (`TaskReconcileTab`): 13 existing + 2 new — (a) systemuser pick → name shown + normalized id in the 034 apply body + `lookupObjects` called with `entityTypes:['systemuser','team']`; (b) non-MDA host → lookup no-ops, text-input fallback stays editable.
- Step 9.5: code-review CLEAN, adr-check CLEAN (ADR-021 tokens; ADR-012 via the shared getXrmForPicker bridge).

## Next (plan §9)
065 (E2b typed controls + E2c "+ Update other fields") → 064 (E1b + E1c BFF resolver).
