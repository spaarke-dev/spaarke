---
description: Create a new JPS (JSON Prompt Schema) definition for an AI Analysis Action — generates valid JSON with instruction, output fields, scopes, template params, and examples
tags: [ai, jps, playbook, prompt-schema, action]
techStack: [azure-openai, aspnet-core, dataverse]
appliesTo: ["create JPS action", "new JPS definition", "create analysis action", "new playbook action"]
alwaysApply: false
exemplar: .claude/skills/jps-action-create/examples/document-profiler.json
last-reviewed: 2026-06-29
---

# jps-action-create

> **Last Reviewed**: 2026-06-29
> **Reviewed By**: spaarke-ai-platform-unification-r7 task 070 (FR-32 — node-first dispatch rewrite). Action is now framed as a reusable prompt template; dispatch identity lives on `node.sprk_executortype`. The `sprk_analysisaction.sprk_actiontypeid` lookup and `sprk_executoractiontype` INT columns were dropped in Wave 4 tasks 043+044 — no JPS-author signal touches them anymore. Prior review: ai-procedure-quality-r1 (Phase 2b Wave 2d).
> **Exemplar rationale**: `examples/document-profiler.json` is the canonical simple-extraction reference (named throughout this skill body as "gold standard"). For complex-with-scopes pattern, see `examples/clause-analyzer.json`.

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

### Step 1.5: Where does this config live? (Config-Home Guard — BINDING per canonical-truth loop 2026-06-26; updated for R7 node-first dispatch 2026-06-29)

Before writing JPS JSON, confirm what you are putting on the Action vs the node. **Actions are reusable prompt templates; per-instance wire-up AND dispatch identity live on the node row.** Walk the 4-Home decision tree at [`docs/architecture/ai-architecture-actions-nodes-scopes.md`](../../../docs/architecture/ai-architecture-actions-nodes-scopes.md) §4 BEFORE Step 2:

| If the field is... | Home | Goes on... |
|---|---|---|
| Action-intrinsic prompt template — system prompt, output schema, default temperature | A | `sprk_analysisaction` columns — THIS skill creates these |
| Identity/scheduling/capability of the playbook | B | `sprk_analysisplaybook` columns — NOT this skill |
| **Per-node dispatch identity** (`sprk_executortype` Choice — selects which executor runs the node) AND per-node runtime config (input bindings, output variable, position, executor-specific knobs) | C | `sprk_playbooknode` row / `sprk_configjson` — NOT this skill |
| Declarative "this playbook needs Skill X" scope | D | N:N relationships — NOT this skill |

**Dispatch was REMOVED from Home A in R7** (FR-07, FR-08, FR-09). The node's `sprk_executortype` Choice now owns dispatch identity per design.md §2 Invariant 2 — a single-hop read in `PlaybookOrchestrationService.ExecuteNodeAsync`. Why is this binding? See design.md §3.1 — the pre-R7 3-layer dispatch storage (`node.sprk_nodetype` + `Action.sprk_executoractiontype` + `Action.sprk_actiontypeid` lookup chain) was never enforced to agree, drifted across releases, and caused every release to ship a different version of the same class of "wrong executor ran" bug. The Wave 4 schema cleanup (tasks 043+044, 2026-06-29) dropped the two Action-side columns; if your JPS author muscle-memory says "set ActionType on the Action," the column literally does not exist anymore.

If your "Action config" turns out to be Home C (per-instance wire-up OR dispatch identity), STOP — that belongs on the node, not in the JPS. The JPS describes WHAT the Action does intrinsically (its system prompt + output shape + scope references). The NODE describes WHO runs it (the executor) and HOW it's wired (config, bindings).

**Note on `sprk_outputschemajson`**: this column exists on `sprk_analysisaction` and is read at runtime by the orchestrator for prompt-driven executors (AiAnalysis, AiCompletion, AiEmbedding) — so it IS a Home A field. JPS authors can populate it via the `output.structuredOutput: true` flag (the deploy pipeline derives the JSON Schema from `output.fields[]`).

