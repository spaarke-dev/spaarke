# T226 — shared-service secrets: verified read path + per-secret disposition

> **Task**: T226 (plan §7, gap G1). **Date**: 2026-09-30 SESSION 26. **Branch state**: post-master-merge `40c4b2a02`.
> Step 1 = read-only verification (this file §1–§3). Step 2 = proposed disposition (§4) — **owner approval required
> before any change** (POML constraint, root CLAUDE.md §6).

## 1. Where the six secrets are written and read today

| # | Fact | Evidence |
|---|---|---|
| a | **H4-shared writes to a SHARED vault named by run parameter `sharedKeyVaultName`**, extracting keys from services in run parameter `sourceResourceGroupName` (the `sprksharedprod-*` services). | `H4SharedKvSecretsPopulationHandler.cs:83-87, 235-248` |
| a' | **Nothing in the pipeline sets `sharedKeyVaultName` / `sourceResourceGroupName`** — only the retired `model1-shared.bicep` / `model1-customer.bicep` declare them. H4-shared fails `Resumable` when either is missing. | `git grep` over `src/`, `scripts/`, `.claude/skills/provision-environment/`, `provisioning-runs/`, `infrastructure/` |
| a'' | ⇒ **On every dedicated-customer run H4-shared fails**, and because `DagAdvancer.HandlerDependencies[H4b] = {H4, H4Shared}` and `[H9] = {H3, H4b}`, **H4b (app settings) and H9 (BFF deploy) never run.** Independent of the credential question, this is a Task-186 blocker. | `DagAdvancer.cs:160,163,168` |
| b | **H4b points every KV reference at ONE vault** — run parameter `keyVaultName` (the customer vault) — via the generated `Configure-AppServiceSettings.generated.ps1 -VaultName`. | `H4bBulkAppSettingsHandler.cs:62,188,312`; generated script `:74-78` |
| c | **Per-customer H4 skips all `from-shared-service` entries**; the resolver hard-fails if one reaches it. | `H4KvSecretsPopulationHandler.cs:464-471`; `KvSecretValueResolver.cs:193-199` |
| c' | **`customer.bicep` already writes 5 of the 6 into the customer vault from the customer's OWN resources**: Redis, Storage, DocIntel key always; AI Search admin key + Service Bus connection string only when `requireSecretFreeIdentity=false` (omitted, never sentinel, when true). It does **not** write `AzureOpenAI-ApiKey` (deliberate — BFF uses MI). | `customer.bicep:721-752` (post-merge line numbers may differ by a few) |
| d | **Customer UAMI RBAC**: Service Bus Data Sender + Receiver; AI Search Index Data Contributor + Service Contributor (`bff-runtime-rbac.bicep`); Cognitive Services User on OpenAI and DocIntel (`openai.bicep:167-168`, `doc-intelligence.bicep:51-52`). OpenAI (`kind: OpenAI`) and DocIntel (`kind: FormRecognizer`) both have `customSubDomainName` set. | modules cited |

**Live check**: no customer-shaped BFF created by a provisioning run exists (live App Services: `spaarke-bff-dev`,
`spaarke-bff-demo` (stopped), `sprksharedprod-api` (retired shared tier, stopped 2026-09-30)). POML escalation trigger 1
does **not** fire.

## 2. How the BFF authenticates to each service (code)

| Service | BFF selection logic | MI possible without a BFF code change? |
|---|---|---|
| Redis | Parses a connection string (`CacheModule.cs:59-97`); **no Entra-auth path** | ❌ access key required |
| Service Bus | MI when `ServiceBus:FullyQualifiedNamespace` is set, else connection string (`ServiceBusClientFactory.cs:83-96`). H4b sets the FQNS. | ✅ already MI |
| AI Search | MI when `AiSearch:ManagedIdentity:Enabled=true` (H4b sets it) or key absent; **all** consumers go through `SearchClientFactory` (`SearchClientFactory.cs:60-84`; `DataverseIndexSyncService.cs:82-92`; `AnalysisServicesModule.cs:1663-1670`; `RecordMatchService.cs:48`) | ✅ already MI |
| Azure OpenAI | Key if `AzureOpenAI:ApiKey` / `DocumentIntelligence:OpenAiKey` non-empty, else MI (`AiModule.cs:118-138`; `OpenAiClient.cs:84-113`) | ✅ by config — see §3 hazard |
| Document Intelligence | Key if `DocIntelKey` non-empty, else MI (needs custom subdomain + Cognitive Services User — both present) (`TextExtractorService.cs:72-97`) | ✅ by config |
| Storage | **No runtime consumer** of `ConnectionStrings:Storage` anywhere in `src/` (the session blob store uses endpoint + MI) | n/a — secret is unused |

## 3. 🔴 Hazard found: an unresolvable KV reference is sent as a credential

App Service uses the **literal** `@Microsoft.KeyVault(...)` string when a reference cannot resolve. H4b always sets
`AzureOpenAI__ApiKey` and `DocumentIntelligence__OpenAiKey` to a reference to `AzureOpenAI-ApiKey`, which **no writer
puts in the customer vault** (customer.bicep omits it; H4 skips `from-shared-service`; H4-shared writes the SHARED
vault). The BFF sees a non-empty "key", takes the key path, and every OpenAI call fails 401. `customer.bicep:561-566`
warns about exactly this; H4b re-introduces it. (The same literal-string effect on `ServiceBus__ConnectionString` /
`AiSearch__ApiKeySecretName` on secret-free stamps is harmless, because the FQNS / `ManagedIdentity:Enabled` settings
select MI first.)

