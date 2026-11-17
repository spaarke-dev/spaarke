# Task 113 - Procedure-change banner on the Invoice Review read (model 8.2, 15 answer 6)

Branch `worktree-agent-a066f68fe05c2b621`, base `f9f74de82e`. Rigor FULL. Status: built, awaiting review (no adversarial pass run here, by instruction).

## 1. What was built

`GET /api/finance/invoices/{invoiceId}/review` gains one nullable member, `procedureChange`: a state and its text, nothing else.

| State | Text | When |
|---|---|---|
| `Changed` | The procedures that apply to this invoice have changed; re-check to apply them. | the current resolution differs from the snapshot the invoice was judged under |
| `NotChanged` | (omitted) | no difference |
| `NotYetChecked` | Not yet checked | no usable snapshot (no findings yet, a row from before v5, a dropped / damaged / unknown-version member) |
| `CouldNotBeChecked` | Procedure changes could not be checked | any fault, a 429, the 5 s timeout, two versions in force (a publish in progress), an invoice that does not name exactly one core record, no work dates |
| absent (null, omitted from the JSON) | | the caller was not classed Internal by step 0; the invoice is decided (not In Review); the invoice has no firm; no evaluation ran (the stored findings carry no decision: held before any rule was judged) |

Computed once per request, after the review's own reads, app-only through `ProcedureResolver.ReadAsync` (082) for the invoice's single core record and firm; every judged work date is resolved in memory (`RulesOn`). Nothing is stored (no column, marker or cache entry).

