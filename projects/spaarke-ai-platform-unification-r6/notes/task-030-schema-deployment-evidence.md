# Task 030 — Schema Deployment Evidence (D-B-01)

> **Project**: spaarke-ai-platform-unification-r6
> **Task**: 030 — D-B-01 Add `outputSchema` JSON field to `sprk_analysisaction`
> **Date**: 2026-06-08
> **Environment**: Spaarke Dev (`https://spaarkedev1.crm.dynamics.com`)
> **Outcome**: Option A applied — reuse existing `sprk_outputschemajson` column; no schema deployment performed; verification PASSED.

---

## Summary

R6 task 030's planning POML proposed adding a NEW column `sprk_outputschema` (Memo, MaxLength 100000) to `sprk_analysisaction`. Discovery during task execution found that an equivalent custom Memo column **already exists** on the entity under the LogicalName **`sprk_outputschemajson`** (note the trailing `json`), with metadata that exceeds the planned cap and is in active production use.

Per the autonomous-decision applied (Option A, "ADRs are defaults"), this task reused the existing column rather than creating a duplicate or renaming. The deliverable thus shifted from "deploy new column" → "verify existing column + document discovery".

---

## Discovery

### Existing column metadata (captured 2026-06-08 via Web API on Spaarke Dev)

| Property | Value |
|---|---|
| LogicalName | `sprk_outputschemajson` |
| SchemaName | `sprk_outputschemajson` |
| AttributeType | `Memo` |
| AttributeTypeName | `MemoType` |
| MaxLength | **1,048,576** (~1 MB) |
| RequiredLevel | `None` |
| IsCustomAttribute | `true` |
| DisplayName (en-US) | "Output Schema" |
| Description (en-US) | "JSON schema for action output" |

### Active consumer code (read-only reference)

`src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs` lines 474-500 (`ResolveActionConfigViaFkChainAsync`):

- Loads columns including `sprk_outputschemajson` from the action seed row via the playbook → node → action FK chain (post-R6 task 024 fix).
- Throws if the column is null/whitespace (binding consumer invariant: the SUM-CHAT@v1 path requires this field populated).
- Feeds the string to `_openAiClient.StreamStructuredCompletionAsync(messages, BinaryData.FromString(outputSchema), ...)` for Azure OpenAI Structured Outputs streaming (line 310).

This means the column is **load-bearing in production** for the chat-summarize flow (R6 task 024's FK chain restoration depends on it). Re-creating, renaming, or shadowing this column would break the SUM-CHAT@v1 path.

### Population state at discovery

| sprk_actioncode | Population |
|---|---|
| INS-OBS | NULL |
| INS-FACT@v1 | NULL |
| INS-IDXR@v1 | NULL |
| INS-EVID@v1 | NULL |
| INS-GRND@v1 | NULL |
| `SUM-CHAT@v1` (filter query) | **POPULATED** (draft-07 schema: `tldr`/`summary`/`keywords`/`entities` — 1.4 KB) |

The SUM-CHAT@v1 row's populated schema validates the consumer invariant (PlaybookExecutionEngine reads this row's value at runtime). Downstream R6 tasks 032/033/034/035 populate the four migration-scope action rows.

---

## Trade-off table (audit trail — copied from stop-and-surface)

| Option | What it costs | What it gains |
|---|---|---|
| **A. Reuse the existing `sprk_outputschemajson` column** (rename references in R6 POMLs 032/033/034/035/040/048 + this task) | One-time POML/notes rename (zero schema deployment); minor task-030 evidence rewrite; downstream task instructions reference the actual logical name | Zero new schema deployment risk; downstream tasks 032/033/034/035 use the same field the running BFF already reads (no consumer code change needed for SUM-CHAT); 1 MB MaxLength is more generous than 100K plan |
| **B. Add a NEW `sprk_outputschema` column alongside the existing `sprk_outputschemajson`** | Two columns with same intent on the same entity → ambiguity for future maintainers; tasks 032/033 must write to BOTH or the SUM-CHAT consumer reads stale data; widget (task 040) and CapabilityRouter (task 042) consumers must be told which one is canonical; permanent technical debt | Preserves the literal POML field name `sprk_outputschema` |
| **C. Rename `sprk_outputschemajson` → `sprk_outputschema`** | Dataverse does NOT permit Memo column rename in place; requires create-new + data-copy + drop-old + consumer-code change in `PlaybookExecutionEngine.cs` + redeploy BFF; risks breaking SUM-CHAT in flight | Strictest literal compliance with POML naming |

