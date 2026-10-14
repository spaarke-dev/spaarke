# Task 037 — In-portal notification feed + last-seen marker (FR-08)

Date: 2026-10-12. Rigor FULL (tags bff-api, auth, cache, testing). Implemented by a wave implementer agent in an isolated worktree, branched from the R3 head `0cd675b77`. The review passes belong to the main session.

## What it does

Two routes, both mapped in the `/api/v1/external` group (`ExternalAccessEndpoints.MapExternalUserEndpoints` → `externalGroup.MapExternalNotificationFeedEndpoints()`). They inherit `ExternalCollaboration` and `CallerPrincipalAuthorizationFilter`. Identity comes only from `HttpContext.Items[CallerPrincipal]`. Neither handler branches on plane.

- `GET /api/v1/external/notifications` returns the caller's derived feed and an `unreadCount`.
- `POST /api/v1/external/notifications/seen` sets the caller's last-seen marker to the server's "now".

The feed has two sources. There is no new table (owner 2026-10-08).

**(a) Grants.** `ExternalDataService.GetRecentGrantsAsync` makes an app-only read of the `sprk_externalrecordaccess` rows where:
- `_sprk_contact_value` is the caller's contact;
- `statecode eq 0`;
- `sprk_granteddate ge` today − 30;
- the enforcement path's `ExpiryPredicate` holds, so a lapsed grant is never announced.

A row whose root is not exactly one of project, matter or work assignment is skipped. That happens, for example, when the root was deleted and the lookup emptied. Each remaining row's root is then checked with `CallerPrincipal.RightsOn(root)`, which must carry Read. A grant row for a record that is no longer accessible is dropped (fail-closed). The item time is the row's `createdon`. `sprk_granteddate` is a date written once, on create (`GrantExternalAccessEndpoint.BuildGrantPayload` is its only writer).

**(b) Messages.** The records searched are:
- the caller's Read-bearing projects, matters and work assignments;
- the caller's own service requests. These are read app-only by `sprk_requestedby` (`GetServiceRequestsRequestedByAsync`) and then admitted by the Service Requests module's own `ScopeRows`, so the partner switch is honoured.

Over those records, `CommunicationThreadReadService.ReadRecentForRecordsAppOnlyAsync` (new) runs the app-only query. It chunks the records 50 per query, caps them at 1,000, and asks for `$top = 101`, newest first, `createdon ge` the cutoff. The external rules are the task 033 read's, through shared helpers:
- the status allow-list (Send / Delivered);
- `ExternalPrivilegeFilterClause` in the query, plus `IsHiddenFromExternal` per row (`IsExternallyVisibleRow`, which the 033 read now also uses);
- the shared `ICommunicationAccessFilter`, so internal-only messages reach an internal systemuser only (the `IsExternalAsync` bit).

Every row is re-checked: the window, and that one of its regarding values is one of the requested records. That record is the one the item reports. A message filed on an invoice reaches its root through the core-ancestor stamp. The caller's own messages are dropped by `ExternalMessageEndpoints.IsAuthoredBy`, the 034 `isMine` rule: `sprk_sentby` for a systemuser and `sprk_createdbycontact` for a contact. Direction and sender name are never used.

**Titles.** `ExternalDataService.GetRecordNamesAsync` reads the primary name columns that the to-do `RootBindings` use, chunked. Service-request names come from the requester read. When a title read is complete and does not return a record, that record is gone, so its items are dropped and the feed still answers (task 032 deletes). When a title read fails, the items are kept with `recordTitle = null`.

**Bounds.** The feed covers 30 days, returns at most 100 items, and is ordered newest first. Grants are cut to 100 before the merge, and messages are read with a cap of 100. Any cut sets `truncated`. A source read that fails or is truncated also sets `truncated`.

**Last-seen marker.** The marker is kept through `ITenantCache` (Redis, ADR-009; `decisions.md` 2026-10-10). Its key is:
- resource `external-notifications-seen`;
- id `systemuser:{id}` or `contact:{id}` — the principal kind decides, as the creator stamp does;
- tenant = the token `tid` (`MembershipEndpoints.ExtractTenantId`);
- version 1.

