# SDAP Office Integration - AI Context

> **Purpose**: This file provides context for Claude Code when working on sdap-office-integration.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Complete
- **Last Updated**: 2026-01-20
- **Completed**: 2026-01-20
- **Current Task**: None - Project Complete
- **Next Action**: None - All 56 tasks completed

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - Design specification (permanent reference, 6000+ words)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan with 7 phases
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (will be created by task-create)

### Project Metadata
- **Project Name**: sdap-office-integration
- **Type**: Office Add-in (Outlook + Word) + API
- **Complexity**: High (multiple hosts, background jobs, auth flows)
- **Estimated Effort**: 45-60 days

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

### Authentication (NAA)
- MUST use Nested App Authentication as primary method (MSAL.js 3.x)
- MUST implement Dialog API fallback for unsupported clients
- MUST NOT use legacy Exchange tokens (getCallbackTokenAsync, getUserIdentityTokenAsync)

### Manifests
- **Outlook**: Unified JSON manifest (GA, production-ready)
- **Word**: XML add-in-only manifest (unified is preview for Word)

### API Patterns
- MUST use Minimal API for `/office/*` endpoints (ADR-001)
- MUST use BackgroundService + Service Bus for async jobs (ADR-001)
- MUST route all SPE operations through SpeFileStore facade (ADR-007)
- MUST use endpoint filters for resource authorization (ADR-008)
- MUST return ProblemDetails for all errors with OFFICE_001-015 codes (ADR-019)

### UI/Frontend
- MUST use Fluent UI v9 exclusively (`@fluentui/react-components`)
- MUST use design tokens for all colors/spacing (no hard-coded values)
- MUST support dark mode and high-contrast
- MUST wrap task pane in FluentProvider with theme
- MUST meet WCAG 2.1 AA accessibility requirements

### Data
- Document MUST be associated to exactly ONE of: Matter, Project, Invoice, Account, Contact
- "Document Only" saves are NOT allowed
- Use idempotency keys (SHA256 of canonical payload)

### Office.js Requirements
- Outlook: Mailbox 1.8+ (for getAttachmentContentAsync)
- Word: WordApi 1.3+
- Always check capability at runtime with isSetSupported()

---

## Applicable ADRs

| ADR | Title | Key Constraint |
|-----|-------|----------------|
| **ADR-001** | Minimal API + BackgroundService | No Azure Functions, use Service Bus |
| **ADR-004** | Async Job Contract | Standard job schema, idempotent handlers |
| **ADR-007** | SpeFileStore Facade | No Graph SDK types in DTOs |
| **ADR-008** | Endpoint Filters | Authorization via filters, not middleware |
| **ADR-010** | DI Minimalism | ≤15 non-framework DI registrations |
| **ADR-012** | Shared Component Library | Use @spaarke/ui-components |
| **ADR-019** | ProblemDetails | RFC 7807 error responses |
| **ADR-021** | Fluent UI v9 Design System | Design tokens, dark mode required |

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-01-20 | XML manifest for Word, Unified for Outlook | Unified manifest is preview for Word; XML is production-ready |
| 2026-01-20 | SSE via fetch+ReadableStream | Native EventSource doesn't support Authorization headers |
| 2026-01-20 | Client-side attachment retrieval only (V1) | Server-side requires Mail.Read scope and additional complexity |
| 2026-01-20 | No "Document Only" saves | Business rule: all documents must be associated to an entity |

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

### Authentication Notes
- NAA uses `createNestablePublicClientApplication()` from MSAL.js 3.x
- Fallback path uses Office Dialog API for auth popup
- Token acquisition: silent first, then interactive popup

### SSE Notes
- Native EventSource cannot send custom headers
- Use fetch + ReadableStream for SSE with bearer auth
- Fallback to polling at 3-second intervals if SSE fails

### Attachment Notes
- Use `getAttachmentContentAsync()` (requires Mailbox 1.8+)
- Returns base64-encoded content
- 25MB per file limit, 100MB total per email (code-configurable)
- Server-side retrieval is out of V1 scope

---

## Resources

### Code Patterns
- `.claude/patterns/api/endpoint-definition.md` - API endpoint structure
- `.claude/patterns/api/background-workers.md` - Worker implementation
- `.claude/patterns/api/endpoint-filters.md` - Authorization filters
- `.claude/patterns/api/error-handling.md` - ProblemDetails
- `.claude/patterns/auth/msal-client.md` - MSAL configuration
- `.claude/patterns/auth/obo-flow.md` - On-behalf-of flow

### Knowledge Docs
- `docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md` - Email processing
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` - AI integration
- `docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md` - Schema creation

### External Documentation
- [NAA Documentation](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in)
- [Unified Manifest Overview](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/unified-manifest-overview)
- [Mailbox Requirement Sets](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/outlook/outlook-api-requirement-sets)

### Related Projects
| Project | Relationship |
|---------|--------------|
| SDAP-teams-app | Consumes same backend APIs |
| SDAP-external-portal | Provides invitation APIs for "Grant access" |

---

## File Structure

```
src/client/office-addins/
├── outlook/
│   └── manifest.json        # Unified manifest for Outlook
├── word/
│   └── manifest.xml         # XML manifest for Word
└── shared/
    ├── taskpane/            # Shared React task pane
    │   ├── App.tsx
    │   ├── components/
    │   ├── services/
    │   └── adapters/
    ├── api-client/          # Typed API client
    └── auth/                # NAA service

src/server/api/Sprk.Bff.Api/
├── Endpoints/
│   └── Office/              # New /office/* endpoints
├── Workers/
│   └── Office/              # Background workers
└── Services/
    └── Office/              # Office-specific services
```

---

*This file should be kept updated throughout project lifecycle.*
