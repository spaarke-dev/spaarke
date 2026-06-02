# Wave B — Action Codes + JPS Prompt Drafts (task 002)

> **Status**: DRAFT — pending owner review before `mcp__dataverse__create_record` execution
> **Date**: 2026-06-02
> **Source**: Designed per `/jps-action-create` skill conventions; deterministic-node adaptation of standard JPS schema (since these 6 nodes are NOT LLM-driven — they wrap services/interfaces)
> **Per D-01 §3 Q1 resolution**: Each row will have `sprk_actiontype` set to the matching ActionType enum integer; this is the dispatch key for `PlaybookOrchestrationService.cs:929`

---

## Overview — 6 rows to create

| # | sprk_actioncode | sprk_name | sprk_actiontype | INodeExecutor | LLM? |
|---|---|---|---|---|---|
| 1 | `INS-LIVE-FACT` | Insights — Live Fact Resolver | **80** | LiveFactNode | No (Dataverse query) |
| 2 | `INS-INDEX-RETRIEVE` | Insights — Index Retrieve | **90** | IndexRetrieveNode | No (AI Search query) |
| 3 | `INS-EVIDENCE-SUFFICIENCY` | Insights — Evidence Sufficiency | **100** | EvidenceSufficiencyNode | No (rule eval) |
| 4 | `INS-GROUNDING-VERIFY` | Insights — Grounding Verify | **70** | GroundingVerifyNode | No (mechanical text match) |
| 5 | `INS-DECLINE-TO-FIND` | Insights — Decline to Find | **110** | DeclineToFindNode | No (template substitution) |
| 6 | `INS-RETURN-ARTIFACT` | Insights — Return Insight Artifact | **120** | ReturnInsightArtifactNode | No (envelope serializer) |

**Deterministic-node JPS adaptation**: standard JPS schema (`$schema: https://spaarke.com/schemas/prompt/v1, $version: 1`) is preserved, but `instruction.role` describes the operation (not an LLM persona), `input` documents the ConfigJson + upstream output contract (not a document), `output.fields` documents the structured data shape. No LLM is invoked at runtime — these JPS records serve as **canonical contract documentation** + satisfy spec SC-08 (no `.txt` prompt files).

---

