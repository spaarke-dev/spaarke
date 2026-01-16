# RAG Document Ingestion Pipeline - AI Context

> **Purpose**: This file provides context for Claude Code when working on ai-RAG-pipeline.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Implementation
- **Last Updated**: 2026-01-15
- **Current Task**: Not started
- **Next Action**: Execute task 001 via task-execute

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized specification (permanent reference)
- [`design.md`](design.md) - Original design document
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker

### Project Metadata
- **Project Name**: ai-RAG-pipeline
- **Type**: API + Background Jobs + PCF Integration
- **Complexity**: Medium-High
- **Branch**: `work/ai-rag-pipeline`

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements, and acceptance criteria
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next pending) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

The task-execute skill ensures:
- Knowledge files are loaded (ADRs, constraints, patterns)
- Context is properly tracked in current-task.md
- Proactive checkpointing occurs every 3 steps
- Quality gates run (code-review + adr-check) at Step 9.5
- Progress is recoverable after compaction

**Bypassing this skill leads to**:
- Missing ADR constraints
- No checkpointing - lost progress after compaction
- Skipped quality gates

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: Tasks 020, 021, 022 in parallel - Three separate task-execute calls in one message

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

### From ADR-001 (Minimal API)
- MUST use Minimal API for `/index-file` endpoint
- MUST NOT use Azure Functions

### From ADR-004 (Job Contract)
- MUST use Job Contract schema for RagIndexingJobHandler
- MUST implement handlers as idempotent
- MUST propagate CorrelationId
- MUST NOT place document bytes in job payloads

### From ADR-007 (SpeFileStore)
- MUST access files through `SpeFileStore.DownloadFileAsync()` / `DownloadFileAsUserAsync()` only
- MUST NOT inject GraphServiceClient outside SpeFileStore

### From ADR-008 (Endpoint Filters)
- MUST use endpoint filter for authorization
- MUST NOT use global middleware for resource authorization

### From ADR-013 (AI Architecture)
- MUST extend BFF, no separate AI microservice
- MUST use Redis caching for expensive AI results

### From ADR-015 (Data Governance)
- MUST NOT log document contents or extracted text
- MUST log only identifiers, sizes, timings

### From ADR-016 (Rate Limiting)
- MUST apply rate limiting to indexing endpoint

---

## Canonical Implementations to Follow

| Pattern | Reference File | Purpose |
|---------|----------------|---------|
| Job Handler | `Services/Jobs/Handlers/AppOnlyDocumentAnalysisJobHandler.cs` | RagIndexingJobHandler pattern |
| RAG Service | `Services/Ai/IRagService.cs` | IndexDocumentsBatchAsync interface |
| API Endpoint | `Api/Ai/RagEndpoints.cs` | Endpoint registration pattern |
| Text Extraction | `Services/Ai/TextExtractorService.cs` | Text extraction service |
| Chunking Logic | `Services/Ai/Tools/SummaryHandler.cs:479-517` | Logic to extract for ITextChunkingService |

---

## Decisions Made

| Date | Decision | Rationale | Source |
|------|----------|-----------|--------|
| 2026-01-15 | Include PCF integration (Phase 5) | Owner confirmed client-side RAG call is in scope | design-to-spec interview |
| 2026-01-15 | Include Phase 4 (Document Events) | Owner confirmed DocumentEventHandler is in scope | design-to-spec interview |
| 2026-01-15 | RAG failures use silent warning | RAG is enhancement, not critical path | design-to-spec interview |
| 2026-01-15 | Chunking defaults: 4000/200/preserve | From design.md, consistent with existing handlers | design.md |

---

## Implementation Notes

### Entry Points Summary

| Entry Point | Method | Auth | Use Case |
|-------------|--------|------|----------|
| User Upload | `IndexFileAsync()` | OBO | PCF uploads via API |
| Background (Download) | `IndexFileAppOnlyAsync()` | App-only | Email automation, events |
| Background (Pre-extracted) | `IndexContentAsync()` | App-only | Optimization when text already extracted |

### Key Integration Points

1. **API Endpoint**: `POST /api/ai/rag/index-file` - OBO auth, calls `IndexFileAsync()`
2. **Job Handler**: `RagIndexingJobHandler` - App-only auth, calls `IndexFileAppOnlyAsync()` or `IndexContentAsync()`
3. **Email Integration**: `EmailToDocumentJobHandler.EnqueueRagIndexingJobAsync()` - Enqueues RAG job
4. **Event Handler**: `DocumentEventHandler.HandleDocumentCreatedAsync()` - Triggers on document events

---

## Resources

### Applicable ADRs
- [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) - Minimal API patterns
- [ADR-004](../../.claude/adr/ADR-004-job-contract.md) - Job Contract schema
- [ADR-007](../../.claude/adr/ADR-007-spefilestore.md) - SpeFileStore facade
- [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md) - Endpoint filters
- [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) - AI architecture
- [ADR-014](../../.claude/adr/ADR-014-ai-caching.md) - AI caching
- [ADR-015](../../.claude/adr/ADR-015-ai-data-governance.md) - Data governance
- [ADR-016](../../.claude/adr/ADR-016-ai-rate-limits.md) - Rate limiting
- [ADR-017](../../.claude/adr/ADR-017-job-status.md) - Job status

### Patterns
- [Endpoint Definition](../../.claude/patterns/api/endpoint-definition.md)
- [Background Workers](../../.claude/patterns/api/background-workers.md)
- [Text Extraction](../../.claude/patterns/ai/text-extraction.md)

### Related Projects
- `email-to-document-automation-r2` - Prerequisite project (complete)

### External Documentation
- [Azure AI Search](https://learn.microsoft.com/azure/search/)
- [Azure OpenAI Embeddings](https://learn.microsoft.com/azure/ai-services/openai/concepts/embeddings)

---

*This file should be kept updated throughout project lifecycle*
