# Task 053 — BLOCKED at step 1: Entra auth is disabled on the Search service

> 2026-08-22. FR-E4. Outcome: **escalation trigger FIRED. No code changed, no keys cleared.**
> Delivered instead: a corrected RBAC finding, a corrected site census (7, not 2), and a
> one-command unblock.

---

## 1. The trigger fired on the first check

> *"Search RBAC cannot be granted, or **Entra auth is disabled on the Search service** — STOP;
> do not clear the keys."*

```
az search service show -g spe-infrastructure-westus2 -n spaarke-search-dev
  → authOptions: { "apiKeyOnly": {} }
     disableLocalAuth: false
     sku: standard
```

`apiKeyOnly` means the data plane accepts **only** admin/query keys. Confirmed empirically rather
than inferred from the config field:

```
GET https://spaarke-search-dev.search.windows.net/indexes?api-version=2024-07-01
  Authorization: Bearer <Entra token for https://search.azure.com>
  → HTTP 403
```

There is exactly **one** Search service in the dev subscription, so there is no alternative
Entra-enabled service to point at.

## 2. 🔴 Correction: "the RBAC half is DONE" was WRONG

Task 052's RBAC sweep found the UAMI holds **`Search Index Data Contributor`** on
`spaarke-search-dev`, and I recorded in the previous session's handoff:

> *"053 AI Search — UAMI already holds `Search Index Data Contributor` on `spaarke-search-dev`.
> The RBAC half is DONE — the task is code + config only."*

**That conclusion does not hold.** The role assignment exists (verified again today) but it is
**inert**: while `authOptions` is `apiKeyOnly`, the data plane rejects every Entra token before
any role evaluation happens. A role assignment on an `apiKeyOnly` search service grants nothing.

This is worth naming plainly because it is the project's own signature failure mode, committed by
me, one session ago: **a checked fact ("the role exists") was carried forward as a different,
unchecked claim ("therefore Entra auth will work")**. The same shape as
`constraints/auth.md:108` — a true-ish statement promoted into a conclusion nobody re-tested. The
handoff line has been corrected in `current-task.md`.

## 3. 🔴 Correction: the POML says two key sites. There are seven.

The task targets *"both key-based sites — `AiSearch:ReferencesApiKey` in `InternalIndexProvider`,
and `AiSearchOptions.ApiKeySecretName`."* A `grep` for `AzureKeyCredential` + `SearchClient` +
`SearchIndexClient` across `src/` returns:

| # | Site | Config source | KV secret | In POML? |
|---|---|---|---|---|
| 1 | `AnalysisServicesModule.cs:1613-1615` — **the `SearchIndexClient` singleton** | `DocumentIntelligence:AiSearchKey` | `AiSearch--AdminKey` | ❌ **the central one** |
| 2 | `InternalIndexProvider.cs:80-88` (citations) | `AiSearch:ReferencesApiKey` | `AiSearch--AdminKey` | ✅ |
| 3 | `DataverseIndexSyncService.cs:89-91` | `DocumentIntelligence:AiSearchKey` | `AiSearch--AdminKey` | ❌ |
| 4 | `RecordMatchService.cs:45-46` | `DocumentIntelligence:AiSearchKey` | `AiSearch--AdminKey` | ❌ |
| 5 | `RecordSyncJob.cs:699-700` | `RecordSync:AiSearchApiKey` | `AiSearch--AdminKey` | ❌ |
| 6 | `KnowledgeDeploymentService.cs:322-325, 455-458` | per-tenant `ApiKeySecretName` | **customer's own vault** | ❌ — **carve-out, see §5** |
| 7 | `AiSearchOptions.cs:6` — `ApiKeySecretName` | `AiSearch:ApiKeySecretName` | — | ✅ but **DEAD, see §6** |

So the POML's census is wrong in **both** directions: it names a dead property as a migration
target, and it misses the single registration every RAG consumer depends on.

**Site 1 is the important one.** It is the only `SearchIndexClient` registration in the codebase.
`RagIndexingPipeline`, `RagService`, `IFileIndexingService`, `IKnowledgeDeploymentService`,
`IEmbeddingCache`, `IVisualizationService`, the Insights ingest path and the Finance/invoices path
all resolve through it. Migrating "both sites" as written would have left the entire RAG stack on
the key while declaring FR-E4 done.