## Row 1 — INS-LIVE-FACT (ActionType 80)

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "Deterministic Live Fact resolver for Dataverse subjects (D-P12, design.md §2.1). Wraps ILiveFactResolver — performs direct Dataverse queries against the subject's entity; NO LLM call.",
    "task": "Read the {subject, predicate} pair from node ConfigJson, resolve via the registered ILiveFactResolver for the subject's entity type (matter/project/invoice), emit a FactArtifact with confidence=1.0.",
    "constraints": [
      "Subject format MUST be '<entityType>:<id>' where entityType is in the registered resolver catalog",
      "Confidence is ALWAYS 1.0 (deterministic — Live Facts are by definition certain)",
      "If subject cannot be resolved (entity not found / not authorized), emit NodeOutput.Error with code 'SUBJECT_NOT_FOUND'",
      "No fabrication: only emit facts that derive from a real Dataverse query result"
    ],
    "context": "Used in Insights synthesis playbooks (D-P14 predict-matter-cost) and the universal-ingest pipeline (Wave C). The emitted FactArtifact feeds downstream synthesis nodes via $ref."
  },
  "input": {
    "$comment": "Deterministic node — no document. Input sourced from ConfigJson at runtime.",
    "configJson": {
      "subject": { "type": "string", "required": true, "description": "Subject reference in format '<entityType>:<id>'. Templated at invocation: e.g., 'matter:{{matterId}}'" },
      "predicate": { "type": "string", "required": true, "description": "What fact to resolve (e.g., 'currentMatterFacts', 'totalSpend', 'matterDurationDays')" }
    }
  },
  "output": {
    "fields": [
      { "name": "subject", "type": "string", "description": "Echoed input subject" },
      { "name": "predicate", "type": "string", "description": "Echoed input predicate" },
      { "name": "value", "type": "object", "description": "The resolved fact value (shape varies by predicate)" },
      { "name": "confidence", "type": "number", "description": "Always 1.0 for Live Facts" },
      { "name": "evidence", "type": "array", "description": "Provenance refs (e.g., dataverse://sprk_matter/{guid})" }
    ],
    "structuredOutput": true
  },
  "metadata": {
    "author": "wave-b-r2",
    "authorLevel": 0,
    "createdAt": "2026-06-02T00:00:00Z",
    "description": "Deterministic Live Fact resolver per D-P12 — emits FactArtifact (confidence=1.0) from Dataverse queries via ILiveFactResolver. Used by Insights playbooks.",
    "tags": ["insights", "deterministic", "live-fact", "fact-resolver", "phase-1.5"]
  }
}
```

---

## Row 2 — INS-INDEX-RETRIEVE (ActionType 90)

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "Config-driven AI Search retrieval node for spaarke-insights-index (D-P12, SPEC §3.4.3). Composes OData filter + optional hybrid vector search; NO LLM call.",
    "task": "Read indexName + artifactType + filter + vectorQuery + topK from ConfigJson; execute the AI Search query against spaarke-insights-index; emit ranked observation/precedent documents in NodeOutput.StructuredData.",
    "constraints": [
      "Default indexName: 'spaarke-insights-index'; default topK: 12 (matches predict-matter-cost cohort size per SPEC)",
      "vectorQuery (string) is embedded via text-embedding-3-large (3072 dims) against contentVector field if provided",
      "filter (OData) is applied as-is; templated at invocation",
      "If requireEvidence=true in ConfigJson and result count == 0, emit NodeOutput.Error with code 'EVIDENCE_REQUIRED'; else emit empty array",
      "Honor subject scope from invocation context (matter/project/invoice scoping)"
    ],
    "context": "Used in Insights synthesis playbooks (D-P14 predict-matter-cost cohort + Precedent retrieval) and Wave E /api/insights/search RAG endpoint."
  },
  "input": {
    "configJson": {
      "indexName": { "type": "string", "default": "spaarke-insights-index", "description": "AI Search index to query" },
      "artifactType": { "type": "string", "description": "Filter by artifactType discriminator (e.g., 'observation', 'precedent')" },
      "filter": { "type": "string", "description": "OData filter expression" },
      "vectorQuery": { "type": "string", "description": "Text to embed for hybrid search (optional)" },
      "topK": { "type": "number", "default": 12, "description": "Max results to return" },
      "requireEvidence": { "type": "boolean", "default": false, "description": "If true, fail when result count == 0" }
    }
  },
  "output": {
    "fields": [
      { "name": "documents", "type": "array", "description": "Ranked AI Search docs with @search.score" },
      { "name": "totalCount", "type": "number", "description": "Total matched (capped at topK)" },
      { "name": "queryUsed", "type": "object", "description": "The composed query for debug/audit" }
    ],
    "structuredOutput": true
  },
  "metadata": {
    "author": "wave-b-r2",
    "authorLevel": 0,
    "createdAt": "2026-06-02T00:00:00Z",
    "description": "Deterministic AI Search retrieval node per D-P12 — composes filter + vector search against spaarke-insights-index; emits ranked artifact docs.",
    "tags": ["insights", "deterministic", "index-retrieve", "ai-search", "rag", "phase-1.5"]
  }
}
```

---

