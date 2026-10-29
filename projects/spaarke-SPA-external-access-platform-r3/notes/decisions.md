# Decisions — Spaarke External Access Platform R3

Full owner clarifications live in `spec.md` §Owner Clarifications; the auth path and its rationale are in `notes/r3-auth-path.md`. This file records decisions made during planning and execution, with rationale. One entry per decision; superseded entries are marked, not deleted.

## 2026-10-10 — Planning decisions (project-pipeline)

- **Feed "last seen" marker in Redis (ADR-009), not a Dataverse column.** Spec lists ADR-009 for the feed/last-seen and the owner chose "no new table". Losing the marker only re-marks the last 30 days as unread. Revisit if owner wants it durable across cache loss.
- **Service-request documents link through the existing `sprk_document.sprk_relatedservicerequest` lookup** (live describe, dev, 2026-10-10: relationship `sprk_document_RelatedServiceRequest_sprk_servicerequest`). No new lookup is needed.
- **Intake schema folded into task 020.** `sprk_servicerequest` has no request-type or intake-answers column (R2 task 030 was never built). Task 020 adds `sprk_requesttype` (choice: NDA Assessment, Invention Submission, Policy Question, Trademark Search) and `sprk_intakedata` (multiline JSON), with owner sign-off.
- **Spec wording "WizardModal / WizardRegistry"** maps to the current `Wizard/WizardShell` + `InAppWizardHost` (the old names were deleted 2026-10-03).
- **Creator stamp for service requests is `sprk_requestedby`** (already present). `sprk_createdbycontact` is added to document, to-do, event and communication only.
- **One ADR-028 amendment task (001) for R3's three rule changes**, coordinated with `spaarke-auth-system-of-record-r1`, which owns the §12b items. Owner approval is needed to edit `.claude/adr/`.
- **C3 email builds on UAC-r2 PR #1583** (`GrantAccessNotifier`) instead of a parallel notifier.

## 2026-10-10 — Task 033 (external message read, FR-04)

