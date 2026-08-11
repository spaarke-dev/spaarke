# dotnet-10-upgrade-r1

> **Created**: 2026-08-10 · **Status**: Design (initial) · **Driver**: .NET 8 EOL **2026-11-10**

Upgrade the Spaarke server backend from **.NET 8 → .NET 10 (LTS)** before .NET 8 loses support.

## What this is

A **support-lifecycle** migration — supported runtime, zero behavior change, "no issues." Scope is server-side .NET only:

- ✅ In scope: `Sprk.Bff.Api`, `Spaarke.Core`, `Spaarke.Dataverse`, `Spaarke.Scheduling`, ~7 test projects → `net10.0`
- ❌ Out of scope: the `net462` Dataverse plugin (sandbox-fixed), client/PCF/Code-Page surfaces, and 6 optional library major-bumps

## Key facts (verified 2026-08-10)

- **.NET 8 EOL 2026-11-10** (~3 months). .NET 9 EOL same day → **skip 9, go straight to 10 (LTS, supported to 2028-11-14)**.
- **No hard package blockers.** Ships with patch/minor bumps + one required minor (`Dataverse.Client 1.1.32 → 1.2.26`).
- **App Service .NET 10 is GA** (`DOTNETCORE|10.0`). `linuxFxVersion` is slot-swapped → **zero-downtime cutover path** exists.
- **6 concrete codebase hit-sites** found by research greps — the big one is the `BackgroundService.ExecuteAsync` threading change (~10+ workers).

## Documents

- [`design.md`](design.md) — **the analysis** (lifecycle, hit-sites, package strategy, deployment sequencing, phasing, verification, relationship to `code-quality-and-assurance-r3`)
- `spec.md` — TBD (`/design-to-spec` after open question §13-A is decided)
- `plan.md` / `tasks/` — TBD (`/project-pipeline`)

## Top decision before pipeline

**§13-A — relationship to `code-quality-and-assurance-r3`**: fold in as r3's first ("Runtime") workstream (recommended) vs ship standalone. See [`design.md` §11](design.md).

## Hot-path

BFF **Y** · SpaarkeAi **N** · CI Workflows **Y** · Skill Directives **Y** · Root CLAUDE.md **Y**
