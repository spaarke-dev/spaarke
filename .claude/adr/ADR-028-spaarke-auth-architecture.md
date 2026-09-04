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
- **MUST** use `DefaultAzureCredential` (managed identity) for all server **app-only** outbound — Graph app-only, Dataverse service identity, Cosmos, Key Vault. NOT `ClientSecretCredential`. Documented exceptions: (1) Per-tenant SpeAdmin container-type ops (per-customer secrets, BFF MI cannot impersonate); (2) **Azure OpenAI / AI Services data plane** — see "Documented MI exceptions" below.
- **MUST** authenticate every **confidential client** that acts as the BFF identity (OBO / delegated exchanges, and app-only MSAL clients) with a **secret-free confidential credential** — **Managed Identity as a Federated Identity Credential (MI-FIC, the default)** or a **Key Vault certificate** where MI-FIC's tenancy constraints do not hold. NOT `.WithClientSecret(...)`. `DefaultAzureCredential` **cannot** satisfy this rule — it produces app-only tokens and cannot perform an OBO exchange; the correct mechanism is a **client assertion**. See **Amendment A4**. Transitional exception **E-3** (retained `BFF-API-ClientSecret`) was **CLOSED 2026-08-24** — the secret is deleted from app settings and Key Vault, and the credential order carries no fallback. A `.WithClientSecret(...)` site on the BFF identity is now simply a violation.
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
- **MUST** resolve the workforce-authenticated caller to a **principal** — a `systemuser` (→ ADR-034 membership; ⚠️ **the systemuser derivation is SUPERSEDED by [Amendment A5](#amendment-a5-2026-09-04-workforce-systemuser-root-sets-derive-from-dataverses-impersonated-answer)** — root sets now come from Dataverse's own answer via app-only impersonated read, ∪ contact grants) or, for a non-systemuser, a `contact` (→ **contact-anchored membership**, see the ADR-034 cross-reference below) — and enforce authorization server-side via the **accessible-record-set** check. No Dataverse seat / OBO is required for read/download.

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

## Amendment A4 (2026-08-17): Secret-Free Confidential Credential for OBO and BFF-Identity Clients

> **Status**: Accepted (resolution path **B — amendment**, per root CLAUDE.md §6.5). **Driver project**: [`spaarke-auth-v4-dataverse-MI`](../../projects/spaarke-auth-v4-dataverse-MI/). **Owner-directed** 2026-08-17.
>
> ### ✅ ADOPTION STATUS — **CONFIRMED EMPIRICALLY 2026-08-20**
>
> A4 was accepted on reasoning; it is now **verified on the wire**. `spaarke-auth-v4-dataverse-MI` task 002
> proved, against a real delegated user token on `spaarke-bff-dev/staging`, that the OBO grant succeeds under a
> Managed-Identity-issued client assertion:
>
> | Proven | Detail |
> |---|---|
> | OBO → **Graph / SPE** | full SPE delegated scope set, real IdP exchange |
> | OBO → **Dataverse `user_impersonation`** | **`upn`/`oid` preserved** — row-level authorization still evaluates as the *user*, not app-only |
> | **Long-running OBO** | `InitiateLongRunningProcessInWebApi` → retrieval from cache |
> | **Negative control** | an assertion minted for the wrong identity **fails loudly** at minting time |
>
> **MI-FIC is the adopted credential. The KV-certificate alternative was NOT taken** — it remains sanctioned for
> cases where the same-tenant rule cannot hold (e.g. a cross-tenant Model 2 shape, still unresolved).
>
> Evidence: [`notes/decisions/002-spike-results.md`](../../projects/spaarke-auth-v4-dataverse-MI/notes/decisions/002-spike-results.md) ·
> decision: [`notes/decisions/003-credential-decision.md`](../../projects/spaarke-auth-v4-dataverse-MI/notes/decisions/003-credential-decision.md).
> `.WithClientSecret` remains in place under transitional exception **E-3** until task 033 removes it. Evidence: [`notes/RESEARCH-FINDINGS.md`](../../projects/spaarke-auth-v4-dataverse-MI/notes/RESEARCH-FINDINGS.md) · inventory: [`notes/CREDENTIAL-INVENTORY.md`](../../projects/spaarke-auth-v4-dataverse-MI/notes/CREDENTIAL-INVENTORY.md) · tenancy: [`notes/TENANCY-AND-CREDENTIALS.md`](../../projects/spaarke-auth-v4-dataverse-MI/notes/TENANCY-AND-CREDENTIALS.md).

### Why this amendment exists

The pre-A4 MUST said "use `DefaultAzureCredential` … NOT `ClientSecretCredential`" for **all** server outbound, with no OBO carve-out. That rule was **unsatisfiable for OBO** — `DefaultAzureCredential` produces app-only tokens and cannot perform an On-Behalf-Of exchange — so the OBO paths necessarily violated it, silently and permanently. The consequences were concrete:

1. **Recurring false-positive churn.** `adr-check` (and `adr-aware`) flagged every OBO `ClientSecretCredential` / `.WithClientSecret` as a violation with no sanctioned alternative to move to, so each auth-touching task re-litigated the same finding.
2. **A wrong belief encoded as a rule.** `.claude/constraints/auth.md` asserted *"OBO flow (OAuth spec requires confidential client + secret)."* **This is false.** OAuth requires a confidential **credential**; a secret is one of three ways to satisfy it. That single clause foreclosed the question in every prior auth audit.
3. **An undocumented deviation.** The OBO secret was never entered in this ADR's exception registry, despite the registry's own rule that exceptions be enumerated by PR.

A4 replaces an unsatisfiable rule with the **required shape**, so the question stops being re-opened.

### The required shape

**Two distinct outbound classes, two distinct rules:**

| Class | Mechanism | Credential |
|---|---|---|
| **App-only** (BFF acts as itself): Graph app-only, Dataverse service identity, Cosmos, Key Vault, Service Bus, AI Search | `TokenCredential` → `DefaultAzureCredential` pinned to the UAMI | Managed identity. No app registration involved |
| **Confidential client** (BFF identity as an OAuth client): **OBO / delegated exchanges**, and MSAL app-only clients bound to the app registration | `IConfidentialClientApplication` + **client assertion** | **MI-FIC** (default) or **KV certificate**. **Never a client secret** |

**Default credential — MI-as-Federated-Identity-Credential (MI-FIC).** GA since 2025-05-08. The app registration carries a federated identity credential trusting the App Service's **user-assigned** managed identity; MSAL presents the MI token as a standard `client_assertion`. Microsoft ranks this "Highest" security with **no rotation** (Azure-managed lifecycle), above certificates, and designates client secrets "Development and testing only."

**Sanctioned alternative — Key Vault certificate.** Use where MI-FIC's same-tenant rule cannot be satisfied (see A4 constraints below and [`TENANCY-AND-CREDENTIALS.md`](../../projects/spaarke-auth-v4-dataverse-MI/notes/TENANCY-AND-CREDENTIALS.md)). Precedent already in production: `CiamGraphClientFactory` (`.WithCertificate`, KV PFX, private key ephemeral in-process).

> **⚠️ Correction (2026-08-20, finding E4′): the declarative list below is NOT usable in this codebase.**
> It only takes effect through `Microsoft.Identity.Web`'s token-acquisition surface, and this repo has **zero**
> `EnableTokenAcquisition` / `ITokenAcquisition` / `IDownstreamApi` / `ClientCredentials` in any `.cs`.
> `AddMicrosoftIdentityWebApi` (`AuthorizationModule.cs:36`) is **inbound validation only**, and
> `Spaarke.Dataverse` has no Identity.Web reference at all. Every confidential client here is built directly via
> `ConfidentialClientApplicationBuilder`.
>
> **Use the direct-MSAL equivalent below, and note the consequence: the ordered fallback that the rollback story
> depends on must be BUILT, not inherited.** A reader who configures the JSON will observe no effect and may
> wrongly conclude MI-FIC does not work here. The JSON is retained as accurate *general* Microsoft guidance.

**Preferred wiring — declarative, ordered.** `Microsoft.Identity.Web` `ClientCredentials` supports an ordered fallback list, which is also the local-development answer:

```json
"AzureAd": {
  "ClientCredentials": [
    { "SourceType": "SignedAssertionFromManagedIdentity",
      "ManagedIdentityClientId": "<UAMI client id>",
      "TokenExchangeUrl": "api://AzureADTokenExchange/.default" },
    { "SourceType": "KeyVault", "KeyVaultUrl": "...", "KeyVaultCertificateName": "..." }
  ]
}
```

Direct MSAL equivalent: `WithClientAssertion(Func<AssertionRequestOptions, Task<string>>)`, or `ManagedIdentityClientAssertion` from `Microsoft.Identity.Web.Certificateless` (**reuse the instance** — it caches the signed assertion until expiry).

### New MUST / MUST NOT rules

- **MUST** authenticate BFF-identity confidential clients with MI-FIC or a KV certificate (see the Constraints MUST added by A4).
- **MUST** obtain the confidential credential from the **single shared credential provider** (extending `Infrastructure/Auth/ManagedIdentityCredentialFactory`) rather than constructing credentials per call site. Rationale: seven call sites each rolling their own credential handling is what made the previous state unfixable in one place.
- **MUST** cache `IConfidentialClientApplication` instances at **singleton/process scope**, keyed by `(tenant|client)`. Client assertions require shared clients; per-request construction discards the MSAL token cache. Reference implementation: `DataverseUserClient` static CCA cache.
- **MUST** use a **user-assigned** managed identity for MI-FIC (Entra supports UAMI only as a FIC issuer).
- **MUST NOT** call `.WithClientSecret(...)` for any client authenticating as the BFF identity. **E-3 is closed (2026-08-24) and its site list is empty** — the only remaining carve-out is **E-1** (per-customer owning apps, which are *other* applications' identities).
- **MUST NOT** treat `DefaultAzureCredential` as a substitute for the confidential credential on OBO paths — it cannot perform the exchange.

### Platform constraints this rule must respect

- **Same-tenant rule** — the UAMI and the app registration **must be in the same tenant**. Cross-tenant *resource* access is supported via a multitenant app provisioned into the customer tenant. **Cross-cloud is not supported.**
- **Audience** is exactly `api://AzureADTokenExchange` (sovereign clouds differ).
- **Max 20 FICs** on the object that **holds** them. In MI-FIC the FIC lives on the **app registration**; the UAMI is only the issuer and holds nothing. So the cap counts *how many UAMIs one app registration must trust* — which in **every Spaarke deployment shape is exactly one**. **The cap does not bind in our architecture and should not feature in scoping.** Evidence: [`TENANCY-AND-CREDENTIALS.md` §5](../../projects/spaarke-auth-v4-dataverse-MI/notes/TENANCY-AND-CREDENTIALS.md).
- **Propagation delay** — newly created FICs can return `AADSTS70021` for several minutes; retry logic required.
- **Silent misconfiguration** — a wrong issuer/subject/audience creates successfully and fails only at token exchange.
- **No downstream constraint** — Dataverse, Graph/SPE, Power BI and Azure OpenAI validate only the resulting token; none constrains how the client authenticated.

### Multi-tenant / provisioning shapes (normative)

`customer-provisioning-orchestration-r1` deploys in two models. A4 binds both. **Full analysis lives in [`TENANCY-AND-CREDENTIALS.md`](../../projects/spaarke-auth-v4-dataverse-MI/notes/TENANCY-AND-CREDENTIALS.md)**; the normative summary — the only thing that varies is whether the app registration and the OBO-performing UAMI share a tenant:

| Deployment | App registration | UAMI performing OBO | Credential |
|---|---|---|---|
| **Model 1** — shared Spaarke environment (20+ customers; ONE shared multi-tenant BFF App Service + ONE shared BFF UAMI `sprk-{env}-shared-bff-uami`) | Spaarke tenant | Spaarke tenant | ✅ **MI-FIC** — intra-tenant |
| **Model 2 — Spaarke tenant** (dedicated stamp) | Spaarke tenant | Spaarke tenant | ✅ **MI-FIC** — intra-tenant |
| **Model 2 — customer tenant** (Azure + Dataverse + SPE + app registration all customer-side) | Customer tenant | Customer tenant | ✅ **MI-FIC** — intra-tenant |

**Every Spaarke deployment shape is intra-tenant, so MI-FIC covers all of them** — one mechanism, no special cases. The app registration **MUST** be created in the tenant that hosts the deployment.

**Ruled out (owner decision, 2026-08-18)**: a Spaarke-owned app registration paired with customer-tenant compute. It is the only shape that would break the same-tenant rule (credentials attach to the *application object*, not the consented service principal), and it was never part of the approach — in a full customer-tenant install every job the app registration performs is customer-tenant scoped, so nothing requires a Spaarke-side app object. Rationale + the provisioning doc-fix it implies: [`TENANCY-AND-CREDENTIALS.md` §3.1](../../projects/spaarke-auth-v4-dataverse-MI/notes/TENANCY-AND-CREDENTIALS.md).

**MUST (standing guard)**: if any future shape cannot satisfy the same-tenant rule, fall back to a Key Vault certificate — **not** to a client secret. A client secret is the one credential a hardened customer tenant can refuse outright via Entra app-management policy. No such shape exists today, so **no certificate provisioning automation is required**.

**Open (provisioning's call, does not affect feasibility)**: whether the shared Model 1 BFF authenticates as ONE shared multitenant app registration or one per customer — this decides whether onboarding creates a FIC per customer or none. See `TENANCY-AND-CREDENTIALS.md` §4.

### Adoption status (as of 2026-08-17)

A4 states the target shape; adoption is staged by `spaarke-auth-v4-dataverse-MI`. Until it completes, exception **E-3** governs the retained secret. Verified prerequisites already in place on dev: the BFF app registration (`SDAP-BFF-SPE-API`, `1e40baad-…`) already carries a working FIC (GitHub Actions OIDC, audience `api://AzureADTokenExchange`, 1 of 20 used), and `spaarke-bff-dev` runs a **user-assigned identity only** (`mi-bff-api-dev`). **No FIC automation exists in the repo** — per-environment FIC creation is currently a manual Azure AD admin step.

### Alternatives considered and rejected

- **Path A only (document the secret as an exception and stop)** — rejected as the primary path: it entrenches the credential type Microsoft designates dev/test-only, keeps per-customer secret rotation as a permanent operating cost, and leaves Spaarke exposed to customer-tenant app-management policies that can block or time-limit secrets on a service principal. Retained as the **transitional** mechanism only (E-3).
- **Path C (comply with the pre-A4 rule as written)** — not viable: the rule was literally unsatisfiable for OBO.
- **Certificate as the default instead of MI-FIC** — rejected as the default because it preserves a rotation lifecycle Microsoft's ranking explicitly treats as inferior; retained as the **sanctioned alternative** precisely where MI-FIC's tenancy constraints bite (Model 2b/2c), where it is not a fallback but the correct answer.

> **Note (A4)**: Applied **concise-only** (no full `docs/adr/ADR-028-*.md` exists). This concise ADR now carries Amendments **A1–A4**. A4 changes **server-side credential mechanism only** — it introduces no new IdP or client surface, and **does not weaken the A1/A2/A3 "no OBO on the external, collaboration, or module-host planes" invariants**, which remain in force.

## Amendment A5 (2026-09-04): Workforce `systemuser` root sets derive from Dataverse's impersonated answer

> **Status**: Accepted (resolution path **B — narrow amendment**, per root CLAUDE.md §6.5). **Driver project**: `unified-access-control-r2` (spec ADR Tensions row 3; FR-20). **Amends one clause of A2.** Mechanism + rejected alternatives: [`projects/unified-access-control-r2/notes/investigation/08-option-b-feasibility.md`](../../projects/unified-access-control-r2/notes/investigation/08-option-b-feasibility.md) §5–§6.

**What changes — exactly one clause.** A2's fourth MUST requires the workforce-authenticated caller to
resolve to a principal, "a `systemuser` (**→ ADR-034 membership**)". A5 replaces **only** the
parenthesised derivation for the systemuser branch:

> `systemuser` → **Dataverse's own answer via app-only impersonated read** ∪ contact grants.

**The token model does not change. The client does not change. The plane does not change.** A5 changes
*how the server computes what a workforce systemuser may see*, nothing else.

**Why**: ADR-034 membership derivation approximates Dataverse's answer by pattern-matching columns, and
it is wrong in **both** directions — it grants BU-matched records to users whose role depth does not
cover them, and it hides records that were explicitly shared. Dataverse already computes this exactly,
applying ownership, role depth, business unit, teams, POA shares and hierarchy. Asking it is both more
correct and cheaper than approximating it (3 round trips per request — the same as today's membership
queries, cacheable per `(systemUserId, entityType)`). It also **removes the need for a systemuser
allow-list**: there is no approximation left to tame.

### New MUST rules (workforce `systemuser` plane only)

- **MUST** derive a workforce `systemuser` caller's record root set from an **app-only impersonated
  read** — `DataverseWebApiService.RetrieveMultipleImpersonatedAsync(entitySet, query, callerSystemUserId)`,
  which sets the `MSCRMCallerID` header (`Spaarke.Dataverse/DataverseImpersonation.cs`).
- **MUST** pass the Dataverse **`systemuserid`** in `MSCRMCallerID` — **not** the AAD `oid`. These are
  different identifiers. (`notes/access-model-decision.md` pairs the header with the AAD oid; that
  pairing is **incorrect** per MS Learn, and the helper's own XML doc says so. The live code uses the
  correct one.)
- **MUST** keep the **contact-grants union term**. Grants live in `sprk_externalrecordaccess`, a Spaarke
  table, not in POA — **Dataverse cannot see them**, so its answer is necessarily incomplete without the
  union (register B-17).
- **MUST** fail **closed** on an absent caller id. `RetrieveMultipleImpersonatedAsync` throws on
  `Guid.Empty` — *"refusing to issue an app-only query on the access-scoped read path"*
  (`DataverseWebApiService.cs:978`). ⚠️ **The enforcement is in the READ method, not in the helper**:
  `DataverseImpersonation` deliberately adds no header for a null/empty id, so a *new* impersonated call
  site that skips the read method would silently degrade to an app-only (unscoped) query. Any new
  access-scoped impersonated path MUST carry its own equivalent refusal.
- **MUST** keep the **NFR-04 negative canary** (task 034) as the standing guard: an impersonated
  low-privilege read must return a **strict subset** of the app-only result **and strictly fewer rows**.
  **Equality means impersonation is inert and MUST fail the build** — that is the exact signature of a
  silent degradation to app-only.

### Broker-only compliance (the reading this amendment records)

**Impersonation is not OBO, and A5 does not weaken the no-OBO invariant.**

Broker-only is defined in the code that implements it — `AccessibleRecordSetService.cs:22-24`:
*"reads … APP-ONLY against the already-resolved principal. **No caller-token exchange (no OBO)**."*

An impersonated read uses the **BFF's own app-only credential** and adds a **header naming which user
Dataverse should scope the query to**. The caller's token is never exchanged, never forwarded, and is
not required to exist at Dataverse at all. The BFF acts as itself and asks Dataverse to answer a
narrower question. That satisfies broker-only as written.

This reading is recorded **here**, in the ADR, rather than only in project notes — because a future
reader encountering "impersonation" on a plane whose defining invariant is "no OBO" will otherwise have
to re-derive whether the two conflict, and may reasonably guess wrong.

### Scope + preserved invariants

- **`WorkforcePrincipalKind.SystemUser` only.** The **CIAM / contact plane derivation is untouched** —
  contact-anchored membership (A2's ADR-034 cross-reference) is unchanged, and impersonation is not
  available to it in any case: a `contact` is not a security principal and cannot be impersonated.
- **No OBO is sanctioned anywhere by A5.** The A1/A2/A3 prohibition on exchanging the caller's token
  for a downstream Graph/SPE/Dataverse token is **textually unchanged and still in force**. Investigation
  08 §6 ranked and *rejected* OBO for this plane; A5 does not revisit that.
- **A2's token rules, client surface, plane selection, and Tier-1/Tier-2 split are all unchanged.**
- **ADR-034 is not amended by A5.** Membership resolution remains canonical for the contact plane and
  for every non-root-set use. A5 changes which *source* answers "which records may this systemuser see",
  not the membership engine.

### Deployment prerequisites (register E-2 / E-3)

Both are **blocking** — without them the impersonated read cannot work correctly:

1. **`prvActOnBehalfOfAnotherUser`** on the BFF application user, with a runbook entry recording it.
2. **The BFF app user stays Organization-scoped.** Impersonation returns *the impersonated user's*
   scope, so the app user's own breadth is not a shortcut — but narrowing it breaks the app-only paths
   that legitimately need org breadth.

### Alternatives considered (and rejected)

- **OBO for the systemuser plane** — rejected: forbidden by A2/A3's broker-only invariant, impossible
  for CIAM contacts, and unnecessary — impersonation obtains the same correctness without a token
  exchange. Ranked and rejected in investigation 08 §6.
- **Keep ADR-034 pattern-matching and tune the allow-list** (path C, comply as written) — rejected: the
  approximation is wrong in both directions, and no allow-list makes a column-name convention equal to
  role depth × ownership × teams × sharing. Tuning it indefinitely is the cost this amendment removes.
- **Widen the amendment to cover the contact plane too** — rejected as out of scope and impossible: a
  contact is not a security principal and cannot be impersonated, which is precisely *why* the contact
  plane must compute access rather than ask for it.

> **Note (A5)**: Applied **concise-only** (no full `docs/adr/ADR-028-*.md` exists — confirmed again
> 2026-09-04, consistent with the A2/A3/A4 notes). This concise ADR now carries Amendments **A1–A5**.
> A5 is a **server-side derivation-policy change on one plane**: no new IdP, no new client surface, no
> new token exchange, and **no weakening of the no-OBO invariant**.

## Documented MI exceptions

When MI is genuinely unworkable for a specific outbound surface, the **only** sanctioned alternative is a **Key Vault–backed secret reference** (`@Microsoft.KeyVault(SecretUri=...)`) — never plain text in App Service config. Each exception is enumerated here with rationale, scope, and a remediation TODO. Adding an exception requires a PR that updates this list.

> **Post-A4 note**: for **confidential clients** the sanctioned non-MI credential is a **Key Vault certificate**, not a secret (Amendment A4). E-3 below is the single, time-boxed exception permitting a retained secret during the auth-v4 migration.

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

#### E-2 RE-AFFIRMED with current evidence — 2026-08-21 (`spaarke-auth-v4-dataverse-MI` task 052, FR-E3)

E-2 is **re-affirmed, not resolved.** It was re-tested rather than inherited, and two candidate root causes are now **eliminated**:

| Hypothesis | Checked 2026-08-21 | Verdict |
|---|---|---|
| **Missing custom subdomain** — Microsoft's documented cause of exactly this symptom, and the reason task 052 was scheduled | `spaarke-openai-dev` has `customSubDomainName: spaarke-openai-dev` | **ELIMINATED.** It is configured, and was never the cause. The hoped-for one-config fix does not exist |
| **Missing / unpropagated RBAC** | UAMI `9fd47efb-…` holds **both** `Cognitive Services OpenAI User` and `Cognitive Services User` at account scope | **ELIMINATED** (re-confirms the original finding, still true) |
| **Wrong endpoint host** — the app is configured with `…openai.azure.com` while this `kind=AIServices` account's canonical endpoint is `…cognitiveservices.azure.com` | User-token chat completion returned **HTTP 200 on BOTH hosts** | **Not the cause for user tokens.** Untested for `idtyp=app` tokens — see below |

**What was NOT re-tested, and why it matters:** the decisive comparison in E-2 is *user token → 200* versus *managed-identity token → 401 with correct claims*. Only the first half is reachable from a workstation. The managed-identity half needs IMDS inside the app container: a developer workstation has no route to IMDS, and the **Kudu SCM container does not receive `IDENTITY_ENDPOINT`** (verified 2026-08-21 — `has_endpoint=no`), so it cannot stand in. The original 401 evidence — App Insights `LoggingTokenCredential` capture showing correct `oid`, `appid`, `aud`, `idtyp=app` — remains the only direct measurement and is **not contradicted** by anything found today.

**Next test, and it is cheap**: on the dev slot, set `AzureOpenAI__Endpoint` to `https://spaarke-openai-dev.cognitiveservices.azure.com/` (the account's own endpoint for its `AIServices` kind) **and** clear `AzureOpenAI__ApiKey`, then exercise a chat completion. The host alias is the one variable E-2 never isolated, and it is specifically plausible for an `AIServices`-kind account where `…openai.azure.com` is an alias rather than the resource's endpoint. Two app settings, instantly reversible.

**Do not remove E-2 without that measurement.** Re-testing only the half that was already known to pass would reproduce the prior result and mistake it for a refutation — the failure mode this project exists to eliminate.

### E-3: OBO / BFF-identity confidential clients — transitional retained secret (2026-08-17, per A4) — ✅ **CLOSED 2026-08-24**

> ## ✅ E-3 IS CLOSED. THE SECRET IS GONE. DO NOT CITE THIS EXCEPTION FOR NEW CODE.
>
> Closed **2026-08-24** by `spaarke-auth-v4-dataverse-MI` task 033. Every listed site now takes its
> credential from `OrderedCredentialClientProvider`; the live order on `spaarke-bff-dev` is
> `[ManagedIdentityFederated]` — a **single entry with nothing beneath it** — and
> `Graph:Credentials:RequireSecretFreeIdentity=true` refuses startup outside Development if
> `ClientSecret` returns to the order.
>
> | Removed | |
> |---|---|
> | App settings | `API_CLIENT_SECRET`, `AzureAd__ClientSecret`, `Dataverse__ClientSecret`, `AgentToken__ClientSecret` (2026-08-24 16:50:25Z) |
> | Key Vault | `BFF-API-ClientSecret` + `bff-api-client-secret` (2026-08-24 17:14:40Z; soft-deleted, recoverable to 2026-11-22 — **not purged**) |
>
> **`adr-check` guidance changed**: a `.WithClientSecret(...)` site on the BFF identity is now a plain
> **violation**. There is no longer a set of sites for which citing E-3 is correct. `adr-check`'s ADR-028
> A4 row still says *"cite exception E-3 for the transitional sites it enumerates"* — that clause is
> **spent**; the enumeration is empty.
>
> Enforced mechanically, not by reading: `tests/Spaarke.ArchTests/CredentialGuardTests.cs` fails the build
> on a new site, and `CredentialCensusTests` asserts the construction-site count with a per-site reason.
>
> **Two corrections to the record below**, both found while executing the removal and left visible rather
> than edited away, because this ADR is the place future readers will check:
> 1. *"five keys"* was **four** on the live app — `Graph__ClientSecret` was never set there.
> 2. *"a lowercase Key Vault alias `bff-api-client-secret` **used by the Office add-in deploy**"* is
> **FALSE**. `deploy-office-addins.yml` uses a client **id** and no secret of any kind. The alias's only
> consumer was `scripts/Sync-LocalConfig.ps1` → local development. This false clause propagated from
> here into the project spec and into task 033's own plan, and would have sent the removal to protect the
> wrong surface. It is the same failure this amendment exists to correct, one layer down.
>
> Record: [`projects/spaarke-auth-v4-dataverse-MI/notes/decisions/033-secret-removal.md`](../../projects/spaarke-auth-v4-dataverse-MI/notes/decisions/033-secret-removal.md)

**Historical record of the exception as it stood while open:**

- **Scope**: the confidential clients authenticating as the BFF identity that still use `.WithClientSecret` — `GraphClientFactory` (Graph OBO), `DataverseAccessDataSource` (Dataverse OBO + app-only), `DataverseUserClient` (Dataverse OBO), `AgentTokenService` (Graph + Dataverse OBO for the M365 Copilot agent), `ReportingEmbedService` / `ReportingProfileManager` (Power BI app-only), and the residual `ClientSecretCredential` fallbacks in `DataverseServiceClientImpl` / `DataverseWebApiService`. Config: `BFF-API-ClientSecret` behind five keys (`API_CLIENT_SECRET`, `AzureAd__ClientSecret`, `Graph__ClientSecret`, `Dataverse__ClientSecret`, `AgentToken__ClientSecret`) plus a lowercase Key Vault alias `bff-api-client-secret` used by the Office add-in deploy.
- **Why**: pre-A4 the ADR mandated `DefaultAzureCredential`, which **cannot perform an OBO exchange**, and no secret-free confidential credential was specified. The secret was the only mechanism available. A4 now specifies the target; migration is staged because OBO is the highest-blast-radius auth surface (breaking it disables SPE documents, chat, Office add-ins, the Copilot agent, and **all Dataverse row-level authorization** — fail-closed, so users are locked out immediately and totally).
- **Storage**: `BFF-API-ClientSecret` in `spaarke-spekvcert`, referenced via `@Microsoft.KeyVault(SecretUri=...)`. Live secret expires **2027-12-19**.
- **Rotation**: per `docs/guides/SECRET-ROTATION-PROCEDURES.md` while E-3 is open. Rotation must update **all six paths**, including the lowercase alias.
- **Restore-to-compliance**: complete `spaarke-auth-v4-dataverse-MI` — migrate all listed clients to the A4 credential provider, verify OBO per environment, then remove the secret from app settings and Key Vault and relax `DataverseOptions.ClientSecret` `[Required]` (+ `GraphOptionsValidator`, `AgentTokenOptions`).
- **Remediation TODO**: ~~OPEN~~ — ✅ **DONE 2026-08-24** (task 033). E-3 was time-boxed to `spaarke-auth-v4-dataverse-MI` and that project discharged it. It is **not** a standing exception and must not be cited for new code; see the closure banner above.
- **Not covered by E-3**: E-1 per-customer SpeAdmin owning-app secrets (different applications' identities, architectural); non-Entra API keys (Bing, LlamaParse, Document Intelligence, AI Search — inventoried, out of A4 scope); plaintext secrets in Dataverse columns used by `BaseProxyPlugin` (separate defect, filed).

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

### Server: managed identity for APP-ONLY outbound

```csharp
// Graph app-only, Dataverse service identity, Cosmos, Key Vault, Service Bus.
// Resolve the UAMI-pinned TokenCredential from DI (ManagedIdentityCredentialFactory) —
// do NOT construct credentials inline.
TokenCredential credential = serviceProvider.GetRequiredService<TokenCredential>();

// DefaultAzureCredential chains EnvironmentCredential → WorkloadIdentityCredential
// → ManagedIdentityCredential → AzureCliCredential (the local-dev leg).
```

### Server: secret-free confidential credential for OBO (Amendment A4)

```csharp
// OBO and any MSAL client acting as the BFF identity.
// DefaultAzureCredential CANNOT do this — OBO needs a client assertion.
var cca = ConfidentialClientApplicationBuilder
    .Create(clientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithClientAssertion(opts => _credentialProvider.GetAssertionAsync(opts))  // MI-FIC
    .Build();                                                                  // or .WithCertificate(kvCert)

var result = await cca.AcquireTokenOnBehalfOf(scopes, new UserAssertion(userToken))
                      .ExecuteAsync();
```

**Rules**: obtain the credential from the shared provider (never per call site); **cache the CCA at singleton scope** keyed `(tenant|client)` — assertions require shared clients and per-request construction discards the MSAL token cache; prefer the declarative `AzureAd:ClientCredentials` ordered list (MI-FIC → KV cert → dev credential) over hand-built clients. `.WithClientSecret(...)` is **prohibited** for BFF-identity clients outside transitional exception **E-3**.

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