### One admin key, fanned across four settings

Six of the seven sites resolve to the **same** Key Vault secret, `AiSearch--AdminKey` in
`spaarke-spekvcert`, reached through four different app settings:

```
AiSearch__ApiKeySecretName        = AiSearch--AdminKey          (name; and see §6 — unread)
AiSearch__ReferencesApiKey        = @Microsoft.KeyVault(...AiSearch--AdminKey)
DocumentIntelligence__AiSearchKey = @Microsoft.KeyVault(...AiSearch--AdminKey)
RecordSync__AiSearchApiKey        = @Microsoft.KeyVault(...AiSearch--AdminKey)
```

plus `ai-search-endpoint` fanned across three more. **Structurally identical to
`BFF-API-ClientSecret`** — one credential, many config keys, which is precisely why partial
inventories keep missing sites. And it is an **admin** key: every one of these consumers currently
holds full create/update/delete rights on all eight indexes, where most of them only read.

## 4. ⚠️ The migration hazard that would bite whoever does this next

`AnalysisServicesModule.AddRagServices` gates the whole stack on the **presence of the key**:

```csharp
var docIntelOptions = configuration.GetSection(...).Get<DocumentIntelligenceOptions>();
if (!string.IsNullOrEmpty(docIntelOptions?.AiSearchEndpoint) &&
    !string.IsNullOrEmpty(docIntelOptions?.AiSearchKey))
{
    services.AddSingleton(sp => new SearchIndexClient(..., new AzureKeyCredential(...)));
    services.AddSingleton<IKnowledgeDeploymentService, KnowledgeDeploymentService>();
    services.AddSingleton<IEmbeddingCache, EmbeddingCache>();
    services.AddSingleton<IRagService, RagService>();
    services.AddScoped<IFileIndexingService, FileIndexingService>();
    services.AddSingleton<IVisualizationService, VisualizationService>();
}
```

**Step 5 of this task ("clear the keys from config") would silently un-register six services** —
not degrade retrieval, *delete the capability* — at startup. That is the
**asymmetric-registration Tier 1.5 anti-pattern**, CLAUDE.md §10 § F.1 / ADR-032, with the
credential's presence acting as the feature flag. It is the same shape as this project's own
**FR-A1** finding, where secret *presence* selected the Dataverse auth path.

Acceptance criterion 3 warns that *"a silent empty retrieval would look like a content problem."*
The real failure is worse: the services would not exist at all. **Any migration of site 1 must
apply the ADR-032 Null-Object treatment to this block, not just swap the credential.**

## 5. Carve-out: `KnowledgeDeploymentService` CustomerOwned (site 6)

These two `SearchClient` constructions target a **customer's own Azure AI Search service** in the
customer's tenant, using an API key from a per-tenant `ApiKeySecretName`. Our managed identity has
no principal in the customer's tenant and no role on their resource, so Entra is not available
here by construction.

