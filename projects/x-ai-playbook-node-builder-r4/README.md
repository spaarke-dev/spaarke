# AI Playbook Builder R2

Complete the node-based Playbook execution pipeline end-to-end — from visual canvas design through AI orchestration to formatted output in the Analysis Workspace. Production-quality implementation replacing all stub code with fully functioning Dataverse-backed scope resolution, parallel node execution, and formatted markdown output.

## Quick Links

| Resource | Location |
|----------|----------|
| Design Document | [design.md](design.md) |
| Implementation Spec | [spec.md](spec.md) |
| Implementation Plan | [plan.md](plan.md) |
| Task Index | [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) |
| AI Context | [CLAUDE.md](CLAUDE.md) |
| Active Task State | [current-task.md](current-task.md) |

## Current Status

| Phase | Description | Progress | Status |
|-------|-------------|----------|--------|
| 0 | Job Handler Fix | 100% | Complete |
| 1 | Tool Resolution | 100% | Complete |
| 2 | Skill/Knowledge/Action Resolution | 100% | Complete |
| 3 | Stub Removal + Handler Discovery | 100% | Complete |
| 4 | Canvas-to-Node Sync + DI | 100% | Complete |
| 5 | Node Execution Wiring | 100% | Complete |
| 6 | Streaming + Output Persistence | 100% | Complete |
| 7 | Analysis Workspace UX | 100% | Complete |
| 8 | End-to-End Testing | 100% | Complete |
| 9 | Deployment + Monitoring | 90% | Deployed (verification in progress) |

**Overall**: 98% (deployed, verification in progress) | **Branch**: `work/ai-playbook-builder-r2`

## Problem Statement

The Playbook execution pipeline is ~80% built but has critical gaps preventing activation:

1. **Stub Dictionary Anti-Pattern**: `ScopeResolverService.cs` contains hardcoded fake GUIDs instead of Dataverse queries — scope resolution always fails
2. **No Node Records**: Canvas JSON stores visual designs but never creates `sprk_playbooknode` Dataverse records — `GetNodesAsync()` always returns empty
3. **Legacy Fallback**: Empty node array forces the legacy sequential path that streams raw JSON instead of formatted markdown
4. **Missing DI Registrations**: `AiAnalysisNodeExecutor` and `AppOnlyDocumentAnalysisJobHandler` not registered — nodes can't execute, jobs go to dead-letter queue
5. **Age-Based Auto-Execute**: 60-second `createdOn` heuristic is unreliable — should use statuscode

## Solution Summary

Three focus areas across 12 implementation phases:

1. **Scope Resolution Foundation** — Replace all stub dictionaries with Dataverse Web API queries for tools, skills, knowledge, and actions. Fix job handler registration. Add handler discovery API.
2. **Playbook Node AI Orchestration** — Canvas-to-node auto-sync on playbook save, per-node scope resolution via N:N tables, parallel batch execution via `PlaybookOrchestrationService`, Deliver Output with Handlebars templates.
3. **Analysis Workspace UX** — Statuscode-based auto-execute, completion toast, Run Analysis button, source pane toggle, SprkChat side pane auto-load.

## Graduation Criteria

- [ ] Dead-letter queue errors < 1/day (down from ~5-10/hour)
- [ ] All 4 scope types resolve from Dataverse (not stubs)
- [ ] Zero stub/placeholder code remains in ScopeResolverService
- [ ] `GET /api/ai/handlers` returns all 9 handlers with ConfigurationSchema
- [ ] Document Profile playbook canvas syncs to 4 node records
- [ ] Node-based execution path activates when nodes exist
- [ ] Deliver Output produces formatted markdown (not raw JSON)
- [ ] Analysis statuscode transitions: Draft → In Progress → Completed
- [ ] Per-token SSE streaming works end-to-end
- [ ] SprkChat side pane auto-loads with workspace
- [ ] Legacy path (no nodes) still works unchanged
- [ ] Codebase search for `_stub`, fake GUIDs, `// TODO` returns zero results

## Scope

### In Scope
- Scope resolution Dataverse queries (all 4 types)
- Canvas-to-node sync via existing PUT endpoint
- Node-based execution wiring + parallel batching
- Deliver Output Handlebars rendering
- Analysis Workspace UX enhancements (toast, button, toggle, SprkChat)
- DI registration fixes
- Handler discovery API with ConfigurationSchema
- Dataverse schema changes if needed

### Out of Scope
- New PCF controls
- Staging/production deployment
- Playbook Builder PCF visual changes
- Office add-in changes
- New AI tool handler implementations

## Key Decisions

| Decision | Rationale | Reference |
|----------|-----------|-----------|
| Hook canvas sync into existing PUT | Simpler — no new endpoint, no additional PCF API call | Owner clarification |
| Node failure: continue with available results | Partial data better than total failure; Deliver Output handles missing vars | Owner clarification |
| Statuscode-based auto-execute | Deterministic — no timing dependency | AD-02 in design.md |
| Deliver Output for all formatting | Separation of concerns — tools return JSON, template renders markdown | AD-03 in design.md |
| Three-tier scope resolution | Zero-deployment for new scopes; GenericAnalysisHandler covers 95% | AD-06 in design.md |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Dataverse query performance | High | Medium | IMemoryCache for handler metadata; Redis if latency > 200ms |
| Schema mismatch (field names) | High | Medium | Verify with real Dataverse data before implementation |
| Canvas-to-node sync data loss | Medium | Low | Diff-based sync with logging; preserve records on failure |
| N:N relationship table name mismatch | Medium | Medium | Verify via Dataverse query during implementation |

## Dependencies

### Internal
- Dataverse dev environment with all entities populated
- Azure OpenAI dev endpoint accessible
- Existing Analysis Workspace Code Page (complete)
- Existing Playbook Builder PCF (complete)

### External
- Azure OpenAI token availability
- Dataverse Web API performance

## Execution Model

This project runs with **fully autonomous execution** using Claude Code with `--dangerously-skip-permissions`. Tasks are decomposed to maximize parallelism via Task tool subagents.

---

*Generated by project-pipeline. Last updated: 2026-02-28*
