# Task 105 - Override (server)

Security-sensitive, rigor FULL. Delivers FR-19 and FR-37: a user holding the "eBilling Override" privilege accepts a blocked internal
submission with a mandatory reason, as one person; any user with rights on the submission can return it to the firm with no privilege.

## 1. What "blocked" is, and what Override changes (D-101)

Task 049 persists a submission the rejection rule would reject, on the internal channel, as an invoice In Review whose submission is
`Rejected` with `sprk_outcomereasoncode = ebilling.submission.blocked`. That is the held set (`FinanceBaseValues.HeldSubmissionConditions`:
status in Processing/Rejected AND the blocked code). Held invoices never count toward spend (D-102/D-103). The held set has exactly two
exits, and this task owns both.

| Verb | Privilege | Submission after | Invoice after | Decision Record |
|---|---|---|---|---|
| Override | `prvWritesprk_ebillingoverride` (D-76), a reason | `Accepted`, code `ebilling.submission.overridden` | unchanged (In Review; no longer held, so it reaches approval and pending spend) | one, `override-submission`, subject = the invoice |
| Return | none (D-73), a reason; the route requires Read+Write on the submission | `Rejected`, code `ebilling.submission.returned` | In Review to Returned to Firm via `IInvoiceLifecycleService` | one, `return-to-firm`, subject = the invoice |

Both leave the held set (the code no longer matches / the status is no longer Processing-or-Rejected-with-blocked-code). There is no
second invoice-creation path: under D-101 the invoice and its lines already exist, so the POML's "049 accept path" note is obsolete and
**no 049 file was changed** (after-steps, persister, `Services/Signals/**`, `SignalWriter`, `DecisionRecordWriter`, `FinanceBaseValues` untouched).

## 2. Design

- `Services/Finance/SubmissionOverrideService.cs` - `GetAsync` / `OverrideAsync` / `ReturnAsync`; one `DecideAsync` for both verbs.
- `Services/Finance/EbillingOverridePrivilege.cs` - the one definition of the right (`HoldsAsync`, fail closed).
- `Api/Finance/SubmissionOverrideEndpoints.cs` - `GET /api/finance/submissions/{id}/blocked`, `POST .../override`, `POST .../return`.
- `Services/Finance/Submission/SubmissionReasonCodes.cs` - `Overridden`, `Returned` (added to the existing class, not respelled).
- `OperationAccessPolicy` - `ebilling.submission.decide` = Read|Write (own line only).
- `scripts/ebilling/Deploy-EbillingOverridePrivilegeSchema.ps1` - creates the carrier table and the role. **Written, parse-checked, NOT run** (no live Dataverse writes in this task).

### Privilege mechanism (D-76)

Dataverse has no custom privilege type, so the privilege needs a carrier: a dedicated organization-owned table `sprk_ebillingoverride`
(no rows). `Write` on it is `prvWritesprk_ebillingoverride`; the script creates the role "eBilling Override" and grants it at Global
depth (the only depth an org-owned table offers). `CallerRecordAccessProbe.CallerHoldsPrivilegeAsync` asks Dataverse AS THE CALLER (OBO,
uncached, fail closed). No role-name check, no Entra app role. This is the same mechanism as the billing admin's `prvWritesprk_ruleset`.

The privilege is asked **inside the service, on every call**, not by the route filter: the filter's denial code is the generic
`sdap.access.deny.insufficient_privilege`, the acceptance criterion is the stable `ebilling.override.not_permitted`, and `canOverride`
on the read needs the same question from a GET route, which a filter cannot answer.

### Order of a decision (074 caller obligations)

