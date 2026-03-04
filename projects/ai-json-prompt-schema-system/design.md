# JSON Prompt Schema (JPS) — Prompt Composition for Spaarke AI Playbooks

> **Author**: Ralph Schroeder
> **Date**: March 3, 2026
> **Status**: Design
> **Audience**: Engineers, Claude Code, AI Agents

---

## Executive Summary

Spaarke's playbook engine has a sophisticated node orchestration system (typed field mappings, template-driven UpdateRecord coercion, structured output support via Azure OpenAI). But the prompts feeding this engine are **flat text blobs** — unstructured, unvalidated, and manually maintained. This means the entire system is only as good as whatever someone types into a textarea.

**JSON Prompt Schema (JPS)** introduces a structured, JSON-based format for defining AI instructions. It formalizes the **Composition** stage of the AI pipeline — the step between authoring (defining what the AI should do) and orchestration (executing nodes in order). JPS ensures that all three authoring levels — power users, advanced users, and AI agents — produce the same canonical format, bridging human-created and code-developed AI primitives.

---

## The AI Pipeline — Where Composition Fits

The Spaarke AI pipeline has four stages. JPS introduces the **Composition** stage, which was previously implicit (ad-hoc string concatenation in `GenericAnalysisHandler.BuildExecutionPrompt`):

```
┌─────────────────────────────────────────────────────────────────────────┐
│  STAGE 1: AUTHORING                                                     │
│  Where AI instructions are defined                                      │
│  PlaybookBuilder forms (L1) · JSON editor (L2) · Builder Agent (L3)    │
│  Output: JPS schema stored in sprk_systemprompt                         │
├─────────────────────────────────────────────────────────────────────────┤
│  STAGE 2: COMPOSITION  ← THIS PROJECT                                  │
│  Where structured schemas become executable prompts                     │
│  PromptSchemaRenderer: resolve $refs → render templates → assemble     │
│  Input: JPS schema + resolved scopes + template context                │
│  Output: flat prompt string + optional JSON Schema for structured mode  │
├─────────────────────────────────────────────────────────────────────────┤
│  STAGE 3: ORCHESTRATION                                                 │
│  Where nodes are routed and executed                                    │
│  PlaybookOrchestrationService · ExecutionGraph · Node executors         │
│  Scope resolution · Batch scheduling · Output propagation               │
├─────────────────────────────────────────────────────────────────────────┤
│  STAGE 4: EXECUTION                                                     │
│  Where AI computation happens                                           │
│  Azure OpenAI (GPT-4o) · Document Intelligence · AI Search             │
│  OpenAiClient · Tool handlers · Response parsing                        │
└─────────────────────────────────────────────────────────────────────────┘
```

### Relationship to the Four-Tier Architecture

| Tier | Responsibility | JPS Impact |
|------|---------------|------------|
| **Tier 1: Scope Library** | Reusable AI primitives | JPS schema stored in Action scopes (`sprk_systemprompt`) |
| **Tier 2: Composition Patterns** | How scopes are assembled | **JPS formalizes this** — structured schema replaces ad-hoc text assembly |
| **Tier 3: Execution Runtime** | Where AI runs | Consumes rendered prompts (unchanged interface) |
| **Tier 4: Azure Infrastructure** | Cloud services | Uses structured output mode more effectively |

---

## Problem Statement

### What's Wrong Today

1. **Prompts are flat text** — The `sprk_systemprompt` field on the Action scope contains an unstructured string. There's no separation of role, task, constraints, or expected output format. The entire instruction is a single blob.

2. **Choice values are duplicated** — When an AI node classifies a document type, the valid values (contract, invoice, proposal...) must be typed into the prompt AND separately configured in the downstream UpdateRecord node's `options` map. These two lists can drift apart, causing silent failures where the AI outputs a value the coercion can't match.

3. **No output field definitions** — The prompt mentions expected output fields informally ("return a JSON with documentType, summary, confidence...") but there's no structured definition. This means:
   - No validation that downstream nodes reference fields the AI actually produces
   - No structured output mode (Azure OpenAI JSON Schema constrained decoding) is used
   - No type checking between AI output and Dataverse field types

4. **Knowledge is appended blindly** — All Knowledge scopes linked to a node are concatenated and appended as a "Reference Knowledge" section. There's no way to reference specific knowledge contextually within the prompt or to use knowledge as examples, definitions, or constraints.

5. **Quality depends on the author** — Creating an effective prompt requires prompt engineering expertise. Power users who create playbooks must understand:
   - How to structure a system prompt
   - What makes the AI produce reliable JSON output
   - How to specify constraints that the AI follows
   - How to reference upstream node outputs correctly

6. **Builder Agent can't create effective prompts** — The conversational Builder Agent creates nodes and links scopes, but cannot generate optimized, structured prompts. It leaves prompt writing to the human, creating a bottleneck.

