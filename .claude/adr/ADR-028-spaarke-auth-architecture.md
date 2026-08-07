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
- **MUST NOT** instantiate `PublicClientApplication` directly outside `@spaarke/auth` (internal **Xrm** surfaces). The **collaboration hosts are exempted** — the external SPA and the Teams tab share **one standalone-MSAL module** (direct `PublicClientApplication` + per-tab `sessionStorage` isolation) with a **pluggable authority**: **Entra External ID (CIAM)** for the external SPA (**Amendment A1**) and **workforce Entra (multitenant, Teams SSO/NAA)** for the Teams tab (**Amendment A2**). Neither uses workforce B2B guests. All A1 isolation invariants are preserved for both hosts.
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

## Amendment A2 (2026-08-03): Workforce Auth for the Teams Collaboration Host

> **Status**: Accepted (resolution path **B — amendment**, per root CLAUDE.md §6.5). **Driver project**: `teams-app-r1`. **Builds on Amendment A1.** Full draft + rationale: [`projects/teams-app-r1/adr-028-amendment-draft.md`](../../projects/teams-app-r1/adr-028-amendment-draft.md).

**Why**: A1 sanctions a *CIAM* standalone SPA. `teams-app-r1` extends the same **collaboration product line** to a second host — a Microsoft **Teams tab / personal app** — with two properties A1 does not yet cover: (1) a **workforce-Entra-authenticated standalone (non-Xrm) app** — the Teams host authenticates the user's **workforce** identity via **Teams SSO / NAA** (multitenant) inside a non-Xrm app, which `@spaarke/auth` (Xrm-context-bound + MSAL v3) cannot serve; and (2) **one shared standalone-MSAL module with a pluggable authority** — the external SPA (CIAM) and the Teams host (workforce) are the same collaboration core over two hosts, sharing one auth module whose authority is config-driven (CIAM `*.ciamlogin.com` vs workforce `login.microsoftonline.com`, multitenant). The BFF **already** runs a workforce default JwtBearer scheme; A2 sanctions the workforce **client** plane for the collaboration line — it does **not** add a new IdP.

### New MUST rules (collaboration Teams host only)

- **MUST** authenticate Teams-host collaboration users with their **workforce Microsoft Entra identity** via Teams SSO / NAA against a **multitenant** app registration (per-customer admin consent). CIAM is not used inside Teams.
- **MUST** serve both the external SPA (CIAM) and the Teams host (workforce) from **one shared standalone-MSAL module** whose **authority is config-driven / pluggable**; the module is exempt from the `@spaarke/auth`-only rule exactly as the A1 SPA is.
- **MUST** keep the collaboration surface **broker-only** in both hosts (A1 invariant): the user token authenticates to the BFF **only** and **MUST NOT** be exchanged for a downstream Graph/SPE/Dataverse token (**no OBO on the collaboration path**). Document content streams **app-only**.
- **MUST** resolve the workforce-authenticated caller to a **principal** — a `systemuser` (→ ADR-034 membership) or, for a non-systemuser, a `contact` (→ **contact-anchored membership**, see the ADR-034 cross-reference below) — and enforce authorization server-side via the **accessible-record-set** check. No Dataverse seat / OBO is required for read/download.

### New MUST NOT rules

- **MUST NOT** attempt CIAM / External-ID sign-in inside the Teams host (Teams is a workforce-identity host; a second in-tab login is an anti-pattern).
- **MUST NOT** route the collaboration hosts through `@spaarke/auth` while it remains Xrm-bound + MSAL v3; the shared standalone-MSAL module is the sanctioned surface until a future consolidation (MSAL v3→v5 across the internal estate) is undertaken under a superseding amendment.

### Scope + preserved invariants

- **Collaboration hosts only.** A2 widens the A1 exemption from "external SPA" to "the **collaboration hosts** (external SPA + Teams tab)" with a **pluggable authority** (CIAM *or* workforce-multitenant). **Internal Xrm surfaces (`@spaarke/auth`, PCFs, Code Pages) are UNAFFECTED** — `@spaarke/auth` remains **canonical** for them; the collaboration line is the explicit, bounded exception, not a replacement.
- **All A1 invariants preserved + unweakened**: direct `PublicClientApplication`, per-tab `sessionStorage` isolation, the Bearer-literal allowlist, and broker-only (no OBO; app-only SPE/Dataverse) all carry over to the workforce plane verbatim. A2 widens; it does not weaken.

### ADR-034 cross-reference (contact-anchored membership entry)

