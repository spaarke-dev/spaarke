# Spaarke Solution Package — Release, IAM and Upgrade Runbook

> **Last Reviewed**: 2026-11-11

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
- plus unmanaged `sprk_` **alternate keys on OOB tables** (T255 — an OOB table's key is not a column, so the column rule
  missed it: the contact identity-binding key `sprk_ExternalObjectIdUniqueKey`, which the BFF creates contacts by, was
  absent from the first exports; keys on `sprk_` tables ship with their table);
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
| Source of record | `src/dataverse/solutions/SpaarkeMaster/` — unpacked, both types (`pac solution unpack --packagetype Both`), committed per release (first: 1.2.0.0, 2026-10-08, T218e) |
| Built artifacts | `publish-dataverse-solutions-manifest.yml` packs managed + unmanaged from git with pac (pinned to the version that unpacked the source) and keeps them as a run artifact |
| Provisioning store | `sprkcpartifacts{env}` / container `provisioning-artifacts`: `dataverse-solution-SpaarkeMaster-{version}-managed.zip` / `-unmanaged.zip`, `dataverse-solutions-{buildId}.json`, `dataverse-solutions-latest.json` (what H6 reads) and `dataverse-solutions-latest.previous.json` (the rollback pointer). Until the first CI publish, the store holds the hand-made 2026-08-21 files, which H6 refuses (no `SpaarkeMaster` entry). |

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
   when every add succeeded. Anything packaged outside the rule, and any excluded component still packaged, is
   reported; `-RemoveUnexplained` (owner-approved) takes it out of the solution — nothing is deleted from dev, but a
   component dropped from the package is deleted from an environment on its next managed upgrade (ADR-027 §4). Bump
   the version with `-VersionBumpKind` (semver: Major = breaking schema change; Minor = new table/feature; Build =
   additive content; Revision = a fix for one customer stamp) or set it with `-Version`, which must be higher than the
   current one (H6 refuses a downgrade; 1.2.0.0 on 2026-10-08 sits above the 1.1.0.0 shipped on 2026-08-21).
5. **Export to source**: `./scripts/solution-authoring/Export-SpaarkeMasterSource.ps1` exports managed + unmanaged,
   unpacks into `src/dataverse/solutions/SpaarkeMaster/`, strips environment-variable values, and **fails if
   `Other/Solution.xml` lists a missing dependency on `solution="Active"`** — the F12 leak: a Spaarke component the
   package references but does not contain, so the managed import fails in a fresh environment. `-WhatIf` prints the
   commands; every export names `--environment` (default spaarkedev1).
6. **PR**: the diff of the unpacked source is the release review. The release note lists **removed components**
   (they are deleted from managed customer environments on upgrade — §5).
