# Task 052d — The repair pass report: core-pair findings, retry sets and abandoned rows

Slice d of task 052 (FR-25, FR-41; task 044(d); ontology D-204, D-210; 052b K6, K8). POML: `tasks/052d-repair-pass-report.poml`.
Rigor: FULL. Nothing here enables the pass on any environment.

## What the pass now reports (and never writes)

- **Half pair** (`core_pair_half`): a `sprk_signal` or `sprk_decisionrecord` row created at or after go-live holding
  `sprk_corerecordtype` without `sprk_corerecordid`, or the reverse.
- **Pair changed after create** (`core_pair_changed_after_create`): an update audit of either pair column on such a row, whenever
  the update happened, with the audit time and the changing user's id.
- **Undecidable change** (`core_pair_change_undecided`): an update audit whose changed-column list cannot be read (absent, or an
  entry that is not a number). Never read as "no change".
- **Retry sets and abandoned rows** (K6, K8): each swept table's retry-set size and the ids abandoned in this run.

The report sends only reads (`RetrieveMultiple`, column metadata). No create, update, delete, associate or assign request
reaches either table from the pass in any mode; no ArchTest exemption was added. Completing or clearing a pair stays out of
scope (D-204).

## Decision — the audit read

**Query shape (as corrected in fix round 1, below).** One `QueryExpression` on the `audit` table per core-pair table per run:
`objecttypecode = {the table's numeric ObjectTypeCode, read from its metadata this run}`, `operation in (2, 5)` (Update, Upsert),
`createdon` (the audit row's own time, i.e. when the change was made) in
`[max(go-live, audit watermark − 15 min), run start − 2 min)`, after the last `auditid` read when a capped window continues,
ordered by `auditid`, columns `auditid, createdon, objectid, userid, attributemask, operation`. Create audits (operation 1) are
not asked for: a create is not a change after create. (First written with the logical name in `objecttypecode`; Dataverse refuses
that.)

**How a pair change is decided.** `attributemask` lists the changed columns by their `ColumnNumber`. The two pair columns'
numbers come from the table's column metadata (`IGenericEntityService.GetColumnMetadataAsync`, now carrying
`ColumnMetadata.ColumnNumber`), read once per table per run through the pass's existing metadata cache
(`RecordRepairPassContext`) every run (fix round 1: with the audit settings, not only when the window holds an update audit). A mask naming either number is a change;
a mask naming only other numbers is not; a mask that cannot be read is `core_pair_change_undecided`. Metadata without both
numbers is a fault. `RetrieveAuditDetails` / `RetrieveRecordChangeHistory` were rejected: one request per audit row or per row,
and both return old and new values, which the report must never hold or emit.

**Forward-only.** The audit row does not carry the changed row's `createdon`. The candidate rows' `createdon` is read in
batches (an `in` filter on the primary key, `RowReadChunk` = 100 ids per query); nothing is reported until every batch has
been read. A row created before go-live is not reported. A row the read does not return (for example deleted) cannot be shown
to be inside the forward-only scope: it is counted (`corePairRowsUnread`) and logged (table, row id, audit time, changing user
id), not reported as a finding.

**Paging and caps** (named constants on `RecordRepairCorePairReport`): `MaxRowsPerScan` = 500 half-pair rows and
`MaxAuditRowsPerScan` = 1000 update audit rows per table per run (one page each); `RowReadChunk` = 100; `MaxFindingsInResult` = 50
findings listed in the result JSON (every finding is still counted and logged). A cap reached: `reportTruncated` (or
`corePairFindingsTruncated`), the run is partial, and the window keeps its end and continues after the last id next run.

**The report watermark and its state.** Its own entry in the job's existing state store (`IDistributedCache`), key
`record-repair-pass:{table}:report:state` beside the repair key `record-repair-pass:{table}:state`, 30 days, and its own claim
`record-repair-pass:{table}:report` (owner = run id). Two watermarks: the half-pair scan's (windowed on the row's `createdon`)
and the audit scan's (windowed on the audit row's time); each with its own stopped-sweep end and cursor. The report never reads
or writes the repair state, so it cannot move the repair watermark, sweep cursor or retry set.

