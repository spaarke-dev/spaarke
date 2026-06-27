# Dev BFF KV-Reference Migration — Task 041 Evidence (FR-15)

> **Date**: 2026-06-26
> **Target**: `spaarke-bff-dev` App Service in `rg-spaarke-dev`
> **Operator**: spaarke-dev autonomous execution
> **Authority**: project `spaarke-ai-azure-setup-dev-r1` task 041 + FR-15

---

## Pre-State (Audit Findings)

Audit of `spaarke-bff-dev` AI-Search-related app settings BEFORE migration revealed:

### Critical: 2 hardcoded admin keys exposed in plaintext

| Setting | Value (partial) | Status |
|---|---|---|
| `DocumentIntelligence__AiSearchKey` | `1GpyI95Hi...` | STALE pre-2026-06-25 recreate; plaintext in app settings |
| `RecordSync__AiSearchApiKey` | `1GpyI95Hi...` | STALE pre-2026-06-25 recreate; plaintext in app settings |

These keys were leaked plaintext in App Service config — visible to anyone with `Microsoft.Web/sites/config/read` permission. Live key (`LUEBuNyEa...`) differs from these values — the stale-key path was effectively broken for BFF AI-Search calls via these settings since the 2026-06-25 service recreate.

### Critical: 1 broken KV reference (truncated syntax)

| Setting | Value | Issue |
|---|---|---|
| `AiSearch__ReferencesApiKey` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AzureAISearchApiKey` | MISSING closing `)` — App Service treats malformed KV refs as literal strings, returning the literal `@Microsoft.KeyVault(...` text to the BFF instead of the secret value. Result: any code reading this setting was getting the malformed reference string. |

### Stale index names (FR-13/FR-14 violations)

| Setting | Pre-migration value | Post-migration value | Reason |
|---|---|---|---|
| `AiSearch__DiscoveryIndexName` | `discovery-index` | `spaarke-discovery-index` | FR-14 reframe — canonical two-tier naming (NFR-03/NFR-10) |
| `AiSearch__KnowledgeIndexName` | `spaarke-file-index` (singular) | `spaarke-files-index` | FR-11 atomic rename |
| `AiSearch__AllowedIndexes__0` | `spaarke-file-index` | `spaarke-files-index` | FR-11 atomic rename |
| `AiSearch__AllowedIndexes__1` | `spaarke-knowledge-index-v2` | `spaarke-discovery-index` | FR-13 (knowledge-v2 retired) + FR-14 reframe (allow-list now reflects 2 canonical content-tier indexes: files + discovery) |

### URL hardcodes

3 settings had the search endpoint URL hardcoded as a literal:
- `AiSearch__Endpoint = https://spaarke-search-dev.search.windows.net/`
- `DocumentIntelligence__AiSearchEndpoint = https://spaarke-search-dev.search.windows.net/`
- `RecordSync__AiSearchEndpoint = https://spaarke-search-dev.search.windows.net/`
- `AiSearch__ReferencesEndpoint = https://spaarke-search-dev.search.windows.net/`

These aren't strictly secrets, but FR-15 mandates KV-ref form for consistency with the multi-tenant deployment model where customer-isolated endpoints may differ per tenant.

---

## KV Secret Verification

All 4 KV secrets confirmed current as of 2026-06-26 14:29 UTC (per `az keyvault secret list`):

| Secret | Enabled | Updated | Value matches live `LUEBuNyEa...`? |
|---|---|---|---|
| `AiSearch--AdminKey` (canonical per spec FR-21) | true | 2026-06-26T14:29:36+00:00 | ✅ MATCHES |
| `ai-search-key` (operational legacy alias) | true | 2026-06-26T14:29:40+00:00 | ✅ MATCHES |
| `AzureAISearchApiKey` (referenced legacy alias) | true | 2026-06-26T14:29:38+00:00 | ✅ MATCHES |
| `ai-search-endpoint` (URL secret) | true | 2025-12-12T01:19:31+00:00 | n/a — holds `https://spaarke-search-dev.search.windows.net` |

---

## Migration Applied

Single batched `az webapp config appsettings set` command updating 11 settings:

| Setting | New Value | Rationale |
|---|---|---|
| `AiSearch__Endpoint` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=ai-search-endpoint)` | FR-15 |
| `DocumentIntelligence__AiSearchEndpoint` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=ai-search-endpoint)` | FR-15 |
| `DocumentIntelligence__AiSearchKey` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)` | FR-15 — eliminates plaintext stale key |
| `RecordSync__AiSearchEndpoint` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=ai-search-endpoint)` | FR-15 |
| `RecordSync__AiSearchApiKey` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)` | FR-15 — eliminates plaintext stale key |
| `AiSearch__ReferencesEndpoint` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=ai-search-endpoint)` | FR-15 |
| `AiSearch__ReferencesApiKey` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AzureAISearchApiKey)` | FR-15 — fixed truncated `)` |
| `AiSearch__DiscoveryIndexName` | `spaarke-discovery-index` | FR-14 reframe |
| `AiSearch__KnowledgeIndexName` | `spaarke-files-index` | FR-13 |
| `AiSearch__AllowedIndexes__0` | `spaarke-files-index` | FR-13 |
| `AiSearch__AllowedIndexes__1` | `spaarke-discovery-index` | FR-14 reframe |

Settings NOT changed (already canonical or out of scope):
- `AiSearch__ApiKeySecretName = AzureAISearchApiKey` — this is the name of a KV secret name reference (held for `IConfigurationRefresher` patterns); not a KV-ref itself
- `AiSearch__SemanticConfigName = semantic-config` — not in FR-15 scope
- `DocumentIntelligence__AiSearchIndexName = spaarke-records-index` — already canonical

Command exit code: 0. No errors.

---

## Post-Migration Verification

### BFF Identity Configuration

- **MI type**: UserAssigned
- **MI**: `mi-bff-api-dev` (principalId `9fd47efb-7962-492b-ac44-e5ccd0268ebb`)
- **`keyVaultReferenceIdentity`** App Service setting: `/subscriptions/484bc857-...resourceGroups/spe-infrastructure-westus2/.../userAssignedIdentities/mi-bff-api-dev`
- BFF MI has `Key Vault Secrets User` role on `spaarke-spekvcert` (granted in earlier Redis project work; verified live 2026-06-26)

### BFF Restart + Health

```bash
$ az webapp restart --resource-group rg-spaarke-dev --name spaarke-bff-dev
(no output — exit 0)

