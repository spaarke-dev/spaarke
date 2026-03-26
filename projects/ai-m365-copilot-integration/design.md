# M365 Copilot in Power Apps — Spaarke AI Integration

> **Project**: ai-m365-copilot-integration
> **Status**: Design
> **Priority**: High (M365 Copilot in MDA goes GA April 13, 2026)
> **Last Updated**: March 25, 2026
>
> **Scope (March 25, 2026)**: This project focuses **exclusively on M365 Copilot within Power Apps model-driven apps** — the Copilot side pane that Spaarke customers will see on their Dataverse forms when Copilot goes GA. Future phases (Teams bot, Outlook plugin, Copilot Chat standalone, Power Pages) are deferred to a subsequent project.

---

## Executive Summary

Optimize the M365 Copilot experience within Spaarke's Power Apps model-driven app by integrating our AI primitives — playbook engine, Azure OpenAI, custom RAG pipeline, and SPE document access — into the Copilot side pane that appears on Dataverse forms. When Copilot goes GA on April 13, 2026, Spaarke customers will have an AI assistant that understands their matters, documents, and analysis workflows — not just generic Dataverse queries.

**M365 Copilot replaces SprkChat as the general chat interaction UX.** Rather than building and maintaining our own general-purpose chat component, we leverage Copilot — which Microsoft provides, maintains, and customers already expect — as the conversational AI surface across all Dataverse forms. SprkChat is repositioned as a **special-purpose AI companion exclusively for the Analysis Workspace**, where deep editor integration, streaming write-back, and inline toolbar capabilities exceed what Copilot can deliver.

This means:
- **Copilot = general Spaarke AI** — record queries, document search, playbook invocation, dashboard queries, email drafting — across all MDA pages
- **SprkChat = analysis-specific AI** — interactive analysis output editing, inline AI toolbar, compound write-back actions — only in Analysis Workspace
- **Copilot hands off to SprkChat** when deep analysis work is needed (deep-link to Analysis Workspace)

> **Future scope**: Teams bot, Outlook plugin, Copilot Chat standalone, Power Pages integration, and MCP server are deferred to `ai-m365-copilot-integration-r2`. The architecture decisions in this project (agent gateway endpoint, Adaptive Card templates, SSO token flow) are designed to be reusable across those future channels.

---

## Strategic Context

### Why Now

1. **M365 Copilot in model-driven apps goes GA April 13, 2026** — Spaarke customers will have Copilot on the same forms where our app lives. If we don't integrate, Copilot becomes a competitor on our own turf. If we do, it becomes a gateway.

2. **Custom Engine Agents are GA** — We can surface our entire AI stack (playbooks, custom RAG, Azure OpenAI) through M365 without giving up any control.

3. **MCP is GA in Copilot Studio** — We can expose BFF tools via Model Context Protocol for universal interoperability.

4. **Agent 365 launches May 1, 2026** — Enterprise control plane for governing all agents. ISVs who register look like managed enterprise software; those who don't look like shadow AI.

5. **Enterprise BYOK trend** — Corporate enterprises want M365 Copilot AND control over their AI infrastructure. Our Model 2 (customer-hosted) + Custom Engine Agent is a unique ISV position.

### Evolution of Our Copilot Strategy

| Phase | Position | Rationale |
|-------|----------|-----------|
| **Original (2025)** | Ignore M365 Copilot | Not robust enough for Power Apps/Dataverse; limited extensibility |
| **Mid (March 2026)** | Two-plane strategy | Copilot = general Q&A; SprkChat = contextual analysis. Coexist but separate |
| **Current (March 2026)** | Extend and integrate | Copilot = general Spaarke AI surface (backed by our engine); SprkChat = deep analysis companion. Connected via handoff |

### Relationship to SprkChat

```
┌─────────────────────────────────────────────────────────────┐
│  M365 COPILOT IN MDA (This Project — R1)                     │
│  Copilot side pane on Dataverse forms                        │
│                                                               │
│  "What matters am I assigned to?"                            │
│  "Summarize the Acme NDA"                                    │
│  "Run a risk scan on this contract"                          │
│                                                               │
│  → Adaptive Card results in Copilot side pane                │
│  → "Open in Analysis Workspace" handoff ─────────┐          │
│                                                    │          │
├────────────────────────────────────────────────────┼──────────┤
│  SPRKCHAT IN ANALYSIS WORKSPACE (Separate Project) │          │
│  Streaming · Editor integration · Inline toolbar   ◄──────────┘
│  Write-back · Compound actions · Plan preview      │
│  Deep, interactive analysis work                   │
└────────────────────────────────────────────────────┘

BOTH share: BFF API → Playbook Engine → Azure OpenAI → Custom RAG

┌─────────────────────────────────────────────────────────────┐
│  FUTURE (R2 — Separate Project)                              │
│  Teams bot · Outlook plugin · Copilot Chat standalone        │
│  Power Pages · MCP server · Agent 365 governance             │
│  → Reuses agent gateway + Adaptive Cards from R1             │
└─────────────────────────────────────────────────────────────┘
```

---

## Problem Statement

Spaarke's AI capabilities are currently accessible only through SprkChat in the Analysis Workspace side pane. Users must navigate to a specific Dataverse form and open the analysis workspace to interact with AI. There is no AI interaction surface in:

- **Microsoft Teams** — where users spend most of their collaboration time
- **Outlook** — where legal communications happen
- **Copilot Chat** — the emerging universal AI surface in M365
- **Corporate Workspace** — where users land first (SprkChat is being removed from here in favor of M365 Copilot)
- **Power Pages external portal** — where external users access Spaarke

Meanwhile, M365 Copilot is going GA in model-driven apps (April 13, 2026). Without integration, Copilot will appear alongside Spaarke with zero knowledge of our SPE documents, playbooks, or domain-specific analysis capabilities — creating a confusing dual-AI experience.

---

## Goals

1. **Spaarke AI in every M365 surface** — users interact with Spaarke intelligence from Teams, Outlook, Copilot Chat without navigating to a specific Dataverse form
2. **Our engine, their channels** — Custom Engine Agent uses M365 Agents SDK as delivery layer only; our BFF, playbooks, Azure OpenAI, and RAG pipeline do all the work
3. **SPE document access through our authorization** — M365 Copilot does NOT directly ground on SPE containers (discoverabilityDisabled = true); all document access goes through BFF with our per-matter/per-project security model
4. **Playbook invocation from M365** — users trigger playbook analysis from any M365 surface and receive structured Adaptive Card results
5. **Seamless handoff** — one-click deep-link from M365 Copilot into Analysis Workspace + SprkChat for deep analysis work
6. **Enterprise governance** — agents register in Agent 365; BYOK customers run our engine on their Azure infrastructure

