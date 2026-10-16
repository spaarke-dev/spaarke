# Task 045 - Tracking number allocator `{invoice}.{n}`

Status: implementation and tests done; **escalation trigger 2 fired** (10-way contention exhausts the 5-attempt bound). The bound was not
raised. POML status stays `in-progress` until the owner decides (section 4).

## 1. What was built

| File | Role |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Finance/Submission/ITrackingNumberAllocator.cs` | Interface, `TrackingNumberResult`, `TrackingNumberOutcome` (Allocated / AlreadyAllocated / Refused), `TrackingNumberReasons.Unavailable = "ebilling.tracking.unavailable"` |
| `src/server/api/Sprk.Bff.Api/Services/Finance/Submission/TrackingNumberAllocator.cs` | Implementation over `IGenericEntityService` (read highest n, update the row, retry only on `0x80060892`) |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs` | One scoped registration, unconditional (ADR-032 / FinanceModule flag rule) |
| `tests/Spaarke.ArchTests/ADR010_DITests.cs` | `knownOneToOneCeiling` 162 -> 163 (one new 1:1 interface) |
| `tests/integration/data-mutation/FinanceSubmission/TrackingNumberAllocatorTests.cs` | 24 tests (in-memory store behind a strict `Mock<IGenericEntityService>` that enforces the key) |
| `tests/integration/data-mutation/FinanceSubmission/TrackingNumberAllocatorLiveTests.cs` | Dev concurrency test, `[SkippableFact]` |

