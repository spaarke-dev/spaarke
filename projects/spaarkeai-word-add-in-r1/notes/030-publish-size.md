# Task 030: BFF publish size (root CLAUDE.md §10, ADR-029, NFR-01)

> **Measured**: 2026-09-11, by the task 030 sub-agent.
> **Method**: fresh builds of both sides, zipped with the **same** tool. I did not compare against a recorded baseline number.

## Procedure

1. `git worktree add <sibling>/m030 origin/master --detach` at `origin/master` = **`e0a6f87c4`**.
   - The first attempt, under the session scratchpad, failed with `MSB3030`: the file paths exceeded Windows MAX_PATH.
   - I re-created the worktree at the short sibling path `C:\code_files\spaarke\.claude\worktrees\m030`.
2. `dotnet publish -c Release -o <worktree>/deploy/api-publish` for **both** trees. The branch tree is task 030's working tree on top of `62a59a029`, the project head.
3. I zipped each `deploy/api-publish\*` with PowerShell `Compress-Archive -Path "$PublishPath\*" -DestinationPath $ZipPath -Force`. That is the exact invocation at `scripts/Deploy-BffApi.ps1:259`. It sets no explicit level, so the default, **Optimal**, applies.
4. `git worktree remove` the master worktree.

## Results (sizes include PDBs)

| | files | uncompressed | PDB (uncompressed) | **zip, incl. PDBs** |
|---|---|---|---|---|
| `origin/master` @ `e0a6f87c4` | 214 | 138.75 MB | 2.35 MB | **45.35 MB** (47,556,260 bytes) |
| branch (task 030 working tree) | 214 | 138.85 MB | 2.36 MB | **45.39 MB** (47,592,532 bytes) |
| **Delta** | 0 | +0.10 MB | +0.01 MB | **+0.03 MB** (+36,272 bytes) |

`Sprk.Bff.Api.dll`: 13,049,344 bytes on master and 13,139,968 on the branch, a difference of **+90,624 bytes** uncompressed.

**Re-measured after the Step 9.5 review fixes** (the `CreateTimeFieldMapping` extraction plus the filter/service changes). The branch was re-published and re-zipped with the same invocation and compared with the same master zip:

| | files | uncompressed | **zip, incl. PDBs** |
|---|---|---|---|
| `origin/master` @ `e0a6f87c4` | 214 | 138.75 MB | **45.35 MB** (47,556,260 bytes) |
| branch, final (as committed) | 214 | 138.86 MB | **45.39 MB** (47,593,126 bytes) |
| **Delta** | 0 | +0.11 MB | **+0.04 MB** (+36,866 bytes) |

`Sprk.Bff.Api.dll` in the final build is 13,140,992 bytes (**+91,648** over master). **These final numbers are the ones to cite.**

- **Zip tool**: PowerShell `Compress-Archive` (default Optimal), matching `Deploy-BffApi.ps1`.
- **PDB convention**: sizes include PDBs.
- **Threshold check**: the +0.03 MB delta is well under the +5 MB single-task escalation line. The 45.39 MB absolute size is under the 55 MB review line and the 60 MB hard ceiling.
- **No new NuGet package** was added. `dotnet list package --vulnerable --include-transitive` reports: *"The given project `Sprk.Bff.Api` has no vulnerable packages given the current sources."*

## What the delta contains

The comparison is **branch against master**, as the rule prescribes. The delta therefore contains everything on the project branch that master lacks: task 030's source, plus this project's earlier BFF work (for example task 012's document-identity resolver), minus any master commits the branch hasn't picked up. No new package or asset was added, so task 030's own share is a subset of the +90,624-byte DLL growth.

The total is well under the threshold, so I did not isolate 030 alone further. Doing so would take a third publish of `62a59a029` without the working-tree changes.
