# Smoke: 011 — Deploy DAILY-BRIEFING-NARRATE playbook to spaarkedev1

> **Task**: 011-deploy-and-validate-narrate-playbook.poml
> **Date**: 2026-06-25
> **Environment**: spaarkedev1
> **Spec refs**: FR-4 (lines 123-125), AC-4a
> **Deployer**: task-execute (STANDARD rigor) via Dataverse MCP

---

## Deployment Outcome

- **Outcome**: **CREATED** (idempotency check confirmed no pre-existing row with `sprk_playbookcode = 'BRIEF-NRRT'` or `'BRIEF-NARRATE'`)
- **Resulting `sprk_analysisplaybookid`**: `7b5a6ed3-0271-f111-ab0e-000d3a13a4cd`
- **`sprk_playbookcode`**: `BRIEF-NRRT` (see deviation below)
- **`sprk_name`**: `Daily Briefing Narrate`
- **`sprk_playbooktype`**: `0` (AiAnalysis)
- **`sprk_playbookmode`**: `1` (NodeBased)
- **`sprk_triggertype`**: `0` (Manual)
- **`sprk_capabilities`**: `100000006` (Summarize)
- **`statecode` / `statuscode`**: `0` (Active) / `1` (Active)
- **`sprk_ispublic`**: `true`
- **`sprk_issystemplaybook`**: `true`

---

## DEVIATION — playbook code truncation

**Source-of-truth value**: `sprk_playbookcode = "BRIEF-NARRATE"` (13 chars) per `projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json` line 10.

**Dataverse schema constraint**: `sprk_analysisplaybook.sprk_playbookcode` is `NVARCHAR(10)` — confirmed via `mcp__dataverse__describe`. Initial create attempt failed with:

```
A validation error occurred. The length of the 'sprk_playbookcode' attribute of the
'sprk_analysisplaybook' entity exceeded the maximum allowed length of '10'.
(correlation ID: 62b54df6-2dc5-4fe1-9476-a3df62b2790b)
```

**Resolution applied**: Truncated to `BRIEF-NRRT` (10 chars) — preserves the `BRIEF-` prefix matching the Action codes (BRIEF-NARRATE-TLDR / BRIEF-NARRATE-CHANNEL / BRIEF-VALIDATE-ENTITY-NAMES) and conveys "narrate" intent via vowel-elision.

**Mitigations to preserve the canonical identifier**:
1. Stored `"canonicalPlaybookCode": "BRIEF-NARRATE"` inside the deployed `sprk_configjson` payload (alongside `category`, `channelLabel`).
2. Documented the truncation in the row's `sprk_description` so it surfaces in any list view / audit query.
3. Dispatch lookup at runtime should key on `sprk_analysisplaybookid` (GUID above) or canonicalPlaybookCode (configjson) rather than `sprk_playbookcode`.

**Escalation flag (🔔 OWNER REVIEW)**: The `sprk_playbookcode` 10-char limit is too tight for the established `{DOMAIN}-{INTENT}` naming convention (Action codes use `{DOMAIN}-{INTENT}-{SCOPE}` and exceed 20 chars on `sprk_analysisaction.sprk_actioncode`). Recommend a Dataverse schema fix to expand to NVARCHAR(50) to match the Action code column and accept the canonical `BRIEF-NARRATE` value. Filed for owner consideration; **does not block** task 011 acceptance because the row is queryable by GUID and the canonical code is preserved in configjson.

---

## Verification — read_query post-deploy

Header fields confirmed via:
```sql
SELECT sprk_analysisplaybookid, sprk_playbookcode, sprk_name, sprk_description,
       sprk_playbooktype, sprk_playbookmode, sprk_triggertype, sprk_capabilities,
       statecode, statuscode, sprk_ispublic, sprk_issystemplaybook
FROM sprk_analysisplaybook
WHERE sprk_analysisplaybookid = '7b5a6ed3-0271-f111-ab0e-000d3a13a4cd'
```

Result: `statecode = 0 (Active)`, `statuscode = 1 (Active)`, all fields match the deploy payload.

