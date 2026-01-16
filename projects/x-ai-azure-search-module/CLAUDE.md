# AI Search & Visualization Module - AI Context

> **Purpose**: This file provides context for Claude Code when working on ai-azure-search-module.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Complete
- **Last Updated**: 2026-01-12
- **Current Task**: None (project complete)
- **Next Action**: None - project graduated

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - Original design specification (permanent reference)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker

### Project Metadata
- **Project Name**: ai-azure-search-module
- **Type**: API + PCF (Full-stack feature)
- **Complexity**: High (multiple components, AI integration)

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

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

**API Development:**
- MUST use .NET 8 Minimal API (no Azure Functions) - per ADR-001
- MUST use endpoint filters for authorization, not global middleware - per ADR-008
- MUST use Redis caching for embeddings - per ADR-009
- MUST extend BFF API, no separate microservice - per ADR-013
- MUST apply `ai-standard` rate limit policy

**PCF Development:**
- MUST use PCF control, no legacy JavaScript webresources - per ADR-006
- MUST use Fluent UI v9 exclusively, no v8 - per ADR-021
- MUST support dark mode via Fluent design tokens - per ADR-021
- MUST use React 16 APIs (`ReactDOM.render`, not `createRoot`) - per ADR-022
- MUST declare `platform-library` in manifest for React and Fluent - per ADR-022
- PCF bundle MUST be < 5MB (excluding platform libraries)

**Component Reuse:**
- Reuse existing SPE file viewer (open via fileUrl in new tab)
- Reuse existing Dataverse navigation (Xrm.Navigation.openForm)
- Reuse existing R3 RAG services (IRagService, IEmbeddingCache)
- Reuse existing auth flows (JWT tokens)

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

| Date | Decision | Rationale | Approved By |
|------|----------|-----------|-------------|
| 2026-01-08 | Continue R3 architecture (not Foundry IQ) | SharePoint Embedded not supported by Foundry IQ; R3 is battle-tested | Design review |
| 2026-01-08 | Full-screen modal for visualization | Maximum canvas area for complex graphs (25+ nodes) | Design review |
| 2026-01-08 | d3-force for layout | Natural clustering, similarity = edge distance, interactive | Design review |
| 2026-01-08 | Dedicated documentVector field | Optimal query performance; aggregation fallback for existing data | Design review |
| 2026-01-08 | Backfill existing documents | Owner clarification: yes, backfill existing indexed documents | Owner |

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

### Key Implementation Notes (2026-01-12)

1. **Bundle Size**: PCF bundle reduced from 24.4MB to 6.65MB via platform-library externalization
2. **Vector Dimensions**: Using 3072-dim vectors with `GetBestVector()` fallback to 1536-dim for migration
3. **Orphan Files**: Files without Dataverse records supported via nullable `documentId`, required `speFileId`
4. **Architecture Change**: Implemented inline section-based visualization instead of ribbon button + modal
5. **Test Coverage**: 85 .NET tests + 40 PCF tests + 18 E2E tests = 143 total tests

See [lessons-learned.md](lessons-learned.md) for comprehensive project retrospective.

---

## Resources

### Applicable ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| ADR-006 | PCF Over WebResources | DocumentRelationshipViewer must be PCF |
| ADR-008 | Endpoint Filters | VisualizationAuthorizationFilter |
| ADR-009 | Redis-First Caching | Embedding cache strategy |
| ADR-013 | AI Architecture | Extend BFF API pattern |
| ADR-021 | Fluent UI v9 Design System | All UI components, dark mode |
| ADR-022 | PCF Platform Libraries | React 16 APIs, platform-library declarations |

### Applicable Patterns

| Pattern | Location | Purpose |
|---------|----------|---------|
| Endpoint definition | `.claude/patterns/api/endpoint-definition.md` | API endpoint structure |
| Endpoint filters | `.claude/patterns/api/endpoint-filters.md` | Authorization filter |
| Control initialization | `.claude/patterns/pcf/control-initialization.md` | PCF setup |
| Theme management | `.claude/patterns/pcf/theme-management.md` | Fluent UI theming |
| AI patterns | `.claude/patterns/ai/` | AI service patterns |

### Available Scripts

| Script | Purpose |
|--------|---------|
| `scripts/Deploy-PCFWebResources.ps1` | Deploy PCF to Dataverse |
| `scripts/Export-EntityRibbon.ps1` | Export ribbon for customization |
| `scripts/Test-SdapBffApi.ps1` | Test API endpoints |

### External Documentation

- [React Flow Documentation](https://reactflow.dev/)
- [d3-force Documentation](https://github.com/d3/d3-force)
- [Azure AI Search Vector Search](https://learn.microsoft.com/en-us/azure/search/vector-search-overview)
- [Fluent UI v9 Components](https://react.fluentui.dev/)

---

*This file should be kept updated throughout project lifecycle*
