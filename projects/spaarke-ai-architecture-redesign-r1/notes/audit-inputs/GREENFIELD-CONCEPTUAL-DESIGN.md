# Spaarke AI — Greenfield Conceptual Design

> **Status**: DRAFT **v0.2** — 2026-07-05. v0.1 reviewed with the operator
> (Q&A recorded in §9); revisions: §4.2 rewritten to the **Action + Binding**
> model (single `capability` table rejected — reuse-first + one-Action-many-
> configs), playbook fate recorded, **OQ-2 and OQ-4 resolved** in §8.
> Remaining open before Step 3: OQ-1 (classifier vs loop), OQ-3 (slot-fill
> engine vs loop-native).
> **Premise**: *"As if you were a senior developer working in our platform but with
> no preexisting AI architecture or components, what would that design.md look
> like?"* This document designs the Spaarke AI platform from the **product
> objectives only** — the §0-3 use-case catalog, session-graph framing, and locked
> product decisions (D1-D6) of the canonical doc — deliberately ignoring every
> existing AI component, table, and mechanism. It is written against the
> Microsoft platform as it exists in **July 2026**, not as it existed when the
> current code was accreted (2025 → early 2026).
> **Purpose**: a clean-sheet reference to compare against the constrained v0.3
> design (§4-7 of the canonical doc). Where the two disagree, the disagreement is
> a decision the operator should make explicitly — that comparison is §8 here.
> **Not a migration plan.** Step 3 (`SPAARKE-AI-MIGRATION-MAP.md`) decides how far
> toward this shape the migration goes.

---

## 1. What we are building (requirements restated, one paragraph)

A portfolio of AI capabilities for corporate legal operations, invocable from any
Spaarke surface against any input with results delivered to any destination, where
each capability composes with prior and subsequent capabilities in one flowing
session (canonical journey: upload → summarize → chat → create matter → draft
letter → create task). Hard constraints: tenant boundary absolute; Dataverse
security applies to AI (never an authorization side-channel); human confirmation
before side effects; cost is a first-class metric; **behavior is configuration,
code is shape**; every output is grounded (Consumer-controlled, tool-cited,
confirmation, or refusal — never free-form completion).

### 1.1 Capability families the design must cover (incl. umbrella-project objectives)

Beyond the §3 UC catalog, two umbrella programs define capability families with
shipped or committed contracts (synthesis:
`notes/agent-findings-engine-projects.md`):

- **Insights** — evidence-gated contextual claims: four artifact classes
  (Fact / Observation / Precedent / Inference) with per-class trust and storage
  rules, mechanical grounding verification, evidence-sufficiency gating with
  honest structured decline, an ask (curated) + search (RAG) hybrid, and
  topic-scoped summary cards persisted to host records with TTL pre-warm. In
  this design's terms: a family of **capabilities with a shared output envelope
  and shared honesty middleware** — the grounding/decline machinery is *gate
  middleware on the executor*, not a separate engine. The four-artifact
  envelope and the decline-when-insufficient behavior are non-negotiable
  product commitments this design carries forward.
- **Actions** — user- and system-triggered units of work with approval gates
  (5 gate types incl. timeout), template/instance/run lifecycle, scheduled
  dispatch, and discoverability ("find me something that can do X"). In this
  design's terms: **capabilities + the Confirmation Gate + Event Rules +
  catalog search** — the planned Action Engine vocabulary (tool registry,
  gate resolvers, meta-tools) maps 1:1 onto the Tool Gateway, Confirmation
  Gate, and Agent Turn Runtime; it needs no parallel subsystem.

## 2. Design stance (the five bets)

**B1 — One brain, not a routing stack.** In 2026 the proven pattern for
"understand what the user wants and do it" is a single LLM agent loop with
function calling over a *closed, typed tool surface* (this is how Claude Code,
Copilot agents, and Foundry agents work). We do NOT build a bespoke intent
pipeline (regex → vector match → reranker → classifier). Intent resolution is
what the model is best at — provided the action space is closed and typed. The
deterministic paths (chip click, scheduled trigger, event rule) *bypass* the
brain entirely; the brain handles exactly the part that is genuinely
probabilistic: free-text intent.