| Piece | File |
|---|---|
| Banner + DTO (new) | `src/server/api/Sprk.Bff.Api/Services/Finance/Procedures/ProcedureChangeBanner.cs` |
| DTO member `ProcedureChange` (additive, last, default null, omitted when null) | `src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceReviewReadService.cs:46` (`InvoiceReviewDto`), documented at :31 |
| Service wiring: ctor takes the banner (:414), `GetAsync(id, caller, stepZero, ct)` overload, `SubmissionRead` keeps the raw findings text (:572) | same file |
| Route passes the step-0 verdict | `src/server/api/Sprk.Bff.Api/Api/Finance/InvoiceLineReviewEndpoints.cs:142` (`FinanceAuthorizationFilter.StepZeroClassedInternal`) |
| DI | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs` (`AddScoped<ProcedureChangeBanner>`) |
| Builder extraction | `src/server/api/Sprk.Bff.Api/Services/Finance/Submission/ProcedureSnapshot.cs` (`Build(BillingProcedureBasis)`) |

## 2. Decisions

1. **The current side is built with the writer's own builder.** The banner calls `ProcedureSnapshot.Build` over `ProcedureResolver`'s result and compares the `chain`, `overrides` and `resolutions` members with `JsonNode.DeepEquals`. A second hand-written shape would drift from the stored one. This needed one pure extraction in `ProcedureSnapshot` (the `BillingEvaluationResult` overload now delegates to a `BillingProcedureBasis` overload). The file is outside the POML's list; the change is behaviour-free (the existing 082d suites still pass).
2. **`applications` is not compared.** It is the evaluator's output over the findings, not the resolution; every input it derives from is in the three compared members.
3. **Dates judged = the evaluator's rule, in ONE helper** (`BillingJudgedDates`, used by the evaluator, the banner and `ProcedureAssignmentService.JudgedOnAsync`): the invoice date (else the stored `receivedOn`) plus each Fee and Expense line's date (else the invoice date); Adjustment lines and rows with no cost type are never evaluated. The stored columns are read, which already hold any correction.
4. **Invoices no Procedure judged are exempt** (member absent, not "Not yet checked"): an invoice with no firm (the resolver needs a firm), and an invoice whose stored findings carry no `decision` (the pipeline stores the 073 decision exactly when an evaluation ran; a submission with no firm, or held because its Procedures could not be resolved on the last delivery, stores none; `InvoiceSubmissionProcessor` and `BuildFindingsJson`). It is NOT keyed on a null snapshot: a correction, an added line and the size cap store a null snapshot on an evaluated invoice, which reads "Not yet checked". A firm with no Procedure is NOT exempt: "newly assigned" and "first version published" must fire.
5. **Step 0's verdict is read from the request**, never resolved again (`FinanceAuthorizationFilter.StepZeroClassedInternal`). The two-argument `GetAsync` keeps its signature and passes no verdict (absent).
6. **A fault never reads as "no change", and never fails the page.** Request cancellation propagates (it is not a state). The log names the invoice id, a cause token and the exception TYPE only (the message of a resolver or Dataverse fault names rows).
7. **The banner does not name `sprk_findingsjson`.** The review service passes the findings text in, so `FindingsJsonAppOnlyReadGuardTests`' closed list needed no edit.
8. **The 5 s timeout is a chosen constant** (`ProcedureChangeBanner.ResolutionTimeout`); the model gives a cost bound, not a number. It uses an injected `TimeProvider` so the test is deterministic. It is held two ways: the linked token stops a resolver that honours it, and `Task.WaitAsync(timeout, clock, ct)` holds the limit for a Dataverse call that ignores the token (`TimeoutException` maps to `CouldNotBeChecked`; request cancellation still propagates).

## 3. Guard (deviation from the POML's wording)

The POML asked for a "source-scan test" that no file under `Api/ExternalAccess` or the firm projection references the banner type. I wrote an IL-referrer guard instead, `tests/Spaarke.ArchTests/ProcedureChangeBannerCallerBoundaryGuardTests.cs`, modelled on `FirmFindingsProjectionCallerBoundaryGuardTests`: closed lists (banner: `InvoiceReviewReadService`, `FinanceModule`; DTO: the banner, `InvoiceReviewDto`, `InvoiceReviewReadService`), a reference counts by field, parameter, return, call, generic argument, type operand or string constant, and reading `InvoiceReviewDto.ProcedureChange` counts. What it covers, exactly: every type of the BFF assembly that names the banner, its DTO or `InvoiceReviewDto` (four closed lists: the banner; the banner DTO; `InvoiceReviewDto`: `InvoiceReviewReadService` and `InvoiceLineReviewEndpoints`), and every caller of the `GetAsync` overload that takes step 0's verdict (the route, plus the service's own verdict-less overload that delegates with none). It sees what a text search cannot. What it does NOT cover: a type that passes the verdict through a delegate or reflection, a test assembly, and the VALUE passed (a caller of the verdict overload could pass `null`; the route's wiring is proved by the contract test `Read_InternalCaller_OpenInvoiceWithAFirmAndFindings_CarriesTheProcedureChange`, not by this guard). The `FinanceStepZeroVerdict.Internal` constant itself is not listed: an IL scan sees an `ldc.i4`, not the enum member, so listing "who loads Internal" would list every enum use in the assembly (a known limit). Must-fire and must-not-fire controls are in the same file. New file, so the ArchTests route census and ledger files (owned by task 093) are untouched.

## 4. Tests

All pass.

| Suite | Count | Result |
|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Procedures/ProcedureChangeBannerTests.cs` (new; helper `ProcedureBannerWorld.cs` over `SecureChildShareWorld`) | 36 | pass |
| `InvoiceReviewReadServiceTests` banner wiring tests (names start "113") | 6 | pass |
| `tests/Spaarke.ArchTests/ProcedureChangeBannerCallerBoundaryGuardTests.cs` (new, two closed lists + controls) | 3 | pass |
| `InvoiceLineReviewEndpointsAuthorizationContractTests` (constructor only) | 82 | pass |
| Finance + Signals + review-endpoint + snapshot unit suites together | 5725 | 0 failed |
| Full `tests/Spaarke.ArchTests` | 2625 | 0 failed |

`dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror`: 0 warnings, 0 errors.

The matrix. Changed fires for: an override removed, an override added, an override of the invoice's own firm added, an override reason edited, a record rule switched off, a Procedure retired, a Procedure newly assigned, a version ended, a version retired, a first version newly published (Draft to Active), a version date moved, a line date moved across a version boundary. NotChanged (must not fire) for: the same resolution (nothing written), an override for firm F on firm G's invoice, a change on an unrelated record, an Adjustment line's date, an undated line, a missing invoice date with the stored received date. NotYetChecked (zero reads) for no findings, blank findings and seven unusable snapshot shapes. CouldNotBeChecked for: a resolver fault, a 429 (the log carries no row or reason text), a timeout (fake clock), two versions in force, no single core record; a cancelled request propagates. Absent for: no verdict, a decided invoice, no firm (zero reads). The resolver read count is 7 for 1 and for 150 lines; the page's own queries stay at 6. The serialised member is exactly `{"state":"Changed","text":"..."}` or `{"state":"NotChanged"}`.

