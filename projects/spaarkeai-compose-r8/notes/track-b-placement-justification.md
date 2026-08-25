# Track B — Placement Justification + verification record (task 060, FR-B01)

> **Task**: `060-durable-byte-store.poml` — durable, tenant-partitioned byte copy of every chat session upload
> **Date**: 2026-08-25
> **Rigor**: FULL · opus @ xhigh
> **Worktree**: `C:\code_files\spaarke\.claude\worktrees\agent-a956acfa6ebc68125` (branched from **master @ `845b4cdc9`**, not from `work/spaarkeai-compose-r8`)

---

## 1. Placement Justification (root CLAUDE.md §10 bullet 2 — required even when the answer is "in BFF")

**Decision: in the BFF, at `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionFileBlobStore.cs`.**

Against the [`bff-extensions.md` §A pre-merge checklist](../../../.claude/constraints/bff-extensions.md):

| Criterion | Answer |
|---|---|
| **A.1 — Could this live OUTSIDE the BFF?** | No. The write is **inline in the OBO request scope** of `POST /api/ai/chat/sessions/{id}/documents`: the bytes exist only in that request (`originalBinary`), and FR-B01 requires the copy to be durable *before* the endpoint answers. An Azure Function (ADR-001's out-of-band home) would require either shipping the bytes through a job payload — forbidden by **ADR-015** ("MUST NOT place document bytes in Service Bus job payloads", ADR-004) — or re-reading them from the 4h Redis cache that is itself the thing we do not trust. A separate deployable fails all four refined-ADR-013 exception criteria (no independent scaling need, no separate release cadence, no team boundary, no isolation requirement). |
| **A.2 — Which ADRs bind?** | ADR-014 + ADR-015 (tenant partitioning, retention, no content in logs), ADR-010 (DI minimalism — one registration, concrete type, no new interface), ADR-032 (symmetric/unconditional registration), ADR-013 (lives under `Services/Ai/`, no CRUD→AI dependency introduced), ADR-007 (this is explicitly **not** SPE), ADR-038 (KEEP-path tests). |
| **A.3 — Publish-size regression?** | **No package added.** `Azure.Storage.Blobs` 12.29.1 was already a direct reference with **zero** consumers in `src/` (verified by grep — the "existing consumer" at `UploadFinalizationWorker.cs:610` is a stub that returns `new MemoryStream()` and touches no blob type). Measurement in §7. |
| **A.4 — New CRUD→AI direct dependency?** | No. The store lives under `Services/Ai/Sessions/` and is consumed only by `Api/Ai/ChatDocumentEndpoints.cs`, which is already AI-side. No `PublicContracts/` facade is needed because no CRUD code crosses into it. |
| **A.5 — Feature-module DI convention?** | Yes — registered inside `AddAiPersistenceModule` (the existing AI-persistence composition root that already owns `CosmosClient`, `ISessionPersistenceService`, `IMemoryItemStore`). No new module, no `Program.cs` line. |
| **A.6 — New config field on the playbook surface?** | N/A — §G governs Action/Node/Playbook config homes. This adds two **app-configuration** keys (`SessionFileStore:BlobEndpoint`, `SessionFileStore:ContainerName`), not Dataverse columns. |

### Why blob and not the alternatives

| Candidate | Verdict |
|---|---|
| **Cosmos** | Rejected. Cosmos holds JSON documents, not bytes; the `sessions` container is Tier-3 work history with a document-size ceiling. (Spec §11 records the owner's 7-container check.) |
| **SPE (`SpeFileStore`, ADR-007)** | Rejected — explicitly. SPE is the matter/BU-scoped DMS. Per-user chat scratch would inherit its permission model (container/drive ACLs keyed to matters, not to a chat session), its retention model, and its writer-identity rule (`.claude/patterns/auth/spe-writer-identity-matching.md`). It would also pollute the DMS with ephemeral uploads users never filed. |
| **Keep it in Redis, longer TTL** | Rejected. Redis is the hot tier (ADR-009); a 90-day binary TTL turns a cache into a system of record and multiplies memory cost by the full upload corpus. |
| **Blob (chosen)** | Storage account, containers and role assignment already defined in `infrastructure/bicep/modules/storage-account.bicep`; managed-identity auth; per-blob cost ~0; natural home for opaque bytes. |

---

## 2. §11 three-question gate — the ONE new component

**Component**: `SessionFileBlobStore` (+ its internal `SessionFileBlobGateway` collaborator).

1. **Existing — what does this overlap with?**
   Nothing that is wired. Verified by grep, not assumed:
   - `grep -rn "BlobContainerClient|BlobServiceClient|Azure.Storage.Blobs" src/server/ --include=*.cs` → **one hit**, and it is the package reference at `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj:100`. **There is no blob code in the BFF at all.**
   - `src/server/api/Sprk.Bff.Api/Workers/Office/UploadFinalizationWorker.cs:600-616` (`RetrieveTempFileAsync`) is the GH #231 stub. It is a **read** stub on the Office-add-in → temp-blob → SPE finalization path: a different direction (read, not write), a different lifecycle (transient hand-off, deleted after SPE upload), a different identity (MI background worker, no `tid` claim — it derives a tenant from `TENANT_ID` config, `:584`), and a different destination (SPE). Completing it would not produce a durable session-file copy, and forcing session uploads through it would drag chat scratch into the SPE finalization path — the placement this task explicitly rejects. **Escalation trigger 2 is therefore answered: the stub is NOT the intended seam.** (It remains open as GH #231.)
   - `SpeFileStore` — the DMS facade; see §1.
   - `ITenantCache` / Redis binary (`doc-upload-binary`, 4h TTL) — the hot tier this store is the durable counterpart to, **not** a replacement. Both still run.

2. **Extension — can I extend an existing component instead?**
   No, and the two candidates fail for concrete reasons rather than taste. `UploadFinalizationWorker` is a background worker, not a store, and its blob interaction is a transient read (above). `SpeFileStore` is `ADR-007`'s DMS facade whose every operation is drive/container-scoped — a session-scoped, tenant-partitioned byte path has no drive to resolve. The store is 1 file, 1 registration, ~150 lines of logic.

3. **Cost-of-doing-nothing — what concretely fails?**
   A conversation reopened after ~24h cannot recall from its own uploaded files. Concretely: `StoredSession.UploadedFiles[].SearchDocumentIdsCsv` (Cosmos, 90 days) keeps pointing at `spaarke-session-files` chunk ids that `SessionFilesCleanupJob` has already deleted (it sweeps on Redis-key absence; the session Redis key has a 24h sliding TTL), and the original bytes were only ever in Redis under `doc-upload-binary` with a **4h** TTL (`ChatDocumentEndpoints.UploadDocumentTtl`). Both recall and the R7 re-attach chip degrade to "no longer available" while the conversation is still in History. That is the R7 UAT point 1b defect verbatim, not a hypothetical.

---

## 3. What was verified rather than assumed

The task brief asked for verification of the POML's premises. Results:

| POML claim | Verified? | Evidence |
|---|---|---|
| Blob infra already provisioned in `storage-account.bicep`, wired at `customer.bicep:145` | ✅ **True** | Module + wiring read in full. |
| Provisioned containers fit | ⚠️ **Partly** — see §4 | Actual containers are `temp-files`, `document-processing`, `ai-chunks` (`customer.bicep:45`; `stacks/model1-shared.bicep:139-142` adds `customer-exports`; `stacks/model2-full.bicep:153`). There is **no** `session-files` container. |
| "with managed-identity RBAC" | ❌ **NOT true for two of the three stacks** — see §5 | `storage-account.bicep:150-160` creates the `Storage Blob Data Contributor` assignment only `if (!empty(appServicePrincipalId))`. **`customer.bicep:145-155` does not pass that param. `stacks/model1-shared.bicep:131-143` does not pass it either.** Only `stacks/model2-full.bicep:146-158` does. |
| `Azure.Storage.Blobs` 12.29.1 already referenced | ✅ **True** | `Sprk.Bff.Api.csproj:100`. |
| `UploadFinalizationWorker.cs:610` is its only consumer, and it is a stub | ⚠️ **Half true** | It is a stub (GH #231), but it is **not a consumer at all** — it references no blob type. The package had zero consumers. |
| ADR paths `.claude/adr/ADR-014-tenant-isolation.md` / `ADR-015-data-partitioning.md` | ❌ **Do not exist** | The real files are `ADR-014-ai-caching.md` and `ADR-015-ai-data-governance.md`. Both nevertheless carry the binding rules cited (ADR-014: "MUST scope keys by tenant"; ADR-015: "MUST scope all persisted AI artifacts by tenant", "MUST partition all Tier 2/3 data by `tenantId`"). Read and applied. The POML's `<knowledge><files>` paths are stale and should be corrected. |

---

## 4. Container decision (escalation trigger 1 — surfaced, not escalated-and-stopped)

**Chosen: `ai-chunks`, overridable via `SessionFileStore:ContainerName`. No bicep change made. The store never creates a container.**

Reasoning:
- `ai-chunks` is provisioned in **all three** deployment stacks, is in the **AI domain**, and has **zero** existing consumers in code or docs (`grep -rn "ai-chunks" src/ docs/ .claude/` → only bicep). No collision risk.
- Its only lifecycle rule (`storage-account.bicep:120-135`, tier-to-Cool after 30 days) is **non-destructive** and cost-favourable for content read rarely after 30 days. That rule is gated on `enableTestDocumentLifecycle`, which `customer.bicep:153` sets to **`false`**, so in customer deployments no lifecycle policy exists at all — nothing will delete these blobs.
- `temp-files` and `document-processing` were rejected on naming: both promise a transient lifecycle that this content must **not** have, and a future operator adding a cleanup rule to a container called `temp-files` would silently destroy 90-day session content.

**Owner decision left open (non-blocking):** a dedicated `session-files` container would read better than `ai-chunks`. That is one array entry in `customer.bicep:45` + the two stack files, plus setting `SessionFileStore:ContainerName`. It is deliberately **not** assumed here, per the task's "if a new container is genuinely required, that is a bicep change to justify, not to assume".

---

## 5. 🔔 Operator action required before this works in a deployed environment

Code-complete ≠ working. Two configuration/infra facts, both **owner decisions**, both outside this task's "no new Azure resource" boundary:

1. **App setting `SessionFileStore:BlobEndpoint` is not set anywhere.** No bicep file emits a storage app-setting today (`grep -i storage infrastructure/bicep/modules/app-service*.bicep` → nothing). Until it is set to the storage account's blob endpoint (bicep output `storagePrimaryEndpoint`, e.g. `https://sprk<customer><env>sa.blob.core.windows.net`), the store runs **disabled**: uploads still succeed, and the store logs a warning once per process. That is a deliberate fail-soft so nothing regresses — but it also means **the feature is inert until someone sets the key.**

2. **The Storage Blob Data Contributor role assignment is missing in `customer.bicep` and `stacks/model1-shared.bicep`** (see §3). Even with the endpoint set, writes will 403 in those stacks. Additionally, `model2-full.bicep` grants the role to `bffApi.outputs.appServicePrincipalId` (the **system-assigned** identity), while the BFF's `TokenCredential` is pinned to the **UAMI** in `Graph:ManagedIdentity:ClientId` when configured (`ManagedIdentityCredentialFactory.Create`). If a UAMI is pinned, the grant must be on the **UAMI's** principal id, not the system-assigned one.

Neither was changed here: both are bicep edits and role assignments, i.e. infrastructure decisions.

---

## 6. Verification record — every test was observed to FAIL before it passed

A green suite is not evidence. Each assertion below was watched failing against a deliberately broken implementation first.

### 6a. Tenant isolation — `tests/integration/tenant/Ai/SessionFileBlobStoreTenantIsolationTests.cs`

**Break applied**: the tenant segment was removed from `SessionFileBlobStore.BuildBlobName` (producing `session-files/{sessionId}/{fileId}`) and the `AssertTenantPartitioned` guard was disabled — i.e. exactly the careless-refactor shape this guard exists to catch.

```
Failed!  - Failed: 4, Passed: 15, Skipped: 0, Total: 19
  Write_PlacesTheBlobUnderTheCallingTenantsPrefix [FAIL]
  Read_FromAnotherTenant_WithTheSameSessionAndFileIds_MustNotReturnTheBytes [FAIL]
  TwoTenants_UsingIdenticalSessionAndFileIds_GetIndependentBytes [FAIL]
  Read_IsCaseSensitiveOnTheTenantSegment [FAIL]
```

with the decisive one reading:

```
Expected crossTenantRead to be <null> because a tenant must not be able to read another tenant's
session-file bytes by knowing the session id and file id (ADR-014 / ADR-015 ...), but found
SessionFileBytes { Content = PRIVILEGED — tenant A settlement figures. Must never reach tenant B.,
                   ContentType = "application/pdf" }
```

That is a real cross-tenant read, not a string mismatch. It is real because `InMemorySessionFileBlobGateway` resolves blob names the way Azure Blob does — opaque, ordinal, exact-match, **no path semantics** — so whether the read hits is decided entirely by the name the production code composed. Restoring the tenant segment turned all 19 green.

### 6b. Upload seam — `tests/integration/seam/Ai/SessionDurableFileStoreSeamTests.cs`

**Break applied**: step 9c's `durableFileStore.WriteAsync(...)` call was replaced with a stub returning `Written` — the pre-task-060 world, where the upload still 202s, still writes Redis, still indexes and still updates the manifest.

```
Failed!  - Failed: 4, Passed: 1, Skipped: 0, Total: 5
  Upload_WritesADurableByteCopyAtUploadTime [FAIL]
  Upload_StillWritesTheSessionCacheAndManifest_AlongsideTheDurableCopy [FAIL]
  TwoTenantsUploadingToTheSameSessionId_ProduceSeparateDurableCopies [FAIL]
  Upload_DurableCopyIsReadableByTheUploadingTenant [FAIL]
```

**Honest note**: `Upload_DurableCopy_IsNotReadableByAnotherTenant` **passed** under the break — vacuously, because nothing had been written. A cross-tenant-read assertion is not self-sufficient; it only means something alongside a positive control that proves the bytes were there to leak. That is why the positive controls (`..._IsReadableByTheUploadingTenant`, `TwoTenants...`) are in the same file, and why the tenant suite peeks the literal blob name to prove the write survived the missed read.

### 6c. A real bug found and fixed during review — the `$` anchor

The segment validator was first written as `^[A-Za-z0-9][A-Za-z0-9._-]*$`. **In .NET, `$` also matches immediately before a trailing `\n`**, so `"11111111-…-555555555555\n"` satisfied the pattern and a newline would have reached the blob name (and any header, URL or log line derived from it). Observed:

```
Write_RejectsSessionIdsThatCouldEscapeTheTenantPrefix(craftedSessionId:
  "11111111-2222-3333-4444-555555555555\n") [FAIL]
Failed! - Failed: 1, Passed: 10, Skipped: 0, Total: 11
```

Fixed by anchoring `\A…\z` (the true end-of-string anchor). The trailing-`\n` and `\r\n` cases are now permanent `[InlineData]` rows so re-introducing `^…$` breaks the build's tests, not production. Note `\r\n` was rejected even under `$` — only the bare-`\n` case was reachable, which is exactly why it needed an explicit case rather than reasoning.

---

## 7. Gate results

| Gate | Result |
|---|---|
| Baseline suite (before any change, this worktree) | **10,615 passed / 0 failed / 97 skipped** (`master @ 845b4cdc9`) |
| Suite after change | **10,653 passed / 0 failed / 97 skipped** — +38 tests, zero regressions |
| New DI registrations | **+1** (`SessionFileBlobStore`), unconditional. Repeatable metric `grep -rEn "services\.(AddSingleton\|AddScoped\|AddTransient\|AddHostedService)" src/server/api/Sprk.Bff.Api/ --include=*.cs \| wc -l`: **568 → 569**. (This counts every branch of every feature gate, so it is not the same instrument as ADR-010's "265 at Phase-3 baseline" — it is used here only for the before/after delta.) ADR-010's "≤15 non-framework lines" principle is a long-standing, documented, accepted violation (ADR-010 "Phase 5 baseline (2026-05-26)"); this task adds exactly the one registration the spec §11 table budgets for. |
| ArchTests (`tests/Spaarke.ArchTests/`, incl. `ADR010_DITests`, `ADR013_AiBoundaryTests`, `ADR007_GraphIsolationTests`) | **56 passed / 0 failed** |
| Full solution build | succeeded; zero warnings attributable to the new/changed files |
| New NuGet packages | **0** |
| New Azure resources | **0** |
| New secrets in configuration | **0** — the endpoint is a bare URI and a secret-bearing value is refused at construction |
| Publish size (compressed, incl. PDBs, `Compress-Archive -CompressionLevel Optimal`) | **44.99 MB** — 215 files, 4 `.pdb`, framework-dependent; **+0.03 MB vs the 44.96 MB baseline**; 15.01 MB under the 60 MB ceiling; far below the +5 MB escalation threshold |
| `dotnet list package --vulnerable --include-transitive` | **no vulnerable packages** |

> **Note on the suite baseline**: the task brief cited 11,179 passing. That is the count on `work/spaarkeai-compose-r8`. This worktree was created from **master @ `845b4cdc9`**, where the count is 10,615 — the difference is the R8 branch's own added tests, not a regression. The delta that matters here is 10,615 → 10,653 with 0 failures.

---

## 8. Deliberate non-goals (owned by later Track B tasks)

- **No delete/erase API.** Task 061 must make `SessionFilesCleanupJob` evict the hot index only; the strongest form of that guarantee is that the job has **no reachable code path to durable bytes**. `SessionFileBlobStore` therefore exposes only `WriteAsync` and `ReadAsync`. Erasure is task 063 and will add the delete surface deliberately.
- **No lazy re-index.** Task 061. `ReadAsync` is the seam it will consume.
- **No retention/lifecycle policy, no `contentAvailable` signal.** Task 062 (90-day default for unfiled, indefinite for filed `StoredSession.Ttl == -1`).

## 9. Known weakness noted, not fixed here

`ChatDocumentEndpoints` resolves the tenant as `tid` claim → schema-URI claim → **`X-Tenant-Id` request header** (`:274-276`, `:744-746`, `:833-835`, `:1043-1045`). The header fallback is spoofable unless stripped at the edge, and it is the input to the durable store's partition key exactly as it already is to the Redis key and the AI-Search filter. This is **pre-existing** and affects three stores equally, so it was not changed under a task scoped to adding one. `SummarizeSessionEndpoint.cs:215-216` deliberately omits the header fallback and is the better shape. Worth a follow-up issue.
