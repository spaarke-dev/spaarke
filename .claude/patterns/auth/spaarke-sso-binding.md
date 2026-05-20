# Spaarke SSO Binding & MSAL Configuration Invariants

> **Last Updated**: 2026-05-19 (post-Spaarke Auth v2 sign-off)
> **Status**: Canonical (v2-aligned)
> **Audience**: Anyone touching `@spaarke/auth`, MSAL configuration, or client-side token acquisition
> **Canonical architecture**: [ADR-028: Spaarke Auth Architecture](../../adr/ADR-028-spaarke-auth-architecture.md)

This is the single source of truth for the **MSAL configuration invariants** every Spaarke internal surface MUST satisfy. The token acquisition mechanism (formerly a 6-strategy cascade) was simplified in Spaarke Auth v2 — see ADR-028.

## Binding Requirements (unchanged from v1)

These were confirmed with the product owner on 2026-05-12 and remain non-negotiable:

1. **True SSO from the browser-level AAD account.** If a user is signed into Edge with their work account, every Spaarke surface (PCF, Code Page, dialog, wizard) MUST inherit silently. No "Pick an account" popup in steady state.
2. **Acceptable prompts (only):** first-ever sign-in on a new device, AAD Conditional Access policy re-auth, the once-per-device "Stay signed in?" prompt.
3. **Token survival across:** tab close, browser close, idle > 60 min. Refresh-token auto-renew handles long sessions.
4. **Single shared auth service.** Every component reuses the same provider instance via `getAuthProvider()`. Zero per-component prompts. Tabs share via `localStorage`.
5. **Multi-account browsers:** when the user has multiple Edge profiles or accounts, the tenant-specific authority MUST auto-select the work account without prompting.

## MSAL Configuration Invariants (INV-1..INV-8)

`SpaarkeAuthProvider` constructs MSAL with these values. Deviating from any of them is a regression:

```typescript
{
  // INV-1
  cacheLocation: 'localStorage',     // NOT 'sessionStorage' — must survive tab close
  // INV-2
  storeAuthStateInCookie: true,      // NOT false — required for ssoSilent when 3rd-party cookies are blocked
  // INV-3
  authority: 'https://login.microsoftonline.com/{tenantId}',
                                     // NEVER /organizations or /common
}
```

| # | Invariant | Why |
|---|---|---|
| **INV-1** | `cacheLocation: 'localStorage'` | `sessionStorage` wipes on tab close → popup every new tab |
| **INV-2** | `storeAuthStateInCookie: true` | Browser 3rd-party cookie blocking breaks `ssoSilent` inside iframes; cookie state lets MSAL track auth across redirects |
| **INV-3** | Tenant-specific authority (NOT `/organizations` or `/common`) | AAD can't disambiguate session cookies inside iframes when authority is multi-tenant → `ssoSilent` fails → popup fires |
| **INV-4** | Tenant resolved from `sprk_TenantId` env var (primary) → Xrm frame-walk (fallback) — NOT hardcoded | Allows per-customer deployment without code changes |
| **INV-5** | UPN (NOT display name) as MSAL `loginHint` | `AADSTS50058: none of the currently signed in user(s) match the requested login hint` if display name is used (display names contain spaces and aren't unique) |
| **INV-6** | Library defaults preserved: prefer omitting `authority` field over passing it explicitly | Lets future library fixes propagate without rebuilding all consumers |
| **INV-7** | All consumers share ONE `PublicClientApplication` instance via `@spaarke/auth`'s `getAuthProvider()` | Direct `new PublicClientApplication(...)` in a consumer = isolated MSAL cache = silent SSO failure |
| **INV-8** | **Bundling Reality** — every consumer of `@spaarke/auth` MUST be rebuilt + redeployed when the library version changes | TypeScript libraries are bundled at consumer build time. Skipping a consumer = it bundles the OLD library = popup-firing regression |

## v2 Token Acquisition Model (simplified — supersedes 6-strategy cascade)

The pre-v2 `SpaarkeAuthProvider` had a 6-strategy cascade (CacheStrategy → SessionStorageStrategy → BridgeStrategy → XrmStrategy → MsalSilentStrategy → MsalPopupStrategy). The cascade was **deleted in Phase A** because:

- The Bridge + Xrm strategies didn't validate JWT exp → returned stale tokens past their lifetime
- Multiple cache layers caused unpredictable refresh behavior
- The cascade defeated MSAL's own caching guarantees (MSAL.localStorage is the canonical cross-tab cache)

**v2 model**: pluggable `AuthStrategy` interface. Default is `BrowserMsalStrategy` (wraps MSAL.js v3). `InMemoryCache` wrapper sits above the strategy with explicit JWT exp validation (5-min buffer). MSAL.js's own `localStorage` cache is the cross-tab/iframe sharing mechanism.

Future strategies: `OfficeNaaStrategy` (Office Add-ins NAA flow — Phase B4).

See [ADR-028 §"Key Patterns"](../../adr/ADR-028-spaarke-auth-architecture.md) for the function-based contract that consumers use.

## When to NOT use @spaarke/auth

Skip the bootstrap when the component only uses `Xrm.WebApi` and never calls the BFF. `Xrm.WebApi` is auto-authenticated by Dataverse (no MSAL involved). Adding `@spaarke/auth` bootstrap to such components is dead weight that slows first paint.

See [`xrm-webapi-vs-bff-auth.md`](xrm-webapi-vs-bff-auth.md) for the decision matrix.

## Verification After Changes

In Edge dev tools console:

```javascript
// Clear everything to force first-strategy paths
localStorage.clear();
sessionStorage.clear();
document.cookie.split(';').forEach(c => {
  document.cookie = c.split('=')[0].trim() + '=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/';
});
// CLOSE EDGE COMPLETELY. Reopen. Navigate to the Spaarke app.
// Expected: no "Pick an account" popup; console shows
//   `authority: https://login.microsoftonline.com/{tenantId}` (NOT /organizations or /common)
// Any popup is a bug — capture the [SpaarkeAuth] log line + the authority value.
```

If the popup appears, check the `authority:` value in the log:
- `/organizations` or `/common` → consumer wasn't rebuilt against v2 (INV-8 violation) OR explicit `authority` is wrong
- `login.microsoftonline.com/{tenantId}` → MSAL configuration is correct; popup is from a different cause (Conditional Access, multi-account state, etc.)

## See Also

- [ADR-028: Spaarke Auth Architecture](../../adr/ADR-028-spaarke-auth-architecture.md) — **canonical**
- [`.claude/constraints/auth.md`](../../constraints/auth.md) — MUST / MUST NOT rules
- [`docs/guides/auth-deployment-setup.md`](../../../docs/guides/auth-deployment-setup.md) — new-environment setup checklist
- [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../AUDIT-FINDINGS-AUTH-SYSTEM.md) — original audit doc (design rationale for v2)
- [`xrm-webapi-vs-bff-auth.md`](xrm-webapi-vs-bff-auth.md) — when to bootstrap vs skip
- [`bff-url-normalization.md`](bff-url-normalization.md) — URL construction helper
- [`.claude/archive/2026-05-19/DEPRECATED-msal-client.md`](../../archive/2026-05-19/DEPRECATED-msal-client.md) — pre-v2 MSAL client doc (archived)
- [`.claude/archive/2026-05-19/DEPRECATED-spaarke-auth-initialization.md`](../../archive/2026-05-19/DEPRECATED-spaarke-auth-initialization.md) — pre-v2 bootstrap doc (archived)
