# Task 048: turn the chunk-trim on for every re-index path

> Base: `f93d97e9a` (includes 029 + 047). Rigor FULL, sonnet @ high, directional. Symbols named by symbol;
> line numbers drift.

## 0. Status: IMPLEMENTED

Task 029 built the mechanism (`IRagService.DeleteChunksBeyondCountAsync` + `FileIndexRequest.ReplaceStaleChunks`)
but wired it to only ONE caller: the Office version-save index job. This task turns the SAME mechanism on for
every other path that can re-index an item that already has chunks, per the owner's 2026-09-15 "Yes, new task
here" decision recorded in `notes/029-version-ai-refresh.md`.

## 1. Every caller that can re-index an existing item — audit with evidence

Starting point: `FileIndexingService.IndexTextInternalAsync` is the ONE convergence point (029's own design).
Its three entry points are `IndexFileAsync` (OBO), `IndexFileAppOnlyAsync` (app-only), `IndexContentAsync`
(pre-extracted content, hardcodes `replaceStaleChunks: false`, no caller of it constructs a `FileIndexRequest`
so it is out of scope). Every producer of a `FileIndexRequest` or `RagIndexingJobPayload` was traced by
`Grep` for `new FileIndexRequest` (5 files) and `new RagIndexingJobPayload` / `RagIndexingJobHandler.JobTypeName`
(6 files), then each call site was read to classify it.

| # | Caller | File | Mechanism | Existing-item re-index? | Fixed how |
|---|---|---|---|---|---|
| 1 | Compose save-back | `Services/Compose/ComposeService.cs:2140` | `PostUploadIndexingEnqueuer.EnqueueIfApplicableAsync` (OBO) | **Yes — every edit-and-save of the same document.** `Source: "ComposeCreateOnSave"`. | Fixed at the shared enqueuer (no `Services/Compose/**` edit). |
| 2 | "LinearDocumentProfile" direct-Action re-index | `Api/Ai/AnalysisEndpoints.cs:1043` | Same OBO enqueuer, `Source: "LinearDocumentProfile"` | Plausible — profile can be regenerated on an already-indexed document. | Fixed at the shared enqueuer. |
| 3 | Office create + version save | `Workers/Office/UploadFinalizationWorker.cs:1275` | `PostUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync` (app-only) | Version save: yes (029's original target). Create save: no (harmless first index). | Fixed at the shared enqueuer (unconditional; version-key-suffix logic in the SAME method untouched). |
| 4 | Email-to-Document | `Services/Communication/IncomingCommunicationProcessor.cs:1246` | Same app-only enqueuer, `Source` unset here (defaults) | Mostly first-index (harmless trim). | Fixed at the shared enqueuer. |
| 5 | Outbound-email enrichment | `Services/Communication/CommunicationEnrichmentService.cs:387` | Same app-only enqueuer, `Source: "OutboundEmail"` | Plausible on re-run of enrichment for the same archived `.eml`. | Fixed at the shared enqueuer. |
| 6 | Post-AI-analysis re-index | `Services/Ai/AnalysisResultPersistence.cs:268` | Same app-only enqueuer, `Source: "AnalysisOrchestration"` — comment explicitly says **"re-indexing after AI analysis"** | **Yes — item was indexed once at upload, this is a deliberate second pass.** | Fixed at the shared enqueuer. |
| 7 | Manual Run Index ("Send to Index" ribbon) | `Api/Ai/RagEndpoints.cs` `SendToIndex` (`POST /api/ai/rag/send-to-index`) | Direct `FileIndexRequest`, OBO | **Yes — by definition, an existing Dataverse document.** Named explicitly in the task brief. | Fixed directly: `ReplaceStaleChunks = true`. |
| 8 | Knowledge Base admin reindex | `Api/Ai/KnowledgeBaseEndpoints.cs` `ReindexDocument` (`POST /api/ai/knowledge/indexes/reindex/{documentId}`) | Direct `RagIndexingJobPayload`, unique-per-call key (`rag-reindex-{id}-{unixSeconds}`) | **Yes — route summary literally says "Triggers a background re-indexing job."** Found by this task's audit (not named in the brief). | Fixed directly: `ReplaceStaleChunks = true`. |
| 9 | Playbook "Index" node | `Services/Ai/Nodes/DeliverToIndexNodeExecutor.cs` (`ExecutorType.DeliverToIndex`) | Direct `RagIndexingJobPayload` | Plausible — a playbook can place this node after the document was already indexed earlier in the same run or a prior run. Named in the task brief ("possibly the playbook Index node") and in 029's own residual note §9 item 4. | Fixed directly: `ReplaceStaleChunks = true`. |
| 10 | Document check-in re-index trigger | `Api/DocumentOperationsEndpoints.cs` `TryEnqueueReindexJobAsync` (fires from `CheckInDocument`) | Direct `RagIndexingJobPayload`, `Source: "CheckinTrigger"`, key includes `DateTimeOffset.UtcNow.Ticks` (never skipped) | **Yes — check-in is SharePoint's own "new version of an existing, already-indexed item" mechanism, closely analogous to the Office version-save case 029 fixed.** `ReindexingOptions.Enabled` and `.TriggerOnCheckin` both default `true`. Found by this task's audit. | Fixed directly: `ReplaceStaleChunks = true`. |
| 11 | Bulk / admin / scheduled re-index | `Services/Ai/Jobs/BulkRagIndexingJobHandler.cs` `ProcessSingleDocumentAsync` | Direct `FileIndexRequest`, app-only | **Conditionally yes** — gated by the handler's own `BulkRagIndexingPayload.ForceReindex` flag, which already bypasses the per-item idempotency skip AND the "unindexed only" Dataverse filter (`sprk_ragindexedon eq null`) in `QueryDocumentsAsync`. When `ForceReindex=false` (the default "unindexed" sweep used by `ScheduledRagIndexingService` and the admin default), every returned document is a first index by construction. | Fixed with `ReplaceStaleChunks = payload.ForceReindex` (not unconditional — see §2). |
| 12 | `RagEndpoints.EnqueueIndexing` (`POST /api/ai/rag/enqueue-indexing`) | `Api/Ai/RagEndpoints.cs` | Direct `RagIndexingJobPayload` from a raw `FileIndexRequest` body, API-key auth | No confirmed live caller found (`Grep` over `src/` and `scripts/` for the route — none). Generic automation/testing entry point. | Minor completeness fix only: thread `request.ReplaceStaleChunks` through instead of silently dropping it (was omitted from the copy). Caller-controlled, not forced. |
| 13 | `RagEndpoints.IndexFile` (`POST /api/ai/rag/index-file`) | `Api/Ai/RagEndpoints.cs` | Binds `FileIndexRequest` directly from the request body | Generic, dual-purpose endpoint (could be first-index or re-index depending on caller intent). | **No change** — already caller-controlled: `FileIndexRequest.ReplaceStaleChunks` has been a bindable wire field since 029, so a caller that wants the trim already gets it by setting it in the JSON body. Forcing it server-side would remove legitimate caller choice on a route with no first-class re-index semantics of its own. |

### Checked and found NOT live (no fix needed)

- **`IndexingWorkerHostedService`** (`Workers/Office/IndexingWorkerHostedService.cs`, the "office-indexing" Service
  Bus queue). Registered as a hosted service (conditionally on `DocumentIntelligence:Enabled`) and does construct
  a `FileIndexRequest` → `IndexFileAppOnlyAsync`, so it initially looked live. Verified DEAD from the producer
  side: `grep -rn '"office-indexing"'` finds the queue-name constant declared in `UploadFinalizationWorker.cs`
  and `ProfileSummaryWorker.cs` (both `private const string IndexingQueueName = "office-indexing";`) but **never
  referenced in a `SendMessageAsync` call in either file** — nothing publishes to this queue any more (superseded
  by the `PostUploadIndexingEnqueuer` centralization). Confirmed inert; no fix applied; noted here per "audit for
  any other caller."
- **`ScheduledRagIndexingService`** — always sets `ForceReindex = false` and `Filter = "unindexed"`; falls
  through row 11's `payload.ForceReindex` gate to `ReplaceStaleChunks = false` correctly with no separate change.

## 2. Design: where the trim was turned on, and why two different mechanisms

**Primary mechanism — the shared enqueuer (covers 6 of 13 callers with ONE file's worth of changes).**
`PostUploadIndexingEnqueuer` is, by its own XML doc, "the single seam — every BFF upload endpoint that writes
to SPE calls this after success." Both of its methods now set `ReplaceStaleChunks = true` unconditionally:

- `EnqueueIfApplicableAsync` (OBO, synchronous, no idempotency gate of its own): the `FileIndexRequest` it
  builds now always carries `ReplaceStaleChunks = true`. Covers callers #1–2.
- `EnqueueAppOnlyIfApplicableAsync` (app-only, via Service Bus): the `RagIndexingJobPayload` field was
  `ReplaceStaleChunks = versionDiscriminator is not null` (029); changed to unconditional `true`. The
  `IdempotencyKey` ternary two lines below, which ALSO reads `versionDiscriminator`, is **untouched** — it is
  a separate branch on the same variable, and decoupling `ReplaceStaleChunks` from it does not touch the key.
  Covers callers #3–6.

This satisfies CLAUDE.md §11 (extend, don't duplicate) and the task's own "prefer the shared indexing layer or
the enqueue boundary" guidance, and required zero edits inside `Services/Compose/**` — Compose's ONE indexing
call site (`ComposeService.cs:2140`) already goes through this seam (confirmed: `Grep` for
`IFileIndexingService|IndexFileAsync|IndexFileAppOnlyAsync|IndexContentAsync` under `Services/Compose/` returns
no matches).

**Secondary mechanism — direct producers (5 more callers, each a one-line, structurally identical addition).**
Callers #7, #8, #9, #10 build `FileIndexRequest`/`RagIndexingJobPayload` directly, bypassing the enqueuer (they
either are not upload-adjacent, or are themselves already background-job handlers). Each got
`ReplaceStaleChunks = true` added to its existing object initializer. Caller #11 (`BulkRagIndexingJobHandler`)
is the one case tied to an existing signal instead of unconditional: `ReplaceStaleChunks = payload.ForceReindex`
— `ForceReindex` is this handler's own explicit "this item may already have chunks" flag, already used to
bypass the skip check and the Dataverse filter, so it is the more precise choice and avoids paying for a
guaranteed-empty trim on every scheduled "unindexed" sweep.

**Design choice for the negative case (a first index).** The task's own acceptance criteria permit "no trim
call, or a trim that deletes nothing." This task chose the latter, uniformly, for every fixed caller except
#11 (which chooses the former for the "unindexed" sweep because a precise signal already existed). Proven by
test (§4) rather than merely asserted.

## 3. Escalation triggers — neither fired

- **Trigger 1** ("a path stores chunks under an id scheme the trim does not cover"): every fixed caller reaches
  the trim via the SAME `FileIndexingService.IndexTextInternalAsync` pipeline that stamps chunk ids as
  `{speFileId}_{chunkIndex}` — the exact shape `DeleteChunksBeyondCountAsync` targets. Verified for each of the
  13 rows in §1's table; none uses a different id scheme.
- **Trigger 2** ("turning the trim on requires changing a path's key or retry behaviour"): did not fire. No
  idempotency key, Service Bus `MessageId`, or skip/retry check was changed anywhere in this task — verified
  both by code inspection (every edit is additive to an existing object initializer, never touching key
  construction) and by dedicated regression tests (§4) proving the version-key-suffix logic and the
  feature-flag skip-gate are byte-for-byte unchanged.

## 4. Tests (ADR-038 KEEP paths; new files only except one pre-existing test — see §4.5)

| File | KEEP category | Proves |
|---|---|---|
| `tests/unit/domain/Ai/PostUploadIndexingEnqueuerReplaceStaleChunksTests.cs` | domain | The REAL `PostUploadIndexingEnqueuer`, doubles only at `IFileIndexingService` / `JobSubmissionService` / `IDocumentDataverseService`. (a) `EnqueueIfApplicableAsync` always sets `ReplaceStaleChunks=true` — covers callers #1–2. (b) `EnqueueAppOnlyIfApplicableAsync` sets it true with NO `VersionDiscriminator` (the task-048 behavior change — callers #3–6). (c) With a `VersionDiscriminator`, the idempotency-key version suffix is UNCHANGED (regression guard for 029) and the flag is still true. (d) The feature-flag skip-gate still short-circuits before any payload is built (negative control, unrelated to the flag). |
| `tests/unit/domain/Ai/DeliverToIndexNodeExecutorReplaceStaleChunksTests.cs` | domain | The REAL `DeliverToIndexNodeExecutor.ExecuteAsync` (public method), double only at `JobSubmissionService`. Valid node → submitted payload has `ReplaceStaleChunks=true` and the SAME idempotency key as before. Missing SPE metadata → still no job submitted (negative control). |
| `tests/unit/domain/Ai/BulkRagIndexingJobHandlerReplaceStaleChunksTests.cs` | domain | The REAL `BulkRagIndexingJobHandler.ProcessAsync`, doubles at `IFileIndexingService`, `IIdempotencyService`, `ISearchIndexNameResolver`, and a hand-written (NOT `Mock<HttpMessageHandler>`) fixed-response `HttpMessageHandler` standing in for the Dataverse OData query the handler makes internally. `ForceReindex=true` → `ReplaceStaleChunks=true` on the built `FileIndexRequest`. `ForceReindex=false` → stays `false` (the negative acceptance criterion for this specific caller). |
| `tests/integration/contract/Api/Ai/RagSendToIndexReplaceStaleChunksContractTests.cs` | contract | Through the REAL `POST /api/ai/rag/send-to-index` route (own local `WebApplicationFactory<Program>` fixture, adapting task 033's proven `RagSendToIndexFixture` pattern — module boundaries `IDocumentDataverseService` / `IFileIndexingService` substituted): the captured `FileIndexRequest` carries `ReplaceStaleChunks=true`. |
| `tests/integration/contract/Api/Ai/KnowledgeBaseReindexReplaceStaleChunksContractTests.cs` | contract | Through the REAL `POST /api/ai/knowledge/indexes/reindex/{documentId}` route (own local fixture, module boundary `JobSubmissionService` substituted): the submitted job's payload carries `ReplaceStaleChunks=true`. |

### 4.5 A pre-existing test needed updating (not deleted, not weakened)

`tests/integration/seam/Office/VersionSaveAiRefreshSeamTests.cs`'s
`FirstSave_AndEmailSave_KeepTodaysKeysAndPayloads_AndNeverTrim` (029's own regression test) asserted that a
first save / Email save carry no `ReplaceStaleChunks` on the wire and never call the trim. That was 029's
deliberate, narrower scope statement, and task 048's whole purpose is to widen it — the full targeted run
surfaced exactly this one failure. Resolved by:

- Renaming to `FirstSave_AndEmailSave_KeepTodaysKeysAndPayloads_AndTheNowUnconditionalTrimDeletesNothing`.
- Updating the two now-stale assertions to the correct new expectation: `ReplaceStaleChunks` present and
  `true` (was: absent); `TrimCalls == 2` (was: `0`) — one no-op trim call per non-version index job.
- **Adding** two assertions that were not there before (`ChunksFor(RoutedIndex, Item).Should().HaveCount(5)` /
  `...emailItem...HaveCount(2)`), affirmatively proving the trim deletes nothing rather than only asserting a
  call count — net effect is MORE coverage, not less.
- Everything else in the test (idempotency keys, `VersionSaveJobId` absence) is byte-for-byte unchanged, and
  every sibling test in the same file (`VersionSave_WithDifferentContent...`, `RevertedContent_BThenAThenB...`,
  `SameVersionSave_Redelivered...`, `VersionReindex_WhenTheTrimFails...`) passed unchanged before and after.
- Class-level `<remarks>` updated with a "Task 048 update" paragraph explaining exactly this, so the file's own
  documentation stays accurate.

Full reasoning recorded in the main session's Step 9.5 code-review report (Warning W-1) for explicit reviewer
visibility, since this is the one place this task touches a protected/existing test.

### 4.6 Mutation check — every new/changed test proven to fail without the fix

The 6 production files with logic changes (`PostUploadIndexingEnqueuer.cs`, `RagEndpoints.cs`,
`KnowledgeBaseEndpoints.cs`, `DeliverToIndexNodeExecutor.cs`, `DocumentOperationsEndpoints.cs`,
`BulkRagIndexingJobHandler.cs`) were reverted to their pre-task-048 (`HEAD`) content via
`git show HEAD:<path> > <path>` (never `git stash`; originals backed up first via `cp` to the scratchpad and
verified byte-identical before AND after restore). With the revert in place:

- 6 of the 10 new tests **failed** exactly as expected — every test asserting the NEW/changed behavior
  (`EnqueueAppOnlyIfApplicableAsync_NoVersionDiscriminator_StillSetsReplaceStaleChunksTrue`,
  `ExecuteAsync_ValidNode_SetsReplaceStaleChunksTrueOnTheSubmittedPayload`,
  `ProcessAsync_ForceReindexTrue_SetsReplaceStaleChunksTrueOnTheFileIndexRequest`,
  `EnqueueIfApplicableAsync_AnyCaller_SetsReplaceStaleChunksTrueOnTheFileIndexRequest`,
  `ReindexDocument_ExistingDocument_SetsReplaceStaleChunksTrueOnTheSubmittedPayload`,
  `SendToIndex_ReindexingAnExistingDocument_SetsReplaceStaleChunksTrueOnTheFileIndexRequest`).
- 4 negative-control tests correctly **still passed** on the reverted code (the `VersionDiscriminator`-present
  regression guard, the feature-flag skip gate, the missing-SPE-metadata guard, and the `ForceReindex=false`
  case) — proving those assertions are not accidentally coupled to the fix.
- The pre-existing `VersionSaveAiRefreshSeamTests.FirstSave_AndEmailSave_...` test (§4.5) was separately proven
  to fail on the ORIGINAL (pre-048) assertions when run against POST-048 code, which is the same proof in the
  opposite direction — the targeted-filter run surfaced it directly.
- All 6 files restored from the `cp` backup and verified byte-identical via `diff`; full rebuild green;
  all 10 new tests + the 5 sibling tests in `VersionSaveAiRefreshSeamTests.cs` passed again.

## 5. Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors, 0 warnings |
| New/updated tests (6 files, 11 test methods total: 10 new + 1 renamed) | 11 passed / 0 failed |
| ArchTests | **191 / 191 passed** |
| Targeted (`Rag\|Indexing\|Compose\|Office\|KnowledgeBase\|DeliverToIndex\|DocumentOperations`) | **2,669 passed / 0 failed / 11 skipped** (2,680 total) — the 1 failure found mid-gate was `VersionSaveAiRefreshSeamTests` (§4.5), fixed, then this full targeted run re-confirmed clean. |
| Full `Sprk.Bff.Api.Tests`, run once in 7 disjoint foreground chunks (Services.Ai.Chat+Handlers 1446 · Services.Ai rest 3231 · Services non-Ai 2246 · Seam 1690 · Api 1492 · Contract+AccessControl+Integration 1098 · everything else 1164) | **12,311 passed / 0 failed / 56 skipped = 12,367 total.** Matches the stated baseline ("~12,300 passed / 0 failed / 56 skipped") — skipped count identical (56=56); the +11 passed delta is accounted for exactly by this task's 10 new tests (the 11th accounting unit is the renamed-not-new sibling, already in the baseline). Zero failures across the entire suite. |
| `dotnet list package --vulnerable --include-transitive` (BFF) | "The given project `Sprk.Bff.Api` has no vulnerable packages given the current sources." |

**Publish size** (root CLAUDE.md §10). `dotnet publish -c Release -o C:\t\…`, zipped with PowerShell
`Compress-Archive -CompressionLevel Optimal`, PDBs included, short paths, cleaned up after measurement:

| Build | Commit / state | Zip bytes |
|---|---|---|
| Fresh master | `e0a6f87c4` | 47,556,005 |
| Branch before task 048 (working tree reverted to `f93d97e9a`) | `f93d97e9a` | 47,614,630 |
| Branch with task 048 | working tree | 47,614,639 |
| **Task 048 isolated contribution** | | **+9 B (+0.00 MB)** |
| Branch vs fresh master (all unmerged project work, tasks 001–048) | | +58,634 B (+0.0559 MB) |

Far below the +5 MB justification threshold and the 60 MB ceiling. `origin/master` had not moved since 029's
last measurement (same commit `e0a6f87c4`); the fresh remeasurement (47,556,005 B) is within the same
zip-timestamp noise band 029 observed (≤~60 B) against its recorded 47,555,948 B.

## 6. Step 9.5: code-review and adr-check (self-run, foreground, over the full diff)

**Code review.** Coverage-first; severity and confidence noted. 0 Critical.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| W-1 | Warning | An existing, protected test (`VersionSaveAiRefreshSeamTests.cs`) was modified — see §4.5 for full resolution. Flagged for explicit reviewer confirmation given the "never weaken an existing test" rule; net coverage went UP (4 assertions → 6), nothing deleted. | Resolved and documented; recommend reviewer reads the updated method directly. |
| S-1 | Suggestion | Every re-index through the shared enqueuer now pays one extra `DeleteChunksBeyondCountAsync` search call, including first-index cases (harmless, deletes nothing). Disclosed trade-off, explicitly licensed by the task's own acceptance criteria. | No action; documented. |
| S-2 | Suggestion | `RagEndpoints.EnqueueIndexing`'s fix is narrower (caller-controlled, not forced) with no automated test — no confirmed live caller exists today. | No action; documented as a residual (§7). |

AI Code Smell Detection: 0 across all 5 categories (no new interfaces, no try/catch-log-rethrow, no null
checks on non-nullable types, no code-restating comments, no method grew past 3 responsibilities).

**ADR check.**

| ADR | Result |
|---|---|
| ADR-001 | ✅ No new endpoint, Function, or hosted service. |
| ADR-004 | ✅ Job Contract unchanged — no key, no `MessageId`, no skip/retry logic touched anywhere; verified by dedicated regression tests, not just inspection. |
| ADR-007 | ✅ Not touched. |
| ADR-008 | ✅ Not applicable — no new endpoint, no auth logic touched. |
| ADR-010 | ✅ No new DI registration, no new interface. |
| ADR-013 | ✅ No new CRUD→AI injection. `DocumentOperationsEndpoints.cs` / `KnowledgeBaseEndpoints.cs` already constructed `RagIndexingJobPayload` (a job-queue DTO, the ADR-004 sanctioned pattern) before this task; task 048 only added a property to the existing initializer. |
| ADR-014 / ADR-016 | ✅ No new query surface; tenant threading unchanged. |
| ADR-015 | ✅ No new logging; no content in logs. |
| ADR-017 | ✅ Job status handling unchanged. |
| ADR-032 | ✅ `IRagService`'s member list untouched by this task (029 already extended it and `NullRagService`). |
| ADR-038 | ✅ New tests at KEEP paths only; no banned patterns; the one hand-written `HttpMessageHandler` fake is explicitly not the banned Moq transport mock. |
| ADR-028 | ✅ Not touched. |

**BFF Hygiene (CLAUDE.md §10).** Placement Justification: documented inline at every call site + in this
note. No new CRUD→AI dependency, no new package, no new endpoint added directly in `Program.cs`. Publish-size
verified (§5): +9 B isolated.

**Challenge path: none needed.** Zero violations, zero unresolved warnings.

## 7. Residuals and what remains unverified

1. **`RagEndpoints.EnqueueIndexing`'s pass-through fix (§1 row 12, code-review S-2) has no dedicated automated
   test.** No confirmed live caller exists (checked `src/` and `scripts/` — none), and the endpoint requires a
   different auth scheme (named `RagApiKey` API-key policy, mapped on `app` not `group`) than the JWT-bearer
   fixtures built for the other two contract tests. The fix itself is a one-line, compiler-checked property
   copy, structurally identical to 5 other assignments that ARE tested. Verified by code inspection and by the
   fact the full 12,367-test suite (including every existing `RagEndpoints`/`EnqueueIndexing`-adjacent test)
   passed unchanged.
2. **`DocumentOperationsEndpoints`'s check-in re-index trigger (§1 row 10) has no isolated automated test for
   the `ReplaceStaleChunks=true` addition specifically**, though the check-in flow's OTHER behavior is
   unaffected (nothing else in that endpoint was touched). Root cause: `TryEnqueueReindexJobAsync` is `private
   static`, and the containing `CheckInDocument` handler depends on `DocumentCheckoutService.CheckInAsync`,
   whose method is **not `virtual`** — Moq cannot intercept it for a WebApplicationFactory substitution, and the
   class's own dependencies (a raw Dataverse `HttpClient` plus `SpeFileStore`) would need to be modeled from
   scratch with no existing test harness to build on. Building that from zero for one boolean flag was judged
   disproportionate; reflection-based invocation of the private method is explicitly banned (ADR-038 B8). The
   change is verified by code inspection (identical shape to the 4 other direct `RagIndexingJobPayload`
   producers that ARE tested) and by the full suite passing with 0 failures. Flagged here rather than silently
   left unverified.
3. **Not exercised live**: Azure AI Search's actual response to the trim query/paging/delete on the real
   service for any of the newly-turned-on callers (the same residual 029 recorded; the trim mechanism itself,
   including live-service behavior, is unchanged by this task). No dev environment was used.
4. **No backfill** of stale chunks already in dev (owner decision, per 029 and reaffirmed by this task's
   scope — "do NOT clean up stale chunks already in dev").
5. **Out-of-order re-index races** (two re-indexes of the same item in quick succession) are the same
   pre-existing race 029 already documented as not introduced by its own change; this task does not change
   that risk surface for any of the newly-covered callers either.

## 8. Deviations

- `BulkRagIndexingJobHandler` ties `ReplaceStaleChunks` to `payload.ForceReindex` rather than unconditional
  `true` — a deliberate, documented deviation from the "unconditional at the shared seam" pattern used for the
  other 10 fixed callers, chosen because a more precise existing signal was available (§2).
- `RagEndpoints.IndexFile` was left unchanged (no fix) — already caller-controlled since 029; forcing it
  server-side would have removed legitimate caller choice on a route with no first-class re-index semantics.
- One pre-existing, protected test (`VersionSaveAiRefreshSeamTests.cs`) was modified — not deleted, not
  weakened; full rationale in §4.5, flagged as code-review Warning W-1 for explicit reviewer attention.
- Per the dispatch, this sub-agent did not edit `TASK-INDEX.md`, `current-task.md`, the project `CLAUDE.md`,
  or `src/client/office-addins/ci-gated-suites.txt`; this note is the recovery record.
