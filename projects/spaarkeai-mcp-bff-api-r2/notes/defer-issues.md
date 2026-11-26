# Deferred issues (spaarkeai-mcp-bff-api-r2)

## From task 002 (2026-11-25)

### D-002-1: Coordination: retire T257 per-customer Copilot agent plumbing in customer-provisioning-orchestration-r1

- **GitHub issue**: https://github.com/spaarke-dev/spaarke/issues/3173 (filed by the main session 2026-11-25)
- **Title**: `customer-provisioning-orchestration-r1: remove the retired "Spaarke AI" Copilot agent (T257) plumbing`
- **Body**:
  The legacy "Spaarke AI" declarative agent was retired on 2026-11-25 by `spaarkeai-mcp-bff-api-r2` (task 002 deleted `src/solutions/CopilotAgent/`, `publish-copilot-agent-template.yml`, `scripts/copilot-agent/`, `scripts/Deploy-CopilotAgent.ps1`, `infrastructure/bot-service/`). Task 002 deliberately did not touch the T257 files owned by this project. They now reference deleted things:
  - `infrastructure/bicep/platform-controlplane.bicep` (:195, :507), `modules/controlplane-worker-app-service.bicep` (:208, :234), `parameters/platform-controlplane-dev.bicepparam` (:125), compiled `platform-controlplane.json`: `copilotAgentClientAppId` / the shared Spaarke Copilot Agent app pre-authorization.
  - `scripts/provisioning-prereqs/prereqs.yaml` :231 (pointer to t257-copilot-agent-design.md).
  - `scripts/Extend-DataverseEnvironmentSchema-v3.3.ps1` :45, :186 and `SpaarkeMaster/Entities/sprk_dataverseenvironment/Entity.xml`: the Copilot OAuth registration id column and its description (names the deleted render script).
  - `.claude/skills/provision-environment/SKILL.md` post-Ready Copilot gate (~:2546) will fail at the render step (task 003 of the r2 project edits `.claude/`).
  - `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` section 7.12 (task 004 of the r2 project).
  Decide: remove the column and bicep param (needs a Dataverse schema change and a control-plane redeploy), or keep them dormant. Tenant resources (Registry rows, Azure Bot, Entra app) are the owner's runbook E1/E2.
- **Labels**: project label for customer-provisioning-orchestration-r1; Type=Task.

## From task 005 (2026-11-25)

### D-005-1: Runs left "Running" after a restart or failed final write
- **GitHub issue**: https://github.com/spaarke-dev/spaarke/issues/3178
- Staleness limit after which a stored Running run reads as failed; decide in task 030 / FR-14.

### D-005-2: Retention for sprk_playbookrun / sprk_playbooknoderun
- **GitHub issue**: https://github.com/spaarke-dev/spaarke/issues/3179
- ADR-015; mechanism likely a Dataverse bulk-delete job.

> Defects found during task execution that are out of the task's scope (task 006 entries; issues filed by the main session).

## DEF-006-01 — Event create copies a regarding record's name to a caller without Read on it (security, pre-existing)

- **Found by:** task 006 adversarial review, tenant-isolation pass (classed F1, pre-existing; not introduced by the extraction).
- **Where:** `src/server/api/Sprk.Bff.Api/Services/Events/EventsCapability.cs` — `CreateCoreAsync` (regarding block) and
  `ResolveRegardingFieldsAsync`; policy `src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs:286`
  (`["event.attach_regarding"] = AccessRights.AppendTo`).
- **Defect:** the create authorizes AppendTo on the regarding record only, then reads that record's primary name and number
  app-only and stores them on the new event (`sprk_regardingrecordname`, `sprk_regardingrecordnumber`). AppendTo does not
  imply Read, so a caller whose role grants Append To widely but Read narrowly can learn a record's name and number.
- **Scenario:** B (Append To at Organization depth, Read on matters at User depth) `POST /api/v1/events` with
  `regardingRecordType=1` and the id of A's matter → the create succeeds → `GET /api/v1/events/{newId}` (B's own event)
  returns A's matter name. Same through `IEventsCapability.CreateAsync` once an MCP tool calls it.
