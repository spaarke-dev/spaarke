# THE MERGE, IN PRODUCTION — task 040

> **Landed** 2026-08-21 · Prototype: [`merge-prototype-results.md`](merge-prototype-results.md) ·
> Gate: [`gate-decision.md`](gate-decision.md) · Contract: [`gate-contract.md`](gate-contract.md) ·
> ADR: [`ADR-049` R8 third amendment](../../../.claude/adr/ADR-049-compose-shadow-document.md) (applied first, this task)

---

## 1. POML reconciliation (acceptance criterion 1)

Task 040's POML was authored **before** the Phase-3 gate and carries a `⚠️ PROVISIONAL` banner instructing
that `gate-decision.md` is authoritative where the two conflict. Five reconciliations, each recorded before
any code was written.

| # | POML said | Reconciled to | Why |
|---|---|---|---|
| **R1** | **FR-A01** — promote `ComposeBaselineParaIdStamper` to the render path | **DROPPED** | Gate §6. The merge never resolves a `paraId`, so an unstamped baseline costs it nothing. Confirmed twice over during implementation: (a) the renderer **already** guarantees a unique valid `w14:paraId` on every `w:p` via its own `AssignParaIds` post-pass, so the saved output needs no stamper either; (b) the comparison strips `ParaId` entirely (see R2), so stamping could not change a single clone/render decision. Promoting it would have been a production behaviour change that buys nothing. |
| **R2** | (unstated) compare posted vs base blocks | **Compare with `ParaId` stripped at any depth** | **Not a refinement — required for the merge to work at all through the wire.** The HTML projection the client edits emits `data-paraid` from the projection's identity map, which **mints** an id for a paragraph the file left unstamped; `BuildContentModel` reports that same paragraph's `ParaId` as `null` (it reads the file attribute). The prototype never met this because it built the posted model from the content model directly. Copying the prototype's comparison verbatim would have scored 100% at the renderer and near 0% in production. |
| **R3** | pair by document order | **Pair by longest common subsequence over block equivalence** | Positional pairing collapses to **zero** preservation the moment a block is inserted or deleted — every later index shifts by one and nothing matches. "The user added a paragraph" is the most common edit there is; the prototype only ever measured single-run edits at a fixed block count, so it never met the case. LCS handles edit, insert and delete exactly, and pairs only blocks that are already equivalent, so a mis-pair is harmless by construction. |
| **R4** | thread cloned list items through `ListRenderState` (gate §6) | **Done — and the underlying defect was larger** | `orderedRunByLevel` was **local to each `RenderBlocks` call**, and the prototype flushed a batch at every clone. So the cursor was not merely un-advanced by clones, it was **destroyed** at every clone boundary. Fixed by hoisting one cursor for the whole body, passed in, and recording every cloned block into it. |
| **R5** | FR-A04 property inheritance (POML) vs "041 owns it" (gate §6) | **Basic inheritance HERE; 041 keeps the deeper work** | The base counterpart is already paired and in hand at exactly this point in the code; deferring would mean a second pass over the most contended file in the repo. 040 delivers `pPr` inheritance + dominant-`rPr` inheritance. **041 still owns** opaque-atom carry and character-level re-association, and remains non-optional. |

Also carried out per gate §6: **carrier provenance** — the merge captures its base side from the *same*
`carrierBytes` the renderer opened, inside `RenderIntoCarrier`, so base and output cannot come from different
documents. There is no path by which a stale carrier reaches one and not the other.

---

## 2. Result — the gate, re-run against the production implementation

18 documents · 271 comparable blocks · 210 near-tier-relevant · **253 cloned, 18 rendered**.

