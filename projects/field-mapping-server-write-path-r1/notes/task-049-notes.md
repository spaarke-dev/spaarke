# Task 049 — create path within NFR-02 (notes)

> Branch `task/fm-049`, from `origin/work/field-mapping-server-write-path-r1` at 643aefe0d7. Rigor FULL, opus @ high.
> Run 1 covered **Parts A and C**. Part B was built in run 2 on `task/fm-049b`: see "Part B".

## UAC-r2's conditions (received 2026-11-05, copied verbatim from the coordinator)

**Part A (concurrent caller checks)**
- All-or-nothing: any refusal refuses the create, and nothing is written before every check has returned.
- The reported refusal is deterministic for the same input. Pick it in a stable order, never by which check finishes first.
- An unknown bound record and a denied one still get the same answer.
- **Bound the concurrency, e.g. at most 4**, so a create with many bound records doesn't flood Dataverse.

**Part B (move the grant out of the request)**
1. **Scope: CREATE only.** RecordCreationService's post-create L1 pass moves. Every UPDATE path keeps its inline pass (chat update, Update Record node, push), because an update's removal is applied in the request.
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

## Part B — the create's "Assigned *" pass on a queued job (branch `task/fm-049b`)

> Run 2 (2026-11-05), from `task/fm-049` at d3be9efe9c. Rigor FULL, opus @ high. Waits on UAC-r2's review of the points
> under "For UAC-r2" before merge.

**Design.**
- `RecordCreationService.AfterCreateAsync` (step 12) no longer runs `AssignedAccessMaterializer.RunAfterWriteAsync` for a
  created root. `QueueAssignedAccessGrantAsync` queues one `AssignedAccessCreateGrant` job on `sdap-jobs` and returns.
  It resolves `JobSubmissionService` from a scope (the `CoreAncestorRestampQueue` idiom). It returns `false` when the
  table is not one of the three roots, when no queue is registered, or when the send fails or takes longer than 5 s
  (`EnqueueTimeout`, the `GateDecisionRecordEnqueuer` bound). In each of those cases the pass runs inline, as before.
- **Message** (`AssignedAccessCreateGrantPayload`): `RootType` (by name), `RootId`, `GrantorOid` (the create's,
  `null` when the request names no grantor) and `CacheTenants` (the deployment tenant, as the inline pass used). The
  idempotency key is `assigned-access-create:{rootId:N}`. The correlation id is the create's.
- **Handler** (`Services/Jobs/Handlers/AssignedAccessCreateGrantJobHandler.cs`, registered in `JobProcessingModule`
  beside `CoreAncestorRestampJobHandler`), modelled on `RelocationReleaseRetryJobHandler`:
  1. It takes the per-record lease `NoAccessShareEnforcer.RecordLockId(table, id)` on `IScheduledJobLease`. It waits up
     to 2 minutes, asking every second.
  2. Under the lease it runs `MaterializeAsync` with `AssignedAccessTrigger.Inline` and `RevokeOnChange: true`, which are
     L1 semantics. No new enum value is added. It writes app-only.
  3. It **settles**: a pass that wrote anything is followed by another, until a pass writes nothing (at most 3).
  4. It releases the lease.
  5. It resolves the materializer only when the job runs, because the processor builds every handler for every message.
- **Outcomes.**
  - *Complete* (the last pass wrote nothing), *NotFound* (the root was deleted since) or *NoRegistry*: `Success`.
  - Otherwise `Failure` (redelivered). On the last delivery (`IsFinalDelivery`, 5) the job is `Poisoned` and logs an
    `[ASSIGNED-ACCESS]` error with the root type, the root id and the failure kind: the status plus the failures' kind
    codes, e.g. `failed:ledger-unreadable`, `record-busy`, `faulted`, `unsettled`. Failure messages and exception
    messages are never logged, because they can carry names.

**Membership event: not moved.** The event was already fire-and-forget (`_ = PublishOwnerAddedAsync`, ADR-034 Q2) and the
create never awaited it. It costs the request nothing beyond starting the task. Moving it into this job would add the
owner team and the correlation to the message, which condition 2 forbids. It also covers `sprk_event` and `sprk_document`,
which are not roots. It stays where it is.

### Per condition

1. **CREATE only.** The only caller of the queue is `RecordCreationService` step 12. These keep their inline pass,
   unchanged: `DataverseUpdateRecordHandler:406,525`, `UpdateRecordActionCore:396`, `FieldMappingEndpoints:1327` (push),
   and the chat create's run-as-user path (`DataverseCreateRecordHandler:528`, not the service).
2. **Identity.**
   - The job runs `MaterializeAsync` app-only, as the inline pass does. There is no caller token in the job.
   - G5 (steps 1 to 5) runs before the create (step 11) and the enqueue (step 12), unchanged.
   - Message: the four fields only. A test asserts the exact property set, and that no assignee id is in the JSON.
   - Trigger: `Inline`, with `RevokeOnChange: true`. A test shows an ended assignment's auto grant is removed. With the
     job trigger, that test fails (mutation run).
3. **Idempotency and ordering.**
   - The job re-reads the root's current columns, ledger and grants on every pass.
   - It takes task 143's per-record lease. Tests:
     - two deliveries leave identical ledger JSON and add no writes;
     - a re-assignment before the job grants only the new assignee;
     - two concurrent runs never overlap (max one pass in flight; the second waits for the lease);
     - a lease held past the wait means redelivery, with nothing written.
   - **Finding (adversarial review, F1, fixed): only the removal paths hold that lease.** Task 143's enforcer, the
     Restricted remover and `/unshare-user` take it. The materializer's other triggers (an update's inline pass, the
     sync endpoint, the reconciliation job) take none. So the lease serializes the job with itself and with those
     removers only.
   - The **settle loop** makes the job correct against an unleased pass: every pass re-reads, and a pass that writes
     nothing read the root after every earlier write of this job. A test re-assigns the root with an update's inline pass
     while the job is mid-pass, and the earlier assignee ends with no grant. That test fails without the loop.
4. **Failures.**
   - An enqueue that throws, times out or has no queue runs the inline pass (tests for a throwing send and for a send
     that never answers).
   - A job that fails after its deliveries logs `[ASSIGNED-ACCESS]` with the root id and kind only. A test asserts no
     contact id, name or `@` in the handler's lines.
   - **Backstop: `AssignedAccessReconciliationJob`** (`assigned-access-reconciliation`, cron `*/5 * * * *`, enabled,
     writes on for grant/convert/renew). It runs the same materializer over every root with an "Assigned *" column set
     (`ScanAssignedRootsAsync`), up to 200 roots a run, roots changed in the last 20 minutes (`RecentChangeWindow`, derived from the 20-minute `RecentScanWindow`) first. A new root has its
     column set and `modifiedon` = now, so it is a first-priority candidate on the next tick: within 5 minutes, while
     fewer than 200 roots change per 20 minutes and each type has at most 4,999 assigned roots (the scan bound). Note:
     that job passes no grantor, so a contact granted first by the backstop gets no invite (as with a failed inline pass
     before).
   - Nothing reports "access granted": the create's outcome never carried the pass's result (the inline call's return
     was discarded). The job reports to no user.
