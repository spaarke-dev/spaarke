# LAVERN Pattern Assessment — Action Engine r1

> **Date**: 2026-05-22
> **Owner**: Spaarke Engineering
> **Status**: Decision capture — Action Engine MVP scope expansion ratified (GateResolver + Phase deny-tools + Tool Registry metadata + tier abstraction)
> **Source documents**:
> - [`projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md`](../ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md) — 12 patterns from AnttiHero/lavern (Apache 2.0)
> - [`projects/ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md`](../ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md) — six user-interaction modes
> - This project's [`action-engine-overview.md`](action-engine-overview.md), [`coordination-assessment-with-insights-engine.md`](coordination-assessment-with-insights-engine.md)
> - Sister Insights Engine assessment: [`projects/ai-spaarke-insights-engine-r1/lavern-pattern-assessment.md`](../ai-spaarke-insights-engine-r1/lavern-pattern-assessment.md)

---

## 1. Purpose

Capture the analysis that led to Action Engine MVP scope expansion with LAVERN-derived deliverables (GateResolver, Phase deny-tools, Tool Registry metadata extension, tier abstraction). Preserves the "why these and not others" reasoning so future contributors can revisit the decision basis.

This document is a **companion** to:
- `action-engine-overview.md` §5 (component surfaces, including LAVERN-derived additions), §10 (phased delivery)
- `coordination-assessment-with-insights-engine.md` §4.6, §4.7, §4.8 (joint decisions)

If those docs disagree with this one, **they win**. This is the rationale archive, not the canon.

---

## 2. Action Engine current stage

Action Engine is at a **pre-pipeline, conceptual overview stage**:

- No SPEC.md, no decisions.md, no design.md yet — only the 309-line conceptual overview from 2026-05-20
- Runtime topology is **explicitly OPEN** (action-engine-overview.md §6) — architecture spike required before MVP
- MVP scope defined: manual + scheduled triggers; deterministic tools first-class; pro-code authoring; 3–5 starter templates; endpoint-filter auth
- Tool Registry explicitly named "the strategic engineering asset" (§5)
- Coordination assessment with Insights Engine (194 lines, 2026-05-20) identifies 5 joint decisions; this LAVERN assessment adds 3 more (§4.6, §4.7, §4.8)

**Key implication**: Action Engine is *earlier* than Insights Engine. LAVERN patterns can shape the design from day one — cheaper than for Insights, because Insights had design canon to revise; Action Engine has design freedom.

---

## 3. Patterns adopted in Action Engine MVP

### 3.1 GateResolver (LAVERN Pattern #5 / ADR 10.3) — **CRITICAL FOR MVP**

#### Current state

`action-engine-overview.md` §5 describes the Approval queue abstractly:
> "For Actions with `humanGate.required: true`, execution pauses at the gate. Approval records route to declared roles; UI surfaces (workspace, mobile, Teams) render pending approvals."

This describes the *what* but not the *how*. No interface, no contract.

#### Is there a real gap?

**Yes — the architectural integrity decision for Action Engine.** Without a shared interface, every approval surface (workspace, mobile, Teams, Outlook, M365 Copilot, future surfaces) would need its own approval implementation. The Action Engine MVP would either ship approval as a single-surface implementation (rebuilt 5+ times for R2+) or ship without a real approval primitive.

The lavern source code (`src/gates/gate-resolver.ts`) implements exactly the missing primitive: an `IGateResolver` interface with 4 implementations (Dataverse queue, interactive in-chat, webhook, auto-approve) + 5 gate types. Maps cleanly onto .NET.

#### Concrete change

MVP scope (per `action-engine-overview.md` §5 update and §10 LAVERN-derived additions):

```csharp
public interface IGateResolver
{
    Task<GateDecision> ResolveAsync(GateRequest request, CancellationToken ct);
}
```

Four implementations:
- `DataversePrecedentBoardGateResolver` — writes `sprk_gate_approval` record; polls or webhook-resumes
- `InteractiveGateResolver` — in-chat / context pane card with approve/reject via SSE
- `WebhookGateResolver` — agent-to-agent callbacks
- `AutoApproveGateResolver` — tests + opt-in low-risk Actions

Five gate types: `EthicsCritical`, `MeaningCritical`, `FinalDelivery`, `EngagementAcceptance`, `TeamSelection`, plus `Custom`. Default timeout 5 minutes → auto-reject.

