---
name: azure-openai-reasoning-models-2026-07
description: Azure OpenAI reasoning-tier model catalog + deployment constraints as of 2026-07 (GPT-5 family, o3/o4-mini) for the NDA-r1 ReasoningModel config key
metadata:
  type: reference
---

# Azure OpenAI reasoning models (mid-2026 snapshot)

For `ai-advanced-capabilities-nda-r1` task 013 (`DocumentIntelligence:ReasoningModel` config key), NDA legal-advisory whole-doc review.

**Catalog (all GA, "no access request needed" unless noted):** reasoning tier = **GPT-5 family** (gpt-5, gpt-5-mini, gpt-5-nano @ ver `2025-08-07`) plus successors gpt-5.1 (2025-11-13), gpt-5.2, gpt-5.4, gpt-5.5, gpt-5.6-{sol,terra,luna} (2026-07-09). Legacy o-series still deployable: o3 (2025-04-16), o3-mini (2025-01-31), o4-mini (2025-04-16), o1, o3-pro. gpt-5-pro/gpt-5-codex exist. `reasoning_effort`: none/minimal/low/medium/high/xhigh — `minimal` ONLY on original gpt-5 (dropped in 5.1+); `xhigh` only 5.6/5.5/5.4/5.1-codex-max; `max` only 5.6.

**Structured Outputs / JSON schema: ✅ supported across entire GPT-5 series** (matches BFF `GetStructuredCompletionRawAsync`). Also `verbosity` param (new), `custom_tool`/`lark_tool`.

**MS guidance (model-choice-guide) explicitly lists "Legal or financial document analysis" as a GPT-5 use case.** GPT-5=reasoning/higher-latency/higher-TTFT; GPT-4.1=fast non-reasoning. Recommend GPT-5 @ medium effort for interactive advisory (high effort for hardest multihop but slowest).

**Region constraint (CRITICAL):** Azure OpenAI Global Standard + Data Zone Standard Americas tables list **westus3 ✅ (prod region) with full gpt-5 support** but **westus2 is NOT a column at all** — dev resource `spaarke-openai-dev` is in West US 2. Verify with `az cognitiveservices account list-models`. GlobalStandard routes globally so may still deploy, but region tables don't show westus2. SKUs: GlobalStandard + DataZoneStandard(US) both carry gpt-5 in westus3.

**Deploy values (top rec):** model `gpt-5`, version `2025-08-07`, sku `GlobalStandard`. Fallback: `gpt-5-mini` / `2025-08-07`. Quota depends on tier (Tier 5/6 have default quota; others may need quota request).

**Sources:** learn.microsoft.com/azure/foundry/openai/how-to/reasoning ; /foundry-models/how-to/model-choice-guide ; /foundry-models/concepts/models-sold-directly-by-azure-region-availability?pivots=standard (all updated 2026-07).

**Open questions:** Does West US 2 actually block deployment or is it just table omission (GlobalStandard is region-agnostic for inference)? Should NDA-r1 point ReasoningModel at prod westus3 resource instead of dev westus2? gpt-5.2+ may edge out gpt-5 on quality at similar cost — worth a bake-off.
