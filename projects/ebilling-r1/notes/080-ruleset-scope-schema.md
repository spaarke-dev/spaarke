# Task 080 - rule-set and rate-card scope schema: script, dev checks and -WhatIf (steps 1-3)

Status: script written and `-WhatIf` run against dev (2026-10-20). **Nothing was written to Dataverse.** The real run (step 4) is gated on ontology-r1's schema-review PASS (D-144) and is the main session's.

Script: `scripts/ebilling/Deploy-EbillingRuleSetScopeSchema.ps1` (841 lines; README entry added). It builds on `EbillingSchema.psm1`, copies the pre-flight / escalation shape of `Deploy-EbillingTimekeeperRateCardSchema.ps1` and the secure-then-grant (with revert) shape of `Set-EbillingFirmReasonFieldSecurity.ps1`.

## 1. Collision check on dev (read-only queries)

No POML trigger fired.

| Check | Result |
|---|---|
| `businessunit` `CanBeInManyToMany` | true (cannot be changed; managed property), so the S4 N:N is possible |
| `sprk_ruleset` / `sprk_ratecard` / matter / project / workassignment | all UserOwned, `CanBeInManyToMany` true |
| `sprk_ruleset` columns `sprk_matter`, `sprk_project`, `sprk_workassignment`, `sprk_allbusinessunits` | absent |
| `sprk_ruleset.sprk_scope` options | exactly 100000000 Client default, 100000001 Firm (local, not global). 100000002 / 100000003 are free |
| `sprk_ratecard.sprk_scope` | absent. Existing core lookups `sprk_matter` / `sprk_project` / `sprk_workassignment` / `sprk_firm` exist and read RemoveLink |
| `sprk_securedfrombusinessunit` on matter / project / workassignment | absent |
| New relationship names (`sprk_matter_ruleset_matter`, `sprk_project_ruleset_project`, `sprk_workassignment_ruleset_workassignment`, the three S8 names, N:N `sprk_ruleset_businessunit` and its intersect entity) | all absent |
| Existing `sprk_ratecard` core lookups' delete behaviour | RemoveLink (matter, project, workassignment); `sprk_organization_ruleset_firm` also RemoveLink |
| Rate cards / rule sets on dev | 0 rows each |
| Reader profile | `Spaarke BFF-Managed Field Readers` on all 6 BU default teams; writer `Spaarke BFF-Managed Field Writers` holds both BFF app users (`# mi-bff-api-dev`, `SDAP-BFF-SPE-API`) |
| Existing field permissions on `sprk_ruleset` | none |

Pre-existing, not a collision: `sprk_matter`, `sprk_project` and `sprk_workassignment` already carry `sprk_securitybu` (lookup to `businessunit`, the retired per-project BU mechanism; unpopulated; `ProvisionProjectEndpoint` reads it to refuse re-provisioning). The design note says the only business-unit column on those tables is `owningbusinessunit`; that is slightly wrong. S8 must not reuse `sprk_securitybu`, and the script does not.

## 2. Readers of `sprk_ruleset.sprk_scope` and `sprk_ruleset.sprk_firm`

Why it matters: S2 adds options to `sprk_scope` and S7 makes `sprk_scope` / `sprk_firm` field-secured (Read for everyone through the reader profile; only the BFF application users write). A reader that is not covered by the reader profile would get null.

Server (BFF):
- `Services/Finance/RuleSetService.cs`: reads and writes both columns (lines 320 column set, 387-395 validation, 450-458 create, 1542-1543, 1944-1946 write, 2052-2053 firm clause, 2237-2243). Writes run as the BFF application user, which the writer profile holds.
- `Services/Signals/Billing/BillingFactLoader.cs` (lines 284-311): filters and reads `sprk_scope` and `sprk_firm` to load the rule sets for an invoice's firm (D-39: firm overrides client default). The new `Record` / `Firm record` values (100000002/3) are NOT handled there yet: tasks 082-083 own that. Until then those rows are simply not matched, which is the safe direction.
- `Services/Jobs/Handlers/BillingRuleDraftJobHandler.cs`, `RuleDomain.cs`, `InvoiceComplianceEvaluator.cs`, `BillingRuleDraftTranslator.cs`, `RuleSetEndpoints.cs`: mention the rule-set type or call `RuleSetService`; no direct `sprk_scope` / `sprk_firm` column access found.
- `Services/Ai/Chat/AnalysisChatContextResolver.cs:601` and `DynamicCommandResolver.cs:367`: query a different table that is itself named `sprk_scope`. Not a reader of the column.

