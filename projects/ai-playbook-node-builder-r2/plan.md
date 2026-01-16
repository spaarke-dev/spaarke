# AI Chat Playbook Builder - Implementation Plan

> **Project**: AI Chat Playbook Builder
> **Created**: 2026-01-16
> **Total Effort**: 6-7 weeks

---

## Executive Summary

This plan details the implementation of conversational AI assistance for the PlaybookBuilderHost PCF control. The project is divided into 6 phases covering infrastructure, UI components, AI integration, test execution, scope management, and polish.

---

## Phase Breakdown

### Phase 1: Infrastructure (1.5 weeks)

**Objective**: Establish BFF API endpoint, Dataverse operations, and conversational execution mode.

| Task | Description | Effort | Dependencies |
|------|-------------|--------|--------------|
| 1.1 | Create `AiPlaybookBuilderService` class | 1d | - |
| 1.2 | Add `/api/ai/build-playbook-canvas` SSE endpoint | 1d | 1.1 |
| 1.3 | Define canvas patch schema (add/remove/update nodes/edges) | 0.5d | - |
| 1.4 | Implement Dataverse scope CRUD operations | 1.5d | - |
| 1.5 | Implement N:N link table operations | 1d | 1.4 |
| 1.6 | Extend `PlaybookExecutionEngine` with conversational mode | 1.5d | - |
| 1.7 | Add `ConversationContext` and `CanvasState` models | 0.5d | 1.6 |
| 1.8 | Implement incremental canvas update streaming | 1d | 1.2, 1.6 |
| 1.9 | Add endpoint authorization filter | 0.5d | 1.2 |
| 1.10 | Add rate limiting middleware | 0.5d | 1.2 |

**Deliverables**:
- `AiPlaybookBuilderEndpoints.cs`
- `AiPlaybookBuilderService.cs`
- `CanvasPatch.cs` models
- Conversational execution mode in engine

---

### Phase 2: PCF Components (1 week)

**Objective**: Build the AI assistant modal UI with chat history and streaming support.

| Task | Description | Effort | Dependencies |
|------|-------------|--------|--------------|
| 2.1 | Create `aiAssistantStore` (Zustand) | 0.5d | - |
| 2.2 | Build `AiAssistantModal` container component | 1d | 2.1 |
| 2.3 | Build `ChatHistory` component | 1d | 2.1 |
| 2.4 | Build `ChatInput` component | 0.5d | 2.1 |
| 2.5 | Build `OperationFeedback` component | 0.5d | 2.1 |
| 2.6 | Create `AiPlaybookService` API client | 0.5d | Phase 1 |
| 2.7 | Wire SSE streaming to store | 1d | 2.1, 2.6 |
| 2.8 | Add toolbar button to toggle modal | 0.5d | 2.2 |
| 2.9 | Apply canvas patches from stream | 0.5d | 2.7 |

**Deliverables**:
- `aiAssistantStore.ts`
- `AiAssistant/` component folder
- `AiPlaybookService.ts`
- Toolbar integration

---

### Phase 3: AI Integration + Builder Scopes (1.5 weeks)

**Objective**: Implement intent classification, build plan generation, and builder-specific scopes.

| Task | Description | Effort | Dependencies |
|------|-------------|--------|--------------|
| 3.1 | Design system prompt for canvas building | 1d | - |
| 3.2 | Implement intent classification (11 categories) | 1d | 3.1 |
| 3.3 | Implement entity resolution with confidence | 1d | 3.2 |
| 3.4 | Implement clarification loop for ambiguous input | 0.5d | 3.2 |
| 3.5 | Implement build plan generation | 1d | 3.2 |
| 3.6 | Create ACT-BUILDER-001 through ACT-BUILDER-005 | 0.5d | - |
| 3.7 | Create SKL-BUILDER-001 through SKL-BUILDER-005 | 0.5d | - |
| 3.8 | Create TL-BUILDER-001 through TL-BUILDER-009 definitions | 0.5d | - |
| 3.9 | Create KNW-BUILDER-001 through KNW-BUILDER-004 content | 0.5d | - |
| 3.10 | Implement `ModelSelector` for tiered selection | 0.5d | - |
| 3.11 | Define PB-BUILDER meta-playbook in Dataverse | 0.5d | 3.6-3.9 |
| 3.12 | Implement scope search with semantic matching | 1d | 3.6-3.9 |
| 3.13 | End-to-end test with lease analysis scenario | 0.5d | All |

**Deliverables**:
- Intent classification system
- Builder scope records in Dataverse
- PB-BUILDER playbook definition
- ModelSelector service

---

### Phase 4: Test Execution (1 week)

**Objective**: Implement three test execution modes for playbook validation.

