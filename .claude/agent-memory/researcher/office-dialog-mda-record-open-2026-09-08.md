---
name: office-dialog-mda-record-open-2026-09-08
description: Can the Word add-in host/edit a Dataverse MDA record in an Office Dialog API window? Verdict no evidence it works; REC read-only in-pane + openBrowserWindow escape.
metadata:
  type: project
---

# Office Dialog API for Dataverse/MDA record open (2026-09-08)

**Question**: Can the Word add-in use the Office Dialog API to host and edit a Dataverse model-driven-app record,
authenticate it, and propagate edits back to the pane?

**Findings**: `displayDialogAsync` requires the initial HTTPS URL to be the exact add-in domain; later navigation may go
cross-domain. `messageParent`/`messageChild` can target a different origin only with Dialog Origin 1.1, and the dialog
page must participate in Office messaging — a redirected MDA `main.aspx` is not an evidenced messaging participant, and
the repo's modal standard rejects iframe-hosted OOB `main.aspx`. The add-in has NAA/OBO auth via `@spaarke/auth` and no
`Xrm` host context; nothing shows an MDA form in an Office dialog inherits the pane token or keeps lookups/subgrids
usable at dialog size. REC option 3: read-only detail in-pane + `Office.context.ui.openBrowserWindow` for full editing.

**Sources**:
- https://learn.microsoft.com/en-us/office/dev/add-ins/develop/dialog-api-in-office-add-ins (updated 2026-06-24)
- https://learn.microsoft.com/en-us/javascript/api/office/office.ui?view=word-js-preview (updated 2026-08-31)
- https://learn.microsoft.com/en-us/office/dev/add-ins/develop/dialog-best-practices (updated 2026-04-01)
- projects/spaarkeai-word-add-in-r1/spec.md ; docs/standards/MODAL-DECISION-CRITERIA.md ; src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts

**Open questions**: Live Word desktop probe for MDA redirect, authenticated form load, save, lookup picker, subgrid
usability, and whether close-time re-fetch suffices for pane refresh.
