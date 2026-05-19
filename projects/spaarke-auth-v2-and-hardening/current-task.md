# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: 🎉 **PHASE A COMPLETE** — waiting on user MSAL regression test gate
> **Active Phase**: A done; Phase B (consumer migrations) next
> **Last Updated**: 2026-05-19

## Quick Recovery (Next Session)

| Field | Value |
|-------|-------|
| **Status** | Phase A finished. Phase A gate is a USER step (MSAL regression test). |
| **Next Action** | (a) User runs MSAL regression test from `.claude/patterns/auth/spaarke-sso-binding.md`, **OR** (b) `continue` to start Phase B (consumer migrations 020-028 — many are parallel-safe; will dispatch in batched waves). |

## How to Resume

```
continue
```

## 🎉 Phase A: 7/7 ✅

| # | Task | Status | Commit |
|---|------|--------|--------|
| 010 | AuthStrategy interface + stub + delete 3 obsolete files | ✅ | `7466978d` |
| 011 | Full BrowserMsalStrategy (+ UPN bug fix) | ✅ | `983de29a` |
| 012 | InMemoryCache wrapper (replaces CacheStrategy + SessionStorageStrategy) | ✅ | `4c840994` |
| 013 | useAuth() React hook (function-based public API) | ✅ | `654ddde0` |
| 014 | logout API (client-side SLIM scope; server deferred to CAE/061) | ✅ | `a4edb443` |
| 015 | VERSION constant + broadcast listener (moved to provider constructor) | ✅ | `a85e06ec` |
| 016 | Strategy + cache unit tests + bundled cleanup + comprehensive review | ✅ | (pending commit) |

**Overall**: 12/49 tasks (24%). Phase 0: 5/5 ✅. Phase A: 7/7 ✅. Phase B (consumer migrations): 0/9 — next.

## Phase A Gate (USER STEP — required before Phase B starts)

Per project CLAUDE.md risk-tier cadence, Phase A is a "required" trigger for the MSAL binding regression test. **The bug fix from task 011 (UPN-as-loginHint) is in source but not yet in any deployed consumer bundle** — Phase B's per-consumer rebuilds are where it takes effect. To validate the fix ahead of full Phase B, the user can:

1. **Option A** — manual test in browser DevTools per `.claude/patterns/auth/spaarke-sso-binding.md#verification-after-changes`. Run the localStorage/sessionStorage/cookie clear snippet, close + reopen browser, navigate to a Spaarke surface. PASS = no popup, authority shows tenant GUID (not `/organizations`).
2. **Option B** — one-off rebuild + redeploy of a SINGLE consumer (e.g., SpaarkeAi Code Page) to validate the fix lands cleanly before doing all ~30 in Phase B.
3. **Option C** — skip the manual gate and proceed to Phase B; the per-consumer rebuilds in B will validate as they ship.

Defer to user.

## Last Completed Task (016 — strategy + cache tests + Phase A finale)

- Created [`tests/BrowserMsalStrategy.test.ts`](../../src/client/shared/Spaarke.Auth/tests/BrowserMsalStrategy.test.ts) — 10 test cases mocking `@azure/msal-browser` entirely (acquire cascade, JWT-exp rejection, logout paths, error fallbacks, `getMsalInstance` lifecycle). 80% line coverage on the file.
- `tests/InMemoryCache.test.ts` already covers cache layer (10 cases from task 012, 90% line coverage).
- **39/39 tests passing** across the package (was 27/28 — gained 11, lost the 1 pre-existing baseline).

**Bundled cleanup** (queued by tasks 012-015 as out-of-scope):
- Deleted ~50 stale `src/**/*.d.ts` + `src/**/*.js` + `.map` files (pre-v2 misconfigured build artifacts)
- Added `.gitignore` to the package preventing the regression
- Removed orphaned legacy types (`ITokenResult`, `ITokenStrategy`, `TokenCacheEntry`, `TokenSource`) — no consumers since task 012
- Fixed `tests/config.test.ts` to match the v2 contract (resolveConfig throws when clientId missing)

