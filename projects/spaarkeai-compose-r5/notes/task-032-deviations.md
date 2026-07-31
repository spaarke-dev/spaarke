# Task 032 — G11 Track-changes-off keeps imported/AI redlines visible — Deviations & Notes

> **Status**: ✅ COMPLETE · 2026-07-30 · FULL rigor (overridden UP from POML STANDARD: shared
> `TrackChangesExtension.ts`/`ComposeEditor.tsx`, NFR-09 surface) · sonnet/high · view-only.

## Key finding: BUG-B is already resolved by the decoration-not-mark architecture
Investigation of the current code (tasks 020/022/032 built the live Track-Changes design) showed the
G11 behavior is **already correct**, so **no production behavior change was needed**:

- **The user's own free-typed edits** render as a ProseMirror **DECORATION overlay**
  (`buildTrackChangeDecorations`), gated by the plugin's `enabled` flag. The toolbar toggle
  (`ComposeEditor.tsx:2379`) flips ONLY this via `setMeta(trackChangesPluginKey, {enabled})` → when off,
  `decorations()` returns `DecorationSet.empty` (overlay vanishes).
- **Imported/AI redlines** render as first-class **schema marks** (`insertion`/`deletion` —
  `InsertionMark`/`DeletionMark`; imported revisions via `importedRevisions.ts` `addMark`, AI suggestions
  likewise). These are document CONTENT, styled by distinct classes (`compose-mark-*`) with semantic
  tokens (ADR-021), rendered independently of the toggle. No CSS ancestor-gate hides them.
- `buildTrackChangeDecorations` additionally **SKIPS any block carrying an insertion/deletion mark**
  (TrackChangesExtension.ts:77-84), so the overlay never even touches an imported/AI redline when enabled.

Net: toggling the overlay off hides ONLY the user's own overlay; imported/AI marks stay visible. That is
exactly the G11 requirement. The POML's premise ("fix the flip so it stops hiding marks") reflected the
UAT-era symptom; the current decoration/mark separation already prevents it. Per the directional step
mode, the right action was to **verify + lock the invariant**, not manufacture a change to correct code.

## What shipped
- **`TrackChangesExtension.test.ts`** — appended a `G11 (task 032)` describe block (2 tests) that
  DIRECTLY asserts BUG-B: (1) an imported/AI `insertion` mark survives toggling the overlay off AND back
  on (present in `getHTML()` throughout); (2) the overlay decoration set covers a user-edited block but
  NEVER the imported-mark block (overlay-vs-mark separation). Complements the existing "skip AI-marked
  block" + toggle tests. **10/10 green** (8 existing + 2 new).
- **`TrackChangesExtension.ts`** — added a clarifying comment at the `decorations()` flip documenting the
  G11 invariant (toggle flips ONLY the overlay; imported/AI marks are toggle-independent content). Doc-only
  — no behavior change.

## Escalation trigger — did NOT fire
Trigger: "if keeping imported redlines visible would alter saved bytes, STOP." It stayed strictly
view-only — no write-path/OOXML/persistence touch (the fix is that marks are already content + the toggle
is already decoration-only). No escalation.

## Verification
- `TrackChangesExtension` **10/10**; client typecheck clean for the touched files.
- **No C# change** → Compose C# suite (814), byte-diff 24/24, publish 48.13 MB, ArchTests (3 pre-existing)
  all unchanged from task 030.
- Save output byte-identical whether the toggle is on or off (the toggle never touched the write path —
  proven structurally: the overlay is decorations, and the existing "pure VIEW layer" test at
  TrackChangesExtension.test.ts:115 already asserts building decorations doesn't change content).

## Step 9.5 quality gates (applied)
- **code-review**: no production behavior change (verify + lock); the added tests are behavior-anchoring
  regression tests (not scaffolding — they protect the concrete BUG-B invariant); no security; no AI smells.
- **adr-check**: ADR-049 (client view+controller; the overlay is a pure decoration, imported redlines are
  content marks — no byte authoring, no persistence), ADR-021 (marks + overlay both use semantic tokens,
  no hex — dark-mode-correct), ADR-013 (no AI type), NFR-03 (no new package). No BFF → §10 N/A.

## PR obligations
- `/conflict-check` before the shared-client PR (TrackChangesExtension.ts + ComposeEditor.tsx overlap the
  other Phase-3 tasks — all merged in sequence on this branch; analysis-hub-r1 #694 accept/reject-redline
  parity unaffected — no write-path change).
