# Task 076 — client cutover + the container-supplier classification

> **Scope of THIS document**: the CLIENT half of 076 (POML steps 4–7), plus — added in a second pass
> the same day — the DocumentUploadWizard cutover and **the completed deletion of the legacy route**.
>
> **Date**: 2026-09-03 (two passes; §8 is the second).
>
> ⚠️ **§6 below is SUPERSEDED.** It says steps 2/3/8 are "not correct **yet**". They were executed in
> the second pass, because their one blocker — DocumentUploadWizard still calling the legacy route —
> was removed first. §8 is the record; §6 is kept only for its reasoning.

---

## 1 — 🔴 Six things the prior notes got WRONG. Do not re-derive them.

This project's handoff notes have been wrong repeatedly, and this session found six more. Each was
caught by checking the claim against code, which took under two minutes in every case.

| # | The claim | What the code says |
|---|---|---|
| 1 | **"12 container suppliers."** Stated in the POML §2 and repeated in every downstream note. | **WRONG — there are ~32**, of which ~23 are live. Two independent census passes agreed (31 / 32; the delta is one dead file counted differently). The narrowest defensible reading — *distinct hand-written `systemuser → businessunit.sprk_containerid` chains* — is **15**, still not 12. The "12" appears to have counted only the sites one earlier grep happened to surface. **A count in this project is a lead, never a fact.** |
| 2 | **"Route the 7 server-side Communication sites through 075's resolver"** (POML §3, and the instruction given to this session). | **ALREADY DONE — there is nothing left to route.** `Services/Communication/**` contains exactly **four** byte-moving calls (`UploadSmallAsync`), and **all four are already behind the resolver**: `CommunicationService.cs:2113` (resolved at :2104), `IncomingCommunicationProcessor.cs:926` + `:1101` (075), `MessageAttachmentMaterializer.cs:238` (resolved at :207). The other "sites" in the POML's list of 7 are pointer-*recording* or read-*lookup* sites where routing would create a dangling pointer or a 404 — the notes file's own §3 table said so correctly; the POML's §3 did not. Verified by grepping the whole folder for `UploadSmallAsync`, not by trusting either document. |
| 3 | **"W1 = delete `applyDefaultContainerId`."** | **INCOMPLETE — and following it literally would have left W1 unfixed in 4 of 5 wizards.** There are **four direct `entity['sprk_containerid'] = this._containerId` writes** that run *before* the helper, and two of them carry the comment *"Applied FIRST so it acts as an explicit override during the subsequent BU cascade (INV-5)"*. The helper was the **fallback**; these were the **primary**. Deleting only the helper would have removed the arm that almost never fired and left the one that always did. Sites: `CreateMatterWizard/matterService.ts:226`, `CreateInvoiceWizard/invoiceService.ts:218`, `CreateWorkAssignmentWizard/workAssignmentService.ts:427`, `LegalWorkspace/.../CreateMatter/matterService.ts:184`. All four now deleted. |
| 4 | **Implied: deleting the `containerId` half of `resolveUserBuDefaults` is part of W1.** | **It is NOT safe, and it was not done.** Of its 8 callers, **every one consumes `containerId`**, and **two consume nothing else** — `composeEditor.registration.ts:182` and `ComposeDirectWidget.tsx:208`, whose `resolveContainer()` would become a permanent `no-container` answer. That is the dishonest-banner failure `ComposeWorkspace.tsx` was specifically fixed to avoid. **No caller uses `searchIndexName` alone.** The resolver stays; only the `sprk_containerid` WRITE was removed. |
| 5 | **`sprk_graphdriveid` could keep coming from the client's container.** Not stated anywhere — which is the problem. | **This was a latent dangling-pointer bug that the cutover would have ACTIVATED.** `createDocumentRecords` wrote `sprk_graphdriveid: containerId ?? null` from the container the wizard resolved at OPEN time. That was survivable only while the client also *named* the upload destination, so the two agreed by construction. Under the record-keyed contract the server picks the container, so for a **secure** record they provably disagree: bytes land in the record's own container while the column points at the shared BU one — a pointer that 404s on every later download, on exactly the records that matter most. Now sourced from `file.driveId`. |
| 6 | **`DriveItem.driveId` might not be on the wire** (the AP-12 class this project keeps hitting — a type declaring a field the response lacks; cf. `parentReferenceId` → `parentId`). | **Verified present and always populated**: `UploadSessionManager.cs:313` maps `uploadedItem.ParentReference?.DriveId ?? containerId`, so `FileHandleDto.DriveId` is non-null on every successful upload even though the type declares it `string?`. Checked before depending on it, not after. |

