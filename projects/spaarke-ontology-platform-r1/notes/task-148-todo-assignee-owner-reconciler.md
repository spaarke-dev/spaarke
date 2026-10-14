# Task 148: follow the assignee — TodoAssigneeOwnerReconciliationJob (2026-10-14)

Branch `task/ontology-148-assignee-reconciler` (worktree `C:\wts-148`). Base `docs/ontology-platform-design` at `aeaaed68f`, then `a14349173` (merged before the PR)
(includes #1834 / task 143 and the DI-nn rename). Rigor FULL (bff-api, security, job, dataverse, testing).

## What changed

- **`TodoAssigneeOwnerReconciliationJob`** (new, `Services/Signals/`): an ADR-036 `IScheduledJob` on the shared host, cron
  `*/5 * * * *`. It keeps every Signal and Decision Record about a **personal** To Do (filed under nothing, K2) owned by the
  D-134 answer for the To Do's **current** state:
  - the business-unit team of its Assigned To's one eligible user (`PersonalToDoAssignee` → `ForUserTeam`);
  - otherwise the To Do's own team (the resolver's answer with the To Do as primary parent).
- **Two passes (uac-r2 F2):**
  - Every run takes the To Dos modified since the instance watermark, plus the To Dos an earlier run left unsettled.
  - Nightly (the first run at or after 02:00 UTC, or an admin trigger with `pass=full`), every row whose
    `sprk_regardingtodo` is set is recomputed. A cache cursor carries the pass across runs.
  - Page cap: 500 rows per page, 10 pages per run.
