# SecureChildRibbons — "New &lt;thing&gt;" under a secure record (subgrids of child tables)

> **Owner**: unified-access-control-r2 task 147 r1 / r1c (GitHub #1069) — owner round 28 item 2 ("E2").
> **Live state**: NOT deployed (manual gate G147-6). Nothing here was applied to any environment.

## What it does

The platform subgrid **"+ New"** (`Mscrm.AddNewRecordFromSubGridStandard`, the parent-prefilled create) creates the child
**as the user, owned by the user** — under a secure project / matter / work assignment (directly, or through one of its
children) that makes it readable by the user's whole business unit until the reconcile job re-owns it. On a subgrid of a
child table this change:

- **hides** the platform "+ New" (one extra enable rule, `nativeNewAllowed`), and
- **shows** "New &lt;thing&gt;" (`newChildAvailable`, the exact complement), which opens the product's BFF-backed create
  surface filed under the host: the To Do / Event / Invoice / Report Card wizards (`entityType`, `entityId`, `recordName`),
  the Document Upload wizard, the Communication page in compose mode, or — for a budget, a KPI assessment or a billing
  event, which have no wizard — `POST /api/v1/child-records/{table}` followed by opening the new row.

**Fail closed**: the platform "+ New" is allowed only when the host is

- a project / matter / work assignment whose `sprk_issecure` was read **through `Xrm.WebApi`** (round 28; never the form's
  in-memory copy) and is `false`, or
- a **party** (contact, organization, account) — never an ownership parent, so nothing under it is under a secure record.

An unreadable or empty flag, a secure root, and **any other host** (an event, a document, an invoice, an analysis, a budget
— a child of a secure record's child: its secure ancestor is not known client-side) hide it and show the BFF command; the
BFF decides the owner from what the row is filed under. The Create privilege is **not** removed (owner round 28); a create
from outside the product is re-owned by the reconcile job's recent-changes pass every 2 minutes.

Spaarke's own parent-prefilled create of a served table — **"+ Add KPI"** (`add-kpi-ribbon.xml`, the KPI assessment quick
create) — carries the same rule (authored in that RibbonDiff, and added by the merge script when the export carries the
command), and its script (`sprk_kpi_ribbon_actions.js`) refuses the quick create itself unless the record reads as not
secure.

## Files

| File | Role |
|---|---|
| `secure-child-new.template.xml` | The ONE authored definition (per child entity: `{{entity}}`, `{{label}}`). Carries the platform's own `Mscrm.AddNewRecordFromSubGridStandard` (copied from `RetrieveEntityRibbon`, spaarkedev1, 2026-10-04) plus the one rule. |
| `Merge-SecureChildRibbon.ps1` | Pure file transformation into a FRESH export of the child's `RibbonDiff.xml`; idempotent; extends an existing override instead of replacing it; guards Spaarke's own parent-prefilled creates of the table ("+ Add KPI"); throws if a command is lost. |
| `src/client/webresources/js/sprk_secure_child_ribbon.js` | The command script (`sprk_/scripts/secure_child_ribbon.js`, `Spaarke.SecureChild.Ribbon`). Behaviour pinned by `Spaarke.UI.Components/src/utils/adapters/__tests__/secureChildRibbonScript.test.ts` (the script run in a sandbox). |
| `scripts/Deploy-SecureChildNewCommands.ps1` | Dry run (reads only: a live inventory of every ownership-child subgrid on every active main form — FAIL on one not served; platform drift of the copied command; quick-create turned on; missing helper libraries) / `-Apply` / `-Verify`. |

Served tables: `sprk_todo`, `sprk_event`, `sprk_invoice`, `sprk_reportcard`, `sprk_document`, `sprk_communication`,
`sprk_budget`, `sprk_kpiassessment`, `sprk_billingevent`. `sprk_analysis` is already covered (AnalysisRibbons hides its
platform "+ New" outright; "New Analysis" opens the BFF-backed wizard). `SecureChildNewCommandAgreementTests` (ArchTests)
keeps the script, the merge script and the deploy script on one table list, every ownership-child subgrid of the live
inventory covered, the party hosts exactly the identity tables, the create-then-open tables on the BFF route and stamped
with the creator person, and the "+ Add KPI" guard in place.

## Deployment (MANUAL GATE G147-6 — main session)

Order: the BFF carrying task 147 → `scripts/Set-ChildRecordCreatorPersonSchema.ps1 -Apply` (G147-5: `sprk_memo`,
`sprk_reportcard`, `sprk_budget`, `sprk_kpiassessment`, `sprk_billingevent` gain `sprk_createdbyperson`) → task 142's helper
web resources (`sprk_/scripts/bff_auth.js`, `sprk_/scripts/assignedaccess_postsave.js`) → this:

```powershell
& ./scripts/Deploy-SecureChildNewCommands.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com            # dry run
# export a dedicated ribbon solution (SpaarkeSecureChildRibbons; the nine tables, ribbon only) per the ribbon-edit skill, unpack it
& ./scripts/Deploy-SecureChildNewCommands.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -ExportDir <unpacked> -Apply
& ./scripts/Deploy-SecureChildNewCommands.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
```
