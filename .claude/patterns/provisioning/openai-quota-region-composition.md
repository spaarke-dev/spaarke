# OpenAI Quota + Region Composition Pattern

> **Last Reviewed**: 2026-10-06
> **Reviewed By**: customer-provisioning-orchestration-r1 task 247 (rewritten: one fixed deployment set, no recompose, no support ticket)
> **Status**: Current. Supersedes the task 203a content (gpt-5 frontier tiers, support-ticket quota bumps, "MVP deployment set fallback") — none of that is how stamps deploy.

## When

Load this pattern when:
- Changing a stamp's OpenAI deployments (model, version, SKU, capacity) or its OpenAI region.
- Debugging `ServiceModelDeprecated` / `InsufficientQuota` at deploy time, or H0 failing `openai-pin-freshness` / `azure-openai-tpm-headroom`.
- Reviewing a PR that touches `openai.bicep`, `PinnedModelCatalog.cs` or the H0 OpenAI probes.

## Read These Files (canonical source)

1. `infrastructure/bicep/modules/openai.bicep` — `deployments` default: the stamp set and its rationale comment.
2. `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/RuntimeReferences/PinnedModelCatalog.cs` — the C# mirror (deployments, Azure quota names, `DefaultOpenAiLocation`). Read by H0 (both probes) and H12c.
3. `.../Handlers/Preflight/ArmCognitiveServicesTpmProbe.cs` (quota per quota name; `ResolveOpenAiRegion`) and `ArmOpenAiPinFreshnessProbe.cs` (lifecycle status per pinned model).
4. `infrastructure/bicep/customer.bicep` — `param openAiLocation` (default `westus3`); passes no `deployments`, so the module default is what ARM creates.
5. `scripts/preflight/Test-AzureOpenAiTpmHeadroom.ps1` — operator-run equivalent of the quota probe.
6. Forcing test: `ArmTemplateInspectorTests.PinnedModelCatalog_MatchesTheCompiledCustomerTemplate`.

## Key Rules

1. **The set is fixed — never recompose.** The BFF calls deployments by NAME (`gpt-4o`, `gpt-4o-mini`, `text-embedding-3-large`); dropping or renaming one deploys a stamp that fails at runtime. A quota or availability gap fails H0 before deploy.
2. **Change `openai.bicep` and `PinnedModelCatalog.cs` together**, recompile `customer.json`; the forcing test fails otherwise. Update the PowerShell default too.
3. **Deployment name ≠ model.** `gpt-4o-mini` runs gpt-4.1-mini (gpt-4o-mini 2024-07-18 is Deprecating). Keep names; change models behind them.
4. **SKU DataZoneStandard** (owner 2026-10-06: US data zone). Fresh subs auto-grant it; Standard gpt-4o is 0.
5. **Quota names are Azure's spelling**, matched whole: e.g. `OpenAI.DataZoneStandard.gpt4.1-mini` (no hyphen after "gpt").
6. **Region**: OpenAI in `openAiLocation` (westus3), not the primary region — westus2 offers no OpenAI models. H0 probes read `openAiLocation`, else westus3.
7. **Pins**: only `GenerallyAvailable` / `Legacy` accept new deploys. Check `az cognitiveservices model list --location <openAiLocation> --subscription <id>` before changing one. Current pins retire 2027-04-14; H0 blocks 90 days earlier — refresh before ~2027-01-14.
8. **No support case** (owner). Pick a set that fits auto-grants.

## Cross-refs

- ADR-020 (model version pinning); `docs/architecture/SPAARKE-ENVIRONMENT-RESOURCE-INVENTORY.md` (OpenAI row)
- User memory: `reference_openai_model_pins_stale_fast.md`, `reference_azure_fresh_sub_openai_tier_gates.md`, `reference_azure_fresh_sub_regional_gotchas.md`
