# Communication Workspace — R3 - AI Context

> **Purpose**: This file provides context for Claude Code when working on messaging-communication-app-r3.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Development (initialized 2026-07-20 via `/project-pipeline`)
- **Last Updated**: 2026-07-20
- **Current Task**: Not started
- **Next Action**: Execute Phase 1 task 001 (backend read/thread spine)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized implementation spec (25 FR / 8 NFR) — permanent reference
- [`design.md`](design.md) - Investigation-grounded design narrative
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Phases, WBS, critical path, discovered resources
- [`current-task.md`](current-task.md) - **Active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker + parallel groups

### Project Metadata
- **Project Name**: messaging-communication-app-r3
- **Type**: BFF (backend increment) + PCF + Code Page + Shared React libs + Dataverse schema/web-resource
- **Complexity**: High (6 phases, cross-surface, correctness-critical access model)
- **Hot-path**: BFF=Y · SpaarkeAi=Y · CI=N · Skills=N

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for requirements + acceptance criteria; **plan.md** for phase/critical-path
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)
6. **Run `/conflict-check`** before opening any PR that touches `Services/Communication/**`, `ThreadResolver.cs`, or `EmailComposer/**` — shared with r1/r2/email-r4.

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: invoke Skill tool with `skill="task-execute"` and the task file path.

### Why This Matters

task-execute ensures: knowledge files loaded (ADRs/constraints/patterns) · context tracked in current-task.md · checkpointing every 3 steps · quality gates (code-review + adr-check) at Step 9.5 · progress recoverable after compaction.

### Parallel Task Execution

Tasks in a parallel group each STILL use task-execute — one message, multiple Skill invocations. **Never** parallelize tasks marked `parallel-safe: false` (the shared-`Services/Communication/` backend edits) or any task touching `.claude/` (main-session-only per Sub-Agent Write Boundary).

---

## Execution Model & Tiering (Sonnet-5)

- **Planning** (project-pipeline Steps 0–3): Opus 4.8 / Fable 5.
- **Execution** (task-execute per task): default **Sonnet 5 @ effort `high`**. Each POML carries `<model-tier>` (`opus`/`fable` for the shared-backend `Services/Communication/` edits + access-model correctness + privilege/privacy tasks) and `<effort>` (`xhigh` for the brownfield backend-spine + privilege/privacy tasks).
- **Sonnet-5 authoring discipline**: POMLs are explicit — exact files, cite the canonical reference to copy (`CommunicationTimeline/**`, `SprkChatMessage.tsx`, `CommunicationTimelineRegarding/**`, `CommunicationThreadReadService.cs`), state exact contracts, scope every constraint, closed-set acceptance criteria incl. negative/authorization cases.

---

## Key Technical Constraints

- **Reuse the send engine** — `EmailComposer`/`SendEmailDialog` + `sendCommunication`; **no 6th send impl** (ADR-045).
- **No Dataverse plugin** — thread rename routes through a **BFF endpoint** (hard MUST NOT).
- **Impersonated + access-filtered reads; NO membership-union** (retired 2026-07-16) — correctness-critical (NFR-01).
- **Privilege/privacy never mis-displayed** — recipient list = actual permitted recipients only; never imply access a user lacks (FR-21 / NFR-01).
- **Keep type strings** — widget `communications-list`, section id `communications` (NFR-06).
- **No second** regarding mechanism (ADR-024), grid-config default, or workspace widget.
- **Fluent v9 + dark mode** via host `FluentProvider` (ADR-021, NFR-04).
- **All BFF calls** via `@spaarke/auth` `authenticatedFetch` (ADR-028).
- **BFF hygiene** — every BFF task: state Placement Justification in PR (cite `.claude/constraints/bff-extensions.md`), verify publish ≤60 MB compressed (baseline ~46 MB), 0 new HIGH CVE, update tests in `tests/unit/Sprk.Bff.Api.Tests/` (§10).
- **ADR-038 tests** — seam tests as DoD for the read endpoint (`tests/integration/seam/Communication/`); characterize `CommunicationTimeline`/`SendEmailDialog` before extending.
- **PCF prod build** — `npm run build:prod` (NOT `npm run build`).

### ADR Tensions on record (both Path C — cite in PR)
- **ADR-006** — "Email & Messages" tab uses the sanctioned DataGrid **web-resource** framework (supersedes retired PCF grid); form-`onLoad` scoping is within-framework.
- **ADR-026** — right-pane conversation PCF on OOB form = same Path-A exception R2's `CommunicationTimeline*` PCFs established.

---

## Decisions Made

*No decisions recorded yet*

---

## Implementation Notes

- Merged `origin/master` at init (2026-07-20) — R1/R2/email-r4 `Services/Communication/**` + `Spaarke.Communication.Components` are in the tree. `ListThreadsAsync`, `ConversationView`, and the notification spine do **not** yet exist (all net-new per spec).
- **Notification spine gap**: FR-22 (`communication-arrived`) is not in master; it lives in `email-communication-solution-r4/projects/spaarke-notification-spine-r1`. Keep FR-22 late (Phase 5); confirm producer/consumer contract at P1.

---

## Deferrals & Issues — tracking obligation (read this)

Track deferred work + newly-discovered issues in TWO synced places:
1. **`notes/defer-issues.md`** — source of truth
2. **GitHub Issues** on the portfolio board — visibility

Invoke `/project-defer-issue-tracking` (alias `/defer`) — writes both in one step. NEVER file to `notes/` only. §11 rule applies: every entry must name a concrete failing behavior/contract (not "flexibility"/"testability"). `push-to-github` blocks push on entries without GitHub URLs.

---

## Resources

### Applicable ADRs
- ADR-045 (send engine), ADR-046 (ACS channel), ADR-024 (regarding family), ADR-026 (Path-A PCF), ADR-028 (auth v2), ADR-038 (testing/seam), ADR-021 (Fluent v9/dark), ADR-006 (PCF-over-webresource — tension)

### Related Projects
- `messaging-communication-app-r2` (predecessor — read/query/organize; `communications-list` widget lib)
- `messaging-communication-app-r1` (transport + thread model)
- `email-communication-solution-r4` (EmailComposer / send engine / ADR-045; hosts `spaarke-notification-spine-r1`)

### External Documentation
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`, `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`
- `docs/standards/CHAT-ATTACHMENT-POLICY.md`, `MODAL-DECISION-CRITERIA.md`, `DATA-ACCESS-DECISION-CRITERIA.md`
- `.claude/constraints/bff-extensions.md`
- UX contract: `spaarke-prototype/projects/2026-07-communication-conversation-widget/`

---

*This file should be kept updated throughout project lifecycle*
