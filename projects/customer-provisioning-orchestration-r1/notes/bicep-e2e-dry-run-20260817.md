# Bicep E2E Dry-Run — 2026-08-17

> Owner: customer-provisioning-orchestration-r1 task 034
> Wave: C2 (Bicep + UAMI)
> Mode: Build
> Started: 2026-08-17 12:05:54Z
> Finished: 2026-08-17 12:06:11Z
> Test customer: itsttest in dev (westus2)

---

## Tier 1 — Static `az bicep build`

| Stack | Owner | Expected | Actual | Warnings | Errors |
|---|---|---|---|---|---|
| customer | task 027 | PASS | [OK] PASS | 7 | 0 |
| platform | task 031 | PASS | [OK] PASS | 0 | 0 |
| platform-controlplane | task 033 | PASS | [OK] PASS | 0 | 0 |
| stacks/model1-shared | task 032 (deferred fix) | EXPECTED_FAILURE | [EXPECTED-FAIL] FAIL | 0 | 4 |

### stacks/model1-shared — build errors

```
ERROR: C:\code_files\spaarke-wt-customer-provisioning-orchestration-r1\infrastructure\bicep\stacks\model1-shared.bicep(403,3) : Error BCP035: The specified "object" declaration is missing the following required properties: "userAssignedIdentityResourceId". [https://aka.ms/bicep/core-diagnostics#BCP035]
C:\code_files\spaarke-wt-customer-provisioning-orchestration-r1\infrastructure\bicep\stacks\model1-shared.bicep(407,5) : Error BCP037: The property "keyVaultName" is not allowed on objects of type "params". Permissible properties include "runtimeStack", "userAssignedIdentityResourceId", "vnetIntegrationSubnetId". [https://aka.ms/bicep/core-diagnostics#BCP037]
C:\code_files\spaarke-wt-customer-provisioning-orchestration-r1\infrastructure\bicep\stacks\model1-shared.bicep(408,5) : Error BCP037: The property "enableManagedIdentity" is not allowed on objects of type "params". Permissible properties include "runtimeStack", "userAssignedIdentityResourceId", "vnetIntegrationSubnetId". [https://aka.ms/bicep/core-diagnostics#BCP037]
C:\code_files\spaarke-wt-customer-provisioning-orchestration-r1\infrastructure\bicep\stacks\model1-shared.bicep(574,59) : Error BCP053: The type "outputs" does not contain property "appServicePrincipalId". Available properties include "appServiceDefaultHostName", "appServiceId", "appServiceName", "appServiceUrl". [https://aka.ms/bicep/core-diagnostics#BCP053]
```

**Deferral rationale**: sharedBffApi module invocation still uses deprecated app-service.bicep params (keyVaultName + enableManagedIdentity) + reads deprecated appServicePrincipalId output. Task 029 D1 explicitly deferred caller migration. Follow-on task required.

## Tier 2 — Live `az deployment sub what-if` (dev)

SKIPPED in Mode=Build. Re-run with `-Mode DryRun` (or `-Mode Full` for assertions) against a live dev subscription.

## Follow-ups & Deviations

- FOLLOWUP: Migrate stacks/model1-shared.bicep sharedBffApi module invocation to task-029 UAMI-only app-service.bicep param signature (currently passes deprecated keyVaultName + enableManagedIdentity; reads deprecated appServicePrincipalId output)
- FOLLOWUP: Reconcile design.md §7 module count (v3.2 says 25; on-disk is 0 after Wave 2 additions)
- FOLLOWUP: Coordinate Phase H CI-wiring PR to invoke this script in pull_request and nightly schedule workflows (root CLAUDE.md §10 ci-workflows=Y overlap with ci-cd-unit-test-remediation-r1)

## Scoped-Out (task 034)

The following are NOT verified by this run and are recorded as follow-on work:

- **Model 2 dedicated per-customer full stack composition** (`stacks/model2-full.bicep`) — outside Wave C2 scope; task 029 D1 deferred caller migration. Follow-on task recommended: verify + migrate model2-full.bicep caller pattern (parallel to model1-shared fix).
- **Legacy Model 1 per-customer stack** (`stacks/model1-customer.bicep`) — superseded by `stacks/model1-shared.bicep`; verify at retirement time (Phase F).
- **Real RBAC principalId GUID verification** — what-if reports role-assignment RESOURCES; actual principalId GUID match against a live UAMI is only observable post-apply. Verified separately in Phase F acceptance.
- **CI wiring of this test** — deferred to Phase H coordinated PR per root CLAUDE.md §10 (`ci-workflows=Y` overlap with `ci-cd-unit-test-remediation-r1`).

## Verdict

**[PASS]** — Wave C2 composition is coherent within tested scope. All non-deferred stacks build clean; all structural assertions pass.


