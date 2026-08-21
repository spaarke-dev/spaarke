# The gate contract — task 020 (FR-G01 / FR-G02 / FR-G03)

> **Built** 2026-08-21 · **Measured on** `origin/master` + Track S (commit `75ef9912f`)
> **Code**: [`ComposeBlockPreservationOracle.cs`](../../../tests/integration/seam/Compose/ComposeBlockPreservationOracle.cs) (engine) ·
> [`ComposeFidelityGateHarnessTests.cs`](../../../tests/integration/seam/Compose/ComposeFidelityGateHarnessTests.cs) (gate + self-proof)

---

## What changed, in one sentence

The release gate used to ask *"did the save crash?"*; it now asks *"what survived?"* — and the answer, on
today's code, is **6.53% of untouched blocks and 2.55% of the near tier**.

---

## Why the old gate could not see the defect

`ComposeFidelityGateHarnessTests` shipped in R6 with this sentence in its own header:

> ADR-049 (R6): byte-identity is NOT asserted on the save path — the gate asserts round-trip success +
> edit presence + warn-not-fail degradation instead.

That sentence *is* the hole. A save could rebuild all forty pages from a five-node editor view, drop every
tab stop, indent, style and section break, and the gate stayed green: the HTTP was 200 and the edit marker
came back. Three releases were validated by a gate that could not fail for the reason the product was
failing.

---

## The control — current master, 10 corpus documents, 245 comparable blocks

Each document is loaded, ONE paragraph is edited, saved through the real render-on-save wire path, and
every **other** direct `w:body` child is compared against its original.

| Document | outcome | overall | near tier | near-tier blocks | strict | paraId drift |
|---|---|---:|---:|---:|---:|---:|
| `01 - Test Matter Create Fields Only.docx` | `persisted` | 12.50% | 0.00% | 7 | 12.50% | 0 |
| `AppligentNDA_Signed.docx` | `persisted` | **2.04%** | 0.00% | 48 | 2.04% | 0 |
| `Engagement Letter.docx` | `persisted` | 8.33% | 0.00% | 11 | 8.33% | 0 |
| `PAT 109270W-1 - CLAIMS…docx` | `persisted` | **0.93%** | 0.00% | 107 | 0.93% | 0 |
| `heading-style-numbering.docx` | `persisted` | 54.55% | 50.00% | 10 | 27.27% | 6 |
| `line-numbered-pleading.docx` | `persisted` | 4.35% | 0.00% | 22 | 4.35% | 8 |
| `multi-author-redline-synthetic.docx` | `persisted` | 12.50% | 0.00% | 7 | 12.50% | 0 |
| `multilevel-1-1-1.docx` | `persisted` | 12.50% | 0.00% | 7 | 12.50% | 4 |
| `nda-interrupted-clauses.docx` | `persisted` | 16.67% | 9.09% | 11 | 16.67% | 0 |
| `symbol-section-mark.docx` | `persisted` | 16.67% | 0.00% | 5 | 16.67% | 3 |
| **CORPUS** | — | **6.53%** (16/245) | **2.55%** (6/235) | 235 | — | 21 |

Duplicate `w14:paraId` anywhere in the document: **1 of 10** (`AppligentNDA_Signed.docx`).

Machine-readable form: `fidelity-gate-result.json`, written next to the test assembly on every run. Task 023
publishes this as the formal control; tasks 030/031 assert a merge model against it.

> **Corpus extended the same day (tasks 021 + 022).** Eight fixtures were added — three R4-breakers and five
> near-tier families — and picked up by the gate with **zero code changes**. Post-extension the corpus is
> **18 documents / 271 comparable blocks: 8.86% overall, 2.37% near-tier**. Near-tier preservation *fell*,
> because the new fixtures add 18 near-tier-relevant blocks and preserve **none** of them — the bar got
> harder to clear because it now covers constructs the old corpus never reached. Task 023 should publish the
> 18-document figure as the formal control; the 10-document table above is the measurement task 020 built the
> instrument against. See `tests/fixtures/compose-corpus/corpus-manifest.md` §1.7–§1.8.
>
> The sharpest single reading in the whole corpus is `char-formatting-mixed-runs.docx`: **0% near-tier
> preservation with NO degradation warning at all**. The renderer does not know it dropped the formatting, so
> the user is never told. That is the silent-loss mode this project exists to close, isolated in one fixture.

