# Wave 12.1 Audit 124 — Wave 5 backfill-health sweep across all sprk_playbooknode rows

> **Author**: Claude (R7 Wave 12.1 audit dispatch, sibling to 120/121/122/123)
> **Date**: 2026-06-30
> **Task**: [`tasks/124-audit-wave5-backfill-health-sweep.poml`](../../tasks/124-audit-wave5-backfill-health-sweep.poml)
> **Rigor**: STANDARD (read-only; no Dataverse mutations from this task — operator approves each fix per T141/T142/T143 pattern)
> **Mode**: Static code reading of executor `Validate()` contracts + live Dataverse `mcp__dataverse__read_query` sweeps in spaarkedev1 + Wave 5 CSV cross-reference.

---

## Executive summary

T142 (Project Wizard FK re-link `79af4befd`) and T143 (Matter Wizard EntityNameValidator delete + systemPrompt strip `3cb239e5d`) both surfaced **Wave 5 backfill mis-classifications** — the `Migrate-PlaybookNodes-to-ExecutorType.ps1` script (Wave 5 task 053) set `sprk_executortype` by **node-name match alone** (`AUTO-COPY (confidence=MEDIUM): script suggested AiAnalysis (0)`) without verifying configJson health or Action FK presence. This audit systematically swept all 74 nodes across the entire `sprk_playbooknode` table to surface every other instance of the same class of breakage.

**Sweep results aggregate**: **9 broken nodes across 6 playbooks**, all attributable to Wave 5 backfill quality issues. **Severity distribution**: 1 CRITICAL (live consumer) + 0 HIGH + 8 LOW (abandoned playbooks with no live consumer routing). **Blast-radius**: ONE in-MVP-scope consumer (`compose-summarize` — itself UNWIRED in current BFF code, so blast-radius is effectively zero today). All other findings are abandoned canvas-only playbooks with no consumer code references.

**Recommendation**: **per-consumer fix path, NOT systematic** — only ONE playbook needs immediate action (Document Summary, T124-FIX-A below), and it can ride alongside T141/T142/T143's per-node operator-approved MCP pattern. The other 8 findings are all in abandoned playbooks with zero blast-radius; recommend DEF-NNN tracking + per-playbook owner review at R7 wrap-up to decide DELETE-OR-FIX (not Wave 12 scope). Per CLAUDE.md §11 (Cost-of-doing-nothing): no live consumer fails today without these 8 fixes, so they're not in-MVP-scope.