5. **Membership event.** Informational only.
   - The event writes `sprk_userentityassociation` and evicts membership caches (`MembershipJunctionUpdater`).
   - The junction's only reader in `src` is `ParticipantCorrelationRung` (mail-to-matter correlation) via
     `DataverseServiceClientImpl.QueryContactMembershipsAsync`.
   - `MembershipResolverService`, the access-set composer `AccessibleRecordSetService` and the AI and workspace readers all
     query the entities' live columns, not the junction.
   - The backstop is `MembershipReconciliationJob` (nightly, 02:00). It is unchanged by this task in any case.
6. **Surfaces.**
   - `RecordCreationOutcome` has no assigned-access field.
   - The responses (child-record route `{id, warnings}`, analysis, events, Office `QuickCreateResponse`, chat
     `ToolResult` "Created record …") carry ids and warnings only. The warnings speak of the creator's own isolated
     share.
   - Client grep (Console/SpaarkeAi, wizards, external SPA, Office add-ins, PCF): no create flow says or implies an
     assignee has access. "Granted access to N" appears only in the manual Manage Access dialog and the TrackingFieldTrio
     PCF.
   - The wizards' `syncAssignedAccess` (`matterService.ts:356`, `projectService.ts:375`) shows no UI and still runs right
     after their create; its console text already says "the reconciliation job grants access within minutes".
   - No create flow reads the record back as an assignee.
   - The creator's access: an unfiled root is owned by the creator's business-unit team (I-6); an isolated root is
     shared to the creator inline (`CompleteIsolatedCreateAsync`). Both are unchanged and do not depend on the job. See
     "For UAC-r2" item 1 for the one case to confirm.

### Placement Justification (§10) and §11

- **Placement (ADR-052):** queue-driven BFF work, so an ADR-004 `IJobHandler` on the existing `sdap-jobs` queue. It uses
  BFF domain code (the materializer), BFF-owned tables and the BFF's identity, at low volume (one message per root
  create). No `Task.Run`, no new `BackgroundService`, no new queue, package, endpoint or interface. The registration is
  one line in `JobProcessingModule`.
- **§11:**
  1. Existing: the inline call (kept as the fallback) and `AssignedAccessReconciliationJob` (scheduled, up to 5 minutes
     later).
  2. Extension: the materializer, the lease and the queue are reused unchanged; only the trigger is new.
     `AssignedAccessReconciliationJob` cannot be extended to do it, because it is schedule-driven: a per-create trigger
     there would be a hand-rolled poll.
  3. Cost of doing nothing: the 1.3 s pass stays inside every root create, and NFR-02 fails (+2.0 s after Parts A and C).
- **ADR-036 (lease outside the scheduler):** the third use of the scheduler's lease store as a keyed mutex, under the
  same §6.5 path A that UAC-r2's design §9 records for task 143. UAC-r2's condition 3 asked for this lease. The handler
  is an `IJobHandler`, not an `IScheduledJob`, so A1-7 (host neutrality) is not engaged.

### Tests (KEEP path `tests/integration/data-mutation/ExternalAccess/`)

**New: `AssignedAccessCreateGrantJobTests.cs` (12).** The real materializer runs over `AssignedAccessTestDoubles.Harness`;
the create side goes through the chat create's owned path into `RecordCreationService`.

- Create:
  - queues one job naming only the root, and grants nothing until the job runs;
  - a throwing enqueue grants inline;
  - an enqueue that never answers grants inline after 5 s.
- Job:
  - two deliveries leave the same ledger;
  - a re-assign before the job grants only the new assignee;
  - a re-assign after an earlier pass removes the earlier auto grant (L1);
  - concurrent runs are serialized;
  - an update that re-assigns during the pass leaves only the new assignee with access;
  - a deleted root completes;
  - a held lease means redelivery and no writes;
  - a failing pass is retried, then dead-lettered, logging root and kind only;
  - an invalid payload is dead-lettered.

**Changed: `AssignedAccessWriterTriggerTests.OfficeQuickCreate_SharesTheRecordWithItsAssignedMaker_BeforeItReturns`**
is renamed `…_ThroughItsQueuedGrant`. The Office host now records queued jobs. The test asserts there is no share at
return, then runs the queued job and asserts the share. Its old assertion ("inline, R3 'immediate'") is the behaviour
Part B changes on purpose. It is UAC-r2's test, so UAC-r2 should acknowledge the change (item 1 below).

**Failing first.**
- With `RecordCreationService.cs` restored to d3be9efe9c: 3 fail (the queue test and both Office theory cases); 17 pass.
- Mutations of the new handler:
  - per-call lease keys (no shared lease): the serialization and held-lease tests fail;
  - `Trigger.Job` with `RevokeOnChange: false`: the L1 test fails;
  - the handler as first committed (5a400eb0b9, no settle loop): the mid-pass re-assign test fails.

### For UAC-r2 (review before merge)

1. **Office and chat creates name the maker as the root's "for" person** (`sprk_assignedtointernal`, owner A7 /
   `ChatCallerIsTheForPerson`), so the maker's own Assigned-To share now arrives with the job (seconds) instead of
   before the response.
   - The maker's access to the record does not come from that share when the root is unfiled (their business-unit
     team) or isolated (the creator share).
   - Please confirm the remaining case: a root filed under a non-secure parent owned by another business unit's team
     (e.g. an Office project created under such a matter). There, the maker's immediate access comes from team ownership
     only if their roles reach that business unit.
   - If it should stay immediate, keep the inline pass when the create named the maker as the "for" person. That is a
     one-line predicate in `QueueAssignedAccessGrantAsync`.
2. **Condition 3's lease.** See the F1 finding above. The job is serialized with itself and the removers; it is settled
   against the unleased triggers. If UAC-r2 prefers the update passes and the sync endpoint to take the same lease,
   that is a change to their trigger code.
3. **Known limits (K):**
   - A send that times out but lands gives one inline pass and one job pass. Both are idempotent, and the job settles.
   - The lease is not renewed. A pass longer than 5 minutes loses exclusivity; the settle loop still re-reads.
   - Redelivery is immediate (the processor abandons). A throttling burst can use up 5 deliveries quickly, and the
     reconciliation job then completes the grant without a grantor (so no invite).
   - Pre-existing: `JobSubmissionService` creates a sender per submit without disposing it. Each root create now
     submits one.

### Quality gates (Part B, tip 25733ff9da)

- **verify-suites.sh**: solution build 0 warnings, 0 errors; ArchTests 1931 passed; filtered suites **6820 passed,
  7 skipped, 0 failed**. That is the Part A/C tip (6808) plus the 12 new tests; the one changed Office test passes.
- **Publish delta (§10)**: master `0f53e053bf` against `25733ff9da`: **+6,071 bytes** (zip 39.21 MB, 191 files on both
  sides). Far below +5 MB; the total is below 55 MB.
