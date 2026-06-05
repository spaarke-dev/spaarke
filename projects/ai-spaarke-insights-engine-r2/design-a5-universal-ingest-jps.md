# Design A5 — Universal-Ingest JPS Refactor

> **Wave**: A (Foundations) · **Wave item**: A5 · **Task**: [014](tasks/014-universal-ingest-jps-refactor-design.poml)
> **Status**: Design — feeds Wave C1 (universal-ingest@v1 playbook authoring), C5 (parameterization), C3 (orchestrator retirement)
> **Owner**: Insights Engine r2 · **Author**: task-execute (014) · **Date**: 2026-06-02
> **Spec questions resolved**: Q-A5-1 (node count) · Spec assumption "8 nodes for sizing" confirmed below
> **Constraint sources**: design.md D-P15-01 (single engine), D-P15-02 (one canonical universal-ingest), spec.md Risk-1 (engine patches stay in engine — don't fork)

---

## 1. Summary

This design refactors the code-defined `IngestOrchestrator.cs` into a **JPS-defined `universal-ingest@v1` playbook** (data in Dataverse) executed by the existing `PlaybookExecutionEngine`. The refactor:

- Decomposes the current orchestrator's 10 logical steps into **6 JPS nodes** (coalesced, not 1:1 — rationale §3) plus an existing `Condition`-node-equivalent gate.
- Maps each node to an existing `sprk_analysisaction` ActionType, **reusing Wave B's 6 INS-* action rows** plus the existing `Sanitization` and `Layer2Extraction` action types already needed in r1.
- Defines a runtime parameterization schema (`tenantId`, `practiceAreaHint`, `costCapOverride`, `layer2Threshold`, plus `documentId` and `matterId`) to satisfy D-P15-02 (one canonical playbook, parameterized — not many variants).
- Models the Layer 2 gate via the **existing `EvidenceSufficiencyNode` "selectedBranch" + dependency-failure-skip mechanism** already proven working in `predict-matter-cost@v1` (D-01 closure 2026-06-02) — **no new conditional primitive required**.
- Surfaces **two narrow engine-gap candidates** (a hard one and a soft one); only the soft one needs a small patch in `PlaybookOrchestrationService`. Detailed in §7.

The deliverable downstream is Wave C1 (one canonical playbook JSON deployed via `Deploy-Playbook.ps1`), Wave C3 (delete `IngestOrchestrator.cs` + orphaned interfaces), Wave C4 (`IInsightsAi.RunIngestAsync` invokes playbook), Wave C5 (per-invocation parameter overrides).

---

## 2. Current code-defined sequence (`IngestOrchestrator.cs`)

`IngestOrchestrator.RunAsync` (`src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Ingest/IngestOrchestrator.cs`) is a single async method with the following logical steps:

| # | Step | Code surface | Side effects |
|---|---|---|---|
| 1 | **Fetch document** from `spaarke-files-index` | `IIngestDocumentSource.FetchAsync` | None (read). Null result → early return (non-indexable upload). |
| 2 | **Sanitize** content (D-50) | `IInsightsContentSanitizer.SanitizeAsync` | Sanitized text feeds L1+L2 prompts; original chunks feed grounding (unmodified). Empty sanitized → early return. |
| 3 | **Layer 1 classify** | `IOpenAiClient.GetStructuredCompletionRawAsync` (model `gpt-4o-mini`, `classification@v1` prompt + schema) | LLM call. Result: `Layer1ClassificationResult` (classification + confidence). |
| 4 | **Emit Layer 1 Observation** | `ILayer1ClassificationEmitter.EmitAsync` → `IObservationIndexUpserter.UpsertAsync` + `IObservationMirror.MirrorAsync` | Index write (fatal on fail) + Dataverse mirror (non-fatal). Always exactly 1 observation. |
| 5 | **Gate Layer 2** | `DocumentClassificationExtensions.IsOutcomeBearing` + `Confidence ≥ 0.7` | Pure decision. If gate fails → return with L1 only. |
| 6 | **Layer 2 extract** (gated) | `IOpenAiClient.GetStructuredCompletionRawAsync` (model `gpt-4o`, `outcome-extraction@v1` prompt + schema) | LLM call. Returns raw JSON. |
| 7 | **Validate Layer 2 response** | `OutcomeExtractionResponseValidator.Validate` | Pure. Invalid → log + return with L1 only (L2 dropped). |
| 8 | **Project Layer 2 → ExtractionResult + ground-verify quotes** | `IGroundingVerifier.VerifyAsync` over the candidate citations | Drops fields whose quotes don't ground-verify (Verdict ≠ `Verified` ∧ ≠ `VerifiedApproximate`). |
| 9 | **Emit Layer 2 per-field Observations** | `IObservationEmitter.EmitFromExtractionAsync` → upserter + mirror (per observation) | Index writes (fatal per-write) + Dataverse mirror (non-fatal per-write). 0–4 observations (one per surviving field). |
| 10 | **Return result** | `InsightsIngestResult(ObservationsEmitted, Layer1Classification, Layer2Triggered)` | Telemetry log `UniversalIngestCompleted`. |

**Note**: The POML's prompt summarizes this as "8 logical steps." Empirically the code splits into 10 distinct concerns (above). The POML lumps Steps 1+10 into bookend framing. Below, the design coalesces this 10-step shape into 6 JPS nodes.

---

## 3. Node count decision — **6 nodes, coalesced**

### 3.1 Options considered

| Option | Nodes | Pros | Cons |
|---|---|---|---|
| **A. 1:1** | 10 nodes (one per logical step) | Max observability per step; mirrors code exactly | Fetch is not really a node (it's an input precondition); validation is a sub-step of Layer 2; emission is a side-effect of L1/L2 nodes, not a separable node; over-decomposition makes the playbook noisy and slower (Dataverse round-trips per node start/end event). |
| **B. Coalesced — 6 nodes** (this design's choice) | 6 nodes | Aligns with the natural JPS node-types already in the registry (`Sanitization`, `Layer1Classification`, `Condition`/`EvidenceSufficiency`-equivalent gate, `Layer2Extraction`, `GroundingVerify`, `ObservationEmitter`+`IndexUpsert`+`Mirror` collapsed); reuses existing action types; clean stream events; one node = one action row. | Need a small "fanout-emit" semantic inside one node (acceptable — see §4 ObservationEmitter design). |
| **C. Very coarse — 3 nodes** | 3 nodes (Sanitize+L1+gate / L2+ground / emit-all) | Minimal Dataverse footprint | Loses per-step observability (which node failed?); makes the gate invisible at the node level (defeats the JPS purpose). |

**Decision**: **Option B — 6 nodes**. Rationale:

1. **JPS node-types map 1:1 to existing `INodeExecutor` implementations.** No new executor types needed for steps 1–9; we reuse the Wave B INS-* registrations (Sanitization is existing in r1's action registry; Layer 1/Layer 2 are standard `AiAnalysis` action types per the action catalog; GroundingVerify is INS-GRND from Wave B; the observation-emit fanout reuses the existing `ObservationEmitter` interface called from a single dedicated node).
2. **Observability stays high** — every LLM call + every gate decision + the grounding pass emit `NodeStarted`/`NodeCompleted` stream events. This is more granular than the current orchestrator's single `UniversalIngestCompleted` log line.
3. **Aligns with the `predict-matter-cost@v1` precedent** — 8 nodes there, 6 here. Same shape (sequential `dependsOn` graph with one EvidenceSufficiency-style gate branching to a "skip Layer 2" path).
4. **D-P15-02 satisfied** — one playbook, parameterized. The per-practice-area variation (Wave D2/D3) is achieved by injecting `parameters.practiceAreaHint` into Layer 1 + Layer 2 prompts at runtime, NOT by spawning variant playbooks.

### 3.2 The 6 nodes (overview)

```
[1 Sanitize] → [2 Layer1Classify] → [3 EvidenceSufficiency: gate]
                                          ↓ (sufficient)        ↓ (insufficient)
                                    [4 Layer2Extract] → [5 GroundingVerify] → [6 ObservationEmitter]
                                                                                 ↑
                                                              (insufficient path also routes here,
                                                               emitting only the L1 observation)
```

Detailed dependency graph + branching shape in §5.

---

## 4. Per-node design

For each node: **name** · **action code** (existing Wave B row or new) · **action type** · **inputs** · **outputs** · **side effects**.

### Node 1 — `sanitize`

- **Action code**: `INS-SANI` (new — Wave C1 must create this action row, OR reuse an existing `Sanitization` action type if already present in the action catalog; check during Wave C1)
- **Action type**: `Sanitization` (existing in `ActionType` enum if registered; otherwise add via `sprk_actiontypeid` lookup in Wave C1)
- **Executor**: existing `IInsightsContentSanitizer`-backed `INodeExecutor` (Wave C1 may need to wrap `IInsightsContentSanitizer` in a new `SanitizerNodeExecutor` — confirm during C1; this is a thin wrapper, not new business logic)
- **Inputs**:
  - From parameters: `parameters.documentId` (string GUID) · `parameters.tenantId` (string)
  - From engine: fetches document content via `IIngestDocumentSource` (the executor receives the document ID and resolves content internally — or, alternatively, fetch happens in a "pre-node" inside `IInsightsAi.RunIngestAsync` and sanitize receives raw text. **Design choice for Wave C1**: prefer the latter — the playbook receives `parameters.documentText` already-fetched, simplifying the executor. This pushes the fetch back into `IInsightsAi.RunIngestAsync` where it lives today.)
- **Outputs (`outputVariable: sanitization`)**:
  ```json
  {
    "sanitizedText": "string",
    "originalLength": "int",
    "documentRef": "string",
    "chunks": [ { /* raw chunks for grounding */ } ]
  }
  ```
- **Side effects**: None. Pure transform.
- **Skip condition**: if `sanitizedText` is empty → emit `output.success=false` with `errorCode=SANITIZE_EMPTY`. Downstream `Layer1Classify` skips per the orchestrator's dependency-failure-skip rule (proven in `PlaybookOrchestrationService` line 838–858).

### Node 2 — `layer1Classify`

- **Action code**: `INS-L1C` (new — created during Wave C1 along with its `sprk_systemprompt` JSON via `/jps-action-create`)
- **Action type**: `AiAnalysis` (type 0) — uses existing `AiAnalysisNodeExecutor` with prompt = `classification@v1` per Wave C2 migration
- **Inputs**:
  - From `sanitization.sanitizedText` (template: `{{sanitization.output.sanitizedText}}`)
  - From parameters: `parameters.practiceAreaHint` (optional — injected into prompt as JPS `parameters.practiceAreaContext` for Wave D2 routing)
- **Outputs (`outputVariable: layer1`)**:
  ```json
  {
    "classification": "string (DocumentClassification enum)",
    "confidence": "double [0.0, 1.0]",
    "evidence": { /* quote spans for L1 reasoning */ }
  }
  ```
- **Side effects**:
  - LLM call (model `gpt-4o-mini` per current code; configurable via action row's `sprk_systemprompt.parameters.model` in Wave A4 design)
  - Emits 1 Layer 1 Classification Observation **inside the executor** (via existing `ILayer1ClassificationEmitter.EmitAsync`) → upsert + mirror. This is the L1 emission step (current code Step 4) — kept inside this node so the L1 obs is guaranteed-emitted even if downstream nodes fail.
  - **Note on dual responsibility**: this node both classifies AND emits the L1 obs. Alternative is to split into `layer1Classify` (LLM) + `emitL1Observation` (write). **Decision**: keep them fused. Rationale: L1 emission is a side effect of L1 classification (same observation kind, same scope, same producer identity). Splitting would force the executor to share `Layer1ClassificationResult` shape across two nodes, which adds template plumbing for no observability gain. The orchestrator log already distinguishes "classified" vs "emitted" sub-steps via existing event IDs.

### Node 3 — `checkLayer2Gate`

- **Action code**: `INS-EVID` **(reused from Wave B — already in Dataverse)**
- **Action type**: `EvidenceSufficiency` (type 100) — uses existing `EvidenceSufficiencyNode` executor
- **Inputs (via ConfigJson `rules[]`)**:
  ```json
  {
    "rules": [
      {
        "name": "outcomeBearingClassification",
        "from": "layer1",
        "$comment": "Pass when layer1.classification is in the outcome-bearing set. Phase 1 used DocumentClassificationExtensions.IsOutcomeBearing — Phase 1.5 expresses the predicate declaratively per practice area.",
        "predicate": "in",
        "value": "{{parameters.outcomeBearingClassifications}}",
        "$comment-value": "Default = ['Order','Settlement','Verdict','Judgment'] (litigation-default); per-practice-area overrides come from parameters.outcomeBearingClassifications array, sourced from sprk_practicearea_ref.sprk_outcomebearingjson (Wave D2)."
      },
      {
        "name": "confidenceThreshold",
        "from": "layer1",
        "countFrom": "confidence",
        "$comment-countFrom": "EvidenceSufficiencyNode's countFrom path supports nested-property reading (verified in EvidenceSufficiencyNode.cs Validate, line 88). Confidence is read as a double-valued countFrom.",
        "minCount": "{{parameters.layer2Threshold}}",
        "$comment-minCount": "Default = 0.7 (Phase 1 D-59). Per-invocation override via parameters.layer2Threshold."
      }
    ],
    "sufficientBranch": "layer2Extract",
    "insufficientBranch": "emitObservations"
  }
  ```
- **Outputs (`outputVariable: gate`)**: `EvidenceSufficiencyNode` standard output — `verdict: "sufficient" | "insufficient"`, `selectedBranch`, `gaps[]`.
- **Side effects**: None. Pure deterministic rule eval.
- **NOTE on `EvidenceSufficiencyNode` extension**: the existing executor (`EvidenceSufficiencyNode.cs`) supports two rule shapes — `minCount` (count-based) and `requireNonEmpty` (presence-based). It does **not** currently support a `predicate: "in"` (membership) shape for the `outcomeBearing` rule. **This is engine gap candidate #1 — see §7.** Design decision: either (a) extend `EvidenceSufficiencyNode` with a `predicate: "in"` rule type (small patch, ~15 LOC), OR (b) precompute `outcomeBearing` as a boolean field in Node 2's output and use a `requireNonEmpty`-style check on a derived `outcomeBearingFlag`. **Recommendation**: option (a) — extending `EvidenceSufficiencyNode` is the cleaner fix and benefits all future Insights playbooks needing membership checks. Filed as engine-patch item §7.1.

### Node 4 — `layer2Extract` (gated)

- **Action code**: `INS-L2X` (new — created during Wave C1 with `sprk_systemprompt = outcome-extraction@v1` per Wave C2)
- **Action type**: `AiAnalysis` (type 0) — uses existing `AiAnalysisNodeExecutor`
- **Inputs**:
  - From `sanitization.sanitizedText` (template: `{{sanitization.output.sanitizedText}}`)
  - From parameters: `parameters.practiceAreaHint`, `parameters.documentTypeHint` (Wave D3 injects the appropriate Layer 2 schema based on the (practice-area, doc-type) pair)
- **Outputs (`outputVariable: layer2`)**:
  ```json
  {
    "candidates": [
      { "fieldName": "string", "value": "any", "quote": "string", "confidence": "double", "displayHint": "string" }
    ]
  }
  ```
  Each candidate carries the same shape as the current `ExtractionField` projection (value, quote, confidence, displayHint). Layer 2 prompt's structured output drives the candidate count (e.g., 4 candidates for outcome-extraction: outcomeCategory, settlementAmount, outcomeDate, matterDurationDays).
- **Side effects**: LLM call (model `gpt-4o` per current code; per-action override via `sprk_systemprompt.parameters.model`).
- **Skip condition**: this node only executes when `checkLayer2Gate.selectedBranch == "layer2Extract"`. Per `PlaybookOrchestrationService`'s dependency-failure-skip rule, if Node 3 emits `insufficient`, the orchestrator marks this node's upstream as "branch not selected" and skips it. **This requires the orchestrator's branch-routing fix described in §7.2** — currently the engine treats `dependsOn` as AND-only (all-upstream-success). For sufficient/insufficient branching, the engine must consult `gate.selectedBranch` and skip nodes not on the selected path. **Wave B5 smoke results show this works empirically for `predict-matter-cost@v1`** via dependency-failure-skip (the `synthesize` node sees `checkSufficiency` "succeeded with insufficient verdict" and proceeds, which is **wrong** for a branch gate but the wrong-flow turned out OK because the synthesis prompt itself decided to decline). **For universal-ingest this won't work** because Layer 2 must NOT call the LLM when gated off (cost concern + correctness concern). **This is engine gap candidate #2 — see §7.2.**

### Node 5 — `groundingVerify`

- **Action code**: `INS-GRND` **(reused from Wave B — already in Dataverse)**
- **Action type**: `GroundingVerify` (type 70) — uses existing `GroundingVerifyNode` executor
- **Inputs (via ConfigJson)**:
  ```json
  {
    "citationsFrom": "layer2",
    "citationsJsonPath": "candidates",
    "sourceChunksFrom": "sanitization",
    "sourceChunksJsonPath": "chunks",
    "$comment": "Same shape as predict-matter-cost groundCitations node. Quote-level verification against raw chunks; failed citations are dropped from downstream."
  }
  ```
- **Outputs (`outputVariable: grounded`)**: `layer2.candidates[]` filtered to surviving candidates (those whose quote ground-verified to `Verified` or `VerifiedApproximate`).
- **Side effects**: None (pure quote matching against the raw chunks).

### Node 6 — `emitObservations`

- **Action code**: `INS-OBSE` (new — created during Wave C1)
- **Action type**: New `ObservationEmit` action type (or alternatively `DeliverToIndex` (type 50) reused — confirm during Wave C1). Recommendation: **add a dedicated `ObservationEmit` action type** because the semantic — "emit N observations, one per surviving candidate field, with per-observation upsert + mirror" — is not what `DeliverToIndex` does (it ships an entire payload to AI Search once).
- **Executor**: New `ObservationEmitterNodeExecutor` (Wave C1 creates this — thin wrapper around the existing `IObservationEmitter.EmitFromExtractionAsync` + `IObservationIndexUpserter` + `IObservationMirror`)
- **Inputs**:
  - From `grounded.candidates` (the post-ground-verify candidate list — empty when gate insufficient, since Layer 2 + GroundingVerify both skipped)
  - From `parameters.matterId`, `parameters.tenantId`
  - **Branch-aware**: when `gate.selectedBranch == "emitObservations"` (insufficient path), this node runs with `grounded == null` and emits 0 Layer 2 observations — only the L1 obs (already emitted by Node 2) remains. When `gate.selectedBranch == "layer2Extract"` (sufficient path), Layer 2 ran and `grounded` carries surviving candidates.
- **Outputs (`outputVariable: emission`)**:
  ```json
  {
    "observationsEmitted": "int (1 + N where N = surviving L2 candidates)",
    "layer1Classification": "string",
    "layer2Triggered": "boolean"
  }
  ```
  Shape matches today's `InsightsIngestResult` exactly. This is what `IInsightsAi.RunIngestAsync` (Wave C4) returns to callers.
- **Side effects**:
  - Index writes (fatal — propagated)
  - Dataverse mirror writes (non-fatal — caught + logged inside executor, matching current `TryMirrorAsync` semantics)
  - Telemetry: emits `UniversalIngestCompleted` event ID (preserved from current code) at node completion.

---

## 5. Dependency graph + branching shape

```
                              ┌────────────┐
                              │ sanitize   │ Node 1
                              └─────┬──────┘
                                    │
                                    ▼
                            ┌──────────────┐
                            │ layer1Classify│ Node 2 (also emits L1 obs)
                            └──────┬───────┘
                                   │
                                   ▼
                          ┌─────────────────┐
                          │ checkLayer2Gate │ Node 3 (EvidenceSufficiency)
                          └────┬───────┬────┘
                  sufficient  │       │  insufficient
                              ▼       ▼
              ┌──────────────────┐   │
              │  layer2Extract   │   │ Node 4 (skipped if insufficient)
              └────────┬─────────┘   │
                       ▼             │
              ┌──────────────────┐   │
              │ groundingVerify  │   │ Node 5 (skipped if insufficient)
              └────────┬─────────┘   │
                       │             │
                       └─────┬───────┘
                             ▼
                  ┌──────────────────┐
                  │ emitObservations │ Node 6 (always runs; emits 0 L2 if insufficient)
                  └──────────────────┘
```

**`dependsOnGraph.edges`** (for the playbook JSON):

```json
{
  "edges": [
    { "from": "sanitize",         "to": "layer1Classify" },
    { "from": "layer1Classify",   "to": "checkLayer2Gate" },
    { "from": "checkLayer2Gate",  "to": "layer2Extract",     "branch": "sufficient"   },
    { "from": "checkLayer2Gate",  "to": "emitObservations",  "branch": "insufficient" },
    { "from": "layer2Extract",    "to": "groundingVerify" },
    { "from": "groundingVerify",  "to": "emitObservations" }
  ]
}
```

**Branching semantics**: `emitObservations` has TWO upstream dependencies — `groundingVerify` (sufficient path) and `checkLayer2Gate` (insufficient path). The orchestrator must run it when **either** upstream completes successfully (per the selected branch). This is **OR-semantics on `dependsOn`** — and is **engine gap candidate #2** because today's `PlaybookOrchestrationService` treats `dependsOn` as AND (all upstream must succeed). Detailed patch in §7.2.

---

## 6. Parameterization schema

The playbook's `sprk_configjson.parameterSchema` (consumed by Wave C5 at invocation time):

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["documentId", "matterId", "tenantId"],
  "properties": {
    "documentId": {
      "type": "string",
      "format": "uuid",
      "description": "Source document Guid in spaarke-files-index. Fetched by IInsightsAi.RunIngestAsync before playbook invocation; sanitized text + chunks passed via parameters.documentText/chunks."
    },
    "matterId": {
      "type": "string",
      "format": "uuid",
      "description": "Subject matter Guid. Carried through as ExtractionScope.MatterId on emitted observations."
    },
    "tenantId": {
      "type": "string",
      "description": "Tenant ID for multi-tenant isolation. Required by index + mirror writes."
    },
    "documentText": {
      "type": "string",
      "description": "Concatenated raw document text. Resolved by IInsightsAi.RunIngestAsync from IIngestDocumentSource.FetchAsync. Passed in to avoid Dataverse round-tripping the document body through the playbook engine."
    },
    "chunks": {
      "type": "array",
      "description": "Raw (unmodified) text chunks for grounding verification. Resolved with documentText.",
      "items": { "type": "object" }
    },
    "documentRef": {
      "type": "string",
      "description": "Stable document reference used in evidence citations (e.g., 'doc:M-1234:filename.pdf')."
    },
    "practiceAreaHint": {
      "type": "string",
      "description": "OPTIONAL — practice area code (e.g., 'CTRNS', 'IPPAT') for Wave D2/D3 per-area Layer 1/Layer 2 prompt routing. When absent, defaults to litigation-style prompts per Phase 1 D-59.",
      "enum-source": "sprk_practicearea_ref",
      "$comment-enum-source": "Practice area codes are sourced from sprk_practicearea_ref rows (PA-1 clarification). The enum here is declarative — runtime validation happens in the action executor."
    },
    "documentTypeHint": {
      "type": "string",
      "description": "OPTIONAL — document type code (e.g., 'LEASE', 'NDA', 'INVOICE') for Wave D3 per-(area, doc-type) Layer 2 schema selection. When absent, default outcome-extraction@v1 schema applies."
    },
    "costCapOverride": {
      "type": "number",
      "description": "OPTIONAL — per-invocation cost cap in USD; overrides sprk_configjson.costCap. Used by Phase 2 cost-gating (currently observability-only per design D-52)."
    },
    "layer2Threshold": {
      "type": "number",
      "minimum": 0.0,
      "maximum": 1.0,
      "default": 0.7,
      "description": "OPTIONAL — confidence threshold for Layer 2 gate. Default 0.7 per Phase 1 D-59. Per-invocation override for SME calibration or fixture testing."
    },
    "outcomeBearingClassifications": {
      "type": "array",
      "items": { "type": "string" },
      "default": ["Order", "Settlement", "Verdict", "Judgment"],
      "description": "OPTIONAL — per-tenant or per-practice-area override of which Layer 1 classifications are outcome-bearing. Replaces the hard-coded DocumentClassificationExtensions.IsOutcomeBearing predicate (Phase 1 litigation-default)."
    }
  }
}
```

**Defaults (per `sprk_configjson` on the playbook row)**:
- `layer2Threshold = 0.7`
- `outcomeBearingClassifications = ["Order","Settlement","Verdict","Judgment"]`
- `costCapOverride = null` (uses tenant's monthly cap from D-P9)
- `practiceAreaHint = null` (litigation-default)

**Per-invocation override mechanism**: `IInsightsAi.RunIngestAsync` accepts a typed `InsightsIngestParameters` record; Wave C4 maps that record into the playbook's `Parameters` dictionary; Wave C5 documents how callers (e.g., a per-practice-area mirror handler) inject per-area overrides.

---

## 7. PlaybookExecutionEngine gap analysis

Two patches required. Both are small + stay in the engine per spec Risk-1 (don't fork).

### 7.1 Engine gap #1 — `EvidenceSufficiencyNode` membership predicate

**What's missing**: `EvidenceSufficiencyNode` (`Services/Ai/Nodes/EvidenceSufficiencyNode.cs`) supports `minCount` (number) and `requireNonEmpty` (boolean) rule shapes. It does NOT support a `predicate: "in" (value-in-set)` rule shape needed for Node 3's `outcomeBearingClassification` rule.

**Workaround if not patched**: in Node 2's executor, precompute `outcomeBearingFlag: bool` from the LLM-returned classification + `parameters.outcomeBearingClassifications`, expose it as `layer1.outcomeBearingFlag`, and use a `requireNonEmpty` rule in Node 3 on a derived path. This works but pollutes Node 2's output with parameterization concerns and makes the playbook less declarative.

**Recommended patch** (~15–20 LOC in `EvidenceSufficiencyNode.cs`):

```csharp
// Add a third rule shape to EvidenceSufficiencyRule:
public string? Predicate { get; init; }       // "eq", "in", "gt", "lt"
public object? Value { get; init; }           // scalar or array
public string? ReadFrom { get; init; }        // dotted path into the upstream output

// Add a branch in EvaluateRule(rule, upstreamOutputs):
case "in":
    var arr = rule.Value as IEnumerable<object>;
    var actual = ReadPath(upstreamOutputs, rule.From, rule.ReadFrom);
    return arr?.Any(v => v?.ToString() == actual?.ToString()) == true;
```

This patch is **isolated to one file**, has no downstream callers needing changes, and benefits every future Insights playbook that needs membership checks (Wave D's per-area classification routing also needs this).

**Filed as engine-patch item** for Wave C1: "Extend EvidenceSufficiencyNode with `predicate: in` rule shape."

### 7.2 Engine gap #2 — Branch-aware dependency resolution

**What's missing**: `PlaybookOrchestrationService.ExecuteNodeAsync` (lines 838–860) treats `dependsOn` as AND-only. When an EvidenceSufficiencyNode emits `verdict: insufficient`, its downstream "sufficient-path" nodes (Node 4 `layer2Extract` here, `synthesize` in `predict-matter-cost`) are NOT explicitly skipped — they execute, see the gate's "successful" output (verdict=insufficient is a "success" from the executor's standpoint), and run. In `predict-matter-cost@v1`, this surfaced as: `synthesize` ran even when `checkSufficiency` emitted insufficient, but the synthesis prompt itself decided to decline. **For universal-ingest, this is unacceptable** — `layer2Extract` (an expensive `gpt-4o` call) MUST NOT execute when the gate is insufficient.

**Empirical evidence (D-01 Wave B5 closure 2026-06-02)**: predict-matter-cost emitted a real decline via the synthesis prompt's own logic, not via engine skipping. This is fragile (depends on the synthesis prompt's good behavior) and cost-leaky (the synthesis LLM call ran for a known-declined evidence state). Universal-ingest cannot rely on this.

**Workaround if not patched**: each downstream node executor checks `gate.selectedBranch` at the top of `ExecuteAsync` and short-circuits to an `Ok(null, "branch not selected")` output. Works but distributes the routing logic across N executors (one per branching node), making the playbook's branch semantics implicit in code rather than explicit in JPS.

**Recommended patch** (~25–40 LOC in `PlaybookOrchestrationService.ExecuteNodeAsync`):

```csharp
// Before the existing "Check if dependencies failed" loop (line 838),
// add a "Check if dependencies' branch routing excludes this node" loop:
foreach (var depId in node.DependsOn)
{
    var depNode = graph.GetNode(depId);
    if (depNode == null) continue;

    var depOutput = runContext.GetOutput(depNode.OutputVariable);
    if (depOutput == null || !depOutput.Success) continue; // existing logic handles this

    // NEW: if the upstream produced a ConditionResult or EvidenceSufficiencyResult
    // with selectedBranch, and this node is not on the selected branch, skip it.
    var selectedBranch = ExtractSelectedBranch(depOutput);
    if (selectedBranch != null)
    {
        // The depsOn-graph edges in the playbook JSON carry an optional "branch" qualifier.
        // The graph builder (ExecutionGraph) needs to expose this per-edge branch label.
        // If the edge depNode→node carries branch X and selectedBranch != X, skip this node.
        var edgeBranch = graph.GetEdgeBranch(depNode.Id, node.Id);
        if (edgeBranch != null && edgeBranch != selectedBranch)
        {
            var skipReason = $"Branch '{edgeBranch}' not selected (upstream '{depNode.Name}' selected '{selectedBranch}')";
            runContext.RecordNodeSkipped();
            await writer.WriteAsync(PlaybookStreamEvent.NodeSkipped(
                runContext.RunId, runContext.PlaybookId, node.Id, node.Name, skipReason), cancellationToken);
            return NodeOutput.Ok(node.Id, node.OutputVariable, null, skipReason);
        }
    }
}
```

This patch requires three small parts:

1. **`ExecutionGraph`** (in `PlaybookOrchestrationService` or its companion file) — store per-edge `branch` label when parsing `dependsOnGraph.edges` from the playbook JSON. Today `ExecutionGraph` reads only `dependsOn` arrays per node (no edge metadata).
2. **`ExtractSelectedBranch(NodeOutput)`** helper — pull `selectedBranch` from `ConditionResult` or `EvidenceSufficiencyResult` structured output. Both record types already expose `SelectedBranch`.
3. **OR-semantics for nodes with multiple upstreams** (Node 6 `emitObservations` here): when a node's upstreams include nodes on BOTH branches (groundingVerify on sufficient path, checkLayer2Gate on insufficient path), the node should run when EITHER upstream succeeded-and-was-not-branch-skipped. The patch above handles this naturally: groundingVerify's skip when insufficient is "Ok(null)" (treated as success for flow control per line 857), so emitObservations sees both upstreams "successful" and runs. ✅

**Filed as engine-patch item** for Wave C1: "Add branch-aware dependency resolution to `PlaybookOrchestrationService.ExecuteNodeAsync` + edge-branch storage to `ExecutionGraph`."

**Both gaps fit spec Risk-1's "patches stay in the engine, don't fork" guidance.** Neither patch breaks existing playbooks (`predict-matter-cost@v1` continues to work because its branching is informal today; the patch hardens it by also adding explicit skip on the insufficient branch).

### 7.3 Non-gaps (verified during this design)

- ✅ **Template substitution** — `PlaybookOrchestrationService.ApplyConfigJsonTemplates` (added in Wave B per D-01 fix) already handles `{{var}}` substitution on ConfigJson before executor runs. Used by `checkLayer2Gate` to substitute `{{parameters.layer2Threshold}}` into the rule.
- ✅ **Parameter dictionary** — `PlaybookRunRequest.Parameters` already flows from `IInsightsAi.AnswerQuestionAsync` through `runContext` to executors via the template-context build (`PlaybookOrchestrationService.cs` builds `parameters` into the template context). Wave C5's per-invocation override surfaces here.
- ✅ **Action FK dispatch** — `sprk_actionid` FK is the canonical dispatch source (D-01 fix). No further engine work needed for action wiring.
- ✅ **`AiAnalysis` executor handles structured-output LLM calls** — Layer 1 + Layer 2 both use `AiAnalysisNodeExecutor` with structured-completion via `IOpenAiClient` (same mechanism the current orchestrator uses directly).

---

## 8. Action-row inventory (deliverable for Wave C1)

| Node | Action code | Existing? | ActionType | New executor needed? |
|---|---|---|---|---|
| 1 sanitize | `INS-SANI` | **NEW** (Wave C1) | `Sanitization` or new | YES — thin `SanitizerNodeExecutor` wrapping `IInsightsContentSanitizer` |
| 2 layer1Classify | `INS-L1C` | **NEW** (Wave C1) | `AiAnalysis` (0) | NO — reuses `AiAnalysisNodeExecutor` + L1 emission inside executor (or extend it to call `ILayer1ClassificationEmitter` post-LLM) |
| 3 checkLayer2Gate | `INS-EVID` | ✅ Wave B | `EvidenceSufficiency` (100) | NO (but extend with `predicate: in` per §7.1) |
| 4 layer2Extract | `INS-L2X` | **NEW** (Wave C1) | `AiAnalysis` (0) | NO — reuses `AiAnalysisNodeExecutor` |
| 5 groundingVerify | `INS-GRND` | ✅ Wave B | `GroundingVerify` (70) | NO |
| 6 emitObservations | `INS-OBSE` | **NEW** (Wave C1) | New `ObservationEmit` type | YES — thin `ObservationEmitterNodeExecutor` wrapping `IObservationEmitter` + `IObservationIndexUpserter` + `IObservationMirror` |

**New action rows: 4** (INS-SANI, INS-L1C, INS-L2X, INS-OBSE).
**New executors: 2** (`SanitizerNodeExecutor`, `ObservationEmitterNodeExecutor`).
**New action type values: 1** (`ObservationEmit` — pick a free integer in the 50–150 range, e.g., `130`; coordinate with Wave B's action-type-lookup catalog).
**Engine patches: 2** (per §7.1 + §7.2).

All four new action rows MUST be authored through the `jps-action-create` skill per D-01 owner direction (no inline JPS authoring). The playbook JSON itself MUST be authored through `jps-playbook-design`. Wave C1's POML enforces this.

---

## 9. Open items + handoffs

| Item | Resolution location |
|---|---|
| Confirm `Sanitization` action type integer (exists in catalog or add new) | Wave C1 task 020 — check action-type lookup before authoring INS-SANI |
| Decide whether to split `layer1Classify` (LLM) from `emitL1Observation` (write) | Wave C1 task 020 — current design keeps fused; reconsider if Wave D2 reveals coupling pain |
| Wire prompt content (`classification@v1`, `outcome-extraction@v1`) into `INS-L1C.sprk_systemprompt` + `INS-L2X.sprk_systemprompt` | Wave C2 task 021 (prompt migration) — depends on Wave A4 (013) versioning model |
| Confirm `IInsightsAi.RunIngestAsync` interface change (now invokes playbook with `InsightsIngestParameters`) | Wave C4 task 023 |
| Per-invocation parameter override mechanism for `layer2Threshold`, `practiceAreaHint`, etc. | Wave C5 task 024 |
| Per-practice-area `outcomeBearingClassifications` source (`sprk_practicearea_ref.sprk_outcomebearingjson`?) | Wave D2 task 031 + Wave A3 task 012 (2D taxonomy design) — confirm source-of-truth for the array |
| Engine patch #1 implementation (`EvidenceSufficiencyNode` predicate: in) | Wave C1 task 020 (combined with playbook authoring; bounded by §7.1) |
| Engine patch #2 implementation (branch-aware skip + edge-branch storage) | Wave C1 task 020 — combined with playbook authoring; bounded by §7.2; verify against `predict-matter-cost@v1` regression test |

---

## 10. Acceptance summary (matches POML acceptance-criteria)

| Criterion | Section | Status |
|---|---|---|
| Node count + breakdown decided with rationale | §3 + §3.2 | ✅ 6 nodes, Option B, rationale documented |
| Per-node design (inputs, outputs, action codes) documented | §4 | ✅ All 6 nodes detailed |
| Parameterization schema defined | §6 | ✅ JSON Schema authored; 9 properties, 3 required, 6 optional with defaults |
| `PlaybookExecutionEngine` gaps (if any) flagged with proposed patches | §7 | ✅ Two gaps identified, both with concrete patch outlines; both fit spec Risk-1 "don't fork" guidance |

---

*Design A5 complete. Feeds Wave C1 (universal-ingest@v1 playbook authoring), C3 (retire IngestOrchestrator.cs), C4 (rewire IInsightsAi.RunIngestAsync), C5 (parameterization). Engine-gap notes mirrored as a brief in `notes/spikes/engine-gap-analysis.md`.*
