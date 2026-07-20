# Communication Workspace — R2 — AI Context

> **Purpose**: Context for Claude Code when working on messaging-communication-app-r2.
> **Always load this file first** when working on any task in this project.
> **Status (2026-07-18)**: IMPLEMENTATION — pipeline complete. 21 tasks / 9 waves. Ready for Wave 0 (task 001).

---

## What this project is

R2 = **Communication Workspace** — the read/query/organize layer on top of the R1 messaging channel. R1
shipped transport + capture + the thread model + a per-thread polling Timeline. R2 makes communications
**findable and organized across records and people**: threads-per-record (all 11 entities), a global
all-communications view, a rich workspace widget, thread regarding-resolution, an auto-threading policy, a
queryable participant index, and a richer compose form. The R1 data model already supports the core
experience — R2 is mostly **read surface + UI + two schema deltas**, not a schema migration.

**Follows**: `messaging-communication-app-r1` (COMPLETE, merged, archived 2026-07-18).
**Builds additively on (merged to master)**: `email-communication-solution-r4` (`Services/Communication/**`).
**Coordinates (reserve-only)**: `spaarke-notification-spine-r1` — R2 reserves the `communication-arrived`
kind; **no dependency** (R2 stays BFF-polling, NFR-06).

---

## Project Status

- **Phase**: Implementation — ready for Wave 0 (task 001)
- **Branch**: `work/messaging-communication-app-r2` (synced to latest master 2026-07-18)
- **Current Task**: None active (pipeline complete)
- **Next Action**: `work on task 001` (Phase-0 audit spike)

### Key Files
- [`spec.md`](spec.md) — AI spec (12 FRs, 8 NFRs) — implementation source of truth
- [`design.md`](design.md) — investigation-grounded design
- [`plan.md`](plan.md) — Wave WBS + critical path + discovered resources
- [`current-task.md`](current-task.md) — active task state (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — all tasks + parallel groups + dependency graph
- [`notes/r2-resource-investigation.md`](notes/r2-resource-investigation.md) — 5-part reuse audit (do NOT re-run)

---

## Owner decisions (all LOCKED 2026-07-18) — spec §10 + resolved questions

| # | Decision |
|---|----------|
| Q1 | Build `sprk_communicationparticipant` junction (message grain, 2 typed lookups, unresolved-address rows) |
| Q2 | No category/tags — threads = regarding + name only |
| Q3 | Upgrade the shipped grid config `e1826c4c-…` + `communications-list` widget **in place** (ai-spaarke-ai-workspace-UI-r2 is Complete) |
| Q4 | Ship the standalone All-Communications page (widget/launcher only, no sitemap in R2) |
| Q5 | All 11 regarding-family entities from day one |
| Q-C | Junction identity = **two typed lookups** (`sprk_systemuser`/`sprk_contact`), not the ADR-034 Guid+type tuple (path-C comply-with-intent) |
| Q-D | Unresolved external addresses **write a row** (`isresolved=false` + `addresstext`) |
| Q-E | **Stay BFF-polling; no notification-spine dependency** (reserve alignment only) |

---

## 🚨 MANDATORY: Task Execution Protocol

All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via task-execute |
| "continue" / "next task" | Read `TASK-INDEX.md`, find first 🔲, invoke task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load `current-task.md`, invoke task-execute |

**Sub-Agent Write Boundary**: sub-agents CANNOT write to `.claude/` paths. Tasks touching `.claude/` are
`parallel-safe: false` and run from the main session. Affected: **004** (participant-junction schema ADR).
See root CLAUDE.md §3.

**Max concurrency**: 6 agents/wave. Dispatch each task's subagent with its `<model-tier>` + `<effort>`
(default `sonnet` @ `high`; **opus** on 004/050/070; **xhigh** on 050/070/080).

---

## 🚨 BINDING: Hot-Path Coordination (shared `Services/Communication/`)

R2 edits shared `Services/Communication/` code. **email-r4's BFF work is merged to master (this worktree is
synced); build additively.**

- **Run `/conflict-check` at project start and before every BFF wave.**
- **Tasks 050 (participant-index write) + 070 (auto-threading `IThreadResolver`) are the shared-path edits** —
  `parallel-safe: false`; characterization-test existing email/messaging flows and keep them green BEFORE
  extending; never run concurrently with each other or other BFF writers.
