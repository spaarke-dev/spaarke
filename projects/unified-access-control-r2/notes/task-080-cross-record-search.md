# Task 080 — Restore cross-record search by authorizing the PAGE

> **Status**: in progress · **Rigor** FULL · opus @ xhigh · deps 070
> **Owner decision that created this task** (2026-08-26): *"yes Spaarke offers cross-record search."*
> Task 070 refused `scope=all` on the premise that "there is no caller that needs it". That premise was
> FALSE and is corrected in [`task-070-gate-semantic-search.md`](task-070-gate-semantic-search.md) decision #1.

---

## 0. What the POML got wrong, established before writing any code

The POML was authored from a route/grep reading. Tracing the actual client changed three of its premises.
Recording these first because two of them would have produced the wrong implementation.

### 0.1 The dropdown's entity rows mostly do not reach this endpoint

The POML's table says *"Any entity row → `scope:'entity'` + `entityType`, no `entityId` → 400"*. That is
true only for a subset. [`App.tsx:121-136`](../../../src/client/code-pages/SemanticSearch/src/App.tsx#L121-L136)
`deriveSearchDomain` routes the selected row to one of two *different hooks*:

| Dropdown row | Domain | Hook | Endpoint | Status today |
|---|---|---|---|---|
| "All" | `documents` | `useSemanticSearch` | `POST /api/ai/search` | **403** (task 070) |
| blank label (data-quality) | `documents` | `useSemanticSearch` | `POST /api/ai/search` | **403** |
| `matter` / `project` / `invoice` | `matters`/`projects`/`invoices` | **`useRecordSearch`** | `POST /api/ai/search/records` | works — but that route is **UNGATED, task 077** |
| `document` / `event` / `workassignment` / future | `documents` | `useSemanticSearch` | `POST /api/ai/search` | **400** `ENTITY_ID_REQUIRED`, or **403** `ENTITY_TYPE_NOT_AUTHORIZABLE` if an envelope id is present |

So the three most prominent dropdown rows were never broken by task 070 — they go to **task 077's route**,
which is the one still leaking record names tenant-wide. Fixing 080 does not make the page safe on its own.

### 0.2 The main break is not a dropdown row at all

[`App.tsx:473-474`](../../../src/client/code-pages/SemanticSearch/src/App.tsx#L473-L474):

```ts
const scopeArg     = hasUserInitiatedSearch ? null : initialScope    || null;
const entityIdArg  = hasUserInitiatedSearch ? null : initialEntityId || null;
```

A page launched from a Matter form auto-searches **entity-scoped** (works today — `entityType=matter` +
`entityId` is exactly what task 070 authorizes). The moment the user **types a query**, `hasUserInitiatedSearch`
flips and scope drops to tenant-wide → `useSemanticSearch`'s legacy branch emits `scope:'all'`
([`useSemanticSearch.ts:294-298`](../../../src/client/code-pages/SemanticSearch/src/hooks/useSemanticSearch.ts#L294-L298))
→ **403**.

That is the highest-traffic broken path and the POML does not mention it. It was a deliberate
`multi-container-multi-index-r1` UAT fix (2026-06-09) — scope was made to *drop* on user-initiated search
because a persisted matter scope meant "user could only ever search within that matter".

### 0.3 "Supply the missing entityId" is the wrong fix

The POML constraint says *"Fix the missing `entityId` on the code page's entity-scoped rows too."* There is
no entityId to supply. Those rows mean **"search every document whose parent is of type X"** — a
cross-record search narrowed by TYPE, not a search within one record. `entityId` only ever arrives from the
URL envelope, i.e. when the page was launched *from* a specific record.

`SearchRequestFragment` ([`targetEntityNormalize.ts:79-86`](../../../src/client/code-pages/SemanticSearch/src/services/targetEntityNormalize.ts#L79-L86))
carries no `entityId` **by design**, and `useSemanticSearch.ts:265-271` says so explicitly: *"the dropdown is
the user selecting an index/scope, not narrowing to a specific record."*

**Correct fix**: those rows emit the *filtered cross-record* shape, not a record-scoped shape. Deviation from
the POML step 3 wording, permitted by `<steps mode="directional">`, and it satisfies the same acceptance
criterion by a mechanism that can actually work.

### 0.4 The type-narrowing vocabulary does not line up (pre-existing)

Three overlapping allow-lists, and no two agree:

| List | Values | Source |
|---|---|---|
| `ValidEntityTypes.All` (`filters.entityTypes`) | matter, project, invoice, **account**, **contact** | `SearchFilters.cs:63` |
| `AuthorizableParentEntitySets` (task 070) | matter, project, **workassignment**, invoice (+ `sprk_` forms) | `SemanticSearchAuthorizationFilter.cs:113` |
| Dropdown labels seen in `deriveSearchDomain` | matter, project, invoice, document, event, workassignment | `App.tsx:124-135` |

Consequences, both real:
- `filters.entityTypes = ['workassignment']` is a **400** `INVALID_ENTITY_TYPES` — so the naive client fix breaks.
- `account`/`contact`-parented rows are a valid *filter* target but have **no authorizable-parent mapping**, so
  under the fail-closed rule below they are **dropped**. See §4 open item O-1.

---

## 1. The paging contract (POML step 1 — decided BEFORE coding)

The POML calls paging "THE HARD PART … a caller paging through 10 pages of 20 may see 7 usable pages with
gaps." **That failure cannot occur on this path, because this path has no working pagination today.**

[`SemanticSearchService.cs:189`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SemanticSearchService.cs#L189):

```csharp
var totalResults = results.Count;   // NOT searchResults.TotalCount
```

That was a deliberate change: the raw index count is a **chunk** count (RAG docs are split), which produced
"12 found / 2 shown" mismatches. So on the AI-Search path `totalResults == returnedResults` **already**.
The client derives `hasMore = totalCount > results.length` ([`useSemanticSearch.ts:210`](../../../src/client/code-pages/SemanticSearch/src/hooks/useSemanticSearch.ts#L210)),
so after page 1 that is `20 > 20 = false`. **`loadMore` can never fire on `scope=all`.**

> Corpus-total paging DOES work on `SearchAssociatedOnlyAsync` (`:728 TotalResults = sortedAll.Count`), which
> is the `scope=entity` + `associatedOnly` path — parent-authorized, so untouched by this task. That
> asymmetry is exactly what task 070's `Math.Min` → `total - dropped` correction was about.

**Contract adopted:**

1. `scope=all` returns **one ranked page** of at most `limit` rows the caller may read.
2. `totalResults` is the count of **permitted rows in this response** — the same page-count semantics the
   unfiltered path already had. It never claims a corpus number the caller cannot reach.
3. `hasMore` stays false, exactly as before this change. **No paging regression, because there is no paging.**
4. Multi-page paging over a filtered corpus is **deliberately NOT introduced**. It would require cursor
   paging over the underlying index (offset arithmetic is unusable once rows are dropped post-fetch: the
   client's `offset: results.length` would re-request already-consumed index positions, producing the
   duplicate-then-gap pattern the acceptance criteria forbid). Recorded as follow-up F-1.

## 2. The real hazard this design must defeat: over-filtering, not under-filtering

Dropping rows after a relevance-ranked fetch introduces a **silent recall loss**. If the caller may read 3 of
50 matching documents but none of those 3 rank in the top 20, filtering yields **zero rows** and the response
is indistinguishable from "nothing matched". That is the acceptance criterion *"a caller entitled to 3 of 50
matches gets exactly 3"*, and a naive fetch-page-then-filter fails it.

**Mitigation — bounded over-fetch + an explicit incompleteness signal:**

- Fetch `min(limit × OverFetchFactor, OverFetchCap)` rows from the index, authorize, return at most `limit`.
- If the over-fetch budget was **exhausted** (i.e. the index had more rows than we examined) AND rows were
  dropped, attach a `SearchWarning` so a short page is **visible as incomplete** rather than reported as
  exhaustive. `SearchMetadata.Warnings` already exists (`SearchMetadata.cs:47`) and the client already
  surfaces warnings — no new response surface.

This is the direct answer to the POML's *"treat 'it returns fewer results now' as the failure mode to hunt,
because it is the one that looks like success."* The warning is what stops it looking like success.

## 3. Authorization design — compose task 070, add no third policy

`scope=all` stops being a refusal and becomes a **per-row** decision:

1. Filter authorizes the *request* only for tenant + caller identity, and publishes
   `SemanticSearchAuthorization { Scope = "all" }` — no parent, no document list.
2. Endpoint runs the search, then groups returned rows by **distinct `(parentEntityType, parentEntityId)`**.
   A page of 20 documents from one matter is **one** access check, not 20.
3. Each distinct parent → `AuthorizationService.GetCallerRecordAccessAsync` (task 070's method, Dataverse's
   own answer, evaluated as the caller), absorbed by `CachedAccessDataSource`'s 60 s entity-set-qualified key.
4. **Fail closed per row** (ADR-003 + POML constraint): a row whose `parentEntityType` has no
   authorizable-parent mapping, or whose `parentEntityId` is not a GUID, or whose parent check errors, is
   **DROPPED** — never included, never falls back to a container or tenant check.

No new component. No accessible-record-set enumeration — that needs Dataverse's real answer for workforce
principals, which is task **031**'s ADR-028 A2 amendment and has not landed (task 070 notes decision #4).

## 3.5 What shipped, and the verification

| Surface | Change |
|---|---|
| `SemanticSearchAuthorizationFilter.cs` | `scope=all` → permitted with `RequiresPerRowParentAuthorization = true` (an explicit flag, NOT a `Scope` string comparison, so the result-level fail-closed default stays one positive assignment away from permitting anything). `AuthorizableParentEntitySets` made `internal` + `TryResolveParentEntitySet` added so the row path reuses ONE allow-list. `default:` denial text re-advertises `all`. |
| `SemanticSearchEndpoints.cs` | Relevance-ordered lazy row authorization grouped by distinct parent, bounded candidate pool (3×, cap 150) and check budget (25 distinct parents), `PARTIAL_RESULTS` on incompleteness, pointers stripped. **`/count` REFUSES `scope=all`** — see below. |
| `useSemanticSearch.ts` | Entity fragment with no record id degrades to cross-record scope, in `search()` **and** `loadMore()`. |
| `targetEntityNormalize.ts` | Blank-label fallback now warns instead of silently widening. |

**The `/search` vs `/count` asymmetry is deliberate.** `/search` can serve `scope=all` because it can drop
rows. A COUNT has nothing to drop — the only number it can produce is derived from the unfiltered corpus,
which discloses how many documents exist tenant-wide. Counting only readable documents would mean
authorizing the whole matching corpus rather than a page (unbounded work for a number, and it needs task
031's enumeration). So `/count` returns 403 for `scope=all`. The `RequiresPerRowParentAuthorization` flag is
what makes that obligation visible to any future route that adds this filter and cannot post-filter.

### Perturbation-verified (the tests bite, on two independent mechanisms)

A green suite proves nothing on its own. Both guards were neutralized in turn:

| Perturbation | Tests that went red |
|---|---|
| `readable = true` (access check neutralized) | **9** — ReturnsOnlyRowsWhoseParent…, WithNoGrants…, ThreeOfFifty, BudgetExhausted, TotalResults…, IsAcceptedButNeverABlanketAllow, IsCaseInsensitive ×3 |
| Unresolved parent type defaulted + `Guid.Empty` check dropped | **5** — DropsRowsWhoseParentTypeIsNotAuthorizable ×4, DropsRowsWithMissingOrMalformedParentId(`Guid.Empty`) |

The two sets are **disjoint**, which is the useful part: it shows fail-closed resolution and the access
check are independently load-bearing, not two spellings of one guard. 14 distinct cases are pinned.

### Results

- BFF unit suite: **11,084 pass / 0 fail** / 82 skip — unchanged from the Wave 1 baseline, as expected
  (080's tests are integration-level; a moved unit count here would have meant collateral damage)
- Integration (SemanticSearch): **81 / 81**, 0 fail — 19 new cross-record cases
- ArchTests: **79 / 79** (task 074's route guard included)
- Code-page jest (`useSemanticSearch`): **48 / 48**, incl. 4 new degradation cases
- BFF publish: **43.76 MB** compressed, ceiling 60 ✅
- CVE: clean, 6 projects, no HIGH/CRITICAL

> **Publish-size caveat, stated rather than claimed as a win.** 43.76 MB vs the 45.05 MB recorded for task
> 070 reads as a 1.29 MB *decrease* from adding ~300 lines, which cannot be true. The likely cause is that
> this measurement `rm -rf`'d `deploy/api-publish` first and the earlier one did not, so the prior figure
> probably included stale artifacts. Treat **43.76 MB as the clean baseline** and the "−1.29 MB" as a
> measurement-hygiene artifact, not a real shrink.

> **Pre-existing client test failures, NOT caused by this task.** The code page's full jest run shows 10
> suites failing on `bundleIcon is not a function` (a Fluent/react-icons resolution problem) and
> `SearchFlowIntegration` failing 42 cases. Verified by stashing only the two client files and re-running:
> **identical** 2-failed / 42-failed baseline. Untouched here; worth its own task.

## 4. Open items for the owner

- **O-1 — `account` / `contact`-parented documents become invisible in cross-record search.** They have no
  entry in `AuthorizableParentEntitySets`, so §3 rule 4 drops them. Adding `accounts`/`contacts` to that map
  would make them visible, but it widens the authorization surface beyond the build plan's securable
  entities (matter / project / workassignment). Dropping is the conservative choice and is what ships;
  reversing it is a one-line map addition. **Decision needed** on whether those documents should be
  searchable at all.
- **O-2 — task 077 is the page's remaining hole.** Three of the dropdown's rows use
  `/api/ai/search/records`, which still leaks record names tenant-wide. 080 does not close it.

## 5. Follow-ups

- **F-1** — cursor paging for filtered cross-record search (§1 item 4).
- **F-2** — reconcile the three entity-type vocabularies in §0.4. They will drift again otherwise.
- **F-3** — `SemanticSearchRequest.cs:29-32` documents `scope` as defaulting to `all` when omitted. With
  `all` now permitted-and-filtered this doc is finally accurate again; verify it says *filtered*, not *tenant-wide*.
