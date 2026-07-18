# Communication Workspace — R2 — AI Context

> **Purpose**: Context for Claude Code when working on messaging-communication-app-r2.
> **Always load this file first** when working on any task in this project.
> **Status (2026-07-18)**: DRAFT DESIGN. 4/5 owner decisions locked. Not yet through `/design-to-spec`.

---

## What this project is

R2 = **Communication Workspace** — the read/query/organize layer on top of the R1 messaging channel. R1
shipped the transport + capture + thread model + a per-thread polling timeline. R2 makes communications
**findable and organized across records and people**: threads-per-record, a global all-communications view,
a rich workspace widget, thread-regarding, an auto-threading policy, a queryable participant index, and a
richer compose form.

**Follows**: `messaging-communication-app-r1` (COMPLETE, merged, archived 2026-07-18).
**Coordinates with**: `ai-spaarke-ai-workspace-UI-r2` — **active worktree** at
`c:/code_files/spaarke-wt-ai-spaarke-ai-workspace-UI-r2`. It owns the shipped Communications grid config
(`sprk_gridconfiguration` GUID `e1826c4c-9575-f111-ab0e-7ced8ddc4a05`) + the thin `communications-list`
widget that R2 plans to **upgrade in place**. **This is open question Q3 — confirm before W0.**

---

## Owner decisions (LOCKED 2026-07-18) — design §10

| # | Decision | Effect |
|---|----------|--------|
| Q1 | **Build `sprk_communicationparticipant` junction in R2** | W5 mandatory; exact `participant=` filtering ships (no lookup-only interim) |
| Q2 | **No category/tags in R2** | Threads = regarding + name only; dropped from schema + W0 |
| Q4 | **Ship the standalone All-Communications page** | Surface 2 in scope (~50-line shell, copy `sprk_invoicespage`) |
| Q5 | **All 11 regarding-family entities** | Surface 1 on all 11 forms; W1/W4 test matrix expands |
| **Q3** | **OPEN — coordination confirm** | The ONLY gate before `/design-to-spec`. Confirm upgrading the shipped grid config + widget in place (vs `ai-spaarke-ai-workspace-UI-r2` owning them mid-flight). |

---

## Documents

| File | Purpose |
|---|---|
| [`design.md`](design.md) | Draft design — 3 surfaces, CC-1..CC-5, hot-path decl, §11 reuse ledger, 8 waves, §10 locked decisions |
| [`notes/r2-resource-investigation.md`](notes/r2-resource-investigation.md) | 5-part reuse audit (exact file paths) — **do NOT re-run; read this instead** |
| [`current-task.md`](current-task.md) | Recovery state + decisions |
| [`START-HERE.md`](START-HERE.md) | New-session kickoff (read first) |

---

## 🚨 Binding constraints inherited from R1 (do not relitigate)

- **Reads = impersonation + 2-rule filter.** New `by-regarding`/`query` endpoints MUST extend
  `CommunicationThreadReadService` + `IImpersonatedCommunicationQuery` + `ICommunicationAccessFilter`.
  **Do NOT** add a second access mechanism or reintroduce membership-union on reads (retired 2026-07-16,
  `../messaging-communication-app-r1/notes/access-model-decision.md`).
- **Participants are not queryable** — `sprk_from/to/cc` are `;`-joined TEXT. The person filter needs the
  new `sprk_communicationparticipant` junction (Q1), populated at capture/send reusing
  `ParticipantCorrelationRung` email→contact resolution; align ADR-034 `(personId, personIdType)` tuple.
- **Thread regarding discriminator** — `sprk_communicationthread.sprk_regardingrecordtype` is **Text**
  (communication's is a **Lookup**). RegardingResolver needs a Lookup binding → add a **new** Lookup
  discriminator field (non-breaking), don't retype the Text field.
- **BFF Hygiene (root §10)** — this project touches `Sprk.Bff.Api`. Load
  `.claude/constraints/bff-extensions.md`; state Placement Justification; verify publish-size (R1 baseline
  ~46.99 MB, ceiling 60 MB) + CVE on every BFF task; use `Services/Ai/PublicContracts/` facades for AI.
- **Notification-spine** — align `threadId` + `kind` taxonomy with `spaarke-notification-spine-r1` (R2 is
  where that binds; R1 only polled).

---

## 🚨 MANDATORY: Task Execution Protocol

Once spec + plan + tasks exist, all task work MUST use the `task-execute` skill. Do NOT read POML files
directly and implement manually. Until then, the project is in **design → spec** phase.

---

## Next action

1. Get the owner's Q3 answer (coordination confirm).
2. Run `/design-to-spec` on `design.md` → `/project-pipeline` (which scaffolds spec/plan/tasks + registers
   the project on Portfolio #2 under Epic #431).
