# Email Communication Solution R3 - AI Context

> **Purpose**: This file provides context for Claude Code when working on email-communication-solution-r3.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Implementation (Wave 0 — Foundations)
- **Last Updated**: 2026-06-05
- **Current Task**: None active (pipeline complete; ready for Wave 0 task 001)
- **Next Action**: Run `/task-execute` on task 001 (create ADR-033) to begin Wave 0

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (27 FRs, 9 NFRs, the implementation source of truth)
- [`design.md`](design.md) — Original human design document (background + rationale)
- [`README.md`](README.md) — Project overview + graduation criteria
- [`plan.md`](plan.md) — Wave decomposition + WBS + critical path
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — All tasks + status + parallel groups + dependency graph

### Project Metadata
- **Project Name**: email-communication-solution-r3
- **Type**: Client-side consolidation (React engine + Code Page) + surgical BFF additions + Dataverse schema + docs
- **Complexity**: High (cross-cuts BFF, shared lib, LegalWorkspace, Dataverse customization, 13 docs)
- **Branch**: `work/email-communication-solution-r3` (worktree-isolated)
- **Parent project**: [email-communication-solution-r2](../email-communication-solution-r2/) (server-side, completed 2026-04)
- **Driver assessment**: [Communication Architecture Assessment 2026-06-05](../../docs/assessments/communication-architecture-assessment-2026-06-05.md)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements, acceptance criteria (canonical FR-XX numbers)
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the surface being modified (auto-loaded via `adr-aware` per task tags)
6. **Cross-check empirical findings** below — they adjust the spec's assumed baseline

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke the `task-execute` skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" / "keep going" / "next task" | Read `TASK-INDEX.md`, find first 🔲, invoke task-execute |
| "continue with task X" / "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load `current-task.md`, invoke task-execute |

**Implementation**: When user triggers task work, invoke the Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

The `task-execute` skill ensures:
- ✅ Knowledge files loaded (ADRs, constraints, patterns)
- ✅ Context tracked in `current-task.md`
- ✅ Proactive checkpointing every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5
- ✅ Progress recoverable after compaction
- ✅ PCF version bumping + deployment skills invoked correctly

**Bypassing this skill leads to**: missing ADR constraints, lost progress after compaction, skipped quality gates, manual errors.

### Parallel Task Execution

Tasks marked `<parallel-safe>true</parallel-safe>` with satisfied dependencies can run concurrently. Each task STILL uses `task-execute`. Pattern: ONE message with MULTIPLE Skill tool invocations (one per task). Max concurrency: 6 agents per wave (per `project-pipeline` Step 5).

**Sub-Agent Write Boundary (CRITICAL)**: Sub-agents CANNOT write to `.claude/` paths. Tasks touching `.claude/` are marked `<parallel-safe>false</parallel-safe>` and MUST run from the main session. Affected tasks: 001 (ADR-033), 086, 087, 092. See root CLAUDE.md §3.

---

## 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files, Claude Code MUST:
1. **Decompose into dependency graph** (group by module; identify dependencies)
2. **Delegate to subagents in parallel where safe** (different modules; no shared imports)
3. **Serialize when** files have tight coupling or one must be created before another uses it

Common in this project:
- Wave 1 engine + sub-components: serialize engine first (010, 011), then parallelize sub-components (012–019), then wrappers (021–023), then mode logic (024–027), then tests (028)
- Wave 2 Code Page steps: serialize (030 → 031 → 032 → 033 → 034 → 036), then parallelize entry surfaces (037, 038, 040) and UI tests (041–045)
- Wave 6 documentation: parallelize independent file targets (4–6-way); serialize `.claude/`-writing tasks (main-session-only)

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for protocol.

---

## Key Technical Constraints

### From Spec (binding rules)

