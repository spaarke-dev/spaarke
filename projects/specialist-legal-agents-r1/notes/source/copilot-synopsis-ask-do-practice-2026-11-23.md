> **Source:** a synopsis Microsoft 365 Copilot wrote from the owner's Copilot conversation of 2026-11-23, after the G8 Agent Builder test. The owner saved it as "Download the Markdown synopsis.md". It is copied here **verbatim, for reference**. Claude's review and its corrections are in design.md v0.6, §0.5 and §3.12. Its experiments map onto G8-P2, G9 and G10.

# Spaarke AI Agent Capability & Surface Architecture — Discussion Synopsis

**Status:** Working architecture note / Phase 0 synthesis  
**Purpose:** Capture the emerging capability model, user experience split, technical architecture, test evidence, design principles, and research agenda for Spaarke AI integration with Microsoft 365 Copilot.

## 1. Executive Summary

The emerging Spaarke AI model is best understood as **one AI entry ecosystem with three user intents: Ask, Do, and Practice**.

| Intent | Primary surface | User mental model | Spaarke role |
|---|---|---|---|
| **Ask** | Microsoft 365 Copilot Chat | “Help me understand something.” | Spaarke data/tools are ambient through the federated MCP connector. |
| **Do** | Copilot Cowork | “Carry out this work for me.” | Spaarke participates alongside Microsoft 365 sources/tools in multistep delegated work. |
| **Practice** | Named Spaarke agent, ideally accessible from the Copilot experience | “Perform this professional workflow correctly.” | Spaarke owns the governed workflow, domain reasoning, tool policy, process state, and controls. |

The important architectural principle is that **Spaarke should not create agents simply to expose Spaarke data or CRUD/search tools**. Those capabilities should be ambient through the federated connector. A dedicated Spaarke agent is justified when Spaarke adds a governed professional workflow, specialist reasoning, persistent process state, verification, or domain-specific controls.

This produces a bifurcated orchestration model without requiring a bifurcated user experience:

1. **Microsoft orchestration path:** Copilot Chat/Cowork + Spaarke federated MCP connector.
2. **Spaarke-controlled agent path:** purpose-built agents + direct Spaarke MCP, potentially combined with Microsoft Work IQ/M365 context.

The desired long-term UX is **one front door (Microsoft 365 Copilot), multiple clearly differentiated modes**, rather than requiring users to understand connector/runtime architecture.

---

## 2. Phase 0 Evidence So Far

### Spaarke federated MCP source

The Spaarke MCP source is configured and connected in Microsoft 365 Copilot. The observed configuration exposes:

- 4 read tools, always allowed.
- 2 write/delete tools.
- MCP endpoint: `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations`

### Regular Microsoft 365 Copilot Chat test

A regular Copilot Chat request:

> Using Spaarke MCP, call `spike_whoami`

completed successfully. The response returned `Status: ok` plus Dataverse/user identity claims, including a Dataverse `systemUserId`, tenant ID, object ID, audience validation, and the `user_impersonation` scope.

**Conclusion:** the Spaarke MCP server, federated connector registration, user authentication, MCP tool discovery, and `spike_whoami` execution path are working in regular Copilot Chat.

### Agent Builder test agent

The same capability could not be invoked from the Phase 0 Agent Builder agent. The agent could see Spaarke skills whose declarations referenced `allowed-tools: mcp__spaarke`, but the agent runtime did not expose the actual `mcp__spaarke` callable tool catalog.

**Conclusion:** declaring an MCP dependency in a skill does not, by itself, prove that a user's connected federated MCP source is injected into that Agent Builder agent's executable tool context.

This isolates the current issue primarily to **agent/source/tool binding**, rather than the Spaarke MCP implementation itself.

---

## 3. Capability Model: Ask → Do → Practice

### 3.1 Ask — Copilot Chat

Use Chat for fast questions, retrieval, synthesis, explanations, summaries, and drafting where the main outcome is an **answer or insight**.

Spaarke examples:

- “Find the Acme patent matter.”
- “What is the status of PAT-199531?”
- “What documents are related to this matter?”
- “Summarize recent activity.”
- “Which events are overdue?”

**Design principle:** users should not need to select a Matter Search Agent, Document Search Agent, Patent Search Agent, etc. Search/retrieval capabilities are tools and should be ambient in Chat through the Spaarke connector.

### 3.2 Do — Cowork

Use Cowork when the outcome is **delegated multistep work**, especially work spanning Microsoft 365 apps, files, communications, research, and connected systems.

