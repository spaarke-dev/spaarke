# Task 012 — BFF publish-size measurement

> Root CLAUDE.md §10 bullet 4 / NFR-01. Measured 2026-09-10 against a FRESH build of master, not the recorded baseline.

| | Commit | Zip (incl. PDBs) |
|---|---|---|
| `origin/master` | `e0a6f87c4` (fresh detached worktree) | **45.35 MB** |
| This branch | `d594ad5d4` + uncommitted task-012 changes | **45.37 MB** |
| **Delta** | | **+0.016 MB** |

- **Method:** `dotnet publish -c Release -o <root>/deploy/api-publish` for each side, then PowerShell
  `Compress-Archive -Path "<publish>\*"` with the default Optimal level. That is the same zip method as
  `scripts/Deploy-BffApi.ps1:259`. Both sides used the same tool.
- **Scope of the delta:** it covers everything this branch adds to the BFF, not only task 012. That includes
  tasks 028 and 032, and earlier measurements on this branch read 45.36 MB, so task 012's own contribution is
  about +0.01 MB.
- **Thresholds:** +5 MB for a single task needs justification, 55 MB means an architecture review, and 60 MB is a
  hard stop. The delta is far below all of them.
- **No new package references.** Task 012 adds only source files.
- **Timing:** the measurement was taken before the Step 9.5 review revision (notes/012 §9). That revision changed
  source only, in the same files: it removed the self-heal, reworked exception classification, and made units
  public. It added no package or asset, so its effect on a 45 MB zip is a few kilobytes of IL at most. The
  measurement stands, and was not re-run.
