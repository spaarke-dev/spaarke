# Task 031 — BFF publish size (ADR-029 / root CLAUDE.md §10 NFR-01)

> **Task**: `tasks/031-creation-service-project.poml` · **Date**: 2026-09-17

## Result

| | Compressed size |
|---|---|
| `origin/master` @ `e0a6f87c4` (freshly built + published) | **45.35 MB** |
| Branch `work/spaarkeai-word-add-in-r1` with task 031 | **45.41 MB** |
| **Delta attributable to this task** | **+0.06 MB** |

- **Zip tool**: PowerShell `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*` — the method
  `scripts/Deploy-BffApi.ps1` uses, per `.claude/constraints/azure-deployment.md`. Both sides zipped with the SAME
  tool; the two numbers are therefore comparable.
- **Includes PDBs** (framework-dependent linux-x64 Release publish), same convention as the recorded baselines.
- **Ceiling 60 MB**: not approached. **Escalation threshold +5 MB**: not approached. **Cumulative-review 55 MB**: not reached.

## Method — measured against a FRESH master build, not the recorded baseline

Root CLAUDE.md §10 bullet 4 is explicit that re-measuring master **is** the measurement and the recorded number is
only a sanity check, because the baseline ages as other projects merge. So:

```
git worktree add C:\wt-master-031 origin/master --detach     # SHORT path — avoids MSB3030 long-path failures
dotnet publish C:\wt-master-031\src\server\api\Sprk.Bff.Api\Sprk.Bff.Api.csproj -c Release -o C:\wt-master-031\deploy\api-publish
Compress-Archive -Path C:\wt-master-031\deploy\api-publish\* -DestinationPath ...\master.zip -CompressionLevel Optimal
# then the identical publish + Compress-Archive on the branch worktree
```

The temporary master worktree was removed afterwards (`git worktree remove C:\wt-master-031 --force`).

**Sanity check against the recorded figure**: the 2026-09-02 baseline in root CLAUDE.md §10 is 45.42 MB at
`a826cf347`. This measurement puts master at 45.35 MB at `e0a6f87c4` — 0.07 MB *below* it, which is ordinary
drift in the expected direction of magnitude and corroborates that the tool and convention match. Had I compared the
branch against the recorded 45.42 instead of a fresh build, I would have reported a **−0.01 MB** delta, i.e. the
measurement noise would have swamped and inverted the real +0.06 MB signal — the exact failure the §10 rule exists
to prevent.

## Why the delta is small

Task 031 adds no NuGet package, no project reference and no csproj property — `git diff a481bcef8 --stat` over
`*.csproj` / `*.props` is empty. The change is ~300 lines of C# compiled into an existing assembly, so the delta is
essentially the IL and metadata for one new private method, one static set and one new test-only type (tests are not
published). +0.06 MB is consistent with that.