**B2 — Capabilities are tools.** A curated capability ("NDA review",
"matter pre-fill") is exposed to the agent loop *as a tool* — name, description,
input schema. Its implementation is prompt-controlled and schema-validated. There
is no separate "dispatcher" that maps utterance → capability: the loop either
calls a capability tool, calls primitive tools to compose an answer, or says it
cannot help. One mechanism, three outcomes — and all four grounded-output classes
(D5) fall out naturally.

**B3 — Buy the substrate, write the domain.** Microsoft ships the substrate as
first-class services in 2026: Dataverse MCP server (CRUD + search under user
security context), Azure AI Search (RAG), Document Intelligence (extraction),
Azure OpenAI structured outputs. We write ONLY the domain layer: the capability
catalog, the session ledger, the confirmation gate, the legal-ops prompts, and
the surfaces. Anything Microsoft runs as a service, we consume rather than
reimplement. *(§5.3 records the July-2026 facts this bet rests on.)*

**B4 — No interpreter.** We do not build a graph/workflow interpreter that
executes maker-authored control flow from data. The operator's own R7 lesson
("a config-table-with-rules IS an interpreter") is adopted as a founding
principle here: **control flow is code; behavior is data.** A capability is
either `prompted` (one structured LLM call — the overwhelming majority) or
`coded` (a registered C# workflow class for genuinely compound flows — briefing
collect-narrate-deliver, negotiation-cycle steps). Makers tune prompts, schemas,
models, routing, chips, thresholds — never branches and loops.

**B5 — The session ledger is the product.** Composition — the §3.0 bet that
differentiates Spaarke — reduces to one artifact: an append-only, addressable
**session ledger** of everything that happened (uploads, capability outputs, tool
chains, confirmations, widget events). Every capability reads from it; every
capability writes to it; the UI renders projections of it. If the ledger is
right, composition is free. If it is missing, composition is re-implemented
pairwise per capability forever.

## 3. The architecture (one diagram)

```
┌─ SURFACES ─────────────────────────────────────────────────────────────────┐
│  Assistant (chat+workspace) · record forms · wizards · Office · SPA ·      │
│  scheduler · inbound email                                                 │
└───────┬────────────────────────────────────────────────────────────────────┘
        │  three trigger kinds, three entry paths:
        │  (1) EVENT  → Event Rules (deterministic, no LLM)
        │  (2) CLICK  → direct capability invocation (deterministic, no LLM)
        │  (3) TEXT   → Agent Turn (the one brain)
┌─ SESSION RUNTIME (BFF) ────────────────────────────────────────────────────┐
│                                                                            │
│   SESSION LEDGER (append-only, addressable)                                │
│   docs · outputs · tool-chains · turns · widget events · pending gates     │
│        ▲ read by everything          ▼ written by everything               │
│                                                                            │
│   AGENT TURN RUNTIME (one bounded function-calling loop per text turn)     │
│     tool surface =                                                         │
│       CAPABILITY TOOLS (catalog: prompted | coded)                         │
│     + PRIMITIVE TOOLS (dataverse.* via MCP · document.* · search.* ·       │
│       email.draft · notify.*)                                              │
│     middleware: tenant guard → cost meter → CONFIRMATION GATE (side        │
│       effects suspend → user approves → resume) → telemetry                │
│                                                                            │
│   CAPABILITY EXECUTOR                                                      │
│     prompted: render prompt template + structured-output LLM call          │
│     coded:    registered C# workflow (may call executor + tools itself)    │
│                                                                            │
│   OUTPUT ROUTER (disposition: informational | work_product | overlay |     │
│     email | record | notification) → SSE to surfaces / write-shapes        │
└───────┬────────────────────────────────────────────────────────────────────┘
        │ typed SSE events
┌─ CLIENT RUNTIME ───────────────────────────────────────────────────────────┐
│  Chat control · event bus · widget registry · ONE schema-driven streaming  │
│  widget + specialized widgets · chips (capability-declared)                │
└────────────────────────────────────────────────────────────────────────────┘
```

