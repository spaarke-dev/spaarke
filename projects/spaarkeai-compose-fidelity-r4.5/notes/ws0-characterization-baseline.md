# Task 002 — Read-Fidelity Harness Characterization Baseline

> Written 2026-07-28 by the task 002 sub-agent execution. Sub-agent write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md` are NOT
> touched here — owned by the main session.

## Summary

Task 002 extends the R4 byte-diff/seam fidelity harness with the two golden assertions spec success criteria 2
(text-exact) and 3 (numbering-exact) require. The harness lands at
[`tests/integration/seam/Compose/ComposeReadFidelityHarnessSeamTests.cs`](../../../tests/integration/seam/Compose/ComposeReadFidelityHarnessSeamTests.cs),
extended with FR-09 construct-audit tests (alignment / ordered-list / symbol) in
[`tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeDocxProjectionBuilderTests.cs`](../../../tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeDocxProjectionBuilderTests.cs).

**No escalation fired.** Step 1's headless-invocation check confirmed `ComposeDocxProjectionBuilder.Build(ReadOnlyMemory<byte>, CancellationToken)`
is a pure, public, synchronous method (bytes-in/record-out) — it is driven directly in both test files with no
`WebApplicationFactory`, exactly like the pre-existing unit suite.

This task modifies **test code only** — `ComposeDocxProjectionBuilder.cs` / `ComposeDocxProjection.cs` / any
`Services/Compose/` production file is unchanged (verified: `git diff --stat` for this task touches only the two
test files above, plus this note).

## Corpus construct inventory (ground truth, not narrative)

Verified by unzipping every `.docx` under `tests/fixtures/compose-corpus/` and grepping `word/document.xml` for
literal OOXML markers (2026-07-28):

| Construct | Docs that carry it (body `document.xml`) | Count |
|---|---|---|
| `w:sym` | `symbol-section-mark.docx` only | 2 |
| `w:cr` | **none** | 0 |
| `w:br` | `Engagement Letter.docx` only | 5 |
| `w:noBreakHyphen` | **none** | 0 |
| `w:tab` | **none** | 0 |

`w:cr`/`w:tab`/`w:noBreakHyphen` have zero corpus coverage today — the CarriageReturn (`w:cr`) drop is
characterized via a **synthetic unit-level fixture** instead (see below), since no corpus doc exercises it. If a
future corpus addition (owner-supplied or a WS-2 task) introduces `w:cr`/`w:tab`/`w:noBreakHyphen` into a body
paragraph, the text-exactness Theory in `ComposeReadFidelityHarnessSeamTests.cs` picks it up automatically (the
"known lossy construct" predicate already checks for `CarriageReturn`, and `TabChar`/`NoBreakHyphen` are already
correctly represented today — see below) with zero code changes.

## (a) Text-exactness — NFR-01

**Live (non-skipped), per-paragraph, over all 8 corpus docs**, in
`TextExactness_OnEveryCorpusDoc_MatchesSourceRunTextOrCharacterizesKnownDrop`. Every paragraph across all 8
corpus docs is compared source-run-text-verbatim vs. projected-text, EXCEPT paragraphs carrying a `SymbolChar`/
`CarriageReturn` descendant, which are asserted to **currently mismatch** (the characterization) rather than
skipped — the whole Theory is green today.

**Result: 7 of 8 corpus docs are already 100% text-exact today** (all their paragraphs match source-run-text
verbatim, including `Engagement Letter.docx`'s 5 `w:br` breaks, which `ComposeDocxProjectionBuilder.RenderRun`
already correctly emits as `<br>`). Only `symbol-section-mark.docx` has any characterized drop (its 2 `w:sym`
paragraphs) — the other 4 paragraphs in that doc (including its 2 bulleted-list paragraphs) are text-exact.

| Corpus doc | Paragraphs | Text-exact today | Characterized drop (WS-2 target) |
|---|---|---|---|
| `01 - Test Matter Create Fields Only.docx` | all | ✅ | — |
| `Engagement Letter.docx` | all (incl. 5 `w:br`) | ✅ | — |
| `PAT 109270W-1 - CLAIMS...docx` | all | ✅ | — |
| `nda-interrupted-clauses.docx` | all | ✅ | — |
| `heading-style-numbering.docx` | all | ✅ | — |
| `multilevel-1-1-1.docx` | all | ✅ | — |
| `line-numbered-pleading.docx` | all | ✅ | — |
| `symbol-section-mark.docx` | 6 | 4 ✅ | 2 ❌ (paraIds `AF736810`, `4789D1A7` — the `w:sym` §-mark runs) |

### `w:sym` drop detail (WS-2 FR-06 target)

For `symbol-section-mark.docx` paraId `AF736810`: golden text (NFR-01-correct, per corpus-manifest.md row 12) is
`"§  2.01  Confidentiality Obligations. The Receiving Party shall maintain confidentiality."`; **today's actual
projected text is `"  2.01  Confidentiality Obligations. The Receiving Party shall maintain confidentiality."`**
— the leading `§` (Symbol-font `F0A7`, U+00A7) is silently absent. Same shape for paraId `4789D1A7` ("§ 2.02
Term..."). Root cause: `ComposeDocxProjectionBuilder.RenderRun`'s run-child `switch` (around `:685-708`) has no
`case SymbolChar` — it falls to `default: break`, contributing nothing.

### `w:cr` drop detail (WS-2 FR-05 target) — synthetic, unit-level

No corpus doc carries `w:cr`. Characterized instead via a synthetic fixture in
`ComposeDocxProjectionBuilderTests.Build_ParagraphWithCarriageReturnRun_CurrentlyDropsGlyphSilently_CharacterizationForWS2Fr05`:
a paragraph with runs `"before"` + `CarriageReturn` + `"after"` projects **today** as the HTML text
`"beforeafter"` — no `<br>`, no separator of any kind. This is a stricter drop than `w:sym`: `w:br` (Break) is
handled correctly (`<br>`) by the SAME `switch` block, but the sibling `CarriageReturn` case is simply absent.

## (b) Numbering-exactness — NFR-02

**The projection computes NO per-paragraph displayed number today.** `ParaIdMapEntry` (`ParaIdPreParser.cs:185`)
carries only `(Index, ParaId, IsMinted)` — there is no field a "computed label == golden label" assertion could
read without inventing one, which would be WS-3 scope creep (root CLAUDE.md §11). Two artifacts therefore exist:

1. **The full target-shape Theory** (`NumberingExactness_OnLegalNumberingExemplars_ComputedLabelMatchesGoldenWordLabel`),
   parameterized over 24 (docFileName, paragraph-ordinal-index, goldenLabel) rows drawn verbatim from
   corpus-manifest.md §1.5, gated behind a one-line stub `GetCurrentComputedNumber` that returns `null` today.
   Marked `[Theory(Skip = "numbering-exactness target — unblocked by WS-3 tasks 031/032 ...")]`. **The flip
   point**: once WS-3 adds a computed-label field to the projection (FR-11/FR-16), swap
   `GetCurrentComputedNumber`'s body to read that field and remove the `Skip` — every row either goes green
   (WS-3 correct) or red (WS-3 regression), a real acceptance gate, not a rubber stamp.

2. **Four live (non-skipped) characterization Facts** pinning the CURRENT, structurally observable defect per
   exemplar — these require no invented field, only the HTML/warnings the projection emits today:

| Exemplar | Current (broken) shape | Golden (Word) shape | Test |
|---|---|---|---|
| `nda-interrupted-clauses.docx` | 2 separate `<ol>` blocks, 3 `<li>` each — browser auto-count would show 1,2,3 then 1,2,3 again | ONE continuous run, 1..6 | `NumberingCharacterization_NdaInterruptedClauses_TodayRestartsTheListAtEachInterruption` |
| `heading-style-numbering.docx` | Zero headings carry ANY digit prefix (`>Confidentiality<`, not `>4.2 Confidentiality<`) | `"4.2 Confidentiality"` (FR-12's literal acceptance example) | `NumberingCharacterization_HeadingStyleNumbering_TodayRendersNoNumericPrefixOnAnyHeading` |
| `multilevel-1-1-1.docx` | ONE flat `<ol>`, 7 `<li>` (levels collapsed); `multi-level-numbering` warning fires 5× (once per ilvl>0 paragraph) | `1. / 1.1. / 1.1.1. / 1.1.2. / 1.2. / 2. / 2.1.` | `NumberingCharacterization_Multilevel111_TodayCollapsesAllLevelsIntoOneFlatList` |
| `line-numbered-pleading.docx` | 4 separate `<ol>` blocks (2+2+4+4 `<li>`) — one per section heading | ONE continuous run, 1..12 | `NumberingCharacterization_LineNumberedPleading_TodayRestartsTheListPerSection` |

`symbol-section-mark.docx`'s 2 bulleted-list paragraphs are deliberately excluded from numbering-exactness —
bullets have no golden numeric label (manifest row 12's negative case); its Wingdings custom bullet glyph is
also untestable via projected-HTML-text comparison (it is a numbering-level `lvlText` PUA glyph the browser's
default `<ul>` styling never surfaces as emitted text) — noted here as a known gap, not asserted.

## Construct-audit additions (FR-09 — previously absent)

`ComposeDocxProjectionBuilderTests.cs` had **zero** alignment/ordered-list/symbol fixtures before this task
(verified: no `Justification`/`NumberingProperties`/`SymbolChar`/`CarriageReturn` reference in the file at task
start). Added, all live/passing:

- `Build_ParagraphWithDecimalNumPr_RendersInsideOrderedList`
- `Build_ParagraphWithBulletNumPr_RendersInsideUnorderedList`
- `Build_ParagraphWithJustification_EmitsTextAlignStyle` (Theory: center/right/both)
- `Build_ParagraphWithLeftJustification_EmitsNoTextAlignStyle` (negative case — Word's default emits no style)
- `Build_ParagraphWithSymbolCharRun_CurrentlyDropsGlyphSilently_CharacterizationForWS2Fr06`
- `Build_ParagraphWithCarriageReturnRun_CurrentlyDropsGlyphSilently_CharacterizationForWS2Fr05`

## Test run confirmation

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter "FullyQualifiedName~Compose"
Passed! - Failed: 0, Passed: 617, Skipped: 1, Total: 618
```

The 1 skip is the whole-Theory `[Theory(Skip=...)]` on `NumberingExactness_OnLegalNumberingExemplars_...`
(xUnit v2 reports a skipped `[Theory]` as a single skipped result, not one per `[MemberData]` row — its 24 data
rows do not enumerate individually while skipped). No other test in the `Compose` surface regressed.

## Deviations from the task POML

None. Both required outputs exist at their named locations (`ComposeDocxProjectionBuilderTests.cs`,
`tests/integration/seam/`); the harness reports text-exact ✅/❌ per doc (via the per-doc `_output.WriteLine`
tally in the text-exactness Theory) and numbering-exact ✅/❌ per exemplar (via the 4 live characterization Facts
+ the skip-gated golden Theory); no `Mock<HttpMessageHandler>`/DI-registration/ctor-null test was introduced;
`ComposeDocxProjectionBuilder.cs` is unchanged; build is green.
