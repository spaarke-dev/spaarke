# Track B — Erasure surface + tenant isolation across every store path (task 063, FR-B06)

> **Task**: `063-erasure-tenant-isolation.poml`
> **Date**: 2026-08-26
> **Rigor**: FULL · opus @ xhigh
> **Depends on**: 060 (durable byte store), 061 (hot-index-only cleanup), 062 (retention + list/delete primitives)
> **Store state**: `SessionFileStore:BlobEndpoint` is still **EMPTY**. Nothing in this task arms it. §9 states what closing the gate now requires.

---

## 1. The enumeration (AC #1) — every location a session's file bytes can exist

Derived by reading the upload path end to end (`ChatDocumentEndpoints.UploadDocumentAsync` steps 8–10a and
`PersistDocumentAsync`), not by recalling the design. Each row names the store, what of the file it holds, and
what removes it.

| # | Location | What it holds | Erased by (after this task) | When |
|---|---|---|---|---|
| 1 | **Azure Blob** `{tenantId}/session-files/{sessionId}/{fileId}` | the ORIGINAL bytes | `SessionFileEraser.EraseSessionFilesAsync` — prefix enumerate + delete + **verify** | synchronous, must succeed |
| 2 | **Redis** `doc-upload-binary` (4h) | the ORIGINAL bytes | `SessionFileEraser.EvictUploadCachesAsync` | synchronous, best-effort |
| 3 | **Redis** `doc-upload-text` (4h) | full extracted text | same | synchronous, best-effort |
| 4 | **Redis** `doc-upload-meta` (4h) | file name, token estimate | same | synchronous, best-effort |
| 5 | **Redis** `doc-upload-persist` (4h) | SPE-persist marker (file name + SPE file id) | same | synchronous, best-effort |
| 6 | **Redis** `session:{sessionId}` (24h sliding) | the whole `ChatSession`, incl. `ChatSessionFile.ExtractedText` | `_cache.RemoveAsync` — pre-existing | synchronous |
| 7 | **Cosmos** `sessions/{sessionId}` (90d TTL) | the manifest — `StoredUploadedFile` is identifiers only (`SearchDocumentIdsCsv`), **no** extracted text (verified: `StoredUploadedFile.cs` has no `ExtractedText`) | `SessionPersistenceService.DeleteSessionAsync` — pre-existing | synchronous |
| 8 | **Azure AI Search** `spaarke-session-files` chunks | extracted text, chunked | `SessionFilesCleanupJob.EvictSessionAsync` via the existing cleanup signal — pre-existing | **asynchronous**, seconds (§6) |
| 9 | **Dataverse** `sprk_aichatsummary` / `sprk_aichatmessage` | conversation transcript. Holds **no file bytes and no extracted text** (verified: `ChatDataverseRepository.ArchiveSessionAsync` only flips `sprk_isarchived`; nothing on that path writes `UploadedFiles`) | **deliberately NOT erased** — Tier-2 audit trail (§7) | n/a |
| 10 | **SPE** (only if the user explicitly persisted the upload) | the file, filed into the DMS | **deliberately NOT erased** — a user-filed document, not chat scratch (§7) | n/a |

Rows 1–5 were **not** reached by session deletion before this task. Row 1 was not reached by anything at all.

**Two candidate locations were checked and ruled out**, so their absence is a finding rather than an omission:
`UploadFinalizationWorker`'s temp-blob path (GH #231 — a stub, references no blob type, and is the Office
add-in → SPE direction) and the Compose shadow document (a *new* artifact the user creates by opening a file
in Compose — a separate document with its own lifecycle, not a copy of the session upload).

---

## 2. How erasure enumerates — and why prefix, not manifest

```
prefix = {tenantId}/session-files/{sessionId}/     ← SessionFileBlobStore.ListAsync(tenant, session)
```

`SessionFileEraser` materialises the whole prefix, deletes each row, then **re-enumerates the same prefix** and
refuses to report success if anything is still there.

Walking the Cosmos manifest instead would have been wrong in two independent ways, one slow and one fast:

