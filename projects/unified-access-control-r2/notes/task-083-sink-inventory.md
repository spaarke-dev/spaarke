# Task 083 — the SPE write-sink inventory, classified by SINK

> **Produced**: 2026-08-28 by a read-only Fable sweep (083 steps 1–3), then spot-verified in the main
> session. **Escalation trigger 3 FIRED** (">~3 unlisted instances → STOP and re-plan"); owner chose
> **"widen the guard first, then re-plan"** — so this inventory is the INPUT to the guard, not the
> deliverable. The guard's own discovered list supersedes this table the moment it exists.
> **Method**: classify by SINK (find every SPE write/create/delete call, walk BACKWARDS to where its
> container/drive id came from) — not by grepping `containerId`, which finds reads too.

---

## 1. Two corrections to the POML's §2 inventory — both verified first-hand

### ❌ Rows 4 and 5 are NOT "live holes". The POML and my own earlier reporting overstated them.

`PUT /api/drives/{driveId}/upload` and `DELETE /api/drives/{driveId}/items/{itemId}`.

**"app-only" describes only the OUTBOUND Graph leg.** The inbound routes DO require a caller bearer
token — `canwritefiles` bottoms out in `AuthorizationService.cs:54-72`, which hard-denies token-less
calls. So a caller identity IS available, and 083's escalation trigger 2 ("callers exist AND no identity
available") cannot fire: **both of its premises are false.**

They are also not currently exploitable, for a reason that is luck rather than design:

- `canwritefiles` → `ResourceAccessRequirement("upload_file")` (`AuthorizationModule.cs:296-297`)
  → `ResourceAccessHandler` extracts **`driveId`** as the resource (`ResourceAccessHandler.cs:139-143`)
  → `DataverseAccessDataSource` looks up **`sprk_documents({resourceId})`** (`:373`, `:719`).
- A real drive id (`b!…`) **is not a GUID** → `RetrievePrincipalAccess` 400s → `None` → 403.
- Conversely a valid `sprk_document` GUID passes the gate but **is not a valid Graph drive id**, so the
  Graph call fails.

**No constructible request both passes the gate and lands bytes.** The safety rests on value-space
disjointness between GUIDs and `b!…` drive ids — not on a correct decision. That still argues for
deletion, and loudly: the moment either domain widens, the accident stops holding.

**Caller status:**
- Row 4's only caller (`DocumentOperations.js:309-318`) is **dead upstream** — `processFileUpload` first
  calls `GET /api/containers/{containerId}/drive` (`:31`), a route deleted 2026-08-25, and **throws at
  `:302`** before the PUT is ever built.
- Row 5's caller claim in the waiver (`DocumentOperations.js:578`) **HOLDS** — `driveId`/`itemId` come off
  form attributes `sprk_graphdriveid`/`sprk_graphitemid` (`:565-566`), depending on no deleted route.
- **But neither can authenticate.** `getAuthToken` returns `null` (`:130`); `apiCall` sends
  `credentials: 'include'` cookies and **no `Authorization` header** (`:152-162`). The BFF's schemes are
  JwtBearer + ApiKey + Ciam — **there is no cookie scheme**. Every call 401s before any policy runs.
- ⚠️ Deployment of that web resource is **not determinable from the repo** (manual portal deploy per its
  README). Its README predates auth v2, so a deployed copy is non-functional regardless.

