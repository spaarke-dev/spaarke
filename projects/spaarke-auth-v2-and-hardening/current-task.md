# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: not-started (next task)
> **Active Phase**: A — Core library rebuild (last task remaining)
> **Last Updated**: 2026-05-19

## Quick Recovery (Next Session)

| Field | Value |
|-------|-------|
| **Task** | 016 - BrowserMsalStrategy + InMemoryCache unit tests (Phase A FINAL task) |
| **Step** | Begin Step 1 of task 016 |
| **Status** | not-started |
| **Next Action** | `continue` or `work on task 016` |

## How to Resume

```
continue
```

After 016 lands: **Phase A complete** → comprehensive code-review + adr-check (deferred per D-AUTH-QUALITY-GATE-DEFERRAL) → user runs MSAL regression test → Phase B begins.

## Phase A Progress (1 of 7 tasks remaining)

| # | Task | Status | Commit |
|---|------|--------|--------|
| 010 | AuthStrategy interface + stub + delete 3 obsolete files | ✅ | `7466978d` |
| 011 | Full BrowserMsalStrategy (+ UPN bug fix) | ✅ | `983de29a` |
| 012 | InMemoryCache wrapper (replaces CacheStrategy + SessionStorageStrategy) | ✅ | `4c840994` |
| 013 | useAuth() React hook (function-based public API) | ✅ | `654ddde0` |
| 014 | logout API (client-side SLIM scope; server deferred to CAE/061) | ✅ | `a4edb443` |
| 015 | VERSION constant + broadcast listener (moved to provider constructor) | ✅ | (pending commit) |
| 016 | BrowserMsalStrategy + InMemoryCache unit tests | 🔲 | — |

## Last Completed Task (015 — VERSION + broadcast listener relocation)

- New `src/version.ts` — exports `VERSION = '2.0.0'` (hardcoded literal; comment documents manual sync with package.json — avoids bundler-config fragility)
- `package.json` version bumped 1.0.0 → 2.0.0 (aligns with v2 refactor)
- `VERSION` exported from `src/index.ts`
- Broadcast listener wiring **moved** from `initAuth.ts` → `SpaarkeAuthProvider` constructor (per task 015 POML wording). Provider is now self-contained: console.info version log + onAuthBroadcast subscription happen at construction; dispose() cleans up both alongside the proactive-refresh interval
- Listener now calls `this.clearAllCaches()` (cascading) per POML — diverges slightly from task 014's `provider.clearCache()` (invalidate-only). Mildly redundant but defensive
- `initAuth.ts` simplified accordingly

**Verification**: tsc clean, jest 27/28 passing, lint clean.

## Known Issues / Planned Consequences (unchanged)

- **Pre-existing test failure**: `tests/config.test.ts:6` expects removed default clientId. Fix candidate: task 016.
- **Orphaned legacy types in `types.ts`**: `ITokenResult`, `ITokenStrategy`, `TokenCacheEntry`, `TokenSource`. Cleanup candidate: task 016.
- **Stale `src/**/*.d.ts` and `src/**/*.js` files in git** — pre-v2 misconfigured build outputs. Cleanup candidate: task 016.
- **Consumer compile breaks (planned per Phase B)**: 3 files in `src/solutions/` + `src/client/code-pages/` still import deleted symbols. Migrated in Phase B tasks 024 + 026 + LegalWorkspace work.
- **PCF bundle.js stale (INV-8)**: rebuilt in Phase B task 028.
- **Bug fix not yet deployed**: Task 011's UPN fix is in source but not yet in any deployed consumer bundle.

## Key Design Decisions (cumulative)

- **D-AUTH-PROVIDER-CONSTRUCTOR**: `SpaarkeAuthProvider(userConfig?, strategy?)` — strategy defaults to `BrowserMsalStrategy`.
- **D-AUTH-TENANT-RESOLUTION**: JWT `tid` + Xrm frame-walk fallback.
- **D-AUTH-LOGIN-HINT-FIX**: `resolveLoginHint(msal)` prefers UPN sources.
- **D-AUTH-JWT-EXP-VALIDATION**: Symmetric in `BrowserMsalStrategy._validate` + `InMemoryCache._isFresh`. 5-min buffer.
- **D-AUTH-CACHE-INVALIDATE-VS-CLEAR**: `invalidate()` (in-memory only) vs `clearCache()` (cascades). InMemoryCache.logout() also cascades.
- **D-AUTH-USEAUTH-NON-REACTIVE-PHASE-A**: useAuth is a plain function; React reactivity deferred to a later iteration.
- **D-AUTH-ESLINT-PLUGIN-FOR-REFERENCEABILITY**: `@typescript-eslint/eslint-plugin` installed only so existing inline disables reference a defined rule.
- **D-AUTH-LOGOUT-SLIM**: Task 014 ships client-side only (MSAL.logoutPopup + BroadcastChannel). Server-side OBO cleanup + `/api/auth/logout` deferred — they're hygiene/performance, not access control. Real revocation lands with CAE in task 061.
- **D-AUTH-VERSION-HARDCODED** (NEW from 015): VERSION lives in `src/version.ts` as a hardcoded literal kept in sync with package.json. Considered importing from package.json but rejected: bundler-config fragility + runtime JSON fetch in browser builds.
- **D-AUTH-BROADCAST-LISTENER-IN-PROVIDER** (NEW from 015): Broadcast listener registered in SpaarkeAuthProvider constructor (was in initAuth.ts after task 014). Provider self-contained for init-time side effects; dispose() unifies cleanup. Listener calls `this.clearAllCaches()` (cascading) per task 015 POML — a minor divergence from task 014's invalidate-only choice; documented above.
- **D-AUTH-QUALITY-GATE-DEFERRAL**: Per-task code-review + adr-check deferred to comprehensive review after task 016.

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0 progress: 5/5 ✅
- Phase A progress: 6/7 (010 ✅, 011 ✅, 012 ✅, 013 ✅, 014 ✅, 015 ✅; 016 last)
- Overall: 11/49 tasks complete (22%)
- Library version: `@spaarke/auth@2.0.0`

## Resume Plan (next session — task 016)

Task 016 is the **Phase A finale**: BrowserMsalStrategy + InMemoryCache unit tests. Likely scope:
1. Add tests for `BrowserMsalStrategy` (token-acquisition cascade, JWT exp validation, login-hint resolution priority, MSAL initialization, logout)
2. Add tests for `InMemoryCache` — already 10 passing tests from task 012. Possibly add edge cases the task POML calls out
3. **Cleanup opportunities** (out-of-scope flagged across 012-015 — good time to bundle):
   - Fix stale `tests/config.test.ts` baseline (expects removed default clientId)
   - Remove orphaned legacy types from `types.ts`: `ITokenResult`, `ITokenStrategy`, `TokenCacheEntry`, `TokenSource` (no consumers after task 012)
   - Remove stale `src/**/*.d.ts` + `src/**/*.js` files (pre-v2 misconfigured build outputs)
4. After 016: comprehensive `/code-review` + `/adr-check` pass (deferred from per-task gates)
5. **Phase A gate**: user runs MSAL regression test from `.claude/patterns/auth/spaarke-sso-binding.md`. Validates the UPN fix from task 011 takes effect after a one-off consumer rebuild.
