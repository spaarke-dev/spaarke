# Task 003 - Dev model deployments and Content Understanding defaults

> Executed 2026-10-15 by the task-execute protocol (STANDARD rigor, prescriptive steps), dev only.
> Subscription: `484bc857-3802-427f-9ea5-ca47b43db0f0` (Spaarke Devlopment Environment). Operator: ralph.schroeder@spaarke.com (Owner at subscription scope).
> No key was listed, read, copied or stored. All data-plane calls used an Entra bearer token (`az account get-access-token --resource https://cognitiveservices.azure.com`). No Key Vault secret was touched.

## 1. Discovery (read-only) - step 1

| Command (read) | Result |
|---|---|
| `az account show` | Spaarke Devlopment Environment, tenant a221a95e-6abc-4434-aecc-e48338a1b2f2 |
| `az cognitiveservices account list` | `spaarke-docintel-invoices-dev` AIServices S0 **westus**, rg-spaarke-dev; `spaarke-openai-dev` AIServices S0 **eastus**, rg **spe-infrastructure-westus2**; `spaarke-docintel-dev` FormRecognizer westus2 (not used here) |
| `az webapp config appsettings list -n spaarke-bff-dev -g rg-spaarke-dev` (endpoint keys only; key values redacted, not read) | `DocumentIntelligence__OpenAiEndpoint` = `https://spaarke-openai-dev.openai.azure.com/` (also `Ai__OpenAiEndpoint`, `AzureOpenAI__Endpoint`). **Escalation trigger 3 not fired**: the BFF endpoint is spaarke-openai-dev. |
| `az webapp identity show` | UAMIs attached: `mi-ontology-writer-dev` and `mi-bff-api-dev`. The BFF identity (`ManagedIdentity__ClientId` = `Graph__ManagedIdentity__ClientId` = 5967251e-171c-46fe-a6c2-ef843c90309d) is **mi-bff-api-dev**, principalId `9fd47efb-7962-492b-ac44-e5ccd0268ebb` (rg spe-infrastructure-westus2). |
| `az cognitiveservices account deployment list` (before) | CU resource: gpt-5-608736 (gpt-5 2025-08-07), gpt-5-mini-875777 (gpt-5-mini 2025-08-07), text-embedding-3-large-771514 (text-embedding-3-large v1), all GlobalStandard cap 250. OpenAI: `gpt-4o-mini` (**model is actually gpt-4.1-mini 2025-04-14**, Standard 200), text-embedding-3-small (Standard 120), text-embedding-3-large (Standard 120), gpt-5-reasoning (gpt-5 2025-08-07, GlobalStandard 10). |
| `az cognitiveservices model list -l westus` and `-l eastus` | `gpt-5.2` 2025-12-11, `gpt-5.4-mini` 2026-03-17, `gpt-5.6-luna` 2026-07-09: all GenerallyAvailable, GlobalStandard available, for kind OpenAI and AIServices in both regions. |
| Content Understanding defaults before (GET `/contentunderstanding/defaults?api-version=2025-11-01`, HTTP 200) | `gpt-5`->gpt-5-608736, `gpt-5-mini`->gpt-5-mini-875777, `text-embedding-3-large`->text-embedding-3-large-771514, `prebuilt-analyzer-completion`->gpt-5-608736, `prebuilt-analyzer-completion-mini`->gpt-5-mini-875777, `prebuilt-analyzer-embedding`->text-embedding-3-large-771514 |

## 2. Quota - step 2

`az cognitiveservices usage list -l westus` and `-l eastus`: `OpenAI.GlobalStandard.gpt-5.2` 0/1000, `OpenAI.GlobalStandard.gpt-5.4-mini` 0/2000, `OpenAI.GlobalStandard.gpt-5.6-luna` 0/1000 (units of 1K TPM) in both regions. Quota and region available for all three models; escalation trigger 1 not fired.

## 3. Required CU model set - step 3

Source: https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/concepts/models-deployments (page `updated_at` 2026-09-24; fetched 2026-10-15). "The service requires a `chat completion` model and an `embeddings` model." The PATCH body maps model names and aliases to deployment names under `modelDeployments`; prebuilt aliases are `prebuilt-analyzer-completion`, `prebuilt-analyzer-completion-mini` and `prebuilt-analyzer-embedding`. GPT-5.2 is the recommended model; the embedding model is used for labeled samples and in-context learning.

