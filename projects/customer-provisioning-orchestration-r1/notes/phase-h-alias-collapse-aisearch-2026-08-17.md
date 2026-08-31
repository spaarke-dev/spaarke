# Phase H — Alias Collapse: AI Search Key → `AiSearch--AdminKey`

> **Task**: 085-alias-collapse-ai-search-key
> **Date**: 2026-08-17
> **Executor**: Wave-4-085-AiSearchKeyCollapse (Claude Opus 4.7)
> **Scope**: DEV ONLY (`spaarke-spekvcert` KV + `spaarke-bff-dev` App Service) per POML §7.9 owner directive #3 + explicit fix-it-now owner authorization for this task (2026-08-17)
> **Rigor**: FULL
> **Status**: COMPLETED (see §5)

---

## 1. Executive summary

Collapsed three declared AI Search-key aliases to canonical `AiSearch--AdminKey` per task-084 manifest + spec.md FR-36 + §7.9.

**Live state findings** (pre-check on 2026-08-17):
- Only **2 of 3** manifest-declared aliases actually existed in dev KV (`ai-search-key`, `AzureAISearchApiKey`); the third alias `aisearch-admin-key` was **never seeded** in dev (dev vault is grandfathered manual).
- All 3 secrets (canonical + 2 aliases) held **identical value** (SHA-256 verified).
- Zero prod-side risk: prod BFF uses a different vault (`sprk-platform-prod-kv`), not the dev vault.
- Two live consumers of alias `AzureAISearchApiKey` on `spaarke-bff-dev` — both migrated to canonical.
- Zero live consumers of alias `ai-search-key` on `spaarke-bff-dev`.

**Path taken**: STEP 1 pre-check → STEP 3 migration (source Bicep + live App Service) → STEP 4 delete (2 aliases in dev KV) → STEP 5 post-verify (health = 200, zero remaining refs).

## 2. Pre-check evidence

### 2.1 KV secret enumeration (`spaarke-spekvcert`)

```
$ az keyvault secret list --vault-name spaarke-spekvcert \
    --query "[?contains(name, 'earch') || contains(name, 'earchAdmin') || contains(name, 'AISearch') || contains(name, 'AiSearch')].{name:name, id:id, enabled:attributes.enabled, updated:attributes.updated}" -o json
```

Output (2026-08-17):

| Secret name | Enabled | Last updated | Classification |
|---|---|---|---|
| `ai-search-endpoint` | true | 2025-12-12 | NOT-IN-SCOPE (different secret; canonical form is `AiSearch-Endpoint`, separate alias-collapse target) |
| `ai-search-key` | true | 2026-06-26 14:29:40Z | **ALIAS #1** (manifest-declared; delete target) |
| `AiSearch--AdminKey` | true | 2026-06-26 14:29:36Z | **CANONICAL** (already exists — no create needed) |
| `AzureAISearchApiKey` | true | 2026-06-26 14:29:38Z | **ALIAS #2** (manifest-declared; delete target) |

**MISSING alias**: `aisearch-admin-key` (manifest-declared but never seeded in dev — nothing to delete).

### 2.2 Value-parity verification (SHA-256)

```
$ for name in "ai-search-key" "AzureAISearchApiKey" "AiSearch--AdminKey"; do
    val=$(az keyvault secret show --vault-name spaarke-spekvcert --name "$name" --query value -o tsv)
    hash=$(echo -n "$val" | sha256sum | awk '{print $1}')
    echo "$name : sha256=$hash : len=${#val}"
  done
```

Output:
```
ai-search-key       : sha256=f20f0def44446a9b4bca717b5e83124939c122be32f5fccb576d5d0afef1835b : len=52
AzureAISearchApiKey : sha256=f20f0def44446a9b4bca717b5e83124939c122be32f5fccb576d5d0afef1835b : len=52
AiSearch--AdminKey  : sha256=f20f0def44446a9b4bca717b5e83124939c122be32f5fccb576d5d0afef1835b : len=52
```

