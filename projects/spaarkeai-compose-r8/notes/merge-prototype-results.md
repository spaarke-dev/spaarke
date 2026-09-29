# The merge prototype — results (task 030)

> **Measured** 2026-08-21 · **Control**: [`control-measurement.md`](control-measurement.md) ·
> **Code**: `ComposeDocumentRenderer.RenderIntoCarrier(…, mergeUnchangedBlocks: true)` — **opt-in, default
> OFF**, so no production behaviour changes. Measurement:
> [`MergePrototypeMeasurementTests`](../../../tests/integration/seam/Compose/MergePrototypeMeasurementTests.cs).

---

## Headline

| | Control (R6, today) | **Merge prototype** |
|---|---:|---:|
| Overall block preservation | 18.08% | **100.00%** |
| Near-tier preservation | 6.67% | **100.00%** |
| Documents at 100% overall | 1 of 18 | **18 of 18** |
| Documents at 100% near-tier | 2 of 14 measurable | **14 of 14** |

The 109-block patent claims document goes from preserving **one** block to preserving **all 108 untouched
blocks**. The signed NDA goes from **one of 49** to **all 49**.

Both arms run **in the same test, on the same bytes, with the same edit, through the same oracle**. The only
variable is the `mergeUnchangedBlocks` flag.

---

## Why 100% is not a false pass

A merge that cloned *every* block — including the edited one — would report 100% preservation on every
document while silently throwing the user's edit away. That is the one failure that clears a preservation
gate and ships R9, and **the preservation number alone cannot distinguish it from success.** Three
independent facts, asserted per document, rule it out:

1. **The edit is present in the merged output.** `ExtractBodyText(merged)` contains the marker on all 18.
2. **Exactly one block was rendered per document** (`RenderedBlocks == 1`) — the edited one. An all-clone
   merge would report zero.
3. **The oracle located and excluded the edited block** (`EditedBlockIndex >= 0`). A `-1` would mean it had
   measured the edited block as if untouched.

And the strongest check of all: **the same oracle, in the same run, reports 18.08% for the control arm.** An
instrument that returns two different answers for two inputs is measuring something.

---

## Per-document

| Document | overall (control → merge) | near tier (control → merge) | cloned | rendered |
|---|---|---|---:|---:|
| `PAT 109270W-1 - CLAIMS…docx` | 0.93% → **100%** | 0.00% → **100%** | 107 | 1 |
| `AppligentNDA_Signed.docx` | 2.04% → **100%** | 0.00% → **100%** | 48 | 1 |
| `multilevel-1-1-1.docx` | 12.50% → **100%** | 0.00% → **100%** | 7 | 1 |
| `Engagement Letter.docx` | 16.67% → **100%** | n/a | 11 | 1 |
| `court-filing-spacing.docx` | 20.00% → **100%** | 0.00% → **100%** | 4 | 1 |
| `01 - Test Matter Create Fields Only.docx` | 25.00% → **100%** | n/a | 7 | 1 |
| `multi-author-redline-synthetic.docx` | 25.00% → **100%** | 0.00% → **100%** | 7 | 1 |
| `char-formatting-mixed-runs.docx` | 33.33% → **100%** | 0.00% → **100%** | 2 | 1 |
| `footnote-references.docx` | 33.33% → **100%** | 0.00% → **100%** | 2 | 1 |
| `ref-cross-references.docx` | 33.33% → **100%** | 0.00% → **100%** | 2 | 1 |
| `symbol-section-mark.docx` | 33.33% → **100%** | 0.00% → **100%** | 5 | 1 |
| `nda-interrupted-clauses.docx` | 41.67% → **100%** | 14.29% → **100%** | 11 | 1 |
| `line-numbered-pleading.docx` | 47.83% → **100%** | 36.84% → **100%** | 22 | 1 |
| `interior-text-boxes.docx` | 50.00% → **100%** | n/a | 3 | 1 |
| `multipart-paraid-collision.docx` | 50.00% → **100%** | 0.00% → **100%** | 1 | 1 |
| `alternate-content-duplicate-paraid.docx` | 66.67% → **100%** | n/a | 2 | 1 |
| `content-controls-sdt.docx` | 66.67% → **100%** | 100% → **100%** | 2 | 1 |
| `heading-style-numbering.docx` | 100% → **100%** | 100% → **100%** | 10 | 1 |
| **Corpus** | **18.08% → 100%** | **6.67% → 100%** | **253** | **18** |

Every construct family that the control lost — indentation, spacing, `pStyle`, `numPr`, `rFonts`, tabs,
character formatting, footnote references, fields, content controls — is preserved, because a cloned block
is not re-derived at all. **There is no per-family logic in the merge.** That is the point: property
preservation is not a feature list to be completed, it is a consequence of not rewriting the block.

