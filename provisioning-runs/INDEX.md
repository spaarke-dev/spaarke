# Provisioning-Runs Registry

> **Created by**: `customer-provisioning-orchestration-r1` task 203a per punch list row A05.
> **Source-of-truth spec**: [`projects/customer-provisioning-orchestration-r1/notes/provisioning-run-structure-design.md`](../projects/customer-provisioning-orchestration-r1/notes/provisioning-run-structure-design.md).
> **Analog of**: [`projects/INDEX.md`](../projects/INDEX.md) — one row per coding project ↔ one row per provisioning run.

Every folder under `provisioning-runs/` (except `_archive/` and `_templates/`) is one bounded customer-provisioning transaction with (a) declared intake, (b) tracked handler progress, (c) manual-gate log, (d) postmortem lessons. Runs older than 90 days move to `_archive/` preserving the same 8-file structure.

Runs are authored INCREMENTALLY by `/provision-environment` skill during execution — Step 0 creates the folder + `intake.md`; Step 5 appends `manual-gates.md`; Step 7 writes `lessons-learned.md`. Templates for each per-run file live at [`_templates/`](_templates/).

Storage discipline: `provisioning-runs/` lives in the **main git repo at root**, NOT in per-worktree copies. Worktrees don't own runs; runs belong to Spaarke platform ops.

---

## Active runs

| runId | customerId | tenancyModel | profile | started | ended | status | lessons-count | tenant-hot-path* | link |
|-------|-----------|--------------|---------|---------|-------|--------|---------------|------------------|------|
| — | — | — | — | — | — | — | — | — | — |

_No runs yet. First live-fire target sub `cd95fcec-6b89-49ea-8339-c2b579b12587` per task 186._

`*tenant-hot-path`: Y if the run touched a shared tenant resource that another concurrent run might contend on (SPE container-type, shared BFF Entra app-reg, per-tenant KV RBAC bootstrap). Feeds the shared-tenant coordination workflow (analog of `/conflict-check`). Updated atomically by skill Step 6 (Completion Handoff) — no cron.

## Archived runs (>90 days old)

See [`_archive/`](_archive/) — same 8-file per-run structure preserved. Move by human decision after the run's postmortem has been rolled up into [`PROVISIONING-PREREQUISITES.md`](../docs/guides/PROVISIONING-PREREQUISITES.md) / [`.claude/patterns/provisioning/`](../.claude/patterns/provisioning/) updates.

## Per-run folder shape (from structure design)

```
{customerId}-{runId}/
├── CLAUDE.md                # per-run AI context (loaded when in dir)
├── intake.md                # analog of spec.md — operator's inputs + decisions
├── prerequisites-check.md   # PROVISIONING-PREREQUISITES.md verification snapshot
├── preflight-report.md      # H0 output + Step 2 preflight report
├── handler-log.md           # analog of current-task.md — LIVE handler progress
├── manual-gates.md          # H0.5 / H8 / H11 / any WaitingOnGate transitions
├── handoff-report.md        # final report — supersedes runs/{runId}.md
└── lessons-learned.md       # mandatory Step 7 postmortem
```

Templates at [`_templates/`](_templates/) — copy on run start.
