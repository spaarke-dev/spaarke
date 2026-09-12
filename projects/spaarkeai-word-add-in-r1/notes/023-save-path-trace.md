# Task 023 — `POST /api/office/save` Document-path trace (written BEFORE any code change)

> Step 1–3 of the prescriptive POML. Base: `37494ef9c` (project branch head). Line numbers are as of that
> commit; the POML's 2026-09-04 numbers had drifted by 0 lines for `SaveAsync` itself.

## 1. The as-built Document path (step 1)

`OfficeEndpoints.MapSaveEndpoints` → `POST /api/office/save`, filters in registration order (outermost first):
`OfficeRateLimitFilter(Save)` → `IdempotencyFilter` (X-Idempotency-Key response cache) → `OfficeAuthFilter`
(sets `HttpContext.Items[UserIdKey]`) → `EntityAccessFilter` (AppendTo on `TargetEntity`, via
`CallerRecordAccessProbe`; `next()` when no target). Handler `SaveAsync` → `ValidateSaveRequest`
(friendly entity names; Document requires `request.Document`) → `IOfficeService.SaveAsync`.

`OfficeService.SaveAsync` (`Services/Office/OfficeService.cs:168-605`), Document content type:

| # | Line | What happens |
|---|---|---|
| 1 | 204 | `idempotencyKey = request.IdempotencyKey ?? GenerateIdempotencyKey(request)` |
| 2 | 208 | `CheckForExistingJobAsync(key)` — a Dataverse `sprk_processingjob` query by `sprk_idempotencykey` (PERSISTENT, no TTL). Hit → `Duplicate=true`, 200, **nothing written**. |
| 3 | 233 | Email-only capture (skipped for Document) |
| 4 | 239 | `jobType = DocumentSave` |
| 5 | 281 | `derivedContainerId = ResolveContainerAsync(request)` (`:117-165`) — from the authorized `TargetEntity` via `RecordContainerResolver`, else `EmailProcessing:DefaultContainerId`; FailClosed for a secure record with no container |
| 6 | 284-322 | ProcessingJob row created (payload carries `ContainerId = derived`, and `Document = request.Document` — i.e. the three version fields AND the full `ContentBase64` already ride in the payload JSON) |
| 7 | 376-409 | Document branch: base64 decode → `MemoryStream`; `fileName = SpeUploadPath.SanitizeFileName(request.Document.FileName)` (the folder-minting fix) |
| 8 | 421 | `_storageUploader.UploadToSpeAsync(containerId, fileName, stream)` → `SpeFileStore.UploadSmallAsync(driveId, path, content)` → **app-only, PATH-keyed** `PUT /drives/{d}/root:/{name}:/content` with `conflictBehavior=replace` |
| 9 | 455 | `_documentPersistence.CreateDocumentWithSpePointersAsync(...)` — Document is EDITABLE (task 028): `ContentDedupDetector.ResolveContentIdentityAsync` + `GraduateLinkedCopyIfDivergedAsync(itemId, hash)` then **always `CreateDocumentAsync`** + `UpdateDocumentAsync` (pointers, size, path, hash, association) + optional `sprk_canonicaldocument` link. Returns `(newId, false)` for Document. |
| 10 | 473-498 | `wasContentDuplicate` short-circuit (delete transient blob) — unreachable for Document since 028 (only the immutable branch returns `true`) |
| 11 | 510-526 | Membership `Added` event for the new row's `ownerid` |
| 12 | 538 | `_jobQueue.QueueUploadFinalizationAsync(..., documentId)` → `UploadFinalizationWorker`: uses `payload.DocumentId` (no row created), no artifact for Document, then `AppOnlyDocumentAnalysis` + RAG jobs (app-only) |

**Consequence today**: every Word save creates a NEW `sprk_document`. A re-save under the same sanitized name
lands (path-keyed replace) on the SAME drive item and then tries to create a SECOND row for that item — the D1
`sprk_graphitemid_uk` collision that spike-4 documented (owned by task 025, not this task).

## 2. Empirical grep — `ExistingDocumentId` / `IsNewVersion` / `VersionComment` in `src/server` (step 2)

