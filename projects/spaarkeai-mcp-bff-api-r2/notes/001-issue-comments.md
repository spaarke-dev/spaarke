# Task 001: issue comment drafts and PR text

These are drafts for the main session to post. Task 001 has not posted anything.

## #1523: decision comment (draft, not posted)

> **Decision (spaarkeai-mcp-bff-api-r2 task 001, 2026-11-25):** `AgentTokenService` was removed together with the legacy M365 Copilot agent, so this issue is resolved by removal rather than by migrating the service.
>
> - Its only callers were the legacy agent's handler and services in `Api/Agent/`. That whole folder, `AgentModule`, `AgentTokenOptions` and the `Microsoft.Agents.Hosting.AspNetCore` package are gone from the BFF.
> - Its OBO path (`Create(_options.ClientId)`, options-bound) no longer exists. The remaining BFF-identity confidential-client consumers are `GraphClientFactory`, `DataverseAccessDataSource` and `DataverseUserClient`. All three resolve through `OrderedCredentialClientProvider`.
> - The `AgentToken:ClientSecret` divergence log in `IdentityConfigurationValidator` (rule 5) was removed with it.
> - `AgentToken:CopilotAudience` stays. `AuthorizationModule` still reads it for the JWT audience merge, and its run-time behaviour is unchanged.
> - Spaarke for Microsoft 365 Copilot is served by the MCP server (`/mcp/legal-operations`), which uses the caller's token through the standard OBO path.
>
> The pre-removal tree is commit `37f0864afe`.

## PR description text (for the main session's PR)

### Summary

This PR removes the legacy M365 Copilot agent gateway from the BFF (spec FR-01). It closes #2575.

What was removed:

- `Api/Agent/**` (13 files);
- `Infrastructure/DI/AgentModule.cs` and `Configuration/AgentTokenOptions.cs`;
- the `MapAgentEndpoints` and `AddAgentModule` wiring;
- the `Microsoft.Agents.Hosting.AspNetCore` package;
- the agent-only branches of `PlaybookAuthorizationFilter` and `AiAuthorizationFilter`;
- the agent-only rule 5 of `IdentityConfigurationValidator`;
- the `CopilotAgent` section and the agent-only `AgentToken` keys of `appsettings.template.json`.

What was moved: `HandoffUrlBuilder` moved to `Services/Ai/Handoff/`, with the same public methods and the same Dataverse URL fallback. It is now registered by `ToolFrameworkExtensions.AddToolFramework`, beside the handler scan that registers its live consumer, `DataverseCreateRecordHandler`.

### Placement Justification (`.claude/constraints/bff-extensions.md`)

This change only removes surface from the BFF. It adds no endpoint, service, package or background work.

The one registration that moved is `HandoffUrlBuilder`. It is now registered inside the existing tool-framework registration, the same place its only consumer is registered. That keeps it inside the AI feature-flag gate: with `Analysis:Enabled=false` neither the builder nor its consumer is registered. Before, `AgentModule` registered it unconditionally.

- **Existing:** the same class, moved.
- **Extension:** it extends an existing registration method; no new module was added.
- **Cost of doing nothing:** deleting `AgentModule` without re-registering the builder would make `DataverseCreateRecordHandler` unresolvable, which breaks the chat create-record tool.

### Publish size and CVE

See the "Publish size" section of `notes/001-task-notes.md` for the measured values (master vs branch, Release, framework-dependent, zip with PDBs) and for the result of `dotnet list package --vulnerable --include-transitive`.

### ADR-038 orphan evidence (amendment A3)

| Deleted test(s) | (a) subject deleted | (b) behaviour gone | (c) retirement test | (d) auth invariant re-targeted |
|---|---|---|---|---|
| `AgentConfigurationServiceTests`, `AgentConversationServiceTests` | `Api/Agent/AgentConfigurationService.cs`, `AgentConversationService.cs` | yes | `LegacyAgentRouteRetirementTests` | none apply (no auth invariant) |
| `PlaybookRouteAuthorizationContractTests.AgentRunPlaybook_*` (5), `AgentStatus_*` (3), `AgentOwnedPlaybookList` | agent run-playbook, status and playbook-list handlers | yes | `LegacyAgentRouteRetirementTests` | Run-mode rules are already proven on `POST /api/ai/playbooks/{id}/execute` by `Execute_DeniedUnknownAndFaultingPlaybook`, `Execute_UnreadableDocument`, `Execute_ReaderOfEveryDocument`, `Execute_ServerOwnedParameter`, `Execute_RecordParameter_*` and `Execute_ReaderWithAcceptedParameters_RunsAsTheCaller`. The empty-playbook-id 400 was specific to the agent body shape. |
| `ChatAgentRouteProofTests` S-18 and S-78 proofs | agent message and status routes | yes | `LegacyAgentRouteRetirementTests` | S-24 proof kept; the chat-context rule stays proven on the chat routes |
| `ChatContextAuthorizationContractTests` agent-message section (4) and the agent line of the revocation test | agent message route | yes | `LegacyAgentRouteRetirementTests` | the same rules stay proven on `/api/ai/chat/sessions/**` (create, messages, dispatch, revocation) |
| `ConfidentialClientMigrationSeamTests.AgentTokenService_WithNoCredentialProvider_*` | `AgentTokenService` | yes | n/a (not a route) | fail-closed stays proven for every remaining OBO consumer in the same file |

### Reviews

- **review pending: uac-r2.** It covers the route-authorization ledger and pinned counts, the `AddAiAuthorizationFilter` NonDeciding reason text, the ProvenByTest fixture, and the SessionOwnership, I6 and SpeAppOnly guard edits.
- **review pending: ebilling-r1 and ontology-r1.** They have no direct file overlap; this is for information.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
