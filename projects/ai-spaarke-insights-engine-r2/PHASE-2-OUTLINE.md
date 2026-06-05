# Spaarke Insights Engine — Phase 2 (r3) Priority Outline

> **Authored**: 2026-06-04 (task 090 wrap-up of r2)
> **Predecessor**: [`ai-spaarke-insights-engine-r2`](.) — Phase 1.5; 5 PRs shipped (#330, #334, #336, #337, #339); 14/15 spec.md SCs met
> **Successor**: `ai-spaarke-insights-engine-r3` (Phase 2) — to be created post-owner discussion using this outline as primary input
> **Companion**: [`notes/lessons-learned.md`](notes/lessons-learned.md)

This document is the **primary input to the r3 `design.md` focus-area discussion**. It does not commit r3 scope. Instead, it organizes the known Phase 2 candidates into priority tiers so that the owner can pick r3 wave 1 from Tier 1, wave 2 from Tier 2, etc., based on engineering capacity + business priority at r3 start.

The outline draws from: r2 lessons-learned (§2 anti-patterns + §3 carry-forwards), spec.md Out-of-Scope (Phase 2 items), spec.md Unresolved Questions still open, design.md re-deferred items (Cosmos D-P17), task 090 POML inputs (customer playbook authoring UI, field auto-populations, MCP server contract, embedding-based intent classifier), and the R5 ↔ r2 coordination doc deferrals.

---

## Tier 1 — Architectural cleanup (close r2 debt)

**Recommended r3 wave 1**. Each item closes a known issue or technical debt opened in r2. Cost is small; each unblocks downstream waves. Ship these before adding new capability.

### 1.1 `NullInsightsAi` facade (close asymmetric registration on `IInsightsAi`)

`adr-check` during r2 Wave E flagged that `IInsightsAi` is registered unconditionally in `Sprk.Bff.Api.Infrastructure.DI.InsightsServiceCollectionExtensions`, but transitively depends on services registered only when `Insights:Engine:Enabled=true` AND `Ai:CompoundAi:Enabled=true`. When compound-AI is disabled, callers hit DI resolution failures — violating ADR-032 Null-Object Kill-Switch P3. Wave F deferred to keep scope tight. The fix mirrors the existing `NullRagService` pattern (`src/server/api/Sprk.Bff.Api/Services/Ai/NullRagService.cs`): create `NullInsightsAi : IInsightsAi` whose every method throws `FeatureDisabledException("ai.insights.disabled")`. Register it in the compound-AI-OFF branch of `AnalysisServicesModule.cs`. Endpoints get 503 ProblemDetails via the existing `FeatureDisabledResults.AsFeatureDisabled503()` extension — same UX as `IRagService` already provides. Estimate: **0.5 day** (1 file + 1 DI change + 4 unit tests + 1 integration test). Technical anchors: `Configuration/FeatureDisabledException.cs`, `Configuration/FeatureDisabledResults.cs`, `Services/Ai/NullRagService.cs` (model). ADR-032. Related: `.claude/constraints/bff-extensions.md` §F.1 anti-pattern check (which is exactly what flagged this).

### 1.2 v1.2 contract: `spe://drive/X/item/Y` evidence-ref href resolution

Wave F (Phase 1.5 v1.1) shipped `citations[].href` for the bare-Guid evidence-ref form. Playbook-path citations using the `spe://drive/X/item/Y` URI form currently emit `href: null`. The F1 spike empirically confirmed `spe://` is the dominant emit form for indexing but minority of *playbook citation surfaces* (most citations are observation-IDs which are sprk_document Guids). Promoting to full coverage requires making the citation projection async via `DataverseObservationMirror.ResolveDocumentIdAsync` pattern. Estimate: **1 day** (helper extraction + async projection change + 6–8 new tests). Technical anchors: `Services/Ai/Insights/AssistantToolCallHandler.cs` (citation projection), `DataverseObservationMirror`, F1 spike §B + §F (Wave F future-work entry). Coordinate with R5 — they may want this for higher href-coverage in the AnalysisChunk surface.

### 1.3 Test-fixture hygiene cleanup — eliminate CI flake class

PRs #337 + #339 each tripped 1–2 CI flakes on Windows runners (timing test ±0.5s tolerance; Post Cache race; `FileSystemWatcher` dispose NRE). The FileSystemWatcher fix (commit `c70bbb7a`) suppressed the symptom; the underlying race condition lives in `IntegrationTestFixture` config-binding code. Two carry-forwards from r2 lessons §3.2: (a) sub-agent quality gates run BOTH unit and integration test projects, not just unit; (b) any new `BindConfiguration` in tests gets explicit review for FileSystemWatcher race exposure. The cleanup task is a targeted audit of `tests/integration/Spe.Integration.Tests/IntegrationTestFixture.cs` + sibling fixtures + a small task-execute skill amendment to require integration-test run in Step 9.5 quality gate. Estimate: **1 day**. Technical anchors: `c70bbb7a` commit (suppression), `c--code-files-spaarke-wt-ai-spaarke-insights-engine-r2` memory `reference-spaarke-ci-format-gate.md` (CI gate pattern).

### 1.4 Telemetry maturity — `InsightsActionLookupFailed` event + dashboards

`InsightsActionRouter` (Wave D4) has 15-min sliding TTL cache + miss-fallthrough behavior but emits no telemetry on lookup failure. When per-area routing misses (NULL `sprk_layer2actioncode` on the intersect row — gate-fail by design, e.g., CTRNS × NDA), the BFF emits a structured decline without surfacing the routing decision to operations. r3 Tier 1 adds: `InsightsActionLookupFailed` event with dimensions `practiceAreaCode`, `documentType`, `playbookId`, `nodeOrder`, plus an Application Insights dashboard tile for daily miss rates. Helps SMEs find the gate-fail subset that needs new Layer 2 schema authoring (Wave D3 expansion). Estimate: **1 day**. Technical anchors: `Services/Ai/Insights/InsightsActionRouter.cs`, `InsightsTelemetryConstants.cs`, infra/dashboards/.

**Tier 1 total**: ~3.5 days. Recommended as r3 wave 1 to clear debt before capability expansion.

---

## Tier 2 — Capability expansion (r3 waves 2+)

Higher business value than Tier 1 but each carries r2-deferred design risk. Owner should pick 1–2 items per wave based on r3's scope ambition.

### 2.1 Bidirectional clarification (HTTP 422 + clarification envelope)

Currently the v1.1 contract returns a structured `DeclineResponse` on insufficient evidence; the Assistant cannot request the BFF to ASK the user a follow-up question. v1.2 adds an HTTP 422 + clarification-envelope shape per the v1.1 §11 Phase 2 deferrals list. This is currently a P2 deferral noted in `design-e3-tool-call-contract.md`. Estimate: **3–4 days**. Touches `IInsightsAi.AssistantQueryAsync` return shape + Assistant-side prompt template; cross-team coordination required.

### 2.2 Full SSE on playbook path (structured-output streaming)

Wave F shipped SSE on the RAG path (token-level `delta` events) and coarse `progress` events on the playbook path. Full SSE on playbook requires streaming structured-output assembly which the F1 spike §D ruled out for v1.1. Phase 2 candidate: explore Azure OpenAI structured-output streaming API + JSON-fragment validation, or alternative pattern (e.g., shimmy partial-JSON via deltas with reconciliation at `result`). Estimate: **1–2 weeks**. High R&D risk.

### 2.3 `playbookHint` Assistant-supplied field

Allow the Assistant to pass a routing hint (e.g., "user explicitly asked predict-matter-cost; bias the classifier"). Currently `forceMode` is a binary `playbook | rag | null`. `playbookHint` would be a soft signal that the classifier weights but can override. Estimate: **2–3 days**. Touches `InsightsIntentClassifier` + contract.

### 2.4 Actionable citations (`citations[].action: { type, payload }`)

v1.1's `citations[].href` is display-only. v1.2 candidate: `citations[].action` carries a structured payload the UI can invoke (e.g., `{type: "open-document", payload: {sprk_documentid, page}}` or `{type: "create-task", payload: {...}}`). Owner direction will determine action vocabulary. Estimate: **1–2 weeks** depending on action vocabulary breadth.

### 2.5 Embedding-based intent classifier

Wave E2 shipped an LLM-based classifier (gpt-4o-mini, JSON-schema-constrained, IMemoryCache 15-min sliding TTL). Phase 2 replaces with embedding-based routing for lower latency + cost. Spec.md "Out of Scope" line item; design-eligible now that Wave E proved the classifier-as-routing-component shape. Estimate: **1 week**. Touches `Services/Ai/Insights/InsightsIntentClassifier.cs` + `IIntentClassifier` abstraction.

---

## Tier 3 — Surface area expansion

Less mature; each is a research/scope exploration before estimation.

### 3.1 Multi-area document handling (1 doc → N practice areas)

Today: `sprk_matter.sprk_practicearea` drives ingest routing — 1 matter, 1 area. Reality: some documents (e.g., a corporate ML&A closing doc on a hybrid CTRNS+MA matter) span areas. r3 needs to decide: (a) hard-routing on matter; (b) document-level classification overrides matter; (c) multi-label classification at Layer 1.

### 3.2 Subject scheme expansion beyond matter/project/invoice

Wave D5 introduced `IDictionary<string, ILiveFactResolver>` keyed by scheme. Adding `contract:`, `lead:`, `expense:`, etc. is well-supported but each new resolver is real engineering effort. Phase 2 scopes the next 2–3 entity surfaces by SME + product priority.

### 3.3 Multi-turn conversation state persisted on BFF

Today: each `/api/insights/assistant/query` is stateless. Multi-turn conversation state (e.g., "follow up on the matter you just analyzed") requires either Assistant-side state passthrough or BFF-side conversation persistence. Wave E3 handoff doc raises this.

### 3.4 Cross-tenant federation (Assistant queries Insights across tenants)

Currently single-tenant per deployment. Cross-tenant requires per-query tenant resolution + ACL enforcement at the index level. High security review burden.

---

## Tier 4 — Long-term

Carried-forward from r1, r2, and task 090 POML inputs. Each is a real Phase 2/3 candidate but lower priority than Tier 1/2/3.

### 4.1 Cosmos NoSQL graph (originally D-P17 in r1)

r1 design promised D-P17 as first Phase 1.5 deliverable. r2 design.md re-deferred with rationale: Wave D + E priorities deliver more user-visible value than substrate change. `IInsightGraph` stub remains in code. Phase 2 re-evaluates Cosmos vs alternatives (Postgres+pgvector, dedicated graph DB).

### 4.2 Per-tenant prompt overrides

Wave A4 design exists (`design-a4-prompt-versioning.md`); not implemented. Pattern is well-specified: tenant-scoped variant action rows with fallthrough query. Cost is mostly Dataverse schema + ingest routing extension.

### 4.3 Customer playbook authoring UI (SME builder)

Mentioned in task 090 POML inputs. Phase 2/3 candidate per spec.md Out-of-Scope. Substantial UI + Dataverse-form work; SME UX research required.

### 4.4 Field auto-populations (Dataverse triggers → `/api/insights/ask` → field render)

Mentioned in task 090 POML inputs. Phase 2 per spec.md Out-of-Scope. Requires production cache latency baseline (D-P13 design); risk surfaced as Phase 2-only in spec.md Risks table.

### 4.5 MCP server contract

Mentioned in task 090 POML inputs. Phase 2 per spec.md Out-of-Scope. Would expose Insights as an MCP server callable from Claude Code / similar agents. Cross-team coordination + MCP tool-handler design.

### 4.6 SME-facing prompt iteration UI

Phase 2 per spec.md Out-of-Scope. Wave C2 storage shape is now stable enough to build on. Requires UX research with SMEs.

### 4.7 AI-directed playbook authoring (natural language → drafted playbook)

Phase 3 per spec.md Out-of-Scope. Substantial research surface; likely 6+ months.

### 4.8 Per-tenant monthly cost caps with hard enforcement

Phase 1 design left observability-only; spec.md says hard caps are out of scope. Phase 2/3 candidate when telemetry maturity from Tier 1.4 lands.

### 4.9 Multi-tenant onboarding workflow

Spec.md says single-tenant continues per r1 D-52. Phase 2/3 when business priority warrants.

---

## How r3 should consume this

The r3 `design.md` should:

1. **Pick Tier 1 as r3 wave 1**. Closing r2 debt before adding capability is the lowest-risk shape. All four items are small + well-anchored.
2. **Pick 1–2 Tier 2 items for r3 waves 2+** based on owner discussion. The five Tier 2 items range from 3 days to 2 weeks; pace r3 to ship in 4–6 weeks.
3. **Use Tier 3 + 4 as the "in-scope-if-time" list**. These are explicitly NOT recommended for r3 wave-1 commitment.
4. **Re-evaluate Tier 4 quarterly**. Some Tier 4 items (esp. 4.4 field auto-populations + 4.5 MCP server) may rise as business priority changes; this outline does not lock them in.

For each picked item, the r3 design.md spec section should:
- Cite the technical anchors listed here (file paths + class names)
- Pick up the spec.md Unresolved Questions still open from r2 (Q-A4-1, Q-A5-1, Q-A6-1, Q-D6-1, Q-E2-1, Q-WB-1, Q-AC-1 — see r2 spec.md §"Unresolved Questions")
- Reference r2 lessons-learned §3 carry-forwards as default conventions
- Stand up a coordination doc with R5 (or other concurrent projects) if any picked item touches the `/api/insights/assistant/query` contract — the producer/consumer pattern from r2 lessons §1.5 is the model

---

## Open r2 spec.md questions to carry forward

These are unresolved at r2 close. r3 design.md should triage each:

- **Q-A4-1** — Phase 1.5 prompt-variant + versioning + per-tenant-override pattern. Partially resolved in Wave A4 design (variant rows pattern selected); tenant-override mechanism still open.
- **Q-A5-1** — Universal-ingest node count (5–8 nodes). Resolved at 8 nodes in Wave C1; r3 may consolidate after live behavior review.
- **Q-A6-1** — Per-entity resolver registration pattern. Resolved as `IDictionary<string, ILiveFactResolver>` in Wave D5; r3 may revisit if subject schemes proliferate (Tier 3.2).
- **Q-D6-1** — Index scope shape. Resolved with hybrid backward-compat in Wave D6.
- **Q-E2-1** — Intent classifier threshold tuning + caller override. Resolved with confidence 0.7 default + `forceMode` field in Wave E2. r3 Tier 2.5 (embedding-based) supersedes.
- **Q-AC-1** — Assistant tool-call schema shape. Resolved in Wave E3 contract authoring → Wave F v1.1 amendment. Assistant-side implementation owner-deferred.

---

*This outline is the primary forward input to r3. It is intentionally exhaustive on Tier 1 and concise on Tier 3 + 4 — Tier 1 is the recommended near-term commitment surface, while Tier 3 + 4 await owner direction before scoping investment.*
