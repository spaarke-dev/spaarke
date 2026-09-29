# Track B — Retention + server-authoritative availability (task 062, FR-B04/B05)

> **Task**: `062-retention-availability.poml`
> **Date**: 2026-08-26
> **Rigor**: FULL · opus @ xhigh
> **Depends on**: 060 (durable byte store), 061 (lazy re-index + cleanup-scope narrowing)
> **Store state**: `SessionFileStore:BlobEndpoint` remains **EMPTY**. Nothing in this task arms it.

---

## 1. The retention fact this task is built around

The Cosmos `sessions` container carries `DefaultTimeToLive = 7776000` (90 days) with a per-item override
(`StoredSession.Ttl`): `null` rides the 90-day default, `-1` (`StoredSession.NeverExpireTtl`) means
INDEFINITE and is set when a session is FILED to an Analysis.

**Blobs have no TTL of their own.** So the manifest can expire while the bytes persist — and a retention
rule keyed off the manifest ALONE cannot even *see* those bytes, because the manifest is the thing that
disappeared. That single fact decides the architecture:

- Retention is driven from the **blob side** (`ListAllForRetentionAsync`), asking Cosmos about each session
  it finds — not from the manifest side.
- The **session document's existence** is the retention signal. Cosmos already implements the 90-day
  default, the sliding refresh on every write, and the `-1` override; re-deriving expiry from timestamps in
  application code would be a second, divergent implementation of a rule the database already enforces.

The rule, in full: **session document present ⇒ retain. Definitively absent (a real Cosmos 404) AND the
blob older than the 90-day window ⇒ delete. Everything else retains.** FR-B04's "90-day default for
unfiled, indefinite for filed" falls out of that with no arithmetic.

---

## 2. Every `Ttl` comparison — the enumeration AC #1 requires

`grep -nE "\bTtl\b|NeverExpireTtl|7776000|DefaultTimeToLive" src/server --include=*.cs`, filtered to
`StoredSession.Ttl` (the many unrelated cache-TTL hits are excluded):

| # | Site | Kind | How it handles `-1` |
|---|---|---|---|
| 1 | `Services/Ai/Sessions/StoredSession.cs:23` | **Definition** — `public const int NeverExpireTtl = -1;` | n/a |
| 2 | `Services/Ai/Sessions/StoredSession.cs:146` | **Property** `int? Ttl`, `[JsonIgnore(WhenWritingNull)]` | n/a |
| 3 | `Services/Ai/Chat/ChatSessionManager.cs:695-697` | **Derivation** — `Ttl = filed ? NeverExpireTtl : null` | Writes the sentinel; no comparison |
| 4 | `Services/Ai/Sessions/SessionPersistenceService.cs` (read-modify-write paths) | **Round-trip** — the value rides on the deserialized `StoredSession` | Preserved untouched; no comparison |
| 5 | `Infrastructure/DI/AiPersistenceModule.cs:60-61` | **XML doc comment** | n/a |

**Finding: before this task there were ZERO expiry comparisons against `StoredSession.Ttl` anywhere in the
codebase.** Cosmos does all the expiring; application code only ever *wrote* the sentinel and round-tripped
it. That is worth stating plainly, because it means the `-1` trap the POML warns about is **entirely a
property of the code this task introduces** — there was no pre-existing mis-comparison to find and fix.

The comparisons task 062 adds, and how each handles the sentinel:

| # | New site | How it handles `-1` |
|---|---|---|
| 6 | `SessionFileRetentionPolicy.IsIndefiniteTtl(int?)` | `ttl.HasValue && ttl.Value <= 0` — the sentinel plus a deliberately-safe superset (see §3) |
| 7 | `SessionFileRetentionPolicy.Evaluate(...)` | Calls #6 **as the first statement**, before the state switch and before any age arithmetic. A filed session cannot reach an arithmetic path at all |
| 8 | `SessionFileRetentionJob.RunPassAsync` (pre-delete guard) | Re-checks `verdict == Expired && probe.State == Absent && !IsIndefiniteTtl(probe.Ttl)` immediately before calling `DeleteAsync` — the deletable condition is stated twice, in two files |
| 9 | `SessionPersistenceService.ProbeSessionRetentionAsync` | **Reads** `Ttl` onto the probe. No comparison |

