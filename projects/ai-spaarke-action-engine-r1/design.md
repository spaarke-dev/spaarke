# Spaarke Action Engine — Architecture Overview

> **Audience:** Claude Code and platform engineers consuming this as a design input.
> **Purpose:** Establish the conceptual model, component boundaries, and open architectural questions for the Action Engine before any implementation work begins.
> **Status:** Design overview. Entity names, file paths, and code identifiers below are *conceptual labels*. Implementation specs will follow. Runtime/hosting architecture is explicitly *open* — Section 6 lays out options to evaluate.

---

## 1. What the Action Engine Is

The Action Engine is the **agent creation, management, and execution surface for the Spaarke domain**. It lets users — including non-technical legal-ops users — define, configure, and run agents and automations that operate on their workspace data and operational signals. These range from a one-tap manual action ("summarize this matter") to a recurring scheduled digest ("text me 9 AM weekdays with tasks due in two days for Smith Co") to a fully event-driven proactive agent ("when an invoice arrives over $50k from an off-panel firm, route it for review with a one-paragraph rationale").

The Action Engine is its own concern. It is *not* an extension of the Insights Engine. The Insights Engine senses and emits structured signals; the Action Engine creates and manages user-defined agents that may *consume* those signals (alongside other triggers) and do something useful with them. Insights signals are one of several inputs to the Action Engine, not its defining purpose.

**The relationship to Playbooks.** Playbooks are the orchestration resource — the composition of Actions, Skills, Knowledge, and Tools that defines *how* a piece of work is done. The Action Engine is the management plane *around* Playbooks: how users author them, parameterize them, attach triggers to them, run them, monitor them, and govern them. A Playbook without the Action Engine is a static artifact; an Action without a Playbook has nothing to execute. They are layered, not the same thing.

---

## 2. Inspirations and Influences

OpenClaw is a useful reference point for two specific things:

- **Ease-of-use authoring.** OpenClaw popularized the experience of "tell the assistant what to do and it just does it" — a low-floor authoring surface where users describe an automation in natural language and the system creates a runnable artifact. Spaarke aims for the same authoring feel for legal-ops workflows.
- **Proactive monitoring.** OpenClaw demonstrated that the qualitative jump from "assistant that answers" to "agent that acts on its own" comes from giving users a simple way to author *proactive* behavior — automations that run on schedules, watch for conditions, and reach out without being prompted. The Action Engine should make this equally easy in the Spaarke domain.

The dis-analogies matter too. OpenClaw is a personal, file-based, locally-hosted agent runtime that operates with the credentials of the machine it runs on. Spaarke is a tenant-resident, ALM-packaged platform that operates under the customer's existing Microsoft auth, governance, and audit posture. Where OpenClaw influences are useful they should be drawn on; where the underlying model differs Spaarke should not contort itself to match.

---

## 3. Core Design Principle: Deterministic and Probabilistic Tools Are Peers

A substantial fraction of legal-ops requests are **deterministic queries against structured data**:

- "How many tasks are due on May 20, 2026?" — exact answer, SQL/FetchXML query.
- "Send me invoices over $50k from non-panel firms this week" — exact answer, filtered query.
- "Email a weekly digest of matters in 'pending review' status" — exact answer, scheduled report.

These have correct answers. Running them through an LLM is wasteful and introduces probabilistic failure modes on problems that have none. The Action Engine must treat deterministic tools as **first-class peers** to AI tools — same registry, same dispatch surface, same telemetry.

A second, more important framing: **the Action Engine can be an AI-enabled front end for otherwise deterministic execution.** The natural-language authoring experience is probabilistic (the Builder Agent matches user intent to a template and elicits variables). The trigger evaluation may be deterministic (a cron firing) or probabilistic (a signal classification). But the *execution* of a "send weekly task digest" Action can be — and should be — purely deterministic. AI orchestrates the front door; deterministic code does the work.

A useful mental model:

| Phase | Often deterministic | Often probabilistic |
|---|---|---|
| Authoring (intent → manifest) | | ✓ (Builder Agent) |
| Trigger evaluation | ✓ (cron, event, signal-match) | ✓ (signal classification, anomaly detection) |
| Tool dispatch | ✓ | ✓ |
| Execution of step | ✓ (data query, message send, report gen) | ✓ (summarize, extract, classify, draft) |
| Output formatting | ✓ (template render) | ✓ (NL generation) |

The Action Engine treats this as configurable per step, not as a global choice. AI is a tool, not a default. When the deterministic option exists for a step, the platform should prefer it.

---

## 4. Conceptual Model

The Action Engine uses Spaarke's existing composition primitives. No new vocabulary:

- **Action** — the unit a user defines and runs. The user-facing artifact. Has identity, owner, trigger, parameters, optional human gate, and a reference to the Playbook that executes it.
- **Playbook** — the orchestration resource. Composed of one or more steps. Each step invokes a Tool, optionally augmented by a Skill, optionally grounded by Knowledge. A Playbook can be a single step or many steps. *A Playbook is what an Action runs.*
- **Skill** — instruction fragments that tell the orchestrator how to use a Tool well in a specific context.
- **Tool** — the verb. Real code that does something. Tools come in two flavors:
  - **Deterministic Tools** — exact, predictable, no LLM in the execution path. Examples: query Dataverse, send email via Graph, post to Teams, generate report from template, evaluate OCG rule, export to PDF.
  - **AI Tools** — invoke a model. Examples: summarize, extract structured data, classify, draft text, answer-with-RAG.
- **Knowledge** — context retrieval. Foundry IQ indexes, RAG corpora, Dataverse views.
- **Trigger** — what causes an Action to run. Five surfaces: manual, scheduled, event, signal, webhook.

### Templates and parameters — the authoring shortcut

Most user requests cluster around a small number of patterns with different variables. "Send me a daily 9 AM text with tasks due in the next 2 days for Smith Co" and "Email me Mondays at 6 AM with tasks due in the next 7 days for ACME Co" are the same workflow with different values.

This collapses the authoring problem from open-ended composition to **template matching plus variable elicitation**:

- **Action Templates** — parameterized definitions. A template declares its variables (JSON Schema), defaults, validation rules, and a natural-language description of when it applies. Templates ship in the solution; customers can author their own.
- **Action Instances** — a template reference plus a parameter blob holding the user's variable values.

The variable surface clusters into a small reusable set: **Who** (user/role/team), **What entity** (matter/client/portfolio/document type), **Which one(s)** (specific record/filter/all), **When** (cron/event/signal), **Window** (lookback/lookahead), **Threshold** (amount/count/percentage/duration), **Where** (delivery channel), **How** (format/detail/grouping). A shared library of input controls — one per variable type — gives every template a consistent form-driven editor and a consistent elicitation script for the Builder Agent.

A starter library of 15–25 templates should cover ≥90% of user requests: recurring digest, threshold alert, deadline reminder, status-change watch, anomaly flag, new-record notification, periodic compliance report, stale-matter sweep, cross-matter rollup, approval-pending nudge, document expiration watch, counsel performance digest, budget burn alert, invoice approval queue summary, matter intake routing, OCG sweep, accrual nudge. The Tool Library underneath supports far more, but the template library is what the average user touches.

---

## 5. Component Surfaces

### Action Definition (the manifest)

A declarative record. Conceptually:

- **Identity** — name, description, version, category, owner, visibility scope.
- **Template reference + parameter values** (for instances) OR template metadata (for templates).
- **Playbook reference** — the orchestration resource to execute.
- **Trigger spec** — manual, scheduled (cron), event (entity + change type + filter), signal (signal type + criteria), or webhook.
- **Authorization requirements** — declared resource types and access levels. Endpoint filter enforces (ADR-008).
- **Human Gate** — `{ required: bool, approvalRoles: [...], gateReason: string }`. Default `required: true` for any step with external side effects (send, write, delete, pay, escalate). Opt-out is permitted but logged.
- **Output contract** — shape of result, persistence target, delivery channel.
- **Governance** — feature flag, kill-switch reference (ADR-018), cost-center tag, rate-limit budget.

### Tool Registry

