# Task 080 - Procedure schema: script, dev checks and -WhatIf (steps 1-3 done; 4-12 wait)

Status 2026-10-21: **script authored and `-WhatIf` run (reads only). NOTHING was written to Dataverse.** Steps 4-9 and 10 (docs "live" wording), 11 and 12 wait for the gates in section 7. Branch `task/ebill-080-schema`, from `origin/work/ebilling-r1`.

Script: `scripts/ebilling/Deploy-EbillingProcedureSchema.ps1` (git mv of `Deploy-EbillingRuleSetScopeSchema.ps1`, rewritten on `EbillingSchema.psm1`). Test: `tests/scripts/Deploy-EbillingProcedureSchema.Tests.ps1` (Pester 3.4, 25 tests, no live call).

## 1. What changed from the old script

| Old item | Now |
|---|---|
| Pre-flight shape, S1 record lookups, S2 scope options, S7 field security + profile reuse + secure-then-grant-with-revert, S9 `sprk_createdbyperson` | Kept (S1 and S7 now cover the P5/P9 list; S9 on `sprk_ruleset` only, `sprk_ratecard` / `sprk_ratecardrate` dropped, model 12 / D-120) |
| S3 all business units, S4 business-unit N:N, S6 `sprk_ratecard.sprk_scope` + backfill (`-BackfillOnly`, `-SkipBackfill`), S8 Secure snapshot | Dropped (O-12, model 7.2) |
| Hand-typed three-table core list | Read at run time from `InvoiceFamilyCoreRecordGuard.cs` (the `FilingCores` initializer, line 125) by regex; exit 2 and nothing written if not exactly one declaration with a non-empty list of distinct `sprk_` names. The script text names no core table at all (a test pins that). Each table's schema name and display name come from its metadata. |
| New | Dev-URL-only guard (exit 2 for any other URL); lookup plan printed in pre-flight; pre-flight refuses any planned name that exists with another meaning (table ownership / primary name, column type, lookup target, delete behaviour, relationship name, global choice values); verify covers every acceptance criterion that can be read by metadata |

## 2. Everything the script creates (for ontology-r1's schema review)

Relationship names are this task's choice (the model names none): new-table lookups use `{table}_{column}_{target}` with the `sprk_` prefix dropped from column and target (the `Deploy-EbillingSubmissionSchema.ps1` shape); the rule-set record lookups keep the old script's `{core}_ruleset_{suffix}`; assignment record lookups use `{core}_procedureassignment_{suffix}`; `sprk_createdbyperson` uses `sprk_systemuser_{table}_createdbyperson`.

### 2.1 Global choices (P1) - values from 100000000 in the order listed

| Name | Display | Options |
|---|---|---|
| `sprk_proceduretype` | Procedure Type | 100000000 Guideline, 100000001 Rate card, 100000002 Billing instructions, 100000003 Internal Standard, 100000004 Other |
| `sprk_procedureappliesto` | Procedure Applies To | 100000000 All records, 100000001 Firm, 100000002 Assigned records only |
| `sprk_procedurestatus` | Procedure Status | 100000000 Active, 100000001 Retired |
| `sprk_procedureversionstatus` | Procedure Version Status | 100000000 Draft, 100000001 Active, 100000002 Superseded, 100000003 Retired |
| `sprk_publishstate` | Publish State | 100000000 Draft, 100000001 Published |

An existing set with other values stops the script. `sprk_policydomain` (Billing 100000000, Ontology 100000001, Docketing 100000002) is reused, not touched.

### 2.2 Tables (P2, P3, P4, P10) - all user-owned, primary name `sprk_name` (text 200, ApplicationRequired), table auditing on

| Table | Display | Columns |
|---|---|---|
| `sprk_procedure` | Procedure / Procedures | `sprk_firmfacingname` text 200; `sprk_area` choice (global `sprk_policydomain`); `sprk_proceduretype` choice (global); `sprk_appliesto` choice (global `sprk_procedureappliesto`); `sprk_status` choice (global `sprk_procedurestatus`); `sprk_notes` memo 100000; lookups in 2.3 |
| `sprk_procedureversion` | Procedure Version / Procedure Versions | `sprk_versionnumber` whole number 1..2147483647; `sprk_effectivefrom`, `sprk_effectiveto` DateOnly (behaviour DateOnly); `sprk_status` choice (global `sprk_procedureversionstatus`); `sprk_notconverted` two options, default No (task 116); alternate key `sprk_procedure_versionnumber` on (`sprk_procedure`, `sprk_versionnumber`), waited until Active; lookups in 2.3 |
| `sprk_procedureassignment` | Procedure Assignment / Procedure Assignments | `sprk_mode` LOCAL choice (100000000 Follow latest, 100000001 Pinned, 100000002 Excluded); `sprk_reason` memo 2000; lookups in 2.3 |

