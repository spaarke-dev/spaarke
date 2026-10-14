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

## Fix round 3 (review pass 2 + owner decision 2026-10-14 on received communications)

Worktree `agent-a0fca4a29271bc1f5`, branch `worktree-agent-a0fca4a29271bc1f5`, base `3ea046e1d`. No live Dataverse or Azure calls.

| # | Finding | Status | Where |
|---|---------|--------|-------|
| F2 | The Private-thread floor covered an explicit `ThreadId` only; an email reply with `InReplyToMessageId` and no `ThreadId` joins its parent's thread afterwards and that thread may be Private | Fixed | `CommunicationService.ResolveOutboundOwnerAsync` -> new `ApplyReplyParentThreadFloorAsync` |
| F3 | D1 wrote the thread's regarding without the AppendTo check the route promises | Fixed | `CommunicationRecordAuthorizationFilter.AuthorizeSendBodyAsync` (3b), `CommunicationThreadReadService.ReadVisibleThreadRegardingAsync` |
| K3 | Thread-read mocks used `It.IsAny<string[]>` | Fixed | `CommunicationServiceMessageSendTests`, `DataverseRecordCreationTests` |
| Owner | Received communications follow the same rule | Done | `CommunicationInternalOnlyFloor` + the three inbound writers |
| K2 | A Message into an invoice/document/memo-anchored thread can return 502 `CORE_ANCESTOR_DERIVATION_FAILED` on a transient fault | Accepted, test added | `SendAsync_ForMessageType_IntoRecordThreadWhoseAncestorCannotBeDerived_Returns502AndSendsNothing` |
| K4 | Legacy mixed pointer name | Recorded only (cosmetic) | - |
| K4 | Bulk send can't set the flag | Recorded only (client task 068 covers it) | - |

### F2: the in-reply-to join

`ResolveOutboundOwnerAsync` now has, beside the explicit-`ThreadId` branch, `else if (CommunicationType.Email && InReplyToMessageId set)`.
`ApplyReplyParentThreadFloorAsync` resolves the ancestor exactly as `EmailThreadKeyStrategy` will
(`GetCommunicationByInternetMessageIdAsync ?? GetCommunicationByGraphMessageIdAsync`, then the parent's
`sprk_communicationthread` lookup) and runs the existing `ApplyTargetThreadAsync` privacy floor on that thread with the new
`inheritRegarding: false`, so the floor is taken and the regarding is NOT (D1 covers an explicit `ThreadId` only).

- A failed ancestor lookup, parent read or thread read raises the flag (fail closed). No ancestor, or an ancestor with no thread,
  means the key strategy creates a new Open thread, so nothing is raised.
- Gated on `CommunicationType.Email`: for a Message, `InReplyToMessageId` is a communication id (task 062), not an Internet-Message-Id,
  and the Message thread strategy keys on the ACS thread, never on this header.
- Only `request.InReplyToMessageId` is walked. The outbound envelope carries no `References` (`ResolveOutboundThreadAsync`), so this is
  the same candidate list the key strategy sees.
- It runs even when no `IThreadResolver` is wired (no join can happen then). Production always wires one; the extra lookups are the
  price of not coupling the floor to the resolver, and the result is only ever a raise.

### F3: AppendTo on the thread's record

`AuthorizeSendBodyAsync` step (3b): when `threadId` is set and `associations` is empty (the same condition `ApplyTargetThreadAsync`
inherits under, `Associations is { Length: > 0 }`), the thread's typed regarding is read impersonated through the new
`CommunicationThreadReadService.ReadVisibleThreadRegardingAsync` (one projection of the key and the `_sprk_regarding*_value` columns, with
`ThreadRegardingFields` = `RegardingFieldMap.All` minus the report card, which the thread table lacks) and added to `targets` for the existing AppendTo check.

- The anchor is the first populated lookup in `RegardingFieldMap.All` order, via the service's existing `ResolveRegardingFromRow`. That is the
  order `ReadTargetThreadAsync` writes in, so the record checked is the record written.
- A read that throws, or a thread that is not visible, is the filter's existing deny: `Forbidden(reasonCode)`, 403 with the send reason code. A
  thread with no regarding adds no target. An entity type with no entity set is denied, never guessed (as the inherit-from branch does).
- Conservative edge: with both `threadId` and `inheritRegardingFromCommunicationId`, where the source has a regarding, the send writes the
  source's and not the thread's, but the filter still asks AppendTo on the thread's record. It over-asks and never under-asks; the Console
  and model-driven sends carry a threadId and no inherit-from (CommunicationMessageActionsApp.tsx:221-231), so they do not hit this.
