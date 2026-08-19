# Task 041 — Save-state indicator + beforeunload guard + invariant/test flip (FR-03) — IMPLEMENTED

> Phase 4 (Draft-Safe Autosave / UC-4) · sonnet@high · FULL rigor · 2026-08-16 · client-only (no BFF)
> **ADR-Tensions path A**: this task carries the DELIBERATE reversal of the "no autosave" invariant.

## What shipped

- **Save-state indicator** (absorbs R6 D6) in `ComposeFormatToolbar.tsx`: a subtle single-line `Text`
  after the Save split-button showing **Saving…** (with a Spinner) while a save is in flight, **Unsaved**
  when there are dirty edits, **Saved** otherwise — plus **· Auto Save On/Off**. `data-save-state`
  attribute (`saving`/`unsaved`/`saved`) for testability; `aria-live="polite"` for announcement. Fluent v9
  semantic tokens only (`colorNeutralForeground3`, `fontSizeBase200`, spacing) — ADR-021 dark-mode-correct.
  Rendered only when the host tracks save state (`onSave` wired + `hasUnsavedEdits` provided).
- **Prop threading** (no new state; consumes task-040 state): new `hasUnsavedEdits?: boolean` on
  `ComposeFormatToolbarProps` + `ComposeEditorProps`, forwarded ComposeWorkspace →
  `<ComposeEditor hasUnsavedEdits={isDirty || hasTransientDraft}>` → `<ComposeFormatToolbar>`. Same
  "unsaved work" signal the Save button gates on (`isDirty || hasTransientDraft`).
- **`beforeunload` guard** in `ComposeWorkspace.tsx`: a window `beforeunload` listener that
  `preventDefault()` + sets `returnValue` **only when `hasUnsavedWorkRef.current`** is true (the same live
  mirror the flush-on-unmount uses). Covers the one path a React unmount can't — a real browser
  close/nav/reload. A clean/saved doc never warns. The in-app tab-close / History-switch path stays
  covered by the pre-existing flush-on-unmount + the task-040 local draft.

## ADR-Tensions path A — the deliberate "no autosave" invariant reversal (documented)

Per spec ADR Tensions (path A) the prior invariant ("there is NO autosave/debounce anywhere in this
workspace") is deliberately reversed — but **only for CLIENT-ONLY local drafts**. Updated in place:

- `ComposeWorkspace.tsx:34` (file docblock) — now records the FR-03 client-only autosave + beforeunload +
  indicator, and affirms: **NO automatic SERVER save / SPE version is ever created**; a BFF write still
  happens ONLY on explicit Ctrl+S / toolbar-Save / bridge-chip (plus best-effort flush-on-unmount); the
  autosave path never calls `triggerSave` (NFR-03).
- `ComposeWorkspace.tsx:~2966` (flush-on-unmount rationale) — same reconciliation: the SERVER-save path
  stays narrow; the client draft autosave never POSTs, so flush-on-unmount remains the safety net.

## unmountFlush test — comment reconciled, assertions UNCHANGED (directional adaptation)

The POML framed this as "update the unmountFlush test which asserts 'no POST without explicit save'." On
inspection the test's **assertions are already correct and remain valid**: the DI-02 flush-on-unmount
deliberately DOES POST one `/save` on a dirty unmount (a best-effort save), and a clean unmount POSTs zero.
Task 040's CLIENT draft store does not change that — it never calls `triggerSave`/POSTs. So the only stale
thing was the test's **docblock invariant comment** ("no autosave/debounce anywhere"), which I reconciled
to note the new client-only draft autosave while affirming the flush-on-unmount is still the only
POST-without-explicit-Save path. Rewriting the assertions to "assert a local draft is written" would
duplicate `ComposeWorkspace.draftAutosave.test.tsx`'s coverage and dilute this test's focused DI-02 intent
— so the assertions stand. (Directional-mode adaptation; recorded here + in the PR per §6.5.)

## Verification

- **`ComposeFormatToolbar.test.tsx`** (STANDALONE): **+6 indicator tests, 81/81 green** — not-rendered
  when the host doesn't track save state; Unsaved / Saved / Saving… states (incl. Saving overriding
  dirty); Auto Save On/Off reflected; dark-mode render (ADR-021).
- **`ComposeWorkspace.draftAutosave.test.tsx`** (CI-only): +1 test — a dirty (unsaved) mount cancels a
  `beforeunload` event (`defaultPrevented === true`). The "no warn when clean" case is guaranteed by the
  guard's `if (!hasUnsavedWorkRef.current) return` early-out + the unmountFlush clean-case proof that
  `hasUnsavedWorkRef` is false for a saved doc.
- **`ComposeWorkspace.unmountFlush.test.tsx`** (CI-only): docblock reconciled; assertions unchanged (still
  the DI-02 contract).
- **Full standalone Compose jest: 621 pass / 0 fail** (was 615 after task 040; +6 = the indicator tests).
  CI-only "failed to load" group unchanged at 43.
- **tsc**: no NEW errors in `ComposeFormatToolbar.tsx` / `ComposeEditor.tsx` / `ComposeWorkspace.tsx` / the
  tests — only the known monorepo baseline (`@spaarke/*` resolution + pre-existing err-unknown/implicit-any).
- **BFF**: zero `src/server/**` changes → publish size (44.96 MB incl PDBs net10) + CVE unchanged.

## Gates (Step 9.5)

- **code-review**: PASS — 0 Critical / 0 Warnings. ADR-021 tokens (dark-mode tested); beforeunload reads
  the live ref (warns only on unsaved, listener add/remove once); no AI smells; indicator consumes 040
  state (no new state / no second autosave loop).
- **adr-check**: PASS — 0 violations. ADR-021 ✓, ADR-049 ✓ (no server-save change; NFR-03 intact —
  indicator/guard client-only), ADR-028 ✓. §10 not triggered (zero server bytes). §11 ✓ (indicator
  extends the existing toolbar). ADR-Tensions path A reversal documented in-code + notes.

## Phase 4 (UC-4 Draft-Safe Autosave) COMPLETE — 040 (store/autosave/recovery) + 041 (indicator/guard/invariant).
