# Owner decisions 2026-10-12 — BFF half (closed service request, team bell items, renewal bell items)

Date: 2026-10-12. Implemented by a wave implementer agent in an isolated worktree (`worktree-agent-a638d384aed198572`, based on R3 head `b60f11898`). Not pushed. The SPA half (hiding Delete, the bell copy) is not in this change.

| Item | Outcome | Commit |
|---|---|---|
| 1. A closed service request cannot be deleted | Done | `3c431af38` |
| 2. Bell items for staff with access through a team | **Stopped: no honest timestamp exists.** Options below. | — |
| 3. A bell item when a grant is renewed | Done, using `modifiedon` (only timestamp that exists). Known false positives below. | `fe39a9954` |

---

## Item 1 — a closed service request cannot be deleted

### What changed

- **Open vs closed.** Live describe of `sprk_servicerequest` (2026-10-12): `statecode` Active (0) / Inactive (1); `statuscode` Active (1) / Inactive (2). There is no separate "resolved" status, so closed = `statecode` 1. `docs/data-model/sprk_servicerequest.md:21` says the same.
- **`ExternalDataService.GetServiceRequestOpenStateAsync`** (new virtual read, `$select=statecode`, app-only) returns `ServiceRequestOpenState { Unknown = 0, Open, Closed }`. `Unknown` is the zero value on purpose. Anything but a numeric 0 or 1 (absent row, null, a string, 2) is `Unknown`. A failed read throws `DataverseUnavailableException`.
- **`DeleteOwnServiceRequest`** order is now: requester gate (uniform 403) → state → related-records probe → delete.
  - Closed → 409, `reasonCode = service_request_closed`, `title = "Service request is closed"`, same problem shape as `has_related_records`. The related-records probe does not run.
  - Unknown (or an unreadable state) → the existing retryable 503 `sdap.access.delete_own_unavailable`; nothing is deleted.
  - A non-requester stays the uniform 403 even for a closed request (no existence oracle).
- **SR read for the SPA.** The service-requests module's `ReadableColumns` had `statuscode` but not `statecode`. `statecode` is added (`ExternalAccessModule.cs`), so the grid / record read can return it. `statuscode` 2 means the same thing, so the SPA can hide Delete on either. The live-derivation pin in `ExternalModuleColumnAllowListTests` lists `statecode` as a deliberate non-grid column (as the document stamps are).

### SPA contract addition

| Status | Body | Meaning |
|---|---|---|
| 409 | problem, `reasonCode = service_request_closed`, `detail = "This request is closed, so it can't be deleted."` | The request is inactive. Do not offer Delete; recall is not meaningful either. |

All other statuses are as in `owner-decisions-2026-10-11-bff.md`.

### §11 — new surface

| Surface | 1. Existing | 2. Extension | 3. Cost of doing nothing |
|---|---|---|---|
| `GetServiceRequestOpenStateAsync` + `ServiceRequestOpenState` | `GetServiceRequestRequesterAsync` (same row, same read helper) | Added beside it; a separate method keeps the requester gate's read untouched | A closed request could be hard-deleted (owner decision fails) |
| `service_request_closed` reason code | `has_related_records` | One new value on the existing `reasonCode` key | The SPA could not tell "closed" from "has activity" |
| `statecode` in `ReadableColumns` | The `statuscode` column already there | One column on an existing allow-list | The SPA must infer closed from `statuscode`; owner asked for statecode |

---

## Item 2 — bell items for staff with access through a team: STOPPED

### What was investigated (live describes, 2026-10-12)

How a workforce systemuser gets access through a team: the BFF asks Dataverse itself (`ImpersonatedRootSetSource`: an impersonated id-only read per root type). Dataverse applies ownership, role depth, business unit, **team membership**, and shares natively. The BFF writes no grant row for any of it.

| Candidate | Finding |
|---|---|
| `sprk_externalrecordaccess` team grant row | **Does not exist.** The table has contact and organization lookups only (no team or systemuser lookup). `owningteam` / `ownerid` are the row's Dataverse owner, not a grantee. Team access never writes a row here. |
| `teammembership` intersect | Columns: `systemuserid`, `teamid`, `teammembershipid`, `versionnumber`. **No created-on or modified-on.** `versionnumber` is a row version, not a time. |
| Team / systemuser `createdon` | Says when the team or user was created, not when the user joined the team. |
| `principalobjectaccess` (a record shared to a team) | Has `changedon`, but **date only** (a same-day `/seen` would hide a later item), and it dates the share, not the user's joining. Only covers shares; a record **owned** by a team (the secure-record "Secure Record Owners" model, and ordinary team-owned records) has no POA row at all. |
| `sprk_accessevent` (access event log) | Documented as the history table, "AUTHORED, NOT DEPLOYED" on 2026-09-09. **No appender exists in `src/server`** (no reference to the table in any `.cs`), so it holds nothing to read. |
| `sprk_assignedaccess` (Assigned-To provenance ledger) | Has `createdon` and `sprk_subjectteam`, but the team column is written only for **inherited shares on a secure parent's children** (`AssignedAccessStore` task 158). It is not a general "user gained access via team" source. |
| Dataverse audit | No BFF code reads the `audits` table, and whether audit is enabled on `teammembership` in any environment was not verified. Not a source today. |

