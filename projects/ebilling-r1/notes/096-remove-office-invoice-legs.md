# 096 remove the Office server legs for Invoice as a save target and invoice quick-create

Branch `task/ebilling-096`, base `5fd8013fa8`. Owner decision L: an invoice is created only by submission.

## Step 0 gate (confirmed 2026-11-23)

- word-add-in-r1 removed both add-in legs in slice C: PR #2927, merge `148ec4dec0`, live on dev 2026-11-21.
- The next add-in release was #2999 (merge `1aedc9ea74`, deploy-office-addins run 45227203432, live 2026-11-23 17:26Z). It was client-only.
- Escalation grep (any client other than the Office add-in sending an invoice save or invoice quick-create): only `src/client/office-addins` references these routes; the add-in guards with `isSaveTargetType` and an Invoice prediction of null. No other client sends them. No escalation.

## Behaviour after

- `POST /api/office/save` with a TargetEntity type of invoice (any casing, `sprk_invoice`, `Invoice`) answers 400 ProblemDetails `errorCode: OFFICE_023`, title "Invoice Target Not Supported". The refusal runs in `EntityAccessFilter`, ahead of the caller-rights read (`CallerRecordAccessProbe`), so no job row, capture, upload or Dataverse call happens. `ValidateSaveRequest` repeats it as defence in depth.
- `POST /api/office/quickcreate/invoice` answers the same 400 `OFFICE_023`. It is refused in `QuickCreateSourceAccessFilter` (Half 0, parsed with the same `Enum.TryParse` the handler uses, so `"2"` and `"matter,invoice"` are refused too) and again in `QuickCreateAsync`.
- `OfficeErrorCodes.InvoiceTargetRetired = "OFFICE_023"`, `ProblemDetailsHelper.OfficeInvoiceTargetRetired`.
- The picker never offers an invoice as fileable: `OfficeSearchService.EvaluateFilingAccessAsync` returns `canFile=false` for an invoice (like the account row), so the pickable-equals-savable parity holds.

## Grep hit list (Office server code)

| Hit | Decision | Reason |
|---|---|---|
| `OfficeService.QuickCreateAsync` invoice leg (generic `Entity("sprk_invoice")` create, owner resolution, `StampCreatorOn`) | REMOVED | the leg this task retires |
| `InvoiceDescriptionAttribute`, `InvoiceAssignedToAttribute` constants | REMOVED | unused after the leg |
| `QuickCreateFieldRequirements` Invoice arms (`GetLogicalName`, `GetDisplayName`, `Validate`) | REMOVED | nothing creates or validates one |
| `QuickCreateSourceAccessFilter.CreatePrivilegeFor` Invoice arm | REMOVED | named in the brief; Matter and Project arms remain |
| `"invoice"` in `validEntityTypes` of `ValidateSaveRequest` | REMOVED (refused first) | OFFICE_023 |
| `QuickCreateEntityType.Invoice` enum value | KEPT | only so the route value parses and is refused with OFFICE_023 |
| `EntityAccessFilter.EntitySetByType` invoice rows | KEPT | shared with the upload route and Compose; lockstep with `DocumentAssociationMap` |
| `DocumentAssociationMap` | KEPT | shared; brief |
| `OfficeDocumentPersistence.DirectAssociationAttributes` (`sprk_invoice`) and the collision report | KEPT | brief |
| `OfficeSearchService` Invoice search (`AssociationEntityType.Invoice`) | KEPT | brief |
| `OfficeService` To Do "Related to" `["Invoice"] = ("sprk_regardinginvoice","sprk_invoice")` | KEPT | brief |
| `AnalysisAuthorizationFilter`, `SemanticSearchAuthorizationFilter` invoice rows | KEPT | not Office save or quick-create |

## Tests

New (fail before, pass after):
- `tests/integration/contract/Api/Office/OfficeInvoiceTargetRefusalContractTests.cs`: 22 cases (save by casing, alias, numeric and comma-joined spellings; quick-create; each proves no job row, capture, upload, rights read or Dataverse write; must-not-fire controls for Matter, Project and a Matter-only save). Against the old source 17 failed and 5 passed (the controls); against the new source 22 of 22 pass.
- `tests/integration/regression/OfficeInvoiceLegsRouteRetirementTests.cs`: 4 retirement tests (ADR-038 A3).

Deleted (ADR-038 Amendment A3, named in the PR): `tests/integration/regression/Issue1079_InvoiceQuickCreateNameTests.cs` (it asserted the invoice quick-create name, a removed behaviour; replaced by the retirement test above).

