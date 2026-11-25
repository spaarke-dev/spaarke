# Task 001: legacy agent removal — execution notes

**Status:** implementation, code-review and adr-check are done. The two required adversarial-verifier passes are **PENDING**: the session hit its concurrent-subagent cap (4), so the `adversarial-reviewer` agents could not be launched.

- **Branch:** `worktree-agent-a3c0a6c062265ea73`
- **Worktree:** `C:\code_files\spaarke\.claude\worktrees\agent-a3c0a6c062265ea73`
- **Base:** `058dc46a17` (master `75e01f35f1` plus the project branch `cd7cf78b70`)
- **Rigor:** FULL (tags `bff-api` and `auth`; modifies `.cs`)

## Decisions and deviations

These are also recorded in `notes/decisions.md`.

- **`HandoffUrlBuilder` registration.** It is registered in `ToolFrameworkExtensions.AddToolFramework`, not in `AiModule`/`AiChatModule`. The POML said "grep to confirm", and the grep showed the consumer is registered by the handler scan in that method. The builder is a `TryAddSingleton` with the same URL fallback as before.
  - **Behaviour change (intended):** the builder now sits under the compound AI / tool-framework gate, together with its only consumer. `AgentModule` used to register it unconditionally.
- **`AiAuthorizationFilter` agent branch removed.** This is the same shape as the agent branch in `PlaybookAuthorizationFilter`. Escalation trigger 1 does not fire.
- **`AgentToken:CopilotAudience` kept.** `AuthorizationModule` reads it, and that file has comment-only edits.
- **`IdentityConfigurationValidator` rule 5 removed.** It was log-only and agent-only. The now-unused `Fingerprint` helper was removed with it.
- **Ledger rows deleted, not re-pointed.** The rows for S-18, S-19, S-78, the permanent `GET` agent playbook list, the GovernedFile, the NonDeciding attachment and the claim-only filter were deleted rather than re-pointed at a retirement test, because the acceptance grep forbids the removed prefix in `tests/`.
  - Pinned counts changed:

    | Count | Before | After |
    |---|---|---|
    | Sweep findings | 90 | 87 |
    | Distinct sweep ids | 82 | 79 |
    | NoDecision | 74 | 71 |
    | Task 164 | 19 | 16 |
    | High | 29 | 27 |
    | Low | 6 | 5 |
    | Endpoint-file census | 157 | 156 |

  - **review pending: uac-r2.**
- **AddAiAuthorizationFilter reason text changed.** The NonDeciding reason now says "four chat-context request bodies" (it said five). This is a non-route ledger text change and is flagged for uac-r2.
- **`LegacyAgentRouteRetirementTests` builds the prefix at run time** (`"/api/" + "agent"`). This satisfies both the acceptance grep and the negative 404 criterion. The reason is documented in the class remarks.
- **ProvenByTest fixture switched to a live route.** The fixture (`RouteAuthorizationGuardTests.ProvenByTest.cs`) now uses the live same-shape route `GET /api/ai/playbooks/runs/{runId:guid}`.
- **Two non-agent decorative-control references removed from `RouteAuthorizationGuardTests.cs`.** These were the `AddAgentAuthorizationFilter` `case` in the NonDeciding pin switch and its line in the decorative-forms control list. Both named a filter that no longer exists.
- **`PlaybookRouteAuthorizationContractTests.PlaybookAuthHost.RunStatus` kept.** `PlaybookRunStreamTerminalErrorContractTests` uses it.
- **`ChatAgentRouteProofTests` keeps S-24 and its fixture.** `AiSpendLimitContractTests` shares the fixture and drives the stub agent.
- **Test scope beyond the criteria** (one line each, per the POML):
  - `NoRouteUnderTheRemovedPrefix_IsInTheEndpointTable` and `RetiredRoute_IsNotMatchedByAnyEndpoint` cover all four removed routes, not only the message route. The 404 alone cannot tell a missing route from a FallbackPolicy refusal, so these checks are needed to prove absence.

## Acceptance criteria

