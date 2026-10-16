# Task 045 - Tracking number allocator `{invoice}.{n}`

Status: completed, fix round 1 (owner decision D-87, 2026-10-16). Round 0 built an update-only allocator at attempt bound 5 and stopped on
escalation trigger 2 (see section 6); the owner chose to create the row with its number and to raise the bound to 10. This file describes the
final state, with the round 0 evidence kept as history.

## 1. What was built

| File | Role |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Finance/Submission/ITrackingNumberAllocator.cs` | `CreateSubmissionAsync(submissionId, firmId, firmInvoiceNumber, fields?, ct)`; `TrackingNumberResult`; `TrackingNumberOutcome` (Created / AlreadyCreated / Refused); `TrackingNumberReasons.Unavailable = "ebilling.tracking.unavailable"` |
| `src/server/api/Sprk.Bff.Api/Services/Finance/Submission/TrackingNumberAllocator.cs` | Implementation over `IGenericEntityService`: read the highest n, create the row holding `{number}.{n+1}`, retry only on the alternate-key duplicate |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs` | One scoped, unconditional registration (FinanceModule flag rule) |
| `tests/Spaarke.ArchTests/ADR010_DITests.cs` | `knownOneToOneCeiling` 162 -> 163 on this branch (one new 1:1 interface; see section 5) |
| `tests/integration/data-mutation/FinanceSubmission/TrackingNumberAllocatorTests.cs` | 35 tests over an in-memory table that enforces both keys with the faults measured in dev |
| `tests/integration/data-mutation/FinanceSubmission/TrackingNumberAllocatorLiveTests.cs` | Two dev tests, `[SkippableFact]`: the 10-way create and the redelivered create |

Flow of `CreateSubmissionAsync(submissionId, firmId, firmInvoiceNumber, fields)`:
1. Validate: ids non-empty, number trimmed, non-blank, at most 89 characters; `fields` must not name a column the allocator owns
   (`sprk_invoicesubmissionid`, `sprk_vendororg`, `sprk_firminvoicenumber`, `sprk_submissionsequence`, `sprk_trackingnumber`; any case).