1. Caller identity (`GetCallerSystemUserIdAsync`), then the privilege (Override only). Before anything is read, so a refusal says nothing about the submission.
2. Reason: required (trimmed), at most 2000 characters.
3. Read the submission and give every refusal that needs no write.
4. Per-invoice lock `ebilling-invoice-decision:{invoiceId:N}` (`SubmissionOverrideService.LockKey`) via `IIdempotencyService`, 2 minute lease, owner-checked release in `finally`. **Tasks 103/104 should take the same key** so an approval and an override of one invoice never run together.
5. Re-read the submission and the invoice under the lock; invoice must be In Review (Override) or In Review / already Returned to Firm (Return, finishing an interrupted return).
6. Parse the stored findings (`InvoiceSubmissionAfterSteps.StoredFindings.Parse`): they must be a Blocked evaluation for THIS invoice with at least one deciding finding.
7. Build the `BillingDecision` from the caller and the stored findings only; `Preflight` (the writer's I/O-free refusals).
8. **Record first** (`BillingDecisionRecorder.RecordAsync`). A refusal or fault leaves the submission and the invoice exactly as they were, apart from the decision's Signal set, which is read, authorized and stored just before the record and closed just after it (section 9).
9. Return only: `TransitionAsync(ReturnedToFirm)`; a refused or faulted move stops before the submission is touched.
10. Conditional submission write (`BulkUpdateIfRowVersionsMatchAsync`): status/code and the stamp in `sprk_findingsjson`. A version conflict re-reads once: already decided this way is a no-op, anything else is `concurrent_change`.
11. Override only: best-effort `SubmissionDecidedAsync(Accepted, Internal)` on the notification seam (task 132 notifies the eligible approvers). A sink fault never undoes the override.

The decision id is derived server-side from the submission (`OverrideDecisionIdFor` / `ReturnDecisionIdFor`), and every field of the
decision is rebuilt identically on every attempt, so a retry after any partial failure finds the same Decision Record (the recorder is
idempotent by subject and decision id, content-hashed) and finishes the remaining steps instead of writing a second record.

### The decision (D-100, 074)

Code `override-submission` / `return-to-firm`, subject = the invoice, decider `BillingDecider.Human(systemuserid-of-the-caller, role)`,
Outcome `Overridden` / `Returned`, Reason = the trimmed reason, Values strings only (`submissionId`, `trackingNumber`), PolicyVersionId =
the first deciding finding's version (read from the stored findings), `ResolvedSignalIds` = the D-122 set (the open Signals on the
invoice and its lines; null only when there are none, see section 9), `Written` = the invoice. Facts carry the decision, outcome, tracking
number, blocking reason codes, the stored, scrubbed Signal plan (D-112: never the raw findings) and the set's read time and policy codes
(`resolvedSignalSet`).

### Stored result and the read

The decision is stored in `sprk_findingsjson` under `override` or `return` (`recordId`, `bySystemUserId`, `on`, `reason`); every other
member is kept as it was. `GET .../blocked` returns the state (`Blocked` / `Overridden` / `Returned` / `NotBlocked`), the findings with
`Blocking` marked and, once overridden, `Overridden` set on the blocking ones (an override never deletes or rewrites a finding),
`canOverride` (blocked, invoice In Review, caller holds the privilege) and `canReturn`, and the stamps.

## 3. CLAUDE.md section 11 - component justification

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `SubmissionOverrideService` (+ 1 `AddScoped` in `FinanceModule`; concrete, so the ADR-010 1:1 census stays at 169) | `BillingDecisionRecorder`, `IInvoiceLifecycleService`, `InvoiceSubmissionService` (reads for the submitter), 049's persister (created the invoice) | None decides a held submission; they are tools a caller uses, and 044's service only reads status for the submitter | An internal user cannot accept a legitimately blocked invoice (a negotiated rate not yet in the rate card) or return it: FR-19/FR-37 fail and a held invoice is held forever, outside approval and pending spend |
| Three routes in `SubmissionOverrideEndpoints` (+ `MapSubmissionOverrideEndpoints` line in `EndpointMappingExtensions`; ledger entry and file count 139 to 140 in the route-authorization guard) | `InvoiceSubmissionEndpoints` (submit and status routes) | Those routes belong to the submitter and the SPA; a reviewer's decision routes have a different caller, rights and codes. Adding them there would mix the two audiences | No HTTP surface for the Invoice Review wizard/page (052, 111) to call |
| `EbillingOverridePrivilege` | `CallerRecordAccessProbe.CallerHoldsPrivilegeAsync`, `BillingAdminAuthorization` | The admin check is a route filter with the generic denial code and cannot answer from a GET route | Three consumers would each spell the privilege name and its failure handling; one that treated a probe fault as "allowed" would let anyone accept a blocked invoice |
| `sprk_ebillingoverride` table + role + script | `sprk_ruleset`'s privilege for the billing admin | A custom privilege does not exist in Dataverse; reusing `sprk_ruleset` would make "may override an invoice" and "may edit billing rules" the same right | No grantable privilege, so D-76 (a Dataverse table privilege) cannot be met |
| `SubmissionReasonCodes.Overridden/Returned`, `ebilling.submission.decide` policy key | `SubmissionReasonCodes`, `OperationAccessPolicy` | Added to the existing classes | The repeat guard could not tell an overridden submission from one processing accepted; the route filter would have no key (`OperationAccessPolicyCompletenessTests` fails on an unlisted key) |

