# Task 031 — ViewSelector wiring for the Email surface

**Status**: Complete. Build green (`tsc`), jest green (10/10 new tests; 39/39 package-wide).

## What was built

- `src/client/shared/Spaarke.Communication.Components/src/components/EmailViewSelector/useEmailViews.ts` — host data hook. On mount calls `IDataverseClient.retrieveSavedQueriesForEntity('sprk_communication')`, maps `SavedQuerySummary[]` → `SavedView[]`, resolves the default selection to the "Email — Inbox" saved view by name (case/whitespace-insensitive), falling back to the entity's `isDefault` view, then `views[0]`, and finally a descriptive `error` (never a crash) if no views resolve at all. A second effect re-runs the selected view's FetchXML — **unmodified** — via `retrieveSavedQuery` → `retrieveMultipleRecords` whenever `selectedViewId` changes, exposing `{ views, selectedViewId, setSelectedViewId, rows, isLoading, error }`.
- `.../EmailViewSelector/EmailViewSelector.tsx` — thin, fully-controlled container. Renders the REUSED `DataGridViewSelector` (`@spaarke/ui-components`, ADR-012 — not forked) plus an FR-19 `MessageBar` error affordance when `error` is set (in place of the picker) and a small inline `Spinner` while `isLoading`. Does NOT call `IDataverseClient` itself — the host wires it against `useEmailViews`'s return value.
- `.../EmailViewSelector/index.ts` — folder barrel.
- `.../EmailViewSelector/__tests__/useEmailViews.test.ts` — 6 tests: defaults to "Email — Inbox"; falls back to the entity default view when absent; view switch re-runs the new view's FetchXML unmodified and re-populates `rows`; view-list load failure surfaces via `error` (never throws); zero saved views surfaces a descriptive `error` (the escalation-worthy condition, handled gracefully rather than thrown); a FetchXML run failure surfaces via `error`.
- `.../EmailViewSelector/__tests__/EmailViewSelector.test.tsx` — 4 tests: renders the reused picker with the active view label; emits `onViewChange` when a different view is picked from the menu; renders the error banner in place of the picker when `error` is set; renders legibly under `webDarkTheme` (ADR-021).
- `src/client/shared/Spaarke.Communication.Components/src/components/index.ts` — appended `export * from './EmailViewSelector';` (one-line barrel addition only, coordinated with sibling task's concurrent `EmailReadingPaneShell` barrel addition — both landed cleanly, verified by re-reading the file post-edit).

## Design decision: `rows` are raw Dataverse records, not pre-mapped `EmailCardItem[]`

`useEmailViews`'s `rows` are typed `ReadonlyArray<T>` (default `Record<string, unknown>`), i.e. whatever columns the maker-authored saved view's FetchXML selects — **not** pre-mapped to `EmailCardList`'s `EmailCardItem` shape. This mirrors the "host owns mapping" boundary already documented on `EmailCardList.types.ts` itself ("the host... fetches `sprk_communication` rows... and supplies the already-loaded rows as `items`"). Reasons this task did not also do the raw→`EmailCardItem` mapping:

1. The exact Dataverse column names available in `rows` depend on which attributes each maker-authored view selects — `useEmailViews` has no entity-metadata call in scope to verify column presence, so a hardcoded mapper would silently break if a view omits an expected column.
2. `docs/data-model/sprk_communication.md` does not document a "preview" (plain-text body-summary) or "unread" field — the EmailCardItem docstring is explicit that `preview` must come from Graph's `bodyPreview` (or be routed through `sanitizeEmailHtml` first if derived from HTML), which is outside this task's fetch-and-refetch scope.
3. The mapping/render responsibility explicitly sits with the ReadingPaneShell host (task 032, a sibling task in this same wave) per the `EmailCardList.types.ts` docstring's own attribution ("task 031 view wiring → task 032 reading-pane shell").

`useEmailViews` is generic (`useEmailViews<T>`) so a host that already knows its exact FetchXML columns can supply `T = EmailCardItem` (or any shape) without a code change here.

## List/Thread toggle — OMITTED, deferred

Per the task's "cheap-only" instruction (spec Assumptions: included only if inexpensive), the optional "View by List / View by Thread" toggle was **omitted**. Rationale:

1. Grouping by `sprk_communicationthread` requires that column to be present in the fetched row shape — not guaranteed by an arbitrary maker-authored saved view's FetchXML (the view might not select it), so a generic grouping utility here could silently no-op or crash on a subset of views.
2. Rendering grouped threads meaningfully (thread headers, collapse/expand) is presentation work, not "trivial grouping code" — it would either duplicate `EmailCardList` rendering logic (violates ADR-012 reuse-not-fork) or require touching `EmailCardList`/`ReadingPaneShell`, both explicitly out of this task's guardrails (concurrent sibling ownership this wave).
3. Given (1) and (2), the toggle fails the "reuses the already-fetched rows with no extra query and trivial grouping code" bar from the task constraint — it is a real feature addition, not a cheap add-on.

Filed via the project's defer-issue-tracking obligation (CLAUDE.md "Deferrals & Issues"): see `notes/defer-issues.md` (or file with `/defer` if not yet present) for the synced GitHub Issue.

## Quality gates (Step 9.5 — explicitly requested despite STANDARD rigor's default skip)

- `code-review`: Clean — 0 Critical, 0 Warning. 1 Suggestion (non-blocking): a one-render-frame window where neither `isLoadingViews` nor `isLoadingRows` is true can occur between the view-list effect resolving and the row-fetch effect starting; left as-is because defaulting `isLoadingRows` to `true` would make the "no saved views" error state appear permanently stuck loading.
- `adr-check`: Clean — 0 Violations, 0 Warnings across ADR-012 (reuse, not forked), ADR-021 (Fluent v9 tokens only, no hex colors, no v8 imports), ADR-022/NFR-05 (grep-confirmed zero `as React.ComponentType` casts), ADR-028 (all Dataverse access via the injected `IDataverseClient`; zero `Xrm.WebApi`/raw `fetch`/`Authorization` usage).
