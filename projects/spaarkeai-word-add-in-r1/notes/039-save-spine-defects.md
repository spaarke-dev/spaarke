# Task 039: Office save-spine idempotency defects (findings F1–F4 from task 024)

> Base: `b5c08f26f` (project branch head). Rigor FULL, opus @ xhigh, directional.
> Reproduce-first (root CLAUDE.md §10 §F.3): every test below was written and run against the unchanged
> production code before any fix. Symbols are named by symbol, not by line.

## 0. Summary

| # | Finding | Reproduced? | Fixed? | Failing → passing test |
|---|---|---|---|---|
| 4 | 🔴 A content-blind CREATE key silently drops an edited second save | **Yes, at two layers** | Yes | `OfficeSaveSpineIdempotencyTests.SecondCreateSave_OfEditedContent_UnderThePanesReusedHeaderKey_IsWritten`, `…_WithNoClientKey_IsWritten`, `OfficeSaveSpineIdempotencyContractTests.DocumentCreateKey_CarriesTheContentHash` |
| 2 | A failed job answers its retries as a duplicate | **Yes**, plus an unreported variant (a job stranded Running by an exception) | Yes | `RetryAfterALockedVersionSave_WithTheSameServerKey_StartsANewAttempt_AndWritesTheVersion`, `RetryAfterACreateSaveThatThrewMidway_WithTheSameKey_StartsANewAttempt` |
| 3 | A job completes with no document id | **Yes** (create, version, Email, and the immutable-dedup branch) | Yes | `CompletedCreateSave_…`, `CompletedVersionSave_…`, `CompletedEmailSave_…`, `AttachmentDeduplicatedToAnExistingCanonical_…` |
| 1 | The handler reads `X-Idempotency-Key` and never uses it | **Yes** (dead local, by reading). Its behavioural effect is the header's content-blind replay | Yes: one authoritative source | `VersionSaves_WhoseHeaderAndBodyKeysDisagree_AreDecidedByTheBodyKey_NotReplayedUnderTheReusedHeader` |

Guards that pass both before and after (the escalation triggers did **not** fire):
`VersionSaveKey_IsUnchangedByTask039`, `EmailAndAttachmentKeys_AreUnchangedByTask039` (×2),
`ByteIdenticalCreateRetry_UnderTheSameHeaderKey_IsReplayed_AndWritesNothing`,
`ByteIdenticalCreateRetry_UnderADifferentHeaderKey_IsDeduplicatedByTheServerKey`,
`EmailSave_UnderAReusedHeader_WithADifferentBody_IsStillReplayedFromTheResponseCache_AsBefore`,
`DocumentSave_WithAHeaderKeyOnly_IsKeyedByTheServer_NeverByTheHeader`.

## 1. Reproduction evidence (before the fix, run against `b5c08f26f`'s production code)

17 tests: **11 failed, 6 passed**. The 6 passes are exactly the guards listed above.

| Test | Before (verbatim assertion) |
|---|---|
| F4 `…UnderThePanesReusedHeaderKey_IsWritten` | `Expected world.UploadSmallCalls to be 2 because the edited save reaches SPE — it is not answered from the response cache, but found 1.` |
| F4 `…WithNoClientKey_IsWritten` | `Expected response.StatusCode to be HttpStatusCode.Accepted {value: 202} because a different document is a different operation, but found HttpStatusCode.OK {value: 200}.` (200 = `duplicate: true` from the persistent job lookup) |
| F4 `DocumentCreateKey_CarriesTheContentHash` | `Expected … "gObMtWGU…", but "haLnTOoB…" differs` (the old content-free create key) |
| F2 `RetryAfterALockedVersionSave_…` | `Expected retry.StatusCode to be … Accepted {value: 202} because a failed attempt is not a duplicate of the retry, but found … OK {value: 200}.` |
| F2 `RetryAfterACreateSaveThatThrewMidway_…` | `Expected world.JobStatuses.Values to be 3 because a save that threw marks its job Failed, but found 1.` (left Running) |
| F1 `VersionSaves_WhoseHeaderAndBodyKeysDisagree_…` | `Expected response.Headers.GetValues("X-Idempotency-Status") to be equal to {"new"} … but {"cached"} differs at index 0.` |
| F3 `CompletedCreateSave_…` | `Expected job.Result not to be <null> because the pane can only reach its success state when the completed job names the document.` |
| F3 `CompletedVersionSave_…`, `CompletedEmailSave_…`, `AttachmentDeduplicatedToAnExistingCanonical_…` | `System.NullReferenceException` at `job.Result!` (Result was null). The preconditions had passed, including the dedup branch. An explicit `job.Result.Should().NotBeNull(…)` was added afterwards so the message says so. |