Flow of `AllocateAsync(submissionId, firmId, firmInvoiceNumber)`:
validate ids -> trim/validate number -> read the row -> firm check (row's `sprk_vendororg` must equal `firmId`) -> if the row already has a
sequence, return `AlreadyAllocated` (same number) or throw (different number) -> loop up to `MaxAttempts` (5): read the highest
`sprk_submissionsequence` for (firm, number) excluding this submission, write `{number, n+1, "{number}.{n+1}"}` in one update, catch only
`DataverseServiceClientImpl.IsAlternateKeyDuplicate` -> after the bound return `Refused(ebilling.tracking.unavailable)` with nothing written.
Any other fault propagates on the first attempt.

### Placement Justification (CLAUDE.md section 10)

In the BFF, `Services/Finance/Submission`, beside the submission operation (044) and the submission job handler (049) that call it. It is a
synchronous library, not a host: no endpoint, no background work of its own (ADR-052 not applicable), no AI dependency, no new package. It
uses `IGenericEntityService` (Spaarke.Dataverse), which the Finance services already use. Single server-side owner of the tracking-number
invariant (ADR-002 WP-1).

### Component justification (CLAUDE.md section 11), from the POML

1. Existing: `RecordCreationService.CreateNumberedAsync` (autonumber + alternate key) and `DataverseRegistryConcurrencyStore` (row guard);
   neither yields a per-(firm, invoice number) sequence.
2. Extension: autonumber cannot be scoped per firm and invoice number, so `CreateNumberedAsync` is not reusable as-is; its retry rule and
   `IsAlternateKeyDuplicate` are reused unchanged.
3. Cost of doing nothing: FR-10 fails (resubmissions cannot get `{invoice}.{n}`) and parallel month-end submissions (NFR-07) could share a number.

The interface is the one new 1:1 seam (ADR-010 ceiling +1): it is the boundary 044, 049 and 104 fake in their tests. Falsifiable: if neither 044 nor
049 fakes it by 049, drop the ceiling back to 162 and register the concrete.

## 2. Deviations from the POML (and why)

1. **Test path.** POML output named `tests/unit/.../Services/Finance/Submission/`. Moved to `tests/integration/data-mutation/FinanceSubmission/`
   (ADR-038 / TEST-ARCHITECTURE: new tests live under a KEEP path; this class is optimistic-concurrency, i.e. data-mutation). It is compiled into
   `Sprk.Bff.Api.Tests` by the csproj glob, so `dotnet test` of that project still runs it.
2. **Update-only write shape.** One method `AllocateAsync(submissionId, ...)` on an existing row, used by both 044 and 049. A create-with-number
   path was not added: `sprk_trackingnumber` is SystemRequired (section 5), so the row must exist before it is numbered whichever caller creates it.
3. **Also writes `sprk_firminvoicenumber`** in the same update, so number, sequence and tracking number cannot disagree and the highest-sequence
   read (which filters on `sprk_firminvoicenumber`) sees the row.
4. **Result type** carries `Outcome` (Allocated / AlreadyAllocated / Refused) instead of a bare string, so a redelivered job is distinguishable
   from a fresh allocation. "Numbered" means `sprk_submissionsequence` is non-null (callers test the sequence, not the tracking number).
5. **Own-row exclusion** in the highest-sequence read: a concurrent call for the same submission (a redelivered job) that wrote between this
   call's row read and this read would otherwise be counted and the row renumbered n+1 with a gap. Found by review of my own code; test added.
6. **89-character guard.** `sprk_trackingnumber` is 100 characters; 100 - 1 (dot) - 10 (int digits) = 89. A longer number throws `ArgumentException`
   (`MaxFirmInvoiceNumberLength`); callers should validate at intake against the same constant.

Extra tests beyond the stated contract, one line each (acceptance criterion 6):
- 89-char number with a 10-digit sequence fills the 100-char column: proves the 89 figure is exact, not a guess.
- Log hygiene (ADR-015): the firm invoice number and tracking number appear only at Debug.
- Self-race (`ARedeliveredCallThatRacesItsOwnEarlierWrite...`): pins deviation 5.
- 10-way in-process parallel with a first-read barrier: proves the key (not the read) decides the winner and refusals write nothing.
- Idempotent repeat (two spellings), firm mismatch, no firm, different held number, blank number: the fail-closed edges of deviations 4 and 6.
- Trim / case-insensitive match: the key is case-insensitive (measured), so the sequence must be too.
- Bound Theory (4 duplicates then success; 5 duplicates then refusal): pins the bound at exactly 5 on both sides.

## 3. Evidence

### Key metadata (dev, 2026-10-16, escalation trigger 1 - did not fire)
Alternate key `sprk_invoicesubmission_firm_trackingnumber` on (`sprk_trackingnumber`, `sprk_vendororg`), Active. Measured: it refuses a duplicate
on UPDATE as well as create (`0x80060892`); it ignores case; `sprk_trackingnumber` is SystemRequired (create without it fails `0x80040200`).

### Build and tests (after the test-file move, Debug)
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- Allocator tests: 24 passed, 0 failed, 1 skipped (the live test, Skipped without `SPAARKE_LIVE_TRACKING_DATAVERSE_URL`).
- `Sprk.Bff.Api.Tests` filtered to Finance / DI: 358 passed, 0 failed, 1 skipped.
- `tests/Spaarke.ArchTests`: 1052 passed, 0 failed.
- Full `Sprk.Bff.Api.Tests` run: stopped after about 40 minutes with 40+ failures, all in `WebApplicationFactory` / metrics-listener tests
  (`TaskCanceledException ... client aborted the request` after 3+ minutes) while about ten other agent sessions were running test suites on the same
  machine. A sample of the failing classes (ContentSafetyAuthOutcome, DocumentEmailIdentityContract, CorsAndAuth, RateLimiterPipelineContract,
  ChatMessageStreamingContract, WorkspaceLayoutEndpointContract, OfficeQuickCreateProjectContract) re-run alone: 65 passed, 0 failed. The host builds
  with the new registration. A clean full run on an idle machine was not obtained; the CI Router runs it.

Mutation proof (no old file exists for a new class, so each behaviour was broken in turn and the suite had to fail): drop firm filter; bound 6;
bound 4; catch-all instead of the key classifier; drop idempotency; drop firm check; log the number at Information; add a state filter to the
highest-sequence read; stale increment instead of re-read; no trim; drop own-row exclusion. All 11 made at least one test fail.

### Live 10-way contention (dev, `https://spaarkedev1.crm.dynamics.com`), rows `ZZTEST-045`, all deleted after each run

| Callers | Bound | Result | Duplicates |
|---|---|---|---|
| 2, 3, 5 | 5 | all allocated | 0 |
| 10 (3 runs) | 5 | 5 allocated, 5 refused (`ebilling.tracking.unavailable`) each run | 0 |
| 10 | 10, no jitter | 10 allocated, 0 refused; attempts 1..10; sequences 1..10 | 0 |
| 10 (3 runs) | 5, jitter 0-300 ms | 5 / 5 / 6 allocated | 0 |
| 10 (3 runs) | 5, jitter 0-150 ms x attempt | 5 / 5 / 6 allocated | 0 |

Cause: contenders re-read at nearly the same moment and retry in lockstep, so each round has one winner (the key refuses all others whole) and
the k-th contender needs k attempts. Jitter did not change this in dev. Correctness held in every run (no duplicate number, refusals wrote
nothing, cleanup left 0 `ZZTEST-045` rows); only the 10-way availability criterion fails.

### Publish size (framework-dependent linux-x64 `dotnet publish -c Release`, `Compress-Archive -CompressionLevel Optimal`, short-path worktrees)
| Build | Commit | Files | Zip MB |
|---|---|---|---|
| fresh master | a8c3182ec | 192 | 37.15 |
| task base | 70bfb7586 | 192 | 37.19 |
| this branch | fb52d02cc | 192 | 37.19 |

Delta vs fresh master +0.04 MB (0.00 MB vs the task base; the 0.04 is other branch content, not this task). Far below the +5 MB threshold.
No `.csproj` change, so no new package and no new CVE.

### ADR-010 ceiling
`knownOneToOneCeiling` 162 -> 163 (+1: `ITrackingNumberAllocator -> TrackingNumberAllocator`). Verified: at 162 the test fails and names exactly this pair.

## 4. Escalation: owner decision needed on the attempt bound

POML trigger 2: "more than occasional exhaustion at 10-way concurrency: STOP and present the evidence before raising the bound." It fired
(5 of 10 refused in 3 of 3 runs). Options:

- **A. Raise `MaxAttempts` to 10** (recommended). The only measured fix (10 of 10 in dev). Cost: a pathological pile-up does up to 10
  read/write pairs, and the SDK logs one error per refused write.
- B. Keep 5 and relax the acceptance criterion to a 5-way test. Real contention on one (firm, invoice number) is about 2-way (a firm resubmits
  one invoice a few times), which the measured 2/3/5-way runs cover.
- C. Add an in-process per-key semaphore. Serializes same-instance contention; cross-instance contention is still bounded by the retry.

If A is approved: change `MaxAttempts`; change the bound Theory to 9 and 10 duplicates; rename `TheRetryIsBoundedAtFiveAttempts` and the docs that say
"five"; re-run the live 10-way test and append the evidence here; then set POML status to completed (rework line "fix-round-1 task 045").

## 5. Findings for other tasks and docs

1. **SystemRequired `sprk_trackingnumber` (affects 044 and 049).** A submission row cannot be created without a tracking number, so 044's
   "return null when the firm invoice number is unknown" and 049's "allocate if the tracking number is missing" do not work as written. Options:
   (1) relax the column's required level through the 020 schema script (owner / schema decision; not done here); (2) 044 writes a unique
   placeholder such as `PENDING-<guid>` (cannot collide with a real number, which ends in `.digits`). The allocator works with either, because
   "numbered" is the sequence being non-null; 049 should test the sequence. The live test uses `ZZTEST-045-PENDING-<guid>` placeholders.
2. **Invariant registry gap.** `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` section 5 has no row for the tracking-number invariant
   (014 and 016 added none either). Proposed row: invariant "a submission's tracking number is `{firm invoice number}.{n}`, unique per firm"; owner
   `TrackingNumberAllocator`; guard alternate key (`sprk_vendororg`, `sprk_trackingnumber`); fix-up none (rows are never renumbered). Not edited here
   (docs outside this task's scope).
3. **Callers validate length at intake** against `TrackingNumberAllocator.MaxFirmInvoiceNumberLength` (89).

## 6. Known limits

- A numbered submission that is later deleted frees its n; the next submission of that invoice number takes it. No eBilling flow deletes submissions
  (the lifecycle closes them), so this is a manual-cleanup effect only.
- A tracking number held by a row the sequence cannot see (a manually entered row with no sequence or another invoice number column) makes every
  attempt collide; the allocator exhausts its attempts and refuses (fail-closed, nothing written).
- `DataverseServiceClientImpl.UpdateAsync` logs one error per refused write, so a retried allocation leaves one such error per lost race.
- The 10-way availability limit above, pending the owner decision.

## 7. Quality gates (Step 9.5)

code-review and adr-check run over the six files in scope. Findings (class): single-implementation interface under ADR-010 (documented ceiling
raise; K); constructor null guards (repo idiom, 37 occurrences in `Services/Finance`; K4); the 10-way bound (owner decision, section 4); the
SystemRequired gap and registry gap (reported, section 5). No F-class defect remains in this task's code. ADRs considered: 001/052 (no
background work: n/a), 002 (compliant; WP-1 registry row missing: reported), 007 (no Graph types), 008 (no endpoint), 009 (no cache), 010
(ceiling raised +1, justified), 013 (no AI), 015 (compliant, log-hygiene test), 028 (no auth change), 032 (unconditional registration),
038 (tests under a KEEP path).

Metrics (new files): `TrackingNumberAllocator.cs` 188 lines, 2 public / 3 private methods, 2 ctor parameters - cohesive: one job (number a
submission), one reason to change. `ITrackingNumberAllocator.cs` 79 lines, 1 interface, 1 implementation (ceiling raise above).
`TrackingNumberAllocatorTests.cs` 649 lines - cohesive: one class under test plus the in-memory store that stands in for the key; if 044/049 need the
same store it should be extracted to a shared fake then (K3, not now). No quality-direction baseline (new files); `FinanceModule.cs` +1 registration.

rework: none task 045 (pending owner decision on the attempt bound - see section 4)
