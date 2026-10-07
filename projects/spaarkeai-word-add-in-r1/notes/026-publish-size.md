# Task 026 — BFF publish-size measurement

> Per root CLAUDE.md §10 bullet 4: measured against a FRESH BUILD of `origin/master`, not the recorded
> baseline number. Both sides zipped with the SAME tool, PowerShell `Compress-Archive -CompressionLevel
> Optimal`, matching `scripts/Deploy-BffApi.ps1`.

## Procedure

```
git worktree add /c/wtm026 e0a6f87c4 --detach     # e0a6f87c4 = origin/master tip, confirmed via
                                                    # `git rev-parse origin/master`
cd /c/wtm026/src/server/api/Sprk.Bff.Api
dotnet publish -c Release -o /c/pub026/master

cd src/server/api/Sprk.Bff.Api                     # this worktree (branch)
dotnet publish -c Release -o /c/pub026/branch

# PowerShell, both sides, same tool:
Compress-Archive -Path 'C:\pub026\master\*' -DestinationPath 'C:\pub026\master.zip' -CompressionLevel Optimal
Compress-Archive -Path 'C:\pub026\branch\*' -DestinationPath 'C:\pub026\branch.zip' -CompressionLevel Optimal
```

Worktree and publish artifacts were removed after measurement (`git worktree remove /c/wtm026 --force`,
`rm -rf /c/pub026`) — kept out of git per the constraint.

## Result

| Build | Size (bytes) | Size (MB) |
|---|---|---|
| `origin/master` @ `e0a6f87c4` (fresh build, this session) | 47,555,901 | 45.35 |
| This task's branch (task 026 changes on top) | 47,608,293 | 45.40 |
| **Delta** | **+52,392** | **+0.05** |

**Sanity check against the task's given baseline number** (47,556,260 bytes): the fresh master build
measured 47,555,901 bytes — a 359-byte difference, well within normal Release-build non-determinism
(timestamps, PDB checksums). The given baseline was accurate and current; this measurement is the fresh
build the rule requires, not a re-quote of the recorded number.

## Assessment

+0.05 MB is far under the +5 MB single-task escalation threshold and the 60 MB hard ceiling (branch total
45.40 MB). No architecture review triggered. The delta is exactly what this task's changes should cost:
one new `IReadOnlyDictionary` field map, one new async method, one extended DTO record, and ~20 lines
added to one existing endpoint handler — no new package, no new endpoint, no new DI registration.

## Placement Justification (CLAUDE.md §10)

All production changes extend existing BFF surface already inside `Sprk.Bff.Api` /
`src/server/shared/Spaarke.Dataverse` boundaries:

- `DocumentUrlIdentityResolution.cs` (`Services/Documents/`) — extended with a field-mapping table and one
  new static method. Same file that already owns the direct-slot association model (task 012); the
  natural place for "which column is the display name vs the number, per entity type" to live, since that
  is a fact about the SAME association model this file already encodes.
- `FileOperationModels.cs` — `RelatedRecordIdentity` extended with two new optional fields, additive only.
- `FileAccessEndpoints.cs` — the existing `resolve-identity` handler extended to call the new method when
  a related record is present; no new route, no new filter, no new DI registration.

No new package reference, no new endpoint, no new background job, no new DI-registered service or
interface. §11 (Component Justification) is satisfied by the task POML's own `<justification>` element,
reused verbatim in `notes/026-slot-scope-decision.md` §5.
