# ADR-049: Compose Shadow Document — Extended Record

> **Canonical ADR**: [`.claude/adr/ADR-049-compose-shadow-document.md`](../../.claude/adr/ADR-049-compose-shadow-document.md).
> That file carries the Decision, the locked decisions D1–D5, the MUST / MUST NOT constraints, the R4.5
> read/reference invariants F-1…F-5, and all three amendments in concise form. **Load that one per task.**
>
> **What this file is**: the extended reasoning for the **R8 third amendment** (2026-08-21) — context,
> mechanism, consequences, rejected alternatives, and the evidence the decision rests on. It is the
> `docs/adr/` twin the concise ADR previously said did not exist yet.
>
> **What this file is not**: a second copy of ADR-049. It does not restate R4's original decision or the R6
> amendment; their full reasoning lives in the project folders named under **Evidence** below. Two long
> documents saying the same thing drift; this one deliberately covers only what the concise ADR compresses.

| | |
|---|---|
| **Status** | **Accepted** — owner sign-off 2026-08-21 (*"ADR-049 is fine."*) |
| **Path** | **B — ADR amendment** (root [`CLAUDE.md` §6.5](../../CLAUDE.md)) |
| **Author** | `spaarkeai-compose-r8` task 031, on the evidence of the Phase-3 architecture gate |
| **Applied** | Start of task 040, so that no Phase-4 code is written against the superseded rule |
| **Scope** | The Compose **write/save path only.** R4.5's F-1…F-5 and I-7 are untouched. |

---

## Why a third amendment

ADR-049 already carried two Path-B amendments. **Each is partly right, and neither is correct as written.**

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

---

## Context

Compose reached its eighth release with two standing failures: users could not reliably save, and saves that
landed silently destroyed Word formatting. The second is this amendment's subject.

The mechanism was never in doubt once measured. `ComposeDocumentRenderer.RenderIntoCarrier` calls
`body.RemoveAllChildren()` and rebuilds the body from `ComposeContentModel` — which carries justification,
bold and italic and essentially nothing else. Everything else in `w:pPr` and `w:rPr` is discarded at
**projection** time, before the renderer runs.

Task 023 measured it on an 18-document corpus, one paragraph edited per document: **18.08%** of untouched
blocks survived, **6.67%** of the near tier. On a real 109-block patent-claims document, **one block**
survived.

That measurement is the reason this amendment exists in this shape. The 82% was not visible from the code —
it required an oracle that could compare a saved package against its baseline block by block, and a control
arm run through the same instrument.

---

## Decision

Add the **base side** the render path never had. Not a return to R4's surgical patching, and not a defence
of R6's whole-body rebuild — the third position both prior amendments missed.

### Mechanism (normative)

At save, inside the single body author (`ComposeDocumentRenderer`, ADR-049 **I-5**):

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

### The paired MUST

> **Invariants (1) — every save terminates in a defined outcome — and (2) — untouched blocks are preserved —
> are a PAIR. No future amendment may trade one away to obtain the other.**

An amendment that improves termination at the cost of preservation, or preservation at the cost of
termination, is rejected **by this rule alone**, regardless of its other merits. Both prior amendments made
exactly that trade. This clause exists so a fourth cannot.

---

## Consequences

### Positive

Untouched blocks preserve every construct, including ones the content model cannot represent — fields,
content controls, text boxes, footnote references — with **no per-family code**. The R4-breakers are
neutralised structurally rather than defended against: a duplicate `paraId` cannot mis-pair a merge that does
not key on `paraId`, and opaque regions are never entered because they are never walked into. No new package;
the cost is one extra baseline projection plus a DOM clone per save.

The property that matters most is negative: **there is no per-construct preservation logic, and there must
not be.** Preservation is a consequence of not rewriting. A feature list of preserved constructs is never
finished, and every entry on it is another thing that can be dropped.

### Negative, and accepted

- **The edited block is still rebuilt from the model.** Property inheritance (FR-A04, task 041) narrows this;
  it does not eliminate it. The residual belongs on the published loss list. **041 is not optional and not
  deferrable** — without it the user still watches the paragraph they typed in lose its formatting, which is
  what they report as "it destroyed my document".