Fail-before proof. The tests reference new types, so "before" is shown by mutating the new code and restoring it (byte-identical afterwards):

| Mutation of `ProcedureChangeBanner.cs` | Tests failed |
|---|---|
| `SameResolution` always true (never Changed) | 18 |
| `SameResolution` always false (always Changed) | 8 |
| the Internal / open gate made unreachable | 3 |

The first run of the new ArchTest also failed on the real tree (the record's generated `ToString` named the banner); fixed with a whole-word match.

Guard list edit: `InvoiceMoneyColumnsAppOnlyWriteGuardTests.Readers` gains `ProcedureChangeBanner` (it reads the review invoice's firm, `sprk_vendororg`, from the row the review already read; writes nothing). That file is not in the POML's list; the guard demands it.

## 5. Publish size (root section 10, bullet 4) and CVEs

Convention (`.claude/constraints/azure-deployment.md`): `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -r linux-x64 --self-contained false -o deploy/api-publish/` (framework-dependent linux-x64, Release, PDBs included: 4 files), `Compress-Archive -CompressionLevel Optimal -Path deploy/api-publish/*`, from fresh short-path worktrees (`C:\wt113m` at origin/master `acc96e1927`, `C:\wt113b` at this branch's code commit `fd24f8458b`). Both worktrees were removed with `Remove-WorktreeSafe.ps1`.

| Side | Files | Zip bytes | MiB |
|---|---|---|---|
| master | 191 | 42,311,906 | 40.35 |
| branch | 191 | 42,316,545 | 40.36 |
| Delta | 0 | +4,639 | +0.004 |

File counts match. Far below the +5 MB per-task trigger and the 55 / 60 MB cumulative thresholds. (Master is the merged master, so the delta also holds anything on `work/ebilling-r1` not yet merged to master; the total is still this small.)

`dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive`: no vulnerable packages (no HIGH, none at all). No package was added.

## 6. Known limits

- A banner is computed for the open invoice a reviewer opens; invoice lists, worklists and exports carry none (model 8.2: one route).
- The re-check itself (re-running the evaluation) belongs to ebilling-r2; this task only says that one is due. The review UI (task 110) renders the member; no client file is in this task.
- The comparison reads the live rows without a transaction: a non-atomic publish can show a false "Changed" or "could not be checked" for the moment it takes; the next read is right.
- A correction or an added line after a Procedure change stores a null snapshot (`ProcedureSnapshot.ForPatchedFindings`: the stored basis no longer describes the patched findings), so the banner then says "Not yet checked", not "Changed". It fails safe (it never reads as "no change"), but the reviewer is not told that the procedures changed until the explicit re-check (ebilling-r2) stores a fresh snapshot.
- Legacy (non-Procedure) rule-set changes are not compared: the snapshot stores the Procedure resolution only, so an edit to a legacy rule set after the evaluation gives no banner.
- Cost: the banner's reads run AFTER the page's own reads (it needs the stored findings the page reads first), adding about 7 sequential Dataverse round trips (the resolver's reads; the same for 1 and 150 lines) to every internal review load of an open invoice with a firm. Bounded by the 5 s limit; not parallelised with the page's reads.
- The table to `CoreRecordKind` switch is written out again in several services: `ProcedureChangeBanner.KindOf`, `InvoiceAdjustmentService` (about line 326), `InvoiceCorrectionService` (about 646), `InvoiceFindingsDiff` (about 590) and `InvoiceLineAddService` (about 1101), beside the canonical private `CoreRecordChain.KindOf` and the `InvoiceCoreRecordKind` mapping in `InvoiceSubmissionProcessor.ToResolutionCore`. Folding them into one shared mapping is a refactor across other tasks' files and was not done here (reported to the operator).
- The "judged dates" rule is now one helper (`BillingJudgedDates`), used by the evaluator, the banner and `ProcedureAssignmentService.JudgedOnAsync`. A fourth copy remains: `ProcedureAssignmentService.TakesTheChange` (084d, the dry run's close), which applies the same Fee/Expense and invoice-date fallback inline; it was left as it is (no behaviour change intended outside the three above).
- A distance-1 Secure parent contributes the one bit the evaluation itself already uses; the banner returns one state, not the basis, so it discloses no more than the evaluation.
- Drift between the stored columns and the dates the evaluator saw (an invoice or line date edited after the evaluation) gives a conservative "Changed" when the new dates cross a version boundary. That is the right direction (re-check).
- A `statuscode` the review does not know gives no member (the invoice is not known to be open).

## 7. Deviations and process notes

- `python --version` was run once by mistake in this session (a slip against the "never run python" rule); it printed a version and executed no script. Python was not used again.
- The POML lists `InvoiceReviewReadServiceTests.cs` for tests; the DTO-wiring tests are there, the matrix is in the new `ProcedureChangeBannerTests.cs` over a shared helper `ProcedureBannerWorld.cs` (in-memory, evaluating Dataverse: `SecureChildShareWorld`).
- The existing contract test and `InvoiceReviewReadServiceTests.World` construct the service, so each passes the new ctor argument (no behaviour change).
- rework: one review fix round (section 8).

## 8. Review fix round

1. Route wiring: contract test `Read_InternalCaller_OpenInvoiceWithAFirmAndFindings_CarriesTheProcedureChange` (real route, real filter, an in-memory resolver world). Mutation: the route passing `null` as the verdict fails it (2 cases).
2. The exemption keys on the stored `decision` member, shown with the REAL pipeline JSON (`InvoiceSubmissionProcessorTests` harness: no firm, and a final-delivery Procedures fault; `BuildFindingsJson`). The must-not-fire twins: an evaluated invoice with a null snapshot (size cap) reads "Not yet checked"; an evaluated invoice with its snapshot is judged.
3. `BillingJudgedDates` (new, pure). Difference found: the evaluator and `JudgedOnAsync` agree (invoice date plus Fee/Expense lines' dates, else the invoice date). The banner differed in one way: it excluded only Adjustment rows, so a row with no cost type counted for the banner but not for the evaluator or `JudgedOnAsync`; it now uses the shared Fee/Expense test. The fallback for a missing invoice date differs by design of the caller and is not part of the helper: the evaluator uses the submission's `ReceivedOn`, the banner the stored `receivedOn`, `JudgedOnAsync` `InvoiceAdjustmentService.InvoiceDateOf` (the invoice date, else the creation day). `JudgedOnAsync`'s behaviour is unchanged. The evaluator now takes its distinct dates from the helper (no behaviour change; the evaluator suites pass). Test `BillingJudgedDatesTests` compares the helper with `ProcedureBasis.Dates` of the real evaluator.
4. NotChanged against the real evaluator: `SnapshotOfTheRealEvaluator_*` store `ProcedureSnapshot.Build(evaluator result)` and read NotChanged, with the must-fire twin (a version ended) reading Changed.
5. The two stale `ProcedureSnapshot.cs` comments are corrected.
6. `WaitAsync` holds the 5 s limit for a token-ignoring read; tests for the timeout and for request cancellation during such a read.
7. Guard: closed lists for `InvoiceReviewDto` and for the callers of the verdict overload, with a must-fire control (section 3).
8. Known limits added (section 6); section 3 corrected.

## 113b: the banner on the Invoice Review page (2026-11-15)

No client rendered `procedureChange`: task 110 built the page before 113. Added on the owner's go.

- `src/solutions/InvoiceReview/src/components/ProcedureChangeNotice.tsx`, rendered in `InvoiceReviewPage`'s banner area after the firm-first notice.
  - Changed and CouldNotBeChecked show as warnings, with the server's text.
  - NotYetChecked shows as information, titled "Procedure changes".
  - An unknown state with text shows as information, so a future state is never read as "no change".
  - NotChanged, absent, or an unknown state with no text shows nothing.
- Fallback sentences copy the server's, and are used only when a state arrives without text.
- The banner blocks no action.
- Review: one Sonnet adversarial pass, with no F findings. Its K2 (unknown state) is closed as above.
- Known limits:
  - K1: the text says "re-check" but R1 has no re-check action (ebilling-r2).
  - K3: a warning MessageBar on first render is announced by screen readers, as the firm-first notice already is.
  - K4: the fallback constants are not tested against the server's.
- Tests: `__tests__/ProcedureChangeNotice.test.tsx`, 14 cases. The page suite passes 256/256.
- Deploy: the page (`sprk_invoicereviewpage`) has never been deployed. Its first deploy is part of task 112 (owner go), and 113b ships with it. The banner needs a BFF that carries #2839.
