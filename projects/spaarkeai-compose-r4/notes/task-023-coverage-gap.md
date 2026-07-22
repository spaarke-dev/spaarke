# Task 023 coverage gap — RESOLVED via Path A (re-sequence)

> **Date**: 2026-07-22
> **Status**: ✅ **RESOLVED (Path A)** by the orchestrator. Task 023 `<deps>` now include **031**; task 031's
> scope was extended to wire the client structural-step→operation emission (`onStructuralStep` → deleteParagraph/
> mergeParagraph/…), so whole-paragraph delete/merge is captured before `collectEditedParagraphs` is removed.
> 023 re-queued (`not-started`, gate blocked-on-031). No interim regression window. Original block analysis below.
> **Blocked by (original)**: coverage-check gap at POML Step 1 (mandatory pre-deletion check).

## Gate precondition — satisfied

Task 006 (Phase 0 gate) is 🟢 GREEN per `notes/phase0-gate-decision.md` (2026-07-22). The
006-gate precondition that authorizes 023/032/060 to *begin evaluation* is met. This block is
raised at the task's own **Step 1 coverage check**, not at the 006-gate check.

## The specific uncovered case

**Whole-paragraph deletion / merge** (a load-time paragraph that no longer exists in the current
document — the user deleted the entire paragraph or merged it into a neighbor).

- **Old path (`collectEditedParagraphs`, `docxBridge.ts:360-368`)** explicitly handles this: any
  `paraId` present in the load-time snapshot but absent from the current document is emitted as
  `{ paraId, text: '' }` — a sentinel the server's `ComposeParagraphRedlineSynthesizer` interprets
  as "strike this whole paragraph" (`WordDiff(old, '') → all-delete`). The code comment names the
  exact regression this guards against: *"Without this the retained original kept the vanished
  paragraph verbatim → leftover/duplicate content (the silent Accept-first corruption the round-4
  investigation found)."*
- **New path (`stepOperationInterceptor.ts`, tasks 020/022)** does **not** capture this case at
  all today. Every structural ProseMirror step — `splitParagraph` / `mergeParagraph` /
  `insertParagraph` / `deleteParagraph` — is explicitly deferred via the `defer-structural`
  classification (`classifyStep`, the cross-paragraph `ReplaceStep` branch at line ~380: *"Cross-
  paragraph or block-boundary ReplaceStep (split/merge/para insert/delete) → task 031."*) and
  routed to the `onStructuralStep` seam. `RebasedOperationLog.deriveOperation`'s `default` case
  (line ~741) says outright: *"Structural op types (splitParagraph/mergeParagraph/insertParagraph
  /deleteParagraph) are not emitted by classifyStep yet (task 031's surface) — defensive,
  unreachable today."*
- Confirmed empirically: `onStructuralStep` has **zero wired handlers** anywhere in
  `ComposeEditor.tsx` / `ComposeWorkspace.tsx` (grep: `COMPOSE_R4_STEP_INTERCEPTOR` is registered
  bare — no `onOperations`/`onStructuralStep` callback supplied; no `RebasedOperationLog` instance
  is constructed in production code, only in tests). So today a whole-paragraph delete/merge
  produces **no operation, no flag, no signal of any kind** on the client.

This is precisely task 031's surface: **"Structural operations — split/merge/insert/delete
(FR-05)"** — `tasks/TASK-INDEX.md` shows 031 as `🔲 not-started`, `blocked`, deps `030`
(Patch Engine core), which is itself `🔲 not-started`, `blocked`, deps `003,005,006`. Neither
030 nor 031 has landed.

## Why this is a real gap, not a wiring nit

`design.md` §Q3 (Unresolved Questions) is explicit that structural edits are **in R4 core scope**,
not a stretch: *"is paragraph insert/split/merge/delete in R4 core, or is R4 core = text+format
edits with structural ops as a Phase-6 stretch? Recommendation: in core (it's the main thing the
operational model unlocks), but sequence last within the applier."* Design §5 (Patch, backend)
likewise frames structural ops as future/native capability: *"Structural ops (split/merge/insert/
delete paragraph) applied natively — the capability the paragraph-diff never had."* — but the
paragraph-diff export DID have a coarse capability for the one structural case that matters most
here (whole-paragraph strike via the empty-text sentinel), and that capability disappears the
moment `collectEditedParagraphs` is deleted, with no replacement until 031 lands.

