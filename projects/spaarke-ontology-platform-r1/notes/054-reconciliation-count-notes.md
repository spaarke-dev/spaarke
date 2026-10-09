# Task 054 - reconciliation tab and aggregate item: notes

Rigor FULL (POML said STANDARD; the task adds .ts/.tsx, so raised). Model sonnet @ medium. Amended POML (520b53a7b) followed.

## Verified, nothing added
- `communications-reconciliation` is registered exactly once (`register-workspace-widgets.ts` ~:1000-1033) and mounts
  `ReconciliationWorkspaceWidget`; `reconciliationRegistration` also exists in LegalWorkspace `sectionRegistry.ts:139`.
  No second registration added. `register-workspace-widgets.test.ts`: 65/65 pass (Spaarke.AI.Widgets).

## Delivered (Spaarke.Communication.Components, `components/ReconciliationGrid/`)
- `needsReviewCount.ts`: `loadNeedsReviewCount` / `useNeedsReviewCount` / `buildCountFetchXml`. Reads the SAME
  `sprk_gridconfiguration` record (`NEEDS_REVIEW_CONFIG_ID`) the tab's DataGrid reads, and runs its `source.fetchXml` as an
  aggregate count (entity, filter, link-entities verbatim; attributes/order/paging dropped). Null (rendered Missing) on any failure.
- `ReconciliationAggregateCard.tsx`: thin wrapper over the 057 `AggregateCard`, link "Open Email Review".
- The tab was NOT changed (no rebuild). "By construction" = same config record, same filter; tested on a shared fixture with
  a FetchXML evaluator (inactive, outgoing, null-status and resolved rows included).
- Task 059 places the card (top of the Do lane, outside lane counts, hidden under a Do filter) and supplies `onOpen`.

## Deviations from the original POML
- No `reconciliation.registration.ts` in SpaarkeAi (registration already exists).
- Count source is the Needs Review grid config, not CommunicationQueueFeedService (queue-feed Total capped at 200, different filter).
- Placement criteria moved to 059; misresolution/countsTowardSuppression moved to 034.
- Not done: browser/UI tests (no deployed env); dark mode asserted by rendering under webDarkTheme only.

## Known limit
- Aggregate counts are capped by Dataverse at 50,000 rows (K2).
- Count reflects the config's FetchXML only; a user-applied grid filter/chip in the tab does not change the card (by design: total).

## Round 2 (#1531 review)
- F2 fixed the preferred way: `fetchConfigRecord` / `resolveSource` / `extractEntityFromFetchXML` moved VERBATIM from DataGrid.tsx
  to `DataGrid/resolveGridSource.ts` (exported from the DataGrid barrel); DataGrid imports them, `loadNeedsReviewCount` calls them.
  Schema validation and savedquery / savedquery-set sources now behave the same for card and tab. A config with a membership or
  parent-context overlay (which the card cannot reproduce) or a `distinct` fetch resolves to Missing. DataGrid suites: 83/83 pass.
- F3: loading renders a neutral spinner, not "Missing". Pluralisation ("1 email awaits"). Hook/prop docs: client must be memoised.
- Known limit (K2): a user's filter chip in the tab does not change the card (total by design).
