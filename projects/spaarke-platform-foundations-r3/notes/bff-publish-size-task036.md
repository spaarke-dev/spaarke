# BFF Publish Size — Task 036 (Membership Admin Endpoints)

> **Task**: `036-membership-admin-endpoints.poml`
> **Date**: 2026-06-21
> **Rule**: per `.claude/constraints/azure-deployment.md` BFF Publish-Size Per-Task Verification Rule (NFR-01)

## Measurement

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish-task036/
Compress-Archive -Path deploy/api-publish-task036/* -DestinationPath deploy/api-publish-task036.zip
```

| Metric | Value |
|---|---|
| Compressed (Optimal zip) | **46.19 MB** |
| Prior baseline (task 035) | 46.18 MB |
| **Delta vs prior task** | **+0.01 MB** |
| Cumulative delta vs Phase 5 Outcome A baseline (45.65 MB) | +0.54 MB |
| NFR-01 ≤+1 MB per-task threshold | PASS |
| Hard ceiling 60 MB | PASS (well under) |

## Source of delta

Task 036 added 3 new source files + modified 3 existing:

**Created**
- `src/server/api/Sprk.Bff.Api/Api/Admin/MembershipAdminEndpoints.cs` — endpoint mapping + 2 handlers
- `src/server/api/Sprk.Bff.Api/Api/Admin/Models/RefreshMetadataRequest.cs` — request DTO (record)
- `src/server/api/Sprk.Bff.Api/Api/Admin/Models/RefreshMetadataResponse.cs` — response DTO (record)

**Modified (surgically)**
- `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/IMembershipFieldDiscoveryService.cs` — added `InvalidateCacheAsync(string?, CancellationToken)` method on the existing interface
- `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipFieldDiscoveryService.cs` — added `_populatedEntityKeys` tracking set, recorded keys on successful cache writes, implemented `InvalidateCacheAsync` (single-entity + refresh-all paths)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` — wired `MapAdminMembershipEndpoints()` call

**No new NuGet packages** — reuses `IMembershipFieldDiscoveryService` (task 030), `IDistributedCache` (existing `CacheModule`), and the SystemAdmin policy (existing `AuthorizationModule`). The +0.01 MB delta is the compiled IL for the 3 new files + the InvalidateCacheAsync implementation, well within zip-tool variance.

## CVE check

```
dotnet list src/server/api/Sprk.Bff.Api/ package --vulnerable --include-transitive
```

| Package | Severity | Source | Introduced by task 036? |
|---|---|---|---|
| `Microsoft.Kiota.Abstractions 1.21.2` | High | GHSA-7j59-v9qr-6fq9 | No — pre-existing (same finding present at tasks 013, 020, 030, 031, 035 baselines) |

**No new HIGH-severity CVE introduced by task 036.** Kiota finding tracked at project level (transitive via `Microsoft.Graph 5.99.0` chain pinned in `src/server/api/Sprk.Bff.Api/CLAUDE.md`); upgrade requires coordinated bump of all 7 Kiota packages (separate task — not in 036 scope).

## Pre-merge checklist (bff-extensions.md §A)

- [x] **Placement justification**: BFF (admin tooling lives alongside the service it operates on — `IMembershipFieldDiscoveryService` is already in `Services/Ai/Membership/`; extracting admin endpoints to a sidecar would force operators to authenticate twice. Both handlers are thin pass-throughs to the existing service — no business logic added at the endpoint layer).
- [x] **ADR citations**: ADR-001 (Minimal API), ADR-008 (endpoint-filter authorization via `RequireAuthorization("SystemAdmin")` on the group — NOT global middleware), ADR-010 (consumes existing `IMembershipFieldDiscoveryService` concrete via the DI seam established by task 030 / `MembershipModule`).
- [x] No new direct package references (NuGet graph unchanged).
- [x] No new CRUD→AI dependency (the endpoint lives under `Api/Admin/` and depends only on the membership service already in `Services/Ai/Membership/`).
- [x] Feature-module DI: no new DI registrations — the existing `services.AddSingleton<IMembershipFieldDiscoveryService, MembershipFieldDiscoveryService>()` (task 030) is the singleton both `DiscoverAsync` reads and `InvalidateCacheAsync` writes against, which is what gives task 036 its in-process invalidation guarantee.
- [x] No new HIGH-severity CVE introduced.
- [x] **Test update obligation (bff-extensions.md §F)**: 10 new tests in `tests/unit/Sprk.Bff.Api.Tests/Api/Admin/MembershipAdminEndpointsTests.cs` covering 401/403/200 auth contract on both endpoints, full DiscoveryResult shape (AC-1A.2 — descriptor `source: "auto"` and `"override"` both surfaced), empty-collection happy path, 404 on service-reported entity-not-found, single-entity invalidation, refresh-all (omitted body), refresh-all (empty entityType in body). New fixture `AdminMembershipTestFixture.cs` mirrors `AdminJobsTestFixture` pattern verbatim.
- [x] **Asymmetric-registration check (§F.1)**: N/A — endpoint is registered UNCONDITIONALLY in `EndpointMappingExtensions.cs`; underlying service is also unconditional (task 030's registration). No `if (flag) { ... }` block introduced.
- [x] **Fixture-config-FIRST (§F.2)**: N/A — new fixture `AdminMembershipTestFixture` carries the full canonical config-key set from `AdminJobsTestFixture` (verified manually — same dictionary). Tests pass on first run; no Skip'd tests.
- [x] **Empirical-reproduction-FIRST (§F.3)**: N/A — no ledger entry consulted; greenfield endpoint pair.

## AC verification

- **AC-1A.2** (`GET /api/admin/membership/discovered/sprk_matter` returns expected descriptor list with `source: "auto"` or `"override"`) — verified by `GetDiscovered_Returns200_WithFullDiscoveryResultShape` which asserts both source values appear in the response.
- **AC-1A.7** (Metadata cache invalidates correctly on `POST /api/admin/membership/refresh-metadata`) — verified at the unit level by `PostRefresh_InvalidatesSingleEntity_WhenBodyProvided` (asserts service's `InvalidateCacheAsync("sprk_matter", ...)` is invoked exactly once). The end-to-end "second discover-call re-fetches from Dataverse" assertion is deferred to the P4 wrap-up UAT integration test per task POML instruction.

## Test outcome

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~MembershipAdminEndpointsTests"
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: 75 ms
```

Broader Membership + Admin slice unchanged:
```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Membership|FullyQualifiedName~Admin"
Passed!  - Failed: 0, Passed: 758, Skipped: 1, Total: 759, Duration: 8 s
```
