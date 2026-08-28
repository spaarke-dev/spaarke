# Task 077 — Authorize `POST /api/ai/search/records`

> **Status**: complete 2026-08-26 · FULL rigor · opus @ xhigh · deps 070
> The twin of task 070's defect, on the same route group. Found by task 074's Rule B on its first run.

---

## 1. What was wrong

`RecordSearchAuthorizationFilter.InvokeAsync` did exactly three things: read the `tid` claim (401 if
absent), extract the request, write `LogInformation("Record search authorization granted: …")`, and
`return await next(context)`. Its only constructor dependency was `ILogger`. **No authorization decision
existed anywhere in the file.**

Three separate falsehoods surrounded it:

| Claim | Reality |
|---|---|
| Doc block: *"Validates record types are known entity types"* listed as an authorization rule | The code never did this. The **endpoint** did. |
| Doc block: *"tenant isolation is now enforced at the search index level … this filter remains as the authentication + audit gate"* | Accurate — and an accurate description of a filter that is not an authorization filter, sitting behind a type named `…AuthorizationFilter`. |
| The log line said **"granted"** on every request | Made the absence of a decision read as a decision in any log review. |

**Worse than 070's finding in one respect.** This route returns RECORDS filtered only by `tenantId`, so
any authenticated caller could enumerate matter/project/invoice names tenant-wide. For a secure matter
the NAME is frequently the sensitive fact. And `RecordSearchResult` also carries `Organizations`,
`People`, `Keywords` and `ReferenceNumbers` — so the leak included who is involved, not just that the
matter exists. The POML did not mention those four fields.

## 2. The authorization model chosen — option (c), authorize the page

The POML offered three. Recorded with reasons, per its step 1:

- **(a) constrain to the caller's accessible-record set** — REJECTED. Needs Dataverse's real answer for
  workforce principals, which is task **031**'s ADR-028 A2 amendment and has not landed. Today's
  `AccessibleRecordSetService` resolves ADR-034 membership, far narrower than what a user can read.
  Same blocker task 080 hit.
- **(b) require a parent scope** — REJECTED. Record search *is* cross-record enumeration; requiring a
  parent removes the feature. The POML's own escalation trigger calls this an owner decision, so
  choosing it unilaterally would have been wrong even if it worked.
- **(c) authorize each returned row** — CHOSEN. Same shape as 080's `scope=all`, reusing
  `AuthorizationService.GetCallerRecordAccessAsync`.

**Round-trip count** (acceptance criterion): up to **one check per returned row**, deduplicated by
`(entitySet, recordId)`. Unlike document search there is **no grouping win** — twenty documents often
share one parent, but twenty records are twenty records. Budget is `MaxRecordAuthorizationChecks = 50`,
set to the DTO's maximum page size (`RecordSearchOptions.Limit` is `[Range(1,50)]`) so a full page is
always fully evaluated; a budget below the page size would silently truncate a legitimate page. Typical
page is 20. Repeats across requests are absorbed by `CachedAccessDataSource`'s 60 s key.

## 3. Rule B had to widen, and why the alternative was worse

`RecordSearchAuthorizationFilter` sat in Rule B's `KnownDecorativeFilters` with the note *"FOUND BY RULE
B, in no finding list … Delete this entry when it does."* The acceptance criterion required deleting it.

But Rule B asks a **per-file** question — "does this filter consult a decision service?" — and this
design deliberately puts row authorization in the **endpoint**, so the filter consults none. Two ways to
satisfy the rule as written:

1. **Make the filter wrap `next()`** and rewrite the handler's result. Satisfies the per-file form — and
   **fails OPEN** the moment the handler's result shape changes, because the filter's pattern-match
   silently stops matching.
2. **Enforce in the endpoint.** **Fails CLOSED**: the handler refuses outright when the published
   obligation is absent.

A rule that pushes the design toward the fail-open option is a bad rule, so **the rule widened instead
of the code narrowing**. Rule B now accepts a deciding filter/endpoint **pair**: a filter with no
decision service of its own passes only if EVERY endpoint file attaching it consults one. A decorative
filter on a decorative endpoint still fails — which is the shape that mattered.

`KnownDecorativeFilters` is now **empty**, with a remark saying to keep it that way.

## 4. The existing test suite could not fail

The 13 pre-existing record-search integration tests assert `Results.Should().NotBeNull()` and
**`HaveCountGreaterOrEqualTo(0)`** — true of every possible list. So the suite passed identically whether
the route authorized each row or enumerated the whole tenant. It is the same species as task 070's tests
(which asserted the vulnerability as an expectation); here they asserted nothing falsifiable at all.

This is also why the fixture had no `IAccessDataSource` at all — nothing ever needed one.

Five new tests in `RecordSearchAuthorizationTests`, all against a **deny-by-default** stub:
only-readable-rows-returned · no-grants-yields-nothing · totalCount-reflects-permitted ·
write-alone-is-not-read · no-token-refused-before-search.

### Perturbation-verified (two perturbations, nested failure sets)

| Perturbation | Red |
|---|---|
| `AccessRights != None` instead of `HasFlag(Read)` | **1** — `WriteAccessAloneIsNotEnoughToRead`, the test written for exactly that |
| `readable = true` (authorization neutralized) | **4** — all but the 401 test, which is upstream of the row pass and correctly unaffected |
| A throwaway decorative filter added to the BFF | Rule B **fails** — the widened rule still bites |

## 5. Results

- Search integration (SemanticSearch + RecordSearch): **86 / 86**
- Record-search unit: **73 / 73**
- ArchTests: **9 failures — exactly master's baseline**, verified by `comm` against a pristine
  `origin/master` worktree. No regression; those 9 are master's (FR-27 ×2, FR-28, FR-29, FR-32, FR-F1,
  FR-F2, ADR-010, ServiceBusClientGuard).
- Route guard: **10 / 10** with `KnownDecorativeFilters` empty.

## 6. Follow-ups

- **F-4 — record search cannot announce an incomplete page.** Rows are authorized after ranking, so a
  caller entitled to few records can receive a short page indistinguishable from "nothing matched".
  Document search announces this with a `PARTIAL_RESULTS` warning; `RecordSearchMetadata` has only
  `TotalCount`/`SearchTime`/`HybridMode` — no warnings channel. Adding one is a response-contract change
  that does not belong inside a security fix. Recorded rather than silently accepted.
- **F-5 — the shared allow-list was renamed** `AuthorizableParentEntitySets` → `AuthorizableEntitySets`
  and `TryResolveParentEntitySet` → `TryResolveAuthorizableEntitySet`, because for record search the
  record IS the subject, not a parent. Still exactly one list, which the POML required.
- **F-6 — "valid but unauthorizable" is currently an empty set.** `RecordEntityType` is
  matter/project/invoice and all three are mapped. The 403 branch is written anyway so that adding a
  searchable type without an access mapping DENIES rather than returning unauthorized rows.
