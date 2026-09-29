# WHAT THE EDITED BLOCK LOSES — task 041

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

## Result after FR-A05 carry

**12 of 18 documents come through with the edited block INTACT** — up from 10 at the baseline, and the two
worst failure classes are closed.

| | Baseline (after 040) | After FR-A05 carry |
|---|---:|---:|
| Edited block intact | 10 / 18 | **12 / 18** |
| Bookmarks dropped | 2 docs | **0** |
| Content control dissolved | 1 doc | **0** |
| `PAT …CLAIMS…` differing paths | 8+ | **1** (attribute presence only) |

`ref-cross-references.docx` and `content-controls-sdt.docx` both go from damaged to intact, and each has its
own named test so the aggregate cannot rise for the wrong reason.

### What remains

| Loss | Docs | Real? |
|---|---:|---|
| `w:br` soft breaks | 1 | **Yes** — line breaks inside a paragraph collapse |
| Run-level `rPr` variation (`smallCaps`, `sz`, `vertAlign`, `rPr/spacing`) | 2 | **Yes** — FR-A04's dominant-run residual |
| `p/r/t` | 5 | **No** — `xml:space` attribute presence, text character-identical (see below) |

---

## Baseline, before the carry

**10 of 18 documents came through with the edited block INTACT.**

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

## The `xml:space` class — investigated, not real, and an experiment that backfired

The `p/r/t` differences are **not text loss**. The renderer emits `xml:space="preserve"` on every `w:t`; the
source documents carry it only on some. The text is character-identical.

An obvious-looking fix — emit it only when the text has leading/trailing whitespace, which is when it is
*needed* — was implemented and measured. **It made things markedly worse**: Word emits the attribute far more
liberally than that rule, so the renderer went from disagreeing with 5 documents to disagreeing with 15, and
the intact count fell from 12 to 2. Reverted, with the finding recorded at the call site so the class is not
re-investigated as if it were content.

The lesson is the one the `mc:AlternateContent` paraId experiment taught in task 040: a change that looks
obviously right against a rule of thumb has to be measured against the corpus before it ships, because the
corpus is the only thing that knows what Word actually does.

---

## Why the carry takes constructs from the BASE, not from a client payload

The task POML anticipated extending the client's `composeBlockAtom` / `composeInlineAtom` nodes to ferry
verbatim XML through the model. **Reconciled to base-carry instead**, on four counts:

- The client never touches OOXML, so **ADR-049 I-2 holds trivially** rather than by discipline.
- **No wire growth**, and no opportunity for a client to mangle a payload it cannot interpret.
- It is the **same mechanism as FR-A04 property inheritance**, extended from `w:pPr`/`w:rPr` to sibling
  constructs — one carry path rather than two (root §11: extend before you add).
- It works for constructs the editor renders **invisibly**. A bookmark has no editor representation at all,
  so there is nothing for a client-side atom node to attach to in the first place.

What base-carry cannot do is track a construct the user **moved or deleted**. For bookmarks and content
controls that is correct behaviour: neither is deletable through the editor, so re-instating them is right.

**Bookmark spans widen to the paragraph.** The original extent was defined by positions among runs whose text
the user has just changed, so the exact character range no longer exists to restore. Starts go at the front of
the content and ends at the back — exact for a bookmark spanning the whole paragraph (the shape every
cross-reference target takes), a widening for a partial one. Widening keeps the reference resolving; dropping
it does not.

**FR-A06 (table + atom identity) is not needed as specified.** Its stated rationale was "without identity the
merge cannot decide whether a table changed" — but the merge compares the canonical JSON of the whole block,
table contents included, so it already can. Recorded as a reconciliation rather than silently skipped.

---

## What FR-A05 delivered, in the priority order the measurement set

1. **Bookmarks** — DONE. The highest-value carry, and the least visible failure without it: a bookmark is the
   target of every `REF` field, so the user edits paragraph 12 and a reference in paragraph 40 stops
   resolving, with nothing in the edited paragraph looking wrong. `CarryBookmarks`, asserted by
   `EditedBlock_KeepsItsBookmarks_SoCrossReferencesStillResolve`.
2. **Block-level `w:sdt`** — DONE. The rendered paragraph is re-wrapped in the base's own shell, so alias,
   tag, id, placeholder and binding survive verbatim. `TryWrapInSdtShell`, asserted by
   `EditedBlock_KeepsItsContentControlShell`. An unreconstructable shell degrades to the bare paragraph
   **with a `content-control-flattened` warning** — never a malformed control, never a refusal.
3. **`w:br` and character-level run properties** — STILL OPEN. Both need the *projection* to model them: they
   are read-side gaps, not render-side, so base-carry cannot reach them. Carried to the task-045 residual
   list for whoever next touches `ComposeDocxProjectionBuilder`.

Each must go through the `ComposeFormatChange.PreviousPropertiesXml` carry-with-SDK-parse-gate pattern:
carried XML is validated as parseable **before** it is written, and an unparseable payload degrades to thin
render + warning rather than corrupting the document.