- The existing thread-visibility check (2) is unchanged and still runs first; this is a second impersonated read of the same row. A single
  merged read was rejected so that the existing deny shape for an invisible thread stays byte-identical.

### Received communications (owner decision 2026-10-14)

> "received emails hidden from partners - yes but if an email is related to a record that is shared with a partner then the partner
> should see it; the 'internal only' will hide communications so partner does not see them (this applies equally to workforce contact users)."

The three inbound writers now write `sprk_isinternalonly` explicitly. Each creates its row `true` (its thread is unknown at create, so a row
is never `false` while its thread is unknown) and lowers it to `false` through the new shared helper
`Services/Communication/CommunicationInternalOnlyFloor.cs` only when:

1. the thread was resolved, AND
2. the thread reads explicitly Open (`sprk_privacystate`; Private, a missing value, a null row and a read fault all keep `true`), AND
3. (mailbox email only) the parent it replies to is explicitly shareable.

The outbound `ReadTargetThreadAsync` now takes its Open decision from the same `CommunicationInternalOnlyFloor.IsConfirmedOpen`, so there
is one definition. Visibility to partners then comes from record sharing (the external read filters by regarding and record access) plus this
flag. A failed lowering update is logged and leaves the row `true`; nothing here throws or fails the persist.

| Writer | Create | Lowered where | Parent (b) |
|---|---|---|---|
| `Channels/MessagingIngestor.cs` | `PersistAsync`, `true` | after thread resolution in `IngestAsync`, from `assignment.ThreadId` | **Not reachable.** `AcsEventNormalizer` always sets `InReplyTo = null` (`Engine/AcsEventNormalizer.cs:54`) and there is no chat reply-parent lookup. A non-blank `InReplyTo` (never today) is treated as an unresolvable parent and keeps `true`. No lookup was invented. |
| `EmailUploadCaptureService.cs` | `BuildCommunicationEntity`, `true` | after `ResolveThreadAsync` (now returns the thread id) in `CaptureWithOutcomeAsync` | **Not reachable.** The upload envelope carries no In-Reply-To or References (see `ResolveThreadAsync`), so it can neither name a parent nor join a parent's thread; it starts a new Open thread. Only the thread rule applies. A reconciled duplicate is not touched (the canonical row keeps what its first writer gave it). |
| `IncomingCommunicationProcessor.cs` | `CreateCommunicationRecordAsync`, `true` | new `ResolveThreadAndShareabilityAsync` (extracted from step 4.6 so it is testable; step 4.6b) | Walked the way `EmailThreadKeyStrategy` walks it (In-Reply-To, then References newest to oldest; Internet-Message-Id, else Graph id; nearest existing ancestor wins), then one read of the parent's flag. Raise-only, the outbound rule: a parent whose flag is `true`, unset, unreadable, or whose lookup faults gives `true`. No ancestor found means no parent. |

`EmailUploadCaptureService` gained an optional `IGenericEntityService? genericEntityService = null` constructor parameter (the host's DI
supplies it; the existing three test constructions are unchanged and simply skip the lowering, staying `true`). No new service, endpoint,
DI registration or package.

Consequences to know:
- A reply to a legacy parent with no flag (every inbound row before this change, every row the old outbound code wrote) is `true`:
  not explicitly shareable. This is the outbound reply rule applied as asked; it fails closed on old chains.
- Deployments with no `IThreadResolver` leave every received item `true`. Production always wires one.
- Cost on the inbound path: one thread read plus one update per received item that resolves a thread; a mailbox reply adds the ancestor lookup and one parent read.

**Read side not changed.** Workforce contacts and CIAM contacts are both non-systemuser callers, and `FilterExternalMessagesAsync`
(`CommunicationThreadReadService.cs:549-556`) already hides `true`/`null` from both, so one flag serves both.

### Tests

- `DataverseRecordCreationTests.cs`: `SendAsync_EmailReplyJoiningPrivateThreadViaInReplyTo_WritesInternalOnly` (both modes, explicit
  `false` and unset), the Graph-id variant, Open control, no-privacy-value, the three-way throwing-lookup variant (ancestor lookup,
  parent thread read, joined thread read), no-ancestor and ancestor-without-thread controls, no regarding inherited on this path, and the
  explicit-`ThreadId` path not consulting the ancestor.
- `CommunicationRecordAuthorizationContractTests.cs` (the filter's test class; the host's `FakeImpersonatedQuery` gained
  `ThrowWhenQueryContains` to fault one read of a set): deny without AppendTo on the thread's record; allow with it; deny when the thread
  read fails; a thread with no regarding adds no target; the priority-order anchor is the one checked; the request's own association
  means the thread's record is not checked.
