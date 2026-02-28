# AI Playbook Builder R2 — Implementation Plan

> **Status**: Ready for Implementation
> **Created**: 2026-02-28
> **Estimated Effort**: 60-80 hours (significantly reduced by parallel execution)
> **Phases**: 10 (with parallel groups)
> **Branch**: `work/ai-playbook-builder-r2`

## Executive Summary

Complete the node-based Playbook execution pipeline by replacing stub code with production-quality Dataverse-backed scope resolution, wiring canvas-to-node sync, enabling parallel node execution, and delivering formatted markdown output through the Analysis Workspace.

**Critical Path**: Phase 0 → Phase 1 → Phases 2a/2b/2c (parallel) → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 7 → Phase 8 → Phase 9

## Architecture Context

### Design Constraints

| ADR | Key Constraint |
|-----|---------------|
| ADR-001 | Minimal API + BackgroundService; no Azure Functions |
| ADR-004 | Job Contract — idempotent handlers, propagate CorrelationId |
| ADR-006 | PCF for form controls, Code Page for standalone dialogs |
| ADR-007 | SpeFileStore facade — no Graph SDK type leakage |
| ADR-008 | Endpoint filters for auth — no global middleware |
| ADR-010 | DI minimalism — ≤15 non-framework registrations |
| ADR-013 | AI Architecture — extend BFF, not separate service |
| ADR-014 | AI Caching — Redis for expensive, IMemoryCache for metadata |
| ADR-021 | Fluent UI v9 — semantic tokens, dark mode, no hard-coded colors |
| ADR-022 | PCF uses React 16 (platform); Code Pages use React 18 (bundled) |

### Discovered Resources

**ADRs** (10): ADR-001, ADR-004, ADR-006, ADR-007, ADR-008, ADR-010, ADR-013, ADR-014, ADR-021, ADR-022
**Skills** (7): adr-aware, script-aware, spaarke-conventions, bff-deploy, code-page-deploy, code-review, adr-check
**Constraints** (7): api.md, ai.md, pcf.md, jobs.md, data.md, auth.md, config.md
**Patterns** (8): endpoint-definition, endpoint-filters, service-registration, streaming-endpoints, text-extraction, analysis-scopes, control-initialization, theme-management
**Scripts** (3): Deploy-BffApi.ps1, Deploy-PCFWebResources.ps1, Test-SdapBffApi.ps1

### Technical Decisions

- Canvas sync hooks into existing `PUT /api/ai/playbooks/{id}/canvas` (no new endpoint)
- Node failure policy: continue with available results, "Completed with warnings"
- Statuscode-based auto-execute replaces 60-second age check
- All stub/placeholder code eliminated — production-quality only

## Implementation Approach

### Execution Model

- **Fully autonomous** with `--dangerously-skip-permissions`
- **Parallel task agents** via Task tool subagents for independent work
- **Self-contained tasks** — each includes all file paths, patterns, constraints
- **Effort levels**: `high` for architecture-critical, `medium` for standard implementation

### Critical Path

```
Phase 0 (Job Handler Fix)
    → Phase 1 (Tool Resolution)
        → Phase 2a/2b/2c (Skill/Knowledge/Action — PARALLEL)
            → Phase 3 (Stub Removal + Handler Discovery)
                → Phase 4 (Canvas Sync + DI Registration)
                    → Phase 5 (Execution Wiring)
                        → Phase 6 (Streaming + Persistence)
                            → Phase 7 (Workspace UX — can overlap Phase 6)
                                → Phase 8 (E2E Testing)
                                    → Phase 9 (Deploy + Wrap-up)
```

## Phase Breakdown

### Phase 0: Job Handler Registration Fix (Critical)

**Objective**: Fix dead-letter queue errors by registering missing job handler.

**Deliverables**:
- Register `AppOnlyDocumentAnalysisJobHandler` in Program.cs
- Register `AiAnalysisNodeExecutor` in Program.cs (needed later but zero-risk to add now)
- Verify build succeeds