The CU resource already has an embedding deployment (`text-embedding-3-large-771514`, mapped to `prebuilt-analyzer-embedding`), so **no new embedding deployment was created**; it is reused. The mini alias stays on `gpt-5-mini-875777` (gpt-5.4-mini is deployed on the OpenAI account, not on the CU resource, and the task does not require it on CU).

## 4. Deployments created

| Account | Deployment name | Model / version | SKU | Capacity (K TPM) | Region | State |
|---|---|---|---|---|---|---|
| spaarke-docintel-invoices-dev (rg-spaarke-dev) | `gpt-5.2` | gpt-5.2 / 2025-12-11 | GlobalStandard | 100 | westus | Succeeded |
| spaarke-openai-dev (rg spe-infrastructure-westus2) | `gpt-5.4-mini` | gpt-5.4-mini / 2026-03-17 | GlobalStandard | 100 | eastus | Succeeded |
| spaarke-openai-dev (rg spe-infrastructure-westus2) | `gpt-5.6-luna` | gpt-5.6-luna / 2026-07-09 | GlobalStandard | 100 | eastus | Succeeded |

`gpt-5.6-luna` is for the task 005 coding comparison only. **Remove it in 162 / wrap-up if 005 does not select it.**

Resource ids:
- `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/rg-spaarke-dev/providers/Microsoft.CognitiveServices/accounts/spaarke-docintel-invoices-dev/deployments/gpt-5.2`
- `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.CognitiveServices/accounts/spaarke-openai-dev/deployments/gpt-5.4-mini`
- `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.CognitiveServices/accounts/spaarke-openai-dev/deployments/gpt-5.6-luna`

## 5. Content Understanding defaults - step 4

`PATCH https://spaarke-docintel-invoices-dev.cognitiveservices.azure.com/contentunderstanding/defaults?api-version=2025-11-01` (Content-Type `application/merge-patch+json`), body:

```json
{"modelDeployments":{"gpt-5.2":"gpt-5.2","prebuilt-analyzer-completion":"gpt-5.2"}}
```

HTTP 200. Merge-patch: other keys unchanged. GET back (HTTP 200):

```json
{"modelDeployments":{"gpt-5":"gpt-5-608736","gpt-5-mini":"gpt-5-mini-875777","text-embedding-3-large":"text-embedding-3-large-771514","prebuilt-analyzer-completion":"gpt-5.2","prebuilt-analyzer-completion-mini":"gpt-5-mini-875777","prebuilt-analyzer-embedding":"text-embedding-3-large-771514","gpt-5.2":"gpt-5.2"}}
```

Change: `prebuilt-analyzer-completion` moved from gpt-5-608736 to `gpt-5.2`; `gpt-5.2`->`gpt-5.2` added. The pre-existing gpt-5 / gpt-5-mini deployments and mappings are kept. The resource is eBilling's dedicated one (spec External Dependencies).

## 6. Test calls (no document content; prompt "Reply with the single word OK.", `max_completion_tokens` 200, api-version 2025-04-01-preview, Entra token)

| Deployment | Endpoint | HTTP | Latency | Response |
|---|---|---|---|---|
| gpt-5.2 | spaarke-docintel-invoices-dev.cognitiveservices.azure.com | 200 | 1.82 s | model gpt-5.2-2025-12-11, content "OK", 17 tokens |
| gpt-5.4-mini | spaarke-openai-dev.openai.azure.com | 200 | 1.99 s | choices returned |
| gpt-5.6-luna | spaarke-openai-dev.openai.azure.com | 200 | 1.29 s | choices returned |

## 7. Role assignment - step 6

`az role assignment create --assignee-object-id 9fd47efb-7962-492b-ac44-e5ccd0268ebb --assignee-principal-type ServicePrincipal --role "Cognitive Services Content Understanding Reader" --scope <spaarke-docintel-invoices-dev>`

- Role: **Cognitive Services Content Understanding Reader** (id 379c52cb-64de-498c-8b5b-c6170d6c49d4). The role exists, so the Cognitive Services User fallback was not needed.
- Assignment id: **aa679dff-41a2-4d2f-b1f4-0977056bc241**; principal mi-bff-api-dev (9fd47efb-7962-492b-ac44-e5ccd0268ebb); scope = the spaarke-docintel-invoices-dev account.
- Verified with `az role assignment list --scope <account>` (filtered to the principal): one row, role as above.
- Not verified: an analyze call as the BFF identity itself (cannot impersonate the UAMI from here). Task 004 / 043 prove the data-plane path. If Reader is insufficient for analyze, the fallback is Cognitive Services User, and the change is owner-visible.
- Not verified: the BFF identity's roles on spaarke-openai-dev (the BFF currently reaches OpenAI by key, section 10). Role scope is the account, so any OpenAI data-plane role it holds there covers the two new deployments; task 047 confirms the coding Action path.

