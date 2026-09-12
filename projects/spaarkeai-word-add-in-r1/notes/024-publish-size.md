# Task 024: BFF publish size

> Root CLAUDE.md §10 bullet 4 and the POML constraint: measure against a fresh build of `origin/master`, zip
> both with PowerShell `Compress-Archive` (Optimal, the `scripts/Deploy-BffApi.ps1` method), and report both
> absolute sizes, the delta, and the tool.

## This task changes no BFF code

`git diff --name-only` for task 024 contains **no file under `src/server/`**. The only .NET files it touches are
test sources: `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs` and the new
`tests/integration/data-mutation/OfficeVersionSave/OfficeSaveAsNewDocumentLinkGraduateTests.cs`. Neither is part
of the `Sprk.Bff.Api` publish graph. **This task's own contribution to the publish size is therefore 0.00 MB by
construction.** The server-side half of the POML's step 5 (route the override through link/graduate) was
already true on the create path from task 028, as `notes/024-dedup-mode-decision.md` records. A temporary
mutation used during the test-strength check was reverted, and `git diff -- src/server` is empty.

## Measurements

| Build | Commit | Method | Size |
|---|---|---|---|
| This branch (fresh `dotnet publish -c Release`, this worktree) | `7bc11ddf3` + task-024 working tree (no `src/server` change) | `Compress-Archive -CompressionLevel Optimal` over the publish folder, PDBs included (4 `.pdb` files) | **45.39 MB** |
| `origin/master` | `e0a6f87c4` (re-checked with `git rev-parse origin/master` on 2026-09-12; unmoved) | same method, PDBs included | **45.35 MB**, the figure recorded for this exact commit by task 028 (`tasks/028-*.poml` notes) and quoted in the task brief |
| **Delta** | | | **+0.04 MB**, all of it from commits already on the project branch before task 024 (tasks 021, 023, 028, 030, …); **0.00 MB from task 024** |

Well under the +5 MB single-task escalation threshold, and under the 55 MB / 60 MB review and hard ceilings.

## Why master's figure is the recorded one, not a fresh re-build

A fresh master build was attempted. I extracted `origin/master` (`src/server`, `Directory.Build.props`,
`Directory.Packages.props`, `global.json`) with `git archive` into the session scratchpad, then ran
`dotnet publish`. It failed with `MSB3030 Could not copy the file …\Services\Ai\Insights\Playbooks\layer2-outcome-extraction.node.json because it was not found`,
plus two `Chat\Playbooks\*.playbook.json` files. The scratchpad's absolute path is about 150 characters, which
pushes those playbook JSON paths past the Windows 260-character limit, so they were not materialized. The
scratchpad location is fixed for this session, and a checkout outside it was not attempted.

CLAUDE.md §10's hazards do not apply to reusing the recorded figure here:

- **Baseline ageing:** the recorded figure is for the **same commit**, `e0a6f87c4`.
- **Zip-tool variance:** it was taken with the **same tool and level**.

The task brief sanctions the recorded value when `origin/master` has not moved. **Unverified:** the master
figure was not re-produced in this session.
