# Current Task State — Spaarke AI Architecture Redesign R2 (Core)

> **Last Updated**: 2026-07-10 evening (post-Tranche-H, pre-compact — by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Protocol: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Progress** | **59 of 62 tasks** — ALL implementation + verification DONE and **MERGED TO MASTER** (core PRs #620 #622 #623 #624 #625; compose-r2's #626 unified both projects on master `14dd8ee3c`+). Worktree = master (merged clean). Remaining rows: **049 + 069 (OPERATOR UAT gates)** + **090 wrap-up close**. |
| **Deployed state** | spaarkedev1 runs **master-unified BOTH-projects build** (compose-r2 redeployed BFF + SpaarkeAi post-#626; healthz 200 Healthy). Memory wave FULLY live (store/memory.write/governance endpoints/Binder + M3 budgets/fresh-bias + 074 audit cutover + 075 retirement). **3 retired sprk_analysistool rows DEACTIVATED** (verified Inactive; catalog↔code converged). `memory-items` + `audit-partitioned` Cosmos containers live on `spe-cosmos-dev-ai`; memory.write row seeded (`2172b721`). |
| **Status** | Nothing in flight. No agents running. Committing this checkpoint next. |
| **Next Action** | On "continue": if operator has UAT findings (049/069) → intake as fixes/follow-ups; else run **090 close**: doc-drift-audit + deferral VERIFY pass (#612–#619 already filed — verify only) + flip 049/069 on operator sign-off + 090 POML/index close + final merge (probably docs-only PR) + `/repo-cleanup` prompt + `/devops-project-sync` completion promotion. |

### Critical Context (essential)
- **Gates**: 079 G-R2-D **PASS** (`notes/079-g-r2-d-gate-record.md`). **Test-diet DONE, 0 delete candidates** (`notes/test-diet-report.md` — FR-B09 artifact; 1 AMBIGUOUS with KEEP recommendation). ADR-042 authored (Proposed→Accepted at G-R2-B; concise + full + INDEX + CHANGELOG).
- **Deferrals**: PE-D1..D8 filed as GitHub **#612–#619** (two-write rule satisfied; 090 pass VERIFIES not files). **#616 = HIGH security project (retrieval ACL + row-level memory read; operator-HELD)**. **#621 = pre-existing env defect** (session-cleanup GET-after-DELETE 500) — fails Changed-Surface smoke on EVERY AI-surface PR; adjudicated per-PR by both projects; root-cause via App Insights QUEUED.
- **Operator UAT recipe (memory wave)**: record-bound chat = ribbon `EntityFormLaunch` OR append `&entityType=sprk_matter&entityId={guid}` to the SpaarkeAi URL (main.tsx:420 reads it). Script: chat regression → capture (silent) → recall in new session → supersession → non-matter record → review/delete via `GET/DELETE /api/memory/user` (API-only in r2, no client UI).
- **Shared-env notes from compose-r2 (#626)**: they fixed `Seed-PlaybookConsumers.ps1` nav-prop casing bug (was 400-ing ALL action-bound binding seeds) + added Compose disposition optionset value 100000006 to dev (tooling-debt: `Deploy-AiCatalogSchemaExtensions.ps1` should carry it for fresh envs — THEIR log). Compose activation rows (5 actions + 5 consumers) now live.
- **Optional operator items parked**: legacy `audit` copy-forward (leave-legacy sanctioned; procedure in `infrastructure/cosmos/audit-container-policy.json`); 2 residual client-TS JSDoc mentions of retired tools (cosmetic); scope-model catalog re-canonicalization via `Refresh-ScopeModelIndex.ps1` (now that rows are inactive, a re-run reconciles format); PE-D4 Fork-C decision; task 049 create-matter seed (DEF-003, 7-step recipe in `notes/jps/create-matter-binding-row-pending-seed.json`).
- **Fable session**; sub-agents can't write `.claude/`; main session consolidates + commits.

---

## Session ledger (2026-07-10, this session — post-M2 compact)
1. **051** ✅ main-session (test-only envelope contracts) → `a0516ff86`.
2. **M2 5-agent batch** ✅ (052 057 062 064 017) + consolidation fix (subject-key normalization at store chokepoint) → `e789e9f28`.
3. **053 Binder convergence** ✅ (main-session design `notes/053-implementation-design.md` + opus impl; bytes-pinned cutover; OrchestratorPromptBuilder deleted) → `e4889189f`.
4. **Operator sequence**: merge-from-master → push → **PR #620 MERGED** → **BFF deployed** (hash-verified) + `memory-items` container created + memory.write row seeded → preliminary UAT unblocked. Deferrals #612–#619 filed at push audit; #621 filed after canary.
5. **M3** ✅ (054 budgets binding + breach-fails-eval; 056 AggregateFreshnessPolicy) → **PR #622 MERGED**.
6. **065 ADR-042** ✅ (Fable main-session, .claude writes) → **PR #623 MERGED**.
7. **Tranche H batch** ✅ (070 harness, 071 eval-gate closure, 072 zero-forks, 073 hygiene incl. 8 live settings removed + catalog applied, 076 orphans closed) → in **PR #624 MERGED** together with **074** (audit re-key → `audit-partitioned`, container created, account typo fixed) + **075** (retirement executed, −4,792 net lines, verdict+execution two-stage).
8. **079 gate PASS** + **test-diet (0 candidates)** → **PR #625 MERGED**.
9. **Deploy coordination with compose-r2** (operator-directed): they re-merged master, **merged #626**, redeployed BOTH surfaces; core then **deactivated the 3 retired rows** (verified Inactive; healthz 200). Handoffs exchanged in both projects' notes.

## Verification status (as of checkpoint)
Full suite 8089/0 at last local run (pre-#626 merge; #626 gate was compose's, 666/666 on the shared bands + CI green). Publish 46.57 MB (070 harness). Eval gate green on every PR. healthz 200 post-row-flip.

---

*Generated by context-handoff 2026-07-10 evening. Resume: "continue" → UAT intake or 090 close.*
