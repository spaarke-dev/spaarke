# Task 034 — route-paging escalation (BLOCKING)

> **Rigor**: FULL · **Model tier**: sonnet @ high · **Step mode**: DIRECTIONAL
> **Status**: escalated per the orchestrator's binding correction #2, before any lazy-scroll code was
> written. No `FindResultsList.tsx` / `useLazyResults.ts` implementation exists in this worktree.

## 🔔 Human Input Required

- **What was checked**: whether `GET /api/ai/visualization/related/{documentId}`
  (`src/server/api/Sprk.Bff.Api/Api/Ai/VisualizationEndpoints.cs:48-59`) — the per-row-authorized
  surface task 032 hardened, and the ONLY source for Find's primary "similar documents" results —
  supports paging (skip/top or a cursor), as required before designing this task's lazy-scroll list.
- **Finding**: **it does not.** `VisualizationQueryParameters`
  (`VisualizationEndpoints.cs:616-681`) exposes `threshold`, `limit` (clamped
  `Math.Clamp(query.Limit ?? 25, 1, 50)` at `:153`), `depth`, `includeKeywords`, `documentTypes`,
  `includeParentEntity`, `relationshipTypes`, `countOnly` — **no `skip`/`offset`/`cursor`/
  `continuationToken` parameter exists anywhere in the query model, `IVisualizationService`, or
  `VisualizationService`** (`grep -n "skip|cursor|continuationToken|Offset|pageToken" -i` over
  `Services/Ai/Visualization/*.cs` returns zero matches for any of those concepts — the two hits are
  unrelated code comments/property names, verified by reading each). The route computes ONE bounded
  graph per call (five hardcoded relationship queries at `TopCount = 50` each, further trimmed by
  task 032's `AuthorizeRowsAsync` to at most `MaxDocumentAuthorizationChecks` (100) evaluated rows —
  see `notes/032-authorization-hardening.md` §4-§5 and `notes/033-find-view-index-gating-decisions.md`
  §5) and returns it in full. There is no way to ask this route for "the next page" of a graph already
  computed — the request re-derives from scratch every time and has no parameter that would change
  which rows come back beyond re-running the whole similarity + hub-topology + authorization pipeline
  with a different `limit`.
- **Why this blocks the task as scoped**: ADR-051 (`.claude/adr/ADR-051-infinite-scroll-lists.md`)
  bans BOTH failure modes this creates:
  - **MUST NOT** "substitute a giant single page... for real lazy scroll" — but the route only ever
    returns one shot, up to `limit` (≤50) rows, so anything client-side that LOOKS like lazy-scroll
    over it is by construction rendering the entire set the server will ever give for that request.
  - **MUST NOT** invent a pager — but there is no server-side "more" to page toward, so a client that
    tried to synthesize `hasMore=true` and "fetch page 2" would have nothing real to fetch: calling the
    route again with the same `documentId` and the same `limit` returns the same bounded set (modulo
    embedding/authorization non-determinism), not a continuation.
  - The `.claude/patterns/ui/infinite-scroll-list.md` "page-fullness `hasMore`" fallback
    (`result.entities.length >= pageSize` ⇒ assume a successor) is written for data sources that CAN
    be asked for the next page (FetchXML `page`/`count`, or a BFF client that returns `moreRecords`).
    Applying it here would produce a `hasMore=true` signal with no route capable of honoring the
    resulting fetch — a UI lie, not a fallback.
- **Two ways to comply with ADR-051 that the operator's corrections explicitly forbid me from picking
  unilaterally**:
  - **(a) Invent client-side pseudo-pagination** over the single response (e.g., fetch once, then
    reveal rows to the DOM in chunks as the user scrolls, faking `IntersectionObserver`-driven "page"
    advances over data that already fully arrived). This satisfies the letter of "scroll reveals more
    rows" but not the spirit — ADR-051's rationale is about **fetching progressively to avoid loading
    (and authorizing) more than the viewport needs**, and task 032's per-row authorization is exactly
    the workload this route already limits to a 100-row budget per request specifically so it is not
    paid ahead of need.
  - **(b) Add server-side paging** (a `skip`/`offset` or cursor) to this route. It is explicitly
    security-hardened (task 032, D-032-2 in task 033) with a fixed authorization budget; adding paging
    changes its request/response contract and its authorization-cost profile (repeated calls at
    increasing offsets would re-run the relationship queries + re-authorize overlapping candidate sets
    unless a cursor scheme is designed to avoid it) — a BFF change of exactly the kind CLAUDE.md §10
    requires be placed and justified deliberately, not improvised inside a client-focused task.

