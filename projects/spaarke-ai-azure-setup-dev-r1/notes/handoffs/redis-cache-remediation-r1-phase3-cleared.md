# Handoff: `spaarke-redis-cache-remediation-r1` Phase 3 Cleared

> **From**: `projects/spaarke-redis-cache-remediation-r1`
> **To**: `projects/spaarke-ai-azure-setup-dev-r1` (this project — gate satisfied)
> **Date**: 2026-06-26
> **Operator**: Claude Code (autonomous, on Spaarke Devlopment Environment subscription `484bc857-3802-427f-9ea5-ca47b43db0f0`)

---

## Gate signal cleared

Per **NFR-11** of `spaarke-redis-cache-remediation-r1` (and **NFR-13** of this sister project), this project's Phase 3 (Deploy Infrastructure) was BLOCKED until the Redis remediation's Phase 3 cutover completed and Success Criterion #1 was verified.

**Success Criterion #1 verified at 2026-06-26T11:21:39 UTC**:

```
2026-06-26T11:21:39.5479053+00:00 ContainerStream:       Distributed cache: Redis enabled with instance name 'spaarke:'
2026-06-26T11:21:42.9803990+00:00 ContainerStream:           - InstanceName: spaarke:
```

This sister project (`spaarke-ai-azure-setup-dev-r1`) can now proceed with its Phase 3 work.

---

## What changed in the Spaarke dev environment

### New Azure resources
- **`spaarke-bff-redis-dev`** (Basic C0, `spe-infrastructure-westus2`) — canonical-named Redis Cache for Redis. Provisioned via `pwsh ./scripts/Deploy-RedisCache.ps1 -Environment dev` at ~10:51 UTC; reached `provisioningState: Succeeded` at ~11:18 UTC.
  - Endpoint: `spaarke-bff-redis-dev.redis.cache.windows.net:6380` (SSL)
  - Redis version: 6.0
  - InstanceName binding: `spaarke:`

### Key Vault secret
- **`Redis-ConnectionString`** added to `spaarke-spekvcert` (RG `SharePointEmbedded`, eastus). KV reference syntax: `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=Redis-ConnectionString)`. Verified resolved status via `Microsoft.Web/sites/config/configreferences` API.

### `spaarke-bff-dev` App Settings
Now configured for production-like caching:
- `Redis__Enabled=true`
- `Redis__InstanceName=spaarke:`
- `Redis__AllowInMemoryFallback=false`
- `ConnectionStrings__Redis=@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=Redis-ConnectionString)` (resolves to the new Redis)

### Legacy `spe-redis-dev-67e2xz`
Tagged for decommission (NOT deleted — kept 7–14 days for reversibility):
- `decommission=2026-06-26`
- `decommission-reason=replaced-by-spaarke-bff-redis-dev`
- `decommission-project=spaarke-redis-cache-remediation-r1`

---

## What did NOT change (yet)

The **deployed BFF code is still pre-Phase-1** (the `ITenantCache` wrapper migration is in the PR branch `work/spaarke-redis-cache-remediation-r1` at commit `8b2cdb676` but is not deployed to `spaarke-bff-dev` yet).

This means:
- The cutover validated the **infrastructure level**: new Redis, KV reference, App Settings, BFF reaches new Redis, startup log shows expected line.
- It did NOT validate the **code level**: tenant-prefixed key format `spaarke:tenant:{tid}:{res}:{id}:v1`, 4-branch fail-fast logic, custom metric emission. These require `bff-deploy` after the PR merges.
- Cache keys produced by the currently-deployed BFF code (e.g., `SpeDashboardSyncService` caching to key `sdap:spe:dashboard:metrics`) will land in the new Redis but in the OLD format. After PR merge + redeploy, key format flips to `spaarke:tenant:*` / `spaarke:{system-key}:*`.

For sister project: this is fine. Sister project depends on dev BFF being Redis-connected (✓) and `spaarke-spekvcert` being usable for secret references (✓). Sister project does NOT depend on the BFF code-level migration.

---

## Useful references

- **Cutover deploy log**: [`../../../spaarke-redis-cache-remediation-r1/notes/cutover-deploy-log.md`](../../../spaarke-redis-cache-remediation-r1/notes/cutover-deploy-log.md)
- **Pre-cutover baseline**: [`../../../spaarke-redis-cache-remediation-r1/notes/dev-cutover-baseline.md`](../../../spaarke-redis-cache-remediation-r1/notes/dev-cutover-baseline.md) (esp. KV name correction — `spaarke-spekvcert` is the dev KV, NOT `sprkspaarkedev-aif-kv`)
- **Operational runbook**: [`../../../../docs/guides/redis-cache-azure-setup.md`](../../../../docs/guides/redis-cache-azure-setup.md)
- **Amended ADR-009**: [`../../../../docs/adr/ADR-009-caching-redis-first.md`](../../../../docs/adr/ADR-009-caching-redis-first.md) (Last Updated 2026-06-26 with 8 operational MUSTs)
- **R7 backlog including S5 — Azure Managed Redis evaluation**: [`../../../spaarke-redis-cache-remediation-r1/notes/r7-backlog.md`](../../../spaarke-redis-cache-remediation-r1/notes/r7-backlog.md)
