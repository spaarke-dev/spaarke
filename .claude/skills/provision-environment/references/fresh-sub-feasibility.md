# Fresh-subscription feasibility (Step 2.5) and the automation gaps

Moved from SKILL.md Step 2.5 and the "Fresh-Sub Automation Gaps" section (2026-11-03). SKILL.md says when to read
this file. The checks here were and remain **operator-run manual queries** (the original's "MVP: INFORMATIONAL"
status below): no script automates them; run the queries yourself with the customer's subscription, read the result,
and apply the decision matrix. L2's H0 preflight runs the F4 and F5 checks itself.

## Contents

- Step 2.5 — when to run, F1–F10 checks, decision matrix, the summary to show
- Fresh-Sub Automation Gaps — the F1–F20a findings table
- What r1 delivery still needs (roadmap)
- The Customer Deployment Web App

## Step 2.5 — Fresh-Sub Deployment Feasibility Check

> **T228**: every check below runs against the **customer's own subscription** (intake `subscriptionId`), never the
> operator's current `az account`: Step 0.5b's `$subId` is rebound here.
>
> ```powershell
> $subId = $subscriptionId   # T228 — the customer's subscription (Step 1b-bis); F3–F6 / F10 below read $subId
> ```

**When to run**: Target Azure subscription was created within the last 90 days, OR this is the FIRST Bicep deploy attempt against this subscription in this region. Fresh subs have gotchas Microsoft has quietly introduced since 2024-2025 that break naive "just deploy" flows. These checks run OPERATOR-SIDE (in this skill) before invoking L2 H0, because L2 doesn't have the visibility (or the mandate) to modify region defaults or Bicep params.

Full evidence base: `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` (findings F1-F9).

Automated checks (each MUST pass or auto-remediate before Step 2):

**F1 — OpenAI model pin freshness**:
```powershell
$pins = (Get-Content infrastructure/bicep/stacks/{stack}.bicep | Select-String "version: '(\d{4}-\d{2}-\d{2})'").Matches.Groups[1].Value
foreach ($pin in $pins) {
  $models = az cognitiveservices model list --location $openAiLocation --query "[?kind=='OpenAI' && model.version=='$pin']" -o json | ConvertFrom-Json
  if ($models[0].model.lifecycleStatus -in @('Deprecating','Deprecated')) {
    # HALT — pin is dead. Recommend bump to latest GA/Legacy in same family.
  }
}
```

**F2 — Deployment SKU compatibility**:
- Verify each pinned model's `sku` field is in the list returned by `az cognitiveservices model list --query "[?...].model.skus"`
- gpt-5.x family REQUIRES `GlobalStandard` (gpt-5-pro literally supports NO other SKU)
- Legacy gpt-4o family accepts `Standard`

**F3 — Primary region App Service quota feasibility** (the template this snippet names, `refs/test-s1-plan.bicep`, does not exist — MOVE-MAP D12; `$tempRg`, `$candidateRegion` and `{stack}` are placeholders to fill in. Until a template exists, check App Service quota for the candidate region in the Portal or with `az quota`):
```powershell
# Deploy a throwaway S1 App Service Plan via what-if to test quota
az deployment group what-if --resource-group $tempRg --template-file .claude/skills/provision-environment/refs/test-s1-plan.bicep `
  --parameters location=$candidateRegion --subscription $subId
