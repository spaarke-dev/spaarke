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

**Both filters are removed. They are NOT the same kind of dead, and the difference matters** — see
the asymmetry note at the end of this section. Do not treat them as one finding.

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

**The sharper argument for deletion — it was always-deny by construction, not merely unattached.**
The filter's first act is to read `WorkforcePrincipal.HttpContextItemsKey` and fail closed (401) when
it is absent. Nothing in `src/**` ever *writes* that key. So had anyone attached this gate to a route,
that route would have returned **401 unconditionally, for every caller** — it could never have
reached its own accessible-set check. And `AccessibleRecordSetAuthorizationFilter.cs:15` carried a
**commented-out usage example** (`.AddAccessibleRecordSetAuthorizationFilter("sprk_project",
"projectId")`), which is an active invitation for the next author to do exactly that. "Dead code"
undersells it: this was a loaded trap with instructions attached.

Deleted with it: `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/AccessibleRecordSetAuthorizationFilterTests.cs`
(tests of the deleted SUT). This is under `tests/unit/**`, **not** `tests/integration/{auth,regression,
data-mutation,tenant}/**`, so the ADR-038 / FR-B06 "deletion requires same-PR replacement" rule does
not apply.

`ADR008_AuthorizationTests.EndpointFiltersShouldExist` only asserts `Assert.NotEmpty` over ~23
remaining filters — unaffected.

### A-23 `OfficeDocumentAccessFilter` — REMOVED (`Api/Filters/OfficeDocumentAccessFilter.cs`, 216 lines)

**Deletion rests on zero call-sites, and on nothing else.** `AddOfficeDocumentAccessFilter` and
`OfficeDocumentAccessFilter` have no references anywhere in source — every grep hit was inside the
file itself. The compiler confirms it: `Sprk.Bff.Api` builds Release + `--warnaserror` at 0 warnings
with the file gone, and the full unit suite is bit-identical at 11166/11262, so the filter had **no
test coverage at all** — not even its own.

> ⚠️ **Do NOT reuse the original A-23 rationale.** Task 003 filed A-23 on 2026-08-21 arguing that the
> doc-comment operations `"share"`/`"attach"` were not registered policy keys. **Task 072 registered
> `["share"] = AccessRights.Share` on 2026-08-26** (`OperationAccessPolicy.cs:248`), so on the project
> branch `"share"` *is* a valid key (`"attach"` still is not). The conclusion is unchanged — delete on
> zero-callers grounds — but that half of the reasoning is spent and must not be repeated.

### The asymmetry between the two orphans (do not conflate them)

| | A-15 `AccessibleRecordSetAuthorizationFilter` | A-23 `OfficeDocumentAccessFilter` |
|---|---|---|
| Attached to any route? | No | No |
| Precondition satisfied if attached? | **NO** — reads `WorkforcePrincipal.HttpContextItemsKey`, written nowhere in `src/**` | **YES** — reads `OfficeAuthFilter.UserIdKey`, which IS written (`OfficeAuthFilter.cs:126`) by a live-attached `AddOfficeAuthFilter()` (`OfficeEndpoints.cs:172`) |
| Behaviour if attached | **401 unconditionally, every caller** — always-deny by construction | Would actually run; outcome depends on the operation's policy registration (not analyzed here — see the stale-reasoning warning above) |
| Had a commented-out usage example inviting attachment | **Yes** (`:15`) | No |

A-15 was a loaded trap; A-23 was an unused-but-functional component. Both warranted deletion, for
**different** reasons. Recording this because a future reader who flattens them into "two dead filters"
will draw the wrong lesson about which one was dangerous.

### Observation surfaced by the A-23 deletion — NOT a regression, and already tracked

`OfficeDocumentAccessFilter` was written for the Office share endpoints (its doc comment names
`ShareLinksRequest` / `ShareAttachRequest`). Those endpoints are **live** — `POST /office/share/links`
(`OfficeEndpoints.cs:1363`) and `POST /office/share/attach` (`:1381`) — and they carry
`AddOfficeRateLimitFilter` + `AddOfficeAuthFilter` (authentication: establishes *who*), but **no
per-document authorization filter**. Contrast `POST /office/save` (`:167`), which does carry
`.AddEntityAccessFilter()`.

This is the exact shape the two-rule forcing function exists for: **Rule A passes** on those routes (a
filter IS attached) while **Rule B** — does a filter consult an authorization service — does not.

Per-document share permission is instead checked in the service layer at `OfficeService.cs:967`, and
that check is a **stub**: `SimulateSharePermissionCheckAsync` (`:1032`), carrying
`// TRACKED: GitHub #229 - Replace with Dataverse security role check` (`:1037`). Related stub:
`CanShare = true // In real implementation, check user permissions` (`:938`).

**My deletion does not regress this.** The filter was never attached, so Rule B's verdict on those
routes is identical before and after — I am not removing the only thing Rule B was passing on, because
Rule B was already failing there. What the deletion *does* remove is the misleading impression that the
mechanism existed. `OfficeDocumentAccessFilter` was plausibly the intended implementation of
**GitHub #229**; deleting it is still correct (dead code is not a plan), but #229 should be the place
that intent now lives. Flagged for the Office/DMS owner — **out of scope for FR-17** (different
surface: Office add-in, not the SPA/Teams external plane) and **verify on the project branch first**,
since this worktree is master-based and may be stale here as it was on task 072.

### Residual (NOT done — outside this task's modify-set)

| Item | File | Why deferred |
|---|---|---|
| `WorkforcePrincipal.HttpContextItemsKey` is now read by nothing | `Infrastructure/ExternalAccess/ExternalCallerContext.cs:160` | `Infrastructure/ExternalAccess/**` is owned by another agent this wave. Harmless (unused `static readonly object`); cosmetic follow-up. |

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
