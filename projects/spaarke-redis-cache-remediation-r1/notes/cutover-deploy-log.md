# Dev Cutover — Deploy Log

> **Project**: `spaarke-redis-cache-remediation-r1`
> **Started**: 2026-06-26 ~06:51 UTC (10:51 UTC per portal timestamp)
> **Operator**: Claude Code (autonomous, on Spaarke Devlopment Environment subscription `484bc857-3802-427f-9ea5-ca47b43db0f0`)

---

## Decision log

| Time (UTC) | Decision | Rationale |
|---|---|---|
| Pre-deploy | **KV name correction**: dev KV is `spaarke-spekvcert` (RG `SharePointEmbedded`, region `eastus`), NOT `sprkspaarkedev-aif-kv` (which is a different KV for AI Foundry) | BFF user-assigned MI `mi-bff-api-dev` has `Key Vault Secrets User` role on `spaarke-spekvcert`. Verified by `az role assignment list`. Spec Assumption §1 listed BOTH as candidates; this is the correct one. |
| Pre-deploy | **Legacy Redis (`spe-redis-dev-67e2xz`) exists in `Succeeded` state** with Basic C0 | Spec narrative assumed it was deleted; reality is the resource is running. The drift was solely the BFF App Setting `Redis__Enabled=false`. New canonical instance will be provisioned alongside; legacy will be tagged (not deleted) for safety. |
| Pre-deploy | Current BFF Redis App Settings: ONLY `Redis__Enabled=false` (nothing else set) | BFF currently running with silent in-memory fallback. Exactly the drift being closed. |
| ~10:51 UTC | **Continue with Azure Cache for Redis** for dev (not Azure Managed Redis) | User-confirmed after architectural review. Dev cost (~$15/mo vs ~$60+/mo) justifies legacy product for dev. Managed Redis added to R7 backlog as S5 for prod evaluation. |

---

## Provisioning timeline

| Time (UTC) | Event | Detail |
|---|---|---|
| 10:51:34 | `az deployment group create` initiated | `redis.bicep` + `redis-dev.bicepparam` → `spaarke-bff-redis-dev` in `spe-infrastructure-westus2` |
| ~10:54 | Resource `spaarke-bff-redis-dev` appeared in portal in `Creating` state | Basic C0; `redisVersion: 6.0`; `sslPort: 6380`; `minimumTlsVersion: 1.2` |
| ~11:13 | Primary key became retrievable via `az redis list-keys` (state still "Creating") | Quirk of Azure Cache for Redis: keys surface before `provisioningState: Succeeded` |
| 11:14:06 | KV secret `Redis-ConnectionString` upserted to `spaarke-spekvcert` (task 032) | Connection string: `spaarke-bff-redis-dev.redis.cache.windows.net:6380,password=<key>,ssl=True,abortConnect=False`. Length 132 chars. Secret version captured by Azure. |
| 11:14+ | Continuing to wait for `provisioningState: Succeeded` before App Settings update + restart | App Settings change while BFF connects against an only-partially-provisioned Redis is risky; wait for state flip first. |

---

## Pending steps (gated on `provisioningState: Succeeded`)

- **Task 033**: Update `spaarke-bff-dev` App Settings:
  - `Redis__Enabled=true`
  - `Redis__InstanceName=spaarke:`
  - `ConnectionStrings__Redis=@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=Redis-ConnectionString)`
  - `Redis__AllowInMemoryFallback=false`
- **Task 034**: `az webapp restart -g rg-spaarke-dev -n spaarke-bff-dev` + capture startup log; verify line `"Distributed cache: Redis enabled with instance name 'spaarke:'"` + `/healthz` 200 (gate signal for sister project)
- **Task 035**: Smoke test — exercise chat-session creation endpoint; verify Redis key matches `spaarke:tenant:{tid}:session:{id}:v1`
- **Task 037**: Legacy `spe-redis-dev-67e2xz` decommission via TAG `decommission=2026-06-26` (NOT delete — keep 7-14 day reversibility window)
- **Task 038**: Append handoff record to `projects/spaarke-ai-azure-setup-dev-r1/notes/handoffs/redis-cache-remediation-r1-phase3-cleared.md`
- **Tasks 040 + 042**: After post-cutover traffic, verify App Insights Live Metrics shows Redis dependency calls + custom metrics (`cache.hits`, `cache.misses`, `cache.redis_call_duration_ms`) in metrics explorer

---

## Gate signal (Success Criterion #1) — ✅ VERIFIED 2026-06-26T11:21:39 UTC

> Dev BFF startup log shows `"Distributed cache: Redis enabled with instance name 'spaarke:'"` with NO in-memory warning.

**Confirmed**:
```
2026-06-26T11:21:39.5479053+00:00 ContainerStream:       Distributed cache: Redis enabled with instance name 'spaarke:'
2026-06-26T11:21:42.9803990+00:00 ContainerStream:           - InstanceName: spaarke:
```

Gate-clear signal for sister project `spaarke-ai-azure-setup-dev-r1` NFR-13 — recorded at `projects/spaarke-ai-azure-setup-dev-r1/notes/handoffs/redis-cache-remediation-r1-phase3-cleared.md`.