**After the fix: 17/17 pass.**

**The fixture had hidden F2.** `OfficeVersionSaveWorld.FindJobByIdempotencyKey` returned Status = Completed for every key, so it
could never show a failed job being replayed. The world now records each row's real `sprk_status` (from
`CreateProcessingJobAsync` + every `UpdateProcessingJobAsync`) and returns the newest row per key. It also gained
`FailNextDocumentCreate` (a save that throws after its job and upload exist). All pre-existing Office tests stay green on
the changed world.

## 2. Findings, as reproduced

### F4: two layers, not one
1. **Response cache (`IdempotencyFilter`).** The Word pane's create header key (`buildIdempotencyCanonical`) names the
   record and document URL, not the content. A second create of edited content re-sends the same header, so the filter
   replays the first 202 (`X-Idempotency-Status: cached`) and the handler never runs.
2. **Persistent job lookup (`OfficeService.GenerateIdempotencyKey`).** With no client key, the create canonical
   `Document|type|id|||FileName|` carried no content. The second save matched the first job and returned `Duplicate`.

### F2: two variants
- As reported: `OfficeDocumentPersistence.CheckForExistingJobAsync` returned any row with the key, including Failed.
- **Not reported, found while reproducing:** a save that THROWS after its job row exists (for example
  `CreateDocumentAsync` fails) fell into `SaveAsync`'s outer catch, which never touched the job. It stayed Running
  ("FileUploaded"), and every retry was a "duplicate" of it. Ignoring Failed jobs alone would not have fixed this path.

### F3
Both save paths, and the immutable dedup branch, set Completed without `Result`. The pane
(`useSaveFlow.ts` `pollJobStatus`) completes only on `result.artifact.id`. This is equally a defect for Outlook
Email/Attachment saves: same hook, same stall.

### F1
Confirmed by reading: `OfficeEndpoints.SaveAsync` assigned the header to a local and never read it. Behaviourally the
server was already "body key, else server key". The header's only effect was the filter's replay cache. That cache was
content-blind for Documents, which is F4's first layer. So F1's behavioural test is "a reused header must not override
a different body key".

### Pre-existing defect found (NOT fixed; outside scope): the filter's no-header body-hash path is inert
Minimal-API endpoint filters run **after** parameter binding. By the time `IdempotencyFilter` runs, the JSON body has
been consumed, so `GenerateIdempotencyKeyAsync` re-buffers an empty stream and skips idempotency. Evidence: a throwaway
probe (deleted, not committed) posted the same Document body twice with no header. Both responses had **no**
`X-Idempotency-Status` header, and the second was `200 {"duplicate":true}` from the persistent layer. This affects all
four routes using `AddIdempotencyFilter()` (save, quickcreate, todo, share/links): without a header there is no response
cache and no concurrency lock. My first F4 design (binding to the raw body) failed for this same reason, which is how it
was found. Recommend a follow-up issue (`/defer`); not filed from this sub-agent.

## 3. Design chosen

**One authoritative key (F1).** `OfficeService.ResolveIdempotencyKey(request)` = body `idempotencyKey` ?? the server's
`GenerateIdempotencyKey`. This one definition decides whether a save runs. The `X-Idempotency-Key` header is NOT that key.
It only names the 24-hour response-replay cache. The handler's dead read is removed, and the contract is documented on
`OfficeEndpoints.SaveAsync` remarks, the route comment and `ResolveIdempotencyKey`.