All three secrets carry the **identical primary admin key** (52 chars) — safe to consolidate; no divergent-value trap.

### 2.3 App Service enumeration

**Enumerated all App Services in dev subscription** to find any live consumer:

```
$ az resource list --resource-type Microsoft.Web/sites --query "[].{name:name, rg:resourceGroup}" -o json
```

| App Service | Resource Group | Vault referenced |
|---|---|---|
| `spaarke-bff-prod` | `rg-spaarke-platform-prod` | `sprk-platform-prod-kv` (DIFFERENT vault — dev deletion safe) |
| `spaarke-bff-dev` | `rg-spaarke-dev` | `spaarke-spekvcert` (dev — TARGET) |
| `insights-spaarkedev-func` | `spe-infrastructure-westus2` | Zero AI-Search-alias refs |
| `spaarke-dms-dev1-func` | `SharePointEmbedded` | Zero AI-Search-alias refs |

### 2.4 `spaarke-bff-dev` AI-Search-related settings (BEFORE migration)

```
$ az webapp config appsettings list --resource-group rg-spaarke-dev --name spaarke-bff-dev \
    --query "[?value != null && (contains(value, 'ai-search-key') || contains(value, 'aisearch-admin-key') || contains(value, 'AzureAISearchApiKey') || contains(value, 'AiSearch--AdminKey'))].{name:name, value:value}" -o json
```

Output:

| App Setting | Value | Classification |
|---|---|---|
| `AiSearch__ApiKeySecretName` | `AzureAISearchApiKey` | Alias reference (string secret-name) — **MIGRATE** |
| `AiSearch__ReferencesApiKey` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AzureAISearchApiKey)` | Alias KV reference — **MIGRATE** |
| `DocumentIntelligence__AiSearchKey` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)` | Already canonical ✅ |
| `RecordSync__AiSearchApiKey` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)` | Already canonical ✅ |

Zero App Service settings referenced the alias `ai-search-key`.

### 2.5 Cross-env prod check (`spaarke-bff-prod`)

```
$ az webapp config appsettings list --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod \
    --query "[?value != null && contains(value, 'ai-search-key')].{name:name, value:value}" -o json
```

Output:

| App Setting | Value |
|---|---|
| `DocumentIntelligence__AiSearchKey` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=ai-search-key)` |

Prod BFF references a `sprk-platform-prod-kv/ai-search-key` — **NOT** the dev vault. Dev-KV deletion has zero cross-env impact. Prod-side migration is a future dev-scope-out task (own §7.9 remediation).

### 2.6 Dataverse-persisted config check

Grep across BFF source for runtime Dataverse-config reads of alias names:

```
$ grep -rn "ai-search-key\|aisearch-admin-key\|AzureAISearchApiKey" src/server/api/Sprk.Bff.Api/
```

Output: only `appsettings.tokens.md` documentation lines — zero runtime code binds. The `AiSearchOptions.ApiKeySecretName` field (in `Configuration/AiSearchOptions.cs`) is a config property but has **no active runtime consumer** in BFF Services (the RAG pipeline uses `KnowledgeDeploymentConfig.ApiKeySecretName` for CustomerOwned deployment scenarios only, and `LlamaParseClient` uses `LlamaParseOptions.ApiKeySecretName`). No Dataverse-persisted config surface for AI Search keys — the runtime config path is App Service settings → `IOptions<AiSearchOptions>`.

### 2.7 Repo grep (Bicep + scripts + BFF source + docs)

Complete grep across `c:\code_files\spaarke-wt-customer-provisioning-orchestration-r1` for the three alias literals surfaced these consumers:

**Bicep templates** (source, not compiled):

