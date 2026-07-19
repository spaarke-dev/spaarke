# Current State — messaging-communication-app-r2 (Communication Workspace)

> **Last Updated**: 2026-07-18 (context-handoff before compaction)
> **Recovery**: Read "Quick Recovery" first. Two projects are in play — **R1 is deployed/merged/essentially done**; **R2 is in DRAFT DESIGN** (this is the active work).

---

## Quick Recovery (READ FIRST)

| Field | Value |
|-------|-------|
| **Active work** | **R2 = "Communication Workspace"** — Wave 0 underway. **Task 001 (Phase-0 audit spike) ✅ COMPLETE 2026-07-19** — findings note at [`notes/001-phase0-schema-audit.md`](notes/001-phase0-schema-audit.md). **Task 040 (standalone All-Communications page, W4/FR-11) ✅ BUILT 2026-07-19** (run early, out of wave order, deps-free): `src/solutions/sprk_communicationspage/` shell + `Deploy-AllDataGridConsumers.ps1` registration; vite `dist/index.html` builds clean (GUID `e1826c4c-…` baked, no `parentContext`, no second config, no sitemap per Q-B). **Live `pac`/web-resource deploy DEFERRED to owner** (deploy gate). **Task 003 (participant junction, W0-B/FR-08) ✅ AUTHORED 2026-07-19**: `scripts/Deploy-CommunicationParticipantSchema.ps1` + `notes/003-communicationparticipant-schema.md` — entity + 6 locked fields + primary name; Org-owned + Cascade parent lookup mirror `sprk_communicationattachment`; person lookups RemoveLink; sprk_role local Choice From/To/Cc/Bcc=100000000-3. **LIVE APPLY DEFERRED** (MCP down — owner runs script `-SolutionUniqueName <messaging solution>` + records actual ints before 050/051). Unblocks 050+051. **Task 002 (thread typed-regarding lookups + markers, W0-B/FR-06) ✅ AUTHORED 2026-07-19**: `scripts/Deploy-ThreadRegardingSchema.ps1` + `notes/002-thread-regarding-schema.md` — 14 additive columns on `sprk_communicationthread` (11 typed `sprk_regarding{...}` lookups mirroring `RegardingFieldMap.All`; NEW Lookup discriminator `sprk_regardingrecordtype_ref`→`sprk_recordtype_ref`; `sprk_nameisautoderived` Boolean default Auto for 071; `sprk_isdefaultthread` Boolean default No for 070). Discriminator named `_ref` (plain name is in-use Text field, MUST-NOT-RETYPE); satisfies RegardingResolver's dynamic discovery so 071 binds it 0-code. Text `sprk_regardingrecordtype` untouched (script guards it); no category/tags/description (Q2). **LIVE APPLY DEFERRED** (MCP down — owner runs script `-SolutionUniqueName <messaging solution>` + MCP-verifies before 010/070/071). Unblocks 010+070+071. No active task. |
| **Next action** | `continue` → Wave **W0-B** tasks **002** (thread typed-regarding lookups + markers, FR-06) + **003** (participant junction, FR-08) — both unblocked by 001, `parallel-safe:true`, can run in parallel. **004** (junction ADR, `.claude/`) runs main-session. All consume `notes/001-phase0-schema-audit.md`. All task work via `task-execute`. |
| **001 result** | email-r4 `Services/Communication` merged-present (8/8 ref files); `RegardingFieldMap.All` 11 lookups captured verbatim; thread delta pinned (add 11 typed lookups + Lookup discriminator + naming-edited + default-thread markers; keep Text `sprk_regardingrecordtype`; Q2 category/tags/description ABSENT); no `sprk_communicationparticipant` junction exists (003 net-new); `sprk_role` = From/To/Cc/Bcc = 100000000/1/2/3. ⚠️ Live MCP re-verify deferred (MCP down) — gate before 002/003 apply. |
| **Artifacts** | [`spec.md`](spec.md) (12 FR/8 NFR) · [`plan.md`](plan.md) (9-wave WBS) · [`README.md`](README.md) · [`CLAUDE.md`](CLAUDE.md) · [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) (24 tasks). |
| **Base** | Branch synced to latest master 2026-07-18 (merge brought assistant-r1 + email-r4 checkpoint work; email-r4 `Services/Communication` merged — R2 builds additively). |
| **Locked decisions** | Q1=**build participant junction** · Q2=**no category/tags** · Q3=**upgrade grid/widget in place** · Q4=**ship standalone page** · Q5=**all 11 entities** · Q-C=**two typed lookups** · Q-D=**write unresolved-address rows** · Q-E=**stay polling, no spine dependency**. |
| **Coordination** | `/conflict-check` before every BFF wave. Shared-path edits = **050** (participant write) + **070** (auto-threading) — `parallel-safe:false`. Widget **030** merge-order vs dataset-grid-r2 + PR #508. |
| **Status** | R2 = **initialized, ready to execute**; R1 = complete + deployed + archived. |

