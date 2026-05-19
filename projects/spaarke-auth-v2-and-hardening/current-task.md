# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: ⏸️ STOPPED ON 014 — human design decision needed
> **Active Phase**: A — Core library rebuild (in progress)
> **Last Updated**: 2026-05-19

## 🎯 SESSION CHECKPOINT — 9/49 TASKS DONE

Phase A advanced 010→013 in this session (commits `4c840994`, `654ddde0`). Task 014 was loaded but stopped before implementation because of a design discrepancy that needs a human decision — see "🔔 Human Decision Needed" below.

## Quick Recovery (Next Session)

| Field | Value |
|-------|-------|
| **Task** | 014 - logout API (client + server `/api/auth/logout` + Redis OBO invalidation) |
| **Step** | Step 0 — design discrepancy resolution pending |
| **Status** | not-started (blocked on design decision) |
| **Next Action** | Read 🔔 below, choose A/B/C, then `continue` |

## How to Resume

```
continue
```

After choosing the invalidation strategy below.

## 🔔 Human Decision Needed — Task 014 Server-Side OBO Invalidation Strategy

**The contradiction**: Task 014 POML step 5 says to "enumerate Redis keys matching `sdap:obo:*:{oidHash}:*` and delete." But the actual OBO cache scheme in [`GraphTokenCache.cs:61`](../../src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs#L61) is keyed by **token hash only**: `sdap:graph:token:{base64-sha256(userToken)}`. There is no oid in the cache key, so a SCAN by oid pattern is not possible without reshaping the cache.

**Why it matters**: "Logout" should mean "all this user's tokens are dead immediately." If we invalidate only the single token used for the /logout call, other concurrently-cached OBO tokens for the same user remain valid until their 55-minute TTL expires. That's a real security gap (e.g., user logs out from tab A; tab B's cached OBO entry still serves Graph calls for up to 55 min).

**Three options**:

| Option | Mechanism | Effort | Security |
|---|---|---|---|
| **A** — Token-hash only | Compute hash of the JUST-SENT logout-call token; `RemoveTokenAsync(hash)`. | Minimal (1 LOC). | Insufficient: only kills the one token used for /logout. |
| **B** — Reshape cache key | Change `sdap:graph:token:{tokenHash}` → `sdap:graph:token:{oidHash}:{tokenHash}`. SCAN by `{oidHash}:*` on logout. Modify `GraphTokenCache` Get/Set/Remove + caller signatures. | Medium — touches hot path of every OBO call. | Full per-user invalidation. |
| **C** — Secondary index ⭐ | Keep `sdap:graph:token:{tokenHash}` as-is; ALSO maintain `sdap:graph:tokens-by-user:{oidHash}` as a Set of token hashes. On logout, read set → DEL each → DEL set. Two Redis ops per OBO cache write (negligible — already going to Redis). | Medium — additive, no signature breakage. | Full per-user invalidation; preserves token-hash isolation. |

**My recommendation**: **Option C**. Additive, preserves the existing cache contract for callers, no key-format migration, full invalidation semantics. Two extra Redis hops per write are negligible (Redis is in-VNet, ~1ms each).

**One related question**: should logout ALSO invalidate the `AgentTokenService` cache (a second OBO surface in `Api/Agent/AgentTokenService.cs:28`)? Probably yes for completeness — I haven't read it in detail yet but I'll handle it the same way as `GraphTokenCache`. Flag if you have a different intent.

## Phase A Progress (3 of 7 tasks remaining)

| # | Task | Status | Commit |
|---|------|--------|--------|
| 010 | AuthStrategy interface + stub + delete 3 obsolete files | ✅ | `7466978d` |
| 011 | Full BrowserMsalStrategy (+ UPN bug fix) | ✅ | `983de29a` |
| 012 | InMemoryCache wrapper (replaces CacheStrategy + SessionStorageStrategy) | ✅ | `4c840994` |
| 013 | useAuth() React hook (function-based public API) | ✅ | `654ddde0` |
| 014 | logout API (client + server `/api/auth/logout` + Redis OBO invalidation) | ⏸️ | blocked on design decision |
| 015 | VERSION constant + BroadcastChannel listener | 🔲 | — |
| 016 | BrowserMsalStrategy + InMemoryCache unit tests | 🔲 | — |

After 016: comprehensive code-review + adr-check (deferred per risk-tier cadence), then Phase A gate — **user runs MSAL regression test**.

## Session Summary (this session, what changed)

- **Task 012** — `InMemoryCache.ts` wraps any `AuthStrategy` with JWT-`exp` freshness (5-min buffer). `SpaarkeAuthProvider` simplified to single `_cache: InMemoryCache` reference. `CacheStrategy.ts` + `SessionStorageStrategy.ts` deleted. 10/10 new tests pass. Commit `4c840994`.
- **Task 013** — `useAuth()` function-based React hook (no token-string field on surface). `AuthenticatedFetchFn` type. `logout` stub. ESLint installed + `.eslintrc.json` with `no-restricted-syntax` rule banning `Bearer template-literal` outside `authenticatedFetch.ts` — positively verified. 6/6 new tests pass. Commit `654ddde0`.

