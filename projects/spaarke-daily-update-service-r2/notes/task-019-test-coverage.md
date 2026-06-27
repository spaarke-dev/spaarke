# Task 019 — Test Coverage Report

**Date**: 2026-06-18
**Task**: 019 — Unit tests for 3 split hooks + smoke test mounting `DailyBriefingApp` (NFR-05)
**Status**: ✅ Acceptance criteria met

## Test Infrastructure

- **Runner**: Jest 30.x + ts-jest 29.x + jsdom (mirrors `@spaarke/auth` canonical pattern in `src/client/shared/`)
- **Config**: `jest.config.cjs` + `tsconfig.test.json` (separate from build tsconfig — `isolatedModules: false` + `noEmit: false` + path aliases for peer-dep mocks)
- **Test directory**: `test/` (per task POML)
- **Local mocks**: `test/__mocks__/spaarke-auth.ts`, `test/__mocks__/spaarke-ui-components.tsx`, `test/__mocks__/spaarke-ui-components-services.ts` — route peer deps to test-local stubs so the package compiles in test context without installing every peer.
- **Setup**: `test/jest.setup.ts` — `@testing-library/jest-dom` matchers + `matchMedia` polyfill for Fluent v9.

## devDependencies Added

(Confined to `devDependencies` per Wave 6 coordination — task 016 owns the `exports` map.)

```json
"@testing-library/dom": "^10.4.0",
"@testing-library/jest-dom": "^6.4.0",
"@testing-library/react": "^16.0.0",
"@types/jest": "^30.0.0",
"jest": "^30.2.0",
"jest-environment-jsdom": "^30.2.0",
"ts-jest": "^29.4.4"
```

Scripts added: `test`, `test:watch`, `test:coverage`, `test:ci`.

## Test Results

```
Test Suites: 4 passed, 4 total
Tests:       19 passed, 19 total
Time:        8.6 s
```

### Per-suite breakdown

| Suite | Tests | Assertions covered |
|-------|-------|--------------------|
| `useBriefingNotifications.test.ts` | 5 | idle when webApi null; fetch + group on mount; per-channel error tolerance; error surfacing; `refetch()` re-triggers fetch. |
| `useBriefingPreferences.test.ts` | 6 | idle when webApi null; idle when userId empty; loads on mount; keeps defaults on fetch failure; `updatePreferences()` optimistic merge + persist; save-failure error surface. |
| `useBriefingActions.test.ts` | 6 | actions return `false` when webApi null; `markAsRead` bumps `refresh` on success; failure does NOT bump; `markAllAsRead` bumps; `dismissAll` delegates to `markAllAsRead`; `refresh` monotonic across calls. |
| `DailyBriefingApp.smoke.test.tsx` | 2 | mounts with mocked `Xrm` + `authenticatedFetch`; asserts `/api/ai/daily-briefing/narrate` POST fires with non-empty `categories` + `channels` payload; asserts the "Overdue Tasks" channel meta renders in DOM. |

### Coverage

```
File                          | % Stmts | % Branch | % Funcs | % Lines
------------------------------|---------|----------|---------|--------
All files                     |   78.06 |    55.42 |   82.92 |   81.25
 components/DailyBriefingApp  |   64.51 |    39.02 |   71.42 |   67.07
 hooks/useBriefingActions     |   92.85 |    87.50 |  100.00 |   92.00
 hooks/useBriefingNotifications|  97.14 |    84.61 |  100.00 |  100.00
 hooks/useBriefingPreferences |   82.50 |    57.14 |   83.33 |   89.18
```

The three hook coverage figures are above the practical 80% bar for stmts/lines on the binding split (`useBriefingActions` and `useBriefingPreferences` both have 100% function coverage on the public hook export; `useBriefingNotifications` has 100% line coverage).

`DailyBriefingApp.tsx` coverage is lower (~64%) because the smoke test deliberately exercises only the happy-path render, not every branch (e.g., empty state, all-caught-up, narration unavailable). Per task NOTE: P2a sub-list rendering tests come in task 024, not here.

## Acceptance Criteria — Verification

- ✅ "All 4 test files exist; `npm test` green." — Confirmed; all 4 files in `test/`, all 19 tests pass.
- ✅ "Smoke test asserts `/narrate` fetch fires with non-empty payload." — Confirmed in `DailyBriefingApp.smoke.test.tsx` (asserts `authenticatedFetch` called with `/api/ai/daily-briefing/narrate`, method=POST, body parses to JSON containing `categories` + `channels` arrays + `totalNotificationCount > 0`).
- ✅ "Each hook test uses independent mocks (no shared state)." — Confirmed; each test file creates its mocks via `jest.mock(...)` at module top + `beforeEach` resets, and each `it` block calls `makeWebApi()` to allocate a fresh webApi mock instance.

## Coordination Notes

- Did NOT modify `package.json` `exports` map (task 016 owns).
- Did NOT modify source files in `src/` (task 015 owned organization).
- Did NOT modify `tasks/TASK-INDEX.md` (per Wave 6 contract — task 019 itself is NOT marked complete here; the orchestrating agent updates the index).
- Did NOT commit or push (per task brief).
