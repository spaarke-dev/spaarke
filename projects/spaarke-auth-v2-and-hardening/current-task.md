# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: not-started (next task)
> **Active Phase**: A — Core library rebuild (in progress)
> **Last Updated**: 2026-05-19

## Quick Recovery (Next Session)

| Field | Value |
|-------|-------|
| **Task** | 013 - useAuth() React hook (function-based public API) |
| **Step** | Begin Step 1 of task 013 |
| **Status** | not-started |
| **Next Action** | `continue` or `work on task 013` |

## How to Resume

```
continue
```

Each subsequent `continue` advances one task.

## Phase A Progress (3 of 7 tasks remaining)

| # | Task | Status | Commit |
|---|------|--------|--------|
| 010 | AuthStrategy interface + stub + delete 3 obsolete files | ✅ | `7466978d` |
| 011 | Full BrowserMsalStrategy (+ UPN bug fix) | ✅ | `983de29a` |
| 012 | InMemoryCache wrapper (replaces CacheStrategy + SessionStorageStrategy) | ✅ | (pending commit) |
| 013 | useAuth() React hook (function-based public API) | 🔲 | — |
| 014 | logout API (client + server `/api/auth/logout` + Redis OBO invalidation) | 🔲 | — |
| 015 | VERSION constant + BroadcastChannel listener | 🔲 | — |
| 016 | BrowserMsalStrategy + InMemoryCache unit tests | 🔲 | — |

After 016: comprehensive code-review + adr-check (deferred per risk-tier cadence), then Phase A gate — **user runs MSAL regression test**.

## Last Completed Task (012 — InMemoryCache wrapper)

- Created `InMemoryCache.ts` — wraps any `AuthStrategy`; JWT-exp freshness with 5-min buffer; composite name `in-memory-cache(<inner>)` surfaces both layers in logs
- Preserved diagnostic surface: `getCachedToken()` (sync — used by SpaarkeAuthProvider for JWT `tid` extraction) + `invalidate()` (non-cascading — used by proactive refresh)
- `clearCache()` cascades to inner (logout / 401 retry semantics)
- Simplified `SpaarkeAuthProvider`: dropped `_cacheStrategy`, `_sessionStorageStrategy`, `_cacheToken()`, `_hasValidCache()`; single `_cache: InMemoryCache` reference
- Deleted `CacheStrategy.ts` + `SessionStorageStrategy.ts` (+ stale .js/.d.ts compile outputs)
- Rewrote `tests/CacheStrategy.test.ts` → `tests/InMemoryCache.test.ts` — 10/10 passing (delegation, freshness gating, JWT-exp preference over `expiresOn`, empty-acquire not cached, `clearCache` cascades, `invalidate` does not, stale-read drops entry)
- tsc clean across the package

## Known Issues / Planned Consequences (unchanged from prior task)

- **Pre-existing test failure**: `tests/config.test.ts:6` expects default clientId `170c98e1-...` that `config.ts` removed in commit `9e480d75`. Stale test; not a regression. Fix candidate: task 016.
- **Consumer compile breaks (planned per Phase B)**: 3 files in `src/solutions/` + `src/client/code-pages/` still import `publishToken`, `BridgeStrategy`, or `XrmStrategy` — no longer exist in `@spaarke/auth`. Migrated in Phase B tasks 024 + 026 + LegalWorkspace work.
- **PCF bundle.js stale (INV-8)**: PCF bundle.js files contain stale references to deleted symbols (`SpeDocumentViewer` bundle still references `CacheStrategy`/`SessionStorageStrategy`). Will be rebuilt + redeployed in Phase B task 028.
- **Bug fix not yet deployed**: Task 011's UPN fix is in `@spaarke/auth` source but not yet in any deployed consumer bundle. Takes effect after Phase B per-consumer rebuilds, or a one-off rebuild + redeploy of any single Spaarke surface to validate ahead of full Phase B.

## Key Design Decisions (cumulative)

- **D-AUTH-PROVIDER-CONSTRUCTOR**: `SpaarkeAuthProvider(userConfig?, strategy?)` — strategy defaults to `new BrowserMsalStrategy(config)`. Backward-compatible; forward-compatible for OfficeNaaStrategy (task 080).
- **D-AUTH-TENANT-RESOLUTION**: Dropped MSAL-instance fallback. Relies on JWT `tid` claim (universal across strategies) + Xrm frame-walk last-resort.
- **D-AUTH-LOGIN-HINT-FIX**: `resolveLoginHint(msal)` in `BrowserMsalStrategy` prefers UPN sources (MSAL accounts → Xrm UPN → legacy userName).
- **D-AUTH-JWT-EXP-VALIDATION**: `_validate()` in `BrowserMsalStrategy` AND `_isFresh()` in `InMemoryCache` both decode JWT `exp` with 5-min buffer. Symmetric — cache and fresh-acquire agree on freshness.
- **D-AUTH-CACHE-INVALIDATE-VS-CLEAR**: InMemoryCache exposes both `invalidate()` (in-memory only — for proactive refresh; lets MSAL serve silently) and `clearCache()` (cascades to inner — for logout / 401). SpaarkeAuthProvider's `clearCache()` calls `invalidate()`; `clearAllCaches()` calls `clearCache()`. Preserves the pre-v2 distinction without the two-layer cascade.
- **D-AUTH-QUALITY-GATE-DEFERRAL**: For the autonomous Phase A chain, per-task `/code-review` + `/adr-check` invocations are deferred. A single comprehensive review runs after task 016 completes.

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0 progress: 5/5 ✅
- Phase A progress: 3/7 (010 ✅, 011 ✅, 012 ✅; 013-016 remaining)
- Overall: 8/49 tasks complete (16%)
- Last commit (pending): task 012 InMemoryCache wrapper

## Resume Plan (next session — task 013)

1. User says `continue`.
2. Read task 013 POML — useAuth() React hook.
3. Identify the hook surface: `{isAuthenticated, getAccessToken, authenticatedFetch, tenantId, logout}`. Note `logout` is implemented in task 014; the hook should expose a stub callable now or be designed to wire up in 014.
4. Create `src/client/shared/Spaarke.Auth/src/hooks/useAuth.ts`. May need a React context provider too — TBD by task design.
5. Update package.json to declare `react` as peerDependency if not already.
6. Add `useAuth` to barrel exports (`src/index.ts`).
7. tsc + commit.
