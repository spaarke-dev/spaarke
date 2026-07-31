# ai-advanced-capabilities-agreements-r1 — Agreement Analysis: Review Depth & Output Deliverables

> **Status**: 🟢 Ready for execution (pipeline run 2026-07-31) · **Branch**: `work/ai-advanced-capabilities-agreements-r1`
> **Spec**: [spec.md](spec.md) (17 FRs / 6 NFRs) · **Plan**: [PLAN.md](PLAN.md) · **Tasks**: [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md)
> **Predecessor**: `ai-advanced-capabilities-nda-r1` (shipped NDA advisory vertical) · **Sibling**: `ai-advanced-capabilities-analysis-hub-r1` (platform — 001–070 + Phase 1 shipped)

## What this project delivers

Generalizes the shipped NDA advisory review into a **type-agnostic Agreement Analysis review machine**:

1. **Document-driven classifier + orientation** — "review this document" in the Assistant → detect *is-this-an-agreement*
   + *which type* (Reasoning tier, accuracy-first) → bind that type's knowledge pack, scope the tool palette, focus the
   conversation. Confirmation gate on uncertainty (≥0.85 baseline) or multiplicity (composite docs → "employment · just
   the NDA · both", where both = multiple packs).
2. **One general `agreement-review` Action** over the **`sprk_agreementtype` registry** (Dataverse table; per-type
   knowledge packs are the value, general pack the fallback; new type = new row, zero code). Per-type projects (lease,
   employment, …) author packs later — the nda-r1 pattern.
3. **Review-depth UX** in Compose — multi-select Review Notes + sequential batch AI actions with progress; bidirectional
   summary↔note↔document highlighting; separated, location-labelled Assistant confirmations.
4. **Durable review** (hub Phases 2/3, accepted 2026-07-31) — reopen restores the review **with zero LLM calls**
   (compose-disposition re-route + findings materializer branch); wizard-finish **auto-runs** the review (file-id
   bridge + durable session binding).
5. **Output deliverables** — Review Summary Memo ({location, before, after, why, golden-ref} → `sprk_analysisoutput`;
   generate-docx / email) + **Word-comment export fidelity** (comments mirror the on-screen gutter structure).
6. **Fidelity** — DEF-01 clause-anchoring correctness fix + nda→agreements rename + WS-4 `ComputedNumber`/`CitationResolver` consumption.

**Out of scope**: PDF ingest (→ compose-r5) · per-type knowledge packs (→ sibling projects) · hub wizard/spine/sessions
(→ analysis-hub-r1, shipped) · autonomous/email intake (future) · doc×question grid (deferred).

## Graduation criteria (from spec Success Criteria)

- Advisory Review runs on a **non-NDA agreement** with correct clause labels; no NDA gating; rename complete.
- Classifier orients an untyped upload on the Reasoning tier; confirmation below ≥0.85 / on composite docs; registry row
  routes with zero code change.
- DEF-01 test green **with the original strict assertion**.
- **Reopen restores the review with zero LLM calls** (gutter + summary panel); wizard-finish auto-runs the review with
  durable binding.
- Memo generates + persists + exports (docx/email); Word comments mirror the gutter (configurable author).
- BFF publish ≤60 MB; golden-utterance evals green; e2e UI tests pass.

## Key documents

| Doc | What |
|---|---|
| [design.md](design.md) | 6-lens design (rev. 2026-07-31 against hub built state) |
| [spec.md](spec.md) | AI implementation spec — 17 FRs / 6 NFRs |
| [notes/HUB-R1-REVIEW-2026-07-30.md](notes/HUB-R1-REVIEW-2026-07-30.md) | 7-agent verified hub built-state review (contracts, corrections, risks) |
| [notes/COORDINATION-agreements-r1-ANSWERS-and-QUESTIONS-to-hub-r1.md](notes/COORDINATION-agreements-r1-ANSWERS-and-QUESTIONS-to-hub-r1.md) | **→ share with hub-r1**: answers to their 4 asks + 5 questions back |
| [notes/COORDINATION-with-analysis-hub-r1.md](notes/COORDINATION-with-analysis-hub-r1.md) | Original asks A1–A6 (2026-07-29; answered by hub's reverse doc) |
| notes/HANDOFF-from-compose-fidelity-r4.5.md · notes/word-comment-export-gap.md | Inherited handoffs (DEF-01, #7 export gap) |

## How to work on this project

Say **"work on task 001"** (or "continue") — tasks execute via the `task-execute` skill with rigor gates.
See [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) for the DAG + parallel waves. Hot-path: **BFF=Y, SpaarkeAi=Y**.