- **Slow**: the `sessions` container carries `DefaultTimeToLive = 7776000` (90 days) and the blobs carry **no
  TTL at all**. The manifest expires while the bytes persist, so a manifest walk would name nothing — leaving
  those bytes behind permanently *and* invisible to every future erasure. (Task 060 notes, open item 6.)
- **Fast**: the durable write lands **before** the manifest write, which is deliberately non-fatal. An upload
  whose manifest update failed produces a blob no manifest ever mentions — on the same day, not at day 91.

Prefix enumeration reaches both. Pinned by `Erasure_EnumeratesByPrefix_SoItReachesABlobNoManifestNames`, which
plants an orphan blob under the session prefix with no manifest entry anywhere and asserts **both** copies go.

The manifest is still read — but only to source file ids for rows 2–5, never to decide what durable bytes
exist. Neither id source is a superset of the other: the blob prefix names files with a durable copy, the
manifest names files uploaded while the store was disabled (i.e. every deployment today). Both are used.

---

## 3. What a PARTIAL failure reports, and why that is the honest answer

Task 062 found that `LoadFromCosmosAsync` catches every exception and returns `null`, so a retention sweep
built on "null ⇒ delete" would read a Cosmos outage as "every session expired". **Erasure has the mirror
hazard**: a transient failure must not read as "every byte erased" — and unlike the retention case, nothing
downstream ever contradicts it, because the manifest and the History entry are the very things that would have
shown the gap.

So the outcome is a tri-state, not a bool:

| State | Meaning | Session record |
|---|---|---|
| `Erased` | the prefix was enumerated, emptied, and **verified empty** by a second enumeration | deleted |
| `StoreDisabled` | no blob endpoint configured — nothing was enumerated and nothing was deleted. **Not** a claim about bytes | deleted (§8) |
| `Incomplete` | enumeration, a delete, or the verification failed. Bytes MAY remain | **left completely intact** |

`Incomplete` is produced by: a delete that threw (`delete-failed`), a prefix that still has rows after the
deletes (`residual-after-delete`), an enumeration that threw (`enumeration-failed`), or a verification that
threw (`verification-failed`). One failing delete does **not** abandon the rest — the pass continues so it
erases as much as it can — but it fixes the verdict.

### The endpoint fails CLOSED

`DELETE /api/ai/chat/sessions/{id}` returns **500** with ADR-019 `errorCode = "session.durable-erasure-incomplete"`
and a `correlationId`, and the session is still there afterwards. Two reasons this is the right direction:

1. A session still listed in History is a **visible, retryable** state. A session that vanished while its
   documents stayed in blob storage is an invisible compliance failure that looks exactly like success.
2. Failing closed is cheap **because** erasure is prefix-driven: the retry finds and removes the residue
   whether or not the manifest still exists. Convergence is asserted in the same test that drives the failure.

The alternative — delete the record and log the erasure failure — was rejected: it converts a loud, user-fixable
condition into a log line nobody reads, and it orphans the bytes from every manifest-based tool at the same time.

### What is best-effort, stated rather than implied

Rows 2–5 (the 4-hour `doc-upload-*` copies) are best-effort and do **not** affect the verdict. They carry a
4-hour absolute TTL, so an unreachable Redis expires them on its own; failing the delete — and stranding a
durable erasure that already succeeded — would trade a bounded residue for an unbounded one. Failures are
logged at Warning, never swallowed.

---

## 4. Tenant isolation — the proof, and its non-vacuity control

The property: **an identifier is not a capability.** Knowing another tenant's `sessionId` must enumerate
nothing and delete nothing.

Three mechanisms, in order:

1. The prefix always begins `{callingTenant}/` (`SessionFileBlobStore.ListAsync` composes it and refuses an
   unsafe segment before any I/O).
2. Every listing row is re-checked by `AssertTenantPartitioned` against the calling tenant before it is yielded.
3. **Each delete is composed from the CALLER's tenant id, never from the listing row.** So even a listing that
   somehow widened cannot redirect a delete across the boundary — proven in §5, where breaking (1) alone still
   destroyed nothing.

Tests, all with a positive control in the same body so the negative cannot pass vacuously:

| Test | File |
|---|---|
| `ErasureRequestedByAnotherTenant_DestroysNothing` (+ control: the owning tenant CAN erase it) | `tests/integration/seam/Ai/SessionFileErasureSeamTests.cs` |
| `ErasingOneTenantsSession_LeavesAnotherTenantsIdenticallyIdentifiedFilesAlone` | same |
| `Erasure_RequestedByAnotherTenant_EnumeratesNothingAndDestroysNothing` (+ control) | `tests/integration/tenant/Ai/SessionFileBlobStoreTenantIsolationTests.cs` |
| `Erasure_DeletesOnlyWithinTheCallingTenantsPrefix_EvenWhenTwoTenantsShareEveryIdentifier` | same |

### Cross-tenant negative test per Track B store path (AC #4) — the whole-track ledger

| Path | Test | File | Task |
|---|---|---|---|
| **write** | `TwoTenantsUploadingToTheSameSessionId_ProduceSeparateDurableCopies` · `Write_PlacesTheBlobUnderTheCallingTenantsPrefix` | seam/`SessionDurableFileStoreSeamTests`, tenant suite | 060 |
| **read** | `Read_FromAnotherTenant_WithTheSameSessionAndFileIds_MustNotReturnTheBytes` · `Upload_DurableCopy_IsNotReadableByAnotherTenant` | tenant suite, seam | 060 |
| **re-index** | `Rehydration_UnderAnotherTenant_CannotReachTheOwningTenantsDurableCopy` | seam/`SessionFileLazyReindexSeamTests` | 061 |
| **retention** | `ExpiringOneTenantsSession_LeavesAnotherTenantsIdenticallyIdentifiedFilesAlone` | seam/`SessionFileRetentionSeamTests` | 062 |
| **availability** | `AnotherTenantsDurableCopy_DoesNotMakeAFileLookAvailable` | seam/`SessionFileAvailabilitySeamTests` | 062 |
| **list** | `List_ReturnsOnlyTheCallingTenantsBlobs` · `List_ScopedToASession_DoesNotReachAnotherTenantsIdenticallyNamedSession` | tenant suite | 062 |
| **delete** | `Delete_FromAnotherTenant_WithTheSameIds_DestroysNothing` | tenant suite | 062 |
| **erasure** | the four rows above | seam + tenant suite | **063** |

All eight paths are covered; 063 supplied the missing one.

---

## 5. Verification record — every claim observed failing first

A green suite is not evidence. Each assertion below was watched failing against a deliberately broken
implementation, then the pristine file was restored from a byte copy (no git operations were used at any point
in this task).

### 5a. Tenant segment dropped from the erasure prefix — stage 1 (enumeration only)

**Break**: `ListPrefixAsync` switched from `ListAsync(tenantId, sessionId)` to `ListAllForRetentionAsync()`
filtered by `sessionId` — the realistic mistake of reaching for the system-scope enumeration on a per-tenant
path (task 062 found this exact shape on the availability probe).

```
Erasure_RequestedByAnotherTenant_EnumeratesNothingAndDestroysNothing [FAIL]
  Expected crossTenant.State to be Erased ... but found Incomplete.
ErasureRequestedByAnotherTenant_DestroysNothing [FAIL]
  Expected crossTenant.State to be Erased ... but found Incomplete.
Failed! - Failed: 2, Passed: 43, Total: 45
```

**Worth reading carefully**: tenant A's bytes were **not destroyed** even with the enumeration broken, because
the delete is composed from the CALLER's tenant. The widened enumeration surfaced instead as a verification
failure — defence-in-depth working, and observable rather than silent.

### 5b. Both isolation guards removed — the true careless-refactor

**Break**: 5a **plus** the delete composed from `blob.TenantId` instead of the caller's.

```
Erasure_RequestedByAnotherTenant_EnumeratesNothingAndDestroysNothing [FAIL]
  Expected crossTenant.BlobsDeleted to be 0, but found 1 (difference of 1).
Erasure_DeletesOnlyWithinTheCallingTenantsPrefix_EvenWhenTwoTenantsShareEveryIdentifier [FAIL]
ErasureRequestedByAnotherTenant_DestroysNothing [FAIL]
ErasingOneTenantsSession_LeavesAnotherTenantsIdenticallyIdentifiedFilesAlone [FAIL]
  Expected erasure.BlobsDeleted to be 1 because an erasure is scoped to the CALLING tenant's prefix
  — an identically-identified session in another tenant is not part of it, but found 2.
Failed! - Failed: 4, Passed: 41, Total: 45
```

