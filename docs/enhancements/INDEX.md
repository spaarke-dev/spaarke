# AI Playbook Node Builder R2 - Enhancement Index

> **Project**: AI Playbook Node Builder R2
> **Created**: January 16, 2026

---

## Overview

This directory contains enhancement design documents for the AI Playbook Node Builder R2 project. Each enhancement addresses specific feature additions or improvements to the playbook builder system.

---

## Enhancement List

| ENH | Title | Priority | Status | Effort | Document |
|-----|-------|----------|--------|--------|----------|
| **001/002** | Canvas Node Types | High/Medium | Pending | 2-3 weeks | [ENH-001-002-canvas-node-types.md](ENH-001-002-canvas-node-types.md) |
| **003** | Flexible Input Model | High | Pending | 3-4 weeks | [ENH-003-flexible-input-model.md](ENH-003-flexible-input-model.md) |
| **004** | Parallel Execution Visualization | Low | Pending | 2-3 days | [ENH-004-parallel-visualization.md](ENH-004-parallel-visualization.md) |
| **005** | AI-Assisted Playbook Builder | High | Design | 6-7 weeks | [ai-chat-playbook-builder.md](../../projects/ai-playbook-node-builder-r2/ai-chat-playbook-builder.md) |

---

## Enhancement Summaries

### ENH-001 & ENH-002: Canvas Node Types

**Combined enhancement** covering two related node type additions:

- **ENH-001: Assemble Output Node Type** - New node type for explicit output consolidation before delivery. Handles selective inclusion, transformation, template mapping, validation, and aggregation.

- **ENH-002: Start Node (Implicit)** - Clarification of playbook entry points. Recommends implicit start behavior (nodes with no incoming edges execute first).

### ENH-003: Flexible Input Model

**Major enhancement** enabling flexible document input patterns:

- **Pattern A**: Subject + Knowledge (RAG-enhanced analysis)
- **Pattern B**: Document Comparison (side-by-side)
- **Pattern C**: Consolidated Analysis (multi-doc merge)
- **Pattern D**: Ad-Hoc File Analysis (temp uploads)

Includes unified API design, C# models, and UI dialog concepts.

### ENH-004: Parallel Execution Visualization

**Minor enhancement** adding visual indicators for parallel node execution:

- Shows which nodes execute concurrently vs. sequentially
- Toggle-based (non-intrusive by default)
- Helps users optimize playbook performance

### ENH-005: AI-Assisted Playbook Builder

**Major enhancement** - Full design document located in project folder.

Key features:
- Natural language playbook creation
- Real-time canvas updates via streaming
- Intelligent scope management (Save As, Extend, Create New)
- Test execution modes (Mock, Quick, Production)
- Unified AI agent framework (PB-BUILDER meta-playbook)
- Tiered AI model selection for cost optimization

---

## Implementation Recommendations

### Suggested Project Groupings

Based on dependencies and logical execution order:

**Project Group 1: Foundation (ENH-001/002 + ENH-004)**
- Canvas node types and visualization
- ~3 weeks combined
- No external dependencies

**Project Group 2: Input Flexibility (ENH-003)**
- Flexible input model with RAG
- ~3-4 weeks
- Can run in parallel with Group 1

**Project Group 3: AI Builder (ENH-005)**
- AI-assisted playbook builder
- ~6-7 weeks
- Depends on stable canvas foundation (Group 1)

---

## Related Documents

- [Main Design Document](../../projects/ai-playbook-node-builder-r2/design.md)
- [AI Playbook Architecture](../architecture/AI-PLAYBOOK-ARCHITECTURE.md)
- [Playbook Real Estate Lease Guide](../guides/PLAYBOOK-REAL-ESTATE-LEASE-ANALYSIS.md)

---

## Revision History

| Date | Changes |
|------|---------|
| 2026-01-16 | Initial index created |
