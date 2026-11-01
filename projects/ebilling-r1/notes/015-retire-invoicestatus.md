# Task 015 - retire sprk_invoicestatus (D-41, FR-06)

> **Status: IN PROGRESS. The dev delete is NOT done.** Code, tests, forms, views, solution source and docs are done. The delete is blocked by a live dependent outside the inventory: the outside-counsel invoices grid configuration `3ff4102c` (SPA-r3 / uac-r2 data, section 3.4). The brief says to stop before any delete when something unexpected turns up, so this run stopped there. The sequence that clears it is in section 3.5.
> **Date**: 2026-10-17. **Environment**: dev (spaarkedev1). **Script**: `projects/ebilling-r1/scripts/Remove-EbillingInvoiceStatus.ps1` (section 5.1 explains why it lives here).
> **Commits** (work/ebilling-r1 lineage): `e0df77339` readers, `b2d8cb6dd` forms and views, `7c474b2c9` entity source and ArchTest guard, `d4e555e38` docs / script / notes. Review fix round (2026-10-17, section 11): D-103 counted spend, transition-window allow-list, delete-script attestations.

## 1. Owner decisions

**D-97 (2026-10-17), quoted:** "Owner approves task 015 in full, including the destructive step. Migrate `sprk_invoicestatus` to `statuscode`, update all readers, forms and views, then delete the `sprk_invoicestatus` column and option set from spaarkedev1 once nothing references them." (Owner chose "Approve incl. delete".)

**D-98 (2026-10-17), SUPERSEDED by D-103:** the owner first confirmed In Review, Approved, Sent to AP and Paid as the counted-spend set (replacing ontology D-137 Q24, "spend counts only Reviewed").

**D-103 (2026-10-17), in force:** counted spend = `statuscode` **Approved (659490000), Sent to AP (659490001) and Paid (659490002)**. **In Review is NOT counted** (it is pending, not actual spend; ebilling's D-104 adds a separate Pending Spend). This is the same decision as ontology-r1 D-166, and it reconciles the two answers the owner gave the two sessions. The filter is on `statuscode`, never `statecode`, because Paid is Inactive and still counts. Rejected, Returned to Firm and Cancelled are not counted. `FinanceBaseValues.CountedInvoiceStatuses` and its comment cite D-103 / ontology D-166. D-102 (held invoices do not count) is satisfied automatically, because a held invoice is In Review.

**D-107 (2026-10-17):** firms see the `statuscode` labels as they are, including "Sent to AP". The external SPA keeps reading `statuscode` and shows its label unchanged.

uac-r2 relayed the same owner decision on 2026-10-18: firms may see every invoice status, including "Sent to AP". That closes the product question from uac-r2's 015 review.

D-97's condition "once nothing references them" is not yet met on dev (section 3.4), so the delete did not run.

## 2. Step 0 and step 1 (grep reconciliation)

Step 0: `git merge-base --is-ancestor f292ad119 HEAD` passes, so ontology #1111 is present.

Step 1 grepped for the literal and for the constant names (`InvoiceStatusReviewed`, `InvoiceStatusToReview`, `InvInvoiceStatus`, `FinanceBaseValues.InvoiceStatus`, `CountedInvoiceStatuses`, `invoiceStatus`) over src, tests, scripts, config, docs, .claude and .github.

| Difference from the POML inventory | Handling |
|---|---|
| Line numbers in the POML had drifted after the origin/master merge (for example SignalReevaluationJobTests). | Each reader was found by name, not by line. |
| `PolicyVersionValidator.AuthorableColumns` remark (ontology, task 102a) | Remark now names `sprk_invoice.statuscode (ebilling-r1 D-41 / D-103, ontology D-166)` after the fix round. No other change. |
| `docs/data-model/sprk_invoice.md`, `docs/architecture/multi-environment-portability-strategy.md:141` | Reworded. `sprk_invoice.md` gains a "Spend" paragraph (D-103 after the fix round). |
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

- **Patch the grid to `statuscode` before a BFF that admits `statuscode` is deployed:** the deployed BFF's external allow-list does not admit `statuscode`, so it refuses the widget's fetch.
- **Delete the column before patching the grid:** Dataverse refuses the grid's fetch because the attribute no longer exists.

So the grid has to move between the BFF deploy and the delete, and the grid belongs to another project. Both reasons fall under the brief's stop rule ("finds something unexpected (more dependents …) STOP before any delete and report") and the POML escalation trigger (a reader in another project's code or data).

### 3.5 Remaining sequence (main session; owner OK is D-97), and the live spend facts

