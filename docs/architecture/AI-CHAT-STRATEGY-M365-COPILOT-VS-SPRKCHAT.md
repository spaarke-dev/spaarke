# AI Chat Strategy: M365 Copilot + SprkChat

> **Status**: Active — Strategic Architecture
> **Date**: 2026-03-16 (Updated)
> **Branch**: `work/ai-sprk-chat-context-awareness-r1`
> **Author**: Ralph Schroeder + Claude Code

---

## Executive Summary

Spaarke's model-driven app has two AI capabilities that serve fundamentally different purposes:

- **M365 Copilot** — General-purpose AI assistant built into the platform (record Q&A, navigation, entity queries)
- **SprkChat** — Purpose-built contextual AI component deployed in specific workflows with specific tools and knowledge

SprkChat is **not** a general chat. It is a flexible component that launches in a specific context with curated resources, playbooks, and tools. When SprkChat opens in the Analysis Workspace, it knows the analysis type, matter type, practice area, and source documents — and provides AI tools tailored to that exact workflow.

### Two-Plane Strategy

| Plane | Technology | Purpose | Analogy |
|-------|-----------|---------|---------|
| **General AI** | M365 Copilot + Copilot Studio | Record Q&A, navigation, entity queries | Native Dataverse Search |
| **Contextual AI** | SprkChat (purpose-built) | Interactive analysis, inline AI tools, context-specific playbooks | AI Semantic Search |

**Key Principle**: SprkChat is deployed per-context with pre-loaded tools and knowledge. It does not compete with M365 Copilot for general chat — it provides what Copilot cannot: streaming analysis, inline document interaction, and context-aware playbook execution.

---

## SprkChat: Contextual AI Component Model

### What SprkChat Is

SprkChat is a **contextual AI component** that:
- Launches in a specific context (e.g., Analysis Workspace, document review)
- Arrives pre-loaded with relevant playbooks, tools, and knowledge
- Provides both **side pane chat** and **inline AI tools** within the workspace
- Shares a session between the chat pane and inline interactions
- Connects to the BFF API with full record context (entity type, analysis type, matter type, practice area)

### What SprkChat Is Not

- Not a general-purpose chatbot (M365 Copilot handles that)
- Not always-present on every page (launches in specific contexts)
- Not a standalone experience (companion to workspaces and editors)

### Context-Driven Architecture

When SprkChat opens, it receives rich context that determines its behavior:

```
Analysis Workspace opens analysis record
    │
    ├─ Entity: sprk_analysisoutput
    ├─ Analysis Type: patent-claims
    ├─ Matter Type: patent
    ├─ Practice Area: intellectual-property
    ├─ Source Document: engagement-letter.pdf
    │
    ▼
Context Mapping Service (BFF API)
    │
    ├─ Resolves: Patent Claims Analysis playbook
    ├─ Loads: Patent-specific tools (claims extraction, prior art search)
    ├─ Configures: Knowledge sources (USPTO, patent databases)
    ├─ Sets: AI model scope (document analysis, legal reasoning)
    │
    ▼
SprkChat launches with:
    ├─ Playbook: Patent Claims Analysis
    ├─ Tools: [Extract Claims, Summarize, Prior Art Search, Claim Mapping]
    ├─ Knowledge: USPTO, patent case law, firm precedents
    └─ Context: Full record metadata for contextual responses
```

### Two Surfaces, One Session

SprkChat provides AI interaction through two integrated surfaces:

```
┌──────────────────────────────────────────────────────────────┐
│ Analysis Workspace                                            │
│                                                               │
│ ┌────────────────────────────────┐  ┌──────────────────────┐ │
│ │ Analysis Output Editor          │  │ SprkChat Pane        │ │
│ │                                 │  │                      │ │
│ │ "The patent claims describe     │  │ Context:             │ │
│ │  a novel method for processing  │  │  Entity: Patent      │ │
│ │  semiconductor wafers using..." │  │  Type: Claims        │ │
│ │       ▲                         │  │  Playbook: Patent    │ │
│ │  ┌────┴──────────────┐         │  │   Claims Analysis    │ │
│ │  │ ✨ Summarize       │         │  │                      │ │
│ │  │ 📝 Simplify        │ Inline  │  │ [Chat history]       │ │
│ │  │ ✓  Fact-check      │ AI Menu │  │                      │ │
│ │  │ 📊 Compare to      │         │  │ User: Summarize the  │ │
│ │  │    prior art       │         │  │ selected claims      │ │
│ │  │ 💬 Ask SprkChat ───────────────►│                      │ │
│ │  └───────────────────┘         │  │ SprkChat: The patent  │ │
│ │                                 │  │ describes 3 primary  │ │
│ │ Source Document Viewer          │  │ claims...            │ │
│ │ ┌─────────────────────────────┐│  │                      │ │
│ │ │ [PDF/Office iframe]         ││  │ [Insert ↩] [Copy]    │ │
│ │ └─────────────────────────────┘│  └──────────────────────┘ │
│ └────────────────────────────────┘                            │
└──────────────────────────────────────────────────────────────┘
```