That is a real cross-tenant deletion — tenant B's erasure request destroyed tenant A's bytes — caught by four
independent assertions. The tests are non-vacuous.

### 5c. "No delete threw" treated as success — stage 1 (failure counter removed)

**Break**: `failures++` deleted from the per-blob catch, leaving only the log.

```
AFailedDelete_ReportsIncomplete_AndLeavesTheSessionRecordIntactSoTheRetryConverges [FAIL]
  Expected erasure.Failures to be 1, but found 0 (difference of -1).
Failed! - Failed: 1, Passed: 11, Total: 12
```

The **State** stayed `Incomplete` — the verification re-enumeration alone still refused to claim success. This
is exactly why the verification pass exists as a second mechanism rather than as a nicety.

### 5d. "No delete threw" treated as success — stage 2 (verification removed too)

**Break**: 5c **plus** the verification re-list replaced with `remaining = 0`.

```
AFailedDelete_ReportsIncomplete_AndLeavesTheSessionRecordIntactSoTheRetryConverges [FAIL]
  Expected erasure.State to be Incomplete because a transient failure mid-erase must NOT report
  success — an erasure that silently skipped bytes is a compliance failure that looks exactly like
  a completed one, but found Erased.
AnIncompleteErasure_IsA500WithAStableErrorCode_NeverA204 [FAIL]
  Expected type to be ProblemHttpResult, but found NoContent.
Failed! - Failed: 2, Passed: 10, Total: 12
```

The second line is the whole point of the task: the naive implementation answers **204 No Content** for a
session whose documents are still in blob storage.

### 5e. Manifest-driven erasure instead of prefix enumeration

**Break**: the `SessionFileEraser` call in `ChatSessionManager.DeleteSessionAsync` replaced with a loop that
deletes the file ids the manifest named.

```
Erasure_EnumeratesByPrefix_SoItReachesABlobNoManifestNames [FAIL]
  Expected erasure.BlobsDeleted to be 2 because erasure enumerates the blob PREFIX, so it reaches a
  copy no manifest names — a manifest walk would have deleted one of these two and reported
  success, but found 0 (difference of -2).
Failed! - Failed: 10, Passed: 2, Total: 12
```

10 of 12 failed, not 1 — and the reason is the finding, not noise: a manifest-driven eraser could not locate
**any** durable copy in this seam, because the manifest lives in Cosmos/Redis and the erasure deliberately does
not depend on either. That is precisely the day-91 condition made visible at test speed.

---

## 6. 🔔 The parts that are NOT synchronously erased (POML escalation trigger)

Stated explicitly rather than implied by "we called delete".

1. **Hot-index chunks (row 8) are asynchronous by design.** `ChatSessionManager` raises the existing
   fire-and-forget cleanup signal; `SessionFilesCleanupJob` evicts within seconds, with the 6-hourly scheduled
   sweep as backstop. When the AI compound gate is OFF the signal is null and only the scheduled sweep applies.
   Unchanged by this task — making it synchronous would put an AI-Search round trip on the delete path, and
   task 061 deliberately made that job structurally unable to reach anything else.
2. **A DISARMED durable store cannot be erased from.** With `BlobEndpoint` empty the store is inert, so
   erasure returns `StoreDisabled`. For a deployment that was never armed this is exactly correct — nothing was
   written either. For a deployment that was armed and later disarmed it is not: bytes written while it was
   enabled become unreachable by erasure until it is re-armed. **Disarming a live durable store is an
   operational decision with a compliance consequence**; the store should be drained (or left armed) rather
   than switched off with content in it.
3. **Azure Blob soft-delete and versioning are NOT enabled** on the storage account (`storage-account.bicep`
   defines only the tier-to-Cool rule, itself gated `false` in `customer.bicep`). So a completed delete is
   immediate and final — there is no retention window quietly holding a copy behind a successful erasure. If an
   operator later enables soft-delete for other reasons, **that becomes a new row 1b in §1** and this section
   must be revisited: erasure would then require an explicit permanent-delete.
