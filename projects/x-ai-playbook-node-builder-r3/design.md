# AI Playbook Assistant Completion - Design Document

> **Project**: ai-playbook-assistant-r1
> **Author**: Claude Code
> **Date**: January 19, 2026
> **Status**: Draft - Pending Review
> **Type**: Completion Project (building on existing foundation)

---

## 1. Executive Summary

Complete the AI Assistant implementation in the Playbook Builder based on the existing comprehensive design ([ai-chat-playbook-builder.md](../ai-playbook-node-builder-r2/ai-chat-playbook-builder.md)). The assistant will enable users to build complete playbooks through natural language conversation, including creating/reusing scopes and constructing complex workflows (AI-assisted, rule-based, or hybrid).

### Alignment with Broader Vision

**Playbooks are the universal workflow composition tool** for ALL Spaarke automation:
- **AI-Assisted Workflows**: Document analysis, content generation, classification
- **Rule-Based Workflows**: Condition nodes, deterministic routing, data transformation
- **Hybrid Workflows**: Mix of AI nodes and rule-based nodes in same playbook

The AI Assistant accelerates playbook creation but the underlying playbook infrastructure supports all workflow types.

---

## 2. Existing Foundation (What's Already Built)

### 2.1 Comprehensive Design Document

**Source**: [ai-chat-playbook-builder.md](../ai-playbook-node-builder-r2/ai-chat-playbook-builder.md)

This 1200+ line design document covers:
- Unified AI Agent Framework (PB-BUILDER meta-playbook concept)
- Builder-specific scopes (ACT-BUILDER-*, SKL-BUILDER-*, TL-BUILDER-*, KNW-BUILDER-*)
- Conversational UX with intent classification
- Test execution architecture (mock, quick, production modes)
- Scope management (ownership model, Save As, Extend)
- Tiered AI model selection

### 2.2 Implemented Components

| Component | File | Status |
|-----------|------|--------|
| **AI Assistant Frontend** | `stores/aiAssistantStore.ts` | ✅ Complete - Chat, streaming, canvas patches |
| **AI Assistant Panel UI** | `components/AiAssistant/` | ✅ Complete - Modal, chat history, input |
| **Builder Service** | `AiPlaybookBuilderService.cs` | ⚠️ Scaffold - Rule-based intent, stub AI calls |
| **Scope Resolver** | `ScopeResolverService.cs` | ⚠️ Scaffold - Stub data, partial Dataverse |
| **Canvas Store** | `stores/canvasStore.ts` | ✅ Complete - Nodes, edges, patches |
| **Node Types** | `components/nodes/` | ✅ Complete - All 7 node types |
| **Properties Panel** | `components/PropertiesPanel/` | ✅ Complete - Node configuration |
| **SSE Streaming** | `AiPlaybookBuilderEndpoints.cs` | ✅ Complete - Stream infrastructure |

### 2.3 Existing BFF AI Services (DO NOT DUPLICATE)

| Service | Purpose | Reuse Strategy |
|---------|---------|----------------|
| `PlaybookOrchestrationService` | Execute playbooks | Use for test execution |
| `OpenAiClient` | Azure OpenAI calls | Use for AI classification |
| `ScopeResolverService` | Load scopes | Extend for CRUD operations |
| `NodeExecutorRegistry` | Node execution | Use for playbook testing |
| `TemplateEngine` | Prompt rendering | Use for builder prompts |

---

## 3. Gap Analysis (What Needs Completion)

### 3.1 Backend Gaps

| Gap | Designed In | Current State | Completion Work |
|-----|-------------|---------------|-----------------|
| **AI-Powered Intent Classification** | Spec FR-01 | Rule-based `ParseIntent()` | Replace with Azure OpenAI structured output |
| **Scope CRUD Operations** | Spec FR-03 | Stub data only | Implement Dataverse operations |
| **Scope Search** | Design Section 4 | Not implemented | Add semantic search with `ScopeResolverService` |
| **Builder-Specific Scopes** | Design Section 11 | Not created | Create ACT/SKL/TL/KNW-BUILDER-* records |
| **PB-BUILDER Meta-Playbook** | Design Section 11 | Not created | Create playbook definition |
| **Test Execution Integration** | Design Section 8 | Skeleton only | Wire to `PlaybookOrchestrationService` |

### 3.2 Frontend Gaps

| Gap | Designed In | Current State | Completion Work |
|-----|-------------|---------------|-----------------|
| **Scope Browser** | Design Section 9 | Not implemented | Add scope selection UI |
| **Save As Dialog** | Spec FR-08 | Not implemented | Add save as workflow |
| **Test Mode Selection** | Design Section 8 | Not implemented | Add test options dialog |
| **Clarification UI** | Design Section 6 | Basic only | Enhance with options |

### 3.3 Data Gaps

