# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: 🎉 **PHASE A COMPLETE + END-TO-END VALIDATED IN PRODUCTION**
> **Active Phase**: A done. Phase B (consumer migrations 020-028) NEXT.
> **Last Updated**: 2026-05-19 (post-hotfix validation)

## Quick Recovery (Next Session)

| Field | Value |
|-------|-------|
| **Status** | Phase A validated end-to-end on spaarkedev1. SpaarkeAi consumer running v2 correctly: no popup, no errors, chat resolves. |
| **Next Action** | `continue` to start Phase B (consumer migrations 020-028 — many parallel-safe; will batch via multiple Skill invocations per memory `feedback_proactive_parallel_dispatch`). |

## How to Resume

```
continue
```

## 🎯 What happened in this session (chronological)

### Session opened with Phase A 6/7 → completed 7/7 → discovered bugs → hotfixed → validated

1. **Task 016 shipped** (commit `3e46f0ad`) — BrowserMsalStrategy + InMemoryCache tests, comprehensive code review (verdict: APPROVED with 2 minor warnings fixed), Phase A cleanup bundle (orphaned types removed, stale .d.ts files purged, config.test.ts fixed). 39/39 tests passing.
2. **User triggered Phase A gate** — built @spaarke/auth, rebuilt SpaarkeAi, deployed `sprk_spaarkeai` to spaarkedev1, ran the manual MSAL regression test.
3. **REGRESSION TEST FAILED** — popup on every startup with `AADSTS50058` + `hint=Ralph Schroeder` (display name). Task 011's "UPN fix" was incomplete.
4. **Root cause analysis** — TWO independent bugs found, not one:
   - **Bug A**: `BrowserMsalStrategy.resolveLoginHint()` had a `userSettings.userName` fallback "for compatibility" that re-triggered the display-name-hint bug in Code Page contexts where `userPrincipalName` is undefined.
   - **Bug B**: `resolveRuntimeConfig.ts` never queried `sprk_TenantId` env var — only tried Xrm.organizationSettings.tenantId (returns empty in Code Pages). Authority fell back to `/organizations`. Long-standing "auth doesn't pick up tenant id" pain point.
5. **Hotfix v1 shipped** (commit `811e98b5`) — 4-part fix:
   - Removed userName fallback in resolveLoginHint
   - Added `TENANT_ID: 'sprk_TenantId'` to env var schema
   - Added `tenantId?: string` to IAuthConfig; resolveConfig builds authority from it
   - SpaarkeAi authInit passes `tenantId: getTenantId()` to initAuth
6. **Hotfix v1 INTRODUCED REGRESSION** — name collision in SpaarkeAi `authInit.ts`: the file imported sync `getTenantId` from runtimeConfig AND exported its own async `getTenantId`. Local function shadowed the import at runtime → `tenantId: getTenantId()` was returning `Promise<string>` → `.trim()` on Promise threw 995× TypeError + recursion stack overflow.
7. **Hotfix v2 shipped** (commit `48788c1f`):
   - Renamed import to `getRuntimeTenantId` (alias) — eliminates the shadow
   - Defensive `typeof userConfig.tenantId === 'string'` guard in resolveConfig — won't crash if any future consumer passes a non-string
   - Test coverage for non-string tenantId
8. **VALIDATED END-TO-END** — user confirmed: no popup, chat resolves, tested in normal Edge with cache cleared, fully working.

## Phase A: 7/7 ✅ + 2 hotfix commits

| # | Task | Status | Commit |
|---|------|--------|--------|
| 010 | AuthStrategy interface + delete obsolete strategies | ✅ | `7466978d` |
| 011 | BrowserMsalStrategy (UPN bug fix attempt — see hotfix below) | ✅ | `983de29a` |
| 012 | InMemoryCache wrapper | ✅ | `4c840994` |
| 013 | useAuth() React hook (function-based public API) | ✅ | `654ddde0` |
| 014 | Logout API (client-side SLIM — server deferred to CAE/061) | ✅ | `a4edb443` |
| 015 | VERSION constant + broadcast listener relocation | ✅ | `a85e06ec` |
| 016 | Strategy + cache unit tests + cleanup + comprehensive review | ✅ | `3e46f0ad` |
| — | **Hotfix v1**: 4-part fix for incomplete task 011 (resolveLoginHint userName fallback + sprk_TenantId env var query + IAuthConfig.tenantId + SpaarkeAi authInit wiring) | ✅ | `811e98b5` |
| — | **Hotfix v2**: name collision fix + defensive typeof guard | ✅ | `48788c1f` |

