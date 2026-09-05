# BFF publish-size verification (root CLAUDE.md §10 / spec NFR-01)

> Measured **2026-09-05**. Both sides built FRESH and zipped with the SAME tool, in the same run.

| | Commit | Compressed |
|---|---|---|
| `origin/master` | `379c221e0` | **45.46 MB** |
| branch `work/unified-access-control-r2` | `94b1d6f54` | **45.48 MB** |
| **Delta attributable to this branch** | | **+0.02 MB** |

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
