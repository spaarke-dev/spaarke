# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: not-started (next task)
> **Active Phase**: A — Core library rebuild (in progress)
> **Last Updated**: 2026-05-19

## Quick Recovery (Next Session)

| Field | Value |
|-------|-------|
| **Task** | 014 - logout API (client + server `/api/auth/logout` + Redis OBO invalidation) |
| **Step** | Begin Step 1 of task 014 |
| **Status** | not-started |
| **Next Action** | `continue` or `work on task 014` |

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
| 013 | useAuth() React hook (function-based public API) | ✅ | (pending commit) |
| 014 | logout API (client + server `/api/auth/logout` + Redis OBO invalidation) | 🔲 | — |
| 015 | VERSION constant + BroadcastChannel listener | 🔲 | — |
| 016 | BrowserMsalStrategy + InMemoryCache unit tests | 🔲 | — |

After 016: comprehensive code-review + adr-check (deferred per risk-tier cadence), then Phase A gate — **user runs MSAL regression test**.

## Last Completed Task (013 — useAuth React hook)

- Created `src/useAuth.ts` — function returning `{ isAuthenticated, getAccessToken, authenticatedFetch, tenantId, logout }`. No React state in Phase A (task 015 adds reactivity).
- Added `AuthenticatedFetchFn` type to `types.ts` so component props can type-annotate without importing the function (avoids cycles via useAuth).
- `logout()` is a defensive stub: calls `provider.clearAllCaches()` + console.warn referencing task 014. Full server-side OBO invalidation + BroadcastChannel notification lands in 014/015.
- Updated `src/index.ts` — exports `useAuth`, `UseAuthResult`, `AuthenticatedFetchFn`.
- ESLint setup (new for `@spaarke/auth`): `eslint@8.57` + `@typescript-eslint/parser@8` + `@typescript-eslint/eslint-plugin@8`. `.eslintrc.json` with `no-restricted-syntax` rule banning `Bearer template-literal` outside `authenticatedFetch.ts`. `npm run lint` script added. Positive sanity check confirmed rule fires.
- Wrote `tests/useAuth.test.ts` — 6/6 passing (shape, no token-string fields, delegations, logout stub clears caches).

**Test suite status**: 23/24 passing across the package. The 1 failure is the documented pre-existing baseline (`tests/config.test.ts` expects a removed default clientId) — out of scope for Phase A.

## Known Issues / Planned Consequences

- **Pre-existing test failure**: `tests/config.test.ts:6` expects default clientId `170c98e1-...` that `config.ts` removed in commit `9e480d75`. Stale test; not a regression. Fix candidate: task 016.
- **Orphaned legacy types in `types.ts`** (introduced earlier; now truly dead): `ITokenResult`, `ITokenStrategy`, `TokenCacheEntry`, `TokenSource`. The Cache/SessionStorage strategies that used them were deleted in task 012; `SpaarkeAuthProvider` still re-exports `ITokenResult` as a backward-compat shim. Cleanup candidate: task 016.
- **Stale src/*.d.ts and src/*.js files in git** (pre-v2 misconfigured build outputs — tsconfig now emits to `dist/`). Cleanup candidate: task 016 or separate small commit.
- **Consumer compile breaks (planned per Phase B)**: 3 files in `src/solutions/` + `src/client/code-pages/` still import `publishToken`, `BridgeStrategy`, or `XrmStrategy` — no longer exist in `@spaarke/auth`. Migrated in Phase B tasks 024 + 026 + LegalWorkspace work.
- **PCF bundle.js stale (INV-8)**: `SpeDocumentViewer` bundle.js still references `CacheStrategy`/`SessionStorageStrategy`. Will be rebuilt + redeployed in Phase B task 028.
- **Bug fix not yet deployed**: Task 011's UPN fix is in `@spaarke/auth` source but not yet in any deployed consumer bundle. Takes effect after Phase B per-consumer rebuilds, or a one-off rebuild + redeploy.

## Key Design Decisions (cumulative)

- **D-AUTH-PROVIDER-CONSTRUCTOR**: `SpaarkeAuthProvider(userConfig?, strategy?)` — strategy defaults to `new BrowserMsalStrategy(config)`. Backward-compatible; forward-compatible for OfficeNaaStrategy (task 080).
- **D-AUTH-TENANT-RESOLUTION**: Dropped MSAL-instance fallback. Relies on JWT `tid` claim (universal) + Xrm frame-walk last-resort.
- **D-AUTH-LOGIN-HINT-FIX**: `resolveLoginHint(msal)` in `BrowserMsalStrategy` prefers UPN sources.
- **D-AUTH-JWT-EXP-VALIDATION**: Symmetric JWT-exp + 5-min buffer in `BrowserMsalStrategy._validate()` AND `InMemoryCache._isFresh()`.
- **D-AUTH-CACHE-INVALIDATE-VS-CLEAR**: InMemoryCache exposes `invalidate()` (in-memory only) + `clearCache()` (cascades). Provider's `clearCache()` calls `invalidate()`; `clearAllCaches()` calls `clearCache()`.
- **D-AUTH-USEAUTH-NON-REACTIVE-PHASE-A**: `useAuth()` is a plain function with no React state in Phase A. Consumers re-read on each render. Task 015 adds BroadcastChannel listener that requires React state — defers React peerDep + testing-library to that task.
- **D-AUTH-LOGOUT-STUB-DEFENSIVE**: `useAuth().logout()` in Phase A calls `clearAllCaches()` + console.warn — does the local-side of logout immediately so consumers aren't silently broken, but warns that server-side invalidation is task 014.
- **D-AUTH-ESLINT-PLUGIN-FOR-REFERENCEABILITY**: `@typescript-eslint/eslint-plugin` installed for `@spaarke/auth` ONLY so existing `eslint-disable-next-line @typescript-eslint/no-explicit-any` comments don't error as "unknown rule". The rule itself never fires (all uses are at disable sites).
- **D-AUTH-QUALITY-GATE-DEFERRAL**: For the autonomous Phase A chain, per-task `/code-review` + `/adr-check` invocations are deferred. A single comprehensive review runs after task 016 completes.

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0 progress: 5/5 ✅
- Phase A progress: 4/7 (010 ✅, 011 ✅, 012 ✅, 013 ✅; 014-016 remaining)
- Overall: 9/49 tasks complete (18%)

## Resume Plan (next session — task 014)

Task 014 is meaningfully larger than 010-013 — it crosses client + server boundary:
1. Client-side `logout()` flesh-out in `useAuth` (replaces the Phase A stub):
   - Call MSAL `logoutRedirect` (or `logoutPopup`) via BrowserMsalStrategy
   - POST to `/api/auth/logout` server endpoint
   - Broadcast invalidation message via BroadcastChannel (the consumer-listener wires in task 015)
   - Cascade-clear all caches
2. Server-side `/api/auth/logout` endpoint:
   - Reads OBO key from caller JWT (oid + tenant)
   - Invalidates the Redis OBO entry for that key
   - Returns 204 No Content
3. Strategy interface update: `BrowserMsalStrategy.logout()` method
4. Tests: client logout test + server endpoint test

Knowledge needed: BFF API entry point patterns (`Program.cs`), Redis OBO cache key format (`.claude/patterns/auth/obo-flow.md` + `Sprk.Bff.Api/Infrastructure/...`), MSAL logout APIs.

Estimated effort larger than 010-013 individually — possibly the largest task in Phase A.
