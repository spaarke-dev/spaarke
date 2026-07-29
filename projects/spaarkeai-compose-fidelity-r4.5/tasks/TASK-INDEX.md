# TASK-INDEX — Spaarke Compose Legal Fidelity R4.5

> **Project**: `spaarkeai-compose-fidelity-r4.5` · **Created**: 2026-07-28
> **Plan**: [`../plan.md`](../plan.md) · **Spec**: [`../spec.md`](../spec.md)
> Status legend: 🔲 not-started · 🔄 in-progress · ✅ completed · ⛔ blocked · ⏸ deferred

## Task Registry

| # | Task | Phase | WS | Model | Effort | Rigor | Deps | ∥-safe | Status |
|---|---|---|---|---|---|---|---|---|---|
| 001 | Extend fidelity corpus — legal-numbering exemplars | 0 Foundation | — | sonnet | med | STANDARD | — | ✔ | ✅ |
| 002 | Read-fidelity harness — text-exact + numbering-exact golden | 0 Foundation | — | sonnet | high | FULL | 001 | ✔ | ✅ |
| 010 | Upload path returns a projection | 1 One reader | WS-1 | sonnet | high | FULL | 002 | ✘ | ✅ (gate @ WS-1 boundary) |
| 011 | Stateless `POST /api/compose/project` for browse | 1 One reader | WS-1 | sonnet | high | FULL | 010 | ✘ | ✅ (gate @ WS-1 boundary) |
| 012 | Open-in-Compose transient projected server-side | 1 One reader | WS-1 | sonnet | high | FULL | 011 | ✘ | ✅ (FR-02 already met by 010+011; guards added) |
| 013 | Delete mammoth fallback + `docxToTipTapHtml` | 1 One reader | WS-1 | sonnet | high | FULL | 012 | ✘ | ✅ (null→error-state; pre-existing advisoryComments fail → 031) |
| 014 | Deploy + UAT — one reader everywhere | 1 One reader | WS-1 | sonnet | med | STANDARD | 013 | ✘ | ⛔ HUMAN (deploy+UAT — batched w/ 034) |
| 020 | Stop silent drops (`w:cr`, `w:sym`) + warning mechanism | 2 Harden read | WS-2 | sonnet | xhigh | FULL | 013 | ✘ | ✅ (8/8 text-exact; gate @ WS-2 boundary) |
| 021 | Emit `w:ind` indentation + `white-space:pre-wrap` | 2 Harden read | WS-2 | sonnet | high | FULL | 020 | ✘ | ✅ (+composeIndentExtension.ts; gate @ WS-2 boundary) |
| 022 | OOXML construct audit + missing projection tests | 2 Harden read | WS-2 | sonnet | high | FULL | 021 | ✘ | ✅ (5 silent drops fixed; **WS-2 gate PASS**) |
| 030 | Numbering-model reader (numbering.xml + style-linked) | 3 Numbering | WS-3 | sonnet | xhigh | FULL | 022 | ✘ | ✅ (model exposed for 031; HTML byte-identical) |
| 031 | **Numbering computation engine (replay Word)** | 3 Numbering | WS-3 | **opus** | xhigh | FULL | 030 | ✘ | ✅ **NFR-02 GREEN — 24/24 golden = Word; theory live** |
| 032 | Render computed label as non-editable number-atom | 3 Numbering | WS-3 | sonnet | high | FULL | 031 | ✘ | ✅ (widget-decoration; text-exact + numbering-exact green) |
| 033 | Round-trip agreement test (write-side ↔ read-side) | 3 Numbering | WS-3 | sonnet | high | FULL | 032 | ✘ | ✅ (caught **DEF-03**; now green) |
| 035 | **Fix DEF-03**: numId-aware counter/reset in NumberingComputationEngine | 3 Numbering | WS-3 | **opus** | xhigh | FULL | 033 | ✘ | ✅ (counter re-keyed (numId,level); 694 pass/0 fail) |
| 034 | Deploy + UAT — numbering identical to Word | 3 Numbering | WS-3 | sonnet | med | STANDARD | 033 | ✘ | 🔲 |
| 040 | Extend projection with reference fields | 4 Reference | WS-4 | sonnet | high | FULL | 033 | ✘ | ✅ (numberingLevel/listPath/headingLevel; 705 pass) |
| 041 | Persist `paraId → number` (payload + session ledger) | 4 Reference | WS-4 | sonnet | high | FULL | 040 | ✘ | ✅ (reused ChatSession/StoredSession per AnchoredAnnotations precedent; /conflict-check clean; coordinate merge w/ ai-redesign-r2) |
| 042 | **Citation resolver (single / sub-item / range)** | 4 Reference | WS-4 | **opus** | xhigh | FULL | 041 | ✘ | ✅ (pure CitationResolver; 739 pass; contract+corpus-gap → confirm) |
| 050 | WS-5 spike — LibreOffice-headless pagination + divergence | 5 Page/Line | WS-5 | sonnet | high | STANDARD | 001 | ✔ | ✅ |
| 051 | WS-5 spike — Word-service eval + NFR-03 licensing | 5 Page/Line | WS-5 | opus | high | STANDARD | — | ✔ | ✅ (2 license items → human sign-off @ 052) |
| 052 | WS-5 decision record — ship vs fast-follow | 5 Page/Line | WS-5 | opus | high | STANDARD | 050,051 | ✘ | ✅ **DEFER** (2 licensing sign-offs → human @ fast-follow) |
| 090 | Project wrap-up (status / lessons / test-diet / archive) | 9 Wrap-up | — | sonnet | med | MINIMAL | all | ✘ | 🔲 |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | ∥-safe | Notes |
|---|---|---|---|---|
| **W0** | 001, 002 | none | true | Different files (fixtures vs harness). 002 consumes 001's fixtures. |
| **W1** | 010 → 011 → 012 → 013 | 002 | **false** | Sequential — shared `ComposeEndpoints.cs` / `ComposeWorkspace.tsx` / `ComposeEditor.tsx`. |
| **W1-deploy** | 014 | 013 | false | Deploy gate (prescriptive). |
| **W2** | 020 → 021 → 022 | 013 | **false** | Sequential — shared `ComposeDocxProjectionBuilder.cs`. |
| **W3** | 030 → 031 → 032 → 033 | 022 | **false** | Flagship. Sequential — shared builder + projection. |
| **W3-deploy** | 034 | 033 | false | Deploy gate (prescriptive). |
| **W4** | 040 → 041 → 042 | 033 | **false** | Sequential — shared `ComposeDocxProjection` + `ComposeService`. |
| **W5** | 050 ∥ 051 → 052 | 001 (for 050) | 050/051 true; 052 false | Research spike — **can run in parallel with W1–W4**; 052 synthesizes. |
| **W9** | 090 | all | false | Wrap-up. |

