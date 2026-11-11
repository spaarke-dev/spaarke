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

**Assign invariant (UAC-r2, 2026-11-09; binding on piece 3 and on the unfile path):**
- A message is assigned to the Direct team ONLY when it carries a valid stamped pair: exactly two distinct, non-empty systemusers, one of them `sprk_sentby`.
- A message without a valid pair stays with its current owner until a stamp makes the pair valid. That includes 054c's manual-review rows.
- This project's writer is the one server-side owner of that assign, so the guard lives there.
- Must-fire tests: the piece-3 move with an empty recipient leaves the owner unchanged; the unfile back to the Direct team with an empty recipient leaves the owner unchanged.
- 198 R2 (`task/uac-198-direct-share-side`, `740df14540..3e8a492908`) reads the pair with explicit selects. Any pair that is not valid is a settled no-op for it: it grants nothing and revokes nothing. A disabled participant is not granted; the other one still is.

**Deploy order for legacy Direct messages (agreed with UAC-r2 on 2026-11-09; binding on both sides; each live step needs the owner's go in its own session):**
1. The piece-2 BFF stamp deploys first.
2. Then 054c: dry run, the owner's go, apply. It stamps only where the thread yields exactly 2 distinct enabled systemusers, one of them `sprk_sentby`; every other row is listed for manual review.
3. Then 198 deploys.

The rows on the review list stay with their current owner until someone stamps them; the assign invariant above keeps them out of the Direct team.

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
| `CommunicationService.cs` Message send, no `ThreadId` (the UI send) | Message | **Piece 2b:** `ResolveSentMessageThreadAsync`. An outbound Direct send whose one address maps to one eligible systemuser → `FindOrCreateDirectThreadAsync(sender, recipient)` → `AssignExplicitThreadAsync` (the respond path's join). Otherwise `ResolveOutboundMessageThreadAsync` → resolver, unchanged. | **Stamped when routed** (the pair's thread is owned by one party and shared with Read to the other, so the stamp finds the pair). Not routed (zero or several addresses, external, ambiguous, disabled, application user, self, record-filed, a fault): today's path — the resolver CREATEs an application-owned, unshared Direct thread (`ThreadResolver.cs:190-197`, no owner at `:248`), no pair, no recipient. |
| `CommunicationService.cs` Message send naming a `ThreadId` (respond) | Message | `AssignExplicitThreadAsync` (:1510 → :1567, join at :1577) | **Stamped only when the named thread holds a pair**, i.e. a thread created by `POST /threads/direct` (`CommunicationEndpoints.cs:181` → `FindOrCreateDirectThreadAsync`, owned by the caller and shared with Read to the other user). A respond into a resolver-created thread stamps none. |
| `MessagingIngestor.cs` inbound chat (ACS), via the resolver (`MessagingIngestor.cs:112-119`) | Message | resolver JOIN (`ThreadResolver.cs:168`) or CREATE (`:190-197`) | **None.** Inbound joins by ACS thread id through `sprk_communicationchannelref`, and the only writer of a channel ref is `MessagingThreadKeyStrategy.OnThreadCreatedAsync` (`MessagingThreadKeyStrategy.cs:63`), which links a thread the resolver just created. So inbound never joins a pair thread. |
| New Direct thread, first message — resolver CREATE with no regarding (`ThreadResolver.cs:190-197`) | Message | the CREATE path's filing write | **None**, unchanged: the new thread is app-owned and unshared. Since piece 2b an outbound Direct send reaches this path only when the pair routing does not resolve (first row); the first message between a resolved pair goes to the thread `FindOrCreateDirectThreadAsync` creates, and is stamped. |

- `startDirectThread` (`src/client/shared/Spaarke.UI.Components/src/services/communicationThreadListApi.ts:385`) has no caller in `src`: no shipped surface creates a `/threads/direct` thread today.
- The stamp fires for an outbound Direct send whose one recipient resolves (filed into the pair's thread), for a respond into a pair thread, and for a join into one if such a thread is ever reached by key. Fail closed: a missing recipient grants nothing.
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

- **L1.** A Direct thread the resolver creates itself (a chat message with no regarding and no known thread) is owned by the BFF application user and shared to nobody, so its two-party list is not a sender/recipient pair and no recipient is stamped. A Direct thread with a real pair is the one `FindOrCreateDirectThreadAsync` creates (owned by the caller, shared to the other user): `POST /threads/direct` and, since piece 2b, an outbound Direct send whose one recipient resolves. Its messages are stamped. What still reaches the resolver CREATE, and so gets no recipient: an outbound Direct send the pair routing refuses (zero or several addresses, an address that maps to no enabled user or to several, the sender, a disabled or application user, a fault), and every inbound chat message (it joins by ACS thread only). See "054a piece 2b".
- **L3. The thread's owner controls which pair the thread yields.** The stamp reads the thread's owner and its Read shares at the filing write. Whoever can change the thread's owner or shares before a message is filed decides that message's recipient. The rule still holds: exactly two distinct enabled people, one of them the sender.
- **L2.** A message filed under a record is not a Direct message and gets no recipient. If it is later unfiled back into a Direct thread, no step here stamps it (the unfile step does not refile into a thread); 054c's backfill rule covers such rows.

### The resolver-CREATE gap — decided (owner 2026-11-10, option a)

- Facts (piece 2): a UI send with no `ThreadId` always reached the resolver CREATE (see the inventory). The thread it creates is a Direct thread with no regarding, owned by the BFF application user and shared to nobody. Inbound joins only such threads.
- **Decision (owner, 2026-11-10, option a):** an outbound Direct send is routed through its pair's Direct thread (`FindOrCreateDirectThreadAsync`). Built in piece 2b, below.
- Rule kept: a resolver-created thread yields no recipient. The stamp still reads only the thread's owner and Read shares; the pair routing decides which thread the message is filed into, never the recipient column. How the resolver creates a Direct thread is unchanged.
- 054c's backfill rule (exactly 2 distinct enabled systemusers, one of them `sprk_sentby`) still selects no message in a resolver-created thread.

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

- **C1 — DI wiring test.** `Spaarke.ArchTests.DirectMessageRecipientDiWiringTests` boots the real BFF (`BootedBff`, Development and Production) and asserts the composed `CommunicationService` and `IThreadResolver` each hold a non-null `IDataverseRecordShareService` (`_recordShares`, read by reflection). Fail-then-pass: with `_recordShares = null` forced in both constructors, **Failed 2, Passed 0** (message: "holds NO IDataverseRecordShareService ... stamp silently does nothing"); restored, **Passed 2, Failed 0**. The shared boot gained a `Communication:DefaultMailbox` and approved sender so the Production boot can construct `CommunicationService` (a config stub, as the other validator stubs there). The five booted-host classes: 120 passed, 0 failed.

## 054a piece 2 — fix round 2 (missing-column fail-safe)

Branch `task/fm-054a-6` (cut from 47c6a26627). FULL rigor. No live Dataverse write, no deploy. Code commit 9e20911e4d.

### The finding (F)

The stamp's columns ride in the same update as the message's thread lookup. `sprk_directrecipient` exists only where `scripts/Set-DirectRecipientSchema.ps1 -Apply` has run. In any other environment that update is refused, the thread lookup is not written either, and the message lands unthreaded behind a non-fatal Warning.

### The fix

- **One helper.** `DirectMessageRecipient.WriteThreadFilingAsync` is the ONE filing write. `ThreadResolver.AssignThreadAsync` (the resolver's join and create) and `CommunicationService.AssignExplicitThreadAsync` (respond-into-thread) both call it. `ThreadResolver.ThreadFilingFields` is unchanged and only this helper writes it.
- **Retry once.** When the write carried stamp columns and the refusal is "this column does not exist on the table" for one of them, the write is sent once more with the thread lookup alone. Then one Error is logged: event `direct_message.recipient_column_missing` (EventId 5405). It names `scripts/Set-DirectRecipientSchema.ps1 -Apply` and carries the message id and the thread id.
- **Narrow.** Any other fault propagates unchanged to the caller's best-effort catch. So does any fault of a write without stamp columns, and any fault of the retry.

### The fault matcher and its source

Reused, not new: `CommunicationService.IsMissingColumnFault(ex, column)` (ebilling-r1 task 151c-2, the batch-link column on the communication create). It is tested against each stamp column the write carried, never the thread lookup. It walks the exception chain. A link matches only when its message names the column AND either:

- it is the SDK's typed `FaultException<OrganizationServiceFault>` with error code `0x80041103` (QueryBuilderNoAttribute; the same constant as `RecordOwnershipResolver.AttributeDoesNotExistErrorCode`); or
- its text says the table has no such attribute or property ("doesn't contain attribute", "does not contain attribute / an attribute", "Could not find a property named" (the Web API's `0x80060888`), "Invalid property", "does not exist on type").

What `IGenericEntityService.UpdateAsync` surfaces: the BFF's implementation (`DataverseServiceClientImpl.UpdateAsync`) calls the SDK `ServiceClient` and rethrows any fault as `InvalidOperationException("Failed to update … : {inner message}", inner)`. The typed fault is therefore the inner exception, and the wrapper's message carries its text. The matcher covers both, and the Web API text form as well.

### Evidence

- **Fail-then-pass** (filter `FullyQualifiedName~DirectMessageRecipientStampTests`; the new tests use no new source symbol, so they compile against the old source).
  - Before (the three source files checked out from 47c6a26627 and rebuilt): **Failed 8, Passed 37, Total 45.** The 8 failures are exactly the must-fire tests: send join × 3 fault shapes (SDK typed + wrapped, SDK typed + localized message, Web API unknown property), the resolver create, the inbound join, and respond × 3 fault shapes. Reason: one filing attempt where two were expected.
  - After: **Passed 45, Failed 0.**
  - The must-not-fire tests pass before and after, as they should: they pin today's behaviour. They cover privilege-on-the-column, throttle, HTTP 403 and missing-referenced-row (code `0x80040217`, naming the column) faults on both the resolver join and the respond path (one attempt, nothing written, the non-fatal Warning, no new event); a stamp-less write that faults on a missing attribute (one attempt, both paths); and a successful combined write (exactly one update, both paths).
  - Rig change: `DirectMessageRecipientStampTests` became partial. The update double records every attempt and can fault by columns. The service gets a recording logger. A flag gives a thread the run creates a pair, used only to drive the create path's filing write.
- **Builds (after the commit's `dotnet format`):** `Sprk.Bff.Api` and `Sprk.Bff.Api.Tests` each built with 0 warnings and 0 errors.
- **Filtered run (after the commit):** the filter was (DirectMessageRecipient | ThreadResolver | CommunicationService | Communication | Messaging | FieldMapping | RecordCreation) with `FullyQualifiedName!~Api.Office.` (#2060). Result: **3562 passed, 0 failed, 2 skipped.**
- **ArchTests:** `DirectMessageRecipientDiWiring`, **2 passed, 0 failed.**
- **CVE:** `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api` reports no vulnerable packages. No package was added.
- **Publish-size delta:** not measured here (`scripts/publish-delta.ps1` runs in the main session). No package or project reference was added.

### The rule

The schema script runs before a BFF built from this branch deploys; when it hasn't, the thread lookup is still written and one Error names the script.

Order: the retry runs first and the Error is logged after it. If the retry itself faults, that fault goes to the caller's non-fatal Warning, and no `recipient_column_missing` Error is logged for that message.

rework: 054a piece 2 fix round 2 — the Direct filing write is one helper (`DirectMessageRecipient.WriteThreadFilingAsync`) used by the resolver and the respond path; a refusal because a stamp column does not exist (the reused `CommunicationService.IsMissingColumnFault`: SDK `0x80041103` or the no-such-attribute/property text) is retried once with the thread lookup alone and logs one Error `direct_message.recipient_column_missing` naming the schema script; every other fault is unchanged.

## 054a piece 2b — outbound Direct pair routing

Branch `task/fm-054a-7` (cut from a9794dbaad). FULL rigor. No live Dataverse write, no deploy. Owner decision 2026-11-10, option a (the resolver-CREATE gap above). UAC-r2's review points applied: one recipient mapping, reused; the count taken after the send's own address normalization; no new access rule; today's path byte-for-byte when refused; no owner change on a fault.

### The rule

- An **outbound Direct send** is a Message send with no `ThreadId` whose prepared row is filed under no record: `CommunicationService.IsOutboundDirectSend` — `CommunicationType.Message`, no `request.ThreadId`, and `!DirectMessageRecipient.CarriesATypedRegarding(preparedRecord)`.
  - "No record filing" is the test the Direct filing stamp makes on the row: a typed regarding lookup in `RegardingFieldMap.AllRegardingFields` with a non-empty id. It was extracted from `ColumnsForDirectFilingAsync`, so both use one predicate.
  - The prepared row carries every regarding the send files it under (explicit association, reply inheritance). `ResolveOutboundOwnerAsync` writes them before the send.
- **The recipient** is resolved on the server. All of these must hold:
  1. `To ∪ Cc ∪ Bcc`, empty entries dropped, each normalized as the participant index does, then de-duplicated, is exactly ONE address. The normalizer is `CommunicationParticipantIndexer.NormalizeAddress` (trimmed, lower-cased invariant), extracted from the index's dedupe key; it is the only address normalization the Message send applies. A self-address beside one recipient counts as two.
  2. That address maps to exactly ONE enabled systemuser through the existing internal-user email read: `IContactIdentityStore.FindEnabledSystemUsersByEmailAsync`, decided by `ContactBindingDecision.MatchingEnabledSystemUserIds`. This is the A9 licensed-user read: enabled systemusers whose `internalemailaddress` OR `domainname` equals the address, two rows. `InquiryRecordFacts` decides "internal" with the same pair. No second mapping was added.
  3. From the mapping on, every comparison is by systemuser id. The id is not the sender's: `senderParticipant.RecordId`, the systemuser `ResolveMessageSenderAsync` resolved from the caller's `oid`. And `DirectMessageRecipient.ReadEligibilityAsync` reads it enabled and without `applicationid`. That is the Direct participant rule, extracted from `BothEnabledPeopleAsync`, which now calls it with the same query.
  - No other flag is checked (no external, Restricted or other access flag). The grant side decides those.
- **When it holds**, after the message is persisted:
  - `IDirectThreadAccessService.FindOrCreateDirectThreadAsync(sender, recipient)` answers the pair's thread.
  - The message is filed into it through the respond path's join, `AssignExplicitThreadAsync`: `ThreadResolver.JoinThreadAsync` → `DirectMessageRecipient.WriteThreadFilingAsync` → `GrantMessageAccessAsync`.
  - Piece 2's stamp writes `sprk_directrecipient` (and `sprk_sentby` when the row lacks it) in that one filing update. There is no second filing write and no second stamp.
- **When it does not hold:**
  - Today's path runs unchanged: `ResolveOutboundMessageThreadAsync` → the resolver CREATE (an application-owned, unshared Direct thread, no recipient, no new share).
  - ONE Information event `direct_message.pair_routing_skipped` (EventId 5406) carries the reason code and the message id, never an address.
  - Reason codes: `not_one_address`, `no_internal_user`, `ambiguous_address`, `recipient_is_sender`, `recipient_disabled`, `recipient_application_user`, `recipient_missing`, and `not_configured` (the Direct thread access service or the email read is not composed).
  - The send is never refused.
  - A disabled user's address refuses as `no_internal_user`, because the reused read returns enabled users only. `recipient_disabled` means the one mapped user read disabled at the eligibility read.
- **Faults** (the send succeeds in every case, NFR-02):
  - The recipient cannot be resolved: the email read throws or does not complete, or the eligibility read throws. One Warning `direct_message.pair_routing_unresolved` (5407); today's path.
  - `FindOrCreateDirectThreadAsync` throws: one Warning `direct_message.pair_thread_failed` (5408); today's path; nothing is stamped.
  - The explicit join faults after the pair's thread was found: that path's existing best-effort catch ("Explicit thread stamp failed", Warning). The message is left unthreaded, keeps its owner, and gets no recipient and no grant. A pair thread just created stays, empty.
- **Not routed, and no routing event**, because these are not outbound Direct sends:
  - a respond (`ThreadId` set);
  - a record-filed send;
  - the external thread post (`PostToRecordThreadAsync` calls `ResolveOutboundMessageThreadAsync` directly, and its row is always filed under a record);
  - every email send (a different branch of `SendAsync`);
  - inbound (`MessagingIngestor`, unchanged, fail-closed).
- The ACS side (`MessagingChannelSender`) is unchanged: no membership change.

### Write order (as the code runs it)

1. **Owner:** decided before the send and written with the create (`BuildMessageDataverseRecordAsync` → `ResolveOutboundOwnerAsync`; an unfiled message keeps its creator, E1). The routing writes no owner. A join into a Direct thread (no typed lookup) runs no reparent, so no owner write follows.
2. **Stamp:** the one filing update (thread lookup + `sprk_directrecipient`, + `sprk_sentby` when missing).
3. **Grant:** `GrantMessageAccessAsync` with the join's Direct audience.

Piece 3 (the owner move) is not built. 2b assigns no owner anywhere.

### Where it lives

`CommunicationService`, beside `ResolveOutboundMessageThreadAsync`:
- `ResolveSentMessageThreadAsync`: the one decision, called only from `SendMessageAsync`;
- `FindDirectPairThreadAsync`, `ResolveDirectRecipientAsync`, `SkipPairRouting`, `IsOutboundDirectSend`;
- the three event codes and `PairRoutingSkipReason`.

### Known limits (stated, not defects)

- **L4.** A routed send still gets a fresh ACS thread per send (`AcsThreadId = null`). That ACS thread is not linked to the pair's thread: no channel ref is written, because the resolver does not run. An inbound reply on it takes the resolver path and gets no recipient (L1). Reusing ACS threads is out of scope (no ACS change).
- **L5.** A routed send is filed into the thread `FindExistingAsync` finds for the pair. That reader is in `DirectThreadAccessService` (UAC-r2's file, unchanged here), so which thread a pair gets is its rule.

### §11 for the new members

`CommunicationService(... contactIdentities)`, an optional constructor parameter. No new DI registration: `IContactIdentityStore` is already registered, singleton, unconditionally, in `ExternalAccessModule`, and `CommunicationService` is a singleton.
- **Existing:** `IContactIdentityStore.FindEnabledSystemUsersByEmailAsync` with `ContactBindingDecision.MatchingEnabledSystemUserIds` is the existing address → internal-user mapping (A9; reused by `InquiryRecordFacts`). The communication services resolve addresses to contacts only (`QueryContactByEmailAsync`).
- **Extension:** reused as is; nothing is added to it.
- **Cost of doing nothing:** the send cannot map its one address to a systemuser, so no outbound Direct send is filed into its pair's thread and none gets a recipient.

`IsOutboundDirectSend`, `ResolveSentMessageThreadAsync`, `FindDirectPairThreadAsync`, `ResolveDirectRecipientAsync`, `SkipPairRouting`, the event codes and the reasons (private or internal, in the existing service):
- **Existing:** `ResolveOutboundMessageThreadAsync` and `AssignExplicitThreadAsync` file a message; `FindOrCreateDirectThreadAsync` finds the pair's thread. Nothing decides a send's pair.
- **Extension:** they call those, and do no filing, stamp or grant of their own.
- **Cost of doing nothing:** the owner's option a is not built, and every outbound Direct send stays in an unshared, application-owned thread with no recipient.

Extracted, with no new behaviour: `DirectMessageRecipient.CarriesATypedRegarding`; `DirectMessageRecipient.ReadEligibilityAsync` + `ParticipantEligibility` (the participant rule, now saying which part failed); `CommunicationParticipantIndexer.NormalizeAddress`. Each was inline in its file; the routing reuses it instead of a copy.

No new package, plugin, DI registration, endpoint or Dataverse column. `git diff a9794dbaad -- src/server/api/Sprk.Bff.Api/Services/Communication/Access/DirectThreadAccessService.cs` is empty.

### Evidence

- **Tests** (KEEP paths):
  - `tests/unit/domain/Communication/DirectMessageRecipientStampTests.PairRouting.cs`: behaviour, through `CommunicationService.SendAsync` / `PostToRecordThreadAsync` with the real `ThreadResolver`.
  - `…PairRoutingRule.cs`: the predicate.
  - The piece-2 rig: `Service()` takes the email read and can drop the Direct access service; a second caller oid.
  - `Spaarke.ArchTests.DirectMessageRecipientDiWiringTests` gained one fact: the booted BFF's `CommunicationService` holds a non-null `_directThreadAccess` and `_contactIdentities` (Development and Production).
  - The email read's double is the directory it reads: enabled users only, case-insensitive, two rows. The pair-thread double is the thread as stored: the first call for a pair creates it, owned by that caller with a Read share to the other; later calls, in either orientation, answer it unchanged.
- **Cases:**
  - Must fire:
    - one internal To → FindOrCreate(sender, recipient), ONE filing update with the thread lookup + the recipient (the row's `sprk_sentby` is kept, not rewritten), and the Direct grant;
    - three sends (A→B, A→B, B→A) reuse one thread, created once and owned by A; each stamps the other party;
    - `"  Other@Contoso.COM "` in To plus `other@contoso.com` and blanks in Cc → one address, one email read.
  - Must not fire. Each asserts: no FindOrCreate; the resolver's create, with no owner and no thread share; no `sprk_directrecipient` in any filing update; ONE Information event with the reason, with no `@` in it. Cases:
    - two addresses; To + a Cc of another; To + the sender's own address in Cc;
    - an external address (in To, and in Bcc only); an address two systemusers hold;
    - a disabled user's address; a mapped user who reads disabled; an application user; the sender's own address;
    - no Direct access service.
    - Also: a refused send's thread create and filing updates equal those of a service without the routing.
  - Not considered (no FindOrCreate, no email read, no routing event): a record-filed send; a send naming a thread (the respond is unchanged: the named pair thread still stamps its recipient); the external thread post; the predicate for an email send.
  - Faults:
    - the email read throws → Warning `pair_routing_unresolved`, today's path, the send succeeds;
    - the email read does not complete (`Failed`) → the same;
    - FindOrCreate throws → Warning `pair_thread_failed`, today's path, nothing stamped;
    - FindOrCreate succeeds and the filing write faults → no landed update, no update attempt carries `ownerid`, no grant, no resolver thread, the explicit path's Warning, and the send succeeds.
- **Fail-then-pass** (filter `FullyQualifiedName~DirectMessageRecipientStampTests`):
  - Before: `CommunicationService.cs`, `DirectMessageRecipient.cs` and `CommunicationParticipantIndexer.cs` checked out from a9794dbaad. The rig's `contactIdentities:` argument was removed and `…PairRoutingRule.cs` left out, because both name new symbols. Result: **Failed 19, Passed 48, Total 67.**
    - The 19: the 3 must-fire tests; the 4 fault tests (the email read throws, does not complete, FindOrCreate throws, the filing faults after the pair's thread was found); and the 12 must-not-fire tests (each asserts the skip event, which did not exist before).
    - The 3 "not considered" tests and every piece-2 test pass before and after.
  - After: **Passed 68, Failed 0.**
- **Builds:** `Sprk.Bff.Api` and `Sprk.Bff.Api.Tests` each 0 warnings, 0 errors.
- **Filtered run** (`--no-build`; `(DirectMessageRecipient | DirectThread | CommunicationService | Communication | ThreadResolver | Messaging) & !Api.Office.`): **2495 passed, 0 failed, 2 skipped.**
- **ArchTests** (`DirectMessageRecipient | Communication | ThreadResolver | InboundIngestion | RecordOwnerAssignmentCensus`, including the new DI-wiring fact): **153 passed, 0 failed.**
- **CVE:** `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` reports no vulnerable packages. No package added.
- **Publish-size delta:** not measured here (`scripts/publish-delta.ps1` runs in the main session). No package or project reference added.

## 054a piece 2b — fix round 1 (review)

Same branch, from a5cacab398. Findings F1-a, F1-b and F-3, plus UAC-r2's addendum (one shape rule on `DirectMessageRecipient`). `DirectThreadAccessService.cs` is still unedited.

### F1-a — the found thread is verified before the join

- **The one Direct-pair shape rule:** `DirectMessageRecipient.ReadStrictPairShapeAsync(IGenericEntityService, IDataverseRecordShareService, Guid threadId, CancellationToken)`, `internal static`. It reads the thread itself (type, owner, privacy, the thread's regarding columns `ThreadResolver.ThreadRegardingColumns`) and answers a `DirectPairShape` (`IsStrictPair`, `OwnerId`, `OtherId`, `Reason`, and `Fault` on a read fault). In order, a thread is a strict pair only when:
  1. it is typed Direct (a missing row also answers `not_direct`);
  2. it carries no typed regarding (`CarriesATypedRegarding`) — else `typed_regarding`;
  3. its privacy is confirmed Open (`CommunicationInternalOnlyFloor.IsConfirmedOpen`; a missing value is not Open) — else `not_open`;
  4. its Read participants are exactly two distinct systemusers, one of them its user owner — else `not_two_read_participants`. Participants are the user owner plus every systemuser share whose mask has Read, through the strict share read (`GetPrincipalAccessOrThrowAsync`). This is the reader `ReadParticipantsAsync` already used, now one private method (`ParticipantsOfAsync`) both call.
  - A fault reading the thread or its shares answers `participant_read_fault`, never a pair. Only the caller's cancellation propagates.
  - Its XML doc names it the one shape rule, shared with the pair-thread lookup (`DirectThreadAccessService.FindExistingAsync` is to call it; that file is not edited here).
- **The routing** (`FindDirectPairThreadAsync`), after `FindOrCreateDirectThreadAsync` answers a thread:
  - `participant_read_fault` → one Warning `direct_message.pair_thread_unread` (5411), today's path, no skip event;
  - not a strict pair → today's path, skip event (5406) with `pair_thread_<reason>`;
  - a strict pair whose {owner, other} is not {sender, mapped recipient} → today's path, skip event with `pair_thread_not_the_pair`;
  - otherwise → the join, as before.
- A thread so refused is not joined, gets no filing write naming it, and no grant is made from it. Nothing is written onto it.

### F1-b — the stamp writes only the mapped recipient

- The mapped recipient goes through as an optional `expectedRecipient`: `AssignExplicitThreadAsync` → `ThreadResolver.JoinThreadAsync` → `DirectFilingColumnsAsync` → `DirectMessageRecipient.ColumnsForDirectFilingAsync`. Every other caller passes nothing, so their behaviour is unchanged.
- At the filing, when the thread's other participant (read again, strictly) is not the expected recipient, the filing writes the thread lookup and no recipient, and logs one Information `direct_message.recipient_not_expected` (5409). The grant follows as the join decides it.

### F-3 — a routed send is logged as routed

- The explicit-join log names its filing: `AssignExplicitThreadAsync(..., filing:)` defaults to `respond-into-thread`; the routing passes `outbound Direct pair thread`.
- A routed send also logs one Information `direct_message.pair_routed` (5410) with the message id and the thread id.
- EventIds 5401–5411 are each used once.

### Known limits (added; stated, not defects)

- **L6.** When `FindOrCreateDirectThreadAsync` creates the thread and then faults on its share grant, the created thread stays, with no message in it. The routing takes today's path (Warning 5408). The fault is inside `DirectThreadAccessService` (UAC-r2's file).
- **L7.** `FindExistingAsync` reads at most 50 of a user's Direct threads. An existing pair thread past that cap can be missed, and a duplicate created. **Fix: 198b**, landing before the 198 deploy.
- **L8.** Until 198 deploys, `GrantMessageAccessAsync` on a routed message reads the thread's participants with master's looser participant read, not the strict one the routing checks with. **Fix: 198b.**

### §11 for the new members

`ReadStrictPairShapeAsync`, `DirectPairShape`, `DirectPairShapeReason` (internal, in the existing `DirectMessageRecipient`):
- **Existing:** `ReadParticipantsAsync` reads a Direct thread's participants; `IsConfirmedOpen` and `CarriesATypedRegarding` read privacy and regarding. Nothing combined them into one answer for a thread the routing did not create.
- **Extension:** built from those three, with the participant reader extracted (`ParticipantsOfAsync`) rather than copied.
- **Cost of doing nothing:** the routing has no rule for which found thread it may join; the rule that it joins only a strict 1:1 Direct thread of the sender and the mapped recipient is not enforced.

`expectedRecipient` (optional parameter on four existing methods), the `filing` label, the event codes 5409–5411:
- **Existing:** the filing stamp derives the recipient from the thread alone.
- **Extension:** an optional parameter on the existing path; nothing new is registered.
- **Cost of doing nothing:** the rule that a routed message's recipient is the one its address mapped to is not enforced at the filing.

No new package, plugin, DI registration, endpoint or Dataverse column.

### Evidence

- **Tests** (KEEP paths):
  - `tests/unit/domain/Communication/DirectMessageRecipientStampTests.PairThreadShape.cs` (new), through `SendAsync` with a stored thread FindOrCreate answers:
    - (A) owned by the recipient, Read shares to the sender and a third user → `pair_thread_not_two_read_participants`;
    - (B) Direct with a typed regarding → `pair_thread_typed_regarding`;
    - (C) owned by the sender, the recipient's share at mask 0, a Read share to a third user → `pair_thread_not_the_pair`;
    - (D) Private, and no privacy value → `pair_thread_not_open`;
    - each asserts: FindOrCreate asked once; no filing write names the found thread; no grant on it; no routed event; today's path (the resolver's create, no recipient); the skip reason;
    - a share-read fault on the found thread → Warning 5411, today's path, no skip event, the send succeeds;
    - a clean pair thread owned by the recipient → joined, recipient stamped, event 5410, the `outbound Direct pair thread` label (and not `respond-into-thread`);
    - F1-b: the share read answers the recipient at the check and a third user at the filing → the filing carries the thread lookup and no recipient, and event 5409;
    - the A9 read's `domainname` match: an address equal to the user's `domainname` (not its `internalemailaddress`), differently cased → routed to that user.
  - `…PairRoutingRule.cs`: the shape rule, one test per reason code plus a pair: a strict pair (owner and other named; a mask-0 share ignored); `not_direct` (record-anchored; missing row); `typed_regarding`; `not_open` (Private; no value); `not_two_read_participants` (no Read share; three participants; a team owner; owner alone); `participant_read_fault` (the share read throws, carrying the fault; the thread read throws); the caller's cancellation propagates; the five codes' strings.
- **Fail-then-pass** (filter `FullyQualifiedName~DirectMessageRecipientStampTests`):
  - Before: `CommunicationService.cs`, `DirectMessageRecipient.cs`, `ThreadResolver.cs` and `…PairRoutingRule.cs` checked out from a5cacab398 (the rule test file names the new symbols). Result: **Failed 8, Passed 69, Total 77.**
    - The 8: (A), (B), (C), (D), no privacy value, the shape-read fault, the clean recipient-owned pair (event 5410 and label), and F1-b.
    - The `domainname` test passes before and after: it covers the existing A9 read and changes no behaviour.
  - After: **Passed 91, Failed 0.**
- **Builds:** `Sprk.Bff.Api` and `Sprk.Bff.Api.Tests` each 0 warnings, 0 errors.
- **Filtered run** (same filter as piece 2b): **2518 passed, 0 failed, 2 skipped.**
- **ArchTests** (same filter as piece 2b): **153 passed, 0 failed.**
- **CVE:** no package added; `dotnet list … --vulnerable --include-transitive` reports none.
- `git diff a9794dbaad -- src/server/api/Sprk.Bff.Api/Services/Communication/Access/DirectThreadAccessService.cs` is empty.

## 054a piece 2b — fix round 2 (security review)

Same branch, from 82da70c50d. Findings F2-1, F2-2 and F2-5. `DirectThreadAccessService.cs` is still unedited.

### F2-1 — a strict pair has exactly one share

- **What the rule sees.** `ReadStrictPairShapeAsync` reads the thread's shares with the strict read (`IDataverseRecordShareService.GetPrincipalAccessOrThrowAsync`). That read is every `principalobjectaccess` row of the thread whose principal is a systemuser or a team, at any `accessrightsmask`. Zero-mask rows are included (a zero mask is how an inherited-access row reads). Rows naming another principal kind are skipped by the seam itself, so the rule never sees them. A row whose principal, mask or `changedon` cannot be read, or a response that continues on another page, throws, and the rule answers `participant_read_fault`.
- **The rule** (`DirectMessageRecipient.StrictOtherOf`):
  - the thread is owned by a systemuser;
  - exactly one systemuser other than the owner holds a share that grants Read — else `not_two_read_participants`;
  - that share is the thread's only share row — else **`extra_principal`** (new code). Any other row refuses, whatever its mask (zero, Append only, Read) and whatever its principal (another systemuser, a team, the owner itself).
- The grant (`GrantMessageAccessAsync`) reads the same share rows. On a thread with the user owner and exactly one share row, its audience is the pair.
- `ReadStrictPairShapeAsync` keeps its signature. Its reason codes are now `not_direct`, `typed_regarding`, `not_open`, `not_two_read_participants`, `extra_principal` and `participant_read_fault`.

### F2-2 — the routed filing is decided before any write

- The routing passes the pair it decided as `ExpectedDirectPair(Sender, Recipient)`. This replaces round 1's `Guid? expectedRecipient` on `AssignExplicitThreadAsync`, `ThreadResolver.JoinThreadAsync`, `DirectFilingColumnsAsync` and `ColumnsForDirectFilingAsync`.
- With a pair, `JoinCoreAsync` adds `sprk_privacystate` to its one read of the thread. Before any write, it requires all of:
  - the row exists, is typed Direct, and has a Direct audience (else `not_direct`);
  - it carries no typed regarding lookup (else `typed_regarding`);
  - its privacy is confirmed Open (else `not_open`);
  - its filing columns are exactly the routed pair (else `not_the_pair`). The recipient must be the routed recipient, any sender column must be the routed sender, and no other column may appear. With a pair, `ColumnsForDirectFilingAsync` checks the row's own `sprk_sentby` against the routed sender and reads the participants under the strict-pair rule above, so a share added in the window refuses too.
- When it refuses, it writes nothing (no filing, no reparent, no owner write) and throws `DirectPairFilingRefusedException`. `AssignExplicitThreadAsync` lets that one exception through without granting. The routing logs one Information `direct_message.pair_filing_refused` (5412) with the reason and takes today's path (`ResolveOutboundMessageThreadAsync`, unchanged). A sender or recipient mismatch also logs 5409.
- Without a pair (the respond path, and every other join), `JoinCoreAsync` reads the same columns and writes the same rows as before.
- The grant after a routed filing still reads the shares itself (L8).

### F2-5 — the caller's cancellation

- At this point `SendMessageAsync` has sent and persisted the message, and every later step is best-effort and never throws. Today's path itself (`ResolveOutboundMessageThreadAsync`) catches everything.
- So in `FindDirectPairThreadAsync`, the caller's cancellation (`OperationCanceledException` while `ct.IsCancellationRequested`) during the recipient read, the find-or-create or the shape read returns null quietly: no Warning, and no `participant_read_fault`.
- The message then takes today's path, which handles the cancellation as it always has.
- A cancellation that is not the caller's (for example an HTTP timeout) is still a fault and logs its Warning.

### Known limits (added; stated, not defects)

- **L9.** A fault in or after the routed filing write (the filing update itself, or anything after it) leaves that message unthreaded and ungranted. The sender still gets success, with the explicit path's Warning. Today's resolver path does not run as a fallback.
  - Everything before the write falls back once to today's path: the F2-2 refusals, and since F-2b-1 a fault of any read the routed join makes before it (the thread read, the filing-columns computation).
  - A fallback after a write fault is not safe. The fault can arrive after the filing update landed (a timeout on a committed write), and the resolver would then file the message a second time, into its own thread, with the pair's recipient already on the row.
- **L10.** When `FindExistingAsync` returns a thread for a pair that is not a strict pair, routing for that pair is blocked. It never shares more than the pair, so it fails safe. **Fix: 198b**, which applies the shape rule in `FindExistingAsync`.

### §11 for the new members

`ExpectedDirectPair` (record struct) and `DirectPairFilingRefusedException`, internal, beside `DirectMessageRecipient`; `RoutedDirectFilingRefusal` and `IsExactlyThePair` (private, `ThreadResolver`); `StrictOtherOf`, `UserOwnerOf` and `ReadStrictParticipantsAsync` (private, `DirectMessageRecipient`); event 5412.
- **Existing:** round 1's `expectedRecipient` covered the stamp only; the join's one thread read and `ColumnsForDirectFilingAsync` already decide the audience and the columns.
- **Extension:** the pair replaces that parameter. The refusal is decided inside the existing join, from its own read. The exception carries the refusal out of the join's existing call chain without changing its return type for every other caller.
- **Cost of doing nothing:** the rule that a routed message is filed only into a strict Direct thread of its pair, as the join itself reads the thread, is not enforced at the write.

No new package, plugin, DI registration, endpoint or Dataverse column.

### Evidence

- **Tests** (KEEP paths):
  - `…PairRoutingRule.cs`:
    - the pair test now has one Read|Append share;
    - `extra_principal` for a user at mask 0, a user with Append only, a team at Read, a team at mask 0, and the owner's own row;
    - the reason-strings test includes `extra_principal` and `not_the_pair`.
  - `…PairThreadShape.cs`:
    - (C) now refuses as `pair_thread_extra_principal`;
    - a new strict pair of the sender and a third user refuses as `pair_thread_not_the_pair`;
    - routing refuses `extra_principal` for {recipient Read + third user at mask 0}, {+ third user Append only} and {+ team}; the clean recipient-owned pair still routes;
    - window cases, each with the thread changing after the shape check: the pair changes (`not_the_pair`, plus 5409); a typed regarding appears (`typed_regarding`, and no update attempt carries `ownerid`); the thread turns Private (`not_open`); another share appears at mask 0 (`not_the_pair`). Each asserts: no filing write names the pair's thread; `GrantMessageAccessAsync` is never called for it; one 5412 with the reason; no "Explicit thread stamp failed"; and today's path ran (the resolver's create, filed with no recipient, its own single grant);
    - cancellation: the caller cancels in the find-or-create, and in the recipient read → no routing Warning, and the send succeeds; a timeout that is not the caller's → Warning 5408, today's path.
- **Fail-then-pass** (filter `FullyQualifiedName~DirectMessageRecipientStampTests`):
  - Before: the three source files checked out from 82da70c50d. In the rule test file, `DirectPairShapeReason.ExtraPrincipal` and `DirectPairFilingRefusedException.NotThePair` were replaced by their literal strings so it compiles. Result: **Failed 15, Passed 91, Total 106.**
    - The 15: the 5 shape `extra_principal` cases, the 3 routing `extra_principal` cases, (C), the 4 window cases, and the 2 caller-cancellation cases.
    - These pass before as well as after: the timeout-is-a-fault test, the new `not_the_pair` scenario, and the reason-strings test in its literal form.
  - After: **Passed 106, Failed 0.**
- **Builds:** `Sprk.Bff.Api` and `Sprk.Bff.Api.Tests` each 0 warnings, 0 errors.
- **Filtered run** (same filter as piece 2b): **2533 passed, 0 failed, 2 skipped.**
- **ArchTests** (same filter as piece 2b): **153 passed, 0 failed.**
- `git diff a9794dbaad -- src/server/api/Sprk.Bff.Api/Services/Communication/Access/DirectThreadAccessService.cs` is empty.

## 054a piece 2b — F-2b-1 (a fault before the routed write)

- **Rule.** On the routed path only (an `ExpectedDirectPair` is given), a fault of any read the join makes before its write is a refusal: `DirectPairFilingRefusedException(threadId, "read_fault")`, carrying the fault as its inner exception. The reads are the thread read and the filing-columns computation; the latter already turns its own read faults into "no recipient" and so into `not_the_pair`.
  - The caller's cancellation is excluded and propagates as before.
  - The routing logs 5412 as a **Warning** with the fault for `read_fault` (Information for the other reasons), then takes today's path once.
- **Unchanged:**
  - The respond path (no pair): the same fault reaches its generic catch ("Explicit thread stamp failed", Warning), with no fallback.
  - A fault in or after the write (L9).
- **§11:** no new type. One new reason constant (`DirectPairFilingRefusedException.ReadFault`), an optional inner-exception parameter on its constructor, and one private predicate (`ThreadResolver.IsRoutedReadFault`).
- **Tests** (`…PairThreadShape.cs`):
  - The routed join's thread read faults after the shape check → today's path ran once; no filing write names the pair's thread; `GrantMessageAccessAsync` was never called for it; one Warning 5412 `(read_fault)`; no "Explicit thread stamp failed".
  - A respond with the same fault → the explicit path's Warning, no update attempt, no resolver thread, no grant, no 5412.
- **Fail-then-pass** against 0ebd43f6a5 (filter `~DirectMessageRecipientStampTests`, the three source files reverted): **Failed 1, Passed 107, Total 108.** The routed read-fault test fails; the respond test passes before and after, as it should. After: **Passed 108, Failed 0.**
- **Filtered run:** **2535 passed, 0 failed, 2 skipped.** **ArchTests:** **153 passed.** **Builds:** 0 warnings, 0 errors; `dotnet format --verify-no-changes` clean.

rework: 054a piece 2b — an outbound Direct send (Message, no ThreadId, no typed regarding) whose To ∪ Cc ∪ Bcc is one address that the A9 internal-user email read maps to one enabled, non-application systemuser other than the sender is filed into FindOrCreateDirectThreadAsync(sender, recipient) through the respond path's explicit join (one filing update carrying the recipient, then the grant); otherwise today's resolver path, with one Information `direct_message.pair_routing_skipped` and its reason; faults log a Warning and take today's path; no owner is written.

## 054b — the five BFF-owned columns (field security + BFF refusals)

### The rule

- Five columns decide who may read a message, and only the BFF's own typed writers write them:
  - `sprk_communicationthread`: `sprk_threadtype`, `sprk_isdefaultthread`;
  - `sprk_communication`: `sprk_sentby`, `sprk_directrecipient`, and the thread lookup `sprk_communicationthread`.
- Creates by the typed writers are allowed. The one UPDATE writer of `sprk_threadtype` / `sprk_isdefaultthread` is `ThreadResolver.AnchorThreadForFilingAsync` (owner decision 2026-11-11, option A). Enforced by the ArchTest `ThreadFlagUpdateWriterGuardTests` (must-fire / must-not-fire controls; the in-memory projection in `RecordFamilyReader.ToNode` is listed with its reason).
- Every generic write path refuses a payload naming any of the five, for every identity and payload source:
  - `InheritedAccessPermission.RefuseAppOnlyWrite` (app-only and caller identity; reason `sdap.access.communication_bff_column_server_only`). A thread's type is also an app-only access column (#2253), which keeps its own code app-only;
  - `RecordTableRules.RefuseFixedAfterCreateColumns`, the one update-side check. That covers:
    - the `/child-records` PATCH core;
    - `OwnedChildWrite.RefileAsync`, which is the chat update tool's re-file and the communication filing route's core;
    - the chat update tool;
    - `UpdateRecordActionCore` (every payload source, including the ConfirmedProposal write impersonated as its user);
    - `DataverseUpdateHandler`;
    - `WorkProductRecordPersister`;
    - the field-mapping push.

    `CoreRecordPairUpdateCheckGuardTests` makes every future update path inherit it;
  - `RecordCreationService.CallerBlockedColumnRefusal` (the filed chat create through the one server create), except the "for" person the server stamps itself (`sprk_sentby` in `ServerSetLookupColumns`);
  - `DataverseCreateRecordHandler` before its run-as-user POST (an unfiled communication, a thread);
  - create-time mapping and Repair drop a rule targeting one (they already ask `RefuseAppOnlyWrite`).
- The filing routes (`/communications/{id}/filing`, the event / child-record filing shape) accept regarding columns only, so they already refused all five (pinned). The generic child-record re-file route does not take `sprk_communication` (pinned).
- `scripts/Set-ThreadFlagFieldSecurity.ps1` secures the same five columns (reader profile on every BU default team; writer profile = the BFF application users only). `CommunicationBffOwnedColumnRefusalTests` reads the script's target lines and keeps them equal to `BffOwnedCommunicationColumns`, with a must-fire control.

### Behaviour change on the generic paths

A message's thread lookup can no longer be moved by a generic update (plain PATCH, re-file core, Update Record node, output mapping, field-mapping push). The #2110 / #2143 tests that moved a thread that way now pin the refusal: nothing is written, and no reconcile runs. A message moves between threads only through the BFF's typed writers (the thread join, the filing reconcile).

### Owner decision (option A, 2026-11-11) and the rule-2 type guard

- **Option A:** `AnchorThreadForFilingAsync` is sanctioned as the one update writer of `sprk_threadtype` (it writes Record-Anchored when a filed email conversation follows its record whole).
- **Rule-2 type guard (A1): HELD, escalated.** The guard as briefed ("a Direct thread (100000001) skips the whole-thread anchor") conflicts with the threads it would hit:
  - `CreateThreadAsync` types every thread it creates without a regarding anchor as Direct (`ThreadResolver.cs:227`), and that includes an inbound email conversation's thread (`EmailThreadKeyStrategy` → `CreateNew`). The type alone cannot tell a resolver email thread from a Direct 1:1 pair thread.
  - Rule 2 exists to anchor exactly those email threads (owner clarification 2026-10-11). `ThreadResolverTests.ReconcileThreadForFiling_DirectThread_IsAnchoredToTheRecord_OwnedByItsTeam_Renamed_AndGrantsFiledMessagesOnly` (the dev repro) pins it. The guard would turn every such filing into an email-only move.
  - F1-c already keeps a thread holding any non-email message (a chat thread) from being anchored. What is left at risk is a strict Direct pair thread that holds only emails, which 054a piece 2b creates for an outbound Direct email send.
  - **Options:**
    - (a) skip the anchor when the thread is Direct **and** is a strict pair, or **and** any of its messages carries `sprk_directrecipient` (stamped only on a Direct pair's messages). This keeps rule 2 for resolver email threads (recommended);
    - (b) give resolver email threads their own type, or leave them untyped (a schema / data decision);
    - (c) accept the reversal: type-only guard, the dev-repro test flips, and filed inbound email threads stop following their record.
  - Until decided, rule 2 is unchanged. The A2 ArchTest still holds the update writer to the one sanctioned member.

### Binding rule (agreed with UAC-r2, 2026-11-11)

`DirectMessage:BusinessUnitName` and `DirectMessage:OwnerTeamName` stay unset on an environment until `Set-ThreadFlagFieldSecurity.ps1 -Verify` exits 0 there. While they are unset, `DirectMessageOwnerTeam` refuses as NotConfigured, so no message is owned by the Direct team and nothing grants from `sprk_sentby` / `sprk_directrecipient`. `-Verify` prints `PRECONDITION MET: DirectMessage:BusinessUnitName and DirectMessage:OwnerTeamName may now be set on <env>` on a pass, and `PRECONDITION NOT MET: leave ... UNSET` on a fail.

### Live order (agreed)

1. Deploy the dev BFF from master.
2. 054b: `Set-ThreadFlagFieldSecurity.ps1` dry run, then `-Apply`, then `-Verify`.
3. Direct setup (UAC-r2: the `DirectMessage:*` settings).
4. 054c Stamp.
5. Piece 3.
6. 054c Move.

### Scan (writers of the five columns in `src/server`)

| Site | Column(s) | Identity | Kind | Verdict |
|---|---|---|---|---|
| `ThreadResolver.CreateThreadAsync` :227 | threadtype | app-only (`IGenericEntityService`) | create | typed writer |
| `ThreadResolver` record-thread create :1678 | threadtype | app-only | create | typed writer |
| `ThreadResolver` default / master thread create :1810, :1813 | threadtype, isdefaultthread | app-only | create | typed writer |
| `DirectThreadAccessService.FindOrCreateDirectThreadAsync` :117 | threadtype | app-only | create | typed writer |
| `ThreadResolver.AnchorThreadForFilingAsync` :1229 | threadtype | app-only | **update** | sanctioned (option A); the one update writer (ArchTest) |
| `RecordFamilyReader.ToNode` :995 | threadtype | none (in-memory node) | — | never sent to Dataverse |
| `DirectMessageRecipient.WriteThreadFilingAsync` / `ThreadResolver.ThreadFilingFields` :2013 | thread lookup, sentby, directrecipient | app-only | update | typed writer |
| `ThreadResolver` join `ParentChanges` :591 | thread lookup | ownership resolver's reparent | update | typed writer |
| `CommunicationService` :1164, :1366, :2024 | sentby | app-only | create (pre-create stamp) | typed writer |
| `EmailDraftToolHandler` :1030 (filed draft) | sentby | one server create (app-only) | create | server-set "for" person (allowed) |
| `DataverseCreateRecordHandler` `WithForPersonAsync` (filed chat create) | sentby | one server create | create | server-set "for" person (allowed) |
| `ChildRecordEndpoints.UpdateAsync` PATCH | any (caller payload) | run as user | update | **refused** (one update-side check) |
| `OwnedChildWrite.RefileAsync` | any (caller / model payload) | run as user | update | **refused** (one update-side check) |
| `DataverseUpdateRecordHandler` (chat update) | any (model payload) | run as user | update | **refused** (:228 and via `RefileAsync`) |
| `UpdateRecordActionCore` (node, action seam, ConfirmedProposal impersonated) | any | app-only or impersonated | update | **refused** (one update-side check) |
| `DataverseUpdateHandler`, `WorkProductRecordPersister`, field-mapping push | any (configuration) | app-only / caller | update | **refused** (push: rule skipped) |
| `DataverseCreateRecordHandler` run-as-user POST | any (model payload) | run as user | create | **refused** (explicit check) |
| `RecordCreationService` step 2 | any (caller payload) | caller | create | **refused** (except the server-set "for" person) |

No BFF path writes `sprk_threadtype` or `sprk_isdefaultthread` as an impersonated caller.

### §11

- **`InheritedAccessPermission.BffOwnedCommunicationColumns`** with `BffOwnedCommunicationColumnIn`, `RefuseBffOwnedCommunicationColumn` and the reason code:
  - **Existing:** `BffOwnedDocumentColumns` (one flat set on the document), `AccessColumns` (table-keyed, app-only identity only), `RecordTableRule.TableServerOwnedColumns` / `CommonServerOwnedColumns` (the create-side caller policy).
  - **Extension:** it extends the existing `InheritedAccessPermission` refusal family, through the same refusal type and the existing checks; there is no new mechanism. Why not extend those sets:
    - `AccessColumns` is asked only for the app-only identity, so the push, the caller identity and a ConfirmedProposal impersonated write would skip it;
    - `TableServerOwnedColumns` would also refuse the server-stamped "for" person (`sprk_sentby`) on the filed chat create and the email draft.

    It is table-keyed because the names collide across tables (`sprk_communicationthread` is both a table and a column).
  - **Cost of doing nothing:** a user-, model- or configuration-built payload could set a message's participants or thread, or a thread's type, through the BFF's generic writers. Those writers run as the BFF application user, past field security, and UAC-r2's share reconcile decides grants from these columns.
- **`CallerBlockedColumnRefusal(..., serverSet)`** (an optional parameter on an existing method). Cost of doing nothing: the server's own "for" person stamp would be refused.
- **`DataverseCreateRecordHandler`**: a refusal on an existing path; no new surface.
- **`scripts/Set-ThreadFlagFieldSecurity.ps1`**:
  - **Existing:** `Set-SecureFlagFieldSecurity.ps1` and `Set-DocumentPointerFieldSecurity.ps1` use the same two profiles.
  - **Extension:** each of those carries column-specific preconditions (null repair, a client-deploy flag) that do not apply here. Generalising them would change scripts already verified live, so this script copies their shape instead.
  - **Cost of doing nothing:** any Dataverse surface outside the BFF (a form, the Web API, an import, a flow running as the user) could change the five columns.
- **`ThreadFlagUpdateWriterGuardTests`**. Cost of doing nothing: the one-update-writer rule (option A) would be prose only.

### Manual checks (after `-Verify` exits 0; they need a non-administrator sign-in)

- (m1) As a non-administrator, PATCH a communication's `sprk_sentby`, `sprk_directrecipient` or `sprk_communicationthread` through the Web API: refused. Repeat on a message the user can write through a record team.
- (m2) As a non-administrator, PATCH a thread's `sprk_threadtype` or `sprk_isdefaultthread`: refused.
- (m3) As a non-administrator, read a Direct message and its thread: the five columns show their true values, not empty.
- (m4) The BFF still creates a per-user master thread and a Direct thread, and files a message into it (thread lookup and both participants set).

### Evidence

- **Tests** (KEEP paths):
  - `tests/integration/data-mutation/ChildAccessPermission/CommunicationBffOwnedColumnRefusalTests.cs`;
  - `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.BffOwnedCommunicationColumns.cs`;
  - updated: `AccessTableAppOnlyWriterTests` (as the caller, a thread's type is now refused), `RefileWriterBindShapeTests`, `SecureChildOwnershipAiToolTests.RefileThreadCaller` and `.ThreadJoin` (the generic thread moves now pin the refusal);
  - ArchTest: `tests/Spaarke.ArchTests/ThreadFlagUpdateWriterGuardTests.cs`.
- **Fail-then-pass** against 89274213eb. The three wiring files were checked out from it, and the one `RefuseAppOnlyWrite` call was removed. The new set and its helpers stayed so the tests compile, and the optional `serverSet` parameter was added to the old signature. The filter was the new and changed tests:
  - Before: **Failed 137, Passed 40, Total 177.** The 40 that pass are:
    - the must-not-fire cases;
    - the set, script and typed-writer pins;
    - the filing-shape pins;
    - the four `sprk_threadtype` forms that the #2253 access-column refusal already refused app-only.
  - After: **Passed 177, Failed 0.**
- **Builds:** `Sprk.Bff.Api` and `Sprk.Bff.Api.Tests` each have 0 warnings and 0 errors.
- **Filtered run:** filters `Communication`, `ChildRecord`, `OwnedChildWrite`, `DataverseCreateRecord`, `ParentLineage`, `DataMutation`, `RecordCreation`, `UpdateRecord`, `ThreadResolver`, `DirectMessage` and `FieldMapping`, excluding `Api.Office.`:
  - **9924 passed, 1 failed, 4 skipped.** The one failure was `ProvisionProjectIdempotencyTests.ProvisionProject_WhenRecordingTheContainerTimesOut_FailsLoudlyAndLogsTheContainerId`, a 1-minute timeout while the ArchTests ran in parallel. Re-run alone: 55 of 55 pass.
- **ArchTests:** **2148 passed, 0 failed.**
- **`dotnet format --verify-no-changes`** on the changed files: clean, except a pre-existing IMPORTS ordering finding at line 1 of `DataverseCreateRecordHandler.cs` (its usings are untouched).
