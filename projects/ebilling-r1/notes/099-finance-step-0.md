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
- **No new DI registration, and no package.** The filter resolves the existing `ICallerSystemUserResolver` and `ISystemUserIdentityResolver` with `GetService`. If either is missing, the filter refuses.
  - `ICallerSystemUserResolver` is registered unconditionally by `CommunicationModule.cs:301` (`TryAddScoped`). The registration at `AnalysisServicesModule.cs:1023` is inside `AddAnalysisOrchestrationServices`, which runs only when Analysis and Document Intelligence are both enabled (`:242-244`), so it is not the one step 0 can rely on. `ISystemUserIdentityResolver` is `NotificationsModule`'s, also unconditional.
  - `FinanceStepZeroCoverageGuardTests` now pins both (see "Review round 2").

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
2. **Placement of the uniform-404 tests.** The uniform-404 tests for the four UniformNotFound routes (`GET /invoice-submissions/{id}`, `GET /submissions/{id}/blocked`, `POST /matters/{id}/recalculate`, `POST /projects/{id}/recalculate`) live in the contract test files that own the hosts mapping those routes (`InvoiceSubmissionEndpointsContractTests`, `SubmissionOverrideEndpointsAuthorizationContractTests`, `FinanceEndpointsAuthorizationContractTests`), plus filter level in `FinanceStepZeroTests`. They compare a step-0 refusal with an internal caller's 404 for an absent id (review round 2).
3. **Test hosts.** Every Finance test host registers the real resolvers through the new shared helper `tests/integration/contract/Api/Finance/FinanceStepZeroCaller.cs` (moved from `tests/integration/Shared/` 2026-10-21: it references the unit assembly's `FinanceAuthzTestAuthHandler`, so it broke the four other projects that compile `Shared/`). Only the Dataverse seam is substituted, and the cache stores nothing. Without the helper every host refuses every caller (fail closed), which made 228 existing tests fail. (2026-10-22, after the master merge that brought UAC task 188: the helper became `tests/integration/contract/StatedCaller.cs`, the ONE stated caller that both step 0 and the external document narrowing hosts use; `AddStatedNarrowingCaller` now delegates to it.)
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
  - the uniform 404: a step-0 refusal is indistinguishable from an internal caller's 404 for an absent id (round 2).
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

**Publish size.** Measured from fresh exports at short paths: base `14a0cdf82c` (`origin/work/ebilling-r1`) against this branch's head.

| | Size |
|---|---|
| Base | 40,126,927 B (38.268 MB) |
| Branch | 40,128,321 B (38.269 MB) |
| Delta | **+1,394 B** |
| `Sprk.Bff.Api.dll` | +3,072 B |

- **Convention:** `dotnet publish -c Release … -r linux-x64 --self-contained false` (framework-dependent), then `Compress-Archive -CompressionLevel Optimal` over the output, PDBs included.
- **Comparison point:** the branch base, not master. A master comparison would measure the whole ebilling branch, not this task.
- **Ceiling:** well under 60 MB.

**CVE.** No package or project reference changed (the csproj is untouched), so no new CVE exposure. The vulnerable-package listing was not run.

**Conflict check.** `/conflict-check` was not run. It is for the main session before the PR; no PR was opened.

**Full unit suite.** 30761 passed, 77 failed, 28 skipped.
- None of the 77 failures is a Finance or step-0 test.
- Every failure was a multi-minute timeout, from running alongside the publish and the reviewer on the same machine.
- Rerun alone, the 48 affected classes passed 795 out of 795.

## Known limits (adversarial review pass 1; not fix-now)

- **K1.** `SystemUserIdentityResolver` caches the oid → systemuser mapping and `sprk_isexternal` for 10 minutes, so a flag change takes effect within at most 10 minutes. This is the same class as #1576. A live, uncached read would need an owner decision.
- **K2.** A blank flag counts as internal (owner round 67). A newly provisioned B2B guest therefore passes step 0 until `Set-ExternalFlagForB2BGuests.ps1` marks it.
- **K3.** The coverage guard checks that the filter is present, not that it comes first in the chain.
  - Today only `CodeSetForbiddenCodeFilter` runs earlier. It post-processes the result and stamps `errorCode` on step-0 403s too.
  - A future filter placed before `AddFinanceAuthorizationFilter` that reads records would not be caught.

### Known limits (review round 2; not fixed, no code change)

- **K1 (uac-r2).** Both `SystemUserIdentityResolver` lookups (oid to systemuser, and the `sprk_isexternal` read) use `TopCount = 1` with no ordering, and the cache keys carry no tenant prefix. Both predate task 099. The per-environment cache `InstanceName` covers the cache. The 10-minute TTL is that resolver's existing TTL, slower than the 60-second external-plane rights cache.
- **K3 (both reviews): JSON request bodies are bound before step 0.** Minimal API binds handler parameters before endpoint filters run, so a JSON body is read before step 0 (the code-set confirm at `CodeSetMappingEndpoints.cs:115`, the rule-set accept, publish, assign and visibility routes at `RuleSetEndpoints.cs:337-431`, and the invoice-review routes). A malformed JSON body gets 400 before step 0's 403. No record is read and nothing leaks, so the owner rule holds. A pre-binding group-level check is a possible follow-up if the cost ever matters; JSON bodies are small.
  - **The multipart routes are not affected, and this is now tested.** The three multipart routes (`InvoiceSubmissionEndpoints.cs:41-60`, `RuleSetEndpoints.cs:66`, `RateCardImportEndpoints.cs:61`) do not bind `IFormFile`: their handlers take `HttpContext` or `HttpRequest` and call `ReadFormAsync` themselves, after the filter chain. `.Accepts<IFormFile>("multipart/form-data")` is OpenAPI metadata only. So the "up to about 101 MB read before step 0" concern does not hold, and no handler change was needed. A recording body stream proves a refused caller's internal-submit request is never read (`StepZero_internal_submit_never_reads_the_request_body_of_a_refused_caller`), with an internal-caller control. Mutant (the filter reads the form before step 0): the 4 must-fire cases fail.

## Review round 2 (uac-r2 and a second adversarial pass)

uac-r2 and our second adversarial pass both found no F1 or F2. The fixes are in `786c3ec258` (tests) and `e3df854366` (guard); this note is the third commit.

| # | Finding | Fix | Evidence |
|---|---|---|---|
| 1 (F4) | The "byte-identical uniform 404" tests compared two step-0 refusals, equal by construction, so they could not fail. | Rewritten on all four UniformNotFound routes to compare a step-0 refusal with an INTERNAL caller's 404 for an absent id: status, Content-Type and body but `correlationId` and `traceId`. Contract hosts exist for all four; `FinanceStepZeroTests` also covers it at filter level. The internal caller's 404 comes from the handler on `invoice-submissions/{id}` and the recalculate routes, and from the filter's own rights denial on `/blocked` and at filter level. | Mutant: the step-0 404 given a different `detail`. 20 of the new tests fail (4 filter level, 4 `invoice-submissions`, 4 `/blocked`, 8 recalculate). The old tests would not have failed. |
| 2 (F4) | `FinanceStepZeroCaller` answered the `sprk_isexternal` read for any `systemuserid`, so a production bug passing the wrong id stayed green. | The fake returns the row only when the query's `systemuserid` equals the configured `SystemUserId`; otherwise no row. | **Mutant:** `CallerExternality` passes `Guid.NewGuid()` to `IsExternalAsync`. 407 of 2756 tests fail (the must-not-fire tests, and the refused kinds whose reason code depends on the read). Restored: 2753 passed, 0 failed, 3 skipped. |
| 3 (F4) | Model note §13 and these notes listed two uniform-404 routes. | Both list all four. | `notes/procedure-and-file-model.md` (the §5.3 table row, the §5.3 prose and §13). |
| 4 (F4) | These notes said `ICallerSystemUserResolver` was registered unconditionally in `AnalysisServicesModule`. | Corrected above (`CommunicationModule.cs:301`). | — |
| 5 (K4, cheap guard) | Nothing proved the caller resolvers stay registered when the AI module is not composed. | Two tests in `FinanceStepZeroCoverageGuardTests`: the Development and Production request scopes resolve the real `CallerSystemUserResolver` and `SystemUserIdentityResolver`; and the Communication and Notifications modules composed alone (no AI module) register both. | **Mutant:** `CommunicationModule`'s `TryAddScoped` removed. The no-AI test fails; the two booted cases stay green, because both boots turn AI on. Restored: green. |
| A | Multipart body read before step 0 (K3). | Not needed: see the K3 entry above. Recording-stream test added. | Mutant above: 4 fail. Restored: green. |
| B | The coverage guard recognised only a literal `api/finance` prefix. | Closed. A route is now judged when its path is under `api/finance`, or its handler (`MethodInfo` metadata, outermost declaring type) is in `Sprk.Bff.Api.Api.Finance`, or it runs `FinanceAuthorizationFilter` from a handler not on `NonFinanceFilterUsers`. | Must-fire: a real Finance-namespace handler (method group and lambda) mapped under `/api/other`, and an unlisted filter user, are reported. Must-not-fire: a flagged one, and the real Development and Production tables. |

**Results after the fixes.** Unit and contract tests filtered to Finance, StepZero, ExternalCaller, InvoiceSubmission and SubmissionOverride: 2753 passed, 0 failed, 3 skipped. ArchTests filtered to `FinanceStepZeroCoverageGuardTests|RouteAuthorizationGuardTests|ADR013`: 105 passed. ArchTests, full suite: 1319 passed, 0 failed. `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. No production code changed in this round.

**Finance handlers outside `Sprk.Bff.Api.Api.Finance`, and the reverse.** Measured on the booted Development table: 34 endpoints under `api/finance`, 34 with a Finance-namespace handler, and the two sets are identical. No Finance handler is mapped outside the prefix, and no `/api/finance` route has a handler from another namespace.

**The filter-marker selector needed a list.** `FinanceAuthorizationFilter` is the platform's one multi-id record-rights filter, not finance-only. Three non-finance handler classes run it on routes that are deliberately outside step 0 (owner 2026-10-21: step 0 is every `/api/finance` route; `ScorecardRoutes_RunNoStepZero` pins that): `ScorecardCalculatorEndpoints` (`/api/matters|projects/{id}/recalculate-grades`), `InsightsSearchEndpoint` (`/api/insights/search`) and `WorkspaceAiEndpoints` (`/api/workspace/ai/summary`). A literal "the filter marker implies the flag" selector would have failed on them, so they are listed in `NonFinanceFilterUsers`, each with its reason. `TheNonFinanceFilterUsersListIsCurrent` pins the list to the real table (a stale or missing entry fails), and a new, unlisted user of the filter outside Finance fails the guard until the owner decides. This departs from the instruction as written and is reported to the coordinator.

### Found on the way, not fixed (pre-existing, not task 099)

- **The BFF does not start with the AI gates off.** Booting `Program` with `Analysis:Enabled=false` (or both gates off) throws while the endpoint table is built: `ChatEndpoints` binds an `IChatClient` parameter that is not registered (`Failure to infer one or more parameters ... chatClient | UNKNOWN`), so `AuthorizationPolicyCache` cannot be constructed. `GateCombinationConstructibilityTests` ledgers the service-graph failures for those combinations but does not see this one, because its `ValidateOnBuild` boot fails first. Consequence here: a booted AI-off host cannot stand in for "AI off", so the K4 guard composes `CommunicationModule` and `NotificationsModule` alone. This needs filing as its own issue.

## Review record

- **Adversarial review pass 1** (opus, `adversarial-reviewer`): no fail-open path was found. Its findings and what happened to each:

  | Finding | Class | Outcome |
  |---|---|---|
  | Model note §5.3, §12 and §13 still described the narrow scope | F4 | Fixed (deviation 5) |
  | A cancelled request was logged at Error | K4 | Fixed: an `OperationCanceledException` raised when the request is aborted still refuses but is not logged as a fault |
  | The test helper did not check that the lookup asked for the caller's oid | K | Fixed: a query for any other oid now finds no row |
  | Caching, blank flag, filter order | K1, K2, K3 | Recorded above |

- **Still to do:** the second adversarial pass (auth tasks need two) and the uac-r2 review of the diff are for the main session.
