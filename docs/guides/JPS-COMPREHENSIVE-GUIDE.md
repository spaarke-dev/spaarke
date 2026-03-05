# JPS Comprehensive Guide

> **Version**: 1.0
> **Date**: March 4, 2026
> **Status**: Production
> **Author**: Spaarke Engineering
> **Audience**: Developers, AI engineers, prompt authors

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
    output.fields[].enum populated from downstream node display names
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

## 3. JPS JSON Schema Reference

For the complete authoring reference with examples and best practices, see [JPS Authoring Guide](JPS-AUTHORING-GUIDE.md).

### Top-Level Structure

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": { },
  "input": { },
  "output": { },
  "scopes": { },
  "examples": [ ],
  "metadata": { }
}
```

### Field Reference

| Field | Type | Required | Default | Purpose |
|-------|------|----------|---------|---------|
| `$schema` | string | Yes | — | Schema URI; triggers JPS format detection |
| `$version` | number | Yes | `1` | Schema version |
| `instruction` | object | Yes | — | Core prompt: role, task, context, constraints |
| `input` | object | No | — | Input field definitions and placeholders |
| `output` | object | No | — | Output field definitions, types, structured output config |
| `scopes` | object | No | — | Named knowledge and skill references via `$ref` |
| `examples` | array | No | — | Few-shot input/output pairs |
| `metadata` | object | Yes | — | Author, description, tags for discoverability |

### instruction

| Field | Type | Purpose |
|-------|------|---------|
| `role` | string | System persona — who the AI is |
| `task` | string (required) | What the AI must do — primary objective |
| `context` | string | Why and where this runs — workflow context |
| `constraints` | string[] | MUST/MUST NOT guardrails for output quality |

### input

| Field | Type | Purpose |
|-------|------|---------|
| `document.required` | boolean | Whether document text must be provided |
| `document.maxLength` | number | Maximum character length |
| `document.placeholder` | string | Template variable (e.g., `{{document.extractedText}}`) |
| `priorOutputs` | array | Upstream node output dependencies (variable, fields, description) |
| `parameters` | object | Additional key-value parameters with template support |

### output

| Field | Type | Purpose |
|-------|------|---------|
| `fields` | array | Output field definitions (see below) |
| `structuredOutput` | boolean | When `true`, generates JSON Schema for constrained decoding |

**Output field properties**:

| Property | Type | Purpose |
|----------|------|---------|
| `name` | string | Dataverse column name the result maps to |
| `type` | string | `"string"`, `"number"`, `"boolean"`, `"array"`, `"object"` |
| `description` | string | What the model should generate (becomes schema description) |
| `maxLength` | number | Max characters (string fields) |
| `enum` | string[] | Allowed values — model must pick from this list |
| `$choices` | string | Dynamic enum from downstream nodes (format: `"downstream:{outputVariable}.{fieldName}"`) |
| `items` | object | Schema for array items when type is `"array"` |
| `minimum` / `maximum` | number | Numeric range constraints |

### scopes

| Field | Type | Purpose |
|-------|------|---------|
| `$knowledge` | array | Knowledge references with `$ref` and optional `as` label |
| `$skills` | array | Skill references with `$ref` |

### metadata

| Field | Type | Purpose |
|-------|------|---------|
| `author` | string | Creator (`"migration"`, username, `"builder-agent"`) |
| `authorLevel` | number | `0` = migration, `1` = admin, `2` = user, `3` = AI agent |
| `createdAt` | string | ISO 8601 timestamp |
| `description` | string | Human-readable purpose description |
| `tags` | string[] | Categorization tags |

---

## 4. Features Deep Dive

### Template Parameters

Any string field in `instruction` supports `{{paramName}}` placeholders. Parameters are supplied at runtime via the playbook node's `ConfigJson.templateParameters`:

```json
// JPS definition
{ "instruction": { "task": "Analyze under {{jurisdiction}} law." } }

