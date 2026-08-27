# Task 018 — FR-17 (A-15 + A-16): dead-filter removal + bounded `in`-clause

> Deliverable for POML step 8. Downstream consumers: task 056 (child-module registration depends on
> this bound), task 032/033 (`AccessibleRecordSet` reshape — the A-15 consumer list shrinks by one).

---

## 1. A-16 — the `in`-clause bound

### The number

`Tier2ScopeFilterInjector.MaxValuesPerInCondition = 500` — Dataverse's documented guidance for the
FetchXML `in` operator. Same number `MembershipResolverService.BuildTransitiveFetchXml` documents
(`MembershipResolverService.cs:1022-1028`) for the same reason.

### Why a LOCAL constant and not `MembershipResolveOptions.DefaultLimit`

The POML said "reuse the existing constant/approach rather than inventing a divergent number". The
number is not divergent — it is the same 500 — but the *constant* is deliberately not shared:

1. **Semantic mismatch.** `MembershipResolveOptions.DefaultLimit` (`IMembershipResolverService.cs:149`)
   is a membership **result-page size** (`MaxLimit = 5000` is its ceiling). It is not a FetchXML
   per-condition **value** limit. Binding them would let someone tuning membership page size silently
   change a query-validity bound on an unrelated authorization path.
2. **Layer coupling.** It would give `Api/ExternalAccess/**` — the external-access authorization plane
   — a compile dependency on `Services/Ai/Membership/**`, against root CLAUDE.md §10 bullet 3 and the
   ADR-013 boundary intent. (It would not currently trip `ADR013_AiBoundaryTests`, whose
   `ForbiddenAiInternalTypes` list is only `IOpenAiClient` + `IPlaybookService` — but the direction is
   the one that ADR guards.)

`ScopeInjectorBoundTests.Bound_IsFiveHundred_MatchingDataverseInOperatorGuidance` pins the value so it
cannot drift away from the sibling guidance silently.

### CHUNK, not CAP — and why that is the whole safety argument

Each dimension's id set is split into `≤500`-value sibling `<condition operator='in'>` elements under
the existing `<filter type='or'>`. Because the combinator is `or`:

```
(attr in c1) OR (attr in c2) …  ==  attr in (c1 ∪ c2 …)
```

**Every accessible id is emitted exactly once. Nothing is dropped, nothing is added.** The bound
therefore changes the query's *shape* and never the caller's *scope*. Consequences:

- **No under-grant** → nothing to surface per NFR-03 (see §2).
- **No over-grant** → no disclosure direction to worry about.
- A *smaller* chunk size would still be correct (more conditions, identical union). The chunk size is
  not a security parameter. This is precisely why chunking was chosen over a truncating cap.

`Enumerable.Chunk` never yields an empty chunk, so a set sized at an exact multiple of the bound
cannot emit an invalid `IN ()` — the off-by-one that would reproduce the A-16 failure.

`FilterCombinator` is a named constant with a comment stating that flipping it to `and` breaks the
re-union (disjoint chunks would intersect to empty — a fail-CLOSED deny, not a disclosure, but still
wrong). A test pins it.

### Why NOT a truncating cap

A truncating cap would silently **under-grant** (caller sees fewer records than they may see). NFR-03
then requires that be user-visible — which `Inject` structurally cannot do: it returns a `string`.
Surfacing a cap would require a signature/return-shape change rippling into
`ExternalModuleDataEndpoints.cs`, outside this task's modify-set. Chunking avoids the question
entirely by never capping.

---

## 2. The "per FR-25" cross-reference — RESOLVED (spec text defect)

Spec `FR-17` (spec.md:67) reads *"bound the `in`-clause **per FR-25**"*. **FR-25 is unrelated**: it is
the Standing-Grant baseline requirement (View Only / Collaborate / Full Access for contacts and
organizations, spec.md:81). It says nothing about caps or `in`-clauses.

The only cap requirement in the spec is **NFR-03** (spec.md:114): *"Result caps must never be silent.
When a result set is capped, the user sees 'Only 5,000 records displayed'"*, reinforced at spec.md:234
(*"the system never silently under-grants without telling the user"*).

**Resolution**: "per FR-25" is a mis-reference; the intended requirement is **NFR-03**. This was
settled by reading both requirements, not by guessing, so the POML's escalation trigger did not need
to fire. And NFR-03's obligation is **not triggered by this implementation** — chunking never caps, so
there is no truncation to make visible.

