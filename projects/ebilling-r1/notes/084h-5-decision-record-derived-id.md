# 084h-5: a derived primary id for keyed Decision Records

> **Task:** `tasks/084h-5-decision-record-derived-id.poml` (FULL) · **Decision:** EB-D-247 (owner, question (d)) · **Design:** ontology-r1 option 1, rules 1-6 · **Base:** `task/ebilling-084h-5` from 4c912fd7b6 · **Status:** built and tested; awaiting ontology-r1's full review. Merge waits on the owner and uac-r2 accepting the known limit (derivable record id, see uac-r2 thread).

## What was built

All production changes are in `src/server/api/Sprk.Bff.Api/Services/Signals/DecisionRecordWriter.cs`. No schema change, no package change, no DI change, no new endpoint and no new refusal reason.

| Item | What it is |
|---|---|
| `DecisionRecordIdOf(Guid key)` (internal static) | THE derivation: key Guid to record id. The only place a record id is derived. |
| `KeyedEntry` (internal enum) | `Gate`, `ConditionCleared`, `Billing`, `RecordProcedure`. |
| `DerivedRecordIdEntriesByDefault` (internal static frozen set) | The per-entry switch: one line per entry: condition-cleared and Procedure on; the gate and billing off (EB-D-251, option B). |
| `DerivedRecordIdEntries` (internal, `init`) | The set this writer instance uses; the default in production. Test seam only. |
| `Plan.RecordId` (`Guid?`) | Carries the id from the entry point to the one private `CreateAsync`. |
| `CreateAsync` | Sets `entity.Id` when `plan.RecordId` is set; maps a DuplicateRecord fault to a private `DerivedRecordIdTakenException`. |
| `IsDuplicateRecord` + `DuplicateRecordFault` | The fault classifier (`0x80040237`). |
| Each keyed entry point | Passes `RecordIdFor(entry, key)` in its plan, and catches `DerivedRecordIdTakenException` around its one create call. |

## The derivation (rule 3)

`DecisionRecordIdOf(key)` = the first 16 bytes of SHA-256 over UTF-8 `"decision-record-id:" + key:D`. This is the scheme every key in the writer already uses (`GateDecisionKey.IdOf`, `BillingDecisionKey.IdOf` and the rest), under its own prefix. It is never the raw key. The input is the entry's key Guid alone, never the action code, so one key is one record whatever the code. A second code under a key keeps its existing other-code refusal (tested).

**Choices for the reviewer:**