| Gap | Current State | Completion Work |
|-----|---------------|-----------------|
| **Scope Ownership Fields** | Not in Dataverse schema | Add `sprk_ownertype`, `sprk_isimmutable` |
| **Builder Scopes** | Not seeded | Create SYS- builder scope records |
| **N:N Link Tables** | Exist but unused | Wire playbook-scope associations |

---

## 4. Scope

### 4.1 In Scope (Complete Existing Design)

| Capability | Design Reference | Notes |
|------------|------------------|-------|
| AI-powered intent classification | Spec FR-01, FR-05 | Replace rule-based parsing |
| Scope CRUD (Actions, Skills, Knowledge, Tools) | Spec FR-03 | Full Dataverse integration |
| Scope search and suggestion | Design Section 4 | Semantic matching |
| Builder-specific scope creation | Design Section 11 | ACT/SKL/TL/KNW-BUILDER-* |
| Scope ownership (SYS-/CUST-) | Spec FR-07, FR-08 | Immutable/editable model |
| Save As / Extend functionality | Spec FR-08 | Copy and inheritance |
| Test execution (3 modes) | Spec FR-06 | Mock, quick, production |
| Scope browser UI | Design Section 9 | Selection and configuration |

### 4.2 Out of Scope (Future Projects)

| Capability | Reason |
|------------|--------|
| Custom tool code generation | Requires code execution infrastructure |
| RAG index creation | Requires Azure AI Search provisioning |
| Playbook execution engine | Already implemented separately |
| M365 Copilot integration | Deferred per design decision |

### 4.3 Limited Scope

| Capability | What's Included | What's Excluded |
|------------|-----------------|-----------------|
| **Tool Scopes** | Create records with configuration | Custom code handlers |
| **Knowledge Sources** | Create records linking existing indexes | New index creation |

---

## 5. Workflow Support Matrix

Playbooks support all workflow types through existing node types:

| Workflow Type | Node Types Used | Example |
|---------------|-----------------|---------|
| **AI-Assisted** | aiAnalysis, aiCompletion | Document summarization, entity extraction |
| **Rule-Based** | condition, wait, createTask, sendEmail | Approval routing, notification triggers |
| **Hybrid** | Mix of above | AI classification → conditional routing → task creation |

The AI Assistant helps build ALL workflow types, not just AI workflows:

```
User: "Create a workflow that routes documents to different approvers based on amount"

AI Assistant understands this requires:
├── aiAnalysis node (extract amount from document)
├── condition node (amount > $10,000?)
│   ├── TRUE branch → createTask (senior approver)
│   └── FALSE branch → createTask (standard approver)
└── sendEmail node (notification)
```

---

## 6. Technical Approach

### 6.1 Extend Existing Services (NOT Create New)

| Need | Approach |
|------|----------|
| AI intent classification | Add to `AiPlaybookBuilderService` using `IOpenAiClient` |
| Scope CRUD | Extend `IScopeResolverService` interface |
| Scope search | Add `SearchScopesAsync` to `ScopeResolverService` |
| Test execution | Wire existing `PlaybookOrchestrationService` |

### 6.2 Builder Scopes (Seed Data)

Create builder-specific scope records in Dataverse:

**Actions (ACT-BUILDER-*)**
| ID | Name | Purpose |
|----|------|---------|
| ACT-BUILDER-001 | Intent Classification | Parse user message into operation |
| ACT-BUILDER-002 | Node Configuration | Generate node config from requirements |
| ACT-BUILDER-003 | Scope Selection | Select appropriate existing scope |
| ACT-BUILDER-004 | Scope Creation | Generate new scope definition |
| ACT-BUILDER-005 | Build Plan Generation | Create structured plan |

**Skills (SKL-BUILDER-*)**
| ID | Name | Purpose |
|----|------|---------|
| SKL-BUILDER-001 | Lease Analysis Pattern | How to build lease playbooks |
| SKL-BUILDER-002 | Contract Review Pattern | Contract playbook patterns |
| SKL-BUILDER-003 | Risk Assessment Pattern | Risk workflow patterns |
| SKL-BUILDER-004 | Node Type Guide | When to use each node |
| SKL-BUILDER-005 | Scope Matching | Find/create appropriate scopes |

**Tools (TL-BUILDER-*)**
| ID | Name | Operation |
|----|------|-----------|
| TL-BUILDER-001 | addNode | Add node to canvas |
| TL-BUILDER-002 | removeNode | Remove node |
| TL-BUILDER-003 | createEdge | Connect nodes |
| TL-BUILDER-004 | updateNodeConfig | Configure node |
| TL-BUILDER-005 | linkScope | Wire scope to node |
| TL-BUILDER-006 | createScope | Create new scope |
| TL-BUILDER-007 | searchScopes | Find existing scopes |
| TL-BUILDER-008 | autoLayout | Arrange canvas |
| TL-BUILDER-009 | validateCanvas | Validate playbook |

