# Coordination Assessment — Insights Engine r1 + Action Engine r1

> **Output type**: Architectural assessment (not implementation). No code changes proposed in this plan; it identifies coordination requirements, contract surfaces, and decisions that must be jointly made before either project hits `/project-pipeline`.
> **Documents reviewed**:
> - `projects/ai-spaarke-insights-engine-r1/SPEC.md` (Phase 1, pipeline-ready, 2026-05-19)
> - `projects/ai-spaarke-insights-engine-r1/decisions.md`, `design.md`, `ai-inventory.md`, `azure-inventory.md`
> - `projects/ai-spaarke-action-engine-r1/action-engine-overview.md` (design overview, 2026-05-20)
> - `.claude/constraints/bff-extensions.md`, `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`, ADR-001/008/010/013

---

## 1. Context — Why this assessment exists

These two projects appear at first glance to be related ("AI" in both names, both adding to the BFF) but are deliberately separate concerns:

- **Insights Engine** is a **back-end signal producer + query API**: it senses (sync from Dataverse → AI Search + Cosmos graph) and synthesizes (`InsightsResolverService` + `Insights Agent` with evidence-sufficiency rules), exposing `POST /api/insights/ask`. Its primary output is structured `InsightArtifact` envelopes (Fact / Observation / **Precedent** / Inference — 4-tier taxonomy per Insights `decisions.md` D-03 + D-46; LAVERN ADR 10.1) and, on Track B, signals on Service Bus.

- **Action Engine** is a **user-facing agent creation/management surface**: it lets users author Actions (manual, scheduled, signal-triggered, event-triggered) that execute Playbooks composed of Tools (deterministic + AI as peers). It is *not* an extension of the Insights Engine — Insights signals are one of several trigger sources.

The risk in launching them as independent projects is divergence on the seven shared substrates listed in §3. The opportunity is that each layers cleanly above existing Spaarke infrastructure — neither needs to invent new orchestration. The job of this assessment is to make the shared contracts explicit so the projects can proceed in parallel without colliding.

---

## 2. One-paragraph assessment

**The two projects are correctly framed as separate concerns and largely compose cleanly: Insights produces signals/answers, Action consumes them as one trigger source.** Both layer over the same existing BFF substrate — `PlaybookExecutionEngine`, `IToolHandlerRegistry`, `ServiceBusJobProcessor`, `IChatClient` + tool framework, the existing Function App pattern — and neither should introduce new orchestration runtimes. The five coordination risks are: (a) **Playbook entity model collision** — Action Engine §12 explicitly asks whether the new Action/Template/Instance layer subsumes or sits above today's JPS playbook entity, and Insights Engine depends on today's playbook engine for closure-extraction; this must be answered jointly before Action Engine MVP. (b) **Signal envelope contract** — Insights's `InsightArtifact` (Fact/Observation/Inference + provenance) must become the input shape for Action Engine Monitors; defining this once prevents two divergent signal models. (c) **Runtime topology** — Insights has chosen BFF + Functions (Hybrid Option D); Action Engine §6 is open; Action Engine should adopt the same hybrid rather than introduce a third runtime. (d) **BFF additions governance** — both projects must execute the `bff-extensions.md` checklist (placement justification, publish-size delta, PublicContracts facade for any CRUD→AI dependency, no new HIGH-CVEs); the 2026-05-19 publish-size jump from 65→75 MB and the 20 existing CRUD→AI violations are the binding evidence base. (e) **Tool Registry stewardship** — Action Engine names Tool Registry "the strategic engineering asset"; Insights adds three handlers (`IFindComparableMattersTool`, `IGetMatterFactsTool`, `IAssessEvidenceSufficiencyTool`); these need a shared classification taxonomy (deterministic vs AI, cost class, idempotency) before either project ships tools.

---

## 3. Shared substrate — what both projects MUST extend, not duplicate

