# Task 053 — AI Search → Entra: 6 sites migrated, service unblocked, live cutover pending deploy

> 2026-08-22. FR-E4. Outcome: **code migration COMPLETE and green; the Search service is now
> `aadOrApiKey`; keys NOT yet cleared.** Status 🔄, not ✅ — the live cutover needs a deploy.

---

## 1. First finding: the service refused Entra entirely

```
az search service show -g spe-infrastructure-westus2 -n spaarke-search-dev
  → authOptions: { "apiKeyOnly": {} }
```

Confirmed empirically rather than read off the config field — `GET /indexes` with an Entra bearer
token returned **HTTP 403**. This fired the task's escalation trigger (*"Entra auth is disabled on
the Search service — STOP; do not clear the keys"*), so the migration stopped and the finding went
to the owner.

**The owner authorised the change and it was applied 2026-08-22:**

```bash
az search service update -g spe-infrastructure-westus2 -n spaarke-search-dev \
  --auth-options aadOrApiKey --aad-auth-failure-mode http403
```

`aadOrApiKey` is additive — Entra **and** keys both work, which is the transitional state a staged
migration needs. Re-probed after the change: the same request returns **HTTP 200**.

> **Read that 200 carefully.** It was made with *my* user token, and I hold **no Search data role** —
> it succeeded because listing index *definitions* falls under subscription `Owner`'s wildcard
> `actions`. It proves the service now **accepts Entra tokens at all**, where it previously rejected
> every one. It does **not** prove document-level reads work; those need `Search Index Data
> Reader/Contributor`, which are **dataActions**, and `Owner` carries none. The UAMI holds
> `Search Index Data Contributor`, which does cover them.

## 2. 🔴 Correction: "the RBAC half is DONE" was WRONG

Task 052's sweep found the UAMI holds `Search Index Data Contributor` on `spaarke-search-dev`, and I
wrote in the previous session's handoff:

> *"The RBAC half is DONE — the task is code + config only."*

**It did not hold.** The assignment existed but was **inert**: under `apiKeyOnly` the data plane
rejects every Entra token *before* any role is evaluated. A `Search Index Data Reader/Contributor`
grant on an `apiKeyOnly` service grants nothing.

Naming this plainly because it is the project's own signature failure mode, committed by me one
session earlier: **a checked fact ("the role exists") carried forward as a different, unchecked
claim ("therefore Entra auth will work")**. Same shape as `constraints/auth.md:108`, the false
sentence this whole project exists to correct.

## 3. 🔴 Correction: the POML says two key sites. There are seven.

The task targets *"both key-based sites."* A `grep` for `AzureKeyCredential` + `SearchClient` +
`SearchIndexClient` across `src/` found seven, and the POML was wrong in **both** directions — it
named a dead property as a target and missed the central one.

| # | Site | Config source | In POML? | Disposition |
|---|---|---|---|---|
| 1 | `AnalysisServicesModule.cs:1613` — **the `SearchIndexClient` singleton** | `DocumentIntelligence:AiSearchKey` | ❌ | **migrated** |
| 2 | `InternalIndexProvider.cs:80-88` (citations) | `AiSearch:ReferencesApiKey` | ✅ | **migrated** |
| 3 | `DataverseIndexSyncService.cs:89-91` | `DocumentIntelligence:AiSearchKey` | ❌ | **migrated** |
| 4 | `RecordMatchService.cs:45-46` | `DocumentIntelligence:AiSearchKey` | ❌ | **migrated** |
| 5 | `RecordSyncJob.cs:699-700` | `RecordSync:AiSearchApiKey` | ❌ | **migrated** |
| 6 | `KnowledgeDeploymentService.cs:322, 455` | per-tenant, customer's own vault | ❌ | **E-1-style carve-out** (§6) |
| 7 | `AiSearchOptions.cs:6` `ApiKeySecretName` | `AiSearch:ApiKeySecretName` | ✅ | **DELETED — was dead** (§7) |

**Site 1 is the one that mattered.** It is the only `SearchIndexClient` registration in the
codebase; `RagService`, `RagIndexingPipeline`, `IFileIndexingService`, `IKnowledgeDeploymentService`,
`IEmbeddingCache`, `IVisualizationService`, Insights ingest and Finance/invoices all resolve through
it. Migrating "both sites" as written would have left the entire RAG stack on the admin key while
reporting FR-E4 done.

### One admin key, fanned across four settings

Six of seven resolved to the **same** Key Vault secret, `AiSearch--AdminKey` in `spaarke-spekvcert`,
reached via `AiSearch__ApiKeySecretName`, `AiSearch__ReferencesApiKey`,
`DocumentIntelligence__AiSearchKey` and `RecordSync__AiSearchApiKey` — **structurally identical to
`BFF-API-ClientSecret` across five keys**, which is exactly why partial inventories keep missing
sites. It is also an **admin** key: consumers that only read currently hold full create/update/delete
on all eight indexes.

