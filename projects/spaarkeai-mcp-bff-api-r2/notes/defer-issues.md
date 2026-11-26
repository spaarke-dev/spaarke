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

## From task 027 (2026-11-25)

### D-027-1: POST /api/ai/search and /api/ai/search/count return the raw exception message as the 500 `detail` (pre-existing, found in passing)

- **GitHub issue**: not filed by task 027 (sub-agent; the brief limits it to recording here). The main session files it with
  `/project-defer-issue-tracking` (project rule: notes AND issue).
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