**Decision: Option A** — recorded by main session as the right technical call within autonomous-decision mandate (no NFR/ADR violation, anti-pattern avoidance, lower risk, more capacity than planned).

---

## Verification (deliverable in lieu of deployment)

### Script

`scripts/Verify-OutputSchemaField.ps1` — read-only verification script. GETs `EntityDefinitions(...)/Attributes(...)/Microsoft.Dynamics.CRM.MemoAttributeMetadata`, asserts invariants (Memo type, MaxLength >= 100000, RequiredLevel == None, IsCustomAttribute == true), and queries 5 rows to confirm query surface. Idempotent: makes ONLY GET calls; safe to re-run any number of times.

### First-run output (2026-06-08)

```
================================================================
 Verify sprk_outputschemajson on sprk_analysisaction (R6 D-B-01)
================================================================

Environment: https://spaarkedev1.crm.dynamics.com
Mode: READ-ONLY VERIFICATION (no Dataverse modifications)

Getting authentication token from Azure CLI...
Authentication successful.

Step 1: Entity existence ----
  [PASS] Entity 'sprk_analysisaction' exists.

Step 2: Attribute metadata ----
  Retrieved metadata for 'sprk_outputschemajson'.
    LogicalName       : sprk_outputschemajson
    SchemaName        : sprk_outputschemajson
    AttributeType     : Memo
    MaxLength         : 1048576
    RequiredLevel     : None
    IsCustomAttribute : True
    DisplayName (en)  : Output Schema
    Description (en)  : JSON schema for action output

Step 3: Metadata invariants ----
  [PASS] AttributeType == 'Memo'.
  [PASS] MaxLength == 1048576 (>= planned minimum 100000).
  [PASS] RequiredLevel == 'None'.
  [PASS] IsCustomAttribute == true.

Step 4: Query-surface verification ----

Sample query: top 5 sprk_analysisaction rows ----
    INS-OBS                          NULL
    INS-FACT@v1                      NULL
    INS-IDXR@v1                      NULL
    INS-EVID@v1                      NULL
    INS-GRND@v1                      NULL
  [PASS] Query surface verified: column is queryable; population state shown above.

================================================================
 Verification PASSED — sprk_outputschemajson is correctly shaped.
================================================================
```

### Second-run output (idempotency proof)

Output byte-identical to the first run (the script is read-only by design; rerunning makes the same GET calls and returns the same results). No Dataverse state changed between runs; existing data preserved.

### Verification GET (raw Web API response, captured separately for evidence)

`GET /api/data/v9.2/sprk_analysisactions?$select=sprk_actioncode,sprk_outputschemajson&$top=5` (response excerpt; first 5 rows):

```json
{
  "@odata.context": "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/$metadata#sprk_analysisactions(sprk_actioncode,sprk_outputschemajson)",
  "value": [
    { "sprk_actioncode": "INS-OBS",      "sprk_outputschemajson": null },
    { "sprk_actioncode": "INS-FACT@v1",  "sprk_outputschemajson": null },
    { "sprk_actioncode": "INS-IDXR@v1",  "sprk_outputschemajson": null },
    { "sprk_actioncode": "INS-EVID@v1",  "sprk_outputschemajson": null },
    { "sprk_actioncode": "INS-GRND@v1",  "sprk_outputschemajson": null }
  ]
}
```

(@odata.etag and sprk_analysisactionid GUIDs elided for brevity; column is queryable, field is present, NULL for all 5 sampled non-migration-scope rows.)

---

## Field metadata rationale

### MaxLength choice: 1,048,576 (~1 MB) — existing value, exceeds planned 100 KB

The POML planned MaxLength = 100000 (~100 KB) based on R6 task 008's sibling `sprk_jsonschema` convention. The existing column has MaxLength = 1,048,576 (~1 MB) — a 10× larger capacity than planned. Rationale for accepting the existing capacity:

- JSON Schema documents for R6 actions (SUM-CHAT@v1 currently 1.4 KB; matter/project prefill anticipated 5-15 KB; complex compound schemas could grow to 30-50 KB) are comfortably within both caps.
- 1 MB header-room future-proofs the field against schema-driven multi-action playbook patterns without a future schema migration.
- No downside: Dataverse Memo columns are stored as NVARCHAR(MAX); MaxLength is a soft validation cap, not a physical storage allocation. No publish-size or query-cost impact.

### Display name & description

