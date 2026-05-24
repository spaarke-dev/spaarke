# LAVERN Pattern Assessment — Insights Engine r1

> **Date**: 2026-05-22
> **Owner**: Spaarke Engineering
> **Status**: Decision capture — Phase 1 expansion ratified (architecture + scaffold for Precedent layer; four enforcement primitives)
> **Source documents**:
> - [`projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md`](../ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md) — 12 patterns from AnttiHero/lavern (Apache 2.0)
> - [`projects/ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md`](../ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md) — six user-interaction modes
> - This project's [decisions.md](decisions.md), [SPEC.md](SPEC.md), [`design.md`](design.md), [`INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md)

---

## 1. Purpose

Capture the analysis that led to Phase 1 Track A expansion with deliverables D-A22 through D-A27 (and corresponding decisions D-46 through D-51). Preserves the "why these five patterns and not the other seven" reasoning so future contributors can revisit the decision basis without re-deriving it from the LAVERN source documents.

This document is a **companion** to:
- `decisions.md` D-46 through D-51 — the canonical decision log entries
- `SPEC.md` D-A22 through D-A27 — the Phase 1 deliverable specifications
- `INSIGHTS-ENGINE-ARCHITECTURE.md` §3.4, §5.5, §19.10 — the architectural treatment

If those three docs disagree with this one, **they win**. This doc is the rationale archive, not the canon.

---

## 2. The analytical discipline applied

For each of the 12 LAVERN patterns, three questions were asked honestly:

1. **Is there a real gap in what we have today?** If our current design already covers the concern (whether explicitly or implicitly), the LAVERN pattern is a tidier framing, not an improvement. Don't adopt for the sake of adoption.

2. **What specifically would change in the codebase or architecture?** Not in prose — in deliverables. Files added, schemas changed, interfaces declared, tests written. If the change can't be specified concretely, the value isn't real.

3. **Does it move Phase 1, or is it Phase 2+?** Phase 1's scope was originally "prove the foundation with mock data." The user clarification on 2026-05-22 — **"build to support real data from the onset"** — re-scored several patterns from "Phase 2+ retrofit" to "Phase 1 must build for inheritance."

The verdicts below treat "build for real data from the onset" as a binding principle.

---

## 3. Pattern-by-pattern verdicts — the 5 adopted in Phase 1

### 3.1 Precedent Board (Pattern #1) — **ADOPT architecture + scaffold; defer lifecycle to Phase 1.5**

#### Current state (before this expansion)

`decisions.md` D-03 declared a three-tier taxonomy: Fact / Observation / Inference. Every Inference re-derived patterns from raw Observations on each query — no persistent "this pattern has been observed N times and ratified by SMEs" layer.

#### Is there a real gap?

**Yes — meaningful gap.** Two specific problems:

1. **Cost + quality**: 200 users asking the same question (e.g., "what's our typical earnout settlement %") trigger 200 separate syntheses over raw Observations. There's no caching of the *pattern*, only the response.
2. **No SME confirmation path**: When the AI surfaces a pattern that an SME knows is institutionally true, there's no architectural mechanism to ratify it as a firm-level rule. The use-case doc's **Mode 4 (Precedent curation)** and **Mode 6 (weekly briefing of Confirmed Precedents)** literally don't exist without Precedent Board.

#### Concrete change

Phase 1 deliverables D-A26 + D-A27 (decisions.md D-46):