4. **Dataverse transcript rows are retained deliberately** — see §7.

---

## 7. What is deliberately NOT erased, and why that mirrors `memory-items`

- **Dataverse `sprk_aichatsummary` / `sprk_aichatmessage`** — archive-not-delete, pre-existing. They hold the
  conversation transcript and **no file bytes or extracted text** (verified). This is the same boundary
  `MemoryItemStore.EraseSubjectAsync` already draws: *"erasure … affects ONLY the Tier 3 `memory-items`
  container. The Tier 2 `audit` container is NEVER touched — independent governance tiers per ADR-015."*
- **SPE** — reached only when the user explicitly persisted the upload into the DMS. That is a filed document
  with its own retention and permission model (ADR-007), not chat scratch. Erasing a conversation must not
  silently delete a document the user chose to file.

---

## 8. Placement Justification (root CLAUDE.md §10 bullet 2) + §11 gate

**Decision: in the BFF, under `Services/Ai/Sessions/`, hanging off the EXISTING session-deletion chokepoint.**

| Criterion | Answer |
|---|---|
| **A.1 — Could this live OUTSIDE the BFF?** | No. It runs inline on `DELETE /api/ai/chat/sessions/{id}` and must complete before that request answers — that synchronicity *is* the fail-closed contract (§3). Its two collaborators are BFF singletons (`SessionFileBlobStore`, `ITenantCache`). An out-of-band worker would make "the bytes are gone" an eventual claim the HTTP response could not make. |
| **A.2 — Which ADRs bind?** | ADR-009 (no `IMemoryCache`; none added), ADR-010 (**zero** new registrations — §8.1), ADR-013 (under `Services/Ai/`, no CRUD→AI dependency), ADR-014/015 (tenant partitioning + GDPR Art. 17), ADR-018 (no new flag — the empty `BlobEndpoint` remains the kill switch), ADR-019 (stable `errorCode` + `correlationId` on the new 500), ADR-032 (the store it consumes is registered unconditionally), ADR-038 (KEEP-path tests). |
| **A.3 — Publish-size regression?** | **+0.01 MB.** Zero `.csproj` delta. §10. |
| **A.4 — New CRUD→AI direct dependency?** | No. Everything is AI-side; no `PublicContracts/` facade needed. |
| **A.5 — Feature-module DI convention?** | N/A — no registration added. One existing factory line in `AnalysisServicesModule` gains an argument. |
| **A.6 — New config field on the playbook surface?** | None. No new configuration key at all. |

### 8.1 ADR-010 budget: **+0**

Project total stays at **3** (060 the store, 061 the rehydration service, 062 the retention job).
Repeatable metric — `grep -rEn "services\.(AddSingleton|AddScoped|AddTransient|AddHostedService)" src/server/api/Sprk.Bff.Api/ --include=*.cs | wc -l`:
**567 → 567**.

`SessionFileEraser` is a **static class** — exactly the shape task 062 used for `SessionFileRetentionPolicy` —
and its collaborators are things the one caller already holds. `SessionFileBlobStore` is already registered
unconditionally (ADR-032 P1), so wiring it into `ChatSessionManager` adds an **edge**, not a registration.

### 8.2 §11 three-question gate

**`SessionFileEraser`** (static, 0 registrations):

1. **Existing overlap?** Verified by reading, not assumed. `SessionFileRetentionJob` (062) deletes durable
   bytes but on a completely different trigger — *age* plus a definitively-absent Cosmos session — and could
   not serve a user-initiated deletion without inverting its safety rule (`Indeterminate` retains).
   `SessionFilesCleanupJob` (061) is *structurally* unable to reach this store, enforced by
   `SessionFilesCleanupScopeTests`. `MemoryItemStore.EraseSubjectAsync` is the right *pattern* but a different
   store (Cosmos `memory-items`, subject-partitioned).
