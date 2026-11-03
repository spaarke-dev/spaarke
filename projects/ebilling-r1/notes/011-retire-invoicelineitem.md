# Task 011 - retire sprk_invoicelineitem

> **Status: IN PROGRESS - the dev delete is NOT done.** Escalation trigger 1 fired: the table holds 4 rows in dev. The delete waits for the owner's decision on those rows and the explicit OK (project CLAUDE.md section 5). Everything else is done.
> **Date**: 2026-10-15/16. **Environment**: dev (spaarkedev1). **Script**: `scripts/ebilling/Remove-EbillingInvoiceLineItem.ps1`.

## 1. Reference grep (step 1)

`git grep -i invoicelineitem` over the whole tree, excluding `projects/` and `src/dataverse/solutions/`, before the edits:

| Hit | Matches the POML inventory? | Done |
|---|---|---|
| `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs:52` (`ChildTables`) | yes | entry removed; remark added |
| `scripts/Migrate-DataverseData.ps1:116` | yes | removed from `$Tier3_Dependent` |
| `docs/data-model/sprk_financial-related-entities.md:286-287` | yes (the POML said "the file"): the two `sprk_timekeeper` rows (lookup + name companion) | rows removed; roster paragraph reworded |
| `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md:744-746` | yes | example redirected to `sprk_billingevent` (Invoice and Matter drill-throughs); warning rewritten |
| `docs/procedures/production-release.md:114` | yes | removed from the Financial list |
| `docs/data-model/sprk_ratecard.md:64` | **new** (written by task 017 after the inventory) | reworded |
| `src/server`, `src/client`, `src/solutions`, `.github`, `config`, `infra`, `infrastructure`, `.claude` | no hit (inventory confirmed: no BFF or client code reads it) | - |

After the edits the grep over the same scope finds only the retirement script, its README entry and nothing else (the script has to name the table). Not matched by the token but related, left alone: `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md:387` writes `sprk_invoice_line_item` (an old design sentence about form write-back creating records), and `docs/data-model/entity-relationship-model.md:366` calls `sprk_billingevent` "Invoice line item". Neither is a reference to the table.

Other projects' notes mention the table (`spaarkeai-word-add-in-r1/notes/080-record-ownership.md`, `unified-access-control-r2/notes/task-146-server-child-writers.md`); they are history, not edited.

## 2. Dev inventory (step 2, read-only, 2026-10-15)

Table `sprk_invoicelineitem`: user-owned, unmanaged, MetadataId `82bc500e-1300-f111-8406-7c1e520a42cd`, ObjectTypeCode 10873, entity set `sprk_invoicelineitems`. Member of 6 unmanaged solutions (Cr2b7d5, Default, SpaarkeCore, SpaarkeMasterTest2, SpaarkeMaster, SPRKDOCINTELLIGENCE). Custom columns: `sprk_amount` (money), `sprk_assignedtoexternal`, `sprk_assignedtointernal` (contact lookups), `sprk_billingevent` (lookup), `sprk_invoice` (lookup), `sprk_name`, `sprk_recordsummary` (memo).

### Rows: 4 (not zero)

| Id | Name | Created | Invoice | Billing event |
|---|---|---|---|---|
| `ed1c6d8b-1f0c-f111-8342-7ced8d1dc988` | Line item #1 | 2026-02-17 | "Invoice Number 3 Test Record" | none |
| `0815a065-e10c-f111-8342-7ced8d1dc988` | Line item #1 | 2026-02-18 | "Invoice Number 4 Test Record" | none |
| `2f2ce1d3-2b48-f111-bec7-7ced8d1dc988` | Attorney Fees (amount 1000) | 2026-05-05 | INV-002 Commercial Real Estate | `5ea5ad2a-...` "Document Review" |
| `75945818-9e9c-f111-b8de-7ced8ddc4cc6` | Line Item 1 (amount 1500) | 2026-08-20 | INV-005 | `5ea5ad2a-...` "Document Review" |

All four are active, owned by the same user, on invoices whose names mark them as test data. `sprk_billingevent` holds 1 row ("Document Review", no amount), so nothing was migrated to the new Invoice Line table and nothing refers to these amounts. Other environments: none named by the owner, none checked.

### Dependencies

`RetrieveDependenciesForDelete` for the table: 6 dependents, all expected.

| Kind | What | Removal in the script |
|---|---|---|
| System form (type 60) | `sprk_billingevent` "Billing Event main form" `{54b1c4d9-...}`: tab `tab_invoicelineitems` with a subgrid, plus a hidden navigation entry | subgrid section, empty tab and navigation entry removed from the form XML, published |
| System form (type 60) | `sprk_invoice` "Invoice main form" `{93aa1c69-...}`: section `overview_section_invoicelineitems` with a subgrid, plus a hidden navigation entry | same |
| Relationship (type 10) | `sprk_InvoiceLineItem_Timekeeper_1n` (`sprk_timekeeper.sprk_invoicelineitem`; 0 timekeeper rows use it) | relationship delete (the column and its name companion go with it) |
| AI skill config (type 10314) x2 | `ais_FormFillFieldOptOut_sprk_invoicelineitem_sprk_recordsummary`, `..._sprk_amount` | delete |
| App module (type 80) | Spaarke Platform (`sprk_SpaarkePlatform`); the table has 6 `appmodulecomponent` registrations | delete the 6 registrations (same method as `scripts/Delete-SprkEventTodoEntity.ps1`) |