**Goal-eligibility**: All waves **NOT goal-eligible** (`/goal` loop off). Rationale: nearly every wave is `parallel-safe: false` (shared `Services/Compose/` files force sequential execution), and W1-deploy/W3-deploy are deploy gates; legal read-fidelity is high-stakes (numbering-exactness a release blocker). Run per-task with `task-execute` + Step 9.5 gates.

## Critical Path

`002 → 010 → 011 → 012 → 013 → 020 → 021 → 022 → 030 → 031 → 032 → 033 → 040 → 041 → 042 → 090`

WS-3 (030–033) is the flagship and the longest single stretch. **WS-5 (050–052) runs alongside** W1–W4 and is decision-only.

## High-Risk Items

- **031 Numbering computation engine** (opus/xhigh) — numbering-exactness is a spec release blocker; must equal Word 100% across interrupted / multi-level / style-linked / letters / roman / legal schemes. A *wrong* legal number is worse than an absent one → hard escalation on any divergence.
- **013 Delete mammoth** — irreversible removal of the fallback; gated on 010–012 covering every entry path. Grep-prove before deleting; an unexpected Compose mammoth consumer → STOP.
- **042 Citation resolver** (opus/xhigh) — the exact tool-facing API contract for ranges + sub-items is a spec **Unresolved Question**; confirm the interface with the human before finalizing.
- **052 WS-5 decision** — a "ship in R4.5" outcome **expands scope** beyond the spike → escalate per root §6 before adding implementation tasks.
- **All `Services/Compose/` tasks** — hot-path overlap with `spaarkeai-compose-r1/r2/r3/r4` + `spaarke-ai-architecture-redesign-r2`. `parallel-safe: false`; **`/conflict-check` before every BFF PR**. Watch PRs #690 (LFS corpus), #266 (OpenXml bump).

## Coordination

- Consume `Services/Ai/PublicContracts/` — **no fork** of `Services/Ai/`. No new AI dispatch; engine frozen (ADR-039).
- Publish ≤60 MB; WS-1..WS-4 ~0 delta; **WS-5 sidecar out-of-publish**.
- Line numbers cited in spec/design/POMLs may have shifted after today's master merge — **re-grep before editing**.