- **CVE**: `dotnet list package --vulnerable --include-transitive`: no vulnerable packages. No package was added.
- **adr-check**: no violations.
  - Compliant: ADR-004 (deterministic idempotency key, correlation carried, `JobOutcome` returned; the receive side is
    made safe by the per-record lease and the materializer's natural-key ledger, not by message de-duplication),
    ADR-052 (an `IJobHandler` on the existing queue; no `Task.Run`, no `BackgroundService`), ADR-010 (no new interface),
    ADR-028 (app-only job; G5 stays in the request), ADR-038 (KEEP path), ADR-002 (no plugin; one owner).
  - Warning: ADR-036. The `IScheduledJobLease` is used as a keyed mutex (UAC-r2 design §9, path A). Not an
    `IScheduledJob`, so A1-7 is not engaged.
- **Adversarial review (one pass)**: one F-class finding (F1: the job was not serialized against the unleased update,
  sync and reconciliation passes). Fixed in 25733ff9da with the settle loop and a test that fails without it. The
  remaining items are the known limits listed above.

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
| PostCreate | 1331 ms | ≈ 0.3–0.4 s (Part B: the number read-back, the I-1/I-16 steps, and one Service Bus send of ~20–50 ms stay) | ≈ −1.0 s | estimate; the live re-measure decides |

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

## Fix round 1 (part A)

> Branch `task/fm-049b`, from 0c9e3f7db8. Part B's files (`AssignedAccessStore`, `AssignedAccessReconciliationJob`) are
> not touched here.

**1. The maker as assignee stays inline (UAC-r2 ruling, option (a)).**
- `RecordCreationService.GrantAssignedAccessAsync` (step 12) runs the pass inline when the root's "for" person
  (`RecordTableRule.ForPerson`, `sprk_assignedtointernal`) is the maker's linked contact. Otherwise it queues the job, and
  falls back inline when nothing could be queued, as before.
- `ForPersonIsTheMakerAsync` asks `IIdentityNormalizationService.ResolveAsync(maker)`, the cached resolver the chat and
  Office adapters fill that column from. The maker is the caller's `systemuserid` (or a system create's requester), now
  carried on the decision as `MakerSystemUserId`.
- Only a root whose create sets the column asks. The answer fails closed: no maker, no resolver in the host, a lookup that
  faults or takes longer than 5 s (`EnqueueTimeout`), or no contact all mean "queue".

**2. F3, the materializer-fault test.** `OfficeQuickCreate_AMaterializerFault_NeverFailsTheCreate` now names another
assignee and makes the queue throw, so the inline fallback runs over `FailLedgerRead`. It asserts the 201, one inline pass
(the request resolved the materializer once), no queued job and no share writes. A new case,
`…_FailsItsQueuedJob_WithoutWriting`, runs the queued job over the same fault: `Failed` (`failed:ledger-unreadable`), no
writes.

**3. K2, settling after a failing pass that wrote.** In `AssignedAccessCreateGrantJobHandler`, a pass that did not complete
but wrote something is followed by another pass while passes remain (at most `MaxPasses` = 3). The last pass decides the
delivery: a later pass that completes and writes nothing is a success; otherwise its failure kinds are reported, as before.

**4. Secure create (UAC-r2 ruling, options (b) and (c)).** The create-time pass waits for secure provisioning, so
eligibility is decided on the final record.
- For an isolated create (`decision.IsolatedFor` set, `sprk_issecure = true` in the create), the pass is neither run nor
  queued at create. It runs from step 12 right after `CompleteIsolatedCreateAsync`, only when that reports the securing
  complete (`IsComplete`, not stranded), under the same rule as item 1 (inline for the maker as assignee, else queued).
- When the securing does not complete, nothing is granted at create; the assigned-access reconciliation job grants later.
- Non-isolated creates are unchanged.
- The Console wizard's secure project sends no secure signal on the create. That path is unchanged here; the wizard path
  is followed up with UAC-r2. The premise check UAC-r2 asked for (two questions on that path) was run; its result went to
  the coordinator.

**5. ArchTest `WriterOnlyTablesDeleteGuardTests` (coordinator item).** On the merged tip the guard flagged
`CallerMetadataReadMemo.DeleteAsync`: a pass-through decorator of `IDataverseUserClient` whose `DeleteAsync` forwards the
caller's path. `DeletersOf` now leaves out a scanned API's own `DeleteAsync` when the calling type implements (or derives
from) that same API (walking out of the async state machine). Its callers are still seen at their own call to the API.
The remarks describe the exemption. Controls: a compiled decorator fixture whose `DeleteAsync` forwards (must not fire)
and whose other member `Purge` deletes through the wrapped client (must fire). With the exemption removed, both the
guard and the control fail. The memo is not listed in `FixedTableDeletes`, because its table is not fixed.

### Tests

| Test | File | Before the change |
|---|---|---|
| `OfficeQuickCreate_ForItsMaker_SharesTheRecordWithThem_BeforeItReturns` (matter, project) | `AssignedAccessWriterTriggerTests` | fails (queued) |
| `OfficeQuickCreate_ForAnotherAssignee_QueuesThePass_AndGrantsNothingInline` (matter, project) | same | passes (pins the unchanged side) |
| `OfficeQuickCreate_WhenTheMakersContactLookupFaults_QueuesThePass` | same | fails (the lookup was never asked) |
| `OfficeQuickCreate_AMaterializerFault_NeverFailsTheCreate` (rewritten, F3) | same | see the mutations below |
| `OfficeQuickCreate_AMaterializerFault_FailsItsQueuedJob_WithoutWriting` | same | passes (pins the job side) |
| `Job_AReassignDuringAPassThatAlsoFails_LeavesTheEarlierAssigneeWithNoGrant` (K2) | `AssignedAccessCreateGrantJobTests` | fails (one pass) |
| `CreateAsync_AnIsolatedCreate_WhoseSecuringDidNotComplete_QueuesNoPass_AndGrantsNothing` | `RecordCreationServiceSecureRootSeamTests` | fails (queued) |
| `CreateAsync_AnIsolatedCreate_ForAnotherAssignee_QueuesItsPass_OnceSecuringCompleted` | same | passes (pins the queued side) |
| `CreateAsync_AnIsolatedCreate_ForItsMaker_RunsItsPassInline_OnceSecuringCompleted` | same | fails (queued) |

- The Part B test `OfficeQuickCreate_SharesTheRecordWithItsAssignedMaker_ThroughItsQueuedGrant` is replaced by the first
  row: UAC-r2's ruling restores "before it returns" for the maker.
- The seam test's scope factory now holds a recording queue, the maker's linked contact and a counted materializer. The
  existing order test (completion before the "Assigned *" step) is unchanged and passes.
- **F3 mutations.** (a) The failed enqueue no longer falls back inline: `NeverFailsTheCreate` fails (0 inline passes).
  (b) The fault handling on the fallback path removed (the ledger-read catch in `MaterializeAsync`, and the catches in
  `AfterWriteAsync` and `RunAfterWriteAsync`, each rethrow): the create answers 500 and the test fails. The old version
  could catch neither: its pass was only queued, and the queued job never ran.
- **K2 mutation.** With the handler restored to 0c9e3f7db8, the K2 test fails (one pass ran).

### Quality gates (fix round 1, part A)

- **verify-suites.sh**: solution build 0 warnings, 0 errors; ArchTests **1939 passed** (the delete guard and its new
  control included); filtered suites **6852 passed, 7 skipped, 0 failed**.
- **Second filter** (`AssignedAccess|CallerChecks|CallerWriteAuthorization|CallerMetadata|JobHandler|JobProcessing|ExternalAccess|Office|DataverseCreateRecord|RecordCreation`):
  **6963 passed, 7 skipped, 1 failed**. The failure was
  `ProvisionProjectIdempotencyTests.ProvisionProject_WhenRecordingTheContainerTimesOut_FailsLoudlyAndLogsTheContainerId`,
  a `TimeoutException` after 1 min 23 s while a second ~9 GB test host ran beside it. This round does not touch that code.
  Run alone, the class passes: 55 of 55 in 9 s.
