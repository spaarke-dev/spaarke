# Spaarke Teams App (R1) - AI Context

> **Purpose**: This file provides context for Claude Code when working on teams-app-r1.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Development (initialized — tasks generated, execution NOT started)
- **Last Updated**: 2026-08-03
- **Current Task**: Not started (see `current-task.md`)
- **Next Action**: Owner runs waves deliberately via `task-execute` (execution is owner-gated per `notes/pipeline-run-guidance.md`). Start with task 001 (foundation spike).

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized implementation spec (16 FRs, 7 NFRs) — permanent reference
- [`design.md`](design.md) - Full technical design (17 sections, D1–D11)
- [`adr-028-amendment-draft.md`](adr-028-amendment-draft.md) - Workforce-auth exemption (Path B — apply before/with auth code)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS (9 phases)
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker + parallel groups
- [`notes/pipeline-run-guidance.md`](notes/pipeline-run-guidance.md) - Owner run directive (INITIALIZE-ONLY; wave-by-wave execution later)

### Project Metadata
- **Project Name**: teams-app-r1
- **Type**: Client (Teams host adapter on `external-spa`) + BFF (resolver, routing) + PCF (`TrackingFieldTrio`) + Dataverse (contact field) + CI (deploy workflow)
- **Complexity**: High (dual-host auth, workforce→principal resolution, enterprise deployment)
- **Hot-path**: BFF=Y, SpaarkeAi=N, CI-Workflows=Y, Skill-Directives=N, root-CLAUDE=N

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for requirements + acceptance criteria; **design.md** for D1–D11 rationale
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)
6. **Run `/conflict-check` before every BFF PR** (13+ active BFF worktrees contend on `Sprk.Bff.Api`)

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

task-execute ensures: knowledge files loaded (ADRs, constraints, patterns) · context tracked in current-task.md · checkpointing every 3 steps · quality gates (code-review + adr-check) at Step 9.5 · recoverable after compaction. Bypassing → missing ADR constraints, lost progress, skipped gates.

### Parallel Task Execution

When tasks can run in parallel (no dependencies + `parallel-safe:true`), each STILL uses task-execute: ONE message with MULTIPLE Skill invocations. `.claude/`-touching tasks (ADR-028 A2 amendment) are **`parallel-safe:false`, main-session-only** (root CLAUDE.md Sub-Agent Write Boundary). Max 6 agents/wave. Build-verify between waves.

### 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files: decompose into a dependency graph, parallelize independent modules (different modules / no shared interfaces / no imports), serialize tightly-coupled work. Serialize the auth spine (resolver → membership → enforcement). See [task-execute SKILL.md Step 8.0].

---

## Execution Model & Tiering (Sonnet-5)

- **Planning** (this pipeline): Opus 4.8 / Fable 5.
- **Execution** (task-execute per task): default **Sonnet 5 @ effort `high`**.
- **Per-task tier + effort**: each POML carries `<model-tier>` (`sonnet` default; **`opus`** for the workforce→principal resolver, contact-anchored membership, `tid`→env routing, ADR-028 A2 amendment) AND `<effort>` (`high` default; **`xhigh`** for the auth spine + enforcement).
- **Step modes**: `directional` default; `prescriptive` for deploy/migration tasks. Judgment-boundary tasks carry `<escalation><trigger>`.

---

## Key Technical Constraints

