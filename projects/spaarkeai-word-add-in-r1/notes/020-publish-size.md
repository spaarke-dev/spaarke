# Task 020 — BFF Publish-Size Verification (FR-06 filename defaults to Document Name)

> Per root CLAUDE.md §10 bullet 4: measured against a **fresh build of `origin/master`**, never the
> recorded baseline number.

## Method

```bash
git fetch origin master
git worktree add /c/t/wm020 origin/master --detach   # fresh master worktree, short path (MSB3030 guard)
cd /c/t/wm020 && dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -o /c/t/pm020

# branch: published directly from this task's own worktree (already at the branch tip + these changes)
dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -o /c/t/pb020
```

Both zipped with **PowerShell `Compress-Archive -CompressionLevel Optimal`** — the method
`scripts/Deploy-BffApi.ps1` uses (per `.claude/constraints/azure-deployment.md`, and the 2026-09-02
finding that the zip tool itself shifts the number by ~1.3 MB on byte-identical content — Compress-Archive
was used consistently for both sides here, so that hazard does not apply to this delta).

## Result

| Side | Commit | Compressed size (incl. PDBs) |
|---|---|---|
| `origin/master` (fresh) | `e0a6f87c4` | **47,555,925 B (45.35 MB)** |
| This branch (task 020) | worktree tip + task 020 changes | **47,614,844 B (45.41 MB)** |
| **Delta** | | **+58,919 B (+0.056 MB)** |

The master figure is within 80 bytes of the previously-recorded `spaarkeai-compose-r8` measurement
(47,556,005 B at the same commit `e0a6f87c4`) — consistent with the same commit, zip-metadata-level
noise only, not baseline drift (master has not moved since that measurement).

## Assessment

- **Delta is +0.056 MB — far under the +5 MB single-task escalation threshold** (root CLAUDE.md §10).
- **Cumulative (45.41 MB) is far under both the 55 MB architecture-review threshold and the 60 MB hard
  ceiling** (spec NFR-01).
- The delta is entirely explained by the new code added: a handful of lines in
  `OfficeDocumentPersistence.cs` (an `internal const int` + a bounds check) and `OfficeService.cs` (one
  new assignment in an existing switch arm). **No new NuGet packages, no new DI registrations, no new
  endpoints.**

## Cleanup

The worktree (`/c/t/wm020`) and publish outputs (`/c/t/pm020`, `/c/t/pb020`, and their `.zip` files) are
removed at the end of this task's execution (`git worktree remove` + directory deletion) — they are
scratch artifacts, not part of the repository.