7. **No composition from reusable parts** — Skills and Knowledge are appended as flat text sections. There's no way for a prompt to reference a specific Knowledge source inline (e.g., "use the definitions from [Standard Terms]") or to compose prompts from reusable fragments with context-aware placement.

### The Bridge Problem

The fundamental challenge: **How do we bridge human-created AI primitives with more sophisticated code-developed AI primitives?**

Today there's a gap between what the system CAN do (structured output, typed coercion, template-driven field writes) and what humans CAN specify (flat text in a textarea). Building a more sophisticated system without making it accessible means the system's capabilities go unused.

The solution is a format that's:
- **Simple enough** for a power user to fill out a form
- **Powerful enough** for developers to write cross-referenced, template-driven schemas
- **Machine-readable enough** for AI agents to generate programmatically
- **Same format** regardless of authoring level — no translation layers

---

## The JSON Prompt Schema (JPS) Format

### Design Principles

1. **Flat text is valid JPS** — backward compatibility is absolute. An existing `sprk_systemprompt` string works unchanged.
2. **References resolve at render time** — `$choices`, `$knowledge`, `$skill` are compile-time directives resolved by the renderer, not stored data.
3. **The schema describes WHAT, not HOW** — the rendering pipeline is separate from the schema format.
4. **All three authoring levels produce identical output** — the renderer doesn't know or care who wrote the schema.
5. **JSON, not XML** — consistent with all other configuration in the Spaarke system (ConfigJson, tool configs, field mappings).