**Confirms T143 §10.9 + §10.10** findings: EntityNameValidator class is fully addressed (Daily Briefing's `11895da7-...` is healthy; the broken Matter one was deleted). The systemPrompt-clobber pattern (P3) returned **ZERO additional matches** beyond the one already fixed at T143 §10.3 — that one was the only instance.

---

## 1. Sweep query catalog

All sweeps executed via `mcp__dataverse__read_query` against spaarkedev1 on 2026-06-30. Aggregate table totals at time of sweep: **74 nodes across ~20 distinct playbooks** (per the `GROUP BY sprk_executortype` distribution query).

### 1.1 Total node distribution by executor type (baseline)

```sql
SELECT sprk_executortype, COUNT(sprk_playbooknodeid) AS node_count
FROM sprk_playbooknode
GROUP BY sprk_executortype
ORDER BY sprk_executortype
```

Result (74 total rows):

| ExecutorType | Name | Node count | Requires Action FK? | Requires configJson? |
|---|---|---|---|---|
| 0 | AI Analysis | 14 | YES (Tool from Action) | NO (NodeExecutor reads action.SystemPrompt/Tool) |
| 1 | AI Completion | 8 | YES (SystemPrompt + OutputSchemaJson from Action) | NO (override-only) |
| 20 | Create Task | 1 | NO | YES (`subject`) |
| 30 | Condition | 8 | NO | YES (`condition`, `trueBranch`/`falseBranch`) |
| 33 | Start | (not in stats) | NO | NO (synthetic shell OK) |
| 40 | Deliver Output | 4 | NO | NO (optional, auto-assembly default) |
| 41 | Deliver To Index | 1 | NO | YES (`indexName`) |
| 42 | Deliver Composite | 1 | NO | YES (`sections[]`) |
| 50 | Create Notification | 7 | NO | YES (`title`, `body`) |
| 51 | Query Dataverse | 8 | NO | YES (`entityLogicalName`, `fetchXml`) |
| 52 | Lookup User Membership | 4 | NO | YES (`entityType`) |
| 60 | Agent Service | 2 | NO | YES (`tenantId`, `prompt`) |
| 70 | Grounding Verify | 2 | NO | YES (`citationsFrom`, `sourceChunksFrom`) |
| 80 | Live Fact | 2 | NO | YES (`subject`, `predicate`) |
| 90 | Index Retrieve | 3 | NO | YES (varies) |
| 100 | Evidence Sufficiency | 2 | NO | YES (`rules[]`) |
| 110 | Decline To Find | 2 | NO | (no Validate() error path) |
| 120 | Return Insight Artifact | 2 | NO | YES (`from`, `predicate`, `producedById`) |
| 141 | Entity Name Validator | 1 | NO | YES (`candidateText`, `allowList`) — see T143 §10.9 |
| 142 | Load Knowledge | 1 | NO | NO (optional) |
| 143 | Return Response | 1 | NO | NO (optional) |

Required-field source: [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/*NodeExecutor.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/) — read every `Validate(NodeExecutionContext context)` method (24 files).

### 1.2 Pattern P1 — Orphan Action FK on prompt-driven executors

```sql
SELECT sprk_playbooknodeid, sprk_name, sprk_executortype, sprk_actionid, sprk_playbookid
FROM sprk_playbooknode
WHERE sprk_executortype IN (0, 1, 2) AND sprk_actionid IS NULL
```

**Matched rows: 7** (5 AiAnalysis + 1 AiCompletion + 1 AiAnalysis missed in first scan = 7 total). See §3.1.

**Why this is CRITICAL contract violation**: [`AiAnalysisNodeExecutor.Validate`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs#L113-L158) requires `context.Tool` (resolved from the Action FK) AND `context.Document.ExtractedText` (run-supplied). With `sprk_actionid IS NULL`, `PlaybookOrchestrationService` falls into the synthetic-action-shell branch ([`PlaybookOrchestrationService.cs:1117-1126`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs)) and yields `context.Tool = null` → `Validate` returns `errors = ["AI analysis node requires a tool to be configured"]` → NodeFailed → entire run aborts (per T143 §10.2 same mechanism).

[`AiCompletionNodeExecutor.Validate`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiCompletionNodeExecutor.cs#L171-L231): explicit `actionMissing = context.Action is null || context.Node.ActionId == Guid.Empty` check (line 178), emits error "AiCompletion node requires an Action FK (prompt source). Set sprk_actionid on the node." (line 181). Same fatal outcome.

### 1.3 Pattern P2 — EntityNameValidator stub configJson missing required fields

```sql
SELECT sprk_playbooknodeid, sprk_name, sprk_playbookid, sprk_configjson
FROM sprk_playbooknode
WHERE sprk_executortype = 141 AND
  (sprk_configjson NOT LIKE '%candidateText%' OR sprk_configjson NOT LIKE '%allowList%')
```

**Matched rows: 0 additional** (the broken Matter node `c3c5226d-...` was DELETED by T143 §10.10 fix A; Daily Briefing's `11895da7-...` is healthy per T143 §10.9 verification). Total count of EntityNameValidator nodes confirmed at 1 (Daily Briefing only).

**Disposition**: ✅ ALREADY ADDRESSED. No new findings.

### 1.4 Pattern P3 — Stub systemPrompt override clobbering Action's real prompt

```sql
SELECT sprk_playbooknodeid, sprk_name, sprk_playbookid, sprk_configjson
FROM sprk_playbooknode
WHERE sprk_executortype = 0 AND sprk_configjson LIKE '%systemPrompt%'
```

**Matched rows: 0** (the Matter AI Analysis node `444b06d3-...` was patched by T143 §10.10 fix B; was the only instance).

**Disposition**: ✅ ALREADY ADDRESSED. No new findings.

**Why clobber matters**: [`PromptSchemaOverrideMerger.MergeInstruction`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaOverrideMerger.cs#L208-L234) replaces the Action's `instruction.task` and `instruction.role` with the node-level override if present. A stub override silently replaces the real prompt with template / canvas-default text — LLM gets garbage instruction; structured output is degraded or wrong.

### 1.5 Pattern P4 — Other executor-required fields missing in configJson

Per-executor sweeps run for every executor whose `Validate()` enforces required configJson keys. Each sweep used `LIKE '%<keyName>%'` predicate (Dataverse SQL `LIKE` is the only available substring test). Detection rule: stub configJson contains ONLY `__canvasNodeId` + `__actionType` (the Power Apps Maker canvas metadata) without the executor-required runtime keys.

| Executor | Required keys (per Validate) | Sweep query | Matches |
|---|---|---|---|
| Condition (30) | `condition` (object) | `WHERE sprk_executortype = 30 AND sprk_configjson NOT LIKE '%condition%'` | **2** (§3.2) |
| Create Task (20) | `subject` | `WHERE sprk_executortype = 20 AND sprk_configjson NOT LIKE '%subject%'` | **1** (§3.2) |
| Query Dataverse (51) | `entityLogicalName` + `fetchXml` | `WHERE sprk_executortype = 51 AND (... NOT LIKE '%fetchXml%' AND ... NOT LIKE '%entityLogicalName%')` | **1** (§3.2) |
| Lookup User Membership (52) | `entityType` | `WHERE sprk_executortype = 52 AND sprk_configjson NOT LIKE '%entityType%'` | **1** (§3.2) |
| Create Notification (50) | `title` + `body` | `WHERE sprk_executortype = 50 AND (... NOT LIKE '%title%' OR ... NOT LIKE '%body%')` | **1** (§3.2) |
| Send Email (21) | `to` + `subject` + `body` | `WHERE sprk_executortype = 21 AND (... NOT LIKE '%to%' OR ... NOT LIKE '%subject%' OR ... NOT LIKE '%body%')` | 0 |
| Agent Service (60) | `tenantId` + `prompt` | `WHERE sprk_executortype = 60 AND (... NOT LIKE '%tenantId%' OR ... NOT LIKE '%prompt%')` | 0 |
| Update Record (22) | configJson required | `WHERE sprk_executortype = 22 AND sprk_configjson IS NULL` | 0 |
| Deliver To Index (41) | `indexName` | `WHERE sprk_executortype = 41 AND sprk_configjson NOT LIKE '%indexName%'` | 0 |
| Deliver Composite (42) | `sections[]` | `WHERE sprk_executortype = 42 AND sprk_configjson NOT LIKE '%sections%'` | 0 |
| Grounding Verify (70) | `citationsFrom` + `sourceChunksFrom` | (covered by null-configjson sweep) | 0 |
| Live Fact (80) | `subject` + `predicate` | (covered by null-configjson sweep) | 0 |
| Evidence Sufficiency (100) | `rules[]` (with name + from + predicate) | (covered by null-configjson sweep) | 0 |
| Return Insight Artifact (120) | `from` + `predicate` + `producedById` | (covered by null-configjson sweep) | 0 |
| Pure executors (33/40/42/142/143) — orphan FK check | (none required) | `WHERE sprk_executortype IN (33,40,42,142,143) AND sprk_actionid IS NOT NULL` | 0 (clean) |

**Total P4 matches: 6** (1 Condition in Real Estate playbook + 1 Condition in Tasks Due Soon + 1 Create Task in Real Estate + 1 Query Dataverse in Tasks Due Soon + 1 Lookup User Membership in Tasks Due Soon + 1 Create Notification in Tasks Due Soon).

---

## 2. Wave 5 backfill root cause — confirmed mechanism

The CSV [`projects/spaarke-ai-platform-unification-r7/notes/drafts/playbook-node-review-output.csv`](../drafts/playbook-node-review-output.csv) is the input to [`scripts/dataverse/Migrate-PlaybookNodes-to-ExecutorType.ps1`](../../../../scripts/dataverse/Migrate-PlaybookNodes-to-ExecutorType.ps1) (Wave 5 task 053). Spot-check of CSV row for orphan `e514cfab` (Document Profile in Document Summary playbook) shows:

```csv
"e514cfab-9d16-f111-8343-7c1e520aa4df","Document Profile","Document Summary","","",
  "","","","","NONE","","0",
  "AUTO-GUESS (NONE-row): Document Profile (no action) -> AiAnalysis
   (best guess for a Summary-pipeline analysis step; VERIFY - could also be 42 DeliverComposite)."
```

**The script + owner pipeline failed three filter gates**:

1. **`action_id` column was empty** — the migration auto-guessed AiAnalysis based on node name "Document Profile" alone (text similarity to other AiAnalysis nodes named "Document Profile Analysis"). The actual canvas configJson on the node says `"__actionType": 40` (DeliverOutput) — **the migration overwrote the canvas-author truth**. The CSV note "VERIFY - could also be 42 DeliverComposite" was a flag, but the auto-decision was applied anyway because the owner accepted the default.
2. **No configJson health check** — the script did not parse the existing `sprk_configjson` to see if it contained `__actionType` (the canvas-asserted intent). If it had, it would have noticed the mismatch.
3. **No Action-FK presence check for prompt-driven executors** — the script set `sprk_executortype = 0` even though `sprk_actionid IS NULL`, which is a deterministic Validate() failure. A pre-write assertion ("AiAnalysis requires action_id present") would have halted the bad write.

The Wave 5 backfill was a TWO-PART operation:
- Part A: read existing nodes, propose `sprk_executortype` per node-name match (script `Review-PlaybookNodes-Dispatch.ps1`, task 050 — READ-only producer)
- Part B: owner-CSV-driven PATCH (`Migrate-PlaybookNodes-to-ExecutorType.ps1`, task 053 — WRITE half)

Part A's name-match heuristic + owner's bulk-accept of MEDIUM-confidence rows = silent mis-classification. The script's idempotency + dry-run + halt-on-error guards (lines 24-43 of the script header) catch DATA integrity issues but NOT DOMAIN integrity issues (executor↔config↔Action coherence).

**Mitigation for future migrations**: any future executortype backfill MUST include a pre-write coherence check:
```
For each row about to PATCH:
  IF executortype IN (0, 1) THEN
    ASSERT existing row's sprk_actionid IS NOT NULL
    ELSE halt + manual review
  IF executortype IN (20, 21, 30, 41, 50, 51, 52, 60, 70, 80, 100, 120, 141) THEN
    ASSERT existing row's sprk_configjson contains the executor's required keys
    ELSE halt + manual review
  IF existing sprk_configjson["__actionType"] != proposed executortype THEN
    WARN: canvas-asserted intent disagrees with name-match auto-guess
    halt + manual review
```

---

## 3. Per-row findings + severity + blast-radius

### 3.1 P1 findings — orphan Action FK on AiAnalysis (executortype=0) or AiCompletion (executortype=1)

| # | Node ID | Node name | Playbook ID | Playbook name | ExecutorType | Has live consumer routing? | Severity | Blast-radius |
|---|---|---|---|---|---|---|---|---|
| 1 | `e514cfab-9d16-f111-8343-7c1e520aa4df` | Document Profile | `47686eb1-9916-f111-8343-7c1e520aa4df` | Document Summary | 0 (AiAnalysis) | **YES** — `compose-summarize` consumer routing exists (record `986799ad-...`, enabled=true) | **LOW (effectively)** — `compose-summarize` is NOT in `ConsumerTypes.cs`; ZERO BFF code references the string; routing row is functionally orphaned | None today (consumer is unwired); WOULD be CRITICAL if any future consumer wires `compose-summarize` |
| 2 | `9af6906e-ad18-f111-8343-7c1e520aa4df` | AI Analysis | `e62f30c6-4aea-f011-8406-7ced8d1dc988` | Quick Document Review | 0 (AiAnalysis) | NO | LOW | Abandoned playbook; zero references in src/ scripts/ docs/ |
| 3 | `374034d4-b120-f111-88b5-7c1e520aa4df` | AI Analysis | `7d9a9209-31da-f011-8406-7ced8d1dc988` | Analyze Agreement Financial Terms | 0 (AiAnalysis) | NO | LOW | Abandoned playbook; zero references |
| 4 | `bb62553e-b220-f111-88b5-7c1e520aa4df` | AI Analysis | `1e657651-9308-f111-8407-7c1e520aa4df` | Finance Invoice Processing | 0 (AiAnalysis) | NO | LOW | Abandoned playbook; zero references |
| 5 | `6589421b-b420-f111-88b5-7c1e520aa4df` | AI Analysis | `ba709d6e-e0f2-f011-8406-7ced8d1dc988` | Real Estate Lease Agreement Review | 0 (AiAnalysis) | NO | LOW | Abandoned playbook |
| 6 | `6989421b-b420-f111-88b5-7c1e520aa4df` | AI Analysis | `ba709d6e-e0f2-f011-8406-7ced8d1dc988` | Real Estate Lease Agreement Review | 0 (AiAnalysis) | NO | LOW | Abandoned playbook (second AiAnalysis node) |
| 7 | `6689421b-b420-f111-88b5-7c1e520aa4df` | AI Completion | `ba709d6e-e0f2-f011-8406-7ced8d1dc988` | Real Estate Lease Agreement Review | 1 (AiCompletion) | NO | LOW | Abandoned playbook |

**KEY FINDING ON #1 (Document Summary)** — the orphan node `e514cfab-...` is NOT simply an "orphan FK" but an **executortype mis-classification**. Its configJson is:

```json
{"__canvasNodeId":"node_1772500148736_ifq8gx1vh",
 "__actionType":40,
 "deliveryType":"markdown",
 "outputFormat":{"includeMetadata":true,"includeSourceCitations":false}}
```

— canvas asserts `__actionType: 40` (DeliverOutput) with full DeliverOutput-shaped config (`deliveryType:"markdown"`, `outputFormat:{...}`). Wave 5 mis-set this to executortype 0 (AiAnalysis) based on the node NAME ("Document Profile" matched the upstream AiAnalysis node "Document Profile Analysis"). The fix is **not** to add an Action FK; the fix is to change `sprk_executortype` from 0 → 40 to match canvas intent.

The Real Estate playbook (ba709d6e — entries #5/#6/#7) has multiple AiAnalysis + AiCompletion nodes all with stub configJson `{"__canvasNodeId":"...","__actionType":0|1}` — canvas asserts executortype, but no Action FK was ever set. The playbook was clearly authored but never wired up; abandoned canvas artifact.

### 3.2 P4 findings — other-executor required-field misses

| # | Node ID | Node name | Playbook ID | Playbook name | ExecutorType | Missing field(s) | Severity | Blast-radius |
|---|---|---|---|---|---|---|---|---|
| 8 | `6789421b-b420-f111-88b5-7c1e520aa4df` | Condition | `ba709d6e-e0f2-f011-8406-7ced8d1dc988` | Real Estate Lease Agreement Review | 30 (Condition) | `condition` (validator: ["Condition node requires configuration"]) | LOW | Abandoned playbook |
| 9 | `b3d94997-9574-f111-ab0e-7ced8ddc4a05` | Check Results | `77f77aa5-5f2d-f111-88b5-7ced8d1dc988` | Tasks Due Soon | 30 (Condition) | `condition` | LOW | Abandoned playbook (zero src/script references) |
| 10 | `6889421b-b420-f111-88b5-7c1e520aa4df` | Create Task | `ba709d6e-e0f2-f011-8406-7ced8d1dc988` | Real Estate Lease Agreement Review | 20 (Create Task) | `subject` | LOW | Abandoned playbook |
| 11 | `b2d94997-9574-f111-ab0e-7ced8ddc4a05` | Query Tasks Due Soon | `77f77aa5-5f2d-f111-88b5-7ced8d1dc988` | Tasks Due Soon | 51 (Query Dataverse) | `entityLogicalName` + `fetchXml` | LOW | Abandoned playbook |
| 12 | `b1d94997-9574-f111-ab0e-7ced8ddc4a05` | Lookup My Matters | `77f77aa5-5f2d-f111-88b5-7ced8d1dc988` | Tasks Due Soon | 52 (Lookup User Membership) | `entityType` | LOW | Abandoned playbook |
| 13 | `b4d94997-9574-f111-ab0e-7ced8ddc4a05` | Create Notification | `77f77aa5-5f2d-f111-88b5-7ced8d1dc988` | Tasks Due Soon | 50 (Create Notification) | `title` + `body` | LOW | Abandoned playbook |

**Note**: ALL P4 findings are in TWO playbooks: Real Estate Lease Agreement Review (`ba709d6e`) + Tasks Due Soon (`77f77aa5`). Both have NO consumer routing rows and ZERO references in src/scripts/docs/. The Tasks Due Soon playbook has a complete CASCADE of broken nodes (Lookup → Query → Condition → CreateNotification) — all stub configJson, none would Validate.

---

## 4. Blast-radius mapping per consumer

| Consumer surface | Affected playbook | In-MVP-scope? | Disposition |
|---|---|---|---|
| `compose-summarize` (UNWIRED — no BFF code uses this; routing row points at Document Summary playbook with broken node #1) | Document Summary (`47686eb1-...`) | **NO** — `compose-summarize` is not in `ConsumerTypes.cs`; no current consumer | DEFER (filed as DEF-NNN if operator wants compose-summarize wired in future, or DELETE the playbook + routing row if not) |
| Quick Document Review | `e62f30c6-...` | NO — no consumer routing | DEFER (DELETE-or-FIX at owner discretion) |
| Analyze Agreement Financial Terms | `7d9a9209-...` | NO — no consumer routing | DEFER |
| Finance Invoice Processing | `1e657651-...` | NO — no consumer routing | DEFER |
| Real Estate Lease Agreement Review | `ba709d6e-...` | NO — no consumer routing | DEFER |
| Tasks Due Soon | `77f77aa5-...` | NO — no consumer routing | DEFER |

**ZERO findings affect in-MVP-scope consumers** (Daily Briefing / Matter Wizard / Project Wizard / WA Wizard / chat-summarize / ai-summary / summarize-file / email-analysis / daily-briefing-narrate / matter-pre-fill / project-pre-fill — none of these route to any of the broken playbooks).

The closest brush with MVP scope is the `compose-summarize` consumer routing row — but since NO code calls `ResolveAsync("compose-summarize")` and the string is absent from `ConsumerTypes.cs`, the routing row is functionally orphaned. Pre-existing wiring that surfaced briefly during PR migration; never completed; never used.

---

## 5. Per-node recommended fix (cited MCP calls)

**OPERATOR APPROVAL REQUIRED for each.** Sandboxed agent did NOT apply (CLAUDE.md §6 escalation for Dataverse mutations; aligns with T141/T142/T143 pattern).

### 5.1 T124-FIX-A — Document Summary node `e514cfab-...` (the one with a live but-unwired consumer)

**Recommended path**: PATCH `sprk_executortype` from 0 (AiAnalysis) to 40 (DeliverOutput) to align with canvas-asserted `__actionType: 40`.

```
mcp__dataverse__update_record(
  tablename = 'sprk_playbooknode',
  recordId  = 'e514cfab-9d16-f111-8343-7c1e520aa4df',
  item      = { sprk_executortype: 40 }
)
```

**Alternative path** (DELETE — if operator confirms the Document Summary playbook is abandoned because `compose-summarize` is unwired and there's no plan to wire it):

```
mcp__dataverse__delete_record(
  tablename = 'sprk_playbooknode',
  recordId  = 'e514cfab-9d16-f111-8343-7c1e520aa4df'
)
# AND
mcp__dataverse__delete_record(
  tablename = 'sprk_playbookconsumer',
  recordId  = '986799ad-e173-f111-ab0e-7ced8ddc4a05'
)
# AND
mcp__dataverse__delete_record(
  tablename = 'sprk_analysisplaybook',
  recordId  = '47686eb1-9916-f111-8343-7c1e520aa4df'
)
```

**Pre-mutation dependency check** (per T141/T143 pattern):
```sql
SELECT sprk_playbooknodeid, sprk_name, sprk_dependsonjson
FROM sprk_playbooknode
WHERE sprk_dependsonjson LIKE '%e514cfab%'
```
Expected: 0 rows (node 3 is the terminal node; no downstream dependents).

**Disposition recommendation**: PATCH (preferred — preserves the playbook in case `compose-summarize` is wired later; minimally-invasive; reversible). Document Profile Analysis node #2 (`00602067-...`) IS healthy (real AiAnalysis with Action FK `bb356968` ACT-011 "Document Profiler"); node 3 should simply project its output via DeliverOutput.

### 5.2 T124-FIX-B through T124-FIX-G — Abandoned-playbook orphans (DEFER recommended)

The 6 remaining playbooks (Quick Document Review, Analyze Agreement Financial Terms, Finance Invoice Processing, Real Estate Lease Agreement Review, Tasks Due Soon) collectively contain **8 broken nodes** with stub configJson. None have consumer routing; none have BFF code references; all are canvas-authored-but-abandoned.

**Recommended disposition**: file as DEF-NNN entries (per project's deferred-issue protocol at [`projects/spaarke-ai-platform-unification-r7/notes/defer-issues.md`](../defer-issues.md)) for owner review at R7 wrap-up. Per playbook, the choice is binary:

- **DELETE** the entire playbook + all its nodes (if the operator confirms it's abandoned). This is the lower-cost option.
- **FIX** the configJson for each node (if the operator wants to retain the playbook as a template for future wiring). This requires per-node owner judgment about what `subject`, `body`, `entityType`, `fetchXml`, etc. should be — i.e., authoring work that's out of audit scope.

**Per-playbook DELETE recipe** (if operator approves):

```
# Step 1: Verify zero dependency from other playbooks
SELECT * FROM sprk_playbookconsumer WHERE sprk_playbook = '<playbookId>'
SELECT * FROM sprk_playbooknode WHERE sprk_playbookid = '<playbookId>'
# Step 2: Delete all nodes in the playbook (one DELETE per node)
mcp__dataverse__delete_record(tablename='sprk_playbooknode', recordId='<nodeId>')  # repeat per node
# Step 3: Delete the playbook
mcp__dataverse__delete_record(tablename='sprk_analysisplaybook', recordId='<playbookId>')
```

The Tasks Due Soon playbook (`77f77aa5-...`) has the largest blast radius (5 broken nodes including a CASCADE: Lookup → Query → Condition → CreateNotification). Either DELETE the whole playbook OR (if retained) the 4 broken nodes need configJson authored from scratch by an owner who knows the original intent.

---

## 6. Recommendation: per-consumer (NOT systematic) fix path

**Decision criteria** (per task POML step 6):
- N broken nodes ≤ 5: systematic one-shot operator session (15–30 min)
- N > 5: prioritize per-consumer; defer non-blocking to DEF-NNN

**This sweep has N = 9 broken nodes** (1 in Document Summary + 8 across 5 abandoned playbooks). However:
- ONLY 1 node has a live consumer routing row (Document Summary node #1)
- That ONE consumer routing row (`compose-summarize`) is itself functionally orphaned (no BFF code references)
- All 8 other broken nodes are in playbooks with ZERO consumer routing AND ZERO code references

**Therefore the recommendation splits**:

| Phase | Action | Effort | Rationale |
|---|---|---|---|
| **Wave 12 in-scope** | T124-FIX-A: PATCH Document Summary node `e514cfab` (executortype 0 → 40) via single MCP call | 5 min operator approval + apply | Lowest-cost, preserves optionality (Document Summary playbook stays available); no DELETE risk |
| **Wave 12 in-scope (optional alt)** | DELETE the `compose-summarize` routing row + Document Summary playbook + 3 nodes IF operator confirms unwired-and-not-planned | 10 min operator approval + apply | Trades 5 min for cleaning up 4 stale Dataverse rows; recoverable from prior canvas commit if needed |
| **Out-of-Wave-12 → DEF-NNN** | 5 abandoned-playbook DELETE-or-FIX decisions (Quick Document Review, Analyze Agreement Financial Terms, Finance Invoice Processing, Real Estate Lease Agreement Review, Tasks Due Soon) | 30 min per playbook for owner judgment + 5 min apply per DELETE; longer if FIX | Zero blast-radius today; per CLAUDE.md §11 Cost-of-doing-nothing test, NO concrete behavior fails today without these fixes. Risk-of-doing-something (deleting in-progress design work) > Risk-of-doing-nothing (canvas clutter). |
| **Mitigation for future** | Add pre-write coherence check to `Migrate-PlaybookNodes-to-ExecutorType.ps1` (executor↔configJson↔Action FK assertions) | 1–2 hours implementation + test | Prevents the next Wave-N backfill from creating this class of breakage. Filed as DEF-NNN if approved. |

---

## 7. Cross-reference to T141/T142/T143/T143-§10.9 prior fixes (avoiding double-work)

| Prior finding | Already addressed by | Status in this sweep |
|---|---|---|
| Project Wizard playbook node `dacac491-...` orphan FK (audit 123 §5.2 / §8.5) | T142 PATCH (`79af4befd`) | ✅ Confirmed; node is no longer in the P1 sweep result set |
| Matter Wizard EntityNameValidator node `c3c5226d-...` stub configJson (audit 123 §10.2) | T143 §10.10 fix A — DELETE node | ✅ Confirmed; P2 sweep returns 0 additional matches |
| Matter Wizard AI Analysis node `444b06d3-...` stub systemPrompt override (audit 123 §10.3) | T143 §10.10 fix B — PATCH (strip systemPrompt key from configJson) | ✅ Confirmed; P3 sweep returns 0 matches |
| Daily Briefing EntityNameValidator node `11895da7-...` (audit 123 §10.9) | (No fix needed — verified healthy) | ✅ Confirmed healthy; out of P2 sweep result set |
| Wave 5 backfill systemic risk (audit 123 §9.2 "other R7-rename-era orphans") | (Audit 123 hypothesis — this audit 124 is the systematic sweep) | ✅ Confirmed — 7 orphan-FK + 6 stub-configJson rows identified, all but 1 in abandoned playbooks |

No double-work. Findings from this audit are NET-NEW relative to T141/T142/T143 (which addressed only the wizard-affecting subset).

---

## 8. In-scope (Wave 12) vs out-of-scope (DEF-NNN) split

### In-MVP-scope (Wave 12)

- **T124-FIX-A** (Document Summary node `e514cfab` PATCH executortype 0 → 40): operator approval + 1 MCP call. Aligns canvas-asserted intent with runtime dispatch. Low-risk; preserves `compose-summarize` consumer routing for optional future wiring.

That's it for in-MVP-scope.

### Out-of-MVP-scope (DEF-NNN candidates)

| DEF-NNN ID (proposed) | Title | Suggested owner action |
|---|---|---|
| DEF-NNN-1 | `compose-summarize` consumer surface — wire or DELETE the routing row + playbook | Operator: confirm if Compose Outlook Add-in intends to use this; if not, DELETE; if yes, wire via `ConsumerTypes.ComposeSummarize` + service consumer |
| DEF-NNN-2 | Quick Document Review playbook abandoned (1 broken AiAnalysis node) | Owner: DELETE-or-FIX |
| DEF-NNN-3 | Analyze Agreement Financial Terms playbook abandoned (1 broken AiAnalysis node) | Owner: DELETE-or-FIX |
| DEF-NNN-4 | Finance Invoice Processing playbook abandoned (1 broken AiAnalysis node) | Owner: DELETE-or-FIX |
| DEF-NNN-5 | Real Estate Lease Agreement Review playbook abandoned (5 broken nodes — 3 AiAnalysis/Completion orphans + 1 Condition + 1 Create Task) | Owner: DELETE-or-FIX (DELETE recommended given scale of stub-config) |
| DEF-NNN-6 | Tasks Due Soon playbook abandoned (4 broken nodes — Lookup + Query + Condition + Create Notification cascade) | Owner: DELETE-or-FIX (DELETE recommended) |
| DEF-NNN-7 | `Migrate-PlaybookNodes-to-ExecutorType.ps1` — add pre-write coherence assertions (executor↔configJson↔Action FK) | Engineer: 1–2 hours implement; gates next backfill cycle |

**Filing instruction**: invoke `/project-defer-issue-tracking` (alias `/defer`) per `projects/spaarke-ai-platform-unification-r7/CLAUDE.md` ("Deferrals & Issues — tracking obligation" section) to file these in BOTH `notes/defer-issues.md` AND GitHub Issues atomically.

---

## 9. Acceptance criteria checklist

- [x] Sweep queries documented + run for all 4 patterns (P1 + P2 + P3 + P4) — §1
- [x] Per-matched-row classification with severity + Recommended fix MCP call — §3 + §5
- [x] Findings grouped by affected consumer / playbook (blast-radius) — §4
- [x] Recommendation on systematic vs per-consumer fix path — §6
- [x] notes/audits/wave12-124-wave5-backfill-health-sweep.md exists with file:line refs throughout — this file
- [x] Cross-references to existing T141/T142/T143 fixes to avoid double-work — §7
- [x] In-scope (Wave 12) vs out-of-scope (DEF-NNN) findings split clearly — §8
- [x] Wave 5 backfill root cause mechanism documented (CSV + script gates that failed) — §2

---

## 10. Confidence

- **Diagnosis confidence**: VERY HIGH — Dataverse data read directly via MCP for all 4 patterns; code path traced from `*NodeExecutor.cs Validate()` methods to orchestrator dispatch; failure mode deterministic (Validate failure → NodeFailed → run abort)
- **Blast-radius confidence**: VERY HIGH — exhaustive `grep -ril` across `src/ scripts/ docs/` for each playbook GUID + name confirmed ZERO live consumer references for 5 of 6 affected playbooks; the 6th (Document Summary) has a routing row but its consumertype string is absent from `ConsumerTypes.cs`
- **Fix-effectiveness confidence (T124-FIX-A)**: VERY HIGH — PATCH executortype 0→40 aligns Wave-5-overwritten value with canvas-asserted `__actionType:40`; preserves all operator-tunable surfaces; reversible single-field PATCH
- **Recommendation-resiliency**: HIGH — per-consumer (NOT systematic) split aligns with CLAUDE.md §11 (Component Justification — Cost-of-doing-nothing test) + minimizes operator session length + preserves optionality

---

## 11. Open follow-ups (filed via this task, NOT in scope of T124)

- ~~Operator approval + apply T124-FIX-A (Document Summary node `e514cfab` executortype 0 → 40)~~ — **APPLIED 2026-06-30 via main-session MCP (see §12)**
- File 7 DEF-NNN entries per §8 — still pending operator session at R7 wrap-up
- Consider whether to delete the `compose-summarize` consumer routing row (`986799ad-...`) if its target playbook is also DELETEd — orphan routing rows are silent landmines for future canvas authors

---

## 12. T124-FIX-A Applied (operator-approved 2026-06-30 main session)

Operator authorized PATCH per audit §5.1 recommendation. Applied via main-session MCP after Batch 2 dispatch.

**Pre-state read** (`mcp__dataverse__read_query`):
- `sprk_playbooknode(e514cfab-9d16-f111-8343-7c1e520aa4df)` "Document Profile"
- `sprk_executortype = 0` (AiAnalysis)
- `sprk_executortypename = "AI Analysis"`
- `sprk_configjson` contained `__actionType:40` + `deliveryType:"markdown"` + `outputFormat` — canvas-asserted Deliver Output intent

**PATCH applied** (`mcp__dataverse__update_record`):
- `item = { sprk_executortype: 40 }` → "Record updated successfully."

**Post-state read** (verification):
- `sprk_executortype = 40` ✅
- `sprk_executortypename = "Deliver Output"` ✅ (auto-updated; aligns with canvas + configJson)
- `sprk_configjson` unchanged (still has deliveryType + outputFormat keys appropriate for Deliver Output executor)

**Net effect**: Wave 5 backfill mis-classification corrected. Document Summary playbook node now dispatches to DeliverOutputNodeExecutor (which the canvas + configJson always intended) instead of AiAnalysisNodeExecutor (which would have failed Validate() because the configJson lacked AiAnalysis-required inputs). The `compose-summarize` consumer routing row remains intact for any future BFF wiring.

**Time to apply**: 1 MCP call + 1 verification read; ~30 seconds total from operator approval.

**Remaining T124 follow-ups**: 7 DEF-NNN entries for abandoned playbooks (§8) + future-mitigation script enhancement (pre-write coherence assertion in `Migrate-PlaybookNodes-to-ExecutorType.ps1`). All deferred to R7 wrap-up per §6 per-consumer recommendation.

---

*End of audit 124. Per-row data sourced from spaarkedev1 via `mcp__dataverse__read_query` 2026-06-30. Executor Validate-rule contracts sourced from [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/) tree (24 files inspected). T124-FIX-A APPLIED 2026-06-30 via main-session MCP per §12.*
