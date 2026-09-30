# Task 022 — BFF publish-size measurement

> Root CLAUDE.md §10 bullet 4 / ADR-029. Measured 2026-09-12 against a FRESH build of `origin/master`
> (not the recorded baseline number), per the binding rule that the baseline AGES and the zip tool
> changes the number.

| | Commit | Zip (incl. PDBs, PowerShell `Compress-Archive -CompressionLevel Optimal`) |
|---|---|---|
| `origin/master` | `e0a6f87c4` (re-verified via `git rev-parse origin/master` — unchanged from the 2026-08-13/09-11 baseline) | **45.35 MB** (47,556,506 bytes) |
| This branch (`worktree-agent-a36ae584d36b79e3e`, task 022 changes) | working tree at time of measurement | **45.40 MB** (47,601,660 bytes) |
| **Delta** | | **+0.04 MB** |

- **Method:** worktree isolation per the binding procedure — `git worktree add /tmp/wt-master-022
  e0a6f87c4 --detach`, `dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -o
  <master-publish>`, then the SAME for this branch into a separate output directory. Both zipped with
  the actual PowerShell `Compress-Archive -CompressionLevel Optimal` cmdlet (not a substitute .NET
  API) — matching `scripts/Deploy-BffApi.ps1`'s default. Cross-checked against a direct
  `System.IO.Compression.ZipFile.CreateFromDirectory(..., CompressionLevel.Optimal, ...)` call, which
  produced the same figures to two decimal places (45.35 MB / 45.40 MB), confirming no tool-choice
  artifact in this measurement.
- **`origin/master` re-verified, not assumed:** `git rev-parse origin/master` returned `e0a6f87c4` at
  measurement time — identical to the commit this branch was rebased from and to the figure recorded
  by `sdap-SPE-admin-app-r2` / task 021 / task 023. Because the commit had not moved, the fresh
  master-build number (45.35 MB) is, expectedly, identical to the recorded baseline — this is
  coincidence of timing, not a decision to reuse the recorded number without re-publishing.
- **Scope of the delta:** task 022 adds ONE new BFF route
  (`POST /api/office/documents/{documentId}/generate-profile`), one new internal collaborator class
  (`OfficeProfileDispatcher.cs`, ~190 lines), three new optional constructor parameters on
  `OfficeService` (all already-registered DI types — `IServiceScopeFactory`, `IDocumentProfileAi`,
  `IHostApplicationLifetime` — zero new DI registrations), and one new interface method
  (`IOfficeService.GenerateProfileAsync`). No new NuGet package reference. A +0.04 MB delta is
  consistent with that scope — a few hundred lines of new IL plus route/filter/XML-doc metadata, no
  new assemblies.
- **No new package references** — `dotnet list package --vulnerable --include-transitive` against
  `Sprk.Bff.Api.csproj` reports **no vulnerable packages**.
- **Thresholds:** +5 MB for a single task needs justification, 55 MB cumulative triggers an
  architecture review, 60 MB is a hard stop. +0.04 MB is far below all three.
- **Worktree cleanup:** the temporary `origin/master` worktree (`/tmp/wt-master-022`) was removed via
  `git worktree remove --force` after measurement; it never touched this branch's own worktree or any
  shared checkout.