**Side Pane Chat**: Persistent conversation with full context. Results can be inserted back into the editor.

**Inline AI Tools**: User highlights text in the editor → floating menu appears with context-specific actions. Actions execute via SprkChat's session and results flow into the chat history.

Both surfaces share:
- Same BFF API connection and auth context
- Same playbook and tool set
- Same chat session (inline actions appear in chat history)
- Same record context (analysis type, matter, document)

---

## M365 Copilot: General-Purpose AI

### What M365 Copilot Handles (Out-of-Box)

- **Dataverse integration**: Queries records, opens forms, answers questions about entity data
- **SPE document access**: Reads and references documents from SharePoint Embedded containers
- **Record summarization**: Summarizes entity records with related data
- **Navigation**: Opens specific records and views from natural language
- **General AI**: Answers general questions, drafts content

### Extending via Copilot Studio (Pro-Code)

Spaarke extends M365 Copilot through Copilot Studio using the **pro-code path** (VS Code extension, YAML files — no Power Automate, no Power Fx):

| Approach | Low-Code? | Spaarke Compatible? |
|----------|-----------|-------------------|
| Visual designer (drag-and-drop) | Yes | For prototyping only |
| **VS Code extension (YAML files)** | **No — pro-code** | **Yes — primary approach** |
| Custom connectors (REST API) | No — configuration | Yes |
| Power Automate flows | Yes (low-code) | **No — avoid** |

Topics are **thin routing layers** that call BFF API endpoints via custom connectors. All intelligence stays in the BFF.

### Handoff: Copilot → SprkChat

For workflows requiring interactive analysis, M365 Copilot can initiate and hand off:

```
User in M365 Copilot: "Analyze the financials for Matter 2024-001"
  │
  ▼
Copilot Studio Topic: AnalyzeFinancials
  │ → Recognizes interactive analysis needed
  │ → Returns: Adaptive Card with "Open in Analysis Workspace" button
  │ → Deep-link opens Analysis Workspace with matter context
  │
  ▼
Analysis Workspace launches SprkChat companion with:
  → Matter context pre-loaded
  → Financial analysis playbook selected
  → Relevant tools available
```

---

## Architecture

### Integration Pattern

```
┌──────────────────────────────────────────────────────────────┐
│                    Model-Driven App                           │
│                                                               │
│  ┌──────────────┐     ┌────────────────────────────────────┐ │
│  │ M365 Copilot │     │ Analysis Workspace                 │ │
│  │ (General AI) │     │  ┌─────────┐  ┌────────────────┐  │ │
│  │              │     │  │ Editor  │  │ SprkChat Pane  │  │ │
│  │  Copilot     │     │  │ + Inline│  │ (Contextual)   │  │ │
│  │  Studio      │     │  │ AI Menu │  │                │  │ │
│  │  Topics      │     │  └────┬────┘  └───────┬────────┘  │ │
│  └──────┬───────┘     │       │ BroadcastChannel │         │ │
│         │             └───────┼──────────────────┼─────────┘ │
│         │                     │                  │            │
│         ▼                     ▼                  ▼            │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │              Custom Connector / Direct API               │ │
│  └────────────────────────┬────────────────────────────────┘ │
└───────────────────────────┼──────────────────────────────────┘
                            │
                            ▼
              ┌───────────────────────┐
              │    Spaarke BFF API    │
              │                       │
              │  Context Mapping      │
              │  Service              │
              │    │                  │
              │    ▼                  │
              │  AI Tool Service     │
              │    │                  │
              │  ┌─┼──────────┐      │
              │  ▼ ▼          ▼      │
              │ Azure  Doc   AI      │
              │ OpenAI Intel Search  │
              └───────────────────────┘
```

### SprkChat Launch Context

```typescript
interface SprkChatLaunchContext {
  // Record context
  entityType: string;              // e.g., 'sprk_analysisoutput'
  entityId: string;                // Record GUID

  // Analysis context (resolved from record)
  analysisType?: string;           // e.g., 'patent-claims', 'financial-review'
  matterType?: string;             // e.g., 'patent', 'trademark', 'litigation'
  practiceArea?: string;           // e.g., 'ip', 'corporate'

  // Source file context
  sourceFileId?: string;           // SPE document ID
  sourceContainerId?: string;      // SPE container ID

  // Playbook (pre-resolved or let SprkChat resolve via context mapping)
  playbookId?: string;

  // Mode
  mode: 'standalone' | 'workspace-companion';
}
```

### Playbook-to-Context Mapping

The existing `ChatContextMappingService` resolves record context into playbooks and tools:

| Spaarke Concept | Copilot Studio Equivalent | SprkChat Role |
|----------------|--------------------------|---------------|
| **Playbook (JPS)** | **Topic** | Pre-loaded based on context |
| **Analysis Action** | **Tool / Action** | Available in inline menu + chat |
| **Context Mapping** | **Topic trigger phrases** | Automatic from record metadata |
| **Knowledge docs** | **Knowledge source** | Scoped to analysis type + practice area |