- **Why not fixed in task 006:** the task is a byte-preserving extraction (frozen-bytes baselines); either fix changes what
  REST returns or who is allowed, which is an authorization behaviour change needing an owner decision (root CLAUDE.md §6).
- **Fix options:** (a) read the name/number only after a caller Read check (probe or `IDataverseUserClient`); without Read,
  keep the request's own name and no number — deny bytes unchanged; (b) make `event.attach_regarding` require
  `Read | AppendTo` — same 403 rights answer, but changes who may attach.
- **Must land before** the events create tool is exposed to Copilot (task 025 / 036).
- **GitHub issue:** https://github.com/spaarke-dev/spaarke/issues/3181

## DEF-006-02 — `EventStatusDeprecationTests.NoSourceFile_ReadsOrWrites_TheDeprecatedColumn` fails on the base (pre-existing)

- **Found by:** task 006 broad test run (`Sprk.Bff.Api.Tests`, 11005 passed / 1 failed).
- **Where:** `scripts/demo-data/DemoDataLib.ps1` and `scripts/demo-data/reference-names.json` reference the deprecated event
  status column the guard forbids; task 006 touched no file under `scripts/`.
- **Fix:** move the demo-data scripts to the replacement column, or add them to the guard's documented exceptions with a reason.
- **GitHub issue:** https://github.com/spaarke-dev/spaarke/issues/3182

## From task 004 (2026-11-25)

### D-004-1: dataverse-mcp-usage skill and knowledge notes use removed tool names
- **GitHub issue**: https://github.com/spaarke-dev/spaarke/issues/3185

## From task 002 verifier (2026-11-25)

### D-002-2: BYOK template still provisions an Azure Bot for the retired agent; runbook E2 misses the bot's Key Vault access policy
- **GitHub issue**: https://github.com/spaarke-dev/spaarke/issues/3186

## From task 021 (2026-11-25)

### D-021-1: CI path filters miss the shared widget transport, so a change to it does not rebuild, retest or republish the Decisions widget
- **GitHub issue**: none — fixed in scope by the main session in 13c7770195 (`src/client/widgets/shared/**` added to both path filters).
- **Title**: `ci: add src/client/widgets/shared/** to the decisions-widget path filters (tier-1 MCP surface, publish-bff-artifact)`
- **Body**:
  Task 021 (spaarkeai-mcp-bff-api-r2) moved the Decisions widget's MCP transport into `src/client/widgets/shared/mcp/`, which the
  widget now imports by relative path and builds into `dist/index.html` (the HTML the BFF embeds as `ui://spaarke/decisions.html`).
  Two path filters still list only `src/client/widgets/spaarke-decisions/**`:
  - `.github/workflows/ci-tier1-blocking.yml` ~:217 (the MCP surface flag that runs the widget job at ~:1205: typecheck, jest, build, 3 MB gate);
  - `.github/workflows/publish-bff-artifact.yml` :38 (the pull_request dry run of the embedded widget).
  Failure scenario: a PR that edits only `src/client/widgets/shared/mcp/mcpFetch.ts` (for example a change in error mapping) runs
  neither the widget's jest suite (which also runs the shared tests) nor its build, so a regression in the live Decide lane / wizard
  error handling merges green. Same gap, pre-existing, for `src/client/shared/Spaarke.UI.Components/src/**` files the widget imports
  by deep path (worklist, Decision wizard). Fix: add `'src/client/widgets/shared/**'` to both filters (and consider the deep-imported
  ui-components folders). Task 021 could not edit `.github/workflows/**` (outside its file list; ci-workflows hot path).
- **Labels**: project label spaarkeai-mcp-bff-api-r2; Type=Task.

### D-021-2: CI filters miss the ui-components Worklist/DecisionWizard source the decisions widget bundles (pre-existing)
- **GitHub issue**: https://github.com/spaarke-dev/spaarke/issues/3196

## From task 020 (2026-11-25)

### D-020-1: HandoffUrlBuilder falls back to a hard-coded dev Dataverse host when the environment URL is missing (found in passing)