---

## 2 — What the client cutover changed

### The chokepoint

`EntityCreationService.uploadFilesToSpe(containerId, files, onProgress)`
→ **`uploadFilesToSpe(entityLogicalName, recordId, files, onProgress)`** (record-keyed)
→ plus **`uploadFilesWithoutRecord(files, onProgress)`** (record-less).

Both delegate to one private `_uploadEach(files, uploadOne, onProgress)` so the two contracts cannot
drift in progress reporting, error collection, or `DriveItem → ISpeFileMetadata` mapping.

**The arity change is deliberate**: it makes every un-migrated call site a *compile error* rather
than a string that silently still fits.

### The 8 call sites (the POML said "6 + 1 more"; the sixth was in `src/solutions/`, not the shared lib)

| # | Site | Now calls |
|---|---|---|
| 1 | `Spaarke.UI.Components/.../CreateMatterWizard/matterService.ts` | `uploadFilesToSpe('sprk_matter', matterId, …)` |
| 2 | `solutions/LegalWorkspace/.../CreateMatter/matterService.ts` | `uploadFilesToSpe('sprk_matter', matterId, …)` |
| 3 | `.../CreateProjectWizard/CreateProjectWizard.tsx` | `uploadFilesToSpe('sprk_project', projectId, …)` |
| 4 | `.../CreateInvoiceWizard/invoiceService.ts` | `uploadFilesToSpe('sprk_invoice', invoiceId, …)` |
| 5 | `.../CreateWorkAssignmentWizard/workAssignmentService.ts` | `uploadFilesToSpe('sprk_workassignment', workAssignmentId, …)` |
| 6 | `.../CreateEventWizard/CreateEventWizard.tsx` | `uploadFilesToSpe('sprk_event', eventId, …)` |
| 7 | `.../EmailComposer/createXrmEmailComposeHandlers.ts` | `uploadFilesWithoutRecord(…)` — parentless |
| 8 | `Spaarke.AI.Widgets/.../CreateAnalysisWizardWidget.tsx` | `uploadFilesWithoutRecord(…)` — parentless |

**Site 3 is finding F-9 closing.** `CreateProjectWizard` provisions a secure project's own container
at step 1d and then, at step 2, uploaded into `context.speContainerId` — the **business-unit**
container resolved when the wizard opened — discarding the stamp provisioning had just written.
Because provisioning has already run by the time the upload happens, resolving server-side from
`projectId` reads the correct container.

### A guard that had to go with it

Every record-bearing site was wrapped in `if (files.length > 0 && <client-resolved container>)`.
That guard is deleted. Keeping it would have made the upload depend on a client-side lookup that is
no longer consulted — skipping uploads for a caller whose BU has no container even where the server
can resolve one from the record. The one user-visible arm it fed (LegalWorkspace's *"File upload
skipped — no SPE container configured"*) is deleted for the same reason: the client is no longer a
participant in that decision, so it is not in a position to render a verdict on it. Failures now
arrive per file through `uploadResult.errors` carrying the **server's** explanation (a secure record
with no container of its own fails closed and names the record).

### W1 and W2