2. **Extend instead?** Yes — and that is what this is. It extends `ChatSessionManager.DeleteSessionAsync`, the
   existing session-deletion chokepoint, and composes 062's `ListAsync` + `DeleteAsync` rather than adding a
   bulk delete to the store (which 062 explicitly declined to add before the semantics were defined). **No new
   endpoint** was added either: GDPR erasure of a session IS `DELETE /api/ai/chat/sessions/{id}`.
3. **Cost of doing nothing?** Concretely: a user deletes a conversation, it vanishes from History, and the
   original uploaded documents remain in blob storage indefinitely — ADR-015's *"MUST support user-initiated
   deletion in Tier 3 (GDPR right to erasure)"* unmet, and the §8a gate holding `BlobEndpoint` empty could
   never be lifted, making tasks 060–062 dead code in every deployed environment.

**`SessionUploadCacheKeys`** (internal constants, 0 registrations): the writer (`ChatDocumentEndpoints`) and
the eraser must compose the same cache key exactly. An eraser one character off removes nothing and reports
success — no exception, no count, no log line. One definition removes that failure mode by construction. A
third private copy remains in `Api/ComposeEndpoints.cs`; it is a READ path (drift degrades to "bytes not
found", not to a missed erasure) on a file several concurrent Compose tasks are editing, so it was left alone
deliberately — a safe follow-up.

### 8.3 One production concession, disclosed

`ChatSessionManager` now has **two** constructors: the original five-parameter one (delegating) and the new
six-parameter one. Reason: **Castle DynamicProxy and `Activator.CreateInstance` bind constructors by exact
arity and do not apply optional-parameter defaults**, so folding the new dependency into a trailing optional
parameter broke every `new Mock<ChatSessionManager>(…five args…)` in the suite — measured: **123 failures
across 16 test classes**, none of them related to session files. The alternative was editing 51 test files
(13 of them active Compose test files other tasks are editing) to append `null!`. The overload is one file,
zero conflict surface, and leaves every existing call site byte-identical.

---

## 9. 🔔 Is the gate closed? What arming `BlobEndpoint` now requires

ADR-015 open item **15-A** (from the 060 notes) required **both** retention and erasure before a persisted
store may be armed:

| Requirement | State |
|---|---|
| *"MUST define retention and deletion behavior for stored outputs"* | ✅ retention: task 062 (`SessionFileRetentionJob`) · ✅ deletion: **this task** |
| *"MUST support user-initiated deletion in Tier 3 (GDPR right to erasure)"* | ✅ `DELETE /api/ai/chat/sessions/{id}` now erases the durable bytes and fails closed if it cannot |
| *"MUST NOT exempt Tier 3 from GDPR deletion requirements"* | ✅ no exemption; the two non-erased locations (§7) are audit-tier and user-filed, matching the `memory-items` boundary |
| Tenant isolation on every store path (ADR-014/015) | ✅ eight paths, each with a cross-tenant negative test (§4) |

**The CODE gate is closed.** What remains is **infrastructure**, unchanged by any of 060–063 and outside this
task's boundary (they are Azure/bicep decisions):

1. Set `SessionFileStore:BlobEndpoint` to the storage account's blob endpoint. No bicep file emits it today.
2. Grant **Storage Blob Data Contributor** — `storage-account.bicep` creates the assignment only when
   `appServicePrincipalId` is passed, which `customer.bicep` and `stacks/model1-shared.bicep` do **not** do.
3. Grant it to the **UAMI's** principal id when `Graph:ManagedIdentity:ClientId` is pinned —
   `model2-full.bicep` currently grants the *system-assigned* identity.
4. Decide the container: default `ai-chunks`, with a dedicated `session-files` container as the recorded
   target (owner ruling 2026-08-25).

Strongly recommended for the first armed environment: `SessionFileStore:RetentionSweepDryRun=true` for the
first passes (062 open item 4). Note that dry-run affects the **retention sweep only** — erasure is
user-initiated and always deletes.

### Two pre-existing weaknesses that arming makes sharper (both still open from 060)

- **`X-Tenant-Id` header fallback** (060 §10.4). It is the partition key of a durable store that can now
  **delete**. `ChatEndpoints.ExtractTenantId` has the same fallback as the upload path, so a principal without
  a `tid` claim can name its own tenant on the delete route too. Blast radius is bounded by the fact that a
  spoofed tenant can only erase within *its own* (spoofed) prefix — but that prefix is another tenant's if the
  spoof is chosen accordingly. **This is the single highest-value follow-up before arming.**
- **Same-tenant cross-user session access** (060 §10.5). `GetSessionAsync` is tenant-scoped only, so a user who
  knows another user's `sessionId` can delete that session — including its durable bytes. Pre-existing (the
  session record was already deletable); durability raises the consequence. Not in scope for a task scoped to
  adding erasure, and fixing it is an auth change requiring human sign-off (root §6).