Updated because they asserted removed behaviour:
- `tests/integration/contract/Api/Office/OfficeQuickCreateContractTests.cs`, `OfficeQuickCreateFieldsContractTests.cs` (invoice rows replaced by the refusal; Matter and Project kept)
- `tests/integration/contract/Api/Office/OfficeCallerDataverseDouble.cs`, `OfficeCallerDataverseDoubleTests.cs` (the Issue1079 caller removed)
- `tests/integration/data-mutation/RecordOwnership/OfficeRecordOwnershipTests.cs` (invoice quick-create ownership case removed)
- `tests/integration/contract/Api/Office/OfficeFilingAccessParityContractTests.cs` (invoice is now never fileable; OFFICE_023 counts as a refusal)
- `tests/integration/auth/UnifiedAccessControl/EntityAccessFilterCharacterizationTests.cs` (the `sprk_invoice` case deleted, not inverted: there is no behaviour left to characterize; the refusal is pinned by the new tests)
- `tests/Spaarke.ArchTests/FeatureRolePrivilegeGuardTests.cs` (role-privilege evidence rows)
- `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` (the `QuickCreateAsync` owner-write and person-bearing entries removed with the leg)

## Results

- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Office|FullyQualifiedName~QuickCreate"`: 1142 total, 1135 passed, 0 failed, 7 skipped (the skips are pre-existing `[Fact(Skip=...)]`).
- ArchTests, filtered only (`RouteAuthorization|Office|WriterOnly|RecordOwnerAssignmentCensus|Adr038|LayerDependency|Credential|FeatureRolePrivilegeGuard`): 303 total, 303 passed, 0 failed. (The first run found the 3 census failures above; fixed.) The whole suite was not run.
- Build: Sprk.Bff.Api and the test project build with 0 errors.

## BFF hygiene (root CLAUDE.md section 10, NFR-01)

- Command: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` from fresh short-path worktrees (`C:\wt096m` at `origin/master` `1aedc9ea74`, `C:\wt096b` at the branch tip `3f10b25223`). The csproj publishes framework-dependent linux-x64. PDBs included. Zip: PowerShell `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`.
- master: 191 files, 42,615,274 bytes = 40.641 MB. branch: 191 files, 42,577,770 bytes = 40.605 MB. Delta -0.036 MB (file counts match). Far below the +5 MB, 55 MB and 60 MB thresholds.
- Note: the branch base is `5fd8013fa8`, not master `1aedc9ea74`, so the delta also carries any other ebilling difference between them; it is at most this size.
- `dotnet list package --vulnerable --include-transitive` on Sprk.Bff.Api: no vulnerable packages. No package added or upgraded. No new HIGH CVE.
- Worktrees removed with `pwsh -File scripts/worktree/Remove-WorktreeSafe.ps1`.
- Component justification (section 11): no new surface beyond one error code and one helper; the refusal reuses the existing ADR-019 `OfficeValidationError` shape.

## No sprk_invoice create anywhere in Office code

`grep -rnE '"sprk_invoice\b' Services/Office Api/Office Api/Filters Models/Office` leaves only: `OfficeDocumentPersistence.cs:47` (collision report), `OfficeSearchService.cs:492` (search), `OfficeService.cs` To Do "Related to", `EntityAccessFilter.cs` (entity-set map and the refusal), `AnalysisAuthorizationFilter.cs`, `SemanticSearchAuthorizationFilter.cs`. None creates a row. The new `OfficeSources_NeverCreateASprkInvoice` source scan in `OfficeInvoiceLegsRouteRetirementTests` pins this.

## Noticed, not changed (out of scope)

- Stale text listing "Invoice" as a valid association type in `ProblemDetailsHelper.OfficeInvalidAssociationType` / `OfficeAssociationRequired`, `OfficeProblemDetailsExtensions.cs` and `OfficeProblemException.cs`; `OfficeProblemDetailsTests.cs:90` pins that text.
- `FeatureRolePrivilegeGuardTests` evidence rows are checked for file existence only, so their line numbers can drift.
- The D3 role privilege `prvCreatesprk_Invoice` is still held by Core User and Office Add In User; the role change stays deferred per the 096b notes.
- `MembershipOptions.TargetingOnlyColumns` rationale for `sprk_assignedto1` now has no Office writer (comment updated only).
- Not verified: whether an `IdempotencyFilter` cached answer for an invoice save made before this release can replay within its key lifetime. Not investigated.
- `Enum.TryParse` accepts an undefined numeric value such as `"7"` for quick-create (pre-existing).
