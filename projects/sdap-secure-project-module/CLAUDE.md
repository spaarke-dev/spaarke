# Secure Project & External Access Platform — AI Context

> **Purpose**: This file provides context for Claude Code when working on sdap-secure-project-module.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning
- **Last Updated**: 2026-03-16
- **Current Task**: Not started
- **Next Action**: Execute task 001

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
- **Project Name**: sdap-secure-project-module
- **Type**: Full-stack (Dataverse + BFF API + Power Pages SPA + SPE + AI Search)
- **Complexity**: High
- **Branch**: `feature/sdap-secure-project-module`

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

### 🚨 MUST: Multi-File Work Decomposition

**For tasks modifying 4+ files, Claude Code MUST:**

1. **Decompose into dependency graph**: Group files by module/component, identify dependencies
2. **Delegate to subagents in parallel where safe**: Use Task tool with multiple invocations
3. **Parallelize when**: Files are in different modules, no shared interfaces, independent work
4. **Serialize when**: Tight coupling, shared state, sequential logic required

---

## Key Technical Constraints

### Architecture
- MUST use .NET 8 Minimal API for all BFF endpoints (ADR-001)
- MUST use endpoint filters for external caller authorization (ADR-008)
- MUST route all SPE operations through SpeFileStore facade (ADR-007)
- MUST use Redis-first caching for access data (ADR-009)
- MUST NOT create plugins for orchestration (ADR-002)
- MUST NOT use Power Automate flows
- MUST NOT use polymorphic lookups (use field resolver)
- Concrete DI registrations only; ≤15 non-framework lines per module (ADR-010)

### Frontend (Power Pages SPA)
- MUST use React 18 with createRoot (bundled, not platform-provided) (ADR-022)
- MUST use Fluent UI v9 exclusively (ADR-021)
- MUST support dark mode and high-contrast
- MUST use @spaarke/ui-components shared library (ADR-012)
- MUST NOT hard-code colors (use Fluent design tokens)
- MUST NOT use Fluent v8 or alternative UI libraries

### UAC Model
- Single custom table: `sprk_externalrecordaccess` (participation junction)
- Power Pages built-in tables: leverage `mspp_webrole`, `mspp_entitypermission`, `adx_invitation` — do NOT replicate
- Three-plane orchestration on grant/revoke (Dataverse records, SPE files, AI Search)
- Access levels: View Only (100000000), Collaborate (100000001), Full Access (100000002)

### Security
- Invitation-only access (no self-service registration)
- IP protection: external users see playbook outputs only, never definitions/prompts
- Email notifications via existing `sprk_communication` module (no new email service)
- AI features via toolbar buttons, NOT SprkChat (deferred to post-MVP)

---

## Applicable ADRs

| ADR | Title | Key Constraint |
|-----|-------|----------------|
| ADR-001 | Minimal API + BackgroundService | No Azure Functions; ProblemDetails for errors |
| ADR-002 | Thin Dataverse plugins | No HTTP/Graph calls in plugins |
| ADR-003 | Authorization seams | IAccessDataSource + SpeFileStore only |
| ADR-006 | PCF vs Code Pages | SPA = Code Page (React 18, bundled) |
| ADR-007 | SpeFileStore facade | No Graph SDK leaks above facade |
| ADR-008 | Endpoint filters | No global auth middleware |
| ADR-009 | Redis-first caching | No hybrid L1 cache |
| ADR-010 | DI minimalism | Concretes; ≤15 non-framework lines |
| ADR-012 | Shared component library | Reuse @spaarke/ui-components |
| ADR-013 | AI Architecture | Extend BFF; no separate AI service |
| ADR-021 | Fluent UI v9 | No hard-coded colors; dark mode required |
| ADR-022 | PCF platform libraries | Code Page: React 18 bundled |
| ADR-026 | Full-page custom page | Vite + React 18 + viteSingleFile |

## Key Knowledge Documents

| Document | Purpose |
|----------|---------|
| `docs/architecture/uac-access-control.md` | UAC three-plane model |
| `docs/architecture/power-pages-spa-guide.md` | Power Pages SPA guide |
| `docs/architecture/power-pages-access-control.md` | Table permissions, web roles, invitations |
| `docs/architecture/sdap-auth-patterns.md` | Auth flows |
| `.claude/constraints/auth.md` | Auth MUST/MUST NOT |
| `.claude/constraints/api.md` | API MUST/MUST NOT |
| `.claude/patterns/api/endpoint-definition.md` | Endpoint structure |
| `.claude/patterns/api/endpoint-filters.md` | Authorization filters |
| `.claude/patterns/webresource/full-page-custom-page.md` | Code Page template |
| `.claude/patterns/auth/graph-sdk-v5.md` | Graph SDK (SPE membership) |

## Key Code References

| Code | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Authorization/` | AuthorizationService, CachedAccessDataSource |
| `src/server/api/Sprk.Bff.Api/Api/Documents/` | Document CRUD endpoints |
| `src/client/code-pages/` | Existing Code Page examples |
| `src/client/shared/Spaarke.UI.Components/` | Shared Fluent v9 components |
| `src/solutions/LegalWorkspace/` | Corporate Workspace SPA reference |
| `src/solutions/DocumentUploadWizard/` | Document upload reference |

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-03-16 | No two-step approval flow for access grants | Owner confirmed: single-step grant, immediate provisioning |
| 2026-03-16 | View Only cannot trigger AI or download | Access level enforcement per owner clarification |
| 2026-03-16 | Full Access can invite other external users | Mainly for Core Users accessing SPA |
| 2026-03-16 | Email via existing sprk_communication | No new email infrastructure; Graph-based |
| 2026-03-16 | Reuse existing file upload pattern | No new upload UX |
| 2026-03-16 | Home page shows pre-computed summaries | No real-time AI calls on home page |
| 2026-03-16 | Removed approvedby/approveddate from table | No two-step approval needed |

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings -->

*No notes yet*

---

*This file should be kept updated throughout project lifecycle*
