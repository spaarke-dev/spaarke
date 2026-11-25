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

### 2.5 Pane

`SaveResponse` gains `attachmentsAdded` (omitted when null; additive). The pane keeps it for the save in flight, as task
131 keeps `emailFiling`, and when the job completes the saved bar's title reads "Added N attachments to the saved email"
(N = 1 → "attachment"). Every other answer, including "already saved", is unchanged.

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
  "Saved to Spaarke and filed to …" sentence, not the "Added N" text (the POML scopes the text to the pane).
- **Duplicate attachment names.** Two attachments with the same file name in one email map to one child name and one SPE
  path. Today the second is refused by the alternate key; that is unchanged.
- **`IdempotencyFilter` replay.** Repeating the exact same add request within 24 h replays the 202 (and its "Added N"),
  as every repeated save replays its first answer today.

## 5. Placement and §11

- **Existing**: the worker's attachment child creation (reused), the save's duplicate branch (extended), the selection
  rule (shared). **Extension**: the planner is a read-only scoped class in `Services/Office/` because both the request
  path and the worker need the same "what is missing" answer; putting the SPE download, MIME parse and child read into
  `OfficeService` would grow an already large class with a second concern. **Cost of doing nothing**: a user cannot add a
  missed attachment to an email already saved to a record; the second save silently adds nothing.
- One new scoped registration (`EmailAttachmentAddPlanner`), dependencies all registered unconditionally; no new
  endpoint, no new package, no new background service.
