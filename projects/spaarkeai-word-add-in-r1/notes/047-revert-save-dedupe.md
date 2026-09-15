# Task 047: a version save that repeats an earlier version's content is written, not answered "Duplicate"

> Base: `6b37a79d2` (project branch, rebased). Rigor FULL, opus @ high, directional.
> Reproduce-first (root CLAUDE.md §10 §F.3): the tests below were written and run against the unchanged production
> code before any fix. Symbols are named by symbol, not by line.

## 0. Summary

| Question | Answer |
|---|---|
| Reproduced? | **Yes, at both layers.** 4 of 9 new tests failed on the unchanged code; the 5 that passed are exactly the guards (retry, Cancelled, Email, Attachment). |
| Which layer lost the third save? | The pane within 24 h: the **response cache** (the header is the content-only version key). The pane after 24 h, and every client that sends no key: the **persistent job lookup** (no time window). |
| Option chosen | **(a), server-side and key-agnostic.** A Completed job answers a version save as its duplicate only while the document still holds exactly that save's bytes. The response cache never replays a version save; it keeps only the in-flight lock. |
| Pane changed? | **No.** `useSaveFlow.ts` is untouched, and task 036's files are untouched. |
| Schema change? | **None.** Escalation trigger 1 did not fire: nothing new is recorded on the job. |
| Email / Attachment / create | Unchanged: keys, replay and duplicate behaviour are pinned by the existing 039 tests and two new ones. |

## 1. Trace: the two de-duplication layers for a version save

**Layer 1: response cache (`IdempotencyFilter`).**
- **Runs:** before the handler and before the auth filters (registration order in `OfficeEndpoints.MapPost("/save")`).
- **Key:** `ITenantCache` resource `idempotency-request`, id `{userId}:{X-Idempotency-Key}:{sha256(binding)}`, tenant-scoped. For Document saves the binding is `OfficeEndpoints.DocumentSaveIdempotencyBinding` = the authoritative key (`OfficeService.ResolveIdempotencyKey`, task 039).
- **TTL:** `IdempotencyFilter.DefaultTtl` = **24 hours** for a cached 2xx; a 2-minute in-flight lock (`LockDuration`).
- **Where the third save dies:** the pane's version key is `sha256(existingDocumentId, fileName, contentSha256, failedAttempts)` (`useSaveFlow.ts` `computeIdempotencyKey` / `buildIdempotencyCanonical` / `VersionIdempotencyParts`). It goes in the header AND the body, so B's header and binding are identical on the first and third save. The third B within 24 h was answered with the first B save's **cached 202**, and the handler never ran.
- Nothing downstream can correct it: a cached 202 names the first job, which is Completed with a `result.artifact`, so the pane reports success.

**Layer 2: persistent job lookup (`OfficeService.SaveAsync` step 2).**
- `ResolveIdempotencyKey` = body key ?? `GenerateIdempotencyKey`, which appends `|version-content:{hash}` for a version save.
- `OfficeDocumentPersistence.CheckForExistingJobAsync` → `GetProcessingJobByIdempotencyKeyAsync`: the **newest** row by `createdon`, **no time window**.
- Queued, Running or Completed ⇒ `Duplicate` (200). Failed or Cancelled ⇒ a new attempt (task 039).
- The third B carries B's key, so it found the first B save's Completed job and wrote nothing.

**Which layer applies.**