- No package, endpoint, DI registration or background work was added. `ForPersonIsTheMakerAsync` resolves the existing
  `IIdentityNormalizationService` singleton from a scope (the `QueueAssignedAccessGrantAsync` idiom), so the service's
  constructor and its callers are unchanged.

## Fix round 1 (part B): the reconciliation backstop

> Done on `task/fm-049c` (98d8b16762, cc795f2b5c) and merged into `task/fm-049b`. UAC-r2 owns the code changed here and approved the change in 049.

### 1. F2: `AssignedAccessStore.ScanAssignedRootsAsync` (done)

**Before.** The full scan sent no `$orderby` and kept at most 4,999 roots per type (`MaxScanRows`). Its comment said
"newest change first", but Dataverse returned whatever prefix it chose. A root created after that bound was reached
could be left out of every run, so a create whose queued grant did not complete had no backstop.

**After.**
- (a) The full scan sends `$orderby=modifiedon desc` (`AssignedAccessStore.ScanAssignedRootsAsync`). A capped scan now
  keeps the most recently changed roots and drops the oldest, so a new root is inside the bound.
- (b) A per-type recent-change scan, `ScanRecentlyChangedAssignedRootsAsync`, reads the assigned roots with
  `modifiedon ge {now − RecentScanWindow}`, newest first, with its own bound.
  - `AssignedAccessReconciliationJob` merges it into the candidates. The candidates are a dictionary keyed by
    (type, id), so a root that both scans return is one candidate.
  - The window is `TickInterval × (1 + RecentScanMarginTicks)` = 5 min × 4 = **20 minutes**: one tick plus 3 ticks of
    margin. `TickInterval` sits beside `DefaultCronSchedule` (`*/5 * * * *`), and a test pins the two together.
  - The job keeps no last-successful-run watermark (`JobRunContext` carries none), so the window is sized from the tick.
  - With (a) in place, (b) restates the guarantee directly: a root changed within the window is a candidate whatever the
    full scan's bound keeps. It costs one more read per root type per run (3 every 5 minutes).
- `DataverseWebApiClient.QueryAsync` has no order parameter, and about 15 test doubles override that virtual method. So
  the client gains a separate virtual `QueryOrderedAsync(entitySet, filter, select, top, orderBy, ct)`. Both methods
  build the URL in one private `QueryCoreAsync`. The existing `QueryAsync` behaviour is unchanged.

**Tests (each failed first, with the old order and merge):**
- `AssignedAccessStoreODataTests` uses a new `AssignedRootsTable` double. It evaluates `$filter` (the shared `ODataFilter`,
  which now also understands `ge` with a UTC date-time), applies `$orderby` and `$top`, and returns stored order when no
  `$orderby` is sent.
  - `TheAssignedRootScan_ReadsNewestChangeFirst_SoAScanPastItsBoundKeepsANewRoot`: 4,999 old roots and one new root,
    stored last. The new root is at the head of the capped result.
  - `TheAssignedRootScan_ReturnsTheRootsNewestChangeFirst`.
  - `TheRecentChangeScan_ReturnsOnlyAssignedRootsChangedSinceItsBound_NewestFirst` (new method). It also pins the
    filter's `modifiedon ge 2026-11-05T11:40:00Z` suffix and the `$orderby`.
- `AssignedAccessReconciliationJobTests` uses the fake store's new `PastScanBound` set: roots the full scan's page does
  not reach.
  - `ARecentRootPastTheFullScansBound_IsStillACandidate_AndIsGranted`.
  - `ARootBothScansReturn_IsOneCandidate_MaterializedOnce`: 3 recent scans, 1 candidate, 1 materialized.
  - `TheRecentChangeScan_ReadsBackOneTickPlusThreeTicksOfMargin_AndTheTickIsTheCrons`.
  - Negative case: `ARootChangedBeforeTheRecentWindow_AndPastTheFullScansBound_IsNotACandidateThisRun`. It passes on
    both versions; the rotation reaches such a root.
- Failing first: with the full scan reverted to the unordered `QueryAsync` and the job's recent scan removed, **6 fail**
  (the 3 store tests and the 3 job tests above). All 53 tests in the two classes pass after the change.

### 2. UAC-r2 conditions (b) and (c): what the reconciliation job re-derives (notes only, no behaviour change)

`AssignedAccessReconciliationJob` (`assigned-access-reconciliation`, `*/5 * * * *`) materializes every candidate root
through `AssignedAccessMaterializer.MaterializeAsync` with `AssignedAccessTrigger.Job`, `GrantorOid: null` and
`RevokeOnChange` = the job's switch (`AssignedAccessReconciliationJob.cs:339`). The candidates are the roots with an
Assigned column set (both scans above), plus the roots that hold a live ledger row (`ScanLedgerRootsAsync`, `:230`), plus
the Restricted roots.

- **Adds: re-derived.** Grant, link conversion (grant to share) and renewal run with writes on, from a fresh read of the
  root's columns, ledger and grants on every pass.
- **Removals.** This job re-derives removals only when gate G-8's switch is on (#2574, on dev since 2026-11-06).
- The materializer's `Declined`, `KeptModified` and `KeptSecureRecord` outcomes apply the same in both settings.

### 3. record-busy reaches users as a message (confirmed; one wording fix)

All the paths share one per-record lease (`NoAccessShareEnforcer.RecordLockId`): the No Access enforcer, `/unshare-user`,
the Restricted remover and, since Part B, the create's queued Assigned-To grant (`AssignedAccessCreateGrantJobHandler:165`).
The handler waits up to 2 minutes for the lease. The routes ask once and do not wait.

| Route | When the lease is held | Status | Title | `reasonCode` | `detail` | Other extensions |
|---|---|---|---|---|---|---|
| `POST …/unshare-user` | `InternalShareEndpoints.cs:694` | **409** | Share not removed | `sdap.access.user_share.record_busy` | "Another access change is under way on this record, so no share was removed. Try again in a moment." | `traceId` |
| `POST …/unshare-user` (lease store unreachable) | `:688` | 500 | Share not removed | `sdap.access.user_share.read_failed` | "This record's access could not be locked for the change, so no share was removed. Try again." | `traceId` |
| `POST …/no-access/enforce` | enforcer `:1039` → `NoAccessEnforceEndpoint.cs:134` | 500 | No Access entry not enforced | `sdap.access.no_access.enforcement_incomplete` | the first failure's message: "User {id}'s share was not removed: another access change is under way on this record. Try again in a moment; the 5-minute safety net retries too." | `traceId`, `report` (`failures[].kind = "record-busy"`) |
| `POST …/assigned-access/sync`, No Access part | enforcer `:1039` → `AssignedAccessSyncEndpoint.cs:214` | 500 | No Access not fully re-applied | `sdap.access.assigned.no_access_incomplete` | the same enforcer message | `traceId`, `outcome` (`noAccess[].failures[].kind = "record-busy"`) |
| `POST …/assigned-access/sync`, Restricted part | remover `:227` → `AssignedAccessSyncEndpoint.cs:223` | 500 | Restricted access not fully applied | `sdap.access.assigned.restricted_external_incomplete` | "External users' access to this Restricted record was not checked: another access change is under way on it. Try again in a moment; the 5-minute safety net retries too." | `traceId`, `outcome` |