# If SubscriptionIsOverQuotaForSku → this region has quota walls; auto-fallback to westus2 (Spaarke canonical)
```

**F4 — OpenAI region GA availability**:
```powershell
# Confirm all pinned models are GA in the stamp's openAiLocation (customer.bicep param; default westus3)
az cognitiveservices model list --location $openAiLocation --subscription $subId --query "[?kind=='OpenAI' && model.name=='$pinnedModelName']" -o table
# Empty result → model not offered in this region; pick another openAiLocation (westus2 offers NO OpenAI models — checked 2026-10-06)
# L2's H0 runs this check itself (openai-pin-freshness, against PinnedModelCatalog.Models).
```

**F5 — Auto-allocated TPM detection**:
```powershell
az rest --method get --url "https://management.azure.com/subscriptions/$subId/providers/Microsoft.CognitiveServices/locations/$openAiRegion/usages?api-version=2023-05-01" `
  --query "value[?limit != '0']" -o json
```
- Enumerate what's already granted. L2's H0 runs this check itself (azure-openai-tpm-headroom, per Azure quota name, against `PinnedModelCatalog`).
- **No recompose (task 247, owner 2026-10-06).** The stamp deployment set is fixed: the BFF calls every deployment by NAME (`gpt-4o`, `gpt-4o-mini`, `text-embedding-3-large`), so dropping or swapping one deploys a stamp that fails at runtime. The set uses DataZoneStandard, which fresh subscriptions auto-grant (westus3, 2026-10-06: gpt-4o 300, gpt4.1-mini 2000, text-embedding-3-large 1000 — vs. a 150/200/350 request). A shortfall fails H0 before anything deploys; the fix is a quota increase or another `openAiLocation`.

**F6 — Provider registration retry-verify loop**:
```powershell
foreach ($ns in $requiredProviders) {
  az provider register --namespace $ns --subscription $subId -o none
  $deadline = (Get-Date).AddMinutes(5)
  while ((Get-Date) -lt $deadline) {
    $state = az provider show -n $ns --query registrationState -o tsv
    if ($state -eq 'Registered') { break }
    Start-Sleep 30
  }
  if ($state -ne 'Registered') {
    # HALT — provide operator with Portal link: https://portal.azure.com/#view/HubsExtension/BrowseAll → subscription → Resource providers → search "$ns" → Register
  }
}
```

**F7 — Fresh-sub UX preamble**:
- Warn operator: "Portal Usage+Quotas dropdown will show empty until resources exist; use https://ai.azure.com Quotas for OpenAI TPM visibility"

**F8 — Support ticket: NOT USED** (owner: an ordinary stamp must deploy without a Microsoft support case). The deployment set is chosen to fit fresh-subscription auto-grants (F5), so no ticket path is needed.

**F10 — Global resource-name availability pre-check** (added 2026-08-22 after F10 discovery):
```powershell
# Service Bus namespace (Azure reserves suffixes like -sb globally)
az rest --method post --url "https://management.azure.com/subscriptions/$subId/providers/Microsoft.ServiceBus/checkNameAvailability?api-version=2022-10-01-preview" `
  --body "{`"name`":`"$sbName`",`"type`":`"Microsoft.ServiceBus/namespaces`"}"
# Storage account (global namespace)
az storage account check-name --name $storageName
# Cognitive Services custom subdomain (global namespace)
az cognitiveservices account check-domain-availability --subdomain-name $openAiName --type OpenAI
```
what-if does NOT run these checks — only actual create-time validation catches global-namespace conflicts. This gap wasted 16m35s on this session's first deploy attempt. Skill Step 2.5 MUST run these before invoking `az deployment sub create`.

**F9 — Support Plan check: NOT USED** (no ticket-dependent action exists — see F8).

**Auto-remediation vs HALT decision matrix**:
| Finding | Auto-remediate? | Fallback |
|---|---|---|
| F1 (pin stale) | NO (requires operator ADR-020 sign-off on new pin) | HALT + recommend bump |
| F2 (SKU wrong) | YES (bicepparam auto-generation) | Log the change |
| F3 (region quota wall) | YES (fallback to westus2) | Log region pivot with rationale |
| F4 (OpenAI region absence) | YES (`openAiLocation` defaults to westus3; westus2 has no OpenAI models) | Log the split |
| F5 (auto-quota mismatch) | NO (task 247: the set is fixed — the BFF calls each deployment by name) | HALT at H0 naming the Azure quotas that fall short; raise quota or change `openAiLocation` |
| F6 (provider reg hang) | Retry 5 min, then HALT | Portal link |
| F7 (UX preamble) | Informational — always show | N/A |
| F8 / F9 (support ticket / plan) | Not used — no support case (owner) | N/A |