- `InboundInternalOnlyFlagTests.cs` (new; all three writers): the create is `true`; an Open thread lowers it; Private, no privacy value,
  null row, read fault, no resolved thread and no resolver keep it `true`; mailbox parent `false` lowers, parent `true`, unset, null row
  and lookup fault keep it; a lowering fault never fails the persist.
- `CommunicationServiceMessageSendTests.cs`: `SendAsync_ForMessageType_IntoRecordThreadWhoseAncestorCannotBeDerived_Returns502AndSendsNothing`
  (`CoreAncestorResolverFixtures.Failing`; 502 `CORE_ANCESTOR_DERIVATION_FAILED`, nothing sent, no row).
- K3: the thread-read mocks in `CommunicationServiceMessageSendTests` (`ThreadWithPrivacy`, `ThreadAnchoredToInvoice`) and
  `DataverseRecordCreationTests` (`ThreadPrivacyReturns`) now use `It.Is<string[]>(c => c.Contains("sprk_privacystate") && c.Contains(<regarding column>))`
  (`sprk_regardingmatter`, and `sprk_regardinginvoice` where the invoice lookup is the one relied on). The brief's line numbers
  (`:61-64`, `:201-203`, `:468-470`) no longer matched; the live thread-read setups were pinned. The throwing and null-row setups were left
  `It.IsAny` (they never return columns).

