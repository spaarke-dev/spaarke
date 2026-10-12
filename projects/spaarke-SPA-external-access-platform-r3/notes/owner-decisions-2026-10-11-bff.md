# Owner decisions 2026-10-11 — BFF half (service-request delete, privileged-message hiding, system sender)

Date: 2026-10-12. Implemented by a wave implementer agent in an isolated worktree (`worktree-agent-a148abc424ee6ab09`, based on R3 head `980e850f1`). One commit per part. The SPA half (delete button, recall action, the 409 message) is not in this change.

Commits (oldest first):

| Part | Commit | Subject |
|---|---|---|
| 1 | `00d9dd275` | requester deletes own service request when it has no related records; recall = message post (FR-26) |
| 1 (fix) | `f3d328739` | route-ledger reason for the service-request delete cites file:line (Task 167 guard) |
| 2 | `4190cfe2a` | hide privileged messages from external callers |
| 3 | `5ddcdea7d` | the grant/registration email sender is configuration, not a literal |

(This notes file and the guide edit ride in the commit that follows these.)

---

## Part 1 — `DELETE /api/v1/external/servicerequests/{id}` (option A)

### What was built

- **Route.** `group.MapDelete("/servicerequests/{id:guid}", DeleteOwnServiceRequest)` in `ExternalProjectDataEndpoints`, beside the document / to-do / event delete routes. It inherits `ExternalCollaboration` and `CallerPrincipalAuthorizationFilter`.
- **Order of checks.**
  1. Requester only. `RightsForDocumentParentAsync(ServiceRequest)` is the same gate the service-request document routes use: the Service Requests module's accessible set is `{caller ContactId}` (`sprk_requestedby`), and the CIAM partner switch (`ExternalAccess:PartnerServiceRequestsEnabled`, default off) is honoured. No Read means the uniform 403. Absent and not-mine are the same 403 (no existence oracle), and a request that has activity is still a 403 for a non-requester, never a 409.
  2. `ExternalDataService.HasRelatedRecordsAsync(id)` probes all 8 one-to-many relationships of `sprk_servicerequest`, app-only and in parallel, `$top=1` each. Any row in any relationship means 409. Any probe that cannot be read (non-success including 404, a body with no `value`, a transport fault) throws `DataverseUnavailableException` and the handler answers 503 with nothing deleted. A found row does not mask a failed probe.
  3. No related rows: `DeleteAuthorizedRowAsync(ServiceRequest)` hard-deletes the row app-only, 204.
- **The 8 relationships** (all `CascadeDelete=RemoveLink` — a delete would orphan staff rows, hence option A): `sprk_communication`, `sprk_communicationthread`, `sprk_document`, `sprk_event`, `sprk_todo`, `sprk_analysis`, `sprk_memo`, `sprk_signal`. A test reads `src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_ServiceRequest.xml` and fails if the solution gains a relationship the list lacks.
- **Recall = REUSE.** No endpoint, column or email was added. See the contract below.
- **Tests pinned deliberately.** `TheExternalPlane_MapsExactlyFourDeleteRoutes` (was three) and `NoDeleteRoute_ForMessages` (the service-request half of the old "no delete for messages or service requests" pin is withdrawn by this owner decision; messages and communications stay unmapped). Route ledger: new `HandlerDecision` for the route.

### §11 — new surface

| Surface | 1. Existing | 2. Extension | 3. Cost of doing nothing |
|---|---|---|---|
| `DELETE …/servicerequests/{id}` route | The three delete-own routes (documents, to-dos, events) and the task 032 framework | Extends that framework (`DeletableKind.ServiceRequest`, same `DeleteAuthorizedRowAsync`, same uniform 403 / 503 shapes); no new gate, the requester gate is `RightsForDocumentParentAsync` | A requester cannot withdraw a request they raised in error; the owner decision (FR-26) fails |
| `ExternalDataService.HasRelatedRecordsAsync` + 8-row `ServiceRequestRelations` | `AnyRowAsync`-style app-only Dataverse reads already in `ExternalDataService` | Added beside `DeleteRecordAsync`, using the same app-only client and `DataverseUnavailableException` | Without it a delete would silently unlink staff rows (`RemoveLink`) — orphaned documents / messages / tasks |
| `has_related_records` reasonCode | The single `reasonCode` problem key from task 032 | One new value on the existing key | The SPA could not tell "has activity" from a refusal and could not offer recall |
| Recall | Task 034 message post | Reused as is; nothing added | Not applicable: nothing was added |

