# Task 065 notes: the internal-only flag and sender on sent communications

Owner check 3, item 5 (decision 2), and the owner's escalation answers of 2026-10-13. Rigor FULL, tier opus/high.
Branch base 15f2dc686. No live commands were run and nothing was deployed.

## Change (BFF)

`src/server/api/Sprk.Bff.Api/Services/Communication/Models/SendCommunicationRequest.cs`: new optional `IsInternalOnly` (`bool?`).

`src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs`: all three record builders now write
`sprk_isinternalonly` explicitly for every communication type.

| Builder | `sprk_isinternalonly` | `sprk_sentby` |
|---|---|---|
| `BuildMessageDataverseRecordAsync` (Message, `SendMessageAsync`) | the request value; with none, `true` when `ThreadId` names a thread whose `sprk_privacystate` is not explicitly Open (Private, missing, or unreadable), else `false` | the sending systemuser that `ResolveMessageSenderAsync` already resolved (new) |
| `BuildDataverseRecordAsync` (shared mailbox) | the request value, else `false` | the calling user, from the caller's oid (new) |
| `BuildDataverseRecordForUserAsync` ("My Mailbox") | the request value, else `false` | unchanged behaviour; the inline oid lookup moved into the shared `StampSentByFromObjectIdAsync` |

- The privacy check is `TargetThreadIsNotConfirmedOpenAsync`, which applies the same fail-closed rule as
  `CommunicationFanOutTargetingService.IsExplicitlyOpen`. It reads the thread before the send and never fails the send.
- With no `ThreadId`, the privacy is known without a read. `MessagingChannelSender` creates a new ACS thread for every
  such send (`MessagingChannelSender.cs:72-84`), so `MessagingThreadKeyStrategy` returns CreateNew, and
  `ThreadResolver.CreateThreadAsync` creates the thread Open (`ThreadResolver.cs:183`). The flag is therefore `false`.
- The sender lookup reuses the existing oid to systemuserid lookup and its two warning messages, word for word. An
  unresolvable oid means no stamp and a warning; the email is still sent and its record is still created.
- `CommunicationAccessFilter` is not changed: a null flag still hides the row from external callers. Old rows are not
  corrected (owner decision).

## Change (web resource)

`src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_communication_send`: `_buildRequest` sends `isInternalOnly`
when the form carries a `sprk_isinternalonly` value. It is checked with `node --check` and not deployed.

**Not changed: the 100000004 → "Message" mapping.** The POML escalation trigger fired; see "Message path" below.

## Tests

`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationServiceMessageSendTests.cs` (7 new):

| Test | Proves |
|---|---|
| `..._WithoutIsInternalOnly_PersistsMessageVisibleToExternalContact` | flag `false`; the real `CommunicationAccessFilter` shows it to a contact caller |
| `..._StampsSentByWithTheSendingSystemUser` | `sprk_sentby` = the sending systemuser |
| `..._WithIsInternalOnlyTrue_PersistsMessageHiddenFromExternalContact` | flag `true`; the real filter hides it from a contact |
| `..._IntoPrivateThreadWithoutIsInternalOnly_PersistsInternalOnly` | Private thread, no value → `true` |
| `..._IntoOpenThreadWithoutIsInternalOnly_PersistsNotInternalOnly` | control: Open thread → `false` |
| `..._IntoPrivateThreadWithIsInternalOnlyFalse_PersistsTheSendersChoice` | the request value wins over the thread default |
| `..._IntoThreadWhosePrivacyIsUnreadable_PersistsInternalOnly` | fail-closed branch; beyond the stated criteria, justified because the new branch would otherwise be untested |

`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/DataverseRecordCreationTests.cs` (6 new): shared mailbox and user
mode with the flag absent (`false`) and `true` (`true`); shared-mailbox `sprk_sentby` = the caller; an unresolvable
caller oid creates the record without `sprk_sentby` and logs the warning.

Before the change: 13 failed, 34 passed (the two files). After: 47 passed. A wider filtered run
(`Communication|SecureChildOwnership|UnifiedAccessControl|ExternalAccess` in `Sprk.Bff.Api.Tests`) gave 4652 passed,
2 skipped and 0 failed. `Spaarke.ArchTests` (`RecordOwner|Census`) gave 57 passed. The BFF builds with 0 warnings and 0 errors.

Criterion 3 (an unresolvable sender oid still sends) applies to the two email builders. A Message cannot be sent
without a systemuser, because ACS needs a sending participant: `ResolveMessageSenderAsync` returns 400
`SENDER_NOT_RESOLVED` (`CommunicationService.cs:829-866`), which is existing behaviour and is unchanged.

## Seeding proof

I removed `["sprk_isinternalonly"] = isInternalOnly,` from `BuildMessageDataverseRecordAsync` and ran
`SendAsync_ForMessageType_WithoutIsInternalOnly_PersistsMessageVisibleToExternalContact`:

```
Failed ...SendAsync_ForMessageType_WithoutIsInternalOnly_PersistsMessageVisibleToExternalContact
  Expected persisted!.GetAttributeValue<bool?>("sprk_isinternalonly") to be false, but found <null>.
Failed!  - Failed: 1, Passed: 0
```

The file was then restored. The restored copy kept its old timestamp, so it had to be touched to force a rebuild. The
re-run passed 72 of 72. A null flag is the value `CommunicationAccessFilter` hides from external callers
(`CommunicationAccessFilter.cs:73-86`).

## Message path from the model-driven form (escalation trigger: STOP before changing the mapping)

### What the Message path requires, with file:line

| Need | Where | Form has it? |
|---|---|---|
| `communicationType` = Message, which routes to `SendMessageAsync` | `CommunicationService.cs:1320-1323` | only after the mapping change |
| A non-empty body | `CommunicationService.cs:650-658` | yes (the form also requires To and Subject: `sprk_communication_send:369-377`) |
| An authenticated HttpContext | `CommunicationService.cs:662-670`; the endpoint always passes it (`CommunicationEndpoints.cs:498`) | yes (MSAL bearer) |
| Sender identity: an oid claim that resolves to a Dataverse systemuser, else 400 | `CommunicationService.cs:818-874` | yes, if the user exists in Dataverse |
| ACS thread | none needed: `AcsThreadId = null` (`CommunicationService.cs:700`); the sender creates a new ACS thread with only the sender as member (`MessagingChannelSender.cs:72-84`) | not needed |
| Recipients or participants | none: To/Cc only fill `sprk_to`/`sprk_cc` and the participant index (`CommunicationService.cs:772`); they are **not** added to the ACS thread | not needed |
| Thread record | without `ThreadId`, a new `sprk_communicationthread` per send (`MessagingThreadKeyStrategy.cs:53-54`, `ThreadResolver.cs:170-204`) | the form has no thread |
| ACS identity and token for the sender | `MessagingChannelSender.cs:66-67` → `IAcsIdentityService` | server-side |
| **`Communication:Acs:Endpoint`** (`AcsOptions.cs:20,26`) | thrown if empty: `AcsServiceCollectionExtensions.cs:36-42` (identity client), `:81-87` (chat client), `MessagingChannelSender.cs:155-161` (transmit) | **config gap, see below** |
| The BFF identity's RBAC on the ACS resource | the identity client uses the central `TokenCredential` (`AcsServiceCollectionExtensions.cs:44-45`) | dev had none on 2026-07-21 (`projects/messaging-communication-app-r3/notes/task-060-notes.md`) |

`AcsOptions.DefaultTokenValidityHours` (`AcsOptions.cs:33`) defaults to 24 and needs no setting.

### Why the trigger fired

`Communication:Acs:Endpoint` is in neither `appsettings.template.json` (its `Communication` section is at lines
399-434 and has no `Acs` key) nor `appsettings.Development.json.template`. There is no committed `appsettings.json`, and
`config/spaarke-resources.yaml` has no ACS entry either. The only record of it is a live dev App Service setting
(`Communication__Acs__Endpoint=https://spaarke-acs-dev.unitedstates.communication.azure.com`, task-060 notes, July). On
that date the BFF identity had no ACS role, and sends returned 401 "Denied by the resource provider". Whether that has
since been fixed needs a live check.

### Further finding: the web resource may not be the form's send path

- No ribbon command or form library in the committed solution calls `Sprk.Communication.Send.sendCommunication`.
  `Entities/sprk_Communication/RibbonDiff.xml` has no reference to `sprk_communication_send`, and a search of
  `src/dataverse` finds it only in the web resource itself and in `Solution.xml:380`.
  `CommunicationActions/index.ts:8` says that PCF "replaces the ribbon `sprk_communication_send.js` (W4 pivot)".
- The **Message main form** (`FormXml/main/{b58ec3d8-...}.xml`) hosts `CommunicationMessageActions` (lines 499-511). That
  PCF already sends `communicationType: 'message'` with the record's `threadId` (`CommunicationMessageActionsApp.tsx:221-231`).
  So the model-driven Message form already takes `SendMessageAsync`, and after this change those sends are written
  `isinternalonly = false` (Open thread) or `true` (Private thread).
- That PCF sends no `isInternalOnly`. The form's `sprk_isinternalonly` (form line 154) belongs to the message being
  viewed, not to the reply being composed, so "the form's value" is ambiguous for this path. Carrying a sender's
  "Internal only = Yes" from the model-driven app needs a toggle in `TimelineComposeBox`/`sendCommunication`. That is a
  design decision for the owner and is not in this task's files.
- The live environment's ribbon may differ from source; this was not checked live.

