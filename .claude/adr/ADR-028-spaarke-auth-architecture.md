# ADR-028: Spaarke Auth Architecture (v2)

| Field | Value |
|-------|-------|
| **Status** | Accepted |
| **Date** | 2026-05-19 |
| **Supersedes** | Cascade portion of `.claude/patterns/auth/spaarke-sso-binding.md` (now retired). Amends ADR-003, ADR-004, ADR-008, ADR-009 client-side touch points. |
| **Full version** | [docs/adr/ADR-028-spaarke-auth-architecture.md](../../docs/adr/ADR-028-spaarke-auth-architecture.md) |
| **Source design doc** | [AUDIT-FINDINGS-AUTH-SYSTEM.md](../AUDIT-FINDINGS-AUTH-SYSTEM.md) — the audit that motivated v2 |

## Decision

Adopt **function-based auth as the only public contract** at every consumer boundary. Eliminate snapshot patterns. Standardize on managed identity for server outbound. Formalize named auth schemes + per-request audit enrichment.

## Constraints

### MUST

- **MUST** use `useAuth()` (React hook) or `authenticatedFetch` (direct) from `@spaarke/auth` for all BFF calls
- **MUST** call `getAccessToken()` per request (NOT snapshot once); MSAL.localStorage handles cross-tab sharing
- **MUST** use tenant-specific Azure AD authority (NOT `common` or `organizations`) — resolved via `sprk_TenantId` env var → Xrm frame-walk fallback
- **MUST** preserve INV-1..INV-8 MSAL configuration invariants (see `.claude/patterns/auth/spaarke-sso-binding.md`)
- **MUST** rebuild AND redeploy every consumer of `@spaarke/auth` when the library version changes (INV-8 "Bundling Reality")
- **MUST** use `DefaultAzureCredential` (managed identity) for all server outbound — Graph app-only, Dataverse service identity, Cosmos, Key Vault. NOT `ClientSecretCredential`. Documented exceptions: (1) Per-tenant SpeAdmin container-type ops (per-customer secrets, BFF MI cannot impersonate); (2) **Azure OpenAI / AI Services data plane** — see "Documented MI exceptions" below.
- **MUST** validate inbound webhooks via HMAC-SHA256 signature header — fail-closed if signing key missing
- **MUST** route admin + bulk API key endpoints through named `AuthenticationHandler<>` schemes (`AuthSchemes.BuilderAdminApiKey`, `AuthSchemes.RagApiKey`) with `CryptographicOperations.FixedTimeEquals` compare
- **MUST** enrich every authenticated server log with `oid`, `appid`, `obo`, `tenantId`, `correlationId` via `ILogger.BeginScope` (AuditEnrichmentMiddleware)

### MUST NOT

- **MUST NOT** add `accessToken: string` or `token: string` props/fields anywhere in client code (except where required by third-party SDK contracts — Power BI `IReportEmbedConfiguration`, MSAL.js result objects — these are NOT Spaarke BFF tokens)
- **MUST NOT** use `useState`/`useEffect` to snapshot a token (root cause of the 401-after-refresh bug v2 fixes)
- **MUST NOT** write raw `fetch(url, { headers: { Authorization: \`Bearer ${...}\` } })` template literals — use `authenticatedFetch`. Limited D-AUTH-7 exceptions: SSE (EventSource ReadableStream), XHR uploads, Dataverse Web API direct calls (BFF-scoped wrapper would route wrong host), External SPA out-of-scope. Each carries `// Auth v2 (D-AUTH-7):` justification comment.
- **MUST NOT** instantiate `PublicClientApplication` directly outside `@spaarke/auth` (internal surfaces). External Workspace SPA is exempted — the external portal uses direct MSAL + sessionStorage for per-tab isolation and authenticates against the **Entra External ID (CIAM) authority** per **Amendment A1** (not workforce B2B)
- **MUST NOT** reference removed symbols: `BridgeStrategy`, `XrmStrategy`, `window.__SPAARKE_BFF_TOKEN__`, `tokenBridge.ts`, `publishToken`, `bffAuthProvider` (deleted in v2)
- **MUST NOT** add `/debug/*` endpoints on the BFF (all removed in v2)
- **MUST NOT** add plaintext secrets to `appsettings*.json` — Key Vault references only (production); dev OK with plain values

## Amendment A1 (2026-07-19): Entra External ID for the External Portal Surface

> **Status**: Accepted (resolution path **B — amendment**, per root CLAUDE.md §6.5). **Driver project**: `spaarke-SPA-external-access-platform-r1`. **Owner-accepted** 2026-07-19. Full draft + rationale: [`projects/spaarke-SPA-external-access-platform-r1/adr-028-amendment-draft.md`](../../projects/spaarke-SPA-external-access-platform-r1/adr-028-amendment-draft.md).

