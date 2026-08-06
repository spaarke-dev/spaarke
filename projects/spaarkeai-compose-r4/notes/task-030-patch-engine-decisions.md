# Task 030 — ComposeShadowPatchEngine core: decisions & deviations

> **Date**: 2026-07-22 · **Status**: task complete · engine at
> `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeShadowPatchEngine.cs`

The engine promotes + hardens the task-005 `SpikeOpenXmlApplier` into the single production byte-author
(D5). Three decisions are worth recording; none is an ADR tension (ADR-007/010/013/029 all satisfied).

## 1. Tracked-change reconciliation is REFUSED, not guessed (POML `<escalation>` resolution)

The projection makes runs inside a pre-existing `w:ins`/`w:del` editor-visible, so the client *can* emit an
op whose `(runIndex, run-local-offset)` resolves onto such a run. Reconciling a NEW edit against an existing
revision is genuinely ambiguous (does deleting already-inserted text become a plain removal, or a del-of-ins?).

**Resolution (root §6.5, path C-adjacent "refuse rather than approximate")**: the engine detects
`RunTrackChange != None` during resolution/split and throws
`ComposePatchException(TrackedChangeReconciliationUnsupported)` — deterministic, never mis-places bytes.
This is NOT a task blocker: the Phase-0 corpus worst-offender (CIPO) is track-changes-clean per the corpus
manifest, so the settled-run path is fully corpus-proven. The reconciliation semantic is surfaced for a
later, explicit decision (a candidate for task 031/beyond) rather than silently approximated. Same guard
covers opaque atoms (field / content control / complex object): ops may target atom boundaries only.

## 2. Marks applied directly to RunProperties (v1), not as `w:rPrChange`

`setMark`/`clearMark` isolate the range and toggle the mark via the SDK's typed `RunProperties` properties
(schema-safe insertion order). Tracking a format change as a native `w:rPrChange` revision is deferred — it is
not required by task 030's text/mark scope, and the run-split + range-isolation mechanics (the load-bearing
part) are identical either way. Documented inline in `ApplyMarkOverRange`.

## 3. Comments carried as `ComposeAnchoredComment` (durable anchor), not `DocxAnnotation`

The op schema (task 003, closed 10-op set) has no comment op, but the engine consolidates
`DocxAnnotationWriter`'s comment capability (retired in task 032) and must migrate EDGE-1 (comment-before-
trackchange ordering). A new small record `ComposeAnchoredComment { ParaId, Range, CommentText, Author,
Initials?, Date }` carries comments on `Apply(..., comments)` — the text-search-free (I-7) replacement for
`DocxAnnotation.target_text`. Justification (§11): `DocxAnnotation` anchors by whole-doc text search (the 422
root cause); the shadow engine needs a durable paraId+range anchor. Emitted before any track-change op (EDGE-1).

## Run-index space (finding #2, load-bearing)

`FlattenEditorRuns` MIRRORS `ComposeDocxProjectionBuilder.CollectRunBoundaries` exactly (descends
`w:hyperlink`/`w:ins`/`w:del`/`w:sdt`; fields / complex objects / special content controls become single
opaque atom slots) so `runIndex`/offset mean the same thing the client measured over the projection — NOT raw
`para.Elements<Run>()`. Proven by `Apply_RunIndexOverHyperlinkFlatten_LandsAfterTheHyperlink`. Per-op
re-flatten keeps sequential same-paragraph batches drift-safe (finding #1).

## Scope boundary

Structural paragraph ops (`splitParagraph`/`mergeParagraph`/`insertParagraph`/`deleteParagraph`) and
`setBlockAttr` route to a clear `ComposePatchErrorKind.StructuralOpNotYetImplemented` seam — task 031. The
engine is NOT yet routed through `ComposeService.SaveAsync` and the old writers are NOT retired — task 032.
