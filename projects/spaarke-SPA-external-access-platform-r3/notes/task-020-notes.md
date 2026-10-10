# Task 020 — R3 schema: sprk_createdbycontact + intake columns

**Status (2026-10-10):** the repo source is done (sonnet/high implementer). The **live dev import is owner-gated** and has not run.

## What changed in source
- **`sprk_CreatedByContact`** (lookup → contact, field-secured), on:
  - `sprk_document`, `sprk_todo`, `sprk_event`, `sprk_communication`.
  - It sits just before `sprk_CreatedByPerson`.
- **Not on `sprk_servicerequest`.** Service requests use the existing `sprk_requestedby` (`notes/decisions.md`; `spec.md` §New Components corrected 2026-10-10).
- **`sprk_servicerequest`** gains:
  - `sprk_RequestType`: a local choice. Values:

    | Value | Label |
    |---|---|
    | 100000000 | NDA Assessment |
    | 100000001 | Invention Submission |
    | 100000002 | Policy Question |
    | 100000003 | Trademark Search |

  - `sprk_IntakeData`: memo, max 1,048,576, format text, **not** field-secured (matches `sprk_recordsummary`; the BFF writes it app-only).
- **Relationships:** `sprk_contact_<table>_createdbycontact` (`Other/Relationships.xml`, `Other/Relationships/Contact.xml`).
  - Delete and archive cascade: RemoveLink.
  - Every other cascade: NoCascade.
- **Field security** (`Other/FieldSecurityProfiles.xml`):
  - "BFF-Managed Field Readers": read-only.
  - "BFF-Managed Field Writers": read, create and update. This is the same split as `sprk_createdbyperson`.
- **Docs:** `docs/data-model/sprk_createdbycontact.md` (new) and `docs/data-model/sprk_servicerequest.md`.
- **XML checks:** all 8 XML files parse; BOM and CRLF are preserved; the diff is additive.

## Pre-check (read-only, dev)
Both BFF application users are enabled members of the Writers profile:
- `# mi-bff-api-dev` (5967251e…);
- `SDAP-BFF-SPE-API` (1e40baad…).

## Owner-gated live steps (not run)
1. **Create the columns** with a contact twin of `scripts/Set-ChildRecordCreatorPersonSchema.ps1`, the repo convention: create in dev by script, then re-export.
   - The twin is `scripts/Set-ChildRecordCreatorContactSchema.ps1`, still to be written.
   - Run it in order: dry run, `-Apply`, `-Verify`.
   - `-EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c`
   - The two service-request columns go through the `dataverse-create-schema` Web API pattern.
2. **Re-export** with `pwsh scripts/solution-authoring/Export-SpaarkeMasterSource.ps1`: run `-WhatIf` first. Diff the export against the hand-written XML and keep the export.
3. **Fallback:** `pac solution pack` + `pac solution import` of the whole solution. Use it only after step 2 shows no drift.
4. **Verify:**
   - a read-only describe of the six columns;
   - an owner-approved create and delete of a test row as the BFF identity, with `sprk_createdbycontact` set.

**Rollback:** delete the six attributes with `DELETE …/EntityDefinitions(LogicalName='<table>')/Attributes(LogicalName='<col>')`. Dataverse drops the relationships, the local option set and the field-permission rows with them. Nothing reads these columns until tasks 030, 032 and 034 ship.

## Found while doing the task (not fixed here)
- `scripts/Deploy-ExternalRecordAccessContactGrantor.ps1:314-315` (`Ensure-Profile`) has older profile descriptions. Re-running it would overwrite the updated text. It is cosmetic.
- `FieldSecurityProfiles.xml` lists both `sprk_grantedbycontact` and `sprk_grantedbycontactid` for `sprk_externalrecordaccess`. This is name drift and outside this task's scope.

## Open
- Owner approval of the columns, the field-security posture and the rollback (acceptance criterion 6).
- Live criteria 1–3 wait for the import.