---

## 10. Gate results

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **succeeded**, 0 errors. 7 warnings, all pre-existing `CS0618 DemoProvisioningOptions` — none from changed files |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/` | **11,335 passed / 0 failed / 97 skipped** (baseline 11,321 / 0 / 97) — **+14**, all new (§11) |
| `dotnet test tests/Spaarke.ArchTests/` | **62 passed / 0 failed** (baseline 62) — incl. `SessionFilesCleanupScopeTests`, untouched and unweakened |
| `dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/` | **96 passed / 6 skipped** (baseline 96 / 6) |
| `dotnet format whitespace --verify-no-changes` (changed paths only, both projects) | clean, exit 0 |
| Line endings (`od -An -tx1 <f> \| tr ' ' '\n' \| grep -c '^0d$'`) | every changed `.cs` pure CRLF — `CR == LF` on all 9 |
| Publish size (**pwsh 7.6.3**, `Compress-Archive -CompressionLevel Optimal`) | **45.05 MB** zip · **215 files** · **4 .pdb** · **raw dir sum 137.48 MB**. vs task 062's 45.04 / 215 / 4 / 137.46 → **+0.01 MB**; 14.95 MB under the 60 MB ceiling |
| New NuGet packages | **0** — zero `.csproj` delta |
| New DI registrations | **0** (§8.1) |
| `dotnet list package --vulnerable --include-transitive` | **no vulnerable packages** |
| New Azure resources / config keys | **0** |
| `SessionFileStore:BlobEndpoint` | **still empty** — unchanged by this task |
| `SessionFilesCleanupJob` / `SessionFilesHotIndexAccess` touched | **no** — zero diff |

---

## 11. Files changed and the +14 test delta

### Server (6 files, 2 new)

| File | Change |
|---|---|
| `Services/Ai/Sessions/SessionFileEraser.cs` | **NEW.** `SessionFileErasureState` / `SessionFileErasureResult` + the static eraser (prefix enumerate → delete → verify) and the `doc-upload-*` cache eviction. Zero DI. |
| `Services/Ai/Sessions/SessionUploadCacheKeys.cs` | **NEW.** The one definition of the four `doc-upload-*` resources, the version, and the `{sessionId}:{fileId}` cache id. |
| `Services/Ai/Chat/ChatSessionManager.cs` | `DeleteSessionAsync` → `Task<SessionFileErasureResult>`; manifest read, durable erasure (fail-closed), cache eviction, all before the existing steps. New optional `durableFileStore` dependency + the arity-preserving ctor overload (§8.3). New private `TryReadManifestFileIdsAsync`. |
| `Api/Ai/ChatEndpoints.cs` | `DeleteSessionAsync` handler returns 500 + `session.durable-erasure-incomplete` on `Incomplete`; route metadata + description updated; handler `private` → `internal` so the contract is asserted against the real handler. |
| `Api/Ai/ChatDocumentEndpoints.cs` | Four private cache-resource consts + `CacheVersion` + `DocCacheId` now delegate to `SessionUploadCacheKeys`. No behaviour change. |
| `Infrastructure/DI/AnalysisServicesModule.cs` | `ChatSessionManager` factory gains `durableFileStore: sp.GetService<SessionFileBlobStore>()`. No new registration. |

### Tests (3 files, 1 new) — +14

| Suite | File | Count |
|---|---|---|
| `tests/integration/seam/Ai/SessionFileErasureSeamTests.cs` | **NEW** — completeness, prefix enumeration, session scoping, idempotency, partial-erasure convergence, delete failure, enumeration failure, the 500 contract, two cross-tenant, two disabled-store | **12** |
| `tests/integration/tenant/Ai/SessionFileBlobStoreTenantIsolationTests.cs` | **extended** — erasure composition isolation (2 facts) | **2** |
| `tests/integration/contract/Api/Ai/ChatDocumentEndpointsContractTests.cs` | fixture only — exposes the upload endpoint's `ITenantCache` so erasure runs against entries a REAL upload wrote | 0 |
| **Total** | | **14** |

Both test paths are ADR-038 KEEP paths (`tests/integration/seam/**` vertical-slice-seam,
`tests/integration/tenant/**` tenant-isolation) and both files are MAINTAIN-class: each test names a concrete
production behaviour whose regression it catches, and §5 records each observed failing under a deliberate break.
No existing test was deleted or weakened; `SessionFilesCleanupScopeTests` has zero diff.

---

## 12. 🔔 Open items for the owner

1. **ADR-015 governed-stores row — proposed text.** The 060 notes' Tier-3 row was recorded with retention and
   erasure marked NOT YET IMPLEMENTED; 062 supplied the retention cell. Both cells can now read (I cannot edit
   `.claude/` — root CLAUDE.md §3):

   | Tier | Store | Content Allowed | Retention | Access | GDPR Erasure |
   |---|---|---|---|---|---|
   | **Tier 3: Work History** | Azure Blob — `{tenantId}/session-files/**` | Original uploaded bytes of chat-session files | 90 days default, following the session document; **indefinite** when `StoredSession.Ttl == -1` (filed) — enforced by `SessionFileRetentionJob` (task 062) | Owning tenant only; tenant is the first blob-name segment | **Yes (Art. 17)** — `DELETE /api/ai/chat/sessions/{id}` erases the durable bytes by tenant/session PREFIX, verifies the prefix is empty, and fails closed (HTTP 500, `session.durable-erasure-incomplete`) rather than reporting an unconfirmed erasure — `SessionFileEraser`, task 063 |

   With that row, **ADR-015 open item 15-A can be marked RESOLVED** and the §8a project-scoped exception
   (path A, 060) retired: the condition it was bounding — "a store with no defined deletion path" — no longer
   holds.

2. **`.claude/CHANGELOG.md` entry** (proposed text, for the main session to apply):
   > `spaarkeai-compose-r8` task 063 — session deletion and GDPR erasure now remove every copy of a chat
   > session's uploaded file bytes: the durable blob (enumerated by tenant/session PREFIX, never by walking
   > the Cosmos manifest, then verified empty) plus the four 4-hour `doc-upload-*` Redis copies. A partial
   > failure reports `Incomplete`, leaves the session record intact, and returns HTTP 500
   > `session.durable-erasure-incomplete` so the retry converges — an unconfirmed erasure is never a 204.
   > Erasure is composed from task 062's list+delete primitives (no new store surface, no new DI
   > registration) and mirrors the `memory-items` erasability pattern. Tenant isolation is now covered by a
   > cross-tenant negative test on all eight Track B store paths. Closes the ADR-015 code gate that has held
   > `SessionFileStore:BlobEndpoint` empty since task 060; arming it now needs only the four Azure steps.

3. **`design.md` `<hot-path-declaration>`** — this task touches BFF (`Services/Ai/Sessions/**`,
   `Services/Ai/Chat/ChatSessionManager.cs`, `Api/Ai/ChatEndpoints.cs`, `Api/Ai/ChatDocumentEndpoints.cs`,
   `Infrastructure/DI/AnalysisServicesModule.cs`) and **not** SpaarkeAi, ci-workflows, skill-directives or root
   CLAUDE.md. `AnalysisServicesModule.cs`, `ChatEndpoints.cs` and `ChatDocumentEndpoints.cs` are
   high-collision files; `/conflict-check` before the PR.

4. **`DELETE /api/ai/chat/sessions/{id}` can now return 500.** Any client that treats a non-204 as "gone"
   must be checked — the whole point is that a 500 here means **nothing was deleted** and the request should be
   retried. Client work was outside this task's file boundary.

5. **The two auth weaknesses in §9** are the highest-value follow-ups before `BlobEndpoint` is armed.