// Playbook node ConfigJson
{ "templateParameters": { "jurisdiction": "Delaware" } }
```

**Behavior**: Matched parameters are replaced. Unmatched `{{paramName}}` placeholders remain as-is (no error). Parameters are applied after override merge but before rendering.

### $ref Resolution

Scopes reference external knowledge and skills by name. The pipeline resolves them against Dataverse at render time:

| Prefix | Resolves To | Dataverse Entity | Resolver Method |
|--------|-------------|------------------|-----------------|
| `knowledge:{name}` | Knowledge record content | `sprk_analysisknowledge` | `GetKnowledgeByNameAsync()` |
| `skill:{name}` | Skill record content | `sprk_analysisskill` | `GetSkillByNameAsync()` |

`JpsRefResolver` extracts references statically (no DI). `AiAnalysisNodeExecutor` then resolves each name via `IScopeResolverService`. The `as` label on knowledge refs controls the rendered section heading (e.g., `"reference"`, `"definitions"`).

### $choices Resolution

Output fields with `"$choices"` or `["$choices"]` in their enum are dynamically populated from downstream playbook node display names. This enables classification-to-routing patterns where the model selects a downstream path.

```json
{
  "name": "sprk_routingdecision",
  "type": "string",
  "enum": ["$choices"]
}
```

At render time, `"$choices"` is replaced with actual display names from connected downstream nodes (e.g., `["IP Review", "Compliance Review", "Standard Review"]`).

The `$choices` property supports a more explicit format: `"downstream:{outputVariable}.{fieldName}"` which resolves from a specific downstream node's field mapping options.

### Override Merge

`PromptSchemaOverrideMerger` merges a node's `promptSchemaOverride` into the base Action's JPS definition:

| Section | Default Behavior | `__replace` Directive |
|---------|------------------|-----------------------|
| `instruction.role` | Override replaces base | N/A (scalar) |
| `instruction.task` | Override replaces base | N/A (scalar) |
| `instruction.context` | Override replaces base | N/A (scalar) |
| `instruction.constraints` | Override appends to base | `"__replace"` as first element replaces entirely |
| `output.fields` | Override appends to base | Field named `"__replace"` replaces entirely |
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

## 5. Creating a New JPS Action (Step-by-Step)

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

Identify what structured data you need. For each field, specify `name` (Dataverse column), `type`, `description`, and optional `enum` or `maxLength`. Use `"$choices"` for routing fields.

### Step 3: Add Scopes (if needed)

Reference shared knowledge or skills via `$ref`. Prefer scopes over inline knowledge — shared records are maintained once and used everywhere. Keep scope count minimal (each adds to token count).

### Step 4: Add Template Parameters

If the same action serves multiple configurations (e.g., different jurisdictions), use `{{paramName}}` placeholders in instruction fields. Document expected parameters in `metadata.description`.

### Step 5: Add Examples

Include 1-3 few-shot examples for complex output formats. Examples should cover edge cases and match `output.fields` names exactly.

### Step 6: Validate JSON

Use the validation checklist from the [JPS Authoring Guide](JPS-AUTHORING-GUIDE.md#validation-checklist):
- `$schema` is `"https://spaarke.com/schemas/prompt/v1"`
- `instruction` has at least `task`
- All `output.fields` have `name`, `type`, and `description`
- `$ref` names match existing Dataverse records
- No production prompt content in documentation (ADR-015)

### Step 7: Seed to Dataverse

```powershell
# Dry run — preview changes without writing
.\scripts\Seed-JpsActions.ps1 -WhatIf

# Seed with backup of existing prompts
.\scripts\Seed-JpsActions.ps1 -BackupPath ./backups

# Seed specific environment
.\scripts\Seed-JpsActions.ps1 -Environment test
```

### Step 8: Test End-to-End

1. Trigger the playbook containing the action
2. Verify format detection selects the JPS path (check logs for `PromptSchemaRenderer`)
3. Verify structured output matches the expected JSON Schema
4. Verify scope resolution succeeded (check for missing `$ref` warnings)
5. Test with template parameters if applicable

---

## 6. Playbook Design Patterns

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

## 7. Migration Guide (Hardcoded to JPS)

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

## 8. Deployment

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

JPS JSON files are stored in `projects/*/notes/jps-conversions/` and mapped to Action records by `sprk_name`.

### Verification

1. Query `sprk_analysisactions` for records with `sprk_systemprompt` starting with `{`
2. Trigger a playbook and check logs for JPS path selection
3. Verify scope resolution succeeded (check for missing `$ref` warnings)

### Rollback

Restore from backup files (`-BackupPath`), re-seed from git history, or remove JPS from the record (dual-path falls back to flat text automatically).

---

## 9. Troubleshooting

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

**Symptom**: Enum contains literal `"$choices"` instead of node names.

| Cause | Fix |
|-------|-----|
| No downstream nodes connected | Connect at least one downstream node in the playbook |
| Downstream nodes have no display name | Set display names on connected downstream nodes |
| `DownstreamNodeInfo[]` not passed to renderer | Verify `AiAnalysisNodeExecutor` passes downstream info |

### Override Merge Unexpected Results

**Symptom**: Override constraints appended instead of replaced (or vice versa).

| Cause | Fix |
|-------|-----|
| Missing `"__replace"` directive | Add `"__replace"` as first element in constraints array to replace |
| `"__replace"` not exact string match | Must be exactly `"__replace"` (case-sensitive, no whitespace) |
| Override has empty `task` field | Empty task is treated as "not provided" — base task is kept |

### Structured Output Validation Errors

**Symptom**: OpenAI rejects the JSON Schema or returns malformed output.

| Cause | Fix |
|-------|-----|
| Unsupported `type` value | Use `"string"`, `"number"`, `"boolean"`, `"array"`, or `"object"` |
| Array field missing `items` schema | Add `items` with type definition for array fields |
| `enum` values contain `"$choices"` unreplaced | Verify `$choices` resolution ran (see above) |

---

## 10. References

### Internal Documentation

| Document | Purpose |
|----------|---------|
| [JPS Authoring Guide](JPS-AUTHORING-GUIDE.md) | Detailed authoring reference with examples and best practices |
| [AI Architecture Guide - Section 19](SPAARKE-AI-ARCHITECTURE.md#19-json-prompt-schema-jps-pipeline-2026-03-04) | Pipeline internals, component diagram, migration strategy |
| [How to Create AI Playbook Scopes](HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md) | Creating knowledge and skill records for `$ref` resolution |

### Example JPS Files

| File | Complexity | Features Demonstrated |
|------|------------|----------------------|
| `projects/ai-json-prompt-schema-system/notes/jps-conversions/document-profiler.json` | Medium | Multi-field output, enum, examples, structuredOutput |
| `projects/ai-json-prompt-schema-system/notes/jps-conversions/clause-analyzer.json` | High | Array output with nested objects, scopes with `$ref` and `as` labels |

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

*Document Owner: Spaarke Engineering*
*Last Updated: March 4, 2026*