- **No path gives a bare 500.** Every answer is ProblemDetails (`application/problem+json`) with a reason code, a
  user-facing `detail` and the trace id.
- The clients show `detail`:
  - the No Access entry form (`sprk_noaccessentry_postsave.js:965-968`) as an ERROR notice (a 409 would be a WARNING);
  - the Assigned-To form script (`sprk_assignedaccess_postsave.js:256`);
  - the wizards' `assignedAccessSync.ts`, which never fails the wizard and logs to the console.
- In the sync route, the materializer still runs when the No Access or Restricted part was busy. Only the response
  reports the busy part.
- **Fixed (wording).** The enforcer's record-busy message said "another No Access enforcement is changing this record's
  access". The lease is shared, so that named the wrong holder whenever `/unshare-user`, the Restricted remover or (since
  Part B) a create's queued grant held it. It now says "another access change is under way on this record", the wording
  `/unshare-user` and the remover already use. There is no status or reason-code change.
- **Tests.**
  - `NoAccessEnforceEndpointTests.Enforce_WhileTheRecordsRemovalLeaseIsHeld_RemovesNothing_AndSaysAnotherAccessChangeIsUnderWay`
    (failed first: old wording).
  - `AssignedAccessSyncEndpointTests.Sync_WhileTheRecordsRemovalLeaseIsHeld_NoAccessRemovesNothing_AndTheAnswerSaysTryAgain`
    (failed first: old wording).
  - `AssignedAccessSyncEndpointTests.Sync_OfARestrictedRecord_WhileTheRecordsRemovalLeaseIsHeld_AnswersRestrictedExternalIncomplete_SayingTryAgain`
    (pins the existing shape; it passed before too).
  - Each test holds the host's real `IScheduledJobLease` and releases it in `finally`. `/unshare-user`'s 409 is already
    pinned by `InternalUserShareTests:1047`.
- **For the main session / UAC-r2 (not changed: a status change is an API contract change).**
  - A busy record is 409 on `/unshare-user` but 500 on `/no-access/enforce` and `/assigned-access/sync`, so the No Access
    form shows it as an error rather than a warning. A 409 with a `record_busy` reason on those two routes would align
    them.
  - Since Part B, a wizard's `/assigned-access/sync` right after its create can meet the create's queued grant holding
    the lease, on a Restricted record or on a secure record with a covering No Access entry. That call then answers 500
    with the "try again" message above. The wizard does not surface it, and the grant job and the 5-minute jobs complete
    the work.

### 4. Invite when the reconciliation grants first (skipped: known limit)

Not cheap.
- The materializer invites only for a pass with a person behind it. `ResolveActorAsync` returns "nobody" for
  `AssignedAccessTrigger.Job` (`AssignedAccessMaterializer.cs:1318`).
- "A background pass never invites" is a stated rule (task 185 §12 I-9, `:1240`): it would replace a code each time the
  pass re-decides a subject.
- Sending the invite from the job needs a person (internal or partner, which changes whether the contact is invited or
  an internal user is told). The job has no such person, and `sprk_grantedby` alone does not settle the invite rule. So
  this is a design decision for UAC-r2, not a fix.

### 5. Known limits (for the folded notes)

- **The invite.** When the reconciliation job grants before the create's queued grant does (the job was redelivered
  until dead-lettered, or the queue was slow), the grant is written without a grantor and no invite is sent, as before
  049 when the inline pass failed. Staff send it with Invite, or the next internal user's save that grants for the
  contact sends it.
- **Queue latency.** `sdap-jobs` is shared with other job types, so under a backlog the create's queued grant can take
  minutes. The reconciliation job (every 5 minutes, and now a candidate by the recent-change scan for 20 minutes) covers
  it either way.
- **The recent window** covers 3 missed ticks beyond the first. A new root whose grant did not complete and whose
  reconciliation runs all failed for longer than 20 minutes is then reached through the full scan (newest first, so
  still inside the bound unless more than 4,999 roots of its type changed more recently) or through the rotation.

### Noticed, not changed (outside this brief)

- `AssignedAccessStore.ScanRestrictedRootsAsync` has the same shape as F2: its comment says "newest change first", but
  it sends no `$orderby` while capping at 4,999 per type. A one-line `QueryOrderedAsync` change would align it.
- `AssignedAccessStore.ScanLedgerRootsAsync` is capped at 4,999 live ledger rows with no order. It decides which roots the job
  visits.
- `NoAccessShareEnforcer`'s remarks on its lease (around `:1000`) still say it "serializes the enforcer with itself
  only — a user's own unshare … do not take it". `/unshare-user` takes it since task 114, and the create-grant job since
  049.

### Gates

- Type-check: `dotnet build tests/unit/Sprk.Bff.Api.Tests`: 0 warnings, 0 errors.
- `verify-suites.sh`:
  - solution build: 0 warnings, 0 errors;
  - filtered suites: **6853 passed, 7 skipped, 0 failed**;
  - ArchTests: 1938 passed, **1 failed**. The failing test is
    `WriterOnlyTablesDeleteGuardTests.EveryGenericDeletePath_RefusesTheWriterOnlyTables_OrIsListed`, which names
    `CallerMetadataReadMemo.DeleteAsync`. That is the 049 Part C decorator meeting 048's D-204 guard after the merge.
    This round does not touch that file. It is for the main session (part A / Part C owner).
- Second filter (`AssignedAccess|ExternalAccess|NoAccess|InternalShare|Reconciliation`): **5332 passed, 0 failed**.

## Fix round 2 (part B): secure from creation

Owner ruling 2026-11-05, with UAC-r2's rulings D1 (a) and D2 (a): a project the wizard creates as secure is secure from
creation.

### What changed

- **Route.** `POST /api/v1/child-records/{table}` takes an optional query parameter, `secure`.
  - `secure=true` is accepted for `sprk_project` and `sprk_workassignment` only.
  - Any other table, or a value that is not `true`/`false` (any case), gets a 400 ProblemDetails with
    `reasonCode = child_record.secure_intent_invalid`. Nothing is read or written.
  - Absent, empty or `false` means no intent: the create is unchanged.
- **Gate (D1).** Before isolation is planned, an intent create asks, as the caller, the same rights
  `DelegationRuleFilter` asks for `provision-project`: Write and the table's Share privilege.
  - It asks on the record as an ordinary create would make it: owned by the team the owner rule names, computed with
    step 7's own decision, asked early.
  - There is no record yet for `RetrievePrincipalAccess`. `CallerWriteAuthorization.RightsOnRecordOwnedByTeamAsync`
    therefore reads the caller's Write and Share grants (`RetrieveUserSetOfPrivilegesByNames`, names from the table's
    metadata) against the owning team's business unit:
    - Global reaches the record.
    - Deep reaches it when the team's unit is the anchor or under it (`UnderOrEqual`).
    - Local reaches it when the team's unit is the anchor.
    - Basic is not counted for a team-owned target (fix round 3, UAC-r2).
    - A grant with no anchor is not counted.
  - The decision is `DelegationRuleFilter.RefusalFor`, the filter's own. The filter now calls it too, so there is one
    rule and no copy.
  - A refusal carries the filter's codes and words (`write_required`, `share_required`). The route renders it in the
    filter's 403 shape (`DelegationRuleFilter.DenyResult`: title, type, `reasonCode`, `traceId`).
  - A rights read that fails or throws, or an owning team whose unit cannot be read, is `check_failed` (deny).
  - Nothing is written on any refusal.
