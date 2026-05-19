# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: not-started (next task)
> **Active Phase**: ✅ Phase 0 COMPLETE — Phase A pending gate
> **Last Updated**: 2026-05-18

## 🚨 PHASE 0 GATE — REGRESSION TEST REQUIRED BEFORE PHASE A

Per project CLAUDE.md "Regression test after every Workstream":

> After each Workstream (Pre-flight, A, B, C, D, E, B4, F) completes, run the MSAL binding regression test from [`spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md#verification-after-changes)

**Pre-flight (Phase 0) is now complete.** Phase A (Core library rebuild — 7 tasks) should NOT start until the regression test passes.

```javascript
// In Edge DevTools console
localStorage.clear(); sessionStorage.clear();
document.cookie.split(';').forEach(c => {
  document.cookie = c.split('=')[0].trim() + '=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/';
});
// CLOSE BROWSER. Reopen. Navigate to SpaarkeAi.
// PASS: no popup; console shows `authority: https://login.microsoftonline.com/{actual-tenant-guid}/`
// FAIL: popup OR `/organizations` in the authority
```

**Note**: Phase 0 changes were docs-only (renames, banners, pointer, prohibition section). No MSAL code was modified, so the regression test should pass trivially. Still run it before Phase A as the project's enforced phase gate.

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | 010 - Define AuthStrategy interface + token result types (Phase A — Core library rebuild) |
| **Step** | Begin Step 1 of task 010 after Phase 0 gate clears |
| **Status** | not-started (gate-blocked) |
| **Next Action** | (1) Run MSAL regression test → (2) `work on task 010` |

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- **Phase 0**: ✅ 5/5 complete (001, 002, 003, 004, 005 — all PF-* applied)
- **Phase A**: 0/7 (gate-blocked pending regression test pass)
- Overall: 5/49 tasks complete

## Phase 0 Summary

| Task | PF | Status | Commit |
|------|----|--------|--------|
| 001 | PF-1, PF-2, PF-3 | ✅ | `c2198007` |
| 002 | PF-4..PF-10 | ✅ | `281f7210` |
| 003 | PF-11 (verified) | ✅ | `f58317b0` |
| 004 | PF-12 | ✅ | `5b04b6ff` |
| 005 | PF-13 | ✅ | (this commit) |

## Phase A Preview (after gate clears)

| Task | Title | Parallel Group |
|------|-------|----------------|
| 010 | Define AuthStrategy interface + token result types | No (foundation) |
| 011 | Implement BrowserMsalStrategy | A-Parallel-1 |
| 012 | Implement in-memory cache wrapper with JWT exp validation | A-Parallel-1 |
| 013 | Implement useAuth() hook | No (depends on 011, 012) |
| 014 | Implement logout() API | A-Parallel-2 |
| 015 | Version stamp + BroadcastChannel invalidation listener | A-Parallel-2 |
| 016 | Strategy + cache unit tests | A-Parallel-2 |

Phase A is the first phase that unlocks **parallel execution** — A-Parallel-1 (tasks 011+012) and A-Parallel-2 (tasks 014+015+016) can run as parallel `task-execute` invocations in a single Claude Code message. Tasks 010 and 013 are sequential bottlenecks.
