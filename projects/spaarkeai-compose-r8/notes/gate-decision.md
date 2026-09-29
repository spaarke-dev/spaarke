# THE GATE DECISION — task 031

> **Decided** 2026-08-21 · Evidence: [`merge-prototype-results.md`](merge-prototype-results.md) ·
> Bar: [`control-measurement.md`](control-measurement.md) · Control: [`gate-contract.md`](gate-contract.md)

---

## 1. The thresholds in force

Stated first, and unchanged since task 023 ratified them on 2026-08-21 — **before any prototype number
existed**. The owner accepted no revision, so the spec defaults stand as 023 refined them:

| # | Criterion | Bar |
|---|---|---|
| T1 | Near-tier preservation, **lenient**, on **every single document** | **100%** |
| T2 | Overall block preservation, **lenient**, corpus-wide | **≥ 95%** |
| T3 | Hard-fails (non-success HTTP, failed projection, edit missing on reopen) | **zero** |
| T4 | Outcome-honesty violations | **zero** |
| T5 | Strict overall — a no-regression ratchet, **not** a gate | **≥ 12.18%** |

The threshold's whole function is to be fixed before the number is known. It was.

---

## 2. The result, per criterion

| # | Bar | Measured | Verdict |
|---|---|---|---|
| T1 | 100% near-tier, every document | **100%** on 14 of 14 documents where the near tier is in play (4 have no near-tier construct; reported `n/a`, never as a vacuous 100%) | ✅ **PASS** |
| T2 | ≥95% overall corpus-wide | **100.00%** — 18 of 18 documents individually at 100% | ✅ **PASS** |
| T3 | Zero hard-fails | **Zero.** Every document produced a readable package; the edit is present in all 18 | ✅ **PASS** |
| T4 | Zero honesty violations | **Zero** across the corpus | ✅ **PASS** |
| T5 | Strict ≥ 12.18% | No regression | ✅ **PASS** |

Control for comparison: **18.08% overall / 6.67% near-tier**.

### Supporting criteria

| | Result |
|---|---|
| FR-G06 heavy restructure | Body fully reversed: `cloned=0, rendered=12/12`, no hard fail — degrades to exactly R6's behaviour, the correct floor |
| FR-G07 N-cycle (N=5) | Flat 100% across all cycles on three documents, through paraId regeneration each cycle — **zero cumulative drift** |
| NFR-07 performance | +2.1 / +19.0 / +19.1 ms per save (warmed medians of 15) — one extra baseline projection + DOM clone, as budgeted |
| NFR-02 no new NuGet | Pure `DocumentFormat.OpenXml`. Clippit not required; no owner decision needed |
| Publish size | 43.68 MB compressed, **−1.28 MB** vs the 44.96 MB baseline (ceiling 60 MB) |

---

## 3. DECISION: **PASS**

**No miss condition fired. Neither escalation trigger fired.**

The three-way merge — re-project the retained baseline server-side, pair by document order, clone the blocks
the user did not touch — is confirmed as R8's architecture. **Phase 4 is authorized.**

### What the PASS rests on, stated so it can be challenged

The result is 100% on every document, which is the shape a *vacuous* pass also takes. Four facts separate
them, all asserted per document in the measurement:

1. The **edit is present** in the merged output (`ExtractBodyText` contains the marker).
2. **Exactly one block was rendered** per document — an all-clone merge would render zero.
3. The oracle **located and excluded** the edited block (`EditedBlockIndex >= 0`).
4. The **same oracle, in the same run**, reports 18.08% for the control arm. An instrument returning two
   different answers for two inputs is measuring something.

---

## 4. The caveat that travels with the PASS

**The gate measures UNTOUCHED blocks. It does not measure the edited one, by construction.**

The paragraph the user actually typed in is still rebuilt from a content model carrying justification, bold
and italic — so it still loses its font, size, colour, indentation, spacing, tabs and numbering.

For a user editing one paragraph of a forty-page contract this is the difference between one damaged
paragraph and forty damaged pages. It is **not** "fidelity solved", and **task 041 (FR-A04 property
inheritance) is what closes it — the prototype does not exercise it at all.**

