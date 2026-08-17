# Task 040 — Client-only local draft store + dirty autosave + crash recovery (FR-03) — IMPLEMENTED

> Phase 4 (Draft-Safe Autosave / UC-4) · sonnet@high · FULL rigor · 2026-08-16 · **client-only** (no BFF)

## What shipped

- **`composeDraftStore.ts`** (NEW, sibling to `composeIdentity.ts`): the CLIENT-ONLY local draft store.
  Best-effort localStorage (try/catch, never throws), **single active-draft-content slot**
  (`COMPOSE_DRAFT_CONTENT_KEY`) consistent with `composeIdentity`'s single `COMPOSE_ACTIVE_DRAFT_ID_KEY`
  slot — so it never grows unbounded across many never-saved born-in-editor docs. API:
  - `saveComposeDraft(logicalId, html, fileName?)` — writes `{ logicalId, html, fileName, savedAt }`.
  - `getComposeDraft(logicalId)` — returns the entry **only when its `logicalId` matches** (a stale slot
    from a different doc is never mis-recovered); null on none/mismatch/corrupt.
  - `clearComposeDraft(logicalId?)` — id-scoped clear (leaves an unrelated doc's draft intact) or
    unconditional clear when no id.
- **`ComposeEditor.tsx`** — added **`getDraftHtml(): string | null`** to `ComposeEditorHandle` + its
  `useImperativeHandle` impl (`editor ? editor.getHTML() : null`). Read-only — **no dirty-flag side
  effect, no byte authoring, no network**. Distinct from `buildContentModel()` (the high-fidelity
  save-path model that DOES reset dirty); the draft store deliberately captures the cheap HTML view.
- **`ComposeWorkspace.tsx`**:
  - **Autosave effect** — a dirty-only `~15s` (`COMPOSE_DRAFT_AUTOSAVE_INTERVAL_MS`) `setInterval` while
    `state.status === 'loaded'`. On tick, gated on the `autoSaveEnabled` toggle (task 020) + a stable
    logical id + `editorRef.current.isDirty()` (the editor's OWN authoritative flag), it writes the draft
    via `saveComposeDraft` — **localStorage only, never `authenticatedFetch`** (NFR-03). Live inputs
    (toggle / logical id / file name) ride a `draftAutosaveMirrorRef` (the same ref-mirror convention as
    `hasUnsavedWorkRef`), so the interval re-arms only on a loaded/unloaded status flip.
  - **Clear on save** — in the `saveSucceeded` path, alongside the existing `clearActiveComposeLogicalId()`,
    also `clearComposeDraft(getComposeLogicalIdentity(state.documentRef))` (the PRE-save logical id) so a
    promoted doc is never resurrected as an unsaved draft.
  - **Recovery on mount** — a fire-once effect: when the workspace opens with NO real mount door
    (`!initialDocumentRef && !initialUploadRef && !initialDraftRef`) and a prior session left a persisted
    active draft (`recoverActiveComposeLogicalId()` + `getComposeDraft`), it re-seeds the draft via the
    EXISTING `mountDraftHtml` born-in-editor path, **reusing the recovered logical id** (never minting a
    fresh one). Scope kept minimal + non-destructive: the prop-guard means recovery can never clobber a
    loaded server doc.

## KEY design decision — Option B (reuse the existing recovery path)

`ComposeEditorHandle` had `buildContentModel()` (structured model) but **no plain HTML getter**, and no
client mount path re-seeds from a content model. Rather than invent a content-model re-seed path, I added
`getDraftHtml()` (`editor.getHTML()`) and recover via the SAME `mountDraftHtml` reducer path the
blank/template/AI-draft/ledger mounts already use. Draft key = the task-010 `getComposeLogicalIdentity`
accessor (`sprkDocumentId ?? speDriveItemId ?? composeLogicalId`) — reused, not re-derived (constraint).

## Escalation trigger (NFR-03) — NOT fired

The trigger fires only if local-draft persistence can't be separated from a server save (version-per-tick
risk). It stayed clean: the draft path imports nothing network-related and calls only `composeDraftStore`
(localStorage). The SPE version is appended EXCLUSIVELY by explicit Save (`triggerSave`). Verified by the
workspace test asserting **zero** persistence-endpoint calls after a 15s autosave tick.

## Ripple (task-013 pattern) — SMALLER than predicted

The design warned that adding a `ComposeEditorHandle` method could break every editor-handle mock (~15
`ComposeWorkspace.*.test.tsx` stubs). In practice **none needed changing**: those stubs use
`useImperativeHandle(ref, () => ({...}))` with an **inferred** (not `ComposeEditorHandle`-annotated) return
type, so a missing `getDraftHtml` is not a tsc error; and the consumer reads `handle.getDraftHtml?.()` with
optional-chaining, so a stub omitting it is a runtime no-op (autosave simply skips that tick). Zero existing
stubs touched.

## Deferred to task 041 (deliberate boundary — do NOT treat as a miss)

- The **"no autosave" invariant comments** (`ComposeWorkspace.tsx:34` + `:2966`) are LEFT UNTOUCHED. They
  assert *no automatic **server** flush / no debounced `triggerSave`* — which **remains factually true**
  (the client draft store never calls `triggerSave`/the BFF). Task 041 owns the wording reconciliation, as
  the documented ADR-Tensions Path-A change, coupled with the `unmountFlush` test flip — one coherent edit.
- **Save-state indicator** (Saving…/Saved/Unsaved + Auto Save On/Off) + **`beforeunload`/modal-close guard**
  are task 041.

## Verification

- **`composeDraftStore.test.ts`** (NEW, standalone): **10/10 green** — save/read round-trip, cross-id
  match-gating, single-slot overwrite, id-scoped vs unconditional clear, empty-id no-op, omitted-fileName
  stays `undefined`, corrupt-slot → null (no throw), missing-html → null.
- **`ComposeWorkspace.draftAutosave.test.tsx`** (NEW, CI-only — mocks `@spaarke/auth` like every sibling
  `ComposeWorkspace.*.test.tsx`): 3 tests — (1) dirty doc auto-drafts to localStorage on the 15s tick AND
  fires zero persistence-endpoint calls (NFR-03); (2) reopen with no mount door recovers the draft into the
  editor (populated, not empty-state); (3) a stored-document ref WINS — no recovery clobber.
- **Full standalone Compose jest: 615 pass / 0 fail** (was 605 at task 030; +10 = the new store suite). The
  CI-only "failed to load" group grew by exactly +1 = the new workspace suite (correct — it joins the
  `@spaarke/auth`-mocked group). No regressions.
- **tsc**: no NEW errors in `composeDraftStore.ts`, the `ComposeEditor.tsx` `getDraftHtml` additions, or the
  new tests. Only the known monorepo baseline (`@spaarke/*` module resolution + 8 pre-existing
  `err is unknown`/implicit-any in unrelated existing handlers) remains.
- **BFF**: zero `src/server/**` changes → publish size (44.96 MB incl PDBs net10) + CVE unchanged; NFR-03
  satisfied structurally.

## Gates (Step 9.5)

- **code-review**: PASS — 0 Critical / 0 Warnings. Security (origin-scoped localStorage; recovered HTML
  rides the existing sanitized `mountDraftHtml` seed path; no path traversal), no AI code smells, hooks
  correct (ref-mirror convention). 1 Suggestion (deferred by design): reconcile the "no autosave" comment
  wording in task 041.
- **adr-check**: PASS — 0 violations. ADR-049 ✓ (save path untouched; NFR-03 honored — draft path calls no
  fetch), ADR-028 ✓, ADR-012 ✓ (context-agnostic util), ADR-021 ✓ (no new UI). §10 BFF hygiene NOT
  triggered (zero server bytes). §11 ✓ (POML justification; concrete failure = unsaved work lost on crash).
