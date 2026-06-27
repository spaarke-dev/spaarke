# Pre-Phase-3 Operational Verification (FR-21 + NFR-13)

> **Task**: 001 — Pre-Phase-3 operational verification (10 checks)
> **Run date**: 2026-06-26
> **Operator**: ralph.schroeder@spaarke.com (Azure CLI)
> **Subscription**: 484bc857-3802-427f-9ea5-ca47b43db0f0 (spaarke-dev)
> **Status**: ✅ **PASS — 10 of 10 (after remediation 2026-06-26T14:31Z)**
>
> **Initial result**: 8/10 PASS; AI-1 + AI-2 FAIL. **Remediation applied 2026-06-26 after owner decision**:
> - AI-1 → Option C: set `AiSearch--AdminKey` (canonical), `AzureAISearchApiKey` (legacy alias, was missing), `ai-search-key` (legacy alias, was stale) all to LIVE primary key `[REDACTED-AZURE-SEARCH-KEY]`. FR-15 will retire legacy aliases post-migration.
> - AI-2 → Option B (interpreted): direct `az role assignment create` granted `Search Index Data Contributor` to BFF MI on `spaarke-search-dev`. The `byok/main.bicep` was determined to target the wrong identity (system-assigned MI) AND not be the actual dev BFF deployment authority (dev was deployed via `customer.bicep` which has NO Search role assignment). True authoritative Bicep fix is a Phase 4 follow-up task (add Search role-assignment module to customer.bicep + customer.json).

---

## Resource Inventory (discovered 2026-06-26)

