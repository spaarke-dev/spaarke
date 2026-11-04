# Task 042c notes: a Signal or Decision Record asks its core record (#2561, DI-019)

**Fix round 4 (owner decision D-204, 2026-11-04):** Signal, Decision Record and Budget Revision rows are created only by their writers, and the general create refuses those tables. The create-path work in the sections up to "Fix round 3" is removed; see "Fix round 4" for what is in place now.

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
- **Step 7**: `DecideOwnerAsync` takes the step-5 answer (`coreRecord`) and `askedAsCaller`; `ResolveOwnerOverAsync` and `OwnerQuestionOf` take an optional `coreRecord` (Repair passes none). `OwnerQuestionOf` passes the core to the one builder, `RecordOwnershipContext.ForRow(..., coreRecord)` (fix round 1, item A; the first version prepended it through `ForParents` plus a `with`, which the R3b-2 ArchTest refused). No second catalog read for a caller.
- **System principal** (no step 5): `CoreRecordOfSystemCreateAsync` (private, new) reads the catalog app-only by the reconciler's own read (`CoreAncestorResolver.CoreTableOfRecordTypeAsync`) inside step 7's try (a fault is 503 `owner_unavailable`); a non-guid id, an id with no type, or a catalog row naming a table that is no core record table is 409 `ParentUndetermined` with the same word template as an undetermined regarding pair. A type the catalog does not hold at all is NOT 409: Dataverse's Retrieve throws for a missing row, so it is the owner fault, 503 `owner_unavailable`, as the reconciler's `ForExistingRowAsync` lets the same read fault propagate (corrected in fix round 1, item G; `ASystemCreateWhoseCoreTypeTheCatalogDoesNotHold_IsTheOwnerFault503`). No production system creator of these tables exists today (DI-007), so this keeps the create's owner equal to the reconciler's on that path too rather than leaving the DI-019 gap open there.

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
| AC1 probes and order | `Step5_AsksTheCatalogThenTheCoresAppendTo_BeforeAnyAppOnlyReadOfTheCore` (both tables): the caller's GETs include `sprk_recordtype_refs(type)?$select=sprk_recordlogicalname` before `RetrievePrincipalAccess` on `sprk_matters(core)`; every app-only read snapshotted at that check names no core id: the resolver's record queries, its single-row reads (`RetrieveAsync`, fix round 1 item F) and the service's own app-only reads (`IGenericEntityService` `RetrieveAsync` / `RetrieveMultipleAsync`); after the create the resolver's queries do (the owner question read it after). |
| AC2 (a) | `ACallersCreateWithASecureCore_GetsTheRow8Refusal_ByteIdentical_AndWritesNothing` (parity, both tables): Kind OwnerUnresolved, code `NoOwnerSource`, `IsSecureOwner`, Detail and ProblemDetails equal to the row-8 create (invoice under the Secure matter) in the same world; nothing created; the reconciler would own the row Secure. Chat twin `Chat_ACreateWithASecureCore_TheCallerHoldingAppendTo_GetsTheRow8Refusal`: code `ValidationFailed` and message equal to a Signal/DR filed under the Secure matter. (UAC-r2 condition 2, positive side.) |
| AC2 (b) | `ACallersCreateWithAnOrdinaryCoreInY_FiledUnderAnInvoiceInX_IsOwnedByYsTeam_AsTheReconcilerOwnsTheWrittenRow` (042b's `CreateParityWorld`, caller principal, both tables): owner CustomerChildTeam (Y) = the reconciler's for the written row (which holds the core pair). Chat twin `Chat_ACreateWithAnOrdinaryCoreInY_IsOwnedByYsTeam`. (UAC-r2 condition 4.) |
| AC3 | `ACallerWhoMayNotFileUnderTheCore_GetsTheUniformNotFound_WhateverTheCore` (one theory, 12 cases): Secure core without AppendTo, ordinary core without AppendTo, an existing core the caller cannot read, a core that does not exist, an unknown record type (no catalog row), and the baseline unappendable lookup; each asserts service Kind NotFoundOrDenied, ProblemDetails 404 / `record_creation.not_found` / the uniform words, chat `DATAVERSE_ACCESS_DENIED` with the same uniform words, nothing created. (UAC-r2 conditions 1 and 2, negative side.) |
| UAC-r2 cond. 1 fault | `AFaultOnTheCorePairsOwnCatalogRead_IsTheExisting503` (both tables; renamed in fix round 1): a 500 on the core pair's own catalog read is Kind Upstream, status 503; chat carries the failure's own code. A fault one step earlier, at the type lookup's AppendTo read, is today's uniform 404: `AFaultOnTheTypeLookupsAppendToRead_IsTodaysUniformNotFound` (DI-P3, K1, design §15.4 row 13). |
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

- **DI-P3 (pre-existing): a faulted catalog read at the TYPE LOOKUP's AppendTo is a 404, not a 503.** `CallerWriteAuthorization.CallerMayAppendToOrganizationOwnedAsync` treats any failed row read as "no AppendTo" (`return row.IsSuccess`), so a 5xx on `sprk_recordtype_refs(id)?$select=sprk_recordtype_refid` gives the uniform not-found. This is the existing rule for every organization-owned lookup (the regarding pair's type too), and it runs BEFORE the core pair's own catalog read (which is the 503 tested above). Changing it would change existing refusals (NFR-07); fail-closed either way. UAC-r2 condition 1 says "never a different 4xx": this path is the exception, pre-existing and shared. **Accepted by UAC-r2 as K1 (fix round 1, item E):** design §15.4 row 13 (`CallerWriteAuthorization.cs:414`: fails closed, uniform, shared, no oracle, NFR-07), pinned by `AFaultOnTheTypeLookupsAppendToRead_IsTodaysUniformNotFound`.
- **DI-P4: Repair does not ask a Signal's or Decision Record's core record.** `RecordCreationService.Repair.cs` `RepairOwnerAsync` -> `ResolveOwnerOverAsync` passes no core, so a Repair of a cored Signal/DR would re-own over its lookups only. Repair has no production caller yet (052, DI-008). Class: K-candidate until Repair is wired.

