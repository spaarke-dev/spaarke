# AI Playbook Assistant Completion - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-19
> **Source**: design.md (Completion Project)
> **Project Type**: Completion - Building on existing foundation

---

## Executive Summary

Complete the AI Assistant implementation in the Playbook Builder to enable users to build complete playbooks through natural language conversation. This is a **completion project** that extends existing scaffolded services (`AiPlaybookBuilderService`, `ScopeResolverService`) rather than creating new components. The assistant will support creating/reusing scopes and constructing complex workflows of all types: AI-assisted, rule-based, and hybrid.

**Key Principle**: Playbooks are the universal workflow composition tool for ALL Spaarke automation - not just AI features.

---

## Scope

### In Scope

| Capability | Description |
|------------|-------------|
| AI-powered intent classification | Replace rule-based `ParseIntent()` with Azure OpenAI structured output |
| Scope CRUD operations | Full Dataverse integration for Actions, Skills, Knowledge, Tools |
| Scope search and suggestion | Semantic matching to find/suggest existing scopes |
| Builder-specific scope creation | Create ACT/SKL/TL/KNW-BUILDER-* records via solution import |
| Scope ownership model | SYS-/CUST- prefix with `ownertype`, `isimmutable` fields |
| Save As / Extend functionality | Copy with lineage tracking (`basedon`, `parentscope` fields) |
| Test execution (3 modes) | Mock, quick, production modes wired to `PlaybookOrchestrationService` |
| Scope browser UI | Selection and configuration UI in PCF |
| Simple model selection | Support for different AI models (GPT-4o, GPT-4o-mini) |

### Out of Scope

| Capability | Reason |
|------------|--------|
| Custom tool code generation | Requires code execution infrastructure |
| RAG index creation | Requires Azure AI Search provisioning |
| Playbook execution engine | Already implemented separately |
| M365 Copilot integration | Deferred per design decision |
| Complex tiered model orchestration | MVP uses simple model selection |

### Limited Scope

| Capability | What's Included | What's Excluded |
|------------|-----------------|-----------------|
| Tool Scopes | Create records with configuration | Custom code handlers |
| Knowledge Sources | Create records linking existing indexes | New index creation |

### Affected Areas

| Path | Description |
|------|-------------|
| `src/server/api/Sprk.Bff.Api/Services/Ai/AiPlaybookBuilderService.cs` | Extend with AI intent classification, scope operations |
| `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` | Extend with CRUD operations, Dataverse integration, search |
| `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` | Add CRUD interface methods |
| `src/client/pcf/PlaybookBuilderHost/` | Add scope browser, save as dialog, test mode UI |
| `infrastructure/dataverse/solutions/` | Builder scopes solution, schema updates |
| Azure Blob Storage | New dedicated container for playbook test documents |

---

## Requirements

### Functional Requirements

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| **FR-01** | AI-powered intent classification | User messages parsed via Azure OpenAI with structured output; confidence thresholds trigger clarification |
| **FR-02** | Scope CRUD - Create | New Actions, Skills, Knowledge, Tools created in Dataverse with correct ownership prefix |
| **FR-03** | Scope CRUD - Read | Scopes loaded from Dataverse by ID or search query |
| **FR-04** | Scope CRUD - Update | Customer-owned scopes (CUST-) can be modified; system scopes (SYS-) reject updates |
| **FR-05** | Scope CRUD - Delete | Customer-owned scopes can be deleted; system scopes protected |
| **FR-06** | Scope search | Semantic search returns relevant scopes ranked by match quality |
| **FR-07** | Scope suggestion | AI suggests existing scopes before creating new; reuse rate > 50% |
| **FR-08** | Save As functionality | Copy scope with `basedon` reference preserved in Dataverse |
| **FR-09** | Extend functionality | Create scope with `parentscope` reference (inherits updates) |
| **FR-10** | Test mode - Mock | Execute playbook with sample data, no external calls |
| **FR-11** | Test mode - Quick | Execute with temp storage in dedicated blob container |
| **FR-12** | Test mode - Production | Execute against real data with full observability |
| **FR-13** | Scope browser UI | PCF component for browsing, selecting, configuring scopes |
| **FR-14** | Save As dialog | PCF dialog for saving scope copies with naming |
| **FR-15** | Test mode selection | PCF UI for selecting test mode before execution |
| **FR-16** | Model selection | Support selecting AI model (GPT-4o, GPT-4o-mini) for operations |
| **FR-17** | Hybrid workflow support | AI Assistant can build playbooks mixing AI nodes and rule-based nodes |

### Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| **NFR-01** | Intent classification latency < 2s for typical messages |
| **NFR-02** | Scope search returns results < 1s |
| **NFR-03** | All Dataverse operations use existing `IDataverseService` patterns |
| **NFR-04** | PCF components follow Fluent UI v9 design system |
| **NFR-05** | No new DI registrations beyond extending existing services |
| **NFR-06** | Test mode cleanup: temp documents deleted after 24 hours |

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| **ADR-001** | Minimal API pattern for new endpoints |
| **ADR-002** | No Dataverse plugin involvement - BFF handles all logic |
| **ADR-006** | PCF controls only - no legacy webresources |
| **ADR-008** | Endpoint filters for authorization |
| **ADR-010** | DI minimalism - extend existing registrations |
| **ADR-013** | AI Tool Framework - use existing `IOpenAiClient` |
| **ADR-014** | Streaming patterns for AI responses |
| **ADR-021** | Fluent UI v9 for all new UI components |
| **ADR-022** | React 16 APIs only; unmanaged solutions |

### MUST Rules

- ✅ MUST extend `IScopeResolverService` interface, not create new service
- ✅ MUST extend `AiPlaybookBuilderService`, not create new builder service
- ✅ MUST use existing `IOpenAiClient` for AI operations
- ✅ MUST use existing `PlaybookOrchestrationService` for test execution
- ✅ MUST use unmanaged solutions for Dataverse deployments
- ✅ MUST use `Spaarke` publisher with `sprk_` prefix
- ✅ MUST support all workflow types (AI, rule-based, hybrid)
- ❌ MUST NOT create duplicate services or redundant abstractions
- ❌ MUST NOT add new DI registrations (extend existing)
- ❌ MUST NOT use managed solutions

### Existing Patterns to Follow

| Pattern | Reference |
|---------|-----------|
| Scope resolution | `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` |
| AI streaming | `src/server/api/Sprk.Bff.Api/Endpoints/AiPlaybookBuilderEndpoints.cs` |
| Dataverse operations | `Spaarke.Dataverse.IDataverseService` |
| PCF stores | `src/client/pcf/PlaybookBuilderHost/control/stores/` |
| Canvas operations | `src/client/pcf/PlaybookBuilderHost/control/stores/canvasStore.ts` |

---

## Success Criteria

| # | Criterion | Verification Method |
|---|-----------|---------------------|
| 1 | User can describe complex playbook and AI builds it | End-to-end test: describe lease analysis playbook, verify canvas populated |
| 2 | AI creates required scopes in Dataverse | Query Dataverse after build, verify records with correct ownership |
| 3 | AI suggests existing scopes when appropriate | Test reuse rate > 50% across 10 sample requests |
| 4 | Test modes execute correctly | Run mock, quick, production modes; verify appropriate behavior |
| 5 | Rule-based workflows supported | Build approval routing playbook using condition nodes only |
| 6 | Hybrid workflows supported | Build AI classification → conditional routing → task creation playbook |
| 7 | SYS- scopes are immutable | Attempt edit of SYS- scope, verify rejection with appropriate error |
| 8 | Save As creates copy with lineage | Save As on scope, verify new record with `basedon` reference |

---

## Dependencies

### Prerequisites (Already Available)

| Component | Location | Status |
|-----------|----------|--------|
| PlaybookBuilderHost PCF | `src/client/pcf/PlaybookBuilderHost/` | ✅ Complete |
| AI Assistant panel UI | `control/components/AiAssistant/` | ✅ Complete |
| aiAssistantStore | `control/stores/aiAssistantStore.ts` | ✅ Complete |
| canvasStore | `control/stores/canvasStore.ts` | ✅ Complete |
| AiPlaybookBuilderService scaffold | `Services/Ai/AiPlaybookBuilderService.cs` | ⚠️ Scaffold |
| ScopeResolverService scaffold | `Services/Ai/ScopeResolverService.cs` | ⚠️ Scaffold |
| SSE streaming infrastructure | `Endpoints/AiPlaybookBuilderEndpoints.cs` | ✅ Complete |
| Azure OpenAI deployment | Production | ✅ Available |
| PlaybookOrchestrationService | `Services/Ai/PlaybookOrchestrationService.cs` | ✅ Complete |

