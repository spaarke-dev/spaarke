# ISS-034 (#2888), task iss034a: an operator route that finishes a stranded blocked Override or Return as recorded

> **Task:** `tasks/iss034a-operator-finish-stranded-decision.poml` (FULL) · **Decision:** EB-D-244 (owner, option 1) · **Base:** `task/ebilling-iss034a` from 3ff10e65a4 (`origin/work/ebilling-r1`) · **Status:** built and tested. Code-review, adr-check and the adversarial pass are for the coordinator.

## The defect

A blocked submission's Override (or blocked Return) can write its Decision Record and then fail before it finishes (release, closes, transition or submission write). Since 095f-2c only the stored decider can finish it by repeating it, and since ISS-031 the other verb is refused `other_decision_pending`. When the decider cannot return, the invoice stays held for good.

## What changed

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Finance/SubmissionOverrideService.cs` | New public `FinishStrandedAsync(submissionId, reason, note, token, ct)`. It calls the existing `DecideAsync` with `operatorFinish: true` (the verb parameter is now `Verb? requestedVerb`; null only for the operator finish), which passes the flag to `DecideLockedAsync`. New helpers: `StrandedVerbAsync` (derives the verb), `RecordOperatorFinishAsync` (the operator's record), `BuildOperatorFinishDecision`, `HoldsAsync` (a caller privilege, fail closed), `StrandedHintAsync` (GET hints). New constants: `FinishActionCode`, `FinishedDecisionValue` / `FinishedRecordValue`, `AdminMarkerPrivilege` (read from `BillingAdminAuthorization.Admin()`'s one check), `OperatorFinishDecisionIdFor(submissionId, verb)` = `InvoiceSubmissionPersister.IdOf("ebilling-operator-finish-{submissionId:D}-{override\|return}")`. New error codes and sentences in `BlockedSubmissionErrors`. `BlockedSubmissionView` gains `StrandedDecision` (new record `BlockedStrandedDecision(Verb, RecordId, CallerIsDecider)`) and `CanFinishAsOperator`. Class remarks gain an "Operator finish" paragraph. |
| `src/server/api/Sprk.Bff.Api/Api/Finance/SubmissionOverrideEndpoints.cs` | `POST /api/finance/submissions/{submissionId:guid}/finish-stranded` (`FinishStrandedBlockedDecision`), body `BlockedDecisionRequest` (`reason` required, `note` optional), the same `AddFinanceAuthorizationFilter("ebilling.submission.decide", ...)` and `RequireEbillingEnabled()` as the decide routes, the same `ToResult`. Remarks describe the route. |
| `src/server/api/Sprk.Bff.Api/Services/Signals/Actions/DecisionActionCatalog.cs` | New billing entry `finish-stranded-decision` (see "Catalog amendment"). |
| Tests | See "Tests". |

No DI change (the service is already registered), no package change, no project file change.

### The route's order of checks

1. Route filter, as the caller: Read and Write on the submission (`ebilling.submission.decide`); then the eBilling kill switch.
2. The caller is resolved (`systemuserid`) and the **billing-admin marker** is asked as the caller (`CallerHoldsPrivilegeAsync` with `AdminMarkerPrivilege`). No caller, a "no", or any fault: **403 `ebilling.blocked.operator_not_permitted`**. Nothing about the submission has been read.
3. The operator's reason (400 `ebilling.reason_required` / `ebilling.reason_too_long`) and note (400 `ebilling.note_too_long` / `ebilling.note_invalid_characters`), by the decisions' own code.
4. The submission is read (404 `not_found`, 503 `unavailable`).
5. The stranded verb, derived server-side (`StrandedVerbAsync`):
   - submission Overridden: Override; Returned: Return (a finished decision; step 7 answers it as a repeat);
   - Blocked or Procedure-held: the invoice must be the one the submission created (409 `invoice_not_held`), then both writer-created records are looked up through `_findStored` (`OverrideDecisionIdFor` / `ReturnDecisionIdFor`): neither, **409 `ebilling.blocked.nothing_stranded`**; both, **409 `other_decision_pending`** (detail `BothDecisionsRecordedDetail`); a writer refusal maps as `RecordRefusal`, any other fault **503 `unavailable`**; exactly one, that verb;
   - any other state: the refusal a decision gives (`not_internal`, `submission_failed`, `already_accepted`, `still_processing`, `not_blocked`).
6. For an Override, the **eBilling Override privilege** (`EbillingOverridePrivilege.HoldsAsync`, fail closed): **403 `operator_not_permitted`**.
7. From here it is the decision's own code path: `Refusal` (a finished decision answers **200 `alreadyDecided`** with the existing column repair and Signal re-close, and no operator record), the invoice link checks, the invoice lock (`LockKey`, 409 `in_progress`, 503).
8. Under the lock (`DecideLockedAsync`), unchanged: the re-read, `Refusal`, the invoice state, the stored findings, the same verb's lookup, the Signal set, **the FR-51 close rights asked as the operator (403 `not_authorized`)**, the ISS-031 other-verb guard (409 `other_decision_pending`).
9. New, operator only: the stranded record must still exist (409 `nothing_stranded`); the caller must not be its decider (**409 `decision_conflict`**, detail `OwnDecisionDetail`: repeat your own decision; no operator record); the record must name a decider (503). From here `decider` is the **stored decider**.
10. Unchanged: the record's readability (503), the stored reason and note replace the request's, the firm gate (409 `ebilling.override.firm_missing`, 503), the invoice's row version (503), the holds derivation (503).
11. New, operator only: the submission's row version is checked (503), then **the operator record** (`RecordOperatorFinishAsync`): preflight (400 `decision_invalid`), then a lookup of the operator decision id: found and confirmed by this operator, reused (no write); found and confirmed by another operator, **409 `decision_conflict`** (`AnotherOperatorDetail`); not found, written through `BillingDecisionRecorder.RecordAsync` → `DecisionRecordWriter.WriteBillingDecisionAsync`. A writer refusal maps as `RecordRefusal`; a fault is 503 `record_unavailable`.
12. Unchanged: the Signal closes (the first write of the finish; under the stored record and the stored decider), the Return transition, the stamp (stored reason, stored decider, stored record), the release transaction (stored note or reason in `sprk_decisionnote`), the re-index, the notification.

The response is the decision's: `Outcome` and `RecordId` are the finished decision's (A's record), as the decider's own repeat answers.

### What the finish keeps from the original decision

The stored reason, note, Signal set and decider (`recordedSet.ConfirmedByUserId`): the stamp's `bySystemUserId`, the holds derivation's provisional stamp and every Signal close credit the stored decider, and the column, stamp and transition hold the stored text. The operator's reason and note go only on the operator record.

### The operator record

`finish-stranded-decision`, subject the invoice (`BillingSubjectEntityOf` maps every non-line code to the invoice), `ConfirmedBy` the operator (role "billing admin finishing another person's recorded decision"; the contact is looked up by the recorder, K3), reason the operator's, outcome `Finished`, values `finishedDecision` (`override-submission` / `return-to-firm`), `finishedRecord` (the finished record id), `submissionId`, and `note` only when given (no null key). It resolves no Signal (the finished decision's record does). Facts: the decision, the submission, the finished decision, record and decider. Decision id `IdOf("ebilling-operator-finish-{submissionId:D}-{override|return}")`, so one per submission and verb.

## Catalog amendment (for ontology-r1 review)

`DecisionActionCatalog.DeclaredBilling` gains `finish-stranded-decision` ("Finish stranded decision"), modelled on 095f-2b's `add-adjustment` / `remove-adjustment`: Decide lane, `IsNextStep: false`, `WorkType: null` (BillingActions only, never the Signals commit route), Judgement class, `Excludes: []`, parameters `finishedDecision` (Choice, required: `override-submission`, `return-to-firm`), `finishedRecord` (Lookup `sprk_decisionrecord`, required), `reason` (TextArea, required) and the optional `note`. Gate `DecisionGateFactors(Tier4, Reversible: false, RecordOfTruthImpact: true)`, which resolves to Tier4: the finish releases or returns the invoice and cannot be undone, so it gates as the most serious decision it can finish. ontology-r1 may prefer Tier3 (the Override's own tier); this is a review point, not a settled ruling.

The exact-set tests were amended the way 095f-2b amended them (an ontology-r1 reviewed amendment of D-100's "additive only"):

- `tests/unit/domain/Signals/DecisionActionCatalogTests.cs`: `BillingCodes` (the exact set), `EachBillingAction_HasItsRecordClassAndGateTier` (Judgement, Tier4), `BillingActions_RequireExactlyTheStatedParameters...` (`finishedDecision`, `finishedRecord`, `reason`), `InvoiceDecisions_DeclareNoteAsAnOptionalTextAreaParameter`.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.Billing.cs`: `Billing_C2_...` (`finish-stranded-decision` files under `sprk_invoice`; the writer literal is unchanged).
- `tests/integration/data-mutation/Signals/DecisionCommitServiceTests.cs`: `ABillingCode_IsRefusedByTheCommitRoute_NothingWritten_Ebilling074` gains the code.