| Substrate | File / location | Insights usage | Action usage |
|---|---|---|---|
| Playbook execution | `Sprk.Bff.Api/Services/Ai/Playbooks/PlaybookExecutionEngine.cs`, `INodeExecutor` registry, `DeliverToIndexNodeExecutor` | Closure-extraction is a JPS playbook ending in `DeliverToIndex` (Insights design.md §6.4, decisions.md D-21) | Every Action runs a Playbook; single-step Actions = single-step playbooks (Action overview §4) |
| Tool handler registry | `Sprk.Bff.Api/Services/Ai/IToolHandlerRegistry.cs`, `ToolHandlerRegistry.cs`, `ToolFrameworkExtensions.AddToolHandler<T>()` | Registers 3 Insights tools (D-A9) | Adds many deterministic + AI tool handlers (Action overview §5 Tool Registry) |
| Chat / agent host | `Services/Ai/Chat/SprkChatAgentFactory.cs`, `ChatSessionManager`, `IChatClient` + `UseFunctionInvocation` | Insights Agent extends IChatClient (Insights SPEC D-A9) | Builder Agent extends existing AI Builder Agent (Action overview §7.1) |
| Background async work | `Services/Ai/PlaybookEmbedding/PlaybookIndexingBackgroundService.cs` (channel-driven, factory-instantiated, ADR-010 compliant `Instance` accessor) | Template for any Insights BFF-hosted async work | Template for Action Engine in-process scheduler if Option A/D chosen |
| Service Bus job dispatch | `Services/Jobs/ServiceBusJobProcessor.cs` (ADR-004 Job Contract) | Track B sync workers (D-B3, D-B4) | Signal-triggered Monitors (Action overview §9) |
| Dataverse webhook intake | `Api/Ai/CapabilityEndpoints.cs` (shared-secret header, RequireRateLimiting, dispatcher pattern) | Track B Dataverse webhook (D-B1, currently blocked on auth Phase C) | Event-triggered Monitors (Action overview §9) |
| AI PublicContracts facade | **Designed, not yet implemented** — `Services/Ai/PublicContracts/` per refined ADR-013 (2026-05-20) | Insights resolver consumed BY CRUD via this facade | Action Engine consumed BY CRUD via this facade |
| Shared client libs | `src/client/shared/Spaarke.UI.Components/`, `src/client/shared/Spaarke.Auth/` | (n/a — back-end only) | Action Engine UI (visual builder, workspace pins, approval queue) builds on these |

**Rule for both projects**: do not stand up a parallel registry, scheduler, or dispatcher when one already exists. The "Tool Registry as strategic asset" framing in the Action Engine overview is correct, *and* the registry already exists — Action Engine extends it; it does not replace it.

---

## 4. The five coordination decisions that must be made BEFORE either project pipelines

These are joint decisions. Each one is a real fork; deferring any of them causes rework downstream.

### 4.1 Playbook ↔ Action entity relationship (HIGH risk if deferred)

**Question (raised in Action overview §12)**: Does the Action/Template/Instance entity model subsume today's JPS playbook entity, or sit above it?

**Why it matters now**: Insights Engine Track A `D-A12` produces a closure-extraction *JPS playbook design document*. If Action Engine later subsumes the playbook entity, Insights's design assumptions about playbook identity, versioning, and storage may shift. Conversely, if Action Engine sits *above* playbooks (Actions reference Playbooks by id), Insights is unaffected and the Action Engine's job is purely management-plane.

**Recommendation**: **Action Engine sits above today's JPS playbook entity.** Action Definition → Playbook reference is a foreign key, not a replacement. This:
- Preserves Insights Engine's existing dependency on `PlaybookExecutionEngine`
- Lets closure-extraction be a Playbook that Insights triggers directly *and* (later) a Playbook that an Action Engine "matter-closure-detected" Action can also invoke
- Avoids a v1 migration of every existing playbook into a new entity model
- Action Templates are a *new* abstraction (parameterized Action definitions) — not a replacement for Playbooks

**Decision artifact**: a new ADR titled "Action Engine layering above JPS Playbook" must be authored before Action Engine MVP. Insights Engine SPEC.md should reference it in §6.

### 4.2 Signal envelope contract — `InsightArtifact` IS the signal payload (MEDIUM)

**Question**: When Insights Engine Track B emits a signal on Service Bus, and an Action Engine Monitor matches it, what is the on-the-wire shape?

**Why it matters now**: Insights defines `InsightArtifact` (Fact / Observation / Inference + provenance + evidence) in [design.md §2.2](../../code_files/spaarke/projects/ai-spaarke-insights-engine-r1/design.md). Action Engine speaks abstractly of "signal classification" and "signal type filters" in §5/§9 without specifying a payload shape.

**Recommendation**: **`InsightArtifact` (specifically the Observation and Inference variants) becomes the canonical signal payload.** Action Engine Monitors filter on:
- `producedBy.id` / `producedBy.version` (which Insights playbook emitted)
- Artifact type (Observation / Inference)
- Subject (`matter:X`, `client:Y`)
- Threshold predicates against fields in the artifact body

