# Task 166 - field-secure the invoice money and firm columns (EB-D-200 part 1)

Branch `task/ebilling-166` (from `work/ebilling-r1` at `1df8f38a8c`). Status: **applied on dev 2026-11-09** (owner's go typed in the ebilling session; uac-r2 notified before and after). See section 7.

## 1. Scope

Seven `sprk_invoice` columns: `sprk_totalamount` (billed total), `sprk_approvedtotal`, `sprk_feestotal`, `sprk_expensestotal`, `sprk_lineadjustmentstotal`, `sprk_invoiceadjustmentstotal`, `sprk_vendororg` (firm).

- The write profile and the read profile from EB-D-200 (decisions log); no other profile has access. The exact grants are in `src/dataverse/solutions/SpaarkeMaster/Other/FieldSecurityProfiles.xml` and are checked by the script's `-Verify`.
- Record-level Write for users is unchanged (D-77).

## 2. Precondition: every writer is app-only (checked first)

All writers go through the BFF's app-only singletons: `IGenericEntityService` resolves to `DataverseServiceClientImpl` (`GraphModule.cs:145`) and `IFieldMappingDataverseService` to `DataverseWebApiService` (`GraphModule.cs:153`). Neither call below passes an impersonation argument.

| Column(s) | Writer (file:line) | Identity |
|---|---|---|
| fees, expenses, line adjustments, invoice adjustments, approved totals | `Services/Finance/InvoiceTotalsService.cs:157-161` (`_records.UpdateRecordFieldsIfUnchangedAsync`) | App |
| `sprk_vendororg`, `sprk_totalamount` (create) | `Services/Finance/Submission/InvoiceSubmissionPersister.cs:1512, 1542`, sent by `_entities.ExecuteTransactionAsync` (`:730`) | App |
| `sprk_vendororg` (Set firm) | `Services/Finance/InvoiceAssignFirmService.cs:1012`, applied by `TrackingNumberAllocator.cs:431` | App |
| `sprk_totalamount` (header correction) | `Services/Finance/InvoiceCorrectionService.cs:273` (`TotalDue` spec); transactions at `:919`, `:941` | App |
| none of the seven | `Services/Office/OfficeService.cs:2250-2292` (invoice quick-create) | The brief's pointer is stale: it writes only `sprk_name`, `sprk_description`, `sprk_assignedto1`, `ownerid` and the creator stamp (`_genericEntityService`, app) |

Every other TYPED type in `src/server` only reads these columns (ApExportService, FinanceBaseValues, FinanceSummaryService, InvoiceReviewService, InvoiceContextService, the ExternalAccessModule allow-list). **The BFF's generic writers** (an AI output mapping, `DataverseUpdateHandler`; an Update Record node, `UpdateRecordActionCore`; Repair; a create-time field-mapping rule) run app-only, so field security is not what governs them; they refuse these columns through `InheritedAccessPermission.AccessColumns["sprk_invoice"]` (refusal reason "it decides no access; it is an invoice amount, and only typed writers derive it") and `RecordTableRules` `MappingProtectedColumns`: the six amount columns were added in the fix round (review F1); `sprk_vendororg` was already in `AccessColumns` (task 095c-5). None of the typed writers above asks that refusal. `InvoiceMoneyColumnsAppOnlyWriteGuardTests` pins every type that names a column (writers app-only, none asks the refusal, a new type must be reviewed), and `AccessTableAppOnlyWriterTests`, `RecordTableRulesTests` and `RecordCreationServiceMappingSeamTests.InvoiceMoneyProtected` pin the refusal and its must-not-fire cases. Repair on an invoice is skipped whole by the invoice-family guard on this build; its per-rule ask is the same `RefuseAppOnlyWrite` call. Other tables with a column of the same name (`sprk_billingexport.sprk_totalamount`; `sprk_vendororg` on submission, line, procedure and assignment tables) are not in scope.

**Not app-only: two model-driven form controls.** `sprk_totalamount` was a typed field (`disabled="false"`) on the main form (`{93aa1c69-...}.xml:112`) and the quick-create form (`{c5e5807d-...}.xml:59`); the other five totals and `sprk_vendororg` were already disabled on the main form (forms script `ForceReadOnly`). The forms carry no script and the solution has no workflow or plugin assembly. Reported to the coordinator, who decided (option a): disable the control, because the billed total comes from the submission (FR-17, FR-20) and totals are server-side (FR-07). Done here; see section 4.

## 3. Reads by users and by other identities

- User-side reads (covered by the Readers grant on every business unit's default team): `src/solutions/ApExport` (reads `sprk_approvedtotal`, `_sprk_vendororg_value` through `Xrm.WebApi` as the user), LegalWorkspace and SmartTodo select lists, the external SPA record detail.
- The derived Threshold's evaluator (`SignalEvaluator`, DI singleton over `IGenericEntityService`) reads `sprk_totalamount` and `sprk_approvedtotal` org-wide. Its identity keeps read through the profile that covers it, so it still reads them. See finding F1.

## 4. What changed

- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Invoice/Entity.xml`: `IsSecured` 1 on the seven columns.
- `src/dataverse/solutions/SpaarkeMaster/Other/FieldSecurityProfiles.xml`: 7 rows on the read profile and 7 on the write profile, after the `sprk_firmhold` rows.
- Forms: `sprk_totalamount` control `disabled="true"` on the main and quick-create forms (and their `_managed` copies) in the SpaarkeMaster source. **The quick-create form can no longer set a billed total**; FR-17 says invoices come from submission anyway.
- `scripts/ebilling/Deploy-EbillingFormsViews.ps1`: `sprk_totalamount` added to the invoice `ForceReadOnly` list, so a forms redeploy keeps it read-only. (The script handles only the main form; the quick-create form is covered by the solution source.)
- `scripts/ebilling/Set-EbillingInvoiceMoneyFieldSecurity.ps1`: `-WhatIf`, plain write, `-Verify`, `-Revert`. A seven-column version of `Set-EbillingResubmissionOfFieldSecurity.ps1` / `Set-EbillingInvoiceFirmHoldColumn.ps1` (the same two profiles, (p7)/(p8) default-team checks). Differences from the single-column copies: it loops the column list; it secures and grants one column at a time and, on a failed grant, reverts every column that run secured; (c) treats ANY access by another profile as FAIL (the contract: "no other profile grants access"), not only create/update.
- `tests/integration/data-mutation/FinanceSubmission/InvoiceMoneyFieldSecurityAgreementTests.cs`: script, solution source, forms and BFF constants agree; parser drifts and verify-check pins are seeded.
- `tests/integration/data-mutation/Signals/DerivedBaseColumnsFieldSecurityTests.cs`: a reviewed exception, see F1.

## 5. -WhatIf against dev (read only, 2026-11-06, az CLI token)

The read-only `-WhatIf` against dev (the dev application ids as in the script's help) -> exit 0, "DRY RUN complete - nothing was written", no gaps.

- (p0) all 7 columns exist and can be secured for read, create and update.
- (p1)/(p7) both profiles exist once and are in SpaarkeMaster. (p2) the write profile's membership matches the expected application identities exactly, with no team. (p3) no default team holds the write profile.
- **(p4) no hits**: no `sprk_fieldmappingrule`, `sprk_aitopicregistry` or `sprk_emailupdatefield` row targets any of the 7 columns (also confirmed with direct queries).
- (p5) all 7 in SpaarkeMaster (via the table). (p8) the Readers profile is on all 6 business-unit default teams.
- (a)/(b): none of the 7 is secured yet; it would secure each and grant 14 rows (7 on each profile). (c) no other profile has any access.
- **Provisioning prerequisite (fix round).** Dev cannot prove what the BFF reads or writes once the columns are secured, because the dev BFF identities have full field access there. The first proof (the derived Threshold's evaluator spend reads, the typed writers, and every read where an empty value would be acted on: approval, AP export, the firm gate) is a **customer stamp whose BFF identity has no such bypass**. Until then the secured state is unproven for the BFF; run `-Verify` and a smoke of an approval, an AP export and a Set firm there before relying on it. Provisioning adds a stamp check that fails the run if the columns are not secured or the evaluator identity cannot read them (ontology-r1, 2026-11-06).
- `-Revert -WhatIf`: exit 0, nothing secured, no rows to delete.
- **Re-run after the fix round (2026-11-06, read only).** `-WhatIf` -> exit 0, "DRY RUN complete - nothing was written", no gaps; (p0) now also reads each column through its typed path and reports the type ("exists (Money)", the firm "exists (Lookup)"), so a wrong cast fails (p0); the rest of the output is unchanged (14 grants, 7 secures, (p4) no hits). `-Revert -WhatIf` -> exit 0, "REVERT DRY RUN complete - nothing was written", all seven columns "not field-secured", no rows to delete. Neither ran a write.
- (p6) live dev forms are behind the source: the quick-create and main forms still have `sprk_totalamount` enabled, and the **main form still has `sprk_vendororg` enabled** (the source and the forms script say disabled; the forms script has not been rerun on dev since 095c-4). Redeploy before or with the apply (F3).
- (p6) the money `_base` companions report `IsSecured=False`, `CanBeSecuredForRead=False` (F2).

## 6. Findings and follow-ups

- **F1 - ontology-r1's guard forbids securing two of these columns.** `DerivedBaseColumnsFieldSecurityTests.EveryDerivedBaseColumn_InTheSolutionSource_IsNotFieldSecured` failed on `sprk_invoice.sprk_totalamount` (and would on `sprk_approvedtotal`): the guard assumes the evaluator would read a secured column as empty. The evaluator's identity keeps read through its profile, so it reads both. Changed the guard minimally: a named exception list (`SecuredForTheBffWriters`, reason EB-D-200), a listed column must still be secured, every other derived column must still be unsecured, and the list may name only derived columns. **Reviewed and approved by ontology-r1 (2026-11-06).** Any other evaluator identity needs the same read grant.
- **F1 follow-up (fix round, review F4).** The live twin of the guard (`SignalPredicateTests.AuthorableColumns_ExistAndAreNotFieldSecured_OnTheLiveSchema`) still required the two columns to be unsecured and would have failed on the first live apply. The exception list now lives in ONE shared file, `tests/integration/Shared/Dataverse/DerivedBaseColumnSecurityExceptions.cs`, used by the offline and the live guard: a listed column must be secured in both, every other derived column unsecured, the offline test still pins that the list names only derived columns. **Needs ontology-r1 review.** The live test skips without a live Dataverse URL, so its changed branch has not run against the schema; run it after the apply.
- **Remarks updated with ontology-r1's OK (2026-11-06).** `FinanceBaseValues.DerivedBaseColumns` now names the reviewed exception list, and the derived-Threshold residual in `PolicyVersionValidator` names the two guards and the two deliberately secured totals.
- **F2 - `_base` companions.** The base-currency columns (`sprk_*_base`) cannot be secured on their own (`CanBeSecuredForRead=False`). Whether the platform masks them when the parent is secured is unverified; after the apply, read one as a non-writer user. They are system-calculated and not user-writable, so there is no integrity risk, only possible read exposure of a value every user can already read through the parent.
- **F3 - live forms drift.** See section 5.
- **After the apply:** only the BFF's typed writers set these columns; every user keeps reading them, and the billed total is read-only on the forms.
- **Money-column PUT (fix round, review K1).** `Set-ColumnSecured` no longer uses the plain `Attributes(LogicalName=...)` path: it reads and PUTs the typed definition (`/Microsoft.Dynamics.CRM.MoneyAttributeMetadata` for the six Money columns, `/LookupAttributeMetadata` for `sprk_vendororg`, as `Deploy-EbillingInvoiceTotalsRange.ps1`), re-reads it, and fails loudly if the Money precision, minimum or maximum (the lookup's targets) moved. (p0) reads each column through the typed path too. The typed read is proven live read-only (the `-WhatIf` above); the typed PUT itself is proven only by the Pester double until the first live apply, so watch the first column's secure and grant, then continue.
- **Partial failure (fix round, review K2).** A failure securing column N no longer leaves the earlier columns unreported and unpublished: they stay secured AND granted, the work written is published, the output names completed / failed / not started and the exit code is 1; if the failed PUT was applied (a changed shape), the column is granted so it is never left masked. A failed grant still reverts every column the run secured. `-Revert` records a failure per column, still reverts the rest, publishes and exits 1; declining to un-secure a column at `-Confirm` keeps that column's permission rows (a secured column with no grants would be masked for everyone). Run by `tests/scripts/Set-EbillingInvoiceMoneyFieldSecurity.Tests.ps1` (Pester 5.7.1, 15 tests; a seeded drift of the typed path and of the keep-the-rows rule each fail one), and pinned as source facts by `InvoiceMoneyFieldSecurityAgreementTests`.
- **Apply order and notices:** uac-r2 notice before and after; run the forms redeploy (and import the quick-create form with the solution) with the apply; run `-Verify` after.

## Reviewed after the 164a merge (2026-11-07)

- `InvoiceLineAddService` (task 164a, Add line): a WRITER of a line's `sprk_vendororg` only (it copies the invoice's confirmed firm onto the added line, like `InvoiceAdjustmentService`), through `IGenericEntityService` (app-only). It reads the invoice's billed total and firm; it writes no invoice money column (the totals go through `InvoiceTotalsService`, and its version-only invoice update sets no column).
- `InvoiceFindingsDiff` (164a-4a): a READER of the invoice's `sprk_vendororg` (the rule facts' firm).


## 7. Live apply on dev (2026-11-09)

- **Owner's go:** typed in the ebilling session on 2026-11-09. uac-r2 was notified before and after.
- **Plain run:** `Set-EbillingInvoiceMoneyFieldSecurity.ps1 -BffApplicationIds <dev ids>` exited 0. It secured the seven columns one at a time and granted the writer and reader profiles at once (a masked window of a few seconds per column, 172 s for the whole run), then published `sprk_invoice`.
- **`-Verify`:** exited 0. Each of the seven columns is secured. The writer profile holds read/create/update and the reader profile holds read. No other profile grants access, apart from the System Administrator profile.
- **Forms:** `Deploy-EbillingFormsViews.ps1 -FormsOnly` exited 0. The invoice main form's totals are now read-only (F3 closed). The script has no per-table filter, so the same run also rebuilt the invoice submission main form (165c pass 1, `13a10bcbfc`: link columns read-only) and the billing export main form (`61ac61d5bc`: New and Delete hidden, legacy fields dropped) from the work-branch source. **Neither change is on master yet.** Until the work branch merges, a forms deploy from master would revert those two forms on dev.
- **Read check (requested by uac-r2): pass.** One known invoice was read with a single-record Web API read, impersonating each identity that reads these columns server-side, plus an ordinary non-admin user. All seven values came back non-null and unchanged, before and after the apply. Caveat (section 5): on dev, these server identities also have platform-level full field access, so dev cannot prove the profile path on its own. The proof is a customer stamp.
- **F2 (`_base` companions), resolved as a non-issue under EB-D-200's read model.** EB-D-200 restricts writes, not reads: every user reads the totals through the Readers profile on their business unit's default team, so the system-calculated base value exposes nothing further. This holds under two preconditions:
  - **(1)** If a later decision restricts *read* of these totals to some users, the base columns become an exposure. That decision must then keep them off forms, views and quick find, enforce this with a guard, and pin the BFF never to project them.
  - **(2)** Every business unit's default team holds the Readers profile, including business units created after provisioning. This is not yet checked: **DEF-066 (#2651)**.
- **Live guards:** `SignalPredicateTests.AuthorableColumns_ExistAndAreNotFieldSecured_OnTheLiveSchema` passes on dev with the shared exception list (F1 follow-up closed). `FinanceBaseValuesSeamTests.SpendQueries_AreAcceptedByDataverse_ReadOnly` passes. `RowReads_OnRealRows_...` fails before writing anything, because dev does not yet carry `sprk_invoicesubmission.sprk_trackingscope` (task 095c-1's schema, not yet applied on dev). No `zz-025b-` rows were left behind. Rerun it after 095c-1's dev apply.
