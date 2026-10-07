# Task 032 — FR-16a per-row authorization on the content-similarity surface

**Finding**: plan.md §3 **F-b** · **Requirement**: spec.md **NFR-02** · **Gates**: task 033
**Date**: 2026-09-08 · **Outcome**: **(a) hardened** — the descope valve was NOT needed.

---

## 1. Why the hardening was tractable (the sizing that decided (a) over (b))

The `TASK-INDEX.md` high-risk table pre-authorized cutting Find from r1 "if hardening proves large". It did
not prove large, and the reason is specific rather than optimistic: **every primitive the fix needs already
existed, and one of them was already injected into the very filter that was under-authorizing.**

| What the fix needs | What already existed | New code required |
|---|---|---|
| "May this caller Read this document?", evaluated AS THE CALLER | `IAiAuthorizationService.AuthorizeAsync(user, ids[], httpContext, ct)` → returns the **permitted subset**, denies without a bearer token, fails closed on exception | none — it was already a constructor dependency of `VisualizationAuthorizationFilter` |
| A fail-closed forcing function | `RecordSearchEndpoints.cs:117-129` | ~20 lines, copied in shape |
| Per-row trim + count rewrite | `RecordSearchEndpoints.AuthorizeRowsAsync:242` and `SemanticSearchEndpoints.AuthorizeRowsByParentAsync` | ~90 lines, adapted for a GRAPH rather than a flat list |
| A programmable access source for the negative test | the `StubAccessDataSource` pattern in `tests/integration/auth/UnifiedAccessControl/**` | ~35 lines, local to the test |

**No new authorization mechanism was created** (root CLAUDE.md §11). The three-question test:

1. **Existing** — `IAiAuthorizationService` (document-scoped, caller-evaluated, already consulted by this
   route's own filter) and `AuthorizationService.GetCallerRecordAccessAsync` (record-scoped) both overlap.
2. **Extension** — yes, and it was taken: the endpoint calls the existing `IAiAuthorizationService` with the
   result rows instead of the single source document. The only new *type* is the
   `VisualizationAuthorization` signal record, which is the direct analogue of `RecordSearchAuthorization`
   and is required by the forcing function the task mandates.
3. **Cost of doing nothing** — a caller holding Read on ONE document received that document's neighbours
   from anywhere in the tenant, including documents of matters they are denied. Concrete, demonstrated, and
   demonstrated to be closed (§4).

`unified-access-control-r2` owns this pattern repo-wide; it is **merged, not live** (no active worktree), so
there was no duplication risk and nothing here belongs on its surface instead. The one thing that DOES
belong to it is recorded in §6.

---

## 2. What changed

### `Api/Filters/VisualizationAuthorizationFilter.cs`
- New `VisualizationAuthorization` record (`RequiresPerRowDocumentAuthorization` + `Subject`), published into
  `HttpContext.Items` — the analogue of `RecordSearchAuthorization`.
- New `VisualizationAuthorizationSubject` enum, so ONE filter class serves both routes and the two cannot
  drift into publishing different obligations.
- New `AddVisualizationContentAuthorizationFilter()` for `POST /related-from-content`, which had no filter.
- The filter now also requires a **bearer token** before proceeding. Without one, access can only be
  evaluated app-only, which `AuthorizationService` refuses by design; refusing here yields a clear 401
  rather than a uniform silent denial downstream.

### `Api/Ai/VisualizationEndpoints.cs`
- `.AddVisualizationContentAuthorizationFilter()` attached to `POST /related-from-content`.
- `RefuseIfNotRowAuthorized` — the forcing function, on **both** handlers. Absent obligation ⇒ **500**, and
  on the GET route it refuses *before* the search runs.
- `AuthorizeRowsAsync` — per-row trimming over the graph:
  - **result rows** = every node that is neither the source (already authorized by the filter) nor a parent
    hub (built solely from the SOURCE document's own lookups);
  - distinct document ids, **memoized by dedup before the call**, bounded by
    `MaxDocumentAuthorizationChecks = 100` (= the service's own `MaxTotalNodes`), rows past the budget
    **dropped, never served**;
  - kept iff `Guid.TryParse` succeeds AND the caller holds `Read` — so **orphan-file nodes**, whose id is an
    SPE file id with no Dataverse row, are never served;
  - edges pointing at a withheld node are dropped; **a hub left with no surviving document is dropped too**,
    because a hub only exists when a neighbour of that type was found, so its bare presence is a count;
  - `TotalResults`, `NodesPerLevel` and `MaxDepthReached` are **recomputed from the permitted rows** — the
    shape of a graph is a count as much as the number is.
- **`countOnly` no longer takes the service's fast path.** That path returned a total computed from rows
  nobody authorized, with no nodes to trim — a pure count side channel ("how many documents resemble this
  one, inside a matter you cannot open"). The full path now runs, rows are trimmed, and `ToCountOnly`
  restores the count-only *response shape* afterwards. Latency consequence in §5.

### `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs`
- `Api/Ai/VisualizationEndpoints.cs` added to `GovernedFiles` as `Scope.RouteLevelGate`, with a reason naming
  task 032 and finding F-b.
- **No out-of-scope routes were flagged** — escalation trigger 3 did not fire. `ExpectedEndpointFileCount`
  is unchanged (117): no endpoint file was added, an existing one was classified.

### `tests/integration/tenant/Ai/TenantSelectionByRequestTests.cs` (not in the POML file list — see §7)
- Two pre-existing tenant-boundary tests call these handlers directly; the new forcing function correctly
  broke them. They now grant the obligation explicitly, and the `Related_` one is handed a
  `NeverConsultedAuthorization` that **throws** if reached, so the empty-graph early return stays honest.

---

## 3. The negative test (the NFR-02 gate)

`tests/integration/contract/Api/Ai/VisualizationRowAuthorizationContractTests.cs` — **11 tests, all passing.**

Only the two module boundaries are substituted: `IVisualizationService` (what the index returned) and
`IAccessDataSource` (what Dataverse would answer, **deny-by-default**). Everything between is the shipped
code — the real `AiAuthorizationService`, the real `AccessRights.Read` comparison, the real token handling,
the real trim. No `Mock<HttpMessageHandler>`, no DI-registration assertion, no ctor null-check (ADR-038).

| Test | Proves |
|---|---|
| `Related_WhenCallerIsDeniedReadOnAMatter_ServesNoneOfThatMattersDocuments` | **THE GATE.** Three documents of a denied matter are absent; the one permitted neighbour is still served; `TotalResults == 1`, not 4 |
| `Related_WhenCallerIsDeniedReadOnEveryNeighbour_ServesNoRowsAndAZeroCount` | zero rows, zero count, hub dropped, no dangling edges |
| `Related_WithWriteButNotRead_ServesNothing` | the gate is `Read`, not "holds any right at all" |
| `Related_CountOnly_ReportsThePermittedCount_NotThePreTrimTotal` | the count side channel is closed, and the service's shortcut is not taken |
| `Related_WithNoAuthorizationSignal_Returns500AndNeverReachesTheSearch` | forcing function, GET — and it refuses *before* searching |
| `Related_WithASignalThatDoesNotRequireRowAuthorization_Returns500` | the **flag** is asserted on, not the mere presence of an object |
| `IndexTemporaryContent_WithNoAuthorizationSignal_Returns500AndIndexesNothing` | forcing function, POST — nothing written to the tenant partition |
| `IndexTemporaryContent_WithTheSignal_SucceedsAndDisclosesOnlyTheCallersOwnUpload` | this route's honest contract (§6) |
| `Related_WithNoCallerToken_ResolvesNoAccess_AndNeverFallsBackToAppOnly` | no token ⇒ no rows, **and the data source is never consulted** |
| `Related_OrphanFileNodes_AreNeverServed` | "no record to evaluate" ≠ "serve it" |
| `Related_AsksDataverseOncePerDistinctDocument_NotOncePerRow` | memoization asserted as observed calls, and dedup of the CHECK does not dedup the ROWS |

---

## 4. Verified to FAIL without the fix (acceptance criterion 8; escalation trigger 2 did not fire)

The row check was disabled (`response = await AuthorizeRowsAsync(...)` replaced with a comment), the suite
re-run, and the result recorded verbatim:

```
Failed!  - Failed: 7, Passed: 4, Skipped: 0, Total: 11
```

The seven that fail are exactly the seven that assert on trimming. The four that still pass are the
forcing-function tests, which exercise a *different* mechanism and are correctly unaffected — stated rather
than glossed, because "11 of 11 failed" would have been the wrong answer and worth suspecting.

The gate test's own failure output:

```
Related_WhenCallerIsDeniedReadOnAMatter_ServesNoneOfThatMattersDocuments [FAIL]
  Expected body.Nodes.Select(n => n.Id) {"11111111-…-000000000001", "matter-44444444-…", "22222222-…-00000000000a",
  "22222222-…-00000000000b", "22222222-…-00000000000c", "33333333-…-000000000001"} to not contain
  {"22222222-…-00000000000a", "22222222-…-00000000000b", "22222222-…-00000000000c"} because a caller denied
  Read on a matter must not receive that matter's documents through the similarity surface …,
  but found {"22222222-…-00000000000a", "22222222-…-00000000000b", "22222222-…-00000000000c"}.
```

The arch guard was validated the same way: detaching `.AddVisualizationContentAuthorizationFilter()` makes
`EveryGovernedRouteCarriesPerResourceAuthorizationOrANamedWaiver` **FAIL** (1 failed / 0 passed), so the new
census entry is not passing vacuously. Restored, and all 191 ArchTests pass.

---

## 5. Latency — the number task 033 should size against

**No escalation is warranted, and here is why, with the arithmetic rather than an assurance.**

Per request, the added work is **one sequential Dataverse `RetrievePrincipalAccess` per DISTINCT result
document**, capped at 100, memoized within the request and absorbed across requests by
`CachedAccessDataSource`'s 60 s entity-set-qualified key. Sequential deliberately —
`DataverseAccessDataSource` mutates `_httpClient.DefaultRequestHeaders.Authorization` per call on a
request-scoped client, and `HttpHeaders` is not thread-safe; concurrency there produces intermittent phantom
denials, which is how an authorization gate gets disabled rather than debugged.

**Authorization is not the dominant term.** `VisualizationService.GetDocumentMetadataAsync` ALREADY performs
one Dataverse fetch per document, sequentially and **unbounded**, and the service's own comment prices it at
"~20-50 ms … worst case ~1-2 s total which is acceptable for this visualization UX". The neighbour count it
walks is not the caller's `limit`: the five hardcoded relationship queries each carry `TopCount = 50`
(`DataverseServiceClientImpl.GetDocumentsByLookupAsync`), so a rich source document can reach ~250 neighbours
plus up to 50 semantic ones before dedup.

So, at the default `limit=25` on a typical document, the trim adds **≈25 round trips ≈ 0.5–1.25 s** on top of
an equal pre-existing cost — roughly a 2× on a path that was already N-round-trips, not a new order of
magnitude. `countOnly` loses its shortcut and now costs the same as a full query.

**For task 033**: size the Find view against **~1–3 s** for a first, uncached similarity call at the default
limit, and near-instant within the 60 s access-cache window. If 033 finds that unacceptable in the pane, the
fix is to lower the default limit or to page — **not** to sample the authorization.

Real per-row wall-clock against a live Dataverse could not be measured from this worktree (no deployed
environment in the loop); the figures above are the in-repo cost model plus the codebase's own recorded
per-call estimate, and are labelled as such rather than presented as observed.

---

## 6. Recorded honestly, not silently

0. **Two findings from this task's own Step 9.5 gates were acted on, not filed.** (a) `adr-check` flagged a
   reflection assertion over `ContentUploadResult`'s properties in the POST test as B16/B17-adjacent — a
   STRUCTURAL invariant living inside a behavioural contract test, where ADR-038 puts structural invariants
   in `tests/Spaarke.ArchTests/**`. **Removed** (Path C); the invariant survives as item 1 below plus an
   in-test comment. (b) `code-review` flagged that `AuthorizeRowsAsync`'s early return on a graph with no
   result rows passed the service's `TotalResults` through untouched. It is 0 in every path that produces
   such a graph today, so nothing observable changed — but "no rows" and "the count of rows" come from
   different code, and the count is now normalised to 0 rather than trusted.
1. **`POST /related-from-content` returns no rows, so its "negative test" is a different test.** The
   acceptance criteria ask for the denied-matter assertion on both routes. That route's response is a
   `ContentUploadResult` carrying a temporary id the caller's own upload just minted — there is no neighbour
   row on it to withhold. Asserting "none of the denied matter's documents appear" there would pass
   vacuously forever. What is asserted instead: the forcing function (500, and nothing indexed), and that the
   response type carries **no collection** — so a future change that starts returning rows from this route
   fails here rather than shipping untrimmed. The row leak this route participates in is at its *exit*,
   `GET /related/{documentId}`, which is fully covered.
2. **The temp-upload round trip looks unreachable today, and was not "fixed" here.** `IndexTemporaryContent`
   mints a `Guid.NewGuid()` that exists only in the search index, never in Dataverse. Feeding it to
   `GET /related/{documentId}` puts it through `IAiAuthorizationService`, which will find no record and
   answer `AccessRights.None` → **403**. That is correct fail-closed behaviour and it is *not* a regression
   introduced here — the filter already ran on that route. Whether the upload→compare flow is meant to work
   at all is a **product** question for task 033, not an authorization change to make under a security task.
   Flagged rather than repaired.
3. **Parent hub nodes are not authorized against their parent record.** A hub carries the source document's
   own matter/project/invoice name. The caller is authorized on the source, but Read on a document does not
   formally imply Read on its matter, so a matter's NAME can reach a caller who holds no rights on the matter
   itself. This is **pre-existing**, is not finding F-b (which is about result rows), and widening scope to
   it silently is exactly what CLAUDE.md §11 forbids. Belongs to `unified-access-control-r2`'s surface.
   → **filed for `notes/defer-issues.md`.**
4. **`ParentEntityType` / `ParentEntityId` are hard-coded `null`** in `GetDocumentMetadataAsync` (GitHub
   #233). This is why the row check authorizes the DOCUMENT rather than its parent, unlike cross-record
   document search. Stated because a future reader comparing the two implementations will otherwise assume
   the divergence was carelessness.
5. **Rows past the 100-check budget are dropped with no warning to the client.** `SemanticSearchResponse` has
   a `PARTIAL_RESULTS` warning channel for exactly this; `GraphMetadata` has none, and adding one is a
   response-contract change that does not belong inside a security fix — the same call `RecordSearchEndpoints`
   made and recorded. → **filed for `notes/defer-issues.md`.**

---

## 7. Scope deviations from the POML

| POML said | What was done | Why |
|---|---|---|
| `Services/Ai/Visualization/VisualizationService.cs` listed `role="modify"` | **not modified** | The trim belongs in the endpoint, exactly as `RecordSearchEndpoints` does it. Putting it in the service would drag `HttpContext` and `ClaimsPrincipal` into the service layer against ADR-008, and the `countOnly` leak was closable from the endpoint without touching it. Less changed surface on a security fix is the safer answer. |
| file list did not include `tests/integration/tenant/Ai/TenantSelectionByRequestTests.cs` | **modified** | Not scope creep: the new forcing function breaks two pre-existing tests that call these handlers directly. Leaving them broken was not an option, and the edit is mechanical (grant the obligation, pass the new argument). |

`src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs` and
`src/client/office-addins/**` were **not touched** — concurrent agents own them.

---

## 8. Verification (real output, not assertion)

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| `dotnet test tests/Spaarke.ArchTests/` | `Passed! - Failed: 0, Passed: 191` |
| New authorization contract tests | `Passed! - Failed: 0, Passed: 11` |
| Same tests, row check disabled (control) | `Failed! - Failed: 7, Passed: 4` |
| Arch guard, POST filter detached (control) | Rule A `[FAIL]` |
| BFF publish size — **fresh build of `origin/master`**, not the recorded baseline | master `e0a6f87c4` **45.35 MB** · branch **45.36 MB** · **delta +0.01 MB** · tool: PowerShell `Compress-Archive -CompressionLevel Optimal` (matching `scripts/Deploy-BffApi.ps1`) |

Publish size is far under the **60 MB** ceiling and far under the **+5 MB** escalation threshold. Note the
branch figure includes other agents' concurrent uncommitted BFF edits present in this worktree; the +0.01 MB
is therefore an upper bound on this task's own contribution, not an under-count.

---

## 9. Placement Justification (CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

**All changes are in the BFF, and belong there.** The BFF is where `HttpContext`, the caller's bearer token,
and the endpoint-filter pipeline live; `Spaarke.Core` deliberately has no ASP.NET Core dependency
(`LayerDependencyTests` guards it), so the obligation signal and the row trim cannot live below the API layer.

- **No new service, no new DI registration, no new package.** `IAiAuthorizationService` was already
  registered and already injected into this route's filter; the endpoint now takes it as a handler parameter
  from the same container.
- **The row trim is in the ENDPOINT, not the filter.** A filter runs before the handler and cannot authorize
  rows that do not exist yet. The alternative — have the filter wrap `next()` and rewrite the handler's
  result — would fail **open** the moment the result shape changed, because the pattern-match would silently
  stop matching. `RouteAuthorizationGuardTests` Rule B was widened in UAC-r2 task 077 specifically to accept
  this filter/endpoint **pair**, so this placement is the sanctioned one, not a workaround.
- **The row trim is not in `VisualizationService`.** See §7.
- **No AI-internal type crosses into CRUD code**; nothing bypasses `Services/Ai/PublicContracts/`.
- **Test obligation met**: 11 new tests compiled into `tests/unit/Sprk.Bff.Api.Tests` (which is where
  `tests/integration/contract/**` builds), plus the arch-guard census entry.

---

## 10. Step 9.5 quality gates

| Gate | Result |
|---|---|
| `adr-check` | **0 violations.** ADR-001 (Minimal API, `Map{Feature}Endpoints` extension) ✓ · ADR-003 fail-closed ✓ · ADR-007 (no `Microsoft.Graph` outside Infrastructure — 0 occurrences) ✓ · ADR-008 (authorization via endpoint filters at route level; group keeps `RequireAuthorization()`; no global middleware added) ✓ · ADR-009/010 (no new DI registration, no new interface, no `IMemoryCache`) ✓ · ADR-013 (no AI-internal type injected into CRUD code; this is AI-surface code consuming an AI-surface service) ✓ · ADR-019 (`Results.Problem` for the refusal, matching the reference forcing function exactly) ✓ · ADR-029 (publish size measured, +0.01 MB) ✓ · ADR-044 (ids are typed `Guid` via `Guid.TryParse`; `Guid.ToString()` is bare-lowercase; no raw GUID interpolated into an OData predicate) ✓. **1 warning acted on** — see §6.0(a). |
| `code-review` | **0 critical.** No secrets, no new input-validation surface, no N+1 introduced beyond the documented per-distinct-document check, no sync-over-async, no swallowed exceptions, no catch-log-rethrow, no null-check-on-non-nullable, no code-restating comments. **1 warning acted on** — see §6.0(b). **1 observation accepted**: `VisualizationEndpoints.cs` grew 375 → 653 lines (+74%). Per CLAUDE.md §11.5 / `COMPONENT-COMPLEXITY.md` this is evaluated on cohesion, not LOC: the addition is ONE responsibility (authorize the rows this file's own routes serve), it is where the reference implementation puts the same logic, and roughly half the delta is the doc comments that make the security reasoning reviewable. No decomposition is warranted; a second reason-to-change has not been introduced. |
| Lint / `dotnet build -warnaserror` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| Full BFF suite (`tests/unit/Sprk.Bff.Api.Tests` — includes `contract/**`, `auth/**`, `tenant/**`, `seam/**`, `regression/**`, `data-mutation/**`) | `Passed! - Failed: 0, Passed: 12078, Skipped: 58, Total: 12136` |
| `tests/Spaarke.ArchTests` | `Passed! - Failed: 0, Passed: 191` |

**No §6.5 ADR conflict arose.** Both gate findings resolved on **Path C (pivot to comply)** — neither
required a project-scoped exception or an ADR amendment, and no ADR rule pushed the design toward a worse
security outcome. The one place the design could have drifted — putting the trim in the filter to satisfy
Rule B's older per-file form — is exactly what UAC-r2 task 077 widened Rule B to avoid, because that shape
fails OPEN. The sanctioned filter/endpoint pair was used instead.
