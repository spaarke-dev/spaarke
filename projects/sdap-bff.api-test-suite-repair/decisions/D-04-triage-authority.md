# D-04: Triage authority — agent judges, following §6 binding rules

**Status**: Locked (2026-05-30 design phase, captured 2026-05-31)
**Source**: [`design.md`](../design.md) §5.4
**Binding on**: §6.2 triage taxonomy enforcement (every task); §4.8 escalation procedure (rewrite gating); NFR-04 (archive count >10 escalation); FR-27 (Phase 4 exit ledger)

---

## Context

The project repairs ~5,215 tests across 269 files, with ~4,844 failing. The open question was whether the owner pre-approves each per-file repair-vs-archive decision (highest oversight, highest burden) or whether the AI agent judges within bounded rules (lower per-decision friction, owner oversight at phase exit via ledger). Per-decision approval would generate hundreds of micro-decision PRs for the owner; agent-judges-with-binding-rules concentrates owner attention on phase-exit aggregates.

## Decision

**Option A (refined).** Per §5.4 (verbatim):

> AI agent judges per-file repair-vs-archive decisions, **strictly bounded by the binding rules in §6 and the escalation procedure in §4.8.**
>
> Refinements (these prevent the silent-drift risk):
> - Agent applies §6.2 triage taxonomy explicitly — every decision tagged in the PR description
> - Agent escalates per §4.8 for any rewrite (>50% lines replaced)
> - Agent escalates if archive count exceeds 10% of touched files in a single phase (signals over-aggressive archiving)
> - Owner does NOT pre-approve individual decisions; owner reviews the per-phase exit ledger

## Rationale

Per §5.4 "Why robust over easy": per-decision owner approval would burden the owner with hundreds of micro-decisions. The binding rules + escalation triggers + ledger-at-phase-exit gives agent judgment AND owner oversight without per-decision PR-process burden.

The 4 binding mechanisms (taxonomy in PR description, §4.8 rewrite escalation, archive-count escalation, phase-exit ledger review) collectively constitute the silent-drift safety net — any one alone would be insufficient; all four together substitute for per-decision approval.

## Rejected alternatives

- **Owner pre-approves every per-file decision** — burdens owner with hundreds of micro-decisions; predecessor projects show this scales poorly and decisions queue indefinitely.
- **Agent judges with no escalation triggers** — silent drift risk: over-aggressive archiving or unsanctioned rewrites accumulate invisibly until phase exit reveals the gap.
- **Defer all per-file decisions to phase exit** — back-loads decision burden; phase exits would become massive review sessions.

## Downstream Impact

- **§6.2 triage taxonomy** — every touched test gets `[Trait("status", …)]`; PR description enumerates per-file decisions (binding on every Phase 1+2+3 task)
- **§4.8 rewrite escalation** — any file with >50% line replacement requires `escalations/rewrite-request-T-XX-FileName.md` before work proceeds (code review rejects unescalated diffs)
- **NFR-04 archive-count escalation** — agent escalates to owner before archiving the 11th file in a single phase
- **FR-27 (Phase 4 exit ledger)** — `ledgers/archive-ledger.md`, `ledgers/real-bug-ledger.md`, `ledgers/flaky-ledger.md` aggregate per-phase triage decisions for owner review at phase exit
- **Hard limit (NFR-02)** — ≤5% of touched files may be escalated for rewrite; if exceeded, project pauses for design-review (signals repair-not-rewrite thesis is wrong)

## Reassessment trigger

If the Phase 1 exit ledger shows the agent escalated >20% of decisions (versus expected ~5% per §4.8 hard limit), the binding-rules-with-thresholds framing is mis-calibrated and per-task authority needs revisiting. Conversely, if escalation count is near zero across two consecutive phases, thresholds may be too permissive and silent over-archiving needs spot-audit.
