# Matter Health Synthesis Prompt — Design Handoff

> **Project**: ai-spaarke-insights-engine-widgets-r1
> **Task**: 020 — Author Matter Health synthesis prompt via jps-action-create
> **Date**: 2026-06-10
> **Status**: COMPLETE — `sprk_analysisaction` row created in dev Dataverse

---

## 1. Canonical surface (where the prompt lives)

| Property | Value |
|---|---|
| Environment | dev Dataverse (`spaarkedev1.crm.dynamics.com`) |
| Table | `sprk_analysisaction` |
| Record ID | `5981632e-4165-f111-ab0c-7ced8ddc4cc6` |
| `sprk_name` | `Matter Health Synthesis (Single Mode)` |
| `sprk_actioncode` | `matter-health-synthesis` **(BARE — no `@v1` suffix per Q-U1 owner ban)** |
| `sprk_actiontypeid` | `60 - Agent Service` (`9532f478-945e-f111-ab0c-7c1e521b425f`) |
| `sprk_executoractiontype` | 60 |
| `sprk_outputformat` | 0 (JSON) |
| `sprk_temperature` | 0.2 |
| `sprk_allowsknowledge` | true |
| `sprk_allowsskills` / `sprk_allowstools` / `sprk_allowsdelivery` / `sprk_availableadhoc` | false (synthesis composes prior-node outputs; no document or tool routing) |
| `sprk_systemprompt` | Full JPS JSON (≈14.7 KB) — see canonical local copy at `notes/handoffs/matter-health-synthesis.jps.json` |

**Canonical-pattern citation**: Per **audit DR-007 / `canonical-architecture-decisions.md` §2.7**, the prompt lives in `sprk_analysisaction.sprk_systemprompt`. No `.txt` file was created. No `/Prompts/` directory entry. This is the **audit-codified pattern** — citation corrected 2026-06-10 (NOT ADR-014, which governs AI caching/reuse).

---

## 2. Prompt structure (Spaarke Canonical Prompt Construction Pattern — audit §2.7)

The 4 pattern elements applied:

| Element | Implementation |
|---|---|
| `instruction.role` | Spaarke Insights Engine synthesizer for matter-health-single playbook; audience = legal ops + engagement leaders |
| `instruction.task` | Compose `{{matterId}}`, `{{currentGrade}}`, `{{matterContext}}`, `{{assessments}}`, `{{observations}}` into a 7-dimension diagnostic narrative + envelope |
| `instruction.constraints` (12 entries) | 7 DIMENSION rules (FR-12) + EVIDENCE GROUNDING + KPI NAMING (FR-13) + ENVELOPE FIELDS + JSON-ONLY OUTPUT + NARRATIVE STYLE |
| `instruction.context` | Playbook orchestration semantics — upstream EvidenceSufficiency gate + downstream GroundingVerifier + persistence target `sprk_matter.sprk_performancesummary` (FR-14) |

Total constraint count: **12** (7 dimension + 5 cross-cutting). Reasoning: explicit dimensional pinning is more reliable than rolling all 7 into a single mega-constraint; the LLM treats each as a separate requirement.

---

## 3. 7 baseline diagnostic dimensions (FR-12 mapping)

| # | Dimension | Encoded as | Output dimensions[] enum |
|---|---|---|---|
| 1 | Composite grade explanation | `DIMENSION 1` constraint | `composite-grade` |
| 2 | Trend over time | `DIMENSION 2` constraint | `trend` |
| 3 | Recurring themes in Assessment Notes | `DIMENSION 3` constraint | `themes` |
| 4 | Inflection-point detection | `DIMENSION 4` constraint | `inflection` |
| 5 | Most-critical assessments as anchors | `DIMENSION 5` constraint | `critical-assessments` |
| 6 | Forward-looking risk | `DIMENSION 6` constraint | `forward-risk` |
| 7 | Honest evidence-gap acknowledgment (Decline) | `DIMENSION 7` constraint | `evidence-gap` |