- `InsightArtifact` envelope supports `type: "precedent"` natively (envelope schema + C# types)
- `sprk_precedent` Dataverse entity + relationship tables `sprk_precedent_observation` (N:N) + `sprk_precedent_related` (N:N self) provisioned via PAC CLI
- `IPrecedentBoard` C# interface in `Sprk.Bff.Api/Services/Insights/Precedents/` with stub `DataversePrecedentBoard` implementation
- Graph schema extension: `Precedent` vertex type + `OBSERVATION_SUPPORTS_PRECEDENT` + `PRECEDENT_RELATED_TO_PRECEDENT` Cosmos edges
- AI Search `insight-precedents` embedding index (deployed via Bicep)
- Agent tools: `ISearchPrecedentsTool`, `ICitePrecedentTool` (stubs)
- Admin endpoint `POST /api/insights/admin/precedents` for manual create/confirm/deprecate

Phase 1 acceptance: manually-created Confirmed Precedent retrieved by `ISearchPrecedentsTool` and cited by Insights Agent in an Inference response with `precedent://...` provenance link.

Phase 1.5 deliverables (NOT Phase 1):

- `PrecedentDecayJob` (daily decay of `effectivenessScore`)
- `PrecedentPromotionJob` (consolidation pass: Tentative → Confirmed when threshold + outcomes)
- Drift detection (`negativeOutcomes ≥ 2` → UnderDriftReview)
- Hybrid retrieval dedup (vector + BM25, NOT lavern's SHA-256)
- Curator consolidation logic
- SME review queue UI (DEF-13 — surface choice deferred to real customer input)
- Inference layer updated to cite Precedents directly instead of re-deriving

#### Phase 1 vs Phase 2+ decision

The architecture+scaffold split is the disciplined middle ground (decisions.md D-46 rationale). Three risks made full Phase 1 implementation untenable:

1. **Calibration on no data**: `CONFIRM_THRESHOLD = 5`, decay `*= 0.95`, drift threshold `≥ 2` — all calibrated against some notion of data volume we haven't observed. Real Observations are needed to tune thresholds.
2. **Product unknowns in SME workflow**: Mode 4 requires actual SMEs. Daily? Weekly? Per-Precedent confirmation criteria? Modification vs reject? These are *product* questions that need customer input.
3. **Cross-project coupling**: LAVERN ADR 10.1 is proposed in `ai-advanced-capabilities-development`, not yet ratified. Coupling Phase 1 to that ratification timeline slows both projects.

But the architecture work is cheap to do now and expensive to retrofit later — hence "scaffold in Phase 1, lifecycle in Phase 1.5."

#### What we lose if we don't adopt

If we ship Phase 1 with 3-tier taxonomy and add Precedent later:

- Mode 4 and Mode 6 from the use-case doc are unbuildable until a substantial schema migration
- Architectural mental model says "3-tier" everywhere; future contributors revisit dozens of code paths
- The system architecturally mirrors generic-AI hedging the marketing explicitly disavows (decisions.md D-04 honesty contract)

### 3.2 Citation verifier — `GroundingVerifier` (Pattern #3) — **ADOPT in Phase 1**

#### Current state

`decisions.md` D-04 declares: *"every Observation/Inference carries `evidence[]`"*. The Insights Agent returns Inferences with citations like `evidence: [{refType: 'comparable-matter', ref: 'matter://M-0567'}]`. The C# type system enforces `Evidence` is non-null; rendering rules enforce surfaces display it.

#### Is there a real gap?

**Yes — fundamental gap.** We never check that a cited reference actually *contains* the claim's quoted text. An LLM could cite M-0567 in support of "settled at 62%" when M-0567 doesn't contain that fact. Our honesty contract is enforced at the *envelope shape* level (does the array exist?) but not at the *truth* level (does the citation actually support the claim?).

The lavern source code (`src/mcp/tools/grounding-verifier.ts`) implements a mechanical zero-LLM check: regex-extract citations and quoted fragments → substring-match against parsed document → fuzzy sliding-window fallback (capped at 10K chars for DoS protection) → maintain "common boilerplate" half-credit list. Catches hallucinated citations for free, in milliseconds.

#### Concrete change

Phase 1 deliverable D-A22 (decisions.md D-47):

- New service `Sprk.Bff.Api/Services/Ai/CitationVerification/GroundingVerifier.cs`
- Runs as post-Agent step in `InsightsResolverService`, before returning `InsightResponse`
- Failed citations either stripped or annotated `[citation could not be verified]` (configurable; default annotate)
- Platform primitive — also exposed for Action Engine R2 consumption

#### What we lose if we don't adopt

We ship Phase 1 knowing the honesty contract is principle-not-enforced. The first customer query that surfaces a hallucinated citation reveals the gap — and customers will not have evidence that any other citation is verified.

#### Complementary to architecture-doc D-42

`INSIGHTS-ENGINE-ARCHITECTURE.md` D-42 specifies a rule-based + embedding-similarity *streaming safety verifier* for response-level claim/evidence binding. D-A22 / D-47 is the **mechanical per-citation check**. Both run in the response pipeline; they don't conflict.

### 3.3 Evidence-required runtime guards — `EvidenceGuard.Validate` (Pattern #6) — **ADOPT in Phase 1**

#### Current state

C# types declare `record InsightArtifact(..., IReadOnlyList<EvidenceRef> Evidence)`. The type system enforces *shape* (the property exists, returns a list), not *non-empty contents*.

#### Is there a real gap?

**Yes — small but real.** Tests, direct callers, or future code constructing payloads programmatically could pass `Evidence: []` (empty list) and the system accepts it. Schema validation is necessary but not sufficient for legally load-bearing invariants ("every Inference cites ≥1 supporting Observation").

The lavern source (`src/mcp/tools/debate-board.ts`) requires `evidence: string[]` `min(1)` in Zod **and** re-checks at the handler. Belt-and-suspenders.

#### Concrete change

Phase 1 deliverable D-A23 (decisions.md D-48):

- `EvidenceGuard.Validate(result)` static method in `Sprk.Bff.Api/Services/Insights/EvidenceGuard.cs`
- Throws `EvidenceRequiredException` on empty `Evidence`
- Applied to `IFindComparableMattersTool`, `IGetMatterFactsTool`, `IAssessEvidenceSufficiencyTool`, `ISearchPrecedentsTool`
- Trivially cheap (~1 day)

#### What we lose if we don't adopt

A programmatic-construction bug or test code could silently bypass D-04 with no detection. Trivially cheap defense; meaningful coverage.

### 3.4 `IDeclineToFindTool` (Pattern #7) — **ADOPT in Phase 1**

#### Current state

The Insights Agent reasons about whether to decline when `IAssessEvidenceSufficiencyTool` returns insufficient. The decision is an LLM call that *could* be coerced ("I see only 4 comparable matters but let me give you a rough estimate anyway"). `decisions.md` D-06 forbids silent fallback to generic AI — but enforcement is currently the LLM behaving correctly under coercion.

#### Is there a real gap?

**Yes — real coercion path.** The lavern source promotes "decline to find" to a first-class MCP tool (`decline_to_find`) — not a prompt-engineered hope. The Agent must invoke the tool; it can't just write decline prose. Mechanically deterministic.

#### Concrete change

Phase 1 deliverable D-A24 (decisions.md D-49):

- New `IDeclineToFindTool` registered in Insights Agent tool set
- Returns structured `DeclineResponse { Reason, Explanation, MinimumEvidenceNeeded, SuggestedActions, ConfidenceInDecline }`
- Agent invokes it when `IAssessEvidenceSufficiencyTool` returns insufficient
- UI rendering: yellow card (not red error, not green success) — "system declined to find because…"

#### What we lose if we don't adopt

D-06 enforcement remains at the LLM's discretion every call. Adversarial prompts or edge-case sufficiency states could yield free-prose decline (or worse, free-prose conclude-with-hedging).

### 3.5 Ingest sanitization — `ISanitizer` + `Smacl1Sanitizer` (Pattern #10) — **ADOPT in Phase 1**

#### Current state

Phase 1 uses mock data; closure-extraction (Phase 2) will read real SPE documents. There is no canonical sanitization step before LLM ingestion.

#### Is there a real gap?

**Yes — and the user's "build to support real data from the onset" principle (2026-05-22) makes this Phase 1 not Phase 2.** Without sanitization:

- Zero-width Unicode characters (U+200B–U+200F, U+202A–U+202E, U+2060–U+206F, U+FEFF) can carry hidden prompt injections
- HTML comments and ANSI escapes survive document parsing and reach the LLM
- Every AI-facing ingest path is its own attack surface

The lavern source (`src/documents/sanitize-text.ts`) implements SMAC-L1 sanitization: strip these vectors before any LLM sees the doc, with audit log of what was removed.

#### Concrete change

Phase 1 deliverable D-A25 (decisions.md D-50):

- `ISanitizer` interface + `Smacl1Sanitizer` default in `Sprk.Bff.Api/Services/Ai/IngestSanitization/`
- Audit log to App Insights custom events (not Dataverse — lower cost, fits security telemetry)
- Wired into stub closure-extraction ingest entry points in Phase 1 so the Phase 2 real-document path inherits sanitization by default, not as a retrofit
- Platform primitive — Action Engine R2 also consumes when webhook/signal triggers land

#### Phase 1 vs Phase 2 reasoning

If we wait for Phase 2 to introduce sanitization, we wire it into already-built ingestion paths — every consumer is then a candidate for "forgot to sanitize." Phase 1 building means Phase 2 inherits the discipline.

#### What we lose if we don't adopt

Phase 2 real-document ingestion ships with a known prompt-injection surface. Retrofit is harder than building right.

---

## 4. Pattern-by-pattern verdicts — deferred to later phases

### 4.1 EvaluatorGate (Pattern #2) — **Phase 2+ quality upgrade**

Single-model Inference synthesis is acceptable for Phase 1's one question (`predict-matter-cost`) on mock data. EvaluatorGate adds:

- Doubles per-Inference LLM cost (specialist + evaluator)
- Requires LAVERN Pattern #11 (provider tier abstraction) to enforce model-tier separation
- Real adoption only valuable when AI-heavy / high-stakes Actions ship (likely R2 Action Engine concern)

Verdict: revisit when a concrete scenario emerges that single-model synthesis demonstrably underperforms.

### 4.2 Phase deny-tools (Pattern #8) — **JPS concern, not Insights**

Closure-extraction is a simple JPS playbook (one DeliverToIndexNode at the end). It doesn't have multi-phase orchestration that needs deny-tools enforcement. This pattern is primarily for Action Engine's `authoring → schedule → execute → approve → deliver` phases — where the Builder Agent's probabilistic dispatch needs mechanical guardrails.

Verdict: cross-reference in Action Engine project; not Insights' concern.

### 4.3 Seed data CUAD/MAUD (Pattern #9) — **Action Engine concern (RedFlagDetector)**

Insights Engine retrieves from the firm's own Observations — not from industry-baseline corpora. CUAD's 41 commercial-contract clause types and MAUD's M&A deal points feed Action Engine's RedFlagDetector / clause-classification tools, not the Insights synthesis layer.

Verdict: Action Engine MVP if RedFlagDetector ships; otherwise Action Engine R2. Cross-reference but not Insights Engine work.

### 4.4 Provider tier abstraction (Pattern #11) — **Platform concern, Insights stays on hardcoded D-08**

`decisions.md` D-08 commits to `text-embedding-3-large` (3072 dim) intentionally. Tier abstraction makes embedding-model swaps *easier* — but we don't *want* easy swaps (re-indexing 10M artifacts is expensive; D-08 / DEF-02 explicitly notes this).

Insights stays on hardcoded D-08. Tier abstraction is primarily an Action Engine + JPS scope concern (enables Pattern #2 EvaluatorGate in R2+).

### 4.5 Tabulate workflow (Pattern #12) — **JPS playbook library entry, not Insights**

The `tabulate` workflow is a playbook composition pattern that returns row-set output. Some Action Engine starter templates are tabulation-style (cross-matter rollup, invoice approval queue summary). Not relevant to Insights Engine's Q&A pattern.

Verdict: Action Engine optional MVP addition (~1–2 days).

---

## 5. Pattern-by-pattern verdicts — not Insights Engine's scope

### 5.1 Flow UI (Pattern #4) — Surface concern

The Insights Engine emits SSE events and `InsightResponse` payloads; surfaces render. The `PlaybookExecutionFlow` shared component lives in `Spaarke.UI.Components` — it's consumed by all surfaces (workspace pane, code pages, PCF controls) but not part of the Engine.

Verdict: Spaarke shared UI library concern; Action Engine R2 deliverable.

### 5.2 GateResolver (Pattern #5) — Built by Action Engine, consumed by Insights Phase 2+

Phase 1 Insights is read-only (`POST /api/insights/ask` returns `InsightResponse`; no writes back). GateResolver is the right primitive when Phase 2+ adds write-back paths (e.g., "save this Inference to `insight-sessions`"). Action Engine MVP builds the primitive (LAVERN ADR 10.3); Insights consumes when needed.

Cross-reference: `decisions.md` D-51 (Insights consumption decision); coordination assessment §4.6 (joint).

---

## 6. Cross-project coordination implications

The user emphasized (2026-05-22): *"be sure that we keep these aligned and coordinated."* Specific cross-project concerns:

### 6.1 Shared platform primitives — Insights builds, Action Engine consumes

| Primitive | Built in | Consumed by | LAVERN ADR |
|---|---|---|---|
| `ISanitizer` + `Smacl1Sanitizer` | Insights Phase 1 (D-A25, D-50) | Action Engine R2 (webhook/signal trigger ingestion) | 10.6 |
| `GroundingVerifier` | Insights Phase 1 (D-A22, D-47) | Action Engine R2 (AI Tools that return findings) | 10.6 |

Insights Engine places both in `Sprk.Bff.Api/Services/Ai/` (platform layer), not under `Services/Insights/` — so Action Engine can DI-inject them without reaching into Insights internals.

### 6.2 Shared primitives — Action Engine builds, Insights consumes

| Primitive | Built in | Consumed by | LAVERN ADR |
|---|---|---|---|
| `IGateResolver` + 4 implementations | Action Engine MVP | Insights Phase 2+ write-back paths (D-51); Self-Service Registration; Email Wizard; future approval surfaces | 10.3 |

Insights Engine's `INSIGHTS-ENGINE-ARCHITECTURE.md` §8.4 originally said "for write-back operations, extend existing `PendingPlanManager`." That plan is **superseded** by GateResolver consumption (decisions.md D-51).

### 6.3 Shared schemas — joint workstream

`ToolHandlerMetadata` extension is in the existing coordination assessment §4.5 and is extended by LAVERN per §4.8 (new). Fields combined:

| Field | Source | Purpose |
|---|---|---|
| `Classification` | Coordination §4.5 | `Deterministic | AI | Hybrid` |
| `CostClass` | Coordination §4.5 | `Free | Cheap | Expensive` |
| `LatencyClass` | Coordination §4.5 | `Sub100ms | Sub1s | Sub10s | LongRunning` |
| `Idempotency` | Coordination §4.5 | `Idempotent | NotIdempotent` |
| `AuthMode` | Coordination §4.5 | `Obo | AppOnly | None` |
| `Discoverability` | Coordination §4.5 | `{ keywords, semanticDescription, sampleInvocations }` |
| **`ModelTier`** | LAVERN Pattern #11 / §4.8 | `Premium | Standard | Fast | Embedding` — enables EvaluatorGate (Action Engine R2+) |
| **`PhaseRestrictions`** | LAVERN Pattern #8 / §4.8 | Array of phase names where the tool MUST NOT dispatch — Action Engine MVP |
| **`EvidenceRequired`** | LAVERN Pattern #6 / §4.8 | bool — Insights D-A23 runtime guard |

Both projects use the extended schema; joint workstream (~3–4 days).

### 6.4 Signal envelope contract extension (coordination assessment §4.2)

Existing §4.2 specifies `InsightArtifact` (Fact / Observation / Inference) as the canonical signal payload for Action Engine Monitors. With Precedent layer adoption, the envelope now supports `type: "precedent"` — Action Engine Monitors can filter on `producedBy.id` for Precedent-derived triggers (e.g., "Action fires when a Precedent enters drift review").

### 6.5 Joint ADR ratification dependencies

The LAVERN ADR proposals (10.1, 10.3, 10.4, 10.6) live in `ai-advanced-capabilities-development` and are not yet ratified. Insights Engine and Action Engine both depend on them:

| LAVERN ADR | Insights Engine impact | Action Engine impact |
|---|---|---|
| 10.1 — Precedent Board | D-A26 design freeze (Phase 1) | R2+ consumer (AI Tools cite Precedents) |
| 10.3 — GateResolver | D-51 reference; Phase 2+ consumption | MVP implementer |
| 10.4 — Provider tier abstraction | No Phase 1 change; awareness only | R2 EvaluatorGate dependency |
| 10.6 — Sanitization + Citation Verification Standard | D-A22 + D-A25 Phase 1 build | R2 consumer |

Both projects should be reviewers on each LAVERN ADR before ratification.

---

## 7. The decision basis — "build for real from the onset"

The user's clarification on 2026-05-22 — *"while yes phase 1 uses mock data, we need to build to support real data from the onset"* — was the load-bearing argument that moved several patterns from "Phase 2 retrofit" to "Phase 1 must build for inheritance."

### 7.1 Pre-production timing means architectural integrity is cheap

No customers, no schemas in production, no data to migrate. Engineering time only. Cost of building "3-tier with room for 4th" is higher than cost of building "4-tier from day one" because branches and assumptions accumulate.

### 7.2 Precedent layer changes the conceptual model, not just storage

If Precedent is genuinely a 4th tier:
- C# envelope shape supports it natively
- Cosmos graph schema accommodates Precedent vertices + edges
- AI Search index family includes `insight-precedents`
- Agent tool set has Precedent-aware tools
- Decision log says 4-tier, not 3

Building those in Phase 1 means Phase 2's implementation is "fill in the lifecycle code" — not "introduce a new tier across the codebase."

### 7.3 Honesty contract becomes mechanical, not aspirational

`GroundingVerifier`, `EvidenceGuard.Validate`, `IDeclineToFindTool`, `ISanitizer` together turn three principle-level decisions (D-04, D-06) into code-enforced guarantees:

| Decision | Before LAVERN | After LAVERN |
|---|---|---|
| D-04 (provenance is API contract) | Envelope shape enforced; truth of citations not checked | `GroundingVerifier` checks each citation; `EvidenceGuard` requires non-empty |
| D-06 (no silent fallback to generic AI) | LLM Agent decides whether to decline | `IDeclineToFindTool` is a deterministic exit path |
| Ingest sanitization | Not in the design | Mandatory at all AI-facing ingest paths |

### 7.4 The user-expectation argument (Mode 4, Mode 6)

`ADVANCED-AI-USE-CASE-PATTERNS.md` lists six modes for Spaarke AI. Modes 4 (Precedent curation) and 6 (weekly briefing) **literally don't exist** without Precedent Board. The marketing positioning ("Spaarke beats black-box AI because institutional knowledge is SME-confirmed and citable") requires the Precedent layer to be real.

If we ship Phase 1 without it, the architecture matches generic-AI hedging — the very thing the marketing claims to differ from.

---

## 8. What we chose NOT to do, and why

### 8.1 Full Precedent Board in Phase 1 (lifecycle automation)

Considered. Rejected because:

- **Calibration risk**: `CONFIRM_THRESHOLD`, decay rate, drift threshold all need real Observations to tune. Picking defaults on no data risks building wrong defaults.
- **Product unknowns**: SME workflow (Mode 4) needs real customer SME input on rhythm and surface choice. Building UI without that input risks rebuilding it.
- **Cross-project coupling**: LAVERN ADR 10.1 is not ratified. Coupling Phase 1 to that timeline slows both projects.

Hence: scaffold in Phase 1, lifecycle in Phase 1.5.

### 8.2 EvaluatorGate in Phase 1

Considered (LAVERN Pattern #2). Rejected because:

- Phase 1 has one question on mock data — quality verification has no signal to operate on
- Doubles per-Inference cost — meaningful operational concern
- Requires LAVERN Pattern #11 (tier abstraction) to enforce model-tier separation — chain of dependencies

Revisit Phase 2+ when high-stakes Inferences ship.

### 8.3 Seed data CUAD/MAUD in Phase 1 (LAVERN Pattern #9)

Not Insights' substrate. Insights retrieves from firm's own Observations; CUAD/MAUD are industry-baseline corpora that feed Action Engine's clause classifiers (RedFlagDetector). Cross-project concern.

### 8.4 Flow UI component (LAVERN Pattern #4)

Surface concern, not Engine concern. The Engine emits SSE events; the shared component lives in `Spaarke.UI.Components` and is consumed by surfaces.

---

## 9. Sequencing

### 9.1 Track A waves with new deliverables interleaved

See `SPEC.md` §8. Summary:

| Wave | Original | + LAVERN | Effort |
|---|---|---|---|
| W1 | D-A1, D-A4 (now 4-tier), D-A5 | — | original + minor |
| W2 | D-A2, D-A3 (5 indexes), D-A13 | — | original + minor |
| W2.5 (new) | — | **D-A25** (sanitizer) | 2–3 days |
| W3 | D-A6 (Precedent vertex/edges), D-A7 | — | original + minor |
| W3.5 (new) | — | **D-A26** (Precedent scaffold) | 9–10 days |
| W4 | D-A8 (Precedent-aware resolver) | — | minor |
| W4.5 (new) | — | **D-A22, D-A23, D-A24** | 4–7 days |
| W5 | D-A9, D-A10 (Precedent tool stubs) | — | minor |
| W6 | D-A11 | — | original |
| W6.5 (new) | — | **D-A27** (Precedent admin endpoint) | 1 day |
| W7 | D-A12, D-A14 + Precedent smoke test | — | minor |

Net Phase 1 expansion: **~16–21 days** (~3–4 weeks of additional work).

### 9.2 Phase 1.5 lifecycle automation roadmap

Triggered after Phase 1 Track A lands. Builds Precedent Board lifecycle automation, drift detection, SME review queue. Estimated ~10–15 days; thresholds calibrated against Track B real data when available.

### 9.3 LAVERN ADR ratification dependencies

Insights Engine work proceeds in parallel with `ai-advanced-capabilities-development`'s ADR ratification process. Hard dependencies:

- **D-A26 design freeze**: blocked on LAVERN ADR 10.1 ratification
- **D-A22 + D-A25 design freeze**: blocked on LAVERN ADR 10.6 ratification

Hard dependencies are documented in `decisions.md` LAVERN coordination items table.

---

## 10. References

- **Source patterns**: [`projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md`](../ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md)
- **Use-case modes**: [`projects/ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md`](../ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md)
- **Canonical decisions**: [`decisions.md`](decisions.md) D-46 to D-51
- **Deliverable specs**: [`SPEC.md`](SPEC.md) D-A22 to D-A27
- **Architectural treatment**: [`INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §3.4 (Precedent layer), §5.5 (Platform primitives), §19.10 (LAVERN decisions), §20 (Constraints), §21.3 (Phase 1.5), §21.7 (LAVERN coordination)
- **Sister project**: [`projects/ai-spaarke-action-engine-r1/`](../ai-spaarke-action-engine-r1/)
- **Joint coordination**: [`projects/ai-spaarke-action-engine-r1/coordination-assessment-with-insights-engine.md`](../ai-spaarke-action-engine-r1/coordination-assessment-with-insights-engine.md) §4.1–§4.8 (with new §4.6, §4.7, §4.8 for LAVERN concerns)
- **LAVERN source repo (Apache 2.0)**: [AnttiHero/lavern](https://github.com/AnttiHero/lavern) — preserved per LAVERN doc §7 vaulting strategy
- **Companion Action Engine assessment**: [`projects/ai-spaarke-action-engine-r1/lavern-pattern-assessment.md`](../ai-spaarke-action-engine-r1/lavern-pattern-assessment.md) — same lens applied to Action Engine

---

*This document is the rationale archive. The canon lives in `decisions.md`, `SPEC.md`, and `INSIGHTS-ENGINE-ARCHITECTURE.md`. When they disagree with this doc, they win.*
