# Task 049c — create path within NFR-02, round 2 (notes)

> Branch `task/fm-049c` (local name `task/fm-049c-impl`; a stale local `task/fm-049c` from 2026-11-05 is held by another
> worktree), from `origin/work/field-mapping-server-write-path-r1` at 8a50a12def (049 and 049b merged). Rigor FULL.
> Owner decision 2026-11-07 (decisions.md): NFR-02 stays as written; speed the create path up with no change to what a
> create guarantees. This run covers POML steps 1 to 3. Step 4 (the live re-measure) runs after a dev deploy.

## 1. Per-step timing (added)

`RecordCreationService.StepTrace` now writes each step's time on its own trace line:
`[RECORD-CREATE] {Table} step {Step}: {State} {Note} (correlationId=…, stepMs=…, totalMs=…)`.
`stepMs` is the time since the previous step line (the first: since the pipeline began), which is the gap task 021 read
between lines. The live re-measure can read the split from one request's lines, with no gap arithmetic. Debug level, as
before; a refused or failed step is Warning, as before. No step record, refusal or outcome field changed.

## 2. Read inventory (from the source, Project → Matter wizard create)

The create: `POST /api/v1/child-records/sprk_matter?mappingSourceEntity=sprk_project&mappingSourceId=…`, a Web API body
with three binds (Matter Type, Practice Area, a contact), the owner-kept "Project to Matter (Attorney Matrix)" profile
(8 Copy rules onto `sprk_assigned*` lookups). "Caller" = under the caller's token (OBO); "app" = the BFF identity.
Live, each read is about 75 to 100 ms (task-021-live.md).