## Known Issues / Planned Consequences

- **Pre-existing test failure**: `tests/config.test.ts:6` expects default clientId `170c98e1-...` that `config.ts` removed in commit `9e480d75`. Stale test; not a regression. Fix candidate: task 016.
- **Orphaned legacy types in `types.ts`**: `ITokenResult`, `ITokenStrategy`, `TokenCacheEntry`, `TokenSource`. Cleanup candidate: task 016.
- **Stale `src/**/*.d.ts` and `src/**/*.js` files in git** — pre-v2 misconfigured build outputs (tsconfig now emits to `dist/`). Cleanup candidate: task 016.
- **Consumer compile breaks (planned per Phase B)**: 3 files in `src/solutions/` + `src/client/code-pages/` still import `publishToken`, `BridgeStrategy`, or `XrmStrategy` — no longer exist. Migrated in Phase B tasks 024 + 026 + LegalWorkspace work.
- **PCF bundle.js stale (INV-8)**: `SpeDocumentViewer` bundle.js still references deleted symbols. Rebuilt + redeployed in Phase B task 028.
- **Bug fix not yet deployed**: Task 011's UPN fix is in `@spaarke/auth` source but not yet in any deployed consumer bundle. Takes effect after Phase B per-consumer rebuilds.

## Key Design Decisions (cumulative)

- **D-AUTH-PROVIDER-CONSTRUCTOR**: `SpaarkeAuthProvider(userConfig?, strategy?)` — strategy defaults to `BrowserMsalStrategy`.
- **D-AUTH-TENANT-RESOLUTION**: JWT `tid` claim + Xrm frame-walk fallback. No MSAL-instance dependency.
- **D-AUTH-LOGIN-HINT-FIX**: `resolveLoginHint(msal)` prefers UPN sources (fixes popup-on-startup).
- **D-AUTH-JWT-EXP-VALIDATION**: Symmetric in BrowserMsalStrategy._validate + InMemoryCache._isFresh. 5-min buffer.
- **D-AUTH-CACHE-INVALIDATE-VS-CLEAR**: `invalidate()` (in-memory only) vs `clearCache()` (cascades to inner).
- **D-AUTH-USEAUTH-NON-REACTIVE-PHASE-A**: useAuth is a plain function; React state deferred to task 015.
- **D-AUTH-LOGOUT-STUB-DEFENSIVE**: Phase A logout stub clears local caches + console.warn (does local side immediately, flags server side as task 014).
- **D-AUTH-ESLINT-PLUGIN-FOR-REFERENCEABILITY**: `@typescript-eslint/eslint-plugin` installed only so existing `eslint-disable-next-line @typescript-eslint/no-explicit-any` comments don't error.
- **D-AUTH-QUALITY-GATE-DEFERRAL**: Per-task `/code-review` + `/adr-check` deferred to comprehensive review after task 016.

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0 progress: 5/5 ✅
- Phase A progress: 4/7 (010 ✅, 011 ✅, 012 ✅, 013 ✅; 014 ⏸️; 015-016 remaining)
- Overall: 9/49 tasks complete (18%)
- Last commit: `654ddde0 feat(auth-v2): useAuth() React hook + Bearer-literal lint rule (task 013)`

## Why the Session Stopped

1. **Design discrepancy on task 014** that genuinely needs human input (security-sensitive auth change). Per root CLAUDE.md §6 Human Escalation Triggers: "Security-sensitive code (auth, secrets, encryption)" + "Ambiguous or conflicting requirements" both apply.
2. **Context budget management**: This session has built up significant context loading 4 task POMLs + their knowledge files + project + module CLAUDE.md files. Task 014 will load substantial server-side context (GraphTokenCache, AgentTokenService, Program.cs, endpoint patterns, plus integration test infrastructure). Stopping now lets the next session start fresh.

## Resume Plan (next session — task 014)

1. **User picks A / B / C** (see 🔔 above) or proposes alternative.
2. Implement client side: `BrowserMsalStrategy.logout()` (MSAL.logoutPopup), `SpaarkeAuthProvider.logout()` (orchestrator), `BroadcastChannel('spaarke-auth-events')` singleton in `initAuth.ts`, `useAuth().logout` flesh-out.
3. Implement server side per chosen option:
   - For Option C: add oid-indexed set to `GraphTokenCache.SetTokenAsync`, add `InvalidateForUserAsync(oidHash)`. Repeat in `AgentTokenService`.
   - New `Api/AuthEndpoints.cs` with `POST /api/auth/logout` — `RequireAuthorization()`, extract `oid` claim (or `sub` fallback), hash it (same SHA256 scheme), call `_graphTokenCache.InvalidateForUserAsync(oidHash)` + `_agentTokenService.InvalidateForUserAsync(oidHash)`. Return 204.
4. Wire `AuthEndpoints.MapAuthEndpoints(app)` in `Program.cs` (near where other endpoint groups map).
5. Integration test: bypass live Redis via `IDistributedCache` test double — verify the InvalidateForUser call removes set + token entries.
6. tsc + dotnet build + tests + commit.