- **W1** — `applyDefaultContainerId` deleted; `applyUserBuDefaults` narrowed to
  `{ searchIndexNameSet }` (dropping the property turns every caller that branched on it into a
  compile error); **plus the four direct writes** per §1 row 3.
- **W2** — the `Xrm.WebApi.updateRecord(parent, { sprk_containerid: buContainerId })` write-back in
  `DocumentUploadWizard/sprk_subgrid_commands.js` deleted. Its trigger was
  `formContext.getAttribute(...)` returning null — which means *"this column is not on this form"*,
  **not** *"this record has no container"*. So it overwrote a correctly-provisioned secure container
  whenever a user opened a form that simply did not display the field.

---

## 3 — Supplier classification (POML step 5)

Rule: *deleted (fed only uploads)* / *retained + owning task* / **SURVIVOR** (unclassified = failure).

### A. Dead by this change — the value is no longer read by any upload

| Supplier | Disposition |
|---|---|
| `EntityCreationService.applyDefaultContainerId` | **DELETED** |
| the `containerId` half of `applyUserBuDefaults` | **DELETED** |
| 4 × direct `entity['sprk_containerid'] = this._containerId` | **DELETED** |
| `sprk_subgrid_commands.js` write-back (W2) | **DELETED** |
| `createDocumentRecords` `options.containerId` at 7 call sites | **DELETED at the call sites**; the parameter itself is retained as a fallback for callers that build document rows for files they did not upload through this service |

### B. Retained — NOT upload suppliers; each has a named owner

| Supplier | Consumer | Owner |
|---|---|---|
| `SemanticSearchControl/NavigationService.ts:354-366` | `&containerId=` on an `Xrm.Navigation.navigateTo` **web-resource** launch of DocumentUploadWizard | ⚠️ **Head of an upload chain one hop downstream** — dies with the DocumentUploadWizard cutover (§5), not with the Semantic Search control |
| `webresources/js/sprk_wizard_commands.js:107-122` | same launch envelope | same as above |
| `LegalWorkspace/.../WorkspaceGrid.tsx:524-545` | same launch envelope (standalone mode) | same as above |
| `webresources/js/sprk_analysis_commands.js:58` | reads `sprk_containerid` off the **`sprk_document` form**; parameterizes the Analysis Builder page | **Retained — read/navigation, not bytes.** Owner: Analysis Builder |
| `FindSimilarWizardWidget.tsx:165-177`, `wizardLaunchers.ts:269`, `NextStepsStep.tsx:668`, `nextStepLauncher.ts:115/139` | `containerId=` on DocumentRelationshipViewer URLs | **Retained — read/navigation.** Owner: DocumentRelationshipViewer |
| `uploadOrchestrator.ts:482` | `record.driveId ?? parentContext.containerId` → RAG index-file | **Retained — INDEXING**, and it already prefers the server's `driveId` |
| `external-spa/.../DocumentUploadPage.tsx:142` | display only (`Container: …`); its upload resolves server-side (R15) | **Retained — display.** Owner: external-spa |
| `SummarizeFilesWizard/SummarizeAnalysisStep.tsx:57-122` | `POST /api/v1/documents` body `{name, containerId}` — a **document-row create**, not a byte upload | **Retained — owner: task 083** (`POST /api/v1/documents` carries a *Permanent* waiver: "CREATE. There is no pre-existing resource to authorize"). Flagged, not fixed here |
| `composeEditor.registration.ts:182`, `ComposeDirectWidget.tsx:208` | `containerId` in the `POST /api/compose/documents/create-on-save` body | **Retained — owner: the #858 CLIENT TAIL** (already tracked in `current-task.md`). ⚠️ **The server no longer reads it** — #858 deleted `SaveComposeDocumentRequest.ContainerId` and `System.Text.Json` drops unknown properties. These two are already vestigial, which is why removing them is safe to do separately and is NOT ship-together |
| `resolveUserBuDefaults` itself | 8 callers, all consuming `containerId` | **RETAINED — see §1 row 4.** Deleting the containerId half breaks the two Compose callers outright |

