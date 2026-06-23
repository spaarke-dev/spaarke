# BFF Publish-Size Measurement — R3 Task 012

> **Task**: 012 — `MembershipOptions` placeholder + appsettings binding skeleton
> **Date**: 2026-06-21
> **Per**: CLAUDE.md §10 BFF Hygiene + `.claude/constraints/azure-deployment.md` NFR-01 verification rule

---

## Inputs

| Field | Value |
|---|---|
| Build | `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` → **succeeded** (0 errors, 16 warnings — all pre-existing, none from MembershipModule / MembershipOptions) |
| Publish command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` |
| Configuration | Release / linux-x64 / framework-dependent |
| Compression method | PowerShell `Compress-Archive` (matches deploy ZIP convention) |

---

## Measurements

| Metric | Value |
|---|---|
| Compressed publish ZIP | **46.13 MB** |
| Baseline (task 010 measurement, 2026-06-21) | 46.12 MB |
| **Delta vs baseline** | **+0.01 MB (~10 KB)** |
| NFR-01 per-task ceiling | +1 MB |
| Hard ceiling | 60 MB |
| Status | ✅ **Within budget** (1% of per-task ceiling) |

### Per-asset contribution

| Asset | Size | Notes |
|---|---|---|
| `MembershipOptions.cs` → compiled into `Sprk.Bff.Api.dll` | a few hundred bytes IL | 5 properties + 2 nested classes; trivial. |
| `MembershipModule.cs` → compiled into `Sprk.Bff.Api.dll` | tens of bytes IL | One static extension method calling `services.Configure<T>(...)`. |
| `appsettings.Development.json.template` | ~970 B | Copied to publish output by SDK. Dev-only template (operators copy to `appsettings.Development.json` which is gitignored). |

No new NuGet packages added; csproj unchanged by this task.

---

## Vulnerability scan

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

Result: **no NEW HIGH CVE introduced by this task**.

- The single pre-existing HIGH advisory (`Microsoft.Kiota.Abstractions 1.21.2`, GHSA-7j59-v9qr-6fq9) is unchanged from task 010 measurement — verified via `git diff HEAD -- src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` showing only the task 010 `Spaarke.Scheduling` ProjectReference diff (no package changes from this task).

---

## Resolvability verification (acceptance criteria #1, #2)

- `IOptions<MembershipOptions>` resolves from the DI container after `services.AddMembership(config)` — verified by `MembershipOptionsTests.AddMembership_BindsOptionsFromConfiguration` (4/4 tests pass).
- `EntityOverrides["sprk_matter"].FieldRoleOverrides["sprk_assignedlawfirm1"]` binds to `"assignedLawFirm"` — verified by the same test.
- Empty / absent `"Membership"` section still resolves defaults — verified by `AddMembership_WithEmptyConfig_StillResolvesDefaults`.