Spaarke examples:

- “Research Project Atlas and prepare a briefing.”
- “Find relevant Spaarke matters and create an Excel report.”
- “Gather the matter, associated documents, relevant emails, and meeting context and prepare a briefing package.”

A useful shorthand is:

- **Chat = answer me.**
- **Cowork = work for me.**

Cowork and Spaarke agents can both perform actions, so “actions” cannot be the boundary between them.

### 3.3 Practice — Purpose-built Spaarke Agent

Use a Spaarke agent where **the method matters, not only the outcome**.

A Practice agent owns a bounded professional capability and may require:

- prescribed playbooks;
- process stages;
- tool policies;
- structured outputs;
- fallback positions;
- escalation rules;
- verification requirements;
- persistent workflow state;
- organization-specific professional controls;
- domain-specific model/tool routing.

Examples:

- Contract/NDA Review Agent.
- Legal Hold Agent.
- Matter Intake Agent.
- Invoice/Outside Counsel Review Agent.
- Investigation Agent.
- IP/Patent workflow agent.

Example distinction:

- “What does this NDA say?” → **Chat**.
- “Review this NDA against the Legal Department NDA playbook and apply the prescribed review protocol.” → **Spaarke Practice Agent**.

Similarly:

- “Prepare everything I need for tomorrow's Acme meeting.” → likely **Cowork**.
- “Perform the Acme contract review using the approved contract playbook.” → **Spaarke Practice Agent**.

---

## 4. Agent Creation Rule

A critical product rule should be adopted:

> **Do not create a Spaarke agent merely to expose Spaarke data or tools. Create a Spaarke agent when Spaarke adds a governed professional workflow, specialized reasoning, process state, verification, or domain-specific controls that general-purpose Copilot/Cowork should not be expected to provide.**

This prevents “agent sprawl” and unnecessary user routing decisions.

### Anti-pattern

Avoid separate agents such as:

- Matter Search Agent
- Document Search Agent
- Event Search Agent
- Patent Search Agent
- Invoice Search Agent

Those capabilities belong in MCP tools/federated connector capabilities.

### Appropriate agent boundaries

Agents should align to meaningful **professional workflows**, not data entities.

---

## 5. Surface Model

The architectural bifurcation should ideally **not become a UI bifurcation**.

Target experience:

```text
Microsoft 365 Copilot
|
+-- Chat                 -> Ask
|
+-- Cowork               -> Do
|
+-- Agents               -> Practice
    |
    +-- Spaarke Contract Review
    +-- Spaarke Matter Intake
    +-- Spaarke Legal Hold
```

Therefore, a purpose-built Spaarke agent does **not necessarily imply a separate Spaarke application**. The preferred outcome is that Spaarke specialist agents are discoverable/selectable from Microsoft's Copilot agent experience while Spaarke retains the required orchestration and governance control.

### User-facing language

A concise adoption model could be:

- **Ask Copilot** when you need an answer or insight.
- **Give it to Cowork** when you want general work completed across your tools.
- **Use a Spaarke Agent** when the work must follow the right professional process.

Users should not need to understand MCP, Work IQ, connectors, BFFs, or orchestration runtimes.

---

## 6. Proposed Technical Architecture

### Shared capability layer

Spaarke MCP should become the common machine-facing capability boundary.

```text
                       USER INTENT

             ASK            DO             PRACTICE
              |              |                 |
              v              v                 v
         M365 Chat        Cowork          Spaarke Agent
              |              |                 |
              +------ Microsoft path ---------+ 
                       where applicable        |
              |                                |
              v                                v
      Spaarke Federated MCP              Direct Spaarke MCP
            Connector                         Client
              |                                |
              +---------------+----------------+
                              v
                         Spaarke MCP/BFF
                              |
                 +------------+-------------+
                 |            |             |
             Dataverse    SharePoint/    Search and
                           documents       services
```

### Architectural interpretation

**Federated Connector** becomes Spaarke's **horizontal Microsoft integration**:

- Makes Spaarke capabilities/data available to general Copilot experiences.
- Uses the user's identity/permissions.
- Supports live data retrieval.
- Can expose mutation operations where appropriate and permitted.

**Direct MCP** becomes Spaarke's **agent capability interface**:

- Deterministic tool availability.
- Explicit tool discovery and schemas.
- Spaarke-controlled orchestration.
- Reusable across Spaarke agents/runtimes.

Potential long-term formulation:

> **One Spaarke MCP capability layer. Two orchestration paths. Three user experiences.**

