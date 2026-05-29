# Spaarke Action Engine — Project Design

> **Audience:** Claude Code and platform engineers consuming this as the canonical project design.
> **Purpose:** Establish the conceptual model, component boundaries, and open architectural questions for the Action Engine. This document is the load-bearing design for the project; implementation specs (`spec.md`, `plan.md`, task files) refine but do not contradict it.
> **Status:** Project design. Entity names, file paths, and code identifiers below are *conceptual labels*. Runtime/hosting architecture is *open* — Section 6 lays out options to evaluate.
> **Revision history:**
> - **2026-05-29** — Promoted from `action-engine-overview.md` to `design.md`. Added §4.5 (Surfaces and Invocation Paths), §7.4 (Execution-Time Conversational Invocation), revised §8 (Ambient Delivery), §10 (Phased Delivery), §11 (Design Principles and Anti-patterns). Incorporated decisions from the 2026-05-28 Spaarke AI Assistant bring-up: deterministic ↔ probabilistic tool peering, meta-tools for execution-time discovery, multi-surface Assistant model, hallucination guardrails as an explicit set.
> - Pre-2026-05-29 — Original overview authored as `action-engine-overview.md`.

---

## 1. What the Action Engine Is

The Action Engine is the **agent creation, management, and execution surface for the Spaarke domain**. It lets users — including non-technical legal-ops users — define, configure, and run agents and automations that operate on their workspace data and operational signals. These range from a one-tap manual action ("summarize this matter") to a recurring scheduled digest ("text me 9 AM weekdays with tasks due in two days for Smith Co") to a fully event-driven proactive agent ("when an invoice arrives over $50k from an off-panel firm, route it for review with a one-paragraph rationale") to a conversational request through the Spaarke AI Assistant ("send an email to outside counsel about the Smith deal status").

The Action Engine is its own concern. It is *not* an extension of the Insights Engine. The Insights Engine senses and emits structured signals; the Action Engine creates and manages user-defined agents that may *consume* those signals (alongside other triggers) and do something useful with them. Insights signals are one of several inputs to the Action Engine, not its defining purpose.

**The relationship to Playbooks.** Playbooks are the orchestration resource — the composition of Actions, Skills, Knowledge, and Tools that defines *how* a piece of work is done. The Action Engine is the management plane *around* Playbooks: how users author them, parameterize them, attach triggers to them, run them, monitor them, and govern them. A Playbook without the Action Engine is a static artifact; an Action without a Playbook has nothing to execute. They are layered, not the same thing.

**The relationship to the Spaarke AI Assistant.** The Assistant is one of several **surfaces** (§4.5) the user encounters Action Engine through. It is the conversational front door. Users with a quick request type or speak it; the LLM matches their intent against the Tool Registry, resolves the parameters, asks for confirmation when needed, executes. The Assistant is not a different engine from the Action Engine — it is a conversational invocation path into it. Section 4.5 details the surfaces and invocation paths; Section 7.4 details the conversational invocation pattern; both build on the conceptual model in Section 4 and the Tool Registry in Section 5.

---

## 2. Inspirations and Influences

OpenClaw is a useful reference point for two specific things:

- **Ease-of-use authoring.** OpenClaw popularized the experience of "tell the assistant what to do and it just does it" — a low-floor authoring surface where users describe an automation in natural language and the system creates a runnable artifact. Spaarke aims for the same authoring feel for legal-ops workflows.
- **Proactive monitoring.** OpenClaw demonstrated that the qualitative jump from "assistant that answers" to "agent that acts on its own" comes from giving users a simple way to author *proactive* behavior — automations that run on schedules, watch for conditions, and reach out without being prompted. The Action Engine should make this equally easy in the Spaarke domain.

The dis-analogies matter too. OpenClaw is a personal, file-based, locally-hosted agent runtime that operates with the credentials of the machine it runs on. Spaarke is a tenant-resident, ALM-packaged platform that operates under the customer's existing Microsoft auth, governance, and audit posture. Where OpenClaw influences are useful they should be drawn on; where the underlying model differs Spaarke should not contort itself to match.

### Note on LAVERN references

