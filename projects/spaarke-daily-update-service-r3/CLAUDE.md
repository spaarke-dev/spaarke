# Daily Briefing — Read-State Decoupling + Producer TTL Hardening (R3) — AI Context

> **Purpose**: This file provides context for Claude Code when working on `spaarke-daily-update-service-r3`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning → Implementation handoff
- **Last Updated**: 2026-06-24
- **Current Task**: Not started
- **Next Action**: Tasks 001–090 generated; execute via `task-execute` (Wave 1 parallel: 001, 010, 020)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (7 FRs + 5 NFRs)
- [`design.md`](design.md) — Human-friendly design (problem + solution + owner clarifications)
- [`README.md`](README.md) — Project overview + graduation criteria
- [`plan.md`](plan.md) — Implementation plan + WBS + critical path + parallel opportunities
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task registry + parallel groups

### Project Metadata
- **Project Name**: `spaarke-daily-update-service-r3`
- **Branch**: `work/spaarke-daily-update-service-r3`
- **PR**: [#451](https://github.com/spaarke-dev/spaarke/pull/451) (draft)
- **Predecessor**: [`projects/spaarke-daily-update-service-r2/`](../spaarke-daily-update-service-r2/) — R2 Pattern D widget migration (R3 builds on this)
- **Sibling**: [`projects/spaarke-platform-foundations-r3/`](../spaarke-platform-foundations-r3/) — producer-side recipient resolution (independent; R3 trusts it)
- **Type**: Multi-surface (Dataverse schema + BFF 1-line fix + shared widget package consumer changes)
- **Complexity**: Low-Medium (~6 hours engineering + 30 min operator)
- **Rigor Level**: FULL (per spec NFR-03: all changes pass `code-review` + `adr-check` at task-execute Step 9.5; BFF-touching tasks per §10 binding rules)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements, and ACs (FR-1..FR-7, NFR-01..NFR-05, AC-1..AC-7b)
4. **Reference design.md** for owner clarifications, root-cause analysis, and rationale
5. **Load the relevant task file** from `tasks/` based on current work
6. **Apply ADRs** relevant to the work (loaded automatically via `adr-aware`)
7. **Reference [`bff-extensions.md`](../../.claude/constraints/bff-extensions.md)** before modifying `NotificationService.cs` (binding constraint per CLAUDE.md §10)

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
- ❌ No checkpointing — lost progress after compaction
- ❌ Skipped quality gates

### Parallel Task Execution

This project has one significant parallel opportunity (**Wave 1**): tasks 001, 010, 020 are independent and can run as 3 concurrent agents.

When dispatching parallel tasks, send ONE message with MULTIPLE Skill tool invocations (one per task). Each invocation calls task-execute with a different task file.

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

R3 tasks are small (1–5 files each). Most do NOT need subagent decomposition. The one task that touches 3+ files (031: NarrativeBullet + ActivityNotesSection + DailyBriefingApp) is sequential by file dependency — single agent + sequential edits is the right call.

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for the full protocol.

---

## Key Technical Constraints

**From spec.md MUST Rules**:

- ✅ MUST add `sprk_briefingstate` as Choice (option-set) NOT Boolean — three discrete states
- ✅ MUST default `sprk_briefingstate` to `0` (Unread) at the Dataverse-schema level (no producer-side write)
- ✅ MUST treat null `sprk_briefingstate` on read as Unread (null-coalesce in widget)
- ✅ MUST use `CalendarAddRegular` from `@fluentui/react-icons` for the Keep button
- ✅ MUST use literal +7 calendar days for the Keep action; `newTtl = currentTtl + 604800` (no weekend logic)
- ✅ MUST issue server-side filter `(sprk_briefingstate ne 2 or sprk_briefingstate eq null)` in widget queries
- ✅ MUST perform optimistic UI update on all 3 new actions
- ✅ MUST show success or error toast via existing `useToastController` pattern
- ❌ MUST NOT change the producer's `toasttype = 200000000` write (canonically correct = "Timed")
- ❌ MUST NOT add weekend-aware TTL calculation (future due-date engine will own this)
- ❌ MUST NOT add widget-side BFF call to filter by user's matter associations (trust producer-side recipient resolution)
- ❌ MUST NOT backfill `sprk_briefingstate` on existing rows
- ❌ MUST NOT write `isread` or `toasttype` from the widget for state mutation (FR-7 invariant)
- ❌ MUST NOT introduce new BFF endpoints — all widget actions go direct to Dataverse via `Xrm.WebApi.updateRecord`

**Cross-cutting (from CLAUDE.md §10 — BFF Hygiene)** — applies to task 010:

- BFF publish-size: ≤ +0.1 MB delta (spec NFR-02); baseline ~45.65 MB; hard ceiling 60 MB
- BFF-touching PR MUST verify size via `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`
- BFF-touching PR MUST verify no new HIGH-severity CVE via `dotnet list package --vulnerable --include-transitive`
- Test update obligation (§10 bullet 6): change to `NotificationService.cs` requires matching test update in `tests/unit/Sprk.Bff.Api.Tests/`
- Placement Justification: spec.md §10 already establishes the 1-line bug fix in existing service has no new components or interfaces — constraint satisfied with minimal scope

---

## Decisions Made

- **2026-06-24**: Use existing `work/spaarke-daily-update-service-r3` branch (not create new `feature/` branch). Reason: matches Spaarke `work/` convention; current branch already set up with PR #451. Who: project initialization session.
- **2026-06-24**: Skip carrying forward R2 lessons-learned. Reason: R2 was a hoist/migration project; R3 is a defect-fix + small feature project. Different domain. Who: project initialization session.
- **2026-06-24**: 7 tasks across 5 phases with Wave 1 parallel (001, 010, 020). Reason: parallel-safety analysis — file-level dependencies; tasks 030/031 must serialize behind 020. Who: project initialization session.
- **2026-06-24**: Don't auto-execute task 001. Reason: user asked for `/project-pipeline` to plan + scaffold; review before kick-off. Who: project initialization session.

---

## Implementation Notes

- **R2 lessons-learned** does not exist (R2 notes/ contains workstream notes but no `lessons-learned.md`). When R3 wraps, author `notes/lessons-learned.md` to capture both R2 and R3 learnings (R3 wrap-up task 090).
- **R3 platform-foundations is independent**: producer-side recipient resolution (`MembershipService`, R3 platform-foundations) is a parallel project that has shipped to master. R3 daily-update-service trusts that resolution and adds NO widget-side filtering (per owner clarification Q5).
- **Microsoft Learn `appnotification` semantics**: `toasttype = 200000000` is "Timed" (display behavior, auto-dismiss), NOT a read marker. This was the root cause of the UAT defect — both the original producer code (correct) and the widget read (incorrect) used this field with mismatched intent.
- **Existing `markNotificationRead` is the pattern template**: located at `notificationService.ts:270-276`, follows `tryCatch + webApi.updateRecord` shape. All 3 new action functions mirror this exactly.
- **Existing `handleAddToTodo` is the UI pattern template**: located at `DailyBriefingApp.tsx:238-287`, follows optimistic-update + service-call + toast shape. All 3 new hook handlers mirror this.

---

## Resources

### Applicable ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| **ADR-001** | BFF Minimal API | NotificationService.cs change is 1-line fix in existing service; no new endpoints |
| **ADR-012** | Shared component library | Widget changes live in `@spaarke/daily-briefing-components`; package boundary unchanged |
| **ADR-021** | Fluent v9 design system | All 3 new buttons use Fluent v9 tokens + icons + dark mode |
| **ADR-024** | `sprk_todo` regarding catalog | Existing `useInlineTodoCreate` (4th button "Add to To Do") preserved unchanged |
| **ADR-027** | Subscription isolation | `appnotification` is CORE; `sprk_briefingstate` is a CORE additive schema change |

### Binding Constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — Sections A (MUST checklist), F (test update obligation) — applies to task 010 NotificationService.cs change
- [`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md) — Griffel, semantic tokens for 3 new buttons (task 031)
- [`.claude/patterns/ui/fluent-v9-theming.md`](../../.claude/patterns/ui/fluent-v9-theming.md) — FluentProvider, dark mode requirements
- [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](../../docs/standards/DATA-ACCESS-DECISION-CRITERIA.md) — Widget actions use `Xrm.WebApi.updateRecord` (host-context), not BFF

### Related Projects

- [`projects/spaarke-daily-update-service-r2/`](../spaarke-daily-update-service-r2/) — R2 Pattern D widget migration (predecessor — R3 builds on its consumer layer)
- [`projects/spaarke-platform-foundations-r3/`](../spaarke-platform-foundations-r3/) — Producer-side membership / recipient resolution (independent; R3 trusts it)

### External Documentation

- **Microsoft Learn — Send in-app notifications**: https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/send-in-app-notifications (canonical `toasttype` + `ttlinseconds` semantics)
- **Microsoft Learn — `appnotification` table reference**: column `TTLInSeconds` — "The number of seconds from when the notification should be deleted if not already dismissed"

### Reference Code

- [`src/server/api/Sprk.Bff.Api/Services/NotificationService.cs:105-106`](../../src/server/api/Sprk.Bff.Api/Services/NotificationService.cs#L105) — Target of task 010 fix
- [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs:488-490`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs#L488) — Canonical `ttlinseconds` write (task 010 aligns to this)
- [`src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts:270-276`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts#L270) — `markNotificationRead` pattern (task 020 mirrors)
- [`src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useBriefingActions.ts`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useBriefingActions.ts) — Existing hook shape (task 030 extends)
- [`src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx:238-287`](../../src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx#L238) — `handleAddToTodo` optimistic + toast pattern (task 031 mirrors)

---

*This file should be kept updated throughout project lifecycle*
