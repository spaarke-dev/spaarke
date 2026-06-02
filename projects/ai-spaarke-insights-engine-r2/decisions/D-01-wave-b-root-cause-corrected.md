# D-01 — Wave B root cause corrected via empirical investigation

> **Status**: APPROVED — path-b re-scope (Option A) per owner direction 2026-06-02
> **Filed**: 2026-06-02
> **Approved**: 2026-06-02
> **Trigger**: bff-extensions.md §F.3 (Empirical-Reproduction-FIRST Protocol) — required before applying a ledger-style recommended fix
> **Source ledger entry**: design.md §0 framing "predict-matter-cost playbook is deployed but doesn't fire end-to-end live because the new Insights node executors were never bound to sprk_analysisaction rows"
> **Investigation by**: task-execute on task 001 (Wave B1), 2026-06-02
> **Outcome**: original framing is partially correct; root cause is broader; path-b re-scope adopted

## Owner direction (2026-06-02)

> "yes adopt path b rescope BUT if this implicates using JPS for the writing ensure that we are following our standard i.e. see skill /jps-action-create and /jps-playbook-design"

**Binding constraints added**:
1. Every JPS prompt authored as part of Wave B MUST go through the **`jps-action-create`** skill (Tier 1 component), which enforces the canonical JPS schema, `$ref` / `$choices` / `structuredOutput` patterns, and loads `docs/guides/JPS-AUTHORING-GUIDE.md` + reference examples at `.claude/skills/jps-action-create/examples/`.
2. The playbook-level fix (delete nodes + recreate with correct config + Designer-clobber prevention) MUST consult **`jps-playbook-design`** skill conventions, especially the `playbook-architecture.md` data model + ActionType dispatch + canvas design documentation it loads.
3. Wave B task 001 (Q1+Q2 investigation) is REPLACED by: read `docs/architecture/playbook-architecture.md` + `.claude/catalogs/scope-model-index.json` (loaded by jps-playbook-design) — likely answers the open questions authoritatively without further empirical investigation.
4. Wave B retains 8 tasks; estimated effort revised from "~1–1.5 days" to "~1–2 days" to account for skill orchestration overhead.

This clarification means **no inline JPS authoring** by task-execute — every prompt-bearing change goes through the dedicated skill, ensuring consistency with the broader Spaarke JPS standard and avoiding drift.

---

---

## 1. What the ledger / design.md said

> "the `predict-matter-cost` synthesis playbook is deployed but doesn't fire end-to-end live because the new Insights node executors were never bound to `sprk_analysisaction` rows (the JPS dispatch contract). Engine logs 'Node X in batch 1 failed - stopping playbook execution' and the orchestrator returns a defensive scaffold decline. Half-day fix."

**Implied fix**: create 6 missing `sprk_analysisaction` rows (one per Insights node executor type), update playbook JSON to reference them, re-deploy.

## 2. What empirical investigation revealed

### 2.1 Dispatch mechanism (confirmed)

Tracing `PlaybookOrchestrationService.cs` lines 920–944:

```csharp
if (node has Action FK):
    action = resolved AnalysisAction;
    actionType = action.ActionType;  // ← from sprk_analysisaction
else:
    // Structural nodes (Output, Control, Workflow)
    actionType = ExtractActionTypeFromConfig(node.ConfigJson)
                 ?? defaultByNodeType;

executor = _executorRegistry.GetExecutor(actionType);
```

`actionType` (the integer used by `NodeExecutorRegistry.GetExecutor(ActionType)`) flows from:

1. **`sprk_playbooknode.sprk_actionid`** → FK to a `sprk_analysisaction` row
2. → that row's **`sprk_actiontype`** (int) field → `entity.ActionTypeValue` in C#
3. → cast to `Sprk.Bff.Api.Services.Ai.Nodes.ActionType` enum
4. → registry lookup to find the matching `INodeExecutor`

`NodeExecutorRegistry` (line 22+) is built from DI-injected `IEnumerable<INodeExecutor>`. Each `INodeExecutor` declares `SupportedActionTypes` (e.g., `LiveFactNode.SupportedActionTypes = [ActionType.LiveFact]`).

### 2.2 Insights ActionType enum values (confirmed)

From `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` lines 78-188:

| Executor | ActionType enum | Integer value |
|---|---|---|
| LiveFactNode | `LiveFact` | **80** |
| IndexRetrieveNode | `IndexRetrieve` | **90** |
| EvidenceSufficiencyNode | `EvidenceSufficiency` | **100** |
| GroundingVerifyNode | `GroundingVerify` | **70** |
| DeclineToFindNode | `DeclineToFind` | **110** |
| ReturnInsightArtifactNode | `ReturnInsightArtifact` | **120** |
| AgentServiceNodeExecutor (existing) | `AgentService` | 60 |

