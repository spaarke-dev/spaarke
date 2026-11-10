# Task 054 notes — Direct message owner team

## 054a split (agreed with UAC-r2, 2026-11-09)

- **Piece 1** (the Direct team lookup and its settings): FINAL on `task/fm-054a-2` (ac6f1743b3). UAC-r2 merged it into 198.
- **Piece 2** (no dependency on 198): the `sprk_DirectRecipient` lookup (to systemuser, on `sprk_communication`) and the BFF stamp at create on Direct messages: send, respond, inbound join, new Direct thread. When the inbound join cannot map the chat identity to a systemuser, no recipient is stamped and a stable Error is logged with the message id.
  - Creating the column on an environment is a live schema change, so it needs the owner's go in this project's session.
  - **Column created on spaarkedev1 on 2026-11-09, with the owner's go**, by `scripts/Set-DirectRecipientSchema.ps1 -Apply`; `-Verify` passes.
    - From live metadata: navigation property `sprk_DirectRecipient` (bind `sprk_DirectRecipient@odata.bind` → `/systemusers(<id>)`), read column `_sprk_directrecipient_value`.
    - The relationship is `sprk_systemuser_sprk_communication_directrecipient`: NoCascade for Assign, Share, Unshare, Reparent and Merge; Delete is RemoveLink. It is in SpaarkeCore and published.
    - The names were sent to UAC-r2 (198 R2).
    - Not yet done: field-level security (054b, which needs its own owner go) and the BFF stamp (piece 2 code). The repo's solution export (`src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Communication/Entity.xml`) is not refreshed; the next solution export picks the column up.
  - §11 for the new column:
    - **Existing:** `sprk_sentby` records only the sender, and the thread's participants are thread-scoped and can change.
    - **Extension:** the thread cannot be used, because a Direct message's audience must not follow later changes to its thread (owner, option A).
    - **Cost of doing nothing:** 198's reconcile has no per-message recipient, so it cannot grant the recipient Read once the message is owned by the Direct team.
- **Piece 3** (moving the owner to the Direct team): built against UAC-r2's behaviour contract below. It needs no code from 198's branch.

## Piece 3 contract (from UAC-r2, 2026-11-09)

For each message moved to the Direct team:

1. **Before the assign** (code already on master): call `IDirectThreadAccessService.GrantMessageAccessAsync(communicationId, assignment)` with the thread's `DirectParticipants` assignment, while the message is still user-owned.
   - It grants Read on the message to the thread's two participants (`ReadDirectParticipantsForGrantAsync`).
   - It is best-effort and never throws: log the result and go on.
2. **Assign** the message and its content rows to the RESOLVED Direct team. Content rows are the attachment, participant and email review log rows, and documents via `sprk_relatedcommunication`.
   - Use this project's lookup only: `IsIdentified` first, ids compared non-null.
   - A refusal or a fault means the message does not move.
   - Shares survive an assign.
3. **After the assign, no call is required.** 198's share job (`SecureChildShareReconciliationJob`, Direct pass, every 2 minutes) selects Direct-team-owned messages by `modifiedon` (the assign bumps it) and runs `DirectMessageParticipantShares.ReconcileAsync`. The reconcile:
   - grants Read to exactly the participants, on the message and on its Direct-owned content rows;
   - applies the Restricted bar;
   - revokes every other user's direct share;
   - never grants a team and never touches a Secure-owned message;
   - is idempotent.

   An incomplete answer is retried (backoff after 3 failures), and the job reports the failure itself; piece 3 owns no retry.
   - Optional, once 198 is on master: call `ReconcileAsync(communicationId, listedDirectTeam: <resolved team id>, ct)` right after the assign. It never throws and returns `Outcome { Status, Complete }`; when it is incomplete, only log.

**Stated limit:** between the assign and the job's next run (up to ~2 minutes), the content rows are readable by neither participant. If step 1 failed, the message itself is not readable either. The limit fails closed, and the reconcile closes it.