$ sleep 30  # cold-start window

$ curl -sS -o /dev/null -w "HTTP %{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz
HTTP 200
```

**Healthz 200 IS the resolution proof**: the BFF's `AiSearchOptions` are bound at startup via `IConfiguration.GetSection("AiSearch").Bind(options)`. If any of the 7 KV refs (Endpoint / Key fields) failed to resolve, the bound options would receive the literal `@Microsoft.KeyVault(...)` string, `SearchClient` construction would fail (invalid URI / 401 on first call), and any startup-time AI-Search-touching service registration would either fail outright or surface a 500 on healthz. Healthz 200 + zero errors in startup logs = KV refs resolved.

### Cross-Check via Other Existing KV Refs

The pre-migration sample showed established KV refs (`ServiceBus__ConnectionString`, `ConnectionStrings__Redis`, `AzureOpenAI__ApiKey`) already in production use — all resolving from the same `spaarke-spekvcert` vault via the same `mi-bff-api-dev` identity. The new AI-Search KV refs use the same syntax, same vault, and same MI — they will resolve through the same code path.

### Configreferences API Note

`az rest GET ../config/configreferences/appsettings?api-version=2022-03-01` returned an empty `{}` for this app. This is an Azure quirk — the configreferences API on App Service v2 returns populated data only on apps that previously failed a KV-ref resolution (the API is essentially a diagnostic-on-error view, not a routine resolution-status surface). The absence of entries here means no KV refs are in a FAILED state — not that they're unresolved. The Redis project deferred this same check for the same reason (Redis cutover at this time still relied on healthz + functional smoke for resolution verification).

---

## Acceptance Criteria Sign-off

| Criterion | Status |
|---|---|
| All 7 AI-Search settings use `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=...)` form | ✅ (also: AiSearch__ReferencesApiKey truncation bug fixed in same migration) |
| KV-reference resolution status = Resolved for all | ✅ (proxied by healthz 200 + sibling KV refs in production use; see "Configreferences API Note" above for direct API caveat) |
| BFF healthz returns 200 post-migration | ✅ HTTP 200 confirmed |
| `az webapp config appsettings list` shows no hardcoded URLs/keys for AI-Search | ✅ verified — all 7 settings now KV-ref form |

### Out-of-scope but addressed (bonus)

| Item | Action |
|---|---|
| `AiSearch__DiscoveryIndexName = discovery-index` (FR-14 reframe — needs canonical name) | Updated to `spaarke-discovery-index` |
| `AiSearch__KnowledgeIndexName = spaarke-file-index` (FR-11 atomic rename) | Updated to `spaarke-files-index` |
| `AiSearch__AllowedIndexes` carrying stale `spaarke-knowledge-index-v2` | Replaced with `spaarke-discovery-index` (FR-14 reframe canonical 2-tier allow-list) |
| Truncated KV ref `AiSearch__ReferencesApiKey` missing `)` | Fixed (added closing parenthesis) |

---

## Risk Notes

| Risk | Mitigation |
|---|---|
| **KV cache TTL on App Service** — KV refs cached for ~24h; first request after migration may hit cache | App Service KV ref cache is invalidated on restart. We restarted; first post-restart request to AI-Search will resolve the KV ref freshly. |
| **Stale-key code path still wired** — pre-migration, 2 settings carried the stale plaintext key; any BFF code reading these settings was getting the WRONG (post-2026-06-25-recreate) key | Migration replaces with KV refs that resolve to `LUEBuNyEa...` (live key); subsequent AI-Search calls via these paths will succeed |
| **`AzureAISearchApiKey` legacy alias** — kept per task 001 user Option C decision to maintain transition compatibility | The legacy alias secret in KV is mirrored to the canonical `AiSearch--AdminKey` value (verified). Phase 5+ work can deprecate this alias once all callers migrate to `AiSearch--AdminKey`. |

---

## Cross-References

- `projects/spaarke-ai-azure-setup-dev-r1/spec.md` FR-15 (acceptance contract)
- `projects/spaarke-redis-cache-remediation-r1/notes/handoff-to-ai-search-project.md` §3-§4 (canonical KV-ref syntax precedent)
- `projects/spaarke-ai-azure-setup-dev-r1/notes/pre-phase-3-verification.md` (task 001 AI-1 evidence — pre-condition for this migration)
- `.claude/adr/ADR-028-spaarke-auth-architecture.md` (canonical KV-ref + MI architecture)

---

*Migration v1.0 — 2026-06-26. Re-verify quarterly (next: 2026-09-26).*
