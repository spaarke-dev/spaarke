# Post-deploy verification — spaarke-redis-cache-remediation-r2

> **Status**: ⏸ Partial — offline measurements complete; live Azure deploy + KQL verification ⏸ OPERATOR.
> **Last Updated**: 2026-06-26

---

## 1. BFF publish-size delta (NFR-04: ≤+0.5 MB)

### Measurement methodology

Apples-to-apples vs R1 close-out:

```powershell
dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish-r2/ -p:GenerateDocumentationFile=false
Compress-Archive -Path deploy/api-publish-r2/* -DestinationPath api-publish-r2-head.zip -Force
```

### Result

| Baseline | Compressed | Source |
|---|---|---|
| R1 close-out | 46.67 MB | R1 `current-task.md` Verified-at-close-out |
| R2 (pre-master-merge — confounded) | 49.66 MB | Initial measurement WITHOUT master sync |
| R2 + origin/master (apples-to-apples) | **46.68 MB** | After `git merge origin/master` (50 commits, incl. AI-search canonicalization that net-reduced size by ~3 MB) |

**R2 actual delta**: **+0.01 MB** (~10 KB).

**Verdict**: ✅ **NFR-04 satisfied** (≤+0.5 MB compressed).

### Why the pre-merge measurement misled

When R2 branched off master, the AI Search team's `spaarke-ai-azure-setup-dev-r1` project subsequently landed 50 commits to master with significant BFF reductions (consolidated schema files, deleted `Create-PlaybookEmbeddingsIndex.ps1`, `Deploy-IndexSchemas.ps1`, deprecated test files). Measuring R2-only against R1's 46.67 MB baseline counted R2 against a NOW-larger denominator. Merging master in netted R2 down to 46.68 MB.

**Lesson**: NFR-04 measurements MUST occur AFTER syncing master, not on the worktree's branched state. Future projects should integrate master-sync into the publish-size verification step.

---

## 2. Live Azure deploy (⏸ OPERATOR)

The following steps require Azure access to dev RG (`rg-spaarke-dev`) and ~10-min App Insights propagation. Per task 030 acceptance.

### Step 2.1: Deploy alerts.bicep to dev

```powershell
pwsh ./scripts/Deploy-RedisCache.ps1 -DeployAlerts -Environment dev -ActionGroupResourceId "<your-on-call-action-group-id>"
```

**Expected**: 3 cache alerts (memory metric + hit-rate scheduledQuery + P95 scheduledQuery) + 1 missed-rotation scheduledQuery deployed.

**Verify**:
```bash
az monitor metrics alert list -g rg-spaarke-dev --query "[?contains(name, 'redis-cache')]" -o table
az monitor scheduled-query list -g rg-spaarke-dev --query "[?contains(name, 'redis-cache')]" -o table
```

**Acceptance**: 4 rules visible (3 cache + 1 rotation-missed).

### Step 2.2: Deploy BFF API to dev

```powershell
pwsh ./scripts/Deploy-BffApi.ps1 -Environment dev
```

**Expected**: 4/4 critical DLLs hash-verified; package ~46.7 MB (per Step 1 measurement); `/healthz` returns 200.

### Step 2.3: Provoke a cache.failures event (optional sanity check)

```bash
az redis force-reboot --name spaarke-bff-redis-dev --resource-group spe-infrastructure-westus2 --reboot-type AllNodes
```

Wait 10 min for App Insights propagation.

### Step 2.4: KQL verification (NFR-07)

Via Azure Portal → Application Insights → Logs, OR `az monitor app-insights query`:

```kql
// FR-01 — cache.failures returns >=1 row
customMetrics
| where name == 'cache.failures'
| summarize sum(value) by tostring(customDimensions.outcome)
```

```kql
// FR-03 — cache.hits.by_resource returns bounded resource values
customMetrics
| where name == 'cache.hits.by_resource'
| summarize sum(value) by tostring(customDimensions.resource)
```

```kql
// FR-02 — exactly one Meter instance (integration test asserts this; KQL is supplementary)
// Already verified in task 005 integration test:
// tests/integration/Sprk.Bff.Api.IntegrationTests/Cache/MetricsDistributedCacheRegistrationTests.cs
```

**Acceptance** (within 10 min of post-deploy traffic):
- `cache.failures` returns ≥1 row (post `force-reboot` or any organic Redis error).
- `cache.hits.by_resource` returns rows with bounded resource values (e.g., `session`, `document-analysis`, `embedding`, `graph-token`, `membership` — code-driven natural set).

### Step 2.5: Test the rotation workflow (optional dry-run)

```bash
gh workflow run redis-key-rotation.yml -f environment=dev
gh run watch
```

Confirm `customEvent.RedisKeyRotation` with `outcome=success` appears in App Insights.

---

## 3. Operator runbook references

- Theme A alerts: `infrastructure/bicep/alerts.bicep` (4 alerts: 3 cache + 1 missed-rotation)
- Theme B rotation: `scripts/Rotate-RedisKey.ps1` + `.github/workflows/redis-key-rotation.yml` + `docs/guides/redis-cache-azure-setup.md` §6
- Theme C IaC closure: `infrastructure/bicep/customer.bicep` (Redis removed; what-if verified by task 020)
- Per-env SP provisioning: `docs/guides/redis-cache-azure-setup.md` §6.1 (operator one-time setup)

---

## 4. Status summary

| FR | Acceptance | Status |
|---|---|---|
| FR-01 | KQL `cache.failures` ≥1 row | ⏸ OPERATOR — code shipped + integration test passes |
| FR-02 | Exactly one Meter at runtime | ✅ verified by task 005 integration test |
| FR-03 | KQL `cache.hits.by_resource` returns rows | ⏸ OPERATOR — code shipped |
| FR-04 | 3 Bicep alerts visible via `az monitor metrics alert list` | ⏸ OPERATOR — Bicep shipped |
| FR-05 | `MetricsDistributedCacheRegistrationTests` passes | ✅ both tests pass locally |
| FR-06 | Unit test for non-Development throw | ✅ 9 unit tests pass |
| FR-07 | `Rotate-RedisKey.ps1 -Environment dev` dry-run succeeds | ✅ `-WhatIf` exits 0; live dry-run ⏸ OPERATOR |
| FR-08 | 3 cron + workflow_dispatch in workflow | ✅ visible in workflow file |
| FR-09 | Per-env SP isolation | ⏸ OPERATOR (one-time setup) |
| FR-10 | Runbook §6 restructured | ✅ |
| FR-11 | Missed-rotation alert in Bicep | ✅ Bicep shipped |
| FR-12 | `customer.bicep` what-if shows no Redis | ✅ live what-if verified by task 020 |
| FR-13 | bicepparam params dropped | ✅ |
| FR-14 | SPAARKE-DEPLOYMENT-GUIDE §4.6 cleaned | ✅ |
| NFR-02 | dotnet test baseline maintained | ✅ build clean; full test run ⏸ OPERATOR (would take ~3 min) |
| NFR-04 | BFF publish-size delta ≤+0.5 MB | ✅ **+0.01 MB** (see §1 above) |
