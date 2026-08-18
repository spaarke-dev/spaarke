# BFF Credential Inventory — every place the server authenticates

> **Status**: VERIFIED reference artifact · **Date**: 2026-08-17 · **Basis**: master @ `308b0909d`
> **Method**: exhaustive grep/read audit of `src/server/**`, `src/dataverse/plugins/**`, config, infra, tests.
> **Supersedes** the partial inventory in [`ASSESSMENT.md`](ASSESSMENT.md) §2 / §9.

This is the authoritative list of what must change for a zero-secret BFF. Every row is `file:line`-cited.
**⚠ = not present in the seed assessment.**

---

## 1. MSAL confidential-client construction sites (8 — the seed said 5)

| # | Site | Flow | Credential today | Config keys | Target | Gated? | If credential absent |
|---|---|---|---|---|---|---|---|
| 1 | `Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs:83-90` (secret :88, OBO :225-228) | **OBO** | client secret | `TENANT_ID`, `API_APP_ID`, `API_CLIENT_SECRET` (+`AZURE_CLIENT_SECRET` fallback :55) | Graph `.default` | No — always built (singleton, `GraphModule.cs:43`) | CCA built with NO credential; every OBO call fails at runtime |
| 2 | `Spaarke.Dataverse/DataverseAccessDataSource.cs:59-63` (OBO :118-121) | **OBO** + app-only | client secret | `TENANT_ID`, `API_APP_ID`, `API_CLIENT_SECRET` (:39-41) | Dataverse row-level access | ⚠ **No flag** — secret *presence* selects the path (:53) | `_cca = null` (:73), falls to DI `TokenCredential`; OBO throws → **fail-closed `AccessRights.None`** |
| 3 | `Sprk.Bff.Api/Services/Ai/Handlers/Dataverse/DataverseUserClient.cs:91-96` (OBO :178-182) | **OBO only** | client secret | `AzureAd:*` primary, `TENANT_ID`/`API_APP_ID`/`API_CLIENT_SECRET` fallback (:83-85) | Dataverse (chat tools) | No | `_cca = null` → fail-closed `OboNotConfigured` (:148-152) |
| 4 | `Sprk.Bff.Api/Api/Agent/AgentTokenService.cs:49-53` (OBO :92-95 Graph, :162-165 Dataverse) | **OBO** | client secret | `AgentToken:ClientSecret` (`AgentModule.cs:21-22`) | Graph + Dataverse (M365 Copilot agent) | No; validation deferred | `[Required]` but not ValidateOnStart → fails at first token request |
| 5 | `Sprk.Bff.Api/Api/Reporting/ReportingEmbedService.cs:77-81` (:604) | app-only | client secret | `PowerBi:ClientSecret/ClientId/TenantId` | Power BI REST | No (singleton `ReportingModule.cs:38`) | options validation error at first use |
| 6 | `Sprk.Bff.Api/Api/Reporting/ReportingProfileManager.cs:74-78` (:247) | app-only | client secret | same `PowerBi:*` | Power BI (SP profiles) | No (singleton `ReportingModule.cs:41`) | as above |
| 7 | ⚠ `Sprk.Bff.Api/Services/SpeAdmin/SpeAdminTokenProvider.cs:306-310` (OBO :148) | **OBO** | client secret **fetched from KV per request** (:135, :318-358) | secret NAME from Dataverse `sprk_specontainertypeconfig.sprk_owningappsecretname` (:339) | Graph (owning-app `api://{appId}/.default`) | Per-config (`HasOwningApp`) | KV 404/403 → throw at request time |
| 8 | `Sprk.Bff.Api/Infrastructure/Graph/CiamGraphClientFactory.cs:129-133` (**cert** :131, :89) | app-only | **certificate** (KV PFX, key ephemeral in-proc :154-170) | `Ciam:GraphProvisioner:ClientId/CertificateName` | Graph (CIAM tenant) | Lazy (`ExternalAccessModule.cs:264`) | ctor throws on missing `Ciam:*` |

**Site 8 is the in-repo secret-free precedent** — certificate-based confidential auth already works in production code.

## 2. `Azure.Identity` credential sites

