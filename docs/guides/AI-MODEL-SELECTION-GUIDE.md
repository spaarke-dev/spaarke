# AI Model Selection Guide

> **Last Updated**: 2026-03-05
>
> **Audience**: Playbook authors, platform engineers, and developers configuring AI analysis nodes
>
> **Related Files**:
> - `src/server/api/Sprk.Bff.Api/Services/Ai/ModelSelector.cs` — ModelSelector service and OperationType enum
> - `projects/ai-spaarke-platform-enhancments-r3/notes/azure-openai-model-inventory.md` — Deployed model inventory
> - `docs/architecture/ai-implementation-reference.md` — Overall AI architecture

---

## Overview

The Spaarke AI platform uses **tiered model selection** to balance cost, latency, and output quality across different operation types. Instead of routing all AI calls through a single model, the `ModelSelector` service maps each operation category to the model best suited for it.

This guide explains:
- What models are available and their deployment status
- Which operation type maps to which model and why
- How the resolution chain works (node override → ModelSelector → default)
- How playbook authors can override the default model for a specific node

---

## Available Model Deployments

The following models are deployed to Azure OpenAI resource `spaarke-openai-dev` (resource group: `spe-infrastructure-westus2`).

| Deployment Name | Model | Status | Capacity | Primary Use |
|----------------|-------|--------|----------|-------------|
| `gpt-4o-mini` | gpt-4o-mini 2024-07-18 | **Deployed** (50K TPM) | Standard | Classification, validation, entity resolution, explanation, summarization, tool handlers |
| `text-embedding-3-large` | text-embedding-3-large | **Deployed** (120K TPM) | Standard | Document and knowledge embeddings (primary) |
| `text-embedding-3-small` | text-embedding-3-small | **Deployed** (120K TPM) | Standard | Legacy embeddings (kept for backward compatibility) |
| `gpt-4o` | gpt-4o 2024-11-20 | **NOT YET DEPLOYED** | — | Scope content generation, analysis operations, default fallback |
| `o1-mini` | o1-mini 2024-09-12 | **NOT YET DEPLOYED** | — | Multi-step plan generation |

> **Important**: `gpt-4o` and `o1-mini` are referenced throughout the codebase as the intended models for quality generation and reasoning operations respectively, but are not yet deployed in the dev environment. Operations requiring these models will fail at runtime until they are deployed. See the [model inventory notes](../../projects/ai-spaarke-platform-enhancments-r3/notes/azure-openai-model-inventory.md) for deployment commands.

### Embedding Models

Embedding models are not part of the ModelSelector. They are configured separately via `DocumentIntelligence.EmbeddingModel` (token: `#{AI_EMBEDDING_MODEL}#`), which defaults to `text-embedding-3-large`.

---

## Operation Types and Default Model Assignments

The `OperationType` enum (defined in `ModelSelector.cs`) categorizes every AI call made by the Playbook Builder. Each type has a default model optimized for its cost/quality profile.

| Operation Type | Default Model | Rationale |
|----------------|---------------|-----------|
| `IntentClassification` | `gpt-4o-mini` | User intent from natural language is a structured classification task. Fast and cheap model is sufficient; structured output format eliminates need for reasoning depth. |
| `EntityResolution` | `gpt-4o-mini` | Resolving playbook entities (nodes, scopes) from user input is pattern-matching work. Low complexity; gpt-4o-mini handles this reliably at minimal cost. |
| `Validation` | `gpt-4o-mini` | Pre-execution canvas state checks produce simple boolean-style outputs. Speed and cost matter; reasoning quality does not add value here. |
| `Explanation` | `gpt-4o-mini` | User-facing explanations of operations or decisions are short, conversational outputs. gpt-4o-mini generates acceptable quality at low latency. |
| `ToolHandlerModel` | `gpt-4o-mini` | Built-in tool handlers (document summary, clause analysis, entity extraction) perform focused extraction and classification. Fast model covers these well. |
| `ScopeGeneration` | `gpt-4o` | Generating Action prompts, Skill fragments, and other scope content requires high-quality long-form text generation. The capable model (gpt-4o) produces significantly better output here. |
| `PlanGeneration` | `o1-mini` | Generating multi-step execution plans involves complex conditional reasoning across many possible node sequences. The reasoning model (o1-mini) is purpose-built for this. |
| _(unknown type)_ | `gpt-4o` | The default fallback for any unrecognized operation type is the quality model, erring on the side of capability over cost. |

### Cost and Latency Profile Summary

| Model | Relative Cost | Relative Latency | Best For |
|-------|--------------|-----------------|----------|
| `gpt-4o-mini` | Low | Fast (~500ms) | High-frequency classification, short structured outputs |
| `gpt-4o` | Medium | Medium (~2-5s) | Long-form generation, complex extraction, general analysis |
| `o1-mini` | Higher | Slower (~5-15s) | Multi-step reasoning, plan generation |

---

## Resolution Chain

When a Playbook Builder operation runs, the model is determined by the following priority chain:

```
1. Node-level override (aiAnalysis node data.model field)
        |
        v  (if not set)
2. ModelSelector (OperationType → ModelSelectorOptions → configured model)
        |
        v  (if operation type unknown)
3. DefaultModel (ModelSelectorOptions.DefaultModel = "gpt-4o")
```

### Step 1: Node-Level Override

Individual `aiAnalysis` nodes on the playbook canvas can specify a `model` field in their `data` configuration. This is stored in the node schema (KNW-BUILDER-003) as:

```json
{
  "model": {
    "type": "string",
    "enum": ["gpt-4o", "gpt-4o-mini", "o1-mini"],
    "default": "gpt-4o",
    "description": "AI model to use for analysis"
  }
}
```

