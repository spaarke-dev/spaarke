# Task 091 Completion Notes — Retire solution-local `navigation.ts` copies (P7, FR-18)

> Written by task 091 execution (FULL rigor, auto-escalated per CLAUDE.md §8 — `.ts`/`.tsx`-modifying). Sibling-safe: this task never touched `sprk_DocumentOperations.js`, the inventory file, `src/client/**`, or `.claude/**`.

## 0. Scope-discovery deviation (read this first)

Task 090's inventory (`notes/oob-navigateto-inventory.md` §9) enumerated **4 real calls** for task 091 — `openRecordDialog` + `navigateToEntityList` × 2 files — because its method was a repo-wide grep for the literal `.navigateTo(` call shape. `navigation.ts` also exports a **third function, `navigateToEntity`**, which calls `Xrm.Navigation.openForm` (a different Xrm API, no `target`/`width`/`height` at all) with a `window.parent.postMessage` fallback. That function doesn't match `.navigateTo(` textually, so it fell outside task 090's scope entirely — but it still had to be retired here since the whole file is deleted. Grepping LegalWorkspace turned up **13 additional files calling `navigateToEntity`** (SmartTodo has **zero** callers of its own copy — confirmed dead code, see §4). This is not scope creep; it's a necessary consequence of the hard AC "grep-prove zero remaining imports." Total real call sites handled: **26**, across 17 files + 2 deletions.

## 1. Caller table

### 1a. `openRecordDialog(entityName, entityId)` → `createXrmNavigationService().openRecordModal?.(entityName, entityId)` — **record** (85%×85%, via the existing hub method, no size flag)

| # | File:Line (pre-edit) | Entity | Rationale |
|---|---|---|---|
| 1 | `RecordCards/RecordCard.tsx:146` (`handleClick`) | generic (Matters/Projects/Invoices) | Existing-record modal open — exact match for `openRecordModal`'s contract |
| 2 | `RecordCards/RecordCard.tsx:150` (`handleEdit`) | generic | Same |
| 3 | `ActivityFeed/ActivityFeed.tsx:349` (`handleEdit`) | `sprk_event` | Same |
| 4 | `ActivityFeed/FeedItemCard.tsx:218` (`handleEdit`, else-branch) | `sprk_event` | Same |
| 5 | `RecordCards/DocumentCard.tsx:163` (`handleExpand`) | `sprk_document` | File's own comment already documented this as "opens as a modal dialog (`navigateTo` target:2)" — exact match |

Note: `openRecordModal` is declared **optional** on `INavigationService` (`openRecordModal?(...)`, JSDoc: "callers use `nav.openRecordModal?.(...)`"). Missing the `?.` on the first pass produced 5 real `TS2722` errors (caught and fixed before reporting — see §5).

### 1b. `navigateToEntity({action:'openRecord', entityName, entityId})` → `createXrmNavigationService().openRecord(entityName, entityId)` — **no size** (uses `Xrm.Navigation.openForm`, a full-page/new-window nav, not a `target:2` dialog — the "OOB size scale" doesn't apply to this shape at all)

| # | File:Line (pre-edit) | Entity | Notes |
|---|---|---|---|
| 6 | `CreateEvent/EventWizardDialog.tsx:144` (`viewEvent`) | `sprk_event` | Wizard "view created record" follow-on |
| 7 | `CreateMatter/WizardDialog.tsx:208` (`viewMatter`) | `sprk_matter` | Same pattern |
| 8 | `CreateProject/ProjectWizardDialog.tsx:250` (`viewProject`) | `sprk_project` | Same pattern |
| 9 | `CreateWorkAssignment/WorkAssignmentWizardDialog.tsx:480` (`viewRecord`) | `sprk_workassignment` | Same pattern |
| 10 | `MyPortfolio/MatterItem.tsx:179` (`handleNavigate`) | `sprk_matter` | Row click |
| 11 | `MyPortfolio/DocumentItem.tsx:203` (`handleNavigate`) | `sprk_document` | Row click |
| 12 | `MyPortfolio/ProjectItem.tsx:193` (`handleNavigate`) | `sprk_project` | Row click |
| 13 | `SmartToDo/TodoDetailPane.tsx:277` (`handleEdit`) | `sprk_todo` | Edit action |
| 14 | `RecordCards/DocumentCard.tsx:129` (`handleDoubleClick`) | `sprk_document` | File's own comment: "Double-click opens the record in a new tab via `Xrm.Navigation.openForm`" — exact match |
| 15 | `RecordCards/DocumentCard.tsx:141` (`handleKeyDown`, Enter key) | `sprk_document` | Same as double-click |
| 16 | `ActivityFeed/FeedItemCard.tsx:213` (`handleRegardingClick`) | dynamic (resolved from `regardingRecordTypeName`) | Opens the regarding record |
| 17 | `FilePreview/FilePreviewDialog.tsx:113` (`handleOpenRecord`) | `sprk_document` | **FLAGGED — see §2a**: dropped `openInNewWindow: true` |
| 18 | `FindSimilar/FindSimilarDialog.tsx` — `filePreviewServices.navigateToEntity` | dynamic (search result) | Translation shim onto shared adapter — see §3 |
| 19 | `FindSimilar/FindSimilarDialog.tsx` — `handleNavigateToEntity` (`onNavigateToEntity` prop) | dynamic (search result) | Same file, second callback — see §3 |

