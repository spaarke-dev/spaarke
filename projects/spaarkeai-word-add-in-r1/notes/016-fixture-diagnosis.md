# Task 016 — Office save contract coverage + identity-route coverage: §F.2/§F.3 diagnosis

> Un-skips `Post_OfficeSave_WithValidRequest_Returns202Accepted` and `Post_OfficeSave_WithoutAuth_Returns401`
> in `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs`, and adds
> `tests/integration/contract/Api/Documents/DocumentIdentityContractTests.cs` for task 012's identity route.
> Base: `62a59a029` (project branch head at task start).

---

## 0. Correction to the task's own premise (found by direct grep against the file, before any change)

plan.md finding **F-f** and this task's own POML both state "13 of 22 tests in the file are skipped." **That
count is wrong.** Verified against `git show HEAD:tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs`
before any edit:

```
[Fact(Skip = ...)]  count: 11
[Fact]              count: 12
Total                     : 23
```

The file had **11 skipped of 23**, not 13 of 22. After un-skipping the two save tests, **9 remain skipped**
(11 − 2 = 9), not 11. The orchestrator's "the file should end with exactly 11 skipped tests" instruction was
downstream of the same wrong count (13 − 2 = 11) and is corrected here rather than silently complied with —
§F.3 applies to the task's own premise, not only to individual `Skip` reasons. §7 below inventories the
correct 9.

---

## 1. `:48` — `Post_OfficeSave_WithValidRequest_Returns202Accepted`

**Recorded skip reason**: "Requires fully mocked Office services - ContainerId not configured in test."

### §F.2 inspection (before any change)

Inspected `OfficeTestWebAppFactory`'s config dictionary and `ConfigureTestServices` against every collaborator
`OfficeService.SaveAsync` touches on the happy path:

| Collaborator | Fixture state found | Consequence if unset |
|---|---|---|
| `EmailProcessing:DefaultContainerId` | **Absent** — matches the skip reason's own words | `ResolveContainerAsync` throws `InvalidOperationException` once `RecordContainerResolver` reports `Unresolved` |
| `ISecurableEntityRegistry` | Not overridden — real `SecurableEntityRegistry` resolves, which calls `IDataverseService.UnwrapServiceClient(...)` on the fixture's loose `Mock<IDataverseService>` (only `TestConnectionAsync` was set up) | `UnwrapServiceClient` returns `null` (unconfigured), so `serviceClient.Execute(request)` inside `SecurableEntityRegistry.QueryMetadataAsync` throws `NullReferenceException`, uncaught, propagating out of `RecordContainerResolver.ResolveForRecordAsync` |
| `SpeFileStore` (`OfficeStorageUploader.UploadToSpeAsync`) | Not overridden — real `SpeFileStore`, built from real `ContainerOperations`/`DriveItemOperations`/etc. wired to an unconfigured `IGraphClientFactory` | Any real Graph call would fail (no live Graph in a test host); `UploadSmallAsync` is `virtual` precisely so this is a module-boundary seam, but nothing was substituting it |
| `IDataverseService.CreateDocumentAsync` / `CreateProcessingJobAsync` | Only `TestConnectionAsync` set up on the loose mock | Unconfigured `Task<string>`/`Task<Guid>` return `null`/`Guid.Empty` — `Guid.Parse(null)` throws for the document id; an unconfigured job id resolves to `Guid.Empty`, which fails `result.JobId.Should().NotBe(Guid.Empty)` even if nothing else throws |
| `OfficeJobQueue` (Service Bus) | Not overridden — real `ServiceBusClient` built from the fixture's fake connection string (`test.servicebus.windows.net`) | `QueueUploadFinalizationAsync` is NOT best-effort (no try/catch at its call site) — a real send attempt against a non-existent namespace throws, and `SaveAsync`'s outer `catch` turns that into `Success = false`, not the 500/timeout the skip comment implies |

**Conclusion**: the skip reason is directionally correct (fixture-shaped, not a DI-registration problem) but
names only ONE of five missing/incorrect fixture pieces. Fixed all five in `OfficeTestWebAppFactory.ConfigureTestServices`
(and the paired config key), none of them a production-code change:

1. `EmailProcessing:DefaultContainerId = "b!test-office-save-drive"` — `"b!"`-prefixed so `SpeFileStore.ResolveDriveIdAsync`'s
   non-virtual short-circuit (`if (containerOrDriveId.StartsWith("b!")) return containerOrDriveId;`) fires
   before any Graph call — the SAME idiom `SpeFlatUploadPathTests.OfficeSave_UploadPath_IsTheBareFileNameWithNoFolderSegment`
   already uses, and its own comment explains why (`ResolveDriveIdAsync` is non-virtual, so Moq cannot
   intercept it directly).