**Skill output on completion of Step 2.5**:
```
FRESH-SUB FEASIBILITY (customer-provisioning-orchestration-r1 lessons):
  [PASS/AUTO-FIX/HALT] F1 OpenAI pin freshness: 3 of 3 pins GA in westus3
  [AUTO-FIX] F3 Primary region: eastus quota wall detected → auto-pivoted to westus2
  [PASS] F4 OpenAI region: openAiLocation=westus3 (westus2 offers no OpenAI models)
  [PASS] F5 DataZoneStandard quota in westus3 covers gpt-4o 150 / gpt4.1-mini 200 / text-embedding-3-large 350
  [PASS] F6 All required providers registered

Proceeding to Step 2 (L2 H0 preflight)...
```

**Current status of Step 2.5 automation** (as of 2026-08-22):
- MVP: Step 2.5 exists as INFORMATIONAL — operator is prompted to check findings F1-F9 manually with the queries above
- Full E2E-no-human-interaction: Step 2.5 becomes FULLY AUTOMATED — the skill absorbs each check as an automation, with the auto-remediation matrix above driving behavior
- Owner directive 2026-08-22: "the expectation for the final delivered solutions is that this process will run E2E with no human interaction. Ultimately the best solution is we have a 'Customer Deployment' web app that allows the user to input whatever information/setting choices and Claude Code / the scripts run everything." Absorbing F1-F9 into Step 2.5 automation is the largest single gap between MVP and full E2E.

## Fresh-Sub Automation Gaps (customer-provisioning-orchestration-r1 lessons 2026-08-22)

**Evidence base**: `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` (findings F1-F20, F20a — 2026-08-22 through 2026-08-24 live stand-up)

**Owner directive**: "the expectation for the final delivered solutions is that this process will run E2E with no human interaction. Ultimately the best solution is we have a 'Customer Deployment' web app that allows the user to input whatever information/setting choices and Claude Code / the scripts run everything."

The first live Model 1 Prod stand-up (2026-08-22, sub `cd95fcec-...`) surfaced 9 gotchas Microsoft has quietly introduced since 2024-2025 that break naive "just deploy" flows for fresh Azure subscriptions. Each gotcha is an automation gap this skill MUST absorb before we can claim E2E-no-human-interaction. Step 2.5 (above) is where the automation lives.