### C. Dead code found in passing — deletion candidates, NOT deleted here

| Surface | Evidence |
|---|---|
| `solutions/LegalWorkspace/src/services/xrmProvider.ts:97-140` `getSpeContainerIdFromBusinessUnit` | byte-identical copy of the SmartTodo function; **no importer anywhere in LegalWorkspace** |
| `Spaarke.UI.Components/src/services/RecordContainerResolver.ts` (whole file) | the **fail-CLOSED** client resolver. **Zero production callers** — only its own test + a barrel re-export. Ironically it is the correct implementation that `AssociateToStep`'s fail-OPEN one should have been |
| `getContainerIdForEntity` on both upload-service adapters | `GET /api/containers/{entity}/{id}`; **zero production callers** (mock + JSDoc only) |
| `ENTITY_CONFIGS[*].containerIdField` in `uploadOrchestrator.ts` (7 entries) | never read in the TS path (`DocumentRecordService` deliberately keeps `sprk_containerid` NULL on `sprk_document`) |

Not deleted because AP-11 applies — a static `from '...'` grep cannot establish dead code in this
repo (it once declared a `React.lazy`-loaded component unreferenced). Each needs the 10-channel check.

### D. 🔴 SURVIVORS — still live, still supply a container to an upload

**Stated plainly rather than classified away.** These remain because they all converge on ONE
consumer, the DocumentUploadWizard upload path, which is §5.

| Survivor | Why it is still alive |
|---|---|
| `DocumentUploadWizard/src/main.tsx:48` (`appParams.get("containerId")`) | feeds `effectiveContainerId` |
| `AssociateToStep.tsx:130-145` `resolveBusinessUnitContainerId` | the skip-path container |
| `AssociateToStep.tsx:147-163` `resolveContainerIdForRecord` | 🔴 **the fail-OPEN resolver — F-5, the POML's highest-value target.** A bare `catch {}` swallows 403/404/network identically to "the column does not exist" and returns the **shared BU container**. For a secure record whose container read is denied, the bytes go to shared storage. Its own neighbouring doc comment claims it "THROWS when no container is found" — **false**; only the BU helper throws |
| `useWizardPageBootstrap.ts:176-186` + 5 wizard-host `main.tsx` `resolveSpeContainerId` chains + `WorkspacePane.tsx:454` + `SmartTodo/xrmProvider.ts:97` | now **unused by the upload path** (the wizards no longer pass them), but the prop plumbing is still wired end-to-end. Vestigial, not harmful — but a survivor until the props are removed |

---

## 4 — 🔴 Ship-together obligation (unchanged, and it is the outage risk)

**This change is ADDITIVE and independently deployable, precisely because the legacy
container-keyed route stays alive.**

- The clients cut over here call `PUT /api/obo/records/{entity}/{id}/files/{*path}` and
  `PUT /api/obo/me/files/{*path}`. **Both already exist and are deployed-ready on the server side.**
- `PUT /api/obo/containers/{id}/files/{*path}` is still mapped, so any client that has NOT been cut
  over — notably DocumentUploadWizard (§3.D) — keeps working.

**The ship-together moment is the DELETION of the legacy route (POML steps 2/3/8), which this change
does NOT do.** When that lands:

- BFF ahead of client → client still calls the container-keyed route → **404 on every upload**.
- Client ahead of BFF → client calls the record-keyed route → **404 on every upload**.

No compatibility window, no feature flag. State it in that PR's description.

---

## 5 — ~~Remaining work~~ → items 1–3 DONE 2026-09-03 (second pass). See §8.