- **Reads**: extend `CommunicationThreadReadService` + `IImpersonatedCommunicationQuery` +
  `ICommunicationAccessFilter` only. **Do NOT** add a second access mechanism or reintroduce membership-union
  on reads (retired 2026-07-16 — `../messaging-communication-app-r1/notes/access-model-decision.md`).
- **SpaarkeAi widget (030)**: dual-deploy (LegalWorkspace + SpaarkeAi); keep type string `communications-list`;
  merge-order coordination with `spaarke-dataset-grid-framework-r2` + open PR #508 (Events.Components).

---

## 🚨 BINDING: BFF Hygiene (root CLAUDE.md §10)

For every BFF-touching task:
1. Load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) before designing.
2. State the **Placement Justification** in the PR (even when "in BFF") — see [`plan.md`](plan.md) §3.
3. Use `Services/Ai/PublicContracts/` facades for any CRUD→AI need (R2 needs none — no AI dependency).
4. **Verify publish-size** every BFF task: report absolute + delta vs **~46.99 MB** baseline (post-R1).
   Ceiling ≤60 MB; ≥+5 MB single-task → justify; ≥55 MB → architecture review; ≥60 MB → HARD STOP.
   Expected R2 delta ≈0 (no new packages).
5. Verify no new HIGH CVE (`dotnet list package --vulnerable --include-transitive`).
6. Update tests in `tests/unit/Sprk.Bff.Api.Tests/`; feature-gated services use ADR-032 Null-Object +
   unconditional endpoint registration.

---

## Key Technical Constraints (from spec — binding)

- ✅ MUST extend `CommunicationThreadReadService`/`IImpersonatedCommunicationQuery`/`ICommunicationAccessFilter`
  for `by-regarding` + `query`; apply impersonation + 2-rule (privacy + internal-only) filter.
- ✅ MUST add a **new Lookup discriminator** on `sprk_communicationthread`; MUST keep the existing Text
  `sprk_regardingrecordtype` (non-breaking).
- ✅ MUST populate the participant junction at **message grain** reusing `ParticipantCorrelationRung`;
  align ADR-034 tuple *intent* via the 2 typed lookups.
- ✅ MUST keep the widget type string `communications-list` + section id `communications`.
- ✅ MUST reuse grid config `e1826c4c-…` (single default); MUST declare a bound `anchorField` on new form PCFs.
- ✅ MUST dual-deploy (LegalWorkspace + SpaarkeAi) on widget/shared-lib changes.
- ❌ MUST NOT reintroduce membership-union on reads; add a second access mechanism, grid default, or widget.
- ❌ MUST NOT retype the Text `sprk_regardingrecordtype`; render message content via VisualHost client-fetch.
- ❌ MUST NOT use CommunicationConnections PCF or the Field Mapping Framework for thread regarding.
- ❌ MUST NOT build a parallel push/fan-out (stay BFF-polling; reserve the spine kind only).

---

## ADR Tensions (spec §6.5 — on the record, all path C)

| ADR | Rule | Path | Resolution |
|---|---|---|---|
| **ADR-034** | `(personId, personIdType)` tuple / no polymorphic parent | **C (comply-with-intent)** | 2 typed lookups (systemuser/contact) — only 2 targets, so no polymorphic lookup needed; honors ADR-034's intent (typed identity, no text-name matching) + adds FK integrity + DataGrid chip auto-derivation. |
| **ADR-046** | no second regarding mechanism | **C (comply)** | Reuse the existing RegardingResolver PCF (0 code) on the thread form; add a Lookup discriminator so it can bind. |
| **T-1** | message content flows only through the BFF `internal-only` filter | **C (comply)** | VisualHost restricted to a count MetricCard; content via the BFF-backed regarding-mode Timeline. |

No ADR amendment (path B) required. No new-surface tension against an existing ADR beyond the above.

---

## Phase 0 — Verify Before Build

Wave 0 task **001** (spike) confirms: live `sprk_communicationthread`/`sprk_communication` schema; category/tags
absent (Q2); email-r4 `Services/Communication` merged state on this worktree; `sprk_role` choice-integer plan.
Do NOT skip — grounds the two schema deltas (002/003).

---

## Decisions Made
*Appended by `task-execute` during execution.*

## Implementation Notes
*No notes yet — first task starts in Wave 0.*
