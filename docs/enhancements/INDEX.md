# Enhancements & Strategy Documents

> **Purpose**: Feature enhancement proposals, strategy documents, and design specifications that are not yet (or never intended to be) architectural decisions
> **Audience**: Product and engineering leads, architects, developers
> **Last Reviewed**: 2026-04-05

## Overview

This directory contains enhancement proposals, positioning/strategy documents, and pre-implementation designs. Contents here are candidates for implementation or already-adopted strategies. For implemented architecture decisions, see `docs/architecture/`. For formal ADRs, see `docs/adr/`.

---

## Strategy & Positioning

- [AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md](AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md) - Two-plane AI strategy: M365 Copilot (general) + SprkChat (contextual). Positioning and roadmap. **For current SprkChat technical architecture, see [`docs/architecture/chat-architecture.md`](../architecture/chat-architecture.md).**

## Cross-Cutting Enhancements

- [ENH-013-ai-authorization-and-service-unification.md](ENH-013-ai-authorization-and-service-unification.md) - AI authorization filter unification and service consolidation

## AI Playbook Node Builder R2

Enhancement designs for the Playbook Node Builder R2 project:

| ENH | Title | Priority | Status | Document |
|-----|-------|----------|--------|----------|
| **001/002** | Canvas Node Types | High/Medium | Pending | [ENH-001-002-canvas-node-types.md](ENH-001-002-canvas-node-types.md) |
| **003** | Flexible Input Model | High | Pending | [ENH-003-flexible-input-model.md](ENH-003-flexible-input-model.md) |
| **004** | Parallel Execution Visualization | Low | Pending | [ENH-004-parallel-visualization.md](ENH-004-parallel-visualization.md) |

### Enhancement Summaries

**ENH-001 & ENH-002: Canvas Node Types** — Combined enhancement covering two related node type additions:
- **ENH-001: Assemble Output Node Type** — New node type for explicit output consolidation before delivery. Handles selective inclusion, transformation, template mapping, validation, and aggregation.
- **ENH-002: Start Node (Implicit)** — Clarification of playbook entry points. Recommends implicit start behavior (nodes with no incoming edges execute first).

**ENH-003: Flexible Input Model** — Enables flexible document input patterns:
- Pattern A: Subject + Knowledge (RAG-enhanced analysis)
- Pattern B: Document Comparison (side-by-side)
- Pattern C: Consolidated Analysis (multi-doc merge)
- Pattern D: Ad-Hoc File Analysis (temp uploads)

**ENH-004: Parallel Execution Visualization** — Adds visual indicators for parallel node execution. Shows concurrent vs sequential nodes; toggle-based non-intrusive display.

---

## Related Resources

- [Architecture Decisions (ADRs)](../adr/INDEX.md) — Formal architectural decisions
- [Architecture Docs](../architecture/INDEX.md) — Current technical architecture
- [Playbook Architecture](../architecture/playbook-architecture.md) — Current playbook system architecture
