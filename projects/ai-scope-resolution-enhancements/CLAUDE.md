# CLAUDE.md - AI Scope Resolution Enhancements

> **Project**: ai-scope-resolution-enhancements
> **Created**: 2026-01-29
> **Status**: In Progress

---

## Project Overview

This project fixes scope resolution architecture across all scope types (Tools, Skills, Knowledge, Actions) to eliminate stub dictionary anti-patterns and resolve the "No handler registered for job type" dead-letter error.

**Key Goal**: Enable configuration-driven extensibility - users create scopes in Dataverse and they work immediately without code deployment.

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

**Why This Matters**:
- ✅ Knowledge files loaded (ADRs, constraints, patterns)
- ✅ Proactive checkpointing every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5
- ✅ Progress recoverable after compaction

**Trigger Phrases** → Auto-invoke task-execute:
- "work on task X"
- "continue"
- "next task"
- "resume task X"

---

## Applicable ADRs

**MUST load these ADRs before any implementation work:**

| ADR | Path | Key Constraint |
|-----|------|----------------|
| ADR-001 | `.claude/adr/ADR-001-minimal-api.md` | No Azure Functions; use BackgroundService |
| ADR-004 | `.claude/adr/ADR-004-job-contract.md` | Idempotent handlers; propagate CorrelationId |
| ADR-010 | `.claude/adr/ADR-010-di-minimalism.md` | ≤15 non-framework DI lines; use feature modules |
| ADR-013 | `.claude/adr/ADR-013-ai-architecture.md` | Extend BFF; no separate AI microservice |
| ADR-014 | `.claude/adr/ADR-014-ai-caching.md` | Redis for AI results; version cache keys |
| ADR-017 | `.claude/adr/ADR-017-job-status.md` | Persist status transitions |

---

## Key Patterns

**Load these patterns for implementation guidance:**

| Pattern | Path | When to Use |
|---------|------|-------------|
| Analysis Scopes | `.claude/patterns/ai/analysis-scopes.md` | Understanding scope system |
| Background Workers | `.claude/patterns/api/background-workers.md` | Job handler registration |
| Web API Client | `.claude/patterns/dataverse/web-api-client.md` | Dataverse queries |
| Unit Tests | `.claude/patterns/testing/unit-test-structure.md` | Writing tests |

---

## Critical Files

### Files to Modify

| File | Purpose | Phase |
|------|---------|-------|
| `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` | Replace stubs with Dataverse queries | 2-5 |
| `src/server/api/Sprk.Bff.Api/Program.cs` | Job handler DI registration | 0 |
| `src/server/api/Sprk.Bff.Api/Endpoints/AiEndpoints.cs` | Handler discovery API | 6 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/ToolHandlerMetadata.cs` | Add ConfigurationSchema | 6 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/*.cs` | Add JSON Schema | 6 |

### Reference Files (Read-Only)

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs` | Handler resolution pattern |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` | Job processor pattern |
| `src/server/api/Sprk.Bff.Api/Services/Ai/IToolHandlerRegistry.cs` | Handler registry interface |

---

## Dataverse Entity Mapping

| Scope Type | Entity Set | Query Pattern |
|------------|-----------|---------------|
| Tools | `sprk_analysistools` | `sprk_analysistools({id})?$expand=sprk_ToolTypeId($select=sprk_name)` |
| Skills | `sprk_promptfragments` | `sprk_promptfragments({id})?$expand=sprk_SkillTypeId($select=sprk_name)` |
| Knowledge | `sprk_contents` | `sprk_contents({id})?$expand=sprk_KnowledgeTypeId($select=sprk_name)` |
| Actions | `sprk_systemprompts` | `sprk_systemprompts({id})?$expand=sprk_ActionTypeId($select=sprk_name)` |

---

## Handler Resolution Logic

```csharp
// 1. Check sprk_handlerclass field first
if (!string.IsNullOrWhiteSpace(tool.HandlerClass))
{
    handler = registry.GetHandler(tool.HandlerClass);
}

// 2. Fall back to GenericAnalysisHandler if not found
if (handler == null)
{
    _logger.LogWarning("Handler '{HandlerClass}' not found. Available: [{Available}]. Falling back...",
        tool.HandlerClass, string.Join(", ", registry.GetRegisteredHandlerIds()));
    handler = registry.GetHandler("GenericAnalysisHandler");
}
```

---

## Scripts for Deployment/Testing

| Script | Purpose |
|--------|---------|
| `scripts/Deploy-BffApi.ps1` | Deploy API to Azure |
| `scripts/Test-SdapBffApi.ps1` | Test API endpoints |
| `scripts/Debug-OfficeWorkers.ps1` | Debug worker issues |

---

## Success Metrics

| Metric | Target | Current |
|--------|--------|---------|
| Dead-letter errors | < 1/day | ~5-10/hour |
| Scope resolution latency | < 200ms | TBD |
| Analysis success rate | > 98% | TBD |
| Handler discovery response | < 100ms | TBD |

---

## Parallel Execution Opportunities

**Tasks that CAN run in parallel** (after Phase 1 complete):
- Phase 2: Skill resolution (tasks 020-024)
- Phase 3: Knowledge resolution (tasks 030-034)
- Phase 4: Action resolution (tasks 040-044)

**How to parallelize**: Send ONE message with multiple Task tool calls, each invoking task-execute with a different task file.

---

## Decisions Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-01-29 | All handlers get ConfigurationSchema | Owner clarified: not incremental |
| 2026-01-29 | Dev environment only | No staging environment exists |
| 2026-01-29 | Full-time dedicated work | Owner clarified work mode |

---

## Quick Recovery

If resuming after compaction or in a new session:
1. Read `current-task.md` for active task state
2. Load applicable ADRs from table above
3. Continue from last checkpoint

---

*Generated by project-pipeline skill on 2026-01-29*
