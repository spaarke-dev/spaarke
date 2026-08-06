# Task 053 — Import round-trip (FR-10) — decisions + deviation notes

## Finding: the mount already existed, inherited from the R3 base

Before writing any code, Step 0 (context load) surfaced that the R3 base this branch extends
(`spaarkeai-compose-r3`, project `CLAUDE.md`: "the deployed base R4 extends") already shipped the FULL
FR-10 mount, not just the reader:

- `src/client/shared/Spaarke.Compose.Components/src/widgets/importedRevisions.ts` (task 050, R3) —
  projects `ImportedRevision[]` onto `InsertionMark`/`DeletionMark`, paraId-primary anchored with an
  anchorText/paragraphHint fuzzy fallback.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/importedComments.ts` (task 051, R3) —
  groups `ImportedComment[]` into FR-23 comment threads + anchors them with `CommentAnchorMark`.
- `ComposeEditor.tsx` already wires both into BOTH mount paths — the R4 Phase-1 server-projection mount
  (`projection.html`, data-paraid) AND the legacy mammoth-fallback mount (transient/browse) — calling
  `applyImportedRevisions`/`applyImportedCommentAnchors` after `setContent`/`stampParaIds` and before
  `captureParaIdSnapshot`/`opLogRef.reset()`, so an imported mark folds into the load-time baseline and
  is never mistaken for a user edit on save.
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs` (`LoadAsync`, lines ~328-380) already
  runs the EXISTING `DocxAnnotationReader` once per Load and projects `ImportedRevisions`/
  `ImportedComments` onto the Load response, each resolved to its `w14:paraId` via the paraId map the
  same single-pass projection builder produces. **No `ComposeDocxProjectionBuilder.cs` change was
  needed** — the POML's suggested extension point assumed the server work hadn't landed yet; it had.
- Accept/reject: `usePendingRedline.accept`/`.reject` scan the LIVE DOCUMENT for marks by `ledgerRef`
  (`collectMatchingRanges`) — NOT gated on hook-internal `pending` state — so the SAME per-change popover
  + accept/reject flow any AI redline uses already works, unmodified, for an imported revision's
  `imported:{id}` ledgerRef. Verified in `importRoundTrip.test.tsx`.

**Deviation from the POML's step 1 ("extend the projection … `ComposeDocxProjectionBuilder`")**: not
needed — the server projection is already complete via `ComposeService.LoadAsync`, independent of
`ComposeDocxProjectionBuilder`. No server (`.cs`) file was touched by this task.

## The actual gap closed: I-7 compliance for unresolvable REVISIONS

