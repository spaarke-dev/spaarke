# Task 011 (G3 heading/list applier) — Deviations & Findings

> Written per task 011 POML step 11. No scope change from the brief; findings + intentional
> scope decisions worth recording for downstream tasks (012/014/021/041).

## 1. Numbering reuse is MODEL-only in the write path — the engine is not "called" to renumber

Per task 005 (reference-in-place) + R5-D4, the list applier reuses R4.5's numbering by calling
`ComposeDocxProjectionBuilder.BuildNumberingModel` / `ResolveParagraphNumbering` (the read-side MODEL), NOT
by computing labels on write. **Word computes numbering labels at render** from `w:numPr` (numId + ilvl) —
the `.docx` stores no literal label — so there is no write-side numbering algorithm to fork. The applier's
job is only to write a `w:numPr` the read-side `NumberingComputationEngine` can resolve; the seam test then
drives that exact engine over the patched bytes and asserts the label ("matches read-time model"). This is
the strongest possible form of R5-D4 compliance (the engine is literally the oracle in the test) and means
`NumberingComputationEngine.Compute` is never invoked in the write path — correctly, since invoking it there
would imply a second numbering author, which the project forbids.

## 2. numId selection: reference-existing-first, author-fallback (NFR-08)

`EnsureListNumbering(ordered)` prefers an EXISTING direct-list numId already in the document's numbering
model (excluding style-linked/heading numbering), and only AUTHORS a minimal ordered/bullet definition when
the document carries no suitable list numbering. Authoring is REQUIRED (not optional): removing the SDL-2
guard must never trap the user on a plain doc with no lists (NFR-08 no user-triggerable error / no silent
no-op). Authoring declares numbering DATA (an abstractNum + instance mirroring `ComposeDocumentRenderer`'s
decimal/bullet vocabulary) — it does NOT re-implement the label ALGORITHM, so R5-D4 holds.

## 3. Byte-surgical model build — probe over retained bytes, not the editable package (NFR-01 / I-4)

**Finding:** merely READING `mainPart.NumberingDefinitionsPart.Numbering` (or `.StyleDefinitionsPart.Styles`)
via the SDK typed accessor MATERIALIZES that part's DOM; the SDK then RE-SERIALIZES it (normalized, different
bytes) when the package is saved — even though nothing changed. `BuildNumberingModel` reads both parts, so a
naive "build the model from `_mainPart`" made a list op rewrite `numbering.xml` + `styles.xml` (observed:
933→802 / 619→575 bytes), breaking the untouched-parts byte-identity the alignment seam already guaranteed.

**Fix:** `GetNumberingModel()` builds the model from a **throwaway read-only probe** opened over the retained
bytes, so a reference-only list op never materializes the EDITABLE numbering/styles DOM → those parts stay
copied-verbatim byte-identical. Once this session AUTHORS a definition (which legitimately modifies
`numbering.xml`), it flips `_numberingAuthored` and reads the model from the now-modified editable part so a
subsequent list op sees the just-authored numId. `styles.xml` is never modified by this task in any path.
Downstream (012/014) doing similar model reads on the editable package MUST use the same probe pattern.

## 4. w:pPrChange nested properties = ParagraphPropertiesExtended, and it is CT_PPrBase

Confirmed task 010's finding (§1 of task-010-deviations): the `w:pPrChange` child deserializes as
`ParagraphPropertiesExtended`. **Additional finding:** that element is `CT_PPrBase` — it holds the
paragraph-property children (`w:pStyle`/`w:numPr`/`w:jc`/`w:ind`…) but NOT `w:rPr` (paragraph-mark run
properties), `w:sectPr`, or a nested `w:pPrChange`. `SnapshotPriorPPr` therefore clones the prior `w:pPr`
EXCLUDING those three — cloning them in serializes but is schema-invalid (Word rejects; the lenient SDK
reader does not, so a round-trip test would NOT catch it — caught in code review, fixed before commit).
Task 014 (table-property changes: `w:tblPrChange`/`w:trPrChange`/`w:tcPrChange`) should verify the analogous
CT_*Base membership before snapshotting prior table properties.

## 5. Scope: TRACKED path only; client rebased-log extension (task-022 surface) touched intentionally

Like task 010, this task implements ONLY the imported/tracked `w:pPrChange` path — the engine's sole caller.
The Style/List appliers are self-contained (own resolve + own mutation) so task 021's `trackChanges:false`
clean branch can be added as a sibling without touching the tracked path. Acceptance criterion 3 (authored
doc → clean properties) is task 021's deliverable; task 010 set the same precedent.

**One deliberate cross-task edit:** `classifyStep` now emits `setBlockAttr` from `ReplaceStep`/
`ReplaceAroundStep` (heading/list), but `buildAnchor`/`deriveOperation` (the task-022 `RebasedOperationLog`
save-path machinery, same file) only handled `setBlockAttr` from an `AttrStep` (alignment). Left unextended,
the new ops would be SILENTLY DROPPED by the save log (an NFR-08 silent-loss path). So `buildAnchor` now
anchors a `setBlockAttr` from a block-boundary step, and `deriveOperation` returns the op as-captured (its
paraId is captured durably from the pre-step block; re-deriving from the live position could mis-address a
heading toggle that re-minted its paraId). This is a minimal, well-commented extension needed for coherence —
flagged for the task-022 owner.

## 6. Publish size

Release publish (excl PDBs, same Python zip @ compresslevel=9 A/B method as task 010): **45.19 MB** — delta
~+0.02 MB vs task 010's 45.17 MB (pure C#, zero new package, NFR-03 honored). Well under the 60 MB ceiling.
