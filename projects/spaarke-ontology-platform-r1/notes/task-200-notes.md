# Task 200 notes: lookup fields return keyboard focus after the Dataverse lookup pane closes

Branch `fix/ontology-200-lookup-field-focus`, cut from origin/master `2dcbe35e34`. Found by the main session's dev check on 2026-11-22
(Console > Ontology admin > New rule > Scope and noise > "Limit to one matter": after a pick `document.activeElement` was `<body>`).

## The one helper

`src/client/shared/Spaarke.UI.Components/src/utils/focusReturn.ts` (moved out of `EmailComposer.tsx`, where task 199 added it as a local function).

| Export | Shape | Use |
|---|---|---|
| `scheduleFocusReturn(spec): () => void` | `spec = { doc, target(), fallback?(), isKept?(active), isActive?() }`; returns `cancel` | The core: "put focus back on X in its own document unless focus moved somewhere meaningful". Task 202 (SprkModal returns focus on close) uses this directly: `target` = the element that opened the modal, `isKept` = focus is inside something else the host opened. |
| `useFocusReturn(scope): (trigger) => void` | `scope = { getRoot(), getRegion?(), getFallback?() }`; `trigger` = an element or a function that finds it | React binding used by every trigger: tracks unmount, supersedes a pending return, cancels on unmount. `returnFocus(trigger)` is called when the lookup has settled. |
| `closestDialogOrSelf(el)` | closest `[role=dialog]` holding `el`, else `el` | The region for "focus is already inside the host dialog on purpose". |

Behaviour (unchanged from 199, now shared): deferred on a timer so the host's "yielding" state is cleared and the trigger has re-rendered; `target` is
evaluated at each attempt (never captured earlier); the document and window come from the surface's own `ownerDocument` (iframe-safe); skips when the
surface unmounted or focus is already inside the region; verifies the focus landed and retries up to 10 x 50 ms (cross-frame `inert` lift); stops when
focus moved to some other real element since the previous attempt (body/null counts as "nobody has it").

One change to the core while moving it: `seenAfterAttempt` is now recorded after EVERY attempt, including one where nothing could be focused yet
(control still disabled / not yet re-rendered). Without it, a link the pane left focus on was mistaken at the retry for "a host chose this" and the
return gave up. The DataverseLookupField tests caught it; EmailComposer's 14 tests pass unmodified.

Not exported from the package index. Nothing outside `@spaarke/ui-components` needs it today; every caller in this PR is inside the package, and the PCFs
and code pages get the behaviour through the components. (The follow-up issue below would need an export; that is its first step.)

EmailComposer now calls `useFocusReturn({ getRoot: rootRef, getFallback: rootRef })` and keeps a 12-line `withLookupFocusReturn(trigger, run)` wrapper
(run, then `returnFocus(trigger)` on resolve or reject). Region = the composer root (its own tests need that), so it does not use `closestDialogOrSelf`.

## Inventory (shared lookup triggers that await a side-pane lookup)

Grep: `openLookup`, `lookupObjects`, `lookupSearch`, `pickPerson`, `pickOrganization` in `Spaarke.UI.Components/src` (non-test).