The non-systemuser (contact) principal resolves to membership via an **additive contact-anchored entry** on the existing membership engine (ADR-034 resolution **Path C**): the resolver reuses `MembershipResolverService` / `BuildFetchXml` (which already binds a `ContactId` for Contact-typed descriptors), filtered to the access-conferring `sprk_assigned*` role allowlist. This is a new *entry* path, not a second membership model — ADR-034 is complied with, not amended.

### Alternatives considered (and rejected)

- **CIAM inside the Teams tab** (reuse A1 unchanged) — rejected: Teams runs as a workforce identity; a separate in-tab CIAM login is double sign-in, is blocked by the desktop client's popup handling, and splits one person across two identities.
- **Fold the collaboration hosts onto `@spaarke/auth` now** — rejected for R1: forces an MSAL v3→v5 migration across the entire internal consumer estate + de-Xrm-ing config resolution (large blast radius); deferred to a future consolidation amendment.
- **Path C (comply within A1)** — not viable: A1 covers a CIAM standalone SPA only; it neither covers a workforce-authenticated standalone app nor the shared pluggable-authority module. Compliance would require *not* building the Teams host.

> **Note**: A separate full `docs/adr/ADR-028-*.md` does not currently exist (the "Full version" links are aspirational); this concise ADR is the canonical ADR-028 and carries Amendments **A1 and A2**. (A2 was applied concise-only, mirroring how A1 was applied — task 002 prescriptive step 3 targeted a `docs/adr/` full copy that does not exist; creating one was declined as scope creep contradicting this note.)

## Amendment A3 (2026-08-06): Dual-Plane Module-Host Platform + Principal-Agnostic Endpoints (canonical)

> **Status**: Accepted (resolution path **B — amendment**, per root CLAUDE.md §6.5). **Driver project**: `spaarke-SPA-external-access-platform-r2`. **Builds on Amendments A1 + A2.** Ratifies shipped reality (teams-app-r1 FR-22 delivery: `CallerPrincipalResolver` + `ExternalCollaboration` dual-scheme; full BFF suite 9761 pass / 0 fail, CIAM preserved byte-for-byte). Source content: R2 `spec.md` ADR Tensions row for ADR-028(+A1) + FR-22; delivery record [`projects/spaarke-SPA-external-access-platform-r2/notes/r2-coordination-response.md`](../../projects/spaarke-SPA-external-access-platform-r2/notes/r2-coordination-response.md).

**Why**: A1 sanctioned a *CIAM standalone SPA*; A2 widened that to *"the collaboration hosts (external SPA + Teams tab)"* as a two-host product line. R2 makes the next generalization: the same broker-only, dual-plane surface becomes a **module-host SPA platform** serving **every non-core (SPA) user** — outside counsel (CIAM) and internal unlicensed workforce (workforce SSO) alike — where capabilities are **modules** registered behind **one principal-agnostic endpoint group**. teams-app-r1 already **shipped** the mechanism A3 ratifies: a `CallerPrincipalResolver` that selects an identity *plane* from the cryptographically-validated token and hands every endpoint a uniform `CallerPrincipal`, so handler bodies are identical across planes and a new plane (or module) plugs in without touching routes or handlers. A3 does **not** introduce a new IdP or a new client — it declares the **shipped principal-agnostic endpoint pattern** and the **dual-plane external-app model** to be the **canonical platform pattern** for all future SPA modules, so downstream R2 auth code (tasks 013 auth bootstrap, 015 module-framework generalization) builds against an authoritative rule set rather than re-deriving it.

### New MUST rules (module-host platform surface)

- **MUST** treat the **dual-plane external-app model** as canonical for the module-host SPA: a caller is authenticated on exactly **one** plane — **CIAM** (external, `*.ciamlogin.com`) **or** **workforce Entra** (internal, incl. license-free workforce) — selected by the validated token, never by client-supplied input. The plane is an authentication detail; **module authorization is plane-agnostic**.
- **MUST** authorize module endpoints through the **principal-agnostic resolver pattern**: the `ExternalCollaboration` dual-scheme policy (`AuthPolicies.ExternalCollaboration` = `{ AuthSchemes.Ciam, JwtBearerDefaults.AuthenticationScheme }`, `Infrastructure/DI/AuthorizationModule.cs`) authenticates the caller, and `ICallerPrincipalResolver` (`Infrastructure/ExternalAccess/CallerPrincipalResolver.cs`) resolves them to a single `CallerPrincipal` that carries the Tier-2 accessible-record set. Endpoint handlers **MUST** read `CallerPrincipal` from `HttpContext.Items` (set by the group-level `CallerPrincipalAuthorizationFilter`, ADR-008) and **MUST NOT** branch on plane, scheme, `iss`, or `tid` in handler bodies.
- **MUST** select the plane **only** from cryptographically-validated token claims: CIAM iff the validated token's `iss` is a `*.ciamlogin.com` authority **OR** its `tid` equals the configured `Ciam:TenantId`, otherwise workforce (`CallerPrincipalResolver.DeterminePlane`). Because a token validates against exactly one authority, exactly one scheme succeeds and exactly one plane applies per request — this is the spoof-safe invariant; do not weaken it by reading plane from headers, query, or body.
- **MUST** add a **new identity plane or module** through the shipped extension seam ONLY: register one additional `ICallerPrincipalStrategy` (new `CallerPrincipalPlane` value) + one `DeterminePlane` branch; per-module record-scope is a Tier-2 predicate composed into `CallerPrincipal.ProjectAccess`. Routes, the group filter, and handler bodies stay untouched. This is the canonical way the third plane / next module plugs in.
- **MUST** keep the two entitlement tiers **independent + both server-enforced**: **Tier-1 module entitlement** (which modules a caller may see/route to) is resolved separately from **Tier-2 record scope** (the per-module accessible-record predicate carried on `CallerPrincipal`). Tier-1 is neither implied by authentication nor by Tier-2 access. (The Tier-1 entitlement store + `/me` projection are R2 P2 deliverables — A3 fixes the *invariant*, not the schema.)

