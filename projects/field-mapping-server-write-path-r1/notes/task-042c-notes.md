# Task 042c notes: a Signal or Decision Record asks its core record (#2561, DI-019)

Rigor FULL (tags security, bff-api, ownership, testing). Branch `task/fm-042c`, cut from `5f443eaae0` (work-branch tip; contains 042b's merge `49ba684343`). POML steps 0-4 here; step 5 (two adversarial passes, UAC-r2's diff review) and step 6 (close DI-019 / #2561, TASK-INDEX) are the main session's.

## AC2 amendment (coordinator + UAC-r2, 2026-11-04)

AC2 as written ("a Secure core ... is owned at create by the core's Secure owner") contradicts `RecordCreationService.OwnerDecided`, which refuses any Secure owner for a table that is not a reparentable child (Signal and Decision Record are not: design.md section 15.4 row 8). Meeting it literally would change an existing refusal (NFR-07 trigger), so it was raised before implementing. Ruling: `OwnerDecided` is not touched, and AC2 reads:

- **(a)** Secure core, the caller holding AppendTo on it: the existing row-8 refusal (409 `NoOwnerSource`, chat `ValidationFailed`), code, status and words byte-identical to row 8; nothing written.
- **(b)** Ordinary core in BU Y, subject in BU X: owned by Y's team, equal to `ResolveOwnerAsync(ForExistingRowAsync(written row))`.
- **Ordering:** step 5's core AppendTo refuses before step 7 can produce the 409, so a caller without AppendTo on a Secure core gets the uniform AppendTo refusal, never the 409 (which would say the core is Secure).

UAC-r2's four conditions (OK'd 2026-11-04), each tested (see the AC table): (1) ordering, one AC3 theory over a Secure core without AppendTo, a nonexistent id, an unknown type and an ordinary core without AppendTo, plus a faulted catalog read = the existing 503; (2) the 409 only with AppendTo on the Secure core, both sides tested; (3) must-not-fire for no core pair, and the writers keep their own question; (4) ordinary core Y / subject X through 042b's parity harness.

**Release note:** a Signal or Decision Record created through chat whose core record is Secure is now refused with 409 NoOwnerSource (design §15.4 row 8), where before it was created team-owned and left for the reconciler. Also: one that names a core record the caller may not append to is now refused (the uniform not-found), and one whose core pair cannot be told (a non-guid id, an id with no type, a type naming no core record table) is refused 400.

## What changed in src

`CallerWriteAuthorization.PairTargetAsync`: two optional parameters, `idColumn` / `typeColumn`, defaulting to the regarding pair's (`SecureRootInheritance.PairIdColumn` / `PairTypeColumn`). They name the pair in the refusal words, in `RefusedColumn` and in the returned lookup's column. Every existing caller passes neither, so its words, codes and probes are byte-identical (modification of existing surface; no new member).