## Fix the class (step 9.5 rule 6)

Other server builders of `sprk_communication` that write no `sprk_isinternalonly`, so external callers never see their
rows. They are outside this task's three builders and are listed for the operator only:
- `Services/Communication/Channels/MessagingIngestor.cs:200` (inbound ACS messages, including the echo backstop)
- `Services/Communication/EmailUploadCaptureService.cs:357` (Office add-in or upload capture)
- `Services/Communication/IncomingCommunicationProcessor.cs:747` (inbound Graph email)

Guard that would catch it: an ArchTest that requires every `new Entity("sprk_communication")` in `Services/Communication`
to set `sprk_isinternalonly`. Not built (not asked for by the POML).

## Gates

- BFF hygiene (CLAUDE.md section 10): no new package, endpoint, service or DI registration. Two new private helpers
  inside `CommunicationService` (one is the extracted existing oid lookup), and one new DTO property, which is optional
  and additive. Publish-size delta: not measured; no fresh master build was run in this worktree.
- Contract: `isInternalOnly` is optional; existing clients are unchanged and get `false`.
- The adversarial pass is left to the main session (brief).

## Fix round 1 (adversarial pass)

Fail-closed decisions from the main session. Nothing else changed.

| # | Finding | Status | Where |
|---|---------|--------|-------|
| F1 | Reply/forward quotes the parent but wrote `isInternalOnly ?? false` | Fixed | `ResolveOutboundOwnerAsync` raises the flag to true after `CopyRegardingFromSourceAsync` (which now also reads `sprk_isinternalonly` and returns "source explicitly false"), when `request.IsInternalOnly` is null |
| K2 | Email into a Private thread was written false | Fixed | New `ResolveRequestedInternalOnlyAsync` (explicit value, else `TargetThreadIsNotConfirmedOpenAsync`) used by all three builders |
| K3 | Unreadable-thread branch untested | Fixed | Tests where the thread read throws or returns a null row, for email (both modes) and Message |
| O1 | Messages carry no regarding | Open (owner) | Unchanged |
| O2 | Every filed email is visible to external callers; no client opt-out | Open (owner) | Unchanged |
| O3 | Shared-mailbox sends show the individual sender | Open (owner) | Unchanged |

Rule: with no explicit `IsInternalOnly` and an `InheritRegardingFromCommunicationId`, the row is true unless the source
flag is explicitly false. A true or null flag, a null source row or a failed source read gives true. An explicit request
value always wins. It is raise-only, so the private-thread result is never lowered by an Open parent. The flag rides on the
existing source read (no second read).

Message sends: yes, they can carry `inheritRegardingFromCommunicationId`. The Message builder calls the same
`ResolveOutboundOwnerAsync` with the same `SendCommunicationRequest`, so the rule covers it. A Message test was added.

Seeding proof (unit tests, filtered to `DataverseRecordCreationTests` + `CommunicationServiceMessageSendTests`):
- Inherit rule disabled (`if (false && ...)`): 10 failed / 76 passed, including `source true -> true`, `source null -> true`,
  the null-row and failed-read cases, and the Message reply cases. Restored.
- Thread check disabled (private-thread check never evaluated): 13 failed / 73 passed, including the email Private-thread,
  unreadable-thread (throws, null row) and Message cases. Restored.
- Restored: 86 passed in those two classes; `Sprk.Bff.Api.Tests.Services.Communication` + `CommunicationAccessFilter` +
  `CommunicationRecordAuthorization`: 1221 passed, 0 failed. `dotnet build Sprk.Bff.Api -warnaserror`: 0 warnings, 0 errors.

## Fix round 2 (owner decisions 2026-10-14)

Merged `work/spaarke-SPA-external-access-platform-r3` into the branch first (merge, no conflicts; BFF builds).

Owner decisions:
- **D1 YES.** A Message posted into a record's thread is linked to that record. The Console and model-driven send
  (`CommunicationMessageActionsApp.tsx`) carries `communicationType: 'message'` + `threadId` and no association, and the
  external read (`ReadRegardingAppOnlyAsync`) filters on the typed lookup `_{regarding}_value eq id`, so it never returned
  those messages. Closes O1.
- **D2.** All filed emails are partner-visible by default for now; the builders' explicit `false` default stays. A
  per-message "Internal only" toggle is a separate client task. Closes O2 for now.
- **D3.** The individual sender's name on shared-mailbox emails is OK. Closes O3 (K7).
- **D4.** No dev data correction; old rows stay as they are.

### Change (`CommunicationService.cs`, `SendCommunicationRequest.cs`)

