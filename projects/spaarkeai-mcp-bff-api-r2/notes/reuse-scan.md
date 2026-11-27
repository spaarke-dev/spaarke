# Reuse scan: spaarkeai-mcp-bff-api-r2 (design-to-spec Step 3)

> **Date:** 2026-11-25
> **Agent:** one `code-mapper`, model sonnet, effort low. It used 94,510 tokens and 25 tool calls, read `.claude/patterns/CAPABILITY-INDEX.md` first, and excluded `projects/`, `.claude/worktrees/` and `node_modules`.
> **Judged by:** the main session. The spec's §11 table cites this file.

Paths are under `src/server/api/Sprk.Bff.Api/` unless stated.

## Per capability

| Capability | Searched | Candidates | Verdict |
|---|---|---|---|
| Document search tool | `/api/ai/search`, `ISemanticSearchService`, the `Api/Mcp` files, `IDecisionCapability` | `Api/Ai/SemanticSearchEndpoints.cs:80` (handler `:111`; `ISemanticSearchService`, `IRetrievalAccessTrim`) | **Extend** `ISemanticSearchService`. No MCP tool exists yet |
| Record search tool | `RecordSearchService`, `IRecordSearchService`, `/api/ai/search/records` | `Api/Ai/RecordSearchEndpoints.cs:40`; `Services/Ai/RecordSearch/IRecordSearchService.cs` | **Extend** `IRecordSearchService` |
| Documents (list, get, preview) | `preview-url`, `/api/documents`, `IDocumentDataverseService` | `Api/FileAccessEndpoints.cs:65` (preview-url; also `/open-links :111`, `/view-url :122`); list is `GET /api/v1/documents`, `DataverseDocumentsEndpoints.cs:622` (from the legacy inventory) | **Extend** `IDocumentDataverseService` and the FileAccess logic |
| Playbook list | `IPlaybookService`, `/api/ai/playbooks`, `/api/ai/chat/playbooks` | `Api/Ai/PlaybookEndpoints.cs:90` and `:98`; `Api/Ai/ChatEndpoints.cs:323`; legacy `Api/Agent/AgentEndpoints.cs:62` (merge and de-duplicate logic, being removed) | **Extend** `IPlaybookService`. Copy the de-duplication logic from the agent before deleting it |
| Playbook run start and status | `IPlaybookOrchestrationService`, `PlaybookRunEndpoints`, `StatusUrl` | `Api/Ai/PlaybookRunEndpoints.cs:54` (execute streams), `:69`, `:108`; `Services/Ai/IPlaybookOrchestrationService.cs:23`; `Api/Filters/PlaybookAuthorizationFilter.cs:127` | **Extend**. Execute streams, so the start tool needs a non-streaming start. The filter's checks move into a shared method |
| Analysis | `/api/ai/analysis`, `IAnalysisOrchestrationService`, `RecordCreationService` | `Api/Ai/AnalysisEndpoints.cs:70` (create `:166`), `:137` (get `:707`) | **Extend** |
| Tasks (events) | `/api/v1/events`, `IEventDataverseService`, `EventEndpoints` | `Api/Events/EventEndpoints.cs:98`: list `:437`, get `:651`, create `:710`, complete `:1318`. The logic is in endpoint statics with 8 dependencies | **Extend by extraction** into an events capability (r1 B-1 style), keeping REST bytes unchanged |
| Portfolio | `PortfolioService`, `/api/workspace/portfolio` | `Api/Workspace/WorkspaceEndpoints.cs:32`; `Services/Workspace/PortfolioService.cs:154` (concrete class) | **Extend** |
| Briefing | `BriefingService`, `/api/workspace/briefing`, `daily-briefing` | `Api/Workspace/WorkspaceEndpoints.cs:58` (`BriefingService`); `Api/Ai/DailyBriefingEndpoints.cs:20` (AI Daily Briefing) | **Two candidates: owner question.** Answered: **AI Daily Briefing** |
| Matter AI summary | `HandleAiSummary`, `IBriefingAi`, Insights cache | `Api/Workspace/WorkspaceMatterEndpoints.cs:65` (hand-written SSE); `Api/Workspace/WorkspaceAiEndpoints.cs:41`; `Services/Ai/PublicContracts/IBriefingAi.cs:26`; `Services/Ai/Insights/InsightsPlaybookExecutionCache.cs`, `TopicRegistryTtlLookup.cs` | **Several candidates: owner question.** Answered: **Insights cache**, with start and status when it is missing or expired |
| Matter finance | `IFinanceSummaryService`, `/api/finance/matters` | `Api/Finance/FinanceEndpoints.cs:96` (`:594`); `Services/Finance/FinanceSummaryService.cs:13` | **Extend**. The authorization in the route filters moves to a shared method |
| Long-running pattern | `GetRunStatusAsync`, `IJobStatusService`, `IJobHandler` | `IPlaybookOrchestrationService.cs:68`; legacy `AgentEndpoints.cs:97` / `:461` / `:481` (`StatusUrl`, `StartedByOid` owner check, uniform 404); `Services/Office/IJobStatusService.cs`; `ServiceBusJobProcessor` | **Extend** the playbook run status, made durable in `sprk_playbookrun` (owner). Keep the `StartedByOid` rule. `IJobStatusService` is not used: it belongs to the Office module |
| MCP result mapping and tool helpers | `McpToolResults`, `McpToolAuthorization`, `DecisionToolCall`, `PlanAndLookupToolSupport`, `DecisionResultMapper`, `McpToolCatalog` | `Api/Mcp/McpToolResults.cs:20` (typed to `DecisionToolError`); `Api/Mcp/McpToolAuthorization.cs:33`; `Api/Mcp/LegalOperations/Decisions/DecisionToolCall.cs:22`; `PlanAndLookupToolSupport.cs:25`; `CompleteDecisionTool.cs:81` | **Extend**. Generalise `McpToolResults` and reuse the rest as they are |
| Widget MCP transport | `createWorklistService`, `createDecideLaneFetch`, `src/client/widgets` | `Spaarke.UI.Components/.../Worklist/worklistService.ts:350`; `src/client/widgets/spaarke-decisions/src/App.tsx:38`, `lane/decideLaneFetch.ts`, `mcp/mcpHost.ts`, `mcp/mcpFetch.ts` | **Extend**: generalise the decision-specific shim into a shared transport |
| Daily Briefing component | `Spaarke.DailyBriefing.Components`, `authenticatedFetch`, `Xrm` | `DailyBriefingApp.tsx:75` (direct `authenticatedFetch` at `:940`/`:949`; `Xrm.Navigation` at `:139`/`:525`/`:589`); `services/briefingService.ts:31` | **Extend**: add a data seam. Today it has none |
| Search results component | `SearchResultsWidget`, `SearchResultItem` | `Spaarke.AI.Widgets/src/outputs/output-widgets/SearchResultsWidget.tsx:142` (props only) | **Reuse as is** |
| Email compose component | `EmailComposer`, `EmailRecipients`, `useEmailComposeActions` | `Spaarke.UI.Components/src/components/EmailComposer/EmailComposer.tsx` (host injected; Xrm handlers in `createXrmEmailComposeHandlers.ts` / `useXrmEmailHostProps.ts`); `EmailRecipients.tsx:110` is display-only | **Extend**: add an MCP host implementation |
| Send email | `InquiryEmailSender`, `CommunicationService`, `/api/communications/send` | `Services/Signals/Actions/BudgetInquiryExecutor.cs:73` (`InquiryEmailSender` → `CommunicationService`); `Api/CommunicationEndpoints.cs:111`; `Services/Communication/CommunicationService.cs:174` | **Extend** the `CommunicationService` core (CAPABILITY-INDEX row 12) |