- **Internal-only messages reach only an internal systemuser on the external plane.** CIAM contacts AND workforce contacts never receive `sprk_isinternalonly = true` (the conservative reading of spec FR-04 "callers flagged external"; POML 033 note). A systemuser that is external-licensed (`sprk_isexternal = true`) is also refused them — the same `IsExternalAsync` bit the internal reads use (#675). Enforced by the shared `CommunicationAccessFilter`; details in `task-033-notes.md`.
- **Documents have no message thread.** `sprk_communication` has no regarding lookup to `sprk_document` (`RegardingFieldMap`, `SecureChildLineage`), so `documents` is an unsupported root (404) on `/api/v1/external/{root}/{id}/messages`, although the POML listed it. Task 034 and 051 inherit this.
- **External message read returns only sent / received messages (review fix F1, 2026-10-10).** The app-only read of `sprk_communication` is an ALLOW-list on Status Reason: **Send (659490002)** and **Delivered (659490003)** only. Values verified in `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_communication/Entity.xml` (statuscode optionset `sprk_communication_statuscode`, lines ~3519-3600), `docs/data-model/sprk_communication.md:123` and `Services/Communication/Models/CommunicationStatus.cs`. Draft (1, statecode 0 — `EmailDraftToolHandler` writes full subject/body Draft rows with regarding stamps), Deleted (2) and Recalled (659490006) are excluded; so are Queued (659490001, not yet sent), Failed (659490004) and Bounded (659490005, never delivered) and any row whose status is missing or unreadable. Inbound mail is covered because every ingest path (`IncomingCommunicationProcessor`, `MessagingIngestor`, `EmailUploadCaptureService`) stamps Delivered. An allow-list, not a deny-list: a status added later stays hidden from the external plane until it is admitted in `CommunicationThreadReadService`. The filter is in the OData query AND re-checked per returned row. Internal-plane reads are unchanged. Reversible: add the status to the allow-list.

## 2026-10-11 — Task 036 scope (owner)

- **No email on message post (option A).** The 036 POML asked for an email to member contacts on each message post, which contradicted the 2026-10-08 decision (spec :42, :64, :391, :402). The implementation (`4a2a767a2`) was reverted for that path in `38e565453`. The owner confirmed option A on 2026-10-11. Members learn of new messages through the in-portal feed (task 037). Task 036 delivers the grant email (FR-07) only.

## 2026-10-11 — Owner answers to the open questions

- **020 (schema):** approved in principle for dev. `sprk_intakedata` follows the `sprk_createdbyperson` settings, which means it is field-secured with the same two profiles ("BFF-Managed Field Readers" read-only, "BFF-Managed Field Writers" read/create/update). The source XML is changed to match. The live import still runs only after the owner sees the exact commands and the rollback and says go.
- **032 (delete-own):** option (b), widened. A contact caller matches `sprk_createdbycontact`. A systemuser caller matches `sprk_createdbyperson` OR `sprk_createdbycontact` = its server-resolved linked contact.
- **003 (FR-12 self-registration):** Option B, a custom join page plus a shared registration service. The spike's sub-questions Q2–Q5 are still open.
- **060 (registration service):** built in R3, not handed off to R4. The host is still to be chosen under ADR-052 (spike Q5).
- **031 (containers):** one SPE container per customer business unit. The only exception is secure records (secure project, matter or work assignment), which have their own container. There are no containers per record type, so none for invoices or service requests. This matches `RecordContainerResolver`, where children of a secure root use that root's container and everything else uses the owning business unit's container. R3 creates no containers. Still open: the invoice access rule (A/B/C), and whether a service request that regards a secure matter puts its documents in that matter's container.
- **042:** option (a). Hide the playbook page and SemanticSearch on the external plane.


## 2026-10-12 — Task 037 (in-portal notification feed, FR-08)

- **Systemuser callers: grants are not represented for team-membership access (POML 037 escalation trigger, recorded and continued).** A licensed staff member's accessible set comes mostly from Dataverse team membership, which writes no `sprk_externalrecordaccess` row. The feed's grant source is only the rows naming the caller's contact as grantee, so a systemuser sees a grant item only for a grant made to its linked contact (task 141 link), and otherwise sees message items only. No other grant source was invented. Organization grants (a row with no contact) are not announced either; the org members still gain access, but the feed has no per-contact row to show.
- **The last-seen marker is Redis through `ITenantCache`** (as planned 2026-10-10): resource `external-notifications-seen`, id `systemuser:{id}` or `contact:{id}` (the principal KIND decides, as the creator stamp does), the token's `tid` as the tenant, version 1, 30-day TTL refreshed on every `POST /seen`. A missing or unreadable marker means every item is unread; a failed write is a retryable 503.
- **Privileged messages never reach the feed** (owner 2026-10-11): the feed's message read is `CommunicationThreadReadService.ReadRecentForRecordsAppOnlyAsync`, which carries `ExternalPrivilegeFilterClause` in the query and re-checks `IsHiddenFromExternal` per row through the same helper as the task 033 read. No second filter.

## 2026-10-21 — ebilling firm invoice-file route (owner, in the R3 session)

- **A pending malware scan:** owner: "yes show 'checking file' message with no download". The file is listed with its pending status; the SPA shows "Checking file…" and no Download; its content answers 409 `file_scan_pending`. A file found malicious leaves the list and answers 404.
- **No transition period:** owner: "we do not need a transitional solution--this is all in dev; we can remove the components and functionality that are not needed". Invoice PDFs move to ebilling Stored Files with no SPE period alongside them.
- **Route rule** (agreed with uac-r2 and ebilling; ebilling's file model is still awaiting the owner's go, ebilling D-148):
  - `GET /api/v1/external/invoices/{id}/files` and `.../files/{fileId}/content`, with no upload route and no versions.
  - Systemusers are served the 036 way. CIAM contacts go through the ReadOne firm path. Both then pass one file gate: Firm-visible, Servable, clean, parent match.
  - Uniform 404 on any denial; 503 on a read fault. uac-r2's task 188 guard names the gate method.
- **Retirement proposed by ebilling** (final with D-148): the four invoice-document routes, `ExternalProjectDataEndpoints.cs:378-412`. The invoice-submission routes stay; only the storage target changes. The SPA work is R4: drop the invoice-document upload and versions, and list files from the new route.