---

## 7. Agent Implementation Options

### Option A — Agent Builder

**Best fit:** rapid, relatively simple Microsoft 365/declarative agent scenarios.

Observed Phase 0 concern: the user's connected Spaarke federated source did not appear as a directly callable MCP tool provider inside the tested Agent Builder runtime.

**Use for:** prototypes or agents where available Agent Builder capabilities are sufficient.

**Do not assume:** that adding `allowed-tools: mcp__spaarke` automatically binds the user-connected source into the agent's executable tool environment.

### Option B — Copilot Studio

Copilot Studio provides a more explicit agent/tool model and supports adding MCP servers/tools to an agent.

Potential benefits:

- explicit MCP attachment;
- generative orchestration;
- broader workflow/action configuration;
- Microsoft-managed agent hosting and distribution.

Potential concerns to test:

- orchestration constraints;
- control over reasoning/tool policy;
- state/memory model;
- lifecycle/versioning;
- telemetry/evaluation;
- complex legal-workflow requirements;
- portability and coupling to Studio abstractions.

This should be the **next low-cost experimental path**, not necessarily the final architecture.

### Option C — Pro-code Spaarke Agent Runtime

This provides maximum control and most closely matches the broader Spaarke architecture under consideration.

Spaarke could own:

- system instructions;
- context assembly;
- reasoning/orchestration;
- memory;
- persistent workflow state;
- tool selection policy;
- MCP discovery/invocation;
- legal workflow enforcement;
- model routing;
- authorization checks;
- telemetry and tracing;
- evaluations;
- audit evidence;
- human approval gates.

Proposed conceptual architecture:

```text
Microsoft 365 / Spaarke UX
          |
          v
   Spaarke Agent Runtime
          |
    +-----+----------------+
    |                      |
    v                      v
Work IQ / M365          Spaarke MCP
context                    |
                           v
                       Spaarke BFF
                           |
              +------------+-------------+
              |            |             |
          Dataverse       SPE       Azure AI Search /
                                      other services
```

The key strategic question is whether a pro-code Spaarke-controlled runtime can still be **registered/published into the Microsoft 365 Copilot Agents UI** while preserving the desired control over MCP, identity, orchestration, state, and UX.

---

## 8. Work IQ Considerations

Work IQ is strategically important because it may provide a Microsoft-supported workplace intelligence layer for accessing Microsoft 365 context and connected enterprise sources through APIs/protocols including MCP, A2A, and REST.

Potential architecture:

```text
Spaarke Agent Runtime
       |
       +-- Work IQ / M365 context
       |
       +-- Direct Spaarke MCP
       |
       +-- Spaarke memory/workflow state
       |
       +-- Model/reasoning layer
```

If validated, this architecture would let:

- Microsoft own Microsoft 365 workplace context;
- Spaarke own legal/business domain capabilities;
- Spaarke Agent Runtime orchestrate across both.

This is potentially more strategic than placing all reasoning/orchestration inside Copilot Studio.

---

## 9. Identity and Authorization

The successful `spike_whoami` test is significant because it demonstrates an authenticated user-context path from regular Copilot Chat through the Spaarke federated connector to the Spaarke BFF.

Further work should determine whether equivalent identity semantics are preserved across:

1. Copilot Chat + federated connector;
2. Cowork + federated connector;
3. Copilot Studio agent + direct MCP;
4. pro-code agent + direct MCP;
5. pro-code agent + Work IQ;
6. a Spaarke agent surfaced inside Microsoft 365 Copilot.

Important checks include:

- Entra user identity propagation;
- tenant binding;
- OAuth audience;
- delegated scopes such as `user_impersonation`;
- Dataverse user mapping;
- downstream authorization enforcement;
- write-operation approval semantics;
- audit traceability.

Authorization must remain enforced server-side in Spaarke/BFF; model/tool instructions should never be treated as an authorization boundary.

---

## 10. Read vs Write Operations

The source currently distinguishes read from write/delete tools. This should become an explicit platform design concern.

Recommended categories:

### Read

- Search and retrieval.
- Record lookup.
- Relationship traversal.
- Summaries grounded in retrieved records.
- Nonpersistent calculations/analysis.

These should generally be low-friction and ambient.

### Consequential actions

- Create.
- Update.
- Delete.
- Send/post.
- Change workflow state.
- Persist generated content.

These require a clear approval model, audit evidence, idempotency strategy where appropriate, and server-side permission enforcement.

