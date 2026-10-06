# Redis Cache — Azure Setup & Operational Guide

> **Last Updated**: 2026-10-05 (T242b: Azure Managed Redis, Microsoft Entra only)
> **Audience**: BFF operators, infrastructure engineers
> **Status**: Authoritative

This guide is the canonical operational reference for provisioning, cutting over, validating, rolling back and decommissioning the Spaarke BFF Redis cache (`spaarke-bff-redis-{env}`) in any environment. A fresh operator should be able to provision a new environment's Redis end-to-end in under 30 minutes by following only this document (per FR-19, Success Criterion #6).

**Every Spaarke Redis is Azure Managed Redis (`Microsoft.Cache/redisEnterprise`), Microsoft Entra only** (customer-provisioning-orchestration-r1 tasks T242 / T242b, owner decisions D12 / D13). Access keys are disabled on the database: there is no key, no connection string and no Key Vault secret. Each client signs in with its managed identity, which must hold an access-policy assignment on the cache. Classic Azure Cache for Redis (`Microsoft.Cache/Redis`) is no longer provisioned.

For architectural context (tenant isolation, multi-instance behavior, Cache Instance Registry, failure modes), see [`docs/architecture/caching-architecture.md`](../architecture/caching-architecture.md). For the binding constraints, see [`.claude/adr/ADR-009-redis-caching.md`](../../.claude/adr/ADR-009-redis-caching.md) (concise) and [`docs/adr/ADR-009-caching-redis-first.md`](../adr/ADR-009-caching-redis-first.md) (full).

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Provision Command — Per Environment](#2-provision-command--per-environment)
3. [Verification](#3-verification)
4. [Cutover Protocol](#4-cutover-protocol)
5. [Rollback Procedure](#5-rollback-procedure)
6. [Secret Rotation Procedure](#6-secret-rotation-procedure)
7. [Decommission Procedure](#7-decommission-procedure)
8. [Troubleshooting](#8-troubleshooting)
9. [Known Limitation — In-Memory Fallback Mode](#9-known-limitation--in-memory-fallback-mode)
10. [Lessons Learned](#10-lessons-learned)
11. [Cross-References](#11-cross-references)

---

## 1. Prerequisites

### What exists today

| Environment | Cache | Resource group / subscription | High availability | Access-policy assignments |
|---|---|---|---|---|
| dev | `spaarke-bff-redis-dev` — endpoint `spaarke-bff-redis-dev.westus2.redis.azure.net:10000` | `spe-infrastructure-westus2` / "Spaarke Devlopment Environment" `484bc857-3802-427f-9ea5-ca47b43db0f0` | Disabled | `9fd47efb-…` = `mi-bff-api-dev` (the dev BFF `spaarke-bff-dev` runs as it); `38f7693f-…` = `sprk-controlplane-dev-uami` (L2 Worker) |
| demo | `spaarke-bff-redis-demo` | `rg-spaarke-demo` / "Spaarke Demo Environment" `2ff9ee48-6f1d-4664-865c-f11868dd1b50` | Disabled | `eaf9591e-…` = `mi-bff-api-demo`. The demo BFF is stopped pending task T242c. |
| staging / prod | not created yet (no Spaarke staging or prod BFF exists) | `rg-spaarke-staging` / `rg-spaarke-prod` | Enabled | placeholders in the `.bicepparam` — replace before deploying |
| customer stamps | `sprk-{customer}-{env}-redis`, created by [`infrastructure/bicep/customer.bicep`](../../infrastructure/bicep/customer.bicep), not by this guide's script | per-customer stamp | Enabled | the stamp's user-assigned identity |

All caches share one shape, set by [`infrastructure/bicep/modules/redis.bicep`](../../infrastructure/bicep/modules/redis.bicep):

| Property | Value |
|---|---|
| SKU | `Balanced_B0` (size up only on a measured memory metric — Managed Redis has no scale-down) |
| High availability | Chosen at create time only; cannot be changed later |
| Database | `default`, port **10000**, TLS only (`clientProtocol: Encrypted`, minimum TLS 1.2) |
| Clustering policy | **OSSCluster** (immutable; clients must be cluster-aware — StackExchange.Redis is) |
| Eviction | `AllKeysLRU` |
| Authentication | `accessKeysAuthentication: Disabled` — access only through per-identity assignments of the built-in `default` access policy, by principal **object ID** |

### Before running any command in this guide

- **Azure subscription access** with at least Contributor on the target resource group (see the table above).
- **Azure CLI installed and logged in** — verify with `az account show`. Pass the subscription explicitly (`-SubscriptionId` on the scripts, `--subscription` on `az`) rather than running `az account set`: the CLI context is shared by every session on the machine. Demo lives in its own subscription.
- **PowerShell 7+ available** — verify with `pwsh -Version` (must be 7.0 or later). Windows PowerShell 5.1 is NOT supported.
- **The object ID of every identity that needs the cache** is listed in `accessPolicyPrincipalIds` of the environment's `.bicepparam`. With access keys disabled, an identity that is not listed cannot connect (the module refuses an empty list).
- **The BFF runs as a user-assigned managed identity** and its App Service carries that identity's **client ID** in `ManagedIdentity__ClientId` (or `Graph__ManagedIdentity__ClientId`). The BFF refuses to start with `Redis__Endpoint` set and no client ID.
- **The BFF build is T242 or later** (master ≥ `2677d48c0`). Older builds read only `ConnectionStrings__Redis` and refuse to start once it is removed.
- **`spaarke-bff-{env}` App Service exists** in `rg-spaarke-{env}` — verify with `az webapp show -g rg-spaarke-{env} -n spaarke-bff-{env} --query state`.
- **App Settings template loaded** — see [`src/server/api/Sprk.Bff.Api/appsettings.template.json`](../../src/server/api/Sprk.Bff.Api/appsettings.template.json) for the Redis settings shape (`Redis__Enabled`, `Redis__Endpoint`, `Redis__InstanceName`, `Redis__AllowInMemoryFallback`).
- **Bicep module + parameter file present**:
  - [`infrastructure/bicep/modules/redis.bicep`](../../infrastructure/bicep/modules/redis.bicep)
  - [`infrastructure/bicep/parameters/redis-{env}.bicepparam`](../../infrastructure/bicep/parameters/) (`dev`, `demo`, `staging`, `prod`)
- **Validation harness present** — [`tests/manual/RedisValidationTests.ps1`](../../tests/manual/RedisValidationTests.ps1) (used by `Deploy-RedisCache.ps1` after a deploy and by `-VerifyOnly`).

No Key Vault is involved: the BFF setting `Redis__Endpoint` is a plain value (host:port, not a secret).

---

## 2. Provision Command — Per Environment

All Spaarke BFF environments use the same idempotent script: [`scripts/Deploy-RedisCache.ps1`](../../scripts/Deploy-RedisCache.ps1). The script:

- Detects an existing `Microsoft.Cache/redisEnterprise` cache in `Succeeded` provisioning state and skips the template deploy (NFR-01).
- Rejects `prod` and `demo` without `-Force` (NFR-05).
- Supports `-WhatIf` (plan-only, NFR-06) and `-VerifyOnly` (run the validation harness against the existing cache, NFR-06).
- `-CutoverBffSettings` sets `Redis__Enabled=true`, `Redis__InstanceName=spaarke:`, `Redis__Endpoint={host}:10000` and `Redis__AllowInMemoryFallback=false` on `spaarke-bff-{env}` (resource group `rg-spaarke-{env}`). An existing `ConnectionStrings__Redis` is left in place on purpose.
- `-RemoveBffConnectionString` deletes `ConnectionStrings__Redis` and `Redis__ConnectionString` from the BFF. It refuses if the app has no `Redis__Endpoint`. The Key Vault secret the setting referenced is left alone.
- `-SubscriptionId` names the subscription on every `az` call.
- `-DeployAlerts` deploys the alert rules (§8).
- Runs the validation harness after the deploy (with the BFF checks when `-CutoverBffSettings` or `-RemoveBffConnectionString` was passed).

Customer stamps do not use this script: `customer.bicep` creates the stamp's cache (HA on) and sets the stamp BFF's `Redis__Endpoint`.

### Dev

```powershell
pwsh ./scripts/Deploy-RedisCache.ps1 -Environment dev -SubscriptionId 484bc857-3802-427f-9ea5-ca47b43db0f0 -CutoverBffSettings
```

Expected banner:

```
Deploy-RedisCache.ps1 starting
  Environment    : dev
  ResourceGroup  : spe-infrastructure-westus2
  Redis name     : spaarke-bff-redis-dev
  Bicep module   : <repo>/infrastructure/bicep/modules/redis.bicep
  Bicep param    : <repo>/infrastructure/bicep/parameters/redis-dev.bicepparam
  Redis          : spaarke-bff-redis-dev (Azure Managed Redis, Entra only)
  Mode           : deploy
```

To preview without changes, add `-WhatIf`:

```powershell
pwsh ./scripts/Deploy-RedisCache.ps1 -Environment dev -SubscriptionId 484bc857-3802-427f-9ea5-ca47b43db0f0 -WhatIf
```

A create takes about 8 minutes.

### Demo (requires explicit `-Force`)

```powershell
pwsh ./scripts/Deploy-RedisCache.ps1 -Environment demo -Force `
  -SubscriptionId 2ff9ee48-6f1d-4664-865c-f11868dd1b50 -CutoverBffSettings -RemoveBffConnectionString
```

This is the command T242b ran. The demo BFF had no previous cache, so the connection-string removal could run in the same call.

### Staging

Replace the placeholder in [`redis-staging.bicepparam`](../../infrastructure/bicep/parameters/redis-staging.bicepparam) with the staging BFF identity's **object ID** first (the placeholder is deliberately not a GUID, so Azure rejects it), then:

```powershell
pwsh ./scripts/Deploy-RedisCache.ps1 -Environment staging -SubscriptionId <staging-sub> -CutoverBffSettings
```

Default resource group: `rg-spaarke-staging` (override with `-ResourceGroup`). High availability is Enabled.

### Prod (requires explicit `-Force`)

Replace the placeholder in [`redis-prod.bicepparam`](../../infrastructure/bicep/parameters/redis-prod.bicepparam) the same way, then:

```powershell
pwsh ./scripts/Deploy-RedisCache.ps1 -Environment prod -SubscriptionId <prod-sub> -CutoverBffSettings -Force
```

The `-Force` flag is required per NFR-05; without it the script exits with code 2 and an `NFR-05` message. No Spaarke prod BFF exists today (customer production runs on per-customer stamps); deploying this needs a separate owner go/no-go.

### Granting another identity access

Add its object ID to `accessPolicyPrincipalIds` in the `.bicepparam`. Because `Deploy-RedisCache.ps1` skips the template when the cache already exists, apply the change with the deployment the parameter file documents:

```powershell
az deployment group create --subscription <sub> `
  --resource-group <rg> `
  --template-file infrastructure/bicep/modules/redis.bicep `
  --parameters infrastructure/bicep/parameters/redis-{env}.bicepparam
```

Assignments are named after the object ID without dashes, so a re-deploy shows no change for identities already listed. Removing an ID from the list does not remove its assignment (incremental deployment); delete the assignment resource explicitly.

---

## 3. Verification

After a deploy completes, verify each of the following before declaring success.

1. **Cache is in `Succeeded` provisioning state**:

   ```powershell
   az resource show --subscription <sub> -g <rg> -n spaarke-bff-redis-{env} `
     --resource-type Microsoft.Cache/redisEnterprise --query properties.provisioningState
   ```

   Expect: `"Succeeded"`. Use the resource type: during a cutover a classic `Microsoft.Cache/Redis` cache of the same name may still exist.

2. **Validation harness** — a read-only Azure Resource Manager check (no data-plane access):

   ```powershell
   pwsh ./scripts/Deploy-RedisCache.ps1 -Environment {env} -SubscriptionId <sub> -VerifyOnly
   ```

   [`tests/manual/RedisValidationTests.ps1`](../../tests/manual/RedisValidationTests.ps1) checks:
   - the cluster is `Microsoft.Cache/redisEnterprise`, provisioningState `Succeeded`, resourceState `Running`, a `Balanced_*` SKU (high availability is reported);
   - database `default`: access keys disabled, TLS only, `OSSCluster`, port 10000;
   - at least one `default` access-policy assignment; with `-ExpectedPrincipalIds` the set must match exactly;
   - with `-BffAppName`: the app's `Redis__Endpoint` equals `{hostName}:10000`; with `-RequireNoBffConnectionString`, no `ConnectionStrings__Redis` / `Redis__ConnectionString` setting remains.

   `-VerifyOnly` runs only the first three. For the full check, call the harness directly, for example for dev:

   ```powershell
   pwsh tests/manual/RedisValidationTests.ps1 -RedisName spaarke-bff-redis-dev -ResourceGroup spe-infrastructure-westus2 `
     -SubscriptionId 484bc857-3802-427f-9ea5-ca47b43db0f0 `
     -ExpectedPrincipalIds 9fd47efb-7962-492b-ac44-e5ccd0268ebb,38f7693f-e6e2-4a3e-9acf-7f9e29dd4044 `
     -BffAppName spaarke-bff-dev -BffResourceGroup rg-spaarke-dev -RequireNoBffConnectionString
   ```

   Non-zero exit = at least one check failed. Code-level behavior (endpoint → managed identity, the connection-string refusal, the Null-Object kill switch) is covered by `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/CacheModuleTests.cs`, not by this harness.

3. **Restart the BFF** (if the settings changed without a restart):

   ```powershell
   az webapp restart --subscription <sub> -g rg-spaarke-{env} -n spaarke-bff-{env}
   ```

4. **Stream the startup log**:

   ```powershell
   az webapp log tail --subscription <sub> -g rg-spaarke-{env} -n spaarke-bff-{env}
   ```

   Expect, verbatim, the two lines:

   ```
   Distributed cache: Redis enabled with instance name 'spaarke:'
   Distributed cache: Redis authentication is managed identity (Microsoft Entra)
   ```

   The log MUST NOT contain an in-memory fallback warning. The container log does not always retain startup lines; step 6 is the decisive check.

5. **Health check returns 200**:

   ```powershell
   curl -i https://spaarke-bff-{env}.azurewebsites.net/healthz
   ```

   Expect HTTP `200 OK`.

6. **App Insights — Redis traffic reaches the new cache** (REQUIRED). After about 10 minutes of traffic (exercise the BFF, e.g. open a chat session from a code page), run:

   ```bash
   # Redis dependencies by target — expect the new host:10000 plus node IP:85xx, and zero failures
   az monitor app-insights query \
     --app spe-insights-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 \
     --analytics-query "dependencies | where timestamp > ago(30m) | where type contains 'redis' | summarize calls=count(), failures=countif(success == false), last=max(timestamp) by target"

   # Custom cache metrics — expect cache.hits, cache.misses, cache.redis_call_duration_ms
   az monitor app-insights query \
     --app spe-insights-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 \
     --analytics-query "customMetrics | where timestamp > ago(10m) | where name startswith 'cache.' | summarize total=sum(value), records=count() by name"
   ```

   With the OSS clustering policy, calls appear under the endpoint (`spaarke-bff-redis-{env}.westus2.redis.azure.net:10000`) and under the node address (`<node IP>:85xx`). Because the cache accepts only Microsoft Entra, successful calls also prove managed-identity authentication. No calls should target the old `*.redis.cache.windows.net` host.

   **Common failure modes** (wiring per ADR-009 §9: `UseAzureMonitor()` + `AddRedisInstrumentation()` + `RedisCacheOptions.ConnectionMultiplexerFactory`):
   - `customMetrics` query empty → `UseAzureMonitor()` not wired in `Program.cs` (still using classic `AddApplicationInsightsTelemetry()`)
   - `dependencies` query empty even though custom metrics flow → `RedisCacheOptions.ConnectionMultiplexerFactory` not wired in `CacheModule.cs` (DI-registered multiplexer is idle; `Microsoft.Extensions.Caching.StackExchangeRedis` built its own internal one)
   - Both queries empty → either `APPLICATIONINSIGHTS_CONNECTION_STRING` not set on the BFF App Service, or the exporter package (`Azure.Monitor.OpenTelemetry.AspNetCore`) is missing from `Sprk.Bff.Api.csproj`

---

## 4. Cutover Protocol

This is the sequence T242b used to move the dev BFF from a classic key-based cache to Azure Managed Redis with no outage. It applies to any environment still on a classic `Microsoft.Cache/Redis` cache.

1. **Create the new cache** via §2. The name may differ from the old one or be the same: Azure accepted `spaarke-bff-redis-dev` for the new cluster while the old `Microsoft.Cache/Redis` cache of the same name still existed (different resource types, different DNS zones — `*.redis.azure.net` vs `*.redis.cache.windows.net`).
2. **Grant access** — the BFF's identity (and any other client, such as the L2 Worker) is in `accessPolicyPrincipalIds`; confirm with the harness (§3 step 2, `-ExpectedPrincipalIds`).
3. **Set the endpoint** — `Deploy-RedisCache.ps1 -Environment {env} -SubscriptionId <sub> -CutoverBffSettings`. The BFF keeps its old `ConnectionStrings__Redis`: a T242+ build uses `Redis__Endpoint` when both are set, while an older build still reads the connection string and stays on the old cache.
4. **Deploy a BFF build that has T242** (master ≥ `2677d48c0`), per the BFF deploy procedure.
5. **Verify** per §3 — in particular step 6: App Insights `redis` dependencies target the new host (endpoint host:10000 plus node IP:85xx), zero failures, none to the old host.
6. **Only then remove the connection string** — `Deploy-RedisCache.ps1 -Environment {env} -SubscriptionId <sub> -RemoveBffConnectionString`. The harness then confirms no connection-string setting remains. In a shared environment, do this only once every build deployed there is T242+: **any BFF build older than T242 refuses to start once the connection string is gone.** The Key Vault secret `Redis-ConnectionString` is left in place, unreferenced.
7. **Delete the old cache** per §7, with owner approval.

**Cache contents are not migrated.** The new cache starts cold and fills from cache misses; expect a short period (typically minutes) of elevated latency. Spaarke cache entries are regenerable by design.

---

## 5. Rollback Procedure

If post-cutover verification fails (`/healthz` returns non-200, the BFF does not start, App Insights shows Redis failures, or P95 latency is unacceptable):

1. **Fix forward first.** Most failures are an identity missing from the access policy, a missing `ManagedIdentity__ClientId`, or a wrong endpoint — see §8 "Connection failures". The new cache can stay provisioned; idempotent re-deploys are safe (NFR-01).
2. **Before step 6 of §4** (the connection string is still set): redeploy the previous BFF build. A pre-T242 build ignores `Redis__Endpoint` and reads `ConnectionStrings__Redis`, so it runs on the old cache again.
3. **After step 6 but before the old cache is deleted**: a T242+ build cannot use a connection string outside Development/Testing, so going back to the old cache means restoring the setting AND redeploying a pre-T242 build:

   ```powershell
   az webapp config appsettings set --subscription <sub> `
     -g rg-spaarke-{env} -n spaarke-bff-{env} `
     --settings ConnectionStrings__Redis='@Microsoft.KeyVault(VaultName=<kv>;SecretName=Redis-ConnectionString)'
   ```

   The Key Vault secret still exists (secrets are never deleted).
4. **After the old cache is deleted** there is nothing to roll back to — fix forward.

Investigate the root cause, then re-attempt the cutover when ready.

---

## 6. Secret Rotation Procedure

**There is nothing to rotate.** Every Spaarke Redis — dev, demo and every customer stamp — is Azure Managed Redis with access keys disabled: no key, no connection string, no Key Vault secret in use. The BFF and the L2 Worker sign in with their managed identities, whose tokens Azure issues and refreshes automatically. Access is changed by editing the access-policy assignments (§2 "Granting another identity access"), not by rotating anything.

The key-rotation tooling — `scripts/Rotate-RedisKey.ps1`, `.github/workflows/redis-key-rotation.yml` and the missed-rotation alert — was **removed on 2026-10-05** (task 242b): it could only operate a classic key-based `Microsoft.Cache/Redis` cache, none exists, ADR-009 forbids a Redis key in Key Vault, and a current BFF refuses a connection string outside Development/Testing. Its scheduled runs had all failed since 2026-07-01. The dev vault's old `Redis-ConnectionString` secret stays in place, unreferenced (Key Vault secrets are never deleted).

---

## 7. Decommission Procedure

After the verification window has passed without regressions (dev used the same day; staging/prod per the environment's go/no-go):

1. **Get owner approval.** Old `Microsoft.Cache/Redis` caches are deleted only with explicit owner approval.
2. **Delete with the type-specific command.** The old and new caches may share a name (dev did), so never delete by name alone through a generic command:

   ```powershell
   az redis delete --subscription <sub> -g <rg> -n <legacy-redis-name> --yes
   ```

   `az redis` addresses only `Microsoft.Cache/Redis`; confirm with §3 step 1 afterwards that the `Microsoft.Cache/redisEnterprise` cache is untouched.

   Where historical data may be needed for audit, tag for delayed deletion instead:

   ```powershell
   az resource tag --tags decommission=YYYY-MM-DD `
     -g <rg> -n <legacy-redis-name> `
     --resource-type Microsoft.Cache/Redis
   ```

3. **Never delete Key Vault secrets.** `Redis-ConnectionString` stays in place, unreferenced.
4. **Record the decommission** in the project's task notes: date, method (delete vs. tag), tag value if tagged, operator, and the owner approval.

---

## 8. Troubleshooting

### Connection failures (BFF cannot reach Redis)

**Symptoms**: the BFF does not start (`Failed to connect to Redis at startup …`, or a configuration error naming `Redis__Endpoint`); `/healthz` returns 503; App Insights shows failed `redis` dependencies.

**Common causes** (Microsoft Entra only):

1. **Identity not in the access-policy list** — authentication error at connect. Run the harness with `-ExpectedPrincipalIds` (§3 step 2); add the identity's **object ID** per §2 "Granting another identity access".
2. **No managed-identity client ID** — `'Redis__Endpoint' is set but no managed-identity client id is configured`. Set `ManagedIdentity__ClientId` (or `Graph__ManagedIdentity__ClientId`) to the user-assigned identity's **client ID** (not its object ID), and confirm that identity is attached to the App Service.
3. **BFF build older than T242 with no connection string** — the old build reads only `ConnectionStrings__Redis` and refuses to start. Deploy a T242+ build (master ≥ `2677d48c0`).
4. **Connection string instead of endpoint** — a T242+ build outside Development/Testing refuses to start when `Redis__Endpoint` is unset (`A Redis connection string … is accepted only in Development and Testing`). Run `-CutoverBffSettings`.
5. **Wrong port** — Managed Redis listens on **10000**, not 6380. `Redis__Endpoint` is `host:10000` and must not contain a password or user.
6. **Network path blocked** — with the OSS clustering policy the client connects to the endpoint on 10000 and then directly to the node ports **85xx**; outbound traffic to both must be allowed (check NSG / VNet integration rules, and DNS resolution of `*.redis.azure.net`).
7. **Cache down** — `az resource show … --resource-type Microsoft.Cache/redisEnterprise --query properties.provisioningState` (§3 step 1). If `Failed`, re-run the provision command.

The L2 Worker uses the same pattern: `Redis__Endpoint` plus its user-assigned identity (`src/server/services/Sprk.Provisioning.ControlPlane.Core/Dispatch/DispatchModule.cs`); outside Development/Testing it refuses a connection string the same way.

### Latency spikes (average call latency > 100 ms sustained)

**Symptoms**: the average of the `cache.redis_call_duration_ms` histogram exceeds 100 ms for sustained windows (§8 Alert 2); user-visible BFF latency increases.

**Common causes**:

1. **SKU undersize** — check the `usedmemorypercentage` Azure Monitor metric on the cache. If it is sustained above 70%, scale up via `skuName` in `infrastructure/bicep/parameters/redis-{env}.bicepparam` (`Balanced_B0` → `B1` → `B3` …) and redeploy with `az deployment group create` (§2). There is no scale-down.
2. **Network issue** — check Azure Service Health for `Cache` in the deployment region. If a regional incident is active, ride it out (auto-mitigates).
3. **Hot keys** — a small set of keys receives disproportionate traffic. Identify via the App Insights dependency call breakdown by operation name. Consider sharding the hot key by tenant or adding a brief client-side cache for the specific resource.
4. **Client-side connection pool exhaustion** — symptom: BFF-side P95 is high while the cache's own metrics are normal. Check `StackExchange.Redis.ConnectionMultiplexer` configuration; verify thread-pool sizing on the App Service plan.

### Hit-rate degradation (`cache.hit_rate < 80%` sustained)

**Symptoms**: App Insights `cache.hit_rate` custom metric drops below 80% for sustained windows.

**Common causes**:

1. **TTL too short** — a recently-tuned TTL evicts entries before normal re-access. Check `RedisCacheOptions` and `IDistributedCache.Set*` TTL values vs. prior versions.
2. **Key drift** (version mismatch) — a recent deploy wrote keys without the `:v{n}` suffix or with the wrong version. Check `git log` on `CacheKeys.cs` and any `IDistributedCache.Set*` callsites in the last 24 hours.
3. **Tenant ID computation bug** — a code path bypassed the `tenant:{tenantId}:` prefix derivation. Grep the BFF for direct `IDistributedCache` usage outside the `ITenantCache` wrapper (per NFR-08, only allow-listed exceptions are valid).
4. **Cold start after restart, scale event or cutover** — transient; resolves naturally within ~30 minutes as the cache warms.

### Alert Definitions (FR-17)

The alerts are deployed from [`infrastructure/bicep/alerts.bicep`](../../infrastructure/bicep/alerts.bicep) by:

```powershell
pwsh ./scripts/Deploy-RedisCache.ps1 -Environment {env} -SubscriptionId <sub> -DeployAlerts `
  -ActionGroupResourceId /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Insights/actionGroups/{name}
```

`-AppInsightsName` defaults per environment (dev: `spe-insights-dev-67e2xz`). `alerts.bicep` accepts `environment` `dev`, `demo`, `staging` or `prod`. They were drafted in [`projects/spaarke-redis-cache-remediation-r1/notes/alert-definitions-draft.md`](../../projects/spaarke-redis-cache-remediation-r1/notes/alert-definitions-draft.md); the definitions below are the operational source of truth.

All alerts are **Sev 2 (Warning)** by convention. Sustained or co-occurring firings escalate via the runbook below.

#### Alert 1 — Cache Hit Rate Below 80%

- **Name**: `redis-cache-hit-rate-low-{env}`
- **Resource scope**: App Insights
- **Severity**: Warning (Sev 2)
- **Evaluation frequency**: 5 minutes
- **Window**: 15 minutes
- **Threshold**: `avg_hit_rate < 0.80` for the window
- **Metric source**: App Insights custom metrics `cache.hits` and `cache.misses` (FR-16)
- **Suggested action**: "Cache key/version drift; investigate."

KQL expression (scheduledQueryRules):

```kusto
let hits = customMetrics
  | where name == "cache.hits"
  | summarize hits = sum(valueSum) by bin(timestamp, 5m), resource = tostring(customDimensions.resource);
let misses = customMetrics
  | where name == "cache.misses"
  | summarize misses = sum(valueSum) by bin(timestamp, 5m), resource = tostring(customDimensions.resource);
hits
| join kind=fullouter misses on timestamp, resource
| extend hits = coalesce(hits, 0.0), misses = coalesce(misses, 0.0)
| extend total = hits + misses
| where total >= 100  // noise floor
| extend hit_rate = hits / total
| summarize avg_hit_rate = avg(hit_rate) by bin(timestamp, 15m), resource
| where avg_hit_rate < 0.80
```

#### Alert 2 — Redis call latency above 100 ms

- **Name**: `redis-cache-p95-latency-high-{env}` (name kept so the deployed rule updates in place)
- **Resource scope**: App Insights — BFF-observed latency of each `IDistributedCache` call, not cache-side only.
- **Severity**: Warning (Sev 2)
- **Evaluation frequency**: 1 minute
- **Window**: 5 minutes
- **Threshold**: the **average** call duration per operation (`get`, `set`, `refresh`, `remove`) over 5 minutes `> 100` ms
- **Metric source**: the histogram `cache.redis_call_duration_ms`, recorded by `Infrastructure/Cache/MetricsDistributedCache.cs` (tags `op`, `tier`). App Insights stores it pre-aggregated (sum, count, min, max), so a true P95 is not available from `customMetrics`; the average is what can be alerted on.
- **History**: until 2026-10-05 the rule queried `cache.redis_p95_ms`, which nothing emits, so it could never fire.
- **Suggested action**: "Network issue, SKU undersize, or client-side delay." Compare with the `redis` dependency durations — if those are low while this is high, the time is spent in the BFF's cache client, not in Redis.

KQL expression (scheduledQueryRules):

```kusto
customMetrics
| where name == "cache.redis_call_duration_ms"
| extend op = tostring(customDimensions.op)
| summarize avg_ms = sum(valueSum) / sum(valueCount) by bin(timestamp, 5m), op
| where avg_ms > 100
```

#### Alert 3 — Redis Memory Usage Above 80% of SKU Limit

- **Name**: `redis-cache-memory-high-{env}`
- **Resource scope**: the cache (`spaarke-bff-redis-{env}`, `Microsoft.Cache/redisEnterprise`) — Azure Monitor platform metric
- **Severity**: Warning (Sev 2)
- **Evaluation frequency**: 5 minutes
- **Window**: 15 minutes (sustained)
- **Threshold**: `usedmemorypercentage > 80` for the window
- **Metric source**: Azure Monitor platform metric `Microsoft.Cache/redisEnterprise` / `usedmemorypercentage`
- **Suggested action**: "Scale to the next Balanced SKU" (`B0` → `B1` → `B3` → `B5` → `B10`; no scale-down).

Azure Monitor metric alert (no KQL required):

- Namespace: `Microsoft.Cache/redisEnterprise`
- Metric name: `usedmemorypercentage`
- Aggregation: Average
- Operator: GreaterThan
- Threshold: 80
- Window: PT15M
- Evaluation frequency: PT5M

#### Threshold Tuning — Dev vs. Prod

Defaults above are dev/staging-appropriate. Prod tuning is tighter (finalize during prod provisioning; `alerts.bicep` parameters `hitRateThreshold`, `p95LatencyMsThreshold`, `memoryPercentThreshold`):

| Alert | Dev/Staging | Prod (proposed) |
|---|---|---|
| Hit rate | < 80% | < 90% |
| Average call latency | > 100 ms | > 50 ms |
| Memory | > 80% | > 70% |

#### Cross-alert correlation runbook

- **(2) + (3) together** → SKU is undersized for current load. Default action: scale up one tier and observe for 24 h.
- **(1) + (2) without (3)** → likely code regression (key-version drift causing both increased Redis traffic for misses AND slower per-op latency). Default action: check recent deploys, consider rollback while investigating.
- **(1) alone without (2) or (3)** → likely TTL / key-naming bug from a recent commit. Code review focus.
- **(3) alone without (1) or (2)** → healthy growth signal; scale before it impacts latency.

---

## 9. Known Limitation — In-Memory Fallback Mode

> **WARNING — In-memory fallback mode does NOT support multi-instance deployment.** When `Redis:Enabled=false` and `Redis:AllowInMemoryFallback=true` (Development and Testing only — other environments throw at startup per FR-03), Pub/Sub cache invalidations are no-op (`NullConnectionMultiplexer.GetSubscriber().Subscribe(...)` registers but never delivers). This means cache entries can become stale across instances. **The in-memory mode is for local, single-instance development only.** Any deployed environment MUST run with `Redis:Enabled=true` against a real Redis instance.

This is by design (per Q-B in `spec.md`) — documented, not engineered around. The Null-Object `IConnectionMultiplexer` (per ADR-032) preserves symmetric DI registration without requiring callers to null-check the multiplexer.

---

## 10. Lessons Learned

This section summarizes how the drift this project remediated originated and the guardrails now in place that would have prevented it. Project-specific execution lessons (test-fixture sweep, inventory accuracy, parallelism viability) live in [`projects/spaarke-redis-cache-remediation-r1/notes/lessons-learned.md`](../../projects/spaarke-redis-cache-remediation-r1/notes/lessons-learned.md) and are intentionally not duplicated here — this guide is the canonical operational reference, not a project retrospective.

### How the drift originated

The state at the start of the remediation project — `spaarke-bff-dev` silently running on in-memory cache for an unknown duration — was the cumulative result of five compounding failures, none of which alone would have been catastrophic:

1. **A prior project deleted the dev Redis instance** (`spe-redis-dev-67e2xz`). The deletion was operational, not coordinated with BFF owners; no follow-up issue was filed to re-provision.
2. **BFF App Setting `Redis__Enabled` was left at `false`.** Possibly an emergency mitigation during the deletion or stale earlier config; no record of the rationale exists.
3. **`CacheModule` had `AbortOnConnectFail = false`,** so even when a connection was attempted, failures were silent — the BFF would start, log a single warning, and run on `MemoryDistributedCache` indefinitely.
4. **`CacheModule` had no environment guard.** Redis-off + in-memory fallback was treated as a universal default. There was no distinction between "local developer laptop" (where in-memory is acceptable) and "deployed App Service environment named Development" (where it is not — multi-instance Pub/Sub never delivers).
5. **The in-memory warning log line was ignored or lost.** No App Insights alert fired on it; no startup-health check enforced the invariant; nobody was paged. The warning sat in the log stream, technically observable but operationally invisible.

The combination produced a deployed environment running on in-memory cache with no key tenant prefix enforcement, no Pub/Sub invalidation, and no visibility of the degradation. The state could have persisted indefinitely.

### Guardrails now in place

Each guardrail below independently breaks the failure chain above.

1. **Fail-fast in deployed environments.** `CacheModule` now throws `InvalidOperationException` at startup when Redis is configured-but-unreachable (`AbortOnConnectFail = true` + environment-guarded fallback). A deployed BFF either runs on Redis or it does not start. Reference: `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs`, ADR-009 (amended).
2. **Explicit opt-in for fallback.** `Redis:AllowInMemoryFallback` defaults `false`. Even the Development environment requires it `true` to use in-memory cache. The deployed dev App Service ships with `false`; in-memory mode is now a local-developer-laptop-only state.
3. **Null-Object `IConnectionMultiplexer`.** Symmetric DI registration (per [ADR-032](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md)) means consumers in dev see no-op Pub/Sub + an explicit `NotSupportedException` on direct database access — never a missing-service error. This eliminates an entire class of `IConnectionMultiplexer?` nullable-defensive code that previously masked degraded state.
4. **Canonical naming.** `spaarke-bff-redis-{env}` (top-level env-suffix) per NFR-03 makes off-pattern legacy instances visible at a glance in resource lists and lifecycle scripts.
5. **Tenant-prefix mandatory in keys.** The canonical key format `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}` is enforced at every call site via the `ITenantCache` wrapper. System-level exceptions (feature flags, system config) are explicitly allow-listed with JSON-comment justification (NFR-08).
6. **App Insights observability.** Redis dependency telemetry (auto) + `cache.hits` / `cache.misses` / `cache.redis_call_duration_ms` custom metrics (wrapper-emitted) + the §8 alert rules (hit rate < 80%, P95 latency > 100 ms, memory > 80%) make any future degradation visible within minutes.
7. **Deployment checklist.** [`scripts/Deploy-RedisCache.ps1`](../../scripts/Deploy-RedisCache.ps1) (idempotent, multi-env, `-WhatIf` / `-VerifyOnly` / `-CutoverBffSettings` / `-RemoveBffConnectionString` / `-Force` per NFR-01/05/06) and this runbook (§§1–9) let any future operator provision a new env Redis end-to-end in under 30 minutes (FR-19, Success Criterion #6).

### 2026-10-05 — Azure Managed Redis, Microsoft Entra only (T242 / T242b)

Classic Azure Cache for Redis (Basic/Standard/Premium) retires on 2028-09-30 and has refused new customers since 2026-04-01, so the owner decided (D12) that every Spaarke Redis is Azure Managed Redis, and (D13) that caches are keyless. T242 changed `redis.bicep`, `customer.bicep`, the BFF and the L2 Worker to sign in with managed identities through `Redis__Endpoint`; T242b created `spaarke-bff-redis-dev` and `spaarke-bff-redis-demo` as Managed Redis, cut the dev BFF over with no outage (§4), removed its connection string, retired dev key rotation and the dev missed-rotation alert, and pointed the memory alert at `Microsoft.Cache/redisEnterprise`. Two lessons: the post-deploy harness had been checking a source layout that no longer existed and failed on every run since 2025, so it was rewritten as a read-only ARM check of what actually matters (keys disabled, access policy, endpoint); and the cutover worked without an outage only because the BFF kept its connection string until a T242+ build was deployed and verified in App Insights — removing it first would have stopped every older build. The old classic cache is deleted only with owner approval, and its Key Vault secret stays in place.

---

## 11. Cross-References

- [`docs/architecture/caching-architecture.md`](../architecture/caching-architecture.md) — design rationale: Tenant Isolation, Multi-instance Behavior, Cache Instance Registry, Failure Mode Catalog.
- [`.claude/adr/ADR-009-redis-caching.md`](../../.claude/adr/ADR-009-redis-caching.md) — concise ADR-009 constraints (MUST / MUST NOT).
- [`docs/adr/ADR-009-caching-redis-first.md`](../adr/ADR-009-caching-redis-first.md) — full ADR-009 rationale.
- [`scripts/Deploy-RedisCache.ps1`](../../scripts/Deploy-RedisCache.ps1) — provisioning automation (idempotent, multi-env, `-WhatIf`, `-VerifyOnly`, `-CutoverBffSettings`, `-RemoveBffConnectionString`, `-SubscriptionId`, `-Force`, `-DeployAlerts`).
- [`tests/manual/RedisValidationTests.ps1`](../../tests/manual/RedisValidationTests.ps1) — read-only ARM validation harness (cluster state, SKU, keys disabled, TLS, OSSCluster, port 10000, access-policy assignments, BFF `Redis__Endpoint`, no connection string).
- [`infrastructure/bicep/modules/redis.bicep`](../../infrastructure/bicep/modules/redis.bicep) — Azure Managed Redis module (also used by `customer.bicep`).
- [`infrastructure/bicep/parameters/redis-dev.bicepparam`](../../infrastructure/bicep/parameters/redis-dev.bicepparam) / [`redis-demo.bicepparam`](../../infrastructure/bicep/parameters/redis-demo.bicepparam) — live environments; staging / prod parameter files follow the same shape with placeholders.
- [`infrastructure/bicep/alerts.bicep`](../../infrastructure/bicep/alerts.bicep) — alert rules (§8).
- [`src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs) — BFF cache registration (endpoint → managed identity, RESP3; connection string only in Development/Testing).

---

*This guide is the operational source of truth for Redis cache management across all Spaarke environments. Updates SHOULD accompany any change to `Deploy-RedisCache.ps1`, `redis.bicep`, `alerts.bicep`, `RedisValidationTests.ps1`, `CacheModule.cs`, or ADR-009.*
