# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-01-10
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

<!-- This section is for FAST context restoration after compaction -->
<!-- Must be readable in < 30 seconds -->

| Field | Value |
|-------|-------|
| **Task** | Fix documentVector gap in RAG indexing |
| **Step** | Completed |
| **Status** | completed |
| **Next Action** | Re-upload a document via RAG, then test visualization (Task 023) |

### Files Modified This Session
<!-- Only files touched in CURRENT session, not all time -->
- `src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs` - Modified - Added documentVector field
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` - Modified - Auto-compute documentVector during indexing

### Critical Context
<!-- 1-3 sentences of essential context for continuation -->
**RAG documentVector gap FIXED.** The root cause of 404 errors for new documents was that RAG ingestion created chunks with `contentVector` but NOT `documentVector`. Modified `IndexDocumentsBatchAsync` to automatically compute `documentVector` by averaging chunk vectors with L2 normalization. Single-chunk documents also handled in `IndexDocumentAsync`. **Existing documents still need backfill** (run `DocumentVectorBackfillService`) OR re-upload documents. Next: Re-upload a test document and verify visualization works.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 021 |
| **Task File** | tasks/021-create-ribbon-button.poml |
| **Title** | Create ribbon button command |
| **Phase** | 3: Integration & Ribbon |
| **Status** | not-started |
| **Started** | — |
| **Rigor Level** | FULL |
| **Rigor Reason** | Ribbon customization requires careful XML editing |

---

## Progress

### Completed Steps

<!-- Updated by task-execute after each step completion -->
<!-- Format: - [x] Step N: {description} ({YYYY-MM-DD HH:MM}) -->

*Task 021 not started yet*
*Previous: Task 020 (Register PCF on form) completed - PCF on sprk_document Search tab*

### Current Step

Waiting to start Task 021

### Files Modified (All Task)

<!-- Track all files created or modified during this task -->
<!-- Format: - `path/to/file` - {Created|Modified} - {brief purpose} -->

*None yet - Task 021 not started*

### Decisions Made

<!-- Log implementation decisions for context recovery -->
<!-- Format: - {YYYY-MM-DD}: {Decision} — Reason: {why} -->

*None yet*

---

## Blockers

<!-- List anything preventing progress -->

**Status**: None

No blockers. Phase 1 complete, ready for Phase 2.

---

## Next Action

**Continue Phase 3: Integration & Ribbon**

To start Task 021:
- Say "work on task 021" or "continue"
- This will create a ribbon button to launch the visualization modal

**Phase 2 Complete** (10/10 tasks):
- All Phase 2 tasks ✅

**Phase 3 Progress** (1/5 tasks):
- Task 020: ✅ Register PCF on sprk_document form - DONE
- Task 021: 🔲 Create ribbon button command - NEXT
- Task 022: 🔲 Implement modal dialog launcher
- Task 023: 🔲 End-to-end testing in Dataverse
- Task 024: 🔲 Deploy Phase 3 ribbon

---

## Session Notes

<!-- Free-form notes for current session context -->
<!-- These persist across compaction for context recovery -->

### Current Session
- Started: 2026-01-09
- Focus: Tasks 012-016 completion (DocumentNode, DocumentEdge, ControlPanel, NodeActionBar, RelationshipViewerModal)

### Key Learnings
<!-- Gotchas, warnings, or important discoveries -->

- Task 001: Schema updates to Azure AI Search can be done without reindexing when adding new fields
- Task 002: Follow IRagService.cs pattern - DTOs and interface in single file with XML docs
- Task 003: Use DataverseOptions.EnvironmentUrl for building Dataverse record URLs
- Task 003: VisualizationDocument internal model needed to query documentVector field
- Task 004: Follow AiAuthorizationFilter pattern - use IAiAuthorizationService for document access
- Task 004: Extract oid claim for Dataverse user lookup
- Task 005: Follow RagEndpoints.cs pattern for endpoint structure
- Task 005: Use [AsParameters] for query parameter binding
- Task 006: BackgroundService pattern with configuration options (DocumentVectorBackfillOptions)
- Task 006: SearchAsync returns Response<SearchResults<T>> - use .Value for typed access or implicit conversion
- Task 006: Average pooling with L2 normalization for document embeddings
- Task 007: Use `SearchModelFactory.SearchResult<T>` for mocking Azure Search responses
- Task 007: VisualizationDocument accessible via InternalsVisibleTo
- Task 008: Kiota package versions must be consistent - transitives can conflict with direct refs
- Task 008: When updating Kiota packages, add explicit refs for ALL Kiota packages (Abstractions, Authentication.Azure, Http.HttpClientLibrary, Serialization.*)
- Task 008: Dataverse__ClientSecret config must be set in App Service for options validation
- Task 010: PCF with platform libraries - use ReactControl interface, returns React elements from updateView()
- Task 010: featureconfig.json with pcfReactPlatformLibraries: on enables React/Fluent externalization
- Task 010: Empty dependencies in package.json - React/Fluent in devDependencies only per ADR-022
- Task 010: FluentProvider wrapper at component root for theme resolution (ADR-021)
- Task 010: Use nullish coalescing (??) not logical or (||) to satisfy ESLint rules
- Task 011: @xyflow/react requires React 17+ - use react-flow-renderer v10.3.17 for React 16 compatibility
- Task 011: d3-force simulation: forceLink with distance = 200 * (1 - similarity), forceManyBody strength=-300
- Task 011: forceCollide prevents node overlap with configurable radius (default 50)
- Task 011: useForceLayout hook runs simulation on mount and when nodes/edges change
- Task 011: Fix source node at center (fx/fy) while letting others float
- Task 011: Bundle size increases to 1.4 MiB with react-flow-renderer + d3-force bundled
- Task 012: Handle component className prop not supported in react-flow-renderer v10 - use inline styles instead
- Task 012: makeStyles border* properties not valid - use shorthand 'border' instead of borderColor/Width/Style
- Task 013: react-flow-renderer v10 lacks BaseEdge and EdgeLabelRenderer - use path + foreignObject instead
- Task 013: getBezierPath returns string (not tuple) in v10, use getEdgeCenter for label positioning
- Task 015: Xrm.Navigation.openForm for opening Dataverse records - requires global type declaration
- Task 015: window.open with noopener,noreferrer for secure external navigation to SharePoint
- Task 015: NodeActionBar can be conditionally rendered based on selected node state
- Task 016: Full-screen modal requires z-index 10000+ for Dataverse form compatibility
- Task 016: Use setTimeout to get container dimensions after modal opens (DOM needs to be ready)
- Task 016: Custom modal overlay with click-outside-to-close is simpler than Fluent Dialog for full-screen
- Task 017: @testing-library/react v12.1.5 required for React 16 compatibility (v13+ requires React 18)
- Task 017: Jest 29 with ts-jest works well for TypeScript PCF projects
- Task 017: Mock react-flow-renderer entirely to avoid memory issues during testing (heavy bundle)
- Task 017: Global Xrm mock in jest.setup.ts enables Dataverse navigation testing
- Task 017: identity-obj-proxy handles CSS module mocking for makeStyles
- Task 019: pac pcf push creates PowerAppsToolsTemp_sprk temporary solution - for dev testing only
- Task 019: Need to disable Directory.Packages.props before pac pcf push (CPM conflict)
- Task 019: Control deployed at Spaarke.Controls.DocumentRelationshipViewer namespace
- Task 020: PCF registration on form is UI-based task in Power Apps Maker portal
- Task 020: Virtual control type allows PCF to bind to entity primary key
- Task 020: Static values for tenantId and apiBaseUrl configured in control properties
- Task 020: Control renders placeholder when document context not available
- API Integration: Create types/api.ts mirroring C# API response models from IVisualizationService.cs
- API Integration: Use Record<string, string> for headers to satisfy ESLint dot-notation rule
- API Integration: Type cast JSON responses with `as` to satisfy ESLint unsafe-any rules
- API Integration: Use RegExp.exec() instead of string.match() per ESLint prefer-regexp-exec rule
- API Integration: Use `void` keyword for floating promises in useEffect
- API Integration: Map API node.data.label to PCF node.data.name, node.type==="source" to isSource
- API Integration: Extract file type from document name using regex or fallback to documentType mapping
- DocumentVector Fix: Root cause of 404 - RAG created contentVector but NOT documentVector on new documents
- DocumentVector Fix: KnowledgeDocument model needed documentVector field (was only in BackfillDocument internal model)
- DocumentVector Fix: IndexDocumentsBatchAsync now groups chunks by DocumentId and computes averaged documentVector
- DocumentVector Fix: Use L2 normalization after averaging for cosine similarity compatibility
- DocumentVector Fix: Single-chunk documents in IndexDocumentAsync get documentVector = contentVector
- DocumentVector Fix: Existing docs require backfill OR re-upload; new docs are now auto-computed

### Handoff Notes
<!-- Used when context budget is high or session ending -->
<!-- Another Claude instance should be able to continue from these notes -->

**DocumentVector Gap FIXED (2026-01-10).** User tested visualization and got 404 because new documents didn't have `documentVector`. Root cause: RAG ingestion created chunk `contentVector` but visualization API requires `documentVector` (document-level embedding). Fixed by:
1. Added `documentVector` field to `KnowledgeDocument.cs` (was only in internal BackfillDocument)
2. Modified `IndexDocumentsBatchAsync` to auto-compute `documentVector` when all chunks are indexed together
3. Added single-chunk support to `IndexDocumentAsync` (sets documentVector = contentVector)
4. 88 unit tests pass, build succeeds

**To test the fix**: Deploy updated API to Azure, then either:
- Re-upload/re-analyze a document (will get documentVector computed)
- OR run DocumentVectorBackfillService for existing documents

**Previous Session Context**:
- API Integration Complete (v1.0.17 deployed) - PCF calls BFF API
- Tasks 021-022 (ribbon button + modal) are NO LONGER RELEVANT - control is section-based
- API Endpoint: `GET /api/ai/visualization/related/{documentId}?tenantId={tenantId}&threshold=0.65&limit=25&depth=1`
- Control requires tenantId in form control properties

---

## Quick Reference

### Project Context
- **Project**: ai-azure-search-module
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
<!-- From task constraints -->
- ADR-006: PCF over webresources
- ADR-008: Endpoint filters for authorization
- ADR-009: Redis-first caching
- ADR-013: AI Architecture
- ADR-021: Fluent UI v9 Design System
- ADR-022: PCF Platform Libraries

### Knowledge Files Loaded
<!-- From task knowledge section -->
- `.claude/skills/azure-deploy/SKILL.md`
- `docs/architecture/auth-azure-resources.md`

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read Active Task and Progress sections
3. **Load task file**: `tasks/{task-id}-*.poml`
4. **Load knowledge files**: From task's `<knowledge>` section
5. **Resume**: From the "Next Action" section

**Commands**:
- `/project-continue` - Full project context reload + master sync
- `/context-handoff` - Save current state before compaction
- "where was I?" - Quick context recovery

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