Filtered run in `Sprk.Bff.Api.Tests` (`Communication|SecureChildOwnership|UnifiedAccessControl|ExternalAccess|Office|Messaging`, which includes the filter's contract class, the three writers' classes and the Office email-save tests): 5378 passed, 9 skipped (pre-existing), 0 failed. `Spaarke.ArchTests` (`RecordOwner|Census|RouteAuthorization`): 146 passed. `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors; the unit test project also builds with `-warnaserror`.

### Seeding proof

- **F3 guard** (the one asked for): commented out `targets.Add((set, threadRecordId))` in the filter. `CommunicationRecordAuthorizationContractTests`:
  2 failed / 170 passed, including `Send_AMessageIntoARecordThreadWithoutAppendToOnItsRecord_Is403_AndNothingIsSent` and
  `Send_IntoAThreadWithTwoRegardingLookups_ChecksTheOneTheSendFilesUnder_ThePriorityOrderAnchor`. Restored (file touched to force a rebuild): 172 passed.
- F2 (extra): disabled the new `else if` branch in `ResolveOutboundOwnerAsync`: 9 failed / 69 passed in `DataverseRecordCreationTests`
  (the Private, no-privacy-value, Graph-id and three throwing-lookup tests). Restored.
- Inbound floor (extra): `IsConfirmedOpen` forced to `true`: 25 failed / 117 passed across `InboundInternalOnlyFlagTests`,
  `DataverseRecordCreationTests` and `CommunicationServiceMessageSendTests`. The parent rule forced to "shareable": 3 failed in
  `InboundInternalOnlyFlagTests` (parent `true`, unset, null row). Restored.

### Not done / for the operator

- K4 legacy mixed pointer name: cosmetic, no change.
- K4 bulk send cannot set the flag: client task 068 covers it.
- The per-message "Internal only" toggle in the client is still the separate task (D2).
- The guard suggested under "Fix the class" (an ArchTest requiring every `new Entity("sprk_communication")` in `Services/Communication` to set
  `sprk_isinternalonly`) is still not built. With the inbound writers fixed, it would now have no known violations to baseline.
- Publish-size delta not measured (no new package, endpoint, service or DI registration).

## Fix round 4 (re-check findings)

Step 0: merged `work/spaarke-SPA-external-access-platform-r3` (master + UAC task 185, #1827) into the worktree. Two content conflicts, both
"keep both sides": `CommunicationService.BuildDataverseRecordAsync` (`sprk_body` now `request.SystemMail?.StoredBody ?? request.Body`, task 185, next to
`sprk_isinternalonly = RequestedInternalOnly(request)`) and `SendCommunicationRequest` (`IsInternalOnly` and the internal `SystemMail` property).
Build with `-warnaserror` clean after the merge.

### Changes

- **F1 part 1 - upload capture computes the parent rule.** `EmailMetadata` gains optional `InReplyTo` and `References` (`Models/Office/SaveRequest.cs:191,198`;
  additive, the current add-in sends neither, and the Office add-in client is untouched). `BuildEnvelope` maps them
  (`EmailUploadCaptureService.cs:350-351`; `References` is split on whitespace, blank is absent). When the envelope carries either,
  `CaptureWithOutcomeAsync` (`:238-243`) uses the mailbox path's `HasParentNotExplicitlyShareableAsync`; it now runs only when a thread resolved.
  Because the envelope now carries the headers, the email key strategy can also join the parent's thread for such a save (the Private-thread floor covers that).
- **F1 part 2 - the owner-pending no-headers decision is ONE line:** `CommunicationInternalOnlyFloor.AddInSaveWithoutReplyHeadersIsShareable = true`
  (`CommunicationInternalOnlyFloor.cs:39`). `true` is today's behaviour (a header-less add-in save is lowered when its thread is Open); `false` keeps every
  header-less add-in save internal-only. It lives in the floor (not as a private const in the capture service) because the filing sweep (K1) needs the
  same answer, so a single line flips both.
- **F3** - the "no entity service" case now also verifies the other Dataverse service (`dv`) received no `sprk_isinternalonly` update, and that the capture
  itself completed (`InboundInternalOnlyFlagTests`, `EmailUpload_CaptureWithoutAThreadOrTheServiceToCheckIt_StaysInternalOnly`).
- **K2** - `MessagingIngestor.cs:~147-163` catches `OperationCanceledException` around `LowerWhenSafeAsync` the way the neighbouring post-create steps do: the
  row stays `true`, a warning is logged, participants and enrichment still run, and no retry creates a second row. The helper docs
  (`CommunicationInternalOnlyFloor.cs:20-22,122-123`) now say cancellation propagates (faults never escape).
- **K1 (sweep)** - `ThreadResolver.ResolveMissingThreadAsync` (`:1083-1100`) lowers an Incoming row through the shared `LowerWhenSafeAsync` after it gives
  the row a thread (the reconcile now also reads `sprk_direction` and `sprk_graphmessageid`). Only an explicit Incoming direction is lowered.
- **K3 tests** - mailbox parent rule walking `References` (In-Reply-To absent, newest unknown, parent older); Graph-id fallback for the parent lookup; In-Reply-To
  set but the parent not in Dataverse; a Message send with `InReplyToMessageId` never looks up the ancestor (nor reads a parent thread); authorization filter
  step 3b with `associations: []` (403 without AppendTo on the thread's record, sent with it).

### Contradictions and decisions the brief did not settle

- **The sweep cannot read the parent.** A received row stores neither In-Reply-To nor References (only an outbound row stamps `sprk_inreplyto`), so "same parent
  rule" cannot be evaluated from the row. The sweep therefore treats a row WITH a Graph message id (a mailbox email whose headers were lost) as "cannot
  evaluate" and keeps it `true` (fail closed), and a row WITHOUT one (an add-in save, header-less by construction) follows the owner-pending constant above.
  If the owner decides a header-less add-in save stays internal-only, the one flip also makes the sweep keep every row `true`.
- **In-Reply-To set but the parent not in Dataverse lowers.** Confirmed as the code's behaviour and the helper's documented rule ("a message with no known
  ancestor has no parent"); the rule is unchanged. The existing "nullrow" case (parent FOUND but its row unreadable) is the one that stays `true`.
- The mailbox call site (`IncomingCommunicationProcessor.ResolveThreadAndShareabilityAsync`) has the same unguarded cancellation as the ingestor had. Not in scope
  (a retry there dedups on the Graph id and returns early, so the row stays `true` and is never lowered). Reported, not changed.

### Known limits (recorded, not fixed)

- **Unconditional lowering.** `LowerWhenSafeAsync` writes `sprk_isinternalonly = false` without re-reading the row's current value, so a form edit that sets the
  flag during the ~1 s window between create and lowering would be overwritten. No automated writer sets the flag after create, and a person editing a row
  that was created a second ago is theoretical. The same applies to the sweep: a person who marks a still-threadless received email Internal only, and then files
  it, has the mark overwritten when the sweep lowers it (add-in row on an Open thread).
- A header-less add-in save, and a sweep of one, is partner-visible by default until the owner decides (constant above).

### Tests

New: 3 `EmailUpload_*` parent/References cases (+ blank-headers and no-headers controls), F3 strengthened, 2 K2 cancellation cases + 1 helper-contract case,
4 sweep tests (`ThreadResolverTests`, 6 cases), 3 mailbox K3 tests (5 cases), 1 Message-send ancestor case, 2 authorization 3b empty-array cases.
Seeding: the old `MessagingIngestor`/`ThreadResolver` made the cancellation and the sweep-lowering tests fail (3); making the capture ignore the reply headers
made the 3 header tests fail; restored. Filtered run
(`Communication|InboundInternalOnly|DataverseRecordCreation|CommunicationServiceMessageSend|CommunicationRecordAuthorization|Office|Messaging|ThreadResolver`):
0 failed / 2278 passed / 9 skipped (the skips are pre-existing integration-gated tests). `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors.

## Fix round 5 (owner decisions D6/D7, 2026-10-14)

### Owner decisions recorded