**Validated in production-like environment**: SpaarkeAi deployed to spaarkedev1, end-to-end behavior confirmed by user (no popup, chat resolves, console clean).

## 🚨 CRITICAL FOR PHASE B — DON'T REPEAT THESE BUGS

Each Phase B consumer migration (tasks 020-028) MUST do TWO things to avoid repeating the bugs we just chased:

### 1. Pass `tenantId` to `initAuth()`

Canonical pattern (from SpaarkeAi `authInit.ts` post-hotfix):

```typescript
// Use IMPORT ALIAS to avoid name collision with locally exported async getTenantId
import {
  getBffBaseUrl,
  getBffOAuthScope,
  getMsalClientId,
  getTenantId as getRuntimeTenantId,  // ← REQUIRED alias
} from "../config/runtimeConfig";

await initAuth({
  clientId: getMsalClientId(),
  tenantId: getRuntimeTenantId(),     // ← sync function returning string
  bffBaseUrl: getBffBaseUrl(),
  bffApiScope: getBffOAuthScope(),
  proactiveRefresh: true,
});

// The locally exported async getTenantId stays unchanged (consumers depend on it)
export async function getTenantId(): Promise<string> {
  await ensureAuthInitialized();
  return getAuthProvider().getTenantId();
}
```

### 2. BEFORE editing each consumer's `authInit.ts`:

```bash
grep -E "export.*function getTenantId|export.*getTenantId" path/to/consumer/src/services/authInit.ts
```

If the file exports its own `getTenantId` (most do), use the import alias `getTenantId as getRuntimeTenantId`. Otherwise the local function shadows the import at runtime → Promise instead of string → TypeError 1000s of times + recursion.

**TypeScript `tsc --noEmit` does NOT flag this collision.** Catch it via grep + the new defensive guard in `resolveConfig` will WARN (not crash) if it slips through, but you should still fix the root cause.

## Memory entries added this session

- [`project_auth_v2_baseline_msal_bug.md`](MEMORY) — REWRITTEN to reflect the two-bug root cause and the canonical fix pattern
- [`feedback_name_collision_in_consumer_authinit.md`](MEMORY) — NEW — the import/export shadow pattern Phase B must check
- [`feedback_dont_over_estimate_context.md`](MEMORY) — NEW — don't quote context % I don't know (came up earlier this session)
- [`feedback_proactive_parallel_dispatch.md`](MEMORY) — NEW — on `continue`, batch parallel-safe tasks in one message
- [`feedback_push_back_on_own_framing.md`](MEMORY) — NEW — when user re-frames a question, re-analyze from their constraint instead of defending original options menu

## Key Design Decisions (cumulative)