| Client | Within 24 h of the first B | After 24 h |
|---|---|---|
| Pane (header = body key) | Layer 1 | Layer 2 |
| No client key | Layer 2 (the filter's no-header path is inert: 039 §2) | Layer 2 |

**Root cause:** a content key names a document and its content, not a moment. Neither layer could tell a retry of a save from a later save that happens to carry the same bytes.

## 2. Reproduction evidence (before the fix, unchanged production code)

`OfficeVersionSaveRevertTests`, 9 tests: **4 failed, 5 passed.**

| Test | Before (verbatim) |
|---|---|
| `VersionSaves_B_A_B_UnderThePanesKeys_WriteThreeVersions` (layer 1) | `Expected world.SpeItems[itemId].Versions to contain 4 item(s) because the seed plus three saves: the third save of B follows a different written version, so it is written, but found 3: {…0x30}, {…0x42, 0x42}, {…0x41, 0x41, 0x41}.` SPE ended at **A**. |
| `VersionSaves_B_A_B_WithTheContentKeyInTheBody_AndAFreshHeaderEachTime_WriteThreeVersions` (layer 2, body key) | `Expected third.StatusCode to be HttpStatusCode.Accepted {value: 202} because the first B save's Completed job no longer describes the document, which now holds A, but found HttpStatusCode.OK {value: 200}.` |
| `VersionSaves_B_A_B_WithNoClientKey_WriteThreeVersions` (layer 2, server key) | `Expected third.StatusCode to be HttpStatusCode.Accepted {value: 202}, but found HttpStatusCode.OK {value: 200}.` |
| `ASecondIdenticalVersionSave_WhenTheDocumentsCurrentContentCannotBeRead_IsWritten_NotAnsweredFromTheOldJob` | `Expected world.SpeItems[itemId].Versions to contain 3 item(s), but found 2`. The status assertion (202) had passed, so this was a 202 that wrote nothing: a layer-1 replay. |
| Guards (passed before): the retry under the pane's keys, the retry with no key, the Cancelled retry, the Email resend, the Attachment replay | pass |

**After the fix: 9/9.**

## 3. Options evaluated

| Option | Covers layer 1 | Covers layer 2 | Covers clients already deployed / no-key clients | Retry stays at one write | Verdict |
|---|---|---|---|---|---|
| **(a) server: a Completed job is the duplicate only while the document still holds its content** | Only together with a cache change (below) | Yes | **Yes** (key-agnostic) | Yes (the document holds exactly the retry's bytes) | **Chosen** |
| (b) pane adds its base version / eTag to the key | Yes | Yes (body key) | **No.** The server-key path and the deployed pane stay broken until the add-in redeploys. The pane does not know an SPE eTag, and a pane-session "last job" base collides across sessions: reopen the document, save B with base "none" = the first B key again. | Yes | Rejected: needs a BFF change to expose the eTag anyway, and still leaves the server-key path |
| (c) pane adds a per-Save-click id | Yes | Yes (body key) | **No**, same gap as (b) | Only for replays of one click. Two clicks of unchanged content write two identical versions. | Rejected: same gap, and weaker for double clicks |

**Sub-choices within (a): how to know "the document still holds this save's content".**

| Approach | Why not / why |
|---|---|
| The job records the version / eTag it wrote | `sprk_processingjob` has no column for it. Writing it into `sprk_payload`, `sprk_correlationid` or `sprk_currentstage` would misuse a column, and a new column is a schema change (escalation trigger 1). Rejected. |
| Compare SPE's `quickXorHash` with the request | Needs a local QuickXorHash of the request bytes. The world's hash is a SHA-256 stand-in, so a wrong local implementation would pass every test and fail silently live. Rejected. |
| Compare the item's `lastModifiedDateTime` with the job's `sprk_completeddate` | Two clocks (Graph against the BFF). Metadata-only changes and co-authoring autosave move the timestamp. Rejected. |
| **Download the item's current content and compare bytes** (`SpeFileStore.DownloadFileAsync`, the facade) | Exact. Nothing is recorded, so no schema change. The cost falls only on the rare branch where a Completed version job already carries this key (a retry, or B→A→B). **Chosen.** |

**Why the response cache had to change too.** Layer 1 answers before the handler, so no handler-side check can see the third B. Binding the cache to document state would mean Graph I/O inside a filter that runs *before* authorization. Instead, `IdempotencyFilter` gained an opt-in replay gate (`mayReplayResponse`). The save route marks a VERSION save non-replayable (`OfficeEndpoints.SaveResponseMayBeReplayed`). Such a request keeps the in-flight lock (a concurrent double submit still gets 409, as before), but it is neither answered from nor written to the response cache. Binding a per-request nonce was rejected: it would have removed the lock.

## 4. Design as built

- **`OfficeService.SaveAsync` step 2.** After `CheckForExistingJobAsync`, the new `IsStillTheSameOperationAsync` decides:
  - For a non-version save, or a job that is not Completed (Queued/Running: its write may not have landed), it returns **true**. The key alone decides, as before.
  - For a Completed version job, it decodes the request bytes, resolves the target (`OfficeDocumentPersistence.ResolveVersionTargetAsync`, existing) and asks `OfficeStorageUploader.ItemHoldsContentAsync`.
  - Duplicate **only on a proven `true`**. On `false` (a later save wrote other content) or an unknown (unreadable, no pointers, bad content, any exception), the save proceeds as a new operation, and its own validation then refuses (OFFICE_016/017/018) or writes.
- **`OfficeStorageUploader.ItemHoldsContentAsync`.** An app-only download through the facade (ADR-007). It compares byte for byte, streaming, stops at the first difference, and never reads past `expected.Length + 1`. The content is never returned. `null` means unreadable.
- **`IdempotencyFilter`.** `AddIdempotencyFilter(bindClientKeyTo, mayReplayResponse = null)` and the matching ctor parameter. A `false` or throwing predicate means: no cache read, no cache write, lock kept, `X-Idempotency-Status: new`. The other three routes that use `AddIdempotencyFilter()` are untouched.
- **Failure direction, pinned by a test:** an unreadable document writes one more identical version, never a lost one.

## 5. Behaviour a reviewer must know

| # | Change | Why acceptable |
|---|---|---|
| 1 | A same-key re-send of a COMPLETED version save within 24 h now gets **`200 duplicate:true` (the first save's job)** instead of a **cached `202`**. The pane then shows its "duplicate" state ("This item was previously saved.") instead of re-tracking the completed job. | It is the response the pane already got after the 24 h TTL, and the one every no-key client always got. It is truthful: nothing new was saved. AC3 holds: the second request returns the first save's job. **Not observed live.** |
| 2 | An unreadable document (a Graph failure on the download) turns a completed save's retry into one extra identical SPE version, plus a new job. Finalization is re-queued, so task 029's per-save refresh would re-run for identical bytes. | The chosen direction: never lose a save. Rare: it needs a retry AND a failed read. |
| 3 | The duplicate-candidate branch (a Completed version job under this key) costs one Dataverse read and one SPE download. On the proceed path the target is resolved again (a second read). | Only on retries and reverts. Not on the normal save path, which is unchanged. |
| 4 | Garbage or empty `contentBase64` under a completed job's key now reaches the normal path's error instead of `Duplicate`. | Edge case; the request is invalid. |

**Document creates: not changed. The create variant is reachable only through D1 (task 025).** Create B → create A (same name, same record) → create B would answer the third create "Duplicate". But the second create is a path-keyed Replace upload onto the FIRST save's drive item. In production it then fails on `sprk_graphitemid_uk` (D1), after the upload has already overwritten the first document's file with A. So the create variant is a *consequence* of D1, whose own damage (the first document's file silently replaced by an errored save) is the bigger defect. Task 025's refuse-before-upload design removes both: the item cannot change under a completed create's key. **Recommendation:** task 025 carries a create B→A→B test. Not fixed here: the world does not model the uk, so it would "prove" a production state that D1 makes different.

## 6. Tests added

| File | Tests | KEEP path |
|---|---|---|
| `tests/integration/data-mutation/OfficeVersionSave/OfficeVersionSaveRevertTests.cs` (new) | 9: B→A→B under the pane's keys / a body key with fresh headers / no key (**3 failed before**); a retry under the pane's keys / with no key (writes once, returns the first job); an unreadable document writes again (**failed before**, the direction pin); Cancelled retry runs again; Email resend still Duplicate with no file read; Attachment still replayed from cache | data-mutation ✓ |
| `tests/unit/Sprk.Bff.Api.Tests/Filters/IdempotencyFilterReplayGateTests.cs` (new) | 5: non-replayable skips a cached response; is never cached; still takes the lock (409 to a concurrent duplicate); a throwing predicate means no replay; replayable is still replayed | **not a KEEP path.** It sits beside its sibling `IdempotencyFilterTests.cs`. For `/test-diet`: AMBIGUOUS → doubt = KEEP. It is the only test of the lock-only concurrency guarantee. |
| `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs` (fixture) | The world gains `Download` / `DownloadCalls` / `FailDownloads`, and the factory binds `SpeFileStore.DownloadFileAsync`. Additive only; no test changed or weakened. | contract ✓ |

No `Mock<HttpMessageHandler>`, DI-registration or ctor null-check tests (ADR-038 §4).

## 7. Placement justification (root CLAUDE.md §10) and component justification (§11)

**Placement: in the BFF, on the existing save spine.** The decision needs BFF-managed state: the ProcessingJob row and the document's SPE item behind `SpeFileStore`. It happens inside `POST /api/office/save` during a synchronous user wait. No new route, service, DI registration, package or background work.

**§11: new surface** = one public method (`OfficeStorageUploader.ItemHoldsContentAsync`), one private helper (`OfficeService.IsStillTheSameOperationAsync`), one optional parameter on the existing filter overload/ctor, and one route predicate (`OfficeEndpoints.SaveResponseMayBeReplayed`).
1. **Existing:** `IdempotencyFilter` + `AddIdempotencyFilter(bindClientKeyTo)` (task 039); `CheckForExistingJobAsync`; `SpeFileStore.DownloadFileAsync`; `OfficeDocumentPersistence.ResolveVersionTargetAsync`. All are reused. `git grep` finds no existing "does this item hold these bytes" helper: `ContentDedupDetector` compares `quickXorHash` between items, never against request bytes.
2. **Extension:** yes. The replay gate is an optional parameter on the existing overload; the content check sits on the class that already owns the Office SPE I/O.
3. **Cost of doing nothing:** B, then A, then B again is answered with the first B save's job; SPE keeps A while the pane says "saved" (reproduced in §2).

ADR-001 / 007 / 008 / 009 / 010 / 013 / 019 / 044 are unaffected (§10).

## 8. Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **0 errors, 0 warnings** |
| Test project build | 0 errors, 0 warnings |
| New tests | revert 9/9 (**4 failed / 5 passed before**); replay gate 5/5 |
| Office + Idempotency + ContentDedup + DuplicateDetection sweep | **404 passed / 0 failed / 10 skipped** |
| ArchTests | **191 / 191** |
| Full `Sprk.Bff.Api.Tests` (after the fix; six foreground chunks, an exact partition) | **12,343 total: 12,287 passed / 0 failed / 56 skipped**. `Services.` 6,899/0/24 · `Seam.` 1,744/0/0 · `Tests.Api.Ai.` 599/0/14 · `Tests.Api.Office` 147/0/9 · rest of `Tests.Api.` 721/0/0 · complement 2,177/0/9. Baseline (main session) ≈ 12,249 / 0 / 56; the skips match. |
| `dotnet format whitespace --verify-no-changes` (changed files) | clean. The two new test files were normalised to CRLF. |
| CVE (`dotnet list … --vulnerable --include-transitive`) | "has no vulnerable packages" |
| `/conflict-check` | Silent pass: no open PR (40 listed) touches `IdempotencyFilter.cs`, `OfficeEndpoints.cs`, `OfficeService.cs`, `OfficeStorageUploader.cs`, `OfficeDocumentPersistence.cs` or `OfficeEndpointsContractTests.cs` |
| Pane gates (tsc / build / jest) | N/A: the pane was not changed |

**Publish size** (Release, PDBs included, PowerShell `Compress-Archive -CompressionLevel Optimal` over the publish folder, short paths):

| Build | Commit | Bytes |
|---|---|---|
| Fresh `origin/master` | `e0a6f87c4` | **47,555,940** |
| Project head (this task's base) | `6b37a79d2` | **47,609,275** |
| This branch | task 047 | **47,612,160** |
| **Task delta** (branch − head) | | **+2,885 B** |
| Branch − master | | +56,220 B (mostly earlier project tasks) |

## 9. Step 9.5: code-review + adr-check

**Metrics (before → after).**

| File | Lines | Signal |
|---|---|---|
| `IdempotencyFilter.cs` | 581 → 619 | +6.5% |
| `OfficeEndpoints.cs` | 2,186 → 2,213 | |
| `OfficeService.cs` | 2,986 → 3,058 | +2.4% |
| `OfficeStorageUploader.cs` | 204 → 263 | +29%: one cohesive SPE-read method |

Branch-bearing lines: +25 / −1 across the production diff.

**No Critical findings. No ADR violations.**

| Sev | Finding | Confidence | Disposition |
|---|---|---|---|
| Warning | The comparison read is **app-only** (`DriveItemOperations.DownloadFileAsync` → `ForApp`), while the version write is OBO. It bypasses SPE's own ACL for the read. | High (fact); low (risk) | Accepted. It runs after the route's `write` gate on the document (ADR-008). It returns only a boolean, never content. It is the same identity as the version path's existing app-only `quickXorHash` read. The OBO twin (`DownloadFileAsUserAsync`) is non-virtual (no module-boundary double) and would add a token exchange. ADR-028 compliant. |
| Warning | §5 #1: a retry within 24 h gets a 200 duplicate instead of a cached 202; the pane shows "duplicate" | High | Accepted; documented; unverified live. |
| Warning | §5 #2: an unreadable document ⇒ one extra identical version, and finalization re-runs | High | Accepted by design (never lose a save). |
| Warning | Pre-existing, not changed: `IdempotencyFilter`'s outer `catch` re-invokes `next(context)` when the handler itself throws inside the guarded block, so the endpoint can run twice | Medium | Out of scope. The save handler catches its own exceptions, so impact is limited. Recommend a follow-up issue. |
| Warning | Pre-existing: a job stranded Running by a process crash still makes every same-key retry a duplicate (039 residual) | Medium | Unchanged. Only Completed jobs are state-checked, deliberately: an in-flight job's write may not have landed. |
| Warning | `OfficeService.cs` (3,058 lines) keeps growing; `SaveAsync` is multi-responsibility | High | Accepted (§11.5). The new logic is a separate private method with one reason to change (the duplicate decision). |
| Suggestion | The duplicate-candidate branch resolves the version target twice (the check, then the normal path) | High | Accepted: rare branch. Could be passed through. |
| Suggestion | `IdempotencyFilterReplayGateTests` lives outside a KEEP path | High | See §6; flag for `/test-diet`. |

AI-smell scan: no single-implementation interface; no log-and-rethrow (the helper's `catch` recovers: it returns "not provably the same"). The `!` on `request.Document` and `ExistingDocumentId` is guaranteed by `IsVersionSave`. Comments explain *why*.

**adr-check.**

| ADR | Result |
|---|---|
| ADR-001 | No new endpoint ✓ |
| ADR-007 | SPE only through `SpeFileStore`; no Graph type outside Infrastructure ✓ |
| ADR-008 | The idempotency filter is cross-cutting; resource authz unchanged and still before the handler ✓ |
| ADR-009 | `ITenantCache` only; tenant-scoped; nothing new cached; no `IMemoryCache` ✓ |
| ADR-010 | No registration or interface ✓ |
| ADR-013 | No AI dependency ✓ |
| ADR-019 | Unchanged ✓ |
| ADR-028 | App-only read noted above ✓ |
| ADR-029 | Measured ✓ |
| ADR-038 | KEEP paths as in §6; no banned patterns ✓ |
| bff-extensions §A / §F | Placement stated; no package; `Services/` changes carry tests ✓ |

## 10. Deviations from the POML

| # | Deviation | Why |
|---|---|---|
| 1 | Edited `IdempotencyFilter.cs`, `OfficeEndpoints.cs` and `OfficeStorageUploader.cs` (not in `<relevant-files>`). `OfficeDocumentPersistence.cs` (listed "modify") was **not** changed; its existing `ResolveVersionTargetAsync` is reused. | Layer 1 lives in the filter and its route registration. SPE I/O for the Office save belongs to the uploader (ADR-007 facade use). |
| 2 | Additive fixture change in the shared `OfficeVersionSaveWorld` / factory | The check reads SPE content; the world had no download seam. |
| 3 | A new unit-test file in a non-KEEP path | Only way to pin the lock-only concurrency guarantee deterministically (§6). |
| 4 | The full suite ran once, after the fix. "Before" is the targeted run (§2) plus the main session's baseline. | "Full suite once", per the brief. |
| 5 | Chunk partition differs from 039's (its `Api.` chunk split in three) | 039's `Api.` chunk took 9 m 49 s against the 10-minute per-call cap. |

## 11. Unverified

- Live SPE: the app-only download's latency and identity under production credentials.
- **Whether SPE stores a `.docx` byte-for-byte as PUT.** If SPE rewrote bytes (for example property demotion), a completed save's retry would never match and would write one extra identical version. B→A→B is unaffected (it is written either way).
- Live Redis: the lock-only mode uses the same `ITenantCache` calls as before, but no live run.
- No live Word pane run: the pane's behaviour on a 200 duplicate for a version save (§5 #1) is inferred from `useSaveFlow.ts` (`isDuplicateSaveResponse` → `flowState: 'duplicate'`).
