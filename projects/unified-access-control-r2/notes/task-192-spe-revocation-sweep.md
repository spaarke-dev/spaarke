# Task 192: SPE JIT revocation sweep, "Invalid hostname for this tenancy" (#1830, R3 ISS-006)

Branch `fix/uac-1830-spe-sweep-unresolvable-container`, from origin/master `3891cc098f`. This PR is **Part of #1830**: the owner's config and data items (§6) remain open. It also fixes item 1 of #1352 (the job reported Failed on every run); item 2 of #1352 (short job history) is untouched.

## 1. Root cause

The sweep (`SpeContainerMembershipSync.RemoveRevokedJitGrantsAsync`, run every 5 min by `SpeContainerMembershipSyncJob`) reads every secure record's container. One of them, secure project **65a3fab2**'s container `b!MVasATu_GE6Lqs6JOGaeghG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`, no longer resolves in the tenant. Its SharePoint site is gone.

**The decoded id.** A `b!` id is base64url of three little-endian GUIDs: site, web and list.

| Container | Site | Web | List |
|---|---|---|---|
| `b!MVasATu…` (65a3fab2, dead) | `01ac5631-bf3b-4e18-8baa-ce8938669e82` | `5811bf11-1ce7-4500-a712-e6d45620d808` | `5c80807b-7983-494d-b887-5f976ab7f78d` |
| `b!Q93-JrZ7…` (a healthy secure container, batch-4 P1) | `26fedd43-7bb6-497d-af88-739b2431e6ca` | same | same |

Only the site part differs, and that site no longer exists.

**Graph answers** (read-only diagnosis, 2026-10-22). Every token gives the same answers, so the BFF's client and tenant are fine:

| Call | Dead id `b!MVasATu…` | Sibling `b!OKn3KsSg…` |
|---|---|---|
| `GET /storage/fileStorage/containers/{id}` | **400** `invalidRequest` "Invalid hostname for this tenancy" | 403 |
| `GET /drives/{id}` | 404 | 403 |
| `GET /sites/{site}` | 404 | 403 |

The same 400 for this id was first recorded on 2026-10-06 (`notes/batch4-live-gates-2026-10-06.md`, G4/G6, F-G6-1). On 2026-10-07 #1352 recorded that the sync job reported Failed on every run because of it.

**Why it surfaced as an Error on every run instead of a skip:**

1. The id is in the dev app setting `SharePointEmbedded__OwnedContainerIds`, so `SpeContainerOwnershipGuard.IsOwnedCoreAsync` trusts it without a Graph read (`SpeContainerOwnershipGuard.cs:178-181`). Without that entry, the guard's own read (`:335-344`) would map the 400 to "not owned", and the sweep's NotOwned branch would log a Warning and skip it, uncounted.
2. `SpeContainerMembershipService.ReadMarkersCoreAsync` maps only a 404 to null (the container does not exist). The 400 propagated to the sweep's generic catch, which logged an **Error** and counted `failed++`.
3. `SpeContainerMembershipSyncJob` reports `Success=false` whenever `failed > 0`, so every run read Failed, and a genuine new fault looked the same as this standing one.

Per-record isolation was already in place: the other 3 secure containers were processed on every run.