| Trigger | Awaits | Changed | Focus target after settle |
|---|---|---|---|
| `LookupField/DataverseLookupField.tsx` | `navigationService.openLookup` | yes | the field's one button (`controlRef`): Select when empty, Clear after a pick; region = closest dialog |
| `AssociateToStep/AssociateToStep.tsx` | `navigationService.openLookup` | yes | the Select Record button (it stays rendered) |
| `CreateRecordWizard/CreateRecordWizard.tsx` (existing-record picker) | `picker.navigationService.openLookup` | yes | the one button the row renders (Select Existing, or Clear after a pick), found by query after the re-render |
| `DecisionWizard/steps/ParameterFields.tsx` (LookupParameter) | host `openLookup` | yes | the field's input (it stays rendered for empty and filled) |
| `PolymorphicPicker/PolymorphicPicker.tsx` | `Xrm.Utility.lookupObjects` | yes | the toolbar trigger (disabled while the pane is open) |
| `RecordHeader/fields/LookupField.tsx` (via `lookupSearch.openAdvancedLookup`) | `Xrm.Utility.lookupObjects` | yes | the field's `role=button` value (a different element empty vs filled), by query |
| `AccessGrantModal/AccessGrantModal.tsx` (Add person / Add organization) | host `pickPerson` / `pickOrganization` | yes | the "+" button that opened it (both disabled while a pane is open) |
| `EmailComposer` (recipients, Link a record, Link documents, record search) | host callbacks | yes (task 199's helper moved, behaviour kept) | unchanged from 199 |
| `DecisionWizard/steps/EmailMessageFields.tsx` | the composer's `onLookupRecipients` | no change needed | covered by EmailComposer |
| `RecordHeader/lookupSearch.ts` | the single `lookupObjects` call site | no | not a trigger: a function with no UI; its caller (the row above) restores focus |
| `Worklist/WorklistWidget.tsx`, `DecisionWizard/DecisionWizard.tsx`, `DecisionItemWizard.tsx` | pass `openLookup` through | no | not triggers; `ParameterFields` is the trigger |
| `CreateWorkAssignmentWizard/SelectWorkStep.tsx` and the other create wizards | render `DataverseLookupField` | no | covered by the field |
| `utils/adapters/*NavigationServiceAdapter.ts`, `services/*` | implement the pane | no | adapters, not triggers |

Triggers outside `@spaarke/ui-components` (same defect, not touched here, filed as #2985): `Spaarke.AI.Widgets` CreateAnalysisWizardWidget.tsx:495,
`Spaarke.Compose.Components` ComposeWorkspace.tsx:4014, `Spaarke.Communication.Components` TaskReconcileTab.tsx:333 / FieldUpdateReconcileTab.tsx:436 /
EmailConnectionsReview.tsx:244, PCF `CommunicationConnections` CommunicationConnectionsApp.tsx:694, code pages `DocumentUploadWizard` AssociateToStep.tsx:321
(a local copy), `EventDetailSidePane` LookupFieldRenderer.tsx:108, `FindSimilarCodePage` App.tsx:302.

Design choices worth a reviewer's eye:
- DataverseLookupField uses a ref on its one button rather than a `querySelector` from the wrapper: `labelExtra` is caller content and could hold a button, a
  ref cannot pick the wrong one. It is read when the timer fires, after the re-render, so it is not stale.
- `returnFocus` is called in the `finally`, AFTER `onChange` and the state resets, so the control is looked up once the re-render has been scheduled.
  The retry covers a render that lands later; a control still disabled at the first attempt is retried (it used to be skipped).
- Region = closest `[role=dialog]` holding the field, else the field's own wrapper. A field on a plain form (no dialog) therefore takes focus back when it is
  on `<body>` or anywhere outside the wrapper; the platform pane is modal-ish while open, so focus elsewhere on the page at settle time is the pane's
  hand-back, not a user choice.

## Fail-before evidence

New tests (7 files, 31 tests). Run against the UNCHANGED component sources (the 7 modified components restored from origin/master, the new tests and
`focusReturn.ts` kept): 7 suites failed, 20 tests failed, 7 passed. The 7 that pass on old code are the negative cases (focus inside the dialog is not moved;
unmounted before settle makes no focus call), which hold on old code by construction; every pick / cancel / failure case failed:

- DataverseLookupField (5): pick (focus on body), pick (focus on a link outside the dialog), cancel, openLookup throws, field outside any dialog
- AssociateToStep (2): pick; cancel + failure
- PolymorphicPicker (2): pick; cancel
- RecordHeader LookupField (2): pick on an empty field; cancel
- CreateRecordWizard existing-record picker (2): pick (Clear button); cancel (Select Existing button)
- AccessGrantModal (4): Add person pick; Add person cancel; Add organization pick; Add organization cancel
- DecisionWizard ParameterFields (3): pick; cancel; failure

Typical failure: `expect(document.activeElement).toBe(<the control>)` received `<body>` / the outside link.

After the change: the same 7 suites pass (31/31). EmailComposer.lookupFocus (14 tests) passes unmodified against the moved helper.

## Verification

- Filtered jest (from `src/client/shared/Spaarke.UI.Components`): LookupField, EmailComposer, AssociateToStep, PolymorphicPicker, RecordHeader, CreateRecordWizard,
  DecisionWizard, AccessGrantModal, Worklist: 106 suites / 1860 tests pass. CreateMatterWizard, CreateProjectWizard, CreateWorkAssignmentWizard,
  CreateTodoWizard, CreateEventWizard, CreateReportCardWizard, CreateInvoiceWizard, Wizard, NewThreadModal, TodoDetail, utils: 112 suites / 1637 tests pass.
- `tsc --noEmit -p .` in `Spaarke.UI.Components`: clean. `npm run build` (tsc, fresh `dist/`): clean.
- `node scripts/quality/client-lint/client-lint.mjs check` (after a fresh `dist/`): ui-components, communication-components, spaarkeai, semantic-search ok.
  ai-widgets, compose-components, daily-briefing-components, legal-workspace-app and reporting report errors in files this PR does not touch
  (PinnedMemoryEditDialog.tsx:463, AISummaryDialog.tsx:455, ReportDropdown.tsx:278 and "suppressions that no longer occur"); those five surfaces have no
  `node_modules` in this worktree, so the type-aware rule runs without their types. Not verified against master; none of the files is in this diff.
- prettier: clean on every changed/new `.ts`/`.tsx`.

## PCFs (bundle check, versions, builds)

Which PCFs bundle a changed module was decided from the imports (`sideEffects: false`, so tree-shaken) and confirmed on the built bundles. All eight were
built with `scripts/Invoke-PcfBuildProd.ps1` (judged by the build output: `PASS ... [build] Succeeded`, no webpack errors, not the exit code).

| PCF | Bundles | Old -> new |
|---|---|---|
| CommunicationActions | EmailComposer (shared helper moved; no behaviour change beyond the retry fix) | 1.3.24 -> 1.3.25 |
| TrackingFieldTrio | EmailComposer, AccessGrantModal (Add person / Add organization) | 1.0.66 -> 1.0.67 |
| SemanticSearchControl | EmailComposer (via DocumentEmailWizard) | 1.1.91 -> 1.1.92 |
| RecordHeader | RecordHeaderLookupField (record header lookup) | 1.1.12 -> 1.1.13 (the 4-part `1.1.13.0` in solution.xml and pack.ps1) |
| RegardingResolver | PolymorphicPicker | 1.6.4 -> 1.6.5 |
| CommunicationConversationPanel | AssociateToStep (new-thread modal) | 1.18.5 -> 1.18.6 (pack.ps1 reads solution.xml) |
| CommunicationTimeline, CommunicationTimelineRegarding | built, bundle does NOT contain the changed components (no `associate-to-step` markup in the bundle) | unchanged, not bumped |

The three composer PCFs were already bumped by task 199 (1.3.24 / 1.0.66 / 1.1.91); their bundles change again here (module move plus the retry fix), so they are bumped
again. If 199's versions were never deployed, the extra bump is harmless. MatterHeader imports only the inline `LookupField` (which does not use the helper), so it is unchanged.

`npm install` for the five PCFs that had no `node_modules` here rewrote their `package-lock.json`; those were restored (not part of the change). No bundle or `out/` is committed.

## Console and code pages

- Console (`src/solutions/SpaarkeAi`, ships LegalWorkspace): `npm run build` succeeds (`vite build` ok, ribbon build ok; the tsc surface gate reports 0 surface-owned errors, 439 pre-existing in shared libs).
- Code pages built (`npm install --legacy-peer-deps` then `npm run build`, each ends `built in`): CreateMatterWizard, CreateProjectWizard, CreateWorkAssignmentWizard,
  CreateTodoWizard, CreateEventWizard, CreateReportCardWizard, SmartTodo, SummarizeFilesWizard, RuleReview. Their `package-lock.json` changes were restored.
- NEEDS REDEPLOY for the fix (these bundle `DataverseLookupField`; found by tracing imports from each solution): the nine above plus the Console. After a Console deploy
  the dev check path is Console > Ontology admin > New rule > "Limit to one matter" (RuleReview is the standalone page for the same field). The Console auto-deploys to dev from master on merge
  (`deploy-spaarke-ai.yml`); the code pages do not.
- Bundle changes only, behaviour unchanged (composer helper moved; redeploy at the next release, not for this fix): DocumentUploadWizard, EmailPage, DailyBriefing,
  CommunicationReconciliation, sprk_communicationconversationpage, sprk_communicationspage, and the office add-ins (via communication-components). Static analysis, not built here.
  CreateInvoiceWizard and RecordFamily are flagged only through `useWizardPageBootstrap` (type-only edges over-count); treat as unchanged unless a bundle diff says otherwise.

## Out-of-scope defects (Step 9.5)

- #2985: nine lookup triggers outside `@spaarke/ui-components` with the same defect (listed above). Failure scenario: after a pick or cancel in any of them the keyboard user's
  focus is on `<body>`. First step of the fix is how those packages reach `useFocusReturn` (it is internal to the package today).
- The five client-lint surfaces with no `node_modules` in this worktree fail on files this PR does not touch (see Verification); CI installs them, so not filed.

## Quality gates

Self-review of the diff (no sub-agents, as instructed): no new prop, component, endpoint or package (CLAUDE.md section 11: one internal helper, replacing 199's local one). No behaviour
change to values, `onChange`, single-flight, cancel or failure handling: the only added call in each trigger is `returnFocus(...)` in the existing `finally`. ADR-012 (shared library
stays free of host imports): the helper uses only DOM and React. ADR-021 not touched (no styling). The independent review is the main session's.

Known limits (K-class): the retry window is 10 x 50 ms, so a host that lifts `inert` later than that leaves focus on the body (same bound as 199); a field on a plain form with no
dialog takes focus back from any element outside its wrapper at settle time (see design choices).

rework: none task 200