> **✅ POML task files**: all 21 `tasks/NNN-*.poml` files authored (6 parallel wave-authors) + validated
> (`Validate-TaskPoml.ps1` → 21 clean / 0 errors). Pipeline **paused before Step 5 wave execution** at owner
> request — ready for `work on task 001` (or `continue`).

---

## R1 final state (this session's ops work — all on master)

- **Deployed BFF** to `spaarke-bff-dev` (master `04fb31ecf`). Messaging endpoints live (401, not 404). Timeline PCF renders real messages on the thread form (verified by owner screenshot).
- **Deployed PCFs**: `CommunicationTimeline` v1.0.1 + `CommunicationMessageActions` v1.0.2 — both fixed with a **bound `anchorField` property** (a code component only appears in the form component library if it declares a bound field; the original had none). ZIPs under each PCF's `Solution/bin/`.
- **Merged to master**: PR #655 (messaging), #658 (ACS boot-safety + format), #659 (CI tier2 concurrency fix). Branch `work/messaging-communication-app-r1`.
- **Fixed 3 production issues this session**:
  1. **ACS boot-safety** — the BFF crash-looped at startup (SIGABRT/134) when `Communication__Acs__Endpoint` was unset (ACS client factory threw, resolved eagerly via `MembershipReconcileSweepService`). Fixed: `AcsIdentityService`/`AcsThreadService` now inject `Lazy<client>` (ADR-032). Regression test `AcsBootSafetyTests`. **Set `Communication__Acs__Endpoint=https://spaarke-acs-dev.unitedstates.communication.azure.com` on dev.**
  2. **PCF bound-property** (above).
  3. **CI `CI / Router` spurious red** — tier2 concurrency keyed on `github.ref` cancelled superseded master runs; alls-green treats cancelled tier2 as fail. Fixed: per-SHA concurrency group.
- **⚠️ Deploy prerequisite (binding for staging/prod)**: any env running this BFF MUST set `Communication__Acs__Endpoint` (dev is set; boot-safety fix means missing config no longer crashes, but SEND still needs it).
- **R1 open findings** (for the 090 wrap-up PR): send-into-existing-thread (MED), messaging-archival gap (MED — chat not SPE-archived), DI-cycle-refactor (LOW), config gate (app-user Share privilege on both messaging tables). Owner still to import nothing further (PCFs deployed).

---

## R2 design — key conclusions (from the 5-part investigation)

**Big insight: R2 is smaller than it looked — surfaces 2 & 3 already exist** (built by `ai-spaarke-ai-workspace-UI-r2`).

