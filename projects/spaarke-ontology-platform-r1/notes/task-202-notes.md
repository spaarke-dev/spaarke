# Task 202 notes: SprkModal returns keyboard focus to its opener on close

Branch `fix/ontology-202-sprkmodal-focus-restore`, cut from origin/master `973d72b67e`. Found by the main session's dev check on 2026-11-22 (Console, 1440x900):
every close of a SprkModal left `document.activeElement` on `<body>`.

## Cause

`SprkModal` wraps Fluent `Dialog`. Fluent restores focus only to a `DialogTrigger` or a `useRestoreFocusTarget` element; hosts open a SprkModal from state,
so neither applies, and the element that had focus is removed or ignored. No code in SprkModal returned it.

## The fix (SprkModal.tsx, no new prop, no new component, no second helper)

| Piece | Where | Why |
|---|---|---|
| Capture the opener | in RENDER, on the render where `open` is true and the modal was not open (`openerCandidateRef`); committed to `openerRef` by a layout effect | See "Ordering evidence" |
| Restore | the same layout effect, on `open` true -> false; and the unmount cleanup when still open | Only a real open -> closed transition. `hidden` and `yieldToSidePane` keep `open` true, so they never reach it (task 198 behaviour untouched). |
| How | `scheduleFocusReturn` from `utils/focusReturn.ts` (task 200): `setTimeout(0)` then the bounded retry (10 x 50 ms), in the opener's own `ownerDocument` | iframe-safe; lands after Fluent/tabster; verified and retried |
| Surface element | a callback ref keeps `lastSurfaceRef` | `surfaceRef` is null by the time the close effect runs (the surface unmounts in the same commit) |
| Reopen | the open branch cancels a pending restore | a quick close + reopen is not pulled out |