### 3.1 The three entry paths, precisely

| Path | Trigger | Mechanism | LLM involved in *deciding*? |
|---|---|---|---|
| **Event** | upload, form open, schedule, inbound email | **Event Rules**: manifest rows `{event → [capability, order]}` under bounds (cost cap, opt-out, bulk top-1, explicit-command supersede) | No — the rule is data; the capability itself may use an LLM |
| **Click** | chip, ribbon button, wizard action, card | direct `invoke(capability_id, args)` — chip carries the id (D4) | No |
| **Text** | user types | **Agent Turn**: one bounded loop; the model picks a capability tool, composes primitives, asks a clarifying question, or refuses | Yes — this is the only place |

This dissolves the "dispatch layer" as a separate subsystem. There is no
classifier to tune, no thresholds to calibrate, no vector index of trigger
phrases to maintain — the capability tool descriptions ARE the intent surface,
and improving routing = improving a description (a maker edit, versioned in the
catalog). Confidence-gated confirmation (product D1) survives as *behavior*:
capability tools whose declared `risk` requires it, and any ambiguous case where
the model asks before acting — plus the hard gate on side effects (§4.4).

### 3.2 A turn, end to end (the NDA walkthrough replayed)

1. Upload NDA → **Event path**: rule `document_uploaded → [classify, summarize]`
   runs both capabilities directly. Classify writes `ledger.outputs[classify@t1]`;
   summarize's prompt template references it; low classifier confidence →
   the summarize invocation is preceded by a confirmation turn (rule-declared).
2. Summary renders `informational` + chips from the capability's declared
   next-steps. Ledger has both outputs, addressable.
3. User clicks "Flag issues" → **Click path**: `invoke(nda-review-vs-library,
   {doc: ledger.docs[0]})`. Output disposition `informational + overlay` →
   Assistant narrative + widget highlight event.
4. User types "make a to-do for this" → **Agent Turn**: model sees capability
   tools incl. `create_task`; calls it; input schema requires `due_date`,`owner`
   — missing → the loop *asks* (structured elicitation is native to a
   function-calling loop; no bespoke slot-fill engine). User answers; model
   re-calls with full args.
5. `create_task` is `side_effect: write` → **Confirmation Gate** suspends the
   call… but the two-turn elicitation already constitutes explicit user intent,
   so the gate's policy (`conversational-confirm-suffices`) admits it. Record
   written via `dataverse.create` carrying ledger refs (`source_analysis:
   outputs[nda-review@t3]`). ✅ + link + next chips.

Every P1-P10 proposition from the canonical walkthrough holds, with roughly half
the machinery of the constrained design.

## 4. Components (the whole platform is ~14)

### 4.1 Session Ledger (the core)

Append-only per-session store; Redis hot + Cosmos durable; entries typed:

```
LedgerEntry = Doc | Output | ToolChain | Turn | WidgetEvent | Gate
Output: { key: "cap-id@t{n}", capability_id, uc_id, payload (schema-validated),
          disposition, source_refs[], widget_id?, created_at }
```

Rules: writes are universal and automatic; reads are by key or by typed query
("latest classify output"); payloads size-capped with blob pointers; the ledger
is the ONLY carrier of cross-capability context (no capability reads the screen).
Retention: session TTL → durable archive of outputs onto the Dataverse record
(matter timeline) where a capability declares it.

### 4.2 Capability Catalog + Executor (REVISED v0.2 — Action + Binding, not a new entity)