**Comprehensive code review** (D-AUTH-QUALITY-GATE-DEFERRAL collected) — 10 v2 source files reviewed. Verdict: APPROVED. 0 critical, 2 warnings (both fixed in same commit), 0 AI code smells, 0 ADR violations:
1. `SpaarkeAuthProvider.dispose()` now cascades to `_cache.clearCache()` → prevents MSAL-instance leak on `initAuth()` re-init
2. `BrowserMsalStrategy._validate()` near-expiry rejection bumped from `console.warn` → `console.error` with `msToExpiry` + `bufferMs` payload (App Insights signal for upstream refresh-logic problems)

## Known Issues / Planned Consequences

- **Consumer compile breaks (planned per Phase B)**: 3 files in `src/solutions/` + `src/client/code-pages/` still import `publishToken`, `BridgeStrategy`, or `XrmStrategy` — no longer exist. Migrated in Phase B tasks 024 + 026 + LegalWorkspace work.
- **PCF bundle.js stale (INV-8)**: `SpeDocumentViewer` bundle still references deleted symbols. Rebuilt in Phase B task 028.
- **Bug fix not yet deployed**: Task 011's UPN fix is in source but not yet in any deployed consumer bundle. See Phase A Gate above for options.

## Key Design Decisions (cumulative)

- **D-AUTH-PROVIDER-CONSTRUCTOR**: `SpaarkeAuthProvider(userConfig?, strategy?)` — strategy defaults to `BrowserMsalStrategy`.
- **D-AUTH-TENANT-RESOLUTION**: JWT `tid` + Xrm frame-walk fallback.
- **D-AUTH-LOGIN-HINT-FIX**: `resolveLoginHint(msal)` prefers UPN sources.
- **D-AUTH-JWT-EXP-VALIDATION**: Symmetric in `BrowserMsalStrategy._validate` + `InMemoryCache._isFresh`. 5-min buffer. _Updated 016_: near-expiry log is now `console.error` (was `console.warn`).
- **D-AUTH-CACHE-INVALIDATE-VS-CLEAR**: `invalidate()` (in-memory only) vs `clearCache()` (cascades). InMemoryCache.logout() also cascades.
- **D-AUTH-USEAUTH-NON-REACTIVE-PHASE-A**: useAuth is a plain function; React reactivity deferred to a later iteration.
- **D-AUTH-ESLINT-PLUGIN-FOR-REFERENCEABILITY**: `@typescript-eslint/eslint-plugin` only so existing inline disables reference a defined rule.
- **D-AUTH-LOGOUT-SLIM**: Task 014 ships client-side only (MSAL.logoutPopup + BroadcastChannel). Server-side OBO cleanup + `/api/auth/logout` deferred — hygiene, not access control. Real revocation comes with CAE (task 061).
- **D-AUTH-VERSION-HARDCODED**: VERSION in `src/version.ts` as a hardcoded literal kept in sync with package.json. Avoids bundler-config fragility.
- **D-AUTH-BROADCAST-LISTENER-IN-PROVIDER**: Broadcast listener registered in SpaarkeAuthProvider constructor (moved from initAuth.ts in 015). Provider self-contained for init-time side effects; dispose() unifies cleanup. _Updated 016_: dispose() also cascades `_cache.clearCache()` to prevent leak on re-init.
- **D-AUTH-QUALITY-GATE-DEFERRAL**: Per-task review deferred — collected in single comprehensive review at task 016. Done; verdict APPROVED.

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0 progress: 5/5 ✅
- Phase A progress: 7/7 ✅
- Phase B progress: 0/9 (starting next)
- Overall: 12/49 tasks complete (24%)
- Library version: `@spaarke/auth@2.0.0`
- Tests: 39/39 passing; BrowserMsalStrategy 80% line coverage, InMemoryCache 90%

## Resume Plan — Phase B (consumer migrations 020-028)

Phase B has 9 consumer migrations, most parallel-safe. With the user's batched-parallel preference in mind (per `feedback_proactive_parallel_dispatch`), I'll:
1. On `continue`, read TASK-INDEX.md for Phase B's parallel-execution structure
2. Identify the first unblocked parallel group
3. Dispatch a wave with ONE message + MULTIPLE Skill invocations (one per task)
4. Mandatory build verification between waves (per task-execute Step 0.3 wave protocol)
5. **Constraint reminder**: each Phase B consumer touched gets an automatic MSAL regression test trigger (per project CLAUDE.md risk-tier cadence) — that may be a per-wave gate the user wants to handle manually rather than autonomously.

Worth confirming the autonomous-vs-gated preference for Phase B before starting.
