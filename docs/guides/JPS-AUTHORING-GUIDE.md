# JPS Authoring Guide

> **Version**: 3.0 (consolidated)
> **Date**: March 2026
> **Status**: Production
> **Author**: Spaarke Engineering
> **Audience**: Developers, AI engineers, prompt authors, solution architects
> **Supersedes**: PLAYBOOK-JPS-PROMPT-SCHEMA-GUIDE.md, PLAYBOOK-DESIGN-GUIDE.md (merged in)
> **Related**: [AI Architecture Guide](../architecture/AI-ARCHITECTURE.md), [Scope Configuration Guide](SCOPE-CONFIGURATION-GUIDE.md)

---

## Table of Contents

1. [What is JPS?](#1-what-is-jps)
2. [JPS Pipeline Architecture](#2-jps-pipeline-architecture)
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
6. [Creating a New JPS Action (Step-by-Step)](#6-creating-a-new-jps-action-step-by-step)
7. [Playbook Design Patterns](#7-playbook-design-patterns)
8. [Examples](#8-examples)
9. [Migration Guide (Hardcoded to JPS)](#9-migration-guide-hardcoded-to-jps)
10. [Deployment](#10-deployment)
11. [Best Practices](#11-best-practices)
12. [Validation Checklist](#12-validation-checklist)
13. [Troubleshooting](#13-troubleshooting)
14. [Designing Playbooks with Claude Code](#14-designing-playbooks-with-claude-code)
   - [Quick Start](#quick-start)
   - [Scope Catalog](#scope-catalog)
   - [Model Selection](#model-selection)
   - [Output Node Types](#output-node-types)
   - [Creating New Scope Primitives](#creating-new-scope-primitives)

---

## 1. What is JPS?

### The Problem

Hardcoded prompts in C# handler classes are unmaintainable. When prompt logic lives inside `BuildExecutionPrompt()` methods, every change requires a code deployment. Prompts cannot be reused across actions, tested independently, or edited by non-developers.

### The Solution

JSON Prompt Schema (JPS) externalizes prompt logic into structured JSON stored in the `sprk_systemprompt` column of `sprk_aiaction` Dataverse records. The BFF API detects the format at runtime and renders it into an assembled prompt for OpenAI consumption.

### Benefits

| Benefit | Description |
|---------|-------------|
| No-code editing | Prompt authors modify JSON in Dataverse — no C# deployment needed |
| Structured output | Output fields generate JSON Schema for constrained decoding |
| Scope composition | Attach shared knowledge and skills via `$ref` — maintained once, used everywhere |
| Template reuse | One Action definition serves multiple nodes via template parameters and overrides |
| Validation | Typed fields, enums, and `$choices` constrain model output at the schema level |

### When to Use JPS vs. Flat Text

| Use JPS When | Use Flat Text When |
|--------------|--------------------|
| Action needs structured JSON output | Simple text-in, text-out with no field mapping |
| Action is reused across multiple playbook nodes | One-off prompt with no reuse potential |
| Prompt needs shared knowledge scopes | Prompt is self-contained and short |
| Output routes to downstream nodes (`$choices`) | Prototyping or rapid experimentation |
| Multiple teams author or customize prompts | Legacy handler not yet migrated |

---

## 2. JPS Pipeline Architecture

### Data Flow

```
sprk_aiaction.sprk_systemprompt (JSON or flat text)
        |
        v
  Format Detection (IsJpsFormat)
  --------------------------------
  Starts with '{' AND contains "$schema" --> JPS path
  Otherwise --> flat-text legacy path (pass-through)
        |
   +----+----+
   v         v
 JPS Path    Legacy Path (unchanged)
   |
   v
 1. Override Merge (PromptSchemaOverrideMerger)
    ConfigJson.promptSchemaOverride merged into base schema
        |
        v
 2. Scope Resolution (ScopeResolverService)
    Parallel Dataverse queries for skills, knowledge by ID
        |
        v
 3. Named $ref Resolution (JpsRefResolver --> IScopeResolverService)
    Extract knowledge:{name} and skill:{name} from scopes section
    Resolve against Dataverse by name
        |
        v
 4. Template Parameter Substitution
    {{paramName}} replaced from ConfigJson.templateParameters
        |
        v
 5. $choices Resolution
    output.fields[].enum populated from Dataverse lookups, option sets, or downstream nodes
        |
        v
 6. PromptSchemaRenderer.Render()
    Assembles instruction + scopes + input + output + examples --> prompt text
    Generates JSON Schema for structuredOutput if enabled
        |
        v
 RenderedPrompt --> OpenAI (via OpenAiClient)
```

### Key Components

| Component | File | Purpose |
|-----------|------|---------|
| `PromptSchemaRenderer` | `Services/Ai/PromptSchemaRenderer.cs` | Renders JPS JSON + runtime context into assembled prompt text |
| `JpsRefResolver` | `Services/Ai/JpsRefResolver.cs` | Extracts `knowledge:{name}` and `skill:{name}` refs from scopes (static, no DI) |
| `PromptSchemaOverrideMerger` | `Services/Ai/PromptSchemaOverrideMerger.cs` | Merges node-level overrides into base schema (static, no DI) |
| `ScopeResolverService` | `Services/Ai/ScopeResolverService.cs` | Resolves skills, knowledge, and tools by ID or name from Dataverse |
| `AiAnalysisNodeExecutor` | `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` | Central wiring point — orchestrates the full pipeline per node |
| `GenericAnalysisHandler` | `Services/Ai/Handlers/GenericAnalysisHandler.cs` | Default handler consuming JPS prompts via the renderer |
| `PromptSchema` | `Services/Ai/Models/PromptSchema.cs` | C# data model for the JPS JSON structure |

All file paths are relative to `src/server/api/Sprk.Bff.Api/`.

### Format Detection

```csharp
private static bool IsJpsFormat(string rawPrompt)
{
    return rawPrompt.TrimStart().StartsWith('{') && rawPrompt.Contains("\"$schema\"");
}
```

This check is duplicated in both `PromptSchemaRenderer` and `AiAnalysisNodeExecutor` for independent decision-making. If JPS parsing fails, the renderer falls back to flat-text with a warning log.

### Dual-Path Pattern

Migrated handlers that retain custom logic use a dual-path pattern:

```csharp
var prompt = context.ActionSystemPrompt;
if (IsJpsFormat(prompt))
{
    // JPS path: renderer handles everything
    return await _genericHandler.ExecuteAsync(context, ct);
}
// Legacy path: hardcoded prompt construction (backward compatible)
return await ExecuteLegacyAsync(context, ct);
```

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

## 6. Creating a New JPS Action (Step-by-Step)

### Decision Tree: Do I Need JPS?

```
Does the action need structured JSON output?  --> Yes --> Use JPS
                                               --> No
Is the action reused across multiple nodes?    --> Yes --> Use JPS
                                               --> No
Does it need shared knowledge scopes?          --> Yes --> Use JPS
                                               --> No
Does output route to downstream nodes?         --> Yes --> Use JPS
                                               --> No  --> Flat text may suffice
```

### Step 1: Define the Instruction

Write the `role`, `task`, `context`, and `constraints`. Be specific: domain-expert personas produce better results than generic assistants. Make `task` actionable and unambiguous. Make `constraints` testable.

### Step 2: Define Output Fields

Identify what structured data you need. For each field, specify `name` (Dataverse column), `type`, `description`, and optional `enum` or `maxLength`. Use `"$choices"` for routing fields. Use `"$choices": "lookup:..."` for Dataverse-constrained lookup fields.

### Step 3: Add Scopes (if needed)

Reference shared knowledge or skills via `$ref`. Prefer scopes over inline knowledge — shared records are maintained once and used everywhere. Keep scope count minimal (each adds to token count).

### Step 4: Add Template Parameters

If the same action serves multiple configurations (e.g., different jurisdictions), use `{{paramName}}` placeholders in instruction fields. Document expected parameters in `metadata.description`.

### Step 5: Add Examples

Include 1-3 few-shot examples for complex output formats. Examples should cover edge cases and match `output.fields` names exactly.

### Step 6: Validate JSON

See the [Validation Checklist](#12-validation-checklist) below.

### Step 7: Seed to Dataverse

```powershell
# Dry run — preview changes without writing
.\scripts\Seed-JpsActions.ps1 -WhatIf

# Seed with backup of existing prompts
.\scripts\Seed-JpsActions.ps1 -BackupPath ./backups

# Seed specific environment
.\scripts\Seed-JpsActions.ps1 -Environment test

# Seed with explicit token (CI/CD)
.\scripts\Seed-JpsActions.ps1 -Environment prod -Token "eyJ0eXAi..."
```

JPS JSON files are stored in `projects/*/notes/jps-conversions/` and mapped to Action records by `sprk_name`.

### Step 8: Test End-to-End

1. Trigger the playbook containing the action
2. Verify format detection selects the JPS path (check logs for `PromptSchemaRenderer`)
3. Verify structured output matches the expected JSON Schema
4. Verify scope resolution succeeded (check for missing `$ref` warnings)
5. Test with template parameters if applicable

---

## 7. Playbook Design Patterns

### Simple Action (Single Node, No Routing)

A standalone classification or extraction action. One node reads a document, produces structured output, and writes to Dataverse.

```
[Document Upload] --> [AI Analysis Node] --> [Update Record]
```

JPS features used: `instruction`, `output` with `structuredOutput: true`, optional `examples`.

### Classification + Routing

A classifier node uses `$choices` to route to specialized downstream nodes based on document type or content.

```
[Document] --> [Classifier Node] --> $choices --> [Path A: Contract Review]
                                              --> [Path B: Invoice Processing]
                                              --> [Path C: General Filing]
```

JPS features used: `output.fields` with `"enum": ["$choices"]`, `structuredOutput: true`.

### Multi-Document Comparison

A comparison handler receives multiple document texts via `input.priorOutputs` and produces a comparative analysis.

```
[Doc A Analysis] --+
                    +--> [Comparison Node] --> [Summary Record]
[Doc B Analysis] --+
```

JPS features used: `input.priorOutputs` for upstream dependencies, template parameters for comparison criteria.

### RAG-Augmented Analysis

An action that combines document text with retrieved knowledge scopes for domain-specific analysis.

```
[Document] --> [AI Node + $knowledge refs] --> [Structured Output]
```

JPS features used: `scopes.$knowledge` with `$ref` for domain knowledge, `as` labels for section organization.

### Template Parameter Patterns

One Action definition serves multiple use cases via `templateParameters` in each consuming node's `ConfigJson`:

```
Shared Action: "Analyze under {{jurisdiction}} law with {{depth}} depth"
  |
  +-- Node A: { "templateParameters": { "jurisdiction": "Delaware", "depth": "comprehensive" } }
  +-- Node B: { "templateParameters": { "jurisdiction": "California", "depth": "summary" } }
```

---

## 8. Examples

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

## 9. Migration Guide (Hardcoded to JPS)

### Assessment Checklist

Migrate a handler when:
- [ ] Handler has a hardcoded prompt string > 200 characters
- [ ] Prompt logic is reusable across multiple actions or nodes
- [ ] Handler needs structured output that maps to Dataverse columns
- [ ] Non-developers need to edit the prompt content
- [ ] Handler has no custom pre/post-processing logic beyond prompt construction

### Conversion Steps

1. **Extract prompt**: Copy the hardcoded prompt text from the handler
2. **Create JPS**: Structure the text into `instruction`, `output`, and other sections
3. **Add dual-path**: Implement the JPS/legacy check in the handler (see Section 2)
4. **Seed to Dataverse**: Run `Seed-JpsActions.ps1` to populate the Action record
5. **Test**: Verify both paths produce equivalent results
6. **Deprecate legacy**: After validation, remove the legacy code path

### Migration Strategies

| Strategy | When to Use |
|----------|-------------|
| **Consolidated** | No custom logic — remove handler; `GenericAnalysisHandler` serves via JPS |
| **Thin Wrapper** | Has custom pre/post-processing — retain handler with dual-path |

### Testing Strategy

Run both paths against the same input. Compare output field-by-field, verify enum values and routing, check edge cases (empty/long/ambiguous documents), and disable flat-text fallback in test to verify JPS parsing.

---

## 10. Deployment

### BFF API Deployment

```powershell
# Deploy BFF API to Azure App Service
.\scripts\Deploy-BffApi.ps1
```

The BFF API contains all JPS pipeline components (`PromptSchemaRenderer`, `JpsRefResolver`, `PromptSchemaOverrideMerger`). Any changes to these classes require a BFF deployment.

### Dataverse Seeding

```powershell
# Dry run — preview which records will be updated
.\scripts\Seed-JpsActions.ps1 -WhatIf

# Seed with backup of existing prompts
.\scripts\Seed-JpsActions.ps1 -BackupPath ./backups

# Seed specific environment
.\scripts\Seed-JpsActions.ps1 -Environment test

# Seed with explicit token (CI/CD)
.\scripts\Seed-JpsActions.ps1 -Environment prod -Token "eyJ0eXAi..."
```

### Verification

1. Query `sprk_analysisactions` for records with `sprk_systemprompt` starting with `{`
2. Trigger a playbook and check logs for JPS path selection
3. Verify scope resolution succeeded (check for missing `$ref` warnings)

### Rollback

Restore from backup files (`-BackupPath`), re-seed from git history, or remove JPS from the record (dual-path falls back to flat text automatically).

---

## 11. Best Practices

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

## 12. Validation Checklist

Before deploying a new JPS definition:

- [ ] `$schema` is `"https://spaarke.com/schemas/prompt/v1"` and `$version` is `1`
- [ ] `instruction` has at least `role` and `task`
- [ ] All `output.fields` have `name`, `type`, and `description`
- [ ] `enum` values are lowercase if used for Dataverse option sets
- [ ] `$ref` names match existing Dataverse records
- [ ] Template parameters have corresponding `templateParameters` in consuming nodes
- [ ] `metadata` includes `description` and `tags`
- [ ] No actual production prompt content in documentation or logs (ADR-015)
- [ ] Test with flat-text fallback disabled to verify JPS parsing

---

## 13. Troubleshooting

### Format Detection Issues

**Symptom**: JPS JSON is treated as flat text.

| Cause | Fix |
|-------|-----|
| JSON does not start with `{` (leading whitespace after BOM) | Ensure `sprk_systemprompt` has no BOM or leading non-`{` characters |
| `$schema` property missing or misspelled | Verify `"$schema": "https://spaarke.com/schemas/prompt/v1"` is present |
| JSON parse failure (malformed) | Validate JSON syntax; check for trailing commas in strict parsers |

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
| [Scope Configuration Guide](SCOPE-CONFIGURATION-GUIDE.md) | Creating Tools, Skills, Knowledge, and Action records in Dataverse; builder UI; pre-fill integration |
| [AI Architecture](../architecture/AI-ARCHITECTURE.md) | Full platform architecture, JPS pipeline internals |
| [Playbook Architecture](../architecture/playbook-architecture.md) | Playbook internals, node executors, execution engine |

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

## 14. Designing Playbooks with Claude Code

This section covers how to design and deploy complete playbooks using Claude Code — from natural language descriptions to deployed Dataverse records.

### Quick Start

#### Option 1: Natural Language (Recommended)

Tell Claude Code what you need in plain English:

```
I need a playbook that reviews commercial lease agreements.
It should extract key dates, financial terms, and obligations,
then flag any non-standard clauses and produce an executive summary.
```

Claude Code will:
1. Design a node graph based on your description
2. Select the right analysis actions, skills, knowledge, and tools from the scope catalog
3. Choose the optimal AI model for each node (gpt-4o for deep analysis, gpt-4o-mini for triage)
4. Ask you to confirm the design
5. Generate the playbook definition
6. Deploy everything to Dataverse
7. Verify it appears in the Playbook Builder canvas

#### Option 2: Slash Command

```
/jps-playbook-design
```

This invokes the structured 13-step workflow with prompts at each decision point.

#### Option 3: Definition File (Advanced)

Write a playbook definition JSON directly and deploy with:

```powershell
.\scripts\Deploy-Playbook.ps1 -DefinitionFile "path/to/my-playbook.json"
```

### How Claude Code Designs Playbooks

```
YOU                          CLAUDE CODE                    DATAVERSE
 │                              │                              │
 │  Describe your playbook      │                              │
 │ ───────────────────────────► │                              │
 │                              │  1. Load scope catalog       │
 │                              │  2. Design node graph        │
 │                              │  3. Select scopes & models   │
 │                              │                              │
 │  ◄─── "Here's my plan:      │                              │
 │        3 nodes, 5 skills,    │                              │
 │        estimated $3/1M tok"  │                              │
 │                              │                              │
 │  "Looks good, deploy it"     │                              │
 │ ───────────────────────────► │                              │
 │                              │  4. Generate definition JSON │
 │                              │  5. Run Deploy-Playbook.ps1  │
 │                              │ ────────────────────────────► │
 │                              │     Create playbook record   │
 │                              │     Create nodes             │
 │                              │     Link scopes (N:N)        │
 │                              │     Save canvas layout       │
 │  ◄─── "Deployed! Open in    │                              │
 │        Playbook Builder"     │                              │
```

### Scope Catalog

#### Actions (ACT-*)

The primary analysis instruction for a node. Each AI node uses exactly one action.

| Code | Name | Best For |
|------|------|----------|
| ACT-001 | Contract Review | MSAs, PSAs, vendor agreements |
| ACT-002 | NDA Analysis | NDAs, CDAs, confidentiality agreements |
| ACT-003 | Lease Agreement Review | Commercial and residential leases |
| ACT-004 | Invoice Processing | Vendor invoices, utility bills |
| ACT-005 | SLA Analysis | Service level agreements |
| ACT-006 | Employment Agreement Review | Offer letters, employment contracts |
| ACT-007 | Statement of Work Analysis | SOWs, work orders |
| ACT-008 | General Legal Document Review | Any legal document (catch-all) |

#### Skills (SKL-*)

Composable expertise that enriches the analysis. Each node can use multiple skills.

| Code | Name | What It Adds |
|------|------|-------------|
| SKL-001 | Citation Extraction | Section and page citations for every claim |
| SKL-002 | Risk Flagging | [RISK: HIGH/MEDIUM/LOW] annotations |
| SKL-003 | Summary Generation | Executive summary in 3-5 bullets |
| SKL-004 | Date Extraction | All dates normalized to ISO 8601 |
| SKL-005 | Party Identification | Full legal names, roles, contacts |
| SKL-006 | Obligation Mapping | Structured obligation table |
| SKL-007 | Defined Terms | Alphabetical glossary of defined terms |
| SKL-008 | Financial Terms | Monetary amounts, schedules, rates |
| SKL-009 | Termination Analysis | Triggers, notice periods, consequences |
| SKL-010 | Jurisdiction & Governing Law | Applicable law, dispute resolution |

#### Knowledge Sources (KNW-*)

Reference materials injected into the prompt for context.

| Code | Name | Content |
|------|------|---------|
| KNW-001 | Contract Terms Glossary | 50+ standard term definitions |
| KNW-002 | NDA Review Checklist | 20-item NDA checklist |
| KNW-003 | Lease Standards | Commercial lease provisions |
| KNW-004 | Invoice Processing Guide | AP rules and fraud indicators |
| KNW-005 | SLA Metrics Reference | SLA/SLO/SLI definitions |
| KNW-006 | Employment Law Reference | US employment fundamentals |
| KNW-007 | IP Assignment Clause Library | Annotated IP clauses |
| KNW-008 | Termination Provisions | Triggers, damages, survival |
| KNW-009 | Jurisdiction Guide | Governing law, arbitration |
| KNW-010 | Red Flags Catalog | 32 red flags across 10 categories |

#### Tools (TL-*)

Executable handlers that perform specific operations.

| Code | Name | What It Does |
|------|------|-------------|
| TL-001 | DocumentSearch | Search knowledge base and document index |
| TL-002 | AnalysisRetrieval | Retrieve prior analysis results |
| TL-003 | KnowledgeRetrieval | Retrieve knowledge source content |
| TL-004 | TextRefinement | AI-assisted text editing |
| TL-005 | CitationExtractor | Extract citation references |
| TL-006 | SummaryGenerator | Generate structured summaries |
| TL-007 | RedFlagDetector | Detect risk/compliance issues |
| TL-008 | PartyExtractor | Extract party information |

### Model Selection

Claude Code automatically selects the best AI model for each node:

| Node Purpose | Model Selected | Why |
|-------------|---------------|-----|
| Document classification | gpt-4o-mini | Simple categorical decision — fast and cheap |
| Document triage | gpt-4o-mini | Binary/bounded decision — speed matters |
| Deep contract analysis | gpt-4o | Complex reasoning, nuanced interpretation |
| Entity extraction | gpt-4o | Accuracy critical for structured output |
| Simple summary (TL;DR) | gpt-4o-mini | Bullet summaries don't need depth |
| Detailed summary with citations | gpt-4o | Cross-referencing requires full model |
| Legal reasoning | gpt-4o | Interpretive analysis needs depth |
| Financial calculation | gpt-4o | Multi-step computation needs accuracy |
| Condition routing | gpt-4o-mini | Simple boolean evaluation |

**Cost Impact**:

| Strategy | Estimated Cost (per 1M tokens) |
|----------|-------------------------------|
| All gpt-4o | ~$7.50 |
| Optimized (mix) | ~$3.00 |
| All gpt-4o-mini | ~$0.45 |

Claude Code will show you the estimated cost breakdown before deploying.

### Output Node Types

#### DeliverOutput (ActionType 40)

The standard output node. Assembles results from upstream nodes into a structured output, optionally writing fields to the triggering Dataverse record via UpdateRecord-style field mappings.

**When to use**: Displaying results in UI, writing analysis output back to the source record.

#### DeliverToIndex (ActionType 41)

Indexes upstream node results into Azure AI Search for semantic retrieval. Enqueues a `RagIndexing` background job via Service Bus — processing is asynchronous.

**When to use**: Making playbook output searchable via Semantic Search. Common pattern: Document Profile playbooks that index document metadata for later retrieval.

**Configuration**:

| Property | Default | Description |
|----------|---------|-------------|
| `indexName` | `"knowledge"` | Target Azure AI Search index |
| `indexSource` | `"document"` | Source type: `"document"` (full doc) or `"field"` (specific field) |

**Design tip**: Use DeliverToIndex alongside DeliverOutput when you want both UI display AND search indexing. They can run in parallel as separate output branches.

### Creating New Scope Primitives

If your playbook needs a scope that doesn't exist yet:

#### New Action

Tell Claude Code: "I need a new action for analyzing insurance policies"

Claude Code will:
1. Run `/jps-action-create` to generate the JPS definition
2. Add it to `Seed-JpsActions.ps1` and seed to Dataverse
3. Add it to `scope-model-index.json`

#### New Skill

Tell Claude Code: "I need a skill for extracting coverage limits from insurance documents"

Claude Code will:
1. Create the prompt fragment content
2. Add it to `Seed-AnalysisSkills.ps1` and update Dataverse
3. Add it to `scope-model-index.json`

#### New Knowledge Source

Tell Claude Code: "I need a knowledge source with standard insurance policy terms"

Claude Code will:
1. Create the reference content
2. Add it to `Seed-KnowledgeScopes.ps1` and seed to Dataverse
3. Add it to `scope-model-index.json`

After creating new scopes, refresh the index:

```powershell
# Regenerate scope-model-index.json from current Dataverse state
.\scripts\Refresh-ScopeModelIndex.ps1 -Environment dev
```

This ensures Claude Code always has the latest scope catalog when designing playbooks.

### Definition File Format

For full control, write the definition JSON directly:

```json
{
  "$schema": "https://spaarke.com/schemas/playbook-definition/v1",
  "playbook": {
    "name": "Custom Lease Review",
    "description": "Three-stage lease analysis pipeline",
    "isPublic": true,
    "capabilities": ["analyze", "search"],
    "scopes": {
      "actions": ["ACT-003"],
      "skills": ["SKL-003", "SKL-004", "SKL-005", "SKL-006", "SKL-008"],
      "knowledge": ["KNW-003"],
      "tools": ["TL-001", "TL-007"]
    }
  },
  "nodes": [
    {
      "name": "Lease Profiler",
      "actionCode": "ACT-003",
      "nodeType": "AIAnalysis",
      "outputVariable": "profile",
      "model": "gpt-4o-mini",
      "positionX": 100,
      "positionY": 200,
      "dependsOn": [],
      "scopes": {
        "skills": ["SKL-004", "SKL-005"],
        "knowledge": [],
        "tools": ["TL-001"]
      }
    },
    {
      "name": "Deep Clause Analysis",
      "actionCode": "ACT-003",
      "nodeType": "AIAnalysis",
      "outputVariable": "analysis",
      "model": "gpt-4o",
      "positionX": 400,
      "positionY": 200,
      "dependsOn": ["Lease Profiler"],
      "scopes": {
        "skills": ["SKL-006", "SKL-008"],
        "knowledge": ["KNW-003"],
        "tools": ["TL-001", "TL-007"]
      }
    },
    {
      "name": "Executive Summary",
      "actionCode": "ACT-003",
      "nodeType": "AIAnalysis",
      "outputVariable": "summary",
      "model": "gpt-4o-mini",
      "positionX": 700,
      "positionY": 200,
      "dependsOn": ["Deep Clause Analysis"],
      "scopes": {
        "skills": ["SKL-003"],
        "knowledge": [],
        "tools": ["TL-006"]
      }
    }
  ],
  "edges": [
    { "source": "Lease Profiler", "target": "Deep Clause Analysis" },
    { "source": "Deep Clause Analysis", "target": "Executive Summary" }
  ]
}
```

Deploy with:

```powershell
.\scripts\Deploy-Playbook.ps1 -DefinitionFile "my-lease-playbook.json"

# Preview first without creating:
.\scripts\Deploy-Playbook.ps1 -DefinitionFile "my-lease-playbook.json" -DryRun
```

### Troubleshooting Deployment

| Issue | Cause | Fix |
|-------|-------|-----|
| "Scope code not found" during deploy | Code doesn't exist in Dataverse | Run the appropriate seed script first |
| Nodes overlap on canvas | Default positions conflict | Edit positionX/Y in definition, or drag in canvas |
| Playbook doesn't appear in canvas | Canvas layout JSON not saved | Re-run Deploy-Playbook.ps1 with the same definition |
| Wrong model being used | Model deployment not found | Check `sprk_aimodeldeployments` in Dataverse |
| Node scopes not linked | N:N association failed | Check script output for errors; verify relationship names |

---

*Document Owner: Spaarke Engineering*
*Last Updated: March 2026*
