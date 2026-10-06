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
| d | Can a grant carry a marker so the removal pass can tell its own grants from hand-granted / owner roles? | **The permission object has no marker field** and Graph does not expose who granted it. **Container `customProperties`** are app-only writable string key/values (add/update/delete; `isSearchable` default false) — no size or count limit is documented. Ownership rule: grant with a PLAIN `POST …/permissions` — the container-permission API takes no conflict parameter, and the code sends none (corrected 2026-10-06, adversarial finding 11: an earlier draft of this row said `@microsoft.graph.conflictBehavior=fail`). The rule relies on Graph answering **409** when the user already holds a role on the container (never ours — never record, never remove); on **201** record one custom property per grant. The 409 is pinned by live check **D2** (hand-granted members are left untouched, with no `SprkStd…` marker). **No new Dataverse column or table is needed** — escalation trigger 3 does not fire. | Medium (no documented cap). [post-customproperty](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-post-customproperty?view=graph-rest-1.0), [permission](https://learn.microsoft.com/en-us/graph/api/resources/permission?view=graph-rest-beta) |
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

### Step 0 addendum (owner amendment 8, relayed 2026-10-06): Word DESKTOP

| Question | Answer | Source |
|---|---|---|
| Does desktop Word need a role even to open READ-ONLY? | **Yes.** `ms-word:ofv|u|…` / `ofe|u|…` carry no credential; desktop Office signs the user in and SPE requires "the user has permission to view or edit the file". | VERIFIED — [Office URI Schemes](https://learn.microsoft.com/en-us/office/client-developer/office-uri-schemes), [open-office-files](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/open-office-files) |
| Consequence for the build | On a SECURE container the JIT grant is made **before** `/open-links` (and `/office`) returns the `ms-word:` link. A Read-only user gets no grant (POML step 4: grant only on Write), so on a secure container they cannot open in desktop at all — view is the in-app preview. Owner option: a JIT **reader** grant for Read-only desktop opens (not built; would be a second grant kind). | — |
| Residual window after removal (desktop) | **No documented desktop re-check interval.** Writes (AutoSave every few seconds, co-authoring sync, explicit save) fail at the next server round-trip → seconds to a few minutes while editing. Content already open stays readable (and can be copied / saved locally) until the document is closed. CAE does NOT fire on ACL changes; access tokens last 60–90 min (up to 24–28 h with CAE) but SharePoint checks file permission per request, so token lifetime does not extend file access. Treat removal as "blocks future opens and saves", not "revokes an open copy". | VERIFIED: [coauth](https://learn.microsoft.com/en-us/microsoft-365/cloud-storage-partner-program/online/scenarios/coauth), [CAE](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation), [token lifetimes](https://learn.microsoft.com/en-us/entra/identity-platform/configurable-token-lifetimes), [AutoSave](https://support.microsoft.com/en-us/office/what-is-autosave-6d6bd723-ebfd-4e40-b5f6-ae6e8088f7a5); window INFERRED |
| Are desktop saves on a SECURE container picked up like on a BU container? | A desktop save creates a new driveItem version (first AutoSave edit, then ~every 10 min). Compose change detection uses an **app-only** drive-root subscription + delta (`SpeSyncOrchestrator`, created on Compose load for the document's drive) — app-only, so it works on a secure container exactly as on a BU container (no membership involved). Version history is read app-only (task 171), so a desktop save shows in it on either container. **AI re-index is NOT triggered by a desktop save on ANY container** — it runs from the BFF save paths (Compose, Office add-in, upload); nothing re-indexes on a drive change. That is pre-existing and identical on BU and secure containers (parity holds; the gap is reported, not built — it would be new background work). driveItem notification latency: < 1 min average, **max 6 h** — the poll-on-focus `check-changes` path is the backstop. Live webhook fire has never been verified (compose-r2 spike 6) → in the live gate. | VERIFIED: [SPE webhooks](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/respond-to-changes-webhooks), [change-notifications overview](https://learn.microsoft.com/en-us/graph/change-notifications-overview) |

## What was built (steps 2–6)

**Facade (ADR-007 — Graph only in `Infrastructure/Graph` / `Infrastructure/ExternalAccess`).** Each byte operation now has ONE
body (`…CoreAsync(GraphServiceClient, …)`) so the typed error translation cannot drift between identities; the public
members are app-only, plus OBO members kept ONLY for the record-less paths in the table above.
- `UploadSessionManager`: app-only `UploadSmallAsync` inherits the OBO twin's typed translation (403/409/412/413/423/429);
  `UploadSmallAsUserAsync` deleted; new app-only `CreateUploadSessionAsync` (replaces the OBO one that 403'd) and
  `ReplaceFileContentAsync` (If-Match); `UploadSmallToStagingAsUserAsync` is the one OBO upload left (staging only).
- `DriveItemOperations`: app-only `GetFileMetadataUncachedAsync` (never the Redis cache — Compose sends the ETag in
  If-Match), `GetCurrentVersionIdAsync`, `GetEmbedPreviewUrlAsync`, `GetDriveItemAsync`, `CreateSharingLinkAsync`;
  downloads / version downloads share one core with 403 → `UnauthorizedAccessException`. Deleted: the OBO
  `ListFileVersionsAsUserAsync`, `GetPreviewUrlAsUserAsync`, `GetDriveItemAsUserAsync`, `CreateSharingLinkAsUserAsync`,
  `GetContentStreamAsUserAsync`, and four uncalled OBO byte methods (`ListChildrenAsUserAsync`,
  `DownloadFileWithRangeAsUserAsync`, `UpdateItemAsUserAsync`, `DeleteItemAsUserAsync`).

**Routes / services converted** — see the step-1 table. Every row-pointer read runs the document-pointer check first.

**Compose tie** — `Api/Filters/ComposeDocumentAuthorizationFilter.cs` on load (read), save (write), apply-template (write),
pull / reanchor annotations (read), check-changes (read): finds the row by `sprk_graphitemid` (the SAME lookup the save
uses, duplicate self-heal included), delegates to `DocumentAuthorizationFilter`, verifies the pointer, and marks the
request with the row's own drive+item (`ComposeBrokeredDocument`). `Services/Compose/ComposeSpeAccess.cs` is the ONE
identity rule for every Compose byte call: app-only exactly when the mark covers the (drive, item); otherwise OBO
(Path B). Create-on-save: new item app-only with `ConflictBehavior.Rename` (a same-named file in a shared container is
never overwritten by someone else's first save); transient-key hit requires Write on the matched row (as the caller) +
pointer check before the app-only replace.

**Step 4 — JIT Office edit** — `Services/Documents/OfficeEditAccessService.cs`, called by `/office` and `/open-links` before
the URL is returned: secure container (`RecordContainerResolver.ResolveOwningRecordAsync`) + Write on the secure record
(`CallerRecordAccessProbe`, as the caller) → `SpeContainerMembershipService.GrantMarkedWriterAsync` (writer; 409 = already
held → reuse; marker write failure → the grant is undone). No Write → no grant. A Write holder whose grant fails → 503
`edit_access_unavailable`. `/office` now reports `canEdit` honestly instead of a hard-coded `true`. Code review
(2026-10-06) added: an external caller (`sprk_isexternal = true`) on a Restricted secure record — or one whose flag cannot
be read — gets NO grant (the removal pass's round-67 rule, applied up front, so a grant is never made only to be removed
five minutes later); a caller already holding a non-editing role is reported `canEdit=false`, `role="member-read-only"`.
The standing pass self-heals a STALE marker (its permission removed outside this code): the marker is cleared and the
eligible user granted again, instead of being blocked forever.

**Step 5 + round 70 — one sync job** — `Services/Access/SpeContainerMembershipSync.cs` + `SpeContainerMembershipSyncJob.cs`
(ADR-036 `IScheduledJob`, `*/5 * * * *`, enabled): (1) standing writers on every BU container; (2) removal of JIT grants on
secure containers. Marker = container custom property `SprkStd<systemuserid:N>` / `SprkJit<systemuserid:N>` = permission id.
Removal uses `Prefer: onlyRemoveContainerScopedPermission` and deletes only a permission still shaped `["writer"]`.
**Why a new job, not an extension** (§11): every existing reconciliation job keeps Dataverse SHARES; this one's single
reason to change is SPE container roles. Existing primitives reused: `SpeContainerMembershipService` (extended),
`InternalShareEndpoints.ClassifyEligibility` (the one "enabled person" rule), `IDataverseRecordShareService.GetPrincipalRightsAsync`
(RetrievePrincipalAccess as the user — answer vs fault), `ISecurableEntityRegistry`.

**Step 6** — the blanket 403 text ("api identity lacks required container-type permission") is gone from
`GraphErrorTranslator` and `ProblemDetailsHelper`; a non-`Authorization_RequestDenied` 403 now reads "SharePoint Embedded
denied access to this container or item: {Graph's message}". `docs/standards/INTEGRATION-CONTRACTS.md` updated.

**Step 7 — docs** — `SECURE-DOCUMENTS-BUILD-PLAN.md` §1 (amendment box), `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`
§7.5 (no per-user step; the job and its interval; JIT; `isSharingRestricted` recommendation; the "Modified by" trade-off),
`docs/guides/SPAARKE-SELF-SERVICE-USER-REGISTRATION.md` (Step 8 now redundant, kept; troubleshooting split view vs edit).

## Placement justification (root CLAUDE.md §10 / bff-extensions §A, §D)

| Component | Placement | Why |
|---|---|---|
| Facade members (app-only twins) | BFF `Infrastructure/Graph` | ADR-007: the only Graph layer. Twins on the existing classes, same `ForApp()` client — no new service. |
| `ComposeDocumentAuthorizationFilter` | BFF `Api/Filters` | ADR-008 endpoint filter; delegates to `DocumentAuthorizationFilter` (the existing decision). |
| `ComposeSpeAccess` | BFF `Services/Compose` | One identity rule for ~15 Compose call sites; static, no DI. |
| `OfficeEditAccessService` | BFF `Services/Documents` | Request-path decision combining resolver + probe + grant; two routes need it. |
| `SpeContainerMembershipSync(+Job)` | BFF, in-process `Spaarke.Scheduling` (ADR-052) | Schedule-driven, low volume, BFF identity, BFF domain code (B2/B3) — the same placement as the other access reconciliation jobs; Functions/Container Apps would add a deployable for a 5-minute loop over BFF-owned primitives. |

No new NuGet package, no new Dataverse column/table, no new configuration key, no new `.WithClientSecret`.

## Post-deploy live gate (dev) — for the main session to run AFTER deploying this branch

Pre-reqs: dev BFF on this branch; a non-admin internal test user **U** (e.g. testuser1) who is a member of NO container
(read `GET /api/spe/containers/{id}/permissions?configId=c3a25b9a…` for the BU1 and the target secure container and record
the starting lists); a second internal user **V** with NO rights on the secure record; App Insights access. Every check
names the evidence to capture.

**A. Broker-only bytes on a SECURE record (acceptance 1–3)** — U holds Write on a fresh secure project PS (its own
container, 0 members):
1. `PUT /api/obo/records/sprk_project/{PS}/files/g171-small.txt` → 200; App Insights shows the Graph PUT under the BFF app
   (no OBO exchange on the request).
2. `POST /api/obo/records/sprk_project/{PS}/upload-session?path=g171-big.bin` → 200 with an `uploadUrl`; PUT one chunk to it
   → 201. (This is the exact 2026-10-06 upload403 call.)
3. Create the `sprk_document` row for (1) the way the UI does; then as U: `preview-url`, `preview`, `content`, `view-url`,
   `versions`, `versions/{v}/content`, `/download` → all 200.
4. Compose: open the document (`GET /api/compose/documents/{item}?driveId=…`) → 200; save a small edit → 200; the new
   version is visible in `versions`.
5. AI: run an analysis / a chat over the document → the text is extracted (not the "Failed to download" placeholder).
6. Read U's permissions on PS's container again → **still empty** (nothing above granted a role).
7. As V (no rights): every route in 1–5 → the routes' existing 403/404, and App Insights shows **no Graph call** for V.

**B. JIT Office edit (acceptance 4)**
1. As U: `GET /api/documents/{doc}/open-links` → 200 with `desktopUrl` + `webUrl`; PS's container now lists **exactly one**
   `writer` for U, and its custom properties include `SprkJit<U systemuserid, N format>` = that permission id. `webUrl` in
   Word for the web → editable; `desktopUrl` (`ms-word:ofe|u|…`) → Word desktop opens it editable.
2. Repeat `open-links` → still exactly one permission (reused).
3. A Read-only user R on PS: `open-links` → 200 and `GET /api/documents/{doc}/office` reports `canEdit: false`; **no**
   permission added for R; SharePoint refuses R's `webUrl` (expected: R's view path is the in-app preview).

**C. JIT removal (acceptance 5)** — remove U's Write on PS (unshare U, or a No Access entry for U):
1. `POST /api/admin/jobs/spe-container-membership-sync/trigger` (SystemAdmin), or wait ≤5 min.
2. `GET /api/admin/jobs/spe-container-membership-sync/status` shows `jit.removed ≥ 1`; PS's permission list no longer has
   U; the `SprkJit…` property is gone; hand-granted / owner roles on every container unchanged.
   If instead `jit.unknown ≥ 1` and App Insights logs "403 with error code X — not an access answer": record X. That is
   how Dataverse answers an unshared user's impersonated RetrievePrincipalAccess; if X is an access denial, add it to
   `RetrievePrincipalRightsOrUnknownAsync` (finding 3). A `CannotActOnBehalfOfAnotherUser` (0x8004A110) there means the
   BFF application user lacks the Delegate privilege — fix the role, not the code.
3. With U's Word session still open: record how long saves keep working after removal (expected: the next AutoSave fails
   within minutes; the open copy stays readable) — the observed residual window for owner amendment 8.

**D. Standing writers (amendment 6 (i)–(iii))**
0. **The 409 the marker rule depends on (finding 11).** Before the first run, with the deployed BFF, trigger an edit-open
   (`GET /api/documents/{doc}/office` on a SECURE document) for a user who ALREADY holds a writer role on that container
   (hand-add one first if none): App Insights shows the permission POST answered **409**, `role` = `member`, and the
   container gains NO `SprkJit…` property and the user's role is unchanged. If Graph instead answers 2xx (re-granting or
   changing the role), STOP — the "a role we did not create is never recorded" rule does not hold and must be redesigned.
1. Before the first run record BU1 container b!vzGD…'s roles (Ralph owner, Eyal writer, testuser1 writer — hand-granted).
2. Trigger the job: `standing.granted` = enabled internal person users of BU1 (and of every other unit with a stamped
   container) who held no role; Ralph / Eyal / testuser1 untouched (no marker).
3. Create or enable an internal user W in BU1 → the next run adds W as writer with a `SprkStd…` marker. Disable W → the
   next run removes it. Set `sprk_isexternal = true` on a granted user → removed; a flagged-external user is never added.
4. Record the run's duration and any Graph 429s (a large unit converges over runs; ≤200 grants per run).

**E. Contact plane (acceptance 8)** — CIAM Test User with a grant on PS: `POST /api/v1/external/projects/{PS}/documents`
→ 201 into PS's container; `…/documents/{doc}/content` → 200; `…/versions` → 200; a contact with no grant → 403; the
contact holds no SPE permission.

**F. Make Secure (acceptance 9)** — a non-secure project with one document; Make Secure; after relocation a non-member
sharee previews and downloads it → 200 (the round-68 known risk closes).

**G. Office add-in** — Word add-in "save new version" of a PS document by U → 202 + new version; Outlook "save attachment
to PS" → document created.

**H. Desktop change detection on a SECURE container (amendment 8)** — open a PS document in Compose (this creates the
app-only drive subscription for PS's container), then edit and save it in Word **desktop** (JIT grant from B): Compose
`check-changes` reports `changed: true`, and if `Compose:Webhook:*` is provisioned the webhook fires (App Insights
`spe-doc-changed`) — the first live proof of the webhook leg; `versions` shows the desktop version. Repeat on a BU
document for parity. AI re-index is not triggered by a desktop save on either container (expected, pre-existing).

**B2. JIT refusal for external on Restricted** — set PS to Restricted (`sprk_accesspermission` = 100000002) and give an
internal user flagged `sprk_isexternal = true` Write on it (if Dataverse allows): `/office` → `canEdit: false`, no
permission added on PS's container.

**J. Adversarial-round checks (2026-10-06)**
1. Upload with `?conflictBehavior=replace` (record-keyed PUT, `upload-session`, `/me/files`) → **409**
   `upload_replace_not_supported`, nothing written; the DocumentUploadWizard offers only "Keep both" on a collision.
2. `/api/obo/me/files/x.txt` as a user flagged `sprk_isexternal = true` (or disabled) → **403** `acting_user_not_eligible`.
3. SPE admin: `GET …/containers/{BU1}/customproperties` shows no `SprkStd…`/`SprkJit…`; a PUT naming one → **403**
   `spe.admin.deny.container_grant_marker_server_owned`.
4. Self-registration of a demo user: the container gains `SprkStd<systemuserid>` = the new permission id.
5. `/office` on a BU document by a user of ANOTHER unit with no role → `canEdit: false`, `role: none`.

**I. Error text (acceptance 7)** — a remaining OBO 403 (e.g. Compose Path B on a container U has no role on) reads
"SharePoint Embedded denied access to this container or item: …", never "api identity lacks required container-type
permission".

## Verification (branch `task/uac-r2-171`)

- **Full BFF suite** (`tests/unit/Sprk.Bff.Api.Tests`, includes `tests/integration/**`) at `ef69cbe24`: **18,063 passed,
  16 failed, 54 skipped (18,133)**. All 16 failures were in ONE file (`AnalysisEndpointsAuthorizationContractTests`, task
  162): its host lacked the `RecordContainerResolver` that `AnalysisDocumentLoader` now needs. Fixed by registering the
  permissive test resolver. Re-run of that file + every class touched by the code-review fixes
  (`OfficeEditAccessServiceTests`, `SpeContainerMembershipSyncTests`, `ComposeDocumentAuthorizationFilterTests`,
  `FileAccessBrokerIdentityTests`, `OfficeEndpointsContractTests`): **135 passed, 0 failed, 7 skipped**.
- **ArchTests** (`tests/Spaarke.ArchTests`) after the review fixes: **809 / 809 passed**.
- **Publish size** (root CLAUDE.md §10.4): fresh short-path worktrees, `dotnet publish -c Release`, zipped with PowerShell
  `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*` (the `Deploy-BffApi.ps1` method), PDBs included
  (4 per side), **192 files on both sides**:
  master `b977fc7a6` (`C:\p171m`) **36.13 MB** (37,887,025 B) vs branch `ef69cbe24` (`C:\p171b`) **36.15 MB**
  (37,909,319 B) = **+0.02 MB** (+22,294 B). (Master itself is below the 45.42 MB recorded baseline — the baseline aged;
  the delta is the measurement.) The later review fixes are source-only edits of a few dozen lines.
- **CVE**: `dotnet list package --vulnerable --include-transitive` → no vulnerable packages (branch); no package changes.
- **Step 9.5 gates**: code-review — 3 defects fixed (above), 2 performance items recorded below; adr-check — no
  violations (ADR-007 Graph stays in `Infrastructure/*`; ADR-008 filters; ADR-010 concretes; ADR-013 no new AI-internal
  injection; ADR-028 A4 no `.WithClientSecret`; ADR-036/052 `AddScheduledJob`; ADR-002 no plugin, no Dataverse write;
  ADR-038 no banned test shapes added).

### Adversarial verification round (2026-10-06) — fix-then-ship

| # | Change |
|---|---|
| 1 | App-only uploads (record-keyed PUT, `upload-session`, `/me/files`) never REPLACE: `conflictBehavior=replace` → 409 `upload_replace_not_supported`, decided before any Dataverse/Graph call; unknown values → `fail` (`ConflictBehaviorExtensions` default too). SdapClient: `UploadReplaceNotSupportedError` (a refused replace is not re-offered as a collision); DocumentUploadWizard offers only "Keep both". Oracle reasoning + the missing web version-upload route: known limits below. |
| 2 | `ResolveForActingUserAsync` (record-less: `/me/files`, Compose matter-less create-on-save, Office no-record save) refuses 403 `acting_user_not_eligible` unless `SpeContainerMembershipSync.IsStandingEligible` (enabled internal person; blank `sprk_isexternal` = internal). |
| 3 | JIT removal uses `RetrievePrincipalRightsOrUnknownAsync`: 403 counts as "no rights" only for `0x80040220`; 404 = cannot read; any other 403 (incl. `CannotActOnBehalfOfAnotherUser` 0x8004A110) or unreadable → UNKNOWN → kept + logged. Pinned at the wire (`DataverseRecordShareWireTests`). |
| 5 | Pointer-refusal route tests for preview-url, preview, content, view-url, office, open-links, versions, version content, share-link: 409 `document_storage_unverified`, zero Graph calls. Seed-verified: removing `/content`'s pointer check failed exactly that case. |
| 6 | Self-registration Step 8 grants through `GrantMarkedWriterAsync` (standing marker) — reconciled by the sync; the granter arch guard is now TWO types. |
| 7 | `SprkStd…`/`SprkJit…` custom properties are reserved in the SPE admin editor: hidden from GET/PUT responses; any PUT naming one → 403 `spe.admin.deny.container_grant_marker_server_owned`. |
| 9 | `/office` on a shared container reports `canEdit` only for a standing writer OF THAT container (eligible + own unit maps to it) or a caller holding an editing role; `/open-links` (grant side only) skips those reads. |
| 11 | Note and code comment now both say: plain POST, relies on Graph's 409 for an existing member — pinned by live check D0. |

Verification: full BFF suite **18,123 passed, 0 failed, 54 skipped (18,177)**; ArchTests **809/809**; BFF
`-warnaserror` build clean; SdapClient jest 18/18 + tsc clean; DocumentUploadWizard tsc — only environmental errors
(unbuilt shared packages), none in the changed files. Publish size: **source-only** (no package added). Deploy: BFF +
the DocumentUploadWizard code page (an old wizard's "Save as new version" now gets the 409 and re-shows its collision
prompt until the user picks "Keep both").

## Known limits (owner round 56 classes d–f, plus recorded trade-offs)

- **(adversarial round, finding 8 — recorded, no code)** Grants NO pass revisits:
  (i) a business unit RE-STAMPED to another container, or its stamp CLEARED — the standing pass walks containers that
  are stamped TODAY, so the old container's `SprkStd…` grants are never visited again (they stay until removed by hand
  or the container is retired);
  (ii) JIT grants on a secure record that is DELETED (or un-secured — the existing (e) row below) — the removal pass
  lists live secure records only;
  (iii) a grant whose marker write AND its undo both failed — logged `Critical` ("UNRECORDED writer permission … an
  operator must remove it"); nothing can tell it from a hand-granted role afterwards.
  Operator remedy for all three: the SPE admin console's permission list for the container.
- **(finding 1 — reasoning) The name-existence oracle.** Replace is refused BEFORE any Graph call, so that answer says
  nothing. The default `fail` still answers 409 when a same-named file exists in the destination folder, which tells
  the caller one bit: "a file of this name exists in this container". Today's container-member behaviour already gave
  that and more: a member lists the container. Who can ask now: a caller with AssociateContent on SOME record whose
  container this is — on a secure container, a person entitled to that record's documents; on a business-unit
  container, in practice its own standing writers (who can list it in Office anyway) plus anyone sharing a record of
  that unit. The residual — a user of another unit learning that a file NAME exists in a unit container they share one
  record of — is name-only (no content, no metadata, no id), and avoiding it would mean server-generated upload names,
  which changes the flat-path contract (`SpeUploadPathIsFlatGuardTests`) and the duplicate-file UX. Recorded, not built.
- **(finding 1)** There is no web "upload this file as a new version of document X" route. Replace-on-upload was the
  only way to do that from the upload wizard, and it is now refused; a new version comes from Word / the Office add-in /
  Compose. A Write-gated `POST /api/documents/{id}/versions` would be new surface — an owner call.
- **(finding 2)** The round-70 eligibility now gates EVERY record-less write — `/me/files`, Compose matter-less
  create-on-save AND the Office add-in's no-record save — because it lives in `ResolveForActingUserAsync`, the one rule
  all three share. A disabled / external / non-person caller gets `403 acting_user_not_eligible` on all three.
- **(finding 3)** `RetrievePrincipalRightsOrUnknownAsync` counts a 403 as "no rights" only for Dataverse's access-check
  denial code `0x80040220` and a 404 as "cannot read". Whether an unshared user's impersonated `RetrievePrincipalAccess`
  answers 200-with-no-Write, 403/0x80040220 or 404 is not documented — live check **C2** shows which. If it is some other
  403 code, JIT grants are kept (logged UNKNOWN each pass) until that code is added. The deny-side callers of the
  lenient read (NoAccess enforcement, secure-designation removal) keep their mapping — reading a fault as "no rights"
  DENIES there, which is safe.
- **(finding 6)** Self-registration targets `environment.DataverseUrl`. When that is NOT the BFF's own Dataverse, the
  `SprkStd…` marker names a systemuser the BFF's sync cannot see; if the container is also not stamped on one of the
  BFF's business units the grant is never visited (same as before — but now at least marked).

- **(trade-off, owner question pending)** App-only writes make SharePoint's "Modified by" / the SPE version author the
  Spaarke app. The person is on the Dataverse row for creates (`createdby` / `sprk_createdbyperson`); there is no existing
  modifier-person column, so a later version's person is in the BFF logs only. Not built (it would be schema).
- **(trade-off)** App-only preview URLs carry the BFF identity's rights for their short life (Graph: "anyone who accesses
  the URL acts as the caller"). The routes never log or store them (two URL log lines were removed); Microsoft recommends
  a read-only app identity for minting them — an Entra app registration, out of scope (owner/ops follow-up).
- **(trade-off)** Under the container type's default "open" sharing model a standing or JIT writer can re-share a file from
  Office outside Dataverse. Recommend `isSharingRestricted = true` (container-type setting; not changed here).
- **(e)** A JIT grant on a container whose record is later UN-secured is no longer visited by the removal pass (it lists
  secure records only); the unsecure move abandons that container, so the grant reaches no current document.
- **(e)** The standing pass never removes a role it did not record, so a HAND-granted user later flagged external keeps
  that hand-granted role (amendment 6 (ii)'s "removed if already a member" holds for members this code added; it is the
  "never removes principals it did not grant" rule winning).
- **(e)** A user who already holds a hand-granted READER role on a secure container gets no JIT writer (a role is never
  changed); `/office` now reports it honestly as `canEdit=false`, `role="member-read-only"` (code review, 2026-10-06).
- **(perf, recorded)** ONE Graph app budget for every user. Broker-only means all SPE byte traffic counts against the BFF
  app's 12,000 RU/min per tenant (step 0 (e)) instead of each user's own delegated budget. Throttling surfaces as the
  existing 429 + Retry-After. Watch `429` rates after rollout; the escalation path is SPE's per-app limit raise, not code.
- **(perf, recorded)** The JIT removal pass reads ONE container (markers only) per secure record every 5 minutes —
  ~1 RU each, capped at 5,000 records (then TRUNCATED is reported). At a few hundred secure records this is noise; past
  ~2,000 it is a meaningful share of the budget above. The fix then is an index of containers that hold JIT grants
  (written at grant time) with the full sweep as a slower backstop — not built now (no evidence of that scale).
- **(e)** Compose PDF-intake resume on a secure container: the derived .docx is still probed OBO (the derived item's row
  is not authorized separately), so the resume falls back to re-projecting the PDF. Fails toward "show the PDF again".
- **(f)** A Read-only user cannot open a SECURE document in desktop Word, even read-only (a role is required; a JIT
  reader grant is not built — owner option).
- **(e)** Desktop saves do not trigger AI re-index on any container (pre-existing; parity with BU containers holds).
- **(d)** `SpeBrokerOnlyByteIdentityGuardTests` reads compiled IL; reflection / late binding are outside it (the scan's
  stated limit).
- **(perf, measured cost accepted)** Every converted read now runs the task-166 pointer check (one Graph creator read +
  Dataverse reads) and `/office` + `/open-links` add one secure-owner lookup — the cost `/download` already paid.
- **(observation)** The external SPA's `/upload` page calls the workforce-only record-keyed route (CIAM → 401); no link
  reaches it. Not changed.
