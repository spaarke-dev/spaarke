# ISS-034 (#2888), task iss034d: the History tab shows "Finished by {name}"

> **Task:** `tasks/iss034d-history-finished-by.poml` (FULL) · **Decision:** EB-D-257 (owner) · **Base:** `task/ebilling-iss034d` from 07efff3312 (`work/ebilling-r1`) · **Delivers:** iss034c AC5.
> Code-review, adr-check and the adversarial pass are for the coordinator.

## What changed

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceReviewReadService.cs` | `ReviewDecisionRecordDto` gains a defaulted last member `CompletedBy` (`ReviewCompletedByDto(string Name)`, omitted from the JSON when null). `WithCompletersAsync` fills it (below). |
| `src/server/api/Sprk.Bff.Api/Services/Finance/SubmissionOverrideService.cs` | `ParseRoot` is `internal` (was `private`) so the review read reuses it with `ReadStamp`; no behaviour change. |
| `src/solutions/InvoiceReview/src/services/invoiceReviewApi.ts` | `ReviewDecisionRecord.completedBy?: { name: string } \| null`. |
| `src/solutions/InvoiceReview/src/components/tabs/HistoryTab.tsx` | One `Caption1` line "Finished by {name}" under the decision's `RecordRow` (Fluent tokens only), shown only when `completedBy.name` is present. It sits beside the row, not in its closed accordion, so it is visible without opening the entry. |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceReviewReadServiceTests.cs`, `src/solutions/InvoiceReview/src/__tests__/InvoiceDecisions.test.tsx` | Tests (below). |

No new write, endpoint, schema, column, package or DI registration. The route and its authorization are unchanged: the name leaves the server only inside the review the caller already reads.

## How the completer is found

`WithCompletersAsync` runs after the review's own reads, on the findings JSON the review already holds (no extra submission read). For each of the two verbs (`override` / `override-submission`, `return` / `return-to-firm`) it takes `SubmissionOverrideService.ReadStamp`. An entry gets a completer only when the stamp names a completer, the completer differs from the stamp's decider (`BySystemUserId`), and the history holds a record with the stamp's `RecordId` and that verb's action code.

The names are read in ONE `systemuser` query (`fullname`, `systemuserid In (...)`, at most two ids), and only when at least one entry qualifies, so a review with no operator-finished decision reads exactly what it read before. The number of reads does not depend on the number of history rows. The `decidedBy.name` of a record is the Dataverse lookup name on `sprk_confirmedby`, which Dataverse supplies with the row; a stamp holds a bare user id, so a name read is the only way to resolve it, and it is the same app-only read the review's other lookups use.

A fault (unreadable or malformed stamp, a failed user read, a missing or blank name) drops `completedBy`: the history is returned as it was, and the fault is logged as a warning with the invoice id and exception type only. A cancelled request still cancels.

## Tests

| Acceptance criterion | Test (`InvoiceReviewReadServiceTests`, `iss034d:`) | Cases |
|---|---|---|
| AC1: an operator-finished Override and Return give `completedBy.name` on their entry only; the decider is unchanged | `OperatorFinished_GivesCompletedByOnItsEntry` | 2 |
| AC1: the decider's own completion, a stamp without a completer, and a stamp naming another record give none and read no user | `NoCompleter_GivesNone_AndReadsNoUsers` | 3 |
| AC1: a stamp for one verb naming a record of the other verb gives none | `StampNamingARecordOfAnotherVerb_GivesNone` | 1 |
| AC2: a failed user read, a missing user and a blank name give none; the review returns; the log carries the invoice id and no user id or message | `UserReadProblem_GivesNone_AndTheReviewReturns` | 3 |
| AC2: an unreadable findings column, a malformed completer, no submission and no history give none; the review returns | `UnreadableStamp_GivesNone_AndTheReviewReturns` | 1 |
| AC2: one user read for Override and Return together, for 2 and for 60 history rows | `UserReads_DoNotGrowWithTheHistoryRows` | 2 |
| Wire: `completedBy` is on the JSON only when present | `CompletedBy_IsOnTheWireOnlyWhenPresent` | 1 |
| AC3 (Jest, `InvoiceDecisions.test.tsx`, "History pane") | shows "Finished by {name}" once, on the right entry; none for an absent, null or blank `completedBy` | 1 |

**Fail-before runs.** (1) The service seeded to the old behaviour (`WithCompletersAsync` returns the history unchanged): 6 of the 79 `InvoiceReviewReadServiceTests` failed (the positive cases; the must-not-fire cases passed). (2) The History tab line removed: the new Jest test failed (1 failed, 4 other History tests passed). Both were restored and re-run green.

## Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | 0 warnings, 0 errors |
| BFF tests, filter `InvoiceReview\|SubmissionOverride` | 693 passed, 0 failed |
| ArchTests, filter `Finance\|Route` | 195 passed, 0 failed |
| InvoiceReview `npx jest` | 16 suites, 563 passed, 0 failed |
| InvoiceReview `npm run typecheck` | surface-owned errors 0 (99 pre-existing in shared libs) |
| InvoiceReview `npm run build` | succeeded |

## Component justification (CLAUDE.md section 11)

One defaulted member on an existing DTO and one private method in an existing service; no new component or route. Existing: the review read and its `decisionHistory`. Cost of doing nothing: a reviewer cannot see who finished a decision someone else recorded (iss034c AC5).

## Known limits

- **K1. Not shown for a stamp whose completer is the decider** (nothing to add) or a stamp written before iss034c (no completer recorded). By design.
- **K2. The name is the user's current `fullname`.** A renamed user shows the new name; a deleted or unreadable user shows nothing.
- **K3. Only the submission's current stamp is read.** A later stamp for the same verb replaces the earlier one (the existing stamp contract), so an older record of the same verb would show none. A re-hold (`InvoiceSubmissionRerun` removes the override/return stamp) makes "Finished by" disappear from the old record; the operator's own finish record still shows who acted.
- **K4. The shared library's `BlockedDecisionStamp` copy** is untouched (iss034c K3 stands).
- **K5. Where the completer comes from.** The completer id is read from stored data with the app identity; only internal reviewers see the name. The column is field-secured to the BFF writers and System Administrator, so no other user can change it; pinned by `FindingsJsonFieldSecuredGuardTests` (uac-r2 accepted, see uac-r2 thread).
- **K6. A completer equal to the entry's own decider** (`decidedBy.id`) is dropped as well as one equal to the stamp's decider, so an entry never reads "Decided by X" and "Finished by X" together (review fix round 1).