This document cites patterns and section numbers from **LAVERN** ([AnttiHero/lavern](https://github.com/AnttiHero/lavern)), an Apache-2.0 TypeScript multi-agent legal system used as a reference source for several design patterns (`IGateResolver`, Phase deny-tools, extended Tool Registry metadata, etc.). Pattern adoption is documented in [`projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md`](../ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md) §10. **LAVERN is not a Spaarke ADR series.** Where this design refers to `LAVERN reference §10.x` or `LAVERN Pattern #N`, it points at the analysis document's section/pattern numbers — not at ratified Spaarke ADRs. Per the 2026-05-29 `spec.md` owner clarification, the patterns LAVERN inspired are formalized inline within this project's own design and implementation; no separate Spaarke ADR ratification is required to adopt them.

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

## 4.5 Surfaces and Invocation Paths

The Action Engine is a runtime + management plane; users encounter it through many **surfaces**. Three execution invocation paths cut across all surfaces, and the Spaarke AI Assistant is a conversational surface that runs in many host contexts.

### 4.5.1 The three execution invocation paths

| Path | Trigger | LLM involvement | Resolution model |
|---|---|---|---|
| **(a) Conversational** | User types or speaks in an Assistant chat surface (§4.5.2) | LLM matches intent → Tool Registry candidates → resolves params via conversation → confirms → executes | Meta-tools `FindResources`, `GetResourceDetail`, `InvokeResource` + always-on Tools for high-frequency operations. See §7.4. |
| **(b) Explicit** | User clicks a UI affordance directly bound to a specific Action: command bar button, prompt pill, tool icon, ribbon button, pinned tile | None for intent-matching. May be used inside the Action's Playbook if a step is AI-classed. | Direct binding from UI to Action id. Parameters resolved via deterministic UI form (Visual Builder shapes from Template's variable schema). Skip the intent-match hop entirely. |
| **(c) System** | Declared trigger fires the Action: scheduled cron, Service Bus event, Dataverse webhook, Insights Engine signal match | None for invocation. May be used inside the Playbook for AI steps. | Action Definition's trigger spec binds the trigger condition to the Action id. Parameters supplied by trigger payload + Action's parameter defaults. |

All three paths share the same runtime, the same Tool Registry, the same authorization model (ADR-008 endpoint filters + Phase deny-tools), the same `IGateResolver` for side-effect confirmation, the same audit posture. Only the entry point differs.

This means **the same Action ("Send weekly task digest to Smith Co") can be invoked all three ways**: from the Assistant chat ("send me that weekly digest now"), from a ribbon button on the Smith Co matter form, and from a cron trigger every Monday 9am. The Action Definition is the canonical artifact; the path choice is a UX concern.

### 4.5.2 Assistant surfaces (where conversational invocation happens)

The Spaarke AI Assistant is a conversational surface. It runs in many **host contexts**, each providing different default context and slightly different UX, but **all share the same agent runtime** (the BFF `SprkChatAgent` + `UseFunctionInvocation` per §6 recommendation) and the same Tool Registry:

| Surface | Host context | Default context provided | Use case |
|---|---|---|---|
| **SpaarkeAi Code Page** (`sprk_spaarkeai`) | Standalone model-driven app entry point | None — user lands without entity context | General cross-matter requests, exploratory queries, draft content, request status digests |
| **Assistant PCF on entity form** | Matter / Project / Account / Contact / etc. form | Entity record id + type | Conversational invocation scoped to the record the user is on. The form provides ambient grounding. |
| **Embedded chat in workspace pane** | AnalysisWorkspace, LegalWorkspace, code pages with chat regions | Analysis id, workspace state, document selection | Tightly coupled to the workspace task — questions about the analysis, refinements to a draft |
| **Command bar / ribbon Assistant button** | Any form or grid | Selected records | Ad-hoc invocation from existing UX flows. Surfaces explicit-path Tools as well. |
| **Office Add-in (Outlook, Word, Excel)** | Email or document context | Mail item / document content | Compose, extract, summarize in-place |
| **M365 Copilot agent** | M365 Copilot Chat | M365 user context + recent activity | Cross-app access to Spaarke Actions via Copilot's host surface |
| **Teams chat / mobile push / SMS** | Teams / mobile / SMS | Conversation context | Asynchronous, remote, or low-bandwidth access |

Each surface contributes **surface context** to the agent (entity ids, document ids, selected records, current user, etc.). The Tool Registry's `SourceHints` on each Tool's parameter schema declares "this parameter is supplied by surface context if available, otherwise ask the user." The agent doesn't ask for what the surface already provided.

### 4.5.3 Explicit-path UI affordances per surface

Each conversational surface ALSO supports the explicit invocation path (b) via UI affordances bound to specific Tools or Templates. Examples:

- **Assistant PCF on a Matter form** might show three pinned Actions ("Summarize matter", "Find similar matters", "Draft status email") that invoke directly without an LLM hop. The chat input is still there for conversational requests; the pinned Actions are shortcuts for known operations.
- **Command bar buttons** on a Document Library grid might expose "Bulk classify", "Bulk redact PII", "Send selected to Legal Hold review" — each bound to a specific Action. Selecting records sets the parameters; clicking invokes.
- **Prompt pills** in any chat surface offer pre-written conversational requests ("What's due this week?", "Summarize this matter"). These pre-fill the chat input and submit — still conversational, but with explicit starting text.

The same Tool can be exposed as (a) discoverable via FindResources, (b) bound to a pinned button, and (c) scheduled to run on a cron. The Tool Registry entry doesn't care; the surface chooses.

### 4.5.4 Surface-context awareness in the agent

The agent's runtime behavior changes based on launch context:

- **No entity context** (SpaarkeAi Code Page landing): default playbook with broad meta-tools (`FindResources`, `InvokeResource`) + foundational always-on Tools (`SearchDocuments`, `QueryDataverse`) registered. System prompt emphasizes discovery: "ask the user what they need, look it up via tools, never invent."
- **Entity context** (PCF on Matter form, embedded chat in workspace): playbook selected from `sprk_aichatcontextmap` based on the entity type. Tool set narrows to entity-relevant Tools (matter-specific Templates promoted to always-on). System prompt knows "you are helping with this specific record." Surface context fills in known parameters automatically.
- **Document context** (Office Add-in): document content is part of the prompt context (within window limits). Tools relevant to the document type (summarize, extract clauses, fact-check, classify) become always-on.
- **Explicit Action invocation** (ribbon button → specific Template): no LLM intent-matching hop. Direct parameter-elicitation form. The conversational chat surface may then be used in parallel to elaborate or refine.

The agent runtime is the same; the registered tool set and the system prompt vary by surface and context. This is configurable via `sprk_aichatcontextmap` and per-Template `IsAlwaysOnInAssistant` / `PromoteInContexts` flags on the Tool Registry.

### 4.5.5 Result delivery routes back to the originating surface

For path (a) conversational: streamed response in the chat surface; citations rendered inline; suggested follow-ups offered.
For path (b) explicit: completion notification + result tile in the originating UI; result also appears in the Assistant chat history if the surface includes one.
For path (c) system: delivery channel declared by the Action Definition (Teams post, email digest, mobile push, etc. — per §8 Ambient Delivery).

For composite results (e.g., "Drafted email + sent to outside counsel + posted to Teams"), each side-effect step goes through `IGateResolver` if `humanGate.required: true`, regardless of invocation path.

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
- **`ModelTier`** — `Premium | Standard | Fast | Embedding` (per LAVERN reference §10.4; enables future EvaluatorGate tier-separation enforcement in R2+)
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

### Approval queue — implemented via `IGateResolver` (LAVERN reference §10.3)

Approval queue is implemented via the **`IGateResolver` interface** per LAVERN reference §10.3 (proposed in `projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md` §10.3). The Action Engine MVP is the implementer of this primitive across Spaarke; the Insights Engine consumes it for Phase 2+ write-back paths, and Self-Service Registration / Email Wizard adopt it over time. **One canonical approval primitive** — no per-surface reimplementation.

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

### 7.4 Execution-Time Conversational Invocation (the Assistant pattern)

§7.1–§7.3 cover **authoring** — how a new Action gets created. §7.4 covers the analogous pattern for **execution** — how an existing Action gets invoked conversationally through the Assistant. The two share infrastructure (Tool Registry, embeddings, Template metadata) but have different concerns. The Builder Agent's job is "translate user intent into a saved Action Definition." The Assistant's job is "translate user intent into an immediate Action execution."

This section answers: *how does the LLM know what's available to invoke, and how does it resolve the parameters?*

#### 7.4.1 Meta-tools — the discovery surface for execution

Three tools registered on every Assistant session expose the Tool Registry to the LLM. They are first-class tools, not framework hooks — the LLM uses them the same way it uses any other tool. The semantic match runs server-side against the Tool Registry's `Discoverability` metadata (keywords + semantic embedding + sample invocations per §5).

```csharp
[Description("Find Spaarke resources (Tools, Action Templates) matching a user's intent. " +
             "Returns ranked candidates with ids, descriptions, and required parameter schemas. " +
             "Call this when the user expresses an intent (send email, find documents, analyze) " +
             "and you need to know what's available.")]
ResourceCandidate[] FindResources(string userIntent, int topK = 5);

[Description("Get the full schema and parameter source hints for a specific resource by id, " +
             "so you can correctly resolve required inputs and know which can be auto-resolved " +
             "vs which must be asked from the user.")]
ResourceDetail GetResourceDetail(string resourceId);

[Description("Invoke a resource by id with resolved parameters. Returns the execution result " +
             "or a confirmation request if the resource requires a human gate.")]
ResourceExecutionResult InvokeResource(string resourceId, Dictionary<string, object> parameters);
```

These three meta-tools — combined with foundational always-on Tools (`SearchDocuments`, `QueryDataverse`, `GetCurrentEntityContext`) — give the LLM the ability to conversationally **discover**, **inspect**, and **invoke** any resource the Tool Registry contains. Worked example for "send email to outside counsel about Matter X status":

1. **User intent**: "send an email to outside counsel about Matter X status"
2. **Agent**: calls `FindResources("send email to outside counsel about matter status")` → returns `[{ id: "send-email-about-matter", confidence: 0.91, description: "Send an email to a contact about a matter" }, ...]`
3. **Agent**: confidence ≥ threshold → calls `GetResourceDetail("send-email-about-matter")` → returns parameter schema with source hints: `{matter: <ask|search>, recipient: <matter.outsideCounsel|search|ask>, subject: <ask|smartDefault>, body: <ai-draft|ask>}`
4. **Agent**: resolves deterministic params via tool calls: `QueryDataverse(entityType=Matter, name="X")` → matter id; `QueryDataverse(entityType=Contact, role=outsideCounsel, matterId=...)` → recipient email
5. **Agent**: asks user for ambiguous params: "About what — a Q2 status check, or something specific?"
6. **Agent**: calls AI draft Tool to generate the body based on Action's draft template + parameters
7. **Agent**: shows confirmation (the `IGateResolver` UI): "I'll send to jane@outside-counsel.com about Q2 status with this body: <preview>. Send?"
8. **User**: confirms; **Agent**: calls `InvokeResource("send-email-about-matter", {...})`
9. **BFF execution path**: runs the Playbook, including any further human gates inside the Playbook, writes audit per ADR-015
10. **Agent**: reports "Sent. Audit id: ABC-123."

The same Tool Registry / Discoverability metadata serves both Builder Agent authoring (§7.1) and Assistant execution (§7.4). Adding a new Tool to the registry simultaneously makes it authorable AND executable conversationally.

#### 7.4.2 High-frequency Tools as always-on (skip the FindResources hop)

For the top 10–20 most-used Tools (Send Email, Find Documents, Find Matter, Summarize Document, Analyze Contract), register them **directly as first-class LLM tools** at session start. The LLM picks them by tool description and doesn't need a `FindResources` hop. This reserves the meta-tool path for the long tail and improves UX for common requests.

A `IsAlwaysOnInAssistant: bool` flag on the Tool Registry entry controls this. A `PromoteInContexts: string[]` field optionally narrows it ("always-on when surface context = MatterForm"). Cheap registration cost; substantial UX improvement.

#### 7.4.3 Default Assistant system prompt — the discipline layer

The default playbook's system prompt is the load-bearing piece for trustworthy execution. The discipline it establishes is what prevents hallucination without limiting the LLM's helpfulness:

> You are the Spaarke AI Assistant — the user's intelligent legal-operations assistant inside their tenant. You have access to their Dataverse records, SharePoint Embedded documents, Azure AI Search, and a registry of Tools and Templates for common workflows.
>
> **Always ground in real data when the user asks about their matters, documents, contacts, finance, or operational data.** Use your tools (`SearchDocuments`, `QueryDataverse`, `FindResources`) to look up the answer; cite the source in your response.
>
> **Never invent facts about the user's data.** Never invent record ids, document names, dates, counterparty names, financial figures, or status. If you can't find what they're asking about, say so and offer to search differently.
>
> **For requests requiring action** (send email, create record, run analysis, run a digest now), use `FindResources` to locate the right Template, then walk the user through required parameters one at a time. Show a confirmation summary before executing anything with external side effects.
>
> **For general legal or domain questions** ("what is an NDA?", "explain force majeure", "what's the standard structure of a term sheet?"), you may answer from general training. Prefix the response with **"Based on general legal knowledge:"** so the user knows it is not grounded in their specific data. If the user prefers grounded answers, suggest searching the firm's reference library (Foundry IQ + curated clause taxonomies).
>
> **When uncertain whether to ground or answer generally, ASK.** "Do you want me to search your matters for this, or answer in general terms?"
>
> **Always offer 2–3 sensible follow-up suggestions** at the end of each response.

This is one system prompt. It is tunable per playbook. It defines the discipline that prevents hallucination without restricting the LLM's autonomy.

#### 7.4.4 General knowledge is a specific Tool, not a fallback mode

A common architectural mistake is to treat "general LLM knowledge" as the default that kicks in when no Tool matches. That model fails for two reasons:

1. **It silently produces ungrounded answers** when the user expected grounded ones. Users see a confident answer about their data that was fabricated.
2. **It conceals what mode the LLM is operating in** — users (and audit) can't tell whether the answer was from their data or from training.

The Action Engine treats general knowledge as a **specific Tool category** — `Classification: AI`, `Discoverability` includes general-legal-concept queries, `EvidenceRequired: false`, `Citations: GeneralKnowledge`. Examples: `AnswerLegalConcept(question)`, `DraftFromGenericTemplate(documentType, instructions)`, `ExplainTerm(term)`. The LLM CAN call these, and when it does, the response is labeled "Based on general knowledge" and audited as such.

This makes the conversational invocation path always discoverable and reviewable. Users always know what mode they got. Auditors always see what kind of answer was given.

#### 7.4.5 Hallucination guardrails — the explicit set

The mechanisms that keep conversational invocation safe. None of these are optional in MVP; together they are why "LLM in conversational control" does not imply "LLM in factual control":

| # | Mechanism | What it prevents | Where it's enforced |
|---|---|---|---|
| 1 | **System prompt discipline** (§7.4.3) | Free-form synthesis of facts about user data | Per-playbook system prompt; tunable |
| 2 | **Tool descriptions as contracts** | LLM invoking the wrong Tool for the intent | Tool Registry `Description` + `Discoverability` |
| 3 | **`EvidenceRequired: true` on Tools that produce findings** | Inference Tools running without supporting evidence; LAVERN Pattern #6 | Runtime guard in dispatch layer |
| 4 | **`IGateResolver` before side effects** (LAVERN reference §10.3) | "Oops, sent the email." | `humanGate.required: true` on Tools with `SideEffects: External \| Write` |
| 5 | **Phase deny-tools** (LAVERN Pattern #8) | Execution-phase Tools running from authoring; authoring-phase Tools running from execution | Mechanical at `IToolHandlerRegistry` dispatch |
| 6 | **Authorization filter** (ADR-008) | LLM proposing actions the user cannot actually invoke | Tool Registry filters discovery results by caller's permissions |
| 7 | **Citation discipline** | Ungrounded answers appearing grounded; grounded answers without provenance | Tool result schema includes `SourceCitations`; UI renders citations vs general-knowledge differently |
| 8 | **Audit on every invocation** (ADR-015) | After-the-fact debugging without trace; compliance gaps | `IToolHandlerRegistry` writes audit on every dispatch |

This is the set. New conversational features must respect all 8 or extend the set. "Skipping a guardrail because it makes the UX cleaner" is the path to GA-blocking incidents.

#### 7.4.6 Composition — multi-step conversational flows

Many requests cascade across multiple Tools. "Find me three similar matters and email the top one's outside counsel" is three Tools chained in one conversation. The agent runtime's `UseFunctionInvocation` supports this directly: LLM calls Tool A, sees the result, calls Tool B with parameters derived from A's result, etc. No new runtime needed.

Each step that mutates state goes through its own `IGateResolver`. The user sees a confirmation summary at each side-effect boundary, not just at the end of the cascade.

This is the same multi-step execution the Action Engine runs for scheduled or signal-triggered Actions; the conversational version is the same execution path with a user driving the loop instead of a cron.

#### 7.4.7 The Assistant default playbook is the configuration of all of the above

In Dataverse terms, the Assistant's behavior at landing is determined by a single playbook record: the "Spaarke Assistant — General" playbook (or the entity-scoped variant resolved from `sprk_aichatcontextmap`). That playbook record specifies:

- **System prompt** (§7.4.3)
- **Always-on Tools** registered for the session (§7.4.2)
- **Default Knowledge sources** scoped for `SearchDocuments`
- **Default `IGateResolver` behavior** (which gate surface — inline chat card, Teams card, Dataverse Precedent Board, etc.)
- **Cost/latency budgets** for this surface (which model tier to use by default — see LAVERN reference §10.4)

Tuning the Assistant's behavior across hosts is a Dataverse record change, not a code change. New Assistant hosts (e.g., a new entity form PCF) add a row to `sprk_aichatcontextmap` and are immediately functional.

---

## 8. Ambient Delivery (where Actions live after authoring)

OpenClaw's UX advantage is that everything lives in one chat surface. Spaarke has more surfaces, which is both a feature and a tax. The Spaarke AI Assistant is one of those surfaces and itself runs in many hosts (§4.5.2 — SpaarkeAi Code Page, Assistant PCF on entity forms, embedded chat in workspaces, Office Add-ins, Copilot, Teams, mobile). At save time the user picks placement from a small menu, with smart defaults based on the trigger:

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

- **Insights Engine** — emits structured `InsightArtifact` signals (Fact / Observation / **Precedent** / Inference per the 4-tier taxonomy in Insights Engine `decisions.md` D-03 + D-46 / LAVERN reference §10.1). The Action Engine subscribes via Monitors on the signal envelope contract (coordination assessment §4.2). **Precedents** are a new artifact type Action Engine AI Tools can cite when drafting content (R2+); supports Mode 4 (Precedent curation) and Mode 6 (weekly briefing) from `ADVANCED-AI-USE-CASE-PATTERNS.md`.
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

*Assistant-surface additions for MVP* (from 2026-05-29 design revision; per §4.5 + §7.4):

- **Default "Spaarke Assistant — General" playbook** in Dataverse with:
  - System prompt per §7.4.3 (grounded-when-data / general-when-asked / cite-or-disclose discipline)
  - Always-on Tools: `SearchDocuments`, `QueryDataverse`, `GetCurrentEntityContext`, plus the three meta-tools (`FindResources`, `GetResourceDetail`, `InvokeResource`)
  - `IGateResolver` default = `InteractiveGateResolver` (inline chat card)
- **Meta-tool implementation** at the BFF: the three meta-tools defined in §7.4.1, backed by the Tool Registry's `Discoverability` semantic index (reuses the `playbook-embeddings` AI Search index from `projects/ai-spaarke-platform-enhancments-r3` or a parallel `spaarke-resource-registry-index`).
- **`sprk_aichatcontextmap`** entries for the initial surfaces: SpaarkeAi Code Page (no-context default) and at least one entity-form context (Matter form) for the Assistant PCF embedding.
- **Assistant PCF on Matter form** as the second conversational surface (validates the multi-surface model in §4.5.2; SpaarkeAi Code Page is the first).
- **`IsAlwaysOnInAssistant` + `PromoteInContexts` flags** on Tool Registry entries — the mechanism by which high-frequency Tools become first-class without LLM discovery hop.

*LAVERN-derived additions for MVP* (per `lavern-pattern-assessment.md`):

- **`IGateResolver` interface + 4 implementations + `sprk_gate_approval` entity + `GateApprovalCard` UI component** (LAVERN reference §10.3) — the canonical approval primitive across Spaarke. See "Approval queue" subsection in §5.
- **Phase deny-tools schema + dispatch enforcement** (LAVERN Pattern #8) — mechanical phase guardrails for the Builder Agent. See "Phase deny-tools" subsection in §5.
- **Tool Registry metadata extension** (joint with Insights Engine per `coordination-assessment-with-insights-engine.md` §4.5 + §4.8) — adds `Classification / CostClass / LatencyClass / Idempotency / AuthMode / Discoverability / ModelTier / PhaseRestrictions / EvidenceRequired` to `ToolHandlerMetadata`.
- **`ModelTier` field on JPS scopes** (LAVERN reference §10.4) — foundation for EvaluatorGate (LAVERN Pattern #2) in R2+; not used in MVP but the schema is in place.
- *Optional*: **`PB-011 Tabular Extraction` Playbook** (LAVERN Pattern #12) if any of the 3–5 starter templates is tabulation-style (e.g., cross-matter rollup, invoice approval queue summary).

*MVP-conditional* (depends on starter template choices):

- **CUAD + MAUD seed-data ingestion** + `sprk_clausetype` Dataverse taxonomy + `spaarke-reference-clauses` AI Search index (LAVERN reference §10.5 + Pattern #9) — **required only if RedFlagDetector ships in MVP starter templates**. If RedFlagDetector defers to R2, this whole workstream defers with it.

### R2

*Original scope*

- Signal-triggered Monitors (Insights Engine coupling).
- Event-triggered Monitors (Dataverse webhooks).
- Approval queue with UI in workspace and Teams.
- Conversational Builder Agent v1 (template matching + parameter elicitation).
- Expanded template library (15+ templates).

*LAVERN-derived additions for R2*:

- **`EvaluatorGate` JPS Action category** (LAVERN reference §10.2 / Pattern #2) — opt-in per-step quality check on high-stakes AI Actions. Requires `ModelTier` from MVP. Default off; per-Action opt-in.
- **`PlaybookExecutionFlow` shared component** (LAVERN Pattern #4) in `Spaarke.UI.Components` — SSE-driven flow UI for multi-step Actions. Mounted by workspace pane, embedded chat, Office add-ins. Consumed by Mode 1 (reactive review) and Mode 2 (proactive monitoring + triage) from `ADVANCED-AI-USE-CASE-PATTERNS.md`.
- **Consume `ISanitizer` shared primitive** (LAVERN reference §10.6 + Pattern #10) — Insights Engine Phase 1 builds it; Action Engine R2 wires it into webhook/signal trigger ingestion paths. See coordination assessment §4.7.
- **Consume `GroundingVerifier` shared primitive** (LAVERN reference §10.6 + Pattern #3) — Insights Engine Phase 1 builds it; Action Engine R2 AI Tools that return findings (RedFlagDetector, draft tools with citations) consume it. See coordination assessment §4.7.

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
- **Approval is a shared primitive.** `IGateResolver` (LAVERN reference §10.3) is the canonical contract across Spaarke. Action Engine MVP is the implementer; never reimplement approval per surface.
- **Phase boundaries are mechanically enforced.** Phase deny-tools (LAVERN Pattern #8) prevents probabilistic agents (Builder Agent) from dispatching tools in the wrong phase. Mechanical enforcement at `IToolHandlerRegistry`, not prompt-coached.
- **Every conversational invocation goes through a registered Tool.** "Just chatting" is not a category. Every user input resolves either to a Tool from the Registry (grounded or general-knowledge type) or to a clarifying question. No free-form synthesis of facts about the user's data, ever. See §7.4.4.
- **General knowledge is a Tool category, not a fallback mode.** Pre-trained LLM knowledge is exposed via specific Tools (`AnswerLegalConcept`, etc.) that label their outputs "Based on general knowledge" and audit accordingly. See §7.4.4.
- **The Assistant runs in many hosts; the runtime is one.** The SpaarkeAi Code Page, the Assistant PCF on entity forms, embedded chat in workspaces, Office Add-ins, M365 Copilot, Teams, mobile — all share the same `SprkChatAgent` runtime, the same Tool Registry, and the same conversational invocation pattern (§7.4). New hosts add a `sprk_aichatcontextmap` row, not a new code path. See §4.5.2.

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
- **Do not let the Assistant free-form synthesize answers about user data.** Every input must resolve to a Tool from the Registry (grounded type for user data, general-knowledge type when explicitly invoked) or to a clarifying question. If a "general chat fallback" appears in the system prompt or routing logic, fix the routing — never allow ungrounded synthesis on user data.
- **Do not allow side-effect Tools to execute conversationally without `IGateResolver`.** If a Tool's `SideEffects: External | Write` and `humanGate.required: true`, dispatch must route through the gate. "We'll add confirmation later" is the path to "the AI sent the wrong email" incidents. The mechanical check belongs at dispatch.
- **Do not register a new Assistant host as a new code path.** New conversational surfaces (entity-form PCF, command-bar embedded chat, etc.) configure via `sprk_aichatcontextmap` + Tool Registry flags. Hosts that need a code change to add tools or change behavior have a design smell — investigate before shipping.

---

## 12. Open Questions for Implementation Spec

Resolved before MVP build (originally listed; some now superseded by LAVERN adoption):

- **Runtime topology** (the central question). Microsoft Agent Framework vs existing BFF orchestrator. Azure-native scheduling vs BFF scheduler. Hybrid composition. Output: a runtime ADR. **Coordination recommendation per §6**: adopt Insights Engine's Hybrid D — BFF for dispatch + Azure-native Function scheduler.
- Exact entity schema for Action Definition, Template, Instance, Run, Monitor, Approval. Approval entity now `sprk_gate_approval` per LAVERN reference §10.3.
- Relationship and migration path between today's playbook entity and the Action / Template / Instance model — do they merge, or does the new layer sit above the existing playbook? **Coordination recommendation per coordination assessment §4.1**: Action Engine sits above the JPS playbook entity (Action Definition → Playbook reference is a foreign key).
- Tool Handler registration mechanism (attribute, configuration, hybrid).
- Variable-type control library scope and component shapes (lives in the shared component library per ADR-012).
- Builder Agent prompt/skill specification and few-shot example set.
- Cost-attribution model for AI tool invocations within Actions (ties to ADR-016).
- Cross-solution layering rules for Templates (system → ISV → customer overrides).
- Cancellation and recovery semantics for long-running multi-step Actions.
- ~~Approval routing rules and escalation timing~~ — **resolved by `IGateResolver` (LAVERN reference §10.3)**: 5-min default timeout → auto-reject, surface routing via `SurfaceHint`, declared `AuthorizedApproverRoles`.

Newly open (introduced by LAVERN adoption):

- ~~**LAVERN ADR ratification timeline**~~ — **resolved 2026-05-29** (per `spec.md` owner clarification): LAVERN-derived patterns (`IGateResolver`, Phase deny-tools, extended Tool Registry metadata) are formalized inline as part of THIS project's design. No separate ADR ratification required. LAVERN is an external Apache-2.0 reference project (see §2 clarifying note); `LAVERN-ANALYSIS-AND-PLAN.md §10.x` references are pattern inspiration only.
- **SME workflow design for Precedent curation surfaces** — cross-cuts to Insights Engine Phase 1.5 (Mode 4 from `ADVANCED-AI-USE-CASE-PATTERNS.md`). Action Engine R2+ surfaces (workspace pin, embedded chat, etc.) may render the SME confirmation queue — surface choice is a product decision deferred to real customer input.
- **CUAD + MAUD seed-data scope** — depends on whether RedFlagDetector ships in MVP. If yes, ADR 10.5 + Pattern #9 are MVP-blocking; if no, R2.
- **`PB-011 Tabular Extraction` Playbook scope** — depends on which 3–5 starter templates ship MVP (Pattern #12 is optional MVP).

Newly open (introduced by 2026-05-29 design revision — §4.5 + §7.4):

- **Resource Registry vs Tool Registry naming**. §7.4 introduces "meta-tools that expose the Tool Registry to the LLM." The 2026-05-28 design conversation surfaced "Resource Registry" as an alternative framing that includes Action Templates as first-class entries alongside Tools. Decision: do `ActionTemplate` records get indexed under the same `Discoverability` schema as Tools (one registry, two record types) or as a parallel registry? Lean: one registry — simplifies the LLM's discovery surface.
- **`sprk_aichatcontextmap` schema scope**. The 2026-05-29 revision references this as the mechanism for surface-context-to-playbook mapping. The exact schema (entity type, optional view id, optional command-bar context, etc.) is implementation-spec, not design-doc. To be specified before MVP build.
- **Meta-tool dispatch latency budget**. `FindResources` runs semantic search on every invocation; with always-on Tools registered, the meta-tool path is the long-tail fallback. Latency budget: <200ms p95 for `FindResources` so the chat experience stays snappy. To be validated in the runtime/hosting architecture spike (§6).
- **The first Assistant PCF embedding (Matter form) scope**. The 2026-05-29 revision lists this as MVP scope for validating the multi-surface model in §4.5.2. The exact PCF component (new vs reuse Spaarke.UI.Components SprkChat) and its visual integration with the form (sidebar, floating button, embedded panel) is a product/design decision deferred to a small UX spike.

---

*This document is the project design. Implementation specs (`spec.md`), the plan (`plan.md`), task POML files, and the runtime ADR follow from this design.*
