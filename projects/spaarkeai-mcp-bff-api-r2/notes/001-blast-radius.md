# Task 001: legacy agent blast radius (re-run 2026-11-25)

This is the step 1 grep, run on the task branch at `058dc46a17` (master `75e01f35f1` plus project branch `cd7cf78b70`):

```
git grep -n -i "api/agent\|AgentEndpoints\|AgentModule\|AgentTokenService\|AgentTokenOptions\|Api\.Agent\|HandoffUrlBuilder" -- src/server tests ':!**/bin/**' ':!**/obj/**'
```

It returned 238 hits in 65 files.

## Findings beyond notes/reuse-scan.md

- **`Api/Filters/AiAuthorizationFilter.cs` uses `AgentMessageRequest` at run time, not only in a cref.** It appears in `ChatContextRequestTypes` (`:106`), in a `case` of the chat-context switch (`:430`), and in the agent-only helper `ReadResumedAgentSessionAsync`.
  - This is an agent-only branch keyed on the agent route's DTO, the same shape as the `PlaybookAuthorizationFilter` branch.
  - It is removed together with the route. No other chat DTO's branch changes.
- **Comment-only mentions of `HandoffUrlBuilder`** stay valid because the class keeps its name:
  - `Services/Ai/CompletionEngine.cs`;
  - `Services/Ai/PublicContracts/OutcomeCard.cs`;
  - `tests/integration/contract/Api/Ai/OutcomeCardContractTests.cs`.
- **`AgentToken:CopilotAudience` is not agent-only, so it stays.** `Infrastructure/DI/AuthorizationModule.cs:129` reads it for the JWT audience merge, and several auth tests set it. The ADR-028 constraint says AuthorizationModule's run-time behaviour does not change. Only the agent-only keys of the `AgentToken` section are removed from `appsettings.template.json`.
- **The `Microsoft.Agents.Hosting.AspNetCore` package** (csproj) is consumed only by `Api/Agent/SpaarkeAgentHandler.cs`.
- **Escalation trigger 1 does not fire.** Outside `Api/Agent/**`, code depends at run time on an Agent* type only through:
  - the two `AgentEndpoints` constants, which are used only in the agent branch;
  - the agent branches of the two filters;
  - `HandoffUrlBuilder`'s consumers.

## Hits per file