- **Auth**: Teams users MUST authenticate with **workforce Entra** via Teams SSO/NAA (multitenant); MUST NOT use CIAM in Teams. Shared standalone-MSAL, pluggable authority. Do NOT route collaboration hosts through `@spaarke/auth` (Xrm-bound + MSAL v3) — ADR-028 A2.
- **Broker-only** (NFR-02): user token authenticates to BFF only; **never** exchanged downstream; all SPE/Dataverse access app-only (no OBO on the collaboration path). Per ADR-007 use `SpeFileStore`; MUST NOT inject Graph SDK types.
- **Role allowlist** (NFR-05): membership-derived access MUST be filtered to access-conferring `sprk_assigned*` roles (convention-based metadata discovery + exclusion list); adverse/informational roles MUST NOT confer access.
- **Reuse, don't fork**: `sprk_externalrecordaccess`, `MembershipResolverService` (`BuildFetchXml`), `InviteAndGrantExternalUserEndpoint`, `SendEmailDialog`, `TrackingFieldTrio`. No duplicated feature component across hosts without a §11 sign-off.
- **BFF hygiene** (ADR-029 / §10): ≤60 MB compressed (baseline ~49.63 MB incl. PDBs); **no** M365 Agents SDK / Bot packages; measure + report per BFF-touching task; Placement Justification in every BFF PR; no new HIGH CVE.
- **Endpoint filters** (ADR-008) for authz, not global middleware; ProblemDetails (ADR-019); Minimal API (ADR-001); DI minimalism (ADR-010); Redis-first cache (ADR-009).
- **UI**: PCF over webresources (ADR-006), shared component library (ADR-012), Fluent v9 + dark mode (ADR-021), React (ADR-022).

---

## Decisions Made

<!-- Format: Date, Decision, Rationale, Who -->

- **2026-08-03** — Owner-confirmed decisions locked in `spec.md` (Owner Clarifications / Resolved Decisions) + `design.md` §2.1 (D1–D11): workforce SSO for Teams; extend `external-spa` in place; derive-from-membership; Option-A access-permission posture; convention-based role allowlist; standing grants + email icon = R1; ADR-028 → Path B, ADR-034 → Path C. **Do not re-litigate.**
- **2026-08-03** — Pipeline run scope = INITIALIZE-ONLY (owner directive); execution is wave-by-wave, owner-gated. Structure every task with `<parallel-group>` + `<parallel-safe>`.

---

## Implementation Notes

- **ADR-028 A2 amendment** (`adr-028-amendment-draft.md`) must be applied to the canonical concise + full ADR **before/with** the Teams-host auth code (Phase 1). This is a `.claude/` + `docs/adr/` edit → main-session-only, `parallel-safe:false`.
- **`sprk_primarycontact`** linkage is an **admin activity out of project scope** — do NOT create tasks for it; it is a documented external prerequisite + go-live checklist item.
- Serialize the critical path: spike → A2 amendment → auth module/adapter → resolver → contact-anchored membership → enforcement → download gating.

---

## Deferrals & Issues — tracking obligation (read this)

Track deferred work + newly-discovered issues in TWO synced places: `notes/defer-issues.md` (source of truth) + GitHub Issues (visibility). File via `/project-defer-issue-tracking` (alias `/defer`) — writes BOTH in one step. NEVER add to `notes/defer-issues.md` only. CLAUDE.md §11 rule applies: every entry must name a concrete behavior/contract that fails without the work ("future flexibility" / "testability" = refused). `push-to-github` blocks push on entries without GitHub URLs.

---

## Resources

### Applicable ADRs
ADR-028 (+A1 +proposed A2), ADR-034, ADR-024, ADR-045, ADR-007, ADR-008, ADR-009, ADR-010, ADR-019, ADR-001, ADR-006, ADR-012, ADR-021, ADR-022, ADR-029, ADR-038 (testing).

### Related Projects
- `spaarke-SPA-external-access-platform-r1` (the deployed SPA base — reuse)
- `ai-m365-copilot-integration` (Teams/workforce Entra plumbing — merged; Entra app `1e40baad-…`)
- `sdap-teams-app` (prior Teams-surface spec — design-only precedent)
- Active BFF-contending worktrees (see `projects/INDEX.md`) — `/conflict-check` before every BFF PR.

### External Documentation
- `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` (host checklist §8 = Teams acceptance test)
- `docs/standards/MODAL-DECISION-CRITERIA.md` + `MODAL-DESIGN-SYSTEM.md` (grant modal)
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`; `.claude/constraints/bff-extensions.md` (§10)
- M365 Agents Toolkit; Teams manifest v1.29 schema; App Centric Management (org catalog).

---

*This file should be kept updated throughout project lifecycle*