### Fail-before evidence

With the handler's related-record check replaced by `hasRelated = false`: **17 of the new SR tests fail** (8 × `OneRelatedRow_OfAnyKind_Is409_HasRelatedRecords_AndNothingIsDeleted`, 8 × `AnUnreadableRelationshipCheck_Is503_AndNothingIsDeleted`, and `TheConflict_TellsTheUserTheRequestHasActivity_AndTheyCanAskLegalToRecallIt`). Restored: all pass. Transport tests (probe URLs, auth, `$top=1`, fail-closed statuses) run against the real `ExternalDataService` over a hand-written `HttpMessageHandler` (ADR-038).

### SPA contract

`DELETE /api/v1/external/servicerequests/{id}` — no body. Bearer token as for every external route.

| Status | Body | Meaning |
|---|---|---|
| 204 | none | Deleted (the caller is the requester and the request has no related record) |
| 401 | — | No / invalid token |
| 403 | problem, `reasonCode = sdap.access.deny.delete_own` | Not the requester, request absent, or a CIAM partner with the switch off (all indistinguishable) |
| 409 | problem, `reasonCode = has_related_records`, `title = "Service request has activity"`, `detail = "This request already has activity (documents, messages or other records), so it can't be deleted. You can ask legal to recall it by sending a request to recall."` | Any related row in any relationship. Offer **Recall** |
| 503 | problem, `reasonCode = sdap.access.delete_own_unavailable` | A relationship check or the row delete could not complete; nothing was deleted; retry |
| 503 | problem, `reasonCode = sdap.access.service_request_unreadable` | The requester could not be read; nothing was deleted; retry |

Recall (the 409 follow-up) is the task 034 message post on the service-request thread:

```
POST /api/v1/external/servicerequests/{id}/messages
{ "sprk_body": "Please recall this request." }
```

- 201 with `Location: /api/v1/external/servicerequests/{id}/messages` and an `ExternalMessageDto` (task 034). Plain text, 1 to 10,000 characters; no attachments; no email is sent.
- The requester has Read + Create on their own request, so the post is allowed; anyone else gets 403 `sdap.external.messages.record_not_accessible`.
- The sender is stamped from the principal (`sprk_createdbycontact`); the body cannot choose it.
- **Consequence the SPA should expect:** once a recall message is posted the request has a communication row, so it can no longer be deleted (409). Recall is a request to staff, not a deletion.

### Known limits

- The probe and the delete are separate calls. A related row created between them (for example by staff) is unlinked, not deleted. There is no transaction across the two.
- The eight probes run in parallel (8 small reads per delete attempt; the route is rare and user-initiated).

---

## Part 2 — privileged messages are hidden from external callers

### What was built

- **Rule.** An external message read never returns a communication whose `sprk_privilegeclassification` is Privileged (100000002) or Potentially Privileged (100000001). None (100000000) and null are shown. An unrecognised value is hidden (fail closed).
- **One named constant.** `CommunicationThreadReadService.ExternallyHiddenPrivilege` (a list of `CommunicationPrivilegeClassification`). **To relax to Privileged-only, delete `PotentiallyPrivileged` from that list — one line.** The Dataverse `$filter` clause (`ExternalPrivilegeFilterClause`) and the in-memory re-check (`IsHiddenFromExternal`) are both derived from it; nothing else names the values.
- **Where.** `ReadRegardingAppOnlyAsync` is the single external message read (task 033), shared by the list, the count and the single-message read. The clause is in the Dataverse `$filter` (so `$top=501` paging cannot be short-changed by hidden rows), and the same rule is re-applied in memory. A hidden message requested by id is 404, exactly like an absent one. `CommunicationThreadReadService` was extended, not forked; the internal read paths are unchanged.
- **Other external read paths.** At this base there is only the one. The activity feed (task 037) and any count or "latest message" added later must use `CommunicationThreadReadService.ExternalPrivilegeFilterClause` (or read through `ReadRegardingAppOnlyAsync`).
- **Post response.** The 201 from the task 034 post has no privilege member, and a new post is the author's own plain row, so nothing leaks through the post.
- **Data facts.** The column defaults to None. It is not field-secured. The classification value is never put in a response (`ExternalMessageDto` has no such member).

### §11 — new surface