- **Create.** The intent plans the create into isolation (`SecureRootFilingGate` / `SecureRootInheritance.PlanCreateAsync`,
  `secureRequested`). It has the same creator, No Access and named-team checks as a create filed under a secure record.
  - Step 12 completes it with `CompleteIsolatedCreateAsync` (creator share, container, record), then runs the
    "Assigned *" pass under the existing maker-inline / queued rule. That pass therefore sees a record that is already
    secure.
  - A completion that does not finish works as before: no pass runs, nothing is granted, and the row is removed when its
    creator could not be shared. The intent create's outcome words say "as you asked".
  - UAC-r2's A3 rule and the materializer are unchanged.
- **Client** (`src/client/shared/Spaarke.UI.Components`):
  - `IBffCreateOptions.secure` sends `secure=true`.
  - `ProjectService.createProject` sends the intent when the Secure choice is on and the data service routes child
    writes through the BFF (`withBffChildWrites`, as `InAppWizardHost` does). It reports `securedAtCreate`.
  - `provisionSecureProject(..., { securedAtCreate })` treats `sdap.provision.already_provisioned` as success.
    `CreateProjectWizard` and `SummarizeFilesDialog` pass it. Without it, and for every other code, the answer is
    unchanged.

### D2: provisioning after an intent create does only what is left (confirmed; no server change)

- `provision-project` answers `already_provisioned` only when the project is owned by the named team AND has a recorded
  `sprk_containerid` (`RootRow.IsProvisioned`).
- On the isolated path the container is recorded only after the creator share was ensured and proven: in the resume
  order, `EnsureResumeCreatorShareAsync` runs before Step 6/7.
- `sprk_containerid` is server-owned on a root (`RecordTableRules.RootServerOwnedColumns`), so a caller cannot name one.
- An intent create that half-finished (no container) is resumed by the next call, which creates the container: 200,
  not 409.
- Pinned by `ProvisionProjectSecureFromCreationTests`. These are characterization tests, and they pass on the code as it
  was.

### §10 placement and §11

- **Placement.** It extends the existing child-record route and the unified create (`RecordCreationService`, a new
  partial file). There is no new endpoint, service, DI registration or package.
- **Publish size:** to be measured by the main session.
- **Existing:** the isolated-create path (task 158 r1, `SecureRootInheritance`) and `provisionSecureProject` /
  `provision-project`.
- **Extension:** a declared-intent trigger on that path: one optional query parameter, validated on the route.
- **Cost of doing nothing:** a project the wizard creates as secure is not secure from creation.

### Known limits

- **An intent create filed under a secure record is refused.** It is fail closed. The ordinary owner there is the Secure
  Record Owners team, which step 7 refuses for a root, so the gate refuses it. The Create Project wizard files a project
  only after it is created, and a create filed under a secure record is created secure without the intent.
- **The standalone CreateProjectWizard code page is out of scope (task 022).** It is `src/solutions/CreateProjectWizard`,
  which uses `createXrmDataService()` (Xrm.WebApi, undecorated). It creates and then provisions as before, with no
  intent sent.
- **"Try securing again" sends no `securedAtCreate`.** That button (`SecureProvisioningOutcome`) appears only after a
  retryable failure. If a retry after an intent create finds the project complete, it shows the "already secured"
  state.
- **A failed business-unit hierarchy question is "does not reach".** For a Deep grant this gives `write_required` /
  `share_required`, not `check_failed`. It is still a deny.

### Tests

- **BFF tests** (KEEP paths):
  - `tests/integration/data-mutation/ExternalAccess/SecureRootInheritanceWriterTests.SecureFromCreation.cs` (route level,
    REAL gate and provisioning fixture):
    - without Share → 403 `share_required` in the filter's shape, nothing written;
    - Write at a depth that does not reach the owner team (Local elsewhere; Basic, member or not) → 403
      `write_required`;
    - a throw, a failed rights question, or an unreadable team unit → 403 `check_failed`;
    - with both (Local, Deep or Global) → created owned by the named team, flagged, shared to its creator, own
      container; the assignee row is `PendingConfirmation`, with no grant and no share;
    - the creator cannot be shared → removed, nothing granted;
    - no intent (absent, empty, `false`, `FALSE`) → ordinary, and no delegation question is asked;
    - malformed or wrong-table intent → 400 `child_record.secure_intent_invalid`, nothing asked.
  - `tests/integration/auth/UnifiedAccessControl/DelegationRuleSharedPredicateTests.cs`: the `RefusalFor` truth table;
    a source pin that the filter and the create call `RefusalFor`, with no copy of the rule in the create; the depth
    model.
  - `tests/integration/data-mutation/ExternalAccess/ProvisionProjectSecureFromCreationTests.cs`: D2.
  - `SecureChildOwnershipAiToolTests.ScriptedUserClient`: the tables' Write/Share privileges in its metadata, and
    `teammembership_association`.
- **Client tests:**
  - `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/__tests__/secureFromCreation.test.ts`;
  - three cases added to `CreateProjectWizard.secureHoldBack.test.tsx`: `already_provisioned` after an intent create is
    success and nothing is held back; after an ordinary create it is held back; any other code is held back.
- **Failed first:**
  - With the route ignoring the parameter (the code as it was), 15 of the new route tests failed. With the gate removed
    (intent kept), the 6 D1 refusal tests failed.
  - With the five client sources at their earlier versions, 5 client tests failed: the intent create, the refused intent
    create's URL, the two `securedAtCreate` successes, and the wizard's no-hold-back case.

### Code pages and packages to rebuild and deploy

- `src/client/shared/Spaarke.UI.Components`: the shared library (projectService, provisioningService,
  CreateProjectWizard, SummarizeFilesDialog, bffChildWriteAdapter).
- Its consumers that host the in-app Create Project wizard or the Summarize Files dialog through `InAppWizardHost`
  (the host that decorates the data service). The main session confirms the exact set:
  - `src/client/shared/Spaarke.AI.Widgets` (the Create Project wizard widget);
  - `src/solutions/LegalWorkspace`;
  - `src/solutions/SpaarkeAi`.
- These bundle the changed code with no behaviour change, because they use undecorated Xrm.WebApi data services, so no
  intent is sent. A rebuild is optional: `src/solutions/SummarizeFilesWizard`.
- Out of scope: `src/solutions/CreateProjectWizard`. It is the standalone code page on Xrm.WebApi (task 022); a rebuild
  changes nothing there.
- The BFF needs a deploy for the route parameter. Without it, an intent create is answered as before, because ASP.NET
  ignores an unknown query parameter. The project is then created ordinary and provisioned as before.

## Fix round 2 (part C): the other two scans

> Done on `task/fm-049c`. UAC-r2 owns the code changed here and asked for the change. The rule: every candidate is reached within N runs.

### 1. The Restricted-root scan (done)

**Before.** `AssignedAccessStore.ScanRestrictedRootsAsync` sent no `$orderby` and kept at most 4,999 roots per type
(`MaxScanRows`). Its comment said "newest change first", but Dataverse returned whatever prefix it chose, so a Restricted
root changed after the bound was reached could be left out of every run. It supplies the candidates of the Restricted remover.

**After.** Copied from `ScanAssignedRootsAsync` (fix round 1, part B):
- The full scan reads with `QueryOrderedAsync` and `$orderby=modifiedon desc`, so a capped scan keeps the most recently
  changed roots.