### The three R4-breakers, individually (task 021 fixtures)

| Fixture | Result | What it proves |
|---|---|---|
| `alternate-content-duplicate-paraid.docx` | 66.67% → **100%** | Duplicate `w14:paraId` across `mc:Choice`/`mc:Fallback` does not mis-pair — pairing is by document order, and the whole `mc:AlternateContent` subtree clones as one opaque unit. |
| `interior-text-boxes.docx` | 50.00% → **100%** | `w:txbxContent` paragraphs are never entered. The host paragraph clones whole, text box intact — the construct R6 flattens. |
| `multipart-paraid-collision.docx` | 50.00% → **100%** | A `paraId` shared across `document.xml` / `footnotes.xml` / `header1.xml` is irrelevant: the merge never uses `paraId` as a key, and the footnote reference survives inside its cloned run. |

These are the constructs that ended R4. The merge is unaffected by all three **because it does not use
`paraId` as a key and never descends into opaque regions** — the two disciplines the project CLAUDE.md names
as "do not re-derive".

---

## FR-G06 — heavy restructure

**Case**: the entire body reversed (`nda-interrupted-clauses.docx`, 12 blocks). This is strictly harsher than
any real cut-paste: every block moves, and document-order pairing finds a counterpart at each index that is
never the right one.

**Result**: `cloned=0, rendered=12 of 12`. No hard fail, no refusal, a readable package out.

**Graceful degradation confirmed** — and the mechanism degrades to *exactly R6's behaviour*, which is the
correct floor. It is never worse than what ships today.

**This is also the prototype's sharpest limitation, and it is a real one**: under reorder the merge delivers
**no benefit at all**. Document-order pairing cannot recognise a block that moved. See "What task 040 must
do differently" below.

## FR-G07 — N-cycle Word ⇄ Compose round trip

**N = 5.** Chosen because a per-cycle loss of even 2–3% compounds into an unmistakable slope over five
cycles, while staying cheap enough to run on every gate execution. Each cycle takes the **previous cycle's
output** as the next cycle's carrier — what a user editing the same document five times actually produces.
Preservation is measured against the **original** every time, not against the previous cycle, so a steady
decline cannot hide.

| Document | cycle 1 | 2 | 3 | 4 | 5 |
|---|---:|---:|---:|---:|---:|
| `char-formatting-mixed-runs.docx` | 100% | 100% | 100% | 100% | 100% |
| `court-filing-spacing.docx` | 100% | 100% | 100% | 100% | 100% |
| `nda-interrupted-clauses.docx` | 100% | 100% | 100% | 100% | 100% |

**Zero cumulative drift.**

**On paraId regeneration between cycles**: each render passes the body through `AssignParaIds`, which mints
ids for paragraphs lacking them and drops duplicates — so ids do move between cycles, exactly as Word's own
save does. It makes no difference, because the merge pairs by **document order** and treats `paraId` as
corroboration only. This is the empirical vindication of the project's "paraId is not a durable file key"
invariant: a design that keyed on it would drift here, and this one does not.

---

## Performance — NFR-07

Median of 15 runs after 5 warm-up passes (the first measurement pass showed a 52 ms mean that was **JIT
noise**; these are the honest numbers):

| Document | blocks | control | merged | delta | ratio |
|---|---:|---:|---:|---:|---:|
| `nda-interrupted-clauses.docx` | 13 | 2.6 ms | 4.7 ms | **+2.1 ms** | 1.80× |
| `AppligentNDA_Signed.docx` | 50 | 12.4 ms | 31.4 ms | **+19.0 ms** | 2.53× |
| `PAT 109270W-1 - CLAIMS…docx` | 109 | 8.0 ms | 27.2 ms | **+19.1 ms** | 3.38× |

**Within budget.** NFR-07 allows "one extra baseline projection + DOM clone per save", and that is precisely
what the delta is: `BuildContentModel(carrierBytes)` once, plus `CloneNode(true)` per cloned block. The
absolute cost — **+19 ms on the largest corpus document** — is immaterial next to the SPE round trip the same
save already performs.

---

## Spec §5.3, answered with data

