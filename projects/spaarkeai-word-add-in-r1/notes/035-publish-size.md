# Task 035 — BFF publish-size measurement

> Per root CLAUDE.md §10 bullet 4: measured against a FRESH BUILD of `origin/master`, not the recorded
> baseline number. Both sides zipped with the SAME tool, PowerShell `Compress-Archive -CompressionLevel
> Optimal`, matching `scripts/Deploy-BffApi.ps1`.

## Procedure

```
git worktree add C:\t\wt-master-035 origin/master --detach    # resolved e0a6f87c4
cd C:\t\wt-master-035\src\server\api\Sprk.Bff.Api
dotnet publish -c Release -o C:\t\pubmaster

cd src/server/api/Sprk.Bff.Api                                # this worktree (branch)
dotnet publish -c Release -o C:\t\pub035

# PowerShell, both sides, same tool:
Compress-Archive -Path 'C:\t\pubmaster\*' -DestinationPath 'C:\t\pubmaster.zip' -CompressionLevel Optimal
Compress-Archive -Path 'C:\t\pub035\*' -DestinationPath 'C:\t\pub035.zip' -CompressionLevel Optimal
```

Worktree and publish artifacts were removed after measurement (`git worktree remove C:\t\wt-master-035
--force`, `rm -rf C:\t\pub035 C:\t\pub035.zip C:\t\pubmaster C:\t\pubmaster.zip`) — kept out of git per
the constraint. Both publishes used a short path (`C:\t\...`) to avoid the MSB3030 deep-path failure.

## Result

| Build | Size (bytes) | Size (MB) |
|---|---|---|
| `origin/master` @ `e0a6f87c4` (fresh build, this session) | 47,555,979 | 45.35 |
| This task's branch (task 035 changes on top) | 47,609,648 | 45.40 |
| **Delta** | **+53,669** | **+0.05** |

**Sanity check against the task's given baseline number** (47,556,260 bytes): the fresh master build
measured 47,555,979 bytes — a 281-byte difference, well within normal Release-build non-determinism
(timestamps, PDB checksums). The given baseline was accurate and current; this measurement is the fresh
build the rule requires, not a re-quote of the recorded number.

## Assessment

+0.05 MB is far under the +5 MB single-task escalation threshold and the 60 MB hard ceiling (branch total
45.40 MB). No architecture review triggered. The delta is exactly what this task's changes should cost:
two new dictionary entries on an existing map, two new nullable `Guid` request fields, ~20 lines added to
one existing service method — no new package, no new endpoint, no new DI registration, no new background
job.

## Placement Justification (CLAUDE.md §10)

All production changes extend existing BFF surface already inside `Sprk.Bff.Api`:

- `Models/Office/CreateTodoRequest.cs` — the existing `CreateTodoRequest` record extended with two
  optional fields (`DocumentId`, `CommunicationId`), additive only. Same file already owns the To Do
  request contract (`RegardingEntityType`/`RegardingRecordId`/`RegardingRecordName`); the natural place
  for the second, independent regarding slot FR-14 adds.
- `Services/Office/OfficeService.cs` — `CreateTodoAsync`'s existing "Regarding (the filed record)" block
  is unchanged; a new, independent block writes the carrying document/communication lookup using two new
  entries on the already-existing `_todoRegardingMap`. No new service, no new interface, no new DI
  registration — the same `IGenericEntityService`/`CoreAncestorResolver` dependencies this method already
  held are reused as-is.
- `Api/Office/OfficeEndpoints.cs` — UNCHANGED. Minimal API model binding picks up the two new
  `CreateTodoRequest` fields automatically; no route, filter, or handler-signature change was needed
  (constraint: "Do NOT create a new endpoint").

No new package reference, no new endpoint, no new background job, no new DI-registered service or
interface. §11 (Component Justification) — three-question template:

1. **Existing** — `_todoRegardingMap` (Matter/Project/Invoice → typed lookup) and the request/response
   contract for `POST /api/office/todo` already exist and already do everything this task needs except
   carry a second, independent regarding.
2. **Extension** — yes: two dictionary entries plus two nullable request fields plus one new,
   independent write block in the same method. No new abstraction was introduced.
3. **Cost-of-doing-nothing** — without this, a pane-created To Do for a document/communication the user
   is actively working from would carry ONLY the business-record regarding, losing the "which document
   was I looking at when I made this To Do" relationship FR-14 requires — a concrete behavior gap, not a
   speculative one (spec.md FR-14 acceptance criteria name it directly).