| Surface | 1. Existing | 2. Extension | 3. Cost of doing nothing |
|---|---|---|---|
| `ExternallyHiddenPrivilege` + `ExternalPrivilegeFilterClause` + `IsHiddenFromExternal` | `CommunicationThreadReadService` (the external read), `CommunicationPrivilegeClassification`, `ActiveOnlyClause` / `ExternalStatusClause` | Added next to those clauses inside the same service; no new service or endpoint | A privileged or potentially privileged message is returned to an outside party — a privilege waiver |

### Fail-before evidence

With the `$filter` clause and the in-memory re-check removed: **5 tests fail** (4 × `PrivilegedAndPotentiallyPrivilegedMessages_AreAbsent_NoneAndNullAreShown_ForEveryPrincipalKind` — workforce-contact, external-licensed-systemuser, ciam-contact, internal-systemuser — and `ThePrivilegeRule_IsInTheDataverseFilter_NotOnlyAfterTheRowsCame_SoPagingCannotShortChangeIt`). Restored: all pass. Also covered: null / None present, unknown value hidden, the named set, `IsHiddenFromExternal`, internal reads unchanged, the post response carries no classification.

### SPA contract

No route or body change. `GET /api/v1/external/{root}/{id}/messages` and `…/messages/{messageId}` simply never contain privileged / potentially privileged messages; the count in a list is the count of what is returned; a hidden message by id is the same 404 as an absent one. Nothing in the response signals that anything was hidden.

---

## Part 3 — the system sender mailbox is configuration

### What was built

- **Key reused:** `Communication:DefaultMailbox` (`CommunicationOptions.DefaultMailbox`), the platform's existing default mailbox. No new key. The stamp channel already supplies it (`Communication__DefaultMailbox`, Key Vault secret `Communication-DefaultMailbox`).
- **`RegistrationEmailService`.** The `FromMailbox` constant is gone. The service takes `IOptions<CommunicationOptions>`, and `ResolveFromMailbox` returns the trimmed `DefaultMailbox` or throws `InvalidOperationException` when it is blank (no substitute sender is ever used). `BuildPortalNotificationRequest` takes the mailbox as a parameter. Used by registration, CIAM onboarding and the R3 grant / portal notification emails.
- **`appsettings.template.json`.** `Communication:DefaultMailbox` = `mailbox-central@spaarke.com` with `_DefaultMailbox_comment`; the `ApprovedSenders` entry now has the same address. `ApprovedSenderValidator` requires the sender to be approved, so the two must stay equal. The old `#{COMMUNICATION_DEFAULT_MAILBOX}#` token is no longer used anywhere. No tracked dev appsettings carries the key.
- **Fail fast.** `CommunicationOptionsValidator` (`IValidateOptions<CommunicationOptions>`, registered in `CommunicationModule`) fails when `DefaultMailbox` is empty outside Development and Testing, with a message naming the key. `StartupValidationService` reads `IOptions<CommunicationOptions>.Value` at host start, so a deployed host with no sender refuses to start instead of failing on its first grant email. `CommunicationOptions` as a whole is deliberately not validated at start (it carries four `[Required]` webhook members that exist only where Graph webhooks are provisioned), so the validator checks this one member only. The `IOptionsDriftTests` ratchet tracks `ValidateOnStart` chains only, so it is unaffected.
- **Other hard-coded `demo@demo.spaarke.com` occurrences** (grep of the tracked tree):
  - Source: none left. (Comments in `RegistrationEmailService.cs`, `CommunicationOptionsValidator.cs` and the template comment mention the old literal.)
  - Tests: `tests/integration/auth/UnifiedAccessControl/IdentityBinding/IdentityLinkReconciliationTests.cs:51` — a test fixture email, unrelated to sending. Left alone.
  - Docs: `docs/guides/AZURE-SETUP-SELF-SERVICE-REGISTRATION.md:503` — an Exchange `Test-ApplicationAccessPolicy` example; **not changed** (operator-run script for the demo tenant). `docs/guides/SPAARKE-SELF-SERVICE-USER-REGISTRATION.md` lines 113, 475, 500, 556 said the sender is hard-coded; updated to describe `Communication:DefaultMailbox`.
  - No other sender in code was changed (scope: the R3 grant / registration path only).

### §11 — new surface

