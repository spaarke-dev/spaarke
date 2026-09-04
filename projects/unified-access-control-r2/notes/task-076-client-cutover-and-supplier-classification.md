# Task 076 — client cutover + the container-supplier classification

> **Scope of THIS document**: the CLIENT half of 076 (POML steps 4–7). The server half shipped
> earlier. **POML steps 2/3/8 — deleting the legacy container-keyed route, its Pending waiver, and
> `uploadFile()` — are deliberately NOT done here.** See §6.
>
> **Date**: 2026-09-03.

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

## 5 — Remaining work, in order

1. **DocumentUploadWizard cutover** — the last upload client on the legacy route, and the one that
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
2. **Delete the vestigial `speContainerId` prop chains** (§3.D row 4) once (1) lands.
3. **THEN, and only then, POML steps 2/3/8** — delete the legacy route, its Pending waiver, and
   `SdapApiClient.uploadFile()` / `UploadOperation.uploadSmall()`. **Reserved for human review.**
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