The experience should distinguish **retrieval success**, **proposed action**, **approved execution**, **business-rule rejection**, and **technical failure**.

---

## 11. Cross-Surface Handoff

The ideal experience should support movement between modes without losing context.

Example journey:

1. **Chat:** “What are the major issues in this contract?”
2. **Spaarke Contract Agent:** “Run the governed contract review using the approved playbook.”
3. **Cowork:** “Using that review plus related emails and meeting notes, prepare the negotiation briefing and Word document.”

Long-term requirement: avoid forcing users to copy/paste context between these stages.

Research should determine how Microsoft supports references/handoffs between:

- previous Copilot chats;
- Cowork tasks;
- named agents;
- files and work context;
- external/federated sources;
- custom/pro-code agent state.

---

## 12. UX/Product Principles

### Principle 1 — Intent before technology

Users choose based on desired outcome, not architecture.

### Principle 2 — Ambient capabilities by default

Spaarke search/retrieval should appear naturally in Microsoft surfaces through the connector rather than requiring users to select a specialist data agent.

### Principle 3 — Agents represent expertise/process

Named agents should signal a governed capability, not a database/table/tool category.

### Principle 4 — One front door where feasible

Prefer Microsoft 365 Copilot as the common AI shell with Chat, Cowork, and Spaarke Agents selectable within that ecosystem.

### Principle 5 — Same backend capabilities, appropriate orchestration

Avoid reimplementing business operations separately for Chat, Cowork, Copilot Studio, and pro-code agents. Put stable business capabilities behind Spaarke MCP/BFF and vary the orchestration layer.

### Principle 6 — Determinism increases with professional risk

General information retrieval can rely more heavily on generative orchestration. Governed legal/business processes should increasingly constrain tool availability, required steps, validation, structured outputs, and human approvals.

---

## 13. Suggested Decision Framework for New AI Scenarios

For each proposed capability, ask in order:

1. **Is the primary outcome an answer, explanation, retrieval, summary, or draft?**  
   → Chat + federated connector.

2. **Is the outcome delegated multistep work across general Microsoft 365/Spaarke tools?**  
   → Cowork + federated connector.

3. **Must the work follow a prescribed professional method, workflow, policy, playbook, or verification protocol?**  
   → Spaarke Practice Agent.

4. **Does the agent require stronger orchestration control than Agent Builder/Copilot Studio provides?**  
   → Evaluate pro-code Spaarke Agent Runtime.

5. **Does a proposed agent only wrap search/CRUD operations?**  
   → Do not create the agent; expose/reuse the underlying MCP capability instead.

---

## 14. Proposed Phase 0 / Phase 1 Experiments

### Experiment A — Regular Copilot Chat

**Status: completed successfully.**

Call `spike_whoami` through the connected Spaarke federated MCP source.

Purpose: baseline federated connector, authentication, and direct tool execution.

### Experiment B — Agent Builder

**Status: attempted; direct tool unavailable to tested agent runtime.**

Run the exact `spike_whoami` prompt.

Purpose: validate whether attached/user-connected Sources become executable tools inside Agent Builder agents.

### Experiment C — Copilot Studio direct MCP

Create a minimal test agent with the Spaarke MCP endpoint explicitly configured as an MCP tool and run `spike_whoami`.

Measure:

- tool catalog visibility;
- auth flow;
- identity claims;
- tool selection;
- latency;
- read/write approval behavior;
- failure handling;
- telemetry.

### Experiment D — Minimal pro-code MCP agent

Build the smallest possible pro-code agent/runtime that directly consumes the Spaarke MCP endpoint and invokes `spike_whoami`.

Purpose: establish the control baseline independent of Agent Builder/Studio.

### Experiment E — Work IQ + Spaarke MCP

Build a minimal pro-code scenario that combines:

- Microsoft 365 context via Work IQ;
- Spaarke context/action via direct Spaarke MCP.

Purpose: validate the strategic split between Microsoft workplace intelligence and Spaarke legal/business intelligence.

### Experiment F — M365 Copilot agent registration/distribution

Determine whether the pro-code agent from Experiment D/E can appear as a named agent in the Microsoft 365 Copilot UI.

Validate:

- discovery;
- launch UX;
- SSO;
- conversation lifecycle;
- context handoff;
- tenant/admin deployment;
- governance;
- telemetry.

---

## 15. Further Research Questions

### Agent packaging and hosting