- A new `ScanRecentlyChangedRestrictedRootsAsync(rootType, modifiedSince)` reads the Restricted roots with
  `modifiedon ge {since}`, newest first, with its own bound. Both go through one private `ScanRestrictedRootsCoreAsync`.
- `AssignedAccessReconciliationJob` runs it per root type with the same `recentSince` as the first scan
  (`RecentScanWindow` = `TickInterval` x 4 = 20 minutes) and merges the two results. Candidates are keyed by (type, id), so a
  root both scans return is one candidate.

### 2. The ledger-root scan (done)

**Before.** `ScanLedgerRootsAsync` read at most 4,999 live ledger rows with no order. Roots came from those rows, so a
root whose ledger row changed after the bound was reached could be left out of every run. It decides which roots the job visits.

**After.**
- The full scan reads ledger rows newest change first (`$orderby=modifiedon desc`).
- A new `ScanRecentlyChangedLedgerRootsAsync(modifiedSince)` reads the live rows (same predicate: not Revoked, not
  deactivated, no inherited-share row) with `modifiedon ge {since}`, newest first. One private `ScanLedgerRootsCoreAsync`
  builds both.
- The job merges its roots into the ledger candidates (`TryAdd`, so a root that is already a candidate keeps its
  modified time).

### 3. Truncation reporting (the job)

Each of the new reads reports its own `Truncated`, and the job ORs them into the one `truncated` flag, exactly as it
already did for the first scan's two reads. So a capped read of any scan shows as `TRUNCATED` and the run is not clean.
A full scan that is capped stays reported as truncated even though the recent-change scan now reaches the recently
changed roots: the bound is still hit and the rotation covers the rest. The job's remarks now list all six reads.

### 4. Tests (each failed first)

- `AssignedAccessStoreODataTests` (the table doubles now evaluate `$orderby`, `ge` and `sprk_accesspermission`):
  - `TheRestrictedRootScan_ReadsNewestChangeFirst_SoAScanPastItsBoundKeepsARecentlyChangedRoot`: 4,999 old Restricted
    roots and one recently changed root, stored last. It is at the head of the capped result.
  - `TheRestrictedRecentChangeScan_ReturnsOnlyRestrictedRootsChangedSinceItsBound_NewestFirst`: also pins the filter
    (`sprk_accesspermission eq 100000002 and modifiedon ge 2026-11-05T11:40:00Z`) and the order.
  - `TheLedgerRootScan_ReadsNewestChangeFirst_SoAScanPastItsBoundKeepsARecentlyChangedRow`: 4,999 old live rows and one
    recent row, stored last. Its root is inside the capped result.
  - `TheLedgerRecentChangeScan_ReturnsOnlyRootsOfLiveRowsChangedSinceItsBound_NewestFirst`: Revoked and inherited-share rows
    stay out.
- `AssignedAccessReconciliationJobTests` (the fake store's `PastScanBound` now also holds roots back from the Restricted
  and ledger full scans, and reports them truncated):
  - `ARecentRestrictedRootPastTheFullScansBound_IsStillACandidate_AndItsExternalShareIsRemoved`.
  - `ARestrictedRootBothScansReturn_IsOneCandidate_AndEachRecentScanRunsOncePerRootType`: 3 Restricted recent scans and
    1 ledger recent scan per run, 1 candidate.
  - `ARecentLedgerRootPastTheFullLedgerScansBound_IsStillACandidate_AndItsClearedAssignmentIsRevoked`.
  - Negative cases, which pass on both versions (the rotation reaches such a root):
    `ARestrictedRootChangedBeforeTheRecentWindow_AndPastTheFullScansBound_IsNotACandidateThisRun` and
    `ALedgerRootChangedBeforeTheRecentWindow_AndPastTheFullLedgerScansBound_IsNotACandidateThisRun`.
- Failing first: with the Restricted and ledger reads unordered (`QueryAsync`) and the job's two new merges removed,
  **7 fail** (the 4 store tests and 3 job tests above). The 2 negative cases pass on both. All 62 tests in the two classes
  pass after the change.

### 5. The record lease: every holder (comments corrected)

The lease `no-access-enforce:{table}:{id}` (`NoAccessShareEnforcer.RecordLockId`, `IScheduledJobLease`) is taken by four
holders. Each calls `TryAcquireAsync` with `RecordLockId`:

| Holder | Lease taken at |
|---|---|
| `NoAccessShareEnforcer.RemoveUnderRecordLockAsync` | `Infrastructure/ExternalAccess/NoAccessShareEnforcer.cs:1021` (acquire `:1025`) |
| `RestrictedExternalShareRemover` | `Infrastructure/ExternalAccess/RestrictedExternalShareRemover.cs:208` (acquire `:212`) |
| `POST …/unshare-user` | `Api/ExternalAccess/InternalShareEndpoints.cs:678` (acquire `:682`) |
| `AssignedAccessCreateGrantJobHandler` | `Services/Jobs/Handlers/AssignedAccessCreateGrantJobHandler.cs:167` (acquire `:176`) |

(Line numbers are at `226f47453d`; part C's comment edits shift the enforcer's lines.) Nothing else builds
the key: Grep for `no-access-enforce:` finds only the `RecordLockId` definition and comments. Tests that hold it:
`NoAccessEnforceEndpointTests:201`, `AssignedAccessSyncEndpointTests:309`, `InternalUserShareTests:1044`,
`RestrictedExternalShareRemoverTests:376,413`, `AssignedAccessCreateGrantJobTests:277`, `NoAccessShareEnforcerTests:447`.

**Comments fixed (no behaviour change).**
- `NoAccessShareEnforcer.RemoveUnderRecordLockAsync` remarks said the lease "serializes the enforcer with itself only — a
  user's own unshare (task 139) ... do not take it". That has been wrong since `/unshare-user` took it (task 114), then the
  Restricted remover, then the create's queued grant (049). It now lists the four holders and says what does not take it
  (the Share dialog, an update path's inline pass, the sync endpoint, the reconciliation job's own pass).
- The `recordLock` parameter remark on the enforcer's constructor and the `SystemCacheKeys.SchedulerLease` remark named
  the enforcer as the only user. Both now point at the four holders.

### 6. Known limits and not changed

- The recent-change scans share the 20-minute window of the first scan. A root that changes and is not reached by any run
  for longer than that is reached through its full scan (newest first) or the rotation.
- The ledger recent-change scan keys on the ledger row's `modifiedon`. A materializer pass that writes a row refreshes it,
  so a root the job just handled stays a candidate for the window. That costs one read per run and no writes (a pass over
  an unchanged root writes nothing).
- `ScanRootlessLedgerRowsAsync` (capped at 4,999) names rows with no root and drives only the marking of rows whose record was
  deleted. Fix round 3 pages it by row id (see "Fix round 3", item 7).


## Fix round 3

> Branch `task/fm-049b`, from 598805e9ac. Items 1 to 11 of the coordinator's list, in order. Each fix has a test that failed
> first (the mutation or the old file put back), except the comment-only items.

**Facts from the main session.**
- Publish delta vs master f34993dc78: +15,331 B (39.22 MB total, 191/191 files). `dotnet list package --vulnerable`: none.

### 1. A half-finished secure-from-creation create promises no repair that never comes