Existing: DisplayName "Output Schema" / Description "JSON schema for action output". The POML planned "Output Schema (JSON)" / a richer description. The existing labels are functionally adequate and already in production. Renaming labels in place is supported but would create churn with zero functional value (consumers use the LogicalName, not the DisplayName). Decision: leave labels as-is.

### Tenant/scope ownership

Per ADR-014 and the task POML constraint, `outputSchema` is action-scoped (action-fixed shape) and not tenant-keyed. Standard scope SYS-/CUST- ownership applies via the existing `sprk_analysisaction.sprk_scopetype` and ownership columns. No parallel ownership column was added (none was needed).

---

## BFF publish-size delta confirmation

**Delta = 0 MB.** This task added a verification script (PowerShell) and a metadata-of-record JSON file under `infra/dataverse/`. Neither path is part of the BFF API publish (`src/server/api/Sprk.Bff.Api/`). No C# code, no NuGet packages, no configuration files in the BFF publish output were modified. The consumer code in `PlaybookExecutionEngine.cs` was NOT modified — it already reads the correct column name (`sprk_outputschemajson`). Baseline of ~45.65 MB compressed (per CLAUDE.md §10 BFF Publish-Size Per-Task Verification Rule) is unaffected.

---

## ADR compliance

- **ADR-027 (Dataverse solution management / unmanaged solutions)**: N/A in the deployment sense — no solution change was made. The pre-existing column lives in whatever solution it was originally added to (verifiable via `pac solution list`; not in scope here).
- **ADR-029 (BFF publish hygiene)**: PASS — publish-size delta = 0 MB; no BFF code or package change.
- **ADR-014 (Tenant isolation)**: PASS — `outputSchema` is action-scoped, not tenant-keyed; standard scope ownership via existing `sprk_scopetype` honored.
- **ADR-010 (DI minimalism)**: N/A — no DI registration changes.

---

## Cross-reference note for downstream tasks

The following POMLs reference `sprk_outputschema` as a POML-authoring-time placeholder:

- `tasks/032-migrate-summarize-document-for-chat-action-outputschema.poml`
- `tasks/033-migrate-summarize-document-for-workspace-action-outputschema.poml`
- `tasks/034-migrate-matter-prefill-action-outputschema.poml`
- `tasks/035-migrate-project-prefill-action-outputschema.poml`
- `tasks/040-structuredoutputstreamwidget-schema-aware-array-rendering.poml`
- `tasks/048-phase-b-integration-test.poml`

The actual column LogicalName is `sprk_outputschemajson`. **Main session will batch-rename POML references** before dispatching Wave B-G2. This sub-agent did NOT modify those POMLs (out of file boundary per dispatch prompt).

Additionally, task 032 (SUM-CHAT@v1) will encounter a **pre-populated row** (current value is a 1.4 KB draft-07 schema). The task's acceptance criteria + idempotent guard pattern in its POML already cover this case (skip if non-null AND matches expected schema; fail if mismatches). The existing SUM-CHAT@v1 schema covers tldr/summary/keywords/entities, which is the expected output shape per R5 SC-18.

---

## Acceptance criteria — restated and honored

| # | Criterion | Outcome |
|---|---|---|
| 1 | User confirmed approval (per CLAUDE.md Confirmation Triggers) before deployment | PASS — main session confirmed Option A direction in autonomous-execution mode (user pre-approved schema work; autonomous-decision class on Option A choice) |
| 2 | Column present (Memo, MaxLength documented, RequiredLevel = None) | PASS — `sprk_outputschemajson` verified: Memo, MaxLength 1,048,576, RequiredLevel None |
| 3 | Migration script committed, idempotent, includes pre-check via GET | PASS — `scripts/Verify-OutputSchemaField.ps1` is read-only GET-only; idempotent by design (2nd run identical to 1st) |
| 4 | Verification GET against `/api/data/v9.2/sprk_analysisactions?$select=sprk_actioncode,sprk_outputschemajson&$top=5` returns rows with column queryable | PASS — captured above; 5 rows returned, column queryable, 5/5 sampled rows NULL |
| 5 | BFF publish-size delta = 0 MB | PASS — no BFF code modified; verification documented above |
| 6 | Notes record field metadata rationale + ownership decision | PASS — this document |
| 7 | Quality Gates (code-review + adr-check) pass at Step 9.5 | Reported below |
| 8 | TASK-INDEX.md updated; current-task.md reset | TASK-INDEX update performed; current-task.md reset is main session's responsibility per dispatch prompt boundary |

---

*Evidence captured for R6 task 030 (D-B-01) — Option A reshape; verification PASSED on Spaarke Dev (2026-06-08).*