Document this in a joint **`projects/ai-spaarke-shared/signal-envelope-contract.md`** owned by both projects.

### 4.3 Runtime topology — Action Engine adopts the Insights hybrid (HIGH)

**Question (Action overview §6)**: BFF BackgroundService (A) vs Microsoft Agent Framework (B) vs Azure-native scheduler (C) vs Hybrid (D)?

**Why it matters now**: Insights Engine has already chosen Hybrid D (BFF for query API + Functions for sync/extraction/timer per [decisions.md](../../code_files/spaarke/projects/ai-spaarke-insights-engine-r1/decisions.md)). Action Engine SHOULD adopt the same hybrid topology — *one* Function App for cross-project out-of-band work, BFF for synchronous tool dispatch and short-lived agent loops. Introducing a third runtime (e.g., adopting Microsoft Agent Framework as a separate hosted process for Action Engine only) would create two divergent agent execution paths and a new operational seam.

**Recommendation**:
- **Scheduler**: Azure-native Function timer (Option C) — same Function App as Insights Track B
- **Single-step / deterministic Action execution**: BFF (Option A) — direct dispatch through `IToolHandlerRegistry`
- **Multi-step probabilistic agent loops**: BFF (Option A) — extend existing `IChatClient` + `UseFunctionInvocation` + `SprkChatAgentFactory`; do *not* introduce Microsoft Agent Framework as a runtime in Phase 1. Reconsider only if a concrete scenario emerges that the existing orchestrator demonstrably cannot serve.

**Decision artifact**: Action Engine architecture spike (called for in §6) must conclude *before* Action Engine MVP `/project-pipeline`. Output is the runtime ADR. Insights team must be a reviewer on that ADR.

### 4.4 BFF additions governance — both projects file Placement Justification (BINDING)

Per [`.claude/constraints/bff-extensions.md`](.claude/constraints/bff-extensions.md) and CLAUDE.md §10, **every project that adds code to the BFF MUST have a `design.md` section titled "Placement Justification"** answering the decision criteria for each major component, and MUST verify publish-size delta before merge.

**Insights Engine status**: design.md is comprehensive but does not yet contain a section literally titled "Placement Justification" with the four ADR-013 exception criteria mapped per component. **This must be added before `/project-pipeline` runs.**

**Action Engine status**: action-engine-overview.md §6 explicitly defers runtime decisions to an architecture spike. **The spike's output must be a Placement Justification table for each major component** (scheduler, agent loop, tool dispatch, run record, builder agent, monitor dispatch).

**Both projects MUST**:
1. Use `Services/Ai/PublicContracts/` facade for any CRUD-side consumption (when it lands as part of the BFF remediation project; if it lands *after* Insights Phase 1, Insights must add facade types alongside its own services so other CRUD code never reaches inside `Services/Insights/`).
2. Run `dotnet publish --runtime linux-x64` locally and report delta in PR description. Baseline is ~60 MB compressed; 2026-05-19 already pushed it to 75 MB.
3. Run `dotnet list package --vulnerable --include-transitive` and verify no new HIGH-severity CVEs.
4. Register via focused `Add{Feature}Module()` extension methods (ADR-010), ≤15 unconditional registrations per module.

### 4.5 Tool Registry stewardship — shared classification taxonomy (MEDIUM)

**Question**: The Action Engine overview correctly identifies Tool Registry as the strategic asset. But today's `IAnalysisToolHandler` metadata is minimal. To support Action Engine's Builder Agent (which semantically searches the registry to match user intent to tools) and to support Action Engine's "deterministic + AI as peers" principle (which requires the registry to declare classification per handler), the registry's handler metadata schema needs to be extended.

**Recommendation**: A short joint workstream (1-2 tasks, ~3 days) before either project pipelines, to extend `ToolHandlerMetadata` with:
- `Classification`: `Deterministic | AI | Hybrid`
- `CostClass`: `Free | Cheap | Expensive`
- `LatencyClass`: `Sub100ms | Sub1s | Sub10s | LongRunning`
- `Idempotency`: `Idempotent | NotIdempotent`
- `AuthMode`: `Obo | AppOnly | None`
- `Discoverability`: `{ keywords, semanticDescription, sampleInvocations }` (for Builder Agent semantic search)

