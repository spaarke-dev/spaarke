# Task 015 - retire sprk_invoicestatus (D-41, FR-06)

> **Status: IN PROGRESS. The dev delete is NOT done.** Code, tests, forms, views, solution source and docs are done. The delete is blocked by a live dependent outside the inventory: the outside-counsel invoices grid configuration `3ff4102c` (SPA-r3 / uac-r2 data, section 3.4). The brief says to stop before any delete when something unexpected turns up, so this run stopped there. The sequence that clears it is in section 3.5.
> **Date**: 2026-10-17. **Environment**: dev (spaarkedev1). **Script**: `projects/ebilling-r1/scripts/Remove-EbillingInvoiceStatus.ps1` (section 5.1 explains why it lives here).
> **Commits** (work/ebilling-r1 lineage): `f6b8113a6` readers, `7d5c4ee2e` forms and views, `b0c53eb48` entity source and ArchTest guard, plus the docs / script / notes commit.

## 1. Owner decisions

**D-97 (2026-10-17), quoted:** "Owner approves task 015 in full, including the destructive step. Migrate `sprk_invoicestatus` to `statuscode`, update all readers, forms and views, then delete the `sprk_invoicestatus` column and option set from spaarkedev1 once nothing references them." (Owner chose "Approve incl. delete".)

**D-98 (2026-10-17):** the owner confirmed the counted-spend set, which replaces ontology D-137 Q24 ("spend counts only Reviewed"). `FinanceBaseValues.CountedInvoiceStatuses` = `statuscode` In Review (1), Approved (659490000), Sent to AP (659490001), Paid (659490002). The filter is on `statuscode`, never `statecode`, because Paid is Inactive and still counts. Rejected, Returned to Firm and Cancelled are not counted. The `FinanceBaseValues` comment cites D-98.

D-97's condition "once nothing references them" is not yet met on dev (section 3.4), so the delete did not run.

## 2. Step 0 and step 1 (grep reconciliation)

Step 0: `git merge-base --is-ancestor f292ad119 HEAD` passes, so ontology #1111 is present.

Step 1 grepped for the literal and for the constant names (`InvoiceStatusReviewed`, `InvoiceStatusToReview`, `InvInvoiceStatus`, `FinanceBaseValues.InvoiceStatus`, `CountedInvoiceStatuses`, `invoiceStatus`) over src, tests, scripts, config, docs, .claude and .github.

| Difference from the POML inventory | Handling |
|---|---|
| Line numbers in the POML had drifted after the origin/master merge (for example SignalReevaluationJobTests). | Each reader was found by name, not by line. |
| `PolicyVersionValidator.AuthorableColumns` remark (ontology, task 102a) | Remark now names `sprk_invoice.statuscode (ebilling-r1 D-41 / D-98)`. No code change. |
| `docs/data-model/sprk_invoice.md`, `docs/architecture/multi-environment-portability-strategy.md:141` | Reworded. `sprk_invoice.md` gains a D-98 "Spend" paragraph. |
| `scripts/ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1:29` (a comment) | Reworded without the literal. |
| `InvoiceReviewWritePathTests :43-44` in the POML | These are the *document* review-status constants (`sprk_document`), not the invoice column. Left unchanged. The invoice create-payload entry (old :172) was removed, and an assertion now checks that the create keys hold no `statuscode` / `statecode`. |
| `invoiceStatus` (camel case) in PolicyVersionValidator predicate names and the live-fact key | These are fact or predicate names, not the column. They stay. |
| `.claude/skills/jps-action-create/examples/ACT-004.json:95` | Main session only (root CLAUDE.md §3). The replacement text is in section 8. |
| **Live only, not in any inventory:** `sprk_gridconfiguration` `3ff4102c-1092-f111-b8dc-7ced8ddc4a05` "Outside Counsel — Invoices (external widget)". Its configjson names the column in fetchXml, layoutXml and the column settings. | It is SPA-r3 / uac-r2 data, so the delete was stopped (section 3.4). |

After the edits, a grep over src, tests, scripts, config, docs and .github finds only `tests/Spaarke.ArchTests/InvoiceStatusRetirementGuardTests.cs`. That file has to spell the name it guards against, and it excludes itself from its own scan. `.claude/` has only ACT-004.json (main session).

## 3. Live dev (spaarkedev1)

### 3.1 Inventory before any write

