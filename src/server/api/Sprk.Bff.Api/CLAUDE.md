<!--
Maintainer notes (stripped before Claude reads this file):
- Loads whenever Claude reads or edits a file under src/server/api/Sprk.Bff.Api/ — i.e. on most backend tasks.
  Keep it to orientation + load-bearing facts. Do not restate .claude/rules/bff-hygiene.md (it auto-loads for
  this folder) or ADR text; point to them.
- Size target: about 8 KB — a target, not a cap; exceed it when the content is load-bearing and say why in the PR. No generic C# samples (Claude knows ProblemDetails); point to .claude/patterns/api/.
- The heading "Package Management" is cited by a comment in Sprk.Bff.Api.csproj — do not rename it.
- Auth state is time-boxed and environment-specific: point to ADR-028 / constraints, do not paraphrase status.
- Previous full version: .claude/archive/2026-10-07/modules/Sprk.Bff.Api.CLAUDE.md
-->
# Sprk.Bff.Api — module notes

The .NET 10 Minimal API that is the single backend for every client surface (SPE documents, AI/chat, Office add-ins, email/communication, finance, workspace, background jobs). Platform overview: [`docs/architecture/sdap-overview.md`](../../../../docs/architecture/sdap-overview.md).

**Also loaded automatically when you edit here:** [`.claude/rules/bff-hygiene.md`](../../../../.claude/rules/bff-hygiene.md) — placement decision, AI `PublicContracts` facade, publish-size delta vs fresh master, CVE check, tests (root `CLAUDE.md` §10). Design-time: [`.claude/constraints/bff-extensions.md`](../../../../.claude/constraints/bff-extensions.md).

## Where things are

| To find… | Start at |
|---|---|
| Startup, DI, middleware | `Program.cs`; endpoint mapping `Infrastructure/DI/EndpointMappingExtensions.cs` |
| Endpoints | `Api/**` — one `*Endpoints.cs` per area (`Api/Ai/`, `Api/Office/`, `Api/SpeAdmin/`, `Compose*Endpoints.cs`, …) |
| Resource authorization filters | `Api/Filters/` (e.g. `DocumentAuthorizationFilter.cs`) |
| SPE operations facade | `Infrastructure/Graph/SpeFileStore.cs` |
| Graph clients / OBO | `Infrastructure/Graph/GraphClientFactory.cs`; OBO token cache `Services/GraphTokenCache.cs` |
| Confidential-client credentials | `Infrastructure/Auth/OrderedCredentialClientProvider.cs` (reads `Graph:Credentials:Order`) |
| Settings inventory (`_comment` entries explain the keys where the reason matters) | `appsettings.template.json` |
| Startup config validation | `Configuration/` (`IdentityConfigurationValidator.cs`, `GraphOptionsValidator.cs`) |
| Audit enrichment (`oid`, `appid`, `obo`, `tenantId`, `correlationId`) | `Infrastructure/Logging/AuditEnrichmentMiddleware.cs` |
| Chat pipeline | `Api/Ai/ChatEndpoints.cs` → `Services/Ai/Chat/ChatSessionManager.cs` → `SprkChatAgentFactory.cs` → `PlaybookChatContextProvider.cs` → `AgentToolCatalogProjector.cs` (closed catalog, ADR-039) → `Services/Ai/RagService.cs` |
| AI analysis pipeline | `Services/Ai/AnalysisOrchestrationService.cs`; design: `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` |
| Background jobs | `Services/Jobs/ServiceBusJobProcessor.cs` (queue, ADR-004); scheduled: ADR-036 `IScheduledJob` |
| Code patterns (endpoint, filter, errors, DI, workers, resilience) | `.claude/patterns/api/` |
| Inbound API-key schemes; webhook HMAC | `Infrastructure/Authentication/ApiKeyAuthenticationHandler.cs` (one handler, several named schemes); `Api/Filters/WebhookSignatureFilter.cs` |
| Local dev secrets | `src/server/api/Sprk.Bff.Api/docs/SPE.BFF.API-SECRETS-SETUP.md` (short answer: `az login` covers everything except OBO; any Key Vault step it mentions is governed by root §9) |

