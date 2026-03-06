# jps-validate

---
description: Validate a JPS JSON file against schema and test rendering
tags: [ai, jps, testing, validation, prompt-schema]
techStack: [azure-openai, aspnet-core]
appliesTo: ["validate JPS", "check JPS", "test JPS definition", "validate prompt schema"]
alwaysApply: false
---

## Purpose

**Tier 1 Component Skill** — Validates a JPS (JSON Prompt Schema) JSON file against the expected schema, checks for common errors, and optionally tests rendering via the PromptSchemaRenderer. Reports pass/fail for each check with actionable fixes.

**Why This Skill Exists**:
- Malformed JPS causes silent runtime failures (format detection falls back to flat-text)
- Missing required fields produce incomplete prompts
- Incorrect $ref or $choices syntax causes resolution failures at runtime
- Validation before deployment prevents production issues

## Applies When

- User says "validate JPS", "check JPS", "test JPS definition"
- Before seeding JPS to Dataverse (pre-deployment check)
- After creating or editing a JPS file
- Explicitly invoked with `/jps-validate {file-path}`
- NOT for creating new JPS (use `jps-action-create`)

---

## Workflow

### Step 1: Load JPS File

```
IF user provides file path:
  LOAD file from path
ELSE IF user provides JPS content in conversation:
  PARSE content directly
ELSE:
  ASK user for file path or content
  SUGGEST: "Look in projects/ai-json-prompt-schema-system/notes/jps-conversions/"
```

### Step 2: JSON Syntax Validation

```
CHECK 1: Valid JSON
  - Parse with JSON parser
  - Report line/column of syntax errors if invalid
  - STOP if not valid JSON (remaining checks require valid JSON)
```

### Step 3: Schema Structure Validation

Run these checks (report each as PASS/FAIL/WARN):

```
REQUIRED FIELDS:
  ✅/❌ CHECK 2:  Has "$schema" field
  ✅/❌ CHECK 3:  $schema value is "https://spaarke.com/schemas/prompt/v1"
  ✅/❌ CHECK 4:  Has "instruction" section
  ✅/❌ CHECK 5:  instruction.role is non-empty string
  ✅/❌ CHECK 6:  instruction.task is non-empty string

OPTIONAL BUT RECOMMENDED:
  ✅/⚠️ CHECK 7:  Has "output" section with at least 1 field
  ✅/⚠️ CHECK 8:  Has "metadata" section with description
  ✅/⚠️ CHECK 9:  Has at least 1 "examples" entry
  ✅/⚠️ CHECK 10: instruction.constraints is array (not string)
```

### Step 4: Output Field Validation

```
FOR each field in output.fields:
  ✅/❌ CHECK 11: Has "name" (non-empty string)
  ✅/❌ CHECK 12: Has "type" (one of: string, number, boolean, array)
  ✅/❌ CHECK 13: Has "description" (non-empty string)
  ✅/⚠️ CHECK 14: "maxLength" set for string fields
  ✅/⚠️ CHECK 15: "enum" values are strings (if present)

IF field has "$choices":
  ✅/❌ CHECK 16: Format uses a supported prefix:
    - "lookup:{entity}.{field}" (Dataverse reference entity records)
    - "optionset:{entity}.{attribute}" (single-select choice/picklist)
    - "multiselect:{entity}.{attribute}" (multi-select picklist)
    - "boolean:{entity}.{attribute}" (two-option boolean labels)
    - "downstream:{outputVariable}.{fieldName}" (downstream node routing)
  ✅/❌ CHECK 16a: Reference has two parts separated by "." after the prefix
  ✅/⚠️ CHECK 17: No static "enum" alongside "$choices" (choices override enum)
```

### Step 5: Scope Reference Validation

```
IF "scopes" section exists:
  FOR each entry in scopes.$knowledge:
    ✅/❌ CHECK 18: $ref uses "knowledge:{name}" prefix
    ✅/⚠️ CHECK 19: Has "label" for display purposes

  FOR each entry in scopes.$skills:
    ✅/❌ CHECK 20: $ref uses "skill:{name}" prefix
```

### Step 6: Template Parameter Validation

```
SCAN instruction.role, instruction.task, instruction.context, instruction.constraints[]:
  FOR each {{paramName}} found:
    ✅/⚠️ CHECK 21: Parameter name is alphanumeric + underscores
    ✅/⚠️ CHECK 22: Parameter is documented in metadata or has obvious purpose
```

