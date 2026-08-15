# Publish-Size Re-Baseline (task 031, FR-12 / NFR-02)

> **Date**: 2026-08-13 · **Task**: 031 · **Gate**: 033 complete (Graph 6.5 / Kiota 2.0 landed — this measures the FINAL post-033 package graph).
> **Rule**: `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule (NFR-01)" · root CLAUDE.md §10.

---

## Measurement

- **Command**: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`
- **Compression convention** (matches 2026-07-08 task-055 baseline): PowerShell `Compress-Archive -CompressionLevel Optimal` over all files in `deploy/api-publish/`.
- **Entry count**: 215 files (4 `.pdb`).
- **Publish shape**: framework-dependent linux-x64 — **no `runtimes/` RID tree** (ADR-029 verified: `SelfContained=false`, `RuntimeIdentifier=linux-x64`); no `PublishTrimmed` / `PublishAot` / `RuntimeFrameworkVersion`.

| Convention | net10 (2026-08-13) | Baseline (2026-07-08) | Delta |
|---|---|---|---|
| **Compressed incl. PDBs** | **44.96 MB** | 49.63 MB | **−4.67 MB** |
| Compressed excl. PDBs | 44.05 MB | 45.87 MB | −1.82 MB |

**PDB convention**: the headline governance number is **incl. PDBs** (as the 2026-07-08 baseline was). New baseline = **44.96 MB incl. PDBs** (44.05 MB excl.).

## Verdict

- ✅ **≤60 MB ceiling** (NFR-02): 44.96 MB — **15.04 MB of headroom**.
- ✅ **Negative delta** (−4.67 MB): the net10 retarget SHRANK the publish. No escalation (well below the ≥+5 MB single-task and ≥55 MB cumulative thresholds).
- Framework-dependent publish confirmed (no self-contained `runtimes/` tree).

### Why it shrank despite adding Graph 6.5 / Kiota 2.0

Net reductions outweighed the Graph-6 add:
- **FR-04 pin removals** — `System.Text.Json`, `System.Formats.Asn1`, `System.Text.RegularExpressions`, `System.Security.Cryptography.Xml` no longer ship as standalone assemblies (net10 shared framework supplies them → not copied to publish).
- **FR-06** — classic `Microsoft.ApplicationInsights.AspNetCore` SDK removed (task 014); OTel → Azure Monitor is the sole telemetry path.
- **net10 shared framework** supersedes more inbox assemblies than net8 did (NU1510 pruning), so fewer `System.*` DLLs land in the publish dir.
- The Graph 5→6.5 / Kiota 1→2 move (task 033) is roughly size-neutral (same SDK family, transitive Kiota replaces 7 direct Kiota assemblies of similar footprint).

## Governance updates applied (main-session-only writes, root §3)

- **root `CLAUDE.md` §10** — baseline number 49.63 → 44.96 MB (incl. PDBs), date 2026-08-13, task 031.
- **`.claude/constraints/azure-deployment.md`** — both the "Publish & Packaging" bullet and the "BFF Publish-Size Per-Task Verification Rule (NFR-01)" baseline updated to 44.96 MB incl. PDBs (44.05 excl.), 2026-08-13.

The r3 handoff note (task 090) must record that this baseline moved 49.63 → 44.96 MB incl. PDBs.