6a. **Role changes that take privileges off a role** (the release carries Basic User read-only, removals gap R3, and Core User's
   reference-table AppendTo, gap G17; `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5.1): on any environment other than dev,
   before the release, users who hold an add-on (Office Add In User, AI Analysis User, Console User) without Core User are given Core User
   or listed for the owner. Console User needs it too: it holds no AppendTo on `sprk_matter` or `sprk_project`, which the Console's
   decision commit asks, and Core User holds both. The release's expectations file under `scripts/provisioning/role-privilege-expectations/` lists the R3 privileges as absent on
   Basic User and the G17 privileges as present on Core User (the file is cumulative: a later version carries them too).
7. **Publish** (after merge): run `publish-dataverse-solutions-manifest.yml` — first with `publish: false` (dry run:
   pack + checks + manifest as a run artifact; every pull request touching the package source runs this too), then
   with `publish: true`. It refuses a zip whose name, version or managed flag is wrong, a zip with environment-variable
   values, a source with a missing dependency on `solution="Active"` (F12), and a source whose form, ribbon, sitemap or
   app module names a `sprk_` web resource the package does not contain (`Find-MissingWebResourceReferences`; a
   hand-edited form never reaches the F12 list). `publish: true` runs from master
   only. **A version publishes once**: a blob of that version from another commit refuses the run — bump with
   `Assemble-SpaarkeMasterSolution.ps1 -Version` and re-export; a re-run of the same commit after a partial failure
   replaces its own blobs. It keeps the replaced manifest as `dataverse-solutions-latest.previous.json`, then moves
   `dataverse-solutions-latest.json` and reads it back. Rollback: copy `latest.previous` over `latest` — **except
   right after the first CI publish**, when `previous` is the hand-made 2026-08-21 manifest that H6 refuses (no
   `SpaarkeMaster` entry); then roll back by re-publishing the earlier version. Nothing is deleted.
8. **Roll out**: re-run provisioning per customer (§5).

## 4. First import (provisioning H6)

- **Order in the run**: H5 (environment adopted) → H3 + H10 (the importing identity is an application user) → **H6
  imports SpaarkeMaster** → H7 (environment-variable values) and H11 (users get the package's roles). The BFF deploy
  (H9) waits for H6.
- **Type**: intake `solutionPackageType` = `managed` (default) or `unmanaged` (explicit instruction only); stored on the
  run and the registry row (`sprk_solutionversion` = `SpaarkeMaster {Managed|Unmanaged} {version}`, written by H13).
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

**Roles ship in the package** — those of the ROOT business unit (the rule takes no other: Dataverse refuses a role
authored in a child unit, "root component Role is missing"). "Spaarke Office Add In User" ships since 1.2.0.0 *(218e)*.
"Secure Record Owner" is deliberately contained in the Secure Record business unit (SECURE-PROJECT-ENVIRONMENT-SETUP.md
§5.2), so provisioning creates it per environment with that unit and its team (H7b, T256) — never the package.

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
| 2026-10-08 | SpaarkeMaster 1.2.0.0 complete in dev and committed as source (T218e); the CI pack-and-publish workflow replaces the hand-uploaded zip and manifest (T218d). |
| 2026-10-13 | SpaarkeMaster 1.2.2.0 exported. It adds `contact.sprk_externalinvitedon` and its field security (UAC task 184), the `sprk_ServiceRequest` table, the Spaarke Document Management and Spaarke Administration apps, and Dataverse search configuration. Nothing is removed. The role `Spaarke Internal Contact User (Workforce)` is excluded until UAC task 186 closes. |
| 2026-10-14 | SpaarkeMaster 1.2.3.0 exported. It adds `contact.sprk_externalinvitecodehash` (secured, not audited) and its field security (UAC task 185): the writer profile `Spaarke BFF-Managed Field Writers` gets read/create/update, the reader profile gets nothing. Also carries other projects' dev changes inside the package rule (`sprk_ServiceRequest` auto-number `RQST-{SEQNUM:4}` and read-only-in-UI request number; web-resource and PCF bundle updates). Nothing is removed. The role `Spaarke Internal Contact User (Workforce)` stays excluded until UAC task 186 closes. |
| 2026-10-16 | SpaarkeMaster 1.2.3.1 (Revision). The first managed install into a new environment (T186, ACME) failed: 22 options had **empty English labels** — global choices `sprk_identityplane` and `sprk_identitycollisionreason`, and `sprk_assignedaccess.sprk_state`. The schema scripts that created them read option labels from an `[ordered]` dictionary by int key, which indexes by position and returns `$null`. The fix labels the options in spaarkedev1 and in source, fixes both scripts (they now label existing empty options too), and adds the CI guard `SpaarkeMasterOptionLabelsPackagedTests`. An unmanaged import over existing options does not catch this; only a fresh managed install does. Nothing is removed. |
| 2026-10-23 | SpaarkeMaster 1.2.4.0 (Build) in source. It carries the contact field security that `scripts/Set-ExternalAccessContactSecuritySchema.ps1` applies to an existing environment: `sprk_accesspermissiongrant`, `sprk_identityplane` and the five `sprk_identitycollision*` columns secured, and three BFF-only columns secured at creation (`sprk_externalinvitedemailhash`, `sprk_externalinvitedtenantid`, `sprk_externalaccessremovedon`), with their profile permissions. Provisioning H7b (S18, S19, S22) verifies these locks, so an H7b carrying those checks requires 1.2.4.0 published before the control plane is deployed. The package source was edited by hand (no Assemble or Export ran); before publishing, the release owner sets SpaarkeMaster in spaarkedev1 to 1.2.4.0, confirms the script's `-Verify` exits 0 there, and re-exports so the next export keeps the version and the locks. Nothing is removed. |
| 2026-11-06 | SpaarkeMaster 1.3.0.0 (Minor: new tables). Source only (no Assemble or Export ran); the version is set by hand in `Other/Solution.xml`. **Adds** ebilling-r1's schema (#2591, closes the source half of #2584): `sprk_ebillingoverride`, `sprk_procedure`, `sprk_procedureassignment`, `sprk_procedureversion`, `sprk_storedfile` with their columns, option sets, relationships and field security, plus the other eBilling tables, columns, views and ribbon since 1.2.4.0; the role `Spaarke Billing Administrator`; D-206 audit flags on 90 tables and 542 columns (#2582 — they record only with the H6 org setting). **Removes (deleted with their data on a managed upgrade):** table `sprk_InvoiceLineItem` with its forms, views and AI skill config (ebilling-r1 task 029), column `sprk_invoice.sprk_invoicestatus` and global option set `sprk_invoicestatus` (task 015), and columns `sprk_timekeeper.sprk_invoice` and `sprk_timekeeper.sprk_invoicelineitem`. Every removal was owner-approved in ebilling-r1: `sprk_InvoiceLineItem` by D-18 (`sprk_billingevent` is the invoice line; task 011, `notes/011-retire-invoicelineitem.md`), `sprk_invoicestatus` column and option set by D-41 and D-97 (task 015, OOB statecode/statuscode instead), `sprk_timekeeper.sprk_invoice` on 2026-10-15 (`notes/017-timekeeper-ratecard-schema.md` §8.5) and `sprk_timekeeper.sprk_invoicelineitem` with its table (task 011). ACME (on 1.2.3.1) holds 0 invoice and 0 invoice-line-item rows (read 2026-11-06). **Role changes:** Basic, Core User and Ontology Service lose `sprk_InvoiceLineItem`; Basic and Core User lose Read on `sprk_ExternalServiceConfig` and read AI and grid configuration at Global (#2474); Basic, Core and Office Add In User gain the eBilling tables; Basic User becomes read-only on Spaarke tables (it loses every Create, Write, Delete, Append, AppendTo, Assign and Share privilege on a `sprk_*` table: the 80 privileges of removals gap R3, which a managed upgrade takes off the role), and Core User gains AppendTo on `sprk_agreementtype`, `sprk_organizationtype_ref` and `sprk_documenttype_ref` at Global (gap G17); on an environment other than dev, do step 6a first. After the upgrade run `scripts/provisioning/Test-RolePrivilegeExpectations.ps1` with `spaarkemaster-1.3.0.0.json`. `sprk_workassignmenttype` is still not in source (#2584, owner unclaimed). Before publishing, the release owner sets SpaarkeMaster in spaarkedev1 to 1.3.0.0 so the next export keeps the version. A stamp BFF carrying ebilling 092 or later needs this release installed first. |
| 2026-11-12 | SpaarkeMaster 1.3.1.0 (Build: additive content). Source only (no Assemble or Export ran); the version is set by hand in `Other/Solution.xml`. Diff base: the published 1.3.0.0 (master `d53d67f604`, publish run 2026-11-07). **Adds:** `sprk_invoice`'s seven money and firm columns (`sprk_totalamount`, `sprk_approvedtotal`, `sprk_feestotal`, `sprk_expensestotal`, `sprk_lineadjustmentstotal`, `sprk_invoiceadjustmentstotal`, `sprk_vendororg`) field-secured with the BFF-Managed Field Readers / Writers grants (ebilling-r1 task 166, EB-D-200; provisioning H7b S26 verifies the lock, and before this release only `scripts/ebilling/Set-EbillingInvoiceMoneyFieldSecurity.ps1` could apply it); `sprk_invoicesubmission.sprk_trackingscope` with the scope key `sprk_invoicesubmission_scope_trackingnumber` next to the firm key (095c-1; both keys stay in source) and `sprk_sourceemaildocument` field-secured (095a); `sprk_procedureversion.sprk_publishedon` (DEF-061) and `sprk_procedureversion.sprk_notconvertedlist`, a field-secured memo (100000) with `sprk_notconverted`'s grants, which the BFF reads and writes from ebilling-r1 task 116 (#2724) on (EB-D-211; it was in `scripts/ebilling/Deploy-EbillingProcedureSchema.ps1` only, added to source by hand in this release, together with that script's EB-D-211 Description on `sprk_notconverted`); `sprk_externalrecordaccess.sprk_pairedsystemuser` (field-secured lookup to systemuser, readers read only) with its relationship (unified-access-control-r2 193, #2700); the role `Spaarke Decision Record Reader` (Read on `sprk_DecisionRecord` at Global only; ebilling-r1 DEF-059); ribbon changes on `sprk_billingevent`, `sprk_invoice` and `sprk_billingexport` and `sprk_/scripts/secure_child_ribbon.js` (ebilling-r1 096b, 164, 151e); the Finance area of the Matter Management app with `sprk_billingexport`; form changes (invoice totals and submission firm/outcome read-only on the forms, the billing export quick view shows Sent to AP Date, the CSV / JSON document cells leave the billing export main form). **Removes:** nothing (no table, column, option set, key, relationship, form, view, role or privilege; the form edits drop cells, not components). **Role changes:** additive only (the new role); after the upgrade run `scripts/provisioning/Test-RolePrivilegeExpectations.ps1` with `spaarkemaster-1.3.1.0.json` (cumulative). **Upgrade of an environment that already has `sprk_invoicesubmission` rows:** run `scripts/ebilling/Deploy-EbillingSubmissionTrackingScopeSchema.ps1` (steps 1 to 8) immediately after the import, as `projects/ebilling-r1/notes/095c-from-email-firm-optional.md` section 9 says; a new stamp (no rows) needs nothing. Before publishing, the release owner sets SpaarkeMaster in spaarkedev1 to 1.3.1.0 so the next export keeps the version. A stamp BFF carrying ebilling task 116 or later needs this release installed first. **Also in 1.3.1.0 (field-mapping-server-write-path-r1 task 054b, #2733; merged after the version bump, packed in this publish):** `sprk_communication.sprk_sentby` and `.sprk_communicationthread`, and `sprk_communicationthread.sprk_threadtype` and `.sprk_isdefaultthread`, are field-secured, with read for `Spaarke BFF-Managed Field Readers` and read/create/update for `Spaarke BFF-Managed Field Writers`. A secured column reads EMPTY, with no error, for any user outside the reader profile, and the profiles' members are per environment. So on every environment that imports this release, run `scripts/Set-RecordCreatorPersonSchema.ps1 -Apply` (the profile members; dev: unified-access-control-r2 191i, step 2 after `Set-ExternalCollaboratorRole -Apply`, on that project's owner go, never run there out of order) and `scripts/Set-DirectRecipientSchema.ps1 -Apply`, then `scripts/Set-ThreadFlagFieldSecurity.ps1` dry run, `-Apply` and `-Verify` (it must exit 0; it also secures `sprk_directrecipient`, which is not in source), as the customer deployment guide says. **Exception, a customer stamp:** provisioning runs none of these three scripts. H7b adds the profile members (S11/S12) and its S28 (customer-provisioning-orchestration-r1 task 270) verifies the four packaged columns' lock; `sprk_directrecipient` stays absent and the Direct-message feature off, unless the owner turns it on (customer deployment guide §7.4). On dev, `Set-ThreadFlagFieldSecurity.ps1` already ran (2026-11-13, `-Verify` exit 0). Role changes: none from this item (`spaarkemaster-1.3.1.0.json` is unchanged). |
| 2026-11-16 | SpaarkeMaster 1.3.2.0 (Build: additive content). Source only (no Assemble or Export ran); the version is set by hand in `Other/Solution.xml`. Diff base: the published 1.3.1.0 (master `d8b53ec0c3`, publish run 2026-11-13). **Adds:** the invoice-family field lock (unified-access-control-r2 #2799; owner decision 2026-11-16 "path A", #2859): the 24 columns of `scripts/common/InvoiceFamilyLockedColumns.psd1` (the matter, project and work-assignment lookups of `sprk_invoice`, `sprk_billingevent`, `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_invoicecorrection` and `sprk_externalidentifier`, their invoice links, and `sprk_invoice.sprk_supersedesinvoice`) field-secured, with read for `Spaarke BFF-Managed Field Readers`, read/create/update for `Spaarke BFF-Managed Field Writers` and no other profile, which is what `scripts/Set-InvoiceFamilyFieldSecurity.ps1` applies (`InvoiceFamilyFieldLockSourceGuardTests` pins the source to the list); Read on `sprk_procedureversion` and `sprk_procedureassignment` at Deep for `Spaarke Billing Administrator` (ebilling-r1 task 084f, EB-D-220, #2822, merged after the 1.3.1.0 publish); `sprk_ruleset.sprk_visibletofirms` field-secured, with read for `Spaarke BFF-Managed Field Readers` and read/create/update for `Spaarke BFF-Managed Field Writers` (ebilling-r1 ISS-040, #2901: only the BFF sets whether a rule set is shown to firms; every user still reads it); `sprk_invoicesubmission.sprk_firmnoticetext` field-secured, with read/create/update for `Spaarke BFF-Managed Field Writers` and NO reader profile (ebilling-r1 task 132: the stored firm notice is read and written by the BFF only, so every other user reads it empty). **Removes:** Create, Write, Delete, Assign and Share on `sprk_invoicecorrection` from `Spaarke Core User` (ebilling-r1 ISS028, EB-D-249: correction rows are written only by the BFF; Read, Append and AppendTo at Deep stay), and the Firm notice section (the `sprk_firmnoticetext` cell) from the `sprk_invoicesubmission` main form (a form edit, not a component). **Role changes:** additive (the two Reads) plus the five ISS028 removals, so this release needs a **managed stage-and-upgrade** (a plain update import leaves the removed privileges in place); after the upgrade run `scripts/provisioning/Test-RolePrivilegeExpectations.ps1` with `spaarkemaster-1.3.2.0.json` (cumulative; it lists the five ISS028 privileges in Core User's absent list). On dev the ISS028 removals and both new column locks are already applied by script (`scripts/Set-FeatureRolePrivileges.ps1 -Remove -Gap ISS028`, `scripts/ebilling/Set-EbillingRuleSetVisibilityFieldSecurity.ps1`, `scripts/ebilling/Set-EbillingFirmNoticeTextFieldSecurity.ps1`, 2026-11-17, each `-Verify` passing), so the import changes nothing there for them; to roll back the role removals on dev, run `Set-FeatureRolePrivileges.ps1 -Restore -Gap ISS028 -Apply`. **Pre-import requirement on an EXISTING environment:** deploy, BEFORE this import, (1) the BFF that writes these columns as the application (any BFF build from master at or after the #2799 merges, chunks 1-3) and (2) the current web resources (every one the `Set-InvoiceFamilyFieldSecurity.ps1` dry run names in (p4a)). Otherwise every client write of these lookups fails once the import secures them. **Where `scripts/Set-InvoiceFamilyFieldSecurity.ps1` already ran (dev):** the import is a no-op for these columns (dev read back 2026-11-16: all 24 secured, with exactly the reader and writer grants of the source, plus the platform's System Administrator profile, which is never in source). **New stamp:** the import alone secures the columns and grants both profiles; provisioning H7b S27 verifies the lock (customer-provisioning-orchestration-r1 task 270, #2859; not yet on master). On every environment the two profiles' MEMBERS are per environment (`scripts/Set-RecordCreatorPersonSchema.ps1 -Apply`, as for 1.3.1.0): a user outside the reader profile reads these lookups EMPTY. **Also refreshed in this release:** the code pages `sprk_creatematterwizard`, `sprk_spaarkeai` and `sprk_createinvoicewizard`, whose committed bundles (2026-10-13) were older than source and wrote a locked column on a family table; they are rebuilt from master source (`npm run build` in `src/solutions/CreateMatterWizard`, `SpaarkeAi`, `CreateInvoiceWizard`), `Find-ClientInvoiceFamilyWrite.ps1` finds no write in them, and the ratchet baseline of `InvoiceFamilyFieldLockGuardTests` is empty. **Before publishing:** the release owner sets SpaarkeMaster in spaarkedev1 to 1.3.2.0 so the next export keeps the version. |
| 2026-11-22 | SpaarkeMaster 1.3.2.1 (Revision: fix). Source only. 1.3.1.0 and 1.3.2.0 cannot be imported where the `sprk_invoicefiles` web resource does not already exist: the Invoice main form's Files section (ebilling-r1 task 151e) names it and the package did not contain it. Dev never failed because its form has no Files section. Adds the built `src/solutions/InvoiceFiles` page as `WebResources/sprk_invoicefiles` with its type 61 root component, and the publish check `Find-MissingWebResourceReferences`. Nothing is removed. |
| 2026-11-24 | Source change for the next SpaarkeMaster release (no version set; the release owner sets it). **Removes** the web resource `sprk_DocumentDelete.js` (customer-provisioning-orchestration-r1 ISS-027, #1866): no ribbon, form or web resource references it, and it maps organization URLs to deleted BFF hosts. Dependency check in spaarkedev1 (`RetrieveDependenciesForDelete`, 2026-11-24): none. A managed upgrade deletes it from each customer environment, which is intended. **Before the next export:** the release owner removes it from the SpaarkeMaster solution in spaarkedev1 (`RemoveSolutionComponent`, component type 61), or the export adds it back. The unshipped legacy copy `src/dataverse/webresources/spaarke_documents/` is deleted from the repository (not deployed in spaarkedev1). |
| 2026-11-25 | Source change for the next SpaarkeMaster release (no version set; the release owner sets it). **Role change (customer-provisioning-orchestration-r1 ISS-042, #3105):** Spaarke Basic User, Spaarke Core User and Spaarke Office Add In User read `sprk_eventtype_ref` at Global (`prvReadsprk_EventType_Ref` Deep to Global; owner ruling 2026-11-25, same pattern as #2474). Reason: the table is user-owned, provisioning creates its rows as the control-plane app user in a stamp's root business unit, and the stamp's users sit in a child unit, so Deep read hid every event type (the Create Event wizard picker was empty). Additive: it raises one privilege depth on three roles and removes nothing, so a managed upgrade has no customer-data impact. **Declared in two places:** Core User and Basic User in `config/product-configuration-read-role.json` (the product-configuration list of #2474, applied by `scripts/Set-ProductConfigurationReadDepth.ps1`); Office Add In User in `config/feature-role-privileges.json` (gap ISS-042, applied by `scripts/Set-FeatureRolePrivileges.ps1`; it is held with a base role and is ruled not standalone). `config/core-user-wizard-linking-role.json` no longer needs the `appendToAboveRead` exception on `sprk_eventtype_ref`, since Read now equals AppendTo. **The next version's `scripts/provisioning/role-privilege-expectations/spaarkemaster-{version}.json` must carry it** (the three roles at `prvReadsprk_EventType_Ref` Global; the guard enforces Office Add In User's entry, the other two are written by hand); the release owner writes that file when the version is set, and the published files are not edited. **Before the next export from spaarkedev1,** the release owner applies it on dev, so the export keeps the Global depth. Each step is a dry run until `-Apply`, and `-Apply` needs the owner's OK. (1) Core User and Basic User: `pwsh scripts/Set-ProductConfigurationReadDepth.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run; expect exactly two pending lines, `prvReadsprk_EventType_Ref` on Spaarke Core User and Spaarke Basic User, Deep to Global; the script has no gap filter either, so any other pending line is applied by `-Apply` too), then the same with `-Apply`, then the same with `-Verify` (exit 0). (2) Office Add In User: `pwsh scripts/Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run; expect exactly one pending line, Spaarke Office Add In User `prvReadsprk_EventType_Ref` Deep to Global), then the same with `-Apply`, then the same with `-Verify -ChangesOnly` (exit 0). `-Gap` does not apply here: it works only with `-Remove` and `-Restore` (the script throws otherwise), and the adds have no gap filter, so `-Apply` applies EVERY pending change in the config, not only ISS-042; read the dry run first and stop if it lists anything else. `-ChangesOnly` checks the adds only, because plain `-Verify` also fails while any removal of the config is still pending. Until it is applied on dev, `scripts/Set-WizardLinkingAndMembershipPrivileges.ps1 -Verify` reports Core User's Read on `sprk_eventtype_ref` as drift (live Deep, declared Global); that is expected, and it clears once step (1) has run (that script only raises AppendTo and never writes Read). **Stamps already provisioned** keep their Deep read, so their Create Event picker stays empty until their next SpaarkeMaster managed upgrade (an upgrade run) imports the release that carries this; nothing in this change reaches them earlier. |
| 2026-11-25 | SpaarkeMaster 1.3.3.0 (Build). Source only (no Assemble or Export ran); the version is set by hand in `Other/Solution.xml`. Diff base: the published 1.3.2.1 (master `bc4565a1b5`, publish run 2026-11-22). **Adds:** a "Billing firm" section with `sprk_billingfirm` and `sprk_billingresponsible` on the Billing tab of the Matter and Project main forms, and a new Billing tab with that section on the Work Assignment main form (ebilling-r1 task 079); the Rule Review page `WebResources/sprk_rulereviewpage` with its type 61 root component, and a "Rule Review" entry after Rule Sets in the eBilling group of the Spaarke Administration sitemap (079, D-114); `sprk_policyversion.sprk_firmdescription` and `.sprk_rejectionreason`, memo 2000 (ebilling-r1 130e-1 and D-113 / ISS-024; on dev by script since then). **Changes:** the Matter main form's Email & Messages grid is wired to its matter (#2998); the `sprk_communication` grid command reads "New Email" and `sprk_/scripts/secure_child_ribbon.js` is updated (#2938); the invoice main and quick-create forms drop the retired `sprk_extractedjson` and `sprk_extractionstatus` cells (ebilling-r1 task 194, ISS-045). **Removes (deleted with their data on a managed upgrade):** columns `sprk_ratecard.sprk_matter`, `.sprk_project` and `.sprk_workassignment` with the relationships `sprk_matter_ratecard_matter`, `sprk_project_ratecard_project` and `sprk_workassignment_ratecard_workassignment`, their cells on the rate card main form and their column in a rate card view (ebilling-r1 task 193, DEF-027: a rate card belongs to a Procedure Version since task 085 and nothing reads them; owner decision 2026-11-23); the web resource `sprk_DocumentDelete.js`, which nothing referenced (ISS-027, #3074; the 2026-11-24 source change above, which this version carries). **Role changes:** additive only: Spaarke Basic User, Spaarke Core User and Spaarke Office Add In User read `sprk_eventtype_ref` at Global (ISS-042, #3107; the 2026-11-25 source change above, which this version carries). After the upgrade run `scripts/provisioning/Test-RolePrivilegeExpectations.ps1` with `spaarkemaster-1.3.3.0.json` (cumulative). On dev, apply ISS-042 first, as the row above says, so the next export keeps the Global depth. **Before publishing:** the release owner sets SpaarkeMaster in spaarkedev1 to 1.3.3.0 so the next export keeps the version. |

## Related

- [ADR-027](../../.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md) — the binding rule
- [Customer deployment guide](../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) — H6 in the provisioning run
- [Microsoft Learn: Organize your solutions](https://learn.microsoft.com/en-us/power-platform/alm/organize-solutions) · [ALM basics](https://learn.microsoft.com/en-us/power-platform/alm/basics-alm)
