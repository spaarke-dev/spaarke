# CLAUDE.md — Production Performance Improvement R1

> **Purpose**: AI context file for Claude Code. Always load this file first when working on this project.
> **Last Updated**: 2026-03-11

## Project Status

| Field | Value |
|-------|-------|
| Phase | Complete |
| Last Updated | 2026-03-13 |
| Current Task | None (project complete) |
| Next Action | Merge to master (Task 005 blocked on external dependency) |

## Quick Reference

### Key Files

| File | Purpose |
|------|---------|
| [spec.md](spec.md) | AI-optimized specification (source of truth) |
| [README.md](README.md) | Project overview and graduation criteria |
| [plan.md](plan.md) | Implementation plan with phase breakdown |
| [current-task.md](current-task.md) | Active task state tracker |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | Task status overview |

### Project Metadata

| Field | Value |
|-------|-------|
| Name | Production Performance Improvement R1 |
| Type | Performance optimization + code quality + infrastructure |
| Complexity | High (7 domains, 30+ deliverables, 6 phases) |
| Branch | `feature/production-performance-improvement-r1` |
| Beta Scale | 15-50 concurrent users, ~200 analyses/day |

## Context Loading Rules

When working on any task in this project:
1. Load this `CLAUDE.md` first for project context
2. Load `current-task.md` for active task state
3. Load the specific task `.poml` file via `task-execute` skill
4. The task-execute skill auto-loads knowledge files, ADRs, and patterns

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing tasks, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

**Trigger phrases** → Required action:
- "work on task X" → Invoke task-execute with task X POML
- "continue" / "next task" → Check TASK-INDEX.md, invoke task-execute
- "resume task X" → Invoke task-execute with task X POML

**Parallel Execution**: This project is designed for maximum parallelism via Claude Code task agents. When tasks have no dependencies between them, execute them simultaneously using multiple Skill tool invocations in a single message.

## Key Technical Constraints

### From ADRs (MUST follow)
- **ADR-009**: Redis-first caching via `IDistributedCache`; ETag-versioned keys; no L1 without profiling proof
- **ADR-003**: Cache auth **data** (roles, teams); compute **decisions** fresh per-request
- **ADR-007**: All Graph/SPE operations through `SpeFileStore` facade
- **ADR-008**: Endpoint filters for authorization; no global middleware
- **ADR-010**: ≤15 non-framework DI registrations; register concretes
- **ADR-013**: Extend BFF for AI; no separate microservice; rate limit AI endpoints
- **ADR-015**: Log only identifiers/sizes/timings; never document content or prompts

### Canonical Implementations (follow these patterns)
- `Services/GraphTokenCache.cs` — Redis caching pattern (production-proven)
- `Api/FileAccessEndpoints.cs` — Endpoint filter authorization
- `Services/Ai/Handlers/GenericAnalysisHandler.cs` — JPS-based analysis
- `Infrastructure/Caching/CacheMetrics.cs` — Cache telemetry
- `Infrastructure/Caching/DistributedCacheExtensions.cs` — Cache helpers

### Owner Decisions
- **Auth (F1)**: Use same MSAL bearer auth as all BFF endpoints
- **Obsolete handlers (F3)**: Remove now — JPS replacements verified complete
- **Workspace services (F5)**: Implement real Dataverse queries
- **Beta scale**: 15-50 concurrent users, ~200 analyses/day

## Parallel Execution Groups

Tasks within the same group CAN be executed simultaneously using Claude Code task agents. See `tasks/TASK-INDEX.md` for the full parallel execution map.

| Group | Phase | Tasks | Prerequisite | Notes |
|-------|-------|-------|-------------|-------|
| P1-A | 1 | C0, B3 | None | Independent infrastructure + Dataverse fixes |
| P1-B | 1 | F1, F6 | None | Independent security + safety fixes |
| P1-C | 1 | F2 | Task 033 design | Auth filters (may need to wait for design) |
| P2-A | 2 | E1, E3, E4 | Phase 1 | Independent AI pipeline optimizations |
| P2-B | 2 | B1, A3, A4, G2 | Phase 1 | Independent Dataverse + BFF + logging fixes |
| P3-A | 3 | E2, E5 | E1 | AI caching (text + RAG results) |
| P3-B | 3 | A1, A2 | Phase 2 | BFF caching (Graph + auth) |
| P3-C | 3 | B2, B4 | B1 | Dataverse batching + pagination |
| P4-A | 4 | G1, G3, G4, G5 | Phase 3 | All logging tasks (independent) |
| P4-B | 4 | F3, C8 | Phase 3 | Code cleanup (independent) |
| P4-C | 4 | F5 | Phase 3 | Workspace real queries |
| P5 | 5 | C1-C7 | Phase 4 | Infrastructure (some sequential) |
| P6 | 6 | D1-D4, F4 | Phase 5 | CI/CD + refactoring |

## Decisions Made

| Date | Decision | Rationale | Who |
|------|----------|-----------|-----|
| 2026-03-11 | MSAL auth for F1 endpoints | Same mechanism as all BFF endpoints | Owner |
| 2026-03-11 | Remove obsolete handlers now | JPS replacements verified; 217 tests passing | Owner |
| 2026-03-11 | Implement real Workspace queries | Beta users need real data | Owner |
| 2026-03-11 | 15-50 concurrent beta users, ~200 analyses/day | Beta scale target | Owner |

## Implementation Notes

*(Updated during implementation)*

## Resources

### Applicable ADRs
1. ADR-001: Minimal API + BackgroundService (no Azure Functions)
2. ADR-002: Thin plugins (<200 LoC, <50ms)
3. ADR-003: Authorization seams (cache data, not decisions)
4. ADR-007: SpeFileStore facade (no Graph SDK leakage)
5. ADR-008: Endpoint filters (no global auth middleware)
6. ADR-009: Redis-first caching (IDistributedCache, ETag keys)
7. ADR-010: DI minimalism (≤15 registrations, concretes)
8. ADR-013: AI Architecture (extend BFF, rate limit)
9. ADR-015: AI Data Governance (log only identifiers)
10. ADR-019: BFF API patterns (Minimal API)

### Constraint Files
- `.claude/constraints/api.md`
- `.claude/constraints/auth.md`
- `.claude/constraints/data.md`
- `.claude/constraints/ai.md`
- `.claude/constraints/jobs.md`
- `.claude/constraints/azure-deployment.md`
- `.claude/constraints/testing.md`

### Pattern Files
- `.claude/patterns/api/distributed-cache.md`
- `.claude/patterns/api/token-cache.md`
- `.claude/patterns/api/request-cache.md`
- `.claude/patterns/api/resilience.md`
- `.claude/patterns/api/endpoint-filters.md`
- `.claude/patterns/api/streaming-endpoints.md`
- `.claude/patterns/api/text-extraction.md`
- `.claude/patterns/api/error-handling.md`
- `.claude/patterns/api/service-registration.md`

---

*This file should be kept updated throughout the project lifecycle.*