Every `POST /seen` refreshes its 30-day TTL. When the marker is missing or cannot be read, every item counts as unread and the feed still answers. When the write fails, the POST answers a retryable 503.

There is no email, no OBO, no new package and no new DI registration. `ExternalDataService`, `CommunicationThreadReadService`, `ITenantCache` and `ExternalModuleRegistry` are all registered already.

## SPA contract (for task 055, the bell)

### `GET /api/v1/external/notifications`

No query parameters. Auth: the external SPA token (CIAM or workforce), the same as every `/api/v1/external` route. The route is rate-limited with `dataverse-query` (50 requests per minute per user, sliding window), so poll at most about once a minute.

200 body:

```json
{
  "value": [
    {
      "kind": "grant",                // "grant" | "message"
      "id": "…guid…",                 // sprk_externalrecordaccessid | sprk_communicationid
      "recordType": "matters",        // projects | matters | workassignments | servicerequests
      "recordId": "…guid…",
      "recordTitle": "Acme v. Widget",// null when unreadable
      "occurredOn": "2026-10-10T09:12:00+00:00",
      "senderName": null,             // message: sender display name (or null); grant: null
      "isUnread": true
    }
  ],
  "unreadCount": 1,                   // items in value with isUnread = true
  "lastSeenAt": "2026-10-09T08:00:00+00:00", // null when no marker is stored
  "truncated": true                   // OMITTED when false
}
```

- `value` is newest first, holds at most 100 items, and none older than 30 days.
- `recordType` is the external **API route segment**, not an SPA route. `/api/v1/external/{recordType}/{recordId}/messages` is the record's thread. The SPA opens a record in `RecordDetailModal`, so task 055 needs a segment-to-opener mapping (`projects`, `matters`, `workassignments`, `servicerequests` each to the SPA's own opener for that record type); it must not build a URL from the segment.
- `unreadCount` counts the **returned page only** (at most 100 items). When `truncated` is true the real number may be higher, so task 055 shows "100+" instead of the bare count.
- No message body, subject, address, privilege or internal-only value, and no contact or systemuser id, is in the response.

Errors:

| Status | Body | Meaning |
|---|---|---|
| 401 / 403 | from the group policy or the principal filter | not signed in, or not resolvable |
| 429 | rate limit | back off |
| 500 | `errorCode: EXT_NOTIFICATIONS_INTERNAL_ERROR` | a Dataverse read failed; retry later |
| 503 | `errorCode: EXT_NOTIFICATIONS_CONTACT_UNREADABLE` | the caller's linked contact could not be read right now (a Dataverse fault on the systemuser row or the oid binding), so the grant and service-request sources would be missing. Nothing was read; a fixed retry detail, no exception text. Retry shortly; do not show an empty bell. |

### `POST /api/v1/external/notifications/seen`

No body. The same auth and rate limit apply.

| Status | Body |
|---|---|
| 200 | `{ "lastSeenAt": "<server now, ISO-8601>" }` |
| 403 | `reasonCode: sdap.external.notifications.caller_unattributable` — the principal has neither a systemuser nor a contact |
| 503 | `errorCode: EXT_NOTIFICATIONS_SEEN_UNAVAILABLE` — the marker was not stored; retry |
| 503 | `errorCode: EXT_NOTIFICATIONS_CONTACT_UNREADABLE` — the caller's contact could not be read; NO marker is written, so a marker can never be set while a source was missing. Retry |

After a 200, a re-read of the feed shows `unreadCount = 0` until a newer item arrives. The items stay in `value` with `isUnread = false`.

## §11 component justification

**`GET /api/v1/external/notifications` and `POST /api/v1/external/notifications/seen`** (new endpoints in the new file `ExternalNotificationFeedEndpoints.cs`)
1. Existing — `appnotification` (Dataverse) targets systemusers only. The internal unread count (`GET /api/communications/threads/{id}/unread-count`) is per thread and impersonated. No external feed exists.
2. Extension — there is no external notification surface to extend. The routes reuse the 033 read rules (extended in place), the grant rows, the module registry and `ITenantCache`.
3. Cost of doing nothing — users get no in-portal notice of new access or messages (G3). Since the owner chose option A on 2026-10-11, the feed is the only way members learn of a new message.