---

## Architecture

### Integration Tiers

#### Tier 1: Declarative Agent with API Plugin (RECOMMENDED — Primary Integration Path)

A Declarative Agent running on Microsoft's orchestrator inside M365 Copilot, extended with an **API Plugin** that calls our BFF API directly via OpenAPI spec. No Copilot Studio involved. No Custom Engine Agent hosting required for this tier.

**How it works**: M365 Copilot's AI reads function descriptions from the API plugin and decides when to call them — exactly like Azure OpenAI function calling. There are no scripted question sequences; behavior is guided through **instructions** (system prompt in the manifest), not scripted flows.

**Three files define the integration**:

| File | Purpose |
|---|---|
| `declarativeAgent.json` | Agent manifest with instructions (system prompt), capabilities, conversation starters |
| `spaarke-api-plugin.json` | Function definitions — names, descriptions, parameters, return types |
| `spaarke-bff-openapi.yaml` | OpenAPI spec pointing to our BFF API — the actual HTTP contract |

**Architecture**:
```
M365 Copilot (Microsoft's orchestrator)
  │
  ├── Reads declarativeAgent.json → loads instructions + capabilities
  ├── Reads spaarke-api-plugin.json → understands available functions
  ├── AI decides WHEN to call functions based on user message + function descriptions
  │
  ▼ Direct HTTPS call (no intermediary)
Spaarke BFF API (our endpoints, defined in spaarke-bff-openapi.yaml)
  ├── GET /api/agent/playbooks?documentType={type}
  ├── POST /api/agent/run-playbook
  ├── GET /api/agent/documents/search?query={query}
  ├── POST /api/agent/message
  └── ... (all existing BFF endpoints exposed via OpenAPI)
```

**Deployed via**: M365 Agents Toolkit (VS Code extension) — packages the three files + Teams app manifest → sideloads or publishes to org app catalog.

| Capability | Implementation |
|---|---|
| Record Q&A | "What's the status of Matter 2024-001?" → Copilot queries Dataverse natively |
| Navigation | "Open the Acme project" → Deep-link to record |
| Entity summarization | "Summarize my overdue tasks" → Copilot reads Dataverse entities |
| Document search | "Find the NDA for Acme" → API Plugin calls BFF search endpoint |
| Playbook invocation | "Run a risk scan on this contract" → API Plugin calls BFF playbook endpoint |
| Spaarke-specific instructions | Custom system prompt scoping the agent to legal operations vocabulary |

**Conversation flow model**: The AI model reads function descriptions and decides what to call and when. You guide behavior through instructions, not scripted flows. Actions CAN return Adaptive Cards (so playbook results, document lists get buttons). But the AI's own questions between actions are text-based (no button choices for user input). For complex record creation (e.g., matters): the AI collects fields conversationally, then deep-links to the wizard with pre-filled params.

**Limitations**: Uses Microsoft's model (not our Azure OpenAI) for orchestration. Cannot do SSE streaming. No inline editor integration. But CAN access SPE documents via API plugin → BFF.

**Value**: Ships with minimal infrastructure (just BFF API endpoints + three manifest files). Gives Copilot full Spaarke awareness including document search and playbook invocation.

#### Tier 2: Custom Engine Agent (Spaarke AI Engine)

The flagship integration. M365 Agents SDK as channel; our BFF API as the brain.

```
┌──────────────────────────────────────────────────────────┐
│  M365 Channel Layer (Agents SDK)                          │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌─────────┐ │
│  │ Teams    │  │ Outlook  │  │ Copilot  │  │ Web /   │ │
│  │ Bot      │  │ Plugin   │  │ Chat     │  │ Portal  │ │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬────┘ │
│       └──────────────┴──────┬─────┴──────────────┘      │
│                             │                            │
│              M365 Agents SDK Activity Handler            │
│              (SpaarkeAgentHandler)                        │
└─────────────────────────────┬────────────────────────────┘
                              │ HTTPS
                              ▼
┌─────────────────────────────────────────────────────────┐
│  Spaarke BFF API (The Brain)                             │
│                                                          │
│  ┌──────────────────┐  ┌──────────────────────────────┐ │
│  │ Agent Gateway     │  │ Context Resolution           │ │
│  │ Endpoint          │  │ (entity → playbook mapping)  │ │
│  │ POST /api/agent/  │  │                              │ │
│  │   message         │  │ ChatContextMappingService    │ │
│  └────────┬─────────┘  └──────────────────────────────┘ │
│           │                                              │
│  ┌────────▼─────────┐  ┌──────────────────────────────┐ │
│  │ Playbook Engine   │  │ Tool Handlers                │ │
│  │                   │  │ • DocumentSearch             │ │
│  │ JPS execution     │  │ • SummaryGenerator           │ │
│  │ Multi-step        │  │ • DataverseQuery             │ │
│  │ orchestration     │  │ • PlaybookDispatcher         │ │
│  └────────┬─────────┘  │ • EmailDraft                 │ │
│           │             └──────────────────────────────┘ │
│  ┌────────▼─────────────────────────────────────────┐   │
│  │ Azure Services (Ours)                             │   │
│  │ ┌────────────┐ ┌────────────┐ ┌───────────────┐ │   │
│  │ │ Azure      │ │ AI Search  │ │ Document      │ │   │
│  │ │ OpenAI     │ │ (tenantId  │ │ Intelligence  │ │   │
│  │ │ (our model)│ │ isolation) │ │               │ │   │
│  │ └────────────┘ └────────────┘ └───────────────┘ │   │
│  │ ┌────────────┐ ┌────────────┐                    │   │
│  │ │ Redis      │ │ SPE via    │                    │   │
│  │ │ Cache      │ │ Graph API  │                    │   │
│  │ └────────────┘ └────────────┘                    │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

**Capabilities**:

| Capability | User Experience | Backend |
|---|---|---|
| **Document search** | "Find the NDA for Acme Corp" → Adaptive Card with document list + open links | BFF → AI Search (semantic) → SPE via Graph |
| **Document summary** | "Summarize this contract" → Structured summary card | BFF → SPE document retrieval → Azure OpenAI |
| **Playbook invocation** | "Run a risk scan on the uploaded lease" → Risk findings card | BFF → PlaybookExecutionEngine → multi-step analysis |
| **Matter queries** | "What's active for the Smith litigation?" → Entity card list | BFF → Dataverse query |
| **Email drafting** | "Draft an update to outside counsel on Matter 2024-001" → Email preview card | BFF → Azure OpenAI with matter context |
| **Analysis handoff** | "Analyze this in detail" → Deep-link button | Xrm.Navigation.navigateTo URL with context params |

**Response Format**: Adaptive Cards for structured content; markdown for conversational responses.

```
User in Teams: "What are the risk flags in the Acme NDA?"