1. ✅ **DONE — DocumentUploadWizard cutover** — the last upload client on the legacy route, and the one that
   retires all of §3.D. It needs BOTH contracts, because the wizard has two branches:
   - **associate branch** (`resolvedParent` has `entityType` + `id`) → `uploadFileForRecord`. This
     is a *record-bearing* path, so converting it **deletes the fail-OPEN resolver** rather than
     correcting it.
   - **skip-associate branch** (user declined a parent) → `uploadFileWithoutRecord`.
   Files: `DocumentUploadWizardDialog.tsx` (`effectiveContainerId` :280, `parentContext` :429),
   `services/uploadOrchestrator.ts` (:220-225), `Spaarke.UI.Components/src/services/document-upload/`
   `MultiFileUploadService.ts` (:54) + `FileUploadService.ts` (:46-66) + its `FileUploadRequest`
   type, and `AssociateToStep.tsx`. Then the three launch envelopes in §3.B row 1–3 lose their
   `&containerId=` and the §3.D prop chains can be deleted.
2. ⚠️ **PARTIAL — the vestigial `speContainerId` prop chains** (§3.D row 4). DocumentUploadWizard's own
   chain is fully deleted (prop, URL param, three launch envelopes, `NextStepsStep` plumbing). The five
   OTHER wizard-host `main.tsx` chains + `useWizardPageBootstrap.ts:176-186` + `WorkspacePane.tsx:454`
   + `SmartTodo/xrmProvider.ts:97` are NOT — they feed non-upload consumers. Vestigial, not harmful;
   a separate cleanup. §8.3 has the exact after-grep.
3. ✅ **DONE — POML steps 2/3/8** — legacy route, its Pending waiver, `SdapApiClient.uploadFile()` and
   `UploadOperation.uploadSmall()` deleted in ONE commit. See §8.4.
4. Filed, not fixed: the outbound-attachment path records a **wrong `(driveId, itemId)` pair** —
   `CommunicationService.cs:1259` / `:1573` pass `_options.ArchiveContainerId` as the drive for
   documents whose bytes are at `docRecord.GraphDriveId`, and `ArchiveOutboundAttachmentsAsync:2308`
   writes the **`sprk_document` GUID** into `sprk_graphitemid`. Both halves of the pointer are wrong.
   Independent of container isolation; routing the resolver there would only make it differently
   wrong. Needs the real pair carried out of `DownloadAndBuildAttachmentsAsync`.
5. Filed, not fixed: `ArchiveToSpeAsync` and `MaterializeAsync` **throw** the resolver's two
   permanent refusal codes where the inbound helper **catches and skips** them. Both are fail-closed
   (no bytes written either way), but the divergence is unintended.

---

## 6 — Why steps 2/3/8 are NOT in this change

Deleting the legacy route while DocumentUploadWizard still calls it would 404 every upload from that
wizard — which is reachable from the Semantic Search control, the LegalWorkspace grid, the subgrid
ribbon, and the wizard command web resource. The deletion is correct and is the acceptance evidence
for the task; it is simply not correct **yet**. It is reserved for human review per operator
direction.

---

## 7 — Placement Justification (CLAUDE.md §10)

**No BFF change in this increment.** Server endpoints, services, DI registrations, packages and
background work are all untouched — the three routes this cutover targets already shipped. Publish
size and the CVE surface are therefore unchanged by construction, and the §10 checklist has no
applicable rows. `Services/Communication/**` was **inspected and deliberately not modified** (§1 row 2).

New client component: **one** — `uploadFilesWithoutRecord`, on the existing `EntityCreationService`.
Per CLAUDE.md §11: (1) *existing* — `uploadFilesToSpe` is the only overlap; (2) *extension* — it IS
the extension, sharing `_uploadEach` with its sibling rather than duplicating the loop; (3)
*cost-of-doing-nothing* — the three parentless flows have no callable upload path once
`uploadFilesToSpe` requires a record, and giving the record-keyed route a container parameter for
their benefit is option (B), which was rejected.

---

## 8 — Second pass, 2026-09-03: the wizard cutover and the deletion

### 8.1 — What DocumentUploadWizard now does

