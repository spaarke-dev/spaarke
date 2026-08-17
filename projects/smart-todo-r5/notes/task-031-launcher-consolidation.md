# Task 031 — Open shares the New Task launch mechanism (FR-11)

> Spec ref: FR-11 (U-5, owner-confirmed 2026-08-14). Gate: task 030 complete.

## What changed

Three independent `Xrm.Navigation.navigateTo({pageType:'entityrecord', entityName:'sprk_todo', ...})`
call sites are now ONE function: `navigateToEntityRecordSurfaceAsync` in
`src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts`.

| Call site | Before | After |
|---|---|---|
| `SmartTodoApp.tsx` — `openSprkTodoAsLayout1` (Code Page, open existing) | Inline `navigateTo`, inline `(window.parent as any)?.Xrm ?? (window as any).Xrm` resolver, fire-and-forget, no outcome signal | Thin `void`-returning wrapper delegating to the shared launcher with `entityId` |
| `todo.registration.ts` — `FeedSyncBridgeHost.handleOpenTodo` (LegalWorkspace widget, open existing) | Inline `navigateTo` re-declaring the same 85%×85%/`position:1` shape, single-frame `globalThis.Xrm` check, `openForm` defensive fallback | Delegates to the shared launcher with `entityId`; the `openForm` fallback is preserved, now triggered by `outcome.launched === false` instead of a pre-check |
| `newTaskLauncher.ts` — `launchNewTaskCreateForm` (Code Page, create) | Task 030's new call, already delegating | Unchanged — automatically inherits any future shared-launcher improvements |

## Function signature decision

Kept `navigateToEntityRecordSurfaceAsync`'s exported NAME unchanged (per the task's
"minimize call-site churn" guidance). Extended `EntityRecordSurfaceParams`:

```ts
export interface EntityRecordSurfaceParams {
  entityName: string;
  entityId?: string;   // NEW — present = open existing, absent = create
  title?: string;       // CHANGED from required → optional (see below)
  defaultValues?: Record<string, unknown>;
}
```

**`title` required → optional.** Both pre-031 OPEN call sites (`SmartTodoApp.tsx`,
`todo.registration.ts`) never set a `title` navOption. The pre-031 `EntityRecordSurfaceParams.title`
was `string` (required) because the only caller was task 030's CREATE path, which always
supplies `'New To Do'`. Making it optional — and only setting `navOptions.title` when
`params.title !== undefined` — was the only way to consolidate onto one function without
introducing a title bar the OPEN dialogs never had before (would have been an
undocumented chrome regression, which the task's sizing-preservation constraint implicitly
extends to). CREATE is unaffected (`newTaskLauncher.ts` always passes a title).

Branch behavior inside `navigateToEntityRecordSurfaceAsync` (`isOpenExisting = entityId present`):
- OPEN: `pageInput.entityId` set (braces stripped via `.replace(/[{}]/g, '')`, matching the
  pre-031 `SmartTodoApp.tsx` convention); navOptions use `OOB_MODAL_SIZES.record` (85%×85%) +
  `position: 1`.
- CREATE: unchanged — `OOB_MODAL_SIZES.createForm` (70%×80%), no `position` key.

## SmartTodoApp.tsx caller decision

Kept `openSprkTodoAsLayout1(todoId: string): void` as a **thin wrapper** (did not thread
`Promise<NavigateToOutcome>` through its two callers — the `OPEN_TODOS_EVENT` listener and
the `useLaunchContext` openTodo effect). Both callers are fire-and-forget event handlers with
zero interest in the async outcome, so a thin wrapper is strictly smaller diff than updating
both call sites to an async shape for no behavioral gain. The wrapper preserves the one piece
of pre-031 behavior worth keeping — the "Xrm unavailable" `console.warn` diagnostic — by
inspecting `outcome.launched` in a `.then()`.

## todo.registration.ts fallback preservation