┌──────────────────────────────────────────────┐
│ ⚠️ NDA Risk Analysis — Acme Corp              │
│                                               │
│ Risk Flags (3):                               │
│ • Indemnification cap below market ($2M)     │
│ • Non-compete scope: 36 months (broad)       │
│ • Governing law: Delaware (non-standard)     │
│                                               │
│ Standard Clauses (5 confirmed) ✅             │
│                                               │
│ Source: NDA Review Playbook                   │
│ Confidence: High (94%)                        │
│                                               │
│ [📊 Full Analysis]  [📄 View Document]        │
│ [✏️ Open in Workspace]                        │
└──────────────────────────────────────────────┘
```

#### Tier 3: MCP Server (Universal Tool Exposure)

Expose BFF API tools as a Model Context Protocol server for maximum interoperability.

```
Spaarke MCP Server
  ├── Tools
  │   ├── search_documents(query, scope, entity_type)
  │   ├── summarize_document(document_id)
  │   ├── run_playbook(playbook_name, document_id, parameters)
  │   ├── query_matters(filter, fields)
  │   ├── query_events(filter, assignee)
  │   └── draft_email(recipient_role, matter_id, purpose)
  │
  ├── Resources
  │   ├── matter://{id} → matter record + related entities
  │   ├── document://{id} → document metadata + preview URL
  │   └── analysis://{id} → analysis output + findings
  │
  └── Prompts
      ├── risk-scan → pre-built risk analysis prompt
      ├── contract-review → contract review prompt
      └── matter-summary → matter summarization prompt
```

**Consumers**:
- Copilot Studio agents (MCP GA)
- Declarative agents in M365 Copilot
- Claude Desktop / Claude Code (developer workflows)
- VS Code Copilot
- Customer-built agents in Copilot Studio
- Any MCP-compatible AI tool

#### Tier 4: Agent 365 Governance

Register Spaarke agents in Microsoft's enterprise control plane.

| Capability | What It Enables |
|---|---|
| **Agent inventory** | IT admins see Spaarke agents alongside all other enterprise agents |
| **Policy enforcement** | Data access policies apply uniformly |
| **Audit trail** | All agent interactions logged and auditable |
| **Deployment control** | IT controls who can access Spaarke agents, in which apps |
| **Usage analytics** | Track adoption, query volume, cost per interaction |

---

### SPE Document Access Strategy

**Critical design decision**: M365 Copilot does NOT directly access our SPE containers.

| Approach | Risk | Our Choice |
|---|---|---|
| Set `discoverabilityDisabled = false` | Copilot sees ALL documents; bypasses our per-matter/per-project authorization | **No** |
| Custom Engine Agent → BFF → SPE via Graph | BFF enforces authorization; Copilot only sees what user is permitted | **Yes** |
| SPE Agent SDK (Microsoft's RAG) | Uses Microsoft's RAG, not our domain-specific chunking/indexing | **Evaluate** for simple use cases |

**Authorization flow**:
```
User in Teams: "Find contracts for Acme"
  │
  ▼
Custom Engine Agent (receives user identity via M365 SSO)
  │
  ▼
BFF API: POST /api/agent/message
  ├── Validates user identity (Entra ID token)
  ├── Resolves user's authorized matters/projects
  ├── Queries AI Search with tenantId + authorization filter
  ├── Retrieves document metadata from SPE via Graph
  └── Returns only authorized documents
  │
  ▼