**`CommunicationThreadReadService.ReadRecentForRecordsAppOnlyAsync` + `AppOnlyRecentReadResult` / `RecentRecordMessage`** (new public method on an existing service)
1. Existing — `ReadRegardingAppOnlyAsync` (task 033), which reads one record, oldest first, with no window.
2. Extension — this method is that extension. It shares the projection (`ExternalSelect`), the per-row rule (`IsExternallyVisibleRow`) and the access-filter tail (`FilterExternalMessagesAsync`). The 033 method now calls the same helpers, so the privilege and status rules exist in one place. Calling the 033 method once per record would mean N queries, each unbounded in time.
3. Cost of doing nothing — the feed would need its own `sprk_communication` query, which is the forked filter the owner decision forbids.

**`ExternalDataService.GetRecentGrantsAsync` / `GetServiceRequestsRequestedByAsync` / `GetRecordNamesAsync` + their result records** (new virtual methods on the existing app-only reader)
1. Existing — `ExternalParticipationService` reads grant rows to compose access, but it does not expose `createdon` or the row id, and it caches. `GetProjectContactIdsAsync` is private and project-only. `GetServiceRequestRequesterAsync` reads one row by id.
2. Extension — these are extensions of `ExternalDataService`, as the POML names. They reuse `GetCollectionAsync` / `GetByIdsAsync` (paging, the honesty flag), `ExpiryPredicate` and `RootBindings`.
3. Cost of doing nothing — the feed cannot list a caller's recent grants, own requests or record titles.

**`ExternalNotificationDtos.cs`** — the response allow-list. The allow-list is the security property: no body, address, privilege or id crosses the surface.

There are no new DI registrations, packages, options, secrets or Dataverse columns. ADR-010 is unaffected.

## Placement Justification (`.claude/constraints/bff-extensions.md`)

The feed lives in the BFF, in the existing external-access endpoint group and the existing communication read model and external data reader. Everything it composes is already in the BFF and runs in-process: the authorization (`CallerPrincipal`, the module registry), the communication read rules and the Redis wrapper. It is request-scoped reads plus one cache write. There is no AI, no background work and no new package.

## Decisions

1. **Systemuser grants (POML escalation trigger; recorded in `decisions.md` and continued).** Team-membership access writes no grant row. A systemuser therefore sees grant items only for grants made to its linked contact, and otherwise sees messages only. Organization grants, which have no contact on the row, are not announced. No other grant source was invented.
2. **Item time for a grant is `createdon`.** `sprk_granteddate` is a date only. Using midnight would hide a grant made after a same-day `/seen`. A re-grant (a level or expiry change) is not a new item.
3. **`recordType` is the route segment** (the 033 `Roots` table), not the logical name, so the bell links straight to the detail and the thread.
4. **A deleted record's items are dropped only on a complete title read.** A failed title read keeps the items with no title and sets `truncated` (review K2). Unreadable is not the same as deleted. The names read has no `statecode` filter, so a deactivated record is kept; only a deleted one is dropped.
5. **Both routes are rate-limited** with `dataverse-query`. The GET makes up to about 25 app-only reads per call, and a bell polls.
6. **The own-message exclusion runs after the read.** The query takes the newest 101 rows, and the caller's own posts are removed afterwards. A caller who posted most of the recent messages can therefore see fewer than 100 items, flagged `truncated` only when the read was cut (K3).

## Tests

`tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalNotificationFeedTests.cs` holds 34 cases. `ExternalNotificationFeedRouteTests` holds 1 host-based case.

The cases are driven through `GetFeedCoreAsync` / `MarkSeenCoreAsync`, then the production module registry, then the real `ReadRecentForRecordsAppOnlyAsync` and the real `CommunicationAccessFilter`. Only the I/O seams are doubled:
- the app-only query. It returns every row unfiltered, so each negative case proves the server's own re-check.
- the three `ExternalDataService` reads, in memory.
- the cache: `InMemoryTenantCache`, or a Moq for the fault and key cases.

The impersonated query and the identity resolver are Strict mocks.