### 1c. `navigateToEntity({action:'openView', entityName})` → preserved **verbatim** as inline `window.parent.postMessage` (no size at all — this action never touched `Xrm.Navigation`)

| # | File:Line (pre-edit) | Entity | Notes |
|---|---|---|---|
| 20 | `MyPortfolio/MyPortfolioWidget.tsx:299` (`handleViewAllMatters`) | `sprk_matter` | **FLAGGED — see §2b** |
| 21 | `MyPortfolio/MyPortfolioWidget.tsx:306` (`handleViewAllProjects`) | `sprk_project` | **FLAGGED — see §2b** |
| 22 | `MyPortfolio/MyPortfolioWidget.tsx:313` (`handleViewAllDocuments`) | `sprk_document` | **FLAGGED — see §2b** |

### 1d. `navigateToEntityList(entityName, viewId)` → inline `xrm.Navigation.navigateTo({pageType:'entitylist',...})` mapped onto **record** (85%×85%, nearest-fit) — **FLAGGED for every site, see §2c**

| # | File:Line (pre-edit) | Entity | Notes |
|---|---|---|---|
| 23 | `Shell/WorkspaceGrid.tsx:226` (`handleOpenAllUpdates`) | `sprk_event` | Fixed viewId |
| 24 | `Shell/WorkspaceGrid.tsx:479` (`handleOpenDocumentsDialog`) | `sprk_document` | Caller-supplied or default viewId |
| 25 | `Shell/WorkspaceGrid.tsx:657` (`handleNavigate`, `target.type === "view"` branch) | dynamic | Generic `SectionFactoryContext` navigate target |
| 26 | `QuickSummary/QuickSummaryRow.tsx:77` (inline `onClick`) | dynamic (6 metric cards) | Per-card entity/view |

`WorkspaceGrid.tsx` and `QuickSummaryRow.tsx` construct the `navigateTo` call inline (no shared hub method exists for `pageType:'entitylist'` — only `openRecordModal` for `entityrecord` and `openDialog` for `webresource`), importing `OOB_MODAL_SIZES` from `@spaarke/ui-components` for the width/height values — this is the exact pattern task 090 already established for its own "bypass sites" with no dedicated hub function. `WorkspaceGrid.tsx` reuses the file's own pre-existing inline `(window as any)?.Xrm ?? (window.parent as any)?.Xrm ?? (window.top as any)?.Xrm` resolution idiom (already repeated 7× in that file); `QuickSummaryRow.tsx` had no prior local idiom, so it imports `getXrm` from the solution's existing `services/xrmProvider.ts` (the same utility `navigation.ts` itself used internally — not a new/reintroduced helper).

## 2. Flags for the one-time P7 visual review

### 2a. `FilePreviewDialog.tsx` — dropped `openInNewWindow: true`
The retired `navigateToEntity` call passed `openInNewWindow: true`. The shared adapter's `INavigationService.openRecord(entityName, entityId): Promise<void>` has no such option, and `Spaarke.UI.Components` (`src/client/**`) was a hard boundary for this task — extending the interface was out of scope. **Current behavior after this task: opens in the same window/tab instead of a new one.** Recommend: either confirm same-window is fine, or file a follow-up to add an optional `options?: {openInNewWindow?: boolean}` third parameter to `openRecord` (purely additive, backward compatible with all other ~15 consumers).

### 2b. `MyPortfolioWidget.tsx` — 3× `action:'openView'` postMessage, no known receiver
Traced the retired function precisely: `navigateToEntity`'s `if` branch only fires Xrm.Navigation for `action === "openRecord"`; for `action === "openView"` (used only by `handleViewAllMatters`/`handleViewAllProjects`/`handleViewAllDocuments`, all with **no `entityId`/`viewId`**) it goes straight to `window.parent.postMessage({action:'openView', entityName}, '*')`. Repo-wide grep for a receiver (`openView`, `event.data.action`, any generic `message` listener in `src/` and `infrastructure/`) found **none** — this looks like a comment-documented intent ("Footer navigation posts a postMessage to the parent MDA frame for each tab") that was never wired up on the receiving end. Preserved verbatim (zero behavior change) rather than guessed at — inventing a `navigateToEntityList`-style redirect would require a `viewId` that doesn't exist at these call sites. Recommend: confirm whether this is genuinely dead functionality (candidate for `/defer`) or whether a receiver exists outside this repo.