Not tracked as a dependency but found by the form scan: the `sprk_billingevent` "Information" form `{fc621f4e-...}` carries the hidden navigation entry for `sprk_BillingEvent_InvoiceLineItem_1n`; the script cleans it too.

Nothing else: no view of another table, no personal view, no workflow or flow (primary entity or XAML / client data), no site map, no chart, no grid configuration, no other lookup targeting the table (only the timekeeper one), no many-to-many. 13 many-to-one and 9 one-to-many relationships belong to the table itself (system ones plus 4 custom lookups: invoice, billing event, two contacts), 8 security privileges (`prv...sprk_InvoiceLineItem`, 9 roles hold create/read; they go with the table and the role XML is refreshed by 029). Own components that go with it: 5 forms, 7 views. 38 plug-in step registrations on the table are all platform steps ("ObjectModel Implementation" and the Microsoft data-archival plug-ins); there is no custom plug-in.

### Row-count and dependency evidence for the owner

Run the read-only report any time: `pwsh scripts/ebilling/Remove-EbillingInvoiceLineItem.ps1`. It ends `VERDICT: BLOCKED - 4 row(s) ...` today.

## 3. Script tests (all against dev, none changed anything)

| Run | Result |
|---|---|
| report (no switch) | exit 0, inventory above, BLOCKED (4 rows) |
| `-Delete -OwnerApproval x` (rows present) | **exit 2**, "Refusing to delete ...: 1 blocker(s)", nothing changed |
| `-Delete` without `-OwnerApproval` | exit 1 (refuses before reading) |
| `-DeleteRows` without `-Delete` | exit 1 |
| `-Delete -DeleteRows -OwnerApproval x -WhatIf` | exit 0, VERDICT READY (the four rows are the known four); the plan: backup, 3 form edits (the XML transform ran for real on the live form XML and left 0 mentions), relationship, 2 AI skill configs, 6 app registrations, 4 rows, 7 views, table; no write |

Review fixes (adversarial pass, F2): the pre-flight now also runs the form transform on the three known forms and checks the timekeeper relationship name, so every refusal (exit 2, nothing changed) happens before the first write; the table's row count is re-read right before the final delete (a row created meanwhile refuses and names the steps already run); entity-scoped publishes run unconditionally (sprk_billingevent and sprk_invoice after the form edits, sprk_timekeeper after the lookup, all three after the delete); `-Delete` refuses an environment that came from `$env:DATAVERSE_URL` unless it is spaarkedev1 or `-EnvironmentUrl` is passed; a section / tab with no parent is skipped; dead `Get-Count` removed. Re-tested: report (BLOCKED, 4 rows), `-Delete` refused exit 2, env guard exit 2, `-WhatIf` READY exit 0.

The destructive path itself (steps 1-9 for real) was **not run** - it is the owner-gated step.

## 4. Repo changes (step 3) and ArchTests

`dotnet test tests/Spaarke.ArchTests` before: 1052 passed, 0 failed. After the edit: 1052 passed, 0 failed. Control: with `sprk_billingevent` temporarily dropped from `ChildTables`, `SecureFamilyConsistencyTests` fails (2 tests: the lineage / role set / census agreement and its negative control), proving the list is still enforced; restored. Only the `sprk_invoicelineitem` entry was removed; nothing in `src` ever created the table, so no census entry changed. **uac-r2 owns this guard: mention the one-entry edit in the PR and ask uac-r2 to review.**

## 5. After the owner's OK - the one command

Decision needed first (see the questions in the task report): are the 4 test rows disposable?

```powershell
# rows disposable (backed up to a JSON file first):
pwsh scripts/ebilling/Remove-EbillingInvoiceLineItem.ps1 -Delete -DeleteRows -OwnerApproval '<date> <who gave the OK>'
# rows already deleted or moved by the owner:
pwsh scripts/ebilling/Remove-EbillingInvoiceLineItem.ps1 -Delete -OwnerApproval '<date> <who gave the OK>'
```

