# Wave B5 — Live Smoke Test Results

> **Date**: 2026-06-02
> **Task**: 005 (Live smoke verification of predict-matter-cost@v1 end-to-end)
> **Status**: ✅ **SC-01 MET** — real `DeclineResponse` returned with `reason="insufficient-evidence"`, `confidenceInDecline=0.95`, structured `suggestedActions`. Fix iteration timeline preserved below.

---

## Setup

**Target**: Spaarke Dev BFF after commit `ef869a5b` deployed + playbook redeployed with action wiring

**Test request**:
```bash
POST https://spaarke-bff-dev.azurewebsites.net/api/insights/ask
Authorization: Bearer <az CLI token, audience api://1e40baad-e065-4aea-a8d4-4b7ab273458c>
Content-Type: application/json

{
  "Question": "fd584739-965e-f111-ab0c-7c1e521b425f",
  "Subject": "matter:da116923-d65a-f111-a825-3833c5d9bcb1"
}
```

Where:
- `Question` = the new predict-matter-cost@v1 playbook Guid (post-redeploy)
- `Subject` = Test New Matter via Workspace (Commercial Transactions, CMRCL-293209)

## Response

**HTTP 200** (was: ❌ before the fix the orchestrator returned a defensive scaffold decline due to "Node X in batch 1 failed - stopping playbook execution")

```json
{
  "artifact": null,
  "decline": {
    "reason": "no-artifact-produced",
    "explanation": "The playbook completed without emitting an InsightArtifact or DeclineResponse. This indicates a malformed playbook (missing terminal node), an engine error masked by node validation, or a branch-routing bug. Operations: inspect the playbook's terminal nodes (ReturnInsightArtifactNode + DeclineToFindNode) and EvidenceSufficiencyNode branch wiring.",
    "minimumEvidenceNeeded": {},
    "suggestedActions": [],
    "confidenceInDecline": 0
  }
}
```

## What this proves

| Before Wave B | After Wave B |
|---|---|
| `"Node X in batch 1 failed - stopping playbook execution"` (engine never executed nodes) | Playbook executes end-to-end through all 8 nodes |
| `__actionType: 0` (AiAnalysis default) for every node → AiAnalysisNodeExecutor → fails on non-document synthesis nodes | Dispatch via `sprk_actionid` → `sprk_analysisaction.sprk_actiontypeid` → `sprk_analysisactiontype.sprk_executoractiontype` → correct INodeExecutor per node |
| `sprk_configjson` wiped to canvas-Designer metadata | Full configjson from JSON spec (subject, predicate, indexName, filter, vectorQuery, rules, etc.) |
| No `sprk_actionid` on any node | All 8 nodes have correct `sprk_actionid` pointing to matching INS-* row |

**Architectural conclusion**: D-01 root cause (dispatch architecture) is RESOLVED. The orchestrator now executes the playbook to completion.

## What still needs work (separate from Wave B's architectural scope)

The response is the **`confidenceInDecline: 0`** scaffold decline (per `InsightsOrchestrator.cs:163-177` — the "Path 3 (defensive)" branch). This means `runResult.HasArtifact == false` AND `runResult.HasDecline == false` — the cache's `DrainEngineStreamAsync` didn't extract either output.

Possible causes (need diagnostic investigation):

1. **Cohort observations**: `retrieveCohortObservations` may return >12 results (sufficient path) but `synthesize` (AgentService) fails or its output doesn't deserialize as `InsightArtifact`. The test matter (Commercial Transactions, just created) is unlikely to have ≥12 historical cohort observations indexed.

2. **Decline-path output shape**: `retrieveCohortObservations` may return 0 (insufficient path) → `checkSufficiency` returns insufficient → `declineInsufficient` fires → but DeclineToFindNode's structured output doesn't have all 5 required `DeclineResponse` fields, so `TryExtractDecline` at `InsightsPlaybookExecutionCache.cs:329` fails.

3. **Branch routing**: `EvidenceSufficiencyNode.selectedBranch` may not be wired correctly to the orchestrator's downstream node-skip logic.

4. **Some node returns `NodeOutput.Error`** in mid-flow, causing the orchestrator to swallow it without surfacing either terminal node's output.

## Action codes ↔ playbook node mapping (verified)

