# Task 040 — `EmailWorkspace` composition root (Pattern D source of truth)

**Status**: Complete. Build green (`tsc`), lint green (`tsc --noEmit`), jest green (7/7 new tests; 97/97 package-wide).

## What was built

New component folder `@spaarke/communication-components/src/components/EmailWorkspace/**` — the ONE shared
React 19 component both mounts (041 widget, 042 code page) render:

- `EmailWorkspace.types.ts` — `EmailWorkspaceProps` (host-agnostic: `dataverseClient`, `dataService`,
  `navigationService`, `webApi`, `authenticatedFetch`, `bffBaseUrl`, `accessPermissionOptions?`,
  `onSearchRecipients?`, `linkAnotherCatalog?`, `initialSelectedId?`) + `EmailWorkspaceWebApi` type alias.
- `EmailWorkspace.mapping.ts` — the "one place in the tree" for `sprk_communication` field-name knowledge this
  assembly needs: `mapRowToEmailCardItem` (saved-view row → `EmailCardItem`), `readFiledAssociations` (raw
  record → `FiledAssociation[]`, mirrors `CommunicationConnectionsApp.tsx`'s own `_field_value` annotation
  read), `resolveEmlArchiveDocumentId` (verified against `CommunicationService.cs`
  `FindExistingArchiveDocumentAsync`: entity `sprk_document`, lookup field `sprk_communication`, flag
  `sprk_isemailarchive`), `toWorkspaceRecordState`, `EMAIL_TRACKING_FIELDS` (placeholder — see below),
  `DEFAULT_ACCESS_PERMISSION_OPTIONS`.
- `useEmailWorkspaceRecord.ts` — the ONE per-selection `retrieveRecord` (no `$select`) + `.eml`-archive-lookup
  hook shared by the body/connections/tracking slots; owns the tracking-field write callbacks
  (`updateMonitor`/`updateHighPriority`/`updateAccessPermission`) and a `reload()`/`retry()` seam
  (`onAssociationsChanged` + `useEmailComposeActions`'s `onSent` both call `reload`).
- `EmailWorkspace.tsx` — the composition root: `EmailViewSelector`+`useEmailViews` (031) on top,
  `EmailReadingPaneShell` (032) below with all five `render*` slots filled by the Phase-3 sub-views
  (033/034/035) plus `useEmailComposeActions`'s `actions`/`composerDialog` (036). `OpenFullFormButton` (036,
  FR-15) is rendered in the `renderHeader` slot, wrapped in its own single-item `<Toolbar>` (see Deviation 1),
  directly above `EmailReadingHeader` — "near the toolbar" per the task prompt.
- `index.ts` — folder barrel.
- `__tests__/EmailWorkspace.test.tsx` — 7 tests: two-pane composition render, selection paints
  header/body(degrade-path)/tracking/connections, selection WITH a resolved `.eml` archive (verifies the
  archive-doc-id lookup wires through to `EmailBodyView`'s `authenticatedFetch` call + sandboxed-iframe
  `srcdoc`, closing acceptance criterion 3's "given a selected Email card with a `.eml` archive" case),
  dual-mount-parity smoke (two independent host wrappers around the SAME component produce identical
  `outerHTML`), dark-mode render with no console errors, and a closed negative set (no
  `isWidget`/`isCodePage`-style branch, no self-mounted `FluentProvider`, no `as React.ComponentType<` cast,
  no `ComponentFramework`/`Xrm` reference).
- `src/components/index.ts` — appended `export * from './EmailWorkspace';` (one-line barrel addition only).

## The `EmailWorkspaceProps` contract (for tasks 041/042)

Both mounts must supply, as plain host-resolved objects/functions (no PCF `ComponentFramework` type, no `Xrm`
import in either mount's OWN wiring code — only inside whichever adapter factory each mount calls):

| Prop | Type | Typical resolution |
|---|---|---|
| `dataverseClient` | `IDataverseClient` | An `XrmDataverseClient`/`BffDataverseClient` instance (`@spaarke/ui-components`) |
| `dataService` | `IDataService` | `createXrmDataService()` (Xrm host) or a BFF-backed equivalent |
| `navigationService` | `INavigationService` | `createXrmNavigationService()` |
| `webApi` | `EmailWorkspaceWebApi` (`IResolverWriteContext['webApi'] & IPolymorphicPickerWebApi`) | `context.webAPI` (PCF/Xrm) cast, or a BFF equivalent bridge |
| `authenticatedFetch` | `AuthenticatedFetchFn` | `@spaarke/auth`'s `authenticatedFetch` free function |
| `bffBaseUrl` | `string` | The mount's configured BFF base URL (no `/api` suffix) |
| `accessPermissionOptions?` | `IAccessPermissionOption[]` | Optional — defaults to the Standard/Limited/Restricted fallback triple |
| `onSearchRecipients?` | `(query) => Promise<ILookupItem[]>` | Optional recipient-directory typeahead |
| `linkAnotherCatalog?` | `readonly RecordTypeCatalogEntry[]` | Optional — defaults to the `TODO_REGARDING_CATALOG`-derived catalog already built into `EmailConnectionsReview` |
| `initialSelectedId?` | `string` | Optional deep-link into a specific email |

Both mounts render `<EmailWorkspace {...props} />` UNCHANGED inside their own sized container + `FluentProvider`
— NO business logic, NO conditional branching lives in the mount; every behavior difference is a config value
(e.g. a different `bffBaseUrl`), never an `if` branch inside `EmailWorkspace` itself (NFR-06).

## Deviations / notes for reviewers

1. **`OpenFullFormButton` wrapped in its own `<Toolbar>`.** Discovered during Step 9.5 test authoring: mounting
   task-036's standalone `<ToolbarButton>`-based `OpenFullFormButton` alongside the shell's own `<EmailToolbar>`
   AND the already-mounted `DataGridViewSelector` (031/`@spaarke/ui-components`) in one jsdom test tree crashed
   deep in `@fluentui/react-tabster`'s `useTabster` hook (`getMover`: "Cannot read properties of undefined
   (reading 'set')") — root-caused to a cross-package Fluent/tabster VERSION MISMATCH (this package's own
   `@fluentui/react-components@^9.46.2`/`tabster@8.8.0` vs. `@spaarke/ui-components`'s
   `@fluentui/react-components@^9.73.2`/`tabster@8.7.0`), NOT a defect in `OpenFullFormButton` itself (its own
   isolated test suite passes). Fixed at the TEST-INFRA layer, not by touching `OpenFullFormButton` or
   `@spaarke/ui-components` source: added `@fluentui/react-components`/`@fluentui/react-tabster`/`tabster`
   dedup entries to `jest.config.cjs`'s `moduleNameMapper`, extending the SAME "pin to this package's own
   node_modules copy" strategy already used there for React/ReactDOM (see the config file's own updated header
   comment for the full root-cause note). Wrapping `OpenFullFormButton` in a single-item `<Toolbar>` is also
   the semantically-correct usage of a Fluent `ToolbarButton` (kept regardless of the infra fix).