### Complete Schema Format

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,

  "instruction": {
    "role": "You are a document analysis assistant specializing in contract review.",
    "task": "Extract key entities and assess risk factors from the provided document.",
    "constraints": [
      "Only use information present in the document — do not infer or assume",
      "Provide confidence scores between 0.0 and 1.0 for each extraction",
      "If a field cannot be determined, return null rather than guessing"
    ],
    "context": "The user is a legal professional reviewing contracts for compliance."
  },

  "input": {
    "document": {
      "required": true,
      "maxLength": 50000,
      "placeholder": "{{document.extractedText}}"
    },
    "priorOutputs": [
      {
        "variable": "classify",
        "fields": ["output.documentType", "output.confidence"],
        "description": "Document classification result from upstream node"
      }
    ],
    "parameters": {
      "jurisdiction": "{{run.parameters.jurisdiction}}",
      "focusAreas": ["liability", "indemnification", "force majeure"]
    }
  },

  "output": {
    "fields": [
      {
        "name": "summary",
        "type": "string",
        "description": "One-paragraph executive summary of the document",
        "maxLength": 500
      },
      {
        "name": "documentType",
        "type": "string",
        "description": "Document classification",
        "$choices": "downstream:update_doc.sprk_documenttype"
      },
      {
        "name": "parties",
        "type": "array",
        "items": { "type": "string" },
        "description": "Named parties involved in the document"
      },
      {
        "name": "keyDates",
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "date": { "type": "string", "description": "Date in ISO 8601 format" },
            "description": { "type": "string" }
          }
        },
        "description": "Important dates and deadlines"
      },
      {
        "name": "riskLevel",
        "type": "string",
        "enum": ["low", "medium", "high", "critical"],
        "description": "Overall risk assessment"
      },
      {
        "name": "isConfidential",
        "type": "boolean",
        "description": "Whether the document contains confidential or privileged information"
      },
      {
        "name": "confidence",
        "type": "number",
        "minimum": 0,
        "maximum": 1,
        "description": "Overall confidence in the analysis quality"
      }
    ],
    "structuredOutput": true
  },

  "scopes": {
    "$skills": ["inline"],
    "$knowledge": [
      { "$ref": "knowledge:standard-contract-clauses", "as": "reference" },
      { "$ref": "knowledge:liability-definitions", "as": "definitions" },
      { "inline": "Force majeure clauses should be flagged as high risk in the current environment" }
    ]
  },

  "examples": [
    {
      "input": "Agreement between Acme Corp and Beta Inc for consulting services dated January 15, 2026...",
      "output": {
        "summary": "Consulting services agreement between Acme Corp and Beta Inc...",
        "documentType": "contract",
        "parties": ["Acme Corp", "Beta Inc"],
        "riskLevel": "low",
        "isConfidential": false,
        "confidence": 0.92
      }
    }
  ],

  "metadata": {
    "author": "builder-agent",
    "authorLevel": 3,
    "createdAt": "2026-03-03T10:00:00Z",
    "description": "Contract entity extraction with risk assessment",
    "tags": ["contract", "legal", "entity-extraction"]
  }
}
```

### Schema Sections

#### `instruction` — The Core AI Instruction

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `role` | string | No | System-level identity (e.g., "You are a contract analysis specialist") |
| `task` | string | **Yes** | The specific work to perform — the most important field |
| `constraints` | string[] | No | Behavioral constraints rendered as a bullet list |
| `context` | string | No | Additional context; supports Handlebars variables |

**Rendering**: Role is rendered as the opening line. Task follows. Constraints render as a numbered list under "## Constraints".

#### `input` — What the AI Receives

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `document` | object | No | Document text configuration (required, maxLength, placeholder) |
| `priorOutputs` | array | No | Declares upstream node output dependencies (for validation and documentation) |
| `parameters` | object | No | Additional parameters; supports Handlebars variables |

**`priorOutputs`** is declarative — it documents what upstream nodes this prompt expects, enabling validation. The actual data flows through Handlebars templates (`{{output_classify.output.documentType}}`).

#### `output` — What the AI Must Produce

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `fields` | array | **Yes** (if present) | Output field definitions with types, constraints, descriptions |
| `structuredOutput` | boolean | No | Use Azure OpenAI JSON Schema constrained decoding (default: false) |

**Each field supports**:

| Property | Type | Description |
|----------|------|-------------|
| `name` | string | Field name in the JSON output |
| `type` | string | `string`, `number`, `boolean`, `array`, `object` |
| `description` | string | What this field represents (becomes part of the prompt) |
| `enum` | string[] | Fixed set of valid values (rendered inline in prompt) |
| `$choices` | string | Auto-inject values from downstream node: `"downstream:nodeVar.fieldName"` |
| `items` | object | Array item schema (when type is `array`) |
| `maxLength` | number | Maximum string length |
| `minimum`/`maximum` | number | Numeric range constraints |

#### `scopes` — Explicit Scope References

| Field | Type | Description |
|-------|------|-------------|
| `$skills` | array | `"inline"` (use N:N scopes) or explicit named references |
| `$knowledge` | array | Named references (`$ref`) or inline content |

**`$knowledge` reference types**:

| Type | Syntax | Resolution |
|------|--------|------------|
| Named reference | `{"$ref": "knowledge:record-name"}` | Queries Dataverse for `sprk_analysisknowledge` by `sprk_name` |
| Inline content | `{"inline": "text content"}` | Used as-is, no resolution needed |
| Contextual label | `"as": "reference" \| "definitions" \| "examples"` | Controls section heading when rendered |

The `$knowledge` and `$skill` references **supplement** the N:N scope relationships, not replace them. N:N scopes are always included; `scopes` section adds additional or contextually-placed content.

#### `examples` — Few-Shot Learning

| Field | Type | Description |
|-------|------|-------------|
| `input` | string | Example input text |
| `output` | object | Expected output matching `output.fields` schema |

When present, examples are rendered as a "## Examples" section in the prompt, formatted to teach the AI the expected output structure.

#### `metadata` — Provenance and Classification

| Field | Type | Description |
|-------|------|-------------|
| `author` | string | Who created this schema (username or "builder-agent") |
| `authorLevel` | number | 1 (form), 2 (JSON editor), 3 (AI agent), 0 (migration) |
| `createdAt` | string | ISO 8601 timestamp |
| `description` | string | Human-readable description of this prompt's purpose |
| `tags` | string[] | Classification tags for search and organization |

### Reference Types

| Reference | Syntax | Resolution Source | Example |
|-----------|--------|-------------------|---------|
| `$choices` | `"downstream:nodeVar.fieldName"` | Downstream UpdateRecord node's `fieldMappings[].options` map | `"downstream:update_doc.sprk_documenttype"` → `["contract", "invoice", "proposal"]` |
| `$knowledge` | `{"$ref": "knowledge:name"}` | `sprk_analysisknowledge` record by `sprk_name` | `{"$ref": "knowledge:standard-contract-clauses"}` |
| `$skill` | `{"$ref": "skill:name"}` | `sprk_analysisskill` record by `sprk_name` | `{"$ref": "skill:liability-analysis"}` |
| Template | `{{output_nodeVar.output.field}}` | Handlebars from `PreviousOutputs` at runtime | `{{output_classify.output.documentType}}` |

---

## Format Detection and Backward Compatibility

JPS is stored in the **existing** `sprk_systemprompt` field on `sprk_analysisaction`. No new Dataverse column is needed.

**Detection logic**:

```
if (prompt is null or empty)
    → use tool/operation default (existing GenericAnalysisHandler behavior)
else if (prompt.TrimStart().StartsWith('{') && contains "$schema")
    → parse as JPS JSON, render via PromptSchemaRenderer
else
    → treat as flat text, render via legacy BuildExecutionPrompt path
```

**This means**:
- Every existing playbook continues to work unchanged — zero migration required
- New playbooks created via the form or Builder Agent automatically use JPS
- Users can upgrade existing prompts to JPS at any time by editing in the builder

### Upgrade Path

When a user opens an existing flat-text prompt in the new PlaybookBuilder form:
1. The form shows the flat text in the "Task" field (all other fields empty)
2. When the user saves, the form serializes to JPS format
3. The flat text is now wrapped in a proper schema — instant upgrade, no data loss

---

## The Composition Rendering Pipeline

### PromptSchemaRenderer Service

**New file**: `src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs`

This service replaces the static `BuildExecutionPrompt` method in `GenericAnalysisHandler.cs` (lines 408-456). It's a pure function — given a schema, scopes, and context, it produces a deterministic prompt string.

```
PromptSchemaRenderer.Render(
    rawPrompt,           // sprk_systemprompt value (flat text or JPS JSON)
    resolvedScopes,      // N:N skills, knowledge, tools from ScopeResolverService
    templateContext,     // Handlebars context (PreviousOutputs, document, run)
    downstreamNodes      // For $choices resolution
)
    ↓
