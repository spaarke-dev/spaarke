# D-8 — "+ New Task" host-record auto-associate (UAT 2026-08-17)

> **Status**: Implemented (client-side, well-grounded) with explicit LIVE-VERIFICATION items.
> Not a pure escalation — the mechanism mirrors the already-proven subgrid "+ New" flow
> and MS-Learn-documented pre-seed conventions. But the OOB-form pre-fill behavior cannot
> be confirmed without the live `sprk_todo` form (same class of gap as task 030).

## Problem (operator UAT)

Clicking **"+ New Task"** on the Smart To Do surface while it is embedded in a record
context (e.g. a Matter form) opened the OOB To Do create modal with the **RELATED RECORD
(RegardingResolver) EMPTY** — the host Matter was not pre-associated.

## Investigation findings

### 1. How "+ New Task" can learn the host record

- The "+ New Task" button lives in the **SmartTodo Code Page** (`SmartTodoApp.tsx` Header →
  `handleNewTask` → `launchNewTaskCreateForm`). The Code Page (`sprk_smarttodo` web resource)
  runs in an iframe inside the Dataverse shell (`xrmProvider.getXrm()` frame-walks
  window → parent → top).
- **Before this fix, NOTHING read the hosting form's record.** `buildNewTaskDefaultValues`
  only read `useLaunchContext()` (URL params: VisualHost drill-through `openTodos.regardingFilter`
  / Outlook `createTodo.initialRegarding`). When the Code Page is embedded on a Matter form,
  no URL param carries the Matter → `defaultValues` was `undefined` → plain, empty-regarding create.
- **Most reliable CLIENT-ONLY signal** (needs no Dataverse/form config): the shell form context
  `Xrm.Page.data.entity.getEntityReference()` → `{entityType, id, name}` of the host record.
  Reachable via the same frame-walk `getXrm()` already uses. Works both when the Code Page is
  embedded as a web resource ON a record form (`window.parent.Xrm.Page`) and when it is opened
  as a dialog OVER one (`window.top.Xrm.Page`). Implemented as `resolveHostRegardingRecord()`.
  - The alternative "host passes record via URL param" is more deterministic but requires
    **form-config work** (a form script to inject the current record id into the web-resource
    URL) — out of the `src/` boundary and needs live Dataverse. The `Xrm.Page` read needs zero
    Dataverse changes, so it is the pragmatic most-reliable client-side mechanism.
  - The **widget** surface (`SmartTodoWidget` in LegalWorkspace/SpaarkeAi) is a DIFFERENT
    component whose add path is `onAddTodo` → `CreateTodoWizard`; it does not use this button
    and is out of scope for D-8.

### 2. Reliable pre-populate of the regarding lookup (MS-Learn research, 2026-08-17)

The RegardingResolver PCF on the To Do form auto-completes the 5 resolver fields on load IF
the entity-specific lookup (e.g. `sprk_regardingmatter`) is pre-populated
(`detectPrePopulatedParent` reads `Xrm.Page.getAttribute('sprk_regardingmatter').getValue()`).
So the fix must make the OOB create form open with that lookup set.

Two delivery mechanisms were evaluated against Microsoft Learn:

| Mechanism | Verdict |
|---|---|
| **`data` three-key convention** — `{lookup}`=GUID, `{lookup}name`=name, `{lookup}type`=logical name (["Set column values using parameters passed to a form"](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/set-field-values-using-parameters-passed-form)) | **PRIMARY.** Direct + deterministic — names the exact field. `sprk_regardingmatter` is a **single-target** lookup → documented-supported. All 12 catalog lookups are on the To Do main form (hidden cells, task 013) so `getAttribute` resolves them. |
| **`createFromEntity`** — relationship-attribute-mapping "create from parent" seam (same as subgrid "+ New"; sibling of `data` on the pageInput) | **SECONDARY (belt-and-suspenders).** INDIRECT — copies whatever relationship attribute-**mappings** exist; cannot name a field; **silently pre-fills nothing when no mapping is configured**. Kept because it mirrors the proven subgrid flow (task 014 verified subgrid auto-detect works) and costs nothing. |
| **`JSON.stringify([{id,entityType,name}])` under one key** (the pre-D-8 task-030 shape) | **REMOVED — unsupported.** No MS doc describes it; "invalid parameter → error". This is the suspected reason the task-030 pre-seed never populated. |

