# Task 042b notes: derive the I-1 stamp before the owner (FR-41, #2483, DI-009)

Rigor FULL (security-tagged; modifies tests). Branch `task/fm-042b`, cut from `cfebe65f9b` (work branch tip; also an ancestor of `origin/master`). POML steps 0-5 here; steps 6-7 (two adversarial passes, the UAC-r2 full pass, closing DI-009 / #2483, TASK-INDEX) are the main session's.

Facts the POML predates, applied: 041 is merged (step 8 is NoOp, so no step-8 index assertion; design.md section 15.5's step-8 bullet updated, AC13). Task 020 is on this base: `SecureRootServerColumnRefusal` and the 403 `sdap.access.secure_flag_transition_only` refusals are untouched (no diff in those paths).

## What changed in src

`RecordCreationService.cs`
- **Step 6½** (new, in `DecideAsync` after step 6): `trace.Begin(StampAndInheritance)`, then `PlanStampBeforeOwnerAsync` (private, new): skips a document (`RecordsOwnNarrowing`) and a non-stamped table; otherwise `PlanMissingStampAsync(table, MergedRowOf(table, item))` app-only, for every principal. Permanent fault -> 400 `record_creation.stamp_underivable`; any other fault -> 503 `record_creation.stamp_unavailable`; same words and log lines step 10 had.
- **Step 7**: `DecideOwnerAsync` takes the planned dictionary and asks `ResolveOwnerOverAsync` over `OwnedChildWrite.LookupsOf(item)` ∪ {derived column -> `RecordOwnershipParent(root)`} ∪ the record-thread filing (`RecordThreadFiling.ParentsAsync` / `FilingLookupsOf`, AC10), through the unchanged `OwnerQuestionOf` / `ForRow`. The thread read sits inside the existing try: a fault is 503 `owner_unavailable`.
- **Step 10**: `StampBeforeCreateAsync` takes the planned dictionary and does NO derivation: it binds it (`BindStampAsync`, 500 `no_decision` / 503 unchanged, still at step 10) and sets I-16 from the same merged row. A system principal still binds after the create (DI-007).
- `MergedRowOf` (private static, new): the item's lookups as `EntityReference`s plus the pair id (the row shape both 6½ and step 10 read; it was inline in step 10).
- Repair: `RepairOwnerAsync` takes `(Missing, Faulted)`; derived lookups join `keyed` after the thread filing; `Faulted` keeps the owner (Skipped `record_repair.stamp_unavailable`, Warning log with table, row id and reason) and blocks the mapping (`MappingFilingUndecided`).

`RecordCreationService.Repair.cs`
- `RepairAsync` plans once (stamped tables only) before `RepairOwnerAsync`; a fault is logged and recorded as `Faulted`.
- `RepairStampAsync` became `RepairStamp` (static, no I/O): Faulted -> Skipped `StampUnavailable`; nothing missing -> Present `StampNothingMissing`; otherwise the patch plus Repaired entries, as before.

No new type, enum member, interface, DI registration, endpoint or package (section 11: the only new members are the private helpers `PlanStampBeforeOwnerAsync` and `MergedRowOf`; `MergedRowOf` is the step-10 row shape moved, not new behaviour).

## AC table

| AC | Evidence |
|---|---|
| AC1 order | Step 6½ in `DecideAsync`; route A1 asserts one intermediate read before the create (`IntermediateReads` = 1 at `_onAppCreate`, no second derivation); 400/503 codes and words pinned by the unchanged `RouteCreate_WhenTheParentsRootCannotBeDetermined_*` and `RouteCreate_WhenTheParentsRootCannotBeReadRightNow_*` and seam `:84-148`; bind refusals still at step 10 (seam metadata-read tests unchanged, green); system principal binds after the create (seam A3). The call-count assertion passes before and after (the old code also derived once); it guards the shape. |
| AC2 | A1 `RouteCreate_AnEventUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheSecureTeam_WithTheShareeMirrorAtCreate`; A2 `RouteCreate_TheReconcilersQuestionOverTheWrittenEvent_AgreesWithTheCreatesOwner`; A3 `CreateAsync_AsTheSystem_AnEventUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheSecureTeam_WithTheShareeMirrorAtCreate`; chat twin `ChatCreate_AnEventUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheSecureTeam_WithTheShareeMirrorAtCreate`. Each 042a test is renamed and inverted (old name in its XML doc); none deleted. |
| AC3 | First half: `RouteCreate_AToDoWithNoPairUnderATeamOwnedReportCardInX_WhoseMatterIsInY_IsOwnedByYsTeam` (CustomerChildTeam) and the parity theory `ACreateUnderAReportCard_IsOwnedByItsMattersTeam_AsTheReconcilerOwnsIt` (todo/event/communication/memo). **Second half NOT met as written** (open question Q1): `RouteCreate_AToDoWithItsPairNamingATeamOwnedReportCardInX_IsOwnedAsTheReconcilerOwnsTheWrittenRow` pins that the create and the reconciler both give Y. |
| AC4 | `RouteCreate_UnderATeamOwnedEventOfAMatterInTheTransientMakeSecureInterval_IsRefused409_AsADirectChildIs` (flagged, owned by ChildTeam) and `..._LeftFlaggedByACompensatedMakeSecure_...` (flagged, owned by a user): 409 `record_owner_secure_parent_not_isolated`, nothing created. |
| AC5 | Both tests assert status, reason code and detail text equal a DIRECT event under the same flagged matter, verbatim, and that the detail contains "re-run Make Secure on it, then retry". `grep -rE "SecuringRoot|Securing[A-Z]\w*Exempt|MakeSecureInProgress" src` finds nothing (only the pre-existing `UnsecuringRoot`, task 148). |
| AC6 | `RouteCreate_WhenTheRootReadFaults_AndTheOwnerWouldBeRefused_Is503StampUnavailable` (event absent from the owner world, stamp read faults): 503 `record_creation.stamp_unavailable`. |
| AC7 | A1: the caller is not a sharee of the Secure matter; the 201 body serializes to exactly `{ "id" }`; body plus Location contain none of SecureMatter, SecureNamedTeam, SecureBu, SecureDefaultTeam. |
| AC8 | `AUserOwnedToDoMissingItsStampUnderATeamOwnedInvoiceOfASecureMatter_IsReOwnedToSecureAndStamped_InOneUpdate` (to-do in the Secure Record unit: one update with `ownerid` = SecureNamedTeam and `sprk_regardingmatter`); `AUserOwnedToDoInAnotherUnitUnder..._IsNeverReOwnedToTheOrdinaryTeam_AndIsStamped` (cross-unit: task 016's `OwnerMovesBusinessUnit` escalation, stamp still written; before 042b it was re-owned to ChildTeam); `WhenTheMissingStampCannotBePlanned_TheOwnerIsKept_ReportedStampUnavailable_AndLoggedWithTheRowId` (owner Skipped `record_repair.stamp_unavailable`, stamp Skipped `stamp_unavailable`, no owner or stamp written, Warning log carries the row id and the reason). |
| AC9 | `WriterBuilderOwnershipParityTests` 042b section: `TheParityTheory_CoversEveryStampedRegistryTable` (registry == the 7 tables), secure-world and ordinary-world theories over all 7 tables through the REAL service and `ResolveOwnerAsync(ForExistingRowAsync(written row))`, plus the Q3 communication with `sprk_communicationthread`. Differences found: memo (harness had no memo columns: fixed in StampWorld), Q3 (fixed, AC10), Signal / Decision Record (Q2 below). |
| AC10 | Q3 fact `ACommunicationCreatedInASecureRecordThread_IsOwnedAsTheReconcilerOwnsItWithTheThreadsFiling` failed before the thread fix and passes after; the thread filing is added inside the create's question (the lookups handed to `OwnerQuestionOf` / `ForRow`), as Repair does. No new seam, no DI entry. |
| AC11 | `ACreateUnderAReportCardWhoseMattersUnitHasNoDefaultTeam_IsRefused_AsTheReconcilerRefusesIt` (todo/event/communication/memo): create 409 `OwnerUnresolved`, nothing created; the reconciler is not owned, with the same refusal code. No disagreement, no fix needed. |
| AC12 | Chat `:106` green, comment rewritten; A1 asserts every AppendTo probe names only the event and none names SecureMatter; `RouteCreate_ABrowserSentStamp_DerivesNothing_AndIsAskedAsTheCallerAsBefore` (the stamp the caller sent is its own mapped value, asked as the caller as before, owner SecureNamedTeam); document (seam) and work assignment (seam) tests unchanged and green; `EmailDraftRecordCreationTests`, `ChildRecordCreateRefusalContractTests`, `CrossSurfaceOwnerTests`, `OfficeRecordOwnershipTests`, `SecureRootCreateRouteTests.Office*` green with unchanged assertions. |
| AC13 | 041 merged first: no step-8 assertion; design.md section 15.5 "Step 8 index" bullet now says step 8 is NoOp. |
| AC14 | See "Verification" below. |
| AC15 | Main session (UAC-r2 pass). |

## Section 15.4 rows as K lines

- K row 1 (root underivable AND step 7 refuses -> 400 `stamp_underivable`, was 409): needs a compound data fault after step 5's AppendTo on the intermediate passed. Not separately tested.
- K row 2 (root read faults AND step 7 refuses -> 503 `stamp_unavailable`, was 409): pinned by AC6's test.
- K row 3 (root read faults AND owner decision faults -> 503 `stamp_unavailable`, was 503 `owner_unavailable`): same status, code differs. Not separately tested.
- K row 4 (system principal, intermediate missing -> 400 `stamp_underivable`, was 409 `ParentUnresolved`): no system creator exists (DI-007, owner task 033).
- K row 5 (root flagged secure but not isolated -> 409 `SecureParentNotIsolated`, was 201 ordinary): pinned by AC4's two tests, words equal to the direct child's (AC5).
- K row 6 (root unreadable at step 7 after 6½ read it -> 409 `ParentUnresolved`, was 201): a race only.
- K row 7 (report-card child whose root's unit has no default team -> 409, was 201): pinned by AC11's theory; the reconciler refuses with the same code.
- K addendum A (not in section 15.4): Signal and Decision Record filed under a team-owned intermediate of a Secure root now get 409 `NoOwnerSource` (the existing `OwnerDecided` / `SecureFilingRefused` words, `OwnerDecision.IsSecureOwner` true) instead of 201 owned by an ordinary team. Section 15.4's "every stamped table is a reparentable child" is wrong for these two (they are not in `OwnershipParentEntities`). Fail closed; see Q2.
- K addendum C (Repair): a stamp-planning fault now also blocks the mapping (`MappingFilingUndecided`) on a filed row whose owner is kept for another reason (for example team-owned), where before the fault only skipped the stamp. Fail closed by design (the filing's owner is undecided without the stamp's root); Repair has no production caller yet (052, DI-008).
- K addendum B: a create carrying `sprk_communicationthread` whose thread cannot be read now gets 503 `owner_unavailable` at step 7 (the thread read is inside the owner try). Reachable only by a race or fault, because step 5 already asks AppendTo on the thread as the caller.

## Seeding proof

Replaced `derivedStamp ?? new Dictionary<string, object>()` with `new Dictionary<string, object>()` in `DecideOwnerAsync` (the step-7 inclusion of the derived root), rebuilt, ran A1: **1 failed** - `Expected fields["ownerid@odata.bind"] to be "/teams(d9ec0b6f-...0003)" ... but found "/teams(cf15f587-...)"` (ChildTeam). Reverted; `diff` against the saved copy: identical.

## Failing-first

Copied `RecordCreationService.cs` and `RecordCreationService.Repair.cs` back from `cfebe65f9b`, rebuilt, ran `SecureChildOwnershipAiToolTests|RecordCreationServicePipelineSeamTests|WriterBuilderOwnershipParityTests|RecordRepairModeTests`: **28 failed / 658 passed** (686). The 28: A1, A2, A3, chat twin, row-5 x2, AC6, AC3 no-pair, AC3 pair-parity, Repair x3, parity secure-world x7, change-1 x4, row-7 x4, Q3. Restored my versions: **0 failed / 686 passed**. The tests that pass before as well (must-not-fire by design): browser-sent stamp, ordinary-world theory x7, the registry-coverage fact, and the AC1 read-count sub-assertion.

## Existing-test exceptions (assertions changed outside the POML list, or harness)

None of an existing test's assertions changed outside the POML's files. Harness changes (no assertion changes), listed as the brief requires:
1. `tests/integration/data-mutation/CoreAncestorStamping/StampWorld.cs`: added `["sprk_memo"]` column metadata (regarding project/matter/workassignment/servicerequest, analysis/communication/document/event/invoice/agreement/budget, `sprk_reportcard`, pair id and type, contact/organization/timekeeper). Reason: the parity theory creates a memo through the service; without the columns the stamp world's column probe answered "no stamp columns" for memo.
2. `SecureChildOwnershipAiToolTests.cs` `ScriptedUserClient.Lookups["sprk_todo"]`: added `("sprk_regardingreportcard", "sprk_reportcard", "sprk_RegardingReportCard")` (live navigation property, `Spaarke.Dataverse.Models` / TodoRegardingUpdateBuilder). Reason: AC3's to-do under a report card is a route create; the scripted metadata refused the bind.
3. `RecordCreationServicePipelineSeamTests.cs`: the sharee mirror's share table is a field (`_shareTable`) instead of a fresh `FakeRecordShareTable` per `Service()`; the post-create re-derivation's update of a created child row also lands in the share world (`ApplyToTheCreatedShareRow`), as one Dataverse would hold it. Reason: A3 asserts the sharee mirror for a system principal, whose stamp is bound by that update before the mirror reads the row.
4. `RecordRepairModeTests.cs` `RepairWorld`: the service's logger is a capturing logger (`Log`) instead of `NullLogger`; new helpers `ToDoMissingItsStampUnderATeamOwnedInvoiceOfTheSecureMatter`, `InvoiceStampReadFaults`. Reason: AC8 asserts the log line.

Test-scope note: every added test maps to an AC or a section 15.6 must-fire / must-not-fire item; nothing beyond.

## Verification

All at fix commit `aaefc40f49`.
- BFF / solution build (`dotnet build Spaarke.sln`, via verify-suites.sh): 0 errors, 0 warnings.
- Touched suites (`SecureChildOwnershipAiToolTests|RecordCreationServicePipelineSeamTests|WriterBuilderOwnershipParityTests|RecordRepairModeTests`): 686 passed / 0 failed (28 failed on the base src).
- AC14 filter (`SecureChildOwnershipAiToolTests` (includes the ChatCreatedChildStampTests and RouteCreatePreCreateStampTests partials), `RecordCreationServicePipelineSeamTests`, `WriterBuilderOwnershipParityTests`, `RecordRepairModeTests`, `EmailDraftRecordCreationTests`, `CrossSurfaceOwnerTests`, `ChildRecordCreateRefusalContractTests`, `OfficeRecordOwnershipTests`, `OfficeCopyOfCanonicalOwnershipTests`, `SecureRootCreateRouteTests`): 774 passed / 0 failed. (A first attempt with a bare `FullyQualifiedName~Office` filter took the host to 5.8 GB and was stopped; it is broader than "Office ownership suites".)
- `verify-suites.sh` project set: 6659 passed / 0 failed / 7 skipped (the pre-existing `OfficeEndpointsContractTests` skips), 26 m 46 s; host peaked at about 8.6 GB with 7 GB of RAM free.
- ArchTests (rebuilt first, by the script): 1911 passed / 0 failed.
- Publish delta (`publish-delta.ps1 -MasterSha cfebe65f9b -BranchSha aaefc40f49`; zip tool PowerShell `Compress-Archive -CompressionLevel Optimal`): master 41,212,584 bytes / 192 files; branch 41,213,395 bytes / 192 files; delta +811 bytes; total 39.3 MB. MasterSha is the branch base, which is a commit on `origin/master` (merged via #2551); the master tip (`aef86b89f7`) carries later unrelated PRs, so the base isolates this change.
- CVE: `dotnet list package --vulnerable --include-transitive` - no vulnerable packages. No new HIGH CVE.

## Open questions and escalations (for the main session / UAC-r2)

- **Q1 (AC3 second half not met).** Design 15.6 says "the same report card with the to-do's pair set is owned by X". On this code a pair is the to-do's subject only when it names one of `TodoRegardingBuilder.RegardingLookupByEntity`'s 11 targets, and `sprk_reportcard` is not among them (the client `TodoRegardingUpdateBuilder.ts` has it as the 12th, visual-host-create-button-r1 task 040). So the pair names no subject, the lineage order decides, and the to-do goes to Y, for the create AND the reconciler (they agree; no parity defect). Making it X means adding `sprk_reportcard` to the server regarding map, which changes `SubjectColumnOf` for every to-do writer and the reconciler: outside `OwnerQuestionOf` / `ForRow`, so not done here. Recommendation: file the proposed DI below and let the owner decide whether a report-card pair should be the subject; 042b ships with the parity pin.
- **Q2 (Signal / Decision Record, K addendum A).** A Signal or Decision Record created through the service under a team-owned intermediate of a Secure root is now refused 409 `NoOwnerSource` (existing words) instead of being created with an ordinary owner. This is fail-closed and uses an existing refusal, so no refusal's code or words changed (the NFR-07 trigger does not fire), but it is a new outcome not listed in section 15.4. Recommendation: UAC-r2 confirms it in its pass and section 15.4 gets a row for it.
- No POML escalation trigger fired: the parity differences were closed inside the create's question (Q3) or were harness gaps (memo); row 7 agrees; no single-condition refusal changed; no new AppendTo probe (A1 asserts it).

## Fix round 1 (2026-11-03)

Decision: `notes/decisions.md` "2026-11-03 — 042b F1" on the work branch (`c1428c181d`): owner chose **A' ("replace, not add")**.

**F1 (the defect).** Step 6½ was fill-only, so a stamp the item already HELD that disagreed with the planner's root went into step 7's question; the step-12 restamp then overwrote it, leaving the #2483 state (probe: POST `sprk_event` with `sprk_RegardingEvent` = an event team-owned by ChildTeam under Secure S, and `sprk_RegardingMatter` = ordinary M2, got owner ChildTeam).

**What changed in src (round 1).**
- `CoreAncestorRestamper.cs` (the one file outside the POML list; owner-approved under A'): new `PlanStampOfRowAsync` returns the FULL plan, the restamp's own `PlanStamp` (SET `EntityReference`, CLEAR `DBNull`, an agreeing column absent; `null` when nothing changes). `PlanMissingStampAsync` is now `MissingOf(row, PlanStampOfRowAsync(...))`, so its behaviour and callers are unchanged. Section 11: (1) Existing: `PlanMissingStampAsync` and `RestampChildAsync`'s planning; (2) Extension: it IS the extension, the body that was `PlanMissingStampAsync`'s, minus its fill-only filter, which moved to `MissingOf`; (3) Cost of doing nothing: step 7 cannot know which held stamp the restamp will replace, so F1 stays open.
- `RecordCreationService.cs`: step 6½ keeps the full plan; step 7 asks over `AsTheRowEnds(LookupsOf(item), plan)` (internal static, new: each planned column takes the planned root, a disagreeing held value is replaced, a planned clear removes it, a missing one is added; everything else unchanged); step 10 binds `MissingOf(merged row, plan)`, fill-only as before. Repair: `RepairOwnerAsync` asks over `AsTheRowEnds(row lookups, plan)` (security finding 6); `RepairStamp` writes `MissingOf(row, plan)`, fill-only.
- Unchanged by A': the bind (fill-only); step 5's AppendTo set (the derived root is never probed as the caller); every refusal's code, status and words.

**Amendments.**
- **AC1** (read count): step 6½ plans whenever the item has exactly one intermediate source, a stamp sent or not (`ClassifyStampSource` does not stop on a held root column), so the intermediate is read once in both cases. This was already true in round 0 (the coordinator's premise that "planning runs when a stamp is sent" is a change does not hold; the count stays 1). The browser-sent-stamp test now asserts it (1 read before the create).
- **AC12** (amended by the owner): "a sent stamp that agrees changes nothing; one that disagrees is replaced in the owner question". `RouteCreate_ABrowserSentStamp_DerivesNothing_AndIsAskedAsTheCallerAsBefore` now also asserts that the plan of the agreeing row is `null` and `AsTheRowEnds(lookups, null)` equals the lookups.

**Tests (round 1), each failing first where it is a behaviour change.**
- F1 (a) route: `RouteCreate_ASentOrdinaryStampUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheSecureTeam_WithTheShareeMirrorAtCreate` (owner SecureNamedTeam; body keeps the sent ordinary bind; the restamp puts SecureMatter on the row; the sharee mirror at create; AppendTo asked on the sent OrdinaryMatter, never on SecureMatter).
- F1 (a) chat twin: `ChatCreate_ASentOrdinaryStampUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheSecureTeam_WithTheShareeMirrorAtCreate`.
- F1 (b): `RouteCreate_ASentSecureMatterStampUnderAnEventOfAnOrdinaryMatter_IsOwnedByTheOrdinaryTeam_NeverTheSecureTeam` (owner ChildTeam, never SecureNamedTeam; the restamp puts OrdinaryMatter on the row; no sharee mirror).
- F1 (c): the browser-sent agreeing test above (must not fire).
- Repair: `AUserOwnedToDoHoldingAStaleStampUnderATeamOwnedInvoiceOfASecureMatter_IsReOwnedToSecure_AndItsStampIsKept` (Secure Record unit: one write, `ownerid` = SecureNamedTeam, no `sprk_regardingmatter` written) and `AUserOwnedToDoInAnotherUnitHoldingAStaleStampUnderATeamOwnedInvoiceOfASecureMatter_IsNeverReOwnedToTheOrdinaryTeam` (child unit: `OwnerMovesBusinessUnit`, nothing written, still person-owned; before the fix it was re-owned to the stale matter's ordinary team).
- AC8 fault test: also asserts Mapping Skipped `record_repair.mapping_filing_undecided`.
- Parity (review item 1): `CreateParityWorld` now records each create's body (`Creates` carries `Fields`) and materializes the row from the BODY's binds (not from the item's lookups), so the written row is what the create sent plus what the restamp wrote. New `CreateAsCallerAsync` (the route harness's `ScriptedUserClient` as `Directory.CallerUserId`) and the theory `ACallersCreateUnderATeamOwnedIntermediateOfASecureMatter_BindsTheStamp_AndIsOwnedAsTheReconcilerOwnsTheWrittenRow` (memo, analysis, communication): step 10 binds `sprk_RegardingMatter` in the body and the owner equals the reconciler's.
- The reconciler half: `SecureChildReconciler.ContextFor` is `private` in the `private sealed class Pass` (SecureChildReconciler.cs:3033; its thread filing at ~:3049-3053), so it cannot be called from a test. The copy stays, with a remarks block pinning it to those lines.

**Failing-first (round 1).** A full copy-back of round-0 src does not compile against the new tests (they call `PlanStampOfRowAsync` and `AsTheRowEnds`), so the round-0 behaviour was restored inside `AsTheRowEnds` instead: keep every held lookup, add a planned root only where nothing is held (exactly round 0's fill-only question). Rebuilt and ran the 10 round-1 tests: **5 failed / 5 passed**. The 5 failing: F1 (a) route, F1 (a) chat, F1 (b), Repair x2 (owner found ChildTeam instead of SecureNamedTeam; SecureNamedTeam instead of ChildTeam; Repair: no write / `ReasonCode` null). The 5 passing before as well, by design: F1 (c) (must not fire), the AC8 fault test (its new Mapping assertion pins round-0 behaviour), and the 3 caller-principal parity cases (coverage of step 10's bind, no held stamp). Restored the fixed file (no probe text left): 10 / 10 pass.

**Seeding proof for (a).** The failing-first probe above is the "break the replace" seed: with held values kept (no replace), (a) fails (`Expected Owner(fields) to be {...0003} ... but found {cf15f587-...}`, ChildTeam). A second seed, "add, not replace" (keep the held value AND add the planned one), fails only (b) (owner SecureNamedTeam where ChildTeam is expected): (a) alone would not catch an "add" implementation, (b) does. Reverted both; the restored file matches the saved copy.

**Review item 3 (Signal / Decision Record with a core pair): STOPPED, not fixed (outside OwnerQuestionOf / ForRow).** By inspection they differ. `RecordOwnershipContext.ForExistingRowAsync` (RecordOwnershipResolver.cs:655-751) asks a Signal or Decision Record WITH a core pair as `[core record (sprk_corerecordtype read through the record-type catalog, CoreTableOfRecordTypeAsync, + sprk_corerecordid), subject, the subject's derived stamps, (DR: resolved Signals' subjects), rest]`. The create asks `ForRow(table, lookups, pairId)` (RecordCreationService.OwnerQuestionOf, :1155-1163), whose parents are lookups only: `sprk_corerecordtype` targets `sprk_recordtype_ref` (not an ownership parent, dropped) and `sprk_corerecordid` is a string, so the core record is not in the create's question at all. A Signal created through the service with a core matter A and a subject under matter B can therefore get another primary (and, if A is Secure and nothing else is, another secure-if-any answer) than the reconciler gives the written row. Fixing it needs an async catalog read (record type -> core table) in the create's step 7, which is outside `OwnerQuestionOf` / `ForRow` (both synchronous, no reads). Not tested here; proposed DI-P2 below.

**K lines (both reviews), recorded.**
- K1: Signal / Decision Record under a team-owned intermediate of a Secure root -> 409 `NoOwnerSource` (fail closed; UAC-r2 agreed 2026-11-03); design.md section 15.4 row 8.
- K1 (UAC-r2 condition (b)): the 409 `SecureParentNotIsolated` words (row 5) can name a root id the caller did not send; accepted under condition (b) (the same words as the direct child).
- K2: a permanent planning fault on Repair is reported as `record_repair.stamp_unavailable` (no new enum member is allowed); design.md section 15.4 row 10.
- K2: two-source rows (an item with two intermediate sources): no stamp is planned; the create and the reconciler agree (neither derives a root).
- Filed: missing-thread 503 and the thread-only communication -> #2557 / DI-018 (section 15.4 row 9 is addendum B). AC3 second half -> #2556 / DI-017 (Q1 above).
- design.md: section 15.3 (step 7, step 10, the A' shape, Repair), section 15.4 (rows 8-10, addenda A-C; the reparentable-child bullet corrected), section 15.6 (A' must-fire cases, caller-principal parity, AC12 / AC1 amended, AC3 second half filed).

**Existing-test exceptions (round 1).** Assertions added, none removed, in files 042b already owned: `RouteCreatePreCreateStampTests.cs` (browser-sent test: + the read count, the null plan and the unchanged question), `RecordRepairModeTests.cs` (AC8 fault test: + the Mapping assertion). `WriterBuilderOwnershipParityTests.CreateParityWorld.Creates` changed type to carry `Fields`; its existing `.Should().BeEmpty()` uses are unchanged. Harness changes:
1. `StampWorld.cs`: `AfterUpdate` hook, invoked after each PATCH is applied (default none). Reason: the route harness carries the step-12 restamp into the share world, so the sharee mirror reads the row as the restamp left it.
2. `SecureChildOwnershipAiToolTests.cs` `ScriptedUserClient.Lookups`: `("sprk_regardinginvoice", "sprk_invoice", "sprk_RegardingInvoice")` added for `sprk_memo` and `sprk_analysis` (both in `CoreAncestorResolver.StampSourceColumns` / the StampWorld column sweep). Reason: the caller-principal parity cases bind the invoice as the caller.
3. `RecordRepairModeTests.cs` `RepairWorld.ToDoMissingItsStampUnderATeamOwnedInvoiceOfTheSecureMatter`: optional `heldMatterStamp`.

**Verification (round 1), at fix commit `c0fbc125ee`.**
- Build: `dotnet build Spaarke.sln` (via verify-suites.sh): 0 errors, 0 warnings.
- Round-1 tests: 10 / 10 pass (5 fail on the round-0 semantics, see failing-first).
- AC14 filter (the same ten classes as round 0): **782 passed / 0 failed** (774 + the 8 new tests).
- `verify-suites.sh` project set (log dir `scratchpad/verify-042b-r1`): **6667 passed / 0 failed / 7 skipped** (the same pre-existing `OfficeEndpointsContractTests` skips), 22 m 26 s.
- ArchTests (rebuilt first, by the script): **1911 passed / 0 failed**.
- Publish delta (`publish-delta.ps1 -MasterSha cfebe65f9b -BranchSha c0fbc125ee`, `Compress-Archive -CompressionLevel Optimal`): master 41,212,610 bytes / 192 files; branch 41,213,439 bytes / 192 files; **delta +829 bytes**; total 39.3 MB. (The master zip differs by 26 bytes from round 0's measurement of the same commit: archive nondeterminism.)
- CVE: `dotnet list package --vulnerable --include-transitive`: no vulnerable packages. No new HIGH CVE.

## Proposed DI entries (the main session files them)

- **DI-P1 (found in passing): a to-do's report-card pair is never its subject.** `TodoRegardingBuilder.RegardingLookupByEntity` (server, 11 targets) lacks `sprk_reportcard`, which the client `TodoRegardingUpdateBuilder.ts` writes as its 12th target. `RecordOwnershipContext.SubjectColumnOf` therefore never ranks a report card first on a to-do, for the create, the reconciler or Office. Owner decision needed (Q1). Class: K-candidate (pre-existing; create and reconciler agree). Filed as #2556 / DI-017.
- **DI-P2 (fix round 1, review item 3): the service's create of a Signal or Decision Record does not ask about its core record.** `RecordCreationService.OwnerQuestionOf` -> `RecordOwnershipContext.ForRow` reads lookups only, so the core pair (`sprk_corerecordtype` + `sprk_corerecordid`) that `ForExistingRowAsync` asks FIRST is absent from the create's question; the create and the reconciler can disagree on a Signal or DR created through the service with a core pair. Pre-existing (round 0 did not change it). The fix needs the record-type catalog read in step 7 (outside `OwnerQuestionOf` / `ForRow`). Unverified by a test; evidence by inspection in "Fix round 1" above. Class: proposed F-class if a product path creates a Signal or DR through the service with a core pair (the child-records route and chat accept `sprk_signal` / `sprk_decisionrecord`, `RecordTableRules.cs:401-412`); the ontology writers (`SignalWriter`, `DecisionRecordWriter`) build their own question and are not affected.