- **Column:** `sprk_invoice.sprk_invoicestatus`, MetadataId `150d244b-9a07-f111-8407-7c1e520aa4df`.
- **Global option set:** `sprk_invoicestatus`, id `dfeedd70-9907-f111-8407-7ced8d1dc988`, with 100000000 ToReview and 100000001 Reviewed.
- **Rows holding a value: 3.** All hold 100000000 (ToReview) and are at statecode 0 / statuscode 1 (In Review):
  - `bcf81d78-1f0c-f111-8342-7ced8d1dc988` 'Invoice Number 3 Test Record'
  - `d6e0a25f-e10c-f111-8342-7ced8d1dc988` 'Invoice Number 4 Test Record'
  - `4c0f4d12-9e9c-f111-b8de-7ced8ddc4cc6` 'INV-005'
- **All invoices by (statecode, statuscode):** (0, 1) = 10.
- **No other mentions:** no workflow, flow, personal view or chart names the column.

**RetrieveDependenciesForDelete(attribute 150d244b…) BEFORE: 5**

| Component type | Object | What |
|---|---|---|
| 10314 | `a32ae5a7-3908-f111-8407-7c1e520aa4df` | aiskillconfig `ais_FormFillFieldOptOut_sprk_invoice_sprk_invoicestatus` (aiskill FormFillFieldOptOut; solution fd140aae…) |
| 60 | `93aa1c69-0406-f111-8406-7c1e525abd8b` | Invoice main form |
| 60 | `c5e5807d-e20c-f111-8342-7c1e520aa4df` | Invoice quick create form |
| 26 | `b9f6d045-9a5e-f111-ab0c-7c1e521545d7` | 'Invoice - Matter Context' view |
| 26 | `1ad5e8f3-4408-f111-8406-7c1e525abd8b` | 'Invoice Review Queue Projects' view |

**RetrieveDependenciesForDelete(option set dfeedd70…): 1.** The only dependent is component type 2, the attribute itself.

`sprk_gridconfiguration` is a data row, not a solution component, so RetrieveDependenciesForDelete does not report it. The script's mention scan found it.

### 3.2 Runs

1. `-Migrate -FormsAndViews -GridConfig -WhatIf`: the plan printed and nothing was written.
2. `-Migrate -FormsAndViews` (real run):
   - **Migrate: 0 rows written.** The 3 ToReview rows were already In Review, so FR-06 maps them to where they already are. The script logged the 3 ids. It refuses any other value or any other statuscode, and none was found.
   - **FormsAndViews:**
     - Main form: the body cell `{981398e4…}` and the 3 RecordHeader layoutJson entries were removed.
     - Quick create: the row was removed.
     - 'Invoice - Matter Context': the column and cell now use `statuscode`.
     - 'Invoice Review Queue Projects': the filter is now `statuscode eq 1`.
     - Publish: `PublishXml` for `sprk_invoice` only, with no PublishAll, no solution import and no version bump.
3. `-Delete -WhatIf`: **refused, exit 2.** The only blocker is grid configuration `3ff4102c`.

The XML of both forms and both views was snapshotted before the edit. The seam test's `zz-025b-*` rows were cleaned up (0 left).

### 3.3 After the forms and views edit (re-verified with a read-only run, 2026-10-17)

- **RetrieveDependenciesForDelete(attribute): 1.** Only the aiskillconfig `a32ae5a7…` is left. It is a known dependent, and `-Delete` removes it first.
- **RetrieveDependenciesForDelete(option set): 1** (the attribute).
- **Mentions:** forms 0, views 0, personal views 0, workflows 0, charts 0, grid configurations **1** (`3ff4102c`).
- **Rows holding a value:** still the same 3, all In Review. Invoices: (0, 1) = 10.
- **Still present on dev:** the aiskillconfig, the column and the option set.

### 3.4 Why the delete stopped

The live external Invoices widget for outside counsel reads `sprk_invoice` through grid configuration `3ff4102c`, which names the column in its fetch. The two possible orders each break something:

- **Patch the grid to `statuscode` before the BFF carrying `f6b8113a6` is deployed:** the deployed BFF's external allow-list does not admit `statuscode`, so it refuses the widget's fetch.
- **Delete the column before patching the grid:** Dataverse refuses the grid's fetch because the attribute no longer exists.

So the grid has to move between the BFF deploy and the delete, and the grid belongs to another project. Both reasons fall under the brief's stop rule ("finds something unexpected (more dependents …) STOP before any delete and report") and the POML escalation trigger (a reader in another project's code or data).

### 3.5 Remaining sequence (main session; owner OK is D-97)

