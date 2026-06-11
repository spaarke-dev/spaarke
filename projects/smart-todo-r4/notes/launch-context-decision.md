# useLaunchContext decision

> Task: R4-004 · Date: 2026-06-10 · Status: complete

## TL;DR

**The premise of task 004 is incorrect.** The R4 spec.md (line 168) and the task POML claim `src/solutions/SmartTodo/src/hooks/useLaunchContext.ts` does NOT exist. **It does exist** — shipped by R3 task 070b, fully implemented (235 lines), unit-tested (~210-line test file), and already consumed by `SmartTodoApp.tsx`. The project's initial discovery missed it (likely a stale enumeration; the existing file list in `CLAUDE.md` line 172 omitted it).

**Decision**: **REPURPOSE + EXTEND** the existing hook with a second action discriminator (`'openTodos'`) for FR-34 Visual Host drill-through. Do NOT create a new hook; do NOT rename the existing one (its API is already wired into the Outlook launch flow and tests bind to it).

## 1. Existing hooks inventory

Directory: `src/solutions/SmartTodo/src/hooks/`

| Hook | Path | Purpose | URL-param awareness? |
|---|---|---|---|
| `useFeedTodoSync` | `useFeedTodoSync.ts` | No-op stub of `FeedTodoSyncContext` for standalone SmartTodo (cross-block lifecycle bus stub) | No |
| `useKanbanColumns` | `useKanbanColumns.ts` | Assigns todos to Today/Tomorrow/Future columns; provides `moveItem`/`togglePin`/`recalculate` with optimistic UI + Dataverse writes | No |
| `useLaunchContext` | `useLaunchContext.ts` | **Parses `window.location.search` for `action=createTodo` + regarding triple; clears params via `history.replaceState`; returns `ILaunchContext | undefined`** | **YES (this is the hook)** |
| `useTodoItems` | `useTodoItems.ts` | Fetches `sprk_todo` records (statecode=0, statuscode in {Open, In Progress}); sorts by To Do Score DESC then due-date ASC | No |
| `useTodoScoring` | `useTodoScoring.ts` | Manages BFF scoring dialog state per todo; calls `POST /api/workspace/events/{id}/scores`; deterministic mock fallback (NFR-06); uses `useAuth()` from `@spaarke/auth` | No |
| `useUserPreferences` | `useUserPreferences.ts` | Reads/writes `sprk_userpreference` JSON; defaults `TodoKanbanThresholds = { today: 60, tomorrow: 30 }` | No |

Plus `__tests__/useLaunchContext.test.ts` — pure-parser tests + jsdom integration stubs (~210 lines).

## 2. Existing URL-param consumption (across SmartTodo)

Grep for `URLSearchParams|window.location.search|new URL\(|searchParams` in `src/solutions/SmartTodo/src/`:

- **`useLaunchContext.ts` lines 119–149, 223** — only consumer in SmartTodo.
  - `parseLaunchContextFromSearch(search: string)` — pure parser (exported for tests).
  - `useLaunchContext()` — hook reads `window.location.search` once on mount via `React.useMemo([])`, returns `ILaunchContext | undefined`, clears the four launch params via `history.replaceState` in `useEffect([])`.
- **`SmartTodoApp.tsx` lines 38, 169, 170, 242** — `LaunchCreateTodoWizardHost` consumes the hook; opens `<CreateTodoWizard>` with `initialRegarding={launchContext?.initialRegarding}` when `launchContext?.action === 'createTodo'`.

No other URL-param parsing exists in SmartTodo. The R3 070b hook is the single canonical URL parser.

## 3. Sibling Code Page precedent

Grep across `src/solutions/*/src/` for the same patterns:

| Surface | Pattern | Notes |
|---|---|---|
| `SpaarkeAi/src/utils/launch-resolver.ts` | Params-only: `entityLogicalName`, `entityId`, `matterId` — **no action discriminator** | Used by ribbon scripts to build URLs; doesn't tie URL parsing to an action verb. |
| `EventDetailSidePane/src/utils/parseParams.ts` | `parseSidePaneParams()` returns `{ eventId, eventType }`; handles two URL shapes (direct + Dataverse `?data={base64}` envelope) | Pure utility, not a React hook. |
| `CalendarSidePane/src/utils/parseParams.ts` | Same Dataverse-envelope pattern as EventDetailSidePane | Pure utility. |
| `DocumentUploadWizard/src/services/nextStepLauncher.ts` | URL builder side (writes params for downstream consumers) | Builder, not parser. |

**No peer Code Page has a `useLaunchContext`-style React hook**. SmartTodo's `useLaunchContext` is itself the precedent for the codebase. Peers either use plain `parseParams()` utilities or `launch-resolver.ts`-style helpers. SmartTodo's hook is a more idiomatic React-19 pattern (memoized read + `useEffect` side-effect) and should remain the model.

## 4. R3 task 070b actual deliverable

POML: `projects/smart-todo-decoupling-r3/tasks/070b-smarttodo-launch-context-parser.poml`.

Goal verbatim: "Outlook ribbon 'Create To Do' launches the SmartTodo Code Page, which auto-opens the CreateTodo wizard with regarding pre-filled to the email's communication."

Delivered:
- `src/solutions/SmartTodo/src/hooks/useLaunchContext.ts` — 235 lines, named exports: `LAUNCH_ACTION_CREATE_TODO`, `LAUNCH_PARAM_KEYS`, `ILaunchRegarding`, `ILaunchContext`, `parseLaunchContextFromSearch`, `useLaunchContext`.
- `src/solutions/SmartTodo/src/hooks/__tests__/useLaunchContext.test.ts` — 217 lines.
- Wired into `SmartTodoApp.tsx` `LaunchCreateTodoWizardHost` component.