- **D6.** Outlook add-in saves without reply headers stay visible to partners. `CommunicationInternalOnlyFloor.AddInSaveWithoutReplyHeadersIsShareable = true` is
  final; its comment (and the two call-site comments and the one test comment that said "pending") now state the decision. No behaviour change.
- **D7.** When a thread is made Private, partners stop seeing its EARLIER messages too. A partner only ever sees an item that sits in a thread that is
  **explicitly Open at read time**, on top of the existing `sprk_isinternalonly` rule. This closes ISS-009 (#1859).
- **Licensed users flagged `sprk_isexternal`** stay unable to see Internal Only items. Current behaviour, no change. They are non-internal callers, so the D7 gate
  applies to them as well (see the read list).

### D7 read-side gate

**Rule.** A non-internal caller (a contact, or a systemuser flagged `sprk_isexternal`) sees a `sprk_communication` only if its `sprk_communicationthread` lookup is set
AND that thread's `sprk_privacystate` reads Open now. Hidden: no thread, a thread row the read does not return (missing or unreadable), a thread row with no privacy
value, a Private thread. Internal callers are unchanged (no thread read, no query-shape change).

**How thread privacy is fetched.** One batched read per page of the DISTINCT thread ids of the rows that survived the flag rule: `sprk_communicationthreads`,
`$select=sprk_communicationthreadid,sprk_privacystate&$filter=<id> or <id>...&$top=<n>`, in chunks of 50 ids (the same URL-length bound the feed uses), never one
read per row. No `$expand` (the lookup navigation property name is not in the repo metadata, and a wrong case is a 400). The identity matches the message read:
APP-ONLY for the contact plane (`ReadRegardingAppOnlyAsync`, `ReadRecentForRecordsAppOnlyAsync`), IMPERSONATED as the caller for the flagged-external systemuser reads
(a thread the caller cannot read is absent, so its rows are hidden). The column name and the "explicitly Open" test are the floor's: `CommunicationInternalOnlyFloor.ThreadPrivacyField`
(new constant, replacing the two literals) and `CommunicationInternalOnlyFloor.IsConfirmedOpen`, called on a minimal entity built from the JSON row. New helper:
`Services/Communication/CommunicationOpenThreadGate.cs` (CLAUDE.md section 11: existing = the floor, reused as is; extension = the floor is the write side over
`IGenericEntityService` while the readers hold the two query seams; cost of nothing = ISS-009).
A read FAULT propagates (the request fails, nothing is returned); it never turns into "show". The app-only seam being absent throws (fail closed), like the message read.
Paging and truncation are unchanged: a row the gate hides is filtered after the query exactly like one the flag hid, and never counts toward `Truncated`.

**Every external read of `sprk_communication`, and what was done.**

| # | Read | file:line | Caller | Gate |
|---|---|---|---|---|
| 1 | `FilterExternalMessagesAsync` (shared tail) | `CommunicationThreadReadService.cs:564` (gate at :586) | contact or systemuser, per `IsExternalAsync` | gated for non-internal (app-only thread read); the select now carries `_sprk_communicationthread_value` (`ExternalSelect`) |
| 2 | `ReadRegardingAppOnlyAsync`, the route `GET /api/v1/external/{root}/{id}/messages` (`ExternalMessageEndpoints`) | `CommunicationThreadReadService.cs:439` (calls #1 at :484) | contact / external user | via #1 |
| 3 | `ReadRecentForRecordsAppOnlyAsync`, the route `GET /api/v1/external/notifications` (`ExternalNotificationFeedEndpoints`) | `CommunicationThreadReadService.cs:632` (calls #1 at :700) | contact / external user | via #1; the feed `unreadCount` is derived from exactly these messages, so a hidden item is never counted |
| 4 | `ReadThreadAsync` (thread messages) | `CommunicationThreadReadService.cs:218` (gate at :234) | systemuser, flagged external | gated: thread not Open gives an empty result and a null name (same as "none visible") |
| 5 | `GetUnreadCountAsync` (unread badge) | `CommunicationThreadReadService.cs:290` (gate at :304) | systemuser, flagged external | gated: thread not Open gives count 0 |
| 6 | `QueryVisibleMessagesAsync`, behind `ReadByRegardingAsync` and `QueryCommunicationsAsync` | `CommunicationThreadReadService.cs:1054` (gate at :1093) | systemuser, flagged external | gated, one batched impersonated thread read |
| 7 | `ReadVisibleCommunicationAsync` (per-record route gate) | `CommunicationThreadReadService.cs:1286` (gate at :1322) | systemuser, flagged external | gated; the thread lookup is selected only for an external caller |
| 8 | `CommunicationQueueFeedService.GetQueueFeedAsync` (queue-feed route) | `CommunicationQueueFeedService.cs:133` (gate at :178) | systemuser, flagged external | gated; the thread lookup is selected only for an external caller |

Counts and badges: #5 above, and the feed unread count (#3), which counts only the already-gated list.

**Not gated, with the reason (nothing here lists or counts partner-visible items).**

- `CanCallerSeeMessageAsync`: an impersonated existence probe for the message-deactivate route, documented as access-filter-free; not a read surface.
- `ReadByRegardingAsync` still lists a Private thread (name, pinned flag) with zero messages for a flagged-external caller: the thread rows come from the impersonated
  thread query, not `sprk_communication`, and D7 speaks of items. Hiding the thread rows too is a separate decision for the owner.
- `CommunicationFanOutTargetingService` (who is notified at send time) reads the thread at send time and is the write-side half; it does not list items to a reader.
- Other readers of `sprk_communication` (AI tools, `ChatDocumentEndpoints`, the todo/analysis filters, `RecordContainerResolver`) are internal-only surfaces, outside the external plane.

**Contradictions with D7.** None found. Note: a flagged-external systemuser used to see any flag-false item in a thread they could read; they now also need the thread Open. That is D7 as stated.

### Retry gap (IncomingCommunicationProcessor)

`ResolveThreadAndShareabilityAsync` now catches `OperationCanceledException` around the parent check and the lowering (warning logged, the row stays `true`, processing
completes), the same approach as `MessagingIngestor`. Chosen over running `LowerWhenSafeAsync` in the dedup path because it is one catch and the dedup path stays untouched.
Tests: `Mailbox_WhenLoweringIsCancelled_DoesNotThrow_AndTheRowIsNotLowered`, `Mailbox_WhenTheParentReadIsCancelled_DoesNotThrow_AndTheRowIsNotLowered`.

### Tests

New file `CommunicationOpenThreadGateTests.cs`: open visible; Private hidden; missing privacy value hidden; no thread hidden; unreadable thread hidden; a thread switched to
Private after the send hides the earlier item (D7); the flag still wins in an Open thread; one batched read for a mixed page; chunking; the thread query shape pinned
(`$select=sprk_communicationthreadid,sprk_privacystate...`); the message select carries the thread lookup; a thread-read fault fails the read; the app-only seam absent
refuses; an internal caller sees every case and reads no thread; the feed list; the unread count for a flagged-external user (Open, then Private, zero for Private) and
unchanged for an internal user; `ReadThreadAsync`, `ReadByRegardingAsync` and `ReadVisibleCommunicationAsync` gated for a flagged-external user and unchanged internally.
Plus two queue-feed tests in `CommunicationQueueFeedServiceTests`. The existing external suites (`ExternalMessageEndpointsTests`, `ExternalNotificationFeedTests`,
`ExternalMessageSendTests`) now seed their rows into one Open thread through `AppOnlySelectProjection.WithOpenThread` / `OpenThreadAnswer`, and the feed pinned select
gained the thread column.

### Results and seeding proof

- Filtered run (`Communication|InboundInternalOnly|DataverseRecordCreation|CommunicationServiceMessageSend|CommunicationRecordAuthorization|ExternalAccess|Office|Messaging|ThreadResolver`):
  0 failed / 5181 passed / 9 skipped (the skips are the pre-existing integration-gated tests). `Spaarke.ArchTests`: 0 failed / 983 passed.
  `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors.
- New tests: 21 in `CommunicationOpenThreadGateTests`, 2 queue-feed, 2 retry-gap. The first full run (before the fixtures were updated) failed 4 old external cases that had no thread; fixed as described above.
- **Seeding proof, D7 gate.** With the `FilterExternalMessagesAsync` gate disabled (`if (false && !isInternal ...)`), 10 tests failed (Private thread hidden, no privacy value, no thread, unreadable
  thread, thread switched to Private after the send (D7), mixed page, chunking, query shape, fault, feed). Restored; the same run is 82 / 82 green.
- **Seeding proof, retry gap.** With the new catch made inert (`when (ex.Message == "never")`), both new `Mailbox_*Cancelled*` tests failed; restored, 49 / 49 green in `InboundInternalOnly`.

## Fix round 6 (owner decisions D8/D9, 2026-10-14)

### Owner decisions recorded

- **D8.** If a record's default thread is Private, a partner's portal post goes into a NEW Open default thread on that record (the partner can then read their own post back).
- **D9.** External callers cannot rename, pin or deactivate Private threads.
- **Visibility rule (restated).** "External caller" = a contact on either plane, or a systemuser flagged `sprk_isexternal` (`IsExternalAsync` true; fails closed). An external caller sees a
  communication only if `sprk_isinternalonly` is explicitly false AND its thread exists and is explicitly Open at read time. Licensed-external users are treated like partners for Private threads.

### Changes, by item

All paths under `src/server/api/Sprk.Bff.Api/`.

1. **F1, attachment-text bypass.** `Services/Communication/CommunicationAttachmentTextService.cs:115`: `GetAttachmentTextAsync` runs the id through
   `CommunicationThreadReadService.ReadVisibleCommunicationAsync` right after caller resolution; null returns the existing empty result. New ctor parameter `threadReadService`.
2. **F1, Private thread names in thread lists.** `Services/Communication/CommunicationThreadReadService.cs:362` (`ReadByRegardingAsync`) and `:765` (`ListThreadsAsync`): for an external caller the thread
   `$filter` gains `sprk_privacystate eq <Open>` IN the query (so paging and the keyset token stay right). The clause is `CommunicationOpenThreadGate.OpenOnlyThreadFilterClause`
   (`CommunicationOpenThreadGate.cs:49`), built from `CommunicationInternalOnlyFloor.ThreadPrivacyField` and `ThreadPrivacyState.Open`.
3. **F1, defensive, AI tool.** `Services/Ai/Handlers/EmailDraftToolHandler.cs:800`: `FetchThreadAsync` first calls `ReadVisibleCommunicationAsync`; null (or no principal) gives the tool's normal not-found error.
   It covers `email.summarize_thread`, reply and forward (they share the fetch). ADR-013 check: an AI handler calling a CRUD read service is the allowed direction (no CRUD code takes an AI type), so no STOP.
4. **F2, a partner's post can vanish.**
   - (A) `Services/Communication/CommunicationService.cs:1431`: a null thread id retires the created row (`RetirePostWithoutThreadAsync`, :1451; statecode 1, statuscode Deleted; a fault there is logged, not thrown) and throws
     `SdapProblemException` 503 with code `ThreadUnavailableCode = "EXT_MESSAGES_THREAD_UNAVAILABLE"` (:1444). The existing `catch (SdapProblemException)` in `ExternalMessageEndpoints` already maps it to the external problem shape, so the endpoint file is unchanged.
   - (B, D8) `IThreadResolver.cs`: `ThreadResolutionRequest.RequireOpenDefaultThread`. `CommunicationService.cs:1198/1425` sets it for the external post only. `ThreadResolver.cs:1479` (`FindDefaultThreadAsync`) adds `sprk_privacystate == Open` when asked, so a Private default is skipped and an Open default is found or created.
     The filing reconcile (`ThreadResolver.cs:1011`) passes `requireOpen: false` (unchanged).
   - **Race guard: none exists.** The default-thread find-or-create has no lock or unique key (documented as "rare, benign duplicate"). Two concurrent partner posts onto a record with only a Private default could each create an Open default thread. Not fixed here; reported.
   - Side effect to note: after D8 a Private and an Open default can coexist, so the lookup now orders by `createdon` ascending (oldest first) for determinism. This also applies to internal lookups (previously unordered); it picks the same row whenever there is only one.
5. **D9.** `CommunicationThreadReadService.CanCallerSeeThreadAsync` (:1227, clause at :1231) adds the same Open-only clause for an external caller. Rename, pin and deactivate (`Api/CommunicationEndpoints.cs:893/934/964`, which already call it) therefore return their existing "not found or forbidden" response for a thread that is not explicitly Open. Internal callers unchanged.
   Side effect: the send-route filter `CommunicationRecordAuthorizationFilter.CanSeeThreadAsync` shares this method, so an external caller sending INTO a non-Open thread is now denied too (consistent with the rule).
   The three handlers were made `internal static` (were `private static`) so they can be unit-tested.
6. **F3.** Deleted `CommunicationOpenThreadGateTests.ExternalRead_WithoutTheAppOnlySeam_Refuses`. Added tests for a flagged-external systemuser on the external routes and the feed; they fail if `!isInternal` becomes `callerSystemUserId is null`.
7. **K1.** `Services/Communication/EmailUploadCaptureService.cs:436`: when headers are present the row stores `sprk_inreplyto` (In-Reply-To, else the newest non-blank Reference; the Reference fallback goes slightly beyond "when headers are present"). `ThreadResolver.cs:1101`: the filing sweep treats a non-blank `sprk_inreplyto` on an Incoming row as not shareable (`InReplyToField` :515, selected at :546). Header-less add-in saves stay shareable (D6).
8. **K2.** `EmailUploadCaptureService.cs:250`: `catch (OperationCanceledException)` around the parent check and the lowering; the row stays `true`, a warning is logged, the capture continues so enrichment runs (same approach as `MessagingIngestor`).
9. **Known limit (notes only).** An old Private thread with 500 or more messages can hide older Open-thread messages while "Truncated" still shows: the page cap applies before the visibility filters. The flag rule has the same limit. Accepted, not fixed.

### Tests

New fixture `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/CommunicationVisibilityFixtures.cs` (a real `CommunicationThreadReadService` over doubles). New or changed tests:
`CommunicationAttachmentTextServiceTests` (6: external+internal-only, external+Private, external+no-privacy-value, not readable, internal gets text, external+Open gets text);
`CommunicationOpenThreadGateTests` (thread-list external/paging/internal, by-regarding query pin, D9 rename/pin/deactivate refused and allowed, `CanCallerSeeThread` query shapes, F3 flagged-external on the routes and feed);
`EmailDraftToolHandlerPerItemToolsTests` (7: external denied, internal summary, no principal, ...);
`ExternalMessageSendTests` (null thread gives 503 plus deactivated row; thread step throws; deactivation also fails still 503; resolved thread not deactivated; resolver asked for an Open default);
`ThreadResolverTests` (6 D8: Private default gives a new Open thread read back through `ReadOpenThreadIdsAsync`; Open reused; Private+Open joins Open; no-privacy-value default not joined; internal unchanged; oldest-first; plus 2 K1 sweep);
`InboundInternalOnlyFlagTests` (3 K1 store, 3 K2 cancellation cases). Existing ctor sites of `EmailDraftToolHandler` and the two thread contract-test factories were updated (an identity-resolver mock; without it the real resolver returned 500).

### Results and seeding proof

- Filtered run (`Communication|ExternalMessage|ExternalNotificationFeed|QueueFeed|InboundInternalOnly|EmailUploadCapture|EmailDraftTool|ThreadResolver|CommunicationRecordAuthorization`): 0 failed / 1993 passed / 2 skipped (the two pre-existing `CommunicationIntegrationTests` skips). Extra run of `SecureChildOwnershipAiTool|CommunicationRenameThread|CommunicationSetThreadPinned`: 199 / 199.
  `Spaarke.ArchTests` filtered to `Census|RouteAuthorization|Ai`: 0 failed / 258 passed. `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors.
- **Seeding proof, attachment-text check** (`... is null && false`): 4 failures (internal-only, Private, no privacy value, not readable). Restored.
- **Seeding proof, thread-list filter.** Clause removed from `ListThreadsAsync`: 2 failures; removed from `ReadByRegardingAsync`: 1 failure. Restored.
- **Seeding proof, D9 action check.** Clause removed from `CanCallerSeeThreadAsync`: 7 failures (rename/pin/deactivate Private, three no-privacy-value cases, query shape). Restored.
- Extra mutations (all restored): F3 (`!isInternal` replaced by `callerSystemUserId is null`) 2 failures; AI handler check neutralised 5; F2 503 removed 3; D8 `requireOpen` neutralised 3; K1 sweep 2; K1 store 2; K2 catch inert 3.

### Not done / for the operator

- No race guard on the default-thread find-or-create (see item 4).
- The ArchTests ledger reason text for `attachments/text` (`RouteAuthorizationGuardTests.Ledger.cs`, near line 1463) does not yet mention the new visibility check (text only; tests pass).

## Final re-check of fix round 6 (2026-10-14, opus adversarial-reviewer)

No F-class finding (558 filtered tests passed). Main session fixed three K2 items in the closing commit:

- `CommunicationService` retires a threadless partner post with `CancellationToken.None`. A client abort no longer leaves an active threadless row.
- `EmailDraftToolHandler.FetchThreadAsync` turns the visibility read's 403 (an unresolvable caller) into the same not-found answer. Test `SummarizeThread_UnresolvableCaller_IsDeniedAsNotFound`; seeding proof: with the catch disabled 1 failed, restored 47/47.
- `EmailUploadCaptureService` truncates `sprk_inreplyto` to its 1000-character column.

Known limits:

- **K, filed as ISS-010:** the generic `dataverse.read_query` chat tool (`DataverseReadQueryHandler`), the `DELETE /api/communications/{id}` route, and direct Dataverse or model-driven reads apply Dataverse security only. A licensed user flagged `sprk_isexternal` who has Dataverse read on a record's communications can read internal-only and Private-thread bodies that way. Internal Only is enforced by the BFF's reads, not by Dataverse security. Contacts (CIAM and workforce) have no Dataverse access and are not affected.
- **K4 (D8):** concurrent first partner posts can create two Open default threads. Both stay visible, so nothing is hidden or leaked. Ordering the default-thread lookup by `createdon` does not change which thread internal sends land in.
- **D8 scope:** `RequireOpenDefaultThread` is set only on the external post route. That includes systemuser authors on the external plane, whose posts are written `false`, so an Open thread fits.
