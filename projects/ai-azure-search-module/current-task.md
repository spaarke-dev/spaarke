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
| **Task** | 050 - Update embedding configuration |
| **Step** | Not started |
| **Status** | not-started |
| **Next Action** | Say "work on task 050" to update embedding configuration for 3072 dimensions |

### Files Modified This Session
<!-- Only files touched in CURRENT session, not all time -->
- `infrastructure/ai-search/spaarke-knowledge-index-v2.json` - Created v2 schema with 3072-dim vectors
- `infrastructure/ai-search/spaarke-knowledge-index-migration.json` - Schema update for existing index
- `src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs` - Added new fields + 3072 vectors
- `src/server/api/Sprk.Bff.Api/Services/Ai/IRagService.cs` - Made DocumentId nullable
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` - Updated indexing to handle new fields
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/RagServiceTests.cs` - Added tests for new field handling
- `projects/ai-azure-search-module/tasks/TASK-INDEX.md` - Updated Tasks 040-043 to ✅

### Critical Context
<!-- 1-3 sentences of essential context for continuation -->
**Phase 5a (Schema Extension) COMPLETED.** All 4 tasks (040-043) done: v2 schema created, index updated, KnowledgeDocument model has new fields (SpeFileId, FileName, FileType, 3072-dim vectors, nullable DocumentId), RagService indexing updated with validation and file metadata extraction. **Next: Phase 5b (Embedding Migration)** starting with Task 050 to update embedding configuration.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 050 |
| **Task File** | tasks/050-update-embedding-configuration.poml |
| **Title** | Update embedding configuration |
| **Phase** | 5b: Embedding Migration |
| **Status** | not-started |
| **Started** | — |
| **Rigor Level** | STANDARD |
| **Rigor Reason** | Configuration changes for embedding model |

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

**Execute Task 050: Update embedding configuration**

To start Task 050:
- Say "work on task 050" or "continue"
- This begins Phase 5b to migrate to 3072-dimension embeddings

**Phase 5: Schema Migration** (5/16 tasks):
| Phase | Tasks | Status |
|-------|-------|--------|
| 5-fix | 025 (Azure config fix) | ✅ |
| 5a | 040-043 (Schema Extension) | ✅ |
| 5b | 050-053 (Embedding Migration) | 🔲 **NEXT** |
| 5c | 060-064 (Service Updates) | 🔲 |
| 5d | 070-072 (Cutover) | 🔲 |

**Key Decisions Made**:
- Standardize on 3072 dimensions (text-embedding-3-large)
- Add new fields: `speFileId`, `fileType`, `fileName`
- Support orphan files (no linked `sprk_document`)
- Keep `documentId` (always `sprk_document`) - NOT rename to `sourceRecordId`
- Derive parent entity at runtime from `sprk_document` lookups

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

**Phase 5 Created (2026-01-10).** Investigation of visualization 500 errors revealed critical schema mismatch:
1. `Analysis__SharedIndexName` was pointing to `spaarke-records-index` (Record Matching) instead of RAG index
2. User clarified terminology: "Documents" = Dataverse `sprk_document`, "Files" = SPE content
3. User confirmed: similarity is by FILE content (full info), NOT Document metadata
4. User decided: Standardize on 3072 dims, support orphan files, add `speFileId`/`fileType`/`fileName`

**Created**:
- Analysis doc: `notes/024-index-schema-unification.md` (comprehensive investigation + plan)
- 16 new tasks across Phase 5 sub-phases (025, 040-043, 050-053, 060-064, 070-072)
- Updated TASK-INDEX.md with Phase 5 section

**IMMEDIATE PRIORITY**: Task 025 fixes Azure configuration to unblock visualization testing.

**Full Migration Plan**:
- Phase 5a: Add new fields to index schema, update C# models
- Phase 5b: Migrate embeddings from 1536→3072 dimensions via background service
- Phase 5c: Update services to use new fields, update PCF for orphan file support
- Phase 5d: Remove deprecated fields, final cutover, E2E testing

**Previous Session Context**:
- API Integration Complete (v1.0.17 deployed) - PCF calls BFF API
- Tasks 021-022 (ribbon button + modal) are DEFERRED - control is section-based
- DocumentVector gap was fixed in previous session

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