Comments already satisfied invariant I-7 ("surfaces for review, never silently dropped") —
`groupImportedComments` is PURE and always seeds the FR-23 panel regardless of whether the in-document
anchor resolves (proven by the pre-existing `importedComments.test.ts` "un-anchorable thread ... the
thread is not dropped by the pure projection" case).

**Revisions did not.** `applyImportedRevisions` computed an `unresolved` COUNT, but `ComposeEditor.tsx`
discarded the function's return value entirely at both call sites — an unresolvable revision rendered
nothing, was reflected nowhere in the UI, and the count itself was thrown away. From the user's
perspective this reads as a silent drop despite the underlying `ImportedRevision` surviving server-side.

### Fix

1. `applyImportedRevisions` now also returns `unresolvedItems: ImportedRevision[]` (not just a count).
2. New `renderUnresolvedRevisionPlaceholders(editor, unresolvedItems)` (same file) — REUSES the task-021
   opaque-atom node (`composeInlineAtom`, zero new schema surface) as a review marker: one atom per
   unresolved revision, wrapped in a fresh paragraph, **appended at the END of the document** (never
   inline at a guessed position — I-7 bans text-search placement outright). Runs as a SEPARATE pass
   AFTER `applyImportedRevisions`'s main loop finishes, so an earlier placeholder can never pollute a
   later revision's fuzzy-anchor search.
3. `ComposeEditor.tsx` wires this at BOTH mount sites (server-projection path + mammoth fallback),
   before the paraId snapshot / op-log reset (same "load-time baseline, not a user edit" contract every
   other import-mark helper follows), and also folds a summary count into the existing
   `onImportWarnings` banner channel (privacy-safe — counts only, no document content in the banner
   text itself; the placeholder atom is the actual review surface and DOES carry content, consistent
   with how imported revision/comment marks already render document content in the editor).
4. Dark mode (ADR-021): the placeholder inherits the `.compose-atom`/`.compose-atom-inline` Griffel
   rules `ComposeEditor.opaqueAtomTheme.test.ts` already proves are token-based (no hex/rgb literal) —
   zero new CSS, zero new dark-mode risk.

### Save survival

No new save-path code was needed. Because the unified `ComposeShadowPatchEngine` only touches paraIds
the user actually edited (invariant I-4, "untouched XML subtrees byte-identical after save") and imports
that were resolved via the code already shipped are patched at their real anchoring paraId (not the
review placeholder, which carries no paraId and is never captured into an operation — its mount
transaction runs with `addToHistory: false` and is wiped by the SAME `opLogRef.reset()` call every other
import-mark mount already relies on), a save of an untouched document is a true no-op: zero operations
sent, the server writes back the retained original bytes unchanged. The client-observable half of
"survives a save+reload round trip" is proven in `importRoundTrip.test.tsx` (re-mounting a fresh editor
from the identical `ImportedRevision`/`ImportedComment` set reproduces byte-identical HTML/marks). The
through-the-wire (real bytes → BFF → client) half of this same proof is task 054's seam slice, per this
task's own `<notes>`.

## Escalation trigger — NOT fired

The POML's escalation trigger ("an imported revision/comment whose paraId is unresolvable … that cannot
be surfaced without dropping it") did not fire: the review-placeholder mechanism surfaces every
unresolved revision without ever needing to drop one. No schema change was required.

## Files changed

- `src/client/shared/Spaarke.Compose.Components/src/widgets/importedRevisions.ts` — `unresolvedItems` on
  the result; new `renderUnresolvedRevisionPlaceholders`.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/importedRevisions.test.ts` — updated
  `toEqual` shape + new placeholder-rendering coverage.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` — wires the placeholder +
  banner summary at both mount sites (server-projection path + mammoth fallback).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/importRoundTrip.test.tsx` (new) — the FR-10
  acceptance suite: render + accept/reject-able + negative (unresolvable revision surfaces, unresolvable
  comment already-compliant parity) + save/reload round-trip determinism.

No `.cs` files changed — server-side FR-10 support (`DocxAnnotationReader`, `ComposeService.LoadAsync`)
was already complete. BFF publish size: **unchanged** (no server code touched by this task).

## Verification

- `npx jest importedRevisions importedComments importRoundTrip opaqueAtomTheme` — 4 suites, 30 tests, all
  green.
- `npm run typecheck` — A/B verified via `git stash` against the pre-existing baseline: byte-identical
  error set (8 pre-existing `@spaarke/ai-widgets`/`@spaarke/document-operations` sibling-dist errors,
  environmental per root `CLAUDE.md`); this task introduces zero new typecheck errors.
- Full-mount `ComposeEditor`/`ComposeWorkspace` React-render tests (e.g. `ComposeWorkspace.imports.test.tsx`)
  could not run in this worktree — pre-existing environmental gap (`@spaarke/ui-components` has no
  installed `node_modules`/built `dist/` here), confirmed via the SAME `git stash` A/B (fails identically
  with and without this task's changes) and already documented by the prior `ComposeEditor.opaqueAtomTheme.test.ts`
  (task 024). `importRoundTrip.test.tsx` exercises the same production code paths via `usePendingRedline`
  (imported as `import type` only from `ComposeEditor.tsx` — erased at compile time, so it loads cleanly)
  over a headless editor built from ComposeEditor's own extension set, per that file's header rationale.
- ADR-021 dark mode: no new CSS — the reused `.compose-atom`/`.compose-atom-inline` rules are already
  proven token-based by `ComposeEditor.opaqueAtomTheme.test.ts`.