**Deploy-order rule (uac-r2's re-check; supersedes the looser wording in 3.4 and the Sequence below where they differ).** The order is:
1. A BFF that admits `statuscode` in the external allow-lists goes live **before** any SPA that reads `statuscode`. Otherwise invoice details and the project / matter invoice sections get HTTP 400 `ErrorFetchXmlColumnNotPermitted`.
2. The outside-counsel grid (`3ff4102c`) moves to `statuscode` any time after step 1. **Done 2026-10-18** (section 12).
3. The column is deleted only after **both** a BFF carrying 015's Finance readers **and** the `statuscode` SPA are live.
4. Then the transitional entries are removed (the follow-up below).

**Sequence.** Shared dev's BFF and external SPA are deployed only by SPA-r3 from its branch (project CLAUDE.md section 6), so every step that depends on a deploy is R3's. The delete script cannot see what is live; the operator passes the commit each deploy was built from, and the script checks that commit's source by content (section 5.6).

1. **R3 deploys a BFF that admits both columns** (candidate `2bda5ebe1`: its invoices allow-list holds the old column and `statuscode`), **then the SPA**. 015's own allow-list is the same union (section 4), so the merge of the two branches stays consistent.
2. **R3 confirms the deploy is live.**
3. **`-GridConfig`, `-WhatIf` first:**
   `pwsh projects/ebilling-r1/scripts/Remove-EbillingInvoiceStatus.ps1 -GridConfig -BffDeployAttestation <the BFF commit from step 1> -WhatIf`, then the same command without `-WhatIf`. It moves `3ff4102c` fetch, layout and column settings to `statuscode`, with `If-Match` on the row's etag. Tell SPA-r3 / uac-r2 first (section 9). It must be a separate run from `-Delete`.
4. **015 reaches master, and R3's branch merges it.**
5. **R3 deploys a BFF containing 015's Finance readers** (FinanceBaseValues, FinanceRollupService, InvoiceReviewService on `statuscode`). `2bda5ebe1` does NOT qualify: it admits both columns but still reads the old one in FinanceBaseValues (the script refuses it for `-Delete`, section 5.6).
6. **`-Delete`, `-WhatIf` first, with `-OwnerApproval 'D-97 2026-10-17 owner'`:**
   `pwsh projects/ebilling-r1/scripts/Remove-EbillingInvoiceStatus.ps1 -Delete -OwnerApproval 'D-97 2026-10-17 owner' -BffDeployAttestation <step 5 commit> -SpaDeployAttestation <step 1 SPA commit> -WhatIf`, then the same command without `-WhatIf`. The script:
   1. re-checks every blocker;
   2. deletes aiskillconfig `a32ae5a7`;
   3. prints RetrieveDependenciesForDelete(attribute) and refuses unless it is 0;
   4. deletes the attribute;
   5. prints RetrieveDependenciesForDelete(option set) and refuses unless it is 0;
   6. deletes the option set;
   7. publishes `sprk_invoice`;
   8. verifies both return 404.
   Record the dependency output after the delete (expected 0, then 404) in this file.
7. ~~A follow-up commit removes the transitional allow-list entries~~ Done ahead of the delete (below): the entry was removed on master in #2450, and the dev column delete waits for the next dev BFF deploy.

**Rule agreed with R3.** After the delete, every dev BFF deploy must contain 015, **checked by content, not ancestry** (a squash merge breaks `merge-base --is-ancestor`). The check: `FinanceBaseValues.cs` has `InvoiceStatusCodeColumn` and none of FinanceBaseValues, FinanceRollupService or InvoiceReviewService names the old column. `Remove-EbillingInvoiceStatus.ps1` runs exactly that check at the attested commit; R3's deploy gate should run the same grep.

**Follow-up (done 2026-10-29, ebilling-r1 integration merge of master ≥ 929dc42d31).** The external invoices allow-list entry was removed on master in #2450 (R3 task 002, with ebilling's OK), ahead of the dev column delete, which waits for the next dev BFF deploy. The integration merge took master's `ExternalAccessModule.cs`, dropped the test constant and the transition tests, and removed the guard's exemption list; the plain guard now allows the name nowhere but in its own file (master's must-refuse tests name the column through a constant spelled in two parts). What the follow-up originally listed:
- `Infrastructure/DI/ExternalAccessModule.cs`: the invoices `ReadableColumns` line marked `transitional (015/D-97)`;
- `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs`: the `TransitionalInvoiceStatusColumn` constant and its uses (the live-grid row goes back to `statuscode`, the transition tests go, the two list pins drop the entry);
- `tests/Spaarke.ArchTests/InvoiceStatusRetirementGuardTests.cs`: the `TransitionalExemptions` list and its three controls. The guard has a ratchet for this: `EveryTransitionalExemption_IsStillNeeded` fails as soon as one of the two lines is removed without its exemption.
The same removal applies to R3's branch (it carries the same union).

**Live spend facts (read on dev 2026-10-17; D-103).**
- All 10 dev invoices are In Review (statecode 0, statuscode 1), so **counted spend is 0 everywhere** under D-103. In Review is not counted.
- The seeded `sprk_totalspendtodate` of 25000 on matter `2444af6d-e1f2-f011-8406-7ced8d1dc988` (baseline-finance-pipeline.md) is a stored copy; it is **rewritten to 0 at the next rollup** of that matter. Nothing was written here.
- **No seeding** of invoices at counted statuses until ontology-r1's owner answers.
- POL-COMMIT-BUDGET is an **Existence** rule (`sprk_ruletype` Existence, 100000002), not a spend Threshold: its firing does not depend on the counted set. Spend and budget Thresholds, where authored, do.

Until step 6 runs, SpaarkeMaster source is **ahead of dev**: the attribute, option set and root component are already removed (`7c474b2c9`). A full solution import from source before the delete would try to remove them through the import instead.

## 4. Code changes, by owner

**ebilling-r1:**
- `Services/Finance/FinanceSummaryService.cs`: reads `FinanceBaseValues.InvoiceStatusCodeColumn`. `BuildRecentInvoicesQuery` and `ToRecentInvoice` were extracted so they can be tested.
- `Services/Finance/InvoiceReviewService.cs`: the create no longer writes a status, so a new invoice takes the `statuscode` default, In Review.
- `Services/Finance/Tools/FinancialCalculationToolHandler.cs`: both calculations now use `FinanceRollupService.BuildInvoiceQuery`, the D-103 set on `statuscode`. This fixes the #1771 defect, where the old code filtered on 100000002 / 100000003, values that never existed.
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
- `Services/Finance/FinanceBaseValues.cs`: `InvoiceStatus` becomes `InvoiceStatusCodeColumn = "statuscode"`. `InvoiceStatusReviewed` is removed. `CountedInvoiceStatuses` holds Approved, Sent to AP and Paid (D-103; In Review is not counted). The grouped FetchXML filters on `statuscode`.
- `Services/Finance/FinanceRollupService.cs`: the column set and the condition use `InvoiceStatusCodeColumn`.
- `Services/Signals/PolicyVersionValidator.cs`: remark only (cites D-103 / ontology D-166).
- `tests/unit/domain/Signals/SignalReevaluationJobTests.cs`: the spend test counts Approved, Paid and Sent to AP and ignores In Review, Rejected, Returned to Firm and Cancelled. The harness default is Approved on `statuscode`, and the aggregate fake throws on a condition attribute it does not model.
- `tests/integration/seam/Signals/FinanceBaseValuesSeamTests.cs`: the live seam test has all 7 statuses. The Inactive ones are set through an update with statecode / statuscode. The expected matter sum is **200** (Approved 120 + Sent to AP 60 + Paid 20; the 300 In Review does not count).
- `tests/Spaarke.ArchTests/FinanceBaseValuesSingleDefinitionGuardTests.cs`: the literal regex is `\b65949000[0-4]\b`, the control copy holds the new values, and `InvoiceStatusCodes.` in the two scanned files is flagged too.

**uac-r2 / SPA-r3 (shared):**
- `Infrastructure/DI/ExternalAccessModule.cs`: the invoices `ReadableColumns` admits `statuscode` AND, during the transition window, the old column (`transitional (015/D-97): remove after the dev column delete`).
- `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs`: the live-derived and Task134 allow-lists carry both entries (through one constant, `TransitionalInvoiceStatusColumn`). The LIVE grid row is the current fetch (old column); the post-switch fetch is `TargetInvoicesGridFetchXml`, labelled as the target; new cases prove the current live fetch and the target fetch are both admitted, and that only the invoices module admits the old column.
- `src/client/external-spa/src/components/detail/RecordDetailFields.tsx`, `RecordDetailModal.tsx`, `tests/RecordDetailModal.test.tsx` (read `statuscode`; unchanged by the fix round).
- **Live data, not in the repo:** grid configuration `3ff4102c` (section 3.5 step 3).

## 5. Deviations

- **5.1 Script location:** the script is in `projects/ebilling-r1/scripts/`, not `scripts/ebilling/`. The new guard and acceptance criterion 1 forbid the literal under `scripts/`, and the script has to name the column. There is precedent for scripts under `projects/*/scripts/`.
- **5.2 No `Import-SolutionScoped.ps1` round trip (acceptance criterion 7):** the brief for this run forbids a solution import. Forms and views were edited live through `systemforms` / `savedqueries`, and SpaarkeMaster was edited by hand to match the live XML (DEF-021 / #2045). The round trip waits until after the delete, when source and dev match again.
- **5.3 Main-form RecordHeader field removed, not replaced:** a `statuscode` entry in the header card's layoutJson would be an editable option set. OptionSetField has no read-only mode, so that would let a user set the status without going through the lifecycle service (#1957). Status stays visible in the form header.
- **5.4 Acceptance criterion 1:** the grep still hits the guard file, which is deliberate and self-excluded, and ACT-004.json until the main session edits it.
- **5.5 PR, ontology-r1 review record and TASK-INDEX:** these are left to the main session (no push from this agent; the TASK-INDEX edit is forbidden by the brief).
- **5.6 Delete script attestations (review fix round; ontology-r1 F2, uac-r2 K1/K2):**
  - `-BffDeployAttestation <sha>` is mandatory for `-GridConfig` and `-Delete`; `-SpaDeployAttestation <sha>` for `-Delete`.
  - `git cat-file -e` runs first (an unknown or malformed sha fails clearly), then the commit's source is read with `git show <sha>:<path>`:
    - `-GridConfig`: the external invoices allow-list in `ExternalAccessModule.cs` lists `"statuscode"`;
    - `-Delete`: also none of `FinanceBaseValues.cs`, `FinanceRollupService.cs`, `InvoiceReviewService.cs` names the old column, and `FinanceBaseValues.cs` has `InvoiceStatusCodeColumn`; and (SPA) `RecordDetailModal.tsx` and `RecordDetailFields.tsx` name no old column.
  - The checks are git-only, run before any Dataverse call, and also run under `-WhatIf` (exit 2, nothing changed).
  - `-GridConfig -Delete` in one run is refused (the grid blocker would clear partway through).
  - K2: the "column already retired" exit no longer skips `-GridConfig`. K1: the grid PATCH sends `If-Match` with the row's etag; a 412 is reported and nothing is overwritten.
  - Not proven live: the already-retired `-GridConfig` path (the column still exists on dev) and the 412 path. Both are covered by reading the code only.

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

This is the backstop against a stale ontology branch reintroducing a reader (section 9).

**Ratchet (transition window, D-97).** Two exact lines are exempt until the column is deleted from dev: the `"sprk_invoicestatus", // transitional (015/D-97): remove after the dev column delete` entry in `ExternalAccessModule.cs`, and the one `TransitionalInvoiceStatusColumn` constant in `ExternalModuleColumnAllowListTests.cs` (file plus exact trimmed line, compared as text). The list only shrinks: `EveryTransitionalExemption_IsStillNeeded` fails when a line is removed without its exemption, `TheExemptionList_NeverGrows` caps it at two, and the must-fire control `TheDetector_StillFires_OnTheRetiredNameAnywhereElse` proves the name still fails on another line of an exempt file, in another file, and with a changed comment. Real-scan proof: a comment naming the column added to `FinanceSummaryService.cs` made the scan fail with `FinanceSummaryService.cs:536` (and only that line).

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

Review fix round (2026-10-17), same rule (each failed with the mutation, passed after restoring):

| Id | Mutation | Failed |
|---|---|---|
| A (D-103) | `InvoiceStatusCodes.InReview` put back into `CountedInvoiceStatuses` | 7 of 306 in FinanceBaseValuesTests, SignalReevaluationJobTests and FinancialCalculationToolHandlerTests: the new rollup-query test (2), the new grouped-query test (2), the counted-set test, the `AnInvoice_IsCountedAsSpend` row for In Review, and `Run_SpendThreshold_...OnlyCountsTheCountedStatuses` (the ReadAsync path) |
| B1 | The module's transitional entry removed | 7 of 136 in ExternalModuleColumnAllowListTests (current-live-fetch case, only-the-invoices-module case, live-grid theory row, live-derivation pin, 3 rule-(b) controls that start from the live fetch) and `EveryTransitionalExemption_IsStillNeeded` |
| B2 | A comment naming the old column added to FinanceSummaryService.cs | 1 of 18 in the guard class, naming `FinanceSummaryService.cs:536`; the exempt lines did not fire |

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

> ebilling-r1 task 015 retires `sprk_invoice.sprk_invoicestatus` (D-41). The owner's final answer is **D-103** (= your D-166), which supersedes our D-98 and your **D-137 Q24** ("spend counts only Reviewed"). Spend = `statuscode` **Approved, Sent to AP and Paid**, filtered on `statuscode` (Paid is Inactive and still counts); **In Review is not counted** (nor Rejected, Returned to Firm, Cancelled). Please review these ontology-owned files before merge:
>
> - `src/server/api/Sprk.Bff.Api/Services/Finance/FinanceBaseValues.cs`: `InvoiceStatus` becomes `InvoiceStatusCodeColumn = "statuscode"`; `InvoiceStatusReviewed` is removed; `CountedInvoiceStatuses` = `InvoiceStatusCodes.Approved, SentToAp, Paid`; the grouped FetchXML filters on `statuscode`.
> - `src/server/api/Sprk.Bff.Api/Services/Finance/FinanceRollupService.cs`: the column set and the condition use `InvoiceStatusCodeColumn`.
> - `src/server/api/Sprk.Bff.Api/Services/Signals/PolicyVersionValidator.cs`: an AuthorableColumns remark only.
> - `tests/unit/domain/Signals/SignalReevaluationJobTests.cs`: the spend test counts Approved, Paid and Sent to AP and ignores In Review, Rejected, Returned to Firm and Cancelled; the harness default is Approved on `statuscode`; the aggregate fake throws on a condition attribute it does not model (your K2).
> - `tests/integration/seam/Signals/FinanceBaseValuesSeamTests.cs`: all seven statuses live; expected matter sum **200** (the 300 In Review does not count). Not re-run live in this round (it writes test rows).
> - `tests/Spaarke.ArchTests/FinanceBaseValuesSingleDefinitionGuardTests.cs`: the literal regex is `\b65949000[0-4]\b`, the control holds the new values, and `InvoiceStatusCodes.` in the two scanned files is flagged too (your K2).
>
> **Stale branches:** these unmerged branches still carry the pre-master FinanceRollupService that reads the retired column: `task/ontology-070-inquiry`, `-131b`, `-044`, `-039-gate-notes` and `ontology/081-cleanup`. You confirmed none of them will merge. If one ever does, the new ArchTest `InvoiceStatusRetirementGuardTests` fails on the column name and on `FinanceBaseValues.InvoiceStatus`. Your 102a authoring-contract note and D-161 are yours to update. ebilling-r1 did not edit your projects/ files.

### 9.2 uac-r2 / SPA-r3

> ebilling-r1 task 015 moves the external Invoices status column from the retired `sprk_invoicestatus` to `statuscode`. During the transition window the invoices allow-list admits BOTH (the same union R3's branch carries), each entry marked `transitional (015/D-97): remove after the dev column delete`; a follow-up commit removes them after the dev delete. The run order is in section 3.5 (your BFF with both columns first, then the SPA; `-GridConfig` only after you confirm it is live). It touches these shared files:
>
> - `Infrastructure/DI/ExternalAccessModule.cs` (the invoices ReadableColumns)
> - `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs`
> - `src/client/external-spa/src/components/detail/RecordDetailFields.tsx`, `RecordDetailModal.tsx` and `tests/RecordDetailModal.test.tsx`
>
> **Live data:** `sprk_gridconfiguration` `3ff4102c-1092-f111-b8dc-7ced8ddc4a05` ("Outside Counsel — Invoices (external widget)") names the old column. It will be switched to `statuscode` (fetch, layout and column settings) by `Remove-EbillingInvoiceStatus.ps1 -GridConfig`, right after the BFF with the new allow-list is deployed and before the column is deleted from dev. Please tell us if you have unmerged work on that row or those files.

## 10. Final checks

Original run on `7c474b2c9` plus the docs, script and notes commit (no code change after `7c474b2c9`), 2026-10-17. The review fix round (section 11) re-ran the same suites; its numbers are in section 11.3.

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ -m:1` | succeeded, 0 warnings, 0 errors |
| `tests/unit/Sprk.Bff.Api.Tests`, filter `Finance\|Invoice\|Signal\|ExternalModule\|LiveFact\|FinancialCalculation` | 3960 passed, 0 failed, 8 skipped (live-only seams) |
| `tests/Spaarke.ArchTests` (all; the new guard included) | 1140 passed, 0 failed |
| `src/client/external-spa` `npx vitest run` | 36 files, 1019 passed. A first run alongside the dotnet runs had 24 load-induced failures (timeouts); two runs alone passed 1019 / 1019. |
| Live seam `FinanceBaseValuesSeamTests` (spaarkedev1; `SIGNALS_LIVE_CALLER_ID` set) | passed under D-98 (expected sum 500); its rows were cleaned up. NOT re-run for D-103 (it writes test rows; the fix round has no live writes). Expected sum is now 200. |

**Publish size (CLAUDE.md §10 bullet 3).** Both trees come from `git archive` of the base `16c76e7d8` and of `7c474b2c9`, extracted to `C:\tmp\t015\base` / `head`. Each was built with `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/` and zipped with `Compress-Archive -CompressionLevel Optimal`; both outputs have 192 files.

| Tree | Compressed |
|---|---|
| base `16c76e7d8` | 39,462,216 B = 37.634 MB |
| head `7c474b2c9` | 39,462,867 B = 37.634 MB |
| **Delta** | **+651 B** |

No `*.csproj`, `Directory.Packages.props` or `package.json` changed since the base, so there is no new package and no new CVE source.

## 11. Review fix round (2026-10-17): item map, evidence, open points

Code commits on top of `7c0daf8c9`: `62d51bf2e` (A), `22b95cdf6` (B, C), `450d6969f` (F, guard), `9e49bda82` (E); the SignalReevaluationJobTests fake (F) is in `62d51bf2e`; the notes update is `d249e9a50`. The follow-up round after uac-r2's and ontology-r1's re-checks is section 11.5.

### 11.1 Item to proof

| Item | Done | Proof |
|---|---|---|
| A. D-103 | `CountedInvoiceStatuses` = Approved, Sent to AP, Paid; comments, docs, PolicyVersionValidator remark cite D-103 / ontology D-166; harness default and the "exactly 50000" invoice are Approved; seam expected sum 200; one explicit test per path (`TheRollupQuery_FinanceRollupService_DoesNotCountAnInReviewInvoice`, `TheGroupedSumQuery_FinanceBaseValues_DoesNotCountAnInReviewInvoice`) plus the behaviour test `Run_SpendThreshold_...OnlyCountsTheCountedStatuses` (In Review 950000 ignored, ReadAsync path) | Mutation A in section 7: InReview put back fails 7 of 306 |
| B. Transition window | Module and both test pins admit both columns; each marked `transitional (015/D-97): remove after the dev column delete`; guard exemption names exactly the module line and the test's one constant | Mutations B1 and B2 in section 7; follow-up in 3.5 |
| C. Live-grid label | The invoices row of `LiveGridFetchXml` is the CURRENT live fetch (old column); the post-switch fetch is `TargetInvoicesGridFetchXml`, labelled target; new cases admit both | `ExternalModuleColumnAllowListTests`: 136 passed; B1 fails 7 of them |
| D. Notes | Old-worktree shas replaced by `e0df77339`, `b2d8cb6dd`, `7c474b2c9`, `d4e555e38`; runbook rewritten (3.5); the R3 rule recorded (3.5 and project CLAUDE.md section 6) | This file |
| E. Delete script | Section 5.6 | 11.2 |
| F. K2s | Single-definition guard flags `InvoiceStatusCodes.` (must-fire and must-not-fire controls); aggregate fake throws on an unmodelled condition | Mutations: private named-constant list in FinanceRollupService fails the guard (1 of 6); an extra `statecode` condition in the grouped query fails 19 of 234 SignalReevaluationJobTests with `The aggregate fake does not model a condition on: statecode` |
| G. Spend facts | Section 3.5 | Live read-only inventory re-run 2026-10-17 (all 10 invoices at (0, 1)) |

### 11.2 Delete script refusal evidence (`-WhatIf` runs; git-only checks, run before any Dataverse call; no writes)

| Case | Result |
|---|---|
| `-GridConfig -Delete` together | REFUSED, exit 2: cannot run together |
| `-GridConfig` with no `-BffDeployAttestation` | REFUSED, exit 2 |
| `-Delete` with no `-SpaDeployAttestation` | REFUSED, exit 2 |
| `-BffDeployAttestation deadbeef12` (unknown) | REFUSED, exit 2: not a commit in this repository (`git cat-file -e` failed) |
| `-BffDeployAttestation --output=x` | REFUSED, exit 2: not a commit sha |
| `-GridConfig`, BFF commit `16c76e7d8` (pre-015 allow-list) | REFUSED, exit 2: allow-list does not list `"statuscode"` |
| `-Delete`, BFF commit `2bda5ebe1` (R3 candidate: both columns admitted, 015's readers absent) | REFUSED, exit 2: `FinanceBaseValues.cs` still reads the old column. This is why runbook step 5 exists. |
| `-Delete`, SPA commit `16c76e7d8` | REFUSED, exit 2: `RecordDetailModal.tsx` still reads the old column |
| `-Delete`, BFF commit `16c76e7d8` | REFUSED, exit 2 (allow-list) |
| `-GridConfig -WhatIf`, BFF commit `2bda5ebe1` | passes the pre-flight; read-only inventory; `WOULD EDIT grid ... If-Match W/"21226563"`; nothing written |
| `-Delete -WhatIf`, both attestations = this branch's tip | passes the pre-flight; then REFUSED, exit 2, on the live grid blocker (`3ff4102c` not moved yet); nothing deleted |

Not exercised live (the column still exists on dev, and no write was allowed): the already-retired `-GridConfig` path (K2) and the HTTP 412 path (K1). Both were read, not run.

### 11.3 Final runs (after the last commit)

| Check | Result |
|---|---|
| `dotnet test tests/unit/Sprk.Bff.Api.Tests`, filter `Finance\|Invoice\|Signal\|ExternalModule\|LiveFact\|FinancialCalculation` | 4492 passed, 0 failed, 8 skipped |
| `dotnet test tests/Spaarke.ArchTests` (all) | 1166 passed, 0 failed |
| Build (the test runs build the BFF with warnings as errors) | succeeded |
| external-spa `npx vitest run` | not run: the fix round touches no SPA file |
| Publish size, round only (`7c0daf8c9` vs the tip, same procedure as section 10) | 39,554,739 B vs 39,554,767 B = **+28 B**; 192 files each |
| Live seam `FinanceBaseValuesSeamTests` | not run (writes test rows; no live writes this round) |

### 11.4 Open points and known limits

- Merge with R3: both branches edit the invoices `ReadableColumns` block (R3 puts `statuscode` on its own line with its own comment; 015 adds the old column on its own line with the `transitional` marker). Expect a trivial textual conflict; the resolution is the union, keeping 015's marker comment so the guard's exemption line stays exact. The follow-up commit then removes the old-column line on the merged branch.
- **Merge note: R3's text in `ExternalAccessModule.cs` names the column inline** (`a6513cd56:456` and `:460`). On merge, keep **015's marker line** (`"sprk_invoicestatus", // transitional (015/D-97): remove after the dev column delete`). Keeping R3's text instead fails `NoReader_OfTheRetiredInvoiceStatusColumn` and `EveryTransitionalExemption_IsStillNeeded` loudly (the exact exempt line is gone and another line names the column), and the `-Delete` pre-flight refuses that commit too (its `git grep` finds a module line that is not the exact marker line).
- K-class (no fix): `-Delete`'s attestation checks the three Finance readers the brief names, not every 015 reader (`FinanceSummaryService`, `FinancialCalculationToolHandler`, `InvoiceLiveFactResolver`). They arrive in the same commit as the three, so a build containing the three contains the rest (K4).
- The seam test's live run for D-103 and the K1/K2 live paths are proven by reading only (11.2, 11.3).

### 11.5 Follow-up round (2026-10-18): uac-r2 and ontology-r1 re-checks of the fix round

Commits on top of `be1c60e38`: `7965ef68e` (items 1, 2), `6c348fd86` (item 3); this notes update is the last commit. No live writes in this round (the grid switch already ran, section 12); every Dataverse call below is a read-only GET.

| Item | Done | Proof |
|---|---|---|
| uac F2: "never grows" | `TheExemptionList_NeverGrows` asserts the two exact file paths (in order) and that each exempt line contains `transitional (015/D-97)` (`ExemptionListProblem`); it no longer checks a count | Mutation: the test file's entry swapped for the same line in `FinanceSummaryService.cs` (still 2 entries): the test fails with "the exempt files must be exactly [...], found [...FinanceSummaryService.cs]"; the old `Length <= 2` check passed it. Restored. Control `TheExemptionListCheck_FiresOnAnyDeparture` (swapped file, reorder, dropped, extra, unmarked line). |
| uac K1: duplicate copies | The scan counts exempt hits per entry (`ScanLines`, `ExemptionUsageProblems`) and fails any count above 1 | Mutation: the marker line pasted twice in `ExternalAccessModule.cs`: `NoReader_...` fails with "the exempt line occurs 2 times". Restored. Must-fire control `TheDetector_FiresOnADuplicatedExemptLine`. |
| uac K2 (grid case) | After the case-sensitive Replace, `(?i)sprk_invoicestatus` still matching refuses (before any write, also under `-WhatIf`) | Harness (below), case C |
| ontology K4 (412) | On HTTP 412 the row is re-read: no longer naming the column (and not empty) reports "already applied" and returns (exit 0); otherwise the refusal stays | Harness, case D (five re-read outcomes plus a non-412 error rethrown unchanged) |
| `-Delete` content check | `Assert-NoRetiredColumnReaders`: `git grep -il sprk_invoicestatus <sha> -- src/server` on the BFF sha and `-- src/client/external-spa/src` on the SPA sha; any hit refuses except the one exact transitional line in `ExternalAccessModule.cs` (at most once); the specific checks stay | Section 11.5.1 |
| `-WhatIf` wording | Help (DESCRIPTION), the run header and the closing line say `-WhatIf` writes nothing but performs read-only GETs against Dataverse after the git pre-flight | Run output below |

**Interpretation, flagged:** the brief's single command covers both paths. Each attested commit is scanned over the tree its artifact is built from: the BFF sha over `src/server`, the SPA sha over `src/client/external-spa/src`. R3 deployed the BFF (03:34) before the SPA (03:36), so scanning the SPA tree at the BFF's commit would refuse a correct BFF attestation.

#### 11.5.1 Script evidence (read-only; the harness is a scratch file, not committed: it loads the script's functions by AST, stubs Dataverse and uses real git; cases A to D)

Final test runs on `6c348fd86` (the notes commit changes no code): `dotnet test tests/Spaarke.ArchTests` 1172 passed, 0 failed (the guard family alone, filter `InvoiceStatusRetirement|FinanceBaseValues`: 26 passed); `dotnet test tests/unit/Sprk.Bff.Api.Tests` filter `ExternalModule|Finance`: 1558 passed, 0 failed, 3 skipped.


- `-Delete -WhatIf`, BFF `9721819ed`: REFUSED, exit 2, at the existing allow-list check (that commit does not list `statuscode`), before the new scan.
- `Assert-NoRetiredColumnReaders` on `9721819ed` directly (real git): BFF tree REFUSED, 5 files: `FinanceBaseValues.cs`, `FinanceSummaryService.cs`, `InvoiceReviewService.cs`, `Tools/FinancialCalculationToolHandler.cs`, `Signals/PolicyVersionValidator.cs`; SPA tree REFUSED, 2 files: `RecordDetailFields.tsx`, `RecordDetailModal.tsx`. (The new scan catches `FinanceSummaryService.cs` and `FinancialCalculationToolHandler.cs`, which the three named-file checks never looked at.)
- The same function on the follow-up's code tip (`6c348fd86`): BFF tree and SPA tree pass.
- Stubbed `git`: nothing found passes; the exact marker line once passes (CRLF-trimmed); the exact line twice, a second module line naming the column, R3's inline text (`a6513cd56:456`), another file in `src/server`, and a git failure each REFUSE.
- `-Delete -WhatIf -OwnerApproval 'D-97 2026-10-17 owner'`, BFF = SPA = `6c348fd86`: pre-flight passes ("Pre-flight (git only, before any Dataverse call) passed"), then the read-only inventory: 3 rows with a value (all ToReview, statuscode 1), 10 invoices at (0, 1), forms / views / grid configurations 0, `DELETE VERDICT: READY`, every step `WOULD ...`, "nothing was written (read-only GETs against Dataverse did run, after the git pre-flight)". The delete itself was not run.
- `-GridConfig -BffDeployAttestation ebc74b331 -WhatIf`: pre-flight passes; the grid row has no reference ("skipped ... no reference"), as expected after 2026-10-18 03:42:42.

## 12. Live run log

| When (UTC) | Step | Evidence |
|---|---|---|
| 2026-10-18 03:34:49 | R3 deployed the BFF admitting both columns (ProductVersion 1.0.0+ebc74b331…) | R3 message |
| 2026-10-18 03:36:44 | R3 deployed the SPA reading statuscode (run 39094144398) | R3 message |
| 2026-10-18 03:42:42 | `-GridConfig -BffDeployAttestation ebc74b331`, WhatIf first then live: grid 3ff4102c moved sprk_invoicestatus → statuscode (3 mentions, If-Match W/"21226563") | Read back: 0 old mentions, etag W/"28505656". Delete verdict is still BLOCKED until a BFF with 015's Finance readers is live (R3 merges 015) |
| 2026-10-30 ~15:40 | `-Delete -WhatIf -BffDeployAttestation 9f4afe82ac -SpaDeployAttestation 96f1f6b675` | REFUSED (exit 2) before any Dataverse call: the SPA commit still names the column in a JSDoc example at `Spaarke.UI.Components/.../RecordHeader/fields/OptionSetField.tsx:64` (015's e0df773395 is on master, not in R3's SPA branch). The BFF attestation passed. Waits for R3's next dev SPA deploy from a commit containing e0df773395. Nothing written. |
| 2026-11-01 ~03:00 | `-Delete -WhatIf -OwnerApproval 'D-97 2026-10-17 owner' -BffDeployAttestation b083d62183 -SpaDeployAttestation b083d62183` | Pre-flight passed; 3 rows with a value (ToReview), 11 invoices at (0, 1), forms / views / flows / charts / grid configurations 0, one Copilot form-fill opt-out row; `DELETE VERDICT: READY`. Notices sent to R3 and uac-r2; R3: no objection; uac-r2: the live BFF is d0c1c5dc3c (deployed 03:09:00Z) |
| 2026-11-01 03:14:51 | `-Delete -OwnerApproval 'D-97 2026-10-17 owner' -BffDeployAttestation d0c1c5dc3c -SpaDeployAttestation b083d62183` | Deleted the aiskillconfig a32ae5a7 (form-fill opt-out), the column (attribute 150d244b; dependents 0 first), the global option set dfeedd70 (dependents 0), published sprk_invoice. Verify: the column 404; the option set still read once (exit 1, propagation) |
| 2026-11-01 03:15:20 | The same command again | "sprk_invoice.sprk_invoicestatus does not exist (already retired). Option set: gone." exit 0. **015 complete on dev.** |

**Observed side effect (uac-r2, 2026-11-01):** for the time the drop took to run (03:14:36Z–03:15:18Z), other services' queries that touched `sprk_invoice` failed with `Sql Number: 207` (invalid column name). There were 20 such faults on uac-r2's reconciler descendant reads; later passes did the work, and none have occurred since. For any future column delete, expect about a minute of these from any query on that table, and give peers the exact time window in the notice.

## 13. ontology-r1 re-check of the follow-up (2026-10-18)

Clean apart from these, fixed in the main session:
- F4: stale pre-cherry-pick hashes. They are replaced above by `7965ef68e` and `6c348fd86`; the patch-ids are identical.
- K2: the SPA content scan now also covers `src/client/shared/Spaarke.UI.Components/src`, which the SPA bundles through the vite alias `@spaarke/ui-components`. Today's only hit there is a doc comment in `RecordHeader/fields/OptionSetField.tsx:64`, which the SPA does not import.
- K4: the 412 re-read reports "already applied" only when the row names `statuscode` and not the old column. A row that names neither is refused, and a row that can't be re-read (404) is refused with an accurate message.
uac-r2's re-check of the same range: PASS, no new findings.

## #2095 - InvoiceReviewService re-file now runs AfterRefileAsync (found by uac-r2's guard, #2094)

Filed here because 015 is the last task with a notes file that changed `InvoiceReviewService`; task 050 owns the file next (it redirects the confirm path) and must carry this forward.

- **Defect.** `RefileDocumentUnderInvoiceAsync` re-parented the document under the confirmed matter (`ReparentAsync`, inherited matter) and never ran `SecureChildReconciler.AfterRefileAsync`. A document filed under an invoice of a Secure matter stayed readable by its business unit until the 2-minute net; one moved out of a Secure record released nothing until the sweep.
- **Fix.** The method reads whether the document is Secure-team-owned before the reparent (`RefileIsolationRead.BeforeWriteAsync`, one retry), then, after the reparent succeeds and moved a parent, calls `AfterRefileAsync("sprk_document", id, wasIsolated, CancellationToken.None)`. The constructor takes `SecureChildReconciler` (scoped, already registered). The resumed-confirm path (no-op change) runs through the same method, so a first attempt that died before the step is completed by the retry.
- **Deviation from the reference writers (accepted by uac-r2).** The unattended writers (the playbook node, the field-mapping push, the update action) write anyway when the read faults twice and release nothing, because a refusal there would fail a whole run over a transient read. Confirm is one interactive action that is safe to retry, so a double fault throws `InvoiceReviewException(IsolationUnreadable)` before any write, which `ConfirmFailure` renders as a 503 ProblemDetails with `reasonCode` `sdap.finance.invoice_review.isolation_unreadable`. The detail names no record and not the inner fault; the service logs a Warning with the exception and the endpoint logs the exception again. The fault test asserts zero writes of any kind on every fake.
- **Re-check fix (uac-r2 F1, K1).** F1: the resumed confirm ran the review stamp before the isolation read, so a double fault answered 503 "nothing was saved" with the document already stamped Confirmed and no job queued. The resume branch now reads isolation BEFORE `MarkDocumentConfirmedAsync` and hands the answer to the re-file (`wasIsolatedPreRead`); the first-attempt branch still reads inside the re-file. K1: a test cancels the request after the reparent and asserts the mirror still moves; it fails if `AfterRefileAsync` is passed `ct` (`FakeRecordShareTable.RevokeAccessAsync` now throws on a cancelled token, as the real transport does). Retry-After: no finance or Signals 503 sets it, so none is added.
- **Guard.** `InvoiceReviewService` is off the baseline in `RefileWriterAfterRefileGuardTests` and is now one of its non-vacuity "compliant" writers.
- **For task 050.** If the confirm path is redirected into the submission operation, the new filing path must read isolation before its reparent and call `AfterRefileAsync` after it; the guard will fail otherwise.
- **Tests.** 1 endpoint test in `FinanceEndpointsAuthorizationContractTests` (fault gives 503 with the code), plus 4 in `InvoiceReviewWritePathTests` (isolated to ordinary: mirror removed; ordinary to Secure: mirror created; double read fault: no write at all; refused reparent: step not run) and 1 DI-resolution test in `RefileWriterReconcilerRegistrationTests`.
- **Publish size.** Method bodies only: no package or registration changed.