`Services/Ai/Memory/MemoryItemDocument.Ttl` is a different type on a different store (`memory-items`) and is
out of scope; it was checked and carries no expiry comparison either.

### Why `<= 0` and not `== -1`

Cosmos defines exactly one sentinel (`-1`) and rejects `0` outright, so no legitimate short expiry lives in
the non-positive range. Widening the predicate can therefore only ever cause bytes to be **KEPT**; narrowing
it, or comparing numerically, deletes the files of filed matters. Given that asymmetry the safe direction is
the only defensible one. Pinned by `NonPositiveTtlIsAlwaysIndefinite` / `PositiveOrAbsentTtlIsNotIndefinite`.

---

## 3. What was built

### Server

| File | Change |
|---|---|
| `Services/Ai/Sessions/SessionFileRetentionPolicy.cs` | **NEW.** Pure static decision + `SessionRetentionState` / `SessionRetentionProbe` / `SessionFileRetentionVerdict`. Zero DI, zero I/O. |
| `Services/Ai/Sessions/SessionFileRetentionJob.cs` | **NEW.** `BackgroundService` + `PeriodicTimer` expiry pass (ADR-001), mirroring `SessionFilesCleanupJob`'s shape. Groups blobs by `(tenant, session)`, one Cosmos probe per session, skips sessions with no aged blob. |
| `Services/Ai/Sessions/SessionFileBlobStore.cs` | `ListAsync(tenant, session?)`, `ListAllForRetentionAsync()`, `DeleteAsync(tenant, session, file)`, `TryParseBlobName`, shared `IsSafeSegment`; gateway gains `ListAsync`/`DeleteAsync`. |
| `Services/Ai/Sessions/ISessionPersistenceService.cs` + `SessionPersistenceService.cs` | `ProbeSessionRetentionAsync` — a Cosmos point read that distinguishes 404 from failure (§4). |
| `Services/Ai/Sessions/SessionFileRehydrationService.cs` | `ProbeSessionAvailabilityAsync` + `SessionFileAvailability` record. |
| `Services/Ai/Sessions/RestoredSession.cs` | `RestoredUploadedFile.ContentAvailable` (`bool?`). |
| `Services/Ai/Sessions/SessionRestoreService.cs` | Computes availability; optional trailing ctor param. |
| `Api/Ai/ChatEndpoints.cs` | `SessionRestoreUploadedFileDto.ContentAvailable` → wire `contentAvailable`. |
| `Infrastructure/DI/AiPersistenceModule.cs` | ONE `AddHostedService` (§6). |
| `appsettings.template.json` | Documents the retention rule + two optional keys. `BlobEndpoint` untouched (still empty). |

### The delete/list surface IS the shared primitive task 063 needs

Task 063 (GDPR erasure) composes it without adding store surface:

- erase one session → `ListAsync(tenantId, sessionId)` then `DeleteAsync` per row;
- erase a subject/tenant → `ListAsync(tenantId)` then `DeleteAsync` per row.

Enumerating by **prefix** rather than by walking the Cosmos manifest is load-bearing for 063 specifically:
the 060 notes' open item #6 warns that an orphaned blob (durable write lands before the non-fatal manifest
write) is named by no manifest, so a manifest-driven erasure would leave it behind. Prefix enumeration
reaches it. `ParsedListings_AlwaysAttributeABlobToTheTenantItIsPhysicallyStoredUnder` pins the property that
makes the system-scope enumeration tenant-correct without a caller tenant to trust.

No bulk convenience deletes were added — 063 should compose the two primitives rather than inherit a
`DeleteSessionAsync` whose semantics it has not defined yet.

---

## 4. The most dangerous thing found, and what was done about it

`SessionPersistenceService.LoadFromCosmosAsync` **catches every exception and returns `null`**
(`SessionPersistenceService.cs:754-760`). That is right for a restore — a session that cannot be read
degrades to the new-session path instead of 500ing. It is **catastrophic** for retention: a Cosmos outage,
a throttle, or a missing container would present as *"every session has expired"*, and a sweep built on
`LoadSessionAsync(...) is null ⇒ delete` would delete every durable byte in the account, silently, in one
pass.

