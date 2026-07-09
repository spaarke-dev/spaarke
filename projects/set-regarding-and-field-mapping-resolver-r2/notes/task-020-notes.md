# Task 020 — Wire engine into event, matter, project services — Notes

**Completed**: 2026-07-09 · Rigor: FULL · Model: sonnet@high · Verdict: COMPLETED · Wave B (parallel with 021, 022)

## Scope note (deviation from the literal 3-file `<outputs>` list — documented)

The POML's `<outputs>` lists only `eventService.ts`/`matterService.ts`/`projectService.ts`, and
TASK-INDEX.md's Wave-B goal-condition text says "git status shows only the 6 wizard-service
files changed" (the aspirational `/goal` condition for all of 020+021+022 combined). Investigation
showed this is **not achievable while also satisfying task 020's own acceptance criteria**:

- `EventService`/`ProjectService` constructors took **only** `dataService` — no
  `authenticatedFetch`/`bffBaseUrl` — so the engine (which requires both) could never fire from
  these two services without a source change reaching further than the service file.
- `MatterService`/`ProjectService.createProject` had **no parent/association parameter at all**
  flowing into their create methods — Matter and Project only link to a parent (Project/Account or
  Matter/Account) via a **post-create N:N** association done in the wizard `.tsx`, not a pre-create
  regarding parameter the service could read.
