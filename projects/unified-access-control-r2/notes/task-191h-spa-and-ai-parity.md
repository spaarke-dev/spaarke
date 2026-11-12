# Task 191h: SPA and AI predicate parity (#2189, #2369 part 2)

> Owner decisions 2026-11-11: Q4 = Restricted is internal-only; Q6 = invoices stay SPA-only; Q7 = accept all four;
> "external" means the stored `sprk_isexternal = true` only, never `#EXT#`. Design: `2179-flagged-external-dataverse-design.md`
> §1.2, §3.3, §3.10. Builds on 191b (`ExternalVisibility`) and is stacked on 191c (#2723), whose internal
> `ExternalVisibilityDecider` it wraps. Nothing was applied live; the dev count in §4 used read-only queries.

## 1. What was built

| Surface | Change | Fault behaviour |
|---|---|---|
| SPA to-do and event lists (`ExternalDataService.GetTodosAsync`, `GetEventsAsync`) | After the existing root and invoice rules, ONE predicate call for the page's rows; only a Visible row is kept. Runs for a contact and for a systemuser flagged external; an internal systemuser's list is unchanged and the predicate is never asked. | An Undecided row is hidden and the list says `Truncated` (EB-D-215 / D-70). A faulted, throwing or missing seam hides every row and says `Truncated`. |
| `dataverse.read_query` | For an external caller, every row of a lineage table (SecureChildLineage child) or root table is asked; a not-Visible row is dropped and counted (`external_visibility_hidden`), and the rows-left-out note is added. | A fault throws: the tool fails with no data. |
| `dataverse.search_data` | Same, over the guarded hits of the page (one call). | A fault throws: the tool fails with no data. |
| `dataverse.describe` | Same, by id; a not-Visible record answers `RecordNotVisible` (one shape with not-found, NFR-01). | A fault throws: the tool fails with no data. |
| Retrieval trim (`RetrievalAccessTrim.NarrowDocumentKeysForExternalCallerAsync`) | After task 188's narrowing, the predicate over the page's readable document ids; a document key is kept only when BOTH show it. Parent-record keys are untouched. | A faulted or throwing predicate withholds the document keys only. |

- **New seam** `AppOnlyExternalVisibility` (public delegate) + `ExternalRowVisibility` (public result over the internal
  `ExternalVisibilityResult`), file `Infrastructure/ExternalAccess/AppOnlyExternalVisibility.cs`, registered scoped in
  `ExternalAccessModule` over 191c's internal decider.
- **Guard helpers** in `ExternalCallerCommunicationGuard`: `IsVisibilityGuarded`, `NotVisibleRowsAsync`, `NotVisibleIdsAsync`
  (an empty id is not visible; one call; a faulted result throws).
- **K7 ArchTest** (`ExternalDocumentNarrowingGuardTests`): the new seam joins the narrowing seams (consumer allow-list), and a
  new `VisibilitySurfaces` list must REACH the predicate (three tools, the trim, the two SPA lists), with must-fire and
  must-not-fire controls.
- **Remarks**: `IDataverseUserClient` ("The ONE exception" now names this seam) and `IRetrievalAccessTrim`.

## 2. Deviations from the POML

1. **One new DI registration** (POML: "No new endpoint, route or DI registration"). The predicate and 191c's decider are
   `internal`; the handlers, the trim and `ExternalDataService` are `public` classes whose constructors cannot take an
   internal type. Options rejected: making 191b's types public (widens the predicate's whole surface); `InternalsVisibleTo`
   tricks (no effect on public constructor signatures). §11: (1) Existing — 191c's `ExternalVisibilityDecider`; (2)
   Extension — it is the one being wrapped, the wrapper adds no logic; (3) Cost of doing nothing — the three surfaces
   cannot be given the predicate. No new endpoint or route.
2. **Roots are guarded on the AI tools, not only children.** The POML says "every registry table"; roots
   (`sprk_matter`, `sprk_project`, `sprk_workassignment`) are in the registry, so a Restricted root is now hidden from an
   external caller on the tools (Q4). The `read_query` R1 test now expects the rows-left-out note on a root query.
3. **The ExternalDataServiceTests file named in the POML** is not where the list tests landed: they are in
   `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalListVisibilityTests.cs`, beside the existing route tests,
   over the same fake Dataverse.
4. **Task 188's narrowing is kept** on every surface; the predicate is added on top (it can only hide more). Removing 188
   where the predicate subsumes it is a later decision (owner question 1).

## 3. Known limits (K-class; none holds a confirmed defect on a real path)

- **K1** The SPA document list and single-document routes, the SPA single to-do/event routes, the external modules for
  other tables and the internal-plane routes still use task 188 alone; the brief scoped 191h to the lists, the tools and the
  trim.
- **K2** `grid_overview` (#2125) is out of scope.
- **K3** `ExternalDataService` is listed in K7 as an allowed consumer under its existing "reader" entry; the entry's
  reason was updated, not split.
- **K4** `.claude/constraints/auth.md` ("External document narrowing — D7") should name the new seam; `.claude/` is
  main-session only.
- **K5** An external caller's SPA PATCH of a to-do that the list now hides is not re-checked against the predicate here
  (the single-record route keeps its current rule; see K1).

## 4. Dev document count (spaarkedev1, read-only, 2026-11-12; counts only)

Documents whose decision for an external caller changes: 188-visible before, predicate-hidden now. Only the AI tools and
the retrieval trim ask the predicate about documents; the SPA document routes are unchanged (K1).

| Bucket | Active documents | Changes? |
|---|---|---|
| All active documents | 643 | |
| No filing link at all (NoRoot → Undecided → hidden) | 324 (310 null permission, 14 Standard) | Yes |
| Reach a root only through a parent / canonical / version document | 62, of which 7 have a filed parent one hop up | Up to 55 more (deeper chains not walked) |
| Under a Restricted matter / project (direct) | 20 / 6 | No: all are Restricted themselves (188 already hides) |
| Under a Restricted related matter / project / work assignment | 0 | No |
| Under a root whose `sprk_issecure` is null | 0 | No |
| Linked to an invoice | 0 | No |
| Linked to an internal-only or non-Open communication | 3 | No: 188's communication leg already hides them |
| Linked to an event / to-do / agreement | 1 (its event's matter is Standard) | No |

**Result: 324 to 379 of 643 active documents** change, all of them documents with no matter, project or work-assignment
lineage. An external caller reaches a document on these surfaces only through a record they hold, so the practical change for
a real external caller is expected to be nil; the count is the upper bound the predicate imposes.

## 5. Owner questions

1. Documents stricter than 188 alone (NoRoot, Restricted root, taint) on the AI tools and the trim: keep (as built), or
   relax NoRoot documents back to 188's answer?
2. Extend the predicate to the SPA document routes and single-record routes (K1, K5) in a follow-up?
