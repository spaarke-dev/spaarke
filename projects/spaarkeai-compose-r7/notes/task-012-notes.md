# Task 012 — Save As is a real fork (FR-07a) — IMPLEMENTED

> Phase 1 (Save-Identity Fix / UC-8) · sonnet@xhigh · FULL rigor · 2026-08-16

## Directional adaptation vs `task-012-analysis.md`

The analysis proposed a **server-side** fix (route the `forkNew` create through Graph `conflictBehavior=rename`). On implementation I chose the **client-side** path instead, because:
- The POML **constraint** names the client site as the primary target: *"The Save As ('new') path in `ComposeWorkspace.tsx` triggerSave/forkNew MUST uniquify the filename"* — the server `ResolveFileName` touch was explicitly *conditional* ("if … touched server-side").
- Client-side uniquification **fully closes the bug with no BFF change**: the client sends a collision-free `displayName`, so the server's existing `UploadSmallAsUserAsync` PUT-by-path writes to a NON-existing path → Graph mints a **distinct** drive-item (a real fork), never a re-version. This avoids the SPE-facade/Graph-SDK conflict-behavior plumbing (Kiota does not cleanly expose `@microsoft.graph.conflictBehavior` on a simple content PUT; the only proven infra is the chunked upload-session path — too heavy for a small fork) and keeps FR-07a off the BFF entirely (no publish-size/CVE/`/conflict-check` gate).
- The escalation trigger ("uniquify needs a round-trip that risks a duplicate window") does **not** fire — the token is derived by construction, no drive listing.

`ComposeService.cs` was therefore **not** modified (the POML listed it as an output; directional mode + the constraint's primary client-site directive make client-only the correct, cleaner realization). No server round-trip, no duplicate window, no BFF surface.

## What shipped (client-only)

- **`composeIdentity.ts`**: `uniquifyForkFileName(fileName, forkKey)` — inserts a 6-char alnum token (from the fork's fresh transient key, a per-fork UUID) before the extension: `"Contract.docx" → "Contract (copy 3f2a1b).docx"`. Collision-safe by construction (every fork mints a fresh key → a distinct name), no round-trip. Empty/extension-less names handled.
- **`ComposeWorkspace.tsx` `triggerSave`**: for `forkNew`, compute `forkDisplayName = uniquifyForkFileName(originalName, effectiveTransientKey)` and `forkLogicalId = startNewComposeLogicalId()`. Send `forkDisplayName` as the create-on-save `displayName`; adopt `forkDisplayName` + `forkLogicalId` onto the forked `documentRef` via `saveSucceeded`.
- **`ComposeWorkspace.types.ts`**: `saveSucceeded` action gains optional `fileName?` + `composeLogicalId?`; the reducer adopts them when present (fork), else preserves existing. So the forked ref shows the NEW name + a NEW logical id — not the original's.
- **`index.ts`**: barrel-exports `uniquifyForkFileName`.

## Acceptance mapping

- *Distinct new `sprk_document` + distinct file, never a silent re-version* → unique `displayName` → distinct SPE PUT-by-path item; `forkNew` skips server transient-key dedup → distinct record; the original's drive-item is never PUT to. ✓
- *Fork carries a NEW task-010 logical id* → `forkLogicalId` (fresh `startNewComposeLogicalId`) adopted onto the forked ref; the accessor also promotes to the new `sprkDocumentId`. ✓
- *`ComposeSaveMode` unchanged* → still `'version' | 'new'`; only fork behavior changed. ✓
- *BFF gates* → N/A, no BFF touched.

## Verification

- `composeIdentity.test.ts`: +6 tests (uniquify: token-before-ext, distinct-per-key collision-safety, no-ext, empty fallback, UUID-punctuation strip; reducer: fork adopts new name + new logical id, accessor promotes to the new sprkDocumentId). **21/21 green.**
- Reducer regression (`renderOnSave.reducer`, `saveBaseline`): **26/26 green** — the additive `saveSucceeded` fields broke nothing.
- Typecheck: no new errors in touched code.
- `docxBridge.ts` untouched; client-only (ADR-049 append-only + ADR-007 facade unchanged).

## Feeds
Task 020 (Save/Save As dropdown) surfaces this fork behavior. The `'new'` path is now a genuine fork.