| Criterion | Test(s) |
|---|---|
| AC1 a grant appears with record type, id and title | `ANewGrantToTheCaller_Appears_WithItsRecordTypeIdAndTitle` (project, matter, work assignment) |
| AC2 someone else's message appears; the caller's own does not | `AMessageBySomeoneElse_OnAnAccessibleRecord_Appears_WithTheRecordAndSender`; `AContactCallersOwnMessage_DoesNotAppear_ButAnotherContactsDoes` (CIAM, workforce contact); `ASystemUserCallersOwnMessage_DoesNotAppear_DecidedBySentBy` |
| AC3 internal-only never appears for a contact | `AnInternalOnlyMessage_NeverAppears_NorCounts_ForAContactCaller` (CIAM and workforce contact, flag true or unset) |
| Owner 2026-10-11: privileged messages never visible | `AHiddenPrivilegeMessage_NeverProducesAFeedItemOrACount_EvenForAnInternalSystemUser` (Privileged, Potentially Privileged, an undefined value; asserts the shared clause is in every query); `ANoneOrUnclassifiedMessage_IsShown` (control) |
| AC4 a record outside the set never appears | `AGrantRowForARecordOutsideTheAccessibleSet_IsDropped`; `AMessageOnARecordOutsideTheSet_NeverAppears_AndTheQueryIsKeyedOnlyOnTheSet`; `ACallerWithNoAccessibleRecordAndNoGrant_GetsAnEmptyFeed_WithNoMessageRead` |
| AC5 unread against last-seen; `/seen` clears it until a newer item | `UnreadCount_DropsToZeroAfterSeen_UntilANewerItemArrives`; `TheMarkerIsPerPrincipal_OneCallersSeenDoesNotClearAnothers` |
| Cache miss → all unread (POML step 5) | `ACacheMiss_MarksEveryItemUnread`; `AnUnreadableMarker_IsACacheMiss_AndTheFeedStillAnswers` |
| AC6 older than 30 days excluded; at most 100 | `ItemsOlderThanThirtyDays_AreExcluded`; `AtMostOneHundredItems_NewestFirst_AndTheCutIsFlagged`; `AFeedUnderTheBound_IsNotFlaggedTruncated` |
| Routes in the external group, authorized | `FeedRoutes_AreMappedInTheExternalGroup_Authorized_AndRateLimited` |

Tests beyond the stated criteria, one line each:
- `AnInternalSystemUser_DoesSeeAnInternalOnlyMessage_TheFilterIsTheSharedOne` — a positive control, so the AC3 negatives cannot pass on an always-hide bug.
- `AnUnsentMessage_Draft_NeverAppears` — the 033 status allow-list also holds on the feed path.
- `AMessageOnTheRequestersOwnServiceRequest_Appears_ButNoOneElsesRequestIsRead` and `ACiamRequester_SwitchOff_GetsNoServiceRequestMessages_AndSwitchOn_Does` — the brief's `sprk_requestedby` scope, and the FR-06 switch in both settings (gotcha 2026-10-10).
- `AnItemWhoseRecordNoLongerExists_IsDropped_AndTheFeedStillAnswers` and `WhenTheTitleReadFails_ItemsAreKept_WithNoTitle` — the brief: deleted records must not break the feed.
- `Seen_StoresTheMarker_KeyedByThePrincipalId_WithAThirtyDayTtl` and `Seen_ByAnUnattributablePrincipal_Is403_AndACacheFault_Is503` — the POML ADR-009 constraint (the key includes the principal id) and the `/seen` error contract.
- `TheSerializedFeed_CarriesNoBodySubjectAddressPrivilegeOrIds` — the response allow-list.

### Fail-before / seeding proofs

The routes, the read method and the data-service methods did not exist before this task, so the test file does not compile against `0cd675b77`. Before the change, every `/notifications` request was a routing 404.

The proofs below change the new code in place and run the feed tests plus the 033/034 `ExternalMessageEndpointsTests` (90 cases). A script (`perturb037.py`, in the session scratchpad) copied each file to a backup, applied one change, rebuilt, ran the tests, and restored the file.

