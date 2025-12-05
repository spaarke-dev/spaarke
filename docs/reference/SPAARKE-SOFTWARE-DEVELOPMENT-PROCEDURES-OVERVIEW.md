# Spaarke Software Development Procedures

> **Version**: 2.0  
> **Last Updated**: December 4, 2025  
> **Status**: Active  
> **Audience**: Product Managers, Software Engineers, AI Agents

---

## Overview

This document provides Spaarke's end-to-end software development procedures, integrating **AI-directed development** with **human engineering oversight**.

**For detailed documentation, see the modular reference guides in [`procedures/`](procedures/INDEX.md).**

> 📄 **Full consolidated version**: [SPAARKE-SOFTWARE-DEVELOPMENT-PROCEDURES-FULL.md](SPAARKE-SOFTWARE-DEVELOPMENT-PROCEDURES-FULL.md) (1800+ lines)

---

## Quick Navigation

### By Role

| Role | Start Here | Key Documents |
|------|------------|---------------|
| **Product Manager** | [01-overview.md](procedures/01-overview.md) | [02-stage-0-discovery.md](procedures/02-stage-0-discovery.md), [03-stages-1-3-planning.md](procedures/03-stages-1-3-planning.md) |
| **Software Engineer** | [07-quick-start.md](procedures/07-quick-start.md) | [03-stages-1-3-planning.md](procedures/03-stages-1-3-planning.md), [08-stage-checklists.md](procedures/08-stage-checklists.md) |
| **AI Agent** | [04-ai-execution-protocol.md](procedures/04-ai-execution-protocol.md) | [05-poml-reference.md](procedures/05-poml-reference.md), [06-context-engineering.md](procedures/06-context-engineering.md) |

### By Stage

| Stage | Name | Document |
|-------|------|----------|
| 0 | Discovery & Research | [02-stage-0-discovery.md](procedures/02-stage-0-discovery.md) |
| 1-3 | Planning & Design | [03-stages-1-3-planning.md](procedures/03-stages-1-3-planning.md) |
| 4-6 | AI-Directed Development | [04-ai-execution-protocol.md](procedures/04-ai-execution-protocol.md), [07-quick-start.md](procedures/07-quick-start.md) |
| 7-8 | Testing & Completion | [08-stage-checklists.md](procedures/08-stage-checklists.md) |

---

## Document Index

| Document | Purpose | Size |
|----------|---------|------|
| [INDEX.md](procedures/INDEX.md) | Navigation hub | ~80 lines |
| [01-overview.md](procedures/01-overview.md) | Introduction, roles, lifecycle | ~220 lines |
| [02-stage-0-discovery.md](procedures/02-stage-0-discovery.md) | Discovery & research process | ~120 lines |
| [03-stages-1-3-planning.md](procedures/03-stages-1-3-planning.md) | Feature request → design spec | ~350 lines |
| [04-ai-execution-protocol.md](procedures/04-ai-execution-protocol.md) | **AI task execution protocol** | ~200 lines |
| [05-poml-reference.md](procedures/05-poml-reference.md) | POML tag definitions | ~200 lines |
| [06-context-engineering.md](procedures/06-context-engineering.md) | Context thresholds, handoffs | ~100 lines |
| [07-quick-start.md](procedures/07-quick-start.md) | Design spec → completion guide | ~180 lines |
| [08-stage-checklists.md](procedures/08-stage-checklists.md) | Stage-by-stage checklists | ~130 lines |

---

## Development Lifecycle Summary

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    SPAARKE DEVELOPMENT LIFECYCLE                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   DISCOVERY & DESIGN                  AI-DIRECTED DEV     QUALITY       │
│   ══════════════════                  ═══════════════     ═══════       │
│                                                                         │
│   Stage 0    Stage 1    Stage 2    Stage 3                              │
│   Discovery  Feature    Solution   Design     ─┐                        │
│   & Research Request    Assessment Spec        │                        │
│       │          │          │         │        │                        │
│       ▼          ▼          ▼         ▼        │                        │
│   [Research] [Use Cases] [RFC]    [BDD/Figma]  │                        │
│                                       │        │                        │
│                              ┌────────┘        │                        │
│                              ▼                 │                        │
│                        Stage 4    Stage 5    Stage 6                    │
│                        Project    Task       Task       ─┐              │
│                        Init       Decompose  Execute     │              │
│                            │          │         │        │              │
│                            ▼          ▼         ▼        │              │
│                       [plan.md]  [.poml]    [Code]       │              │
│                                                 │        │              │
│                                        ┌───────┘        │              │
│                                        ▼                │              │
│                                  Stage 7    Stage 8     │              │
│                                  Testing    Complete ───┘              │
│                                      │          │                       │
│                                      ▼          ▼                       │
│                                  [Tests]    [PR Merged]                │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Human-in-the-Loop Summary

| Transition | Type | Approver |
|------------|------|----------|
| Stage 0 → 1 | ✋ GATE | PM |
| Stage 1 → 2 | ✋ GATE | PM |
| Stage 2 → 3 | ✋ GATE | Dev Lead |
| Stage 3 → 4 | ✋ GATE | PM + Dev |
| Stage 4 → 5 | ⚡ CHECKPOINT | Dev |
| Stage 5 → 6 | ⚡ CHECKPOINT | Dev |
| During Stage 6 | ⚡ CHECKPOINT | Dev (spot-check) |
| Stage 7 → 8 | ✋ GATE | Dev |
| Stage 8 → Complete | ✋ GATE | PM |

**Legend**: ✋ GATE = Formal approval required | ⚡ CHECKPOINT = Review, proceed if OK

---

## For AI Agents

**Critical rules are embedded in root CLAUDE.md.** Reference full protocols only for detailed guidance:

| Protocol | Location | When to Reference |
|----------|----------|-------------------|
| AIP-001 | `protocols/AIP-001-task-execution.md` | Handoff templates, session strategy |
| AIP-002 | `protocols/AIP-002-poml-format.md` | Creating/parsing .poml files |
| AIP-003 | `protocols/AIP-003-human-escalation.md` | Escalation format examples |

---

## Related Resources

| Resource | Location | Purpose |
|----------|----------|---------|
| **ADRs** | `docs/reference/adr/` | Architecture principles (system) |
| **AIPs** | `docs/reference/protocols/` | AI behavior principles (agent) |
| Skills | `.claude/skills/` | Workflow definitions |
| Templates | `docs/ai-knowledge/templates/` | Project/task scaffolding |
| Root CLAUDE.md | `/CLAUDE.md` | Embedded critical rules |

---

*Document version: 2.0 | December 4, 2025*