Dataverse (read-only dev query):
- View `Active Rule Sets` (`f8fe410f-1840-4796-af0a-a1bb2f1da2ea`): shows both columns. Under field security a user without the reader profile sees `*****`; every BU default team has it.
- Main form `Information` (`bafad1a6-ace0-42ad-bc71-b9df4ecbd8eb`, type 2): shows both columns. Same.
- The other six views (Quick Find, Advanced Find, Associated, Inactive, Lookup, My) and the two other forms (types 6 and 11): name neither column.
- No personal views, charts, workflows, cloud flows or business rules on `sprk_ruleset`; no other table's view or form mentions `sprk_ruleset`; no web resource reads it.

Scripts: `scripts/ebilling/Deploy-EbillingFormsViews.ps1` (:112-114) lists both columns in the rule-set form and view spec; `Seed-BillingSystemPolicy.ps1` and `Deploy-EbillingPolicyStoreSchema.ps1` use `sprk_ruleset` only as a lookup on `sprk_policy`. Nothing there reads the columns' values.

Other: `sprk_allbusinessunits` is empty (not Yes) on rule-set rows that exist before the column is created; the loader (task 082) must read empty as Yes. The script reports the count and does not write.

## 3. Backfill list (`sprk_ratecard.sprk_scope`)

The rule: only rows whose scope is empty; never overwrite a set value.
- Exactly one of `sprk_matter` / `sprk_project` / `sprk_workassignment` set: **Core record** (100000001).
- None of the three set: **Firm** (100000000). The firm lookup is required on a rate card, so a card with no core lookup is a firm-wide card. If auditing were on, a card whose core lookup was cleared by the RemoveLink on a deleted record would be listed as ORPHANED and marked **Core record** (it applied to one record that is gone, so it must apply to nothing, not to the whole firm).
- Two or three core lookups set: **left empty and listed** (ambiguous; the owner decides).
- Inactive (`statecode` 1): **left empty and listed** (Dataverse refuses to update an inactive row).

On dev: **0 rate cards**, so 0 Firm, 0 Core record, 0 left empty. Auditing is OFF for `sprk_ratecard`, so the orphan check cannot run there (the script says so and lists every no-lookup card for the owner). The apply path and the audit path were exercised only against a mocked Dataverse (7 synthetic rows: 2 already set, 1 Core record, 1 Core record orphaned, 1 Firm, 2 left empty), not against real data. The real-run read-back counts the still-empty active cards and warns (does not fail) when a card appeared meanwhile; re-run with `-BackfillOnly`.

## 4. Deviations and items for ontology-r1

- Option labels are my choice (the design note names values only for scope): `sprk_ruleset.sprk_scope` 100000002 "Record", 100000003 "Firm record"; `sprk_ratecard.sprk_scope` "Applies to": 100000000 "Firm", 100000001 "Core record".
- S8 relationship names are my choice (the design note is silent): `sprk_businessunit_{matter|project|workassignment}_securedfrombusinessunit`. The module's `Add-Lookup` cannot set `AssociatedMenuConfiguration` to DoNotDisplay (uac-r2's script does); not extended.
- Legacy `sprk_securitybu` exists on the three core tables (see section 1); S8 is a separate column on purpose.
- Delete behaviour: RemoveLink on all seven new/touched lookups, no Restrict (D-172). The pre-flight escalates (exit 2) if an existing core lookup is not RemoveLink.
- Field security: the script joins uac-r2's existing pair and adds permission rows only; it never changes members of an existing profile (`-CreateProfiles` makes an ebilling-owned pair instead). uac-r2's agreement to reuse their pair is still needed (task step 4, main session). S8 snapshot columns get the writer profile only, no reader, on no form and no view (D-141).
- Securing `sprk_ruleset.sprk_scope` (a Picklist) uses a typed GET-then-PUT with a one-time `$expand=OptionSet` fallback; the post-run verify checks the options remain exactly 100000000-100000003. Not exercisable without a write, so this is the highest-risk step in the real run.
- Ownership / placement: all new components are created with the SpaarkeMaster solution header; the five tables are whole-table components of SpaarkeMaster, and matter / project / workassignment also of SpaarkeCore. SpaarkeMaster source is not edited here (task 079 syncs it).
- Dev has no rate cards and no rule sets, so the backfill and the "existing rows" notes are untested on real data.