2. Read by submission id. If the row exists: same firm, numbered, same firm invoice number -> `AlreadyCreated` with its number, nothing written;
   anything else -> `InvalidOperationException` (a caller bug; never renumbered, never another firm's number returned).
3. Loop at most `MaxAttempts` (10): read the highest `sprk_submissionsequence` for (firm, number) over rows in every state; create the row
   (`Entity.Id` = the caller's id, the caller's `fields`, the firm, `sprk_firminvoicenumber`, `sprk_submissionsequence`, `sprk_trackingnumber`) in one write.
   - Alternate-key duplicate `0x80060892` (`DataverseServiceClientImpl.IsAlternateKeyDuplicate`): re-read and retry; after the bound return
     `Refused(ebilling.tracking.unavailable)` and create nothing.
   - Primary-key duplicate `0x80040237` (the id is taken): read the row by id; it is the answer (`AlreadyCreated`) or a caller bug (throws); if no row
     answers to the id, rethrow (not a race for a number).
   - Any other fault propagates on the first attempt (a timeout may have created the row; the redelivery finds it by its id).

### Placement Justification (CLAUDE.md section 10)

In the BFF, `Services/Finance/Submission`, beside the submission operation (044) that calls it. A synchronous library, not a host: no endpoint, no
background work (ADR-052 not applicable), no AI dependency, no new package. Uses `IGenericEntityService` (Spaarke.Dataverse), which the Finance
services already use. Single server-side owner of the tracking-number invariant and the only creator of `sprk_invoicesubmission` rows
(ADR-002 WP-1).

### Component justification (CLAUDE.md section 11), from the POML

1. Existing: `RecordCreationService.CreateNumberedAsync` (autonumber + alternate key) and `DataverseRegistryConcurrencyStore` (row guard); neither
   yields a per-(firm, invoice number) sequence.
2. Extension: autonumber cannot be scoped per firm and invoice number, so `CreateNumberedAsync` is not reusable as-is; its retry rule and
   `IsAlternateKeyDuplicate` are reused unchanged.
3. Cost of doing nothing: FR-10 fails (resubmissions cannot get `{invoice}.{n}`) and parallel month-end submissions (NFR-07) could share a number.

The interface is the one new 1:1 seam (ADR-010 ceiling +1): the boundary 044 fakes in its tests. Falsifiable: if 044's tests do not fake it, register
the concrete and drop the ceiling back to 162.

## 2. Idempotency shape (the owner left it to me)

Chosen: **the caller's submission id is the new row's id.** 044 already mints a submission id before upload and uses it as the job's idempotency key
(ADR-004 / ADR-017), so the same value serves as the create's idempotency key and no new column is needed. A redelivered create finds the row by
that id (read first; or, when two calls with one id race, the primary key refuses the second and it reads the row). Measured in dev: Dataverse accepts a
client-chosen id on create through `DataverseServiceClientImpl.CreateAsync`, and refuses a taken id with `0x80040237` "Cannot insert duplicate key",
checked **before** the alternate key (a repeat of id and number raises the primary-key fault), and not matched by `IsAlternateKeyDuplicate`.

Rejected:
- A separate idempotency-key column (a hash or a header value): a schema change plus a lookup on a new column; the owner ruled schema changes out and
  the submission id already exists.
- Looking the row up by (firm, firm invoice number, document): a resubmission of the same document with the same number is a legitimate new
  submission (D-37), so the lookup would swallow it.

Consequence for 044: mint the submission id once per request, before the upload, and pass the same id to the allocator and to the job. A new request
(a user's second submission of INV-002) mints a new id and gets INV-002.2; a redelivery of the first request reuses the id and gets INV-002.1 back.
On `AlreadyCreated` the existing row stands: the redelivery's `fields` are not applied.

## 3. Deviations from the POML and the round 0 design

1. **Create, not update (D-87).** The round 0 update-only path (read the row, number it) is gone.
2. **Bound 10 (D-87).** k parallel callers need at most k attempts (a caller loses an attempt only to another caller's committed number), so 10 covers
   the NFR-07 ten-way burst. An eleventh simultaneous contender can be refused (section 4).
3. **Test path.** Tests live under `tests/integration/data-mutation/FinanceSubmission/` (ADR-038 KEEP path, optimistic concurrency), not
   `tests/unit/`; compiled into `Sprk.Bff.Api.Tests` by the csproj glob.
4. **Result type** carries an outcome (Created / AlreadyCreated / Refused) instead of a bare string, so a redelivery is distinguishable.
5. **`fields` guard.** The allocator rejects a caller-supplied owned column (ADR-002: a number set without the sequence behind it).
6. **89-character guard.** `sprk_trackingnumber` is 100 characters; 100 - 1 (dot) - 10 (int digits) = 89. Callers validate at intake against
   `TrackingNumberAllocator.MaxFirmInvoiceNumberLength`.
7. The round 0 own-row exclusion in the highest-sequence read is removed: with no update path the submission's own row cannot be there before it is
   created, and after a redelivery the read-by-id answers first.

Tests beyond the stated contract, one line each (acceptance criterion 6):
- 89-char number with a 10-digit sequence fills the 100-char column: proves the 89 figure is exact.
- Log hygiene (ADR-015): the firm invoice number and tracking number appear only at Debug.
- Redelivery races (two tests): the id-taken path through the primary key, which Dataverse checks first.
- Primary-key duplicate with no row: pins the `throw;` so it cannot turn into an endless retry.
- Owned-column guard (six names, one in another case) and empty ids: the fail-closed contract of the `fields` parameter.
- Repeated number is never refused (D-88): three arrivals get .1 .2 .3.
- 10-way in-process parallel with a first-read barrier: proves the key, not the read, decides the winner, and nothing is doubled.
- Trim / case-insensitive match: the key is case-insensitive (measured), so the sequence must be too.

## 4. Evidence

### Key metadata and fault codes (dev `https://spaarkedev1.crm.dynamics.com`, 2026-10-16)
Alternate key `sprk_invoicesubmission_firm_trackingnumber` on (`sprk_trackingnumber`, `sprk_vendororg`), Active; it ignores case.
`sprk_trackingnumber` is SystemRequired. Create with a client id: returned id equals requested id. Taken id (same or different number):
`0x80040237` "Cannot insert duplicate key." Alternate key: `0x80060892` "Entity Key Firm and Tracking Number violated ..." (names attributes, not values,
so the allocator's error log carries no business data).

### Build and tests (Debug)
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- Allocator unit tests: 35 passed, 0 failed (2 live tests skipped without `SPAARKE_LIVE_TRACKING_DATAVERSE_URL`).
- `Sprk.Bff.Api.Tests` filtered to allocator / Finance / DI: 369 passed, 0 failed, 2 skipped.
- `tests/Spaarke.ArchTests`: 1052 passed, 0 failed.
- A full `Sprk.Bff.Api.Tests` run was attempted in round 0 on a machine loaded by about ten concurrent agent sessions and showed only load-induced
  failures (web-host tests timing out after 3+ minutes); seven of the failing classes re-run alone passed (65 of 65). A clean full run was not obtained;
  CI runs it.

### Mutation proof (no old file exists for the rewritten behaviour, so each rule was broken in turn; the allocator suite had to fail)
Bound 9; bound 11; no read-before-create; primary-key duplicate not handled; owned columns accepted; catch-all instead of the key classifier; no firm
filter on the sequence read; state filter on the sequence read; firm not written; no trim; reconcile ignores the firm; reconcile ignores the number;
tracking number logged at Information; stale increment instead of re-read; wrong primary-key code. All 15 made at least one test fail (1 to 7 each);
the source was restored and re-verified.

### Live, dev: create path (rows `ZZTEST-045`, all deleted after each run, 0 left every time)

| Run | Callers | Bound | Result | Attempts per caller | Duplicates |
|---|---|---|---|---|---|
| 1-5 | 10 | 10 | 10 created, 0 refused in every run; sequences 1..10 | highest per run: 9, 9, 8, 10, 10 | 0 |
| 5 | 12 | 10 | 11 created, 1 refused (`ebilling.tracking.unavailable`); sequences 1..11 | 1,2,2,3..9,10,10 | 0 |
| redelivery | 1 sequential + 4 racing on one id | 10 | sequential: Created then AlreadyCreated (same number); racing: 1 Created, 3 AlreadyCreated, one number; 2 rows in total | n/a | 0 |

(Run 1 went red on a test bug, `OnlyContain` on an empty refused list, with 10 created and 0 refused; the assertion was removed and runs 2-5 passed.)
Read as: bound 10 gives every caller of a ten-way burst a number; the 12-way run shows the documented limit (a burst larger than the bound can be
refused whole, nothing written); a redelivered create mints no second row or number.

### Round 0 history (update path, bound 5; the evidence that fired escalation trigger 2)
Ten simultaneous callers: 5 allocated and 5 refused in 3 of 3 runs, 0 duplicates. 2, 3 and 5 callers all succeeded. Cause: lockstep retries, one winner
per round. Jitter (0-300 ms, 0-150 ms x attempt) at bound 5 still gave 5-6 of 10; bound 10 without jitter gave 10 of 10. The owner chose bound 10.

### Publish size
Round 0 (framework-dependent linux-x64 `dotnet publish -c Release`, `Compress-Archive -CompressionLevel Optimal`, short-path worktrees): fresh master
`a8c3182ec` 192 files 37.15 MB; task base `70bfb7586` 192 files 37.19 MB; branch `fb52d02cc` 192 files 37.19 MB. Delta +0.04 MB vs master (0.00 vs
base; the 0.04 is other branch content). Fix round 1 rewrites the same two source files (about 100 lines), adds no package, project reference or asset,
and was not re-published; the delta stays far below the +5 MB threshold. No `.csproj` change, so no new package and no new CVE.

## 5. ADR-010 ceiling

`knownOneToOneCeiling` 162 -> 163 on this branch (+1: `ITrackingNumberAllocator -> TrackingNumberAllocator`), verified in round 0 (at 162 the test fails and
names exactly this pair). The project branch `work/ebilling-r1` is already at 165 after tasks 046 (+1) and 047 (+2), so on merge this task's line must
become **166** (165 + 1), with the comment block kept.

## 6. Escalation record

Trigger 2 (10-way exhaustion) fired in round 0 and was presented. Owner decision D-87 (2026-10-16): create-with-number, bound 10, and a statement that
the tracking number is never blank. Done; the 10-way and redelivery evidence is in section 4.

## 7. Open items for other tasks and docs

1. **044 must have the firm invoice number at first save.** The number is `{firm invoice number}.{n}` and cannot be blank (D-87), so the submission form
   and both routes must require the firm invoice number; the allocator throws `ArgumentException` for a blank one. 044's POML still carries the old
   text ("allocate through 045 when the firm invoice number is supplied; otherwise return null (049 allocates after extraction)", goal "trackingNumber or
   null", acceptance "non-null when a firm invoice number was supplied"), which contradicts its own D-87 constraint. If a submitter can legitimately
   upload a PDF without typing the number, the owner has to say what the tracking number is then.
2. **049's POML contradicts D-87** at its "tracking" constraint ("If the submission has no tracking number, allocate it through 045 ..."), beside its D-87
   constraint ("The job does not allocate"). The allocator no longer has an allocate-onto-existing-row method, so that line cannot be implemented.
3. **044 mints the submission id** before the upload and passes the same id to the allocator and to the job (section 2), and passes every other
   column through `fields` (never the five owned columns).
4. **Invariant registry gap.** `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` section 5 has no row for the tracking-number invariant.
   Proposed row: invariant "a submission's tracking number is `{firm invoice number}.{n}`, unique per firm, set at create"; owner
   `TrackingNumberAllocator`; guard alternate key (`sprk_vendororg`, `sprk_trackingnumber`) plus the primary key for redelivery; fix-up none (rows are never
   renumbered). Not edited here (docs outside this task's scope).
5. **Length at intake**: validate against `TrackingNumberAllocator.MaxFirmInvoiceNumberLength` (89).

## 8. Known limits

- A numbered submission that is later deleted frees its n; the next submission of that invoice number takes it. No eBilling flow deletes submissions.
- A tracking number held by a row the sequence cannot see (a manually entered row with no sequence, or another invoice number column) makes every
  attempt collide; the allocator exhausts its attempts and refuses (fail-closed, nothing created).
- More than ten simultaneous creates for one (firm, firm invoice number) can be refused (the 12-way run: 11 created, 1 refused). The caller may try again.
- `DataverseServiceClientImpl.CreateAsync` logs one error per refused create, so a retried create leaves one such error per lost race.
- A timeout that did create the row is recovered only by the caller redelivering with the same submission id.

## 9. Quality gates (Step 9.5)

Fix-round scope: the allocator, its interface, the two test files, the ADR-010 comment and the FinanceModule comment.
code-review: no F-class finding. Looked at and accepted: a read-by-id before every create (one cheap query on the common path, and it is what makes a
redelivery write nothing); constructor null guards (repo idiom, 37 in `Services/Finance`); the interface as a single-implementation seam (ceiling raise,
section 5). adr-check, ADRs considered: 001/052 (no background work: n/a), 002 (compliant: single owner; fail closed on a foreign or unnumbered row;
registry row missing: item 4), 007 (no Graph types), 008 (no endpoint), 009 (no cache), 010 (ceiling +1, justified), 013 (no AI), 015 (compliant; the
log-hygiene test), 028 (no auth change), 032 (unconditional registration), 038 (tests under a KEEP path). Metrics: `TrackingNumberAllocator.cs` about 290
lines, 2 public / 8 private methods, 2 constructor parameters, one job (create a numbered submission) - cohesive. The test file (about 730 lines) is one
class under test plus the in-memory table that stands in for the two keys - cohesive; if 044 needs the same table it should move to a shared fake then.

rework: fix-round-1 task 045
