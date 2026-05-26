# Publish Output Size by Category (Task 016)

> **Source**: `deploy/api-publish/` after `dotnet publish -c Release` (no `--runtime` flag)
> **Captured**: 2026-05-24
> **Comparison baselines**:
> - Constraint: ~60 MB compressed / ~240 entries (per `.claude/constraints/azure-deployment.md`)
> - 2026-05-19 drift point: 75.19 MB compressed / 212 MB uncompressed
> - **Today (2026-05-24)**: 75.2 MB compressed (twice-verified via deploy script) / **212 MB uncompressed**

---

## Size by category

| Category | Size (bytes) | Size (MB) | % of total | Phase 4 impact |
|---|---:|---:|---:|---|
| **Managed .dll (top-level)** | 115,000,656 | ~109 | 51.6% | Largely fixed; trim via package removal (HIGH risk) |
| **Native binaries (`runtimes/`)** | 81,029,607 | ~77 | 36.4% | **FR-A1 RID trim**: ~54-67 MB savings |
| **wwwroot assets** | 9,586,516 | ~9 | 4.3% | **FR-A2 sourcemap exclusion**: ~few MB savings |
| **Runtime metadata (.deps.json, .runtimeconfig.json, .pdb, .xml)** | 1,673,917 | ~1.6 | 0.75% | Largely fixed |
| **Config + web.config** | 89,388 | ~0.085 | 0.04% | Fixed |
| **Exe + misc** | 151,552 | ~0.14 | 0.07% | Fixed |
| **TOTAL** | 222,835,032 | **212** | 100% | |

---

## Cross-check with current deploy

| Metric | Value | vs baseline | vs drift point | Status |
|---|---|---|---|---|
| Uncompressed | 212 MB | +152 MB over 60 MB compressed baseline (note: baseline was COMPRESSED) | == 212 MB drift point | matches |
| Compressed (zip) | 75.2 MB (per deploy script output) | +15.2 MB over 60 MB | == 75.19 MB drift point | matches |
| File count | 287 (find -type f) | +47 over 240 baseline | n/a | drift |
| DLL count | 216 (find -name "*.dll") | n/a | n/a | reference |
| Recursive .dll+.exe+.pdb+.deps.json+.runtimeconfig.json+web.config | 223 | n/a | n/a | what deploy validates |

---

## Phase 4 outcome A trajectory

Based on this inventory, Outcome A SAFE candidates project the following savings:

| Candidate | Tier | Est. uncompressed savings | Est. compressed savings |
|---|---|---:|---:|
| FR-A1 (single RID — `linux-x64` for prod) | SAFE (size-only) | ~67 MB | ~15-20 MB |
| FR-A2 (exclude `*.js.map`) | SAFE | ~5-7 MB | ~2-3 MB |
| FR-A3 (Cosmos ServiceInterop dedup) | SAFE | 0 MB (NOT FOUND in current publish) | 0 MB |
| **Combined SAFE Outcome A** | | **~72-74 MB** | **~17-23 MB** |

**Projected post-Outcome-A compressed size**: 75 - 20 = **~55 MB compressed** (under 60 MB baseline ✅)

**However**: the dev/prod OS mismatch (see `native-binaries.md`) complicates FR-A1. If dev keeps `win-x64` and prod uses `linux-x64`, dev savings are smaller (~54 MB uncompressed) and CI publish must handle both.

---

## Drift attribution

The 75.19 MB → 75.2 MB (essentially zero drift over 5 days) suggests the BFF deploy package size is stable at the moment. The earlier 60 MB → 75 MB jump (per design.md) happened in the period preceding 2026-05-19 and is the project's remediation target.

Inventory matches design's "Driver" framing. No surprises in current state.
