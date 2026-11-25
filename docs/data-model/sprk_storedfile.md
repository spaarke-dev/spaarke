# sprk_storedfile (Stored File)

> **Project**: ebilling-r1, EB-D-148 (tasks 086, 088, 090; docs task 189)
> **Last Updated**: 2026-11-25
> **Solution**: `SpaarkeMaster` (unmanaged). Source: `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_StoredFile/Entity.xml` and the `sprk_storedfile_*` files under `Other/Relationships/`.
> **Scripts**: `scripts/ebilling/Deploy-EbillingProcedureSchema.ps1` (table, columns, relationships; task 080), `scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1` (source and creator columns; task 088).
> **Architecture**: [EBILLING-FILE-STORE.md](../architecture/EBILLING-FILE-STORE.md). Rationale: [procedure-and-file-model.md](../../projects/ebilling-r1/notes/procedure-and-file-model.md) sections 4, 9, 10.

## Purpose

One table holds every file the e-billing feature stores: a Procedure Version's source file, an invoice PDF, an invoice supporting file and an export file. A row is the file's metadata and its pointer into Azure Blob Storage; the bytes live in the blob, never in Dataverse. The row is created, flipped to Servable, read and removed only by the BFF (`src/server/api/Sprk.Bff.Api/Services/Finance/Files/StoredFileService.cs`); no client, form or flow touches it (ADR-002: no plugin, no business rule).

Exactly one of the three parent lookups (`sprk_procedureversion`, `sprk_invoice`, `sprk_billingexport`) is set on a row. The BFF enforces this; the schema does not.

User-owned, entity set `sprk_storedfiles`, primary name `sprk_name`, auditing on, duplicate detection flag on. No alternate key: file ids are deterministic GUIDs the BFF computes (`StoredFileIds` in `Services/Finance/Files/StoredFileContracts.cs`), so a retried create lands on the same row.

## Fields