| # | Finding | Auto-remediation strategy | Status |
|---|---|---|---|
| F1 | OpenAI model version pins age ~4-6 months; Microsoft blocks new deploys of Deprecating pins | Pre-deploy `az cognitiveservices model list` check; HALT + operator sign-off for pin bump (ADR-020) | MVP: informational; TODO: fully automated with operator confirmation |
| F2 | gpt-5.x family REQUIRES GlobalStandard SKU; module hardcoded 'Standard' broke this | Per-deployment `sku` field in openai module (safe-access default 'Standard') — DONE this session | ✅ Module fix committed in `798f61c9` |
| F3 | East US fresh subs have 0 App Service quota AND Portal auto-denies quota request | Preflight test-deploy in candidate region; auto-fallback to westus2 (Spaarke canonical) | MVP: manual; TODO: automated region auto-selection |
| F4 | West US 2 has NO gpt-5 family; West US 3 has all (2026-10-06: West US 2 offers no OpenAI models at all) | customer.bicep `openAiLocation` (default westus3) splits the OpenAI account from the primary region | ✅ Template default; H0 checks quota + pins in `openAiLocation` (task 247) |
| F5 | Fresh subs auto-grant mini/embedding TPM generously (500+); frontier tiers (gpt-5.4, gpt-5-pro) = 0; Standard gpt-4o = 0 | Choose a deployment set that fits the auto-grants (DataZoneStandard gpt-4o / gpt-4.1-mini / text-embedding-3-large); no recompose — the BFF calls each deployment by name | ✅ Task 247: fixed set + H0 quota check per Azure quota name |
| F6 | `az provider register` reports success but state stays NotRegistered on fresh subs | Retry-verify loop 5 min; HALT with Portal link if not registered | TODO: not implemented |
| F7 | Portal Usage+Quotas provider dropdown empty on fresh subs (only shows providers with existing resources) | Preemptive operator warning + link to https://ai.azure.com Quotas | MVP: informational only |
| F8 | Portal auto-denies fresh-sub quota requests + pushes to Support Ticket | Avoid needing one: the deployment set fits auto-grants (F5) | ✅ Not used — no support case (owner) |
| F9 | Support Plan availability varies; skill must not queue ticket-dependent actions on plan-less sub | No ticket-dependent action exists (F8) | ✅ Not needed |
| F10 | Global resource-name reservations not caught by what-if (Service Bus `-sb` suffix, etc.) — burned 16m35s on this session's first deploy | Run `az {svc} check-name` for every resource with global namespace BEFORE `az deployment sub create` | Bicep fix committed; skill automation TODO |
| F11 | Cognitive Services accounts hold a 3-5 min soft-lock after failed deploys (invisible to `provisioningState`); back-to-back retries fail with RequestConflict even when everything reads Succeeded | Detect RequestConflict on CogSvc writes + linear backoff retry (30s → 90s → 180s → 300s) | TODO: not implemented — burned 3 failed retries this session; 3-min explicit `sleep 180` broke through |
| F12 | `Build-SpaarkeMaster.ps1` was calling `AddSolutionComponent AddRequiredComponents=$false` → managed export had 105 self-referencing "leaky" deps against `solution="Active"`. Fresh env installs failed with 240 total MissingDependency (105 Cat B + 135 Cat A first-party) | Line 138 changed to `$true`; rebuilt in spaarkedev1 → 485 components (was 386); re-export → 77 MissingDep, ALL Category A (Cat B eliminated) | ✅ Script fix committed on this branch; longer-term automation TODO: nightly smoke-install job on rebuilt .zip to a throwaway env + CI assert `MissingDependency solution="Active"` count == 0 |
| F13 | Fresh Production-tier envs do NOT include Power BI Extensions (`msft_PowerBI_Anchor`, publisher solution `msft_PowerBI_Entities`, `isFirstParty="False"`). One SpaarkeMaster env-var-def carries a spurious dep on `powerbimashupparameter` → import fails with 1 unresolved MissingDependency even after F12 fix | Pre-import: intersect `pac application list --environment {env}` with a `Required Applications` manifest; auto-install any missing via `pac application install --application-name {name}` in a loop (each takes ~6 min, polls every 30s). Initial manifest: `msft_PowerBI_Anchor` | ✅ Installed manually this session; automation TODO — introduce `Required Applications` config-driven manifest on H6 solution-import handler; longer-term: fix the spurious dep at source in Build-SpaarkeMaster.ps1 |
| F14 | Fresh Production-tier envs default `organization.maxuploadfilesize = 5,242,880` (5 MB); UniversalDocumentUpload PCF bundle exceeds this. Import fails 5min in with "Webresource content size is too big" | Pre-import: run `pac org list-settings` → compare against Spaarke `Org Settings Contract` → any drift → auto-apply via `pac org update-settings --name maxuploadfilesize --value 25600000`. Idempotent + fast (single API call). Initial contract: `maxuploadfilesize: 25_600_000` (25 MB, matches spaarkedev1) | ✅ Applied manually this session; automation TODO — introduce `Org Settings Contract` config-driven map on H6 solution-import handler |
| F15 | Fresh RBAC-enabled Key Vaults grant NO data-plane access even to subscription Owner. `enableRbacAuthorization=true` KVs require explicit data-plane role (`Key Vault Secrets Officer` for read/write). `az role assignment create` had a `MissingSubscription` bug on this endpoint (F15b) — use `az rest --method put` fallback | Post-Bicep: detect operator OID + KV RBAC mode; if RBAC=true, grant `Key Vault Secrets Officer` (role ID `b86a8fe4-44ce-4948-aee5-eccb2c155cd7`) via `az rest`; poll `az keyvault secret list` until non-403. Idempotent | ✅ Applied manually this session for per-tenant KV; automation TODO — operator-RBAC-bootstrap step in H3 or pre-H4 |
| F16 | Shared BFF App Service `keyVaultReferenceIdentity` set to literal `"SystemAssigned"` but only UserAssigned identity attached → all `@Microsoft.KeyVault(...)` refs silently unresolvable. Also shared UAMI has 0 data-plane RBAC on the shared KV. Two independent misconfigs, both from Bicep | (Not yet applied — planned) Grant shared UAMI `Key Vault Secrets User` on shared KV via `az rest`; PATCH kvRefIdentity to UAMI resource ID (not "SystemAssigned") via `az webapp update --set keyVaultReferenceIdentity=...`; apply to both production + staging slots per T1 rule; restart App Service | Bicep hardening TODO: (a) never emit `keyVaultReferenceIdentity='SystemAssigned'` when only UAMI attached, (b) emit role assignments for attached UAMIs on referenced KVs. T1 handler must verify + auto-remediate |
| F17 | Bicep deploy provisions App Service resource but does NOT deploy BFF code. Root URL returns default "empty App Service" page; `/healthz` returns 404. All subsequent config work (KV refs, App User, /healthz verify) tests an empty shell | Fresh-env case: detect empty App Service via root-URL response check; build+publish BFF (`dotnet publish -c Release`); zip-deploy via `az webapp deploy --type zip`; poll `/healthz` with 30-90s warm-up backoff. NFR-01 constrains publish size ≤60 MB | H9 handler exists as name in catalog but no code yet. Reference `.claude/skills/deploy-new-release/SKILL.md` (built for shared-env slot-swap, needs fresh-env variant) — verified this session with 46 MB compressed publish (PASSES NFR-01) |
| F16.5 | `az webapp update -g <rg> -n <name> --set keyVaultReferenceIdentity="<uami-resource-id>"` returns `ERROR: Operation returned an invalid status 'Bad Request'` — the CLI wrapper does not accept this property path (mirror of F15b bug pattern for a different endpoint). Blocks the F16 Part B remediation via the obvious CLI form | Use `az rest --method patch --url "https://management.azure.com/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Web/sites/{name}?api-version=2022-03-01" --body '{"properties":{"keyVaultReferenceIdentity":"<uami-resource-id>"}}'` fallback | ✅ Fallback applied this session; T1 handler should skip `az webapp update --set` and go straight to `az rest --method patch` (or file Azure CLI issue upstream) |
| F18 | Fresh RBAC-enabled SHARED KV grants NO data-plane access even to subscription Owner — SAME F15 pattern for a different vault. Shared KV `sprk-prod-kv` blocked operator from listing/reading/writing secrets during F19 investigation | Same as F15: pre-H4 bootstrap grants operator `Key Vault Secrets Officer` via `az rest` fallback. Idempotent. Extend F15 handler to ALL fresh RBAC-enabled KVs in scope (per-tenant AND shared) | ✅ Applied manually this session; automation TODO absorbed into F15 handler (widen scope to enumerate every KV in RG) |
| F19 | Bicep deploy provisions shared KV `sprk-prod-kv` but leaves it EMPTY — all 6 BFF `@Microsoft.KeyVault(...)` references point to non-existent secrets. Even after F16 fixes (RBAC + kvRefIdentity), refs cannot resolve. BFF fail-fast at boot on missing config downstream | H4-shared handler: extract keys/conn-strings from source Azure services (AI Search admin key, Cog Svc key1, SB RootManageSharedAccessKey, Storage conn string, Redis primaryKey composed with `<host>:<port>,password=<key>,ssl=True,abortConnect=False`), seed to KV under canonical secret names matching App Service KV-ref manifest. Idempotent | ✅ Applied manually this session (6 secrets seeded); H4 handler needs extending — currently H4 is scoped per-tenant KV; needs sibling H4-shared handler + canonical secret-name manifest (with F19 initial map) |
| F20 | BFF crashes at startup with exit code 134 (SIGABRT) via `Unhandled exception. System.InvalidOperationException: SpeAdmin:KeyVaultUri (or KeyVaultUri) configuration is required for SpeAdminModule.` (`SpeAdminModule.cs:45`). Bicep provisions App Service with 14 app settings incl. 6 KV refs — but NOT the app settings that hard-required IOptions modules read directly (bypass `ValidateOnStart`). BFF has ~40 `.ValidateOnStart()` modules — many will surface as F20a/b/c... in sequence | H4b-BulkAppSettings handler (companion to H4-KV-seed): read canonical app-settings template from a manifest, resolve KV refs, `az webapp config appsettings set --settings k1=v1 k2=v2 ...` (single call, single restart). Template driven from `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` § App Service settings + per-env inputs (TenantId, BFF ClientId, ContainerTypeId, Webhook signing keys, EmailProcessing signing key) | ❌ NOT YET REMEDIATED — chose NOT to serially chase 40-module chain this session; single setting `SpeAdmin__KeyVaultUri` applied revealed F20a. Full remediation deferred to design of H4b handler |
| F20a | After F20 setting applied, next fail-fast: `Unhandled exception. System.InvalidOperationException: CosmosPersistence:Endpoint is not configured. Add this setting to appsettings.json or Azure App Service configuration.` (`AiPersistenceModule.cs:56`). Progressive discovery pattern confirmed | Same as F20 — batch-set all required app settings from canonical template in one call | ❌ NOT YET REMEDIATED — evidence for F20 progressive chain; validates the "40 IOptions modules" scale claim |

