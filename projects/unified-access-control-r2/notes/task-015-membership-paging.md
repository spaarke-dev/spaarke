# Task 015 — Deterministic, complete membership paging (finding A-10 / FR-14 / NFR-03)

> **Status**: implemented · full BFF suite green (11,171 passed / 0 failed / 96 skipped)
> **Files**: `Services/Ai/Membership/MembershipResolverService.cs` · `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` · `tests/integration/auth/UnifiedAccessControl/MembershipPagingCharacterizationTests.cs`
> **Date**: 2026-08-27

---

## 1. The paging scheme chosen, and why

**Dataverse page/count paging with a paging cookie, ordered by the entity's primary id.**

The POML offered two options — "page/count with a paging cookie, or top with a proper sentinel". The
sentinel option was rejected: FetchXml has no offset operator, so a `top`-based scheme still has to
advance the cursor by *something*, and the only candidates are (a) a page number — which is
page/count wearing a disguise, or (b) a keyset predicate `primaryid > lastSeen`. Keyset paging would
be the better design in general (immune to concurrent inserts, no cookie), but **Dataverse does not
support `gt`/`lt` on a `uniqueidentifier` attribute** — the condition operators for a GUID column are
`eq`/`ne`/`in`/`not-in`/`null`/`not-null`. So keyset-on-the-primary-key is not expressible, and
page/count is the only correct scheme available.

| Element | Before | After |
|---|---|---|
| Row cap | `top='{limit+1}'` **and** `count='{limit+1}' page='N'` when continuing | `count='{limit}' page='N'` only — `top` is never emitted |
| Order | *(none)* | `<order attribute='{entity}id' descending='false' />`, emitted before `<filter>` per the FetchXml schema sequence |
| has-more | `allIds.Count > limit` — derived by over-fetching one row and **discarding** it | `EntityCollection.MoreRecords`, the platform's own flag — costs no data row |
| Cursor | base64 of a bare `int` skip | base64url of `v2\|{page}\|{pagingCookie}` |
| Bad cursor | silently decoded to skip 0 (restart at page 1) | `ArgumentException` → 400 via the endpoint's existing mapping |
| Page trimming | page truncated to `limit`, dropping the extra row | **never trimmed**; over-return is logged and de-duplicated |

### The order key is derived by convention, and that is a known limit