**Recommended spec edit (main-session-owned — NOT made by this task)**: `spec.md:67`, change
`bound the `in`-clause per FR-25` → `bound the `in`-clause per NFR-03`.

---

## 3. A-15 / A-23 — dead filter removal

### A-15 `AccessibleRecordSetAuthorizationFilter` — REMOVED

Reachability proof (not a reading):

| Check | Result |
|---|---|
| `AddAccessibleRecordSetAuthorizationFilter` call-sites in `src/**` | **zero** — only the definition (`:45`) and its own doc-comment example (`:15`) |
| `WorkforcePrincipal.HttpContextItemsKey` **written** anywhere in `src/**` | **never** — read only at `AccessibleRecordSetAuthorizationFilter.cs:102` |
| Route attachment | none — the `/api/v1/collab` group that used it was removed by SPA-r2 task 018 |
| Live enforcement instead runs via | `ExternalModuleDataEndpoints.GetScopedRecordAsync:266-273` (CallerPrincipal path) |

Because **zero routes attach it**, removal cannot change any route's authorization verdict — there is
no route whose "is a filter attached / does the filter consult an authorization service" answer
depended on it.

Deleted with it: `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/AccessibleRecordSetAuthorizationFilterTests.cs`
(tests of the deleted SUT). This is under `tests/unit/**`, **not** `tests/integration/{auth,regression,
data-mutation,tenant}/**`, so the ADR-038 / FR-B06 "deletion requires same-PR replacement" rule does
not apply.

`ADR008_AuthorizationTests.EndpointFiltersShouldExist` only asserts `Assert.NotEmpty` over ~23
remaining filters — unaffected.

### Residual (NOT done — outside this task's modify-set)

| Item | File | Why deferred |
|---|---|---|
| `WorkforcePrincipal.HttpContextItemsKey` is now read by nothing | `Infrastructure/ExternalAccess/ExternalCallerContext.cs:160` | `Infrastructure/ExternalAccess/**` is owned by another agent this wave. Harmless (unused `static readonly object`); cosmetic follow-up. |
| **A-23** `OfficeDocumentAccessFilter` — second orphaned filter | `Api/Filters/OfficeDocumentAccessFilter.cs` (216 lines) | Outside this task's assigned modify-set. **Confirmed orphaned**: `AddOfficeDocumentAccessFilter` has zero call-sites in `src/**`. Still needs deletion per the task-003 scope addition. |

---

## 4. Test coverage + mutation evidence

`tests/unit/Sprk.Bff.Api.Tests/AccessControl/ScopeInjectorBoundTests.cs` (22 tests). Task 001 wrote no
A-15/A-16 characterization (`notes/task-001-untestable-findings.md`), so POML step 5 "flip the
characterization tests" was a **no-op — there was nothing to flip**.

Each guard was perturbed **individually** and confirmed to bite:

| Mutation | Meaning | Tests that failed |
|---|---|---|
| `MaxValuesPerInCondition` 500 → 501 | bound drifts from Dataverse guidance | 1 |
| `.Chunk(MaxValuesPerInCondition)` → `.Chunk(int.MaxValue)` | **the original A-16 bug** | 8 |
| `.Chunk(…).SkipLast(1)` | drops a chunk (under-grant / unfiltered) | 14 |
| chunk attribute → `nonEmpty[0].Attribute` | chunk leaks to another dimension (**over-grant**) | 3 |
| `FilterCombinator` `or` → `and` | chunks intersect instead of re-uniting | 2 |

No test doubles exist in this suite — the injector is a static pure function over its arguments, so
there is no double that could default permissive on unmodelled input.

### What these tests CANNOT falsify

1. **That 500 is the right number.** It is documented Dataverse guidance; no in-process test can
   observe the platform's real per-condition limit. Only a live Dataverse fetch would.
2. **Total-payload size.** Chunking removes the *per-condition* ceiling but a caller with an enormous
   accessible set still produces a large FetchXML overall. That is a different (pre-existing) limit
   and is deliberately not addressed here — addressing it would require a truncating cap, i.e. the
   silent under-grant §1 rejects.
3. **The end-to-end fetch.** The injector is tested as pure domain logic; the actual Dataverse
   execution needs a live `ServiceClient` (ADR-038 rationale already recorded in the sibling
   `Tier2ScopeFilterInjectorTests` header).
4. **Paging.** Unchanged and still absent on this seam (`01-spa-plane.md:163`) — accessible rows past
   one page are still truncated. Out of scope for FR-17.
