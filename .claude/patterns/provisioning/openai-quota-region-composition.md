# OpenAI Quota + Region Composition Pattern

> **Last Reviewed**: 2026-08-24
> **Reviewed By**: customer-provisioning-orchestration-r1 task 202 (SKELETON — task 203 fills)
> **Status**: Skeleton

## When
Composing OpenAI deployments (model set + region + SKU) for a fresh sub. Model pins age fast (~4-6 month deprecation), regions have varying model availability, fresh subs have varying auto-granted TPM.

## Read These Files (task 203 fills)
1. `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` § F1 / F2 / F4 / F5 / L-o1 / L-o2 — regional gotchas + tier gates.
2. `docs/guides/PROVISIONING-PREREQUISITES.md` PRQ-C-01 (TPM headroom for pinned frontier models) + PRQ-C-02 (model GA per region for pinned versions).
3. `infrastructure/bicep/stacks/model1-shared.bicep` — the `sharedOpenAiDeployments` param + `sharedOpenAiLocation` param (per F4 fix).
4. **User memory** `reference_azure_fresh_sub_openai_tier_gates.md` — canonical Spaarke strategy: `westus2` platform + `westus3` OpenAI.
5. **User memory** `reference_openai_model_pins_stale_fast.md` — always `az cognitiveservices model list` before greenfield deploy.

## Constraints
- Frontier models (gpt-5.x family) require **GlobalStandard** SKU. Standard SKU rejected with `InvalidResourceProperties`.
- Fresh subs auto-grant mini/embedding tiers generously (500+ TPM) but ZERO TPM for frontier tiers.
- Model status vocabulary: only `GenerallyAvailable` and `Legacy` accept new deploys. `Deprecating` blocks new deploys even though existing deploys still work. `Deprecated` rejects all.
- Region availability varies: `westus2` has NO gpt-5 family; `westus3` has full family GA.
- DataZoneStandard SKU exists as compliance-region alternative to GlobalStandard (surface as operator input for compliance-sensitive customers).

## Key Rules (task 203 fills detail)
1. Step 2.5 preflight per profile: `az cognitiveservices model list --location {region} --query "[?kind=='OpenAI' && model.version=='{pinnedVer}'].model.lifecycleStatus"` → require `GenerallyAvailable` or `Legacy` for every pinned model.
2. Step 2.5 TPM check per model: `az cognitiveservices usage list --location {region} --query "[?contains(name.value,'{skuGroup}') && contains(name.value,'{modelFamily}')].{limit:limit, current:currentValue}"` → require `limit >= current + expected_load`.
3. Region composition (Model 1 canonical): platform resources in `westus2`, OpenAI in `westus3`. Bicep `sharedOpenAiLocation` param overrides primary location.
4. If PRQ-S-02 (Support Plan) present + `--batch autoAdvance: true`: auto-file quota-bump ticket via `az support tickets create` (~8-24h SLA). If Support Plan absent: HARD STOP at Step 0.5 with remediation.
5. Deployment set for MVP: use only auto-allocated resources (mini/embedding) if frontier-model quota unavailable. BFF fallback documented.