## Row 3 — INS-EVIDENCE-SUFFICIENCY (ActionType 100)

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "Deterministic evidence-sufficiency rule evaluator (D-P12, D-49 LAVERN Pattern #7). Reads prior node outputs and applies configured rules; NO LLM call.",
    "task": "Read rules[] from ConfigJson; for each rule, look up the referenced upstream node's output and count against the rule's threshold; emit verdict ('sufficient' / 'insufficient') + structured gap analysis.",
    "constraints": [
      "Verdict is deterministic — sufficient ONLY when ALL rules pass; otherwise insufficient",
      "Gap analysis must reference the failing rule by name + show actual vs required counts",
      "Selects downstream branch (used by orchestrator's branch routing): sufficient → synthesis path, insufficient → DeclineToFind path",
      "ConfigJson.rules must be a non-empty array of {name, from, countFrom, minCount}"
    ],
    "context": "The pre-condition gate before a DeclineToFind branch in synthesis playbooks. Without this gate, synthesis nodes might produce empty-evidence Inferences which EvidenceGuard would reject downstream."
  },
  "input": {
    "configJson": {
      "rules": {
        "type": "array",
        "required": true,
        "description": "Array of {name, from (upstream node name), countFrom (JSON path), minCount (threshold)}"
      }
    },
    "upstreamOutputs": {
      "$comment": "Each rule's 'from' field references an upstream NodeOutput.StructuredData by node name"
    }
  },
  "output": {
    "fields": [
      { "name": "verdict", "type": "string", "enum": ["sufficient", "insufficient"], "description": "Aggregate verdict across all rules" },
      { "name": "selectedBranch", "type": "string", "description": "Either 'sufficient' or 'insufficient' — drives orchestrator branch routing" },
      { "name": "ruleResults", "type": "array", "description": "Per-rule {name, have, need, passed} for transparency" },
      { "name": "gap", "type": "object", "description": "When insufficient: structured gap analysis {firstFailedRule, have, need, shortfall}" }
    ],
    "structuredOutput": true
  },
  "metadata": {
    "author": "wave-b-r2",
    "authorLevel": 0,
    "createdAt": "2026-06-02T00:00:00Z",
    "description": "Deterministic evidence-sufficiency gate per D-P12 + D-49. Emits verdict + gap analysis; drives synthesis-vs-decline branch routing.",
    "tags": ["insights", "deterministic", "evidence-sufficiency", "gate", "lavern-pattern-7", "phase-1.5"]
  }
}
```

---

## Row 4 — INS-GROUNDING-VERIFY (ActionType 70)

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "Mechanical citation verifier (D-P9, D-47, LAVERN ADR 10.6). Wraps IGroundingVerifier — performs verbatim text matching of citations against source chunks; NO LLM call.",
    "task": "Read citations from a prior node's output (per citationsFrom + citationsJsonPath); verify each citation's quoted snippet appears VERBATIM in one of the source chunks (per sourceChunksFrom + sourceChunksJsonPath); strip citations that fail; emit the verified set.",
    "constraints": [
      "Verification is strict text match (modulo whitespace normalization including CRLF↔LF per RB-T028-02 fix)",
      "Stripped citations are NOT annotated in the output (per current GroundingVerifier behavior); they're removed",
      "If allowStrip=false in ConfigJson, emit NodeOutput.Error on first failed citation instead",
      "EvidenceGuard downstream enforces the minimum-citation count after stripping"
    ],
    "context": "Used after synthesis nodes (e.g., predict-matter-cost's AgentService synthesis) to ensure cited evidence is verbatim from source. Without this gate, LLM-fabricated citations would pass through to InsightArtifacts."
  },
  "input": {
    "configJson": {
      "citationsFrom": { "type": "string", "required": true, "description": "Upstream node name carrying the citations" },
      "citationsJsonPath": { "type": "string", "default": "evidence", "description": "JSON path within citationsFrom output to the citations array" },
      "sourceChunksFrom": { "type": "string", "required": true, "description": "Upstream node name carrying source chunks (typically IndexRetrieve output)" },
      "sourceChunksJsonPath": { "type": "string", "default": "documents", "description": "JSON path within sourceChunksFrom to the chunks array" },
      "annotationText": { "type": "string", "default": "[citation could not be verified]", "description": "Reserved for future annotation-instead-of-strip mode" }
    }
  },
  "output": {
    "fields": [
      { "name": "verifiedCitations", "type": "array", "description": "The verified subset of input citations (failed ones removed)" },
      { "name": "strippedCount", "type": "number", "description": "How many citations failed verification" },
      { "name": "verificationReport", "type": "object", "description": "Per-citation {snippet, sourceFound, sourceChunkId} for audit" }
    ],
    "structuredOutput": true
  },
  "metadata": {
    "author": "wave-b-r2",
    "authorLevel": 0,
    "createdAt": "2026-06-02T00:00:00Z",
    "description": "Mechanical citation verifier per D-P9 / LAVERN ADR 10.6 — strips citations whose snippets don't appear verbatim in source chunks.",
    "tags": ["insights", "deterministic", "grounding-verify", "honesty-enforcement", "lavern-10-6", "phase-1.5"]
  }
}
```

---