## Tests

| Acceptance criterion | Test | Fail-before |
|---|---|---|
| AC1: a stranded Override (release failed; closes and release failed; invoice bound exhausted) and a stranded Return (transition failed; closes and transition failed; submission write failed) by decider A are finished by operator B: released or returned, A's record unchanged, Signals closed crediting A under A's record, stamp and column hold A's text, one `finish-stranded-decision` record by B naming A's record, B's reason and note | `SubmissionOverrideOperatorFinishTests.StrandedDecision_IsFinishedByAnOperator_AsRecorded` (6) | 6 of 6 fail |
| The operator record comes after the lock and before the first finish write (the close) | `TheOperatorRecord_IsWrittenBeforeTheFirstFinishWrite` | fails |
| AC2: refusals write nothing: no admin marker (Override and Return strands), no Override privilege, a privilege-probe fault (both), an unidentified caller (403 `operator_not_permitted`; for the marker refusals, no entity read and no record lookup happened); no FR-51 rights (403 `not_authorized`, both verbs); caller is the decider (409 `decision_conflict`, both verbs); a lookup fault (503 `unavailable`, both verbs); the operator record's lookup faults (503 `record_unavailable`); firm gate (409 `firm_missing`) | `OperatorFinishRefusal_WritesNothing` (14) | 14 of 14 fail |
| AC2: nothing stranded, 409 `nothing_stranded`, refused before the lock | `NothingStranded_Is409_AndWritesNothing` | fails |
| AC2: both records, 409 `other_decision_pending` | `BothRecordsStored_Is409_AndWritesNothing` | fails |
| The operator's reason is checked before any read | `AMissingOperatorReason_Is400_BeforeAnyRead` | fails |
| AC3: a repeat after a full finish is 200 `alreadyDecided`, no second operator record, nothing written (the re-close finds every Signal already closed) | `ARepeatAfterAFullFinish_IsAlreadyDecided_WithNoSecondOperatorRecord` (2) | 2 of 2 fail |
| AC3: a finish interrupted after the operator record (release, transition fault) completes on the next call, even with another text, with one operator record holding the first text | `AFinishInterruptedAfterTheOperatorRecord_CompletesOnTheNextCall_WithOneOperatorRecord` (2) | 2 of 2 fail |
| Known limit K1 below: a second operator is refused | `AnInterruptedFinish_IsRefusedToASecondOperator` | fails |
| AC4 must-not-fire: a billing admin who is not the decider still gets `decision_conflict` from the ordinary repeat and `other_decision_pending` from the other verb; the decider's own repeat finishes with no operator record | `TheExistingRoutes_AreUnchanged_ForAnAdmin` (2), plus the unchanged ISS-031 and 095f-2c suites and golden hashes (`174D4566...`, `779A8F87...`, `10BFFB69...`) | pass (controls) |
| AC5 GET: the operator sees `StrandedDecision` (Override, A's record, not decider) and `CanFinishAsOperator` true; a Return strand without the Override privilege is finishable | `Get_ShowsTheStrandedDecision_AndCanFinish_ForTheOperator`, `Get_AStrandedReturn_CanBeFinishedWithoutTheOverridePrivilege` | 2 of 2 fail |
| AC5 GET: false for the decider (`CallerIsDecider` true), a non-admin, no Override privilege, a probe fault | `Get_CannotFinish_ForTheDecider_ANonAdmin_OrAProbeFault` (4) | 4 of 4 fail |
| AC5 GET: no hint with nothing stranded, or a lookup fault (the view still loads) | `Get_NoHint_WhenNothingIsStranded_OrTheLookupFaults` | passes (control) |
| The admin marker is `Admin()`'s check; the wire code | `TheAdminMarker_IsTheBillingAdminCheck` | passes (control) |
| The route through the real map and filter: 401; no rights or Read only, 403 before any privilege question or read; rights without the marker, 403 `operator_not_permitted` (errorCode and reasonCode) before the submission is read; nothing stranded, 409 | `SubmissionOverrideEndpointsAuthorizationContractTests.FinishStranded_*` (5) | 5 of 5 fail (route absent) |
| AC6: RouteAuthorizationGuard covers the route | `RouteAuthorizationGuardTests.FinishStranded_RuleA_FiresWithoutTheFinanceFilter` (negative control), `FinishStranded_RuleA_PassesAsShipped`; the `SubmissionOverrideEndpoints.cs` ledger entry names the route | the negative control needs the route (fails before) |
| AC6: catalog exact sets | the amended tests above | 6 fail before |

**Fail-before runs.** (1) The service seeded to its pre-change behaviour (`FinishStrandedAsync` answering a 404 as if no route existed, the GET hints removed) and the catalog file restored from the base commit: the new and amended tests were run, **41 failed** (35 of the 39 new service tests plus the 6 catalog tests); the 4 that passed are the controls named above. (2) The endpoints file restored from the base commit: the 5 new contract tests **failed 5 of 5**. Each file was then restored from a saved copy and the suites re-run green.

**Test harness note.** The test World's identity double now also resolves `World.Operator` to a linked contact (the recorder reads the operator's contact before writing the operator record). The Decision Record writer harness answers every create with the same id (`NewId`), so tests tell the operator record apart by its action code and key, not by its id.

## Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | succeeded, 0 warnings |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests --warnaserror`, `dotnet build tests/Spaarke.ArchTests --warnaserror` | succeeded, 0 warnings |
| BFF tests, filter `SubmissionOverride\|DecisionNote\|DecisionRecord\|DecisionActionCatalog\|BlockedSubmission\|InvoiceRejectReturn` | 1667 passed, 1 skipped (a live test), 0 failed |
| BFF tests, filter `DecisionCommitService\|DecisionActionExecutors\|InvoiceAdjustment` (catalog consumers) | 680 passed, 0 failed |
| ArchTests, filter `Invoice\|Decision\|RouteAuthorization` | 240 passed, 0 failed |

No package, project file or DI registration changed; the publish-size delta is a few kilobytes of IL (not measured against a fresh master publish).

## Component justification (CLAUDE.md section 11)

- **New route** (`POST .../finish-stranded`). Existing: the override and return routes. Extension: they cannot serve it, because they answer for the caller's own decision (`decision_conflict` for another person's, by the 095f-2c rule the owner kept) and take the verb from the path, while this one derives the verb and needs its own authority and its own record. Cost of doing nothing: a stranded decision whose decider is gone holds its invoice for good (ISS-034).
- **New service method** `FinishStrandedAsync`. It is a flag on the existing `DecideAsync` / `DecideLockedAsync`, not a copy: the same lock, re-read, refusals, firm gate, holds derivation, closes, release, transition and notification.
- **New catalog code** `finish-stranded-decision`. Existing: `override-submission` and `return-to-firm`. Extension: they are the decider's records and must stay the decider's (append-only, and the trail must show who decided and who finished, EB-D-244). Cost of doing nothing: the finish would leave no record of the operator.
- **New view fields** `StrandedDecision`, `CanFinishAsOperator`. Existing: `CanOverride`, `CanReturn`, the stamps. Extension: none tells the client a decision is stranded or who may finish it. Cost of doing nothing: iss034b cannot offer the action without guessing.

