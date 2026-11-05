# Task 049 — create path within NFR-02 (notes)

> Branch `task/fm-049`, from `origin/work/field-mapping-server-write-path-r1` at 643aefe0d7. Rigor FULL, opus @ high.
> This run covers **Parts A and C**. Part B is **released** under UAC-r2's conditions (below), but is **not started here**: see "Part B — status".

## UAC-r2's conditions (received 2026-11-05, copied verbatim from the coordinator)

**Part A (concurrent caller checks)**
- All-or-nothing: any refusal refuses the create, and nothing is written before every check has returned.
- The reported refusal is deterministic for the same input. Pick it in a stable order, never by which check finishes first.
- An unknown bound record and a denied one still get the same answer.
- **Bound the concurrency, e.g. at most 4**, so a create with many bound records doesn't flood Dataverse.

**Part B (move the grant out of the request)**
1. **Scope: CREATE only.** RecordCreationService's post-create L1 pass moves. Every UPDATE path keeps its inline pass (chat update, Update Record node, push), because a lagging revoke would fail open.
2. **Identity**
   - The job writes app-only, as the inline L1 pass does.
   - The G5 caller-rights checks stay in the request, before the create and the enqueue. The job never re-derives them and never runs as the caller.
   - The message carries only RootType, RootId, GrantorOid (it becomes sprk_grantedby, for FR-33) and CacheTenants: no assignee list and no snapshot.
   - Keep Trigger = L1 semantics (RevokeOnChange = true). A new enum value must behave as L1.
