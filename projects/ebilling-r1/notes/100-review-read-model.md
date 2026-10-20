# Task 100 - Invoice Review read model and line decisions (FR-31)

Branch `worktree-agent-a2ab51c57f4224486`, base `9b272493b`. Rigor FULL.

## 1. What was built

| Piece | File |
|---|---|
| Read model | `Services/Finance/InvoiceReviewReadService.cs` (`GET /api/finance/invoices/{invoiceId}/review`) |
| Decisions | `Services/Finance/InvoiceLineDecisionService.cs` |
| Routes | `Api/Finance/InvoiceLineReviewEndpoints.cs`: `POST .../lines/{lineId}/decision`, `POST .../lines/decisions`, `POST .../lines/accept-clean` |
| Policy key | `ebilling.invoice.decide` = Write in `OperationAccessPolicy` (D-77: any Write holder decides) |
| Registrations | two `AddScoped` lines in `FinanceModule.cs`, one `Map...` line in `EndpointMappingExtensions.cs` |
| Additive change to 049 | `InvoiceSubmissionPersister.BuildFindingsJson` stores `suggestedAdjustment {kind, amount}` per finding (not for a withheld group, D-112) |

Every route: ADR-008 `AddFinanceAuthorizationFilter` (core-record access through the invoice, `finance.read` for the read and `ebilling.invoice.decide` for the three writes), then `RequireEbillingEnabled` (503 `ebilling.disabled`). 401 without a token, 403 without the right, uniform 404 for a missing or foreign invoice or line, 409 `ebilling.invoice.not_in_review` outside In Review.

## 2. CLAUDE.md section 11 (Existing / Extension / Cost of doing nothing)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `InvoiceReviewReadService` | `InvoiceRelatedWorkService` reads one invoice's related rows; the Signal read route reads Signals | Neither returns lines + stored findings + extraction fields + decision history in one shape; the Signal route strips text (D-123), so findings come from the stored `sprk_findingsjson` | The reviewer UI (101) has no read to render |
| `InvoiceLineDecisionService` | `InvoiceTotalsService` (016) owns totals, `BillingDecisionRecorder` owns Decision Records | Both are called, neither can validate, serialise and apply a line decision (status, approved amount, reason, reviewer) | There is no way to decide a line at all (FR-31) |
| 4 endpoints (new file) | Existing `Api/Finance/*Endpoints.cs` files cover other resources | A new route group on the invoice resource; same filters as its neighbours | No HTTP surface for 101/103 |
| `ebilling.invoice.decide` | `finance.read`, `finance.write` keys | A separate key so deciding can be tightened later without changing reads (D-77 maps it to Write) | Reads and decisions share one gate, and the completeness test would not pin the decision gate |
| 2 `AddScoped`, 1 `Map` | n/a | n/a | The services/routes are unreachable |
| `suggestedAdjustment` in the stored findings | 049 persisted rule, clause, location, values but not the suggestion | Additive member of the same JSON (older readers ignore it) | The reviewer cannot "apply suggestion" |

## 3. Serialisation and exactly-once

Named mechanism: a per-invoice processing lock (`IIdempotencyService.TryAcquireProcessingLockAsync`, key `ebilling-line-decision:{invoice:N}`, 2 minute lease) held for the whole request, plus a row-version-conditional all-or-nothing line write (`BulkUpdateIfRowVersionsMatchAsync`; a `DBConcurrencyException` is 409 `ebilling.line.changed`).