### Three findings the numbers hand to Phase 3

1. **The loss is INSIDE blocks, not structural.** Every document's block count is stable (109 → 109,
   50 → 50, 9 → 9). Nothing is dropped or reordered; the renderer rewrites each paragraph's contents. That
   is the precise shape R8's merge model is designed for — **clone the untouched blocks verbatim** — and it
   means the fix does not have to solve a re-alignment problem it does not have.

2. **The near tier is where the loss lands.** The top difference paths across the corpus, by frequency:

   | count | path | what it is |
   |---:|---|---|
   | 48 | `p/pPr/spacing` | paragraph spacing |
   | 44 | `p/pPr` | paragraph properties wholesale |
   | 42 | `p/r\|pPr` | the renderer **inserted** a `w:pPr` where the original had a run |
   | 42 | `p/pPr/pStyle` | paragraph style |
   | 38 | `p/pPr/rPr` | paragraph-mark run properties |
   | 37 | `p/pPr/ind` | **indentation** |
   | 36 | `p/r/rPr` | character formatting |
   | 27 | `p/pPr/numPr\|pStyle` | numbering ↔ style substitution |
   | 8 | `p/pPr/tabs` | **tab stops** |

   This is the owner's dev banner, itemised: *"indentation, internal links, line breaks, paragraph styles,
   section breaks, tab stops and table formatting."* The banner was telling the truth.

3. **`heading-style-numbering.docx` is the only document above 50%, and it is the one with paraId drift**
   (54.55% lenient vs 27.27% strict). Where content survives, identity does not. Task 042 owns that.

4. **`AppligentNDA_Signed.docx` already carries duplicate `w14:paraId`s** — the [MS-DOCX] case, found by
   the full-subtree scan (a body-level-only scan reports `false` for it). The corpus therefore *already*
   contains one of the three R4-breakers task 021 was going to author from scratch; 021 should check
   coverage before synthesizing a duplicate.

### What the control says about Track S

All ten documents terminate in `persisted`, and **zero** carry an outcome-honesty violation. The save
contract Track S shipped holds across the whole corpus: nothing claims a write it did not make. Half B is
closed; this document is the measurement of Half A.

---

## FR-G03 — the two comparison levels

Both run the **same** engine and differ in exactly one bit.

| Level | `w14:paraId` / `w14:textId` | Detects |
|---|---|---|
| **Lenient** | normalized away | **Content loss** — a block that survived with a regenerated id is not lost |
| **Strict** | kept | **Identity drift** — the session anchors edit-capture depends on |

Strict is by construction harder than lenient: every lenient difference is also a strict one.

A third level, `StrictIgnoringRevisionIds`, is a **diagnostic and never a gate**. It answers "how much of
the measured loss is only `w:ins`/`w:del` renumbering?" — on the current corpus, essentially none
(107 vs 107 differences on the CIPO claims document), so revision-id churn is not inflating the numbers.

---

## FR-G03 — the normalization table

An oracle silently becomes wrong through normalization. Too lenient passes a broken merge model and ships
R9; too strict fails a correct one and re-opens a settled architecture. **Both errors are invisible — a
green suite looks identical either way.** So every entry below carries a justification, and the default for
anything *not* listed is that a difference **is** loss.

