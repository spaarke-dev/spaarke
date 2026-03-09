# CLAUDE.md — AI Playbook Builder R2

> **Project**: ai-playbook-builder-r2
> **Phase**: Implementation
> **Last Updated**: 2026-02-28
> **Current Task**: None (awaiting task 001)
> **Next Action**: Execute task 001

## Quick Reference

| Resource | Path |
|----------|------|
| Design | projects/ai-playbook-builder-r2/design.md |
| Spec | projects/ai-playbook-builder-r2/spec.md |
| Plan | projects/ai-playbook-builder-r2/plan.md |
| Task Index | projects/ai-playbook-builder-r2/tasks/TASK-INDEX.md |
| Current Task | projects/ai-playbook-builder-r2/current-task.md |
| Branch | work/ai-playbook-builder-r2 |
| PR | #201 (draft) |

## Context Loading Rules

**ALWAYS load before any task work:**
1. This file (CLAUDE.md)
2. current-task.md (active task state)
3. The active task .poml file
4. Knowledge files listed in the task's `<knowledge>` section

**Load on demand:**
- design.md — When implementation details or architecture questions arise
- spec.md — When checking requirements or acceptance criteria
- plan.md — When checking phase dependencies or parallel groups

## Implementation Standard

**This is a production-quality implementation, not a POC.**
- No stubs, no hardcoded values, no TODO placeholders
- Every method has a real, production-quality implementation or gets removed
- No "good enough for now" shortcuts

## Execution Model

- **Fully autonomous**: `--dangerously-skip-permissions`
- **Parallel task agents**: Independent tasks run via Task tool subagents
- **Self-contained tasks**: Each includes all file paths, patterns, constraints
- **No human confirmation prompts** during task execution

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing project tasks, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Invoke task-execute with task X POML file |
| "continue" | Check TASK-INDEX.md for next pending task, invoke task-execute |
| "next task" | Check TASK-INDEX.md for next pending task, invoke task-execute |
| "keep going" | Check TASK-INDEX.md for next pending task, invoke task-execute |

## Key Technical Constraints

### Server-Side (.NET 8 BFF API)
- **ADR-001**: Minimal API endpoints only (`app.MapGet/MapPost`)
- **ADR-004**: Job handlers must be idempotent, propagate CorrelationId
- **ADR-007**: No Graph SDK types above SpeFileStore facade
- **ADR-008**: Endpoint filters for auth, no global middleware
- **ADR-010**: ≤15 non-framework DI registrations
- **ADR-013**: Extend BFF, not separate AI service
- **ADR-014**: IMemoryCache for handler metadata (short-lived)

### Client-Side (Analysis Workspace Code Page)
- **ADR-006**: Code Page pattern (React 18, standalone dialog)
- **ADR-021**: Fluent UI v9 only, semantic tokens, dark mode required
- **ADR-022**: React 18 `createRoot()` in Code Pages (not PCF React 16)

### Patterns to Follow
- **Dataverse query**: `$expand` for lookups, 404 → null, `ReadFromJsonAsync<TEntity>`
- **Handler fallback**: HandlerClass → GenericAnalysisHandler → type-based
- **SSE streaming**: `text/event-stream`, `yield return` async stream
- **Node execution**: `INodeExecutor` interface, registered as singleton

## Applicable ADRs

| ADR | Domain | Key Constraint |
|-----|--------|---------------|
| ADR-001 | API | Minimal API + BackgroundService |
| ADR-004 | Jobs | Idempotent handlers, CorrelationId |
| ADR-006 | Frontend | PCF for forms, Code Page for dialogs |
| ADR-007 | Storage | SpeFileStore facade |
| ADR-008 | Auth | Endpoint filters, no global middleware |
| ADR-010 | DI | ≤15 non-framework registrations |
| ADR-013 | AI | Extend BFF, tool framework |
| ADR-014 | Caching | Redis for expensive, IMemoryCache for metadata |
| ADR-021 | UI | Fluent v9, dark mode, semantic tokens |
| ADR-022 | PCF | React 16 in PCF, React 18 in Code Pages |

## Applicable Constraints

| File | Domain |
|------|--------|
| .claude/constraints/api.md | API architecture rules |
| .claude/constraints/ai.md | AI feature rules |
| .claude/constraints/pcf.md | Frontend/UI rules |
| .claude/constraints/jobs.md | Background processing rules |
| .claude/constraints/data.md | Data access rules |
| .claude/constraints/auth.md | Authentication rules |

## Applicable Patterns

| Pattern | Path |
|---------|------|
| Endpoint definition | .claude/patterns/api/endpoint-definition.md |
| Endpoint filters | .claude/patterns/api/endpoint-filters.md |
| Service registration | .claude/patterns/api/service-registration.md |
| SSE streaming | .claude/patterns/ai/streaming-endpoints.md |
| Scope resolution | .claude/patterns/ai/analysis-scopes.md |
| Text extraction | .claude/patterns/ai/text-extraction.md |
| Theme management | .claude/patterns/pcf/theme-management.md |

## Decisions Made

*(Updated during implementation)*

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-02-28 | Canvas sync hooks into existing PUT | Simpler — no new endpoint needed |
| 2026-02-28 | Node failure: continue with available results | Partial data > total failure |
| 2026-02-28 | Statuscode-based auto-execute | Deterministic, no timing dependency |
| 2026-02-28 | Dataverse schema changes in scope | Fix gaps as encountered |

## Implementation Notes

*(Updated during implementation — gotchas, workarounds, discoveries)*

---

*Generated by project-pipeline. Source: spec.md + design.md*
