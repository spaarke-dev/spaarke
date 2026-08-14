# Task 070 — Search bar / quick-switcher (FR-11)

**Status**: shipped. NavigatorPane suite: 139/139 passing (125 prior + 14 new
QuickSwitcher tests — 13 initial + 1 added during self-review to lock in the
OData-encoding fix). `tsc --noEmit` clean. `npm run build` (Vite) green;
verified the built `dist/index.html` contains the new QuickSwitcher string.

## Files

- **NEW** `src/solutions/NavigatorPane/src/services/navigatorSearchIndex.ts` — the shared in-memory index (module store, not React state/context).
- **NEW** `src/solutions/NavigatorPane/src/services/liveSearchService.ts` — the live Xrm.WebApi/ViewService escalation.
- **NEW** `src/solutions/NavigatorPane/src/components/QuickSwitcher.tsx` — the search box + results dropdown.
- **NEW** `src/solutions/NavigatorPane/src/components/__tests__/QuickSwitcher.test.tsx`.
- **MODIFIED** `src/solutions/NavigatorPane/src/NavigatorBody.tsx` — swaps the task-040 placeholder `Input` for `<QuickSwitcher/>`; keeps the existing info-icon `Tooltip` (portal re-wrap, ADR-021) with updated copy.
- **MODIFIED** `src/solutions/NavigatorPane/src/tabs/RecentTab.tsx` / `PinnedTab.tsx` / `ViewsTab.tsx` — each reports its already-loaded (and, for Recent/Pinned, already-080-trimmed) rows into `navigatorSearchIndex.ts` right after its existing `setRows`/`setViews` call. No new query is issued by any of the three tabs.
- **MODIFIED** `src/solutions/NavigatorPane/__tests__/NavigatorBody.test.tsx` — the `navigator-search-placeholder` assertion is replaced with `navigator-quickswitcher-input` (the placeholder this task's own dispatch note said would be replaced); everything else in that file (tab switching, light/dark portal re-wrap of the info tooltip, `--sprk-ui-scale`, no-Xrm empty state) is unchanged and still passes.

## Shared-index approach (no duplicate queries, no trim bypass)

`NavigatorBody.tsx` mounts exactly ONE tab panel at a time (`selectedTab === 'recent' ? <RecentTab/> : ...` — task 040, unchanged by this task; `NavigatorBody.test.tsx`'s `render_TabClick_SwitchesActivePanel` asserts the non-selected panel is NOT in the DOM, so this behavior is locked in and was not touched). Lifting all three tabs' loads into `NavigatorBody` (or always-mounting all three tab panels) would have been the "obvious" fix but either (a) breaks that locked-in test, or (b) is a much larger refactor than this task's scope justifies.

Instead, `navigatorSearchIndex.ts` is a **module-scoped store** (`useSyncExternalStore`-backed hook for React consumers), not component state:

- `RecentTab.tsx` calls `setRecentSearchEntries(accessible.map(rowToSearchEntry))` right after its existing `setRows(accessible)` in the Viewed-history load — `accessible` is the SAME array already filtered by `securityTrimService.classifyTargets` (task 080). No second trim pass, no re-derivation from raw rows.
- `PinnedTab.tsx` calls `setPinnedSearchEntries(trimmed.map(rowToSearchEntry))` right after `setRows(trimmed)` in `loadPins()` — covers BOTH the Records and Bookmarks groups (same `rows` source PinnedTab itself partitions client-side). The Monitored group is deliberately NOT indexed — it's a live query, separately loaded, and PinnedTab.tsx's own docblock already treats it as "NEVER merged into Records"; extending that same never-merge precedent to search keeps this task's diff smaller. A future task could add it (map `MonitoredItem` -> `SearchIndexEntry`) with no risk, since it's already access-live (see `securityTrimService.ts`'s exemption reasoning).
- `ViewsTab.tsx` calls `setViewSearchEntries(userQueries.map(viewToSearchEntry))` right after `setViews(userQueries)`.

Being module-scoped (not React state owned by `NavigatorBody`) means the aggregated entries **survive a tab's unmount** when the user switches away — once a tab has loaded at least once this pane session, its entries stay locally searchable.

**Documented tradeoff — "progressive availability"**: Recent is the default active tab, so its entries are searchable the instant the pane opens. Pinned/Views entries only become locally searchable once the user has visited that tab at least once in the session (their `useEffect` load hasn't fired yet otherwise). Until then, a query that would have matched a not-yet-loaded Pinned/Views entry finds zero LOCAL hits and correctly escalates to the live lookup (`liveSearchService.ts`) — which is FR-11's specified fallback path, not a broken case, so the user still finds the record/view, just via the live path instead of instantly. This is a deliberate, documented scope-narrowing (Path A-style) rather than the literal "always index Recent+Pinned+Views regardless of tab visits" reading of FR-11; flagged during self-review for the record. A full fix (proactively pre-warming Pinned/Views' loaders on `NavigatorBody` mount without visually mounting those tabs) is a larger refactor better suited to a follow-up task if this proves insufficient in practice.

## Keyboard accelerator

**Ctrl+K** (Cmd+K on macOS), registered via a `document`-level `keydown` listener in `QuickSwitcher.tsx`. Chosen over `/`: NavigatorPane is a single `webresource` iframe occupying the whole side pane (ADR-006) with no nested iframe inside it, so a document-level listener reaches every element the pane ever renders — satisfying "from anywhere in the pane." `/` was rejected because it would fire while the user is typing in any OTHER Navigator text input (e.g. `PinnedTab.tsx`'s "Paste or type a link to bookmark" field), stealing keystrokes instead of being a safe global accelerator.

**Runtime assumption** (per the task's own escalation-adjacent dispatch note, which explicitly permits implementing + unit-testing without live-UCI verification): this is verified by `QuickSwitcher.test.tsx`'s `keydown_CtrlKAnywhereInDocument_FocusesSearchBox` test (dispatches the keydown on `document` with focus elsewhere) but has NOT been confirmed against the live UCI's actual iframe/focus boundary at runtime. If a future runtime check shows the listener doesn't receive the event from inside the real MDA shell (e.g. a Fluent focus trap elsewhere intercepts it first), that is the trigger condition for this task's `<escalation>` block and should be raised at that point — not assumed away now.

Results list keyboard navigation: ArrowUp/ArrowDown move a virtual `activeIndex` highlight (not DOM focus — the input keeps focus, standard ARIA 1.2 combobox pattern); Enter activates the highlighted option (defaults to index 0 — the top result — since `activeIndex` resets to 0 whenever the visible result set changes); Escape clears the query. `aria-activedescendant` on the input is wired to the highlighted option's `id` (added during self-review — see Quality gates below) so screen readers announce which option is active without focus leaving the input.

## Local-first -> escalate-on-miss flow + navigation

1. As the user types, `filterLocalEntries()` fuzzy-scores every entry in the CURRENT `useNavigatorSearchIndex()` snapshot synchronously (in-memory, no network) — a contiguous substring match scores highest; an in-order subsequence match (fuzzy) scores lower but still counts.
2. Only when that produces ZERO matches for a non-empty query does a 300ms-debounced `liveSearch()` fire — `liveSearchRecords()` (per-entity `Xrm.WebApi` `contains()` query over `editedByMeService.CORE_ENTITY_SET`, reused rather than a second hardcoded entity list) + `liveSearchViews()` (`ViewService.getAllUserQueries()`, filtered client-side by name substring). Live results render with a "Live · <chip>" badge so they're visually distinct from local hits.
3. Enter/click navigation, by target kind:
   - `entityrecord` / `entitylist` -> `Xrm.Navigation.navigateTo(...)` (never a raw URL).
   - `weblink` -> `window.open(url, '_blank', 'noopener')` — per the task's explicit HARD CONSTRAINT, matching `PinnedTab.tsx`'s existing Bookmarks-group convention.
   - **Known inconsistency, informational only**: `RecentTab.tsx`'s own row-click for a `WebLink`-pagetype row calls `xrm.Navigation.openUrl(row.sprk_url)` (its task-041 behavior, unchanged by this task), not `window.open`. So the SAME row now navigates differently depending on whether it's clicked directly in the Recent tab vs. found via QuickSwitcher. This diff followed the task's literal, explicit instruction ("Only true weblinks use `window.open(url,'_blank','noopener')`," matching PinnedTab's convention) rather than RecentTab's `openUrl` convention. Surfaced here for reviewer awareness; not changed without a decision, since RecentTab's `navigateToRow` is out of this task's file list.

## Quality gates (Step 9.5)

Self-run `code-review` + `adr-check` (Sonnet, this task) against the diff. Two concrete findings were identified and FIXED before considering the task done:

1. **Warning (correctness) — fixed.** `liveSearchService.ts` originally escaped OData single-quotes (`'` -> `''`) but never percent-encoded the resulting literal before splicing it into the `retrieveMultipleRecords` `options` query string. A search query containing `&`, `#`, or `%` (e.g. "Smith & Co") would have its `&` read as an OData query-option separator, truncating `$filter` mid-clause — the live escalation would silently return zero results for an otherwise-accessible, existing record. **Fix**: `encodeODataStringLiteral()` now `encodeURIComponent`s the escaped value. Locked in by a new test, `type_QueryContainingAmpersand_PercentEncodesTheODataFilterValue`.
2. **Warning (WCAG 2.1 AA) — fixed.** The combobox/listbox/option ARIA pattern was missing `aria-activedescendant` on the input, so a screen-reader user had no way to hear which result is currently highlighted via arrow keys (DOM focus never leaves the input — that's correct for this pattern, but the missing attribute meant no announcement). **Fix**: each result row now has a stable `id="navigator-quickswitcher-option-{index}"`; the input's `aria-activedescendant` points at the active one.
3. **Suggestion (informational, not fixed)** — the RecentTab-vs-QuickSwitcher weblink navigation inconsistency documented above.
4. **Suggestion (informational, not fixed)** — the "progressive availability" tradeoff documented above.

ADR-021: confirmed via grep — zero `@fluentui/react` (v8) imports, zero hardcoded hex colors, in the full task-070 diff; QuickSwitcher does not portal-render (no `Popover`/`Menu`/`Dialog`/`createPortal`), so it correctly does NOT need its own portal re-wrap; `NavigatorBody.tsx`'s existing info-icon `Tooltip` (which DOES portal-render) keeps its `FluentProvider applyStylesToPortals` re-wrap unchanged.

ADR-022: N/A for this diff — confirmed via `git status --short` scoped to `src/client/shared/Spaarke.UI.Components/**` that none of this task's Write/Edit calls touched that tree (the two untracked files that DO show there, `SidePane/__stub__/` + `SidePane/__tests__/stubContributor.test.tsx`, predate this task and were not created or modified by it).

## Verdict

**Shipped.** No `<escalation>` trigger fired (the keyboard-accelerator implementation is complete and unit-tested; only the live-UCI runtime confirmation is deferred, per the task's own explicit allowance for that). Two self-identified code-review findings were fixed in-task; two informational findings are documented above for a human/reviewer decision rather than fixed unilaterally, since fixing them would require touching files outside this task's declared scope (`RecentTab.tsx`'s `navigateToRow`) or a larger architectural change (proactive Pinned/Views pre-warming) not justified by "smallest refactor."
