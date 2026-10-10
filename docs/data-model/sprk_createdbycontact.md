# `sprk_createdbycontact` — contact creator stamp on Document, To Do, Event and Communication

> **Last Reviewed**: 2026-10-10
> **Reviewed By**: spaarke-SPA-external-access-platform-r3 task 020 (FR-25)
> **Status**: In solution source (`SpaarkeMaster`); the dev import is owner-gated and not yet run
> **Solution**: `SpaarkeMaster` (`src/dataverse/solutions/SpaarkeMaster`)

## Purpose

`sprk_createdbycontact` records the **contact** who created a record from the external SPA. It is the contact-side twin of `sprk_createdbyperson` (a lookup to `systemuser`). A record the BFF creates app-only has `createdby` = the BFF application user, so neither the platform `createdby` nor `sprk_createdbyperson` can name a contact author. The delete-own check (FR-26) reads the stamp for the caller's kind:

| Caller | Column the delete check reads |
|---|---|
| systemuser | `sprk_createdbyperson` |
| contact | `sprk_createdbycontact` |

A delete is allowed only when the stamp equals the caller **and** the caller still holds Read on the parent.

## Tables and definition

| Table | Column | Type | Target | Required | Delete behaviour |
|---|---|---|---|---|---|
| `sprk_document` | `sprk_createdbycontact` | Lookup | `contact` | No | RemoveLink |
| `sprk_todo` | `sprk_createdbycontact` | Lookup | `contact` | No | RemoveLink |
| `sprk_event` | `sprk_createdbycontact` | Lookup | `contact` | No | RemoveLink |
| `sprk_communication` | `sprk_createdbycontact` | Lookup | `contact` | No | RemoveLink |

- Schema name `sprk_CreatedByContact`; display name "Created By (Contact)".
- Relationships (one per table), defined on the contact side in `Other/Relationships/Contact.xml`: `sprk_contact_sprk_document_createdbycontact`, `sprk_contact_sprk_todo_createdbycontact`, `sprk_contact_sprk_event_createdbycontact`, `sprk_contact_sprk_communication_createdbycontact`. Cascade: `CascadeDelete` and `CascadeArchive` = `RemoveLink`, every other cascade `NoCascade`. Deleting a contact clears the stamp and never deletes a document, to do, event or message.
- Not audited (`IsAuditEnabled` 0), like `sprk_createdbyperson`.
- Service requests do **not** get this column: they stamp the creator in the existing `sprk_requestedby` (a lookup to `contact`).

## Field security

The column is **secured** (`IsSecured` 1) and sits in the same two field security profiles as `sprk_createdbyperson` on the same table (`Other/FieldSecurityProfiles.xml`):

| Profile | Members | Read | Update | Create |
|---|---|---|---|---|
| `Spaarke BFF-Managed Field Readers` | Every business unit default team (every user keeps reading it; a secured column is hidden from anyone without Read) | Yes | No | No |
| `Spaarke BFF-Managed Field Writers` | The BFF application user(s) only | Yes | Yes | Yes |

The precedent for a secured contact lookup written by the BFF is `sprk_externalrecordaccess.sprk_grantedbycontact`.

## Writer

**The BFF only**, from the resolved contact principal and never from client input (FR-11, FR-25), at create time in the shared stamp path (task 030). No plugin (ADR-002; write-path invariants in [DATAVERSE-WRITE-PATH-ARCHITECTURE.md](../architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md)). Users and the Dataverse client cannot write it. The BFF application user needs `Spaarke BFF-Managed Field Writers` membership for the app-only create to carry the value.

## Readers

- Delete-own check (task 032): compares the stamp with the caller's contact id.
- Sender stamp on messages (task 034): `sprk_communication.sprk_createdbycontact` names the contact author of a thread post.

## Rollback

Delete the four `sprk_createdbycontact` columns (Web API `DELETE EntityDefinitions(LogicalName='<table>')/Attributes(LogicalName='sprk_createdbycontact')`); Dataverse removes the four relationships and the field-permission rows with them. No data outside the column is touched. Nothing else reads the column until tasks 030, 032 and 034 ship.