```
Models/Office/SaveRequest.cs:364   public bool IsNewVersion { get; init; }
Models/Office/SaveRequest.cs:369   public Guid? ExistingDocumentId { get; init; }
Models/Office/SaveRequest.cs:375   public string? VersionComment { get; init; }
Services/Office/OfficeService.cs:620   $"{request.Document?.ExistingDocumentId}";   ← idempotency canonical string
Models/CheckoutModels.cs:23           string? VersionComment,     ← UNRELATED: CheckInResponse (checkout/check-in surface)
Services/DocumentCheckoutService.cs:320   VersionComment: comment, ← UNRELATED: echoes the check-in comment into CheckInResponse
```

**F-d confirmed, no contradiction.** The only server reference to the three `DocumentMetadata` fields is the
idempotency canonical string. The two `VersionComment` hits are a same-named property on a different model
(`CheckInResponse`) on the checkout surface; they are not reads of `DocumentMetadata.VersionComment`.
Additionally the three fields are *serialized* (never read) into the ProcessingJob payload because the whole
`request.Document` is. No escalation.

**Idempotency key confirmed** (`:611-625`): `ContentType|EntityType|EntityId|messageId-or-subject|attachmentId|FileName|ExistingDocumentId`.
A version save and a fresh save of the same filename DO hash differently. **But** two version saves of the SAME
document (same file name, same target) hash IDENTICALLY — the canonical string carries no content component —
and step 2 above is a persistent Dataverse lookup. So the SECOND revision a user saves would be answered
`Duplicate=true` with the first save's job and **never written to SPE**: silent loss of the revision. The
version path must therefore key on the content too (see §4 D-3).

## 3. SPE versioning through `SpeFileStore` (step 3)

| Candidate | Facade? | Addresses | Verdict |
|---|---|---|---|
| `UploadSmallAsync(driveId, path, content[, Replace])` (app-only) | yes | a PATH | **Rejected.** Path-keyed: if the item was renamed, or the user typed a different name, it mints a SECOND item — the exact rename-approximation the POML forbids. |
| `ReplaceFileContentAsUserAsync(ctx, driveId, itemId, content[, ifMatch])` (OBO) | yes (`SpeFileStore.cs:296`, `UploadSessionManager.cs:432-575`) | the ITEM ID | **Chosen.** `PUT /drives/{driveId}/items/{itemId}/content`. SharePoint commits the bytes as a **new version of the same drive item**: item id unchanged, previous content kept in the item's version history. Same call Compose's save-back uses for "update in place, never mint a duplicate" (`ComposeSaveStorageCoordinator.cs:289`). Typed errors already translated inside `Infrastructure.Graph` (ADR-007): 404 → `null`, 403 → `UnauthorizedAccessException`, 423/resourceLocked → `DocumentLockedByWordException`, 429 → `GraphThrottledException`, other → `InvalidOperationException`. |
| An app-only replace-by-item-id | **no** | — | Does not exist on the facade. Adding one is new facade surface (CLAUDE.md §11) when an existing call already expresses the operation. Not added. |

Escalation trigger 2 does **not** fire: the facade expresses a same-item version write.

