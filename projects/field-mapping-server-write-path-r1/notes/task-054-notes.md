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