| Perturbation | Result |
|---|---|
| P1 privilege per-row re-check removed (shared by the feed and the 033 read) | 7 failed: the feed's hidden-privilege theory and the 033 `PrivilegedAndPotentiallyPrivilegedMessages_…` theory |
| P2 privilege clause dropped from the feed query | 3 failed (hidden-privilege theory) |
| P3 grant root not re-checked against the accessible set | 1 failed (`AGrantRowForARecordOutsideTheAccessibleSet_IsDropped`) |
| P4 a row matching no accessible record kept | 2 failed (record outside the set; someone else's service request) |
| P5 the caller's own messages not excluded | 3 failed (contact ×2, systemuser) |
| P6 every caller treated as internal | 7 failed (feed internal-only theory ×4, 033 CIAM / workforce / external-licensed) |
| P7 the 30-day window not re-checked per row | 1 failed (`ItemsOlderThanThirtyDays_AreExcluded`) |
| P8 last-seen marker ignored | 2 failed (unread-after-seen; per-principal marker) |
| P9 items of a deleted record kept | 1 failed (`AnItemWhoseRecordNoLongerExists_…`) |
| Restored | **90 passed, 0 failed** |

## Build, tests, measurements

| Command | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` | 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~ExternalAccess\|…~ExternalMessage\|…~CommunicationThreadReadService\|…~ExternalNotification\|…~CommunicationListThreadsSeam\|…~CommunicationWorkspaceReadSeam"` | **2,342 passed, 0 failed** |
| Feed tests alone (`~ExternalNotificationFeed`) | 35 passed, 0 failed |
| `dotnet test tests/Spaarke.ArchTests/` | First run: 1 failure (a Permanent reason must cite file:line). After the fix: **941 / 941 pass** |
| `dotnet list src/server/api/Sprk.Bff.Api/ package --vulnerable --include-transitive` | No vulnerable packages. No package was added. |
| Full unit suite | Not run: the machine is loaded (project `CLAUDE.md` §6 gotcha 2026-10-11, and the brief). |

**Route ledger** (`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`):
- a `GovernedFile` entry (GroupGated);
- two `Permanent … CallerScopedOnly` entries, following the `/me` precedent (neither route takes an id), citing `ExternalNotificationFeedEndpoints.cs:191, :217, :274, :285, :310, :325` (moved by the 2026-10-12 review fixes);
- census 120 → 121, with a history line.

**Publish size** (bff-hygiene item 4). Each side was built from a fresh `git archive` of its commit (`src/server`, `config`, `Directory.*.props`, `global.json`) into a short path:
- `C:\tmp\wt037b` — this branch at `9da1bcef6`;
- `C:\tmp\wt037m` — freshly fetched `origin/master` at `eaa29bafe`;
- `C:\tmp\wt037p` — the parent at `0cd675b77`.

Each side ran `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, then `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included. Every publish has 192 files and 4 PDBs, so the counts match.

| Tree | Zip bytes | MB |
|---|---|---|
| branch (`9da1bcef6`) | 38,289,439 | 36.52 |
| fresh master (`eaa29bafe`) | 38,232,191 | 36.46 |
| parent (`0cd675b77`) | 38,268,738 | 36.50 |

- Delta vs fresh master: **+57,248 bytes (+0.05 MB)**. This covers the whole unmerged R3 branch.
- Delta caused by task 037 alone (vs parent): **+20,701 bytes**.
- The total is 36.52 MB, under the 55 MB review line and the 60 MB hard stop.

## Known limits (not fixed here)

- **K1 — systemusers and organization grants.** See decision 1. Licensed staff with team access see messages, plus grants to their linked contact only. Organization-grant members get no grant item.
- **K2 — `/seen` marks "now", not "what was displayed".** An item created between the bell's last GET and the POST is marked read unseen. A future `seenThrough` body field would close this. Server/Dataverse clock skew is not corrected.
- **K3 — own-message exclusion after the read.** See decision 6.
- **K4 — record scan cap.** A caller with more than 1,000 accessible records is answered from the first 1,000, in principal order, and flagged `truncated`. The GET can make up to 20 message queries plus 3–4 reads; there is no caching of the feed itself.
- **K5 — a re-grant is not an item, and a restored grant is silent (OPEN OWNER QUESTION).** Changing the level or expiry of an existing row does not re-announce it. Worse, a lapsed grant restored in place (`GrantExternalAccessEndpoint.cs` ~:562-602, which raises `AccessGained` and which task 036 emails) produces **no bell item at all**: the row's `sprk_granteddate` and `createdon` are unchanged, so the feed's 30-day window (and its unread marker) still place it where it was, or outside the window. The member is emailed that they have access again, and the bell says nothing. Owner decision needed: re-stamp a date on restore (a grant-endpoint change, outside task 037 and not made here), or accept the email as the only notice. The grant endpoint was not touched.
- **K6 — a Dataverse fault on the message read is a coded 500, not a partial feed.** Grant, service-request and title read failures degrade to `truncated` or a missing title.
- **K7 — the marker is not durable.** A Redis loss re-marks the window as unread, by design (`decisions.md` 2026-10-10).
- **Hook note.** The repo's lint-staged pre-commit hook makes and drops a backup stash entry on the shared stack during `git commit` (entry `69cdbf8e7`, already gone afterwards). This agent ran no `git stash` itself.

## Review fixes (2026-10-12)

A fix round for the review of `9da1bcef6` / `196f162d0`. Tests run filtered (`ExternalNotificationFeed|ExternalDataService|ExternalMessage|CommunicationThreadReadService|CallerPrincipal`) because the machine is loaded.

- **F3-a — an unreadable contact is a 503, not a thin feed.** When `CallerPrincipal.ContactUnreadable` is true and no contact was derived, `GET /notifications` and `POST /notifications/seen` both answer 503 `EXT_NOTIFICATIONS_CONTACT_UNREADABLE` with a fixed retry detail (the task 035 intake rule, `ExternalIntakeEndpoints.cs` ~:152). Before: the feed skipped the grant source and the service-request messages but reported itself complete, and a following `/seen` buried those items for good. After: nothing is read and no marker is written. Tests: both routes with `ContactUnreadable = true` give 503, with no grant, service-request, title or message read and no cache call (a strict mock); a systemuser with a contact is unaffected.
- **F3-b — the three `ExternalDataService` reads are tested at the HTTP seam** (`ExternalDataServiceTests`, via `CapturingHandler`, which now records GET URLs and can script responses). They assert the grants query (`_sprk_contact_value eq`, `statecode eq 0`, `sprk_granteddate ge`, `sprk_expiresdate ge`, the `$orderby`, no `$top`), the service-request filter on `_sprk_requestedby_value`, the entity sets and id/name columns for projects, matters and work assignments, the 100-id chunking, and the parse: a two-root or rootless row is skipped, `createdon` maps to `GrantedOn` with the `sprk_granteddate` fallback, a failed read or chunk gives `Truncated` / `Complete = false`, and a missing id in a complete read is gone. Fail-before proofs are in the commit report.
- **K2 — a failed title read is reported.** `truncated |= !names.Complete`. Before: the items were kept untitled and the response said nothing.
- **K3 — the feed no longer reads message bodies.** `ReadRecentForRecordsAppOnlyAsync` projects without `sprk_body`, `sprk_bodyformat` and `sprk_subject` (`ExternalSelect(..., includeContent: false)`). The task 033 thread read keeps its exact select, now asserted in `ExternalMessageEndpointsTests`; the feed's `$select` is asserted in `ExternalNotificationFeedTests`.
- **K4 — the per-row 30-day re-check is exercised for grants.** A grant on the boundary day at 08:00Z (date at or after `since`, instant before `now - 30 d`) is excluded; one at 13:00Z is kept.
- **Doc fix.** `RecordNamesRead` said "deleted or deactivated is not distinguished". The names read has no `statecode` filter, so a deactivated record is kept and only a deleted one is dropped.
- **Contract notes** for task 055 (the `recordType` mapping, `unreadCount` on a truncated page, the new 503s) are in the SPA contract above, and K5 is sharpened.
- **Route ledger** citations in `RouteAuthorizationGuardTests.Ledger.cs` were moved to the new lines.