> **Revision note (2026-07-05, operator review)**: v0.1 sketched a single new
> `capability` table. That was rejected on review for two reasons: (1) it
> breaks the **one-Action-many-configs** reuse pattern the overlap analysis
> (§3.9.8 canonical doc) depends on — the same briefing Action serves a widget
> binding AND a scheduled-email binding; a single row would duplicate prompts;
> (2) it violates the reuse-first rule when two fit-for-purpose tables with
> reader services already exist. **"Capability" is hereby vocabulary, not
> schema**: the concept of an Action × Binding pair, projected to the agent
> loop as one tool.

**The catalog is two existing tables, refined:**

**`sprk_analysisaction` (Action — the execution unit)**. Already owns the JPS
prompt + output schema + scope refs. Refinements:

| Field | Meaning |
|---|---|
| `kind` | `prompted` (default) \| `coded` (+ workflow class ref) — Wave-11 code-defined composites get a first-class home INSIDE the Action concept |
| `input_schema` | typed args incl. `ledger_resolution` per arg ("latest summarize output.entities") |
| `model_tier` (default) | overridable per binding |
| prompt, output schema, skills/knowledge/persona refs | unchanged — already there |

**`sprk_playbookconsumer` (Binding — the invocation unit)**. Already owns
consumertype/code, environment, priority, target FK. Refinements:

| Field | Meaning |
|---|---|
| tool description + match surface | the intent text the agent loop sees |
| `disposition` | informational \| work_product \| overlay \| email \| record \| notification |
| `next_steps` | `[{target_binding_id, chip_label}]` |
| `risk` | none \| confirm-when-uncertain \| always-confirm |
| `on_event` | Layer 0 / Event-path membership |
| `surfaces`, `enabled`, per-binding model override | where it appears |

The Executor: `prompted` = render template with resolved args + ledger refs →
one structured-output call → validate → ledger write. `coded` = instantiate the
workflow class → orchestrates in C# (calling the executor and tools as needed;
reading its prompts from child Action rows) → ledger write. **~30-60 bindings
over a smaller set of Actions cover the §3 catalog**; the ten overlap
consolidations (§3.9.8) become one Action each with N bindings — prompts stay
single-sourced. Authoring UX presents "Action + its binding" as one flow for
the majority 1:1 case; "add another binding" is the advanced path.

**What happens to playbooks** (operator-confirmed 2026-07-05; full resolution
in canonical doc §4.2.1): a playbook is at most a **system-defined composite
Action** — control flow is hard-coded (C# workflow, or frozen developer-
authored graphs for existing Insights pipelines); the business-analyst surface
is the prompt-based scopes (Actions/Skills/Personas) and binding metadata,
never graphs. Single-node playbook wrappers dissolve into Action + binding;
playbook-as-dispatch-unit retires; "playbook" survives as product language.
PlaybookBuilder's future = the BA scope/prompt/binding editor (canvas
de-scoped).

### 4.3 Agent Turn Runtime

A bounded function-calling loop (Microsoft.Extensions.AI `IChatClient`,
or hosted in Azure AI Foundry Agent Service — deployment choice, not
architecture): system prompt = persona + tenant framing + session digest;
tools = capability tools (catalog projection) + primitive tools filtered by the
caller's Dataverse permissions; budget ≤ N tool calls/turn; every read result
cites; chain persisted to the ledger. Refusal is the loop's honest terminal
state, rendered through a tenant template. Grounded chat over docs/records is
this same loop with a scoped tool subset — not a separate feature.

### 4.4 Tool Gateway + Confirmation Gate

- **Primitive tools**: `dataverse.*` — **consume the first-party Dataverse MCP
  server** (user-context auth; the tenant/security guarantee is Microsoft's
  contract, not our reimplementation) *(pending §5.3 verification of the
  server-side user-context pattern)*; `document.*` (SPE + extraction);
  `search.*` (AI Search); `email.draft` (Graph); `notify.*`. Each declares
  `side_effect_class`, `permission_scope`, `budget_class`.
- **Confirmation Gate**: middleware on every tool/capability invocation with
  `side_effect_class ∈ {write, communicate}` OR `risk` policy match. ONE pending
  store; suspend → render confirm turn/chips → resume or cancel. Policies:
  `explicit-click`, `conversational-confirm-suffices`, `always-modal`.