## Decisions made where the POML could not be followed literally

- **Two new codes only.** "The caller is the stored decider" and "another operator's finish has not completed" reuse `ebilling.blocked.decision_conflict` with their own detail sentences (`OwnDecisionDetail`, `AnotherOperatorDetail`); "both records" reuses `other_decision_pending` with `BothDecisionsRecordedDetail`. The POML named only `operator_not_permitted` and `nothing_stranded` as new.
- **A finished decision answers 200 `alreadyDecided`.** The POML says "as today's repeat (already_overridden / already_returned, ...)". Today's repeat of the SAME verb answers 200 `alreadyDecided` (the codes named are the OTHER verb's answers), and only that answer runs the column repair and re-close the POML asks for, so the operator route takes the finished verb and answers as its repeat. For a finished Override the operator must still hold the Override privilege (fail closed: 403 otherwise).
- **"The submission's row version" before the operator record.** The decision path checks the submission's row version only after the Signal closes. For the operator finish it is also checked before the operator record (an extra check, no extra read), so that refusal cannot follow the record.
- **A repeat of an interrupted finish reuses the operator record only for the same operator.** The record is looked up first, so the same operator's repeat completes with any text (as a decider's repeat does). Another operator is refused (fail closed); see K1.
- **The operator record's lookup and write share one fault answer**: 503 `record_unavailable` ("Nothing was changed. Try again."), not `unavailable`.

