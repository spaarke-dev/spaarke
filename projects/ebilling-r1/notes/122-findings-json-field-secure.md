# Task 122: field-secure sprk_findingsjson, generic Duplicate sentence in Decision Records (#2235, DEF-029)

> Steps 1 to 5 done 2026-10-23 on `task/ebill-122-findings-json` (base `a05b5dfa0c` on `work/ebilling-r1`, which has 0bba46c1ba, #2294 / DEF-043). Step 6 (live dev run) waits for ontology-r1's review of the script and its -WhatIf, the notices, and the owner's OK.

## 1. Inventory: every reader and writer of `sprk_invoicesubmission.sprk_findingsjson`

Searched: `sprk_findingsjson`, `SubmissionFindingsJson` and `findingsjson` (case-insensitive) over the whole repository except `projects/`; the dev environment's field-mapping rules, AI topic-registry rows, email update fields and forms (the script's (p4) and (p6)).

**Identity.** `IGenericEntityService` is `DataverseServiceClientImpl` (`Infrastructure/DI/GraphModule.cs:145`), the BFF's application identity with no impersonation. On dev that is `# mi-bff-api-dev` (appId 5967251e-...); `SDAP-BFF-SPE-API` (1e40baad-...) is the other BFF application user. Both are members of "Spaarke BFF-Managed Field Writers" and both hold System Administrator. The caller-identity client is `IDataverseUserClient` (OBO).

### BFF (all app-only)

| # | Reader / writer | file:line (`src/server/api/Sprk.Bff.Api/Services/Finance/`) | What | Identity | Runs from |
|---|---|---|---|---|---|
| 1 | `InvoiceSubmissionPersister` | `Submission/InvoiceSubmissionPersister.cs:872` (`BuildSubmissionUpdate`), sent at `:424` (`_entities.ExecuteTransactionAsync`) | write | app-only (`IGenericEntityService`) | submission processing job |
| 2 | `InvoiceSubmissionAfterSteps` | read: `Submission/InvoiceSubmissionAfterSteps.cs:87` (`SubmissionColumns`), `:941` (`_entities.RetrieveAsync`), `:300` (value used); write: `:673`, `:825`, `:1108` (`_entities.UpdateAsync` at `:671`, `:823`, `:1120`) | read and write | app-only | job (part D) |
| 3 | `AdminAttentionText.NoteAsync` | `Submission/AdminAttentionText.cs:239-243` (`entities.RetrieveAsync`, then parse) | read | app-only (the `IGenericEntityService` its callers pass: `InvoiceSubmissionAfterSteps.cs:1272`, `Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs:418`, `:461`) | job |
| 4 | `SubmissionOverrideService` | read: `SubmissionOverrideService.cs:270` (`SubmissionColumns`), `:1260` (`_entities.RetrieveAsync` in `ReadSubmissionAsync`), values at `:444` (GET /blocked), `:684` and `:807` (override / return); write: `:790` (`BulkUpdateIfRowVersionsMatchAsync`), `:871` | read and write | app-only, after `FinanceAuthorizationFilter` (caller Read on the submission) and step 0 | `GET /api/finance/submissions/{id}/blocked`, `POST .../override`, `.../return` |
| 5 | `InvoiceReviewReadService` | `InvoiceReviewReadService.cs:358` (`SubmissionColumns`), `:497` (`_entities.RetrieveMultipleAsync`), `:504` (parse) | read | app-only, after the route's filter (caller Read on the invoice) | `GET /api/finance/invoices/{id}/review` |

Not readers: `DuplicateDetailGate.cs`, `StoredSignalSetCheck.cs` and `IInvoiceSubmissionAfterSteps.cs` name the column only in doc comments. `InvoiceSubmissionService` (`GET .../invoice-submissions/{id}`) excludes it from its view columns. The caller client is used in #4 and #5 only for the D-176 earlier-invoice check (`DuplicateDetailGate` → `EarlierInvoiceReadability`, reads of `sprk_invoice`), never for the submission row. No BFF read selects all columns of `sprk_invoicesubmission`.

### Outside the BFF

