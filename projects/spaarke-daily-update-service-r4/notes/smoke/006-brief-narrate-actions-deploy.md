# Smoke: Task 006 — BRIEF-NARRATE-TLDR + BRIEF-NARRATE-CHANNEL Action Deploy

> **Authored**: 2026-06-25
> **Task**: 006 (Phase 1 / W0 / PR 1 — Group B)
> **Status**: ✅ Deployed + verified

---

## Summary

Two `sprk_analysisaction` rows deployed to spaarkedev1 via Dataverse MCP. Both Active. Both grounded (no baked firm/case names). Canonical repo source JSON committed.

---

## A. Canonical Source Files

- `projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-tldr.action.json`
- `projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-channel.action.json`

Both follow the JPS schema (`https://spaarke.com/schemas/prompt/v1`, `$version: 1`). Both wrap a `_dataverseRow` metadata block (stripped at deploy time) that supplies the Dataverse column values; the body becomes `sprk_systemprompt`.

The `_comment` and `_dataverseRow` keys are stripped before serializing to `sprk_systemprompt` — the deployed prompt contains only the JPS body (`$schema`, `$version`, `instruction`, `input`, `output`, `metadata`).

---

## B. MCP Availability

Dataverse MCP available in this worktree session (`mcp__dataverse__*` tools loaded via ToolSearch). `describe`, `read_query`, `create_record` all functional. Idempotency check via `read_query` returned `[]` before deploy (no prior rows), so `create_record` used (not `update_record`).

---

## C. Deployment Result

| Field | BRIEF-NARRATE-TLDR | BRIEF-NARRATE-CHANNEL |
|---|---|---|
| `sprk_analysisactionid` | `ce299eb4-fc70-f111-ab0e-7ced8ddc4cc6` | `dc3533c0-fc70-f111-ab0e-7ced8ddc4cc6` |
| `sprk_name` | Brief Narrate TLDR | Brief Narrate Channel |
| `sprk_actioncode` | BRIEF-NARRATE-TLDR | BRIEF-NARRATE-CHANNEL |
| `sprk_actionid` | BRIEF-NARRATE-TLDR | BRIEF-NARRATE-CHANNEL |
| `sprk_executoractiontype` | 0 (AiAnalysis) ✅ | 0 (AiAnalysis) ✅ |
| `sprk_temperature` | 0.0 ✅ | 0.0 ✅ |
| `sprk_outputformat` | 0 (JSON) | 0 (JSON) |
| `statecode` | 0 (Active) ✅ | 0 (Active) ✅ |
| `statuscode` | 1 (Active) ✅ | 1 (Active) ✅ |
| `sprk_systemprompt` length | ~3500 chars | ~3200 chars |

Both rows verified via post-deploy `read_query`:

```sql
SELECT sprk_analysisactionid, sprk_actioncode, sprk_name, sprk_executoractiontype,
       sprk_temperature, statecode, statuscode
FROM sprk_analysisaction
WHERE sprk_actioncode = 'BRIEF-NARRATE-TLDR'
   OR sprk_actioncode = 'BRIEF-NARRATE-CHANNEL'
-- → 2 rows, both Active, both executoractiontype=0, both temperature=0
```

---

## D. AC-2b / AC-13a Audit — Prohibited Names

**Audit method**: Post-deploy fetched `sprk_systemprompt` for both rows via `read_query`, then case-insensitive grep for: `Acme`, `Johnson`, `Davis`, `Metro Transit`, `engagement letter`, `Smith`, `Doe`, `Jones`, `Brown`, `Anderson`, `Wilson`.

**Result**: ZERO matches in either deployed `sprk_systemprompt`. ✅

Examples within the source JSON use only generic placeholder tokens (`<category-A>`, `<matter-name-1>`, `<channel-name>`, `<person-A>`). The `_comment` strings that mention forbidden names (as anti-pattern documentation) live ONLY in the source repo JSON's `_comment` keys, which are stripped before the JSON is stored to `sprk_systemprompt`. The deployed prompt body is clean.

**Conclusion**: AC-2b (NO example names from old prompts) + AC-13a (audit for absence of "Acme Corp", "Johnson & Lee", or any specific case/firm name) both pass.

---

## E. Grounding Instruction Audit (FR-13)

Both `sprk_systemprompt.instruction.constraints[0]` contains the literal:

> "Use ONLY entity names, dates, identifiers present in the provided input. If you cannot summarize a category from the data, write 'no items' or omit the bullet."

Both `instruction.role = "notification summarizer"` (verbatim per spec FR-2).

Both `instruction.context` reinforces the groundedness imperative + identifies the user as a legal-operations professional. Both `instruction.task` repeats "must reflect only what is in the payload — do not invent matters, firms, cases, or people".

Three layered constraints in each row reinforce the grounding rule (role + task + constraints[0] + constraints[1] + constraints[2]). The TLDR action also includes an explicit empty-payload short-circuit rule (constraint[4]).

---

## F. Output Schemas

**TldrResult** (BRIEF-NARRATE-TLDR):
- `summary: string` (≤300 chars)
- `keyTakeaways: array` (≤4 items)
- `topAction: string` (≤200 chars)
- `categoryCount: number`
- `priorityItemCount: number`
- `structuredOutput: true`

**ChannelNarrationResult** (BRIEF-NARRATE-CHANNEL):
- `channel: string` (≤200 chars — echo of input)
- `narrative: array` (≤5 bullet strings)
- `itemCount: number`
- `bulletCount: number`
- `structuredOutput: true`

The shapes match the consumer in `useBriefingNarration.ts` (TldrResult → top of widget; ChannelNarrationResult → per-channel bullets) per decision 030's payload contract section D.

---

## G. JPS Validation

Manual validation against `.claude/skills/jps-validate/SKILL.md` checks (Steps 2–7):

| Check | TLDR | CHANNEL |
|---|---|---|
| Valid JSON | ✅ | ✅ |
| Has `$schema` = `https://spaarke.com/schemas/prompt/v1` | ✅ | ✅ |
| `instruction.role` non-empty | ✅ | ✅ |
| `instruction.task` non-empty | ✅ | ✅ |
| `instruction.constraints` is array | ✅ (6 items) | ✅ (7 items) |
| `output.fields` ≥ 1 | ✅ (5 fields) | ✅ (4 fields) |
| Output field types valid (string/number/boolean/array) | ✅ | ✅ |
| Field `name` + `type` + `description` present | ✅ | ✅ |
| `maxLength` set on string fields | ✅ | ✅ |
| `examples` ≥ 1 entry | ✅ (2 — non-empty + empty-payload cases) | ✅ (2) |
| `metadata.description` + `tags` | ✅ | ✅ |
| `IsJpsFormat()` detection: starts with `{` + contains `"$schema"` | ✅ | ✅ |

All structural checks pass. No `$ref` or `$choices` features used (not needed for these actions — both consume freeform structured payload, not Dataverse-lookup-bound enums).

---

## H. Downstream Wiring

- These 2 Action rows will be composed by the `DAILY-BRIEFING-NARRATE` playbook (PR 2 task 010 / FR-4). That playbook's node graph: `Start → LoadKnowledge → [GenerateTldr (this TLDR action) ‖ GenerateChannelNarratives (this CHANNEL action, parallel per channel)] → ValidateEntityNames → ReturnResponse`.
- BFF `/narrate` wrapper (PR 4 task 031 / FR-12) will dispatch to the playbook via `IConsumerRoutingService` + new `ExecutePayloadPlaybookAsync` per decision 030.
- No JS/C# code consumes these Action rows directly — they're invoked via the playbook engine.

---

## I. Open Items

None for this task. The deployed rows are ready for PR 2 task 010 to reference (`DAILY-BRIEFING-NARRATE` playbook authoring).

Note: Per `jps-action-create` skill Step 7, a `jps-scope-refresh` should be run after seeding new actions so future playbook designers see them in the scope catalog. That step is deferred to the end of PR 1 (task that wraps up W0 deployment), not run per-task.

---

## J. Acceptance Criteria Status

- **AC-2a**: Both rows exist + Active in spaarkedev1; JPS systemprompt validates via jps-validate → ✅ (this doc § C + § G)
- **AC-2b**: No example names from old prompts → ✅ (this doc § D)
- Both rows have `sprk_executoractiontype = 0` (AiAnalysis) → ✅
- Both rows have `sprk_temperature = 0` → ✅
- Both prompts contain literal "use only entity names present in input" grounding → ✅ (this doc § E)
