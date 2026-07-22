# Task 032 push-annotations scope discovery — RESOLVED via Path A

> **Date**: 2026-07-22
> **Status**: ✅ **RESOLVED (Path A)** by the orchestrator. 032 re-scoped to the SAVE path only:
> retire `ComposeParagraphRedlineSynthesizer` (fully) + move the save path off `DocxAnnotationWriter`
> onto the engine + wire the client op-log send. `DocxAnnotationWriter` deletion + the deployed
> **FR-24 push-annotations** surface are DEFERRED to **new task 036**, where the retire-vs-migrate
> (Path B vs C) choice is an **owner product decision** (flagged, deferred — not blocking). Interim
> two-byte-author coexistence (engine for save, `DocxAnnotationWriter` for push-annotations) is a
> documented §6.5 Path-A exception, closed when 036 lands (required for Success Criterion 7 / I-5).
> Original coverage-check analysis below.
> **Raised at (original)**: POML Step 2 coverage-check BEFORE any deletion. No code was changed.

---

## Preconditions — ALL GREEN (this is NOT a gate/precondition failure)

| Precondition | Status | Evidence |
|---|---|---|
| Task 006 Phase 0 gate green (FR-12 / NFR-08) | 🟢 | `notes/phase0-gate-decision.md` — cutover AUTHORIZED |
| Tasks 030 + 031 complete (engine + structural ops) | ✅ | `tasks/TASK-INDEX.md` rows 030/031 ✅ |
| EDGE-1…4 wisdom migrated INTO `ComposeShadowPatchEngine` | ✅ (grep-confirmed) | EDGE-1 comments-first in `PatchSession.Execute`; EDGE-3 monotonic ids in `SeedRevisionId`; EDGE-4 `w:delText` in `WrapRunAsDeleted`; para-mark deletion in `MarkParagraphMark`; whole-paragraph strike in `ApplyDeleteParagraph` |

The deletion is authorized to *begin evaluation*. The block is raised at the task's own **Step-2
coverage check**, exactly as the POML `<escalation>` trigger prescribes.

## What the coverage check CONFIRMS is covered (the save-path writers)

Both **save-path** behaviors of the two writers ARE covered by the engine, so — for the SAVE path
alone — the cutover would be sound:

| Legacy save-path behavior | Old writer | Engine coverage |
|---|---|---|
| Word-diff redline of an edited paragraph | `ComposeParagraphRedlineSynthesizer.SynthesizeRedline` | Superseded by step-level `insertText`/`deleteRange`/`replaceRange` ops (client emits granular ops; no server-side diff needed) |
| Whole-paragraph strike (`{paraId,text:''}` sentinel) | synthesizer via `WordDiff(old,'')` | `ApplyDeleteParagraph` (settled runs → `w:del` + para-mark `w:del`) |
| Inserted/deleted/comment native OOXML | `DocxAnnotationWriter.Annotate` | `ApplyInsertText`/`ApplyDeleteRange`/`ApplyComment` (`ComposeAnchoredComment`) |
| EDGE-1 comment-before-trackchange, EDGE-3 ids, EDGE-4 `w:delText` | `DocxAnnotationWriter` | Migrated into `PatchSession` (task 030) |

## The blocker — an out-of-scope, text-anchored surface the deletion pulls in

`DocxAnnotationWriter` is **not only** a save-path writer. It is also the byte-author of the
**deployed FR-24 "push-annotations" surface**, which the 032 POML scope (SAVE path + `triggerSave`
op-log send) does **not** mention and gives **no** instruction for. The acceptance criterion
"`DocxAnnotationWriter` removed, ZERO call sites" cannot be satisfied while this surface exists.

### Live call sites of `DocxAnnotationWriter` OUTSIDE the save path (code-verified)

- **Server** — `ComposeService.PushAnnotationsAsync` (`Services/Compose/ComposeService.cs:1515`):
  `annotatedBytes = _annotationWriter.Annotate(sourceBytes, request.Annotations);` — the FR-24
  "render accepted annotations to native Word track-changes + comments, push to SPE with If-Match"
  pipeline. Backed by endpoint **(9) `POST /api/compose/document/{documentSpeId}/push-annotations`**
  (`ComposeEndpoints.cs:154`), mapped **unconditionally**.
