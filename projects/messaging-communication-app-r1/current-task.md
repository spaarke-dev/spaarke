# Current Task State — messaging-communication-app-r1

> **Last Updated**: 2026-07-17 (context-handoff — 25 done, 4 remain; ready for /compact)
> **Recovery**: Read "Quick Recovery" first, then "Remaining Plan". Branch `work/messaging-communication-app-r1` (HEAD `872ff6d56`), PR #655 (draft). All work committed + pushed; tree clean.
> **Note**: total task count is now **29** (task **052** open-thread-grant was added mid-project). 25 ✅, 4 🔲 (061, 080, 081, 090).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | messaging-communication-app-r1 — 2nd communication channel (ACS Chat) over ADR-045 seams |
| **Progress** | **25 of 28 tasks ✅** — server-side + all client UI (timeline, accessories, quoting) + send-contract closure done. Owner config gates: Delegate role + User-level Read SATISFIED; **app-user Share privilege on `sprk_communicationthread` + `sprk_communication`** needed for GrantAccess/POA (043/052). |
| **Active task** | **063 COMPLETE** ✅ (quoteBody + quote actions). Remaining: **061** (package `CommunicationTimeline` as PCF + DEPLOY — needs owner env), **080** (vertical-slice seam tests, TEST-MODIFYING), **081** (architecture doc), **090** (wrap-up + `/test-diet`). |
| **Next Action** | **task 061** — package `CommunicationTimeline` as a form-bound PCF (build + pack ZIP; DEFER `pac solution import` to owner like 062). Then 080 (seam tests), 081 (arch doc), 090 (wrap). All remaining are code/docs — no more design-decision forks. |
| **Status** | in-progress (all UI logic done; PCF packaging + tests + docs + wrap remain) |

### 🚚 OWNER DEPLOY HANDOFFS (packaged, awaiting owner's Dataverse env)
- **062 PCF**: `src/client/pcf/CommunicationMessageActions/Solution/bin/CommunicationMessageActionsSolution_v1.0.0.zip` — `pac solution import --path ... --publish-changes`; place on `sprk_communicationthread` + `sprk_communication` forms; uses existing `sprk_MsalClientId`/`sprk_BffApiAppId`/`sprk_BffApiBaseUrl` env vars (no new ones). Full steps in the 062 commit body.
- **061 (pending)**: will package `CommunicationTimeline` as a form-bound PCF similarly for owner import.