| Step | Read | As | Table / path | Filter / key | Repeats (before) |
|---|---|---|---|---|---|
| 1 Principal | WhoAmI | caller | `WhoAmI()` | — | none (`CallerWhoAmIMemo`) |
| 2 TableRules | relationships | caller | `EntityDefinitions(sprk_matter)` | `$expand=ManyToOneRelationships` | **read again at step 3** (the merge) |
| 2 | entity set ×3 | caller | `EntityDefinitions(<related>)` | `$select=EntitySetName`, one per bound table | **each read again at step 3** |
| 3 FieldMapping | record-type ids | app | `sprk_recordtype_refs` | `sprk_recordlogicalname` in (source, target) | none in the request; same answer every create |
| 3 | profile + rules | app | `sprk_fieldmappingprofiles` | source/target type ids, `statecode eq 0`, `$expand` active rules | none in the request; same answer every create |
| 3 | source Read probe | caller | `RetrievePrincipalAccess` on the project | — | none |
| 3 | source read | app | `sprk_projects(<id>)` | the rules' source columns | none |
| 3 | caller mask | caller | `sprk_projects(<id>)` | the same columns (field security, #2188) | none |
| 3 | target columns | caller | `EntityDefinitions(sprk_matter)/Attributes` | `AttributeType, IsValidForCreate` of the rules' targets | none |
| 3 | merge: relationships | caller | as step 2 | as step 2 | **repeat of step 2** |
| 3 | merge: entity sets | caller | `EntityDefinitions(<related>)` | every bound table, caller's and mapped | **repeat of step 2** for the caller's tables; new for a mapped lookup's table |
| 5 CallerChecks | privilege names | caller | `EntityDefinitions(sprk_matter)` | `Privileges` | once per pass (memo since 049) |
| 5 | held Create/Append | caller | `RetrieveUserSetOfPrivilegesByNames` | — | once per pass (never kept: access) |
| 5 | secured columns | caller | `EntityDefinitions(sprk_matter)/Attributes` | `IsSecured` of the row's columns | once per pass |
| 5 | AppendTo per record | caller | `RetrievePrincipalAccess` (+ ownership metadata, held AppendTo, row read for an org-owned target) | each bound record, at most 4 at once | **every record asked in both passes** (the caller's own, then the merged row): access, never kept |
| 6½ / 7 Owner | caller's BU, Secure BU by name, BU default team | app | `systemuser`, `businessunit`, `team` | — | none (UAC-r2's resolver; unchanged) |
| 11 Create | create | app | `sprk_matters` | — | — |
| 12 PostCreate | number read-back, I-1/I-16, the queue send, the maker's share | app | — | — | unchanged (049/049b) |

**What the inventory says.** The pre-create cost is almost all sequential round trips, not work. Three kinds:
1. **Repeats** (option a): the merge at step 3 re-reads the relationships and every entity set step 2 read (1 + 3 reads on
   this create).
2. **Waits on reads that do not depend on what they wait for** (option b): step 3's profile, probe, source read, mask and
   target-column reads depend only on the named source and the two tables, yet wait for step 2; inside step 3 the
   target-column read waits for the source chain; at step 5 the merged check waits for the caller's-own check although
   neither reads the other's answer.
3. **Reads with the same answer for every create** (option c): the record-type ids and the profile with its rules
   (2 app reads), the table metadata (caller reads). Not cached in this run (see section 5).

## 3. Changes (each with what it removes)

### 3.1 Option (a): steps 2 and 3 ask each table-metadata read once

- Steps 2 and 3 read through one `CallerMetadataReadMemo` (the task-049 decorator: schema reads only; access answers,
  record reads and failures are never kept; a write clears it). Step 3's merge takes step 2's relationships and entity sets.
- Steps 3½, 5 (its own memo, as before), 5½ (the pre-write rules get the caller's own client), and 6 to 10 keep their
  clients, so the 049 Part C reasons (the stamp bind's own read; the pre-write rule's client) still hold.
- Removes on this create: 1 + 3 caller reads (≈ 300 ms live).
- **One existing test's fault trigger moved** (assertions unchanged):
  `CreateAsync_WhenMappingFaultsAfterARuleWasSkippedAndAValueDropped_OnlyTheFaultIsReported` threw on step 3's re-read of
  the relationships, a read that no longer happens. It now adds a mapped lookup and throws on the one read step 3 still
  makes after the skip and the drop (the mapped lookup's entity set). Same assertions: one `MappingFaulted` warning, nothing
  mapped, the caller's row created.

### 3.2 Option (b): step 3's reads start beside step 2, for a named source

- `MapFieldsAsync` is split: `ReadMappingInputsAsync` (profile, rules, the app-only-refused rule drop, the probe and the
  app-only source read, the target columns), unchanged in what it reads and says, then the mask and the mapping itself.
- **The #2188 mask stays in `MapFieldsAsync`**, beside the engine call, in the shape the ArchTest
  `MappingSourceCallerMaskGuardTests` accepts (a local assigned only from `MappingSourceCallerMask.ApplyAsync`). It is
  one caller read after the early reads; since those outlast step 2 anyway, it costs nothing on the critical path
  (measured: the split is the same as with the mask inside the early reads). The guard was not changed.
- When the caller NAMED the source (`mappingSource`, which the resolver takes whole whatever the row holds), the reads start
  right after step 1 (`MappingReadsPrefetch`) and step 3 awaits them. Not started for: no mapping, an excluded table, a system
  principal, a half-named source, or a request with server-set lookups (whose K-2 guard can refuse before step 3 reads
  anything).
- **Answers unchanged.** The reads' warnings are added at step 3, in the same order. A fault surfaces at step 3 (nothing
  mapped, `MappingFaulted`), as before. FR-04 holds: the probe still answers before the app-only read, inside the chain.
- **A create refused at step 2** made these reads for nothing: they are cancelled and awaited before `DecideAsync` returns,
  and their answers dropped. Reads only; the source is read app-only only after the caller's Read allowed it.
- **Inside step 3**, the target-column read starts beside the probe and source read (started second). Answers are taken in
  the old order: an unreadable source answers first (the target read is then cancelled and awaited, its answer and fault
  dropped, as when it was never asked); then the mask (an unavailable caller read is the same single warning); then the
  target read's answer (its fault rethrown, or "target unreadable"). Nothing of it is in flight when step 3 answers.
- Removes: the chain's overlap with step 2 (≈ 330 ms live) plus one read inside step 3 (≈ 80 ms).

### 3.3 Option (b): step 5's two checks of a mapped row run together

- `CheckMergedPayloadAsync` starts the caller's-own check and the merged-row check together (in that order) and takes their
  answers in that order. The caller's own refusal answers and its fault propagates whatever the merged check came to; the
  merged check's fault is then dropped (as when it was never asked). Both have returned before the step answers.
- Removes one check's chain (≈ 150 to 250 ms live on a mapped create; nothing on an unmapped one).
- **For UAC-r2.** With both checks in flight, up to 2 × (4 records + the table chain + the column read) caller reads are in
  flight, against 4 + 2 before. Each check keeps its own bound of 4 records.
- **Also for UAC-r2 (review K1).** Started together, the two checks each ask some schema reads the second would have taken
  from the memo when they ran one after the other (privilege names, `IsSecured`, ownership type): up to one extra read
  each on a mapped create. Access answers were never shared, so none is asked fewer times. Under a transient fault, a
  read asked twice is a second chance to fault; the answer is still the first check's, in order.

### 3.4 Not done in this run

- **(c) Caches.** Not needed for the measured cut, and not added: (i) the profile with its rules and the record-type ids
  (2 app reads, ≈ 180 ms) are now inside the reads that run beside step 2, so a cache saves only what of that chain outruns
  step 2 (estimate ≤ 100 ms); (ii) a cross-request cache of caller-read table metadata (ADR-009 allows metadata in an L1
  cache for ≤ 15 min) would remove most of step 2's reads and step 5's metadata reads (estimate 300 to 500 ms), but it
  changes when a schema change is seen and needs a design for the reads that must fail closed today (the stamp bind's own
  read, 049 Part C). Each needs its own invalidation design and tests. Recommended as a follow-up task if the live
  re-measure still exceeds the budget.
- **(d) Batched reads.** No two single reads to one table in this create could be merged without changing a check's shape
  (049 Part C: merging the held-privilege questions is on the security boundary).
- **Owner (≈ 300 ms) and PostCreate (≈ 350 ms).** Not touched. Overlapping step 7 with step 5, or the post-create steps with
  each other, changes the order of the owner question or of the post-create writes, which the POML escalates ("an order").
  The resolver is UAC-r2's surface.

## 4. Local before and after (delayed doubles)

Test `RecordCreationServiceMappingSeamTests.CreatePathTiming.cs`, every round trip 100 ms (caller reads, the probe, the
app-only reads, the owner resolver), an event mapped from a named matter (3 rules, one a lookup). Split from the step lines.

| Step | Before (8a50a12def + timing lines) | After | Saved |
|---|---|---|---|
| Principal | 100 ms | 111 ms | — |
| TableRules | 215 ms | 218 ms | — |
| FieldMapping | **869 ms** | **327 ms** | −542 ms (≈ 5.4 reads) |
| CallerChecks | **324 ms** | **218 ms** | −106 ms (≈ 1 read) |
| Owner | 110 ms | 108 ms | — |
| PostCreate | 109 ms | 109 ms | — |
| **Pre-create total** | ≈ 1620 ms | ≈ 980 ms | **≈ −640 ms (≈ 6.4 reads)** |

## 5. Live estimate (for the main session; step 4 measures it)

Per-read cost about 75 to 100 ms; the Project → Matter create maps 8 lookups.

| Step | Live before (sampled median) | Estimate after | Why |
|---|---|---|---|
| Principal | ~350 | ~350 | unchanged |
| TableRules | ~330 | ~330 | unchanged (the mapping reads now run beside it) |
| FieldMapping | ~900 | ~150–250 | chain of 5 reads beside step 2's 4; merge reads only a mapped lookup's new tables |
| CallerChecks | ~555 | ~350–400 | the merged check (11 records, 3 waves of 4) no longer waits for the caller's own |
| Owner | ~300 | ~300 | unchanged |
| Create | ~200 | ~200 | unchanged |
| PostCreate | ~350 | ~350 | unchanged |

- **Expected:** about −850 to −950 ms at the median. The queued "after" p50 of 2660 ms becomes about 1.7 to 1.8 s; the
  p95 (3509 ms, n = 10) about 2.5 to 2.7 s.
- **Against NFR-02** (before p95 1369 ms, so after p95 ≤ ~1870 ms): **expected still over by about 0.6 to 0.8 s at p95.**
  The residue is mostly Principal, TableRules, Owner, Create and PostCreate, which this run did not change. If step 4
  confirms it, the POML's escalation applies: report the per-step medians (now on each trace line) to the owner. The next
  levers, in order of size: the metadata cache in 3.4 (ii), the owner reads (UAC-r2), and the post-create order (owner
  and UAC-r2).

## 6. Tests (KEEP path `tests/integration/seam/RecordCreation/`)

New file `RecordCreationServiceMappingSeamTests.CreatePathTiming.cs` (7 tests), a partial of the existing mapping seam
fixture. The fixture's doubles gained optional delays (`MappingUserClient.DelayOf`, `ReadProbe.Delay`, the app-only reads'
and the owner resolver's round trips); with none set they answer at once, as before, so every existing test runs as it did.

| Test | Pins | Base (8a50a12def + timing lines) | After |
|---|---|---|---|
| `…ANamedSourceMappedCreate_WithDelayedReads_OverlapsTheMappingReads_AndAnswersAsTheSequentialRun` | the split; same row, warnings and steps as the immediate run | FAIL (FieldMapping 870 ms) | pass |
| `…StepsTwoAndThree_AskEachTableMetadataReadOnce_ButEveryAccessQuestionIsAskedAgain` | option (a); access never kept | FAIL (relationships read 2×) | pass |
| `…TwoChecksWouldFail_TheCallersOwnRefusalAnswers_NeverTheMergedChecksFault` | seeded two-refusal precedence; both checks asked | FAIL (merged check never asked; the refusal assertions passed) | pass |
| `…AStepTwoRefusal_AnswersAsBefore_WhileTheEarlyMappingReadsFault_AndNothingIsCreated` | seeded two-failure precedence (step 2 over the early reads' fault); the early reads were made and faulted first (step 2's relationships read held 6 round trips); no read after the answer | pass | pass |
| `…ASourceTheCallerMayNotRead_AnswersAsBefore_AndTheTargetColumnsReadBesideItIsCancelled` | review F4: a not-taken target read is cancelled and awaited, never completes after the answer | FAIL (on the first shape, which only observed its fault) | pass |
| `…WhenTheEarlyMappingReadsFault_NothingIsMapped_AndTheRowIsCreatedWithTheCallersValues` | AC5: an early fault is step 3's (nothing mapped) | pass | pass |
| `…WhenACheckBothStepFiveChecksAskFaults_TheCreateIsRefused503_AndNothingIsWritten` | AC5: a concurrent check's fault refuses, nothing written | pass | pass |

**Fail-first.** With the base service files (only the step-timing lines added), the mapping seam class ran 179: **3 failed**
(the three marked FAIL, each for the reason given), 176 passed. Restored afterwards. The F4 test failed against the
first implementation (target read only observed) and passes on the fix.

**Existing test changed.** `CreateAsync_ASourceColumnTheCallerCannotRead_IsNotCopied_ButTheReadableOnesAre` pinned the
journal order probe → source read → mask → target columns → owner → create. The target-columns read no longer waits for
the mask (it is independent of the source), so the assertion is split: probe → source read → mask → owner → create, and
target columns → owner → create. The #2188 assertions (the hidden column never copied) are unchanged.
`CreateAsync_WhenMappingFaultsAfterARuleWasSkippedAndAValueDropped_OnlyTheFaultIsReported` keeps its assertions; its fault
trigger moved to the read that step 3 still makes after the skip and the drop (3.1).

## 6a. Review and gates

- **Adversarial review (one pass, opus).** F4 (a not-taken target read left running) and F3 (the step-2-refusal test did
  not prove the early reads had faulted) fixed as above. K1 recorded in 3.3. K2: a step-2 refusal waits for the early
  reads to drain (bounded by one read after cancellation). K3: an in-flight memo read could store after a `Clear()`;
  steps 2 and 3 make no write, so it cannot arise today. K4: the timing bounds are wall-clock, with margins of two round
  trips or more. Verified clean: the named-source match, K-2, the FR-04 order, faults, disposal, the step-5 order.
- **Gates (after the fixes).** BFF build 0 warnings, 0 errors. Mapping seam class 180/180. Filtered BFF run (RecordCreation,
  FieldMapping, AssignedAccess, OfficeQuickCreate, SecureRootCreateRoute, CallerChecksConcurrency, SecureFromCreation,
  ChildRecord, RecordOwnership, Office): 4229 passed, 7 skipped, 0 failed. ArchTests in full: 1998/1998; the #2188 guard
  (`MappingSourceCallerMaskGuardTests`) passes unchanged — the first implementation, which masked inside the early reads,
  failed it, and the mask was moved back beside the engine call rather than the guard changed.

## 7. §10 / §11

- **Placement:** in the BFF; it changes the order of reads inside the existing create service. No endpoint, DI
  registration, package, background work or cache was added.
- **§11** (new surface inside existing files: `ReadMappingInputsAsync`, `MappingReadsPrefetch`, `NamedMappingSource`):
  1. *Existing:* `MapFieldsAsync` (the reads, now split out of it), `CallerMetadataReadMemo` (reused, not extended),
     `CallerWriteAuthorization`'s ordered all-settled walk (049 Part A, the model for 3.3).
  2. *Extension:* these are the existing method's reads moved into a callable unit, plus a private holder that cancels and
     drains them; no other component holds an in-flight read across steps.
  3. *Cost of doing nothing:* step 3's reads wait for step 2 although they never read its answer (≈ 0.4 s per mapped
     create live), inside the NFR-02 budget the owner kept.
- **ADR-009:** no cache was added. The memo's scope grew from step 5 to steps 2–3 (a second instance), within one create,
  schema only (Path C as recorded in 049 Part C).

## 8. Fix round 1 (review findings)

1. **F3 - the CallerChecks bound.** The bound `RoundTrip * 6` passed the sequential code too (328 ms against 600 ms). Three
   runs each way, 100 ms round trips: CallerChecks 327, 326, 329 ms with the checks asked one after the other, 218, 216, 219
   ms asked together. The bound is now `RoundTrip * 2.7` (270 ms, the midpoint: about half a round trip either side).
   Reverting the overlap (the merged check started after the caller's own returns) fails it: "Expected ... to be less than
   270ms ... CallerChecks=328ms". The remark in the test file now gives the real margins (FieldMapping 5 round trips is
   about 1.7 above the measured 326 ms and 3.7 below the sequential 869 ms). The overlap is also pinned without a clock:
   `CreatePath_StepFiveChecks_AreInFlightTogether_...` holds the caller's own first read of the matter until the merged
   check's read of the mapped contact is in flight (a new `BeforeAnswer` gate on the fixture's user double); on sequential
   code the hold runs out after 3 s and the test fails. `CreatePath_TwoChecksWouldFail_...` also fails on sequential code
   (the merged check is never asked), so it pins the overlap as well.