| # | Normalized | Justification — why this is not loss |
|---|---|---|
| 1 | `w:rsid*` (all `rsid`-prefixed attributes) | Word's revision-**save** identifiers. Regenerated on essentially every save, explicitly optional in the schema. Two files differing only in rsids render and print identically. |
| 2 | `w:proofErr` | Spell/grammar proofing markers. Transient editor state that Word rebuilds on open; they bracket text without changing it. |
| 3 | `@w:id` on `w:bookmarkStart` / `w:bookmarkEnd` | Arbitrary **local** handles pairing a start with its end. Only `@w:name` is semantic — and it is deliberately **kept**, so a dropped bookmark still registers as loss. |
| 4 | `w:numId` / `w:abstractNumId` values | Legitimately **remapped** when numbering definitions merge. Rewritten to a **first-appearance ordinal**, *not deleted* — deletion would make a list association that was dropped entirely read as preserved. Proved both ways by `Oracle_ToleratesNumberingRemap_ButStillSeesNumberingDropped`. |
| 5 | Attribute **order** | Not information. The writer chooses it; the canonical serializer sorts by (namespace, local name). |
| 6 | Namespace **prefixes** | `w:` vs `w1:` is a binding choice. The canonical serializer emits `{namespace-uri}local`, so the URI — the semantic part — is what is compared. |
| 7 | Whitespace **between** elements | Serialization/indentation artifact. Whitespace **inside** `w:t` / `w:delText` / `w:instrText` is document content and is untouched — leading and trailing spaces in a run are real, which is exactly why OOXML has `xml:space="preserve"`. |
| L | `w14:paraId` / `w14:textId` | **Lenient level only** — the level switch itself. |
| D | `@w:id` on `w:ins` / `w:del` | **Diagnostic level only** — never a gate level. |

### Deliberately NOT normalized

Each of these would have been a plausible-sounding way to make the numbers look better:

- **`w:rPr` / `w:pPr` content of any kind** — that is the near tier itself.
- **Empty paragraphs, `w:br`, `w:tab`** — the R3 empty-paragraph-drift defect lives exactly here.
- **`w:sectPr`** — section breaks are one of the losses the owner reports from dev. It is a direct
  `w:body` child and is counted as a block.
- **`@w:id` on `w:ins`/`w:del` at either gate level** — a writer that renumbers revision ids every save is
  a *finding* about the write path (task 042), not noise to erase. Surfaced via the diagnostic level per
  this task's second escalation trigger: an unjustifiable normalization gets reported, not adopted.

---

## FR-G01 — the near tier

A difference is **near tier** when any element on its path is one of:

`rPr` · `pPr` · `ind` · `tabs` / `tab` · `footnoteReference` / `footnoteRef` ·
`endnoteReference` / `endnoteRef` · `fldSimple` / `fldChar` / `instrText`

A block is **in play** for the near tier when the original carried one of those constructs **OR** the save
introduced a near-tier difference. The second half is load-bearing: on the current corpus the renderer
*adds* `w:pPr` to paragraphs that had none (42 occurrences of the `p/r|pPr` path), and keying relevance off
the original alone would let invented formatting escape the tier entirely.

### `null` is not `100`

An earlier draft returned `100%` for an empty denominator. The first corpus run promptly produced three
documents reading **"near tier: 100%" on a denominator of zero** — documents the oracle had not measured,
presenting as perfect. Both percentages are now `double?`, `null` when nothing was measured, with paired
`overallMeasured` / `nearTierMeasured` flags in the JSON. The Phase-3 gate reads these numbers to decide an
architecture; *"not measured"* and *"measured, nothing lost"* must never be the same value.

This is the same class of error the task's escalation trigger names, caught in the instrument rather than in
the corpus.

---

## Block pairing

- **Direct `w:body` children, in document order.** Never `body.Descendants<Paragraph>()` — descendant
  enumeration interleaves `w:txbxContent` paragraphs (how Word writes every text box, via
  `mc:AlternateContent`) into the body sequence, mis-pairing every block after the first text box and
  manufacturing loss that is not there. Proved by `Oracle_TreatsTextBoxContentAsOpaque_NotAsBodyBlocks`.
- **`paraId` corroborates; it never pairs.** It is not a durable file key — [MS-DOCX] permits duplicates
  across `mc:AlternateContent` and Word regenerates ids on save. Mismatches are counted and reported
  (`paraIdCorroborationMismatchCount`) so identity drift stays visible **even at the lenient level**, where
  the id is normalized out of the comparison.
- **Duplicate-paraId documents are flagged** (`duplicateParaIdsInOriginal` / `…InSaved`) so a reader knows
  the pairing rests on document order alone.
- **Unpaired blocks are reported separately** from the percentage. A dropped block also mis-pairs
  everything after it; the percentage and the unpaired count must be read together.