## Fix round 1 (review items A, C-G; 2026-11-04)

What changed, by review item (UAC-r2's conditions are each tested):

- **A. One builder.** `RecordOwnershipContext.ForRow` takes an optional trailing `RecordOwnershipParent? coreRecord = null`. Given, it is first and the primary, ahead of the primary order (distinct); `SecureOnlyParents` and `ChildEntityLogicalName` are computed as before. Only `CorePairTables` take one: any other table throws `ArgumentException`. Null: unchanged. The XML doc cites `ForExistingRowAsync`'s core-first order as the reason. `OwnerQuestionOf` now passes `coreRecord` to `ForRow`; its `ForParents` / `with` block is deleted. `ForExistingRowAsync` and every other `ForRow` caller are unchanged. No new entry in `Exemptions`, `KnownViolations` or `WriterOrderOptOutAllowList`; the R3b-2 guard's metadata pin of `ForRow` (`ChildRowOwnershipBuilderGuardTests.Builders`) names the new parameter, because it pins the builder by its signature. Tests (all in `ExistingRowOwnershipParityTests`):
  - (a) `ForRow_ACoreRecordForATableThatIsNoCorePairTable_Throws` (todo, invoice, document).
  - (c) `ForRow_WithNoCoreRecord_IsTodaysQuestion_ByteIdentical`: a To Do, an editable copy (Secure-only canonical), a Signal and a Decision Record with no core. The whole context, JSON byte for byte and by strict equivalence, equals today's formula (`ForParents(OrderedParentsOf(row), caller) with { SecureOnlyParents, ChildEntityLogicalName }`), with the parameter left out and passed null.
  - (d) `ForRow_WithTheCoreRecord_IsForExistingRowAsyncsQuestion`: a Signal and a Decision Record, with one stamp present and with none. Parents, order, primary, Secure-only parents, child table and caller are equal, and so is the whole context as JSON.
  - (d, two stamps) `ForRow_WithTheCoreRecord_AndTwoStamps_HasTheReconcilersPrimaryParentsAndOwner`: the same primary, the same first two parents (core, subject), the same parent set, the same Secure-only parents and child table, and the same owner. **Not the same order after the subject: escalated, see below.**
- **C. Core-specific words.** `CallerMayFileUnderCoreAsync` refuses, before any read: an id that is not a record id (non-string, non-guid or the empty guid) with ONE message, `CoreIdNotARecordId` (the earlier `:1559` words; the non-guid string used `PairTargetAsync`'s regarding words), and an id with no type with its own, `CoreIdWithNoType`. Both use the existing 400 `Invalid` kind. `PairTargetAsync`'s words are unchanged (NFR-07); the core pair no longer reaches its id or no-type branches. The AC3 theory is unchanged. Test: `ACoreIdThatIsNotARecordIdOrHasNoType_IsRefusedInTheCorePairsOwnWords` (8 cases: service Detail and 400, chat `ValidationFailed` with the same words, no `$select=sprk_recordlogicalname` read, nothing created).
- **D. The stored core id.** Once `sprk_corerecordid` is a record id, the create body holds `id.ToString("D", InvariantCulture).ToLowerInvariant()`, as `SignalWriter` and `DecisionRecordWriter` store it, because `SignalSuppressionService` compares strings. `WithCoreIdAsTheWritersStoreIt` runs after step 5, whatever the principal. Tests: `ACoreIdIsWrittenAsTheWritersStoreIt` (10 cases: braces, upper case, padding, all three, and already stored; service and chat) and `ASystemCreatesCoreId_IsWrittenAsTheWritersStoreIt` (system principal, 2 cases). The system case is beyond the AC1-AC6 scope; its reason is that the normalisation runs for both principals.
- **E. DI-P3 (K1).** The 503 test is renamed `AFaultOnTheCorePairsOwnCatalogRead_IsTheExisting503` ("a fault on the core pair's own catalog read"). `AFaultOnTheTypeLookupsAppendToRead_IsTodaysUniformNotFound` pins today's uniform 404 for a fault at the type lookup's org-owned AppendTo read: service 404 `record_creation.not_found` with the uniform words, chat `DATAVERSE_ACCESS_DENIED`, nothing created. Design §15.4 row 13.
- **F. AC1 strengthened.** `Step5_...BeforeAnyAppOnlyReadOfTheCore` also snapshots the resolver's `RetrieveAsync` calls (`OwnershipDirectory.RowReads`, a harness addition) and the service's own app-only `IGenericEntityService` calls, at the caller's AppendTo on the core. None names the core.
- **G. These notes.** The system-principal statement is corrected: an unknown catalog id is 503 `owner_unavailable`, not 409. `ASystemCreateWhoseCoreTypeTheCatalogDoesNotHold_IsTheOwnerFault503` covers it; the parity world's app-only Retrieve now throws for a missing row, as Dataverse does. The path has no production caller (no `ForSystem(` caller in `src/`).

Failing first (C, D): the 20 C/D cases run against the tip's `RecordCreationService.cs` (`d13e1cabc7`, with the two word constants added so the tests compile): **16 failed, 4 passed**. The 4 that passed are the non-string id x2 (those words were already `CoreIdNotARecordId`) and "already stored" x2. After the change: 20 / 20 pass. (A) is a refactor; its failing test was the ArchTest (`ChildRowOwnershipQuestionsGoThroughTheOneBuilder`, 1910 / 1911 before).

Seeding proof (A-d): in `ForRow`, the core is put LAST instead of first (`ordered.Concat([coreRecord])`). The six (d) cases (4 exact plus 2 two-stamp): **6 failed / 0 passed**. Reverted; 6 / 6 pass.

**Escalated (not fixed): the stamp order after the subject.** With a core and TWO stamps on the subject (a matter and a project), `ForExistingRowAsync` asks `[core, subject, project, matter]`: its derived stamps come in `CoreAncestorResolver.CoreRecordEntities` order (project, matter, ...). `ForRow(coreRecord)`, like the first version's `OwnerQuestionOf`, asks `[core, subject, matter, project]`, in the table's lineage order. The primary (the core), the parent set and the owner are the same (the resolver's ordinary answer is the primary's unit, and secure-if-any is over the set). Only the order after the subject differs. Matching it would change `ForRow`'s ordering on the core path beyond what UAC-r2 approved, so it is left for UAC-r2 to rule on. Class: K-candidate (same primary and owner; the order of the non-primary parents could only change which unreadable parent a refusal names first). Resolved in fix round 2: K2, design §15.4 row 14 (below).

## Fix round 2 (B')

**Owner rule (2026-11-04).** `RecordCreationService` never writes a HALF core pair on `sprk_signal` or `sprk_decisionrecord` (`sprk_corerecordtype` set, `sprk_corerecordid` missing, null, empty or whitespace), and never refuses because of one.

**Rulings.**
1. The fill comes from the row as it ENDS (042b's `AsTheRowEnds`: step 6½'s planned stamps over the sent ones; a sent stamp that agrees is kept). A filing parent that is itself a core-table record is a root. The derived root is never probed as the caller.
2. The type is a root's table's catalog row: FILLED with that root's id, lower-case "D" form.
3. The type disagrees and there is exactly one root: the pair is REPLACED, the type taken from the root table's catalog row through ONE shared helper (app-only). Zero or several catalog rows: CLEARED. A fault: the existing 503 `stamp_unavailable`, nothing written.
4. Two roots: filled with the one of the sent type; neither: CLEARED.
5. No root: CLEARED (the type is removed; neither column is written).
6. Any planning or derivation FAULT: the existing 503 `stamp_unavailable`. Never a clear on a fault; nothing written.
7. A filled Secure root: the normal path gives the row-8 409 `NoOwnerSource`.
8. Warnings: distinct new codes on the existing pre-create warning channel. The text names neither the root id nor whether it is Secure.
9. Unchanged: an id with no type stays refused in fix round 1's item-C words; no pair, or a full pair, is unchanged.
10. The system-principal twin follows the same rules.
11. ArchTest: server code that writes `sprk_corerecordtype` also writes `sprk_corerecordid`.

**UAC-r2 conditions:** (a) the fill only from the plan or the ending row, never a probe; (b) never a sent type beside an id of another type; (c) no root: clear with a warning; (d) a fault: 503, nothing cleared or written; (e) a Secure fill: 409; (f) the warning names no root id and no Secure state; (g) the ArchTest has its controls.

**Coordinator conditions (UAC-r2 accepted rulings 1-7):**
1. A filled or replaced id is stored lower-case "D"; a replace takes the type from the SAME catalog row as the id's table, so a mixed pair cannot result.
2. The codes are distinct and documented here and in XML doc. They reach only the model and caller through the existing channel. The service's own logs (`RecordCreationService`) never hold the root id at Information or above: step 6¾ logs the table and the outcome code only (Information), and a catalog fault logs the table only (Error). (Scoped in fix round 3: the resolvers' pre-existing Warning logs that name parent ids, such as `CoreAncestorResolver`'s "Derived core ancestor {Entity}({Id}) cannot be stamped", are K1 and pre-date 042c; 042c does not change them.)
3. The ArchTest scans WRITES, not reads or lists that only name the columns.
4. K2: the two-stamp owner-kind theory (below).

**What changed in src.**
- `RecordCreationService` step 6¾ (`CompleteHalfCorePairAsync`, after step 6½'s `missingStamp`, before step 7):
  - roots = `CoreRootsAsTheRowEnds` (the lookups as the row ends; core tables only; no citation, no Secure-only parent; distinct);
  - for each distinct root table, its catalog row through `CoreAncestorResolver.RecordTypeOfCoreTableAsync`;
  - the sent type matches exactly one root → fill; no match, one root, its catalog row known → replace (`sent with { RecordId = rootType }`, the id that root's); otherwise → clear.
  - The filled or replaced core becomes the owner question's core record (`coreRecord`), so step 7 asks it first, as fix round 1's `ForRow(coreRecord)`. `DecideOwnerAsync` skips its own catalog read for a core step 6¾ already decided (`!askedAsCaller && coreRecord is null`).
- `WithCorePair(item, type, id)` is THE one writer of the pair in the service: type and id together, or both removed (body, `Columns`, `Lookups`, `ClearedColumns`). One without the other throws `ArgumentException`; a type to write with no bind in the body to keep the key of throws `InvalidOperationException`. `WithCoreIdAsTheWritersStoreIt` (fix round 1, D) now writes through it.
- `CoreAncestorResolver.RecordTypeOfCoreTableAsync(entities, coreTable, ct)`: the active `sprk_recordtype_ref` row whose `sprk_recordlogicalname` is a core table (`TopCount = 2`; exactly one row, else `null`); app-only (the service's `IGenericEntityService`); throws on a fault.
- Warning codes (`RecordCreationWarning`, XML doc on each):

| Code | Column | Words (`RecordCreationService.`) | When |
|---|---|---|---|
| `record_creation.core_record_filled` | `sprk_corerecordid` | `CoreRecordFilled(noun)` | rulings 1, 2, 4 |
| `record_creation.core_record_replaced` | `sprk_corerecordtype` | `CoreRecordReplaced(noun)` | ruling 3 |
| `record_creation.core_record_cleared` | `sprk_corerecordtype` | `CoreRecordCleared(noun)` | rulings 3 (no or several catalog rows), 4, 5 |

**No change** to `RecordOwnershipResolver` (beyond fix round 1's `ForRow` parameter), `SignalWriter`, `DecisionRecordWriter`, or any existing refusal's code, status or words. Nothing escalated.

**Helper decision.** One new helper, `CoreAncestorResolver.RecordTypeOfCoreTableAsync`; the existing copies are left as they are. Consolidating them is not clean: each has its own columns, filters or failure contract, and `SignalWriter`'s may not change in this round. The copies (table → catalog row):
- `SignalWriter.ResolveRecordTypeRefIdAsync` (about `:1719`);
- `SignalEvaluator` (about `:699`);
- `OfficeService.DeriveFiledUnderTypeAsync` (about `:2655`);
- `CommunicationProposalApplyService` (about `:691`);
- `CommunicationEnrichmentService` (about `:1119`);
- `SignalReadService` (about `:846`);
- `BudgetInquiryExecutor` (about `:226`);
- `ExternalDataService` (about `:3304`).

**Component justification (CLAUDE.md §11).**
- `RecordTypeOfCoreTableAsync`. Existing: the copies above and `CoreTableOfRecordTypeAsync` (the reverse direction). Extension: it sits beside `CoreTableOfRecordTypeAsync` in `CoreAncestorResolver`; the copies have other contracts. Cost of doing nothing: rulings 2-4 cannot tell a root's type, so a half pair cannot be filled or replaced.
- `WithCorePair`. Existing: fix round 1's id rewrite in `WithCoreIdAsTheWritersStoreIt`, now routed through it. Extension: it is that rewrite, generalised to the whole pair. Cost of doing nothing: no single place writes the pair, and the ArchTest has no writer to recognise.
- Step 6¾ and the three warning codes. Existing: step 6½'s warning channel, extended rather than duplicated. Cost of doing nothing: the half pair is written (the owner rule fails).
- No new endpoint, DI registration, package or Dataverse column. Placement: inside the existing create service (§10).

**Interpretation (noted for review).** Two roots of the SAME table, or a sent type matching several roots, → CLEAR (the core cannot be told). The type is "matched" by comparing the sent type's id with the catalog row the helper returns for each root's table; the sent type's own row is never read.

**Tests (KEEP paths; no new file under `tests/unit/Sprk.Bff.Api.Tests/**`).**
- New `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.HalfCorePair.cs`. The service and chat paths, both tables:

| Ruling / condition | Test |
|---|---|
| 1, 2 (converted AC5 case) | `ACreateWithACoreTypeButNoId_IsFilledFromTheRecordItIsFiledUnder` (8 cases: the id missing, null, empty, whitespace; lower-case "D" stored) |
| 1, (a) | `AHalfPair_IsFilledFromThePlannedStamp_NeverFromADisagreeingSentStamp_NorAProbe` (no caller probe of the derived root) |
| 3, (b), coordinator 1 | `AHalfPairOfAnotherType_IsReplacedWholeFromTheOneRoot` (the type and the id from the same root) |
| 3 (zero / several rows) | `AHalfPairWhoseRootsCatalogRowCannotBeTold_IsCleared` (4 cases) |
| 3, 6, (d) | `ACatalogFault_IsTheExisting503_NothingClearedOrWritten`, `APlanningFault_IsTheExisting503_NothingClearedOrWritten` |
| 4 | `AHalfPairUnderTwoRoots_IsFilledWithTheOneOfItsType_OrCleared` (6 cases) |
| 5, (c) | `AHalfPairWithNoRoot_IsCleared_AndTheRowIsCreatedWithNoCore` |
| 7, (e), coordinator 2 | `AHalfPairFilledWithASecureRoot_GetsTheRow8Refusal`: byte-identical row-8 ProblemDetails and chat words; step 6¾'s logged outcome is FILLED; no Information+ log entry holds the root id |
| 8, (f) | every warning case asserts `NamesNoRecordAndNoSecureState` on the text, and that chat's `ToolResult.Warnings` carries it (`BothCreatedWithTheWarning`) |
| 9 | `ARowWithNoPairOrAFullPair_IsUntouchedByStep6AndThreeQuarters` (4 cases); the id with no type stays `ACoreIdThatIsNotARecordIdOrHasNoType_IsRefusedInTheCorePairsOwnWords` (unchanged) |
| 10 | `WriterBuilderOwnershipParityTests.ACreateWithAHalfCorePair_IsCompletedFromTheRowAsItEnds` (18 cases: system principal and caller; fill from the plan, fill when filed under the root, replace, clear with no root, clear with no catalog row, catalog fault, planning fault, Secure root; the owner equals the reconciler's for the written row) |
| 11, (g) | `tests/Spaarke.ArchTests/CorePairTypeImpliesIdGuardTests.cs` (below) |

- **Converted test.** `ACreateWithACoreTypeButNoId_KeepsTodaysOwner` (`SecureChildOwnershipAiToolTests.CoreRecordPair.cs`) is removed. It is now the fill case `ACreateWithACoreTypeButNoId_IsFilledFromTheRecordItIsFiledUnder`.
- **Harness.** The chat `CreateRecordHandler` and `ServiceCreateWithCore` take the service's restamper and app-only entities from the B' world when set. The scripted user client's Signal and Decision Record lookups add `sprk_regardingproject`. The HalfCorePair service create and the parity world capture the service's log (`CapturingLog<RecordCreationService>`).
- **K2 (coordinator 4).** `ExistingRowOwnershipParityTests.ForRow_WithTheCoreRecord_AndTwoStamps_HasTheReconcilersPrimaryParentsAndOwner` is now a theory over the owner kinds (Secure team, ordinary team, user-owned, unreadable or missing) of the subject To Do, the matter stamp and the project stamp, both tables: **128 cases**. For each it asserts the same primary, the same parent set, the same Secure-only parents and child table, and the same owner (`IsOwned`, `OwningTeamId`, `IsSecureOwner`) or the same `RefusalCode` from `ForRow(core)` and `ForExistingRowAsync`. The stamps are derived from a separate world holding the To Do and both roots, so an unreadable subject or stamp changes the resolver's answers, never the parents. **None differs: 128 / 128.** No `ForRow` change; K2 row 14 in design §15.4.

**ArchTest `CorePairTypeImpliesIdGuardTests`.**
- Rule 1 (keyed writes): an Entity or dictionary indexer assignment (`[KEY] =`, not `==`), `.Add(KEY, …)` / `.TryAdd(KEY, …)`, or a JSON writer call (`WriteString`, `WritePropertyName`, `WriteNull`, …) with the type's key needs an id write within 10 lines in the same file. A key is the literal (any case, with or without `@odata.bind`), a same-file `const string` with that value, or `SignalCoreRecordAccess.CoreRecordTypeColumn`.
- Rule 2 (body rewrites): a member that names the type's key and assigns `JsonBody =` or `Lookups =` must be `WithCorePair`, and must write the id.
- Seen today: `SignalWriter`, `DecisionRecordWriter`, `RecordFamilyReader` (rule 1) and `RecordCreationService.WithCorePair` (rule 2). All are non-vacuity pins.
- Controls:
  - Must fire: a type-only write in each shape, a same-file const, the shared const, an id too far away, an id only in a comment, a body rewrite outside `WithCorePair`, and a `WithCorePair` with no id write.
  - Must not fire: a pair write; reads (`GetAttributeValue`, `AddCondition`, a filter string, `==`, `ColumnSet`, `columns.Add(LookupValue(…))`); refuse lists and other lists that only NAME the columns (`RefusedColumns = Columns("sprk_corerecordtype", "sprk_corerecordid")`, collection expressions, a set initializer, `RefuseColumns(table, fields, ["sprk_corerecordtype"])`); `WithCorePair` with a local function.
- So it does not flag a helper that only names the pair in a refuse list.
- Must fire on real code: with `SignalWriter.cs`'s id line removed, the guard reports `SignalWriter.cs: line 1794`. Restored.
- Limits (in its XML doc): a dictionary collection initializer `{ { KEY, v } }`, an anonymous object and a hand-written JSON string are not seen.

**Failing first.** The B' service, chat and parity tests (52 cases) were run against the tip's `RecordCreationService.cs` and `CoreAncestorResolver.cs` (`6d87258cd3`), with only the inert word functions and code constants added so they compile: **42 failed, 10 passed**. The 10 are the unchanged-behaviour controls: ruling 9 must-not-fire (4), the planning fault (2: step 6½ already refused before B'), and 4 parity cases. After the change: 52 / 52.

**Seeding proof.** In `CompleteHalfCorePairAsync` the fill branch was skipped once (`if (ofTheSentType.Count == 1 && SkipTheFill())`, `SkipTheFill() => false`): **24 failed, 28 passed**. The 24 are every fill case: the converted case (8), ruling 1 (a) (2), ruling 4 (4), ruling 7 (2) and the parity theory's fill and Secure cases (8). Reverted; 52 / 52 pass.
- Ruling 7's 409 is row 8's whether or not the pair is filled, because the Secure matter is also the filing parent. Its tests first passed with the fill skipped, so they now also assert step 6¾'s logged outcome (FILLED) in both the service test and the parity twin.

## Fix round 3 (second adversarial review; 2026-11-04)

**Merge of the work branch (task 031), `e094fec5ab`.** One conflict, in `RecordCreationService` step 5's opening: 031's route-authorised lookup check (`CheckRouteAuthorizedLookupsAsync`, `appendToSkip`) is kept first, then 042c's `string ran;`; both lookup checks take `appendToSkip` as 031 has them, and 042c's core AppendTo (`CallerMayFileUnderCoreAsync`) follows unchanged. The closed route-authorisable list (`sprk_analysis.sprk_playbook`) never meets the core pair. `SecureChildOwnershipAiToolTests.cs` auto-merged (harness additions from both sides). No test changed. After the merge: build 0 warnings; the 042c and 031 classes (`SecureChildOwnershipAiToolTests` incl. `TypedRouteCreateInvariantsTests`, `ExistingRowOwnershipParityTests`, `WriterBuilderOwnershipParityTests`, `AnalysisEndpointsAuthorizationContractTests`, `EventEndpointsAuthorizationContractTests`, `RecordCreationServiceMappingSeamTests`) 1182 / 1182; ArchTests `RouteAuthorizedLookupsCallSiteTests`, `CorePairTypeImpliesIdGuardTests`, `ChildRowOwnershipBuilderGuardTests` 6 / 6.

By review item:
1. **(F4) Row 14.** Reworded: same owner, refusal code, primary and parent set; a ParentUnresolved reason may name a different unreadable parent. The K2 theory now also varies the CORE record's owner kind (Secure team, ordinary team, user-owned, unreadable or missing): **512 / 512**, none differs in owner or refusal code (the same assertions as before).
2. **(K3) `WithCorePair`.** Throws whenever the item binds a core type (`CoreTypeOf(item) is not null`) or a type is written, and the body does not bind it exactly once under a recognised key: a clear can no longer leave an unrecognised type bind. Routed to the existing 503 `stamp_unavailable` (not `check_failed`): step 6¾ catches the `InvalidOperationException` around its three `WithCorePair` calls, and the fix-round-1 stored-id rewrite (`WithCoreIdAsTheWritersStoreIt`, after step 5) does the same; each logs the table only (Error). Test: `ACoreTypeBoundUnderAnUnrecognisedKey_IsTheExisting503_NothingWritten` (8 cases: clear, fill, replace, full pair; both tables; the service path, since the chat mapper binds by metadata).
3. **(K3) ArchTest limits.** `CorePairTypeImpliesIdGuardTests` "Not seen" adds a conditional write in one method (near, not always run together), a `[JsonPropertyName]` DTO write and `Entity.SetAttributeValue(KEY, ...)`.
4. **(F2-minor) `RecordTypeOfCoreTableAsync`.** Takes the caller's logger. Two or more active rows: a Warning naming the table only, then `null` (cleared as today). A `null` collection: an `InvalidOperationException`, which step 6¾'s existing catch maps to 503 `stamp_unavailable`. Tests: `TwoActiveCatalogRowsForOneCoreTable_AreLoggedAtWarning_TheTableOnly_ThenCleared` (no record or catalog id in any form), must-not-fire `OneActiveCatalogRow_LogsNoCatalogWarning`, `ACatalogReadReturningNoCollection_IsTheExisting503_NeverAClear` (2 cases each). A null `Entities` on a non-null collection is not constructible (`EntityCollection.Entities` is get-only and never null); the null-collection case covers the branch.
5. **Log hygiene.** Coordinator condition 2 above is scoped to the service's own logs; the resolvers' pre-existing Warning logs of parent ids are K1, pre-dating 042c.
6. **Design §15.4 rows 15-17** (K1 root table and count from the outcome; K1 the step-6¾ columns skip step 5's field-security and caller checks, neither secured today, task 044; K2 where the fill differs from `SignalWriter`, always matching the reconciler).
7. **`sprk_steps` on `sprk_decisionrecord`.** A caller CAN set it on a create through `RecordCreationService` (chat or service): it is not in `RecordTableRules.CommonServerOwnedColumns`, the `sprk_decisionrecord` rule has no `TableServerOwnedColumns`, `DataverseCreateRecordHandler` blocks only `sprk_document` and the server-written invoice-family tables, and the column is not field-secured (`Entity.xml` `IsSecured 0`; `DecisionCommitService` already notes this). The create's owner question does not read it; `ForExistingRowAsync` asks the subjects of the Signals its `resolvedSignalIds` lists, after the primary. Added as K2 row 18 (diverges only toward Secure; an unreadable listed Signal leaves the question Incomplete). `sprk_steps` handling not changed.

**Failing first.** The 14 new service cases (items 2 and 4) against the merge commit's `RecordCreationService.cs` and `CoreAncestorResolver.cs` (`e094fec5ab`): **12 failed, 2 passed** (the 2 are the must-not-fire). The clear cases reached the create with the stray bind (the harness's create refused the unknown property `sprk_CoreRecordTypeRef@odata.bind`); the fill, replace and full-pair cases were `check_failed`; the null collection was cleared and created; two catalog rows logged no Warning. After: 14 / 14. Item 1 is a widened theory over unchanged code (512 / 512).

## Fix round 4 (owner decision D-204)

**Decision (owner, 2026-11-04; decisions.md, ontology D-204):** Signal, Decision Record and Budget Revision rows are created only by their writers. The general create (`RecordCreationService` and the chat `dataverse.create_record` tool) refuses `sprk_signal`, `sprk_decisionrecord` and `sprk_budgetrevision` as whole tables, for the caller and for the system principal, before anything is written. This replaces fix rounds 1-3 (step 5's core check, step 6¾ and the stored-id rewrite) for these tables.

**The refusal.**
- Registry: `RecordTableRule.CreatedOnlyByItsWriter` (new `bool` property, default `false`), set on the three entries in `RecordTableRules.All`.
- Service: `RecordCreationService.CreateAsync` refuses a flagged table at the same point and with the same refusal as a table it does not create: kind `Invalid` (400), code `record_creation.table_unsupported`, detail `'<table>' records are not created here.`. No new code. It runs before WhoAmI, mapping, every check, the owner decision and the create, for both principals; the trace holds no step (as for an unregistered table).
- Chat: `DataverseCreateRecordHandler.IsCreatedOnlyByItsWriter` (reads the registry flag, so the two cannot drift) is checked right after the `ServerWrittenInvoiceFamilyTables` check in both `ValidateChat` and `ExecuteChatAsync`; `ToolErrorCodes.ValidationFailed` with `CreatedOnlyByItsWriterCreateBlockedMessage(table)`, before any Dataverse call. The invoice-family block and its words are unchanged.

**Component justification (CLAUDE.md §11).**
- `CreatedOnlyByItsWriter`. Existing: the registry's table entries and the service's `table_unsupported` refusal; the chat's `ServerWrittenInvoiceFamilyTables`. Extension: one property on the existing entry; the refusal reuses the existing one. Unregistering the tables instead was rejected: the entries also carry their mapping exclusion (`OntologyContentIsNotMapped`), which the registry tests pin. Cost of doing nothing: the general create keeps creating these rows.
- `IsCreatedOnlyByItsWriter` and its message. Existing: the invoice-family set and message beside them. Extension: a sibling check reading the registry flag, rather than a second hand-kept list. Cost of doing nothing: the chat create asks the user to confirm a create that can never run.
- Placement (§10): inside the existing create service and chat handler; no endpoint, DI registration, package or background work.

**Create paths found (step 1.3).** Every `RecordCreationService.CreateAsync` caller, with the table it creates:
- `ChildRecordEndpoints.CreateAsync` (the browser create route): `CreateTables` only; none of the three. `DecisionRouteCores.CreateChildAsync` (Add To Do, Create Event, Assign Work, Assign Task, Create Budget) goes through it.
- `EventEndpoints` (`sprk_event`), `AnalysisEndpoints` (`sprk_analysis`), `EmailDraftToolHandler` (`sprk_communication`), `OfficeService` quick-create (`QuickCreateFieldRequirements.GetLogicalName`: matter, project, invoice, account, contact), `BudgetInquiryExecutor` (`sprk_servicerequest`).
- `DataverseCreateRecordHandler` (chat): any table; now refuses the three before the service is asked.
- Not a create path: `RecordCreationService.RepairAsync` (an existing row), the playbook node executors (Create Task: `task`; Create Notification: `appnotification`), the chat delete tool.
- The writers: `SignalWriter`, `DecisionRecordWriter`, `ReviseBudgetExecutor` (the writer identity, `OntologyWriterDataverseClient`) never reach the general create (new ArchTest below).
- No first-party flow creates the three tables through the general create.

**Removed (042c code whose only route was a general create of these tables).** `RecordCreationService.cs`, `CallerWriteAuthorization.cs`, `CoreAncestorResolver.cs` and `RecordOwnershipResolver.cs` are back to master's text, then the refusal is added:
- step 5's `CallerMayFileUnderCoreAsync`, `CorePairOf`, the `CorePairWrite` struct, the core-pair refusal words (`CoreIdNotARecordId`, `CoreIdWithNoType`) and the trace note;
- step 6¾ (`CompleteHalfCorePairAsync`, `CoreRootsAsTheRowEnds`), its three warning codes and words, and the `WithCorePair` pair writer;
- the stored-id rewrite (`WithCoreIdAsTheWritersStoreIt`, `StoredCoreId`);
- step 7's core-record parameters (`DecideOwnerAsync`, `ResolveOwnerOverAsync`, `OwnerQuestionOf`) and the system principal's `CoreRecordOfSystemCreateAsync`;
- `CoreAncestorResolver.RecordTypeOfCoreTableAsync`;
- `RecordOwnershipContext.ForRow`'s `coreRecord` parameter (the R3b-2 pin in `ChildRowOwnershipBuilderGuardTests` is master's text again);
- `CallerWriteAuthorization.PairTargetAsync`'s `idColumn` / `typeColumn` parameters (no other caller).
- Kept (another path uses them, unchanged by 042c): `RecordCreationService.Repair.cs`, the reconciler, `ForExistingRowAsync`, `PairTargetAsync` as master has it.

**Tests: deleted or changed, with the reason.**
- Deleted `SecureChildOwnershipAiToolTests.CoreRecordPair.cs` (every case: step 5's core check, its words, the stored id, the core's owner) and `SecureChildOwnershipAiToolTests.HalfCorePair.cs` (step 6¾): the behaviour is removed.
- `ExistingRowOwnershipParityTests.cs`, `WriterBuilderOwnershipParityTests.cs` (042c's half-pair theory and the `CreateParityWorld` additions), `SecureChildOwnershipAiToolTests.cs` (042c harness additions) and `OwnershipDirectory.cs` (`RowReads`) are back to master's text: the `ForRow(coreRecord)` tests (including the K2 two-stamp theory) and the harness existed only for the removed code.
- `CorePairTypeImpliesIdGuardTests` (kept; it guards the server writers, and task 044 relies on it): rule 2 no longer exempts `WithCorePair` (gone): no member may rewrite a body that carries the type. Its `RecordCreationService.WithCorePair` non-vacuity pin is removed; the writers' pins stay. Controls: a `WithCorePair` that writes the id now fires too; the must-not-fire pair-writer case is replaced by a body rewrite that never names the type.
- Deleted `SecureChildOwnershipAiToolTests.SignalCitation.cs` (`CreateRecord_ASignalCitingARuleHeldSecure_...`, unified-access-control-r2 #2084) and `RecordCreationServiceMappingSeamTests.CreateAsync_ACitationLookupMarkedServerSet_...`: both created a Signal through the general create, which is now refused before either question is asked. The citation rule stays pinned by the reconciler's own tests.
- Changed `RecordCreationServicePipelineSeamTests.CreateAsync_ANonReparentableRecordUnderASecureMatter_Is409NoOwnerSource_AndCreatesNothing`: the table is now a `task` under the Secure matter (was `sprk_signal`). The arm and its assertions are unchanged.
- Changed `RecordCreationServiceMappingSeamTests.PostMappingDefault_OnATableFieldMappingDoesNotApplyTo_IsAnArgumentError`: the table is now `task` (`ChatPlatformTableIsNotMapped`; was `sprk_signal`).
- Changed `WriterBuilderOwnershipParityTests`: `StampedRegistryTables` drops `sprk_signal` and `sprk_decisionrecord`; the coverage fact counts the stamped tables the service creates (`!CreatedOnlyByItsWriter`); the Secure-world theory's Signal/Decision Record branch is gone (no case reaches it).
- Harness addition: `SecureChildOwnershipAiToolTests.ScriptedUserClient` knows `transactioncurrency` (entity set, `sprk_budget.transactioncurrencyid`, organization-owned, AppendTo held), for the Create Budget commit below.

**New tests (KEEP paths).**

| What | Test |
|---|---|
| Must fire, service: 3 tables x (caller Web API body, caller mapped item, system principal); 400 `table_unsupported`, the words, nothing written, nothing read (the journal is empty: no WhoAmI, no caller question, no owner, no create, no re-stamp, no mirror, no event), no step | `RecordCreationServicePipelineSeamTests.WriterOnlyTables.CreateAsync_ATableOnlyItsWriterCreates_IsRefused_BeforeAnyReadOrWrite` (9) |
| The refusal equals an unregistered table's (kind, code, words, steps) | `..._IsRefusedAsAnUnregisteredTableIs` (3) |
| Must not fire, service: an event is still created | `CreateAsync_AnEvent_IsStillCreated_TheRefusalDoesNotFire` |
| Must fire, chat execute and validate (no Dataverse call, no create) | `ChatServerWrittenTableBlockTests.CreateRecord_ATableOnlyItsWriterCreates_IsRefused_WithNoCreateCall` (3), `ValidateChat_ATableOnlyItsWriterCreates_IsRefusedBeforeTheConfirmation` (4, one upper-case) |
| Must not fire, chat: the invoice family keeps its own words; ordinary tables are not flagged | `CreateRecord_AServerWrittenInvoiceFamilyTable_KeepsItsOwnRefusal` (4); the existing invoice-family and `sprk_todo` tests unchanged |
| Must not fire, decision commits (real executors, real `DecisionRouteCores`, real `ChildRecordEndpoints.CreateAsync`, real `RecordCreationService` over this harness) | `SecureChildOwnershipAiToolTests.DecisionCreateBudget_StillCreatesTheBudget_ThroughTheGeneralCreate`, `DecisionAssignTask_StillCreatesTheTaskEvent_ThroughTheGeneralCreate`, `DecisionReviseBudget_StillRecordsTheRevision_ThroughTheWriter_NotTheGeneralCreate` (the general create in the request's services, the revision created by the writer identity, nothing asked of the general create). End to end at the executor seam: the commit route itself is not driven here |
| Registry | `RecordTableRulesTests.CreatedOnlyByItsWriter_OntologyWriterTables_AreFlagged` (3), `CreatedOnlyByItsWriter_IsTheThreeOntologyWriterTablesOnly`; ArchTest `RecordTableRulesRegistryGuardTests.NoCreateTable_IsOneOnlyItsWriterCreates` with must-fire and must-not-fire controls |
| Writers untouched (ArchTest) | `WriterOnlyTablesWritersGuardTests`: `SignalWriter`, `DecisionRecordWriter`, `ReviseBudgetExecutor`, `OntologyWriterDataverseClient` never depend on the general create (`RecordCreationService`, `RecordCreationRequest`, `ChildRecordEndpoints`, `DecisionRouteCores`, `DataverseCreateRecordHandler`): IL (NetArchTest, each type and its nested state machines) and source (the files; the executor file's `ReviseBudgetExecutor` span only). Controls: a seeded fixture whose async method calls `RecordCreationService.CreateAsync` is found (must fire); a clean async fixture and a comment are not (must not fire) |

**Failing first.** The new behaviour tests (31 cases: `DisplayName~042c fix round 4` and the registry flag tests) were run against the source with the refusal made inert (the flag declared but set on no entry, the service and chat checks removed, the helper members kept so the tests compile): **23 failed, 8 passed**. The 23 are every must-fire case (service 9 + 3, chat 3 + 4, registry 4); the 8 are the must-not-fire cases (the event, the invoice family x4, the three decision commits). ArchTest `RecordTableRulesRegistryGuardTests`: the flag guard and its must-fire control failed (no table flagged); the writer guard passed (it pins today's structure). After the change: all pass.

**Seeding proof (writer guard on real code).** A private method in `ReviseBudgetExecutor` that resolves `RecordCreationService` and calls `CreateAsync`: both writer-guard facts failed (IL: `ReviseBudgetExecutor`; source: `DecisionActionExecutors.cs` line 392). Reverted; they pass.

**Found in passing (pre-existing, not changed).** `RecordCreationService`'s server-set filing-lookup guard exempts a citation lookup (`RecordOwnershipResolver.IsCitation`); the only citation is `sprk_signal.sprk_policy`, so with the refusal that exemption is not reached through the service. Left as is (not 042c code); the reconciler still applies the citation rule.

## Verification

Fix round 4, at `5a2fc5281a` (src and tests; the docs commit follows):
- `verify-suites.sh`: solution build 0 warnings, 0 errors. Filtered suites **6729 passed, 0 failed, 7 skipped** (7365 at fix round 3: the deleted core-pair, half-pair and K2 cases, plus the new cases). ArchTests **1926 / 1926** after the B3 fix in `5a2fc5281a` (the script run at `c68090c2db` had 1925 / 1926: ADR-038 B3 flagged a DI-resolution assertion in the Revise Budget commit test, now removed).
- Not in the script's filter, run alone: `AnalysisEndpointsAuthorizationContractTests` + `EventEndpointsAuthorizationContractTests` 151 / 151. Decision-commit tests (`SecureChildOwnershipAiToolTests.Decision*`) 9 / 9.
- Publish delta (`publish-delta.ps1`, master `7c7d24a3b7`, branch `5a2fc5281a`): **+389 bytes** (zip 41,214,619 to 41,215,008; 192 files each side; total 39.31 MB). Under every §10 threshold.
- CVE (`dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`): no vulnerable packages.

Fix round 3, at `028deaf0d5` (src; the docs commit follows):
- `verify-suites.sh`: solution build 0 warnings, 0 errors. ArchTests **1919 / 1919**. Filtered suites **7365 passed, 0 failed, 7 skipped** (one run; no timeout-only failures, so no re-run).
- Not in the script's filter, run alone: `AnalysisEndpointsAuthorizationContractTests` + `EventEndpointsAuthorizationContractTests` 151 / 151.
- Publish delta (`publish-delta.ps1`, master `0d4aa87ee0`, branch `028deaf0d5`): **+9,135 bytes** (zip 41,213,274 to 41,222,409; 192 files each side; total 39.31 MB), which includes 031's merged code. Under every §10 threshold.
- CVE (`dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`): no vulnerable packages.

Fix round 2 (B'), at `083ca71be3`:
- `verify-suites.sh`: solution build 0 warnings, 0 errors. ArchTests **1914 / 1914** (the 3 new guard tests). Filtered suites **6933 passed, 0 failed, 7 skipped** (6757 at fix round 1; the new cases).
- B' service, chat and parity tests: 52 / 52. K2 theory: 128 / 128. `ExistingRowOwnershipParityTests`: 156 / 156. `WriterBuilderOwnershipParityTests`: 70 / 70.
- Publish delta (`publish-delta.ps1`, master `0d4aa87ee0`, branch `083ca71be3`): **+5,576 bytes** (zip 41,213,260 to 41,218,836; 192 files each side; total 39.31 MB). Under every §10 threshold.
- CVE (`dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`): no vulnerable packages.

Fix round 1, at `60962dd568` (items A, C-G):
- `verify-suites.sh`: solution build 0 warnings, 0 errors. ArchTests **1911 / 1911** (`ChildRowOwnershipQuestionsGoThroughTheOneBuilder` passes; it failed at `d13e1cabc7`). Filtered suites **6757 passed, 0 failed, 7 skipped** (6720 before; the new cases).
- `DisplayName~042c`: 87 / 87.
- Publish delta (`publish-delta.ps1`, master `0d4aa87ee0`, branch `60962dd568`): **+3,182 bytes** (zip 41,213,292 to 41,216,474; 192 files each side; total 39.31 MB). Under every §10 threshold.
- CVE (`dotnet list package --vulnerable --include-transitive` on the branch's `Sprk.Bff.Api`): no vulnerable packages.