Returns RenderedPrompt {
    PromptText,          // Assembled flat prompt string
    JsonSchema?,         // For structured output mode (nullable)
    SchemaName,          // For Azure OpenAI schema naming
    Format               // FlatText | JsonPromptSchema
}
```

### Pipeline Steps

```
Step 1: FORMAT DETECTION
  ├─ null/empty → delegate to tool/operation defaults (existing logic)
  ├─ flat text → legacy path (identical to current BuildExecutionPrompt)
  └─ JPS JSON → continue to Step 2

Step 2: PARSE & VALIDATE
  ├─ Deserialize JSON → PromptSchema model
  ├─ Validate required fields (instruction.task is mandatory)
  └─ Validate field types and constraints

Step 3: RESOLVE $choices REFERENCES
  ├─ For each output.field with $choices:
  │   ├─ Parse reference: "downstream:{outputVariable}.{fieldName}"
  │   ├─ Find downstream UpdateRecord node by outputVariable
  │   ├─ Read fieldMappings[].options for matching field
  │   └─ Inject option labels as enum values on the field
  └─ Log warnings for unresolvable references

Step 4: RESOLVE $knowledge AND $skill REFERENCES
  ├─ For each $ref in scopes.$knowledge:
  │   ├─ Query Dataverse sprk_analysisknowledge by sprk_name
  │   └─ Load sprk_content
  ├─ For each $ref in scopes.$skills:
  │   ├─ Query Dataverse sprk_analysisskill by sprk_name
  │   └─ Load sprk_promptfragment
  └─ Merge with N:N resolved scopes (N:N takes precedence on conflicts)

Step 5: RENDER HANDLEBARS TEMPLATES
  ├─ Render instruction.context: {{run.parameters.jurisdiction}} → "California"
  ├─ Render input.document.placeholder: {{document.extractedText}} → text
  ├─ Render input.parameters values
  └─ Uses existing TemplateEngine.Render() (unchanged)

Step 6: ASSEMBLE PROMPT SECTIONS
  ├─ 1. Role instruction (if present)
  ├─ 2. Task description
  ├─ 3. Constraints (numbered list)
  ├─ 4. Context (if present)
  ├─ 5. Document text
  ├─ 6. Parameters (if present)
  ├─ 7. Prior output references (if present)
  ├─ 8. Skills context (N:N + $skill references)
  ├─ 9. Knowledge context (N:N + $knowledge references, with section labels)
  ├─ 10. Examples (if present, formatted as few-shot)
  └─ 11. Output instructions (see Step 7)

Step 7: GENERATE OUTPUT SCHEMA
  ├─ If structuredOutput == true:
  │   ├─ Convert output.fields → JSON Schema Draft-07
  │   ├─ Include enum values (from $choices resolution)
  │   ├─ Set as response_format for constrained decoding
  │   └─ Append brief text instruction: "Return valid JSON matching the provided schema"
  └─ If structuredOutput == false:
      └─ Append text instructions listing expected fields with types and constraints
```

### Assembled Prompt Example

For the schema in the Complete Schema Format section above, the rendered prompt would be:

```
You are a document analysis assistant specializing in contract review.

Extract key entities and assess risk factors from the provided document.

## Constraints
1. Only use information present in the document — do not infer or assume
2. Provide confidence scores between 0.0 and 1.0 for each extraction
3. If a field cannot be determined, return null rather than guessing

The user is a legal professional reviewing contracts for compliance.

## Document

[Full document text inserted here]

## Additional Analysis Instructions

[Skill 1 prompt fragment]
[Skill 2 prompt fragment]

## Reference Knowledge

### Standard Contract Clauses
[Content from knowledge:standard-contract-clauses]

### Liability Definitions
[Content from knowledge:liability-definitions]

Force majeure clauses should be flagged as high risk in the current environment

## Examples

Input: "Agreement between Acme Corp and Beta Inc for consulting services..."
Expected output:
{
  "summary": "Consulting services agreement between Acme Corp and Beta Inc...",
  "documentType": "contract",
  "parties": ["Acme Corp", "Beta Inc"],
  "riskLevel": "low",
  "isConfidential": false,
  "confidence": 0.92
}

## Output Format