## Binding rules

- **ADR-007** — SPE access goes through `SpeFileStore`; never inject `GraphServiceClient` into endpoints.
- **ADR-008** — resource authorization uses endpoint filters (`.AddEndpointFilter<…AuthorizationFilter>()` + `.RequireAuthorization()`), not global middleware.
- Every endpoint requires auth except `/healthz` and `/ping` (root §9).
- **ADR-010** — DI minimalism: register concretes; add an interface only as a real seam.
- Errors return `ProblemDetails` (`Results.Problem(...)`), never raw exception text. Log with structured properties, not string interpolation. Keep endpoints thin; logic lives in services.

## Auth — load-bearing facts ([ADR-028](../../../../.claude/adr/ADR-028-spaarke-auth-architecture.md), [`.claude/constraints/auth.md`](../../../../.claude/constraints/auth.md))

- **OBO needs a confidential *credential*, not a secret.** Here it is a managed-identity-issued federated client assertion (ADR-028 A4). Do not re-derive "OBO needs a secret" from any doc; if you find a doc that says so, fix it.
- The BFF identity is **secret-free**. `OrderedCredentialClientProvider` resolves the credential named in `Graph:Credentials:Order`; `Graph:Credentials:RequireSecretFreeIdentity=true` makes the app refuse to start outside Development if `ClientSecret` returns to the order. App-only Graph and Dataverse use the managed identity when `Graph:ManagedIdentity:Enabled=true`; it is a user-assigned identity selected by `Graph:ManagedIdentity:ClientId` (validated at startup in `Configuration/`). Mailbox-scoped Graph (`Mail.*`) also needs Exchange *RBAC for Applications* scoping (`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §4.2.1; it replaced the legacy `ApplicationAccessPolicy`, which some older docs still describe).
- ⚠️ **Never add a `.WithClientSecret(...)` site.** `tests/Spaarke.ArchTests/CredentialGuardTests.cs` fails the build on one and `CredentialCensusTests` asserts the construction-site count. The only sanctioned secret-bearing credentials are ADR-028's listed exceptions — read their current text there.
- Inbound from trusted external systems uses named API-key schemes (`AuthenticationHandler<>` per scheme, constant-time compare with `CryptographicOperations.FixedTimeEquals`).
- Clients call with `@spaarke/auth` (`authenticatedFetch`). Never add `accessToken` props or custom auth headers to the client contract.
- Key Vault secrets or credential order: root §9 (and `.claude/rules/credentials.md`). New environment: `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`.

## Testing

Follow [`tests/CLAUDE.md`](../../../../tests/CLAUDE.md): a new endpoint gets a contract test, a bug fix gets a regression test, integration-first through a `WebApplicationFactory<Program>` fixture. Do not unit-test endpoint handlers against mocked facades.

## Package Management

**Microsoft.Graph / Kiota.** The BFF references `Microsoft.Graph` 6.x directly (`Sprk.Bff.Api.csproj`); Kiota is **transitive only**. (`Directory.Packages.props` sets `ManagePackageVersionsCentrally=false`, so its older Graph/Kiota entries are inert.) Every resolved `Microsoft.Kiota.*` assembly must be the same version or the app fails at runtime with binding errors — Graph.Core's own dependency graph guarantees that today. Do not add direct `Microsoft.Kiota.*` `PackageReference`s unless a genuine transitive conflict forces one. If it does:
1. Confirm: `dotnet list package --include-transitive | grep -i kiota` shows more than one Kiota version.
2. Pin **all** `Microsoft.Kiota.*` references to the same version (never a partial set), with an inline comment naming the conflict.
3. Re-run the check, then build and test locally before deploying.

History (CVE-2026-44503 and the retired direct pins): the csproj comment and `projects/dotnet-10-upgrade-r1/notes/graph6-kiota2-break-assessment.md`.