## Proposed paths (per CLAUDE.md §6.5 — this is an ADR-051 tension, not a CLAUDE.md ADR conflict in the
strict §6.5 sense, since ADR-051 itself is not what's in question; the underlying BFF contract is.
Framed here in the closest equivalent shape since the corrections asked for "the §11 evidence" and a
human decision)

- **Path 1 — (b), scoped**: add real paging to `GET /api/ai/visualization/related/{documentId}`
  (e.g., `skip`/`top`, keeping `limit` as the page size and adding `skip` as the offset into the
  SAME five-relationship-query candidate set, re-authorizing only the new page's distinct ids). This
  is the only path that gives `useLazyResults` an honest `hasMore`. Cost: a BFF contract change to a
  security-hardened route, a new/updated contract-test file per `bff-extensions.md` §F, a publish-size
  re-measurement, and design time to decide whether `skip` re-walks the whole candidate list (simple,
  but repeats work already paid once per request) or the route caches/memoizes the candidate set for
  the request's `documentId`+options tuple (more work, avoids repeat cost).
- **Path 2 — (a), scoped and labeled honestly**: render the single bounded response (≤50 rows, further
  authorization-trimmed) with **no `hasMore` beyond what the response's own metadata proves** — i.e.
  treat the one response as the complete first-and-only page, use `IntersectionObserver` purely as a
  **progressive-reveal** mechanic for the rows already in hand (not a re-fetch trigger), and make
  `hasMore` permanently `false` for this list (the D-032-2 `PARTIAL_RESULTS` warning already tells the
  user when the 100-row authorization budget truncated the candidate set — that is the honest
  "there may be more, but not more we can fetch from here" signal, not a `hasMore` the UI can act on).
  This complies with the LETTER of "no pager, no giant page to avoid paging" only if the operator
  agrees the *absence* of a next-page action is a route limitation being surfaced honestly (the
  `PARTIAL_RESULTS` `MessageBar` already shipped in task 033), not a masked defect. This is the
  reading closest to (b) documents-only in the F-c note above, and keeps this task entirely
  client-side (zero BFF changes, zero publish-size impact).
- **Path 3 — defer**: descope lazy-scroll from this task entirely, ship the results list as the single
  bounded response with the existing D-032-2 warning (already shipped in task 033), and open a
  follow-on task once Path 1's BFF design is chosen.

## Recommendation

**Path 2**, with Path 1 as a named follow-on if the operator wants true paging later. Rationale: the
route's own ceiling (`limit` clamped to 50, further trimmed by a 100-row authorization budget) is
already a small, bounded result set for a "find similar documents to the one you have open" feature —
not a large collection a user would expect to page through the way they would a document list or an
inbox. `PARTIAL_RESULTS` already gives an honest signal when the candidate set exceeded what could be
evaluated. Path 1 is the more "correct" long-term shape but is a BFF-contract change this task was not
scoped or authorized to make unilaterally (per CLAUDE.md §10 and the operator's explicit "STOP...
BEFORE... (b) adding server-side paging to this security-hardened route").

## What is NOT done because of this block

- `FindResultsList.tsx` and `useLazyResults.ts` have **not** been written. Writing either before this
  is resolved would mean committing to one of the two forbidden shapes ((a) or (b) as originally
  conceived) or silently pre-deciding Path 2 without the operator's sign-off it was asked to have.
- `FindView.tsx` has **not** been touched.
- No test files for the results list exist yet.

Everything else in the task (the F-c records-bridge decision, this evidence-gathering) is complete
and committed independently, since it does not depend on this blocker's resolution.
