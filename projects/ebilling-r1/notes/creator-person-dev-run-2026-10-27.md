# Creator-person schema run on dev, 2026-10-27 (081, 090)

Owner OK: 2026-10-27 (EB-D-153). Script: `scripts/Set-ChildRecordCreatorPersonSchema.ps1` at 842c0be38d.
Parameters: `-EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -OntologyWriterApplicationId 69040982-612e-469e-a85f-26d5172367c5`.

## Notices
- Before: uac-r2 (no objection; conditions: send the dry-run lines for the ten ebilling tables before -Apply, the guard-test entries in code, the -Verify lines after) and ontology-r1 (no objection; asks: entity-scoped publish, confirm no ontology path must set the column, send its tables' -Verify lines). uac-r2 cleared -Apply after reading the dry-run lines.
- After: both sent with the results below.

## Results
- **Dry run:** no FAIL, no MISSING. The six tables below held no value in the column (empty-before-securing gate passed); 0 app-created rows on any of them.
- **-Apply (exit 0):** secured `sprk_createdbyperson` and granted Spaarke BFF-Managed Field Readers (read 4) and Writers (read, create, update 4) on sprk_procedure, sprk_procedureversion, sprk_policy, sprk_policyversion, sprk_ruleset, sprk_procedureassignment. The field-permission service needed 1-2 retries on five tables before it saw the column as secured. One entity-scoped PublishXml over the 35 child tables (no PublishAll).
- **-Verify (exit 0):** VERIFY PASS, 272 OK lines. The four invoice-family tables (submission, approval, external identifier, correction) were already secured and are unchanged. The 25 UAC and ontology tables' lines are identical to the dry run. Writers profile: only the two BFF application users. The ontology writer profile has no permission on the Procedure or policy tables.
- **Guard:** `CreatorPersonColumnSecuredGuardTests.SecuredOnlyByDeployScript` already lists the six tables (uac-r2 090 K1).

## Consequence for ontology
Ontology authoring creates and updates sprk_policy / sprk_policyversion without the column; that is allowed. Such policies record no person, so F3 admits only Full Access holders for them. Stamping them would be an ontology task plus a uac-r2 review, not schema.
