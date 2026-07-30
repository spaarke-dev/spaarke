# Task 051 — UAT #1B persist computed numbering to OOXML — ESCALATION (§6.5)

> **Status**: 🔔 ESCALATED — the POML escalation trigger fired. Awaiting owner decision.
> **Date**: 2026-07-30 · Rigor FULL · opus/xhigh.

## Why this escalates
The owner scoped 051 IN ("investigate authoring computed numbering back into numbering.xml now"). A read-only feasibility investigation found the **premise is largely empty** and a safe implementation is **not feasible as specified** — the POML's own escalation trigger ("if authoring cannot be scoped to divergent paragraphs without regressing I-4/byte-diff or touching the R4.5 read engine, STOP and escalate") fires. Silent scope-reduction is forbidden (CLAUDE.md §6.5), so this surfaces for a decision.

## Findings (all file:line-verified)
1. **Task 050 already resolves the real symptom.** The byte-surgical engine (`ComposeShadowPatchEngine.cs:50-56,:146-151,:182`) copies `numbering.xml`/`numPr` **verbatim** for any plain insert/delete/replace op. The all-"1." the user saw was the pre-050 **renderer misroute** re-authoring an interrupted run as fresh `startOverride=1` instances (`ComposeDocumentRenderer.cs:278-296,:607-613`). Post-050, imported edits go byte-surgical → the original numbering is preserved → **if the source rendered correctly in Word, the saved doc does too.**
2. **The R4.5 `NumberingComputationEngine` is a faithful ECMA-376 replay, not a display "correction" that diverges from Word.** Golden labels were derived by hand-simulating Word's algorithm (`corpus-manifest.md:92-95`). So for a well-formed doc the engine's output already EQUALS Word's native render — there is **no divergence to author around**.
3. **The corpus's flagship "renders wrong" fixture actually renders CORRECTLY in Word.** `nda-interrupted-clauses.docx` is documented as continuous 1→6 in Word; the restart-to-1 was a naive `<ol>`-per-run *projection/reader* bug, not Word (`corpus-manifest.md:84`).
4. **No divergence signal exists server-side.** `NumberingComputationEngine.Compute` returns a single label per paragraph; nothing computes "computed vs native-Word" pair. Detecting genuine native divergence would require a SECOND, deliberately-non-faithful numbering simulator — a fork of the algorithm (violates R5-D4) that reaches into the read-engine boundary the escalation trigger protects.
5. Of the three OOXML authoring strategies, only per-paragraph `numPr`+`startOverride` is scopeable, but it depends on the divergence signal that (4) shows cannot be produced safely; the unscoped alternative (author on every numbered paragraph) rewrites already-correct docs → breaks byte-diff 24/24 (the POML's own acceptance criterion).

## Recommended reduced scope (owner to confirm)
Reduce 051 to a **verification** task, defer native-divergent authoring to R6:
- (i) Re-UAT a **freshly-uploaded** interrupted-numbering doc through the post-050 tracked path (a fresh doc is required anyway per task-050's known-limitation note).
- (ii) Add a seam slice asserting `numbering.xml` **byte-identity** through an insert/delete/replace Apply over `nda-interrupted-clauses.docx` — proves the save preserves Word-correct 1→6, closing UAT #1B for the real case.
- (iii) Defer "author computed numbering into numbering.xml" to **R6**, gated on the owner supplying a genuine fixture that **Word itself** renders wrong (at which point per-paragraph `numPr`+`startOverride` is the design to prototype).

## Decision options for the owner
- **A — Accept reduced scope** (recommended): do (i)+(ii) now, defer (iii) to R6. Low risk, closes the real UAT concern.
- **B — Attempt full authoring anyway**: build the divergence detector + `numPr`/`startOverride` authoring. Requires forking/duplicating the numbering algorithm (R5-D4 tension) and risks I-4/byte-diff + the R4.5 read engine — high risk, likely low real-world payoff given findings 1-3.
- **C — Close as resolved-by-050**: treat #1B as fixed by 050 (numbering preserved byte-identical); skip even the extra seam slice.
