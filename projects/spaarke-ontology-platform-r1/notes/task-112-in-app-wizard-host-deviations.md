# Task 112: in-app Create wizard host — deviations and decisions

> Date: 2026-10-08 · Branch `feat/create-wizards-in-app-112` (worktree `C:\wts-112`) · Base `origin/master` @ `23c359eaf` (056 / #1386 merged) · PR #1422

This note records the deviations required by POML step 7. Each entry gives what the POML asked for, what was done, and why.

## D1 — The live Console paths are `launchSurface`, not the fire-and-forget launchers

- **POML**: switch the launches at `wizardLaunchers.ts` ~:159 (`fireNavigateTo`), :197, :219, :232, :289.
- **Found**: none of the five fire-and-forget `launchCreate*Wizard` / `launchAssignWorkWizard` functions has a live caller. The Console opens Matter, Project, Event (`create-task`) and Work Assignment through `launchSurface` → `navigateToWebResourceSurfaceAsync`, which also lives in `wizardLaunchers.ts`. The callers are Quick Start, the Assistant's `surface_launch`, and `CreateRecordChooserModal`.
- **Done**: the routing seam sits under both primitives (`fireNavigateTo` and `navigateToWebResourceSurfaceAsync`), so every path reaches the host. The hand-off contract is unchanged:
  - the host reads the envelope and pre-seeds the wizard;
  - it writes the committed result before closing;
  - `launchSurface` reads the same outcome it reads after a `navigateTo` close.

## D2 — WorkspaceGrid has a sixth entry: the generic `onOpenWizard`

- **POML**: the five dedicated handlers (~:312, :343, :397, :413, :429).
- **Found**: the layout-driven sections reach the wizards through the generic `onOpenWizard(webResourceName)` (`handleOpenWizardGeneric`). Those are the Get Started cards, the To Do "+" and the Latest Updates "+". The dedicated handlers serve only the fallback config and the expand dialog.
- **Done**: both paths are routed. The generic path checks `isInAppWizardName` and routes those five names through the same shared primitive. Every other web resource keeps its `navigateTo`.

## D3 — Files outside the POML list

| File | Why |
|---|---|
| `SpaarkeAi/.../shell/ThreePaneShell.tsx` | The host must be mounted once. It sits next to `CreateOnSaveAssociationGateDialog`, inside the host `FluentProvider`. |
| `CreateRecordWizard` (+ `types.ts`), `CreateMatterWizard`, `CreateProjectWizard`, `CreateEventWizard`, `TodoWizardDialog`, `WorkAssignmentWizardDialog` | Optional `uiScale` prop forwarded to `WizardShell`. ADR-050 requires wizards to pass `uiScale`, and none of these exposed it. |
| `CreateTodoWizard/todoWizardHostSupport.ts` (new), `src/solutions/CreateTodoWizard/src/main.tsx`, `LegalWorkspace/.../todo.registration.ts` | The To Do create broadcast and default-assignee lookup were inline in the code page. The in-app host needs both, so they moved verbatim into a shared module rather than being copied. The To Do widget shim imports the channel constants, replacing the "keep in lockstep" comment. |
| `WorkspaceShell/index.ts`, `components/index.ts`, `CreateTodoWizard/index.ts` | Barrel exports. `InAppWizardHost` is exported from `components/index.ts`, not from the domain-free `Wizard` barrel, because it imports the five Create wizards. |

## D4 — Review fixes

- **`CreateRecordWizard`** (pre-existing, found in passing): `config.resolveSpeContainerId().then(...)` had no `.catch`, so a failed lookup was an unhandled rejection in every host. It is now caught and logged; the id stays `''`. A test and a mutation check cover it.
- **LegalWorkspace Get Started expand dialog** (a consequence of in-app hosting, not a pre-existing defect): a card click now closes the picker before opening the wizard. Otherwise an in-app wizard would stack over the open picker, where the platform dialog used to sit over it. Nothing else about the cards changed.
  - No unit test: LegalWorkspace has no jest setup. Verified by build and by reading the code.
  - **Correction (independent review of #1422, F2-1).** An earlier version of this note, the code comment, the PR body and the POML said the dialog passed its click event to the handlers, so that Summarize Files never opened and Find Similar sent `documentId=[object Object]`. That was wrong. `LegalWorkspace/src/components/GetStarted/ActionCard.tsx` calls `onClick()` with no arguments (:115-118, :125), on master too. Summarize Files already opened, and Find Similar already sent an empty `documentId`. All descriptions are corrected.
- **WorkspaceGrid**: `getBffBaseUrl()` stays inside a try, as before, so an uninitialised config cannot become an unhandled rejection.

## D5 — Decisions inside the host

- **One wizard at a time.** A second launch while one is open resolves at once, which `launchSurface` reads as cancelled. It neither stacks nor replaces the open wizard.
- **Never `closeDialog()`.** The host closes by unmounting. `navigationService.closeDialog()` clicks the platform close button of the dialog the Console itself may be in.
- **Parity cuts.** The code pages also read `entityType/entityId` (Event `initialAssociation`/`lockAssociation`, To Do `initialRegarding`). No in-app launch in scope passes a record, so the host omits them.
- **Container resolution.** Container and BU defaults use one resolver: `EntityCreationService.resolveUserBuDefaults` via `getXrm(['webApi','utility'])`. It passes the Tier 1 Xrm capability guard (85/85).
- **No host mounted.** Behaviour is unchanged; `navigateTo` is called with the same shape. The only shape change is that a title-less `navigateToWebResourceSurfaceAsync` call now omits `title` instead of sending `undefined`. The generic path is the only title-less caller, and it sent no title before either.

## D6 — Not done / out of scope (filed)

- **#1421**: `Spaarke.AI.Widgets` `CreateProjectWizardWidget` still has its own `navigateTo` for `sprk_createprojectwizard`. It is outside 112's file scope, and the fix is a one-call swap to `navigateToWebResourceSurfaceAsync`.
- **#1420**: Work Assignment never reports a committed create to `launchSurface`. It has no `onComplete` seam; the code page shares the gap, which predates this task. As a result Quick Start's `onRecordCreated` never fires for "Assign Work".
- **Not run**: the UI tests in the POML (`<ui-tests>`: open each wizard live, ribbon unchanged, dark mode). Deploying is task 114.

## Known limits (K-class, one line each)

- **K**: when the Console itself runs inside a Dataverse dialog, the in-app wizard is sized within that dialog, not the browser window. This is inherent to in-app hosting.
- **K**: the wizards' lookup steps (`openLookup`) and "View record" (`openForm`) still open platform surfaces from inside the in-app modal. `CreateAnalysisWizardWidget` and SmartTodo already do this.
- **K**: `buildDynamicWorkspaceConfig` case (h) fails on master (#1345), unrelated to this task.

## Measurements

- **SpaarkeAi bundle** (`npm run build`, `.vite` cache cleared): 5,910,532 bytes on master `23c359eaf` → 5,914,116 bytes; **+3,584 bytes**. All five wizards were already bundled. The built bundle contains one copy of the routing registry.
- **Environment note**: the ribbon sub-build needs `Spaarke.SdapClient/dist` and `Spaarke.Auth/dist` in a fresh worktree; build both first.