| Surface | Finding |
|---|---|
| Main form "Information" (`FormXml/main/{c412f942-...}.xml` and `_managed.xml`, was `:190`) | Bound the column. **Removed** in source (task 122); the section is relabelled "Firm notice". The live dev form still binds it until the next SpaarkeMaster import or `Deploy-EbillingFormsViews.ps1` run; once secured it shows masked to users outside the profile. |
| `scripts/ebilling/Deploy-EbillingFormsViews.ps1:122` | Generated that form section. **Updated** so a rerun cannot put the column back. View columns never included it. |
| `scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1:164` | Creates the column (unsecured). Unchanged: the new script secures it after. |
| Code pages, PCF, external SPA, Office add-ins (`src/client`, `src/solutions`) | None. |
| Playbooks, Action mirrors, output mappings (`infra/`, `src/`) | None. |
| Dev maker configuration (field-mapping rules, AI topic registry, email update fields) | None (script (p4), 2026-10-23). |
| Dev data | 0 `sprk_invoicesubmission` rows at all (2026-10-23), so nothing is stored in the column yet. |

**Escalation trigger 1 did not fire:** no reader runs as the caller. **Trigger 2 did not fire:** both BFF application users are members of the writer profile, and no default team holds it (dev -WhatIf below). **Trigger 3:** #2294 is already on the base (0bba46c1ba); nothing added.

## 2. Decision Records: the generic sentence for a Duplicate

`InvoiceSubmissionAfterSteps.RecordSentence(StoredPlanEntry)`: a plan entry with any reason code starting `duplicate.` (the prefix `SignalReadService` keys a Duplicate on) stores `BillingRuleCatalogue.WithheldDuplicateTemplate` (= `EarlierInvoiceReadability.WithheldSentence`) as `reasons[].sentence`; any other entry stores its plan sentence. Used by `BuildRejectionDecision` (reject) and `SubmissionOverrideService.BuildDecision` (override, return). No new text.

Tests: `InvoiceSubmissionAfterStepsTests.TheRejectionDecision_StoresTheWithheldSentence_ForADuplicateReason` (3 codes) and `SubmissionOverrideServiceTests.BuildDecision_StoresTheWithheldSentence_ForADuplicateReason` (3 codes × override / return). Each asserts the Duplicate sentence is the template, the non-Duplicate sentence is unchanged, and the serialized facts name neither the earlier tracking number nor its invoice id. Seed: with `p.Sentence` restored in both builders, all 9 fail; restored, all 9 pass.

**Forward-only (K4 in section 7).** The change applies to records written from now on. Records already written keep their sentence; nothing rewrites them. The billing record's content hash covers the facts, so a decision whose record an older BFF wrote with a Duplicate plan sentence, retried by this BFF, is refused as "same id, other content" (`DecisionRecordWriter.cs:1339`). Dev has no submissions, so no such record exists; nothing is released.

## 3. The app-only-read guard

`tests/Spaarke.ArchTests/FindingsJsonAppOnlyReadGuardTests.cs`, over the compiled IL (`IlCallScan`). No type expresses "app-only", and no existing scan covers column reads, so the rule is a new ArchTest (§16 ladder).

- **C1:** the types that load a string naming the column (Sprk.Bff.Api and Spaarke.Dataverse) are exactly the five above. A new reader fails until it is reviewed and listed; a reviewed type that stops naming it also fails.
- **C2:** no reviewed type calls a caller-identity seam: an `IDataverseUserClient` member, a member of an `Impersonated*` type, an `*AsCaller*` method, or a task-returning method with an `impersonat*` / `callerSystemUserId` parameter.
- **C3:** each reviewed type references `IGenericEntityService`.
- **Controls:** C1 must-fire (a planted reader) and must-not-fire (another column); C2 must-fire for each of the four shapes; C2 must-not-fire and C3 must-fire for an app-only read.
- **Seeding proof:** a `_user.GetAsync(".../sprk_invoicesubmissions?$select=sprk_findingsjson")` added to `InvoiceReviewReadService.ReadSubmissionAsync` fails C2 (1 of 9); restored, 9 pass.
- **Not covered** (in the class remarks): C2 is not transitive; a column array read by another type through a field load; a caller seam named outside the C2 rules; all-columns reads.

## 4. Schema

`scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1` (-WhatIf / plain / -Verify / -Revert), shaped on `Set-EbillingFirmReasonFieldSecurity.ps1` and `Set-DocumentPointerFieldSecurity.ps1`. It reuses "Spaarke BFF-Managed Field Writers" (read, create, update) and grants no reader profile. A Readers row on the column is FAIL. Preconditions (p0)–(p5) must pass for a write run. A failed grant reverts the secure. A second run writes nothing.

After ontology-r1's review (PASS, no fix-now findings), round 2 closed its known limits:
- **K6:** the script declared `SupportsShouldProcess` but never called `ShouldProcess`, so `-Confirm` did nothing. The sibling `Set-EbillingFirmReasonFieldSecurity.ps1` reads `-WhatIf` the same way, so `SupportsShouldProcess` stays, and every write point now goes through `$PSCmdlet.ShouldProcess`: the secure plus the writer grant, a resume grant, the publish, and each `-Revert` write. The automatic revert after a failed grant is a recovery and is never asked.
- **-Verify (uac-r2):** passes only when the column IS secured, the writer profile holds read=4 create=4 update=4, and no other profile grants any access. Any row with access counts, the Readers profile included. The one exception is the platform's own System Administrator field security profile. Dataverse gives it a row on every secured column (checked on dev: `sprk_signal.sprk_firmreason` has one), so the script matches it by its fixed id `572329c1-a042-4e22-be47-367c6374ea45`, never by name. Must-fire control: a temporary copy pointed at `sprk_signal.sprk_firmreason` reported FAIL for the Readers row and for "Spaarke Ontology Writer Firm Reason Writers", and did not report the System Administrator row. The copy was deleted. Pre-run `-Verify` on dev: exit 1 (not secured; writer row missing; no other profile).
- **K2:** a `-Revert` switch rolls back a run, with a `-WhatIf` path (section 6).

Source: `Entity.xml` `IsSecured` 1; the writer row in `Other/FieldSecurityProfiles.xml`; the form control removed; `docs/data-model/sprk_invoicesubmission.md`.

**Dev -WhatIf (2026-10-23, read-only):** every precondition OK.
- The writer profile is `917b64c5-...`, in SpaarkeMaster.
- Its members are `# mi-bff-api-dev` and `SDAP-BFF-SPE-API`. No default team holds it.
- No maker configuration targets the column.
- The column is in SpaarkeMaster (via the table).
- 0 rows carry findings.
- The Information form still binds the column.
- Both BFF users are System Administrator, so the BFF has no masked window.
- The run WOULD secure the column and grant the writer, and the Readers profile has no row on it. No other profile can read or write it.
- INFO: System Administrator is held by the team "Spaarke Demo" and by several users and application users. They keep full field access by platform rule.
- Exit 0.

## 5. Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- Unit, `FullyQualifiedName~Finance|FullyQualifiedName~Submission|FullyQualifiedName~Override`: 3513 passed, 3 skipped, 0 failed.
- ArchTests (full): 1551 passed, 0 failed.
- Test scope: the Decision Record sentence cases, the guard's controls and the seeding proofs, nothing else.

## 6. Live run (step 6): runbook

Prerequisites: ontology-r1 PASS (2026-10-23), the owner's OK, and the before-notices to ontology-r1 and uac-r2. Run from the repository root as an operator signed in with `az login` (System Administrator on spaarkedev1).

```
# 1. Take the column off the live main form (K1). Forms step plus a publish of the touched entity only.
pwsh scripts/ebilling/Deploy-EbillingFormsViews.ps1 -FormsOnly -WhatIf   # expect: only sprk_invoicesubmission 'Information' WOULD be rebuilt
pwsh scripts/ebilling/Deploy-EbillingFormsViews.ps1 -FormsOnly           # expect: CHANGED main form 'Information' rebuilt; published: entities sprk_invoicesubmission

# 2. Form check (read only, in pwsh): the column is gone from the main form. Prints False after step 1 (True on dev today).
$t = az account get-access-token --resource https://spaarkedev1.crm.dynamics.com --query accessToken -o tsv
$f = Invoke-RestMethod -Uri 'https://spaarkedev1.crm.dynamics.com/api/data/v9.2/systemforms(c412f942-875e-45c4-9ea1-cae75a81f1b9)?$select=formxml' -Headers @{ Authorization = "Bearer $t" }
$f.formxml.Contains('datafieldname="sprk_findingsjson"')

# 3. Field security.
pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -WhatIf
pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c   # second run: "Nothing written; no publish."
pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify   # exit 0; the (p6) form line no longer appears
```

