# Task 138 — re-saving an email to the same record adds the attachments not saved the first time (#2209, ISS-022)

Design note, written before code (POML step 1). The save path and the worker are fm-coordinated (W3): the server change
does not merge before field-mapping-r1 answers.

## 1. What happens today (traced 2026-11-25, origin/master `0267ee64a2`)

1. `OfficeService.SaveCoreAsync` resolves the key (`ResolveIdempotencyKey`: the body key, else `GenerateIdempotencyKey`).
   The Email key is content type | target type | target id | stored Message-ID. It has no attachment selection.
2. `_jobs.FindExistingAsync(key, caller)` returns the caller's newest non-failed job under that key. When one is found,
   the save returns `{ success, duplicate: true, jobId }` (200) **before** the email capture, the owner question, the
   job row, the upload and the document create. Nothing else runs.
3. On a first save, `QueueUploadFinalizationAsync` hands the worker the `.eml`'s document id, its SPE item, the owner
   team, the communication id and `EmailMetadata.SelectedAttachmentFileNames`.
4. `UploadFinalizationWorker.ProcessEmailAttachmentsAsync` downloads the stored `.eml`, extracts its attachments
   (`IEmailToEmlConverter`), drops noise (`AttachmentFilterService`: inline parts, signature images, tracking pixels,
   blocked extensions, size), applies the selection (`null` = all, `[]` = none, names = those, case-insensitive), and
   calls `ProcessSingleAttachmentAsync` for each. That method is the one child create:
   - uploads to `{parentDocumentId:N}_{sanitized name}` (Graph path-keyed PUT, so the same parent and name always land on
     the same drive item);
   - creates the child with the parent link, the SPE pointer, the association, the owner team and the communication
     link (or Restricted) all in the create (030b G1–G6);
   - names the child `sprk_documentname` = the attachment's file name.
5. The pane's header key (`buildIdempotencyCanonical`) includes the selected attachment ids, so a re-save with a
   different selection passes `IdempotencyFilter`'s response cache and reaches step 2, where it is answered "duplicate".

So a second selection is never acted on. The pane does not even show "already saved": the duplicate body has no
`documentId`, so `isDuplicateSaveResponse` is false, and the pane tracks the old job to "Saved to Spaarke".

## 2. Design

### 2.1 The key is unchanged

Adding the selection to the key would make the A+B re-save a new save, with a second `.eml` (POML constraint). Not done.

### 2.2 Server: the duplicate branch decides between "already saved" and "add"

When the save finds an existing job, and **all** of these hold, it computes what is missing:

- the save is an Email save from `/office/save` (`submission is null`: the from-email route never adds);
- the selection is not `[]` (empty means none, so there is never an "add"; ebilling's route forces `[]`);
- the request names attachments at all (`Attachments` or `SelectedAttachmentFileNames` non-empty: the worker's own
  `HasAttachments` gate). A save that names none creates no children, so its re-save has nothing to add and reads no
  file (`OfficeVersionSaveRevertTests` pins "an immutable message's duplicate is decided by its key alone");
- the existing job is Completed and its result names the document it created **with its own SPE item**
  (`Result.Artifact.SpeFileId` set). A content-dedup job (`DeduplicatedToExisting`) carries no file id and keeps today's
  duplicate answer: its document is another save's canonical;
- the `.eml` document still exists and still points at that item (one `GetDocumentAsync` read). A deleted `.eml` keeps
  today's answer: children are never created under a missing parent.

"What is missing" is decided from the stored state, not from the client (POML constraint):

```
missing = select( filter( extract(stored .eml) ), selection )  minus  names of the existing children of the .eml
```

`extract`, `filter` and `select` are the worker's own steps (same converter, same filter, same selection rule — the
selection rule becomes one shared static helper so the two cannot drift). The existing children are read with the
existing `IDocumentDataverseService.GetDocumentsByParentAsync`; a child counts as saved when its `sprk_documentname` or
`sprk_filename` equals the attachment's name (case-insensitive, the selection's own comparison). This is the identity the
worker already writes; there is no other per-attachment identity on the child.

