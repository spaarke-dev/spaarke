# Task 080 - Procedure schema: script, dev checks and -WhatIf (steps 1-3 done, review fixes folded in; 4-12 wait)

Status 2026-10-21 (revision 3, after the first dev run stopped on the parental limit): **script authored, revised after the uac-r2 and ontology-r1 reviews, and `-WhatIf` re-run (reads only). NOTHING was written to Dataverse.** Steps 4-9 and 10 (docs "live" wording), 11 and 12 wait for the gates in section 7. Branch `task/ebill-080-schema`, from `origin/work/ebilling-r1`.

Script: `scripts/ebilling/Deploy-EbillingProcedureSchema.ps1` (git mv of `Deploy-EbillingRuleSetScopeSchema.ps1`, rewritten on `EbillingSchema.psm1`). Test: `tests/scripts/Deploy-EbillingProcedureSchema.Tests.ps1` (Pester 3.4, 85 tests, no live call; every new check was mutation-tested, see the Review fixes section).

## Parental limit and the new cascade shape (dev run 2026-10-21)

**What happened.** The first dev run (script at `4ca826aef8`) stopped creating `sprk_procedureassignment.sprk_project`: `0x80047007 ... is parented to Entity ... sprk_matter. Cannot create another parental relation with Entity: sprk_Project`. Dataverse counts **Delete = Cascade as a parental relationship, and a table can have only one**, whatever the other members say. The uac-r2 review had turned the four assignment Cascade lookups into delete-only cascades, but four Delete Cascade lookups on one table are still four parental relationships. Left behind on dev: the five global choices, `sprk_procedure` and `sprk_procedureversion` (complete), and `sprk_procedureassignment` with only `sprk_matter` (Delete Cascade, the rest NoCascade). Nothing secured, granted or published; 0 rows.

**New shape** (uac-r2 accepted; ontology-r1 passed it with the two conditions below):

| Relationship | Now |
|---|---|
| `sprk_procedureassignment.sprk_matter`, `.sprk_project`, `.sprk_workassignment` (one per `FilingCores` table) | **RemoveLink**; Assign, Share, Unshare, Reparent, Merge (and RollupView) NoCascade. Description: RemoveLink, not Cascade; the core-record delete planner (task 117) removes the record's assignment rows first, the 117 sweep is the backstop |
| `sprk_procedureassignment.sprk_excludedpolicy` | Delete **Cascade**, the rest NoCascade: the table's only parental relationship |
| `sprk_procedureversion.sprk_procedure` | Unchanged: Delete Cascade, the rest NoCascade; the version table's only parental relationship |
| every other relationship on the two tables | not parental (the verify reads every many-to-one relationship, system ones included) |

**Fix in place** (`Repair-CoreLookupDrift`, runs at the start of step 4, before any other assignment lookup is created). When a core lookup exists with exactly the known drift (Delete Cascade; Assign, Share, Unshare, Reparent, Merge NoCascade) the pre-flight prints a NOTE instead of refusing, and the repair GETs the relationship, sets the six members (Delete, Assign, Share, Unshare, Reparent, Merge; everything else is sent back as read), PUTs it to `RelationshipDefinitions(<MetadataId>)`, and reads all six back (a difference after retries stops the script). It then PUTs the lookup's Description (ontology-r1 condition: every core lookup says RemoveLink and names planner 117 with the sweep as the backstop) and reads it back. **Any other cascade drift still stops the pre-flight (exit 2).** `-WhatIf` prints the repair as `WOULD CHANGE`.

**Order is pinned** (uac-r2): the creation of `sprk_excludedpolicy` (and of any Delete Cascade lookup) refuses to start while any other relationship on its table is parental (`Assert-NoOtherParental`, the first statement of `Invoke-LookupSpec`; escalation, exit 2). In apply mode a repair that is merely planned does not count; only a relationship that reads back non-parental does. In `-WhatIf`, where nothing is sent, a repair the pre-flight planned counts as done, an unplanned Cascade does not. Also, `Resolve-Plan` refuses a plan with two Delete Cascade lookups on one table or a Cascade core lookup (`Get-ParentalPlanProblems`).

**Verify pins the final shape** (ontology-r1 condition): the three core lookups read Delete RemoveLink and Assign / Share / Unshare / Reparent / Merge NoCascade and carry the RemoveLink Description; `sprk_excludedpolicy` and `sprk_procedureversion.sprk_procedure` are the only relationships on their tables that cascade Delete, Assign, Share, Unshare or Reparent, and read Delete Cascade (`Get-ParentalProblems`; Merge is not counted, Dataverse itself sets it on lookups to contact).

**Consequence.** Deleting a core record no longer deletes its assignment rows by itself. Task 117's planner must delete them first (the sweep catches stragglers). `procedure-and-file-model.md` 5.2 is the coordinator's to update; this task does not touch it.

**Read-only dev facts used** (2026-10-21, GET only): `sprk_procedureassignment` has `sprk_matter` as its only non-system relationship (Delete Cascade, Assign / Share / Unshare / Reparent / Merge / RollupView NoCascade, `Archive` RemoveLink); all its system relationships read NoCascade (business unit Restrict). `sprk_procedure` shows Merge = Cascade on the two contact lookups (Dataverse sets Merge on lookups to contact), which is why Merge is excluded from the parental test and why the non-Cascade lookups' expected Merge is not pinned.

## Review fixes (revision after `793b89750a`)

uac-r2 asked for two changes and ontology-r1's full review returned CHANGES. All are folded into this revision; there is still no live run. Each item names where the script does it and which test pins it (tests in `tests/scripts/Deploy-EbillingProcedureSchema.Tests.ps1`).