| Component | Name | Resource Group | Location |
|---|---|---|---|
| AI Search service | `spaarke-search-dev` | `spe-infrastructure-westus2` | West US 2 |
| Redis (canonical) | `spaarke-bff-redis-dev` | `spe-infrastructure-westus2` | westus2 |
| Redis (legacy, decom'd) | `spe-redis-dev-67e2xz` | `spe-infrastructure-westus2` | westus2 |
| Key Vault (BFF secrets) | `spaarke-spekvcert` | `SharePointEmbedded` | eastus |
| BFF App Service | `spaarke-bff-dev` | `rg-spaarke-dev` | westus2 |
| BFF Managed Identity | `mi-bff-api-dev` (principalId `9fd47efb-7962-492b-ac44-e5ccd0268ebb`) | `spe-infrastructure-westus2` | — |
| Service Bus namespace | `spaarke-servicebus-dev` | `SharePointEmbedded` | eastus |
| Azure OpenAI | `spaarke-openai-dev` (kind=AIServices) | `spe-infrastructure-westus2` | eastus |

**MI confirmation**: BFF uses **only** user-assigned `mi-bff-api-dev`. System-assigned identity is `null`. Any role-assignment Bicep that uses `appService.identity.principalId` would target the system-assigned MI and grant no effective access (this is relevant to AI-2 remediation choice below).

---

## Section A — Redis Prerequisites (NFR-13)

### ✅ Check R-1: Redis provisioned

```bash
$ az redis show -g spe-infrastructure-westus2 -n spaarke-bff-redis-dev --query "provisioningState" -o tsv
Succeeded
```

**PASS** — matches expected.

---

### ✅ Check R-2: KV secret `Redis-ConnectionString` enabled

```bash
$ az keyvault secret show --vault-name spaarke-spekvcert --name Redis-ConnectionString --query "attributes.enabled" -o tsv
true
```

**PASS** — matches expected.

---

### ✅ Check R-3: App Settings contains KV reference

```bash
$ az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev --query "[?name=='ConnectionStrings__Redis'].value" -o tsv
@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=Redis-ConnectionString)
```

**PASS** — exact match with handoff §4 canonical form.

---

### ✅ Check R-4: BFF healthz returns 200

```bash
$ curl -sS -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz
200
```

**PASS**.

---

### ✅ Check R-5: BFF startup log shows Redis-enabled

```
2026-06-26T12:24:57.2962340+00:00 ContainerStream:       Distributed cache: Redis enabled with instance name 'spaarke:'
```

(Also re-confirmed on subsequent restarts at 12:34:52, 12:48:15, 13:06:09, 13:19:41, 13:52:30 on 2026-06-26.)

**PASS** — exact match with Redis project handoff §1 success-criterion log line.

---

## Section B — AI Search Prerequisites (FR-21)

### 🔴 Check AI-1: KV admin-key freshness

```bash
$ az search admin-key show --service-name spaarke-search-dev -g spe-infrastructure-westus2 --query primaryKey -o tsv
[REDACTED-AZURE-SEARCH-KEY-LIVE]   ← LIVE key (canonical value stored in KV: AiSearch--AdminKey)

$ az keyvault secret show --vault-name spaarke-spekvcert --name AiSearch--AdminKey --query value -o tsv
ERROR: (SecretNotFound) A secret with (name/id) AiSearch--AdminKey/... was not found

$ az keyvault secret show --vault-name spaarke-spekvcert --name ai-search-key --query value -o tsv
[REDACTED-AZURE-SEARCH-KEY-STALE]   ← STALE (pre-recreate; invalid since 2026-06-25 service recreate)

$ az keyvault secret show --vault-name spaarke-spekvcert --name AzureAISearchApiKey --query value -o tsv
ERROR: (SecretNotFound) A secret with (name/id) AzureAISearchApiKey/... was not found
```

**FAIL — but more nuanced than spec assumed**. Three independent issues:

1. **Secret naming drift between spec and reality.** Spec assumes secret name `AiSearch--AdminKey` (double-hyphen, canonical KV-ref-config form). KV actually has `ai-search-key` (kebab-case) which has been the operational name. Neither matches `AzureAISearchApiKey` referenced by `AiSearch__ReferencesApiKey` in BFF App Settings (broken KV reference — see context dump below).
2. **`ai-search-key` value is STALE** — holds pre-recreate primary key. After 2026-06-25 search service recreate, the LIVE primary key rotated; KV was not updated.
3. **`AzureAISearchApiKey` is referenced from BFF App Settings but does not exist in KV.** `AiSearch__ReferencesApiKey = @Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AzureAISearchApiKey)` resolves to a missing secret — that App Setting is currently broken.

**Context dump** — all BFF AI-Search-related settings (FR-15 scope):
```
AiSearch__ApiKeySecretName               AzureAISearchApiKey
AiSearch__DiscoveryIndexName             discovery-index                      ← retired per FR-14
AiSearch__Endpoint                       https://spaarke-search-dev.search.windows.net/    ← hardcoded (FR-15 target)
AiSearch__KnowledgeIndexName             spaarke-file-index                   ← rename per FR-10
AiSearch__SemanticConfigName             semantic-config
AiSearch__AllowedIndexes__0              spaarke-file-index                   ← rename per FR-10
AiSearch__AllowedIndexes__1              spaarke-knowledge-index-v2           ← retired per FR-13
AiSearch__ReferencesEndpoint             https://spaarke-search-dev.search.windows.net/    ← hardcoded (FR-15 target)
AiSearch__ReferencesApiKey               @Microsoft.KeyVault(...;SecretName=AzureAISearchApiKey)   ← broken (missing secret)
DocumentIntelligence__AiSearchEndpoint   https://spaarke-search-dev.search.windows.net/    ← hardcoded (FR-15 target)
DocumentIntelligence__AiSearchKey        [REDACTED-AZURE-SEARCH-KEY-STALE]   ← STALE raw key (FR-15 target)
RecordSync__AiSearchEndpoint             https://spaarke-search-dev.search.windows.net/    ← hardcoded (FR-15 target)
RecordSync__AiSearchApiKey               [REDACTED-AZURE-SEARCH-KEY-STALE]   ← STALE raw key (FR-15 target)
```

**Existing KV secrets matching `*earch*`**:
```
Name                Enabled
------------------  -------
ai-search-endpoint  True
ai-search-key       True       ← STALE; matches the raw key in the App Settings above
```

**Remediation candidates** (decision needed — these affect FR-15 KV-ref migration plan):

- **Option A (minimal change)** — update existing `ai-search-key` with the LIVE primary key value. Also set `AzureAISearchApiKey` (with same LIVE value) to unbreak the existing `AiSearch__ReferencesApiKey` KV reference. Spec FR-15 then standardizes on **either** `ai-search-key` **or** `AzureAISearchApiKey` going forward.
- **Option B (spec-aligned)** — create `AiSearch--AdminKey` (per spec FR-21 #1) with LIVE primary key. Plus set `AzureAISearchApiKey` to unbreak the existing reference. Plus migrate Phase-4 FR-15 to standardize all AI-Search secrets on the `AiSearch--*` naming convention (and retire `ai-search-key`/`AzureAISearchApiKey` post-migration).
- **Option C (full standardize-now)** — choose ONE canonical name (e.g., `AiSearch--AdminKey` since it matches the KV-ref-config double-hyphen syntax `AiSearch__AdminKey` cleanly), set it, set the same value into the legacy secret names as aliases until FR-15 migration retires them, document in FR-15 task notes.

⏸ **NOT autonomously remediated.** KV writes are security-sensitive (CLAUDE.md §6 Human Escalation). The naming-convention choice affects FR-15 design.

---

### 🔴 Check AI-2: BFF MI has `Search Index Data Contributor` role

```bash
$ az role assignment list --all --assignee 9fd47efb-7962-492b-ac44-e5ccd0268ebb \
    --query "[?contains(scope,'spaarke-search-dev')].{role:roleDefinitionName,scope:scope}" -o table
(no output — empty)
```

**FAIL** — BFF MI (`mi-bff-api-dev`) has **zero** role assignments on `spaarke-search-dev`. Expected per FR-21 #2 (role lost during 2026-06-25 recreate).

**Remediation note about the Bicep**: spec FR-21 #2 says "re-run `infrastructure/byok/main.bicep`". Inspected lines 443–454:

```bicep
// Grant App Service managed identity "Search Index Data Contributor" role on AI Search
var searchIndexDataContributorRoleId = '8ebe5a00-799e-43f5-93ac-243d3dce84a7'

resource appServiceSearchRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiSearch.id, appService.id, searchIndexDataContributorRoleId)
  scope: aiSearch
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataContributorRoleId)
    principalId: appService.identity.principalId   ← targets SYSTEM-ASSIGNED identity
    principalType: 'ServicePrincipal'
  }
}
```

The Bicep grants the role to `appService.identity.principalId` (system-assigned MI). **But the BFF App Service uses only the user-assigned `mi-bff-api-dev`** (system identity is null). Re-running this Bicep as-is would either fail or grant the role to a non-existent principal — it would NOT fix this check.

**Remediation candidates** (decision needed):

- **Option A (direct grant, minimal)** — single `az role assignment create` granting `Search Index Data Contributor` on `spaarke-search-dev` to principal `9fd47efb-7962-492b-ac44-e5ccd0268ebb`. Smallest scoped change; unblocks immediately. Drift from Bicep grows by 1 assignment.
- **Option B (fix Bicep + re-run)** — update `infrastructure/byok/main.bicep` line 451 from `appService.identity.principalId` → reference to `mi-bff-api-dev` principalId. Then re-deploy. Keeps Bicep authoritative but is a bigger change (affects all 3 role assignments in the same file). Belongs as a Phase-4 cleanup, not Phase-1 remediation.
- **Option C (defer until FR-15 lands)** — Phase 3 schema deploy uses ADMIN KEY auth (Deploy-AllIndexes.ps1 uses `api-key` header), not MI auth. AI-2 is only a hard blocker for FR-15 (BFF runtime KV-ref migration in Phase 4). Could mark AI-2 as 🟡 "deferred to Phase 4 prep" rather than 🔴 blocking Phase 3.

⏸ **NOT autonomously remediated.** Both options require a planning decision.

---

### ✅ Check AI-3: Service Bus queues `sdap-jobs` + `sdap-communication` exist

```bash
$ az servicebus queue list -g SharePointEmbedded --namespace-name spaarke-servicebus-dev \
    --query "[?name=='sdap-jobs' || name=='sdap-communication'].{name:name,status:status}" -o table
Name                Status
------------------  --------
sdap-communication  Active
sdap-jobs           Active
```

**PASS** — both queues exist, both Active. (Namespace is `spaarke-servicebus-dev` in RG `SharePointEmbedded`, eastus — not the placeholder `<rg>/<sbus>` in spec; resolved at runtime.)

---

### ✅ Check AI-4: Azure OpenAI `text-embedding-3-large` deployment

```bash
$ az cognitiveservices account deployment list --name spaarke-openai-dev -g spe-infrastructure-westus2 \
    --query "[?contains(name,'embed')].{name:name,model:properties.model.name,sku:sku.name,capacity:sku.capacity}" -o table
Name                    Model                   Sku       Capacity
----------------------  ----------------------  --------  ----------
text-embedding-3-small  text-embedding-3-small  Standard  120
text-embedding-3-large  text-embedding-3-large  Standard  120
```

**PASS** — `text-embedding-3-large` deployment exists with Standard SKU, capacity 120. FR-20 (BFF appsettings alignment) can proceed. (Note: `text-embedding-3-small` also deployed — this is the model currently referenced in `appsettings.template.json:248` which FR-20 retires from BFF use.)

---

### ✅ Check AI-5: Empirical resource state — `spaarke-search-dev`

```bash
$ az search service show -g spe-infrastructure-westus2 -n spaarke-search-dev --query "{state:provisioningState,sku:sku.name,location:location}" -o table
State      Sku       Location
---------  --------  ----------
succeeded  standard  West US 2

$ curl -sS -H "api-key: <admin-key>" "https://spaarke-search-dev.search.windows.net/indexes?api-version=2024-07-01"
{"@odata.context":"https://spaarke-search-dev.search.windows.net/$metadata#indexes","value":[]}
```

**PASS** — service is `Succeeded`, `standard` tier, `westus2`, **zero indexes** (matches spec narrative "recreated empty 2026-06-25"). No phantom indexes; no retired-identity RBAC dragnet detected.

---

## Summary

| # | Check | Status | Notes |
|---|---|---|---|
| R-1 | Redis provisioned | ✅ | Succeeded |
| R-2 | KV Redis secret enabled | ✅ | true |
| R-3 | App Settings KV ref | ✅ | Canonical form |
| R-4 | BFF healthz 200 | ✅ | OK |
| R-5 | Startup log Redis enabled | ✅ | 2026-06-26T12:24:57Z |
| AI-1 | KV AdminKey freshness | ✅ | Remediated 2026-06-26T14:31Z — 3 secrets set to LIVE key (Option C); follow-up in FR-15 |
| AI-2 | BFF MI Search RBAC | ✅ | Remediated 2026-06-26T14:31Z — role granted via az CLI; Bicep authority fix is Phase 4 backlog |
| AI-3 | Service Bus queues | ✅ | sdap-jobs + sdap-communication Active |
| AI-4 | OpenAI embed-large deployment | ✅ | text-embedding-3-large Standard cap=120 |
| AI-5 | Search service empirical state | ✅ | succeeded / standard / 0 indexes |

**Final Score: 10 / 10 PASS** (8 initial + 2 post-remediation)

---

## Phase 3 Status: ✅ UNBLOCKED (post-remediation 2026-06-26T14:31Z)

### Remediation evidence

**AI-1 (KV admin-key) — Option C executed**:
```
$ az keyvault secret set --vault-name spaarke-spekvcert --name AiSearch--AdminKey --value <LIVE>
  { enabled: true, version: ...174d9faa903a48038e4bcf27617949e3 }
$ az keyvault secret set --vault-name spaarke-spekvcert --name AzureAISearchApiKey --value <LIVE>
  { enabled: true, version: ...de5dc4ede899499d9bed37a30802aaf1 }
$ az keyvault secret set --vault-name spaarke-spekvcert --name ai-search-key --value <LIVE>
  { enabled: true, version: ...5d6813712da7414490bcdf795c31aa24 }
```
All 3 secrets now hold the LIVE primary key (`[REDACTED-AZURE-SEARCH-KEY]`). `AzureAISearchApiKey` reference in BFF `AiSearch__ReferencesApiKey` setting now resolves. FR-15 will standardize on `AiSearch--AdminKey` and retire the 2 aliases post-migration.

**AI-2 (BFF MI role) — Option B (interpreted; see follow-up below)**:
```
$ az role assignment create \
    --assignee 9fd47efb-7962-492b-ac44-e5ccd0268ebb \
    --role "Search Index Data Contributor" \
    --scope /subscriptions/.../resourceGroups/spe-infrastructure-westus2/providers/Microsoft.Search/searchServices/spaarke-search-dev
  { role: Search Index Data Contributor, principalId: 9fd47efb-..., createdOn: 2026-06-26T14:31:30Z }
```
Verified via `az role assignment list --all --assignee 9fd47efb-7962-492b-ac44-e5ccd0268ebb` — role now present.

### Follow-up backlog (Phase 4 — must be added to plan)

- **Bicep authority fix for dev BFF Search role grant**: dev was deployed via `infrastructure/bicep/customer.bicep` (subscription-scope), which has `bffPrincipalId` parameter and assigns Service Bus roles in `modules/membership-topic.bicep` — but does NOT include the AI Search role assignment at all. The role grant was applied via az CLI in this task and is now Bicep-drift. Phase 4 task should: (a) add a new `modules/role-assignment-search.bicep` module mirroring `modules/role-assignment-keyvault.bicep`, (b) wire it into customer.bicep using the existing `bffPrincipalId` parameter, (c) add to `customer.json` parameters file if needed, (d) re-deploy customer.bicep in `-WhatIf` mode to confirm idempotence with the existing live role assignment, (e) document the change in `auth-azure-resources.md`.
- **BYOK Bicep parameterization**: `infrastructure/byok/main.bicep:425, 438, 451` all hardcode `appService.identity.principalId` (system-assigned). The BYOK template should be parameterized to accept either system- or user-assigned MI, so customer environments that choose user-assigned MI (per Spaarke's auth-v2 ADR-028 patterns) deploy correctly. Phase 4 or later cleanup.

### Original blocking question content (now resolved)

✅ All resolved — see Remediation evidence above.

Two FAIL findings require explicit owner decisions before Phase 3 deploy (FR-16) can begin. Both involve write actions to dev Azure infrastructure that have downstream design implications:

### Decision needed #1 (AI-1, KV admin-key remediation + naming convention)
Pick A / B / C above. Recommendation:
- **Option A** if FR-15 will standardize on existing `ai-search-key` naming → smallest delta now.
- **Option B** if FR-15 will standardize on `AiSearch--AdminKey` → minor extra work now but aligns long-term.
- Either way: set `AzureAISearchApiKey` to the LIVE primary key to unbreak `AiSearch__ReferencesApiKey` immediately (no downside; pre-existing setting expects this secret name).

### Decision needed #2 (AI-2, BFF MI role grant)
Pick A / B / C above. Recommendation:
- **Option A** for immediate unblock (one `az role assignment create` command).
- **Option B** as a Phase-4 follow-up (fix Bicep authoritatively for next env-rebuild scenario; out-of-scope for Phase 1).
- **Option C** is acceptable IF the project agrees Phase 3 = schema deploys (admin-key auth) and the MI role is only needed when FR-15 BFF migration runs in Phase 4.

### Non-blocking observations for downstream tasks

- **FR-13 / FR-14 scope confirmed by live App Settings dump** — `AiSearch__DiscoveryIndexName=discovery-index`, `AiSearch__KnowledgeIndexName=spaarke-file-index`, `AiSearch__AllowedIndexes__1=spaarke-knowledge-index-v2` all present as live values. The FR-13/FR-14 refactor will retire all three.
- **FR-15 scope confirmed by live App Settings dump** — 6 hardcoded `https://spaarke-search-dev.search.windows.net/` URLs and 2 raw API keys (`DocumentIntelligence__AiSearchKey`, `RecordSync__AiSearchApiKey`) match FR-15's enumerated list exactly.
- **FR-21 evidence file naming-decision impact** — the AI-1 finding above (3 different secret names in use across spec, KV, App Settings) is a Phase-4 FR-15 design input. Whichever naming convention is chosen should be reflected in the spec.md FR-15 acceptance criteria when the work begins.
- **Bicep authority drift** — `infrastructure/byok/main.bicep` is currently NON-authoritative for the BFF MI role assignment in dev. Either (a) the BYOK Bicep is intended for a different deployment pattern (BYOK customer envs), or (b) it should be updated to reference `mi-bff-api-dev` directly. Belongs in design.md as a known-state note for Phase-4 planning.

---

*Task 001 evidence complete. Awaiting owner decisions on AI-1 + AI-2 remediation before Phase 3 deploy unblocks. Phase 1 doc work (tasks 002–007) is NOT blocked by these findings — can proceed in parallel.*