**Minor, found in the same read:** in `RemoveMarkedGrantAsync`, the `ForOwnedContainerAsync` call sat outside the try. A guard fault there (a throttled ownership read's `SpaarkeStorageException`, or an unresolved customer identity) escaped to the sweep's per-record catch and skipped that container's remaining grants until the next run.

## 2. Blast radius

**No revoked grant kept its membership.** JIT markers (`SprkJit…`, task 171) shipped on 2026-10-07. The container was already unresolvable on 2026-10-06, so no Office edit-open could have granted on it, and no JIT grant or marker can exist there. Every container that can hold a JIT grant was swept on every run.

## 3. The fix

| Change | File |
|---|---|
| `SpeContainerMembershipService.IsUnresolvableContainer(Exception)` reuses the shared rule `SpeAdminGraphService.IsUnresolvableContainerId`: Graph `ODataError`, 400, code `invalidRequest` (measured on dev 2026-10-06). `SpeAdminGraphService.cs` is not modified. | `Infrastructure/ExternalAccess/SpeContainerMembershipService.cs` |
| The JIT sweep gets a new catch for that classification, applied **only to a fault from its first read of a container** (`ReadMarkersAsync`; fix round 1, F2-1). It logs a Warning, adds the problem `"{entity} {id}: container {c} does not resolve in this tenant; skipped"`, counts it in a new `Unresolvable` result field, and does not count it in `failed`. Once that first read answers, the id is proven to resolve, so the same 400 from `ReadAccessAsync` (its container read, the permission collection or a `@odata.nextLink` page) stays an Error and `failed++`. Nothing is read, removed or cleared on that container, and `RemoveMarkedGrantAsync` is never called for it. Genuine faults keep the Error log and `failed++`: 5xx, 429, a 400 with another code, `SpaarkeStorageException`, and anything that is not an `ODataError`. | `Services/Access/SpeContainerMembershipSync.cs` |
| `RemoveMarkedGrantAsync` moves the ownership check inside the try, so a guard fault fails that one grant closed (`Failed`, marker kept). The task-227d refusal (`spe_container_not_owned`) is excluded by the catch filter and still propagates. | `Infrastructure/ExternalAccess/SpeContainerMembershipService.cs` |

**Not changed, on purpose:**

- **`ReadMarkersCoreAsync` / `ReadAccessAsync` still answer null for 404 only.** `DemoExpirationJob.RevokeSpeContainerAccessAsync` reads null as "the container does not exist; nothing to revoke", which would fail open on an id Graph merely cannot resolve. `OfficeEditAccessService` uses `ReadAccessAsync` too. The classification lives only in the sweep's catch.
- **The job's success rule (`SpeContainerMembershipSyncJob.cs`).** `failed == 0` is already right once an unresolvable container is not counted as failed. So the skip stays visible on a green run, the heartbeat line and `ResultJson` carry `unresolvable={n}` (fix round 1, K1), and the problem line is in `ResultJson`.
- **The standing-writer pass.** A business unit stamped with a dead container is still a failure there. It is a config fault on a container that should serve users, the same as its NotOwned branch.

**Why code only, without matching the message text.** The shared rule was measured on dev: both "Invalid hostname for this tenancy" (site part unresolvable) and "The drive ID is incorrectly formatted or was not recognized" (truncated id) are 400 `invalidRequest`, and both mean "this id names no container here". A bug in our own request (a malformed `$select`) answers 400 `BadRequest` and stays a fault. The rule was measured for the single-container GET only, which is why the sweep applies it only to its first read. Reusing the one rule keeps the JIT sweep, the SPE-admin binding read and the guard consistent: for an id that does not resolve, the JIT sweep reports it and does not count a failure, whether it reaches the sweep as the guard's NotOwned refusal or as Graph's 400. The standing-writer pass is different: it counts NotOwned as `failed++` (see above).

**Fail closed.** An unresolvable item is never treated as revoked-and-done. Its markers (if any could exist) are left alone, it is reported again on every run, and only the operator fix in §6 clears it.

## 4. Tests

The unit tests are in `tests/unit/Sprk.Bff.Api.Tests/`:

- **`Services/Access/SpeContainerMembershipSyncTests.cs`**:
  - Must fire: A throws 400 `invalidRequest` "Invalid hostname for this tenancy"; B's JIT holder has lost Write. With A first and with A last, B is removed (`Removed == 1`), `Failed == 0`, A is the single problem, `RemoveMarkedGrantAsync` and `ReadAccessAsync` are never called for A, and A is logged as a Warning with no Error.
  - Must not fire, as a theory: a 503, a 429, a `SpaarkeStorageException`, and a 400 with code `BadRequest` on A each still give `Failed == 1` and an Error, and B is still removed.
  - Must not fire: an all-healthy run has no problems and no failures.
  - Must fire: a guard fault inside `RemoveMarkedGrantAsync` for one of two grants on a container leaves `Removed == 1` and `Failed == 1`, with one DELETE (real service, CallBase mock).
  - Must fire (round 1, F2-1), a theory over the real `ReadAccessAsync` through the Graph stub: the marker read answers, then the 400 `invalidRequest` comes from `ReadAccessAsync`'s container read, from the permission collection, or from its `@odata.nextLink` page. Each gives `Failed == 1`, `Unresolvable == 0` and an Error, and the other container's revoked grant is still removed.
  - Job verdict: an unresolvable-only run gives `Success == true`, names the container in `ResultJson`, and reports `unresolvable=1` in the heartbeat and `ResultJson`; a genuine 503 gives `Success == false` with `failed=1, unresolvable=0`.
- **`Infrastructure/ExternalAccess/SpeContainerMembershipServiceUnresolvableContainerTests.cs`**, over the real Graph SDK through a recording `IRequestAdapter`:
  - The classifier theory: 400 `invalidRequest` (either message, any case) is true; 400 `BadRequest`, a 400 with no body, 403, 404, 429, 503, `SpaarkeStorageException` and `HttpRequestException` are false.
  - `ReadMarkersAsync`: a 404 still gives null; the 400 throws the classified fault from both reads; a 5xx propagates unclassified.
  - `RemoveMarkedGrantAsync`: a guard fault gives `Failed` with nothing sent, and the next removal proceeds; a NotOwned refusal still throws.
- **`Services/Registration/DemoSpeGrantLifecycleTests.cs`** (must not fire): expiry over a real membership service whose container read answers the 400 still **throws**, never "nothing to revoke".
- **`TestInfrastructure/SpeContainerGraphStub.cs`** holds the shared stub and the scripted guard.

**Perturbation (7 mutants, all killed; files restored and verified):**

| Mutant | Tests that failed |
|---|---|
| M1: the sweep's classification catch removed (the old behaviour) | 3: the unresolvable theory ×2 and the job-success test |
| M2: the ownership check back outside the try (the old behaviour) | 2: the service guard-fault test and the sync guard-fault test |
| M3: the 400 mapped to null in `ReadMarkersCoreAsync` | 2: the reads-throw test and the DemoExpiration must-not-fire test |
| M4: the classifier widened to any 400 | 4: the BadRequest and no-body classifier rows, and the genuine-fault BadRequest theory ×2 |
| M5: the refusal swallowed (catch filter without the NotOwned exclusion) | 1: the 227d refusal test |
| M6 (round 1): the classification catch without `!resolved` (any read's 400 classified) | 3: the F2-1 theory (`ReadAccessAsync`'s container read, the permission collection, a nextLink page) |
| M7 (round 1): `unresolvable++` removed | 3: the unresolvable theory ×2 and the job test (heartbeat and `ResultJson` count) |

**Runs** (round 1, on the branch merged with origin/master `ab7cf4fc94`):

| Suite | Result |
|---|---|
| Filtered unit tests (`SpeContainerMembership*`, `DemoExpiration*`, `OfficeEditAccess*`, `SpeContainerOwnershipGuard*`, `DemoSpeGrantLifecycle*`) | 111 / 111 passed (round 1; 108 in round 0) |
| ArchTests | 1361 / 1361 passed |
| `Sprk.Bff.Api.IntegrationTests` | 172 passed, 8 skipped, 0 failed |
| `Spe.Integration.Tests` | 353 passed, 23 skipped, 0 failed |
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |

## 5. CLAUDE.md §10 / §11

**BFF hygiene (§10).** Publish size is 37.75 MB on both sides: 39,582,230 bytes for master `3891cc098f` and 39,582,235 bytes for this branch, a **+5 bytes** delta. Both were measured from fresh worktrees with `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` (framework-dependent linux-x64, Release), then `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, with PDBs included. There are no package changes, so no new CVE surface. Placement: no new service, endpoint, DI registration or background work.

**New surface (§11): `SpeContainerMembershipService.IsUnresolvableContainer` (internal static).**

1. **Existing.** It overlaps `SpeAdminGraphService.IsUnresolvableContainerId`, which it calls rather than duplicates. The guard's 400-to-not-owned is a different (trust) decision and is left alone.
2. **Extension.** It is a two-line wrapper, needed so the sweep (`Services/Access`) never handles a Graph `ODataError` type itself (ADR-007: Graph types stay behind the facade). The wrapper sits beside the reads whose faults it classifies. `SpeAdminGraphService.cs` is out of bounds for this task.
3. **Cost of doing nothing.** The sweep logs an Error and `failed++` for a container that will never resolve, every 5 minutes, and the job reports Failed on every run, so a genuine revocation failure is indistinguishable from the standing one (#1830, #1352).

The new sweep catch branch is a new outcome in an existing method. Answers: existing, the NotOwned branch beside it (same report-and-skip shape); extension, it is that shape extended to Graph's own "does not resolve" answer; cost of doing nothing, as in item 3.

The test-only `SpeContainerGraphStub` and `ScriptedOwnershipGuard` follow the `RecordingAdapter` and `TestSpeOwnership` conventions. They are shared because two test files need the same Graph seam.

## 6. Owner items (not part of this PR)

1. **Remove `b!MVasATu…` from the dev app setting `SharePointEmbedded__OwnedContainerIds`.** This is an app-setting write and needs the owner's go. Once it is removed, the guard reads Graph for the id and refuses it as not owned (Warning), and the Error class goes to 0 at its source.
2. **Re-provision or clear secure project 65a3fab2's container** (`sprk_containerid`). The site behind it is gone, 0 documents point to it (batch-4 notes), and the project cannot hold files until it has a live container. Per the project rules, no agent touches 65a3fab2.

After both items and the deploy, the server gate checks two things: App Insights shows 0 "Invalid hostname for this tenancy" from the sweep over 30 minutes, and the job's run reports show `Success=true`, with revocations processed.

## 7. Found, not fixed

- **#2267 (filed):** stamp-configured container ids (`OwnedContainerIds`, `DefaultContainerId`, `ArchiveContainerId`) are never checked for existence. A dead id is trusted silently and surfaces as a fault in whichever feature uses it first. Proposed: a startup Warning per configured id that does not resolve.
- #1352 item 2 (short job history) is unchanged.
