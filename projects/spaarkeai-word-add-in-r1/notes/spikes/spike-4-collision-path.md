# Spike-4: does the add-in save path share the shipped collision semantics?

> **Answer: NO.** Finding **F-a** is confirmed, and the picture is worse than F-a states.
> **Status**: sections 1–7 were gathered 2026-09-08 by direct code verification during a review
> repair, ahead of `task-execute`; their citations and the §5 operator decisions are preserved
> **verbatim** below. Sections 8–18 were added 2026-09-08 under formal `task-execute` closure of
> task 005 to satisfy the remaining POML acceptance criteria (hop-by-hop server trace, client side,
> small-vs-large payload, expanded comparison table, task 094 overlap boundary, task 025
> implementation sketch, open questions). Every file:line citation in sections 8–18 was verified by
> reading the cited file at closure time. **Task 005 is CLOSED** — see TASK-INDEX.md.

---

## 1. The question

FR-12 asserts the collision fix is already shipped and instructs: *"Consume the shipped collision
handling (verify, do not rebuild)… **Do not rebuild any of this.**"* Its acceptance criterion is
**"no new collision logic is introduced."** Spike-4 exists to verify that premise.

## 2. The premise is false

The add-in does **not** ride the path UAC-r2 fixed.

| | Shipped/protected path | The add-in's actual path |
|---|---|---|
| Entry | client-side upload via `@spaarke/sdap-client` and the OBO endpoint | `POST /api/office/save` (JSON/base64), [`useSaveFlow.ts:901`](../../../src/client/office-addins/shared/taskpane/hooks/useSaveFlow.ts#L901) |
| Server | `UploadSmallAsUserAsync(..., ConflictBehavior.Fail, ...)` | `OfficeService` → `OfficeStorageUploader` → the **no-policy** `UploadSmallAsync` overload |
| Collision behavior | `Fail` → Graph 409, existing item untouched | **`Replace`** — [`UploadSessionManager.cs:103`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs#L103), *"preserving the historical `ConflictBehavior.Replace` behaviour"* |
| Typed client error | `UploadNameConflictError` ([`sdap-client/index.ts:13`](../../../src/client/shared/Spaarke.SdapClient/src/index.ts#L13)) | not reachable — [`office-addins/package.json`](../../../src/client/office-addins/package.json) depends only on `@spaarke/auth` |

**FR-12 as written cannot be satisfied on this path.** "Consume the shipped handling" has nothing to
consume: the protection lives on a different code path, and the add-in's server-side save re-enters
SPE through an overload that hard-codes `Replace`.

## 3. Three distinct defects (the reverted commit addressed only the third, incorrectly)

### D1 — Filename collision silently overwrites

A same-named Office save replaces the existing SPE item before any Dataverse reconciliation. This is
a live overwrite on the Word/Outlook save path.

### D2 — An **editable** Word document runs the **immutable suppress** path 🔴

[`OfficeDocumentPersistence.cs:78-85`](../../../src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs#L78-L85):
on a byte-identical `quickXorHash` hit it **skips the create entirely** and returns the existing
canonical id — the caller then cleans up the transient blob. This runs for every content type,
including `SaveContentType.Document`.

`DEDUP-AND-SAVE-BACK-IDENTITY.md` §3 predicted exactly this and forbade it:

> *"if the save-back path ever treats it like an immutable copy (suppress-forever on a hash hit), two
> genuinely-different drafts that happen to be byte-identical right now would collapse into one
> record — **data loss**."*

That doc's §5 already labels `OfficeDocumentPersistence.cs` the *"immutable suppress caller"*, and
NFR-08 requires **link/graduate** for editable documents, mirroring
`ComposeService.PromoteIfEphemeralAsync`. Word is editable. **This is arguably more serious than D1**:
D1 overwrites a file (SharePoint retains prior content as a version); D2 discards a distinct draft's
record outright.

### D3 — `ExistingDocumentId` / `IsNewVersion` are inert

Finding **F-d**. FR-11's version-save — the mechanism that makes most collisions *impossible* — does
not exist yet on either side.

## 4. The ordering insight (why the reverted commit was the wrong first move)

`DEDUP-AND-SAVE-BACK-IDENTITY.md` §3 closes with the load-bearing note:

> *"if you stay on the item-identity → version-save path (Layer A) for Spaarke-sourced documents,
> you sidestep this entirely — version-save targets the same record, so there's no dedup decision to
> make. Layer B only matters for the 'returned as a new item' edge case."*

**Build FR-11 (023/024) first and most of the collision surface disappears.** A Spaarke-sourced
document that is identified resolves to its own record and versions it — no filename collision, no
hash decision. D1 and D2 then apply only to the residual: documents that left Spaarke and returned
as a new item, or were opened from outside.

The reverted commit `45f45f626` attacked the *last* link in the chain (surface a 409) while the
*first* (identity → version) is unbuilt — and did so by adding the server-side collision logic FR-12
forbids, without closing this spike or coordinating with UAC-r2 task 094.

## 5. 🔔 ADR/spec conflict — resolution required (root CLAUDE.md §6.5)

- **Rule in question**: spec FR-12 — *"Do not rebuild any of this"*; acceptance *"no new collision
  logic is introduced."*
- **Conflict**: the rule assumes the add-in rides the protected path. §2 proves it does not. Strict
  compliance ships D1 and D2 unfixed; literal non-compliance is what the reverted commit did.
- **Proposed path**: **C then B** — pivot to comply first, then amend.
  1. **(C)** Build FR-11 (023 → 024) as specified. Re-measure the residual collision surface.
  2. **(B)** Amend FR-12 against the measured residual: its premise is factually wrong and should
     not survive as written.
- **Rationale**: 023/024 is already on the critical path, is already scoped, and shrinks the problem
  before anyone designs a fix for it. It also avoids duplicating UAC-r2 task 094, which owns the
  pre-flight probe and "Use existing".
- **Alternative considered and rejected**: a project-scoped exception (path A) authorizing new
  Office-path collision logic now. Rejected — it would re-do the reverted commit's *shape*, sizing a
  fix against a surface that FR-11 is about to shrink, and it leaves FR-12's false premise standing.
- **Impact**: FR-12 and task 025 are re-scoped, not descoped. D2 needs its own owner (see §6).

### ✅ Operator decision — 2026-09-08

1. **Path C → B accepted.** Build FR-11 (023 → 024) first, re-measure the residual collision surface,
   then amend FR-12 against what is actually left. No project-scoped exception is granted for new
   Office-path collision logic ahead of FR-11.
2. **F-h (D2) is owned by this project.** There is no other open project to route it to, and r1 is
   already in this code. Filed as **task 028**.
3. **🔴 Binding constraint on 028 — the fix is host-neutral.** The remedy MUST apply to Outlook as
   well as Word, and MUST NOT branch on which host called. This is achievable as stated because the
   editable/immutable axis is **`SaveContentType`**, not host: `Email` and `Attachment` (Outlook) are
   immutable captures for which suppress is correct; `Document` (Word today, any host tomorrow) is
   editable and requires link/graduate. `OfficeDocumentPersistence` already switches on
   `SaveContentType` at lines 94-96, 133 and 178, so one branch in the shared persistence path lands
   for both hosts by construction. Any design that introduces a Word-vs-Outlook Office.js API
   divergence to solve this is out of contract — the divergence belongs in the host adapters, never
   in save or dedup semantics. This also satisfies spec.md:274 ("fixed at the shared path… not
   patched only in the add-in").

## 6. Recommended plan

| # | Work | Owner | Depends on | Note |
|---|---|---|---|---|
| 1 | Close **005** formally via `task-execute`, this report as input | 005 | — | Ratifies §2 and unblocks 025 |
| 2 | Operator decides the §5 path (C-then-B recommended) | operator | 1 | Blocks 3 and 5 |
| 3 | Build **FR-11** version-save: 023 (server) → 024 (client) | 023, 024 | 012 | **Strictly serial** — data-integrity. NFR-08: override routes link/graduate |
| 4 | **D2/F-h**: editable saves must link/graduate, not suppress — **host-neutral, keyed on `SaveContentType`** | **028** (opus / xhigh) | — | ✅ owned by r1 per the 2026-09-08 decision. Mirror `ComposeService.PromoteIfEphemeralAsync`. Independent of 1-3; can start now |
| 5 | Re-scope **025** against the measured residual; `/conflict-check` vs UAC-r2 **094** first | 025 | 2, 3 | May shrink to "surface the typed error", or may need a coordinated server change |
| 6 | Amend FR-12 in `spec.md` to match §2 | — | 2 | Its premise is false as written |

Also correct `design.md` per `DEDUP-AND-SAVE-BACK-IDENTITY.md` §6 (three edits, still unapplied).

## 7. What was reverted and why

`45f45f626` (reverted by `0b68943d1`) passed `ConflictBehavior.Fail`, caught the 409, mapped
`OFFICE_011`, and rewrote the pane's error copy. It was reverted because:

1. It introduces new collision logic — FR-12's acceptance criterion is that none is introduced.
2. It pre-empted this spike; task 005 was still `not-started`.
3. It overloaded `OFFICE_011`, which already means duplicate **content** (dedup), with duplicate
   **filename** — collapsing the two layers `DEDUP-AND-SAVE-BACK-IDENTITY.md` §2 says to keep apart.
4. It hand-rolled `Results.Problem` beside the existing `OfficeProblemDetailsExtensions.DocumentAlreadyExists`,
   giving `OFFICE_011` two divergent wire shapes.
5. Its error copy directed users to a *Save version* / *Save as new* choice that the pane cannot
   offer — the shipped two-option dialog lives in `Spaarke.UI.Components`, which the add-in does not
   depend on.
6. No tests, against the CLAUDE.md §10 test-update obligation.

Its one correct instinct — that the Office path lacks protection — is preserved as **D1** above.

---

> **The sections below (8–18) complete the report to the task 005 POML's specification. They were
> added under formal `task-execute` closure, 2026-09-08. Every file:line citation in this part was
> verified by reading the cited file at the time of writing — none is copied forward unverified from
> plan.md's authoring-time observations.**

## 8. Client side

`src/client/office-addins/package.json` `dependencies` confirmed in full: `@azure/msal-browser`,
`@spaarke/auth`, `@fluentui/react-components`, `@fluentui/react-icons`, `@microsoft/office-js`,
`react`, `react-dom`. **No `@spaarke/sdap-client`.**

The add-in issues the save request from
[`useSaveFlow.ts:900-913`](../../../src/client/office-addins/shared/taskpane/hooks/useSaveFlow.ts#L900-L913):

```ts
const response = await authenticatedJsonFetch(
  `${apiBaseUrl}/api/office/save`,
  {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Idempotency-Key': idempotencyKey },
    body: JSON.stringify(serverRequest),
    signal: abortControllerRef.current.signal,
  },
  saveToken,
  { getRetryToken: getAccessToken }
);
```

Response handling, same file:

- **Lines 915-931**: reads raw response text, then attempts `JSON.parse`.
- **Line 934**: `if (response.status === 200 && responseData.duplicate)` — the ONE conflict-shaped
  branch the add-in has today. It is wired to the server's **content-hash dedup** signal
  (`SaveResponse.Duplicate`, `OfficeService.cs:490-497`), not to a filename collision — there is no
  server-side filename-collision signal for it to key off (§10-§12).
- **Lines 945-954**: any other non-OK response — `isProblemDetails(responseData)` (defined at
  `errorMessages.ts:318`) routes to `mapProblemDetailsToMessage` (`errorMessages.ts:181`); otherwise
  a generic `Save failed: ${response.status}` `Error` is thrown.
- **Lines 963-972**: `catch` block — any thrown error (including the generic one above, and any
  `AbortError`) becomes `createErrorFromException(err, 'Failed to save. Please try again.')`.

`errorMessages.ts:113-120` already has an `OFFICE_011` mapping ("Document Exists" / *"This document
has already been saved to the selected entity."* / `type: 'info'`) — but per §12 below, that code has
**zero server-side callers** on this path. The pane is structurally capable of rendering a typed
ProblemDetails-shaped conflict message; nothing on the server ever produces one for a name collision.

## 9. Server trace, hop by hop

1. **Route registration** —
   [`OfficeEndpoints.cs:168`](../../../src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs#L168):
   `group.MapPost("/save", SaveAsync)`.
2. **Handler** —
   [`OfficeEndpoints.cs:207-259`](../../../src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs#L207-L259)
   (`private static async Task<IResult> SaveAsync(...)`). After identity + request validation, calls
   the service at **line 259**: `var response = await officeService.SaveAsync(request, userId, context, cancellationToken);`.
3. **`OfficeService.SaveAsync`** —
   [`OfficeService.cs:168`](../../../src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs#L168).
   Idempotency check (**line 208**), optional email/attachment enrichment (188-199), container
   resolution (**line 281**), content decode + filename sanitization per content type — Document
   branch at **lines 376-409** (`bytes = Convert.FromBase64String(request.Document.ContentBase64)` at
   **380-383**; `SpeUploadPath.SanitizeFileName(request.Document.FileName)` at **line 408**).
4. **Upload call (synchronous, inline in the request)** —
   [`OfficeService.cs:421`](../../../src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs#L421):
   `var (uploadSuccess, driveId, itemId, webUrl, uploadError) = await _storageUploader.UploadToSpeAsync(containerId, fileName, contentStream, cancellationToken);`.
   ⚠️ **Correction to the framing in §2/task 005's authoring background**: the bytes reach SPE
   **inline, inside the `SaveAsync` request/response cycle** — the Service Bus job queue
   (`OfficeJobQueue`, next hop) is for **finalization** (artifact records, AI triggers) only, not for
   the upload. `OfficeJobQueue.QueueUploadFinalizationAsync`'s payload carries
   `TempFileLocation = $"spe://{driveId}/{itemId}"`
   ([`OfficeJobQueue.cs:63`](../../../src/server/api/Sprk.Bff.Api/Services/Office/OfficeJobQueue.cs#L63))
   — a reference to a file **already in SPE** by the time the job is queued (`OfficeService.cs:538-549`).
5. **`OfficeStorageUploader.UploadToSpeAsync`** —
   [`OfficeStorageUploader.cs:37-81`](../../../src/server/api/Sprk.Bff.Api/Services/Office/OfficeStorageUploader.cs#L37-L81).
   Resolves the drive id (`_speFileStore.ResolveDriveIdAsync`, **line 51**), sanitizes the path again
   (**line 61**), then calls the facade at **line 62**:
   `var result = await _speFileStore.UploadSmallAsync(driveId, uploadPath, content, cancellationToken);`
   — the **4-argument, no-`conflictBehavior`** overload.
6. **`SpeFileStore.UploadSmallAsync`** (facade, ADR-007) —
   [`SpeFileStore.cs:70-75`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs#L70-L75):
   `=> _uploadManager.UploadSmallAsync(driveId, path, content, ct);` — a thin delegate. (The facade
   ALSO exposes an explicit-`conflictBehavior` overload at
   [`SpeFileStore.cs:82-88`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs#L82-L88);
   `OfficeStorageUploader` never calls it.)
7. **`UploadSessionManager.UploadSmallAsync`, no-policy overload — conflict behaviour DECIDED here** —
   [`UploadSessionManager.cs:98-103`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs#L98-L103):
   `=> UploadSmallAsync(driveId, path, content, ConflictBehavior.Replace, ct);` — hard-codes
   `ConflictBehavior.Replace` and forwards to its 5-argument sibling.
8. **`UploadSessionManager.UploadSmallAsync`, conflict-behaviour-aware overload** —
   [`UploadSessionManager.cs:115-200`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs#L115-L200).
   **Line 132** acquires an **app-only** Graph client (`_factory.ForApp()`); **line 138** calls
   `PutContentWithConflictBehaviorAsync(graphClient, driveId, path, content, conflictBehavior, ct)`
   with `conflictBehavior = Replace` (from hop 7).
9. **`PutContentWithConflictBehaviorAsync` — the Graph call** —
   [`UploadSessionManager.cs:50-85`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs#L50-L85).
   **Lines 58-61** build a PUT request to
   `graphClient.Drives[driveOrContainerId].Root.ItemWithPath(path).Content`; **lines 63-64** append
   `@microsoft.graph.conflictBehavior=replace` onto the request URI
   (`conflictBehavior.ToGraphString()`); **line 80** sends it:
   `graphClient.RequestAdapter.SendAsync(requestInfo, DriveItem.CreateFromDiscriminatorValue, errorMapping, ct)`.

**The trace terminates at hop 9 with the value decided at hop 7 and carried explicitly onto the wire
at hop 9** — this is sharper than "the platform default applies": the code is explicit about
`replace`; it simply never gives `OfficeStorageUploader` (hop 5) a way to ask for anything else.

## 10. Where conflict behaviour is decided, and the effective value

- **Decided at**:
  [`UploadSessionManager.cs:103`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs#L103)
  — `UploadSmallAsync(driveId, path, content, ct) => UploadSmallAsync(driveId, path, content, ConflictBehavior.Replace, ct);`
- **Effective value on the add-in's path**: **`ConflictBehavior.Replace`, always.** No code path from
  `POST /api/office/save` ever supplies `.Fail` or `.Rename`.
- **Not an unstated Graph platform default.** The doc comment at
  [`UploadSessionManager.cs:43-48`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs#L43-L48)
  explicitly warns that Graph's own driveItem-PUT docs and createUploadSession docs disagree about
  what an absent parameter defaults to, and states the codebase's own rule: *"the behaviour MUST
  always be stated explicitly."* Line 64 proves it IS stated explicitly here — `replace`, deliberately,
  per the rationale at
  [`UploadSessionManager.cs:88-96`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs#L88-L96):
  the no-policy overload exists so that *"every existing app-only caller (Compose save, communication
  ingest, invoice extraction, …) writes to a path it derived itself and relies on replace-in-place."*
  `OfficeStorageUploader` is simply one of those callers, never having opted into `.Fail`.

## 11. Small versus large payload

- `POST /api/office/save` carries the **entire document as base64 inside the JSON body**
  (`DocumentMetadata.ContentBase64`,
  [`SaveRequest.cs:359`](../../../src/server/api/Sprk.Bff.Api/Models/Office/SaveRequest.cs#L359)),
  decoded server-side to a `MemoryStream`
  ([`OfficeService.cs:380-383`](../../../src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs#L380-L383)).
  `ContentBase64` carries no `[MaxLength]`, and `/save`'s route registration
  ([`OfficeEndpoints.cs:168-183`](../../../src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs#L168-L183))
  has no `RequestSizeLimitAttribute` — contrast `ComposeSaveEndpoints.cs:34` and `:55`, which DO apply
  `RequestSizeLimitAttribute(ComposeSaveLimits.MaxRequestBodyBytes)`.
- `OfficeStorageUploader.UploadToSpeAsync` has **no size branch** — it always calls the same
  `_speFileStore.UploadSmallAsync(driveId, uploadPath, content, cancellationToken)`
  (`OfficeStorageUploader.cs:62`) regardless of `content.Length`.
- There is, in fact, **no app-only large-payload/upload-session mechanism left to branch to**:
  [`UploadSessionManager.cs:203-208`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs#L203-L208)
  records that the app-only chunked-upload pair (`CreateUploadSessionAsync` + `UploadChunkAsync`) was
  **DELETED 2026-08-27** by `unified-access-control-r2` (following its task 073's deletion of
  `Api/UploadEndpoints.cs`, their only caller). The OBO-side chunked upload survives for the
  client-driven wizard path, but nothing on `POST /api/office/save`'s server-side upload can reach it
  — that call is entirely app-only (`UploadSessionManager.cs:132`, `_factory.ForApp()`).
- **Finding: uniform, not size-dependent — because there is no large-payload route on this path at
  all.** Every add-in save, regardless of document size, takes the identical single Graph PUT with
  `conflictBehavior=replace` (hop 9, §9). This is a sharper finding than "small and large might
  differ": the risk is that the *only* route is the small-file, uncapped one, for documents of any
  size the JSON body can carry — bounded only by the platform's default request-size ceiling, which
  is unconfigured for this route and therefore out of scope for a conflict-behaviour trace (see §18
  open question 4).

## 12. Side-by-side comparison table

| | Shipped OBO path (`PUT /api/obo/records/{entity}/{recordId}/files/{name}`) | Add-in save path (`POST /api/office/save`) |
|---|---|---|
| Endpoint + method | `PUT /api/obo/records/{entityLogicalName}/{recordId}/files/{name}` — handler `OBOEndpoints.cs:140-151` | `POST /api/office/save` — `OfficeEndpoints.cs:168` |
| Request encoding | Raw binary stream, `Content-Type: application/octet-stream`, streamed to Graph — `UploadOperation.ts:130-131`; server-side `req.Body` passed through at `OBOEndpoints.cs:146` | JSON body, base64 content — `useSaveFlow.ts:901-909`; decoded to a `MemoryStream` at `OfficeService.cs:380-383` |
| Where conflict behaviour is decided | `OBOEndpoints.cs:558-564` (`ResolveConflictBehavior`) — reads `?conflictBehavior=`, defaults to `ConflictBehavior.Fail` when absent | `UploadSessionManager.cs:98-103` — hard-codes `ConflictBehavior.Replace`; no caller input possible |
| Value when caller specifies nothing | `Fail` — deliberate; `OBOEndpoints.cs:555-556`: *"an absent parameter must mean fail, not the parser's default"* | `Replace` — the ONLY value; there is nothing to specify (`OfficeStorageUploader.cs:62` never passes a `conflictBehavior` argument) |
| Effect on the existing file's bytes on a same-name save | Untouched — Graph 409s before any write (`UploadSessionManager.cs:169-179`, `SpaarkeStorageException` thrown) | **Replaced** — Graph overwrites in place; SharePoint's own versioning is the only safety net (§13) |
| HTTP status + body on collision | 409, `SpaarkeStorageException.ToProblemDetails()` → ProblemDetails body (`OBOEndpoints.cs:158-161`) | **200/202 success** — no collision status exists on this route (`OfficeEndpoints.cs:176-177` document only 202/200 as success codes; no branch returns 409 for a name collision) |
| Can the client distinguish a conflict from a generic failure? | Yes — `UploadOperation.ts:135-139` throws the typed `UploadNameConflictError` on `status === 409` (class at `:33`, doc comment `:22`), checked by `instanceof` | **No — there is nothing to distinguish.** `useSaveFlow.ts:945-954` COULD branch on a ProblemDetails-shaped conflict via `errorMessages.ts`'s `OFFICE_011` mapping (`:113-120`), but nothing on this server path ever produces that shape for a name collision — `DocumentAlreadyExists`/`OFFICE_011` has **zero callers** anywhere in `src/server/api/Sprk.Bff.Api/**` outside its own definition (`OfficeErrorCodes.cs:52`, `OfficeProblemDetailsExtensions.cs:245`, `OfficeProblemException.cs:224`) |
| Payload size dependence | Uniform `Fail` for both `uploadSmallForRecord`/`uploadSmallWithoutRecord` (shared `put`, `UploadOperation.ts:111-146`) | Uniform `Replace` regardless of size (§11) — no size branch exists |

## 13. What a same-name save does today

**A same-named-file save from the add-in silently overwrites the existing SPE item's content with the
new bytes, in place, with no error, no warning, and no opportunity for the user to choose otherwise —
SharePoint's own version history is the only place the prior content survives** (§3 D1, §9 hop 7/9).

## 14. Dedup interaction (NFR-08)

- Filename collision (`Replace`, §10) and content-hash dedup
  (`OfficeDocumentPersistence.ReconcileAsync`, called at
  [`OfficeDocumentPersistence.cs:78`](../../../src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs#L78))
  are **two different, independent mechanisms on this path — and they are NOT mutually exclusive on a
  single save.** The upload (which decides filename collision) always runs first
  ([`OfficeService.cs:421`](../../../src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs#L421)),
  and only after a successful upload does `CreateDocumentWithSpePointersAsync` run the content-hash
  reconciliation
  ([`OfficeService.cs:455`](../../../src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs#L455),
  which calls `OfficeDocumentPersistence.cs:78`). So a single add-in save can, in sequence: (a)
  silently overwrite an existing SPE item because its name collided (§13), THEN (b) have its own
  just-uploaded bytes matched by content-hash against some OTHER canonical document and be suppressed.
  Both can fire on one request — they are not alternatives.
- The content-hash reconciliation at
  [`OfficeDocumentPersistence.cs:78-85`](../../../src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs#L78-L85)
  runs **unconditionally** — there is no `SaveContentType` guard around the
  `_dedupDetector.ReconcileAsync(...)` call at line 78. The `SaveContentType` switch that DOES exist in
  this method (lines 92-98, the `Description` field) is **downstream** of the dedup check, so it never
  gates it. A Word document's save runs the identical suppress-on-hash-hit path as an immutable
  Outlook email/attachment capture.
- Per NFR-08 (spec.md:105) an editable document's content-hash hit MUST route through link/graduate,
  never immutable suppress. §3 D2 already names this defect and its owner (task 028, per the
  2026-09-08 operator decision, §5). This section's addition: a filename collision does not prevent the
  hash-hit path from also firing on the same request — the two defects are independently reachable AND
  jointly reachable on one save.

## 15. Task 094 overlap boundary (`unified-access-control-r2`)

Per `projects/unified-access-control-r2/tasks/094-upload-collision-preflight-probe.poml`
(re-scoped, `pending`, depends on its own task 076):

**094 owns:**
1. The **pre-flight existence probe** — asking the server whether a same-named file already exists
   BEFORE the bytes move, replacing today's "attempt the upload, catch the 409" pattern on the
   OBO/wizard path. Framed explicitly as an optimization on top of the existing `Fail`-on-409
   protection, not a replacement for it (094 POML: *"the probe is an OPTIMISATION, not the
   guarantee... two users can race between probe and upload"*).
2. **"Use existing"** — a third collision option ("do not upload; link the existing document"), gated
   on `unified-access-control-r2` task 095 (the document-record intersection entity) and explicitly
   not to be attempted before 095 lands.

**094 does NOT own, and does NOT cover:**
- Anything on the add-in's `POST /api/office/save` path. 094's entire scope is the OBO record-keyed
  upload route (`UploadOperation.ts`, `MultiFileUploadService.ts`, the wizard's collision dialog) — the
  path §12's comparison table shows the add-in does not use at all. 094's `<knowledge><files>` lists
  no file under `src/client/office-addins/**` or `Services/Office/**`.
- The `Replace`-hard-coded no-policy `UploadSmallAsync` overload this spike traced (§9-§10) — 094's
  POML never mentions `UploadSessionManager.cs` or `OfficeStorageUploader.cs`.
- D2 (the immutable-suppress-on-editable-document defect, §3) — orthogonal to 094's upload-collision
  scope entirely; already assigned to this project as task 028 (§5 operator decision).

**Therefore this project must NOT build:** a pre-flight existence-probe endpoint, or an "Use existing"
option, for the add-in path — both would duplicate 094's design once it lands, and 094's probe pattern
(ask the server before bytes move) is strictly better than either the OBO path's "ask via 409 after
upload, existing file safe" or the add-in's current "overwrite silently, never ask." If/when task 025
is re-scoped (§5 Path C→B), it should evaluate whether the add-in can consume 094's eventual pre-flight
probe endpoint directly (a server-side, app-only call) rather than inventing an add-in-specific probe —
that reuse question is the CLAUDE.md §11 three-question justification 025 will need to answer, not an
answer this spike pre-empts.

## 16. Section 6.5 recommendation

See **§5** above (preserved verbatim as the formal CLAUDE.md §6.5 record and the 2026-09-08 operator
decision). Nothing in §8-§15 changes that recommendation. If anything, §9's sharper trace (the value is
explicitly STATED as `Replace`, never merely defaulted) and §14's finding (filename collision and
content-hash dedup can both fire on one save) strengthen the case for Path C: build FR-11 first and
shrink the residual before sizing a fix for either defect — a narrower residual is easier to reason
about for both at once.

## 17. Implementation sketch for task 025

**Status: provisional, and explicitly NOT startable yet.** Task 025's POML lists `deps: 005` — this
closure unblocks that dependency mechanically — but per §5's Path C→B decision, 025 should not be
SIZED until FR-11 (tasks 023→024) lands and the residual collision surface is re-measured (§4, §6 row
5). The sketch below is a facade-level shape for whichever residual needs it, not a go-ahead to
implement now.

Facade-level shape (ADR-007 — no `Microsoft.Graph` type crosses out of `Infrastructure/Graph/`):

1. **`OfficeStorageUploader.UploadToSpeAsync`** gains an optional `ConflictBehavior` parameter
   (default `Replace`, so every OTHER app-only caller's behavior is unchanged — per the exact
   rationale `UploadSessionManager.cs:88-96` already states for keeping the no-policy overload). Its
   call at `OfficeStorageUploader.cs:62` switches, when a non-default behaviour is requested, to the
   **already-existing** explicit overload — `SpeFileStore.UploadSmallAsync(driveId, path, content,
   conflictBehavior, ct)` (`SpeFileStore.cs:82-88`) — which is defined today but has no app-only
   Office caller. No new facade method is needed; the explicit overload already threads through to
   `UploadSessionManager.cs:115-200`'s existing `ODataError`/409 → `SpaarkeStorageException`
   translation (`UploadSessionManager.cs:169-179`), which already produces a typed, catchable
   exception.
2. **`OfficeService.SaveAsync`** passes `ConflictBehavior.Fail` at its call site (`OfficeService.cs:421`)
   instead of relying on the no-arg overload — mirroring the OBO path's default-to-`Fail` posture
   (`OBOEndpoints.cs:558-564`). This is a FIXED value, not a new resolver: `POST /api/office/save` has
   no `conflictBehavior` query parameter to resolve, so adopting `Fail` here is adopting the same
   STATED value the OBO path already defaults to — not new "collision logic" in the FR-12 sense.
3. **Catch + translate at `OfficeService.cs:421`**: a `SpaarkeStorageException` (409) needs a catch
   clause parallel to the existing `!uploadSuccess` branch (`OfficeService.cs:427-443`, which returns
   `OFFICE_012`) — mapped to a **NEW** office error code (NOT a reuse of `OFFICE_011`, which §7 point 3
   already flags as belonging to content dedup — the reverted commit's exact mistake), through
   `OfficeProblemDetailsExtensions`, at 409, with a body the add-in's EXISTING
   `isProblemDetails`/`mapProblemDetailsToMessage` handling (`useSaveFlow.ts:945-951`,
   `errorMessages.ts`) can already render — a new error-code case in `errorMessages.ts`'s existing
   switch, no new client-side parsing branch.
4. **Pane surfacing**: per task 025's own ADR-012 exception constraint, this stays a thin view — mirror
   (do not import) `UploadNameConflictError`'s shape rather than adding a `@spaarke/sdap-client`
   dependency; the shipped two-option semantics (`rename`/`replace`) are a UX pattern to match, not a
   component to reuse, since the dialog itself lives in `Spaarke.UI.Components`, which the add-in does
   not depend on (per task 025's own POML background and ADR-012 constraint).
5. **Contract-test obligation (ADR-038, F-f)**: `POST /api/office/save` has ZERO executing contract
   coverage today — both its tests are skipped in
   `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs` (lines 48 and 82, per
   plan.md finding F-f). Whatever 025 ships MUST add a LIVE (non-skipped) contract test asserting the
   non-destructive property directly: read the existing item's bytes/hash before a colliding save,
   perform the save, assert the bytes/hash are unchanged AND the response carries the new typed error
   — not merely that a 409 was returned. Un-skipping the pre-existing 13-of-22 skipped tests in that
   file is task 016's scope, not 025's — but 025's new test must not be added to that skip list.

This sketch is deliberately silent on the pre-flight-probe question — that is 094's territory per §15,
and 025 should ask, at re-scope time, whether to consume 094's probe rather than duplicate it.

## 18. Open questions

1. **What does the eventual FR-11 residual actually look like?** §4/§6 predict "documents that left
   Spaarke and returned as a new item, or were opened from outside" but that set is unmeasured — it
   depends on tasks 023/024's shipped identity-resolution behavior, which does not exist yet. Task 025
   cannot be sized accurately until that measurement exists.
2. **Does the add-in need its own pre-flight probe, or can it consume 094's?** §15 raises this; 094
   itself is not yet built (`pending`), so the answer may not be knowable until 094 ships. If 025 lands
   before 094, it may need the reactive (upload-then-catch-409) pattern this sketch describes as an
   interim step, with a follow-up to adopt 094's probe once available — that staging decision belongs
   to whoever sizes 025, not to this spike.
3. **Is there a live production exposure window for D1 right now?** This spike traced the CODE path;
   it did not query production SPE/Dataverse for evidence that an overwrite has actually occurred
   (out of this task's read-only, no-new-queries scope for `src/**`), and no logging currently
   distinguishes "uploaded to a fresh path" from "uploaded over an existing path" —
   `OfficeStorageUploader.cs:66-69`'s success log records `DriveId`/`ItemId` but not whether the ItemId
   pre-existed. If an operator wants an incidence estimate ahead of FR-11 landing, that is a separate,
   explicitly-scoped investigation, not something this report asserts.
4. **Kestrel/App Service request-size ceiling for `/api/office/save`.** §11 confirms no
   `RequestSizeLimitAttribute` is applied to this route (unlike Compose's
   `RequestSizeLimitAttribute(ComposeSaveLimits.MaxRequestBodyBytes)`), so the effective ceiling is
   whatever the platform default is. That default was not measured as part of this spike — it does not
   bear on conflict-behavior semantics, only on whether very large Word documents can be saved from the
   add-in at all — but is worth a one-line follow-up if a large-document save failure is ever reported.
5. **Should `OFFICE_011`/`DocumentAlreadyExists` be removed as dead code, or reserved for a future
   content-dedup surface?** §12/§17 both note it currently has zero callers anywhere in the BFF. This
   spike does not recommend deletion (read-only scope) but flags it as a candidate for whoever next
   touches `Api/Office/Errors/**`.