## 4. ⚠️ The defect that would have made "clear the keys" catastrophic

`AnalysisServicesModule.AddRagServices` gated the whole stack on the **presence of the key**:

```csharp
if (!IsNullOrEmpty(docIntelOptions?.AiSearchEndpoint) && !IsNullOrEmpty(docIntelOptions?.AiSearchKey))
{
    services.AddSingleton(... SearchIndexClient ...);
    services.AddSingleton<IKnowledgeDeploymentService, ...>();
    services.AddSingleton<IEmbeddingCache, ...>();
    services.AddSingleton<IRagService, ...>();
    services.AddScoped<IFileIndexingService, ...>();
    services.AddSingleton<IVisualizationService, ...>();
}
```

Step 5 of this task — *"clear the keys from config"* — would not have degraded retrieval. It would
have **silently un-registered six services at startup**, and every consumer would have failed with
an unrelated "service not registered" error. That is the **asymmetric-registration Tier 1.5
anti-pattern** (CLAUDE.md §10 § F.1 / ADR-032), and the same shape as this project's own **FR-A1**
finding where secret *presence* selected the Dataverse auth path.

**Fixed**: the gate is now the **endpoint**, not the key. Registration is symmetric across both
credential modes, so an auth problem surfaces as an auth error at call time instead of a missing
dependency at startup. This is also what makes acceptance criterion 3 achievable at all.

## 5. What was built: one credential decision, not six

New: **`Infrastructure/Auth/SearchClientFactory.cs`** (~85 lines, no new package, no new interface,
no new DI registration — a static factory).

**Selection rule, deliberately identical to `ContentSafetyAuthHandler`** so the platform has one
shape: `AiSearch:ManagedIdentity:Enabled = true` **OR** no key configured → Entra bearer; otherwise
the admin key.

**§11 three-question justification**

1. *Existing* — `ManagedIdentityCredentialFactory` builds the `TokenCredential` but cannot help:
   `SearchClient` has two mutually exclusive ctor overloads (`TokenCredential` vs
   `AzureKeyCredential`), so the choice must be made where the client is built.
2. *Extension* — no natural host. The five sites are a DI module, a citations provider, a
   record-matching service, an index-sync service and a background job; no shared base type.
3. *Cost-of-doing-nothing* — five independent copies of the branch. Per ADR-028 A4, *"seven call
   sites each rolling their own credential handling is what made the previous state unfixable."*

**Why the flag exists rather than "delete the key and see".** It lets an environment move to Entra
**while the key is still configured as a rollback** — the staged transition NFR-06 requires. It
defaults to `false`, so behaviour is byte-identical until an operator opts in. This matters
especially here: flipping every environment to Entra before its Search service is `aadOrApiKey`
would 403 every call.

## 6. Carve-out: `KnowledgeDeploymentService` CustomerOwned (site 6)

These two `SearchClient` constructions target a **customer's own** Azure AI Search service in the
**customer's tenant**, keyed from a per-tenant `ApiKeySecretName`. Our managed identity has no
principal in that tenant and no role on that resource, so Entra is unavailable by construction.

Same class as ADR-028 **E-1** (`SpeAdminTokenProvider` / `SpeAdminGraphService` — other
applications' identities). **Recommend recording it as an explicit E-1-style exclusion** so the next
audit does not rediscover it as a "missed site".

## 7. Site 7 was dead — and the census was wrong twice

`AiSearchOptions.ApiKeySecretName` was bound from `AiSearch:ApiKeySecretName` and read by
**nothing**; the only occurrence in `src/` was its own declaration. Deleted, along with the
template's `"ApiKeySecretName": "AzureAISearchApiKey"` line.

**Third instance of this pattern in one workstream** — after `Analysis:PromptFlowKey` (task 055) and
`AiSafety:ContentSafety:ApiKey` (task 050). A bound options property that *names* a credential and
has no consumer is no longer an anomaly; it is a recurring defect class with no forcing function.
Task 061's census catches the confidential-client version. **Booked to 090 for the configuration
version.**

**My own count was wrong too.** The first draft of this record said the removal touched "two test
initializers". It touched **five** — the initial grep was filtered too narrowly and I stated the
result without widening it. Corrected before commit; the five are
`SemanticScopeProviderSeamTests`, `ReferenceRetrievalTenantPinTests`,
`SessionFileRetrievalBindingGuardTests`, `RagServiceTests`, `PrivilegeAwareRagServiceTests`.

## 8. Tests: one existing test asserted the behaviour that had to change

`RecordMatchServiceTests.Constructor_ThrowsWhenAiSearchKeyMissing` asserted that a missing
`AiSearchKey` **throws** — a contract that made the admin key mandatory for record matching, so
clearing the key would have broken the service at startup. Rewritten rather than deleted, into
three tests that assert the new contract:

- `Constructor_SelectsManagedIdentity_WhenAiSearchKeyMissing` — absent key selects Entra, no throw
- `Constructor_SelectsManagedIdentity_WhenFlagIsSet_EvenWithKeyConfigured` — the flag wins, which is
  what makes key-in-place rollback possible
- `SearchClientFactory_PrefersKey_WhenFlagIsUnsetAndKeyPresent` — default posture unchanged; guards
  against the migration silently flipping environments to Entra before their service is `aadOrApiKey`

## 9. Why this is 🔄 and not ✅

The remaining criteria need the **new code running against the real service**, and the dev App
Service is still running the previously deployed build. Two consequences:

- **Flipping `AiSearch__ManagedIdentity__Enabled` on dev right now would do nothing** — the deployed
  build has no `SearchClientFactory` to read it. The flag flip is only meaningful *after* a deploy.
- This project's standing discipline is **deploy to the slot, verify, then swap** (`#3b` attempt 1
  took dev down with an in-session flip). Deploying to verify one task's retrieval path is exactly
  the action that discipline exists to prevent.

