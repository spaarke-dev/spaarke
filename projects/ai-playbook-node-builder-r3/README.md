# AI Playbook Assistant Completion (ai-playbook-node-builder-r3)

> **Status**: In Progress
> **Type**: Completion Project
> **Created**: 2026-01-19
> **Branch**: work/ai-playbook-node-builder-r3

---

## Overview

Complete the AI Assistant implementation in the Playbook Builder to enable users to build complete playbooks through natural language conversation. This is a **completion project** that extends existing scaffolded services rather than creating new components.

**Key Deliverable**: Users can describe a playbook in natural language, and the AI Assistant builds it - including creating/selecting scopes and constructing workflows of all types (AI-assisted, rule-based, hybrid).

## Project Goals

1. **AI-Powered Intent Classification** - Replace rule-based parsing with Azure OpenAI structured output
2. **Scope CRUD Operations** - Full Dataverse integration for Actions, Skills, Knowledge, Tools
3. **Scope Search & Suggestion** - Semantic matching to find/suggest existing scopes
4. **Builder Scopes** - Deploy 23 ACT/SKL/TL/KNW-BUILDER-* records
5. **Test Execution** - Wire mock/quick/production modes to PlaybookOrchestrationService
6. **Frontend Enhancements** - Scope browser, save as dialog, test mode selection

## Graduation Criteria

This project is complete when:

- [ ] User can describe complex playbook and AI builds it (end-to-end test)
- [ ] AI creates required scopes in Dataverse with correct ownership
- [ ] AI suggests existing scopes (reuse rate > 50%)
- [ ] Test modes execute correctly (mock, quick, production)
- [ ] Rule-based workflows supported (condition nodes only)
- [ ] Hybrid workflows supported (AI + conditional + tasks)
- [ ] SYS- scopes are immutable (edit rejected)
- [ ] Save As creates copy with lineage tracking

## Architecture

### Extends (NOT Creates)

| Service | Current State | Completion Work |
|---------|---------------|-----------------|
| `AiPlaybookBuilderService` | Rule-based intent | AI-powered classification |
| `ScopeResolverService` | Stub data | Full Dataverse CRUD |
| `IScopeResolverService` | Read-only | Add CRUD interface |

### Key Components

```
Backend (Extend)
├── AiPlaybookBuilderService.cs  → AI intent classification
├── ScopeResolverService.cs      → Dataverse CRUD, search
└── IScopeResolverService.cs     → New interface methods

Frontend (Enhance)
├── components/ScopeBrowser/     → NEW: Scope selection UI
├── components/SaveAsDialog/     → NEW: Save As workflow
├── components/TestModeSelector/ → NEW: Test mode options
└── stores/aiAssistantStore.ts   → Model selection support

Dataverse (Deploy)
├── Builder scopes solution      → 23 scope records
└── Schema updates               → Ownership fields
```

## Implementation Phases

| Phase | Focus | Duration |
|-------|-------|----------|
| **1** | Scope Management Backend | 1 week |
| **2** | AI Intent Enhancement | 1 week |
| **3** | Builder Scopes & Meta-Playbook | 0.5 week |
| **4** | Test Execution Integration | 1 week |
| **5** | Frontend Enhancements | 1 week |
| **6** | Polish | 0.5 week |

**Total**: ~5 weeks

## Technical Constraints

### MUST Rules

- ✅ Extend existing services, not create new
- ✅ Use existing `IOpenAiClient` for AI operations
- ✅ Use existing `PlaybookOrchestrationService` for test execution
- ✅ Use unmanaged solutions for Dataverse
- ✅ Use `Spaarke` publisher with `sprk_` prefix
- ✅ Support all workflow types (AI, rule-based, hybrid)

### Applicable ADRs

- **ADR-001**: Minimal API pattern
- **ADR-006**: PCF over webresources
- **ADR-008**: Endpoint filters for auth
- **ADR-010**: DI minimalism
- **ADR-013**: AI Tool Framework
- **ADR-021**: Fluent UI v9
- **ADR-022**: PCF Platform Libraries (React 16, unmanaged)

## Quick Links

- [Implementation Plan](plan.md)
- [AI Context](CLAUDE.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Original Spec](spec.md)
- [Design Document](design.md)
- [Comprehensive Design](../ai-playbook-node-builder-r2/ai-chat-playbook-builder.md)

## Related Code

| Component | Path |
|-----------|------|
| AI Builder Service | `src/server/api/Sprk.Bff.Api/Services/Ai/AiPlaybookBuilderService.cs` |
| Scope Resolver | `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` |
| AI Assistant Store | `src/client/pcf/PlaybookBuilderHost/control/stores/aiAssistantStore.ts` |
| Canvas Store | `src/client/pcf/PlaybookBuilderHost/control/stores/canvasStore.ts` |
| PCF Control | `src/client/pcf/PlaybookBuilderHost/` |

---

*Project initialized: 2026-01-19*
