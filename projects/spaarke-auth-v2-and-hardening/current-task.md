# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: in-progress
> **Active Phase**: A — Core library rebuild
> **Last Updated**: 2026-05-19

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | 010 - Define AuthStrategy interface + stub BrowserMsalStrategy + delete BridgeStrategy/XrmStrategy/tokenBridge |
| **Step** | 1 of 9: implementing AuthStrategy interface + BrowserMsalStrategy stub + SpaarkeAuthProvider refactor |
| **Status** | in-progress |
| **Rigor Level** | FULL |
| **Next Action** | Batch: types.ts → AuthStrategy.ts → BrowserMsalStrategy.ts → SpaarkeAuthProvider.ts rewrite → initAuth.ts → index.ts → git rm deletes |

## Phase A Plan (sequential due to SpaarkeAuthProvider.ts file conflicts in advertised parallel groups)

1. ☐ Task 010 (now) — AuthStrategy interface + stub + delete 3 files
2. ☐ Task 011 — Full BrowserMsalStrategy (fold MsalSilent + MsalPopup; fix UPN bug)
3. ☐ Task 012 — InMemoryCache wrapper (replace CacheStrategy + SessionStorageStrategy)
4. ☐ Task 013 — useAuth() React hook (function-based API)
5. ☐ Task 014 — logout API (client + server endpoint + Redis OBO invalidation)
6. ☐ Task 015 — VERSION constant + BroadcastChannel listener
7. ☐ Task 016 — Unit tests for BrowserMsalStrategy + InMemoryCache

After 016: **STOP at Phase A gate → user runs MSAL regression test.**

## Knowledge Loaded (this task)

- `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` §4.1 target principles, §4.4 INV-1..INV-8 (✅)
- `.claude/patterns/auth/spaarke-sso-binding.md` §"Required MSAL Configuration" (✅ in context with STOP banner)
- `.claude/constraints/auth.md` (✅ in context with STOP banner)
- `src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts` (✅ — MSAL config at lines 44-68)
- `src/client/shared/Spaarke.Auth/src/types.ts` (✅)
- `src/client/shared/Spaarke.Auth/src/index.ts` (✅)
- `src/client/shared/Spaarke.Auth/src/initAuth.ts` (✅)
- `src/client/shared/Spaarke.Auth/src/config.ts` (✅ — resolveTenantFromXrm + resolveDefaultAuthority)
- `src/client/shared/Spaarke.Auth/src/authenticatedFetch.ts` (✅ — uses provider.getAccessToken/clearCache/getConfig)
- Files to delete: BridgeStrategy.ts, XrmStrategy.ts, tokenBridge.ts (+ .d.ts + .d.ts.map + .js + .js.map for each)

## Design Decisions

- **SpaarkeAuthProvider new constructor signature**: `constructor(userConfig?: IAuthConfig, strategy?: AuthStrategy)` — strategy defaults to `new BrowserMsalStrategy(this._config)` if not provided. Backward-compatible for existing `new SpaarkeAuthProvider(config)` callers (none in src per audit, but defensive); forward-compatible for OfficeNaaStrategy in task 080.
- **AuthStrategy interface**: `{ name: string; acquire(): Promise<TokenResult>; clearCache(): void }` per POML step 2.
- **New TokenResult type** in types.ts: `{ accessToken: string; expiresOn: number; tenantId?: string }`. Legacy ITokenResult kept for internal use by CacheStrategy/SessionStorageStrategy until task 012.
- **MSAL config lifted verbatim** from SpaarkeAuthProvider:44-68 into BrowserMsalStrategy constructor with inline INV-1, INV-2, INV-3 comments.
- **getCachedTenantId / getTenantId**: drop MSAL-instance fallback (strategy-agnostic); rely on JWT `tid` claim extraction (works for any token source) + Xrm frame-walk last resort. The MSAL-account-based tenantId path required exposing MSAL instance via interface, which would break strategy abstraction. JWT `tid` is universal.
- **Stub vs full**: BrowserMsalStrategy stub internally instantiates MsalSilentStrategy + MsalPopupStrategy and delegates. Task 011 folds them in.

## Files Modified This Session

(populated as work proceeds)
