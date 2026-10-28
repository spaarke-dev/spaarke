# Task 075 notes - Push Updates ribbon opens the Family view (FR-35)

Rigor FULL (new code page + ribbon web-resource change). Branch `task/fm-075` from 30204a7c98. Gate 073 (RecordFamilyModal, PR #2366) is on master.

## What changed

| File | Change |
|---|---|
| `src/client/webresources/js/sprk_fieldmapping_push.js` | `pushUpdates` rewritten (v1.2.0): `Xrm.Navigation.navigateTo({ pageType: "webresource", webresourceName: "sprk_recordfamily", data: "entityName=<logical>&id=<bare id>" }, { target: 2, 70% x 80%, title: "Family" })`, then `primaryControl.data.refresh()` on close (resolved or rejected). No confirm dialog, no fetch. `hasSourceProfile` untouched. |
| `src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_fieldmapping_push` | Copied from the source. `WebResourceMirrorParityGuardTests` (#1651) requires the committed SpaarkeMaster copy to equal its maintained source and its baseline is empty ("never add a line"), so the mirror had to move with the source. 28/28 parity tests pass. |
| `src/solutions/RecordFamily/` (new) | Minimal code page: `src/main.tsx`, `index.html`, `vite.config.ts`, `tsconfig*.json`, `package.json`, `package-lock.json`. Renders `RecordFamilyModal` with `open`, `table=entityName`, `recordId=id`, `authenticatedFetch`, `onClose=closeDialog`, and NO `onAction` (both actions render disabled, "Available after the push update"; task 040 wires them). |
| `src/client/shared/Spaarke.UI.Components/src/__tests__/fieldMappingPush.opensFamily.test.ts` (new) | 10 tests, `vm` sandbox with a recording fake `Xrm`/`fetch` (same technique as `secureChildRibbonScript.test.ts`). |

## Reference page

`src/solutions/CreateReportCardWizard/` - the smallest existing code page (46-line `main.tsx`) with the standard auth/bootstrap: `useWizardPageBootstrap` (`@spaarke/auth` `resolveRuntimeConfig` -> `initAuth` -> `authenticatedFetch`, theme + `setupCodePageThemeListener`, `closeDialog` that clicks the platform X). Its `vite.config.ts` (singlefile + shared-lib aliases), `index.html` (with the box-sizing reset the css-reset gate requires) and tsconfigs were copied unchanged except the title. The hook is wizard-named but is the shared page bootstrap for every `navigateTo` dialog page; the unused services it builds (upload/data/navigation adapters) are cheap constructors.

Dialog data is a querystring envelope (`entityName=...&id=...`), parsed by the shared `parseDataParams` (both the Xrm `data` envelope and raw params work). The page shows a fixed error bar if either key is missing.

## ensureNavigatorSidePane - does it apply to this page?

No, deliberately not called. SPAARKE-SIDE-PANE-NAVIGATION.md section 3(b) makes it a standard step for code pages that are a *session start surface* (SpaarkeAi, EmailPage, CommunicationReconciliation, LegalWorkspace, EventsPage). This page is a modal dialog opened with `navigateTo` target 2 from a Matter/Project form (which already carry the Navigator ribbon trigger, and where the pane persists app-wide for the session). No dialog-only wizard page (`Create*Wizard`, `SummarizeFilesWizard`, `FindSimilarCodePage`) calls it either, and a dialog cannot be a fresh-session start. Calling it from a dialog iframe would only add a pane-registration attempt from inside a modal. Suggest the doc's "every new code page" sentence be qualified to "every non-dialog code page" (doc edit left to the owner; out of scope here).

## What was kept of the old push path (owner decision pending on 075 vs 040 order)

`pushUpdates` no longer calls the old flow. These stay in `sprk_fieldmapping_push.js`, unreferenced, under the banner "LEGACY direct-push helpers - NOT called from pushUpdates (task 075). Kept until task 040 ... delete with 040":

- `executePushForAllTargets`, `resolveTargetEntitiesForSource`, `pushSequentially`, `pushOne`, `normalizePushResponse`, `presentAggregateOutcome`, `resolveEntityNameForRecordTypeRef`
- the MSAL / BFF plumbing: `resolveMsalConfig`, `loadMsalLibrary`, `initMsal`, `getAuthTokenForBff`, `ssoAndPopupFallback`, `resolveBffBaseUrl`, `getEnvironmentVariable`, and helpers `extractChildCountFromDetail`, `pickNumber`, `escapeODataString`, `safeSessionGet/Set`
- constants `PUSH_ENDPOINT_PATH`, `OVER_LIMIT_TITLE`, `BFF_*`
- `TARGETS_CACHE_KEY_PREFIX` is NOT purely dead: `queryHasSourceProfile` (the enable rule, live) still references it to WRITE the targets cache that only the legacy push read. That write is the legacy part; task 040 deletes the write together with the constant (the code banner and the constant's comment say the same).
- the header's legacy-flow description (relabelled "Legacy direct-push flow (NOT called; kept until task 040)")
- Live, NOT legacy (used by the enable rule `hasSourceProfile`): `queryHasSourceProfile`, `resolveRecordTypeRefForEntity`, `escapeODataString`, `safeSessionGet`, `safeSessionSet`, the caches. They sit below the banner but are named as exceptions in it.

**Deviation pending the owner decision:** the POML constraint "the old confirm dialog path is removed" is met for *behaviour* (no `openConfirmDialog`, no `/field-mappings/push` call from `pushUpdates`, test-pinned) but not for *code*: the dead helpers remain by instruction. Deleting them is a pure removal (about two thirds of the 1,090-line file) to do with task 040, or at 075 deploy if 040 is already in.

Consequence to flag: until the family endpoint (072) and the modal's Push action (040) are deployed, the Push Updates button opens a Family view that shows its fixed error state and cannot push. Merge/deploy 075 only with (or after) 072, and decide whether to hold it until 040.

## Checks (task Step 9)

- grep: `pushUpdates` body (from `ns.pushUpdates = function` to the LEGACY banner) contains no `field-mappings/push`, `PUSH_ENDPOINT_PATH`, `fetch(`, `openConfirmDialog` or `executePushForAllTargets` - asserted by a test, and the only `/field-mappings/push` strings left in the file are in the header docs, the legacy constant `PUSH_ENDPOINT_PATH` and the legacy `pushOne`.
- Enable rule `hasSourceProfile`: not edited (diff touches only the header, constants, `pushUpdates` and the legacy banner). RibbonDiff XML unchanged (same command and library).
- New tests: 10 pass; against the HEAD version of the script 6 fail (opens dialog x2, pushes nothing, refresh x2, source scan) and 4 pass (guards that were already true), so the tests do discriminate.
- Code page: `npm install --legacy-peer-deps --no-audit --no-fund` then `npx vite build`: built clean, one file `dist/index.html` 914,041 bytes (914 kB), gzip 256.6 kB, 2,346 modules.
- `tsc --noEmit` on the page with a scratch `paths` tsconfig (not committed; the wizard pages have none either and only `vite build`): zero errors in `src/main.tsx`; all other errors are shared-library files that cannot resolve `react`/`@fluentui` because they are outside this package's `node_modules` (same for every wizard page).
- `node scripts/check-html-css-reset.mjs --all`: 35 hosts clean (RecordFamily included).
- `WebResourceMirrorParity` arch tests: 28/28.
- Client-lint ratchet (`install` then `check`): exit 0, all 10 packages ok (ui-components, ai-widgets, compose, communication, document-operations, daily-briefing, spaarkeai, legal-workspace-app, reporting, semantic-search); no baseline change.
- Web-resource tests: no runner exists; the new vm-sandbox jest suite is the test. Neighbouring suites `secureChildRibbonScript` and the `accessRibbon.*` suites still pass (see report).

## Deploy hand-off for task 076

- New web resource: name `sprk_recordfamily`, type Webpage HTML, file `src/solutions/RecordFamily/dist/index.html` (build with `npm run build`). It is not in `scripts/Deploy-WizardCodePages.ps1` (left untouched: deployment is 076); add an entry there or deploy with the code-page-deploy flow. NB the code-page-deploy skill's path map describes the older `src/client/code-pages` webpack layout; the wizard-style `src/solutions/*` Vite pages are what `Deploy-WizardCodePages.ps1` ships.
- Deploy `sprk_fieldmapping_push` v1.2.0 together with it (the new `pushUpdates` opens `sprk_recordfamily`; deploying only the script gives a "page not found" dialog).
- SpaarkeMaster: the mirror of the script is updated; `sprk_recordfamily` is NOT yet in the solution (`Solution.xml` / `WebResources/`) - the next dev export after deploy adds it.
- `RecordFamily` is not in the client-lint `PACKAGES` list (like the other `src/solutions/*` wizard pages), so the ratchet does not lint it.

## Review (Step 9.5, own diff)

Own-diff review (code-review checklist; nothing auth/security-sensitive was added - the page reuses `@spaarke/auth`, ADR-028):

- F-class findings: none open. One found and fixed while reviewing: the legacy banner sat above four helpers the enable rule still uses, which would have read as "safe to delete" - the banner now names them as exceptions (and the mirror was re-copied).
- K-class / observations (not fixed, listed):
  1. Until 072 and 040 ship, the button opens a Family view in its error state with disabled actions (expected; flagged above).
  2. `entityName` in the dialog data is unvalidated on the page, like every other wizard envelope; the server (072) is the authority and answers 404 for anything unknown (the modal shows its single fixed error). No injection surface: the value only goes into a URL path segment the modal already encodes.
  3. The `ensureNavigatorSidePane` doc sentence "every new code page" should say "non-dialog".
  4. `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` row for `sprk_fieldmapping_push.js` updated to describe the new behaviour (one table cell); the doc's wider push narrative is task 040's to rewrite.
  5. `.claude/skills/code-page-deploy/SKILL.md` documents the old `src/client/code-pages` webpack layout; the `src/solutions/*` Vite wizard pages deploy via `scripts/Deploy-WizardCodePages.ps1`.

## Fix round 1 (independent review of e3791a1b69; owner: 075 merges and deploys only together with 040)

| Finding | Fix |
|---|---|
| F1 modal inside a modal | `RecordFamilyModal` gains `embedded` (default false): no `BrowseModal`/Dialog, no title, backdrop, maximize or header navigator; renders the toolbar + table (same `renderBody`) + a footer with "N records shown" and Close (calls `onClose`), filling its host like `WizardShell`'s `embeddedRoot`; returns null (and fetches nothing) while closed. The default path is byte-for-byte the previous `BrowseModal` return, so the TrackingFieldTrio PCF (074, which passes no `embedded`) is unchanged. `main.tsx` passes `embedded` and `onClose={closeDialog}`. |
| F3 enable-rule test proved nothing | Replaced by 7 behaviour tests on the real script: miss + profile -> promise resolving true, cache written, two-step lookup with `statecode eq 0` and the source recordtype id; miss + no profile -> false cached; no recordtype-ref -> false, no profile query; failed query -> false, NOT cached; hit -> synchronous boolean, no query; per-form keying; no context/entity -> false. Notes + banner corrected for `TARGETS_CACHE_KEY_PREFIX` (above). |
| K1 unsaved form | `pushUpdates` saves a dirty form first (`entity.getIsDirty()` then `data.save()`), opens the dialog only after the save; a failed save shows `Xrm.Navigation.openErrorDialog` (the platform error message, or a fixed one) and opens nothing. 5 tests (order save -> navigateTo -> refresh, clean form, no getIsDirty, failed save, failed save without a message). |
| K2 param validation | New pure `src/solutions/RecordFamily/src/familyParams.ts` `readFamilyParams`: table `^[a-z_][a-z0-9_]*$`, id a GUID with balanced braces optional; `main.tsx` shows the existing error bar otherwise. 26 tests. The test caught a real bug in my first regex (`{guid` and `guid}` were accepted); fixed to balanced braces. |
| K3 launch-data contract | Test runs the exact `data` string `pushUpdates` builds through the shared `parseDataParams` (as the Xrm `?data=` envelope and as raw params) and `readFamilyParams`; a renamed key on either side fails. |
| K4 / K5 | Left, as instructed (coordinator writes them into 040's POML). |

Counts: `fieldMappingPush.opensFamily.test.ts` 51 pass (was 10); `RecordFamilyModal.test.tsx` 28 pass (was 22; +5 embedded, +1 non-embedded-unchanged). Fail-before: against the previous script and modal, 7 of these 79 fail (the 3 K1 save tests and the embedded tests; the rest are guards that were already true). Neighbours `secureChildRibbonScript` + `accessRibbon.*`: 162 pass. RecordFamily vite build: 915,310 bytes (gzip 256.9 kB; +1.3 kB). `tsc` with the scratch paths config: no errors in the page's files (one pre-existing unused-variable error in the shared `xrmNavigationServiceAdapter.ts` under this strict config, not mine). The TrackingFieldTrio PCF suite was NOT run: 074 is PR #2383 / `task/fm-074`, not in this branch's base; its only use of the modal is `React.createElement(RecordFamilyModal, {...})` with no `embedded` (read from `origin/task/fm-074`), which takes the unchanged default path.
