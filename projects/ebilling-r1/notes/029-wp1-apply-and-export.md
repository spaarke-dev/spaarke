# Task 029 - Apply WP1 schema to dev; sync SpaarkeMaster source; forms and views

> Project: ebilling-r1 (FR-08, FR-35, FR-48, D-103). Executed 2026-10-16 against dev (`https://spaarkedev1.crm.dynamics.com`, solution `SpaarkeMaster`).
> Script: `scripts/ebilling/Deploy-EbillingFormsViews.ps1` (new, reusable by tasks 053 / 079 / 112 / 136 / 153).
> No commit was made by this task (the main session commits).

## Result

| Step | Outcome |
|---|---|
| 0 Preconditions | 010-028 done; 011 done 2026-10-16 with owner OK (notes/011 section 8). |
| 1 Idempotence | All WP1 scripts re-run: nothing changed (table 1). No escalation trigger fired. |
| 2 Forms and views | 11 stub main forms rewritten; `section_ebilling` added to the sprk_timekeeper / sprk_invoice / sprk_billingevent main forms; 12 Active views re-columned; "Invoices to review" view created and tested (table 2, section 4). |
| 3 Sitemap | Administration app: new eBilling area (5 subareas). Matter Management: invoice submission and billing export after the invoice entry. Published. **Tables not yet in the apps: see section 7.** |
| 4 Completeness | Before/after in section 5. No eBilling finding left; 6 unrelated identity "missing" findings remain. |
| 5 Export / source sync | Done: only eBilling files and hunks copied (section 6). |
| 6 Round trip | `pac solution pack` Both: OK. `-PlanOnly`: lists the new tables, sitemaps and app modules; one unrelated unmapped component (section 8). |
| 7 Column table | Dev equals source for all 23 touched tables (section 9). |

## 1. Idempotence (step 1), dev, second run of every script

| Script (task) | Result |
|---|---|
| Deploy-EbillingInvoiceLineSchema (010) | changed 0, skipped 27 |
| Remove-EbillingInvoiceLineItem (011, report mode) | table already retired, nothing to do |
| Deploy-EbillingInvoiceHeaderSchema (012) | created 0, changed 0, skipped 16 |
| Deploy-EbillingTimekeeperRateCardSchema (017) | created 0, changed 0, skipped 51 |
| Deploy-EbillingBillingCodeSchema (018a) | created 0, changed 0, skipped 18 |
| Seed-EbillingBillingCodes (018b) | no rows created; the 8 seeded sets listed |
| Deploy-EbillingPolicyStoreSchema (019) | created 0, changed 0, skipped 21 |
| Deploy-EbillingSubmissionSchema (020) | changed 0, skipped 32 |
| Deploy-EbillingApprovalSchema (021) | changed 0, skipped 21 |
| Deploy-EbillingExternalIdentifierSchema (022) | changed 0, skipped 21 |
| Deploy-EbillingExportSchema (023) | created 0, changed 0, skipped 12 |
| Deploy-EbillingApproverRoleSchema (024) | created 0, changed 0, skipped 10 |
| Deploy-EbillingSettingsSchema (025) | created 0, changed 0, skipped 23 |
| Deploy-EbillingCorrectionSchema (027) | changed 0, skipped 28 |
| Deploy-EbillingUserProfileRoleChoice (D-84) | created 0, changed 0, skipped 6 |

No script wrote anything; escalation trigger 1 did not fire. (Raw outputs: `C:\tmp\eb029\real-*.txt`, `whatif-*.txt`, not kept in the repo.)

## 2. Forms, views and sitemaps written to dev (GUIDs)

New-table main forms (stub form rewritten, all controls read-only, no script, no business rule - ADR-002):

