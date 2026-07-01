# Deploy-Playbook.ps1 R7 changes — task 055 (FR-20)

> **Date**: 2026-06-29
> **Task**: 055-update-deploy-playbook-write-executortype.poml
> **Wave**: 5
> **Status**: applied

---

## Pre-change audit (grep against `scripts/Deploy-Playbook.ps1` HEAD)

| Line | Construct | Disposition |
|---|---|---|
| 258 | `$NodeTypeMap = @{ 'AIAnalysis' = 100000000; 'Output' = 100000001; 'Control' = 100000002; 'Workflow' = 100000003; 'DeliverComposite' = 100000004 }` | **DELETE** — sprk_nodetype column was dropped pre-R7 (Wave 1 / R6 cleanup). The hashtable + writes are dead. |
| 321 | comment-only mention of `__actionType` injection workaround | DELETE comment, no code injection to remove (already cleaned in earlier R6 work — only the comment is residual). |
| 333 | `if ($lintNode.nodeType -eq 'DeliverComposite') { continue }` inside actionCode-lint loop | KEEP — DeliverComposite still needs the exemption from actionCode lint. The nodeType STRING is fine as friendly-label input. |
| 768-769 | `$nodeType = ...; $nodeTypeValue = $NodeTypeMap[$nodeType]` | **REPLACE** with executorType resolution sourced from `$node.executorType` (preferred) or legacy `$node.nodeType` → `$LegacyNodeTypeToExecutorType[...]` mapping. |
| 821 | `sprk_nodetype = $nodeTypeValue` in POST body | **REPLACE** with `sprk_executortype = $executorTypeValue`. |
| 985-993 | `$nodeType = ...; $canvasType = switch ($nodeType) { 'AIAnalysis' { 'aiAnalysis' }; 'Output' { 'output' }; 'Control' { 'control' }; default { 'aiAnalysis' } }` | KEEP — drives `canvasLayoutJson`, NOT dispatch. The friendly-label switch is correct semantics. Document the intent inline. |

## After-change post-conditions

1. Script writes `sprk_executortype = <int>` on every node-create POST (one of the 33 known enum values).
2. Pre-deploy LINT pass: `Test-PlaybookNodesAgainstExecutorTypeChoiceSet` walks every node; rejects with exit 1 if any node's resolved executorType is not in the 33-element allow-list (named offending node + value).
3. Backward-compat input: legacy playbook JSON files using only `nodeType` string still deploy via `$LegacyNodeTypeToExecutorType` mapping (12 friendly labels covering existing playbooks).
4. NO `__actionType` field is written into `sprk_configjson` from this script (confirmed via post-edit grep — zero hits).
5. NO `sprk_nodetype` field is written into the node POST body (column was dropped pre-R7; the legacy write was a dead-write that 400'd anyway).
6. Dry-run output now includes lint result + planned `sprk_executortype = N` per node.

## Source-of-truth for the 33 enum values

- **C# enum**: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` lines 128-309 (post-Wave 2 task 022 rename from `ActionType` → `ExecutorType`).
- **Dataverse global Choice**: `sprk_playbookexecutortype` (the column the script now writes).
- **Inlined as a PowerShell hashtable** at the top of `Deploy-Playbook.ps1` (chosen per POML default — "inline constant array of 33 values + name mapping; flag as DEF-{NNN} if codegen approach is preferred later"). When the C# enum grows past 33 values, this hashtable MUST be updated in lockstep. **Tracked as DEF-NNN** (file via `/devops-idea-create` if/when a codegen step is preferred over manual sync).

## Legacy `nodeType` (string) → `ExecutorType` (int) backward-compat map

Existing playbook JSON files (R3/R4-era) use these friendly labels. Mapped on input as a convenience; new playbooks SHOULD set `executorType` directly.

| Legacy `nodeType` (string) | `ExecutorType` (int) | Notes |
|---|---|---|
| `AIAnalysis` | 0 (AiAnalysis) | most common in seeded playbooks |
| `AiCompletion` | 1 | Wave 1 addition |
| `AiEmbedding` | 2 | |
| `Output` | 40 (DeliverOutput) | legacy single-section delivery |
| `DeliverComposite` | 42 | ADR-037 multi-section delivery |
| `DeliverToIndex` | 41 | |
| `Control` | 30 (Condition) | conservative default — R4 control nodes split into Condition/Start/LoadKnowledge/ReturnResponse; if legacy JSON has `nodeType: Control` AND a more specific `executorType`, the explicit value wins |
| `Start` | 33 | |
| `LoadKnowledge` | 142 | R4 daily-update-service control-flow executor |
| `ReturnResponse` | 143 | R4 daily-update-service control-flow executor |
| `Workflow` | 20 (CreateTask) | conservative default for legacy "Workflow" label |
| `CreateNotification` | 50 | notification playbooks |
| `EntityNameValidator` | 141 | R4 hallucination scrubber |

Legacy nodes without an explicit `executorType` AND no entry in this map → LINT REJECT with offending node name + missing-mapping error. Fix by adding `executorType: <int>` to the node definition.

## Files changed

- `scripts/Deploy-Playbook.ps1` — see §"Pre-change audit" disposition column.

## Verification

- `grep -n '__actionType' scripts/Deploy-Playbook.ps1` → 0 hits ✅
- `grep -n 'sprk_nodetype' scripts/Deploy-Playbook.ps1` → 0 hits ✅
- `grep -n '\$NodeTypeMap' scripts/Deploy-Playbook.ps1` → 0 hits ✅
- `pwsh -NoProfile -File scripts/Deploy-Playbook.ps1 -DryRun -DefinitionFile <fixture>` → lint pass + `sprk_executortype = N` lines per node ✅

## Carry-forward for task 056

Task 056 (sanity redeploy of 3 representative playbooks) MUST verify:
- Daily Briefing 5 playbooks (R4 — uses friendly `nodeType` labels) redeploys via legacy-mapping path.
- Insights pipeline ~5 playbooks (R6) redeploys.
- chat-summarize playbook redeploys.
- All POST bodies write `sprk_executortype`; spot-check 2 nodes via Dataverse query post-deploy.

