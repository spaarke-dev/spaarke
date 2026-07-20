# Communication Workspace — R2

> **Status**: ✅ **Code-complete** (2026-07-19) — 20/20 work tasks + 090 wrap-up; BFF build clean, **8654 tests pass / 0 fail**, publish ~46.24 MB, 0 new CVE. **Owner live-deploy gates pending** (Dataverse schema apply + PCF/page/config imports — MCP was offline this session; all authored + unit-tested per R1's build-and-defer-live pattern). See "Owner deploy gates" + "Open findings" below.
> **Created**: 2026-07-18 via `/project-pipeline` · **Code-complete**: 2026-07-19
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

1. [🔧] A Matter (and each of the other 10 regarding entities) shows its threads-as-groups → message timelines on the form. — *Built: regarding-mode `CommunicationTimeline` (020) + `CommunicationTimelineRegarding` PCF (021) + 11-form placement spec (022). Gated on owner: schema apply (002) + PCF import + form placement.*
2. [🔧] `by-regarding` + filtered `query` endpoints return access-filtered results across all 11 entity-sets. — *Built + unit/seam-tested (010/011/080; 11-entity pass, private/internal-only negatives, no-membership-union guard). Gated on owner: schema apply for live query.*
3. [🔧] Person filter (`participant=`) returns exact sender/recipient/Cc matches. — *Built (050 write + 051 facet; junction join, not text-LIKE; negative-access test). Gated on owner: `sprk_communicationparticipant` schema apply.*
4. [🔧⚠️] Thread regarding resolves via RegardingResolver on the thread form; name re-derives on regarding change but preserves user edits. — *Built: marker-gated re-derive (071) + RegardingResolver placement spec. ⚠️ **Open**: the re-derive **trigger** isn't wired — thread edits are client-side `Xrm.WebApi` and bypass the BFF; needs a Dataverse plugin on Update (see Open findings). Gated on owner: schema + PCF placement.*
5. [🔧] New messages always land in a sensible thread (subject → record-default → master). — *Built + tested (070, 3-tier ladder, never-null, characterization-green). Gated on owner: `sprk_isdefaultthread` schema apply.*
6. [🔧] Compose form captures Subject + structured To/Cc/Bcc; no message persists "(No Subject)". — *Built + tested (060; reused `RecipientField`, entityType tagging feeds the participant index). Gated on owner: shared-lib + PCF redeploy.*
7. [🔧] Standalone All-Communications page renders the grid with channel/person/date/regarding chips (config `e1826c4c-…`, no second default). — *Built + build-verified (040 page; 041 curation spec — note: regarding/person chips need an explicit `filterChips` block, channel/date auto-derive). Gated on owner: web-resource deploy + config JSON paste.*
8. [🔧] Rich `communications-list` widget renders in both LegalWorkspace and SpaarkeAi; dispatch unbroken. — *Built (030; upgraded in place, type string kept, dual-deploy wired, 172 tests). Gated on owner: dual redeploy. ⚠️ pre-existing Compose-dep gap blocks full prod bundle on both shells (see Open findings).*
9. [✅] BFF publish size ≤60 MB; no new HIGH CVE. — *Verified every BFF task: ~46.24 MB compressed excl-PDB (−0.75 vs R1's 46.99), 0 new CVE, 0 ADR violations.*

---

## Owner deploy gates (consolidated — nothing was applied live; MCP offline this session)

Everything below is **authored + unit/seam-tested against mocked boundaries**; live application is the owner's. Apply in order:

1. **Dataverse schema** — run `scripts/Deploy-ThreadRegardingSchema.ps1` (thread: 11 typed regarding lookups + `sprk_regardingrecordtype_ref` discriminator + `sprk_nameisautoderived` + `sprk_isdefaultthread`; keeps Text `sprk_regardingrecordtype`) and `scripts/Deploy-CommunicationParticipantSchema.ps1` (the `sprk_communicationparticipant` junction; **record the actual `sprk_role` integers** — proposed 100000000–3). `-WhatIf` first (describe-before-write).
2. **App-user privileges** — Create/Read/Append on `sprk_communicationparticipant`; carried-over R1 gates (`Communication__Acs__Endpoint`, Share on the two messaging tables, Delegate role + tables Read=User-level). See spec Dependencies.
3. **PCFs** — import `CommunicationTimelineRegarding` ZIP, then place on all 11 forms per `notes/022-pcf-form-placement.md` (per-entity primary-name: matter→`sprk_mattername`, project→`sprk_projectname`, event→`sprk_eventname`, account→`name`, contact→`fullname`, rest→`sprk_name`); place RegardingResolver on the thread form per `notes/071-regarding-resolver-thread-placement.md`.
4. **Code pages / widget** — deploy `sprk_communicationspage` web resource (`scripts/Deploy-AllDataGridConsumers.ps1 -Only sprk_communicationspage`); dual-redeploy the `communications-list` widget (LegalWorkspace + SpaarkeAi). Paste the `filterChips` block from `notes/041-grid-curation.md` into config `e1826c4c-…`.
5. **VisualHost card (optional)** — create the `sprk_chartdefinition` record per `notes/023-visualhost-summary-card.md`.
6. **Verify live** — run the seam-test scenarios against the real environment (access parity, 11-entity by-regarding, `participant=`).

## Open findings (surfaced honestly, not papered over)

- **[MED] Naming re-derive trigger not wired (Criterion 4).** `ThreadResolver.ReDeriveThreadNameAsync` exists + is marker-gated, but nothing invokes it on a regarding change — thread edits are client-side `Xrm.WebApi` writes that bypass the BFF. Follow-on: a Dataverse plugin on `sprk_communicationthread` Update. (Task 071.)
- **[MED] "Unread" count has no backing field (Criterion, VisualHost 023).** No read/unread-tracking field exists in the R1/R2 schema; the count card ships count-only. A future read-state field + the `MetricCard onViewListClick` wiring (`ChartRenderer.tsx`) would enable unread + exact drill-through. (Task 023.)
- **[MED, pre-existing, not R2] Full prod bundle blocked on both shells.** LegalWorkspace (`mammoth`/`@spaarke/document-operations`) + SpaarkeAi (`@tiptap/extension-unique-id`) have unmet Compose-feature deps that block the *full* production bundle; R2's widget `tsc`-compiles clean and the surface builds. Recommend a separate follow-up. (Task 030.)
- **All live-Dataverse + deploy verification deferred** (MCP offline) — see Owner deploy gates. Consistent with R1's build-and-defer-live close-out.

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
