# BFF publish-size verification (root CLAUDE.md §10 / spec NFR-01)

> Measured **2026-09-05**. Both sides built FRESH and zipped with the SAME tool, in the same run.

| | Commit | Compressed |
|---|---|---|
| `origin/master` | `379c221e0` | **45.46 MB** |
| branch, before the parallel wave | `94b1d6f54` | **45.48 MB** |
| **branch, after 039 + the vocabulary hoist** | **`6db37ef95`** | **45.48 MB** |
| **Delta attributable to this branch** | | **+0.02 MB** |

Measured **twice** — once before the 039 / vocabulary-hoist wave and once after. Master re-measured
fresh both times (45.46 both times) and the branch delta did not move.

### 🔴 A subagent reported 50.42 MB for the same branch. It was wrong.

The task-039 agent reported its branch publish as **50.42 MB** — ~5 MB above the isolated measurement
above, taken at essentially the same commit. It could not run the §10 procedure at all (agents in this
project are forbidden from running git, and the procedure *requires* `git worktree add origin/master`),
so it measured its own working tree by some other means and reported an absolute number with no
comparison. That number was **kept out of every commit message and note** and is recorded here only as
the correction.

**The lesson is the §10 rule restated**: a publish-size figure is meaningless without (a) a fresh
same-run master build to diff against, (b) the zip tool named, and (c) a clean tree. Miss any one and
you get a confident number that is wrong by more than the entire escalation threshold — which is how a
+0.02 MB change could have been reported as a ≥+5 MB regression requiring justification.

- **Zip tool**: PowerShell `Compress-Archive -CompressionLevel Optimal` (the method `scripts/Deploy-BffApi.ps1` uses). Naming the tool is required — the same publish folder measures ~1.3 MB smaller under Python `shutil.make_archive`.
- **PDBs**: included (default publish output).
- **Ceiling**: 60 MB hard (NFR-01). Escalation thresholds: ≥+5 MB single-task delta, ≥55 MB cumulative. **Neither approached.**
- **No dependency delta**: `git diff origin/master...HEAD` shows zero changes to any `.csproj`, `Directory.*.props`, or lockfile. The +0.02 MB is IL from the association-map corrections.

## Method notes (two traps hit while doing this)

**1. Measure master FRESH; never diff against the recorded baseline.** Master ages underneath you — every other project's merges land in your publish. The recorded 2026-09-02 figure was 45.42 MB; master is now 45.46 MB, so a project comparing against the record would have claimed +0.06 MB, three times its actual contribution. Re-measuring master IS the measurement; the recorded number is only a sanity check.

**2. 🔴 Build the worktrees under a SHORT path — `C:\tmp`, not the session scratchpad.** The first attempt failed with:

```
error MSB3030: Could not copy the file
".../Services/Ai/Chat/Playbooks/summarize-document-for-workspace.playbook.json"
because it was not found.
```

The file is present and committed. The scratchpad path is ~110 characters before the repo tree even begins, so that asset's full path exceeds Windows `MAX_PATH`. **MSB3030 reports a length problem as a missing file**, which sends you hunting for a deleted asset that was never deleted.

**3. A native exe's failure does not trip `$ErrorActionPreference = 'Stop'`.** The first script sailed past the failed `dotnet publish` and only died at `Compress-Archive`. Had a stale output folder existed, it would have zipped it and printed a confident, wrong number. The script now checks `$LASTEXITCODE` after each publish and aborts with *"no number is trustworthy"*. Same class as the `| tail` exit-code masking earlier in this project: **a pipeline's success is not the command's success.**

Script: `scratchpad/measure-publish.ps1` (session-local; the durable procedure is the three notes above).