Shared UI: `GateApprovalCard` in `Spaarke.UI.Components`. Surfaces mount it; never reimplement.

#### MVP cost

LAVERN estimates 3–4 days for the interface + 4 implementations. Reusable by other Spaarke subsystems (Self-Service Registration, Email Wizard) per LAVERN §6.

#### What this means for Insights Engine

Insights Engine's design.md §8.4 "extends existing PendingPlanManager" plan is **superseded** by GateResolver consumption (Insights `decisions.md` D-51). Phase 2+ Insights write-back paths consume the same primitive Action Engine MVP builds.

### 3.2 Phase deny-tools (LAVERN Pattern #8) — **STRONG FIT, MVP ADOPT**

#### Current state

`action-engine-overview.md` §11 has an anti-pattern: *"Do not bypass the human gate by default."* §4 conceptual model has implicit phases (authoring → schedule → execute → approve → deliver). §7.1 Builder Agent does template matching + parameter elicitation.

#### Is there a real gap?

**Yes — the Builder Agent is probabilistic and could be coerced.** No mechanical enforcement prevents the Agent from calling execute tools mid-authoring. The anti-pattern in §11 is *prompt-coached*, not *mechanically enforced*.

The lavern source (`src/workflows/templates/verification.ts` and others) declares `phasePermissions.denyTools` per workflow phase. Mechanically enforced at MCP tool dispatch, not prompt-coached.

**Action Engine's phase model maps directly to LAVERN's** — this is one of the strongest pattern fits.

#### Concrete change

MVP scope (per `action-engine-overview.md` §5 new "Phase deny-tools" subsection):

Add `phases: [{ name, denyTools: [...] }]` schema to Action Definition manifest:

```jsonc
{
  "phases": [
    { "name": "authoring", "denyTools": ["execute_action", "send_email", "create_record", "delete_record"] },
    { "name": "schedule",  "denyTools": ["execute_action"] },
    { "name": "execute",   "denyTools": ["modify_manifest", "request_template"] },
    { "name": "approve",   "denyTools": ["execute_action", "modify_manifest"] },
    { "name": "deliver",   "denyTools": ["modify_manifest"] }
  ]
}
```

`IToolHandlerRegistry` enforces deny-lists at dispatch time. Violation throws `PhaseToolDeniedException`.

#### MVP cost

LAVERN estimates 2 days. One of the highest-impact, lowest-cost LAVERN patterns for Action Engine.

### 3.3 Provider tier abstraction (LAVERN Pattern #11 / ADR 10.4) — **MVP FOUNDATION**

#### Current state

`action-engine-overview.md` §5 Tool Registry mentions "classification (deterministic vs AI, cost class, latency class, idempotency)" — but tool metadata doesn't yet declare model tier. JPS scopes hardcode model deployment names.

#### Is there a real gap?

**Yes — and it's a foundation for R2+ patterns.** LAVERN Pattern #2 (EvaluatorGate) requires "different model tier" enforcement. Without tier abstraction in JPS scope schema, EvaluatorGate cannot enforce that specialist and evaluator run on different models. Cost basis to add tier abstraction now is low; cost to retrofit later is high.

Coordination assessment §4.5 + §4.8 (new) folds tier abstraction into the joint Tool Registry metadata workstream.

#### Concrete change

MVP scope (per `coordination-assessment-with-insights-engine.md` §4.8):

Add to `ToolHandlerMetadata`:
- `ModelTier`: enum `Premium | Standard | Fast | Embedding`
- Per-environment App Configuration mapping `(provider, tier) → deployment-name`
- ChatClient constructor reads tier + provider, resolves to concrete deployment

#### MVP cost

~3–4 days as part of joint workstream. Doesn't require migration of every existing JPS scope (existing hardcoded deployments map to default tiers).

#### Why MVP (not R2)

