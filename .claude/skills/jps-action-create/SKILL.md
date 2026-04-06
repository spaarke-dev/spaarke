# jps-action-create

---
description: Create a new JPS definition for an Analysis Action
tags: [ai, jps, playbook, prompt-schema, action]
techStack: [azure-openai, aspnet-core, dataverse]
appliesTo: ["create JPS action", "new JPS definition", "create analysis action", "new playbook action"]
alwaysApply: false
---

## Purpose

**Tier 1 Component Skill** — Creates a new JSON Prompt Schema (JPS) definition for a Dataverse Analysis Action. Generates valid JPS JSON with instruction, output fields, scopes, template parameters, and examples following the established schema and patterns.

**Why This Skill Exists**:
- JPS definitions follow a precise schema — missing fields or wrong types cause runtime failures
- Knowledge of $ref, $choices, structuredOutput patterns requires domain expertise
- Consistent quality across all JPS definitions ensures pipeline reliability

## Applies When

- User says "create JPS action", "new JPS definition", "create analysis action", "new playbook action"
- Explicitly invoked with `/jps-action-create`
- User describes an analysis task that should become a JPS-backed Action
- NOT for designing multi-node playbooks (use `jps-playbook-design` instead)

---

## Workflow

### Step 1: Gather Requirements

Ask the user (use AskUserQuestion or conversation):

1. **What does this action analyze?** (e.g., "extract dates from contracts", "classify document types")
2. **What output fields are needed?** (field names, types, descriptions)
3. **Does it need structured output?** (JSON Schema for constrained decoding — recommended for extraction tasks)
4. **Does it need shared knowledge scopes?** (reusable context like standard clause libraries)
5. **Does it need template parameters?** (runtime customization like `{{jurisdiction}}`)
6. **Does it need dynamic enum values?** (`$choices` from Dataverse lookups, option sets, or downstream routing nodes)

### Step 2: Load Context

```
LOAD knowledge files:
  - docs/guides/JPS-AUTHORING-GUIDE.md (schema reference)


LOAD 2-3 example JPS files as patterns:
  - projects/ai-json-prompt-schema-system/notes/jps-conversions/document-profiler.json (simple extraction)
  - projects/ai-json-prompt-schema-system/notes/jps-conversions/clause-analyzer.json (complex with scopes)

SELECT the closest pattern match for user's requirements.
```

### Step 3: Generate JPS JSON

Build the JPS following this structure:

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,

  "instruction": {
    "role": "[Expert role description]",
    "task": "[Specific task the AI must perform]",
    "constraints": [
      "[Constraint 1 — be specific and actionable]",
      "[Constraint 2]"
    ],
    "context": "[Optional: situational context for the AI]"
  },

  "input": {
    "document": {
      "required": true,
      "maxLength": 100000,
      "placeholder": "{{document.extractedText}}"
    }
  },

  "output": {
    "fields": [
      {
        "name": "field_name",
        "type": "string|number|boolean|array",
        "description": "[What this field should contain]",
        "maxLength": 5000,
        "enum": ["option1", "option2"],
        "$choices": "lookup:{entity}.{field} | optionset:{entity}.{attr} | multiselect:{entity}.{attr} | boolean:{entity}.{attr} | downstream:{outputVar}.{field}"
      }
    ],
    "structuredOutput": true
  },

  "scopes": {
    "$knowledge": [
      { "$ref": "knowledge:{name}", "label": "display name" }
    ],
    "$skills": [
      { "$ref": "skill:{name}" }
    ]
  },

  "examples": [
    {
      "input": "[Sample input text]",
      "output": { "field_name": "[Expected output value]" }
    }
  ],

  "metadata": {
    "author": "[author]",
    "authorLevel": 0,
    "createdAt": "[ISO date]",
    "description": "[What this action does]",
    "tags": ["tag1", "tag2"]
  }
}
```

**Rules**:
- `instruction.role` and `instruction.task` are REQUIRED
- Output field `name` should use `sprk_` prefix for Dataverse columns
- Use `structuredOutput: true` for extraction tasks (generates JSON Schema)
- Use `structuredOutput: false` for freeform analysis (generates text format section)
- Include at least 1 example for few-shot guidance
- ADR-015: Do NOT log actual prompts — examples should use generic/placeholder content

### Step 4: Validate

Run validation checks:
- [ ] Valid JSON (parse without errors)
- [ ] Has `$schema` field set to `https://spaarke.com/schemas/prompt/v1`
- [ ] Has `instruction.role` (non-empty)
- [ ] Has `instruction.task` (non-empty)
- [ ] Has at least 1 output field
- [ ] Output field types are valid: string, number, boolean, array
- [ ] $ref values use correct prefix: `knowledge:` or `skill:`
- [ ] $choices values use a supported prefix: `lookup:`, `optionset:`, `multiselect:`, `boolean:`, or `downstream:`
- [ ] Template parameters use `{{paramName}}` syntax
- [ ] At least 1 example provided
- [ ] Metadata has description and tags

