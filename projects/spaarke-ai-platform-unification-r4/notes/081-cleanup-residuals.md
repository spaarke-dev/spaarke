# Task 081 — Pre-Wrap-up Residuals Cleanup

> **Date**: 2026-05-27
> **Trigger**: R4 final review #3 surfaced 3 outstanding residuals; 2 were quick + cheap to clear before 090 wrap-up
> **Status**: ✅ Both fixes complete + verified

---

## Fix 1: useKeyboardShortcuts TypeError — REAL production bug eliminated

### Before
```typescript
export function useKeyboardShortcuts(options: UseKeyboardShortcutsOptions): void {
  const { commands, context, enabled = true } = options;
  // ... handleKeyDown ...
  // const command = commands.find(...);  ← CRASH if commands is undefined
}
```

### After
```typescript
export function useKeyboardShortcuts(options: UseKeyboardShortcutsOptions): void {
  // R4 task 081: default `commands` to [] to prevent runtime TypeError when a
  // consumer renders before its command list is loaded (e.g., async fetch path).
  // TypeScript marks the prop as required but cannot enforce runtime arrival.
  const { commands = [], context, enabled = true } = options;
}
```

### Impact
- The crash was blocking `CommandBar.test.tsx` (23 tests) from running entirely
- Test suite count jumped: **1051/1051 → 1074/1074 passing** (23 previously-uncounted tests now run)
- Any production caller rendering before its commands were loaded would have crashed the app
- Hook now becomes a benign no-op when commands is undefined (early `[].find(...)` → undefined → return)

### Discovery history
- Task 074 sub-agent flagged: "1 test-suite (CommandBar.test.tsx) fails to load (Jest worker child-process exceptions)"
- Task 077 sub-agent flagged: "Pre-existing issue noted (unrelated to 077): CommandBar.test.tsx worker crash from TypeError in useKeyboardShortcuts.ts"
- Task 078 sub-agent flagged: "1 test-suite (CommandBar.test.tsx) fails to RUN due to a jest-worker crash in useKeyboardShortcuts.ts:32 — verified PRE-EXISTING on pristine HEAD via git stash"

Three sub-agents in a row identified it as "real bug, not our scope". Task 081 closes it.

### Verification
```
$ npx jest src/components/PageChrome/__tests__/CommandBar.test.tsx
Test Suites: 1 passed, 1 total
Tests:       23 passed, 23 total
```

Full suite: 1074/1074 ✅

---

## Fix 2: Deploy artifact tracking — durable master-level governance

### Problem
- `.gitignore` line 122 has `deploy/` rule
- But 272 files in `deploy/api-publish/*` were tracked in git **before** the rule was added
- Once tracked, `.gitignore` cannot untrack — only `git rm --cached` does
- R4 task 060 (B-1) did this on the R4 branch; master never received the fix
- Other projects (auth-v2, BFF remediation, matter-ui-r1) re-tracked them during deploys
- After R4 synced master on 2026-05-27, the tracked artifacts came back

### Fix
```
$ git rm --cached -r deploy/api-publish/
... (272 files removed from index)

$ git ls-files deploy/api-publish/ | wc -l
0

$ ls deploy/api-publish/ | wc -l
189   # ← files still on disk; build/deploy workflow unaffected

$ git check-ignore deploy/api-publish/Sprk.Bff.Api.dll
deploy/api-publish/Sprk.Bff.Api.dll   # ← gitignored as expected
```

### Why this is durable
Once R4 merges to master:
1. Master inherits the untracking (272 files removed from index)
2. `.gitignore` line 122 (`deploy/`) is now actually load-bearing
3. Future `git add deploy/api-publish/...` is ignored
4. Only `git add -f` (force) can re-add — which requires deliberate operator action

Future projects can no longer accidentally re-track artifacts.

### Verification
- `git ls-files deploy/api-publish/` returns 0 files
- `ls deploy/api-publish/` shows 189 files (current publish artifacts on disk)
- `git check-ignore` confirms gitignore covers
- `dotnet build src/server/api/Sprk.Bff.Api/` still produces 0 errors
- BFF deploy workflow unaffected (script uses files from disk, not git)

---

## Item 3 (NOT fixed in 081) — Lint warning carry-overs

Per R4 final review #3, these stay as documented intentional carry-overs:

- **20 `react-hooks/exhaustive-deps` warnings** — each is case-by-case judgment (refs, intentional run-once patterns, would-cause-infinite-loop deps). Reviewing all 20 → ~1-2h, yields maybe 3-5 closures.
- **2 `no-explicit-any` casts in DatasetGrid/GridView.tsx:144** — on Fluent v9 `DataGrid.onSelectionChange` callback `(_e: any, data: any)`. Proper fix needs `NonNullable<DataGridProps['onSelectionChange']>` type or similar — not trivial.

Both are documented in `notes/078-eslint-sweep.md`. Closing R4 with these as documented technical debt is acceptable per operator decision 2026-05-27.

---

## Updated R4 state

| Metric | Value |
|---|---|
| Work tasks complete | **45/47 ✅ (96%)** |
| Remaining | 090 (wrap-up — operator-gated) |
| UI.Components test suite | **1074/1074 passing** (was 1051) |
| Git-tracked deploy artifacts | **0** (was 272) |
| BFF build | 0 errors |
| Master sync | 14 ahead, 0 behind |

---

## Why this matters for the wrap-up

Both fixes close real residuals that would have rotted past R4:
- The TypeError would have eventually surfaced in a production crash report (anyone calling the hook without a populated commands list)
- The deploy artifact tracking issue would have continued plaguing every future project that deploys BFF

By fixing them here, R4 ships cleaner and the next project (whether iframe-wizards, Graph SDK upgrade, or something else) inherits a healthier baseline.

---

*Closes the residuals discussion. Ready for 090 wrap-up gate.*