### New MUST NOT rules

- **MUST NOT** stand up a **second maintained workforce entry point** for the module-host surface. The workforce and CIAM planes share the **one** `ExternalCollaboration`-guarded group; the transitional `/api/v1/collab/*` group is slated for removal (teams-app-r1 §7) and **MUST NOT** be extended or given new callers.
- **MUST NOT** exchange the caller's token for a downstream Graph/SPE/Dataverse token on **either** plane (**broker-only / no-OBO preserved from A1/A2 across the whole module-host platform**). Module document content streams **app-only** (`SpeFileStore` facade, `FileStorageContainer.Selected` + `ReadContent`, ADR-007), with authorization enforced server-side **before** streaming.
- **MUST NOT** infer Tier-1 module entitlement from the fact of authentication, from the identity plane, or from Tier-2 record access; an unentitled module MUST be neither shown nor routable (direct-route denied server-side).
- **MUST NOT** route the module-host SPA through the Xrm-bound `@spaarke/auth` while it remains Xrm-context-bound + MSAL v3; the shared standalone-MSAL module with pluggable authority (A1/A2) is the sanctioned client surface for this platform until a superseding consolidation amendment.

### Scope + preserved invariants

- **Generalizes A2's product-line framing to a platform.** A1 = one CIAM SPA; A2 = the two collaboration **hosts**; A3 = the **module-host platform** (any number of capability modules over the same broker-only dual-plane endpoint group, serving all SPA users). Each amendment **widens, never weakens** its predecessor.
- **All A1 + A2 invariants preserved + unweakened**: direct `PublicClientApplication`, per-tab `sessionStorage` isolation (CIAM path — do NOT switch to `localStorage`/`@spaarke/auth`), the Bearer-literal allowlist, broker-only (no OBO; app-only SPE/Dataverse), and the ADR-034 contact-anchored membership entry all carry over verbatim. The **E-3 direct-Office boundary** (A1) is unchanged: CIAM-only external users still cannot reach user-identity Office/Copilot/Search features.
- **Internal Xrm surfaces are UNAFFECTED**: `@spaarke/auth`, PCFs, and Code Pages remain governed by the base ADR-028 v2 contract; the module-host platform is the explicit, bounded external-app exception, not a replacement for internal auth.

### Alternatives considered (and rejected)

- **Per-plane / per-module duplicated endpoints** (a CIAM handler and a workforce handler per module) — rejected: doubles the auth surface, drifts, and re-creates the `/api/v1/collab` second-entry-point anti-pattern A2/§7 is retiring. The principal-agnostic resolver already erases the need.
- **Leave the pattern as an implementation detail of teams-app-r1** (no A3) — rejected: R2 tasks 013/015 and every future module need an *authoritative* rule set to build against; leaving the canonical pattern undocumented invites each module to re-derive (and diverge on) plane handling. Ratifying shipped reality is exactly the CLAUDE.md §6.5 Path-B case.
- **Path C (comply within A2)** — not viable: A2 covers the *collaboration hosts* and the shared client module, but neither the module-host **platform** generalization nor the **principal-agnostic endpoint pattern** as canonical. Compliance would leave the platform rule set unstated.

> **Note (A3)**: Applied **concise-only** — a full `docs/adr/ADR-028-*.md` does not exist (confirmed 2026-08-06); this concise ADR remains the canonical ADR-028 and now carries Amendments **A1, A2, and A3**. A3 ratifies the teams-app-r1 shipped implementation; it introduces no new IdP, client, or downstream token exchange.

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
