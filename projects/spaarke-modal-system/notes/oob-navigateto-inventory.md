# OOB `navigateTo` Call-Site Inventory — Task 090 (P7, FR-11/FR-18)

> Repo-wide inventory of every `Xrm.Navigation.navigateTo` call site, produced by task 090 (`oobModalSizes.ts` + hub repoint). Consumed by tasks 091 (retire `navigation.ts`), 092 (convert `sprk_DocumentOperations.js`), and the one-time P7 visual review.
>
> **Method**: precise repo-wide `\.navigateTo\(` grep across `src/` + `infrastructure/` (confirmed complete — a second offset pass returned zero further matches), cross-checked against a `.call(` invocation pattern that bypasses that regex (discovered via `toolbarLaunchDefaults.ts`/`useRecordHeaderToolbarActions.ts`). **89 real call sites** total across ~40 files (within the POML's ~85–100 estimate). Comment/JSDoc/type-only references to `navigateTo` (documentation strings, interface signatures, commented-out code) are excluded from all counts below — they are not launch sites.

## Classification legend

| Classification | Meaning |
|---|---|
| **hub** | Lives inside one of the two sanctioned hubs (`xrmNavigationServiceAdapter.ts`, `wizardLaunchers.ts`) |
| **repointed** | Bypass site fixed by task 090 — now sources width/height from `OOB_MODAL_SIZES` (import) or, where no bundler exists, a literal kept in sync with a comment |
| **assigned-091** | `navigation.ts` (LegalWorkspace/SmartTodo) — excluded from task 090 scope per dispatch instructions |
| **assigned-092** | `sprk_DocumentOperations.js` (both copies) — excluded from task 090 scope per dispatch instructions |
| **intentionally-left** | Full-page navigation (no `target: 2`, or no navigation-options argument at all) — not a modal, no size to standardize |
| **flagged-for-visual-review** | Size doesn't fit the 3-name scale (or the collapse is a judgment call) — left unchanged, surfaced for the owner |

---

## 1. Hub definitions (the two sanctioned hubs themselves)

| File | Function | Named size | Notes |
|---|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/utils/adapters/xrmNavigationServiceAdapter.ts` | `openRecordModal` | `record` (85%×85%) | **Repointed** — was an inline `{value:85,...}` literal; now `OOB_MODAL_SIZES.record.width/height`. |
| same file | `openDialog` | caller-supplied | Pass-through only — never hardcoded a percentage; no change needed. |
| `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts` | `fireNavigateTo` (via `DEFAULT_WIDTH`/`DEFAULT_HEIGHT`) | `wizard` (60%×70%) | **Repointed** — constants now source from `OOB_MODAL_SIZES.wizard`. |
| same file | `navigateToWebResourceSurfaceAsync` (via same `DEFAULT_WIDTH`/`DEFAULT_HEIGHT`) | `wizard` (60%×70%) | **Repointed** (shared constants with the row above). |
| same file | `navigateToEntityRecordSurfaceAsync` | `createForm` (70%×80%) — **was `wizard` 60%×70%** | **Repointed + bucket reassignment.** This launches an OOB entity **CREATE** form (`pageType:'entityrecord'`, no existing id) — exactly what `createForm` was named for (spec FR-11). Verified via repo-wide grep: **exactly one caller** — `services/surfaceHandoff/launchSurface.ts` (the Assistant "create-task" hand-off, e.g. creating a To Do). No other consumer. Deliberate, narrow, low-blast-radius change — see task-090-completion.md §Deviations. |

## 2. Repointed bypass sites — `record` bucket (85%×85%)

Entity-record-existing opens (or webresources proxying one) that were hardcoding 70–80% instead of the invariant 85%×85% (`.claude/patterns/ui/record-modal-selection.md`).

| # | File:Line | Before | After | Mechanism |
|---|---|---|---|---|
| 1 | `src/client/pcf/VisualHost/control/components/DueDateCardList.tsx:209` | 80%×80% | 85%×85% | Relative-source import (mirrors this file's existing `Spaarke.Visuals` import convention) |
| 2 | `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverApp.tsx:~1348` (`handleRecordNumberClick`) | 80%×80% | 85%×85% | Main-barrel import (file already imports named values from `@spaarke/ui-components`); test mock updated |
| 3 | `src/client/pcf/CommunicationConnections/CommunicationConnections/CommunicationConnectionsApp.tsx:~829` (record-view branch) | 80%×80% | 85%×85% | Main-barrel import (ditto); test mock updated |
| 4 | `src/client/shared/Spaarke.Events.Components/.../CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx:1063` | 80%×80% | 85%×85% | Relative-source import to `Spaarke.UI.Components/src` (this file's established cross-package convention; package.json has no formal npm dependency) |
| 5 | `src/client/shared/Spaarke.DailyBriefing.Components/.../SubRowLink.tsx:108` | 80%×80% | 85%×85% | Main-barrel import; test + package test-mock updated |
| 6 | `src/client/shared/Spaarke.DailyBriefing.Components/.../NarrativeBullet.tsx:409` | 80%×80% | 85%×85% | Main-barrel import; test + package test-mock updated |
| 7 | `src/client/shared/Spaarke.DailyBriefing.Components/.../DailyBriefingApp.tsx:448` (`openTodo`) | 80%×80% | 85%×85% | Main-barrel import (file already imports from it) |
| 8 | `src/client/shared/Spaarke.DailyBriefing.Components/.../DailyBriefingApp.tsx:522` (`handleOpenRecord`) | 80%×80% | 85%×85% | Same |
| 9 | `src/solutions/EventDetailSidePane/src/services/sidePaneService.ts` (`openEventRecord`) | 80%×80% | 85%×85% | New import added (file had zero prior imports) |
| 10 | `src/client/shared/Spaarke.UI.Components/.../EmailComposer/openEmailRecord.ts` | 80%×80% | 85%×85% | Same-package import; test file updated |
| 11 | `src/client/code-pages/SemanticSearch/src/components/EntityRecordDialog.ts` | 70%×80% | 85%×85% | **Deep import** `@spaarke/ui-components/utils/adapters/oobModalSizes` (avoids the main barrel's `d3-force`/ESM dep — see §5); jest.config.js gained a matching narrow `moduleNameMapper`; test updated |
| 12 | `src/client/webresources/js/sprk_regardingrecordnumber_hyperlink.js` | 80%×80% | 85%×85% | **Literal** (plain Dataverse web resource, no bundler) + cross-reference comment |
| 13 | `src/client/webresources/js/sprk_event_sidepane_form.js` | 80%×80% | 85%×85% | Same |
| 14 | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/NavigationService.ts:588` (`navigateToRecord`, Modal branch **default**) | 80%×80% default | 85%×85% default | Deep-dist import `@spaarke/ui-components/dist/utils/adapters/oobModalSizes`; caller-supplied `modalOptions` override still honored — only the implicit default moved |

**Value-neutral repoints** (already 85%×85% independently — import added purely to kill the duplicate literal, per ADR-012):

| File | What | Notes |
|---|---|---|
| `Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx` (`LAYOUT_1_NAV_OPTIONS`) | DataGrid framework's `defaultRecordOpen` | Was its own independent frozen 85×85 literal; now sources from `OOB_MODAL_SIZES.record` |
| `Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts` (`LAYOUT_1_MODAL`) | Feeds `useRecordHeaderToolbarActions.ts`'s `handleCheckmarkClick` (opens SmartTodo code page) | **Discovered via a `.call()` invocation that bypasses the `\.navigateTo\(` regex** — see §6. Was its own independent 85×85 literal; now sources from `OOB_MODAL_SIZES.record`. Test (`toolbarLaunchDefaults.test.ts`) confirmed still passing. |
| `src/solutions/LegalWorkspace/src/sections/todo.registration.ts` (`handleOpenTodo`) | entityrecord `sprk_todo` | Already 85×85; import added |
| `src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDo.tsx` (`handleOpenSmartTodo`) | webresource `sprk_smarttodo` | Already 85×85; import added |
| `src/solutions/SmartTodo/src/SmartTodoApp.tsx` (`openSprkTodoAsLayout1`) | entityrecord `sprk_todo` | Already 85×85; import added |
| `src/solutions/FindSimilarCodePage/src/App.tsx` (`handleFindSimilar`) | webresource `sprk_documentrelationshipviewer` | Already 85×85; import added |

## 3. Repointed bypass sites — `createForm` bucket (70%×80%)

| File:Line | Before | After | Notes |
|---|---|---|---|
| `src/client/pcf/CommunicationConnections/.../CommunicationConnectionsApp.tsx:723` (`handleCreateType`) | 60%×80% | 70%×80% | entityrecord, no entityId → genuine CREATE-form scenario; matches `createForm`'s definition exactly |
| `Spaarke.UI.Components/.../EmailComposer/openEmailCompose.ts` | 60%×80% | 70%×80% | Composing = creating a new email; test file updated |
| `wizardLaunchers.ts`'s `navigateToEntityRecordSurfaceAsync` | 60%×70% (`wizard`) | 70%×80% (`createForm`) | See §1 — bucket reassignment, single verified caller |

## 4. Repointed bypass sites — `wizard` bucket (60%×70%), value-neutral (already correct)

All of the below already used 60%×70% independently; each got an import (or, for ribbon JS, a cross-reference comment) so the value can no longer drift.

| File | Call sites | Mechanism |
|---|---|---|
| `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx` | **11** — `handleOpenWizard`, `handleOpenProjectWizard`, `handleOpenSummarize`, `handleOpenFindSimilar`, `handleOpenEventWizard`, `handleOpenTodoWizard`, `handleOpenWorkAssignmentWizard`, `handleAddDocument`, `handleOpenWizardGeneric` (default only — caller override preserved), `handleEditLayout`, `handleCreateLayout` | Main-barrel import added. FR-25/NFR-10 precedent (this file's own local handlers, not `wizardLaunchers.ts`) is preserved — only the SIZE SOURCE changed, not the file's architecture/handler ownership. |
| `src/solutions/SpaarkeAi/src/components/workspace/ManageWorkspacesPane.tsx` | 2 — `launchEditWizard`, `launchCreateWizard` | Main-barrel import added |
| `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx:428` (`handleCreateClick`) | 1 | Relative-source import (mirrors this file's `AiSummaryPopover` import convention) |
| `src/client/pcf/SemanticSearchControl/.../NavigationService.ts:407` (`openAddDocument`) | 1 | Deep-dist import |
| `Spaarke.AI.Widgets/.../MeetingScheduleWidget.tsx`, `FindSimilarWizardWidget.tsx`, `EmailComposeWidget.tsx`, `CreateProjectWizardWidget.tsx` | 4 (1 each) | Main-barrel import added for `OOB_MODAL_SIZES`. **Also**: each of these 4 files independently duplicated the identical `resolveXrmNavigation()` frame-walking helper already exported by `wizardLaunchers.ts` — replaced all 4 local copies with the shared import (ADR-012 dedup, verified functionally identical, zero behavior change; full build + suite green) |
| `src/client/webresources/js/sprk_wizard_commands.js` (`DIALOG_OPTIONS`/`SMALL_DIALOG_OPTIONS`, feeds ~7 ribbon-button handlers via `openWizardDialog`) | 1 source call site, multiple ribbon-button callers | Literal (ribbon JS, no bundler) + cross-reference comment |
| `src/client/webresources/js/sprk_analysis_commands.js:428` (playbooklibrary) | 1 | Same |
| `src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js:376,509` | 2 | Same |

## 5. Architectural note — ribbon JS (no module system)

Files under `src/client/webresources/js/` and `infrastructure/dataverse/ribbon/**/WebResources/` are plain, unbundled Dataverse web resources — hand-authored vanilla JS with **no build step, no module system**. They cannot `import` `oobModalSizes.ts`. For these, "repoint" means: (a) fix the literal value where it diverges from the canonical scale, (b) add a cross-reference comment naming the canonical size, so a future reader knows the literal is deliberately mirroring `oobModalSizes.ts` rather than an independent, unreviewed choice. This is the best achievable single-sourcing for this file class; true compiler-enforced sharing would require introducing a build step for ribbon JS, which is out of this task's scope (a legitimate follow-on if ever prioritized).

## 6. Discovered gap — `.call()` invocation bypasses the `\.navigateTo\(` regex

`useRecordHeaderToolbarActions.ts`'s `handleCheckmarkClick`/`handleAnnotationClick` invoke `(xrm.Navigation.navigateTo as unknown as XrmNavigateToTwoArg).call(xrm.Navigation, {...}, LAYOUT_1_MODAL)` — preserving `this`-binding per an in-file v1.0.6 comment (destructuring/aliasing `navigateTo` silently no-ops it). This invocation shape does **not** match `\.navigateTo\(` (no `(` immediately follows `navigateTo`), so it was invisible to the initial inventory grep. Found by tracing `toolbarLaunchDefaults.ts`'s `LAYOUT_1_MODAL`/`NOTEPAD_MODAL` consumers. A repo-wide follow-up grep for `navigateTo as unknown|navigateTo\)\.call\(` confirmed this is the **only** file using this pattern — no other hidden call sites exist.

- `handleCheckmarkClick` → `LAYOUT_1_MODAL` (85%×85%) → **repointed** (§2 value-neutral table).
- `handleAnnotationClick` → `NOTEPAD_MODAL` (25%×35%) → **NOT touched** — see §7 (already-reviewed exception).

## 7. Flagged for the one-time P7 visual review (NOT edited — current behavior preserved)

Per the escalation instructions: sizes that genuinely don't fit `record`/`createForm`/`wizard` are recorded here with their current value + reason, left unchanged, for the owner's one-time P7 review.

| # | Site(s) | Current size | Reason not collapsed |
|---|---|---|---|
| A | `src/client/webresources/js/sprk_playbook_commands.js:177` (Playbook Builder, `sprk_playbookbuilder`) | 95%×95% | In-file comment: "near-full-screen experience" for an authoring surface — genuinely outside the 3-size scale, not a drift artifact. |
| B | `VisualHostRoot.tsx:684,706,729` (3 "drill-through" dialogs — webresource + entitylist, chart drill-through) | 90%×85% | 3 internally-consistent occurrences (not random drift) for viewing a filtered/larger dataset after a chart drill-through — plausibly needs more width than `record`. Height already matches `record` exactly; only width (90 vs 85) differs. |
| C | **"SpaarkeAi-as-modal" pattern** (4 independent occurrences, all `sprk_spaarkeai` webresource): `sprk_analysis_commands.js:193`, `launch-resolver.ts:338` (`openSpaarkeAi`), `launch-resolver.ts:411` (`openSpaarkeAiCompose`), `PlaybookLibrary/main.tsx:247` (`handleComplete`) | 80%×80% (all 4, identical) | Four independent solutions converge on the exact same non-standard value for "open the full SpaarkeAi app as a modal" — a substantial standalone app, not a form; candidate `createForm` (height already matches) but the width delta (80→70) and the app's rich-surface nature make this a judgment call, not an auto-collapse. `launch-resolver.test.ts` already asserts 80×80 as "the shipped value" (unchanged, still passes). |
| D | `NavigationService.ts:488` (`openSemanticSearchPage`) | 80%×80% (parameterized default; caller-overridable) | Search-results page is a rich standalone surface (same category as C), not a form; default left as-is pending owner input. |
| E | `useDailyDigestAutoPopup.ts:181` (Daily Digest auto-popup, `sprk_dailyupdate`) | 60%×80% | Doesn't cleanly match `wizard` (60×70, height off by 10) or `createForm` (70×80, width off by 10) — an idiosyncratic value, not obviously a `record`/`createForm`/`wizard` case. Not an entity-create flow (informational digest), so the create-form auto-collapse rule doesn't apply. |

## 8. Intentionally-left (full-page navigation — not a modal, no size to standardize)

| Site(s) | Why |
|---|---|
| `sprk_emailactions.js` (both copies — `src/client/webresources/js/` + `infrastructure/dataverse/ribbon/EmailRibbons/WebResources/`), the "navigate to the created document" call | `pageType:'entityrecord'`, **no `target`/`width`/`height` at all** — a full-page redirect after a confirm dialog, not a dialog launch. |
| `NavigationService.ts:542` (`viewAllResults`) | `pageType:'custom'`, `{target:1}` only — explicit new-window/inline nav. |
| `VisualHostRoot.tsx:687,714,737` | The `{target:1}` catch-fallback branches paired with the 3 flagged 90%×85% dialog attempts (§7-B) — confirmed full-page/inline nav fallbacks, not dialogs. |
| `ClickActionHandler.ts:130` (`navigateToPage`) | Called with **only** `pageInput` — no second (options) argument at all; platform default applies (not a sized dialog). |
| `LookupField.tsx:237` (`handleClick`) | Same — single-argument call, no dialog requested. |

## 9. Assigned to 091 (untouched — file off-limits per task 090 scope boundary)

| File | Real calls |
|---|---|
| `src/solutions/LegalWorkspace/src/utils/navigation.ts` | `openRecordDialog` (entityrecord, **80%×80%**) + `navigateToEntityList` (entitylist, **80%×80%**) |
| `src/solutions/SmartTodo/src/utils/navigation.ts` | Byte-identical duplicate of the above (confirmed via diff) |

`WorkspaceGrid.tsx` imports `navigateToEntityList` from the LegalWorkspace copy (used by `handleOpenDocumentsDialog`) — a **consumer**, not a second implementation; it will migrate naturally when 091 retires the module. Not separately fixed by 090.

## 10. Assigned to 092 (untouched — file off-limits per task 090 scope boundary)

| File | Real calls |
|---|---|
| `src/client/webresources/js/sprk_DocumentOperations.js` | 8 raw calls — 7× `pageType:'entityrecord'` fallback (no target/size) + 1× `pageType:'entitylist'` fallback (no target/size). **All are already full-page navigations, not modals** — even once 092 converts the DOM-overlay concern, these specific navigateTo calls have no size to standardize. |
| `infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/sprk_DocumentOperations.js` | Byte-identical duplicate (confirmed via diff — same 8 calls, same line offsets) |

## 11. Discovered but out-of-scope: a second undocumented ribbon-file duplication

`sprk_emailactions.js` exists in **two** byte-identical copies — `src/client/webresources/js/sprk_emailactions.js` and `infrastructure/dataverse/ribbon/EmailRibbons/WebResources/sprk_emailactions.js` — mirroring the already-known `sprk_DocumentOperations.js` duplication pattern (documented in the project `CLAUDE.md`), but **not previously listed there**. Both copies were edited identically by task 090 (the `navigateToLoginRequestUrl` MSAL config key match is a false positive substring, unrelated). Not this task's job to deduplicate the ribbon-file copies themselves (out of scope — flagging for wrap-up / `defer-issues.md`).

## 12. Totals

| Classification | Count |
|---|---|
| Hub definitions | 5 (2 in `xrmNavigationServiceAdapter.ts`, 3 in `wizardLaunchers.ts`) |
| Repointed — `record` (value-fixed) | 14 |
| Repointed — `record` (value-neutral dedup) | 6 |
| Repointed — `createForm` (value-fixed / reassigned) | 3 |
| Repointed — `wizard` (value-neutral, incl. 4× resolveXrmNavigation dedup) | 19 |
| Repointed — ribbon JS (`wizard`, literal, comment-only) | 4 source call sites (serving ~11 ribbon-button handlers) |
| **Flagged for visual review** | 10 (across 5 named groups A–E) |
| **Intentionally-left** (page nav / no options arg) | 8 |
| **Assigned-091** | 4 (2 files × 2 calls) |
| **Assigned-092** | 16 (2 files × 8 calls) |
| **Total real call sites inventoried** | **89** |

Within the POML's ~85–100 estimate.