| # | Item | What the script does now | Pinned by |
|---|---|---|---|
| 1 | uac-r2: cascade configuration (revised for the parental limit) | Exactly one Delete Cascade lookup per table, each with the custom delete-only configuration (Delete Cascade; Assign, Share, Unshare, Reparent, Merge, RollupView NoCascade): `sprk_procedureassignment.sprk_excludedpolicy` and `sprk_procedureversion.sprk_procedure` (no longer parental in the full sense). The assignment core lookups are RemoveLink, all NoCascade (see the Parental limit section). Every other lookup is created Assign / Share / Unshare / Reparent NoCascade with its listed delete behaviour. The pre-flight compares all members of an existing relationship (one known drift is repaired in place, any other stops the script); the verify reads them back. Reason: a reassign or share of the parent must not cascade onto these rows, because `RecordOwnershipResolver` and the secure-child job (089) own their owner and shares. | `Get-CascadeMismatches` must-fire for each member = Cascade, a wrong Delete, a parental configuration and an unreadable member; must-not-fire on the exact configuration. The Cascade set is exactly the two lookups above; the three core lookups carry the all-NoCascade configuration; no other lookup carries one. |
| 2 | uac-r2: external-role verify | The `External Collaborator` check is replaced. The verify finds every role held by any systemuser with `sprk_isexternal = true` (direct, through each team the user is in, and through the business unit default team), names each role, and fails if any holds any privilege (Create, Read, Write, Delete, Append, AppendTo, Assign, Share) on `sprk_procedure`, `sprk_procedureversion` or `sprk_procedureassignment`. No flagged-external user is a warning, not a pass; an unreadable query fails the verify. Read only. | Run read-only against dev (section 3): the 3 expected roles are found and named; a control run on `sprk_matter` / `sprk_document` FAILs and names the roles that hold privileges. (The query code is in the script, not in a testable pure function; the pure parts are covered by the live read-only run.) |
| 3 | ontology F1-1 | Field security (Reader Read; Writer Read/Create/Update) now also on `sprk_procedure.sprk_area`, `sprk_procedure.sprk_proceduretype`, `sprk_policyversion.sprk_procedureversion`, `sprk_policyversion.sprk_publishstate` and `sprk_ratecard.sprk_procedureversion`: 18 columns in all (13 + 5). The existing verify loop reads IsSecured and both permission rows for each. | The secured set contains the five and has exactly 18 entries; every secured column is a planned column of the right kind (or one of the two existing rule set columns); name, notes, firm-facing name, reason, mode and version number are never secured; mutation: removing `sprk_area` or the rate card lookup fails a test. |
| 4 | ontology F4-1 | `sprk_createdbyperson` (the D-173 `New-CreatorSpec`, RemoveLink to `systemuser`, created unsecured) is added on `sprk_policy` and `sprk_policyversion`; relationships `sprk_systemuser_sprk_policy_createdbyperson` and `sprk_systemuser_sprk_policyversion_createdbyperson`. **Not** on `sprk_ratecard` or `sprk_ratecardrate`: the verify fails if either has the column. Read-only dev check: none of the four tables has the column or either relationship today. | The creator set is exactly six tables (procedure, version, assignment, rule set, policy, policy version), all RemoveLink to `systemuser`; must-fire if one is added to `sprk_ratecard`. |
| 5 | ontology F4-2 | `sprk_procedureversion.sprk_procedure` and `sprk_procedureversion.sprk_versionnumber` are raised to SystemRequired right after the alternate key (precedent: `Deploy-EbillingSubmissionSchema.ps1` step 4b). `Set-ColumnRequiredLevel -Kind` gained `Integer` (IntegerAttributeMetadata) plus a thin `Set-IntegerRequiredLevel` wrapper, exported from `EbillingSchema.psm1` (additive; no existing caller changes). The verify reads both back. | `Get-RequiredColumnSpecs` is exactly the two; `Get-RequiredLevelProblem` must-fire on None, Recommended, ApplicationRequired and an unreadable column, must-not-fire on SystemRequired; the module text carries `Integer` and the export. |
| 6 | Firm lookups stay Restrict | No code change. `sprk_procedure.sprk_firm` and `sprk_procedureassignment.sprk_firm` stay Restrict. Consequence now written in the data-model docs (`docs/data-model/sprk_ruleset.md`): **deleting or merging an organization fails while a Procedure or an assignment names it.** | Existing Restrict-list test. |
| 7 | Publish list | `PublishXml` now names only what the script changed and owns: the three new tables, `sprk_ruleset`, `sprk_policyversion`, `sprk_ratecard`, the FilingCores tables and the five global choices (`Get-PublishEntities`). `contact`, `systemuser`, `sprk_organization` and `sprk_policy` are gone. `sprk_policy` gains `sprk_createdbyperson` (item 4) but is not published here: task 081 (`Set-ChildRecordCreatorPersonSchema.ps1`) publishes the entities it secures. The column is usable through the Web API before publish. | The list is exactly that set, and must-fire if any of the four dropped names returns; the script text no longer carries the typed list. |
| 8 | K2: grant order | The writer profile is granted before the reader (`Invoke-ColumnSecurity`; the -WhatIf and the field-permission lines now print Writers first). | Script-text test; mutation (reader first) fails it. |
| 9 | K4: scope options | Right after `sprk_ruleset.sprk_scope` is secured (inside the secure-then-grant `try`, before the grants) the options are re-read and the script fails when any of 100000000-100000003 is missing (`Assert-ScopeOptionsIntact`). Being inside the `try`, a failure reverts the column to not secured, like a failed grant. The lost option itself cannot be restored by the script: the message says so. | `Get-MissingOptionValue` must-fire for one lost option and for none returned, must-not-fire for all four (and for extra options); script-text test pins the call site and that it runs only when the column was just secured; mutation (call removed) fails. |

Judgement calls to confirm: (a) `sprk_policy` is not in the publish list although it gains a column (item 7; the coordinator list said drop it). (b) Task 081's creator-person script must list `sprk_policy` and `sprk_policyversion` when it secures `sprk_createdbyperson`; I did not touch it. (c) Securing the two new choice columns on `sprk_policyversion` (7 rows on dev, no value in the new columns) hides nothing that exists today.

Module change in `scripts/ebilling/EbillingSchema.psm1` (shared by every ebilling schema script), all additive: `Add-Lookup -Cascade <hashtable>` (an explicit CascadeConfiguration; used instead of the built-in parental / non-parental configurations when supplied, default `$null` keeps every existing call as it was), `Set-ColumnRequiredLevel -Kind Integer`, `Set-IntegerRequiredLevel`.

## 1. What changed from the old script

| Old item | Now |
|---|---|
| Pre-flight shape, S1 record lookups, S2 scope options, S7 field security + profile reuse + secure-then-grant-with-revert, S9 `sprk_createdbyperson` | Kept (S1 and S7 now cover the P5/P9 list; S9 on `sprk_ruleset`, plus `sprk_policy` and `sprk_policyversion` (ontology F4-1); `sprk_ratecard` / `sprk_ratecardrate` dropped, model 12 / D-120) |
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
| `sprk_procedureversion` | Procedure Version / Procedure Versions | `sprk_versionnumber` whole number 1..2147483647 (**SystemRequired**, ontology F4-2); `sprk_effectivefrom`, `sprk_effectiveto` DateOnly (behaviour DateOnly); `sprk_status` choice (global `sprk_procedureversionstatus`); `sprk_notconverted` two options, default No (task 116); alternate key `sprk_procedure_versionnumber` on (`sprk_procedure`, `sprk_versionnumber`), waited until Active; lookup `sprk_procedure` also **SystemRequired** (F4-2); lookups in 2.3 |
| `sprk_procedureassignment` | Procedure Assignment / Procedure Assignments | `sprk_mode` LOCAL choice (100000000 Follow latest, 100000001 Pinned, 100000002 Excluded); `sprk_reason` memo 2000; lookups in 2.3 |

### 2.3 Lookups (32; every one optional except the two SystemRequired key columns in 2.2) with relationship, delete behaviour and cascade configuration

The parental rule (section "Parental limit"): exactly one Delete Cascade lookup per table. The three assignment core lookups are RemoveLink.

