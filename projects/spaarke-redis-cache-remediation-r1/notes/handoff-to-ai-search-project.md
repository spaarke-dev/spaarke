# Handoff to `spaarke-ai-azure-setup-dev-r1` (AI Search project)

> Authoritative outputs from `spaarke-redis-cache-remediation-r1` — Phase 3 gate cleared per NFR-13.

## 1. Completion confirmation

- **PRs merged to master**: [#458](https://github.com/spaarke-dev/spaarke/pull/458)
- **Merge SHA**: `567b98112` (merge commit `Merge pull request #458 from spaarke-dev/work/spaarke-redis-cache-remediation-r1`) on 2026-06-26
- **BFF deployed**: `Deploy-BffApi.ps1` 2026-06-26T12:24 UTC (hash-verify 4/4 critical files matched; healthz 200)
- **Phase 3 Success Criterion #1 (post-deploy)**:
  ```
  2026-06-26T12:24:57.2962340+00:00 Distributed cache: Redis enabled with instance name 'spaarke:'
  ```
- **Open follow-ups**: Legacy `spe-redis-dev-67e2xz` tagged `decommission=2026-06-26` — delete after 24-hr verification window (operator). R7 backlog tracks S1-S5 stretch (Entra ID auth, Pub/Sub separation, geo-replication, other secrets, **Azure Managed Redis evaluation for prod**).

## 2. Final canonical Redis instance (dev)

Shipped as-is per spec. **NOT** renamed.

| Field | Value |
|---|---|
| Name | `spaarke-bff-redis-dev` |
| Resource group | `spe-infrastructure-westus2` |
| Region | `westus2` |
| Tier | **Basic C0** (confirmed; ~$15/mo) |
| Redis version | 6.0 |
| Endpoint | `spaarke-bff-redis-dev.redis.cache.windows.net:6380` (SSL only) |

```bash
az redis show -g spe-infrastructure-westus2 -n spaarke-bff-redis-dev \
  --query "{name:name, state:provisioningState, sku:sku.name, capacity:sku.capacity}"
# → {"name":"spaarke-bff-redis-dev","state":"Succeeded","sku":"Basic","capacity":0}
```

## 3. Exact KV vault + secret name (dev) — spec assumption WAS WRONG

**Use `spaarke-spekvcert`, NOT `sprkspaarkedev-aif-kv`.** `sprkspaarkedev-aif-kv` is a different KV (AI Foundry). BFF MI does NOT have role on it.

| Field | Value |
|---|---|
| Vault name | `spaarke-spekvcert` |
| Vault resource group | `SharePointEmbedded` (region `eastus` — cross-region KV ref to westus2 works) |
| Secret name | `Redis-ConnectionString` |
| Mode | RBAC (`enableRbacAuthorization: true`) |
| BFF MI role | `Key Vault Secrets User` (scope = KV resource) |

**Request the same `Key Vault Secrets User` role for the AI-Search project's secrets — same vault is the recommended path** (avoids new-vault sprawl).

## 4. KV-reference syntax in `spaarke-bff-dev` App Settings

Used the **`VaultName=;SecretName=`** form (not `SecretUri=...`) — matches existing Spaarke pattern in the same App Service. KV-reference resolution status verified `"Resolved"` via `Microsoft.Web/sites/config/configreferences` API.

```
App Setting key:   ConnectionStrings__Redis
App Setting value: @Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=Redis-ConnectionString)
```

Plus 3 non-KV settings: `Redis__Enabled=true`, `Redis__InstanceName=spaarke:`, `Redis__AllowInMemoryFallback=false`.

**Recommendation for AI-Search**: use `VaultName=;SecretName=` form for consistency.

## 5. Bicep-vs-PowerShell — both

- **Bicep module**: `infrastructure/bicep/modules/redis.bicep` (extended with `redisVersion`, `staticIP`, `redisPrimaryKey` output)
- **Bicep params (env-typed)**: `infrastructure/bicep/parameters/redis-{dev,staging,prod}.bicepparam`
- **PowerShell orchestrator**: `scripts/Deploy-RedisCache.ps1` — wraps `az deployment group create`, KV upsert, App Settings cutover, post-deploy validation
- **Pattern**: PowerShell calls Bicep; PS handles env-routing + KV secret + App Settings; Bicep is purely the resource shape

**Recommendation for AI-Search**: **Same pattern**. Bicep for the AI Search service + indexes (data-plane indexes if Bicep supports them; otherwise PowerShell for indexes), PS for env-routing + secret upserts + cross-resource wiring. Mirror `Deploy-RedisCache.ps1` structure.

## 6. Deploy script template

```powershell
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory)][ValidateSet('dev','staging','prod','demo')][string]$Environment,
    [string]$ResourceGroup,
    [string]$KeyVaultName,
    [switch]$VerifyOnly,
    [switch]$CutoverBffSettings,
    [switch]$Force      # required for prod/demo per NFR-05
)
```

Structure: `-Force` gate (NFR-05) → env→RG mapping → idempotency probe (`az redis show`) → `az deployment group create` against module+paramfile → `az redis list-keys` → build StackExchange-compatible connection string → `az keyvault secret set` (idempotent) → optional `az webapp config appsettings set` (cutover) → `RedisValidationTests.ps1` for post-deploy invariants. Returns non-zero on any failure; `-WhatIf` is native via `SupportsShouldProcess`. Reusable helpers worth lifting: the `if ($PSCmdlet.ShouldProcess(...))` pattern wrapping every destructive call.

## 7. Null-Object pattern + ADR-009 amendments

`src/server/api/Sprk.Bff.Api/Infrastructure/Cache/NullObjects/NullConnectionMultiplexer.cs` — per ADR-032:

```csharp
internal sealed class NullConnectionMultiplexer : IConnectionMultiplexer {
    public ISubscriber GetSubscriber(object? asyncState = null) => _nullSubscriber;
    public IDatabase GetDatabase(int db = -1, object? asyncState = null) => _nullDatabase;
    // NullSubscriber.Publish*: returns 0; NullSubscriber.Subscribe*: no-op
    // NullDatabase.* : throws NotSupportedException("In-memory cache mode does not
    //     support direct Redis database operations. Use IDistributedCache.")
}
```

**Symmetric DI** in `CacheModule.cs` 4-branch logic — REAL `IConnectionMultiplexer` (Redis-on) OR `NullConnectionMultiplexer` (dev-fallback). Never `if (flag) { register }` asymmetric.

**ADR-009 diff** (lockstep `.claude/adr/` + `docs/adr/`, both Last-Updated 2026-06-26):
- + SKU table (dev=Basic C0; staging=Standard C0; prod=Standard C2+ or Premium)
- + Connection string MUST be KV-referenced (no plain text)
- + Fail-fast at startup in deployed envs (`AbortOnConnectFail=true`)
- + Cache key MUST embed tenant: `{InstanceName}tenant:{tid}:{res}:{id}:v{n}`
- + Symmetric `IConnectionMultiplexer` registration (ADR-032 cross-ref)

## 8. Lessons-learned bottom line for AI-Search

- **Spec Assumption §1 in Redis project was wrong**: dev KV is `spaarke-spekvcert` (RG `SharePointEmbedded`, region `eastus`), NOT `sprkspaarkedev-aif-kv`. **Verify your spec's KV name assumption against `az role assignment list --all --assignee <BFF-MI-objectId>` before authoring tasks.**
- **Legacy Redis was NOT deleted before this project started** (spec narrative was wrong) — it was running but BFF App Setting was `Enabled=false`. **Verify legacy AI-Search resource state first** before assuming "needs restoration."
- **Redis cutover did NOT migrate any AI-Search hardcoded URLs/keys** — only `ConnectionStrings__Redis` (new), `Redis__Enabled`, `Redis__InstanceName`, `Redis__AllowInMemoryFallback`. Pre-existing settings unchanged. **FR-15 scope unchanged.**
- **KV admin-key freshness**: not encountered (fresh secret upsert; no rotation). For AI-Search: if you rename/recreate `spaarke-search-dev`, the OLD admin key will be invalid immediately — rotate the KV secret atomically with the resource swap.
- **§F.2 Fixture-Config-FIRST**: tightening DI at startup (e.g., AI-Search forcing KV refs at startup) WILL cause latent `WebApplicationFactory` test failures (we hit 337). Sweep test fixtures alongside production changes.

## 9. Prerequisite-gate checklist (run before AI-Search Phase 3 starts)

```bash
# 1) Redis provisioned
az redis show -g spe-infrastructure-westus2 -n spaarke-bff-redis-dev \
  --query "provisioningState" -o tsv
# expect: Succeeded

# 2) KV secret exists
az keyvault secret show --vault-name spaarke-spekvcert --name Redis-ConnectionString \
  --query "attributes.enabled" -o tsv
# expect: true

# 3) App Settings contain KV reference
az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev \
  --query "[?name=='ConnectionStrings__Redis'].value" -o tsv
# expect: @Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=Redis-ConnectionString)

# 4) BFF healthz
curl -sS -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz
# expect: 200

# 5) Startup log shows Redis-enabled (no in-memory warning)
az webapp log tail -g rg-spaarke-dev -n spaarke-bff-dev | grep -m 1 "Distributed cache"
# expect: "Distributed cache: Redis enabled with instance name 'spaarke:'"
```

All 5 passing = AI-Search Phase 3 unblocked.
