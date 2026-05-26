# Tab Persistence Fix — R4 A-5b (FR-05 fix phase)

> **Task**: 031 (A-5b)
> **Project**: spaarke-ai-platform-unification-r4
> **Phase**: 3 — UQ-03 verify + fix
> **Predecessor**: Task 030 verification memo at [`notes/tab-persistence-verification-2026-05.md`](tab-persistence-verification-2026-05.md)
> **Operator gate**: Path A approved 2026-05-26 ("Path A: promote `chatSessionId` + `playbookId` from sessionStorage → localStorage")
> **Date**: 2026-05-26
> **Author**: Claude Code (task-execute FULL rigor)
> **Status**: Code complete + build verified. Operator smoke deferred to main-session batch deploy.

---

## 1. Design Decision

**Path A: client-side storage promotion from sessionStorage → localStorage** for the two AI session keys, with a one-shot migration that preserves any in-flight sessionStorage values.

### Why Path A (vs B or C)

| Path | Description | Why not chosen |
|---|---|---|
| **A (chosen)** | Promote `chatSessionId` + `playbookId` from sessionStorage → localStorage; one-shot migration | **Smallest surface; no BFF change; no schema change; operator-approved 2026-05-26.** Per 030 memo §7.2: ~3-5h effort; matches OC-R4-02 "user-feedback gap" framing; preserves BFF Cosmos 90d snapshot as durable source-of-truth. |
| B (BFF-side persistence layer change) | Derive `chatSessionId` from `oid + workspace` so it's stable across close/reopen | Breaks multi-session-per-user model; needs BFF schema change; conflicts with History feature. Rejected in 030 §7.3. |
| C (default-active-tab logic correction) | Modify `restoreFromPersistence()` default selection | Misdiagnosis — tabs DON'T persist at all on close/reopen (Q2 in 030 memo: ❌ NO). The active-tab logic is downstream and doesn't fire. |

### Scope honored (load-bearing per CLAUDE.md §6 + task notes)

