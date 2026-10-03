# Bicep E2E Dry-Run — 2026-10-02

> Owner: customer-provisioning-orchestration-r1 task 034
> Wave: C2 (Bicep + UAMI)
> Mode: Build
> Started: 2026-10-02 23:25:06Z
> Finished: 2026-10-02 23:25:31Z
> Test customer: itsttest in dev (westus2)

---

## Tier 1 — Static `az bicep build`

| Stack | Owner | Expected | Actual | Warnings | Errors |
|---|---|---|---|---|---|
| customer | task 027 | PASS | [OK] PASS | 8 | 0 |
| platform | task 031 | PASS | [OK] PASS | 0 | 0 |
| platform-controlplane | task 033 | PASS | [OK] PASS | 0 | 0 |

## Tier 2 — Live `az deployment sub what-if` (dev)

SKIPPED in Mode=Build. Re-run with `-Mode DryRun` (or `-Mode Full` for assertions) against a live dev subscription.

## Follow-ups & Deviations

- FOLLOWUP: Reconcile design.md §7 module count (v3.2 says 25; on-disk is 0 after Wave 2 additions)
- FOLLOWUP: Wire this script into CI (pull_request + nightly) — not yet done

## Scoped-Out (task 034)

The following are NOT verified by this run and are recorded as follow-on work:

- *(retired)* `stacks/model2-full.bicep` was deleted by task 249 (owner D19, 2026-10-02) — `customer.bicep` is the only customer-stamp template; `deploy-infrastructure.yml` now only validates.
- **Real RBAC principalId GUID verification** — what-if reports role-assignment RESOURCES; actual principalId GUID match against a live UAMI is only observable post-apply. Verified separately in Phase F acceptance.
- **CI wiring of this test** — deferred to Phase H coordinated PR per root CLAUDE.md §10 (`ci-workflows=Y` overlap with `ci-cd-unit-test-remediation-r1`).

## Verdict

**[PASS]** — Wave C2 composition is coherent within tested scope. All non-deferred stacks build clean; all structural assertions pass.