- **GitHub issue**: https://github.com/spaarke-dev/spaarke/issues/3198 (must land before task 030 returns console links)
- **Title**: `HandoffUrlBuilder registration falls back to https://spaarkedev1.crm.dynamics.com when Dataverse:EnvironmentUrl is unset`
- **Body**:
  `Services/Ai/ToolFrameworkExtensions.cs` `AddHandoffUrlBuilder` (:97-104, moved unchanged from the legacy agent module by r2 task 001) builds `HandoffUrlBuilder` from `Dataverse:EnvironmentUrl`, then `DataverseServiceClient:Url`, then the literal `https://spaarkedev1.crm.dynamics.com`. In a customer environment where both settings are missing or misnamed, every server-composed deep link (chat create-record refusal links today; MCP long-running `consoleLink`s from tasks 030/032/034 if they take links from it) silently points at the Spaarke dev tenant instead of failing at startup. Failure scenario: a customer BFF deployed with the URL under another key sends its users links into `spaarkedev1`. It violates the no-hard-coded-values portability rule. Fix options: drop the literal and fail at startup (options validation) when neither key is set, or reuse the validated `Dataverse:EnvironmentUrl` options the rest of the BFF reads. r1's `DecisionsTextSummary` already avoids this builder for this reason. Not fixed in 020: the file is outside 020's scope (task 001's move kept behaviour unchanged on purpose).
- **Labels**: project label spaarkeai-mcp-bff-api-r2; Type=Bug.

## Task 020 verifier (2026-11-25)
- F2 fixed in the main session: the guide, the McpLongRunning remarks and CAPABILITY-INDEX row 39 pointed tools at a console-link source that doesn't exist. They now say that task 030 adds a run-link member to the public-contract seam (constraint added to 030).
- Known limits:
  - K4: consoleLink accepts any https host, and the raw string is stored.
  - K4: Status.result is unbounded; 030/032 add worst-case size tests.
  - K4: Partial is untyped; revisit when the first non-decision tool uses it.
  - K2: an unwired partial can hide a stale row (fails closed for tagged tools).
  - K2: an out-of-range McpRunState is a programming error only.

## From task 026 (2026-11-25)

### D-026-1: PortfolioService caches an EMPTY portfolio for 5 minutes when the Dataverse read fails or the caller cannot be resolved (pre-existing, found in passing)

- **GitHub issue**: #3205
- **Title**: `PortfolioService: a failed matter read is aggregated as zero matters and cached for 5 minutes (REST /api/workspace/portfolio and spaarke_get_portfolio)`
- **Body**:
  `Services/Workspace/PortfolioService.cs` `QueryMattersFromDataverseAsync` answers `Array.Empty<MatterRecord>()` when the oid does not resolve to a systemuser or when `ReadMattersForSystemUserAsync` reports `Unavailable` (a failed people-targeting read, an over-ceiling set, or any failed caller-context chunk). `GetPortfolioSummaryAsync` then aggregates that into an all-zero `PortfolioSummaryResponse` and writes it to the `workspace:{userId}:portfolio` cache entry for 5 minutes (also reused by `GetHealthMetricsAsync` and the briefing). Failure scenario: Dataverse throttles one chunk for a user with 200 matters; the Console portfolio card shows 0 active matters, 0 spend, 0 at risk for 5 minutes, and through the new MCP tool (FR-08) Copilot tells the user "you have no active matters" with no error. Not a safe empty state: it is a wrong answer presented as fact. Fix options: return an unavailable result (REST 503 / tool isError `workspace.portfolio.unavailable`) and do not cache it; or cache only a successful read. Not fixed in 026: the file is outside its scope and REST parity (FR-08 acceptance) means the tool must not diverge from REST; the fix changes both hosts together. The "graceful empty state" is documented in the service as a pre-existing endpoint constraint (task 152), so the owner should decide whether the REST contract may change.
- **Labels**: project label spaarkeai-mcp-bff-api-r2; Type=Bug.

### D-026-2: two real-host tests pin the exact tool set and fail for any new tool (follow-up for the main session, not a defect) — DONE in merge 25212daaf3 (both pins updated)

- `tests/integration/contract/Api/Mcp/McpHostContractTests.cs` `RealBffHost_ToolsList_WhenEnabledWithAValidToken_IsExactlyTheFiveV1Tools` (the names list and `HaveCount(5)`) and `tests/integration/auth/Mcp/McpCallerPathTests.cs` `Test5_TheRealBffHost_TheTwoStaticSchemaTools_PublishExactlyTheirPinnedArguments_ASchemaPinNotAnInjectionProof` (the static-schema dictionary; `spaarke_get_portfolio` is a static-schema tool with no arguments: add `["spaarke_get_portfolio"] = []`, or add it to `InferredSchemaTools`) fail as soon as the first r2 tool lands. Tasks 023/024/026/027 run in parallel, so none edits them; update once after the wave merges.

## From task 023 (2026-11-25)

### D-023-1: GetDocumentsByLookupAsync returns at most 50 rows with no ordering, so the matter document list can silently miss documents

- **GitHub issue**: #3206
- **Title**: `IDocumentDataverseService.GetDocumentsByMatterAsync: TopCount 50 and no OrderBy; matters with more documents are cut arbitrarily`
- **Body**:
  `DataverseServiceClientImpl.GetDocumentsByLookupAsync` (the query behind `GetDocumentsByMatterAsync` / `GetDocumentsByProjectAsync`) sets
  `TopCount = 50` and no order. Failure scenario: a matter with 60 documents shows 50 arbitrary ones in the email attachment picker
  (task 011, through `spaarke_list_documents`) and the cut changes between calls. Task 023 tells the model (`hasMore` is true at exactly
  50 rows) but cannot page or order, because the source has no paging. Task 040's same-matter check must not rely on list membership
  (use `DocumentEntity.MatterId`, D7). Fix: order by `modifiedon desc` and add a paging cookie or an explicit limit parameter to the
  service method. Out of scope for 023 (shared Spaarke.Dataverse surface, another consumer set).
- **Labels**: project label spaarkeai-mcp-bff-api-r2; Type=Bug.

### D-023-2: GET /api/v1/documents?containerId= has no caller and carries a known type-bug (found in passing)

- **GitHub issue**: #3207
- **Title**: `DataverseDocumentsEndpoints.ListDocumentsAsync: a route with zero callers, kept behind two authorization gates`
- **Body**:
  The container-keyed list route (`DataverseDocumentsEndpoints.ListDocumentsAsync`) has no caller in the repository (searched src/ and
  tests/ for the path), and the project's task 078 section 4 already notes a type bug on it. Task 023 did not reuse it (the picker asks
  by matter). Failure scenario: a dead route is still an authorization surface that must stay correct (two filters guard it) and that a
  refactor can break unnoticed. Fix: delete it with its filter wiring or give it a caller and a test. Out of scope for 023.