Then send the after-notices.

**Run on dev, 2026-10-26** (owner OK EB-D-149; pre-notices to ontology-r1 and uac-r2, neither objected):

| Step | Result |
|---|---|
| 1. `-FormsOnly -WhatIf` | 1 would change (sprk_invoicesubmission 'Information'), 13 unchanged, exit 0 |
| 1. `-FormsOnly` | CHANGED 'Information' rebuilt; published entity sprk_invoicesubmission only; exit 0 |
| 2. Form check | `False`: the main form no longer binds sprk_findingsjson |
| 3. `-WhatIf` | (p0)-(p5) OK; 0 rows carry findings; both BFF app users hold System Administrator; nothing written; exit 0 |
| 3. Run | secured the column; granted the writer profile read=4 create=4 update=4 (masked window 16.3 s; neither BFF user masked, both hold System Administrator); published; PASS; exit 0 |
| 3. Second run | "Nothing written; no publish." PASS; exit 0 |
| 3. `-Verify` | exit 0: secured, writer 4/4/4, no Readers row, no other profile except System Administrator (572329c1) |

Informational: the -WhatIf listed a System Administrator team ('Spaarke Demo') whose members hold full field access by the platform rule, like every System Administrator. Ontology-r1 received the after-notice; uac-r2 asked for one only if -Verify flagged something, and it did not.

**Why `-FormsOnly` (K1).** The repository's path for dev forms is `Deploy-EbillingFormsViews.ps1`, the generator of this form (task 029). A SpaarkeMaster import would bring the whole solution from this branch. A full run of the forms script throws on dev today: "Exception calling InsertAfter ... The reference node is not a child of this node", in step 3 (the `sprk_MatterManagement` sitemap). That happens after step 1 has written the form and before step 4 publishes it. The sitemap lists `sprk_invoice` twice (`area_mattermanagement/group_work` and `area_finance/group_c6c6d7dd`), and `sprk_invoicesubmission` sits in `area_finance`. The script places the billing-export entry after the submission entry inside the first invoice group, which does not contain it. This is pre-existing navigation drift, not caused by task 122; it is reported for filing.

`-FormsOnly` (added in task 122) runs step 1 and the entity-scoped publish only. Dev -FormsOnly -WhatIf (2026-10-23): only `sprk_invoicesubmission` 'Information' would change, and the other 13 forms are unchanged. The live form differs from the spec only in the findings control and the section label. No pending forms or views. Exit 0.

**Rollback (K2), after a successful run:**
```
pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Revert -WhatIf; pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Revert
```
It un-secures the column first, so no BFF user is ever left masked, then deletes the writer profile's row on it, then publishes `sprk_invoicesubmission`. It exits 1 if the column still reads secured. The SpaarkeMaster source (Entity.xml `IsSecured`, the FieldSecurityProfiles.xml row) is reverted by a commit. The form is not put back: re-adding the control is a forms-script spec change. Dev `-Revert -WhatIf` today: not secured, no writer row, nothing to do, exit 0.

## 7. Follow-ups

- **K3 (audit history):** `sprk_findingsjson` has `IsAuditEnabled=1`. Once a dev submission exists, open its audit history as a non-admin user with Read on it, and confirm the old and new values of `sprk_findingsjson` are not shown. Dataverse should mask a secured column in audit details for a user without read on it; this needs to be checked live.
- **Non-admin read check (acceptance):** once a dev submission exists, as a non-admin user with Read on it, `GET sprk_invoicesubmissions(<id>)?$select=sprk_findingsjson` returns null, and `/review` and `/blocked` still return its findings.
- **K4 (forward-only):** the Decision Record sentence change (section 2) applies to records written from the deploy on. Existing records are not rewritten, and a retry of a decision recorded by an older BFF with a Duplicate sentence is refused as other content. Dev has no such record.
- **Pre-existing, for filing:** a full `Deploy-EbillingFormsViews.ps1` run throws in step 3 on dev (sitemap drift, section 6), after writing forms and before publishing them.