2. **Three tracking fields (`monitor`/`highPriority`/`accessPermission`) are PLACEHOLDER Dataverse field names**
   (`sprk_ismonitored`/`sprk_ishighpriority`/`sprk_accesspermission` — see `EMAIL_TRACKING_FIELDS` in
   `EmailWorkspace.mapping.ts`). `docs/data-model/sprk_communication.md` (reviewed 2026-07-21) does not document
   any field matching these three — design.md itself flags the `TrackingFieldTrio` placement on this form as
   "net-new to the form." Schema creation is out of this task's scope (code-only outputs, no `dataverse` tag);
   the placeholder is the "one place in the tree" (mirrors the `TrackingFieldTrio` PCF's own
   `index.ts` convention) that will need a one-line update once the columns exist. **Flag for `/defer` at
   task 090** (or sooner, if a schema task is scheduled before then).
3. **`EmailCardItem.isUnread` defaults to `false` for every card.** `sprk_communication` has no documented
   read/unread column (verified against the same data-model doc) — flagged alongside item 2 above rather than
   invented.
4. **Card preview text** is derived from `sprk_body` (HTML) via the shared `sanitizeEmailHtml` (task 001) THEN
   tag-stripped to plain text (never `dangerouslySetInnerHTML` in `EmailCardList` — matches its existing
   contract). A saved view that doesn't select `sprk_body` degrades to an empty preview, not a crash.
5. **Did NOT edit** any Phase-3 sub-view (`EmailCardList`, `EmailViewSelector`, `EmailReadingPaneShell`,
   `EmailBody`, `EmailReadingHeader`, `EmailAssociationsAndTracking`, `EmailComposeActions`) — consumed only via
   their published props/hooks per each task's own barrel + notes.

## Quality gates (Step 9.5)

- Build: `npm run build` (tsc) — clean, 0 errors.
- Lint: `npm run lint` (tsc --noEmit) — clean, 0 errors.
- Tests: `npx jest` — 14/14 suites, 97/97 tests green (package-wide; no regression in the other 13 suites from
  the `jest.config.cjs` dedup change).
- `code-review`: Clean — 0 Critical, 0 Warning, 2 Suggestions (both cosmetic — see report). No AI code smells
  beyond a single Suggestion-level note on the composition root's necessarily multi-concern shape (justified —
  matches the canonical `CommunicationsWorkspaceWidget.tsx` Pattern D reference shape).
- `adr-check`: Clean — 0 Violations, 0 Warnings across ADR-012 (context-agnostic, grep-verified), ADR-021
  (Fluent v9 tokens only, no self-mounted `FluentProvider`), ADR-022/NFR-05 (no `React.ComponentType` cast),
  ADR-028 (auth v2 — `authenticatedFetch` injected, no raw fetch/Authorization header), ADR-045 (no new
  client-side association write logic — additive path consumed via `EmailConnectionsReview` unchanged).

## Files changed

- `src/client/shared/Spaarke.Communication.Components/src/components/EmailWorkspace/EmailWorkspace.types.ts` (new)
- `src/client/shared/Spaarke.Communication.Components/src/components/EmailWorkspace/EmailWorkspace.mapping.ts` (new)
- `src/client/shared/Spaarke.Communication.Components/src/components/EmailWorkspace/useEmailWorkspaceRecord.ts` (new)
- `src/client/shared/Spaarke.Communication.Components/src/components/EmailWorkspace/EmailWorkspace.tsx` (new)
- `src/client/shared/Spaarke.Communication.Components/src/components/EmailWorkspace/index.ts` (new)
- `src/client/shared/Spaarke.Communication.Components/src/components/EmailWorkspace/__tests__/EmailWorkspace.test.tsx` (new)
- `src/client/shared/Spaarke.Communication.Components/src/components/index.ts` (modify — one-line barrel append)
- `src/client/shared/Spaarke.Communication.Components/jest.config.cjs` (modify — Fluent/tabster dedup, see Deviation 1)