- **Labels**: project label spaarkeai-mcp-bff-api-r2; Type=Task.

### D-023-3: DocumentEntity.MatterName / ProjectName are never filled in production (pre-existing; the document tools withhold names until it is fixed)

- **GitHub issue**: #3215
- **Title**: `DataverseServiceClientImpl.MapToDocumentEntityWithLookups sets MatterId/ProjectId but never MatterName/ProjectName, and three outputs depend on the null`
- **Body**:
  `MapToDocumentEntityWithLookups` (`src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`, ~:1461-1483) copies the lookup ids
  only, although the retrieves ask for `sprk_matter` / `sprk_project` and the SDK sets `EntityReference.Name`. `DocumentEntity.MatterName` /
  `ProjectName` are therefore null in production. Filling them (`MatterName = matterRef.Name`, `ProjectName = projectRef.Name`) changes three
  outputs that today fall back on the null: (1) `POST /api/ai/search` with `associatedOnly=true`:
  `SemanticSearchService.MapDocumentEntityToSearchResult` sets `parentEntityName` (null becomes the name); (2) `GET /api/ai/visualization/related/{documentId}`:
  `VisualizationService.CreateParentHubNode` sets the hub label (the literal "Matter"/"Project" becomes the name); (3) the index parent
  stamp: `DocumentIndexParentResolver.DecideAsync` sets `ParentEntityContext.EntityName` (the "Unknown ..." fallback becomes the name;
  used by the `RagEndpoints` index paths, so what is written to the index changes). Failure scenario today: the Spaarke MCP tools
  `spaarke_list_documents` and `spaarke_get_document` cannot name the matter or project a document is filed under, and the REST
  search, visualization and index outputs above show placeholder or empty names. Fix: decide whether those three output changes are
  wanted (owner / uac-r2), then fill the names in the mapping and update their tests. After the fix the document tools add the name back
  (`regarding.name` on the list, `regardingMatter.name` / `regardingProject.name` and the "Filed under ..." text lines on the get;
  task 023 removed them: D-023-names in `023-decisions.md`).
