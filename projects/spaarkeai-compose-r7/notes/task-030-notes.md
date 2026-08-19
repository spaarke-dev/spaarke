# Task 030 — Name / file-name modal on first save + Save As (FR-02) — IMPLEMENTED

> Phase 3 (Name Modal / UC-3) · sonnet@high · FULL rigor · 2026-08-16 · **client-only** (no BFF change)

## What shipped

- **`ComposeSaveNameDialog.tsx`** (NEW, sibling to `ComposeApplyTemplateDialog`): a `FormModal` preset (ADR-050) that
  captures a single **Document name** and shows a live **"Saved as: `<name>.docx`"** preview. Mode-aware title/label
  (`first-save` → "Name this document" / "Save"; `save-as` → "Save a copy as" / "Save copy"). Fluent v9 semantic tokens
  (ADR-021), dark-mode verified. Exports two pure helpers `sanitizeComposeName` / `deriveComposeFileName` (the latter
  MIRRORS the server `ComposeService.ResolveFileName` — appends `.docx` once).
- **`ComposeWorkspace.tsx`**: 
  - `UNTITLED_DOC_NAME` const + `isUntitledDraftName()` + `autoNameForUnnamedDraft()` module helpers.
  - `requestSave(mode)` — the EXPLICIT-save entry point (toolbar Save/Save As + Ctrl+S). Opens the name modal when a
    name is needed (`saveNeedsName`: Save As always; a first create-on-save of a never-persisted **untitled** draft),
    else saves directly. `saveNameModal` useState drives `<ComposeSaveNameDialog>`; its `onSubmit` re-enters
    `triggerSave(mode, { displayNameOverride })`.
  - `triggerSave` gained an optional `opts.displayNameOverride`. displayName precedence on create-on-save:
    `forkDisplayName ?? nameOverride ?? (pdfSourced ? .pdf→.docx : untitled ? autoName : fileName)` — so **no path lands
    the literal 'Untitled document.docx'** (incl. the modal-bypassing background flush, via the auto-name fallback).
  - Save As (`forkNew`): honors a distinct user name directly; falls back to `uniquifyForkFileName` only when the entered
    name equals the source (preserves FR-07a coalesce guard without mangling a deliberate name).
  - Toolbar `onSave` + Ctrl+S now route through `requestSave`. Cross-pane bridge + `beforeunload` flush stay DIRECT
    (can't show UI during unload) — protected by the auto-name fallback.

## KEY design decision — no BFF change (directional adaptation)

The POML listed `ComposeEndpoints.cs` + `ComposeService.cs` as modify targets ("thread the entered name to the BFF
create-on-save"). On inspection the **plumbing already exists**: `SaveComposeDocumentBody.DisplayName` (task 100) is
already sent by the client (`triggerSave` @~1582) and the server already maps it to BOTH the SPE file name
(`ResolveFileName`) and the record name (`sprk_documentname`, ComposeService @2565/2568). So the task reduces to
**client-side name capture + overriding that displayName value**. No server code changed.

- **One name, not two fields** (§11): the create-on-save contract carries a single `displayName`; the server derives the
  file name from it. No acceptance criterion requires a file name distinct from the document title, so per CLAUDE.md §11
  (default to reuse; new surface needs a concrete failure) I did NOT add a separate editable file-name field or a new BFF
  `FileName` field. The modal shows the derived file name as a read-only preview for transparency.
- Consequence: **NFR-01 publish size unchanged** (44.96 MB incl PDBs baseline — no server bytes changed; `git status`
  confirms client-only). **NFR-02 no new CVE** (no package change).

## Ripple (task-013-style) — handled

My change makes an EXPLICIT Save open a modal for (a) Save As and (b) first save of an **untitled** draft. Every existing
CI-only `ComposeWorkspace.*.test.tsx` that saves a **named** transient (`draft.docx`/`uploaded.docx`/`contract.docx`) or a
persisted doc is UNAFFECTED (`saveNeedsName` returns false → direct save). Only **one** test opened the modal:
`ComposeWorkspace.renderOnSave.test.tsx`'s PDF Save-As fork (`onSave('new')`). Fixed by adding a behavioral `FormModal`
stub to its `@spaarke/ui-components` mock + driving the modal submit. The non-fork `displayName === 'Corteva NDA.docx'`
assertions (lines 710/784) still hold (nameOverride undefined → falls through to the pdf→docx swap).

## Verification

- **`ComposeSaveNameDialog.test.tsx`** (NEW): **13/13 green locally** — render/close gate, mode-specific title+label, submit
  gating, trimmed/sanitized submit (spaces kept, `< > : " / \ | ? *` stripped), sanitize-to-empty blocks submit, defaultName
  seed (Save As), derived preview (no double `.docx`), Enter-to-submit, dark render, + helper unit tests.
  - **One real bug caught by the test**: the illegal-char regex used `\|` (escaped pipe) instead of `\\|` — literal
    backslash wasn't stripped. Fixed.
- **Full Compose jest suite: 605 pass, 0 fail** (the 42 "failed to run" suites are the KNOWN CI-only `@spaarke/auth` /
  `@spaarke/ui-components` baseline — they run in CI with siblings installed; unchanged by this task).
- **tsc**: no NEW errors in `ComposeSaveNameDialog.tsx` / `ComposeWorkspace.tsx` (only the known monorepo `@spaarke/*`
  resolution baseline + 8 pre-existing `err is unknown`/implicit-any at lines outside my diff hunks — confirmed via
  `git diff --unified=0` hunk ranges vs the error line numbers).
- **BFF**: no server file changed → BFF build/tests/publish/CVE unchanged (post-merge full Compose xUnit was 1124 green).

## Gates (Step 9.5)

- **code-review**: PASS — 0 Critical / 0 Warnings. Security: entered name sanitized before the SPE PUT-by-path (no path
  traversal via filename). No AI code smells. Hooks/deps correct.
- **adr-check**: PASS — ADR-021 ✅, ADR-050 ✅ (FormModal preset, no hand-rolled Dialog), ADR-028 ✅ (no auth work),
  ADR-012 ✅. BFF hygiene not triggered (no `src/server/**`). 0 violations.

## Master merge (pre-030)

Merged `origin/master` (2 commits: RED-4 dead-code deletion + a checkpoint). One conflict in `DataverseWebApiService.cs`
(RED-4 deleted the generic-entity stub block; task 013 had added an `UpsertAsync` stub there). Resolved by taking master's
side (deleting the whole block) — `DataverseWebApiService` implements `IEventDataverseService`/`IFieldMappingDataverseService`,
NOT `IGenericEntityService`, so the stub was genuinely dead; the real `UpsertAsync` lives in `DataverseServiceClientImpl`
(+ on the interface). Post-merge: 1124 Compose xUnit tests green.