- `missing` empty → today's duplicate answer, unchanged (200, `duplicate: true`).
- `missing` not empty → the save continues down the ordinary email path: the capture (idempotent on the Message-ID),
  task 121's filing, the communication resolution and the owner question (task 080 / 194b R4-2), all unchanged. It stops
  where a first save would build the `.eml`: no upload, no document create. Instead it creates a job row (same key, so a
  later re-save finds it as this email's newest job, with the same result) and queues an **attachments-only**
  finalization for the existing `.eml`, with the selection set to the missing names, the owner team and the
  communication just resolved. It answers 202 with `attachmentsAdded: [names]`.

The owner team and the communication link of the new children therefore come from the same code that gave the `.eml`
and its first children theirs. There is no owner, stamp or mapping logic in this task.

### 2.3 Worker: an attachments-only mode, reusing the child create

`UploadFinalizationPayload` gets `AttachmentsOnly` (omitted from the JSON when false, so every existing message is
byte-identical). In that mode the worker:

- requires `DocumentId` (never the fallback create, which would make a second `.eml`);
- skips the email artifact, the `.eml`'s profile/index stage and the temp-file step (all done by the first save);
- runs `ProcessEmailAttachmentsAsync` → `ProcessSingleAttachmentAsync` exactly as today, with one added step: it re-reads
  the `.eml`'s children and drops attachments already saved, just before creating;
- marks its message processed under its own key and completes the job.

No new create path: every child is created by `ProcessSingleAttachmentAsync`.

### 2.4 Concurrency (no new lock type)

Two identical add re-saves create each missing child at most once, by three existing mechanisms:

1. `IdempotencyFilter` holds an in-flight lock per header key; two concurrent identical pane requests carry the same
   header key, so the second gets 409.
2. The worker checks the stored state (the existing children) just before creating, as task 047 checks what the
   document holds. A second add job that runs after the first finds the child and creates nothing.
3. Two add jobs that run at the same moment upload the same attachment to the same path, so the same drive item, and the
   second create is refused by the `sprk_graphitemid_uk` alternate key (NFR-07). The refusal is caught per attachment,
   as every attachment failure is today, and logged.

### 2.4a Authorization: fail closed, as a fresh save (fm-r1 condition 4)

What runs on `POST /api/office/save` today, in order: `OfficeRateLimitFilter`, `IdempotencyFilter`, `OfficeAuthFilter`,
`EntityAccessFilter` (`entity.associate_document` on `TargetEntity`), `OfficeVersionSaveAuthorizationFilter` ("write" on
the existing document — **version saves only**, so it does not run for an email save).

- **Target record.** `EntityAccessFilter` runs on every request, a re-save included, before `SaveAsync`. A caller who can
  no longer file to the record is refused there, exactly as a fresh save is; nothing in the add path runs. Pinned by
  `ReSave_ByACallerWhoMayNoLongerFileToTheRecord_IsRefused_AndQueuesNothing`.
- **The saved `.eml`.** Nothing checked it before this task (the duplicate answer wrote nothing). The add writes children
  under it, so the add path checks "write" on it with the caller's CURRENT rights, through the one document-grant helper
  outside `DocumentAuthorizationFilter` (`InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync`, the
  `OfficeVersionSaveAuthorizationFilter.Operation` "write"). A deny, or an exception from the check, refuses the save
  with `OFFICE_009` (403) **before** the capture, the job row and the queue: nothing is created. Pinned by
  `ReSave_ByACallerWhoLostWriteOnTheSavedEmail_IsRefused_AndQueuesNothing`.
- **The container.** The add re-derives the container as a fresh save does (`ResolveContainerAsync` on the target); a
  refusal there is returned as a fresh save's refusal, and a container that is not the `.eml`'s drive keeps the duplicate
  answer (the children are never put in another drive than their parent).
- **Owner.** Resolved by the save's own `ResolveOwnerAsync` over `OfficeDocumentPersistence.DocumentOwnershipOf` on the
  same association (target, communication, caller); always a team, never the user (fm G3). The add adds no owner logic.
  **New children follow the record's CURRENT owner**, not the `.eml`'s owner of the first save: ownership is
  record-first, so an add after the record changed hands (or became secure, which also changes the container, see above)
  creates its children under the owner a fresh save would give them today. This is intended, the same as a fresh save,
  and field-mapping-r1 accepted it.

### 2.4b fm-r1 conditions (2026-11-25)

1. Nothing of fm's in flight touches the duplicate path or the child loop — taken as given.
2. Every child is created by the existing `ProcessSingleAttachmentAsync`: G1 (association in the create), G3 (owner team
   from the save's resolution), G5 (server-minted id, plain create) and G6 (`FilePointer` and `ParentDocumentLookup` =
   the existing `.eml`'s id in the create) hold unchanged; `OfficeDocumentSaveGuardTests` stays green.
3. Full ArchTests (built) and the filtered Office / RecordOwnership / DocumentAssociation BFF tests run before merge.
4. §2.4a.

### 2.5 Pane

`SaveResponse` gains `attachmentsAdded` (omitted when null; additive). The pane keeps it for the save in flight, as task
131 keeps `emailFiling`. The save marks its job Completed when the finalization is QUEUED, before the worker has made any
document, so the pane never says "Added": when the job completes the saved bar's title reads "Adding N attachment(s) to the
saved email. If they don't appear on the record in a few minutes, save the email again." (see §6, item H1). Every other
answer, including "already saved", is unchanged. (Round 1c adds `attachmentsNotStored`, §6 item G.)

## 3. Acceptance mapping

| AC | Test |
|---|---|
| A then A+B to M → exactly one new document (B) under the same `.eml`, linked to the communication; no second `.eml` | save: one attachments-only payload naming B, no upload, no create; worker: creates B only, parent = `.eml`, linked |
| A only again → "already saved", nothing created | save: 200 duplicate, no new job, no payload |
| `null` → every attachment not yet saved, only those | save: payload names exactly the unsaved, unfiltered ones |
| from-email route (`[]`) unchanged | submission save with missing children still answers its duplicate; existing submission tests |
| two concurrent add re-saves → B once | worker: two jobs racing past the check → one child (the alternate key), and a later job → none |
| 030b G1–G6 and the Office save tests unchanged | existing suites |

## 4. Known limits (not defects of this design)

- **Quick Save copy.** Quick Save sends every non-inline attachment and its own body key. Its add answer shows its normal
  "Saved to Spaarke and filed to …" sentence, not the pane's "Adding N" text (the POML scopes the text to the pane).
- **Duplicate attachment names (corrected 2026-11-25).** Two attachments with the same file name in one email map to one
  child name and one SPE path (`{parent}_{name}`, uploaded with Replace). This note first said the second is refused by the
  alternate key; that was wrong. The second upload REPLACES the first's bytes in the same drive item, so the two
  attachments share one document that holds the later one's content. This is pre-existing, lives in the worker's upload
  path (`ProcessSingleAttachmentAsync`), is not changed by this task, and is filed as #3135.
- **`IdempotencyFilter` replay.** Repeating the exact same add request within 24 h replays the 202 (and its "Adding N"),
  as every repeated save replays its first answer today.
- **Container changed.** A record whose documents now belong in another container than the `.eml`'s keeps the duplicate
  answer (nothing is added there).
- **Rolling deploy.** A message queued by this build and read by an older worker (which does not know
  `AttachmentsOnly`) is not an add; the window is the deploy's.
- **A 0-byte attachment cannot reach a stored `.eml` through the endpoint.** `OfficeEmailEnricher.BuildEmlFromMetadata` skips
  an attachment with no content, so item D's "can be created" rule (§6) guards the planner/worker contract and an `.eml`
  stored by another path; it is not a path the endpoint produces today.

## 5. Placement and §11

- **Existing**: the worker's attachment child creation (reused), the save's duplicate branch (extended), the selection
  rule (shared). **Extension**: the planner is a read-only scoped class in `Services/Office/` because both the request
  path and the worker need the same "what is missing" answer; putting the SPE download, MIME parse and child read into
  `OfficeService` would grow an already large class with a second concern. **Cost of doing nothing**: a user cannot add a
  missed attachment to an email already saved to a record; the second save silently adds nothing.
- One new scoped registration (`EmailAttachmentAddPlanner`), dependencies all registered unconditionally; no new
  endpoint, no new package, no new background service.

## 6. Fix round 1 (A–H) as built

Rounds 1a, 1b and 1c on branch `fix/word-addin-138`, after review (uac-r2, field-mapping-r1 and the review pass).
fm-coordinated files (`OfficeService`, `UploadFinalizationWorker`) keep fm's four conditions (§2.4b): children are created
only by `ProcessSingleAttachmentAsync`, whose create call is unchanged.

- **A. The `.eml` must still be filed to the request's record** (`EmailAttachmentAddPlanner.IsFiledTo`). The match to the
  earlier save is made by an idempotency key, which the client may send and which says nothing about WHERE the email is
  filed. The planner reads the `.eml`'s current filing (`DocumentLinkFields`, both the `sprk_*` and `sprk_related*`
  families) and plans an add only when the links of the request's own record TYPE are present and every one of them is the
  request's record; otherwise the duplicate answer, nothing queued. **Deviation from the brief, accepted by uac-r2:** the
  comparison is scoped to the request's record type. An `.eml` that is also filed to a record of another type (a matter and
  a project, say) is still "filed to" the matter the request targets; the brief's wording compared the whole filing.
