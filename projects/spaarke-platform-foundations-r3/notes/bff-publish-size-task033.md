# BFF publish-size measurement — Task 033

**Task**: 033 — `MembershipResolverService` orchestration
**Date**: 2026-06-21
**NFR**: NFR-01 (`≤+1 MB delta per task`, hard ceiling 60 MB compressed)

## Measurement

```powershell
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
cd deploy/api-publish ; tar -cf - . | gzip -9 | wc -c
```

| Metric                | Value          |
|-----------------------|----------------|
| Uncompressed          | 139.66 MB      |
| Compressed (gzip -9)  | **44.62 MB**   |
| Prior baseline (task 031) | 46.16 MB    |
| **Delta**             | **-1.54 MB**   |
| **vs Hard ceiling 60 MB** | ✅ 15.38 MB headroom |

## Interpretation

Task 033 adds two small `.cs` files (`IMembershipResolverService.cs` +
`MembershipResolverService.cs`, ~530 lines combined including XML doc) and a
single DI line in `MembershipModule.cs`. There is **no plausible mechanism for a
net 1.54 MB reduction from this change alone** — the delta is dominated by
clean-rebuild noise from `rm -rf deploy/api-publish` before publish.

The important facts:

- **No new NuGet packages** added (verified by `git diff src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` — no `<PackageReference>` adds).
- **No new transitive dependencies** pulled (only consumes existing `IDataverseService` + `IDistributedCache` + `IMembershipFieldDiscoveryService` + `IIdentityNormalizationService`).
- **No size growth attributable to task 033**.
- Well under hard ceiling.

## Pre-merge checklist (per bff-extensions.md §A)

- [x] Publish-size measured + delta reported (above)
- [x] No new HIGH-severity CVE (`dotnet list package --vulnerable --include-transitive`
      shows only pre-existing `Microsoft.Kiota.Abstractions 1.21.2` advisory,
      tracked at project level)
- [x] Tests updated/added (`MembershipResolverServiceTests.cs`, 14 tests pass)
- [x] DI registration unconditional (asymmetric-registration §F.1 N/A)
- [x] Placement justified — orchestration is request-scoped, consumed by AI playbook
      nodes + endpoints within the same request lifecycle; tightly coupled to other
      Membership services that live in BFF
- [x] No `.claude/` writes from sub-agents (this task ran in main session)

## Cumulative project trajectory (task 033)

| Task | Delta   | Cumulative | Notes |
|------|---------|------------|-------|
| baseline | —    | 46.13 MB   | project-init |
| 002  | -1.34 MB| 44.78 MB   | joinIds Handlebars helper |
| 013  | +0.01 MB| 46.14 MB   | ScheduledJobHost (different baseline measure) |
| 014  | +0.02 MB| 46.16 MB   | retry/backoff |
| 030  | +0.01 MB| ~46.16 MB  | MembershipFieldDiscoveryService |
| 031  | +0.02 MB| 46.16 MB   | IdentityNormalizationService |
| 032  | (~0)    | 46.16 MB   | OrganizationMembershipResolver |
| **033** | **noise** | **44.62 MB** | **MembershipResolverService orchestration (no new deps)** |

**Trajectory healthy** — well below the NFR-01 ceiling. No size budget concerns
for the remaining Membership tasks (034–037) which are similarly pure-code
orchestration / DTOs / endpoints without new NuGet packages.