## Legacy removal blast radius (FR-01)

The search hit its time limit after counting 158 occurrences in 44 files, so this list is partial. The removal task re-runs the grep.

**Code (BFF)**
- `Infrastructure/DI/AgentModule.cs`.
- `Program.cs:232`.
- `Infrastructure/DI/EndpointMappingExtensions.cs:480` (and the `using` at `:5`).
- `Api/Filters/PlaybookAuthorizationFilter.cs:3`, `:576`, `:581`: a compile dependency on `AgentEndpoints` constants. Move them first.
- `Api/Filters/AiAuthorizationFilter.cs:3`.
- `Configuration/AgentTokenOptions.cs`, plus config comments in `appsettings.template.json:389`, `IdentityConfigurationValidator.cs:204`, `OrderedCredentialClientProvider.cs:511`, `DataverseUserClient.cs:33` and `AuthorizationModule.cs:337`/`:1032`.
- `Sprk.Bff.Api.csproj` (one hit).
- `Directory.Build.props:28`: a warning-suppression note for `AgentEndpoints`; the suppression may become removable.
- `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` and `IConfidentialClientProvider.cs`: likely comments.

**Infrastructure and CI**
- `infrastructure/bot-service/`.
- `infrastructure/ai-foundry/agents/bff-function-tools.yaml:14`.
- `.github/workflows/publish-copilot-agent-template.yml`.
- `scripts/copilot-agent/*`.
- `tests/scripts/CopilotAgentPackage.Tests.ps1`.