Sources: `navigateTo` / `openForm` client-API refs + "Map table columns" (createFromEntity
relies on mappings + "created in any way other than from the associated view… data is not mapped").

## What changed (files)

1. **`src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts`**
   - Added optional `createFromEntity?: {entityType, id, name?}` to `EntityRecordSurfaceParams`
     and set it on `pageInput.createFromEntity` on the **CREATE branch only** (braces stripped).
   - Refined the `data` doc comment (flat attribute→value dictionary; three-key lookup rule).
   - No new `navigateTo` call site (CLAUDE.md §11) — this is an additive param on the ONE launcher.

2. **`src/solutions/SmartTodo/src/services/newTaskLauncher.ts`**
   - Added `resolveHostRegardingRecord()` (reads shell `Xrm.Page.data.entity.getEntityReference()`
     via `getXrm`, defensive, returns `{entityType, recordId, recordName?}`).
   - Added central `resolvePreferredRegarding()`: **host record preferred**, then launch context;
     each admitted only when its entity type is one of the 12 `TODO_REGARDING_CATALOG` targets.
   - Rewrote `buildNewTaskDefaultValues` to emit the **three-key `data` convention** (+ the two
     plain-text resolver fields `sprk_regardingrecordid`/`name`) — replacing the removed
     JSON.stringify shape. `sprk_regardingrecordtype` still deliberately NOT pre-seeded
     (RegardingResolver resolves it — keyed by entity NAME, not the record GUID).
   - `launchNewTaskCreateForm` now passes BOTH `defaultValues` AND `createFromEntity`.

3. **Tests** (`SmartTodoApp.test.tsx` +7 → 19; `wizardLaunchers.test.ts` +3 → 14): updated the
   3 JSON.stringify assertions to the three-key shape; added host-record read, host-preferred
   precedence, unsupported-host fallback, and `createFromEntity`/`data` passthrough coverage.

Reuse / negatives (CLAUDE.md §6.5 / §11 / project MUST-NOTs): ADR-050 Path A intact (still the
OOB `navigateTo` main form, no `SprkModal`); ADR-024 — RegardingResolver is the consumer,
**no AssociationResolver introduced/resurrected**; no BFF touches; no second navigateTo call site.

## ⚠ What the operator MUST live-verify (no live Dataverse in this session)

1. **Three-key `data` pre-fill** — that opening the deployed To Do main form with
   `data: { sprk_regardingmatter:<id>, sprk_regardingmattername:<name>, sprk_regardingmattertype:'sprk_matter' }`
   actually populates the (hidden) `sprk_regardingmatter` lookup so RegardingResolver's
   `detectPrePopulatedParent` fires. Documented-supported for a single-target lookup, but
   unverified against the live form. If it does NOT populate, the `createFromEntity` secondary
   (mirroring subgrid "+ New", which is verified working) is the fallback path.
2. **Host-record read** — that `Xrm.Page.data.entity.getEntityReference()` returns the true host
   Matter in the ACTUAL embed configuration (web-resource-on-form vs dialog-over-form), and that
   a standalone full-page load does NOT surface a stale prior form (mitigated by the 12-target
   catalog gate, but `Xrm.Page` is shell-global — verify no false-positive association).
3. **`createFromEntity` mappings** — whether the `sprk_matter → sprk_todo` (and siblings)
   relationships have attribute mappings that populate `sprk_regardingmatter`. If yes, it is a
   redundant safety net; if no, the three-key `data` path is the one that must carry the load.

The **plain-create fallback is unconditional** (no host + no launch context → no `defaultValues`,
no `createFromEntity` → today's behavior), so any mis-fire degrades gracefully, never errors.

## Verification (2026-08-17)

- **tsc (git-stash new-vs-preexisting)**: Spaarke.UI.Components 3→3 (0 new); SmartTodo 38→38 (0 new).
- **jest**: SmartTodo 9 suites / 121 pass (SmartTodoApp 19/19). Spaarke.UI.Components — `wizardLaunchers`
  14/14 pass; full suite net **improved** vs baseline (36→35 failing suites, 18→17 failing tests,
  +4 passing) — the failing suites are pre-existing environmental failures (unbuilt `dist/`, per
  shared CLAUDE.md), none introduced by this change.
- **hex/rgb/'1px'** on all 4 changed files: zero matches.
