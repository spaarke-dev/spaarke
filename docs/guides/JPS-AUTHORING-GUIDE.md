# JPS Authoring Guide

> **Version**: 1.0
> **Date**: March 4, 2026
> **Status**: Production
> **Author**: Spaarke Engineering
> **Related**: [AI Architecture Guide - Section 19](SPAARKE-AI-ARCHITECTURE.md#19-json-prompt-schema-jps-pipeline-2026-03-04)

---

## Overview

JSON Prompt Schema (JPS) is the structured prompt format for Spaarke playbook actions. JPS definitions replace flat-text prompts with composable, typed JSON that supports scoped knowledge, template parameters, dynamic choices, and structured output. JPS definitions are stored in the `sprk_systemprompt` column of `sprk_aiaction` records.

This guide covers how to author new JPS definitions. For the pipeline architecture, see [AI Architecture Guide - Section 19](SPAARKE-AI-ARCHITECTURE.md#19-json-prompt-schema-jps-pipeline-2026-03-04).

> **ADR-015 Compliance**: This guide uses placeholder content in examples. Do not include actual production prompt text in documentation or logs.

---

## JPS JSON Structure

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

## instruction Section

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
| `task` | string | What the AI must do — the primary objective |
| `context` | string | Why and where this runs — workflow context for the AI |
| `constraints` | string[] | MUST/MUST NOT rules — guardrails for output quality |

**Guidelines**:
- `role` should be specific (domain expert, not generic assistant)
- `task` should be actionable and unambiguous
- `context` helps the model understand the downstream usage of its output
- `constraints` should be testable (avoid vague guidance)

---

## input Section

Defines expected input fields and their constraints:

```json
{
  "input": {
    "document": {
      "required": true,
      "maxLength": 100000,
      "placeholder": "{{document.extractedText}}"
    }
  }
}
```

| Field | Type | Purpose |
|-------|------|---------|
| `required` | boolean | Whether this input must be provided |
| `maxLength` | number | Maximum character length (for truncation guidance) |
| `placeholder` | string | Template variable that the pipeline replaces at runtime |

The `document` field is the standard input for document analysis actions. The extracted text is populated by the pipeline before rendering.

---

## output Section

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
      }
    ],
    "structuredOutput": true
  }
}
```

### Field Properties

| Property | Type | Purpose |
|----------|------|---------|
| `name` | string | Dataverse column name the result maps to |
| `type` | string | Data type: `"string"`, `"number"`, `"boolean"`, `"array"` |
| `description` | string | What the model should generate for this field |
| `maxLength` | number | Max characters (string fields only) |
| `enum` | string[] | Allowed values — model must pick from this list |
| `$choices` | string | Auto-inject enum values at render time from Dataverse or downstream nodes |

### $choices

The `$choices` property auto-injects valid enum values at render time, constraining the AI to return only values that exist in Dataverse. This eliminates fuzzy matching on the frontend.

**Supported prefixes**:

| Prefix | Example | Source |
|--------|---------|--------|
| `lookup:` | `"lookup:sprk_mattertype_ref.sprk_mattertypename"` | Active records from a Dataverse reference entity |
| `optionset:` | `"optionset:sprk_matter.sprk_matterstatus"` | Single-select choice/picklist metadata labels |
| `multiselect:` | `"multiselect:sprk_matter.sprk_jurisdictions"` | Multi-select picklist metadata labels |
| `boolean:` | `"boolean:sprk_matter.sprk_isconfidential"` | Two-option boolean field labels (e.g., `["Yes", "No"]`) |
| `downstream:` | `"downstream:update_doc.sprk_documenttype"` | Downstream node field mapping options (routing patterns) |

**Example — Pre-fill with exact Dataverse values**:

```json
{
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
      }
    ]
  }
}
```

At render time, the `$choices` values are resolved from Dataverse and injected as `"enum"` arrays in the JSON Schema, forcing the model to select from exact values.

**Legacy format**: Fields with `"enum": ["$choices"]` are still supported for backward compatibility with downstream routing patterns. The literal `"$choices"` string is replaced with downstream node display names.

### structuredOutput

When `structuredOutput` is `true`:
- The renderer generates a JSON Schema from the fields array
- The OpenAI client enables structured output mode
- The model is guaranteed to return valid JSON matching the schema
- Field descriptions become the schema descriptions

When `false` or omitted, the model returns free-form text.

---

## scopes Section

Scopes attach external knowledge and skills to the prompt by name. The pipeline resolves `$ref` values against Dataverse before rendering.

```json
{
  "scopes": {
    "$knowledge": [
      { "$ref": "knowledge:contract-law-basics" },
      { "$ref": "knowledge:jurisdiction-rules" }
    ],
    "$skills": [
      { "$ref": "skill:entity-extraction" }
    ]
  }
}
```

### Reference Format

| Prefix | Resolves To | Dataverse Entity |
|--------|-------------|------------------|
| `knowledge:{name}` | Knowledge record content | `sprk_analysisknowledge` |
| `skill:{name}` | Skill record content | `sprk_analysisskill` |

The `{name}` portion matches the record's name field in Dataverse. The `JpsRefResolver` extracts these references and `ScopeResolverService` resolves them to full record content, which is then merged into the assembled prompt by the renderer.

**When to use scopes**:
- The action needs domain knowledge that is maintained separately (e.g., legal rules, industry standards)
- The action relies on reusable skill definitions shared across multiple actions
- You want to avoid duplicating knowledge across JPS definitions

---

## examples Section

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

---

## metadata Section

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
| `author` | string | Who created this definition (`"migration"` for automated, user name for manual) |
| `authorLevel` | number | `0` = system/migration, `1` = admin, `2` = user |
| `createdAt` | string | ISO 8601 timestamp |
| `description` | string | Human-readable description for the Playbook Builder UI |
| `tags` | string[] | Categorization tags for filtering and discovery |

---

## Template Parameters

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

---

## Override Merge

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

### Merge Rules

| Section | Merge Behavior |
|---------|---------------|
| `instruction.role` | Override replaces base |
| `instruction.task` | Override replaces base |
| `instruction.context` | Override replaces base |
| `instruction.constraints` | Override appends to base. Use `"$clear"` as first element to replace entirely |
| `output.fields` | Override fields replace base fields by matching `name`. New fields are appended |

Override merge happens before template parameter substitution and before rendering.

---

## Example: Simple Action

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

---

## Example: Complex Action with Scopes, Parameters, and Choices

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

---

## Best Practices

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
- **Use `$choices` for routing fields**: Connects output to playbook graph structure
- **Keep `description` precise**: The model uses field descriptions to understand what to generate

### Override Merge

- **Use overrides for node-specific customization**: Avoids duplicating entire JPS definitions
- **Use `$clear` sparingly**: Replacing all constraints removes important guardrails
- **Test merged output**: Verify the override produces the expected assembled prompt

---

## Validation Checklist

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

*Document Owner: Spaarke Engineering*
*Last Updated: March 4, 2026*
