# BFF Publish-Size Baseline

> Captured at project initialization. Used as the comparison point for all subsequent BFF-touching tasks (P2b: tasks 030–033, P3: tasks 040–042) per CLAUDE.md §10 R4 NFR-01.

## Capture metadata

| Field | Value |
|---|---|
| Date | 2026-06-18 |
| Branch | `work/spaarke-daily-update-service-r2` |
| Git SHA | `f7669c4d71c7` (HEAD at capture) |
| Build config | Release |
| Publish target | `c:\tmp\sprk-bff-baseline-publish\` |
| Capture command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o c:\tmp\sprk-bff-baseline-publish\` |

## Size measurements

| Metric | Value |
|---|---|
| File count | 261 |
| Uncompressed | 139.23 MB |
| **Compressed (zip, Optimal)** | **46.01 MB** |

## Comparison to last documented value

Per CLAUDE.md §10: "Current baseline as of 2026-05-26: ~45.65 MB (post-Phase 5 Outcome A)."

| Reference | Compressed | Delta vs. R2 baseline |
|---|---|---|
| CLAUDE.md §10 (2026-05-26) | 45.65 MB | +0.36 MB drift before R2 starts |
| **R2 baseline (this file, 2026-06-18)** | **46.01 MB** | — |

The +0.36 MB drift over ~3 weeks is **not attributable to R2** (R2 has not modified BFF yet); it reflects merged work since the 2026-05-26 measurement. Tasks 033 + 042 will measure R2's delta vs. this 46.01 MB baseline.

## R2 budget

- **Spec target (NFR-04)**: ≤ +1 MB compressed delta vs. baseline → R2 max budget: **≤ 47.01 MB compressed**
- **§10 binding ceiling**: 60 MB compressed (HARD STOP). R2 has ~14 MB headroom.

## Vulnerable packages snapshot (NFR-06 baseline)

Command: `dotnet list package --vulnerable --include-transitive` (from `src/server/api/Sprk.Bff.Api/`).

| Package | Resolved | Severity | Advisory | Status |
|---|---|---|---|---|
| `Microsoft.Kiota.Abstractions` | 1.21.2 | High | [GHSA-7j59-v9qr-6fq9](https://github.com/advisories/GHSA-7j59-v9qr-6fq9) | **Pre-existing** — not introduced by R2; tasks 033 + 042 verify no NEW high-severity CVEs |

NFR-06: "No new HIGH-severity CVEs from `dotnet list package --vulnerable --include-transitive` after BFF changes (per CLAUDE.md §10 bullet 5)." The pre-existing Kiota CVE is acceptable as baseline; R2 tasks must verify zero **additional** high-severity CVEs after their changes land.

## How tasks 033 and 042 use this file

1. Run `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` (or equivalent target directory).
2. Compress + measure the same way (zip, Optimal).
3. Write `notes/bff-size-p2b.md` (task 033) / `notes/bff-size-p3.md` (task 042) recording:
   - Absolute compressed size
   - Delta vs. **46.01 MB** (this baseline)
   - CVE diff vs. the table above
4. Acceptance: delta ≤ +1 MB; no new HIGH-severity CVE.

## Build verification at capture

`dotnet build src/server/api/Sprk.Bff.Api/` ran clean at Step A of project-pipeline: 0 errors, ~12 warnings (all pre-existing CS1998 / CS0618 / CS8601 / CS8604 — none introduced by R2).
