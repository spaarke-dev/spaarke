# ADR-049 — Third Amendment (APPLIED)

> ## ✅ APPLIED — 2026-08-21
>
> **Status**: **ACCEPTED and WRITTEN.** Nothing further is pending on this document; it is now the
> drafting record, not a work item.
>
> **Owner sign-off**, verbatim: *"ADR-049 is fine."* — 2026-08-21, in response to the Sign-off Required
> block at the end of this document. Recorded here rather than left in conversation, because an approval
> that lives only in chat scrollback is an approval that gets lost.
>
> **Drafted** 2026-08-21 by task 031 on the evidence of [`gate-decision.md`](gate-decision.md).
> **Applied** 2026-08-21 at the start of task 040 (not deferred to 045) — task 031's constraint reads
> "ready to merge **with or before** task 045", and while the write was outstanding ADR-049 still told a
> reader that *"render-on-save supersedes surgical byte-patch"*, the exact guidance that produced the defect
> 040 exists to fix.
>
> ### Where it landed
>
> | Target | Content | Note |
> |---|---|---|
> | [`.claude/adr/ADR-049-compose-shadow-document.md`](../../../.claude/adr/ADR-049-compose-shadow-document.md) | the **CONCISE** section below | new "R8 Path-B Amendment" section + status line + footer |
> | [`docs/adr/ADR-049-compose-shadow-document.md`](../../../docs/adr/ADR-049-compose-shadow-document.md) | the **FULL** section below | NEW file — the `docs/adr/` twin the concise ADR said did not exist |
> | [`.claude/adr/INDEX.md`](../../../.claude/adr/INDEX.md) | — | the 049 row still described R4's surgical byte-patch as the save contract (never updated for R6 either); rewritten |
> | [`docs/adr/INDEX.md`](../../../docs/adr/INDEX.md) | — | ADR-049 had **no row at all**; added to the main + domain tables |
> | root [`CLAUDE.md`](../../../CLAUDE.md) §17 Compose row | — | same R4 staleness, loaded **every session** — the highest-traffic copy of the superseded rule |
> | [`.claude/CHANGELOG.md`](../../../.claude/CHANGELOG.md) | — | `[Unreleased]` entry per §18 |
>
> The three index/pointer surfaces were **not** in the original two-target plan. They were found stale in
> the same way and by the same amount, and leaving them would have preserved the superseded rule in the
> places an agent is most likely to actually read.

---

## Why a third amendment

ADR-049 carries two prior Path-B amendments. **Each is partly right, and neither is correct as written.**

| Amendment | Said | Was right that | Was wrong that |
|---|---|---|---|
| **R4 (I-4)** | "Untouched XML subtrees are byte-identical" | Untouched content must survive | Achieving it by surgical byte-patch — anchors could not be resolved, producing the HTTP 422 treadmill |
| **R6** | "Render-on-save supersedes surgical byte-patch" | Rendering from a model removes the 422 class entirely | Rendering the **whole body** — the base side was dropped, and with it every property the model does not carry |

The pendulum swung twice because each amendment **traded one requirement for the other**: R4 took
preservation and lost termination; R6 took termination and lost preservation. Both are non-negotiable, and
neither prior amendment said so.

