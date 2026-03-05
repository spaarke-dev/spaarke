# Azure OpenAI Model Inventory

> **Resource**: `spaarke-openai-dev`
> **Resource Group**: `spe-infrastructure-westus2`
> **Verified**: 2026-03-04 via `az cognitiveservices account deployment list`

---

## Currently Deployed Models

| Deployment Name | Model | Version | SKU | Capacity (TPM) | Created |
|----------------|-------|---------|-----|-----------------|---------|
| `gpt-4o-mini` | gpt-4o-mini | 2024-07-18 | Standard | 50K TPM | 2025-12-09 |
| `text-embedding-3-small` | text-embedding-3-small | 1 | Standard | 120K TPM | 2025-12-15 |
| `text-embedding-3-large` | text-embedding-3-large | 1 | Standard | 120K TPM | 2026-01-11 |

---

## Models Referenced in Code (But NOT Deployed)

### gpt-4o (MISSING - HIGH PRIORITY)

Used extensively across the codebase as the primary capable/quality model:

| Location | Usage |
|----------|-------|
| `ModelSelector.cs` | Default model + ScopeGenerationModel (`"gpt-4o"`) |
| `BuilderAgentService.cs` | `DefaultModel = "gpt-4o"` |
| `AiPlaybookBuilderService.cs` | Production mode model (`TestMode.Production => "gpt-4o"`) |
| `GenericAnalysisHandler.cs` | Analysis operations (`ModelName = "gpt-4o"`) |
| `FinanceOptions.cs` | Invoice extraction deployment (`ExtractionDeploymentName = "gpt-4o"`) |
| `ModelEndpoints.cs` (stub) | Listed as available model (`ModelId = "gpt-4o"`) |
| `KNW-BUILDER-003-node-schema.json` | Playbook node model selection enum |
| `KNW-BUILDER-004-best-practices.json` | Best practice guidance for model selection |
| `appsettings.template.json` | `AzureOpenAI.ChatModelName` = `#{AI_CHAT_MODEL_NAME}#` (likely resolves to gpt-4o) |

**Impact if missing**: Scope generation, playbook building, analysis, and finance extraction will fail at runtime with deployment-not-found errors.

### o1-mini (MISSING - MEDIUM PRIORITY)

Used for complex reasoning and multi-step planning:

| Location | Usage |
|----------|-------|
| `ModelSelector.cs` | `PlanGenerationModel = "o1-mini"` |
| `KNW-BUILDER-003-node-schema.json` | Listed in model enum for playbook nodes |
| `Program.cs` | Referenced in DI comments for reasoning operations |

**Impact if missing**: Plan generation in the Playbook Builder will fail. Fallback: could temporarily use gpt-4o.

### gpt-4-turbo (LOW PRIORITY)

| Location | Usage |
|----------|-------|
| `ModelEndpoints.cs` (stub) | Listed as available model (`ModelId = "gpt-4-turbo"`, stub data only) |

**Impact if missing**: Only referenced in stub data for the model deployments endpoint. No runtime code uses this deployment name directly. Low priority.

---

## Deployed But Potentially Under-Provisioned

### gpt-4o-mini (50K TPM)

This is the workhorse model used for:
- Document summarization (`SummarizeModel` default)
- Intent classification (`IntentClassificationModel`)
- Entity resolution (`EntityResolutionModel`)
- Validation operations (`ValidationModel`)
- Explanation generation (`ExplanationModel`)

50K TPM may be insufficient under load with 5+ concurrent operations. Consider increasing to 100K-200K TPM for production.

---

## Commands to Deploy Missing Models

### Deploy gpt-4o (HIGH PRIORITY)

```bash
az cognitiveservices account deployment create \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  --deployment-name gpt-4o \
  --model-name gpt-4o \
  --model-version "2024-11-20" \
  --model-format OpenAI \
  --sku-capacity 80 \
  --sku-name Standard
```

### Deploy o1-mini (MEDIUM PRIORITY)

```bash
az cognitiveservices account deployment create \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  --deployment-name o1-mini \
  --model-name o1-mini \
  --model-version "2024-09-12" \
  --model-format OpenAI \
  --sku-capacity 50 \
  --sku-name Standard
```

> **Note**: o1-mini availability varies by region. Check [Azure OpenAI model availability](https://learn.microsoft.com/en-us/azure/ai-services/openai/concepts/models) for westus2 support. If unavailable, update `ModelSelectorOptions.PlanGenerationModel` to `"gpt-4o"` as a fallback.

---

## Recommended TPM Quotas

| Deployment | Current TPM | Recommended TPM | Rationale |
|-----------|-------------|-----------------|-----------|
| `gpt-4o-mini` | 50K | 150K | High-frequency model (summarization, classification, validation) |
| `gpt-4o` | Not deployed | 80K | Quality model (generation, analysis, extraction) |
| `o1-mini` | Not deployed | 50K | Reasoning model (plan generation, lower frequency) |
| `text-embedding-3-large` | 120K | 120K | Adequate for current embedding workloads |
| `text-embedding-3-small` | 120K | 120K | Legacy; migration to large complete, keep for backward compat |

### Update TPM for gpt-4o-mini

```bash
az cognitiveservices account deployment create \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  --deployment-name gpt-4o-mini \
  --model-name gpt-4o-mini \
  --model-version "2024-07-18" \
  --model-format OpenAI \
  --sku-capacity 150 \
  --sku-name Standard
```

---

## Configuration Token Mapping

| Token | Default Value | Where Set |
|-------|--------------|-----------|
| `#{AI_SUMMARIZE_MODEL}#` | `gpt-4o-mini` | `appsettings.tokens.md` |
| `#{AI_CHAT_MODEL_NAME}#` | (not documented, likely `gpt-4o`) | `appsettings.template.json` > `AzureOpenAI.ChatModelName` |
| `#{AI_EMBEDDING_MODEL}#` | `text-embedding-3-large` | `DocumentIntelligenceOptions.EmbeddingModel` |

---

## Summary

| Status | Model | Deployment Name | Action Required |
|--------|-------|----------------|-----------------|
| DEPLOYED | gpt-4o-mini | `gpt-4o-mini` | Increase TPM quota |
| DEPLOYED | text-embedding-3-small | `text-embedding-3-small` | None (legacy, kept for compat) |
| DEPLOYED | text-embedding-3-large | `text-embedding-3-large` | None |
| MISSING | gpt-4o | `gpt-4o` | Deploy immediately - blocks multiple features |
| MISSING | o1-mini | `o1-mini` | Deploy or configure fallback to gpt-4o |
| STUB ONLY | gpt-4-turbo | `gpt-4-turbo` | No action needed (stub data only) |
