# Spaarke Development Procedures: Overview

> **Audience**: All (Product Managers, Software Engineers, AI Agents)  
> **Part of**: [Spaarke Software Development Procedures](INDEX.md)

---

## 1. Introduction

### 1.1 Purpose

This document defines Spaarke's end-to-end software development procedures, integrating **AI-directed development** with **human engineering oversight**. It establishes a disciplined workflow where AI accelerates coding while humans ensure architecture, quality, and compliance.

### 1.2 Audience

| Audience | How to Use This Document |
|----------|-------------------------|
| **Product Managers** | Understand the full lifecycle, know what artifacts to produce, identify decision gates requiring your input |
| **Software Engineers** | Follow procedures for each stage, understand AI collaboration patterns, know when human judgment is required |
| **AI Agents** | Reference for context, constraints, and execution protocols during task execution |

### 1.3 Guiding Principles

1. **Structured Autonomy** - AI operates within defined boundaries; humans set direction and validate outcomes
2. **Artifact-Driven Workflow** - Each stage produces specific, templated artifacts enabling handoffs
3. **Human-in-the-Loop by Design** - Explicit checkpoints where human review is required before proceeding
4. **Context Engineering** - Optimized information flow to AI within token constraints
5. **ADR Compliance** - Architecture Decision Records govern all technical decisions
6. **Repeatability** - Consistent processes enable quality at scale

### 1.4 Core Technology Stack

| Layer | Technology | Purpose |
|-------|------------|---------|
| **Discovery & Design** | Figma | UI/UX design, prototypes, wireframes |
| | Miro / FigJam | Collaborative workshops, journey maps |
| | Notion / Confluence | Collaborative documentation, RFCs |
| **Development** | VS Code | Primary development interface |
| | Claude Code | AI coding agent - code generation, task execution |
| | GitHub Copilot Chat | AI documentation - design specs, planning |
| **Quality & Testing** | SpecFlow / Cucumber | BDD - executable specifications |
| | Storybook | Component documentation and testing |
| **Version Control** | Git / GitHub | Repository management, CI/CD |

---

## 2. Role Definitions

### 2.1 Product Manager (PM)

**Primary Responsibility**: Define *what* to build and *why*

| Activity | Deliverable | Stage |
|----------|-------------|-------|
| Lead discovery research | Research findings, validated assumptions | Stage 0 |
| Capture business requirements | Feature Request document | Stage 1 |
| Define user value and use cases | Feature Request document | Stage 1 |
| Participate in solution assessment | Solution Assessment document | Stage 2 |
| Review and approve design specifications | Approved Design Spec | Stage 3 |
| Accept feature completion | Sign-off | Stage 8 |

**Decision Authority**: Feature prioritization, scope decisions, UX requirements, acceptance criteria approval, research direction

### 2.2 Software Engineer (Developer)

**Primary Responsibility**: Define *how* to build and ensure quality

| Activity | Deliverable | Stage |
|----------|-------------|-------|
| Technical feasibility assessment | Solution Assessment document | Stage 2 |
| Architecture and detailed design | Design Specification | Stage 3 |
| Review AI-generated project artifacts | Approved plan.md, tasks | Stage 4-5 |
| Oversee AI task execution | Working code | Stage 6 |
| Code review and testing | Validated code | Stage 7 |
| Technical documentation | Updated docs | Stage 8 |

**Decision Authority**: Technical approach, ADR compliance, code quality, architecture decisions

### 2.3 AI Agent (Claude Code)

**Primary Responsibility**: Execute defined tasks within constraints

| Activity | Human Oversight Required | Stage |
|----------|-------------------------|-------|
| Generate project structure | Review before proceeding | Stage 4 |
| Decompose plan into tasks | Review task list | Stage 5 |
| Execute coding tasks | Spot-check during execution | Stage 6 |
| Run tests and validate | Review test results | Stage 7 |
| Generate documentation | Review before publishing | Stage 8 |

**Operating Boundaries**:
- Must follow ADR constraints
- Must request human input for ambiguous decisions
- Must stop at defined checkpoints
- Must report context usage and trigger handoffs at thresholds

### 2.4 Human-in-the-Loop Checkpoints

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     HUMAN-IN-THE-LOOP CHECKPOINTS                       │
├─────────────────────────────────────────────────────────────────────────┤
│  Stage 0 ──► Stage 1      PM validation of research findings            │
│                           ✋ GATE: Problem validated, proceed approved  │
│                                                                         │
│  Stage 1 ──► Stage 2      PM approval of Feature Request               │
│                           ✋ GATE: Business value validated             │
│                                                                         │
│  Stage 2 ──► Stage 3      Technical team approval of approach          │
│                           ✋ GATE: Technical feasibility confirmed      │
│                                                                         │
│  Stage 3 ──► Stage 4      PM + Dev approval of Design Spec             │
│                           ✋ GATE: Design complete and approved         │
│                                                                         │
│  Stage 4 ──► Stage 5      Dev review of project artifacts              │
│                           ⚡ CHECKPOINT: README and plan.md reviewed    │
│                                                                         │
│  Stage 5 ──► Stage 6      Dev review of task decomposition             │
│                           ⚡ CHECKPOINT: Task list validated            │
│                                                                         │
│  During Stage 6           Dev spot-checks AI execution                 │
│                           ⚡ CHECKPOINT: Periodic code review           │
│                                                                         │
│  Stage 7 ──► Stage 8      Tests pass, ADR compliance verified          │
│                           ✋ GATE: Quality gates passed                 │
│                                                                         │
│  Stage 8 ──► Complete     PM acceptance of feature                     │
│                           ✋ GATE: Feature accepted                     │
│                                                                         │
│  Legend: ✋ GATE = Formal approval required                             │
│          ⚡ CHECKPOINT = Review required, proceed if OK                 │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Development Lifecycle Overview

