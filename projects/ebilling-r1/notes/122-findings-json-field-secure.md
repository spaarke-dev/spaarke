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

**Known limit (K2, transition).** The billing record's content hash covers the facts, so a decision whose record an older BFF wrote with a Duplicate plan sentence, retried by this BFF, is refused as "same id, other content" (`DecisionRecordWriter.cs:1339`). Dev has no submissions, so no such record exists; nothing is released.

## 3. The app-only-read guard

`tests/Spaarke.ArchTests/FindingsJsonAppOnlyReadGuardTests.cs`, over the compiled IL (`IlCallScan`). No type expresses "app-only", and no existing scan covers column reads, so the rule is a new ArchTest (§16 ladder).

- **C1:** the types that load a string naming the column (Sprk.Bff.Api and Spaarke.Dataverse) are exactly the five above. A new reader fails until it is reviewed and listed; a reviewed type that stops naming it also fails.
- **C2:** no reviewed type calls a caller-identity seam: an `IDataverseUserClient` member, a member of an `Impersonated*` type, an `*AsCaller*` method, or a task-returning method with an `impersonat*` / `callerSystemUserId` parameter.
- **C3:** each reviewed type references `IGenericEntityService`.
- **Controls:** C1 must-fire (a planted reader) and must-not-fire (another column); C2 must-fire for each of the four shapes; C2 must-not-fire and C3 must-fire for an app-only read.
- **Seeding proof:** a `_user.GetAsync(".../sprk_invoicesubmissions?$select=sprk_findingsjson")` added to `InvoiceReviewReadService.ReadSubmissionAsync` fails C2 (1 of 9); restored, 9 pass.
- **Not covered** (in the class remarks): C2 is not transitive; a column array read by another type through a field load; a caller seam named outside the C2 rules; all-columns reads.

## 4. Schema

`scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1` (-WhatIf / plain / -Verify), shaped on `Set-EbillingFirmReasonFieldSecurity.ps1` and `Set-DocumentPointerFieldSecurity.ps1`. It reuses "Spaarke BFF-Managed Field Writers" (read, create, update) and grants no reader profile. A Readers row on the column is FAIL. Preconditions (p0)–(p5) must pass for a write run. A failed grant reverts the secure. A second run writes nothing.

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

## 6. Live run (step 6, NOT run)

```
pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -WhatIf
pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c   # second run: writes nothing
pwsh scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify   # exit 0
```

The non-admin check (null through the Web API; /review and /blocked still return findings) needs a submission on dev, and dev has none today. The live form keeps the masked control until a SpaarkeMaster import or a `Deploy-EbillingFormsViews.ps1` run.