| Task | Description | Effort | Dependencies |
|------|-------------|--------|--------------|
| 4.1 | Add `/api/ai/test-playbook-execution` endpoint | 0.5d | Phase 1 |
| 4.2 | Implement mock test with sample data generation | 1d | 4.1 |
| 4.3 | Implement temp blob storage service (24hr TTL) | 1d | - |
| 4.4 | Implement quick test with temp blob | 1d | 4.1, 4.3 |
| 4.5 | Integrate Document Intelligence for quick test | 0.5d | 4.4 |
| 4.6 | Implement production test (full flow) | 0.5d | 4.1 |
| 4.7 | Build test options dialog in PCF | 0.5d | Phase 2 |
| 4.8 | Build test execution progress view | 0.5d | 4.7 |
| 4.9 | Add test result preview/download | 0.5d | 4.8 |

**Deliverables**:
- Test execution endpoint
- Three test modes operational
- Temp blob storage with TTL
- PCF test UI components

---

### Phase 5: Scope Management (1 week)

**Objective**: Implement ownership model and scope customization features.

| Task | Description | Effort | Dependencies |
|------|-------------|--------|--------------|
| 5.1 | Add ownership fields to Dataverse schema | 0.5d | - |
| 5.2 | Implement ownership validation (SYS- immutable) | 0.5d | 5.1 |
| 5.3 | Implement "Save As" for playbooks | 0.5d | 5.2 |
| 5.4 | Implement "Save As" for scopes | 0.5d | 5.2 |
| 5.5 | Implement "Extend" with inheritance | 1d | 5.2 |
| 5.6 | Implement duplicate name handling (suffix) | 0.5d | 5.4 |
| 5.7 | Build Scope Browser component | 1d | Phase 2 |
| 5.8 | Add scope creation dialogs | 0.5d | 5.7 |
| 5.9 | Add `GenericAnalysisHandler` for configurable tools | 1d | - |
| 5.10 | Implement proactive scope gap detection | 0.5d | 5.7 |

**Deliverables**:
- Ownership fields in Dataverse
- Save As / Extend functionality
- Scope Browser UI
- GenericAnalysisHandler

---

### Phase 6: Polish (0.5-1 week)

**Objective**: Error handling, UX polish, and documentation.

| Task | Description | Effort | Dependencies |
|------|-------------|--------|--------------|
| 6.1 | Implement comprehensive error handling | 0.5d | All phases |
| 6.2 | Add retry logic with backoff | 0.5d | 6.1 |
| 6.3 | Add loading states and animations | 0.5d | Phase 2 |
| 6.4 | Implement keyboard shortcuts (Cmd/Ctrl+K) | 0.5d | Phase 2 |
| 6.5 | Responsive modal sizing | 0.5d | Phase 2 |
| 6.6 | Dark mode verification | 0.5d | Phase 2 |
| 6.7 | Write user documentation | 0.5d | - |
| 6.8 | Code review and cleanup | 0.5d | All phases |
| 6.9 | Final integration testing | 0.5d | All phases |

**Deliverables**:
- Polished UX
- Documentation
- Clean codebase

---

## Architecture Context

### Discovered Resources

**Applicable ADRs**:
- ADR-001: Minimal API patterns
- ADR-006: PCF over webresources
- ADR-008: Endpoint filters for authorization
- ADR-009: Redis caching
- ADR-010: DI minimalism
- ADR-012: Shared component library
- ADR-013: AI architecture (extend BFF)
- ADR-016: Rate limiting
- ADR-019: ProblemDetails for errors
- ADR-021: Fluent UI v9, dark mode
- ADR-022: PCF platform libraries (React 16)

**Relevant Patterns**:
- `.claude/patterns/ai/streaming-endpoints.md` - SSE streaming pattern
- `.claude/patterns/ai/analysis-scopes.md` - Scope definitions

**Existing Code to Reference**:
- `src/client/pcf/PlaybookBuilderHost/` - Existing canvas, stores, components
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` - Streaming pattern

---

## Risk Analysis

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Intent classification accuracy | Medium | High | Clarification loops, confidence thresholds |
| Token cost overruns | Medium | Medium | Tiered model selection, rate limits |
| SSE connection stability | Low | Medium | Reconnection logic, error handling |
| Scope schema migration | Low | High | Careful Dataverse schema changes |

---

## Dependencies

### Prerequisites
- PlaybookBuilderHost PCF exists and functional
- Playbook execution engine operational
- Dataverse scope entities deployed
- Azure OpenAI access configured

### External
- Azure OpenAI (GPT-4o, GPT-4o-mini, o1-mini)
- Azure Blob Storage
- Dataverse API

---

## Success Metrics

| Metric | Target |
|--------|--------|
| Intent classification accuracy | >90% |
| Canvas update latency | <500ms |
| Bundle size | <5MB |
| Test execution success rate | >95% |

---

## References

- [spec.md](spec.md) - Full specification
- [ai-chat-playbook-builder.md](ai-chat-playbook-builder.md) - Design document
- [AI Playbook Architecture](../../docs/architecture/AI-PLAYBOOK-ARCHITECTURE.md)

---

*Generated by project-pipeline on 2026-01-16*
