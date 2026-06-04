# Deployment Models + Aggregated Migration Cost Analysis — Microsoft.Agents.AI for Spaarke

> **Project**: agent-framework-fit-assessment-r1 · **Task**: 005
> **Captured at**: 2026-06-03
> **Executor**: Claude Code (task-execute STANDARD rigor)
> **Purpose**: For the ADOPT and PARTIAL surfaces from [`notes/04`](04-per-surface-decision-matrix.md), commit to a concrete deployment model keyed to ADR-013 §"Exceptions" + ADR-001 §"Functions scope" + [`.claude/constraints/bff-extensions.md`](../../../.claude/constraints/bff-extensions.md). Aggregate migration cost, publish-size impact, risks, and reversibility paths. Feeds the synthesis assessment document at §6 (deployment) and §7 (migration cost + risks).
> **Read-only**: no `.cs` files modified.
> **Tonal model**: [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) — honest, citation-backed, evidence-thin claims flagged and converted to prototyping recommendations rather than confident commitments.

---

## §0. Method note (read before scanning recommendations)

### Scope of this task

In scope: S1 (PARTIAL), S3 (PARTIAL), S5A (PARTIAL), **S5B (ADOPT)**, S7 (PARTIAL), S8a (PARTIAL — folds into S1), S8b (PARTIAL — folds into S1). That's the carry-forward from [`notes/04 §R`](04-per-surface-decision-matrix.md#r-recommendation-summary-table).

Out of scope (single-line summary in §6 below): S2 (JPS), S4 (Background jobs), S6 (M365 Copilot).

### Central decision

**S5B is the only ADOPT.** It is also the only adopt-surface where the deployment model is non-trivial — every other adopt/partial surface lands in-process BFF by ADR-013 default. The bulk of this document concentrates on S5B's three-way choice: **Workflows-in-BFF · Workflows-in-Function · Foundry-hosted agent**.

### Evidence-thin disclosure

Per task instructions, the F12 durable-hosting evidence base is thin: no dedicated `/hosting/` Microsoft Learn page exists in the recency window, primary evidence is the `04-hosting/` sample tree at SHA `afa7834e` plus Devblog D6 (referenced 2026-06-03) plus open GitHub Issue #6308 ([notes/03 §F12 + §8 Gap #2](03-agent-framework-feature-map.md#f12-hosting--di-helpers--builderaddaiagent--durable-hosting)). For S5B's Foundry-hosted vs Workflows-in-Function decision, this matters. **The recommendation in §1.4 below is to prototype, not commit, until evidence improves.**

### ADR-013 §"Exceptions" 4-criteria gate (applied per surface)

ADR-013 §"Decision" (refined 2026-05-20) permits non-BFF deployables only when **ALL FOUR** hold:

1. No latency coupling with BFF synthesis (no <500ms TTFB requirement against BFF state)
2. No transactional coupling with BFF session/safety/audit state
3. Bounded, well-defined integration surface (HTTP contract, MCP tools)
4. Separating does not require duplicating latency-sensitive components

Every non-default deployment recommendation below cites the 4-criteria evaluation explicitly. Default is **in-process BFF** when even one criterion fails.

---

## §1. Deployment-model recommendations — per surface

### §1.1 S1 SprkChat → **in-process BFF**

**Recommendation**: In-process BFF. No exception to claim.

**ADR-013 4-criteria evaluation**:

| Criterion | S1 evaluation | Result |
|---|---|---|
| (1) No latency coupling | FAILS — `<500ms` TTFB streaming budget against BFF state ([notes/04 §S1.1](04-per-surface-decision-matrix.md#s11-technical-fit-evaluation-spec-4a)) | Gate fails |
| (2) No transactional coupling | FAILS — per-turn citation context, plan-preview Redis state, session token-budget counter, ChatHistoryManager all share request lifecycle | Gate fails |
| (3) Bounded integration surface | N/A (gates already failed) | — |
| (4) No latency-sensitive component duplication | N/A | — |

Two criteria fail decisively. ADR-013 default holds.

**Additional considerations**:
- The S1 lift is gated on **Issue #6268** (`ChatClientAgent.RunStreamingAsync` produces empty assistant text on multi-tool turns; opened 2026-06-02, `needs-maintainer-triage` as of 2026-06-03 per [notes/01 §S1 carry-forward](01-spaarke-ai-surfaces-inventory.md)). Deployment model does not change; **lift timing** does.
- Feature-flag the lift behind a `Sprk.Ai.UseFrameworkAgent` toggle so rollback is a config flip, not a redeploy.

---

### §1.2 S3 Builder → **in-process BFF**

**Recommendation**: In-process BFF. No exception to claim.

**ADR-013 4-criteria evaluation**:

| Criterion | S3 evaluation | Result |
|---|---|---|
| (1) No latency coupling | PARTIAL — Builder is non-streaming, multi-round, latency soft per [notes/04 §S3.1](04-per-surface-decision-matrix.md#s31-technical-fit-evaluation-spec-4a). But it's invoked from BFF endpoints and shares the BFF process. | Gate ambiguous |
| (2) No transactional coupling | PASSES — `CanvasState` per-request; no session/audit/safety coupling | — |
| (3) Bounded integration surface | PASSES — single method entry point `BuilderAgentService.ExecuteAsync` | — |
| (4) No component duplication | FAILS — extracting would duplicate auth + correlation + ProblemDetails for a small surface | Gate fails |

Criterion (4) fails — Builder is too small a surface to justify a separate deployable; the duplication cost exceeds the value. ADR-013 default holds.

**Additional considerations**:
- Builder uses **OpenAI.Chat SDK directly** (not `IChatClient`) per [notes/04 §S3.5 Evidence 3](04-per-surface-decision-matrix.md#s35-rationale-2-concrete-evidence-pieces). The S3 lift includes a DI rewiring step to route through `IChatClient` — this is the pre-work that makes `ChatClientAgent` usable.

---

### §1.3 S5A Foundry wrapper (shipped) → **in-process BFF**

**Recommendation**: In-process BFF (unchanged from current). The wrapper itself stays in BFF; the agent it routes to is already Foundry-hosted.

**ADR-013 4-criteria evaluation**: N/A — the wrapper is request-routing code inside `SprkChatAgent`'s pipeline ([notes/04 §S5A.1](04-per-surface-decision-matrix.md#s5a1-technical-fit-evaluation-spec-4a)). It's not a candidate for extraction; it's a candidate for **internal code simplification** via `AIProjectClient.AsAIAgent(...)`.

**Additional considerations**:
- Default-OFF per ADR-018 ([notes/02 §2.3(c)](02-non-bff-ai-touchpoints-inventory.md)). Low operational pressure. Bundle with S1 lift; do not lift standalone.

---

### §1.4 S5B Foundry canonical durable HITL → **MIXED — prototype before committing**

**Recommendation**: **Mixed deployment, but the specific mix is gated on prototyping.**

This is the assessment's most consequential deployment decision. S5B is greenfield ([notes/04 §S5B.3](04-per-surface-decision-matrix.md#s5b3-migration-cost) — no Spaarke code yet), so the choice is forward-looking, not migration-tracked.

**ADR-013 4-criteria evaluation (the case for non-BFF deployment is strong)**:

| Criterion | S5B evaluation | Result |
|---|---|---|
| (1) No latency coupling | PASSES — multi-day workflows, no <500ms BFF TTFB coupling ([notes/04 §S5B.1](04-per-surface-decision-matrix.md#s5b1-technical-fit-evaluation-spec-4a)) | Gate passes |
| (2) No transactional coupling | PASSES — workflow state durable in workflow checkpoints + Foundry side; BFF session/safety/audit not in critical path | Gate passes |
| (3) Bounded integration surface | PASSES — workflow event stream + HTTP triggers + A2A endpoints (if exposed); contract is workflow-level | Gate passes |
| (4) No component duplication | PASSES — durable workflow hosting doesn't need to duplicate BFF streaming/routing/safety; those don't apply to multi-day workflows | Gate passes |

**All four ADR-013 §"Exceptions" criteria pass.** S5B legitimately qualifies for a non-BFF deployable.

**Three candidate deployment models**:

#### (a) Workflows-in-BFF (`Microsoft.Agents.AI.Workflows` hosted in `Sprk.Bff.Api`)

- **Fit**: Short-running HITL approvals where state survives via Redis/Dataverse for hours-to-days but not weeks
- **Pros**: Single deployment artifact; existing auth/correlation/observability stack; ADR-013 default
- **Cons**: Workflow state survival bounded by process lifetime + Redis TTL; multi-week NDA workflows exceed BFF process lifetime expectations
- **When to choose**: Short HITL (hours, <1 day), low concurrency

#### (b) Workflows-in-Function (Durable-Functions-style hosting via Agent Framework Durable Workflow patterns)

- **Fit**: Multi-day workflows requiring state survival across BFF restarts; event-driven triggers (timer, queue, webhook)
- **Pros**: ADR-001 already permits Functions for out-of-band integration; Workflows in Functions matches the existing Insights Engine sync pipeline pattern; lower per-session cost than Foundry-hosted
- **Cons**: **EVIDENCE-THIN** — the `04-hosting/DurableWorkflows` sample category exists at SHA `afa7834e` ([notes/03 §F12 evidence-thin caveat](03-agent-framework-feature-map.md#f12-hosting--di-helpers--builderaddaiagent--durable-hosting)) but no dedicated Microsoft Learn `/hosting/` page covers production deployment patterns yet. Open GitHub Issue #6308 indicates the Foundry-hosting story is in active triage as of 2026-06-03.
- **When to choose**: Multi-day workflows without VM-isolation / per-agent-Entra-identity / A2A-endpoint requirements

#### (c) Foundry-hosted agent (`FoundryHostedAgents` pattern; agent itself runs in Foundry, framework runtime invokes it)

- **Fit**: Multi-day workflows requiring per-session VM-isolated sandboxes, per-agent Entra identity, A2A endpoint exposure, Foundry-hosted MCP tools
- **Pros**: Maximum durability + isolation; canonical for the `knowledge/foundry-agent-service/` use cases (NDA negotiation, full-matter diligence, regulatory monitoring) per [notes/04 §S5B.4](04-per-surface-decision-matrix.md#s5b4-recommendation)
- **Cons**: Per-session cost (Foundry SKU pricing UNKNOWN per [notes/02 §6 U5](02-non-bff-ai-touchpoints-inventory.md#6-unknowns-no-invention)); new operational surface (Foundry agent lifecycle management); only relevant if Spaarke actually requires the isolation/identity features
- **When to choose**: Workflow requirements include VM isolation + per-agent Entra identity + A2A peer composition

**Decision logic**:

The choice depends on three questions that the assessment cannot answer authoritatively from current sources:

1. Do Spaarke legal workflows actually require per-session VM-isolated sandboxes? (UNKNOWN per [notes/04 §S5B.7 Q1](04-per-surface-decision-matrix.md#s5b7-open-questions))
2. Do they require per-agent Entra identity for A2A composition? (UNKNOWN per Q2)
3. Are Foundry SKU per-session costs acceptable for expected concurrency? (UNKNOWN per Q3)

**Confidence level: LOW.** The F12 evidence gap (no `/hosting/` Learn page; Issue #6308 open; sample tree is the only ground truth for production deployment patterns) means any pre-commitment to a deployment model is design-by-assumption.

**Recommendation: prototyping phase before commitment.**

When the canonical durable HITL surface gets a project SPEC, the project should include a **deployment prototyping phase** (estimated 1-2 weeks) that:
- Stands up a minimal `WorkflowBuilder` + `RequestPort` HITL workflow in each candidate hosting model (BFF, Function, Foundry-hosted)
- Measures cold-start latency, state-survival behavior across restarts, per-session cost (where measurable)
- Validates whether VM isolation + per-agent identity + A2A endpoint exposure are actual Spaarke requirements (interview legal-ops stakeholders, not infer from sample documentation)
- Returns a deployment-model decision with concrete evidence, not platform speculation

Without prototyping, Spaarke commits to a hosting model based on incomplete primary sources. The 2026-05-20 BFF AI extraction assessment's lesson applies: **uncomfortable conclusions land better than premature confident ones.**

**Additional considerations**:
- The S5B project SPEC trigger ([notes/04 §S5B.7 Q4](04-per-surface-decision-matrix.md#s5b7-open-questions)) is a roadmap question outside this assessment. Until that SPEC exists, deployment is "TBD pending prototype."
- If the S5B project ships before Issue #6308 resolves (Foundry hosting deployment story in active triage), the prototyping phase MUST re-check Issue #6308 at SPEC-authoring time and adjust the prototype scope accordingly.

---

### §1.5 S7 Insights Engine MCP → **DEFERRED to D-A20 contract authoring**

**Recommendation**: Deployment model is **UNKNOWN per [notes/02 §6 U2](02-non-bff-ai-touchpoints-inventory.md#6-unknowns-no-invention)** and will be decided at D-A20 contract authoring time (Phase 1 of `ai-spaarke-insights-engine-r1`). The assessment cannot pre-commit.

**ADR-013 4-criteria evaluation (preliminary, contract-pending)**:

| Criterion | S7 MCP server evaluation | Result |
|---|---|---|
| (1) No latency coupling | LIKELY PASSES — MCP tool calls are request/response; sync but not <500ms-BFF-state-coupled | Likely passes |
| (2) No transactional coupling | LIKELY PASSES — MCP server's BFF-side seam is via `IInsightsAi` facade (per [notes/04 §S7.4 D-A20 guidance](04-per-surface-decision-matrix.md#s74-recommendation)); no session/safety coupling | Likely passes |
| (3) Bounded integration surface | PASSES — MCP protocol IS the bounded contract | — |
| (4) No component duplication | PARTIAL — auth + correlation + ProblemDetails would duplicate if MCP server is separate deployable; may be acceptable for a thin transport over `IInsightsAi` | Conditional |

Preliminary read: all four MAY pass, which would justify separate-deployable per ADR-013. But criterion (4) depends on whether the MCP server is a thin transport (low duplication cost) or hosts agents internally (higher duplication risk). The D-A20 contract decides.

**Two candidate deployment models** (per [notes/04 §S7.6](04-per-surface-decision-matrix.md#s76-deployment-model-spec-4d)):

1. **Separate `Sprk.Insights.Mcp` deployable** — ADR-013's "MCP server exposing AI capabilities to external consumers" example. Required if external consumers (M365 Copilot per S6 Open Question 1) need to consume Insights without the full BFF.
2. **MCP endpoints embedded in BFF** — Simpler; matches the [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) §8 recommendation to defer MCP server extraction until Insights Engine Phase 1 lands.

**Additional considerations**:
- The in-BFF Insights Agent (D-A9) tracks S1 ([notes/04 §S7.4](04-per-surface-decision-matrix.md#s74-recommendation)) — its deployment is in-BFF regardless.
- The 2026-05-20 BFF AI extraction assessment §8 recommended "defer MCP server extraction with re-assessment after Insights Engine Phase 1 lands." This task's recommendation is consistent: **the deployment-model decision is downstream of contract authoring + Phase 1 implementation, not upstream of it.**

---

### §1.6 S8a SessionSummarizationService → **fold into S1 perimeter**

**Recommendation**: In-process BFF. Track S1 deployment.

S8a is a sub-component of S1's SprkChat session lifecycle ([notes/04 §S8a.1](04-per-surface-decision-matrix.md#s8a1-technical-fit-evaluation-spec-4a)). It has no independent deployment story; it runs inside the SprkChat pipeline as a fire-and-forget post-turn worker.

**ADR-013 4-criteria evaluation**: N/A — sub-component of S1; inherits S1's gate failures on (1) and (2).

**Additional considerations**: Per [notes/04 §S8a.4](04-per-surface-decision-matrix.md#s8a4-recommendation), S8a is **DON'T ADOPT** for full Agents.AI lift but **PARTIAL** if S1 adopts `RunAsync<T>` for `CompoundIntentDetector` — in which case S8a's `SessionSummary` JSON parsing should be unified under the same pattern. Decision tracks S1.

---

### §1.7 S8b CapabilityRouter → **fold into S1 perimeter**

**Recommendation**: In-process BFF. Track S1 deployment.

Same as S8a — S8b is a sub-component of S1 (called from `SprkChatAgentFactory.CreateAgentAsync` per [notes/04 §S8b.1](04-per-surface-decision-matrix.md#s8b1-technical-fit-evaluation-spec-4a)). It shares the `[FromKeyedServices("raw")] IChatClient` registration with `CompoundIntentDetector`.

**ADR-013 4-criteria evaluation**: N/A — sub-component of S1.

**Additional considerations**: Per [notes/04 §S8b.4](04-per-surface-decision-matrix.md#s8b4-recommendation), S8b adopts F5 (structured outputs) only, alongside S1 lift. Same PR set as `CompoundIntentDetector` lift.

---

## §2. Shared infrastructure changes — amortized across multiple surfaces

The four S1-adjacent PARTIAL surfaces (S1, S3, S8a, S8b — and S5A by adoption-bundling) would benefit from **one cross-cutting infrastructure change**, not four independent migrations. Framing matters: the total cost is the shared change + per-surface lift, not 4× per-surface lift.

### §2.1 The cross-cutting change: middleware lift to `IChatClient.AsBuilder().Use*().Build()`

**Today (per [notes/01 cross-cutting observation 3](01-spaarke-ai-surfaces-inventory.md))**: Spaarke decorates `ISprkChatAgent` (a Spaarke interface) with `AgentTelemetryMiddleware`, `AgentContentSafetyMiddleware`, `AgentCostControlMiddleware`. These are per-instance decorator instances wired in `SprkChatAgentFactory.CreateAgentAsync`. The pattern works but is **structurally non-idiomatic** — the framework expects middleware composed via `chatClient.AsBuilder().Use*().Build()` at the `IChatClient` tier, with agent-level middleware via `agent.AsBuilder().Use*().Build()` at the `AIAgent` tier.

**The lift**: Replace `SprkChatAgentFactory`'s manual decorator stack with the framework's two-tier composition:
- `IChatClient`-tier middleware: `AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build()` (raw client wrappers — function dispatch, OTel)
- `AIAgent`-tier middleware: `.AsBuilder().Use(...)` for Spaarke-specific policy (content safety, cost control, custom telemetry per [notes/03 §F4 + §F10](03-agent-framework-feature-map.md#f4-middleware-framework))

**Why this is one change, not four**:
- S1's `SprkChatAgent` IS the SprkChat surface; its lift IS this change.
- S3 Builder currently has NO middleware composition ([notes/04 §S3.3](04-per-surface-decision-matrix.md#s33-migration-cost-spec-4c) — Builder uses OpenAI.Chat SDK directly). Lifting Builder to `ChatClientAgent` means it inherits whatever the middleware stack provides at that point; no separate middleware code to write.
- S8a and S8b consume the same `[FromKeyedServices("raw")] IChatClient` registration as `CompoundIntentDetector` ([notes/04 §S8b.5 Evidence 1](04-per-surface-decision-matrix.md#s8b5-rationale)). When the "raw" registration upgrades to a framework-composed chain, all three consumers benefit without per-surface code change.
- S5A Foundry wrapper consumes from `SprkChatAgent` shaped contexts; bundling its lift with S1's same-PR set inherits the new middleware stack.

**Per [`notes/04 §S1.4 last paragraph`](04-per-surface-decision-matrix.md#s14-recommendation)**: "Spaarke is decorating `ISprkChatAgent` instead of `IChatClient`, exactly the kind of hand-rolled equivalent that the framework subsumes." Task 001 named this as **the biggest single migration vector for S1**. The task 005 framing: it's also the biggest single migration vector for S3/S8a/S8b/S5A by amortization.

### §2.2 Other shared infrastructure changes

| Change | Surfaces affected | Estimated standalone effort | Notes |
|---|---|---|---|
| Lift `[FromKeyedServices("raw")] IChatClient` from raw OpenAI bridge to `chatClient.AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build()` | S1, S8a, S8b | 2-3 days | The keyed registration becomes the framework-composed chain |
| Standardize OTel source-name conventions on Agent Framework GenAI Semantic Conventions ([notes/03 §F10](03-agent-framework-feature-map.md#f10-observability)) | S1, S3, S5A | 1-2 days | Replaces hand-rolled `AgentTelemetryMiddleware` attributes |
| Migrate Builder DI from OpenAI.Chat SDK to `IChatClient` ([notes/04 §S3.5 Evidence 3](04-per-surface-decision-matrix.md#s35-rationale-2-concrete-evidence-pieces)) | S3 only (but pre-req for S3 lift) | 1-2 days | Sequence: this BEFORE S3's `ChatClientAgent` adoption |
| `AgentSession` reconciliation with Spaarke's Redis-externalized chat history ([notes/04 §S1.7 Q3](04-per-surface-decision-matrix.md#s17-open-questions-for-human-decision)) | S1 (and S5A when bundled) | 3-5 days uncertainty | This is the **most uncertain** shared change — depends on whether `CreateSessionAsync(conversationId)` model fits |

### §2.3 Migration sequencing implied by shared infrastructure

The shared-change framing implies a sequencing:

1. **Phase 0 (precondition)**: GitHub Issue #6268 resolves in a shipped 1.x release. Until this, the S1 PARTIAL verdict gates.
2. **Phase 1 (pre-work)**: Builder OpenAI.Chat SDK → `IChatClient` migration (S3 prep). Independent of #6268.
3. **Phase 2 (shared infrastructure)**: Middleware lift + OTel standardization + `[FromKeyedServices("raw")]` chain upgrade. One PR set.
4. **Phase 3 (parallel surface lifts)**: S1 + S5A + S8a + S8b + S3 each lift in their own PR, consuming the now-framework-composed chains.

Effort estimate (rough, low-confidence): 4-8 person-weeks for shared infrastructure (Phase 1+2); 2-4 person-weeks per surface lift in Phase 3 (5 surfaces × 0.5-1 week each). **Aggregate range: 6-14 person-weeks.** See §6 for confidence framing.

---

## §3. Publish-size impact — table + cumulative projection

### §3.1 Baselines

| Baseline | Compressed size | Source |
|---|---|---|
| Pre-2026-05-19 BFF baseline | ~60 MB | [`.claude/constraints/bff-extensions.md`](../../../.claude/constraints/bff-extensions.md) §"Why This Constraint Exists" |
| 2026-05-19 jump | 75+ MB | [`.claude/constraints/bff-extensions.md`](../../../.claude/constraints/bff-extensions.md) (cited as "65 → 75 MB"); [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) confirms |
| Post-Outcome-A (linux-x64 framework-dependent) baseline | 45.65 MB | [`.claude/constraints/azure-deployment.md`](../../../.claude/constraints/azure-deployment.md) "Phase 5 demo deploy verified" + sdap-bff-api-remediation-fix EXECUTION-LOG |
| Tolerance threshold (flag if exceeded) | 80 MB compressed | Conservative interpretation of "75+ MB jump was a problem; further accumulation worse" |

The **45.65 MB Outcome-A baseline** is the operational current state. The 60/75 MB numbers are pre-remediation. New package additions should be measured against the 45.65 MB current floor; the 80 MB ceiling is a soft tolerance.

### §3.2 Per-surface NuGet additions

The decisive finding from [notes/01 top-level finding](01-spaarke-ai-surfaces-inventory.md#top-level-finding-microsoftextensionsai-vs-microsoftagentsai) and verified inline in `Sprk.Bff.Api.csproj` lines 25-35: **`Microsoft.Agents.AI 1.0.0-rc1` is ALREADY referenced** but zero code uses it. The S1/S3/S8a/S8b/S5A lift activates code paths in an already-shipped assembly; net publish-size delta is **zero** for these surfaces.

| Surface | New top-level NuGet refs implied by adoption | Estimated compressed delta | Notes |
|---|---|---|---|
| S1 SprkChat (PARTIAL) | None — `Microsoft.Agents.AI 1.0.0-rc1` already present | **0 MB** | Activating already-shipped assembly |
| S3 Builder (PARTIAL) | None — Builder's lift uses already-present `Microsoft.Agents.AI` | **0 MB** | Builder may also drop direct `OpenAI.Chat` usage if routed through `IChatClient`; potential -0.x MB micro-reduction (not material) |
| S5A Foundry wrapper (PARTIAL) | None — `Azure.AI.Projects 1.0.0-beta.8` already present; `AsAIAgent` is a method call, not a package addition | **0 MB** | The `Microsoft.Agents.AI` glue for Foundry is likely transitive via existing references; verify at lift time |
| S5B canonical durable HITL (ADOPT) | Likely **`Microsoft.Agents.AI.Workflows`** (separate assembly per [notes/03 §F7](03-agent-framework-feature-map.md#f7-workflows)); possibly **`Microsoft.Agents.AI.Hosting.A2A.AspNetCore`** for A2A exposure (per [notes/03 §F9](03-agent-framework-feature-map.md#f9-a2a)); possibly **`Microsoft.Agents.AI.Foundry`** for Foundry-hosted-agent integration (per [notes/03 §F12](03-agent-framework-feature-map.md#f12-hosting--di-helpers--builderaddaiagent--durable-hosting)) | **2-6 MB cumulative estimate (UNCERTAIN)** | NuGet package sizes for Agent Framework family typically run 0.5-2 MB compressed each per upstream NuGet metadata patterns. Three packages × 0.5-2 MB ≈ 1.5-6 MB. **Flag: this is an estimate, not measured** — Agents.AI family packages may pull additional transitives. Verify at SPEC time. |
| S7 Insights MCP server (PARTIAL) | Depends on deployment: if embedded in BFF, possibly `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` or `ModelContextProtocol.AspNetCore` (D-A20 decides) | **0-2 MB (UNCERTAIN)** | If separate `Sprk.Insights.Mcp` deployable, the additions don't affect BFF publish size at all — they affect the new deployable's size |
| S8a / S8b | None — sub-components of S1 | **0 MB** | — |

### §3.3 Cumulative BFF projection

Two scenarios:

**Scenario A: S1/S3/S5A/S8a/S8b lift only (no S5B in BFF, no S7 in BFF)**

| Component | Compressed size | Notes |
|---|---|---|
| Current BFF baseline | 45.65 MB | Per Outcome-A |
| Net package additions | 0 MB | All additions are no-op |
| **Projected total** | **~45-46 MB** | Within tolerance |

**Scenario B: S1/S3/S5A/S8a/S8b lift + S5B Workflows-in-BFF + S7 MCP embedded in BFF**

| Component | Compressed size | Notes |
|---|---|---|
| Current BFF baseline | 45.65 MB | — |
| S5B additions (Workflows + A2A hosting + possibly Foundry glue) | +1.5-6 MB | UNCERTAIN |
| S7 MCP host additions (if embedded) | +0-2 MB | UNCERTAIN |
| **Projected total** | **~47-54 MB** | Within tolerance; well under 80 MB |

**Conclusion**: Even the worst-case full-adoption scenario lands at ~54 MB compressed, well under the 80 MB tolerance flag and below the 2026-05-19 jump that triggered the bff-extensions governance constraint. **The framework adoption is NOT a publish-size risk for the BFF.**

Caveat: estimates are upper-bounded from the `Microsoft.Agents.AI` family general size profile; actual measurement required at lift time. The bff-extensions constraint §A.3 binds: "verify the addition does not regress the publish baseline...Run `dotnet publish --runtime linux-x64` locally and inspect output size before merging if adding packages."

### §3.4 S5B deployed elsewhere (Function or Foundry-hosted) — different picture

If S5B deploys as Workflows-in-Function or Foundry-hosted (§1.4 candidates b and c), the BFF is unaffected entirely. The new packages live in the Function deployable or are unnecessary (Foundry agent runs in Foundry's process, the framework runtime in BFF only needs the client glue). **The Workflows-in-BFF scenario is the worst case for BFF size; the alternative deployments improve the picture.**

---

## §4. Risk register

Eight risks identified across adopt/partial surfaces. Severity scale: LOW (mitigatable in PR), MED (requires design attention), HIGH (architectural / SLO-level).

| # | Surface(s) | Risk | Severity | Mitigation |
|---|---|---|---|---|
| R1 | S1 | **Issue #6268 doesn't ship resolved in time, or ships incompletely.** SprkChat's canonical workload (multi-tool streaming) is the exact failure mode. Lifting before resolution ships a regression. | **HIGH** | Feature-flag the lift (`Sprk.Ai.UseFrameworkAgent`); maintain hand-rolled fallback path; re-evaluate at each Agent Framework release. Do NOT lift until #6268 is fixed in a shipped 1.x. |
| R2 | S5B | **F12 durable hosting evidence is thin** ([notes/03 §F12 caveat](03-agent-framework-feature-map.md#f12-hosting--di-helpers--builderaddaiagent--durable-hosting)). Foundry-hosted vs Workflows-in-Function decision is currently design-by-assumption. Issue #6308 (Foundry hosting in active triage) signals the upstream story is unstable. | **HIGH** | Prototyping phase before SPEC commitment (§1.4 recommendation). Do NOT pre-commit to a deployment model in the S5B project SPEC before prototype runs. |
| R3 | S1, S3, S5A (shared infra) | **OTel pipeline disruption** during middleware lift. Spaarke's hand-rolled `AgentTelemetryMiddleware` emits Spaarke-specific attributes; framework `WithOpenTelemetry()` emits GenAI Semantic Conventions. Switchover could lose continuity in Application Insights dashboards. | **MED** | Parallel-emit during transition (hand-rolled + framework-standard on different source names); cut over once dashboard parity verified. Update [`docs/guides/`](../../../docs/guides/) observability guide pre-cut-over. |
| R4 | S1 | **`AgentSession` reconciliation with Redis-externalized chat history is the most uncertain shared change.** If `CreateSessionAsync(conversationId)` doesn't map cleanly to Spaarke's Redis cache pattern, the S1 lift requires more code than the rest combined. | **MED** | De-risk via spike before committing to the S1 lift scope. The spike outcome ([notes/04 §S1.7 Q3](04-per-surface-decision-matrix.md#s17-open-questions-for-human-decision)) determines effort estimate confidence. |
| R5 | S3 | **OpenAI.Chat SDK → `IChatClient` migration is pre-work**, not bundled with `ChatClientAgent` lift. Skipping this sequencing produces an intermediate state where Builder runs on `OpenAI.Chat` while using framework types — a known anti-pattern. | **LOW** | Sequence Builder's DI rewiring as an explicit pre-PR before the `ChatClientAgent` adoption PR. Two PRs, not one. |
| R6 | S5B | **Foundry SKU per-session cost is UNKNOWN** ([notes/04 §S5B.7 Q3](04-per-surface-decision-matrix.md#s5b7-open-questions)). If Foundry-hosted is the chosen deployment and the per-session cost is higher than projected, the runtime cost of multi-day workflows could be material. | **MED-HIGH** | Owner-level cost analysis before committing to Foundry-hosted deployment. Workflows-in-Function may be cheaper for the same durability — but lacks the VM-isolation/per-agent-identity features Foundry provides. |
| R7 | S5B, S7 | **A2A protocol still evolving** — framework's `MapA2A` + cross-system agent identity is recent. Pre-committing Spaarke to A2A interop now risks protocol churn. | **MED** | Defer A2A exposure decision to a future iteration; build S5B without A2A first; add when standards stabilize. |
| R8 | S1, S3, S5A | **Test infrastructure shift from `IChatClient` mocks to `AIAgent` mocks.** The ~2,800 AI tests cited in [bff-extraction §5](../../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) include substantial SprkChat-specific tests; re-keying mocks is mechanical but voluminous. | **MED** | Treat test re-keying as part of each surface's lift effort; not an afterthought. Estimate +30-50% effort on top of source code lift for test impact. |
| R9 | S5B | **No Spaarke code yet** ([notes/04 §S5B.3](04-per-surface-decision-matrix.md#s5b3-migration-cost)). The "migration cost" is actually greenfield implementation cost. The risk is mis-scoping the work as "small framework adoption" when it's actually "build a multi-agent durable workflow system from scratch." | **HIGH** | When the S5B SPEC is authored, scope the greenfield work explicitly. Bound the assessment-derived recommendation: "adopt Agent Framework primitives for the workflow layer" is one decision; "ship multi-day legal workflows" is multiple person-quarters of work. |
| R10 | All adopt/partial | **Stacked pre-release packages.** `Sprk.Bff.Api.csproj` already pins three pre-release packages ([bff-extraction §1](../../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md)). The S5B adoption adds `Microsoft.Agents.AI.Workflows` (likely 1.0.0-rc-flavor), possibly more. Each pre-release pin is a future-rev risk. | **MED** | Track each pre-release pin with explicit csproj comment per [bff-extensions §B](../../../.claude/constraints/bff-extensions.md). Set re-evaluation date when 1.0 GA ships. |

**Top 3 HIGH-severity risks**: R1 (Issue #6268), R2 (F12 evidence gap), R9 (S5B mis-scoping). All three argue for **waiting, prototyping, or scoping carefully** rather than premature adoption.

---

## §5. Reversibility analysis — per ADOPT/PARTIAL surface

For each surface, the rollback path if adoption goes badly. Reversibility scale: HIGH (cheap rollback via config or git revert), MEDIUM (PR-set revert), LOW (forward-only migration, data implications).

### §5.1 S1 SprkChat → reversibility: **MEDIUM-HIGH**

**Rollback path**:
- Feature-flag (`Sprk.Ai.UseFrameworkAgent`) gating allows runtime fallback to hand-rolled `ISprkChatAgent` path WITHOUT redeploy. Config flip reverts.
- The hand-rolled `ISprkChatAgent` interface can be retained as a facade over the framework agent during transition ([notes/04 §S1.3 reversibility row](04-per-surface-decision-matrix.md#s13-migration-cost-spec-4c)). When the framework path is removed, this facade is removed too. Until then, both paths exist.
- Middleware lift is mechanical per class; reverting one middleware class (e.g., re-enabling hand-rolled `AgentContentSafetyMiddleware`) does not require reverting the others.

**Code locations to retain during transition**: `Services/Ai/Chat/Middleware/Agent*Middleware.cs` (existing hand-rolled classes); `Services/Ai/Chat/SprkChatAgentFactory.cs` (decorator wiring).

**Forward-only aspects**: None for code. AgentSession state model migration MIGHT have data implications if Redis-stored history schema changes — but the recommendation in [notes/04 §S1.7 Q3](04-per-surface-decision-matrix.md#s17-open-questions-for-human-decision) is to preserve Spaarke's Redis pattern, not migrate it. Assuming that holds, no data forward-only.

### §5.2 S3 Builder → reversibility: **HIGH**

**Rollback path**:
- ~3-5 file surface ([notes/04 §S3.3](04-per-surface-decision-matrix.md#s33-migration-cost-spec-4c)); git-revert-cheap.
- Builder is non-streaming, non-session-stateful — no in-flight state to migrate.
- The pre-PR (OpenAI.Chat SDK → `IChatClient`) could ship independently and be retained even if the subsequent `ChatClientAgent` lift is reverted.

**Code locations**: `Services/Ai/Builder/BuilderAgentService.cs`, `BuilderToolDefinitions.cs`, `BuilderToolExecutor.cs`, plus the DI registration in the appropriate module (likely `AiModule.cs` or a builder-specific module).

**Forward-only aspects**: None.

### §5.3 S5A Foundry wrapper → reversibility: **HIGH**

**Rollback path**:
- ~1 file primary change (`AgentServiceClient.cs`).
- Default-OFF (ADR-018 kill switch) means rollback is config flip even if code is shipped.
- Bundled with S1 lift per recommendation, so reverting requires reverting the S1 lift too — but the S5A piece is small.

**Code locations**: `Services/Ai/Foundry/AgentServiceClient.cs`, `AgentServiceRoutingMiddleware.cs`, `AgentServiceNodeExecutor.cs`.

**Forward-only aspects**: None.

### §5.4 S5B canonical durable HITL → reversibility: **LOW-MEDIUM (depends on deployment)**

**Rollback path**: There is no "rollback" because there is no pre-adoption state — S5B is greenfield. The relevant question is: if the chosen deployment model (BFF / Function / Foundry-hosted) turns out wrong, how cheap is the re-host?

- **Workflows-in-BFF → Workflows-in-Function**: Medium cost. Workflow code largely portable; the change is hosting code (HTTP + DI bootstrap) + deployment pipeline. Estimate: 1-3 weeks for established workflows.
- **Workflows-in-Function → Workflows-in-BFF**: Same.
- **Workflows-in-BFF/Function → Foundry-hosted**: HIGHER cost. Foundry-hosted requires agent definition + tool registration + lifecycle management in Foundry. Cross-host migration is not a simple lift-and-shift.
- **Foundry-hosted → Workflows-in-BFF/Function**: HIGHER cost (same reason in reverse). State stored in Foundry must migrate; per-session VM-isolated state may not have a Workflows-native equivalent.

**Code locations**: TBD (greenfield).

**Forward-only aspects**: Workflow state persistence (checkpoints) creates data with the chosen hosting model. Re-hosting requires either drain-and-restart (mid-flight workflows complete on old host, new workflows start on new host) OR state migration (complex).

**Mitigation per task instruction (prototyping)**: The §1.4 recommendation to prototype all three deployment models BEFORE committing is itself a reversibility hedge — minimize the risk of choosing wrong.

### §5.5 S7 Insights MCP server → reversibility: **HIGH (decision deferral) / MEDIUM (post-commit)**

**Rollback path**:
- Pre-commit (D-A20 contract authoring not yet done): trivial — no decision committed, nothing to revert.
- Post-commit: if D-A20 chooses separate `Sprk.Insights.Mcp` deployable and the decision turns out wrong, re-hosting back into BFF requires endpoint relocation + auth/correlation re-wiring. Estimate: 1-2 weeks.
- Post-commit: if D-A20 chooses BFF-embedded and external consumers (Copilot) later require separate deployable per S6.6 Q1, extraction is a re-run of the [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) exercise scoped to the MCP slice only.

**Code locations**: TBD (Phase 2 deferred).

**Forward-only aspects**: External MCP consumers (M365 Copilot, third-party agents) consume via the MCP protocol contract. Once the protocol contract is published, breaking changes to the contract are forward-only relative to consumers.

### §5.6 S8a, S8b → reversibility: **HIGH** (sub-components of S1)

Track S1 reversibility. The sub-component lifts (~1 file each) are git-revert-cheap independently. Recommended to bundle with S1 PR set for transactional consistency.

---

## §6. DON'T ADOPT surfaces — no migration, no deployment change

Per task instructions, one-line summary each:

- **S2 AnalysisOrchestration + JPS playbooks**: No migration; no deployment change. JPS remains in-process BFF with current `IPlaybookExecutionEngine` + node-executor architecture. (Rationale in [notes/04 §S2.4](04-per-surface-decision-matrix.md#s24-recommendation).)
- **S4 Background AI jobs**: No migration; no deployment change. Job handlers remain on the existing `IJobHandler` + Service Bus + idempotency stack. (Rationale in [notes/04 §S4.4](04-per-surface-decision-matrix.md#s44-recommendation).)
- **S6 M365 Copilot / Declarative Agent surface**: No migration; no deployment change. Uses M365 Agents SDK (`Microsoft.Agents.Builder`), not Agent Framework — different SDK for a different runtime surface. (Rationale in [notes/04 §S6.4](04-per-surface-decision-matrix.md#s64-recommendation).)

---

## §7. Migration cost summary

### §7.1 Rough total person-weeks (with explicit confidence framing)

| Phase | Scope | Estimate (rough, ranged) | Confidence |
|---|---|---|---|
| **Phase 0** | Wait for Issue #6268 to land in shipped 1.x | 0 person-weeks of Spaarke effort (calendar gating, not effort gating) | HIGH that 0 effort needed; LOW on calendar timing |
| **Phase 1** | Builder pre-work (OpenAI.Chat SDK → `IChatClient`) | 1-2 person-weeks | MED |
| **Phase 2** | Shared infrastructure (middleware lift + OTel standardization + keyed-services chain upgrade) | 4-8 person-weeks | LOW-MED |
| **Phase 3** | Per-surface lifts (S1 + S3 + S5A + S8a + S8b), each 0.5-1.5 person-weeks | 3-7 person-weeks | LOW-MED |
| **S5B (separate project)** | Greenfield durable HITL legal workflows | OUT OF SCOPE for this estimate — multiple person-quarters of greenfield work, not a "migration." Per R9. | N/A |
| **S7 (deferred)** | MCP server decision + implementation per D-A20 contract | OUT OF SCOPE — Phase 2 deferred per [notes/04 §S7.4](04-per-surface-decision-matrix.md#s74-recommendation) | N/A |
| **In-scope total (Phase 1+2+3, excluding S5B/S7)** | All S1-family lifts | **8-17 person-weeks** | LOW-MED overall |

### §7.2 Confidence framing

**Why LOW-MED, not higher**:
1. R4 (AgentSession reconciliation) is the most uncertain shared change; could be 1-week spike or 3-week deep rewrite.
2. R8 (test re-keying) adds 30-50% on top of source lift; range captures this.
3. F12 evidence-thin caveat means any S5B-related effort is bracketed by prototyping uncertainty.
4. The Spaarke team has not previously lifted to Agent Framework; learning-curve assumptions are author estimates, not empirical.

**Why not higher uncertainty**:
1. `Microsoft.Agents.AI 1.0.0-rc1` already referenced; zero publish-size risk reduces the deploy-side coordination cost.
2. Code surface is well-bounded — S1 + S3 + S5A + S8a + S8b sum to ~10-15 files of primary change per [notes/04 §S1.3 + §S3.3 + §S5A.3 + §S8a.3 + §S8b.3](04-per-surface-decision-matrix.md#s13-migration-cost-spec-4c).
3. ADR-013 doesn't change; ADR-001 doesn't change; deployment model stays in-process BFF for all S1-family surfaces.

### §7.3 The S5B framing — separate effort, separate calendar

S5B is the **highest-fit, highest-strategic-value adoption** per [notes/04 §S5B.4](04-per-surface-decision-matrix.md#s5b4-recommendation) but it is **not** a migration. It is a greenfield project that should:

1. Have its own SPEC + project scope when the canonical durable HITL legal workflows surface gets owner sign-off.
2. Include the 1-2 week prototyping phase recommended in §1.4 BEFORE the SPEC commits to a deployment model.
3. Be estimated in person-quarters at the project level, not person-weeks at the migration level.

Folding S5B into a single "Agent Framework migration cost" number would mislead. The framework decision (use Agent Framework primitives for the workflow layer) is decoupled from the implementation cost (build multi-day legal workflows from scratch).

---

## §8. Acceptance criteria verification

Per the task POML §acceptance-criteria:

- [x] **Every ADOPT/PARTIAL surface has explicit deployment-model recommendation** — §1.1 (S1: in-process BFF), §1.2 (S3: in-process BFF), §1.3 (S5A: in-process BFF), §1.4 (S5B: mixed, prototype first), §1.5 (S7: deferred to D-A20), §1.6 (S8a: fold into S1), §1.7 (S8b: fold into S1).
- [x] **Each non-default deployment cites ADR-013 §4 criteria** — §1.4 (S5B, all four passes); §1.5 (S7, three pass + one conditional). For in-BFF surfaces, the criteria failures are documented inline showing why the default holds.
- [x] **Publish-size analysis enumerates new NuGet refs + estimated size delta** — §3.2 table, with `Microsoft.Agents.AI 1.0.0-rc1` already-referenced finding called out; cumulative projection (~45-54 MB) under 80 MB tolerance flag in §3.3.
- [x] **Risk register has 5-10 items with surface / mitigation / severity** — §4 has 10 risks (R1-R10), 3 HIGH-severity (R1, R2, R9), 4 MED/MED-HIGH, 1 LOW.
- [x] **DON'T ADOPT surfaces listed with one-line "no migration" notes** — §6 covers S2, S4, S6.

**Additional task-instruction acceptance**:

- [x] **Shared infrastructure as ONE change, not four** — §2 frames middleware lift as the single cross-cutting change amortized across S1/S3/S5A/S8a/S8b.
- [x] **F12 evidence-gap forced a prototyping recommendation** — §1.4 explicitly recommends prototyping phase before S5B SPEC commits; cited as task R2 in risk register.
- [x] **ADR-013 4-criteria gate applied explicitly to S5B** — §1.4 four-row table showing all four pass.
- [x] **Tonal model match** — "Structurally X but operationally Y" framing applied to S5B (structurally durable-workflow-fit but operationally uncertain on deployment); honest "we cannot pre-commit" applied to S7 and S5B deployment.

---

## §9. Sign-off

This document satisfies task 005 acceptance criteria. Downstream consumers:

- **Task 006 (synthesis)** consumes §1 deployment recommendations as the assessment document §6, §4 risk register + §7 migration cost summary as the assessment document §7, §5 reversibility analysis informs §8 open questions, §6 DON'T ADOPT notes summarize as one-line closing per surface.
- **Task 007 (adversarial review)** should challenge: (a) Is §1.4's prototyping recommendation a cop-out, or genuinely the right move given F12 evidence? (b) Is the §3 publish-size finding ("net zero for S1-family") complacent — what about transitive bumps? (c) Is the §7.1 8-17 person-week range too narrow given R4's uncertainty?
- **Future ADR-013 amendment consideration**: §1.4's S5B deployment passes all four ADR-013 §"Exceptions" criteria. If the canonical durable HITL project ships with Workflows-in-Function or Foundry-hosted deployment, ADR-013 should be amended to name this as a permitted exception class.

**The single most important conclusion from this task**: **S5B's deployment-model decision should not be pre-committed by this assessment.** The F12 evidence gap, the Foundry SKU cost UNKNOWN, the three workflow-requirement unknowns ([§S5B.7 Q1-Q3](04-per-surface-decision-matrix.md#s5b7-open-questions)) all point to one answer: **build the prototype before writing the SPEC.**