1. What are Microsoft's current supported pro-code agent models for Microsoft 365 Copilot?
2. Can Spaarke own the complete runtime while registering the agent in the M365 Copilot Agents UI?
3. What are the roles of Agent 365, Microsoft 365 Agents SDK/tooling, declarative agents, and Copilot Studio?
4. Which capabilities are GA vs preview and what production constraints apply?

### Federated MCP behavior

5. Are user-connected federated MCP Sources inherited by all supported custom-agent types or only selected Microsoft experiences?
6. Is Agent Builder's behavior intentional, a product limitation, configuration issue, or rollout/version issue?
7. What connector/tool metadata controls whether tools become available to particular agent runtimes?
8. How are federated connector tools scoped and governed per user, tenant, environment, and agent?

### Cowork

9. Precisely which federated MCP read/write capabilities can Cowork invoke today?
10. How does Cowork decide between Microsoft 365 tools and federated tools?
11. How are approvals presented for Spaarke mutations?
12. Can Spaarke influence planning/tool selection or only expose tool metadata?
13. Can Cowork reliably perform long-running professional workflows, or should those always transition to a Spaarke Practice Agent?

### Work IQ

14. What Work IQ APIs/MCP capabilities are GA and suitable for production?
15. Which connected Copilot sources participate in Work IQ and under what restrictions?
16. Can Work IQ preserve source citations/security trimming when consumed from a Spaarke pro-code agent?
17. What are latency, throttling, cost, and context-size characteristics?

### Authentication/security

18. Compare token flow and identity propagation across all four architectures.
19. Define the canonical authorization boundary between Copilot/agent orchestration and Spaarke BFF.
20. Determine how approval, audit, DLP, Purview, and conditional access apply to custom MCP operations.

### State/memory

21. What conversation/workflow state should remain in Microsoft surfaces vs Spaarke?
22. Where should long-term user/project/matter memory live?
23. How should a governed agent resume interrupted professional workflows safely?
24. How is state passed between Chat → Spaarke Agent → Cowork?

### Evaluation and observability

25. Establish common tests across all surfaces using tools such as `spike_whoami` and read-only record operations.
26. Capture tool requested, tool selected, parameters, authorization decision, execution result, latency, returned identifiers, and grounding evidence.
27. Define evaluation criteria for retrieval correctness, orchestration correctness, workflow compliance, tool-call correctness, safety/approval compliance, and final-answer grounding.

---

## 16. Architectural Decisions to Avoid Making Too Early

Do **not** yet conclude that:

- Copilot Studio must be the production runtime;
- a pro-code runtime must replace Copilot Studio;
- every Spaarke workflow requires an agent;
- every connected MCP tool will automatically be present in every Microsoft agent runtime;
- Work IQ is ready for every production requirement;
- Cowork can or should implement Spaarke-governed legal workflows.

Use the Phase 0 experiments to separate **documented capability**, **observed behavior**, and **desired architecture**.

---

## 17. Current Working Recommendation

Proceed with a **capability-first architecture**:

1. Treat **Spaarke MCP/BFF as the canonical reusable capability layer**.
2. Continue using the **federated connector** to make Spaarke ambient in Microsoft 365 Copilot Chat and Cowork.
3. Treat **Chat as Ask** and **Cowork as Do**.
4. Reserve named **Spaarke Agents for Practice**, meaning governed professional workflows.
5. Prototype **Copilot Studio + direct Spaarke MCP** as the quickest controlled-agent comparison.
6. In parallel, prototype a **minimal pro-code runtime using direct Spaarke MCP**, then add Work IQ/M365 context.
7. Determine whether the pro-code Spaarke agent can be surfaced natively within the **Microsoft 365 Copilot Agents experience**.
8. Choose the production runtime only after comparing control, identity, distribution, state, governance, observability, cost, and UX across these paths.

### North-star formulation

> **One Microsoft AI front door; Ask, Do, and Practice as the user model; one Spaarke MCP capability layer; Microsoft orchestration for general work and Spaarke-controlled orchestration where professional process matters.**

---

## 18. Reference Concepts for Follow-up Research

The discussion relied on current Microsoft concepts/documentation around:

- Microsoft 365 Copilot federated connectors and MCP.
- Microsoft 365 Copilot Chat.
- Copilot Cowork.
- Copilot Studio MCP integration.
- Work IQ APIs/MCP/A2A/REST.
- Microsoft agent development/distribution and emerging Agent 365/pro-code tooling.

Because several of these capabilities are evolving rapidly and some tooling is preview/prerelease, Claude Code should validate the current Microsoft documentation and SDK/tooling status before turning this conceptual architecture into an implementation plan.