## Row 5 — INS-DECLINE-TO-FIND (ActionType 110)

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "Deterministic exit node that emits a structured DeclineResponse (D-49 LAVERN Pattern #7, D-P12). Invoked when EvidenceSufficiencyNode returns insufficient; NO LLM call — composes from template + upstream gap analysis.",
    "task": "Read reason + explanationTemplate + suggestedActions[] + confidenceInDecline from ConfigJson; read the gap analysis from the referenced upstream node (typically EvidenceSufficiencyNode); compose the explanation by substituting template tokens; emit DeclineResponse.",
    "constraints": [
      "explanationTemplate supports tokens like {have}, {need}, {rule}, {from} — substituted from upstream gap analysis",
      "suggestedActions[] are passed through verbatim; they're SME-authored remediation hints",
      "confidenceInDecline defaults to 0.95 (high — decline rules are deterministic)",
      "Output IS a DeclineResponse — NOT an InsightArtifact. Phase 1 piggybacks on null-artifact path; Phase 1.5+ may add explicit ReturnDeclineNode"
    ],
    "context": "Pairs with EvidenceSufficiencyNode in synthesis playbooks (D-P14 predict-matter-cost). When sufficiency returns insufficient, the orchestrator routes to this node instead of the synthesis branch."
  },
  "input": {
    "configJson": {
      "reason": { "type": "string", "default": "insufficient-evidence", "description": "Reason code for the decline" },
      "from": { "type": "string", "required": true, "description": "Upstream node name (typically the EvidenceSufficiencyNode) for gap-analysis source" },
      "explanationTemplate": { "type": "string", "required": true, "description": "Template with {have}, {need}, {rule}, {from} tokens" },
      "suggestedActions": { "type": "array", "description": "SME-authored remediation hints" },
      "confidenceInDecline": { "type": "number", "default": 0.95, "description": "Confidence in the decline decision" }
    }
  },
  "output": {
    "fields": [
      { "name": "declineResponse", "type": "object", "description": "The composed DeclineResponse per D-49: {reason, explanation, minimumEvidenceNeeded, suggestedActions[], confidence}" }
    ],
    "structuredOutput": true
  },
  "metadata": {
    "author": "wave-b-r2",
    "authorLevel": 0,
    "createdAt": "2026-06-02T00:00:00Z",
    "description": "Deterministic decline emitter per D-49 / D-P12 — composes structured DeclineResponse from template + gap analysis; pairs with EvidenceSufficiencyNode.",
    "tags": ["insights", "deterministic", "decline-to-find", "honesty-enforcement", "lavern-pattern-7", "phase-1.5"]
  }
}
```

---

## Row 6 — INS-RETURN-ARTIFACT (ActionType 120)

```json
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "Final node of an Insights synthesis playbook (D-P12, D-P14). Serializes upstream synthesis output into a typed InsightArtifact envelope; NO LLM call.",
    "task": "Read the upstream synthesis output (per from + valueFrom + evidenceFrom + confidenceFrom + reasoningFrom); compose an InsightArtifact (Fact / Observation / Precedent / Inference) per artifactKind; run EvidenceGuard.Validate; emit the envelope.",
    "constraints": [
      "artifactKind MUST be one of: fact | observation | precedent | inference",
      "EvidenceGuard.Validate runs BEFORE emission — if allowEmptyEvidence=false and evidence[] is empty, throws EvidenceRequiredException (D-A23/D-48)",
      "subject + predicate are taken from ConfigJson; producedById, producedByKind, producedByVersion mark provenance",
      "Node name MUST be exactly 'ReturnInsightArtifactNode' — InsightsPlaybookExecutionCache.ReturnInsightArtifactNodeName is hardcoded for stream draining"
    ],
    "context": "Terminates synthesis playbooks; the orchestrator's stream-drain extracts this node's output as the final cached InsightArtifact."
  },
  "input": {
    "configJson": {
      "from": { "type": "string", "required": true, "description": "Upstream node name carrying the synthesis result" },
      "artifactKind": { "type": "string", "required": true, "enum": ["fact", "observation", "precedent", "inference"], "description": "Envelope type per design.md §2" },
      "subject": { "type": "string", "required": true, "description": "Subject ref (templated, e.g., 'matter:{{matterId}}')" },
      "predicate": { "type": "string", "required": true, "description": "Predicate name (e.g., 'predictedCost')" },
      "displayHint": { "type": "string", "description": "UI rendering hint (e.g., 'currency-usd-range')" },
      "producedById": { "type": "string", "required": true, "description": "Provenance: e.g., 'playbook://predict-matter-cost@v1'" },
      "producedByKind": { "type": "string", "required": true, "description": "Provenance kind: 'playbook' | 'system' | 'sme'" },
      "producedByVersion": { "type": "string", "description": "Producer version label" },
      "valueFrom": { "type": "string", "default": "value", "description": "JSON path within 'from' output to the value field" },
      "evidenceFrom": { "type": "string", "default": "evidence", "description": "JSON path within 'from' output to the evidence array" },
      "confidenceFrom": { "type": "string", "default": "confidence", "description": "JSON path within 'from' output to the confidence number" },
      "reasoningFrom": { "type": "string", "default": "reasoning", "description": "JSON path within 'from' output to the reasoning string" },
      "allowEmptyEvidence": { "type": "boolean", "default": false, "description": "If false, EvidenceGuard throws on empty evidence (default)" }
    }
  },
  "output": {
    "fields": [
      { "name": "artifact", "type": "object", "description": "The composed InsightArtifact envelope per design.md §2 (FactArtifact / ObservationArtifact / PrecedentArtifact / InferenceArtifact)" }
    ],
    "structuredOutput": true
  },
  "metadata": {
    "author": "wave-b-r2",
    "authorLevel": 0,
    "createdAt": "2026-06-02T00:00:00Z",
    "description": "Final node of synthesis playbooks per D-P12 / D-P14 — composes InsightArtifact envelope from upstream synthesis output with EvidenceGuard validation.",
    "tags": ["insights", "deterministic", "return-artifact", "envelope-serializer", "evidence-guard", "phase-1.5"]
  }
}
```

---

## sprk_analysisaction row attributes (per row)

In addition to `sprk_systemprompt` (the JPS JSON above), each row will have:

| Attribute | Value (per row) |
|---|---|
| `sprk_actioncode` | INS-LIVE-FACT / INS-INDEX-RETRIEVE / INS-EVIDENCE-SUFFICIENCY / INS-GROUNDING-VERIFY / INS-DECLINE-TO-FIND / INS-RETURN-ARTIFACT |
| `sprk_name` | "Insights — Live Fact Resolver" / etc. |
| `sprk_description` | (1-2 sentence high-level description, mirroring the JPS metadata.description) |
| `sprk_actiontype` (int) | 80 / 90 / 100 / 70 / 110 / 120 |
| `sprk_tags` | "insights, deterministic, [node-type], phase-1.5" |
| `sprk_sortorder` | 80, 90, 100, 70, 110, 120 (matches actiontype for stable visual sort) |
| `sprk_ActionTypeId` | left null (per D-01 Q2 resolution — not load-bearing) |
| `statecode` / `statuscode` | 0 / 1 (Active) — default |

## ✅ Created Guids (2026-06-02)

All 6 rows created in Spaarke Dev. Owner approved JPS-contract-docs approach + execute-all-6 strategy.

| Action Code (final) | sprk_actiontype Enum | sprk_analysisactionid |
|---|---|---|
| **INS-FACT** | LiveFact (80) | `5137365a-825e-f111-a825-6045bdebafa9` |
| **INS-IDXR** | IndexRetrieve (90) | `23939266-825e-f111-a825-6045bdebafa9` |
| **INS-EVID** | EvidenceSufficiency (100) | `6139aa6c-825e-f111-a825-6045bdebafa9` |
| **INS-GRND** | GroundingVerify (70) | `32eafa72-825e-f111-a825-6045bdebafa9` |
| **INS-DECL** | DeclineToFind (110) | `d1121079-825e-f111-a825-6045bdebafa9` |
| **INS-RART** | ReturnInsightArtifact (120) | `96d52e7f-825e-f111-a825-6045bdebafa9` |

**Note**: Action codes shortened from the original drafts (e.g., `INS-LIVE-FACT` → `INS-FACT`) because `sprk_actioncode` has a max length of 10 characters (empirically discovered during create — `INS-LIVE-FACT` (13 chars) was rejected). Final codes match the `INS-OBS` precedent's 7-character format.

### 🔴 D-01 Q1 empirically confirmed: `sprk_actiontype` is ABSENT on `sprk_analysisaction`

The first `mcp__dataverse__create_record` call with `sprk_actiontype: 80` returned:
> "Attribute 'sprk_actiontype' not found in table 'sprk_analysisaction'."

This **confirms** the field doesn't exist on the entity schema in Spaarke Dev (despite the C# code at `AnalysisActionService.cs:392` having `[JsonPropertyName("sprk_actiontype")]`). The 6 rows were created WITHOUT this field.

**Architectural implication**: The FK-path dispatch (`sprk_playbooknode.sprk_actionid` → `sprk_analysisaction.sprk_actiontype`) is closed. All dispatch MUST come through `sprk_playbooknode.sprk_configjson.__actionType` per `playbook-architecture.md` line 54. This shifts emphasis of Wave B tasks 003 + 004:
- **Task 003 lint** must verify `__actionType` is present + correct per node in deployed `sprk_configjson`
- **Task 004 redeploy** is the load-bearing fix step: get Deploy-Playbook.ps1 to write `__actionType: 80/90/100/70/110/120` correctly into each node's configjson

The 6 action rows still serve as:
- Linkable Dataverse refs via `sprk_actionid` (audit, UI traversal)
- Documentation of executor contracts (the JPS prompts)
- Future-proofing if `sprk_actiontype` field is added to the entity in a later schema migration

## ✅ sprk_actiontypeid lookup FK set (2026-06-02 addendum per owner direction)

After D-01 Q1 empirical confirmation showed `sprk_actiontype` (int) absent on the entity, owner directed Path (a): create lookup rows in `sprk_analysisactiontype` + set `sprk_actiontypeid` FK on the 6 INS-* rows for proper categorization.

### 6 new sprk_analysisactiontype lookup rows created

| Lookup name | sprk_analysisactiontypeid |
|---|---|
| `70 - Grounding Verify` | `a1a3a6e6-8f5e-f111-a825-70a8a59455f4` |
| `80 - Live Fact Resolver` | `a2a3a6e6-8f5e-f111-a825-70a8a59455f4` |
| `90 - Index Retrieve` | `a5a3a6e6-8f5e-f111-a825-70a8a59455f4` |
| `100 - Evidence Sufficiency` | `a9a3a6e6-8f5e-f111-a825-70a8a59455f4` |
| `110 - Decline to Find` | `8d0b9fec-8f5e-f111-a825-70a8a59455f4` |
| `120 - Return Insight Artifact` | `8e0b9fec-8f5e-f111-a825-70a8a59455f4` |

Naming convention `NN - Name` mirrors the existing "01 - Extraction" / "02 - Classification" pattern so `AnalysisActionService.ExtractSortOrderFromTypeName()` (line 51) parses the numeric prefix correctly. This gives the 6 rows proper sort order in admin forms.

### Final sprk_actiontypeid FK mappings on the INS-* rows

| INS-* row | sprk_actiontypeid → lookup row |
|---|---|
| INS-GRND | `a1a3a6e6-...` (70 - Grounding Verify) |
| INS-FACT | `a2a3a6e6-...` (80 - Live Fact Resolver) |
| INS-IDXR | `a5a3a6e6-...` (90 - Index Retrieve) |
| INS-EVID | `a9a3a6e6-...` (100 - Evidence Sufficiency) |
| INS-DECL | `8d0b9fec-...` (110 - Decline to Find) |
| INS-RART | `8e0b9fec-...` (120 - Return Insight Artifact) |

### Architectural note — what this does and doesn't fix

| Concern | Status |
|---|---|
| Admin-form display (rows show their type label) | ✅ Fixed — visible in `sprk_analysisaction` form's Action Type field |
| Sort order via `ExtractSortOrderFromTypeName` name-prefix parsing | ✅ Working — INS-* rows get sortOrder 70/80/90/100/110/120 |
| ActionType dispatch via `action.ActionType` reading `entity.ActionTypeValue` (= `sprk_actiontype` int) | ❌ Still broken — field absent on entity. Code reads from missing field, defaults to AiAnalysis(0) |
| ActionType dispatch via `sprk_playbooknode.sprk_configjson.__actionType` | ⏳ To be addressed in B3 + B4 (the working path) |

**Potential future code-side fix** (not in Wave B scope): update `AnalysisActionService.cs:53-55` to derive `ActionType` from the parsed `sortOrder` when `entity.ActionTypeValue` is null + the parsed prefix matches an `ActionType` enum value. This would make `sprk_actiontypeid` the de-facto single source of dispatch truth, eliminating the dependence on the missing `sprk_actiontype` int field.

## Next step

Execute 6 `mcp__dataverse__create_record` calls (one per row). On each create:
- If `sprk_actiontype` is rejected → field doesn't exist; report immediately + revisit D-01 Q1 with empirical evidence
- If `sprk_actiontype` accepted → row created; capture sprk_analysisactionid Guid for downstream tasks (003 lint check ref, task 003 playbook JSON updates)

After all 6 rows created: write the action-code → Guid map back to this file under a new "## Created Guids (2026-06-02)" section for tasks 003+ consumption.
