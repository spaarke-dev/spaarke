# Lessons Learned: Model 1 Production First-Live Stand-Up (2026-08-22)

> **Session context**: customer-provisioning-orchestration-r1, first ever live deploy of `infrastructure/bicep/stacks/model1-shared.bicep` against a fresh Azure subscription.
> **Sub**: Spaarke Model 1 Production (`cd95fcec-6b89-49ea-8339-c2b579b12587`)
> **Sub type**: Pay-As-You-Go (PayAsYouGo_2014-09-01), spending limit Off, created same day
> **Tenant**: Spaarke (`a221a95e-6abc-4434-aecc-e48338a1b2f2`)
> **Operator**: `ralph.schroeder@spaarke.com` (Owner)
> **Commits**: `798f61c9` + `f90918f8`
>
> **Primary directive from owner**: "the expectation for the final delivered solutions is that this process will run E2E with no human interaction. Ultimately the best solution is we have a 'Customer Deployment' web app that allows the user to input whatever information/setting choices and Claude Code / the scripts run everything."
>
> **This document is REQUIRED READING** for any future operator (human or agent) standing up a new Spaarke Azure sub. Each finding below is an automation-gap the r1 skill / scripts / bicep must absorb before we can claim E2E-no-human-interaction.

---

## Executive Summary

Two fix-at-discovery iterations were required before preflight passed. The fresh-sub experience diverges materially from the established-sub experience — Microsoft has quietly tightened new-sub defaults since 2024-2025 (anti-abuse response to crypto-mining + AI-quota exploitation waves). Established subs like `Spaarke Development Environment` don't feel this friction because their auto-grants happened years ago when defaults were more generous.

**Key implication for r1 delivery**: The `Provision-Customer.ps1` script (r2, 1,632 lines, 13 steps) that was validated during r2 development was ONLY EVER tested against the established dev sub. It has never been proven against a fresh-sub scenario. This session is that first proof, and every gotcha we hit is a gap in the r1 skill's promise of E2E-no-human-interaction.

---

## Discoveries (blocking → resolved this session)

### F1: OpenAI model version pins age fast (~4-6 month deprecation cycle)

**Symptom**: Preflight error `ServiceModelDeprecated - The model 'Format:OpenAI,Name:gpt-4o,Version:2024-08-06' has been deprecated since 03/31/2026`.

**Root cause**: `infrastructure/bicep/stacks/model1-shared.bicep` pinned `gpt-4o:2024-08-06` (correct when authored ~4 months prior). Microsoft announced deprecation 2026-03-31 (5 months before deploy attempt). Deprecated models are BLOCKED from new deploys even though existing deploys continue working until retirement date.

**Fix applied**: Bumped to `gpt-5.4:2026-03-05` (Standard tier) + `gpt-5-mini:2025-08-07` (Fast tier) + `gpt-5-pro:2025-10-06` (Reasoning tier) — all currently Generally Available.

**Automation gap**: No pre-deploy check for pin freshness. The skill / script MUST run `az cognitiveservices model list --location {region}` and cross-check against pinned versions BEFORE attempting any greenfield Bicep deploy.

**Automation TODO (r1)**: Add "OpenAI Pin Staleness Check" to skill Step 0.5 preflight. If any pinned version is not `GenerallyAvailable` OR `Legacy` in the target region, HALT with actionable recommendation to bump the pin.

---

### F2: gpt-5.x family requires GlobalStandard SKU (not Standard)

**Symptom**: After F1 fix, preflight error `InvalidResourceProperties - The specified SKU 'Standard' of account deployment is not supported by the model 'gpt-5.4' version: '2026-03-05'`.

**Root cause**: `infrastructure/bicep/modules/openai.bicep` hardcoded `sku.name = 'Standard'` for ALL deployments. gpt-5.x family REQUIRES GlobalStandard (gpt-5-pro literally supports NO other SKU). gpt-4o family supported Standard; the module was locked to that historical assumption.

**Fix applied**: Parameterized the module's SKU field via `deployment.?sku ?? 'Standard'` (safe-access operator, backward-compatible default). Each deployment object can now specify its own `sku`. Model 1 stack sets `sku: 'GlobalStandard'` on all 4 gpt-5.x + embedding entries.

**Automation gap**: Modules with hardcoded SKU strings will break every time Azure introduces a new SKU convention. Modules must be flexible for evolving Azure SKU expectations.

**Automation TODO (r1 / long-term)**: Audit every `infrastructure/bicep/modules/*.bicep` for hardcoded SKU assumptions. Convention: any SKU that could reasonably vary per deployment should be parameterized with a sensible default.

---

### F3: East US new subs have 0 auto-allocated App Service Plan quota AND request is auto-denied

**Symptom**: Preflight error `SubscriptionIsOverQuotaForSku - Current Limit (Total VMs): 0 ... Amount required: 1`. Portal quota request for S1 East US **auto-DENIED by Azure**. Retry in West US: also denied.

**Root cause investigation**:
- Sub metadata: `PayAsYouGo_2014-09-01`, spending limit Off — identical to dev sub
- Sub-level policies: NONE (no restrictive management group either)
- Compute provider: Registered (via Portal auto)
- Yet App Service Plan quota bump was auto-denied
- **Isolated single-resource preflight in West US 2: SUCCEEDED cleanly**
- Fresh Azure subs have zero auto-granted quota for App Service Plan SKUs in East US specifically (2026 Microsoft capacity/anti-abuse tightening for East US)
- West US 2 auto-grants on first deploy transparently

**Fix applied**: Pivot primary `location` from `eastus` → `westus2`. Matches r2's proven pattern (`spaarke-bff-prod` was West US 2). Preflight passes cleanly on the same sub in West US 2 without any quota action.

**Automation gap**: The skill / script has NO region intelligence. It must detect that the target region has quota issues and either (a) auto-fallback to a region with auto-grants, or (b) auto-file a support ticket for the quota bump.

