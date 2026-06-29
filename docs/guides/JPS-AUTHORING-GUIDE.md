# JPS Authoring Guide

> **Version**: 4.0 — node-first dispatch model (R7 spaarke-ai-platform-unification-r7, Wave 6 task 065, FR-30)
> **Date**: 2026-06-28 (R7 rewrite); 2026-04-05 (last full v3 review)
> **Status**: Production
> **Author**: Spaarke Engineering
> **Audience**: Developers, AI engineers, prompt authors, solution architects
> **Related**: [AI Architecture Overview](../architecture/AI-ARCHITECTURE.md), [Playbook Runtime](../architecture/ai-architecture-playbook-runtime.md), [Actions/Nodes/Scopes Boundary](../architecture/ai-architecture-actions-nodes-scopes.md), [Playbook Deploy Recipe](ai-guide-playbook-deploy-recipe.md), [PLAYBOOK-AUTHOR-GUIDE](PLAYBOOK-AUTHOR-GUIDE.md)
>
> **Scope of this guide**: authoring contract for the JPS (JSON Prompt Schema) DSL — section grammars (`instruction`, `input`, `output`, `scopes`, `examples`, `metadata`), feature deep dives (template parameters, `$ref`, `$choices`, override merge, structured output), the node-first authoring flow + worked examples, best practices, validation, troubleshooting.

---

## Why this guide was rewritten (R7, 2026-06-28)

Prior to R7, dispatch — the decision of which executor runs a node — was split across three storage layers: a structural fallback ladder in `PlaybookOrchestrationService`, an `Action.sprk_actiontypeid` lookup chain, and per-node `configJson.__actionType` injection. Authors had to reason about all three layers simultaneously, and the v3 guide taught Action-type-first authoring as if Action drove dispatch. **R7 unified dispatch to a single column: `sprk_playbooknode.sprk_executortype`**. The runtime now reads one Choice value on the node row to decide which executor runs; the Action FK becomes the **prompt template carrier** for the prompt-driven executors only (AiAnalysis, AiCompletion, AiEmbedding) — not a dispatch source. This guide reflects the unified model. See spec FR-07 (single-hop dispatch), FR-12 (AiCompletion executor), FR-30 (this guide rewrite), and CLAUDE.md §10 (BFF Hygiene).

---

## Table of Contents