`RecordCreationService.cs`:
- **Step 5** (caller principal): after the existing check (both the plain and the merged branch), `CallerMayFileUnderCoreAsync` (private, new) for `RecordOwnershipContext.CorePairTables` only: reads the pair (`CorePairOf`: a non-string id is invalid, never absent), calls `PairTargetAsync` with the core columns and NO lookups (so an id with no type is Invalid, never matched by agreement), requires the named table to be in `CoreAncestorResolver.CoreRecordEntities` (the reconciler's own rule in `CoreTableOfRecordTypeAsync`; otherwise Invalid), then `CheckCallerMayAppendToAsync` on the core record. A refusal goes through the existing `CallerCheckRefused`. The trace note gains "; AppendTo on the core record" only when a core was asked (unchanged otherwise).
- **Step 7**: `DecideOwnerAsync` takes the step-5 answer (`coreRecord`) and `askedAsCaller`; `ResolveOwnerOverAsync` and `OwnerQuestionOf` take an optional `coreRecord` (Repair passes none). `OwnerQuestionOf` builds `ForRow` as before, then puts the core first: `ForParents([core, ..question.Parents])` (distinct, ownership parents only, as `ForExistingRowAsync` uses `ForParents`), keeping the builder's Secure-only parents, child table and caller ids. No second catalog read for a caller.
- **System principal** (no step 5): `CoreRecordOfSystemCreateAsync` (private, new) reads the catalog app-only by the reconciler's own read (`CoreAncestorResolver.CoreTableOfRecordTypeAsync`) inside step 7's try (a fault is 503 `owner_unavailable`); a non-guid id, an id with no type or a non-core type is 409 `ParentUndetermined` with the same word template as an undetermined regarding pair. No production system creator of these tables exists today (DI-007), so this keeps the create's owner equal to the reconciler's on that path too rather than leaving the DI-019 gap open there.

Not changed: `OwnerDecided`, `RecordOwnershipResolver`, `SecureChildReconciler`, `SignalWriter`, `DecisionRecordWriter`, `DataverseCreateRecordHandler` (Signal and Decision Record are team-owned, so chat takes the Owned path straight into the service; `RefuseSecureFilingAsync` is not on that path), route filters (ADR-008 path A: the check is in `CallerWriteAuthorization` / the service).

## Placement justification (CLAUDE.md section 10, bff-extensions.md)

A modification inside `RecordCreationService` and its use of `CallerWriteAuthorization`, in the BFF, where the synchronous write path that owns I-6 lives. No new endpoint, service, DI registration, package or background work. ADR-008 path A (this project's approved tension; cite in the PR).

## Component justification (CLAUDE.md section 11)

The new members are private helpers inside an existing file (`CorePairOf`, `CallerMayFileUnderCoreAsync`, `CoreRecordOfSystemCreateAsync`, the `CorePairWrite` record struct, two column constants) and two optional parameters on existing internal methods.
1. **Existing:** `OwnedChildWrite.PairOf` reads the regarding pair from an item; `CallerWriteAuthorization.PairTargetAsync` resolves a pair's type as the caller; `CoreAncestorResolver.CoreTableOfRecordTypeAsync` is the reconciler's app-only catalog read.
2. **Extension:** all three are reused (PairTargetAsync extended with the column names, CoreTableOfRecordTypeAsync called as is). `PairOf` is hard-wired to the regarding columns and returns the regarding pair's shape; `CorePairOf` is its eight-line twin for the core columns rather than a parameterisation of a method four other files call.
3. **Cost of doing nothing:** a chat-created Signal or Decision Record naming a core record the caller may not append to is created and filed under it (no AppendTo check), and one with a Secure core is created ordinary-team owned until the reconciler moves it (DI-019).

## AC table

| AC | Evidence (tests) |
|---|---|
| AC1 probes and order | `Step5_AsksTheCatalogThenTheCoresAppendTo_BeforeAnyAppOnlyReadOfTheCore` (both tables): the caller's GETs include `sprk_recordtype_refs(type)?$select=sprk_recordlogicalname` before `RetrievePrincipalAccess` on `sprk_matters(core)`; the resolver's record queries snapshotted at that check name no core id; after the create they do (the owner question read it after). |
| AC2 (a) | `ACallersCreateWithASecureCore_GetsTheRow8Refusal_ByteIdentical_AndWritesNothing` (parity, both tables): Kind OwnerUnresolved, code `NoOwnerSource`, `IsSecureOwner`, Detail and ProblemDetails equal to the row-8 create (invoice under the Secure matter) in the same world; nothing created; the reconciler would own the row Secure. Chat twin `Chat_ACreateWithASecureCore_TheCallerHoldingAppendTo_GetsTheRow8Refusal`: code `ValidationFailed` and message equal to a Signal/DR filed under the Secure matter. (UAC-r2 condition 2, positive side.) |
| AC2 (b) | `ACallersCreateWithAnOrdinaryCoreInY_FiledUnderAnInvoiceInX_IsOwnedByYsTeam_AsTheReconcilerOwnsTheWrittenRow` (042b's `CreateParityWorld`, caller principal, both tables): owner CustomerChildTeam (Y) = the reconciler's for the written row (which holds the core pair). Chat twin `Chat_ACreateWithAnOrdinaryCoreInY_IsOwnedByYsTeam`. (UAC-r2 condition 4.) |
| AC3 | `ACallerWhoMayNotFileUnderTheCore_GetsTheUniformNotFound_WhateverTheCore` (one theory, 12 cases): Secure core without AppendTo, ordinary core without AppendTo, an existing core the caller cannot read, a core that does not exist, an unknown record type (no catalog row), and the baseline unappendable lookup; each asserts service Kind NotFoundOrDenied, ProblemDetails 404 / `record_creation.not_found` / the uniform words, chat `DATAVERSE_ACCESS_DENIED` with the same uniform words, nothing created. (UAC-r2 conditions 1 and 2, negative side.) |
| UAC-r2 cond. 1 fault | `AFaultedCatalogRead_IsTheExisting503` (both tables): a 500 on the core pair's catalog read is Kind Upstream, status 503; chat carries the failure's own code. |
| AC4 | `ACorePairThatCannotBeTold_IsRefusedFailClosed` (12 cases): type naming a non-core table (sprk_document), type naming no table, catalog row the caller cannot read (the type lookup's existing AppendTo refusal: uniform not-found), non-guid id, id with no type, non-string id; service 400 `record_creation.invalid_payload` (words name `sprk_corerecord*`, never the regarding pair) or the uniform not-found; chat `ValidationFailed` / `DATAVERSE_ACCESS_DENIED`; nothing created. System twin: `ASystemCreateWhoseCorePairCannotBeTold_IsRefused_ParentUndetermined` (6 cases, 409). |
| AC5 | Must not fire: `ACreateWithNoCorePair_KeepsTodaysProbesAndOwner` (no catalog read, one RetrievePrincipalAccess on the row's own lookup, owner ChildTeam), `ACreateWithACoreTypeButNoId_KeepsTodaysOwner`, `ACallersCreateWithAnAgreeingCore_GetsTheSameOwnerAsWithNoCore` (core = the invoice's own matter, as SignalWriter derives it: same owner as no core, = the reconciler). Writers keep their own question (UAC-r2 condition 3): `ExistingRowOwnershipParityTests.ASignal_IsAskedExactlyAsSignalWriterAsks` and `.ADecisionRecord_IsAskedExactlyAsDecisionRecordWriterAsks` green unchanged (neither writer is in the diff). Every other table: the existing suites green with unchanged assertions (see Verification). |
| AC6 | The chat tests above (`Chat_*`, and the AC3/AC4/fault theories, which run each case through the chat handler AND the service). |
| AC7 | Build, suites, ArchTests, publish delta, CVE: see Verification. PR/UAC-r2 verdict line and DI-019 / #2561 closure: main session. |

Also (system principal, beyond AC1-AC6 because the service change covers it): `ASystemCreateWithAnOrdinaryCoreInY_IsOwnedAsTheReconcilerOwnsTheWrittenRow`.

## Failing-first evidence

All 50 new test cases (`DisplayName~042c`) were run against the base service (with the inert `PairTargetAsync` parameterisation only): **38 failed, 12 passed**. The failures were for the expected reasons: the create succeeded where a refusal was expected (`Expected viaService.Refusal not to be <null>`, 20 + 6 + 2 + 2), the owner was X's team (ChildTeam) instead of Y's (6), and no catalog read was made (2). The 12 that passed are the must-not-fire cases (6) and the cases an existing check already refuses (unknown record type and the baseline unappendable lookup in AC3, the unreadable catalog row in AC4: 6). After the change: **50 / 50 pass**.

## Seeding proof

`CallerMayFileUnderCoreAsync`'s AppendTo replaced by `Outcome.Allowed` (core dropped from the AppendTo set), AC3 theory run: **8 failed / 4 passed** (the 4 are the unknown-type and baseline cases, refused elsewhere). The Secure-core cases failed with `Kind ... NotFoundOrDenied ... but found OwnerUnresolved`: without the check the 409 would reveal a Secure core. Reverted; AC3 passes again.

## Existing-test edits

No existing assertion changed. Harness additions only:
1. `SecureChildOwnershipAiToolTests.ScriptedUserClient`: entity set `sprk_decisionrecord`; `sprk_signal` lookups + `sprk_corerecordtype`, `sprk_regardinginvoice`, `sprk_regardingmatter`; `sprk_decisionrecord` lookups (`sprk_matter`, `sprk_corerecordtype`, `sprk_regardinginvoice`, `sprk_regardingmatter`). Reason: the core-pair cases map and bind these columns as the caller.
2. `WriterBuilderOwnershipParityTests.CreateParityWorld`: `MatterInX`, the catalog rows (`MatterRecordType`, `DocumentRecordType`), the written row carries `sprk_corerecordid`, `CreateAsAsync` takes an optional core pair, a caller principal that holds Create/Append on the two tables and reads the catalog, and `RowAsItWouldBeWritten(table, core, ...)`. The 042b fix-round-2 guard (every item lookup is on the written row) now also covers `sprk_corerecordtype`.

## Found in passing (not fixed; proposed DI entries for the main session)

- **DI-P3 (pre-existing): a faulted catalog read at the TYPE LOOKUP's AppendTo is a 404, not a 503.** `CallerWriteAuthorization.CallerMayAppendToOrganizationOwnedAsync` treats any failed row read as "no AppendTo" (`return row.IsSuccess`), so a 5xx on `sprk_recordtype_refs(id)?$select=sprk_recordtype_refid` gives the uniform not-found. This is the existing rule for every organization-owned lookup (the regarding pair's type too), and it runs BEFORE the core pair's own catalog read (which is the 503 tested above). Changing it would change existing refusals (NFR-07); fail-closed either way. UAC-r2 condition 1 says "never a different 4xx": this path is the exception, pre-existing and shared. Class: K-candidate.
- **DI-P4: Repair does not ask a Signal's or Decision Record's core record.** `RecordCreationService.Repair.cs` `RepairOwnerAsync` -> `ResolveOwnerOverAsync` passes no core, so a Repair of a cored Signal/DR would re-own over its lookups only. Repair has no production caller yet (052, DI-008). Class: K-candidate until Repair is wired.

## Verification

(filled in below after the runs)
