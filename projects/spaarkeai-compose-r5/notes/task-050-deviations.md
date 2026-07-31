# Task 050 — UAT #1A redline routing + origin fix — Deviation & Completion Note

> **Completed**: 2026-07-30 · Rigor FULL · opus/xhigh · SEV-1.

## Root cause (confirmed in code)
The redline-not-in-Word regression was a **client save-routing bug**, not a server or engine defect.

- `ComposeWorkspace.tsx` transient create-on-save discriminant was `bornInEditorRender = editorIsDirty || !state.docxBytes`. The `editorIsDirty ||` meant a **dirty imported** doc (Browse/upload-mounted, `state.docxBytes` present) rendered from the ContentModel → `ComposeDocumentRenderer` authored **plain untracked runs** → no `w:ins`/`w:del` in Word. It also caused the server to stamp `sprk_composeorigin=Authored` (origin was derived from ContentModel presence), which then forced every later reopen-save onto the clean branch (`cleanApply`) — permanently clean.
- The op-log was never captured on the transient path (`opLogSnapshot = !isTransientCreate && editorIsDirty`), so there was no tracked path for a transient imported edit.
- The **server already supported** op-log on create-on-save (`ComposeEndpoints.cs:1408/1420` + comment `:1417-1419`: "a browse-local create-on-save MAY carry an op-log the engine applies onto the retained bytes"). The client simply wasn't sending that shape.

## Fix (minimal, aligns client with existing server capability)
1. **`ComposeWorkspace.tsx` op-log capture** (~:1099): capture the op-log for a dirty imported transient too — `editorIsDirty && (!isTransientCreate || !!state.docxBytes)`. Only a true born-in-editor doc (`!state.docxBytes`) skips it. Replace-path behavior unchanged.
2. **`ComposeWorkspace.tsx` transient discriminant** (~:1157): `bornInEditorRender = !state.docxBytes` (removed the `editorIsDirty ||`) — matches the replace path's `bornInEditor`. An imported transient now sends `content` (retained original) + `operationLog` (no ContentModel) → server applies via `ComposeShadowPatchEngine` with `trackChanges:true` (create-on-save has no DocumentRecordId → `cleanApply` false) → native `w:ins`/`w:del`.
3. **`ComposeService.cs` origin hardening** (~:707): `origin = ContentModel is not null && Content.IsEmpty ? Authored : Imported` — defense-in-depth so a save carrying retained original bytes is IMPORTED even if a ContentModel is also (erroneously) present. Still resolved only from request shape (NFR-02/I-7), never inference.

## Two-byte-author split (ADR-049) — NOT merged
The fix routes imported edits to the **existing** tracked engine path; it does not force authored origination through the op log, and does not touch `ComposeDocumentRenderer`. Born-in-editor docs still render from the ContentModel. Split intact.

## Tests
- NEW seam slice `ComposeOriginRoutingSeamTests.Save_ImportedTransient_CreateOnSave_OperationLogPath_StaysTracked_PersistsImportedMarker_ThroughTheWire` — through-the-wire create-on-save with content + op-log asserts (a) persisted bytes contain `w:ins` with the inserted text, (b) `origin == "imported"`, (c) `sprk_composeorigin=Imported (100000001)` stamped on the new row. This is the exact gap the bug lived in (existing tests covered only the replace path).
- Full Compose suite: **822/822** (baseline 821 + this slice), 0 failed. Byte-diff harness green (I-4 no-regression).

## NFR gates
- Byte-diff: no regression (green in the filtered run).
- Publish: **46.84 MB** compressed incl PDBs (≤60 ceiling; ~one-C#-line delta). Zero new runtime package.
- `/conflict-check`: synced with master (behind:0); no conflicting Compose PR (#690/#266 are LFS/dep bumps).
- Client TS: no NEW errors in `ComposeWorkspace.tsx` — only the pre-existing `@spaarke/*` worktree module-resolution errors + pre-existing `unknown`/`any` cascades (identical on master, shifted ~21 lines by added comments).

## Step 9.5
Focused code-review + adr-check applied (ADR-049 two-author split + I-4/I-7; ADR-013/007 no AI/Graph; ADR-038 seam DoD; NFR-02). No violations. Change is 3 small edits + 1 test; formal gate reasoning captured here.

## Known limitation (documented, not fixed here)
Docs **already mis-stamped Authored** during the buggy-deploy window will still clean-apply on reopen (the durable marker is authoritative and reopened-authored vs reopened-imported are indistinguishable from the request by design — g2-clean-apply-decision.md). The fix prevents future mis-stamps; a fresh upload gets the correct tracked behavior end-to-end. Re-UAT with a freshly-uploaded doc. Existing mis-stamped dev test docs should be re-created (or one-off re-stamped) — acceptable in the dev UAT env.
