# Task 171 — broker-only document bytes, standing BU writers, JIT Office edit on secure containers

> Branch `task/uac-r2-171` (worktree `C:\wt171`, from `origin/master` @ `b403c7713`). Owner rounds 69 + 70 (+ the
> round-70 Office add-in note) are the binding design. Evidence that opened the task: the batch-4 upload403
> investigation (secure container = 0 roles, BU1 container = 3 hand-granted roles; OBO `createUploadSession` 403 on
> the first, 200 on the second, same user/route/code).

## Step 0 — research (before any code)

Sources: Microsoft Learn + Graph reference, fetched 2026-10-06 by the researcher subagent; repo
`docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`; the dev evidence above. VERIFIED = stated in the docs;
INFERRED = follows from verified behaviour but not stated.

| # | Question | Answer | Confidence / source |
|---|---|---|---|
| a | Can the app grant a NON-member a FILE-level permission that lets Office edit that file? | **Yes, documented.** "To share individual items without granting container access, use the driveItem invite or permission create endpoints. Sharing an item doesn't grant access to the container or to any other item in it." App-only `POST /drives/{d}/items/{i}/invite` (`roles:["write"]`, `sendInvitation:false`) can invite existing users and guests. Office prerequisite is "the user has permission to view or edit the file". Office for the web: VERIFIED. Office desktop with a file-only permission: INFERRED (needs a live probe). | High (web) / medium (desktop). [configure-authentication-authorization](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/configure-authentication-authorization), [share-files-manage-permissions](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/share-files-manage-permissions), [driveitem-invite](https://learn.microsoft.com/en-us/graph/api/driveitem-invite?view=graph-rest-1.0), [open-office-files](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/open-office-files) |
| a′ | Sharing model of type 8a6ce34c (restrictive vs open) | **Not read** — `GET containerTypes` is delegated-only; app-only needs `FileStorageContainerTypeReg.Selected` on `containerTypeRegistrations/{id}` (v1.0), which the BFF identity is not known to hold, and no tenant read was in scope. Both cases treated: **open** (the default) lets any container member with edit RESHARE a file from Office (outside Dataverse); **restrictive** (`isSharingRestricted=true`) lets only Owner/Manager add file permissions. Whether restrictive also blocks an app-only invite is undocumented (INFERRED not, since app-only ignores container roles). | VERIFIED (model semantics) / not read (this type's value). [fileStorageContainerTypeSettings](https://learn.microsoft.com/en-us/graph/api/resources/filestoragecontainertypesettings?view=graph-rest-1.0), [registration-get](https://learn.microsoft.com/en-us/graph/api/filestoragecontainertyperegistration-get?view=graph-rest-1.0) |
| b | Narrowest container role for Office edit; what it also allows | **`writer`** (reader = read only; writer = read + create/update/delete). A container permission "applies to all its driveItem objects, regardless of any unique or restrictive permissions" — a writer can open and edit **every** file in the container, and under the open model can create sharing links. | High. [post-permissions](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-post-permissions?view=graph-rest-1.0) |
| b′ | How fast does a removed role stop an open Office session? | No SPE-specific propagation delay is documented. Word/PowerPoint for the web re-check permissions **at least every 5 minutes**, Excel every **15**; a save with a revoked token ejects all co-authors from the session. **CAE does not fire on ACL changes** (only disable/delete, password, MFA, token revoke, risk). Desktop: the next save fails, the local copy stays (INFERRED). So removal bites on the next permission re-check, not instantly. | Medium. [coauth](https://learn.microsoft.com/en-us/microsoft-365/cloud-storage-partner-program/online/scenarios/coauth), [CAE](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation) |
| b″ | Removal API side effect | `DELETE /storage/fileStorage/containers/{id}/permissions/{pid}` by default removes the identity's access to the container **and every item in it**; `Prefer: onlyRemoveContainerScopedPermission` keeps item-level permissions. The removal pass sends the header so it removes exactly what it granted. | VERIFIED. [delete-permissions](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-delete-permissions?view=graph-rest-1.0) |
| c | Does an edit link obtained app-only work? | The `webUrl` / `ms-word:ofe|u|…` URL is only a pointer: the user's own Office session needs a file or container permission (INFERRED from the "user has permission" prerequisite). So the BFF can READ the `webUrl` app-only; the edit works only where the user holds a role (standing BU writer, or JIT on a secure container). | High (pointer) |
| c′ | App-only preview for a non-member | Works — "anyone who accesses the URL acts as the caller with the caller's permissions". ⚠️ An app-only preview URL therefore carries the **BFF identity's** rights for whoever holds it; Microsoft recommends minting preview URLs with a read-only application identity. The external SPA already relies on app-only preview semantics for downloads; internal preview inherits the same property here. See Known limits / follow-up. | High. [driveitem-preview](https://learn.microsoft.com/en-us/graph/api/driveitem-preview?view=graph-rest-1.0) |
| c″ | App-only `createLink` | Application permission is listed for SPE (`FileStorageContainer.Selected` + container-type permission); no standard-vs-trial restriction documented. | Medium. [createLink](https://learn.microsoft.com/en-us/graph/api/driveitem-createlink?view=graph-rest-1.0) |
| d | Can a grant carry a marker so the removal pass can tell its own grants from hand-granted / owner roles? | **The permission object has no marker field** and Graph does not expose who granted it. **Container `customProperties`** are app-only writable string key/values (add/update/delete; `isSearchable` default false) — no size or count limit is documented. Ownership rule built from verified behaviour: grant with `@microsoft.graph.conflictBehavior=fail`; a **409** means the user already held a role (never ours — never record, never remove); on **201** record one custom property per grant. **No new Dataverse column or table is needed** — escalation trigger 3 does not fire. | Medium (no documented cap). [post-customproperty](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-post-customproperty?view=graph-rest-1.0), [permission](https://learn.microsoft.com/en-us/graph/api/resources/permission?view=graph-rest-beta) |
| e | App-only list/add/remove for a sync job; limits | List/add/delete all work app-only with `FileStorageContainer.Selected` + the container-type permission (`full` held by the BFF MI on 8a6ce34c, per the topology doc's 2026-10-04 verified state). No member cap documented. 5 RU per permission op; 3,000 RU/min per container, 12,000 RU/min per app per tenant → ~600 permission ops/min/container. B2B guests can be members unless tenant guest sharing is off. | VERIFIED. [limits](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/limits-calling-patterns) |
| f | Can the BFF identity (MI) read a file a USER uploaded OBO? (the 2026-06-08 "writer-identity matching" pattern says no) | **Yes, today.** The pattern predates the MI's registration on the container type; the topology doc records (2026-10-04, read-only probe) that the BFF MI holds `full` app-only + delegated on 8a6ce34c, and task 166's migration read **367 user-uploaded documents app-only** and moved them. `/api/documents/{id}/download` (app-only) works for user-uploaded files on dev. The pattern file `.claude/patterns/auth/spe-writer-identity-matching.md` is stale — see the `.claude` text in the final report. | High (dev evidence) |

### What step 0 means for the design

- **Round 70 stands** (owner premise "SPE access is container-level" is the documented behaviour of a CONTAINER
  permission). Per amendment 1 the finding is REPORTED, not acted on: **per-file edit grants to non-members DO exist**
  (driveItem invite, documented for Office for the web; desktop unproven). On a per-record secure container a
  container-level JIT writer reaches exactly that record's documents, so the narrower option buys little there; it
  would matter on BU containers, where round 70 chose standing membership instead. The owner decides whether to
  revisit.
- **Escalation trigger 1 does not fire**: under round 70 the container-level JIT grant is on SECURE (per-record)
  containers only; BU containers use standing membership by owner decision.
- **Escalation trigger 3 does not fire**: grants are told apart by the 201/409 rule + a per-grant container custom
  property; no Dataverse schema.
- **Self-registration Step 8**: round 70's standing sync makes it redundant for internal users in a BU with a
  container. Left in place (owner round 69 (4)); see step 7.

## Step 1 — inventory (every SPE byte path that ran as the user, plus the contact plane)

Read at `origin/master` `b403c7713`. "Pointer check" = `RecordContainerResolver.EnsureDocumentPointerContainerAsync`
(task 166): before the BFF follows a `sprk_document` row's `sprk_graphdriveid`/`sprk_graphitemid` AS THE
APPLICATION it verifies the pointer names a container (and item) this document may use. Under OBO, SPE's own
container check stood in for it; under app-only it is mandatory, or a Write holder who re-points a row could read
another container's file.

### Workforce byte paths

| Route / caller | Filter (unchanged) | Drive + item from | Was the OBO call the decision? | Disposition |
|---|---|---|---|---|
| `PUT /api/obo/records/{e}/{id}/files/{*path}` | `RecordRouteAccessAuthorizationFilter(AssociateContent)` | server: `RecordContainerResolver.ResolveForRecordAsync` | no (second layer) | **app-only** `UploadSmallAsync` |
| `POST /api/obo/records/{e}/{id}/upload-session` | same | same | no | **app-only** `CreateUploadSessionAsync` (new; OBO twin replaced) |
| `PUT /api/obo/me/files/{*path}` (record-less) | resolvable acting user | server: acting user's BU container | **yes** (SPE membership was the only population check) | **app-only after an explicit Dataverse check of the round-70 population**: an enabled, internal (`sprk_isexternal` not true) person user of that BU — exactly who the standing sync makes a writer there |
| `GET /api/documents/{id}/preview-url`, `/preview`, `/content`, `/view-url` | `DocumentAuthorizationFilter("read")` | the row | no (defence in depth) | **app-only** + pointer check |
| `GET /api/documents/{id}/office`, `/open-links` | `DocumentAuthorizationFilter("read")` | the row | no | **app-only** metadata (`webUrl` is a pointer) + pointer check + **JIT on a secure container** (step 4) |
| `POST /api/documents/{id}/share-link` | `DocumentAuthorizationFilter("share")` | the row | no | **app-only** `createLink` + pointer check |
| `GET /api/documents/{id}/versions`, `/versions/{v}/content` | `DocumentAuthorizationFilter("read")` | the row | no | **app-only** + pointer check |
| `POST /api/office/save` version save (`OfficeStorageUploader` replace) | Office filters + `OfficeVersionSaveAuthorizationFilter` → `DocumentAuthorizationFilter("write")` | the row | no | **app-only** `ReplaceFileContentAsync` + pointer check |
| `GET /api/communications/{id}/attachments/text` (`CommunicationAttachmentTextService`) | rows read impersonated (Dataverse row security decides) | the rows | no | **app-only** + pointer check per document |
| AI: `AnalysisDocumentLoader` (analysis execute, chat rerun, Compose background profile via `DocumentTextSource`) | `AnalysisExecute`/`AnalysisRun` filters; session docs by `AiAuthorizationFilter` | the row | no | **app-only** + pointer check |
| AI: `DocumentContextService` chat document load | `SessionOwnership` + `AiAuthorizationFilter` | the row | no | **app-only** — the app-only branch with the pointer check already existed; the OBO branch is removed |
| AI: `FileIndexingService.IndexFileAsync` (`/api/ai/rag/index-file`, send-to-index, post-upload enqueuer) | Targeted filter: Write on the named document | the row when a `DocumentId` is named | no, WITH a document | **app-only** (`IndexFileAppOnlyAsync`, pointer-checked) when a `DocumentId` is named |
| Compose: load, save, apply-template, pull/reanchor annotations, check-changes, PDF-intake probe, create-on-save dedup | **none** (`/api/compose` group is sign-in only) | **client** (route item id, body/query drive) | **YES** — every one | **tied to the row** by a new `ComposeDocumentAuthorizationFilter`: item id → `sprk_document` (`sprk_graphitemid_uk`) → `DocumentAuthorizationFilter` semantics (`read` / `write`), drive taken from the ROW, pointer checked, then **app-only**. Create-on-save dedup: Write on the matched row before the app-only replace. New-item create-on-save: matter path authorized (Append To) → app-only; no-matter path → the `/me/files` rule |

### NOT converted — escalation trigger 2 (client-chosen ids, no record), or not a byte path

| Path | Why it stays OBO | Options for the owner |
|---|---|---|
| **Compose "Path B"** — a document opened by drive+item that has **no `sprk_document` row** (load / save / annotations / check-changes), and the chat-session SPE-item check in `AiAuthorizationFilter` that authorizes those sessions | No record exists to authorize against; the OBO read IS the decision. Converting would make it always-allow. With round 70's standing membership OBO keeps working for internal users of the BU container; it does not work on a secure container (but a secure container's documents all have rows). | (A) keep OBO for rowless items (as built); (B) refuse rowless Compose and require a row (create one on first open); (C) treat rowless items as BU content under the `/me/files` rule |
| `POST /api/ai/rag/index-file` **without** a `DocumentId` | client drive+item, no row; the OBO download is the gate | (A) keep (as built); (B) require a `DocumentId`; the DocumentUploadWizard sends one when it has it |
| Staging uploads: chat **persist** (`ChatDocumentEndpoints`), chat **Word export**, workspace **pre-fill** (matter + project) | Container comes from config (`SharePointEmbedded:StagingContainerId`), filters only check identity; the user's SPE write right on the staging container is the only population check, so app-only would let any signed-in user write there | (A) keep OBO (as built; works only for staging-container members); (B) app-only under the `/me/files` internal-user rule; (C) move persist/export into the session's record container; (D) drop the pre-fill staging write (its result is discarded) |
| `POST /api/documents/resolve-identity` (Graph `/shares`) | Not a byte path: resolves the URL of the file the user has OPEN in Word, and opening an SPE file in Word already requires the user's role (standing BU writer or JIT). App-only would become an existence oracle for arbitrary URLs. | keep |
| `GET /api/me/capabilities?containerId=` | A pure OBO probe of the user's own SPE rights; no client callers | keep, or delete under the round-10 rule (no caller) — owner call |

### Contact plane (external SPA + Teams / workforce-contact — one route group, `ExternalCollaboration` policy)

All three byte routes are already **app-only after their grant check**, derive the container from the record / row
pointer, run the pointer check, and therefore work on a secure project's own container: `GET
/api/v1/external/projects/{id}/documents/{documentId}/content`, `POST /api/v1/external/projects/{id}/documents`,
`GET /api/v1/external/projects/{id}/documents/{documentId}/versions`. No preview, edit link or delete exists for
contacts, and nothing grants a contact an SPE permission. Nothing to convert. Observation: the external SPA's
`/upload` page calls the workforce-only record-keyed OBO route (a CIAM token gets 401); no link reaches it.
