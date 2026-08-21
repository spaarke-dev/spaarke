# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-21 (task-execute, task 020 start)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **020** — FR-G01/G02/G03: the gate contract (preservation oracle + outcome honesty + two comparison levels) |
| **Phase** | 2 — Oracle & Corpus (**critical path**: 020 → 023 → 030 → **031 GATE**) |
| **Rigor / Tier** | FULL · `opus` @ `max` · steps `directional` · `parallel-safe: false` |
| **Step** | 3 of 7 — implement the normalization layer |
| **Status** | in-progress |
| **Next Action** | Write `tests/integration/seam/Compose/ComposeBlockPreservationOracle.cs`, then wire it into `ComposeFidelityGateHarnessTests.cs` |

### Critical context

Phase 1 (Track S) is **CLOSED** — owner UAT GO on 2026-08-21. Task 020 builds the measurement the whole
architecture decision rests on. **If the oracle reads ~100% on master, STOP** — that means it is
normalizing away real signal (escalation trigger), because R6's silent fidelity loss is verified: the owner
is looking at it in dev right now.

### Design decided (steps 1–2 complete)

- **Existing harness asserts only** round-trip HTTP success + edit presence + warn-not-fail. Its header
  states outright that byte-identity is NOT asserted on the save path. That is the hole R6 shipped through.
- **Reuse (CLAUDE.md §11)**: extend `ComposeFidelityGateHarnessTests` + `ComposeCorpusFixtureLocator`; the
  new comparison engine lands as a sibling helper file, the same idiom as the existing
  `ComposeOoxmlPackagePartComparer.cs`. **Not** a second harness — still one `[Theory]` gate, one corpus,
  one locator.
- **`ComposeOoxmlPackagePartComparer` cannot be extended for this**: it answers a binary
  "is the package byte-identical" over whole parts, and its `IsStructurallyFaithful` uses
  `body.Descendants<Paragraph>()` — the exact walk this task forbids. Left untouched (it serves the no-op
  byte-diff suite).
- **Pairing**: direct `w:body` children, document order, `paraId` corroborating only; unpaired blocks
  reported as dropped/added; duplicate-paraId documents flagged distinctly.
- **Near tier**: rPr · pPr · ind · tabs/tab · footnoteReference/footnoteRef · fldSimple/fldChar/instrText.
- **Outcome honesty**: wire `outcome` ∈ `ComposeSaveOutcomes` closed set, cross-checked against whether
  bytes actually reached the SPE facade boundary (the harness already captures them).

### Files modified this session

| File | Purpose |
|---|---|
| `tests/integration/seam/Compose/ComposeBlockPreservationOracle.cs` | NEW — normalization + pairing + tiered comparison engine |
| `tests/integration/seam/Compose/ComposeFidelityGateHarnessTests.cs` | Wire the oracle + outcome-honesty into the gate; extend the JSON sink |
| `projects/spaarkeai-compose-r8/notes/gate-contract.md` | NEW — normalization justifications + near-tier definition |

---

## Prior task (017) — CLOSED

Track S deployed to dev (BFF `spaarke-bff-dev` + `sprk_spaarkeai`, both from `e5815e862`, one window).
Owner UAT **GO**. One defect found by the UAT and fixed: the save-degradation banner claimed *"the original
file is unchanged until you save"* **after** the bytes were written. Evidence: `notes/track-s-uat.md`.

The two banners the owner still sees are **not** Track S — formatting-simplified is **Track A** (Phases 2–4),
*"wording differs slightly"* is **Track C** (051–053, startable now, not gated on 031).
