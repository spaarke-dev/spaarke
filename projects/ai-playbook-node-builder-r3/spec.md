# AI Playbook Builder R2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-02-27
> **Source**: projects/ai-playbook-builder-r2/design.md
> **Branch**: `work/ai-playbook-builder-r2`
> **Draft PR**: #201

## Executive Summary

Complete the node-based Playbook execution pipeline end-to-end — from visual canvas design through AI orchestration to formatted output in the Analysis Workspace. This is a **production-quality implementation**, not a proof of concept. All stub code, hardcoded dictionaries, fake GUIDs, and placeholder implementations must be replaced with fully functioning production code that queries real data sources, handles errors gracefully, and operates reliably without manual intervention.

The infrastructure is ~80% built but has critical gaps: scope resolution uses hardcoded stub dictionaries with fake GUIDs instead of querying Dataverse, no `sprk_playbooknode` records are created from the canvas, and the execution engine falls through to a legacy sequential path that streams raw JSON.

This project consolidates scope-resolution fixes and playbook-node wiring into a single implementation with three focus areas and 12 implementation phases.

> **Implementation Standard**: Every component delivered by this project must be production-ready. No stubs, no hardcoded values, no TODO placeholders, no "good enough for now" shortcuts. If a method has a stub body, it gets a real implementation or it gets removed.

## Scope

### In Scope

**Focus Area 1: Scope Resolution Foundation (Server-Side)**
- Fix `AppOnlyDocumentAnalysis` job handler DI registration (dead-letter fix)
- Complete `GetToolAsync` Dataverse query (deploy + test existing code)
- Implement `GetSkillAsync` querying `sprk_promptfragments` with `$expand=sprk_SkillTypeId`
- Implement `GetKnowledgeAsync` querying `sprk_contents` with `$expand=sprk_KnowledgeTypeId`
- Implement `GetActionAsync` querying `sprk_systemprompts` with `$expand=sprk_ActionTypeId`
- Remove all stub dictionaries (lines 25-129 of ScopeResolverService.cs) after Dataverse queries proven
- Implement handler resolution with fallback: HandlerClass → GenericAnalysisHandler → type-based
- Add `GET /api/ai/handlers` endpoint for handler discovery with ConfigurationSchema
- Add JSON Schema Draft 07 configuration schemas to all 9 tool handler metadata