## 8. Config template - step 7

`src/server/api/Sprk.Bff.Api/appsettings.template.json`: new `ContentUnderstanding` section (inserted after `Finance`; file re-read immediately before the edit), 7 added lines, nothing else changed (`git diff --stat`: 1 file, 7 insertions):

- `Endpoint`: `#{CONTENT_UNDERSTANDING_ENDPOINT}#`
- `ApiVersion`: `2025-11-01`
- `AnalyzerId`: `sprk_invoice_v1`
- `_comment`: names task 003, states auth is managed identity and no key exists.

JSON check: the template is **not strict JSON in HEAD** (unquoted tokens such as `"RecordMatchingEnabled": #{RECORD_MATCHING_ENABLED}#` at lines 209, 250, 251). With unquoted tokens neutralised, HEAD and the working copy both parse (55 and 56 top-level keys); the new section itself is strict JSON with quoted tokens.

Token `CONTENT_UNDERSTANDING_ENDPOINT` has no producer yet (grep: none in scripts / catalogs). Dev value for the app setting `ContentUnderstanding__Endpoint` is `https://spaarke-docintel-invoices-dev.cognitiveservices.azure.com/` (non-secret). Setting it on `spaarke-bff-dev` is task 043 / deploy work; this task did not change dev App Service settings.

## 9. BFF publish size and CVE (bff-hygiene item 4 / 5)

- This task's only `src/**` change is `appsettings.template.json` (other tasks' PublicContracts edits in the same worktree are theirs to measure), which the csproj excludes from publish (`<Content Update="appsettings.template.json" CopyToPublishDirectory="Never" />`, csproj line 273; confirmed by `dotnet msbuild -getItem:Content` showing `CopyToPublishDirectory: Never`).
- Therefore the publish output is byte-identical to master's for this change. **Delta = 0 MB by construction.** A two-sided fresh-worktree `Compress-Archive` measurement was **not run** (no code, package or content change reaches the publish; running two full publishes beside concurrent agents would add risk and no information). Task 043 (first task that adds the package) performs the real measurement with both sizes, file counts and the zip tool.
- No package reference changed, so no new HIGH CVE is possible from this task.

## 10. Observations and defects (outside this task's scope; not fixed here)

1. The dev BFF still holds OpenAI / Document Intelligence **key** app settings (`Ai__OpenAiKey`, `AzureOpenAI__ApiKey`, `DocumentIntelligence__OpenAiKey`, `DocumentIntelligence__DocIntelKey`). This is existing dev behaviour (ADR-028 E-2 measurement territory), not touched. Values were not read. The new CU path is keyless.
2. `appsettings.template.json` is not strict JSON (unquoted `#{TOKEN}#` values, lines 209/250/251). Acceptance criterion "parses as JSON" is met for the new section and for the file with tokens neutralised; the pre-existing condition is how all environment templates are substituted.
3. Research note caveat 3 recorded the dev Azure OpenAI resource in westus2. Actual: `spaarke-openai-dev` is **eastus** (rg spe-infrastructure-westus2); `spaarke-docintel-dev` (FormRecognizer) is westus2; the CU resource is westus (a CU GA region).
4. The dev `gpt-4o-mini` deployment on spaarke-openai-dev actually serves **gpt-4.1-mini**, which the model retirement schedule also deprecates on 2027-04-14 (research note section 4.1).
5. `gpt-5.4-mini` and `gpt-5.6-luna` deployments are on a different resource than the CU resource, as the task specifies; CU itself uses `gpt-5.2` only.

## 11. Hand-off issue - step 8

DEF-001 / https://github.com/spaarke-dev/spaarke/issues/1915 (labels defer, next-round, ebilling-r1; on board Spaarke Core, Type = Idea). Listed in `notes/defer-issues.md`.

## 12. Unchanged by design

`infrastructure/bicep/modules/openai.bicep`, `PinnedModelCatalog`, `DocumentIntelligence:FastModel/StandardModel/ReasoningModel` template values and dev App Service settings: not edited (git diff shows only the template section, this note, `defer-issues.md` and the POML status).

## 13. Rollback

`az cognitiveservices account deployment delete` for the three deployments; `PATCH` defaults back to `{"prebuilt-analyzer-completion":"gpt-5-608736"}` and remove the `gpt-5.2` key (merge-patch with null); `az role assignment delete --ids <assignment id above>`.

rework: none task 003