**Deploy order (binding):**
- 198 deploys BEFORE piece 3.
- That environment needs the Direct role first (198 Part A, `Set-CustomerOwnerTeams.ps1` Direct mode; a live step under UAC-r2's owner go).
- Piece 3 must never reach an environment where the 198 job is not running.

**Participants after 198 R2:** the reconcile takes the pair from {`sprk_sentby`, `sprk_directrecipient`}.
- It needs exactly 2 distinct, non-empty systemusers; otherwise it grants and revokes nothing.
- A missing recipient is terminal until a repair or 054c's backfill stamps it, which bumps `modifiedon`.
- So a moved message is readable by its recipient only after the recipient is stamped, by piece 2 or by 054c.

## 054a piece 2 — recipient stamp

Branch `task/fm-054a-3` (cut from 5b58c87875). FULL rigor. No live Dataverse write, no deploy.

### The rule

- A **Direct message** is a Message row (`sprk_communicationtype` = Message) that carries no typed regarding lookup and joins a thread the join found Direct (`ThreadGrantAudience.DirectParticipants`: typed Direct, no typed lookup — #1629's audience, not a re-read).
- It carries its own two participants: `sprk_sentby` (the sender) and `sprk_directrecipient` (the thread's other participant).
- The recipient comes from the stamp's own read of the thread's two-party list (`DirectMessageRecipient.ReadParticipantsAsync`, fix round 1): the thread's user owner and every systemuser holding a share on the thread whose mask includes Read (`(mask & 1) != 0`, `RecordShareLevels.CanRead`); a team owner and a team share are never participants; empty unless the thread is typed Direct. The share read is the strict one (`GetPrincipalAccessOrThrowAsync`). It is stamped only when that list is exactly 2 distinct, non-empty ids, one of them the sender, and both systemusers read `isdisabled = false` and carry no `applicationid` (an application user is never a participant). Otherwise no recipient is stamped.
- `sprk_sentby`: a row that already carries it keeps it as it is. A row without one (inbound chat) gets the systemuser its chat identity maps to, in the same write as the recipient.
- Inbound sender not mappable: no sender and no recipient are stamped; ONE Error is logged with event code `direct_message.recipient_unmapped` (EventId name and message), the message id and the thread id. The message is still created and still filed into the thread.
- Any fault reading the message, the thread or its shares stamps nothing; the thread lookup is still written. Each outcome has its own log event:
  - not exactly two participants including the sender: Information, `direct_message.recipient_not_two_participants` (5402);
  - a participant disabled, missing or an application user: Information, `direct_message.recipient_participant_not_eligible` (5403);
  - participants could not be read: Warning, `direct_message.recipient_participants_unread` (5404);
  - sender unmapped: Error, `direct_message.recipient_unmapped` (5401).
- Every other row — email, a record-filed message, a record thread, the per-user master, the chat create, secure filings — gets no column from this step.
- Written as the BFF application user (`IGenericEntityService`, SDK, app-only). No impersonated write.

### Where the stamp is written, and why there

No create path knows that a message is Direct when it creates the row: the thread lookup is never in a create payload. Every path creates the message first and then files it into a thread with one update (the ACS thread is known only after the send; the inbound thread only after the persist; a respond names its thread, but the join that decides Direct runs after the create, on the persisted row). So the stamp goes in **that filing update**: the thread lookup, `sprk_directrecipient` and (when missing) `sprk_sentby` are written in ONE `UpdateAsync`. A message is never in a Direct thread without its participants when a recipient is known.

- One builder for the update: `ThreadResolver.ThreadFilingFields(threadId, alongside)`, used by the resolver's `AssignThreadAsync` and by `CommunicationService.AssignExplicitThreadAsync`.
- `ThreadResolver.JoinCoreAsync` hands its `assign` writer the columns for the join's audience (none on a reparent: a reparent is a record thread).
- `AssignToThreadReconcilingOwnerAsync` stamps nothing; its one production caller moves an email into a record's default thread.

### Create-path inventory (`sprk_communication`)

Corrected in fix round 1: the "can a recipient be stamped today" column is what today's flows reach, not what the stamp would do on a thread holding a pair. Line numbers are on `task/fm-054a-4`.

| Create site | Type | Thread filing step | Recipient in today's flows |
|---|---|---|---|
| `CommunicationService.cs` Message send, no `ThreadId` (the UI send) | Message | `ResolveOutboundMessageThreadAsync` (:1506) → resolver | **None.** The send passes `AcsThreadId = null` (`CommunicationService.cs:942`), so the messaging sender creates a fresh ACS thread (`MessagingChannelSender.cs:72-84`). The key strategy finds no channel ref for it and answers `CreateNew` (`MessagingThreadKeyStrategy.cs:40-54`), so the resolver CREATEs a Direct thread (`ThreadResolver.cs:190-197`). That thread names no owner (`ThreadResolver.cs:248` sets one only for a record anchor), so it is owned by the BFF application user, and it is shared to nobody: no pair, no recipient. |
| `CommunicationService.cs` Message send naming a `ThreadId` (respond) | Message | `AssignExplicitThreadAsync` (:1510 → :1567, join at :1577) | **Stamped only when the named thread holds a pair**, i.e. a thread created by `POST /threads/direct` (`CommunicationEndpoints.cs:181` → `FindOrCreateDirectThreadAsync`, owned by the caller and shared with Read to the other user). A respond into a resolver-created thread stamps none. |
| `MessagingIngestor.cs` inbound chat (ACS), via the resolver (`MessagingIngestor.cs:112-119`) | Message | resolver JOIN (`ThreadResolver.cs:168`) or CREATE (`:190-197`) | **None.** Inbound joins by ACS thread id through `sprk_communicationchannelref`, and the only writer of a channel ref is `MessagingThreadKeyStrategy.OnThreadCreatedAsync` (`MessagingThreadKeyStrategy.cs:63`), which links a thread the resolver just created. So inbound never joins a pair thread. |
| New Direct thread, first message — resolver CREATE with no regarding (`ThreadResolver.cs:190-197`) | Message | the CREATE path's filing write | **None.** The new thread is app-owned and unshared (see the first row). |

- `startDirectThread` (`src/client/shared/Spaarke.UI.Components/src/services/communicationThreadListApi.ts:385`) has no caller in `src`: no shipped surface creates a `/threads/direct` thread today.
- So in today's flows no Direct message gets a recipient. The stamp fires for a respond into a `/threads/direct` thread, and for a join into one if such a thread is ever reached by key. Fail closed: a missing recipient grants nothing.
| `CommunicationService.cs:1696` (created at :1723) — external thread post | Message | record default thread (Tier 2, `ThreadResolver.cs:580`) | not Direct: never stamped (always filed under a record) |
| `CommunicationService.cs:2573` (created at :2336) — email send as user | Email | `ResolveOutboundThreadAsync` / respond | not Direct: never stamped (email) |
| `CommunicationService.cs:2809` (created at :1994) — email from a shared mailbox | Email | as above | not Direct: never stamped (email) |
| `IncomingCommunicationProcessor.cs:827` — unfiled / filed inbound email | Email | resolver | not Direct: never stamped (email) |
| `EmailUploadCaptureService.cs:407` — email upload capture | Email | resolver | not Direct: never stamped (email) |
| `EmailDraftToolHandler.cs:450` (filed draft, `RecordCreationService`) and `:478` (unfiled draft, run-as-user POST) | Email | none | not Direct: never stamped |
| `DataverseCreateRecordHandler.cs:460` (the chat create, filed, `RecordCreationService`) and `:489` (the chat create, unfiled, run-as-user POST) | any | none | not Direct: never stamped |
| Per-user master thread (Tier 3, `ThreadResolver.cs:603`) and the filing reconcile's moves (`MoveToRecordDefaultThreadAsync`, `MoveUnfiledEmailOutAsync`) | — | — | not Direct: never stamped (no Direct audience) |

No Teams inbound handler exists in the BFF today; the only chat inbound path is `IncomingMessagingJobHandler` → `MessagingIngestor`.

### Inbound chat identity

- `AcsEventNormalizer.ResolveSenderIdentifier(payload)` returns the sender's ACS raw id (`rawId`, else `communicationUser.id`), never the display name that `NormalizedMessage.From` prefers.
- `IncomingMessagingJobHandler` passes it as `ChannelIngestRequest.ProviderSenderId`; `MessagingIngestor` passes it as `ThreadResolutionRequest.SenderChatIdentity`.
- The map is the existing one: `systemuser.sprk_communicationuserid`, written by `AcsIdentityService.EnsureIdentityAsync`. Exactly one systemuser holding the id maps; none or several do not.

### Known limits (stated, not defects)

- **L1.** A Direct thread the resolver creates itself (a chat message with no regarding and no known thread) is owned by the BFF application user and shared to nobody, so its two-party list is not a sender/recipient pair and no recipient is stamped. A Direct thread with a real pair is the one `POST /threads/direct` creates (`FindOrCreateDirectThreadAsync`, owned by the caller, shared to the other user); its messages are stamped on the respond path. See "Open: the resolver-CREATE gap".
- **L3. The thread's owner controls which pair the thread yields.** The stamp reads the thread's owner and its Read shares at the filing write. Whoever can change the thread's owner or shares before a message is filed decides that message's recipient. The rule still holds: exactly two distinct enabled people, one of them the sender.
- **L2.** A message filed under a record is not a Direct message and gets no recipient. If it is later unfiled back into a Direct thread, no step here stamps it (the unfile step does not refile into a thread); 054c's backfill rule covers such rows.

### Open: the resolver-CREATE gap (owner decision pending)

- Facts: a UI send with no `ThreadId` always reaches the resolver CREATE (see the inventory). The thread it creates is a Direct thread with no regarding, owned by the BFF application user and shared to nobody. Inbound joins only such threads.
- Rule kept in this round: a resolver-created thread yields no recipient. The stamp does not infer a pair from the ACS membership, the message's addressees or the caller; it reads only the thread's owner and Read shares. Fail closed: no recipient, so 198's reconcile grants and revokes nothing for that message.
- Not changed in this round: how the resolver creates a Direct thread, and whether a send goes through `FindOrCreateDirectThreadAsync`. Either change is the owner's decision. Until it is made, 054c's backfill rule (exactly 2 distinct enabled systemusers, one of them `sprk_sentby`) also selects no message in a resolver-created thread.

### Deploy order (binding, piece 2)

- 054b's field security on `sprk_sentby`, `sprk_directrecipient` and the message's thread lookup is live on an environment BEFORE 198 R2 reads the pair there. Until those columns are secured, a user who can write the message can change them, so 198 R2 must not decide grants from them.

### Impersonated writes found (for 054b)

- The stamp itself: none. Both writers (`ThreadResolver.AssignThreadAsync`, `CommunicationService.AssignExplicitThreadAsync`) use `IGenericEntityService` (app-only SDK).
- Run-as-user writers of `sprk_communication` 054b must cover (each writes as the caller, so once the three columns are secured a payload naming one of them fails; 054b's dry-run lists each with what it can carry):
  - `Api/ChildRecordEndpoints.cs:817` — the generic child-record PATCH (`user.PatchAsync` with the mapped body); `sprk_communication` is a supported table (`:911`). Its body is caller-supplied, so it can name `sprk_sentby`, `sprk_directrecipient` or `sprk_communicationthread`.
  - `Services/Dataverse/OwnedChildWrite.cs:1559` — the owned-child update, a run-as-caller PATCH of the mapped body (`sprk_communication` is handled there, `:1551`).
  - `Services/Signals/Actions/InquiryDispositionService.cs:485` — a run-as-caller PATCH of `sprk_associationstatus` only; listed for completeness, it names none of the secured columns.
  - `DataverseCreateRecordHandler.cs:489` and `EmailDraftToolHandler.cs:478`, below.
- `DataverseCreateRecordHandler.cs:489` — the chat create of an UNFILED `sprk_communication` is a run-as-user POST of model-supplied columns. Once 054b secures `sprk_sentby`, `sprk_directrecipient` and the message's thread lookup, a user-run create naming any of them fails. 054b should confirm the write mapper refuses or drops those columns (or that the failure is the wanted answer), and list this path in its dry-run evidence.
- `EmailDraftToolHandler.cs:478` — an UNFILED email draft is a run-as-user POST. Its payload never names `sprk_sentby` (the sender is written only on the filed, app-only path), the recipient or the thread lookup. No change needed; listed for 054b's scan.

### §11 for the new helper and fields

`DirectMessageRecipient` (static, beside `DirectThreadAccessService`):
- **Existing:** `DirectThreadAccessService` holds the two-party list and the per-message grant; `ThreadResolver` holds the join that decides Direct. Neither writes a participant onto the message.
- **Extension:** it is a static helper (no new service, interface member or DI registration); it uses the join's `ThreadAssignment` for Direct-ness.
- **Deliberate duplicate (fix round 1):** the stamp reads the thread's owner and shares itself (`ReadParticipantsAsync`) instead of calling `GetParticipantSystemUserIdsAsync`. Why: the stamp decides a write from the shares, so it needs the strict read (`GetPrincipalAccessOrThrowAsync`) and the Read-mask filter, and the shared reader in `DirectThreadAccessService.cs` uses the soft read and is being changed by its owner in UAC-r2 task 198. When 198 lands with a strict, Read-filtered reader, the stamp converges onto it and this read is removed.
- **New dependency, not a registration:** `ThreadResolver` and `CommunicationService` take an optional `IDataverseRecordShareService` (already registered, `CommunicationModule.cs:711`), passed to the stamp. Without it no participant is stamped.
- **Cost of doing nothing:** 198's reconcile reads the pair from `{sprk_sentby, sprk_directrecipient}`; without the stamp, no new Direct message has a recipient and the recipient never gets Read once the message is owned by the Direct team.

`ChannelIngestRequest.ProviderSenderId`, `ThreadResolutionRequest.SenderChatIdentity`, `AcsEventNormalizer.ResolveSenderIdentifier`:
- **Existing:** `NormalizedMessage.From` carries the sender, but prefers the display name, which maps to no systemuser.
- **Extension:** they extend the existing DTOs and normalizer (ProviderMessageId / ProviderThreadId precedent); no new type.
- **Cost of doing nothing:** an inbound chat message's sender cannot be mapped, so no inbound Direct message would get a sender or a recipient.

No new package, plugin, DI registration or Dataverse column (the column was created by `Set-DirectRecipientSchema.ps1`).

### Evidence

- **Fail-then-pass** (`tests/unit/domain/Communication/DirectMessageRecipientStampTests.cs`, filter `FullyQualifiedName~DirectMessageRecipientStampTests`).
  - Before: the 7 changed source files were reset to 5b58c87875 and the helper was removed. The test file built against them; the rule test was left out, because it names the helper. Result: **Failed 5, Passed 14, Total 19.** The 5 failures are the 4 must-fire paths (send-join, respond, new Direct thread, inbound join) and the unmapped-sender Error.
  - After: **Passed 20, Failed 0.**
- **Builds:** `Sprk.Bff.Api` and `Sprk.Bff.Api.Tests` each built with 0 warnings and 0 errors.
- **Filtered runs** (all `--no-build`, never unfiltered):
  - Communication / DirectThread / DirectMessage / ThreadResolver / Messaging / Ingest / Teams / Inbound / AcsEvent / RecordOwnership: **4033 passed, 0 failed, 2 skipped.**
  - Round-4 field-mapping filter set (FieldMapping … SecureChildOwnership): **4508 passed, 0 failed, 3 skipped.**
  - Chat / Office, minus the AI chat tests (`Api.Ai.`, `Services.Ai.`) and minus `Api.Office.`: Chat **297 passed**; Office **529 passed**; 0 failed.
  - AI chat (`(Chat|Office)&(Api.Ai.|Services.Ai.)`): **1469 passed, 0 failed, 1 skipped.**
  - `Sprk.Bff.Api.Tests.Api.Office.`: each of its 29 classes, run alone, passes: **567 passed, 0 failed, 7 skipped.**
- **ArchTests** (RefileWriterAfterRefile, RecordOwnerAssignmentCensus, InboundIngestionCreators, CommunicationBatchLink, ChildRowOwnershipBuilder, RecordFamilyEdges, AccessTableReader, FeatureRolePrivilege, Communication, DirectThread, ThreadResolver): **183 passed, 0 failed.**
- **Excluded from the combined run: a host crash that exists before this change** (see #2060, the nightly full-unit-tests failure on master).
  - When the `Sprk.Bff.Api.Tests.Api.Office.` contract classes run together in one test host with the Chat/Office slice, the test host process crashes ("Test host process crashed").
  - The same slice crashes the same way on the base commit 5b58c87875, without this change: 2782 passed and 0 failed before the abort.
  - `--blame-crash` names no single test, and every class passes on its own (above), so the crash depends on load, not on one class.
  - Under that load, `ChatMessageStreamingContractTests.SendMessage_ModelThrowsAfterTwoTokens_EmitsTypingEndThenSafeErrorAndNothingAfter` also timed out once (about 2 minutes). Its class passes alone, 7 of 7.
  - The combined gate is therefore run as the slices above, with `FullyQualifiedName!~Sprk.Bff.Api.Tests.Api.Office.` on the combined filter, and the `Api.Office.` classes are run one at a time.
- **After the commit** (58e846040b; the pre-commit `dotnet format` changed nothing): BFF and test project each 0 warnings / 0 errors. The whole filter with `!~Sprk.Bff.Api.Tests.Api.Office.` finished: **9118 passed, 2 failed, 3 skipped.**
  - The 2 failures are wall-clock timing assertions:
    - `CallerChecksConcurrencyTests.TheWholeCheck_TakesItsLongestChain_NotTheSumOfItsReads` (1.67 s against a 1.2 s bound);
    - `RecordCreationServiceMappingSeamTests.CreatePath_ANamedSourceMappedCreate_WithDelayedReads_OverlapsTheMappingReads_AndAnswersAsTheSequentialRun`.
  - Both classes run alone: **244 passed, 0 failed.** Neither class touches the communication write path. The bounds miss under the machine's parallel load.
- **CVE:** `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api` reports no vulnerable packages. No package was added.
- **Publish-size delta:** not measured here, because `scripts/publish-delta.ps1` runs in the main session only. This change adds no package and no project reference.

rework: 054a piece 2 — sprk_directrecipient and sprk_sentby stamped in the write that files a Message into a Direct thread (send, respond, inbound join, new Direct thread); none on any other row.

## 054a piece 2 — fix round 1 (review)

Branch `task/fm-054a-4` (cut from a26074e6d6). FULL rigor. No live Dataverse write, no deploy. Code commit b8d48727ef.

### Scope as changed by the file's owner

`DirectThreadAccessService.cs` belongs to UAC-r2, which fixes the zero-mask filter in its reader in task 198. This round leaves that file unchanged: `git diff a26074e6d6 -- src/server/api/Sprk.Bff.Api/Services/Communication/Access/DirectThreadAccessService.cs` is empty. So F1 and F2 are fixed on the stamp's own path. The shared reader and its callers (`GetParticipantSystemUserIdsAsync`, the grant path's `ReadDirectParticipantsForGrantAsync`, and `FindExistingAsync`) do not change in this round. The stamp no longer calls the shared reader.

### Fixes

- **F1, zero-mask shares.** `DirectMessageRecipient.ReadParticipantsAsync` counts a share only when it is a systemuser share whose mask includes Read (`RecordShareLevels.CanRead`, the `(mask & 1)` convention of `InternalShareEndpoints.cs` and `SecureChildShareSynchronizer.DirectMasks`).
- **F2, strict read.** The stamp's share read is `GetPrincipalAccessOrThrowAsync`. A throw lands in the existing catch: Warning `direct_message.recipient_participants_unread`, no recipient, and the thread lookup is still written. The outcomes have distinct events (see "The rule").
- **K2, application users.** `BothEnabledPeopleAsync` requires both participants enabled and without `applicationid`.
- **F3/F4, tests.** The participant double is now the thread as stored: the owner on the thread row, and the share rows seeded by the test plus any granted during the run. A thread the resolver creates becomes the stored thread, owned by the application user, with no shares. The resolver-CREATE test asserts no recipient. The send-JOIN and inbound-JOIN cases are named and commented as reachable only for a thread created by `POST /threads/direct`. Must-fire cases kept: respond (both orientations), and a join into a `/threads/direct` thread.
- **Notes.** The path inventory is corrected, and these sections were added: the resolver-CREATE gap, the 054b writer list, the deploy-order rule, and L3 (the thread's owner controls the pair).

### Evidence

- **Fail-then-pass** (filter `FullyQualifiedName~DirectMessageRecipientStampTests`).
  - Before: the three fixes were reverted inside the stamp's reader only. The share read was soft with fault → empty, there was no Read-mask filter and no application-user filter. Result: **Failed 3, Passed 22, Total 25.** The 3 failures are the new must-not-fire tests, one per fix: `AZeroMaskShareRow_IsNotAParticipant_AndGetsNoRecipient` (F1), `AShareReadFault_StampsNothing_LogsTheUnreadWarning_AndStillFilesTheMessage` (F2) and `AThreadOwnedByAnApplicationUser_SharedToTheSender_GetsNoRecipient` (K2).
  - After: **Passed 25, Failed 0.**
  - The rewritten resolver-CREATE test (`FirstMessageOfAThreadTheResolverCreates_GetsNoRecipient_BecauseTheThreadIsAppOwnedAndUnshared`) passes before and after. Production never stamped there; the old test's double returned a pair for every thread, which is what made it fire.
- **Builds (after the commit's `dotnet format`):** `Sprk.Bff.Api` and `Sprk.Bff.Api.Tests` each built with 0 warnings and 0 errors.
- **Filtered run (after the commit):** the filter was (DirectMessageRecipient | DirectThreadAccess | Communication | ThreadResolver | Messaging | InternalShare | SecureChildShare | the round-4 field-mapping set from `scripts/verify-suites.sh`) with `FullyQualifiedName!~Api.Office.` (#2060). Result: **8810 passed, 0 failed, 2 skipped.** No timing test failed in this run.
  - An earlier run gave 3 failures. It ran against a test build left from the fail-then-pass step (the source was restored but not rebuilt). After a rebuild, the same three tests pass. Recorded so the count is not mistaken.
- **CVE:** `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api` reports no vulnerable packages. No package was added.
- **Publish-size delta:** not measured here (`scripts/publish-delta.ps1` runs in the main session). No package or project reference was added.

rework: 054a piece 2 fix round 1 — the Direct recipient stamp reads the thread's owner and Read shares itself with the strict share read; zero-mask shares, application users and unread shares give no recipient; the tests are driven by the thread as stored; notes corrected (reachability, the resolver-CREATE gap, 054b writers, deploy order).
