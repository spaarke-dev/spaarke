# Spaarke External Access Platform (R2) - AI Context

> **Purpose**: This file provides context for Claude Code when working on spaarke-SPA-external-access-platform-r2.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Development (INITIALIZED — tasks generated; execution owner-gated wave-by-wave)
- **Last Updated**: 2026-08-06
- **Current Task**: Not started
- **Next Action**: Owner-gate — begin P0 prototype, then run task-execute per wave

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized specification (permanent reference; FR-01–FR-22, NFRs, ADR Tensions)
- [`design.md`](design.md) - Original scoping brief
- [`notes/ux-brief.md`](notes/ux-brief.md) - **Locked UX north-star** (gates P0 prototype + all frontend build)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS (P0–P4)
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker
- [`notes/external-access-capability-synopsis.md`](notes/external-access-capability-synopsis.md) - R1 code synopsis (file:line)
- [`notes/r2-coordination-response.md`](notes/r2-coordination-response.md) - teams-app-r1 FR-22 delivery
- [`notes/teams-app-r1-coordination.md`](notes/teams-app-r1-coordination.md) - teams-app-r1 coordination

### Project Metadata
- **Project Name**: spaarke-SPA-external-access-platform-r2
- **Type**: BFF (external-access) + Client SPA (external-spa) + Dataverse schema + Teams packaging + CI
- **Complexity**: High (platform foundation + second capability; heavy reuse)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for requirements, ADR Tensions, and acceptance criteria; **notes/ux-brief.md** for any frontend task
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)
6. **Run `/conflict-check` before EVERY BFF PR** (external-access surface shared with teams-app-r1)

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

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

The task-execute skill ensures: knowledge files loaded (ADRs, constraints, patterns); context tracked
in current-task.md; proactive checkpointing every 3 steps; quality gates (code-review + adr-check) at
Step 9.5; progress recoverable after compaction. Bypassing → missing ADR constraints, lost progress,
skipped gates.

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute — one message
with multiple Skill invocations. **MAX 6 agents/wave.** Tasks touching `.claude/` MUST be sequential
(main-session-only, §3) — the ADR-028 A3 task is one of these.

---

## Execution Model & Tiering (Sonnet-5)

- **Planning** (this pipeline run): Opus 4.8 / Fable 5.
- **Execution** (task-execute per task): default **Sonnet 5 @ effort `high`**; each POML carries
  `<model-tier>` (`opus`/`fable` for the ADR-028 A3 amendment, auth-plane, and high-blast-radius
  cleanup tasks) + `<effort>` (`xhigh` for brownfield/root-cause: FR-22 generalization, cleanup
  deletions, live-E2E).
- **Step modes**: `directional` default; `prescriptive` for deploys/migrations/irreversible cleanup.
- Tasks with a judgment boundary carry `<escalation><trigger>` (e.g., cleanup deletion when a caller
  is found; schema-shape decision).

---

## Key Technical Constraints

