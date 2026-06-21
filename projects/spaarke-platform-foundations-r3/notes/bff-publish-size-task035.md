# BFF publish-size — task 035

**Date**: 2026-06-21
**Task**: 035 — `GET /api/users/me/memberships/{entityType}` endpoint + filtering + pagination
**Branch**: `work/spaarke-platform-foundations-r3`

## Measurement

| Step | Value |
|---|---|
| Publish command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish` |
| Compress command | `tar czf bff-publish-task035.tgz -C deploy/api-publish .` |
| **Compressed size (task 035)** | **44.85 MB** (47,023,822 bytes) |
| Prior baseline (task 020) | 46.18 MB |
| **Delta vs task 020 baseline** | **-1.33 MB** (smaller because incremental Release publish reused fewer transient files than the previous baseline's fresh full publish; no NuGet adds) |
| Hard ceiling (NFR-01) | 60 MB |
| Headroom | 15.15 MB |

## Pre-merge checklist (per `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule (NFR-01)")

- [x] Compressed publish-size measured: 44.85 MB
- [x] Delta is negative (improvement) — well within ≤+5 MB single-task threshold
- [x] Cumulative size well below 55 MB architecture-review trigger
- [x] No NuGet package additions to `Sprk.Bff.Api.csproj`, `Spaarke.Core.csproj`, `Spaarke.Dataverse.csproj`, or `Spaarke.Scheduling.csproj`
- [x] No `<PublishTrimmed>` / `<PublishAot>` flags added
- [x] Published from `deploy/api-publish/` (canonical location per azure-deployment.md)

## CVE check

```
dotnet list package --vulnerable --include-transitive
```

- **No NEW HIGH-severity CVE** introduced by task 035
- Pre-existing HIGH (tracked at project level, NOT introduced by 035):
  - `Microsoft.Kiota.Abstractions 1.21.2` — `GHSA-7j59-v9qr-6fq9`

## Files added

- `src/server/api/Sprk.Bff.Api/Api/Membership/MembershipEndpoints.cs` (~440 lines)
- `tests/unit/Sprk.Bff.Api.Tests/Api/Membership/MembershipEndpointsTests.cs` (~580 lines, 18 tests)

## Files modified

- `src/server/api/Sprk.Bff.Api/Program.cs` (+8 lines — using import + `AddMembershipApi()` call)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` (+9 lines — using import + `MapMembershipApi()` call + commentary)

## Placement justification (per bff-extensions.md §A and project-level imperative)

The endpoint stays in the BFF because all four decision-table criteria answer "BFF":

| Question | Answer | Why |
|---|---|---|
| TTFB budget against BFF state? | YES | Per NFR-04, target p95 ≤300ms; resolver hits per-user Redis cache + Dataverse FetchXml |
| Writes to BFF-managed session/audit state in same request lifecycle? | NO | Read-only endpoint |
| Retroactive annotation of streaming response? | NO | Synchronous JSON response |
| Event-driven (timer/queue/webhook) with no user wait? | NO | Synchronous HTTP GET; clearly user-facing |

The endpoint is the synchronous user-facing surface defined by FR-1A.9; no out-of-band scheduler equivalent exists. ADR-013 default placement applies (no exception criteria met for separate-deployable extraction).

## Test surface (per bff-extensions.md §F Test Update Obligation)

The new `MembershipEndpoints.cs` ships with `MembershipEndpointsTests.cs` providing:

- **Auth contract** (3 tests): 401 unauthenticated, 200 authenticated with mocked resolver, 401 when AAD principal has no matching Dataverse systemuser row
- **Malformed input** (2 tests): 400 on `limit=0` and `limit=-5`
- **Query-param → MembershipResolveOptions mapping** (6 tests):
  - `roles` + `identityTypes` CSV → IReadOnlyList<string>
  - `includeRelated` CSV passthrough (Phase 1D activation by task 054)
  - `limit` + `continuationToken` round-trip
  - Default `limit` = 500 when not specified
  - Server-side clamp at `MembershipResolveOptions.MaxLimit` (5000)
  - Empty CSV values normalize to null (resolver treats as "use all")
- **Helper-level coverage** (7 tests): `ExtractAadObjectId` (4 variants) + `ParseCsv` (3 variants)

Total **18/18 pass** in <1s. Mock-based; no real Dataverse calls.

**Integration test deferred**: AC-1A.3 (`GET /api/users/me/memberships/sprk_matter` against seeded user) and AC-1A.5 (p95 ≤300ms over 24h soak) are deferred to P4 UAT against spaarkedev1 per the task brief — they require real AAD object ids + multi-team test users + App Insights telemetry that only exist in the deployed environment.

## Asymmetric-registration check (bff-extensions.md §F.1)

The §F.1 static scan is N/A — neither `AddMembershipApi()` nor `MapMembershipApi()` are wrapped in an `if (flag)` block. Both are unconditional. Per the rule, when the endpoint maps unconditionally, its dependencies must also map unconditionally:

- `IMembershipResolverService` — unconditional (`MembershipModule.AddMembership` line 80, task 033)
- `IDataverseService` — unconditional (registered by `Spaarke.Dataverse` module, used elsewhere)
- `IDistributedCache` — unconditional (registered by `CacheModule.AddCacheModule`, line 93 of Program.cs)

ADR-032 Null-Object Kill-Switch pattern: N/A (no feature gate; endpoint is permanent platform infrastructure).