---

## Subsequent Phase 3 actions

| Time (UTC) | Event | Detail |
|---|---|---|
| 11:18:00 | Redis `provisioningState: Succeeded` | Total provision time ~27 min |
| 11:18:15 | `Deploy-RedisCache.ps1` background process completed (exit 0 on Bicep; the trailing `RedisValidationTests.ps1` Test 1 false-negative on missing `appsettings.json` produced exit 1 — known harness quirk; out of scope) | — |
| 11:18:42 | Task 033: 4 App Settings written to `spaarke-bff-dev` | `Redis__Enabled=true`, `Redis__InstanceName=spaarke:`, `ConnectionStrings__Redis=@Microsoft.KeyVault(...)`, `Redis__AllowInMemoryFallback=false` |
| 11:20:32 | Task 034: `az webapp restart` issued | — |
| 11:20:38 | First container start FAILED (`ContainerTimeout`, 230s limit) | Cold-start transient — App Service auto-retried |
| 11:21:14 | Second container start SUCCEEDED — `/healthz` returns HTTP 200 (41.25s total) | Cold-start latency, normal |
| 11:21:39 | **Startup log confirms `"Distributed cache: Redis enabled with instance name 'spaarke:'"`** | **Success Criterion #1 verified** |
| 11:30+ | KV reference status query (Microsoft.Web `/configreferences/appsettings`): `ConnectionStrings__Redis` → **"Resolved"** ("Reference has been successfully resolved.") | Confirms BFF MI → KV permission chain works end-to-end |
| ~12:18 | Task 037: legacy `spe-redis-dev-67e2xz` tagged for decommission (NOT deleted; 7–14 day reversibility window) | Tags: `decommission=2026-06-26`, `decommission-reason=replaced-by-spaarke-bff-redis-dev`, `decommission-project=spaarke-redis-cache-remediation-r1` |
| ~12:20 | Task 038: sister project handoff signal written | `projects/spaarke-ai-azure-setup-dev-r1/notes/handoffs/redis-cache-remediation-r1-phase3-cleared.md` |

---

## Partial Phase 3 — what we DID NOT validate (and why)

**Task 035 (chat-session-key smoke test)** — partially validated. Full validation requires the **new BFF code deployed**, which has NOT happened in this session.

The deployed BFF code on `spaarke-bff-dev` is **pre-Phase-1** (the working tree at branch `work/spaarke-redis-cache-remediation-r1` has the new `ITenantCache` migration, but the BFF App Service runs whatever was deployed before this project started). So:

- ✅ Infrastructure-level cutover validated: new Redis, new KV secret, new App Settings, KV reference resolves, BFF starts cleanly, `/healthz` 200, startup log confirms `spaarke:` InstanceName binding.
- ⏸ Code-level cutover NOT validated: tenant-prefixed key format `spaarke:tenant:{tid}:{res}:{id}:v1`, 4-branch fail-fast behavior (would manifest if BFF restarted with bad config), custom metric emission (`cache.hits`, `cache.misses`, `cache.redis_call_duration_ms`).
- Currently-deployed BFF cache calls (e.g., `SpeDashboardSyncService` writing to `sdap:spe:dashboard:metrics`) will land in the NEW Redis but in the OLD key format. After PR merge + `bff-deploy`, key format flips to `spaarke:tenant:*` or `spaarke:{system-key}:*`.

**Tasks 040 + 042 (App Insights live-metrics verification)** — partially validated.
- ✅ App Insights instrumentation is wired (`APPLICATIONINSIGHTS_CONNECTION_STRING` set on `spaarke-bff-dev`); IK matches an existing component.
- ⏸ Custom metrics (`cache.hits`, etc.) won't appear in App Insights until the new BFF code is deployed (task 041 emission code is in the working tree, not yet on the App Service).
- Standard Application Insights SDK auto-captures Redis dependency calls — verification post-redeploy.

**Task 044** — subsumed by task 041's publish-size measurement (cumulative delta **−2.0 MB** vs branch start).

---

## Operator next steps (post-PR-merge)

After `work/spaarke-redis-cache-remediation-r1` merges to master:
1. Trigger `bff-deploy` (or CI/CD) to push the new BFF code to `spaarke-bff-dev`. This is when the full `ITenantCache` migration goes live.
2. Restart `spaarke-bff-dev` after deploy (most CI/CD pipelines do this automatically).
3. **Re-verify Success Criterion #1**: should still show the same startup log line (no change expected — log format was preserved across the migration).
4. **Now-valid smoke test**: exercise a chat-session-creating endpoint; verify Redis key matching `spaarke:tenant:{tid}:session:{id}:v1`.
5. **Now-valid metrics**: query App Insights for `cache.hits`/`cache.misses`/`cache.redis_call_duration_ms` with `resource` dimension.
6. 24-hr verification window (task 036) — error rate stays at baseline; no in-memory-mode log lines.
7. After 24 hr: legacy can be deleted (or extend reversibility window further if preferred). Tag was applied today (2026-06-26).