The library of pre-built Tools. The strategic engineering asset — investment here compounds. Tool Registry metadata is **extended via a joint workstream** with the Insights Engine (coordination assessment §4.5 + §4.8). Each registered Tool exposes:

- **Identity** — handler ID, supported tool types
- **Input/output schemas** — Zod / C# types for parameters and return shape
- **`Classification`** — `Deterministic | AI | Hybrid` (LAVERN-adjacent)
- **`CostClass`** — `Free | Cheap | Expensive`
- **`LatencyClass`** — `Sub100ms | Sub1s | Sub10s | LongRunning`
- **`Idempotency`** — `Idempotent | NotIdempotent`
- **`AuthMode`** — `Obo | AppOnly | None` (plus required permissions)
- **`Discoverability`** — `{ keywords, semanticDescription, sampleInvocations }` for Builder Agent semantic search
- **`ModelTier`** — `Premium | Standard | Fast | Embedding` (per LAVERN ADR 10.4; enables future EvaluatorGate tier-separation enforcement in R2+)
- **`PhaseRestrictions`** — array of phase names where the tool MUST NOT dispatch (per LAVERN Pattern #8; see "Phase deny-tools" below)
- **`EvidenceRequired`** — bool (per LAVERN Pattern #6; runtime-guards finder / inference tools)

Both Action Engine and Insights Engine populate this extended schema when registering tools. The joint workstream lands before either project pipelines.

Tool categories the platform needs at minimum:

- **Data query tools** (deterministic) — Dataverse queries, aggregates, joined views, role-scoped queries. These cover the "how many tasks due on May 20" class entirely.
- **Document tools** — generate from template, export PDF/DOCX, fill form, extract content.
- **Communication tools** (deterministic delivery) — send email, send SMS, post to Teams channel, send adaptive card, push notification.
- **AI tools** — summarize, extract, classify, answer-with-RAG, draft.
- **Integration tools** — call Power Automate flow, call connector, invoke Copilot Studio agent, post webhook.
- **Workflow tools** — start approval, escalate, assign task, change status.
- **Matter/legal-domain tools** — open matter, route invoice, evaluate OCG rule, compute budget burn, lookup counsel.

### Phase deny-tools — mechanical phase enforcement (LAVERN Pattern #8)

Action Definition manifests declare phases with deny-lists per LAVERN Pattern #8:

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

`IToolHandlerRegistry` enforces deny-lists at dispatch time. Violation throws `PhaseToolDeniedException` with a clear message naming the phase and the forbidden tool. This is **mechanical** enforcement, not prompt-coached — the Builder Agent's prompt cannot circumvent it.

The default phase sequence (`authoring → schedule → execute → approve → deliver`) maps exactly to Action Engine's conceptual model in §4. Each phase has a deny-list preventing tools from being dispatched in wrong phase (e.g., Builder Agent in `authoring` cannot invoke `send_email`; `execute` cannot modify the Action's own manifest mid-run).

Cross-references: `coordination-assessment-with-insights-engine.md` §4.8 (joint Tool Registry metadata extension); LAVERN Pattern #8.

### Run record

Every Action invocation produces a run record with status states matching ADR-017: `Queued → Running → Completed | Failed | Poisoned | Cancelled`. Includes inputs, step outcomes, output reference, correlation ID, attempt count, approval history. This is the audit surface and the user-facing run history.

### Monitor (signal/event subscription)

Binds a signal-type filter or an event filter to one or more Actions. When the Insights Engine emits a matching signal — or a Dataverse event matches — the Monitor dispatches the bound Action(s). Event-driven, not polling.

### Approval queue — implemented via `IGateResolver` (LAVERN ADR 10.3)

Approval queue is implemented via the **`IGateResolver` interface** per LAVERN ADR 10.3 (proposed in `projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md` §10.3). The Action Engine MVP is the implementer of this primitive across Spaarke; the Insights Engine consumes it for Phase 2+ write-back paths, and Self-Service Registration / Email Wizard adopt it over time. **One canonical approval primitive** — no per-surface reimplementation.

Four implementations ship in MVP:

- **`DataversePrecedentBoardGateResolver`** — writes `sprk_gate_approval` record; polls or webhook-resumes. MVP default surface.
- **`InteractiveGateResolver`** — in-chat / context pane card with approve/reject via existing SSE. MVP for workspace surface.
- **`WebhookGateResolver`** — agent-to-agent callbacks. R2+.
- **`AutoApproveGateResolver`** — tests and opt-in low-risk Actions only.

Five gate types: `EthicsCritical`, `MeaningCritical`, `FinalDelivery`, `EngagementAcceptance`, `TeamSelection`, plus `Custom`. Action Definition's `humanGate` field references a gate type.

Shared UI component `GateApprovalCard` in `Spaarke.UI.Components` renders the approval surface; surfaces (workspace, mobile, Teams, M365 Copilot) mount it. New approval surfaces are *new GateResolver consumers*, not new approval systems.

For Actions with `humanGate.required: true`, execution pauses at the gate. Approval records route to declared roles via the resolver. Approval resumes execution; rejection terminates the run with a recorded reason. Default timeout 5 minutes → auto-reject.

Cross-references: Insights Engine `decisions.md` D-51 (consumption decision); `coordination-assessment-with-insights-engine.md` §4.6 (joint coordination); `projects/ai-spaarke-insights-engine-r1/lavern-pattern-assessment.md` §6.2.

---

## 6. Runtime and Hosting — Open Architectural Question

**This section deliberately presents options rather than a decision.** The right answer depends on trade-offs that should be evaluated, not pre-committed. The key axes:

- **Where does the Action Definition live?** Dataverse is the obvious answer (ALM, security, audit, solution packaging). Likely settled.
- **Where does the scheduler run?** Cron-style triggers need a reliable, low-latency scheduler that wakes up and dispatches. Options below.
- **Where does the agent execution loop run?** For multi-step Playbooks with probabilistic steps (AI reasoning, tool selection), there's a real runtime that maintains agent state, calls tools, and handles intermediate results. Options below.
- **Where does tool dispatch happen?** Almost certainly the BFF, since that's where auth, tools, and the existing dispatch surface already live.
- **Where does state/memory persist?** Dataverse (for durable artifacts), Foundry IQ (for grounded knowledge), Redis (for short-lived cache), and the run record (for execution state). Likely settled.

The open questions concentrate on **scheduler** and **agent runtime**. Options:

### Option A — Existing BFF BackgroundService scheduler + existing tool dispatch

Run the scheduler and the agent loop inside the existing BFF process (ADR-001). For multi-step Playbooks with AI steps, use the existing AI tool orchestrator extended to handle deterministic tools as peers. Microsoft Agent Framework is *not* introduced as a separate runtime; its patterns may be borrowed where useful but the host stays the BFF.

- **Pros**: Minimal new infrastructure. Reuses ADR-001/004/017. Single deployment unit. Auth and tool dispatch already wired.
- **Cons**: BFF becomes responsible for long-running agent state. Scheduler durability under restart is something to design. Doesn't leverage Microsoft's investment in agent runtimes.

### Option B — Microsoft Agent Framework as the agent runtime, BFF as the dispatch surface

Use Microsoft Agent Framework (hosted in-process within the BFF or out-of-process) as the execution loop for multi-step probabilistic agents. Single-step deterministic Actions still dispatch directly through the BFF without invoking the agent framework. The framework handles agent state, tool calling, and intermediate reasoning; the BFF handles auth, tool registration, and the management surface.

- **Pros**: Microsoft is consolidating its agentic stack here, which aligns with the broader Spaarke architecture posture. Built-in patterns for multi-step agent state and tool orchestration. Future-aligned.
- **Cons**: New dependency. Maturity and operational ergonomics need evaluation. Decision about whether the framework runs in-process or out-of-process has cost and latency implications.

### Option C — Azure-native scheduler (Logic Apps timer, Function timer, or Azure Container Apps Jobs) + BFF execution

Decouple the scheduling concern entirely from the BFF. An Azure-native timer wakes up, fetches the due Actions from Dataverse, and calls the BFF to execute. Agent loop (when needed) still runs in the BFF, possibly using Microsoft Agent Framework patterns.

- **Pros**: Scheduling is a solved Azure concern. BFF stays focused on tool dispatch and auth. Scales independently. Survives BFF restarts cleanly.
- **Cons**: More moving parts. Cross-service auth to evaluate. Latency between scheduler tick and execution dispatch.

### Option D — Hybrid

Most likely the actual answer. Examples of how the hybrid might fall out:

- Scheduler: Azure-native (Option C) for reliability and decoupling.
- Single-step / deterministic Action execution: BFF (Option A) for low overhead.
- Multi-step probabilistic agent loops: Microsoft Agent Framework hosted in BFF or adjacent (Option B).
- Signal-triggered Actions: existing Service Bus subscription in BFF.
- Event-triggered Actions: existing Dataverse webhook into BFF.

**What needs to happen before MVP build:** an architecture spike that evaluates Microsoft Agent Framework's runtime model, maturity, and operational profile against the existing BFF BackgroundService approach, and a separate evaluation of Azure-native scheduling options against the existing scheduler. The output is a decision record (probably a new ADR) that commits to a runtime topology before code is written.

**Coordination recommendation** (per `coordination-assessment-with-insights-engine.md` §4.3): Action Engine should adopt the **same hybrid topology** the Insights Engine adopted (Hybrid Option D). Specifically:

- **Scheduler**: Azure-native Function timer (Option C) — same Function App pattern as Insights Track B
- **Single-step / deterministic Action execution**: BFF (Option A) — direct dispatch through `IToolHandlerRegistry`
- **Multi-step probabilistic agent loops**: BFF (Option A) — extend existing `IChatClient` + `UseFunctionInvocation` + `SprkChatAgentFactory`; **do NOT introduce Microsoft Agent Framework as a separate runtime in MVP**. Reconsider only if a concrete scenario emerges that the existing orchestrator demonstrably cannot serve.

Rationale: Phase deny-tools (LAVERN Pattern #8) and GateResolver (LAVERN Pattern #5) are most cleanly enforced at our dispatch layer — introducing Microsoft Agent Framework as a separate runtime would create two divergent agent execution paths and a new operational seam. Cross-project coupling (Insights Track B already uses the hybrid pattern) reinforces this choice.

---

## 7. Authoring Experience

Three authoring paths, in increasing power and decreasing ease.

### 7.1 Conversational Builder Agent (the primary path)

The agent walks the user from natural-language intent to a saved, runnable Action. The pattern is **template matching + variable elicitation**, not open synthesis:

1. User states intent in natural language.
2. Agent searches the template library (semantic + keyword) and proposes top 1–3 matches with one-line explanations.
3. User selects, or asks for variation; agent re-searches.
4. Agent walks the template's parameter schema one variable at a time, with smart defaults inferred from context.
5. Agent runs a **dry-run preview** against recent data and shows what the user would have received yesterday.
6. User approves; the Action Instance is saved and ambient placement is offered.

If no template matches well, the agent suggests a composition (multi-step Playbook) or routes the user to the visual builder. The search space stays bounded; the agent matches and elicits rather than freely composing. This pattern fails predictably (wrong template picked, user corrects) rather than creatively (model invents something subtly broken).

This is an extension of Spaarke's existing AI Builder Agent, not a new system. The upgrades are template/tool/knowledge discoverability and the ability to attach triggers.

**Phase enforcement**: The Builder Agent operates in the `authoring` phase per LAVERN Pattern #8. Phase deny-tools (see "Phase deny-tools" subsection in §5) prevents the Agent from dispatching execution tools (`send_email`, `create_record`, etc.) mid-authoring even if its prompt would otherwise suggest doing so. This is **mechanical** enforcement at the `IToolHandlerRegistry` dispatch layer, not prompt-coached. Fix the registry if the Builder Agent ever succeeds at calling an execute-phase tool from authoring — never fix the prompt alone.

### 7.2 Visual Builder

A form rendering a template's parameter schema using the shared variable-type controls. Falls through to a node canvas for multi-step Playbook composition when needed. For users who prefer direct authoring.

### 7.3 Pro-code

Action Definitions and Templates authored as JSON/YAML in solution source, deployed via ALM. For platform-shipped templates and partner-built starter libraries.

---

## 8. Ambient Delivery (where Actions live after authoring)

OpenClaw's UX advantage is that everything lives in one chat surface. Spaarke has more surfaces, which is both a feature and a tax. At save time the user picks placement from a small menu, with smart defaults based on the trigger:

- **Workspace pin** (default for manual) — Action appears as a tile in the user's workspace.
- **Embedded chat** — invokable from the in-app AI chat surface.
- **Form ribbon button** — for record-scoped actions.
- **Schedule-and-deliver** (default for scheduled) — runs in background, delivers to declared channel.
- **Signal-and-push** (default for signal triggers) — runs on signal match, delivers as notification.
- **M365 Copilot exposure** — generates a Copilot Studio agent or declarative-agent manifest so users invoke via Copilot Chat from Word, Outlook, Teams.
- **Teams channel post / Outlook digest / mobile push** — delivery targets.
- **External webhook** — POST to a declared URL, subject to gate.

---

## 9. Integration with Adjacent Spaarke Components

- **Insights Engine** — emits structured `InsightArtifact` signals (Fact / Observation / **Precedent** / Inference per the 4-tier taxonomy in Insights Engine `decisions.md` D-03 + D-46 / LAVERN ADR 10.1). The Action Engine subscribes via Monitors on the signal envelope contract (coordination assessment §4.2). **Precedents** are a new artifact type Action Engine AI Tools can cite when drafting content (R2+); supports Mode 4 (Precedent curation) and Mode 6 (weekly briefing) from `ADVANCED-AI-USE-CASE-PATTERNS.md`.
- **Playbooks** — the orchestration resource Actions execute. Authoring an Action either selects an existing Playbook + parameters or composes a new one. Single-step Actions resolve to single-step Playbooks.
- **Microsoft Agent Framework** — candidate runtime for multi-step agent loops (see Section 6). May or may not be adopted depending on architecture evaluation.
- **Foundry IQ** — grounding/memory for AI steps and for the Builder Agent's understanding of customer context.
- **Existing AI Tool Framework** — current orchestrator and dispatch mechanism. Extended (not replaced) to treat deterministic tools as peers.
- **M365 Copilot Chat / Copilot Studio agents** — Actions can be exposed to Copilot as callable tools (custom connector). Domain Copilot agents reason about *when* to invoke; the Action Engine handles *how*.
- **Power Platform connectors** — exposed as Tools where useful. Connectors are one source of Tool Handlers, alongside native Spaarke ones.

---

## 10. Phased Delivery

### MVP

*Original scope*

- Action Definition entity, Action Template entity, Action Instance entity, Run record.
- Manual + scheduled triggers only.
- Tool Registry containing existing AI tools plus an initial set of **deterministic** tools (data query, communication, document generation).
- Pro-code authoring only.
- Endpoint-filter authorization (ADR-008).
- 3–5 starter templates (recurring task digest, threshold alert, deadline reminder).
- Runtime/hosting decision made via architecture spike *before* code is written (see Section 6).

*LAVERN-derived additions for MVP* (per `lavern-pattern-assessment.md`):

- **`IGateResolver` interface + 4 implementations + `sprk_gate_approval` entity + `GateApprovalCard` UI component** (LAVERN ADR 10.3) — the canonical approval primitive across Spaarke. See "Approval queue" subsection in §5.
- **Phase deny-tools schema + dispatch enforcement** (LAVERN Pattern #8) — mechanical phase guardrails for the Builder Agent. See "Phase deny-tools" subsection in §5.
- **Tool Registry metadata extension** (joint with Insights Engine per `coordination-assessment-with-insights-engine.md` §4.5 + §4.8) — adds `Classification / CostClass / LatencyClass / Idempotency / AuthMode / Discoverability / ModelTier / PhaseRestrictions / EvidenceRequired` to `ToolHandlerMetadata`.
- **`ModelTier` field on JPS scopes** (LAVERN ADR 10.4) — foundation for EvaluatorGate (LAVERN Pattern #2) in R2+; not used in MVP but the schema is in place.
- *Optional*: **`PB-011 Tabular Extraction` Playbook** (LAVERN Pattern #12) if any of the 3–5 starter templates is tabulation-style (e.g., cross-matter rollup, invoice approval queue summary).

*MVP-conditional* (depends on starter template choices):

- **CUAD + MAUD seed-data ingestion** + `sprk_clausetype` Dataverse taxonomy + `spaarke-reference-clauses` AI Search index (LAVERN ADR 10.5 + Pattern #9) — **required only if RedFlagDetector ships in MVP starter templates**. If RedFlagDetector defers to R2, this whole workstream defers with it.

### R2

*Original scope*

- Signal-triggered Monitors (Insights Engine coupling).
- Event-triggered Monitors (Dataverse webhooks).
- Approval queue with UI in workspace and Teams.
- Conversational Builder Agent v1 (template matching + parameter elicitation).
- Expanded template library (15+ templates).

*LAVERN-derived additions for R2*:

- **`EvaluatorGate` JPS Action category** (LAVERN ADR 10.2 / Pattern #2) — opt-in per-step quality check on high-stakes AI Actions. Requires `ModelTier` from MVP. Default off; per-Action opt-in.
- **`PlaybookExecutionFlow` shared component** (LAVERN Pattern #4) in `Spaarke.UI.Components` — SSE-driven flow UI for multi-step Actions. Mounted by workspace pane, embedded chat, Office add-ins. Consumed by Mode 1 (reactive review) and Mode 2 (proactive monitoring + triage) from `ADVANCED-AI-USE-CASE-PATTERNS.md`.
- **Consume `ISanitizer` shared primitive** (LAVERN ADR 10.6 + Pattern #10) — Insights Engine Phase 1 builds it; Action Engine R2 wires it into webhook/signal trigger ingestion paths. See coordination assessment §4.7.
- **Consume `GroundingVerifier` shared primitive** (LAVERN ADR 10.6 + Pattern #3) — Insights Engine Phase 1 builds it; Action Engine R2 AI Tools that return findings (RedFlagDetector, draft tools with citations) consume it. See coordination assessment §4.7.

### R3

- Visual Builder for templates and composition.
- Conversational Builder Agent v2 (multi-step composition assistance, dry-run improvements).
- Action Library within tenant.
- Copilot Studio agent generation from Action Definitions.

### Post-MVP horizon

- Cross-tenant Action Template sharing (vetted).
- Mobile-first delivery surface.
- Marketplace for partner-built tools and templates.
- Cost/usage governance UI for admins.

---

## 11. Design Principles and Anti-patterns

### Principles

- **Action Engine is its own domain.** It is the agent creation/management surface for users. Playbooks are the orchestration resource. Insights signals are one trigger source.
- **Deterministic and AI tools are peers.** Do not default to AI for problems with exact answers. The Tool Registry is one registry, not two.
- **AI may orchestrate deterministic execution.** The natural-language authoring experience is probabilistic; the resulting execution can be — and often should be — fully deterministic.
- **Templates over open synthesis.** The Builder Agent matches and elicits; it does not freely compose for typical user requests.
- **The Tool Registry is the moat.** Engineering investment here compounds more than anywhere else.
- **Human-controlled defaults.** Actions with external side effects default to `humanGate.required: true`. Opt-out is allowed, logged, reviewable.
- **Tenant-resident.** Runs inside the customer's Microsoft tenant under their auth, governance, and audit.
- **Reuse existing infrastructure.** ADR-001/002/004/007/008/009/010/017/018 all apply. New infrastructure is justified only by the architecture spike in Section 6.
- **Approval is a shared primitive.** `IGateResolver` (LAVERN ADR 10.3) is the canonical contract across Spaarke. Action Engine MVP is the implementer; never reimplement approval per surface.
- **Phase boundaries are mechanically enforced.** Phase deny-tools (LAVERN Pattern #8) prevents probabilistic agents (Builder Agent) from dispatching tools in the wrong phase. Mechanical enforcement at `IToolHandlerRegistry`, not prompt-coached.

### Anti-patterns

- **Do not let AI tools mask deterministic problems.** "How many tasks due tomorrow" is a query, not a prompt.
- **Do not use Power Automate as the runtime.** ADR-001 stands. Power Automate is the customer-extensibility layer that consumes Action events, not the engine that runs them.
- **Do not introduce heavy Dataverse plugins.** ADR-002 stands.
- **Do not poll.** Signals from the Insights Engine and events from Dataverse are the trigger surface for proactive Actions.
- **Do not invent a "machine credentials" auth model.** OBO and app-only flows are correct.
- **Do not conflate Tools and Skills.** A Tool is code that does something. A Skill is instruction text telling the orchestrator how to use a Tool well in context.
- **Do not bypass the human gate by default.**
- **Do not commit to a runtime topology before the architecture spike completes.**
- **Do not reimplement approval primitives per surface.** Workspace, Teams, mobile, M365 Copilot all consume the same `IGateResolver` via shared UI components.
- **Do not bypass phase deny-tools.** If a Builder Agent prompt or Action template can dispatch an execute-phase tool from the authoring phase, the dispatch enforcement is broken — fix the registry, not the prompt.
- **Do not introduce Microsoft Agent Framework as a separate runtime in MVP** without a concrete scenario the existing orchestrator cannot serve. Hybrid topology already chosen by Insights Engine per coordination assessment §4.3.

---

## 12. Open Questions for Implementation Spec

Resolved before MVP build (originally listed; some now superseded by LAVERN adoption):

- **Runtime topology** (the central question). Microsoft Agent Framework vs existing BFF orchestrator. Azure-native scheduling vs BFF scheduler. Hybrid composition. Output: a runtime ADR. **Coordination recommendation per §6**: adopt Insights Engine's Hybrid D — BFF for dispatch + Azure-native Function scheduler.
- Exact entity schema for Action Definition, Template, Instance, Run, Monitor, Approval. Approval entity now `sprk_gate_approval` per LAVERN ADR 10.3.
- Relationship and migration path between today's playbook entity and the Action / Template / Instance model — do they merge, or does the new layer sit above the existing playbook? **Coordination recommendation per coordination assessment §4.1**: Action Engine sits above the JPS playbook entity (Action Definition → Playbook reference is a foreign key).
- Tool Handler registration mechanism (attribute, configuration, hybrid).
- Variable-type control library scope and component shapes (lives in the shared component library per ADR-012).
- Builder Agent prompt/skill specification and few-shot example set.
- Cost-attribution model for AI tool invocations within Actions (ties to ADR-016).
- Cross-solution layering rules for Templates (system → ISV → customer overrides).
- Cancellation and recovery semantics for long-running multi-step Actions.
- ~~Approval routing rules and escalation timing~~ — **resolved by `IGateResolver` (LAVERN ADR 10.3)**: 5-min default timeout → auto-reject, surface routing via `SurfaceHint`, declared `AuthorizedApproverRoles`.

Newly open (introduced by LAVERN adoption):

- **LAVERN ADR ratification timeline**. Action Engine MVP depends on ADRs 10.3 (GateResolver) and (joint) Tool Registry metadata extension being ratified before pipeline. ADRs are proposed in `projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md` §10 but not yet ratified.
- **SME workflow design for Precedent curation surfaces** — cross-cuts to Insights Engine Phase 1.5 (Mode 4 from `ADVANCED-AI-USE-CASE-PATTERNS.md`). Action Engine R2+ surfaces (workspace pin, embedded chat, etc.) may render the SME confirmation queue — surface choice is a product decision deferred to real customer input.
- **CUAD + MAUD seed-data scope** — depends on whether RedFlagDetector ships in MVP. If yes, ADR 10.5 + Pattern #9 are MVP-blocking; if no, R2.
- **`PB-011 Tabular Extraction` Playbook scope** — depends on which 3–5 starter templates ship MVP (Pattern #12 is optional MVP).

---

*This document is a conceptual overview. Implementation specs and a runtime ADR will follow.*
