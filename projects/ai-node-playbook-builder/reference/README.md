# AI Node-Based Playbook Builder

> **Status**: Research/Design
> **Created**: January 2026
> **Prerequisite**: R4 Complete, R5 Design

---

## Overview

Transform Spaarke's table-driven playbook configuration into an intuitive visual workflow editor where users construct AI analysis workflows by connecting Action Nodes on a canvas.

## Vision

```
┌──────────────────────────────────────────────────────────────────────┐
│                                                                       │
│   ┌──────────┐      ┌──────────┐      ┌──────────┐      ┌──────────┐│
│   │ Extract  │─────▶│ Analyze  │─────▶│  Detect  │─────▶│ Generate ││
│   │ Entities │      │ Clauses  │      │  Risks   │      │ Summary  ││
│   │          │      │          │      │          │      │          ││
│   │ [Skills] │      │ [Skills] │      │ [Skills] │      │ [Output] ││
│   │ [Tools]  │      │ [Tools]  │      │ [Knowl.] │      │          ││
│   └──────────┘      └──────────┘      └──────────┘      └──────────┘│
│                                                                       │
│   Each node produces a section of the final work product             │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

## Key Features

| Feature | Description |
|---------|-------------|
| **Visual Canvas** | Drag-and-drop node placement with connection drawing |
| **Action Nodes** | Pre-built nodes for Extract, Analyze, Detect, Compare, Generate |
| **Scope Configuration** | Each node configures Skills, Tools, Knowledge, Output |
| **Real-time Validation** | Graph validation with error highlighting |
| **Execution Visualization** | See progress through nodes during analysis |
| **Output Assembly** | Each node produces a section of the final document |

## Design Documents

| Document | Purpose |
|----------|---------|
| [Spaarke_AI_Playbook_Node_Architecture.md](Spaarke_AI_Playbook_Node_Architecture.md) | Original conceptual architecture |
| [NODE-PLAYBOOK-BUILDER-DESIGN.md](NODE-PLAYBOOK-BUILDER-DESIGN.md) | Full design specification |

## Core Concepts

### Playbook
A workflow container that orchestrates a sequence of Actions to analyze a document type.

### Action Node
A single, atomic unit of AI work executed in sequence. Each node has:
- **Skills**: How the AI reasons (heuristics, rubrics)
- **Tools**: How the action executes (handlers)
- **Knowledge**: What the AI references (RAG, standards)
- **Output**: What section is produced (Risk Assessment, Summary)

### Data Flow
Edges between nodes represent data flow. Output from one action becomes input to the next.

## Market Context

Inspired by visual AI workflow tools:
- **Langflow** - LLM orchestration
- **n8n** - General automation with AI nodes
- **Flowise** - RAG/chatbot builder

Spaarke differentiator: **Purpose-built for legal document intelligence** with domain-specific actions, Dataverse integration, and output-aware nodes.

## Integration Points

| System | Integration |
|--------|-------------|
| **R4 Playbook System** | Migration from table-driven to visual |
| **R5 RAG Pipeline** | Knowledge sources with embedding model selection |
| **Dataverse** | Persist canvas, nodes, edges |
| **BFF API** | Compile and execute playbooks |
| **Tool Handlers** | Existing R4 handlers (Summary, RiskDetector, etc.) |

## Technology Stack

| Layer | Technology |
|-------|------------|
| Canvas UI | React + React Flow + Fluent UI v9 |
| State | Zustand |
| PCF Wrapper | Virtual PCF Control |
| API | .NET 8 Minimal API |
| Persistence | Dataverse |

## Phased Rollout

| Phase | Focus |
|-------|-------|
| **Phase 1** | Canvas UI, basic nodes, save/load |
| **Phase 2** | Execution engine, streaming visualization |
| **Phase 3** | Conditional branches, loops, templates |
| **Phase 4** | AI suggestions, optimization, A/B testing |

## Target Users

- **Legal Operations Analysts** - Create org-specific playbooks
- **AI Configuration Specialists** - Tune AI behavior via skills
- **Knowledge Managers** - Curate and attach knowledge sources

---

*See [NODE-PLAYBOOK-BUILDER-DESIGN.md](NODE-PLAYBOOK-BUILDER-DESIGN.md) for full technical specification.*
