# Task 164 — AI route authorization sweep (chat / agent / playbooks / prompts / record-match)

> GitHub #1103 · branch `task/uac-r2-164` · base `e6dd48b43` (work/unified-access-control-r2) · built on
> `task/uac-r2-162` @ `8a0e3d103` (merge commit `5b1056cdf`) · executed 2026-10-03 · rigor FULL (opus, xhigh).
> Review 164's own diff as `git diff 5b1056cdf..task/uac-r2-164`.

## 1. Outcome in one table

The task covers 14 sweep findings (19 route keys). Owner round 10 item 1 removed six of the findings by
**deleting** their routes; four findings are **fixed**; the chat family (five findings) and the playbook
`Parameters` half of two fixed findings are **stopped on escalation triggers 3 and 4** (no owner answer exists for
either; per the harness rule an unanswered trigger is a first-class stop).

| Sweep row | Route key | Result |
|---|---|---|
| 55 | `GET /api/ai/playbooks/by-id/{id}` | **FIXED** — playbook-use decision before the cache, uniform 404 |
| 29 | `POST /api/ai/playbooks/{id:guid}/execute` | **FIXED** (playbook + documents) · `Parameters` **STOPPED** (trigger 4) |
| 19 | `POST /api/agent/run-playbook` | **FIXED** (playbook + document) · `Parameters` **STOPPED** (trigger 4) |
| 78 | `GET /api/agent/playbooks/status/{jobId:guid}` | **FIXED** — run owner (`StartedByOid`) comparison, uniform 404 |
| 55 | `GET /api/ai/playbooks/by-name/{name}` | **DELETED** (no caller, not published) |
| 54 | `PUT /api/ai/playbooks/{id:guid}/nodes/reorder` | **DELETED** |
| 57 | `GET`, `PUT`, `DELETE /api/ai/prompts/{id}`, `POST /api/ai/prompts/{id}/render` | **DELETED** |
| 56 | `GET /api/ai/prompts/`, `POST /api/ai/prompts/` | **DELETED** |
| 31 | `POST /api/ai/document-intelligence/match-records` | **DELETED** |
| 32 | `POST /api/ai/document-intelligence/associate-record` | **DELETED** |
| 24 | `POST /api/ai/chat/sessions` | **STOPPED** — escalation trigger 3 (host-type / id vocabulary) |
| 23 | `PATCH /api/ai/chat/sessions/{sessionId}/context` | **STOPPED** — trigger 3 |
| 25 | `POST /api/ai/chat/sessions/{sessionId}/messages` | **STOPPED** — trigger 3 |
| 53 | `POST /api/ai/chat/sessions/{sessionId}/dispatch` | **STOPPED** — trigger 3 |
| 18 | `POST /api/agent/message` | **STOPPED** — trigger 3 |

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

## 4. Escalations fired (open — for the owner)

### 4.1 Trigger 3 — HOST-TYPE OR ID VOCABULARY (chat family F0, F1, F2, F3, F12 stopped)

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
| `POST /api/ai/playbooks/{id:guid}/execute` | filter: `PlaybookAuthorizationFilter` (Run) — playbook-use + `AuthorizationService.AuthorizeAsync` Read/Write per document. Parameters: OPEN (trigger 4) — keep a Pending waiver for the parameter half | `Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests.Execute_UnreadableDocument_Is403ProblemDetailsBeforeAnySseHeader_AndNothingRuns` |
| `POST /api/agent/run-playbook` | filter: `AgentAuthorizationFilter` (identity) + `PlaybookAuthorizationFilter` (Run). Parameters: OPEN (trigger 4) | `Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests.AgentRunPlaybook_UnreadableDocument_Is403_AndNothingRuns` |
| `GET /api/agent/playbooks/status/{jobId:guid}` | OWNER-COMPARISON (owner round 12 item 4 basis): handler compares `PlaybookRunStatus.StartedByOid` with the caller oid after `AgentAuthorizationFilter` | `Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests.AgentStatus_AnotherCallersRun_AnOwnerlessRun_AndAnUnknownRun_AreOneUniform404_WithNoJobId` |
| `GET /api/ai/playbooks/by-name/{name}` | DELETED (no caller, not published) | `Sprk.Bff.Api.Tests.AiPlaybookPromptRecordMatchRouteRetirementTests.RetiredRoutes_AreAbsentFromTheEndpointTable` |
| `PUT /api/ai/playbooks/{id:guid}/nodes/reorder` | DELETED | same |
| `GET /api/ai/prompts/`, `POST /api/ai/prompts/` | DELETED | same |
| `GET /api/ai/prompts/{id}`, `PUT /api/ai/prompts/{id}`, `DELETE /api/ai/prompts/{id}`, `POST /api/ai/prompts/{id}/render` | DELETED | same |
| `POST /api/ai/document-intelligence/match-records`, `POST /api/ai/document-intelligence/associate-record` | DELETED | same |
| `POST /api/ai/chat/sessions` · `PATCH /api/ai/chat/sessions/{sessionId}/context` · `POST /api/ai/chat/sessions/{sessionId}/messages` · `POST /api/ai/chat/sessions/{sessionId}/dispatch` · `POST /api/agent/message` | OPEN — escalation trigger 3; keep their Pending waivers owned by 164 | n/a |
| `GET /api/ai/playbooks/`, `GET /api/ai/chat/playbooks`, `GET /api/agent/playbooks` (CallerScopedOnly, owner round 12 item 6) | handler decision: the owned list is filtered by the caller's systemuserid (WhoAmI over OBO); public list unchanged | `Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests.OwnedPlaybookList_FiltersByTheCallersSystemUserId_NotTheEntraOid` |

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