## 4. Proposed disposition (Step 2 — for owner approval)

| Secret | Proposed | Manifest `value_source` | Writer | Change vs today |
|---|---|---|---|---|
| `Redis-ConnectionString` | **Key from the customer's own Redis** (BFF has no MI path) | `from-bicep-output` | `customer.bicep` kvSecrets (already) | manifest label only |
| `ServiceBus-ConnectionString` | **MI** (already selected via FQNS); secret written from the customer's own namespace only on non-secret-free stamps | `from-bicep-output` (stays a secret-free omit target) | `customer.bicep` (already) | manifest label only |
| `AiSearch--AdminKey` | **MI** (already selected); secret written from the customer's own service only on non-secret-free stamps | `from-bicep-output` (stays a secret-free omit target) | `customer.bicep` (already) | manifest label only |
| `DocumentIntelligence-ApiKey` | **Key from the customer's own DocIntel** (works today; MI is a later config toggle) | `from-bicep-output` | `customer.bicep` (already) | manifest label only |
| `Storage-ConnectionString` | **Keep as-is, relabelled** — unused at runtime; retiring it touches Model 2 Bicep + rotation scripts, so it becomes a follow-up (T235/T241 list) | `from-bicep-output` | `customer.bicep` (already) | manifest label only |
| `AzureOpenAI-ApiKey` | **DECISION** — (A, recommended) **MI**: remove the entry, so H4b stops emitting `AzureOpenAI__ApiKey` / `DocumentIntelligence__OpenAiKey`; E-2 fallback = operator adds a key + setting by hand if MI 401s on a customer account. (B) **Key from the customer's own OpenAI**: `customer.bicep` writes `listKeys()` into the vault (`from-bicep-output`); BFF uses the key (ADR-028 E-2 scope). | A: removed · B: `from-bicep-output` | A: none · B: `customer.bicep` | A: manifest + H4b settings · B: + customer.bicep |
| `PromptFlow-Endpoint`, `PromptFlow-Key` | **Remove** (D5) — also drops H4b's `Analysis__PromptFlow*` settings | removed | — | manifest |
| `BingSearch-ApiKey`, `LlamaParse-ApiKey` | Unchanged; purpose text labels them **Spaarke-shared vendor keys** (D5) | `from-run-parameter` | H4 | text only |

**Why A for OpenAI**: it is what `customer.bicep` already chose; ADR-028's canonical path is MI; E-2's 401 evidence is
from the dev account of `kind: AIServices`, whereas the customer module deploys `kind: OpenAI` with a custom subdomain
(E-2 is unproven there, not disproven). Fallback is config-only. **Risk**: if E-2 does reproduce, AI features on the
first customer stamp fail until the operator applies the fallback — H13 acceptance should exercise one OpenAI call
(check during T230).

**Consequence for code (either option)**: with no `from-shared-service` entries left, **H4-shared, its DAG edge,
its handler id / DI / dispatch registration, the shared accessor, source-key extractor and `SharedKvSecretSource`
types, and the `FromSharedService` value-source are retired** (H4b depends on H4 only). No new extraction code is
needed — the POML anticipated extending H4 with extraction; that is unnecessary because `customer.bicep` already
writes the values.

**Not changed by T226 (observations)**: `customer.bicep` `requireSecretFreeIdentity` defaults to `false`, so today
new stamps DO get the AI Search admin key + Service Bus connection string; whether new Model 1 stamps are run
secret-free is an H2a parameter question (note for T225b).

## 5. Owner decision (2026-09-30)

- **`AzureOpenAI-ApiKey` → A: managed identity** (approved). Remove the manifest entry so H4b stops emitting
  `AzureOpenAI__ApiKey` / `DocumentIntelligence__OpenAiKey`; E-2 fallback is an operator config action; H13 should
  exercise one OpenAI call (T230).
- **Other five + Prompt Flow / vendor labels → "Discuss first"**, then (owner, same day):
  - **Service Bus → remove from the process** (key not required; BFF uses MI via FQNS).
  - **AI Search → remove from the process** (key not required; all BFF consumers use `SearchClientFactory` MI; H2b
    uses the L2 identity via `DefaultAzureCredential`, not the admin key).
  - **Document Intelligence → MI** (remove the key).
  - "Remove from the process" = delete the manifest entry (H4b stops emitting the settings) **and** remove the key from
    `customer.bicep`'s `kvSecretValues` **and** its app-setting KV refs (`ConnectionStrings__ServiceBus`,
    `AI_SEARCH_API_KEY`, `DOC_INTELLIGENCE_KEY`) — a reference to a secret no longer written would reach the BFF as a
    literal string. `customer.bicep` is also the Model 2 template; MI behaves identically there (D3: not broken).
  - **Redis**: owner asked how it works today → answered (access key in a connection string, fetched from KV by the App
    Service's MI; Redis itself authenticates the key; no Entra path in BFF code or `redis.bicep`). Decision pending.
  - **Storage**: owner asked what it is → answered (connection string for `sprk{customerId}prodsa`; no reader). Decision pending.