Return valid JSON with the following fields:
- summary (string): One-paragraph executive summary of the document (max 500 chars)
- documentType (string): Document classification — one of: contract, invoice, proposal, report, letter, memo, email, agreement, statement, patent, trademark, nda, other
- parties (array of strings): Named parties involved in the document
- keyDates (array of objects with date and description): Important dates and deadlines
- riskLevel (string): Overall risk assessment — one of: low, medium, high, critical
- isConfidential (boolean): Whether the document contains confidential or privileged information
- confidence (number, 0-1): Overall confidence in the analysis quality
```

When `structuredOutput: true`, the text-based "Output Format" section is replaced by Azure OpenAI's constrained decoding — the AI is physically unable to return invalid JSON.

---

## $choices Auto-Injection — The Original Motivation

### The Duplication Problem

Today, when configuring a playbook for document classification:

**In the AI prompt** (flat text, manually typed):
> "Classify the document type as one of: contract, invoice, proposal, report, letter, memo..."

**In the UpdateRecord node** (fieldMappings config):
```json
{
  "field": "sprk_documenttype",
  "type": "choice",
  "options": { "contract": 100000000, "invoice": 100000001, "proposal": 100000002, ... }
}
```

These are the **same values in two places**. If someone adds "amendment" to the UpdateRecord options but forgets the prompt, the AI never outputs "amendment". If someone adds "amendment" to the prompt but not the options, the coercion fails silently.

### The Solution

The `$choices` reference on an output field pulls valid values from the downstream node:

```
AI Node output.fields:           UpdateRecord fieldMappings:
┌─────────────────────┐          ┌──────────────────────────┐
│ name: "documentType" │          │ field: "sprk_documenttype"│
│ type: "string"       │ ──$choices──→ │ type: "choice"           │
│ $choices: "downstream│          │ options: {               │
│   :update_doc        │          │   "contract": 100000000, │
│   .sprk_documenttype"│          │   "invoice": 100000001,  │
└─────────────────────┘          │   "proposal": 100000002  │
                                  │ }                        │
                                  └──────────────────────────┘
```

At render time, the PromptSchemaRenderer:
1. Follows the edge graph to find the downstream UpdateRecord node
2. Reads its `fieldMappings` for the matching Dataverse field
3. Extracts the option keys: `["contract", "invoice", "proposal"]`
4. Injects into the prompt: "documentType — one of: contract, invoice, proposal"
5. If `structuredOutput: true`, adds `"enum": ["contract", "invoice", "proposal"]` to the JSON Schema

**Values defined once** in the UpdateRecord config. Flows upstream automatically.

---

## The Three Authoring Levels — Bridging the Gap

### Level 1: Power User (Form-Based)

The PlaybookBuilder shows a structured form when editing an AI node's prompt. No JSON knowledge required:

```
┌─ Prompt Configuration ─────────────────────────────────────┐
│                                                             │
│  Role                                                       │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Document analysis assistant specializing in         │   │
│  │ contract review                                      │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  Task *                                                     │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Extract key entities and assess risk factors from   │   │
│  │ the provided document                               │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  Constraints                                                │
│  ┌──────────────────────────────────────────────┐ [×]      │
│  │ Only use information present in the document │          │
│  ├──────────────────────────────────────────────┤ [×]      │
│  │ Provide confidence scores for each extraction│          │
│  └──────────────────────────────────────────────┘          │
│  [+ Add constraint]                                         │
│                                                             │
│  Output Fields                                              │
│  ┌──────────┬─────────┬──────────────────────────┐         │
│  │ Name     │ Type    │ Description              │         │
│  ├──────────┼─────────┼──────────────────────────┤         │
│  │ summary  │ string  │ One-paragraph summary    │ [×]     │
│  │ docType  │ string  │ Values from: [Update ▼]  │ [×]     │
│  │ parties  │ array   │ Named parties            │ [×]     │
│  │ conf.    │ number  │ Confidence 0-1           │ [×]     │
│  └──────────┴─────────┴──────────────────────────┘         │
│  [+ Add field]                                              │
│                                                             │
│  ☑ Use Structured Output (guaranteed JSON)                  │
│                                                             │
│  [Switch to JSON editor ↔]   [Preview rendered prompt ▶]   │
└─────────────────────────────────────────────────────────────┘
```

**Key behaviors**:
- When an output field type is set to "string" and the field name matches a downstream UpdateRecord choice field, the form auto-suggests adding `$choices`
- "Values from: [node name]" shows resolved choice labels as read-only chips
- Preview button renders the full prompt as the AI would receive it
- The form serializes to JPS JSON behind the scenes — user never sees JSON

### Level 2: Advanced User (JSON Editor)

Toggle "Switch to JSON editor" reveals the raw JPS JSON with:
- JPS JSON Schema validation (red underlines on invalid properties)
- Autocomplete for `$choices` references (lists downstream UpdateRecord nodes by output variable)
- Autocomplete for template variables (lists upstream node output variables)
- Preview tab showing the rendered flat prompt
- Syntax highlighting for `$ref` and `{{template}}` tokens

Level 1 and Level 2 are **the same data** viewed differently. Toggling is lossless. No data is lost switching between form and JSON.

### Level 3: AI Agent (Builder Agent)

The Builder Agent in `BuilderAgentService.cs` creates schema-based prompts via a new tool:

```
User: "Create a playbook that extracts document type and summary,
       then writes them to the document record"