| Table.column | Target | Relationship | Delete | CascadeConfiguration (read back by the verify) |
|---|---|---|---|---|
| `sprk_procedure.sprk_firm` | `sprk_organization` | `sprk_procedure_firm_organization` | **Restrict** (proposal, see 6.1) | assign, share, unshare, reparent NoCascade |
| `sprk_procedure.sprk_issuedby` | `sprk_organization` | `sprk_procedure_issuedby_organization` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `sprk_procedure.sprk_externalcontact` | `contact` | `sprk_procedure_externalcontact_contact` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `sprk_procedure.sprk_internalcontact` | `contact` | `sprk_procedure_internalcontact_contact` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `sprk_procedure.sprk_createdbyperson` | `systemuser` | `sprk_systemuser_sprk_procedure_createdbyperson` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `sprk_procedureversion.sprk_procedure` | `sprk_procedure` | `sprk_procedureversion_procedure_procedure` | **Cascade** (delete-only cascade, not the full parental one) | **Parental, the table's only one**: Delete Cascade; assign, share, unshare, reparent, merge, rollup view all NoCascade |
| `sprk_procedureversion.sprk_previousversion` | `sprk_procedureversion` | `sprk_procedureversion_previousversion_procedureversion` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `sprk_procedureversion.sprk_createdbyperson` | `systemuser` | `sprk_systemuser_sprk_procedureversion_createdbyperson` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `sprk_procedureassignment.sprk_matter` | `sprk_matter` | `sprk_matter_procedureassignment_matter` | RemoveLink (was Cascade; parental limit) | Delete RemoveLink; assign, share, unshare, reparent, merge, rollup view all NoCascade; Description says RemoveLink + planner 117 |
| `sprk_procedureassignment.sprk_project` | `sprk_project` | `sprk_project_procedureassignment_project` | RemoveLink (was Cascade; parental limit) | Delete RemoveLink; assign, share, unshare, reparent, merge, rollup view all NoCascade; Description says RemoveLink + planner 117 |
| `sprk_procedureassignment.sprk_workassignment` | `sprk_workassignment` | `sprk_workassignment_procedureassignment_workassignment` | RemoveLink (was Cascade; parental limit) | Delete RemoveLink; assign, share, unshare, reparent, merge, rollup view all NoCascade; Description says RemoveLink + planner 117 |
| `sprk_procedureassignment.sprk_procedure` | `sprk_procedure` | `sprk_procedureassignment_procedure_procedure` | **Restrict** | assign, share, unshare, reparent NoCascade |
| `sprk_procedureassignment.sprk_pinnedversion` | `sprk_procedureversion` | `sprk_procedureassignment_pinnedversion_procedureversion` | **Restrict** | assign, share, unshare, reparent NoCascade |
| `sprk_procedureassignment.sprk_excludedpolicy` | `sprk_policy` | `sprk_procedureassignment_excludedpolicy_policy` | **Cascade** | **Parental, the table's only one**: Delete Cascade; assign, share, unshare, reparent, merge, rollup view all NoCascade |
| `sprk_procedureassignment.sprk_firm` | `sprk_organization` | `sprk_procedureassignment_firm_organization` | **Restrict** (proposal, see 6.1) | assign, share, unshare, reparent NoCascade |
| `sprk_procedureassignment.sprk_createdbyperson` | `systemuser` | `sprk_systemuser_sprk_procedureassignment_createdbyperson` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `sprk_ruleset.sprk_matter` | `sprk_matter` | `sprk_matter_ruleset_matter` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `sprk_ruleset.sprk_project` | `sprk_project` | `sprk_project_ruleset_project` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `sprk_ruleset.sprk_workassignment` | `sprk_workassignment` | `sprk_workassignment_ruleset_workassignment` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `sprk_ruleset.sprk_procedure` | `sprk_procedure` | `sprk_ruleset_procedure_procedure` | **Restrict** | assign, share, unshare, reparent NoCascade |
| `sprk_ruleset.sprk_createdbyperson` | `systemuser` | `sprk_systemuser_sprk_ruleset_createdbyperson` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `sprk_policyversion.sprk_procedureversion` | `sprk_procedureversion` | `sprk_policyversion_procedureversion_procedureversion` | **Restrict** | assign, share, unshare, reparent NoCascade |
| `sprk_policyversion.sprk_basedonversion` | `sprk_policyversion` | `sprk_policyversion_basedonversion_policyversion` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `sprk_policyversion.sprk_createdbyperson` | `systemuser` | `sprk_systemuser_sprk_policyversion_createdbyperson` | RemoveLink (ontology F4-1) | assign, share, unshare, reparent NoCascade |
| `sprk_policy.sprk_createdbyperson` | `systemuser` | `sprk_systemuser_sprk_policy_createdbyperson` | RemoveLink (ontology F4-1) | assign, share, unshare, reparent NoCascade |
| `sprk_ratecard.sprk_procedureversion` | `sprk_procedureversion` | `sprk_ratecard_procedureversion_procedureversion` | **Restrict** | assign, share, unshare, reparent NoCascade |
| `{sprk_matter, sprk_project, sprk_workassignment}.sprk_billingfirm` (3 rows) | `sprk_organization` | `{table}_billingfirm_organization` | RemoveLink | assign, share, unshare, reparent NoCascade |
| `{sprk_matter, sprk_project, sprk_workassignment}.sprk_billingresponsible` (3 rows) | `sprk_organization` | `{table}_billingresponsible_organization` | RemoveLink | assign, share, unshare, reparent NoCascade |

No lookup whose target is a core table is Restrict or Cascade (tests pin both, with planted controls). An existing lookup with another delete behaviour is never changed: the pre-flight stops (exit 2).

### 2.4 Columns on existing tables (non-lookup)

| Table.column | Change |
|---|---|
| `sprk_ruleset.sprk_scope` | InsertOptionValue 100000002 `Record` and 100000003 `Firm record` (existing 100000000 `Client default`, 100000001 `Firm` verified, never changed; stops if a value or label exists with another meaning) |
| `sprk_policyversion.sprk_publishstate` | New choice bound to global `sprk_publishstate`, optional, no default |

### 2.5 Field security (P9) - 18 columns, IsSecured true (plus one per extra FilingCores table)

Reader profile ("Spaarke BFF-Managed Field Readers"): Read (4), Create 0, Update 0. Writer profile ("Spaarke BFF-Managed Field Writers"): Read 4, Create 4, Update 4. Granted writer first, then reader. Permission rows only; no profile member is ever added or removed (the pre-flight stops if the reader profile is missing from a default team or the writer holds anyone but the BFF application users).

`sprk_procedure`: `sprk_area`, `sprk_proceduretype`, `sprk_appliesto`, `sprk_firm`, `sprk_status`. `sprk_procedureversion`: `sprk_effectivefrom`, `sprk_effectiveto`, `sprk_status`, `sprk_notconverted`. `sprk_policyversion`: `sprk_procedureversion`, `sprk_publishstate`. `sprk_ratecard`: `sprk_procedureversion`. `sprk_ruleset`: `sprk_scope`, `sprk_firm`, `sprk_procedure`, `sprk_matter`, `sprk_project`, `sprk_workassignment` (the three record lookups, one per FilingCores table). 5 + 4 + 2 + 1 + 6 = 18, matching the dev run of 2026-10-21 (uac-r2 read-back: Readers 4/0/0, Writers 4/4/4, plus the platform System Administrator profile; nothing else can write).

### 2.6 Other

- Auditing (table level) on the three new tables.
- Publish (only when something changed, never `PublishAllXml`): the three new tables, `sprk_ruleset`, `sprk_policyversion`, `sprk_ratecard`, the core tables, and the five global choices. Not `contact`, `systemuser`, `sprk_organization` or `sprk_policy`.
- Required levels: `sprk_procedureversion.sprk_procedure` and `sprk_versionnumber` SystemRequired, read back by the verify.
- Never created: banner column, `sprk_isexceptions`, business-unit column or N:N, `sprk_ratecard.sprk_scope`, `sprk_storedfile`, any role privilege, form or view. `sprk_ruleset.sprk_sourcedocument` and the `sprk_ratecard` core lookups are untouched.

