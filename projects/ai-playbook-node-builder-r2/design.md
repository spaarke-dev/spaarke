# AI Playbook Node Builder R2 - Design Document

> **Project**: AI Playbook Node Builder R2
> **Status**: Design
> **Created**: January 16, 2026
> **Last Updated**: January 16, 2026 (ENH-005 extracted to ai-chat-playbook-builder.md)

---

## Overview

This project enhances the Playbook Builder PCF control and the underlying orchestration service to support more sophisticated playbook design and execution capabilities.

---

## Architecture Decisions

### Edge/Connector Behavior

**Decision**: Edges define **execution order only**, not data access restrictions.

| Aspect | Behavior |
|--------|----------|
| Edges mean | "Run this node after connected node completes" |
| Data access | Open - any node can read any previous output by variable name |
| Visual purpose | Shows flow/sequence, not data pipes |

**Rationale**: Simpler mental model for users. All outputs accumulate in a shared dictionary (`nodeOutputs`) accessible to all subsequent nodes.

```
┌────────┐      ┌────────┐      ┌────────┐
│ Node A │─────▶│ Node B │─────▶│ Node C │
└────────┘      └────────┘      └────────┘
    │               │               │
    ▼               ▼               ▼
    ═══════════════════════════════════
           nodeOutputs Dictionary
      (all outputs available to all nodes)
    ═══════════════════════════════════
```

---

## Required Enhancements

> **Note**: Detailed enhancement designs have been extracted to separate documents in [docs/enhancements/](../../docs/enhancements/INDEX.md).

### ENH-001 & ENH-002: Canvas Node Types

> **Full Design**: [ENH-001-002-canvas-node-types.md](../../docs/enhancements/ENH-001-002-canvas-node-types.md)

**Priority**: High (ENH-001), Medium (ENH-002)
**Status**: Pending
**Effort**: 2-3 weeks combined

#### Summary

**ENH-001: Assemble Output Node Type** - Adds a new node type for explicit output consolidation before delivery. Separates the responsibilities of:
- Consolidating/transforming outputs from previous nodes
- Generating and delivering documents (Word, PDF, Email)

Features include selective inclusion, transformation, template mapping, validation, aggregation, and conditional assembly.

**ENH-002: Start Node (Implicit)** - Clarifies playbook entry points. Recommends implicit start behavior where nodes with no incoming edges execute first (simplest approach).

---

### ENH-003: Flexible Input Model (Unified)

> **Full Design**: [ENH-003-flexible-input-model.md](../../docs/enhancements/ENH-003-flexible-input-model.md)

**Priority**: High
**Status**: Pending
**Effort**: 3-4 weeks

#### Summary

Extends playbook execution to support multiple input patterns beyond single SPE-stored documents:

| Pattern | Description | Use Case |
|---------|-------------|----------|
| **A: Subject + Knowledge** | RAG-enhanced analysis | "Analyze lease using our standards" |
| **B: Document Comparison** | Side-by-side comparison | "Compare Vendor A vs Vendor B" |
| **C: Consolidated Analysis** | Multi-doc merge | "Analyze all 5 portfolio leases" |
| **D: Ad-Hoc File** | Temp uploads | "Quick analysis before storing" |

Includes unified API design, C# models, knowledge file RAG processing, and UI dialog concepts.

---

### ENH-004: Parallel Execution Visualization

> **Full Design**: [ENH-004-parallel-visualization.md](../../docs/enhancements/ENH-004-parallel-visualization.md)

**Priority**: Low
**Status**: Pending
**Effort**: 2-3 days

#### Summary

Visual indication in the builder when nodes will execute in parallel vs. sequential. Nodes at same "level" (no dependencies between them) highlighted as parallel group. Implemented as an optional toggle to avoid canvas clutter.

---

### ENH-005: AI-Assisted Playbook Builder

> **Note**: This enhancement has been extracted to its own design document due to its scope and complexity.
>
> **See**: [ai-chat-playbook-builder.md](ai-chat-playbook-builder.md)

**Priority**: High
**Status**: Design
**Effort**: 6-7 weeks

#### Summary

Adds conversational AI assistance to the PlaybookBuilderHost PCF control. Users can build playbooks through natural language while seeing results update in real-time on the visual canvas.

**Key Features:**
- Natural language playbook creation
- Real-time canvas updates via streaming
- Intelligent scope management (Save As, Extend, Create New)
- Test execution modes (Mock, Quick, Production)
- Unified AI agent framework (PB-BUILDER meta-playbook)
- Tiered AI model selection for cost optimization

**Full Design**: [ai-chat-playbook-builder.md](ai-chat-playbook-builder.md)

---

## Reference Documents

### Enhancement Designs (Extracted)

- [Enhancement Index](../../docs/enhancements/INDEX.md) - Overview of all enhancements
- [ENH-001/002: Canvas Node Types](../../docs/enhancements/ENH-001-002-canvas-node-types.md) - Assemble Output + Start Node
- [ENH-003: Flexible Input Model](../../docs/enhancements/ENH-003-flexible-input-model.md) - Multi-doc, RAG, ad-hoc files
- [ENH-004: Parallel Visualization](../../docs/enhancements/ENH-004-parallel-visualization.md) - Execution order visualization
- [ENH-005: AI Chat Playbook Builder](ai-chat-playbook-builder.md) - AI-assisted playbook creation

### Architecture References

- [AI Playbook Architecture](../../docs/architecture/AI-PLAYBOOK-ARCHITECTURE.md)
- [Playbook Real Estate Lease Guide](../../docs/guides/PLAYBOOK-REAL-ESTATE-LEASE-ANALYSIS.md)

---

## Open Questions

1. **Expression language**: What syntax for computed fields? JSONPath? Custom DSL? JavaScript subset?
2. **Transform library**: Pre-built transforms or custom code?
3. **Validation UX**: How to show validation errors in the builder before execution?
4. **Template preview**: Can we preview how outputs will render in the template?

---

## Revision History

| Date | Author | Changes |
|------|--------|---------|
| 2026-01-16 | AI Architecture Team | Initial design document |
| 2026-01-16 | AI Architecture Team | Added ENH-005: AI-Assisted Playbook Builder |
| 2026-01-16 | AI Architecture Team | ENH-005: Added project-style AI architecture (skills, tools, build plan) |
| 2026-01-16 | AI Architecture Team | ENH-005: Added conversational UX guidance with near-deterministic interpretation |
| 2026-01-16 | AI Architecture Team | ENH-005: Added test execution architecture (mock, quick, production modes) |
| 2026-01-16 | AI Architecture Team | ENH-005: Added scope management architecture (ownership model, Save As, Extend, tool config) |
| 2026-01-16 | AI Architecture Team | ENH-005: Added unified AI agent framework (PB-BUILDER meta-playbook, plan concept, builder scopes, model strategy) |
| 2026-01-16 | AI Architecture Team | ENH-005: Updated timeline to 6-7 weeks reflecting unified framework additions |
| 2026-01-16 | AI Architecture Team | ENH-005: Extracted to separate document (ai-chat-playbook-builder.md) |
| 2026-01-16 | AI Architecture Team | All ENH items extracted to docs/enhancements/ directory |