**Knowledge (KNW-BUILDER-*)**
| ID | Name | Content |
|----|------|---------|
| KNW-BUILDER-001 | Scope Catalog | Available system scopes |
| KNW-BUILDER-002 | Reference Playbooks | Example patterns |
| KNW-BUILDER-003 | Node Schema | Valid configurations |
| KNW-BUILDER-004 | Best Practices | Design guidelines |

### 6.3 Scope Ownership Implementation

Add fields to Dataverse scope entities:

```
sprk_analysisaction (and all scope entities)
├── sprk_ownertype (OptionSet: System=1, Customer=2)
├── sprk_parentscope (Lookup: self-reference for Extend)
├── sprk_basedon (Lookup: self-reference for Save As)
└── sprk_isimmutable (Boolean)
```

**Rules:**
- SYS- prefix → `ownertype=System`, `isimmutable=true`
- CUST- prefix → `ownertype=Customer`, `isimmutable=false`
- Save As → Copy with `basedon` reference
- Extend → Create with `parentscope` reference (inherits updates)

---

## 7. Implementation Phases

### Phase 1: Scope Management Backend (1 week)
- Extend `IScopeResolverService` with CRUD operations
- Add Dataverse integration for scope creation
- Implement scope search with semantic matching
- Add scope ownership fields to entities

### Phase 2: AI Intent Enhancement (1 week)
- Replace rule-based `ParseIntent()` with Azure OpenAI
- Implement structured output parsing
- Add confidence thresholds and clarification triggers
- Integrate tiered model selection

### Phase 3: Builder Scopes & Meta-Playbook (0.5 week)
- Create ACT-BUILDER-*, SKL-BUILDER-*, TL-BUILDER-*, KNW-BUILDER-* records
- Create PB-BUILDER meta-playbook definition
- Wire builder to use its own scopes

### Phase 4: Test Execution Integration (1 week)
- Wire test modes to `PlaybookOrchestrationService`
- Implement mock mode with sample data
- Implement quick mode with temp storage
- Add test options UI in PCF

### Phase 5: Frontend Enhancements (1 week)
- Add scope browser/selector component
- Implement Save As dialog
- Enhance clarification UI with options
- Add test mode selection

### Phase 6: Polish (0.5 week)
- Error handling and recovery
- Performance optimization
- Documentation updates

**Total: ~5 weeks**

---

## 8. Success Criteria

| Criteria | Verification |
|----------|--------------|
| User can describe complex playbook and AI builds it | End-to-end test with lease analysis |
| AI creates required scopes in Dataverse | Query records after build |
| AI suggests existing scopes when appropriate | Test reuse rate > 50% |
| Test modes execute correctly | Run mock, quick, production |
| Rule-based workflows supported | Build approval routing playbook |
| Hybrid workflows supported | Build AI+conditional playbook |
| SYS- scopes immutable | Attempt edit, verify rejection |
| Save As creates copy with lineage | Inspect Dataverse records |

---

## 9. Dependencies

### Prerequisites (Already Available)
- ✅ PlaybookBuilderHost PCF with AI Assistant panel
- ✅ `aiAssistantStore.ts` for chat and streaming
- ✅ `canvasStore.ts` for canvas operations
- ✅ `AiPlaybookBuilderService.cs` scaffold
- ✅ `ScopeResolverService.cs` scaffold
- ✅ SSE streaming infrastructure
- ✅ Azure OpenAI deployment

### External Dependencies
- Dataverse schema updates (ownership fields)
- Azure Blob Storage (for quick test mode)

---

## 10. Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Scope creation fails | Validation before Dataverse write |
| AI generates invalid canvas | Patch validation, rollback |
| Token limits for complex playbooks | Chunked context, summarization |
| Dataverse schema changes blocked | Separate solution for scope fields |

---

## 11. Open Questions

1. **Dataverse Schema Update Path**: Can scope ownership fields be added to existing entities, or need migration?

2. **Builder Scope Deployment**: Should builder scopes be seeded via solution import or API script?

3. **Test Temp Storage**: Use existing blob container or create dedicated `test-documents` container?

---

## 12. References

- **Primary Design**: [ai-chat-playbook-builder.md](../ai-playbook-node-builder-r2/ai-chat-playbook-builder.md)
- **Specification**: [spec.md](../ai-playbook-node-builder-r2/spec.md)
- **Existing Service**: [AiPlaybookBuilderService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/AiPlaybookBuilderService.cs)
- **Scope Resolver**: [ScopeResolverService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs)
- **AI Assistant Store**: [aiAssistantStore.ts](../../src/client/pcf/PlaybookBuilderHost/control/stores/aiAssistantStore.ts)
- **Example Playbook**: [PLAYBOOK-REAL-ESTATE-LEASE-ANALYSIS.md](../../docs/guides/PLAYBOOK-REAL-ESTATE-LEASE-ANALYSIS.md)

---

*End of Design Document - Completion Project*