- ✅ ONLY promotes the two keys (`AI_SESSION_CHAT_SESSION_KEY`, `AI_SESSION_PLAYBOOK_KEY`)
- ✅ NO promotion of unrelated session state (auth, theme, pane-collapse already use localStorage; pinned-workspaces already use localStorage; nothing else moved)
- ✅ NO Cosmos persistence layer change (that's option B; deferred)
- ✅ NO new `clearChatSession()` / stale-session detector (030 §7.2 optional items; deferred as no forcing function — operator can add later if user feedback warrants)
- ✅ NO key-prefix bump (kept `sprk_ai2_` — operator's call per 030 §7.2; the migration handles transition transparently)

### Placement Justification (CLAUDE.md §10)

**Not applicable** — Path A is entirely client-side. No BFF code, no NuGet packages added, no `Sprk.Bff.Api` changes. Publish-size check not required.

---

## 2. Files Modified

| File | Line range | Change |
|---|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` | L61-89 | Block comment explaining R4 task 031 promotion + ADR-028 compliance note |
| `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` | L66-69 | JSDoc on exported keys updated ("sessionStorage" → "localStorage (was sessionStorage pre-R4 task 031)") |
| `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` | L138-148 | Context-value interface JSDoc updated for chatSessionId / playbookId sections |
| `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` | L214-262 | `readSession()` rewritten — localStorage-first read with one-shot sessionStorage → localStorage migration on first localStorage-aware load |
| `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` | L264-274 | `writeSession()` rewritten — writes to localStorage instead of sessionStorage |
| `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` | L384-396 | Inline comments on `chatSessionId` + `playbookId` `useState` initializers reference R4 task 031 |
| `src/client/shared/Spaarke.AI.Widgets/src/providers/__tests__/AiSessionProvider.test.tsx` | L21-22 | Header doc updated to mention localStorage + migration |
| `src/client/shared/Spaarke.AI.Widgets/src/providers/__tests__/AiSessionProvider.test.tsx` | L340-345 | `beforeEach` clears BOTH storages (was sessionStorage-only) |
| `src/client/shared/Spaarke.AI.Widgets/src/providers/__tests__/AiSessionProvider.test.tsx` | L347-396 | 4 existing tests updated — assertions check `localStorage.getItem(...)` instead of `sessionStorage.getItem(...)` |
| `src/client/shared/Spaarke.AI.Widgets/src/providers/__tests__/AiSessionProvider.test.tsx` | L399-470 | 4 new migration tests added: legacy chatSessionId migration, legacy playbookId migration, localStorage-wins-over-sessionStorage, post-migration sessionStorage-independence |

**Files NOT modified** (intentionally per scope cap):
- `src/client/shared/Spaarke.AI.Widgets/src/index.ts` — re-exports the two key constants; constant values unchanged so re-export is correct as-is
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` — consumes `chatSessionId` via `useAiSession()`; consumer contract unchanged
- `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` — mentions sessionStorage in the storage-layer diagram; doc-drift to be picked up by `doc-drift-audit` at Phase 7 wrap-up, not in scope here
- BFF `SessionPersistenceService.cs`, `StoredSession.cs`, `ChatEndpoints.cs` — Path A is client-only; BFF unchanged

---

## 3. Migration Logic (How the One-Shot Copy Works)

```ts
function readSession(key: string): string | null {
  try {
    // 1. Prefer localStorage (post-R4 source of truth)
    const fromLocal = localStorage.getItem(key);
    if (fromLocal !== null) return fromLocal;

    // 2. If localStorage is empty, check sessionStorage (legacy/migration path)
    const fromSession = sessionStorage.getItem(key);
    if (fromSession !== null) {
      // One-shot migration: copy to localStorage so future reopens find it
      try {
        localStorage.setItem(key, fromSession);
      } catch {
        /* tolerate localStorage quota / private mode */
      }
      return fromSession;
    }

    return null;  // both empty
  } catch {
    return null;  // storage API not available (e.g., some Dataverse webresource sandboxes)
  }
}
```

### Migration matrix

| Pre-state | First read after deploy | localStorage after read | sessionStorage after read |
|---|---|---|---|
| localStorage empty, sessionStorage empty | returns `null` | empty | empty |
| localStorage empty, sessionStorage has `'X'` | returns `'X'` | `'X'` (migrated) | `'X'` (left in place) |
| localStorage has `'A'`, sessionStorage empty | returns `'A'` | `'A'` | empty |
| localStorage has `'A'`, sessionStorage has `'B'` (stale) | returns `'A'` (localStorage wins) | `'A'` | `'B'` (untouched) |

### Why leave sessionStorage in place (don't clear)

Per operator guardrail: "leave sessionStorage alone (don't clear; let it expire naturally with the window)". This:
1. Avoids racing with any other code path that might still consume the legacy entry within the same window
2. Is harmless — subsequent reads short-circuit at the localStorage check (sessionStorage check is never reached after the first migration)
3. Naturally cleans up when the window/process closes (sessionStorage semantics)

### Why keep the same key names (`sprk_ai2_`)

Per 030 memo §7.2 + operator approval: keys hold opaque session/playbook ids only (no PII, no tokens). Changing the prefix to `sprk_ai3_` would force every user to lose their session on deploy day. Keeping the prefix + migrating the values gives zero-impact transition.

---

## 4. Test Coverage

### Unit tests in `AiSessionProvider.test.tsx`

**Updated to localStorage assertions** (4 tests):
- `(c) chatSessionId initialises to null when both storages are empty`
- `setChatSessionId updates chatSessionId in context and persists to localStorage (R4 task 031)`
- `setPlaybookId updates playbookId in context and persists to localStorage (R4 task 031)`
- `chatSessionId and playbookId are restored from localStorage on mount (R4 task 031)`

**New migration tests** (4 tests):
- `R4 task 031: migrates legacy sessionStorage chatSessionId to localStorage on first read`
- `R4 task 031: migrates legacy sessionStorage playbookId to localStorage on first read`
- `R4 task 031: localStorage value wins over sessionStorage when both are present`
- `R4 task 031: subsequent reads after migration use localStorage only (sessionStorage independence)`

**Result**: 28 tests pass (24 original + 4 new). Time: ~1.2s (silent mode).

```
Test Suites: 1 passed, 1 total
Tests:       28 passed, 28 total
```

---

## 5. Build + Bundle-Size Evidence

### `npm run build` in `src/solutions/SpaarkeAi/`

```
> vite build
✓ 3442 modules transformed.
dist/index.html  3,441.20 kB │ gzip: 920.48 kB
✓ built in 12.79s
```

**0 errors, 0 new warnings** (the 3 pre-existing Rollup `/*#__PURE__*/` warnings from Application Insights are unchanged from master).

### Bundle-size delta (NFR-08: budget ≤+50 KB vs 918 KB R3 baseline)

| Measurement | Size (gzip) |
|---|---|
| R3 baseline (per spec NFR-08) | 918 KB |
| Current (R4 task 031 applied) | 920.48 KB |
| **Delta** | **+2.48 KB** |
| Budget | +50 KB |
| Status | ✅ **PASS** (5% of budget consumed) |

The +2.48 KB delta is from added JSDoc comments + the migration logic in `readSession()` (~15 extra lines of code). No new dependencies added.

### TypeScript typecheck

`npm run typecheck` in `@spaarke/ai-widgets` surfaces only **pre-existing errors** that exist on master:
- 2 module-resolution errors (`@spaarke/auth`, `@spaarke/ai-context`) — known B-2 tsc rootDir issue, tracked in spec NFR-05
- 1 implicit-any on streaming state callback — line shifted from 467 → 520 due to added comments but the **code is unchanged** (`prev` parameter literally identical)
- Many `LegalWorkspace/` rootDir leaks — pre-existing, B-2 issue

Verified by stash + re-typecheck on master baseline (same 3 AiSessionProvider errors at L56, L58, L467) → confirmed not regressions.

---

## 6. Quality Gates (Step 9.5)

### code-review (inline, FULL rigor)

| Check | Status | Notes |
|---|---|---|
| ADR-028 invariants | ✅ PASS | No token snapshots; opaque ids only; no `accessToken` props/state introduced |
| ADR-022 (React 19) | ✅ PASS | No React API changes; no fallbacks |
| ADR-012 (shared lib boundary) | ✅ PASS | Change contained to `@spaarke/ai-widgets`; no cross-package coupling |
| Migration safety | ✅ PASS | One-shot, idempotent, non-destructive (sessionStorage untouched), localStorage-wins precedence |
| Storage error handling | ✅ PASS | Outer try/catch + inner try/catch on `localStorage.setItem` (tolerates private mode / quota) |
| PII / secrets | ✅ PASS | Only opaque session/playbook ids stored |
| BFF Hygiene §10 | ⏭️ N/A | No BFF code modified |
| Bundle-size delta | ✅ PASS | +2.48 KB vs 918 KB baseline (5% of 50 KB budget) |

### adr-check (inline, FULL rigor)

No ADR violations detected. Conformance:
- **ADR-028**: Function-based auth preserved; no token persistence; opaque-id-only storage
- **ADR-022**: React 19 unchanged
- **ADR-012**: Shared-library boundary preserved (change is internal to `@spaarke/ai-widgets`)
- **ADR-010, ADR-013**: N/A (no BFF DI or AI architecture touched)

---

## 7. UI Test Definitions (for main-session deploy verification)

Chrome integration not invoked from sub-agent context. Tests below are written for the main-session `ui-test` skill or manual operator smoke after deploy.

### Test 1: Browser refresh — tabs persist (regression)
1. Sign in, open 3 non-Home tabs in SpaarkeAi; note active tab
2. DevTools → Application → Local Storage: confirm `sprk_ai2_chatSessionId` is set
3. Press Ctrl+R; wait for full load
4. **Expected**: 3 tabs restored in same order; active tab unchanged; no console errors

### Test 2: Browser close/reopen — tabs persist (THE fix)
1. Sign in, open 3 non-Home tabs; note active tab
2. DevTools → Local Storage: confirm `sprk_ai2_chatSessionId` value
3. Close ENTIRE browser application (full process exit, not just the SpaarkeAi tab)
4. Reopen browser; navigate to SpaarkeAi (may need re-auth)
5. **Expected**: All 3 tabs restored; active tab matches pre-close; `sprk_ai2_chatSessionId` in Local Storage matches pre-close value

### Test 3: Migration (sessionStorage → localStorage)
1. Pre-seed state via DevTools Console:
   ```js
   localStorage.removeItem('sprk_ai2_chatSessionId');
   sessionStorage.setItem('sprk_ai2_chatSessionId', 'pre-deploy-test-session');
   ```
2. Reload SpaarkeAi
3. DevTools → Local Storage: confirm `sprk_ai2_chatSessionId` is now `'pre-deploy-test-session'` (migration copied it)
4. DevTools → Session Storage: confirm value still present (intentionally not cleared)
5. Close + reopen browser; navigate back
6. **Expected**: Migrated value still in localStorage; sessionStorage now empty; provider initializes with migrated session id

### Test 4: ADR-028 compliance regression guard
1. DevTools → Local Storage: scan for `*token*` / `*bearer*` / `*spaarke*token*` keys
2. DevTools → Session Storage: same scan
3. React DevTools: inspect for `accessToken` props on any component
4. **Expected**: No tokens in either storage; no `accessToken` props (INV-3 + INV-4 preserved)

---

## 8. Known Limitations + Future Work (deliberately out of scope)

Per scope cap. The following items from 030 memo §7.2 were **intentionally not implemented**:

1. **Clear-on-logout affordance** — A `clearChatSession()` hook exposed via `useAiSession()` so the History pane's "Start new conversation" button can explicitly drop the current session id. Without this, users on shared browsers might inherit each other's session id (mitigated by ADR-028: BFF still validates JWT and uses `tenantId` from the token, so the worst case is an empty restore — not a leak). **Defer to operator user-feedback signal.**

2. **Stale-session detector** — Auto-clear `chatSessionId` if BFF GET returns 404 or session is older than N days. Without this, users keep an old session id forever. The mitigating behavior already in `WorkspacePane.tsx` L284: a 404 on the GET is treated as benign and the user lands on the BFF default workspace. **Defer to operator user-feedback signal.**

3. **Cross-browser shared session** — Chrome + Edge on the same machine will now share a `chatSessionId` (origin-scoped localStorage). This is consistent with existing behavior for `spaarke:workspace:pinned-list` and pane-collapse state which were already in localStorage. **Not a regression; documented for clarity.**

4. **localStorage indefinite persistence** — Unlike sessionStorage, localStorage persists until manually cleared or the browser evicts it (rare, only under storage pressure). If a user has a long-running session id from months ago and the BFF Cosmos document has been GC'd past the 90-day retention, the GET will return 404 and the user lands on the BFF default workspace. This is the same fallback path that existed pre-fix; just hit less often. **Acceptable per 030 §7.2 risk analysis.**

If any of these limitations cause operator-reported user pain in production, they become candidates for R5 backlog.

---

## 9. Deviations from 030's Recommendation

**None.** Implementation matches 030 §7.2 Path A exactly:
- ✅ `chatSessionId` + `playbookId` promoted from sessionStorage → localStorage
- ✅ Same key prefix (`sprk_ai2_`) retained
- ✅ One-shot migration from sessionStorage in `readSession()`
- ✅ No BFF change
- ✅ No PII / no tokens persisted (opaque ids only)
- ✅ Tests extended (jest unit tests in `AiSessionProvider.test.tsx`)

The optional items 030 §7.2 mentioned (`clearChatSession()`, stale-session detector) were explicitly excluded by operator scope cap and documented in §8 above as deferred.

---

## 10. Build / Test Summary

| Step | Result |
|---|---|
| `npm run build` in `src/solutions/SpaarkeAi/` | ✅ 0 errors; 920.48 KB gzip (Δ +2.48 KB vs 918 KB baseline) |
| Unit tests in `@spaarke/ai-widgets` (`AiSessionProvider.test.tsx`) | ✅ 28 / 28 pass (24 updated + 4 new) |
| `npm run typecheck` in `@spaarke/ai-widgets` | ⚠️ Pre-existing B-2 tsc rootDir errors; none caused by this change (verified by stash + re-test) |
| BFF `dotnet build` | ⏭️ N/A — no BFF code touched |
| `dotnet publish` size check (NFR-01) | ⏭️ N/A — no BFF code touched |
| New HIGH-severity CVEs from `dotnet list package --vulnerable --include-transitive` | ⏭️ N/A — no NuGet packages added |
| `/code-review` quality gate | ✅ Inline review passed (§6 above) |
| `/adr-check` quality gate | ✅ Inline check passed (§6 above) |

**Deploy NOT performed** per task guardrail — main session batches deploys at end of R4.

---

## 11. Operator Smoke (REQUIRED post-deploy)

To close FR-05, operator must run UI tests 1 + 2 (§7) on the deployed SpaarkeAi after the main-session batch deploy. Tests 3 + 4 are recommended for additional rigor.

**Expected outcome**: All 4 tests pass. Specifically test 2 (browser close/reopen) — which was broken pre-fix per 030 §3 — should now show tabs restored. FR-05 acceptance ("operator-confirmed end-to-end behavior matches expectation") becomes satisfied at that point.

If test 2 fails (tabs still don't return after browser close/reopen), the most likely causes are:
1. localStorage was cleared by browser (e.g., "Clear data on exit" setting enabled) — user-side configuration; out of code scope
2. BFF Cosmos document GC'd past 90-day retention — expected fallback to default workspace
3. JWT tenant mismatch on reopen (e.g., user switched accounts) — expected; the BFF tenant scoping would return 404 for the old `chatSessionId`

None of those are bugs in this fix. Diagnose first; if none apply, escalate.

---

*Implementation complete. Memo author: Claude Code task-execute (FULL rigor). Next: main-session deploy batch → operator smoke → FR-05 acceptance.*