2. **K - the merged check is cancelled when the caller's own check refuses or faults** (`CheckMergedPayloadAsync`). Both
   checks take a token linked to the request's; on the refuse and fault paths the link is cancelled (`NotTakenAsync`, as
   step 3 does) before the merged check is waited for. The answer is unchanged: the caller's own refusal or fault answers
   first; the cancellation is dropped with the merged check's answer; only the request's own cancellation propagates.
   Test `CreatePath_WhenTheCallersOwnCheckRefuses_TheMergedCheckIsCancelled_AndTheRefusalIsUnchanged` holds the merged
   check's read of a mapped lookup's record, and asserts the refusal equals the unmapped run's and the held read saw
   cancellation. Fails before the change (the held read runs its full 3 s, not cancelled).
3. **Trivial.** A comment named `StartMappingReads`; it now says `MappingReadsPrefetch.Start`.

Known limits (recorded, not fixed here):

- **(K)** A create refused at step 2 still runs the early mapping reads it never needs: an app-only profile read, a Read
  probe and, when allowed, an app-only source read that is thrown away; up to three log lines; and read-audit noise on the
  source table when auditing is on. No data is fetched before the probe allows it; nothing is cached, returned or written.
- **(K)** The order in which access questions are asked changed: the FR-04 Read probe is now asked before the step-2 checks.
  The answer precedence is unchanged. UAC-r2's acknowledgement is being sought by the main session.
- **(K4)** The timing tests use wall-clock bounds (the CallerChecks and FieldMapping ones); the two ordering tests above do
  not.
