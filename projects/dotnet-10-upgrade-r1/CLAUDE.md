# dotnet-10-upgrade-r1 — AI Context

> **Purpose**: Context for Claude Code when working on the .NET 8 → .NET 10 backend upgrade.
> **Load this file first** for any task in this project.

---

## Status

- **Phase**: Ready for execution — `/project-pipeline` complete 2026-08-11 (INITIALIZE-ONLY: plan + tasks generated, execution NOT started).
- **Next action**: run `task-execute` on `tasks/001-bump-globaljson-sdk.poml`. Deploy tasks (050/051/060/061) are **operator-driven** (Azure + go/no-go).
- **§13-A**: RESOLVED (owner 2026-08-10) — separate/sequential, .NET 10 first, then r3 re-planned on the net10 baseline.

## What / why

- **Driver**: .NET 8 end-of-support **2026-11-10** (~3 months). .NET 9 EOL same day → target **.NET 10 (LTS)**.
- **Nature**: support-lifecycle retarget — supported runtime, **zero behavior change**, "no issues."
- **Scope**: server .NET only (BFF + 3 shared libs + tests). `net462` Dataverse plugin **excluded** (sandbox-fixed).

## Key files

- [`spec.md`](spec.md) — AI implementation spec (17 FRs, 8 NFRs) — permanent reference for acceptance
- [`plan.md`](plan.md) — implementation plan (P0–P7 WBS + discovered resources)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task registry + serial critical path + operator-gate flags
- [`current-task.md`](current-task.md) — active task state (context recovery)
- [`design.md`](design.md) — full analysis (permanent reference; §5 hit-sites, §6 packages, §7 deploy sequencing, §8 phasing, §11 r3 relationship, §13 open questions)
- [`README.md`](README.md) — overview

## Hot-Path Declaration

- **BFF**: **Y** — BFF-wide TFM + package + hit-site + publish re-baseline. `/conflict-check` before EVERY BFF PR (13+ active BFF worktrees).
- **SpaarkeAi**: **N** — server-only.
- **CI Workflows**: **Y** — `setup-dotnet` 8.x→10.x in 7 workflow files + deploy env.
- **Skill Directives**: **Y** — `azure-deployment.md` publish baseline + `/bff-deploy` runtime string → **main-session-only writes** (§3 boundary).
- **Root CLAUDE.md**: **Y** — §10 publish-size baseline number.

## Load-bearing facts (do NOT re-derive)

- **No hard package blockers.** Required moves: align `Microsoft.Extensions.*` → 10.0.x; `Dataverse.Client 1.1.32 → 1.2.26` (pre-net8 pin; drags MSAL ≥ 4.84.2); `Identity.Web → 4.14.2`. Remove superseded CVE pins (NU1510).
- **6 optional library majors are DEFERRED** (Graph v6/Kiota 2.0, Search v12, AppInsights 3.x, PowerBI v5, Azure.AI.Projects 2.x, Agents.AI GA). None blocks net10. Do NOT pull them in — it defeats the "no issues" mandate.
- **Highest-risk change**: `BackgroundService.ExecuteAsync` now runs entirely on a background thread (.NET 10) — audit ~10+ workers (H1). `TodoGenerationService` had load-bearing startup ordering.
- **`Spaarke.Scheduling` has `TreatWarningsAsErrors=true`** → NU1510/SYSLIB warnings are build errors there. Do it first.
- **Deploy**: `DOTNETCORE|10.0` (pipe) for `linuxFxVersion`; `DOTNETCORE:10.0` (colon) for `create`/`list-runtimes`. `linuxFxVersion` is slot-swapped → **zero-downtime via staging slot swap** (§7). Framework mismatch = hard startup 503. Don't hardcode `ASPNETCORE_URLS` (port 8080). setup-dotnet@v6, `10.0.x`, remove `dotnet-quality: preview`.
- **§10 publish-size**: ≤60 MB ceiling; current baseline ~49.63 MB incl PDBs — **re-baseline on net10 output**.

## Governance

- CLAUDE.md **§10 TRIGGERED** (BFF-wide): Placement Justification (net-negative — removes pins, adds no service), publish re-baseline, CVE audit, `/conflict-check`.
- §6.5 ADR conflict protocol: **none anticipated**.
- Wrap-up: `/test-diet` + doc-drift + update `projects/INDEX.md`.

## Applicable skills

`bff-deploy`, `code-review`, `adr-check`, `ci-cd`, `conflict-check`, `context-handoff`, `test-diet`, `researcher` (for re-scraping the "work-in-progress" .NET 10 breaking-changes page at spec time).

---

*Keep updated through the project lifecycle. Root `CLAUDE.md` governs.*