**Automation TODO (r1)**:
- Add "Region Feasibility Check" to skill Step 0.7 preflight (after Prereqs, before Intake finalization).
- Attempt a dry-run `az deployment group what-if` against a throwaway RG in each candidate region.
- Rank regions by preflight-clean status.
- Present the top-ranked region to the operator, or auto-select if operator has already specified preference.
- Codify the WestUS2 (platform) + WestUS3 (OpenAI) pattern as the DEFAULT for Model 1 Prod.

---

### F4: West US 2 has NO gpt-5 family OpenAI models (only East US + West US 3 do)

**Symptom**: After pivoting to WestUS2, preflight error surfaced NEW: `InsufficientQuota - gpt-5.4 - GlobalStandard` in West US 2. Actually root cause was availability, not quota — West US 2 doesn't offer gpt-5 family at all.

**Root cause investigation**: `az cognitiveservices model list --location westus2` returned EMPTY for gpt-5 family. Same query on `westus3` returned FULL gpt-5 family (gpt-5, gpt-5-mini, gpt-5-pro, gpt-5.4, etc, all Generally Available).

**Fix applied**: Added `sharedOpenAiLocation` param to `stacks/model1-shared.bicep` (defaults to primary `location` for back-compat, override to `westus3` in `model1-prod.bicepparam`). Same-sub cross-region OpenAI is a standard Azure pattern — matches r2's `spaarke-openai-prod` (WestUS3) + `spaarke-bff-prod` (WestUS2) split.

**Automation gap**: OpenAI region availability is orthogonal to primary platform region availability. Neither the operator nor the script should assume `location` == `openAiLocation`.

**Automation TODO (r1)**:
- Auto-detect gpt-5 availability per region during preflight.
- Compose the primary `location` + `sharedOpenAiLocation` combo automatically.
- Document Spaarke's canonical region strategy: **WestUS2 platform + WestUS3 OpenAI** for all Model 1 Prod deploys.

---

### F5: Fresh subs get 0 auto-granted TPM for FRONTIER OpenAI models (gpt-5.4, gpt-5-pro)

**Symptom**: After F4 pivot, preflight error `InsufficientQuota - gpt-5.4 - GlobalStandard: limit is 0`.

**Root cause investigation**: `az cognitiveservices usage list -l westus3` on the fresh sub shows:
- **Auto-allocated generously**: gpt-5-mini GlobalStandard 500 TPM, text-embedding-3-large Standard SKU 350 TPM, gpt-5.4-mini DataZoneStandard 200 TPM
- **Zero allocation**: gpt-5.4 GlobalStandard, gpt-5-pro GlobalStandard, text-embedding-3-large GlobalStandard
- Microsoft's post-2025 pattern: mini/nano/embedding tiers auto-grant generously (cheap compute), frontier tiers (gpt-5.4, gpt-5-pro, o1-pro) require explicit human support ticket approval (expensive GPU capacity)

**Fix applied**: Rewrote deployment set in `stacks/model1-shared.bicep` to use ONLY auto-allocated resources:
- `gpt-4o` alias → gpt-5-mini @ 200 TPM (Standard tier substitute — mini class in Standard slot)
- `gpt-4o-mini` alias → gpt-5-mini @ 100 TPM (Fast tier natural home)
- `text-embedding-3-large` → Standard SKU @ 350 TPM (auto-allocated)
- `o1-mini` (Reasoning) alias OMITTED — BFF's `ModelTierDeploymentResolver.cs` line 47-49 has documented fallback to Standard tier
- Comprehensive UPGRADE PATH comment added: how to swap back to full P5 (gpt-5.4 Standard + gpt-5-pro Reasoning + GlobalStandard embeddings) once support ticket approves the 3 remaining TPM quotas.

**Automation gap**: No "find what's auto-allocated" logic exists. Also: the deployment set was hardcoded rather than parameterized — bicepparam couldn't override the models without a stack edit.

**Automation TODO (r1)**:
- Query `az cognitiveservices usage list` at deploy time to enumerate auto-allocated models.
- Auto-compose the deployment set with a preference order: full-frontier if available → auto-quota compatible if not.
- OPTIONAL LATER: parameterize `sharedOpenAiDeployments` in the stack so bicepparam can override without editing the stack. Not done this session (would delay the deploy).
- Skill Step 0.7 should decide the model tier composition before invoking Bicep.

---

### F6: `az provider register` reports success but state stays NotRegistered

**Symptom**: `az provider register --namespace Microsoft.Compute` returned exit 0 with no error, but subsequent `az provider show` returned `registrationState: NotRegistered` for 10+ minutes. Portal auto-registered it via link click.

**Root cause**: Some Azure providers require additional silent authorization (fraud/verification hooks) before registration completes on brand-new subs. CLI's register command triggers the request but doesn't handle the silent auth flow. Portal handles it via UI click-through.

**Fix applied**: Owner clicked "register the resource provider" link in Portal. Compute became Registered within seconds.

**Automation gap**: The skill's provider registration step needs a retry-verify loop with escalation:
1. Trigger `az provider register`
2. Poll `az provider show` at 30-sec intervals for up to 5 min
3. If still `NotRegistered` after 5 min → try `az rest --method post` with `?force=true` variant
4. If still failing → HALT with owner-actionable "click this Portal link" instruction

**Automation TODO (r1)**: Add register-verify-retry loop to skill Step 0.7 preflight. Fire-and-forget `az provider register` is unreliable on fresh subs.

---

### F7: Portal Usage+Quotas Provider filter is empty until resources exist

**Symptom**: Owner opened Portal → Usage+Quotas on fresh sub → provider dropdown showed only providers that had been "activated" via resource creation (Compute yes, but no Cognitive Services / App Service until resources were deployed). Confusing when trying to file quota requests preemptively.

**Root cause**: Portal Usage+Quotas dropdown is populated based on what's active in the sub, not what's *possible* in the sub. Fresh subs = mostly empty dropdown.

**Fix applied**: Told operator to use https://ai.azure.com Quotas tab as a workaround for OpenAI quota UI (shows all TPM regardless of resource state). But this session ended up not needing manual OpenAI quota bumps.

**Automation gap**: Nothing in the skill preempts this UX confusion.

