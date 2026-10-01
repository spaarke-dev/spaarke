# Task 062 — F1: per-caller trimming on `GET /office/search/entities`

**Finding**: `notes/fable-review-2026-09-21.md` §2, **F1 (HIGH)** — the only live exploitable finding in the
project · **Date**: 2026-09-21 · **Outcome**: closed by an **impersonated (MSCRMCallerID) Dataverse read**.

---

## 1. Placement Justification (CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

All changes are in the BFF and belong there. **No new service, no new interface, no new DI registration, no
new package, no new endpoint.** The three-question test (CLAUDE.md §11):

1. **Existing** — two caller-scoped read mechanisms already exist: post-trim per row via
   `AuthorizationService.GetCallerRecordAccessAsync` (used by `RecordSearchEndpoints.AuthorizeRowsAsync` and
   by this project's task 032), and the impersonated read seam `IImpersonatedCommunicationQuery` →
   `DataverseWebApiService.RetrieveMultipleImpersonatedAsync` (shipped on the Communication read path,
   registered **unconditionally** at `CommunicationModule.cs:279`). `.claude/constraints/auth.md` names both
   as the only genuinely caller-scoped paths in the repo.
2. **Extension** — yes, taken. `OfficeService` consumes the existing impersonated seam and the existing
   `ICallerSystemUserResolver` (already injected into two other handlers in this same endpoint file for
   quick-create ownership). Nothing new was authored except the contract test file.
3. **Cost of doing nothing** — any authenticated caller enumerated every Matter, Project, Invoice, Account
   and Contact in the tenant from a two-character substring. Demonstrated in §2; demonstrated closed in §4.

**Why the trim is in the query rather than in a filter (ADR-008).** ADR-008 puts per-resource checks in
endpoint filters. A filter runs *before* the handler, so there are no result rows for it to authorize, and
the subject here is a whole result set rather than one route-addressed resource — the same reason task 032
put the visualization trim in the endpoint. ADR-008's own constraint carves this out ("where trimming must
happen inside the query, document why in the code"); the `why` is on
`OfficeService.QuerySearchEntityAsync` and on the route registration. **No §6.5 ADR conflict arose** — no
ADR rule pushed this design toward a worse security outcome.

**No AI-internal type crosses into CRUD code**; nothing bypasses `Services/Ai/PublicContracts/`.
`ICallerSystemUserResolver` lives under `Services/Ai/Context/` by filename only — it is a plain Dataverse
`oid → systemuserid` lookup with no AI dependency, and `OfficeEndpoints.cs` already injects it twice.

---

## 2. REPRODUCE-FIRST (acceptance criterion 1) — the gap was real

No deployed environment is in this worktree's loop, so per the task's own fallback this is a test that
**fails against the code as it stood**, not an assertion. The probe registers a double for the app-only
`DataverseWebApiClient` returning what an app-only Dataverse query returns — **everything matching the
substring, regardless of caller** — and asserts that a caller with no rights on a specific matter does not
receive it.

Run against `OfficeService.cs` **before any change**:

```
[xUnit.net 00:00:02.72]     Sprk.Bff.Api.Tests.Api.Office.OfficeEntitySearchAuthorizationContractTests.REPRODUCE_SearchEntities_ServesAMatterTheCallerHasNoRightsOn [FAIL]
[xUnit.net 00:00:02.72]       Expected result!.Results.Select(r => r.Id) {{22222222-2222-2222-2222-222222222222}, {11111111-1111-1111-1111-111111111111}} to not contain {11111111-1111-1111-1111-111111111111} because a caller with no rights on this matter must not receive it from /office/search/entities.
```

```
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 2 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

`11111111-…-111111111111` is `"Acme v. Zenith (SEALED)"`. It came back with its name, its matter number
`M-9001` and its `modifiedon`, to a caller whose identity was never part of the query — the query carried no
caller identity at all. That is the whole finding.

**Two corrections to the finding text, recorded because a later reader will otherwise trust them:**

- The fable review and the POML both cite `matterTypeLookupService.ts:9` as a consumer of this route. It is
  **not** one. Line 9 is a comment that *contrasts* the matter-type list with this route; the service calls
  `GET /api/office/search/matter-types`, a different handler over a 5-row reference table. The real
  consumers are `SaveFlow.tsx:957` and `useEntitySearch.ts:340` (the "Related to" picker) and `App.tsx:431`
  (the Contact picker).
- "50 per type per page with `skip` paging to walk the whole table" overstates the paging reach. `skip` is
  **never sent to Dataverse** — paging is in-memory over the rows already fetched
  (`ordered.Skip(request.Skip).Take(request.Top)`), so one request discloses at most `perTypeTop`
  (≤50) rows per type, not the whole table. The disclosure is real and is HIGH; the walk is not.

---

## 3. The mechanism, and why this one (acceptance criterion 2)

**Chosen: run the query AS the caller** — `MSCRMCallerID` impersonation through the existing
`IImpersonatedCommunicationQuery` seam, with the caller's `systemuserid` resolved at the endpoint by
`ICallerSystemUserResolver`. Dataverse then applies row-level security (ownership, role depth, business
unit, teams, sharing, hierarchy) *inside* the query, and the rows that come back **are** what the caller may
read. The reasoning is also in code on `QuerySearchEntityAsync`, because the next person to touch this
route needs it there and not only here.

**Rejected: post-trim each row through `AuthorizationService.GetCallerRecordAccessAsync`** — the task 032 /
`RecordSearchEndpoints` mechanism. It is correct, and it is the right answer on those surfaces. It is the
wrong answer here on three counts:

| | Post-trim per row | Impersonated query (chosen) |
|---|---|---|
| **Cost** | one Dataverse round trip **per distinct row**. This route's page is up to 50 rows × 5 types = **up to 250 sequential `RetrievePrincipalAccess` calls**, on a *keystroke-driven typeahead* whose written contract is 500 ms. At the codebase's own recorded 20–50 ms/call (task 032 §5) that is 5–12 s. At the pane's actual `top=10`, single-type request it is still ~10 calls ≈ 200–500 ms — the entire budget, spent on authorization | **zero** added round trips per type. Same one query per type the app-only version issued. The only addition on the whole request is **one** `oid → systemuserid` lookup, which the quick-create handlers in this same file already pay |
| **Coverage** | can only trim rows already fetched, so a caller entitled to few records gets a short page indistinguishable from "nothing matched" once ranking pushes their matches past the window — the limitation `RecordSearchEndpoints.AuthorizeRowsAsync` records as follow-up F-4 | page N is drawn from the caller's own rows; no window |
| **All five types** | needs an entity-set allow-list. The shared one (`SemanticSearchAuthorizationFilter.AuthorizableEntitySets`) has matter / project / invoice / workassignment but **no account and no contact** — two of this route's five types. Covering them meant widening a shared allow-list used by other routes' authorization | entity-agnostic; all five covered by one mechanism, nothing shared widened |

**Escalation trigger 1 (p95) did not fire, and this is why**: the chosen mechanism adds one lightweight
indexed lookup per request and nothing per row, so the picker's p95 is within noise of today's. Had the
post-trim mechanism been chosen, trigger 1 *would* have fired. **Escalation trigger 2 (a type Dataverse
cannot express) did not fire**: impersonation is entity-agnostic, so no type is partially covered.
Real wall-clock against a live Dataverse could not be measured from this worktree (no deployed environment
in the loop); the figures above are round-trip counts plus the codebase's own recorded per-call estimate,
and are labelled as such rather than presented as observed.

**The one operational prerequisite, stated rather than buried.** The BFF application user must hold the
Dataverse Delegate privilege **`prvActOnBehalfOfAnotherUser`**. This is not new to this task — the
Communication read path, the reconciliation reader and the Job-B apply path all already depend on it, and
`CommunicationModule.cs` calls it a go-live prerequisite. Without it Dataverse **rejects** the impersonated
read, which fails closed. What this task adds is that a rejection is no longer *silent*: see §5.

---

## 4. The negative test, and the seed in both directions (criteria 3, 4, 5, 6)

`tests/integration/contract/Api/Office/OfficeEntitySearchAuthorizationContractTests.cs` — **8 tests, all
passing.** Only two module boundaries are doubled: the impersonated read seam (standing in for Dataverse,
which is the component that does the trimming, so the double *models Dataverse*: deny-by-default, returns
only permitted rows) and the caller→systemuser resolver. Everything between is shipped code — the real
route, the real filters, the real `OfficeService`, the real ranking, the real paging. No
`Mock<HttpMessageHandler>`, no DI-registration assertion, no ctor null-check (ADR-038 B1/B16/B17). The
app-only `DataverseWebApiClient` is registered as a double that **throws** if the entity search touches it.

| Test | Proves |
|---|---|
| `SearchEntities_WhenCallerIsDeniedReadOnAMatter_ServesNeitherThatMatterNorItsCount` | **THE GATE.** Denied matter absent · permitted neighbour present · `TotalCount == 1`, not 2 |
| `SearchEntities_TrimsAllFiveEntityTypes_NotJustTheSprkOnes` | all five denied records absent, all five permitted present, all five entity sets went through the impersonated seam — four of five would be a gap |
| `SearchEntities_WithSkipBeyondPageOne_ServesNoUnauthorizedRow` | pages at `skip=0,2,4` are each trimmed, and walking them reaches exactly the 5 permitted records |
| `SearchEntities_IssuesEveryQueryAsTheCaller_AndNeverTouchesTheAppOnlyClient` | every query carried the caller's `systemuserid`; the app-only client was never used |
| `SearchEntities_WhenCallerHasNoDataverseSystemUser_Returns403_AndQueriesNothing` | fail-closed — 403, **and nothing was read** |
| `SearchEntities_WhenCallerCanReadNothing_ReturnsAnEmptySetAndAZeroCount` | zero rows, zero count |
| `SearchEntities_WhenEveryTypeQueryFails_ReportsAFailure_NotAnEmptyPicker` | an impersonation rejection is a 500, not a false "no results" |
| `SearchEntities_ForAnAuthorizedUser_StillReturnsTheRowsThePickerNeeds` | the picker's own request shape still returns rows with the fields it binds |

**Test-scope justification (criterion 10)**: the eight cover exactly the trim contract, the negative case,
the paging case, the five types and the two seed directions. The three beyond that literal list —
fail-closed-403, read-nothing, and all-types-failed — are the failure modes the chosen mechanism introduces
(an unresolvable caller, and a missing Delegate privilege). Shipping a fail-closed mechanism without
asserting that it fails closed would leave the security property untested in the direction that matters.

### Seeded RED (criterion 4) — the trim disabled

The trim lives in Dataverse, so "disable it" means "stop impersonating": `QuerySearchEntityAsync` was
reverted to `_dataverseClient!.QueryAsync(...)`, **and** the harness's app-only double was switched from
throwing to returning the untrimmed world (which is what app-only Dataverse does). Both halves are needed —
seeding only the production line would have produced an exception rather than a disclosure, which proves
less. Result, verbatim:

```
Failed!  - Failed:     7, Passed:     1, Skipped:     0, Total:     8, Duration: 10 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

The gate's own failure:

```
Sprk.Bff.Api.Tests.Api.Office.OfficeEntitySearchAuthorizationContractTests.SearchEntities_WhenCallerIsDeniedReadOnAMatter_ServesNeitherThatMatterNorItsCount [FAIL]
  Expected body.Results.Select(r => r.Id) {{22222222-2222-2222-2222-222222222222}, {11111111-1111-1111-1111-111111111111}} to not contain {11111111-1111-1111-1111-111111111111} because a caller denied Read on a matter must not receive that matter from the entity picker.
```

and the five-type one, which is the answer to "is this really all five?":

```
Sprk.Bff.Api.Tests.Api.Office.OfficeEntitySearchAuthorizationContractTests.SearchEntities_TrimsAllFiveEntityTypes_NotJustTheSprkOnes [FAIL]
  Did not expect body.Results.Select(r => r.Id) to intersect with {{11111111-1111-1111-1111-111111111111}, {11111111-2222-1111-1111-111111111111}, {11111111-3333-1111-1111-111111111111}, {11111111-4444-1111-1111-111111111111}, {11111111-5555-1111-1111-111111111111}} because every one of the five entity types is trimmed — four of five is a gap, but found the following shared items {{11111111-5555-1111-1111-111111111111}, {11111111-4444-1111-1111-111111111111}, {11111111-3333-1111-1111-111111111111}, {11111111-2222-1111-1111-111111111111}, {11111111-1111-1111-1111-111111111111}}.
```

**7 of 8, not 8 of 8 — stated rather than glossed**, because "all 8 failed" would have been the wrong
answer and worth suspecting. The one that still passes is
`SearchEntities_WhenCallerHasNoDataverseSystemUser_Returns403_AndQueriesNothing`, which exercises the
endpoint's caller-resolution gate — a *different* mechanism, correctly unaffected by disabling the query-side
trim. Same shape as task 032's control (7 failed / 4 passed there).

### Restored GREEN

```
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8, Duration: 13 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

Both seed edits were reverted from backups; `grep -c SEED` returns 0 in both files.

---

## 5. What changed

### `Services/Office/OfficeService.cs`
- New field `_impersonatedQuery` (`IImpersonatedCommunicationQuery`), optional ctor parameter so bare test
  constructions keep compiling — **but never degraded to app-only**; see the forcing function below.
- `SearchEntitiesAsync` takes a required `Guid callerSystemUserId`.
- **Forcing function**: an absent seam or an empty caller id **throws** rather than falling back. The only
  query reachable without a caller identity is the tenant-wide app-only enumeration this task closes, so
  refusing is the only alternative to reopening it.
- `QuerySearchEntityAsync` issues the query through the impersonated seam with the same `$filter` /
  `$select` / `$top` it built before. The filter string is byte-identical; only the identity changed.
- **All-types-failed is an error, not an empty list.** Per-type failures stay best-effort (a missing table
  or transient 4xx must not fail the whole picker), but a run where *every* attempted type threw now
  throws — because the single most likely cause is a missing `prvActOnBehalfOfAnotherUser`, and rendering
  that as an empty picker tells the user "you have access to nothing", which is false. This is the one
  behaviour added beyond the trim itself, and it exists because the chosen mechanism has a deployment
  prerequisite that can fail uniformly.
- The false comment at the old `:1652` (*"app-only read (no per-user security trimming yet) — tracked as a
  follow-up on #919"*) is **gone**, replaced by a description of what the code now does (criterion 7).
- `OperationCanceledException` is now rethrown from the per-type loop instead of being swallowed as a
  "type failure" — pre-existing, adjacent, one line, and it would have made a cancelled request look like
  a Dataverse outage under the new all-failed rule.

### `Services/Office/IOfficeService.cs`
- `callerSystemUserId` added as a **required positional parameter with no default**. Deliberate: omitting
  it must be a compile error. A nullable-with-default would let a future caller silently inherit the
  app-only behaviour by forgetting — the same forcing-function argument `AuthorizationContext.UserAccessToken`
  makes for being `required` on a nullable property.

### `Api/Office/OfficeEndpoints.cs`
- The handler takes `ICallerSystemUserResolver` (already injected into two other handlers in this file) and
  resolves the caller before searching. Unresolvable ⇒ **403 `OFFICE_SEARCH_FORBIDDEN`**, ProblemDetails per
  ADR-019, with the correlation id. No app-only fallback.
- `.ProducesProblem(403)` added; route comment records why the trim is in the query and not in a filter.

### Not changed, deliberately
- **`GET /office/search/matter-types`** stays app-only. It is a 5-row reference table (`sprk_mattertype_ref`)
  of configuration values, not customer data, and it is not finding F1. Widening scope to it silently is
  what CLAUDE.md §11 forbids. → noted here rather than done.
- **The `_dataverseClient is null` stub branch** is untouched — task 059 owns its removal, and the task brief
  was explicit that the fix must not be built on it. It returns hardcoded fixtures, never tenant data, so it
  discloses nothing; the impersonation forcing function sits *after* it.
- **`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs`** — not touched. Contested with another
  project and owned by task 061. See §8.
- **`src/client/office-addins/**`** — not touched; a concurrent agent holds edits there in this worktree.

---

## 6. Picker verification (criterion 9)

| Check | Result |
|---|---|
| Server half — the picker's exact request shape (`?q=Ac&type=Matter&top=10`, then `type=Contact`) returns rows with `id` / `name` / `displayInfo` / `entityType` populated | ✅ `SearchEntities_ForAnAuthorizedUser_StillReturnsTheRowsThePickerNeeds` |
| Response contract unchanged | ✅ same `EntitySearchResponse` shape, same camelCase fields, same `totalCount` / `hasMore` semantics. Only the row *set* is smaller |
| Client handles the new 403 | ✅ **no client change needed**, verified read-only: `SaveFlow.tsx:967-970` routes any non-OK through `describeFetchFailure` and throws a surfaced message; `useEntitySearch.ts:351-353` throws on non-OK. Neither renders a failure as "no matches" — that was already fixed by task 053's error-surfacing work |
| `matterTypeLookupService.ts` | ✅ unaffected — it calls `/search/matter-types`, not this route (see §2) |

**Not verified**: the picker has **not** been exercised against a live Dataverse from this worktree — no
deployed environment is in the loop, and the impersonation path's real behaviour (including whether
`prvActOnBehalfOfAnotherUser` is granted in the dev environment) is therefore **unproven end-to-end**. This
is the single largest residual on this task; it is called out again in §8.

---

## 7. Verification (real output, not assertion)

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| New authorization contract tests | `Passed! - Failed: 0, Passed: 8` |
| Same tests, trim disabled (control) | `Failed! - Failed: 7, Passed: 1` |
| Reproduce-first probe, pre-change (control) | `Failed! - Failed: 1, Passed: 0` |
| `tests/Spaarke.ArchTests` | `Passed! - Failed: 0, Passed: 191, Skipped: 0, Total: 191` |
| Full BFF suite (`tests/unit/Sprk.Bff.Api.Tests` — includes `contract/**`, `auth/**`, `tenant/**`, `seam/**`, `regression/**`, `data-mutation/**`) | `Passed! - Failed: 0, Passed: 12387, Skipped: 56, Total: 12443` |
| **Test-count reconciliation** | baseline **12,379 / 0 / 56** → **12,387 / 0 / 56**. **Δ = +8, exactly the 8 new tests in this task's one new file.** No test was deleted, skipped or silently repaired |
| `dotnet list package --vulnerable --include-transitive` | `The given project 'Sprk.Bff.Api' has no vulnerable packages given the current sources.` — no new HIGH/CRITICAL. No NuGet package was added or upgraded by this task |

### Publish size — fresh build of `origin/master`, not the recorded number (CLAUDE.md §10 bullet 4)

| Field | Value |
|---|---|
| Command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` |
| RID / mode | framework-dependent **linux-x64** (from the csproj), not self-contained |
| Configuration | **Release** |
| Compression | PowerShell **`Compress-Archive -CompressionLevel Optimal`** over `deploy/api-publish/*` (the method `scripts/Deploy-BffApi.ps1` uses) |
| PDBs | **included** (4 `.pdb` in both publishes) |
| `origin/master` @ `99cdfe2ea` (freshly built + zipped today) | **45.46 MB** |
| This branch | **45.54 MB** |
| **Delta** | **+0.08 MB** |

Well under the **+5 MB** single-task escalation threshold and the **60 MB** ceiling. Two honesty notes:
(a) the recorded 2026-09-02 baseline of 45.42 MB @ `a826cf347` was **not** used for the diff — master has
since moved to `99cdfe2ea` and measures 45.46 MB today, which is why the rule says to re-measure;
(b) the branch figure is the **whole branch** vs master, including this project's earlier tasks and other
agents' concurrent uncommitted BFF edits present in this worktree. **+0.08 MB is therefore an upper bound on
this task's own contribution, not an under-count.** This task adds no package and no file to the publish —
its own contribution is a few hundred bytes of IL.

---

## 8. Recorded honestly — what is NOT met, and what is left open

1. **🔴 Acceptance criterion 8 ("Task 061's guard no longer flags this route") is NOT met, and cannot be
   met by this task.** The POML gates 062 on 061; 061 is **deliberately deferred** by the orchestrator
   because `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` is contested with another project, and
   this task was instructed not to touch it. As it stands, **that guard does not govern any Office route at
   all** (`grep Office` over the file returns nothing), so it does not flag this route today and there is
   nothing to un-flag. When 061 lands and builds the census, it must classify
   `Api/Office/OfficeEndpoints.cs` and record `/search/entities` as **query-level trimming, not a
   route-level gate** — the same distinction Rule B was widened for in UAC-r2 task 077. **This is a real
   open item, not a formality.**
2. **🔴 No live verification.** The impersonated read has not been exercised against a real Dataverse from
   this worktree. In particular, whether the BFF application user holds `prvActOnBehalfOfAnotherUser` in
   the dev environment is **unknown to this task**. If it does not, `/office/search/entities` will return
   HTTP 500 with the diagnostic added in §5 — fail-closed and loudly, which is the correct direction, but
   it is a visible picker outage, not a silent one. **Someone must confirm the privilege before or with
   deployment.** The same privilege is already required by three shipped Communication paths, so the
   likelihood it is missing is low — but "likely fine" is not "verified".
3. **Ranking now happens over the caller's own rows, which is a behaviour change worth naming.** Before,
   ranking ran over the tenant-wide result set and paging cut into it. Now Dataverse returns at most
   `perTypeTop` of the caller's *own* rows and ranking runs over those. For a caller with broad rights the
   result is unchanged; for a narrowly-entitled caller results get *better* (their matches are no longer
   crowded out by records they cannot open). No test asserts the ordering across the boundary because the
   ordering contract itself is unchanged — only its input.
4. **`GET /office/search/matter-types` remains app-only.** A 5-row configuration reference table, not
   finding F1, explicitly out of scope. → worth a line in `notes/defer-issues.md` if anyone wants it
   uniform.
5. **The seam is named `IImpersonatedCommunicationQuery` and is now used from Office.** Its *behaviour* is
   entity-agnostic (entity set + OData string + caller id) and its own doc comment describes it as the
   generic impersonated read; only its name is Communication-specific. Renaming it would touch the
   Communication read path, the reconciliation reader, the queue feed and their tests — all live surface in
   a concurrently-active worktree (`email-communication-intelligence-r*`). Reusing it is what CLAUDE.md §11
   asks for; **renaming it is a legitimate follow-up that does not belong inside a security fix.**
6. **F2, F3 and F4 are unaffected.** F1 was the bulk GUID source that made them easier to exploit; closing
   it does not close them. They have their own tasks.

---

## 9. Step 9.5 quality gates

| Gate | Result |
|---|---|
| `adr-check` (applied against the concise ADR set) | **0 violations.** ADR-001 (Minimal API, no new endpoint) ✓ · ADR-003 fail-closed, deny by default ✓ · ADR-004/ADR-028 (no new credential; the impersonation header rides the existing app token and the seam is unchanged) ✓ · ADR-007 (no `Microsoft.Graph` outside Infrastructure) ✓ · **ADR-008** — authorization stays at the route/handler layer, no global middleware, group keeps `RequireAuthorization()`; the in-query trim is the documented carve-out, reasoning in code ✓ · ADR-009/010 (no new DI registration, no new interface, no new cache) ✓ · ADR-013 (no AI-internal type injected into CRUD code) ✓ · ADR-019 (`Results.Problem` for the 403, matching the file's existing error shape + `errorCode` + `correlationId`) ✓ · ADR-029 (publish size measured against a fresh master, +0.08 MB) ✓ · ADR-032 (the seam consumed is registered unconditionally; the ctor parameter is optional only for test-construction compatibility and is refused at use, never quietly no-op'd) ✓ · **ADR-038** (new tests at the `tests/integration/contract/**` KEEP path; no `Mock<HttpMessageHandler>`, no DI-registration test, no ctor null-check test) ✓ · ADR-044 (ids typed `Guid`; `ToString("D")` bare lowercase; no raw GUID interpolated into an OData predicate — the search value is `Uri.EscapeDataString`'d exactly as before) ✓ |
| `code-review` | **0 critical.** No secrets · no new input-validation surface (the filter string is byte-identical to the one that shipped) · **no N+1 introduced** — this mechanism was chosen specifically to avoid one · no sync-over-async · no swallowed exception that hides a failure (the opposite: an all-types failure is now surfaced) · no catch-log-rethrow · no code-restating comments. **1 observation accepted**: `OfficeService.SearchEntitiesAsync` grew by ~45 lines, roughly half of it the doc comment that makes the mechanism choice reviewable. Per CLAUDE.md §11.5 / `COMPONENT-COMPLEXITY.md` this is evaluated on cohesion, not LOC: it is one responsibility (search the entity types this caller may see) and no second reason-to-change was introduced. No decomposition warranted |
| Lint / build | `Build succeeded. 0 Warning(s) 0 Error(s)` |

**No §6.5 ADR conflict arose.** The one place the design could have drifted — putting the trim in an
endpoint filter to satisfy ADR-008's default form — is the shape that cannot work here (a filter has no rows
to authorize) and that fails **open** if the result shape later changes. ADR-008's own text sanctions the
in-query alternative provided the reason is documented in code, which it is.

---

## §N (appended 2026-09-21 by main session) — the Delegate-privilege risk is CLEARED

The executing agent closed this task with one acceptance criterion explicitly unmet: it could not verify that
the BFF's Dataverse application user holds **`prvActOnBehalfOfAnotherUser`**. Without it, the impersonated
read fails and the entity picker returns 500 — fail-closed and loud, but a **visible outage** on a shipped
surface. It flagged this as needing confirmation before deploy rather than assuming, which was correct.

**Verified live against the dev environment via the Dataverse MCP connection. The privilege is present.**

### The privilege → role mapping (queried, not assumed)

`prvActOnBehalfOfAnotherUser` = `ae5c41f0-e823-4cb9-b25a-8ef020201973`. Roles granting it include:

| Role | roleid |
|---|---|
| **System Administrator** | `10fbf21c-1872-f011-b4cb-7c1e52671ad0` |
| **Delegate** | `75fef21c-1872-f011-b4cb-7c1e52671ad0` |

> ⚠️ **The load-bearing fact, and it is counter-intuitive**: in this environment **System Administrator
> itself grants `prvActOnBehalfOfAnotherUser`**. The common assumption — that impersonation always requires
> the separate **Delegate** role because sysadmin does not imply it — does **not** hold here. That assumption
> is why this was recorded as an open risk in the first place. It was checked rather than reasoned about.

### Both candidate BFF application users hold it

| App user | applicationid | systemuserid | Roles | Has privilege |
|---|---|---|---|---|
| `SDAP-BFF-SPE-API` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | `bb5a90e5-4ca8-f011-bbd3-7c1e5215b8b5` | System Administrator **+ Delegate** | ✅ twice over |
| `# mi-bff-api-dev` (managed identity) | `5967251e-171c-46fe-a6c2-ef843c90309d` | `8793f4b0-01db-f011-8406-7c1e520aa4df` | System Administrator | ✅ via sysadmin |

Both are `isdisabled: false`.

**Why both were checked rather than one**: the BFF's Dataverse identity in dev could be either the
client-secret app registration or the newer managed identity, and the environment has both as enabled
application users. Resolving which one is actually configured would have been the harder question — and it
turned out not to matter, because **either identity has the privilege**. Had only `SDAP-BFF-SPE-API` carried
it (the Delegate-role assumption), identifying the configured identity would have been mandatory before
deploy.

### Consequence

**Task 062 no longer has a pre-deploy blocker.** The impersonated read will work in dev under either
identity. Its second unmet criterion (task 061's guard) is unchanged and remains tied to 061.

**Carry forward to other environments**: this was verified in **dev only**. A different environment may
assign these app users differently, and `spaarke-bff-api-prod` (`92ecc702-…`) was **not** checked. Re-verify
before the first production deploy that depends on impersonation — do not port this result.

### ⚠️ §N.1 CORRECTION (2026-09-21) — §N cleared the WRONG precondition. A second one is NOT satisfied.

§N above verified that the **BFF application user** can impersonate (`prvActOnBehalfOfAnotherUser`). That is
true and unchanged. **It is not sufficient**, and reading §N alone would leave a reader over-confident.

Impersonation has **two** preconditions. §N checked one:

| # | Precondition | Status |
|---|---|---|
| 1 | The BFF app user may act on behalf of another user | ✅ verified §N |
| 2 | **The impersonated END USER may read the tables being queried** | ❌ **NOT satisfied for the add-in's own role** |

Found by task 066's investigation (not by 062's own work) and re-verified independently here against dev:

- **`Spaarke Office Add In User` grants NO `prvReadsprk_Matter` at any depth.** A query for
  `prvReadsprk_Matter` on that role returns **zero rows**.
- Only **`Spaarke Basic User`** grants it, at depth mask **4**.
- Role assignments in dev: `Test User 1` holds **both** roles; `Ralph Schroeder` holds `Spaarke Basic User`.
  **No user in dev holds `Spaarke Office Add In User` alone.**

**Why this is invisible in dev and dangerous outside it**: the one add-in test account happens to carry the
second role. A tenant that provisions add-in users with only `Spaarke Office Add In User` — which is what the
role's *name* implies is its purpose — gets an end user who can read none of the five searched tables.

**Failure shape**: every per-type query fails for that caller, which trips the deliberate
all-types-failed rule task 062 added (the rule exists so a permissions problem does not masquerade as
"no results" — correct reasoning). The result is **HTTP 500 on the entity picker**, not an empty list.
Fail-closed and loud, as designed, but a **visible outage** on a shipped surface for that user population.

**This is the live verification 062 recorded as its own unmet acceptance criterion.** The criterion was right
to be left open; §N closed a different question than the one that mattered most.

**Owner decision required before any deploy that ships 062.** Options, not mutually exclusive:

| | Option | Cost |
|---|---|---|
| **1** | Grant `Spaarke Office Add In User` read on the five searched tables (Matter, Project, Invoice, Account, Contact) at an appropriate depth | A security-role change; needs the depth decided deliberately, since depth IS the trim |
| **2** | Soften 062's all-types-failed rule so a **permission-denied** outcome yields an explicit "you do not have access to search these records" state rather than a 500 | Code change in 062's file; keeps other failure causes loud |
| **3** | Both — 1 makes the picker work, 2 makes the failure legible if a role is ever misconfigured again | Recommended |

Do NOT resolve this by reverting 062. The untrimmed enumeration it replaced is the HIGH finding F1.