| Surface | 1. Existing | 2. Extension | 3. Cost of doing nothing |
|---|---|---|---|
| `CommunicationOptionsValidator` | `PublicConfigOptionsValidator` (the env-aware idiom) and `Communication:DefaultMailbox` | New validator for a member of an existing options class; no new key, no new options class | Without it an unconfigured stamp starts cleanly and the first grant email throws; the owner asked for fail fast |
| `IOptions<CommunicationOptions>` on `RegistrationEmailService` / `StartupValidationService` | Both classes already take options | Adds a constructor parameter | The sender stays a hard-coded literal |
| Template value + comment | `appsettings.template.json` `Communication` section | Edits an existing key | The central mailbox would not be the Spaarke default |

### Fail-before evidence

With `ResolveFromMailbox` returning the old literal: **2 tests fail** (`TheConfiguredMailbox_IsTheSender_NotTheOldLiteral`, `TheConfiguredMailbox_ReachesTheRequestTheNotificationSends`). Restored: all pass. The validator tests (empty / whitespace fails in Production, Staging and Demo; Development / Testing succeed; a configured value passes; reading `IOptions<CommunicationOptions>.Value` in Production with no mailbox throws `OptionsValidationException` as host start does) and the template pin are new and pass. Existing doubles (`RecordingEmails` in `GrantAccessNotifierExternalTests`, `RecordingEmailService` in `AssignedAccessTestDoubles`) were moved to the new constructor, and `PortalNotificationRequestTests` now asserts the configured sender.

### Operator steps (not code)

1. The mailbox `mailbox-central@spaarke.com` must exist in the Spaarke tenant.
2. The stamp identity that sends needs Exchange RBAC for Applications scoped to that mailbox (H14a). Until then a send fails at Graph, not at start.
3. Spaarke's own platform BFF must set `Communication__DefaultMailbox` (the template value now supplies it where the template is used). Customer stamps get their own value from the intake.
4. The mailbox must be listed in `Communication:ApprovedSenders`; the template keeps the two equal, an override of one must override both.

---

## Build, tests, publish size

- **Build:** `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` — 0 warnings, 0 errors.
- **Filtered unit run** (`ExternalAccess|Communication|RegistrationEmail|GrantAccess|SystemSenderMailbox|PortalNotification|OptionsValidation|Registration`): **3790 passed, 0 failed, 8 skipped** (the skips are pre-existing `CommunicationIntegrationTests` / inbound pipeline cases). A further filtered run over `GrantExternalContactEmail|GrantAccessNotification|AssignedAccess|StartupValidation|Onboarding`: 269 passed, 0 failed.
- **`tests/Spaarke.ArchTests/`:** 940 passed, 0 failed (after the ledger reason fix `f3d328739`; it was 1 failed before it).
- **Fail-before, all three parts in one perturbed run** (filtered to the new / pinned test classes): 24 failed of 117 (17 + 5 + 2); restored, all pass.
- **Publish size** (`dotnet publish -c Release`, zipped, PDBs included, from `git archive` exports): `origin/master` (`9695a4ca7`) 38,232,179 bytes; R3 base `980e850f1` 38,255,664; this change (`HEAD`) 38,261,121. **This change: +5,457 bytes (+5.3 KB).** The whole R3 branch vs `master`: +28,942 bytes (+28.3 KB). Total 36.5 MiB, well under the 55 MB review line.
- **CVE:** no `*.csproj` or `Directory.Packages.props` change vs `origin/master` or vs `980e850f1`, so no new package and no new HIGH CVE.

## Files

Source:
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalProjectDataEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationThreadReadService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationEmailService.cs`
- `src/server/api/Sprk.Bff.Api/Configuration/CommunicationOptionsValidator.cs` (new)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CommunicationModule.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/Startup/StartupValidationService.cs`
- `src/server/api/Sprk.Bff.Api/appsettings.template.json`

Tests:
- `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalDeleteOwnTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalDeleteServiceRequestTests.cs` (new)
- `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalMessageSendTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalMessageEndpointsTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/GrantAccessNotifierExternalTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/PortalNotificationRequestTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/OptionsValidation/SystemSenderMailboxConfigurationTests.cs` (new)
- `tests/integration/auth/UnifiedAccessControl/AssignedAccessTestDoubles.cs`

Docs: `docs/guides/SPAARKE-SELF-SERVICE-USER-REGISTRATION.md`.

## Noticed, not changed

- `docs/guides/AZURE-SETUP-SELF-SERVICE-REGISTRATION.md:503` still shows the old mailbox in an Exchange policy example (operator script).
- The commit trailer on `00d9dd275` names Opus 5.5 (per the brief); later commits name Sonnet 5.5 (per the harness attribution instruction).