**The third amendment reconciles them rather than choosing between them**: the save still renders from the
model (R6 holds) **and** untouched content is preserved (I-4's intent holds), by re-projecting the retained
baseline server-side and copying through the blocks the user did not change.

**Measured, not argued** (task 030, 18-document corpus): overall preservation **18.08% → 100%**, near-tier
**6.67% → 100%**, zero hard-fails, zero cumulative drift over 5 round trips, +2–19 ms per save, no new
package.

---

## CONCISE (for `.claude/adr/ADR-049-compose-shadow-document.md`)

### Third amendment (R8, 2026-08-21) — base re-projection + block copy-through

**The save renders from the model AND preserves untouched content.** These are not alternatives.

At save time the renderer re-projects the retained baseline server-side, pairs its blocks against the posted
model **by document order**, and:

- **unchanged block** → the baseline's own `w:p` subtree is **cloned verbatim**, with zero property logic;
- **changed block** → rendered from the model, with property inheritance from its baseline counterpart;
- **unmergeable block** → thin render **+ warning**. Never a content refusal.

There is no per-construct preservation logic and there must not be. Properties survive because an untouched
block is **never re-derived** — preservation is a consequence of not rewriting, not a feature list.

#### The seven standing invariants

1. **Every save terminates in a defined outcome** — never an undefined content refusal.
2. **Untouched blocks are preserved.**
3. **The projection is the only coordinate system** — nothing else independently resolves document positions.
4. **`paraId` is a hint in the *file*, authoritative within a *session*.** Duplicates are spec-legal across
   `mc:AlternateContent`; Word regenerates ids on save. Pair by document order; `paraId` corroborates, never keys.
5. **Concurrency is last-writer-wins with a warning**, enforced by `If-Match` at the storage boundary.
6. **One edit-capture mechanism** — keystroke or model, the same anchor capture and rebasing.
7. **Deterministic information available at capture time MUST be carried, not re-derived.**

#### The paired MUST (load-bearing — do not restate singly)

> **Invariants (1) and (2) are a PAIR. No future amendment may trade one away to obtain the other.**
> An amendment that improves termination at the cost of preservation, or preservation at the cost of
> termination, is rejected **by this rule alone**, regardless of its other merits.

Both prior amendments made exactly that trade. This clause exists so a fourth cannot.

#### On invariant (7)

Stated as a **general rule**, not per surface. It is the rule beneath three of R8's four root causes: R6's
thin content model re-derived formatting it had been handed; the AI edit contract re-derived a location it
had already captured; and the demand for a fuzzy matcher was a consequence of the second. **If a design
re-derives something it already had, that is the bug** — and naming it once, generally, is how it stops
being rediscovered per surface.

---

## FULL (for `docs/adr/`)

### Context

Compose reached its eighth release with two standing failures: users could not reliably save, and saves that
landed silently destroyed Word formatting. The second is this amendment's subject.

The mechanism was never in doubt once measured. `ComposeDocumentRenderer.RenderIntoCarrier` calls
`body.RemoveAllChildren()` and rebuilds the body from `ComposeContentModel` — which carries justification,
bold and italic and essentially nothing else. Everything else in `w:pPr` and `w:rPr` is discarded at
projection time, before the renderer runs.

Task 023 measured it on an 18-document corpus, one paragraph edited per document: **18.08%** of untouched
blocks survived, **6.67%** of the near tier. On a real 109-block patent claims document, **one block** survived.

### Decision

Add the **base side** the render path never had. Not a return to R4's surgical patching, and not a defence
of R6's whole-body rebuild — the third position both prior amendments missed.

#### Mechanism (normative)

At save, inside the single body author (`ComposeDocumentRenderer`, ADR-049 I-5):

1. **Capture** the retained baseline's direct `w:body` children before the swap. Direct children only —
   never `body.Descendants<Paragraph>()`, which interleaves `w:txbxContent` paragraphs into the body
   sequence and mis-pairs every block after the first text box. `mc:AlternateContent`, `w:txbxContent`,
   `mc:Choice` and `mc:Fallback` are **opaque**: carried whole, never entered.
2. **Re-project** the baseline server-side. "Unchanged" MUST be decided against a fresh re-projection —
   **never raw text, never the client's copy**. Base and posted then become two values of the same type from
   the same builder, and their comparison is total.
3. **Pair by document order.** `paraId` corroborates and is reported on mismatch; it is never a key
   (invariant 4).
4. **Dispatch per block**: identical → clone verbatim; different → render with property inheritance;
   unmergeable → thin render + warning, **never a refusal** (invariant 1).

#### Consequences

**Positive.** Untouched blocks preserve every construct, including ones the model cannot represent
(fields, content controls, text boxes, footnote references) — with no per-family code. The R4-breakers are
neutralised structurally: duplicate `paraId` cannot mis-pair a merge that does not key on it, and opaque
regions are never entered. No new package. Cost is one extra baseline projection plus a DOM clone per save.

**Negative, and accepted.**

- **The edited block is still rebuilt from the model.** Property inheritance (FR-A04) narrows this; it does
  not eliminate it. The residual belongs on the published loss list.
- **Reorder yields no benefit.** Document-order pairing cannot recognise a moved block; a reordered body
  degrades to R6's behaviour — never a failure, but no preservation.
- **Cost scales with document size** (+19 ms on a 109-block document), immaterial next to the storage round
  trip the same save performs.

**Neutral.** `ComposeBaselineParaIdStamper` needs no promotion to the render path: the merge never resolves
a `paraId`. **`ComposeShadowPatchEngine` is NOT confirmed as subsumed** — it serves the op-log path, which
this amendment does not touch. See `gate-decision.md` §5.

### Alternatives considered and rejected

| Alternative | Rejected because |
|---|---|
| Keep R6 unchanged; widen the content model to carry every property | An open-ended feature list that is never finished, and every property added is another that can be dropped. Cloning preserves properties nobody enumerated. |
| Return to R4 surgical byte-patching | Reinstates the 422 treadmill — the failure R6 correctly removed, and a direct violation of invariant 1. |
| Adopt Clippit's `WmlComparer` | Forbidden by NFR-02 (no new NuGet) and unnecessary — the measured result needs only `DocumentFormat.OpenXml`. |
| Compare blocks by text equality | Two paragraphs with identical text can differ in formatting, list level, comment anchors or revision state. A text shortcut clones a block the user *did* change, silently discarding their edit — worse than the defect being fixed. |
| Pair blocks by `paraId` | Duplicates are spec-legal and Word regenerates ids on save. This mis-binds on precisely the documents the project exists to survive. |

### Compliance

Supersedes the R6 amendment's implication that whole-body rendering is the save contract. Restores R4's I-4
**intent** — untouched content survives — without the anchor-resolution mechanism that made I-4 unachievable.
ADR-049 I-5 (one body author) is unchanged and reinforced: the merge lives inside the renderer.

### Evidence

`projects/spaarkeai-compose-r8/notes/` — [`control-measurement.md`](control-measurement.md) (the control and
its per-loss classification), [`merge-prototype-results.md`](merge-prototype-results.md) (the prototype),
[`gate-decision.md`](gate-decision.md) (the gate call), [`gate-contract.md`](gate-contract.md) (the oracle
and its normalization justifications).

---

## Sign-off required (CLAUDE.md §6.5 Path B)

| | |
|---|---|
| **ADR** | ADR-049 — Compose Shadow Document |
| **Rule being amended** | The R6 amendment ("render-on-save supersedes surgical byte-patch") and R4's I-4 |
| **Conflict** | Each prior amendment secured one of two non-negotiable properties by surrendering the other |
| **Path** | **B — amendment** |
| **Rationale** | A third mechanism satisfies both. Measured on the corpus, not argued: 18.08% → 100%. |
| **Impact if accepted** | ADR-049 gains a third amendment + seven standing invariants + the paired-MUST rule; Phase 4 implements against it |
| **Alternative considered and rejected** | Path A (project-scoped exception) — wrong instrument: this is not a narrow deviation, it is a correction to the governing decision, and leaving the ADR as-is would let a future project re-derive R6's mistake from a still-authoritative rule |

**Owner action**: ~~accept, revise, or reject~~ → **ACCEPTED 2026-08-21** (*"ADR-049 is fine."*)
→ **APPLIED 2026-08-21** at the start of task 040. See the APPLIED banner at the top of this
document for every file the write touched.