- **Broker-only for CIAM** (no OBO); workforce SPA path no-OBO, no Power-Apps-license dependency — per ADR-028 (+A1/A2/**A3**).
- **Two independent tiers**: module entitlement (Tier 1: App Role / Contact) ≠ record visibility (Tier 2: per-module predicate). Both server-enforced; **negative Tier-2 test per module** (NFR-08).
- **Per-endpoint authz filters / route-group policies** — no global middleware (ADR-008); per-module `Map{Module}Endpoints` groups.
- **Redis-first cache** for `/me` entitlement + participation; invalidate on change (ADR-009).
- **`SpeFileStore` facade** for all SPE ops; app-only download/upload; authz-before-stream (ADR-007).
- **Fluent v9 + React 18**; semantic tokens; dark-mode + Teams-theme parity; zero hardcoded hex (ADR-021/022).
- **All dialogs via `SprkModal` + presets** (ADR-050); admin grant/revoke via existing `AccessGrantModal`. **No hand-rolled UI** (§11).
- **Preserve external-SPA `sessionStorage`** per-tab isolation for CIAM (documented ADR-028 exception; do NOT switch to localStorage/@spaarke/auth on the CIAM path).
- **§10 BFF hygiene**: Placement Justification per BFF addition (cite `.claude/constraints/bff-extensions.md`); **publish ≤60 MB compressed** (baseline 46.90 MB incl PDBs — report delta each BFF task); no new HIGH CVE; tests in `tests/unit/Sprk.Bff.Api.Tests/`.
- **§11 reuse**: extend donors, do **NOT** fork `LegalWorkspaceApp` (Xrm-bound). Lift + generalize the delivered `CallerPrincipalResolver`.
- **DI minimalism** (ADR-010): interface only with ≥2 real impls (module-entitlement resolver qualifies; strategies).

### Coordination guardrails (BINDING)
- **⚠️ teams-app-r1** (BFF=Y, CI=Y, owner-gated) owns the SAME external-access surface (`Api/ExternalAccess/**`, `Infrastructure/ExternalAccess/**`, `deploy-external-spa.yml`) and delivered the FR-22 code R2 lifts. `/conflict-check` before EVERY BFF PR; shared external-access files → `parallel-safe:false`. Its operator-gated BFF redeploy + live Teams E2E is a P1 prerequisite.
- **ADR-028 A3** task edits `.claude/adr/` + `docs/adr/` → main-session-only, `parallel-safe:false`, ordered FIRST before P1 auth code. **A2 already exists** — author A3 (read A2 first).
- **19 active projects touch `Sprk.Bff.Api`** but the external-access corner is isolated from the AI/Compose/Communication cluster.

---

## Decisions Made

- **2026-08-06**: INITIALIZE-ONLY pipeline run — Reason: heavy BFF coordination + owner-gated teams-app-r1 dependency; matches sibling pattern.
- **2026-08-06**: Author ADR-028 **Amendment A3** (not A2) — Reason: A2 exists (teams-app-r1 Teams host); A3 ratifies R2's dual-plane module-framework generalization.
- **2026-08-06**: Prototype-first P0 UX phase, internal precedent only — Reason: many net-new surfaces; validate before build; ~10s-users/month tool doesn't warrant market research.
- **2026-08-06**: Shared-library mandate (`@spaarke/ui-components` + `SprkModal` + `AccessGrantModal`; no hand-rolled UI) — Reason: §11 + consistency + dark-mode/Teams-theme correctness.

---

## Implementation Notes

*No notes yet* — populated during execution.

---

## Deferrals & Issues — tracking obligation (read this)

This project tracks deferred work + newly-discovered issues in TWO places, kept in sync:
1. **`notes/defer-issues.md`** — source of truth
2. **GitHub Issues** on the portfolio board

File via `/project-defer-issue-tracking` (alias `/defer`) — writes BOTH in one step. NEVER file
only locally. §11 rule applies: every entry must name a concrete failing behavior/contract ("future
flexibility" / "testability" alone = refused).

---

## Resources

### Applicable ADRs
ADR-028 (+A1/A2/**A3**), ADR-008, ADR-009, ADR-007, ADR-001, ADR-010, ADR-019, ADR-021, ADR-022, ADR-024, ADR-034, ADR-038, ADR-050.

### Related Projects
- `spaarke-SPA-external-access-platform-r1` (predecessor — shipped)
- `teams-app-r1` (sibling — delivered FR-22 code R2 lifts; owner-gated; heavy coordination)
- R3 (future — E-billing module)

### External Documentation
- `docs/architecture/external-access-spa-architecture.md`, `docs/guides/EXTERNAL-ACCESS-SPA-GUIDE.md`, `EXTERNAL-ACCESS-ADMIN-SETUP.md`
- `docs/architecture/office-outlook-teams-integration-architecture.md` (Teams tab)
- `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` (embedded-mode discipline)
- `.claude/constraints/bff-extensions.md` (binding BFF governance)
- `docs/standards/MODAL-DECISION-CRITERIA.md` + `MODAL-DESIGN-SYSTEM.md`

---

*This file should be kept updated throughout project lifecycle*