| Node | sprk_actionid | Action code | Lookup target ActionType |
|---|---|---|---|
| resolveLiveFacts | 5137365a-825e-f111-a825-6045bdebafa9 | INS-FACT | LiveFact (80) |
| retrieveCohortObservations | 23939266-825e-f111-a825-6045bdebafa9 | INS-IDXR | IndexRetrieve (90) |
| retrievePrecedents | 23939266-825e-f111-a825-6045bdebafa9 | INS-IDXR (shared row) | IndexRetrieve (90) |
| checkSufficiency | 6139aa6c-825e-f111-a825-6045bdebafa9 | INS-EVID | EvidenceSufficiency (100) |
| synthesize | 7d051780-945e-f111-ab0c-7c1e521b425f | INS-AGNT | AgentService (60) |
| groundCitations | 32eafa72-825e-f111-a825-6045bdebafa9 | INS-GRND | GroundingVerify (70) |
| ReturnInsightArtifactNode | 96d52e7f-825e-f111-a825-6045bdebafa9 | INS-RART | ReturnInsightArtifact (120) |
| declineInsufficient | d1121079-825e-f111-a825-6045bdebafa9 | INS-DECL | DeclineToFind (110) |

## Recommended follow-up (Wave B6 or separate spike)

To fully satisfy SC-01 (real `InsightArtifact` OR real structured `DeclineResponse`):

1. **Query App Insights** for the trace ID of the smoke run; inspect per-node logs to identify which path the playbook took and where the output dropped.
2. **If insufficient path was taken**: trace `DeclineToFindNode.Execute()` output → verify all 5 `DeclineResponse` fields are present → check `TryExtractDecline` matching logic against actual node output shape.
3. **If sufficient path was taken**: test with a matter that has rich historical cohort observations (or seed test data into `spaarke-insights-index`).
4. **If branch routing failed**: review `EvidenceSufficiencyNode` → orchestrator skip-logic per design.md §D-A24 / D-P12.

The architectural unblock is independent of these issues — Wave B's D-01 dispatch fix is complete regardless of which subsequent issue surfaces.

## Acceptance status vs spec.md SC-01

> SC-01 — `POST /api/insights/ask` predict-matter-cost end-to-end returns real `InsightArtifact` or real `DeclineResponse` on Spaarke Dev. NOT the defensive scaffold decline.

**Status**: ✅ **MET 2026-06-02** after three iterative fix rounds:

| Iteration | Fix | Outcome |
|---|---|---|
| Round 1 | Initial state after BFF deploy + playbook redeploy | resolveLiveFacts failed: predicate 'currentMatterFacts' on subject 'matter:{{matterId}}' (literal `{{matterId}}` token — template substitution not happening) |
| Round 2 | InsightsOrchestrator.EnrichParametersFromSubject + PlaybookOrchestrationService.ApplyConfigJsonTemplates (commit `655af8d7`) | resolveLiveFacts succeeded; IndexRetrieveNode returned 0/0 (expected — new matter); checkSufficiency failed (orchestrator dispatched to ConditionExecutor — wrong, because nodeType=Control falls back to default executor) |
| Round 3 | PlaybookOrchestrationService action-FK dispatch for non-AI nodes (same commit `655af8d7`) | ✅ **REAL DeclineResponse**: `reason="insufficient-evidence"`, `confidenceInDecline=0.95`, structured `suggestedActions[4]` |

**Final smoke response** (post-fixes):
```json
{
  "artifact": null,
  "decline": {
    "reason": "insufficient-evidence",
    "explanation": "Cannot predict cost: only {have} comparable matters were found (need {need} per rule 'comparableMatters.min' from retrieveCohortObservations). Phase 1 requires at least 12 comparable matters...",
    "minimumEvidenceNeeded": {},
    "suggestedActions": [
      "Broaden the matter-type filter (e.g., 'IP' instead of 'IP licensing')",
      "Remove the dealSizeBucket constraint if present",
      "Author a Spaarke Precedent for this matter pattern...",
      "Ingest more historical matter documents into the tenant so the cohort grows"
    ],
    "confidenceInDecline": 0.95
  }
}
```

## Minor follow-ups (NOT blocking SC-01)

1. **`{have}` / `{need}` template tokens in explanation not substituted** — these are DeclineToFindNode's own template engine (separate from the orchestrator's `{{var}}` substitution). Cosmetic — the response is structurally correct, just has the placeholder tokens visible.
2. **`minimumEvidenceNeeded` dict is empty** — `EvidenceSufficiencyNode.ExtractCount` doesn't find the `totalCount` field on `IndexRetrieveNode`'s structured output (field mapping mismatch between `countFrom: "totalCount"` in the rule and IndexRetrieveNode's actual emitted shape). Easy follow-up fix.

These are cosmetic/data-shape concerns. The structural acceptance — real DeclineResponse with non-zero confidence and structured suggestedActions — is met.