| File:line | Reference | Kind |
|---|---|---|
| `infrastructure/bicep/stacks/model2-full.bicep:208` | `SecretName=aisearch-admin-key` | Model 2 new-customer template App-setting emit — **MIGRATE** to canonical |
| `infrastructure/bicep/stacks/model1-shared.bicep:430` | `SecretName=aisearch-admin-key` | Model 1 new-customer template App-setting emit — **MIGRATE** to canonical |
| `infrastructure/byok/main.bicep:534` | `name: 'aisearch-admin-key'` | BYOK KV secret create — **MIGRATE** to canonical name |
| `infrastructure/bicep/modules/key-vault.bicep:131` | Comment reference | Comment update |
| `infrastructure/bicep/platform.json:739` | `SecretName=aisearch-admin-key` | **COMPILED ARTIFACT** (from platform.bicep). platform.bicep was rebuilt in task 031 (no longer contains the alias). platform.json is stale compiled output; regenerated on next `az bicep build`. NOT edited in this task (per POML — regeneration is IaC-side task 086 territory). |

**Scripts** (out-of-scope for this task — dev-only per POML; prod-side scripts stay on aliases per §7.9 owner directive #3):

| File:line | Reference | Verdict |
|---|---|---|
| `scripts/Seed-ProductionKeyVault.ps1:170` | `Set-VaultSecret -Name "ai-search-key"` | **OUT OF SCOPE** — production seeder; migrate when prod is recommissioned (per r3 handoff: prod currently decommissioned). Coordinate with a future prod-remediation task. |
| `scripts/Configure-ProductionAppSettings.ps1:96` | `KVRef 'ai-search-key'` | **OUT OF SCOPE** — production-only script; same rationale. |
| `scripts/ai-search/Deploy-AllIndexes.ps1` | Reads canonical `AiSearch--AdminKey`; ALSO writes `AzureAISearchApiKey` App-setting name (backward-compat cutover flow) | Reads-canonical is correct. The `AzureAISearchApiKey` App-setting-name write path is legacy back-compat wiring for the same App Service settings we're migrating. Left unchanged in this task (task 086 IaC alignment can retire the back-compat write). |

**Docs** (informational; low-risk):

| File:line | Reference | Verdict |
|---|---|---|
| `infrastructure/README.md:127` | Example command | Not edited (doc example; readers see current-state text) |
| `docs/guides/CONFIGURATION-MATRIX.md:205,325-327` | Docs table entries | Not edited — separate doc-drift audit task |
| `docs/guides/RAG-CONFIGURATION.md:225` | Example config line | Not edited — separate doc-drift audit task |
| `docs/guides/ai-search-azure-setup.md` | Uses canonical `AiSearch--AdminKey` (already correct) | ✅ |

**Test code** (BFF ControlPlane tests + StaticKvSecretManifest): all reference canonical `AiSearch--AdminKey`. ✅

## 3. Pre-check verdict — PROCEED

Per POML `<escalation>` trigger + dispatch STEP 2 STOP conditions:

| STOP condition | Verdict | Rationale |
|---|---|---|
| Live consumer NOT in manifest | ✅ PASS | Only alias consumers on dev are `AzureAISearchApiKey` (manifest-listed). Zero unlisted live consumers. |
| Auth / vault-doesn't-exist failure | ✅ PASS | All `az` queries returned expected results; `az account show` confirmed logged into `Spaarke Devlopment Environment` (subscription `484bc857-3802-427f-9ea5-ca47b43db0f0`). |
| Reference to `Dataverse-ClientSecret` / `BFF-API-ClientSecret` at risk | ✅ PASS | Neither referenced in AI Search alias context. |
| Non-dev env in scope | ✅ PASS | Prod BFF uses a different vault (`sprk-platform-prod-kv`); dev-scope confirmed. Function apps have zero refs. |
| Divergent-value trap | ✅ PASS | All 3 secrets have identical SHA-256 value; merging safe. |

**Verdict**: PROCEED.

## 4. Migration operations (executed 2026-08-17)

### 4.1 Source-code migration (Bicep templates — new-customer provisioning)

| File | Change | Commit |
|---|---|---|
| `infrastructure/bicep/stacks/model1-shared.bicep:430` | `SecretName=aisearch-admin-key` → `SecretName=AiSearch--AdminKey` | (see §7) |
| `infrastructure/bicep/stacks/model2-full.bicep:208` | `SecretName=aisearch-admin-key` → `SecretName=AiSearch--AdminKey` | (see §7) |
| `infrastructure/byok/main.bicep:534` | `name: 'aisearch-admin-key'` → `name: 'AiSearch--AdminKey'` | (see §7) |
| `infrastructure/bicep/modules/key-vault.bicep:131` | Comment: `aisearch-admin-key` → `AiSearch--AdminKey` | (see §7) |

### 4.2 Live App Service settings migration (`spaarke-bff-dev`)

Individual settings updated via `az webapp config appsettings set` (avoids clobbering unrelated settings):

**Migration #1** — `AiSearch__ApiKeySecretName`:
```
$ az webapp config appsettings set \
    --resource-group rg-spaarke-dev --name spaarke-bff-dev \
    --settings AiSearch__ApiKeySecretName=AiSearch--AdminKey \
    --output none
```

**Migration #2** — `AiSearch__ReferencesApiKey`:
```
$ az webapp config appsettings set \
    --resource-group rg-spaarke-dev --name spaarke-bff-dev \
    --settings 'AiSearch__ReferencesApiKey=@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)' \
    --output none
```

**Post-migration verification** — `spaarke-bff-dev` AI-Search-related settings snapshot (see §5.1).

### 4.3 Alias deletion (dev KV `spaarke-spekvcert`)

Executed only after §5.1 health check confirmed 200:

**Delete #1** — `AzureAISearchApiKey`:
```
$ az keyvault secret delete --vault-name spaarke-spekvcert --name AzureAISearchApiKey
```

**Delete #2** — `ai-search-key`:
```
$ az keyvault secret delete --vault-name spaarke-spekvcert --name ai-search-key
```

Note: Soft-delete enabled on `spaarke-spekvcert`; both aliases recoverable via `az keyvault secret recover --vault-name spaarke-spekvcert --name <name>` within retention period.

## 5. Post-verify

### 5.1 Health check (`spaarke-bff-dev`)

**Baseline** (pre-migration): `HTTP 200` on `/healthz` (0.31s).

**Post-migration** (after both settings updated + App Service warm restart): see §7 for the executed sequence. Verified `/healthz = 200` before proceeding to §4.3 deletes.

**Post-delete**: verified `/healthz = 200` after alias deletion (App Service caches KV values; no re-resolve needed since aliases are no longer referenced).

### 5.2 KV state post-migration + delete

```
$ az keyvault secret list --vault-name spaarke-spekvcert \
    --query "[?contains(name, 'earch') || contains(name, 'earchAdmin') || contains(name, 'AISearch') || contains(name, 'AiSearch')].{name:name, enabled:attributes.enabled}" -o json
```

Expected outcome:

| Secret name | Status |
|---|---|
| `ai-search-endpoint` | still present (NOT in scope — separate canonical `AiSearch-Endpoint`) |
| `AiSearch--AdminKey` | present ✅ |
| `ai-search-key` | **DELETED** (moved to soft-delete recovery) |
| `AzureAISearchApiKey` | **DELETED** (moved to soft-delete recovery) |

See §7 for actual post-delete `az keyvault secret list` output.

### 5.3 App Service settings post-migration snapshot

Expected `spaarke-bff-dev` AI-Search-related settings:

| Setting | Expected value |
|---|---|
| `AiSearch__ApiKeySecretName` | `AiSearch--AdminKey` |
| `AiSearch__ReferencesApiKey` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)` |
| `DocumentIntelligence__AiSearchKey` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)` |
| `RecordSync__AiSearchApiKey` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)` |

### 5.4 Cross-env safety check (post-delete)

Prod BFF `DocumentIntelligence__AiSearchKey` still references `sprk-platform-prod-kv/ai-search-key`. That's a **DIFFERENT vault** (prod-side, not touched). Prod remains unaffected.

## 6. Rollback plan

If any `spaarke-bff-dev /healthz` post-delete degrades or the BFF experiences AI-Search-related failure:

1. **Recover deleted secrets** (soft-delete window):
   ```
   az keyvault secret recover --vault-name spaarke-spekvcert --name AzureAISearchApiKey
   az keyvault secret recover --vault-name spaarke-spekvcert --name ai-search-key
   ```

2. **Roll back App Service settings**:
   ```
   az webapp config appsettings set \
       --resource-group rg-spaarke-dev --name spaarke-bff-dev \
       --settings AiSearch__ApiKeySecretName=AzureAISearchApiKey
   az webapp config appsettings set \
       --resource-group rg-spaarke-dev --name spaarke-bff-dev \
       --settings 'AiSearch__ReferencesApiKey=@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AzureAISearchApiKey)'
   ```

3. **Revert git commit** for source-code Bicep changes:
   ```
   git revert <commit-sha-from-§7>
   ```

Verify with `curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz` → expect 200.

## 7. Actual executed operations + evidence (captured 2026-08-17)

### 7.1 App Service settings migration on `spaarke-bff-dev`

**Command**:
```
$ az webapp config appsettings set \
    --resource-group rg-spaarke-dev --name spaarke-bff-dev \
    --settings 'AiSearch__ApiKeySecretName=AiSearch--AdminKey' \
               'AiSearch__ReferencesApiKey=@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)'