- **MUST** use `<EmailComposer />` engine (via `<SendEmailStep />`, `<SendEmailDialog />`, `<SendEmailPage />`) for any email-send UX
- **MUST** use `sendCommunication()` typed wrapper for any programmatic email send (no UI)
- **MUST** inject `authenticatedFetch` via props in shared library components (no direct `@spaarke/auth` import)
- **MUST** register Communication services via `CommunicationModule` only (ADR-010)
- **MUST** use ProblemDetails error responses on BFF; `SendCommunicationError` parses them (ADR-019)
- **MUST** use Fluent UI v9 for all composer styling (ADR-021)
- **MUST** use `@spaarke/auth` v2 bootstrap in the EmailComposer Code Page (ADR-028)
- **MUST** use `code-page-deploy` skill conventions for the Code Page
- **MUST NOT** fork email-touching components in LegalWorkspace — re-export from shared lib
- **MUST NOT** call `fetch` to `/api/communications/send` directly — use `sendCommunication()` or a wrapper
- **MUST NOT** self-bootstrap MSAL — use `@spaarke/auth`
- **MUST NOT** touch `/api/v1/emails/*` path — out of scope
- **MUST NOT** use OOB Dataverse `email` activities
- **MUST NOT** directly import `@spaarke/auth` in shared library components
- **MUST NOT** add a 6th client-side send-email implementation; if a new mount emerges, add a new wrapper

### Performance (NFR-08)
- Composer cold-load render: < 500 ms
- Recipient autocomplete: < 200 ms per keystroke (after warm cache)
- Attachment upload progress UI: shown for files > 5 MB

### Accessibility (NFR-09)
- Fluent v9 patterns per ADR-021
- Full keyboard nav across all composer controls
- Screen-reader announcements for validation errors (live region)
- Focus management on mode transitions (e.g., Reply pre-focuses subject)

---

## Empirical Findings (Pre-Flight Verified) — IMPORTANT DELTAS FROM SPEC

These adjust the spec's implicit baseline. Task POMLs reference them; do not re-investigate.

| # | Spec assumption | Reality (verified 2026-06-05) | Implication |
|---|---|---|---|
| 1 | `EmailComposer/` is NEW | ✅ Confirmed absent | Wave 1 builds from scratch |
| 2 | `communicationApi.ts` has `sendCommunication()` | ✅ Has it; **missing** `SendCommunicationError` export + `attachmentDriveItemIds` field | Wave 0 task 005 is additive only |
| 3 | `SendCommunicationRequest.cs` needs `AttachmentDriveItemIds` added | ✅ Confirmed (only `AttachmentDocumentIds` present) | Wave 0 task 002 non-breaking alias |
| 4 | `CommunicationService.cs` does NOT capture `Internet-Message-Id` | ✅ Confirmed | Wave 0 task 003 (UQ3 spike → implement) |
| 5 | DocumentEmailWizard line 494 has latent bug | ✅ Confirmed at exact line | Wave 5 task 075 |
| 6 | SummarizeFilesDialog has inline fetch | ✅ Confirmed at line 436 | Wave 4 task 060 |
| 7 | `sprk_communication_send.js` ~600 LOC | ✅ Actually ~1,150 LOC × 2 copies (~2.3K total) | Wave 6 retirement is larger than spec stated; reflect in PR descriptions |
| 8 | `WorkAssignmentWizardDialog.tsx:31` cross-package import | ✅ Confirmed: `import { SendEmailStep } from '../CreateRecordWizard/steps/SendEmailStep';` | Wave 5 task 073 |
| 9 | LegalWorkspace forks **5 wizards** | ⚠ **DELTA**: only `CreateMatter/SendEmailStep.tsx` is a true fork. Project/Event/Todo/WorkAssignment LegalWorkspace dirs exist but have NO SendEmailStep | Wave 5 scope smaller than spec implied; task 074 explicitly notes this |
| 10 | Code Page exemplar | ✅ `src/client/code-pages/DocumentRelationshipViewer/` present (22 files) | Wave 2 follows this pattern |
| 11 | `CommunicationEndpoints.cs` path | ✅ Confirmed at `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` (NOT `Services/Communication/Api/`) | Task POMLs use correct path |
| 12 | Shared `SendEmailStep` location | At `src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/steps/SendEmailStep.tsx` (consumed by Project/Event/Todo/WorkAssignment) | Wave 5 task 071 replaces THIS file's body |

---

## Decisions Made

<!-- Updated as project progresses by task-execute. Format: Date | Decision | Rationale | Source -->