- A second request on the same invoice while the first holds the lock gets 409 `ebilling.invoice.busy`, before it reads anything.
- Defence in depth: with the lock defeated, two concurrent same-id requests still produce exactly one applied request and one 409, and one Decision Record (test `ConcurrentSameDecisionId_WithoutTheLock_IsStillExactlyOnce_ByTheConditionalWrite`).
- Decision Records are idempotent by `BillingDecisionKey` (subject, decision id) plus content hash, one per line (D-75). A repeat of the same decision id writes no second record and returns `alreadyRecorded: true`.
- Write order: validate everything (dry run of the totals, the recorder's no-I/O `Preflight`) -> lines (all or nothing) -> 016 recalculation -> correction rows -> Decision Records. A failure after the lines are written returns 503 `ebilling.decision.incomplete` with `committedLineIds` and `failedStep`; the caller repeats the same decision id through the multi-line route and every step is idempotent (tests: record, totals, corrections).

## 4. Behaviour

- Single and multi-line decisions: Accept -> Approved, approved = billed; Reduce -> Reduced, approved amount required, `0 <= approved <= billed` (credit rules D-127..129); Disallow -> Disallowed, approved 0. Reduce and Disallow need a reason (<= 2000 chars, 400 `ebilling.line.reason_required` / `reason_too_long`). An amount on Accept or Disallow is 400. Pending lines are 409 `ebilling.line.not_decidable`. Adjustment lines are refused.
- After a decision the line carries status, approved amount, reason, reviewer (`sprk_reviewedby`) and time; 016 recalculates `sprk_approvedtotal` (I-1).
- Decider = the caller's systemuserid (WhoAmI on the OBO token), never the body; unresolved -> 403 `ebilling.decider.unresolved`.
- Accept all Clean: only `Clean` Fee/Expense lines, and for each AI-sourced field one "Accepted in bulk" correction row with `sprk_islabel` false (D-64; deterministic ids, so a retry upserts). Multi-line is all or nothing; a foreign line id is a uniform 404 with nothing written.
- Read model: header, totals, lines with status (Pending shown as Processing, D-58), extraction fields with confidence and page/box, findings (policy, version, clause, location, values), suggested adjustment, held/overridden flags, decision history, `canDecide`. A withheld group stores no detail (D-112).

## 5. Dataverse read count

The review read issues 5 reads whatever the line count: invoice, lines (one paged query), then in parallel the latest submission, Decision Records for the invoice and its lines (one OR query), and the roster (only when a line has a resolved timekeeper), plus the cached settings threshold and the caller's right check. Test `ReadCount_DoesNotGrowWithTheLineCount` asserts the same count for 3 and for 150 lines.

## 6. Seeding proof and fail-before evidence

Each production change below was reverted by a one-line mutation, the named tests failed, the mutation was restored and the suite re-run green.

| Mutation | Failed tests |
|---|---|
| Authorisation filter removed from the read route (the seeding proof: the core-record access test `Denied_WithoutTheRight_ReadsAndWritesNothing` fails) | 5 contract tests |
| Authorisation filter removed from the three decide routes | 15 contract tests |
| `RequireEbillingEnabled` removed | 4 (`FlagOff_AuthorizedCaller_Gets503...`) |
| Lock always granted | 2 (incl. `ConcurrentSameDecisionId_IsSerialisedByTheInvoiceLock`) |
| Totals recalculation skipped | 9 |
| Reason checks removed | 4 |
| Foreign-line check removed | 1 |
| Empty decision id accepted | 1 |
| Accept-clean takes any status | 2 |
| Bulk correction rows `sprk_islabel` true | 1 |
| In Review check removed | 4 |
| Read service ignores `suggestedAdjustment` | 1 |
| Persister does not write `suggestedAdjustment` | 1 |

A first mutation run hung because the concurrency test blocked every caller inside the critical section; the test now blocks only the first caller and uses bounded waits, so a broken lock fails instead of hanging.

## 7. Known limits and open questions

1. ~~**Signals are not closed.**~~ Superseded: each line decision now closes its line's open Signals (section 9). Decision Records are still written without `PolicyVersionId`.
2. **Retry-revert.** A repeat of a decision id after another reviewer re-decided the same line could re-apply the first decision (no cheap Decision Record lookup before the write). Narrow window; the lock and the row version cover concurrent use.
3. **Concurrent approve (103) racing a line decision** is not atomic; 016's recalculation corrects the totals.
4. **No source version on correction rows**: the analyzer/source version is not persisted anywhere, so `sprk_sourceversion` is empty.
5. **Matter rollup** is refreshed by `FinanceRollupService`, not here.
6. Reason on an Accept is ignored (lenient).
7. **Ledger count.** `RouteAuthorizationGuardTests.Ledger.cs` `ExpectedEndpointFileCount` is 139 -> 140 for this file. Tasks 050 and 105 each add or remove endpoint files too; the sum must be reconciled when the three are integrated.

## 8. Publish size, packages and gates

- Publish size (`.claude/rules/bff-hygiene.md` item 4): baseline = `9b272493b` (the branch base, so only this change is measured; the eBilling branch carries other work not on master), after = the task tip. Both are `git archive` exports at `C:\wt100m` / `C:\wt100e`, built `dotnet publish -c Release -r linux-x64 --self-contained false -p:NuGetAudit=false`, 192 files each, zipped with `Compress-Archive -CompressionLevel Optimal`. Before 39,846,298 B (38.000 MB), after 39,909,216 B (38.060 MB), delta +62,918 B (+0.06 MB). No package added. A comparison against a fresh master build was not made.
- `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages. No csproj or `Directory.Packages.props` change.
- BFF build: 0 warnings, 0 errors.
- Unit run with the project filter plus `InvoiceLine` and `OperationAccessPolicyCompleteness`: 8,508 passed, 0 failed, 11 skipped (8,519 total). It includes the 38 decision, 15 read and 42 contract tests and the completeness test.
- `tests/Spaarke.ArchTests/`: 1,214 passed, 0 failed. ADR-010 class: 5 of 5 pass; the change adds two concrete services and no interface, so the 1:1 interface count is unchanged.
- The completeness scanner (`OperationAccessPolicyCompletenessTests`) only discovers a string literal inside `AddFinanceAuthorizationFilter("...")`; the first draft passed the operation through a const and the scan test `SourceScan_DiscoversKnownCallSiteOperation` caught it. The routes now use the literal.

## 9. Human-decision Signal close (D-122, agreed with ontology-r1)

Each line's decision (Accept, Reduce, Disallow; single, multi-line and Accept all Clean) resolves every Open or Acknowledged Signal, of
every policy, whose subject is THAT line. Another line's Signal is never touched. Each one is closed as **Acted**, never Dismissed.

- **No impersonation.** The app-identity `SignalWriter.CloseActedByDecisionAsync` closes, with `decidedByUserId` = the decider (the
  authenticated caller's `systemuserid`, never request input). The same conditional write sets `sprk_resolvedby`;
  `sprk_resolutionnotes` is never written. Automated callers pass null and write exactly what they wrote before.
- **Authorization before any write.** When the decision closes at least one Signal, the caller must hold AppendTo on the invoice's core
  record and Write on each decided line that has a Signal, asked as the caller (the route passes its bearer token) through the caller-rights
  probe: the commit route's own rule, `DecisionCallerRights`, moved verbatim out of `DecisionCommitService.CallerMayDecideAsync`. A missing
  right, a missing token or an unanswerable probe is 403 `ebilling.decision.not_authorized` and nothing is written (no line, correction,
  total, record or close). A decision that closes no Signal asks nothing beyond the route's filter (unchanged behaviour).
- **The set is read before the record and stored with it.** Per line: the Signal ids are the record's `ResolvedSignalIds` (covered by the
  hash); the read time and each policy code go in the facts (`resolvedSignalSet`, not hashed). The read is one query over the lines that
  have no record yet, with the clock read first.
- **A stored set wins.** For each line the service first asks `BillingDecisionRecorder.FindStoredResolvedSetAsync` (a read-only lookup in
  `DecisionRecordWriter`, `FindBillingDecisionResolvedSetAsync`, through the "already recorded" lookup's own query: created by the writer
  identity, billing codes only, at most 10 rows; more than one match fails closed). When the line's decision is already recorded under the
  decision id, its stored set is used, never re-read or widened: a resend answers AlreadyRecorded (not review_invalid), a Signal raised
  after the first read stays open, and re-closing by the same record is `AlreadyClosedByThisRecord` (nothing twice).
- **A failed close only warns.** One Warning per Signal; the decision and its response are unchanged. A Signal that no longer exists has
  nothing to close. A retry of the same decision id finishes the close over the stored set with the stored read time.

Tests: `InvoiceLineDecisionServiceTests` region "D-122 / FR-51" (each outcome; multi-line; rights asked as the caller; no Signal asks
nothing; missing AppendTo / Write / token is 403 with nothing written; a failed close warns; a deleted Signal; resend after success; retry
after a failed close; a Signal read fault) and `DecisionRecordWriterTests` `Billing_StoredSet_*` (no record is null through the lookup's
filter; round trip; empty set; two records fail closed; a person-created lookalike is never trusted; a set-less record listing Signals
fails closed; a lookup fault; bad input refused without a read).

### 9.1 ontology-r1 independent review, round 1 (2026-10-20)

- **Ruling, amending the binding shape: "ontology-r1 2026-10-20: line subject → Write on the parent invoice (D-90)".** Core User and Basic
  User hold only Read on `sprk_billingevent` (the application writes lines, D-90), so asking Write on a line refused every ordinary
  reviewer with an open line Signal (F1-2; the test doubles granted Write everywhere, which hid it). `HumanDecisionSignalClose.AuthorizeAsync`
  now maps every `sprk_billingevent` subject to (`sprk_invoice`, the decided lines' invoice) before the rights check; AppendTo on the core
  and Write on any other subject are unchanged. The decided lines are read by the invoice, so the parent is the command's invoice. The
  commit route's `DecisionCallerRights` is not changed (its tests pass unedited). The test doubles now model the real roles: Read only on
  `sprk_billingevent`, Write on `sprk_invoice`, AppendTo on the core.
- **"Verify with a real Core User on dev before 100/105 ship."** The unit tests model the roles; only a live Core User proves them.
- **K2:** the stored sets of all decided lines are found in ONE query (`DecisionRecordWriter.FindBillingDecisionResolvedSetsAsync`: subject
  `In` the line ids, the single lookup's writer filter unchanged, keys matched in memory, paged; the writer's IN lists are not chunked, so
  neither is this one). More than one record under a line's key still fails closed (`decision_invalid`, nothing written).
- **K3:** an open Signal whose dedupe-key subject differs from its regarding row is skipped with one Warning (shared
  `StoredSignalSetCheck.RowSubjectOf`), so no row is closed whose rights were not asked.

Tests: `InvoiceLineDecisionServiceTests` `ARealReviewer_DecidesALine_WithAnOpenLineSignal`, `MissingWriteOnASubject_Is403_AndNothingIsWritten`
(Write denied on the invoice), `TheRights_AreAskedAsTheCaller_OnTheCoreAndEachLineWithASignal`, `StoredSets_OfEveryLine_AreOneQuery`,
`StoredSets_TwoRecordsUnderOneKey_FailClosed`, `ASignalWhoseKeyAndRowDisagree_IsSkipped_AndWarned`; `DecisionRecordWriterTests`
`Billing_StoredSets_*`. Seeded mutations: 105 notes section 9.1.