When a node specifies a model, that selection takes precedence over the ModelSelector for that node's execution. This is the primary override mechanism for playbook authors.

### Step 2: ModelSelector

If no node-level override is set, the `ModelSelector` service (`IModelSelector`) selects a model based on the `OperationType`. The mapping is driven by `ModelSelectorOptions`, which reads from the `ModelSelector` configuration section in `appsettings`:

```json
{
  "ModelSelector": {
    "IntentClassificationModel": "gpt-4o-mini",
    "EntityResolutionModel": "gpt-4o-mini",
    "PlanGenerationModel": "o1-mini",
    "ScopeGenerationModel": "gpt-4o",
    "ValidationModel": "gpt-4o-mini",
    "ExplanationModel": "gpt-4o-mini",
    "DefaultModel": "gpt-4o",
    "ToolHandlerModel": "gpt-4o-mini"
  }
}
```

All values above are the compiled-in defaults from `ModelSelectorOptions`. They can be overridden per-environment without code changes by setting the corresponding key in `appsettings.json` or environment-specific configuration.

### Step 3: Default Model

If an unrecognized `OperationType` is passed (e.g., future additions not yet in the switch expression), the `ModelSelector` falls back to `ModelSelectorOptions.DefaultModel`, which is `gpt-4o`.

---

## Override Instructions for Playbook Authors

### Override Model at the Node Level

To use a specific model for an individual analysis node, set the `model` field when configuring the `aiAnalysis` node:

1. Open the Playbook Builder canvas.
2. Select or add an `aiAnalysis` node.
3. In the node configuration panel, locate the **AI Model** field.
4. Select one of the available options: `gpt-4o`, `gpt-4o-mini`, or `o1-mini`.

This overrides the system default for that node only. Other nodes in the same playbook are unaffected.

**When to use each model at the node level:**

| Choose | When the node is... |
|--------|---------------------|
| `gpt-4o-mini` | Extracting a small number of named fields from a document; classifying document type; performing a yes/no validation check; generating a brief summary |
| `gpt-4o` | Writing a detailed legal analysis; generating contract clause language; performing multi-criteria risk scoring; summarizing complex financial data |
| `o1-mini` | Designing a multi-step reasoning plan; evaluating conditional logic across many document sections; deriving conclusions from conflicting evidence |

> **Note**: If `gpt-4o` or `o1-mini` is selected but not yet deployed, the operation will fail with a deployment-not-found error. In the current dev environment only `gpt-4o-mini` is deployed for chat. See deployment status in the table above.

### Override Model System-Wide via Configuration

To change the default model for an entire operation type across all playbooks (without code deployment), update the `ModelSelector` section in the environment's `appsettings.json` or App Service configuration:

```json
{
  "ModelSelector": {
    "ScopeGenerationModel": "gpt-4o-mini"
  }
}
```

This is useful for:
- **Cost reduction during development**: Temporarily route all operations to `gpt-4o-mini` while `gpt-4o` is not deployed.
- **o1-mini fallback**: Set `PlanGenerationModel` to `"gpt-4o"` if `o1-mini` is unavailable in the region.

> Changing system-wide defaults affects all tenants and all playbooks. Node-level overrides continue to take precedence over changed defaults.

---

## GenericAnalysisHandler Model Usage

The `GenericAnalysisHandler` (the engine behind custom Tool scopes defined in Dataverse) currently uses `ModelSelectorOptions.DefaultModel` (`gpt-4o`) for metadata reporting, and `IOpenAiClient` for the actual AI call. The client is wired to the deployment configured in `AzureOpenAI.ChatModelName` (token: `#{AI_CHAT_MODEL_NAME}#`).

Supported operations in the generic handler:

| Operation | Description | Recommended Model |
|-----------|-------------|-------------------|
| `extract` | Extract structured data from a document | `gpt-4o-mini` (simple fields) or `gpt-4o` (complex schemas) |
| `classify` | Classify document or content into categories | `gpt-4o-mini` |
| `validate` | Validate content against rules | `gpt-4o-mini` |
| `generate` | Generate content based on input | `gpt-4o` |
| `transform` | Transform content format | `gpt-4o-mini` or `gpt-4o` depending on complexity |
| `analyze` | General analysis | `gpt-4o` |

---

## Deployment Status and Immediate Actions

| Priority | Action | Impact if Not Done |
|----------|--------|--------------------|
| HIGH | Deploy `gpt-4o` | Scope generation, analysis operations, and default fallback fail at runtime |
| HIGH | Increase `gpt-4o-mini` TPM from 50K to 150K | Rate limiting under concurrent load with 5+ users |
| MEDIUM | Deploy `o1-mini` (or set fallback) | Plan generation fails; workaround: set `ModelSelector:PlanGenerationModel` to `gpt-4o` |

### Deploy gpt-4o

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

### Deploy o1-mini (or configure fallback)

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

If o1-mini is unavailable in westus2, use this configuration workaround instead:

```json
{
  "ModelSelector": {
    "PlanGenerationModel": "gpt-4o"
  }
}
```

---

## Related Documentation

- [AI Implementation Reference](../architecture/ai-implementation-reference.md) — Overall AI system design and tool framework
- [AI Deployment Guide](./AI-DEPLOYMENT-GUIDE.md) — Deploying Azure OpenAI models and configuring the BFF API
- [Playbook Scope Configuration Guide](./PLAYBOOK-SCOPE-CONFIGURATION-GUIDE.md) — Creating Action, Skill, and Knowledge scopes for playbooks
- [RAG Architecture](./RAG-ARCHITECTURE.md) — Embedding model usage and retrieval-augmented generation
