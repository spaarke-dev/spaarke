# Task 025 — CreateEventWizard FR-WIZ-05 Verification Handoff

**Date**: 2026-06-07
**Task**: 025-create-event-wizard-extension
**FR**: FR-WIZ-05 — Event create payload MUST include BOTH `sprk_containerid` AND `sprk_searchindexname` from the user's owning BU
**Assumption A1 outcome**: Latent gap **CONFIRMED + FIXED**

---

## Verification Step 1 — Was `sprk_containerid` being set?

**No.** Pre-task, `src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/eventService.ts` `EventService.createEvent` built its `entity` payload with only:

| Field | Set by pre-task code? |
|---|---|
| `sprk_eventname` | Yes (from form) |
| `sprk_priority` | Yes (from form) |
| `sprk_description` | Conditional (form) |
| `sprk_duedate` | Conditional (form) |
| `sprk_eventtype_ref` lookup binding | Conditional (form) |
| Regarding-record nav-prop binding | Conditional (form) |
| **`sprk_containerid`** | **No — gap** |
| **`sprk_searchindexname`** | **No — gap** |

Neither cascade field was wired. `EventService` did not consume the BU-defaults helpers added by Task 020 (`EntityCreationService.applyUserBuDefaults` / `resolveUserBuDefaults`).

The `CreateEventWizard.tsx` component had no `_containerId` constructor parameter (unlike `MatterService`) and no `EntityCreationService` dependency in its `EventService` instantiation path (the EntityCreationService was already imported but only used for SPE file uploads + email — not for record creation).

## Fix Applied

Inside `eventService.ts` (consistent with the **canonical pattern** established by `matterService.ts` FR-WIZ-01 cascade block, lines ~274–319, and the test pattern established by sibling task 024 / `workAssignmentService.ts`):

1. Imported `EntityCreationService` + `IWebApiLike`.
2. Added module-private helpers `_tryGetCurrentUserId()` (cross-origin-safe walker over `window`/`window.parent`/`window.top` checking both `Xrm.Utility.getGlobalContext().userSettings.userId` and `Xrm.Utility.getUserId()`) and `_toWebApiLike()` (adapter).
3. Extended `EventService.createEvent(formValues, regardingEntityName?, options?)` with an optional third `options.getCurrentUserId` injection seam for testability.
4. Added a try/wrapped cascade block immediately after building the base `entity`:
   - Resolves `userId` via `options.getCurrentUserId ?? _tryGetCurrentUserId`.
   - When a userId is available, calls `EntityCreationService.resolveUserBuDefaults(webApi, userId)` → `EntityCreationService.applyUserBuDefaults(entity, buDefaults)`.
   - Logs `console.info` on cascade hits, BU-NULL skips, and unavailable Xrm.
   - Cascade is **best-effort** — failures (network, missing BU, Xrm unreachable) are caught + logged; event creation never blocks on cascade.

## INV-5 Compliance

The cascade is performed via `EntityCreationService.applyUserBuDefaults`, which the lower-level test suite (`EntityCreationService.cascade.test.ts`) already exhaustively verifies for INV-5:
- Pre-seeded non-empty `sprk_containerid` on the payload → preserved.
- Pre-seeded non-empty `sprk_searchindexname` on the payload → preserved.
- Each field guarded independently.

The Event wizard does not currently pre-seed either field, but the cascade helper enforces INV-5 regardless of caller — meaning any future enhancement (e.g., form field for explicit container override) inherits INV-5 safety for free. The cross-cutting INV-5 contract test lives in `src/client/shared/Spaarke.UI.Components/src/services/__tests__/EntityCreationService.cascade.test.ts`.

## Tests Added

`src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/__tests__/eventService.cascade.test.ts` — 8 tests covering:

1. **Happy path** — BOTH fields cascade on a clean payload (FR-WIZ-05 acceptance).
2. **Regarding-record path** — cascade unaffected by `regardingEntityName` branch.
3. **Spaarke Dev 1 / Test 1** — BU has `containerId` but NULL `searchindexname` → only container cascades; index left unset.
4. **Reverse scenario** — NULL container, populated indexname → only index cascades.
5. **Graceful degradation: no Xrm host** — cascade skipped; BU lookup never called; createRecord still succeeds.
6. **Graceful degradation: BU resolve throws** — cascade swallowed; createRecord still succeeds.
7. **User has no BU** — cascade is a no-op; createRecord still succeeds.
8. **Test-injection seam** — explicit `getCurrentUserId` override honored (path forward for higher-level integration tests).

## Build / Lint Status

| Check | Result |
|---|---|
| `npm run build` (`tsc`) | 0 errors |
| `npx eslint <modified files>` | 0 errors, 0 warnings |
| `npx jest --testPathPatterns=eventService.cascade` | 8/8 pass |
| `npx jest --testPathPatterns="cascade\|EntityCreationService"` | 39/39 pass across 4 suites (no regressions) |

## Files Modified / Created

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/eventService.ts` | EXTENDED — added `_tryGetCurrentUserId` + `_toWebApiLike` helpers + FR-WIZ-05 cascade block in `createEvent`; widened signature with optional `options.getCurrentUserId` injection seam |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/__tests__/eventService.cascade.test.ts` | CREATED — 8 cascade + INV-5 + degradation tests |

## Out of Scope (Intentional)

- `EntityCreationService.ts` — unchanged (constraint).
- `CreateEventWizard.tsx` — unchanged. The cascade is performed inside `EventService.createEvent` so the wizard component does not need to plumb a `getCurrentUserId` callback; the default Xrm probe handles all current hosts (PCF / Code Page).
- Other wizards (Matter, Project, Invoice, WorkAssignment, DocumentUpload) — unchanged.
- `src/client/code-pages/CreateEventWizard/` — does not exist (only shared-lib component is touched). Verified via `glob`.

## Downstream Implications

- **Task 028 (consumer)** — when the Event code-page solution mounts the wizard, it MUST ensure `Xrm.Utility.getGlobalContext()` resolves to the host user (default behavior in any Power Apps / Custom Page / model-driven host). No additional wiring required at the consumer.
- **Task 071 (UAT)** — MCP-verifiable acceptance: create an event via the wizard from a Spaarke Demo BU user; query `sprk_event` for the new record; expect `sprk_containerid` + `sprk_searchindexname` both populated with the BU's values.
