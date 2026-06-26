# Redis Cache — Azure Setup & Operational Guide

> **Last Updated**: 2026-06-26 (§10 Lessons Learned filled in by task 056)
> **Audience**: BFF operators, infrastructure engineers
> **Status**: Authoritative

This guide is the canonical operational reference for provisioning, cutting over, validating, rolling back, rotating secrets for, and decommissioning the Spaarke BFF Redis cache (`spaarke-bff-redis-{env}`) in any environment. A fresh operator should be able to provision a new environment's Redis end-to-end in under 30 minutes by following only this document (per FR-19, Success Criterion #6).

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

Before running any command in this guide, confirm each of the following:

- **Azure subscription access** with at least Contributor on the target resource group (`spe-infrastructure-westus2` for dev; `rg-spaarke-{staging|prod}` for higher environments).
- **Azure CLI installed and logged in** — verify with `az account show`; ensure the active subscription matches the target environment.
- **Key Vault exists** in the target environment (`spaarke-spekvcert` is the assumed dev KV; verify the actual KV per the target environment's cutover baseline notes before secret upsert — see [`projects/spaarke-redis-cache-remediation-r1/notes/dev-cutover-baseline.md`](../../projects/spaarke-redis-cache-remediation-r1/notes/dev-cutover-baseline.md) for the dev pattern).
- **App Service Managed Identity has `Key Vault Secrets User` role** on the target Key Vault. Verify with `az role assignment list --assignee <MI-objectId> --scope <KV-resourceId>`.
- **`spaarke-bff-{env}` App Service exists and is running** — verify with `az webapp show -g rg-spaarke-{env} -n spaarke-bff-{env} --query state`.
- **App Settings template loaded** — see [`src/server/api/Sprk.Bff.Api/appsettings.template.json`](../../src/server/api/Sprk.Bff.Api/appsettings.template.json) for the Redis settings shape (`Redis__Enabled`, `Redis__InstanceName`, `ConnectionStrings__Redis`, `Redis__AllowInMemoryFallback`).
- **PowerShell 7+ available** — verify with `pwsh -Version` (must be 7.0 or later). Windows PowerShell 5.1 is NOT supported.
- **Bicep module + parameter file present**:
  - [`infrastructure/bicep/modules/redis.bicep`](../../infrastructure/bicep/modules/redis.bicep)
  - [`infrastructure/bicep/parameters/redis-{env}.bicepparam`](../../infrastructure/bicep/parameters/)
- **Validation harness present** — [`tests/manual/RedisValidationTests.ps1`](../../tests/manual/RedisValidationTests.ps1) (used by `Deploy-RedisCache.ps1 -VerifyOnly`).

---

## 2. Provision Command — Per Environment

All environments use the same idempotent script: [`scripts/Deploy-RedisCache.ps1`](../../scripts/Deploy-RedisCache.ps1). The script:

- Detects existing instances in `Succeeded` provisioning state and skips redeploy (NFR-01).
- Rejects `prod` and `demo` without `-Force` (NFR-05).
- Supports `-WhatIf` (plan-only, NFR-06) and `-VerifyOnly` (run validation harness against existing instance, NFR-06).
- Optionally upserts the connection string to Key Vault and cuts over App Settings when `-CutoverBffSettings` is passed.

### Dev

```powershell
pwsh ./scripts/Deploy-RedisCache.ps1 -Environment dev -KeyVaultName spaarke-spekvcert -CutoverBffSettings
```

Expected `-WhatIf` plan output (from task 028's integration check; production run emits the same plan header followed by an actual `az deployment group create` invocation):

```
Deploy-RedisCache.ps1 starting
  Environment    : dev
  ResourceGroup  : spe-infrastructure-westus2
  Redis name     : spaarke-bff-redis-dev
  Bicep module   : <repo>/infrastructure/bicep/modules/redis.bicep
  Bicep param    : <repo>/infrastructure/bicep/parameters/redis-dev.bicepparam
  KeyVault       : spaarke-spekvcert
  Mode           : deploy

Deploy-RedisCache.ps1 completed successfully.
```

To preview without changes, add `-WhatIf`:

```powershell
pwsh ./scripts/Deploy-RedisCache.ps1 -Environment dev -WhatIf
```

### Staging

```powershell
pwsh ./scripts/Deploy-RedisCache.ps1 -Environment staging -KeyVaultName <staging-kv> -CutoverBffSettings
```

Replace `<staging-kv>` with the staging Key Vault name. Default resource group: `rg-spaarke-staging` (override with `-ResourceGroup` if your environment uses a different RG).

### Prod (requires explicit `-Force`)

```powershell
pwsh ./scripts/Deploy-RedisCache.ps1 -Environment prod -KeyVaultName <prod-kv> -CutoverBffSettings -Force
```

Replace `<prod-kv>` with the prod Key Vault name. The `-Force` flag is required per NFR-05; without it the script exits non-zero with an `NFR-05` message. Production provisioning is a separate go/no-go with finance + security review per [`spec.md`](../../projects/spaarke-redis-cache-remediation-r1/spec.md) §Out of Scope.

---

## 3. Verification

After a deploy completes, verify each of the following before declaring success.

1. **Redis instance is in `Succeeded` provisioning state**:

   ```powershell
   az redis show -g <rg> -n spaarke-bff-redis-{env} --query provisioningState
   ```

   Expect: `"Succeeded"`.

2. **Connection string is present in Key Vault**:

   ```powershell
   az keyvault secret show --vault-name <kv> --name Redis-ConnectionString --query value -o tsv
   ```

   Expect: a non-empty StackExchange.Redis-compatible connection string referencing the new instance hostname.

3. **Restart the BFF**:

   ```powershell
   az webapp restart -g rg-spaarke-{env} -n spaarke-bff-{env}
   ```

4. **Stream the startup log**:

   ```powershell
   az webapp log tail -g rg-spaarke-{env} -n spaarke-bff-{env}
   ```

   Expect, verbatim, the line:

   ```
   Distributed cache: Redis enabled with instance name 'spaarke:'
   ```

   The log MUST NOT contain any in-memory fallback warning. If it does, the cutover did not take effect — see §5 Rollback.

5. **Health check returns 200**:

   ```powershell
   curl -i https://spaarke-bff-{env}.azurewebsites.net/healthz
   ```

   Expect HTTP `200 OK`.

6. **Smoke test — chat session creation produces a tenant-prefixed key**:

   Exercise a chat-session creation through the BFF (e.g., via the BFF API or a code-page client). Then inspect Redis for the resulting key:

   ```powershell
   az redis show-access-keys -g <rg> -n spaarke-bff-redis-{env}
   # then connect via redis-cli or use a script that runs SCAN with pattern:
   #   spaarke:tenant:*:session:*:v1
   ```

   Expect: at least one key in the form `spaarke:tenant:{tenantId}:session:{sessionId}:v1`. This verifies the tenant prefix invariant (FR-05, FR-06, NFR-08) and the `spaarke:` InstanceName (FR-07).

7. **Validation harness** (full invariant sweep):

   ```powershell
   pwsh ./scripts/Deploy-RedisCache.ps1 -Environment {env} -VerifyOnly
   ```

   Runs [`tests/manual/RedisValidationTests.ps1`](../../tests/manual/RedisValidationTests.ps1), which includes `Test-TenantPrefixInvariant` and `Test-FailFastBehavior` (added in task 026). Non-zero exit = a key invariant is violated.

8. **App Insights telemetry pipeline verification** (R7-S7 closure 2026-06-26 — REQUIRED). After 10 min of post-deploy traffic, both queries below MUST return non-empty results. If either is empty, the telemetry pipeline is broken — see ADR-009 §9 for the required wiring (`UseAzureMonitor()` + `AddRedisInstrumentation()` + `RedisCacheOptions.ConnectionMultiplexerFactory`).

   ```bash
   # Custom cache metrics — expect cache.hits, cache.misses, cache.redis_call_duration_ms
   az monitor app-insights query \
     --app spe-insights-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 \
     --analytics-query "customMetrics | where timestamp > ago(10m) | where name startswith 'cache.' | summarize total=sum(value), records=count() by name"

   # Redis dependency telemetry — expect HMGET / UNLINK / CLIENT / GET / SET
   az monitor app-insights query \
     --app spe-insights-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 \
     --analytics-query "dependencies | where timestamp > ago(10m) | where type contains 'Redis' | summarize count() by type, name"
   ```

   **Common failure modes**:
   - `customMetrics` query empty → `UseAzureMonitor()` not wired in `Program.cs` (still using classic `AddApplicationInsightsTelemetry()`)
   - `dependencies` query empty even though custom metrics flow → `RedisCacheOptions.ConnectionMultiplexerFactory` not wired in `CacheModule.cs` (DI-registered multiplexer is idle; `Microsoft.Extensions.Caching.StackExchangeRedis` built its own internal one)
   - Both queries empty → either `APPLICATIONINSIGHTS_CONNECTION_STRING` not set on the BFF App Service, or the exporter package (`Azure.Monitor.OpenTelemetry.AspNetCore`) is missing from `Sprk.Bff.Api.csproj`

---

## 4. Cutover Protocol

The cutover procedure differs depending on whether the legacy Redis instance holds production-relevant cache data.

### Dev (clean slate — legacy Redis is empty)

The dev environment legacy instance `spe-redis-dev-67e2xz` is empty; no key migration is required.

1. **Provision the new instance** via §2.
2. **Upsert the connection string** to the dev Key Vault (handled by `-CutoverBffSettings`).
3. **Update App Settings** to point `ConnectionStrings__Redis` at the new KV reference; set `Redis__Enabled=true`, `Redis__InstanceName=spaarke:`, `Redis__AllowInMemoryFallback=false` (handled by `-CutoverBffSettings`).
4. **Restart** the BFF and **verify** per §3.
5. **24-hour verification window** — let dev BFF operate against the new instance for 24 hours; monitor App Insights for Redis dependency calls, cache hit rate, P95 latency. If no regressions, proceed to step 6.
6. **Decommission the legacy instance** per §7 — either DELETE or tag `decommission=YYYY-MM-DD`.

### Staging / Prod (data resides in legacy)

When the legacy instance holds cache data with operational value, two options are available; document the chosen path in the cutover record (`notes/cutover-deploy-log.md`).

**Option A — key warming (preferred for high-traffic resources)**:

1. Provision the new instance via §2.
2. Pre-populate hot keys from legacy via a batch migration script (read from legacy, write to new with the same TTL — the canonical key format `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}` is identical between legacy and new instances when `InstanceName=spaarke:` is already in use; if legacy still uses `sdap:`, this option requires explicit re-prefixing).
3. Cut over App Settings, restart, verify.
4. Allow stragglers (long-tail keys) to cache-miss and refill organically.

Option A minimizes P95 latency degradation during cutover at the cost of an extra batch script execution.

**Option B — cache-miss window (preferred for low-traffic resources)**:

1. Provision the new instance via §2.
2. Cut over App Settings, restart, verify.
3. Accept a short period (typically 5–30 minutes) of elevated P95 latency as the new cache fills from cold misses.

Option B is operationally simpler but produces a brief, observable P95 spike. Choose Option B only when traffic is low enough that the user-visible impact is acceptable. **Production should typically use Option A for high-traffic resources, Option B for low-traffic.**

In either case, follow the 24-hour verification window before decommissioning legacy.

---

## 5. Rollback Procedure

If post-cutover verification fails (`/healthz` returns non-200, startup log shows in-memory warning, smoke test does not produce `spaarke:tenant:*` keys, or P95 latency is unacceptable):

1. **Revert `ConnectionStrings__Redis`** in App Settings to point at the legacy Key Vault secret version (or the legacy KV reference if a separate secret was used):

   ```powershell
   az webapp config appsettings set `
     -g rg-spaarke-{env} -n spaarke-bff-{env} `
     --settings ConnectionStrings__Redis='@Microsoft.KeyVault(VaultName=<kv>;SecretName=<legacy-secret-name>)'
   ```

2. **Restart** the BFF:

   ```powershell
   az webapp restart -g rg-spaarke-{env} -n spaarke-bff-{env}
   ```

3. **Verify** the startup log shows the OLD instance name in `"Distributed cache: Redis enabled with instance name 'spaarke:'"` confirms via dependency endpoint, App Insights Redis dependency calls show the legacy hostname).
4. **The new instance can remain provisioned** — idempotent re-deploys are safe (NFR-01). Investigate the failure root cause, then re-attempt cutover when ready.

---

## 6. Secret Rotation Procedure

Rotate the Redis primary key with minimal downtime. Frequency: per organizational policy (typical: every 90 days).

### 6.1 Per-Environment OIDC Service-Principal Provisioning (one-time setup, per FR-09)

**Operator must complete this section BEFORE enabling the automated rotation workflow** ([`.github/workflows/redis-key-rotation.yml`](../../.github/workflows/redis-key-rotation.yml), provisioned by task 011 of `spaarke-redis-cache-remediation-r2`). The workflow consumes three distinct GitHub Environment secrets — one per Azure environment — each backed by a separate Azure AD service principal scoped to ONLY that environment's resources.

#### Rationale (why three SPs, not one)

- **Blast-radius isolation**: a compromised prod SP MUST NOT be able to rotate dev (and vice versa). A single shared SP with org-wide write across all three envs collapses the blast radius of any credential leak to "all envs at once."
- **Compliance posture**: per-env separation of duties is a standard audit expectation (SOC 2 CC6.1, ISO 27001 A.9.2). Per-env SPs make the access boundary auditable via a single `az role assignment list` per principal.
- **Least privilege**: each SP holds only the three role assignments needed to rotate one env (KV secret write, Redis key regenerate, App Service restart). No cross-env grants.

#### Step 1 — Create one service principal per environment

Replace `{SUB_ID}` with the target Azure subscription ID for each env (dev/staging/prod may share a subscription or use separate ones; commands below are per-env regardless).

```bash
# Dev
az ad sp create-for-rbac \
  --name "sp-spaarke-redis-rotation-dev" \
  --role "Reader" \
  --scopes "/subscriptions/{SUB_ID_DEV}/resourceGroups/rg-spaarke-dev" \
  --query "{clientId:appId, tenantId:tenant}" -o json
# Record output clientId → AZURE_CLIENT_ID_DEV

# Staging
az ad sp create-for-rbac \
  --name "sp-spaarke-redis-rotation-staging" \
  --role "Reader" \
  --scopes "/subscriptions/{SUB_ID_STAGING}/resourceGroups/rg-spaarke-staging" \
  --query "{clientId:appId, tenantId:tenant}" -o json
# Record output clientId → AZURE_CLIENT_ID_STAGING

# Prod
az ad sp create-for-rbac \
  --name "sp-spaarke-redis-rotation-prod" \
  --role "Reader" \
  --scopes "/subscriptions/{SUB_ID_PROD}/resourceGroups/rg-spaarke-prod" \
  --query "{clientId:appId, tenantId:tenant}" -o json
# Record output clientId → AZURE_CLIENT_ID_PROD
```

The `Reader` grant at RG scope is a placeholder so `create-for-rbac` succeeds; the operationally meaningful grants are the three narrow role assignments in Step 3. The Reader grant MAY be removed after Step 3 completes if your security policy prefers a strict "only the three rotation roles" posture.

#### Step 2 — Configure federated identity credentials (OIDC, no client secrets)

For each SP, add a federated identity credential that trusts GitHub Actions running in the corresponding GitHub Environment. Repeat per env (replace `{APP_ID}` with the SP's appId from Step 1, `{ENV}` with `dev`/`staging`/`prod`):

```bash
az ad app federated-credential create \
  --id {APP_ID} \
  --parameters '{
    "name": "github-spaarke-redis-rotation-{ENV}",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:spaarke-dev/spaarke:environment:{ENV}",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

The `subject` claim binds the credential to the specific GitHub Environment, so a workflow job running in env `dev` cannot mint a token for the `prod` SP even if it knows the prod clientId.

#### Step 3 — Assign narrowly-scoped roles (env-specific resource IDs only)

For each env, run all three assignments. **Critical**: scopes MUST be the env-specific resource ID, not the RG or subscription. Replace `{SUB_ID}`, `{KV_NAME}`, `{REDIS_NAME}`, `{APP_SERVICE_NAME}`, `{SP_OBJECT_ID}` (the SP's objectId — get via `az ad sp show --id {APP_ID} --query id -o tsv`).

```bash
# (a) Key Vault Secrets Officer — write Redis-ConnectionString secret
az role assignment create \
  --assignee {SP_OBJECT_ID} \
  --role "Key Vault Secrets Officer" \
  --scope "/subscriptions/{SUB_ID}/resourceGroups/rg-spaarke-{ENV}/providers/Microsoft.KeyVault/vaults/{KV_NAME}"

# (b) Redis cache contributor — regenerate primary key
# Built-in "Redis Cache Contributor" includes Microsoft.Cache/redis/regenerateKey/action and listKeys/action.
# If your security policy disallows the built-in (it also grants write/delete on the cache resource),
# create a custom role "spaarke-redis-key-rotator" with ONLY:
#   - Microsoft.Cache/redis/listKeys/action
#   - Microsoft.Cache/redis/regenerateKey/action
#   - Microsoft.Cache/redis/read
# and assign that instead.
az role assignment create \
  --assignee {SP_OBJECT_ID} \
  --role "Redis Cache Contributor" \
  --scope "/subscriptions/{SUB_ID}/resourceGroups/rg-spaarke-{ENV}/providers/Microsoft.Cache/Redis/{REDIS_NAME}"

# (c) Website Contributor — restart App Service so the new KV reference is picked up
# "Website Contributor" includes Microsoft.Web/sites/restart/action. A tighter custom role
# limited to restart/action only is acceptable if preferred.
az role assignment create \
  --assignee {SP_OBJECT_ID} \
  --role "Website Contributor" \
  --scope "/subscriptions/{SUB_ID}/resourceGroups/rg-spaarke-{ENV}/providers/Microsoft.Web/sites/{APP_SERVICE_NAME}"
```

For env `dev`, the resource names per current cutover baseline are: `{KV_NAME}=spaarke-spekvcert`, `{REDIS_NAME}=spaarke-bff-redis-dev`, `{APP_SERVICE_NAME}=spaarke-bff-dev`. Staging and prod names follow the same `{prefix}-{env}` pattern (confirm against env-specific cutover records).

#### Step 4 — Publish the clientId to the corresponding GitHub Environment

Create the three GitHub Environments first (if they do not already exist) at `https://github.com/spaarke-dev/spaarke/settings/environments` — names: `dev`, `staging`, `prod`. Add required reviewers + deployment branch rules on `prod` per organizational policy.

Then publish each SP's clientId as an environment-scoped secret (per spec FR-09 naming):

```bash
gh secret set AZURE_CLIENT_ID_DEV     --env dev     --body "{APP_ID_DEV}"
gh secret set AZURE_CLIENT_ID_STAGING --env staging --body "{APP_ID_STAGING}"
gh secret set AZURE_CLIENT_ID_PROD    --env prod    --body "{APP_ID_PROD}"
```

Also publish `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` per env (these may be repo-level secrets if all envs share the same tenant/sub, or env-scoped if they differ).

#### Step 5 — Verify isolation

For each SP, list ALL role assignments across ALL subscriptions and confirm the only env-meaningful grants are scoped to that SP's env.

```bash
az role assignment list --assignee {SP_OBJECT_ID_DEV}     --all -o table
az role assignment list --assignee {SP_OBJECT_ID_STAGING} --all -o table
az role assignment list --assignee {SP_OBJECT_ID_PROD}    --all -o table
```

Expected shape per SP (three rows, plus the placeholder `Reader` from Step 1 if retained):

```
Principal                                Role                          Scope
--------------------------------------   ---------------------------   -----------------------------------------------------------------------------
sp-spaarke-redis-rotation-{env}          Key Vault Secrets Officer     /subscriptions/.../rg-spaarke-{env}/providers/Microsoft.KeyVault/vaults/...
sp-spaarke-redis-rotation-{env}          Redis Cache Contributor       /subscriptions/.../rg-spaarke-{env}/providers/Microsoft.Cache/Redis/...
sp-spaarke-redis-rotation-{env}          Website Contributor           /subscriptions/.../rg-spaarke-{env}/providers/Microsoft.Web/sites/...
```

**FR-09 acceptance**: every Scope column value MUST contain the SP's own env name (`rg-spaarke-{env}`) and MUST NOT reference any other env's resources. If `az role assignment list --assignee {SP_OBJECT_ID_PROD}` shows any scope under `rg-spaarke-dev` or `rg-spaarke-staging`, isolation is broken — remove the cross-env assignment before enabling the workflow.

#### Operator one-time setup checklist

- [ ] 1. Create three SPs via Step 1 (`sp-spaarke-redis-rotation-{dev|staging|prod}`); record each appId + objectId.
- [ ] 2. Add federated identity credential per SP, bound to `repo:spaarke-dev/spaarke:environment:{env}` (Step 2).
- [ ] 3. Assign three narrow roles per SP — KV Secrets Officer, Redis Cache Contributor, Website Contributor — at env-specific resource scopes (Step 3).
- [ ] 4. Create three GitHub Environments (`dev`, `staging`, `prod`) with required reviewers on prod; publish `AZURE_CLIENT_ID_{ENV}` as env-scoped secret (Step 4).
- [ ] 5. Verify isolation per SP via `az role assignment list --assignee {SP_OBJECT_ID} --all` (Step 5); confirm no cross-env scopes.
- [ ] 6. Enable the cron schedule in [`.github/workflows/redis-key-rotation.yml`](../../.github/workflows/redis-key-rotation.yml) (the workflow is dormant until these SPs exist).

Once this section is complete, the automated rotation workflow (task 011) consumes these SPs via OIDC token exchange — no client secrets stored anywhere.

### Steps

1. **Verify current state** — capture current secret version: `az keyvault secret show --vault-name <kv> --name Redis-ConnectionString --query attributes.version -o tsv`. Record in cutover/rotation log.
2. **Regenerate primary key** in Azure: `az redis regenerate-key -g <rg> -n spaarke-bff-redis-<env> --key-type Primary`.
3. **Build new connection string**: `{host}:6380,password={newPrimaryKey},ssl=True,abortConnect=False`. Get host: `az redis show -g <rg> -n spaarke-bff-redis-<env> --query hostName -o tsv`.
4. **Upsert KV secret** with new value: `az keyvault secret set --vault-name <kv> --name Redis-ConnectionString --value "<new-conn-string>" --output none`. Capture new version.
5. **Pick up rotation** — Key Vault references on App Service cache for ~24 hours by default. Two options:
   - **Option A (immediate)**: Force pickup by restarting BFF: `az webapp restart -g rg-spaarke-<env> -n spaarke-bff-<env>`. ~30-second downtime per instance.
   - **Option B (background)**: Let KV reference TTL expire naturally over ~24 hours. Zero downtime but each instance picks up new value at staggered times.
6. **Verify** — after BFF picks up new value, hit `/healthz` and confirm a fresh chat-session creates a key in Redis (verifies the new connection string works).
7. **Decommission previous key** — `az redis regenerate-key --key-type Secondary` is a separate, optional step to invalidate any lingering use of the OLD primary (now-secondary) key. Do AFTER verifying step 6.
8. **Audit** — record rotation in `notes/cutover-deploy-log.md` with timestamps + KV secret version + which option (A/B) was used + downtime observed.

### Expected Downtime

- Option A: ~30 seconds per BFF instance during restart.
- Option B: 0 downtime; rotation completes within ~24 hours.

### Failure Recovery

- If new connection string is wrong or KV upsert fails: revert by restoring the previous secret version (`az keyvault secret set` with the old value, captured in step 1) and restart BFF.

---

## 7. Decommission Procedure

After the 24-hour verification window for dev (or the environment-specific window for staging/prod) has passed without regressions:

1. **Choose decommission method**:
   - **Delete** (preferred for empty legacy resources):

     ```powershell
     az redis delete -g <rg> -n <legacy-redis-name> --yes
     ```

   - **Tag for delayed deletion** (preferred when historical data may be needed for audit):

     ```powershell
     az resource tag --tags decommission=YYYY-MM-DD `
       -g <rg> -n <legacy-redis-name> `
       --resource-type Microsoft.Cache/Redis
     ```

2. **Record the decommission** in [`projects/spaarke-redis-cache-remediation-r1/notes/cutover-deploy-log.md`](../../projects/spaarke-redis-cache-remediation-r1/notes/cutover-deploy-log.md):
   - Date of decommission
   - Method (delete vs. tag)
   - Decommission tag value (if tagged)
   - Operator who executed the decommission

---

## 8. Troubleshooting

### Connection failures (BFF cannot reach Redis)

**Symptoms**: BFF startup logs show connection errors; `/healthz` returns 503; App Insights logs `RedisConnectionException` or similar.

**Common causes**:

1. **Key Vault reference unresolved** — App Service cannot read the `Redis-ConnectionString` secret.
   - Verify with `az webapp config appsettings list -g rg-spaarke-{env} -n spaarke-bff-{env} --query "[?name=='ConnectionStrings__Redis']"`.
   - If the value shows `@Microsoft.KeyVault(...)` literally (not resolved), check the Managed Identity has `Key Vault Secrets User` role on the KV.
2. **Managed Identity missing role** — assign the role:

   ```powershell
   az role assignment create `
     --assignee <MI-objectId> `
     --role "Key Vault Secrets User" `
     --scope $(az keyvault show -n <kv> --query id -o tsv)
   ```

3. **Redis instance down** — verify with `az redis show -g <rg> -n spaarke-bff-redis-{env} --query provisioningState`. Expect `Succeeded`. If `Failed` or `Deleting`, re-run the provision command.
4. **Network path blocked** (private endpoint / VNet integration) — verify DNS resolution from the App Service to the Redis hostname; check NSG rules on the integration subnet.

### Latency spikes (P95 > 100 ms sustained)

**Symptoms**: App Insights `cache.redis_p95_ms` custom metric exceeds 100 ms for sustained windows; user-visible BFF latency increases.

**Common causes**:

1. **SKU undersize** — check `serverLoad` and `usedmemorypercentage` Azure Monitor metrics. If `serverLoad > 80%` or `usedmemorypercentage > 70%`, scale up via `infrastructure/bicep/parameters/redis-{env}.bicepparam` (update `redisSkuName` / `redisSkuFamily` / `redisSkuCapacity`) and redeploy.
2. **Network issue** — check Azure Service Health for `Cache` in the deployment region. If a regional incident is active, ride it out (auto-mitigates).
3. **Hot keys** — a small set of keys receives disproportionate traffic, exhausting Redis CPU. Identify via `redis-cli --hotkeys` (Premium SKU) or App Insights dependency call breakdown by operation name. Consider sharding the hot key by tenant or adding a brief client-side cache for the specific resource.
4. **Client-side connection pool exhaustion** — symptom: BFF-side P95 is high but Azure Monitor `cacheLatency` is normal. Check `StackExchange.Redis.ConnectionMultiplexer` configuration; verify thread-pool sizing on the App Service plan.

### Hit-rate degradation (`cache.hit_rate < 80%` sustained)

**Symptoms**: App Insights `cache.hit_rate` custom metric drops below 80% for sustained windows.

**Common causes**:

1. **TTL too short** — a recently-tuned TTL evicts entries before normal re-access. Check `RedisCacheOptions` and `IDistributedCache.Set*` TTL values vs. prior versions.
2. **Key drift** (version mismatch) — a recent deploy wrote keys without the `:v{n}` suffix or with the wrong version. Check `git log` on `CacheKeys.cs` and any `IDistributedCache.Set*` callsites in the last 24 hours.
3. **Tenant ID computation bug** — a code path bypassed the `tenant:{tenantId}:` prefix derivation. Grep the BFF for direct `IDistributedCache` usage outside the `ITenantCache` wrapper (per NFR-08, only allow-listed exceptions are valid).
4. **Cold start after restart / scale event** — transient; resolves naturally within ~30 minutes as the cache warms. Verify against App Service restart events in the same window.

### Alert Definitions (FR-17)

Three alerts MUST be configured against each environment's Redis cache and App Insights workspace. These were drafted in [`projects/spaarke-redis-cache-remediation-r1/notes/alert-definitions-draft.md`](../../projects/spaarke-redis-cache-remediation-r1/notes/alert-definitions-draft.md) and are restated here as the operational source of truth.

All three alerts are **Sev 2 (Warning)** by convention. Sustained or co-occurring firings (e.g., latency + memory together) escalate via the runbook in §8.

#### Alert 1 — Cache Hit Rate Below 80%

- **Name**: `redis-cache-hit-rate-low`
- **Resource scope**: App Insights (`spaarke-{env}-appi`)
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

#### Alert 2 — Redis P95 Latency Above 100 ms

- **Name**: `redis-cache-p95-latency-high`
- **Resource scope**: App Insights (`spaarke-{env}-appi`) — wrapper-emitted P95 is preferred over Azure Monitor `cacheLatency` because it reflects BFF-observed latency, not Redis-side only.
- **Severity**: Warning (Sev 2)
- **Evaluation frequency**: 1 minute
- **Window**: 5 minutes
- **Threshold**: `avg(cache.redis_p95_ms) > 100` for the window
- **Metric source**: App Insights custom metric `cache.redis_p95_ms` (FR-16, emitted from cache wrapper)
- **Suggested action**: "Network issue or SKU undersize."

KQL expression (scheduledQueryRules):

```kusto
customMetrics
| where name == "cache.redis_p95_ms"
| extend resource = tostring(customDimensions.resource)
| summarize avg_p95_ms = avg(valueSum / valueCount) by bin(timestamp, 1m), resource
| summarize windowed_p95 = avg(avg_p95_ms) by bin(timestamp, 5m), resource
| where windowed_p95 > 100
```

Alternative (Azure Monitor platform metric, less precise): `Microsoft.Cache/Redis/cacheLatency` (Premium SKU only) or `serverLoad > 80%` as a proxy on Basic/Standard.

#### Alert 3 — Redis Memory Usage Above 80% of SKU Limit

- **Name**: `redis-cache-memory-high`
- **Resource scope**: Redis (`spaarke-bff-redis-{env}`) — Azure Monitor platform metric
- **Severity**: Warning (Sev 2)
- **Evaluation frequency**: 5 minutes
- **Window**: 15 minutes (sustained)
- **Threshold**: `usedmemorypercentage > 80` for the window
- **Metric source**: Azure Monitor platform metric `Microsoft.Cache/Redis/usedmemorypercentage`
- **Suggested action**: "Scale to next SKU." See [`notes/alert-definitions-draft.md`](../../projects/spaarke-redis-cache-remediation-r1/notes/alert-definitions-draft.md) §Alert 3 for the SKU decision matrix (Basic C0 → C1 → Standard C2 → C3/Premium P1 → P2).

Azure Monitor metric alert (no KQL required):

- Namespace: `Microsoft.Cache/Redis`
- Metric name: `usedmemorypercentage`
- Aggregation: Average (or Maximum for tighter)
- Operator: GreaterThan
- Threshold: 80
- Window: PT15M
- Evaluation frequency: PT5M

#### Threshold Tuning — Dev vs. Prod

Defaults above are dev/staging-appropriate. Prod tuning is tighter (finalize during prod provisioning):

| Alert | Dev/Staging | Prod (proposed) |
|---|---|---|
| Hit rate | < 80% | < 90% |
| P95 latency | > 100 ms | > 50 ms |
| Memory | > 80% | > 70% |

#### Cross-alert correlation runbook

- **(2) + (3) together** → SKU is undersized for current load. Default action: scale up one tier and observe for 24 h.
- **(1) + (2) without (3)** → likely code regression (key-version drift causing both increased Redis traffic for misses AND slower per-op latency). Default action: check recent deploys, consider rollback while investigating.
- **(1) alone without (2) or (3)** → likely TTL / key-naming bug from a recent commit. Code review focus.
- **(3) alone without (1) or (2)** → healthy growth signal; scale before it impacts latency.

#### Deployment mechanism

Whether to deploy these alerts via Bicep (extension to `infrastructure/bicep/modules/alerts.bicep`) or via Portal / App Insights workbook is a Phase 4 implementation choice (both satisfy FR-17 acceptance). See `notes/alert-definitions-draft.md` §"Bicep deployment" for skeleton resource definitions.

---

## 9. Known Limitation — In-Memory Fallback Mode

> **WARNING — In-memory fallback mode does NOT support multi-instance deployment.** When `Redis:Enabled=false` and `Redis:AllowInMemoryFallback=true` (Development only — non-Development environments throw at startup per FR-03), Pub/Sub cache invalidations are no-op (`NullConnectionMultiplexer.GetSubscriber().Subscribe(...)` registers but never delivers). This means cache entries can become stale across instances. **The in-memory mode is for local, single-instance development only.** Any deployed environment MUST run with `Redis:Enabled=true` against a real Redis instance.

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

1. **Fail-fast in deployed environments.** `CacheModule` now throws `InvalidOperationException` at startup when Redis is configured-but-unreachable (`AbortOnConnectFail = true` + environment-guarded fallback). A deployed BFF either runs on Redis or it does not start. Reference: `src/server/api/Sprk.Bff.Api/Modules/CacheModule.cs`, ADR-009 (amended).
2. **Explicit opt-in for fallback.** `Redis:AllowInMemoryFallback` defaults `false`. Even the Development environment requires it `true` to use in-memory cache. The deployed dev App Service ships with `false`; in-memory mode is now a local-developer-laptop-only state.
3. **Null-Object `IConnectionMultiplexer`.** Symmetric DI registration (per [ADR-032](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md)) means consumers in dev see no-op Pub/Sub + an explicit `NotSupportedException` on direct database access — never a missing-service error. This eliminates an entire class of `IConnectionMultiplexer?` nullable-defensive code that previously masked degraded state.
4. **Canonical naming.** `spaarke-bff-redis-{env}` (top-level env-suffix) per NFR-03 makes off-pattern legacy instances visible at a glance in resource lists and lifecycle scripts.
5. **Tenant-prefix mandatory in keys.** The canonical key format `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}` is enforced at every call site via the `ITenantCache` wrapper. System-level exceptions (feature flags, system config) are explicitly allow-listed with JSON-comment justification (NFR-08).
6. **App Insights observability.** Redis dependency telemetry (auto) + `cache.hits` / `cache.misses` / `cache.redis_call_duration_ms` custom metrics (wrapper-emitted) + three alert rules (§8: hit rate < 80%, P95 latency > 100 ms, memory > 80%) make any future degradation visible within minutes.
7. **Deployment checklist.** [`scripts/Deploy-RedisCache.ps1`](../../scripts/Deploy-RedisCache.ps1) (idempotent, multi-env, `-WhatIf` / `-VerifyOnly` / `-CutoverBffSettings` / `-Force` per NFR-01/05/06) and this runbook (§§1–9) let any future operator provision a new env Redis end-to-end in under 30 minutes (FR-19, Success Criterion #6).

---

## 11. Cross-References

- [`docs/architecture/caching-architecture.md`](../architecture/caching-architecture.md) — design rationale: Tenant Isolation, Multi-instance Behavior, Cache Instance Registry, Failure Mode Catalog.
- [`.claude/adr/ADR-009-redis-caching.md`](../../.claude/adr/ADR-009-redis-caching.md) — concise ADR-009 constraints (MUST / MUST NOT).
- [`docs/adr/ADR-009-caching-redis-first.md`](../adr/ADR-009-caching-redis-first.md) — full ADR-009 rationale.
- [`scripts/Deploy-RedisCache.ps1`](../../scripts/Deploy-RedisCache.ps1) — provisioning automation (idempotent, multi-env, `-WhatIf`, `-VerifyOnly`, `-CutoverBffSettings`, `-Force`).
- [`tests/manual/RedisValidationTests.ps1`](../../tests/manual/RedisValidationTests.ps1) — validation harness (extended with `Test-TenantPrefixInvariant` and `Test-FailFastBehavior` in task 026).
- [`infrastructure/bicep/modules/redis.bicep`](../../infrastructure/bicep/modules/redis.bicep) — IaC module.
- [`infrastructure/bicep/parameters/redis-dev.bicepparam`](../../infrastructure/bicep/parameters/redis-dev.bicepparam) — dev parameter file (staging / prod parameter files follow the same shape).

---

*This guide is the operational source of truth for Redis cache management across all Spaarke environments. Updates SHOULD accompany any change to `Deploy-RedisCache.ps1`, `redis.bicep`, `RedisValidationTests.ps1`, or ADR-009.*