| Date | Decision | Rationale | Source |
|---|---|---|---|
| 2026-06-05 | Build engine in `@spaarke/ui-components` (not new package) | Lives with wizards/dialogs; no new package boundary | spec Affected Areas |
| 2026-06-05 | 3 thin wrappers, not direct engine usage | Wrappers carry semantic prop API | Owner Clarifications |
| 2026-06-05 | Standalone Code Page (not embedded entity-form control) | ADR-026; matches DocumentRelationshipViewer | ADR-026 |
| 2026-06-05 | Form Component Control swap + retain standard form as admin fallback | Reversibility (NFR-07) | Owner Clarifications |
| 2026-06-05 | React 18 only — no PCF React 16 compat | No PCF mounts composer directly | NFR-01 |
| 2026-06-05 | Non-breaking BFF DTO alias for `AttachmentDriveItemIds` rename | Concurrent caller migration | NFR-05 |
| 2026-06-05 | Single PR for 5-wizard migration (Wave 5) | Minimizes CI/CD overhead | Owner Clarifications |
| 2026-06-05 | Best-effort `Internet-Message-Id` retrieval (does not block send) | Send-path stability | FR-22 |

---

## Implementation Notes

<!-- Updated by task-execute. Add gotchas, workarounds, important learnings here. -->

*No notes yet — first task starts in Wave 0.*

---

## Resources

### Applicable ADRs

| ADR | Title | Relevance |
|---|---|---|
| [ADR-007](../../.claude/adr/ADR-007-spe-filestore.md) | SPE-FILESTORE | Server SPE archival uses existing facade |
| [ADR-008](../../.claude/adr/ADR-008-endpoint-filters-authorization.md) | Endpoint Filters | BFF endpoints retain endpoint-filter auth |
| [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) | DI Minimalism | Communication services via `CommunicationModule` |
| [ADR-019](../../.claude/adr/ADR-019-problemdetails-error-responses.md) | ProblemDetails | `SendCommunicationError` parses ProblemDetails |
| [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) | Fluent UI v9 | Composer styling + dark mode |
| [ADR-024](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md) | Polymorphic Resolver | `IncomingAssociationResolver` consumes `Internet-Message-Id` |
| [ADR-026](../../.claude/adr/ADR-026-full-page-custom-page-standard.md) | Full-Page Custom Page | Code Page architecture standard |
| [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | Spaarke Auth v2 | Code Page auth bootstrap; shared lib injects `authenticatedFetch` |
| ADR-033 (NEW) | Communication architecture | Created in Wave 0 task 001; codifies canonical pattern |

### Related Projects
- [email-communication-solution-r2](../email-communication-solution-r2/) — Server-side Communication Service (completed 2026-04). [`notes/lessons-learned.md`](../email-communication-solution-r2/notes/lessons-learned.md) recommended reading before Wave 0.

### External Documentation
- Microsoft Graph `SendMail` API — outbound send (used by `CommunicationService.SendAsync`)
- Microsoft Graph subscriptions — inbound matching (existing R2 work, unchanged)
- Fluent UI v9 documentation (`@fluentui/react-components`) — composer + sub-component primitives
- `@spaarke/auth` v2 package — Code Page bootstrap

### Applicable Skills (auto-discoverable; bound to task tags)

| Skill | Tag triggers | Used in waves |
|---|---|---|
| `fluent-v9-component` | `react`, `fluent-ui`, `frontend` | 1 (engine + sub-components) |
| `code-page-deploy` | `code-page`, `deploy` | 2 (Code Page) |
| `dataverse-deploy` | `dataverse`, `solution`, `deploy` | 0, 2, 4, 5, 6 |
| `dataverse-create-schema` | `dataverse`, `schema`, `fields` | 0 (task 004) |
| `ribbon-edit` | `dataverse`, `ribbon` | 2 (task 037), 6 (task 080) |
| `bff-deploy` | `bff-api`, `deploy`, `azure` | 0 (task 007) |
| `adr-aware`, `adr-check` | (auto) | all waves |
| `code-review` | (auto, Step 9.5 + wrap-up) | all FULL-rigor tasks + 099 |
| `ui-test` | `pcf`, `frontend`, `e2e-test` | 2 (tasks 041–045) |
| `doc-drift-audit` | `docs`, `cleanup` | 6 (task 093) |
| `merge-to-master` | `git`, `merge` | post-099 (after wrap-up) |

---

*This file should be kept updated throughout project lifecycle. `task-execute` will append to Decisions Made and Implementation Notes sections.*