2. `ISecurableEntityRegistry` replaced with `Mock<ISecurableEntityRegistry>` returning `IsSecurableAsync = false`
   for every entity name, so `RecordContainerResolver.ResolveForRecordAsync` short-circuits to `Unresolved`
   with **zero** `IGenericEntityService` calls, and the save falls through to the container configured above.
3. `SpeFileStore` replaced with the established `Mock<SpeFileStore>` module-boundary double (ADR-007, ADR-038
   §4) — reused the exact construction shape from
   `tests/integration/data-mutation/SpeUploadPaths/SpeFlatUploadPathTests.cs BuildSpeMock` (real
   `ContainerOperations`/`DriveItemOperations`/`UploadSessionManager`/`UserOperations` wired to
   `Mock.Of<IGraphClientFactory>()`, only `UploadSmallAsync` set up).
4. `dataverseServiceMock` (already present in the fixture) gained two more `Setup`s: `CreateDocumentAsync` →
   a fresh GUID string, `CreateProcessingJobAsync` → a fresh GUID. `IDataverseService` is the ONE composite
   implementation DI hands out for `IDocumentDataverseService`/`IGenericEntityService`/`IProcessingJobService`
   too (`GraphModule.cs`: all three registered as `sp.GetRequiredService<IDataverseService>()`), so one mock
   satisfies all three — no separate override needed.
5. `OfficeJobQueue` replaced (it has no interface and no virtual members, so it is constructed for REAL) with
   a fake `ServiceBusClient`/`ServiceBusSender` pair — the same idiom
   `MembershipEventPublisherTests` already uses for the same Azure-SDK types.

### §F.3 — an UNDOCUMENTED, empirically-discovered second defect

Fixing the five items above still produced a **403**, not 202. Hand-tracing the failure (log: `EntityAccessFilter`
"Holds None; requires AppendTo") showed the save route ALSO carries `EntityAccessFilter` — a SEPARATE
authorization decision from anything the recorded skip reason named. It asks whether the caller may append a
document to the `TargetEntity` (the matter), via `CallerRecordAccessProbe`, which runs a REAL OBO delegation
check against Dataverse and fails closed (`AccessRights.None`) because the test host's bearer token is not a
real JWT. This is NOT the skip reason's story at all — it was found only by running the test and reading the
403, per §F.3's empirical-reproduction-first requirement. Fixed by reusing `CallerRecordAccessProbe`'s own
designated test seam — its type doc states it is `public virtual` "precisely so tests can substitute the
authorization answer without mocking its HttpClient transport," ADR-038 §4 — via the EXISTING
`ComposeServiceCollaborators.Probe()` builder (`tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceCollaborators.cs`),
which already grants `AppendToAccess` for exactly this purpose. No new mocking infrastructure invented; reused
what CLAUDE.md §11 asks for.

### A separate, real test-arrangement defect (not a production defect)

The original test's `TargetEntity.EntityType = "sprk_matter"` fails `OfficeEndpoints.ValidateSaveRequest`'s
association check, which accepts only the FRIENDLY entity-type names (`"matter"`, `"project"`, …) — the type's
own doc comment explicitly lists `sprk_matter` as a valid `EntityType` value, but the validation array does not
include it (its own code comment records that this was corrected FROM logical names TO friendly names at some
earlier point, for a different reason). Changed the test's arrangement to `"matter"`. This is a test-fixture
staleness fix, not a weakened assertion — the response-shape assertions are byte-for-byte unchanged.

---

## 2. `:82` — `Post_OfficeSave_WithoutAuth_Returns401`

**Recorded skip reason**: "Requires fully mocked Office services - test auth handler always authenticates."

### §F.2 inspection

Inspected `OfficeTestWebAppFactory`'s `TestAuthHandler` (the ONLY auth handler this fixture registers) BEFORE
touching anything. It already supports representing an unauthenticated caller:

```csharp
if (Request.Headers.ContainsKey("X-Test-Unauthenticated"))
    return Task.FromResult(AuthenticateResult.Fail("Test: unauthenticated caller"));
```

— added by task 073, and already exercised successfully by a SIBLING test in the same file,
`Post_OfficeCreateTodo_WhenUnauthenticated_Returns401`. **The recorded skip reason is stale**, not currently
true (§F.3): the test auth handler does NOT "always authenticate" — the ORIGINAL arrangement simply used the
wrong mechanism (`client.DefaultRequestHeaders.Remove("Authorization")`), which `TestAuthHandler` never reads
(it authenticates unconditionally UNLESS the `X-Test-Unauthenticated` header is present; it never inspects
`Authorization`).

### Fix

Swapped the arrangement to `client.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true")` — the SAME
mechanism the working sibling test uses. No fixture change was needed at all; this was a pure test-arrangement
correction. The 401 assertion is unchanged and now genuinely exercises `RequireAuthorization()`'s real pipeline
rather than bypassing it (ADR-008 constraint honoured).