### Step 2: Load Context

```
LOAD knowledge files:
  - docs/architecture/ai-architecture-actions-nodes-scopes.md (PRIMARY — config-home decision tree)
  - docs/architecture/ai-architecture-playbook-runtime.md (action lookup precedence §5; outputschemajson runtime read site)
  - docs/guides/ai-guide-playbook-deploy-recipe.md (deploy-time actionCode → FK resolution)
  - docs/guides/JPS-AUTHORING-GUIDE.md (schema reference — trimmed to schema-only per 2026-06-26 loop)


LOAD 2-3 example JPS files as patterns:
  - .claude/skills/jps-action-create/examples/document-profiler.json (simple extraction)
  - .claude/skills/jps-action-create/examples/clause-analyzer.json (complex with scopes)

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
SAVE to: .claude/skills/jps-action-create/examples/{action-name}.json

ACTION-NAME convention:
  - Lowercase, kebab-case
  - Descriptive: document-profiler, clause-analyzer, risk-detector
  - Match the Analysis Action's sprk_name field
```

### Step 5.5: Post-Deploy Verification (BINDING per canonical-truth loop 2026-06-26; updated for R7 schema cleanup 2026-06-29)

After the Action row is seeded to Dataverse via `Seed-JpsActions.ps1`, **verify with Dataverse MCP `read_query`** that the row landed with the right columns. The actionCode is the canonical alternate key — every playbook node will FK-resolve to this Action via actionCode → Guid at deploy time (see [`ai-guide-playbook-deploy-recipe.md`](../../../docs/guides/ai-guide-playbook-deploy-recipe.md) §3 step 3).

```
mcp__dataverse__read_query against sprk_analysisaction
  filter: sprk_actioncode eq '{ACTION-CODE}'
  select: sprk_actioncode, sprk_name, sprk_systemprompt, sprk_outputschemajson, sprk_temperature

EXPECT exactly 1 row.
EXPECT sprk_systemprompt non-empty (Memo column — first 200 chars sufficient).
EXPECT sprk_outputschemajson non-empty IF structuredOutput=true in JPS.
```

The `_sprk_actiontypeid_value` lookup and `sprk_executoractiontype` INT columns were dropped from `sprk_analysisaction` in R7 Wave 4 (tasks 043+044, 2026-06-29) — do NOT include them in your select projection; the request will 400 (column does not exist).

If the row is missing or missing required columns, re-run `Seed-JpsActions.ps1` — do NOT manually patch.

### Step 6: Offer Next Steps

Ask user:
- Add this action to `scripts/Seed-JpsActions.ps1` mapping? (for deployment)
- Create a playbook that uses this action? (invoke `jps-playbook-design`)
- Validate the JPS with a render test? (invoke `jps-validate`)

---

## The R7 dispatch model — what changed and why

> **Read this once.** It anchors why "Don't set ActionType on the Action" is binding in R7+.

