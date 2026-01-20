# AI Semantic Search Foundation - AI Context

> **Purpose**: This file provides context for Claude Code when working on ai-semantic-search-foundation-r1.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning
- **Last Updated**: 2026-01-20
- **Current Task**: Not started
- **Next Action**: Execute task 001 (or next pending task in TASK-INDEX.md)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - Original design specification (permanent reference)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker

### Project Metadata
- **Project Name**: ai-semantic-search-foundation-r1
- **Type**: BFF API (AI/Search)
- **Complexity**: Medium-High
- **Key Components**: SemanticSearchService, SearchFilterBuilder, SemanticSearchToolHandler

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

**From Spec (P0 Security)**:
- MUST include `tenantId` filter in every search query
- MUST validate scope authorization before executing search
- `scope=all` NOT supported in R1 (returns 400 NotSupported)
- `scope=entity` requires UAC validation of entityType+entityId
- `scope=documentIds` requires validation of each document

**From ADRs**:
- MUST use Minimal API patterns (no Azure Functions) - per ADR-001
- MUST use endpoint filters for authorization (not global middleware) - per ADR-008
- MUST return ProblemDetails for all errors with correlation ID - per ADR-019
- MUST apply rate limiting to AI endpoints - per ADR-013
- MUST bound concurrency for upstream AI calls - per ADR-016
- DI registrations ≤15 non-framework lines - per ADR-010

**From Design Decisions**:
- Use RRF for hybrid scoring (Azure AI Search default)
- Only `combinedScore` populated for R1; `similarity` and `keywordScore` are null
- Embedding failure falls back to keyword-only with warning (no hard error)
- No result caching for R1 (always fresh)
- Entity names may be cached with 5-min TTL (reference data, not search results)

---

## Index Schema Reference

**Index**: `spaarke-knowledge-index-v2`

**Key Searchable Fields**: `content`, `documentName`, `fileName`, `knowledgeSourceName`

**Key Filterable Fields**: `tenantId`, `documentId`, `speFileId`, `documentType`, `fileType`, `createdAt`, `updatedAt`, `tags`

**Vector Fields**: `documentVector3072` (3072 dimensions), `contentVector3072`

**Required Extensions (R1)**:
- `parentEntityType` (Edm.String, filterable) - "matter", "project", "invoice", "account", "contact"
- `parentEntityId` (Edm.String, filterable) - Parent entity GUID
- `parentEntityName` (Edm.String, searchable) - Parent entity display name

---

## Decisions Made

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-01-20 | Entity-agnostic scoping | Support Matter, Project, Invoice, Account, Contact with single API |
| 2026-01-20 | RRF for hybrid scoring | Azure AI Search default, proven approach |
| 2026-01-20 | No `scope=all` in R1 | Security-first - defer until ACL strategy defined |
| 2026-01-20 | Fallback to keyword-only on embedding failure | Graceful degradation over hard errors |
| 2026-01-20 | New docs only for index migration | Dev environment acceptable; prod migration TBD |

---

## Implementation Notes

*Add notes about gotchas, workarounds, or important learnings during implementation*

*None yet*

---

## Resources

### Applicable ADRs
- `.claude/adr/ADR-001-minimal-api.md` - Minimal API patterns
- `.claude/adr/ADR-008-endpoint-filters.md` - Authorization filters
- `.claude/adr/ADR-010-di-minimalism.md` - DI patterns
- `.claude/adr/ADR-013-ai-architecture.md` - AI architecture
- `.claude/adr/ADR-016-ai-rate-limits.md` - Rate limiting
- `.claude/adr/ADR-019-problemdetails.md` - Error handling

### Patterns
- `.claude/patterns/api/endpoint-definition.md` - Endpoint patterns
- `.claude/patterns/api/endpoint-filters.md` - Auth filters
- `.claude/patterns/api/error-handling.md` - Error handling
- `.claude/patterns/auth/uac-access-control.md` - UAC patterns

### Related Code
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` - Existing search implementation
- `src/server/api/Sprk.Bff.Api/Services/Ai/IRagService.cs` - Search interface pattern
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolHandlerRegistry.cs` - Tool registration

### Related Projects
- `ai-semantic-search-ui-r2` - UI companion project (consumes this API)

### External Documentation
- [Azure AI Search Hybrid Search](https://learn.microsoft.com/en-us/azure/search/hybrid-search-overview)
- [Azure OpenAI Embeddings](https://learn.microsoft.com/en-us/azure/ai-services/openai/concepts/understand-embeddings)

---

*This file should be kept updated throughout project lifecycle*