| Question | Answer |
|---|---|
| Can a re-projected baseline serve as a reliable merge base? | **Yes.** 253 of 271 blocks matched their re-projection exactly and cloned; the 18 that did not were the 18 edited blocks. Zero false matches (the edit survived in every document), zero false mismatches. |
| Is document-order pairing sufficient without `paraId` as a key? | **Yes for edit-in-place; no for reorder.** 18/18 at 100% for in-place editing, including all three R4-breakers. A reordered body pairs nothing and degrades to R6. |
| Does clone-unchanged preserve constructs the model cannot represent? | **Yes, and it is the only thing that does.** Fields, content controls, text boxes, footnote references and every `pPr`/`rPr` property survive because they are never re-derived. |
| Does loss compound over repeated round trips? | **No.** Flat 100% over 5 cycles on three documents, through paraId regeneration each cycle. |
| Is the cost acceptable? | **Yes.** +2 to +19 ms per save, scaling with document size. |
| Does it need a new package (e.g. Clippit `WmlComparer`)? | **No.** Pure `DocumentFormat.OpenXml`. NFR-02 holds; no owner decision needed. |

---

## What the prototype does NOT establish

Stated plainly, because a 100% headline invites over-reading.

### 1. The EDITED block is still rebuilt from the lossy model — and that is not measured here

The oracle measures **untouched** blocks and deliberately excludes the edited one. The paragraph the user
actually typed in is still re-authored from a content model carrying justification, bold and italic —
so **it still loses its font, size, colour, indentation, spacing, tabs and numbering.**

For a user editing one paragraph of a forty-page contract this is a colossal improvement (one damaged
paragraph instead of forty pages). It is **not** "fidelity solved". **FR-A04 property inheritance
(task 041) is what closes it, and this prototype does not exercise it at all.** Any reading of "100%" that
does not carry this caveat is wrong.

### 2. Reorder delivers no benefit

Document-order pairing yields zero clones on a reordered body. Graceful, never a hard fail — but a user who
moves a section still loses that section's formatting. Input to task 040.

### 3. Cloned blocks do not advance `ListRenderState`

A rendered list item appearing after cloned list items computes run continuity against a state that never saw
them, and may restart numbering at 1. At most one block per save in this measurement, and the oracle excludes
it, so **this cannot have flattered any number above** — but task 040 must thread cloned list items through
the run bookkeeping.

### 4. Measured at the renderer, not through the wire

`ComposeService` chooses the carrier bytes (`ResolveSaveBaselineAsync`) and reports the outcome. Task 040
must confirm the baseline reaching the renderer in production is the one the client loaded from — a stale or
substituted carrier would clone the *wrong* blocks. Track S's concurrency work (`If-Match`, last-writer-wins)
is what makes that tractable, but it is not proven by this prototype.

### 5. `ComposeBaselineParaIdStamper` was not promoted

The task's step 1 anticipated promoting the stamper to the render path. **It proved unnecessary**: the merge
never resolves a `paraId`, so an unstamped baseline costs nothing. The stamper stays where it is. Recorded as
a deliberate deviation from the step list (`<steps mode="directional">`), not an omission — and it removes a
production change task 040 would otherwise have had to make.

---

## What task 040 must do differently

1. **Thread cloned list items through `ListRenderState`** (limitation 3).
2. **Consider paraId-corroborated pairing to recover moved blocks** — as a *fallback after* document-order
   pairing fails, never as a primary key. Duplicate-paraId documents must fall back to order, not mis-pair.
3. **Pair with FR-A04 property inheritance (041)** — without it the edited block is still destroyed, which is
   what the user sees.
4. **Verify carrier provenance end-to-end** (limitation 4).

## Input to tasks 043 and 045

| Case | Disposition |
|---|---|
| Heavy reorder | Not a capability-gate trigger — degrades to R6, never fails. **Residual loss list (045).** |
| The edited block's own formatting | **Task 041's** scope; if 041 cannot fully cover it, the residue belongs on the 045 list. |
| Text boxes, fields, content controls on **untouched** blocks | **No longer residual loss** — preserved by cloning. The ADR-049 accept-flatten warnings should fire only for blocks actually re-rendered. Task 044 should narrow the warning taxonomy accordingly, or users will be warned about losses that no longer occur. |

---

## Gate readiness

Against the MISS condition defined in advance in [`control-measurement.md`](control-measurement.md):

| # | Condition | Result |
|---|---|---|
| 1 | Near-tier < 100% at lenient on any document | **Not met** — 14/14 measurable documents at 100% |
| 2 | Overall < 95% at lenient corpus-wide | **Not met** — 100% |
| 3 | Any document classifies `fail` | **Not met** — none |
| 4 | Any outcome-honesty violation | **Not met** — none |
| 5 | Strict overall below the 12.18% control | **Not met** |

**No miss condition fired.** Neither escalation trigger fired: the prototype did not miss the threshold,
block pairing held on all three R4-breakers, and no new package was required.

**Task 031 has the evidence it needs to make the gate decision.** That decision, and the ADR-049 third
amendment, are 031's to make — not this task's.