1. Deploy the BFF containing `f6b8113a6`, which adds `statuscode` to the external invoices allow-list. Until this deploy, the widget's Status column is the old one, which is harmless.
2. `pwsh projects/ebilling-r1/scripts/Remove-EbillingInvoiceStatus.ps1 -GridConfig` (try `-WhatIf` first). This moves `3ff4102c` fetch, layout and column settings to `statuscode`. Tell SPA-r3 / uac-r2 first (section 9).
3. `pwsh projects/ebilling-r1/scripts/Remove-EbillingInvoiceStatus.ps1 -Delete -OwnerApproval 'D-97 2026-10-17 owner' -WhatIf`, then the same command without `-WhatIf`. The script:
   1. re-checks every blocker;
   2. deletes aiskillconfig `a32ae5a7`;
   3. prints RetrieveDependenciesForDelete(attribute) and refuses unless it is 0;
   4. deletes the attribute;
   5. prints RetrieveDependenciesForDelete(option set) and refuses unless it is 0;
   6. deletes the option set;
   7. publishes `sprk_invoice`;
   8. verifies both return 404.
4. Record the dependency output after the delete (expected 0, then 404) in this file.

Until step 3 runs, SpaarkeMaster source is **ahead of dev**: the attribute, option set and root component are already removed (`b0c53eb48`). A full solution import from source before the delete would try to remove them through the import instead.

## 4. Code changes, by owner

**ebilling-r1:**
- `Services/Finance/FinanceSummaryService.cs`: reads `FinanceBaseValues.InvoiceStatusCodeColumn`. `BuildRecentInvoicesQuery` and `ToRecentInvoice` were extracted so they can be tested.
- `Services/Finance/InvoiceReviewService.cs`: the create no longer writes a status, so a new invoice takes the `statuscode` default, In Review.
- `Services/Finance/Tools/FinancialCalculationToolHandler.cs`: both calculations now use `FinanceRollupService.BuildInvoiceQuery`, the D-98 set on `statuscode`. This fixes the #1771 defect, where the old code filtered on 100000002 / 100000003, values that never existed.
- `Services/Insights/LiveFacts/InvoiceLiveFactResolver.cs`: reads `statuscode` and `sprk_totalamount`. This fixes ISS-001 / #1904, where it read `sprk_status` and `sprk_total`, neither of which exists on `sprk_invoice`.
- `Spaarke.UI.Components/.../OptionSetField.tsx`: doc example only.
- `scripts/ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1`: comment only.
- Docs (6 files).
- SpaarkeMaster `sprk_Invoice` forms, views, Entity.xml and Solution.xml; `OptionSets/sprk_invoicestatus.xml` deleted.
- Tests:
  - FinanceBaseValuesTests
  - FinanceSummaryRecentInvoicesTests (new)
  - FinancialCalculationToolHandlerTests
  - InvoiceLiveFactResolverTests
  - InvoiceReviewWritePathTests
  - InvoiceStatusRetirementGuardTests (new)

**ontology-r1 (ontology-owned; they review before merge):**
- `Services/Finance/FinanceBaseValues.cs`: `InvoiceStatus` becomes `InvoiceStatusCodeColumn = "statuscode"`. `InvoiceStatusReviewed` is removed. `CountedInvoiceStatuses` holds the four `InvoiceStatusCodes` values (D-98). The grouped FetchXML filters on `statuscode`.
- `Services/Finance/FinanceRollupService.cs`: the column set and the condition use `InvoiceStatusCodeColumn`.
- `Services/Signals/PolicyVersionValidator.cs`: remark only.
- `tests/unit/domain/Signals/SignalReevaluationJobTests.cs`: the spend test now counts Approved, Paid and Sent to AP and ignores Rejected, Returned to Firm and Cancelled. The harness default is In Review on `statuscode`.
- `tests/integration/seam/Signals/FinanceBaseValuesSeamTests.cs`: the live seam test has all 7 statuses. The Inactive ones are set through an update with statecode / statuscode. The expected matter sum is 500.
- `tests/Spaarke.ArchTests/FinanceBaseValuesSingleDefinitionGuardTests.cs`: the literal regex is now `\b65949000[0-4]\b`, and the control copy holds the new values.

**uac-r2 / SPA-r3 (shared):**
- `Infrastructure/DI/ExternalAccessModule.cs`: the invoices `ReadableColumns` entry is now `statuscode`.
- `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs`: the live grid fetch, the live-derived allow-list and the Task134 allow-lists now use `statuscode`.
- `src/client/external-spa/src/components/detail/RecordDetailFields.tsx`, `RecordDetailModal.tsx`, `tests/RecordDetailModal.test.tsx`.
- **Live data, not in the repo:** grid configuration `3ff4102c` (section 3.5 step 2).

## 5. Deviations

