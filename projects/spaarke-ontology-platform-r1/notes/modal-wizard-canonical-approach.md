# Modal and wizard hosts: one canonical approach

> **Date**: 2026-10-05 · **Status**: Decision-ready (awaiting the owner's choice in §7)
> **Source of truth**: `origin/master` @ `b4b58a361` (2026-10-05). Every `file:line` below is on master. Paths are relative to the repo root.
> **Input**: Console prototype HANDOFF.md §4.1 @ **`ae1cc9f`** (spaarke-dev/spaarke-prototype, branch `feature/2026-10-spaarke-console`).
> **Owner brief (2026-10-05)**: "We are using two different modals … to the extent possible we want a single canonical approach; it must support all functionality plus dark mode (preferably without the 'white header' that can't be changed — think this is the OOB Dataverse modal)."

---

## 0. The answer in brief

1. **One envelope: `SprkModal`.** `WizardShell` keeps its engine (reducer, dynamic steps, `canAdvance`, `isSkippable`, async finish, success screen, `embedded`). Its non-embedded branch then renders **inside `SprkModal`** instead of its own Fluent `Dialog`. `WizardModal` has zero consumers and is **deleted**. `WizardShell` becomes the wizard preset. This is v4's proposal, confirmed and narrowed.
2. **The white header is not one of our two modals.** It is the **Dataverse dialog chrome**: the title bar plus × drawn by `Xrm.Navigation.navigateTo(..., { target: 2 })` in the *parent* document, outside our iframe. Microsoft supports no way to theme or hide it, and states that dark mode for model-driven apps "isn't supported at this time." Every shipped wizard shows it today. They run as code pages in exactly that dialog, with `WizardShell embedded hideTitle`. The platform header is their only header.
3. **The only supported way to lose the white header is not to open a `navigateTo` dialog.** When the caller is already a Spaarke React surface (Console/SpaarkeAi, LegalWorkspace-in-SpaarkeAi, code pages, PCFs), open the wizard **in-app** in `SprkModal`. Keep `navigateTo` + code page only where no Spaarke React host exists: model-driven ribbon and command-bar scripts. Accept the white header there for now, documented as a platform limitation. No DOM or CSS hacks.
4. **The Console decision wizard** = `WizardShell`, mounted in-app (non-embedded) from the worklist widget, never as a code page. Write it against `WizardShell`'s public step API now. The envelope swap is internal, so nothing gets rebuilt.
5. **ADR-050 needs a Path B amendment** (§6). It names `WizardModal` as the wizard preset, says nothing about `navigateTo` chrome, and the decision-layer docs it points to are stale in four places.

---

## 1. Inventory of every modal, dialog and wizard host

Counts are **non-test files with a JSX mount** on master (grep `<Name` over `src/`, excluding `__tests__`, `*.test.*`, `*.stories.*` and the component's own file).

### 1.1 Spaarke-owned hosts (rendered inside our React tree, so our theme applies)

| Host | Files | Consumers (count: names) | Capabilities | Theming |
|---|---|---|---|---|
| **`SprkModal`** (base shell) | `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/SprkModal.tsx` (241 LOC), `SprkModal.types.ts`, `sizes.ts`, `scaledTheme.ts`, `uiScale.ts`, `ModalScrollArea.tsx` | **12 direct** (plus the 6 presets): `ConversationModal` (PCF CommunicationConversationPanel), `EmailConnectionsReview`, `ReconciliationBrowseShell`, `RelatedToCell`, `ComposeConflictDialog`, `AccessGrantModal`, `CloseProjectDialog`, `SendEmailDialog`, `FindSimilarViewerDialog`, `CreateRecordChooserModal`, `AllDocuments/VersionHistoryModal`, `SpaarkeAi/QuickStartModal` | Fluent `Dialog`/`DialogSurface` envelope; 7 named sizes incl. `wizard` + `full` (`sizes.ts:36-60`); header with optional `‹ N of M ›` nav, ellipsized title, `headerActions`, maximize/restore + × (`SprkModal.tsx:179-220`); footer `footerStart` (Cancel, left) / `footer` (right) (`:230-235`); dismiss `light`/`explicit`/`alert` (`:159-170`); `nonBlocking` + `hidden` for native lookup panes (`SprkModal.types.ts:38-60`); `aria-labelledby` (`:147,177`); React 16/17-safe (`:31-33`) | No `FluentProvider` of its own; it inherits the host's. `--sprk-ui-scale` through the `uiScale` prop + `scaleTheme()` (`scaledTheme.ts:20-32`). Semantic tokens only |
| `ConfirmModal` | `SprkModal/presets/ConfirmModal.tsx` | **7**: `PinnedMemoryDeleteConfirmation`, `ComposeConflictDialog`, `ComposeWorkspace`, `CloseProjectDialog`, `SprkChat`, SpeAdmin `ContainersPage`, SpeAdmin `ContainerItemRecycleBin` | `xs`, `dismiss="alert"`, non-maximizable, busy spinner, danger token class | Inherits |
| `ChoiceModal` | `presets/ChoiceModal.tsx` | **2**: `ChoiceDialog` (legacy facade → 1 consumer, `FileAttachSessionPrompt`), `DocumentUploadWizardDialog` | `xs`, `explicit`, 2–4 rich choices (ADR-023 pattern) | Inherits |
| `FormModal` | `presets/FormModal.tsx` | **8**: external-spa `QuickStartPane`, `WidgetLibraryModal`; `PinnedMemoryEditDialog`; `FieldUpdateReconcileTab`, `TaskReconcileTab`; `ComposeApplyTemplateDialog`, `ComposeSaveNameDialog`; `NewThreadModal` | `sm`/`md`, `explicit`, Cancel + Save, busy | Inherits |
| `PreviewModal` | `presets/PreviewModal.tsx` | **2**: `ReconciliationBrowseShell`, `RichFilePreviewDialog` (which has 9 consumers) | `lg` landscape, `light`, stage + meta grid | Inherits |
| `BrowseModal` | `presets/BrowseModal.tsx` | **1**: `RichFilePreviewDialog` | `PreviewModal` + shell `nav`; `onBeforeNavigate` guard (`BrowseModal.tsx:48,65`) | Inherits |
| **`WizardModal`** | `presets/WizardModal.tsx` (133 LOC) | **0.** Imported only by `presets/__tests__/WizardModal.test.tsx` (re-exported at `SprkModal/index.ts:32`). Confirms HANDOFF §4.1 @ ae1cc9f | Static `steps: string[]` + `active` index; Cancel/Skip/Back/Next(Finish); `wizard` size, `explicit` (`:82-110`). No reducer, no dynamic steps, no `canAdvance`, no async finish, no success screen, no embedded mode. Its stepper is plain `div`s with no `aria-current` (`:111-126`) | Inherits; `uiScale` forwarded |
| **`WizardShell`** | `src/client/shared/Spaarke.UI.Components/src/components/Wizard/WizardShell.tsx` (593 LOC), `wizardShellReducer.ts`, `wizardShellTypes.ts`, `WizardStepper.tsx`, `WizardSuccessScreen.tsx` | **8 direct**: `CreateRecordWizard` (in turn → Create **Matter/Project/Event/Invoice/To Do/Report Card** + Analysis), `WorkAssignmentWizardDialog`, `SummarizeFilesDialog`, `DocumentEmailWizard`, `DocumentUploadWizardDialog`, SpeAdmin `RegisterWizard`, **`WorkspaceLayoutWizard/App.tsx`**, **external-spa `DocumentUploadPage`**. *The last two are not in v4's list of six.* | Own Fluent `Dialog` (`:576-589`); reducer + imperative handle `addDynamicStep`/`removeDynamicStep(canonicalOrder)`/`requestUpdate`/`nextStep` (`:288-325`); `canAdvance`, `isEarlyFinish`, `isSkippable`, per-step `footerActions` (`wizardShellTypes.ts:99-138`); async `onFinish` → error `MessageBar` / success screen, and **closes when it returns nothing** (`WizardShell.tsx:382-399`); `initialStepId`; `embedded` (no envelope, `:556-563`) + `hideTitle`; maximize (non-embedded only); accessible stepper `nav`/`ol`/`aria-current="step"` (`WizardStepper.tsx:217,238-242`). **Escape and backdrop always close it** (`:578-580`, no dismiss gating). **No `uiScale`** (`:86-88`). **No `‹ N of M ›`**. Header/footer *tokens* aligned to `SprkModal` in task 080, but "not a full internal re-base onto `SprkModal`" (`:32-42`) | Inherits the host provider (non-embedded); `embeddedRoot` paints `colorNeutralBackground1` (`:133-140`) |
| `RecordNavigationModalShell` | `components/RecordNavigationModalShell/*` (366 LOC) | **0 live consumers.** Only the barrel export (`components/index.ts:159`). `RichFilePreviewDialog` moved to `PreviewModal`/`BrowseModal` (its header `:3-32`) | Nav chrome + cross-frame dirty-check protocol + its own nested "Discard unsaved changes?" `Dialog` (`:337-358`); no envelope | Inherits |
| **Raw Fluent `<Dialog>`** (no `SprkModal`) | 55 files | SpeAdminApp 17 · SpaarkeAi 7 (incl. `MyAssistantDialog`, `MemoryDialog`, `HistoryOverlay`, `NdaReviewProgressModal`) · Spaarke.UI.Components 6 (incl. `EmailComposer`, `DataGrid/CommandBar`, `ConversationView`) · external-spa 5 · Compose 4 · Reporting 3 · LegalWorkspace 3 · SmartTodo 3 · PCFs 4 (`CommunicationActions`, `CommunicationConnections`, `SemanticSearchControl/BulkActionBar`, `UpdateRelatedButton`) · others | Bespoke, one per site | Inherits the host provider, so it generally follows dark mode, but its sizes and close rules have drifted. ADR-050 MUST NOT says "give a surface its own bespoke modal envelope". This is the pre-existing ADR-050 migration backlog, out of scope here |

No Fluent v8 `Dialog`/`Modal`/`Panel` imports remain. Four Fluent `Drawer` usages exist (side panels, not modals).

### 1.2 Platform-owned hosts (Dataverse draws the chrome, so our theme cannot reach it)

| API | Call sites on master | Chrome |
|---|---|---|
| `navigateTo({pageType:'webresource'}, {target:2})`: **a Spaarke code page in a Dataverse dialog** | ~45 call sites, 19 distinct web resources (full list in §2.3) | Platform title bar (`defaultDialogChromeHeader-N`, `h1#defaultDialogChromeTitle-N`, `button[data-id="dialogCloseIconButton"]`) **plus** the expand-to-full-screen button. **White in dark mode** |
| `navigateTo({pageType:'entityrecord'\|'entitylist'}, {target:2})`: Layout 1 OOB form dialog (85%×85%, `oobModalSizes.ts`) | ~30 call sites (DataGrid `defaultRecordOpen`, `xrmNavigationServiceAdapter.ts:125`, `launchCreate.ts:111`, DailyBriefing, Calendar, CommunicationConnections, TrackingFieldTrio, RegardingResolver, WorkspaceGrid lists …) | Platform form-dialog chrome. The 2025-12 DevTools note observed that *native* MDA dialogs (Quick Create, Lookup) **do** follow the unofficial dark flag. Main-form dialogs were not checked (**unverified**) |
| `navigateTo({pageType:'custom'})` | `SemanticSearchControl/services/NavigationService.ts:526` (target **1**, inline), `VisualHost/control/services/ClickActionHandler.ts:101,131` (side pane) | Inline: none. "Custom pages don't use the modern theme" (MS) |
| `Xrm.App.sidePanes.createPane` | `DataGridSidePaneOrchestrator.ts`, `VisualHost ClickActionHandler.ts`, `sprk_openSprkChatPane.js`; `xrmContext.ts:262` already models `hideHeader` | Side-pane header, which **can be hidden** with `hideHeader: true` (MS doc) |
| `openForm` | 21 files (ribbon JS, `xrmNavigationServiceAdapter`, `RegardingLinkRenderer`, …) | Platform form |
| `openWebResource` | 1 (`EventCommands/sprk_event_ribbon_commands.js`) | New browser window, no app chrome |
| `openAlertDialog` / `openConfirmDialog` / `openErrorDialog` | 14 / 7 / 3 files, all ribbon/command JS (`sprk_DocumentOperations.js`, `sprk_emailactions.js`, `sprk_registrationribbon.js`, `sprk_updaterelated_commands.js`, `EventCommands`, …) | Platform dialog; no theme options documented. Dark behavior **unverified** |

---

## 2. The "white header": root cause, evidence, affected surfaces

### 2.1 Root cause (confirmed)

`Xrm.Navigation.navigateTo(pageInput, { target: 2, … })` makes the model-driven app host draw a **dialog frame in the top document**: a title bar (the `title` option), an expand button and a close ×. Our web resource is loaded in an **iframe below that bar**. Our `FluentProvider` themes only the iframe document, so the bar stays white.

Evidence:

| # | Evidence | Source |
|---|---|---|
| 1 | DevTools capture of the bar: `<div id="defaultDialogChromeHeader-7">`, `<h1 id="defaultDialogChromeTitle-7">`, `<button data-id="dialogCloseIconButton">`; "has a hardcoded white background (#FFFFFF) … via CSS class"; "Setting `title: ""` … only removes the title text; the white header bar remains." | `projects/mda-darkmode-theme/notes/DIALOG-CHROME-LIMITATION.md` (master, 2025-12-07) |
| 2 | Our own code reaches **into the parent document** to click the platform ×, which proves the bar is outside our iframe: `frame?.document?.querySelector('[data-id="dialogCloseIconButton"]')` | `src/solutions/DocumentUploadWizard/src/App.tsx:70-85`, `src/solutions/WorkspaceLayoutWizard/src/App.tsx:780,960`, `src/solutions/FindSimilarCodePage/src/App.tsx:419` |
| 3 | The wizards switch their *own* header off because the platform supplies one: `hideTitle` doc says "Use this when the wizard is hosted inside a Dataverse dialog that already provides its own chrome (title bar + close button via `navigateTo` target: 2)" | `Wizard/wizardShellTypes.ts:235-241`; set as `embedded={embedded} hideTitle={embedded}` at `CreateRecordWizard.tsx:837-841`, `WorkAssignmentWizardDialog.tsx:643-644`, `SummarizeFilesDialog.tsx:738-739`, `DocumentEmailWizard.tsx:893-894`, `DocumentUploadWizardDialog.tsx:708-709`, `WorkspaceLayoutWizard/App.tsx:947-948` |
| 4 | Every wizard code page mounts the wizard `embedded={true}` | `src/solutions/{CreateMatter,CreateProject,CreateEvent,CreateInvoice,CreateReportCard,CreateTodo,CreateWorkAssignment,SummarizeFiles}Wizard/src/main.tsx` (e.g. `CreateMatterWizard/src/main.tsx:130-137`) |
| 5 | The `navigationOptions` object has exactly **five** fields: `target`, `width`, `height`, `position`, `title`. Nothing hides or themes the chrome. `title` = "The dialog title on top of the center or side dialog." | [navigateTo reference](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto) (ms.date 2026-04-09; fetched 2026-10-05) |
| 6 | **"Switching themes or enabling dark mode isn't supported at this time."** Also: "Currently, custom pages don't use the modern theme." | [Modern, refreshed look](https://learn.microsoft.com/en-us/power-apps/user/modern-fluent-design) (updated 2026-09-15; fetched 2026-10-05) |
| 7 | Spaarke's MDA dark mode is the **undocumented** URL flag `flags=themeOption%3Ddarkmode`, written by `applyMdaTheme()` and enforced by the ThemeEnforcer PCF. The platform dialog chrome does not honor it | `Spaarke.UI.Components/src/utils/themeStorage.ts:264-297`; `src/client/pcf/ThemeEnforcer/index.ts:15`; `docs/architecture/ui-dialog-shell-architecture.md:122-130` |
| 8 | Modern theme overrides (`AppHeaderColors`) cover the app header, links and primary buttons, with no dialog and no dark option | [Use modern themes](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/modern-theme-overrides) |
| 9 | DOM access to the host page is unsupported: "Directly accessing the Document Object Model (DOM) of any model-driven apps page isn't supported." | [Supported customizations](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/supported-customizations) |

**Conclusion.** The "two modals" surprise (`WizardShell` vs `WizardModal`) and the white header are **separate problems**. The white header comes from the **launch mechanism** (`navigateTo` dialog), not from either Spaarke shell. `WizardShell` *embedded* is perfectly themeable. It just sits under a bar we don't own. ADR-026 (`.claude/adr/ADR-026-full-page-custom-page-standard.md`, matrix row "Wizard / dialog (standalone) → Standalone HTML") is what put every wizard behind that bar.

### 2.2 Can the chrome be hidden or themed?

| Option | Result | Verdict |
|---|---|---|
| Omit or blank `title` | Text goes; the white bar, expand and × remain (evidence #1) | ✗ |
| Another `navigationOptions` field | None exists (evidence #5) | ✗ |
| App theme / modern theme XML / appearance settings | No dark mode, no dialog surface (evidence #6, #8; [Appearance settings](https://learn.microsoft.com/en-us/power-apps/user/appearance-settings) = density only) | ✗ |
| `position: 2` (side dialog) | Same bar | ✗ |
| `pageType:'custom'` (canvas custom page) | Same chrome, and the custom page body ignores the modern theme | ✗ (worse) |
| CSS/DOM injection into `[id^="defaultDialogChromeHeader"]` from our same-origin iframe | Technically works. Unsupported (evidence #9), selectors are undocumented, timing-dependent. The 2025-12 note sketches it and recommends against | ✗ unless the owner signs a §6.5 Path A exception. **Not recommended** |
| `navigateTo` **`target: 1`** (inline, full page) | No dialog chrome; the page replaces the main area (not modal) | ◐ chrome-free, but not a modal-over-record experience |
| `Xrm.App.sidePanes.createPane({ hideHeader: true })` | "Hides the header pane, including the title and close button" ([createPane](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-app/xrm-app-sidepanes/createpane)). The only documented header switch | ◐ chrome-free, but it is a side pane, not a centered wizard |
| `openWebResource` | New browser window; no `Xrm` | ✗ for wizards |
| **Don't use a platform dialog. Render `SprkModal` in-app** | No platform chrome; full dark mode + `--sprk-ui-scale` | ✓ **only clean answer**, wherever a Spaarke React host exists |

### 2.3 Surfaces that currently show the white header (`navigateTo` web-resource dialogs, `target: 2`)

| Web resource | What the user sees | Opened from (file:line) |
|---|---|---|
| `sprk_creatematterwizard` | **Create New Matter** | `LegalWorkspace/.../Shell/WorkspaceGrid.tsx:312`; `WorkspaceShell/wizardLaunchers.ts:197` (→ `fireNavigateTo` `:159`); ribbon `webresources/js/sprk_wizard_commands.js:196,277` |
| `sprk_createprojectwizard` | Create New Project | `WorkspaceGrid.tsx:343`; `wizardLaunchers.ts:232`; `CreateProjectWizardWidget.tsx:166`; `sprk_wizard_commands.js:202,281` |
| `sprk_createeventwizard` | Create New Event | `WorkspaceGrid.tsx:397`; `wizardLaunchers.ts:219`; `sprk_wizard_commands.js:208,285` |
| `sprk_createtodowizard` | Create New To Do | `WorkspaceGrid.tsx:413`; `sprk_wizard_commands.js:214` |
| `sprk_createworkassignmentwizard` | **Create Work Assignment** / "Assign Work" | `WorkspaceGrid.tsx:429`; `wizardLaunchers.ts:289`; `sprk_wizard_commands.js:289` |
| `sprk_summarizefileswizard` | **Summarize Files** | `WorkspaceGrid.tsx:364`; `wizardLaunchers.ts:245`; `sprk_wizard_commands.js:249` |
| `sprk_documentuploadwizard` | **Upload Documents** | `WorkspaceGrid.tsx:574`; `DocumentUploadWizard/sprk_subgrid_commands.js:377,528`; `SemanticSearchControl/services/NavigationService.ts:408`; `sprk_wizard_commands.js:240` |
| `sprk_findsimilar` | Find Similar Documents | `WorkspaceGrid.tsx:381`; `wizardLaunchers.ts:268`; `FindSimilarWizardWidget.tsx:190`; `sprk_wizard_commands.js:255` |
| `sprk_workspacelayoutwizard` | Workspace layout wizard | `WorkspaceGrid.tsx:850,880`; `WorkspaceLoadingStates.tsx:242`; `SpaarkeAi/.../ManageWorkspacesPane.tsx:481,563` |
| `sprk_playbooklibrary` | Playbook Library / intent launches (email compose, meeting schedule) | `ActionCardHandlers.ts:88`; `wizardLaunchers.ts:305`; `dailyBriefing.registration.ts:212`; `DailyBriefing/src/main.tsx:102`; `SpaarkeAi/.../usePlaybookOptions.ts:170`; `EmailComposeWidget.tsx:179`; `MeetingScheduleWidget.tsx:176`; `sprk_analysis_commands.js:422`; `sprk_playbook_commands.js:168`; `sprk_wizard_commands.js:261,268` |
| `sprk_spaarkeai` | **The Console itself** as a modal; Compose modal; Analysis modal | `SpaarkeAi/src/utils/launch-resolver.ts:340` (default `target=2`), `:413-421` (Compose); `sprk_analysis_commands.js:186`; `PlaybookLibrary/src/main.tsx:254` |
| `sprk_emailpage` | Email compose / email record | `EmailComposer/openEmailCompose.ts:86`, `openEmailRecord.ts:101` |
| `sprk_semanticsearch` | Semantic search | `SemanticSearchControl/services/NavigationService.ts:489`; `SpaarkeAi/.../SemanticSearchCriteriaTool.tsx:388` |
| `sprk_documentrelationshipviewer` | Document relationships | `FindSimilarCodePage/src/App.tsx:381` |
| `sprk_smarttodo` | Smart To Do board | `LegalWorkspace/src/hooks/useSmartToDoBridge.ts:199`; `todo.registration.ts` (85% literal) |
| `sprk_notepad` | Notepad | `hooks/toolbarLaunchDefaults.ts:47` (`NOTEPAD_MODAL`) via `useRecordHeaderToolbarActions.ts:309` (RecordHeader/MatterHeader PCFs) |
| `sprk_dailyupdate` | Daily digest auto-popup | `LegalWorkspace/src/hooks/useDailyDigestAutoPopup.ts:187` |
| VisualHost drill-through page (configured) | Chart drill-through | `VisualHost/control/components/VisualHostRoot.tsx:424,680` |
| SprkChat action target / generic `openDialog` | Chat action pages; `SummarizeAnalysisStep` follow-on | `SprkChat/hooks/useActionHandlers.ts:241`; `xrmNavigationServiceAdapter.ts:147`; `WorkspaceGrid.tsx:620` |

**Platform chrome, dark status unverified**: every Layout 1 `entityrecord`/`entitylist` dialog (§1.2) and the ribbon `openAlertDialog`/`openConfirmDialog`/`openErrorDialog` calls. A 15-minute DevTools check in dev (dark flag on: open one main-form dialog, one alert) closes this.

---

## 3. Dark mode: how each host gets its theme

| Host | Where the theme comes from | Renders correctly in dark? |
|---|---|---|
| `SprkModal` + presets | Host `FluentProvider` (the shell renders none, `SprkModal.tsx:20-21`); portal inherits the provider theme; semantic tokens only; `scaleTheme(base, uiScale)` for `--sprk-ui-scale` | **Yes**, wherever the host resolves dark. Scaled only when the host passes `uiScale` (15 consumers do) |
| `WizardShell` non-embedded | Host `FluentProvider` | **Yes**. **No `uiScale`**, so it does not scale at 2K/4K |
| `WizardShell` embedded in a code page | Code-page wrapper: `resolveCodePageTheme()` (localStorage `spaarke-theme` → URL `themeOption` → navbar luminance → light; `themeStorage.ts:331-352`) + `setupCodePageThemeListener()`; `FluentProvider` per `main.tsx` | **Body: yes. Header: no.** The header is the platform bar (§2). No `uiScale` in any wizard code page. `DocumentUploadWizard` uses a **separate copy** of the cascade (`index.html:16-65` → `data-theme` → `main.tsx:59-61`) with no live listener, so it drifts from the shared resolver |
| `navigateTo` chrome | Dataverse host; no supported dark mode (§2.1 #6) | **No** (white bar, expand, ×) |
| OOB form dialogs / Xrm alert dialogs | Dataverse host + unofficial flag | **Unverified** (§2.3) |
| Custom pages | Canvas; "don't use the modern theme" | **No** |

The theme machinery for in-app hosts is sound. Dark mode fails **only** at the platform chrome boundary.

---

## 4. Functionality matrix

✓ = yes · ◐ = partial / consumer-built · ✗ = no · — = not applicable. "v4" = HANDOFF §4.1 @ ae1cc9f (WizardShell + additions, re-based on SprkModal). "Recommended" = §5.

| Capability | `SprkModal` | `WizardModal` | `WizardShell` (today) | `navigateTo` dialog (+ embedded WizardShell) | v4 proposal | **Recommended** |
|---|---|---|---|---|---|---|
| Dynamic steps add/remove (`canonicalOrder`) | — | ✗ | ✓ | ✓ (content) | ✓ | ✓ |
| `canAdvance` / `isSkippable` / `isEarlyFinish` | — | ✗ (Next always enabled) | ✓ | ✓ | ✓ | ✓ |
| Async finish + error bar + success screen | — | ✗ | ✓ (closes when `onFinish` returns nothing, `:387-392`) | ✓ | ✓ + stay-open-after-finish | ✓ + stay-open |
| `'skipped'` step status (no tick on skipped) | — | ✗ | ✗ (`rebuildStatuses` marks all earlier steps completed, `wizardShellReducer.ts:62-64`) | ✗ | ✓ | ✓ |
| Status bar + read-only footer for decided items | ◐ (slots) | ✗ | ✗ | ✗ | ✓ | ✓ |
| Embedded (no envelope) mode | ✗ | ✗ | ✓ | ✓ (that's how it's used) | ✓ | ✓ |
| `‹ N of M ›` browse in header | ✓ (`nav`) | ✗ | ✗ | ✗ (the platform header can't hold it) | ✓ | ✓ |
| `onBeforeNavigate` guard | ◐ (in `BrowseModal`) | ✗ | ✗ | ✗ | ✓ | ✓ (lift into `SprkModal`) |
| Dismiss: explicit (Esc/backdrop ignored) | ✓ | ✓ | ✗ (Esc + backdrop close, `:578-580`) | platform × only; Esc **unverified** | ✓ | ✓ (default `explicit`) |
| Nested discard confirmation | ◐ (consumer-owned `onClose` + `ConfirmModal`) | ✗ | ◐ (consumer `onClose`) | ✗ (the platform × bypasses us) | ✓ | ✓ |
| Sizes incl. `wizard` + maximize | ✓ | ✓ | ◐ (`wizard` default + ad-hoc `maxWidth`/`height` strings; maximize) | ◐ (OOB % sizes; platform expand) | ✓ | ✓ (named sizes; deprecate raw strings) |
| Sidebar step list (Create New Matter look) | — | ◐ (plain divs) | ✓ (`WizardStepper`) | ✓ | ✓ | ✓ |
| Keyboard / focus trap / ARIA | ✓ (Fluent trap, `aria-labelledby`, a11y test) | ◐ (no `aria-current`) | ✓ (Fluent trap; `aria-label`; stepper `aria-current`) | ◐ (no trap of our own; platform owns the frame) | ✓ | ✓ |
| Dark mode, whole surface | ✓ | ✓ | ✓ | **✗ white header** | ✓ (if in-app) | **✓ (in-app)** |
| `--sprk-ui-scale` | ✓ | ✓ | ✗ | ✗ | ✓ (via envelope) | ✓ |
| Works inside an iframe / code page | ✓ (bounded by its iframe) | ✓ | ✓ | ✓ (it *is* the iframe) | ✓ | ✓ |
| Works across the PCF React 16 boundary | ✓ (`ConversationModal`, `RichFilePreviewDialog` in PCFs) | ✓ | ✓ (`SemanticSearchControl` → `DocumentEmailWizard`) | — | ✓ | ✓ |
| Openable from MDA ribbon / command script | ✗ (no React host) | ✗ | ✗ | **✓ (only option)** | — | navigateTo stays for these |
| Direct tests | ✓ (5 suites + 6 preset suites) | ✓ (1 suite, 0 consumers) | **✗ none.** No `Wizard/__tests__`; covered only indirectly via `CreateRecordWizard.*.test.tsx` and `CreateAnalysisWizardWidget.test.tsx` | — | — | add first (§5.2 P1) |

---

## 5. Recommendation

### 5.1 The canonical approach

1. **`SprkModal` is the only modal envelope.** A Spaarke-rendered `Dialog`/`DialogSurface` exists in exactly one file.
2. **`WizardShell` is the only wizard engine**, and the wizard preset of `SprkModal`:
   - Non-embedded: `WizardShell` renders `<SprkModal size={size ?? 'wizard'} dismiss={dismiss ?? 'explicit'} uiScale={uiScale} nav={nav} footerStart={Cancel + footerLeftExtra} footer={Skip · Back · Next/Finish}>` with `WizardStepper` + content as children. Its own `Dialog`, title bar and `ModalWindowControls` go away; they are `SprkModal`'s.
   - Embedded: unchanged (same inner layout, no envelope). Used for workspace tabs (`CreateMatterWizardWidget`), full pages, and the remaining `navigateTo` code pages.
   - Additive props: `dismiss`, `size`, `uiScale`, `nav` + `onBeforeNavigate`, `statusBar` slot, footer override + `stayOpenOnFinish`, `'skipped'` status. Exactly v4's list.
3. **`WizardModal` is deleted.** Zero consumers; keeping it is the "two wizards in the library" state ADR-050 exists to prevent.
4. **Launch rule:**
   - **(a)** When the caller is a Spaarke React surface (Console/SpaarkeAi widgets, LegalWorkspace sections running in SpaarkeAi, code pages, PCFs with the component in their bundle), open the wizard or modal **in-app**. No platform chrome, so the white header disappears.
   - **(b)** Use `navigateTo(webresource, {target:2})` **only** from MDA ribbon/command scripts and other hostless contexts, or where a separately deployable page is the point. The white header is a **documented platform limitation** there. Do not hack it.
   - **(c)** Layout 1 OOB record dialogs (`entityrecord`) are unchanged: full form fidelity is the point.
5. **No DOM or CSS injection into platform chrome** (unsupported). Revisit only if Microsoft ships dark mode for dialogs.
6. **Optional later (not now):** route form-hosted entry points through the RecordHeader/MatterHeader PCF toolbar (already a React host on record forms, `useRecordHeaderToolbarActions.ts`). That removes the white header for record-form launches too, at a PCF bundle-size cost. It needs its own decision.

Why not the alternatives:
- **Grow `WizardModal` into an engine.** That rebuilds `WizardShell` inside a preset: a second engine, a migration of eight consumers, and still two wizards until the end.
- **Keep both shells.** The status quo, and the owner's complaint.
- **Keep `navigateTo` for everything and accept the white header.** Fails the brief.
- **`sidePanes` with `hideHeader`, or `target:1` for wizards.** Chrome-free but changes the UX model. Keep these in reserve for specific hostless cases.

### 5.2 Migration path

**Key fact that de-risks this.** All eight `navigateTo` wizard code pages mount `WizardShell` **`embedded`**. Re-basing the **non-embedded** envelope does not touch them, provided the embedded inner markup stays identical (lock it with a snapshot test first). The envelope change reaches only the in-app (non-embedded) mounts:

| # | Non-embedded consumer today | Host / React | Risk | Regression test |
|---|---|---|---|---|
| 1 | `CreateAnalysisWizardWidget` → `CreateRecordWizard embedded={false} maxWidth="60vw" height="70vh"` (`CreateAnalysisWizardWidget.tsx:1182-1193`) | SpaarkeAi code page, React 18/19 | Low. **Canary**, closest to the Console | Existing `CreateAnalysisWizardWidget.test.tsx` + `ConversationPane.wizard-auto-run.e2e.test.tsx`; live: create an analysis from the hub, dark + light, at 100% and at large display size; Esc does *not* close; × closes |
| 2 | SmartTodo → `CreateTodoWizard` (non-embedded) (`SmartTodo/src/SmartTodoApp.tsx:665`) | SmartTodo code page | Low | `CreateTodoWizard/__tests__/*`; live: + New task from the board, finish, success screen |
| 3 | `DocumentRelationshipViewer` code page → `DocumentEmailWizard maxWidth="1280px" height="85vh"` (`DocumentRelationshipViewer/src/App.tsx:843`) | code page | Medium (size override → map to `lg`) | `DocumentEmailWizard.playbookLookup.test.ts`; live: email 2 docs |
| 4 | external-spa `DocumentUploadPage` (`external-spa/src/pages/DocumentUploadPage.tsx:371`) | Power Pages SPA | Medium (different host, BFF adapters) | external-spa build + manual upload |
| 5 | `SemanticSearchControl` PCF → `DocumentEmailWizard` (1280px/85vh, stacked over `FilePreviewDialog`) (`SemanticSearchControl.tsx:1898`) | **PCF, React 16.14 + Fluent 9.46 platform libs** | **Highest**: React 16, stacking, `pcf-deploy` | PCF build (`npm run build:prod`) + bundle size; live: preview → Email from preview (stacked) and bulk Email |
| — | `RegisterWizard` (SpeAdmin), `embedded hideTitle={false}` in-page | SpeAdmin code page | **Unaffected** (embedded) | smoke only |
| — | All 8 wizard code pages (Matter/Project/Event/Invoice/ReportCard/To Do/Work Assignment/Summarize, plus DocumentUpload, WorkspaceLayout) | `navigateTo` dialogs | **Unaffected** (embedded) | embedded snapshot test + one live open per page |

**Phases.** Effort is dev-days for one engineer, assuming Sonnet-5 task execution with review.

| Phase | Work | Effort |
|---|---|---|
| **P0** | Owner decision; ADR-050 Path B amendment (§6); fix stale rows in `MODAL-DECISION-CRITERIA.md` / `record-modal-selection.md`; cross-reference ADR-026 | 0.5–1 |
| **P1** | **Characterization tests first.** `wizardShellReducer` (next/prev/go-to, add/remove dynamic with `canonicalOrder`, clamp on remove, `initialStepId`) and `WizardShell` (canAdvance gating, Skip, early finish, finish → success / error / close, embedded snapshot). None exist today | 1–1.5 |
| **P2** | Re-base the non-embedded envelope onto `SprkModal`; add `dismiss`/`size`/`uiScale`/`nav`+`onBeforeNavigate`/`statusBar`/footer override/`stayOpenOnFinish`/`'skipped'`; lift `onBeforeNavigate` from `BrowseModal` into `SprkModal`'s `nav`; deprecate `maxWidth`/`height` strings (map consumers 1, 3, 5 to named sizes) | 3–4 |
| **P3** | Delete `WizardModal` + its test + its barrel export; update `MODAL-DESIGN-SYSTEM.md` §7 | 0.5 |
| **P4** | Regression rows 1–5 in the order shown (canary → PCF last); dark/light × 100%/large; Esc/×/Cancel behavior | 1.5–2 |
| **P5** *(separate decision)* | Move in-app callers off `navigateTo`: the `wizardLaunchers.ts` launchers + `WorkspaceGrid.tsx` handlers get an in-app "wizard host" that mounts the wizard component with Xrm adapters, as `CreateAnalysisWizardWidget`/`CreateMatterWizardWidget` already do. This removes the white header from every SpaarkeAi/LegalWorkspace launch. Measure the SpaarkeAi bundle delta. Ribbon (`sprk_wizard_commands.js`) stays on `navigateTo` | 5–8 |

**P0–P4 ≈ 7–9 days** delivers one shell, one wizard engine and the Console's needs. **P5 ≈ 5–8 days** is what visibly removes the white header from today's shipped wizards when launched from inside the app.

Behavior changes for UAT (they are intended):
- Wizards no longer close on Escape or backdrop (ADR-050 already says wizards are `explicit`, `MODAL-DESIGN-SYSTEM.md` §5).
- `resize: both` on the old surface (`WizardShell.tsx:111`) goes away.
- The header title becomes `SprkModal`'s `aria-labelledby` span instead of an `h1`.

---

## 6. ADR-050: amendment needed (CLAUDE.md §6.5, Path B)

**Why B, not A or C.**
- Not A (project exception): the conflict is general. ADR-050's preset list names the wrong wizard, and every project that builds a wizard will hit it.
- Not C (pivot to comply): complying literally means building on `WizardModal`, which is the worse outcome HANDOFF §4.1 @ ae1cc9f already corrected.

🔔 **ADR Conflict — Resolution Required**
- **ADR in question**: ADR-050 Canonical Modal Shell (`.claude/adr/ADR-050-canonical-modal-shell.md`)
- **Specific rule**: Decision (`:12`), "thin presets (`ConfirmModal`, `ChoiceModal`, `FormModal`, `PreviewModal`, `BrowseModal`, **`WizardModal`**)", and MUST NOT "give a surface its own bespoke modal envelope" (`:37`)
- **Conflict**: The only wizard any product uses (`WizardShell`, 8 consumers) owns its own envelope, which violates `:37`. The sanctioned preset (`WizardModal`) has 0 consumers and lacks the engine. ADR-050 is also silent on `navigateTo` chrome, the actual source of the owner's dark-mode complaint.
- **Proposed path**: **B (amend)**
- **Impact**: one preset removed; `WizardShell` re-based (non-embedded branch only); a launch rule added; three docs corrected
- **Alternatives rejected**: A (the problem is not project-local); C (forces a second wizard engine)

**Draft amendment points:**
1. **Decision**: the preset list becomes `ConfirmModal`, `ChoiceModal`, `FormModal`, `PreviewModal`, `BrowseModal`, **`WizardShell`**. `WizardShell` is the **engine-bearing wizard preset**: it renders inside `SprkModal`, and its `embedded` mode (no envelope) is the sanctioned variant for tabs, full pages and pages hosted under platform chrome. `WizardModal` is retired.
2. **MUST**: every multi-step flow uses `WizardShell`. No other wizard engine or reducer may be introduced.
3. **MUST (launch rule)**: when the caller is a Spaarke React surface, open modals and wizards **in-app** via the `SprkModal` family. `Xrm.Navigation.navigateTo(webresource|custom, {target:2})` is reserved for hostless contexts (ribbon/command scripts) or a deliberately separate deployable. Its title bar, expand and × are platform chrome, light-only and not themeable (cite [navigateTo](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto) + [modern look: dark mode not supported](https://learn.microsoft.com/en-us/power-apps/user/modern-fluent-design)).
4. **MUST**: a page shown under platform chrome renders `WizardShell embedded hideTitle` (no double header). This codifies today's practice.
5. **MUST NOT**: inject CSS into or DOM-manipulate platform dialog chrome (cite [supported customizations](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/supported-customizations)); a request to do so needs its own §6.5 sign-off.
6. **MUST**: wizards default to `dismiss="explicit"`, and pass `uiScale` like every other preset.
7. **Unchanged**: Layout 1 OOB `entityrecord` dialogs (85%×85%), the `fullCover` escalation, and the `MUST NOT` on per-entity sizes.
8. **`RecordNavigationModalShell`**: record that it has zero live consumers. Keep it as the dirty-check *protocol* library behind `onBeforeNavigate`, or deprecate it (owner's choice; no runtime impact).
9. **Companion doc fixes**, not ADR text:
   - `docs/standards/MODAL-DECISION-CRITERIA.md`:
     - `:33` says `WizardShell` comes "from `@spaarke/legal-workspace`" and that wizards are "NOT this doc's concern". It is in `@spaarke/ui-components`, and wizards should point to `MODAL-DESIGN-SYSTEM.md`.
     - `:34` tells yes/no confirms to use a raw Fluent `Dialog`. They should use `ConfirmModal`.
     - `:30`, `:95-113` describe Family 3 as composing `RecordNavigationModalShell`. That is now `BrowseModal` + `onBeforeNavigate`.
     - Add "Launching from Spaarke React → in-app; from ribbon → navigateTo (white chrome)".
   - `.claude/patterns/ui/record-modal-selection.md:26` and `.claude/patterns/ui/modal-shell.md:16` still say "compose `RecordNavigationModalShell`".
   - `docs/architecture/ui-dialog-shell-architecture.md`: its Three-Layer Model, step 4 "`embedded={true}` inside a Dataverse dialog", becomes the **ribbon path only**.
   - **ADR-026**, matrix row "Wizard / dialog (standalone) → Standalone HTML": add "deployment wrapper for hostless (ribbon) entry points; in-app hosting preferred when a Spaarke React host exists" (cross-reference, or a small Path B on ADR-026).
   - `.claude/CHANGELOG.md` row (required by `MODAL-DESIGN-SYSTEM.md` footer).

---

## 7. What the Console decision wizard should use NOW

- **Host**: `WizardShell`, **non-embedded**, mounted in-app from the Console worklist widget (SpaarkeAi), the same pattern as `CreateAnalysisWizardWidget` modal mode. **Never** a new `sprk_*` code page opened with `navigateTo`, which would bring the white header back.
- **Write against the stable API**: `IWizardStepConfig` (`renderContent`, `canAdvance`, `isSkippable`, `footerActions`), `IWizardShellHandle.addDynamicStep(config, [...followOnStepIds, 'confirm'])` / `removeDynamicStep` (mutually exclusive actions remove later steps), `initialStepId` (decided items open on the last step), `onFinish`, `finishLabel` ("Record decision" / "Dismiss & record"). None of this changes in P2.
- **Needs from P2 (schedule P1+P2 before the wizard's UI tasks; ≈ 4–5 days)**: `nav` + `onBeforeNavigate` (‹ N of M ›), `dismiss="explicit"`, `statusBar`, footer override + `stayOpenOnFinish` ("Recorded … Next open item"), `'skipped'` status, `uiScale`.
- **Discard check**: the consumer's `onClose` / `onBeforeNavigate` checks for unrecorded choices and opens a nested `ConfirmModal` ("Keep deciding" left / "Discard and close"). No shell change is needed beyond routing ×, Cancel and Close through the one `onClose` (already true, `WizardShell.tsx:473,507`). There is no shipped nested `SprkModal`-in-`SprkModal` precedent yet; `RecordNavigationModalShell`'s nested discard `Dialog` (`:337-358`) is the closest. Add one test.
- **Follow-ons**: `FollowOnGrid` + `WizardFollowOns/steps/*` as they are.
- **If the decision wizard must start before P2 lands**: build it on today's `WizardShell` with the step API only. Do not add a wrapper `Dialog` and do not use `WizardModal`. The P2 swap then reaches it with no code change beyond turning on the new props.
- **Correction to HANDOFF §4.1 @ ae1cc9f**: "every shipped wizard uses `WizardShell`" holds, but the list has **eight** direct consumers, not six (add `WorkspaceLayoutWizard` and external-spa `DocumentUploadPage`). Also, v4's "re-base on `SprkModal`" fixes the *two shells* problem but not the *white header*. That needs the launch rule (§5.1 item 4) and P5.

---

## 8. Open verification items (none blocks the decision)

| Item | How to close | Owner |
|---|---|---|
| Do Layout 1 `entityrecord` main-form dialogs and `openAlertDialog` darken under `themeOption=darkmode`? | Dev env, dark flag on, DevTools on one form dialog + one alert | QA, 15 min |
| Does Escape close a `navigateTo` web-resource dialog (bypassing our explicit-dismiss rule)? | Same session | QA |
| SpaarkeAi bundle delta when P5 mounts the Create* wizards in-app | `code-page-deploy` build, compare sizes | P5 task |
| `SemanticSearchControl` PCF bundle after P2 (`SprkModal` already in other PCFs, so the delta should be small) | `npm run build:prod` before/after | P2 task |

*Evidence sources: code on `origin/master` @ `b4b58a361`; `projects/mda-darkmode-theme/notes/DIALOG-CHROME-LIMITATION.md` (master); Microsoft Learn pages linked inline (navigateTo + modern-look fetched 2026-10-05; createPane, supported-customizations, modern-theme-overrides, appearance-settings and openAlertDialog per researcher sub-agent the same day).*