- **Rule.** The inheritance job visits only records filed under a secure parent (`ListParentsAsync`, then
  `ListFiledRootsAsync`). An intent-created project that is unfiled, or filed under a parent that is not secure, is not
  resumed by it.
- **Wording, intent create only** (`RecordCreationService.AfterCreateAsync`; every non-intent message is unchanged and pinned):
  - creator share failed and the row could not be removed: "an administrator needs to review and remove it"; the Critical log
    in `SecureRootInheritance.CompleteIsolatedCreateAsync` reads "ACTION NEEDED" and no longer says it retries (the new
    `secureFromCreation` argument carries the intent to it);
  - share made, container not: "it is not finished automatically; an administrator can finish securing the {noun}" (the
    warning log says the same), not "completed automatically";
  - row removed again: one neutral sentence, "The project could not be secured, so it was not kept. Please try again or ask an
    administrator." (`RecordCreationService.SecureFromCreationNotSecuredDetail`; the reason code stays in the refusal's code).
- Tests (`SecureRootInheritanceWriterTests.SecureFromCreation.cs`, `.ChildRecordRoute.cs`): 3 failed first (stranded, container,
  removed-again wording). The 3 non-intent pins pass before and after.
- Follow-up filed: #2583, "Secure-from-creation: the inheritance job should also resume a secure root with no container that has
  no secure parent".

### 2. The client error mapping

- `describeWebApiCreateError` maps the three codes `sdap.access.deny.delegation_write_required`, `..._share_required` and
  `..._check_failed` (`DelegationRuleFilter.cs:113,114,120`) to one access-denied sentence: "You don't have the access needed to
  create a secure project. You can create it without Secure, or ask an administrator." (the noun is the caller's).
- The removed-again refusal text is the server's (item 1); there is no client copy of it.
- Jest: 4 failed first; `webApiCreateError.test.ts` 43/43, the CreateProjectWizard suites 239/239 with it, `tsc --noEmit` clean.

### 3. The recent-change window

- `RecentChangeWindow` = `RecentScanWindow` (20 minutes). Ledger-only candidates carry the ledger row's `modifiedon`
  (`AssignedAccessStore` selects it on both ledger root scans, one entry per root with the newest change), so they rank too.
- Doc drift fixed: the handler remark and the Part B text above said 15 minutes.
- 4 tests failed first (window equality, an 18-minute-old root ranked first, a ledger-only root ranked first, the store carrying
  `ModifiedOn`).

### 4. The order test

- `SecureFromCreation_WithWriteAndShare_...` now observes the new row at the moment the "Assigned *" pass asks for its scope:
  creator share = the creator mask, flagged secure, owned by the named team, container set. It fails if the pass is moved before
  `CompleteIsolatedCreateAsync` (run with that mutation).

### 5. The delete-guard exemption

- `WriterOnlyTablesDeleteGuardTests` exempts only the method that implements the scanned API's `DeleteAsync`: the interface map's
  target for an interface API, an override of the base virtual for a class API. Identity is by metadata, not by name.
- Must-fire controls: an extra `DeleteAsync(Guid)` overload on a decorator; a subclass override of another virtual that calls
  `base.DeleteAsync` on a fixed table. Must-not-fire: the real `CallerMetadataReadMemo` and a subclass's override of the base
  `DeleteAsync`. With the old name-keyed exemption the overload control fails.

### 6. The job payload

- `AssignedAccessCreateGrantJobHandler.RequestForAsync`: the grantor oid is used only when it names an active systemuser;
  otherwise the pass runs with no grantor (`GrantorOid` null), a warning is logged (no oid in it) and the grant is never failed.
  The cache tenant is `ImpersonatedRootSetSource.DeploymentCacheTenant(configuration)` (`AzureAd:TenantId`), not the payload's;
  the key exists, so the payload's `CacheTenants` is no longer read.
- 7 tests failed first.

### 7. The rootless-ledger scan

- `AssignedAccessStore.ScanRootlessLedgerRowsAsync(afterId, ct)` reads `sprk_assignedaccessid gt {afterId}` ordered by
  `sprk_assignedaccessid asc`. The job keeps the cursor in memory per instance (it has no durable state store; a restart starts
  at the first row, as its root rotation does). The cursor moves to the last row of a full page, stops before the first row
  deferred at `MaxRootsPerRun`, and wraps to the start once the end is reached.
- Tests: a deletable row behind more stuck rows than one page, and behind more stuck records than one run reads, is reached on
  the second run (2 failed first); the cursor wraps; the store test pins the filter and order.
- Verified on dev (2026-11-06, read-only): Dataverse accepts `gt` on the primary-key column in `$filter`, and paging by it
  returns each row once (213 rows, no duplicates).

### 10. The rights gate no longer counts Basic for a team-owned target

- `CallerWriteAuthorization.RightsOnRecordOwnedByTeamAsync`: only Local, Deep and Global cover a record the owner team will
  own. The team-membership read is gone. The comment at the predicate states the rule.
- Callers of the predicate: `RecordCreationService.SecureFromCreation.cs:85` (the secure-intent create) only.
  `provision-project` is unaffected: its `DelegationRuleFilter` uses `RetrievePrincipalAccess` on the existing record, and shares
  only `DelegationRuleFilter.RefusalFor` (the decision) with the create.
- Tests: the two tests that expected a Basic member to pass are inverted and renamed
  (`RightsOnRecordOwnedByTeam_Basic_AsAMemberOfTheOwningTeam_DoesNotReach`,
  `SecureFromCreation_WithBasicDepth_AsAMemberOfTheOwnerTeam_IsRefused403_AndWritesNothing`); Local, Deep and Global are pinned
  on both the predicate and the create. With the old rule restored the must-fire tests fail (3).

### 11. Comment only

- `CommunicationThreadFilingSweepJob` remarks state the rule, with no environment counts.

### Notes edits for confidentiality (item 8)

- The removals passage under "what the reconciliation job re-derives" is replaced by one rule: "This job re-derives
  removals only when gate G-8's switch is on (#2574, on dev since 2026-11-06)."
- Other places neutralized: Part B condition 1 (the reason for keeping update paths inline), the K2 test table row, the two remarks on which roots the job visits (in "Noticed, not changed" and in the ledger-root scan section),
  the "job trigger and the switch off" remark under condition 2, the Declined / KeptModified / KeptSecureRecord bullets and the
  "for the main session to file" bullet.

## Fix round 4 (scoped re-check of round 3)

- **Secure-from-creation, share made but container not.** The warning no longer names the project or an endpoint: "it is not
  finished automatically; an administrator can finish securing the {noun}." The matching warning log says an administrator
  must finish it. The test pin is renamed and also asserts the text does not name provision-project.
- **Client: a rights check that could not be completed.** `sdap.access.deny.delegation_check_failed` now has its own
  sentence ("Whether you can create a secure {noun} could not be checked. Try again in a moment, or create it without
  Secure.") instead of saying the caller lacks access. A new test pins it for both error shapes.
- **Wording.** The ledger-root scan remarks now say only which roots the job visits. The "Removals" bullet states the
  gate fact alone. The rootless-ledger log line no longer says "the next run" (the id cursor reaches the row again when it
  wraps). The `gt` filter on the primary key is verified on dev, read-only (213 rows, each returned once).
- **Comments.** A test summary that had split from its test is back above it; one `InlineData` comment states the
  round-3 rule.
