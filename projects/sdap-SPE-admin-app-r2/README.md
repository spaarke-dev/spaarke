# SDAP SPE Admin App — R2 (SpeAdminGraphService decomposition)

> **Status**: INITIALIZED (folder + design + research; execution not started, operator-gated)
> **Lineage**: follow-on to [`sdap-SPE-admin-app-r1`](../sdap-SPE-admin-app-r1/) (the original build, ✅ complete Mar 2026). R2 = the maintainability/decomposition remediation of what R1 built.
> **Origin**: code-quality-and-assurance-r3 follow-on (RED-1). Investigation research: [`notes/RED-1-investigation-research.md`](notes/RED-1-investigation-research.md).
> **Epic**: Code Quality (#427) · **Type**: refactor / decomposition · **Surface**: BFF (`Sprk.Bff.Api`)

## One-liner

Decompose `Infrastructure/Graph/SpeAdminGraphService.cs` — the codebase's largest server file (**~4,300–4,900
LOC, ~100 methods, little internal structure**) — into cohesive per-concern services (Containers / Permissions /
Drives / …), **behavior-preserving**. The goal is **reduced complexity/cohesion**, not merely fewer lines
(`docs/standards/COMPONENT-COMPLEXITY.md`): split where responsibilities genuinely diverge.

## Why now

- It is the single largest, lowest-cohesion server component — ~100 methods in one class is unreviewable and
  hard to test at the seam. It repeatedly tops the large-file **observation report**
  (`scripts/report-large-server-files.ps1`).
- **Low risk / high leverage**: phase-1 can be a byte-neutral partial-class split; later phases extract real
  per-concern services.

## Quick links

- [design.md](design.md) — problem, phased approach, scope, risks, acceptance, hot-path declaration
- [notes/RED-1-investigation-research.md](notes/RED-1-investigation-research.md) — the RED-1 investigation/research
- Standard: `docs/standards/COMPONENT-COMPLEXITY.md` (evaluate complexity, not LOC — the God-class LOC ratchet was retired 2026-08-20)

## Graduation criteria (finalize at `/design-to-spec`)

- [ ] `SpeAdminGraphService` split into cohesive per-concern components; **no single component carries multiple
      diverged responsibilities** (complexity reduced per `docs/standards/COMPONENT-COMPLEXITY.md`) — success is
      measured by cohesion, not just a line-count drop.
- [ ] Public contract unchanged (route-dump identical; SpeAdmin integration tests green).
- [ ] Build 0/0 under the analyzer gate; publish size neutral; no new NuGet.
- [ ] No longer an outlier in the large-file observation report.
- [ ] `/conflict-check` clean before each PR.