3. **Idempotency and ordering**
   - The job calls AssignedAccessMaterializer for the root, which re-reads the root's CURRENT assignments and ledger.
   - It takes the same per-record lease the other triggers use (task 143's IScheduledJobLease mutex).
   - Tests: two deliveries give the same ledger; a re-assign between create and job grants only the new assignee; two concurrent runs on one root are serialized.
4. **Failures**
   - An enqueue failure runs the pass inline.
   - If the job fails after its retries, log `[ASSIGNED-ACCESS]` with the root id and failure kind, and no names or emails.
   - Confirm that ExternalAccessReconciliationJob (or whichever job really re-runs the materializer) covers a create's assigned grants within its interval, and name it in the notes.
   - No failure may be reported to the user as "access granted".
5. **Membership event**: it may lag if it grants nothing. Confirm that it is informational and that no access decision reads it.
6. **Surfaces**
   - No create response (MDA, Office, chat, SPA, Console) may say or imply that the assignee already has access. Grep the response and chat wording for that.
   - The creator's own access must not depend on the job; it comes from team ownership. Check that no create flow reads the record back as an assignee before the job runs.
   - An external assignee sees nothing until the job runs, which is acceptable.
7. **Gate (later, live):** p95 against +500 ms, p95 create-to-grant time, and 0 `[ASSIGNED-ACCESS]` failures. Not this task's now.

Use ADR-004 IJobHandler on the existing queue; copy an existing handler. Placement Justification goes in these notes.

## Part B — status

**Released, not started in this run.** The brief to this agent was Parts A and C; the coordinator released Part B mid-run with "finish Part A first". Part B (a new ADR-004 handler, the enqueue with inline fallback, the per-root lease, the three idempotency/ordering tests, the surface and membership-event audits, and naming the repair job) is a deliverable of its own size, so it is left for a separate run on this branch, after this commit. Nothing in Parts A and C touches the post-create pass (`AfterCreateAsync`), so Part B starts from today's code there.

## Part A — concurrent caller checks (done)

**Where.** `Services/Dataverse/CallerWriteAuthorization.cs`:
- `CheckCallerMayCreateAsync` now starts its three questions together: (1) the table's Create/Append privilege (metadata, then the held privileges), (3) no field-secured column, (2) the ADR-024 pair target, then AppendTo on every record named. The server-owned-column check stays first and synchronous.
- `CheckCallerMayAppendToAsync` asks the distinct records concurrently, at most `MaxRecordsAskedAtOnce` = **4** in flight (a `SemaphoreSlim` per call).

**Precedence proof (NFR-07, UAC-r2 "deterministic").**
- Before, the code returned at the first non-success in this fixed order: server-owned column → (1) → (3) → invalid pair id → (2) pair target → AppendTo per record, in the lookups' order (`DistinctBy`).
- Now every question is started, **all are awaited** (`WhenAllSettledAsync`), and the answers are read **in that same order**: the first non-success answers.
- Each question's answer is computed by the same code as before, from reads that depend only on the input, never on another question's answer. So the first non-success in order is the same one as before.
- **Faults.** Before, a fault in question *k* surfaced only if every earlier question passed. Now the walk `await`s question *k*'s task only after every earlier one passed, and that `await` rethrows the original exception. A fault therefore fails closed exactly when it did before (503 at `CreateAsync`), and a refusal earlier in order still wins over a later fault.
- **Unknown versus denied.** A record that is missing, unreadable or unappendable still gives the one uniform not-found (`RightsOnAsync` reads `None`; the privilege fallback answers `false`). The message, `ParentUnavailable` and `RefusedColumn` do not depend on which.
- **Nothing written early.** The checks only read, and no answer is returned until every task has finished, so the create (step 11) cannot start while a check is in flight.
- **What changes.** When an earlier question refuses, the later ones are still asked; before, they were skipped. These are extra reads on a refusal, all as the caller and none app-only, and their answers are discarded. No answer, status, code or wording changes.

**Concurrency safety of the caller client (asked by the brief).**
- `DataverseUserClient.SendAsync` is safe to call concurrently on one instance. `HttpClient.SendAsync` is thread-safe.
- `OrderedCredentialClientProvider` uses `ConcurrentDictionary` and a per-key `SemaphoreSlim` gate.
- MSAL `AcquireTokenOnBehalfOf` on one confidential client is thread-safe, and its in-memory user-token cache is synchronized. By step 5 the caller's token is already cached (WhoAmI at step 1 exchanged it), so concurrent checks hit the cache.
- `CallerWhoAmIMemo` is touched only on a `WhoAmI` path, and it locks `HttpContext.Items`. The checks never ask `WhoAmI`.
- The per-call reads of `HttpContext` (`IHttpContextAccessor.HttpContext`, `Request.Headers.Authorization`) are reads only. Nothing in the check path writes the request.
- **Nothing found unsafe; nothing serialized.** One residual note: ASP.NET Core does not formally promise thread-safe `HttpContext` reads. Concurrent reads of the Authorization header are safe in practice: Kestrel's header collection is not mutated by a read.

**Bound.** At most 4 records are asked at once. Together with the table-level chain (1) and the column read (3), at most 6 caller reads are in flight per check.

**Not changed in Part A:**
- `CheckRouteAuthorizedLookupsAsync`: its answer, the set of AppendTo checks to skip, is an input to the checks, and a typed route has one lookup.
- `CheckMergedPayloadAsync`: its passes are sequential by design, with different fault rules per origin. Each pass now runs its own questions concurrently.
- `DropUnusableReferencesAsync`: each reference is asked alone, and a fault drops only that one.

## Part C — cheap wins (profiled from the source)

The step medians come from task-021-live.md (Project → Matter, no mapping profile).

| Step | Median | What it reads (sequential, ≈ 80 ms each live) | Repeated within the request? |
|---|---|---|---|
| TableRules | 302 ms | caller: `ManyToOneRelationships` of the table, then one `EntitySetName` read per distinct related table (Matter Type, Practice Area, contact): **4 reads** | No |
| FieldMapping | 176 ms | app-only: the field-mapping profile query (`GetFieldMappingProfileAsync`; none found), so one read plus trace overhead | No |
| Owner | 261 ms | app-only: the caller's business unit (`systemuser`), the Secure Record business unit (by name), and the BU's default team: **3 reads**. The I-1 plan is skipped (a matter is not a stamped child), and the requester read is skipped (the system user id is known) | No |
| CallerChecks | 983 ms | caller: table metadata, held Create/Append, column security, then per org-owned target (Matter Type, Practice Area) RetrievePrincipalAccess (400), ownership metadata, held AppendTo and the row read, and for the contact RetrievePrincipalAccess: **12 reads** | No (Part A parallelizes them) |

**Changed: `Infrastructure/Dataverse/CallerMetadataReadMemo.cs`.**
- This decorator wraps the caller's client for ONE create's **step 5** (the caller checks). It answers a repeated `EntityDefinitions(…)` GET from its first successful answer.
- It keeps only table metadata. Access answers (`RetrievePrincipalAccess`, held privileges), record reads and failures are never kept. A write clears it. Nothing is shared across steps, creates, requests or callers.
- Repeats it removes: a mapped create's step 5 checks the caller's own values, then the merged row (and on a failure, each value, then the row as written). Each pass re-read the table's privilege names and each org-owned target's ownership metadata, and a re-map within the step re-reads the relationships and entity sets.
- **For the measured create (no profile) it removes nothing**: one pass, and every metadata path is distinct. It pays off on the owner's planned Project → Matter **profile** re-measure.
- Test (failing first: 2 reads before, 1 after): `CreateAsync_AMappedRow_ReadsEachTableMetadataOnce_ButAsksEveryAccessQuestion`.

**Scope narrowed after the first full run (step 5 only, not steps 1 to 10).** Wrapping steps 1 to 10 failed 6 existing seam tests, all with their assertions unchanged:
- 4 pre-create stamp tests: the step-10 stamp bind's metadata read fails while step 2's identical read succeeded, so the bind must refuse 500/503. The memo answered it from step 2.
- 2 mapping tests: a mapper read faults on the Nth identical read.
- 1 pre-write-rule test: the rule must receive the caller's own client object (ADR-028).

These pin real semantics: a later read is asked again, and the rule gets the caller's client. So the memo is now step-5-only, and those steps keep the caller's own client. This gives up these repeats, left alone on purpose:
- the step-10 stamp bind re-reading step 2's relationships (≈ 80 ms on a stamped child with a missing stamp);
- the ADR-024 pair target re-reading an entity set step 2 read.

**ADR-009 position (Path C, comply).**
- Within-request de-dupe only, and narrower: one create.
- Data only. No authorization answer is kept, so "MUST NOT cache authorization decisions" holds.
- `RequestCache` is not used because it is a plain `Dictionary`, which is not thread-safe under Part A's concurrent reads. It is also absent off-request: the create service also runs from chat tools and jobs.
- The precedent is `CallerWhoAmIMemo` (task 170), which also keeps its per-request answer outside `RequestCache`.
- adr-check is recorded below.

**Left alone, with reasons:**
- **Concurrent `EntitySetName` reads in the payload mapper (TableRules):** about −160 ms for 3 related tables. These are independent reads, not repeats; they are outside "repeated or cacheable" and would change the mapper shared by every caller write. Recommended follow-up.
- **The owner resolver's user-BU and Secure-BU reads, run concurrently (Owner):** about −80 ms. Not a repeat. The resolver is UAC-r2's surface.
- **A cross-request cache of the Secure Record business unit and of the field-mapping profile:** about −80 ms each. Both are data, not authorization answers, so ADR-009 allows Redis with an invalidation. But each needs an invalidation design (a BU rename, a profile edit), which is beyond "cheap". Recommended to the owner if the re-measure still exceeds budget.
- **Merging the held-privilege questions** (Create/Append, then each org-owned target's AppendTo) into one `RetrieveUserSetOfPrivilegesByNames`: it changes the check's shape on the security boundary. Not cheap.

## Local estimate of the time saved per step

The estimate assumes about 80 ms per Dataverse read (task-021's measured medians).

| Step | Before (median) | After (estimate) | Saved | Why |
|---|---|---|---|---|
| CallerChecks | 983 ms | ≈ 330 ms | **≈ −650 ms** | Longest chain: one org-owned target is 4 sequential reads (RetrievePrincipalAccess, ownership metadata, held AppendTo, row), run beside the privilege chain (2) and the column read (1). 3 records fit within the bound of 4. |
| CallerChecks with a 6-lookup profile (task-021's event example: 2.35 s) | 2350 ms | ≈ 0.9–1.1 s | ≈ −1.3 s | 9 records in 3 waves of 4 (Part A); the second pass's repeated metadata answered from the memo (Part C) |
| TableRules / FieldMapping / Owner | 302 / 176 / 261 ms | unchanged | 0 | No repeated read in the measured create (see Part C) |
| PostCreate | 1331 ms | unchanged until Part B | 0 | Part B not started |

**Projected server delta for the measured create after A and C:** +2634 ms → about **+2.0 s** (p95). Still above +500 ms. Part B (≈ −1.3 s) brings it to about +0.7 s. The Part C follow-ups listed above (≈ −0.3 s together) would come close to the budget. The live re-measure (step 5, after a dev deploy) decides; it is not this run's.

Measured locally: the timing tests use a 300 ms delayed double.
- Four records: about 1 delay, against 4 sequentially.
- The whole check with 3 records: about 2 delays, against 6 sequentially.
- Eight records: 2 waves of 4.

## Tests (KEEP paths only)

**New: `tests/integration/auth/FieldMapping/CallerChecksConcurrencyTests.cs`** (18 tests). It uses a scripted, thread-safe caller double with per-record delays and fault switches.

- **Timing and bound:**
  - 4 records take about 1 delay, with 4 in flight.
  - 8 records go 4 at a time, in two waves.
  - The whole check takes about 2 delays, not 6.
  - Every check has returned before the answer, even when the first refuses at once.
- **Precedence** (UAC-r2: deterministic), one test per pair of refusing checks the suites cover. The first in order is made to finish last.
  - Records against each other:
    - two records refused → the first record answers;
    - first refused, second faults → the refusal answers;
    - first faults, second refused → the fault propagates;
    - an unknown record and a denied one get the same answer.
  - Table-level checks against a refused record:
    - no Create privilege, with a secured column and a refused record → the privilege refusal;
    - the table metadata unreadable → the client failure;
    - a secured column → the secured-column refusal;
    - a record fault with no Create privilege → the privilege refusal, not the fault;
    - the table metadata read faults → the fault propagates.
  - The pair against a refused record:
    - a pair type the caller cannot read → the pair refusal;
    - a pair id that is not a record id → Invalid.
- **Memo (Part C):** a repeated metadata read is answered from the first answer; an access answer or a failure is never kept; a write forgets everything.

**New in `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.cs`:** `CreateAsync_AMappedRow_ReadsEachTableMetadataOnce_ButAsksEveryAccessQuestion`.

**Failing first.** With the two source files restored to HEAD (the test's one reference to the new constant replaced by a literal), **5 fail**:
- the 3 timing tests;
- the all-settled test;
- the seam memo test.

The other 14 pass on both versions, as they should: they pin behaviour that must not change. A first-to-finish implementation would fail the precedence tests.

## Quality gates

- **code-review**: no F-class findings.
  - Suggestions, K4: a refusal path now makes the sibling reads it used to skip (the answer is unchanged); the timing margins are wide on purpose, and the `MaxInFlight` assertions are the deterministic proof.
  - Applied: a class-remark note on the concurrency in `CallerWriteAuthorization`.
- **adr-check**: no violations.
  - Warning: ADR-009's "use RequestCache for within-request de-dupe". Resolved as Path C in substance (see Part C); the reviewer confirms.
  - ADR-003, 016 (bounded fan-out), 028 and 038 are compliant.
- **Build**: 0 warnings, 0 errors.
- **verify-suites.sh**:
  - Baseline (643aefe0d7): solution build 0 warnings; ArchTests 1931 passed; filtered suites 6789 passed, 7 skipped, 0 failed.
  - Tip (4b9f712ad9): solution build 0 warnings; ArchTests 1931 passed; filtered suites **6808 passed, 7 skipped, 0 failed**. That is the baseline plus the 19 new tests, with existing assertions unchanged.
- **Publish delta (§10)**: `publish-delta.ps1`, master `d9bef8e90f` against branch tip `4b9f712ad9`: **+2,490 bytes** (zip 39.21 MB, 191 files on both sides). That is far below the +5 MB justification line, and the total is below 55 MB.
- **CVE**: `dotnet list package --vulnerable --include-transitive`: no vulnerable packages, on both the worktree and the publish-delta branch build.
- **Placement (§10/§11)**:
  - No endpoint, DI registration, package or background work was added.
  - One internal decorator, `Infrastructure/Dataverse/CallerMetadataReadMemo.cs`, sits beside `CallerWhoAmIMemo`; its justification is in its class remarks.
  - The rest modifies existing surface: `CallerWriteAuthorization`, and `RecordCreationService` step 5.