The task's constraint text says "keep the `onOpenWizard`/`openForm` defensive fallback... intact."
Reading the actual code (not just the task's narrative, which conflates the two): there are
TWO distinct fallbacks in `handleOpenTodo`:
1. The **`openForm` fallback**, inside the `todoId`-present branch — used when `navigateTo`
   itself isn't reachable.
2. The **`ctx.onOpenWizard` "no selection" branch** — used when `todoId` is absent entirely.

Per CLAUDE.md §2 ("Code wins. Docs lag."), both are preserved, but the OPENFORM fallback had to
change its TRIGGER CONDITION — it can no longer pre-check `xrm?.Navigation?.navigateTo` inline,
because that check now lives inside the shared launcher's `resolveXrmNavigation()`. It now fires
on `outcome.launched === false` (the shared launcher's frame-walk found no host) instead. The
"no selection" `ctx.onOpenWizard` branch is untouched — zero lines changed.

This does NOT violate the "one code path" acceptance criterion: the `openForm` fallback is a
DIFFERENT Xrm API (`Xrm.Navigation.openForm`, not `navigateTo({pageType:'entityrecord'})`) and
does not construct the pageType-`entityrecord` object the acceptance criterion counts.

## Step 2 research — outcome-shape parity (create vs open)

**Source**: Microsoft Learn, `Xrm.Navigation.navigateTo` reference,
`https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto`
(fetched 2026-08-16; `ms.date: 2026-04-09`).

Verbatim "Return value" section:

> Returns a promise. The value passed when the promise resolves depends on the target:
> - *inline*: Promise resolves right away, and doesn't return any value.
> - *dialog*: Promise resolves when the dialog closes. **An object is passed only if the
>   `pageType` = `entityRecord` and you opened the form in create mode.** The object has a
>   `savedEntityReference` array with the following properties to identify the table record
>   created: entityType, id, name.

**Finding: the shapes are NOT the same.** An existing-record OPEN (`entityId` supplied) never
resolves with a `savedEntityReference` — that field is populated **only** for CREATE-mode
`entityrecord` navigations. There is no `Xrm.Navigation.navigateTo`-native signal to distinguish
"user saved changes to the existing record" from "user cancelled/closed without saving" on the
OPEN path; both resolve the promise with no result object.

**How this was handled** (in `navigateToEntityRecordSurfaceAsync`): the two branches populate
`NavigateToOutcome` differently, but both stay within the SAME type:
- CREATE (unchanged from pre-031): `savedEntityReference` present on save;
  `{launched: true, cancelled: true}` when the resolve carries no reference.
- OPEN (new): a clean resolve (dialog closed, no error) returns a **plain**
  `{launched: true}` — no `cancelled` flag, no `savedEntityReference`. A rejected/erroring
  promise (dialog error, not a normal close) still returns `{launched: true, cancelled: true}`,
  matching the CREATE branch's error handling and the pre-031 code's catch-and-swallow
  precedent.
- No host reachable (either branch): `{launched: false}`, unchanged.

**Consequence flagged for task 033** (not this task's scope — cited per the POML's stated
purpose "so task 033 only has to be written once"): task 033's refresh-on-close wiring for the
OPEN path CANNOT gate on `outcome.cancelled` the way CREATE can gate on `savedEntityReference`
presence — there is no reliable "did they actually save" signal from `navigateTo` alone for an
existing-record dialog. Task 033 will need to refresh unconditionally on `outcome.launched`
for the OPEN path (a superset refresh — occasionally refetches when nothing changed, but never
misses a real edit), OR find a different signal (e.g., poll-on-focus, a Dataverse
`retrievemultiple` ETag check) if an unconditional refresh proves too chatty. This is
documented here rather than worked around silently, per the task's step-2 instruction.

## Verification

- `npx tsc --noEmit` in Spaarke.UI.Components / SmartTodo / LegalWorkspace: **zero new errors**
  in all three packages (compared via `git stash` before/after — identical pre-existing error
  sets, all in unrelated files: `@spaarke/auth` / `@spaarke/sdap-client` / `@azure/msal-browser`
  module-resolution gaps and pre-existing `ComponentFramework`/`ITodo` typing gaps).
- `npx jest` — Spaarke.UI.Components: 9/9 new tests pass
  (`wizardLaunchers.test.ts`); SmartTodo + LegalWorkspace full suites: see task report for
  final counts (no regressions expected — neither existing suite imports `SmartTodoApp.tsx` or
  `todo.registration.ts` directly with `window.Xrm` assertions that would be sensitive to this
  refactor; `SmartTodoApp.test.tsx` tests `newTaskLauncher.ts` in isolation via a
  `@spaarke/ui-components` mock, unaffected).
- Grep (`entityrecord` / `pageType.*entityrecord`) across `src/solutions/SmartTodo` and
  `todo.registration.ts`: zero direct constructions remain outside the shared launcher (one
  comment reference only).
- Hex/rgb/`'1px'` literal grep on all four changed files: zero matches (ADR-021).

## ADR notes

- **ADR-050 Path A** (project-scoped exception, cited per CLAUDE.md §6.5): unchanged — this task
  refactors WHICH function issues the OOB `navigateTo` call; the modal family stays OOB
  `Xrm.Navigation.navigateTo`, not `SprkModal`.
- **ADR-012** (shared component library): the consolidation itself is the ADR-012 win — one
  function in `@spaarke/ui-components` instead of three drifted call sites across two host
  surfaces (Code Page + LegalWorkspace widget).
- **`.claude/patterns/ui/record-modal-selection.md`** Layout 1 invariant (85%×85% for every
  entity) is preserved — the `record` OOB size is untouched, only routed through one function.