The chokepoint changed SHAPE rather than moving. `FileUploadRequest.driveId: string` became
`FileUploadRequest.target: UploadTarget`, a **discriminated union**:

```ts
type UploadTarget =
  | { kind: 'record'; entityLogicalName: string; recordId: string }  // PUT /api/obo/records/{e}/{id}/files/{path}
  | { kind: 'no-record' };                                           // PUT /api/obo/me/files/{path}
```

A union rather than an optional pair, deliberately: `{ entityLogicalName?, recordId? }` would let a
caller supply neither and silently get the record-less contract — which for a record-bearing flow
files a secure record's documents in the CALLER's business-unit container. The caller must state
which contract it is on. `FileUploadService` branches on `kind`; `MultiFileUploadService` forwards it
unchanged; `uploadOrchestrator.resolveUploadTarget` maps the wizard's two branches onto it using the
SAME condition `DocumentRecordService` already uses to decide whether to bind a parent lookup, so a
file cannot be uploaded against one record and filed under another.

`AssociateToStep`'s fail-OPEN `resolveContainerIdForRecord` is **deleted**, along with
`resolveBusinessUnitContainerId`, the `isResolving` state and the "Resolving container..." spinner.
`IResolvedParentContext.containerId` and `ParentContext.containerId` are deleted too, so no remaining
supplier compiles.

The name-collision path is intact: `UploadNameConflictError` -> `ServiceResult.nameConflict` -> the
two-option dialog. Its existing tests pass. Four NEW tests pin what was previously unpinned — **which
ROUTE each contract hits**, asserted on the URL, including that a record target missing either half
fails CLOSED rather than downgrading to `/api/obo/me/files`.

### 8.2 — Four things this pass found that sections 1-4 did not say

| # | Finding |
|---|---|
| 1 | **`DocumentRecordService` had the section-1-row-5 bug too, unfixed.** Row 5 recorded that `EntityCreationService.createDocumentRecords` wrote `sprk_graphdriveid` from the client's container, and fixed it THERE. The DocumentUploadWizard path uses a DIFFERENT service — `DocumentRecordService` — which wrote `sprk_graphdriveid: parentContext.containerId` at **three** sites. Same latent dangling-pointer bug, same activation condition, not mentioned anywhere. Now sourced from `SpeFileMetadata.driveId` (new field, mapped from `DriveItem.driveId`), and `ParentContext.containerId` is DELETED so a third copy cannot appear. |
| 2 | **Two launch envelopes did not merely APPEND a container — they REFUSED TO OPEN without one.** `NavigationService.openAddDocument` and `WorkspaceGrid.handleAddDocument` both had `if (!containerId) { console.error(...); return; }`, and `sprk_wizard_commands.js` showed the user *"Cannot upload: no SPE container is configured for your business unit"*. So a user whose BU had no container could not open the wizard **at all** — including for a secure matter with a perfectly good container of its own. Section 3.B classified all three as "head of an upload chain, dies with the cutover": true, but it undersells them. They were also a live availability bug. All three guards deleted. |
| 3 | **`NextStepsStep` threaded `containerId` through three components to reach two consumers, one of which never read it.** `WorkOnAnalysisStepContent` declared, received and destructured it and never used it. The real consumer, `FindSimilarStepContent`, put it on a DocumentRelationshipViewer URL that is about ONE document — while the value was one session-wide container. Now sourced per-document from that document's own `createResult.driveId`. |
| 4 | **The `?? containerId` fallback in the wizard's RAG-indexing call was load-bearing and wrong.** `triggerRagIndexing(record.driveId ?? config.parentContext.containerId, ...)` would, post-cutover, index the WRONG drive whenever `driveId` was absent. Replaced with an explicit skip plus warning: a file with no server-reported drive is not indexed at a guess. |

### 8.3 — The absence-grep (step B acceptance evidence)

**Question**: does any CLIENT code supply a container to an upload route, and does any client
`sprk_containerid` WRITE survive?

