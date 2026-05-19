# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: not-started (next task)
> **Active Phase**: A — Core library rebuild (in progress)
> **Last Updated**: 2026-05-19

## Quick Recovery (Next Session)

| Field | Value |
|-------|-------|
| **Task** | 015 - VERSION constant + BroadcastChannel listener |
| **Step** | Begin Step 1 of task 015 |
| **Status** | not-started |
| **Next Action** | `continue` or `work on task 015` |

## How to Resume

```
continue
```

## Phase A Progress (2 of 7 tasks remaining)

| # | Task | Status | Commit |
|---|------|--------|--------|
| 010 | AuthStrategy interface + stub + delete 3 obsolete files | ✅ | `7466978d` |
| 011 | Full BrowserMsalStrategy (+ UPN bug fix) | ✅ | `983de29a` |
| 012 | InMemoryCache wrapper (replaces CacheStrategy + SessionStorageStrategy) | ✅ | `4c840994` |
| 013 | useAuth() React hook (function-based public API) | ✅ | `654ddde0` |
| 014 | logout API (client-side **SLIM** scope; server deferred to CAE/061) | ✅ | (pending commit) |
| 015 | VERSION constant + BroadcastChannel listener | 🔲 | — |
| 016 | BrowserMsalStrategy + InMemoryCache unit tests | 🔲 | — |

After 016: comprehensive code-review + adr-check (deferred per risk-tier cadence), then Phase A gate — **user runs MSAL regression test**.

## Last Completed Task (014 — logout, SLIM scope per design decision)

**Scope decision** (D-AUTH-LOGOUT-SLIM): client-side only. Server-side OBO Redis cache invalidation + `/api/auth/logout` endpoint INTENTIONALLY DEFERRED after a security/architecture conversation showed:
- The user's concern ("if I log out in tab A, tab B shouldn't have access") is a multi-context UX problem fully addressed by client-side mechanisms (MSAL.logoutPopup kills refresh token + Entra session; BroadcastChannel notifies sibling tabs to drop in-memory caches)
- Server-side Redis cache invalidation is a *performance* cleanup, not an access control. Real access revocation requires CAE (Phase D task 061) or a server-side JWT blocklist — neither was in scope for 014

**What landed**:
- `AuthStrategy.logout(): Promise<void>` added to interface
- `BrowserMsalStrategy.logout()` — MSAL.logoutPopup with clearCache fallback for popup-blocked scenarios
- `InMemoryCache.logout()` — delegates to inner (cascading)
- New `src/broadcastChannel.ts` — singleton 'spaarke-auth-events' channel; `broadcastLogout()` + `onAuthBroadcast(handler)`; degrades to no-ops when BroadcastChannel unavailable
- `SpaarkeAuthProvider.logout()` — broadcasts → cache.logout (cascades to MSAL.logoutPopup)
- `initAuth()` — registers listener that calls `provider.clearCache()` (non-cascading invalidate) on received logout broadcasts
- `useAuth().logout` — no more stub warn; delegates to `provider.logout()`

**Tests**: 27/28 passing. New `broadcastChannel.test.ts` (4 tests) + updated `useAuth.test.ts`. The 1 failure remains the pre-existing `config.test.ts` baseline.

**Server-side work split off** (no new task POML created — to be sequenced with CAE in Phase D):
- POST /api/auth/logout endpoint
- GraphTokenCache.InvalidateForUserAsync (would use Option C secondary-index pattern)
- AgentTokenService equivalent
- JWT validation blocklist (only if needed before CAE lands)

## Known Issues / Planned Consequences

- **Pre-existing test failure**: `tests/config.test.ts:6` expects default clientId `170c98e1-...` that `config.ts` removed in commit `9e480d75`. Stale test; not a regression. Fix candidate: task 016.
- **Orphaned legacy types in `types.ts`**: `ITokenResult`, `ITokenStrategy`, `TokenCacheEntry`, `TokenSource`. Cleanup candidate: task 016.
- **Stale `src/**/*.d.ts` and `src/**/*.js` files in git** (pre-v2 misconfigured build outputs). Cleanup candidate: task 016.
- **Consumer compile breaks (planned per Phase B)**: 3 files in `src/solutions/` + `src/client/code-pages/` still import `publishToken`, `BridgeStrategy`, or `XrmStrategy` — no longer exist. Migrated in Phase B tasks 024 + 026 + LegalWorkspace work.
- **PCF bundle.js stale (INV-8)**: rebuilt in Phase B task 028.
- **Bug fix not yet deployed**: Task 011's UPN fix is in source but not yet in any deployed consumer bundle.

## Key Design Decisions (cumulative)

- **D-AUTH-PROVIDER-CONSTRUCTOR**: `SpaarkeAuthProvider(userConfig?, strategy?)` — strategy defaults to `BrowserMsalStrategy`.
- **D-AUTH-TENANT-RESOLUTION**: JWT `tid` + Xrm frame-walk fallback. No MSAL-instance dependency.
- **D-AUTH-LOGIN-HINT-FIX**: `resolveLoginHint(msal)` prefers UPN sources.
- **D-AUTH-JWT-EXP-VALIDATION**: Symmetric in `BrowserMsalStrategy._validate` + `InMemoryCache._isFresh`. 5-min buffer.
- **D-AUTH-CACHE-INVALIDATE-VS-CLEAR**: `invalidate()` (in-memory only) vs `clearCache()` (cascades). InMemoryCache.logout() cascades too (drops in-memory + calls inner.logout).
- **D-AUTH-USEAUTH-NON-REACTIVE-PHASE-A**: useAuth is a plain function; React reactivity deferred to task 015.
- **D-AUTH-ESLINT-PLUGIN-FOR-REFERENCEABILITY**: `@typescript-eslint/eslint-plugin` installed only so existing inline disables reference a defined rule.
- **D-AUTH-LOGOUT-SLIM** (NEW from this session): Task 014 ships client-side only. Multi-context UX (the actual concern) handled by MSAL.logoutPopup + BroadcastChannel. Server-side OBO Redis cleanup + `/api/auth/logout` endpoint deferred — they're hygiene/performance not security; real access revocation comes with CAE in Phase D task 061. See task 014 notes for full analysis (3 options A/B/C considered).
- **D-AUTH-QUALITY-GATE-DEFERRAL**: Per-task `/code-review` + `/adr-check` deferred to comprehensive review after task 016.

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0 progress: 5/5 ✅
- Phase A progress: 5/7 (010 ✅, 011 ✅, 012 ✅, 013 ✅, 014 ✅; 015-016 remaining)
- Overall: 10/49 tasks complete (20%)

## Resume Plan (next session — task 015)

Task 015 = "VERSION constant + BroadcastChannel invalidation listener". With 014 having already wired the BroadcastChannel listener for `{type:'logout'}`, task 015's likely scope:
1. Add a `VERSION` constant to `@spaarke/auth` (probably `src/version.ts` exporting a build-time string)
2. Stamp VERSION on logout broadcasts so listeners can detect version skew (Phase B deployment scenarios: if tab A is running v2.1 of `@spaarke/auth` and tab B is running v2.0 in a stale PCF bundle, a logout broadcast still works but version is logged for diagnosis)
3. Possibly extend the broadcast message type: `{type: 'logout', version: string, origin: 'browser-msal' | ...}`
4. Add a 'version-check' broadcast message + listener that warns on skew

Will need to confirm details by reading the task 015 POML when resuming.