### Step 5: Save

```
SAVE to: projects/ai-json-prompt-schema-system/notes/jps-conversions/{action-name}.json

ACTION-NAME convention:
  - Lowercase, kebab-case
  - Descriptive: document-profiler, clause-analyzer, risk-detector
  - Match the Analysis Action's sprk_name field
```

### Step 6: Offer Next Steps

Ask user:
- Add this action to `scripts/Seed-JpsActions.ps1` mapping? (for deployment)
- Create a playbook that uses this action? (invoke `jps-playbook-design`)
- Validate the JPS with a render test? (invoke `jps-validate`)

---

## Conventions

- Field names use `sprk_` prefix for Dataverse column mapping
- JPS files stored in `projects/ai-json-prompt-schema-system/notes/jps-conversions/`
- One JPS file per Analysis Action
- Examples should be realistic but not contain actual sensitive data (ADR-015)
- Constraints should be specific and actionable, not vague

---

## Output Format

```
✅ JPS Action Created

📄 File: projects/ai-json-prompt-schema-system/notes/jps-conversions/{name}.json
📋 Fields: {N} output fields
🔧 Features: [structured output | template params | scopes | $choices]
✅ Validation: All checks passed

Next steps:
1. Add to Seed-JpsActions.ps1 for deployment
2. Deploy BFF API + seed Dataverse
3. Test end-to-end with a sample document
```

---

## Examples

### Example 1: Simple extraction action

**Input:**
```
User: "Create a JPS action that extracts key dates from contracts — effective date, expiration date, renewal deadline"
```

**Output:** Creates `date-extractor.json` with:
- instruction.role: expert legal date extraction analyst
- instruction.task: extract all significant dates from the document
- output.fields: sprk_effectivedate, sprk_expirationdate, sprk_renewaldeadline (type: string)
- structuredOutput: true
- 1 example with sample contract dates

### Example 2: Pre-fill with $choices (Dataverse lookup)

**Input:**
```
User: "Create a JPS action that pre-fills matter fields from document analysis — matter type should come from Dataverse"
```

**Output:** Creates `matter-pre-fill.json` with:
- output.fields: matterTypeName with `$choices: "lookup:sprk_mattertype_ref.sprk_mattertypename"`
- output.fields: practiceAreaName with `$choices: "lookup:sprk_practicearea_ref.sprk_practiceareaname"`
- structuredOutput: true
- Enum values resolved from Dataverse at render time, constraining AI to exact values

### Example 3: Classification with $choices (downstream routing)

**Input:**
```
User: "Create a JPS action for document classification that routes to different analysis nodes based on type"
```

**Output:** Creates `document-classifier.json` with:
- output.fields: sprk_documenttype with `$choices: "downstream:classificationResult.documentType"`
- structuredOutput: true
- Routing values populated from downstream node fieldMappings

---

### Step 6: Refresh Scope Index

After seeding a new action to Dataverse, refresh the scope catalog so Claude Code can find it in future playbook designs:

```
RUN: powershell -ExecutionPolicy Bypass -File scripts/Refresh-ScopeModelIndex.ps1 -Environment dev

REPORT: "Scope index refreshed — new action {ACT-XXX} now available for playbook design."

IF called from within jps-playbook-design:
  → SKIP commit (parent skill handles it)
ELSE:
  → git add .claude/catalogs/scope-model-index.json
  → git commit -m "chore(ai): refresh scope-model-index after adding {action-name}"
```

---

## Error Handling

| Situation | Response |
|-----------|----------|
| User doesn't specify output fields | Suggest common fields based on analysis type |
| User wants unsupported field type | Explain valid types (string, number, boolean, array) |
| JPS JSON validation fails | Report specific errors and auto-fix |
| User asks for multi-node playbook | Redirect to `jps-playbook-design` skill |

---

## Related Skills

- `jps-playbook-design` — Design multi-node playbooks (calls this skill for each node)
- `jps-scope-refresh` — Refresh scope index after new action is seeded (called at Step 6)
- `jps-validate` — Validate JPS JSON against schema and render test
- `dataverse-deploy` — Deploy seeded Actions to Dataverse

---

## Tips for AI

- Always load at least 2 example JPS files before generating — match the closest pattern
- Use the document-profiler.json as the "gold standard" for simple extraction actions
- Use the clause-analyzer.json as the "gold standard" for complex actions with scopes
- Never skip the validation step — malformed JPS causes silent runtime failures
- Always include at least 1 concrete example in the examples section
- Prefer `structuredOutput: true` for any action that populates Dataverse columns