`sprk_configjson` confirmed non-empty (4,418 chars), parses as valid JSON, contains:
- `"category": "daily-briefing-narrate"`
- `"canonicalPlaybookCode": "BRIEF-NARRATE"` (preserved identity)
- `actionRefs` block referencing all 4 Action GUIDs from PR 1:
  - `BRIEF-NARRATE-TLDR`: `ce299eb4-fc70-f111-ab0e-7ced8ddc4cc6`
  - `BRIEF-NARRATE-CHANNEL`: `dc3533c0-fc70-f111-ab0e-7ced8ddc4cc6`
  - `BRIEF-VALIDATE-ENTITY-NAMES`: `290e786c-ff70-f111-ab0e-7ced8ddc4cc6`
  - `SYS-LOOKUP-MEMBERSHIP`: `ca44b7aa-fc70-f111-ab0e-7ced8ddc4cc6`
- All 6 nodes: `Start`, `LoadKnowledge`, `GenerateTldr`, `GenerateChannelNarratives`, `ValidateEntityNames`, `ReturnResponse`
- All 6 edges per repo source `edges` array
- `composeStrategy.fanOut` per ADR-037 multinode output composition
- `outputBinding.responseShape = "DailyBriefingNarrateResponse"`

---

## Pre-deploy referential integrity check (PASSED)

```sql
SELECT sprk_analysisactionid, sprk_actioncode, statecode
FROM sprk_analysisaction
WHERE sprk_actioncode IN ('BRIEF-NARRATE-TLDR','BRIEF-NARRATE-CHANNEL',
                          'BRIEF-VALIDATE-ENTITY-NAMES','SYS-LOOKUP-MEMBERSHIP')
```

All 4 Action rows present + Active in spaarkedev1 (matches GUIDs in tasks 005/006/007).

---

## Audit comparison result (jps-playbook-audit workflow)

Per the `jps-playbook-audit` skill, the workflow contract for this task is:
- **CHECK 3 Structural Integrity** — PASS (all 6 nodes have actionCode/actionType where applicable, all `dependsOn` references resolve to declared nodes, all `outputVariable` names unique)
- **CHECK 4 Standards Compliance** — PASS (description present, isPublic = true marked, 6 nodes < 10-node ceiling, 1-2 skills per AiAnalysis node via Action's compatibleActions)
- **Deployed configjson vs repo source-of-truth** — semantically equivalent (deployed payload is a serialized projection of the source JSON's `sprk_configjson` + `nodes[]` + `edges[]` arrays with repo-only annotation keys stripped: `_comment`, `_dataverseRow`, `componentJustification`, `metadata`, `r5BindingPlan._comment`)

Annotations stripped during deployment (intentional — these are repo source-of-truth doc-only fields, not Dataverse data):
- `$schema`, `$comment` (root)
- `_dataverseRow.*` (Dataverse deployment instructions)
- `_comment` keys throughout (inline documentation)
- `playbook.componentJustification` (CLAUDE.md §11 justification block — lives in repo only)
- `playbook.scopes` (PR 1 task scope; referenced via `actionRefs` in deployed configjson)
- `metadata.*` (author/createdAt/specReferences/sourceReferences/actionGuidsDeployed)

**Audit result**: ✅ CLEAN — deployed sprk_configjson semantically equivalent to repo source after annotation-strip.

---

## Acceptance criteria — task 011

| Criterion | Result |
|---|---|
| MCP read_query returns 1 row for `sprk_playbookcode = "BRIEF-NRRT"` with statecode = 0 | ✅ PASS (deviation noted: canonical "BRIEF-NARRATE" truncated to 10-char column max; canonical value preserved in configjson) |
| Deployed sprk_configjson semantically matches repo JSON from task 010 | ✅ PASS (annotations stripped per spec; node graph + edges + composeStrategy + actionRefs identical) |
| `jps-playbook-audit` skill returns clean for the deployed row | ✅ PASS (structural integrity + standards compliance + repo equivalence) |
| Deployment evidence captured | ✅ PASS (this file) |

---

## Next steps (downstream of 011)

- Task 012/013/014 — Audit 7 existing notification playbooks (parallel Group C)
- Task 017 — Smoke test BFF wrapper dispatch against this deployed playbook
- Task 031 — `/narrate` endpoint dispatches to playbook ID `7b5a6ed3-0271-f111-ab0e-000d3a13a4cd` via IConsumerRoutingService (Path A.5)

**Reference for downstream tasks**: Look up by **GUID** (`7b5a6ed3-0271-f111-ab0e-000d3a13a4cd`) or by **canonical code** in configjson (`canonicalPlaybookCode = "BRIEF-NARRATE"`) — not by truncated `sprk_playbookcode` field alone.