Separately from anchor-shape coverage: task 023's own scope note ("Also remove now-dead call
sites... the save path must route through the step interceptor, not the paragraph-diff export")
means deleting the `ComposeWorkspace.tsx:1127` / `ComposeEditor.tsx` call sites now would leave
**every** dirty save a no-op (acceptance criterion #3 explicitly accepts this for inline text
edits — captured-ops-so-far is genuinely `[]` until a save-path consumer exists, task 050). That
general "temporarily no persistence until Phase 3/5 land" tradeoff is already accepted in the
POML's own acceptance criteria and is NOT what this block is about. This block is about the
**permanent, silent loss of one concrete case type** (whole-paragraph delete/merge) that the old
path *did* capture and the new path's current scope explicitly excludes until task 031 — i.e.
even after task 050 (save-path wiring) lands, a whole-paragraph delete will STILL be silently
uncaptured unless 031 lands first. `collectEditedParagraphs` is depended on for that behavior
until 031 exists; deleting it now measurably regresses FR-01's "structural delete/merge" handling
described in `docxBridge.ts:360-368`'s own doc comment, with zero interim signal (not even a
flagged/deferred marker reaches any consumer, since no seam is wired).

## What was tried / checked

1. Read `collectEditedParagraphs` (`docxBridge.ts:339-370`) end-to-end, including the structural
   delete/merge sentinel emission (`:360-368`) and its UAT-round-4 provenance comment.
2. Read `stepOperationInterceptor.ts` end-to-end (both the task-020 interceptor and the task-022
   `RebasedOperationLog` extension) — confirmed `classifyStep`'s `defer-structural` branches and
   `deriveOperation`'s explicit "not emitted yet" comment for all four structural op types.
3. Grepped the whole client (`src/`) for `collectEditedParagraphs` — found call sites at
   `ComposeWorkspace.tsx:1127`, `ComposeEditor.tsx:119,543,1759,1763`, `index.ts:300`, plus
   documentation-only references in `TrackChangesExtension.ts`/`.test.ts`,
   `hooks/trackChangesDiff.ts`, `redlineDocxAnnotations.test.ts`.
4. Grepped `ComposeEditor.tsx`/`ComposeWorkspace.tsx` for any `onStructuralStep`/
   `RebasedOperationLog` wiring — confirmed none exists in production code (bare registration
   only, per the interceptor's own doc comment: *"Registered bare (default options) by
   ComposeEditor for task 020 — wire-in only."*).
5. Cross-checked `tasks/TASK-INDEX.md`: 031 (structural ops) and 030 (Patch Engine core) are both
   `🔲 not-started` / `blocked`; 050 (save-path wiring) is also `🔲 not-started` / `blocked`
   (deps on 032, which deps on 030+031).
6. Cross-checked `design.md` §Q3 and §5 (Patch) — structural ops are in-core scope, sequenced
   last within the applier (i.e., after this task in the WBS, not before).

## Decision needed (owner / next planning pass)

One of:
- **(A) Re-sequence** — move task 023 (and its dep chain) to depend on **031** (not just 022+006),
  so the client deletion happens only once structural capture exists and there is no coverage
  gap window at all. Cleanest option; matches design §Q3's own "sequence last within the applier"
  guidance, just applied to 023's scope-boundary declaration too.
- **(B) Documented interim exception (Path A per root CLAUDE.md §6.5)** — accept that between 023
  and 031, whole-paragraph delete/merge is unsupported client-side (not silently corrupted —
  genuinely unsupported: the edit exists in the editor but no operation, structural or otherwise,
  is ever captured or transmitted for it). Requires an explicit product/owner sign-off since this
  regresses a behavior real users rely on (UAT round-4 found this exact "vanished paragraph" bug
  once already under the old dual-writer system).
- **(C) Pull forward a minimal structural capture** — scope task 023 (or a new interstitial task)
  to add ONLY the `deleteParagraph`/`mergeParagraph` structural op emission needed for this one
  case, ahead of full task 031. Scope creep beyond 023's stated boundary ("do NOT delete
  `buildContentModel`... " / no mention of adding structural capture) — would need its own
  justification + acceptance criteria.

**Recommendation**: (A) — re-sequence 023's `<deps>` to include `031`, since 031 is described in
the WBS as sequenced immediately after the Patch Engine core (030) and before the writer
retirement (032) anyway; letting 023 land in the same window closes the gap with the least
process overhead and avoids inventing new interim-exception documentation for a known, already
-burned UAT failure mode (the "vanished paragraph" corruption).

## Status

Task 023 POML: leave `<status>not-started</status>` unless/until the owner selects a path above;
this file documents the finding for that decision. No code changes were made under task 023 —
`docxBridge.ts`, `ComposeWorkspace.tsx`, `ComposeEditor.tsx`, `index.ts` are all untouched.