Builder Agent:
  1. addNode("aiAnalysis", "Extract Document Info")
  2. addNode("updateRecord", "Update Document")
  3. createEdge(extract → update)
  4. configure_prompt_schema(extractNode, {
       role: "Document analysis specialist",
       task: "Extract the document type and a concise summary",
       outputFields: [
         { name: "documentType", type: "string", $choices: "downstream:update_doc.sprk_documenttype" },
         { name: "summary", type: "string", description: "One-paragraph summary" }
       ],
       structuredOutput: true
     })
  5. updateNodeConfig(updateNode, {
       entityLogicalName: "sprk_document",
       fieldMappings: [
         { field: "sprk_documenttype", type: "choice", value: "{{output_extract.output.documentType}}", options: {...} },
         { field: "sprk_filesummary", type: "string", value: "{{output_extract.output.summary}}" }
       ]
     })
```

The agent produces the same JPS format that Level 1 and 2 produce. `metadata.authorLevel = 3` tracks provenance.

### Why This Bridges the Gap

| Without JPS | With JPS |
|------------|---------|
| Human types a long prompt, hopes it works | Human fills out 4 fields: role, task, constraints, output fields |
| Must manually list valid Choice values | `$choices` auto-injects from downstream node config |
| No validation until runtime failure | Canvas-time validation: "output field X not consumed by downstream node" |
| AI Agent creates nodes but can't write good prompts | Agent uses same structured format, creates optimized schemas |
| Changing a Choice value = edit prompt AND UpdateRecord config | Change in UpdateRecord options → auto-propagates to AI prompt |
| Can't use structured output mode (don't know the schema) | `output.fields` → JSON Schema → guaranteed valid JSON from Azure OpenAI |
| Flat text can't be analyzed or optimized | Structured schema enables analytics, A/B testing, automated optimization |

---

## Storage and Data Model

### Primary Storage: `sprk_systemprompt` (No Change)

JPS JSON is stored in the **existing** `sprk_systemprompt` Multi-Line Text field on `sprk_analysisaction`. This field is nvarchar(max) — no size constraints.

**Why not a new field?**
- `sprk_systemprompt` already contains the primary AI instruction
- JPS is a superset of flat text — format detection handles both
- No Dataverse solution upgrade, no schema migration, no new columns
- Existing code that reads `sprk_systemprompt` gets the same string

### Node-Level Override: `sprk_configjson`

For cases where a specific playbook node needs to extend the Action's base prompt:

```json
{
  "__canvasNodeId": "node_1",
  "__actionType": 0,
  "promptSchemaOverride": {
    "instruction": {
      "constraints": ["Additional constraint specific to this playbook"]
    },
    "output": {
      "fields": [
        { "name": "extraField", "type": "string", "description": "Playbook-specific output" }
      ]
    }
  }
}
```

The renderer **merges** the override on top of the Action's base schema:
- `constraints` arrays are concatenated
- `output.fields` arrays are concatenated (override fields append to base)
- Scalar fields (role, task) are replaced if present in override

This allows a reusable Action scope (e.g., "General Document Analysis") to be customized per-node without modifying the shared scope.

### TypeScript Types

```typescript
interface PromptSchema {
  $schema?: string;
  $version?: number;
  instruction: {
    role?: string;
    task: string;
    constraints?: string[];
    context?: string;
  };
  input?: {
    document?: { required?: boolean; maxLength?: number; };
    priorOutputs?: Array<{ variable: string; fields: string[]; }>;
    parameters?: Record<string, unknown>;
  };
  output?: {
    fields: OutputFieldDefinition[];
    structuredOutput?: boolean;
  };
  scopes?: {
    $skills?: Array<string | { $ref: string }>;
    $knowledge?: Array<{ $ref: string; as?: string } | { inline: string }>;
  };
  examples?: Array<{ input: string; output: Record<string, unknown> }>;
  metadata?: {
    author?: string;
    authorLevel?: number;
    description?: string;
    tags?: string[];
  };
}

interface OutputFieldDefinition {
  name: string;
  type: "string" | "number" | "boolean" | "array" | "object";
  description?: string;
  enum?: string[];
  $choices?: string;  // "downstream:nodeVar.fieldName"
  items?: object;     // For array type
  maxLength?: number;
  minimum?: number;
  maximum?: number;
}
```

### C# Model

```csharp
public sealed record PromptSchema
{
    public string? Schema { get; init; }
    public int Version { get; init; } = 1;
    public required InstructionSection Instruction { get; init; }
    public InputSection? Input { get; init; }
    public OutputSection? Output { get; init; }
    public ScopesSection? Scopes { get; init; }
    public IReadOnlyList<ExampleEntry>? Examples { get; init; }
    public MetadataSection? Metadata { get; init; }
}

