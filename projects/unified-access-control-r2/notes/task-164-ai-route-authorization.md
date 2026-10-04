# Task 164 — AI route authorization sweep (chat / agent / playbooks / prompts / record-match)

> GitHub #1103 · branch `task/uac-r2-164` · base `e6dd48b43` (work/unified-access-control-r2) · built on
> `task/uac-r2-162` @ `8a0e3d103` (merge commit `5b1056cdf`) · executed 2026-10-03 · rigor FULL (opus, xhigh).
> Review 164's own diff as `git diff 5b1056cdf..task/uac-r2-164`.
>
> **Fix round 1** (verifier r1, 2026-10-04): branch `task/uac-r2-164-r1` from `task/uac-r2-164` @ `f7237b527`; merges
> `task/uac-r2-162-r1` @ `d1c5e6f34` (merge `dccbda0`). Owner **round 16** (items 2 and 3) answered both open
> escalations, so the chat family and the playbook `Parameters` rules are now built. The round's record is **§12**;
> §1 below is current, §2–§11 are the round-0 record (superseded where §12 says so).

## 1. Outcome in one table

The task covers 14 sweep findings (19 route keys). Owner round 10 item 1 removed six of the findings by
**deleting** their routes; the other eight findings are **fixed** — four in round 0, and the chat family (five
findings) plus the playbook `Parameters` half of two of them in fix round 1, after owner round 16 answered escalation
triggers 3 and 4.

| Sweep row | Route key | Result |
|---|---|---|
| 55 | `GET /api/ai/playbooks/by-id/{id}` | **FIXED** — playbook-use decision before the cache, uniform 404 |
| 29 | `POST /api/ai/playbooks/{id:guid}/execute` | **FIXED** — playbook (404) + documents (403) + **Parameters** (r1: shared policy, record params as the caller, run user = caller) |
| 19 | `POST /api/agent/run-playbook` | **FIXED** — same as execute (r1 adds the Parameters half) |
| 78 | `GET /api/agent/playbooks/status/{jobId:guid}` | **FIXED** — run owner (`StartedByOid`) comparison, uniform 404 (r1: a lookup fault is the same 404) |
| 55 | `GET /api/ai/playbooks/by-name/{name}` | **DELETED** (no caller, not published) |
| 54 | `PUT /api/ai/playbooks/{id:guid}/nodes/reorder` | **DELETED** |
| 57 | `GET`, `PUT`, `DELETE /api/ai/prompts/{id}`, `POST /api/ai/prompts/{id}/render` | **DELETED** |
| 56 | `GET /api/ai/prompts/`, `POST /api/ai/prompts/` | **DELETED** |
| 31 | `POST /api/ai/document-intelligence/match-records` | **DELETED** |
| 32 | `POST /api/ai/document-intelligence/associate-record` | **DELETED** |
| 24 | `POST /api/ai/chat/sessions` | **FIXED (r1)** — body document, host (by kind) and playbook as the caller; unauthorizable host types dropped |
| 23 | `PATCH /api/ai/chat/sessions/{sessionId}/context` | **FIXED (r1)** — `MapPatch`; every id the body writes into the session, as the caller |
| 25 | `POST /api/ai/chat/sessions/{sessionId}/messages` | **FIXED (r1)** — per-turn document + the stored context, before SSE |
| 53 | `POST /api/ai/chat/sessions/{sessionId}/dispatch` | **FIXED (r1)** — the stored context; a null body refused by the filter |
| 18 | `POST /api/agent/message` | **FIXED (r1)** — `AddAiAuthorizationFilter` attached; body document + a resumed session's stored context |