- **B. Rights before any app read.** `GetEmailAttachmentAddCandidate` (cheap gates, no read) runs first, then
  `AuthorizeEmailAttachmentAddAsync`, then planning. A denied caller gets 403 `OFFICE_009` whenever the request could add
  (an Email save, a selection that is null or non-empty, not from-email), whatever is stored: no row read, no download, no
  child listing. A decision that faults refuses (fail closed). The older "duplicate answer for a caller who lost access"
  behaviour is unchanged.
- **C. The refusal's text reaches the user.** `MapSaveErrorToProblem` renders this save's `OFFICE_009` with the error's own
  message as the problem detail; the fixed sentence stays for a refusal that carries none.
- **D. A shared "can this be created" rule** (`CanBeCreated`: the attachment has content). An attachment the worker would
  skip as "no content" is never counted missing.
- **E. One selection path** (`EmailAttachmentAddPlanner.SelectAttachments`: converter, noise filter, selection, "can be
  created"). The planner and the worker both call it; the worker also applies `ExcludeAlreadySaved` before creating.
- **F. No download when nothing can be missing.** The planner reads the `.eml`'s children first and downloads the `.eml` only
  when the selection is null or names a file with no child yet.
- **G. A ticked attachment the stored `.eml` does not hold** (the first save left it out: unreadable or too big then). The
  answer lists those names in `attachmentsNotStored` (omitted when empty), on the add answer and on the duplicate one. The
  pane says so in plain words ("These attachments could not be added because they were not saved with the email: B. Save
  the email again as a new copy to include them."). Nothing is created for them from the request's bytes: the app creates
  children only from the stored `.eml`.
- **H1. Honest wording.** The save marks the job Completed when the finalization is queued, before the worker runs, so the
  pane never says "Added". It says "Adding N attachment(s) to the saved email. If they don't appear on the record in a few
  minutes, save the email again." A failed add leaves its names without children (and no processed mark), so the next
  re-save plans the same names again. `OfficeJobStatusService`, `ToEffectiveView`, the `sprk_result` semantics and the
  5-minute rule are unchanged.
- **H2. No add while the matched job is still finalizing.** `EmailAttachmentAddPlanner.IsFinalizationPendingAsync`: the
  matched job is unfinished when the worker's processed mark (`UploadFinalizationWorker.ProcessedMarkResource`, the existing
  `ITenantCache` mark, read under the message key the job was queued with: the save's key for a first save, the add job's own
  key for an `AttachmentsQueued` job) is absent AND the job is younger than `OfficeJobStatusService.MaxSaveDuration` (the
  existing abandoned age, reused). While unfinished the answer is the plain duplicate one. Once the mark is set, or the job
  is older, planning proceeds; the children re-read and the add job's own message key keep a repeat from duplicating work.
  Without it, a re-save made while the first worker runs sees no children and counts every attachment missing.
- **I. Fail-closed branches tested:** a faulting access decision, a changed container and an unreadable stored file each end
  in a refusal or the duplicate answer with nothing queued.

## 7. Open with the owner

The service-level rights check on the existing `.eml` (`AuthorizeEmailAttachmentAddAsync`) exists, and how it is resolved
under ADR-008 is pending the owner's decision. This note records no path.