**Before R7**, an Action carried dispatch identity via TWO redundant columns on `sprk_analysisaction`:
- `sprk_actiontypeid` (lookup → `sprk_analysisactiontype` table)
- `sprk_executoractiontype` (INT column duplicating the row's executor value)

The orchestrator resolved dispatch via a 3-layer lookup: `node → Action → ActionType lookup row → executor`. None of the three layers was enforced to agree with the others, so they drifted across releases. Every release shipped a different version of the same "wrong executor ran for this node" bug.

**After R7** (design.md §2 Invariants 1-3 + §3.1 WHY history):
- **Invariant 1**: Every node has `sprk_executortype` (Choice) set. Single source of dispatch identity.
- **Invariant 2**: `PlaybookOrchestrationService.ExecuteNodeAsync` reads `node.sprk_executortype` directly. Single hop. No lookup chain. No structural fallback. No Action override branch.
- **Invariant 3**: Action carries `SystemPrompt + OutputSchema + Temperature` ONLY. It is a reusable prompt template for prompt-driven executors (AiAnalysis, AiCompletion, AiEmbedding). It does NOT carry dispatch identity.

**The schema columns that previously held Action-side dispatch are GONE** (Wave 4 tasks 043+044, 2026-06-29):
- `sprk_analysisaction.sprk_actiontypeid` — deleted via Dataverse Web API; the ManyToOne relationship was dropped, cascading the lookup column.
- `sprk_analysisaction.sprk_executoractiontype` — deleted via Dataverse Web API.

The `sprk_analysisactiontype` lookup TABLE itself is preserved (FR-05) as decorative maker categorization — but no runtime code reads it for dispatch.

**Implication for JPS authoring**: when you create a new Action, you describe WHAT it does intrinsically (system prompt + output schema + temperature). You do NOT describe WHO runs it — that's the node author's call in PlaybookBuilder when they drop the node on the canvas and pick `Executor Type` from the Choice dropdown. If you find yourself reaching for "set ActionType on the Action," stop — the column literally does not exist anymore, and reaching for it suggests you're trying to bake a Home-C concern (node-level dispatch) into a Home-A artifact (the prompt template).

**Cross-references**:
- design.md §2 (Invariants 1-3) — the binding rules
- design.md §3.1 (R3.1 WHY history) — the failure modes that motivated the rewrite
- spec.md FR-07 / FR-08 / FR-09 — runtime reform (single-hop dispatch, structural-fallback delete, Action-override-branch delete)
- spec.md FR-03 / FR-04 — schema column deletions
- `PlaybookOrchestrationService.ExecuteNodeAsync` — runtime source of truth (Wave 2 task 024)

---

## Conventions

- Field names use `sprk_` prefix for Dataverse column mapping
- JPS files stored in `.claude/skills/jps-action-create/examples/`
- One JPS file per Analysis Action
- Examples should be realistic but not contain actual sensitive data (ADR-015)
- Constraints should be specific and actionable, not vague

---

## Output Format

```
✅ JPS Action Created

📄 File: .claude/skills/jps-action-create/examples/{name}.json
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

### Step 7: Refresh Scope Index

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
- Use the `examples/document-profiler.json` as the "gold standard" for simple extraction actions
- Use the `examples/clause-analyzer.json` as the "gold standard" for complex actions with scopes
- Never skip the validation step — malformed JPS causes silent runtime failures
- Always include at least 1 concrete example in the examples section
- Prefer `structuredOutput: true` for any action that populates Dataverse columns

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Generated JPS doesn't render — flat-text fallback used at runtime | `IsJpsFormat()` returned false (missing `$schema` field OR malformed JSON) | Always invoke `jps-validate` (Step 4) BEFORE saving. Check 23 in jps-validate explicitly tests format detection. |
| `$choices` reference fails at runtime | Wrong prefix (`lookup:`, `optionset:`, `multiselect:`, `boolean:`, `downstream:`) OR missing `.fieldName` separator | jps-validate Check 16 catches this. Format: `lookup:entity.field` (with exactly one `.`). |
| Scope `$ref` resolves to non-existent record | Referenced knowledge/skill record was never seeded to Dataverse | Run `jps-scope-refresh` first to confirm scopes exist. If missing, invoke this skill (`jps-action-create`) recursively to create the needed scope. |
| Duplicate Step 6 confusion (offer-next-steps vs refresh-scope-index) | Earlier skill version had two Steps labeled "6" | Fixed 2026-05-17: second Step 6 → Step 7. Renumbered for clarity. |
| Examples path broken after directory move | Examples were at `projects/x-ai-json-prompt-schema-system/notes/jps-conversions/` (archived dir) | Fixed 2026-05-17 (Option A): all 23 canonical JSONs moved to `.claude/skills/jps-action-create/examples/`. Skill body + jps-playbook-design + jps-validate updated to match. |
| Created action not findable in playbook design | Forgot Step 7 (Refresh Scope Index) — scope-model-index.json is stale | ALWAYS run Step 7 after seeding. `jps-playbook-design` reads `scope-model-index.json` — if your new action isn't there, it can't be selected. |