- **5.1 Script location:** the script is in `projects/ebilling-r1/scripts/`, not `scripts/ebilling/`. The new guard and acceptance criterion 1 forbid the literal under `scripts/`, and the script has to name the column. There is precedent for scripts under `projects/*/scripts/`.
- **5.2 No `Import-SolutionScoped.ps1` round trip (acceptance criterion 7):** the brief for this run forbids a solution import. Forms and views were edited live through `systemforms` / `savedqueries`, and SpaarkeMaster was edited by hand to match the live XML (DEF-021 / #2045). The round trip waits until after the delete, when source and dev match again.
- **5.3 Main-form RecordHeader field removed, not replaced:** a `statuscode` entry in the header card's layoutJson would be an editable option set. OptionSetField has no read-only mode, so that would let a user set the status without going through the lifecycle service (#1957). Status stays visible in the form header.
- **5.4 Acceptance criterion 1:** the grep still hits the guard file, which is deliberate and self-excluded, and ACT-004.json until the main session edits it.
- **5.5 PR, ontology-r1 review record and TASK-INDEX:** these are left to the main session (no push from this agent; the TASK-INDEX edit is forbidden by the brief).

## 6. Enforcement

**Enforced by: `tests/Spaarke.ArchTests/InvoiceStatusRetirementGuardTests.cs`** (source scan, root CLAUDE.md §16).

- **Fails on:**
  - the column's logical name, case-insensitive, anywhere under `src/`, `tests/` and `scripts/`, in comments too;
  - `FinanceBaseValues.InvoiceStatus`;
  - `InvoiceStatusReviewed`, `InvoiceStatusToReview` and `InvInvoiceStatus`.
- **Excludes:** `projects/**`, its own file, and node_modules, bin, obj, dist, coverage, .git and .vite.
- **Controls:**
  - 6 must-fire cases: XML, TS, a name companion, a constant read, a test helper and a payload key.
  - 6 must-not-fire cases: the new `InvoiceStatusCodeColumn`, `CountedInvoiceStatuses`, `InvoiceStatusCodes`, and camel-case `invoiceStatus` fact names.
  - `TheWalk_ReachesEveryScannedRoot`, so the scan cannot pass vacuously.

This is the backstop against a stale ontology branch reintroducing a reader (section 9). There is no ratchet baseline: the scan finds 0 violations today.

**Proposed follow-up (not done here):** an ArchTest that fails on a `statuscode` write to `sprk_invoice` outside `InvoiceLifecycleService` (and the create default). It would turn the lifecycle-only write rule (task 014) into a guard. It is out of 015's scope.

## 7. Verification

**Mutation proofs.** Each one failed with the mutation applied and passed again after the file was restored.

| Id | Mutation | Failed |
|---|---|---|
| A | InvoiceReviewService reverted to its pre-015 version (writes the old column on create) | 1 of 45 |
| B | InvoiceLiveFactResolver reverted to pre-015 | 8 of 10 |
| C | ExternalAccessModule reverted to pre-015 | 5 of 133 |
| D1 | `InvoiceStatusCodeColumn` set back to the old column name | 19 of 71 |
| D2 | Paid removed from `CountedInvoiceStatuses` | 4 of 69 |
| D3 | Rejected added to `CountedInvoiceStatuses` | 4 of 69 |
| E | FinanceSummaryService column set back to the old name | 2 of 2 |
| F | FinancialCalculationToolHandler query not the rollup query | 6 of 38 |
| G1-G4 | Guard fed the column in the SPA, a test constant, a script comment and solution XML | 1 of 14 each |
| H | A private `statuscode` copy of the counted values in FinanceRollupService trips FinanceBaseValuesSingleDefinitionGuardTests | 1 of 4 |
| SPA | RecordDetailModal / RecordDetailFields reverted | 2 of 47 |

**Test counts:** see section 10, which was filled in after the final runs.

## 8. ACT-004.json (main session; .claude/ is outside a sub-agent's write boundary)

`.claude/skills/jps-action-create/examples/ACT-004.json` lines 91-96. The field `invoiceStatus` is a *validation* status for an extracted invoice; the example output is `"valid"`. That never matched the old option set (ToReview / Reviewed), and it is not the lifecycle `statuscode` either. Drop the `$choices` binding and keep the field. The replacement is:

```json
      {
        "name": "invoiceStatus",
        "type": "string",
        "description": "Validation status based on mathematical verification and completeness checks"
      },
```

Line 159 (`"invoiceStatus": "valid"`) stays unchanged.

## 9. Notices (the main session sends them)

### 9.1 ontology-r1

> ebilling-r1 task 015 retires `sprk_invoice.sprk_invoicestatus` (D-41). The owner confirmed the counted-spend set as **D-98**, which supersedes your **D-137 Q24** ("spend counts only Reviewed"). Spend = `statuscode` In Review, Approved, Sent to AP and Paid, filtered on `statuscode` (Paid is Inactive and still counts); Rejected, Returned to Firm and Cancelled are not counted. Please review these ontology-owned files before merge:
>
> - `src/server/api/Sprk.Bff.Api/Services/Finance/FinanceBaseValues.cs`: `InvoiceStatus` becomes `InvoiceStatusCodeColumn = "statuscode"`; `InvoiceStatusReviewed` is removed; `CountedInvoiceStatuses` = `InvoiceStatusCodes.InReview, Approved, SentToAp, Paid`; the grouped FetchXML filters on `statuscode`.
> - `src/server/api/Sprk.Bff.Api/Services/Finance/FinanceRollupService.cs`: the column set and the condition use `InvoiceStatusCodeColumn`.
> - `src/server/api/Sprk.Bff.Api/Services/Signals/PolicyVersionValidator.cs`: an AuthorableColumns remark only.
> - `tests/unit/domain/Signals/SignalReevaluationJobTests.cs`: the spend test now counts Approved, Paid and Sent to AP and ignores Rejected, Returned to Firm and Cancelled; the harness default is In Review on `statuscode`.
> - `tests/integration/seam/Signals/FinanceBaseValuesSeamTests.cs`: all seven statuses live; expected matter sum 500.
> - `tests/Spaarke.ArchTests/FinanceBaseValuesSingleDefinitionGuardTests.cs`: the literal regex is now `\b65949000[0-4]\b` and the control holds the new values.
>
> **Stale branches:** these unmerged branches still carry the pre-master FinanceRollupService that reads the retired column: `task/ontology-070-inquiry`, `-131b`, `-044`, `-039-gate-notes` and `ontology/081-cleanup`. You confirmed none of them will merge. If one ever does, the new ArchTest `InvoiceStatusRetirementGuardTests` fails on the column name and on `FinanceBaseValues.InvoiceStatus`. Your 102a authoring-contract note and D-161 are yours to update. ebilling-r1 did not edit your projects/ files.

### 9.2 uac-r2 / SPA-r3

> ebilling-r1 task 015 moves the external Invoices status column from the retired `sprk_invoicestatus` to `statuscode`. It touches these shared files:
>
> - `Infrastructure/DI/ExternalAccessModule.cs` (the invoices ReadableColumns)
> - `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs`
> - `src/client/external-spa/src/components/detail/RecordDetailFields.tsx`, `RecordDetailModal.tsx` and `tests/RecordDetailModal.test.tsx`
>
> **Live data:** `sprk_gridconfiguration` `3ff4102c-1092-f111-b8dc-7ced8ddc4a05` ("Outside Counsel — Invoices (external widget)") names the old column. It will be switched to `statuscode` (fetch, layout and column settings) by `Remove-EbillingInvoiceStatus.ps1 -GridConfig`, right after the BFF with the new allow-list is deployed and before the column is deleted from dev. Please tell us if you have unmerged work on that row or those files.

## 10. Final checks

Run on `b0c53eb48` plus this commit's docs, script and notes (no code change after `b0c53eb48`), 2026-10-17.

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ -m:1` | succeeded, 0 warnings, 0 errors |
| `tests/unit/Sprk.Bff.Api.Tests`, filter `Finance\|Invoice\|Signal\|ExternalModule\|LiveFact\|FinancialCalculation` | 3960 passed, 0 failed, 8 skipped (live-only seams) |
| `tests/Spaarke.ArchTests` (all; the new guard included) | 1140 passed, 0 failed |
| `src/client/external-spa` `npx vitest run` | 36 files, 1019 passed. A first run alongside the dotnet runs had 24 load-induced failures (timeouts); two runs alone passed 1019 / 1019. |
| Live seam `FinanceBaseValuesSeamTests` (spaarkedev1; `SIGNALS_LIVE_CALLER_ID` set) | passed; its rows cleaned up |

**Publish size (CLAUDE.md §10 bullet 3).** Both trees come from `git archive` of the base `16c76e7d8` and of `b0c53eb48`, extracted to `C:\tmp\t015\base` / `head`. Each was built with `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/` and zipped with `Compress-Archive -CompressionLevel Optimal`; both outputs have 192 files.

| Tree | Compressed |
|---|---|
| base `16c76e7d8` | 39,462,216 B = 37.634 MB |
| head `b0c53eb48` | 39,462,867 B = 37.634 MB |
| **Delta** | **+651 B** |

No `*.csproj`, `Directory.Packages.props` or `package.json` changed since the base, so there is no new package and no new CVE source.
