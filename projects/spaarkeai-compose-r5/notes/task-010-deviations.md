# Task 010 (G3 alignment applier) — Deviations & Findings

> Written per task 010 POML step 9. No scope change from the task brief; two findings worth
> recording for downstream tasks (011/021/041).

## 1. `ParagraphPropertiesChange`'s nested properties type — `ParagraphPropertiesExtended`, not `PreviousParagraphProperties`

**What happened**: the first implementation used `DocumentFormat.OpenXml.Wordprocessing.PreviousParagraphProperties`
for the nested `<w:pPr>` child of `<w:pPrChange>` (by analogy — the class name reads as "the obvious
one," and it compiled and even serialized to the correct `<w:pPr>` XML tag when inspected via
`OuterXml` immediately after construction).

**The bug**: on round-trip (`WordprocessingDocument.Open` re-parsing the SAVED bytes), the OpenXml SDK's
schema-contextual deserializer resolves the `<w:pPr>` child of `<w:pPrChange>` to
`ParagraphPropertiesExtended`, NOT `PreviousParagraphProperties` — despite both C# types serializing to
the identical `<w:pPr>` local name. This is an OpenXml SDK convention: the SAME XML element name can map
to different C# types depending on parent context (the SDK picks the concrete type from a compiled
schema keyed on (parent type, child name), not purely from the child's own tag name).

**How it was caught**: the seam test `SetBlockAttrAlignment_ChangedTwiceInOneApply_PPrChangeRecordsImmediatelyPriorValue_NeverStacks`
failed with `previousJc` unexpectedly null when read back via `GetFirstChild<PreviousParagraphProperties>()`
on a FRESHLY RE-OPENED document (not the in-memory tree the engine had just built). A throwaway diagnostic
test dumped `pPrChange.ChildElements` and printed each child's actual runtime `.GetType().FullName` —
`DocumentFormat.OpenXml.Wordprocessing.ParagraphPropertiesExtended`, confirmed. Fixed by using
`ParagraphPropertiesExtended` in the engine (and matching the type in the seam test assertions); all
tests pass after the fix, including the corpus byte-diff round-trip.

**Lesson for future OpenXml SDK work (011/014/021/033)**: when constructing a schema-contextual nested
element (any `w:*Change` tracked-property element with a nested "previous state" child, or similar
same-tag-different-context patterns), **verify the concrete type by round-tripping through
`WordprocessingDocument.Open` and inspecting the deserialized element's actual `GetType()`** — do not
assume the class name matches the parent-context type from IntelliSense/class-name inspection alone. This
generalizes beyond `w:pPrChange`: `w:rPrChange` (`PreviousRunProperties`), `w:tblPrChange`/`w:trPrChange`/
`w:tcPrChange` (table-property-change, relevant to task 014 G4 tables) likely have the same
class-name-vs-round-trip-type gap and should be verified the same way before trusting the "obvious" class
name.

## 2. Publish-size baseline measurement-tool variance (informational, not a regression)

The project's stated baseline (task 001, `notes/baseline-verification.md`) is **46.70 MB excl PDBs**.
Task 010's own same-tool, same-method A/B measurement (git-stash the diff, publish clean baseline vs.
publish with task 010's change, zip both identically via a Python `zipfile.ZIP_DEFLATED` script at
`compresslevel=9`, both **excl PDBs**) produced:

- Clean baseline (no task 010 change): **45.1725 MB**
- With task 010's change: **45.1730 MB**
- **Delta: ~0.0005 MB (essentially zero)** — expected, since the change is pure C# (no new package, no
  new csproj entry).

The absolute ~1.5 MB gap between the project's stated 46.70 MB baseline and this task's 45.17 MB
same-tool baseline is most likely attributable to a different zip/compression tool or compression-level
default between whatever script produced the original 46.70 MB figure and the Python `zipfile` script used
here (PowerShell's `Compress-Archive` was attempted first but hit an unrelated sandbox path-quoting error
in this environment and was abandoned in favor of Python for a working same-tool A/B). **The task's
`NFR-01`/§10 threshold (≥+5 MB single-task delta → justify; ≥55 MB cumulative → review; ≥60 MB → hard
stop) is unaffected either way** — both figures are well under the ceiling, and the A/B comparison
(same tool both sides) is the reliable signal that task 010 added ~0 MB. Future tasks should re-verify with
whatever tool produced the canonical baseline if an authoritative absolute number matters (e.g. task 041's
hardening gate), rather than trusting either figure as directly comparable across tools.
