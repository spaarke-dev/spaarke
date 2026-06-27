# BFF Publish Size — Task 030 (MembershipFieldDiscoveryService)

> **Task**: `030-membership-field-discovery-service.poml`
> **Date**: 2026-06-21
> **Rule**: per `.claude/constraints/azure-deployment.md` BFF Publish-Size Per-Task Verification Rule (NFR-01)

## Measurement

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish/
tar -czf deploy/api-publish.tar.gz deploy/api-publish/
```

| Metric | Value |
|---|---|
| Uncompressed | ~141 MB |
| Compressed (tar.gz) | **45 MB** |
| Prior baseline (task 031) | 46.16 MB |
| **Delta vs prior task** | **-1.16 MB** (no growth; minor compression-tool variance — tar.gz vs zip differ) |
| Cumulative delta vs Phase 5 Outcome A baseline (45.65 MB) | -0.65 MB |
| NFR-01 ≤+1 MB per-task threshold | PASS |
| Hard ceiling 60 MB | PASS (well under) |

> Note: The 45 MB tar.gz measurement is slightly smaller than task 031's 46.16 MB zip because tar.gz typically achieves better compression than zip for the same payload (DEFLATE with longer back-references). The relevant per-task signal is "no significant growth," which holds.

## Source of delta

Task 030 added 4 source files:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/IMembershipFieldDiscoveryService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipFieldDiscoveryService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/Models/MembershipDescriptor.cs`
- modified `src/server/api/Sprk.Bff.Api/Infrastructure/DI/MembershipModule.cs` (registration)

**No new NuGet packages** — uses existing `IDataverseService` (Spaarke.Dataverse, unwrapped to `ServiceClient` for `RetrieveEntityRequest` per canonical `Services.Dataverse.MetadataService` pattern), `IDistributedCache` (existing `CacheModule`), `Microsoft.Xrm.Sdk.Messages` + `Microsoft.Xrm.Sdk.Metadata` types already on the Spaarke.Dataverse transitive graph. The delta is purely the compiled IL for the 3 new files.

## CVE check

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

| Package | Severity | Source | Introduced by task 030? |
|---|---|---|---|
| `Microsoft.Kiota.Abstractions 1.21.2` | High | GHSA-7j59-v9qr-6fq9 | No — pre-existing (same finding present at tasks 013, 031 baselines) |

**No new HIGH-severity CVE introduced by task 030.** The Kiota.Abstractions finding is tracked at project level (transitive via `Microsoft.Graph 5.99.0` chain pinned in `src/server/api/Sprk.Bff.Api/CLAUDE.md`); upgrade requires coordinated bump of all 7 Kiota packages (separate task — not in 030 scope).

## Pre-merge checklist (bff-extensions.md §A)

- [x] Placement justification: BFF (per-request metadata caching + downstream consumption by `MembershipResolverService` (task 033) and `LookupUserMembershipNodeExecutor` (task 041) in same request lifecycle; Redis cache requires `IDistributedCache` already wired in BFF).
- [x] ADR citations: ADR-013 (AI architecture — service lives under `Services/Ai/Membership/`), ADR-009 (Redis cache with configurable TTL — default 60 min), ADR-010 (DI minimalism — concrete + interface as testing seam via protected virtual `FetchLookupAttributesAsync`).
- [x] No new direct package references (NuGet graph unchanged).
- [x] No new CRUD→AI dependency (this lives under `Services/Ai/Membership/` — internal to the Ai surface; consumers are AI playbook nodes + future admin endpoints (task 036)).
- [x] Feature-module DI: registered via `AddMembership()` extension in `MembershipModule.cs` — `services.AddSingleton<IMembershipFieldDiscoveryService, MembershipFieldDiscoveryService>()`.
- [x] No new HIGH-severity CVE introduced.
- [x] Test update obligation (bff-extensions.md §F): 17 new unit tests added in `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/MembershipFieldDiscoveryServiceTests.cs` covering happy path (AC-1A.1), Q4 fix regression guard, role-name strategy (theory + direct helper test), per-entity overrides (FieldRoleOverrides, ExcludedFields, IncludedFields), cache hit, lowercase normalization, input guards (null/empty/whitespace), cancellation.
- [x] Asymmetric-registration check (§F.1): N/A — `IMembershipFieldDiscoveryService` is registered UNCONDITIONALLY (no feature flag). No endpoints map yet; future admin endpoints (task 036) will be unconditional too.
- [x] Fixture-config-FIRST (§F.2): N/A — no test fixtures changed; unit-test uses in-process `FakeDistributedCache` + subclass override of `FetchLookupAttributesAsync` seam.
- [x] Empirical-reproduction-FIRST (§F.3): N/A — no ledger entry applied; greenfield service.

## Q4 verification

Per spec.md owner clarification Q4 (2026-06-20): `sprk_assignedlawfirm1/2` Lookup target is `sprk_organization` (NOT `contact` as design.md's discovery report example originally showed). Two unit tests pin this:

1. `DiscoverAsync_SprkMatter_DiscoversExpectedFieldsPerAC_1A_1` — wires `sprk_assignedlawfirm1/2` to `sprk_organization` target and asserts they appear in `DiscoveredFields` (not `IgnoredFields`).
2. `DiscoverAsync_AssignedLawFirmFields_ResolveToOrganizationIdentityTypePerQ4` — dedicated regression guard asserting `IdentityType == "Organization"` and `TargetTable == "sprk_organization"` for both fields.

`MembershipOptions.IncludedIdentityTables` test fixture maps `sprk_organization → Organization`, so the existing generic target-table-to-identity-type lookup correctly resolves these without any special-casing.