**BEFORE** — `src`, `.ts/.tsx/.js`, excluding built bundles:

| Probe | Result |
|---|---|
| `obo/containers/*/files` or `uploadSmall(` or `.uploadFile(` | **1 live production supplier**: `FileUploadService.ts:64` `this.apiClient.uploadFile(request.driveId, ...)`, reached from `MultiFileUploadService:54` <- `uploadOrchestrator:223` (`containerId: config.parentContext.containerId`) <- DocumentUploadWizard. Plus the `SdapApiClient.uploadFile` / `UploadOperation.uploadSmall` definitions and their tests. |
| `sprk_containerid` write shapes | Comments only (W1/W2 were already deleted in pass one). |

**AFTER**:

| Probe | Result |
|---|---|
| payload key `sprk_containerid:` | **0** |
| `['sprk_containerid'] =` / `.sprk_containerid =` | **0 live** — 2 hits, both COMMENTS describing the deletions |
| `getAttribute("sprk_containerid").setValue(` | **0** |
| `obo/containers/*/files` in client source | **0** — the definitions that held it are deleted |
| **a container supplied to an upload route** | **0 in source** |

**Two things the grep surfaced, stated plainly rather than classified away:**

- **`src/dataverse/webresources/spaarke_documents/DocumentOperations.js:262-310`** DOES read
  `sprk_containerid` off a form and PUT to an upload route — but `/api/drives/{driveId}/upload`, the
  **MI/app-only** route (task 083's territory), not the OBO one. **It is already dead**: its step-2 hop
  calls `GET /api/containers/{containerId}/drive`, and there are **ZERO `/api/containers/...` routes
  mapped on the BFF** — they were deleted 2026-08-25 (`DocumentsEndpoints.cs:15-31`). It throws
  *"Failed to get container drive information."* before reaching any upload. Same shape as the chunked
  path 076 deleted earlier: a client whose first hop hits a route mapped nowhere. Does not block the
  OBO deletion; NOT fixed here.
- **Seven checked-in PCF `bundle.js` artifacts** (`src/client/pcf/Communication*/Solution/...` and
  `TrackingFieldTrio`) still contain an inlined `fetch(".../api/obo/containers/{id}/files/...")` — the
  OLD `EntityCreationService` raw-fetch implementation. They are **build outputs dated 2026-07-29**,
  five weeks before the cutover, regenerated by `npm run build:prod`. Not source, not a supplier — but
  a **deploy obligation**: rebuild and redeploy them, or verify they never reach that code.

### 8.4 — Step 6 EXECUTED, and what it cost beyond the five listed items

Deleted together in ONE commit: the route (`OBOEndpoints.cs`, 54 lines), its **Pending** waiver
(deleted, **not** converted to Permanent — converting inverts the forcing function), the deprecated
`SdapApiClient.uploadFile` and `UploadOperation.uploadSmall`, the legacy sink entry, and the
OBOEndpoints route-count assertion `4 -> 3`.

**Five further changes the deletion FORCED, none optional:**

1. **`SpeWriteSinkContainerProvenanceGuardTests` ordinals RENUMBER.** Ordinals are assigned per
   `(file, sink)` in FILE order. The deleted route held the FIRST `UploadSmallAsUserAsync` in
   `OBOEndpoints.cs`, so `#2 -> #1` (record-keyed) and `#3 -> #2` (record-less). Deleting only the
   ordinal-1 entry fails Rule A **in both directions in one run** — two undeclared sites AND two stale
   declarations. The ClientSupplied census header also moves 7 -> 6.
2. **Three positive controls asserted the route was STILL MAPPED** and would have failed:
   `MiContainerKeyedWriteRouteRetirementTests` (x2) and `OboDriveKeyedRouteRetirementTests` (x1). They
   are the ONLY positive controls in those files — deleting them would make every absence assertion in
   both files vacuous, which the files say about themselves. Re-pointed at `PUT /api/obo/me/files/{*path}`.
   `OboDriveKeyed` additionally gained a NEW absence assertion for the deleted route; its own doc block
   had pre-authorised exactly this flip ("If the route is ever retired instead, flip this to NotFound
   and say so here").
3. `CorsAndAuthTests.Obo_Endpoints_RequireBearer` re-pointed. It is `Skip`'d, so it would NOT have gone
   red — it would have silently become a dead test probing a nonexistent route.
4. Client tests re-pointed at `uploadFileForRecord`; `SdapApiClient.test.ts`'s
   `expect(client.uploadFile).toBeDefined()` INVERTED into an assertion that it is gone.
5. Six stale comment sites corrected (`SpeUploadPath.cs`, `EndpointMappingExtensions.cs`,
   `SpeFlatUploadPathTests.cs`, sdap-client `types/index.ts` and `IndexFileOperation.ts`,
   `CreateAnalysisWizardWidget.tsx`) — each named the deleted route as the live upload surface.

### 8.5 — Ship-together: CLIENT + BFF, same deploy

Both halves are in THIS repo and MUST deploy together. No compatibility window, no feature flag:

- **BFF first** -> a client still on `PUT /api/obo/containers/{id}/files/{*path}` -> **404 on every upload**.
- **Client first** -> a client calling the record-keyed / `/me` routes against a BFF that lacks them ->
  **404 on every upload**.

Surfaces in scope: the BFF, the `sprk_documentuploadwizard` code page, `SemanticSearchControl` (PCF),
`LegalWorkspace`, and the `sprk_wizard_commands.js` web resource — plus the section-8.3 bundle obligation.

### 8.6 — Verification

All re-run AFTER the husky hook reformatted the staged files.

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | Build succeeded, 0 warnings, 0 errors |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` | Build succeeded, 0 errors |
| `dotnet test tests/Spaarke.ArchTests/` | **182 / 182 passed** |
| `dotnet test --filter ~RouteRetirementTests` | **13 / 13 passed** — incl. the NEW assertion that `PUT /api/obo/containers/{id}/files/{*path}` now 404s, and both re-pointed positive controls on `/api/obo/me/files` |
| `Spaarke.UI.Components` jest (full) | **8 failed suites / 13 failed tests** — the stated pre-existing baseline, NOT exceeded |
| `Spaarke.UI.Components` document-upload jest | 3 suites / 32 tests passed |
| `Spaarke.SdapClient` jest | 2 suites / 27 tests passed |
| `Spaarke.UI.Components` / `SemanticSearchControl` `npx tsc --noEmit` | clean |
| `LegalWorkspace` `npx tsc --noEmit` (`WorkspaceGrid.tsx`) | 3 diagnostics, all pre-existing unused-variable noise at lines 19/190/192 — unchanged; the edits here are at ~524-565 |
| DocumentUploadWizard `npx vite build` | built |

**`dotnet build Spaarke.sln` was never observed green in this session — but NOT because of a
compile error in this work.** Two distinct causes, in sequence, both outside this task:

1. Earlier: the concurrent task-052 agent's uncommitted in-flight edits (an `ILogger` ctor param on
   `CoreAncestorResolver` / `ActionSeam` / `CreateTaskNodeExecutor` / `TodoRegardingBuilder`, then an
   unused `_coreAncestors` field in `EmailDraftToolHandler.cs`) produced 11 test-project + 2 BFF
   errors. **Those are now resolved** — that agent finished the change.
2. Since then: **only `MSB3021` / `MSB3027` file-copy locks**, every one naming
   `testhost (68788)` — that agent's long-running `dotnet test`. These occur at the copy-to-output
   step, AFTER compilation, so they are not compile failures.

Both projects the locks obscured were therefore built individually and are clean (rows 1-2 above),
and every test project touched here was executed. Zero diagnostics were reported in any file this
task modified, at any point.
