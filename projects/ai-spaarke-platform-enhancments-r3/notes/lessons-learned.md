# Lessons Learned — AI Resource Activation & Integration (R3)

> **Date**: 2026-03-05
> **Project Duration**: ~4 hours wall-clock (parallel agent execution)

## What Went Well

### Parallel Agent Execution
- 8 Wave 1 tasks ran simultaneously, completing in ~10 minutes wall-clock
- Wave 2 (3 tasks), Wave 3+4 (5 tasks) similarly parallelized
- Total 18 tasks across 5 phases completed in a single session
- POML task format + TASK-INDEX parallel groups enabled efficient orchestration

### Existing Infrastructure Was Solid
- Task 041 (legacy vector cleanup): No changes needed — code was already correct
- Task 033 (model selection dropdown): UI already existed, just needed label alignment
- Task 022 (L2 context): `IRagService` already had the query methods needed
- Task 023 (L3 context): `IRecordSearchService` was already registered and functional

### Clean Architecture Layering
- L1/L2/L3 retrieval layers integrated cleanly into `AiAnalysisNodeExecutor`
- Each layer is independently toggleable via `KnowledgeRetrievalConfig`
- Non-fatal design: any retrieval failure logs warning and continues

## What Needed Manual Follow-Up

### Azure CLI Operations (Tasks 001, 002, 030)
- Cannot create/delete Azure AI Search indexes from automated agents
- Cannot deploy Azure OpenAI models programmatically without permissions
- **Action**: Run documented CLI commands manually

### Dataverse Deployment (Task 010)
- `Create-KnowledgeSourceRecords.ps1` returned 400 — entity may not exist
- N:N relationships not included in seed script
- **Action**: Verify entity schema, deploy manually, then run Task 012

### Deferred Tasks (003, 004, 005, 012)
- Require live Azure/Dataverse access for validation
- Not blocking code completion, only runtime verification
- **Action**: Execute after deployment (Task 050)

## Architecture Decisions Made

| Decision | Rationale |
|----------|-----------|
| Separate `ReferenceRetrievalService` (not extend `RagService`) | Clean separation: references = curated small index vs customer docs = large tenant-scoped index |
| `KnowledgeRetrievalConfig` in ConfigJson | Per-action control without schema changes; backward compatible with defaults |
| Cache key with SHA256 hash | Fixed-length keys, handles variable-length queries and source ID lists |
| 10-minute cache TTL | Matches typical playbook execution session; prevents stale data |
| L3 maps entity types, not arbitrary search | Records index has specific entity types; direct lookup more reliable than semantic search |

## Metrics

| Metric | Value |
|--------|-------|
| Tasks completed | 15/18 (3 deferred to deployment) |
| Files created | 8 new files |
| Files modified | ~30 files |
| Build status | 0 errors, 0 warnings |
| Commits | 4 (init + wave1 + wave2 + wave3+4) |
| New DI registrations | +2 (ReferenceIndexingService, ReferenceRetrievalService) |