**Inputs**: Program.cs, AppOnlyDocumentAnalysisJobHandler.cs
**Outputs**: Updated Program.cs with DI registrations
**Estimate**: 1 hour
**Tags**: bff-api, di, config

---

### Phase 1: Complete Tool Resolution (Deploy + Test)

**Objective**: Verify existing GetToolAsync Dataverse query code works end-to-end.

**Deliverables**:
- Deploy BFF API with existing GetToolAsync code
- Test tool resolution against real Dataverse data
- Verify handler resolution fallback (HandlerClass → GenericAnalysisHandler → type-based)
- Fix any deserialization issues

**Inputs**: ScopeResolverService.cs (GetToolAsync), Dataverse sprk_analysistools records
**Outputs**: Working tool resolution with logs showing "Loaded tool from Dataverse"
**Estimate**: 3 hours
**Tags**: bff-api, ai, dataverse
**Dependencies**: Phase 0

---

### Phase 2a: Skill Resolution from Dataverse

**Objective**: Replace stub dictionary with Dataverse query for skills.

**Deliverables**:
- Implement GetSkillAsync querying sprk_promptfragments with $expand=sprk_SkillTypeId
- Add SkillEntity and SkillTypeReference DTO classes
- Add structured logging
- Test against real Dataverse data

**Inputs**: ScopeResolverService.cs, Dataverse sprk_promptfragment records
**Outputs**: Working skill resolution, DTO classes, log verification
**Estimate**: 3 hours
**Tags**: bff-api, ai, dataverse
**Dependencies**: Phase 1
**Parallel Group**: A (runs with 2b, 2c)

---

### Phase 2b: Knowledge Resolution from Dataverse

**Objective**: Replace stub dictionary with Dataverse query for knowledge.

**Deliverables**:
- Implement GetKnowledgeAsync querying sprk_contents with $expand=sprk_KnowledgeTypeId
- Add KnowledgeEntity and KnowledgeTypeReference DTO classes
- Add MapKnowledgeTypeName helper
- Test against real Dataverse data

**Inputs**: ScopeResolverService.cs, Dataverse sprk_content records
**Outputs**: Working knowledge resolution, DTO classes, type mapping
**Estimate**: 3 hours
**Tags**: bff-api, ai, dataverse
**Dependencies**: Phase 1
**Parallel Group**: A (runs with 2a, 2c)

---

### Phase 2c: Action Resolution from Dataverse

**Objective**: Replace stub dictionary with Dataverse query for actions.

**Deliverables**:
- Implement GetActionAsync querying sprk_systemprompts with $expand=sprk_ActionTypeId
- Add ActionEntity and ActionTypeReference DTO classes
- Add sort order extraction from type name prefix
- Test against real Dataverse data

**Inputs**: ScopeResolverService.cs, Dataverse sprk_systemprompt records
**Outputs**: Working action resolution, DTO classes, sort order parsing
**Estimate**: 3 hours
**Tags**: bff-api, ai, dataverse
**Dependencies**: Phase 1
**Parallel Group**: A (runs with 2a, 2b)

---

### Phase 3: Stub Removal + Handler Discovery API

**Objective**: Remove all stub code and add handler metadata endpoint.

**Deliverables**:
- Delete all stub dictionaries from ScopeResolverService (lines 25-129)
- Remove all fake GUID references
- Remove all TODO placeholders
- Add ConfigurationSchema (JSON Schema Draft 07) to all 9 tool handlers
- Add ToolHandlerMetadata.ConfigurationSchema property
- Implement GET /api/ai/handlers endpoint with IMemoryCache (5-min TTL)
- Verify dotnet build succeeds with zero stub references

**Inputs**: ScopeResolverService.cs, all 9 handler files, HandlerEndpoints.cs
**Outputs**: Clean ScopeResolverService, handler schemas, working API endpoint
**Estimate**: 6 hours
**Tags**: bff-api, ai, api, refactoring
**Dependencies**: Phase 2a, 2b, 2c (all scope queries must be proven before removing stubs)

---