- **Labels**: project label spaarkeai-mcp-bff-api-r2; Type=Bug.
## From task 024 (2026-11-25)

GitHub issues: D-024-1 #3208, D-024-2 #3209 (filed by the main session).

### D-024-1: the playbook list routes answer 500, not the AI kill switch's 503, when AI is disabled (pre-existing)
- **Title**: `GET /api/ai/playbooks and /public answer 500 "Failed to list playbooks" when the AI feature is switched off`
- **Body**: With AI disabled, `NullPlaybookService.ListUserPlaybooksAsync` / `ListPublicPlaybooksAsync` throw `FeatureDisabledException`. The two list handlers' catch-all (now `PlaybookEndpoints.ListPlaybooksForCallerAsync` → `Failed`, unchanged bytes by design in task 024) turn it into a 500, where `GET /api/ai/chat/playbooks` (`ChatEndpoints.cs` ~:1867) answers the kill switch's 503 (`ex.AsFeatureDisabled503()`). Failure scenario: an environment with AI switched off shows "Failed to list playbooks" (an error to retry) instead of "AI is disabled"; `spaarke_list_playbooks` relays it as `playbook.list-failed` / 500 "try again shortly". Fix: add a `Disabled` outcome (503, the feature-disabled code) to `PlaybookListResult`, map it in the REST handlers (a REST contract change; update `PlaybookListRoutesFrozenBytesTests`) and in the tool.
- **Labels**: project label spaarkeai-mcp-bff-api-r2; Type=Bug.