So there is no reliable "user U gained access through team T at time X". The "team grant created-on shown once the user is a member" approximation is **not honest**: a user who joins a team months after the share would get an item dated at the share (or none, if outside 30 days); a user who joined recently into an old share would get nothing. Nothing was implemented.

### Options for the owner

1. **Accept no team items** (status quo; records the 2026-10-10 decision). Staff learn of messages through the feed, which already works for team access, since the message source uses the accessible set.
2. **First-seen diff in Redis** (no new table, in the owner's 2026-10-08 constraint). On each feed read, compare the caller's accessible root ids with a per-user snapshot in `ITenantCache` (the same place the last-seen marker lives). Ids not in the snapshot become "you gained access" items dated at detection time. Needs: a baseline rule (the first read after a missing snapshot announces nothing, or every record is "new"), a cap (accessible sets can reach thousands; `RootIdSet.Truncated` exists), and the honest copy "newly available to you" rather than a grant time. A cache eviction re-baselines silently. This is a design task, not a derivation.
3. **Enable and read Dataverse audit** on `teammembership` (association events) and read `audits` app-only. Needs an environment change (audit settings), the app user's audit read privilege and a new read path; the audit retention then bounds it.
4. **Deploy the `sprk_accessevent` appender** (unified-access-control-r2 task 087, not built) and read Share Granted / Team events. It would record shares but still not a user joining a team.

Recommendation: option 1 until the owner wants more; if team items are required, option 2 is the only one that needs no environment change, and it should be a small scoped task of its own.

---

## Item 3 — a bell item when a grant is renewed

### How renewal works in code

`GrantExternalAccessEndpoint.CreateGrantAsync` updates the **same row in place**: it writes only `sprk_accesslevel` and/or `sprk_expiresdate` (a lapsed grant re-granted is "restored" at today + 90). `sprk_granteddate` and `createdon` are never rewritten (`BuildGrantPayload` is the only writer of the date, on create). `GrantUpsertOutcome.AccessGained` marks a restore or a raised level, which is what drives the grant email (task 036); no timestamp is stored. Live describe of the table: **no renewed-on, expiry-changed-on or reactivated-on column**; `sprk_assignedaccess` has no renewal time either.

### Timestamp chosen: `modifiedon` (renewal is otherwise undetectable)

Because no dedicated column exists and the history table is not populated, `modifiedon` is the only timestamp that moves on a renewal. It is used under the brief's fallback rule.

**Known false positives (not distinguishable from a renewal with existing columns):** a shortened expiry, a lowered level, the internal take-over stamp (`AddInternalTakeOverBinds`), the reconciliation job's R1 stamp, a record-wide `set-record-share-expiry`, and any edit of the row in the model-driven app. Each produces a "you were given access" item for a grant the person already had. The item is gated by the same rights re-check (`RightsOn`), the active + unexpired filter and the 30-day window, so it never reveals a record the caller cannot read; it is a **noise** risk, not a leak.

Mitigation: the row must have been changed more than 2 minutes after `createdon` (`ExternalDataService.RenewalMinGap`). That absorbs the follow-up writes of the create itself (take-over stamp, Assigned-To adopt) so a new grant is not announced twice. It does not remove the classes above. One row is one item: a grant created and then renewed inside the window shows once, at the renewal time.

If the owner wants these false positives gone, the fix is a dedicated column (for example `sprk_renewedon`, written by `CreateGrantAsync` when `AccessGained` is true over an existing row) and a switch of the feed to it; that is a schema change and is not done here.

### What changed

- `ExternalDataService.GetRecentGrantsAsync` takes an optional `modifiedSince`. With it the filter is `(sprk_granteddate ge D or modifiedon ge T)`, still `statecode eq 0` and the `ExpiryPredicate`, and `modifiedon` is selected. A `RecentGrant` gains `RenewedOn` (set only when both `createdon` and `modifiedon` read and the gap exceeds 2 minutes) and `EventOn` (renewal time when later, else grant time).
- `ExternalNotificationFeedEndpoints` passes `modifiedSince: cutoff` and uses `EventOn` for the window re-check, the ordering and the item time. Unread is still `OccurredOn > lastSeen`. The multi-root rule, title handling and rights re-check are untouched. `ExternalMultiRootRule.cs` was not edited.

---

## Test map

Run: `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~External|FullyQualifiedName~Notification|FullyQualifiedName~ServiceRequest"`.

| Requirement | Test(s) |
|---|---|
| 1 open SR deletes | `Requester_WithNoRelatedRows_Is204_AndTheRequestIsGone` (default state Open); `AnOpenRequest_WithActivity_IsStillTheRelatedRecordsConflict` |
| 1 closed → 409 `service_request_closed`, no delete call | `AClosedRequest_Is409_ServiceRequestClosed_AndNoDeleteIsSent` |
| 1 closed decided before related records | `AClosedRequest_IsDecidedBeforeTheRelatedRecordsCheck` (probe count 0) |
| 1 unknown state refused | `AnUnknownState_IsRefused_FailClosed_AndNothingIsDeleted`; `TheStateRule_OnlyZeroIsOpen_OnlyOneIsClosed_ElseUnknown` (incl. the enum default); `AnUnreadableState_Is503_AndNothingIsDeleted` |
| 1 transport | `TheStateRead_IsOneAppOnlyRead_OfStatecode_AndFailsClosedOnAnythingElse` (0, 1, 2, null, string, absent field); `TheStateRead_OfAnAbsentRequest_IsUnknown`; `TheStateRead_ThrowsWhenDataverseFails` |
| 1 requester-only unchanged | `NotTheRequester_WhoseRequestIsClosed_IsStill403_NotAConflict` + the existing `NotTheRequester_*`, `AbsentRequest_*`, `ACiamPartner_*` tests, unchanged and green |
| 1 related-records unchanged | the existing 8-relationship theory, 8 unreadable-probe theory, transport probe tests, unchanged and green |
| 1 `statecode` on the read | `ExternalModuleColumnAllowListTests` (production `ReadableColumns` equals the derivation, with `statecode`) |
| 3 must-fire | `ARenewedGrant_WithAnOldGrantedDate_Appears_AtTheRenewalTime`; `AGrantCreatedInTheWindow_AndThenRenewed_IsOneItem_AtTheRenewalTime`; data service: query shape with `modifiedSince`, `RenewedOn` parse |
| 3 must-not-fire | `AnOldGrant_NeverRenewed_DoesNotAppear`; `ARenewalOutsideTheWindow_DoesNotAppear_EvenIfTheReadReturnsTheRow` (endpoint re-check, double returns the row); `ARenewedGrant_OnARecordNoLongerAccessible_IsDropped`; `ASomeoneElsesRenewedGrant_DoesNotAppear`; data service: `modifiedon` equal to `createdon`, within the 2-minute gap, no `createdon` → no renewal; expiry predicate and `statecode eq 0` stay in the filter |
| 3 unread / seen | `ARenewal_IsUnreadOnlyAfterTheSeenMarker` |
| 3 "an unrelated edit" | **Cannot be tested as must-not-fire**: with `modifiedon` only, an unrelated edit is a renewal to the code. This is the documented known limit above; only creation-time follow-ups are suppressed. |
| 2 | no code, no tests (stopped) |

## Fail-before evidence

Each perturbation changed the production code in place, ran the targeted tests, and was restored (a backup copy was put back; `git diff` shows only the intended change).

| Perturbation | Result |
|---|---|
| Item 1: the closed check and the unknown-state refusal disabled (`if (false && …)`) in `DeleteOwnServiceRequest` | `ExternalDeleteServiceRequestTests`: **3 failed** (`AClosedRequest_Is409…`, `AClosedRequest_IsDecidedBefore…`, `AnUnknownState_IsRefused…`), 64 passed |
| Item 3: the feed uses `GrantedOn` instead of `EventOn`, and the renewal gap is removed (`>= TimeSpan.Zero`) | **6 failed**: `ARenewedGrant_WithAnOldGrantedDate…`, `AGrantCreatedInTheWindow_AndThenRenewed…`, `ARenewal_IsUnreadOnlyAfterTheSeenMarker`, `ARenewedGrant_OnARecordNoLongerAccessible…`, `ARenewalOutsideTheWindow…`, and the data-service `RenewedOn` parse test; 117 passed |
| Restored | all pass |

## Build, tests, size

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~External\|…~Notification\|…~ServiceRequest"` | **3,126 passed, 0 failed** |
| `dotnet test tests/Spaarke.ArchTests` | **941 passed, 0 failed** (no ledger change needed: no route was added or moved) |
| Full unit suite | Not run (brief, and project `CLAUDE.md` §6 gotcha 2026-10-11) |
| New package / DI / route / column | None. One new virtual method, one optional parameter, two additive record members. |

### Publish size (BFF hygiene item 4)

Method as `task-039-notes.md`: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` (linux-x64, framework-dependent, PDBs included), `Compress-Archive -CompressionLevel Optimal`, each side a fresh `git archive` export at a short path (`C:\wt1012m`, `C:\wt1012b`, `C:\wt1012t`, removed afterwards), 149 published files on every side.

| Tree | Zip bytes |
|---|---|
| `origin/master` (`37470dacc`, fresh) | 38,232,016 |
| R3 base `b60f11898` | 38,298,145 |
| This change (`fe39a9954`) | 38,299,332 |

- Delta against fresh master: **+67,316 bytes (+0.064 MB)** — almost all of it earlier R3 work that is not yet on master.
- Delta of this change alone (against its base): **+1,187 bytes**.
- Total 36.5 MB: under the 55 MB review line and the 60 MB hard stop; far under the +5 MB per-task line.
- No vulnerable package was added (no package was added).

## Open items for the owner

1. Item 2: choose an option above (or accept none).
2. Item 3: accept the `modifiedon` false positives, or approve a dedicated `sprk_renewedon` column.
3. SPA: hide Delete on `statecode = 1` (or `statuscode = 2`); handle the 409 `service_request_closed` body. **Done: see "Review fixes" below.**

## Review fixes (2026-10-12)

| Item | Fix |
|---|---|
| F4.1 | `delete-own.ts`: `REASON_SERVICE_REQUEST_CLOSED = 'service_request_closed'`; `deleteFailureOf` answers 409 with it as "This request is closed, so it can't be deleted. Nothing was deleted." (no "try again", not the stored-file wording). |
| F4.2 | `ServiceRequestActions.tsx`: no Delete when `isServiceRequestClosed(record)` (`statecode` 1 or `statuscode` 2; an unread state counts as open, the server decides) or after a 409 `service_request_closed` (a stale read), so the message is not retryable. With no failure to show the component renders nothing. **Recall:** unchanged. It is offered only after a `has_related_records` 409, and the server decides closed BEFORE the related-records probe, so a closed request never reaches it; the closed branch also withdraws the recall notice. Nothing is clearly wrong with it. |
| F4.3 | `RecordDetailEntity.extraColumns` (new, optional): columns read with the record but not rendered. The service-request cfg sets `['statecode']`; `readRecord` adds them to the FetchXML. `statuscode` is already shown as "Status", so the display is unchanged. The BFF module already allows `statecode`. |
| F4.4 | Tests in `tests/DetailDocumentsDelete.test.tsx`, describe "service request: closed": closed (statecode 1, statuscode 2) shows no Delete; open shows Delete (regression guard, passes before and after); the detail fetch includes `statecode` and it is not rendered; 409 closed shows the message, no retry, Delete withdrawn; the wording and the closed rule as units. Fail-before: 5 of the 6 `closed` tests failed with the four source files reverted (the open-request test passes both ways by design). |
| K2 | `ExternalDataService.GetServiceRequestRequesterAndStateAsync` (one row, `$select=_sprk_requestedby_value,statecode`) replaces `GetServiceRequestOpenStateAsync` (removed; no other caller). `RightsForServiceRequestAsync` gained `withState` and returns `(Rights, State)`; the document and message routes call it with `withState: false` and keep their requester-only read. The requester gate still runs first: a non-requester gets the uniform 403 and no state is returned or used (`ANonRequester_NeverHasTheStateUsed...`). Unknown state still 503 `delete_own_unavailable`, nothing deleted. **Behaviour change:** a row read that FAILS is now the requester read's 503 `service_request_unreadable` (was `delete_own_unavailable` when only the separate state read failed); both are retryable 503s with nothing deleted. Tests: transport tests moved to the new method; the fixture counts reads and `TheDelete_ReadsTheRequestRowOnce...` pins one. |
| K1 | Known-limit remark on `DeleteOwnServiceRequest` now says a request closed by staff between the state read and the delete is still deleted (an `If-Match` delete would close it; not done). |
| K3 | Route summary mentions `service_request_closed`; ledger entry for `DeleteOwnServiceRequest` names the state gate, hops through `RightsForServiceRequestAsync` and carries current line refs (:988, :1985, :2001); project `CLAUDE.md` gotcha now says `MapsExactlyFourDeleteRoutes` and lists the service-request delete. |
| K5 | `GetRecentGrantsAsync` orders by `modifiedon desc` (then id desc) when `modifiedSince` is given, else `createdon desc` as before. Pinned-filter tests assert both orders. |

Known limits left as they were: the `If-Match` window (K1); `modifiedon` false positives on renewals (Item 3).
### Verification

- BFF: build 0 warnings / 0 errors; filtered unit run 552 passed, 0 failed; `Spaarke.ArchTests` 941 passed, 0 failed.
- SPA: `typecheck`, `lint` clean; full vitest, `build` and `check:dist` results in the session report.