**Tests**
- `tests/unit/Sprk.Bff.Api.Tests/Api/Agent/` (3 files). Check that `HandoffUrlBuilder` is unused by MCP: `DecisionsTextSummary.cs:11` mentions it.
- ArchTests:
  - `RouteAuthorizationGuardTests*.cs` (ledger);
  - `SessionOwnershipGuardTests.cs`;
  - `ADR013_AiBoundaryTests.cs`;
  - `TenantIsolation/I6_ObAppRegDerivationTests.cs`;
  - `SpeAppOnlyContainerGuardTests.cs`.
- Contract tests:
  - `ChatAgentRouteProofTests.cs`;
  - `PlaybookRouteAuthorizationContractTests.cs`;
  - `ChatContextAuthorizationContractTests.cs`;
  - `BusinessSliceDeterminismContractTests.cs`;
  - `Eval/P2LoopInjectionEvalSuiteTests.cs`.
- Seam and auth tests:
  - `ConfidentialClientMigrationSeamTests.cs`;
  - `ConfidentialClientSharingSeamTests.cs`;
  - `tests/integration/auth/Mcp/McpAuthTests.cs`;
  - ExternalAccess auth tests;
  - `MatterSecureFlagChatRefusalTests.cs`;
  - the data-mutation tests (may be comments only);
  - `DataverseToolNameFreezeTests.cs`;
  - `DataverseCreateRecordHandlerTests.cs`.

**Docs and skills**
- `docs/architecture/M365-COPILOT-INTEGRATION-ARCHITECTURE.md`: already deleted by PR #3147 on master.
- `docs/guides/M365-COPILOT-DEPLOYMENT-GUIDE.md`, `M365-COPILOT-ADMIN-GUIDE.md`, `DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md`.
- `docs/architecture/sdap-component-interactions.md`, `sdap-bff-api-patterns.md`, `AI-ARCHITECTURE.md`, `ai-architecture-playbook-runtime.md`.
- `docs/assessments/` (4 files).
- `.claude/adr/ADR-028-spaarke-auth-architecture.md:810`.
- Skills: `declarative-agent`, `mcp-tool-handler`, `adr-check` references.

## Plan-level duplication check

- **No capability adds a new service where an existing one fits.** Every tool wraps the service its REST route uses.
- **The three new shared surfaces are all generalisations of existing code:**
  - the shared widget transport (from `decideLaneFetch`);
  - the generalised `McpToolResults`;
  - the events capability (extracted from `EventEndpoints`).
- **Run persistence uses tables that already exist** (`sprk_playbookrun`, `sprk_playbooknoderun`) and adds none.
- **Two duplication risks were avoided by owner choice:**
  - a second matter-summary store: Insights cache chosen;
  - a second briefing component: the Daily Briefing component reused with a seam.

## Not covered by the scan

- `CommunicationService.SendAsync`'s public signature.
- `SearchSelectWizardWidget`'s data path.
- The body of the csproj hit.
- The `docs/INDEX.md` and CI path-filter entries.
- DI names for `PortfolioService` and `BriefingService`.

project-pipeline Step 2 covers these in its full discovery.