**Create-key content hashing (F4).** For `ContentType == Document`, `GenerateIdempotencyKey` appends
`|create-content:{HashContent(ContentBase64)}` for a create. It uses the same `HashContent` task 023 uses. The version
branch still appends `|version-content:{hash}`, byte-for-byte unchanged (pinned by `VersionSaveKey_IsUnchangedByTask039`).
Identical bytes produce the identical key, so a true retry still dedups (pinned by the two byte-identical guards).

**Header layer (F4).** A new opt-in overload `AddIdempotencyFilter(Func<EndpointFilterInvocationContext, string?> bindClientKeyTo)`.
For Document saves the save route binds the header's cache entry to the authoritative key
(`OfficeEndpoints.DocumentSaveIdempotencyBinding`, which reads the BOUND `SaveRequest`). The response cache can therefore
replay only a request the authoritative key would also dedupe. A header can split two saves, never merge them. If the
binding throws, the cache is skipped rather than replaying. The default overload is unchanged for the other three routes.

**Failed jobs (F2).** `CheckForExistingJobAsync` returns null for a Failed/Cancelled row. The shared query
`DataverseServiceClientImpl.GetProcessingJobByIdempotencyKeyAsync` now orders by `createdon desc`, so the newest row
decides and an old failed row can't shadow a later completed one. `SaveAsync` marks its job Failed (Dataverse + in-memory)
in the outer catch (with `CancellationToken.None`) and in the in-memory store on the OFFICE_012 branch.

**Job result (F3).** Every Completed job carries `Result.Artifact` (`ArtifactType.Document`, the `sprk_document` id,
`SpeFileId`, drive, `WebUrl`): the created row on create, the EXISTING row on version, the canonical on immutable dedup.

## 4. Email / Attachment: explicit list of behaviour changes

| Change | Why it is a defect fix for them too |
|---|---|
| **Key: unchanged.** Canonical strings are byte-for-byte as before (pinned). | Not changed |
| **Header replay: unchanged.** No binding for Email/Attachment (pinned). | Not changed: their header names the same immutable message/attachment the server key names, so a header hit is a request the server would also dedupe. |
| **F2:** a same-key retry after a FAILED Email/Attachment save now re-runs instead of returning the failed job. So does a retry after a save that threw once its job existed. | F2 as reported covers "every server-keyed retry after OFFICE_012 / OFFICE_019 / OFFICE_014", Email included. |
| **F3:** a completed Email/Attachment job now carries `result.artifact` (the created `.eml`/attachment `sprk_document`; on the immutable-dedup branch, the canonical). | The Outlook pane uses the same `pollJobStatus`, so it stalled identically. |

## 5. Interactions a reviewer must know

- 🔴 **D1 (task 025) becomes more reachable, by design.** An edited same-name re-create to the same record used to be
  swallowed silently. It now executes. The path-keyed upload writes the edits as a NEW VERSION of the first save's drive
  item. `CreateDocumentWithSpePointersAsync` then creates a second row whose pointer update collides on
  `sprk_graphitemid_uk`. In production that surfaces as an error (`OFFICE_INTERNAL`); the job is now marked Failed. So
  silent loss becomes a visible error, while the bytes land on the item the first row points to. The in-memory world does
  not model the uk, so the F4 tests assert writes (uploads, SPE versions, jobs) and make **no** row-count claim. Task 025
  must land before production use of the create re-save path. `sprk_graphitemid_uk` is not relaxed (NFR-07).
- **Failure after the document exists** (for example a Service Bus finalization send failing) now marks the job Failed. A
  same-key retry re-runs: an extra identical SPE version on a version save; D1 on a same-name create. Before, the retry
  returned `Duplicate` and finalization was never queued. `Failed` is the truthful state; the trade is recorded here.
