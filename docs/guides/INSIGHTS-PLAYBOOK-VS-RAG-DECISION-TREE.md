# Playbook vs. RAG — Decision Tree for Insights Engine Consumption

> **Audience**: Developers + SMEs choosing how to answer a new Insights-shaped question.
> **Last Updated**: 2026-06-03 (Phase 1.5 Wave E4, task 043).
> **Companion to**: [`INSIGHTS-ENGINE-GUIDE.md`](./INSIGHTS-ENGINE-GUIDE.md) §2.2 (heuristic table) and §3 (playbook authoring procedure).

---

## TL;DR

The Insights Engine ships **two consumption paths**, both grounded in the same `spaarke-insights-index`:

| Path | Endpoint | Returns | When |
|---|---|---|---|
| **Playbook** (pre-authored JPS) | `POST /api/insights/ask` | Structured `Inference` / `Decline` / `Observation` envelope | High-value, repeated, structured questions |
| **RAG** (generic retrieval + summary) | `POST /api/insights/search` | Ranked Observations/Precedents + LLM-synthesized summary with grounded `[n]` citations | Open-ended, exploratory, long-tail questions |

Spaarke Assistant routes between them via [`InsightsIntentClassifier`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Insights/) (Wave E2). Any caller can override with `forceMode: "playbook" | "rag"`.

**Heuristic**: invest playbook-authoring effort in the top ~5–30 questions; everything else uses the RAG path.

---

## 1. The decision tree (≤1 page)

```
                          ┌──────────────────────────────────┐
                          │   New Insights-shaped question   │
                          └─────────────────┬────────────────┘
                                            │
                                            ▼
                          ┌──────────────────────────────────┐
              YES         │ Does the answer require a        │         NO
       ┌──────────────────┤ STRUCTURED output shape?         ├──────────────────┐
       │                  │ (UI fields, regulator deliverable,│                  │
       │                  │ workflow trigger)                │                  │
       │                  └──────────────────────────────────┘                  │
       │                                                                        │
       ▼                                                                        ▼
┌──────────────┐                                                      ┌──────────────┐
│ Does it have │   NO                                                  │  Is the      │
│ deterministic├────────────────┐                                      │  question    │
│ rules        │                │                                      │  open-ended? │
│ (evidence-   │                │                                      │  (free-form  │
│ sufficiency, │                ▼                                      │  natural-lang│
│ threshold,   │      ┌──────────────┐                                 │  exploration)│
│ branching)?  │      │ Is it asked  │                                 └──────┬───────┘
└──────┬───────┘      │ ≥N times /   │                                        │
       │ YES          │ week?        │                                  YES   │   NO
       │              └──────┬───────┘                                ┌───────┴───────┐
       ▼                     │                                        │               │
┌──────────────┐       YES ──┘  NO ──┐                                ▼               ▼
│ Need grounded│              │      │                          ┌──────────┐   ┌──────────┐
│ citations +  │              │      │                          │   RAG    │   │  Single  │
│ accuracy SLO?│              │      │                          │  path    │   │  doc?    │
└──────┬───────┘              │      │                          │ /search  │   │ → use    │
       │ YES                  │      │                          └──────────┘   │ Contract │
       ▼                      │      │                                         │ Review / │
┌──────────────┐              │      │                                         │ Lease    │
│  PLAYBOOK    │              │      │                                         │ Review   │
│   PATH       │◀─────────────┘      │                                         │ (other   │
│  /ask        │                     │                                         │ Spaarke  │
└──────────────┘                     │                                         │ AI)      │
                                     │                                         └──────────┘
                                     ▼
                            ┌──────────────────┐
                            │   RAG path       │
                            │   /search        │
                            │   (until volume  │
                            │   justifies a    │
                            │   playbook —     │
                            │   see §4 below)  │
                            └──────────────────┘
```

### Quick rule

