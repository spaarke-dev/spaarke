# Lavern Analysis and Adoption Plan
## Spaarke platform — patterns, artifacts, schemas, ADRs, and sequencing

> **Date**: 2026-05-20
> **Status**: Working analysis document (transitional)
> **Owner**: ralph.schroeder@hotmail.com
> **Purpose**: Consolidate all research findings from [AnttiHero/lavern](https://github.com/AnttiHero/lavern) (Apache 2.0) and define Spaarke's adoption plan. This document is the working source-of-truth — it will be decomposed into formal ADRs, project specs, design.md updates, and Dataverse schema specs as workstreams formalize.
> **Source repository**: https://github.com/AnttiHero/lavern (commit at investigation: v0.15.0, first public release 2026-05-13)

---

## 1. Executive Summary

Lavern is a TypeScript multi-agent legal system built on the Claude Agent SDK with ~67 system-prompt agents (59 specialists + 7 workflow orchestrators + 1 base orchestrator) running as in-process subagents. Apache 2.0 licensed. Its substantive contribution is **not** the agent prompts but the four operational layers wrapped around them: a citation-required debate protocol, a three-layer verification stack (evaluator gate + adversarial debate + 10-pass verification pipeline), a four-implementation human-gate abstraction, and a tentative→confirmed Precedent Board with reinforcement/decay across engagements.

Across four research passes we identified **12 patterns** worth adopting in Spaarke, ranging from very-high-value items (Precedent Board, bounded evaluator-gate loop, mechanical citation verifier) to lower-priority but cheap wins (`tabulate` workflow pattern, `decline_to_find` tool verb). Most are not portable as code — lavern is TypeScript on Node, Spaarke is .NET 8 on Azure — but the **patterns, data shapes, and design contracts** port cleanly to our Dataverse + Azure AI Search + Cosmos + Service Bus stack. In several places (storage substrate, hybrid retrieval, multi-tenancy, audit trail durability) our infrastructure is strictly better than lavern's, so adoption means borrowing the shape, not the implementation.

We also confirmed lavern seeds **5 academic legal NLP datasets** (CUAD, MAUD, ACORD, UNFAIR-ToS, LEDGAR) downloaded at install time from HuggingFace and GitHub. Three are CC BY 4.0 (commercial-use-clean with attribution); two are CC BY-SA 4.0 (ShareAlike — legal review required). CUAD's 510 commercial contracts × 41 clause types and MAUD's 152 merger agreements × 92 deal points are the highest-value content for Spaarke and should populate a system-owned cross-tenant index in Azure AI Search alongside CUAD's 41-clause taxonomy as a Dataverse-backed structured taxonomy.

The impact surface is broader than just Insight Engine and Action Engine. Cross-subsystem analysis identifies impact on BFF Platform, JPS Playbook System, Tool Registry, Dataverse Schema, Background Jobs, AI Search indexes, Cosmos graph, Chat/SprkChat, Office Add-ins, PCF controls, Power Pages, SharePoint Embedded ingest, MCP Server, Self-Service Registration, Email Wizard, Demo Environment, and Compliance/Audit.

**Recommended next moves:** (1) preserve lavern artifacts via fork-to-org + mirror clone + curated file vault before the upstream repo's accessibility can change, (2) ratify three new ADRs (Precedent Board, EvaluatorGate primitive, GateResolver interface) before any code work begins, (3) execute the sequenced adoption plan in §11.

---

## 2. Investigation Methodology and Sources

### Research passes conducted

| Pass | Scope | Output |
|---|---|---|
| **Pass 1** | Lavern architecture: runtime model, debate protocol, three verification layers, human gate, provider abstraction, document ingestion, well-designed patterns, missing/weak items | Researcher memory: `.claude/agent-memory/researcher/lavern-multi-agent-legal-system.md` |
| **Pass 2** | Real-time streaming + flow visualization, full agent/workflow inventory, cross-checking flow detail, legal reference data | Findings inline in this document |
| **Pass 3** | Seeded datasets (CUAD/MAUD/ACORD/UNFAIR-ToS/LEDGAR) — where they live, formats, licenses, usage | Researcher memory: `.claude/agent-memory/researcher/lavern-seeded-datasets.md` |
| **Pass 4** | Precedent Board verification — was it real or aspirational | Researcher memory: `.claude/agent-memory/researcher/lavern-precedent-board.md` |

### Inventory passes conducted (Spaarke side)

| Pass | Scope | Output |
|---|---|---|
| **Spaarke A** | JPS scope catalog inventory (Actions, Skills, Knowledge, Tools, Playbooks) and existing BFF streaming infrastructure | Inline findings (see §6 cross-subsystem map) |

### Source files in lavern referenced

Pass 1, 2, 3, 4 between them cited specific files in [AnttiHero/lavern](https://github.com/AnttiHero/lavern):
- `README.md`, `NOTICE`, `CHANGELOG.md`, `CONNECTORS.md`
- `docs/architecture-spec.md`
- `src/orchestrator.ts`, `src/api/ws-handler.ts`
- `src/agents/definitions.ts`, `src/agents/profiles.ts`, `src/agents/prompts/*.ts` (67 files)
- `src/mcp/tools/debate-board.ts`, `src/mcp/tools/evaluator-gate.ts`, `src/mcp/tools/approval-gate.ts`, `src/mcp/tools/grounding-verifier.ts`, `src/mcp/tools/verification-engine.ts`, `src/mcp/tools/knowledge-base.ts`, `src/mcp/tools/memory-system.ts`
- `src/gates/gate-resolver.ts`, `src/hooks/human-gate.ts`
- `src/claw/precedent-board.ts`, `src/claw/curator.ts`, `src/claw/index.ts`, `src/claw/types.ts`
- `src/types/verification.ts`, `src/types/debate.ts`
- `src/workflows/templates/*.ts` (9 files)
- `src/providers/types.ts`
- `src/documents/parser.ts`, `src/documents/sanitize-text.ts`
- `src/events/event-bus.ts`
- `scripts/seed-knowledge-base.ts` (884 lines)
- `viz/src/working/WorkingView.tsx`, `viz/src/working/components/PhaseStrip.tsx`
- `src/config.ts`
- `tests/unit/precedent-board.test.ts`, `tests/unit/claw-precedent-lifecycle.test.ts`, `tests/unit/claw-reader-precedent.test.ts`, `tests/unit/claw-curator.test.ts`

---

## 3. Lavern System Overview — Honest Framing

### What it is

A Node/TypeScript runtime layered on top of `@anthropic-ai/claude-agent-sdk`. The orchestrator (`src/orchestrator.ts`) makes a single `query()` call passing `agents: agentDefinitions` and `mcpServers: { shem: ... }`. All "agents" are SDK subagents running **in the same process**; they communicate via in-process MCP tool calls that mutate a shared `SessionState`. A WebSocket event bus (`ShemEventBus`, a Node EventEmitter) streams events to a React/Vite dashboard. SQLite + FTS5 backs persistence. Fastify hosts the API. There's a SwiftUI macOS menubar app and a "Clawern" 28-module folder-watching daemon for autonomous mode.

### What "67 agents" actually means

Each agent is a **specialized system prompt with its own role, MCP tool permissions, and slot in the debate protocol**. All 67 run on the same underlying frontier LLM (Claude or Mistral). At the bottom of the stack it's a single LLM with 67 distinct configurations, not 67 independent models.

**The substantive work is the four layers wrapped around the prompts:**
1. The debate protocol (citation-required, challenger-must-cite)
2. Three-layer verification (evaluator gate → adversarial debate → 10-pass pipeline) plus a separate mechanical grounding verifier
3. Human gates (`GateResolver` interface with 4 implementations)
4. **Precedent Board** — persistent memory across engagements with reinforcement/decay/promotion lifecycle

### What it is NOT (honest framing)

- **NOT a benchmarked legal-AI system.** README explicitly: "treat the engineering as the contribution and the legal-quality claims as a hypothesis." 1,677 tests but no public legal-quality benchmark.
- **NOT a curated legal reference corpus.** Marketing's "huge repository of legal reference data" is aspirational. What ships: a seed script that downloads 5 academic NLP datasets at install time, plus 6 document templates and 7 inline heuristic modules.
- **NOT enterprise-ready storage.** Precedent Board persists to `~/.lavern/precedents.json`. Knowledge base is SQLite + FTS5 only — no vector retrieval. Auth/Stripe/OAuth gated behind a feature flag; default user is `local-user`.
- **NOT a true DAG flowchart UI.** The "live flowchart" in demos is `PhaseStrip.tsx` — a hardcoded 11-phase horizontal strip rendered with `<div>` dots. No React Flow, no D3, no Mermaid.
- **NOT durable.** No task queue. In-process event bus loses work on restart.
- **NOT EU-isolated as marketed.** README + code admit `src/api/routes/challenge.ts` instantiates Anthropic directly even when `LAVERN_PROVIDER=mistral`.

### Calibrating expectations

For Spaarke this means: we're not behind a curated production legal-AI platform. We're studying a well-designed runtime + governance scaffold with weak content and weak persistence. Our infrastructure (Dataverse, AI Search, Cosmos, Service Bus, Auth v2 SSO, multi-tenant from day one) is strictly better positioned. What lavern hands us is **patterns and shapes**, not a head start.

---

## 4. Patterns Inventory (Ranked)

| # | Pattern | Engine/Subsystem | Effort | Value | New ADR? |
|---|---|---|---|---|---|
| **1** | **Precedent Board** — three-state lifecycle, hybrid retrieval dedup, decay, promotion gate, drift detection | Insight Engine + Platform | ~3 weeks | **Highest** | Yes |
| **2** | **Bounded evaluator-gate loop** — different model tier, max 2 revisions, graceful degradation | JPS primitive (both engines) | ~1 week | Very high | Yes |
| **3** | **Mechanical citation verifier** — zero-LLM substring/section-ref check | Insight Engine | 2–3 days | High | No |
| **4** | **Playbook flow UI component** — context-pane consumer of existing SSE stream | UI (Spaarke.UI.Components) | 2–3 days | High (visible) | No |
| **5** | **GateResolver interface + 5 gate types** | Action Engine + Platform | 3–4 days | High | Yes |
| **6** | **Evidence-required, runtime-enforced** invariants on tools | Insight Engine + Tool Registry | 1 day | High | No |
| **7** | **`decline_to_find` as first-class tool verb** | Insight Engine + Chat | 1–2 days | Medium-high | No |
| **8** | **`phasePermissions.denyTools`** per workflow phase | Action Engine + JPS | 2 days | Medium | No |
| **9** | **Seed-data ingestion** — CUAD + MAUD into AI Search; CUAD taxonomy into Dataverse | Knowledge / Insight Engine | 1–2 weeks | Medium-high | No |
| **10** | **Ingest sanitization + audit log** (SMAC-L1 equivalent) | Platform | 2–3 days | Medium | No |
| **11** | **Tier-based provider abstraction** | JPS catalog + Platform | 3–4 days | Low today, high later | Yes |
| **12** | **`tabulate` workflow pattern** | JPS playbook library | 1–2 days | Low-medium quick win | No |

---

## 5. Pattern Details — Full Description per Item

### 5.1 Precedent Board (Pattern #1)

**What lavern does** — `src/claw/precedent-board.ts` (478 lines), `src/claw/curator.ts` consolidation pass, `src/claw/index.ts:660-690` heartbeat promotion, `src/mcp/tools/memory-system.ts:58-110` `PrecedentEntry` + status enum. Test coverage in `tests/unit/precedent-board.test.ts` (455 lines).

Four behaviors:
1. **Persistent across engagements** — stored at `~/.lavern/precedents.json`, atomic writes, per-client `dir` isolation, singleton `getPrecedentBoard()` (`precedent-board.ts:102-108, 125-127, 462-472`)
2. **Recurring findings reinforced** — `reinforce()` dedups via SHA-256 of `findingType:evidence[0]`, increments `timesUsed`, bumps `effectivenessScore += scoreDelta * 0.2`, appends outcome (`precedent-board.ts:316-344`, called from `indexFindings` line 168)
3. **Stale ones decay** — `decay()` runs ≤1×/day from heartbeat. `daysInactive > decayDays` → `effectivenessScore *= 0.95`; `daysInactive > decayDays * 6` → `deprecated = true` (`precedent-board.ts:352-386`)
4. **Tentative → confirmed promotion** — `PrecedentStatus = 'tentative' | 'confirmed' | 'deprecated'`. Curator's `consolidationPass`: `timesUsed >= CONFIRM_THRESHOLD` (default 5) AND `outcomes.every(o => applied && verificationPassed)` → heartbeat calls `markConfirmed` (`curator.ts:46-48, 382-422`; `precedent-board.ts:290-300`)

Bonus: **drift detection** — `negativeOutcomes >= 2` enters `decision.driftDetected` and surfaces as notification rather than auto-deprecating (`curator.ts:407-413`).

**Weaknesses in lavern's implementation:**
- SHA-256 string dedup — semantically different wordings create duplicate precedents
- Single JSON file storage — no schema, no indexes, no transactions beyond atomic file replace
- `outcomes[]` FIFO-capped at 50 — older evidence silently discarded
- Confirmation doesn't bump `effectivenessScore` — only flips `status`
- Single-tenant by design

**Why it matters for Spaarke**

The Insight Engine's existing Fact / Observation / Inference design (per `projects/ai-spaarke-insights-engine-r1/design.md`) has no layer **above** Observations for cross-matter patterns that accumulate evidence and get promoted to durable, citable rules. Precedent Board adds that layer:

```
Fact (direct query)
  ↓
Observation (per-matter, evidence-cited, indexed in AI Search)
  ↓
Precedent (cross-matter pattern, lifecycle-managed)   ← new layer
  ↓
Inference (synthesized on-demand, can cite Precedents)
```

This addresses three real gaps:
- Today Inferences re-derive from raw Observations every query — no memory that "this M&A clause configuration historically settles at ~$X discount"
- No mechanism for SMEs to confirm or reject patterns the system surfaces
- No way to surface "we've seen this 47 times consistently — this is now a firm-level precedent"

**What we'd change vs lavern**

| Lavern weakness | Spaarke replacement |
|---|---|
| SHA-256 string dedup | Azure AI Search vector + BM25 hybrid match for semantic dedup |
| `~/.lavern/precedents.json` | Dataverse `sprk_precedent` entity for queryable structured state + Cosmos linkage graph |
| `outcomes[]` FIFO-capped at 50 | Full audit trail via Dataverse activity history; no truncation |
| Confirmation doesn't bump effectiveness score | Bump on confirm; recompute on every reinforcement |
| Single-tenant | Multi-tenant via standard Dataverse security model |
| No graph between Precedents | Cosmos edges between related Precedents |
| File-based persistence | Atomic transactions, indexed queries, OData filters |

See §9.1 for `sprk_precedent` schema proposal. See §10.1 for ADR skeleton.

### 5.2 Bounded evaluator-gate loop (Pattern #2)

**What lavern does** — One concrete flow from the `review` workflow template (`src/workflows/templates/review.ts:17-95`):
1. Specialist posts finding via `post_finding` (`src/mcp/tools/debate-board.ts`) — evidence array required at schema **and** runtime
2. Orchestrator calls `run_evaluator_gate(specialist_role, step)` (`src/mcp/tools/evaluator-gate.ts:24-58`), emits `evaluator_gate_run` event with `revisionNumber`
3. Orchestrator dispatches `evaluator` subagent — **architectural rule: must be a different model tier** than the specialist (e.g., specialist on opus → evaluator on sonnet)
4. Evaluator returns `{pass, score 0-1, failure_reasons, revision_suggestions}`
5. `record_evaluation_result` (`evaluator-gate.ts:61-100+`) persists `EvaluatorResult` into `session.genericWorkflow.evaluatorResults`, increments `gw.revisionCount` on fail
6. **Bounded**: `DEFAULT_MAX_ITERATIONS = 2` (`src/mcp/tools/quality-check.ts:26`). Template honors `maxRevisionLoops:2`. On fail-within-budget the orchestrator re-dispatches the specialist with `revision_guidance`. On fail-at-limit: **proceed with quality gaps flagged in the deliverable** (graceful degradation, not abort) (`quality-check.ts:163-179`)

**Why it matters for Spaarke**

Today JPS playbooks run analysis without independent re-evaluation. A bounded cross-check loop with model-tier separation is the cleanest single quality lever in lavern — it directly raises confidence on each Inference, Observation, or Action output without requiring multi-agent orchestration.

**What we'd change vs lavern**

Lavern's loop is fine as-is. The Spaarke implementation maps to a new JPS Action category — `EvaluatorGate` — that wraps any analysis Action. Playbook composition becomes:

```
ACT-001 Contract Review → EvaluatorGate(different model tier, maxRevisions=2) → RedFlagDetector → ...
```

Three things to add on our side:
- Persist `EvaluatorResult` to Dataverse (`sprk_evaluator_result` entity — see §9.2)
- Enforce model-tier separation at scope-resolution time (refuse to run if specialist and evaluator share a deployment)
- Surface the revision loop in the playbook flow UI (Pattern #4) so users see "Specialist v1 → failed eval (score 0.6) → revised → Specialist v2 → passed eval (score 0.85)"

See §10.2 for ADR skeleton.

### 5.3 Mechanical citation verifier (Pattern #3)

**What lavern does** — `src/mcp/tools/grounding-verifier.ts` is a **zero-LLM mechanical citation checker**:
- Regex-extracts citation references (`Section 5.2`, `Clause`, `Article`, etc.) and quoted text fragments (≥8 chars)
- Substring-matches against parsed document
- Fuzzy sliding-window fallback (capped at 10K chars to prevent DoS)
- Maintains a "common boilerplate" half-credit list to avoid penalizing standard legal language

Runs **before** any LLM verification step. Catches hallucinated citations for free.

**Why it matters for Spaarke**

Our Insights Agent returns Inferences with evidence references. Today there's no mechanical check that the cited evidence actually contains the claim's quoted text. The Insight Engine's "honesty contract" (per `design.md`) needs an enforcement mechanism, not just a principle.

**What we'd implement**

A new deterministic post-step in `InsightsResolverService` that runs after the Insights Agent produces a response but before returning to the caller. Same algorithm: regex extraction, substring match, sliding-window fuzzy match, 10K char DoS cap, boilerplate list. Failed citations get either stripped (silent) or annotated (`[citation could not be verified]`) depending on configuration.

This is also broadly applicable beyond Insights — any AI output with citations (Chat responses, Playbook outputs, RedFlagDetector findings) should run through the same verifier. Implementation should live as a shared primitive: `Sprk.Bff.Api/Services/Ai/CitationVerification/GroundingVerifier.cs`.

### 5.4 Playbook flow UI component (Pattern #4)

**What lavern does** — `viz/src/working/components/PhaseStrip.tsx` (lines 32-196) renders a horizontal strip of 11 hardcoded phase tokens with `<div>` dots and color tokens. The "flow" feel comes from Motion transitions and `recharts` for stats. No graph library. Sequential, not DAG.

The dashboard subscribes to WebSocket events via `attachEventStream` (`src/api/ws-handler.ts:63-193`), which forwards events from a per-session `ShemEventBus` (`src/events/event-bus.ts:83`). Late-joiners replay from index. ~50 event types in a single discriminated union.

**What Spaarke has today (excellent news)**

We already have the streaming infrastructure end-to-end:
- **Transport**: SSE via `ServerSentEventWriter.cs`, `useSseStream.ts` (canonical fetch + ReadableStream consumer), `SseClient.ts` (Office add-in variant)
- **Per-node execution events**: `PlaybookStreamEvent` with full set — `RunStarted` / `NodeStarted` / `NodeProgress` / `NodeCompleted` / `NodeSkipped` / `NodeFailed` / `RunCompleted` — emitted by `PlaybookOrchestrationService.ExecuteAsync` (`src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs:65`)
- **Endpoint**: `POST /api/ai/playbooks/{id}/execute` SSE-streams the above events

**What's missing**: the React component that consumes the stream and renders progress.

**What we'd implement**

A new component in `src/client/shared/Spaarke.UI.Components/src/components/PlaybookExecutionFlow/`:
- Accepts a `playbookId`, `runId`, and `accessTokenGetter`
- Renders the playbook's nodes (we have the graph from `playbook.nodes`) as a sequential strip OR top-down list
- Subscribes to `/api/ai/playbooks/{id}/execute` SSE stream via `useSseStream`
- Maps event types to visual state: `NodeStarted` → highlight + spinner, `NodeProgress` → inline content preview, `NodeCompleted` → checkmark + output, `NodeFailed` → red + error message, `NodeSkipped` → gray + reason
- Mounted in context pane (workspace), PlaybookBuilder canvas, SprkChat, Office Add-ins

For true DAG rendering later (parallel branches, conditional routing visualization), add React Flow as a v2 enhancement. Don't pre-build it; lavern's phase-strip approach is sufficient for what users actually saw demoed.

### 5.5 GateResolver interface (Pattern #5)

**What lavern does** — `src/gates/gate-resolver.ts` defines a `GateResolver` interface with four implementations:
- `ReadlineGateResolver` — CLI prompt
- `AsyncGateResolver` — Promise resolved by `POST /api/sessions/:id/gate`, 5-min default timeout → auto-reject
- `AutoApproveGateResolver` — for tests
- `WebhookGateResolver` — agent-to-agent callback

Five gate types: `ethics_critical`, `meaning_critical`, `final_delivery`, `engagement_acceptance`, `team_selection` (per `src/types/debate.ts` and `src/workflows/templates/*.ts`).

`src/mcp/tools/approval-gate.ts` delegates to `session.gateResolver.resolve()`. `src/hooks/human-gate.ts` is currently passive — records `session.triggeredGates` but does not actually block.

**Why it matters for Spaarke**

Our Action Engine's approval-gate design is currently a one-paragraph principle in `action-engine-overview.md` §10. As Actions ship to multiple surfaces (workspace, mobile, Teams, M365 Copilot, agent-to-agent), each surface needs the same approval primitive without re-implementing the contract.

**What we'd implement**

```csharp
public interface IGateResolver
{
    Task<GateDecision> ResolveAsync(GateRequest request, CancellationToken ct);
}

public sealed class GateRequest
{
    public Guid CorrelationId { get; init; }
    public GateType Type { get; init; }            // ethics, meaning, final_delivery, engagement_acceptance, team_selection, custom
    public string Title { get; init; }
    public string Body { get; init; }
    public IReadOnlyList<EvidenceReference> Evidence { get; init; }
    public TimeSpan Timeout { get; init; }          // default 5 min
    public IReadOnlyList<string> AuthorizedApproverRoles { get; init; }
    public ApprovalSurfaceHint SurfaceHint { get; init; }  // workspace, teams, mobile, email, in-chat
}

public sealed class GateDecision
{
    public bool Approved { get; init; }
    public Guid? ApproverUserId { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset DecidedAt { get; init; }
    public GateDecisionPath Path { get; init; }   // approved, rejected, auto-rejected-timeout, overridden
}
```

Implementations:
- `DataverseQueueGateResolver` — writes a `sprk_gate_approval` record, polls or webhook-resumes
- `InteractiveGateResolver` — in-chat / context pane card with approve/reject buttons via existing SSE
- `WebhookGateResolver` — agent-to-agent callbacks
- `AutoApproveGateResolver` — for tests + opt-in low-risk actions

See §9.3 for `sprk_gate_approval` schema. See §10.3 for ADR skeleton.

### 5.6 Evidence-required, runtime-enforced (Pattern #6)

**What lavern does** — `src/mcp/tools/debate-board.ts` `post_finding` tool requires `evidence: string[]` `min(1)` in Zod **and** re-checks at the handler. Empty evidence is rejected even if Zod is bypassed (belt-and-suspenders).

**Why it matters for Spaarke**

Type contracts get bypassed by tests, direct callers, or future code that constructs payloads programmatically. For legally load-bearing invariants ("every Inference must cite ≥1 Observation"), schema validation is necessary but not sufficient.

**What we'd implement**

Apply to:
- `IFindComparableMattersTool`, `IGetMatterFactsTool`, `IAssessEvidenceSufficiencyTool` (Insights)
- Any future tool whose output is meant to be evidence-grounded
- All Tool Registry handlers that produce findings, observations, or inferences

Pattern: handler entry-point method asserts `result.Evidence?.Count > 0`, throws `EvidenceRequiredException` otherwise. Compose with a shared `EvidenceGuard.Validate(result)` static method to avoid duplication.

### 5.7 `decline_to_find` as first-class tool verb (Pattern #7)

**What lavern does** — `decline_to_find` is its own MCP tool, not a prompt-engineered hope. It routes to a YELLOW + human-review path. Makes "I don't know" an explicit affordance.

**Why it matters for Spaarke**

We have evidence-sufficiency rules conceptually ("need ≥12 comparable matters for predict-matter-cost"), but they're enforced inside `IAssessEvidenceSufficiencyTool` reasoning — an LLM call that can be coerced. Promoting "insufficient evidence" to an explicit tool the agent invokes removes the temptation to fabricate inferences from sparse data.

**What we'd implement**

New tool `IDeclineToFindTool` exposed to the Insights Agent. Returns a structured `DeclineResponse`:

```csharp
public sealed class DeclineResponse
{
    public DeclineReason Reason { get; init; }        // insufficient_evidence, ambiguous_request, out_of_scope, conflicting_evidence
    public string Explanation { get; init; }
    public int? MinimumEvidenceNeeded { get; init; }  // e.g., "need ~10 more comparable matters"
    public IReadOnlyList<string> SuggestedActions { get; init; }
    public ConfidenceLevel ConfidenceInDecline { get; init; }
}
```

UI rendering: yellow card (not red error, not green success) with clear "system declined to find because…" framing. Chat surfaces the same way.

### 5.8 `phasePermissions.denyTools` per workflow phase (Pattern #8)

**What lavern does** — Workflow templates declare which tools are forbidden per phase. Examples from `src/workflows/templates/verification.ts:114-156` — can't `request_approval` during intake; can't `post_finding` during report-compilation. Mechanically enforced at MCP tool dispatch, not prompt-coached.

**Why it matters for Spaarke**

Action Engine has clear phases (Build, Schedule, Execute, Approve, Deliver). Without mechanical phase discipline, the probabilistic Builder Agent can accidentally call execution tools mid-authoring, or Execute can re-enter Build mode. Phase deny-lists prevent that class of bug.

**What we'd implement**

Add to JPS Action manifest schema:

```jsonc
{
  "phases": [
    {
      "name": "build",
      "denyTools": ["execute_action", "send_email", "create_record"]
    },
    {
      "name": "execute",
      "denyTools": ["modify_manifest", "request_template"]
    }
  ]
}
```

`PlaybookExecutionEngine` enforces deny-lists at `ToolHandlerRegistry` dispatch time. Violation throws `PhaseToolDeniedException` with a clear message naming the phase and the forbidden tool.

### 5.9 Seed-data ingestion — CUAD + MAUD into AI Search (Pattern #9)

**What lavern does** — `scripts/seed-knowledge-base.ts` (884 lines) downloads 5 datasets from HuggingFace + GitHub on first run, caches in `./data/seed-cache/`, bulk-inserts into SQLite FTS5 knowledge base. Used purely as **runtime RAG retrieval corpora** via the `search_knowledge_base` MCP tool. Owned by `__system__` user with `is_global=1` — cross-tenant reference data.

The 5 datasets:

| Dataset | Source URL | License | Content | Spaarke value |
|---|---|---|---|---|
| **CUAD** | `github.com/TheAtticusProject/cuad/raw/main/data.zip` | CC BY 4.0 | 510 commercial contracts × 41 clause types | **HIGH** — directly feeds RedFlagDetector + KNW-010 Red Flags Catalog |
| **LEDGAR** | HF `coastalcph/lex_glue` config `ledgar` | **CC BY-SA 4.0** | 60K contract provisions × 98 clause types | HIGH — broad clause taxonomy; legal review required |
| **UNFAIR-ToS** | HF `coastalcph/lex_glue` config `unfair_tos` | **CC BY-SA 4.0** | 5.5K ToS sentences × 8 unfairness labels | Medium — consumer-contract red flags; legal review required |
| **MAUD** | HF `theatticusproject/maud` | CC BY 4.0 | 152 merger agreements × 92 deal points | Low today — only if M&A Actions added |
| **ACORD** (Atticus Clause Retrieval Dataset, NOT insurance forms) | HF `theatticusproject/acord` | CC BY 4.0 | Clause retrieval IR benchmark (BEIR format) | Low — benchmark our retrieval, not content |

ContractNLI was removed 2026-05-11 (CC BY-NC-SA 4.0 incompatible with Apache 2.0).

**What we'd implement**

Build a Spaarke seed-data ingestion job modeled on lavern's pattern:
- Single idempotent C# console app or Azure Function (we'd port from TypeScript)
- Per-dataset function: download → cache locally → parse → bulk-insert
- `--force` to re-seed, skip-if-already-present by default
- Local cache directory for raw downloads so re-runs are fast
- Target: Azure AI Search index `spaarke-reference-clauses` (system-owned, cross-tenant)
- Index has hybrid vector + BM25 (strictly better than lavern's FTS5-only)
- Per-document metadata: `{ source, clauseType, allLabels, jurisdiction?, contractTitle }` so retrieval hits stay filterable and citable

Additionally: surface CUAD's 41 clause types as a **structured Dataverse taxonomy** (entity: `sprk_clausetype`) referenced by `TL-007 RedFlagDetector` and enrich `KNW-010 Red Flags Catalog` to reference it.

**Scope phasing**:
- Phase 1: CUAD + MAUD (both CC BY 4.0, clean)
- Phase 2: ACORD (CC BY 4.0) for retrieval benchmarking
- Phase 3 (post legal review): UNFAIR-ToS + LEDGAR (CC BY-SA 4.0 — internal RAG indexing likely OK; redistributing modified versions triggers ShareAlike)

### 5.10 Ingest sanitization + audit log (Pattern #10)

**What lavern does** — `src/documents/sanitize-text.ts` (SMAC-L1) strips zero-width Unicode, HTML comments, ANSI escapes **before any LLM sees the doc**, with an audit log of what was removed. Prompt-injection defense as a build-time invariant.

**Why it matters for Spaarke**

Every Spaarke surface ingests external text:
- SharePoint Embedded uploads
- Email Communication Solution
- Document attachments
- AI Search index population
- Chat (user-pasted content)
- Action Engine triggers (event payloads from Webhooks)

Without a canonical sanitization layer, every ingest path is its own attack surface for prompt injection.

**What we'd implement**

`src/server/api/Sprk.Bff.Api/Services/Ai/IngestSanitization/`:
- `ISanitizer` interface — `SanitizationResult Sanitize(string input)` returning cleaned text + audit log of removals
- `Smacl1Sanitizer` default implementation — strips zero-width Unicode (U+200B–U+200F, U+202A–U+202E, U+2060–U+206F, U+FEFF), HTML comments, ANSI escapes, control chars
- Audit log entries written to a `sprk_sanitizationaudit` table (or Application Insights custom event) — feed into security telemetry
- Required by all AI-facing ingest paths: Insights, Action triggers, Chat, Playbook execution, RedFlagDetector, all document-parsing tools

### 5.11 Tier-based provider abstraction (Pattern #11)

**What lavern does** — `src/providers/types.ts`: `LLMProvider = 'anthropic' | 'mistral' | 'local' | 'managed'`. Every agent declares `costTier: 'opus' | 'sonnet' | 'haiku'`. `resolveModel(tier, provider)` maps tier per provider at runtime (`opus` → `mistral-large-latest` / `gemma3:27b`). Agent specs stay portable across providers.

**Why it matters for Spaarke**

Today our JPS scopes hard-code model deployment names (e.g., `gpt-4o-deployment-east-us-2`). As we add Mistral, Azure AI Foundry agents, or local options, we'll have to refactor every scope. Cost basis to add tier abstraction now is low; cost to retrofit it later is high.

**What we'd implement**

- JPS scope schema: replace `modelDeployment: string` with `modelTier: 'premium' | 'standard' | 'fast' | 'embedding'`
- BFF config: per-environment mapping `modelTier → deployment-name` (e.g., in App Settings or Key Vault)
- `ChatClient` constructor reads tier + provider and resolves to a concrete deployment
- Migration: existing scopes get their hardcoded deployments mapped to tiers, then their JSON updated to use tier

Not urgent — Azure OpenAI only today — but worth doing **before** Pattern #2 (EvaluatorGate) ships, because EvaluatorGate requires "different model tier" enforcement at scope resolution.

See §10.4 for ADR skeleton.

### 5.12 `tabulate` workflow pattern (Pattern #12)

**What lavern does** — `src/workflows/templates/tabulate.ts`. Phase sequence: `intake → specialist_execution → delivered`. Deliverable is a row set (structured table), not prose. Good for "extract every clause of type X from these N contracts" or "list every party with their addresses across these documents."

**What we'd implement**

A new Playbook composition in our JPS library:
- New Playbook code: `PB-011 Tabular Extraction`
- Composes existing Actions (e.g., `ACT-001 Contract Review`) with a structured output Skill
- Output shape: `TabularResult { columns: string[], rows: object[] }` — already supported by our streaming infrastructure
- Quick win, ~1–2 days

---

## 6. Cross-Subsystem Impact Map

The 12 patterns touch far more than the two engines. Comprehensive impact map:

### Pattern × Subsystem matrix

| Pattern | BFF Platform | JPS Playbook | Tool Registry | Dataverse Schema | AI Search | Cosmos | Background Jobs | Chat / SprkChat | PCF Controls | Office Add-ins | Power Pages | MCP Server | Auth | Compliance / Audit |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 Precedent Board | ✓ | ✓ | | **NEW** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | | ✓ | | ✓ |
| 2 EvaluatorGate | ✓ | **NEW** | ✓ | NEW | | | | ✓ | | ✓ | | | | ✓ |
| 3 Citation verifier | ✓ | | ✓ | | | | | ✓ | | ✓ | | ✓ | | ✓ |
| 4 Flow UI | | | | | | | | ✓ | ✓ | ✓ | ✓ | | | |
| 5 GateResolver | ✓ | | ✓ | **NEW** | | | | ✓ | ✓ | ✓ | | ✓ | ✓ | ✓ |
| 6 Evidence-required | | | ✓ | | | | | | | | | | | ✓ |
| 7 decline_to_find | | | ✓ | | | | | ✓ | ✓ | ✓ | | ✓ | | |
| 8 Phase deny-tools | | ✓ | ✓ | | | | | | | | | | | |
| 9 Seed data | ✓ | | ✓ | **NEW** | **NEW** | | | | | | | | | ✓ |
| 10 Sanitization | ✓ | | | NEW | | | | ✓ | | ✓ | ✓ | ✓ | | ✓ |
| 11 Provider tiers | ✓ | ✓ | ✓ | | | | | | | | | | | |
| 12 Tabulate | | ✓ | | | | | | | | | | | | |

(✓ = touched by pattern. **NEW** = requires new entity/index/component.)

### Subsystem-level impact summaries

**BFF Platform (`Sprk.Bff.Api`)** — 9 of 12 patterns touch it. Most additions are new services in `Services/Ai/`:
- `Services/Ai/PrecedentBoard/` — new
- `Services/Ai/EvaluatorGate/` — new
- `Services/Ai/CitationVerification/` — new
- `Services/Ai/IngestSanitization/` — new
- `Services/Ai/Gates/IGateResolver` + implementations — new
- `Services/Ai/SeedDataIngestion/` — new (could also be a separate Azure Function)

Note CLAUDE.md §10 — every new BFF service requires loading [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) and explicit placement justification in the project's `design.md`. This document is the precursor; placement justification must be written per-component when projects formalize.

**JPS Playbook System** — 4 of 12 patterns extend it directly:
- New Action category: `EvaluatorGate`
- New phase-deny-tools schema in Action manifests
- Tier-based model resolution replaces hardcoded deployments
- New playbook composition: `PB-011 Tabular Extraction`

**Tool Registry (`ToolHandlerRegistry`)** — 6 of 12 patterns extend it:
- New tool types: `IDeclineToFindTool`
- Tool metadata extended with `costTier`, `evidenceRequired`, `phaseRestrictions`
- Citation verifier registered as a post-execution hook
- Sanitizer registered as a pre-execution hook on document-ingesting tools

**Dataverse Schema** — 4 new entities required:
- `sprk_precedent` — Precedent Board state (see §9.1)
- `sprk_evaluator_result` — EvaluatorGate persistence (see §9.2)
- `sprk_gate_approval` — GateResolver queue + history (see §9.3)
- `sprk_clausetype` — CUAD-derived taxonomy (see §9.4)
- `sprk_sanitizationaudit` (optional — could go to App Insights instead)

**AI Search Indexes** — 1 new system-owned cross-tenant index:
- `spaarke-reference-clauses` — CUAD + MAUD content (Phase 1); ACORD (Phase 2); UNFAIR-ToS + LEDGAR (Phase 3 post legal review)
- Hybrid vector + BM25

**Cosmos Graph** — extended with new edge types:
- `OBSERVATION_SUPPORTS_PRECEDENT`
- `PRECEDENT_RELATED_TO_PRECEDENT`

**Background Jobs (`ServiceBusJobProcessor`)** — 2 new scheduled jobs:
- `PrecedentDecayJob` — daily heartbeat, decays effectiveness scores, marks deprecated
- `PrecedentPromotionJob` — daily consolidation pass, checks promotion thresholds

**Spaarke.UI.Components** — new components:
- `PlaybookExecutionFlow` — flow UI for context pane
- `GateApprovalCard` — for approval surfaces
- `DeclineToFindBanner` — yellow uncertainty rendering
- `PrecedentReference` — surfaces a Precedent inline with confidence + supporting Observations
- `EvaluatorGateProgress` — shows revision loop in playbook flow

**Chat / SprkChat** — surfaces 7 of 12 patterns:
- Streams playbook flow events inline
- Cites Precedents in responses (Pattern #1)
- Surfaces EvaluatorGate revision state (Pattern #2)
- Citation-verifier results render as evidence-checked badges (Pattern #3)
- Shows gate approval cards inline (Pattern #5)
- Renders DeclineToFind responses as yellow uncertainty cards (Pattern #7)
- Sanitization audit visible in dev/debug mode only (Pattern #10)

**PCF Controls** — surfaces several patterns:
- `DocumentRelationshipViewer` — could surface related Precedents
- `SemanticSearchControl` — could query Precedent Board
- Context pane in workspace hosts `PlaybookExecutionFlow`
- Approval cards surface in PCF widgets

**Office Add-ins** — same component reuse via `Spaarke.UI.Components`. Approval cards in Outlook/Word, flow UI in task panes.

**Power Pages** — external-facing surfaces could expose confirmed Precedents as customer-facing knowledge content (with appropriate tenant filtering and approval surfaces removed).

**MCP Server (Spaarke MCP)** — several patterns become MCP tools:
- `request_approval` → exposes GateResolver to Claude / external agents
- `decline_to_find` → exposes uncertainty primitive
- `verify_citation` → exposes mechanical citation verifier
- `search_precedents` → exposes Precedent Board to external agents

**Auth (`Spaarke.Auth`)** — GateResolver needs approver identity; SSO required.

**Self-Service Registration App** — current registration approval flow can be ported to `GateResolver` infrastructure.

**Email Wizard / Email Communication Solution** — Action Engine workflows that send emails get default `humanGate.required = true`, served by GateResolver.

**Demo Environment** — first landing target for all new patterns; lowest-risk validation.

**Compliance / Audit** — 7 patterns produce audit-relevant artifacts. Precedent Board + EvaluatorGate + GateResolver + Sanitization audit collectively form a powerful **AI decision audit trail** (who approved what, on what evidence, with what model verification) that is genuinely defensible for legal/regulatory review.

---

## 7. Lavern Artifacts to Vault — File List + Retrieval Plan

### The risk

Lavern's repo is currently public (Apache 2.0). If the upstream owner makes it private, deletes it, or pivots, we lose access to:
- The source files for patterns we want to port
- The `NOTICE` file required for attribution if we redistribute derived works
- The dataset URLs and seeding logic
- The agent prompt corpus

### Layered preservation strategy

**Layer 1: Fork to our GitHub org (highest priority)**

Apache 2.0 explicitly permits forking and redistribution with attribution preserved. GitHub does not delete forks when the source repository goes private or is deleted. A fork to our org is the safest single vault.

**Action**: Fork `AnttiHero/lavern` to a Spaarke org repository named e.g. `spaarke-org/lavern-reference-vault`. Mark as internal, not public, to avoid confusion about Spaarke endorsing the project. Preserve original `LICENSE` and `NOTICE` files unchanged.

**Layer 2: Local bare mirror clone**

```bash
git clone --mirror https://github.com/AnttiHero/lavern.git lavern-mirror.git
```

Stored on a Spaarke-controlled location (Azure Storage, secured share). Updated periodically.

**Layer 3: Curated file vault inside Spaarke repo**

Copy the ~40 highest-value source files into `docs/external-references/lavern/` (or similar). NOT under `src/` — these are not Spaarke source code; they are reference artifacts under their original Apache 2.0 license with NOTICE preserved.

**High-value files to vault:**

```
# Patterns
src/mcp/tools/grounding-verifier.ts        → Pattern #3
src/mcp/tools/debate-board.ts              → Pattern #6 (evidence required)
src/mcp/tools/evaluator-gate.ts            → Pattern #2
src/mcp/tools/approval-gate.ts             → Pattern #5
src/gates/gate-resolver.ts                 → Pattern #5
src/hooks/human-gate.ts                    → Pattern #5 (reference)
src/mcp/tools/memory-system.ts             → Pattern #1 (PrecedentEntry type)

# Precedent Board (Pattern #1)
src/claw/precedent-board.ts                → primary impl
src/claw/curator.ts                        → consolidation pass
src/claw/index.ts                          → heartbeat orchestration
src/claw/types.ts                          → type defs
tests/unit/precedent-board.test.ts         → behavior tests
tests/unit/claw-precedent-lifecycle.test.ts
tests/unit/claw-reader-precedent.test.ts
tests/unit/claw-curator.test.ts

# Verification / debate
src/types/verification.ts                  → 10-pass types (reference, not adopting)
src/types/debate.ts                        → debate state shapes

# Workflow templates
src/workflows/templates/review.ts          → Pattern #2 reference
src/workflows/templates/tabulate.ts        → Pattern #12 reference
src/workflows/templates/adversarial.ts     → reference (not adopting v1)
src/workflows/templates/roundtable.ts      → reference (not adopting v1)
src/workflows/templates/counsel.ts         → reference
src/workflows/templates/full-bench.ts      → reference
src/workflows/templates/pre-engagement.ts  → reference (intake patterns)
src/workflows/templates/legal-design.ts    → reference
src/workflows/templates/verification.ts    → 10-pass impl (Pattern #8 phasePermissions reference)

# Agent definitions and roster
src/agents/profiles.ts                     → 67-agent NBA2K-style profiles
src/agents/definitions.ts                  → agent definition shape
src/agents/prompts/*.ts                    → all 67 prompts (corpus reference)

# Streaming + UI
src/events/event-bus.ts                    → Pattern #4 reference (event types)
src/api/ws-handler.ts                      → Pattern #4 reference (WS forwarding)
viz/src/working/components/PhaseStrip.tsx  → Pattern #4 reference (UI shape)
viz/src/working/WorkingView.tsx            → Pattern #4 reference (dashboard host)

# Provider abstraction
src/providers/types.ts                     → Pattern #11

# Document handling + sanitization
src/documents/parser.ts                    → reference
src/documents/sanitize-text.ts             → Pattern #10

# Seed data
scripts/seed-knowledge-base.ts             → Pattern #9 (884 lines, primary)
src/mcp/tools/knowledge-base.ts            → Pattern #9 (consumption pattern)

# Config and metadata
src/config.ts                              → env-tunable thresholds reference
src/orchestrator.ts                        → top-level orchestration (overview)

# Required for attribution
LICENSE
NOTICE
README.md
CHANGELOG.md
CONNECTORS.md
docs/architecture-spec.md
```

**Layer 4: Internet Archive Wayback Machine**

Submit URLs of all high-value files to Wayback for public-record backup. One-time job; no maintenance needed afterward.

```bash
# Example
curl -X POST "https://web.archive.org/save/https://github.com/AnttiHero/lavern/blob/main/src/claw/precedent-board.ts"
```

**Layer 5: Researcher subagent memory (already done)**

The researcher subagent saved findings to:
- `.claude/agent-memory/researcher/lavern-multi-agent-legal-system.md`
- `.claude/agent-memory/researcher/lavern-seeded-datasets.md`
- `.claude/agent-memory/researcher/lavern-precedent-board.md`

Indexed in `.claude/agent-memory/researcher/MEMORY.md`. These preserve the **analysis** even if the source disappears.

### Recommended execution order

1. **Today/this week**: Fork to org (Layer 1) + Wayback submission of top-20 files (Layer 4)
2. **Within 2 weeks**: Mirror clone to Azure Storage (Layer 2) + curated file vault in docs/external-references/ (Layer 3)
3. **Quarterly**: Refresh mirror clone, re-Wayback any updated key files

---

## 8. License and Attribution Posture

### Apache 2.0 (lavern itself)

Permits commercial use, modification, distribution, patent use. Requires:
- Preserve `LICENSE` and `NOTICE` in any redistribution
- State changes in modified files
- Include attribution in any product that incorporates Apache 2.0 code

**Spaarke posture**: We are not redistributing lavern source. We are studying patterns and writing original .NET implementations. Attribution is appropriate in our docs (this document, ADRs that reference lavern patterns) but no Apache 2.0 obligations attach to our derivative .NET code.

### CC BY 4.0 (CUAD, MAUD, ACORD)

Permits commercial use with attribution. Requirements: credit creator, link to license, indicate changes.

**Spaarke posture**: We can ingest these into a system-owned AI Search index and use them as RAG retrieval corpora. Required: include attribution in any user-visible surface that returns content from these datasets (footer link, "Source: CUAD (Atticus Project, CC BY 4.0)"), and preserve the upstream NOTICE in our seed-data ingestion code.

### CC BY-SA 4.0 (UNFAIR-ToS, LEDGAR)

Adds ShareAlike — derivative works must be licensed under the same terms. This is the legal-review hinge.

**Open question**: Does internal RAG indexing constitute creating a "derivative work" that triggers ShareAlike on our index, or is it more like search engine indexing (generally accepted as fair use)?

**Spaarke posture** (provisional pending legal review): Index for **internal use only** as RAG retrieval is likely OK and analogous to internal search infrastructure. Do NOT bundle these datasets into any shipped Spaarke artifact (deployable solution, container image, exported solution package) without legal sign-off — bundling is more likely to constitute distribution and trigger ShareAlike on our wrapper.

**Action item**: Open a legal review ticket before Pattern #9 Phase 3 (UNFAIR-ToS + LEDGAR ingestion).

### Dataset URLs preservation

If the seeding script becomes unavailable, the dataset URLs themselves are documented in lavern's `NOTICE` file and reproduced in §5.9 above. Datasets are independently hosted at HuggingFace and the Atticus Project — not at risk of disappearing if lavern does.

---

## 9. Proposed Dataverse Schemas

These are **draft schemas** for inclusion in formal ADRs. Field names follow Spaarke conventions (`sprk_*` prefix, snake_case).

### 9.1 `sprk_precedent` (new entity)

Tracks cross-engagement legal patterns with lifecycle management.

| Field | Type | Notes |
|---|---|---|
| `sprk_precedentid` | GUID | Primary key |
| `sprk_name` | string (255) | Human-readable pattern label |
| `sprk_patternsignature` | string (1000) | Canonical text used for hybrid retrieval match |
| `sprk_patternembeddingid` | string (255) | Reference to AI Search embedding |
| `sprk_status` | option set | `100000000` Tentative / `100000001` Confirmed / `100000002` UnderDriftReview / `100000003` Deprecated / `100000004` Retired |
| `sprk_findingtype` | option set | Category of finding (e.g., risk-flag, clause-classification, deal-term, outcome-pattern) |
| `sprk_timesused` | int | Reinforcement counter |
| `sprk_positiveoutcomes` | int | Times applied and verification passed |
| `sprk_negativeoutcomes` | int | Triggers drift review at >= 2 |
| `sprk_effectivenessscore` | decimal | 0.0–1.0, decays over time |
| `sprk_confirmthreshold` | int | Per-precedent override of CONFIRM_THRESHOLD (default 5) |
| `sprk_firstobservedon` | datetime | |
| `sprk_lastobservedon` | datetime | Updated on every `reinforce()` |
| `sprk_lastreviewedby` | lookup → systemuser | SME confirmation/override |
| `sprk_lastreviewedon` | datetime | |
| `sprk_originatingmatter` | lookup → sprk_matter | First matter that surfaced this pattern |
| `sprk_jurisdiction` | string (50) | Optional filter |
| `sprk_practicearea` | option set | M&A, contracts, litigation, employment, etc. |
| `sprk_createdon` / `modifiedon` | (system) | |

Related tables:
- `sprk_precedent_observation` (N:N): supporting Observations
- `sprk_precedent_related` (N:N self): graph for related Precedents

### 9.2 `sprk_evaluator_result` (new entity)

Persists EvaluatorGate executions for audit + UI rendering.

| Field | Type | Notes |
|---|---|---|
| `sprk_evaluatorresultid` | GUID | PK |
| `sprk_playbookrunid` | lookup → sprk_playbookrun | Parent run |
| `sprk_nodeid` | string (100) | Which playbook node was evaluated |
| `sprk_revisionnumber` | int | 0-based revision count |
| `sprk_specialistdeployment` | string (100) | Model deployment used by specialist |
| `sprk_evaluatordeployment` | string (100) | Model deployment used by evaluator (must differ from specialist) |
| `sprk_passed` | bool | |
| `sprk_score` | decimal | 0.0–1.0 |
| `sprk_failurereasons` | memo | JSON list of reasons on fail |
| `sprk_revisionguidance` | memo | Evaluator's suggestions to specialist |
| `sprk_finalstatus` | option set | Passed / FailedWithinBudget / FailedAtLimitProceededWithGaps |
| `sprk_createdon` | (system) | |

### 9.3 `sprk_gate_approval` (new entity)

Approval queue + history.

| Field | Type | Notes |
|---|---|---|
| `sprk_gateapprovalid` | GUID | PK |
| `sprk_correlationid` | string (100) | Joins to playbook run / action instance |
| `sprk_gatetype` | option set | EthicsCritical / MeaningCritical / FinalDelivery / EngagementAcceptance / TeamSelection / Custom |
| `sprk_title` | string (255) | Display title |
| `sprk_body` | memo | Markdown body |
| `sprk_evidencejson` | memo | JSON evidence references |
| `sprk_status` | option set | Pending / Approved / Rejected / AutoRejectedTimeout / Overridden |
| `sprk_authorizedapproverroles` | string (1000) | CSV of role names |
| `sprk_decidedby` | lookup → systemuser | |
| `sprk_decidedon` | datetime | |
| `sprk_decisionreason` | memo | |
| `sprk_surfacehint` | option set | Workspace / Teams / Mobile / Email / InChat / Webhook |
| `sprk_timeoutminutes` | int | Default 5 |
| `sprk_expiresat` | datetime | |
| `sprk_createdon` | (system) | |

### 9.4 `sprk_clausetype` (new entity)

CUAD-derived structured taxonomy of 41 clause types (plus extensibility for LEDGAR's 98 if Phase 3 proceeds).

| Field | Type | Notes |
|---|---|---|
| `sprk_clausetypeid` | GUID | PK |
| `sprk_name` | string (255) | e.g., "Limitation of Liability" |
| `sprk_source` | option set | CUAD / LEDGAR / Spaarke-Custom |
| `sprk_sourcecode` | string (100) | Original dataset code |
| `sprk_riskdefaultlevel` | option set | High / Medium / Low / Informational |
| `sprk_description` | memo | What the clause is and what to look for |
| `sprk_referenceurl` | string (500) | Link to source documentation |
| `sprk_relatedknowledgesources` | string (1000) | CSV of KNW-* codes |

### 9.5 `sprk_sanitizationaudit` (optional — could go to App Insights)

If using Dataverse:

| Field | Type | Notes |
|---|---|---|
| `sprk_sanitizationauditid` | GUID | PK |
| `sprk_ingestsource` | option set | SPE / Email / Chat / Attachment / API |
| `sprk_correlationid` | string (100) | Joins to caller |
| `sprk_charactersremoved` | int | Total chars stripped |
| `sprk_removalsjson` | memo | JSON: which patterns matched, counts |
| `sprk_inputlength` | int | Original char count |
| `sprk_outputlength` | int | Sanitized char count |
| `sprk_createdon` | (system) | |

Recommendation: prefer Application Insights custom events for sanitization audit (lower cost, no Dataverse storage churn). Surface in security telemetry dashboards.

---

## 10. Proposed ADRs (Skeletons)

These are the new architectural decisions that need to be ratified before code work begins on the affected patterns. Each is a skeleton — full ADRs would be expanded under [`.claude/adr/`](../../.claude/adr/) and [`docs/adr/`](../../docs/adr/).

### 10.1 ADR-NEW-Precedent-Board

**Status**: Proposed
**Date**: 2026-05-20
**Decision**: Add a Precedent Board layer to the Insight Engine between Observation and Inference, with a three-state lifecycle (Tentative / Confirmed / Deprecated), reinforcement on recurrence, daily decay, promotion gate, and drift detection.

**Context**: The Insight Engine's existing Fact / Observation / Inference layering re-derives patterns from raw Observations on every query. There is no durable, citable, lifecycle-managed representation of cross-engagement patterns. Lavern's Precedent Board (verified real, fully implemented in `src/claw/precedent-board.ts`) addresses this gap. The shape ports cleanly to our infrastructure.

**Decision specifics**:
- Persistence: Dataverse `sprk_precedent` entity (NOT a JSON file) — see §9.1
- Dedup: Azure AI Search vector + BM25 hybrid match on `sprk_patternsignature` (NOT SHA-256 string match)
- Outcomes audit: Dataverse activity history, no truncation (NOT FIFO-50)
- Decay: daily background job in `ServiceBusJobProcessor` — `effectivenessScore *= 0.95` on stale
- Promotion: separate daily consolidation job; `timesUsed >= 5 AND all outcomes positive` → Confirmed
- Drift: `negativeOutcomes >= 2` flips to `UnderDriftReview`, surfaces in SME queue, does NOT auto-deprecate
- Multi-tenant: standard Dataverse security model; precedents scoped per-tenant by default with explicit cross-tenant publishing path

**Consequences**:
- New `sprk_precedent` Dataverse entity + 4 related tables
- New AI Search index for embeddings
- 2 new scheduled Background Jobs
- Inference layer must be updated to consult Precedent Board before synthesizing
- SME-facing review queue UI required (likely in workspace context pane)
- Compliance benefit: durable audit trail of "AI claimed this pattern, on what evidence, who confirmed it"

**Alternatives considered**:
- Skip Precedent Board, keep current 3-layer model — rejected as gap is real
- Use Cosmos exclusively (no Dataverse entity) — rejected; Dataverse needed for SME workflows + security model
- Adopt lavern's JSON file model — rejected as not enterprise-viable

### 10.2 ADR-NEW-EvaluatorGate-Primitive

**Status**: Proposed
**Date**: 2026-05-20
**Decision**: Add `EvaluatorGate` as a first-class JPS Action category that wraps any analysis Action with a bounded re-evaluation loop using a different model tier than the specialist.

**Context**: JPS playbooks today run analysis Actions without independent re-evaluation. Lavern's evaluator-gate loop (max 2 revisions, different model tier required, graceful degradation on fail-at-limit) is the cleanest single quality lever in the lavern design.

**Decision specifics**:
- New JPS Action category: `EvaluatorGate` (alongside existing Tool / AI Analysis categories)
- Schema field on EvaluatorGate: `evaluatorModelTier`, `maxRevisions` (default 2), `failureBehavior` (default `ProceedWithGapsFlagged`)
- `PlaybookExecutionEngine` enforces model-tier separation at scope-resolution time
- Persistence: `sprk_evaluator_result` Dataverse entity per evaluation
- UI: revision loop visualized in flow component (Pattern #4)
- Depends on: Pattern #11 (tier abstraction) shipping first, OR a temporary deployment-name diff check as fallback

**Consequences**:
- Higher cost per playbook run (extra LLM calls) — needs cost monitoring
- Improved confidence on findings
- New audit trail
- Refusal at scope resolution if tiers/deployments overlap

### 10.3 ADR-NEW-GateResolver-Interface

**Status**: Proposed
**Date**: 2026-05-20
**Decision**: Introduce `IGateResolver` interface with 4 implementations (Dataverse queue, interactive in-chat, webhook, auto-approve) and 5 gate types as the canonical human-gate primitive across Spaarke.

**Context**: Approval flows exist in multiple Spaarke subsystems (Action Engine principle, Self-Service Registration, future Email Wizard) without a shared abstraction. Lavern's `GateResolver` is a clean 4-implementation interface that maps directly onto .NET.

**Decision specifics**:
- Interface and types in §5.5
- Persistence: `sprk_gate_approval` entity — see §9.3
- Default timeout: 5 minutes → auto-reject
- Surface routing: based on `SurfaceHint` field + user availability (workspace > teams > email)
- Cross-cutting: all approval flows in Spaarke (Action Engine, Self-Service Registration, Email Wizard, future workflows) consume `IGateResolver`

**Consequences**:
- Replaces ad-hoc approval flows in existing subsystems
- New shared UI component (`GateApprovalCard` in Spaarke.UI.Components)
- Audit trail across approval surfaces
- MCP Server exposes `request_approval` tool for external agents

### 10.4 ADR-NEW-Provider-Tier-Abstraction

**Status**: Proposed
**Date**: 2026-05-20
**Decision**: Replace hardcoded model deployment names in JPS scopes with model tier declarations (`premium` / `standard` / `fast` / `embedding`); resolve to concrete deployments per-environment at runtime.

**Context**: Pattern #2 EvaluatorGate requires "different model tier" enforcement. Pattern #11 enables multi-provider futures (Mistral, Foundry, local). Cost basis to add now is low; cost to retrofit later is high.

**Decision specifics**:
- JPS scope schema: `modelTier` (enum) replaces `modelDeployment` (string)
- Per-environment App Configuration mapping `(provider, tier) → deployment-name`
- ChatClient constructor reads tier + provider, resolves deployment
- Migration: existing scopes have hardcoded deployments mapped to tiers; JSON updated

**Consequences**:
- Migration effort across JPS catalog
- Enables EvaluatorGate model-tier separation enforcement
- Enables future multi-provider work without scope refactor

### 10.5 ADR-NEW-Cross-Tenant-Reference-Index

**Status**: Proposed
**Date**: 2026-05-20
**Decision**: Provision a system-owned Azure AI Search index `spaarke-reference-clauses` seeded with CUAD + MAUD (Phase 1), used as cross-tenant RAG retrieval corpus by RedFlagDetector, Knowledge sources, and the Insights Agent.

**Context**: Pattern #9 — Spaarke currently has no reference legal corpus. CUAD's 510 commercial contracts × 41 clause types and MAUD's M&A deal points are clean (CC BY 4.0) and directly relevant to commercial-document Actions.

**Decision specifics**:
- New AI Search index, system-owned, hybrid vector + BM25
- Phase 1: CUAD + MAUD
- Phase 2 (legal review): UNFAIR-ToS + LEDGAR
- Per-document metadata enables source-attributed citations
- CUAD's 41 clause types surfaced as `sprk_clausetype` Dataverse taxonomy (see §9.4)
- Attribution required in any user-visible surface returning content

**Consequences**:
- New Azure infrastructure component
- New ingestion job (Azure Function or BFF console)
- License attribution surfacing required
- Legal review checkpoint before Phase 2

### 10.6 (Optional) ADR-NEW-AI-Output-Sanitization-and-Citation-Verification-Standard

**Status**: Proposed
**Date**: 2026-05-20
**Decision**: Standardize ingest sanitization (Pattern #10) and mechanical citation verification (Pattern #3) as platform-wide primitives in `Sprk.Bff.Api/Services/Ai/` that all AI-facing ingest and output paths consume.

**Context**: Without canonical platform primitives, every ingest path is its own prompt-injection attack surface and every output path is its own hallucinated-citation risk.

**Decision specifics**:
- `ISanitizer` (with `Smacl1Sanitizer` default) at all ingest boundaries
- `ICitationVerifier` (zero-LLM substring-match) at all evidence-bearing output boundaries
- Audit log to App Insights custom events (not Dataverse, to avoid storage churn)
- Mandatory for: Insights, Chat, Playbook execution, Action triggers, RedFlagDetector, document parsing

**Consequences**:
- Cross-cutting refactor to integrate primitives into existing services
- Defense-in-depth for prompt injection and hallucinated citations
- Improved security telemetry
- Falls under CLAUDE.md §10 BFF additions governance — explicit Placement Justification required in project design.md

---

## 11. Sequenced Implementation Plan

### Workstream organization

Three parallel workstreams that re-converge:

**Workstream A — Foundations (must ship before others build on them)**
- ADR ratification (10.1–10.6)
- Provider tier abstraction migration (Pattern #11)
- Platform sanitization + citation verifier primitives (Patterns #10, #3)
- Vault lavern artifacts (§7)

**Workstream B — Insight Engine extensions**
- Precedent Board (Pattern #1)
- Evidence-required runtime guards (Pattern #6)
- `decline_to_find` (Pattern #7)
- Seed-data ingestion + CUAD taxonomy (Pattern #9)

**Workstream C — Action Engine + Cross-cutting UX**
- GateResolver interface + Dataverse queue (Pattern #5)
- EvaluatorGate primitive (Pattern #2) — depends on Workstream A
- Phase deny-tools (Pattern #8)
- Playbook flow UI component (Pattern #4)
- Tabulate workflow (Pattern #12)

### Sprint-level breakdown (illustrative; assumes ~2-week sprints)

| Sprint | Workstream | Deliverables |
|---|---|---|
| **0 — Discovery & prep** | All | Fork lavern repo to org; mirror clone; Wayback submission; curated file vault in `docs/external-references/lavern/`; legal review ticket for CC BY-SA datasets |
| **1 — ADR + Foundation** | A | Ratify ADRs 10.1–10.6 with stakeholders; spike Smacl1Sanitizer; spike GroundingVerifier; spike provider tier abstraction migration plan |
| **2 — Platform primitives** | A | Ship `ISanitizer` + `Smacl1Sanitizer` (Pattern #10); ship `ICitationVerifier` + `GroundingVerifier` (Pattern #3); integrate into Insights pipeline; provider tier abstraction migration |
| **3 — Quick visible wins** | B, C | Playbook flow UI component (Pattern #4); evidence-required runtime guards (Pattern #6); `decline_to_find` tool (Pattern #7); `tabulate` playbook (Pattern #12) |
| **4 — GateResolver** | C | `IGateResolver` interface, 4 implementations, `sprk_gate_approval` entity, `GateApprovalCard` UI component; migrate Self-Service Registration to GateResolver |
| **5 — EvaluatorGate** | C | EvaluatorGate JPS Action category; `sprk_evaluator_result` entity; model-tier enforcement; UI integration in flow component |
| **6 — Precedent Board v1** | B | `sprk_precedent` entity; basic CRUD; reinforce + decay jobs; hybrid retrieval match; integration with Observation pipeline |
| **7 — Precedent Board v2** | B | Promotion gate; drift detection; SME review queue UI; Inference layer updated to consult Precedents; precedent linkage graph in Cosmos |
| **8 — Seed data + phase deny** | B, C | CUAD + MAUD ingestion into AI Search; `sprk_clausetype` taxonomy from CUAD; RedFlagDetector enrichment; phase deny-tools in JPS (Pattern #8) |
| **9+ — Phase 2 enhancements** | All | Legal review of CC BY-SA datasets; UNFAIR-ToS + LEDGAR ingestion (if approved); MCP Server tool exposure (`request_approval`, `decline_to_find`, `verify_citation`); cross-tenant precedent publishing; full DAG visualization in flow component if needed |

### Critical-path callouts

- **ADR ratification (Sprint 1)** gates everything. Get Precedent Board, EvaluatorGate, GateResolver, and Provider Tier ADRs reviewed and signed off before code work starts.
- **Provider tier abstraction (Sprint 2)** must ship before EvaluatorGate (Sprint 5) because EvaluatorGate enforces tier separation at scope resolution.
- **Sanitization + citation verifier (Sprint 2)** are platform primitives — every later pattern composes on them.
- **GateResolver (Sprint 4)** before Precedent Board v2 (Sprint 7) — Precedent Board's SME review queue uses GateResolver.
- **Legal review (Sprint 0 ticket → resolved before Sprint 9)** — gates UNFAIR-ToS + LEDGAR ingestion.

### Coordination notes

Per CLAUDE.md §10, every BFF addition requires:
1. Load `.claude/constraints/bff-extensions.md` before designing
2. Explicit Placement Justification in project design.md
3. Use `Services/Ai/PublicContracts/` facade for AI capability used from CRUD code
4. Verify publish-size impact (baseline ~60 MB compressed)
5. Verify no new HIGH-severity CVEs

Each of the new BFF services in §6 will need this discipline applied at project-level when workstreams formalize.

The coordination assessment in `projects/ai-spaarke-action-engine-r1/coordination-assessment-with-insights-engine.md` already identifies 5 coordination decisions blocking both engines. The patterns in this document add new coordination concerns:

- How do Precedents interact with Action Engine triggers? (e.g., Action that fires when a Precedent enters drift review)
- Does GateResolver subsume the Action Engine's existing approval design, or wrap it?
- Where in JPS scope schema does `EvaluatorGate` slot?

These should be reflected in the coordination assessment as new decision items.

---

## 12. Open Questions / Decisions Needed

### High priority

1. **Legal review for CC BY-SA 4.0 ShareAlike scope** — does internal RAG indexing of UNFAIR-ToS / LEDGAR trigger ShareAlike on our wrapper? Gates Pattern #9 Phase 3.

2. **Precedent confidentiality model** — Precedents are firm-level patterns derived from many matters. What's the cross-tenant publishing story? Default isolation per-tenant, opt-in publishing of confirmed Precedents to a Spaarke-curated shared pool? Or strict per-tenant always? Affects ADR 10.1 and Pattern #1 schema.

3. **Cost ceiling for EvaluatorGate** — every playbook step running an extra LLM call doubles AI cost on that step. Is this acceptable for all flows, or only for high-stakes Actions? Affects Pattern #2 default behavior.

4. **GateResolver vs existing Self-Service Registration approval** — migrate, coexist, or leave alone? Existing flow uses Power Automate. Affects Pattern #5 scope.

### Medium priority

5. **Surface for SME Precedent review** — workspace context pane, dedicated Precedent management Code Page, Teams app? Affects Pattern #1 UI deliverables.

6. **Drift detection threshold** — lavern uses `negativeOutcomes >= 2`. Is that sensitive enough for legal precedents that can shape downstream advice? Higher threshold means more drift goes unflagged; lower threshold means more false-positive drift reviews.

7. **Phase deny-tools — declarative or imperative?** — JSON schema field on Action manifests vs C# attribute on tool handlers. Affects Pattern #8 implementation pattern.

8. **Citation verifier failure mode** — strip silently vs annotate `[citation could not be verified]` vs block the entire response? Affects Pattern #3 UX.

### Lower priority

9. **Should Precedent Board be exposed to external agents via MCP Server?** — would let Claude in customer environments query confirmed Precedents. Privacy and contract concerns.

10. **Tabulate workflow surface** — does this become a user-pickable Playbook in our standard library, or a "secret" tool only Builder Agent can invoke?

11. **Seed-data refresh cadence** — datasets get updated upstream. Re-seed quarterly? Annually? On-demand?

---

## 13. Appendix A: Full Lavern Workflow Templates (reference)

From `src/workflows/templates/`:

| Template | Phase sequence | Spaarke applicability |
|---|---|---|
| `adversarial.ts` | intake → build → attack → synthesize → delivered | Maybe v2 — useful for "red-team this contract" UX |
| `counsel.ts` | intake → specialist_execution → delivered | Already covered by single-Action playbooks |
| `full-bench.ts` | intake → decomposition → workstream_execution → integration → delivered | Skip — heavy for our use cases |
| `legal-design.ts` | v4 10-step pipeline | Skip — niche |
| `pre-engagement.ts` | conflict_check → kyc → engagement_letter → engagement_acceptance → team_selection → matter_opened | Interesting — maps onto matter-intake; workflow-engine concern |
| `review.ts` | intake → specialist_analysis → evaluator_gate → plain_language_review → verification_pass → final_gate → delivered | **Pattern #2 reference template** |
| `roundtable.ts` | intake → parallel_analysis → debate → gate → synthesis → final_gate → delivered | Maybe — if multi-agent debate becomes a requirement |
| `tabulate.ts` | intake → specialist_execution → delivered (deliverable = row set) | **Pattern #12 — adopt** |
| `verification.ts` | intake → verification_pipeline (10 passes) → report_compilation → final_gate → delivered | Skip — too expensive; our evidence-sufficiency + grounding-verifier is right-sized |

---

## 14. Appendix B: Full Lavern Agent Roster (reference)

From `src/agents/profiles.ts` and `src/agents/prompts/*.ts`:

**Lawyer agents (33)**
- *Leadership (8)*: managing-partner, supervising-partner, of-counsel, innovation-partner, client-relations-partner, risk-partner, transaction-partner, litigation-partner
- *Corp/Trans (9)*: corporate-generalist, ma-specialist, contract-specialist, banking-finance, capital-markets, tech-transactions, startup-counsel, restructuring-specialist, real-estate-counsel
- *Disputes (3)*: litigation-associate, arbitration-specialist, dispute-resolution
- *Regulatory (5)*: regulatory-counsel, compliance-officer, antitrust-specialist, sanctions-specialist, public-law-counsel
- *Specialist Practice (5)*: tax-counsel, ip-specialist, privacy-counsel, employment-counsel, environmental-counsel, international-counsel
- *Juniors (3)*: junior-associate, paralegal, legal-intern

**Specialist non-lawyer agents (~22)**
- *Design/Comms (4)*: service-designer, plain-language-specialist, accessibility-specialist, design-reviewer
- *Research/Behavior (5)*: client-proxy, user-researcher, behavioral-scientist, legal-researcher, contract-reviewer
- *Ethics/Quality (5)*: ethics-auditor, meaning-guardian, synthesis-editor, red-team, transformation-specialist
- *Tech/Data (3)*: legal-engineer, cybersecurity-advisor, ai-ethics-specialist
- *Domain (4)*: fintech-specialist, healthcare-specialist, media-specialist, energy-specialist
- *Ops (1)*: project-manager

**Infrastructure agents (3)**: evaluator, ethics-reviewer, risk-pricer

**Orchestrators (8)**: orchestrator (base), orchestrator-adversarial, orchestrator-counsel, orchestrator-full-bench, orchestrator-review, orchestrator-roundtable, orchestrator-tabulate, orchestrator-verification

**Note**: `profiles.ts` references 4 additional orchestrators (`orchestrator-conductor`, `-closer`, `-professor`, `-fixer`) with no corresponding prompt file — naming drift in lavern.

**Spaarke applicability**: most of these are specific to a legal practice surface broader than Spaarke's current commercial-document focus (8 Actions in our JPS catalog). Expanding our JPS Actions to cover litigation, regulatory, IP, privacy, tax, antitrust, etc. is **opportunistic** — driven by specific customer engagement, not by mimicking lavern's roster.

---

## 15. Appendix C: Spaarke JPS Catalog (current state, for context)

From `.claude/catalogs/scope-model-index.json` (generated 2026-03-05):

**Actions (8)**: Contract Review, NDA Analysis, Lease Review, Invoice Processing, SLA Analysis, Employment Review, SOW Analysis, General Legal Doc Review

**Skills (10)**: Citation Extraction, Risk Flagging, Summary Generation, Date Extraction, Party Identification, Obligation Mapping, Defined Terms, Financial Terms, Termination Analysis, Jurisdiction & Governing Law

**Knowledge Sources (10)**: Common Contract Terms Glossary, NDA Review Checklist, Lease Agreement Standards, Invoice Processing Guide, SLA Metrics Reference, Employment Law Quick Reference, IP Assignment Clause Library, Termination & Remedy Provisions, Governing Law & Jurisdiction Guide, Legal Document Red Flags Catalog

**Tools (8)**: DocumentSearch, AnalysisRetrieval, KnowledgeRetrieval, TextRefinement, CitationExtractor, SummaryGenerator, RedFlagDetector, PartyExtractor

**Playbooks (10)**: Standard Contract Review, NDA Deep Review, Commercial Lease Analysis, Invoice Validation, SLA Compliance Review, Employment Agreement Review, SOW Analysis, IP Assignment Review, Termination Risk Assessment, Quick Legal Scan

---

## 16. Appendix D: Lavern Seeded Datasets (full detail)

See §5.9 for adoption plan. Detail:

**CUAD** — Contract Understanding Atticus Dataset
- Source: `github.com/TheAtticusProject/cuad/raw/main/data.zip`
- Format: SQuAD-style JSON in `train_separate_questions.json`
- Content: 510 commercial contracts × 41 clause types annotated
- Size: ~tens of MB
- License: CC BY 4.0 ✅
- Spaarke action: Phase 1 ingestion

**MAUD** — Merger Agreement Understanding Dataset
- Source: HuggingFace `theatticusproject/maud`
- Format: JSON rows (paginated)
- Content: 152 merger agreements × 92 deal points
- License: CC BY 4.0 ✅
- Spaarke action: Phase 1 ingestion (deferred-use; activate when M&A Actions added)

**ACORD** — Atticus Clause Retrieval Dataset (BEIR IR benchmark)
- Source: HuggingFace `theatticusproject/acord` (corpus.jsonl, queries.jsonl, qrels.tsv)
- Format: JSONL + TSV
- Content: 114 queries, 126K+ relevance judgments (lavern indexes score ≥ 2 only)
- License: CC BY 4.0 ✅
- **Disambiguation**: This is the Atticus Clause Retrieval Dataset, NOT the ACORD.org insurance forms standard
- Spaarke action: Phase 2 — for benchmarking our retrieval

**UNFAIR-ToS**
- Source: HuggingFace `coastalcph/lex_glue` config `unfair_tos`
- Content: 5.5K sentences × 8 unfairness labels
- License: **CC BY-SA 4.0** ⚠️
- Spaarke action: Phase 3 pending legal review

**LEDGAR**
- Source: HuggingFace `coastalcph/lex_glue` config `ledgar`
- Content: 60K contract provisions × 98 clause types
- License: **CC BY-SA 4.0** ⚠️
- Spaarke action: Phase 3 pending legal review

**ContractNLI** — removed from lavern 2026-05-11 (CC BY-NC-SA 4.0, incompatible with Apache 2.0). Available from upstream if needed.

---

## 17. Appendix E: Researcher Memory Files (for future investigation)

Findings preserved in:
- `c:\code_files\spaarke\.claude\agent-memory\researcher\MEMORY.md` (index)
- `c:\code_files\spaarke\.claude\agent-memory\researcher\lavern-multi-agent-legal-system.md` (Pass 1)
- `c:\code_files\spaarke\.claude\agent-memory\researcher\lavern-seeded-datasets.md` (Pass 3)
- `c:\code_files\spaarke\.claude\agent-memory\researcher\lavern-precedent-board.md` (Pass 4)

Pass 2 (streaming, agent inventory, cross-checking) findings are inline in this document.

---

## 18. Footer

**Maintained by**: ralph.schroeder@hotmail.com
**Status as of 2026-05-20**: Working analysis. To be decomposed into formal ADRs (10.1–10.6), Insight Engine `design.md` updates (Precedent Board layer), Action Engine `design.md` updates (GateResolver, EvaluatorGate), and dedicated schema specs in [docs/data-model/](../../docs/data-model/) as workstreams formalize.

**Companion documents in this project directory**:
- [`ADVANCED-AI-USE-CASE-PATTERNS.md`](ADVANCED-AI-USE-CASE-PATTERNS.md) — the six user interaction modes (synchronous review, async triage, conversational inquiry, precedent curation, in-document drafting, scheduled distillation)
- [`TEST-DATA-REQUIREMENTS.md`](TEST-DATA-REQUIREMENTS.md) — strategy for priming demo, validation, and edge-case test data

**Next actions**:
1. Approve fork-to-org + Wayback submission for lavern artifact preservation
2. Approve legal review ticket for CC BY-SA 4.0 datasets
3. Schedule ADR review session for 10.1–10.6
4. Update coordination assessment in `projects/ai-spaarke-action-engine-r1/coordination-assessment-with-insights-engine.md` to incorporate the new coordination decisions from §11

**Cross-references**:
- Insight Engine project: [`projects/ai-spaarke-insights-engine-r1/`](../ai-spaarke-insights-engine-r1/)
- Action Engine project: [`projects/ai-spaarke-action-engine-r1/`](../ai-spaarke-action-engine-r1/)
- Coordination assessment: [`projects/ai-spaarke-action-engine-r1/coordination-assessment-with-insights-engine.md`](../ai-spaarke-action-engine-r1/coordination-assessment-with-insights-engine.md)
- BFF additions governance: [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md)
- AI architecture: [`docs/architecture/AI-ARCHITECTURE.md`](../../docs/architecture/AI-ARCHITECTURE.md)
- JPS scope catalog: [`.claude/catalogs/scope-model-index.json`](../../.claude/catalogs/scope-model-index.json)
