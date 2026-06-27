# BFF Publish-Size + CVE Verification — Combined P2b + P3 Report

> **Scope**: Combined verification covering both P2b (tasks 030 + 031) and P3 (task 040; tests added in 041) BFF modifications. Task 042 measures the cumulative delta after all BFF code changes in R2 are in flight.
>
> **Authored by**: task 042 (`042-p3-publish-size-and-e2e-note.poml`).
> **Supersedes**: the originally-planned separate `bff-size-p2b.md` + `bff-size-p3.md` (combined per task 042 `<notes>` recommendation and Wave 3 instructions).

---

## Capture metadata

| Field | Value |
|---|---|
| Date | 2026-06-18 |
| Branch | `work/spaarke-daily-update-service-r2` |
| Git SHA (HEAD at capture) | `c8aba0f938db` |
| Build config | Release |
| Publish target | `c:\tmp\sprk-bff-post-p2b-p3-publish\` (isolated from concurrent agents) |
| Capture command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o c:\tmp\sprk-bff-post-p2b-p3-publish\` |
| Build elapsed | 6.66 s |

## R2 BFF modifications included in this measurement

| Phase | Task(s) | Files modified | Type |
|---|---|---|---|
| P2b | 030 + 031 | `Api/Ai/DailyBriefingEndpoints.cs` (re-baselined per task 030); test additions in `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/DailyBriefingEndpointsTests.cs` (task 031) | Pure code — endpoint behavior + tests |
| P3 | 040 + 041 | `Services/Ai/Nodes/CreateNotificationNodeExecutor.cs` (task 040); test additions for AI `primaryEntityId` validation (task 041) | Pure code — server-side validation + tests |

**No new NuGet dependencies** added by any of the above. All modifications are pure code edits within existing namespaces and DI registrations.

## Size measurements

| Metric | Post-P2b+P3 (this measurement) | Baseline (`bff-baseline.md`, 2026-06-18) | Delta |
|---|---|---|---|
| File count | 261 | 261 | **0** |
| Uncompressed | 139.23 MB | 139.23 MB | **0.00 MB** |
| **Compressed (zip, Optimal)** | **46.01 MB** | **46.01 MB** | **+0.00 MB** |
| Compressed bytes | 48,248,232 | (matches) | 0 bytes |

### Acceptance vs. spec NFR-04

- **Spec NFR-04**: ≤ +1 MB compressed delta vs. baseline → R2 max budget: ≤ 47.01 MB compressed.
- **§10 NFR-01 binding ceiling**: 60 MB compressed (HARD STOP).
- **Measured**: 46.01 MB (delta = +0.00 MB).
- **Status**: ✅ PASS — well under NFR-04 budget and §10 ceiling. R2 has ~13.99 MB of headroom remaining against the §10 hard ceiling.

The zero delta confirms the spec NFR-04 expectation: "The only BFF changes (P2b + P3) are pure code; no new NuGet dependencies expected." Pure-code edits inside existing files compile to byte-identical (or near-identical) IL within the same publish output structure.

## CVE diff vs. baseline (NFR-06)

Command: `dotnet list package --vulnerable --include-transitive` (run from `src/server/api/Sprk.Bff.Api/`).

| Package | Resolved | Severity | Advisory | Status vs. baseline |
|---|---|---|---|---|
| `Microsoft.Kiota.Abstractions` | 1.21.2 | High | [GHSA-7j59-v9qr-6fq9](https://github.com/advisories/GHSA-7j59-v9qr-6fq9) | **Pre-existing** — identical to baseline; not introduced by R2 |

### Acceptance vs. spec NFR-06 + §10 bullet 5

- **NFR-06**: "No new HIGH-severity CVEs from `dotnet list package --vulnerable --include-transitive` after BFF changes."
- **Measured**: 1 HIGH-severity CVE — the pre-existing Kiota.Abstractions advisory carried as baseline.
- **Diff vs. baseline**: **0 new advisories**. No new packages introduced. No version changes to existing transitive dependencies.
- **Status**: ✅ PASS — zero new HIGH-severity CVEs.

## Acceptance criteria (task 042)

| Criterion | Status | Evidence |
|---|---|---|
| Publish-size delta vs. baseline recorded | ✅ | Table above: +0.00 MB |
| Delta ≤ +1 MB compressed | ✅ | +0.00 MB ≪ +1.00 MB ceiling |
| No new HIGH-severity CVE | ✅ | 1 CVE total, all pre-existing (Kiota.Abstractions) |
| E2E test plan for MDA bell documented for task 062 | ✅ | See sibling note `p3-e2e-test-plan.md` |

## Notes

- Identical byte-for-byte compressed output (48,248,232 bytes) to baseline is expected for pure-code modifications when no new types, namespaces, or assembly references are added. The DailyBriefing endpoint changes (P2b) and `CreateNotificationNodeExecutor` validation (P3) are confined to existing source files.
- Tests in `tests/unit/Sprk.Bff.Api.Tests/` do NOT ship in the BFF publish output (separate project, not referenced by `Sprk.Bff.Api.csproj` for publish). Test additions therefore have zero impact on this measurement.
- This measurement was taken with concurrent Wave 3 agents potentially active. Output directory `c:\tmp\sprk-bff-post-p2b-p3-publish\` was uniquely named to avoid race with concurrent `dotnet test` or other publish operations.

## Cross-reference

- Baseline: [`bff-baseline.md`](bff-baseline.md) (46.01 MB, 2026-06-18)
- E2E test plan: [`p3-e2e-test-plan.md`](p3-e2e-test-plan.md)
- Spec: [`../spec.md`](../spec.md) §NFR-04 + §NFR-06
- Constraint: [`.claude/constraints/bff-extensions.md`](../../../.claude/constraints/bff-extensions.md) §§ A, F
- Constraint: [`.claude/constraints/azure-deployment.md`](../../../.claude/constraints/azure-deployment.md) "BFF Publish-Size Per-Task Verification Rule (NFR-01)"
- CLAUDE.md §10 bullets 4 + 5 (publish-size verification + CVE check)