`PrimaryIdAttribute(entityType)` returns `{entityLogicalName}id`. This holds by construction for every
`sprk_*` custom table (all the resolver's real targets) and for the standard tables it touches. It is
**not** universal — a few system tables deviate (`activitypointer` → `activityid`). If the resolver is
ever pointed at such a table, Dataverse rejects the query with a 400 and the call throws, so the
caller denies (ADR-003 fail-closed) rather than receiving a partial set. Loud and safe, by choice.

Deriving the key from live metadata instead would mean extending `IMembershipFieldDiscoveryService` /
`DiscoveryResult`, both **outside this task's file scope**. Recorded here as the follow-up rather than
widened into silently.

### `MoreRecords` plus a belt

has-more is `collection.MoreRecords || returned >= pageSize`. The disjunct is deliberate: a provider
that returns a full page while reporting `MoreRecords = false` would otherwise truncate the caller
silently — the exact A-10 failure. Cost: one extra round trip returning zero rows when a result set is
an exact multiple of the page size. Cost of omitting it: an undetectable under-grant. Not a close call.

---

## 2. The cap-surfacing contract (NFR-03)

`AccessibleRecordSet` gains two **additive, non-required** members (no existing construction site
changes):

```csharp
public bool Capped { get; init; }                                   // default false
public int  CapLimit { get; init; } = MembershipResolveOptions.MaxLimit;   // 5000
```

`Capped == true` means **"the set is known incomplete"** — composition stopped at the ceiling while
the source still had more. Callers render "Only {CapLimit} records displayed". It does **not** mean
"the count equals the limit".

That distinction is load-bearing and is why the composer spends one bounded confirmation read. Page
size (500) is deliberately **decoupled from** the ceiling (5,000): collapsing them would make the
continuation loop unreachable and any test of it a tautology, because a cap check and a page-full
check would become indistinguishable. With them separate, a result set landing on exactly 5,000 ends
on a full page *plus* a cursor and is nonetheless complete — guessing "capped" there cries wolf,
guessing "complete" resurrects the truncation. One extra read settles it.

**Termination is over-determined** (ADR-003: bounded, never unbounded). Only (1) yields a complete set:

1. resolver reports no further pages → complete;
2. id ceiling reached → one confirmation read decides capped vs complete-at-the-ceiling;
3. a page adds no new ids yet claims more → stop, flag capped (the id ceiling cannot catch this: a
   no-progress loop never grows the count);
4. `MaxMembershipPages` round trips → blunt backstop.

**Errors are not caught.** A mid-stream failure propagates and the caller denies wholesale. Returning
the pages read so far would hand back a partial set carrying no indication that it is partial — every
downstream `Contains` then denies reachable records with nothing anywhere saying why. A loud failure
is recoverable; a plausible-looking short set is not.

---

## 3. Round-trip cost (NFR-02 check — escalation trigger did NOT fire)

NFR-02 is scoped to the impersonated root-set source ("the same 3 Dataverse round trips per
request"), not to this loop. Measured behaviour, asserted in tests:

| Membership size | Round trips before | after |
|---|---|---|
| ≤ 499 | 1 | **1** (asserted: `…CostsExactlyOneRoundTrip`) |
| 500 exactly | 1 (truncated silently) | 2 (one confirming empty page) |
| 501 – 5,000 | 1 (**silently truncated**) | ⌈N/500⌉ |
| > 5,000 | 1 (silently truncated) | 11 (10 + confirmation), then capped + flagged |

The common case is unchanged. Extra round trips are incurred only by callers whose access was
previously being discarded, so there is no cost regression to escalate — the trade being made is
"one to ten round trips" against "a systemuser on 900 matters is denied 400 of them, silently".

---

## 4. Consumers audited (POML escalation trigger 2 — list before changing the shared method)

Every caller of `IMembershipResolverService`. **None depended on the buggy paging shape**, so the
trigger did not fire; the audit is recorded because the trigger required the list.

| Consumer | Call shape | Impact |
|---|---|---|
| `AccessibleRecordSetService.ComposeForSystemUserAsync` | was `options: null` | **fixed here** — now pages |
| `AccessibleRecordSetService.ComposeForContactAsync` (standing-grant term) | was `options: null` | **fixed here** — now pages |
| `BriefingService:292` | `options: null`, reads `.Ids` | unchanged shape; benefits from the stable order. Still first-page-only — **not** in this task's scope (composer-scoped per POML), see §6 |
| `DailyBriefingCollector:628` | `options: null`, reads `.Ids` | same as above |
| `LookupUserMembershipNodeExecutor:292` | passes node-config options, surfaces the token downstream | token is opaque; format change is transparent |
| `MembershipEndpoints:266` | passes query-string options, returns the response verbatim | HTTP contract shape **unchanged**; a malformed `continuationToken` now yields 400 instead of silently restarting at page 1 |

Cache version bumped `3 → 4`: entries written under the old query shape hold silently-truncated id
sets and must be orphaned, not served.

---

## 5. What these tests CANNOT falsify about real Dataverse paging semantics

**This list is a deliverable, not a disclaimer — it feeds task 047.** Everything below is asserted
against a simulator, so the simulator's *model* of Dataverse is what is actually being tested. Each
item is a place where the model could be right and the platform still behave differently. None is
covered by any test in this repo today.

1. **That `page`/`count` paging is honoured at all.** The simulator implements page/count because
   that is the documented contract. If Dataverse ignores `page` for a query of this shape, every
   paging test still passes and production silently re-serves page 1 forever. Needs a live query.
2. **That `<order>` on the primary id is accepted for every target entity.** The order attribute is
   derived by the `{entity}id` convention. Nothing here proves that name exists on `sprk_matter`,
   `sprk_project`, `sprk_workassignment`, `sprk_document`, `sprk_event`, `sprk_todo`,
   `sprk_communication`, `sprk_invoice`, or `sprk_analysis`. A wrong name is a 400 at runtime —
   fail-closed and loud, but **entirely undetected until then**. *Highest-value live check.*
3. **Dataverse's `uniqueidentifier` collation.** The simulator sorts by an order deliberately unlike
   .NET's `Guid.CompareTo` to prove no page boundary is re-derived client-side — but it is a *stand-in*,
   not SQL Server's actual GUID collation. Real ordering is untested.
4. **Paging-cookie semantics.** The simulator issues and validates its own cookie shape. It cannot
   test: whether Dataverse accepts a cookie embedded and XML-escaped the way the resolver escapes it;
   whether a cookie expires; whether a cookie from a *cached* earlier page is still valid 5 minutes
   later (the resolver caches per page, so replay is a real production path); or what happens when a
   cookie is presented with a mismatched page number.
5. **That `MoreRecords` is populated for FetchXml through `ServiceClient`.** The belt
   (`returned >= pageSize`) covers under-reporting, but if `MoreRecords` were *over*-reported the belt
   does nothing and the loop leans on the no-progress guard. Untested against the platform.
6. **Concurrent-write behaviour.** Every test runs against a frozen row set. Page/count paging over a
   set being mutated mid-walk can skip or duplicate rows on any platform; whether Spaarke's actual
   write rate makes that reachable is unknown and unmeasured.
7. **The 5,000 ceiling against real data volumes.** `Ids(5001)` is synthetic. Whether any real
   principal exceeds 5,000 memberships — i.e. whether `Capped` ever fires in production — is unknown.
   If it does, no UI currently renders the flag (see §6).
8. **The OR-filter's correctness.** The simulator validates that the filter is non-empty and uses only
   `eq`, then returns its own row set — it does **not** evaluate conditions against rows. These tests
   therefore say nothing about *which* rows match, only about how a matched set is paged. Row-matching
   is covered by the pre-existing `MembershipResolverServiceTests`.
9. **The transitive (`includeRelated`) query beyond its `<order>`.** The simulator holds rows for one
   table; for the 1-hop query it enforces that an order element is present and returns empty. The `in`
   operator, its ~500-value guidance, and transitive row attribution are unmodelled.
10. **Interaction with row-level security.** All reads here are app-only. Whether an *impersonated*
    read pages identically — same cookie semantics, same `MoreRecords` — is exactly the seam task 034
    /036 build on, and is untested here.

---

## 6. Follow-ups (not in scope, deliberately not widened into)

| # | Item | Why not here |
|---|---|---|
| F-1 | `BriefingService` + `DailyBriefingCollector` still read only the first page (`options: null`). Same under-grant, different surface — a briefing silently omits records past 500. | Files outside this task's modify-set. **Should be filed as its own finding.** |
| F-2 | Derive the primary-id attribute from metadata instead of the `{entity}id` convention (§1). | Requires extending `IMembershipFieldDiscoveryService` / `DiscoveryResult`. |
| F-3 | No UI renders `AccessibleRecordSet.Capped`. The flag exists and is correct; NFR-03's user-visible half is unbuilt. | Client surface. |
| F-4 | Live verification of items 1–5 above. | Needs a real environment — task 047. |