| Display name | Logical name | Type | Required | Description |
|---|---|---|---|---|
| File Name | `sprk_name` | Text (400), primary name | Application | The file name. Not field-secured. |
| Purpose | `sprk_purpose` | Choice, global `sprk_storedfilepurpose` | No | What the file is for. Decides the blob container and who may be served it (see below). |
| File Status | `sprk_filestatus` | Choice (local) | No | Not yet servable = 100000000 (staged, or committed but not yet re-checked), Servable = 100000001, Removed = 100000002. Every reader refuses a row that is not Servable. |
| Firm-visible | `sprk_firmvisible` | Two options, default No | No | Yes only for an invoice PDF the firm uploaded through the external SPA. A Submit Invoice copy, a supporting file, a Procedure source file and an export file are always No. |
| Procedure Version | `sprk_procedureversion` | Lookup -> `sprk_procedureversion` | No | Parent, for a Procedure source file. |
| Invoice | `sprk_invoice` | Lookup -> `sprk_invoice` | No | Parent, for an invoice PDF or supporting file. |
| Billing Export | `sprk_billingexport` | Lookup -> `sprk_billingexport` | No | Parent, for an export file. |
| Source Document | `sprk_sourcedocument` | Lookup -> `sprk_document` | No | For a Submit Invoice copy: the document the bytes were copied from (the .eml for an Outlook add-in submission). The copy rule reads the caller's own Read on it on every access. |
| Source Communication | `sprk_sourcecommunication` | Lookup -> `sprk_communication` | No | For a Submit Invoice copy: the email the file came from. |
| Source Attachment Identity | `sprk_sourceattachmentidentity` | Text (1,000) | No | The marked attachment's identity inside the source (the .eml's MIME part: ordinal and name). Never an Exchange item id. |
| Source Version | `sprk_sourceversion` | Text (20) | No | The source document's version number at staging, as one canonical string (invariant culture, no padding). The commit pins it with `IfRowVersionMatches`. |
| Source SHA-256 | `sprk_sourcesha256` | Text (64) | No | SHA-256 of the source bytes at staging (hex); checked again when the row flips to Servable. |
| SHA-256 | `sprk_sha256` | Text (64) | No | SHA-256 of the stored blob (hex). |
| Size (bytes) | `sprk_sizebytes` | Whole number, 0 .. 2,147,483,647 | No | A larger file cannot be stored; the BFF refuses it. |
| Content Type | `sprk_contenttype` | Text (255) | No | MIME content type. |
| Blob Path | `sprk_blobpath` | Text (1,024) | No | `{organization id}/{parent table}/{parent id}/{file id}` inside the purpose's container. The download checks that the parent id in the path equals the row's parent. Server-only. |
| Created By (Person) | `sprk_createdbyperson` | Lookup -> `systemuser` | No | The person who asked for the file; stamped by the BFF (the row's `createdby` is the BFF application user on an app-only create). |

`statecode` / `statuscode` keep the platform defaults (Active 1 / Inactive 2) and carry no file meaning; the lifecycle is `sprk_filestatus`.

### Global choice `sprk_storedfilepurpose`

| Value | Label | Parent set | Container |
|---|---|---|---|
| 100000000 | Procedure source | `sprk_procedureversion` | `procedures` |
| 100000001 | Invoice PDF | `sprk_invoice` | `invoice-files` |
| 100000002 | Invoice supporting file | `sprk_invoice` | `invoice-files` |
| 100000003 | Export file | `sprk_billingexport` | `billing-exports` |

The container is derived from the purpose in code (`StoredFileSchema.ContainerOf`), not stored on the row.

## Field security

Every column except `sprk_name` is field-secured: read for every user, create and update for the BFF application user only (the "Spaarke BFF-Managed Field Readers" and "Writers" profiles; the creator column follows `Set-ChildRecordCreatorPersonSchema.ps1`). `sprk_blobpath` has no reader row: only the BFF reads it.

## Relationships

| Related table | Type | Lookup field | Delete behaviour | Notes |
|---|---|---|---|---|
| `sprk_procedureversion` | N:1 | `sprk_procedureversion` (`sprk_storedfile_procedureversion_procedureversion`) | **RemoveLink** | A version deleted outside the BFF leaves its file row for the orphan reconcile, which reads the parent from the blob path. |
| `sprk_invoice` | N:1 | `sprk_invoice` (`sprk_storedfile_invoice_invoice`) | **RemoveLink** | As above, for an invoice. |
| `sprk_billingexport` | N:1 | `sprk_billingexport` (`sprk_storedfile_billingexport_billingexport`) | **RemoveLink** | As above, for an export. |
| `sprk_document` | N:1 | `sprk_sourcedocument` (`sprk_storedfile_sourcedocument_document`) | **Restrict** | A document a copy was made from cannot be deleted. |
| `sprk_communication` | N:1 | `sprk_sourcecommunication` (`sprk_storedfile_sourcecommunication_communication`) | **Restrict** | A communication delete only deactivates it, so the copy rule still reads it. |
| `systemuser` | N:1 | `sprk_createdbyperson` (`sprk_systemuser_sprk_storedfile_createdbyperson`) | RemoveLink | |

All cascades other than delete are NoCascade. The Stored File has no parental (Cascade) relationship, so it holds no parent's security: it never inherits access from a Procedure Version, an invoice or an export through Dataverse.

## Access classification

- **Invoice PDF and Invoice supporting file** are secure children of their invoice: one lineage column, `sprk_invoice` (`Services/Access/SecureChildLineage.cs`). The Source Document and Source Communication lookups are not lineage; they are read as extra parents in the row's own access question.
- **Procedure source and Export file** are not secure children of anything: a row with no `sprk_invoice` is never reached by a lineage walk.
- **No Stored File row is ever externally readable**, whatever its purpose or Firm-visible value: the external-visibility registry marks the whole table hard-deny (`Services/Access/SecureChildLineage.cs`, entry `sprk_storedfile`). A firm reads its own invoice's file only through the dedicated firm file route and its gate ([EBILLING-FILE-STORE.md](../architecture/EBILLING-FILE-STORE.md)), which lists only Firm-visible Yes files.

## Other tables that point here

| Table | Column | Page |
|---|---|---|
| `sprk_procedureversion` | none: the version's file is the Stored File row that names it | [sprk_procedure.md](sprk_procedure.md) |
| `sprk_invoice` | none: its files are the rows that name it | [sprk_invoice.md](sprk_invoice.md) |
| `sprk_billingexport` | none: its files are the rows that name it | [sprk_billingexport.md](sprk_billingexport.md) |

## BFF owners

| Area | Code |
|---|---|
| Rows, blobs, upload, download, replace, withdraw | `src/server/api/Sprk.Bff.Api/Services/Finance/Files/StoredFileService.cs` |
| Blob access and scan tags | `Services/Finance/Files/StoredFileBlobs.cs` |
| Orphan and stuck-row reconcile | `Services/Finance/Files/StoredFileReconcileJob.cs` |
| Internal routes | `Api/Finance/StoredFileEndpoints.cs` |
| Firm routes | `Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs` |
