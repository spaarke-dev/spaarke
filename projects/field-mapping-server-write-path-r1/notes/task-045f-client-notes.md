# Task 045f part B - client readers of the regarding record URL

Project: field-mapping-server-write-path-r1. Branch `task/fm-045f-client`. Part A (server) is a separate note and branch.

## Rule

A product surface that opens the stored regarding record URL (`sprk_regardingrecordurl`, or a field carrying it) opens it
only when it is a same-origin relative record URL:

- a path that starts with `/main.aspx?`;
- no scheme, host, `//`, backslash, whitespace, control character or fragment;
- naming exactly one `etn` (a table logical name) and exactly one `id` (a GUID).

Otherwise the surface builds the URL from the row's pair (the regarding record type's table, the regarding record id) in
the form `/main.aspx?pagetype=entityrecord&etn={table}&id={id}`. When it can do neither it does not navigate. A URL a
reader passes on is always rebuilt in that canonical form from the parsed `etn` and `id`, so nothing else in the stored
text reaches the navigation call. When the row's pair is known and the stored (relative) URL names a different record,
the pair wins.

## Helper

`src/client/shared/Spaarke.UI.Components/src/utils/safeRegardingRecordUrl.ts` (exported from `@spaarke/ui-components`):

| Function | Use |
|---|---|
| `safeRegardingRecordUrl(stored, pairTable?, pairId?)` | the canonical relative URL a reader may open or render as an `href`, else `null` |
| `readRelativeRecordTarget(stored)` | `{ entityName, entityId }` from a same-origin relative record URL, else `null` (readers that open a form) |
| `resolveRegardingRecordTarget(stored, pairTable?, pairId?)` | the same decision, as a target |
| `buildRegardingRecordUrl(table, id)` | the canonical URL, validated |
| `resolveRegardingPairTable(webApi, recordTypeId)` | the table logical name of a record type row (`sprk_recordtype_ref.sprk_recordlogicalname`); `null` on any fault |

No new package. No reader is a plain web resource that needs a local copy (see inventory).

## Reader inventory (line numbers at the merge of master 92d0ca09f7)

Only readers of the REGARDING record URL are in scope.

| # | Reader | file:line | Navigates or renders the stored value | Changed |
|---|---|---|---|---|
| 1 | Event side pane "Parent" button | `src/solutions/EventDetailSidePane/src/components/HeaderSection.tsx` (handleParentClick, hasParent), `services/sidePaneService.ts` `navigateToParentRecord` -> `Xrm.Navigation.openUrl` / `window.location` | navigates | yes |
| 2 | TodoDetail "Record" link | `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx` `handleOpenRegardingRecord` -> host callback `onOpenRegardingUrl(url)` | passes the stored value to the host to navigate | yes |
| 3 | CommunicationConnections number link | `src/client/pcf/CommunicationConnections/CommunicationConnections/CommunicationConnectionsApp.tsx` `resolvePrimaryOpenTarget` -> `Xrm.Navigation.navigateTo` | parses `etn`/`id` out of the stored value and opens that form | yes |
| 4 | RegardingResolver number link (priority 2) | `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverApp.tsx` `resolveClickTarget` -> `Xrm.Navigation.navigateTo` | parses `etn`/`id` out of the stored value and opens that form | yes |

Checked, no change:

| Candidate | Finding |
|---|---|
| `src/client/external-spa` (`web-api-client.ts:192`, mocks) | type field and mock data only; nothing opens or renders it |
| `src/solutions/Notepad` (`types/memo.ts:16`) | type field only; nothing opens or renders it |
| `src/solutions/NavigatorPane` | does not read the field |
| `src/client/webresources/js/sprk_todo_regarding_presave.js` | writer (pending payload -> columns), not a reader |
| `ResolverWriteHandler.ts`, `ConnectionsWriteHandler.ts`, `TodoRegardingUpdateBuilder.ts`, `PolymorphicResolverService.ts`, `useInlineTodoCreate.ts`, `todoService.ts`, `eventService.ts` | writers |
| `src/client/pcf/SpaarkeGridCustomizer` `RegardingLinkRenderer.tsx` | already builds from the pair (`sprk_regardingrecordid` + record type) through `openForm`; does not read the URL column |
| `SprkChat` `useActionHandlers.ts` / `OutcomeCard.tsx` / `SprkChat.tsx:1037` `recordUrl` | a BFF tool-result link (`SideEffectGateAIFunction.BuildRecordUrl`), not the stored regarding column |
| `DocumentRelationshipViewer` / `SemanticSearch` `recordUrl`, `NavigationService` | a document record URL from the search/graph API, a different field |
| `DocumentUploadWizard` `SuccessScreen.tsx:154`, office add-ins `openRecordLauncher` | build their own document record URL from the new record's id |
| `ColumnRendererService.renderUrl` (generic `Url` attribute column) | generic URL renderer with no attribute name; would apply to `sprk_regardingrecordurl` only if a grid shows that column. Known limit, below |

## Before -> after per reader