## Known limits

- **K1 (accepted, fail closed): an interrupted operator finish belongs to its operator.** If operator B's finish writes B's record and then fails, and B then cannot return, operator C is refused 409 `decision_conflict` (tested). This mirrors the decider rule one level up. Reaching it needs a second failure after the operator record, by an operator who then also leaves; the finish's own writes are retried by B's repeat. If the owner wants C to complete it, the change is to reuse B's record for any operator (the trail would then not name C).
- **K2: refusals that can still follow the operator record.** After the operator record, the finish can still stop at the same points the decider's own repeat can: a Signal close that fails (503; the repeat re-closes), the release's version conflict after the bound (409 `concurrent_change`), the lifecycle refusing the transition, or the stamp not fitting the findings column (409 `findings_too_large`). In each case the operator record stays and a repeat of the operator route completes with no second record (tested for the release and the transition).
- **K3: the GET hints cost two extra Decision Record lookups** on every read of a held submission (and the privilege probes when exactly one record exists). A lookup fault drops the hints and the view still loads.
- **K4: a stranded decision whose record names no decider** cannot be finished (503): a human record always names one (K3 of 074), so this is a corrupted record, not a reachable state.
- **K2-class, unchanged by this task:** a Return caller with no Signals to close is not asked FR-51 rights (095f-2c, ISS-031 notes); the operator finish inherits it. The route's own filter still requires Read and Write on the submission.

## Client fields for iss034b

`GET .../blocked` now returns `strandedDecision` (`{ verb: "Override" | "Return", recordId, callerIsDecider }` or null) and `canFinishAsOperator` (boolean); the action posts `{ reason, note? }` to `POST .../finish-stranded` and reads the same `BlockedDecisionResponse` (`recordId` is the finished decision's record) or a ProblemDetails with `errorCode` (`ebilling.blocked.operator_not_permitted`, `ebilling.blocked.nothing_stranded`, `decision_conflict`, `other_decision_pending`, and the decisions' existing codes).