Insights's three tools (D-A9) populate these fields when registered; Action Engine's seed deterministic tools populate them; both projects use the same metadata contract.

### 4.6 GateResolver as canonical Spaarke approval primitive (HIGH) — LAVERN ADR 10.3

**Question**: When write-back paths land in Insights Engine (Phase 2+) and approval flows ship in Action Engine MVP, what's the shared interface?

**Recommendation**: Action Engine MVP **builds** `IGateResolver` per LAVERN ADR 10.3 in a location reachable from both projects (`Sprk.Bff.Api/Services/Ai/Gates/` is the proposed home; `Spaarke.Core/Gates/` is the alternative if `Spaarke.Core` library is preferred for cross-cutting primitives). Insights Engine **consumes** when Phase 2+ write-back paths land (per Insights `decisions.md` D-51). Self-Service Registration and Email Wizard adopt over time.

Four implementations: `DataversePrecedentBoardGateResolver`, `InteractiveGateResolver`, `WebhookGateResolver`, `AutoApproveGateResolver`. Five gate types: `EthicsCritical`, `MeaningCritical`, `FinalDelivery`, `EngagementAcceptance`, `TeamSelection`, plus `Custom`. Default timeout 5 minutes → auto-reject.

**Decision artifact**: LAVERN ADR 10.3 ratified jointly with Insights Engine before Action Engine MVP `/project-pipeline`.

**Replaces**: Insights Engine `design.md` §8.4 "extends existing PendingPlanManager" plan is superseded by GateResolver consumption.

### 4.7 Shared platform primitives — Insights builds, Action consumes (MEDIUM) — LAVERN ADR 10.6

Two LAVERN-derived primitives live in `Sprk.Bff.Api/Services/Ai/` (platform layer) and are shared:

| Primitive | Built in (Insights Phase 1) | Consumed by (Action Engine R2) | LAVERN ADR |
|---|---|---|---|
| `ISanitizer` + `Smacl1Sanitizer` | D-A25 / D-50 in Insights | Webhook + signal trigger ingestion paths | 10.6 |
| `GroundingVerifier` | D-A22 / D-47 in Insights | AI Tools that return findings (RedFlagDetector, draft tools with citations) | 10.6 |

**Recommendation**: Insights places both in `Sprk.Bff.Api/Services/Ai/IngestSanitization/` and `Services/Ai/CitationVerification/` respectively. Both register via DI for cross-project reuse. Both surface in shared library exports for `Spaarke.Core` consumers if/when needed.

**Decision artifact**: LAVERN ADR 10.6 ratified jointly. Insights Phase 1 ships the primitives; Action Engine R2 wires them in.

### 4.8 Tool Registry metadata schema extension — joint workstream (MEDIUM) — LAVERN-adjacent

The existing §4.5 proposed extending `ToolHandlerMetadata` with `Classification / CostClass / LatencyClass / Idempotency / AuthMode / Discoverability`. LAVERN patterns add three more fields:

| New field | Source | Purpose |
|---|---|---|
| `ModelTier` | LAVERN ADR 10.4 / Pattern #11 | `Premium | Standard | Fast | Embedding` — enables EvaluatorGate tier-separation in Action Engine R2+ |
| `PhaseRestrictions` | LAVERN Pattern #8 | Array of phase names where the tool MUST NOT dispatch — Action Engine MVP enforcement |
| `EvidenceRequired` | LAVERN Pattern #6 | bool — Insights D-A23 / D-48 runtime guard |

These extend the same schema; combine into a single Tool Registry metadata workstream as §4.5 already proposes. Estimated ~3–4 days. Both projects use the extended schema for all tool registrations.

**Decision artifact**: Joint workstream item before either project pipelines. Insights tools (D-A9) populate `EvidenceRequired = true` on evidence-bearing handlers. Action Engine tools populate `PhaseRestrictions` per Action Definition manifest phases.

---

## 5. Sequencing recommendation