### 3.1 Lifecycle Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    SPAARKE DEVELOPMENT LIFECYCLE                        │
├─────────────────────────────────────────────────────────────────────────┤
│   ═══════════════════════════════════════════════════════════════       │
│   ║            DISCOVERY & DESIGN PHASES                         ║      │
│   ═══════════════════════════════════════════════════════════════       │
│                                                                         │
│   ┌──────────────┐    ┌──────────────┐    ┌──────────────┐             │
│   │   STAGE 0    │    │   STAGE 1    │    │   STAGE 2    │             │
│   │  Discovery   │───►│   Feature    │───►│   Solution   │             │
│   │  & Research  │    │   Request    │    │  Assessment  │             │
│   └──────────────┘    └──────────────┘    └──────────────┘             │
│         │ PM + UX           │ PM                │ PM + Dev             │
│         ▼                   ▼                   ▼                       │
│   [User Research]      [Use Cases]         [RFC + Options]             │
│                                                 │                       │
│                                          ┌──────────────┐              │
│                                          │   STAGE 3    │              │
│                                          │   Design     │              │
│                                          │    Spec      │              │
│                                          └──────────────┘              │
│                                                 │ Dev + PM             │
│                                                 ▼                       │
│                                          [BDD Scenarios]               │
│                                          [Figma Designs]               │
│                                                 │                       │
│   ═══════════════════════════════════════════════════════════════       │
│   ║            AI-DIRECTED DEVELOPMENT                           ║      │
│   ═══════════════════════════════════════════════════════════════       │
│                                                 │                       │
│   ┌──────────────┐    ┌──────────────┐    ┌──────────────┐             │
│   │   STAGE 4    │    │   STAGE 5    │    │   STAGE 6    │             │
│   │   Project    │───►│    Task      │───►│    Task      │             │
│   │    Init      │    │ Decomposition│    │  Execution   │             │
│   └──────────────┘    └──────────────┘    └──────────────┘             │
│         │ AI + Dev          │ AI + Dev          │ AI + Dev             │
│         ▼                   ▼                   ▼                       │
│    [README.md]         [.poml files]       [Code + Tests]              │
│    [plan.md]           [TASK-INDEX.md]                                 │
│                                                 │                       │
│   ═══════════════════════════════════════════════════════════════       │
│   ║            QUALITY GATES                                     ║      │
│   ═══════════════════════════════════════════════════════════════       │
│                                                 │                       │
│   ┌──────────────┐    ┌──────────────┐                                 │
│   │   STAGE 7    │    │   STAGE 8    │                                 │
│   │   Testing &  │───►│  Docs &      │───► COMPLETE                    │
│   │  Validation  │    │ Completion   │                                 │
│   └──────────────┘    └──────────────┘                                 │
└─────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Stage Summary

| Stage | Name | Owner | Input | Output |
|-------|------|-------|-------|--------|
| 0 | Discovery & Research | PM + UX | Business need | Validated problem, prototypes |
| 1 | Feature Request | PM | Discovery findings | Feature Request doc |
| 2 | Solution Assessment | PM + Dev | Feature Request | RFC, Solution Assessment |
| 3 | Design Specification | Dev (lead) | Solution Assessment | Design Spec + BDD scenarios |
| 4 | Project Initialization | AI + Dev | Design Spec | README.md, plan.md, CLAUDE.md |
| 5 | Task Decomposition | AI + Dev | plan.md | Task files (.poml) |
| 6 | Task Execution | AI + Dev | Task files | Code, tests |
| 7 | Testing & Validation | Dev | Code | Test results, ADR report |
| 8 | Documentation & Completion | Dev + PM | All | Feature docs, merged PR |

### 3.3 Document Format Conventions

| Stage | Format | Rationale |
|-------|--------|-----------|
| 0-2 | Collaborative docs (Notion/Confluence) | Comments, @mentions, stakeholder-friendly |
| 3 | Design doc + BDD (Gherkin) | Executable specs, unambiguous |
| 4-8 | Markdown (.md) / POML (.poml) | AI-optimized, version control friendly |

**Conversion Point**: Design Spec is converted to `spec.md` and placed in `projects/{project-name}/` to begin AI-directed development.

---

## Next Steps

- **For PM**: Continue to [02-stage-0-discovery.md](02-stage-0-discovery.md)
- **For Dev starting a project**: Go to [07-quick-start.md](07-quick-start.md)
- **For AI agents**: Load [04-ai-execution-protocol.md](04-ai-execution-protocol.md)

---

*Part of [Spaarke Software Development Procedures](INDEX.md) | v2.0 | December 2025*
