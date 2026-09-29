# Infinite Lazy-Scroll Lists (the Spaarke standard for scrollable collections)

> **Last Reviewed**: 2026-08-31
> **Status**: Current
> **Governed by**: [ADR-051](../../adr/ADR-051-infinite-scroll-lists.md) — read it for the binding MUST / MUST NOT.
> **Recurs often** — this is the canonical answer to "how should a list load more rows?" and "why does my grid stop at 25?".

## When

ANY Spaarke surface that renders a scrollable list / record collection: dataset grids (Documents, reconciliation Needs-Review, Calendar events), timeline / activity feeds, chat message lists, widget list bodies, picker result lists. The rule is one interaction model — **scroll** — for every list.

## The decision in one line

**Load rows progressively as the user scrolls (infinite lazy-scroll) + the canonical thin scrollbar. NEVER a pager.**

## DO NOT (the explicit bans — ADR-051)

- ❌ **DO NOT** add a **down-arrow / chevron "next page"** button, a **prev/next** control, **numbered page** links, or a **"Load more"** button. Scrolling is the ONLY way to reach more rows.
- ❌ **DO NOT** cap a list at its first page. "It shows 25 and there are 100" is a **bug**, not a design (see the trap below).
- ❌ **DO NOT** load the entire set in one giant page (e.g. `pageSize=500`) to "show all" — that renders everything at once and hides a broken `hasMore`. Page incrementally.
- ❌ **DO NOT** hand-roll `::-webkit-scrollbar` rules or a hex thumb — use the canonical [`thinScrollbarStyle`](thin-scrollbar.md).

## The one source of truth: `<DataGrid>`

For anything Dataverse-backed, the shared **`<DataGrid configId=… />`** (`@spaarke/ui-components`) IS the standard scrollable-list component. It ships infinite lazy-scroll + the thin scrollbar with zero per-surface code:

- **`useLazyLoad`** (internal) chains FetchXML pages via the paging cookie / `page`+`count`, accumulating rows.
- An **`IntersectionObserver`** on a bottom **sentinel** `<div>` fires `fetchNextPage()` ~200px before it enters the viewport (`rootMargin: '200px'`), so the next page is already loading as you approach the end.
- The scroll container (`gridScroll`) spreads **`thinScrollbarStyle`**.

```tsx
// A reconciliation / documents / any Dataverse list — infinite scroll + thin scrollbar,
// nothing else to wire. pageSize ~25–50 keeps scrolling smooth with few round-trips.
<DataGrid configId={NEEDS_REVIEW_CONFIG_ID} dataverseClient={client} pageSize={50} />
```

**Default to reuse (§11 / ADR-012).** Reach for a custom scroller only when the data genuinely is not a Dataverse collection and `<DataGrid>` cannot host it — and then replicate the three mechanics below.

## The trap: MDA `Xrm.WebApi` strips the "more records" signal

`useLazyLoad` derives `hasMore` from the server. Under the **MDA `Xrm.WebApi`** client, a FetchXML query with injected `page`/`count` **returns the right rows per page** but the JS result object **omits** `@Microsoft.Dynamics.CRM.morerecords` and `@Microsoft.Dynamics.CRM.fetchxmlpagingcookie`. So a `moreRecords`-only `hasMore` is **always false** → the observer never fires → the list is silently capped at page 1 (the classic "shows only 25").

**The fix (shipped in `useLazyLoad`): fall back to PAGE FULLNESS.**

```ts
// A page filled to pageSize almost certainly has a successor; a short/empty page is the end.
// (One harmless empty fetch at an exact-multiple boundary.) BFF clients that DO return
// moreRecords are unaffected — the flag still short-circuits true.
const looksFull = pageSize > 0 && result.entities.length >= pageSize;
setHasMore(result.moreRecords === true || looksFull);
```

This is why the standard is robust across hosts: it does not depend on an annotation the platform may strip.

## Custom scroller (last resort) — replicate exactly three mechanics

1. **Progressive fetch** with a page-fullness `hasMore` (the snippet above — or reuse the `useLazyLoad` shape).
2. **`IntersectionObserver` on a bottom sentinel**, rooted on the scroll container:
   ```ts
   const obs = new IntersectionObserver(
     (e) => e[0].isIntersecting && hasMore && !loading && fetchNextPage(),
     { root: scrollContainerRef.current, rootMargin: '200px', threshold: 0 },
   );
   obs.observe(sentinelRef.current);
   return () => obs.disconnect();
   ```
3. **Thin scrollbar** on the actual `overflow:auto` element: `{ overflowY: 'auto', ...thinScrollbarStyle }` (see [thin-scrollbar.md](thin-scrollbar.md); the `::-webkit-scrollbar` pseudo-elements do NOT cascade — annotate the real scroller).

## Verify it (don't assume the framework "just works" in a new host)

When a list can exceed one page, **confirm scroll advances past page 1** — the trap above is host-specific and passed the framework's own tests (mock clients returned `moreRecords`). Check that the footer reads `Rows: N+` (the `+` = `hasMore`) and that scrolling to the bottom grows the row count.

## Live examples

- Standard consumer: reconciliation Needs-Review grid — `ReconciliationWorkspace` passes `pageSize={50}` to `<DataGrid>`; 100 rows reachable by scroll.
- Engine: `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/useLazyLoad.ts` (page-fullness `hasMore`) + `DataGrid.tsx` (sentinel `IntersectionObserver`, `gridScroll` slot).
- Test: `DataGrid/__tests__/useLazyLoad.hasMore.test.ts` (full→more, short→stop, paginate-then-stop, honor explicit `moreRecords`).

## Related

- [ADR-051](../../adr/ADR-051-infinite-scroll-lists.md) — the binding decision.
- [`thin-scrollbar.md`](thin-scrollbar.md) — the scrollbar half of the standard (`thinScrollbarStyle` / `thinScrollbarDescendantStyle`).
- [`fluent-v9-theming.md`](fluent-v9-theming.md) — semantic tokens + dark-mode resolution.
- [`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md) — the DataGrid framework this builds on.