This is recorded in the decision itself so that no later reader can take "the gate passed at 100%" to mean
Compose preserves everything. It does not yet.

### Per-family dispositions (escalation trigger 2)

No construct family failed while the aggregate passed. Two families move explicitly rather than being
absorbed:

| Family | Disposition |
|---|---|
| **Heavy reorder** | Not a capability-gate trigger — degrades to R6, never fails, no benefit. → **residual loss list (045)** |
| **The edited block's own formatting** | → **task 041**; any residue after 041 → **045** |
| Text boxes / fields / content controls on **untouched** blocks | **No longer residual loss** — preserved by cloning. Task 044 must NARROW the ADR-049 accept-flatten warning taxonomy so users are not warned about losses that no longer occur |

---

## 5. `ComposeShadowPatchEngine` subsumption: **NOT-CONFIRMED**

**Task 074 MUST NOT delete it.**

### Evidence

The task requires prototype evidence "specifically including clean-apply for reopened authored documents".
**The prototype provides none, because the engine and the prototype serve different paths.** All three live
call sites are on the **op-log** path, not the render path:

| Site | Path |
|---|---|
| `ComposeService.cs:1325` | The **transitional op-log save shape** — logged at Warning for retirement telemetry; its own comment says post-cutover clients "never reach this block", and names tasks 013/090 as owning eventual removal |
| `ComposeService.cs:2349` | Auto-generated ops + auto-comments apply |
| `ComposeService.cs:2581` | Per-unit **partial-apply recovery** |

The prototype exercised `ComposeDocumentRenderer.RenderIntoCarrier` on **Imported**-origin corpus documents.
It did **not** exercise clean-apply, did **not** exercise **Authored** (born-in-editor) documents, and did
**not** invoke the patch engine on any path.

### Determination

"Probably subsumed" is explicitly insufficient to authorize deleting 3,000 lines, and that is all the
evidence supports. **NOT-CONFIRMED.**

### Consequence for FR-D01 (surfaced, per escalation trigger 3)

FR-D01 requires all five Compose god-class files under 2,000 lines **with waivers deleted**.
`ComposeShadowPatchEngine.cs` (2,999 lines) cannot be deleted on this evidence, so **one waiver survives**
unless task 074 first produces the missing evidence.

**What task 074 must obtain before deleting anything** — three pieces, none of which exist today:

1. Clean-apply round trip for a **reopened Authored** document through the render path, at gate-level
   preservation.
2. Proof the transitional op-log shape at `:1325` is **unreachable** from every shipped client — the
   retirement telemetry that comment describes is the natural source.
3. Coverage for the **partial-apply recovery** path (`:2581`), which has no render-path equivalent today.

Recommendation: 074 stays blocked behind that evidence, and the FR-D01 miss is accepted and documented
rather than resolved by deleting a live engine.

---

## 6. Phase-4 POMLs needing amendment

Authored provisionally; the prototype changes four of them.

| Task | Amendment |
|---|---|
| **040** (merge mechanism) | Drop the `ComposeBaselineParaIdStamper` promotion — **proved unnecessary**, the merge never resolves a paraId. **Add**: thread cloned list items through `ListRenderState`; consider paraId-corroborated pairing as a *fallback after* document-order (never a primary key); verify carrier provenance end-to-end |
| **041** (opaque-atom carry) | **Elevated in importance.** It now owns the only remaining user-visible loss — the edited block's own formatting. Explicitly scope FR-A04 property inheritance as its primary deliverable |
| **044** (two document classes) | **Add**: narrow the accept-flatten warning taxonomy — text-box / field / content-control warnings must fire only for blocks actually re-rendered |
| **074** (retire the engine) | **Blocked.** Add the three evidence preconditions in §5 |

---

## 7. Phase-4 authorization

**AUTHORIZED**, with two conditions:

1. **041 is not optional and not deferrable.** Without property inheritance the user still watches their
   edited paragraph lose its formatting, which is what they report as "it destroyed my document". A Phase 4
   that ships 040 alone has not fixed what the owner is looking at.
2. **074 remains blocked** pending §5's evidence.

The gate did its job: it was set before the number was known, the number cleared it, and the two things the
number does *not* cover are named here rather than discovered at UAT.