| | Control (master) | **Production merge** | Prototype | Bar |
|---|---:|---:|---:|---|
| Overall preservation, **lenient**, block-weighted | 18.08% | **100.00%** | 100.00% | ≥95% (T2) |
| Near tier, **lenient** | 6.67% | **100%**, all 14 measurable | 100% | 100% every doc (T1) |
| Overall, **strict** | 12.18% | **16 of 18 at 100%**; 2 partial (§4) | no-regression only | ratchet, not a gate (T5) |
| Hard-fails | — | **0** | 0 | 0 (T3) |
| Honesty violations | — | **0** | 0 | 0 (T4) |

Four documents report near-tier `n/a` (no near-tier construct) — reported as **null, never a vacuous 100%**.

The control arm reproduces **18.08% / 6.67% exactly**, the figures task 023 published — the same instrument,
the same corpus, a different implementation under it.

### Strict is materially better than the prototype achieved

The prototype only had to clear a no-regression ratchet on strict. Production reaches **100% strict on 16 of
18 documents** (e.g. the 109-block patent claims document: **0.93% → 100%**; `AppligentNDA_Signed.docx`
2.04% → 95.92%). Strict differs from lenient in exactly one bit — whether `w14:paraId`/`w14:textId` are
normalized away — so it also catches *identity churn* on untouched blocks, not just content loss.

### Supporting

| | Result |
|---|---|
| N-cycle (N=5) | Flat **100% every cycle** on all three documents — zero cumulative drift |
| Heavy restructure | `cloned=1, rendered=11/12`, no hard fail — degrades to R6's behaviour, the correct floor |
| NFR-07 performance | 60 ms → 131 ms total across 18 documents (**≈ +3.9 ms/document mean**), one extra baseline projection + DOM clone as budgeted |
| Publish size | **43.69 MB** compressed incl. PDBs — **−1.27 MB** vs the 44.96 MB net10 baseline; ceiling 60 MB |
| New NuGet | **None.** Pure `DocumentFormat.OpenXml` |
| CVEs | `dotnet list package --vulnerable --include-transitive` → **no vulnerable packages** |
| NetArchTest | **36/36** |
| Full BFF suite | **10,792 passed / 0 failed / 97 skipped** |

---

## 3. What is new versus the prototype

| Capability | Prototype | Production |
|---|---|---|
| Edit a paragraph | ✅ | ✅ |
| **Insert** a paragraph | ❌ *never measured* — positional pairing gives 0% | ✅ every other block still cloned |
| **Delete** a paragraph | ❌ *never measured* | ✅ every survivor still cloned |
| Edited block keeps its formatting | ❌ not exercised at all | ✅ `pPr` + dominant `rPr` inherited |
| List continuity across clones | ❌ cursor destroyed at every clone | ✅ one cursor, clones recorded into it |
| Works through the wire | ❌ would have failed on unstamped paragraphs | ✅ `ParaId` stripped from the comparison |
| Placed as a collaborator | ❌ inline in the renderer | ✅ `ComposeBlockMerge.cs`, no DI registration |

---

## 4. Residual — carried to task 045's loss list

### 4.1 The edited block still loses what inheritance cannot reach

Inheritance copies the base paragraph's unmodeled `pPr` children and the **dominant** run's `rPr` (the run
holding the most characters). It does **not** re-associate properties to the specific runs they came from, so
a paragraph whose formatting varies mid-run is levelled to its dominant formatting on edit. **Task 041 owns
this**, and the gate still does not measure it — the oracle excludes the edited block by construction.

Three exclusions are deliberate: `w:pStyle`, `w:numPr` and `w:sectPr` are never inherited, because the model
fully determines the first two (a user who demotes a heading must not get the heading style back) and the
renderer re-attaches the trailing section itself.

### 4.2 Reorder still yields no benefit

LCS matches never cross, so a moved block is unmatched and re-rendered. Never a failure; no preservation.
Unchanged from the ADR's stated limitation.

### 4.3 `paraId` re-minting inside `mc:AlternateContent` — an investigated-and-reverted change

**This is the one place where two invariants of this project genuinely conflict, and it should be decided
deliberately rather than by whoever edits the file next.**