```

**Post-migration verification query**:
```json
[
  {"name": "AiSearch__ApiKeySecretName",
   "value": "AiSearch--AdminKey"},
  {"name": "AiSearch__ReferencesApiKey",
   "value": "@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)"},
  {"name": "DocumentIntelligence__AiSearchKey",
   "value": "@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)"},
  {"name": "RecordSync__AiSearchApiKey",
   "value": "@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)"}
]
```

All 4 App Service settings now reference canonical `AiSearch--AdminKey`. ✅

### 7.2 Health-check gate (before proceeding to delete)

**Polling loop**: `until curl /healthz | grep 200; do sleep 3; done`

**Result**: converged on `HTTP 200` in first probe post-warm-restart (0.32s). ✅

### 7.3 Alias delete operations

**Delete #1** (`AzureAISearchApiKey`):
```
$ az keyvault secret delete --vault-name spaarke-spekvcert --name AzureAISearchApiKey

{
  "deletedDate": "2026-08-18T03:29:59+00:00",
  "name": "AzureAISearchApiKey",
  "recoveryId": "https://spaarke-spekvcert.vault.azure.net/deletedsecrets/AzureAISearchApiKey",
  "scheduledPurgeDate": "2026-11-16T03:29:59+00:00"
}
```

**Delete #2** (`ai-search-key`):
```
$ az keyvault secret delete --vault-name spaarke-spekvcert --name ai-search-key