### Phase 4: Canvas-to-Node Sync + DI Registration

**Objective**: Auto-sync canvas JSON to executable Dataverse node records.

**Deliverables**:
- Implement NodeService.SyncCanvasToNodesAsync(playbookId, canvasJson)
- Parse canvas JSON → extract nodes[] and edges[]
- Diff canvas nodes vs existing Dataverse records
- Create/update/delete node records as needed
- Map N:N scopes (skillIds[], knowledgeIds[]) to relationship tables
- Map lookup fields (toolId, actionId)
- Compute execution order from topological sort of edges
- Compute dependsOnJson from incoming edges
- Hook into existing PUT /api/ai/playbooks/{id}/canvas in PlaybookEndpoints.cs
- Add SyncCanvasToNodesAsync to INodeService interface

**Inputs**: NodeService.cs, INodeService.cs, PlaybookEndpoints.cs, canvasStore.ts (reference)
**Outputs**: Working canvas-to-node sync, updated endpoint, N:N relationships mapped
**Estimate**: 8 hours
**Tags**: bff-api, ai, dataverse
**Dependencies**: Phase 3

---

### Phase 5: Node Execution Wiring

**Objective**: Wire ExecutePlaybookAsync to delegate to PlaybookOrchestrationService when nodes exist.

**Deliverables**:
- Implement ResolveNodeScopesAsync querying per-node N:N relationship tables
- Wire AnalysisOrchestrationService.ExecutePlaybookAsync to check for nodes and delegate
- Add statuscode transitions: Draft → In Progress → Completed / Error / Completed with warnings
- Load document text into PlaybookRunContext.Document before execution
- Bridge PlaybookStreamEvent → AnalysisStreamChunk for SSE
- Extract shared document loading method from legacy path

**Inputs**: ScopeResolverService.cs, AnalysisOrchestrationService.cs, PlaybookRunContext.cs
**Outputs**: Working node-based execution path, statuscode transitions, document loading
**Estimate**: 8 hours
**Tags**: bff-api, ai, api
**Dependencies**: Phase 4

---

### Phase 6: Per-Token Streaming + Output Persistence

**Objective**: Enable streaming from AI nodes and persist Deliver Output result.

**Deliverables**:
- Enable per-token streaming in AiAnalysisNodeExecutor via IStreamingAnalysisToolHandler
- Map ToolStreamEvent.Token → NodeProgress → PlaybookStreamEvent → SSE
- Persist Deliver Output result to sprk_workingdocument
- Call FinalizeAnalysisAsync after Deliver Output
- Handle partial data (node failure: continue with available results)

**Inputs**: AiAnalysisNodeExecutor.cs, AnalysisOrchestrationService.cs, WorkingDocumentService.cs
**Outputs**: Working streaming, output persistence, partial data handling
**Estimate**: 6 hours
**Tags**: bff-api, ai, api
**Dependencies**: Phase 5

---

### Phase 7: Analysis Workspace UX

**Objective**: Complete client-side workspace enhancements.

**Deliverables**:
- Replace 60-second age check with statuscode-based auto-execute in useAnalysisExecution.ts
- Export triggerExecute() function for manual button
- Add completion toast notification (Fluent UI v9 Toast + Toaster provider)
- Add "Run Analysis" toolbar button with spinner
- Add source pane toggle button
- Auto-load SprkChat side pane via Xrm.App.sidePanes.createPane()
- Ensure dark mode compatibility for all new components

**Inputs**: useAnalysisExecution.ts, App.tsx, AnalysisWorkspace components
**Outputs**: Working auto-execute, toast, button, toggle, SprkChat
**Estimate**: 6 hours
**Tags**: frontend, fluent-ui, code-page, react
**Dependencies**: Phase 6 (server must work for client to test)
**Parallel Opportunity**: Client-side work can start during Phase 6

---

### Phase 8: End-to-End Testing & Verification

**Objective**: Verify complete pipeline works against real Dataverse data.