`AssignParaIds` walks `body.Descendants<Paragraph>()`, which **enters opaque regions**. Word writes a text box
twice (`mc:Choice` + `mc:Fallback`) carrying the **same** `w14:paraId` — spec-legal and intentional — and the
dedup pass treats the second copy as malformed and re-mints it. So a block the merge cloned *verbatim* is
mutated after the fact. Measured cost, strict level only:

| Document | Strict | Lenient |
|---|---:|---:|
| `alternate-content-duplicate-paraid.docx` | 66.67% | 100% |
| `AppligentNDA_Signed.docx` | 95.92% | 100% |

Excluding opaque-nested paragraphs from the pass takes **both to 100% strict**. It was implemented, measured,
and then **reverted**, because it breaks task 011's global-paraId-uniqueness guarantee — `RenderOnSaveSeamTests`
pins it by name on the NDA's `2BBF07C9/CA/CB` class, and duplicate anchors were part of the production-422
failure chain.

**Why reverted rather than shipped**: strict is a **no-regression ratchet, not a gate** (task 031 T5), and both
documents clear it enormously (33.33% → 66.67%, 2.04% → 95.92%). Trading a safety invariant for a better number
on a metric that is explicitly not a gate is precisely the move the ADR-049 **paired MUST** exists to forbid.
The rationale is recorded inline at the `AssignParaIds` call site so the next person to look at that line finds
the measurement rather than repeating it.

A real resolution means changing what the **identity map** considers a block (the projection already declines
to emit opaque-nested paragraphs as top-level blocks), which is a projection change, not a rendering change.
**Not task 040's to make.**

---

## 5. Tests

New: [`tests/integration/seam/Compose/ComposeMergeSeamTests.cs`](../../../tests/integration/seam/Compose/ComposeMergeSeamTests.cs)
— 12 facts covering insert, delete, property inheritance, the style/numbering exclusions, list continuity
across clones, hyperlink-relationship survival on a cloned block, unmodeled constructs on four corpus
fixtures, fail-open on an unavailable base side, and full accounting of every posted block.

**Three pre-existing seam tests were pinned to the render path** (`mergeUnchangedBlocks: false`), with the
reason recorded at each call site:

| Test | Asserts |
|---|---|
| `ComposeHyperlinkCommentSeamTests.RenderIntoCarrier_HyperlinkCommentRoundTrip_…` | how the renderer **re-authors** comment anchors |
| `ComposeHeaderFooterPageBreakSeamTests.BuildContentModel_InteriorSectionBreak_…` | that an interior `sectPr` **flattens** |
| `ComposeTrackedChangesSeamTests.RoundTrip_Carrier_AuthorsWordValidRevisionMarkup_…` | that revision ids are **minted above** the carrier's |

Each posts the projection **unmodified**, so with the merge on every block is cloned and the re-authoring
under test never runs. They were not asserting stale behaviour — they target the render path, which still
executes for every block a user actually changes. Pinning keeps that coverage instead of deleting it, and
the merge path now has its own.

`mergeUnchangedBlocks` remains on the signature, defaulting to **true**. It is a **test seam, not a feature
flag** — bound to no configuration — and it exists so the measurement can run a control arm through the same
renderer in the same run. That two-arm capability is the anti-vacuity evidence the gate rests on.

---

## 6. Negatives verified

| Criterion | Evidence |
|---|---|
| No `body.Descendants<Paragraph>()` in the merge path | `grep` — the only occurrence in `ComposeBlockMerge.cs` is the comment forbidding it |
| Exactly one body author | `ComposeBlockMerge` never appends to `w:body`; it returns a **plan**. The single `body.AppendChild(clone)` is inside `ComposeDocumentRenderer` |
| No new DI registration (ADR-010) | `grep` finds no registration site; NetArchTest 36/36 |
| No refusal path | Fail-open on unavailable base side (asserted); heavy restructure degrades without hard-failing; no 422 introduced |
| No new NuGet / no new HIGH CVE / size reported | §2 |