This is the same class as ADR-028 **E-1** (`SpeAdminTokenProvider` / `SpeAdminGraphService` —
other applications' identities). Recommend recording it as an explicit E-1-style exclusion rather
than leaving it to be rediscovered as a "missed site" by the next audit. **Not a migration
target.**

## 6. Site 7 is DEAD — `AiSearchOptions.ApiKeySecretName` has zero readers

`grep -rn "ApiKeySecretName" src/` returns the declaration at `AiSearchOptions.cs:6` and **nothing
else** for this type. The app setting `AiSearch__ApiKeySecretName = AiSearch--AdminKey` is bound
and read by nothing.

**Third instance of this pattern in Group F** — after task 055's `Analysis:PromptFlowKey` and task
050's `AiSafety:ContentSafety:ApiKey`. A bound options property that names a credential and has no
consumer is no longer an anomaly in this codebase; it is a recurring defect class with no forcing
function. Task 061's census catches the confidential-client version. **Recommend 090 consider the
configuration version.**

**Not deleted in this task.** It is one of the seven sites the migration will remove together, and
its removal also touches two test initializers
(`tests/integration/seam/Ai/SemanticScopeProviderSeamTests.cs:91`,
`tests/integration/tenant/Ai/ReferenceRetrievalTenantPinTests.cs:113`, both of which set a value
nothing reads). Landing it alone would leave the surface half-migrated and make the eventual
migration diff harder to review, for no functional gain while the task is blocked at step 1.

## 7. 🔔 ESCALATION — the one-command unblock

**Situation** — FR-E4 cannot proceed. Not a code problem: the Search service does not accept Entra
tokens. Every downstream step (migrate, verify retrieval, clear keys) depends on this.

**The fix is additive and reversible.** `aadOrApiKey` enables Entra **alongside** keys — nothing
that works today stops working, which is exactly the transitional state a staged migration needs:

```bash
az search service update \
  -g spe-infrastructure-westus2 -n spaarke-search-dev \
  --auth-options aadOrApiKey \
  --aad-auth-failure-mode http403
```

Reverse with `--auth-options apiKeyOnly`. The key-off step is a *separate*, later flag
(`--disable-local-auth true`) that belongs after all seven sites are migrated and verified — **not
in the same change.**

**Why I did not run it.** The task's escalation trigger names this exact condition and prescribes
STOP, and CLAUDE.md §8.5 treats firing a trigger as a legitimate stop rather than something to
improvise past. It is also a change to the authentication posture of a shared service that this
project does not own. It is a ~10-second owner action, and I would rather ask than assume — the
general provisioning directive ("create what dev needs rather than deferring") is real, but the
specific trigger outranks it.

**Recommended sequence once enabled**, in this order:

1. `--auth-options aadOrApiKey` (owner)
2. Grant the UAMI **`Search Index Data Reader`** where only reads occur (sites 2, 4) and keep
   `Contributor` only where indexing happens (sites 1, 3, 5) — the current blanket admin key gives
   everything write access; the migration is the natural moment to drop that
3. Migrate site 1 **with the ADR-032 Null-Object treatment from §4**, not a bare credential swap
4. Migrate sites 2–5 onto the DI `TokenCredential`
5. Delete dead site 7 + its two test initializers
6. Record site 6 as an E-1-style exclusion
7. Verify retrieval against a known query set (criterion 2)
8. **Only then** clear `AiSearch--AdminKey` from the four app settings and Key Vault

## 8. Impact on the rest of Group F

- **Task 054 (Document Intelligence)** is **partially blocked by the same thing**.
  `DocumentIntelligence:AiSearchKey` is an *AI Search* credential living in the DocIntel options
  object, and it is sites 1/3/4 above. The Document Intelligence resource itself
  (`spaarke-docintel-dev`) is independent and can proceed; its AI-Search-flavoured settings cannot.
  The two tasks overlap on `DocumentIntelligenceOptions` — sequence them, do not run them in
  parallel.
- **Task 033** — add `AiSearch--AdminKey` and the four app settings to the purge list, *after* this
  migration lands. Also `AiSearch__ApiKeySecretName` (dead today regardless).

## 9. Verification

| Criterion | Status | Evidence |
|---|---|---|
| Both AI Search paths authenticate via MI with no key configured | ⛔ **BLOCKED** | Service is `apiKeyOnly`; Entra returns HTTP 403. §1 |
| RAG retrieval returns identical results for a known query set | ⛔ **NOT ATTEMPTED** | Would require the migration, which is blocked |
| Negative case: RBAC absent → actionable failure, not silent empty results | ⛔ **NOT ATTEMPTED** | §4 shows the real negative case is worse than the criterion assumes — services would not register at all |
| Keys removed from config and Key Vault | ⛔ **CORRECTLY NOT DONE** | The trigger's explicit instruction |
| RBAC verified first (constraint) | ✅ **DONE — and the prior finding corrected** | §2 |
| Nothing changed in a live environment | ✅ | All Azure calls read-only; one Entra probe that was rejected 403 |
| Repository state | ✅ | **No code changed.** Build/suite untouched from task 050's green baseline |