### 4.5 Output Router + client runtime

Disposition-driven fan-out (Assistant narrative / workspace tab / widget overlay
/ email draft / record write / notification), always after the ledger write.
Client: one chat control; one typed event bus; one widget registry; ONE
schema-driven streaming widget renders any `prompted` capability's output from
its `output_schema` (specialized widgets — document viewer, redline, calendar —
register for their types); chips carry capability ids.

### 4.6 Proactive plane

The scheduler and inbound-email listener are just **Event path** clients:
`schedule:daily-briefing → invoke(daily-briefing)`, `email_received →
invoke(email-triage)`. Briefing/triage are `coded` capabilities whose outputs
bind to email/notification dispositions. No separate "jobs architecture" for AI
— jobs invoke capabilities.

### 4.7 Component count

Session Ledger · Capability Catalog · Capability Executor (prompted/coded) ·
Agent Turn Runtime · Tool Gateway · Confirmation Gate · Output Router · Event
Rules service · telemetry/cost meter — **9 server components**. Chat control ·
event bus · widget registry · streaming widget + specialized widgets · page
shell — **5 client components**. Everything in §3 of the canonical doc is
reachable through these.

## 5. Platform services consumed (July 2026)

### 5.1 Azure OpenAI / Foundry
Structured outputs for every `prompted` capability; model tiers per catalog row;
the Agent Turn loop self-hosted via Microsoft.Extensions.AI **or** hosted in
Foundry Agent Service — the catalog and ledger contracts are identical either
way, so this is a deployment decision made on ops grounds (latency, egress,
observability), not an architectural one.

### 5.2 Azure AI Search + Document Intelligence
Tenant-partitioned indices for session docs and tenant corpus; extraction
pipeline feeds both the ledger (`Doc` entries carry extracted text) and the
indices. One indexing pathway, not three.

### 5.3 Dataverse MCP (facts as of July 2026 — full brief: `notes/research-dataverse-mcp-2026-07.md`)

The first-party server is **GA** (`/api/mcp`: `describe`, `read_query`,
`search_data`, `create_record`, `update_record`, `delete_record`, file tools,
skill CRUD) and runs every call **under the caller's Dataverse security roles**
— delegated user tokens only, no app-only flow. Two catches for a greenfield
build: (1) calls from agents outside Copilot Studio are **Copilot-credit
metered** for users without D365 Premium / M365 Copilot USL — a per-tool-call
COGS line; (2) the BFF-side OBO exchange for the delegated `mcp.tools` scope is
undocumented (spike required). Greenfield position: **the Tool Gateway's
`dataverse.*` contracts ARE the GA MCP tool contracts**; whether a given tool
executes via `/api/mcp` or via our OBO Web API layer is a per-tool transport
decision driven by metering + the spike result. The MCP C# SDK
(`McpClientTool : AIFunction`) makes the two transports interchangeable inside
the same `IChatClient` loop. The Agent Turn loop stays self-hosted: Foundry
Agent Service's MCP support is GA, but its user-context mechanism (interactive
per-user OAuth consent) doesn't fit a headless multi-tenant BFF.

## 6. Configuration model (what a maker touches)

One table (`capability`) + one child concern (event rules) + the existing scope
vocabulary (skills/knowledge/personas as prompt-composition inputs). A maker
can: create/tune a prompted capability end-to-end (prompt, schema, disposition,
chips, risk, surfaces, model tier) with zero deploys; re-route per environment;
bind capabilities to events; edit chip labels and next-steps; set the refusal
template. A maker cannot: author control flow (coded workflows are engineering
deliverables); add primitive tools; change gate policies platform-wide.

## 7. What this design deliberately does NOT have

- **No intent-classification pipeline** (regex, vector match, reranker,
  intent hints) — capability descriptions + the loop replace it.
