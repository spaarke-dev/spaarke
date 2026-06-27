# Smoke: Task 007 — BRIEF-VALIDATE-ENTITY-NAMES Action Deploy

> **Authored**: 2026-06-25
> **Task**: 007 (Phase 1 / W0 / PR 1 — sequential after W1.A)
> **Status**: ✅ Deployed + verified

---

## Summary

One `sprk_analysisaction` row deployed to spaarkedev1 via Dataverse MCP. Active, ActionType 141 (EntityNameValidator). This closes the EntityNameValidator triplet started in tasks 002 (enum), 003 (executor + tests), 004 (form). Canonical repo source JSON committed.

---

## A. Canonical Source File

- `projects/spaarke-daily-update-service/notes/playbooks/actions/brief-validate-entity-names.action.json`

Follows the JPS schema (`https://spaarke.com/schemas/prompt/v1`, `$version: 1`). Wraps a `_dataverseRow` metadata block (stripped at deploy time) that supplies the Dataverse column values; the body becomes `sprk_systemprompt`.

This is a **system data-ops Tool** (not an LLM analysis action) — `sprk_executoractiontype = 141` routes execution to `EntityNameValidatorNodeExecutor` (in-process C# proper-noun scrubbing), NOT to `AiAnalysisNodeExecutor`. The JPS body documents the input/output contract for catalog discoverability + PlaybookBuilder palette, NOT for LLM rendering. `sprk_temperature = 0` is harmless (irrelevant to Tool dispatch). Same pattern as `sys-lookup-membership.action.json` (task 005, ActionType 52).

---

## B. MCP Availability

Dataverse MCP available in this worktree session (`mcp__dataverse__*` tools loaded via ToolSearch). `read_query`, `create_record` both functional. Idempotency check via `read_query` returned `[]` before deploy (no prior row for `BRIEF-VALIDATE-ENTITY-NAMES`), so `create_record` used (not `update_record`).

Note: Initial `read_query` against table `sprk_analysisactions` (plural) failed with MetadataCache lookup error; corrected to singular `sprk_analysisaction` per the working table name used by tasks 005 + 006.

---

## C. Deployment Result

| Field | Value |
|---|---|
| `sprk_analysisactionid` | `290e786c-ff70-f111-ab0e-7ced8ddc4cc6` |
| `sprk_name` | Brief Validate Entity Names |
| `sprk_actioncode` | BRIEF-VALIDATE-ENTITY-NAMES |
| `sprk_actionid` | BRIEF-VALIDATE-ENTITY-NAMES |
| `sprk_executoractiontype` | 141 (EntityNameValidator) ✅ |
| `sprk_temperature` | 0.0 ✅ (harmless — Tool not LLM) |
| `sprk_outputformat` | 0 (JSON) |
| `statecode` | 0 (Active) ✅ |
| `statuscode` | 1 (Active) ✅ |

Verified via post-deploy `read_query`:

```sql
SELECT sprk_analysisactionid, sprk_actioncode, sprk_name, sprk_executoractiontype,
       sprk_temperature, statecode, statuscode
FROM sprk_analysisaction
WHERE sprk_actioncode = 'BRIEF-VALIDATE-ENTITY-NAMES'
-- → 1 row, Active, executoractiontype=141, temperature=0
```

Result:
```json
[
  {
    "sprk_analysisactionid": "290e786c-ff70-f111-ab0e-7ced8ddc4cc6",
    "sprk_actioncode": "BRIEF-VALIDATE-ENTITY-NAMES",
    "sprk_name": "Brief Validate Entity Names",
    "sprk_executoractiontype": 141,
    "sprk_temperature": 0.0,
    "statecode": 0,
    "statecodename": "Active",
    "statuscode": 1,
    "statuscodename": "Active"
  }
]
```

---

## D. Contract Alignment — Executor (task 003) ↔ Action JPS

The deployed JPS `instruction` + `input` + `output` schemas mirror the `EntityNameValidatorNodeExecutor.cs` contract exactly:

| Surface | Executor (003) | Action JPS (007) | Aligned |
|---|---|---|---|
| Input field 1 | `EntityNameValidatorNodeConfig.CandidateText` (`[JsonPropertyName("candidateText")]`) | `input.candidateText` (string, required) | ✅ |
| Input field 2 | `EntityNameValidatorNodeConfig.AllowList` (`[JsonPropertyName("allowList")]`) | `input.allowList` (array of strings, required, empty OK / null invalid) | ✅ |
| Output field 1 | `scrubbedText` (anonymous output object + `textContent` binding) | `output.fields[0].name = "scrubbedText"` | ✅ |
| Output field 2 | `removedTerms` (anonymous output object) | `output.fields[1].name = "removedTerms"` | ✅ |
| Routing | `SupportedActionTypes = [ActionType.EntityNameValidator]` (= 141) | `sprk_executoractiontype = 141` | ✅ |
| Logging event | `HallucinationDetectedEvent = "hallucination_detected"` (LogWarning per removed term) | Constraint #4 references the literal event name | ✅ |
| Allow-list semantics | empty list = scrub everything; null = validation error | Documented verbatim in constraint #2 + input description | ✅ |
| Sentence-order preservation | Sentence-split, walk in order, reassemble in order | Constraint #5: "MUST preserve sentence ordering" | ✅ |

No contract drift. The PlaybookBuilder palette will surface this as "Brief Validate Entity Names" with the `EntityNameValidatorForm.tsx` (task 004) when the property panel opens (palette resolves Tool → ActionType → form via `sprk_executoractiontype` 141).

---

## E. JPS Validation

Manual validation against `.claude/skills/jps-validate/SKILL.md` checks (Steps 2–7):

| Check | Result |
|---|---|
| Valid JSON | ✅ |
| Has `$schema` = `https://spaarke.com/schemas/prompt/v1` | ✅ |
| `instruction.role` non-empty | ✅ |
| `instruction.task` non-empty | ✅ |
| `instruction.constraints` is array | ✅ (5 items) |
| `output.fields` ≥ 1 | ✅ (2 fields) |
| Output field types valid (string/number/boolean/array) | ✅ (string, array) |
| Field `name` + `type` + `description` present | ✅ |
| `examples` ≥ 1 entry | ✅ (3 — non-empty + empty-payload + all-removed cases) |
| `metadata.description` + `tags` | ✅ |
| `IsJpsFormat()` detection: starts with `{` + contains `"$schema"` | ✅ |

All structural checks pass. No `$ref` or `$choices` features used (Tool consumes freeform config-derived input, not Dataverse-lookup-bound enums).

---

## F. AC-2b / AC-13a Audit — Prohibited Names

**Audit method**: The action JSON `instruction.context` documents the failure mode (R3 UAT observed fictional firm/case names emitted by the LLM). Per prior smoke 006 § D, prohibited literal names (`Acme`, `Johnson & Lee`, `Davis v. Metro Transit`, `engagement letter`) MUST NOT appear in the deployed `sprk_systemprompt`.

The deployed `sprk_systemprompt` for THIS row contains generic placeholder text only — no firm names, no case names, no party names. The repo source JSON examples (which DO use the canonical anti-pattern names "Johnson & Lee LLP", "Davis v. Metro Transit", "ACME Corp" as positive/negative test fixtures) are stored in the `examples` block of the repo source — but the deployed `sprk_systemprompt` was emitted from a compact body that EXCLUDES the `examples` array (the repo JSON examples are for human + designer documentation; deployed prompt has only `$schema`/`$version`/`instruction`/`input`/`output`/`metadata`).

**Conclusion**: AC-2b (NO example names baked into deployed prompts) passes for the deployed body. The repo JSON `examples` retain canonical test fixtures for documentation purposes (NOT shipped to the row).

---

## G. Triplet Closure (Tasks 002 → 003 → 004 → 007)

| Task | Output | Status |
|---|---|---|
| 002 | `ActionType.EntityNameValidator = 141` enum value in `INodeExecutor.cs` | ✅ |
| 003 | `EntityNameValidatorNodeExecutor.cs` (445 LOC) + xUnit tests | ✅ |
| 004 | `EntityNameValidatorForm.tsx` PlaybookBuilder property panel | ✅ |
| 007 | `sprk_analysisaction` row `290e786c-ff70-f111-ab0e-7ced8ddc4cc6` (this task) | ✅ |

All four pieces in place. The DAILY-BRIEFING-NARRATE playbook (PR 2 task 011) can now compose this Action by code (`BRIEF-VALIDATE-ENTITY-NAMES`) and the engine will resolve the Tool, dispatch to the C# executor, and the PlaybookBuilder palette will surface the form.

---

## H. Downstream Wiring

- This Action row will be referenced by the `DAILY-BRIEFING-NARRATE` playbook (PR 2 task 010 / FR-4). Node graph: `Start → LoadKnowledge → [GenerateTldr ‖ GenerateChannelNarratives] → ValidateEntityNames (this Action) → ReturnResponse`.
- The widget consumer (`useBriefingNarration.ts`) receives only the post-scrub `scrubbedText` — never sees the raw LLM output.
- App Insights monitors `hallucination_detected` log events emitted per removed term (frequency = LLM grounding-failure rate; target = 0; alert threshold > 1% of narrate invocations per AI-MONITORING-DASHBOARD.md).

---

## I. Open Items

None for this task. The deployed row is ready for PR 2 task 010 (DAILY-BRIEFING-NARRATE authoring) to reference.

Per `jps-action-create` skill Step 7, `jps-scope-refresh` should be run after seeding new actions so PlaybookBuilder palette discovers them. That step is task 008 (PR 1 wrap-up — runs after tasks 005, 006, 007 are all deployed).

---

## J. Acceptance Criteria Status

- **AC-3a** (`sprk_executoractiontype = 141`, no conflicts with existing values): ✅ — verified via post-deploy read_query
- **AC-3c** (PlaybookBuilder palette displays the Tool; clicking it opens EntityNameValidatorForm.tsx): ⏭️ deferred to task 008 (PR 1 smoke) — requires scope-refresh + interactive PCF verification. Triplet is in place; palette resolution path is mechanical (form is keyed off ActionType 141 via task 004 wiring).
- **JSON committed at canonical path**: ✅ — `projects/spaarke-daily-update-service/notes/playbooks/actions/brief-validate-entity-names.action.json`
- **JPS structural validation**: ✅ (this doc § E)
- **Idempotent upsert pattern**: ✅ — read_query check before create; no duplicate row