- **Surface 1 (record threads view) = NEW** — extend `CommunicationTimeline` to a **regarding-mode** (takes a Matter, calls a new `by-regarding` BFF endpoint, renders threads→timelines). Reuses the component + impersonation access filter. Optional VisualHost count card (client-fetch — tension T-1).
- **Surface 2 (grid) = ALREADY BUILT** — config record `sprk_gridconfiguration` GUID `e1826c4c-9575-f111-ab0e-7ced8ddc4a05`; framework auto-derives channel/person/date/regarding chips. R2 = optional ~50-line standalone page.
- **Surface 3 (SpaarkeAi widget) = EXISTS THIN** — `communications-list` widget is grid-only; R2 upgrades in place to rich Calendar-style (copy `CalendarWorkspaceWidget`), new lib `@spaarke/communication-components`.
- **CC-1 thread regarding** — add typed `sprk_regarding*` lookups + a **new Lookup discriminator** (the thread's `sprk_regardingrecordtype` is Text, not a Lookup — can't bind RegardingResolver to it); place RegardingResolver PCF on thread form (0 code). Naming re-derive + category/tags = new. Don't use CommunicationConnections/Field-Mapping.
- **CC-2 person filter** — sender/recipients are `;`-joined TEXT, not queryable. Needs a new **`sprk_communicationparticipant` junction** (reuse `ParticipantCorrelationRung` email→contact resolution; align ADR-034 tuple). Interim: lookup-only (`sprk_sentby`/`sprk_regardingperson`).
- **CC-3 read endpoints** — `by-regarding` + filtered `query`; extend `CommunicationThreadReadService` + `IImpersonatedCommunicationQuery` + `ICommunicationAccessFilter`. **Do NOT** reintroduce membership-union on reads (retired 2026-07-16).
- **CC-4 auto-threading** — subject → record-default → master.
- **CC-5 compose enrichment (just added)** — the R1 compose form has NO Subject/Cc/Bcc → every msg is `sprk_subject="(No Subject)"`, `sprk_name="Message: (No Subject)"`, and `To`→`sprk_to` TEXT. R2 adds Subject/topic (feeds naming), structured recipient picker (feeds the participant index), Cc/Bcc. Reuse existing `RecipientField`.

**OWNER DECISIONS (locked 2026-07-18, design §10):**
1. Participant index → **BUILD the `sprk_communicationparticipant` junction in R2** (no lookup-only interim). W5 mandatory.
2. Category/tags → **NOT in R2.** Threads = regarding + name only. Removes taxonomy check from W0.
3. Coordination with `ai-spaarke-ai-workspace-UI-r2` → **STILL OPEN** (one-line confirm needed): sanction upgrading their shipped `sprk_gridconfiguration` (`e1826c4c-…`) + `communications-list` widget **in place**. Only gate left before `/design-to-spec`.
4. Standalone "All Communications" page → **SHIP IT** (~50-line shell, copy `sprk_invoicespage`; register in `Deploy-AllDataGridConsumers.ps1`).
5. Scope → **ALL 11 regarding-family entities** from day one (Surface 1 on all 11 forms; W1/W4 test matrix expands; `by-regarding` endpoint already entity-set-agnostic).

**Net: R2 is the full-breadth build** — bigger on entities/junction/page, smaller by dropping category/tags.

**Message compose field mapping (as-built, for reference)**: To→`sprk_to` (text, `;`-joined) · Body→`sprk_body` · Rich/Plain→`sprk_bodyformat` (HTML=100000001/Plain=100000000) · Attach→`sprk_communicationattachment` child rows · (auto) type=Message(100000004), direction=Outgoing(100000001), from=`sprk_from`, sentat, name="Message: {subject}", thread lookup, inReplyTo, acsmessageid/acsthreadid. Persist: `CommunicationService.cs:695-727`.

---

## Files created/modified this session (R2)
- `projects/messaging-communication-app-r2/design.md` (NEW — draft, with CC-1..CC-5, 3 surfaces, hot-path decl, §11 reuse ledger, 8 waves, 5 open Qs).
- `projects/messaging-communication-app-r2/notes/r2-resource-investigation.md` (NEW — 5-audit findings archive with exact file paths).
- `projects/messaging-communication-app-r2/current-task.md` (this file).

## Recovery Instructions
1. Read Quick Recovery + "R2 design key conclusions" above.
2. If owner has answered the 5 questions → run `/design-to-spec` on `design.md`, then `/project-pipeline`. Otherwise, present the 5 questions.
3. R1: if asked to finish, task `090` wrap-up (README→Complete, `/test-diet`, lessons-learned, surface the open findings). R1 branch `work/messaging-communication-app-r1` still exists (merged, not deleted).
4. Do NOT re-run the R2 investigation — findings are in `notes/r2-resource-investigation.md`.