| Table | Main form id | Active view id |
|---|---|---|
| sprk_ebillingsetting | 6d427c3f-b268-4519-822f-a2378d204093 | 99e7f5ec-00b9-40ad-b90b-1ffdce360805 |
| sprk_ratecard (rates subgrid) | da801593-8779-4642-b813-b79a5c7b5f5d | de71004e-a70c-4ba9-a3b5-5e461cfda031 |
| sprk_ratecardrate | 28eabf78-47a2-478a-926a-53c059cc7d30 | 384eda74-dd33-483c-a0b9-8547e0d829be |
| sprk_billingcodeset (codes subgrid) | c60f1ac6-1ca7-440e-9c8a-63a0e25a3b1a | 5c1cb850-9acc-41d9-a5e8-76cf46b21e79 |
| sprk_billingcode | 96155153-6106-45f7-bd8d-1db2d1f1366d | 80e28a07-15d8-4e9a-bfde-c43487653395 |
| sprk_ruleset | bafad1a6-ace0-42ad-bc71-b9df4ecbd8eb | f8fe410f-1840-4796-af0a-a1bb2f1da2ea |
| sprk_invoicesubmission | c412f942-875e-45c4-9ea1-cae75a81f1b9 | 6398d675-b042-46ff-b185-380ba3256c9a |
| sprk_invoiceapproval (eligible-approvers N:N subgrid) | 3975a905-259d-4a2a-bc33-9cf164dddb98 | c9644831-ede8-407e-910c-992831ddc1fe |
| sprk_externalidentifier | 7a565ec1-a5db-44fa-8998-44688098e498 | 41ae0d21-c5c3-4945-af68-9e8cac702c29 |
| sprk_billingexport | bd0020ec-1912-4f2d-b3e6-a6315f8b9e48 | c19542df-a537-49c5-9021-b4a6cbb3fc36 |
| sprk_invoicecorrection (18 controls disabled) | cfdcfae9-2340-455e-98b5-083910cfb67c | 9668e5da-4975-4475-9d14-7b85ec774f5e |

Existing tables (only `section_ebilling` added, rest of the form left as it was):

| Table | Main form id | Notes |
|---|---|---|
| sprk_timekeeper | d7330607-7605-f111-8406-7c1e525abd8b | Active view 2b30e977-c1d6-417a-b5f0-b0489833e354 re-columned |
| sprk_invoice | 93aa1c69-0406-f111-8406-7c1e525abd8b | The five totals are disabled controls (task 016) |
| sprk_billingevent | 54b1c4d9-cebc-f111-aaad-7c1e520a989f | `sprk_amount` is a disabled control (task 016) |

View created in SpaarkeMaster: **Invoices to review** on sprk_invoice, id `0c7694d8-14c9-f111-a05a-7c1e520a989f`.

Sitemaps patched and published: sprk_SpaarkeAdministration `b3e6c66f-f1c3-f111-a05c-0022482913fc` (area `area_ebilling`, group `group_ebilling`, subareas `subarea_eb_setting`, `subarea_eb_timekeeper`, `subarea_eb_ratecard`, `subarea_eb_codeset`, `subarea_eb_ruleset`); sprk_MatterManagement `57410a70-ca73-f011-b4cb-6045bdd6a665` (`subarea_eb_submission`, `subarea_eb_export`). App modules republished: Administration `b7e6c66f-f1c3-f111-a05c-0022482913fc`.