One read of the target thread (`ReadTargetThreadAsync`: `sprk_privacystate`, the thread's typed regarding columns from
`ThreadResolver.ThreadRegardingColumns`, and its denormalized name/url) now feeds both rules, run from
`ResolveOutboundOwnerAsync` (`ApplyTargetThreadAsync`), which all three builders already call. This replaces fix round 1's
`ResolveRequestedInternalOnlyAsync` / `TargetThreadIsNotConfirmedOpenAsync` (a privacy-only read inside each builder), so a
send into a thread is still one read.

| Rule | Behaviour |
|---|---|
| Private thread is always internal | The builders write `request.IsInternalOnly ?? false`; `ApplyTargetThreadAsync` then RAISES the flag to `true` when the thread is not explicitly Open (Private, privacy missing, null row, failed read), even when the request says `false`. An explicit Open thread keeps the request value, else `false`. Messages and emails. |
| D1 regarding inheritance | When `ThreadId` is set, the request has no `Associations`, and the row carries no typed regarding yet (a reply's inherited one wins), the row gets the thread's typed regarding lookup via `RegardingFieldMap.FieldFor` (the same columns `MapAssociationFieldsAsync` writes), then the FR-26 core-ancestor stamp, then `sprk_regardingrecordid/name/url`. |
| Own association wins | `Associations` non-empty: nothing inherited; `MapAssociationFieldsAsync` runs as before. |
| Thread has no typed regarding | Nothing written; the send proceeds as before. |
| Thread unreadable | No regarding is written (never guessed) and the flag is `true` (fix round 1 behaviour kept). |

- The core-ancestor stamp was extracted from `MapAssociationFieldsAsync` into `StampCoreAncestorOrThrowAsync` and is shared:
  the inherited regarding gets the same stamp and the same 502 `CORE_ANCESTOR_DERIVATION_FAILED` fail-closed contract as an
  associated email. No new columns, services, DI or endpoints.
- Only the thread's anchor (the first populated typed lookup in ADR-024 priority order, the same one
  `ThreadResolver.ReadTypedRegardingAnchorFromThread` files a message under) is inherited, not every populated lookup. A
  thread carries one anchor, the stamp takes one target, and a multi-lookup thread would give the row two competing parents.
- `sprk_associationcount` is not set for an inherited regarding (no association was supplied).
- The `SendCommunicationRequest.IsInternalOnly` XML doc now states the Private-thread floor; the `ThreadId` doc states the
  inheritance.
- `AssignExplicitThreadAsync` (the post-create thread stamp and owner reparent) is unchanged. It reads the thread again;
  that is the existing path, not this task's read.

### Tests

`CommunicationServiceMessageSendTests.cs`: inherited regarding written and core-ancestor stamped (invoice thread, matter
ancestor via `WithAncestors`); the request's own association wins and nothing is inherited; a thread with no regarding writes
none; an unreadable thread (read throws, request says `false`) writes no regarding and flag `true`; Private thread plus
explicit `false` gives `true` (the round 1 test `..._PersistsTheSendersChoice` was inverted and renamed
`..._PersistsInternalOnly`); Open plus explicit `false` gives `false`. `BuildOutboundSut` gained an optional
`coreAncestors` parameter.
`DataverseRecordCreationTests.cs`: email Private plus explicit `false` gives `true` (both modes; the round 1 test
`..._TheExplicitValueWins` was inverted and renamed); email Open plus explicit `false` gives `false` (both modes).

Extra tests beyond the list: none. The core-ancestor assertion is inside the inherited-regarding test (the brief requires
the stamp to run). The unreadable-thread test uses an explicit `false`, so it also proves the flag floor on that branch.

Filtered run `FullyQualifiedName~Communication` in `Sprk.Bff.Api.Tests`: 1591 passed, 2 skipped (pre-existing live-integration
skips), 0 failed. `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors. Nothing else measured.

### Seeding proof (rule 2 only)

Dropped the override (`if (false && !thread.ConfirmedOpen)` in `ApplyTargetThreadAsync`) and re-ran the filtered
Communication run: 17 failed / 1574 passed. The named tests fail, including
`SendAsync_ForMessageType_IntoPrivateThreadWithIsInternalOnlyFalse_PersistsInternalOnly` and
`SendAsync_EmailIntoPrivateThreadWithExplicitFalse_WritesInternalOnly` (both modes), plus the fail-closed and privacy-default
tests that ride on the same line. Restored (file touched to force a rebuild): 1591 passed, 0 failed.

### Not done / for the operator

- The per-message "Internal only" toggle in the client is a separate task (D2). Until it exists, a client that sends
  `isInternalOnly: false` into a Private thread now gets `true`; that is the intended floor.
- The inbound builders listed under "Fix the class" (`MessagingIngestor`, `EmailUploadCaptureService`,
  `IncomingCommunicationProcessor`) still write no `sprk_isinternalonly`. Unchanged and still outside this task.
- Publish-size delta not measured (no new package, endpoint, service or DI registration).
