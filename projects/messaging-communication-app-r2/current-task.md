# Current State — messaging-communication-app-r2 (Communication Workspace)

> **Last Updated**: 2026-07-18 (context-handoff before compaction)
> **Recovery**: Read "Quick Recovery" first. Two projects are in play — **R1 is deployed/merged/essentially done**; **R2 is in DRAFT DESIGN** (this is the active work).

---

## Quick Recovery (READ FIRST)

| Field | Value |
|-------|-------|
| **Active work** | **R2 = "Communication Workspace"** — draft design done; awaiting owner decisions on 5 questions, then `/design-to-spec` → `/project-pipeline`. |
| **R2 design** | [`projects/messaging-communication-app-r2/design.md`](design.md) (DRAFT). Investigation archive: [`notes/r2-resource-investigation.md`](notes/r2-resource-investigation.md). NO folder scaffolding beyond design + notes yet (no spec/plan/tasks/README/CLAUDE.md). |
| **Next action** | Get owner answers to the **5 open questions** (design §10), then run `/design-to-spec` on `design.md` → `/project-pipeline`. |
| **R1 status** | **28/29 tasks done, DEPLOYED + MERGED TO MASTER.** Only `090` wrap-up + owner config gates remain. Timeline verified working live. |
| **Status** | R2 = planning/design; R1 = code-complete + deployed, wrap-up pending. |

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

**5 OPEN QUESTIONS for owner (design §10) — need answers before spec:**
1. Participant index (junction) in R2, or defer + ship lookup-only person filter?
2. Category/tags — reuse an existing platform taxonomy entity, or new choice set?
3. Coordinate with `ai-spaarke-ai-workspace-UI-r2` — active? who owns the shared grid config + widget?
4. Standalone "All Communications" page needed, or widget + record panel enough?
5. Scope — Matter-first, or all 11 regarding-family entities?

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