**Automation TODO (r1)**: Skill Step 0 (Prereqs / owner intake) should include a "Fresh-Sub UX Gotchas" preamble that pre-arms the operator with the alternative quota UIs and the "you'll see empty dropdowns until resources exist" note.

---

### F8: Portal auto-approver denies fresh-sub quota requests + pushes to Support Ticket

**Symptom**: Owner filed 1-unit S1 App Service Plan quota request via Portal → auto-DENIED with "Received: 0 of 1" + link to "Create a support request." Same denial in both East US and West US (before we discovered WestUS2 works via first-deploy auto-grant).

**Root cause**: Microsoft's auto-approver on brand-new subs is conservative — many requests that would auto-approve on established subs get bounced to human review. Owner's memory ("in the past didn't need support ticket") was accurate historically but Microsoft tightened in 2025-2026.

**Fix applied**: Session pivoted to auto-grant-compatible resources (F3 + F5 fixes). Support ticket was AVOIDED entirely by using regions and models with generous auto-grants.

**Automation gap**: If the ONLY path forward requires a support ticket, the skill must be able to file it programmatically (`az support in-subscription tickets create` REST API is available). Currently no such automation.

**Automation TODO (r1)**:
- FOR MVP: skill must PREFER auto-grant regions/models (the fix-at-discovery approach we used this session).
- FOR full E2E-no-human: skill must be able to auto-file support tickets via `az support` REST API when preferred config can't be met. Requires operator to have Support-plan permissions on the sub.
- Bake the region strategy (WestUS2 + WestUS3) into the skill defaults so support tickets are rarely needed.

---

### F9: Portal denial dialog "Create a support request" button is grayed until sub has support plan