| Site | Credential | Flow | Gating | Notes |
|---|---|---|---|---|
| `GraphClientFactory.cs:132` / `:147` | `DefaultAzureCredential` / `ClientSecretCredential` | app-only Graph | `Graph:ManagedIdentity:Enabled` (:62-63) | MI=false + no secret → throw (:142-144) |
| `Spaarke.Dataverse/DataverseServiceClientImpl.cs:73` / `:114-118` | DAC via `ServiceClient` `tokenProviderFunction` (:87-97) / `AuthType=ClientSecret` conn-string | app-only | ✅ flag-gated | **#3b migrated**; lazy connect (:126-146) |
| `Spaarke.Dataverse/DataverseWebApiService.cs:65` / `:83` | DAC / `ClientSecretCredential` | app-only | ✅ flag-gated | **#3b migrated**; secret path reads `Dataverse:ClientSecret` (:72-74) |
| ⚠ `Spaarke.Dataverse/DataverseWebApiClient.cs:44` / `:50-52` | `ClientSecretCredential` / DAC | app-only | ⚠ **No flag** — secret presence wins (:42) | Only Dataverse app-only path that degrades gracefully to MI |
| ⚠ `Infrastructure/Graph/SpeAdminGraphService.cs:4055`, `:4185` | `ClientSecretCredential` | app-only | per-config | Per-BU secrets from KV (name from Dataverse :415, :4054) |
| `Infrastructure/Auth/ManagedIdentityCredentialFactory.cs:26-41` + `Program.cs:44-47` | DAC (UAMI-pinned) as **DI singleton `TokenCredential`** | app-only | — | **The existing shared seam** — extension candidate per root §11 |
| `Infrastructure/DI/SpeAdminModule.cs:50` | inline `DefaultAzureCredential` | app-only | — | ⚠ bypasses the factory → no UAMI pinning |
| `Services/Ai/Membership/MembershipJunctionUpdaterHost.cs:120` | inline `new DefaultAzureCredential()` | app-only | `Enabled=false` default | Service Bus |
| `Infrastructure/DI/AiModule.cs:122` / `:126-128` | `ApiKeyCredential` / DI `TokenCredential` | app-only | key wins when set | ADR-028 **E-2** exception; clearing the key restores MI in one config change |

## 3. `BFF-API-ClientSecret` — one secret, FIVE config keys (seed said three)

| Config key | Bound where | `[Required]` | ValidateOnStart | Consequence of removal |
|---|---|---|---|---|
| `Dataverse:ClientSecret` | `DataverseOptions.cs:32-33` | ✅ | ✅ (`ConfigurationModule.cs:30-34`) | **startup crash**, regardless of MI flag |
| ⚠ `Graph:ClientSecret` | `GraphOptions.cs:31` | conditional (required when MI=false, `GraphOptionsValidator.cs:20-23`) | ✅ (`ConfigurationModule.cs:24-28`) | crash when MI=false |
| `AzureAd:ClientSecret` | raw `IConfiguration` (`DataverseUserClient.cs:85`) | — | — | OBO degrades |
| `API_CLIENT_SECRET` | raw config in 5 files | — | — | widest-consumed alias |
| ⚠ `AgentToken:ClientSecret` | `AgentTokenOptions.cs:38-39` | ✅ | ❌ deferred | ⚠ seed calls this a *separate* secret; `CONFIGURATION-MATRIX.md:319` + `Reconcile-DemoEnvironment.ps1:76` map it to the **same** `BFF-API-ClientSecret` |

Fan-out is set by `scripts/Configure-ProductionAppSettings.ps1:69-81`.
⚠ **Sixth path**: a duplicate lowercase KV alias **`bff-api-client-secret`** used by the Office add-in deploy (surface map HYGIENE-1). Any removal that ignores it breaks the add-in.

## 4. Genuinely separate secrets / keys

| Credential | Site | Notes |
|---|---|---|
| `PowerBi:ClientSecret` | `PowerBiOptions.cs:44-45` | separate SP; which one is undocumented |
| `AzureOpenAI:ApiKey` | `AiModule.cs:115` | ADR-028 E-2 |
| `DocumentIntelligence:OpenAiKey` / `DocIntelKey` / `AiSearchKey` | `DocumentIntelligenceOptions.cs:42,152,303` | KV-backed |
| `BingSearch:ApiKey` | `WebSearchHandler.cs:283,504` | ⚠ not in seed |
| `AiSafety:ContentSafety:ApiKey` | `ContentSafetyAuthHandler.cs:41,72` | MI alternative already exists (`ContentSafetyTokenProvider.cs:55`) |
| `AiSearch:ReferencesApiKey` | `InternalIndexProvider.cs:78-88` | ⚠ not in seed |
| `Analysis:PromptFlowKey` | `appsettings.json:118` | ⚠ not in seed |
| `LlamaParse:ApiKeySecretName` | `LlamaParseClient.cs:121-126` | KV-at-runtime |
| `ConnectionStrings:ServiceBus` (SAS) | `appsettings.json:16` | ⚠ a secret credential; Membership path already uses namespace+MI |
| Webhook HMAC keys | `CommunicationOptions.cs:65-66`, `EmailProcessingOptions.cs:192` | inbound validation, not outbound auth — out of scope |
| ⚠ **Plaintext secrets in Dataverse columns** | `BaseProxyPlugin.cs:121-124` + `SimpleAuthHelper.cs:19-26` (`sprk_externalserviceconfig.sprk_clientsecret` / `sprk_apikey`) | Outside Key Vault entirely; `AuthType=2 (ManagedIdentity)` explicitly throws (:199-204) |

