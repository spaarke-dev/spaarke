# Dev Cutover — Baseline (Task 030)

> **Captured**: 2026-06-26
> **Operator**: Claude Code (autonomous, on Spaarke Devlopment Environment subscription)
> **Subscription**: `484bc857-3802-427f-9ea5-ca47b43db0f0` (Spaarke Devlopment Environment)
> **Tenant**: `a221a95e-6abc-4434-aecc-e48338a1b2f2`

---

## BFF App Service

| Field | Value |
|---|---|
| Name | `spaarke-bff-dev` |
| Resource Group | `rg-spaarke-dev` |
| State | Running |
| App Service Plan | `spaarke-dev-plan` |

### Managed Identity
- **Type**: User-assigned
- **MI Name**: `mi-bff-api-dev`
- **MI Resource Group**: `spe-infrastructure-westus2`
- **MI Client ID**: `5967251e-171c-46fe-a6c2-ef843c90309d`
- **MI Principal ID**: `9fd47efb-7962-492b-ac44-e5ccd0268ebb`

---

## Current Redis App Settings (PRE-CUTOVER)

```json
[
  {
    "name": "Redis__Enabled",
    "slotSetting": false,
    "value": "false"
  }
]
```

**Material observation**: Only `Redis__Enabled=false` is set. Missing:
- `ConnectionStrings__Redis`
- `Redis__InstanceName`
- `Redis__AllowInMemoryFallback`

This is the DRIFT this project closes: BFF is currently running with **silent in-memory fallback** (no `AllowInMemoryFallback` opt-in, but the OLD `CacheModule` code didn't enforce that — the new 4-branch logic does). After Phase 1 deploy, restarting BFF in this state would throw at startup per FR-02/FR-03 — which is the correct behavior.

---

## Key Vault (resolved)

**Critical correction to spec Assumption §1**: spec named `spaarke-spekvcert` (correct!). Alternate `sprkspaarkedev-aif-kv` exists but is a **different** KV (AI Foundry use).

| KV Field | Value |
|---|---|
| Name | `spaarke-spekvcert` |
| Resource Group | `SharePointEmbedded` |
| Location | `eastus` (BFF + Redis are westus2 — KV references work cross-region) |
| Mode | RBAC (`enableRbacAuthorization: true`) |

### BFF MI permissions on `spaarke-spekvcert`
- ✅ Role: `Key Vault Secrets User`
- Scope: `/subscriptions/.../resourceGroups/SharePointEmbedded/providers/Microsoft.KeyVault/vaults/spaarke-spekvcert`

### Existing secrets on `spaarke-spekvcert`
Includes (non-exhaustive): `ai-openai-endpoint`, `ai-openai-key`, `ai-search-endpoint`, `ai-search-key`, `application-insights-key`, `bff-api-client-secret`, `BFF-API-ClientSecret`, `Graph-API-ClientSecret`, `MANAGED-IDENTITY-CLIENT-ID`, `ServiceBus-ConnectionString`, `spe-app-cert`, `SPE-ContainerTypeId`, `SPRK-DEV-DATAVERSE-URL`, etc.

**`Redis-ConnectionString` does NOT yet exist** in `spaarke-spekvcert` — task 032 will create it.

---

## Legacy Redis (`spe-redis-dev-67e2xz`)

| Field | Value |
|---|---|
| Name | `spe-redis-dev-67e2xz` |
| Resource Group | `spe-infrastructure-westus2` |
| Location | westus2 |
| State | **Succeeded** (running) |
| SKU | Basic |
| Capacity | 0 (C0) |
| HostName | `spe-redis-dev-67e2xz.redis.cache.windows.net` |

**Material correction to spec narrative**: spec assumed the legacy was deleted. Reality is the resource **still exists in Succeeded state** — the drift was the BFF App Setting `Redis__Enabled=false`, not the resource being missing.

This changes the cutover slightly: spec planned to provision NEW + decommission legacy. Reality is provision NEW (canonical name) + decommission legacy (tag for safety, since it might have data). Per spec FR-15 Q3 "legacy is empty, no data to verify" — but I cannot verify this without redis-cli access.

---

## Cutover Plan (revised based on baseline)

1. **Task 031**: Provision `spaarke-bff-redis-dev` (Basic C0) in `spe-infrastructure-westus2` RG via `Deploy-RedisCache.ps1 -Environment dev`.
2. **Task 032**: Upsert `Redis-ConnectionString` in `spaarke-spekvcert` (RG `SharePointEmbedded`) with new instance's connection string.
3. **Task 033**: Set 4 App Settings on `spaarke-bff-dev`:
   - `Redis__Enabled=true`
   - `Redis__InstanceName=spaarke:`
   - `ConnectionStrings__Redis=@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=Redis-ConnectionString)`
   - `Redis__AllowInMemoryFallback=false`
4. **Task 034**: Restart BFF + verify startup log line (Success Criterion #1).
5. **Task 035**: Smoke test — chat session creation produces `spaarke:tenant:{tid}:session:{id}:v1` key.
6. **Task 036**: 24-hr verification window (this session opens it; close depends on operator).
7. **Task 037**: Decommission legacy by TAG (not delete) — keep reversibility for 7-14 days.

## Risks

- **Cross-RG MI permissions**: BFF MI in `spe-infrastructure-westus2`, KV in `SharePointEmbedded`. ✅ Already verified (Key Vault Secrets User role exists).
- **Cross-region KV reference**: BFF in westus2 reads from eastus KV. ✅ Supported by App Service.
- **Legacy Redis might have data**: spec says empty; cannot verify without redis-cli. Mitigated by tag-only decommission (no delete during session).
- **Bicep deployment may fail**: first time running `Deploy-RedisCache.ps1` in this subscription. If failure, `-WhatIf` plan to diagnose before re-running.