- **Author a playbook when** the answer has a stable shape, deterministic evidence rules, and the question recurs at volume.
- **Use RAG when** the question is open-ended, low-volume, exploratory, or accuracy SLO is "best effort" with grounded citations in summary form.

---

## 2. Criteria — when to author a playbook

A playbook (JPS-defined orchestration in Dataverse: `sprk_playbook` + `sprk_analysisaction` rows; executed by `PlaybookExecutionEngine`) is the right answer when **two or more** of these are true:

| # | Criterion | Why playbook |
|---|---|---|
| 1 | **Structured output is load-bearing** — UI binds to specific fields (`{p25, p50, p75}`, `confidenceInDecline`, `suggestedActions[]`) | RAG returns prose; playbook returns a typed envelope |
| 2 | **Evidence-sufficiency rules are deterministic** — e.g., "need ≥12 comparable matters, p75/p25 spread ≤2x" | Playbook encodes the rule as an `EvidenceSufficiencyNode`; RAG cannot enforce |
| 3 | **Structured Decline is required** — when evidence is insufficient, the caller wants a `Decline` envelope, not "I don't know" | `DeclineToFindNode` emits structured `{reason, confidenceInDecline, suggestedActions}` |
| 4 | **Citations + grounded synthesis are mandatory** with audit trail (regulator deliverable, billable advice) | `GroundingVerifyNode` enforces "every claim has verbatim quote"; RAG synthesis cites but is best-effort |
| 5 | **Conditional branching is needed** — different paths for different evidence states, sub-question fan-out, multi-step reasoning | JPS playbook supports `dependsOn` + conditional nodes; RAG is single-shot |
| 6 | **Question is asked at volume** (heuristic: ≥10 times/week per practice area; or asked from a workflow/automation, not ad-hoc) | Authoring cost (~½–2 days) pays back at volume |
| 7 | **Accuracy SLO is binding** — playbook can be eval'd against a golden dataset (Wave D7 fixtures) and regressed in CI | RAG quality is harder to pin; suitable for "directionally correct" |
| 8 | **Deterministic answer required** — same inputs MUST produce same outputs (cache-friendly, regulator-friendly) | Playbook caches keyed on inputs; RAG re-synthesizes each call |