**Symptom** (from owner's frustration thread): The very UX Microsoft directs operators to when auto-approving is not possible is often not available on Basic/no-plan subs. Circular UX.

**Root cause**: Support ticket creation requires a Support Plan on the sub. Free-tier subs may not have this. This wasn't a blocker this session (we bypassed via auto-grant path) but is a real future risk.

**Automation gap**: Skill needs to detect Support Plan availability and downgrade approach if not available (i.e. NEVER pick a path that requires ticketing on a sub without support).

**Automation TODO (r1)**: Add "Support Plan Availability Check" to skill Step 0.7. Never queue a support-ticket-dependent action on a sub that can't file tickets.

---

### F10: Azure Service Bus reserves the `-sb` suffix globally

**Symptom**: Deploy failed at 16m35s into the resource-create phase (not preflight — the resource-name uniqueness check only fires at actual create time, NOT during what-if):
```
NamespaceUnavailable — Namespace name 'sprksharedprod-sb' is not available.
Reason: InvalidSuffix. Message: Namespace with suffix '-sb' is reserved.
```

**Root cause**: Azure Service Bus reserves certain suffixes on namespace names GLOBALLY across all subs (probably to disambiguate from Microsoft internal namespaces). `-sb` is one of them. The Bicep stack default was `${sharedBaseName}-sb` = `sprksharedprod-sb` (fine syntactically, hit the global reserved-suffix rule at create time).

**Impact**: The whole `az deployment sub create` failed at 16m35s. BUT — Bicep's `@batchSize(1)` semantics + module ordering meant **18 of 20 resources DID get created successfully before the SB module hit the failure**. Only SB namespace + BFF App Service (which depended on SB) were missing. Fix + re-deploy is idempotent — existing 18 resources are left alone.

**Fix applied**: Changed stack default from `${sharedBaseName}-sb` → `${sharedBaseName}-servicebus`. Longer suffix (`-servicebus` unlikely to hit any reserved-suffix rule). Model 1 Prod SB name becomes `sprksharedprod-servicebus`.

**Automation gap**: what-if does NOT validate resource-name uniqueness/availability against Azure's global namespace rules. what-if is happy with `sprksharedprod-sb` because it doesn't call the Service Bus namespace pre-check API. Only actual create-time validation catches this. Same class of issue can hit:
- Storage account names (globally unique, some patterns reserved)
- Cognitive Services custom subdomain names (globally unique)
- Azure Front Door / CDN endpoints (globally unique)
- Any resource with global namespace conflicts

**Automation TODO (r1 fresh-sub Step 2.5)**: Add resource-name availability pre-check for all resources with global namespaces. Run `az servicebus namespace check-name` / `az storage account check-name` / etc. as part of Step 2.5. Catch these BEFORE the 16-minute deploy attempt.

**Related Azure API calls** to add to skill Step 2.5:
```bash
# Service Bus namespace availability check
az rest --method post --url "https://management.azure.com/subscriptions/{subId}/providers/Microsoft.ServiceBus/checkNameAvailability?api-version=2022-10-01-preview" --body '{"name":"sprksharedprod-servicebus","type":"Microsoft.ServiceBus/namespaces"}'
# Storage account name check
az storage account check-name --name sprksharedprodsa
# Cognitive Services subdomain check
az cognitiveservices account check-domain-availability --subdomain-name sprksharedprod-openai --type OpenAI
```

---

### F11: Azure Cognitive Services holds a 3-5 min soft-lock after failed deploys

**Symptom**: After F10 (Service Bus name fix), 3 subsequent deploy attempts (49s, 1m7s, 3-min-wait+retry) all failed with:
```
RequestConflict — Cannot modify resource with id '.../accounts/sprksharedprod-openai' because
the resource entity provisioning state is not terminal.
```

BUT direct query `az cognitiveservices account deployment list` + `az rest GET .../accounts/sprksharedprod-openai` both showed **provisioningState: Succeeded** for the account + all 3 child model deployments. Everything was demonstrably terminal on the resource plane. Only Bicep's write attempt hit the conflict.

**Root cause**: Azure Cognitive Services holds an internal write-lock on account resources for several minutes after a related operation completes (even successfully). This is invisible to `provisioningState` polling — Azure returns Succeeded while still enforcing the lock. Concurrent writes during this window fail with RequestConflict. Not documented in Cognitive Services API reference. Discovered empirically.

**Retry timing observed**:
- Immediately after failed deploy: RequestConflict
- 49s later: RequestConflict
- 1m7s later: RequestConflict
- **~3 min later after explicit `sleep 180`: SUCCESS**

**Fix applied**: Manual 3-min sleep between retry attempts. No Bicep change needed — the resource was fine, just needed Azure's internal lock to clear.

**Automation gap**: The skill's retry logic (currently non-existent) needs to distinguish "transient write-lock" errors from "actual conflict" errors and back off appropriately. Simple linear-backoff retry would work: 30s → 90s → 180s → 300s → fail.

**Automation TODO (r1 fresh-sub Step 2.5)**:
- Detect `RequestConflict` on CognitiveServices/accounts writes AND the specific message "provisioning state is not terminal" AND the direct resource query returns Succeeded → treat as transient soft-lock, back off + retry
- Cap total retry wait at ~10 min; escalate to operator if still blocked

**Broader class of issue**: Cognitive Services is unusually strict about back-to-back writes. Related quirks observed on other Cognitive Services resources (Doc Intelligence, AI Search) — same class of transient write-lock. The retry-with-backoff pattern applies universally to `Microsoft.CognitiveServices/*` and `Microsoft.Search/*` post-failed-deploy scenarios.

### F12: SpaarkeMaster leaky managed-export missing 240 dependencies on install to fresh env

**Symptom**: Import of exported managed `SpaarkeMaster.zip` to fresh Model 1 Prod env failed with 240 `<MissingDependency>` entries in the manifest error. The zip built cleanly in spaarkedev1 (source env) and installs there via reset — but a fresh env with only baseline first-party solutions rejects it.

**Root cause**: `scripts/Build-SpaarkeMaster.ps1` line 138 was calling the `AddSolutionComponent` action with `AddRequiredComponents = $false`. This adds top-level entities to the solution WITHOUT auto-including their subcomponents (attributes, ribbons, forms, views, relationships). The resulting managed export references those subcomponents AS EXTERNAL UNMANAGED CUSTOMIZATIONS in the source env's "Active" (unmanaged) layer — producing 105 self-referencing "leaky" deps. On top of that, 135 refs to legitimate D365 first-party solutions (msdynce_Activities, EnvironmentVariables, PowerAI, PowerAppsChecker, BaseCustomControlsCore, etc.) are unavoidable but expected — they're pre-installed on Production-tier envs.

Diagnostic breakdown of the 240 missing deps on the pre-fix export:
- **Category A (D365 first-party)**: ~135 refs to 25 well-known first-party solutions. Expected pre-installed on any Production-tier env.
- **Category B (leaky self-refs)**: 105 refs to SpaarkeMaster's own subcomponents citing `solution="Active"`. Only produced by improper build. Fixable.

**Fix applied**: `scripts/Build-SpaarkeMaster.ps1` line 138 changed `AddRequiredComponents = $false` → `$true`. Rebuilt in spaarkedev1 (485 components, up from prior 386 — proof that ~99 additional subcomponents were auto-included). Re-exported managed .zip and re-counted:

| Metric | Before F12 | After F12 |
|---|---|---|
| Total MissingDependency | 240 | **77** (−163, −68%) |
| Category B (leaky self-refs) | 105 | **0** ✅ |
| Category A (D365 first-party) | ~135 | 77 |

**Category B fully eliminated**. Category A dropped from ~135 to 77 (attributes that were previously listed as separate deps are now part of their parent entity's subcomponent block).

**Automation gap**: No CI check catches the leaky-export flag. Anyone editing `Build-SpaarkeMaster.ps1` could revert this without detection until a fresh-env install fails.

**Automation TODO**:
- Add a build-verify step in `Build-SpaarkeMaster.ps1` that exports a test managed .zip, extracts `solution.xml`, counts `<MissingDependency>` entries with `solution="Active"`, and FAILS the script if any exist (Category B must always be 0)
- Add a CI check on the exported .zip (nightly or on Build-SpaarkeMaster.ps1 edit): assert `MissingDependency` count with `solution="Active"` == 0
- Add a smoke-install job that installs the managed .zip to a throwaway env once per PR that touches `Build-SpaarkeMaster.ps1`

**Reference**: `scripts/Build-SpaarkeMaster.ps1` line 138 comment cites this section; per-commit rationale in `git log` on file.

### F13: Fresh Production-tier envs do NOT auto-install Power BI Extensions (msft_PowerBI_Anchor)

**Symptom**: After F12 fix + re-export, SpaarkeMaster import to Model 1 Prod failed within 50 seconds with:
```
Some dependencies are missing. The missing dependencies are :
<MissingDependency canResolveMissingDependency="True">
  <Required type="1" schemaName="powerbimashupparameter"
            displayName="Power BI Mashup Parameter"
            solution="msft_PowerBI_Entities (1.0.0.193)">
    <package appName="Power BI" applicationName="Power BI Extensions (Preview)"
             PackageSource="" resolutionAction="Install" resolutionActionValue="Install"
             isFirstParty="False">
  </Required>
  <Dependent type="1" schemaName="environmentvariabledefinition" />
</MissingDependency>
```

Only ONE dep — Power BI Entities — was missing. The other 76 Category A first-party deps (BaseCustomControlsCore, msdyn_PowerAppsChecker, msdynce_AppCommon, msdyn_TimelineExtended, msdyn_FlowApprovalsCore, msdyn_AISolution, etc.) were all satisfied by fresh Production baseline.

**Root cause**: Power BI Extensions (application-name `msft_PowerBI_Anchor`, publisher solution `msft_PowerBI_Entities`) is `isFirstParty="False"` — it's an AppSource-installed extension, NOT part of the base D365 first-party pack. Fresh Production envs do NOT include it by default; it must be explicitly installed. spaarkedev1 has it because someone installed it at some earlier point.

Why does SpaarkeMaster need it? A `sprk_*` `environmentvariabledefinition` in SpaarkeMaster picked up a dep on `powerbimashupparameter` — likely because an env variable was authored in an env where Power BI mashup UI was open. This is a SPURIOUS dep from Dataverse's dep-tracker being overly aggressive. Runtime does not actually need Power BI. But at import time, Dataverse enforces the dep regardless.

**Fix applied (this session)**: Installed Power BI Extensions to Model 1 Prod via:
```
pac application install --environment https://spaarke-model1-prod.crm.dynamics.com \
  --application-name msft_PowerBI_Anchor
# Completed in ~6 min (polls every 30s)
```

`--skip-dependency-check` on `pac solution import` was tried first — it DOES NOT WORK for this class of dep (`ProductUpdatesOnly : False` in the error trailer means the flag only skips deps flagged as "product update", which Power BI Extensions is not).

**Automation gap**: No pre-flight check for AppSource-app prereqs on the target env before attempting SpaarkeMaster import. The `provision-environment` skill has no Step 2.5 check for this.

**Automation TODO (r1 fresh-sub Step 2.5 or H6 solution-import handler)**:
- Add a `Required Applications` manifest to the r1 handler-catalog (config-driven list of AppSource apps that must be present on any Spaarke target env). Initial list: `msft_PowerBI_Anchor` (Power BI Extensions).
- Add a pre-import check that calls `pac application list --environment {env}` → intersects with required-apps manifest → any missing → call `pac application install` in a loop. Wait for each install to complete before proceeding (poll status).
- Longer-term (belongs to a follow-on): identify the specific SpaarkeMaster env variable(s) that carry the spurious Power BI dep and fix them at the source (either remove the dep or verify it's actually needed). If removed, F13 goes away.

### F14: Fresh Production-tier envs default `maxuploadfilesize` to 5 MB, blocking large PCF web resources

**Symptom**: After F13 fix (Power BI installed) + import retry, SpaarkeMaster import to Model 1 Prod failed 5 minutes in with:
```
Import Solution Failed: CustomControl with name Spaarke.Controls.UniversalDocumentUpload
failed to import with error: Webresource content size is too big.
```

Only ONE PCF (`UniversalDocumentUpload`) failed — all other 5 PCFs (RecordHeader, MatterHeader, DatasetGrid, etc.) imported fine within the 5 MB limit.

**Root cause**: Fresh Production-tier envs have `organization.maxuploadfilesize = 5,242,880` bytes (5 MB) — the platform default. UniversalDocumentUpload's compiled bundle exceeds this. spaarkedev1 has this setting raised to `25,600,000` (25 MB) — likely by a past manual `pac org update-settings` or Portal admin action. Model 1 Prod inherited only the default.

Comparison via `pac org fetch` on both envs:
```
=== spaarkedev1 (reference) ===
name        maxuploadfilesize  organizationid
spaarkedev1 25,600,000         0c3e6ad9-...

=== spaarke-model1-prod (target) ===
name                maxuploadfilesize  organizationid
spaarke-model1-prod 5,242,880          e9aa604f-...
```

**Fix applied (this session)**:
```
pac org update-settings --name maxuploadfilesize --value 25600000
# Verified: setting now reports "25,600,000"
```
NOTE: setting name is lowercase `maxuploadfilesize` (not PascalCase); `pac org update-settings` uses the curated env-settings alias table.

**Automation gap**: No preflight check for org-level `maxuploadfilesize` before attempting solution import.

**Automation TODO (r1 fresh-sub Step 2.5 or H6 solution-import handler)**:
- Add an `Org Settings Contract` to the r1 handler-catalog (config-driven map of `settingName → minValue` that MUST be applied to any Spaarke target env). Initial contract:
  - `maxuploadfilesize`: 25_600_000 (25 MB) — required by UniversalDocumentUpload PCF and likely other large webresources
- Add a pre-import check that calls `pac org list-settings` → compares against contract → any drift → auto-apply via `pac org update-settings`. This is idempotent and safe to run every provision.
- Consider adding to the ADR-039 canonical config catalog if any other Spaarke component depends on env-level settings.
- Longer-term: investigate whether UniversalDocumentUpload can be tree-shaken or split. But 25 MB org-setting is trivial to apply per-env and unblocks the current install — priority is E2E flow.

**Combined F13 + F14 automation footprint**: TWO pre-import checks + TWO auto-remediations. Both idempotent (safe to re-run). Both fast (single API call each). Together they eliminate the two silent-fail traps between "F12-clean managed export" and "fresh env accepts import."

### F15: Fresh per-tenant Key Vault denies data-plane access to subscription Owner (RBAC gap)

**Symptom**: After Model 1 Prod Bicep deploy, attempted to list secrets in per-tenant KV `sprk-trial01-prod-kv`:
```
ERROR: (Forbidden) Caller is not authorized to perform action on resource.
Action: 'Microsoft.KeyVault/vaults/secrets/readMetadata/action'
Assignment: (not found)
DecisionReason: null
Inner error: { "code": "ForbiddenByRbac" }
```
Ralph is subscription **Owner** and can read every other resource in the sub — but not KV data-plane on the newly created per-tenant KV.

**Root cause**: The Bicep template creates KVs with `enableRbacAuthorization=true` (RBAC-based access model, not legacy access policies). Azure RBAC treats KV data-plane and control-plane separately: subscription Owner grants only **control-plane** access (write settings, delete vault, etc.). To read/write secrets you need a **data-plane** role: `Key Vault Secrets Officer` (read/write) or `Key Vault Secrets User` (read-only). NO built-in role automatically covers both planes; even `Owner` grants zero secret data-plane access by default. Confirmed by the RBAC assignment query returning `(not found)`.

**Fix applied (this session)**:
```bash
# 'az role assignment create' hit a MissingSubscription bug (see F15b) — used az rest fallback
az rest --method put \
  --url "https://management.azure.com/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/sprk-trial01-prod-kv/providers/Microsoft.Authorization/roleAssignments/{newGuid}?api-version=2022-04-01" \
  --body '{"properties":{"roleDefinitionId":"/subscriptions/{sub}/providers/Microsoft.Authorization/roleDefinitions/b86a8fe4-44ce-4948-aee5-eccb2c155cd7","principalId":"{ralph-oid}","principalType":"User"}}'
# b86a8fe4-... is the built-in Key Vault Secrets Officer role ID
```
Wait ~15s for RBAC propagation, then verify with `az keyvault secret list --vault-name ...` (should return empty [], not 403).

**F15b sub-finding**: `az role assignment create` has an apparent CLI routing bug for KV data-plane role assignments — returns `MissingSubscription` even with sub context set. `az rest --method put` to the Authorization role-assignment endpoint works reliably. Documented as a fallback in the skill.

**Automation gap**: Bicep can idempotently assign RBAC roles at deploy time (`Microsoft.Authorization/roleAssignments`), but the Bicep-time principalId is the deploy identity, not the operator. Post-deploy Owner does not get automatic secret data-plane access.

**Automation TODO (r1 Step 2.5 or H4 pre-seeding handler)**:
- Detect operator's OID from `az ad signed-in-user show`
- Detect KV RBAC mode via `az keyvault show --query properties.enableRbacAuthorization` (only run this step if TRUE)
- Grant `Key Vault Secrets Officer` scoped to the specific KV via `az rest` PUT (bypass F15b bug)
- Idempotent — Azure returns 201 on first PUT, 200 on subsequent (no error). Safe to re-run.
- Poll `az keyvault secret list` in `until` loop with ~10s intervals until it returns non-403 (RBAC prop can take 10-60s)

**Broader class**: Applies to EVERY RBAC-enabled KV created via Bicep — shared vaults + per-tenant vaults + registry vaults. The pattern: operators authorized at control-plane MUST be granted data-plane before they can seed secrets. Consider a project-wide "operator RBAC bootstrap" idempotent step run once per operator + KV pair.

### F16: Shared BFF App Service `keyVaultReferenceIdentity` set to `SystemAssigned` but only UserAssigned identity attached — KV references silently unresolvable

**Symptom**: Inventory of the shared BFF App Service `sprksharedprod-api`:
```
identity.type = "UserAssigned"    (only)
identity.userAssignedIdentities = { sprk-prod-shared-bff-uami }
keyVaultReferenceIdentity = "SystemAssigned"    (❌ mismatch)
```
Also: shared UAMI (`sprk-prod-shared-bff-uami`) has **0 role assignments** on shared KV `sprk-prod-kv`. Data-plane RBAC is empty.

App Service has 6 `@Microsoft.KeyVault(VaultName=sprk-prod-kv;SecretName=...)` references in its settings (AI Search API key, Service Bus conn str, Storage conn str, Doc Intelligence key, OpenAI key, Redis conn str). All 6 are silently unresolvable.

**Root cause**: TWO independent misconfigurations, both from the Bicep template:
1. The Bicep sets `keyVaultReferenceIdentity = 'SystemAssigned'` by default (`sites@2022-03-01` schema). But the App Service was configured with `identity.type = 'UserAssigned'` only — SystemAssigned was never enabled. So `keyVaultReferenceIdentity` points to an identity that doesn't exist.
2. The shared UAMI (attached to the App Service) has no data-plane RBAC on the shared KV. Even if kvRefIdentity were correctly set to the UAMI, KV reference resolution would still fail with 403.

Both must be fixed together for KV references to resolve. Neither is detectable via `az webapp show` — you have to specifically enumerate identities AND role assignments AND compare.

**Fix (planned — NOT yet applied)**:
```bash
# Step 1: Grant shared UAMI Key Vault Secrets User (READ-ONLY) on shared KV
# Use az rest fallback per F15b (Key Vault Secrets User role ID: 4633458b-17de-408a-b874-0445c86b69e6)
az rest --method put \
  --url "https://management.azure.com/subscriptions/{sub}/resourceGroups/rg-spaarke-shared-prod/providers/Microsoft.KeyVault/vaults/sprk-prod-kv/providers/Microsoft.Authorization/roleAssignments/{newGuid}?api-version=2022-04-01" \
  --body '{"properties":{"roleDefinitionId":".../providers/Microsoft.Authorization/roleDefinitions/4633458b-17de-408a-b874-0445c86b69e6","principalId":"{shared-uami-principal-id}","principalType":"ServicePrincipal"}}'

# Step 2: PATCH kvRefIdentity to the shared UAMI resource ID (not "SystemAssigned")
az webapp update -g rg-spaarke-shared-prod -n sprksharedprod-api \
  --set keyVaultReferenceIdentity="/subscriptions/{sub}/resourcegroups/rg-spaarke-shared-prod/providers/Microsoft.ManagedIdentity/userAssignedIdentities/sprk-prod-shared-bff-uami"

# Step 3: Restart App Service for KV reference resolution to re-run
az webapp restart -g rg-spaarke-shared-prod -n sprksharedprod-api
```

Also applies to the staging deployment slot per T1 rule (spec.md § MUST rules): both slots must be PATCHed.

**Automation gap**: Bicep should either enable SystemAssigned + wire it up, OR set `keyVaultReferenceIdentity` to the UAMI resource ID at deploy time. Currently does neither correctly for Model 1 Prod. The RBAC grant also needs to happen in Bicep (or at deploy-time PowerShell step) — currently doesn't.

**Automation TODO (r1 T1 handler + Bicep hardening)**:
- **Bicep**: When `identity.type = 'UserAssigned'`, `keyVaultReferenceIdentity` MUST be set to a specific UAMI resource ID (never `'SystemAssigned'`). Add validation in the Bicep template to reject the invalid combination.
- **Bicep**: Add `Microsoft.Authorization/roleAssignments` sub-resource on shared KV granting the shared UAMI `Key Vault Secrets User` role.
- **T1 handler**: After Bicep deploy, VERIFY the App Service `keyVaultReferenceIdentity` resolves to an attached identity + that identity has `Key Vault Secrets User` or better on all referenced KVs. Emit HARD WARN if not.
- **T1 handler**: On drift, auto-remediate (both PATCH kvRefIdentity + PUT role assignment). Idempotent.

**F16b sub-finding**: The `az webapp show --query "identity.userAssignedIdentities"` output shows only the resource ID + clientId + principalId, NOT which identity is used for `keyVaultReferenceIdentity` binding. You must cross-reference `keyVaultReferenceIdentity` (a resource ID string OR the literal `"SystemAssigned"`) against `identity.userAssignedIdentities` keys. A mismatch is a silent failure mode.

### F17: Shared BFF App Service has NEVER been deployed — root URL returns default "empty App Service" page

**Symptom**: `curl https://sprksharedprod-api.azurewebsites.net/` returns Microsoft's default "Your web app is running and waiting for your content" HTML page. `/healthz` and `/ping` both return 404.

**Root cause**: The Bicep deploy provisions the App Service resource but does NOT deploy the BFF application code. This is by design (Bicep is IaC, not CI/CD) — but the E2E "customer stand-up" workflow needs to include an explicit code-deploy step. Without it, all subsequent config work (KV references, App User bindings, /healthz verification) is testing an empty shell.

**Fix (planned — this is H9 in r1 handler catalog, not yet run this session)**:
```bash
# Prereqs: dotnet 10 SDK, git clean, on main branch
cd src/server/api/Sprk.Bff.Api
dotnet publish -c Release -o ../../../../deploy/api-publish/ --self-contained false
cd ../../../../deploy/api-publish
zip -r ../api-publish.zip .
cd ..
az webapp deploy \
  --subscription {model1-prod-sub} \
  --resource-group rg-spaarke-shared-prod \
  --name sprksharedprod-api \
  --src-path api-publish.zip \
  --type zip \
  --async false

# Verify
curl -sS https://sprksharedprod-api.azurewebsites.net/healthz
# Expected: 200 with health-check payload; degraded if KV refs unresolved (F16 must be fixed first)
```

Also constrained by NFR-01 (BFF publish size ≤60 MB compressed; current baseline 44.96 MB per r1 CLAUDE.md).

**Automation gap**: r1 handler catalog lists H9 as "BFF deploy" but no code exists yet for automation. The `deploy-new-release` skill is the reference model but was designed for shared-env deploys (spaarkedev1 → prod slot swap), not fresh Model 1 Prod App Service where there's no existing deploy to swap from.

**Automation TODO (r1 H9 handler)**:
- Fresh-env case: Detect empty App Service via `curl / | grep "default"` OR check for absence of a specific health-endpoint tag file
- Build BFF locally OR trigger CI pipeline to build + publish
- Zip-deploy to App Service with `az webapp deploy --type zip`
- Post-deploy: poll `/healthz` with backoff (App Service warm-up can take 30-90s)
- Cross-reference against F16: kvRefIdentity + RBAC must be correct BEFORE the app starts, else the app boots in a degraded state and `/healthz` may misreport

**Sequencing implication for r1 handler ordering**: Currently the handler catalog has H4 (KV seed) → H5-H7 (Dataverse solutions, roles) → H9 (BFF deploy) → H10 (App User). This session's discovery: H9 needs to happen BEFORE any /healthz-dependent testing, AND F16 remediation (T1 kvRefIdentity + shared UAMI KV RBAC) needs to happen BEFORE H9 (or BFF starts in degraded state). Proposed re-ordering:
```
[Existing] H0/H0.5/H1/H2a/H2b (infra)
[New pre-H4]  F15 (op RBAC on per-tenant KV)
[New]      F16-1 (shared UAMI Key Vault Secrets User on shared KV)
[Existing] H3 (per-tenant KV creation - already in Bicep)
[New]      F16-2 (T1: kvRefIdentity PATCH to shared UAMI, both slots)
[Existing] H4 (per-tenant KV seed - if any secrets are per-tenant; currently NONE for Model 1)
[Existing] H5/H6/H7 (Dataverse solutions - SpaarkeMaster import)
[Existing] H10 (App User - shared UAMI as sysadmin in Dataverse)
[New/re-cast] H9 (BFF deploy - fresh env: zip-deploy code, verify /healthz)
[Existing] H11+ (customer user provisioning, license assignment)
```

**Combined F15 + F16 + F17 automation footprint**: FIVE new handlers/checks needed for E2E-no-human-interaction:
- F15 (op RBAC on per-tenant KVs)
- F16-1 (shared UAMI KV RBAC on shared KV)
- F16-2 (T1 kvRefIdentity PATCH — MUST replace SystemAssigned with actual UAMI resource ID)
- F17-1 (fresh-env BFF deploy detection)
- F17-2 (BFF zip-deploy + /healthz post-deploy verification with warm-up backoff)

Plus TWO Bicep hardenings:
- Never emit `keyVaultReferenceIdentity='SystemAssigned'` when `identity.type='UserAssigned'` only
- Emit role assignments for attached UAMIs on referenced KVs

---

## Non-Blocking Observations

### O1: `az cognitiveservices model list` output shows separate "Deprecating" vs "Legacy" statuses; only "GA" or "Legacy" are deployable

- `Deprecating` = still works for EXISTING deploys, BLOCKED from new deploys (this is what tripped F1)
- `Legacy` = still works for both new + existing deploys, but Microsoft strongly prefers you upgrade
- `GenerallyAvailable` = current recommended state, longest runway
- `Deprecated` = retired (no more deploys AT ALL, existing may still work briefly)

Skill quota-check logic must accept `GA` or `Legacy`, reject `Deprecating` or `Deprecated`.

### O2: DataZoneStandard SKU exists as an alternative to GlobalStandard for gpt-5 family

- `DataZoneStandard` = data can be processed anywhere in the geopolitical zone (e.g. US)
- `GlobalStandard` = data can be processed anywhere globally (broadest)
- Compliance-sensitive customers may prefer DataZone. For MVP we picked GlobalStandard.
- Skill should surface this as an operator input for compliance-sensitive Model 1 customers.

### O3: `az deployment sub what-if` preflight is comprehensive and cheap — USE IT ALWAYS

- what-if surfaced all 5 blocking issues this session before any resource was created
- Takes ~30-60 sec for a stack of ~30 resources
- Zero cost, zero side effects
- Skill MUST run what-if before every `deployment sub create` on a fresh sub, and iterate to green before invoking create.

### O4: East US "Compute quota: Total VMs: 0" is a MISLEADING error label

- The error message says "Total VMs" (Compute language)
- Actual quota being enforced is Microsoft.Web (App Service Plan)
- Preflight error label doesn't match the actual quota namespace
- Skill error-parser must translate the misleading label into actionable operator guidance.

---

## Process Updates Recommended (r1 project delivery)

### Update 1: `.claude/skills/provision-environment/SKILL.md`

Add these to the skill's Step 0.7 (Preflight/Feasibility) — currently the skill jumps from Step 0 (Prereqs) to Step 1 (Intake) without any of these checks:

- **F1 → OpenAI pin freshness check**: fail-fast if any pinned version is Deprecating/Deprecated in the target region
- **F3 → Region auto-selection**: if primary region has App Service quota walls, fall back to WestUS2 (Spaarke canonical)
- **F4 → OpenAI region split**: if primary region lacks GA gpt-5 family, override to WestUS3 via `sharedOpenAiLocation`
- **F5 → Auto-quota deployment set composition**: query `az cognitiveservices usage list`, pick the deployment set from what's already allocated
- **F6 → Provider registration retry-verify loop**: never fire-and-forget register
- **F7 → Fresh-sub UX preamble**: warn operator about empty dropdowns
- **F8 → Auto-file support tickets** (advanced): via `az support` REST API when no auto-grant path exists
- **F9 → Support Plan availability check**: never queue a ticket-dependent action on a plan-less sub

Also update the skill's "Fallback Matrix" section (currently F1-F3) to add F4-F9 as documented recovery paths.

### Update 2: `infrastructure/bicep/stacks/model1-shared.bicep`

Already partially done (commits `798f61c9` + `f90918f8`). Remaining:

- **Optional TODO**: Parameterize `sharedOpenAiDeployments` array so operators can override without editing the stack. This would let the skill compose the deployment set as a computed input rather than requiring stack edits per environment. Not blocking; nice-to-have.

### Update 3: `projects/customer-provisioning-orchestration-r1/design.md`

Add a new subsection (probably in §4 or §11) titled **"Fresh-Sub Deployment Discoveries (2026-08-22)"**:

- Codify the WestUS2 (platform) + WestUS3 (OpenAI) region strategy as default
- Document the "auto-quota compatible MVP → full P5 after quota approval" progressive deploy pattern
- Reference this lessons-learned doc as the evidence base

### Update 4: `projects/customer-provisioning-orchestration-r1/spec.md`

Amend FR-08 (region strategy) with the WestUS2+WestUS3 default. Amend FR-14 (OpenAI capacity) to note the auto-grant vs frontier tier distinction. Amend NFR-11 (E2E automation) with the concrete gap list from this doc.

### Update 5: Root `CLAUDE.md` §17 pointer for `provision-environment`

Add a footnote: "Fresh-sub deploys require region intelligence + auto-quota detection — see `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` for the discovery arc."

### Update 6: Claude memory

Already have `reference_openai_model_pins_stale_fast.md`. Add:
- `reference_azure_new_sub_regional_gotchas.md` (F3 + F4)
- `reference_azure_fresh_sub_openai_tier_gates.md` (F5)

---

## For the "Customer Deployment Web App" Vision (owner-directed follow-on)

Owner requested: *"the best solution is we have a 'Customer Deployment' web app that allows the user to input whatever information/setting choices and Claude Code / the scripts run everything."*

This is the natural evolution of the L3 `/provision-environment` skill. The web app is the presentation layer; the skill + Bicep + PS1 scripts are the execution layer. To get there, the layers need to reach "E2E no human interaction" first — which is exactly what the r1 delivery aims for.

### Proposed follow-on project: `customer-deployment-webapp-r1`

**Scope**:
- React/PCF UI: form-based intake (customer name, tenancy model, region, contact, budget) validating against the same rules the skill applies
- Backend: BFF endpoint that queues a provisioning job (Cosmos state machine already exists in L2 control-plane)
- The provisioning job runs the r1 handler chain (H0..H14) end-to-end
- Progress UI: real-time SSE stream of handler status
- Completion UI: env URL, sign-in flow, first-user creation

**Prerequisites (must land in r1 first)**:
- Every F1-F9 gap absorbed into the skill / scripts (this document is the driver)
- Handlers H0-H14 all live-validated (currently: H1, H4, H5, H6, H7, H8, H10, H11, H12b live; H0.5, H3, H9, H12a, H12c, H13, H14a still pending live proof — per session's rolling status)
- L2 control-plane REST API surfaces `/api/runs` endpoint with SSE progress stream
- Auth: web app operator identity flows to L2 via OBO (Ralph's identity OR future admin identity)

**Design tensions to resolve**:
- Where does the web app live? Spaarke tenant only, or per-Model-1-shared-env-instance?
- Who can operate it? Spaarke ops only for MVP; later: prospect self-service via marketing?
- What's the failure story? When Microsoft auto-approver denies quota, does the web app queue a support ticket automatically or block for operator?

**Timing**: after r1 delivery completes. r1 delivery target has always been the automation layer; the web app is the UX layer on top.

**Filed as**: `projects/customer-deployment-webapp-r1` skeleton (to be created after r1 wraps).

---

## Session Timeline (for E2E replication timing docs)

| Time | Event |
|---|---|
| T+0 | Owner creates Spaarke Model 1 Production sub + assigns Owner |
| T+~15 min | Iteration 1: preflight fails on eastus quota + deprecated model pins → commit `798f61c9` |
| T+~40 min | Iteration 2: preflight fails on westus2 gpt-5 absence + gpt-5.4 quota → commit `f90918f8` |
| T+~45 min | Preflight passes cleanly (westus2 platform + westus3 OpenAI + auto-quota model set) |
| T+~47 min | `az deployment sub create` kicked off in background |
| T+? | Deploy completion (pending as of doc write) |
| T+? | pac admin create Dataverse env |
| T+? | Solution import |
| T+? | BFF config wire-up |
| T+? | Verify + sign-in test |

**Session total elapsed** (deploy phase only): ~1 hour from sub creation to deploy kickoff (with fix-at-discovery iterations). Future runs (with skill absorbing all F1-F9 fixes): projected ~10-15 min to deploy kickoff — because operator never sees any of the walls.

---

*Written 2026-08-22 during live deploy. Author: Claude (Opus 4.7) with Ralph Schroeder as owner.*