- **Reorder yields no benefit.** Document-order pairing cannot recognise a moved block; a reordered body
  degrades to R6's behaviour — never a failure, but no preservation.
- **Cost scales with document size** (+19 ms on a 109-block document), immaterial next to the storage round
  trip the same save performs.

### Neutral

`ComposeBaselineParaIdStamper` needs no promotion to the render path: the merge never resolves a `paraId`.

**`ComposeShadowPatchEngine` is NOT confirmed as subsumed.** It serves the op-log path, which this amendment
does not touch, and all three live call sites are on that path. "Probably subsumed" does not authorize
deleting 3,000 lines. See `gate-decision.md` §5 for the three pieces of evidence a retirement would need.

---

## Alternatives considered and rejected

| Alternative | Rejected because |
|---|---|
| Keep R6 unchanged; widen the content model to carry every property | An open-ended feature list that is never finished, and every property added is another that can be dropped. Cloning preserves properties nobody enumerated. |
| Return to R4 surgical byte-patching | Reinstates the 422 treadmill — the failure R6 correctly removed, and a direct violation of invariant 1. |
| Adopt Clippit's `WmlComparer` | Forbidden by NFR-02 (no new NuGet) and unnecessary — the measured result needs only `DocumentFormat.OpenXml`. |
| Compare blocks by text equality | Two paragraphs with identical text can differ in formatting, list level, comment anchors or revision state. A text shortcut clones a block the user *did* change, silently discarding their edit — worse than the defect being fixed. |
| Pair blocks by `paraId` | Duplicates are spec-legal and Word regenerates ids on save. This mis-binds on precisely the documents the project exists to survive. |
| **Path A** — project-scoped exception, leaving ADR-049 as written | Wrong instrument. This is not a narrow deviation; it is a correction to the governing decision. Leaving the ADR unamended would let a future project re-derive R6's mistake from a still-authoritative rule. |
| **Path C** — comply with R6 as written | Complying re-ships the silent fidelity loss R8 exists to fix. |

---

## Evidence

Measured on the 18-document corpus, not argued. The threshold was ratified by task 023 **before any prototype
number existed**.

| | Master (control) | Prototype | Gate bar |
|---|---:|---:|---|
| Overall block preservation (lenient) | 18.08% | **100.00%** | ≥ 95% |
| Near-tier preservation | 6.67% | **100%**, every document | 100% |
| Hard-fails | — | **0** | 0 |
| Outcome-honesty violations | — | **0** | 0 |

Supporting: heavy restructure degrades to exactly R6's behaviour without hard-failing (the correct floor);
five round trips show **zero cumulative drift** through `paraId` regeneration each cycle; +2.1 / +19.0 / +19.1 ms
per save on warmed medians; publish 43.68 MB (−1.28 MB vs the 44.96 MB net10 baseline).

**100% is also the shape a vacuous pass takes**, so four facts are asserted per document to separate them:
the edit is present in the merged output; exactly one block was rendered (an all-clone merge renders zero);
the oracle located and excluded the edited block; and the same oracle in the same run reports 18.08% for the
control arm.

**Notes**: `projects/spaarkeai-compose-r8/notes/` — `gate-contract.md` (the oracle and its normalization
justifications), `control-measurement.md` (the control and its per-loss classification),
`merge-prototype-results.md` (the prototype), `gate-decision.md` (the gate call).

---

## Compliance

Supersedes the R6 amendment's implication that whole-body rendering is the save contract. Restores R4's I-4
**intent** — untouched content survives — without the anchor-resolution mechanism that made I-4 unachievable.
ADR-049 **I-5** (one body author) is unchanged and reinforced: the merge lives inside the renderer and is not
a second author.

**Other tensions (Path C — comply, mention only)**: ADR-007 and ADR-013 (the renderer stays model-in →
`byte[]`-out, no Graph types above `SpeFileStore`, no AI internals in `Services/Compose/`) · ADR-039 (engine
frozen — the merge adds no AI dispatch) · ADR-029 / root §10 (no new NuGet; publish reported against the
44.96 MB baseline) · ADR-038 (seam-first — the gate harness and corpus round-trip are the DoD).
