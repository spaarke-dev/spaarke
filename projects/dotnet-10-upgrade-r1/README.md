# dotnet-10-upgrade-r1

> **Created**: 2026-08-10 · **Status**: ✅ **COMPLETE 2026-08-14** — master + dev on .NET 10 (`DOTNETCORE-10.0.9`), suite green, zero-CVE, publish 44.96 MB. Prod/demo cutover deferred (#773). Handoff to r3: [`notes/r3-handoff.md`](notes/r3-handoff.md). · **Driver**: .NET 8 EOL **2026-11-10**

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
- [`spec.md`](spec.md) — AI implementation spec (17 FRs, 8 NFRs) — Ready for Implementation
- [`plan.md`](plan.md) — implementation plan (P0–P7 WBS + discovered resources)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — 22 tasks; serial critical path; deploy tasks operator-driven
- [`current-task.md`](current-task.md) — active task state (context recovery)

## Execution status

Pipeline was run **INITIALIZE-ONLY** (operator decision 2026-08-11 — plan artifacts only, then stop). Execution has **not** started. Begin with `task-execute` on task 001 (`tasks/001-bump-globaljson-sdk.poml`). Deploy phases (P5/P6) are **operator-driven** (Azure credentials + recorded go/no-go).

**§13-A resolved** (owner 2026-08-10): separate/sequential — .NET 10 ships + merges FIRST, then `code-quality-and-assurance-r3` re-plans on the net10 baseline. See [`design.md` §11](design.md).

## Hot-path

BFF **Y** · SpaarkeAi **N** · CI Workflows **Y** · Skill Directives **Y** · Root CLAUDE.md **Y**
