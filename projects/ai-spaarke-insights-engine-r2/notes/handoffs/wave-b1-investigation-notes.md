# Wave B1 — Investigation Handoff (task 001 complete)

> **Date**: 2026-06-02
> **Task**: 001 (re-scoped: investigate playbook-architecture + scope-model-index)
> **Outcome**: D-01 Q1+Q2+Q3 resolved; Wave B re-scope confirmed viable

---

## Three-level node type system (definitive reference per `docs/architecture/playbook-architecture.md` line 50-92)

| Level | Name | Where Stored | Purpose |
|---|---|---|---|
| Canvas Type | `PlaybookNodeType` | React Flow `node.data.type` | React component selection (Designer only) |
| Dataverse NodeType | `sprk_nodetype` | `sprk_playbooknode` OptionSet | Coarse scope resolution (AIAnalysis / Output / Control / Workflow) |
| **ActionType** | `__actionType` in ConfigJson | `sprk_playbooknode.sprk_configjson` | **Fine-grained executor dispatch** |

> **Key rule** (line 92): "NodeType determines scope resolution strategy. ActionType determines which INodeExecutor runs."

## ActionType dispatch — the two paths (per `PlaybookOrchestrationService.cs:920+`)

```
if (node has sprk_actionid FK):
    action = resolved sprk_analysisaction row;
    actionType = action.ActionType;  // from sprk_analysisaction.sprk_actiontype
else:
    actionType = ExtractActionTypeFromConfig(node.ConfigJson)  // from __actionType in configjson
                 ?? defaultByNodeType;

executor = _executorRegistry.GetExecutor(actionType);
```

**Wave B implication**: BOTH paths must yield the correct ActionType integer per node. We will set BOTH:
- `sprk_playbooknode.sprk_actionid` → FK to one of the 6 new sprk_analysisaction rows
- That action row's `sprk_actiontype` = matching enum integer (LiveFact=80, etc.)
- `sprk_playbooknode.sprk_configjson.__actionType` = matching enum integer (belt + suspenders)

## Insights ActionType enum (canonical from `INodeExecutor.cs:78-188`)

| Executor | Enum value | Integer |
|---|---|---|
| AgentService (existing — synthesis) | `AgentService` | 60 |
| GroundingVerifyNode | `GroundingVerify` | **70** |
| LiveFactNode | `LiveFact` | **80** |
| IndexRetrieveNode | `IndexRetrieve` | **90** |
| EvidenceSufficiencyNode | `EvidenceSufficiency` | **100** |
| DeclineToFindNode | `DeclineToFind` | **110** |
| ReturnInsightArtifactNode | `ReturnInsightArtifact` | **120** |

## Canvas Designer mapping table (`docs/architecture/playbook-architecture.md` line 56-68)

Designer's `playbookNodeSync.ts` maps ONLY 9 canvas types: start, aiAnalysis, aiCompletion, condition, deliverOutput, deliverToIndex, updateRecord, createTask, sendEmail.

**ALL 6 Insights ActionTypes are MISSING from this mapping** → opening an Insights playbook in the Designer causes:
1. Canvas reads `__actionType: 80` (LiveFact) → no canvas-type match → defaults to `aiAnalysis`
2. 30-second debounce auto-save (line 104) writes degraded `sprk_configjson` back: `{"__canvasNodeId":"...","__actionType":0}`
3. All custom config (subject, predicate, indexName, filter, vectorQuery, etc.) lost

**Confirmed write boundary** (`playbookNodeSync.ts:13-17`): "The BFF API only reads these records at execution time — it never creates or updates them." → Designer is sole runtime writer of `sprk_playbooknode` records.

## Wave B operational rule (NEW — to be documented in task 006)

**Do NOT open Insights playbooks in the Make Playbook Designer UI.** Doing so will wipe the configjson and break orchestrator dispatch.

This rule applies through Wave B and persists into Wave C. Wave C may consider extending the canvas mapping to handle Insights types as a long-term fix.

## Catalog state

`.claude/catalogs/scope-model-index.json` contains no Insights-related entries (`grep -i insight|LiveFact|IndexRetrieve` returned no matches). The catalog is Designer-driven; Insights actions are JSON-spec + Dataverse-managed only. **No catalog updates needed for Wave B.**

## Action codes (for task 002 to author)

| Action code | Enum integer | INodeExecutor | Notes |
|---|---|---|---|
| `INS-LIVE-FACT` | 80 | LiveFactNode | Deterministic; no LLM; emits FactArtifact |
| `INS-INDEX-RETRIEVE` | 90 | IndexRetrieveNode | Hybrid search; emits ranked observation/precedent docs |
| `INS-EVIDENCE-SUFFICIENCY` | 100 | EvidenceSufficiencyNode | Rule eval; emits sufficient/insufficient + gap |
| `INS-GROUNDING-VERIFY` | 70 | GroundingVerifyNode | Mechanical citation check; emits verified evidence |
| `INS-DECLINE-TO-FIND` | 110 | DeclineToFindNode | Structured DeclineResponse output |
| `INS-RETURN-ARTIFACT` | 120 | ReturnInsightArtifactNode | Serializes upstream into InsightArtifact envelope |

## Ready for task 002

Task 002 invokes `/jps-action-create` skill 6 times to author the JPS prompts, then creates the 6 sprk_analysisaction rows via `mcp__dataverse__create_record`. The successful create call confirms `sprk_actiontype` is writeable (Q1 empirical confirmation deferred to this point per D-01 resolution).