### D-024-2: the chat playbook picker keeps its own copy of the owned+public list rules (pre-existing duplication)
- **Title**: `GET /api/ai/chat/playbooks re-implements the owned/public playbook list instead of PlaybookEndpoints.ListPlaybooksForCallerAsync`
- **Body**: `ChatEndpoints.cs` ~:1830-:1900 resolves the owner (`ResolveCallerSystemUserIdAsync`), lists owned then public at page size 50 and de-duplicates by id — the same rules task 024 made one method for (and the MCP tool's union). It differs: on an unresolvable systemuser it lists public only, on an owned-list fault it continues with public only, and it answers the kill switch 503. Failure scenario: a future change to the owner or fail-closed rule in the shared method (e.g. D-024-1) silently misses the picker. Fix: call the shared method twice (owned, public) and keep only the picker's own union and 503 mapping.
- **Labels**: project label spaarkeai-mcp-bff-api-r2; Type=Task (tech debt).
## From task 027 (2026-11-25)

### D-027-1: POST /api/ai/search and /api/ai/search/count return the raw exception message as the 500 `detail` (pre-existing, found in passing)

- **GitHub issue**: #3212 (filed by the main session; fix assigned to task 022)
- **Title**: `Semantic search 500s echo ex.Message in ProblemDetails.detail (Search Failed / Count Failed)`
- **Body**:
  `src/server/api/Sprk.Bff.Api/Api/Ai/SemanticSearchEndpoints.cs` `Search` (catch-all, `title: "Search Failed", detail: ex.Message`)
  and `Count` (`"Count Failed"`, same) return the exception text of any non-`SdapProblemException` failure to the caller. Failure
  scenario: an Azure AI Search or Dataverse enrichment failure whose message names the search endpoint, index name, a filter
  fragment or a Dataverse URL is shown to the signed-in user, and an MCP tool that maps a REST-equivalent 500 would hand it to the
  model. ADR-019 MUST NOT leak; the sibling `RecordSearchEndpoints` already fixed this (fixed text plus
  `code = RECORD_SEARCH_FAILED`, exception logged). Fix: the same fixed text and a stable code for both routes. Not fixed in 027:
  the task's constraint freezes the REST bytes of these routes, and the catch is not an access decision. Task 022 must not
  surface search exceptions' messages in tool results.
- **Labels**: project label spaarkeai-mcp-bff-api-r2; Type=Bug.

## D-027-2 — empty record page past the end leaked the total count (FIXED in 027) → #3218
- **Status**: fixed in scope (RecordSearchAuthorizationFilter sets TotalCount = 0 on an empty page); the issue tracks the regression test `tests/integration/regression/Ai/Issue3218_RecordSearchEmptyPageCountLeakTests.cs`.

## D-027-3 — `useRecordSearch.ts:182` `hasMore` can never be true → #3219
- **Status**: deferred (client, not this project's surface). Since uac-r2 task 077 `TotalCount` is the page's row count, so `hasMore = totalCount > results.length` is always false and `loadMore` never loads. Found by the 027 pass-2 review.
- **Labels**: Type=Bug.

## D-022-1 — PARTIAL_RESULTS warning derived from unfiltered index counts → #3220
- **Status**: deferred (fix with #1533). On scope=all, `poolWasSaturated` compares the unfiltered index count with the pool size. REST and `spaarke_search_documents` relay it as a 1-bit signal about documents the caller cannot open. Found by the 022 pass-1 review (K2).
- **Labels**: spaarkeai-mcp-bff-api-r2; Type=Bug.

## D-022-2 — SemanticSearchEndpoints logs full exceptions; client abort → 500 → #3221
- **Status**: deferred (pre-existing REST route, outside 022's diff). Log the exception type only; return 499 at Debug on `RequestAborted`, as 024 did for playbook lists.
- **Labels**: spaarkeai-mcp-bff-api-r2; Type=Bug.

## From task 030 (2026-11-26)

### D-030-1: ComposeProfileDispatcher clears the save request's IHttpContextAccessor and carries the bearer token out of the request (pre-existing, found in passing)
- **GitHub issue**: #3224 (filed by the main session 2026-11-26)
- **Title**: `Compose: background profile dispatch nulls the request's HttpContext accessor and detaches the caller token (ADR-052)`
- **Body**:
  - `Services/Compose/ComposeProfileDispatcher.cs` `Dispatch` → `Task.Run(() => RunAsync(...))`. `RunAsync` sets `accessor.HttpContext = detachedContext` on a pool thread.
  - It does not call `ExecutionContext.SuppressFlow()` first, so the task inherits the save request's `HttpContextAccessor` AsyncLocal holder.
  - The accessor's setter clears the holder it finds (`holder.Context = null`) before it installs a new one.
  - Effect: while the save request is still running (`ComposeService.cs:2323` onward: memory capture, completion projection, response), every `IHttpContextAccessor.HttpContext` read on the request flow can return null. Whether it does depends on timing.
  - Failure scenario: a request-flow consumer of the accessor (for example telemetry or `ContextEventEmitter`) loses the caller and logs or behaves as if no caller were present.
  - Separately, the dispatcher copies the caller's `Authorization` header into a detached task that outlives the request. ADR-052 MUST NOT ("the caller's token must not leave the BFF request that holds it") forbids that.
  - Fix:
    - suppress flow before `Task.Run`;
    - decide the ADR-052 position: an exception, or move the profile onto an ADR-004 job with A5 impersonation once #988–#990 land.
  - Add a seam test that asserts the request's accessor is still populated after `Dispatch`.
- **Labels**: compose; Type=Bug. Owner: the active Compose project (r7) / uac-r2 for the ADR-052 position.

## D-034-1 — measure whether real Daily Briefings exceed the tool's 27 s budget (FR-14 start/status not used) → #3226
- **Status**: deferred (needs a dev deploy: tasks 039/043).
- **Draft issue** — title: `spaarke_get_briefing: measure briefing generation time; add FR-14 start/status if it exceeds 27 s`.
  Body: the tool enforces `McpLongRunning.Budget - 3 s` and answers `briefing.timeout` (504). The render is one synchronous composite call with no
  run record to poll, so FR-14's start/status pair was not built. At the dev deploy, record p50/p95 of `/render` for real users; if p95 is near
  27 s, either add a run-record-backed start/status or cache the last briefing. Labels: spaarkeai-mcp-bff-api-r2; Type=Task.

## D-034-2 — saved preferences not applied: the tool and the widget's first view ignore the user's saved Daily Digest preferences → #3232
- **Status**: deferred (draft issue for the main session to file; the owner may instead narrow FR-09 to "default preferences"). The default-window half (BFF 120 h vs
  Console 168 h) is FIXED in task 034's verifier fixes: a call with no arguments now gets the Console's defaults, 5 days and 168 hours. See `notes/034-decisions.md`, F4.
- **Draft issue** — title: `spaarke_get_briefing: apply the caller's saved Daily Digest preferences (windows and disabled channels)`.
  Body: the Console reads the caller's `sprk_userpreference` row (`sprk_preferencetype` = 100000002) in the browser with `Xrm.WebApi`, sends its `dueWithinDays` and
  `timeWindow` as the `/render` windows, and drops `disabledChannels` client-side (`DailyBriefingApp`). No BFF service reads that row as the caller (searched
  `src/server`: only a comment in `DailyBriefingCollector`, a name list in `OwnedChildWrite`, and a never-populated `PlaybookRunContext.UserPreferences`). So a user who saved
  a narrower window or disabled channels sees a different briefing from `spaarke_get_briefing` (and the widget's first view) than from the Console; the widget's Refresh
  sends only the component's default windows because its Preferences menu is hidden. Fix: one BFF reader of the caller's Daily Digest row (new surface: justify under
  CLAUDE.md section 11, and keep ADR-013/ADR-028: read as the caller, fail closed), used by the tool and applied through the same rules as the Console
  (windows, then `disabledChannels`). Labels: spaarkeai-mcp-bff-api-r2; Type=Bug.

## D-034-3 — one factory for the widget resources, and the Decisions widget adopting the shared `hostTheme` → #3227
- **Status**: deferred (the third and fourth copies land in tasks 035 and 040; r1's Decisions files were not touched).
- **Draft issue** — title: `Extract a widget-resource factory (DecisionsWidgetResource, BriefingWidgetResource, ...) and move the Decisions widget to shared/mcp/hostTheme`.
  Body: `BriefingWidgetResource` is a copy of `DecisionsWidgetResource` with different constants; `McpEndpoints.WarnIfWidgetMissing` already takes the widget as
  arguments. `src/client/widgets/spaarke-decisions/src/theme/hostTheme.ts` is identical to `src/client/widgets/shared/mcp/hostTheme.ts`. Do both once the r2
  widgets have all landed. Labels: spaarkeai-mcp-bff-api-r2; Type=Task.

## D-034-4 — the per-widget build/embed steps are repeated in five places → #3228
- **Status**: deferred (task 034 added the briefing to each; tasks 035 and 040 will add two more).
- **Draft issue** — title: `Build and verify every MCP widget from one list (publish, deploy, ci-tier1, run-conformance.sh, Deploy-BffApi.ps1)`.
  Body: `publish-bff-artifact.yml`, `deploy-bff-api.yml`, `ci-tier1-blocking.yml`, `run-conformance.sh` and `Deploy-BffApi.ps1` each list the widgets by hand, and the
  built-widget gate in publish names the test classes. A new widget that is added to the csproj but forgotten in one of them ships an artifact that serves an
  error for its resource (the startup warning is the only signal). Use one matrix or one script that reads the widget folders. Labels: spaarkeai-mcp-bff-api-r2; Type=Task.

## D-034-5 — the briefing widget is 1.13 MB (aim 1 MB) → #3229
- **Status**: deferred (under the 3 MB ceiling; `check-bundle-size.mjs` warns).
- **Draft issue** — title: `Briefing widget bundle: 1.13 MB against the 1 MB aim`.
  Body: most of the page is Fluent UI v9. Stubbing the email composer and document preview took it from 1.56 MB to 1.13 MB. Next candidates: import Fluent
  by deep path instead of the `@fluentui/react-components` barrel, and check which Console components the briefing renders that pull icons. Labels: spaarkeai-mcp-bff-api-r2; Type=Task.

## D-034-6 — POST /api/ai/daily-briefing/render's 503 for an unconfigured dispatch echoes `ex.Message` as the ProblemDetails detail (pre-existing, found in passing) → #3230
- **Status**: deferred (REST bytes are frozen by task 034's constraint; the tool does not copy it).
- **Draft issue** — title: `Daily briefing render 503 (dispatch unconfigured) returns the exception message in ProblemDetails.detail`.
  Body: `DailyBriefingEndpoints.RenderCoreAsync` (the `DailyBriefingDispatchUnconfiguredException` catch) returns `Results.Problem(statusCode: 503, detail: ex.Message)`. The
  message can name a Binding row or a configuration key. ADR-019: fixed text, exception logged. The `/email` leg has the same shape. Fix: fixed detail text with a stable code. Labels: spaarkeai-mcp-bff-api-r2; Type=Bug.

## D-034-7 — `BriefingNavigationHandler.openUrl` is not called by `DailyBriefingApp`
- **Status**: noted (task 010's seam declares it for the widget; the widget maps it to the host's open-link, but no component action uses it yet, so external links in a briefing are plain text). Wire it when a link affordance is added.
## D-031-1 — the retry-key lock is get-then-set, so two same-key calls can both create (spaarke_start_analysis; IdempotencyFilter has the same window) → #3231
- **Status**: deferred (draft issue for the main session to file). Found while writing task 031.
- **Title**: `Idempotency in-flight lock is not atomic (ITenantCache has no set-if-absent): concurrent same-key requests can both run`
- **Body**:
  `StartAnalysisTool.TryLockAsync` (task 031) and `IdempotencyFilter.TryAcquireLockAsync` both read the lock key and then write it.
  Two requests with the same key that arrive within one round-trip both see no lock, and both run. For `spaarke_start_analysis`
  this creates two `sprk_analysis` rows. For an `IdempotencyFilter` route it runs the handler twice. Failure scenario: a host
  retries a tool call on a short timeout while the first call is still in flight. Fix: add an atomic set-if-absent to
  `ITenantCache` (Redis `SET NX PX`, plus the in-memory test-cache equivalent) and use it in both places. That is new surface on a
  shared abstraction (§11), so it is out of 031's scope.
- **Labels**: spaarkeai-mcp-bff-api-r2; Type=Bug.

## D-031-2 — GET /api/ai/analysis/{id} on an uncached read answers Status "Completed", DocumentName = the analysis name and CompletedOn = ModifiedOn for EVERY row (found by task 031's reviews)
- **Status**: deferred (draft issue for the main session to file). REST bytes are a contract change, so task 031 did not touch the loader; `spaarke_get_analysis` drops the three fields instead (`GetAnalysisTool` remarks; `AnalysisToolsContractTests.Get_NeverRunAnalysis_OverTheRealLoader_DoesNotSayCompleted`).
- **Title**: `Analysis read (GET /api/ai/analysis/{id}) reports Completed, a document name and a completion time that were never recorded`
- **Body**:
  `AnalysisDocumentLoader.GetOrReloadFromDataverseAsync` (the "lite" load that `AnalysisOrchestrationService.GetAnalysisAsync` uses on a cache
  miss) builds the model with `Status = "Completed"` for every row, `DocumentName = record.Name` (the analysis's own name, not the document's),
  `CompletedOn = record.ModifiedOn` and no action, then caches that slim entry for 2 hours. A row that was created and never run (every row the
  MCP `spaarke_start_analysis` creates, and any REST create that is not followed by an execute) therefore reads as a finished analysis, with a
  completion time that moves whenever the row is edited. Nothing writes a status to `sprk_analysis` after the create (`statuscode` stays 1,
  Active), and the repo defines no Draft / In progress / Completed / Failed value for it, so the real state is not stored on the row today.
  Failure scenario: a user (or a model) reads "Completed" for an analysis that has not run and acts on it. Fix: decide where a run's state
  lives (a column the execute path writes, or the run store), read it in the loader, return null (not "Completed") when unknown; then restore
  `status`, `completedOn` and `documentName` in the tool. The precondition test
  `AnalysisToolsContractTests.Precondition_TheRestLoaderFabricatesCompleted_ForANeverRunRow` pins today's behaviour and is deleted with the fix.
- **Labels**: spaarkeai-mcp-bff-api-r2; Type=Bug.
