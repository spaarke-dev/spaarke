# pcf-orphan-cleanup-r1 — AI Context

> **Purpose**: Project-scoped AI context for Claude Code when working on `pcf-orphan-cleanup-r1`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Phase 0 — Project Setup
- **Created**: 2026-06-22
- **Current Task**: see [`current-task.md`](current-task.md)
- **Next Action**: execute Task 001 (pre-flight backups) — can run in parallel with Task 002 (source deletion)

## Quick Reference

### Key Files

- [`README.md`](README.md) — project overview + graduation criteria
- [`spec.md`](spec.md) — 8 FRs / 7 NFRs / 12 SCs / 6 binding decisions
- [`design.md`](design.md) — decision rationale (operational procedure lives elsewhere — see below)
- [`current-task.md`](current-task.md) — active task pointer
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker
- [`notes/research-findings-2026-06-22.md`](notes/research-findings-2026-06-22.md) — compiled session research that scoped this project

### Operational procedure (canonical step-by-step)

📋 **[`../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md`](../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md)**

This is THE step-by-step reference. Every task file in `tasks/` is a slice of this procedure. Always read the relevant procedure section before running a task.

### Authoritative source artifacts

- [`../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md`](../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md) — the inventory this project acts on
- [`../ai-procedure-quality-r1/notes/inventory/pcf-bundle-sizes.md`](../ai-procedure-quality-r1/notes/inventory/pcf-bundle-sizes.md) — 2026-05-14 bundle baseline; will be refreshed in Task 007
- [`../sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-a-pcf-audit-2026-06-01.md`](../sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-a-pcf-audit-2026-06-01.md) — 2026-06-01 test-rot audit (different lens; complementary data)

### Project metadata

- **Project Name**: pcf-orphan-cleanup-r1
- **Type**: Quality / hygiene (production fixes + cleanup)
- **Complexity**: MEDIUM (3-5 days, 7 tasks, 1 Dataverse-mutation session that needs explicit gating)
- **Predecessor research**: chat-routing-redesign-r1 (TypeScript drift report) + ai-procedure-quality-r1 (inventory home)
- **Production blast radius**: HIGH (Dataverse customcontrol deletions are not GUID-stable for restoration — see [design.md §3.5 resurrection playbook in the procedure doc])

---

## Context Loading Rules

When working on this project, Claude Code MUST:

1. **Always load this file first** when starting work on any task
2. **Check [`current-task.md`](current-task.md)** for active work state (especially after compaction / new session)
3. **Reference [`spec.md`](spec.md)** for FR / NFR / Success Criteria
4. **Reference [`design.md`](design.md)** for Decisions D-01 through D-06
5. **Load the procedure doc section that maps to the current task** — every task POML cites its procedure-section anchor
6. **Apply ADRs** relevant to PCFs (loaded automatically via `adr-aware`):
   - **ADR-022** (PCF platform libraries) — DIRECTLY binding for FR-05 (VisualHost re-pin DOWN, not UP)
   - **ADR-006** (PCF over webresources) — relevant context (this project retires unused PCFs but doesn't change the preference for PCFs over webresources)
   - **ADR-012** (Shared component library) — FR-04 multi-major peerDep range is consistent with ADR-012's context-agnostic goal

**Context Recovery**: see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via `task-execute` |
| "continue" / "keep going" / "next task" | Read `tasks/TASK-INDEX.md`, find first 🔲, invoke `task-execute` |
| "continue with task X" / "resume task X" | Execute task X via `task-execute` |
| "pick up where we left off" | Load `current-task.md`, invoke `task-execute` |

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Parallel Task Execution

Two parallel-safe windows in this project:

**P1-W1 (early)** — Tasks 001 + 002 in parallel:
- Task 001 (pre-flight backups) writes to `projects/pcf-orphan-cleanup-r1/backups-2026-06-22/`
- Task 002 (source deletion PR) writes to `src/client/pcf/*`
- Disjoint write paths → safe to dispatch in parallel as ONE message with TWO Skill invocations

**P5-W1 (closeout)** — Tasks 006 + 007 sequence (006 → 007 sequential dependency; 007 needs 006 complete to write the cleanup-log)

Mid-project tasks (003, 004, 005) are sequential (003 = Dataverse mutation must complete + soak before 004/005 hit; 004 → 005 sequential because PR-D1 enables PR-D2's peerDep-resolved type matrix).

---

## Key Technical Constraints

### Binding ADRs

- **ADR-022** (PCF platform libraries) — DIRECTLY binding for FR-05 VisualHost re-pin
- **ADR-006** (PCF over webresources) — relevant for understanding why PCFs are even on Dataverse
- **ADR-012** (Shared component library) — relevant for FR-04 peerDep contract

### Binding MUSTs (NFRs)

- ✅ MUST capture §1.1 baseline solution backups before ANY Dataverse-side deletion (NFR-01)
- ✅ MUST run §1.2 4-check per control before deletion; FormXML grep is MANDATORY for SDV (NFR-02; D-02)
- ✅ MUST wait 7 calendar days between spaarkedev1 and spaarkedev2 cleanup (NFR-03)
- ✅ MUST re-verify VisualHost React-18-API grep pre-merge of PR-D2 (NFR-05)
- ✅ MUST spot-test the resurrection path before any deletion in production (NFR-07)
- ❌ MUST NOT re-introduce UQC / DTW / SDV in parallel projects during this project's window (NFR-04)
- ❌ MUST NOT include PlaybookBuilderHost in this project's deletion scope (D-03)

### Project-specific discipline

- **One PR per workstream**: source delete (FR-01), shared lib peerDep (FR-04), VisualHost re-pin (FR-05). Dataverse-side work is NOT a PR — it's a managed maker-portal session logged per FR-07.

---

## Decisions Made

See [`spec.md §5`](spec.md#5-decisions-made-binding-for-this-project) for the binding decisions table.

Notable decisions:

- **D-01**: UQC + DTW + SDV deleted from source in ONE PR (not three separate).
- **D-02**: SDV FormXML grep is MANDATORY (not advisory).
- **D-03**: PlaybookBuilderHost EXPLICITLY out of scope.
- **D-04**: VisualHost re-pinned DOWN to React 16 (not UP to 19). Source verified zero React 18-only API usage.
- **D-05**: 7-day soak between environments.
- **D-06**: Project lifespan ends at spaarkedev2 completion.

---

## Implementation Notes

### Why this is one project, not three PRs

The three workstreams (source delete, Dataverse cleanup, React-types fix) share:
- One inventory of "what's alive" (built during chat-routing-redesign-r1's investigation)
- One execution flow (pre-flight → source → Dataverse → types → replay)
- One production blast-radius gate

Doing them separately invites re-drift between PRs. The cleanup is small enough (3-5 days) to bundle but big enough to warrant project-level discipline.

### Anti-time-waster takeaway

After this project lands, the workspace stops building / testing / lint-checking UQC + DTW + SDV. The next CI run's PCF workspace build step will be visibly faster.

### Recovery is real but GUID-changing

The §3.5 resurrection playbook (in the procedure doc) makes recovery a 5-10 minute operation IF the §1.1 backups exist. GUIDs change on re-import — anything that hardcoded a customcontrol/canvas-app GUID will need rewiring. Name-based references (the common case) survive.

---

## Resources

### Applicable ADRs

- **ADR-022** — PCF Platform Libraries (React 16 APIs only)
- **ADR-006** — PCF Over Webresources (PCF as extension surface)
- **ADR-012** — Shared Component Library

### Related Projects

- **[`ai-procedure-quality-r1`](../ai-procedure-quality-r1/)** — HOST of the inventory + procedure source artifacts; this project is a "child" execution
- **[`chat-routing-redesign-r1`](../spaarke-ai-platform-chat-routing-redesign-r1/)** — originating audit (TypeScript drift report)
- **[`sdap.bff.api-test-suite-repair-r2`](../sdap.bff.api-test-suite-repair-r2/)** — complementary test-rot audit (2026-06-01)

### External Documentation

- [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) — full PCF redeploy procedure (used in FR-05 / Task 005)
- [`.claude/FAILURE-MODES.md#AP-1`](../../.claude/FAILURE-MODES.md) — `build:prod` not `build` reminder

---

*This file should be kept updated throughout project lifecycle. Add to "Decisions Made" and "Implementation Notes" as work progresses.*
