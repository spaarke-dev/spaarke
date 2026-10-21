# Task 099: Finance step 0, implementation notes

Branch `task/ebill-099-step0` (from `origin/work/ebilling-r1` at `14a0cdf82c`). Rigor FULL (auth). Design source: `notes/procedure-and-file-model.md` §5.3. The owner decided on 2026-10-21 (re-plan Q3, POML `<notes>`) that step 0 covers every `/api/finance` route.

## What was built

- **The flag.** `FinanceInternalCallersOnly` is endpoint metadata, added by `RequireInternalFinanceCaller()` on every `/api/finance` route group: 11 `MapGroup` calls across 10 files, covering 34 routes.
  - The owner's "every route" decision supersedes the POML's per-route exception on `FinanceEndpoints.cs` and its allowlist (`/invoices/search`, `/summary`, `/invoice-review/reject`, recalculate, rate cards, code-set mappings).
- **The check.** It runs in `FinanceAuthorizationFilter.InvokeAsync` after the oid 401, and before the route's target declaration, any record, Document or Privilege check, and the handler (`RunStepZeroAsync`).
  - It refuses with the route's own `Deny()`: a 403 with `sdap.access.deny.internal_callers_only` (flag true, or no active row), or `sdap.access.error.system_failure` (a fault, or missing resolvers). On UniformNotFound routes the refusal is the uniform 404.
  - The log carries the oid that is already logged, plus the reason. It never carries the flag value or the systemuser id.
- **The verdict.** `HttpContext.Items[FinanceAuthorizationFilter.StepZeroVerdictKey] = FinanceStepZeroVerdict.Internal` is set only when step 0 ran and the filter let the request through. Read it with `FinanceAuthorizationFilter.StepZeroClassedInternal(ctx)` (for task 113).
- **Reuse, not a second resolution.** `ExternalCallerCommunicationGuard.IsExternalCallerAsync` moved, unchanged, to `Services/Ai/Context/CallerExternality.IsExternalCallerAsync(ClaimsPrincipal?, …)`, and the AI guard delegates to it.
  - The move was required: `ADR013_AiBoundaryTests` (D-66) forbids non-AI code from depending on `Services.Ai.Handlers.Dataverse`.
  - `Services.Ai.Context` already has non-AI consumers (`CommunicationRecordAuthorizationFilter`).
- **No new DI registration, and no package.** The filter resolves the existing `ICallerSystemUserResolver` (AnalysisServicesModule, unconditional) and `ISystemUserIdentityResolver` (NotificationsModule) with `GetService`. If either is missing, the filter refuses.

### §11 justification for the new surface

| Item | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `FinanceInternalCallersOnly` + `RequireInternalFinanceCaller` | None: the filter had no externality check. | It is metadata read by the existing filter. | A flagged-external systemuser reads invoices, findings, override reasons and rule content through internal routes. |
| `FinanceAuthorizationFilterAttached` marker | `WorkforceCeilingFilterAttached` is the precedent. | Endpoint filters cannot be seen from metadata. | A flagged route without the filter runs no step 0, and the coverage guard would still pass. |
| `CallerExternality` | `ExternalCallerCommunicationGuard.IsExternalCallerAsync` | Moved unchanged. Calling it in place breaks D-66. | — |
| `FinanceStepZeroVerdict` + `StepZeroVerdictKey` | — | — | Task 113 cannot tell which callers step 0 classed as internal. |

## Deviations from the POML

1. **Scope.** The flag is on every `/api/finance` route, and the coverage-guard allowlist is empty, per the owner's 2026-10-21 decision. The two negative tests the POML asked for, on `/invoices/search` and `/summary`, therefore became:
   - `/api/matters|projects/{id}/recalculate-grades` (scorecard, outside the prefix) run no step-0 lookup;
   - the external `/api/v1/external/.../invoice-submissions` route runs no step-0 lookup.

   Both are in the contract tests.
