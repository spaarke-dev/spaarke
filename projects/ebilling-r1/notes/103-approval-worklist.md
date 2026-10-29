# Task 103: Approval (eligible approvers, worklist, approve)

Branch `task/ebill-103-approval`, from `work/ebilling-r1` at `e2ed5e55c1`. Rigor FULL (bff-api, authorization). Model: Opus @ high.

## What was built

| Component | File | What it does |
|---|---|---|
| `EligibleApproverResolver` | `Services/Finance/EligibleApproverResolver.cs` | Core record (type, id) → `sprk_billingapproverrole` values, or the 025 settings default for that core type when empty → the contact in each named assignment field → active contacts with a `sprk_systemuser`. Writes only the diff to the N:N `sprk_invoiceapproval_eligibleapprover_contact` and stamps `sprk_eligibleresolvedon`, in one transaction. `RefreshInvoiceAsync` is the 049 append point. |
| `InvoiceApprovalService` | `Services/Finance/InvoiceApprovalService.cs` | GET state (with the D-78 recheck) and approve (eligibility, I-2, D-72, 016 totals, claim, 014 transition, portion, 074 record, link). |
| `EligibleApproverRefreshJob` | `Services/Finance/EligibleApproverRefreshJob.cs` | `IScheduledJob` (ADR-036), cron `7,22,37,52 * * * *`. Re-resolves every Pending portion on an invoice In Review, caching per core record. Skipped when eBilling is off. A query fault throws (the host retries); a unit fault is counted and skipped. |
| Endpoints | `Api/Finance/InvoiceApprovalEndpoints.cs` | `GET /api/finance/invoices/{id}/approval` (`finance.read`), `POST /api/finance/invoices/{id}/approve` (`ebilling.invoice.decide`). Both: step 0, the route filter, then `RequireEbillingEnabled`. |
| Line-decision additions | `Services/Finance/InvoiceLineDecisionService.cs` | `LockKeyFor`, `AcceptCleanHeldAsync` (accept-Clean under a lock the caller already holds), `WriteUnreviewedAsync` (D-72 Unreviewed rows). The correction-row builder now takes a `CorrectionKind`; the bulk ids are unchanged. |
| 049 hook | `Services/Finance/Submission/InvoiceSubmissionAfterSteps.cs` | After acceptance (not for AutoRejected), resolves the eligible approvers. Best effort: a failure only warns, and the job or the next GET retries. |
| DI and routes | `FinanceModule.cs` (3 lines), `EndpointMappingExtensions.cs` (1 line) | Minimal, because 101 and 102 run in parallel. |

## Decisions applied

- **D-72 (F-2).** Approve is allowed with undecided Clean lines. Approve accepts them through the line service's bulk accept, under decision id `IdOf("ebilling-approve-accept-clean-{approvalId}")`, which writes "Accepted in bulk" rows (outcome 100000002). It then writes "Unreviewed" rows (outcome 100000003) for each AI-sourced field of an individually decided line (Accepted, Reduced or Disallowed) that has no correction row. Neither kind of row is a label (`islabel` false). The Unreviewed ids are `IdOf("ebilling-correction-unreviewed-{approvalId}-{line}-{field}")`, so a retry upserts the same rows. This is the "D-64 finalisation rule": at approval, every AI-sourced field of a decided line has a correction row.
- **D-78 (refresh).** There are three layers:
  - Approve checks eligibility live from the core record, and the stored N:N never grants.
  - GET rechecks and mirrors the set onto the N:N while the portion is Pending and the invoice In Review.
  - The job refreshes every 15 minutes.

  So a reassignment shows in the view within 15 minutes, or at once for whoever opens the invoice. The approve check always uses the live value.
- **D-40.** Any one eligible approver approves.
- **D-54 / FR-14.** An empty role field uses the per-type settings default. A role whose field is empty, or whose contact has no user or is inactive, contributes nobody. When nobody is left, nobody may approve: `noEligibleApprover` is true and approve is 403. It never falls back to "anyone".

## Exactly-once (FR-35, FR-30; 074 K-1, K-3, K-4)

Approve takes no body. The decider is the caller's `systemuserid`, taken from the caller's token. The decision id is derived from the portion: `IdOf("ebilling-approve-{approvalId}")`. This is a deviation from "POST .../approve (decision id)", following 074's note ("103: from the approval portion"). It makes every attempt by every approver one decision, so a client cannot mint a second one.

Order:

1. Take two locks: the invoice-decision lock (`SubmissionOverrideService.LockKey`, which the override and 104 also take) and the line lock (`InvoiceLineDecisionService.LockKeyFor`). A held lock gives 409 busy. A lock fault gives 503, because the code fails closed.
2. Read the invoice and the one portion. If either is already Approved, go to the completion/no-op path (below).
3. Run the checks in this order, with nothing written before any of them:
   - In Review;
   - one core record (074 K-3, the writer's filing check);
   - live eligibility (403 `ebilling.approver.not_eligible`);
   - not held (FR-20);
   - no Pending or Flagged line (409 with counts). A null or unknown status counts as Pending.
4. Run the D-72 writes: the bulk accept, then the Unreviewed rows. Both are idempotent under derived ids.
5. Run 016's recalculation to get the approved total, then `Preflight` the record.
6. **Claim.** Write {decidedby, decidedon, approvedamount} with `UpdateIfRowVersionMatchesAsync` on the portion's version as read. A concurrent approve loses here (409 `ebilling.approval.changed`). This is the serialisation mechanism the POML asks for. It holds even when the lock is defeated (an expired lease); the test `ConcurrentApproves_LockDefeated_ExactlyOneRecord` covers that case.
7. Run the 014 transition to Approved:
   - Only `Transitioned` continues.
   - `AlreadyInTarget` gives `alreadyApproved` with no record.
   - `Refused` gives 409.
   - An exception gives 503 incomplete, because the outcome is unknown.
8. Finish: the portion becomes Approved (re-asserting the claim fields), then the record is written through `BillingDecisionRecorder`, then `sprk_decisionrecord` is linked. Each step is idempotent, and any fault gives 503 `ebilling.approval.incomplete` with `failedStep`.

**Completion/no-op path.** The claimant is the caller whose user is linked to the portion's `sprk_decidedby` contact. When the claimant approves again, the decision is rebuilt from the frozen decidedon and amount, and whatever is missing (status, record, link) is completed. The writer's hash ignores facts and time, and the id is the same, so the record is written once. Anyone else gets `alreadyApproved` and no record. If the portion is still Pending on an Approved invoice, its status alone is aligned. Tests cover a fault at the portion, identity, link and transition-after-commit steps, each followed by a retry from the same approver, and each yields exactly one record. Another approver in between records nothing.

## Seeding proofs (all run, all restored; script in the session scratchpad)

| Seed | Change | Tests that failed |
|---|---|---|
| Eligibility guard | `set.For(caller) is not { } approver` replaced by "anyone is an approver" | 4: `NotEligible_Is403_AndWritesNothing`, `StoredSetNeverGrants_TheLiveValueDecides`, `NoEligibleApprover_NobodyMayApprove`, contract `Approve_WriteButNotEligible_Is403_AndWritesNothing` |
| Held (FR-20) | held check disabled | 1: `Held_Is409` |
| I-2 lines open | open-line refusal disabled | 4: `OpenLines_Are409WithCounts` (Flagged, Pending), `UnknownLineStatus_CountsAsPending`, contract `Approve_OpenLines_Is409_WithCounts` |
| Route filter | `AddFinanceAuthorizationFilter("ebilling.invoice.decide", ...)` removed from approve | 11 contract tests (Denied, right on another invoice, step-0 must-fire ×4 and must-not-fire ×2, flag-off unauthorized, failed rights check, needs Write) |

## Gate

| Check | Result |
|---|---|
| BFF build | 0 errors, 0 warnings |
| Builds of `Sprk.Bff.Api.Tests` and `Spaarke.ArchTests` | succeeded |
| Touched suites (`InvoiceApprovalServiceTests`, `EligibleApproverResolverTests`, `InvoiceSubmissionAfterStepsTests`, `InvoiceApprovalEndpointsAuthorizationContractTests`) | 208 / 0 |
| Broad filter (`Finance\|Invoice\|Approv\|Billing\|Decision`) | 7,019 passed, 0 failed, 5 skipped (pre-existing skips) |
| Full ArchTests | 1,576 / 0 |

The ArchTests needed one ratchet entry: `ExternalDocumentNarrowingGuardTests.GenericWritersThatUpdateNoDocument` gains `InvoiceApprovalService::ApproveHeldAsync`, since the claim writes only three literal portion columns. The route ledger gained `InvoiceApprovalEndpoints.cs` (`RouteLevelGate`), and `ExpectedEndpointFileCount` went from 143 to 144. **101 and 102 also add endpoint files, so reconcile the count at integration** (the sum of the three additions).

## BFF hygiene (§10) and placement

- **Placement.** The code is in the BFF, under `Services/Finance` and `Api/Finance`. It is CRUD plus authorization over Dataverse, next to 100 and 105, which it reuses (line service, totals, lifecycle, recorder, locks). It has no AI dependency (ADR-013).
- **Background work (ADR-052).** The job is in the BFF as an `IScheduledJob` (ADR-036): a short Dataverse-only sweep that follows `SecureChildReconciliationJob`.
- **Publish size.** Both builds use `dotnet publish -c Release -r linux-x64 --no-self-contained` from `git archive` trees in the scratchpad.

  | Build | Unzipped | Zipped |
  |---|---|---|
  | Fresh master `2cd0f62e2c` | 132,655,296 B | 37.94 MB |
  | Branch base `e2ed5e55c1` | 135,100,616 B | — |
  | This branch | 135,201,652 B | 38.75 MB |

  - **Delta of this task:** +101,036 B (+0.10 MB) against the base.
  - **Against master:** +2.43 MB unzipped (+0.81 MB zipped). That is the whole work branch, not this task.
- **CVE.** `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages. No package or csproj changed.

## §11 component justification

| Component | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `EligibleApproverResolver` | Nothing resolves approver roles to users (grep `sprk_billingapproverrole` finds only 024/025 settings and constants). | No. `IEbillingSettingsReader` only reads settings; the resolver composes it with the core-record and contact reads and the N:N write. | FR-11/FR-14 fail, nobody can be shown as an approver, and the view is empty. |
| `InvoiceApprovalService` | `InvoiceLineDecisionService` decides lines; `SubmissionOverrideService` decides submissions. | No. Approve is an invoice-level decision with its own record, portion and claim, and it calls the line service for D-72 instead of copying it. | Nobody can approve an invoice (FR-35, success criterion 4). |
| `EligibleApproverRefreshJob` | `SecureChildReconciliationJob` is the pattern; no job refreshes approvers. | No, it is a different table and rule. | Without an assignment-change event, the stored set (and so 029's view) goes stale until someone opens the invoice (FR-11). |
| Two routes | None for approval. | They live in their own endpoints file, because 101 and 102 edit the review endpoints in parallel. | No API to approve or show approval state. |
| `AcceptCleanHeldAsync`, `WriteUnreviewedAsync`, `LockKeyFor` | They extend the existing line service. | — | D-72 cannot be applied under the approve's lock without them. |

## Code review and ADR check (Step 9.5)

Code review was a self-review at standard depth; the operator's adversarial review follows.

| Finding | Class | Disposition |
|---|---|---|
| A `Refused` transition leaves the claim fields (decidedby, decidedon, amount) on a Pending portion. The GET shows them only when the status is not Pending. The next approve re-claims on the new version. Only this service moves an invoice to Approved, but if someone set the invoice Approved by hand in Dataverse, the stale claimant could "complete" a record. | K2 | Known limit. Documented, not fixed (clearing the claim races the same way). |
| A non-claimant on an Approved invoice with a Pending portion aligns the portion status. That write is not gated on eligibility. It writes only the true status, and the route still requires Write on the invoice. | K3 | Known limit. |
| The claimant's completion runs before the eligibility check, so a claimant who was un-assigned after a partial failure still completes their own decision. The claim proves eligibility at decision time. | K3 | Intended. |
| A GET or refresh-job sync that stamps the portion between an approve's read and its claim bumps the portion version, so that approve gets 409 `changed`. A retry succeeds. | K4 | Known limit (rare; stamps happen only on change or a null stamp). |
| The job caps at 20 pages × 500 portions, ordered by id. Beyond 10,000 Pending portions the tail waits, and it can wait indefinitely if the head never shrinks. GET still rechecks. | K4 | Known limit. `truncated` is in the heartbeat. |
| When every unit faults but the query succeeds, the job reports success with a message instead of throwing. | K4 | Known limit (the message and `unitFaults` show it). |
| `InvoiceApprovalService.cs` is 815 lines | — | Cohesive: one decision (approve) plus its read model. |
| `InvoiceLineDecisionService.cs` grows to 1,155 lines | — | Cohesive: the additions are line-decision writes. |

**ADR check.** Compliant with:
- ADR-001 (Minimal API);
- ADR-002 (the approve invariant has one BFF owner, fails closed, no plugin);
- ADR-004/036/052 (`IScheduledJob`);
- ADR-008 (endpoint filters, step 0);
- ADR-009 (no cross-request cache; a per-run dictionary only);
- ADR-010 (concretes registered, no new interface);
- ADR-013 (no AI);
- ADR-015 (records and logs carry ids and amounts only);
- ADR-018 (kill switch after authorization);
- ADR-019 (stable codes);
- ADR-028 (caller token only).

No violations or warnings.

## Known limits and open questions

1. **Signals on approve.** Resolved by EB-D-172 (fix round 1, below).
2. **Nothing in code moves a line from Pending to Clean.** (Fixed by task 101 in 049's persister; tests still seed Clean lines.) The only writers of `sprk_linestatus` are the persister (`InvoiceSubmissionPersister.cs:812`, which writes Pending or Flagged) and the line decisions (`InvoiceLineDecisionService.cs:766`). No code writes Clean. Until the extraction/scoring path sets it, a freshly extracted invoice shows Pending lines and approve is refused (I-2). Reported for the owner of 049/071.
3. **The live view evidence (step 5) was not run.** The brief says nothing live. The 029 view reads the N:N that this task writes. Evidence still needed in dev: an invoice for each eligible approver, and not for another user.
4. An eligible approver also needs Write on the invoice, because the route checks `ebilling.invoice.decide` first. Approvers normally hold it through the core record's access. Flagged for the operator in case an approver role does not.
5. Unreviewed and Accepted-in-bulk rows written before a later refusal (totals, claim conflict, transition refused) remain. They are true statements about those lines, and a later approve reuses the same ids.

## Fix round 1 (coordinator, 2026-10-29: EB-D-171, EB-D-172, F2-F4, K1-K4, #1911 F2)

The K2 row in the code-review table above (claim left after a refused transition) is now fixed.

| Item | Change | Tests (`InvoiceApprovalServiceTests` unless named) |
|---|---|---|
| EB-D-171 | Eligibility matches the caller's OWN contact (`IIdentityNormalizationService.ResolveAsync(caller).ContactId`, the recorder's rule) against the eligible assignment contacts (`EligibleApproverSet.ForContact`). `contact.sprk_systemuser` never grants: not in approve, not in GET `callerEligible`, not in the claimant check on a repeat (which used to read `sprk_systemuser`). No contact gives 403 `ebilling.approver.not_eligible`. | `ContactSpoof_SystemUserLinkNeverGrants` (SEEDED), `OwnContact_Grants`, `UnresolvableIdentity_FailsClosed`; resolver `RolesSet_ResolveTheUsersInTheSelectedFields` |
| EB-D-172 | Approve resolves the invoice's Open and Acknowledged Signals created at or before the decision time (`ReadOpenSignalsAsync(..., createdOnOrBefore)`). It asks the close rights as the caller before ANY write (403 `ebilling.decision.not_authorized`), stores the set with the record (`ResolvedSignalIds` plus the set fact), and closes after the record, before the link. A repeat uses the stored set. Before the record exists, a repeat re-derives the set under the frozen `sprk_decidedon`. | `Approve_ClosesTheInvoiceSignals`, `NoCloseRights_IsRefused_AndWritesNothing` (SEEDED), `NoSignals_NoRightsAsked`, `Replay_AfterTheRecord_UsesTheStoredSet`, `Replay_BeforeTheRecord_IsBoundedByTheFrozenTime` |
| #1911 F2 | The close's failed count is no longer dropped. Above 0, approve is Incomplete at step `signals` (503 `ebilling.approval.incomplete`), before the link, so the claimant's repeat runs the finish again: the record answers AlreadyRecorded and the remaining closes run. | `FailedClose_IsIncomplete_AndTheRepeatFinishesIt` (timeout and ChangedSinceRead; SEEDED), `CleanClose_IsApproved` |
| F2 | Before the bulk accept, approve finds Accepted Fee and Expense lines that have a reviewer and no line Decision Record (one query on `sprk_regardinginvoiceline` plus the line action codes). The caller's lines are completed under `AcceptCleanDecisionIdFor(approvalId)` by `InvoiceLineDecisionService.CompleteBulkAcceptHeldAsync`: same values, same "Accepted in bulk" row ids, only the missing records. Another person's lines give 409 `ebilling.approve.decision_incomplete` with the ids, and nothing is written. | `RetryAfterAFailedBulkAccept_CompletesIt` (faults in records, totals and corrections; SEEDED), `AnotherPersonsUnrecordedLine_Is409`, `RecordedLines_AreLeftAlone` |
| F3 | Approve, GET and `RefreshInvoiceAsync` take the core from exactly one of `InvoiceFamilyCoreRecordGuard.CoreRecordLookups` (`EligibleApproverResolver.SingleCoreOf`). Before any write, approve also runs the recorder's filing read (`CoreAncestorResolver.ResolveStampsAsync`) and requires its one stamp to be that core. | `TwoCoreLookups_AreRefused` (SEEDED), `FilingReadFault_IsUnavailable`; resolver `Job_TwoCoreLookupsOnTheInvoice_IsSkipped`, `RefreshInvoice_TwoCoreLookups_StoresNothing` |
| F4 | GET `canApprove` also requires the decide right: Write on the invoice, asked through `CallerReadCheck` with `ebilling.invoice.decide`, as the review read does. | `Get_CanApproveNeedsTheDecideRight`; contract `Get_CanApprove_NeedsWriteOnTheInvoice` |
| K1 | The claim is refused (409 `ebilling.approval.claimed`) when the portion names a decider other than the caller's contact. The shared per-invoice lock was already taken: `InvoiceLineDecisionService.LockKeyFor` is `LockKeyPrefix` plus the invoice id, alongside the 104/105 decision lock. | `ClaimedByAnother_Is409` (SEEDED) |
| K2 | A refused transition clears decidedby, decidedon and approvedamount. The clear is conditional on the version re-read, and only runs while the portion still holds this call's claim. | `TransitionRefused_RecordsNothing_AndClearsTheClaim` (SEEDED), `TransitionRefused_ClaimChangedSince_IsNotCleared` |
| K3 | The refresh job reads the core from the INVOICE (aliased link columns), not from the portion's copy. The approved amount is in the record's Values, so the hash covers it. | resolver `Job_ReadsTheCoreFromTheInvoice`; `ApprovedAmount_IsCoveredByTheHash` (SEEDED) |
| K4 | A missing or unknown portion status gives 409 `ebilling.approval.status_unknown` and is never read as Pending. GET shows `Unknown`. | `UnknownPortionStatus_IsRefused` (SEEDED) |

**Seeds (all run, all restored).** Each guard was disabled in `InvoiceApprovalService`, and its tests went red:

| Guard disabled | Tests red |
|---|---|
| Eligibility by the `sprk_systemuser` match | 3 (EB-D-171) |
| The close authorization | 1 |
| The F2 completion | 3 theory rows (plus the 409 case) |
| The single-core check and the filing read | 2 |
| Values set to null | 1 |
| The K4 check | 2 |
| The K1 check | 1 |
| The K2 check | 1 |
| Dropping the close count | 2 |

**Deviations, for the reviewer.**
- **EB-D-172 replay before the record.** The portion has no column to store the set before the record exists. So a repeat with no record re-derives the set, bounded by the frozen decision time. The set can shrink (a Signal resolved meanwhile) but never widen.
- **F2.** There is no durable store for the committed ids, so the outstanding lines are found from the data: Accepted, with a reviewer, and no line record. Limit: if the caller has an unrecorded individual Accept of their own (not the approve's bulk accept), it would be completed under the approve's bulk id.
- **EB-D-171 identity fault.** A caller with no contact gets 403, as ruled. A FAILED identity read (a fault, or `ContactUnreadable`) gets 503 `ebilling.approval.unavailable` instead of 403. Neither writes anything. A 403 during an outage would tell an eligible approver they are not eligible.
- **`ReadApproversAsync`** still lists only contacts that have a `sprk_systemuser`. This is the more restrictive set: it no longer grants, it only narrows who is listed.

**Known limits (new).**
- **Approved with no Decision Record if the claimant never retries.** After the transition, only the claimant's repeat writes the record, and nothing sweeps an Approved invoice whose portion has no record. The coordinator files the sweep.
- **K1: an abandoned claim blocks other approvers.** If the claimant's call fails after the claim, before or at the transition (outcome unknown), other approvers are blocked until the claimant repeats.
- **One rare close path cannot be finished by a repeat.** A portion that is Approved and linked counts as complete. If the record link existed before the portion was Approved and a close fails, approve returns Incomplete, but the next repeat is a no-op.
- **Found in passing, not changed.** `InvoiceLineDecisionService.CoreOf` picks the FIRST core lookup that is set. The line service's owner stamp should use the single-core rule too.