1. [What is JPS?](#1-what-is-jps)
2. [JPS Pipeline Architecture (post-R7 single-hop dispatch)](#2-jps-pipeline-architecture)
3. [JPS JSON Structure](#3-jps-json-structure)
4. [Section Reference](#4-section-reference)
   - [instruction](#instruction-section)
   - [input](#input-section)
   - [output](#output-section)
   - [scopes](#scopes-section)
   - [examples](#examples-section)
   - [metadata](#metadata-section)
5. [Features Deep Dive](#5-features-deep-dive)
   - [Template Parameters](#template-parameters)
   - [$ref Resolution](#ref-resolution)
   - [$choices Resolution](#choices-resolution)
   - [Override Merge](#override-merge)
   - [Structured Output](#structured-output)
6. [The Node-First Authoring Flow](#6-the-node-first-authoring-flow)
   - [Step 1 — Pick the Executor Type](#step-1--pick-the-executor-type-drives-dispatch)
   - [Step 2 — Choose or Author an Action (prompt-driven executors only)](#step-2--choose-or-author-an-action-prompt-driven-executors-only)
   - [Step 3 — Configure the Node](#step-3--configure-the-node)
   - [Step 4 — Test + Deploy via Deploy-Playbook.ps1](#step-4--test--deploy-via-deploy-playbookps1)
7. [Worked Examples (node-first)](#7-worked-examples-node-first)
   - [Example A — AiCompletion node (prompt-driven, no tools)](#example-a--aicompletion-node-prompt-driven-no-tools)
   - [Example B — Condition node (pure executor, no Action FK)](#example-b--condition-node-pure-executor-no-action-fk)
   - [Example C — AiAnalysis node (prompt-driven, with tools + document context)](#example-c--aianalysis-node-prompt-driven-with-tools--document-context)
8. [Playbook Design Patterns](#8-playbook-design-patterns)
9. [JPS Schema Examples (full files)](#9-jps-schema-examples-full-files)
10. [Best Practices](#10-best-practices)
11. [Validation Checklist](#11-validation-checklist)
12. [Troubleshooting](#12-troubleshooting)
13. [Designing Whole Playbooks (pointer)](#13-designing-whole-playbooks-pointer)

---

## 1. What is JPS?

### The Problem

Hardcoded prompts in C# handler classes are unmaintainable. When prompt logic lives inside `BuildExecutionPrompt()` methods, every change requires a code deployment. Prompts cannot be reused across actions, tested independently, or edited by non-developers.

### The Solution

JSON Prompt Schema (JPS) externalizes prompt logic into structured JSON stored in the `sprk_systemprompt` column of `sprk_analysisaction` (Action) Dataverse records. The BFF API detects the format at runtime and renders it into an assembled prompt for the LLM.

Under the R7 dispatch model, the Action record carries **prompt material only** (system prompt + output schema + temperature). Which executor runs the node is decided by the **node row's `sprk_executortype` Choice column** — not by any field on the Action. Authors therefore think node-first: pick the executor type, then (for prompt-driven executors) attach an Action whose JPS describes the prompt.

### Benefits

| Benefit | Description |
|---------|-------------|
| No-code editing | Prompt authors modify JSON in Dataverse — no C# deployment needed |
| Structured output | Output fields generate JSON Schema for constrained decoding |
| Scope composition | Attach shared knowledge and skills via `$ref` — maintained once, used everywhere |
| Template reuse | One Action definition serves multiple nodes via template parameters and overrides |
| Validation | Typed fields, enums, and `$choices` constrain model output at the schema level |

### When to Use JPS vs. Flat Text

JPS is the canonical format for all new Actions in R7. Flat-text `sprk_systemprompt` payloads are read-only legacy: some pre-R7 Action rows still carry them and continue to render verbatim (no schema, no override merge, no `$choices`). Author all new Actions as JPS.

| JPS gives you | Flat-text limits |
|---|---|
| Structured JSON output via `structuredOutput: true` + JSON Schema | No schema enforcement; model returns free text |
| Reuse across nodes via template parameters | One-off prompt, no per-node override |
| Shared knowledge + skill scopes via `$ref` | No scope composition |
| `$choices` to constrain outputs against Dataverse lookups or downstream nodes | No dynamic enums |
| Per-node override via `promptSchemaOverride` in `configJson` | No node-level customization without duplicating the Action |

---

## 2. JPS Pipeline Architecture

### Single-hop dispatch (R7 FR-07)

```
PlaybookOrchestrationService.ExecuteNodeAsync(node)
        |
        v
  Read node.sprk_executortype  (one Choice value, single Dataverse read)
        |
        v
  NodeExecutorRegistry.Resolve(executorType)
        |
        v
  Executor.Validate(node)  (per-executor invariants — prompt-driven executors
                            REQUIRE Action FK + JPS; pure executors do not)
        |
        v
  Executor.ExecuteAsync(node, context)
```

The orchestrator does **not** consult `Action.sprk_actiontypeid`, structural ladder helpers, or `configJson.__actionType` for dispatch. Those layers were deleted in R7 (FR-08, FR-09). Per FR-19, every `sprk_playbooknode` row MUST have `sprk_executortype` populated.

### JPS rendering pipeline (runs inside prompt-driven executors only)

For executors that consume a prompt template (`AiAnalysis`, `AiCompletion`, `AiEmbedding`), the executor invokes the JPS pipeline against the `Action.sprk_systemprompt` payload:

```
node.sprk_actionid  -->  Action.sprk_systemprompt (JPS JSON)
        |
        v
 1. Override Merge (PromptSchemaOverrideMerger)
    node.configJson.promptSchemaOverride merged into base JPS
        |
        v
 2. Template Parameter Substitution
    {{paramName}} replaced from node.configJson.templateParameters
        |
        v
 3. Scope Resolution (ScopeResolverService)
    Parallel Dataverse queries for skills, knowledge, tools
        |
        v
 4. Named $ref Resolution (JpsRefResolver --> IScopeResolverService)
    Extract knowledge:{name} and skill:{name} from scopes section
    Resolve against Dataverse by name
        |
        v
 5. $choices Resolution (LookupChoicesResolver + downstream resolver)
    output.fields[].enum populated from Dataverse lookups, option sets,
    or downstream-node display names
        |
        v
 6. PromptSchemaRenderer.Render()
    Assembles instruction + scopes + input + output + examples --> prompt text
    Generates JSON Schema for structuredOutput if enabled
        |
        v
 RenderedPrompt --> IOpenAiClient (GetStructuredCompletionRawAsync)
```

Pure executors (`Condition`, `Start`, `DeliverOutput`, `LookupUserMembership`, etc.) skip the JPS pipeline entirely — they read `node.configJson` directly per their typed config schema (see FR-16 / Wave 3).

### Key Components

| Component | File | Purpose |
|-----------|------|---------|
| `PlaybookOrchestrationService` | `Services/Ai/PlaybookOrchestrationService.cs` | Single-hop dispatch: reads `node.sprk_executortype` and delegates to the executor |
| `NodeExecutorRegistry` | `Services/Ai/Nodes/NodeExecutorRegistry.cs` | Maps `ExecutorType` enum value → `INodeExecutor` implementation |
| `INodeExecutor` | `Services/Ai/Nodes/INodeExecutor.cs` | Executor contract; declares `SupportedExecutorTypes` + `Validate` + `ExecuteAsync` + `GetConfigSchema` (FR-16) |
| `AiAnalysisNodeExecutor` | `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` | Tool-augmented analysis executor (ExecutorType = 0) |
| `AiCompletionNodeExecutor` | `Services/Ai/Nodes/AiCompletionNodeExecutor.cs` | Raw LLM completion executor (ExecutorType = 1) — closes R4 graduation gate per FR-12 |
| `PromptSchemaRenderer` | `Services/Ai/PromptSchemaRenderer.cs` | Renders JPS JSON + runtime context into assembled prompt text |
| `JpsRefResolver` | `Services/Ai/JpsRefResolver.cs` | Extracts `knowledge:{name}` and `skill:{name}` refs from scopes (static, no DI) |
| `PromptSchemaOverrideMerger` | `Services/Ai/PromptSchemaOverrideMerger.cs` | Merges node-level overrides into base schema (static, no DI) |
| `ScopeResolverService` | `Services/Ai/ScopeResolverService.cs` | Resolves skills, knowledge, and tools by ID or name from Dataverse |
| `PromptSchema` | `Services/Ai/Models/PromptSchema.cs` | C# data model for the JPS JSON structure |

All file paths are relative to `src/server/api/Sprk.Bff.Api/`.

### How the executor reaches the JPS pipeline

Each prompt-driven executor (`AiAnalysisNodeExecutor`, `AiCompletionNodeExecutor`, `AiEmbeddingNodeExecutor`) loads the Action's `sprk_systemprompt` payload, runs override merge + template substitution + scope resolution + render, and calls `IOpenAiClient`. The orchestrator does not parse or peek at JPS — that responsibility lives inside the executor.

Authors do **not** write logic that detects "is this JPS or flat text?" in client code. The renderer parses JPS by structural inspection (root object with `$schema`); JPS is the only supported prompt format in R7. The legacy flat-text fallback was removed when the structural-fallback ladder was deleted (FR-08).

---

## 3. JPS JSON Structure

A JPS definition has six top-level sections:

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": { ... },
  "input": { ... },
  "output": { ... },
  "examples": [ ... ],
  "scopes": { ... },
  "metadata": { ... }
}
```

| Section | Required | Purpose |
|---------|----------|---------|
| `$schema` | Yes | Schema identifier — triggers JPS format detection |
| `$version` | Yes | Schema version (currently `1`) |
| `instruction` | Yes | Core prompt: role, task, context, constraints |
| `input` | No | Input field definitions and placeholders |
| `output` | No | Output field definitions, types, and structured output config |
| `examples` | No | Few-shot examples (input/output pairs) |
| `scopes` | No | Named knowledge and skill references via `$ref` |
| `metadata` | Yes | Author, description, tags for discoverability |

---

## 4. Section Reference

### instruction Section

The `instruction` section defines the core prompt content. All fields accept template parameters (`{{paramName}}`).

```json
{
  "instruction": {
    "role": "You are a [description of the AI's persona and expertise].",
    "task": "Perform [specific analysis task] on the provided document.",
    "context": "This runs in the context of [workflow description]. Results are used for [purpose].",
    "constraints": [
      "Constraint 1: [specific rule the model must follow]",
      "Constraint 2: [output format requirement]",
      "Constraint 3: [boundary or limitation]"
    ]
  }
}
```

| Field | Type | Purpose |
|-------|------|---------|
| `role` | string | System persona — who the AI is and what expertise it has |
| `task` | string (required) | What the AI must do — the primary objective |
| `context` | string | Why and where this runs — workflow context for the AI |
| `constraints` | string[] | MUST/MUST NOT rules — guardrails for output quality |

**Guidelines**:
- `role` should be specific (domain expert, not generic assistant)
- `task` should be actionable and unambiguous
- `context` helps the model understand the downstream usage of its output
- `constraints` should be testable (avoid vague guidance)

### input Section

Defines expected input fields and their constraints:

```json
{
  "input": {
    "document": {
      "required": true,
      "maxLength": 100000,
      "placeholder": "{{document.extractedText}}"
    },
    "priorOutputs": [
      { "variable": "profile", "fields": ["documentType"], "description": "Output from upstream Profiler node" }
    ]
  }
}
```

| Field | Type | Purpose |
|-------|------|---------|
| `document.required` | boolean | Whether document text must be provided |
| `document.maxLength` | number | Maximum character length (for truncation guidance) |
| `document.placeholder` | string | Template variable that the pipeline replaces at runtime |
| `priorOutputs` | array | Upstream node output dependencies (variable, fields, description) |
| `parameters` | object | Additional key-value parameters with template support |

The `document` field is the standard input for document analysis actions. The extracted text is populated by the pipeline before rendering.

### output Section

Defines the expected output fields. When `structuredOutput` is `true`, the renderer generates a JSON Schema that the OpenAI client uses for structured output mode.

```json
{
  "output": {
    "fields": [
      {
        "name": "sprk_fieldname",
        "type": "string",
        "description": "What this field should contain and format expectations.",
        "maxLength": 5000
      },
      {
        "name": "sprk_category",
        "type": "string",
        "description": "Classification category.",
        "enum": ["category_a", "category_b", "category_c"]
      },
      {
        "name": "sprk_routingdecision",
        "type": "string",
        "description": "Which downstream action to route to.",
        "enum": ["$choices"]
      },
      {
        "name": "sprk_mattertype",
        "type": "string",
        "description": "The matter type constrained to Dataverse values.",
        "$choices": "lookup:sprk_mattertype_ref.sprk_mattertypename"
      }
    ],
    "structuredOutput": true
  }
}
```

**Output field properties**:

| Property | Type | Purpose |
|----------|------|---------|
| `name` | string | Dataverse column name the result maps to |
| `type` | string | `"string"`, `"number"`, `"boolean"`, `"array"`, `"object"` |
| `description` | string | What the model should generate (becomes schema description) |
| `maxLength` | number | Max characters (string fields) |
| `enum` | string[] | Allowed values — model must pick from this list |
| `$choices` | string | Dynamic enum auto-injected at render time (see [$choices Resolution](#choices-resolution) for all supported prefixes) |
| `items` | object | Schema for array items when type is `"array"` |
| `minimum` / `maximum` | number | Numeric range constraints |

When `structuredOutput` is `true`:
- The renderer generates a JSON Schema from the fields array
- The OpenAI client enables structured output mode
- The model is guaranteed to return valid JSON matching the schema
- Field descriptions become the schema descriptions

When `false` or omitted, the model returns free-form text.

### scopes Section

Scopes attach external knowledge and skills to the prompt by name. The pipeline resolves `$ref` values against Dataverse before rendering.

```json
{
  "scopes": {
    "$knowledge": [
      { "$ref": "knowledge:contract-law-basics", "as": "reference" },
      { "$ref": "knowledge:jurisdiction-rules", "as": "definitions" }
    ],
    "$skills": [
      { "$ref": "skill:entity-extraction" }
    ]
  }
}
```

| Field | Type | Purpose |
|-------|------|---------|
| `$knowledge` | array | Knowledge references with `$ref` and optional `as` label |
| `$skills` | array | Skill references with `$ref` |

**Reference format**:

| Prefix | Resolves To | Dataverse Entity |
|--------|-------------|------------------|
| `knowledge:{name}` | Knowledge record content | `sprk_analysisknowledge` |
| `skill:{name}` | Skill record content | `sprk_analysisskill` |

The `{name}` portion matches the record's name field in Dataverse. The `JpsRefResolver` extracts these references and `ScopeResolverService` resolves them to full record content. The `as` label on knowledge refs controls the rendered section heading (e.g., `"reference"`, `"definitions"`).

**When to use scopes**:
- The action needs domain knowledge that is maintained separately (e.g., legal rules, industry standards)
- The action relies on reusable skill definitions shared across multiple actions
- You want to avoid duplicating knowledge across JPS definitions

### examples Section

Few-shot examples teach the model the expected output format and quality:

```json
{
  "examples": [
    {
      "input": "[Representative input text — use placeholder content, not production data]",
      "output": {
        "sprk_fieldname": "[Expected output for this field]",
        "sprk_category": "category_a"
      }
    }
  ]
}
```

**Guidelines**:
- Include 1-3 examples for complex output formats
- Examples should cover edge cases (short documents, ambiguous input)
- Output should exactly match the field names in `output.fields`
- Keep example input concise but representative

### metadata Section

Metadata supports discoverability, auditing, and tooling:

```json
{
  "metadata": {
    "author": "migration",
    "authorLevel": 0,
    "createdAt": "2026-03-04T00:00:00Z",
    "description": "Brief description of what this action does and when it runs.",
    "tags": ["document-profile", "classification", "auto-trigger"]
  }
}
```

| Field | Type | Purpose |
|-------|------|---------|
| `author` | string | Creator (`"migration"`, username, `"builder-agent"`) |
| `authorLevel` | number | `0` = migration, `1` = admin, `2` = user, `3` = AI agent |
| `createdAt` | string | ISO 8601 timestamp |
| `description` | string | Human-readable purpose description for the Playbook Builder UI |
| `tags` | string[] | Categorization tags for filtering and discovery |

---

## 5. Features Deep Dive

### Template Parameters

Any string field in `instruction` supports `{{paramName}}` placeholders. Parameters are supplied at runtime via the playbook node's `ConfigJson.templateParameters`:

```json
// JPS definition (in sprk_aiaction.sprk_systemprompt)
{
  "instruction": {
    "task": "Analyze this document under {{jurisdiction}} law with {{analysisDepth}} depth."
  }
}

// Playbook node ConfigJson
{
  "templateParameters": {
    "jurisdiction": "Delaware",
    "analysisDepth": "comprehensive"
  }
}
```

**Behavior**:
- Matched parameters are replaced with their string value
- Unmatched `{{paramName}}` placeholders remain as-is (no error)
- Parameters are applied after override merge but before rendering

**Use cases**:
- Jurisdiction-specific analysis (same action, different legal context)
- Configurable output depth or focus areas
- Customer-specific terminology or standards

### $ref Resolution

Scopes reference external knowledge and skills by name. The pipeline resolves them against Dataverse at render time:

| Prefix | Resolves To | Dataverse Entity | Resolver Method |
|--------|-------------|------------------|-----------------|
| `knowledge:{name}` | Knowledge record content | `sprk_analysisknowledge` | `GetKnowledgeByNameAsync()` |
| `skill:{name}` | Skill record content | `sprk_analysisskill` | `GetSkillByNameAsync()` |

`JpsRefResolver` extracts references statically (no DI). `AiAnalysisNodeExecutor` then resolves each name via `IScopeResolverService`. The `as` label on knowledge refs controls the rendered section heading.

### $choices Resolution

The `$choices` property on output fields auto-injects valid enum values at render time. This constrains the AI model to return only values that exist in Dataverse, eliminating fuzzy matching on the frontend.

**Supported prefixes**:

| Prefix | Format | Resolution Source | Example |
|--------|--------|-------------------|---------|
| `lookup:` | `"lookup:{entity}.{field}"` | Active records from a Dataverse reference entity | `"lookup:sprk_mattertype_ref.sprk_mattertypename"` → `["Patent", "Trademark", "Copyright"]` |
| `optionset:` | `"optionset:{entity}.{attribute}"` | Single-select choice/picklist metadata | `"optionset:sprk_matter.sprk_matterstatus"` → `["Active", "Closed", "Pending"]` |
| `multiselect:` | `"multiselect:{entity}.{attribute}"` | Multi-select picklist metadata | `"multiselect:sprk_matter.sprk_jurisdictions"` → `["US", "EU", "UK", "CA"]` |
| `boolean:` | `"boolean:{entity}.{attribute}"` | Two-option boolean field labels | `"boolean:sprk_matter.sprk_isconfidential"` → `["Yes", "No"]` |
| `downstream:` | `"downstream:{outputVar}.{field}"` | Downstream UpdateRecord node field mapping options | `"downstream:update_doc.sprk_documenttype"` → `["contract", "invoice", "proposal"]` |

**Dataverse prefixes** (`lookup:`, `optionset:`, `multiselect:`, `boolean:`) are pre-resolved by `LookupChoicesResolver` before rendering. The resolver queries Dataverse via `IScopeResolverService` and passes results through `ToolExecutionContext.PreResolvedLookupChoices`.

**Downstream prefix** (`downstream:`) is resolved inline by the renderer from `DownstreamNodeInfo[]`.

**Example — Lookup-constrained field**:

```json
{
  "name": "matterTypeName",
  "type": "string",
  "description": "The matter type that best matches the document content",
  "$choices": "lookup:sprk_mattertype_ref.sprk_mattertypename"
}
```

At render time, the `$choices` reference is resolved and injected as `"enum": ["Patent", "Trademark", "Copyright", ...]` in the JSON Schema, forcing the AI to pick an exact Dataverse value.

**Legacy inline format**: Fields with `"enum": ["$choices"]` are still supported for backward compatibility with downstream routing patterns. At render time, `"$choices"` is replaced with display names from connected downstream nodes.

### Override Merge

Playbook nodes can customize a shared Action's JPS definition without duplicating it. Add `promptSchemaOverride` to the node's `ConfigJson`:

```json
// Playbook node ConfigJson
{
  "promptSchemaOverride": {
    "instruction": {
      "constraints": [
        "$clear",
        "Only analyze sections related to intellectual property",
        "Limit output to 500 words"
      ]
    },
    "output": {
      "fields": [
        {
          "name": "sprk_ipsummary",
          "type": "string",
          "description": "Summary focused on IP provisions.",
          "maxLength": 2000
        }
      ]
    }
  }
}
```

`PromptSchemaOverrideMerger` merges a node's `promptSchemaOverride` into the base Action's JPS definition:

| Section | Default Behavior | `$clear` / `__replace` Directive |
|---------|------------------|-----------------------|
| `instruction.role` | Override replaces base | N/A (scalar) |
| `instruction.task` | Override replaces base | N/A (scalar) |
| `instruction.context` | Override replaces base | N/A (scalar) |
| `instruction.constraints` | Override appends to base | `"$clear"` or `"__replace"` as first element replaces entirely |
| `output.fields` | Override fields replace base fields by matching `name`. New fields are appended | Field named `"__replace"` replaces entirely |
| `input` | Override replaces base | N/A (full replacement) |
| `scopes` | Override replaces base | N/A (full replacement) |
| `examples` | Override appends to base | No `__replace` support |
| `metadata` | Override replaces base | N/A (full replacement) |

Override merge happens before template parameter substitution and before rendering.

### Structured Output

When `output.structuredOutput` is `true`:
1. The renderer generates a JSON Schema from the `output.fields` array
2. Field names, types, descriptions, enums, and constraints map to JSON Schema properties
3. The `RenderedPrompt` carries the schema as `JsonSchema` and `SchemaName`
4. The OpenAI client enables structured output mode (constrained decoding)
5. The model is guaranteed to return valid JSON matching the schema

---

## 6. The Node-First Authoring Flow

Authoring a playbook node in R7 is a **four-step decision sequence**. The order matters: each step narrows what is valid for the next.

### Step 1 — Pick the Executor Type (drives dispatch)

The first decision is **which executor will run this node?** That choice is recorded in `node.sprk_executortype` and is the single dispatch identity per FR-07. There are 33 executor types as of R7, organized into clusters:

| Cluster | Range | Executors | When to pick |
|---|---|---|---|
| Generic AI | `0–2` | `AiAnalysis` (0), `AiCompletion` (1), `AiEmbedding` (2) | Prompt-driven LLM work. Pick `AiAnalysis` if tools or document context are needed; `AiCompletion` for pure prompt → structured output; `AiEmbedding` to generate vectors. |
| Business logic | `10–12` | `RuleEngine`, `Calculation`, `DataTransform` | Deterministic non-LLM compute. |
| Side-effect actions | `20–24` | `CreateTask`, `SendEmail`, `UpdateRecord`, `CallWebhook`, `SendTeamsMessage` | Outbound writes to Dataverse / Graph / external systems. |
| Control flow | `30–33` | `Condition`, `Parallel`, `Wait`, `Start` | Branching, fan-out, gates, canvas anchors. |
| Delivery | `40–42` | `DeliverOutput`, `DeliverToIndex`, `DeliverComposite` | Terminal nodes that emit the playbook result. |
| Notification + query | `50–52` | `CreateNotification`, `QueryDataverse`, `LookupUserMembership` | In-app messaging + Dataverse reads + membership resolution. |
| Workflow primitives | `60, 70, 80, 90` | `AgentService`, `GroundingVerify`, `LiveFact`, `IndexRetrieve` | Foundry agent calls, citation verification, deterministic facts, Insights retrieval. |
| Insights synthesis | `100–120` | `EvidenceSufficiency`, `DeclineToFind`, `ReturnInsightArtifact` | Evidence-gated Insights playbook nodes. |
| Post-LLM scrub | `130–143` | `Sanitization`, `ObservationEmit`, `EntityNameValidator`, `LoadKnowledge`, `ReturnResponse` | Sanitizers, validators, and R4 control-flow executors. |

For the authoritative list with descriptions, see the `ExecutorType` enum at `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` (the executor XML doc on each enum value is the canonical 1-line description) and the rewritten decision tree in [`docs/architecture/ai-architecture-actions-nodes-scopes.md`](../architecture/ai-architecture-actions-nodes-scopes.md) §4.

**Implication of this step**: the executor's `Validate(node)` method now sets the contract for the remaining steps. Prompt-driven executors REQUIRE an Action FK; pure executors REJECT one (or ignore it). The Playbook Builder UI shows the right config affordances based on `node.sprk_executortype`.

### Step 2 — Choose or Author an Action (prompt-driven executors only)

If you picked a prompt-driven executor in Step 1 (`AiAnalysis`, `AiCompletion`, `AiEmbedding`), set `node.sprk_actionid` to point at the `sprk_analysisaction` row that carries the prompt template. The Action row owns:

- `sprk_systemprompt` — the JPS JSON (or a flat-text prompt for legacy actions that haven't been converted)
- `sprk_outputschemajson` — the JSON Schema for the LLM's structured output (REQUIRED for `AiCompletion`)
- `sprk_temperature` — the LLM temperature override

The Action does **not** carry dispatch information. The legacy `sprk_actiontypeid` FK was dropped in R7 (FR-03/FR-04); the maker-facing `sprk_analysisactiontype` lookup table is preserved as decorative metadata only (FR-05).

**If you pick a pure executor** (e.g., `Condition`, `Start`, `DeliverOutput`, `LookupUserMembership`), skip this step. Leave `node.sprk_actionid` null — the executor's `Validate` will reject an unexpected Action FK on pure executors (or simply ignore it).

To author a new Action JPS, follow the section-by-section schema reference in [§4 Section Reference](#4-section-reference) and [§5 Features Deep Dive](#5-features-deep-dive). The `jps-action-create` skill automates the seed step:

```powershell
# Invoke via Claude Code
/jps-action-create
```

### Step 3 — Configure the Node

Per FR-16 (Wave 3), every executor now declares a typed `ExecutorConfigSchema` via `INodeExecutor.GetConfigSchema()`. That schema is the contract for `node.sprk_configjson`. The schema enumerates the fields the executor will read at runtime — required vs optional, types, descriptions, defaults, enum values.

The Playbook Builder UI in R7 Wave 8 (in flight) renders the right input controls for an executor based on its declared schema. While building manually, consult the executor's source for the schema. Common fields by executor family:

| Executor family | Common `configJson` fields |
|---|---|
| `AiAnalysis` (0) | `templateParameters`, `promptSchemaOverride`, `knowledgeRetrieval`, `includeDocumentContext`, `parentEntityType`, `parentEntityId` |
| `AiCompletion` (1) | `templateParameters`, `promptSchemaOverride` |
| `Condition` (30) | `condition` (required), `trueBranch`, `falseBranch` |
| `EntityNameValidator` (141) | `candidateText` (required), `allowList` (required) |
| `CreateNotification` (50) | `title` (required), `body` (required), `recipient`, `category`, `priority`, `toastType`, `actionUrl`, `dueDate`, + 8 enrichment fields |

The full schema for each executor lives on the executor class. There is **no** `__actionType` injection — that name-detection hack is removed in R7 (FR-08).

### Step 4 — Test + Deploy via Deploy-Playbook.ps1

The standard deploy path is the `Deploy-Playbook.ps1` script + a JSON definition file. The R7 script writes `sprk_executortype` explicitly per node (no name-detection inference). Full recipe lives at [`docs/guides/ai-guide-playbook-deploy-recipe.md`](ai-guide-playbook-deploy-recipe.md).

```powershell
# Preview deployment without writing
.\scripts\Deploy-Playbook.ps1 -DefinitionFile "my-playbook.json" -DryRun

# Deploy to current environment
.\scripts\Deploy-Playbook.ps1 -DefinitionFile "my-playbook.json"
```

The definition file's per-node `sprk_executortype` field is the source of truth — it maps 1:1 to the `node.sprk_executortype` column at runtime. For end-to-end JPS Action authoring (including the `Seed-JpsActions.ps1` flow), see the [`jps-action-create`](../../.claude/skills/jps-action-create/SKILL.md) skill.

---

## 7. Worked Examples (node-first)

### Example A — AiCompletion node (prompt-driven, no tools)

**Use case**: a "narrate-briefing" node that takes pre-computed structured data and renders it as a short narrative paragraph via raw LLM completion (no tools, no document context). Closes the R4 graduation gate per FR-12 + FR-15.

**Step 1 — Pick the executor**: `AiCompletion` (executor type `1`). Pure prompt → structured output, no tool calls.

**Step 2 — Author the Action** (`BRIEF-NARRATE-TLDR` row in `sprk_analysisaction`):

```json
// sprk_systemprompt (JPS)
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "You are a concise legal-briefing narrator.",
    "task": "Compose a single-paragraph TL;DR for the briefing using the provided structured facts.",
    "constraints": [
      "Maximum 3 sentences",
      "Plain language; no jargon",
      "Open with the most material change"
    ]
  },
  "output": {
    "structuredOutput": true,
    "fields": [
      { "name": "narrative", "type": "string", "description": "The TL;DR narrative paragraph.", "maxLength": 600 }
    ]
  },
  "metadata": {
    "author": "r7-wave-1",
    "authorLevel": 1,
    "createdAt": "2026-06-28T00:00:00Z",
    "description": "Daily-briefing TL;DR narrator (AiCompletion).",
    "tags": ["narrate", "briefing", "ai-completion"]
  }
}

// sprk_outputschemajson (carried by Action row — REQUIRED for AiCompletion)
{ "type": "object", "properties": { "narrative": { "type": "string", "maxLength": 600 } }, "required": ["narrative"], "additionalProperties": false }
```

**Step 3 — Node config** (`sprk_playbooknode` row):

```jsonc
{
  "sprk_executortype": 1,                          // AiCompletion — DRIVES DISPATCH
  "sprk_actionid": "{guid-of-BRIEF-NARRATE-TLDR}", // payload only (prompt + schema)
  "sprk_outputvariable": "tldrNarrative",
  "sprk_configjson": {
    "templateParameters": {
      "facts": "{{upstream.briefingFacts}}"
    }
  }
}
```

**Step 4 — Deploy**: `Deploy-Playbook.ps1 -DefinitionFile daily-briefing-narrate.json`. Verify via Daily Briefing widget UAT in spaarkedev1.

### Example B — Condition node (pure executor, no Action FK)

**Use case**: a routing node that picks one of two downstream branches based on whether the document is a contract.

**Step 1 — Pick the executor**: `Condition` (executor type `30`). Pure executor — no LLM, no Action FK.

**Step 2 — Choose Action**: SKIP. Pure executors do not carry a prompt template.

**Step 3 — Node config**:

```jsonc
{
  "sprk_executortype": 30,                  // Condition — DRIVES DISPATCH
  "sprk_actionid": null,                    // no Action FK for pure executors
  "sprk_outputvariable": "branchTaken",
  "sprk_configjson": {
    "condition": "{{upstream.documentType}} == 'contract'",
    "trueBranch": "contract-review-node",
    "falseBranch": "general-filing-node"
  }
}
```

**Step 4 — Deploy**: `Deploy-Playbook.ps1`. The orchestrator reads `sprk_executortype = 30`, the registry resolves `ConditionNodeExecutor`, the executor reads `condition` / `trueBranch` / `falseBranch` from `configJson` per its declared schema, evaluates the expression, and writes the branch name to `NodeOutput.OutputVariable`. No JPS pipeline is invoked.

### Example C — AiAnalysis node (prompt-driven, with tools + document context)

**Use case**: a contract-review node that runs deep clause analysis over a SharePoint Embedded document, with tool-augmented retrieval against a knowledge corpus. This is the historical primary use case preserved in R7.

**Step 1 — Pick the executor**: `AiAnalysis` (executor type `0`). Tool-augmented LLM with document context.

**Step 2 — Author/choose the Action** (`CONTRACT-REVIEW` row):

```json
// sprk_systemprompt (JPS)
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "You are a commercial-contract review specialist.",
    "task": "Extract key clauses, flag risks, and produce an executive summary.",
    "context": "Runs against an uploaded contract document. Results are written to the matter record.",
    "constraints": [
      "Cite section + page for every risk flag",
      "Use [RISK: HIGH/MEDIUM/LOW] markers consistently",
      "Limit summary to 5 bullets"
    ]
  },
  "input": {
    "document": {
      "required": true,
      "maxLength": 100000,
      "placeholder": "{{document.extractedText}}"
    }
  },
  "scopes": {
    "$knowledge": [
      { "$ref": "knowledge:red-flags-catalog", "as": "reference" }
    ],
    "$skills": [
      { "$ref": "skill:citation-extraction" },
      { "$ref": "skill:risk-flagging" }
    ]
  },
  "output": {
    "structuredOutput": true,
    "fields": [
      { "name": "summary", "type": "string", "description": "Executive summary (max 5 bullets).", "maxLength": 2000 },
      { "name": "risks", "type": "array", "description": "Risk findings.", "items": { "type": "object" } },
      { "name": "routingDecision", "type": "string", "description": "Which downstream review path.", "enum": ["$choices"] }
    ]
  },
  "metadata": { "author": "migration", "authorLevel": 0, "createdAt": "2026-03-04T00:00:00Z", "description": "Commercial contract review.", "tags": ["contract", "ai-analysis"] }
}
```

**Step 3 — Node config**:

```jsonc
{
  "sprk_executortype": 0,                          // AiAnalysis — DRIVES DISPATCH
  "sprk_actionid": "{guid-of-CONTRACT-REVIEW}",    // payload (prompt + schema + scopes)
  "sprk_outputvariable": "contractAnalysis",
  "sprk_configjson": {
    "includeDocumentContext": true,
    "parentEntityType": "sprk_matter",
    "parentEntityId": "{{trigger.matterId}}",
    "templateParameters": { "jurisdiction": "Delaware" },
    "knowledgeRetrieval": { "topK": 8 }
  }
}
```

**Step 4 — Deploy**: `Deploy-Playbook.ps1`. The orchestrator dispatches to `AiAnalysisNodeExecutor`, which loads the Action JPS, merges any `promptSchemaOverride` from configJson, substitutes `{{jurisdiction}}`, resolves scopes, runs tool calls against retrieved knowledge, and emits structured output bound to `contractAnalysis`.

---

## 8. Playbook Design Patterns

The four-step authoring flow composes into reusable patterns. The pattern is named by **which executor types its nodes use** — not by which Action is referenced.

### Simple Analysis (Single AiAnalysis Node)

A standalone classification or extraction node. One `AiAnalysis` node reads a document, produces structured output, and writes to Dataverse via a downstream `UpdateRecord` (`ExecutorType = 22`) or terminal `DeliverOutput` (`ExecutorType = 40`).

```
[Start (33)] --> [AiAnalysis (0)] --> [UpdateRecord (22)]
```

### Classification + Conditional Routing

An `AiAnalysis` classifier node emits a routing field via `$choices`, then a `Condition` executor (`ExecutorType = 30`) selects the downstream branch.

```
[Start] --> [AiAnalysis (0): classifier] --> [Condition (30)] --+--> [Contract Review path]
                                                                |--> [Invoice Processing path]
                                                                +--> [General Filing path]
```

### Multi-Document Comparison

Two parallel `AiAnalysis` nodes feed a third `AiAnalysis` or `AiCompletion` comparison node via `input.priorOutputs`.

```
[Doc A: AiAnalysis (0)] --+
                           +--> [Comparison: AiCompletion (1)] --> [DeliverOutput (40)]
[Doc B: AiAnalysis (0)] --+
```

### RAG-Augmented Analysis

An `AiAnalysis` node combines document text with retrieved knowledge scopes for domain-specific analysis. Use `scopes.$knowledge` with `$ref` for domain knowledge plus `as` labels for section organization.

### Insights Synthesis (evidence-gated)

The Insights pattern uses `IndexRetrieve` (90) to fetch artifacts, `EvidenceSufficiency` (100) to gate, `AiCompletion` (1) to synthesize, and `ReturnInsightArtifact` (120) to emit.

```
[IndexRetrieve (90)] --> [EvidenceSufficiency (100)] --+--> [AiCompletion (1): synth] --> [ReturnInsightArtifact (120)]
                                                       |
                                                       +--> [DeclineToFind (110)]
```

### Notification-On-Completion

Any analysis pattern can fork into a `CreateNotification` (`ExecutorType = 50`) terminal branch alongside the main delivery path.

### Template Parameter Reuse

One Action definition (Step 2) serves multiple use cases via `templateParameters` in each consuming node's `configJson` (Step 3):

```
Shared Action: "Analyze under {{jurisdiction}} law with {{depth}} depth"
  |
  +-- Node A (AiAnalysis, executorType=0): { templateParameters: { jurisdiction: "Delaware", depth: "comprehensive" } }
  +-- Node B (AiAnalysis, executorType=0): { templateParameters: { jurisdiction: "California", depth: "summary" } }
```

---

## 9. JPS Schema Examples (full files)

The three node-first worked examples in §7 show the per-node decisions. The full JPS Action JSON files below show the schema in detail — useful as starter templates for new Action authoring.

### Example: Simple Classification Action

A straightforward classification action with no scopes or parameters:

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "You are a document classification specialist.",
    "task": "Classify the provided document into exactly one category.",
    "constraints": [
      "Return exactly one category from the allowed values",
      "Base classification on document content, not filename or metadata",
      "If uncertain, classify as 'other' rather than guessing"
    ]
  },
  "input": {
    "document": {
      "required": true,
      "maxLength": 50000,
      "placeholder": "{{document.extractedText}}"
    }
  },
  "output": {
    "fields": [
      {
        "name": "sprk_documenttype",
        "type": "string",
        "description": "Single document type classification.",
        "enum": ["contract", "invoice", "letter", "memo", "report", "other"]
      }
    ],
    "structuredOutput": true
  },
  "metadata": {
    "author": "migration",
    "authorLevel": 0,
    "createdAt": "2026-03-04T00:00:00Z",
    "description": "Classifies documents into predefined categories.",
    "tags": ["classification", "document-type"]
  }
}
```

### Example: Complex Action with Scopes, Parameters, and Choices

An action that uses external knowledge, template parameters, and downstream routing:

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "You are a legal document analyst specializing in {{jurisdiction}} law.",
    "task": "Analyze the document for compliance issues and route to the appropriate review workflow.",
    "context": "This action runs as part of an automated intake pipeline. The routing decision determines which specialist team reviews the document next.",
    "constraints": [
      "Apply {{jurisdiction}} regulatory standards",
      "Flag all potential compliance issues with severity (high, medium, low)",
      "Routing must select exactly one downstream path"
    ]
  },
  "input": {
    "document": {
      "required": true,
      "maxLength": 100000,
      "placeholder": "{{document.extractedText}}"
    }
  },
  "scopes": {
    "$knowledge": [
      { "$ref": "knowledge:compliance-standards" },
      { "$ref": "knowledge:regulatory-updates" }
    ]
  },
  "output": {
    "fields": [
      {
        "name": "sprk_compliancesummary",
        "type": "string",
        "description": "Summary of compliance findings with severity ratings.",
        "maxLength": 5000
      },
      {
        "name": "sprk_routingdecision",
        "type": "string",
        "description": "Which review team should handle this document.",
        "enum": ["$choices"]
      }
    ],
    "structuredOutput": true
  },
  "examples": [
    {
      "input": "[Representative document excerpt for compliance analysis]",
      "output": {
        "sprk_compliancesummary": "[Compliance findings with severity ratings]",
        "sprk_routingdecision": "[Selected downstream path name]"
      }
    }
  ],
  "metadata": {
    "author": "admin",
    "authorLevel": 1,
    "createdAt": "2026-03-04T00:00:00Z",
    "description": "Compliance analysis with automated routing to specialist teams.",
    "tags": ["compliance", "routing", "legal-analysis"]
  }
}
```

### Example: Pre-Fill Action with $choices

An action designed for form pre-fill workflows with Dataverse lookup constraints:

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "You are a legal intake specialist.",
    "task": "Extract matter classification fields from the uploaded document.",
    "constraints": [
      "Return exact Dataverse values for constrained fields",
      "Omit fields rather than guess if confidence is low",
      "Confidence must be 0.0 to 1.0"
    ]
  },
  "output": {
    "structuredOutput": true,
    "fields": [
      {
        "name": "matterTypeName",
        "type": "string",
        "description": "The matter type that best matches this document",
        "$choices": "lookup:sprk_mattertype_ref.sprk_mattertypename"
      },
      {
        "name": "practiceAreaName",
        "type": "string",
        "description": "The practice area for this matter",
        "$choices": "lookup:sprk_practicearea_ref.sprk_practiceareaname"
      },
      {
        "name": "matterName",
        "type": "string",
        "description": "A concise matter name (max 10 words)",
        "maxLength": 100
      },
      {
        "name": "confidence",
        "type": "number",
        "description": "Overall extraction confidence (0.0 to 1.0)"
      }
    ]
  },
  "metadata": {
    "author": "admin",
    "authorLevel": 1,
    "createdAt": "2026-03-05T00:00:00Z",
    "description": "Pre-fill matter fields from uploaded intake documents.",
    "tags": ["pre-fill", "matter", "intake"]
  }
}
```

### Example: Existing JPS Definitions

| File | Complexity | Features Demonstrated |
|------|------------|----------------------|
| `projects/ai-json-prompt-schema-system/notes/jps-conversions/document-profiler.json` | Medium | Multi-field output, enum, examples, structuredOutput |
| `projects/ai-json-prompt-schema-system/notes/jps-conversions/clause-analyzer.json` | High | Array output with nested objects, scopes with `$ref` and `as` labels |

---

## 10. Best Practices

### Prompt Design

- **Be specific in `role`**: Domain expert personas produce better results than generic assistants
- **Make `task` actionable**: The model should know exactly what output to produce
- **Use `constraints` for guardrails**: Testable rules prevent common failure modes
- **Include examples for complex output**: Few-shot examples are the most reliable way to teach format

### Scopes and Reuse

- **Prefer scopes over inline knowledge**: Shared knowledge records are maintained once, used everywhere
- **Keep scope references minimal**: Each resolved scope adds to the prompt token count
- **Name knowledge records descriptively**: `knowledge:delaware-corporate-law` over `knowledge:law-1`

### Template Parameters

- **Use parameters for variability**: Same action, different configurations (jurisdiction, depth, focus)
- **Document expected parameters**: Include parameter names in the action's `metadata.description`
- **Provide sensible defaults**: Design the prompt to work even if parameters are not supplied

### Output Fields

- **Use `structuredOutput: true` for machine-consumed results**: Guaranteed valid JSON
- **Use `enum` for classification fields**: Constrains model output to valid values
- **Use `$choices` for routing fields**: Connects output to playbook graph structure or Dataverse lookups
- **Keep `description` precise**: The model uses field descriptions to understand what to generate

### Override Merge

- **Use overrides for node-specific customization**: Avoids duplicating entire JPS definitions
- **Use `$clear` sparingly**: Replacing all constraints removes important guardrails
- **Test merged output**: Verify the override produces the expected assembled prompt

---

## 11. Validation Checklist

Before deploying a new JPS Action + node:

**Node-level (per FR-07 / FR-19):**
- [ ] `node.sprk_executortype` is set to a non-null integer matching a value in the `sprk_playbookexecutortype` Choice set (33 values)
- [ ] For prompt-driven executors (`AiAnalysis`=0, `AiCompletion`=1, `AiEmbedding`=2): `node.sprk_actionid` is set to a valid Action row
- [ ] For pure executors (`Condition`=30, `Start`=33, `DeliverOutput`=40, etc.): `node.sprk_actionid` is null or omitted
- [ ] `node.sprk_configjson` matches the executor's declared `ExecutorConfigSchema` (FR-16) — required fields present, types correct

**Action / JPS-level:**
- [ ] `$schema` is `"https://spaarke.com/schemas/prompt/v1"` and `$version` is `1`
- [ ] `instruction` has at least `role` and `task`
- [ ] All `output.fields` have `name`, `type`, and `description`
- [ ] For `AiCompletion`: `sprk_outputschemajson` is set on the Action row (REQUIRED — executor `Validate` rejects null per FR-13)
- [ ] `enum` values are lowercase if used for Dataverse option sets
- [ ] `$ref` names match existing Dataverse records
- [ ] Template parameters have corresponding `templateParameters` in consuming nodes
- [ ] `metadata` includes `description` and `tags`
- [ ] No actual production prompt content in documentation or logs (ADR-015)

---

## 12. Troubleshooting

### Dispatch Issues (R7 single-hop)

**Symptom**: node fails with `InvalidConfiguration` citing missing executor type, OR node dispatches to the wrong executor.

| Cause | Fix |
|-------|-----|
| `node.sprk_executortype` is null (FR-19 backfill missed this node) | Set the column to the correct Choice value. Per R7, dispatch is single-hop and the orchestrator throws a clear error rather than falling back. |
| Wrong executor type value | Verify the integer matches an `ExecutorType` enum value (see `INodeExecutor.cs`). The `sprk_playbookexecutortype` Choice set is the source of truth. |
| Deploy script wrote `__actionType` instead of `sprk_executortype` | Re-deploy with the R7 `Deploy-Playbook.ps1` (Wave 5 task 055 update). The name-detection workaround was removed; the script writes `sprk_executortype` explicitly. |
| Prompt-driven executor missing Action FK | Set `node.sprk_actionid`. The executor's `Validate` rejects null Action FK for prompt-driven executors per FR-13. |
| Pure executor rejecting unexpected Action FK | Remove `node.sprk_actionid` (set to null). Pure executors (Condition, Start, etc.) do not consume a prompt template. |

### JPS Parsing Issues

**Symptom**: executor fails with JPS parse error or rendered prompt is empty.

| Cause | Fix |
|-------|-----|
| `$schema` property missing or misspelled | Verify `"$schema": "https://spaarke.com/schemas/prompt/v1"` is present |
| JSON parse failure (malformed) | Validate JSON syntax; check for trailing commas in strict parsers |
| Leading BOM or whitespace before `{` | Re-save `sprk_systemprompt` without BOM |

### Scope Resolution Failures

**Symptom**: Knowledge or skill content missing from rendered prompt.

| Cause | Fix |
|-------|-----|
| `$ref` name does not match Dataverse record `sprk_name` | Verify record exists with exact name (case-sensitive) |
| Dataverse query timeout | Check `ScopeResolverService` logs for timeout errors |
| Missing `scopes` section in JPS | Add `scopes.$knowledge` or `scopes.$skills` array |

### Template Parameter Mismatches

**Symptom**: `{{paramName}}` appears verbatim in rendered prompt.

| Cause | Fix |
|-------|-----|
| Node `ConfigJson` missing `templateParameters` | Add `templateParameters` object to the consuming node |
| Parameter name mismatch (case-sensitive) | Verify `{{jurisdiction}}` matches `"jurisdiction"` in parameters |
| Parameters applied after render (ordering bug) | Parameters substitute before rendering — check pipeline order |

### $choices Not Resolving

**Symptom**: Enum contains literal `"$choices"` or field has no enum constraint.

| Cause | Fix |
|-------|-----|
| **Dataverse prefixes** (`lookup:`, `optionset:`, `multiselect:`, `boolean:`) | |
| Entity or field name misspelled | Verify entity logical name and field name in Dataverse |
| No active records in lookup entity | Check that the reference entity has `statecode eq 0` records |
| `LookupChoicesResolver` not registered | Verify `AddScoped<LookupChoicesResolver>()` in DI |
| **Downstream prefix** (`downstream:`) | |
| No downstream nodes connected | Connect at least one downstream node in the playbook |
| Downstream nodes have no display name | Set display names on connected downstream nodes |
| `DownstreamNodeInfo[]` not passed to renderer | Verify `AiAnalysisNodeExecutor` passes downstream info |

### Override Merge Unexpected Results

**Symptom**: Override constraints appended instead of replaced (or vice versa).

| Cause | Fix |
|-------|-----|
| Missing `"$clear"` or `"__replace"` directive | Add `"$clear"` as first element in constraints array to replace |
| Directive not exact string match | Must be exactly `"$clear"` or `"__replace"` (case-sensitive, no whitespace) |
| Override has empty `task` field | Empty task is treated as "not provided" — base task is kept |

### Structured Output Validation Errors

**Symptom**: OpenAI rejects the JSON Schema or returns malformed output.

| Cause | Fix |
|-------|-----|
| Unsupported `type` value | Use `"string"`, `"number"`, `"boolean"`, `"array"`, or `"object"` |
| Array field missing `items` schema | Add `items` with type definition for array fields |
| `enum` values contain `"$choices"` unreplaced | Verify `$choices` resolution ran (see above) |

---

## Related Resources

| Document | Purpose |
|----------|---------|
| [PLAYBOOK-AUTHOR-GUIDE.md](PLAYBOOK-AUTHOR-GUIDE.md) | Sibling guide for authoring whole playbooks (graph + nodes + edges) with the node-first model |
| [ai-guide-playbook-deploy-recipe.md](ai-guide-playbook-deploy-recipe.md) | `Deploy-Playbook.ps1` definition file format + 12-step deploy procedure |
| [AI Architecture](../architecture/AI-ARCHITECTURE.md) | Full platform architecture |
| [Actions/Nodes/Scopes Boundary](../architecture/ai-architecture-actions-nodes-scopes.md) | Four-Home decision tree + ExecutorType allocation policy |
| [Playbook Runtime](../architecture/ai-architecture-playbook-runtime.md) | Single-hop dispatch, executor catalog, runtime contract |
| [Scope Configuration Guide](SCOPE-CONFIGURATION-GUIDE.md) | Creating Tools, Skills, Knowledge, and Action records in Dataverse |
| [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) §G | BFF Hygiene — binding pre-merge checklist for any BFF-touching change |
| [`.claude/skills/jps-action-create/SKILL.md`](../../.claude/skills/jps-action-create/SKILL.md) | Skill — generate a new JPS Action JSON + seed it to Dataverse |
| [`.claude/skills/jps-playbook-design/SKILL.md`](../../.claude/skills/jps-playbook-design/SKILL.md) | Skill — design + deploy a complete playbook end-to-end |
| [`.claude/skills/jps-validate/SKILL.md`](../../.claude/skills/jps-validate/SKILL.md) | Skill — validate a JPS JSON file against schema and test rendering |

### Spec FR References (R7)

| FR | Relevance |
|---|---|
| FR-07 | Single-hop dispatch on `node.sprk_executortype` — the foundation of node-first authoring |
| FR-08 / FR-09 | Removal of structural fallback ladder + Action override branch — no more dispatch via name detection |
| FR-10 | C# enum renamed `ActionType` → `ExecutorType` (and `SupportedActionTypes` → `SupportedExecutorTypes`) |
| FR-12 / FR-13 | `AiCompletionNodeExecutor` build + Validate contract (Action FK + JPS REQUIRED; no Tool) |
| FR-16 | Typed `ExecutorConfigSchema` per executor — the contract for `node.sprk_configjson` |
| FR-19 | Every existing node row must have `sprk_executortype` populated (backfill) |
| FR-30 | This guide rewrite |
| NFR-08 | Documentation discipline (DELETE / UPDATE in place — no SUPERSEDED markers) |

### ADR References

| ADR | Relevance to JPS |
|-----|------------------|
| ADR-010 | DI minimalism — `JpsRefResolver` and `PromptSchemaOverrideMerger` are static (no DI registration) |
| ADR-013 | AI architecture — extend BFF, no separate AI service; JPS pipeline lives in `Sprk.Bff.Api` |
| ADR-015 | No prompt logging — render identifiers only; no actual prompt content in logs or documentation |

### Key Source Files

All paths relative to `src/server/api/Sprk.Bff.Api/`:

| File | Purpose |
|------|---------|
| `Services/Ai/PromptSchemaRenderer.cs` | Core rendering logic (format detection, assembly, JSON Schema generation) |
| `Services/Ai/Models/PromptSchema.cs` | C# record types for the JPS data model |
| `Services/Ai/JpsRefResolver.cs` | Static `$ref` extraction from scopes section |
| `Services/Ai/PromptSchemaOverrideMerger.cs` | Static override merge logic with `__replace` directive |
| `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` | Pipeline orchestration per node |
| `Services/Ai/Handlers/GenericAnalysisHandler.cs` | Default JPS-consuming handler |
| `scripts/Seed-JpsActions.ps1` | Dataverse seeding script for JPS definitions |

---

## 13. Designing Whole Playbooks (pointer)

Authoring a single node + Action is scoped above (§§6, 7, 9). For end-to-end playbook design — multi-node graphs, scope-catalog lookup, model selection, deploy verification — invoke the [`jps-playbook-design`](../../.claude/skills/jps-playbook-design/SKILL.md) skill. The sibling [`PLAYBOOK-AUTHOR-GUIDE.md`](PLAYBOOK-AUTHOR-GUIDE.md) covers playbook-level authoring; it uses the same node-first model documented above.


---

*Document Owner: Spaarke Engineering*
*Last Updated: 2026-06-28 (R7 node-first dispatch rewrite — Wave 6 task 065, spec FR-30); 2026-04-05 (v3 content)*
