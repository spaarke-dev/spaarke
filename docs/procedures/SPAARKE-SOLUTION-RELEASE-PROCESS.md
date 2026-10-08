# Spaarke Solution Package — Release, IAM and Upgrade Runbook

> **Purpose**: how Spaarke's Dataverse content is defined, released, imported into a customer environment and upgraded.
> One document for the whole lifecycle; other docs link here instead of restating it.
>
> **Binding rule**: [ADR-027 §3–§4](../../.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md)
> (amended 2026-10-07): one solution, `SpaarkeMaster`, **managed by default** in customer environments, **unmanaged only
> on explicit instruction**. Design and evidence: `projects/customer-provisioning-orchestration-r1/notes/t218-plan.md`.
>
> **Build status** (2026-10-07). H6 (first import, upgrade, refusals) is built — task 218b. Steps marked *(218c)*,
> *(218d)* or *(218e)* describe behaviour customer-provisioning-orchestration-r1 is still building; the current state is
> noted beside each.

---

## 1. The package

**One solution, `SpaarkeMaster`, publisher `Spaarke` (prefix `sprk`).** It holds everything a customer environment
needs; one solution keeps every dependency inside one import, one version and one upgrade (Microsoft ALM
"single solution" pattern).

**Scope is a rule, not a list.** The package is:

- every unmanaged `sprk`-prefixed component in the **authoring environment** (`spaarkedev1`): tables, columns, global
  option sets, relationships, forms, views, web resources (**including every code page**), PCF controls, security roles,
  model-driven apps and site maps, environment-variable **definitions**;
- plus the `sprk_` columns on OOB tables listed in [`docs/data-model/oob-customizations.yaml`](../data-model/oob-customizations.yaml);
- **minus** the entries in [`docs/data-model/package-scope.json`](../data-model/package-scope.json), each with a reason and a date.
  The rule lives in `scripts/solution-authoring/SpaarkePackageScope.psm1` (tests: `tests/scripts/SpaarkePackageScope.Tests.ps1`).

Why a rule: the old scope collected only components already inside a Spaarke solution, but most deploy scripts
create components in the Default solution. The first rule run against spaarkedev1 (2026-10-07) found **44 in-scope
components missing from SpaarkeMaster** — 11 PCF controls (incl. RecordHeader), the AI Setup app and site map,
`sprk_assignedaccess`, the access scripts, the Console User and Ontology roles, 17 OOB-table columns.

**Never in the package**

| Item | Why |
|---|---|
| Environment-variable **values** | Per customer; H7 writes them. A dev value (dev BFF URL, dev container) in a customer environment is a cross-environment leak. |
| Plugins | ADR-002: no plugins; invariants live in the BFF write path. Reintroducing one needs the ADR-002 reopen criteria (CLAUDE.md §6.5). |
| Test / scratch solutions | `Test$`, `Temp`, `SCRATCH`, `MasterTest`, `TestSpaarke` names. |
| Microsoft tooling installed in dev | `CreatorKit*`, `DataverseAccelerator*`. |
| Content under another publisher | Only publisher `Spaarke` ships. |

**Not Dataverse content** (shipped elsewhere): the Office add-in and Teams packages (shared clients, one per platform),
the M365 Copilot agent (task 257), BFF code (H9).

## 2. Where it lives

| Stage | Location |
|---|---|
| Authoring | `spaarkedev1`, SpaarkeMaster **unmanaged** |
| Source of record | `src/dataverse/solutions/SpaarkeMaster/` — unpacked, both types (`pac solution unpack --packagetype Both`), committed per release *(218c/218e; today: not in git — the 2026-08-21 export exists only as a zip)* |
| Built artifacts | CI packs `SpaarkeMaster_{version}_managed.zip` and `_unmanaged.zip` from git *(218d)* |
| Provisioning store | `sprkcpartifacts{env}` / container `provisioning-artifacts`: both zips + `dataverse-solutions-latest.json` *(218d; today: a hand-uploaded `SpaarkeMaster.zip` and a hand-written manifest from 2026-08-21)* |

Manifest schema read by H6:

```json
{ "solutions": { "SpaarkeMaster": {
    "version": "1.2.0.0",
    "managedBlobName": "dataverse-solution-SpaarkeMaster-1.2.0.0-managed.zip",
    "unmanagedBlobName": "dataverse-solution-SpaarkeMaster-1.2.0.0-unmanaged.zip" } } }
```

## 3. Release (authoring environment → customers)

1. **Dev is release-ready**: the content is finished and tested in `spaarkedev1`.
2. **Drift report** (read-only): `./scripts/solution-authoring/Test-SolutionCompleteness.ps1`. It fails when a component
   matches the rule but is not in SpaarkeMaster, an in-scope table is packaged as a shell (without its columns), an
   excluded component is packaged, something is packaged outside the rule (e.g. Microsoft tables, env-var values), a
   scope entry matches nothing, an OOB column is unlisted, or the committed inventory drifted.
3. **Classify each finding**: add to the package, or add to `package-scope.json` with a reason and date. A new `sprk_` column on
   an OOB table also goes into `oob-customizations.yaml`.
4. **Assemble** (writes to dev — the release owner runs it): `Assemble-SpaarkeMasterSolution.ps1 -WhatIf`, review, then
   without `-WhatIf`. It adds what the rule finds missing and re-adds tables packaged as shells, each custom table WITH
   all its subcomponents; it never pulls in dependencies (`AddRequiredComponents = false` — with `true`, the 2026-08-23
   rebuild dragged five Microsoft tables into SpaarkeMaster); OOB tables go in metadata-only. It bumps the version only
   when every add succeeded. Anything packaged outside the rule is reported for an owner-approved removal. Bump the version (semver: Major = breaking schema change; Minor = new table/feature; Build =
   additive content; Revision = a fix for one customer stamp).
