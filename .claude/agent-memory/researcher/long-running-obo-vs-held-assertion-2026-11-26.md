---
name: long-running-obo-vs-held-assertion-2026-11-26
description: MSAL long-running OBO (ILongRunningWebApi) vs holding the incoming access token for a detached playbook run; token lifetimes; Copilot tool-call 30 s figure; recommendation
metadata:
  type: reference
---

**Question**: For a user-started playbook run that outlives the MCP tool call (30-60 min), keep the caller's incoming token in memory (mech 1) or use MSAL long-running OBO (mech 2)?

**Findings (2026-11-26)**
- Long-running OBO exists: `((ILongRunningWebApi)cca).InitiateLongRunningProcessInWebApi(scopes, userToken, ref sessionKey)` once, then `AcquireTokenInLongRunningProcess(scopes, sessionKey)`; `StopLongRunningProcessInWebApiAsync` since MSAL 4.51.0. Repo has Microsoft.Identity.Client 4.87.0 (OK). Identity.Web wraps it as `AcquireTokenOptions.LongRunningWebApiSessionKey` (+ "AllocateForMe" sentinel) via IDownstreamApi; the BFF uses Identity.Web only for JWT validation (AuthorizationModule.cs:60,628), not ITokenAcquisition.
- Plain `AcquireTokenOnBehalfOf` does NOT keep the refresh token (MSAL source OnBehalfOfRequest.cs: `msalTokenResponse.RefreshToken = null` unless long-running). It caches the downstream AT keyed by assertion hash; MSAL treats an AT as expired 5 min early (Constants.AccessTokenExpirationBuffer). Expired assertion at exchange -> AADSTS500133.
- Long-running OBO stores an RT in the CCA user token cache (in-memory L1 works; Learn only "strongly recommends" distributed). RT: 90-day inactivity window; revoked on user/admin revoke-all, admin password reset (confidential client), CA/SIF re-auth at redemption surfaces as MsalUiRequiredException. Per-CCA-instance cache: a credential failover rebuild in OrderedCredentialClientProvider (line 364-367) would lose it unless `CacheOptions.EnableSharedCacheOptions`.
- Access token default 60-90 min random (CAE-aware clients up to 24-28 h; no-CA tenants ~2 h for Teams/M365 clients). Spaarke sets no WithClientCapabilities (no cp1) so no CAE for OBO tokens.
- Copilot: documented "< 30 s per tool call" appears as a connector requirement (Cowork plugin dev page, WIQD build-reuse-mcp-servers); no documented enforcement figure for declarative-agent MCP plugins. Copilot Studio harness 120 s, Foundry 100 s (50 s classic) are other products.
- Found defect: GraphClientFactory.cs:299 caches OBO Graph token in Redis for fixed 55 min regardless of result.ExpiresOn -> stale token served late in a long run.
- Not verified: RT redemption with MI-FIC client assertion (OBO with MI-FIC proven per ADR-028 A4, RT redemption not); the remaining lifetime of the token the Copilot token store sends.

**Recommendation given**: mech 1 (detached DefaultHttpContext, ComposeProfileDispatcher pattern) + pre-warm OBO for Graph and Dataverse at run start + fix 55-min TTL + cap run ~50 min + suppress ExecutionContext flow (D-030-1). Mech 2 needs the detached context anyway (claims, ExtractBearerToken in ~13 services) plus 4 OBO-site branches.

**Sources**
- https://learn.microsoft.com/entra/msal/dotnet/acquiring-tokens/web-apps-apis/on-behalf-of-flow
- https://learn.microsoft.com/entra/msidweb/call-downstream-apis/from-web-apis
- https://learn.microsoft.com/entra/identity-platform/access-tokens#token-lifetime
- https://learn.microsoft.com/entra/identity-platform/refresh-tokens
- https://learn.microsoft.com/plugins/build-reuse-mcp-servers ; https://learn.microsoft.com/copilot/cowork/cowork-plugin-development
- MSAL source: OnBehalfOfRequest.cs, CacheKeyFactory.cs, Constants.cs (github AzureAD/microsoft-authentication-library-for-dotnet)

**Open**: live probe of long-running OBO with MI-FIC; token store refresh behaviour for MCP plugin tokens.