---

## FR-G02 — outcome honesty (the one thing that IS asserted)

Every corpus save must terminate in a **defined** member of task 013's closed `ComposeSaveOutcomes` set,
and the claim is cross-checked against ground truth the client cannot see — whether bytes actually reached
the SPE facade boundary:

| Reported | Bytes at storage | Verdict |
|---|---|---|
| `persisted` · `persisted-with-warnings` · `partially-recorded` | no | **FAIL** — success with nothing stored (FR-S06) |
| `refused-*` · `storage-failed` | yes | **FAIL** — a refusal that overwrote the stored document. Worse than a failure: the user is told their original is untouched. |
| no `outcome` field at all | — | **FAIL** — the client has only the HTTP status, which is the pre-FR-S06 failure mode |

The defined-outcome list is written out explicitly rather than reflected off the enum, so that **adding** a
member is a deliberate edit here too: an outcome nobody taught the gate about is exactly the "undefined
outcome" FR-S06 forbids, and it should surface as a failing assertion rather than be auto-accepted.

---

## Why the preservation numbers are NOT asserted

This harness must run **green on master**. It is the *control* — a red gate before a fix exists measures
nothing, it just fails. Thresholds (100% near tier / ≥95% overall, zero hard-fails) are asserted at tasks
030/031, against the numbers above.

The one thing that could make a measuring gate worthless is an oracle that reads 100% because it normalized
the signal away, and such an oracle is indistinguishable from a working one at a glance. So the harness
carries eight `Oracle_*` facts that prove the instrument:

| Fact | Pins |
|---|---|
| `Oracle_SeesANearTierLoss_DroppedIndentation` | a dropped `w:ind` is SEEN, at both levels, and classified near-tier |
| `Oracle_IgnoresRsidOnlyDifference_…` | an rsid-only difference is NOT loss |
| `Oracle_TwoLevelsDifferExactlyOnParaId_…` | lenient 100% / strict 50% on the same pair — the levels genuinely differ, and drift stays visible in both |
| `Oracle_ToleratesNumberingRemap_ButStillSeesNumberingDropped` | remap tolerated, removal caught |
| `Oracle_TreatsTextBoxContentAsOpaque_NotAsBodyBlocks` | 4 descendant paragraphs → 2 body blocks |
| `Oracle_ReportsDuplicateParaIdsDistinctly_…` | duplicate paraIds flagged, not silently mis-paired |
| `Oracle_ReportsBlockCountDrift_WhenTheSaveDropsAParagraph` | a dropped block reports as unpaired |
| `Gate_CorpusEnumerationIsDynamic_…` | `[MemberData]` is the live directory glob, never a hand-maintained list |

All eight are synthetic and in-memory. Nothing was added to the corpus: they prove the instrument, they are
not documents under measurement.

---

## What tasks 021 / 022 have to do

**Nothing in code.** `[MemberData]` enumerates the corpus directory at test-discovery time, so a `.docx`
dropped into `tests/fixtures/compose-corpus/` is measured by this gate with zero `.cs` changes — asserted by
`Gate_CorpusEnumerationIsDynamic_NewDocumentNeedsZeroCodeChanges`.

---

## Component justification (CLAUDE.md §11)

| Question | Answer |
|---|---|
| **Existing** | `ComposeOoxmlPackagePartComparer` — compares whole OPC package parts for byte-identity. Verified by reading it, not assumed. |
| **Extension** | Cannot be extended into this. It answers a binary question at a different unit, and its `IsStructurallyFaithful` uses the `body.Descendants<Paragraph>()` walk this task forbids. Widening it would fuse "package integrity" with "per-block preservation" — two independent reasons to change. Left untouched; it still serves the no-op byte-diff suite. |
| **Cost of doing nothing** | The Phase-3 gate has no oracle, the corpus cannot pick the architecture, and R9 follows. |

**No second harness, locator, or corpus was created** — one gate `[Theory]`, one
`ComposeCorpusFixtureLocator`, one `tests/fixtures/compose-corpus/`, one comparison engine. The oracle is a
sibling helper in the same directory, the same relationship the locator and the part comparer already have
to the harness that drives them.