### External Dependencies

| Dependency | Purpose | Action Required |
|------------|---------|-----------------|
| Dataverse schema update | Add ownership fields to scope entities | Solution import (unmanaged) |
| Builder scopes data | 23 ACT/SKL/TL/KNW-BUILDER-* records | Solution import (unmanaged) |
| Azure Blob container | Dedicated playbook test documents storage | Create `playbook-test-documents` container |

---

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Dataverse schema | Can ownership fields be added via solution import? | Yes, use unmanaged solution | Phase 1 uses simple field addition |
| Builder scopes | How to deploy 23 builder scope records? | Solution import (unmanaged) | Scopes deployed as Dataverse records in solution |
| Test storage | Use existing container or dedicated? | Create dedicated blob storage for playbooks | New `playbook-test-documents` container |
| Prefix enforcement | Backend-only or UI guidance? | Backend validation only | No UI prefix selector needed |
| Meta-playbook context | Same session or spawn separate? | Same session context | Builder service directly invokes its own scopes |
| Model selection | Complex tiered or simple selection? | Simple model selection for MVP | Add model parameter to AI operations; avoid complex orchestration |

---

## Assumptions

*Proceeding with these assumptions:*

| Topic | Assumption | Affects |
|-------|------------|---------|
| Existing IDataverseService | Has or can be extended with scope entity operations | Phase 1 implementation |
| OpenAI structured output | Azure OpenAI deployment supports structured/JSON output | Intent classification design |
| Blob storage access | BFF has connection string for new container | Test mode implementation |

---

## Unresolved Questions

*None - all blocking questions resolved during clarification.*

---

## Builder Scopes Reference

23 builder-specific scopes to be created (deployed via unmanaged solution):

### Actions (ACT-BUILDER-*)

| ID | Name | Purpose |
|----|------|---------|
| ACT-BUILDER-001 | Intent Classification | Parse user message into operation |
| ACT-BUILDER-002 | Node Configuration | Generate node config from requirements |
| ACT-BUILDER-003 | Scope Selection | Select appropriate existing scope |
| ACT-BUILDER-004 | Scope Creation | Generate new scope definition |
| ACT-BUILDER-005 | Build Plan Generation | Create structured plan |

### Skills (SKL-BUILDER-*)

| ID | Name | Purpose |
|----|------|---------|
| SKL-BUILDER-001 | Lease Analysis Pattern | How to build lease playbooks |
| SKL-BUILDER-002 | Contract Review Pattern | Contract playbook patterns |
| SKL-BUILDER-003 | Risk Assessment Pattern | Risk workflow patterns |
| SKL-BUILDER-004 | Node Type Guide | When to use each node |
| SKL-BUILDER-005 | Scope Matching | Find/create appropriate scopes |

### Tools (TL-BUILDER-*)

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

### Knowledge (KNW-BUILDER-*)

| ID | Name | Content |
|----|------|---------|
| KNW-BUILDER-001 | Scope Catalog | Available system scopes |
| KNW-BUILDER-002 | Reference Playbooks | Example patterns |
| KNW-BUILDER-003 | Node Schema | Valid configurations |
| KNW-BUILDER-004 | Best Practices | Design guidelines |

---

## Implementation Phases

| Phase | Focus | Duration |
|-------|-------|----------|
| **Phase 1** | Scope Management Backend | 1 week |
| **Phase 2** | AI Intent Enhancement | 1 week |
| **Phase 3** | Builder Scopes & Meta-Playbook | 0.5 week |
| **Phase 4** | Test Execution Integration | 1 week |
| **Phase 5** | Frontend Enhancements | 1 week |
| **Phase 6** | Polish | 0.5 week |

**Total: ~5 weeks**

---

## References

| Document | Purpose |
|----------|---------|
| [ai-chat-playbook-builder.md](../ai-playbook-node-builder-r2/ai-chat-playbook-builder.md) | Primary comprehensive design |
| [spec.md](../ai-playbook-node-builder-r2/spec.md) | Original specification |
| [AiPlaybookBuilderService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/AiPlaybookBuilderService.cs) | Existing service to extend |
| [ScopeResolverService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs) | Existing scope resolver to extend |
| [aiAssistantStore.ts](../../src/client/pcf/PlaybookBuilderHost/control/stores/aiAssistantStore.ts) | Frontend AI assistant store |

---

*AI-optimized specification. Original design: design.md (Completion Project)*