2. **Placement of the uniform-404 tests.** The byte-identical uniform-404 tests for `GET /invoice-submissions/{id}` and `GET /submissions/{id}/blocked` live in `InvoiceSubmissionEndpointsContractTests` and `SubmissionOverrideEndpointsAuthorizationContractTests`, which own the hosts that map those routes. They are not in `FinanceEndpointsAuthorizationContractTests`. That file holds the per-route tests for the routes it hosts.
3. **Test hosts.** Every Finance test host registers the real resolvers through the new shared helper `tests/integration/Shared/FinanceStepZeroCaller.cs`. Only the Dataverse seam is substituted, and the cache stores nothing. Without the helper every host refuses every caller (fail closed), which made 228 existing tests fail.
4. **Ledger entry.** `RouteAuthorizationGuardTests.Ledger.cs` gained a `NotAuthorizationForm("RequireInternalFinanceCaller")` entry. It is metadata that narrows the credited `AddFinanceAuthorizationFilter`, with `RequireSpeAdminPlatformOperator` as the precedent. The census test needs every authorization-shaped form to be classified.
5. **Model note.** `notes/procedure-and-file-model.md` §5.3, §12 and §13 were amended to the owner's widened scope (review finding 1, F4).

## Tests

- **`tests/unit/Sprk.Bff.Api.Tests/Api/Filters/FinanceStepZeroTests.cs`** (20 tests, filter level, real filter and real resolvers). Coverage:
  - the four refused kinds, under both Forbidden and UniformNotFound: no target declaration, no rights call, no handler call;
  - flag false and flag blank, which pass with the verdict set to Internal;
  - an internal caller without rights, who gets the record denial and no verdict;
  - an unflagged route, which runs no lookup;
  - missing resolvers, which refuse;
  - no oid, which returns 401 before step 0;
  - the uniform 404, which is byte-identical.
- **Per-route theories in each Finance contract test file**, covering all 34 routes: must-fire for each refused kind, and must-not-fire for flag false and flag blank.
- **`tests/Spaarke.ArchTests/FinanceStepZeroCoverageGuardTests.cs`.** It runs on the booted BFF (Development and Production): every `/api/finance` endpoint carries the flag and the filter marker, with at least 30 routes. It also has a must-fire test (planted, unfiltered, slash-less and `~/` routes) and a must-not-fire test.

### Results

| Run | Result |
|---|---|
| Finance-filtered unit and contract tests, plus the AI guard handler tests | 2850 passed, 0 failed, 3 skipped |
| ArchTests, full suite | 1314 passed, 0 failed |

### Seeding proof

- **Step-0 branch disabled** (`var stepZero = false && …`). The Finance-filtered run gave 169 failed and 2519 passed: all 148 must-fire tests, 16 must-not-fire tests that assert the step-0 lookup ran, the fail-closed test and the 4 byte-identical tests. With the branch restored, every test passes.
- **Flag removed from the `InvoiceContextEndpoints` group.** The coverage guard failed with "GET /api/finance/invoices/{invoiceId:guid}/context has no step-0 flag". With the flag restored, it passes.

## Publish size, CVE, conflict check

See the "Publish size" section below; it is filled in after measurement.

## Known limits (adversarial review pass 1; not fix-now)

- **K1.** `SystemUserIdentityResolver` caches the oid → systemuser mapping and `sprk_isexternal` for 10 minutes, so a flag change takes effect within at most 10 minutes. This is the same class as #1576. A live, uncached read would need an owner decision.
- **K2.** A blank flag counts as internal (owner round 67). A newly provisioned B2B guest therefore passes step 0 until `Set-ExternalFlagForB2BGuests.ps1` marks it.
- **K3.** The coverage guard checks that the filter is present, not that it comes first in the chain.
  - Today only `CodeSetForbiddenCodeFilter` runs earlier. It post-processes the result and stamps `errorCode` on step-0 403s too.
  - A future filter placed before `AddFinanceAuthorizationFilter` that reads records would not be caught.

## Review record

- **Adversarial review pass 1** (opus, `adversarial-reviewer`): no fail-open path was found. Its findings and what happened to each:

  | Finding | Class | Outcome |
  |---|---|---|
  | Model note §5.3, §12 and §13 still described the narrow scope | F4 | Fixed (deviation 5) |
  | A cancelled request was logged at Error | K4 | Fixed: an `OperationCanceledException` raised when the request is aborted still refuses but is not logged as a fault |
  | The test helper did not check that the lookup asked for the caller's oid | K | Fixed: a query for any other oid now finds no row |
  | Caching, blank flag, filter order | K1, K2, K3 | Recorded above |

- **Still to do:** the second adversarial pass (auth tasks need two) and the uac-r2 review of the diff are for the main session.