| Order | Item | Owner | Blocks |
|---|---|---|---|
| 0 | Joint signal-envelope contract doc (§4.2) — extended for `type: "precedent"` per Insights D-46 | Both | Action MVP signal-triggered code, Insights Track B emit |
| 0 | Decision: Action Engine layers above JPS Playbook (§4.1) — quick ADR | Both | Action MVP entity schema, Insights closure-extraction playbook design (D-A12) |
| 0 | **LAVERN ADR 10.1 (Precedent Board) ratified** (§4.6 / Insights D-46) | Joint | Insights D-A26 design freeze |
| 0 | **LAVERN ADR 10.3 (GateResolver) ratified** (§4.6) | Joint | Action MVP `/project-pipeline` |
| 0 | **LAVERN ADR 10.6 (Sanitization + Citation Verification Standard) ratified** (§4.7) | Joint | Insights D-A22, D-A25 design freeze |
| 1 | Action Engine runtime spike (§4.3) → runtime ADR | Action | Action MVP pipeline |
| 1 | Tool Registry metadata extension (§4.5 + §4.8 with LAVERN fields) | Both, executed by one | Insights D-A9 tool registration, Action MVP Builder Agent |
| 2 | Insights Placement Justification section added to design.md (§4.4) | Insights | Insights `/project-pipeline` |
| 2 | Action Engine Placement Justification section in spike output (§4.4) | Action | Action `/project-pipeline` |
| 3 | Insights Engine Track A `/project-pipeline` (with LAVERN D-A22–D-A27 in scope) | Insights | — |
| 3 | Action Engine MVP `/project-pipeline` (with LAVERN GateResolver + Phase deny-tools + Tool Registry metadata in scope) | Action | — |
| 4 | Insights Phase 1.5 (Precedent Board lifecycle automation) | Insights | Phase 2 cite-by-reference Precedents |
| 4 | Insights Track B unblocked when Phase C auth lands; Action Engine R2 (signal-triggered Monitors) waits on Track B sync producing real signals | Both | — |

Items at the same Order level are parallelizable.

---

## 6. Concrete artifacts to produce (not in this plan — these are the follow-on tasks)

1. **`docs/adr/ADR-XXX-action-engine-layering-above-playbooks.md`** — Decision: Action Definitions reference Playbooks; Action Templates are a new layer; existing JPS playbook entity is unchanged. Status: Proposed.

2. **`docs/adr/ADR-XXX-action-engine-runtime-topology.md`** — Output of Action Engine architecture spike (§4.3). Status: Proposed.

3. **`projects/ai-spaarke-shared/signal-envelope-contract.md`** — Joint contract doc. `InsightArtifact` is the on-the-wire signal payload. Filter predicates Action Engine Monitors support. Versioning. Schema evolution rules.

4. **`projects/ai-spaarke-insights-engine-r1/design.md` — add §"Placement Justification"** — Per-component ADR-013 exception-criteria mapping (resolver, agent, sync functions, intake function).

5. **`docs/adr/ADR-013-ai-architecture.md` — minor addendum** — Tool registry metadata schema (classification, cost class, latency class, idempotency, auth mode, discoverability). Versioning note that the schema extension is non-breaking for existing handlers.

6. **`.claude/constraints/bff-extensions.md` — no change required** — Both projects must continue to satisfy the existing checklist; no relaxation warranted.

7. **LAVERN ADR 10.1 — Precedent Board** — joint ratification before Insights D-A26 design freeze.

8. **LAVERN ADR 10.3 — GateResolver Interface** — joint ratification before Action MVP `/project-pipeline`. Action Engine MVP is the implementer; Insights consumes Phase 2+ write-back paths.

9. **LAVERN ADR 10.4 — Provider Tier Abstraction** — required before EvaluatorGate (LAVERN ADR 10.2 / R2+ Action Engine). Insights stays on hardcoded D-08 embedding model.

10. **LAVERN ADR 10.6 — AI Output Sanitization and Citation Verification Standard** — joint ratification; Insights builds the primitives in Phase 1, Action Engine consumes in R2.

11. **Insights Engine `lavern-pattern-assessment.md`** — captures Insights Engine's adoption decisions across all 12 LAVERN patterns. Already authored at `projects/ai-spaarke-insights-engine-r1/lavern-pattern-assessment.md`.

12. **Action Engine `lavern-pattern-assessment.md`** — captures Action Engine's adoption decisions across all 12 LAVERN patterns. Authored at `projects/ai-spaarke-action-engine-r1/lavern-pattern-assessment.md`.

---

## 7. Verification — how to confirm coordination is working

