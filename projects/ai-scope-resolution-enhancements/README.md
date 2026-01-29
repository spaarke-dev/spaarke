# AI Scope Resolution Enhancements

> **Status**: In Progress
> **Branch**: feature/ai-scope-resolution-enhancements
> **Created**: 2026-01-29
> **Estimated Duration**: 15-20 business days

---

## Executive Summary

Update the scope resolution architecture across all scope types (Tools, Skills, Knowledge, Actions) to eliminate stub dictionary anti-patterns and fix the "No handler registered for job type" error. This project enables configuration-driven extensibility, runtime handler discovery, and ensures all scopes are loaded dynamically from Dataverse without code deployment.

**Business Value**: Users can create new AI playbook scopes in Dataverse and they work immediately without waiting for backend deployments. Eliminates dead-letter queue errors blocking document analysis.

---

## Quick Links

| Resource | Path |
|----------|------|
| Implementation Plan | [plan.md](plan.md) |
| AI Context | [CLAUDE.md](CLAUDE.md) |
| Specification | [spec.md](spec.md) |
| Task Index | [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) |
| Current Task | [current-task.md](current-task.md) |

---

## Problem Statement

### Root Cause
[ScopeResolverService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs) contains hardcoded stub dictionaries with fake GUIDs, while [PlaybookService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookService.cs) loads **real** GUIDs from Dataverse N:N relationships.

**Mismatch** → ScopeResolverService returns null → Tools/Skills/Knowledge/Actions not found → Analysis fails

### Current Error (Dead-Letter Queue)
```
DeadLetterReason: NoHandler
DeadLetterErrorDescription: No handler registered for job type
JobType: AppOnlyDocumentAnalysis
```

---

## Solution Approach

### Three-Tier Scope Resolution Architecture

```
┌────────────────────────────────────────────────────────────────┐
│  Tier 1: Configuration (Dataverse - Source of Truth)           │
│  - sprk_analysistool, sprk_promptfragment, sprk_systemprompt,  │
│    sprk_content                                                 │
│  - Must work without code deployment (new records auto-work)   │
│  - HandlerClass NULL → Defaults to generic handler              │
└────────────────────────────────────────────────────────────────┘
                              ↓
┌────────────────────────────────────────────────────────────────┐
│  Tier 2: Generic Execution (Handles 95% of Cases)              │
│  - GenericAnalysisHandler - Configuration-driven               │
│  - No arbitrary code execution (security safe)                  │
└────────────────────────────────────────────────────────────────┘
                              ↓
┌────────────────────────────────────────────────────────────────┐
│  Tier 3: Custom Handlers (Complex Scenarios Only)              │
│  - EntityExtractorHandler, SummaryHandler, etc.                │
│  - Registered in DI at startup                                  │
└────────────────────────────────────────────────────────────────┘
```

---

## Implementation Phases

| Phase | Description | Status | Duration |
|-------|-------------|--------|----------|
| **Phase 0** | Fix Job Handler Registration (Critical) | Pending | 1 day |
| **Phase 1** | Complete Tool Resolution | Pending | 1 day |
| **Phase 2** | Implement Skill Resolution | Pending | 2-3 days |
| **Phase 3** | Implement Knowledge Resolution | Pending | 2-3 days |
| **Phase 4** | Implement Action Resolution | Pending | 2-3 days |
| **Phase 5** | Remove Stub Dictionaries | Pending | 1 day |
| **Phase 6** | Handler Discovery API | Pending | 2-3 days |
| **Phase 7** | Testing & Validation | Pending | 3-4 days |
| **Phase 8** | Deployment & Monitoring | Pending | 2 days |

**Note**: Phases 2-4 can execute in parallel after Phase 1 completes.

---

## Graduation Criteria

### Functional Completeness
- [ ] Job handler registration fixed (NoHandler error resolved)
- [ ] All scopes (Tools, Skills, Knowledge, Actions) loaded from Dataverse
- [ ] No stub dictionaries remain in codebase
- [ ] GenericAnalysisHandler executes custom tools successfully
- [ ] Handler discovery API returns all registered handlers with ConfigurationSchema
- [ ] All handlers updated with JSON Schema

### Performance Targets
- [ ] Scope resolution latency < 200ms (p95)
- [ ] GET /api/ai/handlers response < 100ms (cached)
- [ ] Analysis success rate > 98%
- [ ] No performance regression vs. stub dictionaries

### Reliability Targets
- [ ] Dead-letter queue errors < 1/day (currently ~5-10/hour)
- [ ] Scope resolution failure rate < 2%
- [ ] Handler not found warnings < 10/hour
- [ ] Zero breaking changes to existing analyses

### User Testing
- [ ] File upload using UniversalDocumentUpload
- [ ] Email-to-document automation
- [ ] Outlook add-in document save
- [ ] Word add-in document save

---

## Key Files

### Modified Files
| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` | Replace stubs with Dataverse queries |
| `src/server/api/Sprk.Bff.Api/Program.cs` | Job handler DI registration |
| `src/server/api/Sprk.Bff.Api/Endpoints/AiEndpoints.cs` | Handler discovery API |
| `src/server/api/Sprk.Bff.Api/Services/Ai/ToolHandlerMetadata.cs` | ConfigurationSchema property |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/*.cs` | Handler schemas |

### Reference Files
| File | Purpose |
|------|---------|
| `.claude/patterns/ai/analysis-scopes.md` | Scope system pattern |
| `.claude/patterns/api/background-workers.md` | Job handler pattern |
| `.claude/patterns/dataverse/web-api-client.md` | Dataverse query pattern |

---

## Applicable ADRs

| ADR | Title | Key Constraint |
|-----|-------|----------------|
| ADR-001 | Minimal API + BackgroundService | No Azure Functions |
| ADR-004 | Async Job Contract | Idempotent handlers |
| ADR-010 | DI Minimalism | ≤15 non-framework DI lines |
| ADR-013 | AI Architecture | Extend BFF, not separate service |
| ADR-014 | AI Caching | Redis for AI results |
| ADR-017 | Job Status | Persist status transitions |

---

## Team

- **Developer**: Claude Code AI Assistant
- **Owner**: User

---

*Generated by project-pipeline skill on 2026-01-29*