---

## 3. Task 012 identity-route coverage — placement

`docs/procedures/testing-and-code-quality.md` / ADR-038 §2 names `tests/integration/contract/**` as the
`endpoint-contract` KEEP category: "Endpoint HTTP contract: route + status + ProblemDetails + payload shape."
`/api/documents/resolve-identity` lives in `FileAccessEndpoints.cs`'s `/api/documents` group, alongside eight
sibling routes (`preview-url`, `content`, `office`, `open-links`, `share-link`, `view-url`, `download`,
`eml-render`) — none of which had ANY contract-test coverage before this task (`tests/integration/contract/Api/`
had no `Documents` subfolder). Created `tests/integration/contract/Api/Documents/DocumentIdentityContractTests.cs`,
matching the repo's `tests/integration/contract/Api/{Feature}/` convention (siblings: `Office`, `Compose`,
`Communication`, `Workspace`, …).

**Existing unit coverage was checked FIRST** (avoid duplication, CLAUDE.md §11): `DocumentUrlIdentityFilterTests.cs`
and `DocumentUrlIdentityResolutionTests.cs` already pin every Graph/Dataverse classification branch (malformed
URL, local file, not-resolvable, resolved, drive conflict) by invoking the filter/resolver directly.
`DocumentUrlIdentityFilterTests`'s own type doc states: "The authorization decision (403 with no metadata)
belongs to `DocumentAuthorizationFilter` and is exercised end-to-end by the route's contract test (task 016)"
— i.e. this task's output was already anticipated by name. The new file therefore covers the full HTTP
pipeline for the cases the acceptance criteria name (happy path, not-a-Spaarke-document, 401, 403-no-leak,
malformed 400) rather than re-deriving every unit-level branch.

**Fixture**: `DocumentIdentityTestWebAppFactory : CustomWebAppFactory` (the project's general-purpose BFF test
host, `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs` — already used by 6 other contract test files),
layering three additional module-boundary doubles the route's two filters need: `SpeFileStore` (reused
`DocumentUrlIdentityResolutionTests.Spe`/`.ResolvedFor`, `internal static`, same assembly — no fourth copy),
`IGenericEntityService`, and `IAccessDataSource` (the SAME seam `SearchIndexNameEndpointContractTests.PermissiveAccessDataSource`
established for a grant answer; this file needs a deny answer too, so `Mock<IAccessDataSource>` is used
directly). `CustomWebAppFactory`'s `FakeAuthHandler` authenticates on ANY `Authorization` header and fails
(401) on its absence, so the unauthenticated test needed no separate factory variant.

### §F.3 finding during this work — a real, repo-wide ADR-019 defect (NOT fixed here; filed, see §6)

The malformed/empty-URL test initially asserted `response.Content.Headers.ContentType?.MediaType == "application/problem+json"`
per ADR-019 ("MUST return ProblemDetails for all HTTP failures" — RFC 7807 defines that media type). It failed:
the actual header was `application/json`. Root cause, confirmed by reading
`src/server/api/Sprk.Bff.Api/Infrastructure/DI/MiddlewarePipelineExtensions.cs`'s global exception handler
(lines ~62–85): it sets `ctx.Response.ContentType = "application/problem+json"` and then calls
`ctx.Response.WriteAsJsonAsync(new {...})` with NO explicit content-type argument — `HttpResponseJsonExtensions.WriteAsJsonAsync`
unconditionally assigns `response.ContentType = contentType ?? "application/json; charset=utf-8"`, silently
overwriting the value just set two lines above. **This affects EVERY `SdapProblemException`-driven response
across the ENTIRE BFF API**, not just this route — confirmed it is NOT a fixture artifact by contrast: this
SAME fixture's 403 response (a DIFFERENT code path — `DocumentAuthorizationFilter`'s `Results.Problem(...)`,
which sets the header through ASP.NET Core's own `ProblemHttpResult`) gets the header right. `grep`'d the
entire `tests/` tree for any existing assertion of `application/problem+json` — **zero hits** anywhere in the
repository; this appears to be a genuinely untested contract, not a previously-worked-around one.

Per this task's escalation trigger 1 and CLAUDE.md §6.5, this is a real production defect, out of scope to fix
here (production code, not `tests/**`). The test was adjusted to assert what the endpoint CURRENTLY, correctly
returns — the ProblemDetails-shaped JSON BODY (`status`/`title`/`detail`/`type` present) — rather than the
broken header, with the full diagnosis recorded inline as a code comment citing this defect. The defect itself
is filed via `/project-defer-issue-tracking` — see §6 for the issue link.

---