- **One function.** Every entry point gets its id from `DecisionRecordIdOf` through `RecordIdFor(entry, key)`, and the tests use the same function (no test recomputes the hash and none pins a golden id). A variant that mixes in a per-environment value is a change to this one function body and nothing else.
- **Per-entry switch.** `DerivedRecordIdEntriesByDefault` has one line per entry. Removing a line (as for the gate and billing, EB-D-251) returns that entry to random ids (today's behaviour) with no other edit. Tested both ways for all four entries.
- **Key rotation is not blocked.** Nothing finds a record by its derived id. The `sprk_steps` lookup stays first (rule 5), and a refused create re-runs that same lookup, so a changed derivation (or switch) still finds every older row.

## Each entry point covered (rule 1)

| Entry point | Key | Switch | Refused-create re-read | "Not found" refusal |
|---|---|---|---|---|
| `WriteGateDecisionAsync` | `GateDecisionKey.IdOf` | `KeyedEntry.Gate` | `FindGateRecordAsync` | `GateRecordLookupFailed` |
| `WriteConditionClearedAsync` | `ConditionClearedRecordKey.IdOf` | `KeyedEntry.ConditionCleared` | `FindConditionClearedRecordAsync` | `ConditionClearedLookupFailed` |
| `WriteBillingDecisionAsync` (internal overload) | `BillingDecisionKey.IdOf` | `KeyedEntry.Billing` | `FindBillingRecordsAsync` + `AlreadyRecordedOrRefuse` | `BillingRecordLookupFailed` |
| `WriteRecordProcedureDecisionAsync` (internal overload) | `RecordProcedureDecisionKey.IdOf` | `KeyedEntry.RecordProcedure` | `FindRecordProcedureRecordsAsync` + `AlreadyRecordedOrRefuse` | `RecordProcedureLookupFailed` |

The id reaches the one private `CreateAsync` through `Plan.RecordId`. There is no second create site: `DecisionRecordContextImmutabilityTests` passes unchanged.

**Not the review path (rule 2).** `WriteAsync` (the commit route) builds its plan with `Validate`, which never sets `RecordId`, so the review record is created with no id set (Dataverse picks it). Its own lock, lookup and hash check are untouched. Pinned by `DerivedId_MustNotFire_TheReviewPath_CreatesWithNoIdSet`.

## The duplicate-fault mapping (rule 4)

- **Fault code: DuplicateRecord, `0x80040237` (-2147220937, "Cannot insert duplicate key").** The Dataverse SDK assemblies in the repo (`Microsoft.PowerPlatform.Dataverse.Client` 1.1.32 and 1.2.26: `Microsoft.Xrm.Sdk`, `Microsoft.Crm.Sdk.Proxy`) carry no named constant for it. The code is the one the repo already matches: `TrackingNumberAllocator.PrimaryKeyDuplicateFault` (measured in dev 2026-10-16 as the fault for a create whose primary id is taken) and `ProcedureAssignmentService.DuplicatePrimaryKey`.
- **Classification:** `IsDuplicateRecord` walks the exception chain. It checks the typed `FaultException<OrganizationServiceFault>` with `Detail.ErrorCode == 0x80040237` first, then the hex code in a message, as `IsDataverseAccessDenied` classifies. It matches the code only, never words.
- **Where:** in `CreateAsync`, a filter catch that runs before the existing catch and only when `plan.RecordId` is set. It throws a private `DerivedRecordIdTakenException`. Every keyed entry point catches that around its one create call, so it never leaves the writer. The writer's only public exception stays `DecisionRecordRefusedException`.
- **The re-read ("as the writer"):** the entry re-runs its own existing lookup. That is the shared sysadmin reader, narrowed to rows the writer identity created (`createdby`) with this key in `sprk_steps`, the same query the lookup-first path makes. The lookup's rules are then applied unchanged:
  - **gate and condition-cleared:** found means AlreadyRecorded (the same result object and log line as the lookup-first path, now one local function per entry).
  - **billing and Procedure:** `AlreadyRecordedOrRefuse` with the same messages and the hash this attempt already computed. Same code and hash give AlreadyRecorded. Another code gives the other-code refusal. Other content gives the other-content refusal. An unreadable hash gives the unreadable refusal.
- **Choice for the reviewer: the id is taken, but the lookup finds no record of this decision.** The lookup's rules have no answer for this case, so the writer refuses with that entry's existing lookup-failed reason (table above). Nothing is written, the reason is retryable in every classifier (`RecordProcedureDecisionJob.RefusalClasses`; the gate job retries a lookup failure), the Dataverse fault is kept as the inner exception, and the message says the id is taken. It is never CreateFailed and never AlreadyRecorded. No new refusal reason was added, so the repair job's exhaustive classification table is unchanged.
- **Any other fault** is unchanged: CreateFailed, or DataverseAccessDenied for a privilege fault. A DuplicateRecord fault on the review path, or on an entry that is switched off, stays CreateFailed as today (both tested).

## Parity (rule 6)

The duplicate path is the lookup-found path reached by a different route. It does not redo the secure-owner read-back (`VerifySecureOwnerAsync`) and reports no `SecureOwnerTeamId`, exactly as the lookup-found path does not. The attempt that created the record ran those steps as before. Pinned by `DerivedId_TheDuplicatePath_LikeTheLookupFoundPath_DoesNoSecureOwnerReadBack_AndReportsNoSecureTeam`. The participant share is the one exception: see review F1 below (the duplicate path shares only when the record it answers is this call's own).

## Tests

**Fakes.** The writer harness (`DecisionRecordWriterTests.Harness`, `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.cs`) now creates the way Dataverse does:
- a create naming a taken primary id is refused with the typed DuplicateRecord fault and stores nothing;
- a create naming a free id stores the row under that id and returns it;
- a create with no id returns `NewId`, as before.

It is thread-safe (creates and lookups under one lock) and has two new hooks: `HoldKeyedLookupsUntil(n)`, a barrier that makes `n` concurrent attempts all read before any of them creates, and `BeforeCreate`, which lets a concurrent row land just before this create. The billing lookup override (`WithBillingLookups`) uses the same lock and barrier. Each of the four harness helpers takes the switch as an optional parameter.

**Amended existing tests (5, expected-value only).** Three gate D-169 tests and the Procedure W2 theory asserted the old random `h.NewId` as the record id of a keyed create. They now assert the derived id through `DecisionRecordIdOf` (`GateRecordId()` for the gate). No assertion was weakened. The gate job's, the repair job's and the billing writers' own test files are unchanged.

**New tests (32):** `tests/integration/data-mutation/Signals/DecisionRecordWriterTests.DerivedRecordId.cs`
- Two concurrent attempts that both pass the lookup give one create, one AlreadyRecorded, one refused create and the record under the derived id. Theory over all four entries.
- Switch off: the same race gives two creates with no id set (today's behaviour). Theory over all four entries. Also: the default set is exactly condition-cleared and Procedure (EB-D-251, option B).
- Derivation: deterministic, not the raw key, one id per key. Two codes under one key race for one id, and the loser gets the other-code refusal.
- Duplicate mapping:
  - billing: same code and hash give AlreadyRecorded; other content, another code and an unreadable hash each give their existing refusal;
  - Procedure: other content, as a real concurrent pair;
  - gate: AlreadyRecorded;
  - id taken but not found by the lookup: the entry's lookup reason, for all four entries.
- Parity: no secure-owner read-back.
- Must-not-fire: the review path sets no id; a DuplicateRecord on the review path stays CreateFailed; any other fault stays CreateFailed on every keyed entry; a privilege fault stays DataverseAccessDenied; a DuplicateRecord on a switched-off entry stays CreateFailed; an untyped wrapper carrying the code is recognised.

**Fail-before (mutation; the source was restored from a copy after each):**

| Mutant | Result |
|---|---|
| Seeding proof: `entity.Id = derivedId` removed | 18 of the 32 fail. All four concurrent tests see two creates ("Expected h.Created to contain a single item"). The switched-off tests, which expect two creates, still pass. |
| The DuplicateRecord mapping disabled (filter `&& false`) | 18 of the 32 fail (the refused create surfaces as CreateFailed or a lost answer). |
| The original writer (base 4c912fd7b6) | The new tests do not compile against it (no `DecisionRecordIdOf` or `KeyedEntry`). |

**Verification on the change:**

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror --no-incremental` | 0 warnings, 0 errors |
| `Sprk.Bff.Api.Tests`, filter `DecisionRecord`, `DecisionCommit`, `RecordProcedureDecision`, `GateDecision`, `SignalGate`, `SubmissionOverride`, `InvoiceRejectReturn`, `InvoiceAdjustment` | 2001 passed, 1 skipped (pre-existing), 0 failed. The `integration/*` folders compile into this project; no other test project holds these names. |
| `DecisionRecordWriterTests` alone, three runs | 451/451 each time, so the concurrency tests are stable |
| Other suites that construct the writer (Procedure assignment, invoice line, approval, firm, submission, spend, seam, ownership, Signals writer and suppression, finance contracts: 26 filters) | 3427/3427 |
| `Spaarke.ArchTests`, filter `Decision` or `Signal` | 71/71 |
| Golden hashes | unchanged (`BillingDecisionHash` and `RecordProcedureDecisionHash` untouched; their golden tests are in the filtered run) |

## Review round 1 (ontology-r1): fixed

**F1: a gate record lost its participant share on one duplicate path.** If the SDK `ServiceClient` retries a Create internally after the first send committed, the retry is refused with DuplicateRecord, the re-run lookup finds this very call's record and the call answered AlreadyRecorded, so the share the create path makes after the create never ran, and the communication's sharees could not read a principal-owned gate record.
- *Fix.* The share step is one private method, `ShareParticipantsAsync`, called by the create path as before. When `CreateAsync` maps a DuplicateRecord fault it now carries, on the private `DerivedRecordIdTakenException`, the derived id and a closure over the share inputs it already computed (principal owner, subject, gate flag, secure team, owning team). Each entry point's duplicate catch calls `ShareIfThisCallsRecordAsync(taken, storedId)` before answering AlreadyRecorded: it runs the share only when the stored record's id equals the derived id of this call. Any other stored id (an older random-id row, another attempt's row) runs nothing.
- *Unchanged.* The secure-owner read-back stays skipped on this path and `SecureOwnerTeamId` stays null. Both share calls never throw, and a re-grant is harmless, so the concurrent-attempt case (the other attempt also shared) costs one idempotent grant.
- *Tests* (`DecisionRecordWriterTests.DerivedRecordId.cs`): `DerivedId_Gate_TheDuplicatePath_AnsweringThisCallsOwnRecord_RunsTheParticipantShareExactlyOnce` (must-fire: stored id equals the derived id, one grant, no read-back); `DerivedId_MustNotFire_Gate_TheDuplicatePath_AnsweringAnotherRecordsId_RunsNoShare` (stored id is another id, no grant).

**F2: a timeout surfacing as a cancellation escaped `CreateAsync`.** Both catch filters used `ex is not OperationCanceledException`, so an HTTP timeout raised as `TaskCanceledException` with an uncancelled token escaped the writer instead of becoming CreateFailed.
- *Fix.* Both filters in `CreateAsync` (the DuplicateRecord filter, first, and the general one) now use `ex is not OperationCanceledException || !ct.IsCancellationRequested`, the clause `FindKeyedRecordsAsync` already uses. A cancellation of the caller's own token still propagates.
- *Tests:* `DerivedId_ATimeoutCancellation_WithAnUncancelledToken_OnAKeyedCreate_IsCreateFailed` (theory, all four keyed entries), `DerivedId_ATimeoutCancellation_WithAnUncancelledToken_OnTheReviewPath_IsCreateFailed`; must-not-fire `DerivedId_MustNotFire_ACancelledToken_OnAKeyedCreate_StillPropagatesTheCancellation` and `DerivedId_MustNotFire_ACancelledToken_OnTheReviewPath_StillPropagatesTheCancellation`.
- *Existing tests amended (expected-value only).* `Cancellation_IsNotSwallowedOrWrapped` now cancels its token (the same exception with a live token is a failed create). Three job tests (`GateDecisionRecordJobTests.AnUnclassifiedException_...`, `RecordProcedureDecisionJobTests.J4_AnExceptionThatIsNotARefusal_...` and `J5_...`) used a create-time `TaskCanceledException` as their example of "an exception that is not a refusal"; that example now comes from a source the writer does not wrap (see round 2), so what they pin (the jobs' handling of a non-refusal exception) is unchanged.

**Review round 2: the six other catch filters with the same defect, fixed.** The gate lookup, the condition-cleared lookup, the condition-cleared subject-row read, `AskOwnerAsync`, the gate owner read and `VerifySecureOwnerAsync` used `ex is not OperationCanceledException` alone, so a timeout surfacing as `TaskCanceledException` with a live token escaped them. Each now uses `ex is not OperationCanceledException || !ct.IsCancellationRequested`, and keeps its existing answer for that timeout: the lookup-failed reason (gate, condition-cleared), the unknown-person record (subject-row read), `OwnerResolutionFailed` (gate owner read, `AskOwnerAsync`), and the logged and metered verify failure with the record id still returned (`VerifySecureOwnerAsync`). After this, every catch in the writer that wraps a Dataverse fault uses the one clause.
- *Tests* (`DecisionRecordWriterTests.TimeoutCancellation.cs`, two theories over one case per catch): `Timeout_ATaskCanceledException_WithAnUncancelledToken_IsTheCatchesExistingFaultAnswer_NotAnEscape` (6 cases, each asserts its catch's answer) and `Timeout_MustNotFire_ACancelledCallerToken_StillPropagatesTheCancellation` (6 cases: the token is cancelled inside the faulting call and the cancellation propagates).
- *Test support.* The test harness gained a per-query fault hook (`QueryFault`), and the shared test `CapturingLogger` an optional `Intercept` that may throw. Round 1 had moved the three job tests' "exception that is not a refusal" example to the ownership resolver; this round wraps that too, so they now take it from the writer's own log call after a successful create (`FaultTheWritersLogAfterTheCreate`), which nothing in the writer wraps. What they pin (the jobs' handling of a non-refusal exception) is unchanged; the gate job test now asserts the job's metric is among those metered rather than the only one, because the create had landed and the share step also ran.

## Owner decision EB-D-251 (option B)

Owner option B (EB-D-251, "For 084h-5 yes option B"; this replaces the earlier gate-only option 2): gate and billing Decision Records keep random ids. `KeyedEntry.Gate` and `KeyedEntry.Billing` are out of `DerivedRecordIdEntriesByDefault`, which is now exactly {ConditionCleared, RecordProcedure}; those two keep the derived id. Gate and billing duplicates stay guarded by the existing processing locks and the `sprk_steps` lookup (the billing duplicate path still answers AlreadyRecorded through them), as on master before 084h-5. The enum values, the gate and billing code paths and the F1 share-on-duplicate code stay in place: they are still correct if a switch is ever turned on, and harmless while it is off. Tests that need the derived id of the gate or billing opt in explicitly through the `DerivedRecordIdEntries` test seam (`DerivedIdOptIn`, all four entries on). `DerivedId_TheDefaultSwitch_IsExactlyConditionClearedAndProcedure` pins the default set; `DerivedId_MustNotFire_WithTheDefaultSet_AGateOrBillingCreateSetsNoId_AndTwoConcurrentAttemptsBehaveAsBefore` (gate and billing) pins that a default create names no id and two concurrent attempts both create; `DerivedId_TheTwoEntriesThatStayOn_KeepTheDerivedIdUnderTheDefaultSet` keeps the seeding proof for condition-cleared and Procedure under the production default. Known limit: derivable record id, see uac-r2 thread. A per-environment keyed derivation (option A) stays possible later, per entry, if the race shows up in practice.

Which decision ids each entry uses: derivable record id, see uac-r2 thread.

## Known limits

- **K1. Id taken but lookup empty: the entry's lookup-failed reason, retryable; derivable record id, see uac-r2 thread.**
- **Derivable record id, see uac-r2 thread.**
- **Rows written before this change** have random ids. The `sprk_steps` lookup stays first (rule 5), so they are still found. A concurrent pair that already left two rows stays two rows; the hash-aware `AlreadyRecordedOrRefuse` still answers them as before.
- **A switched-off entry** races as before (by design of the switch).
- **The processing locks and the lookup are unchanged.** The derived id closes only the concurrent window, as rule 5 says.
- **Test fake:** a create with no id still returns the harness's fixed `NewId`, as before; only a create with an id returns that id.
- **Not measured here:** the BFF publish-size delta (§10). The change adds no package, DI registration or endpoint.
