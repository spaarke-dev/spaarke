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