**Why OBO (the caller's identity) is the right writer here, not a compromise.** (a) The add-in user is, by
construction, editing this drive item — Word reached it in SPE as that user — so the delegated identity holds write
on it. (b) SPE then enforces the caller's own write permission on the item, as defence in depth beneath the
Dataverse `write` gate. (c) An app-only write would bypass SPE's ACL entirely and rest the whole decision on the
filter. **Writer-identity note** (`.claude/patterns/auth/spe-writer-identity-matching.md`): the downstream
finalization AI/RAG jobs read app-only. Word's own autosave already writes versions of these items as the user, so
this does not introduce a new writer-identity mix on the item; but whether app-only reads of a user-written
version succeed was NOT verified live here — recorded as unverified in the task report.

**Test seam**: `ReplaceFileContentAsUserAsync` is not `virtual`, so `Mock<SpeFileStore>` cannot substitute it and a
test would fall through to real Graph. Making it `virtual` is the established idiom on this facade
(`UploadSmallAsync`, `DownloadFileAsync`, `DeleteFileAsync`, `CreateSharingLinkAsUserAsync`,
`CreateContainerAsync` — each made virtual for exactly this reason, ADR-038 §4). One keyword; no behaviour change.

## 4. Decisions for steps 4–8 (recorded before coding)

- **D-1 Scope guard** — version path iff `ContentType == Document` **and** `Document.ExistingDocumentId` has a
  non-empty value. Checked on the content type explicitly, never inferred from `request.Document` being null.
  Email/Attachment never read the field.
- **D-2 Target resolution** — before any ProcessingJob or SPE write: `GetDocumentAsync(id.ToString("D"))`
  (ADR-044: a typed `Guid` formatted "D" is bare-lowercase by definition; the SDK retrieve takes a `Guid`, never an
  OData key string). Null → `OFFICE_016` (404). Row with no `sprk_graphdriveid`/`sprk_graphitemid` →
  `OFFICE_017` (409), never a fallback to creating a new item.
- **D-3 Idempotency** — the version path appends a SHA-256 of `ContentBase64` to the canonical string, so two
  different revisions are two operations while a retried identical request still dedups. The canonical string for
  every other request (Email, Attachment, non-version Document) is byte-for-byte unchanged.
- **D-4 Container** — the version path writes to the target row's OWN drive (`sprk_graphdriveid`), not a
  container derived from `TargetEntity`; `ResolveContainerAsync` is not consulted on the version path (the
  destination is the existing item, and a derived container could only disagree with it).
- **D-5 No create** — the version path never calls `CreateDocumentWithSpePointersAsync`. It refreshes only
  `sprk_filesize`, `sprk_filepath`, and `sprk_filename` when the SPE item's name differs — mirroring
  `ComposeCreateOnSavePromoter.PromoteIfEphemeralAsync`'s existing-row branch. Identity columns
  (`sprk_graphitemid`, `sprk_graphdriveid`, association lookups, `sprk_documentname`, `sprk_canonicaldocument`)
  are never written. No membership `Added` event (no new row, no ownership change).
- **D-6 Dedup** — see §5.
- **D-7 `IsNewVersion`** — see §6.
- **D-8 Finalization** — `QueueUploadFinalizationAsync(..., documentId: <existing id>)`, with the existing
  drive/item ids.
- **D-9 Authorization** — see §7.

## 5. Dedup interaction (step 6)

The version path does **not** call `ContentDedupDetector.ReconcileAsync` (the immutable suppress mode) and does
**not** run the editable create-branch link. So a byte-identical re-save can never be reported as a duplicate, can
never trigger `DeleteFromSpeAsync` on the version just written, and can never redirect the user to a different
canonical. What it does call is the seam task 028 left for it: `GraduateLinkedCopyIfDivergedAsync(itemId, liveHash)`
with the live `quickXorHash` read through `ResolveContentIdentityAsync`. If the target row is a hash-linked COPY
whose content has now diverged from the hash it was linked at, the link is severed and the new hash stamped (it
graduates to its own canonical); if the bytes are still identical, the link stands. A true canonical is left
untouched — `sprk_canonicalhash` is an identity column on that branch, exactly as Compose's existing-row branch
leaves it. `ContentDedupDetector.cs` is not modified (escalation trigger 4 does not fire).

## 6. `IsNewVersion` and `VersionComment` (step 7)

- **SPE cannot carry a comment through this call.** `PUT …/items/{id}/content` has no version-comment parameter;
  Graph's commented check-in (`POST …/checkin {comment}`) needs a prior checkout and is not on the facade.
- **`sprk_document` has no column for one.** Verified against `docs/data-model/field-mapping-reference.md`
  (description, status, checked-in/out date+by — no comment/notes column). Writing it into
  `sprk_documentdescription` would overwrite a user-visible field and is rejected. `sprk_fileversion` (the
  platform's version entity, `sprk_currentversionid`) has no documented column inventory in-repo, and the check-in
  path itself stores no comment ("FileVersion stores statuscode only").
- **What this task does**:
  - `IsNewVersion` becomes an **explicit-intent guard** on the version branch: `ExistingDocumentId` with
    `IsNewVersion == false` is refused with `OFFICE_018` (400). A request naming an existing document while
    declaring "not a new version" is ambiguous, and both guesses are data-integrity faults (a second row, or an
    overwrite nobody asked for). A save WITHOUT `ExistingDocumentId` is untouched whatever `IsNewVersion` says (the
    goal's "behaves exactly as today" binds).
  - Both are **recorded on the `sprk_processingjob` row** for that save (the payload records them, and the version
    path names the job `Document Version Save`), so each revision's comment is retained against the job that
    wrote it.
  - Recording `VersionComment` on the **document** row needs a schema decision (a new column on `sprk_document` or
    `sprk_fileversion`). That is surfaced for a human decision in the task report, not invented here.

## 7. Authorization (ADR-008)

`AddOfficeVersionSaveAuthorizationFilter()` on the save route, after `AddEntityAccessFilter()`. For a version save
it binds the canonical id into `RouteValues["documentId"]` (refusing if already bound — the 012 guard) and
delegates to the unchanged `DocumentAuthorizationFilter(authService, "write")` → `AuthorizationService` →
`OperationAccessPolicy["write"] = AccessRights.Write` on the `sprk_document` row. The same filter and operation as
`PUT /api/v1/documents/{id}` and the checkout family. For every other request it calls `next()` untouched. No
inline permission check inside `SaveAsync`.

**Unknown id vs unauthorized.** `DataverseAccessDataSource` fails closed to `AccessRights.None` for a record
that does not exist, so an unknown id is answered by the filter with 403 — indistinguishable from "not
authorized". This is deliberate (no record-enumeration oracle; the same conflation `EntityAccessFilter` and task 022
keep). The distinct `OFFICE_016` 404 fires when authorization passes but the row cannot be resolved (deleted between
the gate and the read). See the task report for the AC5 reading.

## 8. Placement Justification (`.claude/constraints/bff-extensions.md` — for PR #960)

| Criterion | Answer |
|---|---|
| Latency/TTFB budget against BFF state? | **YES** — the user is waiting on the pane's Save; the version write sits in the same synchronous request as the existing save (≤3 s target, ADR-001). |
| Writes BFF-managed state in the same request lifecycle? | **YES** — the ProcessingJob row, the in-memory job store the pane polls, the idempotency record, and the `sprk_document` refresh, all in the one save request. |
| Retroactive annotation of a stream? | NO. |
| Event-driven, no synchronous user wait? | **NO** — so Functions is the wrong home. |

**Placement: in the BFF, inside the existing `POST /api/office/save`.** No new route, no new package, **no new DI
registration** (ADR-010: `SpeFileStore`, `OfficeStorageUploader`, `OfficeDocumentPersistence`, `AuthorizationService`
are all already registered; the new filter is constructed per-request in its extension method like its siblings),
no CRUD→AI dependency (ADR-013 — finalization still hands AI work to the existing worker). ADRs binding the design:
ADR-001, ADR-007 (version write through `SpeFileStore` only), ADR-008 (target authorization on an endpoint filter),
ADR-010, ADR-019 (distinct ProblemDetails codes), ADR-044 (canonical GUIDs), ADR-038 (contract + data-mutation
tests). §G config boundary: not applicable — no playbook/Action/Node configuration is touched.

## 9. CLAUDE.md §11 — why extend `SaveAsync` rather than add a route

1. **Existing** — `SaveRequest.DocumentMetadata` already declares `ExistingDocumentId`/`IsNewVersion`/`VersionComment`
   (§2 grep: the only server reference was the idempotency string). The save spine already owns idempotency,
   ProcessingJob creation, the in-memory job store, SPE write, the dedup gate and the finalization queue.
   The nearest analogue for existing-row handling is `ComposeCreateOnSavePromoter.PromoteIfEphemeralAsync`, on the
   Compose spine; the item-keyed SPE write already exists as `SpeFileStore.ReplaceFileContentAsUserAsync`.
2. **Extension** — Yes, extend. A second route would duplicate every one of those responsibilities and give the
   add-in two save endpoints whose job/idempotency/finalization behaviour must stay in lockstep. The only new
   surface is what the existing components cannot express: a body-keyed locator in front of the existing
   `DocumentAuthorizationFilter("write")` (that filter reads route values only), two methods on
   `OfficeDocumentPersistence`, one on `OfficeStorageUploader`, and four error codes.
3. **Cost of doing nothing** — every re-save of a Spaarke-sourced Word document creates a SECOND `sprk_document`;
   the record card and profile follow whichever row is newest and the earlier row's associations, profile and index
   entry are stranded (spec Success Criterion 4). And without the content-keyed idempotency, the second revision of
   a version save would be silently dropped as a "duplicate" of the first.

## 10. Contract notes for task 024 (the client half)

- Send `document.existingDocumentId` **and** `document.isNewVersion: true` together; `existingDocumentId` with
  `isNewVersion` false is refused (`OFFICE_018`). "Save as new document" = omit `existingDocumentId`.
- Do **not** send a stable `X-Idempotency-Key` header or body `idempotencyKey` per document: a client-supplied key
  replaces the server's content-aware key, and a key reused across revisions would replay the first revision's
  response. Mint one per save attempt, or send none.
- New refusals to render: `OFFICE_016` 404 (target gone), `OFFICE_017` 409 (target has no file), `OFFICE_018` 400,
  `OFFICE_019` 423 (locked — retryable), plus `OFFICE_009` 403 when SPE itself refuses the caller's write. An
  unknown or unauthorized `existingDocumentId` is answered **403** by the filter.
- The job phases are the ones the pane already renders (`FileUploaded`, `RecordsCreated`, `Complete`).

## 11. What running the suite found (§F.2 / §F.3) — test arrangement, not product defects

1. **Every version save 403'd with `AccessChecks` empty.** `AuthorizationService` fails closed with
   `sdap.access.deny.no_caller_token` when a caller-scoped check has no bearer token. Task 016's `TestAuthHandler`
   authenticates WITHOUT an `Authorization` header. Fixed in the derived factory only
   (`ConfigureClient` adds a bearer token — the `DocumentIdentityContractTests` arrangement). Production is correct.
2. **The "identical re-send" wrote a 4th version.** `VersionSave()` minted a fresh random `TargetEntity.EntityId` per
   call, so the re-send was NOT identical — a different body (IdempotencyFilter passes) and a different server key.
   Fixed by re-posting the same request object.
3. **ArchTest `Task 083 Rule A` failed** on the new `OfficeStorageUploader.cs` `ReplaceFileContentAsUserAsync` sink.
   That is the guard working. Added a `ServerDerivedRecord` `SinkSite` entry with the traced origin
   (`sprk_graphdriveid`/`sprk_graphitemid` off the authorized row) — `tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs`.

## 12. Deviations from the POML (step 15)

| # | Deviation | Why |
|---|---|---|
| 1 | `SpeFileStore.ReplaceFileContentAsUserAsync` (5-arg) made `virtual` — a file outside the POML's `<relevant-files>` | The established facade test-seam idiom; without it the one-item invariant is untestable except through Graph transport mocking (ADR-038 B1). One keyword, no behaviour change. |
| 2 | New file `Api/Filters/OfficeVersionSaveAuthorizationFilter.cs`; `OfficeErrorCodes.cs` gained `OFFICE_016`–`019` | ADR-008 requires the target's authorization on a filter, and the existing `DocumentAuthorizationFilter` cannot read a body; ADR-019 requires distinct codes. §7 / §9. |
| 3 | `GenerateIdempotencyKey` now appends a content hash **for version saves only** | Without it the second revision is silently dropped (§2). The canonical string of every other request is unchanged. |
| 4 | `IsNewVersion` is an explicit-intent guard (`OFFICE_018`) rather than being carried to SPE | SPE's content PUT carries no version comment or flag (§6). |
| 5 | Step 14 (TASK-INDEX ✅) NOT performed | Coordinator boundary: the main session owns `TASK-INDEX.md` / `current-task.md`. |
| 6 | ArchTest allow-list entry added (`tests/Spaarke.ArchTests/...`) | The guard's documented remedy for a new sink (§11.3). |

## 13. Open items for a human decision (NOT resolved here)

- **AC4 — where `VersionComment` lives.** No `sprk_document` column exists; SPE's PUT carries none. Today it is
  recorded only on the `sprk_processingjob` row (payload) for that save. Durable per-document storage needs a schema
  decision (a column on `sprk_document`, or a comment column on `sprk_fileversion`).
- **AC5 — "unknown id → distinct code".** In production an unknown id is refused 403 by the filter (the access
  source fails closed to `None`), indistinguishable from "not authorized" — deliberately, to avoid a
  record-enumeration oracle (the task 022 / `EntityAccessFilter` policy). `OFFICE_016` (404) fires only when
  authorization passed and the row is then not found. Separating the two would be a security-policy change.
- **AI/RAG refresh after a version save (downstream, worker-owned).** `UploadFinalizationWorker` queues
  `AppOnlyDocumentAnalysis` with idempotency key `analysis-{documentId}-documentprofile`, and that handler skips an
  already-processed key. A version keeps the SAME document id, so the profile/index is **not** refreshed for the new
  content. Needs a per-version key (for example, including the drive-item version or content hash). Not changed here
  (`Workers/`, `Services/Ai/Jobs/` are outside this task).
- **Writer identity (unverified live).** The version is written OBO; finalization AI/RAG reads app-only. Word's own
  autosave already writes these items as the user, but whether app-only reads of a user-written version succeed was
  not verified on dev.