## What r1 delivery still needs (roadmap)

Before r1 can claim E2E-no-human-interaction:

1. **Absorb F1-F20 into automated Step 2.5 + H4/H4b handlers + H6 solution-import handler + H9 BFF-deploy handler** (currently mostly informational)
2. **Codify Spaarke canonical region defaults**: westus2 platform + westus3 OpenAI (baked into `Model 1 Prod` profile in `pac admin create`)
3. **Parameterize the OpenAI deployment set** in `customer.bicep` (it passes none today — `openai.bicep` defaults apply) so the skill can compute the deployment set at runtime (auto-quota compatible → full P5 progressive upgrade). The original item named `stacks/model1-shared.bicep`, retired by T225a.
4. **Auto-registration retry-verify** for all `Microsoft.*` providers
5. **Auto-support-ticket flow** for cases where auto-grant path doesn't exist (advanced, gated on operator having Support Plan)
6. (Superseded T253, 2026-10-09: the CI-built SpaarkeMaster has no Power BI dependency; H6 installs no application, PRQ-C-07 retired, `SpaarkeMasterApplicationDependencyTests` guards it.) **Introduce `Required Applications` manifest** on H6 solution-import handler (F13): config-driven list of AppSource apps that MUST be pre-installed on any Spaarke target env before SpaarkeMaster import. Initial list: `msft_PowerBI_Anchor`. Pre-import intersect + auto-install via `pac application install` loop.
7. (Done T253: H6 applies it with one Dataverse Web API `organization` PATCH, no pac.) **Introduce `Org Settings Contract`** on H6 solution-import handler (F14): config-driven map of `settingName → minValue` that MUST be applied to any Spaarke target env before SpaarkeMaster import. Initial map: `maxuploadfilesize: 25_600_000`. Pre-import diff + auto-apply via `pac org update-settings`. Idempotent, single-call, ~2s per setting.
8. **(Partly done, T218c 2026-10-07: `Export-SpaarkeMasterSource.ps1` fails on any `solution="Active"` missing dependency; `Build-SpaarkeMaster.ps1` retired.) Add nightly smoke-install job** for the SpaarkeMaster export (F12 forcing-function): re-export managed .zip → extract solution.xml → CI asserts `MissingDependency solution="Active"` count == 0. Catches regressions in the leaky-export fix before they reach fresh-env installs.
9. **Operator-RBAC-bootstrap step** (F15): idempotent pre-H4 grant of `Key Vault Secrets Officer` to operator on every RBAC-enabled KV, via `az rest` (F15b bypass). Uses `az ad signed-in-user show` for OID auto-detect. Silent success on re-run.
10. **Bicep hardening for kvRefIdentity + UAMI-KV RBAC** (F16): (a) reject `keyVaultReferenceIdentity='SystemAssigned'` combined with UserAssigned-only identity in the Bicep template; (b) auto-emit role assignments for attached UAMIs on referenced KVs. Backstop: T1 handler verifies + auto-remediates any drift post-deploy.
11. **Fresh-env BFF deploy handler** (F17): H9 currently exists as a catalog name only. Needs code that (a) detects empty-App-Service state, (b) builds + zip-deploys BFF, (c) polls `/healthz` with warm-up backoff, (d) sequences AFTER F16 remediation so BFF starts in configured state (not degraded). **This session verified: 46 MB compressed publish passes NFR-01 60 MB ceiling; `az webapp deploy --type zip` uploads cleanly but Site Startup Probe fails when config chain (F20) unresolved.**
12. **H4-shared handler + canonical secret manifest** (F19) — *H4-shared shipped (task 200) and was RETIRED by T226 (2026-09-30): customer stamps never read a shared service's credential; the manifest half stands.* Original remediation item: sibling to H4 (per-tenant); H4-shared extracts keys from source Azure services (AI Search admin key, Cog Svc key1s, SB RootManageSharedAccessKey, Storage conn string, Redis composed conn string) and seeds to shared KV under canonical secret names. Initial 6-secret manifest: `AiSearch--AdminKey`, `DocumentIntelligence-ApiKey`, `AzureOpenAI-ApiKey`, `servicebus-connection-string`, `storage-connection-string`, `redis-connection-string` (must MATCH the App Service `@Microsoft.KeyVault(SecretName=...)` refs). **📝 Handler POML designed 2026-08-24 SESSION 3: [`projects/customer-provisioning-orchestration-r1/tasks/200-implement-h4-shared-kv-source-extraction-handler.poml`](../../../../projects/customer-provisioning-orchestration-r1/tasks/200-implement-h4-shared-kv-source-extraction-handler.poml). Extends task 084 manifest schema with `source: { type, service-ref }` field. Includes IArmKeyVaultRefProbe post-condition (uses F16-remediated kvRefIdentity). Bicep hardening implied: L2 UAMI needs 5 new RBAC assignments on source services (`Cognitive Services User`, `Search Service Contributor`, `Azure Service Bus Data Owner`, `Storage Account Contributor`, `Redis Cache Contributor`).**
13. **H4b-BulkAppSettings handler** (F20/F20a): CRITICAL NEW HANDLER. Reads canonical BFF app-settings template (~40 IOptions modules × ~2-4 settings each ≈ 80-160 app settings) + resolves KV refs + per-env inputs (TenantId, BFF ClientId, ContainerTypeId, WebhookSigningKeys, EmailProcessing WebhookSigningKey), calls `az webapp config appsettings set --settings k1=v1 k2=v2 ...` in single batch to trigger ONE restart cycle. Manifest source: `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` § App Service settings. **This handler is the difference between "BFF App Service exists" and "BFF actually boots" — without it, F20 chain progressively reveals ~40 missing configs.** **📝 Handler POML designed 2026-08-24 SESSION 3: [`projects/customer-provisioning-orchestration-r1/tasks/201-implement-h4b-bulk-appsettings-handler.poml`](../../../../projects/customer-provisioning-orchestration-r1/tasks/201-implement-h4b-bulk-appsettings-handler.poml). Introduces NEW canonical manifest at `scripts/canonical-app-settings/manifest.yaml` (sibling to task 084 secret-catalog). Diff-first idempotency preserves operator overrides. IHealthzProbe polls `/healthz` with 8-min backoff + parses container docker-logs on failure to extract fail-fast module name for actionable diagnostic. Sequencing: H4-shared || H4-per-tenant → H4b → H9 → BFF boots configured.**
14. **F16 Bicep hardening (extend)**: (a) never emit `keyVaultReferenceIdentity='SystemAssigned'` when only UserAssigned attached, (b) auto-emit role assignments for attached UAMIs on referenced KVs — **AND** (c) via F16.5 discovery: T1 handler skips `az webapp update --set keyVaultReferenceIdentity=...` (returns Bad Request); goes straight to `az rest --method patch` on the site resource with `{"properties":{"keyVaultReferenceIdentity":"..."}}` body.

## The Customer Deployment Web App (natural evolution)

Owner-directed follow-on project: replace the operator-invoked skill with a self-service web UI. Prospect/ops user fills a form; the L3 skill (fully automated per above) executes end-to-end; SSE stream provides real-time progress.

Prerequisites:
- All F1-F9 absorbed into fully-automated Step 2.5
- L2 control-plane `/api/runs` API surfaced via BFF
- All handlers H0-H14 live-validated (in-progress; see current-task.md for status)

Filing: proposed as `projects/customer-deployment-webapp-r1` (skeleton to be created after r1 completes).

