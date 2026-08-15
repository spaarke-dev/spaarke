---
description: Migrate a Spaarke worktree onto the .NET 10 baseline (SDK check → merge master → net8-clobber guard → build)
tags: [dotnet, net10, worktree, migration, build, cutover]
techStack: [dotnet, git, powershell]
appliesTo: ["migrate worktree to net10", "update worktree to .net 10", "bring this branch to net10", "merge master net10", "net10 migrate", "update to dotnet 10"]
alwaysApply: false
exemplar: scripts/Update-WorktreeToNet10.ps1
last-reviewed: 2026-08-14
---

# worktree-net10-migrate

> **Category**: Operations
> **Created**: 2026-08-14 by `dotnet-10-upgrade-r1` cutover.
> **Exemplar**: [`scripts/Update-WorktreeToNet10.ps1`](../../../scripts/Update-WorktreeToNet10.ps1) — the one-command migrator (the canonical operational pattern).

Bring any Spaarke worktree onto the **.NET 10** baseline after the 2026-08-14 cutover (master + dev are net10). It's a **merge, not a rewrite** — TFM + package alignment + a handful of Graph 6.5/Kiota 2.0 call sites, all already on master.

---

## When to use

- A worktree/branch is still on net8 and needs to build/deploy against net10 dev.
- Trigger phrases: "migrate this worktree to net10", "update to .NET 10", "bring this branch to net10", "merge master for net10".
- Reactivating a dormant branch (same procedure — expect more `.csproj` conflicts on very old branches).

**Why it matters**: dev App Service now runs `DOTNETCORE|10.0`. A net8 build deployed there fails to start (**503**). A worktree must be on net10 before its next dev deploy.

---

## The one command

From the target worktree root (close Visual Studio / Rider first — see the guard below):

```powershell
pwsh -File scripts/Update-WorktreeToNet10.ps1
```

Or migrate another worktree without cd-ing:

```powershell
pwsh -File scripts/Update-WorktreeToNet10.ps1 -WorktreePath C:\code_files\spaarke-wt-<name>
```

> **First-run bootstrap**: an old branch may not have this script yet (it was added at cutover). Run it from a worktree that HAS it (e.g. the main repo or a freshly-merged worktree) with `-WorktreePath` pointing at the target — one script migrates any worktree.

The script is **non-destructive**: it never commits, pushes, deploys, or auto-stashes. It stops for you on a dirty tree or a merge conflict.

### What it does (and reports)
1. **SDK check** — `.NET 10 SDK` installed? (else prints the `winget install Microsoft.DotNet.SDK.10` fix).
2. **Dirty-tree guard** — refuses to run with uncommitted changes (commit/stash first; your work is never touched).
3. **`git fetch` + `merge origin/master`** — or "already up to date" if behind 0.
4. **Conflicts** — lists them and STOPS (rule: take **net10** for `*.csproj`/`*.props`/`global.json`, keep your feature intent elsewhere). `-AutoResolveCsproj` auto-takes master's net10 csproj **only** when nothing else conflicts.
5. **net8-clobber guard** — verifies every `src/server/**` csproj is `<net10.0>`. Catches the IDE clobber (see below).
6. **Build** — BFF Release build to confirm net10; interprets `NETSDK1045` (SDK) and Graph/Kiota errors.

---

## ⚠️ Close the IDE before merging (net8-clobber)

If **Visual Studio / Rider** has the solution open during `git merge origin/master`, it can autosave its stale **net8** `.csproj` buffer back over the merged **net10** files — a later deploy then ships net8 → **503**. **Close the IDE (or unload the solution) before merging.** The script's step-5 guard fails loudly if a server csproj is still net8; recover with `git checkout -- '*.csproj'`.

---

## After a green run

- Run the branch's tests for the areas it touches (or the full suite).
- The only likely code fixup is **Graph 5→6.5 / Kiota 1→2** call sites — see [`projects/dotnet-10-upgrade-r1/notes/graph6-kiota2-break-assessment.md`](../../../projects/dotnet-10-upgrade-r1/notes/graph6-kiota2-break-assessment.md).
- The branch now builds/deploys net10, compatible with dev.

---

## Related

- Full narrative + cutover sequence: [`projects/dotnet-10-upgrade-r1/notes/cutover-and-worktree-migration.md`](../../../projects/dotnet-10-upgrade-r1/notes/cutover-and-worktree-migration.md)
- Deferred package majors (r3 backlog): [`projects/dotnet-10-upgrade-r1/notes/deferred-package-upgrades.md`](../../../projects/dotnet-10-upgrade-r1/notes/deferred-package-upgrades.md) · GitHub issue #772
- `worktree-sync` (general bidirectional sync) · `bff-deploy` (deploy to dev on net10)