- **No graph/workflow interpreter and no node-executor vocabulary** — compound
  control flow is code (B4). The 33-executor concept dissolves into: prompted
  capabilities (the AI executors), primitive tools (the mutation executors),
  coded workflows (the control-flow executors), and the Output Router (the
  delivery executors).
- **No per-surface AI clients** — every surface speaks
  `invoke(capability_id, args)` + SSE; no surface embeds its own summarize/SSE
  parser/prompt logic.
- **No parallel routing config surfaces** — the catalog row's
  environment/enabled fields are the only routing truth.
- **No unwired scaffolding** — a capability ships when its catalog row, its
  executor path, and its surface affordance land together, or not at all.

## 8. Comparison with the constrained v0.3 design (the decision surface)

| Dimension | Greenfield (this doc) | Constrained v0.3 (canonical §4-7) | The decision this exposes |
|---|---|---|---|
| Free-text intent | Agent loop over capability tools (B1/B2) | L2 classifier stack (vector index + reranker + thresholds) feeding L3 loop | **OQ-1**: is a maintained classifier stack justified vs letting the loop dispatch? v0.3 reuses audited, working components; greenfield removes a whole tunable subsystem. Cost per turn is comparable (both call a model); determinism differs (L2 is replayable; loop dispatch is model-judgment + audit trail). |
| Compound orchestration | `coded` workflows (C# classes) + loop composition; **no interpreter** | Keeps `PlaybookOrchestrationService` (33 executors) as one of three shapes | **OQ-2 RESOLVED 2026-07-05** (canonical §4.2.1): the operator's R7 playbook definition (system-hard-coded structure; BA edits prompt-scopes only) removes the maker-graph promise from requirements. No maker graph authoring ever; new composites = coded workflows; existing Insights pipelines stay on the engine as a maintained-but-frozen representation, retired by attrition. No forced re-migration. |
| Slot-fill | Native loop elicitation (model asks for missing schema args) | Dedicated `SlotFillEngine` + `in_progress_dispatch` state | **OQ-3**: engine vs emergent. Greenfield is less code; v0.3 is more deterministic about mid-fill turn semantics. |
| Session context | Unified ledger (all entry types, one contract) | `session.outputs` grafted onto existing ChatSession | Same destination; ledger is the cleaner end-state model for Step 3 to aim at. |
| Dataverse tools | MCP contracts at the boundary; per-tool transport choice (§5.3) | Native typed handlers, GA-MCP-mirrored contracts, swap-ready (D10 revised) | **OQ-4 RESOLVED 2026-07-05**: the two designs now converge — contracts = GA MCP surface; runtime transport = OBO Web API now (metering + proven auth), `/api/mcp` per-tool later pending the OBO spike. |
| Manifest | ~~ONE `capability` table~~ → **REVISED v0.2**: Action + Binding (two existing tables refined, §4.2) | Extend `sprk_playbookconsumer` + `sprk_analysistool` (D9) | **CONVERGED 2026-07-05**: the two designs now agree — refine `sprk_analysisaction` (execution unit) + `sprk_playbookconsumer` (invocation unit); "Capability" is vocabulary, not schema. Playbook/node tables persist only for frozen Insights composites per OQ-2 resolution. |
| Component count | ~14 | 21 mapped (many kept legacy) | The gap IS the migration cost/benefit question. |

**Reading guide for the operator**: where the two designs agree (ledger/outputs
store, confirmation gate unification, disposition routing, chips-as-ids,
event-rule Layer 0, closed catalogs, single client dispatch path, no new
tables beyond need), treat the agreement as settled direction. Where they
disagree (OQ-1..OQ-4), each is a genuine fork with a real trade — the migration
map should not proceed past the affected components until each is called.

---

## 9. Operator review Q&A (2026-07-05) — decisions and rationale record

Condensed from the review session; kept here because these answers ARE the
design rationale future readers will look for.

**Q1 Does this deliver requirements fully?** Yes — §3 catalog, P1-P10, D1-D6,
umbrella commitments all covered. One qualification: D1's confidence threshold
is delivered as *behavior* (risk-classed gates + ask-when-uncertain), not as a
calibrated per-Consumer score — the loop emits no calibrated confidence.

**Q2 Then why build the constrained v0.3?** Don't — as a destination. v0.3 is
the **migration atlas** (component dispositions, manifest schema, invariants);
its proposed NEW machinery (L2 classifier stack, SlotFillEngine) should not be
built if the greenfield alternative is available — spending new-build budget on
continuity-justified architecture is how the ten-mechanism drift happened.

**Q3 No classifier — hallucination protection? Matching accuracy?** Two
different questions. Hallucination protection was never the classifier's job:
grounding is (schema-enforced structured outputs, closed typed tool set, cited
reads, gated writes, honest refusal) — all kept in full. Matching among 30-100
described tools is core 2026 frontier-model competency, hardened by
deterministic context pre-filtering (only session-valid tools offered),
maker-editable descriptions, schema-triggered elicitation on misfires, and
gates on side effects. Note v0.3's L2 was ALSO probabilistic (embeddings +
LLM rerank) — the choice was whose probabilistic judgment, not whether.