**Why**: Azure AD B2C is end-of-sale; Entra External ID (CIAM) is the successor. The external portal is migrating off Power Pages + B2B guests to a custom SPA (Azure Static Web Apps) + Entra External ID. A Phase-0 spike (GREEN) established the portal is a **pure BFF-broker** — the external user's identity never reaches SPE/Graph; all external-surface SPE + Dataverse access is app-only / managed identity — so a CIAM identity used only to authenticate to the BFF is sufficient and **no per-external-user workforce B2B guest is required** for document read/download.

### New MUST rules (external portal surface only)

- **MUST** authenticate external portal users against a dedicated **Entra External ID (CIAM) tenant** authority (`*.ciamlogin.com`), distinct from the workforce tenant, validated by a **second JwtBearer scheme** pinned to the `/api/v1/external` endpoint group. This supersedes the B2B-guest identity model for the external-SPA surface.
- **MUST** resolve the CIAM-authenticated caller to a Dataverse `Contact` by **stable `oid`** (`sprk_externalobjectid`), and enforce authorization server-side via `sprk_externalrecordaccess` (three-plane model). Downstream authorization is **unchanged**.
- **MUST** keep all external-surface SPE + Dataverse access **app-only / managed identity** (BFF-brokered). The external user's token is used **only** to authenticate to the BFF and **MUST NOT** be exchanged for a downstream Graph/SPE/Dataverse token (**no OBO on the external path**). When document content is exposed, the BFF **MUST** stream it app-only via `FileStorageContainer.Selected` + `ReadContent`.
- **MUST**, when external self-registration is eventually enabled, use Entra External ID **self-service sign-up user flows** (R1 is admin-initiated only; sign-up is disabled via `isSignUpAllowed=false` until a future project enables it).

### New MUST NOT rules

- **MUST NOT** require or provision a per-external-user Entra **B2B guest** object in the workforce tenant for document read/download (eliminated by the broker-only design).
- **MUST NOT** federate the External ID tenant back to internal/workforce identities in a way that reintroduces cross-tenant guest coupling, without a further amendment.

### Documented boundary (limitation E-3)

**Direct-Office features for external users** — Word/Excel/PowerPoint **for Web co-authoring**, **desktop open via `webUrl`**, **user-identity Copilot grounding**, and **Microsoft Search** — REQUIRE the user's own workforce identity reaching SPE (OBO/delegated) and are therefore **not available to CIAM-only external users**. These remain **out of scope**. A future project needing them for external users must reintroduce workforce B2B guests for those users and file a superseding amendment.

> **Note**: A separate full `docs/adr/ADR-028-*.md` does not currently exist (the "Full version" links are aspirational); this concise ADR is the canonical ADR-028 and carries Amendment A1.

## Documented MI exceptions

When MI is genuinely unworkable for a specific outbound surface, the **only** sanctioned alternative is a **Key Vault–backed secret reference** (`@Microsoft.KeyVault(SecretUri=...)`) — never plain text in App Service config. Each exception is enumerated here with rationale, scope, and a remediation TODO. Adding an exception requires a PR that updates this list.

### E-1: SpeAdmin per-tenant container-type ops

- **Scope**: SpeAdmin endpoints performing per-customer container-type management.
- **Why**: Per-customer secrets; the BFF MI cannot impersonate per-customer admin identities.
- **Remediation TODO**: None (architectural). Tracked as a known design exception.

### E-2: Azure OpenAI / AI Services data plane (2026-05-28)