EvaluatorGate (Pattern #2) is R2+. But if we ship MVP with hardcoded deployment names and add tier abstraction in R2, we're refactoring every scope. Cheaper to bake it in from day one — empty `ModelTier` field is harmless; absent field is the retrofit.

### 3.4 Tabulate workflow (LAVERN Pattern #12) — **MVP-OPTIONAL**

#### Current state

`action-engine-overview.md` §4 lists 15–25 planned starter templates including "cross-matter rollup, counsel performance digest, invoice approval queue summary" — several are tabulation-style by shape.

#### Is there a real gap?

**Maybe — depends on MVP starter template choices.** Without a tabulate Playbook composition, each tabulation template reinvents row-set output. The lavern source (`src/workflows/templates/tabulate.ts`) has a simple phase sequence: `intake → specialist_execution → delivered` with a row-set deliverable.

#### Concrete change

MVP scope (optional):
- New starter Playbook code: `PB-011 Tabular Extraction`
- Composes existing Actions (e.g., a query Action) with a structured output Skill
- Output shape: `TabularResult { columns: string[], rows: object[] }` — already supported by Spaarke streaming infrastructure

#### MVP cost

1–2 days. Adopt if any of the 3–5 starter templates is tabulation-style; otherwise R2.

---

## 4. Patterns deferred to R2+

### 4.1 EvaluatorGate (LAVERN Pattern #2 / ADR 10.2) — **R2+ opt-in**

#### Current state

Action Engine MVP has AI tools (summarize, extract, classify, draft) but no quality-verification step.

#### Why R2+ not MVP

- **MVP focus**: deterministic tools first-class + manual/scheduled triggers. EvaluatorGate doesn't have stakes high enough to justify cost in MVP.
- **Cost**: doubles per-step LLM cost (specialist + evaluator).
- **Dependency**: requires Pattern #11 (provider tier abstraction) to enforce tier separation — which IS in MVP, but EvaluatorGate adoption is separate.
- **Real adoption**: only valuable when AI-heavy / high-stakes Actions ship (e.g., draft external email, generate legal document, "deviates from standard cap" cases per use-case Mode 5).

#### MVP hook (optional, ~1 day)

Wire the manifest field + dispatch path in MVP, default off:

```jsonc
{
  "evaluatorGate": {
    "enabled": false,
    "evaluatorTier": "Standard",
    "maxRevisions": 2
  }
}
```

This means R2 Actions opt in by setting `enabled: true`; no architectural retrofit.

### 4.2 Flow UI (LAVERN Pattern #4) — **R2 when multi-step Actions ship**

MVP is manual + scheduled triggers — fire-and-forget. No SSE-streamed multi-step Actions where execution-progress UI matters.

R2 deliverable: `PlaybookExecutionFlow` shared component in `Spaarke.UI.Components`. Subscribes to `/api/ai/playbooks/{id}/execute` SSE. Mounted by workspace pane, embedded chat, Office add-ins. Consumed by Mode 1 (reactive review) and Mode 2 (proactive monitoring + triage) from `ADVANCED-AI-USE-CASE-PATTERNS.md`.

### 4.3 Sanitization (LAVERN Pattern #10) — **R2 consume Insights primitive**

MVP triggers are manual + scheduled — no external payload ingestion. R2 adds webhook + signal triggers; that's when sanitization matters.

Insights Engine Phase 1 builds the primitive (`ISanitizer` + `Smacl1Sanitizer`); Action Engine R2 consumes via DI (coordination assessment §4.7).

### 4.4 Citation verifier + Evidence guards (LAVERN Patterns #3, #6) — **R2 consume Insights primitives**

Action Engine AI Tools that return findings (RedFlagDetector, draft tools with citations) benefit from `GroundingVerifier` + `EvidenceGuard.Validate`. Less critical than for Insights Engine (Insights' core output is evidence-grounded; Action Engine has both deterministic and AI tools).

Insights Engine Phase 1 builds the primitives; Action Engine R2 AI Tools that ship findings consume them (coordination assessment §4.7).

### 4.5 Seed data CUAD + MAUD (LAVERN Pattern #9 / ADR 10.5) — **MVP-CONDITIONAL**

#### Conditional on MVP starter template choices

Action Engine clause-classification Tools (RedFlagDetector implied by `KNW-010 Red Flags Catalog` in existing Spaarke JPS catalog) need a reference clause taxonomy. Without one, every customer starts cold without industry baseline.

CUAD's 41 clause types + MAUD's M&A deal points feed RedFlagDetector / clause classifiers.

**If RedFlagDetector ships in MVP starter templates** → Pattern #9 Phase 1 (CUAD + MAUD ingestion, ~1–2 weeks per LAVERN) is **MVP-blocking**.

**If RedFlagDetector defers to R2** → Pattern #9 defers with it.

The MVP scope decision about which 3–5 starter templates ship determines this.

#### Concrete change (if adopted MVP)

- Single idempotent C# console app or Azure Function: download → cache locally → parse → bulk-insert into AI Search
- Target: `spaarke-reference-clauses` AI Search index (system-owned, cross-tenant)
- CUAD's 41 clause types surfaced as `sprk_clausetype` Dataverse taxonomy referenced by RedFlagDetector

---

## 5. Patterns not Action Engine's concern

### 5.1 Precedent Board (LAVERN Pattern #1 / ADR 10.1) — **Consumer only**

Action Engine doesn't create Precedents; Insights Engine does (Phase 1 scaffold, Phase 1.5 lifecycle). Action Engine AI Tools that draft content ("draft email referencing our standard position") can cite Precedents in R2+ when the Insights Phase 1.5 lifecycle ships and Precedents are populated with real data.

Cross-reference: Insights Engine `lavern-pattern-assessment.md` §3.1; LAVERN ADR 10.1.

### 5.2 `decline_to_find` (LAVERN Pattern #7) — **Insights-specific semantic**

`decline_to_find` is about *inference uncertainty* — when an Inference question has insufficient evidence to answer. Action Engine Tools that "don't find" return empty result sets (e.g., "no invoices over $50k this week") — different semantic; an empty result is a valid answer, not a decline.

Insights Engine adopts this pattern (Insights D-A24 / D-49). Action Engine doesn't need an equivalent.

---

## 6. Cross-project coordination

### 6.1 Shared primitives Action Engine builds (`IGateResolver`)

Action Engine MVP is the **implementer** of `IGateResolver` (LAVERN ADR 10.3). The primitive lives in `Sprk.Bff.Api/Services/Ai/Gates/` (proposed) and registers via DI for cross-subsystem reuse.

Consumers:
- Action Engine MVP (all approval flows)
- Insights Engine Phase 2+ (write-back paths per Insights D-51)
- Self-Service Registration (migration from existing Power Automate approval)
- Email Wizard (humanGate-required Actions sending external email)
- Future approval surfaces

### 6.2 Shared primitives Action Engine consumes

Insights Engine Phase 1 builds and Action Engine R2 consumes:

| Primitive | Location | Action Engine consumption |
|---|---|---|
| `ISanitizer` + `Smacl1Sanitizer` | `Sprk.Bff.Api/Services/Ai/IngestSanitization/` | R2 webhook + signal trigger ingestion paths |
| `GroundingVerifier` | `Sprk.Bff.Api/Services/Ai/CitationVerification/` | R2 AI Tools that return findings (RedFlagDetector, draft tools with citations) |

Both are platform primitives shared via DI. Cross-reference: coordination assessment §4.7.

### 6.3 Shared schemas — joint workstream

`ToolHandlerMetadata` extension is the canonical joint workstream (coordination assessment §4.5 + §4.8). Combined field list:

| Field | Source | Action Engine usage |
|---|---|---|
| `Classification` | §4.5 | Required on every tool |
| `CostClass` | §4.5 | Required; informs cost-attribution model (ADR-016) |
| `LatencyClass` | §4.5 | Required; informs sync vs async dispatch |
| `Idempotency` | §4.5 | Required; informs retry behavior |
| `AuthMode` | §4.5 | Required; informs auth wrapping |
| `Discoverability` | §4.5 | Required for Builder Agent semantic search |
| `ModelTier` | §4.8 / LAVERN #11 | Required on AI tools; foundation for R2+ EvaluatorGate |
| `PhaseRestrictions` | §4.8 / LAVERN #8 | Required where applicable; declares which phases must NOT dispatch this tool |
| `EvidenceRequired` | §4.8 / LAVERN #6 | Used on AI tools that return findings |

Joint workstream estimated ~3–4 days. Both projects use the extended schema for all tool registrations.

### 6.4 Runtime topology alignment

Coordination assessment §4.3 recommends Action Engine adopt the Insights Engine hybrid topology:

- **Scheduler**: Azure-native Function timer (Option C) — same Function App as Insights Track B
- **Single-step / deterministic Action execution**: BFF (Option A) — direct dispatch through `IToolHandlerRegistry`
- **Multi-step probabilistic agent loops**: BFF (Option A) — extend existing `IChatClient` + `UseFunctionInvocation` + `SprkChatAgentFactory`

**Do not introduce Microsoft Agent Framework as a separate runtime in MVP.** Phase deny-tools (Pattern #8) and GateResolver (Pattern #5) are most cleanly enforced at the BFF dispatch layer; introducing a second runtime creates a divergent enforcement path.

---

## 7. The pre-pipeline opportunity

### 7.1 Architectural integrity is cheapest before design canon exists

Action Engine has only a conceptual overview — no SPEC, no decisions.md, no design.md. Every LAVERN-derived addition lands in fresh architecture, not as a retrofit. This is the cheapest moment to adopt patterns.

Contrast with Insights Engine: had design canon (decisions.md, design.md, SPEC.md) that needed revision. Insights' LAVERN adoption required updating multiple existing documents. Action Engine's adoption just adds to a still-being-written document.

### 7.2 LAVERN patterns shape Action Engine MVP from day one

The four MVP-adopted patterns (GateResolver, Phase deny-tools, Tool Registry metadata, tier abstraction) shape the MVP architecture rather than retrofit it. Specifically:

- `IGateResolver` is **the** approval primitive from MVP onwards — no per-surface implementation
- Phase deny-tools are mechanically enforced from MVP onwards — Builder Agent's failure modes are bounded by design
- Tool Registry metadata schema is the joint canonical version from MVP — both projects share it
- `ModelTier` field is present from MVP — R2+ EvaluatorGate can be added without scope refactor

### 7.3 What we chose NOT to do — introduce Microsoft Agent Framework as a separate runtime

Considered. Rejected (for MVP) because:

- Hybrid topology (BFF + Azure-native Function) already chosen by Insights Engine; coupling helps both projects
- Phase deny-tools enforcement is most natural at our `IToolHandlerRegistry` dispatch — introducing a second runtime creates a divergent path
- GateResolver implementation is most natural in our BFF — same reasoning
- Microsoft Agent Framework maturity for our specific scenarios is not yet evaluated; the architecture spike (§6) is the proper venue for that decision, not LAVERN-driven scope

Revisit if/when a concrete scenario emerges that the existing orchestrator demonstrably cannot serve.

---

## 8. Sequencing

Per coordination assessment §5 (updated with LAVERN-derived items):

| Order | Item | Owner |
|---|---|---|
| 0 | LAVERN ADR 10.3 (GateResolver) ratified | Joint |
| 0 | Joint signal-envelope contract doc — extended for `type: "precedent"` | Both |
| 1 | Tool Registry metadata extension (§4.5 + §4.8 with LAVERN fields) | Both, executed by one |
| 1 | Action Engine runtime spike → runtime ADR | Action |
| 2 | Action Engine Placement Justification in spike output | Action |
| 3 | Action Engine MVP `/project-pipeline` (with GateResolver + Phase deny-tools + Tool Registry metadata in scope) | Action |
| 4 | Action Engine R2 (signal-triggered Monitors + sanitization + grounding-verifier consumption + flow UI + EvaluatorGate hook activation) | Action |

Net MVP expansion: **~9–11 days of work** (GateResolver 3–4 days + Phase deny-tools 2 days + Tier metadata 3–4 days + optional Tabulate 1–2 days). Comparable in scale to Insights Engine's expansion (~16–21 days), but earlier in lifecycle so cheaper overall.

---

## 9. References

- **Source patterns**: [`projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md`](../ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md)
- **Use-case modes**: [`projects/ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md`](../ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md)
- **Action Engine canon**: [`action-engine-overview.md`](action-engine-overview.md) (§5 components incl. LAVERN additions, §10 phased delivery, §11 anti-patterns)
- **Joint coordination**: [`coordination-assessment-with-insights-engine.md`](coordination-assessment-with-insights-engine.md) §4.6, §4.7, §4.8 (new); §5 sequencing (updated)
- **Sister Insights Engine assessment**: [`projects/ai-spaarke-insights-engine-r1/lavern-pattern-assessment.md`](../ai-spaarke-insights-engine-r1/lavern-pattern-assessment.md) — same lens applied to Insights Engine
- **LAVERN ADR proposals**: §10.1 (Precedent Board), §10.2 (EvaluatorGate), §10.3 (GateResolver), §10.4 (Provider Tier), §10.5 (Cross-Tenant Reference Index), §10.6 (Sanitization + Citation Verification Standard) — all in LAVERN-ANALYSIS-AND-PLAN.md
- **LAVERN source repo (Apache 2.0)**: [AnttiHero/lavern](https://github.com/AnttiHero/lavern) — preserved per LAVERN doc §7 vaulting strategy

---

*This document is the rationale archive. The canon lives in `action-engine-overview.md` and `coordination-assessment-with-insights-engine.md`. When they disagree with this doc, they win.*