Retention therefore does **not** consume that read. `ProbeSessionRetentionAsync` is a separate Cosmos point
read that returns `Absent` **only** on a genuine `CosmosException(404)` and `Indeterminate` on anything
else; `OperationCanceledException` propagates. `SessionFileRetentionPolicy` treats `Indeterminate` as
RETAIN, and `SessionFileRetentionJob` re-checks `probe.State == Absent` immediately before deleting.
Pinned by `IndeterminateProbe_RetainsEverything` and `ProbeThatThrows_IsTreatedAsIndeterminate_NotAsExpired`.

It reads Cosmos directly rather than Redis→Cosmos: a Redis miss carries no retention information (24h
sliding TTL), and `LoadSessionAsync`'s re-warm side effect must not fire for thousands of long-dead sessions.

---

## 5. FR-B05 — availability, and the tri-state

`contentAvailable` on the restore projection is now server-supplied:

| Value | Meaning |
|---|---|
| `true` | A durable copy exists for this tenant → content lives as long as the session does (re-indexed on demand by task 061's `SessionFileRehydrationService`). |
| `false` | The durable store is configured and holds no copy → content is not guaranteed beyond the hot index's own window. |
| `null` | The server cannot answer (store not configured, or the probe failed). Render as UNKNOWN. |

**`null` is a first-class answer, not an oversight.** Collapsing "no durable copy" and "this deployment has
no durable store" into a single `false` would mark every file in every not-yet-enabled deployment as
unavailable — the mirror image of the failure FR-B05 names ("files that exist but are reported
unavailable"). `StoreDisabled_ReportsUnknown_NeverUnavailable` pins it.

Cost: **one** blob prefix listing answers a whole 20-file manifest, it is skipped entirely when the session
has no uploads or the store is disabled (no network call at all), and any failure degrades to `null` — an
availability signal must never be able to fail a restore or blow its <500ms p95 NFR.

R7's re-attach layer is **reused, not rebuilt**: no new endpoint, no new client fetch, no new session-restore
surface. The field rides the existing `GET /api/ai/chat/sessions/{id}/restore` payload the R7 chip already
consumes.

### 🔔 Known, bounded consequence the owner should see

While `BlobEndpoint` is empty — today, and until 063 merges — `contentAvailable` is `null` for every file,
so the R7 dimmed "no longer available" chip **stops appearing** even though the underlying 24h AI-Search
eviction is still real. That is the deliberate trade FR-B05 requires ("replaced or removed — not left in
place alongside a server signal"): a client-side guess and a server fact are two sources, which is the drift
the FR exists to remove.

Two honest options if the owner wants the warning back before 063:

1. Enable the store (after 063 merges) — the signal becomes fully truthful and the chip returns for exactly
   the files that genuinely lack a durable copy.
2. Have the server also consult the hot index in the `false`/`null` branch. **Not done**, deliberately: it
   costs a search round trip on the restore path, and an inference about another store's sweep timing is
   still an inference — it would re-introduce a guess, just relocated to the server.

Residual imprecision of `false` (worth knowing when writing UI copy): a `false` file may still be recallable
while its hot chunks survive. `false` means "not guaranteed", which is why the recommended client copy is
"may no longer be available" rather than R7's flat "no longer available" (§8).

---

## 6. Placement Justification (root CLAUDE.md §10 bullet 2) + §11 gate

**Decision: in the BFF, under `Services/Ai/Sessions/`, beside the store it sweeps.**

| Criterion | Answer |
|---|---|
| **A.1 — Could this live OUTSIDE the BFF?** | No. The availability probe is inline on the OBO restore request. The sweep's two collaborators are a BFF singleton (`SessionFileBlobStore`) and a BFF-scoped Cosmos service; an Azure Function would need its own blob + Cosmos identity and would duplicate the tenant-partitioning rules that are the safety property. ADR-001 names `BackgroundService` + `PeriodicTimer` for in-process periodic work. |
| **A.2 — Which ADRs bind?** | ADR-001 (hosted service, not a job framework), ADR-009 (no `IMemoryCache` — none added), ADR-010 (+1 registration, justified below), ADR-013 (under `Services/Ai/`, no CRUD→AI dependency), ADR-014/015 (tenant partitioning + retention/erasure), ADR-018 (no new feature flag — the empty `BlobEndpoint` is the kill switch), ADR-032 (unconditional registration), ADR-038 (KEEP-path tests). |
| **A.3 — Publish-size regression?** | None material. Zero `.csproj` delta. §7. |
| **A.4 — New CRUD→AI direct dependency?** | No. No `PublicContracts/` facade needed. |
| **A.5 — Feature-module DI convention?** | Yes — inside `AddAiPersistenceModule`, beside 060's and 061's registrations. No `Program.cs` line. |
| **A.6 — New config field on the playbook surface?** | N/A. Two optional **app-configuration** keys, no Dataverse columns. |

### §11 three-question gate — the new components

**`SessionFileRetentionJob`** (the only new DI registration):

1. **Existing overlap?** `SessionFilesCleanupJob` is the only adjacent sweeper, and it is the wrong one: it
   evicts the hot AI-Search index on a 24h Redis-key signal, and task 061 deliberately made it
   *structurally* incapable of reaching durable bytes (no `IServiceProvider`; reachable surface = one
   `SearchClient` + a read-only multiplexer), enforced by `SessionFilesCleanupScopeTests`. Verified by
   reading it, not assumed.
2. **Extend instead?** No — extending it would undo the exact property 061 was sequenced before 062 to
   establish, and would fail that ArchTest. The generic cron framework (`SchedulingModule`'s
   `ScheduledJobHost` / `IScheduledJob`) was the other candidate: it costs **two** registrations plus a
   seeded definition, and keeps run history in an explicitly-interim `InMemoryBackgroundJobStore` — more
   surface, not less.
3. **Cost of doing nothing?** Concretely: durable bytes accumulate with **no expiry at all**; ADR-015's
   "MUST define retention and deletion behavior for stored outputs" stays unmet; and the §8a gate holding
   `BlobEndpoint` empty can never be lifted — which makes tasks 060 and 061 dead code in every deployed
   environment.

**`SessionFileRetentionPolicy`** — static class, **0 registrations**. **`ProbeSessionRetentionAsync`** —
a method on the EXISTING `ISessionPersistenceService` (the Cosmos handle, partition-key convention and
tenant scoping already live there). **`ProbeSessionAvailabilityAsync`** — a method on the EXISTING
`SessionFileRehydrationService` (same store, same tenant scoping, same disabled-store handling; its two
"cannot help you" states are literally the two `SessionFileRehydrationOutcome` values 061 already defined).
A separate availability service would have duplicated all three and cost a fourth registration.

### ADR-010 budget

**+1** (`AddHostedService<SessionFileRetentionJob>`). Project total is now **3** (spec §11 budgeted one; 060
took #1, 061 took #2). Repeatable metric —
`grep -rEn "services\.(AddSingleton|AddScoped|AddTransient|AddHostedService)" src/server/api/Sprk.Bff.Api/ --include=*.cs | wc -l`:
**566 → 567** (and `AiPersistenceModule.cs` itself 13 → 14). Unconditional (ADR-032 P1): collaborators are a singleton store and a delegate, neither
feature-gated; "no blob endpoint" is a RUNTIME state (`ExecuteAsync` logs once and returns without starting
a timer), never a DI state.

### One deliberate structural choice

The job takes a **one-method delegate** (`SessionRetentionProbeDelegate`) rather than `IServiceProvider` /
`IServiceScopeFactory`. `ISessionPersistenceService` is Scoped so a scope IS required — but the scope
factory stays in the DI closure, not in the job. Task 061's finding was that an ambient service locator
inside a component whose job is to DELETE things is a reach, not a boundary; this job is the *first*
component that genuinely can delete durable bytes, so the same reasoning binds harder here. After
construction the job can reach exactly two things: the store, and the delegate.

---

## 7. Verification record — every claim observed failing first

A green suite is not evidence. Each assertion below was watched failing against a deliberately broken
implementation.

### 7a. The naive sentinel comparison — the failure this task exists to prevent

**Break applied**: the `IsIndefiniteTtl` short-circuit removed from `Evaluate`, and the `Present` branch
replaced with the naive implementation the POML warns about — `ttl = probe.Ttl ?? 7776000;
age >= ttl ⇒ Expired`. With `Ttl == -1`, `age >= -1` is always true.

With ONLY the policy broken (the job's pre-delete guard still in place), 2 of 22 failed — and **no filed
file was deleted**, because the job's second guard caught it:

```
TheSentinelIsCheckedBeforeAnyAgeArithmetic [FAIL]
  Expected ... to be RetainIndefinitely because the -1 sentinel must short-circuit before any age
  comparison can run, but found Expired.
FiledSession_WithTheMinusOneSentinel_KeepsItsFilesThroughAnExpiryPass [FAIL]
  Expected result.BlobsRetainedIndefinitely to be 2 ..., but found 0.
```

That is the defence-in-depth working, and it is also why `BlobsRetainedIndefinitely` is a counter rather
than an implicit outcome — without it the break would have been invisible.

With BOTH the policy and the job's guard broken (the true naive implementation), **filed matters' files were
actually destroyed**:

```
FiledSession_WithTheMinusOneSentinel_KeepsItsFilesThroughAnExpiryPass [FAIL]
  Expected result.BlobsDeleted to be 0, but found 2 (difference of 2).
UnfiledSession_WhoseDocumentStillExists_KeepsItsFiles [FAIL]
  Expected result.BlobsDeleted to be 0, but found 1 (difference of 1).
ExpiringOneTenantsSession_LeavesAnotherTenantsIdenticallyIdentifiedFilesAlone [FAIL]
  Expected result.BlobsDeleted to be 1, but found 2.
TheSentinelIsCheckedBeforeAnyAgeArithmetic [FAIL]
Failed! - Failed: 4, Passed: 18, Total: 22
```

`TheSentinelIsCheckedBeforeAnyAgeArithmetic` pins the ORDER, not just the outcome: it feeds a probe that is
`Absent` AND carries `-1` — a state production never produces — precisely so that moving the sentinel check
below the state switch fails while every realistic test still passes.

### 7b. Tenant isolation on the LISTING path

**Break applied**: the tenant segment dropped from `ListAsync`'s prefix (listing the whole container) and
the `AssertTenantPartitioned` tripwire removed — the careless-refactor shape.

```
List_ReturnsOnlyTheCallingTenantsBlobs [FAIL]
  Expected listedForB to contain a single item because tenant B has exactly one durable copy, but found <2 items>
List_ScopedToASession_DoesNotReachAnotherTenantsIdenticallyNamedSession [FAIL]
  Expected listedForB to be empty because knowing another tenant's session id must not enumerate that
  session's files — it is an identifier, not a capability, but found <1 item>
Failed! - Failed: 2, Passed: 29, Total: 31
```

A listing that crosses the boundary leaks the **existence and size** of another tenant's files even when the
bytes stay unreadable, which is why this needed its own coverage beyond 060's read/write suite.

### 7c. The availability probe reaching across tenants

**Break applied**: `ProbeSessionAvailabilityAsync` switched from `ListAsync(tenantId, sessionId)` to
`ListAllForRetentionAsync()` filtered by `sessionId` — the realistic mistake of reaching for the
system-scope enumeration on a request path.

```
AnotherTenantsDurableCopy_DoesNotMakeAFileLookAvailable [FAIL]
  Expected AvailabilityOf(forOther, DurableFileId) to be false because availability is answered from the
  CALLING tenant's prefix — another tenant's copy is not merely unreadable, it is invisible, but found True.
```

That test carries a positive control in the same body (the owning tenant sees `true`), so the `false`
assertion cannot pass vacuously.

### 7d. The tri-state collapse

**Break applied**: `StoreDisabled` returning an empty answered set instead of "unanswered".

```
StoreDisabled_ReportsUnknown_NeverUnavailable [FAIL]
Failed! - Failed: 1, Passed: 7, Total: 8
```

---

## 8. 🔔 Client hand-off — `AttachedFileSummary` (NOT applied here)

Task 062's file boundary excluded `src/client/**` and `src/solutions/**`, so the client half is specified
here for the main session to apply. Note the POML's `<relevant-files>` pointer to
`Spaarke.Compose.Components` is **stale** — `AttachedFileSummary` lives in the SpaarkeAi code page.

**File**: `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`,
`handleSelectHistorySession` (~lines 2730-2748).

**Current behaviour** — computes a client-side 24h window and uses the server value only if present:

```ts
const stamps = (spec.recentMessages ?? [])
  .map((m) => (m.timestamp ? Date.parse(m.timestamp) : NaN))
  .filter((n) => !Number.isNaN(n));
const lastTs = stamps.length ? Math.max(...stamps) : NaN;
const withinWindow = Number.isNaN(lastTs)
  ? true
  : Date.now() - lastTs < 24 * 60 * 60 * 1000;
const files: AttachedFileSummary[] = rawFiles.map((f) => ({
  id: f.fileId,
  filename: f.fileName,
  status: "ready",
  available: typeof f.contentAvailable === "boolean" ? f.contentAvailable : withinWindow,
}));
```

**Required behaviour** — delete the `stamps` / `lastTs` / `withinWindow` computation entirely (and the now
unused `recentMessages?: Array<{ timestamp?: string }>` member of the local response type), leaving:

```ts
// FR-B05: availability is SERVER-authoritative. `undefined` means the server could not answer
// (durable store not configured) and MUST render as unknown — never as unavailable. Do NOT
// re-introduce a client-side window: two availability sources is the drift this removed.
const files: AttachedFileSummary[] = rawFiles.map((f) => ({
  id: f.fileId,
  filename: f.fileName,
  status: "ready",
  available: typeof f.contentAvailable === "boolean" ? f.contentAvailable : undefined,
}));
```

The server now emits `contentAvailable` on `GET /api/ai/chat/sessions/{id}/restore` →
`uploadedFiles[]`, so the existing declared shape (`contentAvailable?: boolean`) needs no change.

**Also recommended** (`ConversationPaneChrome.tsx` ~lines 595-604, 655-672): `available === false` no longer
means "definitely gone", it means "not guaranteed". Suggested copy change — `" — no longer available"` →
`" — may no longer be available"`, and `"some files no longer available"` →
`"some files may no longer be available"`. Update the `AttachedFileSummary.available` doc comment (which
still describes the R7 24h heuristic) to the tri-state. The `f.available === false` checks themselves are
already correct for a tri-state and need no logic change.

**Verification after applying**: `grep -n "24 \* 60 \* 60 \* 1000" src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`
must return nothing (the AC's "verified by grep"). Existing test
`ConversationPaneChrome.files-availability.test.tsx` should be re-run.

---

## 9. Gate results

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **succeeded**, 0 errors. 7 warnings, all pre-existing `CS0618 DemoProvisioningOptions` — none from changed files |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/` | **11,315 passed / 0 failed / 97 skipped** (baseline 11,277 / 0 / 97) — **+38**, all new (see §10) |
| `dotnet test tests/Spaarke.ArchTests/` | **62 passed / 0 failed** (baseline 62) |
| `dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/` | **96 passed / 6 skipped** (baseline 96 / 6) |
| `dotnet format whitespace --verify-no-changes` (changed paths only, both projects) | clean, exit 0 |
| Line endings (`od -An -tx1 <file> \| grep -c '^0d$'`) | every changed `.cs` is pure CRLF, `lf == crlf` on all 16 |
| Publish size (**pwsh 7.6.3**, `Compress-Archive -CompressionLevel Optimal`) | **45.04 MB** zip · **215 files** · **4 .pdb** · **raw dir sum 137.46 MB**. vs task 060's 45.00 MB / 215 / 4 / ~137.4 MB → **+0.04 MB**; 14.96 MB under the 60 MB ceiling |
| New NuGet packages | **0** — zero `.csproj` delta |
| `dotnet list package --vulnerable --include-transitive` | **no vulnerable packages** |
| New Azure resources / lifecycle policies | **0**. No blob lifecycle DELETE rule added or wanted (§1) |
| `IMemoryCache` introduced | **none** (ADR-009) |
| `SessionFilesCleanupJob` / `SessionFilesHotIndexAccess` touched | **no** — zero diff, as 061 required |

---

## 10. Test delta — all +38 accounted for

| Suite | File | Count |
|---|---|---|
| `tests/integration/seam/Ai/SessionFileRetentionSeamTests.cs` | **NEW** — the expiry pass | 22 |
| `tests/integration/seam/Ai/SessionFileAvailabilitySeamTests.cs` | **NEW** — server-authoritative availability | 8 |
| `tests/integration/tenant/Ai/SessionFileBlobStoreTenantIsolationTests.cs` | **extended** — list/delete isolation (1 theory × 4 cases + 4 facts) | 8 |
| **Total** | | **38** |

One existing test was **modified, not deleted**:
`SessionRestoreServiceUploadedFilesTests.RestoreSessionAsync_WhenSessionHasUploadedFiles_ProjectsMinimalManifest`
asserts `RestoredUploadedFile` has exactly the ADR-015-minimal member set. FR-B05 adds a fifth member, so the
expected set was updated from 4 to 5 **with the rationale recorded inline**: `ContentAvailable` is a boolean
saying WHETHER content still exists, never any of the content itself, so the minimisation rule the assertion
protects is intact. A `ContentAvailable.Should().BeNull()` assertion was added in the same test (that fixture
wires no availability collaborator, so UNKNOWN is the required degradation).

Test placement: both new files are on ADR-038 KEEP paths (`tests/integration/seam/**` vertical-slice-seam,
`tests/integration/tenant/**` tenant-isolation). Both are MAINTAIN-class: each names a concrete production
behaviour whose regression they catch, and §7 records each observed failing.

---

## 11. 🔔 Open items for the owner

1. **The gate is now half-closed, not closed.** ADR-015 open item 15-A (from the 060 notes) required BOTH
   retention and erasure. Retention is now defined and enforced; **erasure (063) is still missing**, so
   `SessionFileStore:BlobEndpoint` MUST stay empty. Nothing in this task changed it.
2. **ADR-015 governed-stores row.** The 060 notes proposed a Tier-3 row for this store with retention and
   erasure marked NOT YET IMPLEMENTED; the owner recorded it as done. The **retention** cell can now read
   *"90 days default (follows the session document); indefinite when `StoredSession.Ttl == -1` — enforced by
   `SessionFileRetentionJob`, task 062"*. I cannot edit `.claude/` (root CLAUDE.md §3) — flagging for the
   main session.
3. **`.claude/CHANGELOG.md` entry** (proposed text, for the main session to apply):
   > `spaarkeai-compose-r8` task 062 — durable session-file retention follows the session's own Cosmos
   > retention (90-day default; indefinite for filed, `StoredSession.Ttl == -1`), swept by
   > `SessionFileRetentionJob`; `SessionFileBlobStore` gains the prefix-list + delete primitive task 063
   > reuses; file availability on session restore becomes server-authoritative (`contentAvailable`
   > tri-state), retiring R7's client-side 24h heuristic.
4. **Dry-run for the first enablement.** `SessionFileStore:RetentionSweepDryRun=true` makes the sweep
   evaluate and log every verdict while deleting nothing. Strongly recommended for the first passes in any
   environment where this store is enabled for the first time — a wrong retention rule is otherwise
   discovered as missing data.
5. **The availability regression while the store is disabled** — see §5's boxed note. It is a deliberate
   consequence of FR-B05's "one source" requirement, not an oversight, but the owner should see it before
   UAT reports "the no-longer-available chip disappeared".
6. **Still open from 060, untouched here**: the `X-Tenant-Id` header fallback (060 §10.4) is now also the
   partition key of a store that can DELETE. Same severity, one more consequence. Same-tenant cross-user
   overwrite (060 §10.5) likewise.
7. **Process error, disclosed.** During break-verification I ran `git checkout -- <file>` on
   `SessionFileBlobStore.cs`, which discarded that file's uncommitted edits (and violated the task's "no git
   writes" rule). The edits were fully reapplied and the final state is verified by the suites in §9; no
   other file was affected and nothing was committed, but the rule was broken and it should be visible.