**Booked onto task 031's slot deployment**, where an operator is present and the §6.1 checklist runs
anyway:

1. Deploy (slot), set `AiSearch__ManagedIdentity__Enabled=true` on the slot only
2. Exercise RAG retrieval + citation verification against a known query set (criterion 2)
3. Watch for `403` from `*.search.windows.net` in dependency telemetry — that is the signal that a
   role is missing rather than the service being misconfigured
4. Consider dropping the blanket `Contributor` to `Search Index Data Reader` for the read-only
   consumers (sites 2 and 4) — the migration is the natural moment
5. **Only then** clear `AiSearch--AdminKey` from the four app settings and Key Vault (task 033)

## 10. Verification

| Criterion | Status | Evidence |
|---|---|---|
| Both (all six) AI Search paths authenticate via MI with no key configured | 🔄 **Code complete; not yet live** | Six sites route through `SearchClientFactory`; dev still has keys and the flag defaults false. §9 |
| RAG retrieval returns identical results for a known query set | 🔄 **Deferred to 031** | Requires the new build deployed. §9 |
| Negative case: RBAC absent → actionable failure, not silent empty results | ✅ **Materially improved** | §4 — the registration no longer vanishes when the key goes, so failures surface as auth errors at call time rather than missing dependencies at startup |
| Keys removed from config and Key Vault | ⏭️ **Correctly not done** | Gated on live verification; booked to 033 |
| RBAC verified first (constraint) | ✅ **Done — and the prior claim corrected** | §2 |
| Use the DI-injected `TokenCredential`, not an inline `DefaultAzureCredential` (constraint) | ✅ | All six sites take `Azure.Core.TokenCredential` from DI (`Program.cs:46`). No new `DefaultAzureCredential` anywhere |
| Retrieval behaviour unchanged — auth change only (constraint) | ✅ | Default posture is byte-identical: flag defaults false, key present → key used. §8 test 3 guards it |
| Build + suites | ✅ | build 0 errors · unit **10,598 / 0** (97 skipped) · **auth seams 60/60** · ArchTests **49/49** · both integration test projects compile |
| Publish size (CLAUDE.md §10) | ✅ | **44.99 MB** incl. PDBs / 215 files — **delta 0.00 MB**, ceiling 60 |
| CVE | ✅ | No vulnerable packages; no package added |
| Live environment changes | ⚠️ **One, authorised** | `spaarke-search-dev` `apiKeyOnly` → `aadOrApiKey` (additive, reversible with `--auth-options apiKeyOnly`) |

## 11. Impact on the rest of Group F

- **Task 054 (Document Intelligence)** — the overlap is now resolved in this task's favour:
  `DocumentIntelligence:AiSearchKey` is an *AI Search* credential that happens to live in the
  DocIntel options object, and all three of its consumers (sites 1, 3, 4) are migrated here. 054
  should confine itself to the **Document Intelligence** resource itself
  (`spaarke-docintel-dev`, endpoint `https://westus2.api.cognitive.microsoft.com/` — a **regional**
  endpoint, not a per-resource subdomain, which is worth checking early for MI auth).
- **Task 033** — purge list gains `AiSearch--AdminKey` plus `AiSearch__ReferencesApiKey`,
  `DocumentIntelligence__AiSearchKey`, `RecordSync__AiSearchApiKey`. `AiSearch__ApiKeySecretName` is
  dead as of today and can go immediately.
- **Task 090** — two items: the E-1-style exclusion for site 6, and the dead-credential-config
  defect class (§7).