### 2.3 Lookups (30; every one optional, required level None) with relationship and delete behaviour

| Table.column | Target | Relationship | Delete |
|---|---|---|---|
| `sprk_procedure.sprk_firm` | `sprk_organization` | `sprk_procedure_firm_organization` | **Restrict** (proposal, see 6.1) |
| `sprk_procedure.sprk_issuedby` | `sprk_organization` | `sprk_procedure_issuedby_organization` | RemoveLink |
| `sprk_procedure.sprk_externalcontact` | `contact` | `sprk_procedure_externalcontact_contact` | RemoveLink |
| `sprk_procedure.sprk_internalcontact` | `contact` | `sprk_procedure_internalcontact_contact` | RemoveLink |
| `sprk_procedure.sprk_createdbyperson` | `systemuser` | `sprk_systemuser_sprk_procedure_createdbyperson` | RemoveLink |
| `sprk_procedureversion.sprk_procedure` | `sprk_procedure` | `sprk_procedureversion_procedure_procedure` | **Parental** (Cascade delete, assign, share, unshare, reparent) |
| `sprk_procedureversion.sprk_previousversion` | `sprk_procedureversion` | `sprk_procedureversion_previousversion_procedureversion` | RemoveLink |
| `sprk_procedureversion.sprk_createdbyperson` | `systemuser` | `sprk_systemuser_sprk_procedureversion_createdbyperson` | RemoveLink |
| `sprk_procedureassignment.sprk_matter` | `sprk_matter` | `sprk_matter_procedureassignment_matter` | Cascade |
| `sprk_procedureassignment.sprk_project` | `sprk_project` | `sprk_project_procedureassignment_project` | Cascade |
| `sprk_procedureassignment.sprk_workassignment` | `sprk_workassignment` | `sprk_workassignment_procedureassignment_workassignment` | Cascade |
| `sprk_procedureassignment.sprk_procedure` | `sprk_procedure` | `sprk_procedureassignment_procedure_procedure` | **Restrict** |
| `sprk_procedureassignment.sprk_pinnedversion` | `sprk_procedureversion` | `sprk_procedureassignment_pinnedversion_procedureversion` | **Restrict** |
| `sprk_procedureassignment.sprk_excludedpolicy` | `sprk_policy` | `sprk_procedureassignment_excludedpolicy_policy` | Cascade |
| `sprk_procedureassignment.sprk_firm` | `sprk_organization` | `sprk_procedureassignment_firm_organization` | **Restrict** (proposal, see 6.1) |
| `sprk_procedureassignment.sprk_createdbyperson` | `systemuser` | `sprk_systemuser_sprk_procedureassignment_createdbyperson` | RemoveLink |
| `sprk_ruleset.sprk_matter` | `sprk_matter` | `sprk_matter_ruleset_matter` | RemoveLink |
| `sprk_ruleset.sprk_project` | `sprk_project` | `sprk_project_ruleset_project` | RemoveLink |
| `sprk_ruleset.sprk_workassignment` | `sprk_workassignment` | `sprk_workassignment_ruleset_workassignment` | RemoveLink |
| `sprk_ruleset.sprk_procedure` | `sprk_procedure` | `sprk_ruleset_procedure_procedure` | **Restrict** |
| `sprk_ruleset.sprk_createdbyperson` | `systemuser` | `sprk_systemuser_sprk_ruleset_createdbyperson` | RemoveLink |
| `sprk_policyversion.sprk_procedureversion` | `sprk_procedureversion` | `sprk_policyversion_procedureversion_procedureversion` | **Restrict** |
| `sprk_policyversion.sprk_basedonversion` | `sprk_policyversion` | `sprk_policyversion_basedonversion_policyversion` | RemoveLink |
| `sprk_ratecard.sprk_procedureversion` | `sprk_procedureversion` | `sprk_ratecard_procedureversion_procedureversion` | **Restrict** |
| `{sprk_matter, sprk_project, sprk_workassignment}.sprk_billingfirm` (3 rows) | `sprk_organization` | `{table}_billingfirm_organization` | RemoveLink |
| `{sprk_matter, sprk_project, sprk_workassignment}.sprk_billingresponsible` (3 rows) | `sprk_organization` | `{table}_billingresponsible_organization` | RemoveLink |

No lookup whose target is a core table is Restrict (a test pins it, with a planted-Restrict control). An existing lookup with another delete behaviour is never changed: the pre-flight stops (exit 2).

### 2.4 Columns on existing tables (non-lookup)

| Table.column | Change |
|---|---|
| `sprk_ruleset.sprk_scope` | InsertOptionValue 100000002 `Record` and 100000003 `Firm record` (existing 100000000 `Client default`, 100000001 `Firm` verified, never changed; stops if a value or label exists with another meaning) |
| `sprk_policyversion.sprk_publishstate` | New choice bound to global `sprk_publishstate`, optional, no default |