## 4. Negative cases, each pinned by a test

(`S` = `SubmissionOverrideServiceTests`, `C` = `SubmissionOverrideEndpointsAuthorizationContractTests`)

| Case | Result | Test |
|---|---|---|
| Unauthenticated, any of the three routes | 401, nothing touched | C `Unauthenticated_Returns401_AndTouchesNothing` |
| No Read/Write on the submission | 403 (filter); GET is the uniform 404; the privilege is never asked | C `Post_ByACallerWithNoRights...`, `Post_WithReadButNotWrite...`, `Get_ByACallerWithNoRead...` |
| Rights on another submission, id swapped in the query | 403, rights checked against the route id | C `RightsOnAnotherSubmission...` |
| Rights check faults | 403/503, nothing written | C `AFailedRightsCheck_IsDenied` |
| Override without the privilege | 403 `ebilling.override.not_permitted` (+ `reasonCode`), nothing read or written | S/C `..WithoutThePrivilege..` |
| Privilege check faults | same 403 | S `Override_WhenThePrivilegeCheckFaults`, C `..Faults_Is403NotPermitted` |
| No bearer token / caller identity unknown / identity lookup faults | 403 (Return: `ebilling.blocked.decider_unverified`) | S `..WithoutACallerToken`, `..CannotBeIdentified`, `..IdentityFaults`, C `..WithoutABearerToken` |
| Reason missing, blank, no body / too long | 400 `ebilling.reason_required` / `ebilling.reason_too_long` | S, C `Post_WithoutAReason` |
| Body names decider, invoice, decision id, policy version, outcome | ignored: decider is the caller, ids derived | C `Override_ABodyCannotNameTheDecider...` |
| Not internal (SPA), Failed, accepted by processing, still processing, rejected for another reason, received | 409 (`not_internal`, `submission_failed`, `already_accepted`, `still_processing`, `not_blocked`), nothing changes | S theories |
| Return after Override / Override after Return | 409 `already_overridden` / `already_returned` | S |
| Repeat of the same verb | 200 `alreadyDecided`, no second record, no write, no notice | S, C |
| Invoice not In Review (Approved, Sent to AP, Paid, Rejected, Cancelled), gone, or no invoice linked | 409 `invoice_not_held`; Returned to Firm gives `already_returned` | S |
| Stored findings missing, garbage, not an object, another invoice, not a Blocked evaluation, no deciding finding | 409 `findings_unreadable`, nothing changes | S theory |
| Another decision holds the invoice lock | 409 `in_progress`, nothing changes, the foreign lock is not released | S |
| Lock cannot be taken / read faults | 503 `unavailable`, nothing changes | S |
| Decision Record cannot be written / writer refuses | 503 `record_unavailable` / 409 `decision_conflict` / `record_refused`; submission still held, invoice unmoved | S |
| Submission write fails after the record | 503; a retry finds the same record (one `sprk_decisionrecord` row) and finishes | S `..ARetryAfterTheSubmissionWriteFailed..` |
| Concurrent writer finished the same override / changed the submission | no-op `alreadyDecided` / 409 `concurrent_change`, nothing overwritten | S |
| Lifecycle refuses or faults the Return move | 409 / 503, submission stays held; a retry finishes with one record | S |
| Interrupted Return (invoice already Returned to Firm) | finishes without moving it again | S |
| Decision would overflow `sprk_findingsjson` | 409 `findings_too_large`, submission not released | S |
| Return with no privilege | 200 (D-73); the override privilege is never asked | S, C |
| Kill switch off | 503 `ebilling.disabled` after authorization; an unauthorized caller still gets 403 | C |
| Logs | carry ids/codes only, never the reason text | S `Logs_CarryIdsAndCodesOnly_NeverTheReason` |
| Pins | privilege name = the script's table; reason codes; error codes; lock key | S `ThePrivilegeName_Is...`, `The*Codes_*` |

