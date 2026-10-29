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

1. **Signals on approve.** Approve does not close invoice-level Signals (the line accept closes line Signals through the line service). Owner: should approve resolve open invoice-level Signals, or leave them to 104 or a later task?
2. **Nothing in code moves a line from Pending to Clean.** The only writers of `sprk_linestatus` are the persister (`InvoiceSubmissionPersister.cs:812`, which writes Pending or Flagged) and the line decisions (`InvoiceLineDecisionService.cs:766`). No code writes Clean. Until the extraction/scoring path sets it, a freshly extracted invoice shows Pending lines and approve is refused (I-2). Reported for the owner of 049/071.
3. **The live view evidence (step 5) was not run.** The brief says nothing live. The 029 view reads the N:N that this task writes. Evidence still needed in dev: an invoice for each eligible approver, and not for another user.
4. An eligible approver also needs Write on the invoice, because the route checks `ebilling.invoice.decide` first. Approvers normally hold it through the core record's access. Flagged for the operator in case an approver role does not.
5. Unreviewed and Accepted-in-bulk rows written before a later refusal (totals, claim conflict, transition refused) remain. They are true statements about those lines, and a later approve reuses the same ids.