**Focus Area 2: Playbook Node AI Orchestration (Server-Side)**
- Canvas-to-node auto-sync via existing `PUT /api/ai/playbooks/{id}/canvas` endpoint
- Register `AiAnalysisNodeExecutor` in DI (missing — nodes can't execute)
- Implement `ResolveNodeScopesAsync` querying per-node N:N relationship tables
- Load document text into `PlaybookRunContext.Document` before execution
- Wire `ExecutePlaybookAsync` to delegate to `PlaybookOrchestrationService` when nodes exist
- Enable per-token streaming in `AiAnalysisNodeExecutor` via `IStreamingAnalysisToolHandler`
- Persist Deliver Output result to `sprk_workingdocument`
- Configure Document Profile playbook with 4 nodes (3 AI + 1 Deliver Output)

**Focus Area 3: Analysis Workspace Application (Client-Side)**
- Replace 60-second `createdOn` age check with statuscode-based auto-execute
- Add completion toast notification (Fluent UI v9 `<Toast>`)
- Add "Run Analysis" toolbar button with `triggerExecute()` callback
- Add source pane toggle button (show/hide document viewer)
- Auto-load SprkChat side pane via `Xrm.App.sidePanes.createPane()`

### Out of Scope

- New PCF controls (Playbook Builder PCF is complete)
- Staging/production deployment (dev environment only for this project)
- Playbook Builder PCF visual changes (canvas UX is complete)
- Office add-in changes
- New AI tool handler implementations (existing 9 handlers are sufficient)

> **Note**: Dataverse schema changes (new fields, option set values, N:N relationships) are **in scope** if required to support implementation. The existing schema is assumed correct but will be verified during implementation — any gaps will be addressed.

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` — Replace all stub dictionaries with Dataverse queries
- `src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs` — Add SyncCanvasToNodesAsync
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` — Wire delegation to PlaybookOrchestrationService, add statuscode transitions
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` — Add streaming support, register in DI
- `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEndpoints.cs` — Hook canvas sync into existing PUT
- `src/server/api/Sprk.Bff.Api/Api/Ai/HandlerEndpoints.cs` — Add GET /api/ai/handlers
- `src/server/api/Sprk.Bff.Api/Program.cs` — Register AiAnalysisNodeExecutor + AppOnlyDocumentAnalysisJobHandler
- `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/*.cs` — Add ConfigurationSchema to all 9 handlers
- `src/client/code-pages/AnalysisWorkspace/src/hooks/useAnalysisExecution.ts` — Statuscode-based auto-execute
- `src/client/code-pages/AnalysisWorkspace/src/App.tsx` — Toast, Run button, source toggle, SprkChat

## Requirements

### Functional Requirements

1. **FR-01**: Fix `AppOnlyDocumentAnalysis` job handler DI registration — Acceptance: Zero "NoHandler" dead-letter errors; email processing creates `sprk_analysis` records successfully
2. **FR-02**: `GetToolAsync` queries Dataverse `sprk_analysistools` with `$expand=sprk_ToolTypeId` — Acceptance: Logs show `"Loaded tool from Dataverse: {ToolName}"`; GenericAnalysisHandler fallback works for missing HandlerClass
3. **FR-03**: `GetSkillAsync` queries Dataverse `sprk_promptfragments` with `$expand=sprk_SkillTypeId` — Acceptance: Logs show `"Loaded skill from Dataverse: {SkillName}"`; returns null for non-existent IDs
4. **FR-04**: `GetKnowledgeAsync` queries Dataverse `sprk_contents` with `$expand=sprk_KnowledgeTypeId` — Acceptance: Logs show `"Loaded knowledge from Dataverse: {KnowledgeName}"`; maps KnowledgeType correctly
5. **FR-05**: `GetActionAsync` queries Dataverse `sprk_systemprompts` with `$expand=sprk_ActionTypeId` — Acceptance: Logs show `"Loaded action from Dataverse: {ActionName}"`; extracts sort order from type name prefix
6. **FR-06**: All stub dictionaries, hardcoded values, and placeholder implementations removed from entire codebase — Acceptance: No references to `_stubTools`, `_stubSkills`, `_stubKnowledge`, `_stubActions`; zero fake GUIDs; zero `// TODO: replace with real implementation` comments; zero methods that return empty/default values as placeholders (e.g., `ResolveNodeScopesAsync` returning empty `ResolvedScopes`)
7. **FR-07**: Handler resolution follows 3-tier pattern: `sprk_handlerclass` → GenericAnalysisHandler → type-based lookup — Acceptance: Log shows fallback chain when handler not found; error messages list available handlers
8. **FR-08**: `GET /api/ai/handlers` returns metadata for all registered handlers including ConfigurationSchema — Acceptance: JSON response includes all 9 handlers with handlerId, name, description, supportedToolTypes, configurationSchema; 5-minute IMemoryCache
9. **FR-09**: All 9 tool handlers include JSON Schema Draft 07 configuration schema — Acceptance: Each handler's metadata includes valid JSON Schema with type, properties, required fields
10. **FR-10**: Canvas-to-node auto-sync on playbook save via existing PUT endpoint — Acceptance: Saving playbook form creates/updates/deletes `sprk_playbooknode` records matching canvas JSON; N:N skill/knowledge relationships mapped; execution order computed from edges
11. **FR-11**: Node-based playbooks execute via `PlaybookOrchestrationService` with parallel batching — Acceptance: `GetNodesAsync()` returns nodes → delegates to PlaybookOrchestrationService (not legacy path); SemaphoreSlim(3) parallel execution; logs show batch execution
12. **FR-12**: Deliver Output node renders formatted markdown via Handlebars templates — Acceptance: `sprk_workingdocument` contains clean formatted markdown (not raw JSON); template variables from AI node outputs resolve correctly
13. **FR-13**: `ResolveNodeScopesAsync` loads per-node scopes from N:N relationship tables — Acceptance: Each node gets its specific skills (via `sprk_playbooknode_skill`), knowledge (via `sprk_playbooknode_knowledge`), and tool (via `sprk_toolid` lookup)
14. **FR-14**: Document text loaded into `PlaybookRunContext.Document` before node execution — Acceptance: AI nodes receive document text in their execution context; text extracted via `ITextExtractor` (PDF, DOCX, TXT, images)
15. **FR-15**: Per-token SSE streaming from AI nodes via `IStreamingAnalysisToolHandler` — Acceptance: Client receives incremental text during AI node execution; tokens mapped from `ToolStreamEvent.Token` → `PlaybookStreamEvent.NodeProgress` → SSE
16. **FR-16**: Analysis statuscode transitions: Draft(1) → In Progress → Completed(2) — Acceptance: Auto-execute fires only for Draft with empty content; BFF sets In Progress at start, Completed after Deliver Output persisted; reopening completed analysis shows content without re-executing
17. **FR-17**: Completion toast notification in Analysis Workspace — Acceptance: Fluent UI v9 Toast with "Analysis complete" appears for 5 seconds after execution finishes
18. **FR-18**: "Run Analysis" toolbar button for manual re-execution — Acceptance: Button disabled during execution and when no playbook/action configured; shows spinner during execution; calls `triggerExecute()`
19. **FR-19**: Source pane toggle in Analysis Workspace toolbar — Acceptance: Toggle hides/shows SourceViewerPanel; EditorPanel takes full width when hidden; state persists during session
20. **FR-20**: SprkChat side pane auto-loads with Analysis Workspace — Acceptance: `Xrm.App.sidePanes.createPane()` called on workspace load with analysisId and documentId; pane closeable and reopenable from rail; graceful fallback if sidePanes API unavailable

### Non-Functional Requirements

- **NFR-01**: Scope resolution latency < 200ms (p95) for individual getters
- **NFR-02**: `GET /api/ai/handlers` response < 100ms with IMemoryCache (5-minute TTL)
- **NFR-03**: Document Profile playbook (4 nodes) end-to-end execution < 30 seconds
- **NFR-04**: Per-token SSE streaming latency < 200ms from Azure OpenAI to client render
- **NFR-05**: Canvas-to-node sync < 2 seconds for playbooks with ≤ 20 nodes
- **NFR-06**: Legacy (non-node) playbook execution continues unchanged (zero regression)
- **NFR-07**: Analysis success rate > 98% after deployment
- **NFR-08**: Dead-letter queue errors < 1/day (down from ~5-10/hour)
- **NFR-09**: Zero code deployment required for new scope configurations in Dataverse
- **NFR-10**: Helpful error messages when handler not found (lists available handlers)

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API + BackgroundService pattern. No Azure Functions. All endpoints use `app.MapGet/MapPost`. Background processing via `BackgroundService` + service bus.
- **ADR-004**: Job Contract — handlers MUST be idempotent, propagate CorrelationId, emit JobOutcome events. Use Service Bus for async processing.
- **ADR-006**: Anti-legacy-JS — PCF for form-bound controls (React 16), Code Page for standalone dialogs (React 18). Analysis Workspace is a Code Page.
- **ADR-007**: SpeFileStore facade — No Graph SDK types leak above facade. Document download via `SpeFileStore.DownloadFileAsUserAsync()`.
- **ADR-008**: Endpoint filters for auth — No global auth middleware. Use `.RequireAuthorization()` or endpoint filter per route.
- **ADR-010**: DI minimalism — ≤15 non-framework registrations. Use feature modules (`AddToolFramework()`, assembly scanning). Register AiAnalysisNodeExecutor and AppOnlyDocumentAnalysisJobHandler without exceeding limit.
- **ADR-013**: AI Architecture — Extend BFF, not separate service. Use SpeFileStore for files. Tool Framework with `IAiToolHandler` interface. Dual pipeline (SPE storage + AI processing).
- **ADR-014**: AI Caching — Redis for expensive AI results. IMemoryCache for short-lived metadata (handler list). Cache scope resolution results where appropriate.
- **ADR-021**: Fluent UI v9 — All UI must use Fluent v9. Dark mode required. Toast, Button, ToggleButton from `@fluentui/react-components`. No hard-coded colors.
- **ADR-022**: PCF Platform Libraries — PCF controls use React 16 APIs (platform-provided). Code Pages bundle React 18 (`createRoot`). Analysis Workspace is a Code Page (React 18).

### MUST Rules

- MUST use Minimal API endpoint pattern (`app.MapGet/MapPost`) for new handler discovery endpoint
- MUST use `.RequireAuthorization()` on handler discovery endpoint
- MUST use `IMemoryCache` for handler metadata caching (short-lived, < 5 min)
- MUST register job handlers via DI (`IJobHandler` interface)
- MUST propagate `CorrelationId` in job handler execution
- MUST keep scope resolution latency under 200ms p95
- MUST use Fluent UI v9 components exclusively in Analysis Workspace
- MUST support dark mode in all new UI components
- MUST preserve legacy execution path when `GetNodesAsync()` returns empty
- MUST NOT use global auth middleware
- MUST NOT leak Graph SDK types above SpeFileStore facade
- MUST NOT exceed ≤15 non-framework DI registrations (use assembly scanning)
- MUST NOT create new PCF controls (use Code Pages for standalone UI)
- MUST NOT use React 18 APIs in PCF controls
- MUST NOT leave any stub, hardcoded, or placeholder code — every method must have a real, production-quality implementation
- MUST NOT introduce new TODOs or "temporary" workarounds — if something needs to work, implement it fully

### Existing Patterns to Follow

- **Dataverse Web API query**: See `ScopeResolverService.GetToolAsync()` — `$expand` for lookup relations, 404 → null, `ReadFromJsonAsync<TEntity>`
- **Handler resolution with fallback**: See `AppOnlyAnalysisService` — check HandlerClass → GenericAnalysisHandler → type-based lookup
- **SSE streaming**: See `AnalysisEndpoints.cs` — `text/event-stream` content type, `yield return` for async stream
- **Node execution**: See `DeliverOutputNodeExecutor` — implements `INodeExecutor`, registered as `INodeExecutor` singleton
- **Canvas JSON format**: See `PlaybookBuilderHost.tsx` canvasStore — `{ nodes: PlaybookNode[], edges: Edge[], version: 1 }`

## Success Criteria

1. [ ] Dead-letter queue errors drop to < 1/day — Verify: Azure Service Bus dead-letter count monitoring
2. [ ] All 4 scope types resolve from Dataverse (not stubs) — Verify: Log entries show "Loaded {type} from Dataverse" for tools, skills, knowledge, actions
3. [ ] Zero stub dictionary code remains in ScopeResolverService — Verify: Code search for `_stub` returns no results
4. [ ] `GET /api/ai/handlers` returns all 9 handlers with schemas — Verify: HTTP call returns JSON array with configurationSchema per handler
5. [ ] Document Profile playbook canvas auto-syncs to 4 node records — Verify: Query `sprk_playbooknode` by playbook ID returns 4 records with correct N:N scopes
6. [ ] Node-based execution path activates when nodes exist — Verify: Server logs show `PlaybookOrchestrationService.ExecuteAsync()` called
7. [ ] Deliver Output produces formatted markdown — Verify: `sprk_workingdocument` content is markdown with headings, not raw JSON
8. [ ] Analysis statuscode transitions correctly — Verify: Draft(1) → In Progress → Completed(2); reopen shows content without re-executing
9. [ ] Per-token SSE streaming works — Verify: Client renders progressive text during execution
10. [ ] SprkChat auto-loads — Verify: Side pane opens on workspace load with analysis context
11. [ ] Legacy path still works — Verify: Playbook without nodes executes via sequential path; action-based analysis unchanged
12. [ ] Zero stub/placeholder code remains — Verify: Codebase search for `_stub`, fake GUIDs, `// TODO`, empty `ResolvedScopes` returns no results; every method has a real implementation

## Dependencies

### Prerequisites

- Dataverse dev environment (`spaarkedev1.crm.dynamics.com`) accessible with all entities/fields/N:N relationships
- Azure OpenAI (`spaarke-openai-dev`) accessible for AI node execution
- Existing `sprk_analysistool`, `sprk_promptfragment`, `sprk_systemprompt`, `sprk_content` records populated in dev
- Document Profile playbook exists with canvas JSON designed (or will be designed during Phase 7)

### External Dependencies

- Azure OpenAI token availability (for AI node execution)
- Dataverse Web API performance (scope queries depend on Dataverse response time)

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Out of scope | What specific areas should be explicitly excluded? | No new PCF controls, no staging/prod deployment, no Playbook Builder PCF visual changes, no Office add-in changes, no new AI tool handlers. Dataverse schema changes ARE in scope if needed. | Scope boundary is clear — dev environment only, no new controls. Dataverse schema gaps (fields, option sets, N:N relationships) will be addressed as encountered. |
| Canvas sync trigger | Should canvas-to-node sync be a new endpoint or hook into existing PUT? | Hook into existing `PUT /api/ai/playbooks/{id}/canvas` | No new endpoint needed — extend SaveCanvasLayout to call SyncCanvasToNodesAsync after saving canvas JSON. Simpler client integration (no additional API call from PCF). |
| Node failure policy | What happens when one AI node fails during parallel execution? | Continue with available results — skip failed node, Deliver Output renders with partial data, mark analysis as "Completed with warnings" | Deliver Output must handle missing template variables gracefully (Handlebars `{{#if}}` guards). PlaybookOrchestrationService continues batch even if one node errors. Final statuscode uses a "Completed with warnings" state if any node failed. |

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **Status codes**: Assuming standard Dataverse statuscode values: 1 = Draft, 100000001 = In Progress, 2 = Completed, 100000002 = Error. A "Completed with warnings" state may need a new custom statuscode option value — will verify against existing schema and add if missing (Dataverse schema changes are in scope).
- **Caching strategy**: Assuming no Redis caching for scope resolution in initial implementation. IMemoryCache only for handler metadata. If latency exceeds NFR-01 threshold, will add Redis caching as a follow-up.
- **Handlebars template safety**: Assuming Handlebars.NET handles missing variables gracefully (outputs empty string). Will verify during Deliver Output testing with partial data scenarios.
- **SprkChat context passing**: Assuming `data` parameter in `pane.navigate()` correctly passes analysisId and documentId to SprkChat. Will verify with existing SprkChat implementation.
- **DI registration count**: Assuming adding AiAnalysisNodeExecutor + AppOnlyDocumentAnalysisJobHandler keeps total ≤15 non-framework registrations. AiAnalysisNodeExecutor registers as `INodeExecutor` (assembly-scanned pattern), so may not count.

## Unresolved Questions

- [ ] Exact Dataverse statuscode values for "In Progress" and "Error" — need to verify against current entity definition. Blocks: FR-16 statuscode transitions.
- [ ] Whether "Completed with warnings" requires a new statuscode option value or can reuse existing — Blocks: Node failure handling in FR-11/FR-16.
- [ ] Whether N:N relationship table names (`sprk_playbooknode_skill`, `sprk_playbooknode_knowledge`) match actual Dataverse schema — Blocks: FR-13 ResolveNodeScopesAsync implementation. Will verify via Dataverse query during implementation.

## Execution Model

### Autonomous Execution with Parallel Task Agents

This project is designed for **fully autonomous execution** using Claude Code with `--dangerously-skip-permissions`. No human confirmation prompts during task execution.

**Task decomposition must maximize parallelism:**
- Tasks that modify different files or independent code paths MUST be marked as parallelizable
- The `task-create` skill should group independent tasks into parallel batches where possible
- Focus Areas 1, 2, and 3 have internal parallelism opportunities (see design.md Phase Overview):
  - **Phases 2, 3, 4** (Skill, Knowledge, Action resolution) — fully parallel, independent scope types
  - **Phase 6** (Handler Discovery API) — can overlap with Phases 7-9
  - **Phase 10** (client-side UX) — can overlap with Phase 9 (server-side)

**Task agent configuration:**
- Use `Task` tool subagents for parallel task execution where tasks touch different files
- Each task agent operates independently with its own context window
- Tasks must be self-contained: include all file paths, patterns, and constraints needed to execute without cross-referencing other in-flight tasks
- Use `effort: medium` for standard implementation tasks, `effort: high` for architecture-critical tasks (execution wiring, scope resolution)

**Permission model:**
- Run with `--dangerously-skip-permissions` for uninterrupted autonomous execution
- All file edits, bash commands, and tool invocations proceed without confirmation
- Quality gates (code-review, adr-check) still execute at task Step 9.5 but do not block on human approval

---

*AI-optimized specification. Original design: projects/ai-playbook-builder-r2/design.md*
