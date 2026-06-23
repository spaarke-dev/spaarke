# BFF Publish-Size Measurement — R3 Task 032

> **Task**: 032 — Define + implement `sprk_organization` user-mapping mechanism
> **Date**: 2026-06-21
> **Per**: CLAUDE.md §10 BFF Hygiene + `.claude/constraints/azure-deployment.md` NFR-01 verification rule

---

## Inputs

| Field | Value |
|---|---|
| Build | `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` → **succeeded** (0 errors, 0 warnings) |
| Publish command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish-task032/` |
| Configuration | Release / linux-x64 / framework-dependent |
| Compression method | PowerShell `Compress-Archive` (matches deploy ZIP convention) |

---

## Measurements

| Metric | Value |
|---|---|
| Compressed publish ZIP | **46.16 MB** (48,397,039 bytes) |
| Publish entry count | 264 files |
| Baseline (task 013, 2026-06-21) | 46.14 MB |
| **Delta vs baseline** | **+0.02 MB (~20 KB)** |
| NFR-01 per-task ceiling | +1 MB |
| Hard ceiling | 60 MB |
| Status | ✅ **Within budget** (~2% of per-task ceiling) |

### Per-asset contribution

| Asset | Size impact | Notes |
|---|---|---|
| `Sprk.Bff.Api.dll` | new types added: `IOrganizationMembershipResolver`, `OrganizationMembershipResolver`, `OrganizationLookupOptions` (nested in `MembershipOptions`), `PersonIdentity` (placeholder; full shape lands via task 031) | ~20 KB IL across the new contract + implementation + tests. No new NuGet packages. |

No new NuGet packages added by this task. `Sprk.Bff.Api.csproj` unchanged.

---

## Vulnerability scan

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

| Severity | Package | Advisory | Status |
|---|---|---|---|
| High | `Microsoft.Kiota.Abstractions 1.21.2` | GHSA-7j59-v9qr-6fq9 | **Pre-existing** — already present in task 010, 012, 013 measurements; not introduced by this task. |

Result: **no NEW HIGH CVE introduced by this task**.

---

## Conclusion

✅ **Within NFR-01 budget.** Task 032 ships the organization-membership resolution mechanism (Option b — configurable Lookup field on `sprk_organization`; see `notes/sprk-organization-mapping-decision.md`). No new packages. Publish-size impact is +20 KB (compiled IL for the new types). No new HIGH CVE.

The 46.16 MB number becomes the new baseline for the next task in the membership pipeline (033 — `MembershipResolverService`).
