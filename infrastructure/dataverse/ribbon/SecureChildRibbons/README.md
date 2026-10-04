# SecureChildRibbons — "New &lt;thing&gt;" under a secure record (subgrids of child tables)

> **Owner**: unified-access-control-r2 task 147 r1 (GitHub #1069) — owner round 28 item 2 ("E2").
> **Live state**: NOT deployed (manual gate G147-6). Nothing here was applied to any environment.

## What it does

The platform subgrid **"+ New"** (`Mscrm.AddNewRecordFromSubGridStandard`, the parent-prefilled create) creates the child
**as the user, owned by the user** — under a secure project / matter / work assignment that makes it readable by the
user's whole business unit until the reconcile job re-owns it. On a subgrid whose host is secure this change:

- **hides** the platform "+ New" (one extra enable rule, `nativeNewAllowed`), and
- **shows** "New &lt;thing&gt;" (`newChildAvailable`, the exact complement), which opens the product's BFF-backed create
  surface filed under the host: the To Do / Event / Invoice / Report Card wizards, the Document Upload wizard, the
  Communication page in compose mode, or — for a budget, which has no wizard — `POST /api/v1/child-records/sprk_budget`
  followed by opening the new budget.

**Fail closed**: the platform "+ New" is allowed only when the host is a project / matter / work assignment whose
`sprk_issecure` was read (form attribute, else `Xrm.WebApi`) and is `false`. An unreadable or empty flag, or a host that is
not one of the three roots, hides it and shows the BFF command. The Create privilege is **not** removed (owner round 28);
a create from outside the product is re-owned by the reconcile job's recent-changes pass every 2 minutes.

## Files

| File | Role |
|---|---|
| `secure-child-new.template.xml` | The ONE authored definition (per child entity: `{{entity}}`, `{{label}}`). Carries the platform's own `Mscrm.AddNewRecordFromSubGridStandard` (copied from `RetrieveEntityRibbon`, spaarkedev1, 2026-10-04) plus the one rule. |
| `Merge-SecureChildRibbon.ps1` | Pure file transformation into a FRESH export of the child's `RibbonDiff.xml`; idempotent; extends an existing override instead of replacing it; throws if a command is lost. |
| `src/client/webresources/js/sprk_secure_child_ribbon.js` | The command script (`sprk_/scripts/secure_child_ribbon.js`, `Spaarke.SecureChild.Ribbon`). |
| `scripts/Deploy-SecureChildNewCommands.ps1` | Dry run (reads only; detects platform drift of the copied command, quick-create turned on, missing helper libraries) / `-Apply` / `-Verify`. |

Served tables: `sprk_todo`, `sprk_event`, `sprk_invoice`, `sprk_reportcard`, `sprk_document`, `sprk_communication`,
`sprk_budget`. `sprk_analysis` is already covered (AnalysisRibbons hides its platform "+ New" outright; "New Analysis"
opens the BFF-backed wizard). `SecureChildNewCommandAgreementTests` (ArchTests) keeps the script, the merge script and the
deploy script on one table list, every root-form secure-child subgrid covered, and the budget create on the BFF route.

## Deployment (MANUAL GATE G147-6 — main session)

Order: the BFF carrying task 147 r1 → `scripts/Set-ChildRecordCreatorPersonSchema.ps1 -Apply` (G147-5: `sprk_budget` gains
`sprk_createdbyperson`) → task 142's helper web resources (`sprk_/scripts/bff_auth.js`,
`sprk_/scripts/assignedaccess_postsave.js`) → this:

```powershell
& ./scripts/Deploy-SecureChildNewCommands.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com            # dry run
# export a dedicated ribbon solution (SpaarkeSecureChildRibbons; the seven tables, ribbon only) per the ribbon-edit skill, unpack it
& ./scripts/Deploy-SecureChildNewCommands.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -ExportDir <unpacked> -Apply
& ./scripts/Deploy-SecureChildNewCommands.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
```