**Reported once per window.** Each window starts 15 minutes before the last watermark (as the pass does, review K5), so the next
run re-reads the overlap. The state keeps the keys of the rows (`h:{id}`) and audit rows (`a:{auditid}`) already handled with
their time; a key found again is skipped. When a window finishes, keys older than (watermark − 15 min) are dropped: the next
window cannot reach them. So a finding is reported by the run that first reads it and by no later run.

**Faults (fail closed).** A failed scan, audit read, metadata read, candidate-row read or state read: counted (`reportFailures`),
logged as an error ("no change is assumed"), the run is partial, and that sweep's watermark does not move, so the next run reads
the same window. A state save that fails is a fault too; the findings already logged may be logged again by the next run.

**Privilege.** Reading `audit` needs `prvReadAuditSummary` (and audit history `prvReadRecordAuditHistory`). The BFF reads app-only
as its Dataverse application users. The customer deployment guide (H10, `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`)
registers both BFF application users (the app registration and the user-assigned managed identity) as **System Administrator**,
which holds both privileges. No Spaarke-defined role is needed or changed: `SpaarkeRoleAuditReadGuardTests` (no Spaarke role grants
audit read) and the UAC-r2 rule "change history stays System Administrator only" (uac-r2 decisions 2026-11-06; ontology D-143)
stay as they are. **No role change; the escalation trigger did not fire.** Dev's auditing state (on for both tables and all four
pair columns) is task 044 notes §5.

## When the report runs

Every scheduled run, after the repair tables, inside the same tick, lease, product-principal check and `try` (a run that cannot
decide its principals reads nothing, as before). Not in an on-demand run: that run repairs one other table over one window
(052c), and the two core-pair tables are never in the pass's table list. A finding does not make the run fail (it is a report,
not a fault of the pass); a fault, a cap reached or a held report claim makes it partial.

## What the operator sees

Result JSON (the existing run record, `GET /api/admin/jobs/record-repair-pass/status` and `/history`), additive fields:
`corePairHalf`, `corePairChanged`, `corePairUndecided`, `corePairRowsUnread`, `reportRowsRead`, `reportAuditRowsRead`,
`reportTruncated`, `reportFailures`, `reportClaimHeld`, `corePairFindingsTruncated`, `corePairFindings` (each `{table, id, finding,
auditTime, changedBy}`), and `retrySets` (each swept table `{table, size, abandoned: [ids], abandonedTruncated}`; abandoned ids
listed up to `MaxAbandonedIdsListed` = 50 per table). Heartbeat: the same counts, `retrySets=` (tables whose set is not empty,
`table=size`) and `abandonedIds=` (`table:id|id`); `-` when none. One Warning line per finding: table, row id, finding code, audit
time and changing user id (plus run id and correlation id). Never a column value, a core-record id or row content.

## Placement Justification (§10, ADR-052)

In the BFF, inside the existing `RecordRepairPassJob` on the in-process `Spaarke.Scheduling` host: the same tick, lease, claim
store, state store and metadata cache, the BFF's app-only Dataverse identity, low volume (rows with a broken pair are rare;
update audits are paged and capped). No new job, route, store, package, policy, role, DI registration or background service.
Publish-size delta and the CVE check are the main session's.

## Component Justification (CLAUDE.md §11)

- **`RecordRepairCorePairReport`** (new internal class, no DI registration; the job builds it per run). (1) Existing: the repair
  pass excludes both tables; `SignalCoreRecordAccess` reads the pair per request but reports nothing; nothing in the BFF reads
  column audit history. (2) Extension: it is a part of the existing job's scheduled run, reusing its claim store, state store,
  metadata cache, result and heartbeat; it is a separate file because its rules (read-only, two windows, audit decoding) differ
  from Repair's. (3) Cost of doing nothing: task 044 removed field security from the pair on the condition that an edit outside
  the server is detected (D-210); without it a half or altered pair on append-only evidence goes unseen.