Adaptive Card with filtered results
```

Our authorization model is the product. It MUST NOT be bypassed.

---

### Power Pages Integration

M365 Copilot Chat is **limited to model-driven apps** — it does NOT extend to Power Pages custom SPAs.

**Options for external workspace**:

| Option | Approach | Effort |
|---|---|---|
| **Copilot Studio Agent embed** | Use Power Pages Agent API to embed a Copilot Studio agent connected to our BFF via MCP | Medium |
| **Custom Engine Agent via web channel** | M365 Agents SDK supports web/custom app channel — embed in external workspace | Medium |
| **Direct SprkChat embed** | Continue current approach — SprkChat React component in our SPA | Already done |

**Recommendation**: Phase 2 — evaluate once Custom Engine Agent is shipping, since the same BFF tools serve all channels.

---

## Deployment Models

### Model 1: Spaarke-Hosted

```
Customer's M365 Tenant
  ├── M365 Copilot (customer's license)
  ├── Spaarke Declarative Agent (Tier 1)
  ├── Spaarke Custom Engine Agent (Tier 2)
  │     │
  │     ▼ calls via HTTPS
  │
  Spaarke Azure Subscription
  ├── BFF API (spe-api-dev-67e2xz)
  ├── Azure OpenAI (spaarke-openai-dev)
  ├── AI Search (spaarke-search-dev, multi-tenant)
  └── Playbook Engine
```

### Model 2: Customer-Hosted (BYOK)

```
Customer's M365 Tenant
  ├── M365 Copilot (customer's license)
  ├── Spaarke Declarative Agent (Tier 1)
  ├── Spaarke Custom Engine Agent (Tier 2)
  │     │
  │     ▼ calls via HTTPS
  │
  Customer's Azure Subscription  ← CUSTOMER controls
  ├── BFF API (deployed by Spaarke IaC)
  ├── Customer's Azure OpenAI  ← Their models, their data boundary
  ├── Customer's AI Search     ← Physical isolation
  ├── Customer's AI Foundry    ← They manage via portal
  └── Playbook Engine
        │
        ▼ registered in
  Agent 365  ← IT governs in single console
```

**Enterprise pitch**: Your AI. Your infrastructure. Your governance. Spaarke provides the intelligence layer; you control everything else.

---

## Functional Use Cases

### UC-M1: Matter Dashboard Queries (Replaces SprkChat UC1)

**Context**: User is on the Corporate Workspace or any model-driven app page. M365 Copilot is available in the side pane (GA April 13).

```
User: "What are my overdue tasks?"

Copilot (via Custom Engine Agent):
┌──────────────────────────────────────────────┐
│ 📋 Overdue Tasks (3)                          │
│                                               │
│ ⚠️ Review NDA — Acme Corp                     │
│    Due: Mar 20 | Matter: Acme Litigation      │
│    [Open Task]                                │
│                                               │
│ ⚠️ File Response Brief                        │
│    Due: Mar 22 | Matter: Smith IP             │
│    [Open Task]                                │
│                                               │
│ ⚠️ Complete Compliance Checklist              │
│    Due: Mar 24 | Project: Q1 Audit            │
│    [Open Task]                                │
└──────────────────────────────────────────────┘
```

**Backend**: BFF → Dataverse query (`sprk_event` where assignee=me, duedate<today) → Adaptive Card

### UC-M2: Document Search + Playbook Selection

**Context**: User in Teams or Copilot Chat wants to analyze a document. They describe what they need.

```
User: "I need to review the Smith lease agreement"

Agent: I found this document:
┌──────────────────────────────────────────────┐
│ 📄 Smith_Lease_2024.pdf                       │
│    Matter: Smith v. Acme | Type: Lease        │
│    Uploaded: Mar 12, 2026 | Pages: 42         │
│                                               │
│ What would you like to do?                    │
│ [📋 Lease Review]     [⚠️ Risk Scan]          │
│ [📝 Quick Summary]    [🔍 Full Analysis]      │
│ [✏️ Open in Workspace]                        │
└──────────────────────────────────────────────┘
```

**How playbook options are determined**:
1. Agent calls BFF search → finds document → gets document type from Document Profile output
2. Agent calls BFF context mapping with document type → receives available playbooks
3. Playbook capabilities rendered as `Action.Submit` buttons in Adaptive Card
4. User clicks a playbook → agent invokes `POST /api/agent/run-playbook` with document ID + playbook ID

**Known platform limitation**: Users **cannot attach/upload files** directly to Custom Engine Agents in Copilot Chat (attachments are silently dropped — [open issue](https://github.com/OfficeDev/microsoft-365-agents-toolkit/issues/15325)). Users must **name or describe** the document; the agent searches SPE to find it. File upload works in Teams bot channel but not in Copilot Chat.

### UC-M3: Playbook Invocation + Results

**Context**: User selected a playbook in UC-M2 or invokes directly.

```
User clicks [📋 Lease Review]

Agent: Running Lease Review analysis...
┌──────────────────────────────────────────────┐
│ ⏳ Analyzing Smith_Lease_2024.pdf...          │
│ Step 1/4: Extracting key terms ✅             │
│ Step 2/4: Identifying parties ✅              │
│ Step 3/4: Analyzing clauses ⏳                │
│ Step 4/4: Generating findings                 │
└──────────────────────────────────────────────┘

(After completion — progressive card update):
┌──────────────────────────────────────────────┐
│ 📋 Lease Review — Smith_Lease_2024.pdf        │
│                                               │
│ Key Terms:                                    │
│ • Term: 5 years (Mar 2024 — Mar 2029)        │
│ • Monthly Rent: $12,500 with 3% escalation   │
│ • Security Deposit: $25,000                   │
│                                               │
│ ⚠️ Risk Flags (2):                            │
│ • Early termination clause favors landlord    │
│ • Assignment clause unusually restrictive     │
│                                               │
│ ✅ Standard Clauses (8 confirmed)             │
│                                               │
│ [📊 Full Report]  [✏️ Open in Workspace]      │
│ [📤 Email Summary to Counsel]                 │
└──────────────────────────────────────────────┘
```

**Backend**: BFF → PlaybookExecutionEngine → multi-step analysis → Adaptive Card formatter

**"Open in Workspace" handoff**: Generates a deep-link URL to the Analysis Workspace with `analysisId`, `sourceFileId`, and playbook context params. SprkChat auto-launches with full context.

**Note on long-running playbooks**: Playbook execution can take 30 seconds to 5 minutes. Teams supports typing indicators and progressive card updates (the agent sends an initial "processing" card, then updates it as steps complete). Copilot Chat has more limited update support — may need to send a final card only.

### UC-M4: Playbook Library Browse

**Context**: User wants to discover available playbooks without a specific document in mind.

```
User: "What analysis tools are available?"

Agent:
┌──────────────────────────────────────────────┐
│ 📚 Available Playbooks                        │
│                                               │
│ Document Analysis:                            │
│ • 📋 Quick Document Review (any document)     │
│ • 📋 Full Contract Analysis (contracts)       │
│ • 📋 NDA Review (NDAs)                        │
│ • 📋 Lease Review (leases)                    │
│ • 📋 Employment Contract Review               │
│                                               │
│ Specialized:                                  │
│ • 📊 Invoice Validation                       │
│ • 📊 Compliance Review                        │
│ • ⚠️ Risk-Focused Scan                        │
│                                               │
│ Tell me which one and I'll help you get       │
│ started, or describe what you need and I'll   │
│ recommend one.                                │
└──────────────────────────────────────────────┘
```

**Backend**: BFF → query `sprk_analysisplaybook` → filter by user permissions → format as Adaptive Card. This is a simpler version of the Playbook Library Code Page — a browse-only view for discovery.

### UC-M5: Email Draft from Matter Context

**Context**: User wants to send a communication about a matter.

```
User: "Draft an update to outside counsel on the Smith matter"

Agent:
┌──────────────────────────────────────────────┐
│ 📧 Draft Email                                │
│                                               │
│ To: j.smith@kirkland.com (Outside Counsel)    │
│ Subject: Status Update — Smith v. Acme        │
│ ───────────────────────────────────────────── │
│ Dear Jane,                                    │
│                                               │
│ I wanted to provide a brief update on the     │
│ Smith matter. [AI-generated summary based on  │
│ recent matter activity and documents]         │
│                                               │
│ Please let me know if you have any questions. │
│                                               │
│ Best regards,                                 │
│ [User name]                                   │
│                                               │
│ [✏️ Edit Draft]  [📤 Send]  [✕ Cancel]        │
└──────────────────────────────────────────────┘
```

**Backend**: BFF → resolve matter → query recent activity → Azure OpenAI generates draft → resolve outside counsel from matter party roles

### UC-M6: Corporate Workspace Dashboard (Replaces SprkChat UC1)

**Context**: User on Corporate Workspace. M365 Copilot available in side pane.

These are the queries previously planned for SprkChat that now move to M365 Copilot:
- "What are my due dates today?" → Dataverse query → entity card list
- "Show my assignments" → Dataverse query → card list
- "What's the matter activity this week?" → Dataverse query → summary card
- "Create a new task for the Acme matter" → Dataverse create → confirmation card

These work well as Declarative Agent capabilities since they primarily query Dataverse natively.

### UC-M7: Handoff to Analysis Workspace

**Context**: Any M365 surface. User needs deep analysis that requires SprkChat.

```
User: "I need to do a detailed clause-by-clause review of this contract"

Agent:
┌──────────────────────────────────────────────┐
│ For detailed clause-by-clause analysis, I     │
│ recommend using the Analysis Workspace where  │
│ you can:                                      │
│ • Work with the analysis output in the editor │
│ • Use inline AI tools on selected text        │
│ • Refine and iterate on findings              │
│ • Export to Word                              │
│                                               │
│ [✏️ Open Analysis Workspace]                  │
│                                               │
│ Or I can run a quick analysis here:           │
│ [📋 Quick Summary]  [⚠️ Risk Scan]            │
└──────────────────────────────────────────────┘
```

The agent recognizes when a request exceeds what Adaptive Cards can deliver and proactively offers the handoff.

### UC-M8: Document Upload + Analysis Pipeline

**Context**: User wants to upload a new document and analyze it.

#### Document Attachment Flow (M365 Copilot)

When a user attaches a file via the "+" button in M365 Copilot, the file goes to the user's **OneDrive for Business** under the `Microsoft Copilot Chat Files/` folder. The file has a Graph driveItem ID.

**Our BFF can retrieve it via**: `GET /me/drive/items/{driveItemId}/content` (using OBO token from the user's session).

**Two paths after attachment**:

| Path | When | What Happens |
|---|---|---|
| **Quick analysis** | User wants immediate results, file stays in OneDrive | BFF retrieves file from OneDrive via Graph → runs playbook → returns Adaptive Card results. No SPE upload, no `sprk_document` record. |
| **Full onboarding** | User wants document in the system | BFF retrieves file from OneDrive → uploads to SPE container → creates `sprk_document` record → triggers Document Profile → offers analysis options |

**Known limitation**: Declarative Agents receive a `ConversationFileReference` when the user attaches a file. **Technical spike required** to validate whether this reference includes a Graph-resolvable driveItem ID that our BFF can use to retrieve the file content. See Phase 1 spikes below.

**Known limitation (Custom Engine Agents)**: For Custom Engine Agents in Copilot Chat, attachments are **silently dropped** — this is a known bug. Users must describe documents by name; the agent searches SPE to find them.

#### Guided Upload Flow (No Attachment)

If the user describes a document without attaching it:

```
User: "I have a new contract to analyze for the Smith matter"

Agent:
┌──────────────────────────────────────────────┐
│ I'll help you get that set up.                │
│                                               │
│ To upload and analyze the document:           │
│                                               │
│ → Open Document Upload Wizard                 │
│   (Matter: Smith v. Acme pre-selected)        │
│                                               │
│ The wizard will:                              │
│ 1. Upload to the matter's document library    │
│ 2. Create the document record                 │
│ 3. Auto-classify the document type            │
│ 4. Offer analysis options                     │
│                                               │
│ Or if the document is already uploaded:       │
│ [🔍 Search Smith matter documents]            │
└──────────────────────────────────────────────┘
```

**Deep-link URL format**: The agent generates a `navigateTo` URL with pre-filled params:
```
sprk_documentuploadwizard?data=parentEntityType=sprk_matter
  &parentEntityId={matterId}&parentEntityName=Smith+v.+Acme
  &containerId={containerIdFromMatter}
```

**Teams bot channel (file attachment works)**: In Teams, users CAN attach files and the agent receives them. The agent can then upload to SPE via BFF, create the document record, trigger Document Profile, and present analysis options — all within the chat. This is a Teams-specific enhanced flow.

---

## Agent → Wizard → Workspace Pipeline

The complete interaction flow showing how M365 Copilot connects to our existing wizard Code Pages and Analysis Workspace:

```
M365 COPILOT (Custom Engine Agent)
│
├─ DISCOVER: User describes need
│  ├── "I need to analyze a document"
│  ├── "Review the Acme NDA"
│  └── "Upload a new contract for Smith matter"
│
├─ RESOLVE: Agent searches/identifies
│  ├── Existing doc → BFF search SPE → present results
│  └── New doc → deep-link to DocumentUploadWizard
│                (matterId, containerId pre-filled)
│
├─ SELECT: Agent presents playbook options
│  ├── Document type resolved from Document Profile classification
│  ├── Context mapping returns available playbooks
│  └── Adaptive Card with Action.Submit buttons per playbook
│
├─ EXECUTE: Two paths based on complexity
│  │
│  ├── QUICK (inline): Risk Scan, Quick Summary, Invoice Validation
│  │   ├── Agent calls BFF → PlaybookExecutionEngine
│  │   ├── Results returned as Adaptive Card
│  │   └── [Open in Workspace] link for deeper dive
│  │
│  └── DEEP (handoff): Full Contract Analysis, NDA Review, Lease Review
│      ├── Agent calls BFF: POST /api/ai/analysis/create
│      │   (creates sprk_analysisoutput + starts playbook execution)
│      ├── Returns "Analysis started" card with deep-link
│      └── → Analysis Workspace opens
│          └── SprkChat auto-launches with analysisId context
│              └── Full interactive experience (streaming, editor,
│                  inline toolbar, write-back, compound actions)
│
└─ FOLLOW-UP: User returns to M365 Copilot
   ├── "What were the risk flags in the Acme NDA analysis?"
   │   → Agent queries existing sprk_analysisoutput → returns summary
   └── "Email the analysis summary to outside counsel"
       → Agent reads analysis output → drafts email → preview card
```

### Existing Wizard Code Pages (Deep-Link Targets)

| Wizard | Web Resource | Pre-fillable Params | Use Case |
|---|---|---|---|
| **DocumentUploadWizard** | `sprk_documentuploadwizard` | `parentEntityType`, `parentEntityId`, `parentEntityName`, `containerId` | Upload new document to matter/project |
| **SummarizeFilesWizard** | `sprk_summarizefileswizard` | `bffBaseUrl` + document context | Summarize selected files |
| **CreateMatterWizard** | `sprk_creatematterwizard` | Various matter fields | Create matter with document upload |
| **CreateProjectWizard** | `sprk_createprojectwizard` | Various project fields | Create project |
| **CreateEventWizard** | `sprk_createeventwizard` | Matter context | Create event/task |
| **PlaybookLibrary** | `sprk_playbooklibrary` | Analysis context | Browse and launch playbooks |
| **AnalysisWorkspace** | via `sprk_AnalysisWorkspaceLauncher` | `analysisId`, `sourceFileId`, playbook context | Full analysis experience |

All wizards use the same launch pattern: `Xrm.Navigation.navigateTo({ pageType: "webresource", webresourceName: "...", data: "..." })`. The agent generates the equivalent URL for deep-linking from M365 surfaces.

---

## What Needs to Be Built

### New Components

| Component | Description | Effort |
|---|---|---|
| **SpaarkeAgentHandler** | M365 Agents SDK ActivityHandler — receives activities, routes to BFF | Medium |
| **Agent Gateway Endpoint** | `POST /api/agent/message` — translates agent activities to BFF chat/tool operations | Medium |
| **Playbook Menu Endpoint** | `GET /api/agent/playbooks?documentType={type}` — returns available playbooks for a document type | Small |
| **Adaptive Card Templates** | JSON templates: document list, matter card, risk findings, playbook menu, email preview, task confirmation, progress indicator | Medium |
| **Adaptive Card Formatter** | Service that takes playbook output JSON → Adaptive Card JSON | Medium |
| **Handoff URL Builder** | Generates Analysis Workspace deep-link with context params | Small |
| **SSO Token Flow** | M365 → OBO → BFF token exchange (pattern exists from Outlook add-in) | Medium |
| **Azure Bot Service Registration** | Bot registration, channel config, Teams app manifest | Small |
| **Declarative Agent Manifest** | `declarativeAgent.json` + `spaarke-api-plugin.json` + `spaarke-bff-openapi.yaml` for Tier 1 agent | Small-Medium |
| **MCP Server** | Expose BFF tools via Model Context Protocol (R2 — customer extensibility play) | Medium-Large |

### Existing Components Reused (No Changes)

| Component | Used For |
|---|---|
| BFF AI endpoints (chat, search, summarize) | All document + analysis operations |
| PlaybookExecutionEngine | Playbook invocation from M365 |
| ChatContextMappingService | Playbook resolution by document type |
| Azure OpenAI + AI Search | All AI operations |
| SPE document access via Graph | Document retrieval |
| OBO token pattern (from Outlook add-in) | Auth flow reference |
| PlaybookDispatcher (semantic matching) | Natural language → playbook resolution |

---

## Copilot Studio: Optional Enhancement, Not Required

### Copilot Studio Is NOT Required to Extend M365 Copilot

A common misconception is that Copilot Studio is required to extend M365 Copilot. It is not.

**Path A (RECOMMENDED): Direct API Plugin** — Declarative Agent manifest (JSON) + API Plugin (OpenAPI spec pointing to our BFF API) + deployed via M365 Agents Toolkit. No Copilot Studio involved. This is the primary integration path for R1.

**Path B (OPTIONAL): Copilot Studio** — Adds structured topic flows, Adaptive Card question nodes, enhanced file handling. Use only if Path A proves insufficient for structured interactions (e.g., guided multi-step wizards where button-driven question sequences are essential).

### Why Path A (Direct API Plugin) Is Preferred

| Factor | Direct API Plugin (Path A) | Copilot Studio (Path B) |
|---|---|---|
| **Complexity** | Three JSON/YAML files + BFF endpoints | Full Copilot Studio project + connector setup |
| **Our BFF API** | Called directly via OpenAPI spec | Called indirectly via custom connector or MCP |
| **Orchestration** | M365 Copilot AI decides when to call functions | Topic flows + AI decide (mixed) |
| **Conversation model** | AI-driven — reads function descriptions, decides actions | Scripted topics + AI fallback |
| **Adaptive Cards** | Returned by API plugin responses | Returned by topics + question nodes |
| **Deployment** | M365 Agents Toolkit → org app catalog | Copilot Studio → publish → Copilot channel |
| **Hosting** | No additional hosting — BFF API only | Microsoft-managed SaaS |
| **ISV distribution** | Agent Store | Agent Store (same) |

### When to Escalate to Path B (Copilot Studio)

Copilot Studio adds value only for specific interaction patterns Path A cannot deliver:
- **Structured question sequences** with button choices (topic flows with Adaptive Card question nodes)
- **Enhanced file handling** with built-in upload UX (if Microsoft fixes the attachment limitation)
- **Customer-facing extensibility** — customers build their own agents on our MCP server

### Where Copilot Studio DOES Fit: Customer Extensibility

Copilot Studio becomes relevant for **customer extensibility** after we ship the MCP server (R2, Tier 3). At that point:

```
SPAARKE SHIPS (We build):
  Custom Engine Agent → BFF → Playbooks → Azure OpenAI
  MCP Server → exposes BFF tools universally

CUSTOMER BUILDS (They extend):
  Copilot Studio Agent
    ├── Connects to Spaarke MCP Server (our tools)
    ├── Adds their own knowledge sources (internal docs, policies)
    ├── Adds their own prompts and topics
    ├── Scoped to their roles and governance
    └── Published to their M365 tenant via Agent Store
```

**Customer use cases for Copilot Studio + our MCP**:
- "We want a Copilot agent that searches our Spaarke documents AND our internal SharePoint site for compliance policies"
- "We need a custom agent that runs the NDA Review playbook but adds our firm's specific clause standards as knowledge"
- "Our paralegals need a simplified agent that only exposes document search and quick summary — not the full playbook library"

**This is a platform play**: Every customer agent built on our MCP server increases platform stickiness. We don't build or maintain these agents — customers do, using tools they already have (Copilot Studio). We provide the intelligence layer.

### Product Positioning

| Product Layer | Who Builds | Technology |
|---|---|---|
| **Spaarke AI Platform** (our product) | Spaarke | Custom Engine Agent + BFF + Playbooks |
| **Spaarke MCP Server** (our platform) | Spaarke | MCP server wrapping BFF tools |
| **Customer AI Extensions** (their agents) | Customer | Copilot Studio consuming our MCP |
| **M365 Native Copilot** (Microsoft's product) | Microsoft | Built-in, Spaarke-unaware (unless extended) |

---

## Known Platform Limitations (March 2026)

| Limitation | Impact | Workaround |
|---|---|---|
| **File attachments silently dropped** in Copilot Chat for Custom Engine Agents | Users cannot drag-and-drop documents for analysis via Custom Engine Agents | Users describe documents by name; agent searches SPE |
| **ConversationFileReference contents unknown** for Declarative Agents | When user attaches via "+", Declarative Agent receives `ConversationFileReference` — unclear if this includes Graph-resolvable driveItem ID | **Technical spike required** (Spike 1) — file goes to OneDrive `Microsoft Copilot Chat Files/` folder; BFF needs driveItem ID to retrieve via `GET /me/drive/items/{id}/content` |
| **`Action.OpenUrl` not supported** for Custom Engine Agents in Copilot Chat | Cannot use URL buttons in Adaptive Cards | Use text deep-links or `Action.Submit` that triggers navigation advice |
| **`Action.OpenUrlDialog`** only works for Declarative Agents | Cannot open modal dialogs from Custom Engine Agents | Return deep-link in text response |
| **No true SSE streaming** in Copilot Chat | Cannot stream token-by-token like SprkChat | Use typing indicators + progressive card updates in Teams; final card in Copilot Chat |
| **Adaptive Card schema 1.5** maximum in Copilot | Some newer card features unavailable | Design within 1.5 constraints |

These limitations reinforce the **handoff pattern** — M365 surfaces handle discovery, quick analysis, and structured results; deep interactive work happens in Analysis Workspace + SprkChat.

---

## SprkChat vs M365 Copilot: Capability Comparison

### What M365 Copilot CAN Do (via Declarative Agent + API Plugin)

| Capability | How |
|---|---|
| Text chat with AI | Built-in M365 Copilot orchestrator |
| Call BFF API endpoints | API Plugin → OpenAPI spec → direct HTTPS |
| Return Adaptive Cards | API plugin responses render as cards |
| Multi-turn conversation | Built-in conversation management |
| Playbook invocation | API Plugin calls `POST /api/agent/run-playbook` |
| Document search | API Plugin calls BFF search endpoint |
| Deep-link to wizards/workspace | URLs in Adaptive Card actions or text |

### What M365 Copilot CANNOT Do

| Capability | Why Not |
|---|---|
| SSE streaming from our model | Copilot does not support server-sent events from API plugins |
| Inline AI toolbar over editor | No editor surface exists in Copilot |
| Insert response into editor | No editor to insert into |
| BroadcastChannel cross-pane communication | No postMessage/BroadcastChannel between Copilot and MDA forms |
| Custom citation popovers | Copilot controls its own citation rendering |
| Write-back with visual diff | No diff UI; Copilot cannot render before/after comparisons |

### The TWO Capabilities Unique to SprkChat

Only two capabilities genuinely require SprkChat and cannot be replicated in M365 Copilot:

1. **Editor <--> Chat bidirectional integration** (BroadcastChannel) — SprkChat sends content to the Analysis Workspace editor, receives selected text from the editor, and executes compound write-back actions. This requires same-origin cross-pane communication that M365 Copilot cannot participate in.

2. **Inline AI toolbar over text selections** — When a user selects text in the Analysis Workspace editor, an AI toolbar appears with contextual actions (expand, refine, rewrite). This is a custom UI overlay on our editor, not something Copilot can provide.

**Conclusion**: M365 Copilot is the general chat UX replacing SprkChat for everything **except** Analysis Workspace editor interaction. SprkChat becomes a special-purpose AI companion exclusively for the Analysis Workspace.

---

## Phases (R1 — Power Apps MDA Focus)

### Phase 1: Declarative Agent + API Plugin for MDA Copilot (MVP)

**Goal**: Spaarke-aware Copilot experience in model-driven app on GA day (April 13).

**Core deliverables**:
- `declarativeAgent.json` — agent manifest with Spaarke-scoped instructions (system prompt), conversation starters, capabilities
- `spaarke-api-plugin.json` — function definitions for BFF API endpoints (document search, playbook invocation, matter queries)
- `spaarke-bff-openapi.yaml` — OpenAPI spec for BFF API endpoints consumed by the API plugin
- Deploy via M365 Agents Toolkit → org app catalog (no Copilot Studio required)
- Dataverse knowledge grounding — Copilot natively queries matters, projects, events, documents
- Adaptive Card templates for: document list, matter summary, task list, playbook results
- Deep-link handoff: "Open in Analysis Workspace" for deep analysis
- Deep-link to wizards: DocumentUploadWizard, SummarizeFilesWizard, PlaybookLibrary with pre-filled params
- Admin configuration: enable/disable Copilot per app, per environment

**Phase 1 Technical Spikes (Priority)**:

| Spike | Question | Why It Matters |
|---|---|---|
| **Spike 1: ConversationFileReference** | What does `ConversationFileReference` actually contain when a user attaches a file to a Declarative Agent? Is the driveItem ID Graph-resolvable? | Determines whether our BFF can retrieve attached files from OneDrive — the entire document attachment flow depends on this |
| **Spike 2: Adaptive Card Action.Submit** | Do `Action.Submit` buttons work in API plugin responses within MDA Copilot? | Playbook selection UX depends on clickable buttons in returned cards — if broken, need text-based fallback |
| **Spike 3: End-to-end file pipeline** | Validate: user attaches file → API plugin receives reference → BFF retrieves from OneDrive → runs playbook → returns Adaptive Card | Proves the complete document analysis flow works through the Declarative Agent + API Plugin path |

### Phase 2: Custom Connector + BFF Agent Gateway

**Goal**: Connect MDA Copilot to our AI primitives via BFF API.

- New BFF endpoint: `POST /api/agent/message` — agent gateway accepting Copilot activities
- `GET /api/agent/playbooks?documentType={type}` — available playbooks for document context
- `POST /api/agent/run-playbook` — invoke playbook execution, return structured results
- SSO token flow: MDA Copilot user identity → custom connector → BFF API OBO token → Graph/Dataverse
- Adaptive Card formatter service: playbook output JSON → Adaptive Card JSON
- SPE document search through BFF (not direct Copilot grounding — `discoverabilityDisabled = true`)
- Multi-turn conversation support with session management

### Phase 3: Playbook Integration + Rich Interactions

**Goal**: Full playbook invocation from Copilot side pane with structured results.

- Document search → playbook selection → execution → Adaptive Card results (full flow)
- Progress indicators for long-running playbook execution (typing indicator + progressive card updates)
- Playbook library browse via Copilot ("What analysis tools are available?")
- Email drafting via `sprk_communication` module triggered from Copilot
- Handoff to wizard Code Pages (DocumentUpload, SummarizeFiles, CreateMatter, CreateEvent) with context
- Write-back confirmation pattern (user approves changes before Dataverse update)

### Phase 4: Enterprise Readiness

**Goal**: Production hardening, governance, and deployment automation.

- Agent 365 registration for Spaarke Copilot agent
- Error handling and graceful degradation (BFF unavailable, token failures, playbook timeouts)
- Telemetry: Copilot interaction logging, playbook invocation metrics, handoff tracking
- Admin controls: which playbooks are exposed to Copilot, per-role restrictions
- BYOK deployment: Bicep templates for customer-hosted BFF + Azure OpenAI + AI Search
- Documentation: admin guide, user guide, troubleshooting

---

## Future Phases (R2 — Deferred to Separate Project)

The following are **out of scope for R1** but architecturally enabled by the work above:

| Capability | Why Deferred | R1 Foundation It Builds On |
|---|---|---|
| **Teams bot** | Different channel, different UX patterns | Agent gateway endpoint, Adaptive Card templates |
| **Outlook plugin** | Requires message extension architecture | SSO token flow, playbook invocation |
| **Copilot Chat standalone** | Broader distribution, different auth model | Custom Engine Agent pattern |
| **Power Pages Agent API** | External portal, different security model | Agent gateway endpoint, BFF tools |
| **MCP server** | Customer extensibility play — not needed when we control both sides | BFF tool handlers exposed via agent gateway |
| **Agent 365 governance** | Enterprise control plane (GA May 1) | Agent registration pattern from Phase 4 |
| **Cross-agent orchestration** | Multi-agent delegation | Playbook dispatcher + structured outputs |

---

## Authentication & Authorization

### Token Flow (MDA Copilot)

```
User on Dataverse form → M365 Copilot side pane
  │
  ├── Copilot has user's Entra ID context (same session as MDA)
  │
  ▼
Declarative Agent → Custom Connector
  │
  ├── Custom connector authenticates to BFF API
  │   (OAuth 2.0 with user's delegated permissions)
  │
  ▼
BFF API
  │
  ├── Validates user identity
  ├── Resolves tenant context
  ├── OBO token → Graph API (for SPE document access)
  ├── OBO token → Dataverse (for record queries)
  │
  ▼
Results returned with user's authorization enforced
```

### Key Security Constraints

- SPE containers remain `discoverabilityDisabled = true` — no direct Copilot grounding
- All document access through BFF with per-matter/per-project authorization
- Agent does NOT cache document content — fetches on demand with user's token
- Tenant isolation enforced at every layer (AI Search tenantId filter, SPE container scoping)

---

## Success Criteria

1. **Day-one presence**: Declarative agent live when M365 Copilot GA hits model-driven apps (April 13)
2. **Document search works**: "Find the NDA for Acme" returns correct documents from SPE via BFF — with authorization enforced
3. **Playbook invocation**: User in Teams triggers NDA Review playbook → receives Adaptive Card with risk findings
4. **Handoff works**: "Open in Workspace" deep-link opens Analysis Workspace with correct context and SprkChat auto-launches
5. **BYOK deployment**: Customer-hosted Azure infrastructure serves the same agent through M365 surfaces
6. **Governance**: Spaarke agents visible in Agent 365 for enterprise IT admins

---

## Dependencies

- **Spaarke BFF API** — existing endpoints + new agent gateway endpoint
- **M365 Copilot GA in MDA** (April 13, 2026) — for Declarative Agent deployment
- **M365 Agents SDK** (GA) — for Custom Engine Agent
- **Azure Bot Service** — hosting for the agent handler
- **MCP specification** — for Tier 3 tool exposure (R2)
- **Agent 365** (GA May 1, 2026) — for Tier 4 governance

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| M365 Copilot GA delayed past April 13 | Declarative agent deployment blocked | Custom Engine Agent works independently of Copilot GA |
| OBO token flow complexity (M365 → BFF → Graph) | Auth failures in multi-hop scenarios | Existing OBO pattern proven in Outlook add-in (sdap-office-integration) |
| Adaptive Card limitations for rich AI output | Can't match SprkChat's interactive experience | Design for "light interaction + handoff" pattern; don't try to replicate SprkChat |
| Customer confusion: two AI surfaces (Copilot + SprkChat) | Users don't know which to use | Clear UX: Copilot for general queries; SprkChat for deep analysis — connected by handoff |
| Agent 365 pricing adds to customer cost | Budget resistance | Bundled in E7; positioned as governance requirement, not optional |
| SPE Agent SDK competes with our custom RAG | Potential overlap / customer confusion | Position as complementary: SPE Agent SDK for simple doc Q&A; our RAG for domain-specific analysis |

---

## Open Questions

1. **Adaptive Card complexity**: What's the maximum practical complexity for analysis result cards? Need to test with real playbook output.
2. **Streaming in Teams**: Teams supports typing indicators but not true SSE. How do we handle long-running playbook executions (3-5 minutes)?
3. **Agent identity**: Does the custom engine agent authenticate as the user (delegated) or as itself (app-only) when calling BFF? Delegated is required for SPE authorization.
4. **Power Pages Agent API**: Is the Agent API for Power Pages mature enough for production use, or should we embed SprkChat directly in the external workspace?
5. **Multi-model in Copilot**: With Claude now available in Copilot via Frontier program, does this affect our custom engine approach? (Likely no — we control the model in our agent.)
6. **Graph Connector for Dataverse metadata**: Should we create a Graph Connector to make Spaarke entity metadata (matter names, project types) available to the Declarative Agent's knowledge grounding? This would improve natural language resolution without custom API calls.

---

## References

- [Custom Engine Agents for Microsoft 365](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/overview-custom-engine-agent)
- [Custom Engine Agent Architecture](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/custom-engine-agent-architecture)
- [M365 Agents SDK](https://learn.microsoft.com/en-us/microsoft-365/agents-sdk/agents-sdk-overview)
- [Declarative Agents for M365 Copilot](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/overview-declarative-agent)
- [MCP in Copilot Studio (GA)](https://www.microsoft.com/en-us/microsoft-copilot/blog/copilot-studio/model-context-protocol-mcp-is-now-generally-available-in-microsoft-copilot-studio/)
- [SharePoint Embedded Agent SDK](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/declarative-agent/spe-da)
- [SPE Agent Advanced Topics (discoverabilityDisabled)](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/declarative-agent/spe-da-adv)
- [Agent 365 Control Plane](https://www.microsoft.com/en-us/microsoft-agent-365)
- [Copilot Cowork and Wave 3](https://www.microsoft.com/en-us/microsoft-365/blog/2026/03/09/powering-frontier-transformation-with-copilot-and-agents/)
- [Power Pages Agent API](https://www.microsoft.com/en-us/power-platform/blog/power-pages/seamlessly-embed-copilot-studio-agents-into-power-pages/)
- [Spaarke AI Strategy & Roadmap](../../docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md)
- [Spaarke AI Chat Strategy (prior version)](../../docs/architecture/AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md)
- [Spaarke AI Architecture](../../docs/architecture/AI-ARCHITECTURE.md)