- **Task 028 unaffected.** Documents never reach suppress (`IsEditableContent` untouched).
  `OfficeSaveAsNewDocumentLinkGraduateTests` 3/3 green.
- **Task 023 unaffected.** Version key unchanged; `OfficeVersionSaveOneRowTests` 4/4 green; wire tests green.

## 6. Pane (task 024) workarounds: now removable, NOT removed (client untouched)

Removable once this BFF is deployed (a pane talking to an older BFF still needs them):
1. **Body `idempotencyKey` on version saves.** The server's version key is already content-aware, and a same-key retry after a failure now re-runs.
2. **`failedAttempts` in the version key** (`versionFailuresRef`). Failed jobs no longer dedupe.
3. **The completion fallback `activeVersionDocumentIdRef`** in `pollJobStatus`. The job now names the existing document.

Stale comments to correct in the same follow-up: `useSaveFlow.ts` (the idempotency block and
`VersionIdempotencyParts` doc), and `notes/024-save-mode-and-deviations.md` §4 and §6 F1–F4.

## 7. Placement justification (root CLAUDE.md §10) and component justification (§11)

Modify-only on the existing save spine, inside `POST /api/office/save`. No new route, service, DI registration or
package. The same answers as `023-save-path-trace.md` §8 apply: a synchronous user wait on BFF-managed state. The only new
surface is one static method (`OfficeService.ResolveIdempotencyKey`) and one filter overload.
1. **Existing:** `IdempotencyFilter` + `AddIdempotencyFilter()`; `GenerateIdempotencyKey`.
2. **Extension:** yes. The overload extends the existing filter; `ResolveIdempotencyKey` names the existing
   `body ?? generated` expression so the filter and the service share it.
3. **Cost of doing nothing:** a reused content-free header silently replays a stale 202 for an edited document (F4).

ADR-001/007/008/010/013/019/044 are unaffected.

## 8. Gates

| Gate | Result |
|---|---|
| `dotnet build` (BFF via the test project; TreatWarningsAsErrors) | green, 0 warnings |
| New task-039 tests | 17/17 (were 11 failed / 6 passed before) |
| Office + ContentDedup + Idempotency + DuplicateDetection sweep | 368 passed, 0 failed, 10 skipped |
| ArchTests | 191/191 |
| Full `Sprk.Bff.Api.Tests` | _see §10_ |
| CVE (`dotnet list … --vulnerable --include-transitive`) | "no vulnerable packages" |
| `dotnet format whitespace --verify-no-changes` | production files clean. The new data-mutation file was normalised. Pre-existing whitespace in `OfficeVersionSaveWorld` (not in this diff) was left alone. |
| Publish (PowerShell `Compress-Archive` Optimal, PDBs incl., Release) | fresh `origin/master` `e0a6f87c4` = **47,555,941 B**; project head `b5c08f26f` = **47,601,723 B**; this branch = **47,604,265 B**. **Task delta +2,542 B**; branch vs master +48,324 B. |
| `/conflict-check` | Soft warn. UAC-r2 PR #950 touches `OfficeEndpoints.cs` (the `ValidateSaveRequest` entity-type list), `OfficeService.cs` (stub generators) and `DataverseServiceClientImpl.cs` (`UpdateDocumentAsync`/associate). No hunk overlaps this diff. PR #960 is this project's own branch. |

## 9. Step 9.5 (code-review + adr-check) findings and disposition

No Critical findings. ADR-001/007/008/010/013/028/038/044 compliant. NFR-07 and NFR-08 honoured.