5. **Export to source**: `./scripts/solution-authoring/Export-SpaarkeMasterSource.ps1` exports managed + unmanaged,
   unpacks into `src/dataverse/solutions/SpaarkeMaster/`, strips environment-variable values, and **fails if
   `Other/Solution.xml` lists a missing dependency on `solution="Active"`** — the F12 leak: a Spaarke component the
   package references but does not contain, so the managed import fails in a fresh environment. `-WhatIf` prints the
   commands; every export names `--environment` (default spaarkedev1).
6. **PR**: the diff of the unpacked source is the release review. The release note lists **removed components**
   (they are deleted from managed customer environments on upgrade — §5).
7. **Publish** (after merge): run `publish-dataverse-solutions-manifest.yml`. CI packs both zips from git, fails if a zip
   carries environment-variable values, uploads versioned blobs and rewrites `dataverse-solutions-latest.json` *(218d)*.
8. **Roll out**: re-run provisioning per customer (§5).

## 4. First import (provisioning H6)

- **Order in the run**: H5 (environment adopted) → H3 + H10 (the importing identity is an application user) → **H6
  imports SpaarkeMaster** → H7 (environment-variable values) and H11 (users get the package's roles). The BFF deploy
  (H9) waits for H6.
- **Type**: intake `solutionPackageType` = `managed` (default) or `unmanaged` (explicit instruction only); stored on the
  run and the registry row (`sprk_solutionversion` = `SpaarkeMaster {version} ({managed|unmanaged})`, written by H13).
- **Pre-import steps**: required Power Platform apps and org settings (e.g. `maxuploadfilesize`) are applied before the
  import (task 253 moves them off `pac`).
- **Verification**: SpaarkeMaster present **and** `ismanaged` equals the requested type.

## 5. Upgrade

Re-run H6 for the customer (normally as part of an upgrade run of the whole pipeline).

| Situation | H6 behaviour |
|---|---|
| Installed version = package version | Skip (success, nothing imported) |
| Installed version lower | `StageAndUpgrade`. In a **managed** environment, a component removed from the package is **deleted** together with its data — check the release note's removal list before rolling out. |
| Installed version higher | **Refused** (Resumable, `downgrade-refused`, nothing imported) — no downgrade |
| Installed type ≠ requested type | **Refused** (Resumable, `package-type-mismatch`, nothing imported) — an environment is never switched silently between managed and unmanaged. Converting unmanaged → managed is an owner-approved operation: back up, remove overlapping unmanaged components, import managed. |
| Environment-variable values | Persist: they are never in the package, so an upgrade cannot overwrite them; H7 re-applies the run's values. |

The BFF and the package release from the same master; because H9 waits for H6, a new BFF never starts against an older
schema. No separate BFF/package version gate exists — the ordering and the downgrade refusal cover that risk.

## 6. Identity and permissions (IAM)

| Who | Needs | Granted by |
|---|---|---|
| H6 importing identity (the stamp's BFF app registration, signing in secret-free through its federated credential) | System Administrator in the customer environment | H10 (application user + role), before H6 |
| L2 Worker identity (H5, H8) | System Administrator application user | Operator, prerequisite PRQ-C-09 |
| Customer users (B2B guests) | The package's Spaarke user role(s) | H11 (`H11UserProvisioningOptions:GuestSecurityRoleNames` must name a role the package ships *(218e)*) |
| Release owner | System Customizer or higher in `spaarkedev1`; repo write for the PR | Spaarke |
| CI publish | Storage Blob Data Contributor on the provisioning-artifacts container, through OIDC | Platform Bicep |

**Roles ship in the package.** Roles created by hand today ("Spaarke Office Add In User", "Secure Record Owner") move
into SpaarkeMaster *(218e)*.

## 7. Governance

1. Publisher `Spaarke` is the only publisher for Spaarke content.
2. Test/scratch solutions never ship (exclusion pattern in `Get-SpaarkeComponents.ps1`).
3. No plugins ship (ADR-002).
4. OOB-table columns are listed in `oob-customizations.yaml` in the same PR that adds them.
5. Every exclusion carries a reason; nothing is excluded silently in code.
6. Customer environments are never customized in place; every change goes dev → git → CI → H6.

## History

| Date | Event |
|---|---|
| 2026-08-20 | Established: one managed SpaarkeMaster; three authoring scripts; 217 components; `Spaarke.Plugins` removed from spaarkedev1. |
| 2026-08-21 | SpaarkeMaster v1.1.0.0 exported (412 root components; 7 PCFs excluded by the owner) and hand-uploaded to the provisioning store. |
| 2026-10-07 | ADR-027 amended (managed by default, unmanaged on explicit instruction). Scope rule replaces solution membership; git becomes the source of record; CI publishes; this document becomes the release/IAM/upgrade runbook (customer-provisioning-orchestration-r1 T218). |

## Related

- [ADR-027](../../.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md) — the binding rule
- [Customer deployment guide](../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) — H6 in the provisioning run
- [Microsoft Learn: Organize your solutions](https://learn.microsoft.com/en-us/power-platform/alm/organize-solutions) · [ALM basics](https://learn.microsoft.com/en-us/power-platform/alm/basics-alm)