public sealed record InstructionSection
{
    public string? Role { get; init; }
    public required string Task { get; init; }
    public IReadOnlyList<string>? Constraints { get; init; }
    public string? Context { get; init; }
}

public sealed record OutputSection
{
    public required IReadOnlyList<OutputFieldDefinition> Fields { get; init; }
    public bool StructuredOutput { get; init; }
}

public sealed record OutputFieldDefinition
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? Enum { get; init; }
    public string? Choices { get; init; }  // $choices reference
    // ... additional constraints
}
```

---

## Validation

### Canvas-Time Validation (PlaybookBuilder UI)

When the user saves or syncs the canvas, validation runs on all AI nodes:

| Rule | Description | Severity |
|------|-------------|----------|
| **Output coverage** | Every `{{output_X.output.field}}` template in downstream nodes must have a matching field in `output.fields` | Warning |
| **Choice consistency** | If downstream UpdateRecord has `options` for a field, the AI node should have `$choices` or `enum` for it | Warning |
| **Type compatibility** | `output.fields[].type` should match downstream UpdateRecord `fieldMappings[].type` | Warning |
| **Missing task** | `instruction.task` is required when using JPS format | Error |
| **Unresolvable $choices** | `$choices` references a node that doesn't exist or has no matching field | Error |

### Pre-Execution Validation (Server-Side)

Before executing a playbook, `PlaybookOrchestrationService.ValidateAsync` checks:

| Rule | Description |
|------|-------------|
| **$choices resolvable** | All `$choices` references can find their downstream nodes and option maps |
| **$knowledge resolvable** | All `$ref: "knowledge:name"` references match existing Dataverse records |
| **Schema parseable** | JPS JSON is valid and has required fields |
| **Structured output compatible** | If `structuredOutput: true`, `output.fields` can generate valid JSON Schema |

---

## Builder Agent Integration

### New Tool: `configure_prompt_schema`

Added to `BuilderToolDefinitions.cs`:

```json
{
  "name": "configure_prompt_schema",
  "description": "Configure the structured prompt schema for an AI node. Creates an optimized JPS with role, task, constraints, and typed output fields. When output fields reference downstream UpdateRecord choice fields, automatically adds $choices references.",
  "parameters": {
    "type": "object",
    "properties": {
      "nodeId": { "type": "string" },
      "role": { "type": "string" },
      "task": { "type": "string" },
      "constraints": { "type": "array", "items": { "type": "string" } },
      "outputFields": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "name": { "type": "string" },
            "type": { "type": "string", "enum": ["string", "number", "boolean", "array", "object"] },
            "description": { "type": "string" },
            "enum": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["name", "type"]
        }
      },
      "useStructuredOutput": { "type": "boolean" },
      "autoWireChoices": { "type": "boolean" }
    },
    "required": ["nodeId", "task", "outputFields"]
  }
}
```

### Agent System Prompt Updates

The `PlaybookBuilderSystemPrompt.cs` is extended with JPS awareness:

```
When creating AI Analysis nodes, ALWAYS use configure_prompt_schema to set structured prompts.
This ensures:
- Output fields are explicitly defined (enables downstream validation)
- Choice fields auto-wire to downstream UpdateRecord options
- Structured output mode produces guaranteed valid JSON