### 2.5 Field security (P9) - 13 columns, IsSecured true

Reader profile ("Spaarke BFF-Managed Field Readers"): Read (4), Create 0, Update 0. Writer profile ("Spaarke BFF-Managed Field Writers"): Read 4, Create 4, Update 4. Permission rows only; no profile member is ever added or removed (the pre-flight stops if the reader profile is missing from a default team or the writer holds anyone but the BFF application users).

`sprk_procedure`: `sprk_appliesto`, `sprk_firm`, `sprk_status`. `sprk_procedureversion`: `sprk_effectivefrom`, `sprk_effectiveto`, `sprk_status`, `sprk_notconverted`. `sprk_ruleset`: `sprk_scope`, `sprk_firm`, `sprk_procedure`, and the three record lookups. `sprk_createdbyperson` is created unsecured (081 secures it).

### 2.6 Other

- Auditing (table level) on the three new tables.
- Publish (only when something changed, never `PublishAllXml`): the three new tables, `sprk_ruleset`, `sprk_policyversion`, `sprk_ratecard`, `sprk_policy`, `sprk_organization`, `contact`, `systemuser`, the core tables, and the five global choices.
- Never created: banner column, `sprk_isexceptions`, business-unit column or N:N, `sprk_ratecard.sprk_scope`, `sprk_storedfile`, any role privilege, form or view. `sprk_ruleset.sprk_sourcedocument` and the `sprk_ratecard` core lookups are untouched.

## 3. Read-only dev checks (2026-10-21, step 2)

Run with GET calls only against `https://spaarkedev1.crm.dynamics.com`.

