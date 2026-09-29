# The never-silent hole: an edited block with no base counterpart

> **Task 047b** (`spaarkeai-compose-r8`, FR-A10) · decided + measured 2026-08-26
> Reported by task 056 §7, owner decision 2026-08-25: *fix this in R8.*
>
> Code: `ComposeBlockMerge.Align` (the traceback tie-break) · `ComposeBlockMerge.CarryUnmodeledConstructs` /
> `WarnForConstructsLostOnThisBlock`
> Tests: `ComposeMergeSeamTests` §7 (four) · the `pictTextBoxTwin` row in `ComposeResidualLossParityTests`
> Document: `docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md` §5 *"The hole under the list"*

---

## 1. Why this outranked its size

The residual-loss list was signed by the owner on 2026-08-25. A sign-off on a list of losses is worth
exactly what one sentence in it is worth: *"Each loss is reported by name on the save; none is silent."*
Editing block 1 of `interior-text-boxes.docx` dropped a `w:pict` and said **nothing**, while editing block 2
reported `complex-object-dropped` correctly. That does not extend the list — it invalidates it, because a
list that under-reports is trusted and a list nobody trusts is at least harmless.

## 2. Root cause — an ambiguous alignment, not a missing warning

`ComposeBlockMerge.Plan` aligns the posted model against the re-projected baseline by longest common
subsequence over a canonical per-block key, then fills each gap between matches by pairing leftover posted
blocks against leftover base blocks positionally. The gap-fill is what gives an EDITED block its base
counterpart, and everything downstream hangs off that counterpart: `InheritProperties`,
`CarryUnmodeledConstructs`, and `WarnForConstructsLostOnThisBlock`, which reports a loss by **diffing the
render against its base**. No base ⇒ no diff ⇒ no report.

`interior-text-boxes.docx` has four body blocks. Blocks 1 and 2 are separate VML text boxes carrying the
same two lines; the box's prose is accept-flattened into the paragraph and the shape's own markup is not
carried, so the two blocks project to **byte-identical** models. The LCS is therefore ambiguous: two
maximum-length alignments exist. The traceback resolved the tie with `dp[x+1, y] >= dp[x, y+1] → x++` — on a
tie, skip the POSTED block — which produced:

```
posted 0 -> Clone base 0
posted 1 -> Render base -1     <-- the edited block, no counterpart
posted 2 -> Clone base 1       <-- the untouched twin, cloned from the WRONG base
posted 3 -> Clone base 3
                base 2 stranded, never written
```

**The pairing was ambiguous, not absent.** That is the whole reason the obvious fix is wrong (§3).

### It was never only a reporting bug

