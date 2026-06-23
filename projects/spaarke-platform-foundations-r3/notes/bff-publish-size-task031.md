# BFF Publish Size — Task 031 (IdentityNormalizationService)

> **Task**: `031-identity-normalization-service.poml`
> **Date**: 2026-06-21
> **Rule**: per `.claude/constraints/azure-deployment.md` BFF Publish-Size Per-Task Verification Rule (NFR-01)

## Measurement

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish-031
Compress-Archive -Path deploy/api-publish-031/* -DestinationPath deploy/api-publish-031.zip
```

| Metric | Value |
|---|---|
| Uncompressed | 146,373,518 bytes (~139.6 MB) |
| Compressed (zip) | **46.16 MB** (48,397,039 bytes) |
| Prior baseline (task 013) | 46.14 MB |
| **Delta vs prior task** | **+0.02 MB** |
| Cumulative delta vs Phase 5 Outcome A baseline (45.65 MB) | +0.51 MB |
| NFR-01 ≤+1 MB per-task threshold | ✅ PASS |
| Hard ceiling 60 MB | ✅ PASS (well under) |

## Source of delta

Task 031 added 4 source files (`PersonIdentity.cs`, `IIdentityNormalizationService.cs`,
`IIdentityOrganizationResolver.cs`, `IdentityNormalizationService.cs`) and modified
`MembershipModule.cs`. **No new NuGet packages** — uses existing `IDataverseService`
(Spaarke.Dataverse), `IDistributedCache` (existing CacheModule), `Microsoft.Xrm.Sdk`
types already on the Spaarke.Dataverse transitive graph. The +0.02 MB delta is purely
the compiled IL for the 4 new files.

## CVE check

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

| Package | Severity | Source | Introduced by task 031? |
|---|---|---|---|
| `Microsoft.Kiota.Abstractions 1.21.2` | High | GHSA-7j59-v9qr-6fq9 | ❌ No — pre-existing (same finding present at task 013 baseline; see `bff-publish-size-task013.md`) |

**No new HIGH-severity CVE introduced by task 031.** The Kiota.Abstractions finding
is tracked at the project level (transitive via Microsoft.Graph 5.99.0 → Kiota chain
pinned in `src/server/api/Sprk.Bff.Api/CLAUDE.md`); upgrade requires a coordinated
bump of all 7 Kiota packages (separate task — not in 031 scope).

## Pre-merge checklist (bff-extensions.md §A)

- [x] Placement justification: BFF (membership resolution is request-scoped, part of
      identity-normalization pipeline that ships in same request lifecycle as
      MembershipResolverService — task 033)
- [x] ADR citations: ADR-028 (cross-ref via azureactivedirectoryobjectid),
      ADR-009 (Redis 10-min TTL), ADR-024 (polymorphic-resolver pattern informs structure),
      ADR-010 (DI minimalism — concrete + interface as testing seam)
- [x] No new direct package references (NuGet graph unchanged)
- [x] No new CRUD→AI dependency (this lives under Services/Ai/Membership/ — internal
      to the Ai surface; consumers are AI playbook nodes + future MembershipEndpoint)
- [x] Feature-module DI: registered via `AddMembership()` extension in `MembershipModule.cs`
- [x] No new HIGH-severity CVE introduced
- [x] Test update obligation (bff-extensions.md §F): 10 new unit tests added in
      `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/IdentityNormalizationServiceTests.cs`
- [x] Asymmetric-registration check (§F.1): N/A — `IIdentityNormalizationService` is
      registered UNCONDITIONALLY (no feature flag). No endpoints map yet; future
      endpoints (task 035) will be unconditional too.