### 2.3 JPS prompt structure (confirmed)

Gold-standard model from existing `sprk_analysisaction` row `ACT-021` ("Classify Document"):

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": { "role": "...", "task": "...", "constraints": [...], "context": "..." },
  "input": { "document": {...}, "parameters": {...} },
  "output": { "fields": [...], "structuredOutput": true },
  "scopes": { "$skills": [...] },
  "examples": [...],
  "metadata": { "author": "...", "createdAt": "...", "description": "...", "tags": [...] }
}
```

### 2.4 Critical issues found in deployed `predict-matter-cost@v1` (NEW — beyond original framing)

Querying the 8 `sprk_playbooknode` rows via MCP:

**Issue 1 — `sprk_configjson` contents WIPED**:

| Node | Deployed `sprk_configjson` |
|---|---|
| resolveLiveFacts | `{"__canvasNodeId":"0069512d-...","__actionType":0}` |
| retrieveCohortObservations | `{"__canvasNodeId":"0569512d-...","__actionType":0}` |
| retrievePrecedents, checkSufficiency, synthesize, groundCitations, declineInsufficient, ReturnInsightArtifactNode | all `__actionType:0` — same pattern |

The playbook JSON spec contains per-node `configJson` blocks with `subject`, `predicate`, `indexName`, `filter`, `vectorQuery`, `rules`, `prompt`, `from`, `artifactKind`, etc. **NONE of this made it into Dataverse.** The deployed configjson contains only canvas-Designer metadata (`__canvasNodeId` GUIDs + `__actionType: 0`).

**Likely cause**: someone (a human, or a process) opened the playbook in the Make Playbook Designer UI after the initial deploy. The Designer rewrote `sprk_configjson` with its own minimal `{__canvasNodeId, __actionType}` shape, losing the deploy-time config.

**Issue 2 — `sprk_actionid` FK not set on any node**:

The 8 deployed nodes have no `sprk_actionid` lookup populated. Looking at `Deploy-Playbook.ps1` line 673:
```powershell
$actionCode = $node.actionCode
```

The script reads `actionCode` (string) from each node in the JSON spec, looks it up in a pre-resolved action dictionary, and sets `sprk_actionid@odata.bind`. But the playbook JSON nodes have `"actionType": 80` (integer for the executor type), not `"actionCode": "..."` (string FK reference). So no `actionCode` resolution happens; no `sprk_actionid` is set.

### 2.5 The full causal chain

1. `predict-matter-cost.playbook.json` spec uses `actionType: 80` (integer) to identify the executor for each node
2. `Deploy-Playbook.ps1` reads `actionCode` (string), not `actionType` (integer) — these are different fields. Result: no `sprk_actionid` FK set, no executor lookup possible
3. `Deploy-Playbook.ps1` correctly writes `sprk_configjson` from the JSON spec at deploy time — but this gets overwritten by the canvas Designer when the playbook is opened in Make UI later
4. At runtime, `PlaybookOrchestrationService.ExecuteAsync` per node:
   - Sees no `sprk_actionid` → goes to "structural node" path (line 938)
   - Calls `ExtractActionTypeFromConfig(node.ConfigJson)` → reads `__actionType` from the canvas metadata → gets `0` (AiAnalysis)
   - Calls `executorRegistry.GetExecutor(ActionType.AiAnalysis)` → returns the AiAnalysisNodeExecutor
   - AiAnalysisNodeExecutor needs document context (NodeExecutionContext.Document with extracted text) — synthesis nodes don't have one → validation fails → "Node X in batch 1 failed"
5. Orchestrator stops execution; downstream nodes never fire; defensive scaffold decline returned

### 2.6 What the design.md framing missed

The original framing — "create 6 sprk_analysisaction rows" — fixes step 1 partially but doesn't address:
- The actionType-vs-actionCode mismatch in the JSON spec + script (needs both)
- The canvas Designer clobbering configjson (means we can't just "patch in place" — we need DELETE + recreate, AND need to never open in Designer again)
- The fact that `sprk_actiontype` field is not directly queryable via MCP TSQL (may be option-set with special exposure rules; needs verification)

## 3. Open questions

| # | Question | Why it matters |
|---|---|---|
| Q1 | Is `sprk_actiontype` (int) actually a writeable field on `sprk_analysisaction`? MCP TSQL says no; C# code expects yes. | Determines whether we can set it via MCP `create_record`, or need a different path |
| Q2 | What is the lookup target entity for `sprk_actiontypeid`? Does it have a numeric code field that derives the ActionType integer? | If yes, we may need to create rows in BOTH `sprk_analysisaction` AND the type lookup entity |
| Q3 | What caused the canvas Designer to overwrite the configjson? Was the playbook opened in Designer after the initial deploy? Can we prevent it? | Determines whether re-deploy will stick or get clobbered again |
| Q4 | Does the existing INS-OBS Insights Observation Mirror row need a similar fix (it has no sprk_actiontype either)? | Wave C may surface a similar pattern |

## 4. Proposed Wave B re-scope (path-b)

### Original Wave B (5 tasks ~½ day)
1. B1 — Create 6 sprk_analysisaction rows (action codes only — JPS prompts)
2. B2 — Add actionCode refs to playbook JSON
3. B3 — Add lint check to Deploy-Playbook.ps1
4. B4 — Re-deploy
5. B5 — Live smoke verify
6. B6 — Doc update

### Re-scoped Wave B (8 tasks ~1–1.5 days)

| # | Task | Original | New |
|---|---|---|---|
| 001 | Resolve open questions Q1+Q2 (actiontype field investigation) | — | NEW — must precede B1 |
| 002 | Create 6 sprk_analysisaction rows with **correct sprk_actiontype values** (LiveFact=80, IndexRetrieve=90, etc.) | partial | rewritten |
| 003 | Update playbook JSON: each node carries BOTH `actionCode` (string FK ref) AND `actionType` (int for executor dispatch). Verify both populate at deploy time. | partial | rewritten |
| 004 | Update Deploy-Playbook.ps1: lint requires actionCode per node; ALSO verify deployed `sprk_configjson` matches JSON spec post-deploy (catches Designer clobbering) | partial | enhanced |
| 005 | **Delete existing 8 nodes** from predict-matter-cost@v1 (do NOT delete the playbook itself); re-deploy nodes only with correct config + actionCode bindings | — | NEW |
| 006 | Document: do NOT open Insights playbooks in the Make Playbook Designer UI after deploy. Add canvas-config preservation as a Q4 open question for future engineering investigation. | — | NEW |
| 007 | Live smoke verification of predict-matter-cost end-to-end (was B5) | same | same |
| 008 | Update Phase 1 verification doc + close decision record D-01 (was B6) | same | enhanced |

Effort: ~1–1.5 days (was ½ day). Wave B remains far smaller than Wave A/C/D/E.

## 5. Decision required

This decision record proposes the path-b re-scope above. The user must decide:

| Option | Description | Implication |
|---|---|---|
| **A. Adopt path-b re-scope** | Update Wave B tasks 001-006 to match the corrected analysis. Add tasks 007 + 008. Proceed in order. | More disciplined; closes ledger gap; +0.5 day cost |
| **B. Adopt re-scope but defer Q3 investigation** | Same as A, but skip the Designer-clobbering question — accept that we delete + redeploy without preventing future clobber. Document the risk. | Slightly faster; risk of regression if Designer is reopened |
| **C. Stay with original Wave B framing** | Proceed with 001 as originally written (create 6 rows). Hit B4 deploy, discover the actionType-vs-actionCode mismatch AND the configjson-wipe issue empirically at that point. | Higher risk; F.3 protocol explicitly says do NOT do this |
| **D. Investigate Q1+Q2 first as a spike, then decide** | Spend 1–2 hours via Power Platform admin tools (Power Apps maker, XrmToolBox, or direct OData metadata calls) confirming the `sprk_actiontype` field exists and is writeable. Then re-present. | Most rigorous; modest delay |

**Recommendation**: A (path-b re-scope) — the empirical findings are robust enough to act on, the +0.5 day cost is cheap insurance against B4 failing live, and the F.3 protocol's intent is satisfied.

---

## Appendix A — Investigation method (per F.3)

1. Read production code path: `PlaybookOrchestrationService.cs` line 920+ → identified actionType source
2. Hand-traced: `sprk_playbooknode` → `sprk_actionid` → `sprk_analysisaction` → `ActionType` enum → `INodeExecutor`
3. Empirically reproduced via MCP queries:
   - `SELECT * FROM sprk_playbooknode WHERE sprk_playbookid = '63b80630-...'` → confirmed `sprk_configjson` wiped
   - `SELECT * FROM sprk_analysisaction WHERE sprk_actioncode = 'ACT-021'` → confirmed JPS prompt model
   - `SELECT sprk_actiontype FROM sprk_analysisaction WHERE ...` → field not present in MCP TSQL metadata (Q1 unresolved)
4. Cross-checked against r1 actionType integer assumption (failed) → confirmed actionType→actionCode mismatch
5. Documented in this record before applying any fix.

This is the path-b decision record F.3 calls for. F.3: "If actual root cause differs from ledger's hypothesis: file a path-b decision record … BEFORE applying the fix." ✅