## 3. Read-only dev checks (2026-10-21, step 2)

Run with GET calls only against `https://spaarkedev1.crm.dynamics.com`.

| Check | Result |
|---|---|
| Five global choices | all absent |
| `sprk_policydomain` | exists: 100000000 Billing, 100000001 Ontology, 100000002 Docketing |
| `sprk_procedure`, `sprk_procedureversion`, `sprk_procedureassignment`, `sprk_storedfile` | all absent |
| Every planned column and lookup (all 32 lookups, `sprk_publishstate`, the three tables' columns) | all absent; no relationship name taken. Re-read for this revision: `sprk_createdbyperson` is absent on `sprk_policy`, `sprk_policyversion`, `sprk_ratecard` and `sprk_ratecardrate`; neither creator relationship name exists; `sprk_policyversion.sprk_procedureversion`, `sprk_policyversion.sprk_publishstate` and `sprk_ratecard.sprk_procedureversion` are absent |
| `sprk_ruleset.sprk_scope` options | exactly 100000000 Client default, 100000001 Firm (local choice) |
| `sprk_ratecard.sprk_scope`, `sprk_ruleset.sprk_isexceptions`, `sprk_ruleset.sprk_allbusinessunits` | absent (the old script never ran) |
| `sprk_ruleset.sprk_sourcedocument` | exists (lookup), untouched |
| `sprk_assignedlawfirm1` / `sprk_assignedlawfirm2` | exist as lookups on `sprk_matter`, `sprk_project` and `sprk_workassignment` |
| Rows | `sprk_ruleset` 0, `sprk_ratecard` 0, `sprk_policyversion` 7 (get no `sprk_publishstate`; dev data is recreated, O-13) |
| Dev state after the first run stopped (2026-10-21; re-read by the `-WhatIf` pre-flight) | Five global choices exist; `sprk_procedure` and `sprk_procedureversion` complete; `sprk_procedureassignment` exists with only `sprk_matter` (Delete Cascade, rest NoCascade: the one known drift). Nothing secured, granted or published; 0 rows. The pre-flight accepts all of it and prints the NOTE for `sprk_matter`; the rest is skipped or planned as create. |
| Billing tab on the main forms | `Matter main form`: yes (tab BILLING). `Project main form`: yes (BILLING). **`Work Assignment main form`: no** (tabs Overview, Events, Search). The secondary "Information" forms have one General tab. No edit made; the work-assignment gap is task 079's. |
| Reader / writer profiles | reader on all 6 business-unit default teams; writer holds `# mi-bff-api-dev` and `SDAP-BFF-SPE-API` and nobody else |
| Flagged-external users and their roles (`sprk_isexternal = true`; direct, through teams, and the business unit default team) | 2 users (test user A, disabled; test user B) hold 3 roles: **Spaarke Basic User**, **Spaarke Console User**, **Spaarke Office Add In User**. None has any privilege on the three new tables (they do not exist yet, so this is true by construction today; the check is the guard for 081 / 089). Control: the same query pointed at `sprk_matter` and `sprk_document` reports FAIL for Spaarke Office Add In User and Spaarke Basic User, so it does fire. 10 GET requests, no write. There is no role named `External Collaborator` on dev, so the old name-based check is gone. |

## 4. `-WhatIf` run (dev, exit 0, reads only; re-run after the parental fix)

Command: `pwsh scripts/ebilling/Deploy-EbillingProcedureSchema.ps1 -WhatIf`, wrapped in a tripwire that logs every HTTP call and throws on anything but GET: **314 requests, 0 non-GET**. Result: created 64, changed 20, skipped 40. Dev already holds the first run's items, so those print as skipped. The changed items are the in-place repair of `sprk_procedureassignment.sprk_matter` (printed as `WOULD CHANGE`: the CascadeConfiguration Delete Cascade -> RemoveLink with the other five members NoCascade, then its Description) and the 18 `secure` items. Everything after the repair in step 4 (`sprk_project`, `sprk_workassignment`, `sprk_mode`, ... `sprk_excludedpolicy` as Delete Cascade) prints as `WOULD CREATE`. The `-WhatIf` skips the verify and the publish step, so the external-role check, the final-shape check and the publish list are shown by the section 3 read-only run and by the tests.

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
  global choice sprk_proceduretype: exists, identical
  global choice sprk_procedureappliesto: exists, identical
  global choice sprk_procedurestatus: exists, identical
  global choice sprk_procedureversionstatus: exists, identical
  global choice sprk_publishstate: exists, identical
  NOTE: table sprk_procedure ALREADY EXISTS - it is verified and skipped, not recreated or changed.
  NOTE: table sprk_procedureversion ALREADY EXISTS - it is verified and skipped, not recreated or changed.
  NOTE: table sprk_procedureassignment ALREADY EXISTS - it is verified and skipped, not recreated or changed.
  NOTE: sprk_procedureassignment.sprk_matter (sprk_matter_procedureassignment_matter) reads Delete = Cascade from the earlier run: it is repaired in place to RemoveLink / NoCascade (the one known drift).
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
  lookup plan (32 lookups): table.column -> target | relationship | delete behaviour | cascade (assign/share/unshare/reparent)
    sprk_procedure.sprk_firm -> sprk_organization | sprk_procedure_firm_organization | Restrict | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_procedure.sprk_issuedby -> sprk_organization | sprk_procedure_issuedby_organization | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_procedure.sprk_externalcontact -> contact | sprk_procedure_externalcontact_contact | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_procedure.sprk_internalcontact -> contact | sprk_procedure_internalcontact_contact | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_procedure.sprk_createdbyperson -> systemuser | sprk_systemuser_sprk_procedure_createdbyperson | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_procedureversion.sprk_procedure -> sprk_procedure | sprk_procedureversion_procedure_procedure | Cascade | NoCascade/NoCascade/NoCascade/NoCascade | custom: merge NoCascade, rollupview NoCascade
    sprk_procedureversion.sprk_previousversion -> sprk_procedureversion | sprk_procedureversion_previousversion_procedureversion | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_procedureversion.sprk_createdbyperson -> systemuser | sprk_systemuser_sprk_procedureversion_createdbyperson | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_procedureassignment.sprk_matter -> sprk_matter | sprk_matter_procedureassignment_matter | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade | custom: merge NoCascade, rollupview NoCascade
    sprk_procedureassignment.sprk_project -> sprk_project | sprk_project_procedureassignment_project | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade | custom: merge NoCascade, rollupview NoCascade
    sprk_procedureassignment.sprk_workassignment -> sprk_workassignment | sprk_workassignment_procedureassignment_workassignment | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade | custom: merge NoCascade, rollupview NoCascade
    sprk_procedureassignment.sprk_procedure -> sprk_procedure | sprk_procedureassignment_procedure_procedure | Restrict | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_procedureassignment.sprk_pinnedversion -> sprk_procedureversion | sprk_procedureassignment_pinnedversion_procedureversion | Restrict | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_procedureassignment.sprk_excludedpolicy -> sprk_policy | sprk_procedureassignment_excludedpolicy_policy | Cascade | NoCascade/NoCascade/NoCascade/NoCascade | custom: merge NoCascade, rollupview NoCascade
    sprk_procedureassignment.sprk_firm -> sprk_organization | sprk_procedureassignment_firm_organization | Restrict | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_procedureassignment.sprk_createdbyperson -> systemuser | sprk_systemuser_sprk_procedureassignment_createdbyperson | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_ruleset.sprk_matter -> sprk_matter | sprk_matter_ruleset_matter | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_ruleset.sprk_project -> sprk_project | sprk_project_ruleset_project | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_ruleset.sprk_workassignment -> sprk_workassignment | sprk_workassignment_ruleset_workassignment | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_ruleset.sprk_procedure -> sprk_procedure | sprk_ruleset_procedure_procedure | Restrict | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_ruleset.sprk_createdbyperson -> systemuser | sprk_systemuser_sprk_ruleset_createdbyperson | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_policyversion.sprk_procedureversion -> sprk_procedureversion | sprk_policyversion_procedureversion_procedureversion | Restrict | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_policyversion.sprk_basedonversion -> sprk_policyversion | sprk_policyversion_basedonversion_policyversion | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_policyversion.sprk_createdbyperson -> systemuser | sprk_systemuser_sprk_policyversion_createdbyperson | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_policy.sprk_createdbyperson -> systemuser | sprk_systemuser_sprk_policy_createdbyperson | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_ratecard.sprk_procedureversion -> sprk_procedureversion | sprk_ratecard_procedureversion_procedureversion | Restrict | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_matter.sprk_billingfirm -> sprk_organization | sprk_matter_billingfirm_organization | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_matter.sprk_billingresponsible -> sprk_organization | sprk_matter_billingresponsible_organization | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_project.sprk_billingfirm -> sprk_organization | sprk_project_billingfirm_organization | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_project.sprk_billingresponsible -> sprk_organization | sprk_project_billingresponsible_organization | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_workassignment.sprk_billingfirm -> sprk_organization | sprk_workassignment_billingfirm_organization | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
    sprk_workassignment.sprk_billingresponsible -> sprk_organization | sprk_workassignment_billingresponsible_organization | RemoveLink | NoCascade/NoCascade/NoCascade/NoCascade
1. P1 global choices (values from 100000000)
  skipped  global option set sprk_proceduretype (exists, 5 options)
  skipped  global option set sprk_procedureappliesto (exists, 3 options)
  skipped  global option set sprk_procedurestatus (exists, 2 options)
  skipped  global option set sprk_procedureversionstatus (exists, 4 options)
  skipped  global option set sprk_publishstate (exists, 2 options)
2. P2 table sprk_procedure (user-owned, audited)
  skipped  table sprk_procedure (exists, primary name sprk_name)
  skipped  sprk_name max length already 200
  skipped  sprk_name already application-required
  skipped  sprk_procedure auditing already on
  skipped  sprk_firmfacingname (exists, String)
  skipped  sprk_area (exists, choice bound to sprk_policydomain)
  skipped  sprk_proceduretype (exists, choice bound to sprk_proceduretype)
  skipped  sprk_appliesto (exists, choice bound to sprk_procedureappliesto)
  skipped  sprk_firm (exists, Lookup -> sprk_organization)
  skipped  sprk_issuedby (exists, Lookup -> sprk_organization)
  skipped  sprk_externalcontact (exists, Lookup -> contact)
  skipped  sprk_internalcontact (exists, Lookup -> contact)
  skipped  sprk_createdbyperson (exists, Lookup -> systemuser)
  skipped  sprk_status (exists, choice bound to sprk_procedurestatus)
  skipped  sprk_notes (exists, Memo)
3. P3 table sprk_procedureversion (user-owned, audited; deleted with its sprk_procedure, delete-only cascade)
  skipped  table sprk_procedureversion (exists, primary name sprk_name)
  skipped  sprk_name max length already 200
  skipped  sprk_name already application-required
  skipped  sprk_procedureversion auditing already on
  skipped  sprk_procedure (exists, Lookup -> sprk_procedure)
  skipped  sprk_versionnumber (exists, Integer)
   alternate key (sprk_procedure, sprk_versionnumber)
  skipped  key sprk_procedure_versionnumber (exists on (sprk_procedure,sprk_versionnumber))
  skipped  sprk_previousversion (exists, Lookup -> sprk_procedureversion)
  skipped  sprk_createdbyperson (exists, Lookup -> systemuser)
  skipped  sprk_effectivefrom (exists, DateTime)
  skipped  sprk_effectiveto (exists, DateTime)
  skipped  sprk_status (exists, choice bound to sprk_procedureversionstatus)
   required levels: sprk_procedureversion.sprk_procedure and sprk_versionnumber -> SystemRequired (the alternate key columns)
  skipped  sprk_procedure already SystemRequired
  skipped  sprk_versionnumber already SystemRequired
  skipped  sprk_notconverted (exists, Boolean)
4. P4 table sprk_procedureassignment (user-owned, audited)
  skipped  table sprk_procedureassignment (exists, primary name sprk_name)
  skipped  sprk_name max length already 200
  skipped  sprk_name already application-required
  skipped  sprk_procedureassignment auditing already on
  WOULD CHANGE sprk_procedureassignment.sprk_matter (sprk_matter_procedureassignment_matter) CascadeConfiguration: Delete Cascade -> RemoveLink; Assign / Share / Unshare / Reparent / Merge NoCascade
  WOULD CHANGE sprk_procedureassignment.sprk_matter Description to the RemoveLink text
  skipped  sprk_matter (exists, Lookup -> sprk_matter)
  WOULD CREATE sprk_project (Lookup -> sprk_project, custom cascade: delete RemoveLink, assign/share/unshare/reparent NoCascade/NoCascade/NoCascade/NoCascade)
  WOULD CREATE sprk_workassignment (Lookup -> sprk_workassignment, custom cascade: delete RemoveLink, assign/share/unshare/reparent NoCascade/NoCascade/NoCascade/NoCascade)
  WOULD CREATE sprk_mode (Picklist)
  WOULD CREATE sprk_procedure (Lookup -> sprk_procedure)
  WOULD CREATE sprk_pinnedversion (Lookup -> sprk_procedureversion)
  WOULD CREATE sprk_excludedpolicy (Lookup -> sprk_policy, custom cascade: delete Cascade, assign/share/unshare/reparent NoCascade/NoCascade/NoCascade/NoCascade)
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
6. P6 sprk_policyversion : sprk_procedureversion, sprk_publishstate, sprk_basedonversion, sprk_createdbyperson; sprk_policy.sprk_createdbyperson
  WOULD CREATE sprk_procedureversion (Lookup -> sprk_procedureversion)
  WOULD CREATE sprk_basedonversion (Lookup -> sprk_policyversion)
  WOULD CREATE sprk_createdbyperson (Lookup -> systemuser)
  WOULD CREATE sprk_createdbyperson (Lookup -> systemuser)
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
  WOULD SECURE sprk_procedure.sprk_area
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_area (read=4 create=4 update=4)
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_area (read=4 create=0 update=0)
  WOULD SECURE sprk_procedure.sprk_proceduretype
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_proceduretype (read=4 create=4 update=4)
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_proceduretype (read=4 create=0 update=0)
  WOULD SECURE sprk_procedure.sprk_appliesto
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_appliesto (read=4 create=4 update=4)
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_appliesto (read=4 create=0 update=0)
  WOULD SECURE sprk_procedure.sprk_firm
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_firm (read=4 create=4 update=4)
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_firm (read=4 create=0 update=0)
  WOULD SECURE sprk_procedure.sprk_status
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_status (read=4 create=4 update=4)
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_status (read=4 create=0 update=0)
  WOULD SECURE sprk_procedureversion.sprk_effectivefrom
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_procedureversion.sprk_effectivefrom (read=4 create=4 update=4)
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_procedureversion.sprk_effectivefrom (read=4 create=0 update=0)
  WOULD SECURE sprk_procedureversion.sprk_effectiveto
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_procedureversion.sprk_effectiveto (read=4 create=4 update=4)
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_procedureversion.sprk_effectiveto (read=4 create=0 update=0)
  WOULD SECURE sprk_procedureversion.sprk_status
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_procedureversion.sprk_status (read=4 create=4 update=4)
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_procedureversion.sprk_status (read=4 create=0 update=0)
  WOULD SECURE sprk_procedureversion.sprk_notconverted
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_procedureversion.sprk_notconverted (read=4 create=4 update=4)
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_procedureversion.sprk_notconverted (read=4 create=0 update=0)
  WOULD SECURE sprk_policyversion.sprk_procedureversion after it is created, then grant: Spaarke BFF-Managed Field Writers (read=4 create=4 update=4); Spaarke BFF-Managed Field Readers (read=4 create=0 update=0)
  WOULD SECURE sprk_policyversion.sprk_publishstate after it is created, then grant: Spaarke BFF-Managed Field Writers (read=4 create=4 update=4); Spaarke BFF-Managed Field Readers (read=4 create=0 update=0)
  WOULD SECURE sprk_ratecard.sprk_procedureversion after it is created, then grant: Spaarke BFF-Managed Field Writers (read=4 create=4 update=4); Spaarke BFF-Managed Field Readers (read=4 create=0 update=0)
  WOULD SECURE sprk_ruleset.sprk_scope
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_scope (read=4 create=4 update=4)
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_scope (read=4 create=0 update=0)
  WOULD SECURE sprk_ruleset.sprk_firm
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_firm (read=4 create=4 update=4)
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_firm (read=4 create=0 update=0)
  WOULD SECURE sprk_ruleset.sprk_procedure after it is created, then grant: Spaarke BFF-Managed Field Writers (read=4 create=4 update=4); Spaarke BFF-Managed Field Readers (read=4 create=0 update=0)
  WOULD SECURE sprk_ruleset.sprk_matter after it is created, then grant: Spaarke BFF-Managed Field Writers (read=4 create=4 update=4); Spaarke BFF-Managed Field Readers (read=4 create=0 update=0)
  WOULD SECURE sprk_ruleset.sprk_project after it is created, then grant: Spaarke BFF-Managed Field Writers (read=4 create=4 update=4); Spaarke BFF-Managed Field Readers (read=4 create=0 update=0)
  WOULD SECURE sprk_ruleset.sprk_workassignment after it is created, then grant: Spaarke BFF-Managed Field Writers (read=4 create=4 update=4); Spaarke BFF-Managed Field Readers (read=4 create=0 update=0)
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_procedure.sprk_area
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_procedure.sprk_proceduretype
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_procedure.sprk_appliesto
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_procedure.sprk_firm
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_procedure.sprk_status
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_procedureversion.sprk_effectivefrom
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_procedureversion.sprk_effectiveto
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_procedureversion.sprk_status
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_procedureversion.sprk_notconverted
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_ruleset.sprk_scope
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_ruleset.sprk_firm
Publish
  WhatIf: publish skipped

================ Summary ================
created : 64
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
    + sprk_policyversion.sprk_createdbyperson
    + sprk_policy.sprk_createdbyperson
    + sprk_policyversion.sprk_publishstate
    + sprk_ratecard.sprk_procedureversion
    + sprk_matter.sprk_billingfirm
    + sprk_matter.sprk_billingresponsible
    + sprk_project.sprk_billingfirm
    + sprk_project.sprk_billingresponsible
    + sprk_workassignment.sprk_billingfirm
    + sprk_workassignment.sprk_billingresponsible
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_area
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_area
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_proceduretype
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_proceduretype
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_appliesto
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_appliesto
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_firm
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_firm
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedure.sprk_status
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedure.sprk_status
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedureversion.sprk_effectivefrom
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedureversion.sprk_effectivefrom
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedureversion.sprk_effectiveto
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedureversion.sprk_effectiveto
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedureversion.sprk_status
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedureversion.sprk_status
    + field permission Spaarke BFF-Managed Field Writers on sprk_procedureversion.sprk_notconverted
    + field permission Spaarke BFF-Managed Field Readers on sprk_procedureversion.sprk_notconverted
    + field permission Spaarke BFF-Managed Field Writers on sprk_policyversion.sprk_procedureversion
    + field permission Spaarke BFF-Managed Field Readers on sprk_policyversion.sprk_procedureversion
    + field permission Spaarke BFF-Managed Field Writers on sprk_policyversion.sprk_publishstate
    + field permission Spaarke BFF-Managed Field Readers on sprk_policyversion.sprk_publishstate
    + field permission Spaarke BFF-Managed Field Writers on sprk_ratecard.sprk_procedureversion
    + field permission Spaarke BFF-Managed Field Readers on sprk_ratecard.sprk_procedureversion
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_scope
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_scope
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_firm
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_firm
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_procedure
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_procedure
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_matter
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_matter
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_project
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_project
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_workassignment
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_workassignment
changed : 20
    ~ sprk_procedureassignment.sprk_matter cascade Cascade -> RemoveLink
    ~ sprk_procedureassignment.sprk_matter description
    ~ secure sprk_procedure.sprk_area
    ~ secure sprk_procedure.sprk_proceduretype
    ~ secure sprk_procedure.sprk_appliesto
    ~ secure sprk_procedure.sprk_firm
    ~ secure sprk_procedure.sprk_status
    ~ secure sprk_procedureversion.sprk_effectivefrom
    ~ secure sprk_procedureversion.sprk_effectiveto
    ~ secure sprk_procedureversion.sprk_status
    ~ secure sprk_procedureversion.sprk_notconverted
    ~ secure sprk_policyversion.sprk_procedureversion
    ~ secure sprk_policyversion.sprk_publishstate
    ~ secure sprk_ratecard.sprk_procedureversion
    ~ secure sprk_ruleset.sprk_scope
    ~ secure sprk_ruleset.sprk_firm
    ~ secure sprk_ruleset.sprk_procedure
    ~ secure sprk_ruleset.sprk_matter
    ~ secure sprk_ruleset.sprk_project
    ~ secure sprk_ruleset.sprk_workassignment
skipped : 40
    = optionset sprk_proceduretype
    = optionset sprk_procedureappliesto
    = optionset sprk_procedurestatus
    = optionset sprk_procedureversionstatus
    = optionset sprk_publishstate
    = table sprk_procedure
    = sprk_procedure.sprk_name length 200
    = sprk_procedure.sprk_name required
    = sprk_procedure auditing
    = sprk_procedure.sprk_firmfacingname
    = sprk_procedure.sprk_area
    = sprk_procedure.sprk_proceduretype
    = sprk_procedure.sprk_appliesto
    = sprk_procedure.sprk_firm
    = sprk_procedure.sprk_issuedby
    = sprk_procedure.sprk_externalcontact
    = sprk_procedure.sprk_internalcontact
    = sprk_procedure.sprk_createdbyperson
    = sprk_procedure.sprk_status
    = sprk_procedure.sprk_notes
    = table sprk_procedureversion
    = sprk_procedureversion.sprk_name length 200
    = sprk_procedureversion.sprk_name required
    = sprk_procedureversion auditing
    = sprk_procedureversion.sprk_procedure
    = sprk_procedureversion.sprk_versionnumber
    = key sprk_procedure_versionnumber
    = sprk_procedureversion.sprk_previousversion
    = sprk_procedureversion.sprk_createdbyperson
    = sprk_procedureversion.sprk_effectivefrom
    = sprk_procedureversion.sprk_effectiveto
    = sprk_procedureversion.sprk_status
    = sprk_procedureversion.sprk_procedure required SystemRequired
    = sprk_procedureversion.sprk_versionnumber required SystemRequired
    = sprk_procedureversion.sprk_notconverted
    = table sprk_procedureassignment
    = sprk_procedureassignment.sprk_name length 200
    = sprk_procedureassignment.sprk_name required
    = sprk_procedureassignment auditing
    = sprk_procedureassignment.sprk_matter
WhatIf run: nothing was written.
EXIT CODE: 0
```

## 5. Acceptance criteria: met now vs needs the live run

| Criterion | State |
|---|---|
| Notes record before-notices, uac-r2's answer, ontology-r1's PASS, owner's OK, with dates, before the run | **Pending** (section 7); notice texts are ready in 8 |
| Five global choices exactly as listed; `sprk_area` bound to `sprk_policydomain` | Script + `-WhatIf` done; **needs the live run** (the script's verify checks it) |
| Three tables user-owned and audited, listed columns, `sprk_procedureversion.sprk_procedure` and `sprk_procedureassignment.sprk_excludedpolicy` the only parental (delete-only cascade) relationships on their tables, the three assignment core lookups RemoveLink / NoCascade,  the two key columns SystemRequired, key Active, DateOnly | Same (the verify reads each) |
| Each lookup reads its delete behaviour and every CascadeConfiguration member (the two Cascade lookups: Delete Cascade, all else NoCascade; the three core lookups: RemoveLink, all NoCascade; no other Cascade on either table); no lookup to a core table reads Restrict | Plan printed and tested offline (Pester pins it, with must-fire controls); live read by the verify after the run |
| `sprk_scope` has exactly the four values; record lookups for every FilingCores table and no other | Same (verify counts one `sprk_ruleset` lookup per core table) |
| Every P9 column (18) IsSecured with the reader Read-only row and writer Create/Update row; no member changed | Same; the pre-flight (live, read-only) already proves both profiles are in the expected shape |
| `sprk_billingfirm` / `sprk_billingresponsible` on every FilingCores table and no other | Same (verify reads the relationships of `sprk_organization` by referencing attribute) |
| Negatives: no banner, `sprk_isexceptions`, BU column / N:N, `sprk_ratecard.sprk_scope`, `sprk_storedfile`; `sprk_sourcedocument` and the ratecard core lookups unchanged; no role held by a flagged-external user (direct or through a team) has a privilege on the three new tables; `git diff --stat src/` empty | Script creates none of them (a test pins this); dev has none today (3); `git diff --stat src/` is empty on this branch; the external-role check runs in the verify (6.6) and was exercised read-only against dev (3 roles, none with a privilege) |
| Second run reports every item skipped and writes nothing | **Needs the live run** |
| After-notices recorded; four docs updated as in step 10 | Docs drafted now with a "not yet run on dev" banner (`docs/data-model/sprk_ruleset.md`, `sprk_ratecard.md`, `sprk_matter-related-tables.md`, `sprk_workassignment.md`); remove the banner after the run; after-notices pending |

## 6. Items for the review and deviations

6.1 **Restrict on the two firm lookups** (`sprk_procedure.sprk_firm`, `sprk_procedureassignment.sprk_firm`) is this task's proposal (POML): a nulled firm would widen a firm-only Procedure, or an override, to every firm. Raise it in both before-notices.

6.2 **Alternate key columns SystemRequired - done** (ontology F4-2, Review fixes item 5).

6.3 **My choices where the task is silent**: relationship names (section 2); `sprk_name` length 200 on all three tables; `sprk_notes` memo 100000, `sprk_reason` memo 2000; `sprk_versionnumber` range 1..2147483647; display names of the five global choices and of the new columns; no default on any choice.

6.4 **Column-level auditing**: table-level `IsAuditEnabled` is set (P10). New columns are created with the Dataverse default; the script does not force per-column auditing (the module's `Add-Lookup` cannot, and `Deploy-EbillingSettingsSchema.ps1` needed a follow-up PUT per lookup). Say so in the review if per-column auditing is wanted.

6.5 **Publish list narrowed** (Review fixes item 7): no `sprk_policy`, `sprk_organization`, `contact` or `systemuser`; the new `sprk_policy.sprk_createdbyperson` is published by 081 when it secures it.

6.6 **External privilege check** (Review fixes item 2). The model (5.2, K-1) says outside users hold no privilege on the three new tables. There is no role named `External Collaborator` on dev, so the check goes by user: every role held by a `sprk_isexternal = true` user (direct, team, business unit default team) is read and none may hold a privilege on the three tables. On dev today those are Spaarke Basic User, Spaarke Console User and Spaarke Office Add In User. This script grants no role privilege, so the check passes on the first run and **will fail by design once 081 / 089 add privileges on the new tables to a role those users hold** (for example a Basic User privilege for the BFF-mediated reads): then the owner decides whether the role or the flag is wrong. Zero flagged users prints a warning, not a pass.

6.7 **Highest-risk live step**: securing the picklists (`sprk_ruleset.sprk_scope`, and the new global-choice columns: `sprk_area`, `sprk_proceduretype`, `sprk_appliesto`, `sprk_status` x2, `sprk_publishstate`) with a typed GET-then-PUT. After `sprk_scope` the options are re-read and the script fails if one is lost (K4). The old script's fallback (retry once with `$expand=OptionSet`) now also covers the Boolean `sprk_notconverted`. A failed grant reverts the column to not secured and publishes. `sprk_ruleset.sprk_scope` and `sprk_firm` hold no data on dev (0 rule sets), so no reader is masked while they are secured.

6.8 **Old script references**: `projects/ebilling-r1/notes/080-ruleset-scope-schema.md` and `notes/rule-set-scope-design.md` are history and still name the old file; `projects/spaarke-ontology-platform-r1/current-task.md` also names it (not mine to edit). `TASK-INDEX.md`, the POML status and `current-task.md` were not touched (this session is authoring only).

6.9 **Dependency on task 117 (new).** With the core lookups RemoveLink, deleting a Matter, Project or Work Assignment leaves its assignment rows with an empty core lookup until the 117 planner removes them first (the sweep is the backstop). Until 117 is live nothing deletes them; an assignment row with no core lookup must be treated as orphaned by the readers. Raise with the 117 owner.

6.10 **Tests** (85, no live call): the script is new, so the first 25 cannot fail "before" in the usual sense (the functions under test did not exist). For the 34 added in this revision: against the previous revision the file fails whole (the new functions are missing); and each new check was mutation-tested against the current script (drop `sprk_area` from the secured set, drop the rate card lookup, drop the policy creator, add a creator on the rate card, add `contact` or `sprk_policy` to the publish list, grant the reader first, remove the scope re-read, drop the version-number required level, set a required level to ApplicationRequired, blind the required-level comparison): all 11 mutants failed at least one test, then the script was restored. Revision 3 adds 26 tests (parental limit): two Delete Cascade lookups on one table (must-fire), a core lookup set to Cascade (must-fire), the real plan (must-not-fire), the known-drift test (accept only the exact drift, refuse each other member, never for a lookup without a drift), the in-place PUT payload (six members set whatever was read, everything else untouched, a missing member refused), the final-shape verify (silent on the final shape and on Merge-only cascades; fires on a Cascade core lookup, assign / share cascades, a missing or non-Cascade `sprk_excludedpolicy`), the ORDER tests (refuses to create `sprk_excludedpolicy` while `sprk_matter` or any other relationship is parental, also when a repair is only planned in apply mode; proceeds after the read-back; `-WhatIf` counts a planned repair; script-text order: repair before any other assignment lookup, PUT then read-back then done, guard first in `Invoke-LookupSpec`), and `Resolve-Plan` stopping on a plan-guard problem. 10 further mutants (core lookups back to Cascade, no DriftFrom, guard call removed, repair after the lookups, fix body skipping Merge, parental checks ignoring Delete, a planned repair counting in apply mode, plan guard dropped, old CASCADE text back, known drift accepting anything) all failed at least one test; the script was restored (85 pass). The Pester file pins: both initializer shapes; refusal of a missing file, no declaration, a commented-out declaration, two declarations, an empty list, a non-`sprk_` name and a duplicate; a mere use of `FilingCores` is not a declaration; one record lookup / typed lookup / billing column pair per core table; a fourth core table adds its lookups with no script change; no Restrict lookup to a core table (with a planted control that the scan does fire on); the script text names no core table; the Cascade set and the exact cascade configuration with must-fire per member; the 18 secured columns; the six creator tables; the two required columns; the publish list; the scope-option re-read helper.

## 7. Gates before the real run (step 4-9) - all pending

| Gate | State |
|---|---|
| Before-notice to ontology-r1 (item list, delete behaviours, `-WhatIf` output) | not sent |
| Before-notice to uac-r2 (same + the profile-reuse question + confirm the dropped `sprk_createdbyperson` on `sprk_ratecard` / `sprk_ratecardrate`) | not sent; uac-r2's answer pending. uac-r2's review of 080 is answered by Review fixes items 1 and 2. |
| ontology-r1 full schema-review PASS (fix every finding, re-run `-WhatIf`) | review returned CHANGES on `793b89750a`; F1-1, F4-1, F4-2 and the publish / K2 / K4 items are fixed in this revision and `-WhatIf` is re-run; **re-review pending**. The parental-limit fix (above) was accepted by uac-r2; ontology-r1 passed it with two conditions (core lookup Descriptions say RemoveLink + planner 117; the verify reads RemoveLink and the NoCascade members back), both done in revision 3 |
| Owner's OK for the dev run, shown the `-WhatIf` output | not asked |
| Real run, second run, verify, after-notices, docs banner removal, TASK-INDEX | not done; **088's real run must come after this one** (its Stored File has a parent lookup to `sprk_procedureversion`) |

Escalation triggers read: none fired during authoring (no planned name exists with another meaning; `FilingCores` parsed; no existing column would be deleted or changed; `-WhatIf` shows only listed items). Triggers 6.2 and 6.6 are raised above as review points, not as blockers of the script.

## 8. Notice texts (ready to send; not sent)

**To ontology-r1 (full schema review).** Task 080 script `scripts/ebilling/Deploy-EbillingProcedureSchema.ps1` on branch `task/ebill-080-schema`. It creates 5 global choices, 3 user-owned audited tables (`sprk_procedure`, `sprk_procedureversion` (delete-only cascade to its Procedure, alternate key (procedure, version number) both SystemRequired), `sprk_procedureassignment`), the `Record` 100000002 / `Firm record` 100000003 scope options and record lookups (one per `FilingCores` table, RemoveLink) plus `sprk_procedure` on `sprk_ruleset`, `sprk_procedureversion` / `sprk_publishstate` / `sprk_basedonversion` on `sprk_policyversion`, `sprk_procedureversion` on `sprk_ratecard`, `sprk_billingfirm` / `sprk_billingresponsible` on every core table, creator-person columns on `sprk_policy` and `sprk_policyversion`, and field security on 18 BFF-only columns. Tables in `notes/080-procedure-schema.md` section 2; `-WhatIf` output in section 4. Please review in full. Points to decide: (1) Restrict on the two firm lookups (6.1); (2) the revision answers your CHANGES: F1-1, F4-1, F4-2, publish list, K2, K4 (Review fixes); (3) relationship names (section 2); (4) `sprk_publishstate` name and values confirmed in this review (model 7.2).

**To uac-r2.** Same script and `-WhatIf`. Questions: (1) may the script add permission rows for the 18 columns to your "Spaarke BFF-Managed Field Readers / Writers" pair (rows only; it never changes members and stops if the pair is not as it was on 2026-10-21)? (2) We drop `sprk_createdbyperson` from `sprk_ratecard` / `sprk_ratecardrate` (rate cards are no longer tied to a core record, D-120): confirm. (3) `sprk_procedureassignment` is a secure child of its record (lineage on the three typed lookups), for your 081-side registration. (4) Your review items are done: one custom delete-only cascade per table (`sprk_excludedpolicy`, version -> Procedure; the assignment core lookups are RemoveLink after the dev run hit the one-parental-relationship limit), and the verify now reads the roles of every `sprk_isexternal` user (found on dev: Spaarke Basic User, Spaarke Console User, Spaarke Office Add In User) and fails if any holds a privilege on the three tables, so 081 / 089 must not grant those roles anything on them. (5) 081's creator-person script should list `sprk_policy` and `sprk_policyversion`.


## Dev run and after-notices (2026-10-21)

- Before-notices: ontology-r1 and uac-r2, 2026-10-21, before each attempt. Owner OK: 2026-10-21 ("1. yes as recommended").
- First attempt: refused at its first write (0x80048d19, the in-place cascade PUT had no `@odata.type`); nothing changed. Fix 11ead6c938 (Get-CascadeFixBody names OneToManyRelationshipMetadata); scoped re-check PASS from uac-r2 and ontology-r1.
- Second attempt: exit 0, created 64, changed 20, skipped 40. Verify output: `notes/080-dev-run-2026-10-21.md`.
- After-notices sent 2026-10-21. uac-r2 ACCEPTED after its own read-only read-back of the 18 secured columns. ontology-r1 confirmed its own metadata read-back matches (D-146 / #2132 holds).
- Idempotence run (2026-10-21, same script): exit 0, created 0, changed 0, skipped 86; every verify check passed.