## 5. Fail-before evidence

The tests are for new code, so "before" is "the guarded behaviour removed". Each mutation was applied to the source, the
`SubmissionOverride*` tests (119) were run, and the source was restored (`git status` showed only the two new test files afterwards).

| Mutation | Failing tests |
|---|---|
| **Seeding proof**: `EbillingOverridePrivilege.HoldsAsync` returns `true` after the token check | 9 (incl. `Override_WithoutThePrivilege_Is403NotPermitted...` in S and C, `Override_WhenThePrivilegeCheckFaults` in S and C, both `canOverride` reads) |
| Probe fault answers `true` (fails open) | 3 (`..PrivilegeCheckFaults..` in S, C and the read) |
| Reason check removed | 7 |
| Lock-acquired check removed | 1 (`..LockIs409InProgress..`) |
| Repeat guard removed (Overridden re-runs) | 2 (`..SameCallTwice..`, `..Repeated_Is200AlreadyDecided..`) |
| Invoice-status check removed | 8 |
| Decision Record write removed | 21 (record-before-mutate, retries, content, ordering) |
| Return asks the override privilege | 2 (`Return_NeedsNoPrivilege..`, `Return_WithRightsAndNoPrivilege..`) |

## 6. Results

- Build `Sprk.Bff.Api`: 0 warnings, 0 errors.
- New tests: 92 (`SubmissionOverrideServiceTests`) + 27 (`SubmissionOverrideEndpointsAuthorizationContractTests`) = 119, all pass. The contract tests are compiled into `Sprk.Bff.Api.Tests` (`tests/integration/contract/**`), so the filtered run below includes them.
- Filtered unit suite (the task's filter): 8481 passed, 11 skipped, 0 failed.
- `tests/Spaarke.ArchTests`: 1214 passed, 0 failed. DI: +1 registration (`AddScoped<SubmissionOverrideService>`), concrete, so the ADR-010 1:1 census stays at 169 (ceiling 169, not raised). Route-authorization ledger: new `GovernedFile`, count 139 to 140. `OperationAccessPolicyCompletenessTests` passes with the new key.
- Publish size (BFF hygiene item 4): `dotnet publish -c Release -r linux-x64 --no-self-contained`, framework-dependent linux-x64, PDBs included, `Compress-Archive -CompressionLevel Optimal`, fresh trees at `C:\wt105{m,a,b}` (`git archive` of master `30324eeac`, base `9b272493b`, tip `fc02833c5`; 192 files each).
  - master 39,250,434 bytes (37.432 MB); base 39,846,278 (38.000 MB); tip 39,870,535 (38.024 MB).
  - **This task: +24,257 bytes (+0.023 MB)** (tip vs base). Whole ebilling branch vs master: +620,101 bytes (+0.591 MB). Well under every threshold (+5 MB / 55 MB / 60 MB).
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages. No package added.

## 7. Known limits

- ~~The blocking Signals stay open on the invoice after an override.~~ Superseded: both decisions now close the open Signals on the invoice and its lines (section 9).
- Return does not change the approval portion; it stays Pending on the Returned invoice (the invoice is final, Returned to Firm, D-43). Whether 103/104 expect the portion closed is theirs to decide.
- The stamp stores `bySystemUserId`, not a display name; the review screens resolve the name.
- A Decision Record can exist while the submission write failed. That is deliberate (record before mutate); the retry finds it. Nothing else reads it as a completed decision, because the stored stamp and the submission code are the state.
- The deploy script ran on dev 2026-10-20 (D-142): Spaarke Core User holds the privilege. In any environment where it has not run, nobody holds it, so Override is refused for everyone (fails closed) and Return still works.
- `ebilling.reason_required` is spelled in `BlockedSubmissionErrors` as well as in task 104's reject/return errors; if 104 lands a shared constant, one of them should reference the other.
- The route filter checks Read+Write on the submission for Return. A user who can only see the invoice cannot return it; that matches D-73 ("no privilege") but is stricter than "any user".

## 8. Open questions for the owner

1. ~~Do you want the blocking Signals themselves re-marked on an override?~~ Answered: yes, closed as Acted by the decision's record, in the shape agreed with ontology-r1 (section 9).
2. ~~FR-39 notification for Return?~~ Answered (D-143): the firm is notified. The seam gained `SubmissionDecisionOutcome.Returned`; Return reports it with `ebilling.submission.returned` after the commit, and task 132 sends the ReturnedToFirm notice.
3. ~~Who may run `Deploy-EbillingOverridePrivilegeSchema.ps1`, and where first?~~ Answered (D-142): applied on dev 2026-10-20 with `-RoleName 'Spaarke Core User'`; Spaarke Core User holds the privilege at Global and no dedicated role exists.
4. 103 and 104 should adopt `SubmissionOverrideService.LockKey(invoiceId)` for approve/reject/return so all invoice decisions serialise. Confirm they will, or the key should move to a shared helper.

## 9. Human-decision Signal close (D-122, agreed with ontology-r1)

An override and a return both resolve every Open or Acknowledged Signal, of every policy, whose subject is the invoice or one of its
lines (the set 049's automatic rejection resolves, read by the same shared `InvoiceSubmissionAfterSteps.ReadOpenSignalSetAsync`). Each one
is closed as **Acted**, never Dismissed.

- **No impersonation.** The app-identity `SignalWriter.CloseActedByDecisionAsync` closes, with `decidedByUserId` = the decider (the
  caller's `systemuserid` from the on-behalf-of identity, never request input). The same conditional write sets `sprk_resolvedby`;
  `sprk_resolutionnotes` is never written. Automated callers pass null and write exactly what they wrote before.
- **Authorization before any write.** When the set is not empty, the caller must hold AppendTo on the invoice's core record and Write on
  each Signal's subject (the invoice, the lines), asked as the caller through the caller-rights probe. That is the commit route's own rule
  (`DecisionCallerRights`, moved verbatim out of `DecisionCommitService.CallerMayDecideAsync`). A missing right, or a question the probe
  cannot answer, is 403 `ebilling.blocked.not_authorized` with nothing written. An invoice with no single core record is 409
  `record_refused`. A decision with no Signal to close asks nothing beyond the route's own authorization.
- **Order:** read the set (clock first), authorize, preflight, **store** the set in `sprk_findingsjson` under `humanDecisionSignalSet`
  (`{decisionId, setReadAtUtc, signals[{id, policyCode}], subjects[{entity, id}]}`; 049's `resolvedSignalSet` is left alone), record
  (`ResolvedSignalIds` = the set), close, then the state change as before. The set is stored even when empty, so a Signal raised between a
  first attempt and its retry can never change the decision's content (the record's hash covers `ResolvedSignalIds`).
- **A stored set wins.** A retry finds the set stored for this decision id and closes exactly it, with its stored read time; it is never
  re-read or widened, so a Signal raised after the first read stays open. Closing again by the same record is
  `AlreadyClosedByThisRecord` (nothing twice).
- **A failed close warns, and (2026-10-29, F2, see below) the answer is then 503 unavailable.** One Warning per Signal (ids and codes only); the decision itself stands. A Signal that
  no longer exists has nothing to close. A repeat of a decided submission (`AlreadyDecided`) re-runs the close over the stored set with the
  stamp's record and decider, after the same rights check; anything it cannot do only warns, and the repeat's answer is unchanged.
  **2026-10-29: a failed close answers 503 `ebilling.blocked.unavailable`; the repeat completes it** (ontology-r1 review of PR #1911, F2;
  decision of the coordinator). `CloseSetAsync` returns how many closes failed (a timeout, or `ChangedSinceRead`) and both call sites used
  to drop it, so a failed close was answered Decided / AlreadyDecided and the Signal stayed Open with no record link. This service has no
  Incomplete outcome (`BlockedDecisionKind` is Decided, AlreadyDecided, Refused), so it reuses its existing retryable refusal
  `Unavailable()`; no new kind. (1) The close inside the decision (record, close, state change: the order is unchanged): every Signal is
  still attempted and the state change and notification still happen (the decision stands, as for a line decision), but a count above
  zero is answered `Unavailable()` instead of Decided. (2) A repeat of a decided submission (`RecloseAsync`, both call sites, before and
  under the lock) returns its failed count: above zero is `Unavailable()`, zero is AlreadyDecided as before. (3) A state-write race that
  ends AlreadyDecided is `Unavailable()` when this run's closes failed. The repeat finds the decided submission, takes the record id, the
  decider and the set from the WRITER-CREATED record (never the stamp), re-runs the close (`AlreadyClosedByThisRecord` for the ones
  already closed) and writes no second Decision Record; it answers AlreadyDecided only once every close is done. **A transient read fault
  on a repeat is the same (2026-10-29):** a fault while reading the writer-created record (the lookup), the stored Signals, the invoice,
  or the entity sets the rights check needs answers 503 `unavailable` at both reclose sites, because the Signals are still Open; a later
  repeat with the reads healthy completes the closes and answers AlreadyDecided. The line decisions (100, section 9) answer Incomplete at
  step `signals` for the same rule.
  **Known limit (2026-10-29): permanent cases stay AlreadyDecided with one warning, because a repeat cannot change them.**
  - No writer-created Decision Record: nothing trusted names the set or the record to close by (the stamp is user-writable, F1-1).
  - A record naming no decider (`sprk_confirmedby`): there is no person to close in (K3).
  - Untrusted subjects: a Signal's row is not the invoice or one of its lines, or its policy code differs from the row's (F1-1, F1-b).
  - The writer refuses to read the set (more than one record under the key, or a set it cannot read): it never picks one (ReviewInvalid).
  - The invoice is gone: there is nothing to authorize the close against.
  - A caller without the rights (or a probe that cannot answer, which is a no): the app-identity writer closes only for an authorized caller (FR-51).
- **Races.** Losing the race at the set store to a concurrent run of the same decision is the same `AlreadyDecided` no-op as losing it at
  the state write; any other change is `concurrent_change`.

Tests: `SubmissionOverrideServiceTests` region "D-122 / FR-51" (each verb closes the set as Acted naming the decider; the record lists the
ids and the set is stored before it; rights asked as the caller; missing AppendTo / Write / an unanswerable probe are 403 with nothing
written; no Signal asks nothing; a read fault is 503; a failed close (timeout, or ChangedSinceRead) is 503 unavailable with the decision
standing; LeftAsIs is Decided; the repeat completes the close with one record and answers AlreadyDecided, and is 503 again while a close still
fails, also under the lock and after a lost state-write race; a read fault on a repeat (the record lookup, the stored Signals, the invoice) is
503 at both reclose sites and a healthy repeat completes it with one record, while the writer refusing the stored set stays AlreadyDecided; a clean close is unchanged; a repeat re-closes the stored set; a retry after the state write
failed closes exactly the stored set and finds its record; the store race; no core record). Seeded mutation (authorization disabled in
`HumanDecisionSignalClose.AuthorizeAsync`): 12 tests failed, among them
`Decision_WithoutAppendToOnTheMatter_Is403_AndNothingIsWritten`.

### 9.1 ontology-r1 independent review, round 1 (2026-10-20)

- **F1-1 (security): nothing read back from `sprk_findingsjson` is trusted.** Core User holds Write Deep on `sprk_invoicesubmission`, the
  column has no field security and the decision id is computable, so a stored set, its subjects and the stamp are claims. Now:
  - the stored `subjects` are never read; each Signal of a stored set is read app-only (`StoredSignalSetCheck`) and its subject taken from
    the ROW (the subject lookup repeating the pair id, with the dedupe key agreeing). Every subject must be the invoice itself or one of
    ITS lines (one query by the invoice and the ids); otherwise 409 `ebilling.blocked.findings_unreadable` and nothing is written. The
    rights check runs on those row-derived subjects. A listed Signal that no longer exists is passed over (nothing to close or authorize).
  - `setReadAtUtc` is bounded: not after now + 1 minute (`StoredSignalSetCheck.FutureSkew`), and not before the submission's `createdon`
    (Dataverse-set when the BFF creates the submission, never updatable; processing starts after it). Out of bounds: 409
    `findings_unreadable`.
  - a repeat (`AlreadyDecided`) takes the record id, the decider (`sprk_confirmedby`) and the set from the WRITER-CREATED Decision Record
    (`FindBillingDecisionResolvedSetAsync`); the stamp is never read for it. No writer-created record: nothing is re-closed.
  - 049's `resolvedSignalSet` gets the same row-derived subject check and bounded read time on resume; untrusted is a permanent Refused
    (`ebilling.submission.findings_unreadable`), nothing recorded or closed.
- **K1:** a retry whose writer-created record exists uses that record's stored set (as 100 does), so a decision in flight across a deploy
  is not `decision_conflict` forever; the set fact is left out of the facts when the record stored none, so its hash still matches.
- **F1-2 ruling: "ontology-r1 2026-10-20: line subject → Write on the parent invoice (D-90)".** Details in 100 notes 9.1; the test
  doubles model the real roles (Read only on `sprk_billingevent`, Write on `sprk_invoice`).
- **"Verify with a real Core User on dev before 100/105 ship."**

Tests: `SubmissionOverrideServiceTests` region "F1-1": a planted set naming another invoice's Signal or its line's is refused (F3 c); the
invoice's own Signal is trusted from its row; out-of-bounds read times; an edited stamp is ignored; no writer-created record re-closes
nothing; a repeat whose caller lacks the rights closes nothing (F3 a); the set-store race lost to a Return is 409 (F3 b); K1. Plus
`Decision_WithAnOpenLineSignal_*` (F1-2) and `InvoiceSubmissionAfterStepsTests` `AutoRejected_APlanted*`,
`AutoRejected_AStoredSetOfItsOwnSignal_IsTrusted`.

Seeded mutations (each applied alone, the filtered suites run, the source restored; every one caught):

| Mutation | Caught by |
|---|---|
| 105 trusts the findings' set (no row check, no bounds) | 105 F1-1 (c) foreign Signal ×2; F1-1 (3) read time ×2; F1-1 own Signal trusted |
| repeat takes record id and decider from the stamp | 105 F1-1 (2) "never from an edited stamp" |
| no writer-created record → fall back to the findings' set and stamp | 105 F1-1 (2) "NO writer-created record re-closes nothing" |
| read-time bounds removed | 049 and 105 F1-1 (3) read time ×2 each |
| 049 resumes a stored set unchecked | 049 F1-1 foreign Signal; 049 read time ×2 |
| a line subject asks Write on the line (F1-2 reverted) | 27 tests in 100 and 105, incl. `Decision_WithAnOpenLineSignal_*` |
| `RecloseAsync`'s rights check deleted | 105 F3 (a) "a repeat whose caller lacks the rights closes nothing" |
| `AfterConflictAsync`'s verb match loosened | 105 F3 (b) "losing the set-store race to a RETURN is 409" |
| the writer-created record's set ignored on a retry | 105 K1 |
| one stored-set lookup per line | 100 K2 "ONE query" |
| the dedupe-key subject taken without the row's agreement | 100 K3 "skipped with a warning" |

### 9.2 ontology-r1 independent review, round 2 (2026-10-20)

- **F1-a (security), fixed: the submission's `sprk_invoice` is not trusted.** It is user-writable: a Core User with Write on submission S
  and only AppendTo on invoice B could point S at B, make the findings name B, and (with no open Signal on B, or a planted empty set)
  POST `/return`, so a Return Decision Record was written under B and B moved to Returned to Firm without Write on B. Now `DecideAsync`
  refuses any invoice other than `InvoiceSubmissionPersister.InvoiceIdFor(submissionId)` (the only id the persister writes; 049 already
  requires it) with 409 `ebilling.blocked.invoice_not_held`, before the lock and any read or write. A repeat (`RecloseAsync`) of such a
  submission returns at once: no record lookup, nothing closed.
- **F1-b (integrity), fixed: a planted policy code never reaches the record.** `StoredSignalSetCheck.SubjectsAsync` now compares each
  Signal's claimed policy code with its row's (the dedupe key's, `RowSubjectOf`); a difference is `subject_unreadable`, so 105 answers 409
  `findings_unreadable` and 049's resume is the permanent Refused `ebilling.submission.findings_unreadable`. Before, the claimed code flowed
  into the append-only record's `resolvedSignalSet` fact and into the close.
- **K1, done:** the lower read-time bound allows the same skew as the upper one (`setReadAtUtc < createdon - FutureSkew` is refused), since
  `createdon` is Dataverse's clock and the read time the BFF's; a BFF running behind Dataverse no longer refuses a 049 resume permanently.
- **K2, done:** one test runs the second page of `DecisionRecordWriter.FindBillingDecisionResolvedSetsAsync` (`MoreRecords` plus the paging
  cookie). The code was correct; the test pins it. `DecisionRecordWriter.cs` is unchanged.
- **K3, done:** a repeat whose writer-created record has signals but no `sprk_confirmedby` logs one Warning (submission, record, verb,
  count) and closes nothing.

Known limits judged K by ontology-r1 (to be filed as issues by the operator, except (4)):

- **(2)** A planted EMPTY set: the Signals stay open and the record names the real caller. Its harmful use (deciding another invoice) is
  gone with F1-a.
- **(3a)** A Signal deleted between the set's store and the record write leaves a 105 decision stuck at 409 `record_refused`: 105 has no
  shrink path like 049's.
- **(3b)** Anyone with Write on the submission can block its decision by planting a junk set (409 `findings_unreadable` until it is
  removed).
- **(4)** A record-derived set with no read time (`SetReadAtUtc` null) is always empty: no defect.

Tests: `SubmissionOverrideServiceTests` region "ontology-r1 round 2":
`Decision_OfASubmissionLinkingAnInvoiceItDidNotCreate_IsRefused_AndNothingIsWritten` (override, return; F1-a decide must-fire),
`Repeat_OfASubmissionLinkingAnInvoiceItDidNotCreate_ClosesNothing` (F1-a repeat must-fire),
`Override_WithAStoredSetClaimingAnotherPolicyCode_IsRefused` (F1-b; stored "EB-AGE", row EB-RATE),
`Override_WithAStoredReadTimeJustBeforeCreatedOn_IsTrustedWithinTheSkew` (K1: -30 s and -60 s trusted, -61 s refused),
`Repeat_WhoseRecordNamesNoDecider_ClosesNothing_AndWarns` (K3). `InvoiceSubmissionAfterStepsTests`
`AutoRejected_AStoredSetClaimingAnotherPolicyCode_IsRefused` (049 F1-b) and `AutoRejected_AStoredReadTimeJustBeforeCreatedOn_IsTrustedWithinTheSkew`
(049 K1). `DecisionRecordWriterTests.Billing` `Billing_StoredSets_ARecordOnTheSecondPage_IsFound_ThroughThePagingCookie` (K2). F1-a's
must-not-fire is every existing decision and repeat test (the world's invoice is `InvoiceIdFor(SubmissionId)`), e.g.
`Decision_ClosesEveryOpenSignalOnTheInvoiceAndItsLines_*` and `Decision_ARepeatAfterAFailedClose_*`. The two round-1 "read time before
createdon" cases moved from -1 to -2 minutes (-1 minute is now inside the skew).

Fail-before: with the round-1 sources the 10 new must-fire cases fail (the F1-a decide answers 200 and records under B; the F1-a repeat
closes B's Signal); the K2 test passes (it pins correct code).

Seeded mutations (each applied alone, the filtered suites run, the source restored; every one caught):

| Mutation | Caught by |
|---|---|
| `DecideAsync`'s `InvoiceIdFor` check removed | F1-a decide ×2 (override, return) |
| `RecloseAsync`'s `InvoiceIdFor` check removed | F1-a repeat |
| `SubjectsAsync`'s policy-code comparison removed | 105 F1-b; 049 F1-b |
