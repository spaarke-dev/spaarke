# Current Task State — messaging-communication-app-r1

> **Last Updated**: 2026-07-16 (task-execute — task 050 in progress)
> **Recovery**: Read "Quick Recovery" first, then "Remaining Plan". Branch `work/messaging-communication-app-r1`, PR #655 (draft).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | messaging-communication-app-r1 — 2nd communication channel (ACS Chat) over ADR-045 seams |
| **Progress** | **20 of 28 tasks ✅**; owner config gates SATISFIED (Delegate role + User-level Read on messaging tables, 2026-07-16) |
| **Active task** | **050 COMPLETE** ✅ — committing next. |
| **Next Action** | **task 043** (1:1 direct threads — narrow thread ownership to the two participants; fills 041's `IThreadExplicitParticipantReader` Null-Object seam; `sprk_threadtype=Direct 1:1`=100000001). Deps 040✅. Then UI wave 060–063, then 080/081, then 090. |
| **Status** | in-progress (050 done) |

### Task 050 — design decisions (locked)
- **New Scoped service** `CommunicationThreadReadService` (Services/Communication/). Rationale: reuses Scoped `ICallerSystemUserResolver` → can't live in Singleton `CommunicationService` (captive-dependency). §11-justified.
- **Reads via impersonation**: `IImpersonatedCommunicationQuery` (thin ADR-010 test-seam adapter over `DataverseWebApiService.RetrieveMultipleImpersonatedAsync`) — record access = Dataverse's job (MSCRMCallerID=caller systemuserid).
- **Filter reuse**: map impersonated JSON rows → `Entity` → `ICommunicationAccessFilter.FilterMessages` (internal-only + privilege). NO second filter.
- **Caller resolution**: reuse `ICallerSystemUserResolver` (oid→systemuserid). Unresolved/Empty → 403 ProblemDetails (fail closed; no app-only fallback).
- **Last-seen source**: caller-supplied `since` query param (ISO-8601). NO new marker table (R1 §11 minimalism; polling client tracks newest rendered).
- **Attachments**: ONE bulk impersonated query on `sprk_communicationattachment` filtered by the page's message ids, grouped in-memory (2 queries/read total — no per-row fan-out, NFR-07).
- **IsInternalUser = true** for R1 (resolved systemuser = internal; external contacts = R2).
- **Routes**: `GET /api/communications/threads/{threadId:guid}/messages` + `.../unread-count` (existing group, `.RequireAuthorization()` + `CommunicationAuthorizationFilter`).
- **As-built names**: message→thread lookup `sprk_communicationthread` (OData `_sprk_communicationthread_value`); `sprk_bodyformat`/`sprk_communicationtype`/`sprk_privilegeclassification` = optionsets (int in JSON); `sprk_isinternalonly` = bool.

### Critical Context
- **Access model DECIDED (impersonation)** — read enforcement = Dataverse impersonation (`MSCRMCallerID` = caller **systemuserid**, resolved from oid). 042 reworked to app-flags only (internal-only + privilege-metadata). Full detail: [`notes/access-model-decision.md`](notes/access-model-decision.md). Granting = OOB "Manage access" (POA), no custom grant table.
- **🔧 OWNER CONFIG GATE (for live)** — impersonation + private threads need: (1) BFF app user gets Delegate role `prvActOnBehalfOfAnotherUser`; (2) messaging tables (`sprk_communicationthread`, `sprk_communication`) role Read = **User-level**. All code is unit-tested; live integration deferred behind these.
- **Live ACS resource** `spaarke-acs-dev` (endpoint `https://spaarke-acs-dev.unitedstates.communication.azure.com`, sub `484bc857-…`, tenant `a221a95e-…`) — the ACS round-trip is live-validated (spike harness). Event Grid → BFF webhook (inbound live test) still needs a publicly reachable BFF URL (deferred).
- **AS-BUILT schema names** (verified live) — message→thread lookup = `sprk_communicationthread` (NOT sprk_thread); ACS thread id = `sprk_acsthreadid`; ACS msg id = `sprk_acsmessageid`; child-ref→thread = `sprk_thread`. Full table in [`notes/messaging-schema-spec.md`](notes/messaging-schema-spec.md).
- **Echo-dedup key** = `acs-msg:{ProviderMessageId}` (`IncomingMessagingJobHandler.IdempotencyKeyFor`); 051 marks it on send so the Event Grid echo is a no-op.

---

## Remaining Plan (9 tasks) — resume order

| # | Task | Notes for the implementer |
|---|---|---|
| **050** | BFF thread-read + unread-count endpoints | Use `DataverseWebApiService.RetrieveMultipleImpersonatedAsync(entitySet, odata, callerSystemUserId)` (built in the 042 rework) → get user-visible rows → apply `ICommunicationAccessFilter.FilterMessages` (internal-only + privilege app-flags) → return. Unread = same filter on since-last-seen. Resolve oid→systemuserid server-side (see `UserPrivilegeChecker`/`CommunicationAccessContext.CallerSystemUserId`). ADR-019 ProblemDetails on whole-thread denial. Deps 040✅/042✅/004✅. **Blocks 060.** |
| **043** | 1:1 direct threads | Narrow thread OWNERSHIP so exactly the two participants have access (impersonation enforces). Explicit two-participant list feeds 041's `IThreadExplicitParticipantReader` seam (currently Null-Object). `sprk_threadtype = Direct 1:1 (100000001)`. Deps 040✅. |
| **060** | Polling timeline component (`@spaarke/ui-components`, Fluent v9) | Interleaved email+chat, reply nesting (`sprk_inreplyto`), compose box, unread indicator, ~5s poll of 050's endpoint. **NO client-side ACS SDK** (NFR-04 — acceptance criterion). Reuse `<EmailComposer/>` sub-components. `npm run build`. Deps 050. |
| **061** | Package timeline as PCF + deploy | React 16/17 platform libs (ADR-022); `npm run build:prod`; `pcf-deploy`; `<ui-tests>` (dark mode, inbound-within-one-poll, no-ACS-SDK bundle check). Deps 060. |
| **062** | PCF send/respond accessories | On OOB form, mirror email-r4 `CommunicationActions`; calls 051 send path. Deps 051✅/060. |
| **063** | Bidirectional content quoting | `quoteBody()` helper reading `sprk_body`/`sprk_bodyformat`; email↔message; no `.eml` parse. Deps 060. |
| **080** | Vertical-slice seam tests | ADR-038 `tests/integration/seam/**`: send/archive/ingest, IThreadResolver, privacy; preserve email characterization. TEST-MODIFYING (9.5 unconditional). Deps 031✅/040✅/042✅/051✅. |
| **081** | Architecture doc | Extend `docs/architecture/` comm doc with thread model + ACS transport + ingestor seam + **impersonation access model**; wire ADR-046. Deps 040✅/007✅. |
| **090** | Wrap-up | README→Complete, lessons-learned, `/test-diet`, archive, final TASK-INDEX reconcile. Main-session (`.claude/`). |

### Orchestration notes for next session
- **Serial vs parallel**: BFF tasks that touch `CommunicationModule.cs` / `CommunicationChannelDispatcher` / `CommunicationService` must be **serial on the main tree** (parallel agents clobber shared files). Genuinely disjoint tasks parallelize via **isolated worktrees** (`isolation: "worktree"`) then cherry-pick — proven for 070/041. ⚠️ `.claude/worktrees/` is gitignored; do NOT `git add -A` while an isolated worktree is live if it's not ignored — use explicit paths.
- **Per task**: dispatch a `task-execute` agent with its model-tier/effort from TASK-INDEX; instruct it NOT to touch `TASK-INDEX.md`/`current-task.md` (main session reconciles); leave changes in working tree; main session build-verifies (`dotnet build src/server/api/Sprk.Bff.Api/` + Communication test filter), commits, pushes, marks the index row ✅.
- **BFF gates every task**: build, publish-size (~45.6 MB, ceiling 60), CVE (pre-existing Kiota HIGH only — report NEW), 9.5 code-review+adr-check, `/conflict-check`. Cite bff-extensions Placement Justification.
- **UI wave (060–063)** is client TS — `npm run build`/`build:prod`, no publish-size/CVE; different surface from BFF (parallelizable with server work).

---

## Decisions Made (this project)

- 2026-07-16: Grouping key = (A) `sprk_communicationthread` entity + lookup (design Q4).
- 2026-07-16: UI = OOB main form + PCFs (ADR-026 Path-A exception).
- 2026-07-16: `CommunicationType.Message = 100000004` (Dataverse choice exists).
- 2026-07-16: `IThreadResolver` invoked from ORCHESTRATORS not the pure `ThreadContinuityRung` (more ADR-045-compliant; 040).
- 2026-07-16: **Privilege = metadata-only, composes with privacy** (owner) — never independently gates; no AI (ADR-015).
- 2026-07-16: **Private-grant = OOB "Manage access" (POA)**, not a custom table (owner). Provider reads `principalobjectaccessset` (mirror `PlaybookSharingService`).
- 2026-07-16: **Read enforcement = Dataverse impersonation** (owner) — supersedes 042's hand-computed union; honors design §5. `RetrievePrincipalAccess` for discrete gates.
- 2026-07-16: Dataverse additive-union rule confirmed (research memoized: `.claude/agent-memory/researcher/dataverse-record-access-security-2026-07-16.md`).

---

## Files / Artifacts map
- Spec/design/plan: `spec.md`, `design.md`, `plan.md`
- Task registry: `tasks/TASK-INDEX.md` (19 ✅, 9 🔲)
- Access model: `notes/access-model-decision.md` (authoritative)
- Schema (as-built): `notes/messaging-schema-spec.md`
- Spikes: `notes/spikes/00{1,2,3}-*.md` + `acs-harness/`
- Provisioning runbook: `notes/012-acs-provisioning-runbook.md`
- ADR-046: `.claude/adr/ADR-046-*.md` (concise, Accepted) + `docs/adr/ADR-046-*.md` (full)
- Portfolio: GitHub Project #654 (Epic #431), draft PR #655
- New server code lives in `src/server/api/Sprk.Bff.Api/Services/Communication/{Acs,Channels,Threads,Membership,Access,Engine}/` + `Services/Jobs/Handlers/` + `Api/AcsEventGridEndpoints.cs`; impersonation in `src/server/shared/Spaarke.Dataverse/DataverseImpersonation.cs`.

---

## Blockers
**Status**: None blocking code. **Live integration gated on owner config** (Delegate role + User-level table Read) + a reachable BFF webhook for inbound Event Grid.

---

## Recovery Instructions
1. Read Quick Recovery + Remaining Plan above.
2. `git status` (expect clean, synced) + `git log --oneline -5`.
3. To resume: "continue" (→ first 🔲 = task 050) or "work on task 050". Each task via `task-execute`.
4. Load `notes/access-model-decision.md` + `notes/messaging-schema-spec.md` before any read/endpoint/schema work.
5. Remind owner of the two config to-dos before live verification.
