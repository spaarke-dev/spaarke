# ADR-051: Scrollable Lists Use Infinite Lazy-Scroll, Not Pagination (Concise)

> **Status**: Accepted (2026-08-31)
> **Domain**: UI/UX — Lists & Scrolling
> **Project**: email-communication-intelligence-r2 (UAT round 5, item 1)
> **References**: strengthens [ADR-021](ADR-021-fluent-design-system.md) (Fluent v9 semantic tokens); composes under [ADR-012](ADR-012-shared-components.md) (shared components); implemented by the DataGrid framework ([`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md)). Patterns: [`.claude/patterns/ui/infinite-scroll-list.md`](../patterns/ui/infinite-scroll-list.md) + [`.claude/patterns/ui/thin-scrollbar.md`](../patterns/ui/thin-scrollbar.md)

---

## Decision

Every Spaarke scrollable list / record collection uses **infinite lazy-scroll** — rows load progressively as the reviewer scrolls toward the bottom — paired with the **canonical thin, theme-aware scrollbar**. There is **no pager of any kind**: no numbered pages, no prev/next buttons, no "Load more" button, no down-arrow / chevron "next page" affordance.

The shared **`<DataGrid>`** (`@spaarke/ui-components`) is the standard implementation and the default choice for any Dataverse-backed list. It already provides this end-to-end: its internal `useLazyLoad` hook chains FetchXML pages, an `IntersectionObserver` on a bottom sentinel fetches the next page ~200px before it enters view, and its scroll container spreads `thinScrollbarStyle`. A custom (non-DataGrid) scroller is a last resort and MUST replicate the same three mechanics.

This resolves the "list shows only the first 25 / *is that all there is?*" failure: the MDA `Xrm.WebApi` client **respects** the injected FetchXML `page`/`count` but **strips** the `@Microsoft.Dynamics.CRM.morerecords` + paging-cookie annotations, so the old `moreRecords`-only `hasMore` was always `false` and scroll never advanced past page 1. `hasMore` now falls back to **page fullness** (a page filled to `pageSize` has a successor; a short/empty page is the end).

---

## Constraints

### MUST

- **MUST** use `<DataGrid configId=… />` for Dataverse-backed list/collection UI — it delivers infinite lazy-scroll + the thin scrollbar out of the box (no per-surface scroll code).
- **MUST**, for any non-DataGrid scroller, load progressively via an `IntersectionObserver` on a bottom sentinel + a **page-fullness `hasMore`** rule (full page ⇒ more; short/empty page ⇒ end), and apply `thinScrollbarStyle` to the actual `overflow:auto` element.
- **MUST** infer `hasMore` from page fullness when the data source omits the paging annotation. The MDA `Xrm.WebApi` FetchXML path does this; BFF clients that DO return `moreRecords` keep working (the rule is `moreRecords === true || page-was-full`).
- **MUST** apply the canonical `thinScrollbarStyle` (one scroller) or `thinScrollbarDescendantStyle` (a surface root) from `@spaarke/ui-components` — semantic tokens only (`colorNeutralStroke1`), dark-mode-safe (ADR-021).
- **MUST** verify infinite scroll actually advances past page 1 when a list can exceed one page (the historical regression) — don't assume the framework "just works" in a new host.
- **MUST** page incrementally with a sane `pageSize` (≈25–50) so the whole set is reachable by scrolling.

### MUST NOT

- **MUST NOT** paginate a list with **numbered pages, prev/next controls, a "Load more" button, or a down-arrow / chevron "next page"** affordance. Scrolling is the one and only navigation for a list.
- **MUST NOT** cap a list at its first page (the "shows only 25" bug) — a bounded page size with no lazy-load-more is a defect, not a design.
- **MUST NOT** substitute a giant single page (e.g. `pageSize=500`) for real lazy scroll — that renders the whole set at once and masks a broken `hasMore`; page incrementally instead.
- **MUST NOT** hand-roll `::-webkit-scrollbar` rules or a hex thumb on a new scroller — spread the canonical token-based style (a bespoke copy breaks dark mode and drifts).
- **MUST NOT** rely on the `@Microsoft.Dynamics.CRM.morerecords` annotation alone for `hasMore` under the MDA `Xrm.WebApi` client — it is stripped there; use the page-fullness fallback.

---

## Key patterns

```tsx
// The standard: a Dataverse list is a <DataGrid>. Infinite scroll + thin scrollbar
// are built in — the host only supplies config + data seam.
<DataGrid configId={NEEDS_REVIEW_CONFIG_ID} dataverseClient={client} pageSize={50} />
```

```ts
// The engine (DataGrid's internal useLazyLoad): hasMore falls back to page fullness
// when the server flag is absent (MDA Xrm.WebApi strips it on FetchXML).
const looksFull = pageSize > 0 && result.entities.length >= pageSize;
setHasMore(result.moreRecords === true || looksFull);
```

```ts
// Custom scroller (last resort): IntersectionObserver on a bottom sentinel.
const obs = new IntersectionObserver(
  (e) => e[0].isIntersecting && hasMore && !loading && fetchNextPage(),
  { root: scrollContainerRef.current, rootMargin: '200px' },
);
obs.observe(sentinelRef.current);
// …and the scroll container: { overflowY: 'auto', ...thinScrollbarStyle }
```

---

## Rationale

One interaction model — **scroll** — across every list, matching modern app + OOB Power Apps behavior; no mode-switch between "scroll a bit" and "click page 2". The page-fullness `hasMore` fallback removes a silent, host-specific trap (annotation stripping) that had capped MDA grids at page 1. The canonical token-based scrollbar reads native and resolves correctly in light + dark without per-surface CSS. Reusing `<DataGrid>` means new lists inherit all of this for free (ADR-012), instead of each surface re-inventing paging + scrollbar chrome and drifting.