### Step 7: Format Detection Test

```
CHECK 23: IsJpsFormat() would detect this as JPS
  - Content starts with '{' (after trimming whitespace)
  - Content contains '"$schema"'
  - If BOTH true → PASS (JPS path will be used)
  - If EITHER false → FAIL (will fall back to flat-text path!)
```

### Step 8: Render Test (Optional)

```
IF user requests render test OR file passes all checks:
  LOAD: src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs

  VERIFY the JPS would render correctly:
    - Check instruction sections would produce non-empty prompt text
    - Check output fields would produce format section or JSON Schema
    - Check structuredOutput flag matches expected behavior

  NOTE: Full render test requires running dotnet test with the renderer.
  SUGGEST: "Run existing tests to verify: dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter PromptSchemaRenderer"
```

### Step 9: Report Results

Generate validation report.

---

## Output Format

```
🔍 JPS Validation Report: {filename}

📋 Schema Checks:
  ✅ Valid JSON
  ✅ $schema field present
  ✅ instruction.role defined
  ✅ instruction.task defined
  ✅ {N} output fields valid
  ⚠️ No examples section (recommended)
  ❌ $choices format invalid: "result" (missing prefix and .fieldName)

📊 Summary:
  Passed:  {N}
  Warnings: {N}
  Failed:  {N}

🔧 Fixes Required:
  1. ❌ Fix $choices format: use a supported prefix — e.g., "lookup:entity.field", "optionset:entity.attr", or "downstream:var.field"

⚠️ Recommendations:
  1. Add examples section with at least 1 input/output pair
  2. Add metadata.description for documentation

{IF all passed}
✅ JPS is valid and ready for deployment!
  Seed to Dataverse: scripts/Seed-JpsActions.ps1
```

---

## Examples

### Example 1: Valid JPS file

**Input:**
```
User: "validate JPS projects/ai-json-prompt-schema-system/notes/jps-conversions/document-profiler.json"
```

**Output:**
```
🔍 JPS Validation Report: document-profiler.json

📋 Schema Checks:
  ✅ Valid JSON
  ✅ $schema: https://spaarke.com/schemas/prompt/v1
  ✅ instruction.role: defined (172 chars)
  ✅ instruction.task: defined (248 chars)
  ✅ instruction.constraints: 8 items
  ✅ instruction.context: defined
  ✅ output: 7 fields, structuredOutput: true
  ✅ examples: 1 entry
  ✅ metadata: complete
  ✅ Format detection: IsJpsFormat() = true

📊 Summary: 10 passed, 0 warnings, 0 failed

✅ JPS is valid and ready for deployment!
```

### Example 2: JPS with errors

**Input:**
```
User: "check this JPS: { \"instruction\": { \"role\": \"analyst\" } }"
```

**Output:**
```
🔍 JPS Validation Report: (inline)

📋 Schema Checks:
  ✅ Valid JSON
  ❌ Missing "$schema" field
  ✅ instruction.role: defined
  ❌ Missing instruction.task
  ❌ No output section
  ⚠️ No examples section
  ⚠️ No metadata section
  ❌ Format detection: IsJpsFormat() = false (missing $schema)

📊 Summary: 2 passed, 2 warnings, 4 failed

🔧 Fixes Required:
  1. Add "$schema": "https://spaarke.com/schemas/prompt/v1"
  2. Add instruction.task with specific analysis instructions
  3. Add output section with at least 1 field
```

---

## Error Handling

| Situation | Response |
|-----------|----------|
| File not found | Check path, suggest jps-conversions/ directory |
| Binary/non-text file | Report error, ask for correct file |
| Valid JSON but not JPS | Explain JPS requirements, offer to convert |
| All checks pass | Confirm ready for deployment |

---

## Related Skills

- `jps-action-create` — Create new JPS definitions
- `jps-playbook-design` — Design multi-node playbooks
- `dataverse-deploy` — Deploy validated JPS to Dataverse

---

## Tips for AI

- Always run the format detection check (Step 7) — this is the most common failure mode
- The `$schema` field is critical: without it, IsJpsFormat() returns false and the entire JPS is ignored
- Report ALL issues at once — don't stop at the first failure
- Offer auto-fix suggestions for every failed check
- For $choices validation, verify the prefix is one of: `lookup:`, `optionset:`, `multiselect:`, `boolean:`, `downstream:` and the value after the prefix contains exactly one `.` separator (e.g., `entity.field`)