- `ICreateEventResult`/`ICreateProjectResult` had **no `warnings` field** to surface engine
  diagnostics on (constraint: "Append the engine's returned warnings to the service's existing
  warnings array").

Wiring the engine so it actually activates (not just compiles as dead code) required touching the
6 service methods listed below **plus** the 3 wizard `.tsx` files that construct these services —
all strictly within the event/matter/project domain (no file owned by parallel tasks 021/022 was
touched). This is flagged here per the spirit of CLAUDE.md §6.5 (surface the tension rather than
silently pick a path) even though it isn't a strict ADR conflict — it's a task-scope/reality gap
discovered during implementation. Verified: `git status --porcelain` shows exactly 6 modified
files (all in the 3 CreateXWizard folders) + 3 new test files, and zero files under
`CreateInvoiceWizard/`, `CreateReportCardWizard/`, `CreateTodoWizard/`, `CreateWorkAssignmentWizard/`
were touched (021/022's exclusive domain).

## What was done

### `eventService.ts`
- Constructor gained 2 new **optional** params: `authenticatedFetch?: AuthenticatedFetchFn`,
  `bffBaseUrl?: string` (after `dataService`). Existing `new EventService(dataService)` call sites
  (lookup-only construction in `CreateEventStep.tsx`, and 3 test files) are unaffected.
- `ICreateEventResult` gained a required `warnings: string[]` field (empty array on both
  success/error paths unless the engine reports something).
- In `createEvent`, immediately after the `applyResolverFields` call inside the
  `if (formValues.regardingRecordId && regardingEntityName)` block and BEFORE `createRecord`:
  calls `applyFieldMappings({ sourceEntity: regardingEntityName, sourceId: formValues.regardingRecordId, targetEntity: 'sprk_event', payload: entity, dataService: this._dataService, authenticatedFetch: this._authenticatedFetch, bffBaseUrl: this._bffBaseUrl })`
  when both deps are present; appends `mappingResult.warnings` to a local `warnings` array returned
  on the result.

### `matterService.ts`
- `createMatter` gained a new **optional, last-positional** param `association?: AssociationResult | null`
  (after `onUploadProgress`, so no existing positional call site is disturbed).
- Constructor's existing (already-required) `authenticatedFetch`/`bffBaseUrl` are now also retained
  as private instance fields (`_authenticatedFetch`/`_bffBaseUrl`) so `createMatter` can pass them to
  the engine (previously they were only forwarded into the internal `EntityCreationService`, with
  no getter exposed).
- Immediately after the lookup-binding loop (Matter has no `applyResolverFields` call — this IS the
  "equivalent regarding block" the task instructions anticipate) and BEFORE `createRecord`: calls
  the engine with `sourceEntity/sourceId = association.entityType/recordId`, `targetEntity: 'sprk_matter'`,
  when `association?.recordId && association.entityType`. Appends warnings to the existing `warnings`
  array (already part of `ICreateMatterResult`).
- **Matter→matter same-entity**: passed through with zero special-casing — `association.entityType`
  can legitimately be `'sprk_matter'` and nothing in the new code branches on it (verified by
  `matterService.fieldMapping.test.ts`'s "matter -> matter" test, and by grep — the only
  `source===target` string occurrences in the diff are explanatory comments).

### `projectService.ts`
- Constructor gained 2 new **optional** params: `authenticatedFetch?: AuthenticatedFetchFn`,
  `bffBaseUrl?: string` (mirrors EventService; Project had zero BFF deps before this task).
- `createProject` gained a new **optional** param `association?: AssociationResult | null` (3rd
  positional, after `cascadeDefaults`).
- `ICreateProjectResult` gained a required `warnings: string[]` field.
- Immediately after the lookup-binding loop (Project also has no `applyResolverFields` call) and
  BEFORE `createRecord`: calls the engine when `association?.recordId && association.entityType &&`
  both BFF deps are present. Appends warnings.

### Wizard `.tsx` call-site updates (necessary for the engine to actually activate — see Scope note)
- **`CreateEventWizard.tsx`**: primary `createEvent` call now passes `authFetch, bffBaseUrl`
  (already-existing component-scope variables — no new host plumbing); the follow-on warnings
  array is seeded from `result.warnings`.
- **`CreateMatterWizard.tsx`**: primary `service.createMatter(...)` call now passes
  `context.association` as the new 6th arg (5th arg `onUploadProgress` explicitly passed as
  `undefined` to preserve position); the follow-on "Create Event" call (Matter → Event) now passes
  `authenticatedFetch, bffBaseUrl` into its own `EventService` construction and surfaces
  `eventResult.warnings`.
- **`CreateProjectWizard.tsx`**: primary `projectService.createProject(...)` call now passes
  `authFetch, bffBaseUrl` into the constructor and `context.association` as the 3rd arg; result
  warnings are pushed onto the onFinish's local `warnings` array; the follow-on "Create Event"
  call (Project → Event) now passes `authFetch, bffBaseUrl` and surfaces `eventResult.warnings`.
- Left unchanged (lookup-only, no create call): `CreateEventStep.tsx`, `CreateProjectStep.tsx`,
  `SummarizeFilesDialog.tsx` constructions of `EventService`/`ProjectService`.

### New tests (not in the original `<outputs>` list — added to cover the acceptance criteria)
- `CreateEventWizard/__tests__/eventService.fieldMapping.test.ts` — 4 tests: profile-found mapping,
  404 graceful no-op, missing-BFF-deps graceful no-op, no-regarding-parent no-op.
- `CreateMatterWizard/__tests__/matterService.fieldMapping.test.ts` — 4 tests: profile-found mapping,
  **matter→matter same-entity positive test**, 404 graceful no-op, no-association no-op.
- `CreateProjectWizard/__tests__/projectService.fieldMapping.test.ts` — 4 tests: profile-found
  mapping, 404 graceful no-op, no-association no-op, missing-BFF-deps no-op.

## Wiring contract confirmation (per-service)

| Service | Engine called after resolver/before createRecord | Warnings appended | Never aborts on mapping failure | No hard-coded target field names | matter same-entity pass-through |
|---|---|---|---|---|---|
| eventService | ✅ (inside the `applyResolverFields` regarding block) | ✅ | ✅ (engine itself never throws; no new try/catch added around it) | ✅ (target field names come entirely from the fetched profile's rules) | N/A (Event has no matter-parent special case to begin with) |
| matterService | ✅ (after the lookup-binding loop, the equivalent regarding block) | ✅ | ✅ | ✅ | ✅ — verified by dedicated test |
| projectService | ✅ (after the lookup-binding loop) | ✅ | ✅ | ✅ | N/A (Project's parent is Matter/Account, not itself) |

## Verification

- `npx tsc --noEmit` (from `src/client/shared/Spaarke.UI.Components`) → **0 errors**.
- `npx jest eventService matterService projectService` → **7 suites, 36 tests, all passing**
  (4 pre-existing suites unaffected + 3 new field-mapping suites).
- `npx jest CreateEventWizard CreateMatterWizard CreateProjectWizard` (broader sweep incl. `.tsx`
  component tests) → **8 suites, 50 tests, all passing** — confirms the wizard call-site edits
  didn't regress component behavior.
- No dist-write race: verification used `tsc --noEmit` (no emit) per the parallel-wave
  instructions; the main session owns the authoritative `npm run build` after all 3 Wave-B tasks land.

## Quality gates (Step 9.5)

**code-review** — CLEAN. 0 Critical, 0 Warning (blocking), several Suggestions:
- Minor duplication: the ~10-line "call engine, push warnings" block is repeated 3x with only
  entity-name substitutions; a shared helper was considered but rejected as scope creep beyond
  task 020's directive (each call site's inputs differ slightly — `regardingEntityName` split
  fields for Event vs. a single `AssociationResult` for Matter/Project).
- `matterService.ts` now stores `_authenticatedFetch`/`_bffBaseUrl` as private fields duplicating
  what's already passed into the internal `EntityCreationService` (which exposes no getter) —
  accepted as the minimal-diff option.
- No AI code smells introduced (no new single-impl interfaces, no catch-log-rethrow, no null
  checks on non-nullable types — the `if (this._authenticatedFetch && this._bffBaseUrl)` guards
  are checking genuinely-optional fields; comments explain WHY/ordering, not restating code).

**adr-check** — CLEAN.
- ADR-024 (polymorphic resolver): engine call placed strictly after `applyResolverFields`
  (Event) / after the equivalent lookup-binding block (Matter, Project); does not interfere with
  the 5-field resolver write or the mutual-exclusion invariant. No `source === target` guard
  anywhere — grep confirms the only 5 occurrences of "source===target" in the diff are explanatory
  comments, matching the pattern already established in `FieldMappingService.ts` (task 010).
- ADR-012 (context-agnostic shared library): no `ComponentFramework` references introduced
  (grep confirmed zero matches in the 3 changed service files); all engine I/O remains injected
  (`IDataService` + `AuthenticatedFetchFn` + `bffBaseUrl` string) — no PCF-specific types.
- ADR-028 (auth): `authFetch`/`authenticatedFetch` passed to the new constructor args are the
  pre-existing, already-injected host `authenticatedFetch` props (confirmed in `CreateEventWizard.tsx`
  via `authenticatedFetch: authFetch` destructuring) — no raw `fetch` + `Authorization` header, no
  `tokenBridge`/`__SPAARKE_BFF_TOKEN__` introduced.
- §10 BFF Hygiene: N/A — no BFF files touched by this task.

## Notes for downstream tasks

- Task 040 (architecture doc): should note that Matter and Project did not have a pre-create
  "regarding parent" mechanism before this task — this task added an `association` parameter to
  both `createMatter`/`createProject` specifically to carry the AssociateToStep selection into the
  Field Mapping Framework engine call. This is additive (nothing in the N:N post-create association
  flow changed) but is worth documenting since it's a new capability, not purely "wiring an existing
  mechanism."
- The follow-on "Create Event" paths inside `CreateMatterWizard.tsx`/`CreateProjectWizard.tsx` (the
  "Assign Work" style follow-on actions) now also get field-mapping applied (Matter→Event,
  Project→Event) since they construct their own `EventService` — this is a superset of the literal
  task ask but consistent with the goal ("Event... wizard-created records inherit mapped fields at
  creation").