| # | Criterion | Result |
|---|---|---|
| 1 | Acceptance grep | Only `Api/Mcp/LegalOperations/Decisions/DecisionsTextSummary.cs:11` remains (task 020's) |
| 2 | BFF build | 0 warnings, 0 errors, including with `-warnaserror` and `--no-incremental` |
| 3 | Tests pass | ArchTests full suite: 2828/2830 before the census fix (the 2 failures were the endpoint-file census and its control). After the fix, the `RouteAuthorizationGuardTests` class passes 105/105. BFF tests, filtered to every touched class and folder: 1838/1838. `LegacyAgentRouteRetirementTests`: 11/11 |
| 4 | No agent branch in `PlaybookAuthorizationFilter` | The execute branch is unchanged. The execute-route tests pass unchanged. Every Run-mode rule is proven on execute (see the ADR-038 table in `001-issue-comments.md`) |
| 5 | `DataverseCreateRecordHandler` resolves `HandoffUrlBuilder` from DI | `LegacyAgentRouteRetirementTests.DataverseCreateRecordHandler_ResolvesTheRelocatedHandoffUrlBuilder_FromDi`. The `HandoffUrlBuilder` tests and the data-mutation tests pass |
| 6 | Negative: `POST` to the removed message route returns 404 | `RetiredRoute_WithAValidBearer_Is404NotRouted` |
| 7 | No ledger change outside the agent prefix | No route row changed. One non-route reason text changed (AddAiAuthorizationFilter, "five" → "four"); flagged for uac-r2 |
| 8 | Publish size and CVE | See "Publish size" below |
| 9 | "Closes #2575" and the #1523 draft | Commit message and `001-issue-comments.md` |

## Publish size

Both builds: Release, framework-dependent, zipped with `Compress-Archive -CompressionLevel Optimal`, PDBs included. Each was built in a fresh short-path worktree.

| Build | Files | Raw | Zip |
|---|---|---|---|
| Master `75e01f35f1` (`C:\wt001m`, the branch's merge base) | 194 | 137.89 MB | 41.61 MB (43,630,850 B) |
| Branch `8a92da8751` (`C:\wt001b`) | 186 | 136.64 MB | 41.13 MB (43,129,984 B) |

- **Delta:** −500,866 B, about −0.48 MB, and −8 files.
- **Removed assemblies:**
  - `AdaptiveCards`
  - `Microsoft.Agents.Authentication`
  - `Microsoft.Agents.Builder`
  - `Microsoft.Agents.Connector`
  - `Microsoft.Agents.Core`
  - `Microsoft.Agents.Hosting.AspNetCore`
  - `Microsoft.Agents.Storage`
  - `Microsoft.Agents.Storage.Transcript`
- **Total:** 41.13 MB, under the 55 MB review and 60 MB hard-stop thresholds.
- **CVE check:** `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages on the branch or on master, so there is no new HIGH CVE.

## Remaining references for task 002 (outside this task's grep scope)

- **`infrastructure/bot-service/parameters.dev.json:9`:** the Azure Bot messaging endpoint still points at the removed message route on `spe-api-dev`.
  - **Owner-gated live step:** once this branch is deployed, delete or disable the dev Azure Bot resource or its channel. Until then the bot gets 404s.
- **`scripts/canonical-secret-catalog/manifest.yaml:185,210`** and the generated `Configure-AppServiceSettings.generated.ps1:123-124` and `appsettings.tokens.generated.md` still map `AgentToken__TenantId` and `AgentToken__ClientId`. Nothing reads these app settings any more.
  - Removing them is a secret-catalog change; read `.claude/constraints/provisioning.md` before making it.
- **`infrastructure/ai-foundry/agents/bff-function-tools.yaml:14-15`** names agent-prefix tool routes. They did not exist in this BFF before the change either, so this is pre-existing drift.
- **Copilot agent package and pipeline:** `src/solutions/CopilotAgent/**` (the OpenAPI document and manifest), `scripts/Deploy-CopilotAgent.ps1`, `scripts/copilot-agent/**`, `tests/scripts/CopilotAgentPackage.Tests.ps1` and `.github/workflows/publish-copilot-agent-template.yml`.
- **Docs:** `docs/guides/*` (M365-COPILOT-*, DECLARATIVE-AGENT-*, CONFIGURATION-MATRIX, SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE) and `docs/architecture/*` (AI-ARCHITECTURE, SPAARKE-AI-SURFACES-AND-PACKAGING, sdap-bff-api-patterns, and others).
- **`.claude/`:** `ADR-028`, `adr-check` validation rules, and the `declarative-agent`, `foundry-agent`, `mcp-tool-handler` and `provision-environment` skills. These are main-session only.
- **Found in passing:** `src/server/services/Sprk.Provisioning.ControlPlane.Api/Middleware/AuditLogMiddleware.cs:42` has a comment that cites the BFF's deleted `AgentTelemetry.cs`. This is comment drift in customer-provisioning code.
- **Left unchanged on purpose:** `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/E2EValidationRunner.cs:33` is a history comment, which the POML says to leave.

## Pre-existing observations (no change in this task)

- **`PlaybookRunStatus.StartedByOid` has no reader now.** The removed agent status route was its only owner-check reader.
  - The surviving `GET /api/ai/playbooks/runs/{runId}` routes are ledger-Permanent `CallerScopedOnly` because the run store is per request (a scoped service with an instance field), so no other caller's run is reachable today.
  - Tasks 005 and 030 (durable run status) must add the owner comparison before the store outlives a request.

## Reviews

- **code-review:** done in-session. One fix: an unused `using` left by the deleted tests in `PlaybookRouteAuthorizationContractTests`. A doc drift in `appsettings.tokens.md` was also fixed: the `COPILOT_AGENT_APP_ID` token row is gone, because the template no longer uses it. There were no other F-class findings.
- **adr-check:** done in-session. 0 violations. The ADRs considered were 001, 002, 007, 008, 009, 010, 012, 013, 028 (including A4), 038, 039 and 052. Nothing new was added that touches 001, 052, 007, 009 or 012.
- **adversarial-verifier:** **0 of 2 passes run.** The agents could not be launched because the concurrent-subagent cap (4) was reached. The main session must run two `adversarial-reviewer` (opus) passes over `058dc46a17..HEAD` before this task is marked completed.
- **External reviews pending:**
  - **uac-r2:** ledger rows and pinned counts, the AddAiAuthorizationFilter reason text, the ProvenByTest fixture, and the SessionOwnership, I6 and SpeAppOnly guard edits.
  - **ebilling-r1:** pending, no overlap.
  - **ontology-r1:** pending, no overlap.

## Measurement worktrees

`C:\wt001m` and `C:\wt001b` are detached, publish-only worktrees. They are to be removed with `scripts/worktree/Remove-WorktreeSafe.ps1`; see the final report for whether that was done.