The same mis-pairing wrote the wrong bytes. The saved package contained `v:shape` id `_x0000_s1027` (block
1's box) at output position 2 and no `_x0000_s1027_1` at all: the untouched twin was replaced by a copy of
its neighbour. **ADR-049 invariant 2 — untouched blocks are preserved — breached by a clone.** The remark on
`Plan` said this could not happen, in as many words: *"LCS pairs only blocks that are already EQUIVALENT, so
a mis-pairing is harmless by construction: two equivalent blocks clone to the same output."* Equal MODEL keys
are not equal OOXML — the model carries `w:jc`, `w:b`, `w:i` and little else — and that comment is why nobody
looked. Corrected in place, along with the same claim in `merge-mechanism-results.md` R3 and a footnote on
`merge-integrity-results.md` row 4, whose "duplicate paraIds cannot mis-clone" was true and adjacent enough
to be read as covering this. Duplicate **ids** were the hazard everyone was watching; duplicate **content**
was the one that bit.

## 3. The decision: distinguish by REPAIRING the pairing, not by classifying the unpaired

The POML's escalation trigger asks what to do if new content and an unidentifiable base cannot be told apart
without a guess. They can — but not by inspecting the unpaired block, and that is the substance of this task.

**Why "warn whenever a block is unpaired" is wrong.** An unpaired block is also exactly what a paragraph the
user just typed looks like. Warning on those puts a degradation notice on every insertion, which is how R7's
banner became something users learned to dismiss. Silence there is *correct*: a new block had no base, so it
lost nothing.

**Why a vague warning is wrong even where the base IS missing.** The guarantee is that a loss is reported
**by name** — `complex-object-dropped`, `unrepresented-footnote-reference`. Naming the construct requires
knowing the base. "Something may have been lost from this paragraph" satisfies nobody and is unactionable.
So any fix that reports without identifying the base fails the acceptance criterion it is trying to meet.

**What separates the two cases is arithmetic, not intent.** Within a gap, if the posted side genuinely holds
more blocks than the base side, the surplus is content that was ADDED — it has no base because there is none
to have. The failure shape is different and unambiguous: a posted block left unpaired **while a base block
goes unused**. There the base existed; the alignment simply failed to bind to it. So the fix is to stop
producing that shape, after which the existing report names the construct exactly as it already does one
block over.

**The trigger did not fire.** No guess was required.

### The change: one branch

`Align`'s traceback now tracks the current gap's two halves — posted and base blocks skipped since the last
match — and resolves a tie by feeding the gap a base block unless it already has spare ones:

```csharp
if (skippingPosted > skippingBase || (skippingPosted == skippingBase && gapBase > gapPosted)) { x++; gapPosted++; }
else                                                                                          { y++; gapBase++; }
```

A tie means the LCS is genuinely indifferent — both moves yield a maximum-length alignment — so the choice is
free, and the two options are not equally good. Skipping a BASE block leaves it in the gap where the
positional fill hands it to the next unmatched posted block. Skipping a POSTED block puts a block into a gap
that may hold no base at all.

This is not a heuristic about user intent. It makes the traceback agree with the pairing rule `FillGap`
**already applies**: an unmatched posted block sitting beside an unmatched base block IS the edit of it
(FR-A04). The tie is the only branch that changed; where the DP has a strict preference it is obeyed exactly,
so any alignment without duplicate keys is untouched.

### Alternatives considered and rejected

| Option | Rejected because |
|---|---|
| Warn on every unpaired block | False positive on every inserted paragraph — the R7 banner failure mode, and the constraint the POML states outright |
| Report a generic "possible loss" when a base is stranded | Cannot name the construct, which is the guarantee. Also fires on legitimate delete-plus-insert |
| Repair the PLAN after the fact (re-point unpaired steps at stranded bases) | Provably impossible here: pairs must stay monotone in base index, and the stranded base sat on the far side of a match. Verified by hand-tracing the fixture before writing code |
| Slide matches forward among equal keys until each gap is satisfied | Equivalent outcome in the failing case, but it cascades: satisfying gap *i* can starve gap *i+1*, so a correct version needs a global objective. More machinery for the same answer |
| Flip the tie-break unconditionally to `y++` | Fixes this case, but changes the binding on ties where the gap already has spare base blocks — behaviour churn with no defect behind it |

## 4. The audit (POML step 4) — one more silent path, closed; two recorded

Every route by which a rendered block reaches the save without a loss report:

| # | Path | Finding | Disposition |
|---|---|---|---|
| 1 | Gap-fill leaves a posted block unpaired **while a base is stranded** | The reported defect | **Fixed** — §3 |
| 2 | Gap genuinely holds more posted blocks than base blocks | Correct silence: the block is new and lost nothing | Working as intended; asserted by `Merge_ParagraphInsertedBesideAnIdenticalTwin_ReportsNoConstructLoss` |
| 3 | **A block that renders NOTHING** | `RenderBlocks` appends one child per block for every shape except one: a `Table` block whose model carries no rows is skipped. `CarryUnmodeledConstructs` then returned early — before the report — because there was no output element to inspect. An entire block left the document in silence | **Fixed.** "Nothing was written" is a countable output — zero of everything. Observed failing first |
| 4 | `Capture` returns null (`BaselineUnavailable`) — empty body, or the baseline will not re-project | The save falls back to R6: the whole body is rebuilt from the model with no base side at all, so no block gets a carry or a report | **Recorded, not fixed** — see below |
| 5 | `Plan` stands down (`BaselineUnaligned`) — the base model and base element counts disagree | Same outcome as 4, by a different route. Standing down is correct: without an established correspondence, a report would be diffed against the wrong base | **Recorded, not fixed** — see below |
| 6 | Alignment degraded (DP table > 4M cells) | Matches are positional-on-equality, but the gap fill still pairs leftovers, so blocks keep their counterparts | No action |
| 7 | LCS cannot see a MOVED block (matches never cross) | A relocated block can be unpaired with its base stranded, so its unmodeled constructs would go unreported | **Recorded** — see below |

**Why 4 and 5 are recorded rather than fixed.** They are a different failure *class*, not a wider instance
of this one. When the merge stands down, the loss is not a per-construct residual in one edited paragraph —
it is R6's whole-document rebuild, which the residual list's scope rule (§1: *loss is per-edited-block*) does
not describe at all. The honest signal is a document-level notice, and that means a new degradation code, a
new client copy entry and a new banner state — a new warning surface, which this project's CLAUDE.md
forbids ("do not create a new degradation-copy layer") and which is far outside 047b. Measured reachability:
**zero** across all 24 corpus documents. Both are already counted on `ComposeMergeStats`; what is missing is
a consumer. Recommend a scoped follow-up rather than an in-flight expansion here.

**Why 7 is recorded rather than fixed.** The move limitation is documented in ADR-049 as a deliberate
property of LCS. Key equality could in principle identify a stranded base as "this block, relocated", but
using it would mean binding a pair that crosses the match order — the one thing the alignment's correctness
argument rests on. Unreached on the corpus after the fix (0 of 294). Building for it now would be machinery
with no measured failure behind it (root CLAUDE.md §11).

## 5. Measured

All numbers from this worktree, 2026-08-26.

**The defect, corpus-wide.** Every corpus document, every block position, a single-block edit at each —
294 scenarios over 24 documents. A single-block edit adds and removes no block, so by arithmetic every
posted block has a base; a plan that says otherwise has stranded one.

| | Posted blocks with no base counterpart | Where |
|---|---|---|
| Before | **5** | `AppligentNDA_Signed.docx` @6, @31, @32, @40 · `interior-text-boxes.docx` @1 |
| After | **0** | — |

Four of the five are in a **real signed NDA**, on consecutive empty paragraphs — the commonest
duplicate-key shape there is, and the reason this was never a fixture curiosity.

**The fixture, through the real renderer** (`interior-text-boxes.docx`, edit at block 1):

| | Plan for the edited block | Codes reported | `v:shape` ids in the saved body |
|---|---|---|---|
| Before | `Render base=-1` | *(none)* | `_x0000_s1027` — the edited block's box, cloned into the twin's position |
| After | `Render base=1` | `complex-object-dropped` | `_x0000_s1027_1` — the twin's own |

**The fidelity gate, before and after.** Byte-for-byte identical per document: 24 documents, **100.00%**
lenient overall (294/294 blocks), **100.00%** near tier (215/215), **0** fails. The gate happens to edit
block 0 of `interior-text-boxes.docx`, which is why it never saw this — an argument for sweeping every
position, which the new corpus test now does.

**Test suite.** `dotnet test tests/unit/Sprk.Bff.Api.Tests/` — 11,283 passed / 0 failed / 97 skipped
(baseline 11,277; +6 = five new seam tests + one new parity family row). `Spaarke.ArchTests` 62/62.

**Publish.** 45.03 MB compressed (pwsh 7) · 215 files · 4 `.pdb` · 137.41 MB raw directory sum — identical
to baseline in every one of the four figures. No package change; `dotnet list package --vulnerable
--include-transitive` reports no new HIGH.

## 6. Tests, and what each one would have caught

All observed failing before the fix, then passing.

| Test | Fails on |
|---|---|
| `Merge_EditedBlockWithAnIdenticallyProjectingTwin_ReportsTheConstructItLost` | The silence itself: `noCounterpart=1 codes=(none)` |
| `Merge_EditedBlockWithAnIdenticallyProjectingTwin_ClonesTheTwinsOwnBytes` | The invariant-2 half: the twin cloned from the wrong base |
| `Merge_SingleBlockEdit_NeverLeavesAPostedBlockWithoutItsBase` | The generalisation — all 24 documents at every position, not the one the gate edits |
| `Merge_BlockThatRendersNothing_StillReportsWhatItsBaseHeld` | Audit finding 3 |
| `ComposeResidualLossParityTests` row `pictTextBoxTwin` | The published list's promise, at the block position where it was actually being broken |
| `Merge_ParagraphInsertedBesideAnIdenticalTwin_ReportsNoConstructLoss` | **Passes before and after** — the control. It is what proves the fix did not buy honesty with noise |

The parity row is the one that matters longest. Every other row in that table sits in a document whose three
blocks all read differently, so the alignment is unambiguous and the report always has a base to work from. A
parity check that only ever measures unambiguous documents cannot see this class of failure at all — which
is precisely how it survived four previous runs of a check built to catch exactly this.

## 7. Proposed `.claude/CHANGELOG.md` entry

Sub-agents cannot write under `.claude/`, so the text is offered here for the main session:

```
### 2026-08-26 — Compose never-silent guarantee repaired (task 047b, spaarkeai-compose-r8)

- `ComposeBlockMerge.Align`: the LCS traceback's tie-break now balances the current gap instead of always
  skipping the posted block. On documents with two identically-projecting blocks (consecutive empty
  paragraphs, repeated signature lines, duplicate callouts) the old tie-break left the EDITED block with no
  base counterpart — no property inheritance, no construct carry, and no loss report — and cloned the
  untouched twin from the wrong base (ADR-049 invariant 2). Corpus: 5 unpaired blocks -> 0 across 294
  single-block-edit scenarios; fidelity gate unchanged at 100%/100%/0 fails.
- `ComposeBlockMerge.CarryUnmodeledConstructs`: a block that renders NOTHING (a Table block with no rows)
  now reports what its base held instead of returning before the report.
- `docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md` §5 gains "The hole under the list". No row changed —
  the signed list is intact; the promise underneath it was repaired.
```