| Sev | Finding | Disposition |
|---|---|---|
| Warning | The `createdon desc` ordering in the shared query has no automated test (no seam in `DataverseServiceClientImpl`) | Accepted. Reasoned; the world models newest-wins. **Unverified live.** |
| Warning | `GetJobStatusAsync`'s Dataverse fallback (job not in THIS instance's memory: scale-out or restart) still returns no `Result`. F3 persists there | Pre-existing limitation, not fixed: it needs a document column/result on `sprk_processingjob`. Recorded as residual. |
| Warning | Pre-existing: the filter's no-header body-hash path is inert (§2) | Out of scope; recommend a follow-up issue. |
| Warning | Pre-existing: `SaveAsync`'s outer catch returns `Details = ex.ToString()` (stack trace to client) and maps `OFFICE_INTERNAL` to 400 | Pre-existing; not changed. The job's `sprk_errormessage` now also gets `ex.Message` (same text the response already carries). |
| Warning | Pre-existing: the persistent lookup is not user-scoped (024 F7) | Unchanged. |
| Warning | `SaveAsync` (already a very large multi-responsibility method) grew by ~30 lines of job-state bookkeeping | Accepted. Same responsibility as the existing job updates. A `MarkJobFailed` helper would dedupe three failure blocks; not extracted, to keep the diff small on a spine with concurrent editors (PR #950). |
| Suggestion | Document saves hash `ContentBase64` twice per request (filter binding + service) | Accepted: one SHA-256 pass per copy. Could be memoised in `HttpContext.Items`. |
| Suggestion | `sprk_errormessage` length is not bounded when marking Failed; an oversized update is swallowed and leaves the job Running | Unverified (column max not in-repo); same exposure as the existing OFFICE_012 path. |
| Suggestion | `ArgumentNullException` guards on non-nullable delegate params (new ctor + overload) | Kept. The overload guard fails at route registration (startup), not per request, and matches the file's existing ctors. |
| Suggestion | The static `_jobStore` is never evicted (pre-existing); entries now carry a small `Result` | Pre-existing. |

## 10. Test counts

- New: 17 (contract 11 incl. theory ×2; data-mutation 6).
- Office/dedup/idempotency sweep: 368 passed / 0 failed / 10 skipped.
- ArchTests: 191 / 191.
- Full `Sprk.Bff.Api.Tests` at `dec051738`, **completed**: **12,289 total, 12,233 passed, 0 failed, 56 skipped**.
  It ran as four foreground chunks under the 10-minute per-call cap. The chunk filters partition the suite exactly
  (complement filter for the last one):
  `Services.` 6,885 / 0 / 24 (58 s) · `Seam.` 1,685 / 0 / 0 (7 m 31 s) · `Api.` 1,449 / 0 / 23 (9 m 49 s) ·
  everything else 2,214 / 0 / 9 (3 m 20 s). The 56 skips match task 016's recorded 56.

## 11. Deviations from the POML

| # | Deviation | Why |
|---|---|---|
| 1 | Edited `IdempotencyFilter.cs`, `OfficeDocumentPersistence.cs` and shared `Spaarke.Dataverse/DataverseServiceClientImpl.cs` (not in `<relevant-files>`) | F4's first layer is the filter. F2's decision lives in `CheckForExistingJobAsync` and its correctness needs the query ordering. The filter change is an additive opt-in overload; the other three routes are unchanged. |
| 2 | New test file `tests/integration/data-mutation/OfficeVersionSave/OfficeSaveSpineIdempotencyTests.cs` | AC1 asks for a data-mutation test that counts writes. |
| 3 | F2 extended to jobs stranded by an exception (outer catch) | Needed for AC2 ("given a failed save, a retry … starts a new attempt"). The "ignore Failed" fix alone leaves that path broken. |
| 4 | F3 applied to Email/Attachment and the immutable-dedup branch | Equally a defect for them (same pane hook); listed in §4. |
| 5 | The header binding uses the bound arguments, not the raw body | The raw body is already consumed when endpoint filters run (§2). |

## 12. Unverified

- Live Dataverse: the `createdon` ordering, `sprk_errormessage` length, and that Failed jobs are written as `sprk_status = 3` on the real table.
- The D1 outcome in production for an edited same-name re-create (the world does not model `sprk_graphitemid_uk`).
- Whether the BFF runs scaled out (the F3 residual on the Dataverse fallback).
- No live Word/Outlook pane run. The pane's success state on a create save is inferred from `pollJobStatus`'s code, not observed.