- **Scope**: `BFF → spaarke-openai-dev` (`kind=AIServices`) chat completions and embeddings. Config: `AzureOpenAI:ApiKey` (KV reference). Code path: `AiModule.BuildInnerClient` chooses `ApiKeyCredential` over `TokenCredential` when this setting is present.
- **Why**: MI auth returned persistent HTTP 401 `PermissionDenied` ("Principal does not have access to API/Operation") for chat completions despite `mi-bff-api-dev` (object id `9fd47efb-7962-492b-ac44-e5ccd0268ebb`) holding **both** `Cognitive Services User` (wildcard data action `Microsoft.CognitiveServices/*`) and `Cognitive Services OpenAI User` at the resource scope. App Insights diagnostic logging (`LoggingTokenCredential`, removed after diagnosis) confirmed the MI token carried correct `oid`, `appid`, `aud=https://cognitiveservices.azure.com`, `iss=https://sts.windows.net/{tid}/`, `tid`, `idtyp=app`, and requested scope `.default`. Direct curl with the same audience using my own bearer token returned HTTP 200 to the same URL. No deny assignments. RBAC propagation past window. MI is healthy on the same App Service for Graph, Dataverse, Cosmos, Key Vault. The community-documented Microsoft escape hatch is API key authentication (see [Microsoft Q&A 2168038](https://learn.microsoft.com/en-us/answers/questions/2168038/how-to-fix-openai-authenticationerror-error-code-4) and adjacent threads).
- **Storage**: `AzureOpenAI-ApiKey` secret in `spaarke-spekvcert` Key Vault. Sourced from the OpenAI account's `key1`. Referenced from App Service via `@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/AzureOpenAI-ApiKey/)`.
- **Rotation**: 90-day cadence (operator responsibility — track alongside other KV-backed secrets in [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md)).
- **Restore-to-MI**: Single config change — clear `AzureOpenAI__ApiKey` app setting; code falls back to `TokenCredential` (MI) automatically. Do this when AIServices-kind MI auth is consistently reliable (track via Microsoft Foundry product updates).
- **Remediation TODO**: Restore MI when reliable. No filed ticket yet (the failure mode is widely documented but Microsoft has not published a confirmed fix).

## Key Patterns

### Client: function-based auth contract

```typescript
import { useAuth, authenticatedFetch, buildBffApiUrl } from '@spaarke/auth';

// React component
function MyComponent() {
  const { authenticatedFetch, getAccessToken, isAuthenticated, tenantId } = useAuth();
  // authenticatedFetch handles bearer header + 401 retry; getAccessToken only for SSE/XHR
}

// Non-React caller
const response = await authenticatedFetch(buildBffApiUrl(base, '/ai/search/...'));
```

### Client: consumer authInit.ts pattern (REQUIRED to avoid silent runtime failure)

```typescript
// Import alias to dodge name collision with locally-exported async getTenantId
import { getTenantId as getRuntimeTenantId } from "../config/runtimeConfig";

await initAuth({
  clientId: getMsalClientId(),
  tenantId: getRuntimeTenantId(),         // sync function returning string
  bffBaseUrl: getBffBaseUrl(),
  bffApiScope: getBffOAuthScope(),
  proactiveRefresh: true,
  // INTENTIONALLY OMIT authority — library resolves from tenantId
});
```

### Server: managed identity for outbound

```csharp
// Graph app-only
TokenCredential credential = _managedIdentityEnabled
    ? new DefaultAzureCredential()
    : new ClientSecretCredential(...);  // local-dev fallback

// Dataverse — DefaultAzureCredential chains EnvironmentCredential → 
// WorkloadIdentityCredential → ManagedIdentityCredential → AzureCliCredential
```

### Server: PostConfigure idempotency

```csharp
private static int _jwtPostConfigureApplied;
services.PostConfigure<JwtBearerOptions>(opts => {
    if (Interlocked.CompareExchange(ref _jwtPostConfigureApplied, 1, 0) != 0) return;
    // ... merge audiences, chain OnAuthenticationFailed handlers
});
```

## Integration with Other ADRs

- **ADR-003** (Authorization seams) — UNCHANGED. Server-side `IAuthorizationRule` model + `IAccessDataSource` seam still canonical. OBO flow + 55-min cache TTL covered here; v2 only changes the client API surface and adds named API key scheme + HMAC webhook layer alongside.
- **ADR-007** (SpeFileStore facade) — Graph client constructed by `IGraphClientFactory` uses `DefaultAzureCredential` (managed identity) for app-only when `Graph__ManagedIdentity__Enabled=true`; OBO retained for delegated.
- **ADR-008** (Endpoint filters) — UNCHANGED. v2 adds new auth schemes via `AddAuthentication().AddScheme<>()`.
- **ADR-009** (Redis-first caching) — UNCHANGED for server OBO cache. Client-side cache is now in-memory only per `InMemoryCache` wrapper.
- **ADR-012** (Shared components) — Service factories (`createBffDataService`, `createBffUploadService`) accept `authenticatedFetch` from `@spaarke/auth` per the v2 function-based contract.

## Deferred / Out of Scope (deliberate)

- **Task 040** (rotate AzureAd + AgentToken secrets to Key Vault refs) — deferred; dev env has low blast radius. Revisit at prod-readiness planning.
- **Phase D** (CSP middleware, Continuous Access Evaluation, claims hardening for oid-as-canonical-identity, step-up auth, refresh token rotation test) — spun out as `auth-v3-hardening` project. Not blocking v2 deliverables.
- **DPoP, multi-SP privilege separation, HSM-backed keys, cryptographic audit chaining, mobile clients** — out of v2 scope. Evaluated in audit doc §6.
- **B2C portal** — **Azure AD B2C remains out of scope** (end-of-sale). Per **Amendment A1 (2026-07-19)**, **Microsoft Entra External ID (CIAM) is now IN scope for the external portal surface** (see Amendment A1 section).

## Source Documentation

- Full ADR: [`docs/adr/ADR-028-spaarke-auth-architecture.md`](../../docs/adr/ADR-028-spaarke-auth-architecture.md)
- Original audit doc (design rationale): [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../AUDIT-FINDINGS-AUTH-SYSTEM.md)
- Migration project: [`projects/spaarke-auth-v2-and-hardening/`](../../projects/spaarke-auth-v2-and-hardening/)
- MSAL invariants: [`.claude/patterns/auth/spaarke-sso-binding.md`](../patterns/auth/spaarke-sso-binding.md)
- Deployment setup: [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md)
- Operator runbook commits: Phase B (33c91fe6), Phase C Wave 1 (59a9246f), Phase C Wave 2 (c4bb4a4e), Phase C sign-off (939e0392)
