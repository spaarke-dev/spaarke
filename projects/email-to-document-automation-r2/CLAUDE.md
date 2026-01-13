# Email-to-Document Automation R2 - AI Context

> **Purpose**: This file provides context for Claude Code when working on email-to-document-automation-r2.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning
- **Last Updated**: 2026-01-13
- **Current Task**: Not started
- **Next Action**: Run task-create to decompose plan into task files

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - Original design specification (permanent reference)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (will be created by task-create)

### Project Metadata
- **Project Name**: email-to-document-automation-r2
- **Type**: API Enhancement + Dataverse Ribbon
- **Complexity**: Medium-High
- **Phases**: 5 (Download, Attachments, AI Service, Playbook, UI)

### R1 Reference
- [`docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md`](../../docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md) - R1 architecture and patterns

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements, and acceptance criteria
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)
6. **Reference R1 architecture** at `docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md` for existing patterns

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5
- ✅ Progress is recoverable after compaction

**Bypassing this skill leads to**:
- ❌ Missing ADR constraints
- ❌ No checkpointing - lost progress after compaction
- ❌ Skipped quality gates

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: Tasks 020, 021, 022 in parallel → Three separate task-execute calls in one message

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

### From ADRs

| ADR | Constraint | Impact on R2 |
|-----|------------|--------------|
| ADR-001 | Minimal API pattern, no Azure Functions | Download endpoint uses Minimal API |
| ADR-004 | Standard JobContract schema | New `AppOnlyDocumentAnalysis` job follows schema |
| ADR-007 | SpeFileStore facade for all SPE ops | Download proxies through SpeFileStore |
| ADR-008 | Endpoint filters for authorization | Download auth via filter, not middleware |
| ADR-010 | DI minimalism (≤15 non-framework) | R2 adds ~2 services (within limit) |

### From Spec

- ✅ MUST use endpoint filter for download authorization (not middleware)
- ✅ MUST stream file response (not buffer in memory)
- ✅ MUST follow existing JobContract schema
- ✅ MUST use MimeKit for attachment extraction (existing dependency)
- ❌ MUST NOT make HTTP calls from Dataverse plugins

### Existing Patterns to Follow

| Pattern | Location | Use For |
|---------|----------|---------|
| `EmailToDocumentJobHandler` | Services/Jobs/Handlers/ | New job handler pattern |
| `DocumentAuthorizationFilter` | Filters/ | Download authorization filter |
| `EmailTelemetry` | Telemetry/ | Telemetry naming conventions |
| `AnalysisOrchestrationService` | Services/Analysis/ | AI service pattern (OBO reference) |

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

*No decisions recorded yet*

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

### R1 Lessons Learned (Reference)

From `docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md`:

1. **DateTime Format**: Dataverse webhooks send WCF format (`/Date(xxx)/`), not ISO 8601
2. **Field Names**: Use `sprk_mimetype` (not `sprk_filetype`)
3. **FileSize Type**: Cast to `int` (Dataverse Whole Number is int32)
4. **Container ID**: Must use Drive ID format (`b!xxx`), not raw GUID
5. **FilePath**: Set `sprk_filepath = fileHandle.WebUrl` for SPE links

---

## Resources

### Applicable ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| [ADR-001](.claude/adr/ADR-001-minimal-api.md) | Minimal API + BackgroundService | Download endpoint, new job handlers |
| [ADR-004](.claude/adr/ADR-004-job-contract.md) | Async Job Contract | AppOnlyDocumentAnalysis job type |
| [ADR-007](.claude/adr/ADR-007-spefilestore.md) | SpeFileStore Facade | Download proxying |
| [ADR-008](.claude/adr/ADR-008-endpoint-filters.md) | Endpoint Filters | Download authorization |
| [ADR-010](.claude/adr/ADR-010-di-minimalism.md) | DI Minimalism | Service registration limits |

### Related Projects
- **Email-to-Document R1**: Completed (PR #104) - Foundation for R2
- **AI Node Playbook Builder**: In progress (PR #106) - May affect playbook integration

### External Documentation
- [MimeKit Documentation](https://www.mimekit.net/) - Attachment extraction
- [Graph API DriveItem](https://learn.microsoft.com/en-us/graph/api/resources/driveitem) - SPE file access
- [Dataverse Ribbon](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/customize-commands-ribbon) - UI customization

### Key Source Files

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs` | Endpoint definitions |
| `src/server/api/Sprk.Bff.Api/Services/Email/EmailToEmlConverter.cs` | Email conversion |
| `src/server/api/Sprk.Bff.Api/Services/Email/EmailAttachmentProcessor.cs` | Attachment processing |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` | Job handler |
| `src/server/api/Sprk.Bff.Api/Services/Analysis/AnalysisOrchestrationService.cs` | AI analysis (OBO) |
| `src/server/api/Sprk.Bff.Api/Configuration/EmailProcessingOptions.cs` | Configuration |

---

*This file should be kept updated throughout project lifecycle*
