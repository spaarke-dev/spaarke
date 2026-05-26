# Architecture — Spaarke Auth v2 + Hardening

> **Authoritative source**: [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md) §4. This file summarizes; the audit doc has full rationale and file:line refs.

## High-level architecture (target state)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ CLIENT (Dataverse-hosted Code Pages / PCFs / Office Add-ins)                │
│                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ @spaarke/auth v2                                                      │  │
│  │                                                                       │  │
│  │   useAuth() hook  →  { isAuthenticated, getAccessToken,               │  │
│  │                        authenticatedFetch, tenantId, logout }         │  │
│  │           │                                                           │  │
│  │           ▼                                                           │  │
│  │   AuthProvider (singleton)                                            │  │
│  │           │                                                           │  │
│  │           ▼                                                           │  │
│  │   AuthStrategy ◄── BrowserMsalStrategy (Dataverse)                    │  │
│  │   (pluggable)  ◄── OfficeNaaStrategy   (Outlook/Word)                 │  │
│  │           │                                                           │  │
│  │           ▼                                                           │  │
│  │   InMemoryCache wrapper (JWT exp validation, 5-min buffer)            │  │
│  │           │                                                           │  │
│  │           ▼                                                           │  │
│  │   MSAL.js (PublicClientApplication)                                   │  │
│  │     • cacheLocation: 'localStorage' ◄────── INV-1                     │  │
│  │     • storeAuthStateInCookie: true ◄────── INV-2                      │  │
│  │     • authority: https://login.microsoftonline.com/{tenantId}/ ◄ INV-3│  │
│  │     • localStorage = cross-tab/iframe token sharing mechanism          │  │
│  │                                                                       │  │
│  │   BroadcastChannel('spaarke-auth-events')                             │  │
│  │     • Logout broadcasts (NOT token transport)                         │  │
│  │     • Revocation events                                               │  │
│  │                                                                       │  │
│  │   authenticatedFetch(url, init)                                       │  │
│  │     • Only code that materializes Bearer token as string              │  │
│  │     • 401 retry × 3 with cache clear + exponential backoff            │  │
│  │     • ESLint rule bans Authorization literals outside this file       │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│  Consumers (~30 bundles): SpaarkeAi, PlaybookBuilder, AnalysisWorkspace,    │
│  DocumentRelationshipViewer, SemanticSearch, External SPA, Office Add-ins,  │
│  PCFs (UniversalDatasetGrid, UniversalQuickCreate, SpeDocumentViewer, ...)  │
│                                                                             │
│  All consumers use useAuth().authenticatedFetch — never raw fetch + Bearer  │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼ HTTPS + Bearer JWT
┌─────────────────────────────────────────────────────────────────────────────┐
│ BFF API (Sprk.Bff.Api, .NET 8 Minimal API)                                  │
│                                                                             │
│  Named auth schemes:                                                        │
│    • Bearer            — Azure AD JWT (default user auth)                   │
│    • PowerPagesB2B     — B2B guest JWT (same scheme, separate audience)     │
│    • ApiKey            — Named scheme for service-to-service                │
│                                                                             │
│  PostConfigure<JwtBearerOptions>:                                           │
│    • Idempotency guard                                                      │
│    • Audience list (primary + Copilot)                                      │
│    • CAE: ClientCapabilities = ["cp1"]                                      │
│                                                                             │
│  Audit middleware:                                                          │
│    • Enriches LogScope with oid, appid, obo, tenantId, correlationId        │
│    • Customer pipes structured logs to their Sentinel/Log Analytics         │
│                                                                             │
│  CSP + Trusted Types middleware:                                            │
│    • script-src 'self', no inline, no eval                                  │
│    • Trusted Types enforced in production                                   │
│                                                                             │
│  Step-up auth middleware:                                                   │
│    • [RequiresStepUp] attribute → returns 401 with claims challenge         │
│                                                                             │
│  Rate limiting:                                                             │
│    • Per-IP + per-user buckets on anonymous + API key endpoints             │
│                                                                             │
│  Outbound auth (all managed identity):                                      │
│    • DefaultAzureCredential → Cosmos, AI, Graph app-only, Dataverse         │
│    • OBO flow → Graph (user-on-behalf-of), cached in Redis 55min            │
│                                                                             │
│  Secrets:                                                                   │
│    • All in Key Vault references @Microsoft.KeyVault(SecretUri=...)         │
│    • No plain-text in App Service config                                    │
│                                                                             │
│  Webhook receivers:                                                         │
│    • HMAC-SHA256 signature validation                                       │
│    • Secret key in Key Vault, rotatable                                     │
│                                                                             │
│  Anonymous endpoints (explicit minimum):                                    │
│    • /healthz, /ping, /status — NO /debug/* in production                   │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Critical invariants (MUST be preserved)

From [audit §4.4](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md#44-regression-invariants-do-not-break) and [`.claude/patterns/auth/spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md):

| # | Invariant | Verification |
|---|---|---|
| INV-1 | `cacheLocation: 'localStorage'` (NEVER sessionStorage) | Close browser → reopen → no popup |
| INV-2 | `storeAuthStateInCookie: true` | 3rd-party cookies blocked → ssoSilent still works |
| INV-3 | `authority: https://login.microsoftonline.com/{tenantId}` from `Xrm.Utility.getGlobalContext().organizationSettings.tenantId` via frame-walk — **NEVER `/organizations` or `/common`** | DevTools console shows actual GUID |
| INV-4 | Single shared provider via `getAuthProvider()` — every component reuses same instance | No component instantiates `new PublicClientApplication()` |
| INV-5 | Strategy cascade in exact order: Cache → MSAL (Browser or NAA strategy) | MsalPopup never fires in steady state |
| INV-6 | Tokens survive tab close, browser close, idle > 60min | Close all tabs → reopen 65min later → no prompt |
| INV-7 | `clearCache()` clears in-memory only, NOT localStorage (would cascade other components) | 401 in one PCF doesn't cascade prompts elsewhere |
| INV-8 | Every `@spaarke/auth` consumer rebuilt + redeployed when library changes | After library change, every PCF + Code Page bundle has new version |

## Per-tenant deployment model

Each customer installs Spaarke into their own Azure tenant:

- Their own Azure AD app registrations (BFF + MSAL client + Copilot agent)
- Their own App Service, Key Vault, Dataverse org
- Their own Conditional Access policies, MFA, identity governance
- Tokens never cross customer boundaries; data never crosses customer boundaries

**Implications**: no multi-tenant code patterns, no cross-customer features, no shared-instance privilege separation. Each install is fully isolated.

## Environment-independence

Adding a new customer-tenant deployment requires exactly:

**Client side** (Dataverse environment variables):
- `sprk_BffApiBaseUrl` → BFF host
- `sprk_BffApiAppId` → BFF service principal app ID
- `sprk_MsalClientId` → MSAL public client app ID
- `sprk_TenantId` → Azure AD tenant GUID

**Server side** (App Service config + Key Vault):
- `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__Audience`, `KeyVault__Url`
- Key Vault secrets: `BFF-API-ClientSecret` (or replaced by MI), `Dataverse-ServiceUrl`, `Communication-WebhookSecret`, `Email-WebhookSecret`, `Copilot-SSO-ProviderAppId`, etc.

Documented in `docs/guides/auth-deployment-setup.md` (task 093).

## Audit/threat model context

Per-tenant install means:
- **No** cross-customer attack surface
- **No** shared-instance data isolation requirements in code
- **Customer owns** identity governance, MFA enforcement, secret rotation cadence, Sentinel/Monitor pipeline, CA policies
- **We own** secure deployment artifacts, hardened code, audit emission contract, claims handling, mechanical environment tokenization

## See Also

- [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md) — full design, conflict map, SOC 2 readiness
- [`.claude/patterns/auth/spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md) — MSAL config invariants (canonical sections)
- [`docs/architecture/AUTH-AND-BFF-URL-PATTERN.md`](../../docs/architecture/AUTH-AND-BFF-URL-PATTERN.md) — `buildBffApiUrl()` (unchanged)
- ADR-027 (forthcoming, task 090) — will become canonical
