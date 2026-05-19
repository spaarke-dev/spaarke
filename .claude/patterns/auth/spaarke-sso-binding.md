# Spaarke SSO Binding & Token Strategy Chain

> **Last Updated**: 2026-05-13
> **Status**: Canonical
> **Audience**: Anyone touching `@spaarke/auth`, MSAL configuration, or client-side token acquisition

This is the **single source of truth** for how Spaarke's internal components authenticate. Other auth docs (constraints, architecture, pattern files) point here.

## Binding Requirements

These were confirmed with the product owner on 2026-05-12 and ARE non-negotiable:

1. **True SSO from the browser-level AAD account.** If a user is signed into Edge with their work account, every Spaarke surface (PCF, Code Page, dialog, wizard) MUST inherit silently. No "Pick an account" popup in steady state.
2. **Acceptable prompts (only):** first-ever sign-in on a new device, AAD Conditional Access policy re-auth, the once-per-device "Stay signed in?" prompt.
3. **Token survival across:** tab close, browser close, idle > 60 min. Refresh-token auto-renew handles long sessions.
4. **Single shared auth service.** Every component reuses the same provider instance via `getAuthProvider()`. Zero per-component prompts. Tabs share via `localStorage` + the same-origin parent frame bridge.
5. **Multi-account browsers:** when the user has multiple Edge profiles or accounts, the tenant-specific authority MUST auto-select the work account without prompting.

## Required MSAL Configuration

`SpaarkeAuthProvider` constructs MSAL with these values. Deviating from any of them is a regression:

```typescript
{
  cacheLocation: 'localStorage',     // NOT 'sessionStorage' — must survive tab close
  storeAuthStateInCookie: true,      // NOT false — required for ssoSilent when 3rd-party cookies are blocked
  authority: 'https://login.microsoftonline.com/{tenantId}',
                                     // tenantId from Xrm.Utility.getGlobalContext().organizationSettings.tenantId
                                     // via frame-walk. NEVER /organizations or /common.
}
```

**Why each setting:**
- `sessionStorage` was the prior default. It gets wiped on tab close, so every new tab fired a popup — that's how the regression was discovered.
- `storeAuthStateInCookie: false` was the prior default. Browser 3rd-party cookie blocking breaks `ssoSilent` inside iframes; the cookie state lets MSAL track auth across redirects.
- `/organizations` was the prior authority. AAD can't decide which tenant's session cookie to use inside an iframe, so `ssoSilent` fails → popup fires. A tenant-specific URL bypasses this.

`@spaarke/auth/src/config.ts` exports `resolveTenantFromXrm()` and uses it inside `getDefaultAuthority()` so any consumer that omits the `authority` field gets the correct value automatically. **Prefer omitting the field** over passing it explicitly — that lets a future fix in the library propagate without rebuilding all consumers.

## The 6-Strategy Token Acquisition Chain

`SpaarkeAuthProvider.getAccessToken()` tries strategies in this order. The first one that returns a non-expired token wins. The chain exists so that a successful neighbor (a PCF that already initialized auth, a parent frame that holds a token) can short-circuit MSAL entirely.

| # | Strategy | What it returns | When it succeeds |
|---|---|---|---|
| 1 | `CacheStrategy` | In-memory token (per-provider instance) | Same provider already acquired in this session |
| 2 | `SessionStorageStrategy` | Token from `__spaarke_bff_token_cache__` | Same-origin neighbor (another PCF/iframe) stored a token |
| 3 | `BridgeStrategy` | Parent frame's `window.__SPAARKE_BFF_TOKEN__` | We're an iframe and the host set the bridge |
| 4 | `XrmStrategy` | `Xrm.WebApi`-derived token | Available — **but Dataverse-scoped only, NOT BFF.** Useful for Dataverse queries, never for BFF API calls |
| 5 | `MsalSilentStrategy` | `acquireTokenSilent` then `ssoSilent` | Fresh refresh token in localStorage; cookie state present |
| 6 | `MsalPopupStrategy` | Interactive popup | All silent strategies failed. **This firing is a regression.** |

When `MsalPopupStrategy` fires, log the failure reasons of strategies 5 and below. The most common root cause is `authority` being wrong (still `/organizations`) — that means a consumer wasn't rebuilt after the library update.

## Bundling Reality (CRITICAL)

`@spaarke/auth` is a **TypeScript library bundled at build time** into every consumer's `bundle.js` (PCF) or `index.html` (Code Page). Changing the library source does **NOT** auto-update consumers. Each must be rebuilt and redeployed individually.

There are currently ~30 consumers (8 active virtual PCFs + ~20 Code Pages + the External Workspace SPA). When you change anything in `src/client/shared/Spaarke.Auth/src/`:

1. Run `npm run build` in `src/client/shared/Spaarke.Auth/` to recompile `dist/`.
2. Rebuild and redeploy every consumer. Use parallel agents for the Code Page batch (they all use `az`-token deploys; safe to parallelize). Sequence the PCF batch (pac CLI is stateful).

The first consumer to NOT be rebuilt after a library change becomes the popup-firing component. Diagnosing this is straightforward: open browser dev tools, watch the `[SpaarkeAuth] All 6 token strategies failed` log, and check the `authority` value — if it's `/organizations`, that consumer is on the old library.

## When to NOT use @spaarke/auth

Skip the bootstrap when the component only uses `Xrm.WebApi` and never calls the BFF. `Xrm.WebApi` is auto-authenticated by Dataverse (no MSAL involved). Adding `@spaarke/auth` bootstrap to such components is dead weight that slows first paint.

See `xrm-webapi-vs-bff-auth.md` for the decision matrix.

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
// Expected: no "Pick an account" popup. Any popup is a bug — capture the [SpaarkeAuth] log line.
```

If the popup appears, check the `authority:` value in the log. `/organizations` means an un-rebuilt consumer is the culprit, not the library.

## See Also

- `.claude/patterns/auth/DEPRECATED-spaarke-auth-initialization.md` — bootstrap order in Code Pages (⛔ deprecated — superseded by Spaarke Auth v2 `useAuth()`; see [AUDIT-FINDINGS-AUTH-SYSTEM.md](../../AUDIT-FINDINGS-AUTH-SYSTEM.md))
- `.claude/patterns/auth/xrm-webapi-vs-bff-auth.md` — when to bootstrap vs skip
- `.claude/patterns/auth/bff-url-normalization.md` — URL construction helper
- `docs/architecture/sdap-auth-patterns.md` — full taxonomy + threat model
- `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md` — the URL pattern reference