| Hits | File |
|---:|---|
| 1 | `src/server/api/Sprk.Bff.Api/Api/Agent/AdaptiveCardFormatterService.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Api/Agent/AgentAuthorizationFilter.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Api/Agent/AgentConfigurationService.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Api/Agent/AgentConversationService.cs` |
| 19 | `src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs` |
| 5 | `src/server/api/Sprk.Bff.Api/Api/Agent/AgentErrorHandler.cs` |
| 5 | `src/server/api/Sprk.Bff.Api/Api/Agent/AgentModels.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Api/Agent/AgentTelemetry.cs` |
| 9 | `src/server/api/Sprk.Bff.Api/Api/Agent/AgentTokenService.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Api/Agent/EmailDraftService.cs` |
| 3 | `src/server/api/Sprk.Bff.Api/Api/Agent/HandoffUrlBuilder.cs` |
| 9 | `src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookInvocationService.cs` |
| 5 | `src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookStatusEndpoints.cs` |
| 5 | `src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs` |
| 2 | `src/server/api/Sprk.Bff.Api/Api/Filters/AiAuthorizationFilter.cs` |
| 6 | `src/server/api/Sprk.Bff.Api/Api/Filters/PlaybookAuthorizationFilter.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Api/Mcp/LegalOperations/Decisions/DecisionsTextSummary.cs` |
| 5 | `src/server/api/Sprk.Bff.Api/Configuration/AgentTokenOptions.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Configuration/IdentityConfigurationValidator.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/OrderedCredentialClientProvider.cs` |
| 10 | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs` |
| 2 | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs` |
| 3 | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/DataverseUserClient.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Program.cs` |
| 2 | `src/server/api/Sprk.Bff.Api/Services/Ai/CompletionEngine.cs` |
| 7 | `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookOrchestrationService.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookParameterPolicy.cs` |
| 8 | `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/OutcomeCard.cs` |
| 1 | `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` |
| 1 | `src/server/api/Sprk.Bff.Api/appsettings.template.json` |
| 1 | `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/E2EValidationRunner.cs` |
| 1 | `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` |
| 1 | `src/server/shared/Spaarke.Dataverse/IConfidentialClientProvider.cs` |
| 1 | `tests/Spaarke.ArchTests/ADR013_AiBoundaryTests.cs` |
| 9 | `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` |
| 8 | `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.ProvenByTest.cs` |
| 1 | `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` |
| 2 | `tests/Spaarke.ArchTests/SessionOwnershipGuardTests.cs` |
| 2 | `tests/Spaarke.ArchTests/TenantIsolation/I6_ObAppRegDerivationTests.cs` |
| 1 | `tests/Spaarke.ArchTests/TenantIsolation/SpeAppOnlyContainerGuardTests.cs` |
| 3 | `tests/integration/contract/Api/Ai/BusinessSliceDeterminismContractTests.cs` |
| 6 | `tests/integration/contract/Api/Ai/ChatAgentRouteProofTests.cs` |
| 6 | `tests/integration/contract/Api/Ai/ChatContextAuthorizationContractTests.cs` |
| 3 | `tests/integration/contract/Api/Ai/OutcomeCardContractTests.cs` |
| 20 | `tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs` |
| 1 | `tests/integration/contract/Eval/P2LoopInjectionEvalSuiteTests.cs` |
| 1 | `tests/integration/data-mutation/ExternalAccess/AssignedAccessCreateGrantJobTests.cs` |
| 1 | `tests/integration/data-mutation/ExternalAccess/AssignedAccessGrantorTests.cs` |
| 3 | `tests/integration/data-mutation/ExternalAccess/AssignedAccessWriterTriggerTests.cs` |
| 8 | `tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceWriterTests.cs` |
| 2 | `tests/integration/data-mutation/RecordOwnership/ChatCreatedChildStampTests.cs` |
| 2 | `tests/integration/data-mutation/RecordOwnership/ChatPreSuspendServiceRefusalTests.cs` |
| 2 | `tests/integration/data-mutation/RecordOwnership/ChatServerWrittenTableBlockTests.cs` |
| 3 | `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.SearchIndexCreateRoute.cs` |
| 2 | `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.cs` |
| 5 | `tests/integration/seam/Auth/ConfidentialClientMigrationSeamTests.cs` |
| 2 | `tests/integration/seam/Auth/ConfidentialClientSharingSeamTests.cs` |
| 2 | `tests/integration/seam/ChildRecords/MatterSecureFlagChatRefusalTests.cs` |
| 2 | `tests/unit/Sprk.Bff.Api.Tests/Api/Agent/AgentConfigurationServiceTests.cs` |
| 2 | `tests/unit/Sprk.Bff.Api.Tests/Api/Agent/AgentConversationServiceTests.cs` |
| 7 | `tests/unit/Sprk.Bff.Api.Tests/Api/Agent/HandoffUrlBuilderTests.cs` |
| 6 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseCreateRecordHandlerTests.cs` |
| 2 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseToolNameFreezeTests.cs` |

## Raw output (lines cut at 240 characters)

```
src/server/api/Sprk.Bff.Api/Api/Agent/AdaptiveCardFormatterService.cs:4:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/AgentAuthorizationFilter.cs:4:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/AgentConfigurationService.cs:10:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/AgentConversationService.cs:7:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:11:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:23:public static class AgentEndpoints
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:33:    /// Called from Program.cs: <c>app.MapAgentEndpoints();</c>
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:35:    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:37:        var group = app.MapGroup("/api/agent")
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:41:        // POST /api/agent/message — receive agent message, route to existing services
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:61:        // GET /api/agent/playbooks — list available playbooks
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:73:        // POST /api/agent/run-playbook — execute a playbook
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:96:        // GET /api/agent/playbooks/status/{jobId} — poll playbook execution status
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:117:    /// POST /api/agent/message
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:129:        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Agent.AgentEndpoints");
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:285:            // agent-gateway entry point. AgentEndpoints returns JSON (not SSE) so a 503
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:304:    /// GET /api/agent/playbooks
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:313:        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Agent.AgentEndpoints");
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:392:    /// POST /api/agent/run-playbook
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:402:        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Agent.AgentEndpoints");
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:461:                StatusUrl = $"/api/agent/playbooks/status/{runId}"
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:478:    /// GET /api/agent/playbooks/status/{jobId}
src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs:488:        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Agent.AgentEndpoints");
src/server/api/Sprk.Bff.Api/Api/Agent/AgentErrorHandler.cs:4:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/AgentErrorHandler.cs:25:    private readonly HandoffUrlBuilder _handoffUrlBuilder;
src/server/api/Sprk.Bff.Api/Api/Agent/AgentErrorHandler.cs:48:        HandoffUrlBuilder handoffUrlBuilder)
src/server/api/Sprk.Bff.Api/Api/Agent/AgentErrorHandler.cs:51:        _handoffUrlBuilder = handoffUrlBuilder ?? throw new ArgumentNullException(nameof(handoffUrlBuilder));
src/server/api/Sprk.Bff.Api/Api/Agent/AgentErrorHandler.cs:103:        var workspaceUrl = _handoffUrlBuilder.BuildAnalysisWorkspaceUrl(
src/server/api/Sprk.Bff.Api/Api/Agent/AgentModels.cs:3:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/AgentModels.cs:6:/// Request model for POST /api/agent/message.
src/server/api/Sprk.Bff.Api/Api/Agent/AgentModels.cs:37:/// Response model for POST /api/agent/message.
src/server/api/Sprk.Bff.Api/Api/Agent/AgentModels.cs:62:/// Request model for POST /api/agent/run-playbook.
src/server/api/Sprk.Bff.Api/Api/Agent/AgentModels.cs:85:/// Response model for GET /api/agent/playbooks/status/{jobId}.
src/server/api/Sprk.Bff.Api/Api/Agent/AgentTelemetry.cs:3:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/AgentTokenService.cs:9:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/AgentTokenService.cs:15:///   M365 Copilot → [Agent Bearer Token] → BFF AgentTokenService
src/server/api/Sprk.Bff.Api/Api/Agent/AgentTokenService.cs:25:public sealed class AgentTokenService
src/server/api/Sprk.Bff.Api/Api/Agent/AgentTokenService.cs:50:    private readonly ILogger<AgentTokenService> _logger;
src/server/api/Sprk.Bff.Api/Api/Agent/AgentTokenService.cs:51:    private readonly AgentTokenOptions _options;
src/server/api/Sprk.Bff.Api/Api/Agent/AgentTokenService.cs:53:    public AgentTokenService(
src/server/api/Sprk.Bff.Api/Api/Agent/AgentTokenService.cs:55:        IOptions<AgentTokenOptions> options,
src/server/api/Sprk.Bff.Api/Api/Agent/AgentTokenService.cs:56:        ILogger<AgentTokenService> logger,
src/server/api/Sprk.Bff.Api/Api/Agent/AgentTokenService.cs:97:                "AgentTokenService requires an OrderedCredentialClientProvider for the OBO exchange. "
src/server/api/Sprk.Bff.Api/Api/Agent/EmailDraftService.cs:3:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/HandoffUrlBuilder.cs:5:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/HandoffUrlBuilder.cs:11:public sealed class HandoffUrlBuilder
src/server/api/Sprk.Bff.Api/Api/Agent/HandoffUrlBuilder.cs:15:    public HandoffUrlBuilder(string dataverseBaseUrl)
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookInvocationService.cs:6:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookInvocationService.cs:17:///   - <see cref="HandoffUrlBuilder"/> for deep-link generation
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookInvocationService.cs:38:    private readonly HandoffUrlBuilder _handoffUrlBuilder;
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookInvocationService.cs:64:        HandoffUrlBuilder handoffUrlBuilder,
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookInvocationService.cs:68:        _handoffUrlBuilder = handoffUrlBuilder;
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookInvocationService.cs:271:        return _handoffUrlBuilder.BuildAnalysisWorkspaceUrl(
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookInvocationService.cs:352:    /// The job ID is returned for status polling via GET /api/agent/playbooks/status/{jobId}.
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookInvocationService.cs:483:    /// Status can be polled via GET /api/agent/playbooks/status/{jobId}.
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookInvocationService.cs:512:    /// Used for status polling via GET /api/agent/playbooks/status/{jobId}.
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookStatusEndpoints.cs:9:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookStatusEndpoints.cs:29:    private readonly HandoffUrlBuilder _handoffUrlBuilder;
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookStatusEndpoints.cs:39:        HandoffUrlBuilder handoffUrlBuilder)
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookStatusEndpoints.cs:43:        _handoffUrlBuilder = handoffUrlBuilder;
src/server/api/Sprk.Bff.Api/Api/Agent/PlaybookStatusEndpoints.cs:120:            deepLinkUrl = _handoffUrlBuilder.BuildAnalysisWorkspaceUrl(
src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs:6:namespace Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs:25:    // TODO: Inject AgentTokenService when MCI-014 is implemented — provides OBO token exchange for agent-to-BFF calls.
src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs:29:    // TODO: Inject HandoffUrlBuilder for generating deep-link URLs in Adaptive Card responses.
src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs:165:            //   "view-document"   → Generate HandoffUrlBuilder deep link
src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs:167:            //   "open-workspace"  → Generate workspace deep link via HandoffUrlBuilder
src/server/api/Sprk.Bff.Api/Api/Filters/AiAuthorizationFilter.cs:3:using Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Filters/AiAuthorizationFilter.cs:60:/// <c>POST /api/agent/message</c> (<see cref="AgentMessageRequest"/>) — every record the session is, or is about to be,
src/server/api/Sprk.Bff.Api/Api/Filters/PlaybookAuthorizationFilter.cs:3:using Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Api/Filters/PlaybookAuthorizationFilter.cs:48:    /// <c>POST /api/agent/run-playbook</c> (<see cref="PlaybookAuthorizationMode.Run"/>): the shared playbook-parameter
src/server/api/Sprk.Bff.Api/Api/Filters/PlaybookAuthorizationFilter.cs:127:    /// <c>POST /api/ai/playbooks/{id:guid}/execute</c> and <c>POST /api/agent/run-playbook</c> (task 164): the
src/server/api/Sprk.Bff.Api/Api/Filters/PlaybookAuthorizationFilter.cs:541:    /// Run (<c>POST /api/ai/playbooks/{id:guid}/execute</c>, <c>POST /api/agent/run-playbook</c>): the ids come from
src/server/api/Sprk.Bff.Api/Api/Filters/PlaybookAuthorizationFilter.cs:576:                return BadRequestProblem(AgentEndpoints.PlaybookIdRequiredDetail);
src/server/api/Sprk.Bff.Api/Api/Filters/PlaybookAuthorizationFilter.cs:581:                return BadRequestProblem(AgentEndpoints.DocumentIdRequiredDetail);
src/server/api/Sprk.Bff.Api/Api/Mcp/LegalOperations/Decisions/DecisionsTextSummary.cs:11://   Existing: the Console renders the lane in the browser; HandoffUrlBuilder (Api/Agent, the April surface) and
src/server/api/Sprk.Bff.Api/Configuration/AgentTokenOptions.cs:15:public class AgentTokenOptions
src/server/api/Sprk.Bff.Api/Configuration/AgentTokenOptions.cs:41:    /// <c>AgentTokenService.cs:105</c> — so relaxing the attribute changes what is <i>required</i>, not
src/server/api/Sprk.Bff.Api/Configuration/AgentTokenOptions.cs:81:/// Validates AgentTokenOptions with cross-property rules.
src/server/api/Sprk.Bff.Api/Configuration/AgentTokenOptions.cs:84:public class AgentTokenOptionsValidator : IValidateOptions<AgentTokenOptions>
src/server/api/Sprk.Bff.Api/Configuration/AgentTokenOptions.cs:86:    public ValidateOptionsResult Validate(string? name, AgentTokenOptions options)
src/server/api/Sprk.Bff.Api/Configuration/IdentityConfigurationValidator.cs:204:        // Before task 022, AgentTokenService built its own confidential client from
src/server/api/Sprk.Bff.Api/Infrastructure/Auth/OrderedCredentialClientProvider.cs:511:    /// into this precedence could change which secret <c>AgentTokenService</c> presents. Reconciling it
src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs:2:using Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs:12:public static class AgentModule
src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs:14:    public static IServiceCollection AddAgentModule(
src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs:21:        services.AddOptions<AgentTokenOptions>()
src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs:22:            .Bind(configuration.GetSection(AgentTokenOptions.SectionName));
src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs:23:        services.AddSingleton<IValidateOptions<AgentTokenOptions>, AgentTokenOptionsValidator>();
src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs:24:        // FR-A2 (auth-v4 task 011): SINGLETON, was Scoped. AgentTokenService builds an MSAL
src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs:29:        // IOptions<AgentTokenOptions>, ILogger<>) and HttpContext arrives as a method parameter
src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs:31:        services.AddSingleton<AgentTokenService>();
src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs:49:        services.AddSingleton(new HandoffUrlBuilder(dataverseUrl));
src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs:337:        // and AgentTokenService alike. This module is where a reader looks for "how does the BFF
src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs:1032:        // per-class CCA caches (DataverseAccessDataSource, DataverseUserClient, AgentTokenService) onto
src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs:5:using Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs:479:        // M365 Copilot Agent gateway endpoints (/api/agent/*)
src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs:480:        app.MapAgentEndpoints();
src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/DataverseUserClient.cs:33:/// (<c>AgentTokenService</c>, <c>DataverseAccessDataSource</c>): primary keys
src/server/api/Sprk.Bff.Api/Program.cs:232:builder.Services.AddAgentModule(builder.Configuration);
src/server/api/Sprk.Bff.Api/Services/Ai/CompletionEngine.cs:14:/// record link (<c>HandoffUrlBuilder</c> / the entityrecord deep-link composer) — from the gated
src/server/api/Sprk.Bff.Api/Services/Ai/CompletionEngine.cs:27:/// <c>UserSummary</c>/<c>HandoffUrlBuilder</c> behave.
src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs:4:using Sprk.Bff.Api.Api.Agent;
src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs:119:    /// composing it via <see cref="HandoffUrlBuilder"/> keeps the URL server-trusted (D-F0(b): the
src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs:124:    /// <see cref="HandoffUrlBuilder.BuildDocumentUploadWizardUrl(Guid?)"/>).
src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs:128:        var url = _handoffUrlBuilder.BuildDocumentUploadWizardUrl(matterId);
src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs:229:    private readonly HandoffUrlBuilder _handoffUrlBuilder;
src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs:243:        HandoffUrlBuilder handoffUrlBuilder,
src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs:252:        _handoffUrlBuilder = handoffUrlBuilder ?? throw new ArgumentNullException(nameof(handoffUrlBuilder));
src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookOrchestrationService.cs:724:    /// comparison on <c>GET /api/agent/playbooks/status/{jobId}</c> (unified-access-control-r2 task 164, sweep #78).
src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookParameterPolicy.cs:18:/// applies before a run starts (<c>POST /api/ai/playbooks/{id}/execute</c> and <c>POST /api/agent/run-playbook</c> via
src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/OutcomeCard.cs:12:/// outcome: (1) a SERVER-composed record/deep link (<c>Api/Agent/HandoffUrlBuilder.cs</c> and the
src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/OutcomeCard.cs:19:/// <c>UserSummary</c>/<c>HandoffUrlBuilder</c> behave (task-011 escalation boundary).
src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/OutcomeCard.cs:50:/// and a <c>HandoffUrlBuilder</c> output → <see cref="OutcomeCardLink.ServerComposed"/>) is
src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/OutcomeCard.cs:94:    /// Server-composed record/deep link (from <c>HandoffUrlBuilder</c> or the entityrecord deep-link
src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/OutcomeCard.cs:127:    /// <paramref name="link"/> from a <c>HandoffUrlBuilder</c> output.
src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/OutcomeCard.cs:156:                "OutcomeCard.Link must be SERVER-composed (HandoffUrlBuilder / entityrecord deep-link " +
src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/OutcomeCard.cs:265:    /// True when the URL was composed by trusted server code (HandoffUrlBuilder / deep-link
src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/OutcomeCard.cs:273:    /// reference producer passes a <c>HandoffUrlBuilder</c> output or an entityrecord deep-link here.
src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj:71:                                          Api/Agent/SpaarkeAgentHandler.cs.
src/server/api/Sprk.Bff.Api/appsettings.template.json:389:    "_ClientSecret_comment": "AgentToken:ClientSecret REMOVED 2026-08-24 (spaarke-auth-v4 task 033, ADR-028 A4). AgentTokenService stopped reading it at task 022 — it now takes its
src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/E2EValidationRunner.cs:33:// REMOVED BY TASK 230b — the four G-8 Batch 11 "sample-workload" checks (POST /api/agent/message, POST
src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs:28:    /// (<c>GraphClientFactory</c>, <c>DataverseUserClient</c>, <c>AgentTokenService</c>) inject the
src/server/shared/Spaarke.Dataverse/IConfidentialClientProvider.cs:32:/// <c>GraphClientFactory</c>, <c>DataverseUserClient</c>, <c>AgentTokenService</c> — can inject the
tests/Spaarke.ArchTests/ADR013_AiBoundaryTests.cs:64:        "Sprk.Bff.Api.Api.Agent.AgentEndpoints",                          // M365 Copilot agent gateway (playbook-discovery pattern)
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs:173:        new GovernedFile("Api/Agent/AgentEndpoints.cs", Scope.RouteLevelGate,
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs:174:            "/api/agent/* — the M365 Copilot gateway (sweep S-18, S-19, S-78; task 164)."),
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs:1584:        new NonDecidingAttachment("AddAgentAuthorizationFilter", "Api/Agent/AgentAuthorizationFilter.cs", "164",
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs:2785:                "M365 Copilot gateway (/api/agent/*). Asserts a resolvable oid AND tid on the inbound agent token and denies "
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs:3659:        Permanent("GET /api/agent/playbooks", PermanentBasis.CallerScopedOnly, "167",
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs:3661:            + "id, AgentEndpoints.cs:306-311) plus the public catalog. Takes no id. Owner round 12 item 6; the "
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs:3880:        new SweepFinding("S-18", Severity.High, "POST /api/agent/message", "164", Gap.NoDecision)
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs:3882:        new SweepFinding("S-19", Severity.High, "POST /api/agent/run-playbook", "164", Gap.NoDecision)
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs:3884:        new SweepFinding("S-78", Severity.Low, "GET /api/agent/playbooks/status/{jobId:guid}", "164", Gap.NoDecision)
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.ProvenByTest.cs:209:                var denied = await _client.GetAsync($"/api/agent/playbooks/status/{other}");
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.ProvenByTest.cs:211:                var allowed = await _client.GetAsync($"/api/agent/playbooks/status/{mine}");
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.ProvenByTest.cs:218:                var denied = await _client.GetAsync($"/api/agent/other/{other}");
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.ProvenByTest.cs:220:                (await _client.GetAsync($"/api/agent/other/{mine}")).StatusCode.Should().Be(HttpStatusCode.OK);
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.ProvenByTest.cs:226:                (await _client.GetAsync($"/api/agent/playbooks/status/{other}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.ProvenByTest.cs:236:                (await host.Client.GetAsync($"/api/agent/playbooks/status/{other}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.ProvenByTest.cs:237:                (await host.Client.GetAsync($"/api/agent/playbooks/status/{mine}")).StatusCode.Should().Be(HttpStatusCode.OK);
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.ProvenByTest.cs:245:        const string route = "GET /api/agent/playbooks/status/{jobId:guid}";
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs:6938:                                    "GET /api/ai/chat/playbooks", "GET /api/agent/playbooks" })
tests/Spaarke.ArchTests/SessionOwnershipGuardTests.cs:64:            ["Api/Agent/AgentEndpoints.cs"] =
tests/Spaarke.ArchTests/SessionOwnershipGuardTests.cs:65:                "POST /api/agent/message takes ConversationReference (a session id) in the body. A "
tests/Spaarke.ArchTests/TenantIsolation/I6_ObAppRegDerivationTests.cs:96:/// <c>Api/Agent/AgentTokenService.cs</c> — <c>Create(_options.ClientId)</c>
tests/Spaarke.ArchTests/TenantIsolation/I6_ObAppRegDerivationTests.cs:247:        Assert.False(IsNonDerivedAppRegExpression("_options.ClientId"));          // AgentTokenService
tests/Spaarke.ArchTests/TenantIsolation/SpeAppOnlyContainerGuardTests.cs:76:        "Configuration/AgentTokenOptions.cs",
tests/integration/contract/Api/Ai/BusinessSliceDeterminismContractTests.cs:153:        var handoffUrlBuilder = new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com");
tests/integration/contract/Api/Ai/BusinessSliceDeterminismContractTests.cs:155:            new Mock<IDataverseUserClient>().Object, new Mock<ILogger<DataverseCreateRecordHandler>>().Object, handoffUrlBuilder,
tests/integration/contract/Api/Ai/BusinessSliceDeterminismContractTests.cs:159:            new Mock<IDataverseUserClient>().Object, new Mock<ILogger<DataverseCreateRecordHandler>>().Object, handoffUrlBuilder,
tests/integration/contract/Api/Ai/ChatAgentRouteProofTests.cs:51:/// Both outcomes of S-24 (<c>POST /api/ai/chat/sessions</c>), S-18 (<c>POST /api/agent/message</c>) and S-78
tests/integration/contract/Api/Ai/ChatAgentRouteProofTests.cs:52:/// (<c>GET /api/agent/playbooks/status/{jobId:guid}</c>), each in one test, over the REAL app.
tests/integration/contract/Api/Ai/ChatAgentRouteProofTests.cs:108:        var denied = await client.PostAsJsonAsync("/api/agent/message", new { message = "Summarize this", documentId = unreadable });
tests/integration/contract/Api/Ai/ChatAgentRouteProofTests.cs:115:        var allowed = await client.PostAsJsonAsync("/api/agent/message", new { message = "Summarize this", documentId = readable });
tests/integration/contract/Api/Ai/ChatAgentRouteProofTests.cs:153:        var denied = await client.GetAsync($"/api/agent/playbooks/status/{foreignRun}");
tests/integration/contract/Api/Ai/ChatAgentRouteProofTests.cs:158:        var allowed = await client.GetAsync($"/api/agent/playbooks/status/{ownRun}");
tests/integration/contract/Api/Ai/ChatContextAuthorizationContractTests.cs:21:using Sprk.Bff.Api.Api.Agent;
tests/integration/contract/Api/Ai/ChatContextAuthorizationContractTests.cs:44:/// exercised through the REAL <c>MapChatEndpoints</c>, <c>MapDispatchSessionEndpoint</c> and <c>MapAgentEndpoints</c>:
tests/integration/contract/Api/Ai/ChatContextAuthorizationContractTests.cs:47:/// (#53) and <c>POST /api/agent/message</c> (#18), plus the owner-round-12-item-6 chat playbook list.
tests/integration/contract/Api/Ai/ChatContextAuthorizationContractTests.cs:846:    // POST /api/agent/message  (sweep #18, F12)
tests/integration/contract/Api/Ai/ChatContextAuthorizationContractTests.cs:976:        var request = Authenticated(HttpMethod.Post, "/api/agent/message");
tests/integration/contract/Api/Ai/ChatContextAuthorizationContractTests.cs:1129:            _app.MapAgentEndpoints();
tests/integration/contract/Api/Ai/OutcomeCardContractTests.cs:26:/// the server-composed link stands in for a <c>HandoffUrlBuilder</c> output. The producer wiring that
tests/integration/contract/Api/Ai/OutcomeCardContractTests.cs:73:        // Server-composed link (from HandoffUrlBuilder) — url + label + kind, no provenance leak.
tests/integration/contract/Api/Ai/OutcomeCardContractTests.cs:222:            // A URL the MODEL invented, not composed by HandoffUrlBuilder — must be refused.
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:30:using Sprk.Bff.Api.Api.Agent;
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:47:/// <c>MapAgentEndpoints</c>: <c>GET /api/ai/playbooks/by-id/{id}</c> (sweep #55), <c>POST
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:48:/// /api/ai/playbooks/{id:guid}/execute</c> (#29), <c>POST /api/agent/run-playbook</c> (#19), <c>GET
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:49:/// /api/agent/playbooks/status/{jobId:guid}</c> (#78), and the owner-round-12-item-6 owned-playbook list.
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:465:    // POST /api/agent/run-playbook  (sweep #19)
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:535:        (await response.Content.ReadAsStringAsync()).Should().Contain(AgentEndpoints.PlaybookIdRequiredDetail);
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:541:    // GET /api/agent/playbooks/status/{jobId:guid}  (sweep #78)
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:556:        var others = await host.SendAsync(Get($"/api/agent/playbooks/status/{othersRun}"));
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:557:        var ownerless = await host.SendAsync(Get($"/api/agent/playbooks/status/{ownerlessRun}"));
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:558:        var unknown = await host.SendAsync(Get($"/api/agent/playbooks/status/{unknownRun}"));
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:578:        var response = await host.SendAsync(Get($"/api/agent/playbooks/status/{runId}", CallerOid));
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:868:    // GET /api/agent/playbooks/status/{jobId:guid} — a lookup fault is the same uniform 404 (task 164 r1)
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:882:        var unknown = await host.SendAsync(Get($"/api/agent/playbooks/status/{unknownRun}"));
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:883:        var faulting = await host.SendAsync(Get($"/api/agent/playbooks/status/{faultingRun}"));
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:906:        (await host.SendAsync(Get("/api/agent/playbooks"))).StatusCode.Should().Be(HttpStatusCode.OK);
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:1320:        var request = Authenticated(HttpMethod.Post, "/api/agent/run-playbook", CallerOid);
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:1334:        var request = Authenticated(HttpMethod.Post, "/api/agent/run-playbook", CallerOid);
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:1544:                throw new InvalidOperationException("POST /api/agent/message is not exercised by this host"));
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:1546:                throw new InvalidOperationException("POST /api/agent/message is not exercised by this host"));
tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs:1560:            _app.MapAgentEndpoints();
tests/integration/contract/Eval/P2LoopInjectionEvalSuiteTests.cs:605:            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/ExternalAccess/AssignedAccessCreateGrantJobTests.cs:532:            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/ExternalAccess/AssignedAccessGrantorTests.cs:126:            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/ExternalAccess/AssignedAccessWriterTriggerTests.cs:648:            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/ExternalAccess/AssignedAccessWriterTriggerTests.cs:672:            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/ExternalAccess/AssignedAccessWriterTriggerTests.cs:724:            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceWriterTests.cs:10:using Sprk.Bff.Api.Api.Agent;
tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceWriterTests.cs:171:            new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"), resolver,
tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceWriterTests.cs:382:            new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceWriterTests.cs:534:            new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"), resolver.Object, IdentityNormalizationFixtures.NoLinkedContact(), Sprk.Bff.Api
tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceWriterTests.cs:854:                new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceWriterTests.cs:1223:                new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceWriterTests.cs:1484:                new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceWriterTests.cs:1536:                new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/RecordOwnership/ChatCreatedChildStampTests.cs:9:using Sprk.Bff.Api.Api.Agent;
tests/integration/data-mutation/RecordOwnership/ChatCreatedChildStampTests.cs:54:        return new(_user, CreateLogger<DataverseCreateRecordHandler>(), new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/RecordOwnership/ChatPreSuspendServiceRefusalTests.cs:5:using Sprk.Bff.Api.Api.Agent;
tests/integration/data-mutation/RecordOwnership/ChatPreSuspendServiceRefusalTests.cs:160:        new(_user, CreateLogger<DataverseCreateRecordHandler>(), new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/RecordOwnership/ChatServerWrittenTableBlockTests.cs:5:using Sprk.Bff.Api.Api.Agent;
tests/integration/data-mutation/RecordOwnership/ChatServerWrittenTableBlockTests.cs:281:        new(_user, CreateLogger<DataverseCreateRecordHandler>(), new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.SearchIndexCreateRoute.cs:8:using Sprk.Bff.Api.Api.Agent;
tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.SearchIndexCreateRoute.cs:172:            _user, handlerLog, new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.SearchIndexCreateRoute.cs:248:            _user, handlerLog, new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.cs:7:using Sprk.Bff.Api.Api.Agent;
tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.cs:675:        new(_user, CreateLogger<DataverseCreateRecordHandler>(), new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/integration/seam/Auth/ConfidentialClientMigrationSeamTests.cs:15:using Sprk.Bff.Api.Api.Agent;
tests/integration/seam/Auth/ConfidentialClientMigrationSeamTests.cs:136:    public async Task AgentTokenService_WithNoCredentialProvider_ReturnsFailure_NeverAToken()
tests/integration/seam/Auth/ConfidentialClientMigrationSeamTests.cs:140:        var sut = new AgentTokenService(
tests/integration/seam/Auth/ConfidentialClientMigrationSeamTests.cs:142:            Options.Create(new AgentTokenOptions
tests/integration/seam/Auth/ConfidentialClientMigrationSeamTests.cs:149:            NullLogger<AgentTokenService>.Instance,
tests/integration/seam/Auth/ConfidentialClientSharingSeamTests.cs:23:/// <c>DataverseUserClient</c>, one on <c>AgentTokenService</c> — each keyed
tests/integration/seam/Auth/ConfidentialClientSharingSeamTests.cs:78:        // AgentTokenService all resolve the BFF's identity through this one provider, so they hold ONE
tests/integration/seam/ChildRecords/MatterSecureFlagChatRefusalTests.cs:5:using Sprk.Bff.Api.Api.Agent;
tests/integration/seam/ChildRecords/MatterSecureFlagChatRefusalTests.cs:70:        new(_user, CreateLogger<DataverseCreateRecordHandler>(), new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/unit/Sprk.Bff.Api.Tests/Api/Agent/AgentConfigurationServiceTests.cs:9:using Sprk.Bff.Api.Api.Agent;
tests/unit/Sprk.Bff.Api.Tests/Api/Agent/AgentConfigurationServiceTests.cs:13:namespace Sprk.Bff.Api.Tests.Api.Agent;
tests/unit/Sprk.Bff.Api.Tests/Api/Agent/AgentConversationServiceTests.cs:8:using Sprk.Bff.Api.Api.Agent;
tests/unit/Sprk.Bff.Api.Tests/Api/Agent/AgentConversationServiceTests.cs:12:namespace Sprk.Bff.Api.Tests.Api.Agent;
tests/unit/Sprk.Bff.Api.Tests/Api/Agent/HandoffUrlBuilderTests.cs:4:using Sprk.Bff.Api.Api.Agent;
tests/unit/Sprk.Bff.Api.Tests/Api/Agent/HandoffUrlBuilderTests.cs:7:namespace Sprk.Bff.Api.Tests.Api.Agent;
tests/unit/Sprk.Bff.Api.Tests/Api/Agent/HandoffUrlBuilderTests.cs:10:/// Unit tests for HandoffUrlBuilder.
tests/unit/Sprk.Bff.Api.Tests/Api/Agent/HandoffUrlBuilderTests.cs:21:public class HandoffUrlBuilderTests
tests/unit/Sprk.Bff.Api.Tests/Api/Agent/HandoffUrlBuilderTests.cs:24:    private readonly HandoffUrlBuilder _builder = new(BaseUrl);
tests/unit/Sprk.Bff.Api.Tests/Api/Agent/HandoffUrlBuilderTests.cs:31:        var builder = new HandoffUrlBuilder("https://example.crm.dynamics.com/");
tests/unit/Sprk.Bff.Api.Tests/Api/Agent/HandoffUrlBuilderTests.cs:41:        var builder = new HandoffUrlBuilder("https://example.crm.dynamics.com");
tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseCreateRecordHandlerTests.cs:6:using Sprk.Bff.Api.Api.Agent;
tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseCreateRecordHandlerTests.cs:34:    private readonly HandoffUrlBuilder _handoffUrlBuilder = new(TestDataverseBaseUrl);
tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseCreateRecordHandlerTests.cs:41:        new(_dataverse.Object, CreateLogger<DataverseCreateRecordHandler>(), _handoffUrlBuilder,
tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseCreateRecordHandlerTests.cs:258:        var expectedUrl = _handoffUrlBuilder.BuildDocumentUploadWizardUrl(matterId);
tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseCreateRecordHandlerTests.cs:278:        var expectedUnscopedUrl = _handoffUrlBuilder.BuildDocumentUploadWizardUrl((Guid?)null);
tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseCreateRecordHandlerTests.cs:297:        var expectedUrl = _handoffUrlBuilder.BuildDocumentUploadWizardUrl(matterId);
tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseToolNameFreezeTests.cs:141:            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseToolNameFreezeTests.cs:193:            new DataverseCreateRecordHandler(mock, Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseCreateRecordHandler>.Instance, new Sprk.
```