- **D-AUTH-PROVIDER-CONSTRUCTOR**: `SpaarkeAuthProvider(userConfig?, strategy?)` — strategy defaults to `BrowserMsalStrategy`.
- **D-AUTH-TENANT-RESOLUTION** (UPDATED post-hotfix): JWT `tid` (cached token) → Xrm frame-walk (last resort). The CONFIG-time tenantId comes from `sprk_TenantId` env var (primary) → Xrm.organizationSettings.tenantId (fallback). Consumers pass `tenantId` to `initAuth({...})`; library builds authority.
- **D-AUTH-LOGIN-HINT-FIX** (UPDATED post-hotfix): `resolveLoginHint(msal)` returns UPN from MSAL accounts → Xrm.userPrincipalName → **undefined** (NO display-name fallback). Better to pass no hint than a wrong hint.
- **D-AUTH-JWT-EXP-VALIDATION**: Symmetric in `BrowserMsalStrategy._validate` + `InMemoryCache._isFresh`. 5-min buffer. Near-expiry rejection logs at console.error.
- **D-AUTH-CACHE-INVALIDATE-VS-CLEAR**: `invalidate()` (in-memory only) vs `clearCache()` (cascades). InMemoryCache.logout() also cascades.
- **D-AUTH-USEAUTH-NON-REACTIVE-PHASE-A**: useAuth is a plain function; React reactivity deferred to a later iteration.
- **D-AUTH-ESLINT-PLUGIN-FOR-REFERENCEABILITY**: `@typescript-eslint/eslint-plugin` installed only so existing inline disables reference a defined rule.
- **D-AUTH-LOGOUT-SLIM**: Task 014 ships client-side only (MSAL.logoutPopup + BroadcastChannel). Server-side OBO cleanup deferred — real revocation comes with CAE in task 061.
- **D-AUTH-VERSION-HARDCODED**: VERSION in `src/version.ts` as hardcoded literal kept in sync with package.json.
- **D-AUTH-BROADCAST-LISTENER-IN-PROVIDER**: Listener registered in SpaarkeAuthProvider constructor; dispose() unifies cleanup + cascades `_cache.clearCache()`.
- **D-AUTH-IAUTHCONFIG-TENANT-ID** (NEW from hotfix): `IAuthConfig.tenantId?: string` added. resolveConfig builds authority from it (so consumers don't leak the `login.microsoftonline.com/{tenant}` URL convention). Defensive typeof guard prevents crash on non-string.
- **D-AUTH-QUALITY-GATE-DEFERRAL**: Per-task review deferred — done at task 016 with verdict APPROVED.

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0: 5/5 ✅
- Phase A: 7/7 ✅ + 2 hotfix commits (`811e98b5`, `48788c1f`)
- Phase B: 0/9 (next — consumer migrations 020-028)
- Overall: 12/49 tasks (24%)
- Library version: `@spaarke/auth@2.0.0`
- Tests: 44/44 passing in `@spaarke/auth`
- Deployed: SpaarkeAi on spaarkedev1 (web resource `sprk_spaarkeai` ID `5206a442-3451-f111-bec7-7ced8d1dc988`, 1874 KB)

## Resume Plan — Phase B (consumer migrations 020-028)

**Strategy**: dispatch in batched parallel waves per `feedback_proactive_parallel_dispatch` memory.

**Step 1** when resuming: read `projects/spaarke-auth-v2-and-hardening/tasks/TASK-INDEX.md` to identify Phase B parallel-execution groups. Tasks 020-028 are the 9 consumer migrations.

**Step 2**: For each Phase B wave, dispatch ONE message with MULTIPLE Skill (`task-execute`) calls.

**Step 3**: Each task's pattern:
- Migrate consumer to v2 API (`useAuth()`, `authenticatedFetch`, drop deleted symbols like `publishToken`/`BridgeStrategy`/`XrmStrategy`)
- **Wire `tenantId: getRuntimeTenantId()` with import alias** per "CRITICAL FOR PHASE B" section above
- Rebuild consumer with new `@spaarke/auth@2.0.0`
- Deploy (each consumer has its own deploy script in `scripts/Deploy-*.ps1`)
- Per project CLAUDE.md risk-tier cadence: each consumer rebuild + redeploy IS a regression-test trigger; user runs the manual MSAL test after each consumer ships (or batches them per their preference)

**Known Phase B consumers** (from TASK-INDEX 020-028):
- 020 AiSessionProvider
- 021 SpaarkeAi app (DONE in this session as the gate validator — but the migration task might involve additional updates)
- 022 SpaarkeAi panes
- 023 SprkChat API refactor
- 024 PlaybookBuilder
- 025 DocRelationshipViewer
- 026 AnalysisWorkspace
- 027 SemanticSearch external SPA
- 028 PCF rebuilds (handles INV-8 bundling reality for the PCF surfaces)

After Phase B: Phase C (server hardening 040-049), Phase D (CSP/CAE/security 060-064), Phase E (CI 070-071), Phase F (docs/ADR-027 090).

## Known Issues / Planned Consequences (carry forward)

- **Consumer compile breaks** in some Phase B surfaces (LegalWorkspace, code-pages) that still import deleted v1 symbols — migrated as part of their respective Phase B tasks.
- **PCF bundle.js stale (INV-8)** for `SpeDocumentViewer` and other PCFs — task 028 rebuilds them all.
- **Server-side logout endpoint** deferred — comes with CAE in Phase D task 061.
- **Daily Briefing / CorporateWorkspace still shows popup on user's home page** (user reported earlier) — that's a v1 consumer that hasn't been rebuilt yet. Will be addressed in its Phase B migration task.
