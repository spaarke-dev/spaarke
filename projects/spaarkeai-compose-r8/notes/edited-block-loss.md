# WHAT THE EDITED BLOCK LOSES — task 041 baseline

> **Measured** 2026-08-22, after task 040 · Instrument:
> `ComposeBlockPreservationOracle.CompareEditedBlock` (additive — no gate number can move because it exists) ·
> Harness: [`tests/integration/seam/Compose/EditedBlockLossMeasurementTests.cs`](../../../tests/integration/seam/Compose/EditedBlockLossMeasurementTests.cs)

The Phase-3 gate measures **untouched** blocks and excludes the edited one by construction, so it reports
100% on a save that still damages the only paragraph the user typed in. This is the complement, established
**before** building FR-A05 — the same discipline task 023 applied to the control, so "we shipped atom carry"
can be checked against a number rather than asserted.

**Method**: same corpus, same representative edit, same normalization. The edit marker is stripped from the
saved block before comparison, so the residual differences are exactly what the save changed **beyond what
the user typed**.

---

## Headline

**10 of 18 documents come through with the edited block INTACT.**

That is task 040's property inheritance working — before it, the edited block was rebuilt from a model
carrying `w:jc`, `w:b`, `w:i` and nothing else. Eight documents still lose something.

| Loss | Docs | What breaks for the user | Owner |
|---|---:|---|---|
| **`w:bookmarkStart` / `w:bookmarkEnd` dropped** | 2 | **Cross-references stop resolving.** A bookmark is the target of every `REF` field in the document; editing the paragraph that *contains* the target silently breaks every reference *to* it | **FR-A05** |
| **`w:sdt` re-rendered as `w:p`** | 1 | A content control becomes ordinary text — the control, its binding and its placeholder are gone | **FR-A05** |
| **`w:br` soft breaks dropped/moved** | 1 | Line breaks inside a paragraph collapse; address blocks and signature blocks reflow | **FR-A05** |
| Run-level `rPr` variation levelled to the dominant run | 2 | A paragraph mixing small-caps / superscript / sizes is flattened to its most common formatting | FR-A04 residual |
| `w:rPr/w:spacing` (character spacing) | 1 | Letter-spacing lost on the edited run | FR-A05 |
| `w:r` / `w:t` boundary differences | 4 | Usually cosmetic (runs merge/split), but co-occurs with the above | investigate |

---

## Per document

| Document | Edited block | Differing paths |
|---|---|---|
| `symbol-section-mark.docx` | ✅ intact | — |
| `multilevel-1-1-1.docx` | ✅ intact | — |
| `court-filing-spacing.docx` | ✅ intact | — |
| `footnote-references.docx` | ✅ intact | — |
| `line-numbered-pleading.docx` | ✅ intact | — |
| `heading-style-numbering.docx` | ✅ intact | — |
| `interior-text-boxes.docx` | ✅ intact | — |
| `multipart-paraid-collision.docx` | ✅ intact | — |
| `nda-interrupted-clauses.docx` | ✅ intact | — |
| `alternate-content-duplicate-paraid.docx` | ✅ intact | — |
| `ref-cross-references.docx` | ❌ | `bookmarkStart`, `bookmarkEnd`, `r`, `t` |
| `PAT …CLAIMS…docx` | ❌ | `bookmarkStart`, `bookmarkEnd`, `rPr`, `rFonts`, `b`, `bCs`, `szCs` |
| `content-controls-sdt.docx` | ❌ | `sdt\|p` |
| `Engagement Letter.docx` | ❌ | `br`, `t` |
| `char-formatting-mixed-runs.docx` | ❌ | `smallCaps`, `sz`, `vertAlign` |
| `AppligentNDA_Signed.docx` | ❌ | `rPr/spacing`, `t` |
| `01 - Test Matter Create Fields Only.docx` | ❌ | `t` |
| `multi-author-redline-synthetic.docx` | ❌ | `t` |

Machine-readable: `edited-block-loss.json`, written by the harness alongside the merge measurement.

---

## Two defects this measurement caught on the way

### 1. Task 040's property inheritance emitted schema-INVALID output

`w:pPr` and `w:rPr` are `xsd:sequence` — child **order** is part of the schema, not a formatting detail.
Task 040's inheritance **appended** inherited children, which produced `w:jc` before `w:spacing`/`w:ind` on
any paragraph where the model had set alignment. The synthetic fixture used in 040's own tests never combined
the two, so nothing caught it; the corpus measurement surfaced it immediately as `spacing|jc`, `ind|spacing`,
`jc|ind` on two documents.

**Fixed**: inherited children are now inserted at their ECMA-376 position
(`CT_PPr` §17.3.1.26 / `CT_RPr` §17.3.2.28), not appended.

### 2. Ten of eighteen corpus fixtures were themselves schema-invalid

When the fix landed, `court-filing-spacing.docx` began reporting a `jc|spacing` difference it had not
reported before. Two readings were possible — the renderer is wrong, or the fixture is — and the measurement
alone cannot tell them apart. **Asking the SDK validator instead of trusting either** settled it: the
fixture's own `w:pPr` was out of order, and nine other fixtures had defects too.

| Defect | Fixtures | Fix |
|---|---:|---|
| `w14:paraId` used with no `xmlns:w14` / `mc:Ignorable` declaration | 5 | declarations added to `w:document` |
| `w:pPr` / `w:rPr` children out of `xsd:sequence` order | 2 | reordered |
| Duplicate VML shape id | 1 | de-duplicated |

All nine repaired fixtures are ones **this project authored** (tasks 021/022). The four real-world documents
were already valid and are **left exactly as received** — their quirks are the test case. One real-world
document (`PAT …CLAIMS…`) was touched by the repair script's `w:sectPr` pass and was **restored from backup**:
the validator had already reported it valid, so that reorder was a false positive.

**Does this invalidate the Phase-3 gate?** No. Untouched blocks are *cloned verbatim*, so they keep whatever
order the source had and compare identical either way — the gate numbers are unaffected. Only the
edited-block measurement was distorted, and only on two documents. A permanent test
(`CorpusFixture_IsSchemaValidWordprocessingML`) now holds the corpus to this standard, because a fixture that
is itself invalid makes every measurement taken against it ambiguous.

---

## What FR-A05 must now deliver, in priority order

1. **Bookmarks** — the highest-value carry. Dropping `w:bookmarkStart`/`w:bookmarkEnd` breaks cross-references
   *elsewhere in the document*, which is a silent, non-local failure: the user edits paragraph 12 and a
   reference in paragraph 40 stops resolving.
2. **Block-level `w:sdt`** — the FR-A05 case as written in the POML.
3. **`w:br`** and character-level run properties the model cannot represent.

Each must go through the `ComposeFormatChange.PreviousPropertiesXml` carry-with-SDK-parse-gate pattern:
carried XML is validated as parseable **before** it is written, and an unparseable payload degrades to thin
render + warning rather than corrupting the document.