### 2c. Entitylist opens (4 sites) mapped to `record` (85%×85%) — doesn't literally match any of the 3 named sizes
`oobModalSizes.ts` names exactly 3 sizes: `record` (existing entity **record**, singular), `createForm` (entity **create** form), `wizard` (Code Page wizard). None is defined for `pageType:'entitylist'` (a system-view grid, not a single record and not a create flow). Per this task's binding point ("do NOT re-pin a bespoke 80%... still repointed to the nearest named size... record the delta precisely"), all 4 sites were mapped to **`record`** (85%×85%) — nearest fit by both semantics (browsing existing data, not creating it) and magnitude (80→85 delta, same collapse already applied to plain record-opens throughout task 090). Recommend the owner confirm `record` is acceptable for entity-list grids, or approve introducing a 4th named size (`entityList`?) in a follow-up if the visual review finds 85%×85% too tall/narrow for a grid view.

## 3. `FindSimilarDialog.tsx` special case — translation shim, not a reintroduced helper

This file passes `navigateToEntity` as **two callback values** matching shared-lib prop contracts: `IFilePreviewServices.navigateToEntity` (typed `action:'openRecord'` literal, `entityId` required — always invoked this way, confirmed by reading the only real caller, `FindSimilarResultsStep.tsx:350`) and `onNavigateToEntity` (typed against the broader `INavigationMessage`, but the shared `FindSimilarResultsStep.handleOpenRecord` only ever calls it with `action:'openRecord'` + `entityId`). Both are now one-line closures over a single module-level `const navigationService = createXrmNavigationService();` that call `.openRecord(entityName, entityId)`. This is a call-shape **adapter** (translating an object-literal callback into `createXrmNavigationService()`'s 2-arg method), not a duplicated navigation implementation — the actual `Xrm.Navigation` call lives entirely in the canonical shared adapter, satisfying ADR-012.

## 4. SmartTodo — dead code, no callers

Exhaustive grep (`utils/navigation`, `navigateToEntity`, `openRecordDialog`, `navigateToEntityList`, `INavigationMessage`) across all of `src/solutions/SmartTodo` found **zero** callers of its local `navigation.ts`. `SmartTodoApp.tsx` already uses `createXrmNavigationService()` directly (task 090's hub) plus its own separate inline `xrm.Navigation.navigateTo` call for `openSprkTodoAsLayout1` (already repointed by task 090, unrelated to this file). No test file references it either (`ToolbarActions.test.ts`, `useLaunchContext.test.ts`, `useUserPreferences.test.ts` — none touch navigation). SmartTodo required **zero caller edits** — just the file deletion.

## 5. Self-review findings caught before reporting

`.openRecordModal(...)` — first pass omitted optional chaining. `INavigationService.openRecordModal?(...)` is declared **optional** (JSDoc: "callers use `nav.openRecordModal?.(...)`"). This produced exactly 5 new `TS2722: Cannot invoke an object which is possibly 'undefined'` errors (243 vs the 238 baseline) at the 5 call sites in §1a. Fixed by adding `?.` at all 5 sites; verified the error count returned to exactly 238 (see §7).

## 6. Deletion + grep proof

Both files deleted:
- `src/solutions/LegalWorkspace/src/utils/navigation.ts` — deleted
- `src/solutions/SmartTodo/src/utils/navigation.ts` — deleted

Repo-wide grep proof (post-deletion), all zero matches:
- `utils/navigation['"]` across `src/` — **0 matches**
- `LegalWorkspace/src/utils/navigation` / `SmartTodo/src/utils/navigation` (any relative-import depth) across `src/` — **0 matches**
- `from ['"](\.\./)+utils/navigation['"]` / `from ['"]\./navigation['"]` repo-wide — **0 matches**
- `ls` on both deleted paths — confirmed "No such file or directory"

Remaining hits for `navigateToEntity\b|openRecordDialog\b|navigateToEntityList\b` are all either (a) my own explanatory comments referencing the *retired* names, or (b) the `IFilePreviewServices.navigateToEntity` **property name** (an interface field in the shared lib, not a call to the deleted function) — verified individually, not code references to the deleted module.

## 7. Builds

- **SmartTodo**: `npm run build` (`vite build` + html rename) — **green**. `dist/smarttodo.html` produced, 2,008.83 kB / gzip 537.95 kB. Only warnings are pre-existing third-party `/*#__PURE__*/` comment-position notices from `@microsoft/applicationinsights-*` (unrelated to this task).
- **SmartTodo**: `npx jest` — **3 suites passed, 61 passed / 1 skipped, 0 failed**. (Pre-existing "Unknown option setupFilesAfterEach" config warning, unrelated.) None of the 3 suites (`ToolbarActions`, `useLaunchContext`, `useUserPreferences`) reference navigation — confirmed no updates needed.
- **LegalWorkspace**: full `npm run build` is broken on master (Issue #712, not this task's concern, per dispatch). Ran scoped `npx tsc --noEmit` instead: **238 errors**, exactly matching the documented pre-existing baseline. First pass was 243 (+5, the `openRecordModal` optional-chaining bug in §5); fixed, re-ran, confirmed back to exactly 238. Cross-checked every remaining error's file:line against my own edit ranges — all 238 fall outside every range I touched (e.g. `ActivityFeed.tsx:445` vs my edit at `~349`; `WorkAssignmentWizardDialog.tsx:360` `Cannot find name 'navigationService'` — a *different*, pre-existing `navigationService` prop reference in `SelectWorkStep`, nowhere near my `viewRecord` edit at `~479`).
- **LegalWorkspace**: no `jest.config` / `test` npm script exists for this solution at all (confirmed — this predates the task; 3 stray `.test.ts(x)` files exist but aren't wired to any runner, and none reference navigation or any of the 17 touched files).
- No `npm install` was needed — `node_modules` already present in both solutions.

## 8. FULL-rigor Step 9.5 gates

**Self code-review**: No dangling imports (grep-verified, §6). Size mapping per-caller verified in the table above — every `record` mapping is either an exact-fit (`entityrecord` dialog open) or an explicitly-flagged nearest-fit (`entitylist`); every `openRecord` (no-size) mapping is verified against the underlying `openForm` API (which genuinely has no width/height concept) rather than assumed. One real bug (missing `?.` on `openRecordModal`) was caught via the tsc diff, not by inspection alone — fixed before reporting, not left for a reviewer to find.

**adr-check**:
- **ADR-012** (Shared Component Library) — compliant. Read the full ADR: its own "What Goes Where" table explicitly places `navigateTo` / dialog-opening *code* in the **consumer**, with the shared library holding the abstracted `INavigationService` + adapter factory + size constants. My entitylist inline `navigateTo` construction in `WorkspaceGrid.tsx`/`QuickSummaryRow.tsx` (consumer-side, using shared size constants) matches this exactly. `FindSimilarDialog.tsx`'s translation shim delegates 100% of the actual Xrm call to the canonical `createXrmNavigationService()` (§3) — no navigation logic duplicated. Zero new solution-local navigation *modules* were created; both existing duplicate files are now deleted (net component count decreases, per the POML's own `<notes>`).
- **Record-invariant** (`.claude/patterns/ui/record-modal-selection.md` — 85%×85% for every entity, MUST NOT vary per-entity) — compliant. Every `openRecordModal` call routes through the one hub method (hardcoded 85×85 internally); every entitylist inline call uniformly uses `OOB_MODAL_SIZES.record` (no per-entity variance).
- **NFR-04** (dual-React compile clean) — SmartTodo (React 19, Vite/esbuild) builds green; LegalWorkspace (React 19) scoped tsc holds at the pre-existing 238-error baseline with zero new errors in touched files.
- **NFR-05** (client-only) — no touch to `src/server/api/Sprk.Bff.Api/**` or any server code; confirmed via edit list (all 17 files under `src/solutions/LegalWorkspace/**`).

## 9. Hard boundaries respected

Did not touch: `TASK-INDEX.md`, `current-task.md`, `.claude/**`, `pcf-safe.ts`, `oob-navigateto-inventory.md` (read-only), `sprk_DocumentOperations.js` (either copy), or anything under `src/client/**` (read several files there for reference only — `xrmNavigationServiceAdapter.ts`, `oobModalSizes.ts`, `serviceInterfaces.ts`, `xrmContext.ts`, `filePreviewTypes.ts`, `findSimilarTypes.ts` — zero writes). No `git add`/`commit` was run.

**Outstanding**: per the dispatch's hard boundary, `TASK-INDEX.md` was NOT updated to ✅ despite POML step 6 — deferred to the orchestrating session (same pattern as `.claude/**`, likely to avoid a merge race with sibling task 092 in the same parallel-group wave).