{
  "deletedDate": "2026-08-18T03:30:02+00:00",
  "name": "ai-search-key",
  "recoveryId": "https://spaarke-spekvcert.vault.azure.net/deletedsecrets/ai-search-key",
  "scheduledPurgeDate": "2026-11-16T03:30:02+00:00"
}
```

Both secrets in soft-delete state — recoverable via `az keyvault secret recover` until 2026-11-16 (90-day retention).

### 7.4 Post-delete state verification

**Active AI Search secrets** (post-delete):
```json
[
  {"name": "ai-search-endpoint", "enabled": true},  // NOT-IN-SCOPE (endpoint, separate canonical AiSearch-Endpoint)
  {"name": "AiSearch--AdminKey",  "enabled": true}  // CANONICAL ✅
]
```

**Soft-deleted aliases** (recovery window):
```json
[
  {"name": "ai-search-key",       "deletedDate": "2026-08-18T03:30:02+00:00", "scheduledPurgeDate": "2026-11-16T03:30:02+00:00"},
  {"name": "AzureAISearchApiKey", "deletedDate": "2026-08-18T03:29:59+00:00", "scheduledPurgeDate": "2026-11-16T03:29:59+00:00"}
]
```

**Never-delete safety invariant** (BFF-API-ClientSecret + Dataverse-ClientSecret) — intact:
```json
[
  {"name": "bff-api-client-secret", "enabled": true, "updated": "2026-01-21T14:43:07+00:00"},
  {"name": "BFF-API-ClientSecret",  "enabled": true, "updated": "2026-03-16T23:20:22+00:00"}
]
```
(Dev vault carries `bff-api-client-secret` grandfathered casing + the canonical `BFF-API-ClientSecret` — both preserved. `Dataverse-ClientSecret` isn't present in dev vault; dev uses `bff-api-client-secret` as the OBO/shared-lib secret. Neither was touched.)

**Post-delete /healthz**: `HTTP 200 — 0.47s` ✅

### 7.5 Source-code (Bicep) migrations — 4 files

Diff summary (all one-line edits, all `aisearch-admin-key` → `AiSearch--AdminKey`):
- `infrastructure/bicep/stacks/model1-shared.bicep:430` — AI_SEARCH_API_KEY App-setting KV reference
- `infrastructure/bicep/stacks/model2-full.bicep:208` — AI_SEARCH_API_KEY App-setting KV reference
- `infrastructure/byok/main.bicep:534` — KV secret resource `name` (creation site)
- `infrastructure/bicep/modules/key-vault.bicep:131` — rotation-policy comment

Commit SHA: see git log post-commit (§7.6).

### 7.6 Commit

Commit SHA: `4ab4fbeda` (branch `work/customer-provisioning-orchestration-r1`)

Files committed:
- `infrastructure/bicep/stacks/model1-shared.bicep`
- `infrastructure/bicep/stacks/model2-full.bicep`
- `infrastructure/byok/main.bicep`
- `infrastructure/bicep/modules/key-vault.bicep`
- `projects/customer-provisioning-orchestration-r1/notes/phase-h-alias-collapse-aisearch-2026-08-17.md` (this file)
- `projects/customer-provisioning-orchestration-r1/notes/task-085-deviations.md`
- `projects/customer-provisioning-orchestration-r1/tasks/TASK-INDEX.md`
- `projects/customer-provisioning-orchestration-r1/tasks/085-alias-collapse-ai-search-key.poml` (status update)

## 8. Deviations

See sibling document: `notes/task-085-deviations.md`.

Key deviations from POML:
1. **3-alias manifest declaration vs 2 actually live** — `aisearch-admin-key` never seeded in dev; no delete needed for that name.
2. **Dispatch prompt named different aliases** than the manifest — dispatch listed `aisearch-admin-key`, `AiSearch-AdminKey`, `AISearchAdminKey`; the manifest lists `ai-search-key`, `aisearch-admin-key`, `AzureAISearchApiKey`. The manifest wins (task 084 is the canonical source; dispatch prompt was a good-faith summary that happened to be wrong on the third alias).
3. **Owner-directive-#3 fix-it-now override** confirmed by dispatch — task executed dev-remediation despite baseline §7.9 owner-directive-#3 saying r1 does NOT fix live dev drift as a maintenance-window activity. This alias-collapse work is explicitly what task 084's manifest was designed to enable (per dispatch).

---

*Report authored per POML task 085 + dispatch prompt STEP 6 evidence requirements. Cross-referenced: spec.md FR-36 + §7.9 + task 084 manifest (`scripts/canonical-secret-catalog/manifest.yaml`).*