| Reader | Before | After |
|---|---|---|
| 1 Event side pane | any stored value reached `openUrl` / `window.location` | `navigateToParentRecord` refuses anything but a same-origin relative record URL. The button calls `openParentRecord`: the stored URL when it is one and names the pair's record, else the URL built from `sprk_regardingrecordid` + the table of `_sprk_regardingrecordtype_value` (one `sprk_recordtype_ref` read), else nothing opens. The header select now also reads those two columns; the button also shows when only the pair exists |
| 2 TodoDetail | the stored value went to the host callback as is | the host is handed only the safe URL (stored, else pair: `RECORD_TYPE_ENTITY_MAP[type name]` + `sprk_regardingrecordid`); when neither works it falls to the existing `onOpenRegardingRecord` path, else nothing |
| 3 CommunicationConnections | `new URL(stored)`; a relative value threw and fell through to the filed association; an absolute value on any host was parsed and its `etn`/`id` opened | `readRelativeRecordTarget`; a relative canonical value now opens directly; any other value is never read and the existing filed-association fallback applies |
| 4 RegardingResolver | `new URL(stored)`, same as 3 | a relative value is read; any other value is never read: the target is built from `sprk_regardingrecordid` + the table of the record type (two extra reads, only on that path); else nothing opens |

## Evidence

Failed before / passed after (the reader tests only; the old reader source copied back, the test run, the new source restored):

| Surface | Test file | Before (old source) | After |
|---|---|---|---|
| `@spaarke/ui-components` helper | `src/utils/__tests__/safeRegardingRecordUrl.test.ts` | new module (no old code) | 33 passed |
| TodoDetail | `src/components/TodoDetail/__tests__/TodoDetail.test.tsx` | 4 failed / 15 passed | 19 passed |
| EventDetailSidePane | `src/services/__tests__/sidePaneService.parentRecord.test.ts` | 15 failed / 2 passed (of 17) | 17 passed; suite 27 passed (3 suites) |
| CommunicationConnections | `__tests__/CommunicationConnectionsApp.test.tsx` | 3 failed / 11 passed | 14 passed; PCF suite 50 passed (5 suites) |
| RegardingResolver | `__tests__/RegardingResolverApp.test.tsx` | 5 failed / 69 passed | 74 passed; PCF suite 146 passed (3 suites) |

Two existing tests used an absolute stored URL as the happy path (CommunicationConnections default denorm URL;
RegardingResolver "pre-loaded record" and "fresh picker selection wins" fixtures). They now use the canonical relative
form, which is what the server stores (the old absolute form is the case the rule refuses).

Other gates:

- `@spaarke/ui-components`: `tsc --noEmit` clean; `eslint` clean on the new util and TodoDetail; `jest src/utils src/components/TodoDetail` 27 suites / 792 tests passed.
- EventDetailSidePane: `npx tsc --noEmit` clean; `npm run build` (vite) succeeded.
- CommunicationConnections: `npm run build:prod` succeeded (3 pre-existing warnings).
- RegardingResolver: `npm run build:prod` succeeded (3 pre-existing warnings).
- Environment: the worktree had no `node_modules` or `dist`; the shared libs (`Spaarke.UI.Components`, `Spaarke.Auth`, `Spaarke.SdapClient`, `Spaarke.Communication.Components`, `Spaarke.SmartTodo.Components`) were installed and built locally. Lock-file drift from the installs was not committed.

## Surfaces that need a rebuild and deploy later (none done here)

- `src/solutions/EventDetailSidePane` (code page; `code-page-deploy`).
- `src/client/pcf/CommunicationConnections` and `src/client/pcf/RegardingResolver` (PCF `build:prod`; `pcf-deploy`; versions not bumped here).
- Every surface that bundles `TodoDetail` from `@spaarke/ui-components` (the library change takes effect when each consumer is rebuilt). No consumer in this repo passes `onOpenRegardingUrl`; hosts that do receive only the safe URL after a rebuild.
- Do not edit the built bundles (`bundle.js`, `src/dataverse/solutions/**`); they are regenerated by the builds above.

## Known limits

- Rows written before the server canonicalizes (part A) hold an absolute URL (`https://{org}/main.aspx?...`, from the old client `buildRecordUrl`). Readers now refuse those and use the pair. Readers 1, 2 and 4 have the pair. Reader 3 (CommunicationConnections) uses its filed-association fallback (matched by record name), as it did when the URL was unusable; it does not read the pair columns.
- When the record type table cannot be read (no privilege, fault), readers 1 and 4 open nothing. Reader 2 uses a fixed 11-entry type-name map, as its legacy path always did.
- The generic `ColumnRendererService.renderUrl` renders any `Url` attribute as an `href` without knowing the attribute; it is not specific to this column and was left alone. A grid that shows `sprk_regardingrecordurl` as a link would need the attribute-aware rule.
- HeaderSection (tsx, Fluent) has no component test harness in the pane (node jest, `.test.ts` only); its click handler is a one-line call into `openParentRecord`, which carries the tests, and the pane type-checks and builds.
- Same-origin means "relative path": an absolute URL on the org's own host is also refused, per the task rule.
