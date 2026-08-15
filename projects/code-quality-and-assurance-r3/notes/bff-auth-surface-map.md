# BFF Auth Surface Map — code-quality-and-assurance-r3 (task 019)

> **Produced**: 2026-08-13 (read-only investigation; branch `work/code-quality-and-assurance-r3`)
> **Purpose**: THE verified auth picture for every auth-touching r3 task (023, 060, 061, 062, #3b/011). Section E (secret/identity → consumer dependency graph) is the centerpiece: "is it safe to change/remove this, and when" is a lookup here.
> **Method**: grep + read across `src/server/api/Sprk.Bff.Api/`, `src/server/shared/Spaarke.*`, `scripts/`, `docs/`; `bin/obj/_archive` excluded. Every claim cited `file:line`. Cross-checked against ADR-028 (+A1/A2), the BFF workstream Verification Addendum, and the 2026-08-13 deployment-refactors addendum.
> **Coverage boundary**: this map covers the BFF's auth schemes/policies, all credential-construction sites, and all auth secrets. It does not enumerate per-endpoint authorization *filters* for every one of the ~100 endpoint groups (only the auth-relevant ones); Dataverse-passthrough / membership filter internals are out of scope.

---

## A. INBOUND (client → BFF)

### A.1 Authentication schemes (4 registered — all in `AuthorizationModule.AddAuthorizationModule`, called from `Program.cs:54`)

| # | Scheme | Kind | Registration site | Validates | Pinned to |
|---|---|---|---|---|---|
| 1 | `JwtBearerDefaults.AuthenticationScheme` (**default**) | JwtBearer via `AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"))` | `Infrastructure/DI/AuthorizationModule.cs:34-36` | Workforce Entra tokens, audience `api://{API_APP_ID}` (`appsettings.template.json:36-41`) | Everything that calls `.RequireAuthorization()` without a named policy; runs automatically as the default scheme |
| 2 | `AuthSchemes.Ciam` = `"Ciam"` | Named JwtBearer, **appended to the same authentication builder** (no second `AddAuthentication` — workforce default preserved) | `AuthorizationModule.cs:46-58`; scheme name at `Infrastructure/Authentication/AuthSchemes.cs:29` | Entra External ID (CIAM) tokens — authority `https://{sub}.ciamlogin.com/{tid}/v2.0` built from the `Ciam` config section (`AuthorizationModule.cs:48-53`) | `/api/v1/external` group via `AuthPolicies.ExternalCollaboration` (`Api/ExternalAccess/ExternalAccessEndpoints.cs:56-58`) |
| 3 | `AuthSchemes.RagApiKey` = `"RagApiKey"` | Custom `ApiKeyAuthenticationHandler` (constant-time compare; config key `Rag:ApiKey`) | `AuthorizationModule.cs:73-80` | Static API key header | `POST /api/ai/rag/enqueue-indexing` via `.RequireAuthorization(AuthPolicies.RagApiKey)` (`Api/Ai/RagEndpoints.cs:136`) |
| 4 | *(removed)* `BuilderAdminApiKey` | — | Removal recorded `AuthorizationModule.cs:70-72` (2026-07-07) | — | — |

**Copilot audience merge**: a `PostConfigure<JwtBearerOptions>` on the **default scheme only** merges `AgentToken:CopilotAudience` into the valid-audience set, with an `Interlocked` idempotency guard (`AuthorizationModule.cs:86-127`). It explicitly does NOT apply to the named `"Ciam"` options instance (`AuthorizationModule.cs:43-45`). Consequence: M365 Copilot agent tokens authenticate on the *default* scheme.

### A.2 Fallback policy — CONFIRMED ABSENT (re-verified)

`grep AddAuthentication|SetFallbackPolicy|FallbackPolicy|AddAuthorization` across `src/server` returns only `AuthorizationModule.cs` (`:35,:46,:73,:171`) — **no `SetFallbackPolicy`, no `options.FallbackPolicy`, no `options.DefaultPolicy` override anywhere**. Implications:
- Endpoints mapped without `.RequireAuthorization()` are **anonymous-by-omission** (the default JwtBearer scheme still *runs* and populates `HttpContext.User` when a token is present, but nothing *requires* it).
- `.RequireAuthorization()` with no arguments applies the framework `DefaultPolicy` = `RequireAuthenticatedUser` against the **default scheme's** authenticate result (workforce; incl. merged Copilot audience). The CIAM scheme never participates unless a policy names it (it is a named scheme, only invoked by `AuthenticationSchemes = { AuthSchemes.Ciam }` policies — `AuthorizationModule.cs:258-262, 270-278`).

### A.3 Authorization policies (`AuthorizationModule.cs:171-298`)

- ~26 resource-operation policies (`canpreviewfiles` … `canmanagecontainers`) each adding a `ResourceAccessRequirement` (`:174-239`), enforced by `ResourceAccessHandler` (`:157`).
- `AuthPolicies.RagApiKey` — pins scheme `RagApiKey` + `RequireAuthenticatedUser` (`:247-251`).
- `AuthPolicies.CiamExternal` — pins scheme `Ciam` only (`:258-262`). **Residual**: defined but no longer applied to any endpoint group (superseded by `ExternalCollaboration` — grep shows no `.RequireAuthorization(AuthPolicies.CiamExternal)` call site; only the definition and a doc comment `Infrastructure/Authentication/AuthPolicies.cs:23`).
- `AuthPolicies.ExternalCollaboration` — accepts **both** `Ciam` AND the workforce default scheme (`:270-278`); a token validates against exactly one authority, so exactly one scheme succeeds per request.
- `SystemAdmin` — role/scope assertion (`:281-297`).
- Tenant routing: `TenantEnvironmentRoutingOptions` + `ITenantEnvironmentRouter` (deny-by-design `tid`→environment map, `AuthorizationModule.cs:159-168`) — registered via bare `services.Configure<>` (no ValidateOnStart; relevant to task 061).

### A.4 Endpoint-group auth pinning (external surface)

- `/api/v1/external` → `.RequireAuthorization(AuthPolicies.ExternalCollaboration)` + `AddCallerPrincipalAuthorizationFilter()` (plane-agnostic CIAM-contact/workforce principal resolution) — `ExternalAccessEndpoints.cs:56-59`.
- `/api/v1/external-access` (internal admin: grant/revoke/invite/provision) → `.RequireAuthorization()` (workforce default) — `ExternalAccessEndpoints.cs:86-88`.
- `/api/v1/collab` (transitional Teams-host group, slated for removal) → `.RequireAuthorization()` + per-endpoint `WorkforceCallerAuthorizationFilter` — `ExternalAccessEndpoints.cs:133-159`.
- The old `ExternalCallerAuthorizationFilter` behavior is reproduced byte-for-byte inside the CIAM strategy of `CallerPrincipalResolver` (`ExternalAccessEndpoints.cs:50-55`; strategies registered `Infrastructure/DI/ExternalAccessModule.cs:78-80`).

### A.5 Anonymous inventory (explicit `.AllowAnonymous()` + anonymous-by-omission)

| Endpoint | Site | Protection | Class |
|---|---|---|---|
| `/healthz`, `/healthz/catalog` | `Infrastructure/DI/EndpointMappingExtensions.cs:52-62` | none (liveness probes) | intended |
| `/healthz/dataverse`, `/healthz/dataverse/crud` | `EndpointMappingExtensions.cs:64-65` | **none — no auth, no rate limit, hit Dataverse live, echo `ex.Message` at `:382,:398`** | task 023 target (B-2) |
| `/healthz/dataverse/doc/{id}` | `EndpointMappingExtensions.cs:67-97` | `.AllowAnonymous().RequireRateLimiting("anonymous")` but **echoes `ex.Message`/`InnerException` at `:93`** | task 023 target (MF-3) |
| `/ping`, `/status` | `EndpointMappingExtensions.cs:99-116` | rate-limited (`/status`) | intended |
| `GET /api/config/client` (MSAL bootstrap) | `Api/ConfigEndpoints.cs:39-41` | rate-limited; non-secret values only (`:23-26`) | intended |
| Finance recalculate ×2 (**Dataverse WRITE**) | `Api/Finance/FinanceRollupEndpoints.cs:29,46` (rationale comment `:13,:22`) | rate limiting only | **task 023 target (B-1)** |
| OBO endpoints ×7 (anonymous-by-omission) | `Api/OBOEndpoints.cs:16,55,106,141,205,245,315` (mapped bare on `app`, e.g. `:16-48`; group has no `RequireAuthorization` — full list per bff design.md:280) | handlers force OBO exchange → crash-401 on missing bearer | **task 023 target (B-3)** |
| User endpoints ×2 (anonymous-by-omission) | `Api/UserEndpoints.cs:18,22` | same crash-401 semantics | **task 023 target (B-3)** |
| Graph webhook (Communication) | `Api/CommunicationEndpoints.cs:282-300` | `AllowAnonymous` + HMAC `X-Hub-Signature-256` (`WebhookSignatureFilter`, key `Communication:WebhookSigningKey`, `:295`) + `clientState` fail-closed (`:997-1004`) | intended (webhook) |
| Graph webhook (Compose) | `Api/ComposeEndpoints.cs:198-218` | `AllowAnonymous` + HMAC (`Compose:Webhook:SigningKey`, `:213`) + clientState | intended (webhook) |
| ACS Event Grid ingress | `Api/AcsEventGridEndpoints.cs:28-31` | `AllowAnonymous` + subscription-validation handshake + topic allow-list + optional `?sig=` (`Configuration/AcsEventGridIngressOptions.cs:15-23,45`) | intended (webhook) |
| Demo registration | `Endpoints/RegistrationEndpoints.cs:22` | self-service surface | intended |
| Office health | `Api/Office/OfficeEndpoints.cs:72-78` | rate-limited | intended |
| Office `/save-debug` | `OfficeEndpoints.cs:113-159` | **Development-environment-only** gate (`:113`) | dev-only |

### A.6 GAP #2 — RESOLVED: task 023's `.RequireAuthorization()` vs the CIAM scheme / missing fallback

**No adverse interaction; the change is scheme-safe and actually *strengthens* ADR-028 A1.** Mechanics:

1. Bare `.RequireAuthorization()` uses the framework DefaultPolicy (never overridden — §A.2), which evaluates the **default workforce scheme's** authenticate result. The `Ciam` scheme runs only when a policy names it (`AuthorizationModule.cs:46-58` registers it as a *named* scheme; only `CiamExternal`/`ExternalCollaboration` name it, `:258-278`).
2. A CIAM token presented to `/api/obo/*` or `/api/me*` will therefore get **401** (workforce authority cannot validate a `*.ciamlogin.com` token). That is the *desired* outcome: ADR-028 A1 mandates **no OBO on the external path** (`.claude/adr/ADR-028-spaarke-auth-architecture.md:49,71`). Today (anonymous-by-omission) a CIAM caller can *reach* the OBO handler and only fails inside MSAL; after 023 they are rejected at the policy layer.
3. Copilot-audience tokens continue to authenticate on the default scheme via the audience merge (`AuthorizationModule.cs:98-113`) — `.RequireAuthorization()` does not narrow audiences; no Copilot-path regression.
4. The absence of a fallback policy means the per-endpoint `.RequireAuthorization()` is the ONLY enforcement — exactly what 023 adds. No double-enforcement or scheme-selection ambiguity is introduced.
5. **Do NOT** use `.RequireAuthorization(AuthPolicies.ExternalCollaboration)` or any Ciam-naming policy on OBO/User/Finance endpoints — plain default-policy `.RequireAuthorization()` is correct (matches the `ScorecardCalculatorEndpoints` sibling per bff design.md:280).
6. Test-infra caveat: the audience-merge `PostConfigure` has a **static** once-per-process guard (`AuthorizationModule.cs:24,:93`); WebApplicationFactory-based negative tests (023 step 6) share that process state — assert on 401 behavior, not on audience-list contents.

---

## B. OUTBOUND (BFF → external services), per service

### B.1 Microsoft Graph — `GraphClientFactory` (workforce tenant)

| Flow | Credential | Construction site | Identity resolved | Context |
|---|---|---|---|---|
| App-only, MI mode (`Graph:ManagedIdentity:Enabled=true` — canonical in Azure) | `DefaultAzureCredential`, pinned to `Graph:ManagedIdentity:ClientId` when set | `Infrastructure/Graph/GraphClientFactory.cs:113-132` | **UAMI** `mi-bff-api-{env}` (see §C) | app-only |
| App-only, legacy mode (flag false) | `ClientSecretCredential(_tenantId, _clientId, _clientSecret)` from `AZURE_TENANT_ID/TENANT_ID`, `AZURE_CLIENT_ID/API_APP_ID`, `AZURE_CLIENT_SECRET/API_CLIENT_SECRET` (`:53-55`) | `GraphClientFactory.cs:136-148` | BFF app-reg (`1e40baad…` dev) | app-only (local-dev fallback) |
| **OBO** (delegated) | `ConfidentialClientApplicationBuilder.Create(API_APP_ID).WithAuthority(login.microsoftonline.com/{TENANT_ID}).WithClientSecret(API_CLIENT_SECRET)` | `GraphClientFactory.cs:66-90` (build), exchange `AcquireTokenOnBehalfOf(["https://graph.microsoft.com/.default"])` at `:225-228` | BFF app-reg acting for the user | user-context |

Scope: `https://graph.microsoft.com/.default` for both app-only (`:151-154`) and OBO (`:226`). App-only client is a cached singleton (`:96,:297-302`); OBO client per-request with Redis token cache (§F GAP #5).

**Latent footgun**: `_clientId = configuration["AZURE_CLIENT_ID"] ?? configuration["API_APP_ID"]` (`GraphClientFactory.cs:54`) — in Azure, `AZURE_CLIENT_ID` is deliberately set to the **UAMI clientId** (`docs/guides/auth-deployment-setup.md:156-163`), so if the MI flag were ever turned OFF in Azure, the legacy path would build `ClientSecretCredential` with the *UAMI's* clientId + the *app-reg's* secret → guaranteed auth failure. Only the MI flag being true protects this. (The OBO CCA is unaffected — it reads `API_APP_ID` directly at `:68`.)

### B.2 Dataverse — FOUR distinct stacks/camps

| Camp | Credential | Site | Identity | Context |
|---|---|---|---|---|
| **(1) ServiceClient** `DataverseServiceClientImpl` (`IDataverseService` singleton + 7 of 9 narrow-interface forwards, `Infrastructure/DI/GraphModule.cs:46-51,68-81`) | connection string `AuthType=ClientSecret;…ClientId={API_APP_ID};ClientSecret={API_CLIENT_SECRET}`; **throws if secret absent** | `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs:40-64` (throw `:45-50`) | BFF app-reg | app-only. **Still secret-based in prod — the #3b migration target** |
| **(2) Raw-HTTP** `DataverseWebApiService` (`IEventDataverseService`/`IFieldMappingDataverseService` forwards, `GraphModule.cs:56-63,73,78`) | `ClientSecretCredential(TENANT_ID, API_APP_ID, Dataverse:ClientSecret)`; **hard-requires `Dataverse:ClientSecret`** (`:51-52`) | `Spaarke.Dataverse/DataverseWebApiService.cs:37-56`; scope `{org}/.default` derived at `:97` | BFF app-reg | app-only. #3b target |
| **(3) Raw-HTTP** `DataverseWebApiClient` (singleton in `Infrastructure/DI/SpeAdminModule.cs:50`; used by SpeAudit/DashboardSync) | **secret-preferred**: `ClientSecretCredential(API_APP_ID/API_CLIENT_SECRET)` when set, else `DefaultAzureCredential` pinned to `ManagedIdentity:ClientId` | `Spaarke.Dataverse/DataverseWebApiClient.cs:36-54` | BFF app-reg today (secret always present for OBO ⇒ secret branch wins) | app-only. #3b target — **needs a code change, not just config removal** (see §G #3b) |
| **(4) `Services/Ai` camp** (`DataverseHttpServiceBase` + 13 files — AUTHV2-042-migrated) | DI-injected `TokenCredential` singleton from `ManagedIdentityCredentialFactory` | `Program.cs:44-48`; factory `Infrastructure/Auth/ManagedIdentityCredentialFactory.cs:29-41` | **UAMI** | app-only. Already ADR-028-compliant |
| **(5) Authorization data** `DataverseAccessDataSource` (typed HttpClient, `Infrastructure/DI/SpaarkeCore.cs:39-54`; decorated by `CachedAccessDataSource` `:59-67`) | dual-mode: `ClientSecretCredential` + **OBO CCA** when `API_CLIENT_SECRET` present (prod), else DI `TokenCredential`/`DefaultAzureCredential` (no OBO) | `Spaarke.Dataverse/DataverseAccessDataSource.cs:49-77`; OBO `AcquireTokenOnBehalfOf({org}/.default)` `:118-121` | BFF app-reg (both app-only + user-context) | mixed — **the OBO half must survive #3b** |
| **(6) User-context tools** `DataverseUserClient` (AI `dataverse.*` handlers) | static-cached CCA per (tenant,client): `AzureAd:ClientSecret ?? API_CLIENT_SECRET`; **fail-closed, no app-only fallback by design** | `Services/Ai/Handlers/Dataverse/DataverseUserClient.cs:83-106` | BFF app-reg acting for user | user-context (OBO) |
| **(7) Agent OBO** `AgentTokenService` (M365 Copilot) | CCA from `AgentToken:ClientId/ClientSecret/TenantId`; scope `{DataverseEnvironmentUrl}/.default` and Graph | `Api/Agent/AgentTokenService.cs:49-53` (build), Graph OBO `:92-95`, Dataverse OBO `:158-165` | BFF app-reg (AgentToken:ClientSecret = same KV secret, §E) | user(agent)-context |

Dataverse **Application User** today = the BFF app-reg `1e40baad…` with System Administrator (`docs/architecture/auth-azure-resources.md:288-295`). Impersonation prereq: BFF app user must hold `prvActOnBehalfOfAnotherUser` (`Spaarke.Dataverse/DataverseImpersonation.cs:24`; go-live note `Infrastructure/DI/CommunicationModule.cs:361`, `DataverseWebApiService.cs:1944`).

### B.3 Cosmos DB

`CosmosClient` singleton built with the **DI `TokenCredential`** (UAMI) — no connection strings (ADR-015): `Infrastructure/DI/AiPersistenceModule.cs:56-75` (credential injected `:66`). Endpoint from `CosmosPersistence:Endpoint`. RBAC: UAMI needs `Cosmos DB Built-in Data Contributor` (`docs/guides/auth-deployment-setup.md:170`, `docs/architecture/auth-azure-resources.md:240`).

### B.4 Key Vault

ONE shared `SecretClient` singleton: `new SecretClient(new Uri(SpeAdmin:KeyVaultUri ?? KeyVaultUri), new DefaultAzureCredential())` — `Infrastructure/DI/SpeAdminModule.cs:39-44`. Note the credential here is **unpinned** `DefaultAzureCredential()`; on the multi-identity App Service it resolves the UAMI only because `AZURE_CLIENT_ID={uami-client-id}` is set as an app setting (`auth-deployment-setup.md:156-163`). Consumers of this one client: `SpeAdminGraphService` (`:312,:342`), `SpeAdminTokenProvider` (`Services/SpeAdmin/SpeAdminTokenProvider.cs:60-67`), `CiamGraphClientFactory` (`Infrastructure/Graph/CiamGraphClientFactory.cs:52-58`), `KnowledgeDeploymentService` (`Services/Ai/KnowledgeDeploymentService.cs:68,461-478`), the tracking-footer HMAC signer (secret NAME in config only — `Configuration/TrackingFooterOptions.cs:17-46`, `Services/Communication/Engine/TrackingFooterGate.cs:38-46`). Separately, App-Service-level `@Microsoft.KeyVault(...)` references resolve via the App Service's `keyVaultReferenceIdentity` = the UAMI (`auth-deployment-setup.md:186-204`).

### B.5 Azure OpenAI / AI Services

`AiModule.BuildInnerClient`: if `AzureOpenAI:ApiKey` set → `ApiKeyCredential` (ADR-028 **documented exception E-2**); else DI `TokenCredential` (UAMI) — `Infrastructure/DI/AiModule.cs:112-132`; exception rationale + restore-to-MI path `ADR-028:106-113`. Secret: `AzureOpenAI-ApiKey` in `spaarke-spekvcert` (`ADR-028:110`). A second, legacy config pair also exists: `DocumentIntelligence:OpenAiEndpoint/OpenAiKey` (KV `ai-openai-endpoint`/`ai-openai-key`, `appsettings.template.json:132-133`).

### B.6 Azure AI Content Safety

`ContentSafetyTokenProvider` **reuses `ManagedIdentityCredentialFactory.Create`** (UAMI bearer) — `Services/Ai/Safety/ContentSafetyTokenProvider.cs:51-55`; `ContentSafetyAuthHandler` prefers `AiSafety:ContentSafety:ApiKey` when configured, else MI bearer (`Services/Ai/Safety/ContentSafetyAuthHandler.cs:14,:72`; module wiring `Infrastructure/DI/AiSafetyModule.cs:54`). Key: KV `ContentSafety-ApiKey` (`appsettings.template.json:280`).

### B.7 SharePoint Embedded admin (SpeAdmin) — per-tenant/per-config

- `SpeAdminGraphService`: reads `sprk_specontainertypeconfig` from Dataverse → per-config `ClientId` + `SecretKeyVaultName` → fetches secret via the shared `SecretClient` (`Infrastructure/Graph/SpeAdminGraphService.cs:380-415, 4136-4142`) → builds `ClientSecretCredential(config.TenantId, config.ClientId, clientSecret)` for Graph (`:4177-4184`) and for SharePoint-scoped tokens (`:4053-4054`). **Per-customer tenant + per-customer app-reg** — ADR-028 exception E-1 (`ADR-028:100-104`).
- `SpeAdminTokenProvider` (multi-app OBO): fetches the **owning app's** secret from KV at request time (`Services/SpeAdmin/SpeAdminTokenProvider.cs:130-135`), builds per-owning-app CCA (`:137-138, 292-306`), OBO with scope `api://{OwningAppId}/.default` (`:142-149`), 55-min in-memory cache keyed `configId:SHA256(userToken)` (`:115-128,:166-169`).
- Secret NAMES are **data-driven** (stored in Dataverse rows; name format validated at `Api/SpeAdmin/ConfigEndpoints.cs:31,241-247,468-480`; DTO fields `Models/SpeAdmin/ConfigDtos.cs:90-96`).

### B.8 CIAM tenant Graph (external-user provisioning)

`CiamGraphClientFactory` — app-only client-credentials against the **CIAM authority**, credential = **certificate** whose PFX is fetched from KV by name `Ciam:GraphProvisioner:CertificateName` via the shared `SecretClient`, private key ephemeral in-process (`Infrastructure/Graph/CiamGraphClientFactory.cs:66-78 (config), 127-133 (CCA `.WithCertificate`), 154-167 (KV PFX load)`). App-reg = "Spaarke CIAM Graph Provisioner" IN the CIAM tenant — distinct from the CIAM token-validation audience (`appsettings.template.json:44,51-54`). Registered singleton at `Infrastructure/DI/ExternalAccessModule.cs:97`. No OBO on this path ever (A1 broker-only, `CiamGraphClientFactory.cs:20-24`).

### B.9 Power BI (Reporting)

`ReportingEmbedService` + `ReportingProfileManager` build their own CCA from `PowerBi:ClientId` / `PowerBi:ClientSecret` / authority (`Api/Reporting/ReportingEmbedService.cs:76-81`; `ReportingProfileManager.cs:71-77`; options `Api/Reporting/PowerBiOptions.cs:23,36-45`). **Which app-reg `PowerBi:ClientId` points to (BFF app-reg vs a dedicated Power BI SP) is not determinable from code/docs — needs portal confirmation** (kill-switch-gated options, deferred validation per `ConfigurationModule` — cited in task 061 POML:25).

### B.10 Service Bus / Redis (auth-adjacent)

Connection-string secrets (`ServiceBus-ConnectionString`, `Redis-ConnectionString` KV refs — `appsettings.template.json:16-17,31`); one MI-based exception: `MembershipJunctionUpdaterHost` uses `ServiceBusClient(namespace, new DefaultAzureCredential())` — unpinned (`Services/Ai/Membership/MembershipJunctionUpdaterHost.cs:100-120`; UAMI has SB Data Sender/Receiver roles per `projects/messaging-communication-app-r3/notes/task-060-notes.md:30`).

---

## C. IDENTITIES (the principals)

| Identity | IDs (dev unless noted) | Role in the system | Evidence |
|---|---|---|---|
| **BFF app registration** ("SPE BFF API") | clientId `1e40baad-e065-4aea-a8d4-4b7ab273458c`, tenant `a221a95e…` | (a) Inbound token audience `api://1e40baad…`; (b) **OBO confidential client** (Graph, Dataverse, Agent); (c) **until #3b**: the app-only Dataverse identity for camps 1/2/3/5 (Dataverse Application User w/ SysAdmin); (d) legacy app-only Graph when MI flag off | `auth-azure-resources.md:288-295,317-328,334-337,349`; `GraphClientFactory.cs:66-90`; §B.2 |
| **App Service managed identity — GAP #4 RESOLVED: USER-ASSIGNED (UAMI), not system-assigned** | dev: `mi-bff-api-dev`, clientId `5967251e-171c-46fe-a6c2-ef843c90309d`, principalId `9fd47efb-7962-492b-ac44-e5ccd0268ebb`; demo: `mi-bff-api-demo`, clientId `b0ce4ca4-…`, principalId `eaf9591e…` | All MI outbound: Graph app-only, `Services/Ai` Dataverse, Cosmos, KV, Content Safety, AI Search, Service Bus (partial), OpenAI (when not on E-2 key) | Runbook mandates UAMI (`auth-deployment-setup.md:50-52,63-67,151-166`); **"No system-assigned identity"** verified live (`projects/messaging-communication-app-r3/notes/task-060-notes.md:29`; `projects/spaarke-ai-azure-setup-dev-r1/current-task.md:122`); ADR-028 E-2 names `mi-bff-api-dev` objectId `9fd47efb…` (`ADR-028:109`) |
| **The `56ae2188…` principal (STALE-DOC identity)** | objectId `56ae2188-c978-4734-ad16-0bc288973f20`, AppId `6bbcfa82-…` | The 2026-05-19 Phase-C-era "BFF MI service principal" that received the original **11 Graph app-role grants** and is named in the Dataverse-app-user comment. It is NOT the current dev UAMI (`9fd47efb`). Its references are drift from before the 2026-05-24 Linux/multi-identity migration. | Grants + identity table: `projects/spaarke-auth-v2-and-hardening/current-task.md:109,204`; stale references: `appsettings.template.json:79`, `docs/architecture/auth-azure-resources.md:274-284` ("Type: System-assigned"), `src/server/api/Sprk.Bff.Api/CLAUDE.md:111` |
| **Per-customer SPE container-type app-regs** | data-driven (Dataverse `sprk_specontainertypeconfig` rows: `ClientId`, `TenantId`, `SecretKeyVaultName`, `OwningAppId`, `OwningAppSecretName`) | Per-customer container-type management + owning-app OBO (E-1 exception) | §B.7 sites |
| **External CIAM app-regs (2)** | (a) CIAM token-validation audience `Ciam:ClientId`/`Ciam:Audience`; (b) "Spaarke CIAM Graph Provisioner" `Ciam:GraphProvisioner:ClientId` + KV certificate | (a) inbound external scheme; (b) app-only cross-tenant user provisioning | `AuthorizationModule.cs:46-58`; `CiamGraphClientFactory.cs:66-78`; `appsettings.template.json:43-54` |
| **Vestigial Dataverse S2S app-reg** | dev: "DSM-SPE Dev 2" `170c98e1-d486-4355-bcbe-170454e0207c`; prod pattern `spaarke-dataverse-s2s-prod` | **ZERO code consumers** (grep `Dataverse-S2S|DATAVERSE_CLIENT_ID|spaarke-dataverse-s2s` across `src/` → none). Lives only in `scripts/Register-EntraAppRegistrations.ps1:8,81,435-509`, `scripts/Test-EntraAppRegistrations.ps1:268-331`, docs. Task 060 drops it. | §E row; `auth-azure-resources.md:825-853` |
| PCF/client app-reg | `5175798e-f23e-41c3-b09b-7a90b9218189` | client-side MSAL only (not a BFF credential) | `auth-azure-resources.md:301-312` |

**GAP #4 verdict — which SP must hold the Graph app-roles**: the **UAMI's service principal** (dev `9fd47efb…`, demo `eaf9591e…`) is the principal that authenticates app-only Graph today (`GraphClientFactory.cs:113-132` + pinning via `Graph:ManagedIdentity:ClientId`), so Graph application-role grants MUST sit on it (grant procedure `auth-deployment-setup.md:405-479`; §51: "The UAMI's principalId is what gets registered in Dataverse Application User (§6) and granted Graph app roles (§5)"). **Unresolved / portal-confirmation needed**: the 2026-05-19 cutover granted the 11 roles to `56ae2188…` (`spaarke-auth-v2-and-hardening/current-task.md:109`), while later work granted `Mail.Send` "+ 5 other roles" to `9fd47efb…` (`projects/sdap-bff-api-remediation-fix/tasks/025-email-send-403-followup.poml:57`). Whether the full 11-role set was ever fully replicated onto `9fd47efb…`, and whether `56ae2188…` still exists with stale grants, cannot be determined from the repo — task 062's live census (its step 1) must resolve this against the UAMI SP.

---

## D. SECRETS (Key Vault + config)

Vaults: dev `spaarke-spekvcert` (`ADR-028:110`, `docs/guides/DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md:227`); prod pattern `sprk-platform-{env}-kv` (`auth-azure-resources.md:814,837`). Canonical secret inventory: `auth-deployment-setup.md:388-397`.

| KV secret (name) | Config key(s) that surface it | Read where | Notes |
|---|---|---|---|
| **`BFF-API-ClientSecret`** | `API_CLIENT_SECRET`, `AzureAd__ClientSecret`, `Graph__ClientSecret`, `Dataverse__ClientSecret`, `AgentToken__ClientSecret` — **five keys, one secret** (`scripts/Configure-ProductionAppSettings.ps1:69-81`; `scripts/Reconcile-DemoEnvironment.ps1:76`; `appsettings.template.json:80,310`; `docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md:776-792`; `docs/guides/CONFIGURATION-MATRIX.md:319`) | See dependency graph §E | The BFF app-reg's client secret. ⚠ Doc contradiction: `auth-azure-resources.md:705-708` maps KV name `BFF-API-ClientSecret` to **DSM-SPE Dev 2** (`170c98e1`) while `:349` + all deployment guides treat it as the BFF app-reg (`1e40baad`) secret. Code requires it to match `API_APP_ID` for OBO (`GraphClientFactory.cs:83-90`), and OBO works (smoke `auth-deployment-setup.md:713`) ⇒ `:705-708` is almost certainly stale — **portal confirmation required before any rotation/removal automation** |
| `Dataverse-ClientSecret` | `Dataverse:ClientSecret` (in envs NOT following the current template; the template points this key at `BFF-API-ClientSecret` instead — `appsettings.template.json:78-80`) | `DataverseWebApiService.cs:40,51-56`; `[Required]` at `Configuration/DataverseOptions.cs:32` + ValidateOnStart (`ConfigurationModule` per task 061 POML:25) | "No longer consumed… can be removed" per `SECRET-ROTATION-PROCEDURES.md:39` / `DATAVERSE-AUTHENTICATION-GUIDE.md:15` — **that is DRIFT**: the config key is hard-required and consumed (see §E). Note the KV *ref target* in the live template is `BFF-API-ClientSecret`, so the separate KV secret `Dataverse-ClientSecret` may already be value-orphaned in template-conformant envs (portal check) |
| `Dataverse-S2S-ClientId`, `Dataverse-S2S-ClientSecret` | none (scripts only) | `scripts/Register-EntraAppRegistrations.ps1:497-509`, `scripts/Test-EntraAppRegistrations.ps1:272-277,328-331`; rotation `docs/guides/SECRET-ROTATION-PROCEDURES.md:58,241-251` | **Zero `src/` consumers** — task 060's removal target |
| `Dataverse-ServiceUrl` | `Dataverse:ServiceUrl` | `appsettings.template.json:78`; consumed by every Dataverse camp (§B.2) | non-credential but auth-relevant (scope derivation, e.g. `DataverseWebApiService.cs:97`) |
| Per-tenant SPE secrets (data-driven names) + `OwningAppSecretName` | none (names live in Dataverse rows) | `SpeAdminGraphService.cs:4136-4142`; `SpeAdminTokenProvider.cs:135,318-324` | E-1 exception; isolated (§F GAP #3) |
| `ciam-graph-provisioner-cert` (KV **certificate**, retrieved as secret/PFX) | `Ciam:GraphProvisioner:CertificateName` | `CiamGraphClientFactory.cs:154-167` | external-access provisioning credential |
| `Rag:ApiKey` (KV ref in prod) | `Rag:ApiKey` | `AuthorizationModule.cs:73-80` (scheme), enforced at `RagEndpoints.cs:136` | inbound API-key scheme |
| `communication-webhook-signing-key` / `communication-webhook-secret` | `Communication:WebhookSigningKey` / `Communication:WebhookClientState` | `CommunicationEndpoints.cs:295,:997-1004`; `[Required]` `Configuration/CommunicationOptions.cs:45-66`; KV refs `appsettings.template.json:353-355` | inbound webhook HMAC + clientState |
| `compose-webhook-signingkey` / `compose-webhook-clientstate` | `Compose:Webhook:SigningKey/ClientState` | `ComposeEndpoints.cs:213`; template `:388-390` | inbound webhook HMAC |
| `Email-WebhookSigningKey` (+ obsolete `Email-WebhookSecret`) | `EmailProcessing:WebhookSigningKey` | Options only: `Configuration/EmailProcessingOptions.cs:171-192`; **the consuming endpoint (`/api/v1/emails/webhook-trigger`) was REMOVED** (`EndpointMappingExtensions.cs:139-143`, ADR-045) | **likely vestigial** — candidate for 061/034 cleanup (verify no Dataverse Service Endpoint still posts) |
| Tracking-footer HMAC key (name-only config) | `Communication:TrackingFooter:SigningKeySecretName` | `TrackingFooterOptions.cs:43-46`; `TrackingFooterGate.cs:38-46` | key material never in config (ADR-028/NFR-07 pattern) |
| `ContentSafety-ApiKey` | `AiSafety:ContentSafety:ApiKey` | `ContentSafetyAuthHandler.cs:14,72`; template `:280` | optional (MI fallback) |
| `AzureOpenAI-ApiKey` | `AzureOpenAI:ApiKey` | `AiModule.cs:115-122`; `ADR-028:106-113` (E-2) | remove = auto-restore-to-MI (single config change) |
| `ai-openai-key`, `ai-docintel-key`, `ai-search-key`, `PromptFlow-Key`, `BingSearch-ApiKey`, `LlamaParseApiKey`, `AzureAISearchApiKey` | `DocumentIntelligence:*`, etc. | template `:132-150,191-192,251,258,300` | AI data-plane keys (not Entra credentials) |
| `ServiceBus-ConnectionString`, `Redis-ConnectionString`, `AppInsights-ConnectionString` | `ConnectionStrings:*`, `ServiceBus:ConnectionString` | template `:16-17,31,217` | infra secrets |
| `AcsProvisioning` / ACS ingress `?sig=` secret | `Communication:Acs:EventGridIngress` | `AcsEventGridIngressOptions.cs:22-45` | webhook-native control |

---

## E. ⭐ THE DEPENDENCY GRAPH — secret/identity → exhaustive consumers → removal verdict

### E.1 `BFF-API-ClientSecret` (the BFF app-reg secret) — **THE shared secret; 9 code consumers behind 5 config keys**

| Consumer (code path) | Config key read | Flow |
|---|---|---|
| `GraphClientFactory` OBO CCA | `API_CLIENT_SECRET` (`GraphClientFactory.cs:55,70,87-88`) | **OBO Graph — the reason the secret can never be removed pre-redesign** |
| `GraphClientFactory` legacy app-only Graph (MI flag off) | `API_CLIENT_SECRET` (`:138-147`) | local-dev fallback |
| `DataverseServiceClientImpl` (camp 1 — prod `IDataverseService` singleton) | `API_CLIENT_SECRET` (`DataverseServiceClientImpl.cs:43-64`) | app-only Dataverse (**#3b migrates**) |
| `DataverseWebApiClient` (camp 3) | `API_CLIENT_SECRET` preferred (`DataverseWebApiClient.cs:39-45`) | app-only Dataverse (**#3b migrates; needs code change** — secret presence wins over MI) |
| `DataverseAccessDataSource` (camp 5) | `API_CLIENT_SECRET` (`DataverseAccessDataSource.cs:41,53-65`) | app-only **AND** the OBO CCA for user-context authorization (**OBO half must survive #3b**) |
| `DataverseUserClient` (camp 6) | `AzureAd:ClientSecret ?? API_CLIENT_SECRET` (`DataverseUserClient.cs:85,91-96`) | OBO Dataverse (user-context tools) — keep |
| `DataverseWebApiService` (camp 2) | `Dataverse:ClientSecret` — which the live template points at THIS KV secret (`appsettings.template.json:80`; `DataverseWebApiService.cs:40,51-56`) | app-only Dataverse (**#3b migrates**) |
| `AgentTokenService` (Copilot OBO) | `AgentToken:ClientSecret` — KV ref to THIS secret (`appsettings.template.json:310`; `Reconcile-DemoEnvironment.ps1:76`; `AgentTokenService.cs:49-53`) | OBO Graph + Dataverse — keep |
| Inbound Microsoft.Identity.Web (`AzureAd` section) | `AzureAd:ClientSecret` (`Configure-ProductionAppSettings.ps1:75`; runbook `auth-deployment-setup.md:139`) | present in config; JWT *validation* itself doesn't need it, but the section feeds `DataverseUserClient` above |
| `GraphOptionsValidator` | requires `Graph:ClientSecret` **only when MI disabled** (`Configuration/GraphOptionsValidator.cs:20-23`) | startup validation |

**Verdict: NEVER-REMOVE** (blocking consumers: OBO in `GraphClientFactory`, `AgentTokenService`, `DataverseUserClient`, `DataverseAccessDataSource`-OBO). After #3b lands, its *Dataverse-app-only* consumers disappear but the secret itself stays for OBO — exactly the ADR-028 posture (`src/server/api/Sprk.Bff.Api/CLAUDE.md:110,221`). ⚠ Rotation blast radius = all 9 consumers at once; also confirm the `auth-azure-resources.md:705-708` name/app mapping contradiction (portal) before automating anything against this name.

### E.2 `Dataverse-ClientSecret` / config key `Dataverse:ClientSecret`

Consumers: `DataverseWebApiService` (hard-throw if absent, `DataverseWebApiService.cs:51-52`) + startup gate `DataverseOptions.ClientSecret [Required]` + ValidateOnStart (`DataverseOptions.cs:32-33`; wiring per task 061 POML:25 `ConfigurationModule.cs:24-34`). **Verdict: REMOVE-AFTER-#3b** (blocking consumer: `DataverseWebApiService`; removal today = startup crash — the `appsettings.template.json:79` "no-op, safe to remove" comment is **drift**, confirmed by the 2026-08-13 addendum, `notes/deployment-refactors-assessment-2026-08-12.md:117`). The docs claiming it's unconsumed (`SECRET-ROTATION-PROCEDURES.md:39`, `DATAVERSE-AUTHENTICATION-GUIDE.md:15`) are wrong until #3b.

### E.3 `Dataverse-S2S-ClientId` / `Dataverse-S2S-ClientSecret` + app-reg `spaarke-dataverse-s2s-*` (dev: DSM-SPE Dev 2 `170c98e1`)

Consumers: **none in `src/`** (grep `Dataverse-S2S|DATAVERSE_CLIENT_ID|spaarke-dataverse-s2s` across `src/` → zero; hits only in `scripts/Register-EntraAppRegistrations.ps1:497-509,629-640`, `scripts/Test-EntraAppRegistrations.ps1:268-331`, `scripts/README.md:323-324`, docs guides, and GitHub-secret examples `docs/guides/PRODUCTION-DEPLOYMENT-GUIDE.md:1319-1357` / `SPAARKE-DEPLOYMENT-GUIDE.md:1634-1660` where `DATAVERSE_CLIENT_ID` is a CI pipeline secret for `pac` auth, not BFF runtime). Consolidated away 2026-01-07 (`auth-azure-resources.md:402-407`). **Verdict: SAFE-TO-REMOVE-NOW** (task 060), with two riders: (a) the CI-workflow `DATAVERSE_CLIENT_ID` GitHub secret used by `pac` deployment docs is a *separate* consumer class — 060 must check `.github/workflows/` before deleting the *app-reg* itself (docs suggest pipelines authenticate `pac` with it); (b) fix the `auth-azure-resources.md:705-708` stale mapping row in the same PR.

### E.4 Per-tenant SPE secrets (data-driven names) — **fully isolated**

Consumers: `SpeAdminGraphService.CreateClientAsync`/SharePoint token (`SpeAdminGraphService.cs:380-415,4053-4054,4177-4184`), `SpeAdminTokenProvider` OBO (`SpeAdminTokenProvider.cs:130-149`). Names come from Dataverse rows, not from appsettings/provisioning scripts; the only shared infrastructure is the `SecretClient` + vault URI (`SpeAdminModule.cs:39-44`). **Verdict: NEVER-REMOVE per active tenant.** See GAP #3 (§F).

### E.5 `ciam-graph-provisioner-cert` — sole consumer `CiamGraphClientFactory.LoadCertificateAsync` (`CiamGraphClientFactory.cs:154-167`). **Verdict: keep while external-user provisioning is live.** Prereq: UAMI needs KV Secrets User on the holding vault (`appsettings.template.json:44`).

### E.6 Webhook/HMAC keys

- `Communication:WebhookSigningKey`/`WebhookClientState`: consumers `CommunicationEndpoints.cs:295,997-1004`, `GraphSubscriptionManager.cs:64`, `MailboxVerificationService.cs:46`; `[Required]` (`CommunicationOptions.cs:45-66`). **Keep.**
- `Compose:Webhook:SigningKey/ClientState`: `ComposeEndpoints.cs:213,1198`. **Keep.**
- `EmailProcessing:WebhookSigningKey`: **no consuming endpoint remains** (`EndpointMappingExtensions.cs:139-143`); options class only (`EmailProcessingOptions.cs:179-192`, registered `EmailServicesModule.cs:40-41`). **Verdict: REMOVE-AFTER-VERIFICATION** that no Dataverse Service Endpoint still targets the retired route (owner check) — then delete key + option (fold into 061 or 034).
- TrackingFooter signing key (name-only): `TrackingFooterGate.cs:38-46`. **Keep.**

### E.7 The UAMI identity (`mi-bff-api-{env}`) — consumers of the ONE shared identity

Injected singleton `TokenCredential` (`Program.cs:46-48`) → Cosmos (`AiPersistenceModule.cs:66`), OpenAI-MI-mode (`AiModule.cs:127`), Content Safety (`ContentSafetyTokenProvider.cs:51-55`), `Services/Ai` Dataverse camp (per `Program.cs:41-45` comment + addendum), SessionRestore (`Services/Ai/Sessions/SessionRestoreService.cs:364`), EML converter (`Services/Email/EmailToEmlConverter.cs:32`), Registration provisioners (`Infrastructure/DI/RegistrationModule.cs:50`), `DataverseAccessDataSource` fallback (`DataverseAccessDataSource.cs:50,72`). Per-call `DefaultAzureCredential` constructions that resolve to the SAME UAMI only via app settings (`AZURE_CLIENT_ID` / pinned options): `GraphClientFactory.cs:117-132` (pinned), `SpeAdminModule.cs:44` (**unpinned**), `MembershipJunctionUpdaterHost.cs:120` (**unpinned**), `DataverseWebApiClient.cs:50-52` (pinned via `ManagedIdentity:ClientId`). Azure RBAC held (dev, live census 2026-07): 6 assignments — OpenAI User, Cognitive Services User, KV Secrets User, SB Data Sender + Receiver, Search Index Data Contributor (`messaging-communication-app-r3/notes/task-060-notes.md:30`). **Verdict: NEVER-REMOVE; every grant change must be replayed on BOTH env UAMIs; the 5 App-Service keys (`Graph__ManagedIdentity__ClientId`, `ManagedIdentity__ClientId`, `AZURE_CLIENT_ID`, `UAMI_CLIENT_ID`, +Enabled flag) must stay in lock-step (`auth-deployment-setup.md:151-166`).**

### E.8 The BFF app-reg identity (`1e40baad…`) — beyond its secret

Consumers of the *identity* itself: inbound audience (`AzureAd` section, `appsettings.template.json:36-41`), all OBO CCAs (§E.1), Dataverse Application User (`auth-azure-resources.md:294`), delegated-permission holder (Graph delegated + Dynamics `user_impersonation`, `Register-EntraAppRegistrations.ps1:85-94`), Graph **application**-permission source-of-truth list for §5 replication (`auth-deployment-setup.md:405-447`). **Verdict: NEVER-REMOVE.** After #3b, its Dataverse Application User registration can be *narrowed* (it remains needed for OBO-user flows' app user? — no: OBO acts as the *user*; the app user is for app-only. Post-#3b the app-reg's Dataverse app user is retirable **after** verifying no residual app-only caller — a task-011-design decision; do not fold into 060).

---

## F. GAP resolutions

### GAP #1 — OBO internals: RESOLVED

Five OBO construction sites, all `ConfidentialClientApplication` + `AcquireTokenOnBehalfOf`, all `.default` scopes:
1. `GraphClientFactory.cs:83-90` (build) / `:225-228` (exchange, `https://graph.microsoft.com/.default`) — Redis cache via `GraphTokenCache` 55-min TTL (`:203-234`; cache impl `Services/GraphTokenCache.cs:19-26`, key `sdap:graph:token:{sha256}` per `Infrastructure/Cache/SystemCacheKeys.cs:75`).
2. `DataverseAccessDataSource.cs:59-63` / `:118-121` (`{org}/.default`).
3. `DataverseUserClient.cs:91-96` / `:178` (`{env}/.default`; static CCA cache `:55-56`).
4. `AgentTokenService.cs:49-53` / `:92-95` (Graph) + `:158-165` (Dataverse) — Redis via `ITenantCache`.
5. `SpeAdminTokenProvider.cs:292-306` / `:142-149` (`api://{owningApp}/.default`) — per-customer secrets, NOT the BFF secret.

Shared config with the r3-changed paths: sites 1–4 all read the **same** `BFF-API-ClientSecret` value (§E.1). **023** adds `.RequireAuthorization()` only — touches no OBO construction; the OBO handlers behind those endpoints keep working for workforce callers (their bearer reaches the handler unchanged). **060** touches scripts/docs/KV `Dataverse-S2S-*` only — zero overlap with any OBO site. **#3b** must remove only the *app-only* usages of the secret (camps 1/2/3 + camp-5 app-only half) and explicitly preserve sites 1–5 above; the `DataverseAccessDataSource` dual-role constructor (`:49-77`) is the one file where app-only and OBO share a single `if` branch — #3b needs a surgical split there, not a config flip. Confirmed: **023/060/#3b as scoped do not disturb OBO.**

### GAP #2 — CIAM vs `.RequireAuthorization()`: RESOLVED — no interaction; see §A.6.

### GAP #3 — per-tenant SPE isolation: RESOLVED — isolated, one shared seam

Per-tenant secrets are named in Dataverse rows and fetched at runtime (§B.7/§E.4); they never appear in `appsettings*`, `Register-EntraAppRegistrations.ps1`, or the `Dataverse-S2S-*` namespace — so **task 060 cannot touch them**, and KV-federation work (#1 / task 017) does not reference them **except** through the one shared seam: the singleton `SecretClient` bound to `SpeAdmin:KeyVaultUri ?? KeyVaultUri` (`SpeAdminModule.cs:39-44`). Any #1 remediation that moves/renames the vault or changes the `SecretClient` registration MUST treat SpeAdmin per-tenant secrets + the CIAM provisioner cert + `KnowledgeDeploymentService` + TrackingFooter keys as co-tenants of that client (precondition for task 017's design). ADR-028 E-1 stands (`ADR-028:100-104`).

### GAP #5 — token/scope/caching: RESOLVED

- **Scopes**: uniformly `.default` (Graph `GraphClientFactory.cs:151-154,226`; Dataverse `{org}/.default` derived from ServiceUrl — `DataverseWebApiService.cs:97`, `DataverseWebApiClient.cs:91`, `DataverseAccessDataSource.cs:47`, `AgentTokenService.cs:158`; SpeAdmin `api://{owningApp}/.default` `SpeAdminTokenProvider.cs:142`). Rationale for `.default` on OBO documented at `GraphClientFactory.cs:218-224` (individual scopes → AADSTS70011).
- **Caches**: (a) OBO Graph tokens → **Redis**, 55-min TTL, keyed SHA-256(inbound token) (`GraphClientFactory.cs:203-234`); (b) Agent OBO tokens → Redis `ITenantCache` (`AgentTokenService.cs:79-102`); (c) SpeAdmin OBO → in-memory 55-min (`SpeAdminTokenProvider.cs:115-128,166-169`); (d) MSAL-internal app-token caches in every CCA (per-instance; `DataverseUserClient` shares CCA statically across transient clients `:55-56,89-96`; `CiamGraphClientFactory` MSAL cache `:47-50`); (e) manual `AccessToken` fields with 5-min expiry buffer (`DataverseWebApiService.cs:75-100`, `DataverseWebApiClient.cs:69-97`, `DataverseAccessDataSource.cs:84-97`).
- **Correctness/lifetime concerns** (observations, not defects to fix in r3 unless picked up): (i) cached downstream Graph tokens can outlive the *inbound* token's validity/revocation by up to 55 min — accepted ADR-009 trade-off, worth stating in the security sweep (task 030); (ii) raw access tokens at rest in Redis — mitigated by hashing only the *key*, the value is the live Graph token; Redis compromise = token disclosure (030 note); (iii) `DataverseAccessDataSource.EnsureAuthenticatedAsync` mutates the shared typed-HttpClient's `DefaultRequestHeaders.Authorization` (`:92-93`) — scoped lifetime bounds the risk but the pattern differs from the per-request-header discipline used elsewhere (`DataverseWebApiService.cs` comment `:105-108` equivalent); (iv) the JwtBearer PostConfigure idempotency guard is process-static (`AuthorizationModule.cs:24`) — benign in prod, surprises in multi-factory test hosts.

---

## G. PER-TASK IMPLICATIONS

### Task 023 (Finance/@spaarke/auth closure + healthz + OBO/User `.RequireAuthorization()`)
- **Confirmed safe** — §A.6. Bare `.RequireAuthorization()` = DefaultPolicy = workforce default scheme; CIAM tokens 401 (desired, enforces A1 no-OBO-external); Copilot audience unaffected; no fallback policy exists so the explicit attribute is the only (and correct) enforcement layer.
- Precondition already in the POML: migrate/hand-off ALL FOUR web-resource callers before flipping Finance (`023 POML:45`); the map adds nothing blocking.
- New minor input: when hardening `/healthz/dataverse*`, note they execute through `IDataverseHealthService` → `DataverseServiceClientImpl` (camp 1, secret-based) — their behavior is a cheap smoke-probe for #3b later; don't remove them, harden them.
- **No scope/sequence change.**

### Task 060 (drop vestigial S2S app-reg + `Dataverse-S2S-*` KV)
- **Confirmed safe-to-remove-now** per §E.3 — zero `src/` consumers; scripts/docs only.
- **Two additions recommended**: (a) verify `.github/workflows/` for a live `DATAVERSE_CLIENT_ID` GitHub-secret consumer (deployment guides show `pac` pipelines authenticating with it — `SPAARKE-DEPLOYMENT-GUIDE.md:1634-1638`, `PRODUCTION-DEPLOYMENT-GUIDE.md:1319-1327`; if a live workflow uses it, the *app-reg* is not deletable even though the BFF never reads it — the KV secrets still are); (b) while editing `auth-azure-resources.md`, fix the stale `:705-708` mapping (KV `BFF-API-ClientSecret` ↛ DSM-SPE Dev 2) and the stale `:274-284` "Type: System-assigned" MI section — both are on this task's modify list anyway.
- **No scope/sequence change** beyond those riders.

### Task 061 (uniform fail-fast config validation)
Auth-specific inputs from this map:
- **Do NOT** add/strengthen `[Required]` on `DataverseOptions.ClientSecret` (`DataverseOptions.cs:32`) — it is on the #3b removal track; ideally 061 leaves the Dataverse options class untouched and lets 011/#3b relax it (map verdict E.2).
- **`Ciam` section has NO options class at all** — read raw at `AuthorizationModule.cs:48-53`, and the scheme registers **unconditionally**; a missing `Ciam` section today produces a malformed authority that fails at first external request, not at startup. If 061 adds validation, it MUST be classified kill-switch-gated/deferred (envs without CIAM must still boot) — this is precisely the BingGrounding-trap shape (`061 POML:41`).
- `TenantEnvironmentRoutingOptions` is a bare `services.Configure<>` (`AuthorizationModule.cs:166-167`) — a legitimate 061 target (deny-by-design map should fail fast on malformed entries).
- `EmailProcessing:WebhookSigningKey` is consumer-less (§E.6) — classify as removal candidate, don't `[Required]` it.
- `AgentTokenOptions` already has `[Required]` + `IValidateOptions` (`Configuration/AgentTokenOptions.cs:38-39,78-91`) — model to keep; `GraphOptionsValidator`'s required-when-MI-disabled conditional (`GraphOptionsValidator.cs:20-23`) is the canonical "required-when" pattern.
- **No scope/sequence change.**

### Task 062 (GraphAppRoles constants + drift verifier)
- **GAP #4 answer the verifier must encode**: target SP = the **UAMI's service principal** per environment (dev principalId `9fd47efb-7962-492b-ac44-e5ccd0268ebb`, clientId `5967251e-171c-46fe-a6c2-ef843c90309d`; demo principalId `eaf9591e…`, clientId `b0ce4ca4-…`) — NOT `56ae2188…` (retired Phase-C-era principal; grants recorded there in 2026-05 may be stale/orphaned) and NOT only the app-reg SP (`1e40baad…`, which remains the *source-of-truth list* per `auth-deployment-setup.md:447` and holds the delegated scopes). Recommend the verifier accept an SP objectId parameter and the runbook run it against BOTH (app-reg SP as expected-list source; UAMI SP as grant target) — and flag `56ae2188…` for portal investigation/cleanup.
- Live census (062 step 1) must resolve the 11-vs-6 grant discrepancy (§C GAP #4 verdict) — cannot be resolved from the repo.
- **No scope/sequence change.**

### Task 011 / #3b (shared-lib `ClientSecret`→MI migration, NG1 design)
Beyond the addendum's grounding (`deployment-refactors-assessment-2026-08-12.md:112-122`), the map adds four sharpenings:
1. **Config-key removal ≠ secret removal**: `Dataverse:ClientSecret` and `API_CLIENT_SECRET` are (per the live template) the *same KV secret* surfaced twice (`appsettings.template.json:80` vs `Configure-ProductionAppSettings.ps1:69-81`). #3b removes the *Dataverse config keys + code paths*; the KV secret stays for OBO.
2. **`DataverseWebApiClient` needs a code change, not config**: its MI fallback only activates when `API_CLIENT_SECRET` is absent (`DataverseWebApiClient.cs:42`) — and it never will be (OBO). The migration must invert the branch or inject the DI `TokenCredential` (pattern: `GraphClientFactory.cs:104-148` flag-gate, or camp-4 DI injection).
3. **`DataverseAccessDataSource` must be split, not flipped**: the same `clientSecret` branch that builds the app-only credential also builds the OBO CCA (`DataverseAccessDataSource.cs:49-66`). Migrating app-only to the DI credential while keeping `_cca` requires decoupling the two (today the MI branch sets `_cca = null` → "OBO not available", `:73`).
4. **Identity attribution + grants**: post-migration app-only writes attribute to the UAMI app user (dev appId `5967251e…`) — the UAMI must be a Dataverse Application User in BOTH envs (procedure `auth-deployment-setup.md:483-510`; demo precedent registered the UAMI *clientId* `b0ce4ca4…`, `sdap-bff-api-remediation-fix/EXECUTION-LOG.md:608-614`) and needs `prvActOnBehalfOfAnotherUser` where impersonation is used (`DataverseImpersonation.cs:24`, `CommunicationModule.cs:361`). ⚠ The `appsettings.template.json:79` instruction to register PrincipalId `56ae2188…` is **stale** — registering that principal would grant the wrong identity.
- **Sequence unchanged** (MI-verify → migrate → relax `[Required]` → drop keys), but the "MI-verify" step must use the UAMI identifiers above, and 061 should land first or leave Dataverse options untouched (§G-061).

---

## H. CROSS-SLICE RISKS + RECOMMENDATIONS (ranked)

1. **One-secret-five-keys fan-out (`BFF-API-ClientSecret`)** — 9 code consumers behind 5 config keys (§E.1). Any rotation, KV-federation (#1/017), or #3b step that assumes "this key = this consumer" will silently break an unrelated path. *Recommendation*: task 040 rule (c) should flag not just "secret-based Dataverse path" but ANY new reader of `API_CLIENT_SECRET`/`AzureAd:ClientSecret`; task 017's config-architecture design must carry §E.1 verbatim as its secrets baseline.
2. **Three-identity confusion (GAP #4 residue)** — `1e40baad` (app-reg), `9fd47efb`/UAMI (current), `56ae2188` (retired Phase-C principal) appear interchangeably in live docs/comments (`appsettings.template.json:79`, `auth-azure-resources.md:274-284`, BFF `CLAUDE.md:111`, the r3 addendum itself at `:119`). Acting on the wrong one mis-registers the Dataverse app user (#3b) or verifies the wrong SP's Graph grants (062). *Recommendation*: 060 fixes the two doc sites it already touches; 062's census resolves the grant split; the r3 addendum's `56ae2188` reference should be corrected when 011 consumes it (it is stale).
3. **Doc-drift asserting secrets are removable that are load-bearing** — `SECRET-ROTATION-PROCEDURES.md:39` + `DATAVERSE-AUTHENTICATION-GUIDE.md:15` + `appsettings.template.json:79` all say `Dataverse-ClientSecret` is unconsumed; `DataverseWebApiService.cs:51-52` hard-throws without it. An operator following those docs today takes the BFF down at next restart. *Recommendation*: escalate as an immediate small doc fix (or fold into 060's doc pass) — do not wait for #3b.
4. **Unpinned `DefaultAzureCredential` sites depend on the `AZURE_CLIENT_ID` app setting** — `SpeAdminModule.cs:44` and `MembershipJunctionUpdaterHost.cs:120` construct unpinned credentials; on the multi-identity App Service they resolve correctly only because `AZURE_CLIENT_ID={uami}` is set (`auth-deployment-setup.md:153-163`; failure mode documented in `ManagedIdentityCredentialFactory.cs:11-14`). Any #1/017 or provisioning change that drops one of the 5 identity keys re-triggers the 2026-05-24 outage class. *Recommendation*: 017's design should propose routing both sites through `ManagedIdentityCredentialFactory`/the DI `TokenCredential`; 061 should NOT mark the 5 keys individually required without treating them as a set.
5. **`auth-azure-resources.md:705-708` secret-name↔app-reg contradiction** — before ANY automated rotation or 060's KV-deletion checklist is executed by an operator, portal-confirm which app-reg's secret actually populates KV `BFF-API-ClientSecret` in dev (code proves it must match `1e40baad` for OBO to work; the doc says `170c98e1`). *Recommendation*: add as an explicit operator verification line in 060's KV checklist.
6. **CIAM scheme registered unconditionally, config unvalidated** (§G-061) — a mis-provisioned env fails at first external request with a confusing JwtBearer error rather than at startup. *Recommendation*: 061 adds a gated `IValidateOptions`-style check ("if any `Ciam:*` key present, all required ones present"), never a bare `[Required]`.
7. **Vestigial `EmailProcessing:WebhookSigningKey`** (§E.6) — an orphaned inbound-auth secret invites false confidence and pointless rotation. *Recommendation*: verify no Dataverse Service Endpoint still posts to the retired route, then delete option + KV secret (061 or 034).
8. **OBO Redis token cache lifetime/at-rest exposure** (§F GAP #5 i–ii) — accepted ADR-009 design, but task 030 (security sweep) should record the 55-min revocation lag + Redis-compromise disclosure surface as a known, accepted risk with citation, so it stops being rediscovered.

---

### Could NOT be determined from code (needs owner/portal confirmation)

1. Which app-reg's secret value currently sits in KV `BFF-API-ClientSecret` (dev) — code implies `1e40baad`; `auth-azure-resources.md:705-708` says `170c98e1` (risk #5).
2. Whether the full 11 Graph app-role set was replicated from `56ae2188…` to the UAMI SP `9fd47efb…`, and whether `56ae2188…` still exists with orphaned grants (062 census).
3. Whether any live GitHub Actions workflow still uses the `DATAVERSE_CLIENT_ID`/`DATAVERSE_CLIENT_SECRET` repo secrets for `pac` auth (task 060 rider).
4. Which service principal `PowerBi:ClientId` refers to (dedicated Power BI SP vs BFF app-reg) — §B.9.
5. Whether any Dataverse Service Endpoint still posts to the retired `/api/v1/emails/webhook-trigger` route (E.6 removal precondition).
6. Whether the separate KV secret `Dataverse-ClientSecret` still exists with a distinct value in any env (template-conformant envs point the config key at `BFF-API-ClientSecret` instead — §D row 2).

---

## Portal verification — dev vault `spaarke-spekvcert` (owner-provided secret list, 2026-08-13)

Confirmation items resolved + KV drift surfaced by inspecting the live dev vault secret list:

- **Item B RESOLVED — no separate `Dataverse-ClientSecret` secret exists** in dev; the `Dataverse:ClientSecret` config key points at `BFF-API-ClientSecret` (template). Task 060's KV cleanup has no distinct `Dataverse-ClientSecret` to remove in dev. (`Dataverse-S2S-*` also absent from dev — 060's target may already be dev-absent; check prod.)
- **⚠️ HYGIENE-1 (live rotation hazard) — duplicate BFF secret across casing**: BOTH `BFF-API-ClientSecret` (PascalCase; the main BFF path, 15 refs) AND `bff-api-client-secret` (lowercase; the **Office add-in** deploy maps `AzureAd:ClientSecret` → `…/secrets/bff-api-client-secret/` — `projects/sdap-office-integration/DEPLOYMENT-PLAN.md:143,239`) exist as separate KV secrets, presumably same value/identity (`1e40baad`). Rotating one leaves the other stale → the Office-addin OR the BFF path breaks. This is a 10th consumer path missed by §E's graph (via the lowercase alias). **De-risk: consolidate to one canonical secret + one casing before any rotation automation.**
- **HYGIENE-2 (orphaned) — `Graph-API-ClientSecret`**: exists in KV but has ZERO code/config consumers (only a redis-project baseline NOTE lists it); the `Graph__ClientSecret` config key resolves to `BFF-API-ClientSecret`. Orphaned/legacy — cleanup candidate; confusion risk.
- **HYGIENE-3 (orphaned) — MI-clientid duplicates**: `MANAGED-IDENTITY-CLIENT-ID` is LIVE (32 refs); `SPRK-MANAGED-IDENTITY-CLIENT-ID` and `UAMI-ClientId` have **0 refs** (orphaned duplicate naming). Consolidate.

**Routing**: HYGIENE-1/2/3 are KV-secret-drift findings for the **config-deployment assessment (task 017 / #1)** — its secrets baseline must include this vault census + a consolidation recommendation. HYGIENE-1 (duplicate BFF secret) is the most consequential and should be flagged in task 060's operator note + the #3b (task 011) plan, since any secret-touching step must account for the lowercase Office-addin alias. Item A (BFF-API-ClientSecret = `1e40baad`, not `170c98e1`) already resolved via config/environments.json.