- **Live filing decides (uac-r2 F1):**
  - A To Do filed under a core record → `SkippedCored` (the re-file rule's).
  - A To Do filed under a non-core record is not personal and keeps its own team (D-135 K2). Its rows take the To Do's own
    team, the answer the writers give them. This is counted once per To Do as `filedNonCore`, and a row the assignee pass
    moved earlier comes back.
  - A core that cannot be derived for a data reason (`CoreAncestorResult.IsPermanent`) → `SkippedRefused`, not carried.
  - A row with a live `sprk_regarding{matter,project,workassignment,servicerequest}` stamp → `SkippedCored`.
  - `sprk_corerecordid` / `sprk_corerecordtype` are never read.
- **Secure wins (D-134 item 7):**
  - `ForParents` takes the To Do first, then the row's lineage ownership-parent lookups, and for a Decision Record the
    subjects of the Signals it resolved.
  - A secure answer → `SkippedSecure`; a refusal → `SkippedRefused`.
  - Some refusals come from configuration, not data (`SecureBusinessUnitAmbiguous`, `SecureOwnerTeamUnresolved`,
    `NoDefaultOwnerTeam`). These are also counted as `configurationRefusals` and make the run partial.
  - A Decision Record whose Signal columns were not probed is not decided (`unverified`). It is never written past
    resolved Signals it could not check.
  - A row owned by a Secure-BU team (`IsSecureOwnerTeamAsync`) is never touched; a user-owned row → `SkippedUserOwned`.
- **One write path (uac-r2 F3):** `SecureChildReconciler.ReownOrdinaryRowAsync`. Outcomes map as the POML states:
  - Conflict carries the To Do to the next run and does not fail this one.
  - A Failed that is Dataverse's privilege refusal is `AccessDenied` (`IsPrivilegeRefusal`). On the reconciler's Web API path
    that is the text "403 (Forbidden)", because `UpdateIfMatchAsync` ends in `EnsureSuccessStatusCode` and drops the body's
    0x80040299. Code or "Privilege Check" text also counts (`SignalWriter.IsAccessDeniedMessage`). It is held back 24 h through
    an idempotency marker: a defined outcome, never a retry storm.
- **Report-only** unless `Ontology:TodoAssigneeOwner:WritesEnabled` is exactly `true`. The would-be re-owns are logged and
  listed in `sampleStale` (`table:id:from->to`, at most 200).
- **Fail closed:**
  - A listing or row-scan fault → status `error`.
  - A To Do whose assignee, core or owner cannot be read → `unverified`, run `partial`. Nothing is written for it, and it is
    carried to the next run.
  - A partial run says why in `ErrorMessage` (ADR-036), not only in the `ResultJson` counts.
  - A page bound on one To Do batch decides the rows read; the rest wait for the nightly recompute.
  - `ProcessedItems` = re-owns.
- **`PersonalToDoAssignee.ClassifyAsync`** (new): the same decision as `ResolveAsync` (which now delegates to it), with the
  reason there is no single user. That reason drives `FallbackNoAssignee` / `FallbackAmbiguous` / `FallbackDisabled`. The
  existing callers (SignalWriter, DecisionCommitService) see no behaviour change; their suites pass unchanged.
- **Registration:** one `AddScheduledJob` line in `SignalsModule`.
- **Census:** `RecordOwnerAssignmentCensusTests.OrdinaryReownCallers` lists every `ReownOrdinaryRowAsync` call site:
  `SignalWriter.ReassignNoCoreOwnerAsync` and this job's `RepairAsync`. Unlisted, count-changed and stale entries fail, with a
  negative control. **This file is uac-r2's; the PR asks for their review.**

## Design decisions

1. One class, two cadences. The 5-minute run carries the nightly pass, so there is no second job id or admin surface.
2. The watermark and the carried set live in instance memory. After a restart, or on another instance, the first run looks
   back 6 h, and the nightly pass covers anything older. Scale-out is safe: every write is claimed per row and target
   (idempotency) and is conditional in UAC (If-Match).
3. The expected owner mirrors the writers (SignalWriter / DecisionRecordWriter `OwnershipContextOf`). The question is the
   parents with the To Do as primary, secure-if-any, and then the assignee team only when that answer is ordinary.
4. In the F1 reverse case, a row that still carries a live core stamp is skipped as cored. This assumes task 144's re-file rule
   clears row stamps on un-filing; until then such a row stays with its last owner and is counted.
5. A full-pass fault on one table does not stop the other table (the scaffold's idiom). It still makes the run `error`.

## Fix round (two adversarial passes, opus)

Pass A reviewed `e43fa8201`. Pass B reviewed the same commit independently, read from git so it never saw the working tree,
and agreed with every pass A finding.

| # | Finding | Class | Disposition |
|---|---|---|---|
| A1 | A privilege refusal never reached AccessDenied. The reconciler's PATCH throws a bare `403 (Forbidden)`, so a missing role was retried every 5 minutes as `RepairFailures`. | F1 | **Fixed.** `IsPrivilegeRefusal` also reads the 403 text. The same misclassification in `SignalWriter.ReassignNoCoreOwnerAsync` (metering only) is pre-existing. It is filed as #1855, not changed here, because the K1/K2 lane owns that classification. |
| A2 | A failed `sprk_signal` probe skipped the Decision Record's resolved-Signal secure widening in the nightly pass, so the record could be written. | F1/F2 | **Fixed.** `ResolvedSignalSubjectsAsync` throws without the probe, so the record is `unverified` and not written. A new test covers it. |
| A3 | A To Do filed under a non-core record was skipped, stranding rows the assignee pass had moved. D-135 K2 says it "keeps its own team". | F1 | **Fixed.** Its rows take the To Do's own team (`filedNonCore`). A new test covers it. |
| A4 | The AccessDenied test double threw a message production never produces. | F3 | **Fixed.** It throws the real client's 403 `HttpRequestException`. |
| A5 | `SkippedRefused` and a parent-resolver fault were not asserted. | F3 | **Fixed.** A new refusal theory (missing parent, ambiguous Secure unit) and a `parent-read` fault case. |
| B1 | A partial run returned `Success=false` with no `ErrorMessage` (ADR-036). | F2 | **Fixed.** One summary line is added; the fault theory asserts it. |
| B2 | Configuration refusals (ambiguous Secure unit, unresolved Secure team, no default team) were counted `SkippedRefused` on an `ok` run, so the job could do nothing indefinitely and look healthy. | F2 | **Fixed.** `configurationRefusals` makes the run partial. Tested with an ambiguous Secure unit. |
| A6 | A permanent core-derivation error was carried, and the run stayed partial, forever. | K2 | **Fixed** (cheap): refused, not carried. |
| A7 | A batch that always overflows the page bound was never decided by either pass. | K2 | **Fixed** (cheap): the rows read are decided. |

Every new or changed test fails against the pre-fix job (7 of 26) and passes after (26/26).

### Re-check of the fix diff (sonnet, scoped to `73a2b3599`)

- Confirmed that A1, A2, A3, A6, A7, B1 and B2 close their findings.
- Confirmed that A3 opens no hole: the secure guards still run, and a cored To Do still skips.
- Confirmed that A7 still excludes a faulted batch.
- Confirmed that the ErrorMessage line is never added to a success run.
- It raised three items:
  - **F3:** A6 and A7 had no test. **Fixed:** `PermanentCoreRefusal_IsRefused_NotCarried` and
    `PageBound_DecidesTheRowsRead_RunIsPartial`. Each fails when its fix is reverted (1/30) and passes with it.
  - **F3 (low):** no secure negative for the non-core branch. **Fixed:** the `secure-parent-non-core` and
    `secure-owned-non-core` cases.
  - **K:** a stale counter name in `148-report-only-run.md`. **Fixed.**
- Accepted as K:
  - a 403 for a reason other than a missing role is also held 24 h, but it is still visible (partial plus ErrorMessage);
  - a standing data refusal (`ParentUnresolved`, a permanent core) shows only as a count and a warning, per B2's closed list.

### uac-r2 review of #1858 at `20e6a0bcf` (clean, no F); two known limits fixed before merge

Both affect the report-only output the owner will use to decide on writes.

- **uac-r2 K1.** The Secure guard recognised only the one named Secure owner team, so a row owned by a per-family team in the
  Secure BU showed in `sampleStale` as a would-be re-own that `ReownOrdinaryRowAsync` would refuse. **Fixed:**
  `IsOwnedInSecureBusinessUnitAsync` applies the reconciler's own guard before a row is listed stale. The row's
  `owningbusinessunit`, or its owning team's business unit, equal to the Secure BU skips it. The Secure BU is identified by
  reusing `SecureChildShareSynchronizer.ResolveSecureBusinessUnitAsync`, the reconciler's identification. An ambiguous Secure BU
  leaves the row not decided. The named-team check stays. Test: `SecureBusinessUnitTeamRow_IsSkipped_NotListedStale`.
- **uac-r2 K4.** A non-configuration refusal of the assignee's team (including an assignee in the Secure BU) counted
  `skippedRefused` on an `ok` run. **Fixed:** `assigneeTeamRefusals` makes the run partial, with a reason in `ErrorMessage`. Test:
  `AssigneeTeamRefused_RunIsPartial_WithAReason`.
- Both tests fail against `20e6a0bcf` and pass after (32/32). The related suites pass 2072/2072, and the BFF builds with 0 warnings.
- Left as known limits, per uac-r2: their K2 (the per-instance watermark and carried set, B3 below) and their K3 (the census regex
  misses method-group calls, A10 below).

### Known limits (recorded, not fixed)

- **K1 — load (A8, B4).** One table failing its scan keeps the full pass due all day, and a table without a cursor is rescanned
  from the top while the other is mid-scan. A full pass costs about 2-6 reads per distinct To Do, on the shared BFF identity.
  Measure it in the dev report-only run; a per-table done marker and a per-run budget are the fix if needed.
- **K1 — the carried set and the watermark are per instance (B3).** On scale-out, or after a restart, a held-back To Do waits
  until its instance runs again, with the nightly pass as backstop. One instance today. The fix, when wanted, is the carried
  set in `IDistributedCache`.
- **K2 — the scan cursor moves before its rows are decided (B5).** Inherited from the scaffold. A cancelled run skips those
  rows until the next day's pass.
- **K2 — the done-day is taken from the finishing run (B6).** An admin `pass=full` or a continuation between 00:00 and 02:00
  marks the new day done.
- **K2 — the carried set grows while a table's probe fails (B7).** The run is loudly `error` meanwhile.
- **K3 — watermark continuation (A9).** A To Do with an id below the continuation cursor, modified during the continuation, waits
  for the nightly pass. So does a row whose own stamp is cleared with the To Do unchanged.
- **K4 — census guard (A10).** The regex does not see a method-group use, and the negative control does not exercise the
  unlisted or count-changed paths.

## Overlap with the 143 K1/K2 lane (`C:\wts-143k`)

The job reuses the base's `SignalWriter.IsAccessDeniedMessage` to classify 0x80040220 / 0x80040299. It does not touch
`SignalEvaluationTriggerHandler.cs` or `SignalReevaluationJob.cs`. If the K1/K2 lane moves or renames that classifier, this
job's single call site follows it.

## Component justification (CLAUDE.md §11)

- **Existing:**
  - `SignalReevaluationJob` re-owns only open hit Signals, nightly, and never a Decision Record.
  - `CoreAncestorStampReconciliationJob` and `SecureChildReconciliationJob` belong to uac-r2.
  - None of them re-owns rows to a To Do's current assignee.
- **Extension:**
  - A pass inside a UAC job would put ontology ownership rules in UAC files.
  - A pass in the nightly Signal job would miss the Decision Record half and the 5-minute cadence.
  - The new class reuses the scaffold, the scheduler host, the admin surface and UAC's re-own entry point.
- **Cost of doing nothing:** after an Assigned To change on the client form (no BFF hook), or the assignee's BU move, a
  personal To Do's Signals and Decision Records stay with the old team indefinitely (D-135 item 2).
- `ClassifyAsync` is an extension of the existing helper, not a new component. Without it the job cannot count its
  fallbacks per reason (a POML criterion), or tell a filed To Do from a missing one, without a second read pattern.

## BFF hygiene (§10)

- **Placement (ADR-052):** the job runs in the BFF on the in-process `Spaarke.Scheduling` host, beside the writers whose rule
  it re-applies. It uses BFF-owned tables and the BFF identity, at low volume. No new scheduler, store, package, identity or
  endpoint.
- **Publish size:** fresh Release publishes from short paths, measured after merging the new base (`a14349173`, the
  Pkcs/Xml 10.0.13 bump, so the bump is not counted as 148's):
  - base (`C:\wts148b` at `a14349173`): 192 files, 129,034,836 bytes;
  - head (`C:\wts-148`): 192 files, 129,125,900 bytes;
  - delta: **+91,064 bytes (+0.09 MB)**. The file count and file list are identical.
- **CVE:** no package changed. On the new base, `dotnet list package --vulnerable --include-transitive` reports none. Before the
  bump, `System.Security.Cryptography.Pkcs` 10.0.11 (HIGH) was reported identically on base and head (tracked as #1826).

## Verification

- New tests: `tests/integration/data-mutation/Signals/TodoAssigneeOwnerReconciliationJobTests.cs`, **32/32 pass** (22 first
  round, plus the fix round, the re-check and uac-r2's K1/K4). The real resolver, reconciler and `CoreAncestorResolver` run over
  `SecureChildShareWorld`.
- Fix round: the 7 new or changed tests fail against the pre-fix job (`e43fa8201`) and pass after.
- Mutation proofs, re-run on the fixed job, each restored afterwards:
  - removing the secure-wins guard (`Expected` → `IsSecureOwner`) fails 2/26 at the fix round (`secure-parent`, `record-resolved-secure-signal`);
  - treating a filed To Do as personal (the live-filing guard: Filed → assignee) fails 2/26 (`todo-under-matter`, and the
    K2 non-core case).
- `Spaarke.ArchTests`: **974/974** before the base merge. After the merge, the census and secure-family consistency set passes
  **38/38**.
- Related suites (`TodoAssigneeOwner|Signals|SecureChild|RecordOwnership` filter): **2066/2066 pass** after the merge.
- BFF build: 0 warnings.
- **Tests beyond the POML list (justified):**
  - The AccessDenied hold-back test, required by the brief's rule that "0x80040299 is a defined outcome, never a retry storm".
  - The Signal-probe fault test and the configuration-refusal case, which cover review findings A2 and B2.
- **Not run:** a dev report-only run. The BFF with this job is not deployed to dev (D-126); see `notes/148-report-only-run.md`.
  No Dataverse writes were made.

## Owner / uac-r2 decisions open

- Turn writes on (`Ontology:TodoAssigneeOwner:WritesEnabled=true`), after a dev report-only run that uac-r2 has reviewed.
- F2 (0x80040299): business-unit default teams lack Read on `sprk_signal`. Until that is decided, every live re-own comes back
  `accessDenied` (held back, not retried every tick).
- Live gate (UAC 183 K2): one stale-ETag 412 → Conflict on each table, once writes are on.
- uac-r2 review of the census entry and of the shape.