- [ ] Both `SPEC.md` / project plans cite the joint signal-envelope contract doc.
- [ ] Action Engine's MVP runtime ADR is co-reviewed by an Insights Engine contributor; Insights's design.md updates are co-reviewed by an Action Engine contributor.
- [ ] Both projects' design.md files contain a "Placement Justification" section before `/project-pipeline` runs.
- [ ] `dotnet publish --runtime linux-x64` delta reported in every PR touching `Sprk.Bff.Api/` from either project; running total of BFF size is tracked in PR descriptions.
- [ ] Both projects' tool registrations use the extended `ToolHandlerMetadata` schema.
- [ ] No new CRUD→AI direct dependencies introduced (current backlog: 20 violations per assessment). Both projects route any CRUD-side consumption through `Services/Ai/PublicContracts/`.
- [ ] `/adr-check` skill returns zero new violations on both projects' first PRs.
- [ ] Both projects' deliverable IDs reference LAVERN ADRs (10.1, 10.3, 10.4, 10.6) where applicable.
- [ ] Shared platform primitives (`ISanitizer`, `GroundingVerifier`, `IGateResolver`) registered in DI and discoverable by both project codebases.
- [ ] Tool Registry metadata extended with LAVERN-adjacent fields (`ModelTier`, `PhaseRestrictions`, `EvidenceRequired`); both projects use the extended schema.
- [ ] Insights Engine Phase 1 acceptance includes 4-tier envelope round-trip + Precedent-layer scaffold end-to-end smoke test.
- [ ] Insights Engine Phase 1.5 plan exists in `INSIGHTS-ENGINE-ARCHITECTURE.md` §21.3 for lifecycle automation.
- [ ] Action Engine MVP design includes GateResolver consumption + Phase deny-tools enforcement.

---

## 8. What this plan deliberately does not do

- Does not propose code changes.
- Does not redesign either project; both designs are largely sound.
- Does not recommend merging the two projects — they remain separate concerns.
- Does not commit Action Engine to a specific runtime topology beyond recommending it adopt Insights's Hybrid D; the spike output is the binding ADR.
- Does not block Insights Engine Track A on Action Engine decisions; only §4.1 (playbook layering) is shared, and the recommendation (Action layers *above*) preserves Insights's design as-is.

---

## 9. Critical files referenced in this assessment

- [`projects/ai-spaarke-insights-engine-r1/SPEC.md`](../../code_files/spaarke/projects/ai-spaarke-insights-engine-r1/SPEC.md)
- [`projects/ai-spaarke-insights-engine-r1/decisions.md`](../../code_files/spaarke/projects/ai-spaarke-insights-engine-r1/decisions.md)
- [`projects/ai-spaarke-insights-engine-r1/design.md`](../../code_files/spaarke/projects/ai-spaarke-insights-engine-r1/design.md)
- [`projects/ai-spaarke-action-engine-r1/action-engine-overview.md`](../../code_files/spaarke/projects/ai-spaarke-action-engine-r1/action-engine-overview.md)
- [`.claude/constraints/bff-extensions.md`](../../code_files/spaarke/.claude/constraints/bff-extensions.md)
- [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../code_files/spaarke/docs/assessments/bff-ai-extraction-assessment-2026-05-20.md)
- [`docs/adr/ADR-001-minimal-api-and-workers.md`](../../code_files/spaarke/docs/adr/ADR-001-minimal-api-and-workers.md)
- [`docs/adr/ADR-013-ai-architecture.md`](../../code_files/spaarke/docs/adr/ADR-013-ai-architecture.md)
- [`docs/adr/ADR-010-di-minimalism.md`](../../code_files/spaarke/docs/adr/ADR-010-di-minimalism.md)
- [`src/server/api/Sprk.Bff.Api/Services/Ai/IToolHandlerRegistry.cs`](../../code_files/spaarke/src/server/api/Sprk.Bff.Api/Services/Ai/IToolHandlerRegistry.cs)
- [`src/server/api/Sprk.Bff.Api/Services/Ai/ToolFrameworkExtensions.cs`](../../code_files/spaarke/src/server/api/Sprk.Bff.Api/Services/Ai/ToolFrameworkExtensions.cs)
- [`src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookIndexingBackgroundService.cs`](../../code_files/spaarke/src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookIndexingBackgroundService.cs)
- [`src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs`](../../code_files/spaarke/src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs)
- [`src/server/api/Sprk.Bff.Api/Api/Ai/CapabilityEndpoints.cs`](../../code_files/spaarke/src/server/api/Sprk.Bff.Api/Api/Ai/CapabilityEndpoints.cs)
- [`src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs`](../../code_files/spaarke/src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs)
