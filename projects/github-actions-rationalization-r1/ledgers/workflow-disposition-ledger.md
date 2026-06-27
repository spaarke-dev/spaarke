# Workflow Disposition Ledger — github-actions-rationalization-r1

> **Date**: 2026-06-01
> **Phase**: 2 (Rationalization) — task 020 produces this ledger; tasks 021 + 022 execute the dispositions
> **Tasks driving execution**: 021 (deploy-* dispositions), 022 (non-deploy dispositions)
> **Acceptance**: FR-06 — total workflow count drops to ≤8 (from 13)
> **Companion artifacts**:
> - `baseline/workflow-inventory-2026-06-01.md` (Wave A — the input audit)
> - `decisions/D-NN-disposition-*.md` (Wave C records D-04 through D-10) + `decisions/D-03-nightly-and-weekly-quality-disposition.md` (Wave B record)

---

## Summary table

All 13 currently-existing workflows are listed below for full traceability. The 7 P5 workflows in scope for THIS task (020) are D-04 through D-10; D-03 (Wave B) covers the nightly + weekly quality pair.

| Workflow | Recommendation | Decision Record | Resulting count change |
|---|---|---|---|
| adr-audit.yml | KEEP (not in P5; 88.5% success) | n/a — Wave A inventory KEEP | 0 |
| auto-add-to-project.yml | DELETE | [D-10](../decisions/D-10-disposition-auto-add-to-project.md) | −1 |
| deploy-bff-api.yml | KEEP (consolidation target for slot-swap) | [D-04](../decisions/D-04-disposition-deploy-bff-api.md) | 0 |
| deploy-infrastructure.yml | KEEP (FIX applied in Wave B) | n/a — Wave B fix in Phase 1 task 012b | 0 |
| deploy-office-addins.yml | KEEP (65.2% success — evidence-of-value override of D-03) | [D-07](../decisions/D-07-disposition-deploy-office-addins.md) | 0 |
| deploy-platform.yml | DELETE | [D-05](../decisions/D-05-disposition-deploy-platform.md) | −1 |
| deploy-promote.yml | KEEP (FIX applied in Wave B; artifact contract verified) | n/a — Wave B fix; see [D-02](../decisions/D-02-deploy-promote-artifact-contract-verified.md) | 0 |
| deploy-slot-swap.yml | CONSOLIDATE → deploy-bff-api.yml | [D-06](../decisions/D-06-disposition-deploy-slot-swap.md) | −1 (file deleted; functionality already in deploy-bff-api.yml) |
| insights-eval.yml | DELETE | [D-09](../decisions/D-09-disposition-insights-eval.md) | −1 |
| nightly-quality.yml | DELETE | [D-03](../decisions/D-03-nightly-and-weekly-quality-disposition.md) | −1 |
| provision-customer.yml | DELETE | [D-08](../decisions/D-08-disposition-provision-customer.md) | −1 |
| sdap-ci.yml | KEEP (Risk R1 DEFERRED per NFR-01) | [D-01](../decisions/D-01-master-ci-failure-disposition.md) | 0 |
| weekly-quality.yml | DELETE | [D-03](../decisions/D-03-nightly-and-weekly-quality-disposition.md) | −1 |

### Subtotals

| Bucket | Count |
|---|---|
| **Subtotal: current (pre-Phase-2)** | **13** |
| Deletes from D-03 (nightly + weekly) | −2 |
| Deletes from D-05 (deploy-platform) | −1 |
| Consolidate from D-06 (deploy-slot-swap → deploy-bff-api) | −1 |
| Deletes from D-08 (provision-customer) | −1 |
| Deletes from D-09 (insights-eval) | −1 |
| Deletes from D-10 (auto-add-to-project) | −1 |
| **Subtotal: post-Phase-2** | **6** |
| New workflows added in Phase 3 (workflows-validate.yml — FR-07) | +1 |
| New workflows added in Phase 4 (report-workflow-health.yml — FR-11) | +1 |
| **Final count** | **8** |
| **FR-06 target ≤8?** | **YES — exact target met** |

### Math check

13 (current) − 2 (D-03 nightly+weekly) − 1 (D-05) − 1 (D-06) − 1 (D-08) − 1 (D-09) − 1 (D-10) + 2 (new) = **8 workflows** ✅

---

## Execution mapping

### Task 021 — deploy-* dispositions

Task 021 executes the deploy-related portions of this ledger:

| Workflow | Action | Decision record |
|---|---|---|
| deploy-bff-api.yml | no-action — KEEP as-is in tree | D-04 |
| deploy-platform.yml | `git rm .github/workflows/deploy-platform.yml` + commit citing D-05 | D-05 |
| deploy-slot-swap.yml | `git rm .github/workflows/deploy-slot-swap.yml` + commit citing D-06 (consolidate-by-delete; no merge of logic into deploy-bff-api.yml, since deploy-bff-api.yml is functionally a superset for the in-use prod-only deploy path) | D-06 |
| deploy-office-addins.yml | no-action — KEEP as-is | D-07 |
| provision-customer.yml | `git rm .github/workflows/provision-customer.yml` + commit citing D-08 (provision-customer is operator-driven manual provisioning, deploy-adjacent — falls into task 021's deploy-* scope) | D-08 |

**Task 021 total deletions**: 3 files (deploy-platform, deploy-slot-swap, provision-customer). 2 KEEP no-actions.

### Task 022 — non-deploy dispositions

Task 022 executes the non-deploy portions of this ledger:

| Workflow | Action | Decision record |
|---|---|---|
| nightly-quality.yml | `git rm .github/workflows/nightly-quality.yml` + commit citing D-03 | D-03 (Wave B) |
| weekly-quality.yml | `git rm .github/workflows/weekly-quality.yml` + commit citing D-03 | D-03 (Wave B) |
| insights-eval.yml | `git rm .github/workflows/insights-eval.yml` + commit citing D-09 (speak-now mention to spaarke-dev: see D-09 §"Owner sign-off note") | D-09 |
| auto-add-to-project.yml | `git rm .github/workflows/auto-add-to-project.yml` + commit citing D-10 | D-10 |

**Task 022 total deletions**: 4 files (nightly-quality, weekly-quality, insights-eval, auto-add-to-project).

### Phase 3 + 4 additions (not in scope for tasks 021/022)

- **Phase 3 task 030**: author `.github/workflows/workflows-validate.yml` (`actionlint` pre-merge validation per FR-07)
- **Phase 4 task 040**: author `.github/workflows/report-workflow-health.yml` (weekly workflow-health report per FR-11)

These are NEW workflow authoring tasks, not dispositions of existing files — they are referenced here only to make the final count math complete.

---

## Critical preservation notes

1. **ADR-029 hash-verify + slot-swap rollback**: MUST be preserved in `deploy-bff-api.yml` (the surviving BFF deploy workflow per D-04). Verification: `deploy-bff-api.yml` already implements (a) explicit `rollback` job with `if: failure()`, (b) staging-slot deploy → health check → swap → prod-health verify → rollback-swap-back, (c) `/healthz` checks at staging AND production. No additional preservation work required in task 021. **Consolidation merge effort from deploy-slot-swap.yml: ZERO additional edits to deploy-bff-api.yml** (per D-06 plan).
2. **Multi-env resolve-config (currently in deploy-slot-swap.yml)**: NOT preserved at consolidation. The `STAGING_*` and `PROD_*` secrets the resolve-config job references are not currently populated; only `prod` is actively deployed. Recoverable from git history (`git show 0bbebe73:.github/workflows/deploy-slot-swap.yml`) if future multi-env work needs it.
3. **Phase 3 must run actionlint locally** on any new workflow file before push (NFR-08 + project CLAUDE.md). When `deploy-bff-api.yml` eventually receives the multi-env or hash-verify enhancements, the same actionlint-before-push rule applies.
4. **D-09 (insights-eval) speak-now**: The 2026-05-28 commit `ecbfc17e` is within 14 days of this ledger date (2026-06-01 — 4 days). Per the project POML `<notes>` block, the author (spaarke-dev) should have a chance to surface objection before task 022 executes the deletion. The default action remains DELETE; the author can re-add from git history when Insights PR cadence justifies it.
5. **The 4 KEEP workflows** that survive post-Phase-2 are: `adr-audit.yml`, `deploy-bff-api.yml`, `deploy-infrastructure.yml` (Wave-B-fixed), `deploy-office-addins.yml`, `deploy-promote.yml` (Wave-B-fixed), and `sdap-ci.yml`. That is 6 — combined with the 2 new Phase 3+4 additions = 8 final.

---

## Cross-reference

- Wave A inventory: [`baseline/workflow-inventory-2026-06-01.md`](../baseline/workflow-inventory-2026-06-01.md)
- Decision records: [`decisions/`](../decisions/)
- Spec FR-06: [`spec.md` § Functional Requirements](../spec.md)
- Design D-03 / D-04: [`design.md` §4 Locked Decisions](../design.md)
- Project CLAUDE.md (NFR-08 binding): [`CLAUDE.md`](../CLAUDE.md)

---

*Per NFR-06, every DELETE and CONSOLIDATE recommendation in this ledger has a corresponding decision record with ≥1 paragraph of rationale. Per NFR-04, all executions in tasks 021/022 use `git rm` + commit, NOT comment-out-and-leave.*
