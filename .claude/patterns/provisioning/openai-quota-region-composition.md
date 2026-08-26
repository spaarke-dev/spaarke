# OpenAI Quota + Region Composition Pattern

> **Last Reviewed**: 2026-08-25
> **Reviewed By**: customer-provisioning-orchestration-r1 task 203a per punch list row A07
> **Status**: Content filled (task 203a). Was skeleton from task 202.

## When

Load this pattern when:
- Composing OpenAI deployments (model set + region + SKU) for a fresh Azure subscription.
- Reviewing a PR that adds/updates OpenAI model pins in Bicep.
- Debugging `ServiceModelDeprecated` at Bicep deploy time (model pin aged out).
- Debugging `InsufficientQuota` at deployment time on a fresh sub (auto-granted TPM was 0 for that tier).
- Adding a new OpenAI model to the stack (e.g., a new fine-tuned variant or a Compliance-region deployment).

## Read These Files (canonical source)

1. `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` § F1 / F2 / F4 / F5 / L-o1 / L-o2 — regional gotchas + tier gates + support-ticket workflow.
2. `docs/guides/PROVISIONING-PREREQUISITES.md` PRQ-C-01 (TPM headroom for pinned frontier models) + PRQ-C-02 (model GA per region for pinned versions) + PRQ-S-02 (Support Plan required for quota-bump tickets).
3. `infrastructure/bicep/stacks/model1-shared.bicep` — the `sharedOpenAiDeployments` param + `sharedOpenAiLocation` param (per F4 fix; westus2 platform + westus3 OpenAI).
4. `infrastructure/bicep/modules/openai.bicep` — the module that emits `Microsoft.CognitiveServices/accounts/deployments` resources.
5. **User memory** `reference_azure_fresh_sub_openai_tier_gates.md` — fresh subs auto-grant mini/embedding tiers generously but ZERO for frontier tiers.
6. **User memory** `reference_azure_fresh_sub_regional_gotchas.md` — canonical Spaarke strategy: `westus2` platform + `westus3` OpenAI.
7. **User memory** `reference_openai_model_pins_stale_fast.md` — always `az cognitiveservices model list` before greenfield deploy.

## Constraints

- Frontier models (gpt-5.x family) require **GlobalStandard** SKU. `Standard` SKU rejected with `InvalidResourceProperties`. `DataZoneStandard` exists as a compliance-region alternative — surface as operator input when the customer is compliance-sensitive.
- Fresh subs auto-grant mini/embedding tiers generously (500+ TPM) but ZERO TPM for frontier tiers. Frontier requires a support ticket (~8-24h SLA) via `az support tickets create` — REQUIRES `PRQ-S-02` (Support Plan) present. If absent, HARD STOP at Step 0.5.
- Model status vocabulary — only `GenerallyAvailable` and `Legacy` accept new deploys. `Deprecating` blocks new deploys even though existing deploys still work. `Deprecated` rejects all.
- Region availability varies: `westus2` has NO gpt-5 family; `westus3` has full family GA. Region composition MUST be per-model, not per-platform.
- Model pins age fast (~4-6 month deprecation cycle). ALWAYS run `az cognitiveservices model list` before a greenfield Bicep deploy or preflight rejects with `ServiceModelDeprecated`.

## Key Rules (walk this for every OpenAI deployment composition)

1. **Step 2.5 preflight per profile** — for each pinned model, verify GA status in the target region:
   ```
   az cognitiveservices model list --location {region} \
     --query "[?kind=='OpenAI' && model.name=='{modelName}' && model.version=='{pinnedVer}'].model.lifecycleStatus"
   ```
   Require `GenerallyAvailable` or `Legacy` for every pinned model. If `Deprecating` or `Deprecated` → HARD STOP with model-pin update remediation.
2. **Step 2.5 TPM check per model**:
   ```
   az cognitiveservices usage list --location {region} \
     --query "[?contains(name.value,'{skuGroup}') && contains(name.value,'{modelFamily}')].{limit:limit, current:currentValue}"
   ```
   Require `limit >= current + expected_load` (expected_load per manifest). If insufficient → auto-file quota-bump ticket (see rule 4).
3. **Region composition** (Model 1 canonical): platform resources in `westus2`, OpenAI in `westus3`. `sharedOpenAiLocation` Bicep param overrides primary location.
4. **Quota-bump flow** — if PRQ-S-02 (Support Plan) present AND skill `--batch autoAdvance: true`: auto-file quota-bump ticket via `az support tickets create` (~8-24h SLA). If Support Plan absent OR interactive mode: HARD STOP at Step 0.5 with remediation (operator files ticket via Azure Portal).
5. **MVP deployment set fallback**: if frontier-model quota unavailable and can't be bumped in time, use only auto-allocated resources (mini/embedding). BFF has documented fallback behavior — quality degrades but SC #5 (env Ready) still achievable.
6. **DataZoneStandard for compliance-sensitive customers**: expose `--sku-compliance-region {us|eu|apac}` as operator input; deployment uses `DataZoneStandard` SKU + region-scoped model routing.

## Anti-patterns this catches