## 4. ADR-038 banned-pattern check (constraint, step 6)

```
grep -n "Mock<HttpMessageHandler>"                                    → no hits
grep -n "Assert.NotNull(services.GetRequiredService...)" (DI-reg test) → no hits
grep -n "ArgumentNullException>(() => new "  (ctor null-check test)   → no hits
grep -n "Stopwatch"                                                    → no hits
```

Both touched/new files sit at an ADR-038 KEEP path (`tests/integration/contract/**`, `endpoint-contract`
category).

---

## 5. BFF publish size (constraint)

`git status --porcelain` after every change in this task shows ONLY:

```
M tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs
?? tests/integration/contract/Api/Documents/
```

**No file under `src/server/api/Sprk.Bff.Api/**` was touched.** No BFF publish-size change occurred; the
publish-size verification procedure was not run because there is nothing for it to measure.

---

## 6. Real product defects found — filed, not fixed here

| # | Finding | Where | Filed |
|---|---|---|---|
| 1 | Global exception handler serves `application/json` instead of `application/problem+json` for every `SdapProblemException` response (ADR-019 violation, repo-wide) | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/MiddlewarePipelineExtensions.cs` (`WriteAsJsonAsync` call resets `ContentType` set two lines earlier) | `notes/defer-issues.md` ISS-003; GitHub [spaarke-dev/spaarke#975](https://github.com/spaarke-dev/spaarke/issues/975) |

---

## 7. The 9 tests still skipped in `OfficeEndpointsContractTests.cs` — inventory (step 7, out of scope, NOT un-skipped)

None of these were touched, run individually, or empirically re-diagnosed beyond reading their recorded skip
text — that would expand this task's scope, which the POML explicitly forbids ("do not opportunistically
un-skip them, and do not let their fixture needs expand this task"). The fixture-vs-product column is a
FIRST-READ classification from the skip text and this task's own findings (the same class of "Requires fully
mocked X" phrasing that turned out to be fixture-shaped, twice, above) — a follow-up MUST still apply the §F.2/§F.3
protocol per test rather than trust this table.

| Line | Test | Skip reason | First-read classification |
|---|---|---|---|
| 169 | `Get_OfficeSearchEntities_ReturnsResults` | "Requires fully mocked Dataverse search services" | Likely fixture (same `IGenericEntityService`/`IDataverseService` mocking shape as this task's §1 fix) |
| 183 | `Get_OfficeSearchEntities_WithEntityTypeFilter_FiltersResults` | same | Likely fixture |
| 197 | `Get_OfficeSearchEntities_WithPagination_RespectsSkipTop` | same | Likely fixture |
| 215 | `Get_OfficeSearchDocuments_ReturnsResults` | same | Likely fixture |
| 229 | `Get_OfficeSearchDocuments_WithContentTypeFilter_FiltersResults` | same | Likely fixture |
| 246 | `Post_OfficeQuickCreate_Matter_Returns201Created` | "Requires fully mocked Dataverse services for quick create" | Likely fixture (same `IGenericEntityService.CreateAsync` shape) |
| 269 | `Post_OfficeQuickCreate_Contact_Returns201Created` | same | Likely fixture |
| 291 | `Post_OfficeQuickCreate_InvalidEntityType_Returns400` | same | **Worth a first look**: posts an INVALID entity type, which may 400 on validation before any Dataverse call is made (the same shape as this task's malformed-URL test) — the skip reason may be as stale as the two fixed here, but this was NOT verified |
| 351 | `Post_OfficeCreateTodo_WithValidRequest_Returns201Created` | "Requires a fully mocked IGenericEntityService for the sprk_todo create" | Likely fixture (same shape) |

---

## 8. Test-count summary (real output, `dotnet test --no-build`)

| Run | Total | Passed | Failed | Skipped |
|---|---|---|---|---|
| `OfficeEndpointsContractTests` + `DocumentIdentityContractTests`, filtered | 29 | 20 | 0 | 9 |
| `Sprk.Bff.Api.Tests.Api` namespace (whole `tests/integration/contract/Api/**` tree) | 1409 | 1386 | 0 | 23 |
| `Sprk.Bff.Api.Tests` — full project (unit + integration + contract, `dotnet test` with no filter) | 12203 | 12147 | 0 | 56 |

No previously-passing test in any of the above runs regressed. The full-project run took 15m41s.

There is no captured "before" full-project baseline from this exact session (the change was made before a
full run was taken) — `current-task.md`'s prior-session note records "full `Sprk.Bff.Api.Tests` 12,139/0" as
of 2026-09-10 (before this task); this task's post-change full run is 12,203/0, a net +64 tests (matches: +6
new identity-route tests, +2 un-skipped, plus whatever else landed on the branch between those two sessions —
not independently decomposed here since 0 failures is the material fact).