Publish: entity-scoped PublishXml for the 14 touched entities, the 2 sitemaps and the 2 app modules. Never PublishAllXml. Pre-change JSON of every edited form, view and sitemap is in `C:\tmp\eb029\backup\` (temp, not in the repo).

Pending collateral (D-103): sprk_invoicecorrection had 3 task-027 forms that were unpublished; they were published with `-AllowPendingCollateral` (they are eBilling's own, task 027). No other project's pending change was published.

Final re-run of the script: `changed 0, unchanged 29`, no publish.

## 3. Read-only controls (task 014 / 016 constraints)

- sprk_invoice main form: `sprk_feestotal`, `sprk_expensestotal`, `sprk_lineadjustmentstotal`, `sprk_invoiceadjustmentstotal` and `sprk_approvedtotal` are disabled (the totals are written by `IInvoiceTotalsService`).
- sprk_billingevent main form: `sprk_amount` is disabled. **Note:** this also makes Fee / Expense line amounts read-only on the form (they are written by the extraction pipeline, not typed). The Adjustment-line rule (task 016) is satisfied; narrowing it to Adjustment lines only would need a script or business rule, which ADR-002 rules out.
- Hide OOB Activate / Deactivate on the sprk_invoice command bar (task 014, #1957): see section 6 (source only).

## 4. "Invoices to review" - behaviour test (dev, run as the calling user)

FetchXML: invoice (statecode 0, statuscode 1 In Review) -> `sprk_invoiceapproval` (Pending 100000000) -> N:N `sprk_invoiceapproval_eligibleapprover_contact` -> contact where `sprk_systemuser eq-userid`.

Four temporary invoices as Ralph Schroeder (contact 8e9918a9..., user 1d02f31c...):

| Test invoice | Approval | Eligible approver | In the view? |
|---|---|---|---|
| 1 In Review | Pending | me | yes (expected) |
| 2 In Review | Pending | another person's contact | no (expected) |
| 3 In Review | Approved | me | no (expected) |
| 4 Approved invoice | Pending | me | no (expected) |

All four invoices and their approvals were deleted afterwards (0 left).

**Not tested:** the view as a second user. Impersonating non-admin users failed with 0x80040220: the Spaarke roles carry no privilege on `sprk_invoiceapproval` (see section 7, finding B).

## 5. Test-SolutionCompleteness (step 4 and 6)

| Finding | Before | After |
|---|---|---|
| MISSING FROM PACKAGE | 6 (identity: contact.sprk_externaltenantid, systemuser.sprk_homeobjectid / homeobjectidkey / hometenantid / hometenantidkey, EntityKey systemuser.sprk_homeidentityuniquekey) | same 6 (not eBilling; no eBilling table, column, form or view is missing) |
| STALE EXCLUSION | 6 custom controls | same 6 |
| OOB COLUMN NOT LISTED | 5 | same 5 |
| INVENTORY new in dev | Entity 11, OptionSet 3, Attribute 5, ComponentType_14 1 | Attribute 5, ComponentType_14 1 (identity, not eBilling) |
| INVENTORY gone from dev | Entity 1 (sprk_invoicelineitem), ComponentType_10314 2, CanvasApp 3, CustomControl 5, WebResource 2 | CanvasApp 3, CustomControl 5, WebResource 2 (not eBilling) |

Nothing was added with AddSolutionComponent: no eBilling "missing / shell / outside" finding existed (the 12 tables and their columns were already in SpaarkeMaster; the new forms and view were created in or added to it). The inventory file `docs/data-model/spaarke-components-inventory.json` was patched by hand with the eBilling hunks only (11 entities and 3 option sets added; sprk_invoicelineitem and its 2 config rows removed; counts and sort order kept). A full inventory refresh stays a release-time step.

Outputs: `C:\tmp\eb029\completeness-before.txt`, `completeness-after.txt`.

## 6. Source sync (step 5)

Export: `Export-SpaarkeMasterSource.ps1` into a temp folder (`C:\tmp\eb029\exp`), compared with `src/dataverse/solutions/SpaarkeMaster` (line endings ignored), only eBilling files and hunks copied.

The script's F12 guard threw at the end on 6 unrelated leaks (the identity columns of section 5, found by the type-70 role dependency). The unpack had finished by then; the folder was used only as a source for eBilling hunks. Nothing from it was copied wholesale.

Copied whole (every difference was eBilling):
- New: `Entities/sprk_{billingcode,billingcodeset,billingexport,ebillingsetting,externalidentifier,invoiceapproval,invoicecorrection,invoicesubmission,ratecard,ratecardrate,ruleset}/`; `Entities/sprk_Invoice/SavedQueries/{0c7694d8-...}.xml`; `OptionSets/sprk_billingapproverrole.xml`, `sprk_policydomain.xml`, `sprk_userprofilerole.xml`; `Other/Relationships/sprk_{billingcode,billingcodeset,billingexport,invoiceapproval,invoicesubmission,ratecard,ruleset}.xml`.
- Changed: `Entity.xml` of sprk_Invoice, sprk_BillingEvent, sprk_Timekeeper, sprk_Policy, sprk_PolicyVersion, sprk_Matter, sprk_Project, sprk_WorkAssignment, sprk_Agreement, sprk_Organization, sprk_UserProfile, sprk_MatterType_Ref, sprk_PracticeArea_Ref; the Invoice, BillingEvent (2) and Timekeeper main forms (+ `_managed`); the Timekeeper Active view; `OptionSets/sprk_costtype.xml` (Adjustment), `sprk_timekeeperrole.xml`; `Other/Relationships.xml` and all 18 changed `Other/Relationships/*.xml` (every added or removed relationship checked by name: all eBilling or the retired line item); both sitemaps (+ `_managed`).
  (Earlier reading that sprk_UserProfile Entity.xml was unrelated drift was wrong: its change is the `sprk_userprofilerole` choice, task D-84.)

Hunks only:
- `Other/Solution.xml`: the 11 new table RootComponents, the 3 option-set RootComponents, the sprk_invoicelineitem RootComponent removed. Not taken: the version bump 1.2.3.0 to 1.2.3.1 (release step), 6 identity MissingDependency entries, a reordered PowerAppsOneGrid dependency.
- `Other/FieldSecurityProfiles.xml`: the 4 new `sprk_createdbyperson` permissions (invoicecorrection, externalidentifier, invoicesubmission, invoiceapproval). Not taken: the identity permissions on contact and systemuser.
- `Roles/Spaarke Basic User.xml`, `Spaarke Core User.xml`, `Spaarke Ontology Service.xml`: only the `*sprk_InvoiceLineItem` privilege lines removed (2, 8, 1). Not taken: `prvReadsprk_Policy` (Basic User) and `prvAppendToContact` (Ontology Service), other projects' changes.
- `AppModules/sprk_SpaarkeAdministration/AppModule.xml` (+ `_managed`): AppModuleComponent entries for sprk_billingcodeset, sprk_ebillingsetting, sprk_ratecard, sprk_ruleset, sprk_timekeeper. `AppModules/sprk_MatterManagement/AppModule.xml` (+ `_managed`): sprk_billingexport, sprk_invoicesubmission. So a release import puts the tables in the apps (dev has them only after the app-designer step, section 7).

Removed (notes/011 section 6): `Entities/sprk_InvoiceLineItem/`, `Other/Relationships/sprk_InvoiceLineItem.xml`, `aiskillconfigs/ais_FormFillFieldOptOut_sprk_invoicelineitem_sprk_recordsummary/`, the line-item privilege lines in the 3 roles, the RootComponent line, and the Timekeeper / BillingEvent / Invoice references (via the copied files). `grep -ri invoicelineitem src/dataverse/solutions/SpaarkeMaster` finds nothing.

Hand-written (not in the export): `Entities/sprk_Invoice/RibbonDiff.xml` now hides the OOB Activate / Deactivate (task 014, #1957) with 7 `HideCustomAction` elements (pattern of `Entities/Contact/RibbonDiff.xml`): `Mscrm.Form.sprk_invoice.Activate`, `.Deactivate`, `.Re-activate`; `Mscrm.HomepageGrid.sprk_invoice.Activate`, `.Deactivate`; `Mscrm.SubGrid.sprk_invoice.Activate`, `.Deactivate` (button ids confirmed with RetrieveEntityRibbon). **Source only**: a ribbon change reaches dev only through a ribbon solution import, which this task must not run; the live command bar still shows them until the release import.

Kept as in the repo (the export differs; needs deploying separately): `WebResources/sprk_/scripts/secure_child_ribbon.js`. The dev copy is not the task-013 version; the web resource needs a SpaarkeMaster redeploy (a release or ribbon-deploy task).

### Unrelated drift left alone (not eBilling)

`Controls/*` (CommunicationActions, CommunicationConnections, CommunicationConversationPanel, CommunicationMessageActions, CommunicationTimeline bundle.js and ControlManifest.xml); `WebResources/sprk_/icons/**`, `sprk_communicationconversationpage.html`, `sprk_emailpage`, `sprk_event_ribbon_commands.js`, `sprk_spaarkeai`; `Entities/sprk_assignedaccess/Entity.xml` (option labels), `Entities/sprk_Communication/**` (label rename "Is Internal Only"); `OptionSets/sprk_identitycollisionreason.xml`, `sprk_identityplane.xml`; `Other/Customizations.xml` (server version attribute only); identity entries in Solution.xml and FieldSecurityProfiles.xml; `prvReadsprk_Policy`, `prvAppendToContact`; the version bump.

## 7. Open findings (owner / follow-up)

A. **Add the 7 tables to the apps by hand** (app designer, make.powerapps.com, then Save and Publish): sprk_ebillingsetting, sprk_timekeeper, sprk_ratecard, sprk_billingcodeset, sprk_ruleset to sprk_SpaarkeAdministration; sprk_invoicesubmission, sprk_billingexport to sprk_MatterManagement. `AddAppComponents` returns 204 but adds nothing, `appmodulecomponent` has no Create (0x80040800) and rows cannot be DELETEd. Until then the sitemap entries open nothing and `ValidateApp` warns for the Administration sitemap. The source AppModule.xml entries (section 6) cover the release import. The Administration app also gained an `entity` component row when it was republished (a PublishXml side effect, 18 to 19 rows; Matter Management already carries one); it cannot be removed through the API and was not copied to source.
B. **Roles lack privileges on the new tables.** A non-admin user gets 0x80040220 (no prvRead) on sprk_invoiceapproval, so "Invoices to review" fails for non-admins until a roles / privileges task grants Read on the new tables (and Append / AppendTo where lookups need it). Out of scope here ("do not change roles"). **Resolved by D-90: section 11.**
C. **sprk_amount is read-only for all billing-event lines**, not only Adjustment lines (section 3).
D. **Ribbon hide not live in dev** (section 6): needs the release import.
E. **secure_child_ribbon.js** needs a SpaarkeMaster redeploy (section 6).
F. **Export-SpaarkeMasterSource F12 guard fails in dev** on 6 identity components from another project (section 5). Owner of the identity work should add those to the package or exclude them.
G. **`pac` shim**: on this machine `C:\Users\RalphSchroeder\bin\pac` (a bash wrapper) shadows `pac.cmd` in pwsh. Workaround used: prepend `C:\Users\RalphSchroeder\AppData\Local\Microsoft\PowerAppsCLI` to PATH for the pwsh call.

## 8. Round trip (step 6)

- `pac solution pack --folder src/dataverse/solutions/SpaarkeMaster --packagetype Both --zipfile <temp>`: exit 0; `SpaarkeMaster-eb029.zip` 16 706 803 bytes and `SpaarkeMaster-eb029_managed.zip` 16 693 398 bytes (temp, deleted by the main session if wanted; not in the repo). No real import, no Assemble-SpaarkeMasterSolution.ps1, no version bump.
- `Import-SolutionScoped.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -SolutionUniqueName SpaarkeMaster -PlanOnly`: exit 0. The ParameterXml names all 11 new tables plus sprk_invoice, sprk_billingevent and sprk_timekeeper, 4 sitemaps and 4 app modules (including the two this task changed); `sprk_invoicelineitem` is gone. One warning: `Unmapped component(s): 14/18e5156a-89be-f111-aaaf-0022482913fc`. That is the alternate key `sprk_externalobjectiduniquekey` on `contact`, held by SpaarkeMaster and SpaarkeCore: identity-project work, the same family as the six completeness findings. PlanOnly reads the installed dev solution, so the warning does not depend on this task's source change. **Escalation trigger "unmapped component from another project" fired in the letter;** nothing was run to work around it and no eBilling item depends on it, so Steps 7-9 went on; the owner should confirm that is acceptable (it also affects any future release import of this source).
- `PlanOnly` does not report pending collateral; the entity-scoped publish in step 2 did that check (section 2).
- Re-run of Test-SolutionCompleteness: section 5.

## 9. Dev vs source column comparison (step 7)

Dev attribute names and types (read per table from `EntityDefinitions`) against the `<attribute>` entries of the repo `Entity.xml`. "sprk_ columns" counts custom columns without the primary key id (dev side) / with it (source side), so source is always dev + 1.

| Table | Dev sprk_ columns | Source sprk_ columns | In dev, not in source | In source, not in dev | Type mismatches |
|---|---|---|---|---|---|
| sprk_billingcode | 6 | 7 | none | primary key id only | none |
| sprk_billingcodeset | 4 | 5 | none | primary key id only | none |
| sprk_billingevent | 50 | 51 | none | primary key id only | none |
| sprk_billingexport | 10 | 11 | none | primary key id only | none |
| sprk_ebillingsetting | 17 | 18 | none | primary key id only | none |
| sprk_externalidentifier | 16 | 17 | none | primary key id only | none |
| sprk_invoice | 56 | 57 | none | primary key id only | none |
| sprk_invoiceapproval | 18 | 19 | none | primary key id only | none |
| sprk_invoicecorrection | 23 | 24 | none | primary key id only | none |
| sprk_invoicesubmission | 27 | 28 | none | primary key id only | none |
| sprk_matter | 58 | 59 | none | primary key id only | none |
| sprk_mattertype_ref | 3 | 4 | none | primary key id only | none |
| sprk_organization | 12 | 13 | none | primary key id only | none |
| sprk_policy | 16 | 17 | none | primary key id only | none |
| sprk_policyversion | 17 | 18 | none | primary key id only | none |
| sprk_practicearea_ref | 3 | 4 | none | primary key id only | none |
| sprk_project | 62 | 63 | none | primary key id only | none |
| sprk_ratecard | 8 | 9 | none | primary key id only | none |
| sprk_ratecardrate | 10 | 11 | none | primary key id only | none |
| sprk_ruleset | 9 | 10 | none | primary key id only | none |
| sprk_timekeeper | 6 | 7 | none | primary key id only | none |
| sprk_userprofile | 8 | 9 | none | primary key id only | none |
| sprk_workassignment | 42 | 43 | none | primary key id only | none |

Also covered by the same export diff: sprk_agreement (`sprk_billingapproverrole` present in dev and source). Forms, views, relationships, choices and sitemaps: after the sync the only remaining differences between a fresh dev export and the repo source are the unrelated items of section 6 and the hand-written ribbon and app-module entries.

## 10. Procedure for later tasks (053, 079, 112, 136, 153)

1. Apply the schema script for the task (dev only).
2. Add the new column names to `$Spec` in `scripts/ebilling/Deploy-EbillingFormsViews.ps1`; run it with `-WhatIf`, then for real (it publishes only the touched entities).
3. Export with `Export-SpaarkeMasterSource.ps1 -OutputFolder <temp>` (use the PATH workaround of finding G if `pac` fails), diff against `src/dataverse/solutions/SpaarkeMaster` ignoring line endings, copy only eBilling files and hunks (section 6 lists the usual mixed files).
4. `pac solution pack --packagetype Both` to a temp zip, `Import-SolutionScoped.ps1 -PlanOnly`, then `Test-SolutionCompleteness.ps1 -FailOnDrift:$false`; compare with section 5.

## 11. Role privileges (D-90)

Owner decision D-90 (2026-10-16, explicit owner approval of this role change): the table privileges that finding B (section 7) said were missing. Script: `scripts/ebilling/Deploy-EbillingRolePrivileges.ps1` (new, idempotent, `-WhatIf`, add-only). Dev `https://spaarkedev1.crm.dynamics.com`. Raw output: `C:\tmp\eb029r\d90-whatif.txt`, `d90-real1.txt`, `d90-real2.txt` (temp, not in the repo).

**Rule applied.** The script reads the role's own `sprk_invoice` privileges and mirrors them. Working tables (`sprk_invoiceapproval`, `sprk_invoicesubmission`, `sprk_invoicecorrection`, `sprk_externalidentifier`, `sprk_billingexport`): the same privilege set at the same depth. Configuration tables (`sprk_ebillingsetting`, `sprk_ratecard`, `sprk_ratecardrate`, `sprk_billingcodeset`, `sprk_billingcode`, `sprk_ruleset`, `sprk_timekeeper`): Read only.

**Step 0 - what the roles held on `sprk_invoice` before** (root role, business unit `06fbf21c-...`; the per-business-unit copies follow the root role):

| Role | `sprk_invoice` privileges | Action |
|---|---|---|
| Spaarke Core User (`75e1b627-2ab6-f111-aaad-0022482913fc`) | Create, Read, Write, Delete, Append, AppendTo, Assign, Share, all Deep (also Read Deep on `sprk_billingevent` and `sprk_timekeeper`) | mirrored |
| Spaarke Basic User (`11f93c04-ddf6-f011-8406-7c1e520aa4df`) | Read Deep, AppendTo Deep (also Read Deep on `sprk_billingevent` and `sprk_timekeeper`) | same mirror rule applied: it gets only Read and AppendTo |
| every other role (Secure Record Owner, admin roles, Console / Ontology / Office roles, ...) | not touched | - |

**Added (62 privileges, all in the first real run; none removed or lowered):**

| Role | Table | Privileges added (depth) |
|---|---|---|
| Core User | `sprk_invoiceapproval`, `sprk_invoicesubmission`, `sprk_invoicecorrection`, `sprk_externalidentifier`, `sprk_billingexport` | Create, Read, Write, Delete, Append, AppendTo, Assign, Share (Deep each; 8 x 5 = 40) |
| Core User | `sprk_ratecard`, `sprk_ratecardrate`, `sprk_ruleset` | Read (Deep) |
| Core User | `sprk_ebillingsetting`, `sprk_billingcodeset`, `sprk_billingcode` | Read (**Global**, see below) |
| Core User | `sprk_timekeeper` | nothing: Read Deep already held |
| Basic User | the five working tables | Read, AppendTo (Deep each; 10) |
| Basic User | `sprk_ratecard`, `sprk_ratecardrate`, `sprk_ruleset` | Read (Deep) |
| Basic User | `sprk_ebillingsetting`, `sprk_billingcodeset`, `sprk_billingcode` | Read (Global) |
| Basic User | `sprk_timekeeper` | nothing: Read Deep already held |

Core User 737 -> 783 privileges (+46), Basic User 655 -> 671 (+16).

**Decisions inside the rule (for the owner to confirm).**
1. Read on the three organization-owned configuration tables (`sprk_ebillingsetting`, `sprk_billingcodeset`, `sprk_billingcode`) is **Global**. An organization-owned table's privileges offer only Global depth (the metadata says Basic / Local / Deep are not allowed), so "the depth the role holds on `sprk_invoice`" (Deep) does not exist for them. Global read of a table that has no owner and is org-wide configuration is what Deep means for the user-owned ones.
2. **No configuration table received AppendTo.** AppendTo on a target table is needed only when a record a user writes carries a lookup to it; the script computes this from the metadata and none of the five working tables looks up any of the seven configuration tables (their lookups go to `sprk_invoice`, `sprk_document`, `sprk_matter`, `sprk_project`, `sprk_workassignment`, `sprk_organization`, `sprk_recordtype_ref`, `sprk_decisionrecord`, `contact`, `systemuser`, and to themselves).
3. Basic User holds `sprk_invoice` Read and AppendTo, so it got the same mirror (Read and AppendTo on the working tables, Read on the configuration tables); no Create / Write.

**Script runs (dev).**

| Run | Result |
|---|---|
| `-WhatIf` | 62 privileges listed as "WOULD ADD", 2 skipped (`sprk_timekeeper` Read held, both roles), nothing written |
| First real run | `added: 62`, `held / skipped: 2`; each role read back after the add (every added privilege present at its depth, no existing privilege lower than before); the 5 per-business-unit copies of each role hold the added privileges |
| Second real run | `nothing to add for Spaarke Core User`, `nothing to add for Spaarke Basic User`, `added: 0`, `held / skipped: 64` |

**Evidence: the roles' privileges on the 13 tables after the run** (the script's own read-back; `sprk_invoice` shown for reference):

```
Spaarke Core User
  sprk_invoice               Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep
  sprk_invoiceapproval       Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep
  sprk_invoicesubmission     Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep
  sprk_invoicecorrection     Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep
  sprk_externalidentifier    Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep
  sprk_billingexport         Create=Deep Read=Deep Write=Deep Delete=Deep Append=Deep AppendTo=Deep Assign=Deep Share=Deep
  sprk_ebillingsetting       Read=Global
  sprk_ratecard              Read=Deep
  sprk_ratecardrate          Read=Deep
  sprk_billingcodeset        Read=Global
  sprk_billingcode           Read=Global
  sprk_ruleset               Read=Deep
  sprk_timekeeper            Read=Deep
Spaarke Basic User
  sprk_invoice               Read=Deep AppendTo=Deep
  sprk_invoiceapproval       Read=Deep AppendTo=Deep
  sprk_invoicesubmission     Read=Deep AppendTo=Deep
  sprk_invoicecorrection     Read=Deep AppendTo=Deep
  sprk_externalidentifier    Read=Deep AppendTo=Deep
  sprk_billingexport         Read=Deep AppendTo=Deep
  sprk_ebillingsetting       Read=Global
  sprk_ratecard              Read=Deep
  sprk_ratecardrate          Read=Deep
  sprk_billingcodeset        Read=Global
  sprk_billingcode           Read=Global
  sprk_ruleset               Read=Deep
  sprk_timekeeper            Read=Deep
```

**Verification as non-admin users (impersonation, `MSCRMCallerID`).** Before the change this failed with 0x80040220 (section 4, finding B).
- "Invoices to review" (`0c7694d8-14c9-f111-a05a-7c1e520a989f`) now runs without error for Test User 1 (Core + Basic), Jessica Broyles (Core), the UAC child-business-unit test user and a Basic-User-only user (0 rows each: no invoices of theirs).
- All 12 new tables return HTTP 200 on a `$top=1` read for three Core-User identities (settings 1 row, code sets 1, codes 1, the rest empty).
- End to end as Test User 1 (temporary data, all deleted afterwards; 0 left): created a contact linked to himself, an In Review invoice, a Pending `sprk_invoiceapproval` for it (Create + Append / AppendTo on the new table) and associated himself as eligible approver. The view returned exactly that invoice (1 row); `sprk_invoiceapprovals` read back 1 row.
- Observation, not a defect of this change: when the same contact was created by the admin (owner in the parent business unit) instead of by the user, the view returned 0 rows for Test User 1, because the view's join to `contact` is subject to the user's Read depth on `contact` and the admin's contact is outside his business unit. Eligible-approver contacts of non-admin approvers must be readable by the approver (same business unit or shared); tasks 044 / 049 should create them with that in mind.

**Source.** `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Core User.xml` (+46 `RolePrivilege` lines) and `Spaarke Basic User.xml` (+16), taken from a fresh export (`Export-SpaarkeMasterSource.ps1 -OutputFolder C:\tmp\eb029r\exp`, same F12 failure on the 6 identity leaks as finding F; the unpack had finished). Exactly the added lines, in the export's order, no removals. Not taken from the export: `prvReadsprk_Policy` in Basic User (another project's change, as in section 6). The other 10 role files were not touched.

## Deviations

- The brief's "AddSolutionComponent for eBilling findings" was not needed (no eBilling finding).
- The tables could not be added to the apps through the API (finding A); the script prints the manual step instead of failing.
- Roles for the new tables were not touched by task 029 itself (finding B); D-90 did it afterwards: section 11.
- The ribbon hide is in source only (finding D).
- Inventory file patched by hand (eBilling hunks), not regenerated.

**Follow-up (main session, 2026-10-16).** These points are deliberately left as they are:
- No AppendTo on `sprk_billingcode` or `sprk_timekeeper` for the line lookups.
- No Write on `sprk_billingevent`.

Today users only read invoice lines, and the server writes them. Mirroring the full invoice access onto `sprk_billingevent` would have let users create and delete lines directly, so it was not applied. Task 100 carries the constraint: line-decision writes go through the server, or the role change becomes an owner decision first.

Contact visibility, for tasks 044 and 049: an approver whose Read on `contact` is business-unit scoped cannot see a contact created outside their business unit. The approval rows these tasks write must reference contacts the approver can read.
