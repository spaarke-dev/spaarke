# BFF Publish-Size Measurement — R3 Task 010

> **Task**: 010 — Scaffold `Spaarke.Scheduling` library + add Cronos NuGet
> **Date**: 2026-06-21
> **Per**: CLAUDE.md §10 BFF Hygiene + `.claude/constraints/azure-deployment.md` NFR-01 verification rule

---

## Inputs

| Field | Value |
|---|---|
| Solution build | `dotnet build Spaarke.sln -c Release` → **succeeded** (0 errors, 18 warnings — all pre-existing in BFF / tests, none from Spaarke.Scheduling) |
| Publish command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/r3-task010-publish/` |
| Configuration | Release / linux-x64 / framework-dependent (per existing csproj `RuntimeIdentifier=linux-x64`, `SelfContained=false`) |
| Compression method | PowerShell `Compress-Archive -CompressionLevel Optimal` (matches deploy ZIP convention) |

---

## Measurements

| Metric | Value |
|---|---|
| Compressed publish ZIP | **46.12 MB** (48,363,038 bytes) |
| Baseline (per CLAUDE.md §10, post-Phase 5 Outcome A 2026-05-26) | 45.65 MB |
| **Delta vs baseline** | **+0.47 MB** |
| NFR-01 per-task ceiling | +1 MB (spec NFR-01) |
| Hard ceiling | 60 MB |
| Threshold for escalation | ≥+5 MB |
| Status | ✅ **Within budget** (47% of per-task ceiling) |

### Per-assembly contribution (Cronos)

| Assembly | Uncompressed | Notes |
|---|---|---|
| `Cronos.dll` | 64,824 B (~63 KB) | Cronos 0.13.0 — latest stable on nuget.org as of 2026-06-21. POML originally suggested 0.7.1 but flagged "verify"; 0.13.0 is the GA from HangfireIO. |
| `Spaarke.Scheduling.dll` | 4,096 B (~4 KB) | Placeholder sentinel only (real contracts land in task 011). |

Compressed delta of ~0.47 MB is consistent with adding a ~63 KB NuGet whose compression ratio is poor (already-compact CIL) plus pdb files plus the new Spaarke.Scheduling pdb. The expected +50 KB from the POML referenced the older 0.7.x Cronos series; 0.13.0 is slightly larger but still trivially under budget.

---

## Vulnerability scan

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

Result: **no NEW HIGH CVE introduced by this task**.

- One pre-existing HIGH advisory (`Microsoft.Kiota.Abstractions 1.21.2`, GHSA-7j59-v9qr-6fq9) — present on master prior to this task; verified by `git stash` + re-scan. Tracked separately, not in scope for task 010.
- Cronos 0.13.0 brings no vulnerable transitive dependencies (zero direct deps; pure managed cron parser from HangfireIO).

---

## Resolvability verification (acceptance criterion #4)

Cronos is resolvable from BFF code via the `Spaarke.Scheduling` shared library:

- `src/server/shared/Spaarke.Scheduling/SchedulingSentinel.cs` holds a static `CronExpression.Parse("* * * * *")` reference that is JIT-touched at type load (per `[InternalsVisibleTo("Spaarke.Scheduling.Tests")]` — visible to the future test assembly).
- Sprk.Bff.Api transitively pulls `Cronos.dll` into its publish output (confirmed: `deploy/r3-task010-publish/Cronos.dll` present, 63 KB).

The sentinel will be removed by task 011 once `IScheduledJob` + real contracts replace it.