**Disposition: DELETE both**, per the 073/076 precedent ("delete rather than convert where a path is
dead"). Sanctioned replacements already ship, record/document-keyed: creation via 076's record-keyed
route; delete via `DocumentOperationsEndpoints.cs:80,437` → `DocumentCheckoutService.cs:787`, which reads
`DriveId/ItemId` off the **authorized** `sprk_document` row. Converting rows 4/5 would mint a SECOND
record-keyed upload surface and a SECOND record-keyed delete surface — a §11 reuse violation on its face.

⚠️ Honest caveat: an out-of-repo caller (Postman, ops script, third-party) cannot be disproven. The
wrong-domain analysis above bounds the damage — no caller can currently complete a write through them.

### ❌ Rows 7 and 8 are not this defect class at all.

Both resolve the container from **configuration**, not client input — `SharePointEmbedded:StagingContainerId`
then `EmailProcessing:DefaultContainerId`. No caller names a container.

They do violate the **settled model** by being record-blind: the session's `HostContext.EntityType/EntityId`
is available server-side (row 7 logs it at `:124-125`) and is simply not consulted. An export from a
secure-record chat lands in the shared staging container, which its own comment (`:226-227`) says is
"accessible to all authenticated users via OBO".

| Row | File | Callers | Disposition |
|---|---|---|---|
| 7 | `Api/Ai/ChatWordExportEndpoints.cs:120,148,154` (helper `:224-237`) | **ZERO** — `SprkChat.tsx:2610-2612` removed the feature in FR-08/task 025 | **DELETE** (also removes the map call) |
| 8 | `Api/Ai/ChatDocumentEndpoints.cs:1134,1155,1160` (helper `:1511-1523`) | **LIVE** — `SprkChat.tsx:2014` | **CONVERT** to record-keyed via the session's `HostContext` |

Stale doc comments to fix in both: `ChatWordExportEndpoints.cs:19` and `:119`, and
`ChatDocumentEndpoints.cs:1020`, all claim `HostContext` drives container resolution. It does not.

---

## 2. 🔴 THE TWO GENUINELY LIVE INSTANCES — neither was in any inventory

### NEW ROW 9 — `POST /api/office/save` (Office add-in save)

The defect class verbatim, on an **MI write**, and reachable by any authenticated caller.

- `SaveRequest.ContainerId` — **client body** (`Models/Office/SaveRequest.cs:27`, "Target container ID for
  file storage") → `OfficeService.SaveAsync:274` → `OfficeStorageUploader.UploadToSpeAsync:46,54` →
  **`UploadSmallAsync` as Managed Identity**, so there is no SPE ACL backstop.
- The per-record gate is `.AddEntityAccessFilter()` (`Api/Office/OfficeEndpoints.cs:174`) keyed on
  **`SaveRequest.TargetEntity`** — **a different value than the container the bytes land in.** The two can
  disagree freely. That is the class definition.
- **Worse: `TargetEntity` is OPTIONAL.** `ValidateSaveRequest` explicitly permits document-only saves
  (`OfficeEndpoints.cs:328-359`), and **`EntityAccessFilter.cs:148-159` returns `next(context)` when the
  target is absent** — verified in the main session; the comment reads *"let the endpoint handle
  validation. This filter is for authorization only."* So an MI write into a **client-named container**
  runs on **baseline authentication alone**.
- Today's repo clients never populate `ContainerId` (zero matches across `src/client/office-addins/**`;
  `useSaveFlow.ts:896`, `quickSaveHelpers.ts`, `outlook/commands/index.ts:134` all omit it), so live
  traffic lands in `EmailProcessing:DefaultContainerId`. **The hole is the contract, not the traffic** —
  any authenticated caller who writes the JSON themselves picks the container.
- Downstream `Workers/Office/UploadFinalizationWorker.cs:638,646,1205` inherit the same origin.

### NEW ROW 10 — SpeAdmin container items (`Api/SpeAdmin/ContainerItemEndpoints.cs`)

`POST /api/spe/containers/{id}/items/upload` (`:160`) and `DELETE …/items/{itemId}` (`:127`).

- Container id from the **client route**; the app-only Graph client is built from the **`configId` the
  client also supplies** (`:888-925` → `ResolveConfigAsync:694` → `DeleteDriveItemForConfigAsync:1695-1698`;
  upload `:994` → `SpeAdminGraphService:3643`/`3698`).
- **Mapped on the ROOT app** (`Infrastructure/DI/EndpointMappingExtensions.cs`), **not** inside the
  `/api/spe` admin group — so it inherits **neither** `SpeAdminAuthorizationFilter` (admin-role) **nor**
  `SpeAdminTenantScopeFilter`. Each route carries bare `.RequireAuthorization()` only.
- `Api/SpeAdminEndpoints.cs`'s own warning applies un-remediated to exactly these routes: without the
  tenant-scope filter, **`configId` is a bearer capability** and nothing checks ownership.
- ⚠️ Its primary defect is a **missing admin gate**, which is broader than container selection.
- PR overlap: `ContainerItemEndpoints.cs` and `Api/SpeAdminEndpoints.cs` are **untouched** by every open
  PR. Only `Infrastructure/Graph/SpeAdminGraphService.cs` is contended (**PR #859**, spe-admin-r2).

---

## 3. 🔴 THE FORCING FUNCTION HAS A BLIND SPOT — and both live rows sit in it

`RouteAuthorizationGuardTests.cs` governs a **hand-maintained census of exactly 12 files**:

```
Api/FileAccessEndpoints.cs          Api/OBOEndpoints.cs
Api/DataverseDocumentsEndpoints.cs  Api/Ai/SemanticSearchEndpoints.cs
Api/DocumentOperationsEndpoints.cs  Api/Ai/RecordSearchEndpoints.cs
Api/DocumentsBulkEndpoints.cs       Api/ExternalAccess/ExternalProjectDataEndpoints.cs
Api/DocumentVersionEndpoints.cs     Api/ExternalAccess/ExternalModuleDataEndpoints.cs
Api/DocumentsEndpoints.cs           Api/ComposeEndpoints.cs
```

`Api/Office/OfficeEndpoints.cs` and `Api/SpeAdmin/ContainerItemEndpoints.cs` are **both absent** (verified:
`grep -c` returns 0 for each). The guard has found four real holes — but **it can only find holes in files
someone already thought to list.** That is why rows 9 and 10 survived four recounts.

**Remedy in flight**: a NEW fitness function, `tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs`,
which INVERTS the census — it scans every `.cs` under the BFF for SPE write sinks and **fails on any sink
site not classified**, so incompleteness becomes a build failure. New file deliberately: two agents are
concurrently editing `RouteAuthorizationGuardTests.cs`, and §11's three questions are answered — the
existing guard keys on a hand-listed set of files classified for *route authorization*; this keys on
*sinks discovered by scanning the whole tree*. Cost of doing nothing is proven by rows 9 and 10.

---

## 4. The full sink table (manual sweep — superseded by the guard's output when it lands)

| # | file:line | sink | container/drive origin | class |
|---|---|---|---|---|
| S1 | `Api/DocumentsEndpoints.cs:65` | `UploadSmallAsync` (MI) | client route `{driveId}` | CLIENT — row 4, DELETE |
| S2 | `Api/DocumentsEndpoints.cs:122` | `DeleteFileAsync` (MI) | client route | CLIENT — row 5, DELETE |
| S3 | `Api/OBOEndpoints.cs:66,72` | `ResolveDriveIdAsync`+`UploadSmallAsUserAsync` | client route `{id}` | CLIENT — 076 converting |
| S4 | `Api/Ai/ChatWordExportEndpoints.cs:148,154` | same pair (OBO) | config staging → default | CONFIG; zero callers → DELETE |
| S5 | `Api/Ai/ChatDocumentEndpoints.cs:1155,1160` | same pair (OBO) | same config keys | CONFIG; LIVE → CONVERT |
| S6 | `Services/Compose/ComposeService.cs:1482,1484` | `UploadSmallAsUserAsync` | `SaveComposeDocumentRequest.ContainerId` (body) | CLIENT — #858, behind #806 |
| S7 | `Services/Compose/ComposeService.cs:1515` | `ReplaceFileContentAsUserAsync` | `request.DriveId` (body) | CLIENT — behind #806 |
| S8 | `Services/Compose/ComposeService.cs:1448` | `ReplaceFileContentAsUserAsync` | Dataverse transient-key lookup | SERVER (record) ✅ |
| S9 | `Services/Compose/ComposeService.cs:442` | `ReplaceFileContentAsUserAsync` | `body.DriveId` (body) | CLIENT — behind #806 |
| **S10** | `Services/Office/OfficeStorageUploader.cs:46,54` | `UploadSmallAsync` (**MI**) | `SaveRequest.ContainerId` (**body**) | **CLIENT — ROW 9, LIVE** |
| S11 | `Services/Office/OfficeStorageUploader.cs:86` | `DeleteFileAsync` | the item just uploaded | SERVER (self-cleanup) ✅ |
| ~~S12~~ | ~~`Workers/Office/UploadFinalizationWorker.cs:638,646`~~ | ~~`UploadSmallAsync`~~ | ~~`payload.ContainerId` from S10~~ | **CORRECTED 2026-08-28: was NEVER LIVE — DELETED.** It sat inside the worker's `else` ("traditional upload flow") branch, which was unreachable: `OfficeJobQueue` is the sole producer of `UploadFinalizationPayload` and always sets `TempFileLocation = "spe://{driveId}/{itemId}"`, so the branch guard could not be false. Both this sweep and the guard's first AllowList mis-classified it as a live `ClientSupplied` sink. Its private `UploadToSpeAsync`, the never-implemented `RetrieveTempFileAsync` stub (GitHub #231) and the `SpeUploadResult` record went with it |
| S13 | `Workers/Office/UploadFinalizationWorker.cs:1146` (was `:1205`) | `UploadSmallAsync` | same payload | **LIVE** — downstream of ROW 9, and the ONLY surviving sink in this file, so it is now **ordinal 1** in the provenance guard (it was ordinal 2 while dead S12 held ordinal 1). Path flattened 2026-08-28: `{parentDocumentId:N}_{fileName}` in the container root, previously `/emails/attachments/{parentDocumentId:N}/{fileName}`. Container provenance UNCHANGED — still client-supplied via the job payload |
| **S14** | `Api/SpeAdmin/ContainerItemEndpoints.cs:160→994`, `:127→888` | `UploadSmallFileAsync`/`CreateUploadSession`/`Items[].DeleteAsync` | client route `{id}` + client query `configId` | **CLIENT — ROW 10, LIVE** |
| S15 | `Services/Workspace/MatterPreFillService.cs:334` | `UploadSmallAsUserAsync` | `_speOptions.StagingContainerId` | CONFIG (staging) |
| S16 | `Services/Workspace/ProjectPreFillService.cs:304` | `UploadSmallAsUserAsync` | `_speOptions.StagingContainerId` | CONFIG (staging) |
| S17 | `Services/Ai/WorkingDocumentService.cs:172` | `UploadSmallAsync` | matter's **stamped** `sprk_containerid` read directly (`:120-124`) | SERVER (record) ⚠️ stale-stamp risk — flag for resolver conversion |
| S18 | `Services/Communication/CommunicationService.cs:2053,2066` | `UploadSmallAsync` | `_options.ArchiveContainerId` | CONFIG ✅ sanctioned server-ingest |
| S19 | `Services/Communication/IncomingCommunicationProcessor.cs:886,914,1075,1083` | `UploadSmallAsync` | `ResolveContainerForContentAsync` (075) | SERVER (record) ✅ |
| S20 | `Services/Communication/MessageAttachmentMaterializer.cs:206-207` (resolve), `:236` (write) | `UploadSmallAsync` | `_containerResolver.ResolveContainerAsync(communicationId, fallback)` — **record-aware** | SERVER (record) ✅ — **DELETED 2026-08-28, RESTORED 2026-08-29 (task 084)**. See correction below |
| S21 | `Services/Email/EmailAttachmentProcessor.cs:232` | `UploadSmallAsync` | `request.DriveId` | **DEAD** (zero callers) |
| S22 | `Services/DocumentCheckoutService.cs:787` | `DeleteFileAsync` | `document.DriveId/ItemId` off the authorized row | SERVER (record) ✅ the sanctioned delete |
| S23 | `SpeFileStore.DeleteItemAsUserAsync:245`, `CreateUploadSessionAsUserAsync:278` (+ `DriveItemOperations.cs:671`, `UploadSessionManager.cs:414`) | facade | — | **DEAD CODE** since 071/076 deleted their routes |

### ⚠️ Correction to S20 — the deletion was wrong and was reverted (2026-08-29, task 084)

The row above previously read **"RESOLVED — FILE DELETED"**. That disposition rested on two claims, and
both were false when written:

1. **"Zero callers."** True of *production* only. The class had a DI registration, **5 unit tests** — including
   the CHAT-ATTACHMENT-POLICY 25 MB + MIME-allow-list gate tests, the only coverage those policy limits had —
   a citation in the ArchTest suite, and live consumers in two sibling worktrees
   (`spaarke-wt-messaging-communication-app-r2/-r3`). "Zero production callers" is not "dead".
2. **"The `request.DriveId ??` override was a caller-named drive awaiting its first caller."** This was true of
   the **pre-076** shape and had **already been fixed by task 076** by the time the note was written. The
   deletion note described a defect that no longer existed.

Restored in the hardened form only. The ordering is load-bearing and is the reason restoration was safe:
`_containerResolver.ResolveContainerAsync` runs **first** and wins; `request.DriveId ?? ArchiveContainerId`
is passed in only as INV-7's tier-3 **fallback**. Were the resolver to run only when `DriveId` is absent, the
secure-record isolation would be caller-bypassable — any caller supplying a drive id would silently reinstate
the defect. A null resolver **refuses** rather than degrading to the shared archive container (CLAUDE.md §10
F.1). Path flattened and sanitized: `{communicationId:N}_{Sanitize(fileName)}` in the container root.

Provenance reclassified in `SpeWriteSinkContainerProvenanceGuardTests`: `Dead` → **`ServerDerivedRecord`**
(not `ClientSupplied` — the fallback never outranks the resolver).

**The lesson**: a "dead code" claim needs the test suite, the DI graph and sibling worktrees checked, not just
production call sites — and a deletion rationale quoting a defect should be re-verified against HEAD, because
the defect may already have been fixed by a task that landed in between.

**Excluded as non-SPE**: all Cosmos `DeleteItemAsync` (SessionPersistence, PromptLibrary, PinnedContext,
MemoryItemStore, MemoryGovernance, provisioning tests) and Dataverse `DeleteAsync`. Graph
webhook-subscription create/delete (`SpeFileStore.cs:391`, `SpeSyncOrchestrator.cs:100,241`,
`GraphSubscriptionManager.cs`) are not byte writes.

---

## 5. Resolved: the stale symbol both POMLs cite

`TryResolveParentEntitySet` **does not exist** — it was **renamed during task 077**
(`notes/task-077-authorize-record-search.md:110`). The real symbol is
**`SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet`** (`:192`, `internal static`),
already consumed at `SemanticSearchEndpoints.cs:547`, `RecordSearchEndpoints.cs:264`,
`RecordSearchAuthorizationFilter.cs:251`.

⚠️ But there are **THREE** logical-name→entity-set maps, on **different key spaces**, so they are not
interchangeable and consolidation is a deliberate change, not a drive-by:

| Map | Keys on |
|---|---|
| `EntityAccessFilter.EntitySetByType:98` (private) | LOGICAL names (`account`, `sprk_matter`) + short aliases |
| `SemanticSearchAuthorizationFilter.AuthorizableEntitySets:144` (internal) | SHORT names (`matter`) |
| `RecordSearchAuthorizationFilter:246` | built dynamically |

**Decision (076)**: the record-keyed upload route keys on a LOGICAL name (what `ResolveForRecordAsync`
takes), so **extend `EntityAccessFilter`**. A fourth map is an automatic §11 review failure.

⚠️ Also note the sweep reported two wrong paths — `Api/EndpointMappingExtensions.cs` is really
`Infrastructure/DI/EndpointMappingExtensions.cs`, and `Api/SpeAdmin/SpeAdminEndpoints.cs` is really
`Api/SpeAdminEndpoints.cs`. Substantive claims held; paths did not. Verify before citing.
