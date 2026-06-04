# Completion — Agent Framework Fit Assessment R1

> **Project**: `agent-framework-fit-assessment-r1`
> **Completed**: 2026-06-03
> **Total tasks**: 9 (000-008) — all ✅
> **Primary deliverable**: [`docs/assessments/agent-framework-fit-assessment-2026-06-03.md`](../../docs/assessments/agent-framework-fit-assessment-2026-06-03.md) (959 lines)

---

## What shipped

| Output | Path | Purpose |
|---|---|---|
| **Canonical assessment** | `docs/assessments/agent-framework-fit-assessment-2026-06-03.md` | 959 lines; 10 sections + 36-row §10 Sources appendix; the project's primary decision document |
| Primary-source baseline | `projects/agent-framework-fit-assessment-r1/notes/00-primary-source-baseline.md` | 34 primary-source citations captured at 2026-06-03, SHA `afa7834e` |
| In-BFF surfaces inventory | `notes/01-spaarke-ai-surfaces-inventory.md` | S1-S4 + 2 discovered S8 surfaces |
| Non-BFF surfaces inventory | `notes/02-non-bff-ai-touchpoints-inventory.md` | S5 (bimodal), S6, S7 |
| Feature map | `notes/03-agent-framework-feature-map.md` | 12 features F1-F12 with 19 distinct primary-source citations |
| Per-surface decision matrix | `notes/04-per-surface-decision-matrix.md` | 10 surface verdicts with SPEC §4 criteria applied |
| Deployment + migration analysis | `notes/05-deployment-and-migration.md` | Deployment models + 8-17 person-week cost estimate + 10-item risk register |
| Adversarial review log | `notes/07-review-log.md` | 13 counter-arguments + adjudication trail + source freshness re-check |
| Unblock note for knowledge-r1 | `projects/agent-framework-knowledge-r1/UNBLOCK-RECOMMENDATION.md` | Recommended SPEC re-scoping for the parked downstream project |

## Key findings

The assessment reached **1 ADOPT (S5B canonical durable HITL legal workflows) · 5 PARTIAL · 4 DON'T ADOPT** across 10 evaluated surfaces. The single ADOPT (S5B) is greenfield with no migration cost; its deployment model — Foundry-hosted vs Workflows-in-BFF vs Workflows-in-Function — is honestly unresolved and recommended for prototyping before commitment (F12 evidence-thin). S1 SprkChat is PARTIAL gated on Issue #6268, but the adversarial review's source-freshness re-check surfaced that Spaarke's GPT-4o + Chat Completions stack may not actually be exposed to the bug's reproduction surface (Q7) — empirical verification could downgrade the gating to a feature-flag-only caveat. S2 JPS DON'T ADOPT is migration-cost-driven, not framework-mismatch; in greenfield, Workflows would be a credible competitor to JPS. The shared middleware-lift change (decorating `ISprkChatAgent` → `chatClient.AsBuilder().Use*().Build()` composition) is the single biggest cross-cutting infrastructure change implied by adoption — amortized across S1 + S3 + S5A + S8b, not four separate migrations. Quality discipline: 100% live-URL recency floor compliance, all decisions traced to primary sources captured at 2026-06-03, both quality-gate rounds (tasks 006 + 007) passed adr-check and code-review.

## Owner decisions required (the actual deliverable for human review)

Per the assessment's closing line 893, five decisions are surfaced for owner action:

a. Accept S1 PARTIAL + wait-for-#6268 default, OR pilot S1 lift now with feature flag (informed by Q7 verification)
b. Override and pilot S1 lift now with feature flag
c. Initiate S5B canonical durable HITL legal workflows project SPEC
d. Commit S7 D-A20 contract authoring to explicitly resolve U1/U2/U3
e. Capture the shared middleware-lift infrastructure change in an ADR-013 successor when S1 lift is approved

The choice shapes the next revision of ADR-013 and the unblock note for [`projects/agent-framework-knowledge-r1/UNBLOCK-RECOMMENDATION.md`](../agent-framework-knowledge-r1/UNBLOCK-RECOMMENDATION.md).

## Intentionally deferred (per project scoping decisions)

These were named in SPEC §3 "Non-goals" and are NOT in this project's scope:

- **No ADR-013 amendment.** The assessment surfaces an implied amendment (capture the shared middleware lift as binding pattern) but does NOT edit ADR-013. That's a downstream PR after human review.
- **No `agent-framework-knowledge-r1` SPEC edits.** The unblock-recommendation note in that project describes what SHOULD change; the SPEC itself is not edited. That project's owner reviews and decides.
- **No code changes to `src/`.** Read-only on `.cs` files preserved across all 9 tasks; the assessment cites code, it doesn't change it.
- **No TCO / licensing analysis** beyond what affects the fit decision. Foundry SKU costs are noted as an UNKNOWN affecting Q1 / Q4; the assessment does not produce a cost model.

## Future REFRESH triggers

Per assessment §9.3, monthly REFRESH cadence should re-check:

- **GitHub Issue #6268 status** — if it closes/has a workaround, S1 PARTIAL caveat dissolves
- **F12 durable hosting Learn page** — if Microsoft publishes a dedicated `/hosting/` page, the S5B prototyping recommendation's evidence base strengthens (or shifts)
- **Issue #6308 status** — Foundry hosting story currently in active triage
- **Sample tree churn** — upstream SHA changes; particularly `04-hosting/` since S5B depends on it
- **Q7 empirical test** — if/when run, document outcome and update §1 + §5.1 accordingly

## Process discipline observed

- All 9 tasks executed via `task-execute` skill per root CLAUDE.md §4 (mandatory protocol)
- Rigor levels honestly applied per project calibration (STANDARD for analysis tasks, FULL for tasks 006/007, MINIMAL for wrap-up)
- Sub-agent write boundary respected — sub-agents handled all `notes/` and assessment-doc writes; main session ran the Step 9.5 quality gate skills and aggregation commits
- Source recency floor (≥2026-04-01) enforced at every analysis step; 100% compliance preserved through adversarial revision
- Adversarial review delivered the right shape — 13 counter-arguments → 0 verdict flips → 3 new open questions → 8 framing strengthenings. Not over-correction, not insufficient aggression.

## Final tree state

```
projects/agent-framework-fit-assessment-r1/
├── SPEC.md                 (canonical plan; corrected S5 bimodal framing in task 002)
├── CLAUDE.md               (project conventions; corrected S5 row)
├── README.md               (status: COMPLETE)
├── TASK-INDEX.md           (all 9 tasks ✅)
├── current-task.md         (project complete; no active task)
├── COMPLETION.md           (this file)
├── tasks/                  (9 POMLs, all status=completed with completion-summary blocks)
└── notes/                  (07 files: 00-baseline, 01-spaarke-surfaces, 02-non-bff, 03-features, 04-matrix, 05-deployment, 07-review-log)

docs/assessments/
└── agent-framework-fit-assessment-2026-06-03.md   (the deliverable)

projects/agent-framework-knowledge-r1/
├── README.md               (parking notice updated; forward-pointer to assessment + unblock note)
├── UNBLOCK-RECOMMENDATION.md  (new; recommends SPEC re-scoping)
└── ... (SPEC.md, TASK-INDEX.md unchanged per scoping decision)
```
