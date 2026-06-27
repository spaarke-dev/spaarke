# Task 022 — CreateProjectWizard G2 latent gap verification

> **Date**: 2026-06-07
> **Task**: 022 (CreateProjectWizard FR-WIZ-02 + G2 fix)
> **Status**: G2 GAP **CONFIRMED** — proceeded with full fix

## Empirical verification (Step 1)

**File inspected**: `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/projectService.ts` (pre-Task-022 state).

**`ProjectService.createProject` build of the create payload** (lines 280–294 of original file):

```typescript
const entity: Record<string, unknown> = {
  sprk_projectname: formValues.projectName.trim(),
};

if (formValues.description && formValues.description.trim() !== '') {
  entity['sprk_projectdescription'] = formValues.description.trim();
}

if (formValues.isSecure === true) {
  entity['sprk_issecure'] = true;
}
```

Followed only by lookup `@odata.bind` entries (project type, practice area, attorney,
paralegal, outside counsel).

**Searches across the wizard for the two cascade fields**:

```
Grep "sprk_containerid|sprk_searchindexname"
  in src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard
  → No matches found
```

**Conclusion**: **G2 confirmed** — `CreateProjectWizard` was setting **neither** `sprk_containerid`
nor `sprk_searchindexname` on `sprk_project` before this task. This matches spec.md
§A1 assumption and FR-WIZ-02's "fixes latent gap G2" framing.

## Comparison to canonical Matter pattern

`matterService.ts:215–217` (canonical):
```typescript
if (this._containerId) {
  entity['sprk_containerid'] = this._containerId;
}
```

`MatterService` receives `_containerId` via constructor (resolved from BU upstream by
`CreateProjectWizard`-equivalent at `src/solutions/CreateMatterWizard/src/main.tsx`).
Matter ALSO does not yet set `sprk_searchindexname` — that is sibling task **021**.

## Code-page consumer

`src/solutions/CreateProjectWizard/src/main.tsx` already resolves the SPE container ID
via `resolveSpeContainerId` (lines 55–65) and passes it down. **However**: the resolved
value is passed only to the file-upload step (via `context.speContainerId`), NOT to
the `projectService.createProject` payload-build. This is the wiring gap that allowed
G2 to persist.

## Fix approach (per Task 022 spec)

1. **`projectService.ts`** — extend `createProject(formValues)` to accept an optional
   `cascadeDefaults` second arg of type `IUserBuCascadeDefaults` (re-exported from
   `EntityCreationService.ts`). Inside `createProject`, call
   `EntityCreationService.applyUserBuDefaults(entity, cascadeDefaults)` after building
   the scalar fields and before submitting. INV-5 is preserved by the helper.
2. **`CreateProjectWizard.tsx`** — before calling `projectService.createProject(...)`,
   resolve the current user's BU defaults via
   `EntityCreationService.resolveUserBuDefaults(webApiAdapter, userId)`. Pass the
   result through to `createProject`.
3. **`userId` source** — read from `(window).Xrm.Utility.getGlobalContext().userSettings.userId`,
   mirroring the existing `resolveSpeContainerId` callback in the code-page main.tsx.
   We funnel this into the wizard via a new optional callback prop (kept optional so
   tests can omit it) — see implementation. **Pattern alignment**: matches the
   existing `resolveSpeContainerId` callback shape.

## Why this matches Task 020's intent

Task 020 deliberately shipped `applyUserBuDefaults` + `resolveUserBuDefaults` as
**static helpers** on `EntityCreationService` precisely so each wizard service can
call them WITHOUT inheriting EntityCreationService construction (which requires
`authenticatedFetch` + `bffBaseUrl`). The helpers are pure / side-effect-free / INV-5-safe.

`ProjectService` already takes `IDataService` — which is `IWebApiLike`-compatible at
the call signatures `resolveUserBuDefaults` uses (`retrieveRecord(entityType, id, options)`).
We pass `dataService` directly to `resolveUserBuDefaults` (no adapter needed).

## Files modified by Task 022

1. `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/projectService.ts`
2. `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/CreateProjectWizard.tsx`
3. `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/__tests__/projectService.test.ts` (NEW)

## INV-5 evidence

- `applyUserBuDefaults` internally calls `applyDefaultContainerId` + `applyDefaultSearchIndexName`,
  each of which checks `_hasExplicitValue(entity, fieldLogicalName)` before mutating
  (lines 149–155 of `EntityCreationService.ts`).
- Unit tests (added in `projectService.test.ts`) verify both fields cascade when empty
  AND are preserved when explicitly pre-set.

## Acceptance pre-check

- `sprk_containerid` populated on Project create payload when BU has it → ✅ closed
- `sprk_searchindexname` populated on Project create payload when BU has it → ✅ closed
- INV-5 preserved for both fields → ✅ closed (per applyUserBuDefaults contract + tests)
- `npm run build` → run at end of task and reported in main task notes