**Deliverables**:
- Test scope resolution for all 4 types with real GUIDs
- Test canvas-to-node sync via playbook save
- Test node-based execution for Document Profile playbook
- Verify formatted markdown output (not raw JSON)
- Verify statuscode transitions
- Verify legacy path still works (playbook without nodes)
- Verify dead-letter queue errors resolved
- Run dotnet build and verify zero warnings related to stubs
- Codebase search: zero _stub, fake GUIDs, TODO placeholders

**Inputs**: All modified files, Dataverse dev environment
**Outputs**: Test results, verification checklist, any bug fixes
**Estimate**: 4 hours
**Tags**: testing, integration-test, verification
**Dependencies**: Phase 7

---

### Phase 9: Deployment + Project Wrap-up

**Objective**: Deploy to dev, monitor, and complete project.

**Deliverables**:
- Deploy BFF API via Deploy-BffApi.ps1
- Deploy AnalysisWorkspace Code Page via code-page-deploy skill
- Verify in Dataverse environment
- Update README status to Complete
- Update TASK-INDEX.md final status
- Create lessons-learned.md

**Inputs**: All completed work
**Outputs**: Deployed system, updated docs
**Estimate**: 3 hours
**Tags**: deploy, azure, bff-api, code-page, wrap-up
**Dependencies**: Phase 8

## Dependencies Graph

```
Phase 0 ──→ Phase 1 ──→ Phase 2a ──┐
                    ├──→ Phase 2b ──┼──→ Phase 3 ──→ Phase 4 ──→ Phase 5 ──→ Phase 6 ──→ Phase 8 ──→ Phase 9
                    └──→ Phase 2c ──┘                                         ↑
                                                                    Phase 7 ──┘ (can overlap)
```

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | Phase 2a, 2b, 2c | Phase 1 complete | Independent scope types, different DTO classes |
| B | Phase 7 (client) overlaps Phase 6 (server) | Phase 5 complete | Different codebases (TypeScript vs C#) |
| C | Phase 3 sub-tasks (9 handler schemas) | Phase 2 complete | Each handler file independent |

## Testing Strategy

### Unit Testing
- ScopeResolverService: Mock HttpClient, verify Dataverse query URLs and deserialization
- NodeService.SyncCanvasToNodesAsync: Mock Dataverse, verify create/update/delete operations
- DeliverOutputNodeExecutor: Verify Handlebars template rendering with partial data

### Integration Testing
- End-to-end scope resolution against Dataverse dev environment
- Canvas-to-node sync with real playbook save
- Full playbook execution (Document Profile: 4 nodes)

### Verification Checklist
- Zero _stub references in codebase
- Zero fake GUIDs in codebase
- Zero TODO placeholders
- All 12 success criteria from spec.md pass

## Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|-------------|--------|------------|
| R1 | Dataverse field name mismatch | Medium | High | Verify with real queries before full implementation |
| R2 | N:N relationship table names wrong | Medium | Medium | Query Dataverse metadata API to confirm |
| R3 | Canvas-to-node sync data loss | Low | High | Diff-based with logging; preserve on failure |
| R4 | DI registration exceeds 15 limit | Low | Medium | Use assembly scanning pattern for node executors |
| R5 | Handlebars missing variable handling | Low | Medium | Test with partial data scenarios |

## References

| Resource | Path |
|----------|------|
| Design Document | projects/ai-playbook-builder-r2/design.md |
| Specification | projects/ai-playbook-builder-r2/spec.md |
| AI Architecture Guide | docs/guides/SPAARKE-AI-ARCHITECTURE.md |
| ADR Index | .claude/adr/ |
| API Constraints | .claude/constraints/api.md |
| AI Constraints | .claude/constraints/ai.md |
| Frontend Constraints | .claude/constraints/pcf.md |
| Streaming Patterns | .claude/patterns/ai/streaming-endpoints.md |
| Scope Patterns | .claude/patterns/ai/analysis-scopes.md |
| BFF Deploy Script | scripts/Deploy-BffApi.ps1 |

---

*Generated by project-pipeline. Last updated: 2026-02-28*
