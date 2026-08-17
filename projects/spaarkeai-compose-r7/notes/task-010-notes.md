# Task 010 — Stable non-rotating logical document id (FR-07b)

> Phase 1 (Save-Identity Fix / UC-8) · opus · FULL rigor · 2026-08-15

## Implementation choice (POML step 2: persist transientKey vs new field)

**Chosen: a dedicated `composeLogicalId` field on `ComposeDocumentRef`** (still an *extension* of the existing ref, §11-compliant — not a parallel identity object).

Why not overload `transientKey` (the POML's nominal first preference): `transientKey` has a **distinct server contract and lifetime** — it is sent on every create-on-save for `sprk_composetransientkey_uk` server dedup, and is deliberately `undefined` for loaded/promoted docs. Making it *also* the persisted, cross-remount logical identity conflates two concerns (per-mount server-dedup key vs client identity) and would force `transientKey` to be defined for cases its contract says it must not be. A dedicated `composeLogicalId` keeps each contract honest while remaining a field on the same ref. The identity accessor consumes it last (`sprkDocumentId ?? speDriveItemId ?? composeLogicalId`), so `transientKey`'s "undefined for loaded docs" rule is untouched.

## What shipped

- **`compose-contracts.ts`**: `ComposeDocumentRef.composeLogicalId?: string` + `getComposeLogicalIdentity(ref)` — the single identity accessor implementing `sprkDocumentId ?? speDriveItemId ?? composeLogicalId`, **empty-guarded** (transient mounts set `speDriveItemId: ''`, so a bare `??` would wrongly return `''`).
- **`composeIdentity.ts`** (new util): `mintComposeLogicalId`, `startNewComposeLogicalId` (mint + persist), `recoverActiveComposeLogicalId`, `persistActiveComposeLogicalId`, `clearActiveComposeLogicalId`, `COMPOSE_ACTIVE_DRAFT_ID_KEY`. Backed by a **single active-draft slot in localStorage** (best-effort; SSR/private-browsing/quota safe — never throws).
- **`ComposeWorkspace.types.ts`**: `composeLogicalId` on the `mountTransient` / `mountDraftHtml` / `loadSucceeded` actions; stamped onto `documentRef` in those reducer cases; preserved through `saveSucceeded` via the existing `...state.documentRef` spread.
- **`ComposeWorkspace.tsx`**: all **6 transient/draft mint doors** (browse @~2975, born-in-editor @~3024, assistant-upload @~3303, Part-B inline draft @~3367, Part-A ledger draft @~3440, PDF-sourced load @~1122) now also mint+persist `composeLogicalId` via `startNewComposeLogicalId()`. First-Save promotion **clears the slot** (guarded on the mounted doc's own logical id) so a persisted doc is never resurrected as a blank draft.
- **`index.ts`**: barrel exports the accessor + lifecycle helpers for FR-03 (040) and FR-07 dedup (011).

## Storage model (bounded decision — for 040/011 to build on)

- **localStorage, single "active draft" slot** (`spaarke.compose.activeDraftId`). localStorage (not sessionStorage) so recovery survives tab **close+reopen**, matching the owner's "never lose work" priority. Consistent with the codebase's existing single-slot Compose conduits ("last-mounted instance wins", ComposeWorkspace.tsx:~3050).
- **New-vs-recover distinction**: user-initiated new-doc doors call `startNewComposeLogicalId()` (fresh id, replaces slot); the *recovery* path (task 040 drives the content re-mount) calls `recoverActiveComposeLogicalId()`. Task 010 lays the id + persistence + accessor + persist-back; **040 builds the reload-recovery UX and content draft** keyed by this id; **011 dedups on `getComposeLogicalIdentity`**.
- **Known limitation (accepted, documented)**: concurrent multi-Compose-tab shares one slot (last active draft wins) — consistent with the existing single-slot architecture; 040 may refine the scope key if needed. Device-switch loss is an accepted client-only limitation (spec Owner Clarifications).

## Verification

- `composeIdentity.test.ts` — 14 tests: mint distinctness, persist/recover round-trip, **id stable across simulated re-mount (no fresh mint)**, new-doc replaces slot, clear-on-promotion, storage-unavailable safety, accessor derivation (incl. `''`-guard), reducer persist-through (mountTransient/mountDraftHtml stamp + saveSucceeded promote-to-sprkDocumentId). **14/14 green.**
- Pure-reducer regression suites (`renderOnSave.reducer`, `saveBaseline`) + identity = **40/40 green** — additive field broke nothing.
- Typecheck: no new errors reference the added code; remaining tsc/jest failures are the pre-existing `@spaarke/*` workspace-link gap when building this package standalone (unrelated to this task).
- Client-only: **no BFF/server file touched**; ADR-049 append-only versioning unchanged (NFR-03 satisfied structurally). `docxBridge.ts` untouched.
