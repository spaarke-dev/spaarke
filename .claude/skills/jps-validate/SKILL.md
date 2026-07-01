---
description: Validate a JPS JSON file against schema and test rendering
tags: [ai, jps, testing, validation, prompt-schema]
techStack: [azure-openai, aspnet-core]
appliesTo: ["validate JPS", "check JPS", "test JPS definition", "validate prompt schema"]
alwaysApply: false
exemplar: .claude/skills/jps-action-create/examples/document-profiler.json
last-reviewed: 2026-06-29
---

# jps-validate

> **Last Reviewed**: 2026-06-29
> **Reviewed By**: spaarke-ai-platform-unification-r7 task 073 (FR-32 — executor-first validation). New Step 7.6 R7-Dispatch checks anchor validation on the single-hop `node.sprk_executortype` Choice (33 values matching the C# `ExecutorType` enum). Step 7.5 CHECK 25 list of legacy NodeType values (AIAnalysis | Output | Control | Workflow | DeliverComposite) MARKED LEGACY — the canonical contract is now the 33-value `sprk_playbookexecutortype` global Choice set. Step 7.5 CHECK 26 (configJson.__actionType structural fallback) DELETED — the structural fallback ladder was removed in Wave 2 task 025; reading `__actionType` is no longer load-bearing. Step 7.7 references the Wave 3 typed-config-schema endpoint (FR-16) for per-executor configJson validation. Prior review: ai-procedure-quality-r1 (Phase 2b Wave 2b-A; Wave 2d post-move).

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
  SUGGEST: "Look in .claude/skills/jps-action-create/examples/"
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

### Step 7.5: Playbook-Definition File Validation (BINDING per canonical-truth loop 2026-06-26)

If the file under validation is a **playbook definition** (top-level keys `playbook`, `nodes`), apply the additional contract checks from [`docs/guides/ai-guide-playbook-deploy-recipe.md`](../../../docs/guides/ai-guide-playbook-deploy-recipe.md) — these enforce the runtime contract per [`docs/architecture/ai-architecture-playbook-runtime.md`](../../../docs/architecture/ai-architecture-playbook-runtime.md) and the schema gates in [`src/server/api/Sprk.Bff.Api/Models/Ai/node-routing-config.schema.json`](../../../src/server/api/Sprk.Bff.Api/Models/Ai/node-routing-config.schema.json):

```
NODE-LEVEL CHECKS (apply per node in nodes[]):
  ✅/❌ CHECK 24: actionCode is present for every PROMPT-DRIVEN node (executorType ∈ {AiAnalysis=0, AiCompletion=1, AiEmbedding=2})
    — Pure executors (Condition, ReturnResponse, etc.) MUST NOT carry actionCode — it is unused at runtime
    — Deploy-Playbook.ps1 still lints this for prompt-driven nodes; missing actionCode → executor Validate() throws
  ✅/❌ CHECK 25 [R7]: executorType is one of the 33 values from `sprk_playbookexecutortype` Choice
    — Source of truth: src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ExecutorType.cs (C# enum, post-Wave-2-task-022 rename)
    — Aligns with the 33-value global Dataverse Choice (sprk_playbookexecutortype)
    — LEGACY (pre-R7) values like AIAnalysis | Output | Control | Workflow | DeliverComposite are gone; if encountered, FLAG as LEGACY-25 drift
  ✅/⚠️ CHECK 27: configJson is well-formed against node-routing-config.schema.json
    — Same schema gate Deploy-Playbook.ps1:789 applies (FR-14e)

  ⚠️ R7 NOTE: pre-R7 this section also CHECK 26'd that `configJson.__actionType` was set as a STRUCTURAL FALLBACK for AIAnalysis nodes. That fallback was DELETED in Wave 2 task 025 (~150 LOC removed from `PlaybookOrchestrationService.cs`). Reading or writing `__actionType` is now load-NOT-bearing; FLAG any occurrence as LEGACY-26 drift.

  ⚠️ NOTE on sprk_isactive: the JSON file format does NOT carry this field — Deploy-Playbook.ps1:823
     writes sprk_isactive=true explicitly. There is nothing to validate at the JSON level;
     the audit skill (jps-playbook-audit) checks the deployed row.

ANTI-PATTERN CHECKS (per ai-architecture-actions-nodes-scopes.md §5):
  ✅/⚠️ CHECK 28: playbook.sprk_configjson does NOT contain a "nodes" or "edges" array
    — That's the R4 deploy bug: putting nodes in playbook configjson instead of deploying as rows
  ✅/⚠️ CHECK 29: Scope decisions (skills, knowledge, tools) live in scopes.*, NOT inline in node configJson
    — Audit + refresh tooling cannot find inline-JSON scope declarations
```

### Step 7.6: R7 Dispatch-Identity Checks (BINDING per spec FR-32; added 2026-06-29 by task 073)

Anchor every executor-related validation on `node.sprk_executortype` (Choice). This is the canonical dispatch axis post-R7 single-hop refactor. Failures here are non-bypassable.

```
PER-NODE DISPATCH CHECKS:
  ✅/❌ R7-V-01: node.executorType is non-NULL and is a valid Choice value (0-143 per the 33-value catalog)
  ✅/❌ R7-V-02: If executorType is prompt-driven (∈ {0=AiAnalysis, 1=AiCompletion, 2=AiEmbedding}), then sprk_actionid FK MUST be non-NULL
  ✅/⚠️ R7-V-03: If executorType is pure (anything NOT prompt-driven), then sprk_actionid FK MUST be NULL (non-NULL = LEGACY-F drift per audit Check 3.6 pattern F; warning, not fail)
  ✅/❌ R7-V-04: Action FK (when present) resolves to a sprk_analysisaction row that exists in Dataverse

LEGACY-DRIFT FAIL conditions (any of these = validator FAIL with LEGACY-* rule id):
  - LEGACY-NT: node carries `sprk_nodetype` field (column removed pre-R7)
  - LEGACY-AT: node configJson references `__actionType` (structural fallback deleted in Wave 2 task 025)
  - LEGACY-LK: validation logic reads `sprk_actiontypeid` lookup as a dispatch signal (column dropped in Wave 4 task 043 — `_sprk_actiontypeid_value` no longer exists)
  - LEGACY-EX: validation logic reads `sprk_executoractiontype` INT (column dropped in Wave 4 task 044)
  - LEGACY-NV: Action JPS output field is named `ActionType` / `ExecutorActionType` / `NodeType` — these names mean nothing under R7
  - LEGACY-DH: Node config carries a dispatch hint that is NOT `sprk_executortype` (e.g., a custom `dispatchTo` field) — author is reaching for the old model
```

### Step 7.7: Typed Config-Schema Check (BINDING per spec FR-16; Wave 3 endpoint)

For each node, fetch the executor's typed config schema from the BFF endpoint and validate the node's `sprk_configjson` against it. This catches authoring errors before Deploy-Playbook.ps1 sees them.

```
  ✅/❌ R7-V-05: GET /api/ai/playbook-builder/executor-config-schemas (Wave 3 task 033)
    — Returns 33 schemas (one per ExecutorType value)
    — Each schema declares fields[].name, type, required, default, description

  ✅/❌ R7-V-06: node.sprk_configjson conforms to its executorType's schema
    — Required fields are present
    — Field types match (string / number / boolean / array)
    — Unknown fields → WARN (forward-compatibility) — only fail if executorType also unknown
    — Empty schema (e.g., StartNodeExecutor) → configJson SHOULD be {} or absent

  ✅/⚠️ R7-V-07: If executor is Wave-3-rich-schema (AiAnalysis, AiCompletion, Condition, EntityNameValidator, CreateNotification), validate ALL declared fields. If executor is Wave-8-placeholder-schema (the other 28 per task 085), validate AT LEAST the 1 declared field. Both paths are FR-23 compliant.
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
User: "validate JPS .claude/skills/jps-action-create/examples/document-profiler.json"
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
| File not found | Check path, suggest examples/ directory |
| Binary/non-text file | Report error, ask for correct file |
| Valid JSON but not JPS | Explain JPS requirements, offer to convert |
| All checks pass | Confirm ready for deployment |

---

## The R7 dispatch model — what changed and why this validator works

> **Read this once.** It anchors why Step 7.6 + 7.7 fail FAST on `sprk_executortype` deviations.

**Before R7**, validation was complacent because none of the 3 dispatch layers (`node.sprk_nodetype` + `Action.sprk_executoractiontype` INT + `Action.sprk_actiontypeid` lookup) was authoritatively enforced. The validator could pass and the wrong executor could still run, because the runtime ladder picked whichever layer was first non-NULL — and that varied across releases. Every release shipped a different version of the same class of "wrong executor ran" bug (design.md §3.1).

**After R7** (Invariants 1-3):
- `PlaybookOrchestrationService.ExecuteNodeAsync` reads `node.sprk_executortype` (Choice) once. Single hop. No ladder.
- The 33 executor types catalog in `sprk_playbookexecutortype` global Choice mirrors the C# `ExecutorType` enum (Wave 2 task 022 rename from the legacy `ActionType` name).
- Each executor declares its own typed config schema via `INodeExecutor.GetConfigSchema()` (Wave 3 FR-16 + tasks 030-036). The BFF endpoint `GET /api/ai/playbook-builder/executor-config-schemas` returns all 33.
- Action is a reusable prompt template for prompt-driven executors only. Pure executors don't reference an Action.

**This validator's load-bearing job** under R7:
1. **Step 7.5** — playbook-definition contract (preserved from canonical-truth loop 2026-06-26; CHECK 25 + 26 updated to flag legacy NodeType + `__actionType` drift).
2. **Step 7.6** — dispatch-identity anchored on `sprk_executortype`. 4 R7-V-* checks + 6 LEGACY-* drift flags.
3. **Step 7.7** — per-executor typed config validation against the live BFF schema endpoint. Catches the failures Wave 3 was designed to prevent.

**The pre-R7 "$schema + structural fallback + lookup precedence" trio is gone**, and so is the validator's reliance on it. If you see code or docs that still describe the old contract, it's stale — file as `LEGACY-*` drift, not a validator bug.

**Cross-references**:
- design.md §2 (Invariants 1-3) — binding rules
- design.md §3.1 (R3.1 WHY history) — failure modes that motivated the rewrite
- design.md §11 (executor categorization) — tier mapping for the 33 values
- spec.md FR-07 / FR-08 / FR-09 — single-hop runtime refactor
- spec.md FR-16 — typed config schemas
- spec.md FR-32 — this skill's rewrite
- `ExecutorType.cs` — C# enum (renamed in Wave 2 task 022)
- `PlaybookOrchestrationService.ExecuteNodeAsync` — runtime source of truth (Wave 2 task 024)
- `GET /api/ai/playbook-builder/executor-config-schemas` — BFF endpoint serving the 33 schemas (Wave 3 task 033)

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

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Schema validator passes but renderer rejects at runtime | `IsJpsFormat()` in `PromptSchemaRenderer.cs` checks more than just the `$schema` field (e.g., requires certain top-level keys) | After schema validation passes, ALWAYS run the render test (Step 6 in Workflow). Schema OK + render OK = ready. |
| `$choices` reference is syntactically valid but the entity doesn't exist | Reference to `lookup:sprk_<entity>.<field>` where the entity was renamed or deleted | Validate `$choices` prefixes are well-formed (Tip #5), then optionally cross-reference Dataverse via `mcp__dataverse__list_tables()` to confirm the entity exists. |
| Validator passes but production output has missing fields | JPS structured-output schema lists fields but the AI model doesn't reliably produce all of them | Schema validation can't catch runtime AI behavior. Run a sample render through the actual deployed model BEFORE relying on the JPS in production. |
| Validator reports false-positive failure | JPS spec evolved; validator wasn't updated | Validators MUST track the schema spec version. If schema version doesn't match validator version, treat as inconclusive (warn-only). |