Optionally `-WhatIf` first. Exit 0 = done and verified (entity 404; the three tables' main forms load and no longer mention the table). Exit 2 = refused, nothing changed (or a dependency stayed after the removals: the message says so). Exit 1 = error; re-run. Then quote the owner's OK, the date and the run output here, set the POML status to completed, tick the index (the main session does this) and record the line below.

## 6. Solution-source files task 029's sync must drop (not edited here)

All under `src/dataverse/solutions/SpaarkeMaster/`. Re-export from dev after the delete; the following should disappear or lose their references.

- `Entities/sprk_InvoiceLineItem/` (whole folder: `Entity.xml`, 7 `SavedQueries/*.xml`, forms)
- `Entities/sprk_Timekeeper/Entity.xml` (the `sprk_invoicelineitem` and `sprk_invoicelineitemname` attributes)
- `Entities/sprk_BillingEvent/FormXml/main/{54b1c4d9-cebc-f111-aaad-7c1e520a989f}.xml` and `_managed.xml` (tab and navigation entry), `{fc621f4e-ff20-4e50-9ec4-e6e758b909fc}.xml` and `_managed.xml` (navigation entry)
- `Entities/sprk_Invoice/FormXml/main/{93aa1c69-0406-f111-8406-7c1e525abd8b}.xml` and `_managed.xml` (section and navigation entry)
- `Other/Relationships.xml` and `Other/Relationships/{BusinessUnit,Contact,Owner,SystemUser,Team,TransactionCurrency,sprk_BillingEvent,sprk_Invoice,sprk_InvoiceLineItem}.xml`
- `Other/Solution.xml` (root component and the entity)
- `Roles/Spaarke Basic User.xml`, `Roles/Spaarke Core User.xml`, `Roles/Spaarke Ontology Service.xml` (the `prv*sprk_InvoiceLineItem` privileges)
- `aiskillconfigs/ais_FormFillFieldOptOut_sprk_invoicelineitem_sprk_recordsummary/` and the sibling `..._sprk_amount` config if it is exported
- Other unmanaged solutions in dev that hold the table (Cr2b7d5, Default, SpaarkeCore, SpaarkeMasterTest2, SPRKDOCINTELLIGENCE) lose it with the delete; no source for them in this repo mentions it.

## 7. Deviations

- The delete was not run and the task stays `in-progress` (escalation trigger 1: rows exist; trigger 3: no owner OK).
- The script also edits `{fc621f4e-...}` (not in the dependency list) and takes `-OwnerApproval` / `-DeleteRows` switches that the POML did not specify: the first is needed so the exported forms do not keep a dangling navigation entry (same reason 017 cleaned the Invoice form), the second is the single guarded way to include the rows once the owner decides.
- The grep acceptance criterion cannot read "nothing" while the retirement script exists: the script, its README entry and file name carry the token by necessity.

## 8. Run status (dev spaarkedev1, 2026-10-15)

Owner OK: "Yes, delete with rows" (Ralph Schroeder, 2026-10-15, Claude session).

Done in dev:
- the 4 rows were backed up to `notes/011-invoicelineitem-rows-backup.json`;
- the Billing Event and Invoice forms were edited and published;
- the Timekeeper lookup `sprk_InvoiceLineItem_Timekeeper_1n` was deleted;
- the 2 AI skill-config opt-outs were deleted;
- the 2 Copilot table-search entries (`dvtablesearchentity`) naming the table were deleted.

Not done: the 4 rows and the table still exist. One dependency remains. The app `Spaarke Platform` (`sprk_SpaarkePlatform`, appmoduleid `d908a85b-9454-f111-a825-7c1e520aa4df`) still lists the table, and the script stops at step 5 and names it:
- `RemoveAppComponents` returns 204 but changes neither the working set nor the published set. Both entity-reference forms (typed `entityid`, and `@odata.id`) were tried, with and without deleting the table-search entries first.
- The app is unmanaged, its validation shows warnings only, and its sitemap does not mention the table.
- A solution export of the app shows `<AppModuleComponent type="1" schemaName="sprk_invoicelineitem" />`. The app bundles Copilot bots, an MCP server and table-search indexes, so importing an edited copy was not attempted.

To finish:
1. In make.powerapps.com, open the Spaarke Platform app in the designer, remove the Invoice Line Item table, then Save and Publish.
2. Re-run the script with `-Delete -DeleteRows -OwnerApproval '2026-10-15 owner (Ralph Schroeder) via Claude session'`. Steps 0-4 are idempotent.

**Completed 2026-10-16.** The owner removed the table in the maker portal. The table delete took the 4 rows with it; their backup is kept in `notes/011-invoicelineitem-rows-backup.json`.

Verified against dev:
- `EntityDefinitions(LogicalName='sprk_invoicelineitem')` returns 404.
- The `sprk_timekeeper.sprk_invoicelineitem` lookup returns 404.
- No 1:N relationship from billing event, invoice or timekeeper points to it.
- No system form, view or sitemap mentions it.
- The Spaarke Platform app is republished with 96 components, the table is not among them, and ValidateApp reports 0 errors.

Six `appmodulecomponent` rows still name the deleted table's id. All six belong to app versions that no longer exist, so they are platform leftovers and are not cleaned up.
