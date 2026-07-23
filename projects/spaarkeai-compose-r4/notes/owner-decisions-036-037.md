# Owner Decisions — Tasks 036 & 037 (Phase-3 boundary product decisions)

> **Date**: 2026-07-22
> **Decided by**: Owner (ralph.schroeder) via orchestrator AskUserQuestion
> **Context**: Both tasks were deferred `<owner-decision-required>` product decisions surfaced at the
> Phase-3 boundary (see `notes/task-032-pushannotations-scope.md` + `notes/task-033-table-operation-gap.md`).
> Both block Success Criterion 7 (one byte-author, I-5) + task 060 (hard-replace completion).

---

## Task 036 — push-to-Word annotations (FR-24 / DocxAnnotationWriter)

**DECISION: Path B — RETIRE.**

Remove the push-annotations surface entirely:
- Server: endpoints (9) `POST /api/compose/document/{id}/push-annotations` + (9b) push-preview (FR-28);
  `ComposeService.PushAnnotationsAsync` + `PreviewPushAnnotationsAsync`; `ComposePushSavePreviewCalculator`.
- Client: `useComposePushAnnotations` (in `useComposeWordShuttle.ts`), the Word-shuttle "push to Word" leg,
  the `index.ts` export, and associated tests.
- Then delete `DocxAnnotationWriter.cs` (incl. `LocateTarget`) + `DocxAnnotation.TargetText` cleanly.

**Rationale**: `DocxAnnotationWriter` is the LAST text-anchored byte-author (locates edits by whole-document
text search — the exact behavior invariant I-7 bans and the root cause of the old 422s). R4 already persists
editor edits as native Word tracked-changes via `ComposeShadowPatchEngine`, making the standalone "push to
Word" shuttle redundant. Retiring completes one-byte-author (I-5) and removes the last text-search author.
This is a **breaking change** (removes a deployed FR-24 capability) — signed off by the owner per root §6/§6.5.

## Task 037 — born-in-editor tables (FR-09 / op-schema gap)

**DECISION: Path C — IMPORT-ONLY tables.**

Disable the `insertTable` toolbar command when there is no loaded baseline (no `documentSpeId`); tables become
import-only (present in uploaded docs, never authored fresh from an empty editor). Then unify born-in-editor
onto the op model (no table authoring case), retiring the second byte-author path for born-in-editor.

**Rationale**: The closed 10-op schema (FR-11 spine) has no table primitive, and extending it (Path B) is a
3-5 day spine change with an architectural risk (a nested table sub-model may strain the flat
`(paraId,runIndex,offset)` anchor contract). Import-only lets one-byte-author (I-5) hold literally with
minimal risk and ships R4 fastest. This is a **product-visible feature regression** (drops new-doc table
authoring) — signed off by the owner per root §6/§6.5. A future project may add table authoring (Path B) if
UAT shows the born-in-editor table workflow is needed.

**Task 033 folds into 037**: with tables import-only, FR-09 born-in-editor unification has no table case, so
033 completes as part of 037.

---

## Execution order (post-decision)

`036 (retire)` → `037 (import-only, folds in 033)` → `060 (hard-replace completion / remove mammoth)` →
`061 (corpus proof + size + CVE + NetArch)` → **STOP at deploy boundary**. Tasks `035` (dev deploy) + `062`
(full R4 deploy + CIPO UAT) are owner-orchestrated deploys, not run autonomously. `063` (flagship gate) +
`090` (wrap-up) follow the deploy.