When the user describes what they want to extract or analyze:
1. Create the AI Analysis node
2. Create downstream nodes (UpdateRecord, DeliverOutput, etc.)
3. Connect them with edges
4. Call configure_prompt_schema with output fields matching downstream expectations
5. Set autoWireChoices=true to auto-connect $choices references
```

---

## Implementation Phases

This system must be implemented completely to be effective. Partial implementation creates inconsistency — some prompts structured, some not; some validated, some not. Each phase adds capability, but the system only delivers its full value when all phases are complete.

### Phase 1: Schema Format + Renderer (Server Only)

**Goal**: JPS renders identically to flat text for existing prompts. New prompts can use schema format.

**New files**:
- `Services/Ai/Models/PromptSchema.cs` — C# records for JPS format
- `Services/Ai/PromptSchemaRenderer.cs` — Core renderer with format detection

**Modified files**:
- `Services/Ai/Handlers/GenericAnalysisHandler.cs` — Replace `BuildExecutionPrompt` (L408-456) with `IPromptSchemaRenderer.Render()` call
- `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` — Pass renderer to handler context

**Scope**:
- Format detection (flat text vs JPS JSON vs empty)
- `instruction` section rendering (role + task + constraints)
- `output.fields` → text-based instructions (no structured output yet)
- `examples` rendering as few-shot sections
- Backward compatibility: flat text path produces identical output to today
- DI registration for `IPromptSchemaRenderer`
- Unit tests for rendering + backward compatibility

**Verification**: Run existing playbooks → prompt output is byte-for-byte identical.

### Phase 2: PlaybookBuilder UI (Level 1 + Level 2)

**Goal**: Power users can create prompts via form; advanced users can edit JSON directly.

**New files**:
- `components/properties/PromptSchemaForm.tsx` — Level 1 form (role, task, constraints, output fields)
- `components/properties/PromptSchemaEditor.tsx` — Level 2 JSON editor with preview

**Modified files**:
- `components/properties/NodePropertiesForm.tsx` — Add "Prompt" accordion section for AI nodes
- `types/canvas.ts` — Add `promptSchema?: PromptSchema` to `PlaybookNodeData`
- `services/playbookNodeSync.ts` — Serialize `promptSchema` in `buildConfigJson` for AI node types
- `stores/canvasStore.ts` — Handle `promptSchema` in node data updates

**Scope**:
- Form fields: role (textarea), task (textarea, required), constraints (tag list), output fields (repeating row)
- Output field row: name (input), type (dropdown), description (input), remove button
- Toggle between form and JSON editor views (lossless)
- Prompt preview (render the prompt as text to show what the AI will receive)
- TypeScript `PromptSchema` type definitions

### Phase 3: Structured Output + $choices

**Goal**: Choice fields auto-wire. Structured output guarantees valid JSON.

**Modified files**:
- `Services/Ai/PromptSchemaRenderer.cs` — Add `$choices` resolution and JSON Schema generation
- `Services/Ai/PlaybookOrchestrationService.cs` — Collect downstream node ConfigJson for `$choices`
- `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` — Pass downstream info through to renderer
- `Services/Ai/Handlers/GenericAnalysisHandler.cs` — Use `RenderedPrompt.JsonSchema` with `GetStructuredCompletionAsync<T>`
- `components/properties/PromptSchemaForm.tsx` — $choices UI (show "Values from: [node]" with resolved labels)
- `services/playbookNodeSync.ts` — Canvas-side validation for output fields vs downstream references

**Scope**:
- `output.fields` → JSON Schema Draft-07 conversion
- `GetStructuredCompletionAsync<T>` integration (guaranteed valid JSON from Azure OpenAI)
- `$choices` reference resolution from downstream UpdateRecord `fieldMappings[].options`
- `DownstreamNodeInfo` collection during orchestration
- Canvas-side validation warnings (output coverage, choice consistency, type compatibility)
- Server-side pre-execution validation

### Phase 4: Builder Agent Integration (Level 3)

**Goal**: Builder Agent creates optimized schema-based prompts.

**Modified files**:
- `Services/Ai/Builder/BuilderToolDefinitions.cs` — Add `configure_prompt_schema` tool
- `Services/Ai/Builder/BuilderToolExecutor.cs` — Execute `configure_prompt_schema`
- `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs` — JPS-aware system prompt updates

**Scope**:
- `configure_prompt_schema` tool definition with output field typing
- Tool executor generates full JPS JSON from simplified parameters
- `autoWireChoices` flag traverses canvas edges to auto-connect `$choices`
- System prompt updated to always use structured prompts for AI nodes
- `metadata.authorLevel = 3` for provenance tracking

### Phase 5: Cross-Scope References + Advanced

**Goal**: Full scope composition with named references and analytics.

**Scope**:
- `$knowledge` named references (resolve by `sprk_name` in Dataverse)
- `$skill` named references
- Contextual placement with `as` labels (reference, definitions, examples)
- `promptSchemaOverride` merge in `sprk_configjson` for node-level customization
- Prompt schema template library (pre-built schemas for common patterns)
- Prompt performance analytics (which schemas produce best AI output)
- Schema versioning and upgrade tooling

---

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| **Breaking existing playbooks** | Format detection ensures flat text always takes the legacy path. Zero migration. |
| **JPS complexity overwhelms power users** | Level 1 form hides all JSON. User fills in role, task, constraints — system handles the rest. |
| **$choices creates circular dependency** | $choices is resolved at render time, not stored as data. No circular references possible — it's a one-way lookup. |
| **JSON Schema constrained decoding limits AI flexibility** | `structuredOutput` is opt-in. Users can keep free-form output. |
| **Builder Agent generates bad prompts** | JPS validates structure (required fields, types). Bad content is a prompt quality issue, not a schema issue. |
| **Partial adoption creates inconsistency** | Commit to all phases. Each playbook's prompts are independently JPS or flat text — no system-wide inconsistency. |

---

## Success Criteria

1. **Existing playbooks unchanged** — Zero regressions, identical prompt output for all flat-text prompts
2. **Power users can create effective prompts without prompt engineering expertise** — Form guides them through role, task, constraints, output fields
3. **Choice values defined once** — UpdateRecord options automatically appear in AI prompts via `$choices`
4. **Structured output mode works** — AI returns guaranteed valid JSON matching the schema
5. **Canvas-time validation catches mismatches** — Missing output fields, unresolvable references, type mismatches surfaced before execution
6. **Builder Agent creates complete playbooks** — Agent generates structured prompts, not just nodes and edges
7. **Three authoring levels produce identical runtime behavior** — Form, JSON editor, and agent output all render to the same prompt