- **`ColumnMetadata.ColumnNumber`** (new optional field in `Spaarke.Dataverse`, projected in `ColumnMetadataOf`). (1) Existing:
  `GetColumnMetadataAsync` already reads every attribute's metadata (one `RetrieveEntityRequest`), and projected type, flags and
  bounds but not the number. (2) Extension: one more field of the same projection; no new method or request. (3) Cost of doing
  nothing: an update audit's `attributemask` cannot be decoded, so a pair change could not be told from any other update.
- **Result JSON / heartbeat fields** (`retrySets`, the report counts, `corePairFindings`) and `RunCounts.RetrySets`,
  `AbandonedIds`, `Report`. Extensions of the existing run record; without them an operator sees abandoned rows only in
  individual log lines and a large retry set not at all (052b K6, K8).

## Tests

`tests/integration/data-mutation/RecordOwnership/RecordRepairPassReportTests.cs` (a partial of `RecordRepairPassJobTests`, as
052c's on-demand file is, so it shares the pass harness; its harness part extends `PassWorld` with core-pair rows, audit rows and
column numbers); one projection test in `tests/unit/domain/Dataverse/ColumnMetadataProjectionTests.cs`.

| AC | Test |
|---|---|
| AC1 | `Report_AHalfPair_IsReported_EitherHalf_AndAWholePairOrNeitherHalf_IsNot`; `Report_AHalfPairRowCreatedBeforeGoLive_OrAWholePair_IsNotReported_EvenWhenTheScanReturnsIt`; `Report_TheQueries_AreBoundedByGoLiveAndTheSettlePoint_AndAskForUpdatesOnly` |
| AC2 | `Report_AnUpdateOfEitherPairColumn_IsReported_WithTheAuditTimeAndTheChangingUser`; `Report_AnUpdateOfOtherColumnsOnly_ACreate_AndAChangeToARowCreatedBeforeGoLive_AreNotReported`; `Report_AnUpdateWhoseChangedColumnsCannotBeRead_IsReportedUndecided_NeverNoChange` (2) |
| AC3 | `Report_SendsNoWriteToEitherTable_InAScheduledOrAnOnDemandRun` (every Dataverse call the job's doubles received is a read; no repair write to either table); ArchTests unchanged |
| AC4 | `Report_KeepsItsOwnState_AndNeverMovesTheRepairWatermarkSweepOrRetrySet`; `Report_ASecondRunOverTheSameWindow_ReportsNothingNew_ThoughTheOverlapReadsTheRowsAgain` |
| AC5 | `Report_MoreHalfPairRowsThanTheCap_*`; `Report_MoreAuditRowsThanTheCap_*` |
| AC6 | `Report_AFaultReadingTheChanges_IsAFault_NeverNoChanges_AndTheAuditWatermarkStays` (audit read, column numbers, changed rows) |
| AC7 | `Report_TheResultAndTheHeartbeat_CarryEachTablesRetrySetSize_AndTheIdsAbandonedThisRun` |
| AC8 | `Report_NoEntryOrLogLine_CarriesAColumnValueOrACoreRecordId` |
| — | `ColumnMetadataProjectionTests.AColumnsNumber_IsCarried_AndAbsentWhenNotStated` |

**Fail before.** With the job file put back to its pre-change version (the report class present but never called), 16 of the 17
report tests failed; the one that passed is `Report_KeepsItsOwnState_*`, which calls the report directly (its must-fire is A7).

**Must-fire, each rule switched off then restored with `git checkout -- <file>`** (test project rebuilt; run over
`RecordRepairPass*` + `ColumnMetadataProjection*`, 128 tests):

| Batch | Rule switched off | Tests that failed (all expected, no others) |
|---|---|---|
| A | A1 the half-pair code re-check (`type || id`) | `Report_AHalfPairRowCreatedBeforeGoLive_OrAWholePair_*` |
| A | A2 an unreadable mask read as "no change" | `Report_AnUpdateWhoseChangedColumnsCannotBeRead_*` (2), `Report_SendsNoWrite_*` (undecided count) |
| A | A3 the go-live check on changed rows | `Report_AnUpdateOfOtherColumnsOnly_ACreate_AndAChangeToARowCreatedBeforeGoLive_*` |
| A | A4 the once-per-window keys (half and audit) | `Report_ASecondRunOverTheSameWindow_*` |
| A | A5 truncation not counted | `Report_MoreHalfPairRowsThanTheCap_*`, `Report_MoreAuditRowsThanTheCap_*` |
| A | A6 an audit read fault taken as "no rows" | `Report_AFaultReadingTheChanges_*("the audit read")` |
| A | A7 the report state under the repair state's key | `Report_KeepsItsOwnState_*` |
| A | A8 a core-record id written to a log line | `Report_NoEntryOrLogLine_*` |
| B | B1 retry-set size not recorded | `Report_TheResultAndTheHeartbeat_CarryEachTablesRetrySetSize_*` |
| B | B2 the report's state ignored by the run status | `Report_AFaultReadingTheChanges_*` (3), `Report_MoreHalfPairRowsThanTheCap_*`, `Report_MoreAuditRowsThanTheCap_*` |
| B | B3 `ColumnNumber` not projected | `ColumnMetadataProjectionTests.AColumnsNumber_*` |
| C | abandoned ids not recorded | `Report_TheResultAndTheHeartbeat_CarryEachTablesRetrySetSize_*` |
| D | an update request sent to the table by the report | `Report_SendsNoWriteToEitherTable_*`, failing on the reads-only assertion (`UpdateAsync` found) |

Batch A: 11 failed; batch B: 7; batch C: 1; batch D: 1 (after moving the no-write assertions to the top of the test, so the guard
itself fires first). Restored: all pass.

## Known limits

- **K1 — Superseded by fix round 1 (below).** The first version filtered `objecttypecode` by logical name, which Dataverse
  refuses; the read-only live check of the query shape found it, and the query now filters by the numeric code.
- **K2 — Every update of a pair column is reported, whoever made it.** The server never updates the pair after create (the BFF
  update paths hold it fixed), so any such audit is either an edit outside the server or a server defect; both are worth a line.
- **K3 — Half-pair presence.** A half is "a lookup with an id" xor "text that is not blank". The server-side filter uses null /
  not null; a pair column holding only whitespace is not returned by the filter. An id that is not a GUID is not a half pair
  (it is the access check's `Malformed`, a different question).
- **K4 — A deleted row's change is counted, not reported** (`corePairRowsUnread`, with a log line), because the row cannot be shown
  to be created after go-live.
- **K5 — A failed state save may repeat findings** the next run (the keys that suppress them were not saved).
- **K6 — Update audits of other columns are read to be decided.** The mask is decided in code, not filtered in the query (a `like`
  filter on `attributemask` is not relied on); on a busy table most audit rows read are not findings. The cap bounds each run and
  the next run continues.
- **K7 — The report is not part of an on-demand run** (by design, above); a window the scheduled report has passed is not re-read.

## Review and fix round 1

Findings, their class, and the outcome. Each rule below is enforced by a test shown to fail with the rule switched off.

| # | Finding | Class | Outcome |
|---|---|---|---|
| F2a | The audit read filtered `objecttypecode` by the table's logical name. Live read-only check on dev (2026-11-17, main session; no write, no setting changed): a condition with the logical name fails with HTTP 400 "Expected type of attribute value: System.Int32"; with the numeric ObjectTypeCode (dev: `sprk_signal` 11003, `sprk_decisionrecord` 11002) it returns rows. So every audit read faulted on Dataverse; the report failed closed, so nothing was ever reported. | F (confirmed defect on a real path) | Fixed. The code is read at run time from the table's entity metadata (`IGenericEntityService.GetTableAuditMetadataAsync`, one `RetrieveEntityRequest` for the entity's own properties; `TableAuditMetadata.ObjectTypeCode`), kept for the run; a code the metadata does not state is an audit fault. Custom tables' codes differ per environment, so no number is in code. The test double refuses (throws) an audit query whose `objecttypecode` value is not an int and answers by code. |
| F2b | The sweep never checked that auditing is on. With `isauditenabled` off for the organization, or `IsAuditEnabled` off for the table or either pair column, the history is empty; the sweep read zero rows, moved the audit watermark and reported a clean run (fail open). | F | Fixed. Every run, before the audit read (also when the window holds no audit rows), the report reads the organization's `isauditenabled` (app-only read of `organization`, once per run), the table's `IsAuditEnabled` and each pair column's `IsAuditEnabled` (`ColumnMetadata.IsAuditEnabled`, projected from the existing column metadata read). A flag that is off, or not stated, is an audit fault: one error line per table per run ("auditing is not on (...)", naming which setting), `reportFailures` counted, the run partial, the audit watermark not moved. The half-pair scan does not depend on auditing and still runs. |
| K→F | Only operation 2 was asked for; an Upsert (5) that changed a pair column was missed. | K, fixed | Fixed. `operation in (2, 5)`; the mask decides. Rule for an Upsert audit that is the row's own creation: its audit time is **not after the row's `createdon`** (`audit time <= createdon`) → creation, not a change, not read, not reported (not even as undecided). An Update audit is a change whenever it happened. The row's `createdon` is already read for the forward-only check. |
| H | The report's reads went straight through the job's `IGenericEntityService`. | hardening | Done. The report holds a `WriteRefusingEntityService` over it (`WriteRefusingEntityService`, Finance.Procedures; its write members throw before anything is sent), so "no write" is a property of the type, not of the code that happens to be written. The class's behaviour is unchanged; it gained one pass-through read member because `IGenericEntityService` gained one (below), and `ProcedureDryRunPlanningContractTests` classifies that member as a read. |

**Surface added, and why (CLAUDE.md §11).** `IGenericEntityService.GetTableAuditMetadataAsync` and `TableAuditMetadata`
(object type code, table audit flag), `ColumnMetadata.IsAuditEnabled`. (1) Existing: `GetColumnMetadataAsync` reads one
`RetrieveEntityRequest` per table, per-column only; nothing exposes the table's own code or flag. (2) Extension: a field of the
existing column projection for the column flag; the table-level facts are one entity-level read of the same request type (a
sibling member, default body throws as `GetColumnMetadataAsync`'s does), because a per-column dictionary cannot carry them.
(3) Cost of doing nothing: without the code no audit can be read on Dataverse, and without the flags an unrecorded history reads
as "no change". The organization flag uses the existing `RetrieveMultipleAsync` on `organization` (already a listed non-access
table in `AccessTableReaderScanTests`).

**Live check (read-only, main session, 2026-11-17).** Dev facts used to shape the query and the tests (no number is
hard-coded in the BFF): `audit.operation` options 1 Create, 2 Update, 3 Delete, 4 Access, 5 Upsert, 115 Archive, 116 Retain,
117 RollbackRetain; `attributemask` values look like `80` or `51,53,59,56` (comma-separated ColumnNumbers, no braces; the parser
already trims and skips empty entries, so a leading or trailing comma also reads); dev ColumnNumbers `sprk_signal`
`sprk_corerecordid` 93, `sprk_corerecordtype` 94 (95 is `sprk_corerecordtypename`, AttributeOf `sprk_corerecordtype`),
`sprk_decisionrecord` 61 and 62 (63 the name column); `organizations.isauditenabled` is true on dev.

**Tests (fix round 1)**, in `RecordRepairPassReportTests.cs` unless noted:
`Report_TheAuditRead_FiltersByEachTablesNumericObjectTypeCode_ReadFromItsMetadata`;
`Report_ATableWhoseObjectTypeCodeIsNotStated_IsAnAuditFault_NeverNoChanges`;
`Report_AuditingNotOn_AtTheOrganizationTheTableOrEitherPairColumn_IsAnAuditFault_NeverNoChanges` (7 cases: organization off,
table off, type column off, id column off, and each of organization / table / column not stated);
`Report_AuditingOff_IsAFault_EvenWhenTheWindowHoldsNoAuditRows`;
`Report_TheOrganizationsAuditSettingThatCannotBeRead_IsAnAuditFault_NeverAuditingOn`;
`Report_AnUpsertThatChangedAPairColumn_IsReported_AndAnUpsertThatCreatedTheRow_IsNot`;
`Report_AnUpdateAuditAtTheRowsCreationTime_IsStillAChange_OnlyAnUpsertIsReadAsACreation`; the queries test now asserts the
numeric code per table and `operation in (2, 5)`; `ColumnMetadataProjectionTests.AColumnsAuditSetting_*` and
`ATablesObjectTypeCode_AndAuditSetting_*`; the contract test's read-member list gains the new member; the pass's metadata-read
count test now expects the report's pair-column read of the two core-pair tables (audit settings are read every run).

**Must-fire (fix round 1)** — each rule switched off once, the test project rebuilt, the affected tests shown failing, then
restored with `git checkout -- <file>` (tree clean after each):

| Rule switched off | Tests that failed |
|---|---|
| `objecttypecode` filtered by the name (a string) instead of the number | 32 of 132 (every audit read faults), incl. `Report_TheAuditRead_FiltersByEachTablesNumericObjectTypeCode_*` |
| Organization audit flag ignored | the organization-off case, the organization-not-stated case, `Report_AuditingOff_IsAFault_EvenWhenTheWindowHoldsNoAuditRows` |
| Table audit flag ignored | the table-off case, the table-not-stated case |
| Type column's audit flag ignored | the type-column-off case |
| Id column's audit flag ignored | the id-column-off case, the column-not-stated case |
| Flags read as on when not stated (`== false` instead of `!= true`) | the three "not stated" cases |
| A missing object type code read as 0 | `Report_ATableWhoseObjectTypeCodeIsNotStated_*` |
| Organization setting read fault swallowed as "on" | `Report_TheOrganizationsAuditSettingThatCannotBeRead_*` |
| `operation in (2)` only (Upsert not asked for) | `Report_AnUpsertThatChangedAPairColumn_*`, `Report_TheQueries_*` |
| Upsert-at-creation rule off (an upsert audit not after `createdon` read as a change) | `Report_AnUpsertThatChangedAPairColumn_IsReported_AndAnUpsertThatCreatedTheRow_IsNot` |
| `IsAuditEnabled` / table metadata not projected | `ColumnMetadataProjectionTests.AColumnsAuditSetting_*`, `ATablesObjectTypeCode_*` |

Not separately must-fired: the `WriteRefusingEntityService` hardening (a write through the report cannot be written without
changing the field's type; the existing no-write test still asserts every call that reaches the Dataverse double is a read, and
`ProcedureDryRunPlanningContractTests` / `DryRunNoWriteGuardTests` guard the class itself).

### Known limits added in fix round 1

- **K-b — A row whose `createdon` is backdated before go-live or before the report's watermark is never reported.** A row created
  with `overriddencreatedon` earlier than go-live is "created before go-live" by the forward-only rule; one backdated to before the
  half-pair watermark falls behind that window and is never read. The repair pass's own window has the same property.
- **K-c — The result JSON lives in the run history store**, which is per instance and lost on restart. The Warning log line is
  the durable record of a finding (table, row id, finding code, audit time, changing user id).
- **K-d — Rows no longer readable leave the run ok.** An update audit whose row cannot be read (for example deleted) is counted
  (`corePairRowsUnread`) and logged, not reported, and does not make the run partial.
- **K-e — NoLock reads may report an update that was later rolled back.** The reads are `NoLock`; a finding can be a false
  positive, never a missed one.
- **K-f — An Upsert audit stamped later than the row's own `createdon`** would be read as a change although it is the creation
  (the rule is `audit time <= createdon`). The error is a false positive (a line to look at), never a missed change. Not
  measured live: the creation audit's time against the row's `createdon` was not part of the live check.
- **K-g — A change within the creation transaction by an Update audit** is reported (an Update audit is never the creation).
- **K-h — The audit settings are read each run** (the organization once, the table's two flags and the pair columns' once per
  table); the metadata reads of the pass's `metadataReads` now include the two core-pair tables' column reads every run.

## Gates

Fix round 1 (re-run): solution build 0 warnings, 0 errors; ArchTests 2639/2639 (no exemption, no list change); Sprk.Bff.Api.Tests
filtered (Repair, RecordOwnership, Scheduled, Job, Admin, RecordCreation, Contact, AssignCascade, Census, ColumnMetadata, Signal,
DecisionRecord, Dataverse, Procedure, Finance; `!~Api.Office.`) 24678 passed, 16 skipped, 0 failed; `dotnet format whitespace
--verify-no-changes` clean on every touched .cs. First-round gates, for the record:

- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- Sprk.Bff.Api.Tests filtered (Repair, RecordOwnership, Scheduled, Job, Admin, RecordCreation, Contact, AssignCascade, Census,
  ColumnMetadata, Signal, DecisionRecord, Dataverse; `!~Api.Office.`): 16693 passed, 12 skipped (pre-existing), 0 failed.
- `tests/Spaarke.ArchTests`: first run 2638/2639. `AccessTableReaderScanTests` (#2253 K3) requires every table named in a
  scanned folder to be refused or listed as not access; `audit` is now listed (platform-written change history, read only). The
  writer-only guards for Signal and Decision Record are unchanged, with no exemption. Re-run: 2639/2639 (owner-write census
  unchanged).
- `dotnet format whitespace --verify-no-changes` on every touched .cs: clean.

## Deviations from the POML

- `tests/Spaarke.ArchTests/AccessTableReaderScanTests.cs` gains one `NotAccessTables` entry (`audit`, with its reason): the guard's
  own mechanism for a newly named table. It is not an exemption from a writer-only guard.

- The test file is a partial of `RecordRepairPassJobTests` (052c's precedent), so its tests are named
  `RecordRepairPassJobTests.Report_*`, not `RecordRepairPassReportTests.*`.
- `Spaarke.Dataverse` is extended by a field of an existing read projection (`ColumnMetadata.ColumnNumber`), not by a read method:
  the existing metadata read already returns the number.

## Main-session record (2026-11-17)

- Live read-only check on dev (operator `az` identity, GETs only; nothing written, no setting changed): an `audit` FetchXML filtered by `objecttypecode` = the logical name returns HTTP 400 (Int32 expected); by the numeric ObjectTypeCode it returns rows. `audit.operation` options 1 Create, 2 Update, 3 Delete, 4 Access, 5 Upsert, 115 Archive, 116 Retain, 117 RollbackRetain. `attributemask` is comma-separated column numbers. The pair columns carry column numbers on both tables. Org auditing is on. These facts drove fix round 1.
- Opus adversarial pass on 1c72db09af..6eb53d821a: F2-1 (auditing off read as no change), K-a..K-e; fixed or recorded in round 1. Sonnet re-check of 6eb53d821a..86cde07a88: no F1/F2; one K test gap (the type column's "not stated" case) closed in the main session after the merge, must-fire shown (the case fails with that check relaxed to `== false`; file restored).
- Merged into the work branch at 53d8d08242 (`task/fm-052d` 7a7a574665).
- Still to do before enabling the pass on any environment: run the report's audit read once read-only on dev end to end (the query shape is confirmed; the job's own read is not yet exercised against Dataverse).
