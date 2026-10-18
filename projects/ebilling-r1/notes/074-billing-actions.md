# Task 074 - Billing actions in DecisionActionCatalog (FR-30)

> Rigor FULL. Base `0c1c058d1` (contains `02c260152` and `f292ad119`). Started 2026-10-17.
> **Status: implementation complete (2026-10-17).** Part one (catalog, commit-route separation) landed in `0c2f8590f`. Part two (the
> writer entry and `BillingDecisionRecorder`) followed owner decisions D-100 (section 6.5 path B, with ontology-r1's binding conditions)
> and D-101 (every read submission has its invoice). See section 8. **Merge gate:** ontology-r1 reviews the PR and diffs `WriteAsync`,
> the code allow-list and the existing tests mechanically against `62a1ec56b`.

## 1. Step 0

- `git merge-base --is-ancestor 02c260152 HEAD`: ok (after resetting the agent worktree to `work/ebilling-r1`, `0c1c058d1`).
- `git merge-base --is-ancestor f292ad119 HEAD`: ok.
- 026 has landed: `sprk_decisionrecord.sprk_regardinginvoiceline` exists, and `DecisionRecordWriter` files line and invoice subjects
  and refuses `SubjectUnfileable` unless `request.Core` is the subject's single derived core (026 fix-round-1 item 3a). So the
  third escalation trigger does not fire.

## 2. F-9 answer (D-75, owner, 2026-10-15)

- One Decision Record per **line decision**. Its subject is the line (`sprk_billingevent`, filed through `sprk_regardinginvoiceline`).
- One Decision Record per **invoice decision**. Its subject is the invoice (`sprk_invoice`, through `sprk_regardinginvoice`).
- A line record is never reached through its invoice, and nothing assumes a line belongs to exactly one invoice. The line's core
  record comes from the line's own core lookups (026 / 013, D-94), never from the invoice.
- Consequence to accept: "Accept all Clean" on an invoice with n clean lines writes n Decision Records.

## 3. The billing actions (implemented)

They are in `DecisionActionCatalog`, as a closed set of their own (`BillingActions`, `TryGetBilling`, `FirstBillingConflict`).
All seven have `PlanLane = Decide`, `IsNextStep = false`, `WorkType = null` (no Signals plan offers them),
`ResolvesSignal = true` and `SendsMessage = false`. The firm notice is WP5 (131, 132), not the decision.

| Code | Label | Subject | Record class | Gate factors | Tier | Parameters | Excludes (mutual) |
|---|---|---|---|---|---|---|---|
| `accept-line` | Accept line | line | Routine | Tier2b, reversible, record of truth | Tier2b | none | reduce-line, disallow-line |
| `reduce-line` | Reduce line | line | Judgement | Tier2b, reversible, record of truth | Tier2b | approvedAmount (Money, req), reason (TextArea, req) | accept-line, disallow-line |
| `disallow-line` | Disallow line | line | Judgement | Tier2b, reversible, record of truth | Tier2b | reason (TextArea, req) | accept-line, reduce-line |
| `approve-invoice` | Approve invoice | invoice | Judgement | Tier4, irreversible, record of truth | Tier4 | note (TextArea, optional) | reject-invoice, return-to-firm |
| `reject-invoice` | Reject invoice | invoice | Judgement | Tier4, irreversible, externally visible, record of truth | Tier4 | reason (TextArea, req) | approve-invoice, return-to-firm |
| `return-to-firm` | Return to firm | invoice | Judgement | Tier4, irreversible, externally visible, record of truth | Tier4 | reason (TextArea, req) | approve-invoice, reject-invoice |
| `override-submission` | Override blocking failure | invoice (the invoice the blocked submission created, held In Review until Override / Return; D-101) | Judgement | Tier3, reversible, record of truth | Tier3 | reason (TextArea, req) | none |

Why these values:
- **Reversible.** Line decisions can change while the invoice is In Review. Approve freezes the approved total and leads to the
  AP export. Reject is final. Return closes the invoice: the firm's resubmission is a new invoice (D-43). An override can still
  be followed by a reject.
- **Externally visible.** Only reject and return show the firm a reason when the decision is made. Line reasons reach the firm
  later, through the WP5 notice.
- **Routine for accept-line.** It leaves the billed amount as it is, like `mark-complete`. A finding the reviewer accepted
  against is still named on the record as a resolved Signal. This is open question Q4.

### Keeping billing codes out of the Signals commit route

The mechanism is a **separate closed set**, not a flag on the shared set:
- `All` and `TryGet` do not contain the billing codes, so every Signals consumer refuses them with no code change:
  - `DecisionPlanService.Resolve` refuses them as `UnknownAction` (a Signals plan cannot name one);
  - `DecisionCommitService` refuses them as `UnresolvedAction`, before anything runs;
  - the ontology admin authoring lists do not offer them;
  - the `DecisionActionExecutors` constructor refuses an executor for one, so a no-op executor cannot be registered.
- `MissingCodes()` stays empty, and `Registry_EveryCatalogActionHasAnExecutor` is unmodified and green.
- Static initialisation throws if a billing code is also a Signals code (`Separate`). Billing exclusions go through the same
  `Mutualize`.
- `DecisionRecordWriter.WriteAsync` still refuses a billing code (`RecordClassUnderivable`, because `TryGet` does not know it).
  Billing records need the entry proposed in section 4.

Why a separate set rather than the POML's catalog flag: a flag puts the codes in `All`, so every consumer of `All`
(`DecisionPlanService`, `DecisionCommitService`, `OntologyAdminAuthoringService`, `MissingCodes`, the commit-route test rig)
would need an edit, and two existing tests would change (`CatalogCodesAreUnique_AndExactlyTheStatedSet` and the MissingCodes
loop). The separate set changes no existing behaviour and no existing test, and it fails closed because nothing on the commit
route knows the codes. ontology-r1 should confirm this (Q2).

## 4. Proposal for ontology-r1: the billing writer entry (as proposed; implemented per section 8)

### 4.1 Why the shipped entries do not fit

| Need | `WriteAsync` (review) | `WriteGateDecisionAsync` (gate) |
|---|---|---|
| Approve, reject or return an invoice with no open finding | Refused: `PolicyVersionId` must be non-empty (`Validate`) and `ResolvedSignalIds` must name at least one Signal | n/a |
| A line accepted with no finding (Accept all Clean) | Refused for the same two reasons | n/a |
| A billing code as the step | Refused: `TryGet` does not know it (and must not, section 3) | n/a: free-text action |
| A decision with no human (049 automatic rejection) | Refused: `ConfirmedByUserId` is required | Communication only: `CommunicationId`, subject hard-wired to `sprk_communication`, keyed by (communication, outcome), and owned under the communication's own rules (D-144) |
| Exactly once per decision id (FR-30, FR-35) | No idempotency on `ReviewId` | Keyed by (communication, outcome), not by decision |

The project rule forbids placeholder policy versions, Signals, communications or cores. So FR-30 and design section 9 cannot be
met as written. That is the §6.5 conflict.

### 4.2 The entry: `DecisionRecordWriter.WriteBillingDecisionAsync`

This is additive, in the same way as ontology tasks 042 / 149 (`WriteGateDecisionAsync`) and 153 (`WriteConditionClearedAsync`).
Nothing existing changes:
- `WriteAsync`, `Validate`, `WriteGateDecisionAsync`, `WriteConditionClearedAsync` and their request shapes are unchanged.
- Below the request it is the SAME code: `Content` (D-34 context, one writer), `FiledAndOwnedAsync` (shared regarding builder,
  core-ancestor stamp, uac-r2 owner over every parent, secure-if-any, the 026 invoice-family checks) and the one `CreateAsync`.

```csharp
public sealed record BillingDecisionRecordRequest(
    Guid DecisionId,                                  // the caller's decision id; with the subject, the idempotency key
    string ActionCode,                                // a billing code (DecisionActionCatalog.TryGetBilling)
    DecisionRecordRef Subject,                        // sprk_billingevent for line codes; sprk_invoice for invoice codes and override
    Guid? ConfirmedByUserId,                          // the deciding human (systemuserid); null ONLY for a system decision
    string DeciderRole,                               // the human's role, or "automated: <what>" for a system decision
    IReadOnlyDictionary<string, object?> Values,      // the action's parameter values (approvedAmount, reason, note) and amounts
    string Outcome,                                   // what the endpoint did, e.g. "Reduced", "Approved"
    IReadOnlyDictionary<string, object?> Facts,       // MANDATORY, non-empty: billed / approved amounts, totals, findings (rule, version)
    string? Reason,                                   // sprk_reason; required when the code's "reason" parameter is required
    Guid? PolicyVersionId = null,                     // the primary resolved finding's policy version; null when none is resolved
    IReadOnlyList<Guid>? ResolvedSignalIds = null,    // the findings this decision resolves (may be empty); the caller closes and links them
    IReadOnlyList<DecisionRecordRef>? Written = null, // rows the decision wrote (approval portion, correction record)
    Guid? DeciderContactId = null,                    // sprk_assignedtointernal (D-129)
    bool PrivilegeFlagged = false,                    // ADR-015, copied forward
    DateTimeOffset? DecidedOn = null);

public static class BillingDecisionKey               // prefix "billing-dr-", distinct from "gate-dr-" and "cleared-dr-"
{
    public static string Of(string subjectEntity, Guid subjectId, Guid decisionId);
    public static Guid IdOf(string subjectEntity, Guid subjectId, Guid decisionId);   // first 16 bytes of SHA-256, as GateDecisionKey
}

public Task<DecisionRecordWriteResult> WriteBillingDecisionAsync(BillingDecisionRecordRequest request, CancellationToken ct = default);
```

Behaviour, in order:
1. **Validate, no I/O.** The checks are:
   - the code is a billing code;
   - the subject table is the code's (line codes take `sprk_billingevent`, the others `sprk_invoice`);
   - `Facts` is non-empty (else `FactSnapshotMissing`);
   - `DecisionId` and the subject id are not empty;
   - `Reason` is present when the catalog marks `reason` required: reduce, disallow, reject, return and override (FR-19, FR-36, FR-37);
   - the catalog's other required parameters are in `Values` (reduce needs `approvedAmount`);
   - `ConfirmedByUserId` is null only when `DeciderRole` starts with `automated:`;
   - column lengths are as `Validate` checks them.

   A failure refuses `ReviewInvalid`, with nothing read or written.
2. **Exactly once.** The key is `BillingDecisionKey.IdOf(subject, DecisionId)`, stored as `reviewId` in `sprk_steps`. The lookup
   is `FindConditionClearedRecordAsync`'s shape: the subject's regarding lookup, the key in `sprk_steps`, `"code":"<billing code>"`
   in `sprk_steps`, and `createdby` = the writer identity, through the shared sysadmin reader.
   - If a record is found, nothing is written and the result is `AlreadyRecorded = true` with the existing id.
   - If the lookup faults, nothing is written: a new refusal reason, `decision_billing_lookup_failed`.
3. **Core (D-34, D-75).** It is derived from the subject by `CoreAncestorResolver` (the line's own core lookups; never the
   invoice for a line), and the record type comes from the builder's cache.
   - An invoice-family subject must have exactly one stamp; `FileUnderSubjectAsync` already refuses anything else.
   - Callers do not pass a core, so none can name the invoice's matter for a line.
   - `RequireCoreIsTheInvoiceFamilySubjectsOwn` holds by construction.
4. **Plan.** There is one taken step: the code, its catalog label, `Values`, `Outcome` and `Written`. The record is built as
   follows:

   | Field | Value |
   |---|---|
   | Class | the catalog's `RecordClass` |
   | `sprk_decisionoutcome` | Authorized: the decision was taken, including reject and return (Q3) |
   | `sprk_actioncode` | the code |
   | `sprk_gatetier` | `DecisionGateTier.ToText(DecisionGateTier.Resolve(def.Gate))` for a human decision; null for a system decision, as on the gate path |
   | `sprk_policyversion` | set only when given |
   | `sprk_confirmedby` | null for a system decision |
5. **Owner.** It is `FiledAndOwnedAsync` as for a review: never the gate's communication rule, and `IsGate = false`. The parents
   are the derived core, the subject and its derived cores, and `RequestedBy` is the confirmer. A system decision uses
   `NoRequester = true`, so `sprk_createdbyperson` stays empty, as for the condition-cleared record.
6. **Create.** The one `CreateAsync`. Errors raise the same single exception type, logged by `LogRefusal`.

Size: about 150 lines in `DecisionRecordWriter.cs`:
- the request record and the key class;
- the entry, `ValidateBilling` and `FindBillingRecordAsync`;
- a small subject-core helper: a single-stamp version of `ResolveGateCoreAsync`, without the D-37 tie-break, because
  invoice-family subjects are single-core;
- one refusal constant.

Tests would go in `DecisionRecordWriterTests` (the entry) and in one billing case in `DecisionRecordWriterSeamTests`.

### 4.3 §6.5 resolution path: **B (amendment)**

- **The rule.** Spec FR-30 and the design section 9 "Billing actions" row (agreed with ontology-r1, 2026-10-14) say: "Human
  decisions use the commit route (`WriteAsync`); decisions without a human use `WriteGateDecisionAsync` with a null confirmer,
  queue-based and idempotent."
- **The conflict.** The table in 4.1 shows that both entries refuse billing decisions unless the request carries fabricated
  data, which the project forbids.
- **The proposed path.** B: amend FR-30 and the design section 9 row to "billing decisions, human or not, use the additive
  `WriteBillingDecisionAsync`, idempotent by decision id". The amendment merges with the writer change.
- **The rationale.** Writer entries already have additive precedent (042 / 149, 153), and the existing entries stay right for
  their callers. The amendment only corrects which entry billing uses.
- **The impact:**
  - one new public method and request type on an ontology-owned class;
  - no change to existing behaviour or tests;
  - FinanceModule is untouched by the writer itself; the recorder adds one registration (ADR-010 ceiling 166 to be checked).
- **Alternatives rejected:**
  - **A (project exception).** This is not a narrow deviation: the agreed mechanism cannot work for any billing decision that
    has no open finding.
  - **C (comply).** It needs a placeholder policy version or Signal, or a communication subject, which the project rule forbids.
  - **Relax `Validate` in place** (optional policy version, Signals and confirmer). This weakens the review contract the commit
    route relies on, and editing the writer's validation in place is forbidden.
  - **Generalise `WriteGateDecisionAsync` to any subject.** Its key (communication, outcome), its owner rule (D-144,
    `KeepCreatorUnlessTargetIsTeamOwned`) and its queue are communication-specific. A billing decision is keyed by decision,
    not by outcome.
  - **A recorder-side lookup in front of `WriteAsync`.** It still fails the policy-version and Signal checks. The `createdby`
    narrowing needs the writer identity, which is internal to the writer.

### 4.4 Recorder shape (Finance side, after the entry is agreed)

`Services/Finance/BillingDecisionRecorder.cs` is a concrete class registered in `AddFinanceModule`, with one method:

```csharp
Task<BillingDecisionRecordResult> RecordAsync(BillingDecision decision, CancellationToken ct);
// BillingDecision: DecisionId, ActionCode, Subject (line id | invoice id), Decider (systemuserid + role + contact, or Automated(role)),
//                  Values, Reason, Outcome, ResolvedFindings (Signal ids + primary policy version), Written, Facts, PrivilegeFlagged.
// BillingDecisionRecordResult: RecordId, AlreadyRecorded, RecordClass.
```

- Before any I/O it refuses a missing reason for reduce, disallow, reject, return and override, and a code / subject mismatch.
  The consumers' 400 `ebilling.reason_required` comes from this, with no write.
- It builds one `BillingDecisionRecordRequest` per decision: one for each line in a bulk line action (D-75), one per invoice
  decision.
- It does not close or link Signals. Each consumer does that with the record id: 100 for line findings; 103, 104 and 105 for
  invoice findings.
- Consumers choose a deterministic decision id where FR-35 / FR-36 need exactly-once behaviour across callers. For example, 103
  derives it from the approval portion, so a second approver's attempt finds the first record.

## 5. Open questions for ontology-r1

| # | Question | Our proposal |
|---|---|---|
| Q1 | Accept the additive `WriteBillingDecisionAsync` (section 4.2, path B)? | Yes |
| Q2 | Billing codes as a separate closed set in `DecisionActionCatalog` (outside `All` / `TryGet`) rather than a flag on the shared set? | Separate set (implemented; no existing test or behaviour changed) |
| Q3 | `sprk_decisionoutcome` for reject and return: Authorized (decision taken) or Denied? | Authorized; the code says what was decided |
| Q4 | `accept-line` Routine or Judgement? | Routine |
| Q5 | 049's automatic submission rejection, and 105's Return of a blocked submission, have no filable subject: `sprk_decisionrecord` has no submission lookup, and there is no invoice. | **Superseded by D-101 (owner, 2026-10-17):** every read submission creates its invoice, so the automatic rejection, the override and the blocked return all file under that invoice. No submission lookup, no schema change. |
| Q6 | Gate tier on a system decision | Null, as on the gate path |

## 6. Verification (part one, the catalog)

### Build and tests

| Check | Result |
|---|---|
| BFF build (`-m:1`) | Green; no warnings in the changed file |
| Unit project, filter DecisionAction, DecisionRecord, Billing, Signal, Finance, DecisionCommit, DecisionGateTier, OntologyAdminAuthoring | 3,379 passed, 6 skipped, 0 failed |
| Spaarke.ArchTests | 1,126 / 1,126 |
| Existing tests | None modified. `CatalogCodesAreUnique_AndExactlyTheStatedSet`, `Registry_EveryCatalogActionHasAnExecutor` (MissingCodes) and `OnlyTheInquiryAndTheReminderLeaveTheItemOpen` are unchanged and green |

### Fail-before proofs

| Test state | Result |
|---|---|
| The pre-task catalog (HEAD) under the new tests | Does not compile: 34 CS0117 (`BillingActions`, `TryGetBilling`, `FirstBillingConflict`) |
| Mutation 1: Signals `TryGet` also resolves billing codes (billing leaks into the commit route) | 11 fail: 4 plan-refusal rows, the separation test, the executor-registry test, 5 commit-route rows |
| Mutation 2: `reject-invoice` no longer excludes `return-to-firm` | 1 fails (the exclusion test) |
| Mutation 3: `reject-invoice` reason made optional | 1 fails (the required-parameters test) |

The catalog was restored after each mutation, and the suites re-ran green.

### BFF hygiene (§10)

**Publish size:**

| Item | Value |
|---|---|
| Command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <dir>` |
| Target | framework-dependent linux-x64 |
| Compression | `Compress-Archive -CompressionLevel Optimal` |
| PDBs | included |
| Base (`0c1c058d1`) | 39,467,507 B |
| This change | 39,468,978 B |
| Delta | +1,471 B; total 39.5 MB |
| File count | 192 / 192 |

- **Deviation from the fresh-worktree rule.** Both sides were published from this agent worktree, with only the catalog file
  swapped. The agent is isolated to its worktree. The matching file counts, and the billing code present only in the changed DLL,
  show that both publishes are complete. Re-measure from fresh short-path worktrees with the writer entry and the recorder.
- **Packages.** There are no package changes. `dotnet list package --vulnerable --include-transitive` reports no vulnerable
  packages.
- **ADR-010.** There are no DI changes yet, and the ceiling of 166 is untouched.

## 7. Files

- `src/server/api/Sprk.Bff.Api/Services/Signals/Actions/DecisionActionCatalog.cs` (ontology-owned; additive billing set)
- `tests/unit/domain/Signals/DecisionActionCatalogTests.cs` (new billing section; existing tests unmodified)
- `tests/unit/domain/Signals/DecisionActionExecutorsTests.cs` (one new test; existing tests unmodified)
- `tests/integration/data-mutation/Signals/DecisionCommitServiceTests.cs` (one new theory; existing tests unmodified)

Part two (section 8):

- `src/server/api/Sprk.Bff.Api/Services/Signals/DecisionRecordWriter.cs` (ontology-owned; additive billing entry, insertions only)
- `src/server/api/Sprk.Bff.Api/Services/Finance/BillingDecisionRecorder.cs` (new)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs` (one registration)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.Billing.cs` (new partial)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/BillingDecisionRecorderTests.cs` (new)
- `tests/integration/seam/Signals/DecisionRecordWriterBillingSeamTests.cs` (new; live opt-in, not run)

## 8. Part two: the writer entry and the recorder (D-100, D-101; 2026-10-17)

Base `62a1ec56b`. Commits `9c9f47cb8` (writer entry), `0eac6d2aa` (recorder), `1ca994acc` (code-review F2), `92b18d3bb` (adversarial
round, 8.6).

### 8.1 What was built

- `DecisionRecordWriter.WriteBillingDecisionAsync(BillingDecisionRecordRequest)` and `PreflightBillingDecision`, with
  `BillingDecisionKey` (`billing-dr-{subject}-{subjectId}-{decisionId}`, SHA-256 to a Guid, stored as `sprk_steps.reviewId`) and the
  refusal reason `decision_billing_lookup_failed`. The shape is as proposed in 4.2, with these settled differences:
  - the subject table is fixed per code by the entry's own map (`BillingSubjectByCode`): the three line codes file under the line, every
    other code under the invoice (D-75; D-101 puts the override, the automatic rejection and the blocked return under the invoice);
  - a system decision (null confirmer) may not name a deciding contact (code-review F2, D-129);
  - a missing record-type catalog row for the derived core refuses as `FilingFailed` instead of writing without a core. An invoice-family
    subject requires its core pair, so the shared 026 check would refuse it anyway, with a less accurate reason.
- `Services/Finance/BillingDecisionRecorder.cs`: `BillingDecision`, `BillingDecider` (`Human` / `Automated` -> `automated: ...`) and
  `BillingDecisionRecordResult`; `RecordAsync` and `Preflight`. Concrete and singleton (its one dependency is the singleton writer),
  registered in `AddFinanceModule`. The ADR-010 1:1 ceiling (166) and the route ledger (137) are unchanged: no interface, no route.
- The override row's subject (section 3) and Q5 (section 5) follow D-101. No submission lookup, no schema change.

### 8.2 ontology-r1's binding conditions and their tests

All tests are in `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.Billing.cs`, a new partial. The existing writer
test files are untouched.

| # | Condition | Test(s) |
|---|---|---|
| C1 | A billing code via `WriteAsync` is refused; a Signals code via the billing entry is refused | `Billing_C1_ABillingCodeThroughTheExistingWriteAsync_IsRefused_AndNothingIsWritten` (4 codes); `Billing_C1_AnOntologyOrSignalsCodeThroughTheBillingEntry_IsRefused_BeforeAnyIo` (5 codes) |
| C2 | A subject-table mismatch is refused before any I/O: zero reader calls, zero `CreateAsync` | `Billing_C2_ASubjectThatIsNotTheCodesTable_IsRefused_WithZeroReaderCallsAndZeroCreates` (10 cases: both directions, plus matter, To Do and submission); `Billing_C2_TheBillingEntrysSubjects_AreTheLineForLineCodes_AndTheInvoiceForEveryOther` |
| C3 | Idempotent by decision id; `createdby` = the writer identity through the sysadmin reader; another creator is not AlreadyRecorded | `Billing_C3_ARepeatedDecisionId_ReturnsAlreadyRecorded_WithExactlyOneCreate`; `Billing_C3_TheExistenceCheck_ReadsCreatedByEqualToTheWriterIdentity_ThroughTheSysadminReader`; `Billing_C3_ARowWithTheSameKeyAndCodeButAnotherCreator_IsNotAlreadyRecorded_AndTheRecordIsWritten`; also `Billing_C3_AWriterRecordWithTheSameKeyButAnotherCode_IsNotThisDecisionsRecord` (a Signals code under the key is skipped), `Billing_C3_TheSameDecisionIdWithAnotherBillingCode_IsRefused_AndNoSecondRecordIsWritten` (adversarial F2) and `Billing_C3_AnotherDecisionIdOrAnotherSubject_IsItsOwnRecord` |
| C4 | A lookup fault writes nothing (seeding proof in 8.3) | `Billing_C4_ALookupFault_WritesNothing`; `Billing_C4_AWriterIdentityFault_WritesNothing`; `Billing_C4_ALookupTimeoutSurfacingAsACancellationTheCallerDidNotAskFor_IsTheLookupRefusal_AndWritesNothing` (adversarial K4) |
| C5 | A null confirmer is refused unless the decider is `automated:` | `Billing_C5_ANullConfirmerWithoutAnAutomatedDecider_IsRefused_BeforeAnyIo` (3 roles, each with null and Guid.Empty); `Billing_C5_ASystemDecision_IsRecordedWithNoConfirmer_NoRequester_AndNoGateTier`; `Billing_C5_AHumanDecision_RecordsTheConfirmingUser_AndTheCodesGateTier` |
| C6 | The 026 checks run on the new path, by reuse | `Billing_C6_AnInvoiceOrLineNotFiledUnderExactlyOneCore_IsUnfileable_AndNothingIsWritten` (4 cases); `Billing_C6_ALineDecision_IsFiledUnderTheLine_WithTheLinesOwnCore_NeverItsInvoicesMatter`; `Billing_C6_AnInvoiceDecision_IsFiledUnderTheInvoice_WithTheInvoicesOwnMatterAsItsCore`; `Billing_C6_AFirstDerivationThatFaults_IsAFilingFault_NotAPermanentRefusal_EvenWhenARereadWouldSucceed` (adversarial F1). Reuse: the entry calls the shared `CreateAsync` -> `FiledAndOwnedAsync` -> `FileUnderSubjectAsync` + `RequireCoreIsTheInvoiceFamilySubjectsOwn`; the 026 checks still occur once in the file |
| C7 | Additive only | `git diff 62a1ec56b..HEAD` removes 0 lines from any pre-existing file. The existing writer tests, `WriteAsync`, `Validate`, the request shapes and the catalog's Signals set are untouched |

Edges beyond the stated contract, one line each:
- A user-confirmed decision with an `automated:` decider is refused, because it would misstate who decided.
- A system decision that names a deciding contact is refused (D-129).

**How condition 2 was read.** The condition says "sprk_invoice and sprk_billingevent are on the billing entry's allowed subject list
only, not on WriteAsync's". The billing entry has its own list (`BillingSubjectByCode`: invoice and line only), and `WriteAsync` neither
reads nor changes it. Both tables were ALREADY in `WriteAsync`'s shared subject map before this task
(`TodoRegardingBuilder.SignalAndDecisionRecordRegardingLookupByEntity`: task 141 added the invoice, task 026 the line), because a Signals
review of an invoice or line Signal is filed under it. Removing them would change `WriteAsync` (condition 7) and break 026. ontology-r1 to
confirm this reading.

### 8.3 Seeding proofs (mutate, run, restore)

Each mutation was applied to one file, the billing tests were run, the file was restored and the suite re-run green.

| Mutation | Result |
|---|---|
| **M1 (C4, the requested proof): the lookup's fault handler returns "none found" instead of refusing** | 2 fail: `Billing_C4_ALookupFault_WritesNothing` and `Billing_C4_AWriterIdentityFault_WritesNothing` (a record is created) |
| M2 (C3): the `createdby` condition removed from the lookup | 2 fail: the other-creator test and the createdby-condition test |
| M3 (C2): the subject-table check removed | 10 fail (every C2 case) |
| M4 (C5): the null-confirmer check removed | 3 fail (every C5 refusal case) |
| M5 (C1): the billing entry also accepts Signals codes | 3 fail (the Signals catalog codes) |
| Recorder: every decision filed under the invoice | 3 fail (the line-decision tests) |
| Base writer and no recorder (`62a1ec56b`) under the new tests | Do not compile: CS0246 for `BillingDecisionRecordRequest`, `BillingDecisionRecorder`, `BillingDecision` and `BillingDecider` |
| Adversarial round: the `1ca994acc` writer under the `92b18d3bb` tests | 3 fail (the F1, F2 and K4 tests above); 50 pass. The fixed writer restored: 68 / 68 |

### 8.4 Verification

| Check | Result |
|---|---|
| BFF build | Green; no warnings in the changed files |
| Unit filter: Decision, Billing, InvoiceFamily, E026, CoreAncestor, Signal, OntologyAdminAuthoring | 3,361 passed, 3 skipped, 0 failed (before the F2 fix; the writer and recorder tests re-ran after it, 248 / 248) |
| Unit filter: DiGraphValidation, EbillingEnabledFilter, Services.Finance | 624 / 624 |
| Spaarke.ArchTests | 1,160 / 1,160 |
| After the adversarial round (`92b18d3bb`): unit filter DecisionRecord, DecisionActionCatalog, Billing, FinanceModule, InvoiceFamily, CoreAncestor | 1,354 / 1,354 |
| After the adversarial round: ArchTests DecisionRecordContextImmutability, RecordOwnerAssignmentCensus, ADR010 | 47 / 47 |
| After the adversarial round: `git diff --numstat 62a1ec56b -- src/ tests/` | 0 removed lines in every file (C7) |
| Seam `DecisionRecordWriterBillingSeamTests` (new file, one live billing case) | Compiles; NOT run live, because this task makes no live writes. Opt-in like the other writer seams |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages; no package or csproj change |

**Publish size (CLAUDE.md section 10).** Each side was exported with `git archive` (src/server, config, the root props and global.json)
into a fresh short-path tree under `C:\tmp`, published with `dotnet publish -c Release`, and zipped with
`Compress-Archive -CompressionLevel Optimal`. The agent cannot add git worktrees, so a clean archive export stands in for the fresh
worktree; it is the same tree.

| Side | Zip bytes | Files |
|---|---|---|
| master `1df4ee12d` (fresh) | 39,126,116 | 192 |
| task base `62a1ec56b` | 39,545,535 | 192 |
| task head (before the F2 fix, which adds a few bytes) | 39,554,267 | 192 |

| Measure | Value | Threshold |
|---|---|---|
| This task (base to head) | +8,732 B | +5 MB per task |
| Branch over master | +428,151 B | — |
| Total | 39.55 MB | 55 MB triggers review; 60 MB is a hard stop |

The two fix commits (`1ca994acc`, `92b18d3bb`) change method bodies in the same assembly and add no file or package; they were not
re-measured. Their size is a few hundred bytes of IL against the +5 MB per-task threshold.

### 8.5 Component justification (CLAUDE.md section 11)

**`WriteBillingDecisionAsync`, with its request, key, refusal reason and preflight.**
- Existing: `WriteAsync` (review), `WriteGateDecisionAsync` (communications gate) and `WriteConditionClearedAsync` (nightly closure), all
  in `DecisionRecordWriter.cs`.
- Extension: `WriteAsync` requires a policy version, resolved Signals, a Signals plan code and a confirmer. A clean or automatic billing
  decision has none of these, and relaxing `Validate` would weaken the commit route's contract, which the owner forbids. The gate entry is
  keyed by (communication, outcome) and owned under the communication's rules. So the change is an additive entry over the same shared
  create, as 042 / 149 and 153 were.
- Cost of doing nothing: no billing decision without an open finding can be recorded (approving a clean invoice, Accept all Clean), and
  049's automatic rejection cannot be recorded at all. That fails FR-30, FR-19, FR-37 and NFR-09.

**`BillingDecisionRecorder`, with `BillingDecision`, `BillingDecider`, the result and one DI registration.**
- Existing: the writer entry above. Nothing under `Services/Finance` records a decision.
- Extension: the code-to-table pairing and the human / automated decider shape are the eBilling callers' contract. Putting them in the
  ontology-owned writer would make it carry eBilling caller types.
- Cost of doing nothing: five callers (049, 100, 103, 104, 105) would each build the request. One that passed a confirmer for a system
  decision, or a line code with an invoice id, would be refused at run time after it had already changed the invoice.

### 8.6 Quality gates

**code-review.** One F-class finding, fixed in `1ca994acc`: a system decision could name a deciding contact (F2, D-129). Known limits:
- K1: the new entry is not listed in `DecisionRecordContextImmutabilityTests.CreateEntryPoints`. The one-create-site rule still holds and
  is pinned. The list is an existing ontology test kept byte-identical, so ontology-r1 should add the name when it reviews.
- K4: `approvedAmount` is checked for presence, not range; consumer 100 owns the amount rule.
- K4: `ThrowIfNull` on a non-nullable request follows the writer's existing idiom.

**adr-check.** No violations:
- ADR-001: no endpoint.
- ADR-002: one server-side create owner, no plugin.
- ADR-010: concrete, feature module, ceiling unchanged.
- ADR-013: only the `PublicContracts.DecisionGateTier` facade.
- ADR-032: unconditional registration over an unconditional dependency.
- ADR-039: the closed billing set.
- ADR-044 / ADR-024: the shared `Content` and regarding builder.
- ADR-038: tests added, none deleted.

**adversarial-reviewer (Opus; two passes over `62a1ec56b..1ca994acc`; both found all seven conditions hold).** Fixed in `92b18d3bb`, each
with a test that fails against `1ca994acc`:
- F1: a transient fault in the first core derivation handed "no core" on. When the filing step's re-derivation succeeded, the 026 check
  refused `SubjectUnfileable` (permanent) for a transient fault. It is now classified at once: a fault is `FilingFailed`, a data condition
  `SubjectUnfileable`.
- F2: the key is (subject, decision id) with no code, but the lookup also filtered on the code. So the same id with another billing code
  (reject, a create timeout, then return-to-firm) wrote a second, contradictory record. The lookup now reads `sprk_actioncode` and skips
  non-billing codes (a Signals review that reused the id). The same code is AlreadyRecorded; another billing code is refused `ReviewInvalid`.
- K4, treated as fix-now because it is a real path: a lookup timeout that surfaces as `TaskCanceledException`, with the caller's token not
  cancelled, escaped raw. It is now `BillingRecordLookupFailed`, with the same filter `ResolveBillingCoreAsync` uses.
- Doc: the subject map's comment claimed a static-initialisation guard that does not exist; the guard is the exact-set C2 test.

Known limits (none is a confirmed defect on a path that exists today):
- K1, a caller obligation for 049, 100, 103, 104 and 105: "exactly once" holds for retries made one after another. Two concurrent calls
  with the same decision id can both write, because the check is lookup then create with no unique key, the same as on the gate path. Each
  caller records under its own per-invoice serialisation (its status check-and-set). 103's "the second approver finds the first record"
  (4.4) holds only that way.
- K3: an AlreadyRecorded result echoes the request's `ResolvedSignalIds`, as the gate path does. A caller links only the findings its
  first write named.
- K4: `BillingDecisionRecordResult` does not carry the writer's `ConfirmedByUserId`. 103 adds it if it needs to know who approved first.
- K4: the live seam test reports Passed when the live environment is absent, like its sibling seam tests.
- K4, the same as `WriteAsync`: `Values` accepts undeclared keys; a `null` in `Written` or an unserialisable value throws a non-refusal
  exception from `Content`. Only internal callers can supply these.
- K, doc drift in pre-existing lines left unchanged to keep C7: the `Plan.NoRequester` comment ("only for a condition-cleared closure")
  and the constructor's `entities` comment. For ontology-r1, with `CreateEntryPoints` (K1 above).
- Invalid caller input is logged at Error and counted in `ontology.writer.failures`, as `WriteAsync`'s is. Callers run `Preflight` first.