### 🔔 OPEN FINDINGS (status)
1. ✅ **RESOLVED — Open-thread message access gap** (was HIGH): closed by **task 052** (grant Open-thread msgs to the 041-derived set at persist — option b). Task-050 impersonated reads now return matter-thread msgs.
2. **Send-into-existing-thread gap** (MED, OPEN): `/api/communications/send` always creates a NEW ACS thread (`AcsThreadId` always null; `SendCommunicationRequest` has no `ThreadId`). Multi-message continuity into an existing thread isn't wired via the generic send path. Pre-existing 051 limitation, out of 043/052 scope. **Follow-up task candidate for R1-polish or R2** — surface to owner; does NOT block the polling timeline (which reads persisted rows, not ACS).
3. ✅ **Owner config gate recorded**: app-user **Share** privilege on both messaging tables (access-model-decision.md prereq #4).
4. **Code-quality Suggestion (LOW, deferred)**: 052 broke a real DI cycle with `Lazy<>`; a cleaner future refactor could extract the Direct participant-reader to remove the cycle structurally.

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

## Remaining Plan (4 tasks) — resume order: 061 → 080 → 081 → 090

| # | Task | Notes for the implementer |
|---|---|---|
| **061** | Package `CommunicationTimeline` as a form-bound PCF + deploy | Wrap the shipped `@spaarke/ui-components` `CommunicationTimeline` (task 060 ✅) in a PCF host (mirror the **062** `CommunicationMessageActions` PCF just built — same host/auth/manifest pattern, `src/client/pcf/CommunicationMessageActions/` is the template). React 16/17 platform libs (ADR-022); Fluent v9; `@spaarke/auth` at boundary only; **grep the bundle for no `@azure/communication`** (NFR-04 hard gate); `npm run build:prod` (NOT `build`); pack the Solution ZIP. **DEFER `pac solution import` to owner** (owner has env) — document form/section placement (bind to `sprk_communication`/thread form) in the commit like 062. Deps 060✅. |
| **080** | Vertical-slice seam tests (C#) | ADR-038 `tests/integration/seam/**`: send/archive/ingest, `IThreadResolver`, privacy/access; preserve email characterization. **TEST-MODIFYING → 9.5 gates UNCONDITIONAL** (root §8). Deps 031✅/040✅/042✅/051✅/052✅. Note: also exercise the 052 open-thread grant + 043 direct-thread membership if feasible. |
| **081** | Architecture doc | Extend/refresh a `docs/architecture/` communication doc with the thread model + ACS-as-transport + ingestor seam + **the impersonation access model** (043/050/052) + the send-contract/thread-stamp (062). Wire ADR-046. Deps 040✅/007✅. |
| **090** | Wrap-up | README→Complete, lessons-learned, **`/test-diet`** (mandatory at project close per root §7), archive, final TASK-INDEX reconcile, portfolio sync. Main-session (touches `.claude/`). Surface the 2 open findings (send-into-existing-thread MED; DI-cycle-refactor LOW) + config gate (Share privilege) in the wrap-up PR. |

### Orchestration notes (what worked this session — reuse next session)
- **Subagent-per-task pattern (proven for 043/052/060/062/063)**: dispatch a `general-purpose` subagent (model `sonnet`) with a precise brief (inject exact contracts + pre-decide scope forks so it doesn't rabbit-hole); instruct it to leave changes uncommitted + NOT touch `TASK-INDEX.md`/`current-task.md`/`.claude/` + return a factual report. Main session then independently verifies (build + tests + hard-gate greps + review the highest-risk diff), runs 9.5 gates, updates the index, commits + pushes. Subagents edit non-`.claude/` files fine.
- **⚠️ Pre-commit hook gotcha (this worktree)**: repo ROOT has no `node_modules`, so the husky pre-commit `npx lint-staged` → `prettier --write` FAILS on staged `.ts/.tsx` files (`'prettier' is not recognized`). Fixed this session by `npm install --no-save --legacy-peer-deps prettier@^3.8.1` at repo root (node_modules is gitignored). If a fresh session/worktree, re-run that before committing TS. Do NOT `--no-verify` (repo policy).
- **⚠️ `git add` cwd**: stage from the REPO ROOT with explicit paths (a stray `cd` into a sub-package leaves cwd there and repo-relative pathspecs fail). Never `git add -A`.
- **BFF gates**: build, publish-size (baseline ~46.99 MB compressed incl PDBs, ceiling 60 — measure via `Compress-Archive` of the publish output), CVE (pre-existing `Microsoft.Kiota.Abstractions` HIGH ONLY — report NEW), 9.5 code-review+adr-check. **ArchTest note**: 3 ADR-007/010 tests fail PRE-EXISTING (stale ceiling 76 vs ~140, Graph email debt) — proven via stash-and-rerun; task changes must not add a NEW failure type, but the 3 stale ones are not this project's to fix.
- **Client tasks**: `npm run build` (tsc) / `build:prod` (PCF), no publish-size/CVE. Hard gates = grep no `@azure/communication` (NFR-04) + no `@spaarke/auth` in shared components (ADR-028).

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
- Task registry: `tasks/TASK-INDEX.md` (**25 ✅, 4 🔲** — 061/080/081/090)
- Access model: `notes/access-model-decision.md` (authoritative — incl. Share-privilege prereq #4 + the OPEN-thread-grant finding now RESOLVED by 052)
- Schema (as-built): `notes/messaging-schema-spec.md`
- Spikes: `notes/spikes/00{1,2,3}-*.md` + `acs-harness/`; Provisioning runbook: `notes/012-acs-provisioning-runbook.md`
- ADR-046: `.claude/adr/ADR-046-*.md` (concise, Accepted) + `docs/adr/ADR-046-*.md` (full)
- Portfolio: GitHub Project #654 (Epic #431), draft PR #655
- **Server code**: `src/server/api/Sprk.Bff.Api/Services/Communication/{Acs,Channels,Threads,Membership,Access,Engine}/` + `Services/Jobs/Handlers/` + `Api/{AcsEventGridEndpoints,CommunicationEndpoints}.cs`; impersonation + POA in `src/server/shared/Spaarke.Dataverse/{DataverseImpersonation,DataverseWebApiService}.cs`.
  - Read model (050): `Services/Communication/{CommunicationThreadReadService,IImpersonatedCommunicationQuery,CommunicationThreadReadModels}.cs`.
  - Direct threads (043) + open-thread grant (052): `Services/Communication/Access/{DirectThreadAccessService,IDirectThreadAccessService,IDataverseAccessGrantService}.cs` + `Membership/DirectThreadExplicitParticipantReader.cs`. Send-contract/thread-stamp (062): `CommunicationService.AssignExplicitThreadAsync` + `SendCommunicationRequest.ThreadId`.
- **Client code**: timeline `src/client/shared/Spaarke.UI.Components/src/components/CommunicationTimeline/` + `services/communicationTimelineApi.ts` + `utils/quoteBody.ts` (063). Send/respond PCF: `src/client/pcf/CommunicationMessageActions/` (062, ZIP built).

---

## Blockers
**Status**: None blocking code — all functional code done. **Live use gated on owner config**: (1) Delegate role, (2) messaging-tables Read=User-level, (3) **Share privilege on both messaging tables** (043/052) — all in `access-model-decision.md`. Inbound live Event Grid still needs a publicly reachable BFF webhook (deferred). Owner to import the 062 PCF ZIP.

---

## Recovery Instructions
1. Read Quick Recovery + Remaining Plan above. `git status` (expect clean, synced) + `git log --oneline -5` (HEAD `872ff6d56`).
2. **⚠️ Before any TS commit**: `npm install --no-save --legacy-peer-deps prettier@^3.8.1` at repo root (pre-commit hook needs it — see Orchestration notes).
3. To resume: "continue" (→ first 🔲 = **task 061**) or "work on task 061". Use the subagent-per-task pattern (Orchestration notes). Load `notes/access-model-decision.md` + `notes/messaging-schema-spec.md` for any server/schema work.
4. Sequence: 061 (package PCF, defer deploy) → 080 (seam tests) → 081 (arch doc) → 090 (wrap + `/test-diet`). No remaining design-decision forks.