## 5. OBO blast radius (what breaks if OBO breaks)

| OBO site | Downstream | Surfaces / routes |
|---|---|---|
| `GraphClientFactory` (:270-279) | `ContainerOperations`, `DriveItemOperations`, `UploadSessionManager`, `UserOperations`, `OfficeEmailEnricher`, `CommunicationService`, `EmailChannelSender`, `SendEmailNodeExecutor`, `EmailExportService`, `MemoryItemStore`, `ContextBinder`, `PrivilegeGroupResolver` | SPE documents (`/api/documents`, `/api/v1/documents`, `/api/workspace/files`, `/api/spe`); Office add-ins (`/api/office`); send-as-user email (`/api/communications`); chat memory (`/api/memory`, `/api/ai/chat`) |
| `DataverseAccessDataSource` (:118-121) | `AuthorizationService`, `CachedAccessDataSource`, `AiAuthorizationService`, `AiAuthorizationFilter`, `VisualizationAuthorizationFilter`, `PermissionsEndpoints.cs:56,116` | **Row-level authorization for every document + AI endpoint running an authorization filter** — highest blast radius. Fails *closed* → users locked out (not exposed) |
| `DataverseUserClient` (:178-182) | `dataverse.*` AI tool handlers | `/api/ai/chat` SSE tool calls, `/api/dataverse` |
| `AgentTokenService` (:92, :162) | `SpaarkeAgentHandler` | `/api/agent` (M365 Copilot) |
| `SpeAdminTokenProvider` (:148) | `SpeAdminGraphService` multi-app | SPE admin / BU container management |

## 6. Refactor surface

- **8 CCA sites** + **5 `ClientSecretCredential` sites** using the BFF's own identity.
- ⚠ **Lifetime hazard**: `DataverseAccessDataSource` is a **transient** typed HttpClient (`SpaarkeCore.cs:39`) → new CCA per resolution; `AgentTokenService` is **scoped** (`AgentModule.cs:24`) → new CCA per request. Client assertions require shared/cached clients. `DataverseUserClient.cs:55-56,91` already solved this with a process-wide static CCA cache keyed `(tenant|client)` — **that is the model to copy**.
- **Reuse seam (root §11)**: `ManagedIdentityCredentialFactory` (`Infrastructure/Auth/`) + DI singleton `TokenCredential` (`Program.cs:44-47`). `ContentSafetyTokenProvider.cs:15-22` already documents extending it.
- **Estimate**: ~350–550 LOC across ~15 files.

## 7. Test impact

- **46 test files** seed dummy `API_CLIENT_SECRET` / `Graph:ClientSecret` / `Dataverse:ClientSecret` to satisfy ValidateOnStart + the `GraphClientFactory` ctor (canonical: `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs:38,49,57`; `tests/integration/.../DataverseIntegrationTestFixture.cs:111,128,136`).
  Relaxing `[Required]` is backward-compatible; **requiring a new credential-provider ctor arg breaks all 46** unless the provider has a test-friendly default.
- Direct CCA-constructing tests: `ReportingEmbedServiceTests.cs:47`, `ReportingProfileManagerTests.cs:37,166,184`.
- Auth-mode branch tests: `ContentSafetyAuthHandlerTests.cs`; `Spe.Integration.Tests/AuthorizationIntegrationTests.cs`.

## 8. Local development

- `appsettings.Development.json` (gitignored) sets `Graph:ManagedIdentity:Enabled=false` and placeholder secrets; the real value comes from **user-secrets** (`UserSecretsId cbc576fa-…`, `Sprk.Bff.Api.csproj:14`).
- ⚠ That local file also contains a **live Service Bus SAS key** — rotate-worthy.
- Removing the secret fallback breaks local dev in three ways: `DataverseOptions` `[Required]` startup crash; MI=false ctor throws in `GraphClientFactory`/`DataverseServiceClientImpl`/`DataverseWebApiService`/`GraphOptionsValidator`; **and all local OBO dies** — `DefaultAzureCredential` via az-CLI covers app-only but there is no local MI to issue a FIC assertion. This is a real design problem, not a footnote.