---

## Deployment Model Changes

### What Changes from Global Side Pane

SprkChat was previously deployed as a global side pane (always present via SidePaneManager). The contextual model changes this:

| Aspect | Previous (Global) | New (Contextual) |
|--------|-------------------|-------------------|
| **Launch** | Auto-registers on every page | Launched by host workspace |
| **Presence** | Always visible in side pane rail | Appears when context requires it |
| **Context** | Detects entity from current form | Receives rich context from launcher |
| **Playbook** | Resolved at runtime from entity type | Pre-resolved from analysis metadata |
| **Injection** | SidePaneManager injected into all Code Pages | Only host workspaces launch SprkChat |

### What Stays

- Application Ribbon "SprkChat" button (manual trigger for users)
- `openSprkChatPane.ts` launcher (expanded with richer context)
- BroadcastChannel bridge (existing, well-tested)
- BFF API integration (unchanged)
- Context mapping service (becomes more central)

### What Changes

- Remove SidePaneManager injection from EventsPage, SpeAdminApp
- Analysis Workspace becomes the primary launcher for its context
- Launch context model expands (analysis type, matter type, practice area)
- Inline AI tools added as companion feature

---

## ALM: Packaging and Deployment

### Copilot Studio Artifacts

Copilot Studio agents package into Dataverse solutions via `pac solution export/import`. Pro-code authoring via VS Code extension (YAML files, version-controlled).

### SprkChat Artifacts

SprkChat deploys as web resources (HTML Code Pages, JS scripts) — unchanged from current pipeline.

---

## Phased Implementation Roadmap

### Phase 1: SprkChat Contextual Companion (Current Focus)

- [x] Identify M365 Copilot availability and capabilities
- [x] Document two-plane strategy
- [x] Build context awareness (context mapping service, entity detection)
- [ ] Reposition SprkChat as contextual component (this design)
- [ ] Extend launch context model (analysis type, matter, practice area)
- [ ] Build inline AI tools for Analysis Workspace editor
- [ ] Integration test: Analysis Workspace + SprkChat companion

### Phase 2: Copilot Studio Integration

- [ ] Create Copilot Studio agent via VS Code extension (YAML)
- [ ] Implement Topics for simple queries (record summarization, search)
- [ ] Configure custom connector to BFF API
- [ ] Build "Open in Analysis Workspace" handoff Topic

### Phase 3: Advanced Inline Tools

- [ ] Expand inline AI menu with practice-area-specific tools
- [ ] Add "Insert to Editor" flow from SprkChat responses
- [ ] Build diff review for inline AI suggestions
- [ ] Context-specific knowledge sources (USPTO, case law databases)

### Phase 4: Additional Workspace Companions

- [ ] Evaluate SprkChat companion for other workspaces (document review, contract analysis)
- [ ] Create reusable SprkChat launcher component for new workspaces
- [ ] Standalone app strategy (non-model-driven-app scenarios)

---

## Decision Log

| Decision | Rationale | Date |
|----------|-----------|------|
| Two-plane strategy (Copilot + SprkChat) | M365 Copilot for general Q&A; SprkChat for contextual interactive analysis | 2026-03-16 |
| SprkChat as contextual component, not general chat | Avoids competing with M365 Copilot; focuses on what Copilot cannot do | 2026-03-16 |
| Inline AI tools integral to SprkChat | Same session, same context, same playbook — creates cohesive experience | 2026-03-16 |
| Context-driven playbook association | Analysis type + matter type + practice area → auto-resolves relevant tools | 2026-03-16 |
| Pro-code only via VS Code extension | Aligns with no-low-code principle; YAML files are version-controlled | 2026-03-16 |
| Remove global auto-launch | SprkChat launches in specific contexts, not everywhere | 2026-03-16 |

---

## Open Questions

1. **M365 Copilot GA timeline**: Currently in preview; GA for model-driven apps expected April 2026.
2. **Custom connector auth**: Validate Azure AD token flow from Copilot Studio custom connector to BFF API.
3. **Agent API availability**: `Xrm.Copilot.executeEvent()` — confirm availability in current UCI version.
4. **Inline tool UX**: Floating menu vs. toolbar vs. right-click context menu for inline AI actions.
5. **Cross-workspace reuse**: How much of the SprkChat companion is reusable across different workspace types.

---

## References

- [AI Architecture](AI-ARCHITECTURE.md) — Spaarke AI Tool Framework
- [Side Pane Platform Architecture](SIDE-PANE-PLATFORM-ARCHITECTURE.md) — Side pane design
- [Playbook Architecture](playbook-architecture.md) — JPS playbook system
- [BFF API Patterns](sdap-bff-api-patterns.md) — API endpoint patterns
- [Analysis Workspace SprkChat Companion Design](../../projects/ai-sprk-chat-context-awareness-r1/design.md) — Detailed design specification