- **uac-r2 review 2026-10-20 items 081.3 / 086.3 (scope change from the main session):** the script also creates `sprk_createdbyperson` on `sprk_ruleset`, `sprk_ratecard` and `sprk_ratecardrate` (step 6b). This REVERSES the POML negative criterion "no sprk_createdbyperson column was added by this script" for those three tables; it still holds for `sprk_matter`, `sprk_project` and `sprk_workassignment`, whose only new column is the S8 snapshot. Shape copied from ebilling task 013 (`Deploy-EbillingSubmissionSchema.ps1:182-183`, same in the Approval / Correction / ExternalIdentifier scripts): lookup to `systemuser`, schema `sprk_CreatedByPerson`, display "Created By (Person)", RequiredLevel None, RemoveLink, relationship `sprk_systemuser_{table}_createdbyperson` (also what `Set-ChildRecordCreatorPersonSchema.ps1` expects), the same description text. Created UNSECURED like 013; securing it is the creator-person script's job (081 / 086 add the tables). Dev check before writing: the column and the three relationships are absent on all three tables (none exists, so no "already exists" report). `Set-ChildRecordCreatorPersonSchema.ps1` and its Pester test are NOT edited. Publish list gained `sprk_ratecardrate`; verify checks the three relationships read RemoveLink.

## 5. `-WhatIf` run (dev, exit 0)

Command: `pwsh -NoProfile -File scripts/ebilling/Deploy-EbillingRuleSetScopeSchema.ps1 -WhatIf` (re-run after the S9 scope change; created 27, changed 8, skipped 1)