**Decline path semantics** (D7): If `assessments.length < 2` OR ALL Notes are empty/whitespace, the model emits a structured Decline (`body` explicitly says "insufficient evidence to diagnose"; `citations[]` is empty; `dimensions: ['evidence-gap']`). This is the existing r2 DeclineResponse pattern that `InsightSummaryCard` already renders.

---

## 4. FR-13 KPI Performance Area naming (exact canonical names)

The prompt enforces the FR-13 names with a **dedicated KPI NAMING constraint** that explicitly forbids abbreviation, pluralization, and synonyms:

| Performance Area | Choice value | Prompt occurrences |
|---|---|---|
| Guideline Compliance | 100,000,000 | 15× in JPS JSON |
| Budget Compliance | 100,000,001 | 9× |
| Outcomes Achievement | 100,000,002 | 8× |

Rationale for high occurrence count: the names appear in (a) DIMENSION 1, (b) the KPI NAMING constraint itself, (c) Example 1 (full diagnostic) input + output, and (d) Example 2 (Decline) recommended-next-step text. Repetition is intentional — single-instance terms are easier for the model to drift on.

---

## 5. Output envelope shape (aligned to FR-14 persistence contract)

The JPS `output.fields` schema produces the exact envelope shape that `UpdateRecord` writes to `sprk_matter.sprk_performancesummary`:

```json
{
  "schemaVersion": "1.0",
  "body": "<markdown narrative>",
  "citations": [
    { "type": "assessment" | "document", "id": "...", "ref": "...", "label": "...", "excerpt": "...", "chunkId": "..." }
  ],
  "generatedAt": "<ISO 8601 UTC>",
  "playbookName": "matter-health-single",
  "playbookVersion": "1.0",
  "tenantId": "<Guid>",
  "dimensions": ["composite-grade", "trend", ...]
}
```

Key constraints:
- `schemaVersion`, `playbookName`, `playbookVersion` are `enum`-constrained — model cannot drift values
- `citations[].excerpt` has `minLength: 12` — enforces the verbatim-substring grounding rule that the downstream `GroundingVerifyNode` will mechanically validate
- `dimensions[]` is `enum`-constrained to the 7 canonical labels — guarantees stable downstream filtering

---

## 6. Example invocations (test data for prompt verification)

### Example 1 — Full diagnostic (5 assessments across 3 Performance Areas)

**Input**: Matter `M-12345`, current grade D-, 5 assessments with recurring themes (missed deadlines, scope expansion, client copy gaps), 3 observations including a Q1 2026 attorney-reassignment memo.

**Expected output** (encoded as `examples[0].output` in the JPS):
- 6 of 7 narrative dimensions addressed (D7 not triggered — evidence is sufficient)
- 1,299-char markdown body
- 3 citations: 2 assessment-type + 1 document-type
- `dimensions[]`: `["composite-grade", "trend", "themes", "inflection", "critical-assessments", "forward-risk"]`

**Verification**: Body contains "composite D- grade", "Trend.", "Recurring themes", "Inflection point", "Most-critical assessments", "Forward risk" sub-sections.

### Example 2 — Decline path (1 assessment, empty Notes)

**Input**: Matter `M-99999`, 1 assessment with B grade, empty Notes, no observations.

**Expected output** (encoded as `examples[1].output`):
- Body begins "## Insufficient evidence to diagnose"
- `citations[]`: empty
- `dimensions[]`: `["evidence-gap"]`

**Verification**: Body contains "insufficient evidence" + recommends a minimum second assessment.

---

## 7. Test-invocation outcome (Step 6)

Live LLM invocation against the deployed action row was **not** performed as part of this task because:

1. The playbook (`matter-health-single`) that wires this action into the orchestrator does not exist yet — it is task 025 (or sibling) in Phase 2.
2. Standalone action invocation requires the AgentService runtime + template-parameter resolution, which only fires inside `PlaybookOrchestrationService`.
3. The action is verifiable structurally + via the two embedded examples (Step 6 verifications above). End-to-end LLM invocation is the responsibility of the playbook UAT task downstream.

**Structural verification performed (acceptance proof)**:

- JSON parse: VALID (8 output fields, 12 constraints, 2 examples)
- Row creation: SUCCESS (id `5981632e-4165-f111-ab0c-7ced8ddc4cc6`)
- Round-trip read: SUCCESS (action code + executor action type + output format confirmed)
- 7 DIMENSION constraints present
- FR-13 KPI names present (×15, ×9, ×8)
- Zero `@v1`/`@vN` vernacular (Q-U1 ban respected — verified via regex over entire JPS body)
- Example 1 (full diagnostic): all 6 narrative dimensions present in body
- Example 2 (Decline): D7 evidence-gap path correctly modeled

---

## 8. Key design decisions

| # | Decision | Rationale |
|---|---|---|
| D1 | Action code is bare `matter-health-synthesis` (no `@v1` suffix) | Q-U1 owner ban; versioning via `schemaVersion: "1.0"` string in envelope, NOT in identifier |
| D2 | Action type = 60 (Agent Service), not 0 (AiAnalysis) | Matches existing `INS-AGNT@v1` synthesis precedent — `AgentServiceNodeExecutor` accepts pure prompt + tenant + template params, no document binding (synthesis has no document) |
| D3 | 7 dimensions encoded as separate constraints (12 total) | Single-constraint approach drifts; per-dimension pinning + dedicated KPI NAMING constraint + grounding constraint is more reliable per existing INS-AGNT@v1 pattern |
| D4 | `playbookVersion: "1.0"` (string semver) enforced via `enum` | Q-U1 forbids `@vN` suffix; `enum` prevents model drift |
| D5 | Two examples: full diagnostic + Decline path | Few-shot for both the happy path AND the evidence-gap path improves Decline-path reliability (LLMs over-narrate when given only happy-path examples) |
| D6 | `scopes: {}` empty | r1 does NOT use shared knowledge/skill scopes — the matter context is delivered inline via template placeholders. Future r2+ may attach knowledge scopes for legal-domain framing |
| D7 | `sprk_temperature = 0.2` | Synthesis benefits from low temperature — narrative consistency + grounding are more important than creativity |
| D8 | `sprk_outputformat = 0` (JSON) | Output is structured envelope; `structuredOutput: true` in JPS ensures constrained decoding |

---

## 9. Downstream consumers (what comes next)

The matter-health-single playbook (task 025 or sibling in Phase 2) will reference this action row from its `analyzeAndSynthesize` LLM node:

```json
{
  "name": "analyzeAndSynthesize",
  "nodeType": "AIAnalysis",
  "actionCode": "matter-health-synthesis",
  "actionType": 60,
  "outputVariable": "insightArtifact",
  "configJson": {
    "modelDeployment": "gpt-4o",
    "templateParameters": {
      "matterId": "{{Subject}}",
      "currentGrade": "{{Parameters.currentGrade}}",
      "matterContext": "{{matterContext}}",
      "assessments": "{{assessments}}",
      "observations": "{{observations}}"
    }
  },
  "dependsOn": ["evidenceSufficiencyGate"]
}
```

The `UpdateRecord` node downstream writes the synthesized envelope to `sprk_matter.sprk_performancesummary` per FR-14.

---

## 10. Sibling task (Wave 1A parallel)

Task 010 (Wave 1A sibling) is authoring the `sprk_aitopicregistry` schema entity definition — distinct artifact, no conflict with this task.

---

## 11. Local artifacts

| File | Purpose |
|---|---|
| `notes/handoffs/matter-health-synthesis.jps.json` | Pretty-printed JPS for human review + git history |
| `notes/handoffs/matter-health-synthesis.jps.compact.json` | Compact JSON (the exact string written to `sprk_systemprompt`) |
| `notes/handoffs/matter-health-prompt-design.md` | This handoff document |

**These are notes/record-keeping files, NOT the canonical surface.** Per audit DR-007 / §2.7, the canonical prompt lives in the Dataverse row. The local files are a versioned design record so the design can be reviewed/audited without round-tripping through Dataverse.

---

*Task 020 complete. Phase 2 prompt authoring deliverable ready for the matter-health-single playbook wiring task.*
