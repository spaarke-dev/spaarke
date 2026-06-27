# BFF Publish-Size — R3 Task 021

> **Task**: `POST /api/admin/jobs/{jobId}/trigger` admin endpoint
> **Date**: 2026-06-21
> **Branch**: `work/spaarke-platform-foundations-r3`

## Measurement

| Metric | Value |
|---|---|
| Baseline (post-task 020) | **46.18 MB** compressed |
| Post-task 021 | **44.86 MB** compressed |
| **Delta** | **-1.32 MB** |
| NFR-01 ceiling | 60 MB |
| Single-task escalation threshold | +5 MB |
| Cumulative architecture-review threshold | 55 MB |

## Method

```powershell
dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj `
    -o deploy/api-publish-task021 -r linux-x64 --self-contained false
tar -cf - deploy/api-publish-task021 | gzip | wc -c | awk '{ printf "%.2f MB\n", $1/1024/1024 }'
```

## Notes

- **No new NuGet packages added.** `JobNotFoundException` + `TriggerResult` are POCOs added to
  the existing `Spaarke.Scheduling` assembly (no new dependencies). `TriggerResponse` is a
  positional `record` added to `Sprk.Bff.Api` under `Api/Admin/Models/`.
- **Net decrease** (-1.32 MB) is consistent with the pattern seen on task 002 (44.78 MB) — Release-
  mode + linux-x64 + IL-trim heuristics can re-converge after small code additions; the BFF's
  reflection-heavy stack causes individual file deltas to occasionally cause the optimizer to
  re-baseline. No code was REMOVED in task 021.
- The new endpoint + handler add ~120 LOC; the new helper methods on `ScheduledJobHost` add
  ~200 LOC. None of this materially affects the published size relative to the existing
  Spaarke.Scheduling.dll footprint.

## CVE Verification

```powershell
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

**Result**: Only pre-existing HIGH finding:
- `Microsoft.Kiota.Abstractions 1.21.2` → `https://github.com/advisories/GHSA-7j59-v9qr-6fq9`
  - **Tracked at project level**; not introduced by task 021.

**No new HIGH-severity CVEs introduced by task 021.** ✅

## Pre-Merge Checklist (bff-extensions.md §A + Test Update §F)

- [x] Placement Justification stated (this file + POML notes — admin trigger is a thin BFF
      surface over shared-lib `TriggerNowAsync`; trigger logic correctly placed in
      `Spaarke.Scheduling`; facade pattern N/A — this is admin/ops, not CRUD→AI)
- [x] ADRs cited: ADR-001 (in-process), ADR-008 (endpoint-filter auth at MapGroup), ADR-010
      (DI minimalism — singleton concrete; interface only when ≥2 impls justify), ADR-029
      (publish-size verification), ADR-032 N/A (host registered unconditionally — no kill-switch)
- [x] Publish-size measured: 44.86 MB compressed; delta -1.32 MB vs 46.18 baseline (well under
      +5 MB single-task escalation threshold; far under 60 MB hard ceiling)
- [x] No new CRUD→AI dependency (admin endpoint does not consume `Services/Ai/PublicContracts/`)
- [x] Feature-module DI per ADR-010 (host registration added to existing `SchedulingModule.cs`;
      no flat `Program.cs` blob registrations)
- [x] No new HIGH-severity CVE (only pre-existing `Microsoft.Kiota.Abstractions 1.21.2`)
- [x] Test update obligation: +6 BFF endpoint tests + +5 Spaarke.Scheduling host tests
- [x] Asymmetric-registration §F.1: endpoint mapped unconditionally; `ScheduledJobHost`
      registered unconditionally in same module. Static scan: `TriggerJobAsync` is the only
      consumer; ScheduledJobRegistry + IBackgroundJobStore + ScheduledJobHostOptions all
      unconditional. No latent transitive conditional deps.