Also done, owner round 12 item 6 (the oid-vs-systemuserid defect): `PlaybookAuthorizationFilter` OwnerOnly now
compares the caller's **systemuserid** (WhoAmI over OBO) with `_ownerid_value`; its owner/shared/public mode is the
one playbook-use decision (public, or the caller's own Dataverse Read on the row); the owned-playbook lists
(`GET /api/ai/playbooks`, `GET /api/ai/chat/playbooks`, `GET /api/agent/playbooks`) and share/unshare pass the
caller's systemuserid.

## 2. Step 0 — conflict check, anchors, merge order, 167

- **/conflict-check (2026-10-03):** 23 open PRs, **no file overlap**. `chatendpoints-decomposition-r1` is folder-only
  (no worktree, no branch); `work/spaarkeai-assistant-enhancements-r4` is fully merged into master (0 unmerged
  commits). The only overlaps are the sibling sweep branches: 162 (`PlaybookAuthorizationFilter.cs`,
  `PlaybookService.cs`, `OperationAccessPolicy.cs`, `RouteAuthorizationGuardTests.cs`), 163
  (`Spe.Integration.Tests/Api/Ai/ChatEndpointsTests.cs`, `EndpointMappingExtensions.cs`,
  `RouteAuthorizationGuardTests.cs`), 159 (`FinanceAuthorizationFilter.cs`, `OperationAccessPolicy.cs`),
  160/161/167-r2 (`RouteAuthorizationGuardTests.cs`, 167-r2 also `EndpointMappingExtensions.cs`).
- **Escalation trigger 11 (merge contention) — answered by the project record, not fired as a stop.** Task 162's
  POML (main-session authored, the same batch as 164) already fixes the order: *"Whichever of 146, 164, 167 and 162
  lands second rebases onto the first"* and *"build it here ... so 164 can call it and switch
  PlaybookAuthorizationFilter's own routes to it"*. 162 executed first, so 164 is **built on `task/uac-r2-162`**
  (merge `5b1056cdf`). **Integration order: 162 (or its final fix round), then 164.** 162's own trigger ("164 lands
  a playbook-use rule with different semantics → STOP") is respected: 164 reuses 162's
  `PlaybookAuthorizationFilter.BuildPlaybookUseCheckAsync` byte-identical (no second rule) and 162's
  `AnalysisAuthorizationFilter.RunCanWriteDocuments` / `ExecutorSideEffects` for "can this run write the documents".
  `AiAuthorizationFilter.cs` is NOT edited by 164 (the chat family is stopped), so no contention there.
- **Escalation trigger 1 (playbook identity defect)** — answered by **owner round 12 item 6** ("fix the
  oid-vs-systemuserid mismatch ... PlaybookAuthorizationFilter if the same — task 164"). Confirmed by code:
  `PlaybookAuthorizationFilter.cs:125` (pre-164) compared `playbook.OwnerId` (`_ownerid_value`, a systemuserid,
  `PlaybookService.cs:602`) with `CallerResolution.ResolveObjectId` (Entra oid); `PlaybookSharingService` received
  the oid and queried `teammemberships?$filter=systemuserid eq {oid}` (`:284`). Fixed as described in §1.
- **Task 167 has NOT landed** on this branch (no Pending waiver for any 164 key in `RouteAuthorizationGuardTests`),
  so no waivers, ledger entries or GovernedFiles edits were made. §9 is the ledger input for the main session.
- **Task 156** (associate-record cascade) — moot: associate-record is deleted.
- Anchors in the POML background were re-verified; drift was line-number only (e.g. `ChatSessionManager.cs:713`,
  `ComposeService.cs:1192`, `AgentEndpoints.cs:35/62/78`). One correction: there is **no sprk_analysis
  entity-set constant in code from 164** (the chat family that needed it is stopped); the live value is
  `sprk_analysises` (task 162's live `EntityDefinitions` read, its note line 61).

## 3. Step 1 — inventory

**(a) HostContext types and DocumentId sources (TRIGGER 3 FIRED — see §4.1).**
- Console (`src/solutions/SpaarkeAi`): `ConversationPane.tsx:3176-3182` sends `hostContext = { entityType:
  entityContext.entityType, entityId }` unmodified; `entityContext` comes from `ThreePaneShell.tsx:802-813`, built
  from `main.tsx:536-553`: URL `entityType` / `entityLogicalName`, or the HOST FORM's entity
  (`Xrm.Utility.getPageContext().input.entityName`). Launchers: `ribbon/EntityFormLaunch.ts` (documented "sprk_matter,
  contact, sprk_document, **(any entity)**"), `ribbon/DocumentComposeLaunch.ts:149/157/174` (`sprk_document`),
  `ribbon/AnalysisRecordLaunch.ts:125` (the form's entity), `webresources/js/sprk_analysis_commands.js:173`
  (`sprk_document`), and `main.tsx:512-533` (the **sprk_analysis** form embed). So shipped HostContext types include
  `sprk_document`, `contact`, `sprk_analysis` and arbitrary logical names — none of them in the closed set
  (matter/project/workassignment/invoice + `sprk_analysisoutput`).
- Server-minted sessions: `CreateAnalysisWizardWidget.tsx:1024-1033` + `ChatSessionManager.cs:713` +
  `AnalysisEndpoints.cs:1223` (`sprk_analysisoutput` sentinel, EntityId = sprk_analysis id);
  `ComposeService.cs:1189-1192` (matter host); `AnalysisChatContextEndpoints.cs:107` (query-supplied type).
- **Non-GUID DocumentId:** `ComposeService.cs:977-979` binds a Compose session's `DocumentId` to the **SPE drive-item
  id** on Path B (no Dataverse row) — a non-GUID that every later messages/dispatch turn on that session carries.
  Integration fixtures also use `"doc-test-001"` (`ChatEndpointsTests.cs:51`).
- Copilot agent OpenAPI: `CreateSessionRequest.hostContext {entityType, entityId(uuid), workspaceType}`.
- Per-turn `documentId` from the Console comes from `armDocumentTurn` (sprk_document ids).

**(b) Consumers of stored session context (partial — the chat family stopped before a full trace).** Files reading
`session.HostContext / DocumentId / AdditionalDocumentIds / PlaybookId`: `ChatEndpoints.cs` (18 sites),
`ChatSessionManager.cs` (10), `SessionDispatchOrchestrator.cs` (6), `AnalysisEndpoints.cs` (4),
`AgentEndpoints.cs` (4), `ChatDataverseRepository.cs` (4), `ComposeRecordResolution.cs` (4), `ReviewMemoEndpoints.cs`
(3), `AdvisoryCapabilityRunner.cs` (3), `OutputRouter.cs` (2), `ComposeActiveDocumentEndpoints.cs` (2), and one each in
`ChatWordExportEndpoints.cs`, `ChatDocumentEndpoints.cs`, `ComposeReferenceMapping.cs`, `ComposeService.cs`,
`IChatContextProvider.cs`, `SprkChatAgent.cs`, `SprkChatAgentFactory.cs`, `SessionRestoreService.cs`,
`StoredSession.cs`. **Confirmed:** the session `PlaybookId`'s definition DOES reach the prompt app-only —
`PlaybookChatContextProvider.cs:353` loads it via `IPlaybookService.GetPlaybookAsync`, then the primary Action's
system prompt, inline knowledge and skill instructions (`:357-389`). So the chat-session PlaybookId criterion
applies (body PlaybookId must pass the playbook-use decision) when the chat family resumes.

**(c) Playbook template tokens a caller parameter can fill (TRIGGER 4 FIRED — see §4.2).** In-repo
`Services/Ai/Insights/Playbooks/*.json`: `matterId` (13 uses: Live Fact ×4, Return Insight Artifact ×2, Index
Retrieve, Update Record ×2, Query Dataverse ×2, Agent Service ×2), `tenantId` (3), content tokens `matterDescription`,
`matterContext`, `assessments`, `currentGrade`, `observations`, `cohortObservations`, `liveFacts`, `precedents`.
Live `sprk_playbooknode` (93 rows, read-only, 2026-10-03) adds: **`timeWindowHours` (10 uses, all in Query Dataverse
FetchXML)**, **`todayUtc` (2, Query Dataverse FetchXML)**, `userPreferences.timeWindow` (3, Query Dataverse),
`run.userId` (17, Create Notification recipients), `focus` (AI Completion), `start.*` (Start-node outputs, which bind
the caller's Parameters), and many node-output paths (`*.output.*`, `item.*`). Code-level parameters:
`practiceAreaHint`, `documentTypeHint` (`PlaybookOrchestrationService.cs:1852/1867`), scheduler `userId`
(`ExecuteAppOnlyAsync`, `:149-154`).

**(d) sprk_analysis entity set:** `sprk_analysises` (task 162 live read; not used by 164's code this round).

**(e) Playbook visibility and the stable-id mirror (live, 2026-10-03):** Document Profile `18cf3cc8` is **public**;
Summarize File `4a72f99c` (DocumentEmailWizard's `SUMMARIZE_NEW_FILES_PLAYBOOK_ID`) is **private**, owned by the
owner's user; its by-id decision is therefore the caller's Dataverse Read. Spaarke Core User and Spaarke Basic User
hold `prvReadsprk_analysisplaybook` at depth 4 (Deep); task 162 verified the child-BU test user can read 4a72f99c.
**Stable-id mirror holds:** every one of the 34 rows with a non-null `sprk_playbookid` has it equal to the primary key;
the 14 rows with a null `sprk_playbookid` cannot be found by the by-id lookup at all (they 404 either way).
**Trigger 2 did not fire.** All 34 active playbooks are user-owned (owneridtype 8) by one user.

**(f) Prompt Team OwnerIds:** moot — the prompt library is deleted (trigger 6 cannot fire).

**(g) Copilot agent OpenAPI (`src/solutions/CopilotAgent/spaarke-bff-openapi.yaml`):** publishes `POST
/api/ai/chat/sessions` (hostContext), `.../messages`, `GET /api/ai/playbooks/{id}`, `POST /api/ai/playbooks/{id}/execute`
(`PlaybookExecuteRequest {documentId (singular!), parameters: object}`), `/api/agent/message` (`{message,
conversationId, entityContext}`), `/api/agent/playbooks`, `/api/agent/run-playbook` (`{playbookId, documentId,
parameters: object}`), `/api/agent/playbooks/status/{jobId}`. **Pre-existing contract drift (not changed here):** the
published execute body names `documentId` but the BFF binds `documentIds`, so the agent's execute call 400s today;
the published agent-message body names `conversationId`/`entityContext` but the BFF binds
`ConversationReference`/`DocumentId`. It does NOT publish prompts, by-id, by-name, reorder, dispatch, PATCH context,
match-records or associate-record. `/api/agent/message` also has an in-repo caller
(`Sprk.Provisioning.ControlPlane.Core/.../E2EValidationRunner.cs:252`).

**(h) AttachmentClassificationJobHandler → RecordMatchService:** passes only `RecordTypeFilter = "sprk_matter"`,
`MaxResults = 10` (`:304/327/372`). With the HTTP routes deleted no caller input reaches the OData filter, so the
`RecordTypeFilter` closed-set hardening in `RecordMatchService` is not needed and was not added.

## 4. Escalations fired in round 0 (both ANSWERED by owner round 16 — built in fix round 1, §12)

### 4.1 Trigger 3 — HOST-TYPE OR ID VOCABULARY (chat family F0, F1, F2, F3, F12 stopped)

> **Answered — owner round 16 item 2: option (a)**, authorize by kind, no new map; then complete F0-F3 and F12. Built in §12.2.

🔔 **Human Input Required.** The POML's closed host-type set (matter, project, workassignment, invoice + their
`sprk_` forms, plus `sprk_analysisoutput`) would 400 shipped surfaces, and the stored-context rule would deny shipped
sessions:

| Shipped value | Source | What the closed rule would do |
|---|---|---|
| HostContext `sprk_document` | DocumentComposeLaunch.ts, sprk_analysis_commands.js, EntityFormLaunch (document form) | 400 on create/PATCH; deny every turn |
| HostContext `contact` | EntityFormLaunch (contact form) | same |
| HostContext `sprk_analysis` | main.tsx form embed (Analysis form, UAT round 4) | same |
| HostContext = any other logical name | EntityFormLaunch "(any entity)" | same |
| DocumentId = SPE drive-item id (non-GUID) | ComposeService.cs:977-979 (Compose Path B) | every messages/dispatch turn on that session denied |

Options (recommendation first):
- **(a) RECOMMENDED — authorize by KIND, no new map:** matter/project/workassignment/invoice → Read via
  `SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet`; `sprk_document` host → Read on the document
  path (`sprk_documents`, the same seam as DocumentId); `sprk_analysis` host and `sprk_analysisoutput` sentinel →
  Read on ONE `sprk_analysises` constant; **every other type → the session is created WITHOUT a host context** (logged)
  — no memory/pin read can then be keyed on it; a non-GUID stored DocumentId (Compose Path B) → authorized through
  the caller's own SPE read of that drive item (OBO), else denied. `contact`/`account` hosts would lose host context
  until a set is added by owner decision (CLAUDE.md §11 lockstep).
- (b) Strict: deny (400) every type outside the closed set, and change the Console to send no hostContext for those
  types (client change, Console redeploy).
- (c) Widen `SemanticSearchAuthorizationFilter.AuthorizableEntitySets` with document/contact/analysis entries — rejected
  in the POML (it widens `POST /api/ai/search`).

Until answered, F0-F3 and F12 stay open (all high except dispatch, medium). The chat-session PlaybookId check
(§3(b): the definition does reach the prompt) goes with them.

### 4.2 Trigger 4 — PARAMETER VOCABULARY (Parameters on execute and agent run-playbook stopped)

> **Answered — owner round 16 item 3:** fix the ROOT CAUSE first (escape every value at the FetchXML / OData substitution
> point), then ONE shared parameter policy owned by 164; no refuse-all interim. Built in §12.3.

🔔 **Human Input Required.** Live node configs put **non-identity caller parameters into app-only FetchXML
positions** — `timeWindowHours` (10 Query Dataverse nodes in the public scheduled-notification playbooks: Tasks Due
Soon, Tasks Overdue, New Events, New Documents, New Emails, Matter/Project Activity, New Work Assignments) and
`todayUtc` (2) — so a caller can inject FetchXML into an app-only query today by running one of those PUBLIC
playbooks through `/execute` or `/run-playbook` with `parameters.timeWindowHours = "...`. `run.userId` (17 Create
Notification recipients) resolves from run context, and on the HTTP path `UserId` is unset, so the recipient can come
from a caller-shaped value. **This is the most urgent open item.**

Proposed rules (recommendation):
1. **Server-owned (400 in any letter case):** `userId`, `tenantId`, `run` and any `run.*`, `start` and any `start.*`,
   `userPreferences` — these are bound by the server or the scheduler, never by an HTTP caller.
2. **Record-identity map:** `matterId → sprk_matter` (Read; plus Write when an Update Record node's `recordId`
   references `{{matterId}}`, which the two in-repo Insights playbooks do).
3. **Typed non-record parameters that fill FetchXML:** `timeWindowHours` must parse as an integer 1..8760 (else 400);
   `todayUtc` must parse as an ISO-8601 date (else 400) — or, stricter, refuse both on HTTP entry because only the
   scheduler supplies them.
4. **Free-text non-record list:** `matterDescription`, `matterContext`, `assessments`, `currentGrade`, `observations`,
   `cohortObservations`, `liveFacts`, `precedents`, `focus`, `practiceAreaHint`, `documentTypeHint`.
5. Any other parameter whose value parses as a GUID → 400.
- **Interim option (c):** refuse ANY caller `Parameters` on these two HTTP routes until (1)-(5) are approved. No
  in-repo client sends Parameters to either route; the Copilot agent's published schema allows an arbitrary object
  (unknown whether it sends any) — which is exactly why the trigger stops rather than guessing.
- **Coordination with task 163** (the POML: "whichever task lands second reuses the first's parameter rules"): 163
  (not yet landed; in verification) refuses identifier parameters on `POST /api/insights/ask` except the subject's own
  `matterId` (`insights.parameters.not_accepted`) and escalated whether Read suffices when a non-identifier parameter
  (`currentGrade`) feeds the matter write (its §3.4, recommends Write when any non-identifier parameter is sent). The
  rules above are the superset /execute needs (no subject to pin, plus the FetchXML-typed keys); one owner answer
  should cover both tasks.

### 4.3 Triggers not fired / answered

1 answered (owner round 12 item 6) · 2 not fired (mirror holds) · 5 live-gate only (agent token) · 6 moot (prompt
library deleted) · 7 moot (associate-record deleted) · 8 not fired (no new service, filter class, map, ADR exception;
`FinanceAuthorizationFilter.cs` untouched) · 9 live-gate only · 10 recorded in §3(b), resumes with the chat family ·
11 answered by 162's POML (§2).

## 5. Routes deleted (owner round 10 item 1)

Evidence common to all ten: **not published** — `src/solutions/CopilotAgent/spaarke-bff-openapi.yaml` lists none of
them; `spaarke-api-plugin.json` points at that OpenAPI; `declarativeAgent.json` has no route paths; the only other
`*openapi*` / `ai-plugin*` / `declarativeAgent*` / API-manifest files are Office/Teams/SPA app manifests with no BFF
routes, plus samples under `knowledge/`. **No caller** — Grep of `src/**` (including built PCF `bundle.js`),
`scripts/**`, `infrastructure/**`, `docs/**` (outside the BFF and tests) for each path:

| Route | No-caller evidence | Removed with it |
|---|---|---|
| `GET/POST /api/ai/prompts/`, `GET/PUT/DELETE /api/ai/prompts/{id}`, `POST /api/ai/prompts/{id}/render` | `api/ai/prompts` appears only in the BFF and its tests | `Api/Ai/PromptLibraryEndpoints.cs`, `Services/Ai/PromptLibrary/*` (8 files), the `IPromptLibraryService` registration, `PromptLibraryServiceTests.cs` |
| `GET /api/ai/playbooks/by-name/{name}` | only doc comments in `useAiSummary.ts:20` / `DocumentEmailWizard.tsx:160` ("being retired"; both call `/by-id/`) and the stable-id suite's absence assertions | the handler; `PlaybookByNameDeprecationTests.cs` + fixture (`IPlaybookService.GetByNameAsync` stays — `InvoiceAi`, `AppOnlyAnalysisService` use it) |
| `PUT /api/ai/playbooks/{id:guid}/nodes/reorder` | no `/nodes` call of any kind outside the BFF | the handler, `ReorderNodesRequest`, `INodeService/NodeService.ReorderNodesAsync`, the route-exists test |
| `POST /api/ai/document-intelligence/match-records`, `/associate-record` | only `Spe.Integration.Tests/Phase2RecordMatchingTests.cs` | `Api/Ai/RecordMatchEndpoints.cs` (+ its request/response DTOs); `RecordMatchService` STAYS (background job) |

Docs updated: `docs/architecture/AI-ARCHITECTURE.md` (prompts container row), `docs/guides/ai-troubleshooting.md`
(record matching row), `docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md` (verification probe now uses `/by-id/`).
Infrastructure NOT changed: the Cosmos `prompts` container stays provisioned (`infrastructure/bicep/customer.json:1409`,
`stacks/model1-shared.json:3261`) — retiring it is a separate infra change for the main session.

## 6. Design and placement (CLAUDE.md §10 / §11, `.claude/constraints/bff-extensions.md`)

**Placement: in BFF — existing routes, an extension of an existing filter, no new service.** Every change is on
routes that already exist in `Sprk.Bff.Api`; the per-record decision is an endpoint filter in each route's own chain
(ADR-008); evaluation is the existing `Spaarke.Core.Auth.AuthorizationService` (OBO, fails closed) and
`CallerRecordAccessProbe` (WhoAmI); no AI-internal type enters CRUD code. No new service, DI registration, package,
endpoint, option, job or Dataverse column. Net: 10 routes, 1 service and 2 endpoint files removed.

Three-question justification for each piece of NEW surface (grep evidence in brackets):

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `PlaybookAuthorizationMode.UniformById` / `.Run` + `AddPlaybookByIdAuthorizationFilter()` / `AddPlaybookRunAuthorizationFilter()` | `PlaybookAuthorizationFilter` already guards every playbook route; 162 added the playbook-use decision to it [`BuildPlaybookUseCheckAsync`] | Yes — two modes on the existing filter (the POML's "extend PlaybookAuthorizationFilter"); no new filter class | by-id serves any private definition app-only; execute/run-playbook run any playbook on any document app-only |
| `PlaybookRunContext.StartedByOid` + `PlaybookRunStatus.StartedByOid` (`[JsonIgnore]`) | `PlaybookRunContext.UserId` exists but holds a systemuserid for eq-userid [`QueryDataverseNodeExecutor.cs:186-233`] | No — reusing `UserId` would feed an oid into FetchXML (POML constraint) | the agent status route cannot tell whose run it is |
| `PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync` | `CallerRecordAccessProbe.GetCallerSystemUserIdAsync` is the one WhoAmI path [used by task 130/146] | Yes — a 15-line wrapper that resolves the probe and token and fails closed; shared by OwnerOnly and three lists | each list/owner call site would repeat probe + token + fault handling |
| `UniformPlaybookNotFound` / `UniformRecordAccessDenied` + detail constants | `FinanceAuthorizationFilter.UniformRecordNotFound`, `ProblemDetailsHelper.Forbidden` | Yes — both build on them (the 403 IS `ProblemDetailsHelper.Forbidden` with a constant detail); the 404 keeps by-id's existing `type`/`title` so its RFC 7807 contract is unchanged except the id-free detail | per-route bodies drift, reasonCodes differ between deny and fault (an oracle) |
| `AgentEndpoints.PlaybookIdRequiredDetail` / `DocumentIdRequiredDetail`, `PlaybookRunEndpoints.DocumentIdsRequiredMessage` / `RunFailedMessage` | the literals existed in the handlers | Yes — moved to constants used by handler and filter | filter and handler 400 texts drift |

**Why FinanceAuthorizationFilter's evaluator is not the run-mode evaluator.** Its `Forbidden` denial renders the
evaluation's reasonCode (`insufficient_rights`, `no_caller_token`, `system_failure`, ...), so a seam fault and a denial
would differ — this task's constraint requires one body per id kind. The filter calls the same seams directly
(`AuthorizationService.GetCallerRecordAccessAsync` + `OperationAccessPolicy.HasRequiredRights` for the playbook row,
`AuthorizationService.AuthorizeAsync` for documents) and renders one constant body. `FinanceAuthorizationFilter.cs`
is unchanged (task 159 edits it).

**Behaviour changes on sibling routes (outside the 19 keys), deliberate under owner round 12 item 6:**
- OwnerOnly (PUT playbook, share, unshare, canvas PUT, node writes): the owner can now edit (before: nobody could).
  All 34 live active playbooks are owned by one user. Note for the owner: playbooks created through `POST
  /api/ai/playbooks` are created app-only (`PlaybookService.CreatePlaybookAsync` binds no owner), so they are owned by
  the BFF application user and no person can edit them through OwnerOnly — pre-existing, unchanged, a candidate for
  the round-7-item-3 "record the person" pattern.
- OwnerOrSharedOrPublic (GET by id, sharing, canvas GET, clone, validate, run history): public, or the caller's own
  Dataverse Read — ownership, team GrantAccess shares and role depth (Core/Basic User hold Read at Deep). Before: only
  public. The 404-unknown / 403-denied split is kept on these routes (POML constraint).
- `IPlaybookSharingService.UserHasSharedAccessAsync` now has no caller; its doc states it needs a systemuserid.

## 7. Tests (closed set) and suites

| Behaviour | Test (fully qualified) |
|---|---|
| by-id: denied / unknown / fault = one 404, no id, cache never read | `Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests.ById_DeniedUnknownAndFaultingPlaybook_AreOneUniform404_ThatNeverEchoesTheId_AndTheCacheIsNeverRead` |
| by-id: public and readable-private → 200 | `...ById_PublicPlaybook_AndPrivatePlaybookTheCallerCanRead_Return200` |
| by-id: an allowed caller warming the cache does not admit a denied caller | `...ById_AnAllowedCallerWarmingTheCache_DoesNotLetADeniedCallerReadTheCachedEntry` |
| by-id: handler miss = same uniform 404 | `...ById_LookupMissAfterAnAllowedDecision_IsTheSameUniform404` |
| by-id: no token fails closed, no app-only query | `...ById_NoBearerToken_Is404_WithoutAnyAppOnlyRightsQuery` |
| by-id: non-GUID id, unauthenticated | `...ById_NonGuidId_Is404_WithoutALookup`, `...ById_Unauthenticated_Is401` |
| execute: playbook denied / unknown / fault = one 404, nothing runs | `...Execute_DeniedUnknownAndFaultingPlaybook_AreOneUniform404_AndNothingRuns` |
| execute: unreadable document = 403 ProblemDetails before SSE | `...Execute_UnreadableDocument_Is403ProblemDetailsBeforeAnySseHeader_AndNothingRuns` |
| execute: unknown / denied / fault document = one 403 | `...Execute_UnknownDocument_DeniedDocument_AndSeamFault_AreOneUniform403` |
| execute: side-effecting playbook needs Write | `...Execute_SideEffectingPlaybook_RequiresWriteOnEveryDocument` |
| execute: reader gets SSE; route id never authorized as a document | `...Execute_ReaderOfEveryDocument_GetsTheSseStream_AndTheRouteIdIsNeverAuthorizedAsADocument` |
| execute: no token, missing DocumentIds, RunFailed text | `...Execute_NoBearerToken_Is403_WithoutAnyAppOnlyRightsQuery`, `...Execute_MissingDocumentIds_Is400_BeforeAnyRightsQuery`, `...Execute_RunThatThrows_EmitsAFixedRunFailedMessage_NotTheExceptionText` |
| agent run-playbook: playbook 404 / document 403 / reader 202 / empty id 400 | `...AgentRunPlaybook_DeniedUnknownAndFaultingPlaybook_AreOneUniform404_AndNothingRuns`, `...AgentRunPlaybook_UnreadableDocument_Is403_AndNothingRuns`, `...AgentRunPlaybook_ReaderOfThePlaybookAndTheDocument_Gets202`, `...AgentRunPlaybook_EmptyPlaybookId_Is400_BeforeAnyRightsQuery` |
| agent status: other / ownerless / unknown = one 404, no jobId; owner 200 | `...AgentStatus_AnotherCallersRun_AnOwnerlessRun_AndAnUnknownRun_AreOneUniform404_WithNoJobId`, `...AgentStatus_TheCallerWhoStartedTheRun_Gets200` |
| run owner recorded on StartedByOid, UserId untouched; never serialized | `Sprk.Bff.Api.Tests.Services.Ai.PlaybookOrchestrationServiceTests.ExecuteAsync_RecordsTheHttpCallersOid_AsTheRunOwner_AndLeavesUserIdUnset`, `...PlaybookRunStatus_Json_NeverCarriesTheRunOwner` |
| owned list by systemuserid; unresolvable → empty page | `...PlaybookRouteAuthorizationContractTests.OwnedPlaybookList_FiltersByTheCallersSystemUserId_NotTheEntraOid`, `...OwnedPlaybookList_UnresolvableSystemUserId_IsAnEmptyPage_WithNoQuery` |
| sibling filter: OwnerOnly systemuserid; access = playbook-use decision | `Sprk.Bff.Api.Tests.Filters.PlaybookAuthorizationFilterTests.*` (13) |
| retired routes absent (table + wire) and siblings still routed | `Sprk.Bff.Api.Tests.AiPlaybookPromptRecordMatchRouteRetirementTests.*` (15), `Spe.Integration.Tests.Phase2RecordMatchingTests.RetiredRecordMatchRoute_IsNotRouted` |
| set name pinned | `...PlaybookRouteAuthorizationContractTests.PlaybookEntitySet_IsTheOneTheServiceUses` |

Beyond the AC, one-line justifications: the set-name pin guards the one literal the decision interpolates into a URL;
the enum-value pin guards "appended, never renumbered" for a mode enum that other routes select by value.
Removed tests: `PlaybookAuthorizationFilterTests.Constructor_WithAllParameters_CreatesFilter` (ADR-038 ctor-test ban)
and the oid-comparison tests that encoded the defect (replaced); `PlaybookByNameDeprecation*` and the reorder /
match / associate route tests (routes deleted); `PromptLibraryServiceTests` (service deleted).
Updated fixtures: `PlaybookByIdIntegrationTestFixture` (public-playbook `IPlaybookService` + no-rights access seam),
`PlaybookByIdProblemDetailsTests` (detail must NOT contain the id; type/title/instance unchanged),
`PlaybookByIdEndpointTests` (warm-hit id made a named public constant).

**Suite results (2026-10-03, on commit `6474131bf`):** BFF build `-warnaserror` green · affected tests 118/118 ·
full BFF unit suite 14,245 passed / 54 skipped (pre-existing) / **1 failed under contention** —
`Sprk.Bff.Api.Tests.Api.Memory.PinnedMemoryEndpointsContractTests.DeletePin_Authenticated_Returns204AndEmitsCounter`
(other agents' testhosts were running; re-run in isolation: the class passes 15/15; outside this task's area) ·
NetArchTest 346/346 · `Sprk.Bff.Api.IntegrationTests` 101/101 (the three by-name deprecation tests went with the
route) · `Spe.Integration.Tests` 398 passed / 25 skipped (pre-existing) / 0 failed · `dotnet list package
--vulnerable --include-transitive`: no vulnerable packages · publish size not measured (task brief).

## 8. Seeded removals (each removal → a named test turns red → restored byte-identical and touched)

Run by a script (`seed164.py`, scratchpad) that writes the seed, builds `Sprk.Bff.Api.Tests` (-warnaserror), runs
the named tests, then writes the original bytes back and touches the file; the working tree was clean afterwards.
Short names below are in `Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests` unless prefixed.

| # | Decision removed (seed) | File | Named test(s) that turned red |
|---|---|---|---|
| S1 | by-id: `.AddPlaybookByIdAuthorizationFilter()` deleted from the route | PlaybookEndpoints.cs | `ById_DeniedUnknownAndFaultingPlaybook_AreOneUniform404_ThatNeverEchoesTheId_AndTheCacheIsNeverRead` (1/1) |
| S2 | run mode: playbook-use decision short-circuited to allow | PlaybookAuthorizationFilter.cs | `Execute_DeniedUnknownAndFaultingPlaybook_AreOneUniform404_AndNothingRuns`, `AgentRunPlaybook_DeniedUnknownAndFaultingPlaybook_AreOneUniform404_AndNothingRuns` (2/2) |
| S3 | run mode: document rights short-circuited to allow | PlaybookAuthorizationFilter.cs | `Execute_UnreadableDocument_Is403ProblemDetailsBeforeAnySseHeader_AndNothingRuns`, `AgentRunPlaybook_UnreadableDocument_Is403_AndNothingRuns` (2/2) |
| S4 | run mode: always "read" (Write-when-the-run-can-write dropped) | PlaybookAuthorizationFilter.cs | `Execute_SideEffectingPlaybook_RequiresWriteOnEveryDocument` (1/1) |
| S5 | agent run-playbook: `.AddPlaybookRunAuthorizationFilter()` not attached | AgentEndpoints.cs | `AgentRunPlaybook_DeniedUnknownAndFaultingPlaybook_AreOneUniform404_AndNothingRuns`, `AgentRunPlaybook_UnreadableDocument_Is403_AndNothingRuns` (2/2) |
| S6 | agent status: run-owner comparison removed (only `runStatus is null` kept) | AgentEndpoints.cs | `AgentStatus_AnotherCallersRun_AnOwnerlessRun_AndAnUnknownRun_AreOneUniform404_WithNoJobId` (1/1) |
| S7 | `StartedByOid` not recorded in `ExecuteAsync` | PlaybookOrchestrationService.cs | `Sprk.Bff.Api.Tests.Services.Ai.PlaybookOrchestrationServiceTests.ExecuteAsync_RecordsTheHttpCallersOid_AsTheRunOwner_AndLeavesUserIdUnset` (1/1) |
| S8 | `[JsonIgnore]` removed from `PlaybookRunStatus.StartedByOid` | IPlaybookOrchestrationService.cs | `...PlaybookOrchestrationServiceTests.PlaybookRunStatus_Json_NeverCarriesTheRunOwner` (1/1) |
| S9 | by-id handler's lookup-miss 404 echoes the id again | PlaybookEndpoints.cs | `ById_LookupMissAfterAnAllowedDecision_IsTheSameUniform404` (1/1) |
| S10 | OwnerOnly compares the Entra oid with `_ownerid_value` again | PlaybookAuthorizationFilter.cs | `Sprk.Bff.Api.Tests.Filters.PlaybookAuthorizationFilterTests.OwnerOnly_CallerSystemUserIdIsTheOwner_Allows_EvenThoughTheOidDiffers`, `...OwnerOnly_OwnerIdEqualToTheCallersOid_IsNotOwnership` (2/2) |
| S11 | OwnerOrSharedOrPublic reduced to `IsPublic` | PlaybookAuthorizationFilter.cs | `...PlaybookAuthorizationFilterTests.Access_PrivatePlaybookTheCallerCanReadInDataverse_Allows` (1/1) |
| S12 | owned-playbook list filters by the Entra oid again | PlaybookEndpoints.cs | `OwnedPlaybookList_FiltersByTheCallersSystemUserId_NotTheEntraOid` (1/1) |
| S13 | RunFailed SSE event carries `ex.Message` again | PlaybookRunEndpoints.cs | `Execute_RunThatThrows_EmitsAFixedRunFailedMessage_NotTheExceptionText` (1/1) |
| S14 | a retired route re-mapped (`GET /by-name/{name}`) | PlaybookEndpoints.cs | `Sprk.Bff.Api.Tests.AiPlaybookPromptRecordMatchRouteRetirementTests.RetiredRoutes_AreAbsentFromTheEndpointTable`, `...RetiredRoute_WithAValidBearer_Is404NotRouted(GET, /api/ai/playbooks/by-name/Document%20Profile)` (2 of 15) |
| S15 | playbook-use decision fails OPEN on a fault | PlaybookAuthorizationFilter.cs | `ById_DeniedUnknownAndFaultingPlaybook_...`, `Execute_DeniedUnknownAndFaultingPlaybook_...` (2/2) |

First attempt of S2/S3 used `if (false)`, which the build refuses (CS0162 under -warnaserror); re-seeded as
`!(await ... || true)` and both bit.

## 9. Route authorization ledger input (task 167 not landed — for the main session)

| Route key (as the guard spells it) | Mechanism that now decides | Deny test (proves a caller without rights is refused) |
|---|---|---|
| `GET /api/ai/playbooks/by-id/{id}` | filter: `PlaybookAuthorizationFilter` (UniformById) — playbook-use decision via `AuthorizationService.GetCallerRecordAccessAsync` on `sprk_analysisplaybooks` (OBO) | `Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests.ById_DeniedUnknownAndFaultingPlaybook_AreOneUniform404_ThatNeverEchoesTheId_AndTheCacheIsNeverRead` |
| `POST /api/ai/playbooks/{id:guid}/execute` | filter: `PlaybookAuthorizationFilter` (Run) — playbook-use + `AuthorizationService.AuthorizeAsync` Read/Write per document + (r1) `PlaybookParameterPolicy` syntax (400) and record parameters via `AuthorizationService.GetCallerRecordAccessAsync` (Read, Write when a writing node uses it), run user = caller's systemuserid (`CallerRecordAccessProbe`) | `Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests.Execute_UnreadableDocument_Is403ProblemDetailsBeforeAnySseHeader_AndNothingRuns`; parameters: `...Execute_RecordParameter_UnknownDeniedAndFaulting_AreOneUniform403_AndNothingRuns` |
| `POST /api/agent/run-playbook` | filter: `AgentAuthorizationFilter` (identity) + `PlaybookAuthorizationFilter` (Run) — as execute | `Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests.AgentRunPlaybook_UnreadableDocument_Is403_AndNothingRuns`; parameters: `...AgentRunPlaybook_ParameterPolicy_AppliesTheSame_AndTheRunUserIsTheCaller` |
| `GET /api/agent/playbooks/status/{jobId:guid}` | OWNER-COMPARISON (owner round 12 item 4 basis): handler compares `PlaybookRunStatus.StartedByOid` with the caller oid after `AgentAuthorizationFilter` | `Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests.AgentStatus_AnotherCallersRun_AnOwnerlessRun_AndAnUnknownRun_AreOneUniform404_WithNoJobId` |
| `GET /api/ai/playbooks/by-name/{name}` | DELETED (no caller, not published) | `Sprk.Bff.Api.Tests.AiPlaybookPromptRecordMatchRouteRetirementTests.RetiredRoutes_AreAbsentFromTheEndpointTable` |
| `PUT /api/ai/playbooks/{id:guid}/nodes/reorder` | DELETED | same |
| `GET /api/ai/prompts/`, `POST /api/ai/prompts/` | DELETED | same |
| `GET /api/ai/prompts/{id}`, `PUT /api/ai/prompts/{id}`, `DELETE /api/ai/prompts/{id}`, `POST /api/ai/prompts/{id}/render` | DELETED | same |
| `POST /api/ai/document-intelligence/match-records`, `POST /api/ai/document-intelligence/associate-record` | DELETED | same |
| `POST /api/ai/chat/sessions` | filter (r1): `AiAuthorizationFilter` chat-context evaluation — documents via `IAiAuthorizationService`, host records via `AuthorizationService.GetCallerRecordAccessAsync`, playbook via `PlaybookAuthorizationFilter.IsPlaybookUseAllowedForCallerAsync` | `Sprk.Bff.Api.Tests.Api.Ai.ChatContextAuthorizationContractTests.Create_UnreadableHost_UnreadableDocument_UnknownIds_AndASeamFault_AreOneUniform403_AndNothingIsStored` |
| `PATCH /api/ai/chat/sessions/{sessionId}/context` | filter (r1): `SessionOwnershipFilter` + `AiAuthorizationFilter` chat-context evaluation (route now `MapPatch`) | `...ChatContextAuthorizationContractTests.SwitchContext_AnyIdTheCallerCannotRead_IsTheUniform403_AndTheSessionIsUnchanged` |
| `POST /api/ai/chat/sessions/{sessionId}/messages` | filter (r1): `SessionOwnershipFilter` + `AiAuthorizationFilter` (per-turn document + stored context; SPE items via the caller's OBO read, `ISpeFileOperations`) | `...ChatContextAuthorizationContractTests.Messages_APerTurnDocumentTheCallerCannotRead_IsTheUniform403ProblemDetails_NotAStream_AndNoTurnRuns` |
| `POST /api/ai/chat/sessions/{sessionId}/dispatch` | filter (r1): `SessionOwnershipFilter` + `AiAuthorizationFilter` (stored context; null body refused) | `...ChatContextAuthorizationContractTests.Dispatch_AStoredContextTheCallerCannotRead_IsTheUniform403_AndTheOrchestratorNeverRuns` |
| `POST /api/agent/message` | filter (r1): `AgentAuthorizationFilter` (identity) + `AiAuthorizationFilter` (body document + resumed session's stored context) | `...ChatContextAuthorizationContractTests.AgentMessage_ABodyDocumentTheCallerCannotRead_IsTheUniform403_BeforeAnySessionIsCreated` |
| `GET /api/ai/playbooks/`, `GET /api/ai/chat/playbooks`, `GET /api/agent/playbooks` (CallerScopedOnly, owner round 12 item 6) | handler decision: the owned list is filtered by the caller's systemuserid (WhoAmI over OBO); public list unchanged | one test PER list (r1 — round 0 credited one test for all three): `...PlaybookRouteAuthorizationContractTests.OwnedPlaybookList_FiltersByTheCallersSystemUserId_NotTheEntraOid` (`/api/ai/playbooks`), `...PlaybookRouteAuthorizationContractTests.AgentOwnedPlaybookList_FiltersByTheCallersSystemUserId_NotTheEntraOid` (`/api/agent/playbooks`), `...ChatContextAuthorizationContractTests.ChatOwnedPlaybookList_FiltersByTheCallersSystemUserId_NotTheEntraOid` (`/api/ai/chat/playbooks`) |
| `POST /api/ai/playbooks/{id:guid}/share`, `/unshare` (outside the 19 keys; owner round 12 item 6) | filter `PlaybookAuthorizationFilter` OwnerOnly (systemuserid); handler passes the systemuserid to the sharing service | `...PlaybookRouteAuthorizationContractTests.Share_And_Unshare_PassTheCallersSystemUserId_NotTheEntraOid` |

Census: `ExpectedEndpointFileCount` 120 → 118 (two endpoint files deleted); reconcile with siblings at integration.
`AgentAuthorizationFilter`'s ClaimOnlyFilters reason was corrected (identity precondition; per-record decisions are
other filters' / the handler's). `PlaybookAuthorizationFilter` passes Rule B on its own (`AuthorizationService`).

## 10. Manual live gate (dev, main session; ids redacted to 8 characters; no live writes needed)

Use `uac.child.user@demo.spaarke.com` (systemuser `d6f8f439`, Spaarke Business Unit 1) as U1 and the owner's user as
U2, after the integrated build is deployed.
- (a) **by-id:** U1 `GET /api/ai/playbooks/by-id/18cf3cc8…` (Document Profile, public) → 200; U1
  `GET /api/ai/playbooks/by-id/4a72f99c…` (Summarize File, private) → 200 if U1 has Read (task 162 says yes), else
  the uniform 404; a random GUID → the identical 404 body (compare all but correlationId/instance).
- (b) **Shipped consumers:** the document AI summary (useAiSummary, Document Upload) and the document email wizard's
  summarize step still load for U1 (POML gate (g)).
- (c) **execute:** U1 `POST /api/ai/playbooks/{Quick Document Review e62f30c6…}/execute` with a document U1 cannot read →
  403 ProblemDetails (not an SSE stream); with a document U1 can read → SSE. With Document Profile (side-effecting:
  Update Record) on a document U1 can Read but not Write → 403.
- (d) **agent status:** if the Copilot agent is deployed in dev, start a run as U1, poll its status as U1 → 200, as U2 →
  404 with no jobId in the body. Otherwise record "not available" (POML gate (j)).
- (e) **owner round 12 item 6:** U2 (owner of all 34 playbooks) `PUT /api/ai/playbooks/{an owned id}` → reaches the
  handler (before: 403 for everyone); U1 → 403. `GET /api/ai/playbooks` as U2 lists U2's playbooks (before: empty).
- (f) **retired routes:** `GET /api/ai/prompts`, `GET /api/ai/playbooks/by-name/Document%20Profile`, `POST
  /api/ai/document-intelligence/match-records` → 404 with a valid bearer.
- Chat-family gates (a)-(f), (h)-(j) of the POML wait for the trigger-3 answer.

## 11a. Quality gates (task-execute Step 9.5)

**code-review** (coverage-first; severity · confidence):
- **Critical:** none in the code shipped. The two OPEN escalations are Critical-risk *findings* left in place by
  design until the owner answers: the playbook `Parameters` FetchXML-injection path (§4.2) and the chat family (§4.1).
- **W1 · high:** sibling read routes (GET by id, sharing, canvas GET, clone, validate, run history) widen from
  "public only" (the defect) to the caller's Dataverse Read — Core/Basic User hold Read at Deep, so a private playbook
  becomes readable by colleagues Dataverse lets read it. Intended (owner rounds 9 and 12, task 162's rule), but it is
  a behaviour change on routes outside the 19 keys; listed in §6 for the owner.
- **W2 · medium:** latency — run mode does `GetPlaybookAsync` (5 Dataverse calls), `GetNodesAsync` and one rights
  call per document before `ExecuteAsync` reads the nodes again; the three owned-playbook lists add an OBO + WhoAmI
  per call (`GET /api/ai/chat/playbooks` runs on Console load). Candidate for per-request reuse; not changed.
- **W3 · low:** `Api/Filters/PlaybookAuthorizationFilter` now references `Api.Agent` / `Api.Ai` DTOs and constants
  (same shape as task 162's `AnalysisAuthorizationFilter` → `AnalysisEndpoints`).
- **S1:** `ResolveCallerSystemUserIdAsync` guards a non-nullable `HttpContext` with `ThrowIfNull` (parity with 162's
  helper). **S2:** the by-id handler's blank-id 400 is now unreachable (the filter 404s first). **S3:**
  `IPlaybookSharingService.UserHasSharedAccessAsync` has no caller — candidate for removal. **S4:**
  `PlaybookAuthorizationFilter.cs` grew ~200 → 582 lines with four modes; it is one cohesive responsibility (playbook
  authorization) — acceptable under CLAUDE.md §11.5, no decomposition proposed.
- Metrics: PlaybookAuthorizationFilter.cs 582 lines, 4 ctor params; AgentEndpoints.cs 613; PlaybookEndpoints.cs 806
  (−~70: by-name removed); PlaybookRunEndpoints.cs 501. Direction: net −1,193 lines in the commit; complexity added
  only in the filter's two new modes.
- AI smells: no interface added; no catch-log-rethrow (catches deny or return null); no restating comments of note.

**adr-check:** compliant — ADR-001 (Minimal API, routes in `Map*Endpoints`), ADR-002 (no plugins), ADR-003 (every
fault, missing token, missing service and unresolved caller denies; seeds S15 prove it), ADR-007 (no Graph types),
ADR-008 (decisions are endpoint filters in each route's chain; the status owner comparison is the documented
filter/endpoint pair exception (d)), ADR-010 (no new interface or registration; one registration removed), ADR-013
(no CRUD→AI injection; `INodeService` in an AI-surface filter, as task 162 — ADR013_AiBoundaryTests green),
ADR-015 (logs carry ids only), ADR-019 (ProblemDetails with reasonCode; RunFailed text fixed), ADR-038 (no
`Mock<HttpMessageHandler>`, no DI-registration or ctor tests; one ctor test removed), ADR-052 (no background work).
No violation, so no §6.5 path was needed.

## 11. For the main session

- Integrate after 162 (164 contains 162's two commits via `5b1056cdf`).
- Owner questions: §4.1 (trigger 3) and §4.2 (trigger 4, URGENT — FetchXML injection through public playbooks).
- `.claude/**`: no edit needed.
- Infra follow-up (optional): retire the Cosmos `prompts` container from bicep.
- Copilot OpenAPI drift (§3(g)) is pre-existing; worth an issue for the CopilotAgent owner.
- Pre-existing, outside the 19 keys: `/api/admin/record-matching/*` is `RequireAuthorization()` only (no admin
  policy) — not a sweep finding; flag for task 167's classification.

## 12. Fix round 1 (adversarial verifier round 1, 2026-10-04)

Branch `task/uac-r2-164-r1` (from `task/uac-r2-164` @ `f7237b527`). Binding inputs: owner rounds 1-12 (12 relayed by the
harness), **owner round 16 items 2, 3 and 7** (main-session decisions under the owner's standing directive "fix it in
the correct way; never defer or sideline", recorded on `work/unified-access-control-r2` @ `0c007772c`), and the
main session's note in the worktree ("Task 164's escalations are DECIDED ... implement, completely, owner round 15
(task 162) and round 16"). Live
Dataverse was read only (two read queries, §12.5); nothing was written.

### 12.1 The verifier's 28 items, one by one

| # | Item | Closed by |
|---|---|---|
| 1 | Scope: the chat family and the Parameters half were stopped | Round 16 answered both triggers; both are built (§12.2, §12.3). Nothing in the 19 keys is open in code. |
| 2 | Suite results | Re-run at the end of this round (§12.8). |
| 3 | Seeds V1, V2, V6 bite | Confirmation only; no change. |
| 4 | V3/V4/V5 survive: only `/api/ai/playbooks` pinned | Three new real-host tests, one per surface: `AgentOwnedPlaybookList_...`, `ChatOwnedPlaybookList_...`, `Share_And_Unshare_PassTheCallersSystemUserId_NotTheEntraOid` (share AND revoke). §9 now credits one test per list. Seeds V3, V4, V5, V5b turn them red (§12.7). |
| 5 | Agent status: a `GetRunStatusAsync` fault answered 500 | The catch now answers the SAME `UniformRecordNotFound` as an unknown / ownerless / foreign run (`OperationCanceledException` on an aborted request is rethrown). Test `AgentStatus_ALookupFault_IsTheSameUniform404_AsAnUnknownRun`; seed A1. |
| 6 | Comment drift in `BuildPlaybookUseCheckAsync` | Remark rewritten: the oid-vs-systemuserid comparison is described as the PRE-164 defect that 164 fixed (OwnerOnly resolves the systemuserid). |
| 7 | Publish size not measured | **NOT CLOSED, by instruction:** this round's harness says "Skip publish-size measurement". Procedure for the main session in §12.10. |
| 8 | Deletions verified | Confirmation only. |
| 9 | Implemented routes: no fail-open | Confirmation only. |
| 10 | Sibling behaviour change (OwnerOrSharedOrPublic → caller's Read) | Informational; unchanged and still listed in §6. |
| 11 | Consumer check (Summarize File by-id needs Read) | Informational; stays a live-gate item (§10 (b)); the chat family adds its own consumer gates (§12.9). |
| 12 | Integrate 162 first; merge 162's fix rounds into 164 | `task/uac-r2-162-r1` @ `d1c5e6f34` merged (commit `dccbda0`, no conflict — 162-r1 touches only the analysis filter / endpoints). Any later 162 round (round 15's anchorless-analysis work) must also be merged before 164 integrates; order stays 162 → 164 → 163's `/ask` consumer (round 16 item 7). |
| 13 | A full dev playbook GUID in `DATAVERSE-AUTHENTICATION-GUIDE.md` | Replaced by `{playbookId}` with a pointer to `DOCUMENT_PROFILE_PLAYBOOK_ID` in `useAiSummary.ts` (the guide is cross-environment; the id is environment data). |
| 14 | F0 create | Built (§12.2). Tests `Create_*` (9). |
| 15 | F0 validation, closed set, pinned `sprk_analysis` set | Non-GUID document id / host id → 400 before any rights query; host types decided BY KIND (round 16 item 2 replaced "outside the set → 400" by "→ dropped"). The analysis host needs no entity set at all: it is decided by task 162's analysis-read rule (Read on every anchor, §12.2), which owner round 15 item 4 requires (never a business-unit-depth row Read on an analysis), so no `sprk_analysises` URL is ever built and 162's `AnalysisEntitySetLabel` stays the one constant (a deny-log label). Tests `Create_AnAnalysisHost_IsDecidedByTheAnalysisReadRule_...` (asserts no call on `sprk_analysises`), `Create_AnAnalysisHostWithNoAnchor_...`, `Messages_AStoredAnalysisHost_...`. |
| 16 | F1 PATCH `/context` | `MapPatch`; still `AddSessionOwnershipFilter`; body ids authorized; session unchanged on deny. Tests `SwitchContext_*` (4). |
| 17 | Chat session PlaybookId | The playbook-use decision on create / PATCH, and on every turn that loads the stored playbook (messages, agent message). |
| 18 | F2 messages | Per-turn document + stored document / additional documents / host / playbook, before SSE. Tests `Messages_*` (7). |
| 19 | F2 ActiveItem | Traced (§12.5): the active item reaches ONLY the workspace-state prompt block as id / type / label (`SprkChatAgentFactory.BuildWorkspaceStateBlock`, `:1663-1673`); no code reads the record it names. No code change (the criterion's "otherwise" branch). |
| 20 | F3 dispatch | Stored context; a null body is refused by the filter; dispatch Args traced (§12.5): no Args value is read as a record id app-only. Tests `Dispatch_*` (4). |
| 21 | Revocation | `Revocation_ASessionCreatedWhileTheCallerHeldRead_IsDeniedOnItsNextMessagesDispatchAndAgentTurn`. |
| 22 | F12 agent message | `AddAiAuthorizationFilter` attached; body document and a resumed session's stored context authorized before any session is created or resumed. Tests `AgentMessage_*` (4). |
| 23 | F11 parameter half | Built (§12.3). |
| 24 | Parameters F9 / F11 + FetchXML injection | Root cause escaped at the substitution point; shared policy at both HTTP entries; run user = caller (§12.3). |
| 25 | Fail closed, every family | Chat: missing token (`Create_NoBearerToken_...`, `Messages_NoBearerToken_...`), seam fault (`Create_..._AndASeamFault_...`), undecidable stored ids; agent status fault (item 5); parameters: record-path fault (`Execute_RecordParameter_ASeamFault_...`), unresolvable caller (`Execute_CallerWhoseSystemUserIdCannotBeResolved_...`). |
| 26 | Real-host denial for the 5 chat keys | `ChatContextAuthorizationContractTests` hosts the REAL `MapChatEndpoints`, `MapDispatchSessionEndpoint` and `MapAgentEndpoints`; one or more denial tests per key (§9). |
| 27 | Seeds for the new decisions; V3-V5 bite | 34 seeds, each red then restored (§12.7). |
| 28 | Suites: publish size | See item 7. Every test suite re-run (§12.8). |

### 12.2 The chat family (owner round 16 item 2, option (a))

**Where:** the ONE shared evaluation in `AiAuthorizationFilter` (POML: "extend AiAuthorizationFilter"), in each route's
own chain (ADR-008). `AddAiAuthorizationFilter` became a filter FACTORY so the evaluation is keyed on the handler's
DECLARED body type — `ChatCreateSessionRequest`, `ChatSwitchContextRequest`, `ChatSendMessageRequest`,
`DispatchSessionRequest`, `AgentMessageRequest` — never on a `sessionId`; every other route that attaches the filter
gets exactly the old path (`History_ARouteWithNoChatRequestBody_KeepsTheExistingPath_...`, and the unmodified
`AiAuthorizationFilterTests`).

**By kind, no new map:**

| What | Decision (as the caller) |
|---|---|
| Host of a type `SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet` resolves (matter, project, work assignment, invoice, short or `sprk_` form) | Read via `AuthorizationService.GetCallerRecordAccessAsync` on that set (cached path) |
| Host `sprk_document` | document Read via `IAiAuthorizationService` |
| Host `sprk_analysis` or the `sprk_analysisoutput` sentinel | task 162's analysis-read rule, ONE declaration shared with `GET /api/ai/analysis/{analysisId}`: `AnalysisAuthorizationFilter.ResolveAnalysisReadTargetsAsync` reads the analysis's anchor ids app-only and requires Read on EVERY populated anchor (documents and records), evaluated exactly as the per-route evaluator does. No anchor, an unknown id, an anchor type with no entity set, no token or a fault → deny. Never a row Read on the analysis (owner round 15 item 4); see §12.10 for standalone analyses |
| Host of ANY other type (contact, account, sprk_event, …) or a blank type | DROPPED: create stores no host; PATCH clears it; a stored one is cleared and the clear PERSISTED (`ChatSessionManager.UpdateSessionCacheAsync`) before the turn — the dispatch orchestrator re-reads the session itself |
| Document id that is a GUID (body, stored, additional) | document Read via `IAiAuthorizationService` |
| Body document id that is not a GUID (non-empty) | 400 before any rights query |
| STORED document id that is an SPE drive-item id (Compose Path B) | the caller's own SPE read, OBO: `ISpeFileOperations.GetFileMetadataAsUserAsync(drive, item)` must return the item; the drive comes from the new `ChatSession.DocumentDriveId`, which `ComposeService.LoadAsync` records on every Path B load. No drive recorded → undecidable → deny (fail closed; the next Compose load records it) |
| Stored additional id that is not a GUID; a stored host of an authorizable type with a non-GUID id | undecidable → deny |
| Playbook (create / PATCH body; stored on messages and agent turns) | the playbook-use decision, `PlaybookAuthorizationFilter.IsPlaybookUseAllowedForCallerAsync` (public, or the caller's Dataverse Read on the row — task 162's rule, one rule) |

**Per route:** create → body document, host, playbook. PATCH → body document, every additional document (cap 5 → the
handler's 400, before any query), host, playbook. Messages → the per-turn document, else the stored one; stored
additional documents, host, playbook. Dispatch → stored document, additional documents, host (the playbook is the
Binding's, not the session's); a null body is answered by the filter with the handler's own 400
(`dispatch.binding-required`). Agent message → the body document; and when the conversation reference names a session
the CALLER owns (the only case the handler resumes), its stored context as for messages.

**Answers:** validation 400s first; every denial (unknown id, denied id, seam fault, no token, undecidable stored id) is
ONE 403 — `ProblemDetailsHelper.Forbidden(sdap.access.deny.insufficient_rights, "You do not have access to one or more of
the records this conversation uses.")` — that names no id. The stored session is read once: `SessionOwnershipFilter`
now leaves the session it read in `HttpContext.Items` (its own decision is unchanged).

**Not changed:** `ChatSessionManager`, `SprkChatAgentFactory`, `ContextBinder`, the orchestrators (ADR-008: no decision
moved into them); `FinanceAuthorizationFilter.cs`; `IMemoryAccessAuthorizer` is not used.

### 12.3 Playbook parameters (owner round 16 item 3)

**Root cause first — at the substitution point.** Layer 1 (`PlaybookOrchestrationService.ApplyConfigJsonTemplates`,
and the fan-out iteration render) now renders every node-config string that the executor runs as a QUERY against a
context whose values are escaped for that language: `QueryTextPositions` = QueryDataverse `fetchXml` (XML-escaped:
`& < > " '`) and IndexRetrieve `filter` (OData literal: `'` doubled). The list is CLOSED and pinned, and
`EveryExecutorConfigField_DescribedAsQueryText_IsAQueryTextPosition` fails the build if an executor's config schema
declares another query-text field. The VALUES are escaped (`PlaybookTemplateContextBuilder.EscapeForQueryText`), not
the output, so the `default` / `joinIds` helpers and triple-stash cannot bypass it; node outputs are escaped too (an
LLM output or a Start-node payload is caller-influenced). GUIDs, integers and ISO dates are unchanged by either escape,
so legitimate runs render the same text (`LegitimateValues_RenderTheSameText_AsBefore`). The escaped copy keeps the
context's shape, self-references included — the context builder exposes a node's output dictionary under its own
`output` key, which a naive copy recursed on forever; found by `ExecuteAsync_AFanOutQueryDataverseNode_...` before
commit and covered by `EscapeForQueryText_KeepsSelfReferences_...` and
`ExecuteAsync_AQueryDataverseNodeAfterAStructuredNode_RendersItsIds_TheNotificationPlaybookShape` (the live notification
playbooks' `{{joinIds myMatters.ids}}`). At the same point the typed keys are type-checked
(`PlaybookParameterPolicy.EnsureTypedParametersValid`): a wrong type fails the node with a message that names the key,
never the value. A query-text config that is not JSON now fails its node instead of falling back to the flat render.

**Then ONE shared policy — `Services/Ai/PlaybookParameterPolicy`** (consumed by `/execute` and `/run-playbook` through
`PlaybookAuthorizationFilter` run mode; task 163's `/ask` consumes the same `Evaluate` / `ReferencesParameter` API):

| Rule | Keys (closed, pinned by `PlaybookParameterPolicyTests`) | Answer |
|---|---|---|
| 1. server-owned (any case, or a `run.` / `start.` / `userPreferences.` path) | `userId`, `tenantId`, `userName`, `run`, `start`, `userPreferences` | 400 |
| 2. record identity → entity LOGICAL name (set via the existing resolver) | `matterId` → `sprk_matter`, `projectId` → `sprk_project`, `invoiceId` → `sprk_invoice` | non-GUID 400; then Read as the caller (403), plus **Write** when a node that can write (side-effecting per `ExecutorSideEffects`, or unclassifiable) references it (`RecordParameterOperation`) |
| 3. typed tuning keys | `timeWindowHours` int 1..8760, `dueWithinDays` int 0..3660, `todayUtc` / `dueSoonWindowUtc` ISO date or date-time | wrong type 400 |
| 4. declared text keys (the reasoned non-record list) | `matterDescription`, `matterContext`, `assessments`, `currentGrade`, `observations`, `cohortObservations`, `liveFacts`, `precedents`, `focus`, `practiceAreaHint`, `documentTypeHint` | accepted; a GUID value 400 (a record id only on a rule-2 key) |
| 5. any other key | — | 400 (the policy is a declared ALLOW-list — round 16: "a declared allow-list of tuning keys with types") |

Inventory the lists came from: §3(c) (in-repo Insights playbooks, the 93 live nodes, `PlaybookSchedulerJob`, the code
parameters) plus `InsightsOrchestrator`'s `projectId` / `invoiceId` subject enrichment. `userName` is server-owned
because only the scheduler sets it (the recipient's display name in notifications). `dueSoonWindowUtc` /
`dueWithinDays` are the scheduler's two other tuning keys.

**Order in the run filter:** the parameter syntax 400 runs before any lookup or rights query; then the playbook (404),
then documents and record parameters (one 403), then the run's user.

**Why a closed allow-list, not "refuse only undeclared GUIDs".** An undeclared key can carry a record id INSIDE a
larger value (`matter:{id}`, `{id},{id}`) or shadow a node output (Parameters win over node outputs on a key collision)
that a later node uses in an id position; neither is visible to a GUID test. Round 16 says "a declared allow-list",
so every key outside the four lists is a 400 naming the key. Consequence for the Copilot agent (whose published schema
allows an arbitrary `parameters` object): an undeclared key is refused with a message that names it; no in-repo
client sends `Parameters` to either route (§4.2).

**`run.userId` set server-side.** The run filter resolves the caller's systemuserid (WhoAmI over OBO,
`ResolveCallerSystemUserIdAsync`; unresolvable → the uniform 403) and publishes it; both handlers put it on the new
`PlaybookRunRequest.RunUserId`, and `ExecuteAsync` sets `PlaybookRunContext.UserId` from it — so `eq-userid`,
`{{run.userId}}` and notification recipients act for the CALLER on the HTTP path, never for a caller-chosen value. Every
other caller of `ExecuteAsync` leaves it null (unchanged); `ExecuteAppOnlyAsync` (the scheduler) is unchanged.

### 12.4 Other changes this round

- `GET /api/agent/playbooks/status/{jobId:guid}`: a lookup fault is the uniform 404 (item 5).
- `PlaybookAuthorizationFilter`: comment drift fixed (item 6); `IsPlaybookUseAllowedForCallerAsync` exposes the SAME
  playbook-use evaluation to the chat filter (the instance path calls the same private static).
- `RouteAuthorizationGuardTests`: the `AgentAuthorizationFilter` ClaimOnlyFilters reason no longer calls `/message` open —
  it names `AiAuthorizationFilter` as the per-record decision there.
- `Spe.Integration.Tests/Api/Ai/ChatEndpointsTests.cs` and `ReAnalysisFlowTests.cs` fixtures: GUID document ids (the shape clients send; `doc-test-001`
  is now a 400) and a permissive record path on its `IAccessDataSource` mock, matching its existing permissive document
  mock (authorization itself is proven in the contract tests).
- `AnalysisAuthorizationFilter`: the analysis-read rule's target resolution is extracted, unchanged, into
  `ResolveAnalysisReadTargetsAsync` (internal static), which `GET /{analysisId}` and the chat analysis host both call —
  one declaration, so the two can never disagree. Its `AnalysisEntitySetLabel` is untouched (it reaches no URL).
- `DispatchSessionEndpoint`: the binding-required errorCode and detail are `internal` constants shared with the filter.
- `PlaybookParameterPolicy` has no regular expression: the ISO date check is an exact parse over seven formats and the
  node-reference detector is a plain scan. The second full unit run exposed the defect (§12.8): under full-suite load
  the compiled date regex's first match exceeded its 100 ms budget and `RegexMatchTimeoutException` escaped the run
  filter as a 500 instead of the 400. Code that cannot time out answers the same under any load; the cases are pinned
  (`TypedKey_IsAcceptedOnlyWhenItParsesAsItsType`, `ReferencesParameter_...`, now 21 and 16 cases; seeds P9, P9b).

### 12.5 Inventory added this round

- **ActiveItem (F2):** `ChatEndpoints.SendMessageAsync` maps it to `WorkspaceActiveItemHandle` and passes it to
  `SprkChatAgentFactory.CreateAgentAsync`, whose ONLY use is `BuildWorkspaceStateBlock` writing `id`, `type`, `label`
  into the prompt (`SprkChatAgentFactory.cs:1663-1673`). Nothing loads the record. (A tool the model later calls with
  an id is a tool-level read, outside these routes.)
- **Dispatch Args (F3):** `SessionDispatchOrchestrator` reads `fileIds` (the session's OWN uploaded files), the confirm
  gate id, `subDomain` (an agreement-type registry key), `reviewDepth`, structured operand TEXT
  (`selectionText` / `changesText` / `documentText`) and `ledger_resolution` (a key into the SAME session's ledger);
  coded workflows get the args verbatim with `UserId = null` (the Daily Briefing collector refuses without a user; the
  upload notification reads `fileName` text). No Args value is read as a record id app-only.
- **Stored-context consumers (§3(b) completed for the turn routes):** the turn routes are messages
  (`SprkChatAgentFactory` → `PlaybookChatContextProvider`: document summary, playbook definition, record memory, RAG
  scope), dispatch (`ContextBinder`: record memory, matter pins) and agent message (the same provider). All three now
  re-authorize the stored context per turn. Other session-scoped routes that read stored context (refine, restore,
  suggest, review-memo, summarize, Compose routes) are owned by their sweep tasks; none was in this task's 19 keys.
- **Live (read-only):** `prvReadsprk_analysis` is depth 4 (Deep) on both Spaarke Core User and Spaarke Basic User
  (`roleprivileges`, 2026-10-04) — see §12.10's first bullet. The five live Query Dataverse nodes with FetchXML were
  re-read (`sprk_playbooknode`, 2026-10-04): their FetchXML tokens are `{{timeWindowHours}}`, `{{todayUtc}}`,
  `{{matterId}}` and `{{joinIds myMatters.ids}}` — every one renders identically under the escape for legitimate
  values.

### 12.6 New surface — three-question justification (CLAUDE.md §11; grep evidence in brackets)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `Services/Ai/PlaybookParameterPolicy` (static class: closed key lists, `Evaluate`, `IsValidTypedValue`, `EnsureTypedParametersValid`, `ReferencesParameter`) | none [grep `Parameters\["userId"\]|ServerOwned|RecordIdentity` under `src/server`: only the scheduler's own `userId` read]; 163's `/ask` has its own identifier refusal on its branch | No existing type holds a parameter policy. Round 16 item 3 mandates ONE policy shared by `/execute`, `/run-playbook` and 163's `/ask`, used by BOTH the API layer (the run filter) and the Services layer (the substitution-point type check) — so it lives in Services, where the filter can reach it and the orchestrator does not reach into `Api`. A static class: no DI, no interface. | a caller names the run's `userId`, points `matterId` at any matter (including an app-only UpdateRecord write onto it) and sends any GUID through an undeclared key |
| `PlaybookRunRequest.RunUserId` | `PlaybookRunContext.UserId` (the run's systemuserid) has no way in on the HTTP path [only `ExecuteAppOnlyAsync` sets it, from `Parameters["userId"]`] | Not by reusing `Parameters["userId"]` — that is exactly the caller-controlled key the policy now refuses. One nullable init property, null everywhere else | `run.userId` / `eq-userid` on the HTTP path fall back to a node output the caller can shape |
| `ChatSession.DocumentDriveId` + `StoredSession.DocumentDriveId` (+ the two mapper lines, + `ComposeService` records it) | `ActiveDocumentIdentity.SpeDriveId` exists but is the CHAT session's active-document pointer, read by `SendWorkspaceArtifactHandler`; overloading it would change that handler's choices | A Path B session stores only the drive-ITEM id; Graph cannot read an SPE item without its drive. One nullable init property next to `DocumentId`, persisted the same way | a Compose Path B session's turns cannot be decided by the caller's SPE read; they would be denied for everyone (fail closed) — the Compose AI toolbar broken on unsaved documents |
| `PlaybookOrchestrationService.QueryTextPositions` + `HasQueryTextPositions`; `PlaybookTemplateContextBuilder.QueryTextLanguage` + `EscapeForQueryText` + `EscapeQueryTextValue` | `IndexRetrieveNode.EscapeODataValue` and `RagService.EscapeFilterValue` escape the values THEY build; nothing escapes a Layer-1 template value [grep `SecurityElement.Escape|EscapeFilterValue|EscapeODataValue`] | Extended the existing Layer-1 renderer and context builder (no new type beyond the nested enum); the table is next to the walker that applies it | any parameter or node output injects into an app-only FetchXML query or an AI Search filter |
| `PlaybookAuthorizationFilter.IsPlaybookUseAllowedForCallerAsync`, `GetRunUserId`, `ParameterRejectedErrorCode`, `RecordParameterOperation` | the filter's private `IsPlaybookUseAllowedAsync` and run mode | Same filter, same evaluation (the instance method now calls the shared private static) | the chat filter would copy the playbook-use rule (a second policy) |
| `AiAuthorizationFilter` constants (host types, deny / 400 details), `IsHostContextDropped`, `ChatContextDenied`, private `HostKind` / `ChatContextChecks` / `IsAnalysisReadableAsync` | the filter already guards every chat route but decided nothing for chat DTOs | The POML's "extend AiAuthorizationFilter"; no new filter class, no new map, no new constant for the analysis entity | the five routes stay open |
| `AnalysisAuthorizationFilter.ResolveAnalysisReadTargetsAsync` | `AuthorizeAnalysisAccessAsync`'s inline anchor read (task 162) | extracted from it, unchanged; the GET route calls it | the chat analysis host would need a second copy of 162's analysis-read rule, or a row Read that round 15 forbids |
| `SessionOwnershipFilterExtensions.OwnedSessionItemKey` | the ownership filter already reads the session | one `Items` entry, so the turn does not read the session twice (POML constraint permits sharing it) | a second session read per turn |
| removed / not added | — | No new service, endpoint, DI registration, option, job, Dataverse column, package or filter class; `FinanceAuthorizationFilter.cs` untouched; no new logical-name → entity-set map | — |

### 12.7 Seeded removals this round (each red, then restored byte-identical and touched)

Script `seed164r1.py` (scratchpad): writes the seed, builds `Sprk.Bff.Api.Tests`, runs the named tests, restores the
original bytes, touches the file. The working tree was clean of seed edits afterwards (`git status`).

Short names are in `Sprk.Bff.Api.Tests.Api.Ai.ChatContextAuthorizationContractTests` (C*, V4),
`...PlaybookRouteAuthorizationContractTests` (P1-P5, V3, V5, A1), `Sprk.Bff.Api.Tests.Domain.Ai.PlaybookQueryTextEscapingTests`
and `Sprk.Bff.Api.Tests.Services.Ai.PlaybookOrchestrationServiceTests` (P6-P8).

| # | Decision removed (seed) | File | Named test(s) that turned red |
|---|---|---|---|
| C1 | create: body document / host / playbook not collected | AiAuthorizationFilter.cs | `Create_UnreadableHost_UnreadableDocument_UnknownIds_AndASeamFault_AreOneUniform403_AndNothingIsStored`, `Create_APlaybookTheCallerMayNotUse_IsTheUniform403_AndAPublicOneIsAccepted` (2/2) |
| C2 | stored session context not collected (messages, dispatch, agent) | AiAuthorizationFilter.cs | `Messages_APreFixSessionWhoseStoredContextIsUnreadable_IsDenied`, `Dispatch_AStoredContextTheCallerCannotRead_..._AndTheOrchestratorNeverRuns`, `Revocation_ASessionCreatedWhileTheCallerHeldRead_...`, `AgentMessage_AResumedSessionWithAnUnreadableStoredHost_IsTheUniform403` (4/4) |
| C3 | analysis host kind removed (sentinel treated as unsupported → dropped) | AiAuthorizationFilter.cs | `Create_AnAnalysisHost_IsDecidedByTheAnalysisReadRule_...` (2 cases), `Messages_AStoredAnalysisHost_IsReDecidedByItsAnchorsOnEveryTurn` (3/3) |
| C3b | analysis host decided by a row Read on `sprk_analysises` again (the first r1 build) | AiAuthorizationFilter.cs | `Create_AnAnalysisHost_IsDecidedByTheAnalysisReadRule_...` (2 cases), `Create_AnAnalysisHostWithNoAnchor_AnUnknownOne_AndAnAnchorReadFault_AreTheUniform403`, `Messages_AStoredAnalysisHost_...` (4/4) |
| C3c | an analysis with no anchor allowed | AiAuthorizationFilter.cs | `Create_AnAnalysisHostWithNoAnchor_...` (1/1) |
| C3d | a stored analysis host not collected | AiAuthorizationFilter.cs | `Messages_AStoredAnalysisHost_IsReDecidedByItsAnchorsOnEveryTurn` (1/1) |
| C4 | request host drop not applied (unsupported host stored) | AiAuthorizationFilter.cs | `Create_AHostOfAnUnauthorizableType_IsDropped_...` (4 cases), `SwitchContext_ToAnUnauthorizableHostType_DropsTheHost` (5/5) |
| C5 | stored host drop not persisted | AiAuthorizationFilter.cs | `Messages_AStoredHostOfAnUnauthorizableType_IsDroppedAndPersisted_AndTheTurnProceeds` (1/1) |
| C6 | SPE item: the caller's SPE read ignored | AiAuthorizationFilter.cs | `Messages_AStoredSpeItemDocument_IsDecidedByTheCallersOwnSpeRead` (1/1) |
| C6b | SPE item with no recorded drive treated as decided | AiAuthorizationFilter.cs | `Messages_AStoredSpeItemDocument_IsDecidedByTheCallersOwnSpeRead` (1/1) |
| C7 | chat playbook-use decision skipped | AiAuthorizationFilter.cs | `Create_APlaybookTheCallerMayNotUse_...`, `Messages_APreFixSessionWhoseStoredContextIsUnreadable_IsDenied` (2/2) |
| C8 | null dispatch body passed through to the handler | AiAuthorizationFilter.cs | `Dispatch_TheFilterIsKeyedOnTheDeclaredBodyType_AndANullBodyNeverReachesTheHandler` (1/1) |
| C9 | `.AddAiAuthorizationFilter()` removed from `POST /api/agent/message` | AgentEndpoints.cs | `AgentMessage_ABodyDocumentTheCallerCannotRead_IsTheUniform403_BeforeAnySessionIsCreated` (1/1) |
| C10 | PATCH `/context`: host and playbook not collected | AiAuthorizationFilter.cs | `SwitchContext_AnyIdTheCallerCannotRead_IsTheUniform403_AndTheSessionIsUnchanged` (1/1) |
| C11 | chat decision fails OPEN on a fault | AiAuthorizationFilter.cs | `Create_UnreadableHost_UnreadableDocument_UnknownIds_AndASeamFault_...` (1/1) |
| C12 | messages: per-turn document not collected | AiAuthorizationFilter.cs | `Messages_APerTurnDocumentTheCallerCannotRead_IsTheUniform403ProblemDetails_NotAStream_AndNoTurnRuns` (1/1) |
| C13 | a Compose Path B load does not record the drive (`DocumentDriveId = null`) | ComposeService.cs | `Sprk.Bff.Api.Tests.Seam.Compose.ComposeReferenceMapSessionLedgerSeamTests.Load_PathB_RecordsTheDocumentsDriveOnTheSession_SoItsTurnsCanBeDecidedByTheCallersSpeRead` (1/1) |
| P1 | parameter policy (syntax 400s) not applied | PlaybookAuthorizationFilter.cs | `Execute_ServerOwnedParameter_InAnyLetterCase_...` (6 cases), `Execute_ParameterOfTheWrongShape_Is400_AndNothingRuns` (6 cases), `AgentRunPlaybook_ParameterPolicy_AppliesTheSame_AndTheRunUserIsTheCaller` (13/13) |
| P1b | the allow-list removed (an undeclared key accepted) | PlaybookParameterPolicy.cs | `Sprk.Bff.Api.Tests.Domain.Ai.PlaybookParameterPolicyTests.AnUndeclaredKey_IsRefused_WhateverItsValue` (4 cases), `Execute_ParameterOfTheWrongShape_Is400_AndNothingRuns(tone, formal)` (5 of 10; the other cases are refused by other rules) |
| P9 | the node-reference scan ignores word boundaries | PlaybookParameterPolicy.cs | `ReferencesParameter_MatchesTheKeyInsideAnyTemplateExpression` (3 of 16 cases: the word-boundary ones) |
| P9b | an ISO date accepts a space-separated time | PlaybookParameterPolicy.cs | `TypedKey_IsAcceptedOnlyWhenItParsesAsItsType(todayUtc, "2026-10-04 10:00")` (1 of 21) |
| P2 | record parameters not authorized | PlaybookAuthorizationFilter.cs | `Execute_RecordParameter_UnknownDeniedAndFaulting_AreOneUniform403_AndNothingRuns`, `Execute_RecordParameter_ASeamFault_IsTheSameUniform403`, `AgentRunPlaybook_ParameterPolicy_...` (3/3) |
| P3 | Write-when-a-writing-node-references-it dropped (always Read) | PlaybookAuthorizationFilter.cs | `Execute_RecordParameterAWritingNodeUses_RequiresWrite_ReadSufficesOtherwise` (1/1) |
| P4 | the caller's systemuserid not published as the run user | PlaybookAuthorizationFilter.cs | `Execute_ReaderWithAcceptedParameters_RunsAsTheCaller_TheirSystemUserIdIsTheRunUser`, `AgentRunPlaybook_ParameterPolicy_...` (2/2) |
| P5 | an unresolvable caller allowed to run | PlaybookAuthorizationFilter.cs | `Execute_CallerWhoseSystemUserIdCannotBeResolved_Is403_AndNothingRuns` (1/1) |
| P6 | substitution-point escaping removed (query text rendered with raw values) | PlaybookOrchestrationService.cs | `A_ParameterValue_CannotChangeTheFetchXmlStructure` (4 cases), `A_NodeOutput_IsEscapedToo_ThroughAHelper`, `The_IndexRetrieveFilter_EscapesForAnODataStringLiteral`, `The_NestedWrapperConfigFormat_IsEscapedAtTheInnerLevel`, `ExecuteAsync_QueryDataverseNode_ReceivesFetchXmlWhoseSubstitutedValuesAreEscaped`, `ExecuteAsync_AFanOutQueryDataverseNode_ReceivesEscapedValuesInEveryIteration` (9/9) |
| P6b | fan-out iteration renders flat (unescaped) again | PlaybookOrchestrationService.cs | `ExecuteAsync_AFanOutQueryDataverseNode_ReceivesEscapedValuesInEveryIteration` (1/1) |
| P7 | substitution-point type check removed | PlaybookOrchestrationService.cs | `ExecuteAsync_ATypedParameterOfTheWrongType_FailsTheQueryNode_WithoutRunningIt` (1/1) |
| P8 | `ExecuteAsync` ignores `RunUserId` | PlaybookOrchestrationService.cs | `ExecuteAsync_RecordsTheHttpCallersOid_AsTheRunOwner_AndTheRunUserIsOnlyTheServerResolvedSystemUserId(true)` (1 of 2; the `false` case is the unchanged non-HTTP path) |
| V3 | `GET /api/agent/playbooks` filters by the Entra oid again | AgentEndpoints.cs | `AgentOwnedPlaybookList_FiltersByTheCallersSystemUserId_NotTheEntraOid` (1/1) |
| V4 | `GET /api/ai/chat/playbooks` filters by the Entra oid again | ChatEndpoints.cs | `ChatOwnedPlaybookList_FiltersByTheCallersSystemUserId_NotTheEntraOid` (1/1) |
| V5 | share passes the Entra oid again | PlaybookEndpoints.cs | `Share_And_Unshare_PassTheCallersSystemUserId_NotTheEntraOid` (1/1) |
| V5b | unshare passes the Entra oid again | PlaybookEndpoints.cs | `Share_And_Unshare_PassTheCallersSystemUserId_NotTheEntraOid` (1/1) |
| A1 | agent status: a lookup fault answers 500 again | AgentEndpoints.cs | `AgentStatus_ALookupFault_IsTheSameUniform404_AsAnUnknownRun` (1/1) |

First attempts that did not compile or match, re-run: C2 (`if (session is not null) return;` → CS8602 under
nullable-as-error; re-seeded as an always-true condition with no nullability effect), C9 and A1 (CRLF files; the script
now matches line endings). Round-0 seeds S1-S15 still apply to the code they named.

### 12.8 Suites (end of the round)

Affected set first (the new and edited contract / domain / orchestration tests, the chat and re-analysis fixtures, the
guard file): 296/296 after the allow-list change. The full suites then ran three times, each run sequential with
nothing else building, because code changed after the first two:

| Suite | Run 1 (first commit) | Run 2 (+ analysis host by 162's rule) | **Run 3 (final, + regex-free policy)** |
|---|---|---|---|
| `Sprk.Bff.Api.Tests` (full BFF unit suite, incl. the contract / seam / domain globs) | 14,382 passed, 0 failed, 54 skipped (14,436) | 14,384 passed, **1 failed**, 54 skipped (14,439) | **14,399 passed, 0 failed, 54 skipped (14,453)** |
| `Spaarke.ArchTests` (NetArchTest + route-authorization guard) | 346/346 | 346/346 | **346 passed, 0 failed (346)** |
| `Sprk.Bff.Api.IntegrationTests` | 101/101 | 101/101 | **101 passed, 0 failed (101)** |
| `Spe.Integration.Tests` | 392 passed, **6 failed**, 25 skipped; re-run 398 / 0 / 25 | 398 passed, 0 failed, 25 skipped (423) | **398 passed, 0 failed, 25 skipped (423)** |
| `dotnet list package --vulnerable --include-transitive` (Sprk.Bff.Api) | none | none | **none** |

- **Spe run 1, 6 failures** (all in `ReAnalysisFlowTests`): the fixture's stored document id was `"doc-reanalysis-001"`
  (a non-GUID with no drive, which a turn can no longer decide — fail closed) and its `IAccessDataSource` mock was
  `Loose` with no `GetRecordAccessAsync` setup (the session playbook's row is now checked as the caller). Fixed in the
  fixture only (a GUID document id; a permissive `GetRecordAccessAsync`, matching the fixture's already-permissive
  `IAiAuthorizationService`); targeted `ReAnalysisFlowTests` + `ChatEndpointsTests` 28/28, then the full re-run.
- **Unit run 2, 1 failure:** `Execute_ParameterOfTheWrongShape_Is400_AndNothingRuns(todayUtc, "2026-10-04' or")` —
  `RegexMatchTimeoutException` from `PlaybookParameterPolicy`'s date regex, under full-suite load; it escaped the run
  filter as a 500. A real, load-dependent defect, not a test problem: fixed (§12.4, S2), seeds P9 / P9b, run 3 green.
  The same case passed in isolation before and after the fix.
- The Compose Path B seam test was added after run 1 (its class 3/3 in isolation, seed C13); runs 2 and 3 include it.
- Run 3 adds 17 tests over run 1: the seam test, two analysis-host chat tests, and fourteen policy cases.

### 12.9 Live gate additions (dev, main session; ids redacted to 8 characters)

The POML's chat-family gates (a)-(f) and (j) now apply, plus:

- (k) **Parameters:** U1 `POST /api/ai/playbooks/{a public notification playbook}/execute` with
  `parameters.timeWindowHours = "24'/>"` → 400 `playbook.parameter-rejected`; with `parameters.userId = <any GUID>` → 400;
  with `parameters.timeWindowHours = "48"` → SSE, and its Query Dataverse node returns only U1's own records
  (`eq-userid` now resolves to U1's systemuserid).
- (l) **Compose Path B:** open an unsaved document in Compose (a Path B session), run a Compose toolbar action →
  allowed (the load records the drive). A Path B session last loaded BEFORE this deploy is refused once (403) until the
  document is reopened — expected, record it.
- (m) **Unsupported host types:** open the Console on a contact record → the Assistant works, but without host context
  (no record memory for the contact). Expected under round 16 item 2; record it.
- (n) **Analysis wizard:** CreateAnalysisWizardWidget's session create (sentinel `sprk_analysisoutput`) succeeds for the
  analysis its user just created, when the user can read the analysis's anchor (its document / record). A STANDALONE
  analysis (no anchor) is refused (403) until task 162's round-15 personal rule lands (§12.10) — record which case the
  wizard produces.

### 12.10 For the main session

- **The analysis host follows owner round 15 (built this round, after the first r1 build).** The first r1 build
  decided the `sprk_analysis` / `sprk_analysisoutput` host by Read on the `sprk_analysises` ROW. `prvReadsprk_analysis` is
  Deep on Core and Basic User (§12.5), so a colleague in the same business-unit subtree passed that check for another
  user's analysis — exactly the "business-unit-depth row Read" round 15 item 4 forbids. Round 16 item 2 asks for the
  analysis kind with no new map and one constant; round 15 asks that an analysis be readable only through its anchors
  (or, standalone, by its creator). Both are met by deciding the host with task 162's analysis-read rule, extracted into
  ONE declaration (`AnalysisAuthorizationFilter.ResolveAnalysisReadTargetsAsync`) that the GET route and the chat host
  share. Seed C3b (the row Read restored) turns three tests red.
- **Standalone analyses — owned by task 162 (round 15 items 1-4), inherited here with no further change.** On this
  branch 162's rule denies an analysis with no anchor (162-r1). Round 15 makes a standalone analysis readable by its
  creator (matched by systemuserid) and assigns that work, the writer fixes and the backfill to task 162. 162 adds it in
  the shared declaration, and the chat host inherits it with no change in 164; until 162's round-15 round is merged,
  a chat on a standalone analysis is refused for everyone (fail closed, never open). Integration order is already 162 →
  164 (round 16 item 7). Merge note: if 162 expresses the creator rule as a check of a path other than Document /
  Record, `AiAuthorizationFilter.IsAnalysisReadableAsync` denies it (its `default` branch) until it evaluates that path
  the way the per-route evaluator does (a case in one switch; the failure mode is a refusal, never an allow), and the
  chat standalone test gains its creator case alongside 162's own.
- **Publish size (items 7 / 28) not measured**, per this round's harness instruction. Net change this round is code only
  (no package): expected well under +0.1 MB. Procedure if the main session measures at integration: CLAUDE.md §10 —
  fresh worktrees of `origin/master` and this branch at SHORT paths (e.g. `C:\wt164m`, `C:\wt164b`), `dotnet publish -c
  Release` of `Sprk.Bff.Api` into `deploy/api-publish`, `Compress-Archive -CompressionLevel Optimal` both, report both
  sizes, the delta and equal file counts.
- **Integration order:** 162 (and every 162 fix round) → 164 → 163's `/ask` parameter consumer. 163's `/ask` should call
  `PlaybookParameterPolicy.Evaluate` (syntax 400) and authorize `RecordParameters` with
  `PlaybookAuthorizationFilter.RecordParameterOperation(nodes, name)` for the right — the same rule as here; its
  subject's `matterId` gets Write when `matter-health-single`'s UpdateRecord node references it (round 16 item 1).
- **Behaviour changes to expect:** a chat turn now makes the playbook-use check (`GetPlaybookAsync`, 5 Dataverse reads,
  uncached) plus the cached host / document checks — measurable latency on sessions with a playbook (candidate: share
  the provider's own `GetPlaybookAsync` per request; not changed here). Console sessions on contact / account /
  other record types lose their host context (round 16). `doc-test-001`-style ids now 400 on create / PATCH / messages.
- **Conflict check (this round):** 25 open PRs, none touches any file this round edits (incl. `ComposeService.cs`,
  `ChatSession.cs`, `ChatSessionManager.cs`, `StoredSession.cs`, `ChatEndpointsTests.cs`); `work/spaarkeai-compose-r8` and
  `work/spaarkeai-word-add-in-r1` do not touch them either. Sibling sweep branches: 163 edits the same
  `ChatEndpointsTests.cs` fixture ~30 lines away (separate hunk) and `RouteAuthorizationGuardTests.cs`; 167-r2 edits
  `RouteAuthorizationGuardTests.cs`.
- `.claude/**`: no edit needed. The worktree's `NOTE-FROM-MAIN.md` was not committed (deleted before commit, as asked).

### 12.11 Quality gates (task-execute Step 9.5) for this round

**code-review** (coverage-first; severity · confidence):
- Critical: none found.
- **W1 · high (FIXED this round):** the first r1 build decided the analysis host by a Deep row Read, contrary to owner
  round 15 item 4; now task 162's analysis-read rule decides it (§12.10 first bullet, seeds C3-C3d).
- **W2 · medium:** per-turn latency of the stored-playbook check (§12.10).
- **W3 · medium:** Compose Path B sessions loaded before deploy are refused until reopened (fail closed, by design).
- **W4 · low:** `AiAuthorizationFilter.cs` grew from 155 to 802 lines (about half of it doc comments) with a second responsibility (chat context) — the
  POML placed it there ("extend AiAuthorizationFilter", "one shared evaluation"); cohesive (one question: may this caller
  use these records for an AI turn). `PlaybookAuthorizationFilter.cs` 720 lines; the new `PlaybookParameterPolicy.cs` 259.
- **S1:** the near-miss in §12.3 (self-referencing context) was caught by a test before commit; the fan-out and
  structured-predecessor shapes are now pinned.
- **S2 (FIXED):** a load-dependent answer — `PlaybookParameterPolicy`'s regular expressions carried match timeouts, so a
  slow first match threw instead of answering (seen once in the second full run). Replaced by code that cannot time
  out (§12.4).
- AI smells: no interface added; no catch-log-rethrow; catches deny.

**adr-check:** ADR-001 (Minimal API, `MapPatch`), ADR-002 (no plugins), ADR-003 (every fault / missing token / missing
service / undecidable id denies — seeds C11, P5), ADR-007 (SPE read through `ISpeFileOperations`, no Graph types),
ADR-008 (every decision an endpoint filter in the route's chain; the agent-status owner comparison stays the documented
filter/endpoint pair), ADR-010 (no new interface or registration), ADR-013 (no `IPlaybookService` outside the
grandfathered filter — `ADR013_AiBoundaryTests` green), ADR-015 (logs carry ids only; the typed-parameter failure
message names the key, not the value), ADR-019 (ProblemDetails with reasonCode / errorCode), ADR-038 (no banned test
shapes; `GetUninitializedObject` only for handler services a denied request never reaches, as two existing tests do).
No violation, no §6.5 path needed.