Current contract (the file's own header lines 24–40 + line 88–97 ILaunchContext shape):

```ts
const LAUNCH_PARAM_KEYS = {
  ACTION: 'action',
  REGARDING_TYPE: 'regardingType',
  REGARDING_ID: 'regardingId',
  REGARDING_NAME: 'regardingName',
} as const;

interface ILaunchContext {
  action: 'createTodo';
  initialRegarding: ILaunchRegarding | undefined;  // { entityType, recordId, recordName }
}
```

**Behavior**: returns `undefined` when `action !== 'createTodo'`. This is the part R4 must extend, because FR-34 needs a second recognized action (Visual Host drill-through with `regardingType` + `regardingId` only — possibly no `regardingName`).

## 5. Decision

**Choice**: **REPURPOSE + EXTEND the existing `useLaunchContext` hook.**

**Specifically**:
1. Keep the hook name, file path, exports, and the entire `action='createTodo'` code path UNCHANGED.
2. Add a second recognized action value `'openTodos'` (or similar — see §6) for Visual Host drill-through (FR-34).
3. The `ILaunchContext.action` discriminated-union type widens to `'createTodo' | 'openTodos'`.
4. For `action='openTodos'`, only `regardingType` + `regardingId` are required; `regardingName` is optional (Visual Host may not have a display name available — it operates from chart def fetch, not from a record summary).
5. `parseLaunchContextFromSearch` parser logic adds a second branch with its own validation; both branches share the same param-clearing on first mount.
6. Add the new branch to the existing test file in the same `describe` block (test file is set up to absorb extension; runner not yet wired but tests-as-spec pattern documented in the file header).

**Rationale**:

- **The hook already exists, works, has tests, and is named exactly as the R4 spec expects.** Creating a new hook would duplicate URL parsing, fragment the launch contract, and require deleting the existing one (which is wired into the working Outlook flow + has tests). Net: zero value.
- **The spec ITSELF calls for reuse**: FR-34 verbatim says "reuses R3 task 070b URL-param parser". Author intent was extension, not replacement.
- **Extension is low-risk**: the existing hook's design is already an extensible discriminated-union (`action` is a string literal type that can widen; the test file already has a regression case for unknown actions at line 92–95).
- **Sibling precedent supports it**: no other Code Page uses a hook for this — SmartTodo's hook is the idiomatic pattern; concentrating launch-context parsing in one place is the right move.
- **Avoids a project-level naming/refactor cascade**: tasks 020/030/060/081–084 all reference `useLaunchContext` by name. Renaming would force POML rewrites across the project.

## 6. Recommended interface (binding for downstream tasks)

```ts
// File: src/solutions/SmartTodo/src/hooks/useLaunchContext.ts
// Change set: add 'openTodos' branch; widen action union; keep all existing exports.

/** Action: pre-existing Outlook ribbon flow (R3 070b — unchanged). */
export const LAUNCH_ACTION_CREATE_TODO = 'createTodo';

/** Action: NEW for R4 FR-34 — Visual Host card drill-through. */
export const LAUNCH_ACTION_OPEN_TODOS = 'openTodos';

export const LAUNCH_PARAM_KEYS = {
  ACTION: 'action',
  REGARDING_TYPE: 'regardingType',
  REGARDING_ID: 'regardingId',
  REGARDING_NAME: 'regardingName',  // optional for openTodos; required for createTodo pre-fill
} as const;

export interface ILaunchRegarding {
  entityType: string;   // Dataverse logical name (e.g. 'sprk_matter')
  recordId: string;     // GUID, lowercased, no braces
  recordName: string;   // Display name; populated when available
}

// Pre-existing shape (UNCHANGED)
export interface ICreateTodoLaunchContext {
  action: typeof LAUNCH_ACTION_CREATE_TODO;
  initialRegarding: ILaunchRegarding | undefined;
}

// NEW shape for FR-34
export interface IOpenTodosLaunchContext {
  action: typeof LAUNCH_ACTION_OPEN_TODOS;
  /** Regarding filter — Visual Host drill-through prefilters Kanban to this parent record. */
  regardingFilter: {
    entityType: string;   // e.g. 'sprk_matter' — REQUIRED
    recordId: string;     // GUID — REQUIRED
    recordName?: string;  // OPTIONAL — Visual Host may not have this
  };
}

export type ILaunchContext = ICreateTodoLaunchContext | IOpenTodosLaunchContext;

export function useLaunchContext(): ILaunchContext | undefined;
```

### Field justifications

| Field | Required? | Why |
|---|---|---|
| `action` | always | Discriminator — drives which subtype the consumer receives. Existing test (line 92–95) requires unknown values to return `undefined`. |
| `regardingFilter.entityType` | required for `openTodos` | Spec FR-34 query string verbatim: `?regardingType=<entity>&regardingId=<id>`. Without it, Kanban cannot filter to the parent. |
| `regardingFilter.recordId` | required for `openTodos` | Same — spec FR-34. |
| `regardingFilter.recordName` | optional for `openTodos` | Visual Host chart-def context doesn't expose record display name. Kanban can render a header like "To Dos for [entityType] [recordId]" when name is absent — non-blocking. |
| `initialRegarding.*` | required tri-field for `createTodo` | Wizard pre-fill needs all three per existing 070b contract (line 75–82). |

### `mode` field — explicitly EXCLUDED from interface

The task POML's suggested signature `{ regardingType?, regardingId?, mode?: "modal" | "fullpage" }` is rejected. Reasoning:
- The mode (modal vs fullpage) is decided by the CALLER (the ribbon/drill-through navigation handler invoking `Xrm.Navigation.navigateTo({...}, { target: 2 })` per FR-34). It is encoded in the navigation options, NOT in the URL the launched page reads.
- The launched page renders the same UX (Kanban filtered to the regarding) regardless of whether it was opened modal or fullpage — there's no behavior to branch on.
- Adding a `mode` URL param would be an unused signal, encouraging consumers to write dead branches. Reject per YAGNI + per spec NFR-03 (no environment-specific values).

If a consumer ever needs to know "am I in a modal?", they should infer from `window` (e.g., parent presence) rather than URL. Not in scope for R4.

## 7. Downstream task impact

All downstream task POMLs already reference `useLaunchContext` by name (verified via grep). The decision = **no rename** = no downstream POML edits required for naming. Tasks must, however, update their understanding:

| Task | Reference type | Action |
|---|---|---|
| **Task 020** (A widget rebuild) | Spec.md line 168 cites the hook as URL-param parser for drill-through | **No code action**: A widget is workspace-side; doesn't consume `useLaunchContext` directly. May dispatch PaneEventBus events that ultimately trigger a Visual Host drill — that's task 081-084's concern. |
| **Task 030** (B Code Page overhaul) | Spec.md line 168 | **Update**: Task 030 (B layout) will mount SmartTodoApp's Kanban. The new `openTodos` branch in `useLaunchContext` produces a `regardingFilter` that the Kanban must respect (filter `useTodoItems` query by the regarding). Add an acceptance criterion: "When `useLaunchContext()` returns `{ action: 'openTodos', regardingFilter }`, the Kanban auto-filters to that parent record." |
| **Task 060** (E card affordances) | Mentioned in spec.md line 168 | **Likely no impact**: E is card-affordance UX (Open icon, double-click, checkbox). Doesn't directly consume launch context. |
| **Tasks 081–084** (G form Visual Host additions) | Spec.md FR-34 line 115 | **Primary consumer**: tasks 081-084 add the `sprk_drillthroughtarget` chart-def URL — they build the URL with `?regardingType=<entity>&regardingId=<id>&action=openTodos` (note the `&action=openTodos` addition — DO NOT omit; the parser branches on it). Tasks must verify the URL is built with the `action` discriminator, NOT just regarding params. |

### Required POML annotations

Add a one-line cross-reference to this decision doc in tasks **030** and **081–084** (the actual consumers). Tasks 020 and 060 reference the hook only via the spec.md `Discovered Resources` line; no POML edit needed.

Suggested annotation (added under `<knowledge>` or `<context>` section):

```xml
<file purpose="binding-decision">projects/smart-todo-r4/notes/launch-context-decision.md</file>
```

(POML edits are NOT performed by this task — the task POML steps 5–6 say "decision doc" + "update downstream task POML if affected". Since the chosen path adds a NEW discriminator without renaming anything, the annotation is sufficient. Whoever picks up tasks 030/081–084 will add it during their own task-execute.)

## 8. Acceptance criteria checklist (from task 004 POML)

- [x] **`notes/launch-context-decision.md` lists every hook currently in `src/solutions/SmartTodo/src/hooks/` with its purpose** — §1 table (6 hooks).
- [x] **Decision (new vs repurpose) has stated rationale** — §5 (chose REPURPOSE + EXTEND; 5-bullet rationale).
- [x] **Recommended interface signature is documented** — §6 with full TypeScript signature, field justifications, and explicit `mode`-field exclusion.
- [x] **Downstream tasks (081-084 minimum) are clear which hook to call by name** — §7 table; hook name remains `useLaunchContext` with widened `ILaunchContext` union.

## 9. Open items for next task in this area

These are NOT in scope for R4-004 (decision-only task) but flagged for whichever task implements the hook extension:

1. **Test runner**: the existing `useLaunchContext.test.ts` notes (lines 8–27) that SmartTodo's `package.json` has no test runner. The R4 implementation task adding the `openTodos` branch should consider wiring vitest at the same time (the test file is already shape-compatible).
2. **Spec.md correction**: line 172 of `projects/smart-todo-r4/CLAUDE.md` lists existing hooks as `useFeedTodoSync, useKanbanColumns, useTodoItems, useTodoScoring, useUserPreferences` — missing `useLaunchContext`. Should be corrected to reflect the actual 6 hooks. Suggest a small follow-up doc fix.
3. **`SmartTodoApp.tsx` consumer extension**: when the parser learns `openTodos`, the App must add a sibling to `LaunchCreateTodoWizardHost` (e.g., a `LaunchOpenTodosHost` that doesn't open the wizard, just sets a `regardingFilter` prop on the Kanban). Worth a small refactor task or fold into task 030.

---

**Decision finalised**: 2026-06-10 · `useLaunchContext` REPURPOSED + EXTENDED with `openTodos` action discriminator. Hook name/path/exports preserved. No new hook. No rename.
