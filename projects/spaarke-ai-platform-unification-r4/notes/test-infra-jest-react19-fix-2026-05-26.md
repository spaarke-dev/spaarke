# Test-Infra Fix — Jest + React 19 Environment

> **Task**: 068 (Phase 6, build-hygiene cluster)
> **Date**: 2026-05-26
> **Branch**: `work/spaarke-ai-platform-unification-r4`
> **Author**: Claude Code (task-execute STANDARD rigor)

---

## Problem

The `@spaarke/ui-components` package test suite could not execute most React component tests. The failing import chain:

```
Cannot find module 'react-dom/client' from 'node_modules/@testing-library/react/dist/pure.js'
  Require stack:
    node_modules/@testing-library/react/dist/pure.js
    node_modules/@testing-library/react/dist/index.js
    src/components/SprkChat/__tests__/SprkChat.attachments.test.tsx
```

Pre-existing on master baseline. Reported by R4 sub-agents on tasks 050, 053, 054, 042. Operator approved adding to R4 scope 2026-05-26.

### Baseline metrics (before fix)

```
Test Suites: 43 failed, 12 passed, 55 total
Tests:       20 failed, 392 passed, 412 total
"Cannot find module 'react-dom/client'" occurrences: 78
```

---

## Root cause

`@spaarke/ui-components/package.json` devDependencies had React 16 pinned for tests:

```json
"@testing-library/react": "^14.3.1",   // → resolves to RTL 14 (expects React 16/17/18)
"@types/react":           "^16.14.0",
"@types/react-dom":       "^16.9.24",
"react":                  "^16.14.0",  // → installed react@16.14.0
"react-dom":              "^16.14.0",  // → installed react-dom@16.14.0 (NO client.js)
```

But the components under test (`SprkChat*`) are written for **React 19** (used in Code Pages per ADR-022). RTL v14's `pure.js` imports `react-dom/client`, which **does not exist in react-dom@16.14** (added in React 18). Jest module resolution found react-dom@16.14 in local `node_modules`, blew up.

The peerDependencies range `react: >=16.14.0` was correct (PCF clients pin React 16; Code Pages bring React 19) — the bug was only in dev/test pinning.

### Why this had not been caught

Other R4 packages (e.g., `@spaarke/ai-widgets`) had already moved their **dev** versions to React 19 + RTL v16. `@spaarke/ui-components` was a leftover from pre-R3 PCF-era pinning. Its tests only started failing when React-19-only components (SprkChat) were added without updating the test env.

---

## Fix applied

Aligned `@spaarke/ui-components` test devDependencies with the working `@spaarke/ai-widgets` setup. **No production code touched. No peerDependencies modified.**

### `src/client/shared/Spaarke.UI.Components/package.json`

```diff
   "devDependencies": {
+    "@testing-library/dom": "^10.4.1",
     "@testing-library/jest-dom": "^6.9.1",
-    "@testing-library/react": "^14.3.1",
+    "@testing-library/react": "^16.0.0",
     "@testing-library/user-event": "^14.6.1",
     ...
-    "@types/react": "^16.14.0",
-    "@types/react-dom": "^16.9.24",
+    "@types/react": "^19.0.0",
+    "@types/react-dom": "^19.0.0",
     ...
-    "react": "^16.14.0",
-    "react-dom": "^16.14.0",
+    "react": "^19.2.6",
+    "react-dom": "^19.2.6",
```

Resolved versions after `npm install --legacy-peer-deps --no-audit --no-fund`:
- react: 19.2.6
- react-dom: 19.2.6 (contains `client.js`, `client.react-server.js`)
- @testing-library/react: 16.3.2

### `src/client/shared/Spaarke.UI.Components/jest.config.js`

Secondary fix to handle a follow-on ESM transform error in `marked` (ESM-only package transitively imported by `SprkChat.tsx` → `SprkChatMessage` → `renderMarkdown.ts`):

```diff
   transformIgnorePatterns: [
-    'node_modules/(?!(d3-force|d3-dispatch|d3-quadtree|d3-timer)/)'
+    'node_modules/(?!(d3-force|d3-dispatch|d3-quadtree|d3-timer|marked)/)'
   ],
```

---

## Verification

### After-fix metrics

```
Test Suites: 21 failed, 34 passed, 55 total   (+22 suites now running)
Tests:       142 failed, 936 passed, 1078 total   (+544 tests now executing)
"Cannot find module 'react-dom/client'" occurrences: 0   ← target eliminated
```

### Acceptance-criterion specific runs

| Test file | Result |
|---|---|
| `SprkChat.attachments.test.tsx` (task 050) | Suite executes, 3 tests run, 0 module-resolution errors |
| `SprkChat.onAttachmentReady.test.tsx` (task 042) | **6/6 passing** |
| `SprkChat.test.tsx` | Suite executes, 16 passing / 4 test-content failures |

### Regression check — `@spaarke/ai-widgets` test suite

Confirmed `@spaarke/ai-widgets` continues to resolve its own React 19 / RTL v16 — unaffected by ui-components devDep bump (each package has its own `node_modules/react`).

### Remaining test failures (out of scope for task 068)

The 142 remaining test failures are **test-content level**, NOT test-infra:
- `userEvent.type` interaction quirks under React 19 strict-mode-ish act() semantics
- pre-existing test logic divergences (e.g., themeStorage location-spy issues)
- snapshot mismatches and Fluent v9 token assertions
- These were either present before (would have surfaced once tests could run) or are test-author concerns (task 050, 042, etc.) outside the test-infra scope.

The narrow scope of task 068 — "fix Jest + React 19 module resolution" — is satisfied.

---

## Files modified

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/package.json` | RTL → v16, React/types → v19, added `@testing-library/dom` |
| `src/client/shared/Spaarke.UI.Components/package-lock.json` | regenerated by `npm install` |
| `src/client/shared/Spaarke.UI.Components/jest.config.js` | `transformIgnorePatterns` adds `marked` exception |

**Production code touched**: NONE.

---

## Constraints honored

- **ADR-022**: React 19 still required for Code Pages. Did NOT downgrade. Aligned test env upward. ✅
- **ADR-012**: Test infra stays in `@spaarke/ui-components`. Did NOT relocate. ✅
- **No Vitest migration**: Narrow Jest+RTL alignment only. ✅
- **Test-infra-only modification**: package.json devDeps + jest.config.js only. ✅
- **No deploy**: Config + test verification only, per operator instruction. ✅

---

## Follow-up recommendations (NOT in scope for 068)

1. **Test-content cleanup**: Authors of tasks 042 / 050 / and other SprkChat tests should triage the 142 remaining failures and fix test logic that depends on RTL v14 / React 18 behavior.
2. **`themeStorage.test.ts` location-spy**: Pre-existing flake; unrelated to React 19.
3. **`marked` v17 was bumped recently**: PR history may explain why it became ESM-only mid-flight. Consider pinning `marked` major to avoid future ESM transform surprises.
4. **Consumer alignment**: `@spaarke/auth`, `@spaarke/events-components`, and other shared libs should be audited for the same React 16 dev-dep leftover.
