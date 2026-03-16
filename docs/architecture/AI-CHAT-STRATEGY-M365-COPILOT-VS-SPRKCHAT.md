# AI Chat Strategy: M365 Copilot vs SprkChat

> **Status**: Draft — Strategic Assessment
> **Date**: 2026-03-16
> **Branch**: `work/ai-sprk-chat-context-awareness-r1`
> **Author**: Ralph Schroeder + Claude Code

---

## Executive Summary

Spaarke's model-driven app has two AI chat capabilities converging: **M365 Copilot** (Microsoft's built-in AI assistant with Dataverse + SPE integration) and **SprkChat** (Spaarke's custom-built side pane with streaming analysis workspace). Rather than choosing one, we adopt a **two-plane strategy** — each serves a distinct purpose, analogous to how we use native Dataverse search for basic entity queries alongside AI semantic search for deeper analytical work.

| Plane | Technology | Purpose | Analogy |
|-------|-----------|---------|---------|
| **General AI Chat** | M365 Copilot + Copilot Studio | Record Q&A, navigation, entity queries, general-purpose AI assistance | Native Dataverse Search |
| **Interactive Analysis** | SprkChat (custom) | Streaming document analysis, guided playbook workflows, interactive analysis workspace | AI Semantic Search |

**Key Principle**: Spaarke's playbooks (JPS) remain the canonical source of AI process definition and orchestration. M365 Copilot provides the general-purpose chat frontend; SprkChat provides the purpose-built analysis frontend. Both call the same BFF API backend.

---

## Strategic Context

### What M365 Copilot Already Does (Out-of-Box)

M365 Copilot is available in the Spaarke model-driven app with these capabilities:

- **Dataverse integration**: Queries records, opens forms, answers questions about entity data
- **SPE document access**: Reads and references documents from SharePoint Embedded containers
- **Record summarization**: Summarizes entity records with related data
- **Navigation**: Opens specific records and views from natural language
- **General AI**: Answers general questions, drafts content

### What SprkChat Does (Custom-Built)

- **Streaming analysis workspace**: Real-time AI analysis with progress indicators
- **Playbook-driven workflows**: Guided multi-step analysis processes (document summarization, financial analysis, etc.)
- **Context-aware**: Detects current entity form and adapts behavior
- **Interactive editor**: User reviews, edits, and approves AI-generated content before write-back
- **BFF API integration**: Direct connection to Spaarke's AI pipeline (Azure OpenAI, Document Intelligence, AI Search)

### The Two-Plane Decision

| Criterion | M365 Copilot | SprkChat |
|-----------|-------------|----------|
| General Q&A about records | Excellent (built-in) | Overkill |
| Navigation & record lookup | Excellent (built-in) | Not designed for this |
| Streaming document analysis | Not supported | Purpose-built |
| Interactive analysis workspace | No custom UI in responses | Full React workspace |
| Guided multi-step playbooks | Possible via Topics | Native capability |
| Custom UI (charts, editors, approval flows) | Limited to Adaptive Cards | Full React + Fluent UI |
| Deployment effort | Configuration + Topics | Custom code + deployment |
| Maintenance burden | Microsoft-managed | Spaarke-managed |

**Recommendation**: Use M365 Copilot for general-purpose AI chat in the model-driven app. Retain SprkChat for the interactive analysis workspace and streaming document analysis workflows where custom UI is essential.

---

## Architecture: How M365 Copilot Calls Spaarke's BFF API

### Integration Pattern

```
┌─────────────────────────────────────────────────────┐
│                 Model-Driven App                     │
│                                                      │
│  ┌──────────────┐         ┌──────────────────────┐  │
│  │ M365 Copilot │         │   SprkChat Pane      │  │
│  │ (General AI) │         │ (Analysis Workspace) │  │
│  └──────┬───────┘         └──────────┬───────────┘  │
│         │                            │               │
│         ▼                            │               │
│  ┌──────────────┐                    │               │
│  │Copilot Studio│                    │               │
│  │   Topics     │                    │               │
│  │   (YAML)     │                    │               │
│  └──────┬───────┘                    │               │
│         │                            │               │
│         ▼                            ▼               │
│  ┌──────────────────────────────────────────────┐   │
│  │          Custom Connector (REST)              │   │
│  │     → Spaarke BFF API Endpoints               │   │
│  └──────────────────┬───────────────────────────┘   │
└─────────────────────┼───────────────────────────────┘
                      │
                      ▼
          ┌───────────────────────┐
          │    Spaarke BFF API    │
          │  (Sprk.Bff.Api)      │
          │                       │
          │  ┌─────────────────┐  │
          │  │ AI Tool Service │  │
          │  │ (Orchestrator)  │  │
          │  └────────┬────────┘  │
          │           │           │
          │  ┌────────┼────────┐  │
          │  ▼        ▼        ▼  │
          │ Azure   Doc      AI   │
          │ OpenAI  Intel   Search│
          └───────────────────────┘
```

### Custom Connector (Not Power Automate)

M365 Copilot connects to the BFF API via **direct REST API actions** in Copilot Studio — no Power Automate cloud flows required. This aligns with Spaarke's principle of avoiding low-code components.

```yaml
# Example: Custom connector action (conceptual)
action:
  name: SummarizeDocument
  type: REST
  endpoint: https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/tools/document-summary
  method: POST
  authentication: OAuth2 (Azure AD)
  parameters:
    - name: documentId
      type: string
    - name: containerId
      type: string
```

### Response Format Constraint

M365 Copilot responses support **Adaptive Cards v1.6** for rich formatting but do **NOT** support custom React components or PCF controls embedded in responses. This is the key limitation that necessitates SprkChat for interactive analysis workflows.

| Response Type | M365 Copilot | SprkChat |
|--------------|-------------|----------|
| Text/Markdown | Yes | Yes |
| Adaptive Cards | Yes (v1.6) | No (uses React) |
| Custom React UI | No | Yes |
| Streaming output | No | Yes (SSE) |
| Interactive editors | No | Yes |
| Approval workflows | Limited | Full custom UI |

---

## Copilot Studio: Pro-Code Development Path

### No Low-Code Constraint Compatibility

Spaarke's principle: **"We do not use Power App low-code components (Power Apps, Power Fx) or legacy Dynamics (business rules, plugins)."**

Copilot Studio supports a **pro-code development path** that is compatible with this principle:

| Approach | Low-Code? | Spaarke Compatible? |
|----------|-----------|-------------------|
| Visual designer (drag-and-drop) | Yes | For prototyping only |
| **VS Code extension (YAML files)** | **No — pro-code** | **Yes — primary approach** |
| Custom connectors (REST API) | No — configuration | Yes |
| Power Automate flows | Yes (low-code) | **No — avoid** |
| Power Fx expressions | Yes (low-code) | **No — avoid** |

### VS Code Extension for Copilot Studio

The **Copilot Studio VS Code extension** enables clone/edit of agents as YAML files:

```
agent-project/
├── agent.mcs.yaml           # Agent definition (name, description, instructions)
├── topics/
│   ├── summarize-document.mcs.yaml    # Topic: Summarize Document
│   ├── analyze-financials.mcs.yaml    # Topic: Analyze Financials
│   └── search-matters.mcs.yaml       # Topic: Search Matters
├── actions/
│   └── bff-api-connector.yaml        # Custom connector to BFF API
└── knowledge/
    └── sources.yaml                   # Knowledge source configuration
```

**Example Topic YAML** (thin routing to BFF API):

```yaml
# topics/summarize-document.mcs.yaml
kind: AdaptiveDialog
triggers:
  - kind: OnRecognizedIntent
    intent: SummarizeDocument
    patterns:
      - "summarize this document"
      - "what does this document say"
      - "give me a summary of {documentName}"
actions:
  - kind: InvokeRESTAction
    connection: SpaarkeCustomConnector
    operationId: SummarizeDocument
    inputs:
      documentId: "{documentId}"
      containerId: "{containerId}"
  - kind: SendMessage
    message: "{actionOutput.summary}"
```

### "Skills for Copilot Studio" Plugin

The open-source **"Skills for Copilot Studio"** plugin works with Claude Code, enabling:
- Authoring Topics as YAML from task definitions
- Validating Topic structure against schema
- Testing Topics locally before deployment

### Key Distinction: Topics Are Thin Routers

Topics in Copilot Studio should be **thin routing layers** — they receive the user's intent, extract parameters, call the BFF API via custom connector, and format the response. All intelligence stays in the BFF API.

```
User: "Summarize the engagement letter for Matter 2024-001"
  │
  ▼
Copilot Studio Topic: SummarizeDocument
  │ → Extract: documentName="engagement letter", matterRef="2024-001"
  │ → Resolve: documentId, containerId from Dataverse
  │ → Call: POST /api/ai/tools/document-summary
  │ → Format: Adaptive Card with summary
  │
  ▼
User sees: Formatted summary in Copilot chat
```

---

## Playbook-to-Copilot-Studio Mapping

### Concept Alignment

| Spaarke Concept | Copilot Studio Equivalent | Notes |
|----------------|--------------------------|-------|
| **Playbook (JPS)** | **Topic** | Thin routing to BFF API; playbook remains canonical |
| **Analysis Action** | **Tool / Action** | REST API endpoint on BFF |
| **Context Mapping** | **Topic trigger phrases** | Intent recognition patterns |
| **AI Tool Handler (C#)** | **Custom connector endpoint** | BFF API route |
| **Knowledge docs (SPE files)** | **Knowledge source** | Copilot can index SPE containers |
| **Scope / Model (Azure OpenAI)** | **Not mapped** | Stays in BFF — Copilot doesn't control model selection |
| **JPS Schema** | **Topic YAML** | Different format, same semantic content |

### How Playbooks Inform Topics

Playbooks remain the **source of truth** for AI process definitions. Copilot Studio Topics are generated from playbook metadata:

```
Playbook (JPS JSON)                    Copilot Studio Topic (YAML)
─────────────────                      ─────────────────────────────
{                                      kind: AdaptiveDialog
  "name": "document-summary",         triggers:
  "description": "Summarize...",         - kind: OnRecognizedIntent
  "actions": [                             patterns:
    {                                        - "summarize this document"
      "type": "document-summary",            - "what does this say"
      "endpoint": "/api/ai/tools/..."    actions:
    }                                      - kind: InvokeRESTAction
  ],                                         operationId: SummarizeDocument
  "contextMapping": {                    ...
    "entityType": "sprk_matter",
    "triggers": ["summarize"]
  }
}
```

**Workflow**: Playbook (JPS) → Topic YAML generation → Copilot Studio deployment

This means:
1. **Design** playbooks using existing JPS tools (`/jps-playbook-design`)
2. **Generate** Copilot Studio Topics from playbook definitions
3. **Deploy** Topics via Dataverse solution (see ALM section below)

---

## Analysis Workspace Integration

### The Interactive Analysis Challenge

The Analysis Workspace requires capabilities that M365 Copilot cannot provide:

1. **Retrieve SPE file** → AI processes → **streaming results** → **user reviews/edits** → **approves** → **write back to Dataverse**
2. **Multi-step guided workflows** with custom React UI at each step
3. **Real-time progress indicators** (SSE streaming from BFF)
4. **Interactive document editor** for reviewing and modifying AI output

### Hybrid Approach: Copilot Initiates, SprkChat Executes

For analysis workflows, M365 Copilot can **initiate** the process, then **hand off** to SprkChat for the interactive portion:

```
User in M365 Copilot: "Analyze the financials for Matter 2024-001"
  │
  ▼
Copilot Studio Topic: AnalyzeFinancials
  │ → Recognizes this needs interactive analysis workspace
  │ → Returns: "Opening Analysis Workspace for Matter 2024-001..."
  │ → Calls: Agent API executeEvent() to open SprkChat pane
  │       OR returns an Adaptive Card with "Open in Analysis Workspace" button
  │
  ▼
SprkChat Analysis Workspace opens with:
  → Matter context pre-loaded
  → Financial analysis playbook selected
  → Streaming analysis begins
  → User reviews, edits, approves results
```

### Agent API for PCF/Copilot Integration

Copilot Studio provides the **Agent API** for programmatic interaction:

```typescript
// PCF or Code Page can trigger Copilot Studio programmatically
Xrm.Copilot.executeEvent({
  eventName: "AnalyzeDocument",
  parameters: {
    documentId: "...",
    containerId: "..."
  }
});

// Or send a prompt
Xrm.Copilot.executePrompt({
  prompt: "Summarize the engagement letter for this matter"
});
```

This enables the **reverse direction** too — SprkChat or the Analysis Workspace can delegate simple queries to M365 Copilot.

---

## ALM: Packaging and Deployment

### Copilot Studio Artifacts in Dataverse Solutions

Copilot Studio agents, Topics, and connectors are stored as **Dataverse solution components**. This fits Spaarke's existing ALM pipeline:

```bash
# Export Copilot Studio agent as part of a Dataverse solution
pac solution export --name SpaarkeAiAgent --path ./exports/SpaarkeAiAgent.zip

# Import to target environment
pac solution import --path ./exports/SpaarkeAiAgent.zip --publish-changes
```

### Solution Structure

```
SpaarkeAiAgent (Dataverse Solution)
├── Agent: Spaarke AI Assistant
│   ├── Topics/
│   │   ├── SummarizeDocument
│   │   ├── AnalyzeFinancials
│   │   ├── SearchMatters
│   │   └── OpenAnalysisWorkspace
│   ├── Actions/
│   │   └── SpaarkeCustomConnector (REST → BFF API)
│   └── Knowledge Sources/
│       └── SPE Container Reference
└── Solution metadata (publisher: spaarke)
```

### Deployment Pipeline

```
Developer Workstation                    Dataverse
─────────────────────                    ─────────
VS Code + Copilot Studio Extension
  │
  ├── Edit Topics (YAML)
  ├── Edit Connector (YAML)
  ├── Test locally
  │
  ▼
pac solution export --name SpaarkeAiAgent
  │
  ▼
pac solution import --publish-changes ───────► Dev Environment
                                               │
                                               ▼
                                          Managed solution
                                          export for Prod
```

This is identical to how we deploy PCF controls, web resources, and ribbon customizations today.

---

## Phased Implementation Roadmap

### Phase 1: Assessment & Prototype (Current)

- [x] Identify M365 Copilot availability in Spaarke app
- [x] Evaluate out-of-box capabilities (Dataverse, SPE integration)
- [x] Document two-plane strategy (this document)
- [ ] Create proof-of-concept Copilot Studio Topic calling BFF API endpoint
- [ ] Validate custom connector authentication (Azure AD → BFF)
- [ ] Test Adaptive Card response formatting

### Phase 2: General-Purpose Topics

- [ ] Create Copilot Studio agent via VS Code extension (YAML)
- [ ] Implement Topics for existing playbooks that don't need interactive UI:
  - Record summarization
  - Matter search / entity lookup
  - Simple document queries
- [ ] Configure custom connector to BFF API
- [ ] Package as Dataverse solution (`SpaarkeAiAgent`)
- [ ] Deploy and test in dev environment

### Phase 3: Analysis Workspace Handoff

- [ ] Implement "Open in Analysis Workspace" handoff pattern
- [ ] Create Topics that recognize interactive workflow requests
- [ ] Build Adaptive Card with deep-link to SprkChat pane
- [ ] Test end-to-end: Copilot → Topic → SprkChat → BFF API → Results
- [ ] Evaluate Agent API (`executeEvent()` / `executePrompt()`) availability

### Phase 4: SprkChat Scope Reduction

- [ ] Identify SprkChat features now covered by M365 Copilot
- [ ] Migrate general Q&A from SprkChat to Copilot Topics
- [ ] Retain SprkChat for: streaming analysis, interactive editors, approval flows
- [ ] Evaluate standalone app needs (non-model-driven-app scenarios)

### Phase 5: Standalone App Strategy

- [ ] Assess need for SprkChat in standalone web apps (not Power Platform)
- [ ] If needed: SprkChat remains the AI chat for standalone apps
- [ ] If not needed: SprkChat focuses exclusively on Analysis Workspace

---

## Decision Log

| Decision | Rationale | Date |
|----------|-----------|------|
| Two-plane strategy (Copilot + SprkChat) | M365 Copilot excels at general Q&A but cannot provide custom React UI for interactive analysis | 2026-03-16 |
| Pro-code only via VS Code extension | Aligns with no-low-code principle; YAML files are version-controlled | 2026-03-16 |
| No Power Automate in Copilot Topics | Use direct REST API actions (custom connectors) to BFF instead | 2026-03-16 |
| Playbooks remain canonical | JPS defines AI processes; Topics are thin routers generated from playbooks | 2026-03-16 |
| ALM via Dataverse solutions | Copilot Studio artifacts package into solutions like all other components | 2026-03-16 |
| Retain SprkChat for interactive analysis | Adaptive Cards cannot replace custom React workspace with streaming | 2026-03-16 |

---

## Open Questions

1. **M365 Copilot GA timeline**: Currently available in preview; GA for model-driven apps expected April 2026. Need to confirm feature parity at GA.
2. **Custom connector auth**: Validate Azure AD token flow from Copilot Studio custom connector to BFF API matches existing auth patterns.
3. **Agent API availability**: `Xrm.Copilot.executeEvent()` and `executePrompt()` — confirm availability in current UCI version.
4. **SPE knowledge source**: Can Copilot Studio index SPE containers directly, or must it go through BFF API?
5. **Licensing**: Confirm M365 Copilot licensing covers custom Topics and connectors in the Spaarke tenant.

---

## References

- [AI Architecture](AI-ARCHITECTURE.md) — Spaarke AI Tool Framework
- [Side Pane Platform Architecture](SIDE-PANE-PLATFORM-ARCHITECTURE.md) — SprkChat side pane design
- [Playbook Architecture](playbook-architecture.md) — JPS playbook system
- [BFF API Patterns](sdap-bff-api-patterns.md) — API endpoint patterns
- [Copilot Studio VS Code Extension](https://learn.microsoft.com/en-us/microsoft-copilot-studio/authoring-vscode) — Pro-code authoring
- [Custom Connectors for Copilot Studio](https://learn.microsoft.com/en-us/microsoft-copilot-studio/advanced-connectors) — REST API integration
- [Adaptive Cards v1.6](https://adaptivecards.io/) — Response formatting