- ❌ Deploying with a `Deprecating` model pin because "it still works" → deploy succeeds but Microsoft's block on new deploys for the pin is imminent; runbook is now stale.
- ❌ Assuming fresh subs have SOME frontier TPM by default → they don't. Frontier is 0 until you file a support ticket. Deploying without checking will surface `InsufficientQuota` at deploy time.
- ❌ Deploying gpt-5 family in `westus2` → region has no GA for that family. Deploy fails with `LocationNotAvailableForResourceType` or similar.
- ❌ Using `Standard` SKU for frontier tier → rejected with `InvalidResourceProperties`. Frontier requires GlobalStandard (or DataZoneStandard).
- ❌ Skipping the model-pin freshness check → Bicep deploy fails with `ServiceModelDeprecated` after months of the pin working fine.

## Recovery recipes

- **`ServiceModelDeprecated` at deploy time**: update the model pin in `bicepparam` to the latest GA version per `az cognitiveservices model list`. Re-run.
- **`InsufficientQuota` on a fresh sub for gpt-5.x**: file quota-bump ticket via `az support tickets create` (requires PRQ-S-02); wait ~8-24h; re-run deploy. In parallel, deploy the MVP set (mini/embedding auto-granted) so the env is at least partially usable.
- **`LocationNotAvailableForResourceType`**: model isn't GA in that region. Move OpenAI deployment to a region with GA for the pinned family (typically `westus3` for gpt-5.x). Keep platform resources in their primary region.
- **`InvalidResourceProperties` on SKU**: switch from `Standard` to `GlobalStandard` (or `DataZoneStandard` for compliance).

## Worked example — Step 2.5 preflight for gpt-5 pinned model

Suppose the profile pins `gpt-5.4` at version `2025-11-15` in `westus3`. Step 2.5 preflight:

```powershell
$region = "westus3"
$modelName = "gpt-5.4"
$pinnedVer = "2025-11-15"
$expectedTpm = 500  # per manifest

# 1. Model GA check
$modelInfo = az cognitiveservices model list --location $region -o json | ConvertFrom-Json |
  Where-Object { $_.kind -eq "OpenAI" -and $_.model.name -eq $modelName -and $_.model.version -eq $pinnedVer }

if (-not $modelInfo) {
  Write-Error "❌ Model '$modelName' version '$pinnedVer' NOT LISTED in $region. Check ADR-020 pin freshness."
  exit 1
}
$status = $modelInfo.model.lifecycleStatus
if ($status -notin @("GenerallyAvailable", "Legacy")) {
  Write-Error "❌ Model status is '$status' — only 'GenerallyAvailable' and 'Legacy' accept new deploys. Update pin per ADR-020."
  exit 1
}

# 2. TPM check
$skuGroup = "GlobalStandard"
$modelFamily = "gpt-5"
$usage = az cognitiveservices usage list --location $region -o json | ConvertFrom-Json |
  Where-Object { $_.name.value -match "$skuGroup.*$modelFamily" }

$limit = ($usage | Measure-Object -Property limit -Sum).Sum
$current = ($usage | Measure-Object -Property currentValue -Sum).Sum
$headroom = $limit - $current

if ($headroom -lt $expectedTpm) {
  Write-Host "⚠️ Insufficient TPM headroom: need $expectedTpm, have $headroom." -ForegroundColor Yellow

  # 3. Auto-file quota-bump if Support Plan + autoAdvance
  $supportPlan = Test-SupportPlanPresent -SubscriptionId $sub  # PRQ-S-02 check
  if ($supportPlan -and $autoAdvance) {
    $ticket = az support tickets create `
      --ticket-name "sprk-prov-quota-bump-$($runId)" `
      --description "Auto-filed: $modelName $skuGroup TPM bump from $limit to $($current + $expectedTpm + 500) in $region for Spaarke customer-provisioning run $runId" `
      --severity minimal `
      --contact-first-name Ops --contact-last-name Bot `
      --contact-primary-email-address ops@spaarke.com `
      --contact-preferred-communication-channel email `
      --contact-preferred-time-zone "Pacific Standard Time"
    Write-Host "✅ Auto-filed quota-bump ticket: $($ticket.name); waiting up to 24h."
  } else {
    Write-Error "❌ Insufficient TPM + no Support Plan (PRQ-S-02) OR interactive mode. Operator must file ticket via Azure Portal."
    exit 1
  }
}
```

Region composition example (Model 1 canonical, per F4):

```bicep
// stacks/model1-shared.bicep
param location string = 'westus2'                   // platform default
param sharedOpenAiLocation string = 'westus3'       // OpenAI in westus3 for gpt-5 family GA

module openai 'modules/openai.bicep' = {
  name: 'openai'
  params: {
    location: sharedOpenAiLocation  // OVERRIDES the platform default
    deployments: sharedOpenAiDeployments
  }
}
```

## Cross-refs

- Related prereq: PRQ-C-01, PRQ-C-02, PRQ-S-02 in `docs/guides/PROVISIONING-PREREQUISITES.md`
- Related Bicep module: `infrastructure/bicep/modules/openai.bicep`
- Related user memory: `reference_azure_fresh_sub_openai_tier_gates.md`, `reference_openai_model_pins_stale_fast.md`, `reference_azure_fresh_sub_regional_gotchas.md`
- Related ADR: ADR-020 (AI model version pinning)