| Check | Result |
|---|---|
| Five global choices | all absent |
| `sprk_policydomain` | exists: 100000000 Billing, 100000001 Ontology, 100000002 Docketing |
| `sprk_procedure`, `sprk_procedureversion`, `sprk_procedureassignment`, `sprk_storedfile` | all absent |
| Every planned column and lookup (all 30 lookups, `sprk_publishstate`, the three tables' columns) | all absent; no relationship name taken |
| `sprk_ruleset.sprk_scope` options | exactly 100000000 Client default, 100000001 Firm (local choice) |
| `sprk_ratecard.sprk_scope`, `sprk_ruleset.sprk_isexceptions`, `sprk_ruleset.sprk_allbusinessunits` | absent (the old script never ran) |
| `sprk_ruleset.sprk_sourcedocument` | exists (lookup), untouched |
| `sprk_assignedlawfirm1` / `sprk_assignedlawfirm2` | exist as lookups on `sprk_matter`, `sprk_project` and `sprk_workassignment` |
| Rows | `sprk_ruleset` 0, `sprk_ratecard` 0, `sprk_policyversion` 7 (get no `sprk_publishstate`; dev data is recreated, O-13) |
| Billing tab on the main forms | `Matter main form`: yes (tab BILLING). `Project main form`: yes (BILLING). **`Work Assignment main form`: no** (tabs Overview, Events, Search). The secondary "Information" forms have one General tab. No edit made; the work-assignment gap is task 079's. |
| Reader / writer profiles | reader on all 6 business-unit default teams; writer holds `# mi-bff-api-dev` and `SDAP-BFF-SPE-API` and nobody else |
| Role `External Collaborator` | **does not exist on dev** under that name (roles read: Office Collaborator, Spaarke Basic User, Core User, Console User, Internal Contact User (Workforce), ...). The "no privilege on the three new tables" acceptance check therefore cannot be proven against a role of that name; see 6.6. |

## 4. `-WhatIf` run (dev, exit 0, reads only)

Command: `pwsh scripts/ebilling/Deploy-EbillingProcedureSchema.ps1 -WhatIf`, wrapped in a tripwire that logs every HTTP call and throws on anything but GET: **281 requests, 0 non-GET**. Result: created 81 (5 global choices, 3 tables, 44 column and lookup items, 2 `sprk_scope` options, 1 alternate key, 26 field-permission rows), changed 13 (the 13 `secure` items; the three table-audit lines print as WOULD SET auditing), skipped 0. Nothing in it is outside the P1-P10 list and the task-116 `sprk_notconverted` item.

```text
eBilling task 080 - Procedure schema (P1-P10)
Environment: https://spaarkedev1.crm.dynamics.com   Solution: SpaarkeMaster   Mode: WHATIF (no writes)
Profiles   : reader 'Spaarke BFF-Managed Field Readers', writer 'Spaarke BFF-Managed Field Writers' (members are never changed on an existing profile)

0. Pre-flight (read-only)
  FilingCores (parsed from InvoiceFamilyCoreRecordGuard.cs): sprk_matter, sprk_project, sprk_workassignment
    core table sprk_matter: schema sprk_Matter, display 'Matter'
    core table sprk_project: schema sprk_Project, display 'Project'
    core table sprk_workassignment: schema sprk_WorkAssignment, display 'Work Assignment'
  global choice sprk_policydomain: 100000000=Billing, 100000001=Ontology, 100000002=Docketing (reused, not changed)
  global choice sprk_proceduretype: absent
  global choice sprk_procedureappliesto: absent
  global choice sprk_procedurestatus: absent
  global choice sprk_procedureversionstatus: absent
  global choice sprk_publishstate: absent
  sprk_ruleset.sprk_scope options now: 100000000=Client default, 100000001=Firm
  no existing item conflicts
  field security profiles:
  OK       reader profile 'Spaarke BFF-Managed Field Readers' is on default team 'Secure Record' (BU 'Secure Record')
  OK       reader profile 'Spaarke BFF-Managed Field Readers' is on default team 'Spaarke Business Unit 1' (BU 'Spaarke Business Unit 1')
  OK       reader profile 'Spaarke BFF-Managed Field Readers' is on default team 'Spaarke Demo' (BU 'Spaarke Demo')
  OK       reader profile 'Spaarke BFF-Managed Field Readers' is on default team 'Spaarke Dev 1' (BU 'Spaarke Dev 1')
  OK       reader profile 'Spaarke BFF-Managed Field Readers' is on default team 'Spaarke Test 1' (BU 'Spaarke Test 1')
  OK       reader profile 'Spaarke BFF-Managed Field Readers' is on default team 'Spaarke' (BU 'Spaarke')
  OK       writer profile 'Spaarke BFF-Managed Field Writers' holds BFF application user '# mi-bff-api-dev'
  OK       writer profile 'Spaarke BFF-Managed Field Writers' holds BFF application user 'SDAP-BFF-SPE-API'
  lookup plan (30 lookups): table.column -> target | relationship | delete behaviour
    sprk_procedure.sprk_firm -> sprk_organization | sprk_procedure_firm_organization | Restrict
    sprk_procedure.sprk_issuedby -> sprk_organization | sprk_procedure_issuedby_organization | RemoveLink
    sprk_procedure.sprk_externalcontact -> contact | sprk_procedure_externalcontact_contact | RemoveLink
    sprk_procedure.sprk_internalcontact -> contact | sprk_procedure_internalcontact_contact | RemoveLink
    sprk_procedure.sprk_createdbyperson -> systemuser | sprk_systemuser_sprk_procedure_createdbyperson | RemoveLink
    sprk_procedureversion.sprk_procedure -> sprk_procedure | sprk_procedureversion_procedure_procedure | Cascade (parental)
    sprk_procedureversion.sprk_previousversion -> sprk_procedureversion | sprk_procedureversion_previousversion_procedureversion | RemoveLink
    sprk_procedureversion.sprk_createdbyperson -> systemuser | sprk_systemuser_sprk_procedureversion_createdbyperson | RemoveLink
    sprk_procedureassignment.sprk_matter -> sprk_matter | sprk_matter_procedureassignment_matter | Cascade
    sprk_procedureassignment.sprk_project -> sprk_project | sprk_project_procedureassignment_project | Cascade
    sprk_procedureassignment.sprk_workassignment -> sprk_workassignment | sprk_workassignment_procedureassignment_workassignment | Cascade
    sprk_procedureassignment.sprk_procedure -> sprk_procedure | sprk_procedureassignment_procedure_procedure | Restrict
    sprk_procedureassignment.sprk_pinnedversion -> sprk_procedureversion | sprk_procedureassignment_pinnedversion_procedureversion | Restrict
    sprk_procedureassignment.sprk_excludedpolicy -> sprk_policy | sprk_procedureassignment_excludedpolicy_policy | Cascade
    sprk_procedureassignment.sprk_firm -> sprk_organization | sprk_procedureassignment_firm_organization | Restrict
    sprk_procedureassignment.sprk_createdbyperson -> systemuser | sprk_systemuser_sprk_procedureassignment_createdbyperson | RemoveLink
    sprk_ruleset.sprk_matter -> sprk_matter | sprk_matter_ruleset_matter | RemoveLink
    sprk_ruleset.sprk_project -> sprk_project | sprk_project_ruleset_project | RemoveLink
    sprk_ruleset.sprk_workassignment -> sprk_workassignment | sprk_workassignment_ruleset_workassignment | RemoveLink
    sprk_ruleset.sprk_procedure -> sprk_procedure | sprk_ruleset_procedure_procedure | Restrict
    sprk_ruleset.sprk_createdbyperson -> systemuser | sprk_systemuser_sprk_ruleset_createdbyperson | RemoveLink
    sprk_policyversion.sprk_procedureversion -> sprk_procedureversion | sprk_policyversion_procedureversion_procedureversion | Restrict
    sprk_policyversion.sprk_basedonversion -> sprk_policyversion | sprk_policyversion_basedonversion_policyversion | RemoveLink
    sprk_ratecard.sprk_procedureversion -> sprk_procedureversion | sprk_ratecard_procedureversion_procedureversion | Restrict
    sprk_matter.sprk_billingfirm -> sprk_organization | sprk_matter_billingfirm_organization | RemoveLink
    sprk_matter.sprk_billingresponsible -> sprk_organization | sprk_matter_billingresponsible_organization | RemoveLink
    sprk_project.sprk_billingfirm -> sprk_organization | sprk_project_billingfirm_organization | RemoveLink
    sprk_project.sprk_billingresponsible -> sprk_organization | sprk_project_billingresponsible_organization | RemoveLink
    sprk_workassignment.sprk_billingfirm -> sprk_organization | sprk_workassignment_billingfirm_organization | RemoveLink
    sprk_workassignment.sprk_billingresponsible -> sprk_organization | sprk_workassignment_billingresponsible_organization | RemoveLink
1. P1 global choices (values from 100000000)
  WOULD CREATE global option set sprk_proceduretype (100000000=Guideline, 100000001=Rate card, 100000002=Billing instructions, 100000003=Internal Standard, 100000004=Other)
  WOULD CREATE global option set sprk_procedureappliesto (100000000=All records, 100000001=Firm, 100000002=Assigned records only)
  WOULD CREATE global option set sprk_procedurestatus (100000000=Active, 100000001=Retired)
  WOULD CREATE global option set sprk_procedureversionstatus (100000000=Draft, 100000001=Active, 100000002=Superseded, 100000003=Retired)
  WOULD CREATE global option set sprk_publishstate (100000000=Draft, 100000001=Published)
2. P2 table sprk_procedure (user-owned, audited)
  WOULD CREATE table sprk_procedure (user-owned)
  WOULD SET auditing on sprk_procedure
  WOULD CREATE sprk_firmfacingname (String)
  WOULD CREATE sprk_area (Picklist)
  WOULD CREATE sprk_proceduretype (Picklist)
  WOULD CREATE sprk_appliesto (Picklist)
  WOULD CREATE sprk_firm (Lookup -> sprk_organization)
  WOULD CREATE sprk_issuedby (Lookup -> sprk_organization)
  WOULD CREATE sprk_externalcontact (Lookup -> contact)
  WOULD CREATE sprk_internalcontact (Lookup -> contact)
  WOULD CREATE sprk_createdbyperson (Lookup -> systemuser)
  WOULD CREATE sprk_status (Picklist)
  WOULD CREATE sprk_notes (Memo)
3. P3 table sprk_procedureversion (user-owned, audited, parental to sprk_procedure)
  WOULD CREATE table sprk_procedureversion (user-owned)
  WOULD SET auditing on sprk_procedureversion
  WOULD CREATE sprk_procedure (Lookup -> sprk_procedure, parental)
  WOULD CREATE sprk_versionnumber (Integer)
   alternate key (sprk_procedure, sprk_versionnumber)
  WOULD CREATE alternate key sprk_procedure_versionnumber on (sprk_procedure,sprk_versionnumber)
  WOULD CREATE sprk_previousversion (Lookup -> sprk_procedureversion)
  WOULD CREATE sprk_createdbyperson (Lookup -> systemuser)
  WOULD CREATE sprk_effectivefrom (DateTime)
  WOULD CREATE sprk_effectiveto (DateTime)
  WOULD CREATE sprk_status (Picklist)
  WOULD CREATE sprk_notconverted (Boolean)
4. P4 table sprk_procedureassignment (user-owned, audited)
  WOULD CREATE table sprk_procedureassignment (user-owned)
  WOULD SET auditing on sprk_procedureassignment
  WOULD CREATE sprk_matter (Lookup -> sprk_matter)
  WOULD CREATE sprk_project (Lookup -> sprk_project)
  WOULD CREATE sprk_workassignment (Lookup -> sprk_workassignment)
  WOULD CREATE sprk_mode (Picklist)
  WOULD CREATE sprk_procedure (Lookup -> sprk_procedure)
  WOULD CREATE sprk_pinnedversion (Lookup -> sprk_procedureversion)
  WOULD CREATE sprk_excludedpolicy (Lookup -> sprk_policy)
  WOULD CREATE sprk_firm (Lookup -> sprk_organization)
  WOULD CREATE sprk_createdbyperson (Lookup -> systemuser)
  WOULD CREATE sprk_reason (Memo)
5. P5 sprk_ruleset record lookups, scope options, Procedure link, creator person
  WOULD CREATE sprk_matter (Lookup -> sprk_matter)
  WOULD CREATE sprk_project (Lookup -> sprk_project)
  WOULD CREATE sprk_workassignment (Lookup -> sprk_workassignment)
  WOULD CREATE sprk_procedure (Lookup -> sprk_procedure)
  WOULD CREATE sprk_createdbyperson (Lookup -> systemuser)
   sprk_ruleset.sprk_scope options: Record = 100000002, Firm record = 100000003
  WOULD INSERT 100000002 Record
  WOULD INSERT 100000003 Firm record
6. P6 sprk_policyversion : sprk_procedureversion, sprk_publishstate, sprk_basedonversion
  WOULD CREATE sprk_procedureversion (Lookup -> sprk_procedureversion)
  WOULD CREATE sprk_basedonversion (Lookup -> sprk_policyversion)
  WOULD CREATE sprk_publishstate (Picklist)
7. P7 sprk_ratecard : sprk_procedureversion
  WOULD CREATE sprk_procedureversion (Lookup -> sprk_procedureversion)
8. P8 sprk_billingfirm and sprk_billingresponsible on every FilingCores table (RemoveLink, optional, confer no access)
  WOULD CREATE sprk_billingfirm (Lookup -> sprk_organization)
  WOULD CREATE sprk_billingresponsible (Lookup -> sprk_organization)
  WOULD CREATE sprk_billingfirm (Lookup -> sprk_organization)
  WOULD CREATE sprk_billingresponsible (Lookup -> sprk_organization)
  WOULD CREATE sprk_billingfirm (Lookup -> sprk_organization)
  WOULD CREATE sprk_billingresponsible (Lookup -> sprk_organization)
9. P9 field security (reader: Read for every user; writer: BFF application user(s) only)
  WOULD SECURE sprk_procedure.sprk_appliesto after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_procedure.sprk_firm after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_procedure.sprk_status after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_procedureversion.sprk_effectivefrom after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_procedureversion.sprk_effectiveto after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_procedureversion.sprk_status after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_procedureversion.sprk_notconverted after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_ruleset.sprk_scope
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_scope (read=4 create=0 update=0)
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_scope (read=4 create=4 update=4)
  WOULD SECURE sprk_ruleset.sprk_firm
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_firm (read=4 create=0 update=0)
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_firm (read=4 create=4 update=4)
  WOULD SECURE sprk_ruleset.sprk_procedure after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_ruleset.sprk_matter after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_ruleset.sprk_project after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_ruleset.sprk_workassignment after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_ruleset.sprk_scope
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_ruleset.sprk_firm
Publish
  WhatIf: publish skipped

================ Summary ================
created : 81
    + optionset sprk_proceduretype
    + optionset sprk_procedureappliesto
    + optionset sprk_procedurestatus
    + optionset sprk_procedureversionstatus
    + optionset sprk_publishstate
    + table sprk_procedure
    + sprk_procedure.sprk_firmfacingname
    + sprk_procedure.sprk_area
    + sprk_procedure.sprk_proceduretype
    + sprk_procedure.sprk_appliesto
    + sprk_procedure.sprk_firm
    + sprk_procedure.sprk_issuedby
    + sprk_procedure.sprk_externalcontact
    + sprk_procedure.sprk_internalcontact
    + sprk_procedure.sprk_createdbyperson
    + sprk_procedure.sprk_status
    + sprk_procedure.sprk_notes
    + table sprk_procedureversion
    + sprk_procedureversion.sprk_procedure
    + sprk_procedureversion.sprk_versionnumber
    + key sprk_procedure_versionnumber
    + sprk_procedureversion.sprk_previousversion
    + sprk_procedureversion.sprk_createdbyperson
    + sprk_procedureversion.sprk_effectivefrom
    + sprk_procedureversion.sprk_effectiveto
    + sprk_procedureversion.sprk_status
    + sprk_procedureversion.sprk_notconverted
    + table sprk_procedureassignment
    + sprk_procedureassignment.sprk_matter
    + sprk_procedureassignment.sprk_project
    + sprk_procedureassignment.sprk_workassignment
    + sprk_procedureassignment.sprk_mode
    + sprk_procedureassignment.sprk_procedure
    + sprk_procedureassignment.sprk_pinnedversion
    + sprk_procedureassignment.sprk_excludedpolicy
    + sprk_procedureassignment.sprk_firm
    + sprk_procedureassignment.sprk_createdbyperson
    + sprk_procedureassignment.sprk_reason
    + sprk_ruleset.sprk_matter
    + sprk_ruleset.sprk_project
    + sprk_ruleset.sprk_workassignment
    + sprk_ruleset.sprk_procedure
    + sprk_ruleset.sprk_createdbyperson
    + sprk_ruleset.sprk_scope=100000002
    + sprk_ruleset.sprk_scope=100000003
    + sprk_policyversion.sprk_procedureversion
    + sprk_policyversion.sprk_basedonversion
    + sprk_policyversion.sprk_publishstate
    + sprk_ratecard.sprk_procedureversion
    + sprk_matter.sprk_billingfirm
    + sprk_matter.sprk_billingresponsible
    + sprk_project.sprk_billingfirm
    + sprk_project.sprk_billingresponsible
    + sprk_workassignment.sprk_billingfirm
    + sprk_workassignment.sprk_billingresponsible
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_appliesto
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_appliesto
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_firm
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_firm
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_status
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_status
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedureversion.sprk_effectivefrom
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedureversion.sprk_effectivefrom
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedureversion.sprk_effectiveto
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedureversion.sprk_effectiveto
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedureversion.sprk_status
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedureversion.sprk_status
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedureversion.sprk_notconverted
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedureversion.sprk_notconverted
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_scope
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_scope
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_firm
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_firm
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_procedure
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_procedure
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_matter
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_matter
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_project
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_project
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_workassignment
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_workassignment
changed : 13
    ~ secure sprk_procedure.sprk_appliesto
    ~ secure sprk_procedure.sprk_firm
    ~ secure sprk_procedure.sprk_status
    ~ secure sprk_procedureversion.sprk_effectivefrom
    ~ secure sprk_procedureversion.sprk_effectiveto
    ~ secure sprk_procedureversion.sprk_status
    ~ secure sprk_procedureversion.sprk_notconverted
    ~ secure sprk_ruleset.sprk_scope
    ~ secure sprk_ruleset.sprk_firm
    ~ secure sprk_ruleset.sprk_procedure
    ~ secure sprk_ruleset.sprk_matter
    ~ secure sprk_ruleset.sprk_project
    ~ secure sprk_ruleset.sprk_workassignment
skipped : 0
WhatIf run: nothing was written.
EXIT CODE: 0
```

## 5. Acceptance criteria: met now vs needs the live run

| Criterion | State |
|---|---|
| Notes record before-notices, uac-r2's answer, ontology-r1's PASS, owner's OK, with dates, before the run | **Pending** (section 7); notice texts are ready in 8 |
| Five global choices exactly as listed; `sprk_area` bound to `sprk_policydomain` | Script + `-WhatIf` done; **needs the live run** (the script's verify checks it) |
| Three tables user-owned and audited, listed columns, parental, key Active, DateOnly | Same |
| Each lookup reads its delete behaviour; no lookup to a core table reads Restrict | Plan printed and tested offline (the Pester test pins it); live read by the verify after the run |
| `sprk_scope` has exactly the four values; record lookups for every FilingCores table and no other | Same (verify counts one `sprk_ruleset` lookup per core table) |
| Every P9 column IsSecured with the reader Read-only row and writer Create/Update row; no member changed | Same; the pre-flight (live, read-only) already proves both profiles are in the expected shape |
| `sprk_billingfirm` / `sprk_billingresponsible` on every FilingCores table and no other | Same (verify reads the relationships of `sprk_organization` by referencing attribute) |
| Negatives: no banner, `sprk_isexceptions`, BU column / N:N, `sprk_ratecard.sprk_scope`, `sprk_storedfile`; `sprk_sourcedocument` and the ratecard core lookups unchanged; External Collaborator has no privilege; `git diff --stat src/` empty | Script creates none of them (a test pins this); dev has none today (3); `git diff --stat src/` is empty on this branch; the role check needs a role name (6.6) |
| Second run reports every item skipped and writes nothing | **Needs the live run** |
| After-notices recorded; four docs updated as in step 10 | Docs drafted now with a "not yet run on dev" banner (`docs/data-model/sprk_ruleset.md`, `sprk_ratecard.md`, `sprk_matter-related-tables.md`, `sprk_workassignment.md`); remove the banner after the run; after-notices pending |

## 6. Items for the review and deviations

6.1 **Restrict on the two firm lookups** (`sprk_procedure.sprk_firm`, `sprk_procedureassignment.sprk_firm`) is this task's proposal (POML): a nulled firm would widen a firm-only Procedure, or an override, to every firm. Raise it in both before-notices.

6.2 **Alternate key columns are not SystemRequired.** The listed items name an alternate key but no required level. `Deploy-EbillingSubmissionSchema.ps1` (step 4b) raised both key columns to SystemRequired so a null cannot slip past the key. I did not add it, because it is an item the task does not list (and `-WhatIf` showing an unlisted item is an escalation trigger). Proposal for ontology-r1: `sprk_procedureversion.sprk_procedure` and `sprk_versionnumber` SystemRequired (the table is new and empty, so the platform refuse-null is safe); one line each with `Set-LookupRequiredLevel` / `Set-ColumnRequiredLevel`. Awaiting a yes.

6.3 **My choices where the task is silent**: relationship names (section 2); `sprk_name` length 200 on all three tables; `sprk_notes` memo 100000, `sprk_reason` memo 2000; `sprk_versionnumber` range 1..2147483647; display names of the five global choices and of the new columns; no default on any choice.

6.4 **Column-level auditing**: table-level `IsAuditEnabled` is set (P10). New columns are created with the Dataverse default; the script does not force per-column auditing (the module's `Add-Lookup` cannot, and `Deploy-EbillingSettingsSchema.ps1` needed a follow-up PUT per lookup). Say so in the review if per-column auditing is wanted.

6.5 **Publish list includes `sprk_policy`, `sprk_organization`, `contact` and `systemuser`** (the referenced ends of the new relationships) and the five global choices; the old script published `businessunit` the same way. Publishing a system table is a first for these scripts.

6.6 **No `External Collaborator` role on dev.** The model (5.2, K-1) and the POML say the External Collaborator role holds no privilege on the three new tables. The verify's check reads roles by that name; on dev it finds none and prints a loud warning instead of a pass. Nothing in this script grants any privilege (role rows are 081 / 089). Ask uac-r2 which role(s) flagged-external users hold on dev and add the name to the check.

6.7 **Highest-risk live step**: securing the picklists (`sprk_ruleset.sprk_scope`, and the four new global-choice columns) with a typed GET-then-PUT. The old script's fallback (retry once with `$expand=OptionSet`) now also covers the Boolean `sprk_notconverted`. A failed grant reverts the column to not secured and publishes. `sprk_ruleset.sprk_scope` and `sprk_firm` hold no data on dev (0 rule sets), so no reader is masked while they are secured.

6.8 **Old script references**: `projects/ebilling-r1/notes/080-ruleset-scope-schema.md` and `notes/rule-set-scope-design.md` are history and still name the old file; `projects/spaarke-ontology-platform-r1/current-task.md` also names it (not mine to edit). `TASK-INDEX.md`, the POML status and `current-task.md` were not touched (this session is authoring only).

6.9 **Tests**: the script is new, so the tests cannot fail "before" in the usual sense (the functions under test did not exist). The Pester file pins: both initializer shapes; refusal of a missing file, no declaration, a commented-out declaration, two declarations, an empty list, a non-`sprk_` name and a duplicate; a mere use of `FilingCores` is not a declaration; one record lookup / typed lookup / billing column pair per core table; a fourth core table adds its lookups with no script change; no Restrict lookup to a core table (with a planted control that the scan does fire on); the script text names no core table.

## 7. Gates before the real run (step 4-9) - all pending

| Gate | State |
|---|---|
| Before-notice to ontology-r1 (item list, delete behaviours, `-WhatIf` output) | not sent |
| Before-notice to uac-r2 (same + the profile-reuse question + confirm the dropped `sprk_createdbyperson` on `sprk_ratecard` / `sprk_ratecardrate`) | not sent; uac-r2's answer pending |
| ontology-r1 full schema-review PASS (fix every finding, re-run `-WhatIf`) | not started |
| Owner's OK for the dev run, shown the `-WhatIf` output | not asked |
| Real run, second run, verify, after-notices, docs banner removal, TASK-INDEX | not done; **088's real run must come after this one** (its Stored File has a parent lookup to `sprk_procedureversion`) |

Escalation triggers read: none fired during authoring (no planned name exists with another meaning; `FilingCores` parsed; no existing column would be deleted or changed; `-WhatIf` shows only listed items). Triggers 6.2 and 6.6 are raised above as review points, not as blockers of the script.

## 8. Notice texts (ready to send; not sent)

**To ontology-r1 (full schema review).** Task 080 script `scripts/ebilling/Deploy-EbillingProcedureSchema.ps1` on branch `task/ebill-080-schema`. It creates 5 global choices, 3 user-owned audited tables (`sprk_procedure`, `sprk_procedureversion` parental with alternate key (procedure, version number), `sprk_procedureassignment`), the `Record` 100000002 / `Firm record` 100000003 scope options and record lookups (one per `FilingCores` table, RemoveLink) plus `sprk_procedure` on `sprk_ruleset`, `sprk_procedureversion` / `sprk_publishstate` / `sprk_basedonversion` on `sprk_policyversion`, `sprk_procedureversion` on `sprk_ratecard`, `sprk_billingfirm` / `sprk_billingresponsible` on every core table, and field security on 13 BFF-only columns. Tables in `notes/080-procedure-schema.md` section 2; `-WhatIf` output in section 4. Please review in full. Points to decide: (1) Restrict on the two firm lookups (6.1); (2) SystemRequired on the version table's key columns (6.2); (3) relationship names (section 2); (4) `sprk_publishstate` name and values confirmed in this review (model 7.2).

**To uac-r2.** Same script and `-WhatIf`. Questions: (1) may the script add permission rows for the 13 columns to your "Spaarke BFF-Managed Field Readers / Writers" pair (rows only; it never changes members and stops if the pair is not as it was on 2026-10-21)? (2) We drop `sprk_createdbyperson` from `sprk_ratecard` / `sprk_ratecardrate` (rate cards are no longer tied to a core record, D-120): confirm. (3) `sprk_procedureassignment` is a secure child of its record (lineage on the three typed lookups), for your 081-side registration. (4) There is no role named `External Collaborator` on dev: which role(s) do flagged-external users hold?