**Q4 What do we give up?** Greenfield gives up: the calibrated confidence dial;
replayable dispatch decisions (replaced by golden-utterance eval suites in CI);
the maker-graph promise (resolved moot — see OQ-2); R7 dispatch-machinery
continuity. Constrained gives up: ~7 fewer components' simplicity; building two
new subsystems whose only justification is mechanism continuity; a permanent
L2↔L3 threshold seam; industry-pattern alignment.

**Q5 Can we layer sophistication in later?** Yes — every v0.3 sophistication is
a bolt-on to the loop, not vice versa: deterministic pre-filters (day 1) →
golden-utterance CI evals → embedding retrieval as a tool-list PRE-FILTER at
100+ catalog scale (the graceful re-entry point for L2 machinery, as an
optimization not a decision-maker) → post-hoc verifier for high-risk classes →
declarative support for repeating workflow *shapes* (parameters, never control
flow).

**Q6 Session persistence + chat memory?** Same three audited tiers (Redis hot /
Cosmos durable / Dataverse cold); the ledger changes WHAT persists, not where.
Memory = maintained session digest (rolling compaction, generalizing today's
summarize@15) + addressable recall via tools (memory beyond the window is a
tool call, not a bigger prompt) + durable pins (user/matter/tenant) + record-
persisted work products (widgets-r1 pattern).

**Q7 Multi-surface output (Assistant↔Workspace↔Context)?** Server: ledger write
first, then disposition-driven typed SSE (informational → Assistant;
work_product → workspace tab load; overlay → targeted widget update; sources →
context channel). Client: PaneEventBus channels + widget registry + streaming
widget — kept from today. Widgets emit user actions back as ledger events
(cross-pane interactions = channel events referencing ledger keys). Any surface
speaks the same invoke + SSE contract, rendering only the channels it hosts.

**Q8 Capabilities vs Actions; Skills/Personas?** Resolved into the §4.2
Action + Binding model: Capability = vocabulary for an Action × Binding pair.
Skills = prompt-composition fragments (unchanged); Personas = capability-level
voice AND session-level assistant identity (both `sprk_aipersona`); Knowledge =
grounding bindings scoping retrieval. Maker vocabulary compresses from
action+playbook+node+consumer+scopes to **Action + Binding + scopes**.

**Q9 Playbooks?** Four roles, four fates: single-step wrappers dissolve into
Action+Binding; dispatch-unit role retires with dispatch consolidation;
multi-step role = system-defined composite Action (coded workflow, or frozen
developer-authored graphs for existing Insights); legal-sense "firm playbook"
is Knowledge content, untouched. Resolution per the operator's R7 definition —
playbooks were always system-hard-coded; the BA surface is prompt-based scopes.
PlaybookBuilder becomes the BA scope/prompt/binding editor.

---

*End v0.2.*