```text

eBilling task 080 - rule set and rate card scope schema (S1-S4, S6-S8)
Environment: https://spaarkedev1.crm.dynamics.com   Solution: SpaarkeMaster   Mode: WHATIF (no writes)
Profiles   : reader 'Spaarke BFF-Managed Field Readers', writer 'Spaarke BFF-Managed Field Writers' (members are never changed on an existing profile)

0. Pre-flight (read-only)
  businessunit CanBeInManyToMany = true; sprk_ruleset CanBeInManyToMany = True
  sprk_ruleset.sprk_scope options now: 100000000=Client default, 100000001=Firm
  no existing item conflicts; every existing core lookup reads RemoveLink
  field security profiles:
  OK       reader profile 'Spaarke BFF-Managed Field Readers' is on default team 'Secure Record' (BU 'Secure Record')
  OK       reader profile 'Spaarke BFF-Managed Field Readers' is on default team 'Spaarke Business Unit 1' (BU 'Spaarke Business Unit 1')
  OK       reader profile 'Spaarke BFF-Managed Field Readers' is on default team 'Spaarke Demo' (BU 'Spaarke Demo')
  OK       reader profile 'Spaarke BFF-Managed Field Readers' is on default team 'Spaarke Dev 1' (BU 'Spaarke Dev 1')
  OK       reader profile 'Spaarke BFF-Managed Field Readers' is on default team 'Spaarke Test 1' (BU 'Spaarke Test 1')
  OK       reader profile 'Spaarke BFF-Managed Field Readers' is on default team 'Spaarke' (BU 'Spaarke')
  OK       writer profile 'Spaarke BFF-Managed Field Writers' holds BFF application user '# mi-bff-api-dev'
  OK       writer profile 'Spaarke BFF-Managed Field Writers' holds BFF application user 'SDAP-BFF-SPE-API'
1. S1 sprk_ruleset core-record lookups (RemoveLink)
  WOULD CREATE sprk_matter (Lookup -> sprk_matter)
  WOULD CREATE sprk_project (Lookup -> sprk_project)
  WOULD CREATE sprk_workassignment (Lookup -> sprk_workassignment)
2. S2 sprk_ruleset.sprk_scope options: Record = 100000002, Firm record = 100000003
  WOULD INSERT 100000002 Record
  WOULD INSERT 100000003 Firm record
3. S3 sprk_ruleset.sprk_allbusinessunits (two-option, default Yes)
  WOULD CREATE sprk_allbusinessunits (Boolean)
  note: existing sprk_ruleset rows will hold no value in sprk_allbusinessunits after the create; the loader must read empty as Yes. Not written here.
4. S4 N:N sprk_ruleset_businessunit (sprk_ruleset <-> businessunit)
  WOULD CREATE N:N sprk_ruleset_businessunit (sprk_ruleset <-> businessunit)
5. S6 sprk_ratecard.sprk_scope (Applies to: Firm / Core record)
  WOULD CREATE sprk_scope (Picklist)
6. S8 sprk_securedfrombusinessunit on the three core tables (lookup -> businessunit, RemoveLink, field-secured, no form, no view)
  WOULD CREATE sprk_securedfrombusinessunit (Lookup -> businessunit)
  WOULD CREATE sprk_securedfrombusinessunit (Lookup -> businessunit)
  WOULD CREATE sprk_securedfrombusinessunit (Lookup -> businessunit)
6b. S9 sprk_createdbyperson on sprk_ruleset, sprk_ratecard, sprk_ratecardrate (lookup -> systemuser, RemoveLink; created unsecured, as ebilling task 013 did)
  WOULD CREATE sprk_createdbyperson (Lookup -> systemuser)
  WOULD CREATE sprk_createdbyperson (Lookup -> systemuser)
  WOULD CREATE sprk_createdbyperson (Lookup -> systemuser)
  NEXT: run scripts/Set-ChildRecordCreatorPersonSchema.ps1 (tasks 081 / 086 add these tables to it) so sprk_createdbyperson is field-secured (only the BFF may write it).
7. S7 / S8 field security (reader: Read for every user; writer: BFF application user(s) only)
  WOULD SECURE sprk_ruleset.sprk_scope
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_scope (read=4 create=0 update=0)
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_scope (read=4 create=4 update=4)
  WOULD SECURE sprk_ruleset.sprk_firm
  WOULD GRANT Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_firm (read=4 create=0 update=0)
  WOULD GRANT Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_firm (read=4 create=4 update=4)
  WOULD SECURE sprk_ruleset.sprk_matter after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_ruleset.sprk_project after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_ruleset.sprk_workassignment after it is created, then grant: Spaarke BFF-Managed Field Readers (read=4 create=0 update=0); Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_matter.sprk_securedfrombusinessunit after it is created, then grant: Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_project.sprk_securedfrombusinessunit after it is created, then grant: Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  WOULD SECURE sprk_workassignment.sprk_securedfrombusinessunit after it is created, then grant: Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_ruleset.sprk_scope
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and System Administrator can write sprk_ruleset.sprk_firm

S6 backfill: sprk_ratecard.sprk_scope on existing rate cards (only rows whose scope is empty; never overwrites a set value)
  note: sprk_ratecard.sprk_scope does not exist yet (WhatIf): every card is treated as having an empty scope.
  auditing is OFF for sprk_ratecard (or its core lookups): the orphaned-card check CANNOT run; every card with no core lookup is marked Firm and listed for the owner.
  rate cards read: 0
  already set (left alone): 0
  WOULD MARK Core record (exactly one core lookup): 0
  WOULD MARK Core record, ORPHANED (no lookup now; audit history shows one was cleared): 0
  WOULD MARK Firm (no core lookup) - THE OWNER CHECKS THIS LIST BEFORE TASK 085: 0
  LEFT EMPTY and listed (not set): 0
Publish
  WhatIf: publish skipped

================ Summary ================
created : 27
    + sprk_ruleset.sprk_matter
    + sprk_ruleset.sprk_project
    + sprk_ruleset.sprk_workassignment
    + sprk_ruleset.sprk_scope=100000002
    + sprk_ruleset.sprk_scope=100000003
    + sprk_ruleset.sprk_allbusinessunits
    + N:N sprk_ruleset_businessunit
    + sprk_ratecard.sprk_scope
    + sprk_matter.sprk_securedfrombusinessunit
    + sprk_project.sprk_securedfrombusinessunit
    + sprk_workassignment.sprk_securedfrombusinessunit
    + sprk_ruleset.sprk_createdbyperson
    + sprk_ratecard.sprk_createdbyperson
    + sprk_ratecardrate.sprk_createdbyperson
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_scope
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_scope
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_firm
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_firm
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_matter
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_matter
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_project
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_project
    + field permission Spaarke BFF-Managed Field Readers on sprk_ruleset.sprk_workassignment
    + field permission Spaarke BFF-Managed Field Writers on sprk_ruleset.sprk_workassignment
    + field permission Spaarke BFF-Managed Field Writers on sprk_matter.sprk_securedfrombusinessunit
    + field permission Spaarke BFF-Managed Field Writers on sprk_project.sprk_securedfrombusinessunit
    + field permission Spaarke BFF-Managed Field Writers on sprk_workassignment.sprk_securedfrombusinessunit
changed : 8
    ~ secure sprk_ruleset.sprk_scope
    ~ secure sprk_ruleset.sprk_firm
    ~ secure sprk_ruleset.sprk_matter
    ~ secure sprk_ruleset.sprk_project
    ~ secure sprk_ruleset.sprk_workassignment
    ~ secure sprk_matter.sprk_securedfrombusinessunit
    ~ secure sprk_project.sprk_securedfrombusinessunit
    ~ secure sprk_workassignment.sprk_securedfrombusinessunit
skipped : 1
    = sprk_ratecard.sprk_scope backfill (no rate cards)
WhatIf run: nothing was written.
```
