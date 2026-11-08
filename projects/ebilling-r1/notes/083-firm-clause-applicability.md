# 083: firm clause re-keyed to the invoice's evaluation snapshot

Branch `task/ebilling-083`, cut from `origin/work/ebilling-r1` (contains `d58e3a3554`). Rigor FULL. Step 9.5 review passes are the lead's.

## Contract

`RuleSetService.GetFirmClauseAsync(Guid policyVersionId, Guid invoiceId, CancellationToken ct)` returns `FirmClauseText(Quote, SectionReference, FirmFacingName)`.

A firm sees a clause only for a policy version its invoice's snapshot applied. Order of the checks, each failing closed:

1. Either id empty: no text.
2. The invoice's firm (`sprk_vendororg`) is read app-only; no invoice or no firm: no text.
3. The invoice's own submission (`InvoiceBlockedHold.ReadOwnSubmissionAsync`, the single own-submission rule) is read app-only and its `procedureSnapshot` member read with `ProcedureSnapshot.Read`. Absent, JSON null, unknown version, damaged, no submission: no snapshot, no text. The version must be in `AppliedPolicyVersionIds` (applied on at least one subject); displaced, excluded or unlisted: no text. Nothing is re-resolved.
4. The version, its Billing policy and its rule set (`LoadBillingRuleSetAsync`, which, unlike `LoadRuleSetAsync`, returns a record-scoped set). The set must be Published or Retired.
5. The label: neutral `RecordRuleSet.Name` ("Billing instructions for this record") for a record-scoped set; else the Procedure's firm-facing name (Name when blank) read from the set's `sprk_procedure`; a legacy set (no Procedure) has no label. A Procedure that cannot be read or has no usable name: no text.
6. `RuleSetClauseVisibility.ForFirm` last (visible-to-firms flag; a clause with neither quote nor section is no text; the label goes with the text only).

Any exception other than the caller's own cancellation is caught: Warning log with the exception type only, Debug log with the exception, result `FirmClauseText.None`. No scope switch, and the old "was in force" rule (the 7.2 reader) is gone.

## Decisions (confirm or correct)

- **Rule set status.** The POML's closed list does not name the rule-set status. Kept as a check, but widened from Published to Published-or-Retired: a retired set still holds versions that judged earlier invoices, and the contract says a later change must not change what a firm sees for a judged invoice. Draft or other: no text.
- **Label source.** Taken from the rule set's `sprk_procedure` and scope at read time, not from the snapshot's `resolutions[].rules[]`, so `ProcedureSnapshot.cs` (082d's file) is untouched and no second reader of the member was added. Names are resolved at read time, as the 082d notes say.
- **Legacy sets** (source Legacy, no Procedure) show their clause with `FirmFacingName = null`. The POML does not say; showing no label is the least that names nothing internal.
- **Record rule** label does not read the Procedure at all (tested: no `sprk_procedure` query).
- **Cancellation** propagates when the caller's token is cancelled (no response is produced); every other fault is no text.
- `ForFirm` gained an optional fourth parameter; the three-argument callers compile unchanged.
- Arch guard: `RuleSetService` now names `sprk_findingsjson`, so it is added to `FindingsJsonAppOnlyReadGuardTests.ReviewedTypes` and row 17 to `notes/122-findings-json-field-secure.md`.
- The Procedure read is `RetrieveMultipleAsync` on `sprk_procedure` columns name and firm-facing name only (neither is field-secured).

## POML edits (constraint lines only)

130, 131, 133, 134, 135 were all open in `TASK-INDEX.md` when edited. In each, the 076 round 2 item 5 line now cites the new signature, the snapshot rule, `FirmFacingName`, and keeps the two-firms test; one allow-list DTO line was added after it. 135 also gets its re-keyed content rule (general view through the resolver for the firm and today with no record; per-record view gated by `ComposeForCiamContactAsync` and `AccessibleRecordSet.Contains`; "from the parent record" label) in place of the FR-49 constraint, and states it has no invoice so it does not call `GetFirmClauseAsync`. No "business unit not known" lines existed. Not changed (criteria are out of scope): 135 criteria lines 141 and 142 still describe the old "client default or firm's sets" and "in force today" wording; the 135 owner should re-word them.

## Tests (`RuleSetServiceTests`)

Real `ProcedureSnapshot.Build` snapshots via `ProcedureSnapshotPatchKit`. Contract: applied shows quote, section and label; blank firm-facing name falls back to Name; displaced, excluded and unlisted show nothing (applied-on-some, displaced-on-others shows); flag off shows nothing; nine no-snapshot shapes; no own submission and a foreign linked submission; no firm, no invoice, empty ids; a fault on each of six reads (no throw, log carries no id or name); cancellation propagates; no re-resolve (later version, later Procedure, retired set; only six tables queried); record rule label; legacy; no scope switch; draft, Ontology, unnameable Procedure; negative test that the result, its `ToString` and Info-and-above logs carry no Procedure name, id, override reason or Procedure Version id (the snapshot is seeded with an override reason).

## Seeds (each restored from a backup; `cmp` identical)

- (a) applied check removed (`snapshot is null` only): 3 fail (displaced/excluded/unlisted, no-re-resolve, own-submission).
- (b) flag check broken (`ForFirm` passes `false`): 4 fail (flag-off, `TurningTheFlagOff`, the two `ForFirm` unit tests).
- (c) null snapshot passes through (`snapshot is not null && ...`): 10 fail (nine shapes plus own-submission).

Before-change proof: the new tests do not compile against the old code (`FirmClauseText` has no `FirmFacingName`), and with the same two-Guid signature the old code reads the invoice id as a firm id.

## Gates

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- ArchTests (full): 2159 passed, 0 failed. The first full run failed one test, `InvoiceMoneyColumnsAppOnlyWriteGuardTests` C1 (the service now names `sprk_vendororg`); `RuleSetService` is added to its `Readers` list with the reason (it reads the invoice's firm app-only and writes nothing).
- Filtered unit (`RuleSet|Finance|Procedure`): 6916 passed, 0 failed, 4 skipped. `RuleSetServiceTests`: 165 passed.
- Publish size (Release, framework-dependent linux-x64, `git archive` trees, `Compress-Archive -CompressionLevel Optimal`, 191 files each side): task parent `d58e3a3554` 40,471,966 B to tip 40,473,712 B (**+1,746 B**). No package changed, so no CVE delta. Not measured against a fresh master build here (the 082d notes give the project branch's own delta from master).