- **Client** — `useComposePushAnnotations` (`widgets/useComposeWordShuttle.ts:353`) POSTs
  text-anchored `DocxAnnotation` entries to `/push-annotations`; **exported from `index.ts:105`**
  and consumed by the Word-shuttle "push to Word" UX. Tests: `useComposeWordShuttle.test.tsx:130`.
- **Related** `DocxAnnotation` DTO + `DocxAnnotation.TargetText` are also woven through
  `PushAnnotationsBody`/`PushAnnotationsRequest`, `PreviewPushAnnotationsRequest.Annotations`, and
  `ComposePushSavePreviewCalculator` (push-preview endpoint (9b), FR-28).

### Why it can't be absorbed silently (the architectural tension)

Push-annotations is **text-anchored by contract** (`DocxAnnotation.TargetText` — the "existing
document text the annotation targets"). Feeding it to `ComposeShadowPatchEngine` requires
`(paraId, runIndex, run-local-offset)` anchors. Converting text anchors → op anchors **server-side**
is exactly the whole-document text-search the engine forbids (invariant **I-7**;
`DocxAnnotationWriter.LocateTarget` is the 422 root cause being deleted). So push-annotations cannot
be routed through the engine without the **client** first emitting op-anchored input — a change well
beyond "wire `triggerSave` to send the op-log," which is all scope B authorizes.

### Root-cause of the omission

The task's own as-built inventory (`notes/as-built-inventory.md`) frames `DocxAnnotationWriter`
purely as "**one of two save paths**" and does not record its **FR-24 push-annotations role**. The
POML inherited that framing, so scope A/B/D address the save path but not the push surface that also
depends on the class marked for deletion. This is a genuine gap in the task's premise, not a
delete-and-hope opportunity.

## Resolution options (root §6.5 + §11 — orchestrator/owner choice)

- **(A) Re-scope: split push-annotations retirement into its own task, keep 032 to the SAVE path.**
  Land the SAVE-path cutover (invert `SaveAsync`/endpoint to the op-log contract, wire `triggerSave`
  op-log send, migrate save-path `Annotations`→`ComposeAnchoredComment`) but **defer the physical
  deletion of `DocxAnnotationWriter`** until push-annotations is retired/migrated in a dedicated
  task (candidate: fold into 060 "hard-replace cutover completion", or a new 03x). 032's
  acceptance criterion "both classes removed" is amended to "`ComposeParagraphRedlineSynthesizer`
  removed; `DocxAnnotationWriter` deletion tracked by task {new}." Cleanest; no half-migrated save
  contract; no un-signed-off retirement of a deployed feature. **Recommended.**
- **(B) Expand 032 scope: retire the push-annotations surface entirely** (server endpoints 9/9b +
  `PushAnnotationsAsync`/`PreviewPushAnnotationsAsync` + client `useComposePushAnnotations` +
  `useComposeWordShuttle` push leg + tests), then delete `DocxAnnotationWriter` cleanly. This is a
  **product decision** (removing a deployed FR-24 capability) and needs explicit owner sign-off; it
  roughly doubles the task's blast radius.
- **(C) Expand 032 scope: migrate push-annotations to the op-log contract** (client emits
  op-anchored input for the push path too; server routes it through the engine). Largest change —
  a full client-UX rewrite of the accepted-annotations→Word-track-changes flow — and still needs
  the accepted-annotation UI to carry `(paraId,runIndex,offset)` anchors. Highest risk; likely a
  multi-task effort of its own.

## What was NOT done (reversibility preserved)

No file was edited or deleted. `ComposeShadowPatchEngine.cs`, `DocxAnnotationWriter.cs`,
`ComposeParagraphRedlineSynthesizer.cs`, `ComposeEndpoints.cs`, `ComposeService.cs`,
`ComposeModule.cs`, and all client files are **untouched**. Only this `BLOCKED.md` and the task-032
POML `<status>`/`<notes>` were written.
