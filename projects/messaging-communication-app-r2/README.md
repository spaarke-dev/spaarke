# Communication Workspace — R2

> **Status**: 🚧 **Initialized** — pipeline complete; 21 tasks / 9 waves; ready for Wave 0 (task 001).
> **Created**: 2026-07-18 via `/project-pipeline`
> **Branch**: `work/messaging-communication-app-r2` (synced to latest master 2026-07-18)
> **Portfolio**: [Project #662](https://github.com/spaarke-dev/spaarke/issues/662) · Epic [#431 EMAIL & MESSAGING](https://github.com/spaarke-dev/spaarke/issues/431) · [Board #2](https://github.com/users/spaarke-dev/projects/2) · Status: **Active** · Start 2026-07-18
> **Follows**: [`messaging-communication-app-r1`](../messaging-communication-app-r1/) (Complete, merged, archived 2026-07-18).

R2 is the **read / query / organize layer** on top of R1's messaging channel. R1 shipped transport, capture, the thread data model, and a per-thread polling Timeline. R2 makes communications **findable and organized across records and people**: a record-level threads view on all 11 regarding-family entities, a standalone all-communications view, a rich workspace widget, thread regarding-resolution, a queryable participant index, an auto-threading policy, and a richer compose form. The R1 data model already supports the core experience — so R2 is **mostly read surface + UI + two schema deltas**, not a schema migration.

---

## Documents

| File | Purpose |
|---|---|
| [`spec.md`](spec.md) | AI-optimized spec (12 FRs, 8 NFRs) — implementation source of truth |
| [`design.md`](design.md) | Investigation-grounded design — 3 surfaces, CC-1..CC-5, hot-path decl, §11 reuse ledger, 8 waves, §10 locked decisions |
| [`plan.md`](plan.md) | Wave WBS + critical path + discovered resources + coordination |
| [`current-task.md`](current-task.md) | Active task state (context recovery) |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | All tasks + status + parallel groups + dependency graph |
| [`notes/r2-resource-investigation.md`](notes/r2-resource-investigation.md) | 5-part reuse audit (exact file paths) — do NOT re-run |

---

## Graduation Criteria (spec Success Criteria)

> Legend: ✅ built + verified · 🔧 built + deployed, **gated on owner config** · ⚠️ built, follow-up open · 🔲 not started

1. [🔲] A Matter (and each of the other 10 regarding entities) shows its threads-as-groups → message timelines on the form. — *Verify: record with ≥2 threads across ≥2 months; Jun/Feb/Jul grouping, email+chat interleaved.*
2. [🔲] `by-regarding` + filtered `query` endpoints return access-filtered results across all 11 entity-sets. — *Verify: seam tests (≥3 entity-sets + private-thread-hidden + per-facet); 401/200.*
3. [🔲] Person filter (`participant=`) returns exact sender/recipient/Cc matches. — *Verify: send with structured To/Cc; query `participant={personId}`; role-correct junction rows (not text-LIKE).*
4. [🔲] Thread regarding resolves via RegardingResolver on the thread form; name re-derives on regarding change but preserves user edits. — *Verify: regarding change → name updates while marker=auto; edit → marker=edited → preserved.*
5. [🔲] New messages always land in a sensible thread (subject → record-default → master). — *Verify: send with/without subject, with/without existing thread; non-null thread each tier.*
6. [🔲] Compose form captures Subject + structured To/Cc/Bcc; no message persists "(No Subject)". — *Verify: `sprk_subject`, meaningful `sprk_name`, populated participant rows, `sprk_cc`/`sprk_bcc`.*
7. [🔲] Standalone All-Communications page renders the grid with channel/person/date/regarding chips (config `e1826c4c-…`, no second default). — *Verify: chips auto-derived; single default config.*
8. [🔲] Rich `communications-list` widget renders in both LegalWorkspace and SpaarkeAi; dispatch unbroken. — *Verify: dual-deploy; card strip + chips + grid + row modal; type string unchanged.*
9. [🔲] BFF publish size ≤60 MB; no new HIGH CVE. — *Verify: `dotnet publish` size + `dotnet list package --vulnerable` on every BFF task.*

---

## Scope Guardrails (owner-locked, spec §10 + resolved questions)

- ✅ Q1 **Build `sprk_communicationparticipant` junction** (message grain, 2 typed lookups, unresolved-address rows).
- ✅ Q2 **No category/tags** — threads organized by regarding + name only.
- ✅ Q3 **Upgrade the shipped grid config + `communications-list` widget in place** (ai-spaarke-ai-workspace-UI-r2 is Complete — no fork).
- ✅ Q4 **Ship the standalone All-Communications page** (widget/launcher only, no sitemap in R2).
- ✅ Q5 **All 11 regarding-family entities** from day one.
- ❌ MUST NOT add a **second reads access mechanism** or reintroduce **membership-union on reads** (retired 2026-07-16).
- ❌ MUST NOT **retype** the Text `sprk_regardingrecordtype` (add a new Lookup discriminator instead).
- ❌ MUST NOT create a **second grid config default** for `sprk_communication` or a **second widget**.
- ❌ MUST NOT render message **content** via VisualHost client-fetch (count card only — T-1).
- ❌ MUST NOT use CommunicationConnections PCF or the Field Mapping Framework for thread regarding.
- ❌ MUST NOT build a parallel push/fan-out — R2 stays BFF-polling; reserves the notification-spine `communication-arrived` kind only (NFR-06).

---

## Deploy prerequisites (owner, at deploy time)

- App user: **Create + Read + Append/AppendTo** on `sprk_communicationparticipant`.
- Carried over from R1: `Communication__Acs__Endpoint` set; app-user **Share** on `sprk_communication` + `sprk_communicationthread`; app-user **Delegate role + tables Read=User-level** (impersonated reads). See spec Dependencies.

---

## Execution

All task work MUST use the `task-execute` skill (root CLAUDE.md §4). To begin:

```
work on task 001
```

or `continue` to pick up the first 🔲 in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

**Coordination**: run `/conflict-check` at start and **before every BFF wave** — R2 edits shared `Services/Communication/` code (tasks 050, 070). email-r4's BFF work is merged to master (this worktree is synced); build additively. Widget task 030 coordinates merge-order with `spaarke-dataset-grid-framework-r2` + PR #508.