Helper: NOT changed. `scheduleFocusReturn` already evaluates `isKept(active)` before attempt 1, so the "focus moved since the last attempt" guard being
attempt-2-only (the task-200 review's known limit) does not matter here: SprkModal passes an `isKept` that decides on attempt 1 too.

"Lost" (restore) is: focus on `body`/`documentElement`/nothing, OR on a node inside the closing surface, OR on a node no longer connected. Focus on ANY other
connected element is KEPT (another open dialog, a parent wizard the host focused on purpose, an element the host focused as it closed). The target is the
opener only while it is connected, enabled (the helper skips a `disabled` target) and not inside an `[inert]` subtree; otherwise the attempt retries and then
gives up without forcing focus anywhere.

## Ordering evidence

1. **Capture before anything moves focus.** First attempt was a layout effect on `open`. The test "captures the opener before Fluent moves focus" (content with a
   `useLayoutEffect(() => input.focus())`) showed that is too late: the Fluent portal content mounts in the SAME commit as SprkModal, so a child's layout effect
   (or an `autoFocus` input) has already taken focus when a parent layout effect runs; the opener captured was the input. Fluent's own first-element focus
   (`useFocusFirstElement`) is a passive `useEffect` and comes later still. Capturing in render, before the commit, fixes it. Fails if reverted to the layout-effect
   capture.
2. **Restore after the parent is interactive again.** Test (d): a host that sets `[inert]` on the wizard surface while the confirm is open and removes it in a
   passive effect of the commit that closes the confirm, with `HTMLElement.prototype.focus` made a no-op inside `[inert]` (jsdom does not refuse it; a browser does).
   Mutation check: replacing the scheduled restore with a synchronous `opener.focus()` in the layout effect makes (d) fail (and 4 others); the real code passes.
   This is the jsdom stand-in for "tabster re-enables the wizard after the confirm closes".
3. Nested cases (b)/(c)/(b2)/(c2): ConfirmModal always mounted (RuleWizard shape) and mounted only while asked, so it unmounts while open (DecisionWizard shape).
   Keep returns focus to the wizard's own button; Discard (confirm + wizard close together) ends on the page button that opened the wizard, and stays there after
   the confirm's own dead restore has run its retries (it finds focus on a real connected element and leaves it).

## Tests

`SprkModal/__tests__/focusRestore.test.tsx` (17) and `OntologyAdmin/Classification/__tests__/ClassificationFocus.test.tsx` (5).

Fail-before (new tests, the three changed sources restored from origin/master, new tests kept): **13 failed, 9 passed** of 22. The 9 that pass on master are the
negative cases (opener removed / disabled / inert, host focused something else, focus in another dialog, hidden toggle, yield on/off, close+reopen, "focus the
person put elsewhere after Cancel"), which hold on master by construction (nothing ever moved focus). Every positive case failed.

| Positive case | Fails on master |
|---|---|
| (a) button opens, Cancel closes -> button; (a2) the x | yes, yes |
| (b) confirm over wizard, Keep -> wizard button; (b2) Discard -> page button | yes, yes |
| (c) conditional confirm Keep; (c2) Discard and close | yes, yes |
| (d) ordering | yes |
| capture before Fluent / content focus; iframe own document | yes, yes |
| Classification: Edit -> Name, Cancel -> Edit; Save -> Edit; row gone -> table; New category | yes x4 |

## Scope item (4): Classification triage-category form

`CategoryForm` focuses its Name field on mount (Edit and New category both; the New category button is hidden while the form is open, so the same defect applied).
`ClassificationArea` returns focus when the form goes from open to closed (Cancel or Save): the row's Edit button, else the table (`tabIndex={-1}`; the row is gone),
else the New category button, through `useFocusReturn` (task 200). Edit-to-edit (another row's Edit while a form is open) does not return focus.

## Consumers that return focus themselves

`EmailComposer.closeFailureNotice` (a `setTimeout(0)` that focuses the composer root when focus is not inside it) schedules its timer first; the SprkModal restore
(scheduled in the layout effect, after) then finds focus on a real connected element (the root) and keeps it. Final focus is where it is today; the 14
`EmailComposer.lookupFocus` tests, `sendFailureDialog` and the EmailComposer suite pass unmodified. Grep for other post-close `.focus()` after a SprkModal in this
package found no other.

## Known limits (K-class)

- K4: in jsdom, tabster's aria-hidden bookkeeping and `inert` focus refusal are only modelled (test (d)); the real-browser order (Fluent/tabster moves focus after the
  `setTimeout(0)`) is the escalation trigger in the POML and is for the main session's dev check.
- K2: when two modals close in one commit and the inner opener is inside the outer surface (Discard), the inner modal's restore retries up to 10 x 50 ms with nothing
  to focus, then stops; it never forces focus (it yields as soon as the outer restore lands).
- Renders in React StrictMode run the capture twice; it only reads `document.activeElement`.
- Safari does not focus a button on click, so there is no opener to capture there; the restore does nothing (focus is on body as today).

## Verification

- Filtered jest from `src/client/shared/Spaarke.UI.Components` (SprkModal/**, presets, DecisionWizard, OntologyAdmin, Wizard (WizardShell), EmailComposer, LookupField, utils): 116 suites, **2030 passed**, 0 failed. Existing tests unmodified.
- Whole package (`npx jest`): 448 suites, 7038 tests; 3 failed in that run (`AccessGrantModal.followsParent`, `EmailComposer/sendFailureDialog`, `DecisionWizardEmailComposer`) while a PCF
  build and another jest were running on the same machine; re-run alone and together (3 times), all 465 pass. They pass on master too. Treated as load timeouts, not a regression.
- Package typecheck (`npx tsc --noEmit -p .` in `Spaarke.UI.Components`): clean.
- `node scripts/quality/client-lint/client-lint.mjs check` (repo root): ui-components, communication-components, spaarkeai, semantic-search OK (the surfaces this diff touches).
  ai-widgets, compose-components, daily-briefing-components, legal-workspace-app, reporting FAILED on `no-unnecessary-condition` in files this diff does not touch
  (`PinnedMemoryEditDialog.tsx`, `AISummaryDialog.tsx`, `ReportDropdown.tsx`, ...): environmental (those surfaces have no node_modules in this worktree, so type information is incomplete); not ours.
- Console (`npm run build` in `src/solutions/SpaarkeAi`): exit 0, `dist/spaarkeai.html` built, contains the new SprkModal code. (`tsc-surface-gate`: 439 pre-existing shared-lib errors deferred, surface-owned 0.)

## PCF inventory

Method: `build:prod` via `scripts/Invoke-PcfBuildProd.ps1`, then grep the bundle for the SprkModal literal `sprk-modal-title-` (judged by output, not exit code). PCFs without `node_modules` here
were judged by walking the compiled `dist/` import graph of what they import from `@spaarke/ui-components` (a superset: it ignores tree-shaking), and the two it flagged were installed
(`--legacy-peer-deps`, lock files reverted) and built.

| PCF | Bundle has SprkModal | Old -> new |
|---|---|---|
| CommunicationActions | yes (built) | 1.3.25 -> 1.3.26 |
| CommunicationConversationPanel | yes (built) | 1.18.6 -> 1.18.7 |
| SemanticSearchControl | yes (built) | 1.1.92 -> 1.1.93 (footer date 2026-11-23) |
| TrackingFieldTrio | yes (built) | 1.0.67 -> 1.0.68 (footer date 2026-11-23) |
| RelatedDocumentCount | yes (built; FindSimilarViewerDialog) | 1.21.7 -> 1.21.8 |
| CommunicationAttachments | yes (built; RichFilePreviewDialog) | 1.3.4 -> 1.3.5 |
| CommunicationTimeline, CommunicationTimelineRegarding, RecordHeader, RegardingResolver | no (built, tree-shaken) | unchanged |
| MatterHeader, DocumentRelationshipViewer, ScopeConfigEditor, VisualHost, CommunicationConnections, CommunicationMessageActions, UpdateRelatedButton | no (import graph; MessageActions pulls only RecipientField/BodyEditor/AttachmentList/InternalOnlySwitch/validateLocalAttachmentFile, none reach SprkModal) | unchanged |
| the rest (no `@spaarke/ui-components` import) | no | unchanged |

All 5 version locations bumped per the `pcf-deploy` table (Input manifest, UI footer / `CONTROL_VERSION`, `solution.xml`, solution `ControlManifest.xml`, `pack.ps1`; ConversationPanel's `pack.ps1` reads `solution.xml`).
Not touched: `RelatedDocumentCount/Solution/Controls/.../bundle.js` is a tracked build artifact copied in by the deploy step.

## Code pages that need redeploying after merge (no deploy here)

The Console (`sprk_spaarkeai`, which ships LegalWorkspace) redeploys from master by `deploy-spaarke-ai.yml` (it watches `Spaarke.UI.Components/**`). Other code pages whose bundles include SprkModal
(direct import of a modal / wizard / composer component in their source, or through a shared wizard): LegalWorkspace (standalone), WorkspaceLayoutWizard, SummarizeFilesWizard,
CreateMatterWizard, CreateProjectWizard, CreateTodoWizard, CreateWorkAssignmentWizard, CreateEventWizard, CreateInvoiceWizard, CreateReportCardWizard, DocumentUploadWizard, SmartTodo,
SpeAdminApp, RuleReview, RecordProcedures, InvoiceReview, FindSimilarCodePage, AllDocuments, EmailPage, CommunicationReconciliation, sprk_communicationconversationpage. Judged by source imports,
not by building each page; a page whose bundle tree-shakes SprkModal away needs nothing.

rework: none task 202