**Cost of authoring** (current process — see [`INSIGHTS-ENGINE-GUIDE.md` §3](./INSIGHTS-ENGINE-GUIDE.md#3-adding-a-new-insights-playbook-developer-process)):

- Author JSON playbook spec: 1–2h
- Author/iterate prompts in `sprk_analysisaction.sprk_systemprompt`: 2–4h per node (no code deploy needed — SMEs can iterate live)
- Deploy via `Deploy-Playbook.ps1`: 5 min
- Wire to `IInsightsAi.RunPlaybookAsync` or routing layer: 1–2h
- Golden-set evaluation: 2–4h

Total: **~½–2 days per playbook**.

---

## 3. Criteria — when to rely on RAG

RAG (`POST /api/insights/search` → `IRagService.SearchAsync` → ranked hits + LLM-synthesized summary) is the right answer when **two or more** of these are true:

| # | Criterion | Why RAG |
|---|---|---|
| 1 | **Question is open-ended** — natural-language exploration ("explain", "what are", "how does") | RAG handles free-form input without prompt re-authoring |
| 2 | **Long-tail / low-volume** — asked < 10 times/week, or one-off ad-hoc lookup | Playbook authoring cost is not justified |
| 3 | **Free-form answer is acceptable** — caller wants prose with citations, not typed fields | RAG returns LLM-synthesized summary with grounded `[n]` citations |
| 4 | **Exploratory phase** — SMEs are still discovering what questions to ask; question shape is unstable | RAG lets you iterate the question; playbook locks in the shape |
| 5 | **Fast iteration matters** — answer time-to-market is hours, not days | No JSON authoring, no deploy, just call `/api/insights/search` |
| 6 | **Single-document context is sufficient** AND the question is about retrieved evidence patterns | RAG ranks across the index; playbook would over-engineer |
| 7 | **Accuracy bar is "directionally correct with grounding"** — citations matter, but typed fields don't | RAG cites every claim against retrieved evidence; refuses to fabricate when results are empty |

**Important**: RAG still requires grounded citations. If the index returns zero hits, the summary is empty (no fabrication) — per the post-Task 040 behavior documented in [`INSIGHTS-ENGINE-GUIDE.md` §7](./INSIGHTS-ENGINE-GUIDE.md#7-querying-the-insights-index-directly--post-apiinsightssearch-phase-15-new).

---

## 4. Worked examples — initial 3 practice areas

Examples below cover CTRNS (Contracts/Transactional), IPPAT (IP/Patents), BNKF (Banking/Finance). The practice-area codes come from `sprk_practicearea_ref` (Wave D2).

### 4.1 CTRNS — Contracts / Transactional

| Question | Path | Why |
|---|---|---|
| "Predict matter cost for this APA (asset purchase agreement)" | **Playbook** (`predict-matter-cost@v1`) | Structured `{p25, p50, p75, costDriverDistribution}`; ≥12-matter evidence rule; high volume per deal-flow cadence; UI auto-populates Financial Summary field |
| "Is this NDA standard vs. negotiated?" | **Playbook** (candidate — not yet authored) | Structured `{deviationCount, riskTier, suggestedRedlines[]}`; recurring across portfolio; deterministic against MLA baseline |
| "What closing conditions are in this docset?" | **RAG** | Open-ended, free-form answer expected; appears in deal-review meetings (ad-hoc, low volume); the answer is exploratory prose with citations |
| "Show me 5 APAs with similar deal economics to this one" | **RAG** | Pure information retrieval; caller wants ranked hits + a synthesized summary; no structured envelope needed |
| "Summarize the indemnity provisions across our last 10 closings" | **RAG** | Open-ended aggregation; low volume (quarterly review); free-form summary with `[n]` citations to source docs is acceptable |

### 4.2 IPPAT — IP / Patents

| Question | Path | Why |
|---|---|---|
| "Is this office action response timely?" | **Playbook** (candidate) | Structured `{dueDate, daysRemaining, extensionAvailable, riskTier}`; deterministic deadline-math; high volume per patent portfolio; binds to docket UI |
| "Predict prosecution cost for this patent family through allowance" | **Playbook** (candidate; mirrors `predict-matter-cost` shape) | Structured cost envelope; ≥N comparable matters required; binds to budget UI |
| "Explain the prior art cited in this office action" | **RAG** | Open-ended; exploratory; low volume per matter; free-form prose with `[n]` citations to cited references is the right shape |
| "What claim amendments worked in similar prosecution histories?" | **RAG** | Long-tail exploratory; free-form answer with grounded examples; promote to playbook only if usage stabilizes |
| "Compare the file wrapper of this matter to 3 similar cases" | **RAG** | Comparative retrieval + summary; no fixed output schema; one-off attorney research |

### 4.3 BNKF — Banking / Finance

| Question | Path | Why |
|---|---|---|
| "Compute deal economics for this loan agreement" | **Playbook** (candidate) | Structured `{principal, rate, term, prepaymentPenalty, covenantTier}`; deterministic from doc fields; recurring across loan portfolio; binds to deal-sheet UI |
| "Is this covenant package borrower-friendly vs. peer benchmarks?" | **Playbook** (candidate) | Structured `{deviationFromMedian, peerCohortSize, riskTier}`; requires ≥N comparable loan-agreement evidence; binds to credit-committee report |
| "What covenants apply to this borrower?" | **RAG** | Open-ended retrieval; free-form list with `[n]` citations to specific covenant sections; low-volume ad-hoc lookup |
| "Find loan agreements with similar EBITDA-based maintenance covenants" | **RAG** | Pure information retrieval; ranked hits + synthesized summary; one-off due-diligence query |
| "Summarize material adverse change clauses across our 2024 closings" | **RAG** | Open-ended aggregation; quarterly review cadence; free-form summary acceptable |

**Total**: 5 examples × 3 areas = **15 worked examples** (covering both paths per area).

---

## 5. Evolution path — promoting a RAG query to a playbook

A question may start as RAG and evolve into a playbook as usage patterns stabilize. Signals + procedure:

### 5.1 Signals it's time to promote

| Signal | Threshold |
|---|---|
| Same/similar query asked from Spaarke Assistant | ≥10 times/week sustained over 4 weeks |
| Accuracy requirement increases (e.g., regulator/audit ask) | Any binding accuracy SLO |
| Output structure stabilizes (consumers always want the same fields) | UI/workflow explicitly binds to fields |
| Evidence-sufficiency rule becomes clear (e.g., "we always want ≥12 comparables") | Rule articulable in JSON Schema |
| Caller wants conditional behavior (different paths for different evidence states) | Branching logic emerges in caller code |
| Same prompt gets re-implemented by multiple callers | Duplication > 2 places |

### 5.2 Promotion procedure

1. **Stabilize the question shape** — observe RAG traffic for 2–4 weeks; capture the canonical query template + the output fields consumers actually use. Use BFF logs (`InsightsSearchEndpoint` request bodies) + Spaarke Assistant call-pattern analysis.
2. **Draft the JPS playbook** — follow [`INSIGHTS-ENGINE-GUIDE.md` §3](./INSIGHTS-ENGINE-GUIDE.md#3-adding-a-new-insights-playbook-developer-process). Reference `predict-matter-cost@v1` as the canonical worked example. Nodes typically include: `LiveFact` (current entity facts) → `IndexRetrieve` (RAG retrieval over the same `spaarke-insights-index`) → `EvidenceSufficiency` (the deterministic rule) → `Synthesize` (structured-output prompt) → `GroundingVerify` (citation enforcement) → either `ReturnInsightArtifact` or `DeclineToFind`.
3. **Author prompts in `sprk_analysisaction.sprk_systemprompt`** — use the `/jps-action-create` skill. SMEs can iterate prompts without code deploys.
4. **Wire to routing**:
   - If the playbook is the **canonical answer for a known practice area + document type**, register in [`InsightsActionRouter`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Insights/) Layer 2 (Wave D4). The 2D taxonomy will dispatch to it automatically.
   - If the playbook answers an **explicit caller intent** (e.g., "predict cost"), expose via `IInsightsAi.RunPlaybookAsync(playbookId, ...)`. Update the Spaarke Assistant tool-call schema (Wave E3) if exposed there.
   - Update the [intent classifier](../../src/server/api/Sprk.Bff.Api/Services/Ai/Insights/) (Wave E2) training prompt to include the new playbook as a candidate route.
5. **Deploy via `Deploy-Playbook.ps1`** — capture the playbook GUID; register in App Service config (`Insights__Playbooks__Map__<friendly_name_snake_case_v1>`) per §3.5 of the operator guide.
6. **Run a side-by-side eval** — measure playbook vs. RAG on the same query set; check `EvidenceSufficiency` rule fires correctly; verify `GroundingVerify` rejects ungrounded synthesis. Use Wave D7 synthetic fixtures + a golden subset of real production queries.
7. **Cut over the caller** — change Spaarke Assistant tool-call or workflow integration to route the question through `/ask` (playbook) instead of `/search` (RAG). Keep RAG as the fallback for low-confidence intents (per the [intent classifier confidence threshold](./INSIGHTS-ENGINE-GUIDE.md#7a-intent-classifier-phase-15-wave-e2)).
8. **Monitor**: emit `InsightArtifactType` distribution (Inference vs. Decline rate). A high Decline rate post-promotion means the evidence-sufficiency rule is too strict or the index is under-populated for that question class — tune the rule or extend ingest scope before declaring success.

### 5.3 When NOT to promote

- The question varies too much across callers (each caller wants a different field set) → keep RAG; let each caller post-process.
- Volume is sustained but the answer shape is genuinely free-form (e.g., "explain X") → keep RAG; playbook would add no structural value.
- The evidence-sufficiency rule cannot be articulated deterministically → keep RAG; the LLM-synthesized summary with `[n]` citations is honest about uncertainty.

### 5.4 Demotion (rare)

A playbook can be retired and traffic moved back to RAG if: (a) the question stops being asked at volume, (b) the structured output no longer matches consumer needs, or (c) the practice area is dropped. Procedure: deactivate the `sprk_playbook` row (`sprk_isactive = false`); the routing layer falls through to RAG; preserve the JPS spec in source control for re-activation.

---

## 6. Operational notes

- **Both paths share the same substrate**: `spaarke-insights-index` (Azure AI Search). Playbooks call `IndexRetrieveNode` → RAG layer; the `/search` endpoint calls `IRagService.SearchAsync` directly. Same index, same scope shape (post-Wave D6 generalization).
- **Both paths enforce grounding**: playbook via `GroundingVerifyNode`; RAG via the synthesis prompt's "cite every claim with [n]" instruction + empty-results-empty-summary invariant.
- **Both paths obey the kill-switch**: per ADR-032, the RAG service has a Null-Object that throws `FeatureDisabledException` ("ai.rag.disabled") → uniform 503 ProblemDetails. Playbook execution falls under `IPlaybookService` kill-switch ("ai.playbook.disabled"). Disabling either path fails loudly per ADR-004 dead-letter semantics.
- **Prompt iteration without code deploys**: both paths' prompts live in Dataverse (`sprk_analysisaction.sprk_systemprompt` for playbook nodes; the RAG synthesis prompt lives in `RagService` and is one of the few code-side prompts — promote to Dataverse if SME-iteration need emerges).
- **Caller override**: the intent classifier's routing decision is advisory. Any caller can pass `forceMode: "playbook" | "rag"` to bypass classification. Use this for known-shape callers (workflows, automated jobs) that don't need NLP routing.

---

## 7. References

- [`INSIGHTS-ENGINE-GUIDE.md`](./INSIGHTS-ENGINE-GUIDE.md) — operator/developer guide (§2.2 heuristic table, §3 playbook authoring, §7 `/search` endpoint, §7A intent classifier, §7B Spaarke Assistant integration)
- [`INSIGHTS-ENGINE-ARCHITECTURE.md`](../architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) — Spaarke-wide architecture (§3.5 facade boundary, layer model)
- [`ADR-013`](../../.claude/adr/ADR-013-ai-architecture.md) — AI architecture (facade refinement; CRUD code consumes Insights only via `IInsightsAi`)
- [`ADR-032`](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — Null-Object Kill-Switch Pattern (uniform 503 on disabled features)
- [`spec.md`](../../projects/ai-spaarke-insights-engine-r2/spec.md) — Phase 1.5 r2 spec (FR-04 RAG path, FR-05 forceMode, SC-04 acceptance)
- [`design.md`](../../projects/ai-spaarke-insights-engine-r2/design.md) — D-P15-06 hybrid consumption decision
- [`/jps-action-create`](../../.claude/skills/jps-action-create/SKILL.md) — JPS action authoring skill
- [`/jps-playbook-design`](../../.claude/skills/jps-playbook-design/SKILL.md) — end-to-end playbook design skill
- [`Deploy-Playbook.ps1`](../../scripts/Deploy-Playbook.ps1) — playbook deployment script

---

*Authored 2026-06-03 by Wave E task 043 (E4) of project `ai-spaarke-insights-engine-r2`.*
